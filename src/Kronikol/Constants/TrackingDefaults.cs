namespace Kronikol.Constants;

/// <summary>
/// Default values shared across all tracking options classes.
/// </summary>
public static class TrackingDefaults
{
    /// <summary>
    /// Default display name for the calling service in diagrams when no explicit name is configured.
    /// Used as the default value of <c>CallerName</c> on all tracking options classes.
    /// </summary>
    public const string CallerName = "Caller";

    /// <summary>
    /// CDN base URL for the PlantUML JavaScript renderer used in HTML reports.
    /// </summary>
    public const string PlantUmlJsCdnBase = "https://cdn.jsdelivr.net/gh/lemonlion/plantuml-js-plantuml_limit_size_98304@v1.2026.3beta6-patched";

    /// <summary>
    /// Default number of Web Workers a <c>BrowserJs</c> report renders diagrams on (capped by the
    /// viewer's <c>navigator.hardwareConcurrency</c>). <c>0</c> renders on the main thread, the pre-3.0.45 path.
    /// See <see cref="ReportConfigurationOptions.BrowserRenderWorkers"/>.
    /// </summary>
    public const int BrowserRenderWorkers = 4;

    /// <summary>Default byte bound (in MB) of the per-page rendered-SVG cache. See <see cref="ReportConfigurationOptions.BrowserRenderCacheMegabytes"/>.</summary>
    public const int BrowserRenderCacheMegabytes = 64;

    /// <summary>Default estimated-height (px) at which the browser renderer splits one diagram into fragments. See <see cref="ReportConfigurationOptions.BrowserFragmentMaxHeight"/>.</summary>
    public const int BrowserFragmentMaxHeight = 12000;
}
