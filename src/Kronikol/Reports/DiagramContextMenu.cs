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

    public static string GetPlantUmlBrowserRenderScript() =>
        LoadResource("plantuml-browser-render-script.js").Replace("__PLANTUML_CDN_BASE__", PlantUmlJsCdnBase);

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