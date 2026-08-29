using Kronikol.ComponentDiagram;
using Kronikol.Reports;

namespace Kronikol;

/// <summary>
/// Configuration options for generating test reports with sequence diagrams.
/// </summary>
public record ReportConfigurationOptions
{
    /// <summary>Options for C4-style component diagram generation. <c>null</c> uses defaults.</summary>
    public ComponentDiagramOptions? ComponentDiagramOptions { get; set; }

    /// <summary>Base URL of the PlantUML server used for diagram rendering. Default: <c>"https://plantuml.com/plantuml"</c>.</summary>
    public string PlantUmlServerBaseUrl { get; set; } = "https://plantuml.com/plantuml";

    /// <summary>Optional post-processor applied to request/response content after all other processing.</summary>
    public Func<string, string>? RequestResponsePostProcessor { get; set; }

    /// <summary>Optional mid-processor applied to request/response content during processing.</summary>
    public Func<string, string>? RequestResponseMidProcessor { get; set; }

    /// <summary>Title displayed at the top of the test run report. When set, overrides the default title derived from <see cref="ComponentDiagram.ComponentDiagramOptions.Title"/> or <see cref="FixedNameForReceivingService"/>. Default: <c>null</c> (auto-derived).</summary>
    public string? TestRunReportTitle { get; set; }

    /// <summary>Title displayed at the top of the specifications report. Default: <c>"Service Specifications"</c>.</summary>
    public string SpecificationsTitle { get; set; } = "Service Specifications";

    /// <summary>File name (without extension) for the HTML specifications report. Default: <c>"Specifications"</c>.</summary>
    public string HtmlSpecificationsFileName { get; set; } = "Specifications";

    /// <summary>File name (without extension) for the HTML test run report. Default: <c>"TestRunReport"</c>.</summary>
    public string HtmlTestRunReportFileName { get; set; } = "TestRunReport";

    /// <summary>Custom CSS stylesheet for the HTML specifications report. Default: violet theme.</summary>
    public string? HtmlSpecificationsCustomStyleSheet { get; set; } = Stylesheets.VioletThemeStyleSheet;

    /// <summary>File name (without extension) for the YAML specifications data file. Default: <c>"Specifications"</c>.</summary>
    public string YamlSpecificationsFileName { get; set; } = "Specifications";

    /// <summary>
    /// Folder where reports are written. A relative path is resolved against the test output directory
    /// (<c>AppDomain.CurrentDomain.BaseDirectory</c>); an absolute path is used as-is. Honoured by every
    /// file the standard pipeline emits (HTML, data, schema, component diagram, CI summary, diagnostic
    /// report, copied attachments). Default: <c>"Reports"</c>.
    /// </summary>
    public string ReportsFolderPath { get; set; } = "Reports";

    /// <summary>HTTP headers to exclude from diagram annotations. Default: empty.</summary>
    public string[] ExcludedHeaders { get; set; } = [];

    /// <summary>When <c>true</c>, setup/teardown steps are displayed in a separate section from the main scenario.</summary>
    public bool SeparateSetup { get; set; }

    /// <summary>When <c>true</c>, setup/teardown steps are visually highlighted. Default: <c>true</c>.</summary>
    public bool HighlightSetup { get; set; } = true;

    /// <summary>Background color for the setup partition when <see cref="HighlightSetup"/> is <c>true</c>. Default: <c>"#F6F6F6"</c>.</summary>
    public string SetupHighlightColor { get; set; } = "#F6F6F6";

    /// <summary>When <c>true</c>, diagram images use lazy loading for better page performance. Default: <c>true</c>.</summary>
    public bool LazyLoadDiagramImages { get; set; } = true;

    /// <summary>Visual emphasis style applied to the focused participant in a sequence diagram. Default: <see cref="FocusEmphasis.Bold"/>.</summary>
    public FocusEmphasis FocusEmphasis { get; set; } = FocusEmphasis.Bold;

    /// <summary>Visual de-emphasis style applied to non-focused participants. Default: <see cref="FocusDeEmphasis.LightGray"/>.</summary>
    public FocusDeEmphasis FocusDeEmphasis { get; set; } = FocusDeEmphasis.LightGray;

    /// <summary>PlantUML theme name to apply to all diagrams (e.g. <c>"cerulean"</c>). <c>null</c> uses the default theme.</summary>
    public string? PlantUmlTheme { get; set; }

    /// <summary>Image format for PlantUML diagrams. Default: <see cref="PlantUmlImageFormat.Png"/>.</summary>
    public PlantUmlImageFormat PlantUmlImageFormat { get; set; } = PlantUmlImageFormat.Png;

    /// <summary>Optional callback for rendering PlantUML diagrams locally (e.g. via IKVM) instead of using a remote server.</summary>
    public Func<string, PlantUmlImageFormat, byte[]>? LocalDiagramRenderer { get; set; }

    /// <summary>Directory path for caching locally-rendered diagram images. <c>null</c> disables caching.</summary>
    public string? LocalDiagramImageDirectory { get; set; }

    /// <summary>Diagram notation format. Default: <see cref="DiagramFormat.PlantUml"/>.</summary>
    public DiagramFormat DiagramFormat { get; set; } = DiagramFormat.PlantUml;

    /// <summary>How PlantUML diagrams are rendered in the browser. Default: <see cref="PlantUmlRendering.BrowserJs"/>.</summary>
    public PlantUmlRendering PlantUmlRendering { get; set; } = PlantUmlRendering.BrowserJs;

    /// <summary>When <c>true</c>, SVG diagrams are inlined directly in the HTML instead of using <c>&lt;img&gt;</c> tags.</summary>
    public bool InlineSvgRendering { get; set; }

    /// <summary>
    /// <c>BrowserJs</c> only. Number of Web Workers the report page renders diagrams on, so the PlantUML
    /// engine (7 MB of JavaScript) never runs on the main thread: the page is interactive immediately,
    /// diagrams render in parallel and note/assertion/step toggles never freeze the page. The page caps
    /// this at the viewer's <c>navigator.hardwareConcurrency</c>. <c>0</c> renders on the main thread — the
    /// pre-3.0.45 behaviour (also what the page falls back to on its own when Workers, <c>fetch</c> or
    /// <c>OffscreenCanvas</c> are unavailable, or the engine cannot be fetched — e.g. offline with a
    /// cold cache). Default: <c>4</c>. See the wiki page <em>PlantUML Browser Rendering</em>.
    /// </summary>
    public int BrowserRenderWorkers { get; set; } = Constants.TrackingDefaults.BrowserRenderWorkers;

    /// <summary>
    /// <c>BrowserJs</c> only. Byte bound (in megabytes) of the page's rendered-SVG cache, keyed by fragment
    /// source. Every successful render fills it, so a toggle that re-splits a big diagram is a series of
    /// cache hits rather than re-renders; the toggle paths also pre-render their new fragments in parallel.
    /// Oldest entries are evicted first. <c>0</c> disables the cache. Default: <c>64</c>.
    /// </summary>
    public int BrowserRenderCacheMegabytes { get; set; } = Constants.TrackingDefaults.BrowserRenderCacheMegabytes;

    /// <summary>
    /// <c>BrowserJs</c> only. Estimated rendered height (px; 45 per arrow, 18 per note line) at which the
    /// browser splits one diagram into fragments rendered separately. Smaller fragments render and
    /// re-render faster (4,000–6,000 measured ~20 % faster on note-heavy reports) at the cost of more
    /// fragment seams. Default: <c>12000</c>.
    /// </summary>
    public int BrowserFragmentMaxHeight { get; set; } = Constants.TrackingDefaults.BrowserFragmentMaxHeight;

    /// <summary>When <c>true</c>, internal flow tracking data (OpenTelemetry spans) is included in reports. Default: <c>true</c>.</summary>
    public bool InternalFlowTracking { get; set; } = true;

    /// <summary>How internal flow diagrams are displayed. Default: <see cref="InternalFlowDisplay.Popup"/>.</summary>
    public InternalFlowDisplay InternalFlowDisplay { get; set; } = InternalFlowDisplay.Popup;

    /// <summary>User interaction that opens an internal flow diagram. Default: <see cref="InternalFlowTrigger.Click"/>.</summary>
    public InternalFlowTrigger InternalFlowTrigger { get; set; } = InternalFlowTrigger.Click;

    /// <summary>Diagram style for internal flow visualisation. Default: <see cref="InternalFlowDiagramStyle.ActivityDiagram"/>.</summary>
    public InternalFlowDiagramStyle InternalFlowDiagramStyle { get; set; } = InternalFlowDiagramStyle.ActivityDiagram;

    /// <summary>Granularity of spans included in internal flow diagrams. Default: <see cref="InternalFlowSpanGranularity.AutoInstrumentation"/>.</summary>
    public InternalFlowSpanGranularity InternalFlowSpanGranularity { get; set; } = InternalFlowSpanGranularity.AutoInstrumentation;

    /// <summary>Explicit list of OpenTelemetry activity source names to include. <c>null</c> includes all sources.</summary>
    public string[]? InternalFlowActivitySources { get; set; }

    /// <summary>Behaviour when no internal flow data is available for a step. Default: <see cref="InternalFlowNoDataBehavior.HideLink"/>.</summary>
    public InternalFlowNoDataBehavior InternalFlowNoDataBehavior { get; set; } = InternalFlowNoDataBehavior.HideLink;

    /// <summary>Behaviour when internal flow data is available for a step. Default: <see cref="InternalFlowHasDataBehavior.ShowLinkOnHover"/>.</summary>
    public InternalFlowHasDataBehavior InternalFlowHasDataBehavior { get; set; } = InternalFlowHasDataBehavior.ShowLinkOnHover;

    /// <summary>When <c>true</c>, flame chart visualisation is included in internal flow popups. Default: <c>true</c>.</summary>
    public bool InternalFlowShowFlameChart { get; set; } = true;

    /// <summary>Position of the flame chart relative to the activity diagram. Default: <see cref="InternalFlowFlameChartPosition.BehindWithToggle"/>.</summary>
    public InternalFlowFlameChartPosition InternalFlowFlameChartPosition { get; set; } = InternalFlowFlameChartPosition.BehindWithToggle;

    /// <summary>Strategy for including internal flow HTML content. Default: <see cref="InternalFlowContentStrategy.Embedded"/>.</summary>
    public InternalFlowContentStrategy InternalFlowContentStrategy { get; set; } = InternalFlowContentStrategy.Embedded;

    /// <summary>Folder name for external internal flow fragment files. Default: <c>"spans"</c>.</summary>
    public string InternalFlowFragmentsFolderName { get; set; } = "spans";

    /// <summary>Custom CSS stylesheet for internal flow popup windows.</summary>
    public string? InternalFlowPopupCustomStyleSheet { get; set; }

    /// <summary>Controls whole-test flow visualization mode. Default: <see cref="WholeTestFlowVisualization.Both"/>.</summary>
    public WholeTestFlowVisualization WholeTestFlowVisualization { get; set; } = WholeTestFlowVisualization.Both;

    /// <summary>When <c>true</c>, a C4-style component diagram is generated alongside reports. Default: <c>true</c>.</summary>
    public bool GenerateComponentDiagram { get; set; } = true;

    /// <summary>When <c>true</c>, the HTML specifications report is generated. Default: <c>true</c>.</summary>
    public bool GenerateSpecificationsReport { get; set; } = true;

    /// <summary>When <c>true</c>, the HTML test run report is generated. Default: <c>true</c>.</summary>
    public bool GenerateTestRunReport { get; set; } = true;

    /// <summary>When <c>true</c>, the specifications data file (YAML/JSON/XML) is generated. Default: <c>true</c>.</summary>
    public bool GenerateSpecificationsData { get; set; } = true;

    /// <summary>When <c>true</c>, the test run report data file (JSON/XML/YAML) is generated. Default: <c>true</c>.</summary>
    public bool GenerateTestRunReportData { get; set; } = true;

    /// <summary>When <c>true</c>, the test run report schema file is generated. Default: <c>true</c>.</summary>
    public bool GenerateTestRunReportSchema { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, the test run report data file (JSON) is enriched with everything required to
    /// reconstruct a full HTML report later: component relationships, precomputed internal-flow segment
    /// data, precomputed whole-test-flow fragments, and CI metadata. This enables several such files
    /// (e.g. from parallel CI runners) to be merged into a single combined <c>TestRunReport.html</c>
    /// via <c>kronikol merge</c>. The enriched file is larger than the standard report. Default: <c>false</c>.
    /// Only honoured when <see cref="GenerateTestRunReportData"/> is also <c>true</c> and the format is JSON.
    /// </summary>
    public bool GenerateMergeableData { get; set; }

    /// <summary>
    /// When <c>true</c>, every step in the test run report data file carries its full detail: parameters
    /// (inline values, data tables, tree values), text segments, doc string, comments and bypass reason.
    /// This is what makes a parameterised failure legible from the data file alone — without it the inputs
    /// that produced the failure are only in the HTML. Step detail is measured in kilobytes against
    /// megabytes of payload, so turn this off only if the file size genuinely matters. Default: <c>true</c>.
    /// </summary>
    public bool TestRunReportFullStepDetail { get; set; } = true;

    /// <summary>When <c>true</c>, writes a test summary to the CI job summary (e.g. GitHub Actions).</summary>
    public bool WriteCiSummary { get; set; }

    /// <summary>Maximum number of diagrams to include in the CI summary output. Default: <c>10</c>.</summary>
    public int MaxCiSummaryDiagrams { get; set; } = 10;

    /// <summary>When <c>true</c>, publishes report files as CI artifacts (GitHub Actions).</summary>
    public bool PublishCiArtifacts { get; set; }

    /// <summary>Name of the CI artifact containing the reports. Default: <c>"TestReports"</c>.</summary>
    public string CiArtifactName { get; set; } = "TestReports";

    /// <summary>Number of days to retain CI artifacts. Default: <c>1</c>.</summary>
    public int CiArtifactRetentionDays { get; set; } = 1;

    /// <summary>When set, all tracked requests use this name as the receiving service instead of inferring from the port.</summary>
    public string? FixedNameForReceivingService { get; set; }

    /// <summary>When <c>true</c>, step numbers are shown in the specifications report. Default: <c>true</c>.</summary>
    public bool SpecificationsShowStepNumbers { get; set; } = true;

    /// <summary>When <c>true</c>, step numbers are shown in the test run report.</summary>
    public bool TestRunReportShowStepNumbers { get; set; }

    /// <summary>Additional CSS injected into all generated HTML reports.</summary>
    public string? CustomCss { get; set; }

    /// <summary>Base64-encoded favicon to use in generated HTML reports.</summary>
    public string? CustomFaviconBase64 { get; set; }

    /// <summary>Custom HTML for a logo displayed in the report header.</summary>
    public string? CustomLogoHtml { get; set; }

    /// <summary>Data format for the test run report output. Default: <see cref="DataFormat.Json"/>.</summary>
    public DataFormat TestRunReportDataFormat { get; set; } = DataFormat.Json;

    /// <summary>Data format for the specifications data output. Default: <see cref="DataFormat.Yaml"/>.</summary>
    public DataFormat SpecificationsDataFormat { get; set; } = DataFormat.Yaml;

    /// <summary>When <c>true</c>, automatically discovers OpenTelemetry activity sources.</summary>
    public bool ActivitySourceDiscovery { get; set; }

    /// <summary>When <c>true</c>, enables diagnostic logging for troubleshooting report generation.</summary>
    public bool DiagnosticMode { get; set; }

    /// <summary>When <c>true</c>, background steps are rendered inline with the scenario steps instead of in a separate collapsible section.</summary>
    [Obsolete("Background steps are inlined by default. Set SeparateBackgroundSteps = true for the old separate section.")]
    public bool InlineBackgroundSteps { get; set; }

    /// <summary>
    /// When <c>true</c>, a scenario's background steps are rendered in their own collapsible
    /// <c>Background Steps</c> section above the scenario's own <c>Steps</c>. Default: <c>false</c> —
    /// background steps are listed inline, first, in the one <c>Steps</c> list, matching the order the
    /// data files and step paths already use (<c>b0</c>, <c>b1</c>, then <c>0</c>, <c>1</c>).
    /// Applies to both HTML reports; the data outputs keep the two collections separate either way.
    /// </summary>
    public bool SeparateBackgroundSteps { get; set; }

    /// <summary>
    /// When <c>true</c>, a step repeating the primary keyword already in force is displayed as
    /// <c>And</c> — so a background <c>Given</c> followed by a scenario <c>Given</c> reads
    /// <c>Given / And</c> rather than <c>Given / Given</c>. Default: <c>true</c>. Purely a rendering
    /// choice: the keyword the producer recorded is what every data output emits. Localised Gherkin
    /// keywords are not recognised and pass through unchanged.
    /// </summary>
    public bool CollapseRepeatedStepKeywords { get; set; } = true;

    /// <summary>When <c>true</c>, parameterized tests are grouped into a single collapsible table. Default: <c>true</c>.</summary>
    public bool GroupParameterizedTests { get; set; } = true;

    /// <summary>When <c>true</c>, sequence diagram arrows are colored by dependency type. Default: <c>true</c>.</summary>
    public bool SequenceDiagramArrowColors { get; set; } = true;

    /// <summary>When <c>true</c>, sequence diagram participant headers get colored backgrounds matching their dependency type. Default: <c>false</c>.</summary>
    public bool SequenceDiagramParticipantColors { get; set; }

    /// <summary>
    /// When <c>true</c>, maximal runs of consecutive identical calls within one test — same caller,
    /// service, method, path+query, GraphQL operation and status code — are collapsed into a single
    /// request/response pair wrapped in a PlantUML <c>loop ×N</c> fragment (with the min–max duration
    /// when timestamps are available). Keeps poll/retry-heavy traffic legible. Default: <c>false</c>.
    /// </summary>
    public bool CollapseConsecutiveIdenticalCalls { get; set; }

    /// <summary>Minimum run length that triggers collapsing when <see cref="CollapseConsecutiveIdenticalCalls"/> is on. Default: <c>2</c>.</summary>
    public int CollapseThreshold { get; set; } = 2;

    /// <summary>
    /// Maximum number of request/response pairs rendered per test's sequence diagram (counted after
    /// collapsing). The remainder is summarised as a single <c>… +N more calls omitted …</c> line.
    /// Default: <c>null</c> (unlimited).
    /// </summary>
    public int? MaxArrowsPerDiagram { get; set; }

    /// <summary>
    /// When <c>true</c>, a scenario whose id matched no tracked interaction renders an explicit
    /// "No interactions captured" marker instead of silently omitting the diagram section. Default: <c>true</c>.
    /// </summary>
    public bool ShowNoInteractionsMarker { get; set; } = true;

    /// <summary>User overrides for dependency-type colors. Keys are <see cref="Tracking.RequestResponseLog.DependencyCategory"/> strings (e.g. <c>"CosmosDB"</c>), values are hex colors (e.g. <c>"#E74C3C"</c>).</summary>
    public Dictionary<string, string>? DependencyColors { get; set; }

    /// <summary>User overrides mapping service names to dependency categories. Keys are service names, values are category strings (e.g. <c>"CosmosDB"</c>, <c>"Redis"</c>).</summary>
    public Dictionary<string, string>? ServiceTypeOverrides { get; set; }

    /// <summary>Controls how GraphQL request bodies are displayed in sequence diagram notes. Default: <see cref="GraphQlBodyFormat.FormattedWithMetadata"/>.</summary>
    public GraphQlBodyFormat GraphQlBodyFormat { get; set; } = GraphQlBodyFormat.FormattedWithMetadata;

    /// <summary>
    /// <c>BrowserJs</c> only. The initial display format for JSON note payloads in sequence diagrams.
    /// <see cref="Reports.NotePayloadFormat.Yaml"/> starts every eligible JSON payload in the derived
    /// YAML view; readers can still switch any note — or all of them via the JSON/YAML toolbar
    /// dropdowns — either way in the report itself. Default: <see cref="Reports.NotePayloadFormat.Json"/>.
    /// </summary>
    public NotePayloadFormat NotePayloadFormat { get; set; } = NotePayloadFormat.Json;

    /// <summary>Maximum number of parameter columns shown per parameterized test group. Default: <c>10</c>.</summary>
    public int MaxParameterColumns { get; set; } = 10;

    /// <summary>When <c>true</c>, parameter names are converted to title case in report tables. Default: <c>true</c>.</summary>
    public bool TitleizeParameterNames { get; set; } = true;

    /// <summary>
    /// Upper-case the first letter of every step and assertion label that carries no Gherkin keyword, so
    /// the step list reads as sentences however inconsistent the producers were. Default: <c>true</c>.
    /// See <see cref="Reports.StepText"/> for the exact rule — Gherkin steps, quoted literals and
    /// markers (<c>✓ ✗ ⚠</c>) are all handled deliberately. <c>kronikol ingest --no-capitalise</c> turns
    /// it off.
    /// </summary>
    public bool CapitaliseStepText { get; set; } = true;

    /// <summary>
    /// Upper-case the first letter of every feature, rule and scenario title (including an outline's
    /// template title), so the headings read as sentences however the producer wrote them — a Gherkin
    /// <c>Scenario: the overview renders</c> becomes <c>The overview renders</c>. Default: <c>true</c>.
    /// Same helper and same exceptions as <see cref="CapitaliseStepText"/> (a title starting with a
    /// quote, bracket, digit or symbol is left alone); <c>kronikol ingest --no-capitalise</c> turns both
    /// off. Note that a scenario's <c>stableId</c> is computed from the capitalised title, so a title
    /// this rule changes gets a new one — titles that already start with a capital are unaffected.
    /// </summary>
    public bool CapitaliseTitles { get; set; } = true;

    /// <summary>
    /// Optional delegate returning the total number of test scenarios expected in this assembly.
    /// When set, report generation is skipped if the actual scenario count is less than the expected
    /// count — preventing partial test runs (e.g. single-test filtering) from overwriting the
    /// full Specifications report.
    /// </summary>
    public Func<int>? ExpectedTestCount { get; set; }
}