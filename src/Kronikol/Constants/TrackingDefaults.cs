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
    /// CDN base URL for the PlantUML JavaScript renderer used in HTML reports. The build is a
    /// <em>stock</em> ES-module engine (plantuml/plantuml master <c>0e4f452e</c>, 1.2026.8beta1 —
    /// carries the Teoz/Smetana performance work, so it renders Kronikol-shaped sequence diagrams
    /// 4–8× faster than the previous <c>v1.2026.6-patched</c> pin). Unlike the older tags nothing is
    /// patched: the engine's default 8192px size limit is raised at render time instead — every
    /// ES-module render call site passes <c>{ maxSvgSize: 98304 }</c> (pinned by unit tests on the
    /// scripts and an end-to-end render past the stock default). Every consumer (the browser shim,
    /// the worker host, <c>plantuml-render.js</c>) rewrites the engine's trailing <c>export</c>
    /// statement into an assignment, since Workers and <c>vm</c> contexts cannot evaluate ES modules.
    /// If this is ever pointed at a host that does not send CORS headers, the browser falls back to
    /// script-tag loading on the main thread (see
    /// <see cref="ReportConfigurationOptions.BrowserRenderWorkers"/>).
    /// </summary>
    public const string PlantUmlJsCdnBase = "https://cdn.jsdelivr.net/gh/lemonlion/plantuml-js-plantuml_limit_size_98304@v1.2026.8beta1-0e4f452";

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
