using System.Globalization;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Kronikol.ComponentDiagram;
using Kronikol.InternalFlow;
using Kronikol.Tracking;

namespace Kronikol.Reports;

/// <summary>
/// Generates HTML, JSON, XML, YAML, and CI summary reports from test features and diagrams.
/// This is the primary report generation entry point called by framework adapters.
/// </summary>
public static class ReportGenerator
{
    internal static string KronikolVersion { get; } =
        typeof(ReportGenerator).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(ReportGenerator).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    internal static bool ShouldEmbedComponentDiagram(ReportConfigurationOptions options) =>
        (options.ComponentDiagramOptions ?? new ComponentDiagramOptions()).EmbedInTestRunReport;

    /// <summary>Rendered in place of the diagram section for a scenario whose id matched no tracked interaction.</summary>
    internal const string NoInteractionsMarkerHtml =
        "<div class=\"no-interactions\" data-no-interactions=\"true\">No interactions captured for this scenario.</div>";
    private static readonly Lazy<string> AdvancedSearchJs = new(() =>
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("advanced-search.js", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Embedded resource advanced-search.js not found.");
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> ResourceCache = new();

    // Loads an embedded report asset (externalized inline JS/CSS, JAVA_PORT_PLAN 4.2) by file-name suffix.
    private static string LoadResource(string name) => ResourceCache.GetOrAdd(name, n =>
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(r => r.EndsWith(n, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded resource {n} not found.");
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    // The output directory for the report generation currently in flight. Flows (via ExecutionContext)
    // into the Parallel.Invoke workers that call WriteFile, so every file of one run lands in the
    // directory resolved from that run's ReportConfigurationOptions.ReportsFolderPath.
    private static readonly AsyncLocal<string?> ActiveReportsDirectory = new();

    /// <summary>
    /// Resolves the directory reports are written to for the given options: <see cref="ReportConfigurationOptions.ReportsFolderPath"/>
    /// as-is when absolute, otherwise relative to <c>AppDomain.CurrentDomain.BaseDirectory</c>. Defaults to
    /// <c>&lt;BaseDirectory&gt;/Reports</c> when options are <c>null</c> or the folder is blank.
    /// </summary>
    public static string ResolveReportsDirectory(ReportConfigurationOptions? options = null)
    {
        var folder = options?.ReportsFolderPath;
        if (string.IsNullOrWhiteSpace(folder))
            folder = "Reports";
        return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, folder));
    }

    /// <summary>The directory the current (or default) report generation writes to.</summary>
    internal static string CurrentReportsDirectory =>
        ActiveReportsDirectory.Value ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Reports");

    public static void CreateStandardReportsWithDiagrams(Feature[] features, DateTime startRunTime, DateTime endRunTime, ReportConfigurationOptions options)
    {
        var previous = ActiveReportsDirectory.Value;
        ActiveReportsDirectory.Value = ResolveReportsDirectory(options);
        try
        {
            CreateStandardReportsWithDiagramsCore(features, startRunTime, endRunTime, options);
        }
        finally
        {
            ActiveReportsDirectory.Value = previous;
        }
    }

    private static void CreateStandardReportsWithDiagramsCore(Feature[] features, DateTime startRunTime, DateTime endRunTime, ReportConfigurationOptions options)
    {
        // Guard: skip report generation entirely when there are zero scenarios.
        // This prevents the xUnit v3 test-discovery pass (which triggers
        // ITestPipelineStartup but runs no tests) from overwriting a valid
        // report from a previous run with an empty one.
        if (features.Length == 0 || features.All(f => f.Scenarios is null || f.Scenarios.Length == 0))
        {
            if (RequestResponseLogger.RequestAndResponseLogs.Length > 0)
            {
                Console.WriteLine("⚠ WARNING: No test contexts were enqueued, but tracking logs exist. " +
                    "Reports will be empty. Ensure DiagrammedTestRun.TestContexts.Enqueue(TestContext.Current) " +
                    "is called in every test's DisposeAsync().");

                if (options.DiagnosticMode)
                    DiagnosticReportGenerator.Generate(RequestResponseLogger.RequestAndResponseLogs, features, options);
            }

            return;
        }

        // One pass over the finished model, before anything reads it, so the HTML, JSON, XML and YAML
        // views of a step all show the same sentence (Reports.StepText explains the rule).
        if (options.CapitaliseStepText)
            StepText.ApplyToFeatures(features);

        // Same idea for the headings: a Gherkin "Scenario: the overview renders" is a sentence too.
        if (options.CapitaliseTitles)
            StepText.ApplyToTitles(features);

        ReportLowercaseSteps(features);
        ReportLowercaseTitles(features);

        if (options.ExpectedTestCount != null)
        {
            var scenarioCount = features.SelectMany(f => f.Scenarios).Count();
            if (scenarioCount < options.ExpectedTestCount())
            {
                options.GenerateSpecificationsReport = false;
                options.GenerateSpecificationsData = false;
            }
        }

        if (options.InternalFlowTracking && options.DiagramFormat == DiagramFormat.PlantUml)
        {
            if (options.PlantUmlRendering is PlantUmlRendering.Server or PlantUmlRendering.Local or PlantUmlRendering.NodeJs)
            {
                options.InlineSvgRendering = true;
                options.PlantUmlImageFormat = PlantUmlImageFormat.Svg;
            }
        }

        var fetcherOptions = new DiagramsFetcherOptions
        {
            PlantUmlServerBaseUrl = options.PlantUmlServerBaseUrl,
            RequestPostFormattingProcessor = options.RequestResponsePostProcessor,
            ResponsePostFormattingProcessor = options.RequestResponsePostProcessor,
            RequestMidFormattingProcessor = options.RequestResponseMidProcessor,
            ResponseMidFormattingProcessor = options.RequestResponseMidProcessor,
            ExcludedHeaders = options.ExcludedHeaders,
            SeparateSetup = options.SeparateSetup,
            HighlightSetup = options.HighlightSetup,
            SetupHighlightColor = options.SetupHighlightColor,
            LazyLoadDiagramImages = options.LazyLoadDiagramImages,
            FocusEmphasis = options.FocusEmphasis,
            FocusDeEmphasis = options.FocusDeEmphasis,
            PlantUmlTheme = options.PlantUmlTheme,
            PlantUmlImageFormat = options.PlantUmlImageFormat,
            LocalDiagramRenderer = options.LocalDiagramRenderer,
            LocalDiagramImageDirectory = options.LocalDiagramImageDirectory,
            DiagramFormat = options.DiagramFormat,
            PlantUmlRendering = options.PlantUmlRendering,
            InlineSvgRendering = options.InlineSvgRendering,
            InternalFlowTracking = options.InternalFlowTracking,
            SequenceDiagramArrowColors = options.SequenceDiagramArrowColors,
            SequenceDiagramParticipantColors = options.SequenceDiagramParticipantColors,
            DependencyColors = options.DependencyColors,
            ServiceTypeOverrides = options.ServiceTypeOverrides,
            GraphQlBodyFormat = options.GraphQlBodyFormat,
            CollapseConsecutiveIdenticalCalls = options.CollapseConsecutiveIdenticalCalls,
            CollapseThreshold = options.CollapseThreshold,
            MaxArrowsPerDiagram = options.MaxArrowsPerDiagram
        };
        var diagrams = DefaultDiagramsFetcher.GetDiagramsFetcher(fetcherOptions)();

        var internalFlowDataScript = "";
        Dictionary<string, InternalFlowSegment>? wholeTestSegments = null;
        Dictionary<string, InternalFlowSegment>? perBoundarySegments = null;
        RequestResponseLog[]? trackedLogs = null;
        if (options.InternalFlowTracking)
        {
            trackedLogs = RequestResponseLogger.RequestAndResponseLogs
                .Where(x => !(x?.TrackingIgnore ?? true))
                .ToArray();

            var spans = InternalFlowSpanCollector.CollectSpans(
                options.InternalFlowSpanGranularity,
                options.InternalFlowActivitySources);

            perBoundarySegments = InternalFlowSegmentBuilder.BuildSegments(trackedLogs, spans);

            internalFlowDataScript = DiagramContextMenu.GetInternalFlowConfigScript(options.InternalFlowHasDataBehavior)
                + InternalFlowHtmlGenerator.GenerateSegmentDataScript(
                perBoundarySegments,
                options.InternalFlowDiagramStyle,
                options.InternalFlowShowFlameChart,
                options.InternalFlowFlameChartPosition,
                options.InternalFlowNoDataBehavior,
                options.InternalFlowSpanGranularity,
                options.InternalFlowActivitySources);

            if (options.WholeTestFlowVisualization != WholeTestFlowVisualization.None)
            {
                wholeTestSegments = InternalFlowSegmentBuilder.BuildWholeTestSegments(trackedLogs, spans);
            }
        }

        var ciMetadata = CiMetadataDetector.Detect();

        // The data file's httpInteractions block must not depend on internal-flow tracking being on:
        // externally captured traffic (proxy taps, ingested NDJSON) has no in-process spans but the
        // interactions are the whole point of the data export.
        var dataLogs = trackedLogs ?? RequestResponseLogger.RequestAndResponseLogs
            .Where(x => !(x?.TrackingIgnore ?? true))
            .ToArray();

        var specsDataExtension = GetDataFormatExtension(options.SpecificationsDataFormat);
        var testRunDataExtension = GetDataFormatExtension(options.TestRunReportDataFormat);

        // Pre-compute component diagram PlantUML for embedding
        string? componentDiagramPlantUml = null;
        if (options.GenerateComponentDiagram)
        {
            var componentOptions = options.ComponentDiagramOptions ?? new ComponentDiagramOptions();
            componentOptions.DependencyColors ??= options.DependencyColors;
            var componentLogs = RequestResponseLogger.RequestAndResponseLogs.Where(x => !(x?.TrackingIgnore ?? true));
            var componentRelationships = ComponentDiagramGenerator.ExtractRelationships(componentLogs, componentOptions.ParticipantFilter);
            var useBrowserJs = options.PlantUmlRendering == PlantUmlRendering.BrowserJs;
            componentDiagramPlantUml = ComponentDiagramGenerator.GeneratePlantUml(componentRelationships, componentOptions, useC4: !useBrowserJs);
        }

        // Copy attachment files into the Reports directory so that HTML links resolve
        // when reports are uploaded to GitHub Pages or CI artifacts.
        var reportsDir = CurrentReportsDirectory;
        Directory.CreateDirectory(reportsDir);
        CopyAttachmentsToReportsFolder(features, reportsDir);

        // Everything recorded so far — the host's entries (IngestRequest.HostDiagnostics), malformed lines,
        // diagram render failures, attachment failures — goes into the report itself. One snapshot, taken
        // before the outputs run in parallel, so the HTML and the data files agree; an OutputFailure raised
        // by one of those outputs is therefore only in the collector, not in the files.
        var reportDiagnostics = ReportDiagnosticsScope.Current?.Entries ?? [];

        var actions = new List<(string Name, Action Run)>();
        void Add(string name, Action run) => actions.Add((name, run));

        // One deep-search build cache shared by both HTML reports (§5.1): they render the same
        // features/diagrams, so the expensive normalize+hash work happens once.
        var searchIndexCache = new SearchIndex.SearchIndexBuildCache();

        if (options.GenerateSpecificationsReport)
        {
            Add($"{options.HtmlSpecificationsFileName}.html", () => GenerateHtmlReport(diagrams, features, startRunTime, endRunTime, options.HtmlSpecificationsCustomStyleSheet, $"{options.HtmlSpecificationsFileName}.html", options.SpecificationsTitle, false, generateBlankOnFailedTests: true, lazyLoadImages: options.LazyLoadDiagramImages, diagramFormat: options.DiagramFormat, plantUmlRendering: options.PlantUmlRendering, inlineSvgRendering: options.InlineSvgRendering, internalFlowTracking: options.InternalFlowTracking, internalFlowDataScript: internalFlowDataScript, wholeTestSegments: wholeTestSegments, trackedLogs: trackedLogs, wholeTestVisualization: options.WholeTestFlowVisualization, showStepNumbers: options.SpecificationsShowStepNumbers, customCss: options.CustomCss, customFaviconBase64: options.CustomFaviconBase64, customLogoHtml: options.CustomLogoHtml, groupParameterizedTests: options.GroupParameterizedTests, maxParameterColumns: options.MaxParameterColumns, titleizeParameterNames: options.TitleizeParameterNames, showNoInteractionsMarker: options.ShowNoInteractionsMarker, browserRenderWorkers: options.BrowserRenderWorkers, browserRenderCacheMegabytes: options.BrowserRenderCacheMegabytes, browserFragmentMaxHeight: options.BrowserFragmentMaxHeight, separateBackgroundSteps: options.SeparateBackgroundSteps, collapseRepeatedStepKeywords: options.CollapseRepeatedStepKeywords, notePayloadFormat: options.NotePayloadFormat, fullSearchIndex: options.FullSearchIndex, searchIndexCache: searchIndexCache));
        }

        if (options.GenerateTestRunReport)
        {
            Add($"{options.HtmlTestRunReportFileName}.html", () => GenerateHtmlReport(diagrams, features, startRunTime, endRunTime, null, $"{options.HtmlTestRunReportFileName}.html", GetTestRunReportTitle(options), true, lazyLoadImages: options.LazyLoadDiagramImages, diagramFormat: options.DiagramFormat, plantUmlRendering: options.PlantUmlRendering, inlineSvgRendering: options.InlineSvgRendering, internalFlowTracking: options.InternalFlowTracking, internalFlowDataScript: internalFlowDataScript, wholeTestSegments: wholeTestSegments, trackedLogs: trackedLogs, wholeTestVisualization: options.WholeTestFlowVisualization, ciMetadata: ciMetadata, showStepNumbers: options.TestRunReportShowStepNumbers, customCss: options.CustomCss, customFaviconBase64: options.CustomFaviconBase64, customLogoHtml: options.CustomLogoHtml, groupParameterizedTests: options.GroupParameterizedTests, maxParameterColumns: options.MaxParameterColumns, titleizeParameterNames: options.TitleizeParameterNames, componentDiagramPlantUml: ShouldEmbedComponentDiagram(options) ? componentDiagramPlantUml : null, showNoInteractionsMarker: options.ShowNoInteractionsMarker, diagnostics: reportDiagnostics, browserRenderWorkers: options.BrowserRenderWorkers, browserRenderCacheMegabytes: options.BrowserRenderCacheMegabytes, browserFragmentMaxHeight: options.BrowserFragmentMaxHeight, separateBackgroundSteps: options.SeparateBackgroundSteps, collapseRepeatedStepKeywords: options.CollapseRepeatedStepKeywords, notePayloadFormat: options.NotePayloadFormat, fullSearchIndex: options.FullSearchIndex, searchIndexCache: searchIndexCache));
        }

        if (options.GenerateSpecificationsData)
        {
            Add($"{options.YamlSpecificationsFileName}.{specsDataExtension}", () => GenerateSpecificationsData(features, $"{options.YamlSpecificationsFileName}.{specsDataExtension}", options.SpecificationsTitle, options.SpecificationsDataFormat, true));
        }

        if (options.GenerateTestRunReportData)
        {
            if (options.GenerateMergeableData && options.TestRunReportDataFormat == DataFormat.Json)
            {
                Add($"{options.HtmlTestRunReportFileName}.{testRunDataExtension}", () => WriteFile(
                    BuildMergeableReportJson(features, startRunTime, endRunTime, diagrams, trackedLogs, perBoundarySegments, wholeTestSegments, ciMetadata, options, reportDiagnostics),
                    $"{options.HtmlTestRunReportFileName}.{testRunDataExtension}"));
            }
            else
            {
                Add($"{options.HtmlTestRunReportFileName}.{testRunDataExtension}", () => GenerateTestRunReportData(features, startRunTime, endRunTime, $"{options.HtmlTestRunReportFileName}.{testRunDataExtension}", options.TestRunReportDataFormat, diagrams, dataLogs, reportDiagnostics, options.TestRunReportFullStepDetail));
            }
        }

        if (options.GenerateTestRunReportSchema)
        {
            Add("TestRunReport schema", () => GenerateTestRunReportSchema($"{options.HtmlTestRunReportFileName}.schema.{GetSchemaExtension(options.TestRunReportDataFormat)}", options.TestRunReportDataFormat));
        }

        if (options.GenerateComponentDiagram)
        {
            Add("ComponentDiagram.html", () => ComponentDiagramReportGenerator.GenerateComponentDiagramReport(
                RequestResponseLogger.RequestAndResponseLogs.Where(x => !(x?.TrackingIgnore ?? true)),
                options,
                perBoundarySegments: perBoundarySegments,
                wholeTestSegments: wholeTestSegments));
        }

        RunOutputs(actions);

        var diagnostics = ReportDiagnostics.Analyse(
            RequestResponseLogger.RequestAndResponseLogs, features,
            includeSourceDiscovery: options.ActivitySourceDiscovery);
        foreach (var message in diagnostics)
            Console.WriteLine(message);

        if (options.DiagnosticMode)
            DiagnosticReportGenerator.Generate(RequestResponseLogger.RequestAndResponseLogs, features, options);

        if (options.WriteCiSummary)
        {
            var (truncatedDiagrams, fullDiagrams) = DefaultDiagramsFetcher.GetCiSummaryDiagrams(fetcherOptions);
            var markdown = CiSummaryGenerator.GenerateMarkdown(features, truncatedDiagrams, fullDiagrams, startRunTime, endRunTime, options.MaxCiSummaryDiagrams,
                options.DiagramFormat, options.PlantUmlServerBaseUrl, options.LocalDiagramRenderer);

            var directory = CurrentReportsDirectory;
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "CiSummary.md"), markdown);

            var ciEnvironment = CiEnvironmentDetector.Detect();
            CiSummaryWriter.Write(markdown, ciEnvironment);
        }

        if (options.PublishCiArtifacts)
        {
            var ciEnv = CiEnvironmentDetector.Detect();
            var ciReportsDir = CurrentReportsDirectory;
            if (Directory.Exists(ciReportsDir))
            {
                var reportFiles = Directory.GetFiles(ciReportsDir)
                    .Where(f => f.EndsWith(".html") || f.EndsWith(".yml") || f.EndsWith(".md") || f.EndsWith(".json") || f.EndsWith(".xml"))
                    .ToArray();
                CiArtifactPublisher.Publish(reportFiles, ciEnv, options.CiArtifactName, options.CiArtifactRetentionDays);
            }
        }
    }

    /// <summary>
    /// Runs the report outputs in parallel, isolating each one: an output that throws is recorded as an
    /// <see cref="DiagnosticKind.OutputFailure"/> diagnostic and every other output is still written.
    /// </summary>
    /// <remarks>
    /// Before this, <c>Parallel.Invoke</c> propagated the first failure as an
    /// <see cref="AggregateException"/> and the whole report was lost — one unwritable file, one
    /// serialisation bug in one scenario's data, and the HTML nobody could otherwise reproduce went with
    /// it. A report is diagnostics: a partial one beats none.
    /// </remarks>
    private static void RunOutputs(List<(string Name, Action Run)> outputs)
    {
        Parallel.Invoke(outputs.Select(output => (Action)(() =>
        {
            try
            {
                output.Run();
            }
            catch (Exception ex)
            {
                ReportDiagnosticsScope.Record(DiagnosticKind.OutputFailure, $"Could not write {output.Name}", ex);
                Console.WriteLine($"⚠ WARNING: could not write {output.Name}: {ex.GetType().Name}: {ex.Message}");
            }
        })).ToArray());
    }

    /// <summary>
    /// Records how many step and assertion labels still do not read as sentences after
    /// <see cref="StepText"/> ran — the quoted literals the rule deliberately leaves alone, and anything a
    /// producer slipped past it — with the first few as examples, so the gap is visible in
    /// <c>kronikol ingest</c>'s output and on a dashboard instead of only in the rendered HTML.
    /// </summary>
    private static void ReportLowercaseSteps(Feature[] features)
    {
        if (ReportDiagnosticsScope.Current is null)
            return;

        var (count, examples) = StepText.FindNotStartingWithCapital(features);
        if (count == 0)
            return;

        var suffix = examples.Length == 0 ? "" : $" e.g. {string.Join(" | ", examples)}";
        ReportDiagnosticsScope.Record(DiagnosticKind.StepsNotStartingWithCapital,
            $"{count} step text(s) do not start with a capital letter.{suffix}");
    }

    /// <summary>
    /// Records the feature, rule and scenario titles that still start with a lower-case letter after
    /// <see cref="StepText.ApplyToTitles"/> ran — the sibling of <see cref="ReportLowercaseSteps"/>.
    /// </summary>
    private static void ReportLowercaseTitles(Feature[] features)
    {
        if (ReportDiagnosticsScope.Current is null)
            return;

        var (count, examples) = StepText.FindTitlesNotStartingWithCapital(features);
        if (count == 0)
            return;

        var suffix = examples.Length == 0 ? "" : $" e.g. {string.Join(" | ", examples)}";
        ReportDiagnosticsScope.Record(DiagnosticKind.TitlesNotStartingWithCapital,
            $"{count} feature/rule/scenario title(s) do not start with a capital letter.{suffix}");
    }

    public static string GetTestRunReportTitle(ReportConfigurationOptions options)
    {
        if (!string.IsNullOrEmpty(options.TestRunReportTitle))
            return options.TestRunReportTitle;
        var prefix = options.ComponentDiagramOptions?.Title;
        if (string.IsNullOrEmpty(prefix))
            prefix = options.FixedNameForReceivingService;
        return string.IsNullOrEmpty(prefix) ? "Test Run Report" : $"{prefix} - Test Run Report";
    }

    public static string GenerateHtmlReport(DefaultDiagramsFetcher.DiagramAsCode[] diagrams,
        Feature[] features,
        DateTime startRunTime,
        DateTime endRunTime,
        string? stylesheet,
        string fileName,
        string title,
        bool includeTestRunData,
        bool generateBlankOnFailedTests = false,
        bool lazyLoadImages = true,
        DiagramFormat diagramFormat = DiagramFormat.PlantUml,
        PlantUmlRendering plantUmlRendering = PlantUmlRendering.BrowserJs,
        bool inlineSvgRendering = false,
        bool internalFlowTracking = false,
        string internalFlowDataScript = "",
        Dictionary<string, InternalFlowSegment>? wholeTestSegments = null,
        RequestResponseLog[]? trackedLogs = null,
        WholeTestFlowVisualization wholeTestVisualization = WholeTestFlowVisualization.None,
        CiMetadata? ciMetadata = null,
        bool showStepNumbers = false,
        string? customCss = null,
        string? customFaviconBase64 = null,
        string? customLogoHtml = null,
        bool groupParameterizedTests = true,
        int maxParameterColumns = 10,
        bool titleizeParameterNames = true,
        string? componentDiagramPlantUml = null,
        Dictionary<string, Merge.WholeTestFlowFragment>? precomputedWholeTestContent = null,
        bool showNoInteractionsMarker = false,
        IReadOnlyList<DiagnosticEntry>? diagnostics = null,
        int browserRenderWorkers = Constants.TrackingDefaults.BrowserRenderWorkers,
        int browserRenderCacheMegabytes = Constants.TrackingDefaults.BrowserRenderCacheMegabytes,
        int browserFragmentMaxHeight = Constants.TrackingDefaults.BrowserFragmentMaxHeight,
        bool separateBackgroundSteps = false,
        bool collapseRepeatedStepKeywords = true,
        NotePayloadFormat notePayloadFormat = NotePayloadFormat.Json,
        bool fullSearchIndex = true,
        SearchIndex.SearchIndexBuildCache? searchIndexCache = null)
    {
        if (generateBlankOnFailedTests && features.Any(x => x.Scenarios.Any(y => y.Result == ExecutionResult.Failed)))
            return WriteFile(string.Empty, fileName);

        var scenarioFeatureMapHelper = LoadResource("report-scenario-feature-map-helper.js");

        // Shared gzip+base64 decompressor — always included; several conditionally-emitted
        // scripts (context menu, internal-flow popup, deep search) call it in rendering modes
        // where the BrowserJs render script is absent.
        var decompressHelper = LoadResource("report-decompress-helper.js");

        var toggleHappyPathsFunction = LoadResource("report-toggle-happy-paths-function.js");
        var searchFunction = LoadResource("report-search-function.js");

        // Deep search ("search everything") client — always included; it no-ops when the
        // kron-search-index blob is absent (FullSearchIndex=false / older reports).
        var searchIndexClientScript = LoadResource("report-search-index.js");

        var dependencyFilterFunction = LoadResource("report-dependency-filter-function.js");

        var categoryFilterFunction = LoadResource("report-category-filter-function.js");

        var statusFilterFunction = LoadResource("report-status-filter-function.js");

        // Collapse/Expand All
        var collapseExpandAllFunction = LoadResource("report-collapse-expand-all-function.js");

        var lightboxFunction = LoadResource("report-lightbox-function.js");

        var toggleTableRefFunction = LoadResource("report-toggle-table-ref-function.js");

        var sortTableFunction = LoadResource("report-sort-table-function.js");



        // Copy scenario name
        var copyScenarioNameFunction = LoadResource("report-copy-scenario-name-function.js");

        // Toggle examples detail row
        var toggleExamplesDetailFunction = LoadResource("report-toggle-examples-detail-function.js");

        // Parameterized row selection
        var selectRowFunction = LoadResource("report-select-row-function.js");

        // Toggle between grouped and flat parameter tables
        var toggleFlattenParamsJs = LoadResource("report-toggle-flatten-params-js.js");

        // R4: param-expand toggle auto-selects row + cell-subtable click isolation
        var paramExpandJs = LoadResource("report-param-expand-js.js");

        // Toggle timeline
        var deactivateComponentDiagramJs = !string.IsNullOrEmpty(componentDiagramPlantUml)
            ? """
                                             if (hidden) {
                                                 var cd = document.getElementById('component-diagram');
                                                 if (cd && cd.style.display !== 'none') {
                                                     cd.style.display = 'none';
                                                     var cdBtn = document.querySelector('button.timeline-toggle-active[onclick*="toggle_component_diagram"]');
                                                     if (cdBtn) cdBtn.classList.remove('timeline-toggle-active');
                                                 }
                                             }
              """
            : "";
        var toggleTimelineFunction = $$"""
                                     function toggle_timeline(btn) {
                                         var tl = document.getElementById('scenario-timeline');
                                         if (!tl) return;
                                         var hidden = tl.style.display === 'none';
                                         tl.style.display = hidden ? '' : 'none';
                                         btn.classList.toggle('timeline-toggle-active', hidden);{{deactivateComponentDiagramJs}}
                                     }
                                     """;

        // Toggle component diagram
        var toggleComponentDiagramFunction = !string.IsNullOrEmpty(componentDiagramPlantUml)
            ? """
              function toggle_component_diagram(btn) {
                  var cd = document.getElementById('component-diagram');
                  if (!cd) return;
                  var hidden = cd.style.display === 'none';
                  cd.style.display = hidden ? '' : 'none';
                  btn.classList.toggle('timeline-toggle-active', hidden);
                  if (hidden) {
                      if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(cd);
                      var tl = document.getElementById('scenario-timeline');
                      if (tl && tl.style.display !== 'none') {
                          tl.style.display = 'none';
                          var tlBtn = document.querySelector('button.timeline-toggle-active[onclick*="toggle_timeline"]');
                          if (tlBtn) tlBtn.classList.remove('timeline-toggle-active');
                      }
                  }
              }
              """
            : "";

        // Jump to failure
        var hasFailures = features.SelectMany(f => f.Scenarios).Any(s => s.Result == ExecutionResult.Failed);
        var failureCount = features.SelectMany(f => f.Scenarios).Count(s => s.Result == ExecutionResult.Failed);
        var jumpToFailureFunction = LoadResource("report-jump-to-failure-function.js");

        // Duration filter
        var hasDurations = features.SelectMany(f => f.Scenarios).Any(s => s.Duration.HasValue);
        var durationFilterFunction = LoadResource("report-duration-filter-function.js");

        // Export filtered view
        var exportFunction = LoadResource("report-export-function.js");

        // Persistent filter state
        // No-op stubs (localStorage persistence removed)
        var persistentFilterFunction = LoadResource("report-persistent-filter-function.js");

        // URL-anchored filters
        var urlHashFunction = LoadResource("report-url-hash-function.js");

        // Keyboard navigation
        var keyboardNavigationFunction = LoadResource("report-keyboard-navigation-function.js");

        // Deep link + init script
        var initScript = LoadResource("report-init-script.js");

        var combinedStylesheet = $"""
                                 {Stylesheets.HtmlReportStyleSheet}
                                 {stylesheet}
                                 """;

        var isPlantUmlBrowser = plantUmlRendering == PlantUmlRendering.BrowserJs;
        var isInlineSvg = !isPlantUmlBrowser && inlineSvgRendering;
        var hasInteractiveDiagrams = isPlantUmlBrowser || isInlineSvg;
        var hasAssertionNotes = isPlantUmlBrowser && (
            (trackedLogs is not null && trackedLogs.Any(l => l.PlantUml is not null && l.PlantUml.Contains("<<assertionNote>>"))) ||
            diagrams.Any(d => d.CodeBehind.Contains("<<assertionNote>>")));
        var hasStepDelimiters = isPlantUmlBrowser && (
            (trackedLogs is not null && trackedLogs.Any(l => l.PlantUml is not null && l.PlantUml.Contains("<<stepDelimiter>>"))) ||
            diagrams.Any(d => d.CodeBehind.Contains("<<stepDelimiter>>")));
        var hasDatabaseParticipants = isPlantUmlBrowser && (
            (trackedLogs is not null && trackedLogs.Any(l => l.PlantUml is not null && (l.PlantUml.Contains("\ndatabase \"") || l.PlantUml.Contains("\ncollections \"")))) ||
            diagrams.Any(d => d.CodeBehind.Contains("\ndatabase \"") || d.CodeBehind.Contains("\ncollections \"")));
        // Pretty-printed JSON note payloads open with a bare { or [ on its own
        // line. A false positive is harmless — the dropdown's queue builder
        // finds nothing eligible and the no-op clears its pending state.
        var hasJsonNotePayloads = isPlantUmlBrowser && (
            (trackedLogs is not null && trackedLogs.Any(l => l.PlantUml is not null && (l.PlantUml.Contains("\n{") || l.PlantUml.Contains("\n[")))) ||
            diagrams.Any(d => d.CodeBehind.Contains("\n{") || d.CodeBehind.Contains("\n[")));
        // The JSON/YAML note payload format dropdown, emitted beside the filter
        // toggles at report and scenario level. Built once — the five scenario
        // toolbar variants all use the same string, so they cannot drift. The
        // control is label-free (kept compact deliberately); aria-label/title
        // carry its meaning instead.
        var yamlDefaultSelected = notePayloadFormat == NotePayloadFormat.Yaml;
        var noteFormatOptions = $"<option value=\"json\"{(yamlDefaultSelected ? "" : " selected")}>JSON</option><option value=\"yaml\"{(yamlDefaultSelected ? " selected" : "")}>YAML</option>";
        var reportNoteFormatSelect = hasJsonNotePayloads
            ? $"<select class=\"note-format-select\" autocomplete=\"off\" aria-label=\"Note payload format\" title=\"Note payload format\" onchange=\"window._setNoteFormat(this)\">{noteFormatOptions}</select>"
            : "";
        var scenarioNoteFormatSelect = hasJsonNotePayloads
            ? $"<select class=\"note-format-select\" autocomplete=\"off\" aria-label=\"Note payload format\" title=\"Note payload format\" onchange=\"window._setScenarioNoteFormat(this)\">{noteFormatOptions}</select>"
            : "";
        var plantUmlBrowserScript = isPlantUmlBrowser ? DiagramContextMenu.GetPlantUmlBrowserRenderScript(browserRenderWorkers, browserRenderCacheMegabytes, browserFragmentMaxHeight) : "";
        var collapsibleNotesScript = isPlantUmlBrowser ? DiagramContextMenu.GetCollapsibleNotesScript(notePayloadFormat) : "";
        var collapsibleNotesStyles = isPlantUmlBrowser ? DiagramContextMenu.GetCollapsibleNotesStyles() : "";
        var contextMenuScript = hasInteractiveDiagrams || internalFlowTracking ? DiagramContextMenu.GetContextMenuScript() : "";
        var contextMenuStyles = hasInteractiveDiagrams || internalFlowTracking ? DiagramContextMenu.GetStyles() : "";
        var inlineSvgStyles = (isInlineSvg || isPlantUmlBrowser) ? DiagramContextMenu.GetInlineSvgStyles() : "";
        var internalFlowPopupStyles = internalFlowTracking ? DiagramContextMenu.GetInternalFlowPopupStyles() : "";
        var internalFlowPopupScript = internalFlowTracking ? DiagramContextMenu.GetInternalFlowPopupScript() : "";
        var flameChartRenderScript = internalFlowTracking ? DiagramContextMenu.GetFlameChartRenderScript() : "";
        var toggleScript = internalFlowTracking ? DiagramContextMenu.GetToggleScript() : "";
        var diagramToggleLayoutScript = DiagramContextMenu.GetDiagramToggleLayoutScript();

        var customCssBlock = customCss is not null ? $"<style>{customCss}</style>" : "";
        var faviconLink = $"<link rel=\"icon\" href=\"{customFaviconBase64 ?? Constants.DefaultFavicon.DataUri}\">";

        var enrichSearchDataScript = "";

        var advancedSearchScript = AdvancedSearchJs.Value;

        var html = $$"""
                    <!DOCTYPE html>
                    <html>
                        <head>
                            <meta charset="utf-8" />
                            <meta name="viewport" content="width=device-width, initial-scale=1" />
                            <meta name="generator" content="Kronikol v{{KronikolVersion}}" />
                            <title>{{title}}</title>
                            <style>
                                {{combinedStylesheet}}
                                {{contextMenuStyles}}
                                {{inlineSvgStyles}}
                                {{collapsibleNotesStyles}}
                                {{internalFlowPopupStyles}}
                            </style>
                            {{customCssBlock}}
                            {{faviconLink}}
                            <script>
                                {{decompressHelper}}
                                {{advancedSearchScript}}
                                {{scenarioFeatureMapHelper}}
                                {{toggleHappyPathsFunction}}
                                {{searchFunction}}
                                {{searchIndexClientScript}}
                                {{dependencyFilterFunction}}
                                {{categoryFilterFunction}}
                                {{statusFilterFunction}}
                                {{collapseExpandAllFunction}}
                                {{lightboxFunction}}
                                {{toggleTableRefFunction}}
                                {{sortTableFunction}}
                                {{copyScenarioNameFunction}}
                                {{toggleExamplesDetailFunction}}
                                {{selectRowFunction}}
                                {{toggleFlattenParamsJs}}
                                {{paramExpandJs}}
                                {{toggleTimelineFunction}}
                                {{toggleComponentDiagramFunction}}
                                {{jumpToFailureFunction}}
                                {{durationFilterFunction}}
                                {{exportFunction}}
                                {{persistentFilterFunction}}
                                {{urlHashFunction}}
                                {{keyboardNavigationFunction}}
                                {{initScript}}
                                {{enrichSearchDataScript}}
                            </script>
                            {{plantUmlBrowserScript}}
                            {{collapsibleNotesScript}}
                            {{contextMenuScript}}
                            {{flameChartRenderScript}}
                            {{internalFlowDataScript}}
                            {{internalFlowPopupScript}}
                            {{toggleScript}}
                            {{diagramToggleLayoutScript}}
                        </head>
                        <body>
                    """;

        var body = new StringBuilder();
        if (customLogoHtml is not null)
            body.Append($"<div class=\"custom-logo\">{customLogoHtml}</div>");
        body.Append($"<h1>{title}</h1>");

        if (includeTestRunData)
        {
            var numberOfFeatures = features.Length;
            var scenarios = features.SelectMany(x => x.Scenarios).ToArray();
            var passedScenarios = scenarios.Where(x => x.Result == ExecutionResult.Passed).ToArray();
            var skippedScenarios = scenarios.Where(x => x.Result == ExecutionResult.Skipped).ToArray();
            var failedScenarios = scenarios.Where(x => x.Result == ExecutionResult.Failed).ToArray();
            var overallStatus = failedScenarios.Any() ? "Failed" : "Passed";

            // Feature summary table (collapsible, above execution summary)
            var hasAnySteps = features.Any(f => f.Scenarios.Any(s => s.Steps is { Length: > 0 } || s.BackgroundSteps is { Length: > 0 }));
            var hasAnyDurations = features.Any(f => f.Scenarios.Any(s => s.Duration.HasValue));
            var nextCol = 5;
            body.Append("<details class=\"features-summary-details\"><summary class=\"h2\">Features Summary</summary>");
            body.Append("<div class=\"features-summary-table-wrapper\">");
            body.Append("<table class=\"feature-summary-table\"><thead><tr>");
            body.Append("<th onclick=\"sort_table(0)\">Feature</th>");
            body.Append("<th onclick=\"sort_table(1)\">Scenarios</th>");
            body.Append("<th onclick=\"sort_table(2)\">Passed</th>");
            body.Append("<th onclick=\"sort_table(3)\">Failed</th>");
            body.Append("<th onclick=\"sort_table(4)\">Skipped</th>");
            if (hasAnySteps)
            {
                body.Append($"<th onclick=\"sort_table({nextCol++})\">Steps</th>");
                body.Append($"<th class=\"step-status-header\" onclick=\"sort_table({nextCol++})\">Passed</th>");
                body.Append($"<th class=\"step-status-header\" onclick=\"sort_table({nextCol++})\">Failed</th>");
                body.Append($"<th class=\"step-status-header\" onclick=\"sort_table({nextCol++})\">Skipped</th>");
            }
            if (hasAnyDurations)
            {
                body.Append($"<th onclick=\"sort_table({nextCol++})\">Duration</th>");
                body.Append($"<th onclick=\"sort_table({nextCol++})\">Avg</th>");
                body.Append($"<th onclick=\"sort_table({nextCol})\">Longest</th>");
            }
            body.Append("</tr></thead><tbody>");

            foreach (var feature in features)
            {
                var totalSc = feature.Scenarios.Length;
                var passedSc = feature.Scenarios.Count(s => s.Result == ExecutionResult.Passed);
                var failedSc = feature.Scenarios.Count(s => s.Result == ExecutionResult.Failed);
                var skippedSc = feature.Scenarios.Count(s => s.Result is ExecutionResult.Skipped or ExecutionResult.Bypassed or ExecutionResult.SkippedAfterFailure);
                var featureHasFail = failedSc > 0;

                body.Append($"<tr{(featureHasFail ? " class=\"failed\"" : "")}>");
                body.Append($"<td>{System.Net.WebUtility.HtmlEncode(feature.DisplayName)}</td>");
                body.Append($"<td>{totalSc}</td>");
                body.Append($"<td>{passedSc}</td>");
                body.Append($"<td>{failedSc}</td>");
                body.Append($"<td>{skippedSc}</td>");

                if (hasAnySteps)
                {
                    var allSteps = feature.Scenarios
                        .SelectMany(s => (s.BackgroundSteps ?? []).Concat(s.Steps ?? []))
                        .ToArray();
                    var stepCount = CountStepsRecursive(allSteps);
                    var stepStatusCounts = CountStepsByStatusRecursive(allSteps);
                    body.Append($"<td>{stepCount}</td>");
                    body.Append($"<td>{stepStatusCounts.Passed}</td>");
                    body.Append($"<td>{stepStatusCounts.Failed}</td>");
                    body.Append($"<td>{stepStatusCounts.Skipped}</td>");
                }

                if (hasAnyDurations)
                {
                    var durations = feature.Scenarios.Where(s => s.Duration.HasValue).Select(s => s.Duration!.Value).ToArray();
                    var totalDuration = durations.Length > 0 ? durations.Aggregate(TimeSpan.Zero, (a, b) => a + b) : TimeSpan.Zero;
                    var avgDuration = durations.Length > 0 ? totalDuration / durations.Length : TimeSpan.Zero;
                    var maxDuration = durations.Length > 0 ? durations.Max() : TimeSpan.Zero;
                    body.Append($"<td>{FormatDuration(totalDuration)}</td>");
                    body.Append($"<td>{FormatDuration(avgDuration)}</td>");
                    body.Append($"<td>{FormatDuration(maxDuration)}</td>");
                }

                body.Append("</tr>");
            }

            body.Append("</tbody></table>");
            body.Append("</div>");
            body.Append("</details>");

            body.Append($"""
                    <div class="header-row">
                    <div class="test-execution-summary">
                        <h2>Test Execution Summary</h2>
                        <table>
                            <tr><td colspan="2" class="column-header">Execution</td><td colspan="2" class="column-header">Content</td></tr>
                            <tr><td>Overall status:</td><td>{overallStatus}</td><td>Features: </td><td>{numberOfFeatures}</td></tr>
                            <tr><td>Start Date:</td><td>{startRunTime:yyyy-MM-dd} (UTC)</td><td>Scenarios: </td><td>{scenarios.Length}</td></tr>
                            <tr><td>Start Time:</td><td>{startRunTime:HH:mm:ss} (UTC)</td><td>Passed Scenarios: </td><td>{passedScenarios.Length}</td></tr>
                            <tr><td>End Time:</td><td>{endRunTime:HH:mm:ss} (UTC)</td><td>Failed Scenarios: </td><td>{failedScenarios.Length}</td></tr>
                            <tr><td>Duration:</td><td>{FormatDuration(endRunTime - startRunTime)}</td><td>Skipped Scenarios: </td><td>{skippedScenarios.Length}</td></tr>
                            <tr style="display:none"><td>Kronikol Version:</td><td>{KronikolVersion}</td><td></td><td></td></tr>
                        </table>
                    </div>
                    """);

            if (ciMetadata is not null)
            {
                body.Append("<div class=\"ci-chart-group\">");
                body.Append("<div class=\"ci-metadata\"><table>");
                body.Append($"<tr><td colspan=\"2\" class=\"column-header\">CI ({ciMetadata.Provider})</td></tr>");
                if (ciMetadata.BuildNumber is not null)
                    body.Append($"<tr><td>Build #:</td><td>{System.Net.WebUtility.HtmlEncode(ciMetadata.BuildNumber)}</td></tr>");
                if (ciMetadata.Branch is not null)
                    body.Append($"<tr><td>Branch:</td><td>{System.Net.WebUtility.HtmlEncode(ciMetadata.Branch)}</td></tr>");
                if (ciMetadata.CommitSha is not null)
                {
                    var shortSha = ciMetadata.CommitSha.Length > 7 ? ciMetadata.CommitSha[..7] : ciMetadata.CommitSha;
                    body.Append($"<tr><td>Commit:</td><td><code title=\"{System.Net.WebUtility.HtmlEncode(ciMetadata.CommitSha)}\">{System.Net.WebUtility.HtmlEncode(shortSha)}</code></td></tr>");
                }
                if (ciMetadata.PipelineUrl is not null)
                    body.Append($"<tr><td>Pipeline:</td><td><a href=\"{System.Net.WebUtility.HtmlEncode(ciMetadata.PipelineUrl)}\" target=\"_blank\" rel=\"noopener noreferrer\">View Run</a></td></tr>");
                if (ciMetadata.Repository is not null)
                    body.Append($"<tr><td>Repository:</td><td>{System.Net.WebUtility.HtmlEncode(ciMetadata.Repository)}</td></tr>");
                body.Append("</table></div>");
            }

            var bypassedScenarios = scenarios.Where(x => x.Result == ExecutionResult.Bypassed).ToArray();
            body.Append(GeneratePieChartSvg(passedScenarios.Length, failedScenarios.Length, skippedScenarios.Length, bypassedScenarios.Length));

            if (ciMetadata is not null)
                body.Append("</div>"); // close ci-chart-group
        }

        var diagramsByTestId = diagrams.ToLookup(x => x.TestRuntimeId);

        // Extract dependencies and search terms per scenario from diagram source code
        var scenarioDependencies = new Dictionary<string, HashSet<string>>();
        var scenarioDiagramSearchTerms = new Dictionary<string, HashSet<string>>();
        var allDependencies = new HashSet<string>();
        foreach (var feature in features)
        foreach (var scenario in feature.Scenarios)
        {
            var deps = new HashSet<string>();
            var searchTerms = new HashSet<string>();
            foreach (var diagram in diagramsByTestId[scenario.Id])
            {
                foreach (var dep in ExtractDependencies(diagram.CodeBehind, diagramFormat))
                    deps.Add(dep);
                foreach (var term in ExtractDiagramSearchTerms(diagram.CodeBehind))
                    searchTerms.Add(term);
            }
            scenarioDependencies[scenario.Id] = deps;
            scenarioDiagramSearchTerms[scenario.Id] = searchTerms;
            foreach (var d in deps) allDependencies.Add(d);
        }

        body.Append($"""
                 <div class="filtering-box">
                    <div class="filtering-box-header"><h2>Filtering</h2><div class="filtering-box-export"><button class="export-btn" onclick="clear_all_filters()">Clear All</button><button class="export-btn" onclick="export_html()">Export Filtered HTML</button><button class="export-btn" onclick="export_csv()">Export Filtered CSV</button></div></div>
                    <div class="filter-search"><input id="searchbar" autocomplete="off" placeholder="Search... (@tag, $status, &&, ||, !!, parentheses)" onkeyup="search_scenarios()" /><button type="button" class="search-help-toggle" onclick="toggle_search_help()" title="Search syntax help">?</button></div>
                    <div class="mobile-filter-toggle">Filters</div>
                    <div class="filters">
                    <div class="search-help-panel" style="display:none">
                    <table class="search-help-table">
                    <tr><th>Syntax</th><th>Meaning</th><th>Example</th></tr>
                    <tr><td><code>word</code></td><td>Text search (feature name, scenario name, step text, tags, diagram source)</td><td><code>order</code></td></tr>
                    <tr><td><code>"phrase"</code></td><td>Exact phrase match</td><td><code>"create order"</code></td></tr>
                    <tr><td><code>&&</code></td><td>AND — both sides must match</td><td><code>order && create</code></td></tr>
                    <tr><td><code>||</code></td><td>OR — either side must match</td><td><code>payment || order</code></td></tr>
                    <tr><td><code>!!</code></td><td>NOT — excludes matches</td><td><code>order && !!delete</code></td></tr>
                    <tr><td><code>( )</code></td><td>Parentheses — group expressions</td><td><code>(a || b) && c</code></td></tr>
                    <tr><td><code>@tag</code></td><td>Filter by tag / category</td><td><code>@smoke && @api</code></td></tr>
                    <tr><td><code>$status</code></td><td>Filter by status</td><td><code>$failed</code>, <code>$passed</code>, <code>$skipped</code></td></tr>
                    </table>
                    <p class="search-help-note">Space-separated words use implicit AND. Press <kbd>/</kbd> to focus the search bar. Operators <code>&&</code> <code>||</code> <code>!!</code> activate advanced mode; without them, legacy tag expressions (<code>@a and @b or not @c</code>) are also supported.</p>
                    </div>
                    <div class="filter-row">
                 """);

        // Status filter toggles
        {
            body.Append("""<div class="status-filters"><span class="status-filters-label">Status:</span>""");
            foreach (var status in Enum.GetValues<ExecutionResult>().OrderBy(s => s))
            {
                if (status == ExecutionResult.SkippedAfterFailure) continue;
                var statusName = status.ToString();
                body.Append($"""<button class="status-toggle" data-status="{statusName}" onclick="toggle_status(this)">{statusName}</button>""");
            }
            body.Append("</div>");
        }

        body.Append("""
                    <div class="happy-path-filters"><span class="happy-path-filters-label">Happy Paths:</span><button class="happy-path-toggle" onclick="toggle_happy_paths(this)">Happy Paths Only</button></div>
                 """);

        body.Append("</div>"); // close filter-row

        // Duration filter (only shown when scenarios have duration data)
        if (hasDurations)
        {
            var durationsMs = features.SelectMany(f => f.Scenarios)
                .Where(s => s.Duration.HasValue)
                .Select(s => s.Duration!.Value.TotalMilliseconds)
                .OrderBy(d => d)
                .ToArray();
            var p50Ms = durationsMs.Length > 0 ? durationsMs[(int)(durationsMs.Length * 0.50)] : 0;
            var p90Ms = durationsMs.Length > 0 ? durationsMs[(int)(durationsMs.Length * 0.90)] : 0;
            var p95Ms = durationsMs.Length > 0 ? durationsMs[(int)(durationsMs.Length * 0.95)] : 0;
            var p99Ms = durationsMs.Length > 0 ? durationsMs[(int)(durationsMs.Length * 0.99)] : 0;

            body.Append($"""<div class="duration-filters" data-p50="{p50Ms:F0}" data-p90="{p90Ms:F0}" data-p95="{p95Ms:F0}" data-p99="{p99Ms:F0}"><span class="duration-filters-label">Duration ≥:</span><button class="percentile-btn" data-threshold-ms="{p50Ms:F0}" onclick="set_percentile(this)">P50 ({FormatDurationBadge(TimeSpan.FromMilliseconds(p50Ms))})</button><button class="percentile-btn" data-threshold-ms="{p90Ms:F0}" onclick="set_percentile(this)">P90 ({FormatDurationBadge(TimeSpan.FromMilliseconds(p90Ms))})</button><button class="percentile-btn" data-threshold-ms="{p95Ms:F0}" onclick="set_percentile(this)">P95 ({FormatDurationBadge(TimeSpan.FromMilliseconds(p95Ms))})</button><button class="percentile-btn" data-threshold-ms="{p99Ms:F0}" onclick="set_percentile(this)">P99 ({FormatDurationBadge(TimeSpan.FromMilliseconds(p99Ms))})</button><button class="percentile-btn" data-custom="1" onclick="set_percentile(this)">Custom</button><span id="custom-duration-wrap" style="display:none;align-items:center;gap:0.3em"><input id="duration-threshold" autocomplete="off" type="number" step="0.1" min="0" placeholder="seconds" onchange="filter_duration()" /><span class="duration-filters-unit">seconds</span></span></div>""");
        }

        if (allDependencies.Count > 0)
        {
            body.Append("""<div class="dependency-filters"><span class="dependency-filters-label">Dependencies:</span><button class="dep-mode-toggle" title="AND: show scenarios matching ALL selected dependencies. OR: show scenarios matching ANY selected dependency. Click to toggle." onclick="toggle_dep_mode(this)">AND</button>""");
            foreach (var dep in allDependencies.OrderBy(d => d))
            {
                body.Append($"""<button class="dependency-toggle" data-dependency="{System.Net.WebUtility.HtmlEncode(dep)}" onclick="toggle_dependency(this)">{System.Net.WebUtility.HtmlEncode(dep)}</button>""");
            }
            body.Append("</div>");
        }

        // Category filter (only shown when scenarios have category data)
        var allCategories = features.SelectMany(f => f.Scenarios)
            .Where(s => s.Categories is { Length: > 0 })
            .SelectMany(s => s.Categories!)
            .Distinct()
            .OrderBy(c => c)
            .ToArray();
        if (allCategories.Length > 0)
        {
            body.Append("""<div class="category-filters"><span class="category-filters-label">Categories:</span><button class="cat-mode-toggle" title="OR: show scenarios matching ANY selected category. AND: show scenarios matching ALL selected categories. Click to toggle." onclick="toggle_cat_mode(this)">OR</button>""");
            body.Append("""<button class="category-toggle category-active" data-category="" onclick="toggle_category(this)">All</button>""");
            foreach (var cat in allCategories)
            {
                body.Append($"""<button class="category-toggle" data-category="{System.Net.WebUtility.HtmlEncode(cat)}" onclick="toggle_category(this)">{System.Net.WebUtility.HtmlEncode(cat)}</button>""");
            }
            body.Append("""<button class="category-toggle" data-category="__uncategorized__" onclick="toggle_category(this)">Uncategorized</button>""");
            body.Append("</div>");
        }

        body.Append("</div>"); // close filters
        body.Append("</div>"); // close filtering-box
        if (includeTestRunData)
            body.Append("</div>"); // close header-row

        // Toolbar row: expand buttons left, Details/Headers right
        body.Append("""<div class="toolbar-row">""");
        body.Append("""<div class="toolbar-left"><button class="collapse-expand-all" onclick="toggle_expand_collapse(this, 'details.feature', 'Expand All Features', 'Collapse All Features')">Expand All Features</button><button class="collapse-expand-all" onclick="toggle_expand_collapse(this, 'details.scenario', 'Expand All Scenarios', 'Collapse All Scenarios')">Expand All Scenarios</button>""");
        if (hasDurations)
            body.Append("""<button class="timeline-toggle" onclick="toggle_timeline(this)">Scenario Timeline</button>""");
        if (!string.IsNullOrEmpty(componentDiagramPlantUml))
            body.Append("""<button class="timeline-toggle" onclick="toggle_component_diagram(this)">Component Diagram</button>""");
        body.Append("</div>");
        body.Append("""<div class="toolbar-right">""");
        if (isPlantUmlBrowser)
        {
            body.Append("""<span class="details-radio"><span class="details-radio-label">Details:</span><button class="details-radio-btn" data-state="expanded" onclick="window._setReportDetails('expanded')">Expand</button><button class="details-radio-btn" data-state="collapsed" onclick="window._setReportDetails('collapsed')">Collapse</button><button class="details-radio-btn details-active" data-state="truncated" onclick="window._setReportDetails('truncated')">Truncate</button><select class="truncate-lines-select" autocomplete="off" onchange="window._setTruncateLines(this)"><option value="3">3</option><option value="4">4</option><option value="5">5</option><option value="10">10</option><option value="15">15</option><option value="20">20</option><option value="25">25</option><option value="30">30</option><option value="35">35</option><option value="40" selected>40</option><option value="50">50</option><option value="60">60</option><option value="80">80</option><option value="100">100</option></select><span class="truncate-lines-label">lines</span></span>""");
            body.Append("""<button class="details-radio-btn toggle-btn details-active" data-toggle="headers" data-shown="true" onclick="window._toggleHeaders(this)">Headers Shown</button>""");
            if (hasAssertionNotes)
                body.Append("""<button class="details-radio-btn toggle-btn" data-toggle="assertions" data-shown="false" onclick="window._toggleAssertions(this)">Assertions Hidden</button>""");
            if (hasStepDelimiters)
                body.Append("""<button class="details-radio-btn toggle-btn details-active" data-toggle="steps" data-shown="true" onclick="window._toggleSteps(this)">Steps Shown</button>""");
            if (hasDatabaseParticipants)
                body.Append("""<button class="details-radio-btn toggle-btn details-active" data-toggle="databases" data-shown="true" onclick="window._toggleDatabases(this)">Databases Shown</button>""");
            body.Append(reportNoteFormatSelect);
        }
        body.Append("</div>");
        body.Append("</div>");

        var plantUmlBrowserCounter = 0;
        var diagramDataMap = new Dictionary<string, string>();

        // Pre-compute median span count for outlier detection
        var medianSpanCount = 0;
        if (precomputedWholeTestContent is { Count: > 0 })
        {
            var spanCounts = precomputedWholeTestContent.Values
                .Where(f => f.SpanCount > 0)
                .Select(f => f.SpanCount)
                .OrderBy(c => c)
                .ToArray();
            if (spanCounts.Length > 0)
                medianSpanCount = spanCounts[(spanCounts.Length - 1) / 2];
        }
        else if (wholeTestSegments is not null && wholeTestSegments.Count > 0)
        {
            var spanCounts = wholeTestSegments.Values
                .Where(s => s.Spans.Length > 0)
                .Select(s => s.Spans.Length)
                .OrderBy(c => c)
                .ToArray();
            if (spanCounts.Length > 0)
                medianSpanCount = spanCounts[(spanCounts.Length - 1) / 2];
        }

        // Failure clusters
        var allScenarios = features.SelectMany(f => f.Scenarios).ToArray();

        // Pre-compute unique anchor IDs for all scenarios (handle duplicate display names)
        var scenarioAnchorIds = new Dictionary<string, string>();
        var anchorIdCounts = new Dictionary<string, int>();
        foreach (var scenario in allScenarios)
        {
            var baseAnchor = GenerateScenarioAnchorId(scenario.DisplayName);
            if (anchorIdCounts.TryGetValue(baseAnchor, out var count))
            {
                anchorIdCounts[baseAnchor] = count + 1;
                scenarioAnchorIds[scenario.Id] = $"{baseAnchor}-{count + 1}";
            }
            else
            {
                anchorIdCounts[baseAnchor] = 1;
                scenarioAnchorIds[scenario.Id] = baseAnchor;
            }
        }

        // Deep-search index (SEARCH_INDEX_PLAN): corpus pieces are collected at the exact emission
        // sites of the client-readable surfaces (data-search, puml-data / raw-plantuml source,
        // whole-test-flow attributes) so the index and the client verify pass can never drift.
        // The heavy trigram hashing of diagram sources is prewarmed on the thread pool so it
        // overlaps HTML body building, and is shared across both HTML reports via the cache.
        var buildSearchIndex = fullSearchIndex && allScenarios.Length > 0;
        var searchIndexPieces = buildSearchIndex ? new Dictionary<string, List<string>>() : null;
        if (buildSearchIndex)
        {
            searchIndexCache ??= new SearchIndex.SearchIndexBuildCache();
            searchIndexCache.StartPrewarm(diagrams.Select(d => d.CodeBehind));
            foreach (var s in allScenarios) searchIndexPieces![s.Id] = [];
        }

        var clusters = FailureClusterer.Cluster(allScenarios);
        if (clusters.Length > 0)
        {
            // Build scenario-to-feature lookup for display
            var scenarioFeatureLookup = new Dictionary<string, string>();
            foreach (var feature in features)
            foreach (var scenario in feature.Scenarios)
                scenarioFeatureLookup[scenario.Id] = feature.DisplayName;

            body.Append("<details class=\"failure-clusters\" open>");
            body.Append($"<summary>Failure Clusters ({clusters.Length} root cause{(clusters.Length == 1 ? "" : "s")})</summary>");
            foreach (var cluster in clusters)
            {
                var anchorLinks = string.Join("", cluster.Scenarios.Select(s =>
                {
                    var anchorId = scenarioAnchorIds[s.Id];
                    var featureName = scenarioFeatureLookup.GetValueOrDefault(s.Id, "");
                    var prefix = featureName.Length > 0 ? $"<span style=\"color:rgb(100,100,100);font-size:0.85em\">{System.Net.WebUtility.HtmlEncode(featureName)} &rsaquo;</span> " : "";
                    return $"<li>{prefix}<a class=\"failure-cluster-scenario-link\" href=\"#{anchorId}\" onclick=\"event.preventDefault();var el=document.getElementById('{anchorId}');if(el){{var p=el;while(p){{if(p.tagName==='DETAILS')p.setAttribute('open','');p=p.parentElement;}}if(el.tagName==='TR')el.click();else el.setAttribute('open','');el.scrollIntoView({{behavior:'smooth',block:'start'}});history.replaceState(null,'',location.pathname+location.search+'#{anchorId}');}}\">{System.Net.WebUtility.HtmlEncode(s.DisplayName)}</a></li>";
                }));
                body.Append($"<details class=\"failure-cluster\"><summary>{System.Net.WebUtility.HtmlEncode(cluster.ClusterKey)}<span class=\"failure-cluster-count\">{cluster.Scenarios.Length} scenarios</span></summary>");
                body.Append($"<ul class=\"failure-cluster-scenarios\">{anchorLinks}</ul></details>");
            }
            body.Append("</details>");
        }

        if (includeTestRunData && diagnostics is { Count: > 0 })
            body.Append(RenderReportDiagnostics(diagnostics));

        // Scenario timeline / Gantt (hidden by default)
        if (hasDurations)
        {
            var timelineScenarios = features
                .SelectMany(f => f.Scenarios.Select(s => (Feature: f.DisplayName, Scenario: s)))
                .Where(x => x.Scenario.Duration.HasValue)
                .OrderByDescending(x => x.Scenario.Duration!.Value)
                .ToArray();

            if (timelineScenarios.Length > 0)
            {
                var maxDuration = timelineScenarios.Max(x => x.Scenario.Duration!.Value.TotalMilliseconds);
                body.Append("<div id=\"scenario-timeline\" class=\"scenario-timeline\" style=\"display:none\">");
                body.Append("<div class=\"timeline-header\">Scenario Timeline <span class=\"timeline-info\" title=\"The Scenario Timeline shows every test scenario ordered by duration (longest first). Each bar is proportional to the scenario's elapsed time, colour-coded by result: green = passed, red = failed, yellow = skipped. Use it to quickly spot slow tests, compare relative durations, and identify performance outliers across the entire test run.\">&#x1F6C8;</span></div>");
                foreach (var (featureName, scenario) in timelineScenarios)
                {
                    var durationMs = scenario.Duration!.Value.TotalMilliseconds;
                    var widthPercent = maxDuration > 0 ? (durationMs / maxDuration * 100) : 0;
                    var statusClass = scenario.Result switch
                    {
                        ExecutionResult.Failed => "timeline-bar-failed",
                        ExecutionResult.Skipped or ExecutionResult.SkippedAfterFailure => "timeline-bar-skipped",
                        ExecutionResult.Bypassed => "timeline-bar-bypassed",
                        _ => "timeline-bar-passed"
                    };
                    body.Append($"<div class=\"timeline-row\">");
                    body.Append($"<div class=\"timeline-label\" title=\"{System.Net.WebUtility.HtmlEncode(scenario.DisplayName)}\">{System.Net.WebUtility.HtmlEncode(scenario.DisplayName)}</div>");
                    body.Append($"<div class=\"timeline-track\"><div class=\"timeline-bar {statusClass}\" style=\"width:{widthPercent:F1}%\" title=\"{FormatDurationBadge(scenario.Duration.Value)}\"></div></div>");
                    body.Append($"<div class=\"timeline-duration\">{FormatDurationBadge(scenario.Duration.Value)}</div>");
                    body.Append("</div>");
                }
                body.Append("</div>");
            }
        }

        // Embedded component diagram
        if (!string.IsNullOrEmpty(componentDiagramPlantUml))
        {
            var compDiagramId = $"puml-{plantUmlBrowserCounter++}";
            var compDiagramCompressed = InternalFlowHtmlGenerator.CompressToBase64(componentDiagramPlantUml);
            diagramDataMap[compDiagramId] = compDiagramCompressed;
            body.Append($"""<div id="component-diagram" class="component-diagram-section" style="display:none"><div class="plantuml-browser" id="{compDiagramId}" data-diagram-type="plantuml"></div></div>""");
        }

        body.Append("<div id=\"report-content\">");
        var paramGroupCounter = 0;
        foreach (var feature in features)
        {
            var featureHasFailures = feature.Scenarios.Any(s => s.Result == ExecutionResult.Failed);
            var featureAllSkipped = !featureHasFailures && feature.Scenarios.All(s => s.Result == ExecutionResult.Skipped);
            body.Append($"""
                     <details class="feature">
                        <summary class="h2{(featureHasFailures ? " failed" : featureAllSkipped ? " skipped" : "")}">{feature.DisplayName}{(feature.Endpoint is null ? "" : $" <div class=\"endpoint\">{System.Net.WebUtility.HtmlEncode(feature.Endpoint)}</div>")}{(feature.Labels is { Length: > 0 } fl ? string.Concat(fl.Select(l => $" <span class=\"label\">{System.Net.WebUtility.HtmlEncode(l)}</span>")) : "")}</summary>
                     """);

            if (feature.Description is not null)
            {
                body.Append($"""<div class="feature-description">{System.Net.WebUtility.HtmlEncode(feature.Description)}</div>""");
            }

            var orderedScenarios = feature.Scenarios.OrderByDescending(x => x.IsHappyPath).ThenBy(x => x.DisplayName).ToArray();

            // Group parameterized scenarios using ParameterGrouper
            Func<Scenario[], bool> diagramComparer = groupScenarios =>
            {
                if (groupScenarios.Length < 2) return false;
                var firstDiags = diagramsByTestId[groupScenarios[0].Id].Select(d => d.CodeBehind).OrderBy(s => s).ToArray();
                if (firstDiags.Length == 0) return false;
                for (var gi = 1; gi < groupScenarios.Length; gi++)
                {
                    var thisDiags = diagramsByTestId[groupScenarios[gi].Id].Select(d => d.CodeBehind).OrderBy(s => s).ToArray();
                    if (!firstDiags.SequenceEqual(thisDiags)) return false;
                }
                return true;
            };
            var (paramGroups, _) = ParameterGrouper.Analyze(orderedScenarios, groupParameterizedTests, maxParameterColumns, diagramComparer);

            // Build lookup from scenario ID → group for first-encounter rendering
            var scenarioToGroup = new Dictionary<string, ParameterizedGroup>();
            var renderedGroupKeys = new HashSet<string>();
            foreach (var pg in paramGroups)
                foreach (var s in pg.Scenarios)
                    scenarioToGroup[s.Id] = pg;

            // Group by Rule for rendering
            string? currentRule = "__NOTSET__";
            var ruleOpen = false;
            foreach (var scenario in orderedScenarios)
            {
                // Is this scenario part of a parameterized group?
                ParameterizedGroup? group = null;
                string? groupKey = null;
                if (scenarioToGroup.TryGetValue(scenario.Id, out var g))
                {
                    groupKey = g.GroupDisplayName + "|" + string.Join(",", g.Scenarios.Select(s => s.Id));
                    if (renderedGroupKeys.Contains(groupKey))
                        continue;
                    group = g;
                }

                if (scenario.Rule != currentRule)
                {
                    if (ruleOpen)
                    {
                        body.Append("</details>"); // close previous rule
                    }
                    currentRule = scenario.Rule;
                    if (currentRule is not null)
                    {
                        body.Append($"<details class=\"rule\" open><summary class=\"h2-5\">{System.Net.WebUtility.HtmlEncode(currentRule)}</summary>");
                        ruleOpen = true;
                    }
                    else
                    {
                        ruleOpen = false;
                    }
                }

                // Render parameterized group
                if (group is not null)
                {
                    renderedGroupKeys.Add(groupKey!);
                    var groupPrefix = $"pgrp{paramGroupCounter++}";
                    RenderParameterizedGroup(body, group, groupPrefix, diagramsByTestId, scenarioDependencies,
                        scenarioDiagramSearchTerms,
                        showStepNumbers, isPlantUmlBrowser, isInlineSvg, lazyLoadImages,
                        ref plantUmlBrowserCounter, diagramDataMap, wholeTestSegments, trackedLogs, wholeTestVisualization, medianSpanCount,
                        titleizeParameterNames,
                        hasAssertionNotes: hasAssertionNotes,
                        hasStepDelimiters: hasStepDelimiters,
                        hasDatabaseParticipants: hasDatabaseParticipants,
                        showNoInteractionsMarker: showNoInteractionsMarker,
                        scenarioAnchorIds: scenarioAnchorIds,
                        featureDisplayName: feature.DisplayName,
                        featureDescription: feature.Description,
                        featureEndpoint: feature.Endpoint,
                        featureLabels: feature.Labels,
                        precomputedWholeTestContent: precomputedWholeTestContent,
                        separateBackgroundSteps: separateBackgroundSteps,
                        collapseRepeatedStepKeywords: collapseRepeatedStepKeywords,
                        scenarioNoteFormatSelect: scenarioNoteFormatSelect,
                        searchIndexPieces: searchIndexPieces);
                    continue;
                }

                var failed = scenario.Result == ExecutionResult.Failed;
                var skipped = scenario.Result == ExecutionResult.Skipped;
                var depsAttr = scenarioDependencies.TryGetValue(scenario.Id, out var deps) && deps.Count > 0
                    ? $" data-dependencies=\"{System.Net.WebUtility.HtmlEncode(string.Join(",", deps.OrderBy(d => d)))}\""
                    : "";
                var statusAttr = $" data-status=\"{scenario.Result}\"";

                // Duration attributes and badge
                var durationAttr = "";
                var durationBadge = "";
                if (scenario.Duration.HasValue)
                {
                    var durationMs = scenario.Duration.Value.TotalMilliseconds;
                    durationAttr = $" data-duration-ms=\"{durationMs:F0}\"";
                    var durationClass = durationMs < 2000 ? "duration-fast" : durationMs < 5000 ? "duration-moderate" : "duration-slow";
                    durationBadge = $" <span class=\"duration-badge {durationClass}\">{FormatDurationBadge(scenario.Duration.Value)}</span>";
                }

                // Deep link anchor ID
                var anchorId = scenarioAnchorIds[scenario.Id];

                // Pre-build searchable text: feature context + scenario name + error info + step text + diagram sources + tags
                var searchParts = new List<string> { feature.DisplayName, scenario.DisplayName };
                if (feature.Description is not null) searchParts.Add(feature.Description);
                if (feature.Endpoint is not null) searchParts.Add(feature.Endpoint);
                if (!string.IsNullOrWhiteSpace(scenario.Description)) searchParts.Add(scenario.Description);
                if (scenario.Rule is not null) searchParts.Add(scenario.Rule);
                if (feature.Labels is { Length: > 0 }) searchParts.AddRange(feature.Labels);
                if (scenario.Categories is { Length: > 0 }) searchParts.AddRange(scenario.Categories);
                if (scenario.Labels is { Length: > 0 }) searchParts.AddRange(scenario.Labels);
                if (failed && scenario.ErrorMessage is not null) searchParts.Add(scenario.ErrorMessage);
                CollectStepText(scenario.BackgroundSteps, searchParts);
                CollectStepText(scenario.Steps, searchParts);
                if (scenarioDiagramSearchTerms.TryGetValue(scenario.Id, out var diagramTerms) && diagramTerms.Count > 0)
                    searchParts.AddRange(diagramTerms);
                AddExampleValueSearchParts(scenario, searchParts);
                var searchText = string.Join(" ", searchParts).ToLowerInvariant();
                var searchAttr = $" data-search=\"{System.Net.WebUtility.HtmlEncode(searchText)}\"";
                searchIndexPieces?[scenario.Id].Add(searchText);

                var categoriesAttr = scenario.Categories is { Length: > 0 }
                    ? $" data-categories=\"{System.Net.WebUtility.HtmlEncode(string.Join(",", scenario.Categories))}\""
                    : "";

                var labelsAttr = scenario.Labels is { Length: > 0 }
                    ? $" data-labels=\"{System.Net.WebUtility.HtmlEncode(string.Join(",", scenario.Labels))}\""
                    : "";

                var encodedName = System.Net.WebUtility.HtmlEncode(scenario.DisplayName);
                var scenarioLabelsHtml = scenario.Labels is { Length: > 0 }
                    ? string.Concat(scenario.Labels
                        .Where(l => !scenario.IsHappyPath || !l.Equals("Happy Path", StringComparison.OrdinalIgnoreCase))
                        .Select(l => $" <span class=\"label\">{System.Net.WebUtility.HtmlEncode(l)}</span>"))
                    : "";

                var scenarioTooltip = scenario.Result switch
                {
                    ExecutionResult.Passed => "Passed — all assertions passed",
                    ExecutionResult.Failed => "Failed — an assertion or runtime failure occurred",
                    ExecutionResult.Skipped => "Skipped — either the entire test did not run (e.g. a skip attribute or filter excluded it), or a step was skipped at runtime which also prevented all subsequent steps from executing",
                    ExecutionResult.Bypassed => "Bypassed — some or all of the logic in a step was intentionally skipped over at runtime without preventing execution of subsequent steps",
                    ExecutionResult.SkippedAfterFailure => "Skipped after failure — this scenario was never reached because an earlier step failed",
                    _ => ""
                };

                body.Append($"""
                         <details class="scenario{(scenario.IsHappyPath ? " happy-path" : "")}"{depsAttr}{statusAttr}{searchAttr}{durationAttr}{categoriesAttr}{labelsAttr} id="{anchorId}" tabindex="0">
                            <summary class="h3{(failed ? " failed" : skipped ? " skipped" : "")}" title="{scenarioTooltip}">{scenario.DisplayName}{(scenario.IsHappyPath ? " <span class=\"label\">Happy Path</span>" : "")}{scenarioLabelsHtml}{durationBadge}<button class="copy-scenario-name" title="Copy scenario name" data-scenario-name="{encodedName}" onclick="copy_scenario_name(this, event)">&#128203;</button><a class="scenario-link" href="#{anchorId}" title="Link to this scenario" onclick="event.stopPropagation()">&#128279;</a></summary>
                         """);

                if (failed)
                {
                    var diffHtml = "";
                    var diffResult = ErrorDiffParser.TryParseExpectedActual(scenario.ErrorMessage);
                    if (diffResult is not null)
                        diffHtml = ErrorDiffParser.GenerateDiffHtml(diffResult.Expected, diffResult.Actual);

                    // Message and trace are HTML-encoded: "<null>" in an assertion message would
                    // otherwise parse as an unknown tag and vanish from the rendered text (and
                    // break the textContent round-trip the deep-search verify reads through).
                    body.Append($"""
                              <details class="failure-result" open>
                                 <summary class="h4">Failure Result</summary>
                                 <pre>
                              Failure Cause: {System.Net.WebUtility.HtmlEncode(scenario.ErrorMessage)}

                              {System.Net.WebUtility.HtmlEncode(scenario.ErrorStackTrace)}
                                 </pre>
                                 {diffHtml}
                              </details>
                              """);
                    // Stack traces are index-only (never data-search): frame tokens are too
                    // high-frequency for the instant search, but deep search still finds them —
                    // the client verify reads the .failure-result pre textContent back.
                    if (scenario.ErrorStackTrace is not null)
                        searchIndexPieces?[scenario.Id].Add(scenario.ErrorStackTrace);
                }

                if (!string.IsNullOrWhiteSpace(scenario.Description))
                    body.Append($"""<div class="scenario-description">{System.Net.WebUtility.HtmlEncode(scenario.Description)}</div>""");

                RenderScenarioStepSections(body, scenario, showStepNumbers, separateBackgroundSteps, collapseRepeatedStepKeywords);

                if (scenario.Attachments is { Length: > 0 })
                {
                    body.Append("""<div class="scenario-attachments">""");
                    foreach (var attachment in scenario.Attachments)
                    {
                        if (attachment.IsInlineImage)
                        {
                            body.Append($"<a class=\"attachment-image-link\" href=\"{System.Net.WebUtility.HtmlEncode(attachment.RelativePath)}\" target=\"_blank\"><img class=\"attachment-image\" src=\"{System.Net.WebUtility.HtmlEncode(attachment.RelativePath)}\" alt=\"{System.Net.WebUtility.HtmlEncode(attachment.Name)}\" /></a>");
                        }
                        else
                        {
                            body.Append($"<a class=\"step-attachment\" href=\"{System.Net.WebUtility.HtmlEncode(attachment.RelativePath)}\">{System.Net.WebUtility.HtmlEncode(attachment.Name)}</a>");
                        }
                    }
                    body.Append("</div>");
                }

                var diagramsForTest = diagramsByTestId[scenario.Id].ToArray();

                // Get whole-test-flow content (activity + flame) if available
                var wholeTestContent = ResolveWholeTestFlowContent(
                    scenario.Id, precomputedWholeTestContent, wholeTestSegments, trackedLogs, wholeTestVisualization, diagramDataMap);

                var hasSequenceDiagrams = diagramsForTest.Length > 0;
                var hasWholeTestFlow = wholeTestContent is not null;

                // Span count warning for outliers (>= 10x median AND > 100 spans)
                var spanWarning = "";
                if (hasWholeTestFlow && medianSpanCount > 0 && wholeTestContent!.Value.SpanCount >= medianSpanCount * 10 && wholeTestContent.Value.SpanCount > 100)
                {
                    var count = wholeTestContent.Value.SpanCount;
                    spanWarning = $"<span class=\"span-count-warning\">(Warning: {count:N0} spans. This might indicate a problem/recursive loop in your test.)</span>";
                }

                if (hasSequenceDiagrams || hasWholeTestFlow)
                {
                    body.Append("<details class=\"example-diagrams\" open>");

                    if (hasWholeTestFlow && hasSequenceDiagrams)
                    {
                        body.Append("<summary class=\"h4\">Diagrams</summary>");
                        body.Append("<div class=\"diagram-toggle\">");
                        body.Append("<button class=\"diagram-toggle-btn diagram-toggle-active\" data-dtype=\"seq\">Sequence Diagrams</button>");
                        if (!string.IsNullOrEmpty(wholeTestContent!.Value.ActivityHtml))
                            body.Append("<button class=\"diagram-toggle-btn\" data-dtype=\"activity\">Activity Diagrams</button>");
                        if (!string.IsNullOrEmpty(wholeTestContent!.Value.FlameHtml))
                            body.Append("<button class=\"diagram-toggle-btn\" data-dtype=\"flame\">Flame Chart</button>");
                        body.Append(spanWarning);
                        if (isPlantUmlBrowser)
                            body.Append("<span class=\"diagram-toggle-spacer\"></span><span class=\"details-radio\"><span class=\"details-radio-label\">Details:</span><button class=\"details-radio-btn\" data-state=\"expanded\" onclick=\"window._setAllNotes(this,'expanded')\">Expand</button><button class=\"details-radio-btn\" data-state=\"collapsed\" onclick=\"window._setAllNotes(this,'collapsed')\">Collapse</button><button class=\"details-radio-btn details-active\" data-state=\"truncated\" onclick=\"window._setAllNotes(this,'truncated')\">Truncate</button><select class=\"truncate-lines-select\" autocomplete=\"off\" onchange=\"window._setScenarioTruncateLines(this)\"><option value=\"3\">3</option><option value=\"4\">4</option><option value=\"5\">5</option><option value=\"10\">10</option><option value=\"15\">15</option><option value=\"20\">20</option><option value=\"25\">25</option><option value=\"30\">30</option><option value=\"35\">35</option><option value=\"40\" selected>40</option><option value=\"50\">50</option><option value=\"60\">60</option><option value=\"80\">80</option><option value=\"100\">100</option></select><span class=\"truncate-lines-label\">lines</span></span><button class=\"details-radio-btn toggle-btn details-active\" data-toggle=\"headers\" data-shown=\"true\" onclick=\"window._toggleScenarioHeaders(this)\">Headers Shown</button>");
                        if (hasAssertionNotes)
                            body.Append("<button class=\"details-radio-btn toggle-btn\" data-toggle=\"assertions\" data-shown=\"false\" onclick=\"window._toggleScenarioAssertions(this)\">Assertions Hidden</button>");
                        if (hasStepDelimiters)
                            body.Append("<button class=\"details-radio-btn toggle-btn details-active\" data-toggle=\"steps\" data-shown=\"true\" onclick=\"window._toggleScenarioSteps(this)\">Steps Shown</button>");
                        if (hasDatabaseParticipants)
                            body.Append("<button class=\"details-radio-btn toggle-btn details-active\" data-toggle=\"databases\" data-shown=\"true\" onclick=\"window._toggleScenarioDatabases(this)\">Databases Shown</button>");
                        body.Append(scenarioNoteFormatSelect);
                        body.Append("</div>");
                    }
                    else if (hasSequenceDiagrams)
                    {
                        body.Append("<summary class=\"h4\">Sequence Diagrams</summary>");
                        if (isPlantUmlBrowser)
                        {
                            body.Append("<div class=\"diagram-toggle\">");
                            body.Append("<span class=\"diagram-toggle-spacer\"></span><span class=\"details-radio\"><span class=\"details-radio-label\">Details:</span><button class=\"details-radio-btn\" data-state=\"expanded\" onclick=\"window._setAllNotes(this,'expanded')\">Expand</button><button class=\"details-radio-btn\" data-state=\"collapsed\" onclick=\"window._setAllNotes(this,'collapsed')\">Collapse</button><button class=\"details-radio-btn details-active\" data-state=\"truncated\" onclick=\"window._setAllNotes(this,'truncated')\">Truncate</button><select class=\"truncate-lines-select\" autocomplete=\"off\" onchange=\"window._setScenarioTruncateLines(this)\"><option value=\"3\">3</option><option value=\"4\">4</option><option value=\"5\">5</option><option value=\"10\">10</option><option value=\"15\">15</option><option value=\"20\">20</option><option value=\"25\">25</option><option value=\"30\">30</option><option value=\"35\">35</option><option value=\"40\" selected>40</option><option value=\"50\">50</option><option value=\"60\">60</option><option value=\"80\">80</option><option value=\"100\">100</option></select><span class=\"truncate-lines-label\">lines</span></span><button class=\"details-radio-btn toggle-btn details-active\" data-toggle=\"headers\" data-shown=\"true\" onclick=\"window._toggleScenarioHeaders(this)\">Headers Shown</button>");
                            if (hasAssertionNotes)
                                body.Append("<button class=\"details-radio-btn toggle-btn\" data-toggle=\"assertions\" data-shown=\"false\" onclick=\"window._toggleScenarioAssertions(this)\">Assertions Hidden</button>");
                            if (hasStepDelimiters)
                                body.Append("<button class=\"details-radio-btn toggle-btn details-active\" data-toggle=\"steps\" data-shown=\"true\" onclick=\"window._toggleScenarioSteps(this)\">Steps Shown</button>");
                            if (hasDatabaseParticipants)
                                body.Append("<button class=\"details-radio-btn toggle-btn details-active\" data-toggle=\"databases\" data-shown=\"true\" onclick=\"window._toggleScenarioDatabases(this)\">Databases Shown</button>");
                            body.Append(scenarioNoteFormatSelect);
                            body.Append("</div>");
                        }
                    }
                    else
                    {
                        // Only whole-test-flow, no sequence diagrams
                        var hasActivity = !string.IsNullOrEmpty(wholeTestContent!.Value.ActivityHtml);
                        var hasFlame = !string.IsNullOrEmpty(wholeTestContent!.Value.FlameHtml);
                        if (hasActivity && hasFlame)
                        {
                            body.Append("<summary class=\"h4\">Diagrams</summary>");
                            body.Append("<div class=\"diagram-toggle\">");
                            body.Append("<button class=\"diagram-toggle-btn diagram-toggle-active\" data-dtype=\"activity\">Activity Diagrams</button>");
                            body.Append("<button class=\"diagram-toggle-btn\" data-dtype=\"flame\">Flame Chart</button>");
                            body.Append(spanWarning);
                            if (isPlantUmlBrowser)
                            {
                                body.Append("<span class=\"diagram-toggle-spacer\"></span><span class=\"details-radio\"><span class=\"details-radio-label\">Details:</span><button class=\"details-radio-btn\" data-state=\"expanded\" onclick=\"window._setAllNotes(this,'expanded')\">Expand</button><button class=\"details-radio-btn\" data-state=\"collapsed\" onclick=\"window._setAllNotes(this,'collapsed')\">Collapse</button><button class=\"details-radio-btn details-active\" data-state=\"truncated\" onclick=\"window._setAllNotes(this,'truncated')\">Truncate</button><select class=\"truncate-lines-select\" autocomplete=\"off\" onchange=\"window._setScenarioTruncateLines(this)\"><option value=\"3\">3</option><option value=\"4\">4</option><option value=\"5\">5</option><option value=\"10\">10</option><option value=\"15\">15</option><option value=\"20\">20</option><option value=\"25\">25</option><option value=\"30\">30</option><option value=\"35\">35</option><option value=\"40\" selected>40</option><option value=\"50\">50</option><option value=\"60\">60</option><option value=\"80\">80</option><option value=\"100\">100</option></select><span class=\"truncate-lines-label\">lines</span></span><button class=\"details-radio-btn toggle-btn details-active\" data-toggle=\"headers\" data-shown=\"true\" onclick=\"window._toggleScenarioHeaders(this)\">Headers Shown</button>");
                                body.Append(scenarioNoteFormatSelect);
                            }
                            body.Append("</div>");
                        }
                        else if (hasActivity)
                        {
                            body.Append("<summary class=\"h4\">Activity Diagrams</summary>");
                        }
                        else
                        {
                            body.Append("<summary class=\"h4\">Flame Chart</summary>");
                        }
                    }

                    if (hasSequenceDiagrams)
                    {
                        var seqWrap = hasWholeTestFlow;
                        if (seqWrap) body.Append("<div class=\"diagram-view diagram-view-seq\">");

                        var lazyLoadAttr = lazyLoadImages ? " loading=\"lazy\"" : "";
                        var rawLabel = "Raw Plant UML";
                        foreach (var diagram in diagramsForTest)
                        {
                            // Every branch below emits a client-readable copy of the source
                            // (puml-data blob or the raw-plantuml <pre>) — index it.
                            searchIndexPieces?[scenario.Id].Add(diagram.CodeBehind);
                            if (isPlantUmlBrowser)
                            {
                                var diagramId = $"puml-{plantUmlBrowserCounter++}";
                                var compressed = InternalFlowHtmlGenerator.CompressToBase64(diagram.CodeBehind);
                                diagramDataMap[diagramId] = compressed;
                                body.Append($"""
                                         <div class="plantuml-browser" id="{diagramId}" data-diagram-type="plantuml"></div>
                                         """);
                            }
                            else if (isInlineSvg)
                            {
                                var svgDiagramId = $"puml-svg-{plantUmlBrowserCounter++}";
                                var sourceCompressed = InternalFlowHtmlGenerator.CompressToBase64(diagram.CodeBehind);
                                diagramDataMap[svgDiagramId] = sourceCompressed;
                                body.Append($"""
                                         <div class="plantuml-inline-svg" id="{svgDiagramId}" data-diagram-type="plantuml">{diagram.ImgSrc}</div>
                                         """);
                            }
                            else
                            {
                                body.Append($"""
                                         <details class="example">
                                            <summary class="example-image">
                                                <img{lazyLoadAttr} src="{diagram.ImgSrc}">
                                            </summary>
                                            <div class="raw-plantuml">
                                                <h4>{rawLabel}</h4>
                                                <pre>{System.Net.WebUtility.HtmlEncode(diagram.CodeBehind)}</pre>
                                             </div>
                                         </details>
                                         """);
                            }
                        }

                        if (seqWrap) body.Append("</div>");
                    }

                    if (hasWholeTestFlow)
                    {
                        var wtf = wholeTestContent!.Value;
                        var hideActivity = hasSequenceDiagrams; // hidden when seq is default
                        var hideFlame = hasSequenceDiagrams || (!string.IsNullOrEmpty(wtf.ActivityHtml) && !hasSequenceDiagrams);

                        if (!string.IsNullOrEmpty(wtf.ActivityHtml))
                            body.Append($"<div class=\"diagram-view diagram-view-activity\"{(hideActivity ? " style=\"display:none\"" : "")}>{wtf.ActivityHtml}</div>");
                        if (!string.IsNullOrEmpty(wtf.FlameHtml))
                            body.Append($"<div class=\"diagram-view diagram-view-flame\"{(hideFlame ? " style=\"display:none\"" : "")}>{wtf.FlameHtml}</div>");
                        if (searchIndexPieces is not null)
                            AddWholeTestFlowSearchPieces(wtf.ActivityHtml, wtf.FlameHtml, diagramDataMap, searchIndexPieces[scenario.Id]);
                    }

                    body.Append("</details>");

                    // A diagram made only of step bars / assertion notes (a test that never touched a
                    // tracked dependency) still deserves the explicit "no interactions" affordance —
                    // especially as those notes are hidden by default in the browser.
                    if (showNoInteractionsMarker && hasSequenceDiagrams
                        && !(trackedLogs ?? RequestResponseLogger.RequestAndResponseLogs)
                            .Any(l => l.TestId == scenario.Id && !l.TrackingIgnore && !l.IsOverrideStart && !l.IsOverrideEnd && !l.IsActionStart))
                    {
                        body.Append(NoInteractionsMarkerHtml);
                    }
                }
                else if (showNoInteractionsMarker)
                {
                    body.Append(NoInteractionsMarkerHtml);
                }

                body.Append("</details>");
            }
            if (ruleOpen)
            {
                body.Append("</details>"); // close last rule
            }
            body.Append("</details>");
        }
        body.Append("</div>");

        // Jump-to-failure button (only when there are failures)
        if (hasFailures)
        {
            body.Append($"""<button class="jump-to-failure" onclick="jump_to_next_failure()">Next Failure <span class="failure-counter" id="failure-counter">(0/{failureCount})</span></button>""");
        }

        // Back-to-top FAB (#10)
        body.Append("""<button class="back-to-top" id="back-to-top" onclick="window.scrollTo({top:0,behavior:'smooth'})">↑</button>""");

        html += body;
        if (diagramDataMap.Count > 0)
        {
            html += "<script id=\"puml-data\" type=\"application/json\">";
            html += System.Text.Json.JsonSerializer.Serialize(diagramDataMap);
            html += "</script>";
        }
        if (buildSearchIndex)
        {
            html += BuildSearchIndexScript(allScenarios, scenarioAnchorIds, searchIndexPieces!, searchIndexCache!);
        }
        html += """
                    </body>
                </html>
                """
        ;

        return WriteFile(html, fileName);
    }

    /// <summary>
    /// Assembles and serializes the deep-search index blob: per scenario doc (in
    /// <c>allScenarios</c> enumeration order — the same order the anchor-id map is built in),
    /// the union of the trigram bucket sets of its collected corpus pieces, serialized to the
    /// §4.2 v1 layout, gzipped and embedded as
    /// <c>&lt;script id="kron-search-index" type="application/json"&gt;</c>.
    /// </summary>
    private static string BuildSearchIndexScript(
        Scenario[] allScenarios,
        Dictionary<string, string> scenarioAnchorIds,
        Dictionary<string, List<string>> searchIndexPieces,
        SearchIndex.SearchIndexBuildCache cache)
    {
        cache.WaitForPrewarm();
        var docAnchors = new string[allScenarios.Length];
        var bucketsPerDoc = new IReadOnlyCollection<int>[allScenarios.Length];
        Parallel.For(0, allScenarios.Length, i =>
        {
            var pieces = searchIndexPieces[allScenarios[i].Id];
            var pieceBuckets = new int[pieces.Count][];
            for (var p = 0; p < pieces.Count; p++)
                pieceBuckets[p] = cache.GetOrAddBuckets(pieces[p]);
            bucketsPerDoc[i] = pieces.Count == 0 ? [] : SearchIndex.SearchIndexBuilder.UnionBuckets(pieceBuckets);
        });
        for (var i = 0; i < allScenarios.Length; i++)
            docAnchors[i] = scenarioAnchorIds[allScenarios[i].Id];

        var raw = SearchIndex.SearchIndexBuilder.Serialize(docAnchors, bucketsPerDoc);
        return $"<script id=\"kron-search-index\" type=\"application/json\">\"{SearchIndex.SearchIndexBuilder.CompressToBase64(raw)}\"</script>";
    }

    /// <summary>
    /// Extracts the searchable text of a scenario's whole-test-flow content from the HTML that
    /// was actually emitted — activity diagram PlantUML (inline <c>data-plantuml-z</c> on the
    /// merge path, or registered in <c>puml-data</c> via its element id on the live path) and
    /// flame chart text (<c>data-flame-z</c> → the <c>s</c>/<c>f[i][1]</c>/<c>m[i][1]</c> fields,
    /// newline-joined in JSON order — the client verify pass assembles the same string).
    /// Extracting from the emitted HTML rather than re-deriving from segments guarantees the
    /// corpus matches what the DOM holds, on the live and merge paths alike.
    /// </summary>
    private static void AddWholeTestFlowSearchPieces(
        string activityHtml, string flameHtml, Dictionary<string, string> diagramDataMap, List<string> pieces)
    {
        if (!string.IsNullOrEmpty(activityHtml))
        {
            foreach (Match m in Regex.Matches(activityHtml, "data-plantuml-z=\"([^\"]+)\""))
                pieces.Add(InternalFlowHtmlGenerator.DecompressFromBase64(m.Groups[1].Value));
            foreach (Match m in Regex.Matches(activityHtml, "class=\"plantuml-browser[^\"]*\" id=\"([^\"]+)\""))
                if (diagramDataMap.TryGetValue(m.Groups[1].Value, out var compressed))
                    pieces.Add(InternalFlowHtmlGenerator.DecompressFromBase64(compressed));
        }

        if (!string.IsNullOrEmpty(flameHtml))
        {
            foreach (Match m in Regex.Matches(flameHtml, "data-flame-z=\"([^\"]+)\""))
            {
                var json = InternalFlowHtmlGenerator.DecompressFromBase64(m.Groups[1].Value);
                var text = ExtractFlameSearchText(json);
                if (text.Length > 0) pieces.Add(text);
            }
        }
    }

    /// <summary>The flame text the client verify pass reads: sources, span names, marker labels, newline-joined in JSON order.</summary>
    internal static string ExtractFlameSearchText(string flameJson)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(flameJson);
        var parts = new List<string>();
        if (doc.RootElement.TryGetProperty("s", out var sources))
            foreach (var s in sources.EnumerateArray())
                parts.Add(s.GetString() ?? "");
        if (doc.RootElement.TryGetProperty("f", out var spans))
            foreach (var span in spans.EnumerateArray())
                parts.Add(span[1].GetString() ?? "");
        if (doc.RootElement.TryGetProperty("m", out var markers))
            foreach (var marker in markers.EnumerateArray())
                parts.Add(marker[1].GetString() ?? "");
        return string.Join("\n", parts);
    }

    /// <summary>
    /// SEARCH_INDEX_PLAN §1.2 coverage fix: parameterized example values are rendered as table
    /// cells but historically appeared in neither <c>data-search</c> nor <c>data-row-search</c>.
    /// </summary>
    private static void AddExampleValueSearchParts(Scenario scenario, List<string> searchParts)
    {
        if (scenario.ExampleFlatValues is { Count: > 0 })
            searchParts.AddRange(scenario.ExampleFlatValues.Values.Where(v => !string.IsNullOrEmpty(v)));
        else if (scenario.ExampleValues is { Count: > 0 })
            searchParts.AddRange(scenario.ExampleValues.Values.Where(v => !string.IsNullOrEmpty(v)));
    }

    public static string GenerateYamlSpecs(DefaultDiagramsFetcher.DiagramAsCode[] diagrams,
        Feature[] features,
        string fileName,
        string title,
        bool generateBlankOnFailedTests = false)
    {
        if (generateBlankOnFailedTests && features.Any(x => x.Scenarios.Any(y => y.Result == ExecutionResult.Failed)))
            return WriteFile(string.Empty, fileName);

        var yml = new StringBuilder();
        yml.Append("Title: " + title + "\n");
        yml.Append("Features:\n");

        foreach (var feature in features.OrderBy(x => x.DisplayName))
        {
            yml.Append("  - Feature: " + feature.DisplayName.SanitiseForYml() + "\n");

            if (feature.Endpoint is not null)
                yml.Append("    Endpoint: " + feature.Endpoint + "\n");

            if (feature.Description is not null)
                yml.Append("    Description: " + feature.Description.SanitiseForYml() + "\n");

            if (feature.Labels is { Length: > 0 })
            {
                yml.Append("    Labels:\n");
                foreach (var label in feature.Labels)
                    yml.Append("      - " + label.SanitiseForYml() + "\n");
            }

            yml.Append("    Scenarios:\n");

            var orderedScenarios = feature.Scenarios.OrderByDescending(x => x.IsHappyPath).ThenBy(x => x.DisplayName);
            foreach (var scenario in orderedScenarios)
            {
                yml.Append("      - Scenario: " + scenario.DisplayName.SanitiseForYml() + "\n");
                yml.Append("        IsHappyPath: " + scenario.IsHappyPath.ToString().ToLower() + "\n");

                if (scenario.Labels is { Length: > 0 })
                {
                    yml.Append("        Labels:\n");
                    foreach (var label in scenario.Labels)
                        yml.Append("          - " + label.SanitiseForYml() + "\n");
                }

                if (scenario.Categories is { Length: > 0 })
                {
                    yml.Append("        Categories:\n");
                    foreach (var cat in scenario.Categories)
                        yml.Append("          - " + cat.SanitiseForYml() + "\n");
                }

                // Emitted as a sibling of Steps, matching the TestRunReport writers: merging the two would
                // lose the b{i}/{i} split the step paths and interaction attribution depend on.
                if (scenario.BackgroundSteps is { Length: > 0 })
                {
                    yml.Append("        BackgroundSteps:\n");
                    foreach (var step in scenario.BackgroundSteps)
                        AppendYamlStep(yml, step, "          ");
                }

                if (scenario.Steps is { Length: > 0 })
                {
                    yml.Append("        Steps:\n");
                    foreach (var step in scenario.Steps)
                        AppendYamlStep(yml, step, "          ");
                }

                yml.Append("\n");
            }
        }

        return WriteFile(yml.ToString(), fileName);
    }

    private static void AppendYamlStep(StringBuilder yml, ScenarioStep step, string indent)
    {
        var text = step.Keyword is not null ? $"{step.Keyword} {step.Text}" : step.Text;
        yml.Append(indent + "- " + text.SanitiseForYml() + "\n");

        if (step.SubSteps is { Length: > 0 })
        {
            foreach (var sub in step.SubSteps)
                AppendYamlStep(yml, sub, indent + "  ");
        }
    }

    private static int CountStepsRecursive(ScenarioStep[] steps)
    {
        var count = steps.Length;
        foreach (var step in steps)
        {
            if (step.SubSteps is { Length: > 0 })
                count += CountStepsRecursive(step.SubSteps);
        }
        return count;
    }

    private static (int Passed, int Failed, int Skipped) CountStepsByStatusRecursive(ScenarioStep[] steps)
    {
        var passed = 0;
        var failed = 0;
        var skipped = 0;
        foreach (var step in steps)
        {
            switch (step.Status)
            {
                case ExecutionResult.Passed: passed++; break;
                case ExecutionResult.Failed: failed++; break;
                case ExecutionResult.Skipped or ExecutionResult.Bypassed or ExecutionResult.SkippedAfterFailure: skipped++; break;
                default: skipped++; break;
            }
            if (step.SubSteps is { Length: > 0 })
            {
                var sub = CountStepsByStatusRecursive(step.SubSteps);
                passed += sub.Passed;
                failed += sub.Failed;
                skipped += sub.Skipped;
            }
        }
        return (passed, failed, skipped);
    }

    internal static string GeneratePieChartSvg(int passed, int failed, int skipped, int bypassed)
    {
        var total = passed + failed + skipped + bypassed;
        if (total == 0) return "";

        var passRate = (int)Math.Round(100.0 * passed / total);
        var segments = new List<(double pct, string color, string label, int count)>();
        if (passed > 0) segments.Add((100.0 * passed / total, "#1daf26", "Passed", passed));
        if (failed > 0) segments.Add((100.0 * failed / total, "#cc0000", "Failed", failed));
        if (skipped > 0) segments.Add((100.0 * skipped / total, "#949494", "Skipped", skipped));
        if (bypassed > 0) segments.Add((100.0 * bypassed / total, "#2e7bff", "Bypassed", bypassed));

        const double radius = 40;
        const double circumference = 2 * Math.PI * radius;
        var sb = new StringBuilder();
        sb.Append("<div class=\"summary-chart\">");
        sb.Append("<svg viewBox=\"0 0 100 100\">");

        var offset = 0.0;
        foreach (var (pct, color, label, count) in segments)
        {
            var dash = circumference * pct / 100.0;
            var gap = circumference - dash;
            var dashOffset = -offset * circumference / 100.0;
            sb.Append($"<circle cx=\"50\" cy=\"50\" r=\"{radius:F1}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"12\" " +
                      $"stroke-dasharray=\"{dash:F2} {gap:F2}\" stroke-dashoffset=\"{dashOffset:F2}\" transform=\"rotate(-90 50 50)\">" +
                      $"<title>{label}: {count} ({pct:F0}%)</title></circle>");
            offset += pct;
        }

        sb.Append($"<text x=\"50\" y=\"50\" text-anchor=\"middle\" dominant-baseline=\"central\" font-size=\"16\" font-weight=\"bold\" fill=\"#333\">{passRate}%</text>");
        sb.Append("</svg></div>");
        return sb.ToString();
    }

    private static string FormatDuration(TimeSpan duration)
    {
        var total = duration.Duration();
        if (total.TotalSeconds < 1)
            return $"{total.Milliseconds}ms";
        if (total.TotalMinutes < 1)
            return $"{total.Seconds}s";
        return $"{(int)total.TotalMinutes}m {total.Seconds}s";
    }

    private static bool HasAnyFailed(ScenarioStep step)
    {
        if (step.SubSteps is not { Length: > 0 }) return false;
        foreach (var sub in step.SubSteps)
        {
            if (sub.Status == ExecutionResult.Failed) return true;
            if (HasAnyFailed(sub)) return true;
        }
        return false;
    }

    private static bool HasAnyBypassed(ScenarioStep step)
    {
        if (step.SubSteps is not { Length: > 0 }) return false;
        foreach (var sub in step.SubSteps)
        {
            if (sub.Status == ExecutionResult.Bypassed) return true;
            if (HasAnyBypassed(sub)) return true;
        }
        return false;
    }

    private static bool HasAnySkipped(ScenarioStep step)
    {
        if (step.SubSteps is not { Length: > 0 }) return false;
        foreach (var sub in step.SubSteps)
        {
            if (sub.Status == ExecutionResult.Skipped) return true;
            if (HasAnySkipped(sub)) return true;
        }
        return false;
    }

    /// <summary>
    /// Resolves the whole-test-flow content for a scenario. When precomputed fragments are supplied
    /// (the merge path), they are returned verbatim; otherwise the content is rendered live from the
    /// in-process segments and tracked logs.
    /// </summary>
    private static (string ActivityHtml, string FlameHtml, int SpanCount)? ResolveWholeTestFlowContent(
        string scenarioId,
        Dictionary<string, Merge.WholeTestFlowFragment>? precomputedWholeTestContent,
        Dictionary<string, InternalFlowSegment>? wholeTestSegments,
        RequestResponseLog[]? trackedLogs,
        WholeTestFlowVisualization wholeTestVisualization,
        Dictionary<string, string> diagramDataMap)
    {
        if (precomputedWholeTestContent is not null)
            return precomputedWholeTestContent.TryGetValue(scenarioId, out var frag)
                ? (frag.ActivityHtml, frag.FlameHtml, frag.SpanCount)
                : null;

        if (wholeTestSegments is null || wholeTestVisualization == WholeTestFlowVisualization.None)
            return null;

        var boundaryLogs = trackedLogs?
            .Where(l => l.TestId == scenarioId && l.Type == RequestResponseType.Request && l.Timestamp.HasValue)
            .OrderBy(l => l.Timestamp!.Value)
            .Select(l => ($"{l.Method.Value}: {l.Uri.PathAndQuery}", l.Timestamp!.Value))
            .ToArray() ?? [];

        return InternalFlowHtmlGenerator.GetWholeTestFlowContent(
            wholeTestSegments, scenarioId, boundaryLogs, wholeTestVisualization, diagramDataMap);
    }

    /// <summary>
    /// Computes the Examples: block separator bands for a parameterized group whose members carry
    /// block structure. Keyed by the index of the first member row of each block (members are
    /// already sorted by block by <see cref="ParameterGrouper"/>); values are pre-encoded HTML parts.
    /// </summary>
    private static Dictionary<int, (string NameHtml, string? DescHtml, string CountsText)> BuildExamplesBlockBands(Scenario[] scenarios)
    {
        var bands = new Dictionary<int, (string, string?, string)>();
        for (var ri = 0; ri < scenarios.Length;)
        {
            var start = ri;
            var blockIndex = scenarios[start].ExamplesBlockIndex;
            var blockName = scenarios[start].ExamplesBlockName;
            do { ri++; }
            while (ri < scenarios.Length
                   && scenarios[ri].ExamplesBlockIndex == blockIndex
                   && scenarios[ri].ExamplesBlockName == blockName);

            var blockMembers = scenarios[start..ri];
            var passCount = blockMembers.Count(s => s.Result == ExecutionResult.Passed);
            var failCount = blockMembers.Count(s => s.Result == ExecutionResult.Failed);
            var skipCount = blockMembers.Count(s => s.Result is ExecutionResult.Skipped or ExecutionResult.Bypassed or ExecutionResult.SkippedAfterFailure);
            var countParts = new List<string>();
            if (failCount > 0) countParts.Add($"{failCount} failed");
            if (skipCount > 0) countParts.Add($"{skipCount} skipped");
            countParts.Add($"{passCount}/{blockMembers.Length} passed");

            var nameHtml = string.IsNullOrEmpty(blockName)
                ? "Examples"
                : $"Examples: {System.Net.WebUtility.HtmlEncode(blockName)}";
            var desc = scenarios[start].ExamplesBlockDescription;
            var descHtml = string.IsNullOrWhiteSpace(desc) ? null : System.Net.WebUtility.HtmlEncode(desc);

            bands[start] = (nameHtml, descHtml, string.Join(", ", countParts));
        }
        return bands;
    }

    /// <summary>
    /// Emits one Examples: block separator band. The band deliberately carries no
    /// <c>data-row-idx</c>, <c>onclick</c>, <c>data-row-search</c> or <c>id</c> so every existing
    /// row-selection, flatten-toggle and search behavior treats it as inert.
    /// </summary>
    private static void AppendExamplesBlockBand(StringBuilder body, (string NameHtml, string? DescHtml, string CountsText) band, int colspan)
    {
        body.Append($"<tr class=\"examples-block-row\"><td colspan=\"{colspan}\">");
        body.Append($"<span class=\"examples-block-name\">{band.NameHtml}</span>");
        body.Append($"<span class=\"examples-block-counts\">{band.CountsText}</span>");
        if (band.DescHtml is not null)
            body.Append($"<span class=\"examples-block-desc\">{band.DescHtml}</span>");
        body.Append("</td></tr>");
    }

    private static void RenderParameterizedGroup(
        StringBuilder body,
        ParameterizedGroup group,
        string prefix,
        ILookup<string, DefaultDiagramsFetcher.DiagramAsCode> diagramsByTestId,
        Dictionary<string, HashSet<string>> scenarioDependencies,
        Dictionary<string, HashSet<string>> scenarioDiagramSearchTerms,
        bool showStepNumbers,
        bool isPlantUmlBrowser,
        bool isInlineSvg,
        bool lazyLoadImages,
        ref int plantUmlBrowserCounter,
        Dictionary<string, string> diagramDataMap,
        Dictionary<string, InternalFlowSegment>? wholeTestSegments,
        RequestResponseLog[]? trackedLogs,
        WholeTestFlowVisualization wholeTestVisualization,
        int medianSpanCount,
        bool titleizeParameterNames = true,
        bool hasAssertionNotes = false,
        bool hasStepDelimiters = false,
        bool hasDatabaseParticipants = false,
        Dictionary<string, string>? scenarioAnchorIds = null,
        string? featureDisplayName = null,
        string? featureDescription = null,
        string? featureEndpoint = null,
        string[]? featureLabels = null,
        Dictionary<string, Merge.WholeTestFlowFragment>? precomputedWholeTestContent = null,
        bool showNoInteractionsMarker = false,
        bool separateBackgroundSteps = false,
        bool collapseRepeatedStepKeywords = true,
        string scenarioNoteFormatSelect = "",
        Dictionary<string, List<string>>? searchIndexPieces = null)
    {
        var scenarios = group.Scenarios;

        // Named Examples: blocks render as separator bands only when the group actually has
        // block structure; a single unnamed block (or no block data at all) must produce
        // byte-identical output to a report generated without the block fields.
        var hasBlockStructure = scenarios.Select(s => s.ExamplesBlockIndex).Distinct().Count() > 1
            || scenarios.Any(s => !string.IsNullOrEmpty(s.ExamplesBlockName));
        var blockBands = hasBlockStructure ? BuildExamplesBlockBands(scenarios) : null;

        // Aggregate status
        var hasFailure = scenarios.Any(s => s.Result == ExecutionResult.Failed);
        var hasSkipped = scenarios.Any(s => s.Result == ExecutionResult.Skipped);
        var overallStatus = hasFailure ? ExecutionResult.Failed
            : hasSkipped ? ExecutionResult.Skipped
            : scenarios.Any(s => s.Result == ExecutionResult.Bypassed) ? ExecutionResult.Bypassed
            : ExecutionResult.Passed;

        // Build search text
        var searchParts = new List<string> { group.GroupDisplayName };
        if (featureDisplayName is not null) searchParts.Add(featureDisplayName);
        if (featureDescription is not null) searchParts.Add(featureDescription);
        if (featureEndpoint is not null) searchParts.Add(featureEndpoint);
        if (featureLabels is { Length: > 0 }) searchParts.AddRange(featureLabels);
        foreach (var s in scenarios)
        {
            searchParts.Add(s.DisplayName);
            if (!string.IsNullOrWhiteSpace(s.Description)) searchParts.Add(s.Description);
            if (s.Rule is not null) searchParts.Add(s.Rule);
            if (s.Categories is { Length: > 0 }) searchParts.AddRange(s.Categories);
            if (s.Labels is { Length: > 0 }) searchParts.AddRange(s.Labels);
            if (s.ErrorMessage is not null) searchParts.Add(s.ErrorMessage);
            CollectStepText(s.BackgroundSteps, searchParts);
            CollectStepText(s.Steps, searchParts);
            if (scenarioDiagramSearchTerms.TryGetValue(s.Id, out var diagramTerms) && diagramTerms.Count > 0)
                searchParts.AddRange(diagramTerms);
            if (hasBlockStructure)
            {
                if (!string.IsNullOrEmpty(s.ExamplesBlockName)) searchParts.Add(s.ExamplesBlockName);
                if (!string.IsNullOrEmpty(s.ExamplesBlockDescription)) searchParts.Add(s.ExamplesBlockDescription);
            }
            AddExampleValueSearchParts(s, searchParts);
        }
        var groupSearchText = string.Join(" ", searchParts).ToLowerInvariant();
        var searchAttr = $" data-search=\"{System.Net.WebUtility.HtmlEncode(groupSearchText)}\"";
        // Each member scenario is a deep-search doc; the group <details> is the element whose
        // data-search the client verify pass reads, so the group text is part of every member's corpus.
        if (searchIndexPieces is not null)
            foreach (var s in scenarios)
                searchIndexPieces[s.Id].Add(groupSearchText);

        // Aggregate categories, labels, dependencies
        var categories = scenarios.Where(s => s.Categories is { Length: > 0 }).SelectMany(s => s.Categories!).Distinct().ToArray();
        var categoriesAttr = categories.Length > 0 ? $" data-categories=\"{System.Net.WebUtility.HtmlEncode(string.Join(",", categories))}\"" : "";
        var labels = scenarios.Where(s => s.Labels is { Length: > 0 }).SelectMany(s => s.Labels!).Distinct().ToArray();
        var labelsAttr = labels.Length > 0 ? $" data-labels=\"{System.Net.WebUtility.HtmlEncode(string.Join(",", labels))}\"" : "";
        var allDeps = scenarios.Where(s => scenarioDependencies.ContainsKey(s.Id)).SelectMany(s => scenarioDependencies[s.Id]).Distinct().OrderBy(d => d).ToArray();
        var depsAttr = allDeps.Length > 0 ? $" data-dependencies=\"{System.Net.WebUtility.HtmlEncode(string.Join(",", allDeps))}\"" : "";

        // Total duration
        var totalDuration = scenarios.Where(s => s.Duration.HasValue).Select(s => s.Duration!.Value).Aggregate(TimeSpan.Zero, (acc, d) => acc + d);
        var durationAttr = totalDuration > TimeSpan.Zero ? $" data-duration-ms=\"{totalDuration.TotalMilliseconds:F0}\"" : "";
        var durationBadge = totalDuration > TimeSpan.Zero
            ? $" <span class=\"duration-badge {(totalDuration.TotalMilliseconds < 2000 ? "duration-fast" : totalDuration.TotalMilliseconds < 5000 ? "duration-moderate" : "duration-slow")}\">{FormatDurationBadge(totalDuration)}</span>"
            : "";

        // Pass/fail summary
        var passCount = scenarios.Count(s => s.Result == ExecutionResult.Passed);
        var failCount = scenarios.Count(s => s.Result == ExecutionResult.Failed);
        var skipCount = scenarios.Count(s => s.Result is ExecutionResult.Skipped or ExecutionResult.Bypassed or ExecutionResult.SkippedAfterFailure);
        var summaryParts = new List<string>();
        if (failCount > 0) summaryParts.Add($"{failCount} failed");
        if (skipCount > 0) summaryParts.Add($"{skipCount} skipped");
        summaryParts.Add($"{passCount}/{scenarios.Length} passed");
        var summaryText = $" <span class=\"label\">{string.Join(", ", summaryParts)}</span>";

        var anchorId = GenerateScenarioAnchorId(group.GroupDisplayName);
        var encodedGroupName = System.Net.WebUtility.HtmlEncode(group.GroupDisplayName);
        var isGroupHappyPath = scenarios.Any(s => s.IsHappyPath);
        var happyPathClass = isGroupHappyPath ? " happy-path" : "";
        var happyPathBadge = isGroupHappyPath ? " <span class=\"label\">Happy Path</span>" : "";

        body.Append($"<details class=\"scenario scenario-parameterized{happyPathClass}\" data-status=\"{overallStatus}\"{depsAttr}{searchAttr}{durationAttr}{categoriesAttr}{labelsAttr} id=\"{anchorId}\" tabindex=\"0\">");
        body.Append($"<summary class=\"h3{(hasFailure ? " failed" : hasSkipped ? " skipped" : "")}\">{encodedGroupName}{happyPathBadge}{summaryText}{durationBadge}<button class=\"copy-scenario-name\" title=\"Copy scenario name\" data-scenario-name=\"{encodedGroupName}\" onclick=\"copy_scenario_name(this, event)\">&#128203;</button><a class=\"scenario-link\" href=\"#{anchorId}\" title=\"Link to this scenario\" onclick=\"event.stopPropagation()\">&#128279;</a></summary>");

        // Parameter table
        var hasFlatView = group.FlatParameterNames is { Length: > 0 };
        if (hasFlatView) body.Append("<div class=\"param-table-wrapper\">");

        // Flat parameter table (visible by default) — shows original Gherkin Example columns as scalar values
        if (hasFlatView)
        {
            var flatNames = group.FlatParameterNames!;
            body.Append($"<table class=\"param-test-table param-table-flat\" data-prefix=\"{prefix}\"><thead>");
            body.Append($"<tr><th rowspan=\"2\" style=\"width:2.5em\">#</th>");
            body.Append($"<th colspan=\"{flatNames.Length}\" class=\"master-header\"><button class=\"flatten-toggle\" onclick=\"toggleFlattenParams(this,'{prefix}')\" title=\"Show grouped columns\">\u2212</button>Input Parameters</th>");
            body.Append("<th rowspan=\"2\" style=\"width:5em\">Status</th>");
            body.Append("<th rowspan=\"2\" style=\"width:5.5em\">Duration</th></tr>");
            body.Append("<tr>");
            foreach (var name in flatNames)
            {
                var displayName = titleizeParameterNames ? name.Titleize() : name;
                body.Append($"<th class=\"sub-header\">{System.Net.WebUtility.HtmlEncode(displayName)}</th>");
            }
            body.Append("</tr></thead><tbody>");

            for (var ri = 0; ri < scenarios.Length; ri++)
            {
                var s = scenarios[ri];
                var rowStatusClass = s.Result switch
                {
                    ExecutionResult.Passed => "row-passed",
                    ExecutionResult.Failed => "row-failed",
                    ExecutionResult.Skipped or ExecutionResult.SkippedAfterFailure => "row-skipped",
                    ExecutionResult.Bypassed => "row-bypassed",
                    _ => ""
                };
                var activeClass = ri == 0 ? " row-active" : "";
                var badgeClass = s.Result switch
                {
                    ExecutionResult.Passed => "badge-pass",
                    ExecutionResult.Failed => "badge-fail",
                    ExecutionResult.Skipped or ExecutionResult.SkippedAfterFailure => "badge-skip",
                    ExecutionResult.Bypassed => "badge-bypass",
                    _ => ""
                };
                var badgeText = s.Result switch
                {
                    ExecutionResult.Passed => "Passed",
                    ExecutionResult.Failed => "Failed",
                    ExecutionResult.Skipped => "Skipped",
                    ExecutionResult.Bypassed => "Bypassed",
                    ExecutionResult.SkippedAfterFailure => "Skipped",
                    _ => ""
                };

                var rowSearchParts = new List<string> { s.DisplayName };
                if (featureDisplayName is not null) rowSearchParts.Add(featureDisplayName);
                if (featureDescription is not null) rowSearchParts.Add(featureDescription);
                if (featureEndpoint is not null) rowSearchParts.Add(featureEndpoint);
                if (!string.IsNullOrWhiteSpace(s.Description)) rowSearchParts.Add(s.Description);
                if (featureLabels is { Length: > 0 }) rowSearchParts.AddRange(featureLabels);
                if (s.Categories is { Length: > 0 }) rowSearchParts.AddRange(s.Categories);
                if (s.Labels is { Length: > 0 }) rowSearchParts.AddRange(s.Labels);
                if (s.ErrorMessage is not null) rowSearchParts.Add(s.ErrorMessage);
                CollectStepText(s.BackgroundSteps, rowSearchParts);
                CollectStepText(s.Steps, rowSearchParts);
                if (scenarioDiagramSearchTerms.TryGetValue(s.Id, out var rowDiagramTermsFlat) && rowDiagramTermsFlat.Count > 0)
                    rowSearchParts.AddRange(rowDiagramTermsFlat);
                if (hasBlockStructure)
                {
                    if (!string.IsNullOrEmpty(s.ExamplesBlockName)) rowSearchParts.Add(s.ExamplesBlockName);
                    if (!string.IsNullOrEmpty(s.ExamplesBlockDescription)) rowSearchParts.Add(s.ExamplesBlockDescription);
                }
                AddExampleValueSearchParts(s, rowSearchParts);
                var rowSearchAttr = $" data-row-search=\"{System.Net.WebUtility.HtmlEncode(string.Join(" ", rowSearchParts).ToLowerInvariant())}\"";

                if (blockBands is not null && blockBands.TryGetValue(ri, out var flatBand))
                    AppendExamplesBlockBand(body, flatBand, 1 + flatNames.Length + 2);

                body.Append($"<tr class=\"{rowStatusClass}{activeClass}\" data-row-idx=\"{ri}\"{rowSearchAttr} onclick=\"selectRow(this,'{prefix}')\">");
                body.Append($"<td>{ri + 1}</td>");

                foreach (var name in flatNames)
                {
                    var val = s.ExampleFlatValues?.GetValueOrDefault(name, "") ?? "";
                    body.Append($"<td class=\"mono\">{FormatDisplayValue(val)}</td>");
                }

                var rowDuration = s.Duration.HasValue ? FormatDurationBadge(s.Duration.Value) : "";
                body.Append($"<td><span class=\"status-badge {badgeClass}\">{badgeText}</span></td>");
                body.Append($"<td class=\"mono\">{rowDuration}</td>");
                body.Append("</tr>");
            }
            body.Append("</tbody></table>");
        }

        // Grouped parameter table (hidden when flat view exists)
        var groupedTableClass = hasFlatView ? " param-table-grouped" : "";
        var groupedStyle = hasFlatView ? " style=\"display:none\"" : "";
        body.Append($"<table class=\"param-test-table{groupedTableClass}\"{groupedStyle} data-prefix=\"{prefix}\"><thead>");
        if (group.Rule is ParameterDisplayRule.ScalarColumns or ParameterDisplayRule.FlattenedObject && group.ParameterNames.Length > 0)
        {
            // R1/R2: Two-row header with master "Input Parameters" header
            body.Append($"<tr><th rowspan=\"2\" style=\"width:2.5em\">#</th>");
            var toggleBtn = hasFlatView ? $"<button class=\"flatten-toggle\" onclick=\"toggleFlattenParams(this,'{prefix}')\" title=\"Show flattened columns\">+</button>" : "";
            body.Append($"<th colspan=\"{group.ParameterNames.Length}\" class=\"master-header\">{toggleBtn}Input Parameters</th>");
            body.Append("<th rowspan=\"2\" style=\"width:5em\">Status</th>");
            body.Append("<th rowspan=\"2\" style=\"width:5.5em\">Duration</th></tr>");
            body.Append("<tr>");
            foreach (var name in group.ParameterNames)
            {
                var displayName = titleizeParameterNames ? name.Titleize() : name;
                body.Append($"<th class=\"sub-header\">{System.Net.WebUtility.HtmlEncode(displayName)}</th>");
            }
            body.Append("</tr>");
        }
        else
        {
            // R0: Fallback single-row header
            body.Append("<tr><th style=\"width:2.5em\">#</th><th>Test Case</th><th style=\"width:5em\">Status</th><th style=\"width:5.5em\">Duration</th></tr>");
        }
        body.Append("</thead><tbody>");

        for (var ri = 0; ri < scenarios.Length; ri++)
        {
            var s = scenarios[ri];
            var rowStatusClass = s.Result switch
            {
                ExecutionResult.Passed => "row-passed",
                ExecutionResult.Failed => "row-failed",
                ExecutionResult.Skipped or ExecutionResult.SkippedAfterFailure => "row-skipped",
                ExecutionResult.Bypassed => "row-bypassed",
                _ => ""
            };
            var activeClass = ri == 0 ? " row-active" : "";
            var badgeClass = s.Result switch
            {
                ExecutionResult.Passed => "badge-pass",
                ExecutionResult.Failed => "badge-fail",
                ExecutionResult.Skipped or ExecutionResult.SkippedAfterFailure => "badge-skip",
                ExecutionResult.Bypassed => "badge-bypass",
                _ => ""
            };
            var badgeText = s.Result switch
            {
                ExecutionResult.Passed => "Passed",
                ExecutionResult.Failed => "Failed",
                ExecutionResult.Skipped => "Skipped",
                ExecutionResult.Bypassed => "Bypassed",
                ExecutionResult.SkippedAfterFailure => "Skipped",
                _ => ""
            };

            var rowSearchParts = new List<string> { s.DisplayName };
            if (featureDisplayName is not null) rowSearchParts.Add(featureDisplayName);
            if (featureDescription is not null) rowSearchParts.Add(featureDescription);
            if (featureEndpoint is not null) rowSearchParts.Add(featureEndpoint);
            if (!string.IsNullOrWhiteSpace(s.Description)) rowSearchParts.Add(s.Description);
            if (featureLabels is { Length: > 0 }) rowSearchParts.AddRange(featureLabels);
            if (s.Categories is { Length: > 0 }) rowSearchParts.AddRange(s.Categories);
            if (s.Labels is { Length: > 0 }) rowSearchParts.AddRange(s.Labels);
            if (s.ErrorMessage is not null) rowSearchParts.Add(s.ErrorMessage);
            CollectStepText(s.BackgroundSteps, rowSearchParts);
            CollectStepText(s.Steps, rowSearchParts);
            if (scenarioDiagramSearchTerms.TryGetValue(s.Id, out var rowDiagramTerms) && rowDiagramTerms.Count > 0)
                rowSearchParts.AddRange(rowDiagramTerms);
            if (hasBlockStructure)
            {
                if (!string.IsNullOrEmpty(s.ExamplesBlockName)) rowSearchParts.Add(s.ExamplesBlockName);
                if (!string.IsNullOrEmpty(s.ExamplesBlockDescription)) rowSearchParts.Add(s.ExamplesBlockDescription);
            }
            AddExampleValueSearchParts(s, rowSearchParts);
            var rowSearchAttr = $" data-row-search=\"{System.Net.WebUtility.HtmlEncode(string.Join(" ", rowSearchParts).ToLowerInvariant())}\"";

            if (blockBands is not null && blockBands.TryGetValue(ri, out var groupedBand))
            {
                var groupedCols = group.Rule is ParameterDisplayRule.ScalarColumns or ParameterDisplayRule.FlattenedObject && group.ParameterNames.Length > 0
                    ? 1 + group.ParameterNames.Length + 2
                    : 4;
                AppendExamplesBlockBand(body, groupedBand, groupedCols);
            }

            var rowAnchorId = scenarioAnchorIds?.GetValueOrDefault(s.Id) ?? GenerateScenarioAnchorId(s.DisplayName);
            body.Append($"<tr class=\"{rowStatusClass}{activeClass}\" data-row-idx=\"{ri}\" id=\"{rowAnchorId}\" data-scenario-id=\"{rowAnchorId}\"{rowSearchAttr} onclick=\"selectRow(this,'{prefix}')\">");
            body.Append($"<td>{ri + 1}</td>");

            if (group.Rule is ParameterDisplayRule.ScalarColumns or ParameterDisplayRule.FlattenedObject && group.ParameterNames.Length > 0)
            {
                // R1/R2: Individual parameter columns with cell-level R3/R4 rendering
                foreach (var name in group.ParameterNames)
                {
                    var rawValue = s.ExampleRawValues?.GetValueOrDefault(name);
                    if (rawValue is not null && ParameterValueRenderer.IsSmallComplexObject(rawValue))
                    {
                        // R3: Sub-table for small complex objects
                        body.Append("<td>");
                        ParameterValueRenderer.RenderSubTable(body, rawValue);
                        body.Append("</td>");
                    }
                    else if (rawValue is not null && ParameterValueRenderer.IsComplexValue(rawValue))
                    {
                        // R4: Expandable details for deeply complex objects
                        body.Append("<td>");
                        ParameterValueRenderer.RenderExpandable(body, rawValue);
                        body.Append("</td>");
                    }
                    else
                    {
                        // Try string-based R3/R4 when raw values aren't available
                        var val = s.ExampleValues?.GetValueOrDefault(name, "") ?? "";
                        var tdBody = new StringBuilder();
                        if (ParameterValueRenderer.TryRenderFromParsedString(tdBody, val))
                        {
                            body.Append("<td>");
                            body.Append(tdBody);
                            body.Append("</td>");
                        }
                        else
                        {
                            // Scalar: plain text
                            body.Append($"<td class=\"mono\">{FormatDisplayValue(val)}</td>");
                        }
                    }
                }
            }
            else
            {
                // R0: Full display name as "Test Case"
                var displayText = s.ExampleDisplayName ?? s.DisplayName;
                body.Append($"<td class=\"mono\">{System.Net.WebUtility.HtmlEncode(displayText)}</td>");
            }

            var rowDuration = s.Duration.HasValue ? FormatDurationBadge(s.Duration.Value) : "";
            body.Append($"<td><span class=\"status-badge {badgeClass}\">{badgeText}</span></td>");
            body.Append($"<td class=\"mono\">{rowDuration}</td>");
            body.Append("</tr>");
        }
        body.Append("</tbody></table>");
        if (hasFlatView) body.Append("</div>"); // close param-table-wrapper

        // Detail panels (steps, failure) — rendered below the parameter table
        var hasAnyDetail = scenarios.Any(s => s.Steps is { Length: > 0 } || s.BackgroundSteps is { Length: > 0 } || s.Result == ExecutionResult.Failed);
        if (hasAnyDetail)
        {
            body.Append($"<div class=\"param-detail-panels\">");
            for (var ri = 0; ri < scenarios.Length; ri++)
            {
                var s = scenarios[ri];
                var display = ri == 0 ? "" : " style=\"display:none\"";
                body.Append($"<div class=\"param-detail-panel\" id=\"{prefix}-detail-{ri}\"{display}>");

                if (!string.IsNullOrWhiteSpace(s.Description))
                    body.Append($"""<div class="scenario-description">{System.Net.WebUtility.HtmlEncode(s.Description)}</div>""");

                RenderScenarioStepSections(body, s, showStepNumbers, separateBackgroundSteps, collapseRepeatedStepKeywords);

                if (s.Attachments is { Length: > 0 })
                {
                    body.Append("""<div class="scenario-attachments">""");
                    foreach (var attachment in s.Attachments)
                    {
                        if (attachment.IsInlineImage)
                        {
                            body.Append($"<a class=\"attachment-image-link\" href=\"{System.Net.WebUtility.HtmlEncode(attachment.RelativePath)}\" target=\"_blank\"><img class=\"attachment-image\" src=\"{System.Net.WebUtility.HtmlEncode(attachment.RelativePath)}\" alt=\"{System.Net.WebUtility.HtmlEncode(attachment.Name)}\" /></a>");
                        }
                        else
                        {
                            body.Append($"<a class=\"step-attachment\" href=\"{System.Net.WebUtility.HtmlEncode(attachment.RelativePath)}\">{System.Net.WebUtility.HtmlEncode(attachment.Name)}</a>");
                        }
                    }
                    body.Append("</div>");
                }

                if (s.Result == ExecutionResult.Failed)
                {
                    var diffHtml = "";
                    var diffResult = ErrorDiffParser.TryParseExpectedActual(s.ErrorMessage);
                    if (diffResult is not null)
                        diffHtml = ErrorDiffParser.GenerateDiffHtml(diffResult.Expected, diffResult.Actual);
                    body.Append("<details class=\"failure-result\" open><summary class=\"h4\">Failure Result</summary><pre>");
                    if (s.ErrorMessage is not null)
                        body.Append($"Failure Cause: {System.Net.WebUtility.HtmlEncode(s.ErrorMessage)}\n\n");
                    if (s.ErrorStackTrace is not null)
                    {
                        body.Append(System.Net.WebUtility.HtmlEncode(s.ErrorStackTrace));
                        // index-only, like the non-parameterized path: deep-findable, never data-search
                        searchIndexPieces?[s.Id].Add(s.ErrorStackTrace);
                    }
                    body.Append($"</pre>{diffHtml}</details>");
                }

                body.Append("</div>");
            }
            body.Append("</div>");
        }

        // Compute whole-test-flow content per scenario
        var wholeTestContents = new (string ActivityHtml, string FlameHtml, int SpanCount)?[scenarios.Length];
        if (precomputedWholeTestContent is not null || (wholeTestSegments is not null && wholeTestVisualization != WholeTestFlowVisualization.None))
        {
            for (var ri = 0; ri < scenarios.Length; ri++)
            {
                wholeTestContents[ri] = ResolveWholeTestFlowContent(
                    scenarios[ri].Id, precomputedWholeTestContent, wholeTestSegments, trackedLogs, wholeTestVisualization, diagramDataMap);
            }
        }

        var hasAnyWholeTestFlow = wholeTestContents.Any(w => w is not null);
        var allWtfIdentical = false;
        if (hasAnyWholeTestFlow && group.AllDiagramsIdentical)
        {
            // Check if all whole-test-flow content is identical too
            var firstActivity = wholeTestContents[0]?.ActivityHtml ?? "";
            var firstFlame = wholeTestContents[0]?.FlameHtml ?? "";
            allWtfIdentical = wholeTestContents.All(w =>
                (w?.ActivityHtml ?? "") == firstActivity && (w?.FlameHtml ?? "") == firstFlame);
        }

        // Diagrams
        var hasAnySeqDiagrams = scenarios.Any(s => diagramsByTestId[s.Id].Any());
        var hasDiagramContent = hasAnySeqDiagrams || hasAnyWholeTestFlow;

        if (hasDiagramContent)
        {
            body.Append("<details class=\"example-diagrams\" open>");

            // Determine toggle buttons needed
            var showSeqToggle = hasAnySeqDiagrams;
            var showActivityToggle = hasAnyWholeTestFlow && wholeTestContents.Any(w => !string.IsNullOrEmpty(w?.ActivityHtml));
            var showFlameToggle = hasAnyWholeTestFlow && wholeTestContents.Any(w => !string.IsNullOrEmpty(w?.FlameHtml));
            var multipleTypes = (showSeqToggle ? 1 : 0) + (showActivityToggle ? 1 : 0) + (showFlameToggle ? 1 : 0) > 1;

            if (multipleTypes)
            {
                body.Append("<summary class=\"h4\">Diagrams</summary>");
                body.Append("<div class=\"diagram-toggle\">");
                if (showSeqToggle)
                    body.Append("<button class=\"diagram-toggle-btn diagram-toggle-active\" data-dtype=\"seq\">Sequence Diagrams</button>");
                if (showActivityToggle)
                    body.Append($"<button class=\"diagram-toggle-btn{(!showSeqToggle ? " diagram-toggle-active" : "")}\" data-dtype=\"activity\">Activity Diagrams</button>");
                if (showFlameToggle)
                    body.Append("<button class=\"diagram-toggle-btn\" data-dtype=\"flame\">Flame Chart</button>");
                if (isPlantUmlBrowser && showSeqToggle)
                    body.Append("<span class=\"diagram-toggle-spacer\"></span><span class=\"details-radio\"><span class=\"details-radio-label\">Details:</span><button class=\"details-radio-btn\" data-state=\"expanded\" onclick=\"window._setAllNotes(this,'expanded')\">Expand</button><button class=\"details-radio-btn\" data-state=\"collapsed\" onclick=\"window._setAllNotes(this,'collapsed')\">Collapse</button><button class=\"details-radio-btn details-active\" data-state=\"truncated\" onclick=\"window._setAllNotes(this,'truncated')\">Truncate</button><select class=\"truncate-lines-select\" autocomplete=\"off\" onchange=\"window._setScenarioTruncateLines(this)\"><option value=\"3\">3</option><option value=\"4\">4</option><option value=\"5\">5</option><option value=\"10\">10</option><option value=\"15\">15</option><option value=\"20\">20</option><option value=\"25\">25</option><option value=\"30\">30</option><option value=\"35\">35</option><option value=\"40\" selected>40</option><option value=\"50\">50</option><option value=\"60\">60</option><option value=\"80\">80</option><option value=\"100\">100</option></select><span class=\"truncate-lines-label\">lines</span></span><button class=\"details-radio-btn toggle-btn details-active\" data-toggle=\"headers\" data-shown=\"true\" onclick=\"window._toggleScenarioHeaders(this)\">Headers Shown</button>");
                if (hasAssertionNotes)
                    body.Append("<button class=\"details-radio-btn toggle-btn\" data-toggle=\"assertions\" data-shown=\"false\" onclick=\"window._toggleScenarioAssertions(this)\">Assertions Hidden</button>");
                if (hasStepDelimiters)
                    body.Append("<button class=\"details-radio-btn toggle-btn details-active\" data-toggle=\"steps\" data-shown=\"true\" onclick=\"window._toggleScenarioSteps(this)\">Steps Shown</button>");
                if (hasDatabaseParticipants)
                    body.Append("<button class=\"details-radio-btn toggle-btn details-active\" data-toggle=\"databases\" data-shown=\"true\" onclick=\"window._toggleScenarioDatabases(this)\">Databases Shown</button>");
                body.Append(scenarioNoteFormatSelect);
                body.Append("</div>");
            }
            else if (showSeqToggle)
            {
                body.Append("<summary class=\"h4\">Sequence Diagrams</summary>");
                if (isPlantUmlBrowser)
                {
                    body.Append("<div class=\"diagram-toggle\">");
                    body.Append("<span class=\"diagram-toggle-spacer\"></span><span class=\"details-radio\"><span class=\"details-radio-label\">Details:</span><button class=\"details-radio-btn\" data-state=\"expanded\" onclick=\"window._setAllNotes(this,'expanded')\">Expand</button><button class=\"details-radio-btn\" data-state=\"collapsed\" onclick=\"window._setAllNotes(this,'collapsed')\">Collapse</button><button class=\"details-radio-btn details-active\" data-state=\"truncated\" onclick=\"window._setAllNotes(this,'truncated')\">Truncate</button><select class=\"truncate-lines-select\" autocomplete=\"off\" onchange=\"window._setScenarioTruncateLines(this)\"><option value=\"3\">3</option><option value=\"4\">4</option><option value=\"5\">5</option><option value=\"10\">10</option><option value=\"15\">15</option><option value=\"20\">20</option><option value=\"25\">25</option><option value=\"30\">30</option><option value=\"35\">35</option><option value=\"40\" selected>40</option><option value=\"50\">50</option><option value=\"60\">60</option><option value=\"80\">80</option><option value=\"100\">100</option></select><span class=\"truncate-lines-label\">lines</span></span><button class=\"details-radio-btn toggle-btn details-active\" data-toggle=\"headers\" data-shown=\"true\" onclick=\"window._toggleScenarioHeaders(this)\">Headers Shown</button>");
                    if (hasAssertionNotes)
                        body.Append("<button class=\"details-radio-btn toggle-btn\" data-toggle=\"assertions\" data-shown=\"false\" onclick=\"window._toggleScenarioAssertions(this)\">Assertions Hidden</button>");
                    if (hasStepDelimiters)
                        body.Append("<button class=\"details-radio-btn toggle-btn details-active\" data-toggle=\"steps\" data-shown=\"true\" onclick=\"window._toggleScenarioSteps(this)\">Steps Shown</button>");
                    if (hasDatabaseParticipants)
                        body.Append("<button class=\"details-radio-btn toggle-btn details-active\" data-toggle=\"databases\" data-shown=\"true\" onclick=\"window._toggleScenarioDatabases(this)\">Databases Shown</button>");
                    body.Append(scenarioNoteFormatSelect);
                    body.Append("</div>");
                }
            }
            else if (showActivityToggle && showFlameToggle)
            {
                body.Append("<summary class=\"h4\">Diagrams</summary>");
                body.Append("<div class=\"diagram-toggle\">");
                body.Append("<button class=\"diagram-toggle-btn diagram-toggle-active\" data-dtype=\"activity\">Activity Diagrams</button>");
                body.Append("<button class=\"diagram-toggle-btn\" data-dtype=\"flame\">Flame Chart</button>");
                body.Append("</div>");
            }
            else if (showActivityToggle)
            {
                body.Append("<summary class=\"h4\">Activity Diagrams</summary>");
            }
            else
            {
                body.Append("<summary class=\"h4\">Flame Chart</summary>");
            }

            // Sequence diagrams
            if (hasAnySeqDiagrams)
            {
                var seqWrap = hasAnyWholeTestFlow && multipleTypes;
                if (seqWrap) body.Append("<div class=\"diagram-view diagram-view-seq\">");

                if (group.AllDiagramsIdentical)
                {
                    var firstDiagrams = diagramsByTestId[scenarios[0].Id].ToArray();
                    if (firstDiagrams.Length > 0)
                    {
                        body.Append("<span class=\"param-diagram-identical-badge\">All diagrams identical across test cases</span>");
                        RenderDiagramsForScenario(body, firstDiagrams, isPlantUmlBrowser, isInlineSvg, lazyLoadImages, ref plantUmlBrowserCounter, diagramDataMap);
                        // The single emitted copy is a descendant of the group <details>, so it is
                        // part of every member doc's verify corpus.
                        if (searchIndexPieces is not null)
                            foreach (var s in scenarios)
                                foreach (var diagram in firstDiagrams)
                                    searchIndexPieces[s.Id].Add(diagram.CodeBehind);
                    }
                }
                else
                {
                    for (var ri = 0; ri < scenarios.Length; ri++)
                    {
                        var s = scenarios[ri];
                        var display = ri == 0 ? "" : " style=\"display:none\"";
                        body.Append($"<div id=\"{prefix}-diagram-{ri}\"{display}>");
                        var diagrams = diagramsByTestId[s.Id].ToArray();
                        if (diagrams.Length == 0 && showNoInteractionsMarker)
                            body.Append(NoInteractionsMarkerHtml);
                        if (diagrams.Length > 0)
                        {
                            RenderDiagramsForScenario(body, diagrams, isPlantUmlBrowser, isInlineSvg, lazyLoadImages, ref plantUmlBrowserCounter, diagramDataMap);
                            if (searchIndexPieces is not null)
                                foreach (var diagram in diagrams)
                                    searchIndexPieces[s.Id].Add(diagram.CodeBehind);
                        }
                        body.Append("</div>");
                    }
                }

                if (seqWrap) body.Append("</div>");
            }

            // Activity diagrams
            if (showActivityToggle)
            {
                var hideActivity = showSeqToggle; // hidden when seq is default
                if (hideActivity) body.Append("<div class=\"diagram-view diagram-view-activity\" style=\"display:none\">");
                else body.Append("<div class=\"diagram-view diagram-view-activity\">");

                if (allWtfIdentical && wholeTestContents[0] is not null)
                {
                    body.Append("<span class=\"param-diagram-identical-badge\">All diagrams identical across test cases</span>");
                    body.Append(wholeTestContents[0]!.Value.ActivityHtml);
                    if (searchIndexPieces is not null)
                        foreach (var s in scenarios)
                            AddWholeTestFlowSearchPieces(wholeTestContents[0]!.Value.ActivityHtml, "", diagramDataMap, searchIndexPieces[s.Id]);
                }
                else
                {
                    for (var ri = 0; ri < scenarios.Length; ri++)
                    {
                        var display = ri == 0 ? "" : " style=\"display:none\"";
                        body.Append($"<div id=\"{prefix}-activity-{ri}\"{display}>");
                        if (wholeTestContents[ri] is not null && !string.IsNullOrEmpty(wholeTestContents[ri]!.Value.ActivityHtml))
                        {
                            body.Append(wholeTestContents[ri]!.Value.ActivityHtml);
                            if (searchIndexPieces is not null)
                                AddWholeTestFlowSearchPieces(wholeTestContents[ri]!.Value.ActivityHtml, "", diagramDataMap, searchIndexPieces[scenarios[ri].Id]);
                        }
                        body.Append("</div>");
                    }
                }

                body.Append("</div>");
            }

            // Flame charts
            if (showFlameToggle)
            {
                var hideFlame = showSeqToggle || (showActivityToggle && !showSeqToggle);
                if (hideFlame) body.Append("<div class=\"diagram-view diagram-view-flame\" style=\"display:none\">");
                else body.Append("<div class=\"diagram-view diagram-view-flame\">");

                if (allWtfIdentical && wholeTestContents[0] is not null)
                {
                    body.Append("<span class=\"param-diagram-identical-badge\">All diagrams identical across test cases</span>");
                    body.Append(wholeTestContents[0]!.Value.FlameHtml);
                    if (searchIndexPieces is not null)
                        foreach (var s in scenarios)
                            AddWholeTestFlowSearchPieces("", wholeTestContents[0]!.Value.FlameHtml, diagramDataMap, searchIndexPieces[s.Id]);
                }
                else
                {
                    for (var ri = 0; ri < scenarios.Length; ri++)
                    {
                        var display = ri == 0 ? "" : " style=\"display:none\"";
                        body.Append($"<div id=\"{prefix}-flame-{ri}\"{display}>");
                        if (wholeTestContents[ri] is not null && !string.IsNullOrEmpty(wholeTestContents[ri]!.Value.FlameHtml))
                        {
                            body.Append(wholeTestContents[ri]!.Value.FlameHtml);
                            if (searchIndexPieces is not null)
                                AddWholeTestFlowSearchPieces("", wholeTestContents[ri]!.Value.FlameHtml, diagramDataMap, searchIndexPieces[scenarios[ri].Id]);
                        }
                        body.Append("</div>");
                    }
                }

                body.Append("</div>");
            }

            body.Append("</details>");
        }

        body.Append("</details>");
    }

    private static void RenderDiagramsForScenario(
        StringBuilder body,
        DefaultDiagramsFetcher.DiagramAsCode[] diagrams,
        bool isPlantUmlBrowser,
        bool isInlineSvg,
        bool lazyLoadImages,
        ref int plantUmlBrowserCounter,
        Dictionary<string, string> diagramDataMap)
    {
        var lazyLoadAttr = lazyLoadImages ? " loading=\"lazy\"" : "";
        foreach (var diagram in diagrams)
        {
            if (isPlantUmlBrowser)
            {
                var diagramId = $"puml-{plantUmlBrowserCounter++}";
                var compressed = InternalFlowHtmlGenerator.CompressToBase64(diagram.CodeBehind);
                diagramDataMap[diagramId] = compressed;
                body.Append($"<div class=\"plantuml-browser\" id=\"{diagramId}\" data-diagram-type=\"plantuml\"></div>");
            }
            else if (isInlineSvg)
            {
                var svgDiagramId = $"puml-svg-{plantUmlBrowserCounter++}";
                var sourceCompressed = InternalFlowHtmlGenerator.CompressToBase64(diagram.CodeBehind);
                diagramDataMap[svgDiagramId] = sourceCompressed;
                body.Append($"<div class=\"plantuml-inline-svg\" id=\"{svgDiagramId}\" data-diagram-type=\"plantuml\">{diagram.ImgSrc}</div>");
            }
            else
            {
                body.Append($"""
                         <details class="example">
                            <summary class="example-image">
                                <img{lazyLoadAttr} src="{diagram.ImgSrc}">
                            </summary>
                            <div class="raw-plantuml">
                                <h4>Raw Plant UML</h4>
                                <pre>{System.Net.WebUtility.HtmlEncode(diagram.CodeBehind)}</pre>
                             </div>
                         </details>
                         """);
            }
        }
    }

    /// <summary>
    /// Renders a scenario's steps — the one place both the plain-scenario surface and the
    /// parameterized-group detail panels go through, so the two cannot drift apart.
    /// <para>
    /// By default the background steps are concatenated in front of the scenario's own and the whole lot
    /// is rendered as one <c>Steps</c> list, numbered continuously, matching the order the data files and
    /// step paths already use (<c>b0</c>, <c>b1</c>, then <c>0</c>, <c>1</c>). With
    /// <paramref name="separateBackgroundSteps"/> the background gets its own collapsible section above,
    /// and the <c>Steps</c> list continues its numbering after it rather than restarting at 1.
    /// </para>
    /// </summary>
    private static void RenderScenarioStepSections(
        StringBuilder body,
        Scenario scenario,
        bool showStepNumbers,
        bool separateBackgroundSteps,
        bool collapseRepeatedStepKeywords)
    {
        var background = scenario.BackgroundSteps ?? [];
        var steps = scenario.Steps ?? [];

        if (separateBackgroundSteps)
        {
            if (background.Length > 0)
            {
                // Each section collapses independently, so the Steps list still opens with its own primary.
                var backgroundKeywords = collapseRepeatedStepKeywords ? StepKeywordCollapser.DisplayKeywords(background) : null;
                body.Append("""<details class="scenario-background">""");
                body.Append("""<summary class="h4">Background Steps</summary>""");
                for (var bi = 0; bi < background.Length; bi++)
                {
                    var numberPrefix = showStepNumbers ? $"{bi + 1}." : null;
                    RenderStep(body, background[bi], numberPrefix, skipTabularInline: false,
                        displayKeyword: backgroundKeywords?[bi], isBackground: true);
                }
                body.Append("</details>");
            }

            if (steps.Length > 0)
                RenderStepsList(body, steps, showStepNumbers, background.Length, backgroundCount: 0, collapseRepeatedStepKeywords);

            return;
        }

        ScenarioStep[] combined = background.Length == 0 ? steps
            : steps.Length == 0 ? background
            : [.. background, .. steps];

        if (combined.Length == 0)
            return;

        RenderStepsList(body, combined, showStepNumbers, numberOffset: 0, backgroundCount: background.Length, collapseRepeatedStepKeywords);
    }

    /// <summary>
    /// The <c>Steps</c> disclosure itself. <paramref name="numberOffset"/> is added to the displayed step
    /// number, and the first <paramref name="backgroundCount"/> entries are marked <c>step-background</c>.
    /// </summary>
    private static void RenderStepsList(
        StringBuilder body,
        ScenarioStep[] steps,
        bool showStepNumbers,
        int numberOffset,
        int backgroundCount,
        bool collapseRepeatedStepKeywords)
    {
        var displayKeywords = collapseRepeatedStepKeywords ? StepKeywordCollapser.DisplayKeywords(steps) : null;

        body.Append("""<details class="scenario-steps" open>""");
        body.Append("""<summary class="h4">Steps</summary>""");

        var renderCombined = ShouldRenderCombinedTable(steps);
        var afterThen = false;
        for (var si = 0; si < steps.Length; si++)
        {
            // Tracked against the keyword the producer recorded, not the collapsed display keyword:
            // an `And` inherits the phase before it either way.
            var keyword = steps[si].Keyword?.Trim();
            if (keyword?.Equals("Then", StringComparison.OrdinalIgnoreCase) == true)
                afterThen = true;
            else if (keyword?.Equals("Given", StringComparison.OrdinalIgnoreCase) == true ||
                     keyword?.Equals("When", StringComparison.OrdinalIgnoreCase) == true)
                afterThen = false;

            var numberPrefix = showStepNumbers ? $"{numberOffset + si + 1}." : null;
            RenderStep(body, steps[si], numberPrefix, skipTabularInline: renderCombined && afterThen,
                displayKeyword: displayKeywords?[si], isBackground: si < backgroundCount);
        }

        if (renderCombined)
            RenderCombinedTabularParameters(body, steps);

        body.Append("</details>");
    }

    /// <summary>
    /// Renders one step (and, recursively, its sub-steps).
    /// <para>
    /// <c>displayKeyword</c> shows in place of <see cref="ScenarioStep.Keyword"/> —
    /// <see cref="StepKeywordCollapser"/> substitutes <c>And</c> for a repeat of the primary keyword in
    /// force. A render-time projection only; the step itself is never modified, because background steps
    /// are shared across scenarios and the data writers run concurrently with this one.
    /// </para>
    /// <para>
    /// <c>isBackground</c> says the step came from <see cref="Scenario.BackgroundSteps"/>, marking it
    /// <c>step-background</c> so a combined list still shows where the background ends.
    /// </para>
    /// </summary>
    private static void RenderStep(StringBuilder body, ScenarioStep step, string? numberPrefix = null, bool skipTabularInline = true,
        string? displayKeyword = null, bool isBackground = false)
    {
        var statusClass = step.Status switch
        {
            ExecutionResult.Passed => HasAnySkipped(step) ? "passed-skipped" : HasAnyBypassed(step) ? "passed-bypassed" : "passed",
            ExecutionResult.Failed => "failed",
            ExecutionResult.Skipped => "skipped",
            ExecutionResult.Bypassed => "bypassed",
            ExecutionResult.SkippedAfterFailure => "skipped-after-failure",
            _ => ""
        };

        var statusIcon = step.Status switch
        {
            ExecutionResult.Passed => "&#10003;",
            ExecutionResult.Failed => "&#10005;",
            ExecutionResult.Skipped => "&#216;",
            ExecutionResult.Bypassed => "&#8631;",
            ExecutionResult.SkippedAfterFailure => "!",
            _ => ""
        };

        var statusTooltip = step.Status switch
        {
            ExecutionResult.Passed => HasAnySkipped(step)
                ? "Passed (with skipped sub-steps) — all assertions passed, but one or more sub-steps were skipped. Skipped steps did not execute and also prevented execution of subsequent steps"
                : HasAnyBypassed(step)
                ? "Passed (with bypassed sub-steps) — all assertions passed, but one or more sub-steps were bypassed (intentionally skipped over without preventing execution of subsequent steps)"
                : "Passed — all assertions in this step passed",
            ExecutionResult.Failed => "Failed — this step threw an exception or an assertion failed",
            ExecutionResult.Skipped => "Skipped — this step did not execute because it was intentionally skipped, either at the scenario level, or at the step level. In the latter case the skip also prevented execution of subsequent steps",
            ExecutionResult.Bypassed => "Bypassed — some or all of the logic in this step was intentionally skipped over without preventing execution of subsequent steps",
            ExecutionResult.SkippedAfterFailure => "Skipped after failure — this step was never reached because an earlier step failed",
            _ => ""
        };

        var hasSubSteps = step.SubSteps is { Length: > 0 };
        var backgroundClass = isBackground ? " step-background" : "";

        if (hasSubSteps)
        {
            body.Append(HasAnyFailed(step)
                ? $"<details class=\"step step-collapsible{backgroundClass}\" open>"
                : $"<details class=\"step step-collapsible{backgroundClass}\">");
            body.Append("<summary>");
        }
        else
        {
            body.Append($"<div class=\"step{backgroundClass}\">");
        }

        if (numberPrefix is not null)
        {
            body.Append($"<span class=\"step-number\">{numberPrefix}</span>");
        }

        if (step.Status.HasValue)
        {
            body.Append($"<span class=\"step-status {statusClass}\" title=\"{statusTooltip}\">{statusIcon}</span>");
        }

        if (step.Keyword is not null)
        {
            body.Append($"<span class=\"step-keyword\">{System.Net.WebUtility.HtmlEncode(displayKeyword ?? step.Keyword)}</span> ");
        }

        // Render step text — either structured segments with inline params, or plain text
        if (step.TextSegments is { Length: > 0 })
        {
            body.Append("<span class=\"step-text\">");
            foreach (var seg in step.TextSegments)
            {
                if (seg.Parameter is not null)
                {
                    var paramStatusClass = seg.Parameter.Status switch
                    {
                        VerificationStatus.Success => "param-success",
                        VerificationStatus.Failure => "param-failure",
                        VerificationStatus.Exception => "param-exception",
                        VerificationStatus.NotProvided => "param-not-provided",
                        _ => "param-na"
                    };
                    var display = seg.Parameter.Expectation is not null
                        ? $"{FormatDisplayValue(seg.Parameter.Value)}/{FormatDisplayValue(seg.Parameter.Expectation)}"
                        : FormatDisplayValue(seg.Parameter.Value);
                    var titleAttr = seg.ParameterName is not null
                        ? $" title=\"{System.Net.WebUtility.HtmlEncode(seg.ParameterName)}\""
                        : "";
                    body.Append($"<span class=\"step-param-inline {paramStatusClass}\"{titleAttr}>{display}</span>");
                }
                else if (seg.TableReference is not null)
                {
                    // Check if this table reference has a backing table/tree parameter
                    var matchingParam = step.Parameters?.FirstOrDefault(p => p.Name == seg.TableReference);
                    if (matchingParam is { Kind: StepParameterKind.Inline, InlineValue: not null } &&
                        ParameterParser.IsComplexObjectString(matchingParam.InlineValue.Value))
                    {
                        // Complex inline value with no backing table — render based on size
                        if (ParameterParser.IsSmallComplexValue(matchingParam.InlineValue.Value))
                        {
                            // Small: render inline like a normal parameter
                            var inlineDisplay = ParameterParser.FormatComplexValueInline(matchingParam.InlineValue.Value)
                                                ?? matchingParam.InlineValue.Value;
                            body.Append($"<span class=\"step-param-inline param-na\" title=\"{System.Net.WebUtility.HtmlEncode(seg.TableReference)}\">{System.Net.WebUtility.HtmlEncode(inlineDisplay)}</span>");
                        }
                        else
                        {
                            // Large: render as expandable button with data-value
                            var json = ParameterParser.FormatComplexValueAsJson(matchingParam.InlineValue.Value)
                                       ?? matchingParam.InlineValue.Value;
                            body.Append("</span>");
                            body.Append($"<button class=\"step-table-ref\" onclick=\"toggle_table_ref(this)\" data-param=\"{System.Net.WebUtility.HtmlEncode(seg.TableReference)}\" data-value=\"{System.Net.WebUtility.HtmlEncode(json)}\">{System.Net.WebUtility.HtmlEncode(seg.TableReference)}</button>");
                            body.Append("<span class=\"step-text\">");
                        }
                    }
                    else
                    {
                        // Check for simple inline value (not complex), or no matching param at all
                        if (matchingParam is { Kind: StepParameterKind.Inline, InlineValue: not null })
                        {
                            // Simple inline value — render as inline span showing the value
                            var display = FormatDisplayValue(matchingParam.InlineValue.Value);
                            body.Append($"<span class=\"step-param-inline param-na\" title=\"{System.Net.WebUtility.HtmlEncode(seg.TableReference)}\">{display}</span>");
                        }
                        else if (matchingParam is { Kind: StepParameterKind.Tabular or StepParameterKind.Tree })
                        {
                            // Table/Tree parameter — render as button (scrolls to table)
                            body.Append("</span>");
                            body.Append($"<button class=\"step-table-ref\" onclick=\"toggle_table_ref(this)\" data-param=\"{System.Net.WebUtility.HtmlEncode(seg.TableReference)}\">{System.Net.WebUtility.HtmlEncode(seg.TableReference)}</button>");
                            body.Append("<span class=\"step-text\">");
                        }
                        else
                        {
                            // No matching parameter — render formatted value if available, otherwise plain text
                            if (seg.TableReferenceFormattedValue is not null)
                            {
                                body.Append($"<span class=\"step-param-inline param-na\" title=\"{System.Net.WebUtility.HtmlEncode(seg.TableReference)}\">{System.Net.WebUtility.HtmlEncode(seg.TableReferenceFormattedValue)}</span>");
                            }
                            else
                            {
                                body.Append(System.Net.WebUtility.HtmlEncode(seg.TableReference));
                            }
                        }
                    }
                }
                else if (seg.Text is not null)
                {
                    body.Append(System.Net.WebUtility.HtmlEncode(seg.Text));
                }
            }
            body.Append("</span>");
        }
        else
        {
            var stepText = step.Text;

            // Strip tabular parameter reference suffixes like [paramName: "<$paramName>"] from step text
            if (step.Parameters?.Any(p => p.Kind == StepParameterKind.Tabular) == true)
                stepText = StripTabularParamSuffixRegex().Replace(stepText, "").TrimEnd();

            body.Append($"<span class=\"step-text\">{System.Net.WebUtility.HtmlEncode(stepText)}</span>");
        }

        if (step.Duration.HasValue)
        {
            body.Append($" <span class=\"step-duration\">({FormatDurationBadge(step.Duration.Value)})</span>");
        }

        if (step.Comments is { Length: > 0 })
        {
            foreach (var comment in step.Comments)
            {
                body.Append($"<div class=\"step-comment\">{System.Net.WebUtility.HtmlEncode(comment)}</div>");
            }
        }

        if (step.Attachments is { Length: > 0 })
        {
            foreach (var attachment in step.Attachments)
            {
                if (attachment.IsInlineImage)
                {
                    body.Append($"<a class=\"attachment-image-link\" href=\"{System.Net.WebUtility.HtmlEncode(attachment.RelativePath)}\" onclick=\"openLightbox(event, this)\"><img class=\"attachment-image\" src=\"{System.Net.WebUtility.HtmlEncode(attachment.RelativePath)}\" alt=\"{System.Net.WebUtility.HtmlEncode(attachment.Name)}\" /></a>");
                    body.Append($"<span class=\"attachment-image-name\">{System.Net.WebUtility.HtmlEncode(attachment.Name)}</span>");
                }
                else
                {
                    body.Append($"<a class=\"step-attachment\" href=\"{System.Net.WebUtility.HtmlEncode(attachment.RelativePath)}\">{System.Net.WebUtility.HtmlEncode(attachment.Name)}</a>");
                }
            }
        }

        if (step.Parameters is { Length: > 0 })
        {
            foreach (var param in step.Parameters)
            {
                if (skipTabularInline && param.Kind == StepParameterKind.Tabular) continue; // Rendered as combined table at scenario level
                if (step.TextSegments is { Length: > 0 } && param.Kind == StepParameterKind.Inline) continue; // Already rendered inline in text segments
                RenderParameter(body, param);
            }
        }

        if (step.DocString is not null)
        {
            var codeClassAttr = step.DocStringMediaType is not null
                ? $" class=\"language-{System.Net.WebUtility.HtmlEncode(step.DocStringMediaType)}\""
                : "";
            body.Append($"<pre class=\"step-docstring\"><code{codeClassAttr}>{System.Net.WebUtility.HtmlEncode(step.DocString)}</code></pre>");
        }

        if (hasSubSteps)
        {
            body.Append("</summary>");
            body.Append("<div class=\"sub-steps\">");
            for (var ssi = 0; ssi < step.SubSteps!.Length; ssi++)
            {
                var subPrefix = numberPrefix is not null ? $"{numberPrefix}{ssi + 1}." : null;
                RenderStep(body, step.SubSteps[ssi], subPrefix, isBackground: isBackground);
            }
            body.Append("</div>");
            body.Append("</details>");
        }
        else
        {
            body.Append("</div>");
        }
    }

    private static void RenderParameter(StringBuilder body, StepParameter param)
    {
        switch (param.Kind)
        {
            case StepParameterKind.Inline when param.InlineValue is not null:
                var statusClass = param.InlineValue.Status switch
                {
                    VerificationStatus.Success => "param-success",
                    VerificationStatus.Failure => "param-failure",
                    VerificationStatus.Exception => "param-exception",
                    VerificationStatus.NotProvided => "param-not-provided",
                    _ => "param-na"
                };
                var display = param.InlineValue.Expectation is not null
                    ? $"{FormatDisplayValue(param.InlineValue.Value)}/{FormatDisplayValue(param.InlineValue.Expectation)}"
                    : FormatDisplayValue(param.InlineValue.Value);
                body.Append($"<span class=\"step-param-inline {statusClass}\" title=\"{System.Net.WebUtility.HtmlEncode(param.Name)}\">{display}</span>");
                break;

            case StepParameterKind.Tabular when param.TabularValue is not null:
                var colNames = string.Join(",", param.TabularValue.Columns.Select(c => c.Name));
                body.Append($"<div class=\"step-param-table\" data-param=\"{System.Net.WebUtility.HtmlEncode(param.Name)}\" data-columns=\"{System.Net.WebUtility.HtmlEncode(colNames)}\">");
                var showRowIndicator = param.TabularValue.Rows.Any(r => r.Type != TableRowType.Matching);
                body.Append(showRowIndicator ? "<table><thead><tr><th></th>" : "<table><thead><tr>");
                foreach (var col in param.TabularValue.Columns)
                {
                    body.Append($"<th{(col.IsKey ? " class=\"key\"" : "")}>{System.Net.WebUtility.HtmlEncode(col.Name)}</th>");
                }
                body.Append("</tr></thead><tbody>");
                foreach (var row in param.TabularValue.Rows)
                {
                    var rowIndicator = row.Type switch
                    {
                        TableRowType.Matching => "=",
                        TableRowType.Surplus => "+",
                        TableRowType.Missing => "-",
                        _ => ""
                    };
                    body.Append(showRowIndicator
                        ? $"<tr class=\"row-{row.Type.ToString().ToLowerInvariant()}\"><td>{rowIndicator}</td>"
                        : $"<tr class=\"row-{row.Type.ToString().ToLowerInvariant()}\">");
                    foreach (var cell in row.Values)
                    {
                        var cellClass = cell.Status switch
                        {
                            VerificationStatus.Success => "param-success",
                            VerificationStatus.Failure => "param-failure",
                            VerificationStatus.Exception => "param-exception",
                            VerificationStatus.NotProvided => "param-not-provided",
                            _ => ""
                        };
                        var cellDisplay = cell.Expectation is not null && cell.Status == VerificationStatus.Failure
                            ? $"{FormatDisplayValue(cell.Value)}/{FormatDisplayValue(cell.Expectation)}"
                            : FormatDisplayValue(cell.Value);
                        body.Append($"<td class=\"{cellClass}\">{cellDisplay}</td>");
                    }
                    body.Append("</tr>");
                }
                body.Append("</tbody></table></div>");
                break;

            case StepParameterKind.Tree when param.TreeValue is not null:
                body.Append("<div class=\"step-param-tree\">");
                RenderTreeNode(body, param.TreeValue.Root);
                body.Append("</div>");
                break;
        }
    }

    private static void RenderTreeNode(StringBuilder body, TreeNode node)
    {
        var statusClass = node.Status switch
        {
            VerificationStatus.Success => "param-success",
            VerificationStatus.Failure => "param-failure",
            VerificationStatus.Exception => "param-exception",
            VerificationStatus.NotProvided => "param-not-provided",
            _ => ""
        };
        var valueDisplay = node.Expectation is not null && node.Status == VerificationStatus.Failure
            ? $"{FormatDisplayValue(node.Value)}/{FormatDisplayValue(node.Expectation)}"
            : FormatDisplayValue(node.Value);
        body.Append($"<div class=\"tree-node {statusClass}\"><span class=\"tree-node-name\">{System.Net.WebUtility.HtmlEncode(node.Node)}</span>: {valueDisplay}");

        if (node.Children is { Length: > 0 })
        {
            body.Append("<div class=\"tree-children\">");
            foreach (var child in node.Children)
                RenderTreeNode(body, child);
            body.Append("</div>");
        }

        body.Append("</div>");
    }

    private static bool ShouldRenderCombinedTable(ScenarioStep[] steps)
    {
        var afterThen = false;
        TabularParameterValue? setupTable = null;
        TabularParameterValue? assertionTable = null;
        foreach (var step in steps)
        {
            var keyword = step.Keyword?.Trim();
            if (keyword?.Equals("Then", StringComparison.OrdinalIgnoreCase) == true)
                afterThen = true;
            else if (keyword?.Equals("Given", StringComparison.OrdinalIgnoreCase) == true ||
                     keyword?.Equals("When", StringComparison.OrdinalIgnoreCase) == true)
                afterThen = false;

            var tab = step.Parameters?.FirstOrDefault(
                p => p.Kind == StepParameterKind.Tabular && p.TabularValue is not null)?.TabularValue;
            if (tab is not null)
            {
                if (afterThen) assertionTable ??= tab;
                else setupTable ??= tab;
            }
        }

        if (setupTable is null || assertionTable is null) return false;

        if (assertionTable.IsLinkedOutput) return true;

        var outputKeyNames = assertionTable.Columns
            .Where(c => c.IsKey).Select(c => c.Name).ToHashSet();
        if (outputKeyNames.Count > 0 &&
            setupTable.Columns.Any(c => outputKeyNames.Contains(c.Name)))
            return true;

        if (setupTable.Rows.Length > 1 &&
            setupTable.Rows.Length == assertionTable.Rows.Length)
            return true;

        return false;
    }

    private static void RenderCombinedTabularParameters(StringBuilder body, ScenarioStep[] steps)
    {
        var namedParams = steps
            .Where(s => s.Parameters is { Length: > 0 })
            .SelectMany(s => s.Parameters!)
            .Where(p => p.Kind == StepParameterKind.Tabular && p.TabularValue is not null)
            .Select(p => (Name: p.Name, Table: p.TabularValue!))
            .ToArray();

        if (namedParams.Length == 0) return;

        var hasSeparator = namedParams.Length > 1;
        var inputParams = namedParams.Length > 1 ? namedParams[..^1] : namedParams;
        var outputParam = namedParams.Length > 1 ? namedParams[^1] : ((string Name, TabularParameterValue Table)?)null;

        // Determine alignment mode
        var useKeyAlignment = false;
        HashSet<string>? sharedKeyNames = null;
        if (outputParam is not null && !outputParam.Value.Table.IsLinkedOutput)
        {
            var outputKeyNames = outputParam.Value.Table.Columns
                .Where(c => c.IsKey).Select(c => c.Name).ToHashSet();
            if (outputKeyNames.Count > 0)
            {
                sharedKeyNames = new HashSet<string>(
                    inputParams.SelectMany(p => p.Table.Columns)
                        .Where(c => outputKeyNames.Contains(c.Name))
                        .Select(c => c.Name));
                useKeyAlignment = sharedKeyNames.Count > 0;
            }
        }

        // Build aligned row pairs when using key-based alignment
        int[]? inputRowOrder = null;
        int maxRows;
        if (useKeyAlignment && outputParam is not null && inputParams.Length > 0)
        {
            var primaryInput = inputParams[0];
            var keyColIndicesInput = sharedKeyNames!
                .Select(k => Array.FindIndex(primaryInput.Table.Columns, c => c.Name == k))
                .Where(i => i >= 0).ToArray();
            var keyColIndicesOutput = sharedKeyNames!
                .Select(k => Array.FindIndex(outputParam.Value.Table.Columns, c => c.Name == k))
                .Where(i => i >= 0).ToArray();

            var inputKeyLookup = new Dictionary<string, int>();
            for (var i = 0; i < primaryInput.Table.Rows.Length; i++)
            {
                var key = string.Join("\0", keyColIndicesInput.Select(ci =>
                    ci < primaryInput.Table.Rows[i].Values.Length ? primaryInput.Table.Rows[i].Values[ci].Value : ""));
                inputKeyLookup.TryAdd(key, i);
            }

            var alignedInput = new List<int>();
            var matchedInputRows = new HashSet<int>();
            foreach (var outputRow in outputParam.Value.Table.Rows)
            {
                var outputKey = string.Join("\0", keyColIndicesOutput.Select(ci =>
                    ci < outputRow.Values.Length ? outputRow.Values[ci].Value : ""));
                if (inputKeyLookup.TryGetValue(outputKey, out var inputIdx) && matchedInputRows.Add(inputIdx))
                    alignedInput.Add(inputIdx);
                else
                    alignedInput.Add(-1); // no matching input row
            }

            // Append orphaned input rows
            for (var i = 0; i < primaryInput.Table.Rows.Length; i++)
            {
                if (!matchedInputRows.Contains(i))
                    alignedInput.Add(i);
            }

            inputRowOrder = alignedInput.ToArray();
            maxRows = inputRowOrder.Length;
        }
        else
        {
            maxRows = namedParams.Max(t => t.Table.Rows.Length);
        }

        var showRowIndicator = namedParams.Any(t => t.Table.Rows.Any(r => r.Type != TableRowType.Matching));

        body.Append(showRowIndicator
            ? "<div class=\"step-param-combined-table\"><table><thead><tr><th></th>"
            : "<div class=\"step-param-combined-table\"><table><thead><tr>");

        foreach (var param in inputParams)
        {
            var encodedName = System.Net.WebUtility.HtmlEncode(param.Name);
            foreach (var col in param.Table.Columns)
                body.Append($"<th data-param=\"{encodedName}\"{(col.IsKey ? " class=\"key\"" : "")}>{System.Net.WebUtility.HtmlEncode(col.Name)}</th>");
        }

        if (hasSeparator)
        {
            body.Append("<th class=\"combined-separator\">=</th>");

            var encodedOutputName = System.Net.WebUtility.HtmlEncode(outputParam!.Value.Name);
            foreach (var col in outputParam!.Value.Table.Columns)
                body.Append($"<th data-param=\"{encodedOutputName}\"{(col.IsKey ? " class=\"key\"" : "")}>{System.Net.WebUtility.HtmlEncode(col.Name)}</th>");
        }

        body.Append("</tr></thead><tbody>");

        for (var ri = 0; ri < maxRows; ri++)
        {
            var inputRi = inputRowOrder is not null ? inputRowOrder[ri] : ri;
            var outputRi = inputRowOrder is not null
                ? (ri < (outputParam?.Table.Rows.Length ?? 0) ? ri : -1)
                : ri;

            var rowType = outputParam is not null && outputRi >= 0 && outputRi < outputParam.Value.Table.Rows.Length
                ? outputParam.Value.Table.Rows[outputRi].Type
                : inputRi >= 0 && inputParams[0].Table.Rows.Length > inputRi
                    ? inputParams[0].Table.Rows[inputRi].Type
                    : TableRowType.Matching;

            var rowIndicator = rowType switch
            {
                TableRowType.Matching => "=",
                TableRowType.Surplus => "+",
                TableRowType.Missing => "-",
                _ => ""
            };

            body.Append(showRowIndicator
                ? $"<tr class=\"row-{rowType.ToString().ToLowerInvariant()}\"><td>{rowIndicator}</td>"
                : $"<tr class=\"row-{rowType.ToString().ToLowerInvariant()}\">");

            foreach (var param in inputParams)
            {
                var encodedName = System.Net.WebUtility.HtmlEncode(param.Name);
                if (inputRi >= 0 && inputRi < param.Table.Rows.Length)
                {
                    foreach (var cell in param.Table.Rows[inputRi].Values)
                        RenderCell(body, cell, encodedName);
                }
                else
                {
                    for (var ci = 0; ci < param.Table.Columns.Length; ci++)
                        body.Append($"<td data-param=\"{encodedName}\"></td>");
                }
            }

            if (hasSeparator)
            {
                body.Append("<td class=\"combined-separator\"></td>");

                var encodedOutputName = System.Net.WebUtility.HtmlEncode(outputParam!.Value.Name);
                if (outputRi >= 0 && outputRi < outputParam!.Value.Table.Rows.Length)
                {
                    foreach (var cell in outputParam.Value.Table.Rows[outputRi].Values)
                        RenderCell(body, cell, encodedOutputName);
                }
                else
                {
                    for (var ci = 0; ci < outputParam.Value.Table.Columns.Length; ci++)
                        body.Append($"<td data-param=\"{encodedOutputName}\"></td>");
                }
            }

            body.Append("</tr>");
        }

        body.Append("</tbody></table></div>");
    }

    private static void RenderCell(StringBuilder body, TabularCell cell, string? dataParam = null)
    {
        var cellClass = cell.Status switch
        {
            VerificationStatus.Success => "param-success",
            VerificationStatus.Failure => "param-failure",
            VerificationStatus.Exception => "param-exception",
            VerificationStatus.NotProvided => "param-not-provided",
            _ => ""
        };
        var cellDisplay = cell.Expectation is not null && cell.Status == VerificationStatus.Failure
            ? $"{FormatDisplayValue(cell.Value)}/{FormatDisplayValue(cell.Expectation)}"
            : FormatDisplayValue(cell.Value);
        var dataParamAttr = dataParam is not null ? $" data-param=\"{dataParam}\"" : "";
        body.Append($"<td class=\"{cellClass}\"{dataParamAttr}>{cellDisplay}</td>");
    }

    private static string FormatDisplayValue(string? value)
    {
        if (value is null or "null") return "<pre>null</pre>";
        if (value.Length > 0 && value.Trim().Length == 0)
            return $"<pre>{System.Net.WebUtility.HtmlEncode(value)}</pre>";
        return System.Net.WebUtility.HtmlEncode(value);
    }

    private static readonly Regex StripTabularParamSuffixCompiledRegex = new(@"\s*\[[a-zA-Z_]\w*:\s*""<\$[a-zA-Z_]\w*>""\]", RegexOptions.Compiled);
    private static Regex StripTabularParamSuffixRegex() => StripTabularParamSuffixCompiledRegex;

    internal static string FormatDurationBadge(TimeSpan duration)
    {
        var total = duration.Duration();
        if (total.TotalSeconds < 1)
            return $"{(int)total.TotalMilliseconds}ms";
        if (total.TotalMinutes < 1)
            return $"{total.TotalSeconds:F1}s";
        return $"{(int)total.TotalMinutes}m {total.Seconds}s";
    }

    internal static string GenerateScenarioAnchorId(string displayName)
    {
        // Convert to lowercase, replace non-alphanumeric with hyphens, collapse multiple hyphens
        var slug = System.Text.RegularExpressions.Regex.Replace(displayName.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return $"scenario-{slug}";
    }

    /// <summary>
    /// The "Report diagnostics" block of <c>TestRunReport.html</c>: a collapsed <c>&lt;details&gt;</c> listing
    /// every <see cref="DiagnosticEntry"/> the generation (and the host, via
    /// <see cref="Kronikol.Ingestion.IngestRequest.HostDiagnostics"/>) recorded — kind, message, scenario —
    /// so a dead tap or a skipped capture line is a line in the report, not only in a log. Empty input
    /// renders nothing.
    /// </summary>
    internal static string RenderReportDiagnostics(IReadOnlyList<DiagnosticEntry> diagnostics)
    {
        if (diagnostics.Count == 0)
            return string.Empty;

        var byKind = diagnostics.GroupBy(d => d.Kind)
            .OrderByDescending(g => g.Key == DiagnosticKind.CaptureDegraded)
            .ThenByDescending(g => g.Count())
            .Select(g => g.Count() == 1 ? g.Key.ToString() : $"{g.Key} ×{g.Count()}");
        var summary = $"Report diagnostics ({diagnostics.Count}: {string.Join(", ", byKind)})";

        var html = new StringBuilder();
        html.Append("<details class=\"report-diagnostics\">");
        html.Append($"<summary>{summary}</summary>"); // enum names and counts only — nothing to encode, and the × must stay a glyph
        html.Append("<ul class=\"report-diagnostics-list\">");
        foreach (var entry in diagnostics)
        {
            var kindClass = $"report-diagnostic-kind report-diagnostic-kind-{entry.Kind.ToString().ToLowerInvariant()}";
            html.Append($"<li><span class=\"{kindClass}\">{System.Net.WebUtility.HtmlEncode(entry.Kind.ToString())}</span> ");
            html.Append(System.Net.WebUtility.HtmlEncode(entry.Message));
            if (!string.IsNullOrEmpty(entry.ScenarioId))
                html.Append($" <span class=\"report-diagnostic-scenario\">[{System.Net.WebUtility.HtmlEncode(entry.ScenarioId)}]</span>");
            html.Append("</li>");
        }
        html.Append("</ul></details>");
        return html.ToString();
    }

    /// <summary>The <c>diagnostics</c> array of the data files: <c>{kind, message, scenarioId}</c> per entry.</summary>
    private static object[] MapDiagnosticsJson(IReadOnlyList<DiagnosticEntry>? diagnostics) =>
        (diagnostics ?? []).Select(d => (object)new { Kind = d.Kind.ToString(), d.Message, d.ScenarioId }).ToArray();

    public static string GenerateTestRunReportData(Feature[] features, DateTime startTime, DateTime endTime, string fileName, DataFormat format, DefaultDiagramsFetcher.DiagramAsCode[]? diagrams = null, RequestResponseLog[]? trackedLogs = null, IReadOnlyList<DiagnosticEntry>? diagnostics = null, bool fullStepDetail = true)
    {
        var diagramLookup = diagrams?.ToLookup(d => d.TestRuntimeId, d => d.CodeBehind);
        // Diagram markers belong to the diagram, not the interaction list: exported as-is they read as
        // content-free calls to http://override.com/ — one pair per Gherkin step and assertion.
        var logLookup = trackedLogs?.Where(l => !l.IsDiagramMarker).ToLookup(l => l.TestId);
        var durations = ComputeInteractionDurations(trackedLogs);
        var (stepPaths, annotations) = AttributeInteractionsToSteps(trackedLogs, features);

        return format switch
        {
            DataFormat.Json => WriteFile(GenerateTestRunReportJson(features, startTime, endTime, diagramLookup, logLookup, diagnostics, fullStepDetail, durations, stepPaths, annotations), fileName),
            DataFormat.Xml => WriteFile(GenerateTestRunReportXml(features, startTime, endTime, diagramLookup, logLookup, durations, stepPaths), fileName),
            DataFormat.Yaml => WriteFile(GenerateTestRunReportYaml(features, startTime, endTime, diagramLookup, logLookup, durations, stepPaths), fileName),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
    }

    /// <summary>
    /// One scenario-level annotation: a diagram marker that carries information found nowhere else in the
    /// data file. Step and assertion markers are deliberately excluded — those are already structured in
    /// <c>steps</c>, and repeating them would be duplication rather than disclosure.
    /// </summary>
    private sealed record ScenarioAnnotation(int Index, DiagramMarkerKind Kind, string Text);

    /// <summary>
    /// Walks one test's ordered log stream and works out, for every real interaction, which step it happened
    /// under, plus the annotations worth exporting.
    ///
    /// <para>Attribution is positional: the <em>n</em>th step marker opens the <em>n</em>th step in
    /// document order (background steps first). That is sound because <c>RequestResponseLogger</c> is a
    /// FIFO queue, so one test's records keep their relative order however many tests run in parallel. It
    /// is not sound for a test that does work on a background thread, where a record can enqueue after the
    /// marker for the following step — so the marker's text is checked against the step's, and a
    /// disagreement produces a null <c>stepPath</c> and a diagnostic rather than a confident wrong
    /// answer.</para>
    /// </summary>
    private static (Dictionary<string, List<string?>> StepPaths, Dictionary<string, List<ScenarioAnnotation>> Annotations)
        AttributeInteractionsToSteps(RequestResponseLog[]? trackedLogs, Feature[] features)
    {
        var stepPaths = new Dictionary<string, List<string?>>();
        var annotations = new Dictionary<string, List<ScenarioAnnotation>>();
        if (trackedLogs is null || trackedLogs.Length == 0)
            return (stepPaths, annotations);

        var stepsByTestId = features
            .SelectMany(f => f.Scenarios)
            .GroupBy(s => s.Id)
            .ToDictionary(g => g.Key, g => OrderedStepPaths(g.First()));

        foreach (var perTest in trackedLogs.GroupBy(l => l.TestId))
        {
            var ordered = stepsByTestId.TryGetValue(perTest.Key, out var s) ? s : [];
            var paths = new List<string?>();
            var found = new List<ScenarioAnnotation>();
            string? current = null;
            var stepMarkerCount = 0;
            var interactionIndex = 0;

            foreach (var log in perTest)
            {
                if (!log.IsDiagramMarker)
                {
                    paths.Add(current);
                    interactionIndex++;
                    continue;
                }

                // The pair straddles the fragment; only the opening half carries it.
                if (!log.IsOverrideStart || log.PlantUml is null)
                    continue;

                switch (log.MarkerKind)
                {
                    case DiagramMarkerKind.Step:
                        current = stepMarkerCount < ordered.Count && StepMarkerMatches(log.PlantUml, ordered[stepMarkerCount].Text)
                            ? ordered[stepMarkerCount].Path
                            : null;

                        if (current is null && stepMarkerCount < ordered.Count)
                            ReportDiagnosticsScope.Record(DiagnosticKind.StepAttributionMismatch,
                                $"Step marker {stepMarkerCount + 1} does not match step '{ordered[stepMarkerCount].Text}'; interactions after it carry no stepPath.",
                                perTest.Key);

                        stepMarkerCount++;
                        break;

                    case DiagramMarkerKind.Row or DiagramMarkerKind.Custom:
                        found.Add(new ScenarioAnnotation(interactionIndex, log.MarkerKind, AnnotationText(log.PlantUml)));
                        break;
                }
            }

            stepPaths[perTest.Key] = paths;
            annotations[perTest.Key] = found;
        }

        return (stepPaths, annotations);
    }

    /// <summary>
    /// Every step of a scenario in the order its marker will arrive, paired with the address it gets in the
    /// data file: <c>b0</c>, <c>b1</c> for background steps, then <c>0</c>, <c>1</c> for the scenario's own.
    /// Only top-level steps appear — a step delimiter is emitted for those alone.
    /// </summary>
    private static List<(string Path, string Text)> OrderedStepPaths(Scenario scenario)
    {
        var ordered = new List<(string, string)>();
        for (var i = 0; i < (scenario.BackgroundSteps?.Length ?? 0); i++)
            ordered.Add(($"b{i}", scenario.BackgroundSteps![i].Text));
        for (var i = 0; i < (scenario.Steps?.Length ?? 0); i++)
            ordered.Add(($"{i}", scenario.Steps![i].Text));
        return ordered;
    }

    /// <summary>
    /// Whether a step delimiter's PlantUML belongs to a given step. The bar's label is the step text, but
    /// possibly with the keyword prepended and the first letter capitalised, so this compares loosely: the
    /// answer is only used to decide whether to trust positional attribution at all.
    /// </summary>
    private static bool StepMarkerMatches(string plantUml, string stepText)
    {
        if (string.IsNullOrWhiteSpace(stepText))
            return true;

        var marker = plantUml.Replace('\n', ' ').Trim();
        return marker.Contains(stepText, StringComparison.OrdinalIgnoreCase)
               || marker.Contains(StepText.CapitaliseIfEnabled(stepText) ?? stepText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The readable half of an annotation marker: everything after the PlantUML note preamble. Falls back to
    /// the fragment as written when it is not a one-line note, because a partially-parsed annotation is
    /// worse than a verbatim one.
    /// </summary>
    private static string AnnotationText(string plantUml)
    {
        var text = plantUml.Trim();
        var colon = text.IndexOf(" : ", StringComparison.Ordinal);
        if (colon >= 0)
            return text[(colon + 3)..].Trim();

        var firstLine = text.Split('\n')[0];
        return firstLine.Length == text.Length ? text : text[firstLine.Length..].Trim();
    }

    /// <summary>
    /// Wall-clock duration per request/response pair, keyed on <see cref="RequestResponseLog.RequestResponseId"/>.
    /// The record itself has no duration field — the diagram derives it the same way, from the timestamps of
    /// the two halves — so the data files derive it too rather than making every reader do the join.
    /// Both halves of a pair get the same value; an unanswered request gets none.
    /// </summary>
    private static Dictionary<Guid, double> ComputeInteractionDurations(RequestResponseLog[]? trackedLogs)
    {
        var durations = new Dictionary<Guid, double>();
        if (trackedLogs is null)
            return durations;

        foreach (var pair in trackedLogs.Where(l => !l.IsDiagramMarker).GroupBy(l => l.RequestResponseId))
        {
            // A capturer that measured the call itself is believed over anything inferred here — it is the
            // only source for a call sent as a single record, which the NDJSON ingest contract permits.
            if (pair.Select(l => l.DurationMs).FirstOrDefault(d => d is not null) is { } measured)
            {
                durations[pair.Key] = measured;
                continue;
            }

            var request = pair.FirstOrDefault(l => l.Type == RequestResponseType.Request);
            var response = pair.FirstOrDefault(l => l.Type == RequestResponseType.Response);
            if (request?.Timestamp is not { } start || response?.Timestamp is not { } end)
                continue;

            var elapsed = (end - start).TotalMilliseconds;
            if (elapsed >= 0)
                durations[pair.Key] = elapsed;
        }

        return durations;
    }

    private static string GenerateTestRunReportJson(Feature[] features, DateTime startTime, DateTime endTime, ILookup<string, string>? diagramLookup, ILookup<string, RequestResponseLog>? logLookup, IReadOnlyList<DiagnosticEntry>? diagnostics = null, bool fullStepDetail = true, IReadOnlyDictionary<Guid, double>? durations = null, IReadOnlyDictionary<string, List<string?>>? stepPaths = null, IReadOnlyDictionary<string, List<ScenarioAnnotation>>? annotations = null)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
        var data = new
        {
            KronikolVersion = KronikolVersion,
            StartTime = startTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
            EndTime = endTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
            Features = BuildFeaturesJsonModel(features, diagramLookup, logLookup, fullStepDetail, durations, stepPaths, annotations),
            Diagnostics = MapDiagnosticsJson(diagnostics)
        };
        return JsonSerializer.Serialize(data, options);
    }

    /// <summary>
    /// Builds the serializable feature/scenario model shared by the standard test-run report JSON
    /// and the enriched "mergeable" JSON. Keeping a single source of truth ensures the mergeable
    /// format remains a strict superset that the merge reader can parse.
    /// </summary>
    private static object[] BuildFeaturesJsonModel(Feature[] features, ILookup<string, string>? diagramLookup, ILookup<string, RequestResponseLog>? logLookup, bool fullStepDetail = false, IReadOnlyDictionary<Guid, double>? durations = null, IReadOnlyDictionary<string, List<string?>>? stepPaths = null, IReadOnlyDictionary<string, List<ScenarioAnnotation>>? annotations = null)
    {
        Func<ScenarioStep, object> stepMapper = fullStepDetail ? MapStepJsonFull : MapStepJson;
        return features.OrderBy(f => f.DisplayName).Select(f => (object)new Dictionary<string, object?>
        {
            ["name"] = f.DisplayName,
            ["endpoint"] = f.Endpoint,
            ["description"] = f.Description,
            ["labels"] = f.Labels ?? [],
            ["scenarios"] = f.Scenarios.Select(s =>
            {
                var scenario = new Dictionary<string, object?>
                {
                    ["id"] = s.Id,
                    ["stableId"] = ScenarioStableId.Compute(f.DisplayName, s.DisplayName, s.OutlineId, s.ExampleValues),
                    ["name"] = s.DisplayName,
                    ["description"] = s.Description,
                    ["result"] = s.Result.ToString(),
                    ["durationSeconds"] = s.Duration?.TotalSeconds ?? 0.0,
                    ["isHappyPath"] = s.IsHappyPath,
                    ["errorMessage"] = s.ErrorMessage,
                    ["errorStackTrace"] = s.ErrorStackTrace,
                    ["labels"] = s.Labels ?? [],
                    ["categories"] = s.Categories ?? [],
                    ["rule"] = s.Rule,
                    ["outlineId"] = s.OutlineId,
                    ["examplesBlockName"] = s.ExamplesBlockName,
                    ["examplesBlockDescription"] = s.ExamplesBlockDescription,
                    ["examplesBlockIndex"] = s.ExamplesBlockIndex,
                    ["exampleValues"] = s.ExampleValues,
                    // The flattened view drives the pivot table's columns; without it a merged report
                    // loses the parameterised grouping the original had.
                    ["exampleFlatValues"] = s.ExampleFlatValues,
                    ["exampleDisplayName"] = s.ExampleDisplayName,
                    ["attachments"] = (s.Attachments ?? []).Select(MapAttachmentJson).ToArray(),
                    ["backgroundSteps"] = (s.BackgroundSteps ?? []).Select(stepMapper).ToArray(),
                    ["steps"] = (s.Steps ?? []).Select(stepMapper).ToArray()
                };

                if (diagramLookup != null)
                    scenario["diagrams"] = diagramLookup[s.Id].ToArray();

                if (logLookup != null)
                {
                    var paths = stepPaths is not null && stepPaths.TryGetValue(s.Id, out var p) ? p : null;
                    scenario["httpInteractions"] = logLookup[s.Id]
                        .Select((l, i) => MapLogJson(l, durations, paths is not null && i < paths.Count ? paths[i] : null))
                        .ToArray();
                    scenario["annotations"] = (annotations is not null && annotations.TryGetValue(s.Id, out var a) ? a : [])
                        .Select(x => (object)new { x.Index, Kind = x.Kind.ToString(), x.Text })
                        .ToArray();
                }

                return scenario;
            }).ToArray()
        }).ToArray();
    }

    /// <summary>
    /// Assembles the enriched "mergeable" report from in-process state captured during a test run:
    /// extracts component relationships from the tracked logs, precomputes the self-contained
    /// internal-flow segment data and per-scenario whole-test-flow fragments (inlining their payloads
    /// so no shared diagram-data map is required), then serializes everything to JSON.
    /// </summary>
    private static string BuildMergeableReportJson(
        Feature[] features,
        DateTime startTime,
        DateTime endTime,
        DefaultDiagramsFetcher.DiagramAsCode[]? diagrams,
        RequestResponseLog[]? trackedLogs,
        Dictionary<string, InternalFlowSegment>? perBoundarySegments,
        Dictionary<string, InternalFlowSegment>? wholeTestSegments,
        CiMetadata? ciMetadata,
        ReportConfigurationOptions options,
        IReadOnlyList<DiagnosticEntry>? diagnostics = null)
    {
        var diagramLookup = diagrams?.ToLookup(d => d.TestRuntimeId, d => d.CodeBehind);

        var componentOptions = options.ComponentDiagramOptions ?? new ComponentDiagramOptions();
        var componentLogs = (trackedLogs ?? RequestResponseLogger.RequestAndResponseLogs.Where(x => !(x?.TrackingIgnore ?? true)).ToArray());
        var relationships = ComponentDiagramGenerator.ExtractRelationships(componentLogs, componentOptions.ParticipantFilter);

        Dictionary<string, object>? internalFlowSegmentData = null;
        if (perBoundarySegments is not null)
        {
            internalFlowSegmentData = InternalFlowHtmlGenerator.BuildSegmentData(
                perBoundarySegments,
                options.InternalFlowDiagramStyle,
                options.InternalFlowShowFlameChart,
                options.InternalFlowFlameChartPosition,
                options.InternalFlowNoDataBehavior,
                options.InternalFlowSpanGranularity,
                options.InternalFlowActivitySources);
        }

        Dictionary<string, Merge.WholeTestFlowFragment>? wholeTestFlow = null;
        if (wholeTestSegments is not null && options.WholeTestFlowVisualization != WholeTestFlowVisualization.None)
        {
            wholeTestFlow = new Dictionary<string, Merge.WholeTestFlowFragment>();
            foreach (var scenario in features.SelectMany(f => f.Scenarios))
            {
                var boundaryLogs = trackedLogs?
                    .Where(l => l.TestId == scenario.Id && l.Type == RequestResponseType.Request && l.Timestamp.HasValue)
                    .OrderBy(l => l.Timestamp!.Value)
                    .Select(l => ($"{l.Method.Value}: {l.Uri.PathAndQuery}", l.Timestamp!.Value))
                    .ToArray() ?? [];

                // diagramDataMap left null so payloads are inlined into the fragment HTML.
                var content = InternalFlowHtmlGenerator.GetWholeTestFlowContent(
                    wholeTestSegments, scenario.Id, boundaryLogs, options.WholeTestFlowVisualization, diagramDataMap: null);

                if (content is { } c)
                    wholeTestFlow[scenario.Id] = new Merge.WholeTestFlowFragment(c.ActivityHtml, c.FlameHtml, c.SpanCount);
            }
        }

        return GenerateMergeableReportJson(
            features, startTime, endTime, diagramLookup,
            relationships, internalFlowSegmentData, wholeTestFlow,
            options.WholeTestFlowVisualization, ciMetadata, diagnostics);
    }

    /// <summary>
    /// Serializes the enriched "mergeable" test-run report: the standard JSON model plus everything
    /// needed to reconstruct a full HTML report when merging multiple files — component relationships,
    /// precomputed internal-flow segment data, precomputed whole-test-flow fragments, and CI metadata.
    /// </summary>
    internal static string GenerateMergeableReportJson(
        Feature[] features,
        DateTime startTime,
        DateTime endTime,
        ILookup<string, string>? diagramLookup,
        Kronikol.ComponentDiagram.ComponentRelationship[]? componentRelationships,
        Dictionary<string, object>? internalFlowSegmentData,
        Dictionary<string, Merge.WholeTestFlowFragment>? wholeTestFlow,
        WholeTestFlowVisualization wholeTestVisualization,
        CiMetadata? ciMetadata,
        IReadOnlyList<DiagnosticEntry>? diagnostics = null)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
        var data = new Dictionary<string, object?>
        {
            ["kronikolVersion"] = KronikolVersion,
            ["mergeableFormatVersion"] = 1,
            ["startTime"] = startTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["endTime"] = endTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["features"] = BuildFeaturesJsonModel(features, diagramLookup, logLookup: null, fullStepDetail: true),
            ["wholeTestVisualization"] = wholeTestVisualization.ToString(),
            ["componentRelationships"] = (componentRelationships ?? []).Select(r => new
            {
                r.Caller,
                r.Service,
                r.Protocol,
                Methods = r.Methods.OrderBy(m => m).ToArray(),
                r.CallCount,
                r.TestCount,
                r.DependencyCategory
            }).ToArray(),
            ["internalFlowSegments"] = internalFlowSegmentData ?? new Dictionary<string, object>(),
            ["wholeTestFlow"] = (wholeTestFlow ?? new Dictionary<string, Merge.WholeTestFlowFragment>())
                .ToDictionary(kvp => kvp.Key, kvp => (object)new
                {
                    kvp.Value.ActivityHtml,
                    kvp.Value.FlameHtml,
                    kvp.Value.SpanCount
                }),
            ["ciMetadata"] = ciMetadata is null ? null : new
            {
                Provider = ciMetadata.Provider.ToString(),
                ciMetadata.BuildNumber,
                ciMetadata.Branch,
                ciMetadata.CommitSha,
                ciMetadata.PipelineUrl,
                ciMetadata.Repository,
                ciMetadata.RunId
            },
            ["diagnostics"] = MapDiagnosticsJson(diagnostics)
        };
        return JsonSerializer.Serialize(data, options);
    }

    /// <summary>
    /// An interaction in the data files. Everything the diagram renderer reads off the record travels with
    /// it — the categorisation that decides participant shape, the phase, the W3C trace ids that bridge to
    /// OpenTelemetry and application logs, which capture path produced it, and the derived duration —
    /// so a reader of the JSON is never told less than a reader of the diagram.
    /// </summary>
    private static object MapLogJson(RequestResponseLog log, IReadOnlyDictionary<Guid, double>? durations = null, string? stepPath = null) => new
    {
        Type = log.Type.ToString(),
        Method = log.Method.Value?.ToString()?.ToUpperInvariant(),
        Uri = log.Uri.ToString(),
        log.ServiceName,
        log.CallerName,
        log.Content,
        Headers = log.Headers.Select(h => new { h.Key, h.Value }).ToArray(),
        StatusCode = log.StatusCode?.Value?.ToString(),
        TraceId = log.TraceId.ToString(),
        RequestResponseId = log.RequestResponseId.ToString(),
        Timestamp = log.Timestamp?.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
        MetaType = log.MetaType.ToString(),
        log.DependencyCategory,
        log.CallerDependencyCategory,
        Phase = log.Phase.ToString(),
        log.IsUserAction,
        log.ActivityTraceId,
        log.ActivitySpanId,
        log.CapturedBy,
        DurationMs = durations is not null && durations.TryGetValue(log.RequestResponseId, out var ms) ? ms : (double?)null,
        StepPath = stepPath
    };

    /// <summary>
    /// An attachment in the data files: the display name, where it is, and the media type the producer
    /// declared (null when it declared none — the renderer then sniffs the extension).
    /// </summary>
    private static object MapAttachmentJson(FileAttachment attachment) => new Dictionary<string, object?>
    {
        ["name"] = attachment.Name,
        ["relativePath"] = attachment.RelativePath,
        ["mediaType"] = attachment.MediaType,
    };

    /// <inheritdoc cref="MapAttachmentJson"/>
    private static XElement MapAttachmentXml(FileAttachment attachment) =>
        new("Attachment",
            new XElement("Name", attachment.Name),
            new XElement("RelativePath", attachment.RelativePath),
            attachment.MediaType != null ? new XElement("MediaType", attachment.MediaType) : null);

    private static object MapStepJson(ScenarioStep step) => new
    {
        step.Keyword,
        step.Text,
        Status = step.Status?.ToString(),
        DurationSeconds = step.Duration?.TotalSeconds,
        // Failure detail rides on the lean mapper too: the small file is for saving payload bytes, not for
        // withholding why the test failed.
        step.FailureMessage,
        step.SourceFile,
        step.SourceLine,
        SubSteps = (step.SubSteps ?? []).Select(MapStepJson).ToArray(),
        Attachments = (step.Attachments ?? []).Select(MapAttachmentJson).ToArray()
    };

    /// <summary>
    /// Step mapping for the mergeable report — a superset of <see cref="MapStepJson"/> that also carries
    /// the data needed for full rendering fidelity when merging: inline parameter highlighting
    /// (<see cref="ScenarioStep.TextSegments"/>), tabular/tree/inline parameters, doc-strings, comments
    /// and bypass reason.
    /// </summary>
    private static object MapStepJsonFull(ScenarioStep step) => new
    {
        step.Keyword,
        step.Text,
        Status = step.Status?.ToString(),
        DurationSeconds = step.Duration?.TotalSeconds,
        step.BypassReason,
        step.DocString,
        step.DocStringMediaType,
        step.FailureMessage,
        step.SourceFile,
        step.SourceLine,
        Comments = step.Comments ?? [],
        SubSteps = (step.SubSteps ?? []).Select(MapStepJsonFull).ToArray(),
        Attachments = (step.Attachments ?? []).Select(MapAttachmentJson).ToArray(),
        Parameters = (step.Parameters ?? []).Select(MapStepParameterJson).ToArray(),
        TextSegments = step.TextSegments?.Select(MapTextSegmentJson).ToArray()
    };

    private static object MapStepParameterJson(StepParameter p) => new
    {
        p.Name,
        Kind = p.Kind.ToString(),
        InlineValue = p.InlineValue is null ? null : MapInlineValueJson(p.InlineValue),
        TabularValue = p.TabularValue is null ? null : new
        {
            Columns = p.TabularValue.Columns.Select(c => new { c.Name, c.IsKey }).ToArray(),
            Rows = p.TabularValue.Rows.Select(r => new
            {
                Type = r.Type.ToString(),
                Values = r.Values.Select(MapCellJson).ToArray()
            }).ToArray(),
            p.TabularValue.IsLinkedOutput
        },
        TreeValue = p.TreeValue is null ? null : new { Root = MapTreeNodeJson(p.TreeValue.Root) }
    };

    private static object MapInlineValueJson(InlineParameterValue v) => new
    {
        v.Value,
        v.Expectation,
        Status = v.Status.ToString()
    };

    private static object MapCellJson(TabularCell c) => new
    {
        c.Value,
        c.Expectation,
        Status = c.Status.ToString()
    };

    private static object MapTreeNodeJson(TreeNode n) => new
    {
        n.Path,
        n.Node,
        n.Value,
        n.Expectation,
        Status = n.Status.ToString(),
        Children = n.Children?.Select(MapTreeNodeJson).ToArray()
    };

    private static object MapTextSegmentJson(StepTextSegment s) => new
    {
        s.Text,
        s.ParameterName,
        Parameter = s.Parameter is null ? null : MapInlineValueJson(s.Parameter),
        s.TableReference,
        s.TableReferenceFormattedValue
    };

    private static string GenerateTestRunReportXml(Feature[] features, DateTime startTime, DateTime endTime, ILookup<string, string>? diagramLookup, ILookup<string, RequestResponseLog>? logLookup, IReadOnlyDictionary<Guid, double>? durations = null, IReadOnlyDictionary<string, List<string?>>? stepPaths = null)
    {
        var doc = new XDocument(
            new XElement("TestRunReport",
                new XElement("KronikolVersion", KronikolVersion),
                new XElement("StartTime", startTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")),
                new XElement("EndTime", endTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")),
                new XElement("Features",
                    features.OrderBy(f => f.DisplayName).Select(f =>
                        new XElement("Feature",
                            new XElement("Name", f.DisplayName),
                            f.Endpoint != null ? new XElement("Endpoint", f.Endpoint) : null,
                            f.Description != null ? new XElement("Description", f.Description) : null,
                            (f.Labels is { Length: > 0 }) ? new XElement("Labels", f.Labels.Select(l => new XElement("Label", l))) : null,
                            new XElement("Scenarios",
                                f.Scenarios.Select(s =>
                                {
                                    var scenarioElements = new List<object?>
                                    {
                                        new XElement("Id", s.Id),
                                        new XElement("StableId", ScenarioStableId.Compute(f.DisplayName, s.DisplayName, s.OutlineId, s.ExampleValues)),
                                        new XElement("Name", s.DisplayName),
                                        s.Description != null ? new XElement("Description", s.Description) : null,
                                        new XElement("Result", s.Result.ToString()),
                                        new XElement("DurationSeconds", (s.Duration?.TotalSeconds ?? 0.0).ToString("F3")),
                                        new XElement("IsHappyPath", s.IsHappyPath.ToString().ToLower()),
                                        s.ErrorMessage != null ? new XElement("ErrorMessage", s.ErrorMessage) : null,
                                        s.ErrorStackTrace != null ? new XElement("ErrorStackTrace", s.ErrorStackTrace) : null,
                                        (s.Labels is { Length: > 0 }) ? new XElement("Labels", s.Labels.Select(l => new XElement("Label", l))) : null,
                                        (s.Categories is { Length: > 0 }) ? new XElement("Categories", s.Categories.Select(c => new XElement("Category", c))) : null,
                                        s.Rule != null ? new XElement("Rule", s.Rule) : null,
                                        (s.BackgroundSteps is { Length: > 0 }) ? new XElement("BackgroundSteps", s.BackgroundSteps.Select(MapStepXml)) : null,
                                        (s.Steps is { Length: > 0 }) ? new XElement("Steps", s.Steps.Select(MapStepXml)) : null,
                                        (s.Attachments is { Length: > 0 }) ? new XElement("Attachments", s.Attachments.Select(MapAttachmentXml)) : null
                                    };

                                    if (diagramLookup != null)
                                    {
                                        var diags = diagramLookup[s.Id].ToArray();
                                        if (diags.Length > 0)
                                            scenarioElements.Add(new XElement("Diagrams", diags.Select(d => new XElement("Diagram", d))));
                                    }

                                    if (logLookup != null)
                                    {
                                        var logs = logLookup[s.Id].ToArray();
                                        if (logs.Length > 0)
                                            scenarioElements.Add(new XElement("HttpInteractions", logs.Select((l, i) => MapLogXml(l, durations, StepPathAt(stepPaths, s.Id, i)))));
                                    }

                                    return new XElement("Scenario", scenarioElements.ToArray());
                                })
                            )
                        )
                    )
                )
            )
        );
        return doc.ToString();
    }

    /// <inheritdoc cref="MapLogJson"/>
    private static XElement MapLogXml(RequestResponseLog log, IReadOnlyDictionary<Guid, double>? durations = null, string? stepPath = null) =>
        new("HttpInteraction",
            new XElement("Type", log.Type.ToString()),
            new XElement("Method", log.Method.Value?.ToString()?.ToUpperInvariant()),
            new XElement("Uri", log.Uri.ToString()),
            new XElement("ServiceName", log.ServiceName),
            new XElement("CallerName", log.CallerName),
            log.Content != null ? new XElement("Content", log.Content) : null,
            log.Headers.Length > 0 ? new XElement("Headers", log.Headers.Select(h => new XElement("Header", new XElement("Key", h.Key), new XElement("Value", h.Value)))) : null,
            log.StatusCode != null ? new XElement("StatusCode", log.StatusCode.Value?.ToString()) : null,
            new XElement("TraceId", log.TraceId.ToString()),
            new XElement("RequestResponseId", log.RequestResponseId.ToString()),
            log.Timestamp != null ? new XElement("Timestamp", log.Timestamp.Value.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")) : null,
            // XML omits what carries nothing rather than writing empty elements, as the rest of this writer does.
            log.MetaType != RequestResponseMetaType.Default ? new XElement("MetaType", log.MetaType.ToString()) : null,
            log.DependencyCategory != null ? new XElement("DependencyCategory", log.DependencyCategory) : null,
            log.CallerDependencyCategory != null ? new XElement("CallerDependencyCategory", log.CallerDependencyCategory) : null,
            log.Phase != TestPhase.Unknown ? new XElement("Phase", log.Phase.ToString()) : null,
            log.IsUserAction ? new XElement("IsUserAction", "true") : null,
            log.ActivityTraceId != null ? new XElement("ActivityTraceId", log.ActivityTraceId) : null,
            log.ActivitySpanId != null ? new XElement("ActivitySpanId", log.ActivitySpanId) : null,
            log.CapturedBy != null ? new XElement("CapturedBy", log.CapturedBy) : null,
            durations is not null && durations.TryGetValue(log.RequestResponseId, out var ms)
                ? new XElement("DurationMs", ms.ToString("F3", CultureInfo.InvariantCulture))
                : null,
            stepPath != null ? new XElement("StepPath", stepPath) : null
        );

    /// <summary>The step address for the <paramref name="index"/>th interaction of a scenario, if one was worked out.</summary>
    private static string? StepPathAt(IReadOnlyDictionary<string, List<string?>>? stepPaths, string scenarioId, int index) =>
        stepPaths is not null && stepPaths.TryGetValue(scenarioId, out var paths) && index < paths.Count
            ? paths[index]
            : null;

    private static XElement MapStepXml(ScenarioStep step) =>
        new("Step",
            step.Keyword != null ? new XElement("Keyword", step.Keyword) : null,
            new XElement("Text", step.Text),
            step.Status != null ? new XElement("Status", step.Status.ToString()) : null,
            step.Duration != null ? new XElement("DurationSeconds", step.Duration.Value.TotalSeconds.ToString("F3")) : null,
            step.FailureMessage != null ? new XElement("FailureMessage", step.FailureMessage) : null,
            step.SourceFile != null ? new XElement("SourceFile", step.SourceFile) : null,
            step.SourceLine != null ? new XElement("SourceLine", step.SourceLine.Value.ToString(CultureInfo.InvariantCulture)) : null,
            (step.SubSteps is { Length: > 0 }) ? new XElement("SubSteps", step.SubSteps.Select(MapStepXml)) : null,
            (step.Attachments is { Length: > 0 }) ? new XElement("Attachments", step.Attachments.Select(MapAttachmentXml)) : null
        );

    private static string GenerateTestRunReportYaml(Feature[] features, DateTime startTime, DateTime endTime, ILookup<string, string>? diagramLookup, ILookup<string, RequestResponseLog>? logLookup, IReadOnlyDictionary<Guid, double>? durations = null, IReadOnlyDictionary<string, List<string?>>? stepPaths = null)
    {
        var yml = new StringBuilder();
        yml.Append("KronikolVersion: " + KronikolVersion + "\n");
        yml.Append("StartTime: " + startTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ") + "\n");
        yml.Append("EndTime: " + endTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ") + "\n");
        yml.Append("Features:\n");

        foreach (var feature in features.OrderBy(f => f.DisplayName))
        {
            yml.Append("  - Name: " + feature.DisplayName.SanitiseForYml() + "\n");

            if (feature.Endpoint is not null)
                yml.Append("    Endpoint: " + feature.Endpoint + "\n");

            if (feature.Description is not null)
                yml.Append("    Description: " + feature.Description.SanitiseForYml() + "\n");

            if (feature.Labels is { Length: > 0 })
            {
                yml.Append("    Labels:\n");
                foreach (var label in feature.Labels)
                    yml.Append("      - " + label.SanitiseForYml() + "\n");
            }

            yml.Append("    Scenarios:\n");
            foreach (var scenario in feature.Scenarios)
            {
                yml.Append("      - Name: " + scenario.DisplayName.SanitiseForYml() + "\n");
                yml.Append("        StableId: " + ScenarioStableId.Compute(feature.DisplayName, scenario.DisplayName, scenario.OutlineId, scenario.ExampleValues) + "\n");
                if (scenario.Description is not null)
                    yml.Append("        Description: " + scenario.Description.SanitiseForYml() + "\n");
                yml.Append("        Result: " + scenario.Result + "\n");
                yml.Append("        DurationSeconds: " + (scenario.Duration?.TotalSeconds ?? 0.0).ToString("F3") + "\n");
                yml.Append("        IsHappyPath: " + scenario.IsHappyPath.ToString().ToLower() + "\n");

                if (scenario.ErrorMessage is not null)
                    yml.Append("        ErrorMessage: " + scenario.ErrorMessage.SanitiseForYml() + "\n");

                if (scenario.ErrorStackTrace is not null)
                    yml.Append("        ErrorStackTrace: " + scenario.ErrorStackTrace.SanitiseForYml() + "\n");

                if (scenario.Labels is { Length: > 0 })
                {
                    yml.Append("        Labels:\n");
                    foreach (var label in scenario.Labels)
                        yml.Append("          - " + label.SanitiseForYml() + "\n");
                }

                if (scenario.Categories is { Length: > 0 })
                {
                    yml.Append("        Categories:\n");
                    foreach (var cat in scenario.Categories)
                        yml.Append("          - " + cat.SanitiseForYml() + "\n");
                }

                if (scenario.Rule is not null)
                    yml.Append("        Rule: " + scenario.Rule.SanitiseForYml() + "\n");

                if (scenario.BackgroundSteps is { Length: > 0 })
                {
                    yml.Append("        BackgroundSteps:\n");
                    foreach (var step in scenario.BackgroundSteps)
                        AppendTestRunYamlStep(yml, step, "          ");
                }

                if (scenario.Steps is { Length: > 0 })
                {
                    yml.Append("        Steps:\n");
                    foreach (var step in scenario.Steps)
                        AppendTestRunYamlStep(yml, step, "          ");
                }

                if (scenario.Attachments is { Length: > 0 })
                {
                    yml.Append("        Attachments:\n");
                    foreach (var att in scenario.Attachments)
                    {
                        yml.Append("          - Name: " + att.Name.SanitiseForYml() + "\n");
                        yml.Append("            RelativePath: " + att.RelativePath.SanitiseForYml() + "\n");
                        if (att.MediaType is not null)
                            yml.Append("            MediaType: " + att.MediaType.SanitiseForYml() + "\n");
                    }
                }

                if (diagramLookup != null)
                {
                    var diags = diagramLookup[scenario.Id].ToArray();
                    if (diags.Length > 0)
                    {
                        yml.Append("        Diagrams:\n");
                        foreach (var diag in diags)
                            yml.Append("          - |\n" + string.Join("\n", diag.Split('\n').Select(line => "            " + line)) + "\n");
                    }
                }

                if (logLookup != null)
                {
                    var logs = logLookup[scenario.Id].ToArray();
                    if (logs.Length > 0)
                    {
                        yml.Append("        HttpInteractions:\n");
                        for (var i = 0; i < logs.Length; i++)
                            AppendTestRunYamlLog(yml, logs[i], "          ", durations, StepPathAt(stepPaths, scenario.Id, i));
                    }
                }
            }
        }

        return yml.ToString();
    }

    private static void AppendTestRunYamlStep(StringBuilder yml, ScenarioStep step, string indent)
    {
        yml.Append(indent + "- Keyword: " + (step.Keyword ?? "").SanitiseForYml() + "\n");
        yml.Append(indent + "  Text: " + step.Text.SanitiseForYml() + "\n");
        yml.Append(indent + "  Status: " + (step.Status?.ToString() ?? "") + "\n");
        if (step.Duration != null)
            yml.Append(indent + "  DurationSeconds: " + step.Duration.Value.TotalSeconds.ToString("F3") + "\n");
        if (step.FailureMessage != null)
            yml.Append(indent + "  FailureMessage: " + step.FailureMessage.SanitiseForYml() + "\n");
        if (step.SourceFile != null)
            yml.Append(indent + "  SourceFile: " + step.SourceFile.SanitiseForYml() + "\n");
        if (step.SourceLine != null)
            yml.Append(indent + "  SourceLine: " + step.SourceLine.Value.ToString(CultureInfo.InvariantCulture) + "\n");

        if (step.SubSteps is { Length: > 0 })
        {
            yml.Append(indent + "  SubSteps:\n");
            foreach (var sub in step.SubSteps)
                AppendTestRunYamlStep(yml, sub, indent + "    ");
        }

        if (step.Attachments is { Length: > 0 })
        {
            yml.Append(indent + "  Attachments:\n");
            foreach (var att in step.Attachments)
            {
                yml.Append(indent + "    - Name: " + att.Name.SanitiseForYml() + "\n");
                yml.Append(indent + "      RelativePath: " + att.RelativePath.SanitiseForYml() + "\n");
                if (att.MediaType is not null)
                    yml.Append(indent + "      MediaType: " + att.MediaType.SanitiseForYml() + "\n");
            }
        }
    }

    /// <inheritdoc cref="MapLogJson"/>
    private static void AppendTestRunYamlLog(StringBuilder yml, RequestResponseLog log, string indent, IReadOnlyDictionary<Guid, double>? durations = null, string? stepPath = null)
    {
        yml.Append(indent + "- Type: " + log.Type + "\n");
        yml.Append(indent + "  Method: " + (log.Method.Value?.ToString()?.ToUpperInvariant() ?? "") + "\n");
        yml.Append(indent + "  Uri: " + log.Uri + "\n");
        yml.Append(indent + "  ServiceName: " + log.ServiceName.SanitiseForYml() + "\n");
        yml.Append(indent + "  CallerName: " + log.CallerName.SanitiseForYml() + "\n");
        if (log.Content is not null)
            yml.Append(indent + "  Content: " + log.Content.SanitiseForYml() + "\n");
        if (log.StatusCode is not null)
            yml.Append(indent + "  StatusCode: " + log.StatusCode.Value + "\n");
        yml.Append(indent + "  TraceId: " + log.TraceId + "\n");
        yml.Append(indent + "  RequestResponseId: " + log.RequestResponseId + "\n");
        if (log.Timestamp is not null)
            yml.Append(indent + "  Timestamp: " + log.Timestamp.Value.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") + "\n");
        if (log.MetaType != RequestResponseMetaType.Default)
            yml.Append(indent + "  MetaType: " + log.MetaType + "\n");
        if (log.DependencyCategory is not null)
            yml.Append(indent + "  DependencyCategory: " + log.DependencyCategory.SanitiseForYml() + "\n");
        if (log.CallerDependencyCategory is not null)
            yml.Append(indent + "  CallerDependencyCategory: " + log.CallerDependencyCategory.SanitiseForYml() + "\n");
        if (log.Phase != TestPhase.Unknown)
            yml.Append(indent + "  Phase: " + log.Phase + "\n");
        if (log.IsUserAction)
            yml.Append(indent + "  IsUserAction: true\n");
        if (log.ActivityTraceId is not null)
            yml.Append(indent + "  ActivityTraceId: " + log.ActivityTraceId.SanitiseForYml() + "\n");
        if (log.ActivitySpanId is not null)
            yml.Append(indent + "  ActivitySpanId: " + log.ActivitySpanId.SanitiseForYml() + "\n");
        if (log.CapturedBy is not null)
            yml.Append(indent + "  CapturedBy: " + log.CapturedBy.SanitiseForYml() + "\n");
        if (durations is not null && durations.TryGetValue(log.RequestResponseId, out var ms))
            yml.Append(indent + "  DurationMs: " + ms.ToString("F3", CultureInfo.InvariantCulture) + "\n");
        if (stepPath is not null)
            yml.Append(indent + "  StepPath: " + stepPath + "\n");
        if (log.Headers.Length > 0)
        {
            yml.Append(indent + "  Headers:\n");
            foreach (var h in log.Headers)
                yml.Append(indent + "    - Key: " + h.Key.SanitiseForYml() + "\n" + indent + "      Value: " + (h.Value ?? "").SanitiseForYml() + "\n");
        }
    }

    public static string GenerateSpecificationsData(Feature[] features, string fileName, string title, DataFormat format, bool generateBlankOnFailedTests = false)
    {
        if (generateBlankOnFailedTests && features.Any(x => x.Scenarios.Any(y => y.Result == ExecutionResult.Failed)))
            return WriteFile(string.Empty, fileName);

        return format switch
        {
            DataFormat.Yaml => WriteFile(GenerateSpecificationsYaml(features, title), fileName),
            DataFormat.Json => WriteFile(GenerateSpecificationsJson(features, title), fileName),
            DataFormat.Xml => WriteFile(GenerateSpecificationsXml(features, title), fileName),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
    }

    private static string GenerateSpecificationsYaml(Feature[] features, string title)
    {
        var yml = new StringBuilder();
        yml.Append("Title: " + title + "\n");
        yml.Append("Features:\n");

        foreach (var feature in features.OrderBy(x => x.DisplayName))
        {
            yml.Append("  - Feature: " + feature.DisplayName.SanitiseForYml() + "\n");

            if (feature.Endpoint is not null)
                yml.Append("    Endpoint: " + feature.Endpoint + "\n");

            if (feature.Description is not null)
                yml.Append("    Description: " + feature.Description.SanitiseForYml() + "\n");

            if (feature.Labels is { Length: > 0 })
            {
                yml.Append("    Labels:\n");
                foreach (var label in feature.Labels)
                    yml.Append("      - " + label.SanitiseForYml() + "\n");
            }

            yml.Append("    Scenarios:\n");

            var orderedScenarios = feature.Scenarios.OrderByDescending(x => x.IsHappyPath).ThenBy(x => x.DisplayName);
            foreach (var scenario in orderedScenarios)
            {
                yml.Append("      - Scenario: " + scenario.DisplayName.SanitiseForYml() + "\n");
                yml.Append("        IsHappyPath: " + scenario.IsHappyPath.ToString().ToLower() + "\n");

                if (scenario.Labels is { Length: > 0 })
                {
                    yml.Append("        Labels:\n");
                    foreach (var label in scenario.Labels)
                        yml.Append("          - " + label.SanitiseForYml() + "\n");
                }

                if (scenario.Categories is { Length: > 0 })
                {
                    yml.Append("        Categories:\n");
                    foreach (var cat in scenario.Categories)
                        yml.Append("          - " + cat.SanitiseForYml() + "\n");
                }

                // Emitted as a sibling of Steps, matching the TestRunReport writers: merging the two would
                // lose the b{i}/{i} split the step paths and interaction attribution depend on.
                if (scenario.BackgroundSteps is { Length: > 0 })
                {
                    yml.Append("        BackgroundSteps:\n");
                    foreach (var step in scenario.BackgroundSteps)
                        AppendYamlStep(yml, step, "          ");
                }

                if (scenario.Steps is { Length: > 0 })
                {
                    yml.Append("        Steps:\n");
                    foreach (var step in scenario.Steps)
                        AppendYamlStep(yml, step, "          ");
                }

                yml.Append("\n");
            }
        }

        return yml.ToString();
    }

    private static string GenerateSpecificationsJson(Feature[] features, string title)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
        var data = new
        {
            Title = title,
            Features = features.OrderBy(f => f.DisplayName).Select(f => new
            {
                Name = f.DisplayName,
                f.Endpoint,
                f.Description,
                Labels = f.Labels ?? [],
                Scenarios = f.Scenarios.OrderByDescending(s => s.IsHappyPath).ThenBy(s => s.DisplayName).Select(s => new
                {
                    Name = s.DisplayName,
                    s.IsHappyPath,
                    Labels = s.Labels ?? [],
                    Categories = s.Categories ?? [],
                    BackgroundSteps = (s.BackgroundSteps ?? []).Select(MapSpecStepJson).ToArray(),
                    Steps = (s.Steps ?? []).Select(MapSpecStepJson).ToArray()
                }).ToArray()
            }).ToArray()
        };
        return JsonSerializer.Serialize(data, options);
    }

    private static string MapSpecStepJson(ScenarioStep step)
    {
        var text = step.Keyword is not null ? $"{step.Keyword} {step.Text}" : step.Text;
        // Specifications steps are text-only (matching YAML format)
        // SubSteps are not included as separate entries per the YAML spec format
        return text;
    }

    private static string GenerateSpecificationsXml(Feature[] features, string title)
    {
        var doc = new XDocument(
            new XElement("Specifications",
                new XElement("Title", title),
                new XElement("Features",
                    features.OrderBy(f => f.DisplayName).Select(f =>
                        new XElement("Feature",
                            new XElement("Name", f.DisplayName),
                            f.Endpoint != null ? new XElement("Endpoint", f.Endpoint) : null,
                            f.Description != null ? new XElement("Description", f.Description) : null,
                            (f.Labels is { Length: > 0 }) ? new XElement("Labels", f.Labels.Select(l => new XElement("Label", l))) : null,
                            new XElement("Scenarios",
                                f.Scenarios.OrderByDescending(s => s.IsHappyPath).ThenBy(s => s.DisplayName).Select(s =>
                                    new XElement("Scenario",
                                        new XElement("Name", s.DisplayName),
                                        new XElement("IsHappyPath", s.IsHappyPath.ToString().ToLower()),
                                        (s.Labels is { Length: > 0 }) ? new XElement("Labels", s.Labels.Select(l => new XElement("Label", l))) : null,
                                        (s.Categories is { Length: > 0 }) ? new XElement("Categories", s.Categories.Select(c => new XElement("Category", c))) : null,
                                        (s.BackgroundSteps is { Length: > 0 }) ? new XElement("BackgroundSteps", s.BackgroundSteps.Select(MapSpecStepXml)) : null,
                                        (s.Steps is { Length: > 0 }) ? new XElement("Steps", s.Steps.Select(MapSpecStepXml)) : null
                                    )
                                )
                            )
                        )
                    )
                )
            )
        );
        return doc.ToString();
    }

    private static XElement MapSpecStepXml(ScenarioStep step)
    {
        var text = step.Keyword is not null ? $"{step.Keyword} {step.Text}" : step.Text;
        var element = new XElement("Step", text);
        if (step.SubSteps is { Length: > 0 })
        {
            foreach (var sub in step.SubSteps)
                element.Add(MapSpecStepXml(sub));
        }
        return element;
    }

    /// <summary>
    /// Copies attachment files referenced by steps into the reports directory and rewrites
    /// their <see cref="FileAttachment.RelativePath"/> to point to the local copy.
    /// Attachments whose source file does not exist or whose path is already relative
    /// to an <c>attachments/</c> subfolder are left unchanged.
    /// </summary>
    public static void CopyAttachmentsToReportsFolder(Feature[] features, string reportsDirectory)
    {
        var attachmentsDir = Path.Combine(reportsDirectory, "attachments");
        var copiedFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var feature in features)
        {
            if (feature.Scenarios is null) continue;
            foreach (var scenario in feature.Scenarios)
            {
                if (scenario.Attachments is { Length: > 0 })
                    scenario.Attachments = ProcessAttachments(scenario.Attachments, attachmentsDir, copiedFiles, usedNames);
                if (scenario.BackgroundSteps is { Length: > 0 })
                    ProcessSteps(scenario.BackgroundSteps, attachmentsDir, copiedFiles, usedNames);
                if (scenario.Steps is { Length: > 0 })
                    ProcessSteps(scenario.Steps, attachmentsDir, copiedFiles, usedNames);
            }
        }
    }

    private static void ProcessSteps(ScenarioStep[] steps, string attachmentsDir,
        Dictionary<string, string> copiedFiles, HashSet<string> usedNames)
    {
        for (var i = 0; i < steps.Length; i++)
        {
            var step = steps[i];
            if (step.Attachments is { Length: > 0 })
                step.Attachments = ProcessAttachments(step.Attachments, attachmentsDir, copiedFiles, usedNames);
            if (step.SubSteps is { Length: > 0 })
                ProcessSteps(step.SubSteps, attachmentsDir, copiedFiles, usedNames);
        }
    }

    private static FileAttachment[] ProcessAttachments(FileAttachment[] attachments, string attachmentsDir,
        Dictionary<string, string> copiedFiles, HashSet<string> usedNames)
    {
        var result = new FileAttachment[attachments.Length];
        for (var i = 0; i < attachments.Length; i++)
        {
            var att = attachments[i];
            var sourcePath = att.RelativePath;

            // A link to something that lives elsewhere (a Playwright report, a Grafana trace) is not a
            // file: it must survive untouched, and must never be handed to the path APIs — a URL's
            // "http://host:port/…" is not a legal Windows path and GetFullPath throws on it.
            if (att.IsUrl)
            {
                result[i] = att;
                continue;
            }

            // Skip paths already pointing to attachments/ subfolder
            if (sourcePath.StartsWith("attachments/", StringComparison.OrdinalIgnoreCase) ||
                sourcePath.StartsWith("attachments\\", StringComparison.OrdinalIgnoreCase))
            {
                result[i] = att;
                continue;
            }

            // Resolve to absolute if relative
            string fullPath;
            try
            {
                fullPath = Path.IsPathRooted(sourcePath)
                    ? sourcePath
                    : Path.GetFullPath(sourcePath, AppDomain.CurrentDomain.BaseDirectory);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                ReportDiagnosticsScope.Record(DiagnosticKind.AttachmentFailure, $"Attachment path '{sourcePath}' is not usable", ex);
                result[i] = att;
                continue;
            }

            if (!File.Exists(fullPath))
            {
                result[i] = att;
                continue;
            }

            // Check if we've already copied this exact source file
            var normalizedSource = Path.GetFullPath(fullPath);
            if (copiedFiles.TryGetValue(normalizedSource, out var existingRelative))
            {
                result[i] = att with { RelativePath = existingRelative };
                continue;
            }

            // Deduplicate the destination file name
            var fileName = Path.GetFileName(fullPath);
            var destName = GetUniqueFileName(fileName, usedNames);
            usedNames.Add(destName);

            Directory.CreateDirectory(attachmentsDir);
            var destPath = Path.Combine(attachmentsDir, destName);
            try
            {
                File.Copy(fullPath, destPath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A capturer may still hold the file, or the disk may be full: link to where it is
                // rather than losing the whole report over one screenshot.
                ReportDiagnosticsScope.Record(DiagnosticKind.AttachmentFailure, $"Could not copy attachment {fullPath}", ex);
                result[i] = att;
                continue;
            }

            var relativePath = $"attachments/{destName}";
            copiedFiles[normalizedSource] = relativePath;
            result[i] = att with { RelativePath = relativePath };
        }

        return result;
    }

    private static string GetUniqueFileName(string fileName, HashSet<string> usedNames)
    {
        if (!usedNames.Contains(fileName))
            return fileName;

        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        var counter = 2;
        string candidate;
        do
        {
            candidate = $"{nameWithoutExt}_{counter}{ext}";
            counter++;
        } while (usedNames.Contains(candidate));

        return candidate;
    }

    private static string WriteFile(string text, string fileName)
    {
        var directory = CurrentReportsDirectory;
        Directory.CreateDirectory(directory);
        var filePath = Path.Combine(directory, fileName);
        try
        {
            File.WriteAllText(filePath, text);
        }
        catch (IOException)
        {
            var fallback = Path.Combine(directory,
                Path.GetFileNameWithoutExtension(fileName) + "2" + Path.GetExtension(fileName));
            File.WriteAllText(fallback, text);
            return fallback;
        }
        return filePath;
    }

    internal static HashSet<string> ExtractDependencies(string codeBehind, DiagramFormat format)
    {
        var deps = new HashSet<string>();
        if (string.IsNullOrEmpty(codeBehind)) return deps;

        foreach (var line in codeBehind.Split('\n'))
        {
            var trimmed = line.Trim();

            // Match all PlantUML participant types: actor, boundary, control, entity, database, collections, queue, participant
            var match = System.Text.RegularExpressions.Regex.Match(trimmed,
                @"^(?:actor|boundary|control|entity|database|collections|queue|participant)\s+""([^""]+)""\s+as\s+");
            if (match.Success)
                deps.Add(match.Groups[1].Value);
        }

        return deps;
    }

    private static readonly System.Text.RegularExpressions.Regex ParticipantRegex = new(
        @"^(?:actor|boundary|control|entity|database|collections|queue|participant)\s+""([^""]+)""",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex UrlRegex = new(
        @":\s*(?:GET|POST|PUT|DELETE|PATCH|HEAD|OPTIONS|CONNECT|TRACE):\s*(\S+)",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    internal static HashSet<string> ExtractDiagramSearchTerms(string codeBehind)
    {
        var terms = new HashSet<string>();
        if (string.IsNullOrEmpty(codeBehind)) return terms;

        foreach (var line in codeBehind.Split('\n'))
        {
            var trimmed = line.Trim();

            var participantMatch = ParticipantRegex.Match(trimmed);
            if (participantMatch.Success)
                terms.Add(participantMatch.Groups[1].Value);

            var urlMatch = UrlRegex.Match(trimmed);
            if (urlMatch.Success)
                terms.Add(urlMatch.Groups[1].Value);
        }

        return terms;
    }

    private static void CollectStepText(ScenarioStep[]? steps, List<string> parts)
    {
        if (steps is null) return;
        foreach (var step in steps)
        {
            parts.Add(step.Text);
            CollectStepText(step.SubSteps, parts);
        }
    }

    private static string GetDataFormatExtension(DataFormat format) => format switch
    {
        DataFormat.Json => "json",
        DataFormat.Xml => "xml",
        DataFormat.Yaml => "yml",
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    private static string GetSchemaExtension(DataFormat format) => format switch
    {
        DataFormat.Json => "json",
        DataFormat.Xml => "xsd",
        DataFormat.Yaml => "json", // YAML uses JSON Schema
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    public static string GenerateTestRunReportSchema(string fileName, DataFormat format)
    {
        return format switch
        {
            DataFormat.Json => WriteFile(GenerateTestRunReportJsonSchema(), fileName),
            DataFormat.Xml => WriteFile(GenerateTestRunReportXmlSchema(), fileName),
            DataFormat.Yaml => WriteFile(GenerateTestRunReportJsonSchema(), fileName), // YAML uses JSON Schema
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
    }

    private static string GenerateTestRunReportJsonSchema()
    {
        var resultEnumValues = Enum.GetNames(typeof(ExecutionResult));
        var statusEnumValues = resultEnumValues;

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

        var schema = new Dictionary<string, object?>
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["title"] = "TestRunReport",
            ["description"] = "Schema for Kronikol test run report data",
            ["type"] = "object",
            ["required"] = new[] { "startTime", "endTime", "features" },
            ["properties"] = new Dictionary<string, object?>
            {
                ["kronikolVersion"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Version of Kronikol that generated this report" },
                ["startTime"] = new Dictionary<string, object?> { ["type"] = "string", ["format"] = "date-time", ["description"] = "UTC start time of the test run" },
                ["endTime"] = new Dictionary<string, object?> { ["type"] = "string", ["format"] = "date-time", ["description"] = "UTC end time of the test run" },
                ["diagnostics"] = new Dictionary<string, object?>
                {
                    ["type"] = "array",
                    ["description"] = "Everything worth knowing about how this report was produced: capture health handed in by the host (CaptureDegraded), skipped malformed lines, diagrams that could not be rendered, labels that still do not read as sentences. Empty is the happy path.",
                    ["items"] = new Dictionary<string, object?> { ["$ref"] = "#/$defs/diagnostic" }
                },
                ["features"] = new Dictionary<string, object?>
                {
                    ["type"] = "array",
                    ["items"] = new Dictionary<string, object?>
                    {
                        ["type"] = "object",
                        ["required"] = new[] { "name", "labels", "scenarios" },
                        ["properties"] = new Dictionary<string, object?>
                        {
                            ["name"] = new Dictionary<string, object?> { ["type"] = "string" },
                            ["endpoint"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true },
                            ["description"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true },
                            ["labels"] = new Dictionary<string, object?> { ["type"] = "array", ["items"] = new Dictionary<string, object?> { ["type"] = "string" } },
                            ["scenarios"] = new Dictionary<string, object?>
                            {
                                ["type"] = "array",
                                ["items"] = new Dictionary<string, object?>
                                {
                                    ["type"] = "object",
                                    ["required"] = new[] { "id", "stableId", "name", "result", "durationSeconds", "isHappyPath", "labels", "categories", "steps" },
                                    ["properties"] = new Dictionary<string, object?>
                                    {
                                        ["id"] = new Dictionary<string, object?> { ["type"] = "string" },
                                        ["stableId"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Deterministic cross-run identifier derived from feature name + scenario display name (+ outline ID and ordered example values for parameterized scenarios). Use this for matching the same test across runs." },
                                        ["name"] = new Dictionary<string, object?> { ["type"] = "string" },
                                        ["description"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true, ["description"] = "The scenario's own free-text description (the prose under Scenario:)" },
                                        ["result"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = resultEnumValues },
                                        ["durationSeconds"] = new Dictionary<string, object?> { ["type"] = "number" },
                                        ["isHappyPath"] = new Dictionary<string, object?> { ["type"] = "boolean" },
                                        ["errorMessage"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true },
                                        ["errorStackTrace"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true },
                                        ["labels"] = new Dictionary<string, object?> { ["type"] = "array", ["items"] = new Dictionary<string, object?> { ["type"] = "string" } },
                                        ["categories"] = new Dictionary<string, object?> { ["type"] = "array", ["items"] = new Dictionary<string, object?> { ["type"] = "string" } },
                                        ["rule"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true, ["description"] = "Gherkin Rule grouping this scenario belongs to" },
                                        ["outlineId"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true, ["description"] = "Original scenario outline name for parameterized scenarios" },
                                        ["examplesBlockName"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true, ["description"] = "Name of the Examples: block this outline row came from" },
                                        ["examplesBlockDescription"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true, ["description"] = "Free-text description under the Examples: header" },
                                        ["examplesBlockIndex"] = new Dictionary<string, object?> { ["type"] = "integer", ["nullable"] = true, ["description"] = "0-based position of the Examples: block within the outline" },
                                        ["exampleValues"] = new Dictionary<string, object?> { ["type"] = "object", ["nullable"] = true, ["description"] = "Example parameter values for parameterized scenarios", ["additionalProperties"] = new Dictionary<string, object?> { ["type"] = "string" } },
                                        ["backgroundSteps"] = new Dictionary<string, object?>
                                        {
                                            ["type"] = "array",
                                            ["items"] = new Dictionary<string, object?> { ["$ref"] = "#/$defs/step" }
                                        },
                                        ["steps"] = new Dictionary<string, object?>
                                        {
                                            ["type"] = "array",
                                            ["items"] = new Dictionary<string, object?> { ["$ref"] = "#/$defs/step" }
                                        },
                                        ["attachments"] = new Dictionary<string, object?>
                                        {
                                            ["type"] = "array",
                                            ["description"] = "Scenario-level file attachments (added when no step was active)",
                                            ["items"] = new Dictionary<string, object?>
                                            {
                                                ["type"] = "object",
                                                ["properties"] = new Dictionary<string, object?>
                                                {
                                                    ["name"] = new Dictionary<string, object?> { ["type"] = "string" },
                                                    ["relativePath"] = new Dictionary<string, object?> { ["type"] = "string" },
                                                    ["mediaType"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true, ["description"] = "IANA media type; image/* renders inline, anything else as a link" }
                                                }
                                            }
                                        },
                                        ["diagrams"] = new Dictionary<string, object?>
                                        {
                                            ["type"] = "array",
                                            ["items"] = new Dictionary<string, object?> { ["type"] = "string" }
                                        },
                                        ["httpInteractions"] = new Dictionary<string, object?>
                                        {
                                            ["type"] = "array",
                                            ["items"] = new Dictionary<string, object?> { ["$ref"] = "#/$defs/httpInteraction" }
                                        },
                                        ["annotations"] = new Dictionary<string, object?>
                                        {
                                            ["type"] = "array",
                                            ["description"] = "Diagram markers that carry information found nowhere else: which row of a tabular input was in flight, and fragments the test author injected. Step and assertion markers are excluded — those are already structured in steps.",
                                            ["items"] = new Dictionary<string, object?>
                                            {
                                                ["type"] = "object",
                                                ["properties"] = new Dictionary<string, object?>
                                                {
                                                    ["index"] = new Dictionary<string, object?> { ["type"] = "integer", ["description"] = "Position in httpInteractions the marker sat before" },
                                                    ["kind"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = new[] { "Row", "Custom" } },
                                                    ["text"] = new Dictionary<string, object?> { ["type"] = "string" }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            },
            ["$defs"] = new Dictionary<string, object?>
            {
                ["diagnostic"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["required"] = new[] { "kind", "message" },
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["kind"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = Enum.GetNames(typeof(DiagnosticKind)), ["description"] = "What the entry is about (DiagnosticKind)" },
                        ["message"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "One-line description, safe to print" },
                        ["scenarioId"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true, ["description"] = "The scenario the entry belongs to, when it is scenario-specific" }
                    }
                },
                ["step"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["keyword"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true },
                        ["text"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["status"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = statusEnumValues, ["nullable"] = true },
                        ["durationSeconds"] = new Dictionary<string, object?> { ["type"] = "number", ["nullable"] = true },
                        ["subSteps"] = new Dictionary<string, object?>
                        {
                            ["type"] = "array",
                            ["items"] = new Dictionary<string, object?> { ["$ref"] = "#/$defs/step" }
                        },
                        ["attachments"] = new Dictionary<string, object?>
                        {
                            ["type"] = "array",
                            ["items"] = new Dictionary<string, object?>
                            {
                                ["type"] = "object",
                                ["properties"] = new Dictionary<string, object?>
                                {
                                    ["name"] = new Dictionary<string, object?> { ["type"] = "string" },
                                    ["relativePath"] = new Dictionary<string, object?> { ["type"] = "string" },
                                    ["mediaType"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true, ["description"] = "IANA media type; image/* renders inline, anything else as a link" }
                                }
                            }
                        },
                        ["bypassReason"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true, ["description"] = "Why the step was skipped, when its status is Bypassed" },
                        ["docString"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true, ["description"] = "The step's Gherkin doc-string body" },
                        ["docStringMediaType"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true, ["description"] = "Media type declared on the doc string, when the source gave one" },
                        ["comments"] = new Dictionary<string, object?> { ["type"] = "array", ["items"] = new Dictionary<string, object?> { ["type"] = "string" }, ["description"] = "Comment lines attached to the step in the source" },
                        ["parameters"] = new Dictionary<string, object?> { ["type"] = "array", ["description"] = "The step's inputs: inline values, data tables (columns and rows) and tree values. Present unless TestRunReportFullStepDetail is turned off.", ["items"] = new Dictionary<string, object?> { ["type"] = "object" } },
                        ["textSegments"] = new Dictionary<string, object?> { ["type"] = "array", ["nullable"] = true, ["description"] = "The step text split into literal prose and inline parameter values, for highlighted rendering", ["items"] = new Dictionary<string, object?> { ["type"] = "object" } },
                        ["failureMessage"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true, ["description"] = "Why this step or assertion failed — the assertion message, or the exception that ended the step" },
                        ["sourceFile"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true, ["description"] = "File the assertion was written in (name only), when the caller supplied it" },
                        ["sourceLine"] = new Dictionary<string, object?> { ["type"] = "integer", ["nullable"] = true, ["description"] = "Line in sourceFile" }
                    }
                },
                ["httpInteraction"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["type"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = new[] { "Request", "Response" } },
                        ["method"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true },
                        ["uri"] = new Dictionary<string, object?> { ["type"] = "string", ["format"] = "uri" },
                        ["serviceName"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["callerName"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["content"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true },
                        ["headers"] = new Dictionary<string, object?>
                        {
                            ["type"] = "array",
                            ["items"] = new Dictionary<string, object?>
                            {
                                ["type"] = "object",
                                ["properties"] = new Dictionary<string, object?>
                                {
                                    ["key"] = new Dictionary<string, object?> { ["type"] = "string" },
                                    ["value"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true }
                                }
                            }
                        },
                        ["statusCode"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true },
                        ["traceId"] = new Dictionary<string, object?> { ["type"] = "string", ["format"] = "uuid" },
                        ["requestResponseId"] = new Dictionary<string, object?> { ["type"] = "string", ["format"] = "uuid" },
                        ["timestamp"] = new Dictionary<string, object?> { ["type"] = "string", ["format"] = "date-time", ["nullable"] = true },
                        ["metaType"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = Enum.GetNames(typeof(RequestResponseMetaType)), ["description"] = "Default for a request/response exchange, Event for a fire-and-forget publish" },
                        ["dependencyCategory"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true, ["description"] = "What kind of thing the callee is (database, cache, queue, ...) — drives participant shape and arrow colour in the diagram" },
                        ["callerDependencyCategory"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true, ["description"] = "The same, for the caller" },
                        ["phase"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = Enum.GetNames(typeof(TestPhase)), ["description"] = "Whether the call happened during Setup or the Action under test; Unknown when phase detection is off" },
                        ["isUserAction"] = new Dictionary<string, object?> { ["type"] = "boolean", ["description"] = "A UI interaction (click, navigate) rather than a dependency call" },
                        ["activityTraceId"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true, ["description"] = "W3C trace id — the bridge to OpenTelemetry traces and application logs. Unlike traceId, which is Kronikol's own identifier for the request/response pair." },
                        ["activitySpanId"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true, ["description"] = "W3C span id" },
                        ["capturedBy"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true, ["description"] = "Which capture path produced this entry: wire (proxy/TCP tap) or span (OpenTelemetry receiver)" },
                        ["durationMs"] = new Dictionary<string, object?> { ["type"] = "number", ["nullable"] = true, ["description"] = "Wall-clock milliseconds between the request and its response, derived from the two timestamps. Repeated on both halves of the pair; null when the request went unanswered or timestamps are absent." },
                        ["stepPath"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true, ["description"] = "Which step this call happened under: an index into the scenario's steps, prefixed b for a background step (b0, 0, 1, ...). Null before the first step, and whenever attribution could not be trusted — see the StepAttributionMismatch diagnostic." }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(schema, options);
    }

    private static string GenerateTestRunReportXmlSchema()
    {
        var xs = XNamespace.Get("http://www.w3.org/2001/XMLSchema");

        var resultEnumValues = Enum.GetNames(typeof(ExecutionResult));

        var executionResultType = new XElement(xs + "simpleType",
            new XAttribute("name", "ExecutionResult"),
            new XElement(xs + "restriction",
                new XAttribute("base", "xs:string"),
                resultEnumValues.Select(v => new XElement(xs + "enumeration", new XAttribute("value", v)))
            ));

        var headerType = new XElement(xs + "complexType",
            new XAttribute("name", "HeaderType"),
            new XElement(xs + "sequence",
                new XElement(xs + "element", new XAttribute("name", "Key"), new XAttribute("type", "xs:string")),
                new XElement(xs + "element", new XAttribute("name", "Value"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0"))
            ));

        var stepType = new XElement(xs + "complexType",
            new XAttribute("name", "StepType"),
            new XElement(xs + "sequence",
                new XElement(xs + "element", new XAttribute("name", "Keyword"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "Text"), new XAttribute("type", "xs:string")),
                new XElement(xs + "element", new XAttribute("name", "Status"), new XAttribute("type", "ExecutionResult"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "DurationSeconds"), new XAttribute("type", "xs:decimal"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "FailureMessage"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "SourceFile"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "SourceLine"), new XAttribute("type", "xs:int"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "SubSteps"), new XAttribute("minOccurs", "0"),
                    new XElement(xs + "complexType",
                        new XElement(xs + "sequence",
                            new XElement(xs + "element", new XAttribute("name", "Step"), new XAttribute("type", "StepType"), new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"))
                        )
                    )
                ),
                new XElement(xs + "element", new XAttribute("name", "Attachments"), new XAttribute("minOccurs", "0"),
                    new XElement(xs + "complexType",
                        new XElement(xs + "sequence",
                            new XElement(xs + "element", new XAttribute("name", "Attachment"), new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"),
                                new XElement(xs + "complexType",
                                    new XElement(xs + "sequence",
                                        new XElement(xs + "element", new XAttribute("name", "Name"), new XAttribute("type", "xs:string")),
                                        new XElement(xs + "element", new XAttribute("name", "RelativePath"), new XAttribute("type", "xs:string")),
                                        new XElement(xs + "element", new XAttribute("name", "MediaType"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0"))
                                    )
                                )
                            )
                        )
                    )
                )
            ));

        var httpInteractionType = new XElement(xs + "complexType",
            new XAttribute("name", "HttpInteractionType"),
            new XElement(xs + "sequence",
                new XElement(xs + "element", new XAttribute("name", "Type"), new XAttribute("type", "xs:string")),
                new XElement(xs + "element", new XAttribute("name", "Method"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "Uri"), new XAttribute("type", "xs:string")),
                new XElement(xs + "element", new XAttribute("name", "ServiceName"), new XAttribute("type", "xs:string")),
                new XElement(xs + "element", new XAttribute("name", "CallerName"), new XAttribute("type", "xs:string")),
                new XElement(xs + "element", new XAttribute("name", "Content"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "Headers"), new XAttribute("minOccurs", "0"),
                    new XElement(xs + "complexType",
                        new XElement(xs + "sequence",
                            new XElement(xs + "element", new XAttribute("name", "Header"), new XAttribute("type", "HeaderType"), new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"))
                        )
                    )
                ),
                new XElement(xs + "element", new XAttribute("name", "StatusCode"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "TraceId"), new XAttribute("type", "xs:string")),
                new XElement(xs + "element", new XAttribute("name", "RequestResponseId"), new XAttribute("type", "xs:string")),
                new XElement(xs + "element", new XAttribute("name", "Timestamp"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "MetaType"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "DependencyCategory"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "CallerDependencyCategory"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "Phase"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "IsUserAction"), new XAttribute("type", "xs:boolean"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "ActivityTraceId"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "ActivitySpanId"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "CapturedBy"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "DurationMs"), new XAttribute("type", "xs:decimal"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "StepPath"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0"))
            ));

        var scenarioType = new XElement(xs + "complexType",
            new XAttribute("name", "ScenarioType"),
            new XElement(xs + "sequence",
                new XElement(xs + "element", new XAttribute("name", "Id"), new XAttribute("type", "xs:string")),
                new XElement(xs + "element", new XAttribute("name", "StableId"), new XAttribute("type", "xs:string")),
                new XElement(xs + "element", new XAttribute("name", "Name"), new XAttribute("type", "xs:string")),
                new XElement(xs + "element", new XAttribute("name", "Description"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "Result"), new XAttribute("type", "ExecutionResult")),
                new XElement(xs + "element", new XAttribute("name", "DurationSeconds"), new XAttribute("type", "xs:decimal")),
                new XElement(xs + "element", new XAttribute("name", "IsHappyPath"), new XAttribute("type", "xs:boolean")),
                new XElement(xs + "element", new XAttribute("name", "ErrorMessage"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "ErrorStackTrace"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "Labels"), new XAttribute("minOccurs", "0"),
                    new XElement(xs + "complexType",
                        new XElement(xs + "sequence",
                            new XElement(xs + "element", new XAttribute("name", "Label"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"))
                        )
                    )
                ),
                new XElement(xs + "element", new XAttribute("name", "Categories"), new XAttribute("minOccurs", "0"),
                    new XElement(xs + "complexType",
                        new XElement(xs + "sequence",
                            new XElement(xs + "element", new XAttribute("name", "Category"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"))
                        )
                    )
                ),
                new XElement(xs + "element", new XAttribute("name", "Rule"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "BackgroundSteps"), new XAttribute("minOccurs", "0"),
                    new XElement(xs + "complexType",
                        new XElement(xs + "sequence",
                            new XElement(xs + "element", new XAttribute("name", "Step"), new XAttribute("type", "StepType"), new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"))
                        )
                    )
                ),
                new XElement(xs + "element", new XAttribute("name", "Steps"), new XAttribute("minOccurs", "0"),
                    new XElement(xs + "complexType",
                        new XElement(xs + "sequence",
                            new XElement(xs + "element", new XAttribute("name", "Step"), new XAttribute("type", "StepType"), new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"))
                        )
                    )
                ),
                new XElement(xs + "element", new XAttribute("name", "Attachments"), new XAttribute("minOccurs", "0"),
                    new XElement(xs + "complexType",
                        new XElement(xs + "sequence",
                            new XElement(xs + "element", new XAttribute("name", "Attachment"), new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"),
                                new XElement(xs + "complexType",
                                    new XElement(xs + "sequence",
                                        new XElement(xs + "element", new XAttribute("name", "Name"), new XAttribute("type", "xs:string")),
                                        new XElement(xs + "element", new XAttribute("name", "RelativePath"), new XAttribute("type", "xs:string")),
                                        new XElement(xs + "element", new XAttribute("name", "MediaType"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0"))
                                    )
                                )
                            )
                        )
                    )
                ),
                new XElement(xs + "element", new XAttribute("name", "Diagrams"), new XAttribute("minOccurs", "0"),
                    new XElement(xs + "complexType",
                        new XElement(xs + "sequence",
                            new XElement(xs + "element", new XAttribute("name", "Diagram"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"))
                        )
                    )
                ),
                new XElement(xs + "element", new XAttribute("name", "HttpInteractions"), new XAttribute("minOccurs", "0"),
                    new XElement(xs + "complexType",
                        new XElement(xs + "sequence",
                            new XElement(xs + "element", new XAttribute("name", "HttpInteraction"), new XAttribute("type", "HttpInteractionType"), new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"))
                        )
                    )
                )
            ));

        var featureType = new XElement(xs + "complexType",
            new XAttribute("name", "FeatureType"),
            new XElement(xs + "sequence",
                new XElement(xs + "element", new XAttribute("name", "Name"), new XAttribute("type", "xs:string")),
                new XElement(xs + "element", new XAttribute("name", "Endpoint"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "Description"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "Labels"), new XAttribute("minOccurs", "0"),
                    new XElement(xs + "complexType",
                        new XElement(xs + "sequence",
                            new XElement(xs + "element", new XAttribute("name", "Label"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"))
                        )
                    )
                ),
                new XElement(xs + "element", new XAttribute("name", "Scenarios"),
                    new XElement(xs + "complexType",
                        new XElement(xs + "sequence",
                            new XElement(xs + "element", new XAttribute("name", "Scenario"), new XAttribute("type", "ScenarioType"), new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"))
                        )
                    )
                )
            ));

        var doc = new XDocument(
            new XElement(xs + "schema",
                new XAttribute(XNamespace.Xmlns + "xs", "http://www.w3.org/2001/XMLSchema"),
                executionResultType,
                headerType,
                stepType,
                httpInteractionType,
                scenarioType,
                featureType,
                new XElement(xs + "element",
                    new XAttribute("name", "TestRunReport"),
                    new XElement(xs + "complexType",
                        new XElement(xs + "sequence",
                            new XElement(xs + "element", new XAttribute("name", "KronikolVersion"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                            new XElement(xs + "element", new XAttribute("name", "StartTime"), new XAttribute("type", "xs:string")),
                            new XElement(xs + "element", new XAttribute("name", "EndTime"), new XAttribute("type", "xs:string")),
                            new XElement(xs + "element", new XAttribute("name", "Features"),
                                new XElement(xs + "complexType",
                                    new XElement(xs + "sequence",
                                        new XElement(xs + "element", new XAttribute("name", "Feature"), new XAttribute("type", "FeatureType"), new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"))
                                    )
                                )
                            )
                        )
                    )
                )
            ));

        return doc.Declaration != null ? doc.Declaration + "\n" + doc : doc.ToString();
    }
}