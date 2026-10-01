using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Kronikol.Constants;

namespace Kronikol.PlantUml;

/// <summary>
/// Renders PlantUML diagrams locally using a bundled Node.js PlantUML renderer.
/// Downloads the required JavaScript files on first use and caches them locally, checked against their known hashes.
/// </summary>
public static class NodeJsPlantUmlRenderer
{
    private const string CdnBase = TrackingDefaults.PlantUmlJsCdnBase;
    private const string VizFileName = "viz-global.js";
    private const string PlantUmlFileName = "plantuml.js";
    private const string RenderScriptName = "plantuml-render.js";

    /// <summary>The V8 code cache <c>plantuml-render.js</c> keeps next to the downloaded engine (see <see cref="CodeCachePath"/>).</summary>
    public const string CodeCacheFileName = PlantUmlFileName + ".v8cache";

    // The cache directory carries the engine's version, so machines that run two Kronikol versions on different
    // engines keep both instead of replacing one with the other on every run. Every Kronikol version on one engine
    // shares the directory, which is why the render script is written under a name of its own bytes.
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Kronikol", "plantuml-js", CdnVersionSegment());

    private static string CdnVersionSegment()
    {
        var last = CdnBase.TrimEnd('/').Split('/')[^1];
        var at = last.IndexOf('@');
        var segment = at >= 0 && at < last.Length - 1 ? last[(at + 1)..] : last;
        foreach (var c in Path.GetInvalidFileNameChars())
            segment = segment.Replace(c, '_');
        return segment;
    }

    /// <summary>Where the engine's V8 code cache lives; delete it to force a cold compile (it is rebuilt on the next render).</summary>
    public static string CodeCachePath => Path.Combine(CacheDir, CodeCacheFileName);

    /// <summary>
    /// What the last node process reported about the engine's V8 code cache: <c>hit</c> (reused),
    /// <c>miss</c> (none yet, written now), <c>rejected</c> (rebuilt now: the file's SHA-256 did not match its
    /// data, or V8 refused it, as it does after a node upgrade or for a source of another length; V8 never
    /// compares the source's bytes, which is why the engine's replacement deletes the cache), or <c>null</c>
    /// when nothing has run. Diagnostics only.
    /// </summary>
    public static string? LastCodeCacheStatus { get; private set; }

    // The machine's engine files, checked once per process and again whenever one has gone (EngineCache.Ready). Lazy, so
    // the fields it reads are set before it is built.
    private static readonly Lazy<EngineCache> MachineEngine = new(() => new EngineCache(CacheDir, Download, CdnBase, ExpectedIntegrity));

    private static byte[] Download(string url)
    {
        using var http = new HttpClient();
        return http.GetByteArrayAsync(url).GetAwaiter().GetResult();
    }

    /// <summary>One diagram's outcome from <see cref="RenderMany(IReadOnlyList{string})"/>: the SVG, or the engine's error for that diagram alone.</summary>
    public sealed record NodeRenderResult(string? Svg, string? Error)
    {
        public bool Succeeded => Svg is not null;
    }

    public static byte[] Render(string plantUml, PlantUmlImageFormat format)
    {
        if (format is not (PlantUmlImageFormat.Svg or PlantUmlImageFormat.Base64Svg))
            throw new InvalidOperationException(
                $"NodeJs rendering only supports SVG output. Got: {format}");

        var svg = RenderSvg(plantUml);
        var svgBytes = Encoding.UTF8.GetBytes(svg);

        return format == PlantUmlImageFormat.Base64Svg
            ? Encoding.UTF8.GetBytes(Convert.ToBase64String(svgBytes))
            : svgBytes;
    }

    /// <summary>
    /// Renders every diagram through <em>one</em> node process (NDJSON in, NDJSON out) and returns one
    /// result per input, in input order. Node start, engine compile and warm-up are paid once per call
    /// instead of once per diagram; a diagram the engine refuses (a syntax error, a diagram too large to
    /// draw, markup asking for a bundle the renderer cannot load, such as an OpenIconic icon) gets its own
    /// <see cref="NodeRenderResult.Error"/>. The diagrams render one after another in the same process, so
    /// a render that never finished would hold every later one until its own timeout: that is why the
    /// renderer answers the engine's script loads with an error at once instead of waiting for them.
    /// Throws only when the process itself cannot run (no <c>node</c> on PATH, engine download failure, a
    /// crash before any output).
    /// </summary>
    public static IReadOnlyList<NodeRenderResult> RenderMany(IReadOnlyList<string> plantUmls) => RenderMany(plantUmls, MachineEngine.Value);

    /// <summary><see cref="RenderMany(IReadOnlyList{string})"/> on the engine files <paramref name="engine"/> keeps.</summary>
    internal static IReadOnlyList<NodeRenderResult> RenderMany(IReadOnlyList<string> plantUmls, EngineCache engine)
    {
        if (plantUmls.Count == 0) return [];

        using var process = StartNode(engine, batch: true);
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
        using var process = StartNode(MachineEngine.Value, batch: false);

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

    private static readonly Lazy<byte[]> RenderScript = new(ReadRenderScript);

    /// <summary>
    /// The render script of this build: <c>plantuml-render.&lt;hash&gt;.js</c> in the cache directory, named for its own
    /// bytes, so each Kronikol version runs its own script and none rewrites a file another process is running.
    /// </summary>
    internal static string RenderScriptPath => Path.Combine(CacheDir, EngineCache.ScriptFileName(RenderScriptName, RenderScript.Value));

    private static Process StartNode(EngineCache engine, bool batch) =>
        Process.Start(NodeStartInfo(engine, batch))
            ?? throw new InvalidOperationException("Failed to start Node.js process. Ensure 'node' is available on PATH.");

    /// <summary>
    /// Node started on this build's render script in <paramref name="engine"/>'s directory, once the script and the engine
    /// files are there and verified (<see cref="EngineCache.Ready"/>).
    /// </summary>
    internal static ProcessStartInfo NodeStartInfo(EngineCache engine, bool batch)
    {
        var renderScriptPath = engine.Ready(RenderScriptName, RenderScript.Value);
        var vizPath = Path.Combine(engine.DirectoryPath, VizFileName);
        var plantumlJsPath = Path.Combine(engine.DirectoryPath, PlantUmlFileName);

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
        return psi;
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

    private static byte[] ReadRenderScript()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("plantuml-render.js", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Embedded resource plantuml-render.js not found.");

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }

    /// <summary>The known hash of each engine file under <see cref="CdnBase"/> (plans/ENGINE_PIN_PLAN.md S3).</summary>
    internal static readonly IReadOnlyDictionary<string, string> ExpectedIntegrity = new Dictionary<string, string>
    {
        [VizFileName] = TrackingDefaults.VizGlobalJsIntegrity,
        [PlantUmlFileName] = TrackingDefaults.PlantUmlJsIntegrity,
    };
}
