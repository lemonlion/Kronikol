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

    /// <summary>
    /// The suite this run belongs to. It scopes every <c>stableId</c>, so it is what stops two projects
    /// that both have a <c>Cake</c> feature with an <c>Order a cake</c> scenario from minting the same id
    /// and colliding when their reports are combined — by <c>kronikol merge</c>, by an ingest folding two
    /// runners, or by a cross-run ledger.
    ///
    /// <para>Default: <c>null</c>, which means Kronikol resolves it from the test assembly that produced
    /// the run (see <see cref="Reports.RunSuite"/>). Set it explicitly when the assembly name is not the
    /// identity you want carried across runs — for example when one assembly is sharded into several jobs
    /// that should stay distinguishable, or when an assembly is renamed and existing ids must survive.
    /// </para>
    ///
    /// <para>Changing this value re-keys every scenario in the run: deep links of the form
    /// <c>#sid-&lt;id&gt;</c> and any stored baseline are keyed on it.</para>
    /// </summary>
    public string? SuiteName { get; set; }

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

    /// <summary>
    /// When <c>true</c>, HTML reports embed a compact full-text search index over everything the
    /// report contains — note payloads, diagram message text, SQL, flame-chart span names and
    /// parameterized example values — so the report search box can find text the plain
    /// per-scenario search data does not cover. Adds roughly 0.25-1&#160;MB to large reports
    /// (measured ~1&#160;MB at a 147&#160;MB corpus). Default: <c>true</c>.
    /// </summary>
    public bool FullSearchIndex { get; set; } = true;

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
    ///
    /// <para><b>JSON only.</b> The XML and YAML writers emit the reduced step shape whatever this is set
    /// to — they were never handed the flag — so on those formats a step carries keyword, text, status,
    /// duration, failure message, source location, sub-steps and attachments, and nothing else. Setting
    /// this to <c>false</c> to shrink an XML or YAML file therefore changes nothing. Closing that gap
    /// means porting the parameter, tree and text-segment shapes to both writers and to the XSD; it is
    /// tracked as its own item rather than implied by this flag.</para>
    /// </summary>
    public bool TestRunReportFullStepDetail { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, the run prints one closing block naming the reports directory, the size of the
    /// data file, the failing scenarios and the <c>kronikol query failures</c> command that explains them.
    /// Paths, counts, stableIds and scenario names only — never a message, a URI or a body, because CI logs
    /// are read far more widely than artifacts. On GitHub Actions a matching <c>::notice</c> annotation
    /// follows it. Default: <c>true</c>.
    /// </summary>
    /// <remarks>
    /// The console is a best-effort channel, not a guaranteed one: measured on .NET 10, every VSTest-hosted
    /// runner under <c>dotnet test</c> swallows what a library writes from a run-end hook unless the
    /// verbosity is <c>detailed</c>, and TUnit's runner suppresses it at any verbosity. The channels that
    /// always work are the files — see <see cref="GenerateFailuresDigest"/> and
    /// <see cref="WriteAgentInstructions"/> — and the CI job summary.
    /// </remarks>
    public bool WriteRunSummaryToConsole { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, writes <c>Failures.md</c> and <c>Failures.jsonl</c> next to the report: every
    /// failure in context — error, parsed expected/actual, the failing step and its source location, the
    /// calls made inside it, attachments, and the query address of each — grouped by the first line of the error so that
    /// twenty scenarios stopped by one cause are worked through once rather than twenty times. Written on a green run too (as <c>No failures</c>),
    /// so its absence always means the run did not finish. Never contains a payload, a header or a diagram.
    /// Default: <c>true</c>.
    /// </summary>
    public bool GenerateFailuresDigest { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, writes byte-identical <c>CLAUDE.md</c> and <c>AGENTS.md</c> files into the reports
    /// directory telling an AI agent to read <c>Failures.md</c> and use <c>kronikol query</c> rather than
    /// open the report. Static text — no scenario names, no captured content — because an instruction file
    /// carrying attacker-influenceable text is a prompt-injection channel. Default: <c>true</c>.
    /// </summary>
    public bool WriteAgentInstructions { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, writes <c>ctrf-report.json</c> next to the report: the run in the Common Test
    /// Report Format, for the CI tooling that already speaks it — annotation actions, PR comment bots,
    /// flaky-test dashboards. One test per scenario with its status, duration, failure message and trace,
    /// plus the <c>sN</c> address that leads back into <c>kronikol query</c>. It carries failure messages
    /// verbatim and is swept up by <see cref="PublishCiArtifacts"/> like every other <c>.json</c> in the
    /// folder. Off by default: it is written for a consumer, and a run with no such consumer should not
    /// pay for it. Default: <c>false</c>.
    /// </summary>
    public bool GenerateCtrfReport { get; set; }

    /// <summary>
    /// When <c>true</c>, writes <c>&lt;YamlSpecificationsFileName&gt;.md</c> next to the report: the
    /// specification as prose — every feature, rule, scenario and step, with no result, no duration, no
    /// interaction and no diagram. The same suite run red and run green produces the same bytes, which is
    /// what makes it safe to commit to a docs site, and it is deliberately NOT blanked on a failed run the
    /// way <c>Specifications.html</c> and the specifications data file are: a reader who reaches for it
    /// mid-failure needs the narrative most. Suppressed by <see cref="ExpectedTestCount"/> like every
    /// other specification output. Default: <c>false</c>.
    /// </summary>
    public bool GenerateSpecificationsMarkdown { get; set; }

    /// <summary>When <c>true</c>, writes a test summary to the CI job summary (e.g. GitHub Actions).</summary>
    public bool WriteCiSummary { get; set; }

    /// <summary>
    /// When <c>true</c> (the default), a CI run with failures prints a short "Debug this run" block naming
    /// the reports directory and the two commands that explain it - to <b>stdout and, additionally, to the
    /// job summary</b>.
    ///
    /// <para><b>Why it is separate from <see cref="WriteCiSummary"/>.</b> That option generates the full
    /// <c>CiSummary.md</c>, which carries rendered diagrams and has been measured at 48&#160;KB; turning it
    /// on to get four lines of debugging advice is a different cost/benefit entirely, and merging the two
    /// behind one flag is what left a failing CI job saying nothing about how to debug itself. This is a
    /// new option defaulting to on rather than a change to either existing default, because flipping a
    /// default so existing code behaves differently without being touched is a MAJOR bump under this
    /// repository's own rule.</para>
    ///
    /// <para><b>Why both channels, measured.</b> The step summary is not the job log: content written only
    /// to <c>$GITHUB_STEP_SUMMARY</c> is absent from <c>gh run view --log</c>, which is the command an
    /// agent reaches for - so a summary-only block is invisible to the reader it is written for. And the
    /// console alone is not enough either: measured on .NET&#160;10, <c>dotnet test</c> under NUnit&#160;4
    /// swallowed a library's stdout while letting its stderr through, and under xUnit&#160;2 swallowed
    /// <b>both</b>. No console channel survives every runner, which is why this writes to two places and
    /// why the files beside the report remain the channel that always works.</para>
    ///
    /// <para>Silent on a green run and off CI: a block that speaks on every run is a block people learn to
    /// skip. Default: <c>true</c>.</para>
    /// </summary>
    public bool WriteCiDebugSection { get; set; } = true;

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

    /// <summary>
    /// Whether <c>TestRunReport.html</c> carries the "Report diagnostics" section: the collapsed list of
    /// what the run recorded about itself — a tap whose decoder gave up, a skipped capture line, a render
    /// that failed. Default: <c>false</c>, because on a healthy run it is a line of noise above the
    /// features. Nothing else moves with it: every diagnostic still reaches
    /// <see cref="Kronikol.Ingestion.IngestResult.Diagnostics"/>, the <c>diagnostics</c> array of
    /// <c>TestRunReport.json</c>, and the console. <see cref="ReportToggleDefaults.DiagnosticsOpen"/>
    /// decides whether the section starts open, and is inert while this is <c>false</c>.
    /// </summary>
    public bool ShowReportDiagnosticsSection { get; set; }

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

    /// <summary>
    /// How wide, in pixels, a sequence-diagram note body is drawn before PlantUML breaks it at a
    /// space (<c>skinparam wrapWidth</c>). Default: <c>800</c>.
    /// <para>
    /// Raise it when your notes carry wide content — analytics SQL, XML, tabular text — and the report
    /// is not rendered with <see cref="PlantUmlRendering.BrowserJs"/>: this is the only note-width
    /// control that reaches <c>Local</c>, <c>NodeJs</c> and a source a reader copies out of the report,
    /// because the per-note full-width and monospace toggles are client-side. Under <c>BrowserJs</c> it
    /// sets the width every note <em>starts</em> at; readers can still widen individual notes.
    /// </para>
    /// <para>
    /// Valid range 720-4096. The floor is the width a form-url-encoded note chunk needs to stay on one
    /// drawn line; the ceiling is PlantUML's own <c>PLANTUML_LIMIT_SIZE</c>, past which a rasterised
    /// diagram (a PNG render, or a source pasted into plantuml.com) is silently cropped. A value
    /// outside it fails report generation with the range in the message.
    /// </para>
    /// </summary>
    public int DiagramNoteWrapWidth { get; set; } = PlantUml.PlantUmlCreator.DefaultNoteWrapWidth;

    /// <summary>
    /// Default start states for the interactive report controls (Details radio, truncate lines,
    /// headers/assertions/steps/databases toggles, note format, expand states, diagram tab, panels,
    /// filter modes, disclosure sections). Applies to the HTML test run report AND, unless a
    /// property is specifically overridden via <see cref="SpecificationsToggleDefaults"/>, to
    /// Specifications.html. Default: all unset (built-in defaults).
    /// </summary>
    public ReportToggleDefaults TestRunReportToggleDefaults { get; set; } = new();

    /// <summary>
    /// Specifications.html overrides for <see cref="TestRunReportToggleDefaults"/>. Any property
    /// left <c>null</c> inherits the effective TestRunReport value. Default: all unset (full
    /// inheritance).
    /// </summary>
    public ReportToggleDefaults SpecificationsToggleDefaults { get; set; } = new();

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

    // ─── Cross-run history ─────────────────────────────────────

    /// <summary>
    /// The history ledger this run reads and appends to: an append-only <c>history.jsonl</c> holding the
    /// last runs of every suite, so a report can say whether a failure is new, whether the scenario has
    /// been failing since a particular run, and whether it flips.
    ///
    /// <para>Default: <c>null</c>, which resolves the ledger in this order: the <c>KRONIKOL_HISTORY</c>
    /// environment variable (the value <c>off</c> switches history off for the run), then
    /// <c>&lt;repository root&gt;/.kronikol/history.jsonl</c> for the nearest repository or
    /// <c>.kronikol</c> directory above the test output or the reports directory. A relative path is
    /// taken against the repository root when there is one. When nothing resolves, the run still writes
    /// its <c>History.run.json</c> fragment and records a <c>HistoryUnavailable</c> diagnostic saying how
    /// to enable it.</para>
    /// </summary>
    public string? HistoryFilePath { get; set; }

    /// <summary>
    /// The identity this run is recorded under, for a CI provider Kronikol does not detect. Default:
    /// <c>null</c>, which derives it: <c>gh:&lt;run id&gt;:&lt;attempt&gt;</c> on GitHub Actions,
    /// <c>ado:&lt;build id&gt;:1</c> on Azure DevOps, and a timestamped <c>local:</c> id elsewhere. Set it
    /// from the pipeline's own variables — <c>gitlab:$CI_PIPELINE_ID:1</c> — so that shards of one pipeline
    /// fold into one run and a re-run does not overwrite the run it re-ran. One identity per suite per
    /// run: a second append under the same identity is ignored as a duplicate.
    /// </summary>
    public string? HistoryRunId { get; set; }

    /// <summary>
    /// How many earlier runs of the suite the verdicts look back over. Default: 50. The ledger is scanned
    /// end to end whatever the window; only the windowed runs are parsed, so this bounds the cost of
    /// reading history at the end of a run. <c>kronikol history prune</c> drops runs outside it.
    /// </summary>
    public int HistoryWindow { get; set; } = 50;

    /// <summary>
    /// How many pass-or-fail verdicts a scenario needs before it can be called flaky or slower.
    /// Default: 5. Below it the status verdicts (new, broke, failing, fixed) still apply and the report
    /// says how many runs are recorded.
    /// </summary>
    public int HistoryMinRuns { get; set; } = 5;

    /// <summary>Whether per-scenario durations are recorded, which is what the slower verdict and the duration trend need. Default: <c>true</c>.</summary>
    public bool HistoryDurations { get; set; } = true;

    /// <summary>
    /// Whether the interaction fingerprint — the set of calls each scenario made, with ids, timestamps
    /// and what a statement carried as data templated away — and the call count are recorded. They are
    /// what the behaviour-changed verdict compares. Default: <c>true</c>.
    /// </summary>
    public bool HistoryShapes { get; set; } = true;

    /// <summary>
    /// Whether the first line of each failure message (at most 200 characters) is kept in the ledger as
    /// the failure's cluster key. Default: <c>true</c>. Set <c>false</c> to keep only a hash of that
    /// line: failures still cluster across runs and no message text reaches the ledger.
    /// </summary>
    public bool HistoryErrorKeys { get; set; } = true;

    /// <summary>
    /// The flip rate — changes between pass and fail over the transitions between verdicts — at or above
    /// which a scenario is flaky, once it has failed, recovered and failed again. Default: 0.1. Flip rate
    /// rather than fail rate: a scenario that failed five of ten in a row broke and was fixed, one that
    /// failed five of ten alternating is flaky, and the fail rate cannot tell them apart.
    /// </summary>
    public double HistoryFlakyRate { get; set; } = 0.1;

    /// <summary>
    /// The factor over the window's 95th-percentile duration that makes a scenario slower, when both this
    /// run and the previous one exceed it. Default: 1.5. Every duration is read against the speed of its
    /// run — the median of the other scenarios' durations in that run — so a slow runner lifts the bar
    /// with the readings, and a scenario is slower only when it got slower than its run did.
    /// </summary>
    public double HistorySlowerBy { get; set; } = 1.5;

    /// <summary>
    /// The least a scenario must be over the bar, in milliseconds, before it is slower. Default: 100.
    /// A scenario measured in single digits clears the factor on runner jitter alone; the floor keeps a
    /// verdict for a change somebody would notice.
    /// </summary>
    public int HistorySlowerMinMs { get; set; } = 100;

    /// <summary>
    /// How many runs back a set of calls the scenario held counts as a known state. Default: 10. A
    /// scenario back on a set it held within this many passing runs, with a different set in between,
    /// reads <c>alternating</c> rather than <c>behaviour-changed</c>: two states, both its own, and which
    /// one a run sees depends on ordering. The memory is short so that a regression back to how the
    /// scenario behaved long ago still reads as a change.
    /// </summary>
    public int HistoryAlternatingRuns { get; set; } = 10;

    /// <summary>
    /// How many consecutive runs a changed call count must hold before the change is behaviour. Default: 2.
    /// The same set of calls made a different number of times is an N+1 regression when it stays and the
    /// processor's timing when it does not, and it does not stay more often than it does (measured on a
    /// consumer's ledger: ten of thirteen such changes reverted the next run). The run that shows the new
    /// count reads it out in the evidence; the run that still holds it is the verdict, and names both. 1 is
    /// the 3.15.0 rule, a verdict on the run the count changes. Whatever this is set to, the count must
    /// have been constant over <see cref="HistoryMinRuns"/> runs before the change.
    /// </summary>
    public int HistoryCountRuns { get; set; } = 2;

    /// <summary>
    /// The share of the previous run's scenarios a run may lack before it is recorded as partial — a
    /// filtered run, a crashed half — so its missing scenarios are not reported absent and it is not the
    /// run the next one is compared against. Default: 0.10.
    /// </summary>
    public double HistoryPartialThreshold { get; set; } = 0.10;

    /// <summary>
    /// Whether this run is partial. Default: <c>null</c>, which applies <see cref="HistoryPartialThreshold"/>.
    /// <c>true</c> records it as partial whatever it holds; <c>false</c> records it as complete.
    /// </summary>
    public bool? HistoryPartialRun { get; set; }

    /// <summary>
    /// Whether the same calls in a different order is a verdict (<c>reordered</c>). Default: <c>false</c>,
    /// because parallel steps reorder calls run to run without anything having changed.
    /// </summary>
    public bool HistoryReordered { get; set; }

    /// <summary>
    /// The branch stream to read the run against. Default: <c>null</c> — on a pull request build, the
    /// branch the pull request targets (<c>GITHUB_BASE_REF</c> on GitHub Actions,
    /// <c>SYSTEM_PULLREQUEST_TARGETBRANCH</c> on Azure DevOps, both set only on pull request builds);
    /// otherwise the run's own branch (<c>local</c> off CI). Verdicts are computed within a stream, and
    /// a pull request's runs form their own, so reading them against their own earlier runs would be a
    /// cold start when the question is what changed against the branch they target. An empty string is
    /// the run's own branch even on a pull request build; any other value names the stream to read
    /// against. The run's own line still records under its own branch.
    /// </summary>
    public string? HistoryBranch { get; set; }

    /// <summary>
    /// A second branch stream to read the same run against, reported beside the run's own reading — on
    /// the failures digest, the run-end pointer and the report's History section — as
    /// <c>on main: 1 broke (against 12 earlier runs on main)</c>. Default: <c>null</c>, no second reading.
    /// </summary>
    public string? HistoryCompareBranch { get; set; }

    /// <summary>
    /// Whether the run writes <c>History.run.json</c> into the reports directory: its own line of history,
    /// with the roster it needs. Default: <c>true</c>. It is what a sharded or CI build hands to
    /// <c>kronikol history record</c>, which folds the fragments of one run into one ledger line.
    /// </summary>
    public bool GenerateHistoryFragment { get; set; } = true;

    /// <summary>
    /// Whether the run appends its line to the ledger itself. Default: <c>null</c>: append when the ledger
    /// was named explicitly (<see cref="HistoryFilePath"/> or <c>KRONIKOL_HISTORY</c>), or when the run is
    /// not on CI. A CI run that merely found the repository's ledger writes only its fragment — the
    /// checkout is discarded, and the fragments are what the workflow records — while a developer's run
    /// appends to the working tree's ledger directly. <c>true</c> always appends; <c>false</c> never does.
    /// </summary>
    public bool? WriteHistoryLedger { get; set; }

    /// <summary>
    /// Whether the test run report embeds the history it read — a sparkline and verdict beside each
    /// scenario, and the History section when <see cref="ShowHistorySection"/> asks for it. Default:
    /// <c>true</c>. With no ledger the report is byte-for-byte what it was without history.
    /// </summary>
    public bool EmbedHistoryInReport { get; set; } = true;

    /// <summary>
    /// Whether <c>TestRunReport.html</c> carries the History section beside the timeline: the run's
    /// summary line, the trend of the last runs, and the lists of what changed. Default: <c>false</c> —
    /// a run where nothing changed still has the section to say so, and above the features that reads as
    /// noise. Off, the history a run read is still beside the scenario it is about (the sparkline and the
    /// verdict pill, which <see cref="EmbedHistoryInReport"/> governs), still in <c>Failures.md</c>, the
    /// CTRF document and the ledger, and still what <c>kronikol query history</c> answers from.
    /// <c>kronikol merge --history</c> turns it on for the merged report it was asked to render.
    /// </summary>
    public bool ShowHistorySection { get; set; }
}