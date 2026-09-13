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

    /// <summary>
    /// The version of the <i>shape</i> of the data files — <c>TestRunReport.json</c>, <c>.xml</c> and
    /// <c>.yml</c> — as distinct from <see cref="KronikolVersion"/>, which is the build that wrote them.
    ///
    /// <para>It exists so a reader can refuse a file it does not understand instead of half-parsing it.
    /// <c>kronikolVersion</c> cannot do that job: it moves on every release whether or not the shape did,
    /// and every working-tree build stamps the same string. Additive changes — a new key — do not move
    /// this number; a reader that does not know the key ignores it. It moves when a key changes meaning
    /// or type, which is exactly when a reader must stop rather than continue.</para>
    /// </summary>
    public const int ReportFormatVersion = 1;

    /// <summary>
    /// The framework's classification of a failure, on its own line and only when there is one. Until
    /// 3.1.0 the label <c>Failure Cause:</c> sat in front of the error <em>message</em>, which was
    /// coincidentally readable on xUnit v3 — whose adapter spliced the cause onto the message — and a
    /// mislabel on the other seven adapters, where the text after it is the assertion, not the cause.
    /// </summary>
    /// <remarks>
    /// Ends with its own newline and contributes NOTHING when there is no cause, so that both failure
    /// blocks — the raw-string one, where the interpolation sits on a line of its own and so supplies a
    /// line break whatever this returns, and the appended one, which supplies its own — produce the same
    /// spacing. They briefly did not, and one blank line of drift between two renderings of the same
    /// failure is exactly the sort of difference a byte-for-byte golden in the Java port fails on.
    /// </remarks>
    private static string FailureCauseLine(string? failureCause) =>
        failureCause is null ? "" : $"Cause: {System.Net.WebUtility.HtmlEncode(failureCause)}\n";

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

    /// <summary>
    /// Scopes the directory every <c>WriteFile</c> on this async flow writes into, for callers that reach
    /// <see cref="GenerateHtmlReport"/> without going through
    /// <see cref="CreateStandardReportsWithDiagrams"/> — which is how <c>kronikol merge</c> gets here.
    ///
    /// <para>Without it those callers fall back to <c>&lt;BaseDirectory&gt;/Reports</c>, so a merge asked
    /// to write <c>/some/where/Combined.html</c> put the file beside the running binary instead. The path
    /// the caller passed was reduced to its file name and the directory silently discarded.</para>
    /// </summary>
    internal static IDisposable ScopeReportsDirectory(string directory)
    {
        var previous = ActiveReportsDirectory.Value;
        ActiveReportsDirectory.Value = directory;
        return new DirectoryScope(previous);
    }

    private sealed class DirectoryScope(string? previous) : IDisposable
    {
        public void Dispose() => ActiveReportsDirectory.Value = previous;
    }

    /// <summary>
    /// Writes the standard set of reports for a finished run.
    /// </summary>
    /// <remarks>
    /// <c>environment</c> is what the run executed on. Null means this machine, which is right for a
    /// test run generating its own report. <see cref="RunEnvironment.Unrecorded"/> leaves the key out of
    /// the data file, which is what <c>kronikol ingest</c> needs when the source does not say what the
    /// run ran on - the tool's own operating system and .NET version describe the machine doing the
    /// reading, not the run.
    /// </remarks>
    public static void CreateStandardReportsWithDiagrams(Feature[] features, DateTime startRunTime, DateTime endRunTime, ReportConfigurationOptions options, RunEnvironment? environment = null)
    {
        var previous = ActiveReportsDirectory.Value;
        ActiveReportsDirectory.Value = ResolveReportsDirectory(options);
        // A host that already scoped a collector (kronikol ingest, a dashboard) keeps it — its entries and
        // ours land in the same report. An adapter-driven run has none, and without one every
        // ReportDiagnosticsScope.Record on this path — render failures, attachment failures, step
        // attribution mismatches, output failures — was a silent no-op and the JSON's diagnostics array was
        // always empty. The AsyncLocal keeps concurrent generations apart.
        var ownScope = ReportDiagnosticsScope.Current is null ? ReportDiagnosticsScope.Begin(new ReportDiagnosticsCollector()) : null;
        try
        {
            CreateStandardReportsWithDiagramsCore(features, startRunTime, endRunTime, options, environment);
        }
        finally
        {
            ownScope?.Dispose();
            ActiveReportsDirectory.Value = previous;
        }
    }

    private static void CreateStandardReportsWithDiagramsCore(Feature[] features, DateTime startRunTime, DateTime endRunTime, ReportConfigurationOptions options, RunEnvironment? environment)
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

        // Resolved once per run and handed to every writer, so the stableId in the HTML, the data file,
        // the digest, the CTRF document and the run-end pointer is the same string. A writer that
        // resolved its own would be the comparer bug again, in a different field.
        var suite = RunSuite.Resolve(options);

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
                options.GenerateSpecificationsMarkdown = false;
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
            DiagramNoteWrapWidth = options.DiagramNoteWrapWidth,
            CollapseConsecutiveIdenticalCalls = options.CollapseConsecutiveIdenticalCalls,
            CollapseThreshold = options.CollapseThreshold,
            MaxArrowsPerDiagram = options.MaxArrowsPerDiagram
        };
        var diagrams = DefaultDiagramsFetcher.GetDiagramsFetcher(fetcherOptions)();

        var internalFlowDataScript = "";
        var internalFlowDataScriptSpecifications = "";
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

            string BuildFlowScript(InternalFlowTab startTab) =>
                DiagramContextMenu.GetInternalFlowConfigScript(options.InternalFlowHasDataBehavior)
                + InternalFlowHtmlGenerator.GenerateSegmentDataScript(
                    perBoundarySegments,
                    options.InternalFlowDiagramStyle,
                    options.InternalFlowShowFlameChart,
                    options.InternalFlowFlameChartPosition,
                    options.InternalFlowNoDataBehavior,
                    options.InternalFlowSpanGranularity,
                    options.InternalFlowActivitySources,
                    startTab);

            // The popup data script is shared by both HTML reports; only a Specifications
            // override that actually changes the internal-flow tab pays for a second build.
            var testRunTab = ReportToggleDefaultsResolver.Resolve(options, specifications: false).InternalFlowTab;
            var specificationsTab = ReportToggleDefaultsResolver.Resolve(options, specifications: true).InternalFlowTab;
            internalFlowDataScript = BuildFlowScript(testRunTab);
            internalFlowDataScriptSpecifications = specificationsTab == testRunTab
                ? internalFlowDataScript
                : BuildFlowScript(specificationsTab);

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
            Add($"{options.HtmlSpecificationsFileName}.html", () => GenerateHtmlReport(diagrams, features, startRunTime, endRunTime, options.HtmlSpecificationsCustomStyleSheet, $"{options.HtmlSpecificationsFileName}.html", options.SpecificationsTitle, false, generateBlankOnFailedTests: true, lazyLoadImages: options.LazyLoadDiagramImages, diagramFormat: options.DiagramFormat, plantUmlRendering: options.PlantUmlRendering, inlineSvgRendering: options.InlineSvgRendering, internalFlowTracking: options.InternalFlowTracking, internalFlowDataScript: internalFlowDataScriptSpecifications, wholeTestSegments: wholeTestSegments, trackedLogs: trackedLogs, wholeTestVisualization: options.WholeTestFlowVisualization, showStepNumbers: options.SpecificationsShowStepNumbers, customCss: options.CustomCss, customFaviconBase64: options.CustomFaviconBase64, customLogoHtml: options.CustomLogoHtml, groupParameterizedTests: options.GroupParameterizedTests, maxParameterColumns: options.MaxParameterColumns, titleizeParameterNames: options.TitleizeParameterNames, showNoInteractionsMarker: options.ShowNoInteractionsMarker, browserRenderWorkers: options.BrowserRenderWorkers, browserRenderCacheMegabytes: options.BrowserRenderCacheMegabytes, browserFragmentMaxHeight: options.BrowserFragmentMaxHeight, separateBackgroundSteps: options.SeparateBackgroundSteps, collapseRepeatedStepKeywords: options.CollapseRepeatedStepKeywords, notePayloadFormat: options.NotePayloadFormat, fullSearchIndex: options.FullSearchIndex, searchIndexCache: searchIndexCache, toggleDefaults: ReportToggleDefaultsResolver.Resolve(options, specifications: true), suite: suite));
        }

        if (options.GenerateTestRunReport)
        {
            Add($"{options.HtmlTestRunReportFileName}.html", () => GenerateHtmlReport(diagrams, features, startRunTime, endRunTime, null, $"{options.HtmlTestRunReportFileName}.html", GetTestRunReportTitle(options), true, lazyLoadImages: options.LazyLoadDiagramImages, diagramFormat: options.DiagramFormat, plantUmlRendering: options.PlantUmlRendering, inlineSvgRendering: options.InlineSvgRendering, internalFlowTracking: options.InternalFlowTracking, internalFlowDataScript: internalFlowDataScript, wholeTestSegments: wholeTestSegments, trackedLogs: trackedLogs, wholeTestVisualization: options.WholeTestFlowVisualization, ciMetadata: ciMetadata, showStepNumbers: options.TestRunReportShowStepNumbers, customCss: options.CustomCss, customFaviconBase64: options.CustomFaviconBase64, customLogoHtml: options.CustomLogoHtml, groupParameterizedTests: options.GroupParameterizedTests, maxParameterColumns: options.MaxParameterColumns, titleizeParameterNames: options.TitleizeParameterNames, componentDiagramPlantUml: ShouldEmbedComponentDiagram(options) ? componentDiagramPlantUml : null, showNoInteractionsMarker: options.ShowNoInteractionsMarker, diagnostics: reportDiagnostics, browserRenderWorkers: options.BrowserRenderWorkers, browserRenderCacheMegabytes: options.BrowserRenderCacheMegabytes, browserFragmentMaxHeight: options.BrowserFragmentMaxHeight, separateBackgroundSteps: options.SeparateBackgroundSteps, collapseRepeatedStepKeywords: options.CollapseRepeatedStepKeywords, notePayloadFormat: options.NotePayloadFormat, fullSearchIndex: options.FullSearchIndex, searchIndexCache: searchIndexCache, toggleDefaults: ReportToggleDefaultsResolver.Resolve(options, specifications: false), suite: suite));
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
                    BuildMergeableReportJson(features, startRunTime, endRunTime, diagrams, trackedLogs, perBoundarySegments, wholeTestSegments, ciMetadata, options, reportDiagnostics, suite, environment),
                    $"{options.HtmlTestRunReportFileName}.{testRunDataExtension}"));
            }
            else
            {
                Add($"{options.HtmlTestRunReportFileName}.{testRunDataExtension}", () => GenerateTestRunReportData(features, startRunTime, endRunTime, $"{options.HtmlTestRunReportFileName}.{testRunDataExtension}", options.TestRunReportDataFormat, diagrams, dataLogs, reportDiagnostics, options.TestRunReportFullStepDetail, ciMetadata, suite, environment));
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

        // Rung zero of the debugging ladder, and the bait that makes the instruction file below load.
        //
        // Two actions, not one. Generation is shared — same data, same code, and a Lazy so the work still
        // happens once — but the WRITES fail independently: a file held open by a reader, a path already
        // taken by a directory, a disk that filled between them. One action meant either failure cost both
        // files, and the isolation rule this list exists for stopped at the pair. What is left behind when
        // a write fails is not nothing, it is the PREVIOUS run's file: stale, plausible, and describing
        // different failures, in the directory an agent has just been told to read first.
        if (options.GenerateFailuresDigest)
        {
            var digest = new Lazy<FailuresDigest>(() =>
                FailuresDigestGenerator.Generate(features, dataLogs,
                    // Null when no report is being written, so the digest links to nothing rather than to
                    // a path that will not exist. It is a decision, not a race: the HTML is written by a
                    // sibling action in this same parallel list, so File.Exists here would answer whatever
                    // the scheduler happened to have done.
                    options.GenerateTestRunReport ? options.HtmlTestRunReportFileName : null,
                    KronikolVersion, reportDiagnostics, suite));

            Add(FailuresDigestFileName, () => WriteFile(digest.Value.Markdown, FailuresDigestFileName));
            Add(FailuresDigestJsonlFileName, () => WriteFile(digest.Value.Jsonl, FailuresDigestJsonlFileName));
        }

        if (options.GenerateSpecificationsMarkdown)
        {
            var specsMarkdownFileName = $"{options.YamlSpecificationsFileName}.md";
            Add(specsMarkdownFileName, () => WriteFile(
                SpecificationsMarkdownGenerator.Generate(features, options.SpecificationsTitle),
                specsMarkdownFileName));
        }

        // Written for somebody else's tooling rather than for a reader of this directory, which is why it
        // is off by default and why it sits in the isolated list like everything else.
        if (options.GenerateCtrfReport)
        {
            Add(CtrfReportGenerator.FileName, () => WriteFile(
                CtrfReportGenerator.Generate(features, startRunTime, endRunTime, ciMetadata, KronikolVersion, suite),
                CtrfReportGenerator.FileName));
        }

        if (options.WriteAgentInstructions)
        {
            Add(AgentInstructionsGenerator.ClaudeFileName, () =>
            {
                var block = AgentInstructionsBlock.Wrap(AgentInstructionsGenerator.Build(options.HtmlTestRunReportFileName));
                WriteAgentInstructionsFile(block, AgentInstructionsGenerator.ClaudeFileName);
                WriteAgentInstructionsFile(block, AgentInstructionsGenerator.AgentsFileName);
            });
        }

        var written = RunOutputs(actions);

        var diagnostics = ReportDiagnostics.Analyse(
            RequestResponseLogger.RequestAndResponseLogs, features,
            includeSourceDiscovery: options.ActivitySourceDiscovery);
        foreach (var message in diagnostics)
            Console.WriteLine(message);

        if (options.DiagnosticMode)
            DiagnosticReportGenerator.Generate(RequestResponseLogger.RequestAndResponseLogs, features, options);

        // Gathered once, from what THIS RUN actually wrote: an output the isolated list could not write is
        // never named by the pointer or offered in the CI summary. Existence alone is not the test — a
        // file the previous run left behind exists, so naming it would hand the reader another run's bytes
        // under this run's heading, which is the one failure mode a pointer cannot afford.
        var runSummary = RunSummaryConsoleWriter.Summarise(
            features,
            reportsDir,
            new[]
            {
                $"{options.HtmlTestRunReportFileName}.html",
                $"{options.HtmlTestRunReportFileName}.{testRunDataExtension}",
                FailuresDigestFileName
            }.Where(written.Contains),
            agentInstructionsWritten: options.WriteAgentInstructions
                                      && File.Exists(Path.Combine(reportsDir, AgentInstructionsGenerator.ClaudeFileName)),
            suite: suite);

        if (options.WriteCiSummary)
        {
            var (truncatedDiagrams, fullDiagrams) = DefaultDiagramsFetcher.GetCiSummaryDiagrams(fetcherOptions);
            var markdown = CiSummaryGenerator.GenerateMarkdown(features, truncatedDiagrams, fullDiagrams, startRunTime, endRunTime, options.MaxCiSummaryDiagrams,
                options.DiagramFormat, options.PlantUmlServerBaseUrl, options.LocalDiagramRenderer);

            // The job summary is written to a file descriptor rather than to stdout, so unlike the console
            // pointer it survives every runner — which makes it the reliable place to say how to debug.
            markdown += RunSummaryConsoleWriter.BuildCiSummarySection(runSummary);

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
                    .Where(f => f.EndsWith(".html") || f.EndsWith(".yml") || f.EndsWith(".md") || f.EndsWith(".json") || f.EndsWith(".jsonl") || f.EndsWith(".xml"))
                    .ToArray();
                CiArtifactPublisher.Publish(reportFiles, ciEnv, options.CiArtifactName, options.CiArtifactRetentionDays);
            }
        }

        // A failing CI job says how to debug itself, on both channels, without anyone having had to turn
        // on the 48 KB diagram-bearing CiSummary.md to get it. Two channels because neither is enough on
        // its own: content written only to $GITHUB_STEP_SUMMARY is absent from `gh run view --log`, which
        // is the command an agent reaches for, and the console is swallowed by runners — measured on .NET
        // 10, `dotnet test` under NUnit 4 let a library's stderr through and swallowed its stdout, while
        // xUnit 2 swallowed both.
        //
        // Skipped when WriteCiSummary already ran, which appends the same block to the same place.
        if (options.WriteCiDebugSection && !options.WriteCiSummary && runSummary.Failures.Count > 0)
        {
            var ciEnvironment = CiEnvironmentDetector.Detect();
            if (ciEnvironment != CiEnvironment.None)
            {
                var debugSection = RunSummaryConsoleWriter.BuildCiSummarySection(runSummary);
                Console.WriteLine(debugSection);
                CiSummaryWriter.Write(debugSection, ciEnvironment);
            }
        }

        // Last, so it is the final thing the run says.
        if (options.WriteRunSummaryToConsole)
            RunSummaryConsoleWriter.Write(runSummary, CiEnvironmentDetector.Detect(), Console.WriteLine);
    }

    /// <summary>The failure digest's two file names — the markdown an agent reads and its machine-readable twin.</summary>
    internal const string FailuresDigestFileName = "Failures.md";

    internal const string FailuresDigestJsonlFileName = "Failures.jsonl";

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
    /// <returns>
    /// The names of the outputs that completed. The pointer is built from this rather than from what is on
    /// disk, because a file left behind by an EARLIER run exists — so an output that threw left the
    /// previous run's bytes under the name everything uses, and the pointer named them, sized them and
    /// sent the reader to them. The worst case read exactly like the best one.
    /// </returns>
    private static HashSet<string> RunOutputs(List<(string Name, Action Run)> outputs)
    {
        var written = new HashSet<string>(StringComparer.Ordinal);

        Parallel.Invoke(outputs.Select(output => (Action)(() =>
        {
            try
            {
                output.Run();
                lock (written)
                    written.Add(output.Name);
            }
            catch (Exception ex)
            {
                ReportDiagnosticsScope.Record(DiagnosticKind.OutputFailure, $"Could not write {output.Name}", ex);
                Console.WriteLine($"⚠ WARNING: could not write {output.Name}: {ex.GetType().Name}: {ex.Message}");
            }
        })).ToArray());

        return written;
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

    /// <summary>The JS state string for a details radio state ('truncated' / 'expanded' / 'collapsed').</summary>
    private static string DetailsStateJs(ReportDetailsState state) => state switch
    {
        ReportDetailsState.Expanded => "expanded",
        ReportDetailsState.Collapsed => "collapsed",
        _ => "truncated"
    };

    /// <summary>
    /// The Details radio group + truncate-lines dropdown, with the configured start state computed
    /// into the markup: which button carries <c>details-active</c>, which <c>&lt;option&gt;</c> is
    /// <c>selected</c> (the list is built from <see cref="TruncateLineCount"/>, the single source of
    /// truth for the presets), and <c>disabled</c> on the select whenever the state is not
    /// truncated — <c>syncRadioButtons</c> disables it on every state change, so the markup must
    /// agree before the first click. Report and scenario level differ only in their handlers.
    /// </summary>
    private static string BuildDetailsRadio(ResolvedToggleDefaults toggles, bool scenarioLevel)
    {
        string Active(ReportDetailsState state) => toggles.Details == state ? " details-active" : "";
        string Click(string state) => scenarioLevel
            ? $"window._setAllNotes(this,'{state}')"
            : $"window._setReportDetails('{state}')";
        var change = scenarioLevel ? "window._setScenarioTruncateLines(this)" : "window._setTruncateLines(this)";
        var disabled = toggles.Details == ReportDetailsState.Truncated ? "" : " disabled";
        var options = string.Concat(Enum.GetValues<TruncateLineCount>().Select(v =>
            $"<option value=\"{(int)v}\"{(v == toggles.TruncateLines ? " selected" : "")}>{(int)v}</option>"));
        return "<span class=\"details-radio\"><span class=\"details-radio-label\">Details:</span>"
            + $"<button class=\"details-radio-btn{Active(ReportDetailsState.Expanded)}\" data-state=\"expanded\" onclick=\"{Click("expanded")}\">Expand</button>"
            + $"<button class=\"details-radio-btn{Active(ReportDetailsState.Collapsed)}\" data-state=\"collapsed\" onclick=\"{Click("collapsed")}\">Collapse</button>"
            + $"<button class=\"details-radio-btn{Active(ReportDetailsState.Truncated)}\" data-state=\"truncated\" onclick=\"{Click("truncated")}\">Truncate</button>"
            + $"<select class=\"truncate-lines-select\" autocomplete=\"off\"{disabled} onchange=\"{change}\">{options}</select>"
            + "<span class=\"truncate-lines-label\">lines</span></span>";
    }

    /// <summary>
    /// One Shown/Hidden filter toggle button (headers / assertions / steps / databases) with the
    /// configured start state computed into class, <c>data-shown</c> and label — the label must
    /// match what <c>syncToggleBtn</c> derives from the toggle name on every later click.
    /// </summary>
    private static string BuildFilterToggleButton(string toggleName, bool shown, bool scenarioLevel)
    {
        var label = char.ToUpperInvariant(toggleName[0]) + toggleName[1..];
        var handler = scenarioLevel ? $"window._toggleScenario{label}(this)" : $"window._toggle{label}(this)";
        return $"<button class=\"details-radio-btn toggle-btn{(shown ? " details-active" : "")}\" data-toggle=\"{toggleName}\" data-shown=\"{(shown ? "true" : "false")}\" onclick=\"{handler}\">{label} {(shown ? "Shown" : "Hidden")}</button>";
    }

    /// <summary>
    /// Which diagram-type tab a scenario's diagram section starts on: the requested tab where that
    /// view exists for this scenario, else the built-in fallback order (sequence, then activity,
    /// then flame) — so a configured tab can never select an absent view.
    /// </summary>
    private static DiagramTabKind ResolveDiagramTab(DiagramTabKind requested, bool hasSeq, bool hasActivity, bool hasFlame)
    {
        if (requested == DiagramTabKind.Sequence && hasSeq) return DiagramTabKind.Sequence;
        if (requested == DiagramTabKind.Activity && hasActivity) return DiagramTabKind.Activity;
        if (requested == DiagramTabKind.FlameChart && hasFlame) return DiagramTabKind.FlameChart;
        return hasSeq ? DiagramTabKind.Sequence : hasActivity ? DiagramTabKind.Activity : DiagramTabKind.FlameChart;
    }

    /// <summary>
    /// The scenario-level diagram toolbar controls (spacer, Details radio, filter toggles, note
    /// format dropdown). Built once per report — the five scenario toolbar emission sites all use
    /// this builder, so they cannot drift (the same contract as the note-format select).
    /// </summary>
    private static string BuildScenarioDiagramToolbar(ResolvedToggleDefaults toggles,
        bool includeAssertions, bool includeSteps, bool includeDatabases, string scenarioNoteFormatSelect,
        string scenarioNoteFontSelect = "", string scenarioNoteWidthSelect = "")
    {
        var sb = new StringBuilder();
        sb.Append("<span class=\"diagram-toggle-spacer\"></span>");
        sb.Append(BuildDetailsRadio(toggles, scenarioLevel: true));
        sb.Append(BuildFilterToggleButton("headers", toggles.HeadersShown, scenarioLevel: true));
        if (includeAssertions)
            sb.Append(BuildFilterToggleButton("assertions", toggles.AssertionsShown, scenarioLevel: true));
        if (includeSteps)
            sb.Append(BuildFilterToggleButton("steps", toggles.StepsShown, scenarioLevel: true));
        if (includeDatabases)
            sb.Append(BuildFilterToggleButton("databases", toggles.DatabasesShown, scenarioLevel: true));
        sb.Append(scenarioNoteFormatSelect);
        sb.Append(scenarioNoteFontSelect);
        sb.Append(scenarioNoteWidthSelect);
        return sb.ToString();
    }

    /// <summary>
    /// The note appearance dropdowns (payload font, note width), emitted beside the filter toggles
    /// at report and scenario level and built once so the toolbar variants cannot drift — the same
    /// contract as the JSON/YAML select. Both are <see cref="PlantUmlRendering.BrowserJs"/>-only:
    /// they re-render the diagram client-side, which no other rendering mode can do.
    /// </summary>
    private static string BuildNoteAppearanceSelect(string cssClass, string label, string handler,
        (string Value, string Text)[] options, string selected) =>
        $"<select class=\"{cssClass}\" autocomplete=\"off\" aria-label=\"{label}\" title=\"{label}\" onchange=\"window.{handler}(this)\">"
        + string.Concat(options.Select(o =>
            $"<option value=\"{o.Value}\"{(o.Value == selected ? " selected" : "")}>{o.Text}</option>"))
        + "</select>";

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
        SearchIndex.SearchIndexBuildCache? searchIndexCache = null,
        ResolvedToggleDefaults? toggleDefaults = null,
        string? suite = null)
    {
        if (generateBlankOnFailedTests && features.Any(x => x.Scenarios.Any(y => y.Result == ExecutionResult.Failed)))
            return WriteFile(string.Empty, fileName);

        // Defaulted here as well as in the data writers, and for the same reason: the HTML's
        // data-stable-id attributes and the data file's stableId values are the same identity, and a
        // caller that reaches one writer directly must not get a different answer from the other.
        suite ??= RunSuite.Current;

        // The resolved toggle-defaults record wins when provided; the legacy notePayloadFormat
        // parameter (kept for compatibility — tests and helpers call it directly) folds into the
        // built-ins otherwise.
        var toggles = toggleDefaults ?? ResolvedToggleDefaults.BuiltIn with { NotePayloadFormat = notePayloadFormat };

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

        // The filter-mode scripts seed their AND/OR start mode from the resolved toggle defaults;
        // the substitution happens on the returned copy, never in the resource cache.
        var depModeText = toggles.DependencyFilterMode == FilterCombinationMode.Or ? "OR" : "AND";
        var catModeText = toggles.CategoryFilterMode == FilterCombinationMode.And ? "AND" : "OR";
        var dependencyFilterFunction = LoadResource("report-dependency-filter-function.js")
            .Replace("__DEP_MODE_DEFAULT__", depModeText);

        var categoryFilterFunction = LoadResource("report-category-filter-function.js")
            .Replace("__CAT_MODE_DEFAULT__", catModeText);

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
        var yamlDefaultSelected = toggles.NotePayloadFormat == NotePayloadFormat.Yaml;
        var noteFormatOptions = $"<option value=\"json\"{(yamlDefaultSelected ? "" : " selected")}>JSON</option><option value=\"yaml\"{(yamlDefaultSelected ? " selected" : "")}>YAML</option>";
        var reportNoteFormatSelect = hasJsonNotePayloads
            ? $"<select class=\"note-format-select\" autocomplete=\"off\" aria-label=\"Note payload format\" title=\"Note payload format\" onchange=\"window._setNoteFormat(this)\">{noteFormatOptions}</select>"
            : "";
        var scenarioNoteFormatSelect = hasJsonNotePayloads
            ? $"<select class=\"note-format-select\" autocomplete=\"off\" aria-label=\"Note payload format\" title=\"Note payload format\" onchange=\"window._setScenarioNoteFormat(this)\">{noteFormatOptions}</select>"
            : "";
        // The note appearance controls need notes to act on, and nothing more: unlike the JSON/YAML
        // select they apply to any payload, so the gate is simply "this report draws notes".
        var hasDiagramNotes = isPlantUmlBrowser && (
            (trackedLogs is not null && trackedLogs.Any(l => l.PlantUml is not null && l.PlantUml.Contains("\nnote "))) ||
            diagrams.Any(d => d.CodeBehind.Contains("\nnote left") || d.CodeBehind.Contains("\nnote right")));
        (string, string)[] noteFontOptions = [("default", "Aa"), ("mono", "Mono")];
        (string, string)[] noteWidthOptions = [("default", "Fit"), ("full", "Full")];
        var fontSelected = toggles.NoteFont == NoteFontFamily.Monospace ? "mono" : "default";
        var widthSelected = toggles.NoteWidth == NoteWidthMode.Full ? "full" : "default";
        var reportNoteFontSelect = hasDiagramNotes
            ? BuildNoteAppearanceSelect("note-font-select", "Note payload font", "_setNoteFont", noteFontOptions, fontSelected)
            : "";
        var scenarioNoteFontSelect = hasDiagramNotes
            ? BuildNoteAppearanceSelect("note-font-select", "Note payload font", "_setScenarioNoteFont", noteFontOptions, fontSelected)
            : "";
        var reportNoteWidthSelect = hasDiagramNotes
            ? BuildNoteAppearanceSelect("note-width-select", "Note width", "_setNoteWidth", noteWidthOptions, widthSelected)
            : "";
        var scenarioNoteWidthSelect = hasDiagramNotes
            ? BuildNoteAppearanceSelect("note-width-select", "Note width", "_setScenarioNoteWidth", noteWidthOptions, widthSelected)
            : "";
        // The full scenario-level control run, built once (see BuildScenarioDiagramToolbar). The
        // no-filters variant serves the flow-only branch, which has no sequence content to filter.
        var scenarioToolbarControls = BuildScenarioDiagramToolbar(toggles,
            hasAssertionNotes, hasStepDelimiters, hasDatabaseParticipants, scenarioNoteFormatSelect,
            scenarioNoteFontSelect, scenarioNoteWidthSelect);
        var scenarioToolbarControlsNoFilters = BuildScenarioDiagramToolbar(toggles,
            includeAssertions: false, includeSteps: false, includeDatabases: false, scenarioNoteFormatSelect,
            scenarioNoteFontSelect, scenarioNoteWidthSelect);
        var plantUmlBrowserScript = isPlantUmlBrowser ? DiagramContextMenu.GetPlantUmlBrowserRenderScript(browserRenderWorkers, browserRenderCacheMegabytes, browserFragmentMaxHeight) : "";
        var collapsibleNotesScript = isPlantUmlBrowser ? DiagramContextMenu.GetCollapsibleNotesScript(toggles) : "";
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
            body.Append($"<details class=\"features-summary-details\"{(toggles.FeaturesSummaryOpen ? " open" : "")}><summary class=\"h2\">Features Summary</summary>");
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
            body.Append($"""<div class="dependency-filters"><span class="dependency-filters-label">Dependencies:</span><button class="dep-mode-toggle" title="AND: show scenarios matching ALL selected dependencies. OR: show scenarios matching ANY selected dependency. Click to toggle." onclick="toggle_dep_mode(this)">{depModeText}</button>""");
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
            body.Append($"""<div class="category-filters"><span class="category-filters-label">Categories:</span><button class="cat-mode-toggle" title="OR: show scenarios matching ANY selected category. AND: show scenarios matching ALL selected categories. Click to toggle." onclick="toggle_cat_mode(this)">{catModeText}</button>""");
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
        // The expand-all buttons flip by label, so a seeded-expanded start must seed the
        // matching flip label ("Collapse All …") or the first click would be a no-op.
        var featuresBtnLabel = toggles.FeaturesExpanded ? "Collapse All Features" : "Expand All Features";
        var scenariosBtnLabel = toggles.ScenariosExpanded ? "Collapse All Scenarios" : "Expand All Scenarios";
        body.Append($"""<div class="toolbar-left"><button class="collapse-expand-all" onclick="toggle_expand_collapse(this, 'details.feature', 'Expand All Features', 'Collapse All Features')">{featuresBtnLabel}</button><button class="collapse-expand-all" onclick="toggle_expand_collapse(this, 'details.scenario', 'Expand All Scenarios', 'Collapse All Scenarios')">{scenariosBtnLabel}</button>""");
        // Seeded panel visibility: the two panels are mutually exclusive; when both are
        // configured visible the timeline wins (it exists in both reports), with the
        // component diagram one click away. A panel that is not emitted leaves its setting
        // inert — so a timeline-less report can still seed the component diagram visible.
        var showTimelinePanel = toggles.ScenarioTimelineVisible && hasDurations;
        var showComponentPanel = toggles.ComponentDiagramVisible && !string.IsNullOrEmpty(componentDiagramPlantUml) && !showTimelinePanel;
        if (hasDurations)
            body.Append($"""<button class="timeline-toggle{(showTimelinePanel ? " timeline-toggle-active" : "")}" onclick="toggle_timeline(this)">Scenario Timeline</button>""");
        if (!string.IsNullOrEmpty(componentDiagramPlantUml))
            body.Append($"""<button class="timeline-toggle{(showComponentPanel ? " timeline-toggle-active" : "")}" onclick="toggle_component_diagram(this)">Component Diagram</button>""");
        body.Append("</div>");
        body.Append("""<div class="toolbar-right">""");
        if (isPlantUmlBrowser)
        {
            body.Append(BuildDetailsRadio(toggles, scenarioLevel: false));
            body.Append(BuildFilterToggleButton("headers", toggles.HeadersShown, scenarioLevel: false));
            if (hasAssertionNotes)
                body.Append(BuildFilterToggleButton("assertions", toggles.AssertionsShown, scenarioLevel: false));
            if (hasStepDelimiters)
                body.Append(BuildFilterToggleButton("steps", toggles.StepsShown, scenarioLevel: false));
            if (hasDatabaseParticipants)
                body.Append(BuildFilterToggleButton("databases", toggles.DatabasesShown, scenarioLevel: false));
            body.Append(reportNoteFormatSelect);
            body.Append(reportNoteFontSelect);
            body.Append(reportNoteWidthSelect);
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

            body.Append($"<details class=\"failure-clusters\"{(toggles.FailureClustersOpen ? " open" : "")}>");
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
            body.Append(RenderReportDiagnostics(diagnostics, toggles.DiagnosticsOpen));

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
                body.Append($"<div id=\"scenario-timeline\" class=\"scenario-timeline\"{(showTimelinePanel ? "" : " style=\"display:none\"")}>");
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
            body.Append($"""<div id="component-diagram" class="component-diagram-section"{(showComponentPanel ? "" : " style=\"display:none\"")}><div class="plantuml-browser" id="{compDiagramId}" data-diagram-type="plantuml"></div></div>""");
        }

        body.Append("<div id=\"report-content\">");
        var paramGroupCounter = 0;
        foreach (var feature in features)
        {
            var featureHasFailures = feature.Scenarios.Any(s => s.Result == ExecutionResult.Failed);
            var featureAllSkipped = !featureHasFailures && feature.Scenarios.All(s => s.Result == ExecutionResult.Skipped);
            body.Append($"""
                     <details class="feature"{(toggles.FeaturesExpanded ? " open" : "")}>
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
                        body.Append($"<details class=\"rule\"{(toggles.RulesOpen ? " open" : "")}><summary class=\"h2-5\">{System.Net.WebUtility.HtmlEncode(currentRule)}</summary>");
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
                        searchIndexPieces: searchIndexPieces,
                        scenarioToolbarControls: scenarioToolbarControls,
                        toggleDefaults: toggles,
                        // Without this the flat outline rows compute their ids with no suite while the
                        // scenario <details> around them computes with one, so one report carries two
                        // identity schemes and the deep link from a flat row resolves to nothing.
                        suite: suite);
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

                // The second address this element answers to. `id` is a slug of the display name, so
                // it moves on a rename and collides across features; the stable id is what the data
                // file, `kronikol query` and Failures.md all speak, and `#sid-<id>` resolves here.
                // It goes BEFORE ` id=`: the cluster-link pins match `[^>]*id="([^"]+)"` greedily,
                // and `data-stable-id="` ends in a word-boundary `id="` that would win that race.
                var scenarioStableId = ScenarioStableId.Compute(suite, feature.DisplayName, scenario.DisplayName, scenario.OutlineId, scenario.ExampleValues);

                body.Append($"""
                         <details class="scenario{(scenario.IsHappyPath ? " happy-path" : "")}"{(toggles.ScenariosExpanded ? " open" : "")}{depsAttr}{statusAttr}{searchAttr}{durationAttr}{categoriesAttr}{labelsAttr} data-stable-id="{scenarioStableId}" id="{anchorId}" tabindex="0">
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
                              Error: {System.Net.WebUtility.HtmlEncode(scenario.ErrorMessage)}
                              {FailureCauseLine(scenario.FailureCause)}
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

                RenderScenarioStepSections(body, scenario, showStepNumbers, separateBackgroundSteps, collapseRepeatedStepKeywords,
                    toggles.StepsSectionOpen, toggles.BackgroundStepsOpen);

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
                    var hasActivityView = hasWholeTestFlow && !string.IsNullOrEmpty(wholeTestContent!.Value.ActivityHtml);
                    var hasFlameView = hasWholeTestFlow && !string.IsNullOrEmpty(wholeTestContent!.Value.FlameHtml);
                    var activeTab = ResolveDiagramTab(toggles.DiagramTab, hasSequenceDiagrams, hasActivityView, hasFlameView);

                    body.Append($"<details class=\"example-diagrams\"{(toggles.DiagramsSectionOpen ? " open" : "")}>");

                    if (hasWholeTestFlow && hasSequenceDiagrams)
                    {
                        body.Append("<summary class=\"h4\">Diagrams</summary>");
                        body.Append("<div class=\"diagram-toggle\">");
                        body.Append($"<button class=\"diagram-toggle-btn{(activeTab == DiagramTabKind.Sequence ? " diagram-toggle-active" : "")}\" data-dtype=\"seq\">Sequence Diagrams</button>");
                        if (hasActivityView)
                            body.Append($"<button class=\"diagram-toggle-btn{(activeTab == DiagramTabKind.Activity ? " diagram-toggle-active" : "")}\" data-dtype=\"activity\">Activity Diagrams</button>");
                        if (hasFlameView)
                            body.Append($"<button class=\"diagram-toggle-btn{(activeTab == DiagramTabKind.FlameChart ? " diagram-toggle-active" : "")}\" data-dtype=\"flame\">Flame Chart</button>");
                        body.Append(spanWarning);
                        if (isPlantUmlBrowser)
                            body.Append(scenarioToolbarControls);
                        body.Append("</div>");
                    }
                    else if (hasSequenceDiagrams)
                    {
                        body.Append("<summary class=\"h4\">Sequence Diagrams</summary>");
                        if (isPlantUmlBrowser)
                        {
                            body.Append("<div class=\"diagram-toggle\">");
                            body.Append(scenarioToolbarControls);
                            body.Append("</div>");
                        }
                    }
                    else
                    {
                        // Only whole-test-flow, no sequence diagrams
                        var hasActivity = hasActivityView;
                        var hasFlame = hasFlameView;
                        if (hasActivity && hasFlame)
                        {
                            body.Append("<summary class=\"h4\">Diagrams</summary>");
                            body.Append("<div class=\"diagram-toggle\">");
                            body.Append($"<button class=\"diagram-toggle-btn{(activeTab == DiagramTabKind.Activity ? " diagram-toggle-active" : "")}\" data-dtype=\"activity\">Activity Diagrams</button>");
                            body.Append($"<button class=\"diagram-toggle-btn{(activeTab == DiagramTabKind.FlameChart ? " diagram-toggle-active" : "")}\" data-dtype=\"flame\">Flame Chart</button>");
                            body.Append(spanWarning);
                            if (isPlantUmlBrowser)
                            {
                                body.Append(scenarioToolbarControlsNoFilters);
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
                        if (seqWrap) body.Append($"<div class=\"diagram-view diagram-view-seq\"{(activeTab != DiagramTabKind.Sequence ? " style=\"display:none\"" : "")}>");

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
                                         <details class="example"{(toggles.RawPlantUmlOpen ? " open" : "")}>
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
                        var hideActivity = activeTab != DiagramTabKind.Activity;
                        var hideFlame = activeTab != DiagramTabKind.FlameChart;

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
            AppendYaml(yml, "  - Feature: ", feature.DisplayName);

            if (feature.Endpoint is not null)
                yml.Append("    Endpoint: " + feature.Endpoint + "\n");

            if (feature.Description is not null)
                AppendYaml(yml, "    Description: ", feature.Description);

            if (feature.Labels is { Length: > 0 })
            {
                yml.Append("    Labels:\n");
                foreach (var label in feature.Labels)
                    AppendYaml(yml, "      - ", label);
            }

            yml.Append("    Scenarios:\n");

            var orderedScenarios = feature.Scenarios.OrderByDescending(x => x.IsHappyPath).ThenBy(x => x.DisplayName);
            foreach (var scenario in orderedScenarios)
            {
                AppendYaml(yml, "      - Scenario: ", scenario.DisplayName);
                yml.Append("        IsHappyPath: " + scenario.IsHappyPath.ToString().ToLower() + "\n");

                if (scenario.Labels is { Length: > 0 })
                {
                    yml.Append("        Labels:\n");
                    foreach (var label in scenario.Labels)
                        AppendYaml(yml, "          - ", label);
                }

                if (scenario.Categories is { Length: > 0 })
                {
                    yml.Append("        Categories:\n");
                    foreach (var cat in scenario.Categories)
                        AppendYaml(yml, "          - ", cat);
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
        AppendYaml(yml, indent + "- ", text);

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
        Dictionary<string, List<string>>? searchIndexPieces = null,
        string scenarioToolbarControls = "",
        ResolvedToggleDefaults? toggleDefaults = null,
        string? suite = null)
    {
        var toggles = toggleDefaults ?? ResolvedToggleDefaults.BuiltIn;
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

        body.Append($"<details class=\"scenario scenario-parameterized{happyPathClass}\"{(toggles.ScenariosExpanded ? " open" : "")} data-status=\"{overallStatus}\"{depsAttr}{searchAttr}{durationAttr}{categoriesAttr}{labelsAttr} id=\"{anchorId}\" tabindex=\"0\">");
        body.Append($"<summary class=\"h3{(hasFailure ? " failed" : hasSkipped ? " skipped" : "")}\">{encodedGroupName}{happyPathBadge}{summaryText}{durationBadge}<button class=\"copy-scenario-name\" title=\"Copy scenario name\" data-scenario-name=\"{encodedGroupName}\" onclick=\"copy_scenario_name(this, event)\">&#128203;</button><a class=\"scenario-link\" href=\"#{anchorId}\" title=\"Link to this scenario\" onclick=\"event.stopPropagation()\">&#128279;</a></summary>");

        // Parameter table — where both views exist, the configured default picks which one
        // starts visible (toggleFlattenParams reads live visibility, so it needs no seeding).
        var hasFlatView = group.FlatParameterNames is { Length: > 0 };
        var startGrouped = hasFlatView && toggles.ParameterTableView == ParameterTableView.Grouped;
        if (hasFlatView) body.Append("<div class=\"param-table-wrapper\">");

        // Flat parameter table (visible by default) — shows original Gherkin Example columns as scalar values
        if (hasFlatView)
        {
            var flatNames = group.FlatParameterNames!;
            body.Append($"<table class=\"param-test-table param-table-flat\"{(startGrouped ? " style=\"display:none\"" : "")} data-prefix=\"{prefix}\"><thead>");
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

                // The flat table is the one displayed by default, so its rows carry the stable id
                // too — deliberately without an `id`, which the grouped copy of the same row owns
                // (Flat_table_rows_have_no_id_attribute). Duplicate data attributes are legal; the
                // hash script picks whichever copy is displayed.
                var flatRowStableId = ScenarioStableId.Compute(suite, featureDisplayName ?? "", s.DisplayName, s.OutlineId, s.ExampleValues);
                body.Append($"<tr class=\"{rowStatusClass}{activeClass}\" data-row-idx=\"{ri}\" data-stable-id=\"{flatRowStableId}\"{rowSearchAttr} onclick=\"selectRow(this,'{prefix}')\">");
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

        // Grouped parameter table (hidden when the flat view exists and starts visible)
        var groupedTableClass = hasFlatView ? " param-table-grouped" : "";
        var groupedStyle = hasFlatView && !startGrouped ? " style=\"display:none\"" : "";
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
            // Every row of an outline shares one display name, so the slug cannot address a row and
            // the stable id — which hashes the example values — is the only handle `#sid-` can use.
            var rowStableId = ScenarioStableId.Compute(suite, featureDisplayName ?? "", s.DisplayName, s.OutlineId, s.ExampleValues);
            body.Append($"<tr class=\"{rowStatusClass}{activeClass}\" data-row-idx=\"{ri}\" data-stable-id=\"{rowStableId}\" id=\"{rowAnchorId}\" data-scenario-id=\"{rowAnchorId}\"{rowSearchAttr} onclick=\"selectRow(this,'{prefix}')\">");
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

                RenderScenarioStepSections(body, s, showStepNumbers, separateBackgroundSteps, collapseRepeatedStepKeywords,
                    toggles.StepsSectionOpen, toggles.BackgroundStepsOpen);

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
                        body.Append($"Error: {System.Net.WebUtility.HtmlEncode(s.ErrorMessage)}\n{FailureCauseLine(s.FailureCause)}\n");
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
            body.Append($"<details class=\"example-diagrams\"{(toggles.DiagramsSectionOpen ? " open" : "")}>");

            // Determine toggle buttons needed
            var showSeqToggle = hasAnySeqDiagrams;
            var showActivityToggle = hasAnyWholeTestFlow && wholeTestContents.Any(w => !string.IsNullOrEmpty(w?.ActivityHtml));
            var showFlameToggle = hasAnyWholeTestFlow && wholeTestContents.Any(w => !string.IsNullOrEmpty(w?.FlameHtml));
            var multipleTypes = (showSeqToggle ? 1 : 0) + (showActivityToggle ? 1 : 0) + (showFlameToggle ? 1 : 0) > 1;
            var activeTab = ResolveDiagramTab(toggles.DiagramTab, showSeqToggle, showActivityToggle, showFlameToggle);

            if (multipleTypes)
            {
                body.Append("<summary class=\"h4\">Diagrams</summary>");
                body.Append("<div class=\"diagram-toggle\">");
                if (showSeqToggle)
                    body.Append($"<button class=\"diagram-toggle-btn{(activeTab == DiagramTabKind.Sequence ? " diagram-toggle-active" : "")}\" data-dtype=\"seq\">Sequence Diagrams</button>");
                if (showActivityToggle)
                    body.Append($"<button class=\"diagram-toggle-btn{(activeTab == DiagramTabKind.Activity ? " diagram-toggle-active" : "")}\" data-dtype=\"activity\">Activity Diagrams</button>");
                if (showFlameToggle)
                    body.Append($"<button class=\"diagram-toggle-btn{(activeTab == DiagramTabKind.FlameChart ? " diagram-toggle-active" : "")}\" data-dtype=\"flame\">Flame Chart</button>");
                if (isPlantUmlBrowser && showSeqToggle)
                {
                    body.Append(scenarioToolbarControls);
                }
                else
                {
                    // No sequence toggle: the Details radio and headers toggle stay out, but the
                    // report-wide filter buttons and the note-format select are still offered
                    // (their gates are report-wide, matching the pre-builder emission).
                    if (hasAssertionNotes)
                        body.Append(BuildFilterToggleButton("assertions", toggles.AssertionsShown, scenarioLevel: true));
                    if (hasStepDelimiters)
                        body.Append(BuildFilterToggleButton("steps", toggles.StepsShown, scenarioLevel: true));
                    if (hasDatabaseParticipants)
                        body.Append(BuildFilterToggleButton("databases", toggles.DatabasesShown, scenarioLevel: true));
                    body.Append(scenarioNoteFormatSelect);
                }
                body.Append("</div>");
            }
            else if (showSeqToggle)
            {
                body.Append("<summary class=\"h4\">Sequence Diagrams</summary>");
                if (isPlantUmlBrowser)
                {
                    body.Append("<div class=\"diagram-toggle\">");
                    body.Append(scenarioToolbarControls);
                    body.Append("</div>");
                }
            }
            else if (showActivityToggle && showFlameToggle)
            {
                body.Append("<summary class=\"h4\">Diagrams</summary>");
                body.Append("<div class=\"diagram-toggle\">");
                body.Append($"<button class=\"diagram-toggle-btn{(activeTab == DiagramTabKind.Activity ? " diagram-toggle-active" : "")}\" data-dtype=\"activity\">Activity Diagrams</button>");
                body.Append($"<button class=\"diagram-toggle-btn{(activeTab == DiagramTabKind.FlameChart ? " diagram-toggle-active" : "")}\" data-dtype=\"flame\">Flame Chart</button>");
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
                if (seqWrap) body.Append($"<div class=\"diagram-view diagram-view-seq\"{(activeTab != DiagramTabKind.Sequence ? " style=\"display:none\"" : "")}>");

                if (group.AllDiagramsIdentical)
                {
                    var firstDiagrams = diagramsByTestId[scenarios[0].Id].ToArray();
                    if (firstDiagrams.Length > 0)
                    {
                        body.Append("<span class=\"param-diagram-identical-badge\">All diagrams identical across test cases</span>");
                        RenderDiagramsForScenario(body, firstDiagrams, isPlantUmlBrowser, isInlineSvg, lazyLoadImages, ref plantUmlBrowserCounter, diagramDataMap, toggles.RawPlantUmlOpen);
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
                            RenderDiagramsForScenario(body, diagrams, isPlantUmlBrowser, isInlineSvg, lazyLoadImages, ref plantUmlBrowserCounter, diagramDataMap, toggles.RawPlantUmlOpen);
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
                var hideActivity = activeTab != DiagramTabKind.Activity;
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
                var hideFlame = activeTab != DiagramTabKind.FlameChart;
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
        Dictionary<string, string> diagramDataMap,
        bool rawPlantUmlOpen = false)
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
                         <details class="example"{(rawPlantUmlOpen ? " open" : "")}>
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
        bool collapseRepeatedStepKeywords,
        bool stepsSectionOpen = true,
        bool backgroundStepsOpen = false)
    {
        var background = scenario.BackgroundSteps ?? [];
        var steps = scenario.Steps ?? [];

        if (separateBackgroundSteps)
        {
            if (background.Length > 0)
            {
                // Each section collapses independently, so the Steps list still opens with its own primary.
                var backgroundKeywords = collapseRepeatedStepKeywords ? StepKeywordCollapser.DisplayKeywords(background) : null;
                body.Append($"""<details class="scenario-background"{(backgroundStepsOpen ? " open" : "")}>""");
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
                RenderStepsList(body, steps, showStepNumbers, background.Length, backgroundCount: 0, collapseRepeatedStepKeywords, stepsSectionOpen);

            return;
        }

        ScenarioStep[] combined = background.Length == 0 ? steps
            : steps.Length == 0 ? background
            : [.. background, .. steps];

        if (combined.Length == 0)
            return;

        RenderStepsList(body, combined, showStepNumbers, numberOffset: 0, backgroundCount: background.Length, collapseRepeatedStepKeywords, stepsSectionOpen);
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
        bool collapseRepeatedStepKeywords,
        bool open = true)
    {
        var displayKeywords = collapseRepeatedStepKeywords ? StepKeywordCollapser.DisplayKeywords(steps) : null;

        body.Append($"""<details class="scenario-steps"{(open ? " open" : "")}>""");
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
    internal static string RenderReportDiagnostics(IReadOnlyList<DiagnosticEntry> diagnostics, bool open = false)
    {
        if (diagnostics.Count == 0)
            return string.Empty;

        var byKind = diagnostics.GroupBy(d => d.Kind)
            .OrderByDescending(g => g.Key == DiagnosticKind.CaptureDegraded)
            .ThenByDescending(g => g.Count())
            .Select(g => g.Count() == 1 ? g.Key.ToString() : $"{g.Key} ×{g.Count()}");
        var summary = $"Report diagnostics ({diagnostics.Count}: {string.Join(", ", byKind)})";

        var html = new StringBuilder();
        html.Append($"<details class=\"report-diagnostics\"{(open ? " open" : "")}>");
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

    /// <summary>
    /// The <c>ciMetadata</c> object, emitted whether or not the run was on CI. A shape that appears
    /// only sometimes is a shape nothing can key on, so off CI the same eight keys come back with
    /// <c>provider: "None"</c> and nulls beneath it — <see cref="CiMetadataDetector.Detect()"/> keeps
    /// returning null, because the HTML summary's CI table is gated on exactly that.
    /// </summary>
    private static object MapCiMetadataJson(CiMetadata? ciMetadata) => new
    {
        Provider = (ciMetadata?.Provider ?? CiEnvironment.None).ToString(),
        ciMetadata?.BuildNumber,
        ciMetadata?.Branch,
        ciMetadata?.CommitSha,
        ciMetadata?.PipelineUrl,
        ciMetadata?.Repository,
        ciMetadata?.RunId,
        ciMetadata?.RunAttempt
    };

    /// <summary>
    /// What to write for <c>environment</c>: the value given, this machine when the caller said nothing,
    /// or null when the caller said there is nothing true to write.
    /// </summary>
    /// <remarks>
    /// Null from the caller means "this machine", matching the <c>kronikolVersion</c> parameter beside it
    /// and leaving every existing caller writing exactly what it wrote before.
    /// <see cref="RunEnvironment.Unrecorded"/> is the other answer, and the writers leave the key out
    /// entirely for it - a merge whose shards disagreed, or an ingest of a run that was never on .NET,
    /// has no environment to report and should not borrow the reading process's.
    /// </remarks>
    private static RunEnvironment? ResolveEnvironment(RunEnvironment? environment)
    {
        var resolved = environment ?? RunEnvironment.Current;
        return ReferenceEquals(resolved, RunEnvironment.Unrecorded) ? null : resolved;
    }

    /// <summary>The <c>environment</c> object: what the run executed on, and nothing about who ran it.</summary>
    private static object? MapEnvironmentJson(RunEnvironment? environment) =>
        ResolveEnvironment(environment) is { } resolved ? new { resolved.Os, resolved.Runtime } : null;

    /// <summary>The <c>diagnostics</c> array of the data files: <c>{kind, message, scenarioId}</c> per entry.</summary>
    private static object[] MapDiagnosticsJson(IReadOnlyList<DiagnosticEntry>? diagnostics) =>
        (diagnostics ?? []).Select(d => (object)new { Kind = d.Kind.ToString(), d.Message, d.ScenarioId }).ToArray();

    /// <summary>
    /// Writes the test-run data file in the requested format.
    /// </summary>
    /// <remarks>
    /// <c>environment</c> is what the run executed on. Null means this machine, which is what a live run
    /// wants; <see cref="RunEnvironment.Unrecorded"/> leaves the key out, for a lane that cannot know.
    /// </remarks>
    public static string GenerateTestRunReportData(Feature[] features, DateTime startTime, DateTime endTime, string fileName, DataFormat format, DefaultDiagramsFetcher.DiagramAsCode[]? diagrams = null, RequestResponseLog[]? trackedLogs = null, IReadOnlyList<DiagnosticEntry>? diagnostics = null, bool fullStepDetail = true, CiMetadata? ciMetadata = null, string? suite = null, RunEnvironment? environment = null)
    {
        var diagramLookup = diagrams?.ToLookup(d => d.TestRuntimeId, d => d.CodeBehind);
        // Diagram markers belong to the diagram, not the interaction list: exported as-is they read as
        // content-free calls to http://override.com/ — one pair per Gherkin step and assertion.
        var logLookup = trackedLogs?.Where(l => !l.IsDiagramMarker).ToLookup(l => l.TestId);
        var durations = ComputeInteractionDurations(trackedLogs);
        var (stepPaths, annotations) = AttributeInteractionsToSteps(trackedLogs, features);

        return format switch
        {
            DataFormat.Json => WriteFile(GenerateTestRunReportJson(features, startTime, endTime, diagramLookup, logLookup, diagnostics, fullStepDetail, durations, stepPaths, annotations, ciMetadata, suite, environment), fileName),
            DataFormat.Xml => WriteFile(GenerateTestRunReportXml(features, startTime, endTime, diagramLookup, logLookup, diagnostics, durations, stepPaths, annotations, ciMetadata, suite, environment), fileName),
            DataFormat.Yaml => WriteFile(GenerateTestRunReportYaml(features, startTime, endTime, diagramLookup, logLookup, diagnostics, durations, stepPaths, annotations, ciMetadata, suite, environment), fileName),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
    }

    /// <summary>
    /// One scenario-level annotation: a diagram marker that carries information found nowhere else in the
    /// data file. Step and assertion markers are deliberately excluded — those are already structured in
    /// <c>steps</c>, and repeating them would be duplication rather than disclosure.
    /// </summary>
    internal sealed record ScenarioAnnotation(int Index, DiagramMarkerKind Kind, string Text);

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
    internal static Dictionary<string, List<string?>> AttributeInteractionsToStepPaths(RequestResponseLog[]? trackedLogs, Feature[] features) =>
        AttributeInteractionsToSteps(trackedLogs, features).StepPaths;

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

    private static string GenerateTestRunReportJson(Feature[] features, DateTime startTime, DateTime endTime, ILookup<string, string>? diagramLookup, ILookup<string, RequestResponseLog>? logLookup, IReadOnlyList<DiagnosticEntry>? diagnostics = null, bool fullStepDetail = true, IReadOnlyDictionary<Guid, double>? durations = null, IReadOnlyDictionary<string, List<string?>>? stepPaths = null, IReadOnlyDictionary<string, List<ScenarioAnnotation>>? annotations = null, CiMetadata? ciMetadata = null, string? suite = null, RunEnvironment? environment = null)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
        // Resolved ONCE and used for both the key and the ids under it. Writing `suite ?? RunSuite.Current`
        // at the key while passing the un-defaulted `suite` to the model made the file disagree with
        // itself: it named a suite its own stableIds had not been computed under.
        var resolvedSuite = suite ?? RunSuite.Current;
        // A dictionary rather than an anonymous type, because `environment` is left out when there is
        // none to report and an anonymous type cannot drop a member. Keys are written in their final
        // casing: the naming policy renames properties, not dictionary keys, and the feature model below
        // is already built the same way.
        var data = new Dictionary<string, object?>
        {
            // First, so a reader can check the contract before parsing anything that depends on it. The
            // same idiom the other two machine outputs already use (Failures.jsonl, query --json).
            ["formatVersion"] = ReportFormatVersion,
            ["kronikolVersion"] = KronikolVersion,
            ["suite"] = resolvedSuite,
            ["startTime"] = startTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["endTime"] = endTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
            // Before `features`, which is nearly the whole file: a streaming reader and a person
            // running `head` both see which run this is without the megabytes after it.
            ["ciMetadata"] = MapCiMetadataJson(ciMetadata)
        };

        if (MapEnvironmentJson(environment) is { } environmentJson)
            data["environment"] = environmentJson;

        data["features"] = BuildFeaturesJsonModel(features, diagramLookup, logLookup, fullStepDetail, durations, stepPaths, annotations, resolvedSuite);
        data["diagnostics"] = MapDiagnosticsJson(diagnostics);

        return JsonSerializer.Serialize(data, options);
    }

    /// <summary>
    /// Builds the serializable feature/scenario model shared by the standard test-run report JSON
    /// and the enriched "mergeable" JSON. Keeping a single source of truth ensures the mergeable
    /// format remains a strict superset that the merge reader can parse.
    /// </summary>
    private static object[] BuildFeaturesJsonModel(Feature[] features, ILookup<string, string>? diagramLookup, ILookup<string, RequestResponseLog>? logLookup, bool fullStepDetail = false, IReadOnlyDictionary<Guid, double>? durations = null, IReadOnlyDictionary<string, List<string?>>? stepPaths = null, IReadOnlyDictionary<string, List<ScenarioAnnotation>>? annotations = null, string? suite = null)
    {
        Func<ScenarioStep, object> stepMapper = fullStepDetail ? MapStepJsonFull : MapStepJson;
        return features.OrderBy(f => f.DisplayName).Select(f => (object)new Dictionary<string, object?>
        {
            ["name"] = f.DisplayName,
            ["endpoint"] = f.Endpoint,
            ["description"] = f.Description,
            ["sourceFile"] = f.SourceFile,
            ["labels"] = f.Labels ?? [],
            ["scenarios"] = f.Scenarios.Select(s =>
            {
                var scenario = new Dictionary<string, object?>
                {
                    ["id"] = s.Id,
                    ["stableId"] = ScenarioStableId.Compute(suite, f.DisplayName, s.DisplayName, s.OutlineId, s.ExampleValues),
                    ["name"] = s.DisplayName,
                    ["description"] = s.Description,
                    ["result"] = s.Result.ToString(),
                    ["durationSeconds"] = s.Duration?.TotalSeconds ?? 0.0,
                    ["isHappyPath"] = s.IsHappyPath,
                    ["errorMessage"] = s.ErrorMessage,
                    ["errorStackTrace"] = s.ErrorStackTrace,
                    ["failureCause"] = s.FailureCause,
                    ["labels"] = s.Labels ?? [],
                    ["categories"] = s.Categories ?? [],
                    ["rule"] = s.Rule,
                    ["attempt"] = s.Attempt,
                    ["sourceFile"] = s.SourceFile,
                    ["sourceLine"] = s.SourceLine,
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
        IReadOnlyList<DiagnosticEntry>? diagnostics = null,
        string? suite = null,
        RunEnvironment? environment = null)
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
                options.InternalFlowActivitySources,
                ReportToggleDefaultsResolver.Resolve(options, specifications: false).InternalFlowTab);
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
            options.WholeTestFlowVisualization, ciMetadata, diagnostics, trackedLogs, suite: suite, environment: environment);
    }

    /// <summary>
    /// Serializes the enriched "mergeable" test-run report: the standard JSON model — captured
    /// interactions and all — plus everything needed to reconstruct a full HTML report when merging
    /// multiple files: component relationships, precomputed internal-flow segment data, precomputed
    /// whole-test-flow fragments, CI metadata and the run's diagnostics.
    ///
    /// <para><paramref name="trackedLogs"/> is the run's captured traffic. Supplying it makes the file a
    /// genuine superset of the standard report; omitting it writes scenarios with no
    /// <c>httpInteractions</c>, which is what every mergeable file written before 3.1.0 looks like.</para>
    ///
    /// <para><paramref name="kronikolVersion"/> is the version that produced the run. It defaults to the
    /// writing assembly's, which is right for a live run and wrong for a merge —
    /// <see cref="Merge.MergeableReportRenderer"/> passes the source's.</para>
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
        IReadOnlyList<DiagnosticEntry>? diagnostics = null,
        RequestResponseLog[]? trackedLogs = null,
        string? kronikolVersion = null,
        IReadOnlyDictionary<string, List<string?>>? stepPathsOverride = null,
        IReadOnlyDictionary<string, List<ScenarioAnnotation>>? annotationsOverride = null,
        string? suite = null,
        RunEnvironment? environment = null)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

        // The same derivation the standard writer does. Without it the "superset" was missing the one
        // thing that dominates a report - every captured call - so a merged run could be read but not
        // debugged, and `kronikol query services|interactions|body|values|flow|trace` all came back empty.
        var logLookup = trackedLogs?.Where(l => !l.IsDiagramMarker).ToLookup(l => l.TestId);
        var durations = ComputeInteractionDurations(trackedLogs);

        // A merge hands both in: attribution reads the diagram markers, and those never round-trip
        // through the file, so re-deriving from re-read logs would quietly blank every stepPath.
        IReadOnlyDictionary<string, List<string?>> stepPaths;
        IReadOnlyDictionary<string, List<ScenarioAnnotation>> annotations;
        if (stepPathsOverride is not null || annotationsOverride is not null)
        {
            stepPaths = stepPathsOverride ?? new Dictionary<string, List<string?>>();
            annotations = annotationsOverride ?? new Dictionary<string, List<ScenarioAnnotation>>();
        }
        else
        {
            (stepPaths, annotations) = AttributeInteractionsToSteps(trackedLogs, features);
        }

        var data = new Dictionary<string, object?>
        {
            // The version that produced the RUN, not the one doing the writing: a merge re-serialises
            // someone else's data, and `query summary` prints this to say which contract the file honours.
            ["formatVersion"] = ReportFormatVersion,
            ["kronikolVersion"] = string.IsNullOrEmpty(kronikolVersion) ? KronikolVersion : kronikolVersion,
            ["mergeableFormatVersion"] = Merge.MergeableReportReader.MergeableFormatVersion,
            ["suite"] = suite,
            ["startTime"] = startTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["endTime"] = endTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["features"] = BuildFeaturesJsonModel(features, diagramLookup, logLookup, fullStepDetail: true, durations, stepPaths, annotations, suite),
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
            ["ciMetadata"] = MapCiMetadataJson(ciMetadata),
            ["diagnostics"] = MapDiagnosticsJson(diagnostics)
        };

        // Left out rather than written as null: a merged report whose shards disagreed has no
        // environment, and a null would read as "unknown" rather than "this file does not record one".
        if (MapEnvironmentJson(environment) is { } environmentJson)
            data["environment"] = environmentJson;

        return JsonSerializer.Serialize(data, options);
    }

    /// <summary>
    /// An interaction in the data files. Everything the diagram renderer reads off the record travels with
    /// it — the categorisation that decides participant shape, the phase, the W3C trace ids that bridge to
    /// OpenTelemetry and application logs, which capture path produced it, and the derived duration —
    /// so a reader of the JSON is never told less than a reader of the diagram.
    /// </summary>
    /// <summary>
    /// An interaction instant, as UTC, in the one format all three data writers share.
    /// </summary>
    /// <remarks>
    /// The trailing <c>Z</c> is a literal: uppercase <c>Z</c> is not a .NET format specifier (the offset
    /// specifiers are lowercase <c>z</c>/<c>zz</c>/<c>zzz</c>), so it is copied to the output and asserts
    /// UTC without doing anything to make it true. The conversion has to be explicit, which is why this
    /// exists once rather than at each writer: all three had the format string and none had the
    /// conversion, so a timestamp carrying a non-zero offset kept its local wall-clock reading and was
    /// then labelled UTC - off by exactly the offset. The run-level stamps in the same writers always
    /// converted first, which is what showed the omission was accidental.
    /// </remarks>
    private static string FormatInstant(DateTimeOffset instant) =>
        instant.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static object MapLogJson(RequestResponseLog log, IReadOnlyDictionary<Guid, double>? durations = null, string? stepPath = null) => new
    {
        Type = log.Type.ToString(),
        Method = log.Method.Value?.ToString()?.ToUpperInvariant(),
        Uri = log.Uri.ToString(),
        log.ServiceName,
        log.CallerName,
        log.Content,
        Headers = log.Headers.Select(h => new { h.Key, h.Value }).ToArray(),
        StatusCode = InteractionStatus.Split(log.StatusCode).Code,
        StatusText = InteractionStatus.Split(log.StatusCode).Text,
        TraceId = log.TraceId.ToString(),
        RequestResponseId = log.RequestResponseId.ToString(),
        Timestamp = log.Timestamp is { } jsonAt ? FormatInstant(jsonAt) : null,
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

    private static string GenerateTestRunReportXml(Feature[] features, DateTime startTime, DateTime endTime, ILookup<string, string>? diagramLookup, ILookup<string, RequestResponseLog>? logLookup, IReadOnlyList<DiagnosticEntry>? diagnostics = null, IReadOnlyDictionary<Guid, double>? durations = null, IReadOnlyDictionary<string, List<string?>>? stepPaths = null, IReadOnlyDictionary<string, List<ScenarioAnnotation>>? annotations = null, CiMetadata? ciMetadata = null, string? suite = null, RunEnvironment? environment = null)
    {
        var resolvedSuite = suite ?? RunSuite.Current;
        var doc = new XDocument(
            new XElement("TestRunReport",
                new XElement("FormatVersion", ReportFormatVersion),
                new XElement("KronikolVersion", KronikolVersion),
                resolvedSuite is { Length: > 0 } xmlSuite ? new XElement("Suite", xmlSuite) : null,
                new XElement("StartTime", startTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")),
                new XElement("EndTime", endTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")),
                MapCiMetadataXml(ciMetadata),
                ResolveEnvironment(environment) is { } xmlEnvironment
                    ? new XElement("Environment",
                        new XElement("Os", xmlEnvironment.Os),
                        new XElement("Runtime", xmlEnvironment.Runtime))
                    : null,
                new XElement("Features",
                    features.OrderBy(f => f.DisplayName).Select(f =>
                        new XElement("Feature",
                            new XElement("Name", f.DisplayName),
                            f.Endpoint != null ? new XElement("Endpoint", f.Endpoint) : null,
                            f.Description != null ? new XElement("Description", f.Description) : null,
                            f.SourceFile != null ? new XElement("SourceFile", f.SourceFile) : null,
                            (f.Labels is { Length: > 0 }) ? new XElement("Labels", f.Labels.Select(l => new XElement("Label", l))) : null,
                            new XElement("Scenarios",
                                f.Scenarios.Select(s =>
                                {
                                    var scenarioElements = new List<object?>
                                    {
                                        new XElement("Id", s.Id),
                                        new XElement("StableId", ScenarioStableId.Compute(resolvedSuite, f.DisplayName, s.DisplayName, s.OutlineId, s.ExampleValues)),
                                        new XElement("Name", s.DisplayName),
                                        s.Description != null ? new XElement("Description", s.Description) : null,
                                        new XElement("Result", s.Result.ToString()),
                                        new XElement("DurationSeconds", (s.Duration?.TotalSeconds ?? 0.0).ToString("F3")),
                                        new XElement("IsHappyPath", s.IsHappyPath.ToString().ToLower()),
                                        s.ErrorMessage != null ? new XElement("ErrorMessage", s.ErrorMessage) : null,
                                        s.ErrorStackTrace != null ? new XElement("ErrorStackTrace", s.ErrorStackTrace) : null,
                                        s.FailureCause != null ? new XElement("FailureCause", s.FailureCause) : null,
                                        (s.Labels is { Length: > 0 }) ? new XElement("Labels", s.Labels.Select(l => new XElement("Label", l))) : null,
                                        (s.Categories is { Length: > 0 }) ? new XElement("Categories", s.Categories.Select(c => new XElement("Category", c))) : null,
                                        s.Rule != null ? new XElement("Rule", s.Rule) : null,
                                        s.Attempt != null ? new XElement("Attempt", s.Attempt.Value.ToString(CultureInfo.InvariantCulture)) : null,
                                        s.SourceFile != null ? new XElement("SourceFile", s.SourceFile) : null,
                                        s.SourceLine != null ? new XElement("SourceLine", s.SourceLine.Value.ToString(CultureInfo.InvariantCulture)) : null,
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

                                        if (annotations is not null && annotations.TryGetValue(s.Id, out var scenarioAnnotations) && scenarioAnnotations.Count > 0)
                                            scenarioElements.Add(new XElement("Annotations", scenarioAnnotations.Select(a =>
                                                new XElement("Annotation",
                                                    new XElement("Index", a.Index.ToString(CultureInfo.InvariantCulture)),
                                                    new XElement("Kind", a.Kind.ToString()),
                                                    new XElement("Text", a.Text)))));
                                    }

                                    return new XElement("Scenario", scenarioElements.ToArray());
                                })
                            )
                        )
                    )
                ),
                MapDiagnosticsXml(diagnostics)
            )
        );
        return doc.ToString();
    }

    /// <summary>
    /// The <c>Diagnostics</c> element: the XML spelling of what <see cref="MapDiagnosticsJson"/> writes.
    /// </summary>
    /// <remarks>
    /// Written even when empty, because the alternative is that a reader cannot tell a clean run from a
    /// format that does not carry diagnostics - which is the state this element was added to end. It sits
    /// last, where the JSON has it, and the XSD's sequence says so too.
    /// </remarks>
    private static XElement MapDiagnosticsXml(IReadOnlyList<DiagnosticEntry>? diagnostics) =>
        new("Diagnostics", (diagnostics ?? []).Select(d =>
            new XElement("Diagnostic",
                new XElement("Kind", d.Kind.ToString()),
                new XElement("Message", d.Message),
                d.ScenarioId is { Length: > 0 } scenarioId ? new XElement("ScenarioId", scenarioId) : null)));

    /// <inheritdoc cref="MapLogJson"/>
    private static XElement MapLogXml(RequestResponseLog log, IReadOnlyDictionary<Guid, double>? durations = null, string? stepPath = null) =>
        new("HttpInteraction",
            new XElement("Type", log.Type.ToString()),
            // Omitted when there is none, like every other line here. It used to be written
            // unconditionally, so a bare event - which has no verb - produced <Method />: not "no
            // method" but the empty string, which is a different claim. XML has no way to tell those two
            // apart in element content, so an empty label is written as absent rather than as blank.
            log.Method.Value?.ToString()?.ToUpperInvariant() is { Length: > 0 } xmlMethod ? new XElement("Method", xmlMethod) : null,
            new XElement("Uri", log.Uri.ToString()),
            new XElement("ServiceName", log.ServiceName),
            new XElement("CallerName", log.CallerName),
            log.Content != null ? new XElement("Content", log.Content) : null,
            log.Headers.Length > 0 ? new XElement("Headers", log.Headers.Select(h => new XElement("Header",
                new XElement("Key", h.Key),
                // Same again: a header sent with no value at all read back identically to one sent empty.
                h.Value is { Length: > 0 } headerValue ? new XElement("Value", headerValue) : null))) : null,
            InteractionStatus.Split(log.StatusCode).Code is { } xmlStatusCode ? new XElement("StatusCode", xmlStatusCode) : null,
            InteractionStatus.Split(log.StatusCode).Text is { } xmlStatusText ? new XElement("StatusText", xmlStatusText) : null,
            new XElement("TraceId", log.TraceId.ToString()),
            new XElement("RequestResponseId", log.RequestResponseId.ToString()),
            log.Timestamp is { } xmlAt ? new XElement("Timestamp", FormatInstant(xmlAt)) : null,
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

    /// <summary>
    /// The <c>&lt;CiMetadata&gt;</c> block. The element itself is always written - <c>Provider</c> alone
    /// says whether there was a CI to read - but its empty children are omitted, which is this writer's
    /// convention throughout (the JSON keeps every key, and is the shape a consumer keys on).
    /// </summary>
    private static XElement MapCiMetadataXml(CiMetadata? ciMetadata) =>
        new("CiMetadata",
            new XElement("Provider", (ciMetadata?.Provider ?? CiEnvironment.None).ToString()),
            ciMetadata?.BuildNumber != null ? new XElement("BuildNumber", ciMetadata.BuildNumber) : null,
            ciMetadata?.Branch != null ? new XElement("Branch", ciMetadata.Branch) : null,
            ciMetadata?.CommitSha != null ? new XElement("CommitSha", ciMetadata.CommitSha) : null,
            ciMetadata?.PipelineUrl != null ? new XElement("PipelineUrl", ciMetadata.PipelineUrl) : null,
            ciMetadata?.Repository != null ? new XElement("Repository", ciMetadata.Repository) : null,
            ciMetadata?.RunId != null ? new XElement("RunId", ciMetadata.RunId) : null,
            ciMetadata?.RunAttempt != null ? new XElement("RunAttempt", ciMetadata.RunAttempt) : null
        );

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

    /// <summary>A YAML line written only when its value is there - the writer omits, it does not blank.</summary>
    private static void AppendYamlIfPresent(StringBuilder yml, string prefix, string? value)
    {
        if (value is not null) AppendYaml(yml, prefix, value);
    }

    /// <summary>
    /// Writes <c>{prefix}{value}</c> and a newline, with the value in whichever YAML form reads back as
    /// exactly the string given.
    /// </summary>
    /// <remarks>
    /// A block scalar's lines are written at <paramref name="prefix"/>'s own width, which is past the end
    /// of the key and therefore deeper than the mapping holding it - the indentation is what makes those
    /// lines part of this value rather than the start of the next key.
    /// </remarks>
    private static void AppendYaml(StringBuilder yml, string prefix, string value)
    {
        yml.Append(prefix).Append(value.ToYamlScalar(new string(' ', BlockIndentFor(prefix)))).Append('\n');
    }

    /// <summary>
    /// The column a block scalar written after <paramref name="prefix"/> puts its lines at.
    /// </summary>
    /// <remarks>
    /// A block scalar has to be indented past its parent node, and the parent is whatever the prefix
    /// opened last: for <c>"        ErrorMessage: "</c> that is the mapping at column 8, and for
    /// <c>"          - Kind: "</c> it is the mapping inside the sequence entry, at column 12. So the
    /// measure is where the key begins - after the indentation and after any <c>- </c> indicators - plus
    /// two. Using the prefix's own length instead is always safe but indents a two-line message twenty
    /// columns in, which defeats the point of choosing YAML.
    /// </remarks>
    private static int BlockIndentFor(string prefix)
    {
        var at = 0;
        while (at < prefix.Length && prefix[at] == ' ') at++;
        while (at + 1 < prefix.Length && prefix[at] == '-' && prefix[at + 1] == ' ') at += 2;
        return at + 2;
    }

    /// <summary>
    /// The same, for a value that may be absent. An absent one is written as an empty scalar, which reads
    /// back as null - matching the JSON writer, which emits the key with a null rather than dropping it.
    /// </summary>
    private static void AppendYamlNullable(StringBuilder yml, string prefix, string? value)
    {
        if (value is null)
            yml.Append(prefix.TrimEnd()).Append('\n');
        else
            AppendYaml(yml, prefix, value);
    }

    /// <summary>
    /// A YAML sequence of strings, written even when it is empty.
    /// </summary>
    /// <remarks>
    /// An empty block sequence cannot be expressed - there is nothing to indent - so an empty one is
    /// written in flow form as <c>[]</c>. It has to be written at all because the schema marks these
    /// keys required, and it is required because the JSON writer emits them unconditionally: the two
    /// files are the same report, and a consumer should not have to ask which format it is holding
    /// before it knows whether a missing `labels` means "none" or "not recorded".
    /// </remarks>
    private static void AppendYamlSequence(StringBuilder yml, string indent, string key, IEnumerable<string>? values)
    {
        var items = values?.ToArray() ?? [];
        if (items.Length == 0)
        {
            yml.Append(indent).Append(key).Append(": []\n");
            return;
        }

        yml.Append(indent).Append(key).Append(":\n");
        foreach (var item in items)
            AppendYaml(yml, indent + "  - ", item);
    }

    private static string GenerateTestRunReportYaml(Feature[] features, DateTime startTime, DateTime endTime, ILookup<string, string>? diagramLookup, ILookup<string, RequestResponseLog>? logLookup, IReadOnlyList<DiagnosticEntry>? diagnostics = null, IReadOnlyDictionary<Guid, double>? durations = null, IReadOnlyDictionary<string, List<string?>>? stepPaths = null, IReadOnlyDictionary<string, List<ScenarioAnnotation>>? annotations = null, CiMetadata? ciMetadata = null, string? suite = null, RunEnvironment? environment = null)
    {
        var resolvedSuite = suite ?? RunSuite.Current;
        var yml = new StringBuilder();
        yml.Append("FormatVersion: " + ReportFormatVersion + "\n");
        AppendYaml(yml, "KronikolVersion: ", KronikolVersion);
        AppendYamlIfPresent(yml, "Suite: ", resolvedSuite);
        // Quoted, by the emitter: an unquoted 2026-01-01T10:00:00Z is a timestamp to a YAML 1.1
        // parser, and the schema says these two are strings.
        AppendYaml(yml, "StartTime: ", startTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"));
        AppendYaml(yml, "EndTime: ", endTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"));
        yml.Append("CiMetadata:\n");
        AppendYaml(yml, "  Provider: ", (ciMetadata?.Provider ?? CiEnvironment.None).ToString());
        AppendYamlIfPresent(yml, "  BuildNumber: ", ciMetadata?.BuildNumber);
        AppendYamlIfPresent(yml, "  Branch: ", ciMetadata?.Branch);
        AppendYamlIfPresent(yml, "  CommitSha: ", ciMetadata?.CommitSha);
        AppendYamlIfPresent(yml, "  PipelineUrl: ", ciMetadata?.PipelineUrl);
        AppendYamlIfPresent(yml, "  Repository: ", ciMetadata?.Repository);
        AppendYamlIfPresent(yml, "  RunId: ", ciMetadata?.RunId);
        AppendYamlIfPresent(yml, "  RunAttempt: ", ciMetadata?.RunAttempt);
        if (ResolveEnvironment(environment) is { } ymlEnvironment)
        {
            yml.Append("Environment:\n");
            AppendYaml(yml, "  Os: ", ymlEnvironment.Os);
            AppendYaml(yml, "  Runtime: ", ymlEnvironment.Runtime);
        }
        yml.Append("Features:\n");

        foreach (var feature in features.OrderBy(f => f.DisplayName))
        {
            AppendYaml(yml, "  - Name: ", feature.DisplayName);

            if (feature.Endpoint is not null)
                AppendYaml(yml, "    Endpoint: ", feature.Endpoint);

            if (feature.Description is not null)
                AppendYaml(yml, "    Description: ", feature.Description);

            if (feature.SourceFile is not null)
                AppendYaml(yml, "    SourceFile: ", feature.SourceFile);

            AppendYamlSequence(yml, "    ", "Labels", feature.Labels);

            yml.Append("    Scenarios:\n");
            foreach (var scenario in feature.Scenarios)
            {
                AppendYaml(yml, "      - Id: ", scenario.Id);
                AppendYaml(yml, "        Name: ", scenario.DisplayName);
                AppendYaml(yml, "        StableId: ", ScenarioStableId.Compute(resolvedSuite, feature.DisplayName, scenario.DisplayName, scenario.OutlineId, scenario.ExampleValues));
                if (scenario.Description is not null)
                    AppendYaml(yml, "        Description: ", scenario.Description);
                if (scenario.Attempt is not null)
                    yml.Append("        Attempt: " + scenario.Attempt.Value.ToString(CultureInfo.InvariantCulture) + "\n");
                if (scenario.SourceFile is not null)
                    AppendYaml(yml, "        SourceFile: ", scenario.SourceFile);
                if (scenario.SourceLine is not null)
                    yml.Append("        SourceLine: " + scenario.SourceLine.Value.ToString(CultureInfo.InvariantCulture) + "\n");
                AppendYaml(yml, "        Result: ", scenario.Result.ToString());
                yml.Append("        DurationSeconds: " + (scenario.Duration?.TotalSeconds ?? 0.0).ToString("F3") + "\n");
                yml.Append("        IsHappyPath: " + scenario.IsHappyPath.ToString().ToLower() + "\n");

                if (scenario.ErrorMessage is not null)
                    AppendYaml(yml, "        ErrorMessage: ", scenario.ErrorMessage);

                if (scenario.ErrorStackTrace is not null)
                    AppendYaml(yml, "        ErrorStackTrace: ", scenario.ErrorStackTrace);

                if (scenario.FailureCause is not null)
                    AppendYaml(yml, "        FailureCause: ", scenario.FailureCause);

                AppendYamlSequence(yml, "        ", "Labels", scenario.Labels);
                AppendYamlSequence(yml, "        ", "Categories", scenario.Categories);

                if (scenario.Rule is not null)
                    AppendYaml(yml, "        Rule: ", scenario.Rule);

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
                else
                {
                    yml.Append("        Steps: []\n");
                }

                if (scenario.Attachments is { Length: > 0 })
                {
                    yml.Append("        Attachments:\n");
                    foreach (var att in scenario.Attachments)
                    {
                        AppendYaml(yml, "          - Name: ", att.Name);
                        AppendYaml(yml, "            RelativePath: ", att.RelativePath);
                        if (att.MediaType is not null)
                            AppendYaml(yml, "            MediaType: ", att.MediaType);
                    }
                }

                if (diagramLookup != null)
                {
                    var diags = diagramLookup[scenario.Id].ToArray();
                    if (diags.Length > 0)
                    {
                        yml.Append("        Diagrams:\n");
                        foreach (var diag in diags)
                            AppendYaml(yml, "          - ", diag);
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

                    if (annotations is not null && annotations.TryGetValue(scenario.Id, out var scenarioAnnotations) && scenarioAnnotations.Count > 0)
                    {
                        yml.Append("        Annotations:\n");
                        foreach (var annotation in scenarioAnnotations)
                        {
                            yml.Append("          - Index: " + annotation.Index.ToString(CultureInfo.InvariantCulture) + "\n");
                            AppendYaml(yml, "            Kind: ", annotation.Kind.ToString());
                            AppendYaml(yml, "            Text: ", annotation.Text);
                        }
                    }
                }
            }
        }

        // Last, where the JSON has it, and written even when empty - a reader has to be able to tell a
        // run with nothing to report from a format that could not have told them either way.
        yml.Append("Diagnostics:");
        if (diagnostics is not { Count: > 0 })
        {
            yml.Append(" []\n");
        }
        else
        {
            yml.Append('\n');
            foreach (var entry in diagnostics)
            {
                AppendYaml(yml, "  - Kind: ", entry.Kind.ToString());
                AppendYaml(yml, "    Message: ", entry.Message);
                if (entry.ScenarioId is { Length: > 0 } scenarioId)
                    AppendYaml(yml, "    ScenarioId: ", scenarioId);
            }
        }

        return yml.ToString();
    }

    private static void AppendTestRunYamlStep(StringBuilder yml, ScenarioStep step, string indent)
    {
        AppendYaml(yml, indent + "- Keyword: ", (step.Keyword ?? ""));
        AppendYaml(yml, indent + "  Text: ", step.Text);
        AppendYamlNullable(yml, indent + "  Status: ", step.Status?.ToString());
        if (step.Duration != null)
            yml.Append(indent + "  DurationSeconds: " + step.Duration.Value.TotalSeconds.ToString("F3") + "\n");
        if (step.FailureMessage != null)
            AppendYaml(yml, indent + "  FailureMessage: ", step.FailureMessage);
        if (step.SourceFile != null)
            AppendYaml(yml, indent + "  SourceFile: ", step.SourceFile);
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
                AppendYaml(yml, indent + "    - Name: ", att.Name);
                AppendYaml(yml, indent + "      RelativePath: ", att.RelativePath);
                if (att.MediaType is not null)
                    AppendYaml(yml, indent + "      MediaType: ", att.MediaType);
            }
        }
    }

    /// <inheritdoc cref="MapLogJson"/>
    private static void AppendTestRunYamlLog(StringBuilder yml, RequestResponseLog log, string indent, IReadOnlyDictionary<Guid, double>? durations = null, string? stepPath = null)
    {
        AppendYaml(yml, indent + "- Type: ", log.Type.ToString());
        AppendYamlNullable(yml, indent + "  Method: ", log.Method.Value?.ToString()?.ToUpperInvariant());
        AppendYaml(yml, indent + "  Uri: ", log.Uri?.ToString() ?? "");
        AppendYaml(yml, indent + "  ServiceName: ", log.ServiceName);
        AppendYaml(yml, indent + "  CallerName: ", log.CallerName);
        if (log.Content is not null)
            AppendYaml(yml, indent + "  Content: ", log.Content);
        var (ymlStatusCode, ymlStatusText) = InteractionStatus.Split(log.StatusCode);
        if (ymlStatusCode is not null)
            yml.Append(indent + "  StatusCode: " + ymlStatusCode.Value.ToString(CultureInfo.InvariantCulture) + "\n");
        if (ymlStatusText is not null)
            AppendYaml(yml, indent + "  StatusText: ", ymlStatusText);
        AppendYaml(yml, indent + "  TraceId: ", log.TraceId.ToString());
        AppendYaml(yml, indent + "  RequestResponseId: ", log.RequestResponseId.ToString());
        if (log.Timestamp is not null)
            AppendYaml(yml, indent + "  Timestamp: ", FormatInstant(log.Timestamp.Value));
        if (log.MetaType != RequestResponseMetaType.Default)
            AppendYaml(yml, indent + "  MetaType: ", log.MetaType.ToString());
        if (log.DependencyCategory is not null)
            AppendYaml(yml, indent + "  DependencyCategory: ", log.DependencyCategory);
        if (log.CallerDependencyCategory is not null)
            AppendYaml(yml, indent + "  CallerDependencyCategory: ", log.CallerDependencyCategory);
        if (log.Phase != TestPhase.Unknown)
            AppendYaml(yml, indent + "  Phase: ", log.Phase.ToString());
        if (log.IsUserAction)
            yml.Append(indent + "  IsUserAction: true\n");
        if (log.ActivityTraceId is not null)
            AppendYaml(yml, indent + "  ActivityTraceId: ", log.ActivityTraceId);
        if (log.ActivitySpanId is not null)
            AppendYaml(yml, indent + "  ActivitySpanId: ", log.ActivitySpanId);
        if (log.CapturedBy is not null)
            AppendYaml(yml, indent + "  CapturedBy: ", log.CapturedBy);
        if (durations is not null && durations.TryGetValue(log.RequestResponseId, out var ms))
            yml.Append(indent + "  DurationMs: " + ms.ToString("F3", CultureInfo.InvariantCulture) + "\n");
        if (stepPath is not null)
            AppendYaml(yml, indent + "  StepPath: ", stepPath);
        if (log.Headers.Length > 0)
        {
            yml.Append(indent + "  Headers:\n");
            foreach (var h in log.Headers)
            {
                AppendYaml(yml, indent + "    - Key: ", h.Key);
                // A header with no value is null here, as it is in the JSON. It used to be coalesced to
                // "" first, which made a header that was sent empty and one that carried no value at all
                // read back as the same thing.
                AppendYamlNullable(yml, indent + "      Value: ", h.Value);
            }
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
            AppendYaml(yml, "  - Feature: ", feature.DisplayName);

            if (feature.Endpoint is not null)
                yml.Append("    Endpoint: " + feature.Endpoint + "\n");

            if (feature.Description is not null)
                AppendYaml(yml, "    Description: ", feature.Description);

            if (feature.Labels is { Length: > 0 })
            {
                yml.Append("    Labels:\n");
                foreach (var label in feature.Labels)
                    AppendYaml(yml, "      - ", label);
            }

            yml.Append("    Scenarios:\n");

            var orderedScenarios = feature.Scenarios.OrderByDescending(x => x.IsHappyPath).ThenBy(x => x.DisplayName);
            foreach (var scenario in orderedScenarios)
            {
                AppendYaml(yml, "      - Scenario: ", scenario.DisplayName);
                yml.Append("        IsHappyPath: " + scenario.IsHappyPath.ToString().ToLower() + "\n");

                if (scenario.Labels is { Length: > 0 })
                {
                    yml.Append("        Labels:\n");
                    foreach (var label in scenario.Labels)
                        AppendYaml(yml, "          - ", label);
                }

                if (scenario.Categories is { Length: > 0 })
                {
                    yml.Append("        Categories:\n");
                    foreach (var cat in scenario.Categories)
                        AppendYaml(yml, "          - ", cat);
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

    /// <summary>
    /// Writes the agent block into <paramref name="fileName"/> without destroying anything else in it.
    ///
    /// <para>These two names are not Kronikol's alone. <c>kronikol init-agents</c> writes the same block
    /// into a file a human owns and has always spliced; this used to overwrite. Where a reports folder and
    /// an instruction file share a directory, a run silently deleted somebody's standing instructions —
    /// and the body it left behind had no markers, so a later <c>init-agents</c> appended to it instead of
    /// replacing it. One protocol now, in <see cref="AgentInstructionsBlock"/>, used by both.</para>
    ///
    /// <para>A file the protocol refuses (a block opened and never closed, or two blocks) is left exactly
    /// as it is and recorded as a diagnostic. Every repair for those is a guess, and a wrong guess deletes
    /// text nobody can get back.</para>
    /// </summary>
    private static void WriteAgentInstructionsFile(string block, string fileName)
    {
        var path = Path.Combine(CurrentReportsDirectory, fileName);

        string? existing = null;
        if (File.Exists(path))
        {
            try
            {
                existing = File.ReadAllText(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Fall through to the plain write, which reports its own failure.
            }
        }

        if (existing is null)
        {
            WriteFile(block + "\n", fileName);
            return;
        }

        if (AgentInstructionsBlock.Merge(existing, block, out var problem) is { } merged)
        {
            WriteFile(merged, fileName);
            return;
        }

        ReportDiagnosticsScope.Record(DiagnosticKind.OutputFailure, $"Left {fileName} alone: it {problem}");
        Console.WriteLine($"⚠ WARNING: left {fileName} alone — it {problem}");
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
        catch (IOException exception)
        {
            // The salvage: the canonical path is unusable — held open by a reader, a viewer, a previous
            // process — so the bytes go somewhere rather than nowhere. But this used to return as though
            // it had succeeded, which is the worse half: the run was never marked, and the canonical name
            // on disk still held the PREVIOUS run's bytes, under the name the pointer prints, the CI
            // summary offers and `kronikol query` opens. Writing the salvage and then rethrowing is the
            // accurate report of what happened — this output did not produce the file it was asked for.
            var fallback = Path.Combine(directory,
                Path.GetFileNameWithoutExtension(fileName) + "2" + Path.GetExtension(fileName));
            try
            {
                File.WriteAllText(fallback, text);
                Console.WriteLine($"⚠ WARNING: {fileName} was not writable — this run's copy is in {Path.GetFileName(fallback)}; "
                                  + $"{fileName} on disk is from an earlier run.");
            }
            catch (Exception salvageFailure) when (salvageFailure is IOException or UnauthorizedAccessException)
            {
                // Nothing more to try. The rethrow below is what marks the run.
            }

            throw new IOException($"Could not write {fileName}: {exception.Message}", exception);
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
        DataFormat.Yaml => "json", // YAML is described by JSON Schema, as it is everywhere else
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    public static string GenerateTestRunReportSchema(string fileName, DataFormat format)
    {
        return format switch
        {
            DataFormat.Json => WriteFile(GenerateTestRunReportJsonSchema(), fileName),
            DataFormat.Xml => WriteFile(GenerateTestRunReportXmlSchema(), fileName),
            // The same document, with the property names the YAML writer uses. Handing YAML the JSON
            // one described a file nobody writes: every name differed, and because a schema with no
            // additionalProperties permits whatever it has not heard of, that showed up as four missing
            // required keys rather than as "none of this matches".
            DataFormat.Yaml => WriteFile(GenerateTestRunReportJsonSchema(pascalCasePropertyNames: true), fileName),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
    }

    /// <summary>
    /// The schema is the field-level contract of <c>TestRunReport.json</c> — what an agent reads instead of
    /// the file. Every property the writer emits is declared here and every declaration carries a
    /// description; <c>TestRunReportSchemaContractTests</c> walks a generated report against it and fails on
    /// the first undeclared key or undescribed property, so a new field cannot land in the writer alone.
    /// </summary>
    /// <remarks>
    /// <c>pascalCasePropertyNames</c> renames the declared properties to the casing the YAML writer
    /// uses. The schema is one contract written once; only the spelling of the names differs between the
    /// formats that carry it.
    /// </remarks>
    private static string GenerateTestRunReportJsonSchema(bool pascalCasePropertyNames = false)
    {
        var resultEnumValues = Enum.GetNames(typeof(ExecutionResult));
        // A step's status may be absent, and `enum` is type-blind: it is asserted against every instance,
        // including null, so widening `type` to ["string","null"] is not enough on its own. The null has to
        // be a member of the enum as well, or a step the producer left unjudged fails its own schema.
        object?[] statusEnumValues = [.. resultEnumValues, null];

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

        static Dictionary<string, object?> Attachment(string description) => new()
        {
            ["type"] = "array",
            ["description"] = description,
            ["items"] = new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["name"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Display name, normally the file name" },
                    ["relativePath"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Path relative to the report directory (attachments/<file>), or an absolute URL when the attachment is a link; join it to the report's own directory to open the file" },
                    ["mediaType"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "IANA media type; image/* renders inline, anything else as a link" }
                }
            }
        };

        var schema = new Dictionary<string, object?>
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$comment"] = "A real TestRunReport.json runs to megabytes, with single embedded diagrams past 600 KB, so do not read it whole: `kronikol query <command> <report>` (dotnet tool install -g Kronikol.Tool) answers questions about it under a byte budget — summary, failures, steps sN, services, flow sN, http sN/iN. This schema is the field-level contract of that file.",
            ["title"] = "TestRunReport",
            ["description"] = "Schema for Kronikol test run report data",
            ["type"] = "object",
            // formatVersion is required because a reader that cannot find it is reading a file written
            // before the contract was versioned, and should say so rather than guess.
            ["required"] = new[] { "formatVersion", "startTime", "endTime", "features" },
            ["properties"] = new Dictionary<string, object?>
            {
                ["formatVersion"] = new Dictionary<string, object?> { ["type"] = "integer", ["description"] = "Version of the report SHAPE, as distinct from the Kronikol build that wrote it. Bumped when a key changes meaning or type, not when one is added." },
                ["kronikolVersion"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Version of Kronikol that generated this report" },
                ["suite"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "The test suite this run belongs to. Every stableId in the file is scoped to it, which is what stops two suites that name a feature and a scenario the same way from minting the same id. Null when it could not be resolved, in which case ids are unscoped." },
                ["startTime"] = new Dictionary<string, object?> { ["type"] = "string", ["format"] = "date-time", ["description"] = "UTC start time of the test run" },
                ["endTime"] = new Dictionary<string, object?> { ["type"] = "string", ["format"] = "date-time", ["description"] = "UTC end time of the test run" },
                ["ciMetadata"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["description"] = "Which run this file is: the CI provider and the build it came from. Always present with the same eight keys; off CI provider is None and the rest are null. This is what a baseline index keys on - commitSha identifies the code, runId the run, runAttempt which try of it.",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["provider"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = Enum.GetNames(typeof(CiEnvironment)), ["description"] = "The CI system detected from the environment, or None when the run was not on CI" },
                        ["buildNumber"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "The provider's human-facing build number (GITHUB_RUN_NUMBER, BUILD_BUILDNUMBER)" },
                        ["branch"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "The branch or ref the run was triggered on" },
                        ["commitSha"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "The full commit SHA the run built", ["examples"] = new[] { "9f3c1b2a4d5e6f708192a3b4c5d6e7f809a1b2c3" } },
                        ["pipelineUrl"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "Link back to the run in the provider's UI, when enough of the environment was present to build one" },
                        ["repository"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "Repository the run belongs to (org/repo on GitHub)" },
                        ["runId"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "The provider's own identifier for the run - what an artifact download is addressed by" },
                        ["runAttempt"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "Which try of that run this is, counting from 1 (GITHUB_RUN_ATTEMPT). The run id is unchanged by a re-run, so this is the only thing that tells a retry apart from the run it retried. Null off GitHub Actions." }
                    }
                },
                ["environment"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["description"] = "What the run executed on. Two fields and no more: a report is an artifact other people download, so no machine name, user name or environment dump.",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["os"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Operating system description, as the runtime reports it - a description, not a parseable identifier", ["examples"] = new[] { "Microsoft Windows 10.0.26200" } },
                        ["runtime"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "The .NET runtime the tests executed on", ["examples"] = new[] { ".NET 10.0.0" } }
                    }
                },
                ["diagnostics"] = new Dictionary<string, object?>
                {
                    ["type"] = "array",
                    ["description"] = "Everything worth knowing about how this report was produced: capture health handed in by the host (CaptureDegraded), skipped malformed lines, diagrams that could not be rendered, labels that still do not read as sentences. Empty is the happy path.",
                    ["items"] = new Dictionary<string, object?> { ["$ref"] = "#/$defs/diagnostic" }
                },
                // The mergeable superset. It is written under the SAME name as the standard file, with
                // this same schema beside it, so a schema that closed its root without declaring these
                // would make a merged report fail the contract shipped next to it. Optional, because the
                // standard file - the common case - has none of them.
                ["mergeableFormatVersion"] = new Dictionary<string, object?> { ["type"] = "integer", ["description"] = "Present only on a report written with GenerateMergeableData. Versions the mergeable superset, separately from formatVersion, which versions the shape both files share." },
                ["wholeTestVisualization"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = Enum.GetNames(typeof(WholeTestFlowVisualization)), ["description"] = "Mergeable only: which whole-test-flow rendering the run produced, so a merge rebuilds the same one" },
                ["componentRelationships"] = new Dictionary<string, object?>
                {
                    ["type"] = "array",
                    ["description"] = "Mergeable only: the caller-to-service edges the run observed, which is what the component diagram is drawn from",
                    ["items"] = new Dictionary<string, object?>
                    {
                        ["type"] = "object",
                        ["required"] = new[] { "caller", "service", "protocol", "methods", "callCount", "testCount" },
                        ["properties"] = new Dictionary<string, object?>
                        {
                            ["caller"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "The component that made the calls" },
                            ["service"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "The component that received them" },
                            ["protocol"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "How they talked (HTTP, SQL, a message bus)" },
                            ["methods"] = new Dictionary<string, object?> { ["type"] = "array", ["description"] = "The distinct operation labels seen on this edge, sorted", ["items"] = new Dictionary<string, object?> { ["type"] = "string" } },
                            ["callCount"] = new Dictionary<string, object?> { ["type"] = "integer", ["description"] = "How many calls crossed this edge" },
                            ["testCount"] = new Dictionary<string, object?> { ["type"] = "integer", ["description"] = "How many scenarios used it" },
                            ["dependencyCategory"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "The category the service was classified as, when one was resolved" }
                        },
                        ["additionalProperties"] = false
                    }
                },
                // Deliberately open. A merge re-serialises these payloads verbatim from the shard files
                // it read, which a different Kronikol version may have written, so pinning what is inside
                // them would make `kronikol merge` emit a file that fails its own schema with no code
                // change on either side.
                ["internalFlowSegments"] = new Dictionary<string, object?> { ["type"] = "object", ["description"] = "Mergeable only: precomputed internal-flow payloads keyed by segment id. The values are rendering data carried through a merge unchanged, and are deliberately not described here - a merged file may hold shapes written by another version." },
                ["wholeTestFlow"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["description"] = "Mergeable only: per-scenario whole-test-flow fragments, keyed by scenario id",
                    ["additionalProperties"] = new Dictionary<string, object?> { ["$ref"] = "#/$defs/wholeTestFlowFragment" }
                },
                ["features"] = new Dictionary<string, object?>
                {
                    ["type"] = "array",
                    ["description"] = "One entry per feature (a test class, a Gherkin Feature:), ordered by name. Scenario ordinals in kronikol query (s0, s1, ...) count through this list in order.",
                    ["items"] = new Dictionary<string, object?>
                    {
                        ["type"] = "object",
                        ["required"] = new[] { "name", "labels", "scenarios" },
                        ["properties"] = new Dictionary<string, object?>
                        {
                            ["name"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Feature display name: the test class, or the title after Feature: in Gherkin" },
                            ["endpoint"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "The endpoint or component the feature covers, when declared (an @endpoint: tag, or the adapter's attribute)" },
                            ["description"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "Free text under the Feature: line, dedented" },
                            ["sourceFile"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "Where the feature is written: a project-relative path with forward slashes, from the Gherkin document. Null on the lanes that cannot supply one (unit-test adapters, the tests NDJSON). Features are grouped by display name, so two files sharing a Feature: title collapse into one entry and the first path seen wins.", ["examples"] = new[] { "Features/Cake.feature" } },
                            ["labels"] = new Dictionary<string, object?> { ["type"] = "array", ["description"] = "Feature-level tags (in Gherkin the feature's own tags; otherwise the labels every scenario shares)", ["items"] = new Dictionary<string, object?> { ["type"] = "string" } },
                            ["scenarios"] = new Dictionary<string, object?>
                            {
                                ["type"] = "array",
                                ["description"] = "The feature's scenarios in run order",
                                ["items"] = new Dictionary<string, object?>
                                {
                                    ["type"] = "object",
                                    ["required"] = new[] { "id", "stableId", "name", "result", "durationSeconds", "isHappyPath", "labels", "categories", "steps" },
                                    ["properties"] = new Dictionary<string, object?>
                                    {
                                        ["id"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "The runtime id the test framework gave this scenario (a test case id, a pickle id). Unique within the run but not stable across runs; use stableId for that." },
                                        ["stableId"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Deterministic cross-run identifier derived from feature name + scenario display name (+ outline ID and ordered example values for parameterized scenarios). Use this for matching the same test across runs. Not unique: repeated rows and retries share one.", ["examples"] = new[] { "a1b2c3d4e5f60718" } },
                                        ["name"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Scenario display name; an outline row shows its expanded name" },
                                        ["description"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "The scenario's own free-text description (the prose under Scenario:)" },
                                        ["result"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = resultEnumValues, ["description"] = "The scenario's verdict" },
                                        ["durationSeconds"] = new Dictionary<string, object?> { ["type"] = "number", ["description"] = "Wall-clock seconds the scenario took; 0 when unknown" },
                                        ["isHappyPath"] = new Dictionary<string, object?> { ["type"] = "boolean", ["description"] = "Marked as the happy path (an @happy-path tag or the adapter's attribute); the report lists happy paths first" },
                                        ["errorMessage"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "The failure message the framework reported, when the scenario failed" },
                                        ["errorStackTrace"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "The stack trace the framework reported, when the scenario failed" },
                                        ["failureCause"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "The framework's CLASSIFICATION of the failure (xUnit v3: Assertion, Exception, Timeout, Other; MSTest: the exception type name), null where the framework does not classify. A category, not a cause - it says an assertion failed, never which one - so it is never a grouping key" },
                                        ["labels"] = new Dictionary<string, object?> { ["type"] = "array", ["description"] = "Scenario-level tags (feature tags are on the feature)", ["items"] = new Dictionary<string, object?> { ["type"] = "string" } },
                                        ["categories"] = new Dictionary<string, object?> { ["type"] = "array", ["description"] = "Category tags (@category: in Gherkin, the framework's category attribute otherwise); the report's category filter reads these", ["items"] = new Dictionary<string, object?> { ["type"] = "string" } },
                                        ["rule"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "Gherkin Rule grouping this scenario belongs to" },
                                        ["attempt"] = new Dictionary<string, object?> { ["type"] = new[] { "integer", "null" }, ["description"] = "Which run of this scenario produced the result, when the runner retries: 1 for the first, 2 for the first retry. 1-based, matching the retry N label in the HTML (Cucumber's own wire value is 0-based). Null where the runner reports nothing about attempts." },
                                        ["sourceFile"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "Where the scenario is written, matching the feature's sourceFile. NOT the same contract as a step's sourceFile, which is a bare file name from [CallerFilePath] on the build machine.", ["examples"] = new[] { "Features/Cake.feature" } },
                                        ["sourceLine"] = new Dictionary<string, object?> { ["type"] = new[] { "integer", "null" }, ["description"] = "The line the Scenario: or Scenario Outline: keyword is on - the declaration, not the Examples: row, so every row of an outline points at the same line. exampleValues is what says which row." },
                                        ["outlineId"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "Original scenario outline name for parameterized scenarios" },
                                        ["examplesBlockName"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "Name of the Examples: block this outline row came from" },
                                        ["examplesBlockDescription"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "Free-text description under the Examples: header" },
                                        ["examplesBlockIndex"] = new Dictionary<string, object?> { ["type"] = new[] { "integer", "null" }, ["description"] = "0-based position of the Examples: block within the outline" },
                                        ["exampleValues"] = new Dictionary<string, object?> { ["type"] = new[] { "object", "null" }, ["description"] = "Example parameter values for parameterized scenarios", ["additionalProperties"] = new Dictionary<string, object?> { ["type"] = "string" } },
                                        ["exampleFlatValues"] = new Dictionary<string, object?> { ["type"] = new[] { "object", "null" }, ["description"] = "exampleValues flattened to one level (nested objects become dotted keys): the columns of the report's parameterized table, and what a merged report groups on", ["additionalProperties"] = new Dictionary<string, object?> { ["type"] = "string" } },
                                        ["exampleDisplayName"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "The display name of this example row when the producer gave one that differs from name" },
                                        ["backgroundSteps"] = new Dictionary<string, object?>
                                        {
                                            ["type"] = "array",
                                            ["description"] = "Steps from the Gherkin Background:, addressed b0, b1, ... by stepPath",
                                            ["items"] = new Dictionary<string, object?> { ["$ref"] = "#/$defs/step" }
                                        },
                                        ["steps"] = new Dictionary<string, object?>
                                        {
                                            ["type"] = "array",
                                            ["description"] = "The scenario's own steps in order, addressed 0, 1, ... by stepPath; tracked assertions are keyword-less sub-steps",
                                            ["items"] = new Dictionary<string, object?> { ["$ref"] = "#/$defs/step" }
                                        },
                                        ["attachments"] = Attachment("Scenario-level file attachments (added when no step was active)"),
                                        ["diagrams"] = new Dictionary<string, object?>
                                        {
                                            ["type"] = "array",
                                            ["description"] = "Raw PlantUML source of each sequence diagram rendered for the scenario: hundreds of kilobytes each, never needed to answer a question (kronikol query flow sN tells the same story in a couple of KB)",
                                            ["items"] = new Dictionary<string, object?> { ["type"] = "string" }
                                        },
                                        ["httpInteractions"] = new Dictionary<string, object?>
                                        {
                                            ["type"] = "array",
                                            ["description"] = "Every captured call in capture order, both halves of each request/response pair: the bulk of the file. kronikol query addresses one as sN/iM where M is its index here.",
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
                                                    ["kind"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = new[] { "Row", "Custom" }, ["description"] = "Row: which row of a tabular input the following calls belong to; Custom: a fragment the test author injected" },
                                                    ["text"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "The marker's text as rendered in the diagram" }
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
                ["wholeTestFlowFragment"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["description"] = "One scenario's precomputed whole-test-flow rendering, inlined so a merge needs no shared diagram-data map",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["activityHtml"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "The activity view, as HTML" },
                        ["flameHtml"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "The flame view, as HTML" },
                        ["spanCount"] = new Dictionary<string, object?> { ["type"] = "integer", ["description"] = "Spans in the fragment, which is what the median-span threshold is measured against" }
                    },
                    ["additionalProperties"] = false
                },
                ["stepParameter"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["description"] = "One input to a step. Exactly one of inlineValue, tabularValue and treeValue is set; which one is what `kind` names.",
                    ["required"] = new[] { "name", "kind" },
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["name"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "The parameter's name, as the step or the example column named it" },
                        ["kind"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = Enum.GetNames(typeof(StepParameterKind)), ["description"] = "Which of the three value shapes this parameter carries" },
                        ["inlineValue"] = new Dictionary<string, object?> { ["$ref"] = "#/$defs/comparedValue" },
                        ["tabularValue"] = new Dictionary<string, object?>
                        {
                            ["type"] = new[] { "object", "null" },
                            ["description"] = "A data table: its columns, then its rows of cells",
                            ["required"] = new[] { "columns", "rows" },
                            ["properties"] = new Dictionary<string, object?>
                            {
                                ["columns"] = new Dictionary<string, object?>
                                {
                                    ["type"] = "array",
                                    ["description"] = "The table's columns in order",
                                    ["items"] = new Dictionary<string, object?>
                                    {
                                        ["type"] = "object",
                                        ["required"] = new[] { "name" },
                                        ["properties"] = new Dictionary<string, object?>
                                        {
                                            ["name"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "Column heading" },
                                            ["isKey"] = new Dictionary<string, object?> { ["type"] = "boolean", ["description"] = "Whether this column identifies the row, which is what a row-level comparison matches on" }
                                        },
                                        ["additionalProperties"] = false
                                    }
                                },
                                ["rows"] = new Dictionary<string, object?>
                                {
                                    ["type"] = "array",
                                    ["description"] = "The table's rows, each a list of cells in column order",
                                    ["items"] = new Dictionary<string, object?>
                                    {
                                        ["type"] = "object",
                                        ["required"] = new[] { "type", "values" },
                                        ["properties"] = new Dictionary<string, object?>
                                        {
                                            ["type"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = Enum.GetNames(typeof(TableRowType)), ["description"] = "Whether the row was expected, actual, or matched" },
                                            ["values"] = new Dictionary<string, object?> { ["type"] = "array", ["description"] = "The row's cells, in column order", ["items"] = new Dictionary<string, object?> { ["$ref"] = "#/$defs/comparedValue" } }
                                        },
                                        ["additionalProperties"] = false
                                    }
                                },
                                ["isLinkedOutput"] = new Dictionary<string, object?> { ["type"] = "boolean", ["description"] = "Whether the table is an output linked to an earlier input table rather than a parameter in its own right" }
                            },
                            ["additionalProperties"] = false
                        },
                        ["treeValue"] = new Dictionary<string, object?>
                        {
                            ["type"] = new[] { "object", "null" },
                            ["description"] = "A structured value, compared node by node",
                            ["required"] = new[] { "root" },
                            ["properties"] = new Dictionary<string, object?>
                            {
                                ["root"] = new Dictionary<string, object?> { ["$ref"] = "#/$defs/treeNode" }
                            },
                            ["additionalProperties"] = false
                        }
                    },
                    ["additionalProperties"] = false
                },
                ["comparedValue"] = new Dictionary<string, object?>
                {
                    ["type"] = new[] { "object", "null" },
                    ["description"] = "A value that was checked: what was there, what was wanted, and how that came out. The same shape for an inline parameter and for a table cell.",
                    ["required"] = new[] { "status" },
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["value"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "The value as it was, rendered for display" },
                        ["expectation"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "What the test said it should be; null when the value was not an assertion" },
                        ["status"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = Enum.GetNames(typeof(VerificationStatus)), ["description"] = "How the comparison came out" }
                    },
                    ["additionalProperties"] = false
                },
                ["treeNode"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["description"] = "One node of a structured parameter value, with its children",
                    ["required"] = new[] { "path", "node", "status" },
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["path"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "Dotted path from the root, which is how a failing node is addressed" },
                        ["node"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "This node's own name" },
                        ["value"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "The leaf value, rendered for display; null for a branch" },
                        ["expectation"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "What the test said this node should be" },
                        ["status"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = Enum.GetNames(typeof(VerificationStatus)), ["description"] = "How this node's comparison came out" },
                        ["children"] = new Dictionary<string, object?> { ["type"] = new[] { "array", "null" }, ["description"] = "Nested nodes", ["items"] = new Dictionary<string, object?> { ["$ref"] = "#/$defs/treeNode" } }
                    },
                    ["additionalProperties"] = false
                },
                ["textSegment"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["description"] = "One run of the step text: either literal prose, or the place a parameter value was substituted into it",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["text"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "The literal text of this segment" },
                        ["parameterName"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "The parameter this segment stands for, when it is a substitution rather than prose" },
                        ["parameter"] = new Dictionary<string, object?> { ["$ref"] = "#/$defs/comparedValue" },
                        ["tableReference"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "The data table this segment points at, when the step text names one" },
                        ["tableReferenceFormattedValue"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "That table rendered for display inside the step text" }
                    },
                    ["additionalProperties"] = false
                },
                ["diagnostic"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["required"] = new[] { "kind", "message" },
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["kind"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = Enum.GetNames(typeof(DiagnosticKind)), ["description"] = "What the entry is about (DiagnosticKind)" },
                        ["message"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "One-line description, safe to print" },
                        ["scenarioId"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "The scenario the entry belongs to, when it is scenario-specific" }
                    }
                },
                ["step"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["keyword"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "Gherkin keyword (Given, When, Then, And, But); null for a tracked assertion or a sub-step" },
                        ["text"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "The step text, capitalised per CapitaliseStepText, placeholders expanded for an outline row" },
                        ["status"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["enum"] = statusEnumValues, ["description"] = "The step's own verdict; null when the producer recorded none" },
                        ["durationSeconds"] = new Dictionary<string, object?> { ["type"] = new[] { "number", "null" }, ["description"] = "Seconds the step took; null when unknown" },
                        ["subSteps"] = new Dictionary<string, object?>
                        {
                            ["type"] = "array",
                            ["description"] = "Nested steps and tracked assertions, addressed <parent>.0, <parent>.1, ... by stepPath",
                            ["items"] = new Dictionary<string, object?> { ["$ref"] = "#/$defs/step" }
                        },
                        ["attachments"] = Attachment("Files attached while this step was active"),
                        ["bypassReason"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "Why the step was skipped, when its status is Bypassed" },
                        ["docString"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "The step's Gherkin doc-string body" },
                        ["docStringMediaType"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "Media type declared on the doc string, when the source gave one" },
                        ["comments"] = new Dictionary<string, object?> { ["type"] = "array", ["items"] = new Dictionary<string, object?> { ["type"] = "string" }, ["description"] = "Comment lines attached to the step in the source" },
                        ["parameters"] = new Dictionary<string, object?> { ["type"] = "array", ["description"] = "The step's inputs: inline values, data tables (columns and rows) and tree values. Present unless TestRunReportFullStepDetail is turned off.", ["items"] = new Dictionary<string, object?> { ["$ref"] = "#/$defs/stepParameter" } },
                        ["textSegments"] = new Dictionary<string, object?> { ["type"] = new[] { "array", "null" }, ["description"] = "The step text split into literal prose and inline parameter values, for highlighted rendering", ["items"] = new Dictionary<string, object?> { ["$ref"] = "#/$defs/textSegment" } },
                        ["failureMessage"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "Why this step or assertion failed — the assertion message, or the exception that ended the step" },
                        ["sourceFile"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "File the assertion was written in (name only), when the caller supplied it" },
                        ["sourceLine"] = new Dictionary<string, object?> { ["type"] = new[] { "integer", "null" }, ["description"] = "Line in sourceFile" }
                    }
                },
                ["httpInteraction"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["type"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = new[] { "Request", "Response" }, ["description"] = "Which half of a call this is; the two halves share requestResponseId" },
                        ["method"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "HTTP verb, or the operation label a non-HTTP tracker recorded (SELECT, GET for a cache, Publish for a message); null for a bare event" },
                        ["uri"] = new Dictionary<string, object?> { ["type"] = "string", ["format"] = "uri", ["description"] = "The request URI; for non-HTTP dependencies a synthetic scheme://service/path the tracker built" },
                        ["serviceName"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "The dependency (callee), as named in the diagram" },
                        ["callerName"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "The caller: the system under test, or the test itself" },
                        ["content"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "The body as captured, after capture-time redaction and any MaxContentLength cap (a capped body ends with an ...truncated (N chars total) marker); null when there was none" },
                        ["headers"] = new Dictionary<string, object?>
                        {
                            ["type"] = "array",
                            ["description"] = "Captured headers after capture-time redaction; the render-time ExcludedHeaders list does not apply here",
                            ["items"] = new Dictionary<string, object?>
                            {
                                ["type"] = "object",
                                ["properties"] = new Dictionary<string, object?>
                                {
                                    ["key"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Header name" },
                                    ["value"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "Header value; null when the header was present without one" }
                                }
                            }
                        },
                        ["statusCode"] = new Dictionary<string, object?> { ["type"] = new[] { "integer", "null" }, ["description"] = "The numeric status. Null on the request half, and null for a tracker whose outcome has no number (a broker Ack, a cache Hit) - those carry statusText only." },
                        ["statusText"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "The label for the status: the .NET name for an HTTP code (OK, BadRequest) or the word a non-HTTP tracker recorded (Ack, Responded, Hit). Null on the request half." },
                        ["traceId"] = new Dictionary<string, object?> { ["type"] = "string", ["format"] = "uuid", ["description"] = "Kronikol's own id for the request/response pair. Not the W3C trace id — that is activityTraceId." },
                        ["requestResponseId"] = new Dictionary<string, object?> { ["type"] = "string", ["format"] = "uuid", ["description"] = "Pairs a request with its response: both halves carry the same value" },
                        ["timestamp"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["format"] = "date-time", ["description"] = "When this half was captured (UTC); null when the capture path recorded none" },
                        ["metaType"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = Enum.GetNames(typeof(RequestResponseMetaType)), ["description"] = "Default for a request/response exchange, Event for a fire-and-forget publish" },
                        ["dependencyCategory"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "What kind of thing the callee is (database, cache, queue, ...) — drives participant shape and arrow colour in the diagram" },
                        ["callerDependencyCategory"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "The same, for the caller" },
                        ["phase"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = Enum.GetNames(typeof(TestPhase)), ["description"] = "Whether the call happened during Setup or the Action under test; Unknown when phase detection is off" },
                        ["isUserAction"] = new Dictionary<string, object?> { ["type"] = "boolean", ["description"] = "A UI interaction (click, navigate) rather than a dependency call" },
                        ["activityTraceId"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "W3C trace id — the bridge to OpenTelemetry traces and application logs. Unlike traceId, which is Kronikol's own identifier for the request/response pair.", ["examples"] = new[] { "4bf92f3577b34da6a3ce929d0e0e4736" } },
                        ["activitySpanId"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "W3C span id" },
                        ["capturedBy"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "Which capture path produced this entry: wire (proxy/TCP tap) or span (OpenTelemetry receiver)" },
                        ["durationMs"] = new Dictionary<string, object?> { ["type"] = new[] { "number", "null" }, ["description"] = "Wall-clock milliseconds between the request and its response, derived from the two timestamps. Repeated on both halves of the pair; null when the request went unanswered or timestamps are absent." },
                        ["stepPath"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "Which step this call happened under: an index into the scenario's steps, prefixed b for a background step (b0, 0, 1, ...). Null before the first step, and whenever attribution could not be trusted — see the StepAttributionMismatch diagnostic.", ["examples"] = new[] { "0", "b0", "2.1" } }
                    }
                }
            }
        };

        CloseDeclaredObjects(schema);

        if (pascalCasePropertyNames)
        {
            UsePascalCasePropertyNames(schema);

            // The root $comment points at `kronikol query`, which reads TestRunReport.json and says
            // "No TestRunReport.json under <path>" for a directory holding a YAML run. Inheriting it
            // would ship an instruction that cannot be followed from the file it is attached to.
            schema["$comment"] = "The field-level contract of TestRunReport.yml. Property names are the "
                + "YAML writer's; the JSON file beside a JSON run uses the same contract in camelCase. "
                + "`kronikol query` reads the JSON form only, so a YAML run is read with a YAML parser "
                + "rather than with the tool.";
        }

        return JsonSerializer.Serialize(schema, options);
    }

    /// <summary>
    /// Adds <c>additionalProperties: false</c> to every object node that declares a fixed set of
    /// properties, in place.
    /// </summary>
    /// <remarks>
    /// <para>Without it a JSON Schema permits every key it has not heard of, so the schema could not
    /// detect the one thing it exists to detect - a writer emitting a field nobody declared. That check
    /// was being done instead by a hand-written walker in the test suite, which meant it ran here and
    /// nowhere else: a consumer with a validator and no walker saw nothing.</para>
    ///
    /// <para>Applied as a rule rather than written out node by node, so a node added to the schema later
    /// is closed by default and has to opt out deliberately. Opting out is what "already declares
    /// <c>additionalProperties</c>" means, and a node with no <c>properties</c> at all is not a fixed
    /// key set in the first place - that is how <c>internalFlowSegments</c> stays open, which it must,
    /// because a merge carries those payloads through verbatim from shards another version wrote.</para>
    /// </remarks>
    private static void CloseDeclaredObjects(object? node)
    {
        switch (node)
        {
            case Dictionary<string, object?> map:
                if (map.ContainsKey("properties") && !map.ContainsKey("additionalProperties"))
                    map["additionalProperties"] = false;

                foreach (var value in map.Values.ToArray())
                    CloseDeclaredObjects(value);
                break;

            case System.Collections.IEnumerable sequence and not string:
                foreach (var item in sequence)
                    CloseDeclaredObjects(item);
                break;
        }
    }

    /// <summary>
    /// Rewrites a schema's declared property names into PascalCase, in place.
    /// </summary>
    /// <remarks>
    /// Only two things name a property: the keys of a <c>properties</c> object, and the entries of a
    /// <c>required</c> array. Everything else that looks like a name is not one — <c>$defs</c> keys are
    /// definition names a <c>$ref</c> points at, and renaming either would break the reference — so the
    /// walk renames exactly those two and recurses through the rest untouched.
    /// </remarks>
    private static void UsePascalCasePropertyNames(object? node)
    {
        switch (node)
        {
            case Dictionary<string, object?> map:
                if (map.TryGetValue("properties", out var properties) && properties is Dictionary<string, object?> declared)
                    map["properties"] = declared.ToDictionary(entry => PascalCase(entry.Key), entry => entry.Value);

                if (map.TryGetValue("required", out var required) && required is string[] names)
                    map["required"] = names.Select(PascalCase).ToArray();

                foreach (var value in map.Values.ToArray())
                    UsePascalCasePropertyNames(value);
                break;

            case System.Collections.IEnumerable sequence and not string:
                foreach (var item in sequence)
                    UsePascalCasePropertyNames(item);
                break;
        }
    }

    private static string PascalCase(string name) =>
        name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];

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
                new XElement(xs + "element", new XAttribute("name", "StatusCode"), new XAttribute("type", "xs:int"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "StatusText"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
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
                new XElement(xs + "element", new XAttribute("name", "FailureCause"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
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
                new XElement(xs + "element", new XAttribute("name", "Attempt"), new XAttribute("type", "xs:int"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "SourceFile"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "SourceLine"), new XAttribute("type", "xs:int"), new XAttribute("minOccurs", "0")),
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
                ),
                new XElement(xs + "element", new XAttribute("name", "Annotations"), new XAttribute("minOccurs", "0"),
                    new XElement(xs + "complexType",
                        new XElement(xs + "sequence",
                            new XElement(xs + "element", new XAttribute("name", "Annotation"), new XAttribute("type", "AnnotationType"), new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"))
                        )
                    )
                )
            ));

        var annotationType = new XElement(xs + "complexType",
            new XAttribute("name", "AnnotationType"),
            new XElement(xs + "sequence",
                new XElement(xs + "element", new XAttribute("name", "Index"), new XAttribute("type", "xs:int")),
                new XElement(xs + "element", new XAttribute("name", "Kind"), new XAttribute("type", "xs:string")),
                new XElement(xs + "element", new XAttribute("name", "Text"), new XAttribute("type", "xs:string"))
            ));

        // ScenarioId is absent on a report-level entry, which is the same omit-don't-blank rule the rest
        // of this writer follows.
        var diagnosticType = new XElement(xs + "complexType",
            new XAttribute("name", "DiagnosticType"),
            new XElement(xs + "sequence",
                new XElement(xs + "element", new XAttribute("name", "Kind"), new XAttribute("type", "xs:string")),
                new XElement(xs + "element", new XAttribute("name", "Message"), new XAttribute("type", "xs:string")),
                new XElement(xs + "element", new XAttribute("name", "ScenarioId"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0"))
            ));

        var featureType = new XElement(xs + "complexType",
            new XAttribute("name", "FeatureType"),
            new XElement(xs + "sequence",
                new XElement(xs + "element", new XAttribute("name", "Name"), new XAttribute("type", "xs:string")),
                new XElement(xs + "element", new XAttribute("name", "Endpoint"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "Description"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "SourceFile"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
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

        // Provider is always written; the six optional children are omitted when empty, which is what
        // this writer does everywhere. The JSON keeps every key - that is the shape to key on.
        var ciMetadataType = new XElement(xs + "complexType",
            new XAttribute("name", "CiMetadataType"),
            new XElement(xs + "sequence",
                new XElement(xs + "element", new XAttribute("name", "Provider"), new XAttribute("type", "xs:string")),
                new XElement(xs + "element", new XAttribute("name", "BuildNumber"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "Branch"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "CommitSha"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "PipelineUrl"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "Repository"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "RunId"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "RunAttempt"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0"))
            ));

        var runEnvironmentType = new XElement(xs + "complexType",
            new XAttribute("name", "RunEnvironmentType"),
            new XElement(xs + "sequence",
                new XElement(xs + "element", new XAttribute("name", "Os"), new XAttribute("type", "xs:string")),
                new XElement(xs + "element", new XAttribute("name", "Runtime"), new XAttribute("type", "xs:string"))
            ));

        var doc = new XDocument(
            new XElement(xs + "schema",
                new XAttribute(XNamespace.Xmlns + "xs", "http://www.w3.org/2001/XMLSchema"),
                executionResultType,
                headerType,
                stepType,
                httpInteractionType,
                scenarioType,
                annotationType,
                diagnosticType,
                featureType,
                ciMetadataType,
                runEnvironmentType,
                new XElement(xs + "element",
                    new XAttribute("name", "TestRunReport"),
                    new XElement(xs + "complexType",
                        new XElement(xs + "sequence",
                            new XElement(xs + "element", new XAttribute("name", "FormatVersion"), new XAttribute("type", "xs:int"), new XAttribute("minOccurs", "0")),
                            new XElement(xs + "element", new XAttribute("name", "KronikolVersion"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                            new XElement(xs + "element", new XAttribute("name", "Suite"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                            new XElement(xs + "element", new XAttribute("name", "StartTime"), new XAttribute("type", "xs:string")),
                            new XElement(xs + "element", new XAttribute("name", "EndTime"), new XAttribute("type", "xs:string")),
                            // xs:sequence is ordered: these sit exactly where GenerateTestRunReportXml
                            // writes them, and minOccurs="0" keeps every pre-3.1.0 file valid.
                            new XElement(xs + "element", new XAttribute("name", "CiMetadata"), new XAttribute("type", "CiMetadataType"), new XAttribute("minOccurs", "0")),
                            new XElement(xs + "element", new XAttribute("name", "Environment"), new XAttribute("type", "RunEnvironmentType"), new XAttribute("minOccurs", "0")),
                            new XElement(xs + "element", new XAttribute("name", "Features"),
                                new XElement(xs + "complexType",
                                    new XElement(xs + "sequence",
                                        new XElement(xs + "element", new XAttribute("name", "Feature"), new XAttribute("type", "FeatureType"), new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"))
                                    )
                                )
                            ),
                            // Last, matching the writer and the JSON. minOccurs="0" because every XML
                            // report written before this element existed is still a valid one.
                            new XElement(xs + "element", new XAttribute("name", "Diagnostics"), new XAttribute("minOccurs", "0"),
                                new XElement(xs + "complexType",
                                    new XElement(xs + "sequence",
                                        new XElement(xs + "element", new XAttribute("name", "Diagnostic"), new XAttribute("type", "DiagnosticType"), new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"))
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