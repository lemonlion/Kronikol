using System.Diagnostics;
using System.Reflection;
using System.Text;
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

    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Kronikol", "plantuml-js");

    private static bool _initialized;
    private static readonly object InitLock = new();

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

    private static string RenderSvg(string plantUml)
    {
        var renderScriptPath = Path.Combine(CacheDir, RenderScriptName);
        var vizPath = Path.Combine(CacheDir, VizFileName);
        var plantumlJsPath = Path.Combine(CacheDir, PlantUmlFileName);

        var psi = new ProcessStartInfo
        {
            FileName = "node",
            Arguments = $"\"{renderScriptPath}\" \"{vizPath}\" \"{plantumlJsPath}\"",
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

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start Node.js process. Ensure 'node' is available on PATH.");

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

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Node.js PlantUML render failed (exit code {process.ExitCode}): {error}");

        if (string.IsNullOrWhiteSpace(svg) || !svg.Contains("<svg", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Node.js PlantUML render produced no SVG output. stderr: {error}");

        return svg;
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
