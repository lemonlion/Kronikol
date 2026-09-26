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
    /// CDN base URL for the PlantUML JavaScript renderer used in HTML reports: the published
    /// <c>@plantuml/core</c> package at its release version 1.2026.8, on jsDelivr's <c>/npm/</c> route. The
    /// registry never lets a published name@version hold other bytes, so nobody can change what this URL
    /// serves. It is a stock ES-module build of PlantUML's release commit <c>149874a1</c>, nine commits past
    /// the <c>0e4f452e</c> build that was pinned before it, none of them reachable from a Kronikol diagram, and
    /// it draws every diagram Kronikol emits byte-identical to that build (plans/ENGINE_PIN_PLAN.md §1.3,
    /// §1.16). The engine's default 8192px size limit is raised at render time: every ES-module render call
    /// site passes <c>{ maxSvgSize: 98304 }</c>. Every consumer (the browser shim, the worker host,
    /// <c>plantuml-render.js</c>) rewrites the engine's trailing <c>export</c> statement into an assignment,
    /// since Workers and <c>vm</c> contexts cannot evaluate ES modules. The previous engine builds stay
    /// published on the <c>lemonlion/plantuml-js-plantuml_limit_size_98304</c> fork's tags, for the reports
    /// that reference them.
    /// <para>
    /// A <c>const</c> is compiled into the code that reads it: a consumer that copied this value into its
    /// own assembly keeps the old URL, which still works, until it is rebuilt.
    /// </para>
    /// </summary>
    public const string PlantUmlJsCdnBase = "https://cdn.jsdelivr.net/npm/@plantuml/core@1.2026.8";

    /// <summary>
    /// The Subresource Integrity value of <c>plantuml.js</c> under <see cref="PlantUmlJsCdnBase"/>: the base64 SHA-256
    /// of the file's bytes, as jsDelivr lists it and as the registry tarball's file hashes (plans/ENGINE_PIN_PLAN.md
    /// §1.1, §1.13). The report page hands it to the browser's own integrity check, and the Node renderer checks its
    /// cached copy against it. It moves with the CDN base; the fork build's values are in the plan's A.3.
    /// </summary>
    internal const string PlantUmlJsIntegrity = "sha256-rejxXtfyoyJYFDMtOsbQW8fr277IYwUSzHz2eLwMTVI=";

    /// <summary>The Subresource Integrity value of <c>viz-global.js</c> under <see cref="PlantUmlJsCdnBase"/> (see <see cref="PlantUmlJsIntegrity"/>).</summary>
    internal const string VizGlobalJsIntegrity = "sha256-/Gyi3oPdTj/Kln1SFBInd4kKXNGrEBEVofcW/ITm3F4=";

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
