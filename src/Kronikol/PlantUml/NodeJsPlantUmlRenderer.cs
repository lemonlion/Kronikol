using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Kronikol.Constants;

namespace Kronikol.PlantUml;

/// <summary>
/// Renders PlantUML diagrams locally using a bundled Node.js PlantUML renderer.
/// Downloads the required JavaScript files on first use and caches them locally.
/// </summary>
public static class NodeJsPlantUmlRenderer
{
    private const string CdnBase = TrackingDefaults.PlantUmlJsCdnBase;
    private const string VizFileName = "viz-global.js";
    private const string PlantUmlFileName = "plantuml.js";
    private const string RenderScriptName = "plantuml-render.js";

    /// <summary>The V8 code cache <c>plantuml-render.js</c> keeps next to the downloaded engine (see <see cref="CodeCachePath"/>).</summary>
    public const string CodeCacheFileName = PlantUmlFileName + ".v8cache";

    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Kronikol", "plantuml-js");

    /// <summary>Where the engine's V8 code cache lives; delete it to force a cold compile (it is rebuilt on the next render).</summary>
    public static string CodeCachePath => Path.Combine(CacheDir, CodeCacheFileName);

    /// <summary>
    /// What the last node process reported about the engine's V8 code cache: <c>hit</c> (reused),
    /// <c>miss</c> (none yet — written now), <c>rejected</c> (V8 refused it, e.g. after a node upgrade —
    /// rebuilt now), or <c>null</c> when nothing has run. Diagnostics only.
    /// </summary>
    public static string? LastCodeCacheStatus { get; private set; }

    private static bool _initialized;
    private static readonly object InitLock = new();

    /// <summary>One diagram's outcome from <see cref="RenderMany"/>: the SVG, or the engine's error for that diagram alone.</summary>
    public sealed record NodeRenderResult(string? Svg, string? Error)
    {
        public bool Succeeded => Svg is not null;
    }

    public static byte[] Render(string plantUml, PlantUmlImageFormat format)
    {
        if (format is not (PlantUmlImageFormat.Svg or PlantUmlImageFormat.Base64Svg))
            throw new InvalidOperationException(
                $"NodeJs rendering only supports SVG output. Got: {format}");

        EnsureInitialized();

        var svg = RenderSvg(plantUml);
        var svgBytes = Encoding.UTF8.GetBytes(svg);

        return format == PlantUmlImageFormat.Base64Svg
            ? Encoding.UTF8.GetBytes(Convert.ToBase64String(svgBytes))
            : svgBytes;
    }

    /// <summary>
    /// Renders every diagram through <em>one</em> node process (NDJSON in, NDJSON out) and returns one
    /// result per input, in input order. Node start, engine compile and warm-up are paid once per call
    /// instead of once per diagram; a diagram the engine cannot render gets its own
    /// <see cref="NodeRenderResult.Error"/> and never affects the others. Throws only when the process
    /// itself cannot run (no <c>node</c> on PATH, engine download failure, a crash before any output).
    /// </summary>
    public static IReadOnlyList<NodeRenderResult> RenderMany(IReadOnlyList<string> plantUmls)
    {
        if (plantUmls.Count == 0) return [];

        EnsureInitialized();

        using var process = StartNode(batch: true);
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        var stdin = process.StandardInput;
        for (var i = 0; i < plantUmls.Count; i++)
        {
            stdin.Write(JsonSerializer.Serialize(new BatchLine(i.ToString(), plantUmls[i])));
            stdin.Write('\n');
        }
        stdin.Close();

        // The whole report in one process: a generous, count-proportional bound (the per-diagram
        // engine timeout is 20 s inside the script).
        var timeoutMs = (int)Math.Min(30 * 60_000L, 60_000L + 25_000L * plantUmls.Count);
        if (!process.WaitForExit(timeoutMs))
        {
            try { process.Kill(); } catch { /* best effort */ }
            throw new TimeoutException($"Node.js PlantUML batch render of {plantUmls.Count} diagram(s) timed out after {timeoutMs / 1000} seconds.");
        }

        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        RecordCodeCacheStatus(stderr);

        var results = new NodeRenderResult?[plantUmls.Count];
        foreach (var line in stdout.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            BatchResultLine? parsed;
            try { parsed = JsonSerializer.Deserialize<BatchResultLine>(trimmed); }
            catch (JsonException) { continue; }
            if (parsed?.Id is null || !int.TryParse(parsed.Id, out var index) || index < 0 || index >= results.Length) continue;
            results[index] = parsed.Svg is not null && parsed.Svg.Contains("<svg", StringComparison.OrdinalIgnoreCase)
                ? new NodeRenderResult(parsed.Svg, null)
                : new NodeRenderResult(null, parsed.Error ?? "Node.js PlantUML render produced no SVG output.");
        }

        if (process.ExitCode != 0 && results.All(r => r is null))
            throw new InvalidOperationException(
                $"Node.js PlantUML batch render failed (exit code {process.ExitCode}): {stderr}");

        for (var i = 0; i < results.Length; i++)
            results[i] ??= new NodeRenderResult(null,
                $"Node.js PlantUML batch render returned no result for this diagram (exit code {process.ExitCode}). {stderr}".Trim());

        return results!;
    }

    private sealed record BatchLine(string id, string source);

    private sealed class BatchResultLine
    {
        public string? id { get; set; }
        public string? svg { get; set; }
        public string? error { get; set; }
        public string? Id => id;
        public string? Svg => svg;
        public string? Error => error;
    }

    private static string RenderSvg(string plantUml)
    {
        using var process = StartNode(batch: false);

        process.StandardInput.Write(plantUml);
        process.StandardInput.Close();

        var svgTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(60_000))
        {
            try { process.Kill(); } catch { /* best effort */ }
            throw new TimeoutException("Node.js PlantUML render timed out after 60 seconds.");
        }

        var svg = svgTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();
        RecordCodeCacheStatus(error);

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Node.js PlantUML render failed (exit code {process.ExitCode}): {error}");

        if (string.IsNullOrWhiteSpace(svg) || !svg.Contains("<svg", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Node.js PlantUML render produced no SVG output. stderr: {error}");

        return svg;
    }

    private static Process StartNode(bool batch)
    {
        var renderScriptPath = Path.Combine(CacheDir, RenderScriptName);
        var vizPath = Path.Combine(CacheDir, VizFileName);
        var plantumlJsPath = Path.Combine(CacheDir, PlantUmlFileName);

        var psi = new ProcessStartInfo
        {
            FileName = "node",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            // Without this the diagram text goes to node in the console's code page (cp1252 on Windows),
            // and every non-ASCII glyph — the `×`/`·`/`–` in loop labels, accented participant names,
            // non-Latin test titles — renders as `x`, `�` or `?`.
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        psi.ArgumentList.Add(renderScriptPath);
        psi.ArgumentList.Add(vizPath);
        psi.ArgumentList.Add(plantumlJsPath);
        if (batch) psi.ArgumentList.Add("--batch");

        return Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start Node.js process. Ensure 'node' is available on PATH.");
    }

    private static void RecordCodeCacheStatus(string stderr)
    {
        const string marker = "[plantuml-render] code cache: ";
        var idx = stderr.LastIndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return;
        var rest = stderr[(idx + marker.Length)..];
        var end = rest.IndexOfAny(['\r', '\n']);
        LastCodeCacheStatus = (end >= 0 ? rest[..end] : rest).Trim();
    }

    private static void EnsureInitialized()
    {
        if (_initialized) return;

        lock (InitLock)
        {
            if (_initialized) return;

            Directory.CreateDirectory(CacheDir);
            ExtractRenderScript();
            DownloadJsFiles();
            _initialized = true;
        }
    }

    private static void ExtractRenderScript()
    {
        var targetPath = Path.Combine(CacheDir, RenderScriptName);

        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("plantuml-render.js", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Embedded resource plantuml-render.js not found.");

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var file = File.Create(targetPath);
        stream.CopyTo(file);
    }

    private static void DownloadJsFiles()
    {
        using var http = new HttpClient();
        DownloadIfMissing(http, VizFileName);
        DownloadIfMissing(http, PlantUmlFileName);
    }

    private static void DownloadIfMissing(HttpClient http, string fileName)
    {
        var targetPath = Path.Combine(CacheDir, fileName);
        if (File.Exists(targetPath)) return;

        var url = $"{CdnBase}/{fileName}";
        var bytes = http.GetByteArrayAsync(url).GetAwaiter().GetResult();
        File.WriteAllBytes(targetPath, bytes);
    }
}
