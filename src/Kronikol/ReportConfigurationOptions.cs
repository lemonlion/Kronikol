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

    /// <summary>Folder path (relative to the test output directory) where reports are written. Default: <c>"Reports"</c>.</summary>
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
    public bool InlineBackgroundSteps { get; set; }

    /// <summary>When <c>true</c>, parameterized tests are grouped into a single collapsible table. Default: <c>true</c>.</summary>
    public bool GroupParameterizedTests { get; set; } = true;

    /// <summary>When <c>true</c>, sequence diagram arrows are colored by dependency type. Default: <c>true</c>.</summary>
    public bool SequenceDiagramArrowColors { get; set; } = true;

    /// <summary>When <c>true</c>, sequence diagram participant headers get colored backgrounds matching their dependency type. Default: <c>false</c>.</summary>
    public bool SequenceDiagramParticipantColors { get; set; }

    /// <summary>User overrides for dependency-type colors. Keys are <see cref="Tracking.RequestResponseLog.DependencyCategory"/> strings (e.g. <c>"CosmosDB"</c>), values are hex colors (e.g. <c>"#E74C3C"</c>).</summary>
    public Dictionary<string, string>? DependencyColors { get; set; }

    /// <summary>User overrides mapping service names to dependency categories. Keys are service names, values are category strings (e.g. <c>"CosmosDB"</c>, <c>"Redis"</c>).</summary>
    public Dictionary<string, string>? ServiceTypeOverrides { get; set; }

    /// <summary>Controls how GraphQL request bodies are displayed in sequence diagram notes. Default: <see cref="GraphQlBodyFormat.FormattedWithMetadata"/>.</summary>
    public GraphQlBodyFormat GraphQlBodyFormat { get; set; } = GraphQlBodyFormat.FormattedWithMetadata;

    /// <summary>Maximum number of parameter columns shown per parameterized test group. Default: <c>10</c>.</summary>
    public int MaxParameterColumns { get; set; } = 10;

    /// <summary>When <c>true</c>, parameter names are converted to title case in report tables. Default: <c>true</c>.</summary>
    public bool TitleizeParameterNames { get; set; } = true;

    /// <summary>
    /// Optional delegate returning the total number of test scenarios expected in this assembly.
    /// When set, report generation is skipped if the actual scenario count is less than the expected
    /// count — preventing partial test runs (e.g. single-test filtering) from overwriting the
    /// full Specifications report.
    /// </summary>
    public Func<int>? ExpectedTestCount { get; set; }
}