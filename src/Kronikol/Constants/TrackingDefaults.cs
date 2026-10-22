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
    /// CDN base URL for the PlantUML engine used in HTML reports: a build of PlantUML's master at <c>f44b054d1</c>
    /// (1.2026.10beta1, MIT flavor) compiled with TeaVM 0.17.0-dev-2, published under the tag of the same name on the
    /// <c>lemonlion/plantuml-js-plantuml_limit_size_98304</c> fork and served by jsDelivr's <c>/gh/</c> route (5.0.0,
    /// plans/WASM_ENGINE_PLAN.md). It is the stock <c>npmPackage</c> build but for its TeaVM version, and it holds
    /// two engines that draw the same SVG: <c>plantuml.js</c>, and PlantUML's Wasm GC engine packed into one script,
    /// <c>plantuml-wasm-bundle.js</c>. The report page draws with the Wasm one wherever the browser can run it, and
    /// the Node renderer runs <c>plantuml.js</c>. jsDelivr keeps a tag's files once it has served them, and every
    /// file is checked against its known hash (<see cref="PlantUmlJsIntegrity"/>), so a file that changed on the
    /// fork is refused rather than drawn with. The engine's default 8192px size limit is raised at render time:
    /// every render call site passes <c>{ maxSvgSize: 98304 }</c>. Every consumer (the browser shim, the worker
    /// host, <c>plantuml-render.js</c>) rewrites the engine's trailing <c>export</c> statement into an assignment,
    /// since Workers and <c>vm</c> contexts cannot evaluate ES modules. Through 4.x the base was the published
    /// <c>@plantuml/core</c> 1.2026.8 package on jsDelivr's <c>/npm/</c> route (plans/ENGINE_PIN_PLAN.md), and the
    /// builds before that stay on the fork's other tags, for the reports that reference them.
    /// <para>
    /// A <c>const</c> is compiled into the code that reads it: a consumer that copied this value into its
    /// own assembly keeps the old URL, which still works, until it is rebuilt.
    /// </para>
    /// </summary>
    public const string PlantUmlJsCdnBase = "https://cdn.jsdelivr.net/gh/lemonlion/plantuml-js-plantuml_limit_size_98304@v1.2026.10beta1-f44b054-teavm0.17.0-dev-2";

    /// <summary>
    /// The Subresource Integrity value of <c>plantuml.js</c> under <see cref="PlantUmlJsCdnBase"/>: the base64 SHA-256
    /// of the file's bytes as the fork's tag holds them (plans/ENGINE_PIN_PLAN.md §1.1, plans/WASM_ENGINE_PLAN.md).
    /// The report page hands it to the browser's own integrity check, and the Node renderer checks its cached copy
    /// against it. It moves with the CDN base.
    /// </summary>
    internal const string PlantUmlJsIntegrity = "sha256-/vtFyFB1p3iF7f2rj0da7UNXYle2np+qILBnXWqAEgg=";

    /// <summary>
    /// The Subresource Integrity value of <c>plantuml-wasm-bundle.js</c> under <see cref="PlantUmlJsCdnBase"/> (see
    /// <see cref="PlantUmlJsIntegrity"/>): PlantUML's Wasm GC engine with its runtime and its module in one script, which
    /// the report page draws with wherever the browser runs Wasm GC (5.0.0, plans/WASM_ENGINE_PLAN.md).
    /// </summary>
    internal const string PlantUmlWasmEngineIntegrity = "sha256-Cnk+sDwumDPD/bLkAL7f5dDvyXrY8CowxReQJ8AgxzE=";

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

    /// <summary>Default for the most body lines a payload note draws before it scrolls in its box. See <see cref="ReportConfigurationOptions.PayloadScrollLines"/>.</summary>
    public const int PayloadScrollLines = 40;
}
