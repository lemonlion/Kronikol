using Kronikol.Constants;

namespace Kronikol.Reports;

/// <summary>
/// Generates the context menu HTML and JavaScript for diagram interactions in the report viewer.
/// </summary>
public static class DiagramContextMenu
{

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> ResourceCache = new();

    private static string LoadResource(string name) => ResourceCache.GetOrAdd(name, n =>
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(r => r.EndsWith(n, System.StringComparison.OrdinalIgnoreCase))
            ?? throw new System.InvalidOperationException($"Embedded resource {n} not found.");
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new System.IO.StreamReader(stream);
        return reader.ReadToEnd();
    });
    public static string GetStyles() => LoadResource("context-menu-styles.css");

    public static string GetInlineSvgStyles() => LoadResource("inline-svg-styles.css");

    public static string GetCollapsibleNotesStyles() => LoadResource("collapsible-notes-styles.css");

    public static string GetInternalFlowPopupStyles() => LoadResource("internal-flow-popup-styles.css");

    public static string GetInternalFlowConfigScript(InternalFlowHasDataBehavior hasDataBehavior) =>
        $"<script>window.__iflowConfig = {{ hasDataBehavior: '{(hasDataBehavior == InternalFlowHasDataBehavior.ShowLinkOnHover ? "showLinkOnHover" : "showLink")}' }};</script>";

    private const string PlantUmlJsCdnBase = TrackingDefaults.PlantUmlJsCdnBase;

    /// <summary>
    /// The Web Worker host for the TeaVM plantuml.js engine: the minimal DOM the engine needs, an SVG
    /// serializer and the render protocol. The browser render script inlines it (JSON-escaped) into the
    /// Blob it builds each worker from, together with the engine fetched from the CDN — a worker created
    /// by a <c>file://</c> page cannot load anything over the network itself.
    /// </summary>
    public static string GetPlantUmlWorkerHostScript() => LoadResource("plantuml-worker-host.js");

    /// <summary>Browser render script with the default worker/cache/fragment settings (see <see cref="TrackingDefaults"/>).</summary>
    public static string GetPlantUmlBrowserRenderScript() =>
        GetPlantUmlBrowserRenderScript(TrackingDefaults.BrowserRenderWorkers, TrackingDefaults.BrowserRenderCacheMegabytes, TrackingDefaults.BrowserFragmentMaxHeight);

    /// <summary>
    /// Browser render script with the given settings baked in as constants (the way the CDN base is):
    /// <paramref name="browserRenderWorkers"/> Web Workers (0 = main-thread engine, the legacy path),
    /// a rendered-SVG cache of <paramref name="browserRenderCacheMegabytes"/> MB and diagram fragments of
    /// at most <paramref name="browserFragmentMaxHeight"/> estimated px. Negative values are treated as 0.
    /// </summary>
    public static string GetPlantUmlBrowserRenderScript(int browserRenderWorkers, int browserRenderCacheMegabytes, int browserFragmentMaxHeight)
    {
        var hostSource = GetPlantUmlWorkerHostScript();
        // JSON-escaping gives a JavaScript string literal; the default encoder also escapes `<`, so the
        // inlined worker source can never contain a literal `</script>`.
        var hostLiteral = System.Text.Json.JsonSerializer.Serialize(hostSource);
        return LoadResource("plantuml-browser-render-script.js")
            .Replace("__BROWSER_RENDER_WORKERS__", Math.Max(0, browserRenderWorkers).ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("__BROWSER_RENDER_CACHE_MB__", Math.Max(0, browserRenderCacheMegabytes).ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("__BROWSER_FRAGMENT_MAX_HEIGHT__", (browserFragmentMaxHeight > 0 ? browserFragmentMaxHeight : TrackingDefaults.BrowserFragmentMaxHeight).ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("__PLANTUML_CDN_BASE__", PlantUmlJsCdnBase)
            .Replace("__PLANTUML_WORKER_HOST_SOURCE__", hostLiteral);
    }

    public static string GetContextMenuScript() => LoadResource("context-menu-script.js");

    public static string GetInternalFlowPopupScript() => LoadResource("internal-flow-popup-script.js");

    public static string GetToggleScript() => LoadResource("toggle-script.js");

    /// <summary>
    /// Client-side JavaScript that renders flame charts from compact JSON data.
    /// Flame chart elements with a <c>data-flame</c> attribute or inside popups
    /// with <c>flameData</c> are rendered on demand instead of being pre-rendered
    /// as HTML on the server, dramatically reducing report file size.
    /// </summary>
    public static string GetFlameChartRenderScript() => LoadResource("flame-chart-render-script.js");

    public static string GetCollapsibleNotesScript() => LoadResource("collapsible-notes-script.js");
}