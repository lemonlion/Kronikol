using Kronikol.History;
using System.Globalization;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Kronikol.ComponentDiagram;
using Kronikol.Compression;
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

    /// <summary>
    /// The user stylesheets a report carries after its built-in sheets, in cascade order: the given
    /// theme (the specifications sheet, or none), then <see cref="ReportConfigurationOptions.InternalFlowPopupCustomStyleSheet"/>
    /// when internal-flow tracking is on. Null when there are none, so the report keeps its bytes.
    /// </summary>
    internal static string? UserStylesheets(string? theme, ReportConfigurationOptions options)
    {
        var popup = options.InternalFlowTracking ? options.InternalFlowPopupCustomStyleSheet : null;
        if (string.IsNullOrEmpty(popup)) return theme;
        return string.IsNullOrEmpty(theme) ? popup : theme + "\n" + popup;
    }

    internal static bool ShouldEmbedComponentDiagram(ReportConfigurationOptions options) =>
        (options.ComponentDiagramOptions ?? new ComponentDiagramOptions()).EmbedInTestRunReport;

    /// <summary>Rendered in place of the diagram section for a scenario whose id matched no tracked interaction.</summary>
    internal const string NoInteractionsMarkerHtml =
        "<div class=\"no-interactions\" data-no-interactions=\"true\">No interactions captured for this scenario.</div>";
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> ResourceCache = new();

    // Loads an embedded report asset (externalized inline JS/CSS, JAVA_PORT_PLAN 4.2) by file-name suffix.
    internal static string LoadResource(string name) => ResourceCache.GetOrAdd(name, n =>
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(r => r.EndsWith(n, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded resource {n} not found.");
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    // A report script with its fenced $verdict lines (VerdictFences): kept when the report draws verdict attributes,
    // replaced by the lines that stand in for them when it does not. Each variant is cached under its own key.
    internal static string LoadScript(string name, bool verdicts) =>
        ResourceCache.GetOrAdd($"{name}|{(verdicts ? "verdicts" : "plain")}", _ => VerdictFences.Variant(LoadResource(name), verdicts));

    // The light/dark switch at the right of the title line (5.0.0, plans/OPTION_C_PLAN.md S1): a moon while the page is
    // light, the theme it switches to, and a sun while it is dark. report-theme-switch-script.js keeps aria-pressed and
    // the title in step with the theme the boot script chose. The labs page's title line carries it too (S13).
    internal const string ThemeSwitchButton =
        "<button type=\"button\" class=\"theme-toggle\" aria-label=\"Dark theme\" aria-pressed=\"false\" title=\"Switch to the dark theme\">"
        + "<svg class=\"theme-icon theme-moon\" viewBox=\"0 0 24 24\" aria-hidden=\"true\"><path d=\"M20.5 14.2A8.5 8.5 0 0 1 9.8 3.5a8.5 8.5 0 1 0 10.7 10.7z\"/></svg>"
        + "<svg class=\"theme-icon theme-sun\" viewBox=\"0 0 24 24\" aria-hidden=\"true\"><path d=\"M12 7.5a4.5 4.5 0 1 0 0 9 4.5 4.5 0 0 0 0-9z\"/>"
        + "<path d=\"M12 1.8v2.4M12 19.8v2.4M4.8 4.8l1.7 1.7M17.5 17.5l1.7 1.7M1.8 12h2.4M19.8 12h2.4M4.8 19.2l1.7-1.7M17.5 6.5l1.7-1.7\"/></svg>"
        + "</button>";

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
    public static void CreateStandardReportsWithDiagrams(Feature[] features, DateTime startRunTime, DateTime endRunTime, ReportConfigurationOptions options, RunEnvironment? environment = null) =>
        CreateStandardReportsWithDiagramsInEnvironment(features, startRunTime, endRunTime, options, environment, Environment.GetEnvironmentVariable);

    /// <summary>
    /// The same, with the process environment injected — for tests, the way
    /// <see cref="HistoryRunContext.Create"/> and <see cref="CiMetadataDetector"/> take theirs. What a run
    /// does about its predecessor depends on whether it is on CI and on <c>KRONIKOL_KEEP_RUNS</c>, and
    /// the test process's real environment is neither stable (its own pipeline is CI) nor its own (one
    /// class sets <c>GITHUB_ACTIONS</c> for every test running beside it).
    /// </summary>
    /// <remarks>
    /// <paramref name="getEnv"/> answers for CI detection, history resolution and run retention — the
    /// three that decide what is on disk afterwards — and for the CI channels further down: the step
    /// summary, the artifact publisher, and what the CI text says of the run (<see cref="CiReader"/>). A test
    /// that injects "on CI" names its own step summary, and never appends to the real job summary.
    ///
    /// <para><paramref name="beforeFirstWrite"/> is handed the reports directory after the previous run has
    /// been rotated out of it and before this run writes its first byte into it — the one moment a host
    /// may safely delete from it. Not called for a pass with no scenarios, which writes nothing.</para>
    /// </remarks>
    // spans: the spans an ingest was handed (IngestRequest.Spans), drawn in place of this process's span store, which is
    // then neither read nor written; null reads the store, as an in-process run always has.
    // historyBaseDirectory: where the history ledger is looked for first (then above the reports directory); null is this
    // process's base directory, right for a test binary built inside its repository. An ingest passes the directory it
    // runs in: the tool's own folder says nothing about whose run it is replaying.
    // attachments: what an ingest knows of its capture's attachments that the copier cannot see (AttachmentContext); null for
    // an in-process run.
    internal static void CreateStandardReportsWithDiagramsInEnvironment(Feature[] features, DateTime startRunTime, DateTime endRunTime, ReportConfigurationOptions options, RunEnvironment? environment, Func<string, string?> getEnv, Action<string>? beforeFirstWrite = null, IReadOnlyList<FlowSpan>? spans = null, string? historyBaseDirectory = null, AttachmentContext? attachments = null)
    {
        var previous = ActiveReportsDirectory.Value;
        ActiveReportsDirectory.Value = ResolveReportsDirectory(options);
        // A host that already scoped a collector (kronikol ingest, a dashboard) keeps it — its entries and
        // ours land in the same report. An adapter-driven run has none, and without one every
        // ReportDiagnosticsScope.Record on this path — render failures, attachment failures, step
        // attribution mismatches, output failures — was a silent no-op and the JSON's diagnostics array was
        // always empty. The AsyncLocal keeps concurrent generations apart.
        var ownScope = ReportDiagnosticsScope.Current is null ? ReportDiagnosticsScope.Begin(new ReportDiagnosticsCollector()) : null;
        // What this run writes, recorded where it is written, for the Run.json it ends with. Scoped like
        // the directory above and for the same reason: it has to reach the Parallel.Invoke workers.
        var runFiles = RunFileCollector.Begin(ActiveReportsDirectory.Value);
        try
        {
            // A run that scoped its own collector has no host to print what it recorded, so it prints what a reader must
            // not miss itself; a host that scoped one (kronikol ingest) prints every entry already.
            CreateStandardReportsWithDiagramsCore(features, startRunTime, endRunTime, options, environment, getEnv, beforeFirstWrite, spans, historyBaseDirectory,
                attachments, printsDiagnostics: ownScope is not null);
        }
        finally
        {
            runFiles.Dispose();
            ownScope?.Dispose();
            ActiveReportsDirectory.Value = previous;
        }
    }

    private static void CreateStandardReportsWithDiagramsCore(Feature[] features, DateTime startRunTime, DateTime endRunTime, ReportConfigurationOptions options, RunEnvironment? environment, Func<string, string?> getEnv, Action<string>? beforeFirstWrite, IReadOnlyList<FlowSpan>? suppliedSpans, string? historyBaseDirectory,
        AttachmentContext? attachmentContext, bool printsDiagnostics)
    {
        // Guard: skip report generation entirely when there are zero scenarios.
        // This prevents the xUnit v3 test-discovery pass (which triggers
        // ITestPipelineStartup but runs no tests) from overwriting a valid
        // report from a previous run with an empty one.
        // A call an owner window still holds (DocumentOwnership) will not be confirmed for this report:
        // settled before the first read below, so every read of the run's interactions agrees.
        DocumentOwnership.Settle();
        if (features.Length == 0 || features.All(f => f.Scenarios is null || f.Scenarios.Length == 0))
        {
            if (RequestResponseLogger.RequestAndResponseLogs.Length > 0)
            {
                Console.WriteLine("⚠ WARNING: No test contexts were enqueued, but tracking logs exist. " +
                    "Reports will be empty. Ensure DiagrammedTestRun.TestContexts.Enqueue(TestContext.Current) " +
                    "is called in every test's DisposeAsync().");

                if (options.DiagnosticMode)
                {
                    // Never over the page the previous run's Run.json lists: this pass is no run, so the next
                    // rotation would file its page under that run's name, in place of that run's own.
                    if (RunManifest.TryRead(CurrentReportsDirectory) is { } previousRun
                        && previousRun.Files.Contains(DiagnosticReportGenerator.FileName, StringComparer.Ordinal))
                        Console.WriteLine($"⚠ {DiagnosticReportGenerator.FileName} was not written: the previous run's copy is kept, since its {RunManifest.FileName} lists it.");
                    else
                        RunIsolated(DiagnosticReportGenerator.FileName, () => DiagnosticReportGenerator.Generate(RequestResponseLogger.RequestAndResponseLogs, features, options));
                }
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

        // Every read of the run's interactions below goes through this one snapshot. A call that carried a
        // scenario's identity after the scenario had ended is the host's background work, not the
        // scenario's (plans/BACKGROUND_ATTRIBUTION_PLAN.md): it is re-attributed to no scenario here, once,
        // so the diagrams, the data files, the history line and the digest all agree about whose it is.
        var runLogs = BackgroundAttribution.Expire(features, RequestResponseLogger.RequestAndResponseLogs);
        var background = BackgroundAttribution.Summarise(runLogs, features);
        // The calls the run's first-call warm-up slowed down (plans/WARM_UP_PLAN.md), found once for every output, with the
        // consumer's shape templates when set (Q12).
        var warmUp = WarmUpCalls.Find(features, runLogs, HistoryShapeRules.Create(options.HistoryShapeTemplates));
        foreach (var group in background.AfterScenarioEnd)
            ReportDiagnosticsScope.Record(DiagnosticKind.BackgroundCalls,
                $"{group.Calls} call{(group.Calls == 1 ? "" : "s")} arrived after '{group.ScenarioName}' ended and are listed as background calls",
                group.ScenarioId);
        // Calls under an id no scenario has are in no scenario and not in the background section either: recorded
        // here, before the outputs, so the data files, the labs page and kronikol query say so (F7).
        if (OrphanedCalls.Describe(OrphanedCalls.Find(features, runLogs)) is { } orphaned)
            ReportDiagnosticsScope.Record(DiagnosticKind.UnattributedInteractions, orphaned);
        RecordOptionDiagnostics(options);

        // Decided here rather than with the outputs, so the diagnostic reaches the data files: a reports
        // directory inside a C# project's folder takes no query.cs, because that project would compile it.
        var queryScriptBlockedBy = WritesQueryScript(options) ? QueryScriptGenerator.CompilingProject(CurrentReportsDirectory) : null;
        if (queryScriptBlockedBy is not null)
        {
            var message = QueryScriptGenerator.NotApplied(queryScriptBlockedBy);
            ReportDiagnosticsScope.Record(DiagnosticKind.OptionNotApplied, message);
            Console.WriteLine("⚠ WARNING: " + message);
        }

        if (options.ExpectedTestCount != null)
        {
            // the run's tests: a scenario that is not a test (an ingest's fold, a stack's start-up) is none of those expected
            var scenarioCount = features.SelectMany(f => f.Scenarios).Count(s => !s.NotATest);
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
            MaxArrowsPerDiagram = options.MaxArrowsPerDiagram,
            Logs = runLogs
        };
        var diagrams = DefaultDiagramsFetcher.GetDiagramsFetcher(fetcherOptions)();

        var internalFlowDataScript = "";
        var internalFlowDataScriptSpecifications = "";
        Dictionary<string, InternalFlowSegment>? wholeTestSegments = null;
        Dictionary<string, InternalFlowSegment>? perBoundarySegments = null;
        RequestResponseLog[]? trackedLogs = null;
        if (options.InternalFlowTracking)
        {
            trackedLogs = runLogs
                .Where(x => !(x?.TrackingIgnore ?? true))
                .ToArray();

            // One span list for both builders, whatever recorded it: an ingest's spans are already FlowSpans, and this
            // process's activities become them here, with nothing internal flow reads changed.
            var spans = suppliedSpans is not null
                ? InternalFlowSpanCollector.FilterSupplied(suppliedSpans, options.InternalFlowSpanGranularity, options.InternalFlowActivitySources)
                : InternalFlowSpanCollector.CollectSpans(options.InternalFlowSpanGranularity, options.InternalFlowActivitySources)
                    .Select(FlowSpan.From)
                    .ToArray();

            perBoundarySegments = InternalFlowSegmentBuilder.SegmentsOf(trackedLogs, spans);
            if (suppliedSpans is not null)
                SuppliedSpanDiagnostics.Record(trackedLogs, suppliedSpans, spans, perBoundarySegments);

            if (options.WholeTestFlowVisualization != WholeTestFlowVisualization.None)
            {
                wholeTestSegments = InternalFlowSegmentBuilder.WholeTestSegmentsOf(trackedLogs, spans);
            }
        }

        var ciMetadata = CiMetadataDetector.Detect(getEnv);

        // The data file's httpInteractions block must not depend on internal-flow tracking being on:
        // externally captured traffic (proxy taps, ingested NDJSON) has no in-process spans but the
        // interactions are the whole point of the data export.
        var dataLogs = trackedLogs ?? runLogs
            .Where(x => !(x?.TrackingIgnore ?? true))
            .ToArray();

        // Cross-run history: resolved, read and analysed once, before any output is written, so the
        // digest, the CTRF document, the pointer and the report all say the same thing about this run
        // (plans/CROSS_RUN_HISTORY_PLAN.md §6.6). It never throws — anything that stops it is a diagnostic —
        // and it is null only when history is switched off.
        var runEndedAt = new DateTimeOffset(endRunTime.ToUniversalTime());
        var history = HistoryRunContext.Create(features, dataLogs, suite, ciMetadata, runEndedAt,
            options, CurrentReportsDirectory, KronikolVersion, getEnv, historyBaseDirectory);

        var specsDataExtension = GetDataFormatExtension(options.SpecificationsDataFormat);
        var testRunDataExtension = DataFileExtension(options);

        // A run always has a name, because the directory a run is retained under is made from it: the id
        // on its line of history when history is on, and the same minting — the host's HistoryRunId, else
        // the CI run, else a timestamp — when it is off.
        var runId = history?.Run.Id
                    ?? (string.IsNullOrWhiteSpace(options.HistoryRunId) ? HistoryRunBuilder.RunId(ciMetadata, runEndedAt) : options.HistoryRunId.Trim());

        // The last N runs are kept (RunRotation): the previous run's files move to runs/<run>/ before this
        // run writes any of its own. Here and nowhere earlier or later — after the zero-scenario guard, so
        // a discovery pass never rotates a real run away; after history, which is where the id comes from;
        // and before the first byte this run writes, which is the attachment copies just below. It never
        // throws: whatever stops it is a ReportRotationFailed diagnostic, recorded before the snapshot the
        // report's own diagnostics are taken from, and the run overwrites as it always has.
        var rotation = RunRotation.Prepare(new RunRotation.Request(
            CurrentReportsDirectory,
            runId,
            OnCi: ciMetadata is not null,
            RunRotation.ResolveKeepRuns(options.KeepRuns, getEnv, onCi: ciMetadata is not null),
            options.HtmlTestRunReportFileName,
            testRunDataExtension,
            PlannedFiles(options, history, specsDataExtension, testRunDataExtension)));

        // Whatever a host does to the directory before a run writes into it happens here, after the
        // previous run has been moved out of it and not before: `kronikol ingest --clean-attachments`
        // empties attachments/, and emptied it while the run about to be kept still owned what was in it.
        beforeFirstWrite?.Invoke(CurrentReportsDirectory);

        // Pre-compute component diagram PlantUML for embedding, as the Components tab draws it: without its title, which the
        // tab's heading carries, and with the tests that made each connection's calls, for its controls (5.0.0,
        // plans/OPTION_C_PLAN.md S11, D26, D28). A run whose calls give the diagram no connection has no tab: there is
        // nothing to draw. ComponentDiagram.html is written from the same calls on its own, title and all.
        EmbeddedComponentDiagram? componentDiagram = null;
        if (options.GenerateComponentDiagram)
        {
            var componentOptions = options.ComponentDiagramOptions ?? new ComponentDiagramOptions();
            componentOptions.DependencyColors ??= options.DependencyColors;
            var componentLogs = runLogs.Where(x => !(x?.TrackingIgnore ?? true));
            var componentRelationships = ComponentDiagramGenerator.ExtractRelationships(componentLogs, componentOptions.ParticipantFilter);
            // The syntax ComponentDiagram.html is written in: until 3.31.8 the run report gave NodeJs the C4 library, which
            // the Node renderer cannot load.
            if (componentRelationships.Length > 0)
                componentDiagram = ComponentDiagramGenerator.GenerateEmbedded(componentRelationships, componentOptions,
                    useC4: ComponentDiagramReportGenerator.UsesC4(options.PlantUmlRendering), logs: componentLogs);
        }
        var componentDiagramPlantUml = componentDiagram?.PlantUml;

        if (perBoundarySegments is not null)
        {
            // The segment map's id list is computed over every diagram source a page can show, so it needs the
            // component diagram, which is why the popup data is built here rather than with the segments. A page shows
            // a diagram only for a scenario it lists (every read of diagramsByTestId is by a scenario's id), so a test
            // the process logged that these features do not name is left out: counted in, its links, none of which the
            // page shows, could outnumber the page's own and tip the list's choice.
            var shownTestIds = features.SelectMany(f => f.Scenarios).Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
            var linkSources = diagrams.Where(d => shownTestIds.Contains(d.TestRuntimeId)).Select(d => (string?)d.CodeBehind)
                .Append(ShouldEmbedComponentDiagram(options) ? componentDiagramPlantUml : null)
                .ToArray();
            var linkedSegments = InternalFlowHtmlGenerator.LinkedSegments(perBoundarySegments, linkSources);

            string BuildFlowScript(InternalFlowTab startTab) =>
                DiagramContextMenu.GetInternalFlowConfigScript(options.InternalFlowHasDataBehavior)
                + InternalFlowHtmlGenerator.WrapSegmentData(
                    InternalFlowHtmlGenerator.StoreFlowsOnce(InternalFlowHtmlGenerator.BuildSegmentData(
                        linkedSegments,
                        options.InternalFlowDiagramStyle,
                        options.InternalFlowShowFlameChart,
                        options.InternalFlowFlameChartPosition,
                        options.InternalFlowNoDataBehavior,
                        options.InternalFlowSpanGranularity,
                        options.InternalFlowActivitySources,
                        startTab,
                        suppliedSpans?.Count)),
                    linkSources);

            // The popup data script is shared by both HTML reports; only a Specifications
            // override that actually changes the internal-flow tab pays for a second build.
            var testRunTab = ReportToggleDefaultsResolver.Resolve(options, specifications: false).InternalFlowTab;
            var specificationsTab = ReportToggleDefaultsResolver.Resolve(options, specifications: true).InternalFlowTab;
            internalFlowDataScript = BuildFlowScript(testRunTab);
            internalFlowDataScriptSpecifications = specificationsTab == testRunTab
                ? internalFlowDataScript
                : BuildFlowScript(specificationsTab);
        }

        // Copy attachment files into the Reports directory so that HTML links resolve
        // when reports are uploaded to GitHub Pages or CI artifacts.
        var reportsDir = CurrentReportsDirectory;
        Directory.CreateDirectory(reportsDir);

        // Decided before the outputs run in parallel, because the digest tells its reader about query.cs and is
        // written beside it: planned, not yet written. A query.cs someone else put here is not Kronikol's to
        // replace, and the digest must not send anyone to run it.
        var queryScriptPlanned = WritesQueryScript(options) && queryScriptBlockedBy is null && !QueryScriptGenerator.IsForeign(reportsDir);
        // Every attachment the report does not hold is said once (#148): recorded before the snapshot below, so the HTML, the
        // data files and kronikol query all carry it, and printed by a run with no host to print it, which is otherwise
        // where a broken screenshot went unexplained.
        var attachmentAccount = CopyAttachments(features, reportsDir);
        attachmentAccount.Include(attachmentContext);
        if (attachmentAccount.Describe(attachmentContext) is { } attachmentMessage)
        {
            ReportDiagnosticsScope.Record(DiagnosticKind.AttachmentFailure, attachmentMessage);
            if (printsDiagnostics)
                Console.WriteLine("⚠ WARNING: " + attachmentMessage);
        }

        // The run report's component diagram, drawn now by the report's renderer where the page will not draw it
        // (NodeJs, Server, Local), and before the diagnostics are taken below, so a failure to draw it is in the report.
        var componentDiagramDrawn = options.GenerateTestRunReport && ShouldEmbedComponentDiagram(options) && componentDiagramPlantUml is not null
            ? ComponentDiagramReportGenerator.DrawEmbedded(componentDiagramPlantUml, options)
            : null;

        // Capture health a component recorded while it was set up, before any generation was scoped (Kafka build
        // interception that tiered compilation can end): every report this process writes carries it.
        if (ReportDiagnosticsScope.Current is { } setupDiagnostics)
            ProcessDiagnostics.AddTo(setupDiagnostics);

        // Everything recorded so far — the host's entries (IngestRequest.HostDiagnostics), malformed lines,
        // diagram render failures, attachment failures — goes into the report itself. One snapshot, taken
        // before the outputs run in parallel, so the HTML and the data files agree; an OutputFailure raised
        // by one of those outputs is therefore only in the collector, not in the files.
        var reportDiagnostics = ReportDiagnosticsScope.Current?.Entries ?? [];

        // Which call happened under which step, and the annotations worth exporting: read by the data file
        // (standard or mergeable) and by Failures.md, and deriving it records a StepAttributionMismatch.
        // Derived once per run, here, rather than once per reader, which printed every mismatch twice.
        // Lazy and thread-safe, because the outputs run in parallel and a run writing none of the three
        // never pays for it.
        var attribution = new Lazy<(Dictionary<string, List<string?>> StepPaths, Dictionary<string, List<ScenarioAnnotation>> Annotations)>(
            () => AttributeInteractionsToSteps(dataLogs, features));

        var actions = new List<(string Name, Action Run)>();
        void Add(string name, Action run) => actions.Add((name, run));

        // One deep-search build cache shared by both HTML reports (§5.1): they render the same
        // features/diagrams, so the expensive normalize+hash work happens once.
        var searchIndexCache = new SearchIndex.SearchIndexBuildCache();

        if (options.GenerateSpecificationsReport)
        {
            Add($"{options.HtmlSpecificationsFileName}.html", () => GenerateHtmlReportCore(diagrams, features, startRunTime, endRunTime, UserStylesheets(options.HtmlSpecificationsCustomStyleSheet, options), $"{options.HtmlSpecificationsFileName}.html", options.SpecificationsTitle, false, generateBlankOnFailedTests: true, lazyLoadImages: options.LazyLoadDiagramImages, diagramFormat: options.DiagramFormat, plantUmlRendering: options.PlantUmlRendering, inlineSvgRendering: options.InlineSvgRendering, internalFlowTracking: options.InternalFlowTracking, internalFlowDataScript: internalFlowDataScriptSpecifications, wholeTestSegments: wholeTestSegments, trackedLogs: trackedLogs, wholeTestVisualization: options.WholeTestFlowVisualization, showStepNumbers: options.SpecificationsShowStepNumbers, customCss: options.CustomCss, customFaviconBase64: options.CustomFaviconBase64, customLogoHtml: options.CustomLogoHtml, groupParameterizedTests: options.GroupParameterizedTests, maxParameterColumns: options.MaxParameterColumns, titleizeParameterNames: options.TitleizeParameterNames, showNoInteractionsMarker: options.ShowNoInteractionsMarker, browserRenderWorkers: options.BrowserRenderWorkers, browserRenderCacheMegabytes: options.BrowserRenderCacheMegabytes, browserFragmentMaxHeight: options.BrowserFragmentMaxHeight, separateBackgroundSteps: options.SeparateBackgroundSteps, collapseRepeatedStepKeywords: options.CollapseRepeatedStepKeywords, notePayloadFormat: options.NotePayloadFormat, fullSearchIndex: options.FullSearchIndex, searchIndexCache: searchIndexCache, toggleDefaults: ReportToggleDefaultsResolver.Resolve(options, specifications: true), suite: suite, renderCollapsedSubStepsOnExpand: options.RenderCollapsedSubStepsOnExpand, warmUp: warmUp));
        }

        if (options.GenerateTestRunReport)
        {
            Add($"{options.HtmlTestRunReportFileName}.html", () => GenerateHtmlReportCore(diagrams, features, startRunTime, endRunTime, UserStylesheets(null, options), $"{options.HtmlTestRunReportFileName}.html", GetTestRunReportTitle(options), true, lazyLoadImages: options.LazyLoadDiagramImages, diagramFormat: options.DiagramFormat, plantUmlRendering: options.PlantUmlRendering, inlineSvgRendering: options.InlineSvgRendering, internalFlowTracking: options.InternalFlowTracking, internalFlowDataScript: internalFlowDataScript, wholeTestSegments: wholeTestSegments, trackedLogs: trackedLogs, wholeTestVisualization: options.WholeTestFlowVisualization, ciMetadata: ciMetadata, showStepNumbers: options.TestRunReportShowStepNumbers, customCss: options.CustomCss, customFaviconBase64: options.CustomFaviconBase64, customLogoHtml: options.CustomLogoHtml, groupParameterizedTests: options.GroupParameterizedTests, maxParameterColumns: options.MaxParameterColumns, titleizeParameterNames: options.TitleizeParameterNames, componentDiagramPlantUml: ShouldEmbedComponentDiagram(options) ? componentDiagramPlantUml : null, componentDiagramDrawn: componentDiagramDrawn, componentDiagram: ShouldEmbedComponentDiagram(options) ? componentDiagram : null, showNoInteractionsMarker: options.ShowNoInteractionsMarker, diagnostics: reportDiagnostics, background: background, browserRenderWorkers: options.BrowserRenderWorkers, browserRenderCacheMegabytes: options.BrowserRenderCacheMegabytes, browserFragmentMaxHeight: options.BrowserFragmentMaxHeight, separateBackgroundSteps: options.SeparateBackgroundSteps, collapseRepeatedStepKeywords: options.CollapseRepeatedStepKeywords, notePayloadFormat: options.NotePayloadFormat, fullSearchIndex: options.FullSearchIndex, searchIndexCache: searchIndexCache, toggleDefaults: ReportToggleDefaultsResolver.Resolve(options, specifications: false), suite: suite, history: options.EmbedHistoryInReport && (options.ShowScenarioHistory || options.ShowHistorySection) ? history?.Verdicts : null, showHistorySection: options.ShowHistorySection, showReportDiagnostics: options.ShowReportDiagnosticsSection, renderCollapsedSubStepsOnExpand: options.RenderCollapsedSubStepsOnExpand, showScenarioHistory: options.ShowScenarioHistory, warmUp: warmUp));
        }

        if (options.GenerateSpecificationsData)
        {
            Add($"{options.YamlSpecificationsFileName}.{specsDataExtension}", () => GenerateSpecificationsData(features, $"{options.YamlSpecificationsFileName}.{specsDataExtension}", options.SpecificationsTitle, options.SpecificationsDataFormat, true));
        }

#pragma warning disable CS0618 // CompressTestRunReportPayloads: obsolete from 5.0.0 for the code that sets it, honoured by the JSON file until v6
        if (options.GenerateTestRunReportData)
        {
            if (options.GenerateMergeableData && options.TestRunReportDataFormat == DataFormat.Json)
            {
                // dataLogs, not trackedLogs: the latter is null with internal-flow tracking off, and a
                // shard written that way carried no interactions at all, the gap 3.8.0 closed for the
                // standard file and this branch kept.
                var compressed = WritesCompressedReport(options);
                Add($"{options.HtmlTestRunReportFileName}.{testRunDataExtension}", () =>
                {
                    var model = BuildRunMergeableModel(features, startRunTime, endRunTime, diagrams, dataLogs, perBoundarySegments, wholeTestSegments, ciMetadata, options, reportDiagnostics, suite, environment, attribution.Value, suppliedSpans?.Count, warmUp,
                        compressPayloads: !compressed && options.CompressTestRunReportPayloads);
                    if (compressed)
                        WriteCompressedReport(model, $"{options.HtmlTestRunReportFileName}.{testRunDataExtension}");
                    else
                        WriteFile(JsonSerializer.Serialize(model, TestRunReportJsonOptions), $"{options.HtmlTestRunReportFileName}.{testRunDataExtension}");
                });
            }
            else
            {
                Add($"{options.HtmlTestRunReportFileName}.{testRunDataExtension}", () => GenerateTestRunReportData(features, startRunTime, endRunTime, $"{options.HtmlTestRunReportFileName}.{testRunDataExtension}", options.TestRunReportDataFormat, diagrams, dataLogs, reportDiagnostics, options.TestRunReportFullStepDetail, ciMetadata, suite, environment, attribution.Value, options.CompressTestRunReportPayloads, warmUp,
                    compressedFile: WritesCompressedReport(options)));
            }
        }
#pragma warning restore CS0618

        if (options.GenerateTestRunReportSchema)
        {
            Add("TestRunReport schema", () => GenerateTestRunReportSchema($"{options.HtmlTestRunReportFileName}.schema.{GetSchemaExtension(options.TestRunReportDataFormat)}", options.TestRunReportDataFormat));
        }

        if (options.GenerateComponentDiagram)
        {
            Add("ComponentDiagram.html", () => ComponentDiagramReportGenerator.GenerateComponentDiagramReport(
                runLogs.Where(x => !(x?.TrackingIgnore ?? true)),
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
                    KronikolVersion, queryScriptPlanned, reportDiagnostics, suite, stepPaths: attribution.Value.StepPaths, history: history?.Verdicts,
                    // A retry that passes writes "All N scenarios passed" two seconds after the failure it
                    // retried. The attempt that failed is kept under runs/, and this is the file an agent
                    // reads first, so it says so.
                    earlierAttempt: rotation.FailedAttemptOfThisRun is { } attempt
                        ? new FailuresDigestEarlierAttempt(
                            Path.GetRelativePath(reportsDir, attempt.Directory).Replace('\\', '/'), attempt.Manifest.Failed, attempt.Manifest.Scenarios)
                        // Not a retry: the run this one has just moved aside, when it failed. Said by the
                        // run that replaced it and by no later one.
                        : rotation is { KeptDirectory: { } keptRun, Kept: { Failed: > 0 } keptManifest }
                            ? new FailuresDigestEarlierAttempt(
                                Path.GetRelativePath(reportsDir, keptRun).Replace('\\', '/'), keptManifest.Failed, keptManifest.Scenarios, SameRun: false)
                            : null,
                    // A data file the folder lookup would not find is named in every command, as the CI text names it.
                    dataFileName: options.GenerateTestRunReportData && options.TestRunReportDataFormat == DataFormat.Json
                                  && RunSummaryConsoleWriter.QueryTargetFor(Path.Combine(reportsDir, $"{options.HtmlTestRunReportFileName}.{DataFileExtension(options)}")) is { } named
                        ? Path.GetFileName(named)
                        : null));

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
                CtrfReportGenerator.Generate(features, startRunTime, endRunTime, ciMetadata, KronikolVersion, suite, history?.Verdicts),
                CtrfReportGenerator.FileName));
        }

        // The run's own line of history, with the roster it needs: race-free (one process, its own
        // directory), part of the CI artifact by construction, and the unit `kronikol history record`
        // folds — which is what makes eight shards one run (plans/CROSS_RUN_HISTORY_PLAN.md §3.6).
        if (options.GenerateHistoryFragment && history is not null)
        {
            Add(HistoryFormat.FragmentFileName, () => WriteFile(history.Fragment(), HistoryFormat.FragmentFileName));
        }

        // The labs page: the history and diagnostics views a default report no longer carries
        // (plans/HISTORY_AND_DIAGNOSTICS_MOVE_PLAN.md). Written only when it has something to show, and linked to the
        // report by name when the options say one is written, never by whether a sibling output reached disk.
        var labsHistory = options.EmbedHistoryInReport ? history?.Verdicts : null;
        if (options.GenerateLabsReport && LabsReportGenerator.HasContent(labsHistory, reportDiagnostics))
        {
            var labsFileName = LabsReportGenerator.FileName(options.HtmlTestRunReportFileName);
            Add(labsFileName, () => WriteFile(
                LabsReportGenerator.Build(new LabsPage(GetTestRunReportTitle(options), features, suite, labsHistory, reportDiagnostics,
                    options.GenerateTestRunReport ? $"{options.HtmlTestRunReportFileName}.html" : null, runId, runEndedAt,
                    options.CustomCss, options.CustomFaviconBase64, options.CustomLogoHtml)),
                labsFileName));
        }

        if (options.WriteAgentInstructions)
        {
            Add(AgentInstructionsGenerator.ClaudeFileName, () =>
            {
                var block = AgentInstructionsBlock.Wrap(NamingTheDataFile(AgentInstructionsGenerator.Build(options.HtmlTestRunReportFileName), options));
                WriteAgentInstructionsFile(block, AgentInstructionsGenerator.ClaudeFileName);
                WriteAgentInstructionsFile(block, AgentInstructionsGenerator.AgentsFileName);
            });
        }

        // `kronikol query` without the tool (plans/QUERY_FALLBACK_PLAN.md). Never the run's: like the two
        // instruction files it describes the directory, and in runs/<run>/ it would name an engine by a path
        // one folder too shallow.
        if (WritesQueryScript(options) && queryScriptBlockedBy is null)
        {
            Add(QueryScriptGenerator.FileName, () => QueryScriptGenerator.Write(reportsDir, QueryScriptGenerator.Build(reportsDir),
                text => WriteFile(text, QueryScriptGenerator.FileName, partOfTheRun: false)));
        }

        var written = RunOutputs(actions);
        AfterOutputsForTests.Value?.Invoke();

        // The data file under its other name, which this run replaces (PlannedFiles), goes once this run's is on disk (5.2.7).
        if (options.GenerateTestRunReportData && written.Contains($"{options.HtmlTestRunReportFileName}.{testRunDataExtension}"))
            RunRotation.RemoveReplacedDataFile(reportsDir, options.HtmlTestRunReportFileName, testRunDataExtension);

        // After every output is on disk: a run whose report failed to write still has its fragment, and a
        // ledger that stays locked past the retry budget costs a diagnostic, never the run.
        history?.Append();

        var diagnostics = ReportDiagnostics.Analyse(
            runLogs, features,
            includeSourceDiscovery: options.ActivitySourceDiscovery,
            internalFlowTracking: options.InternalFlowTracking,
            suppliedSpans: suppliedSpans?.Count);
        foreach (var message in diagnostics)
            Console.WriteLine(message);

        // Isolated as the output list is: written after it, an exception here ended the run before Run.json.
        if (options.DiagnosticMode)
            RunIsolated(DiagnosticReportGenerator.FileName, () => DiagnosticReportGenerator.Generate(runLogs, features, options, suppliedSpans?.Count));

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
                FailuresDigestFileName,
                // After the report, so the pointer's first .html is still the report.
                LabsReportGenerator.FileName(options.HtmlTestRunReportFileName)
            }.Where(written.Contains),
            agentInstructionsWritten: options.WriteAgentInstructions
                                      && File.Exists(Path.Combine(reportsDir, AgentInstructionsGenerator.ClaudeFileName)),
            suite: suite,
            // A data file the directory lookup would not find (a custom HtmlTestRunReportFileName) is named, as a
            // merge names its own; without it every printed query answered "No TestRunReport.json under".
            queryTarget: options.TestRunReportDataFormat == DataFormat.Json && written.Contains($"{options.HtmlTestRunReportFileName}.{testRunDataExtension}")
                ? RunSummaryConsoleWriter.QueryTargetFor(Path.Combine(reportsDir, $"{options.HtmlTestRunReportFileName}.{testRunDataExtension}"))
                : null,
            // Only when it has something to say, or when the pointer is speaking anyway: a line on every
            // green run is a line people learn to skip.
            history: history is not null && (history.Verdicts?.HasAnything == true || features.Any(f => (f.Scenarios ?? []).Any(s => s.Result == ExecutionResult.Failed && !s.NotATest)))
                ? history.Summary()
                : null)
            with
            {
                // Only when the run that was moved out of the way had failures: "nothing warned me" (#80),
                // answered at the moment the failing report stops being the one on top. A line about every
                // green run rotated is a line people learn to skip.
                PreviousRun = rotation is { KeptDirectory: { } keptDirectory, Kept.Failed: > 0 }
                    ? new RunSummaryPreviousRun(keptDirectory, rotation.Kept.Failed)
                    : null,
                QueryScriptWritten = written.Contains(QueryScriptGenerator.FileName)
            };

        // The CI writes below read the run's environment (getEnv), as the rest of the run does, and each is
        // isolated as the output list is: an exception in one costs a diagnostic, never Run.json or the pointer.
        var ciEnvironment = CiEnvironmentDetector.Detect(getEnv);
        // Who reads the CI text, and from where: off the runner, from the run's page or a download of its artifacts.
        var ciReader = CiReader.Detect(getEnv, options);
        if (options.WriteCiSummary)
        {
            string? markdown = null;
            RunIsolated(CiSummaryFileName, () =>
            {
                var (truncatedDiagrams, fullDiagrams) = DefaultDiagramsFetcher.GetCiSummaryDiagrams(fetcherOptions);
                markdown = CiSummaryGenerator.GenerateMarkdown(features, truncatedDiagrams, fullDiagrams, startRunTime, endRunTime, options.MaxCiSummaryDiagrams,
                    options.DiagramFormat, options.PlantUmlServerBaseUrl, options.LocalDiagramRenderer);

                // On GitHub the job summary is a file the runner reads rather than stdout, so unlike the console
                // pointer it survives the runners that swallow output; Azure DevOps takes it as a logging command on
                // stdout, which dotnet test swallows (plans/CI_SUMMARY_AUDIENCE_PLAN.md F10). A failing run's ends
                // with how to debug it, a green run's with where the report is.
                markdown += RunSummaryConsoleWriter.BuildCiSummarySection(runSummary, ciReader);

                var directory = CurrentReportsDirectory;
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, CiSummaryFileName), markdown);
                RunFileCollector.Record(Path.Combine(directory, CiSummaryFileName));
            });

            // Written even when CiSummary.md could not be: on GitHub the job summary is the channel that survives.
            if (markdown is { } summary)
                RunIsolated("the CI job summary", () => CiSummaryWriter.Write(summary, ciEnvironment, getEnv, File.AppendAllText, Console.WriteLine));
        }

        // The last file the run writes, after every other one: Run.json, naming the run and listing what
        // reached disk. Its presence therefore means "every output named here is in this directory", and
        // its absence — the previous one was removed before this run's first write — means the run did not
        // finish. Before the artifact publisher, which globs the directory and should find it.
        WriteRunManifest(reportsDir, new RunManifest
        {
            Run = runId,
            At = runEndedAt,
            Suite = suite,
            // the run's tests: a scenario that is not a test is in no count (plans/RUN_WINDOW_PLAN.md R1)
            Scenarios = features.Sum(f => (f.Scenarios ?? []).Count(s => !s.NotATest)),
            Failed = features.Sum(f => (f.Scenarios ?? []).Count(s => s.Result == ExecutionResult.Failed && !s.NotATest)),
            Partial = history?.Run.Partial,
            KronikolVersion = KronikolVersion
        });

        if (options.PublishCiArtifacts)
        {
            var ciReportsDir = CurrentReportsDirectory;
            if (Directory.Exists(ciReportsDir))
            {
                RunIsolated("the CI artifact list", () =>
                    CiArtifactPublisher.Publish(CiArtifactPublisher.ReportFiles(ciReportsDir), ciEnvironment, options.CiArtifactName, options.CiArtifactRetentionDays,
                        getEnv, File.AppendAllText, Console.WriteLine, File.Exists,
                        // The attachments Run.json lists, beside the kept runs.
                        [.. CiArtifactPublisher.RetainedFiles(ciReportsDir),
                            .. CiArtifactPublisher.AttachmentFiles(ciReportsDir, RunFileCollector.Current?.Snapshot().Attachments ?? [])]));
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
        if (options.WriteCiDebugSection && !options.WriteCiSummary && runSummary.Failures.Count > 0 && ciEnvironment != CiEnvironment.None)
        {
            RunIsolated("the CI debug section", () =>
            {
                var debugSection = RunSummaryConsoleWriter.BuildCiSummarySection(runSummary, ciReader);
                Console.WriteLine(debugSection);
                CiSummaryWriter.Write(debugSection, ciEnvironment, getEnv, File.AppendAllText, Console.WriteLine);
            });
        }

        // Last, so it is the final thing the run says.
        if (options.WriteRunSummaryToConsole)
            RunIsolated("the run summary", () => RunSummaryConsoleWriter.Write(runSummary, ciEnvironment, Console.WriteLine, Console.WriteLine, ciReader));
    }

    /// <summary>
    /// The top-level files this run is about to write, by name — asked once, by the rotation, about a
    /// directory that has no <c>Run.json</c> to say which files the run in it wrote (one written before
    /// manifests existed, or by a run killed before its own). Those are the files this run would overwrite,
    /// so they are what is moved out of the way. Never <c>CLAUDE.md</c> or <c>AGENTS.md</c>, which are
    /// spliced in place and describe the directory rather than the run.
    /// </summary>
    /// <remarks>
    /// Deliberately not what the manifest is built from: a plan names the file an output failure then
    /// prevents. The manifest lists what <see cref="RunFileCollector"/> saw reach disk.
    /// </remarks>
    private static List<string> PlannedFiles(ReportConfigurationOptions options, HistoryRunContext? history, string specsDataExtension, string testRunDataExtension)
    {
        var planned = new List<string>();
        if (options.GenerateTestRunReportData)
        {
            planned.Add($"{options.HtmlTestRunReportFileName}.{testRunDataExtension}");
            // The JSON data file's other name, which this run replaces: the run before a switch wrote that one.
            if (testRunDataExtension is "json" or "krkl")
                planned.Add($"{options.HtmlTestRunReportFileName}.{(testRunDataExtension == "json" ? "krkl" : "json")}");
        }
        if (options.GenerateTestRunReport)
            planned.Add($"{options.HtmlTestRunReportFileName}.html");
        if (options.GenerateTestRunReportSchema)
            planned.Add($"{options.HtmlTestRunReportFileName}.schema.{GetSchemaExtension(options.TestRunReportDataFormat)}");
        if (options.GenerateSpecificationsReport)
            planned.Add($"{options.HtmlSpecificationsFileName}.html");
        if (options.GenerateSpecificationsData)
            planned.Add($"{options.YamlSpecificationsFileName}.{specsDataExtension}");
        if (options.GenerateSpecificationsMarkdown)
            planned.Add($"{options.YamlSpecificationsFileName}.md");
        if (options.GenerateFailuresDigest)
        {
            planned.Add(FailuresDigestFileName);
            planned.Add(FailuresDigestJsonlFileName);
        }
        if (options.GenerateCtrfReport)
            planned.Add(CtrfReportGenerator.FileName);
        if (options.GenerateHistoryFragment && history is not null)
            planned.Add(HistoryFormat.FragmentFileName);
        if (options.GenerateComponentDiagram)
        {
            var componentFileName = (options.ComponentDiagramOptions ?? new ComponentDiagramOptions()).FileName;
            planned.Add($"{componentFileName}.html");
            planned.Add($"{componentFileName}.png");
            planned.Add($"{componentFileName}.svg");
        }
        if (options.DiagnosticMode)
            planned.Add(DiagnosticReportGenerator.FileName);
        if (options.WriteCiSummary)
            planned.Add(CiSummaryFileName);
        // Whenever the switch is on: whether the page has anything to show is known only once the run is analysed,
        // and a page an older run left behind is that run's to keep either way.
        if (options.GenerateLabsReport)
            planned.Add(LabsReportGenerator.FileName(options.HtmlTestRunReportFileName));
        return planned;
    }

    /// <summary>
    /// Writes <c>Run.json</c> with the files the run's <see cref="RunFileCollector"/> saw reach disk. Like
    /// every other output it costs a diagnostic when it cannot be written, never the run — and a run whose
    /// manifest is missing reads, correctly, as one that did not finish writing.
    /// </summary>
    private static void WriteRunManifest(string reportsDirectory, RunManifest manifest)
    {
        try
        {
            var (files, attachments) = RunFileCollector.Current?.Snapshot() ?? ([], []);
            (manifest with { Files = files, Attachments = attachments }).Write(reportsDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ReportDiagnosticsScope.Record(DiagnosticKind.OutputFailure, $"Could not write {RunManifest.FileName}", exception);
            Console.WriteLine($"⚠ WARNING: could not write {RunManifest.FileName}: {exception.GetType().Name}: {exception.Message}");
        }
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
            if (RunIsolated(output.Name, output.Run))
                lock (written)
                    written.Add(output.Name);
        })).ToArray());

        return written;
    }

    /// <summary>
    /// Runs one output the way <see cref="RunOutputs"/> runs each of its own: an exception becomes an
    /// <see cref="DiagnosticKind.OutputFailure"/> diagnostic and a console warning, and the run goes on.
    /// For the writes that come after the output list, where an exception used to end the run before
    /// <c>Run.json</c>, the artifact list and the pointer. Returns whether <paramref name="run"/> completed.
    /// </summary>
    private static bool RunIsolated(string name, Action run)
    {
        try
        {
            run();
            return true;
        }
        catch (Exception ex)
        {
            ReportDiagnosticsScope.Record(DiagnosticKind.OutputFailure, $"Could not write {name}", ex);
            Console.WriteLine($"⚠ WARNING: could not write {name}: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Called once the output list has run, before anything after it: a test's stand-in for a process killed at
    /// that point, since everything after the list is isolated and a failure there no longer ends the run.
    /// Null outside tests.
    /// </summary>
    internal static readonly AsyncLocal<Action?> AfterOutputsForTests = new();

    /// <summary>The job summary's copy in the reports directory, written when <see cref="ReportConfigurationOptions.WriteCiSummary"/> is on.</summary>
    internal const string CiSummaryFileName = "CiSummary.md";

    /// <summary>
    /// The directory a writer that is handed the options, rather than called through <see cref="WriteFile(string, string, bool)"/>,
    /// writes into: the run's own while one is in flight (or a merge's scoped one), else the one the options name.
    /// </summary>
    internal static string ReportsDirectoryFor(ReportConfigurationOptions options) =>
        ActiveReportsDirectory.Value ?? ResolveReportsDirectory(options);

    /// <summary>
    /// Records an <see cref="DiagnosticKind.OptionNotApplied"/> for each option the run's rendering mode ignores:
    /// a PlantUML theme under <see cref="PlantUmlRendering.BrowserJs"/> or <see cref="PlantUmlRendering.NodeJs"/>,
    /// which load the engine without its theme bundle. Called after the zero-scenario guard, so a discovery pass
    /// does not warn, and before the diagnostics snapshot, so the entry reaches the data files. The entry is the
    /// channel; the console line is a courtesy, since most test runners swallow library output.
    /// </summary>
    private static void RecordOptionDiagnostics(ReportConfigurationOptions options)
    {
        if (options.PlantUmlRendering is not (PlantUmlRendering.BrowserJs or PlantUmlRendering.NodeJs))
            return;

        if (!string.IsNullOrWhiteSpace(options.PlantUmlTheme))
            RecordThemeNotApplied(nameof(ReportConfigurationOptions.PlantUmlTheme), options.PlantUmlTheme, options.PlantUmlRendering);

        if (options.GenerateComponentDiagram && options.ComponentDiagramOptions?.PlantUmlTheme is { } componentTheme
            && !string.IsNullOrWhiteSpace(componentTheme))
            RecordThemeNotApplied($"{nameof(ReportConfigurationOptions.ComponentDiagramOptions)}.{nameof(ComponentDiagram.ComponentDiagramOptions.PlantUmlTheme)}",
                componentTheme, options.PlantUmlRendering);
    }

    /// <summary>
    /// Whether this run writes <c>query.cs</c>: asked for, and with a JSON data file for it to query, since the
    /// engine reads JSON only. Where it goes is <see cref="QueryScriptGenerator.CompilingProject"/>'s call.
    /// </summary>
    private static bool WritesQueryScript(ReportConfigurationOptions options) =>
        options.WriteQueryScript && options.GenerateTestRunReportData && options.TestRunReportDataFormat == DataFormat.Json;

    private static void RecordThemeNotApplied(string option, string theme, PlantUmlRendering mode)
    {
        var what = mode == PlantUmlRendering.BrowserJs
            ? "so every diagram is drawn unthemed and the browser console says \"themes.js could not be loaded\""
            : "so every diagram is drawn unthemed; the engine's warning goes to the renderer's stderr, which a successful render does not keep";
        var message = $"{option} \"{theme}\" has no effect under PlantUmlRendering.{mode}: the report's renderer loads the engine " +
                      $"without its theme bundle, {what}. Server and Local apply a theme; leave {option} unset to silence this.";
        ReportDiagnosticsScope.Record(DiagnosticKind.OptionNotApplied, message);
        Console.WriteLine("⚠ WARNING: " + message);
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
    /// agree before the first click. Report and scenario level differ in their handlers, and in the diagrams their titles
    /// say they act on (5.0.0, plans/OPTION_C_PLAN.md D49, <see cref="PayloadsWhere"/>). The line-count select is named
    /// "Payload length", its group's heading, as the format and width selects are named by theirs.
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
            $"<option value=\"{(int)v}\"{(v == toggles.TruncateLines ? " selected" : "")}>{(int)v} lines</option>"));
        var diagrams = PayloadsWhere(scenarioLevel);
        return $"<span class=\"details-radio\"><span class=\"details-radio-label\" title=\"How the payloads in {diagrams} are shown (the boxes beside the arrows, holding each request, response and message, with its headers): whole, folded to one line, or cut at a line limit\">Payloads</span>"
            + $"<button class=\"details-radio-btn{Active(ReportDetailsState.Expanded)}\" data-state=\"expanded\" onclick=\"{Click("expanded")}\" title=\"The payloads in {diagrams} show in full{(toggles.PayloadScrollLines > 0 ? "; a long one scrolls in its box" : "")}\">{BuildButtonLabel("Expanded")}</button>"
            + $"<button class=\"details-radio-btn{Active(ReportDetailsState.Collapsed)}\" data-state=\"collapsed\" onclick=\"{Click("collapsed")}\" title=\"The payloads in {diagrams} fold to one line each; a box's + button opens it\">{BuildButtonLabel("Collapsed")}</button>"
            + $"<button class=\"details-radio-btn{Active(ReportDetailsState.Truncated)}\" data-state=\"truncated\" onclick=\"{Click("truncated")}\" title=\"Payloads in {diagrams} longer than the line limit beside this are cut at that many lines\">{BuildButtonLabel("Truncated")}</button>"
            + $"<select class=\"truncate-lines-select\" autocomplete=\"off\" aria-label=\"Payload length\" title=\"How many lines a cut payload box keeps in {diagrams} (with Truncated picked)\"{disabled} onchange=\"{change}\"><optgroup label=\"Payload length\">{options}</optgroup></select></span>";
    }

    /// <summary>
    /// A payload control's label (5.0.0): the words in a span that also holds them, unseen, in bold (its
    /// <c>data-label</c>, drawn by the stylesheet), so a control is as wide off as on and switching it does not move the
    /// ones beside it. The toggle code writes a label back into this span, never over the button.
    /// </summary>
    private static string BuildButtonLabel(string text) => $"<span class=\"btn-label\" data-label=\"{text}\">{text}</span>";

    /// <summary>
    /// One filter toggle (headers / assertions / steps / databases): its bare word, with the configured
    /// start state in its class, <c>data-shown</c> and <c>aria-pressed</c>, which <c>syncToggleBtn</c>
    /// keeps on every later click (5.0.0; the label no longer changes), titled with what it shows or hides and in which
    /// diagrams (D49).
    /// </summary>
    private static string BuildFilterToggleButton(string toggleName, bool shown, bool scenarioLevel)
    {
        var label = char.ToUpperInvariant(toggleName[0]) + toggleName[1..];
        var handler = scenarioLevel ? $"window._toggleScenario{label}(this)" : $"window._toggle{label}(this)";
        var on = shown ? "true" : "false";
        return $"<button class=\"details-radio-btn toggle-btn{(shown ? " details-active" : "")}\" data-toggle=\"{toggleName}\" data-shown=\"{on}\" aria-pressed=\"{on}\" onclick=\"{handler}\" title=\"{ToggleTitle(toggleName, PayloadsWhere(scenarioLevel))}\">{BuildButtonLabel(label)}</button>";
    }

    /// <summary>
    /// The diagrams a payload control acts on, as its title names them (5.0.0, plans/OPTION_C_PLAN.md D49): the report
    /// bar's act on every sequence diagram in the report, a scenario's own bar on that scenario's only.
    /// </summary>
    private static string PayloadsWhere(bool scenarioLevel) =>
        scenarioLevel ? "this scenario's sequence diagrams" : "every sequence diagram in the report";

    /// <summary>What a filter toggle shows or hides in <paramref name="diagrams"/> (D49).</summary>
    private static string ToggleTitle(string toggleName, string diagrams) => toggleName switch
    {
        "headers" => $"Show or hide the headers at the top of each payload in {diagrams}",
        "assertions" => $"Show or hide the boxes with the test's assertions in {diagrams}",
        "steps" => $"Show or hide the bars marking where each step starts in {diagrams}",
        "databases" => $"Show or hide the databases and their calls in {diagrams}",
        "events" => $"Show or hide the calls to and from message brokers (the participants drawn as queues), with their payloads, in {diagrams}",
        "setup" => $"Show or hide the setup in {diagrams}: everything before the first When step, or the Setup section where the report keeps setup apart",
        _ => throw new ArgumentOutOfRangeException(nameof(toggleName), toggleName, "a toggle with no title")
    };

    /// <summary>The payload format select's title (D49).</summary>
    private static string NoteFormatTitle(bool scenarioLevel) =>
        $"How the payloads in {PayloadsWhere(scenarioLevel)} are written: JSON as captured, or the same data as YAML";

    /// <summary>The payload width select's title, which spells out both states (D49).</summary>
    private static string NoteWidthTitle(bool scenarioLevel) =>
        $"How wide the payloads are in {PayloadsWhere(scenarioLevel)}. Wrap: they wrap at a set width. Wide: they widen to fill the diagram.";

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
    /// The scenario-level payload controls, in three zones (5.0.0, D30): the Payloads radio, the format and width
    /// dropdowns, the toggles. Built once per report and written by <see cref="AppendDiagramSectionHead"/> into every
    /// scenario's settings, so they cannot drift (the same contract as the note-format select).
    /// </summary>
    private static string BuildScenarioDiagramToolbar(ResolvedToggleDefaults toggles,
        bool includeAssertions, bool includeSteps, bool includeDatabases, bool includeEvents, bool includeSetup,
        string scenarioNoteFormatSelect, string scenarioNoteWidthSelect = "")
    {
        var sb = new StringBuilder();
        sb.Append(BuildDetailsRadio(toggles, scenarioLevel: true));
        if (scenarioNoteFormatSelect.Length + scenarioNoteWidthSelect.Length > 0)
            sb.Append("<span class=\"diagram-format\">").Append(scenarioNoteFormatSelect).Append(scenarioNoteWidthSelect).Append("</span>");
        sb.Append("<span class=\"diagram-flags\">");
        sb.Append(BuildFilterToggleButton("headers", toggles.HeadersShown, scenarioLevel: true));
        if (includeAssertions)
            sb.Append(BuildFilterToggleButton("assertions", toggles.AssertionsShown, scenarioLevel: true));
        if (includeSteps)
            sb.Append(BuildFilterToggleButton("steps", toggles.StepsShown, scenarioLevel: true));
        if (includeDatabases)
            sb.Append(BuildFilterToggleButton("databases", toggles.DatabasesShown, scenarioLevel: true));
        if (includeEvents)
            sb.Append(BuildFilterToggleButton("events", toggles.EventsShown, scenarioLevel: true));
        if (includeSetup)
            sb.Append(BuildFilterToggleButton("setup", toggles.SetupShown, scenarioLevel: true));
        sb.Append("</span>");
        return sb.ToString();
    }

    /// <summary>
    /// What the page's Setup filter (stripSetup in collapsible-notes-script.js) finds a setup by: the Setup frame the
    /// SeparateSetup option writes, or a When step bar in either of the forms
    /// <see cref="PlantUml.StepBarPlantUml"/> writes.
    /// </summary>
    private static readonly Regex SetupMarker = new(
        @"^[ \t]*(?:partition\b.*\bSetup\r?$|hnote across <<stepDelimiter>>(?: #black:<color:white>|<<stepBody>>: )(?:<b>)?when\b)",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private const string DiagramTabsOpen = "<div class=\"diagram-toggle diagram-toggle-tabs\" role=\"tablist\" aria-label=\"Diagram views\">";

    /// <summary>The cog the settings gear draws: eight teeth around a ring, the hole cut by the even-odd rule.</summary>
    private const string ScenarioSettingsCog = "M6.81 -2.33L9.41 -1.91L9.41 1.91L6.81 2.33L6.47 3.17L8.00 5.30L5.30 8.00L3.17 6.47L2.33 6.81L1.91 9.41L-1.91 9.41L-2.33 6.81L-3.17 6.47L-5.30 8.00L-8.00 5.30L-6.47 3.17L-6.81 2.33L-9.41 1.91L-9.41 -1.91L-6.81 -2.33L-6.47 -3.17L-8.00 -5.30L-5.30 -8.00L-3.17 -6.47L-2.33 -6.81L-1.91 -9.41L1.91 -9.41L2.33 -6.81L3.17 -6.47L5.30 -8.00L8.00 -5.30L6.47 -3.17Z M3.4 0A3.4 3.4 0 1 0 -3.4 0A3.4 3.4 0 1 0 3.4 0Z";

    private const string ScenarioSettingsGear = "<button type=\"button\" class=\"scenario-settings-gear\" aria-label=\"This scenario's settings\" aria-expanded=\"false\" title=\"This scenario's payload and diagram settings, to set it apart from the report's\">"
        + "<svg viewBox=\"-12 -12 24 24\" aria-hidden=\"true\" focusable=\"false\"><path d=\"" + ScenarioSettingsCog + "\" fill-rule=\"evenodd\"/></svg></button>";

    /// <summary>The same settings' gear on the section's title line, which a phone shows in place of the one above.</summary>
    private const string ScenarioSettingsSummaryGear = "<button type=\"button\" class=\"scenario-diagram-controls-toggle gear-in-summary\" aria-label=\"Diagram settings\" title=\"Diagram settings\" aria-expanded=\"false\">\u2699</button>";

    private static string BuildDiagramTab(string dtype, string label, bool active) =>
        $"<button class=\"diagram-toggle-btn{(active ? " diagram-toggle-active" : "")}\" role=\"tab\" aria-selected=\"{(active ? "true" : "false")}\" tabindex=\"{(active ? "0" : "-1")}\" data-dtype=\"{dtype}\" title=\"{DiagramViewTitle(dtype)}\">{label}</button>";

    /// <summary>
    /// What a diagram view shows (5.0.0, plans/OPTION_C_PLAN.md D49): its tab's title, and the section's heading's where
    /// the section has that view alone.
    /// </summary>
    private static string DiagramViewTitle(string dtype) => dtype switch
    {
        "seq" => "The calls between the test and the services, in order",
        "activity" => "The work inside the services, span by span, as an activity diagram",
        _ => "Where the time went inside the services, span by span, as nested bars"
    };

    /// <summary>
    /// A diagram section's head (5.0.0, plans/OPTION_C_PLAN.md D29 to D34), for a scenario and for a parameterized group:
    /// the section's title; the tabs of its views on a bar of their own, when it has more than one; and the scenario's
    /// settings, the options bar behind a gear, which act on sequence diagrams only and so are written where the section
    /// has some and the page draws them (<paramref name="settingsControls"/> is empty otherwise). The bar starts with
    /// "This scenario:", so it reads apart from the report's own; on a phone the gear on the title opens it. A section
    /// that opens on another view starts with its settings out of sight (<c>diagrams-not-seq</c>, which
    /// toggle-script.js keeps as the tabs change). The warning goes on the tabs bar, or on the title when there is no bar.
    /// </summary>
    private static void AppendDiagramSectionHead(StringBuilder body, bool open, bool hasSeq, bool hasActivity, bool hasFlame,
        DiagramTabKind activeTab, string settingsControls, string warning)
    {
        var views = (hasSeq ? 1 : 0) + (hasActivity ? 1 : 0) + (hasFlame ? 1 : 0);
        var hasSettings = hasSeq && settingsControls.Length > 0;
        var notSeq = hasSettings && activeTab != DiagramTabKind.Sequence;
        body.Append($"<details class=\"example-diagrams{(notSeq ? " diagrams-not-seq" : "")}\"{(open ? " open" : "")}>");
        var title = views > 1 ? "Diagrams" : hasSeq ? "Sequence Diagrams" : hasActivity ? "Activity Diagrams" : "Flame Chart";
        var tooltip = views > 1 ? "This scenario's diagrams" : hasSeq ? "This scenario's sequence diagrams" : DiagramViewTitle(hasActivity ? "activity" : "flame");
        body.Append($"<summary class=\"h4\" title=\"{tooltip}\">").Append(title);
        if (hasSettings) body.Append(ScenarioSettingsSummaryGear);
        if (views == 1) body.Append(warning);
        body.Append("</summary>");
        if (views > 1)
        {
            body.Append(DiagramTabsOpen);
            if (hasSeq) body.Append(BuildDiagramTab("seq", "Sequence Diagrams", activeTab == DiagramTabKind.Sequence));
            if (hasActivity) body.Append(BuildDiagramTab("activity", "Activity Diagrams", activeTab == DiagramTabKind.Activity));
            if (hasFlame) body.Append(BuildDiagramTab("flame", "Flame Chart", activeTab == DiagramTabKind.FlameChart));
            body.Append(warning).Append("</div>");
        }
        if (hasSettings)
            body.Append("<div class=\"scenario-settings\"><div class=\"diagram-toggle diagram-options-bar\"><span class=\"scenario-settings-label\">This scenario:</span>")
                .Append(settingsControls).Append("</div>").Append(ScenarioSettingsGear).Append("</div>");
    }

    /// <summary>
    /// The note appearance dropdowns (payload font, note width), emitted beside the filter toggles
    /// at report and scenario level and built once so the toolbar variants cannot drift — the same
    /// contract as the JSON/YAML select. Both are <see cref="PlantUmlRendering.BrowserJs"/>-only:
    /// they re-render the diagram client-side, which no other rendering mode can do.
    /// <para>
    /// The controls are label-free to stay compact, so the options sit under an
    /// <c>&lt;optgroup&gt;</c> whose heading names the control once it is open (and to a screen
    /// reader); <paramref name="title"/> spells out both states. The heading is not free: Chromium
    /// indents grouped options and the closed select measured 15px wider (Firefox: 0).
    /// </para>
    /// </summary>
    private static string BuildNoteAppearanceSelect(string cssClass, string label, string title, string handler,
        (string Value, string Text)[] options, string selected) =>
        $"<select class=\"{cssClass}\" autocomplete=\"off\" aria-label=\"{label}\" title=\"{title}\" onchange=\"window.{handler}(this)\">"
        + $"<optgroup label=\"{label}\">"
        + string.Concat(options.Select(o =>
            $"<option value=\"{o.Value}\"{(o.Value == selected ? " selected" : "")}>{o.Text}</option>"))
        + "</optgroup></select>";

    /// <summary>
    /// Renders one HTML report from the diagrams and features handed to it, into the current reports directory under
    /// <paramref name="fileName"/>, and returns the path written. A run calls it through
    /// <c>CreateStandardReportsWithDiagrams</c>, with every option read from <see cref="ReportConfigurationOptions"/>.
    /// </summary>
    /// <remarks>
    /// It draws what it is handed: verdicts in <paramref name="history"/> draw the per-scenario sparkline, verdict pill
    /// and <c>data-history-verdicts</c> attribute, as they did before 4.6.0, and the History tab with
    /// <paramref name="showHistorySection"/>; <paramref name="diagnostics"/> draw the Diagnostics tab with
    /// <paramref name="showReportDiagnostics"/>. Each brings its stylesheet rules and search code with it. A run
    /// hands it history only when <see cref="ReportConfigurationOptions.ShowScenarioHistory"/> or
    /// <see cref="ReportConfigurationOptions.ShowHistorySection"/> asks.
    /// </remarks>
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
        BackgroundCalls? background = null,
        int browserRenderWorkers = Constants.TrackingDefaults.BrowserRenderWorkers,
        int browserRenderCacheMegabytes = Constants.TrackingDefaults.BrowserRenderCacheMegabytes,
        int browserFragmentMaxHeight = Constants.TrackingDefaults.BrowserFragmentMaxHeight,
        bool separateBackgroundSteps = false,
        bool collapseRepeatedStepKeywords = true,
        NotePayloadFormat notePayloadFormat = NotePayloadFormat.Yaml,
        bool fullSearchIndex = true,
        SearchIndex.SearchIndexBuildCache? searchIndexCache = null,
        ResolvedToggleDefaults? toggleDefaults = null,
        string? suite = null,
        HistoryVerdicts? history = null,
        bool showHistorySection = false,
        bool showReportDiagnostics = false,
        bool renderCollapsedSubStepsOnExpand = true) =>
        GenerateHtmlReportCore(
            diagrams: diagrams,
            features: features,
            startRunTime: startRunTime,
            endRunTime: endRunTime,
            stylesheet: stylesheet,
            fileName: fileName,
            title: title,
            includeTestRunData: includeTestRunData,
            generateBlankOnFailedTests: generateBlankOnFailedTests,
            lazyLoadImages: lazyLoadImages,
            diagramFormat: diagramFormat,
            plantUmlRendering: plantUmlRendering,
            inlineSvgRendering: inlineSvgRendering,
            internalFlowTracking: internalFlowTracking,
            internalFlowDataScript: internalFlowDataScript,
            wholeTestSegments: wholeTestSegments,
            trackedLogs: trackedLogs,
            wholeTestVisualization: wholeTestVisualization,
            ciMetadata: ciMetadata,
            showStepNumbers: showStepNumbers,
            customCss: customCss,
            customFaviconBase64: customFaviconBase64,
            customLogoHtml: customLogoHtml,
            groupParameterizedTests: groupParameterizedTests,
            maxParameterColumns: maxParameterColumns,
            titleizeParameterNames: titleizeParameterNames,
            componentDiagramPlantUml: componentDiagramPlantUml,
            precomputedWholeTestContent: precomputedWholeTestContent,
            showNoInteractionsMarker: showNoInteractionsMarker,
            diagnostics: diagnostics,
            background: background,
            browserRenderWorkers: browserRenderWorkers,
            browserRenderCacheMegabytes: browserRenderCacheMegabytes,
            browserFragmentMaxHeight: browserFragmentMaxHeight,
            separateBackgroundSteps: separateBackgroundSteps,
            collapseRepeatedStepKeywords: collapseRepeatedStepKeywords,
            notePayloadFormat: notePayloadFormat,
            fullSearchIndex: fullSearchIndex,
            searchIndexCache: searchIndexCache,
            toggleDefaults: toggleDefaults,
            suite: suite,
            history: history,
            showHistorySection: showHistorySection,
            showReportDiagnostics: showReportDiagnostics,
            renderCollapsedSubStepsOnExpand: renderCollapsedSubStepsOnExpand,
            // A caller that hands this method verdicts gets the per-scenario history, as before 4.6.0.
            showScenarioHistory: history is not null);

    /// <summary>
    /// <see cref="GenerateHtmlReport"/> with the run report's component diagram as the report's renderer drew it
    /// (<see cref="ComponentDiagramReportGenerator.DrawEmbedded"/>), which the page shows in its panel in place of drawing
    /// <paramref name="componentDiagramPlantUml"/> itself, and with <paramref name="componentDiagram"/>, the diagram as the
    /// Components tab draws it: its source stands for <paramref name="componentDiagramPlantUml"/>, and where the page draws
    /// it, the tab's controls redraw it from its parts (5.0.0, plans/OPTION_C_PLAN.md S11, D28).
    /// </summary>
    internal static string GenerateHtmlReportCore(DefaultDiagramsFetcher.DiagramAsCode[] diagrams,
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
        BackgroundCalls? background = null,
        int browserRenderWorkers = Constants.TrackingDefaults.BrowserRenderWorkers,
        int browserRenderCacheMegabytes = Constants.TrackingDefaults.BrowserRenderCacheMegabytes,
        int browserFragmentMaxHeight = Constants.TrackingDefaults.BrowserFragmentMaxHeight,
        bool separateBackgroundSteps = false,
        bool collapseRepeatedStepKeywords = true,
        NotePayloadFormat notePayloadFormat = NotePayloadFormat.Yaml,
        bool fullSearchIndex = true,
        SearchIndex.SearchIndexBuildCache? searchIndexCache = null,
        ResolvedToggleDefaults? toggleDefaults = null,
        string? suite = null,
        HistoryVerdicts? history = null,
        bool showHistorySection = false,
        bool showReportDiagnostics = false,
        bool renderCollapsedSubStepsOnExpand = true,
        ComponentDiagramReportGenerator.DrawnDiagram? componentDiagramDrawn = null,
        bool showScenarioHistory = false,
        WarmUpResult? warmUp = null,
        EmbeddedComponentDiagram? componentDiagram = null)
    {
        // The Specifications page describes the tests, so a scenario that is not a test is not in it.
        if (!includeTestRunData)
            features = SpecifiedFeatures(features);

        if (generateBlankOnFailedTests && CannotVouchForSpecifications(features))
            return WriteFile(string.Empty, fileName);

        // The run's first-call warm-up (plans/WARM_UP_PLAN.md R2): a run hands in what it found once for every output, a
        // merge what its shards carried; a caller with the calls alone gets the same reading. A page with no mark is
        // written as it was without them.
        warmUp ??= trackedLogs is null ? WarmUpResult.None : WarmUpCalls.Find(features, trackedLogs);
        var warmUpByScenario = warmUp.Calls.Values.ToLookup(m => m.ScenarioId, StringComparer.Ordinal);

        // The Components tab's diagram with the parts its controls draw it from: its source is the one the tab draws.
        if (componentDiagram is not null)
            componentDiagramPlantUml = componentDiagram.PlantUml;

        // Defaulted here as well as in the data writers, and for the same reason: the HTML's
        // data-stable-id attributes and the data file's stableId values are the same identity, and a
        // caller that reaches one writer directly must not get a different answer from the other.
        suite ??= RunSuite.Current;

        // The resolved toggle-defaults record wins when provided; the legacy notePayloadFormat
        // parameter (kept for compatibility — tests and helpers call it directly) folds into the
        // built-ins otherwise.
        var toggles = toggleDefaults ?? ResolvedToggleDefaults.BuiltIn with { NotePayloadFormat = notePayloadFormat };
        // whatever draws the diagrams: a value out of range fails the report as the option's documentation says
        ReportToggleDefaultsResolver.CheckPayloadScrollLines(toggles.PayloadScrollLines);

        // The per-scenario history (sparkline, pill, verdict attribute) is drawn only when the report asks for it
        // (ReportConfigurationOptions.ShowScenarioHistory); the section has a switch of its own. The $verdict search
        // code ships only with the attributes it reads.
        var scenarioHistory = showScenarioHistory ? history : null;
        var drawsVerdicts = scenarioHistory is not null;

        var scenarioFeatureMapHelper = LoadScript("report-scenario-feature-map-helper.js", drawsVerdicts);

        // Shared gzip+base64 decompressor — always included; several conditionally-emitted
        // scripts (context menu, internal-flow popup, deep search) call it in rendering modes
        // where the BrowserJs render script is absent.
        var decompressHelper = LoadResource("report-decompress-helper.js");

        // The Tags row's start mode, which the chip script and the row's switch both carry (plans/TAGS_PLAN.md §4.11).
        var tagModeText = toggles.TagFilterMode == FilterCombinationMode.And ? "AND" : "OR";
        // and each row's ways, two-way (5.10.0, plans/REPORT_UI_FIXES_PLAN.md C13): the script and the row's switch both carry them
        var filterChipsScript = LoadResource("report-filter-chips-script.js").Replace("__TAG_MODE_DEFAULT__", tagModeText)
            .Replace("__TAG_WAYS_DEFAULT__", ChipWaysDefault).Replace("__DEP_WAYS_DEFAULT__", ChipWaysDefault);
        var filterBarScript = LoadResource("report-filter-bar-script.js");
        var searchFunction = LoadScript("report-search-function.js", drawsVerdicts);

        // Deep search ("search everything") client — always included; it no-ops when the
        // kron-search-index blob is absent (FullSearchIndex=false / older reports).
        var searchIndexClientScript = LoadScript("report-search-index.js", drawsVerdicts);

        // The filter-mode scripts seed their AND/OR start mode from the resolved toggle defaults;
        // the substitution happens on the returned copy, never in the resource cache.
        var depModeText = toggles.DependencyFilterMode == FilterCombinationMode.Or ? "OR" : "AND";
        var dependencyFilterFunction = LoadResource("report-dependency-filter-function.js")
            .Replace("__DEP_MODE_DEFAULT__", depModeText);

        var statusFilterFunction = LoadResource("report-status-filter-function.js");

        // Collapse/Expand All
        var featuresToolsScript = LoadResource("report-features-tools-script.js");

        var lightboxFunction = LoadResource("report-lightbox-function.js");

        var toggleTableRefFunction = LoadResource("report-toggle-table-ref-function.js");

        var sortTableFunction = LoadResource("report-sort-table-function.js");



        // Copy scenario name, through the shared clipboard writer (a page without a secure context has no navigator.clipboard)
        var copyScenarioNameFunction = LoadResource("report-copy-text-function.js") + "\n" + LoadResource("report-copy-scenario-name-function.js");

        // Parameterized row selection
        var selectRowFunction = LoadResource("report-select-row-function.js");

        // Step status tooltips, held once for the page
        var stepStatusTitlesScript = StepStatusTitlesScript();
        var subStepsOnExpandScript = LoadResource("report-sub-steps-on-expand.js");

        // Toggle between grouped and flat parameter tables
        var toggleFlattenParamsJs = LoadResource("report-toggle-flatten-params-js.js");

        // R4: param-expand toggle auto-selects row + cell-subtable click isolation
        var paramExpandJs = LoadResource("report-param-expand-js.js");

        // Jump to failure
        // A scenario that is not a test is no failure of the run's and has no test time (plans/RUN_WINDOW_PLAN.md R1).
        var hasFailures = features.SelectMany(f => f.Scenarios).Any(s => s.Result == ExecutionResult.Failed && !s.NotATest);
        var failureCount = features.SelectMany(f => f.Scenarios).Count(s => s.Result == ExecutionResult.Failed && !s.NotATest);
        var jumpToFailureFunction = LoadResource("report-jump-to-failure-function.js");

        // Duration filter
        var hasDurations = features.SelectMany(f => f.Scenarios).Any(s => s.Duration.HasValue && !s.NotATest);
        var durationFilterFunction = LoadResource("report-duration-filter-function.js");

        // Export filtered view
        var exportFunction = LoadResource("report-export-function.js");

        // Persistent filter state
        // No-op stubs (localStorage persistence removed)
        var persistentFilterFunction = LoadResource("report-persistent-filter-function.js");

        // URL-anchored filters
        var urlHashFunction = LoadResource("report-url-hash-function.js");

        // Back and Forward as the report's navigation (5.2.0, plans/BACK_BUTTON_NAVIGATION_PLAN.md): the one script that
        // writes the session history, in every page these scripts run in
        var navigationScript = LoadResource("report-navigation-script.js");
        // the search button and the query builder (plans/SEARCH_PLAN.md 4.2, 4.3)
        var searchUiScript = LoadResource("report-search-ui-script.js");
        // the Search tab (4.5)
        var searchResultsScript = LoadResource("report-search-results-script.js");
        var searchHitScript = LoadResource("report-search-hit-script.js");
        // the marks a match is shown with where it is drawn (4.6.5)
        var searchMarksScript = LoadResource("report-search-marks-script.js");

        // Keyboard navigation
        var keyboardNavigationFunction = LoadResource("report-keyboard-navigation-function.js");

        // Deep link + init script
        var initScript = LoadResource("report-init-script.js");

        // The base sheet first; the custom sheet (the specifications theme, or a user's) goes after the
        // component sheets in the <style> block below, so it wins at equal specificity. The trailing
        // newline is the line the custom sheet used to occupy here: kept, so a report without one keeps
        // its bytes.
        // The rules of the history views and of the diagnostics section ship only with the markup they style.
        // The token sheet opens them all (5.0.0): every sheet after it, a theme's or CustomCss included, can use or
        // set the tokens.
        var combinedStylesheet = Stylesheets.ReportTokensStyleSheet
                                 + Stylesheets.HtmlReportStyleSheet
                                 + Stylesheets.SearchStyleSheet
                                 + (history is not null && (showHistorySection || drawsVerdicts) ? Stylesheets.HistoryStyleSheet : "")
                                 + (includeTestRunData && showReportDiagnostics && diagnostics is { Count: > 0 } ? Stylesheets.ReportDiagnosticsStyleSheet : "")
                                 + (warmUp.ScenarioMs.Count > 0 ? Stylesheets.WarmUpStyleSheet : "")
                                 + "\n";
        var themeStyles = string.IsNullOrEmpty(stylesheet) ? "" : "\n" + stylesheet;

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
        // The Events and Setup toggles (5.0.0, D38): a message broker is a participant drawn as a queue; a setup is one the
        // page's stripSetup finds, a Setup frame or a When step bar with the setup before it.
        var hasEventParticipants = isPlantUmlBrowser && (
            (trackedLogs is not null && trackedLogs.Any(l => l.PlantUml is not null && l.PlantUml.Contains("\nqueue \""))) ||
            diagrams.Any(d => d.CodeBehind.Contains("\nqueue \"")));
        var hasSetup = isPlantUmlBrowser && (
            (trackedLogs is not null && trackedLogs.Any(l => l.PlantUml is not null && SetupMarker.IsMatch(l.PlantUml))) ||
            diagrams.Any(d => SetupMarker.IsMatch(d.CodeBehind)));
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
        // carry its meaning instead. Since 5.0.0 it takes an <optgroup> heading,
        // "Payload format", like the width and line-count selects: the owner's
        // choice, over the 15px of closed-state width Chromium adds to a select
        // whose options are grouped.
        var yamlDefaultSelected = toggles.NotePayloadFormat == NotePayloadFormat.Yaml;
        var noteFormatOptions = $"<option value=\"json\"{(yamlDefaultSelected ? "" : " selected")}>JSON</option><option value=\"yaml\"{(yamlDefaultSelected ? " selected" : "")}>YAML</option>";
        var reportNoteFormatSelect = hasJsonNotePayloads
            ? $"<select class=\"note-format-select\" autocomplete=\"off\" aria-label=\"Payload format\" title=\"{NoteFormatTitle(scenarioLevel: false)}\" onchange=\"window._setNoteFormat(this)\"><optgroup label=\"Payload format\">{noteFormatOptions}</optgroup></select>"
            : "";
        var scenarioNoteFormatSelect = hasJsonNotePayloads
            ? $"<select class=\"note-format-select\" autocomplete=\"off\" aria-label=\"Payload format\" title=\"{NoteFormatTitle(scenarioLevel: true)}\" onchange=\"window._setScenarioNoteFormat(this)\"><optgroup label=\"Payload format\">{noteFormatOptions}</optgroup></select>"
            : "";
        // The note appearance controls need notes to act on, and nothing more: unlike the JSON/YAML
        // select they apply to any payload, so the gate is simply "this report draws notes".
        var hasDiagramNotes = isPlantUmlBrowser && (
            (trackedLogs is not null && trackedLogs.Any(l => l.PlantUml is not null && l.PlantUml.Contains("\nnote "))) ||
            diagrams.Any(d => d.CodeBehind.Contains("\nnote left") || d.CodeBehind.Contains("\nnote right")));
        // "Wrap" is the start state (notes wrap at DiagramNoteWrapWidth); "Wide" lifts the ceiling to
        // the diagram's width. The option VALUES are what the handlers and NoteWidthMode key on.
        (string, string)[] noteWidthOptions = [("default", "Wrap"), ("full", "Wide")];
        var widthSelected = toggles.NoteWidth == NoteWidthMode.Full ? "full" : "default";
        var reportNoteWidthSelect = hasDiagramNotes
            ? BuildNoteAppearanceSelect("note-width-select", "Payload width", NoteWidthTitle(scenarioLevel: false), "_setNoteWidth", noteWidthOptions, widthSelected)
            : "";
        var scenarioNoteWidthSelect = hasDiagramNotes
            ? BuildNoteAppearanceSelect("note-width-select", "Payload width", NoteWidthTitle(scenarioLevel: true), "_setScenarioNoteWidth", noteWidthOptions, widthSelected)
            : "";
        // The scenario-level control run, built once (see BuildScenarioDiagramToolbar).
        var scenarioToolbarControls = BuildScenarioDiagramToolbar(toggles,
            hasAssertionNotes, hasStepDelimiters, hasDatabaseParticipants, hasEventParticipants, hasSetup,
            scenarioNoteFormatSelect, scenarioNoteWidthSelect);
        // A report drawn when it was written (NodeJs, Server, Local) still has views only the page draws: the Activity
        // tab and the internal-flow popups, whose diagrams are one per scenario and one per call. It carries the engine
        // on demand for them, fetched the first time one is shown; until 3.31.8 it carried none and they stayed blank,
        // and a NodeJs diagram's links, drawn as text, opened nothing (DIAGRAM_COLOURS_PLAN §12.6).
        // A component diagram nobody drew for it (a caller that passed only its source) is the page's to draw as well.
        var drawsOnDemand = !isPlantUmlBrowser
            && (internalFlowTracking || (!string.IsNullOrEmpty(componentDiagramPlantUml) && componentDiagramDrawn is null));
        var plantUmlBrowserScript = isPlantUmlBrowser || drawsOnDemand
            ? DiagramContextMenu.GetPlantUmlBrowserRenderScript(browserRenderWorkers, browserRenderCacheMegabytes, browserFragmentMaxHeight, onDemand: drawsOnDemand)
            : "";
        var collapsibleNotesScript = isPlantUmlBrowser ? DiagramContextMenu.GetCollapsibleNotesScript(toggles) : "";
        var collapsibleNotesStyles = isPlantUmlBrowser ? DiagramContextMenu.GetCollapsibleNotesStyles() : "";
        var contextMenuScript = hasInteractiveDiagrams || internalFlowTracking ? DiagramContextMenu.GetContextMenuScript() : "";
        var contextMenuStyles = hasInteractiveDiagrams || internalFlowTracking ? DiagramContextMenu.GetStyles() : "";
        var inlineSvgStyles = (isInlineSvg || isPlantUmlBrowser || drawsOnDemand) ? DiagramContextMenu.GetInlineSvgStyles() : "";
        var internalFlowPopupStyles = internalFlowTracking ? DiagramContextMenu.GetInternalFlowPopupStyles() : "";
        var internalFlowPopupScript = internalFlowTracking ? DiagramContextMenu.GetInternalFlowPopupScript() : "";
        var flameChartRenderScript = internalFlowTracking ? DiagramContextMenu.GetFlameChartRenderScript() : "";
        var toggleScript = internalFlowTracking ? DiagramContextMenu.GetToggleScript() : "";
        var diagramToggleLayoutScript = DiagramContextMenu.GetDiagramToggleLayoutScript();
        // each scenario's settings and their gears exist only where the page draws the diagrams (AppendDiagramSectionHead)
        var scenarioSettingsScript = isPlantUmlBrowser
            ? "<script id=\"kronikol-scenario-settings\">" + LoadResource("report-scenario-settings-script.js") + "</script>"
            : "";
        var themeBootScript = LoadResource("report-theme-boot-script.js");
        var themeSwitchScript = LoadResource("report-theme-switch-script.js");
        var runBandScript = LoadResource("report-run-band-script.js");
        var reportTabsScript = LoadResource("report-tabs-script.js");
        // the Timeline tab's controls (5.0.0, plans/OPTION_C_PLAN.md S11, D27), in a page that has a timeline
        var timelineScript = hasDurations
            ? "<script id=\"kronikol-timeline\">" + LoadResource("report-timeline-script.js") + "</script>"
            : "";
        // the Components tab's controls (5.0.0, plans/OPTION_C_PLAN.md S11, D28), where the page draws the diagram and the
        // report knows its parts: a diagram the report's renderer drew is a picture the page cannot draw again
        var componentControls = componentDiagram is not null && componentDiagramDrawn is null;
        var componentsScript = componentControls
            ? "<script id=\"kronikol-components\">" + LoadResource("report-components-script.js") + "</script>"
            : "";
        // The report's tabs (5.0.0, plans/OPTION_C_PLAN.md S3, D20): each section that sat above the features as a
        // panel of its own (the Scenario Timeline, the Component Diagram, History, Report diagnostics) is a tab of the
        // bar, and the features, with the failure clusters and the background calls at their top (Q4), are the Features
        // tab; the clusters show over the cards, never over the Summary view's table (5.1.0). A page with nothing beside
        // its features has the bar too, with that one tab (plans/SEARCH_PLAN.md F7, Q10): Filters and the Search tab live
        // in it. Until then such a page had no tabs, and so no bar, and no Filters outside the box.
        var hasComponentTab = !string.IsNullOrEmpty(componentDiagramPlantUml);
        var hasHistoryTab = history is not null && showHistorySection;
        var hasDiagnosticsTab = includeTestRunData && showReportDiagnostics && diagnostics is { Count: > 0 };
        // focus the keyboard moves is never left under the pinned bar (5.0.0, plans/OPTION_C_PLAN.md S12, D48)
        var focusScript = "<script id=\"kronikol-focus\">" + LoadResource("report-focus-script.js") + "</script>";

        var customCssBlock = customCss is not null ? $"<style>{customCss}</style>" : "";
        var faviconLink = $"<link rel=\"icon\" href=\"{customFaviconBase64 ?? Constants.DefaultFavicon.DataUri}\">";

        var enrichSearchDataScript = "";

        var advancedSearchScript = LoadScript("advanced-search.js", drawsVerdicts);

        var html = $$"""
                    <!DOCTYPE html>
                    <html>
                        <head>
                            <meta charset="utf-8" />
                            <meta name="viewport" content="width=device-width, initial-scale=1" />
                            <meta name="generator" content="Kronikol v{{KronikolVersion}}" />
                            <title>{{System.Net.WebUtility.HtmlEncode(title)}}</title>
                            <script id="kronikol-theme-boot">{{themeBootScript}}</script>
                            <style>
                                {{combinedStylesheet}}
                                {{contextMenuStyles}}
                                {{inlineSvgStyles}}
                                {{collapsibleNotesStyles}}
                                {{internalFlowPopupStyles}}{{themeStyles}}
                            </style>
                            {{customCssBlock}}
                            {{faviconLink}}
                            <script>
                                {{decompressHelper}}
                                {{advancedSearchScript}}
                                {{scenarioFeatureMapHelper}}
                                {{filterChipsScript}}
                                {{searchFunction}}
                                {{searchIndexClientScript}}
                                {{dependencyFilterFunction}}
                                {{statusFilterFunction}}
                                {{featuresToolsScript}}
                                {{filterBarScript}}
                                {{lightboxFunction}}
                                {{toggleTableRefFunction}}
                                {{sortTableFunction}}
                                {{copyScenarioNameFunction}}
                                {{selectRowFunction}}
                                {{stepStatusTitlesScript}}
                                {{subStepsOnExpandScript}}
                                {{toggleFlattenParamsJs}}
                                {{paramExpandJs}}
                                {{jumpToFailureFunction}}
                                {{durationFilterFunction}}
                                {{exportFunction}}
                                {{persistentFilterFunction}}
                                {{urlHashFunction}}
                                {{navigationScript}}
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
                            <script id="kronikol-theme-switch">{{themeSwitchScript}}</script>
                            <script id="kronikol-run-band">{{runBandScript}}</script>
                            <script id="kronikol-report-tabs">{{reportTabsScript}}</script>
                            {{scenarioSettingsScript}}{{timelineScript}}{{componentsScript}}{{focusScript}}
                            <script id="kronikol-search-ui">{{searchUiScript}}</script>
                            <script id="kronikol-search-results">{{searchResultsScript}}</script>
                            <script id="kronikol-search-hit">{{searchHitScript}}</script>
                            <script id="kronikol-search-marks">{{searchMarksScript}}</script>
                        </head>
                        <body>
                    """;

        var body = new StringBuilder();
        if (customLogoHtml is not null)
            body.Append($"<div class=\"custom-logo\">{customLogoHtml}</div>");
        // The title line: the title, then the search, then the theme switch, as GitHub's search is the leftmost of its
        // header's right-hand controls (plans/SEARCH_PLAN.md 4.2, A3); the query builder the search opens, and the button
        // that reaches it from anywhere down a phone's page (Q7)
        body.Append($"<div class=\"title-line\"><h1>{System.Net.WebUtility.HtmlEncode(title)}</h1><div class=\"title-actions\">{SearchBox}{ThemeSwitchButton}</div></div>");
        body.Append(SearchDialog).Append(SearchFab);

        // The features table, the Features tab's Summary view (S4), and the column its duration group starts at.
        var featureTable = new StringBuilder();
        int? durationCol = null;
        if (includeTestRunData)
        {
            // A scenario that is not a test (Scenario.NotATest: ingest's fold, a stack's start-up) is drawn like any and
            // counted as none, here and in every count and time of the run's tests (plans/RUN_WINDOW_PLAN.md R1): the
            // fold used to count as a passed test, and a run of two tests read "Scenarios 3, Passed 3".
            var numberOfFeatures = features.Count(HoldsATest);
            var scenarios = features.SelectMany(x => x.Scenarios).Where(x => !x.NotATest).ToArray();
            var passedScenarios = scenarios.Where(x => x.Result == ExecutionResult.Passed).ToArray();
            // A scenario skipped after a failure is skipped (5.0.0): 4.x left it out of the summary and the ring, so
            // their counts did not add up to the scenarios, while the features table already counted it.
            var skippedScenarios = scenarios.Where(x => x.Result is ExecutionResult.Skipped or ExecutionResult.SkippedAfterFailure).ToArray();
            var failedScenarios = scenarios.Where(x => x.Result == ExecutionResult.Failed).ToArray();
            var overallStatus = failedScenarios.Any() ? "Failed" : "Passed";

            // The features table (5.0.0, plans/OPTION_C_PLAN.md S4, D21): one row per feature with its counts and times,
            // written in the Features tab beside the feature cards (the Summary view) in place of the 4.x collapsible
            // Features Summary above the execution summary. Passed, Failed and Skipped repeat for scenarios and for steps,
            // so a row above the headers names each group and the group's first column reads Total. The rows are in the
            // cards' order, which report-features-tools-script.js relies on to pair a row with its card.
            var hasAnySteps = scenarios.Any(s => s.Steps is { Length: > 0 } || s.BackgroundSteps is { Length: > 0 });
            var hasAnyDurations = scenarios.Any(s => s.Duration.HasValue);
            var nextCol = 5;
            featureTable.Append("<div class=\"features-summary-table-wrapper\">");
            featureTable.Append("<table class=\"feature-summary-table\"><thead>");
            featureTable.Append("<tr class=\"table-groups\"><th></th><th colspan=\"4\">Scenarios</th>");
            if (hasAnySteps) featureTable.Append("<th class=\"grp-start\" colspan=\"4\">Steps</th>");
            if (hasAnyDurations) featureTable.Append("<th class=\"grp-start\" colspan=\"3\">Duration</th>");
            featureTable.Append("</tr><tr>");
            featureTable.Append("<th onclick=\"sort_table(0)\">Feature</th>");
            featureTable.Append("<th onclick=\"sort_table(1)\">Total</th>");
            featureTable.Append("<th onclick=\"sort_table(2)\">Passed</th>");
            featureTable.Append("<th onclick=\"sort_table(3)\">Failed</th>");
            featureTable.Append("<th onclick=\"sort_table(4)\">Skipped</th>");
            if (hasAnySteps)
            {
                featureTable.Append($"<th class=\"grp-start\" onclick=\"sort_table({nextCol++})\">Total</th>");
                featureTable.Append($"<th class=\"step-status-header\" onclick=\"sort_table({nextCol++})\">Passed</th>");
                featureTable.Append($"<th class=\"step-status-header\" onclick=\"sort_table({nextCol++})\">Failed</th>");
                featureTable.Append($"<th class=\"step-status-header\" onclick=\"sort_table({nextCol++})\">Skipped</th>");
            }
            if (hasAnyDurations)
            {
                durationCol = nextCol;
                featureTable.Append($"<th class=\"grp-start\" onclick=\"sort_table({nextCol++})\">Total</th>");
                featureTable.Append($"<th onclick=\"sort_table({nextCol++})\">Avg</th>");
                featureTable.Append($"<th onclick=\"sort_table({nextCol})\">Longest</th>");
            }
            featureTable.Append("</tr></thead><tbody>");

            foreach (var feature in features)
            {
                var tests = feature.Scenarios.Where(s => !s.NotATest).ToArray();
                // the name is a button: a row opens its feature in the Full view, from the keyboard as well
                var nameCell = $"<td><button type=\"button\" class=\"feature-open\" title=\"Open this feature's card in the Full view\">{System.Net.WebUtility.HtmlEncode(feature.DisplayName)}</button></td>";

                // A feature that holds no test, only scenarios that are not tests (a stack's start-up): its counts say so,
                // and its time cells are empty, so the Features tab's time sorts put it last rather than its minutes first.
                if (tests.Length == 0 && feature.Scenarios.Length > 0)
                {
                    featureTable.Append("<tr class=\"not-a-test\">").Append(nameCell).Append("<td data-sort=\"0\">not a test</td><td></td><td></td><td></td>");
                    if (hasAnySteps) featureTable.Append("<td class=\"grp-start\"></td><td></td><td></td><td></td>");
                    if (hasAnyDurations) featureTable.Append("<td class=\"grp-start\"></td><td></td><td></td>");
                    featureTable.Append("</tr>");
                    continue;
                }

                var totalSc = tests.Length;
                var passedSc = tests.Count(s => s.Result == ExecutionResult.Passed);
                var failedSc = tests.Count(s => s.Result == ExecutionResult.Failed);
                var skippedSc = tests.Count(s => s.Result is ExecutionResult.Skipped or ExecutionResult.Bypassed or ExecutionResult.SkippedAfterFailure);
                var featureHasFail = failedSc > 0;

                featureTable.Append($"<tr{(featureHasFail ? " class=\"failed\"" : "")}>");
                featureTable.Append(nameCell);
                featureTable.Append($"<td>{totalSc}</td>");
                featureTable.Append($"<td>{passedSc}</td>");
                featureTable.Append($"<td>{failedSc}</td>");
                featureTable.Append($"<td>{skippedSc}</td>");

                if (hasAnySteps)
                {
                    var allSteps = tests
                        .SelectMany(s => (s.BackgroundSteps ?? []).Concat(s.Steps ?? []))
                        .ToArray();
                    var stepCount = CountStepsRecursive(allSteps);
                    var stepStatusCounts = CountStepsByStatusRecursive(allSteps);
                    featureTable.Append($"<td class=\"grp-start\">{stepCount}</td>");
                    featureTable.Append($"<td>{stepStatusCounts.Passed}</td>");
                    featureTable.Append($"<td>{stepStatusCounts.Failed}</td>");
                    featureTable.Append($"<td>{stepStatusCounts.Skipped}</td>");
                }

                if (hasAnyDurations)
                {
                    var durations = tests.Where(s => s.Duration.HasValue).Select(s => s.Duration!.Value).ToArray();
                    var totalDuration = durations.Length > 0 ? durations.Aggregate(TimeSpan.Zero, (a, b) => a + b) : TimeSpan.Zero;
                    var avgDuration = durations.Length > 0 ? totalDuration / durations.Length : TimeSpan.Zero;
                    var maxDuration = durations.Length > 0 ? durations.Max() : TimeSpan.Zero;
                    // The cell's text is rounded and mixes units ("500ms", "1m 5s"), so sort_table reads data-sort, the
                    // milliseconds: on the text, "1m 5s" sorted as 1, below "500ms", until 4.7.3.
                    featureTable.Append(CultureInfo.InvariantCulture, $"<td class=\"grp-start\" data-sort=\"{totalDuration.TotalMilliseconds:F0}\">{FormatDuration(totalDuration)}</td>");
                    featureTable.Append(CultureInfo.InvariantCulture, $"<td data-sort=\"{avgDuration.TotalMilliseconds:F0}\">{FormatDuration(avgDuration)}</td>");
                    featureTable.Append(CultureInfo.InvariantCulture, $"<td data-sort=\"{maxDuration.TotalMilliseconds:F0}\">{FormatDuration(maxDuration)}</td>");
                }

                featureTable.Append("</tr>");
            }

            featureTable.Append("</tbody></table>");
            featureTable.Append("</div>");

            // The run band (5.0.0, plans/OPTION_C_PLAN.md S2): the ring, the test execution summary and the CI zone on one
            // row, in place of the 4.x header row and its two tables; the filter box follows it on a row of its own.
            var bypassedScenarios = scenarios.Where(x => x.Result == ExecutionResult.Bypassed).ToArray();
            AppendRunBand(body, overallStatus, startRunTime, endRunTime, numberOfFeatures, scenarios.Length,
                passedScenarios.Length, failedScenarios.Length, skippedScenarios.Length, bypassedScenarios.Length, ciMetadata);
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

        // What the filters leave showing (5.0.0, plans/OPTION_C_PLAN.md S5, D14), beside Clear All: written here for the
        // page as it opens, and kept by the filters (report-scenario-feature-map-helper.js), counting the scenarios as
        // the run counts them. The exports drop "Filtered" where there is no room for it (D13).
        // Clear All's title names what it clears, the rows the box has (D49)
        var clearTitle = ClearAllTitle(features, hasDurations, allDependencies.Count > 0);
        // Counted as the band counts: tests, and the features that hold one (report-scenario-feature-map-helper.js agrees).
        var testCount = features.Sum(f => f.Scenarios.Count(s => !s.NotATest));
        var testFeatures = features.Count(HoldsATest);
        var readoutText = $"All {ScenarioCount(testCount)}, {(testFeatures == 1 ? "1 feature" : string.Create(CultureInfo.InvariantCulture, $"{testFeatures} features"))}";
        // The box is written at the top of the bar, hidden, with every row showing (plans/SEARCH_PLAN.md F1, F2, F5): the
        // bar's Filters opens and closes it there, at rest and pinned. Until then it sat on a row of its own above the bar,
        // its rows folded under More filters, and Filters took it into the bar only while the bar floated. Its readout is
        // seen, not heard: the bar's own status element announces the filters, the box open or not (F8).
        var boxStart = body.Length;
        body.Append($"""
                 <div class="filtering-box" id="filtering-box" role="region" aria-label="Filters" tabindex="-1" hidden>
                    <div class="filtering-box-header"><h2>Filtering</h2><div class="filtering-box-export"><span class="filter-readout" aria-hidden="true" title="{FiltersLeaveShowing}">{readoutText}</span><button class="export-btn" onclick="clear_all_filters()" title="{clearTitle}">Clear All</button><button class="export-btn" onclick="export_html()" title="Download a copy of this report holding only the features the filters show">Export <span class="wide-only">Filtered </span>HTML</button><button class="export-btn" onclick="export_csv()" title="Download the scenarios the filters show as a CSV file: feature, scenario, status and duration in milliseconds">Export <span class="wide-only">Filtered </span>CSV</button>{FiltersClose}</div></div>
                    {FilterField}
                    <div class="filters" id="filter-rows">
                    {FilterHelp}
                    <div class="filter-row">
                 """);

        // The filter rows (5.0.0, plans/OPTION_C_PLAN.md S6, D6 to D12, D19): labels without colons and each row's controls
        // in a span.fltr-controls; Status, Duration and Happy paths share the first row; then Tags and Dependencies, each
        // a row of chips with an ANY / ALL switch, a 2-WAY / 3-WAY switch (5.10.0: a chip is on or off unless its row is
        // three-way, when it goes off, has, has not), a chip for the scenarios with none, and a Clear
        // (report-filter-chips-script.js). A chip's title counts the scenarios it picks out and says what a click does in
        // the row's ways (D49). A category is a tag (plans/TAGS_PLAN.md §4.8).
        var runs = features.SelectMany(f => f.Scenarios).ToArray();
        var tagChips = TagChips.For(features);
        // Status filter toggles
        {
            body.Append("""<div class="status-filters"><span class="status-filters-label">Status</span><span class="fltr-controls">""");
            foreach (var status in Enum.GetValues<ExecutionResult>().OrderBy(s => s))
            {
                if (status == ExecutionResult.SkippedAfterFailure) continue;
                var statusName = status.ToString();
                body.Append($"""<button class="status-toggle" data-status="{statusName}" aria-pressed="false" onclick="toggle_status(this)" title="{StatusToggleTitle(status)}">{statusName}</button>""");
            }
            body.Append("</span></div>");
        }

        // Duration filter (only shown when scenarios have duration data), on the Status line (D7). Each button says what
        // it shows, and that the first-call warm-up is left out when the run had one (D19).
        if (hasDurations)
        {
            // Each scenario's own time: a scenario slow only because it paid the run's first-call warm-up is not slow
            // (plans/WARM_UP_PLAN.md R2), and report-duration-filter-function.js compares the same number. Read as the
            // filter reads it, data-duration-ms less data-warmup-ms with each written whole, so a percentile is a time
            // some scenario takes and the button that names it shows that scenario (until 4.16.3 the difference was
            // rounded once, and could sit a millisecond above it).
            var durationsMs = features.SelectMany(f => f.Scenarios)
                .Where(s => s.Duration.HasValue && !s.NotATest)
                .Select(s => WholeMs(s.Duration!.Value.TotalMilliseconds) - WholeMs(WarmUpMsOf(warmUp, s.Id)))
                .OrderBy(d => d)
                .ToArray();
            var p50Ms = Percentile(durationsMs, 0.50);
            var p90Ms = Percentile(durationsMs, 0.90);
            var p95Ms = Percentile(durationsMs, 0.95);
            var p99Ms = Percentile(durationsMs, 0.99);

            var warm = warmUp.ScenarioMs.Count > 0;
            var notWarm = warm ? ", not counting first-call warm-up" : "";
            string Threshold(double ms, string share) => $"Scenarios taking {FormatDurationBadge(TimeSpan.FromMilliseconds(ms))} or more{notWarm} ({share})";
            body.Append(CultureInfo.InvariantCulture, $"""<div class="duration-filters" data-p50="{p50Ms:F0}" data-p90="{p90Ms:F0}" data-p95="{p95Ms:F0}" data-p99="{p99Ms:F0}"><span class="duration-filters-label"{(warm ? " title=\"Each scenario's own time, without the first-call warm-up it paid\"" : "")}>Duration ≥</span><span class="fltr-controls"><button class="percentile-btn" aria-pressed="false" data-threshold-ms="{p50Ms:F0}" onclick="set_percentile(this)" title="{Threshold(p50Ms, "the slowest half")}">P50 ({FormatDurationBadge(TimeSpan.FromMilliseconds(p50Ms))})</button><button class="percentile-btn" aria-pressed="false" data-threshold-ms="{p90Ms:F0}" onclick="set_percentile(this)" title="{Threshold(p90Ms, "the slowest 10%")}">P90 ({FormatDurationBadge(TimeSpan.FromMilliseconds(p90Ms))})</button><button class="percentile-btn" aria-pressed="false" data-threshold-ms="{p95Ms:F0}" onclick="set_percentile(this)" title="{Threshold(p95Ms, "the slowest 5%")}">P95 ({FormatDurationBadge(TimeSpan.FromMilliseconds(p95Ms))})</button><button class="percentile-btn" aria-pressed="false" data-threshold-ms="{p99Ms:F0}" onclick="set_percentile(this)" title="{Threshold(p99Ms, "the slowest 1%")}">P99 ({FormatDurationBadge(TimeSpan.FromMilliseconds(p99Ms))})</button><button class="percentile-btn" aria-pressed="false" data-custom="1" onclick="set_percentile(this)" title="Scenarios taking at least the seconds you enter{notWarm}">Custom</button><span id="custom-duration-wrap" style="display:none;align-items:center;gap:0.3em"><input id="duration-threshold" autocomplete="off" type="number" step="0.1" min="0" placeholder="seconds" onchange="filter_duration()" title="Seconds: scenarios taking at least this long{notWarm}" /><span class="duration-filters-unit">seconds</span></span></span></div>""");
        }

        // Happy paths: Show, Only or Hide, a face of the Tags row's Happy Path chip, so one filter underneath (D8)
        if (runs.Any(s => s.IsHappyPath))
            body.Append("""<div class="happy-filters"><span class="happy-filters-label">Happy paths</span><span class="fltr-controls" role="group" aria-label="Happy paths"><button type="button" class="happy-seg" data-hp="off" aria-pressed="true" title="Happy-path scenarios and the rest">Show</button><button type="button" class="happy-seg" data-hp="in" aria-pressed="false" title="Only the scenarios tagged as happy paths">Only</button><button type="button" class="happy-seg" data-hp="out" aria-pressed="false" title="Leave out the scenarios tagged as happy paths">Hide</button></span></div>""");

        body.Append("</div>"); // close filter-row

        // Tags: each scenario's effective tags, its feature's and then its own, with Happy Path for a happy path pinned
        // first (D9, plans/TAGS_PLAN.md §4.8). A category is one more tag. One chip per tag, spelled as the report first
        // meets it, in an order no culture sets (TagChips).
        var tagNames = tagChips.Names;
        if (tagNames.Length > 0)
        {
            body.Append("""<div class="tag-filters"><span class="tag-filters-label">Tags</span><span class="fltr-controls">""");
            body.Append(FilterModeToggle("tag-mode-toggle", tagModeText, "ANY: scenarios with any of the chosen tags. ALL: scenarios with every one of them. Click to switch."));
            body.Append(FilterWaysToggle("tag-ways-toggle", ChipWaysDefault));
            foreach (var tag in tagNames)
                body.Append(FilterChip("tag", tag, tag, tagChips.Count(tag), more: tagChips.Hidden.Contains(tag)));
            var untagged = tagChips.Untagged;
            if (untagged > 0) body.Append(FilterChip("tag", "__untagged__", "Untagged", untagged, none: true));
            if (tagChips.Hidden.Count > 0) body.Append(TagMoreChip(tagChips.Hidden.Count));
            body.Append(GroupClear("tag"));
            body.Append("</span></div>");
        }

        // Dependencies: the participants a scenario's diagrams name; "No dependencies" for those that name none (D10)
        if (allDependencies.Count > 0)
        {
            body.Append("""<div class="dependency-filters"><span class="dependency-filters-label">Dependencies</span><span class="fltr-controls">""");
            body.Append(FilterModeToggle("dep-mode-toggle", depModeText, "ANY: scenarios that call any of the chosen dependencies. ALL: scenarios that call every one of them. Click to switch."));
            body.Append(FilterWaysToggle("dep-ways-toggle", ChipWaysDefault));
            foreach (var dep in allDependencies.OrderBy(d => d))
                body.Append(FilterChip("dependency", dep, dep, runs.Count(sc => scenarioDependencies.TryGetValue(sc.Id, out var ds) && ds.Contains(dep))));
            var noDependencies = runs.Count(sc => !scenarioDependencies.TryGetValue(sc.Id, out var ds) || ds.Count == 0);
            if (noDependencies > 0) body.Append(FilterChip("dependency", "__none__", "No dependencies", noDependencies, none: true));
            body.Append(GroupClear("dependency"));
            body.Append("</span></div>");
        }

        body.Append("</div>"); // close filters
        body.Append("</div>"); // close filtering-box
        // taken out of the page's flow here and written at the top of the bar, below
        var filteringBox = body.ToString(boxStart, body.Length - boxStart);
        body.Length = boxStart;

        // The tab the page opens on (the tabs are worked out above the head, which loads the focus script only where the
        // bar is): a panel configured visible; when both are, the timeline, which exists in both reports (Q2).
        var showTimelinePanel = toggles.ScenarioTimelineVisible && hasDurations;
        var showComponentPanel = toggles.ComponentDiagramVisible && hasComponentTab && !showTimelinePanel;
        var startTab = showTimelinePanel ? "timeline" : showComponentPanel ? "components" : "features";
        // FeaturesSummaryOpen opens the Features tab on its Summary view (S4), where 4.x opened the summary's card.
        var hasFeatureTable = featureTable.Length > 0;
        var summaryStart = hasFeatureTable && toggles.FeaturesSummaryOpen;

        // Toolbar row: the tabs, then expand buttons left, Details/Headers right
        var tableMode = summaryStart ? " features-table-mode" : "";
        // The bar pins below a mark that says where (D25), carries the filter box at its top (plans/SEARCH_PLAN.md F2) and
        // the filters on its top row, right of the tabs (D24)
        body.Append("""<div class="nav-mark" aria-hidden="true"></div>""");
        body.Append($"""<div class="toolbar-row report-nav{(startTab == "features" ? " on-features" : "")}{tableMode}">""");
        body.Append(filteringBox);
        {
            // where the page opens, for the navigation script: the scripts change the selected tab before the page settles
            body.Append($"""<div class="nav-row"><div class="report-tabs" role="tablist" aria-label="Report sections" data-start-tab="{startTab}">""");
            // each tab titled with what it shows (D49); the timeline's names the warm-up only where a scenario paid one
            body.Append(ReportTab("features", "Features", "The features and their scenarios, with the filters applied", startTab, features.Length));
            if (hasDurations) body.Append(ReportTab("timeline", "Timeline", "Every scenario as a bar, slowest first" + (warmUp.ScenarioMs.Count > 0 ? ", with the first-call warm-up it paid" : ""), startTab));
            if (hasComponentTab) body.Append(ReportTab("components", "Components", "The services, databases and queues the scenarios called, drawn as one diagram", startTab));
            if (hasHistoryTab) body.Append(ReportTab("history", "History", HistoryHtml.Description, startTab));
            if (hasDiagnosticsTab) body.Append(ReportTab("diagnostics", "Diagnostics", DiagnosticsDescription, startTab, diagnostics!.Count));
            // the Search tab, last, hidden until a search is applied (plans/SEARCH_PLAN.md 4.5.1)
            body.Append(SearchTab);
            body.Append("</div>");
            body.Append(NavFilters(readoutText, ScenarioCount(testCount), clearTitle)).Append("</div>");
            // what the filters leave showing, said to a screen reader whether the box is open or not and however short
            // the bar's row is (plans/SEARCH_PLAN.md F8): visually hidden, never display:none
            body.Append($"""<div class="nav-status" role="status">{readoutText}</div>""");
        }
        // The Features tools (5.0.0, plans/OPTION_C_PLAN.md S4, D21, D22, D35): the Full and Summary views as tabs when
        // the page has the features table; expand and collapse for features and for scenarios, each a pair of icon
        // buttons dimmed while it has nothing to do (the seeded start says which, report-features-tools-script.js keeps
        // it true); the sort; and on a phone the gear that shows the display settings. On a page with tabs they are the
        // Features tab's, in a strip under the tabs that the other tabs hide.
        body.Append("""<div class="features-tools">""");
        body.Append("""<div class="toolbar-left features-tools-line">""");
        if (hasFeatureTable) body.Append(FeaturesViewSwitch(summaryStart));
        body.Append(FoldGroup("features", features.Length, toggles.FeaturesExpanded));
        body.Append(FoldGroup("scenarios", features.Sum(f => f.Scenarios.Length), toggles.ScenariosExpanded));
        body.Append("""<span class="tools-spacer"></span>""");
        if (hasFeatureTable) body.Append(FeaturesSortGroup(durationCol));
        if (isPlantUmlBrowser) body.Append(ToolsGear);
        body.Append("</div>");
        body.Append("""<div class="toolbar-right" id="display-settings">""");
        if (isPlantUmlBrowser)
        {
            // The display settings in zones (D30): what a note shows of its payload, then the payload's format and width,
            // then the toggles for what the diagrams draw.
            body.Append(BuildDetailsRadio(toggles, scenarioLevel: false));
            if (reportNoteFormatSelect.Length + reportNoteWidthSelect.Length > 0)
                body.Append("""<span class="diagram-format">""").Append(reportNoteFormatSelect).Append(reportNoteWidthSelect).Append("</span>");
            body.Append("""<span class="diagram-flags">""");
            body.Append(BuildFilterToggleButton("headers", toggles.HeadersShown, scenarioLevel: false));
            if (hasAssertionNotes)
                body.Append(BuildFilterToggleButton("assertions", toggles.AssertionsShown, scenarioLevel: false));
            if (hasStepDelimiters)
                body.Append(BuildFilterToggleButton("steps", toggles.StepsShown, scenarioLevel: false));
            if (hasDatabaseParticipants)
                body.Append(BuildFilterToggleButton("databases", toggles.DatabasesShown, scenarioLevel: false));
            if (hasEventParticipants)
                body.Append(BuildFilterToggleButton("events", toggles.EventsShown, scenarioLevel: false));
            if (hasSetup)
                body.Append(BuildFilterToggleButton("setup", toggles.SetupShown, scenarioLevel: false));
            body.Append("</span>");
        }
        body.Append("</div>");
        body.Append("</div>"); // the features tools
        body.Append("</div>");

        // The panels: the Features tab's first; the others are written after the features (below) from HTML built where
        // the sections were, so the diagrams keep their ids.
        body.Append("<div class=\"report-panels\">");
        body.Append(ReportPanel("features", startTab));
        body.Append(FiltersEmpty);

        var plantUmlBrowserCounter = 0;
        var diagramDataMap = new Dictionary<string, string>();
        // Closed composite steps' sub-steps held until first opened (5.0.0, plans/REPORT_FORMAT_PLAN.md 4.8 part b).
        var stepDeferral = renderCollapsedSubStepsOnExpand ? new StepDeferral() : null;

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
                .Where(s => s.FlowSpans.Length > 0)
                .Select(s => s.FlowSpans.Length)
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
            var baseAnchor = GenerateScenarioAnchorId(ScenarioStableId.KeyName(scenario));
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
        // every id a scenario took, so that a parameterized group's is unique too (UniqueAnchorId)
        var takenAnchorIds = new HashSet<string>(scenarioAnchorIds.Values, StringComparer.Ordinal);

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

            // they go with the cards: the Summary view is the features table alone (5.1.0)
            body.Append($"<details class=\"failure-clusters\"{(toggles.FailureClustersOpen ? " open" : "")}{(summaryStart ? " hidden" : "")}>");
            body.Append($"<summary>Failure Clusters ({clusters.Length} root cause{(clusters.Length == 1 ? "" : "s")})</summary>");
            foreach (var cluster in clusters)
            {
                var anchorLinks = string.Join("", cluster.Scenarios.Select(s =>
                {
                    var anchorId = scenarioAnchorIds[s.Id];
                    var featureName = scenarioFeatureLookup.GetValueOrDefault(s.Id, "");
                    // A plain link (5.2.0): the navigation script takes it, adds the entry Back returns by, keeps the
                    // filters in the address and opens the scenario the one way every link does (reveal_url_anchor, which
                    // finds the example row that shows); a click with Ctrl or Shift opens it in a new tab
                    // (plans/BACK_BUTTON_NAVIGATION_PLAN.md S2, S6, S16, S20).
                    var prefix = featureName.Length > 0 ? $"<span class=\"failure-cluster-feature\">{System.Net.WebUtility.HtmlEncode(featureName)} &rsaquo;</span> " : "";
                    return $"<li>{prefix}<a class=\"failure-cluster-scenario-link\" href=\"#{anchorId}\" title=\"Go to this scenario\">{System.Net.WebUtility.HtmlEncode(s.DisplayName)}</a></li>";
                }));
                body.Append($"<details class=\"failure-cluster\"><summary>{System.Net.WebUtility.HtmlEncode(cluster.ClusterKey)}<span class=\"failure-cluster-count\">{cluster.Scenarios.Length} scenarios</span></summary>");
                body.Append($"<ul class=\"failure-cluster-scenarios\">{anchorLinks}</ul></details>");
            }
            body.Append("</details>");
        }

        // The Diagnostics tab (ReportConfigurationOptions.ShowReportDiagnosticsSection, on by default since 5.0.0): a
        // healthy run has nothing to list and no tab, and every diagnostic is also on the labs page beside the report,
        // in the data file's diagnostics array and in the run's own result. In its tab the section is always
        // open: the tab is its disclosure, so DiagnosticsOpen has nothing left to open.
        var diagnosticsHtml = hasDiagnosticsTab ? RenderReportDiagnostics(diagnostics!, open: true, inTab: true) : "";

        if (includeTestRunData && background is { Calls: > 0 })
            body.Append(RenderBackgroundCalls(background));

        // Cross-run history (plans/CROSS_RUN_HISTORY_PLAN.md §8.1): the section sits beside the timeline,
        // and the per-scenario entries are looked up by stable id as the scenarios render below. Null
        // when the run had no ledger, and then nothing about history reaches the file.
        // The section is the History tab (ReportConfigurationOptions.ShowHistorySection, on by default since 5.0.0);
        // the sparklines, pills and verdict attributes in the features stay off unless ShowScenarioHistory asks
        // (plans/HISTORY_AND_DIAGNOSTICS_MOVE_PLAN.md). The labs page beside the report carries both.
        var historySlots = scenarioHistory is null ? null : new Dictionary<string, int>(StringComparer.Ordinal);
        var historyHtml = hasHistoryTab ? HistoryHtml.InTab(history!) : "";

        // Scenario timeline / Gantt, the Timeline tab's panel
        var timelineHtml = new StringBuilder();
        if (hasDurations)
        {
            // the tests' times: a scenario that is not a test spans no test's time (plans/RUN_WINDOW_PLAN.md R1)
            var timelineScenarios = features
                .SelectMany(f => f.Scenarios.Select(s => (Feature: f.DisplayName, Scenario: s, Key: ScenarioStableId.FeatureKey(f))))
                .Where(x => x.Scenario.Duration.HasValue && !x.Scenario.NotATest)
                .OrderByDescending(x => x.Scenario.Duration!.Value)
                .ToArray();

            if (timelineScenarios.Length > 0)
            {
                var maxDuration = timelineScenarios.Max(x => x.Scenario.Duration!.Value.TotalMilliseconds);
                // The Timeline tab's controls in the header line, and under it the note Filtered fills (5.0.0,
                // plans/OPTION_C_PLAN.md S11, D27): Warm-up and Order only where a row paid a first-call warm-up, since
                // without one they would change nothing. report-timeline-script.js acts on them.
                var timelineWarmUp = timelineScenarios.Any(x => WarmUpMsOf(warmUp, x.Scenario.Id) > 0);
                timelineHtml.Append("<div id=\"scenario-timeline\" class=\"scenario-timeline\">");
                timelineHtml.Append("<div class=\"timeline-header\">Scenario Timeline <span class=\"timeline-info\" title=\"The Scenario Timeline shows every test scenario ordered by duration (longest first). Each bar is proportional to the scenario's elapsed time, colour-coded by result: green = passed, red = failed, grey = skipped, blue = bypassed. Use it to quickly spot slow tests, compare relative durations, and identify performance outliers across the entire test run."
                    // A page that draws a warm-up stripe says what it is (until 4.16.3 the legend named every colour but it).
                    + (warmUp.ScenarioMs.Count > 0 ? " A striped start is the first-call warm-up the scenario paid; hover over the bar for its calls." : "")
                    + "\">&#x1F6C8;</span>" + TimelineControls(timelineWarmUp) + "</div>");
                timelineHtml.Append(FilteredNote);
                foreach (var (featureName, scenario, featureKey) in timelineScenarios)
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
                    var timelineWarmUpMs = WarmUpMsOf(warmUp, scenario.Id);
                    // The scenario the row draws, by the stable id its element carries, and its time and warm-up in whole
                    // milliseconds as that element writes them (D27), so the page reads nothing back from a title or a
                    // name. Under data-sid: data-stable-id is the scenario element's own address, which a #sid- link
                    // resolves through. A failed scenario's row is marked, and the stylesheet opens its name with a cross.
                    var timelineStableId = ScenarioStableId.For(suite, featureKey, scenario);
                    var timelineWarmUpAttr = timelineWarmUpMs > 0 ? string.Create(CultureInfo.InvariantCulture, $" data-warmup-ms=\"{timelineWarmUpMs:F0}\"") : "";
                    timelineHtml.Append(CultureInfo.InvariantCulture, $"<div class=\"timeline-row{(scenario.Result == ExecutionResult.Failed ? " timeline-row-failed" : "")}\" data-sid=\"{timelineStableId}\" data-duration-ms=\"{durationMs:F0}\"{timelineWarmUpAttr}>");
                    // The name links to the scenario by the same stable id (5.5.0, plans/BACK_BUTTON_NAVIGATION_PLAN.md Q7): the
                    // navigation script takes the link as it takes the History tab's, so Back returns to the timeline where
                    // the reader was, and a click with Ctrl or Shift opens the scenario in a new tab. The title keeps the
                    // whole name where the label cuts it short.
                    timelineHtml.Append($"<a class=\"timeline-label\" href=\"#sid-{timelineStableId}\" title=\"{System.Net.WebUtility.HtmlEncode(scenario.DisplayName)}\">{System.Net.WebUtility.HtmlEncode(scenario.DisplayName)}</a>");
                    if (timelineWarmUpMs > 0)
                    {
                        // The warm-up's share of the bar, shaded from its start: the bar is elapsed time, the share is what
                        // the run's first calls took of it.
                        var tooltip = System.Net.WebUtility.HtmlEncode(WarmUpTooltip(scenario.Duration.Value, timelineWarmUpMs, warmUpByScenario[scenario.Id]));
                        timelineHtml.Append(CultureInfo.InvariantCulture, $"<div class=\"timeline-track\"><div class=\"timeline-bar {statusClass}\" style=\"width:{widthPercent:F1}%\" title=\"{tooltip}\">"
                            + $"<div class=\"timeline-warm-up\" style=\"width:{timelineWarmUpMs / durationMs * 100:F1}%\"></div></div></div>");
                    }
                    else
                        timelineHtml.Append(CultureInfo.InvariantCulture, $"<div class=\"timeline-track\"><div class=\"timeline-bar {statusClass}\" style=\"width:{widthPercent:F1}%\" title=\"{FormatDurationBadge(scenario.Duration.Value)}\"></div></div>");
                    timelineHtml.Append($"<div class=\"timeline-duration\">{FormatDurationBadge(scenario.Duration.Value)}</div>");
                    timelineHtml.Append("</div>");
                }
                timelineHtml.Append("</div>");
            }
        }

        // Embedded component diagram, the Components tab's panel
        var componentHtml = "";
        if (!string.IsNullOrEmpty(componentDiagramPlantUml))
        {
            var compDiagramId = $"puml-{plantUmlBrowserCounter++}";
            diagramDataMap[compDiagramId] = componentDiagramPlantUml;
            // Drawn by the page under BrowserJs, and by the report's renderer under the others (DrawEmbedded).
            var componentDiagramHtml = componentDiagramDrawn switch
            {
                { InlineSvg: { } svg } => $"<div class=\"plantuml-inline-svg\" id=\"{compDiagramId}\" data-diagram-type=\"plantuml\">{svg}</div>",
                { ImageSource: { } src } => $"<img src=\"{System.Net.WebUtility.HtmlEncode(src)}\" alt=\"Component diagram\" style=\"max-width:100%\">",
                { Failure: { } why } => $"<div class=\"component-diagram-failure\">{System.Net.WebUtility.HtmlEncode(why)}</div>",
                _ => $"<div class=\"plantuml-browser\" id=\"{compDiagramId}\" data-diagram-type=\"plantuml\"></div>"
            };
            // The tab's heading, which is the diagram's title, and where the page draws the diagram from parts the report
            // knows, its controls, the note Filtered writes and the model report-components-script.js draws from (5.0.0,
            // plans/OPTION_C_PLAN.md S11, D26, D28). Show and its note only where the report knows which scenarios made each
            // connection's calls.
            var componentUsage = componentControls ? ComponentUsage(componentDiagram!, features, suite) : null;
            var componentHeader = ComponentHeader(componentDiagram?.Heading ?? EmbeddedComponentDiagram.DefaultHeading, componentControls, componentUsage is not null);
            var componentNote = componentUsage is not null ? FilteredNote : "";
            componentHtml = $"""<div id="component-diagram" class="component-diagram-section">{componentHeader}{componentNote}{componentDiagramHtml}</div>""";
            if (componentControls)
                componentHtml += $"""<script id="component-diagram-model" type="application/json">{ComponentModelJson(componentDiagram!, componentUsage)}</script>""";
        }

        if (hasFeatureTable)
        {
            body.Append("<div class=\"features-panel\">");
            body.Append($"<div class=\"features-table-view\" id=\"features-table-view\" role=\"tabpanel\" aria-labelledby=\"view-summary\"{(summaryStart ? "" : " hidden")}>");
            body.Append(featureTable).Append("</div>");
            body.Append($"<div id=\"report-content\" role=\"tabpanel\" aria-labelledby=\"view-full\"{(summaryStart ? " hidden" : "")}>");
        }
        else body.Append("<div id=\"report-content\">");
        var paramGroupCounter = 0;
        foreach (var feature in features)
        {
            var featureHasFailures = feature.Scenarios.Any(s => s.Result == ExecutionResult.Failed);
            var featureAllSkipped = !featureHasFailures && feature.Scenarios.All(s => s.Result == ExecutionResult.Skipped);
            body.Append($"""
                     <details class="feature"{(toggles.FeaturesExpanded ? " open" : "")}>
                        <summary class="h2{(featureHasFailures ? " failed" : featureAllSkipped ? " skipped" : "")}">{System.Net.WebUtility.HtmlEncode(feature.DisplayName)}{(feature.Endpoint is null ? "" : $" <div class=\"endpoint\">{System.Net.WebUtility.HtmlEncode(feature.Endpoint)}</div>")}{(feature.Tags is { Length: > 0 } fl ? string.Concat(fl.Select(l => $" <span class=\"label\">{System.Net.WebUtility.HtmlEncode(l)}</span>")) : "")}</summary>
                     """);

            if (feature.Description is not null)
            {
                body.Append($"""<div class="feature-description">{System.Net.WebUtility.HtmlEncode(feature.Description)}</div>""");
            }

            var orderedScenarios = feature.Scenarios.OrderByDescending(x => x.IsHappyPath).ThenBy(ScenarioStableId.KeyName).ToArray();

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
                        showNoInteractionsMarker: showNoInteractionsMarker,
                        scenarioAnchorIds: scenarioAnchorIds,
                        takenAnchorIds: takenAnchorIds,
                        featureDisplayName: feature.DisplayName,
                        featureKey: ScenarioStableId.FeatureKey(feature),
                        featureDescription: feature.Description,
                        featureEndpoint: feature.Endpoint,
                        featureTags: feature.Tags,
                        tagChips: tagChips,
                        precomputedWholeTestContent: precomputedWholeTestContent,
                        separateBackgroundSteps: separateBackgroundSteps,
                        collapseRepeatedStepKeywords: collapseRepeatedStepKeywords,
                        searchIndexPieces: searchIndexPieces,
                        scenarioToolbarControls: scenarioToolbarControls,
                        toggleDefaults: toggles,
                        // Without this the flat outline rows compute their ids with no suite while the
                        // scenario <details> around them computes with one, so one report carries two
                        // identity schemes and the deep link from a flat row resolves to nothing.
                        suite: suite,
                        history: scenarioHistory,
                        historySlots: historySlots,
                        warmUp: warmUp,
                        stepDeferral: stepDeferral);
                    continue;
                }

                var failed = scenario.Result == ExecutionResult.Failed;
                var skipped = scenario.Result == ExecutionResult.Skipped;
                var depsAttr = scenarioDependencies.TryGetValue(scenario.Id, out var deps) && deps.Count > 0
                    ? $" data-dependencies=\"{System.Net.WebUtility.HtmlEncode(string.Join(",", deps.OrderBy(d => d)))}\""
                    : "";
                var statusAttr = $" data-status=\"{scenario.Result}\"";
                // A scenario that is not a test (plans/RUN_WINDOW_PLAN.md R1) keeps its status, so the status filters treat
                // it as before, and is marked for the readout and the CSV export, which count tests.
                var notATestAttr = scenario.NotATest ? " data-not-a-test=\"1\"" : "";

                // Duration attributes and badge
                var durationAttr = "";
                var durationBadge = "";
                if (scenario.Duration.HasValue && scenario.NotATest)
                {
                    // Its time is shown but is no test's: no data-duration-ms, which the duration filter, the duration:
                    // qualifier and the CSV read, and no speed colour, which judges a test.
                    durationBadge = $" <span class=\"duration-badge\">{FormatDurationBadge(scenario.Duration.Value)}</span>";
                }
                else if (scenario.Duration.HasValue)
                {
                    var durationMs = scenario.Duration.Value.TotalMilliseconds;
                    durationAttr = string.Create(CultureInfo.InvariantCulture, $" data-duration-ms=\"{durationMs:F0}\"");
                    var scenarioWarmUpMs = WarmUpMsOf(warmUp, scenario.Id);
                    var durationClass = DurationClass(durationMs - scenarioWarmUpMs);
                    durationBadge = scenarioWarmUpMs > 0
                        ? WarmUpBadge(durationClass, scenario.Duration.Value, scenarioWarmUpMs, warmUpByScenario[scenario.Id], ref durationAttr)
                        : $" <span class=\"duration-badge {durationClass}\">{FormatDurationBadge(scenario.Duration.Value)}</span>";
                }

                // Deep link anchor ID
                var anchorId = scenarioAnchorIds[scenario.Id];

                // Pre-build searchable text: feature context + scenario name + error info + step text + diagram sources + tags,
                // each section marked with its kind (plans/SEARCH_PLAN.md 4.6.1)
                var searchParts = new List<string>();
                var searchKinds = new SearchKinds(searchParts);
                searchKinds.Next('f'); searchParts.Add(feature.DisplayName);
                searchKinds.Next('n'); searchParts.Add(scenario.DisplayName);
                searchKinds.Next('d'); if (feature.Description is not null) searchParts.Add(feature.Description);
                searchKinds.Next('p'); if (feature.Endpoint is not null) searchParts.Add(feature.Endpoint);
                searchKinds.Next('d'); if (!string.IsNullOrWhiteSpace(scenario.Description)) searchParts.Add(scenario.Description);
                searchKinds.Next('r'); if (scenario.Rule is not null) searchParts.Add(scenario.Rule);
                searchKinds.Next('t'); searchParts.AddRange(ScenarioTags.Effective(feature, scenario));
                if (TagChips.RetryText(scenario) is { } retried) searchParts.Add(retried);
                searchKinds.Next('e'); if (failed && scenario.ErrorMessage is not null) searchParts.Add(scenario.ErrorMessage);
                searchKinds.Next('s'); CollectStepText(scenario.BackgroundSteps, searchParts);
                CollectStepText(scenario.Steps, searchParts);
                searchKinds.Next('g');
                if (scenarioDiagramSearchTerms.TryGetValue(scenario.Id, out var diagramTerms) && diagramTerms.Count > 0)
                    searchParts.AddRange(diagramTerms);
                searchKinds.Next('x'); AddExampleValueSearchParts(scenario, searchParts);
                var searchText = string.Join(" ", searchParts).ToLowerInvariant();
                var searchAttr = $" data-search=\"{System.Net.WebUtility.HtmlEncode(searchText)}\" data-search-kinds=\"{searchKinds}\"";
                searchIndexPieces?[scenario.Id].Add(searchText);

                // Its effective tags in their chips' spellings (plans/TAGS_PLAN.md §4.9), which the chips, @tag and the
                // link read; its badges are its own tags, its feature's being on the feature, then the retry badge (§4.6).
                var tagsAttr = tagChips.Attribute(feature.Tags, scenario);

                var encodedName = System.Net.WebUtility.HtmlEncode(scenario.DisplayName);
                var scenarioLabelsHtml = TagChips.Badges(TagChips.BadgeTags(feature.Tags, scenario)) + TagChips.RetryBadge(scenario);

                var scenarioTooltip = scenario.NotATest ? NotATestTitle : scenario.Result switch
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
                var scenarioStableId = ScenarioStableId.For(suite, feature, scenario);
                var historyEntry = scenarioHistory is null ? null : HistoryHtml.Entry(scenarioHistory, historySlots!, scenarioStableId);
                var historyAttr = historyEntry is null ? "" : HistoryHtml.VerdictAttribute(historyEntry);
                var historyBadges = historyEntry is null ? "" : HistoryHtml.Sparkline(historyEntry) + HistoryHtml.Pill(historyEntry);

                body.Append($"""
                         <details class="scenario{(scenario.IsHappyPath ? " happy-path" : "")}"{(toggles.ScenariosExpanded ? " open" : "")}{depsAttr}{statusAttr}{notATestAttr}{searchAttr}{durationAttr}{tagsAttr}{historyAttr} data-stable-id="{scenarioStableId}" id="{anchorId}" tabindex="0">
                            <summary class="h3{(failed ? " failed" : skipped ? " skipped" : "")}" title="{scenarioTooltip}">{encodedName}{(scenario.IsHappyPath ? " <span class=\"label\">Happy Path</span>" : "")}{(scenario.NotATest ? NotATestBadge : "")}{scenarioLabelsHtml}{durationBadge}{historyBadges}<button class="copy-scenario-name" title="Copy scenario name" data-scenario-name="{encodedName}" onclick="copy_scenario_name(this, event)">&#128203;</button><a class="scenario-link" href="#{anchorId}" title="Link to this scenario" onclick="event.stopPropagation()">&#128279;</a></summary>
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
                                 <summary class="h4" title="{FailureResultTitle}">Failure Result</summary>
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
                    toggles.StepsSectionOpen, toggles.BackgroundStepsOpen, stepDeferral);

                if (scenario.Attachments is { Length: > 0 })
                {
                    body.Append("""<div class="scenario-attachments">""");
                    foreach (var attachment in scenario.Attachments)
                    {
                        if (attachment.Problem is { } problem)
                        {
                            body.Append(MissingAttachmentHtml(attachment, problem));
                        }
                        else if (attachment.IsInlineImage)
                        {
                            body.Append(AttachmentImageLink(attachment, "scenario"));
                        }
                        else
                        {
                            body.Append(AttachmentLink(attachment, "scenario"));
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
                    spanWarning = string.Create(CultureInfo.InvariantCulture, $"<span class=\"span-count-warning\">(Warning: {count:N0} spans. This might indicate a problem/recursive loop in your test.)</span>");
                }

                if (hasSequenceDiagrams || hasWholeTestFlow)
                {
                    var hasActivityView = hasWholeTestFlow && !string.IsNullOrEmpty(wholeTestContent!.Value.ActivityHtml);
                    var hasFlameView = hasWholeTestFlow && !string.IsNullOrEmpty(wholeTestContent!.Value.FlameHtml);
                    var activeTab = ResolveDiagramTab(toggles.DiagramTab, hasSequenceDiagrams, hasActivityView, hasFlameView);

                    AppendDiagramSectionHead(body, toggles.DiagramsSectionOpen, hasSequenceDiagrams, hasActivityView, hasFlameView,
                        activeTab, isPlantUmlBrowser ? scenarioToolbarControls : "", spanWarning);

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
                                diagramDataMap[diagramId] = diagram.CodeBehind;
                                body.Append($"""
                                         <div class="plantuml-browser" id="{diagramId}" data-diagram-type="plantuml"></div>
                                         """);
                            }
                            else if (isInlineSvg)
                            {
                                var svgDiagramId = $"puml-svg-{plantUmlBrowserCounter++}";
                                diagramDataMap[svgDiagramId] = diagram.CodeBehind;
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
        if (hasFeatureTable) body.Append("</div>"); // the features panel
        body.Append("</div>"); // the Features tab's panel
        if (timelineHtml.Length > 0) body.Append(ReportPanel("timeline", startTab)).Append(timelineHtml).Append("</div>");
        if (componentHtml.Length > 0) body.Append(ReportPanel("components", startTab)).Append(componentHtml).Append("</div>");
        if (historyHtml.Length > 0) body.Append(ReportPanel("history", startTab)).Append(historyHtml).Append("</div>");
        if (diagnosticsHtml.Length > 0) body.Append(ReportPanel("diagnostics", startTab)).Append(diagnosticsHtml).Append("</div>");
        body.Append(SearchPanel);
        body.Append("</div>"); // the panels

        // Jump-to-failure button (only when there are failures)
        if (hasFailures)
        {
            body.Append($"""<button class="jump-to-failure" onclick="jump_to_next_failure()" title="Go to the next failed scenario; the count says which one of how many">Next Failure <span class="failure-counter" id="failure-counter">(0/{failureCount})</span></button>""");
        }

        // Back-to-top FAB (#10)
        body.Append("""<button class="back-to-top" id="back-to-top" onclick="window.scrollTo({top:0,behavior:'smooth'})" aria-label="Back to top" title="Back to the top of the report">↑</button>""");

        html += body;
        // The page is written compressed (plans/HTML_COMPRESSION_PLAN.md 4.6): a diagram's place in #puml-data is a token,
        // U+0001 and its number, and its source goes in the diagram sources' pieces in the tokens' order. The map holds each
        // source raw, or in gzip where it came from a whole-test flow's activity diagram.
        var diagramSources = new List<string>(diagramDataMap.Count);
        if (diagramDataMap.Count > 0)
        {
            var tokens = new Dictionary<string, string>(diagramDataMap.Count, StringComparer.Ordinal);
            foreach (var (id, source) in diagramDataMap)
            {
                tokens[id] = CompressedPage.Token(diagramSources.Count);
                diagramSources.Add(CompressedPage.IsGzipBase64(source) ? InternalFlowHtmlGenerator.DecompressFromBase64(source) : source);
            }
            html += "<script id=\"puml-data\" type=\"application/json\">";
            html += System.Text.Json.JsonSerializer.Serialize(tokens);
            html += "</script>";
        }
        if (stepDeferral is { Map.Count: > 0 })
        {
            html += "<script id=\"step-data\" type=\"application/json\">";
            html += System.Text.Json.JsonSerializer.Serialize(stepDeferral.Map);
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

        return WriteFile(
            CompressedPage.Build(CompressedPage.Raw(html), diagramSources,
                new CompressedPage.LoaderParts(title, fileName, KronikolVersion, themeBootScript, faviconLink)),
            fileName);
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
                pieces.Add(CompressedPage.ItemText(m.Groups[1].Value, attribute: true));
            foreach (Match m in Regex.Matches(activityHtml, "class=\"plantuml-browser[^\"]*\" id=\"([^\"]+)\""))
                if (diagramDataMap.TryGetValue(m.Groups[1].Value, out var source))
                    pieces.Add(CompressedPage.ItemText(source, attribute: false));
        }

        if (!string.IsNullOrEmpty(flameHtml))
        {
            foreach (Match m in Regex.Matches(flameHtml, "data-flame-z=\"([^\"]+)\""))
            {
                var json = CompressedPage.ItemText(m.Groups[1].Value, attribute: true);
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
    /// Where each kind of part starts in a scenario's search text (plans/SEARCH_PLAN.md 4.6.1): a letter and an offset per
    /// run of one kind (f the feature's name, n a name, d a description, p the endpoint, r the rule, t the tags, e the error,
    /// s the steps, g the diagrams' participants and call paths, x the examples), written as <c>data-search-kinds</c>
    /// beside <c>data-search</c>, whose text is unchanged. The offsets are counted as the page reads the attribute: the
    /// encoder keeps a CR and the HTML parser makes CRLF and a lone CR one LF, so each CRLF before a part moves it one back;
    /// lower-casing leaves the length as it is (<see cref="string.ToLowerInvariant()"/> maps one UTF-16 unit to one).
    /// </summary>
    private sealed class SearchKinds(List<string> parts)
    {
        private readonly List<(char Kind, int From)> _marks = [];

        /// <summary>The parts added from here on are of <paramref name="kind"/>.</summary>
        public void Next(char kind) => _marks.Add((kind, parts.Count));

        public override string ToString()
        {
            var starts = new int[parts.Count + 1];
            for (var i = 0; i < parts.Count; i++)
                starts[i + 1] = starts[i] + ParsedLength(parts[i]) + 1;
            var runs = new StringBuilder();
            var last = '\0';
            for (var m = 0; m < _marks.Count; m++)
            {
                var (kind, from) = _marks[m];
                var to = m + 1 < _marks.Count ? _marks[m + 1].From : parts.Count;
                if (to <= from || kind == last) continue;
                if (runs.Length > 0) runs.Append(' ');
                runs.Append(kind).Append(starts[from].ToString(CultureInfo.InvariantCulture));
                last = kind;
            }
            return runs.ToString();
        }

        // the part's length once the parser has read it: CRLF and a lone CR are one LF
        private static int ParsedLength(string part)
        {
            var length = part.Length;
            for (var i = 0; i + 1 < part.Length; i++)
                if (part[i] == '\r' && part[i + 1] == '\n') length--;
            return length;
        }
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

    /// <summary>
    /// One step of the Specifications YAML: a plain string, or a mapping when it has sub-steps under it.
    /// </summary>
    /// <remarks>
    /// Sub-steps used to be written as a deeper-indented <c>-</c> beneath the parent's own scalar, which
    /// is not nesting - a parser folds those lines into the parent's text, so a step with one sub-step
    /// read back as <c>When the order is placed - And payment processed</c>. It went unnoticed because
    /// every step was a plain scalar and a plain scalar swallows the lines silently; a step that has to
    /// be quoted (one ending in <c>:</c>, which is what a step introducing a data table looks like)
    /// turns the same shape into a parse error instead. So the nesting is real now. A step with no
    /// sub-steps stays the plain string it has always been, which is most of them, and is what keeps the
    /// file worth reading.
    /// </remarks>
    private static void AppendYamlStep(StringBuilder yml, ScenarioStep step, string indent)
    {
        var text = step.Keyword is not null ? $"{step.Keyword} {step.Text}" : step.Text;

        if (step.SubSteps is not { Length: > 0 })
        {
            AppendYaml(yml, indent + "- ", text);
            return;
        }

        AppendYaml(yml, indent + "- Step: ", text);
        yml.Append(indent).Append("  SubSteps:\n");
        foreach (var sub in step.SubSteps)
            AppendYamlStep(yml, sub, indent + "    ");
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

    /// <summary>A tab of the report bar (5.0.0, S3): the selected one is in the tab order, the others reached by the arrow
    /// keys (report-tabs-script.js); its title says what the tab shows (D49).</summary>
    private static string ReportTab(string key, string label, string title, string selected, int? count = null) =>
        $"<button class=\"report-tab\" id=\"tab-{key}\" role=\"tab\" aria-controls=\"panel-{key}\" aria-selected=\"{(key == selected ? "true" : "false")}\" tabindex=\"{(key == selected ? "0" : "-1")}\" data-tab=\"{key}\" title=\"{title}\">{label}"
        + (count is { } n ? $"<span class=\"tab-n\">{n.ToString(CultureInfo.InvariantCulture)}</span>" : "") + "</button>";

    /// <summary>A tab's panel: hidden unless its tab is the one selected.</summary>
    private static string ReportPanel(string key, string selected) =>
        $"<div class=\"report-panel\" id=\"panel-{key}\" role=\"tabpanel\" aria-labelledby=\"tab-{key}\" data-panel=\"{key}\"{(key == selected ? "" : " hidden")}>";

    /// <summary>
    /// The Timeline tab's controls (5.0.0, plans/OPTION_C_PLAN.md S11, D27), at the right of the timeline's header line:
    /// Show, Filtered or Full, and where a row paid a first-call warm-up, Warm-up, Shown or Hidden, and Order, With warm-up
    /// or Without warm-up. Each is a group of buttons whose first starts pressed (<c>aria-pressed</c>), each titled with what
    /// it shows; report-timeline-script.js acts on them.
    /// </summary>
    private static string TimelineControls(bool warmUp)
    {
        var html = new StringBuilder("<div class=\"timeline-controls\">");
        html.Append(TimelineSegments("Show", "view",
            ("filtered", "Filtered", "Only the scenarios the filters show"),
            ("full", "Full", "Every scenario in the run, whatever the filters")));
        if (warmUp)
        {
            html.Append(TimelineSegments("Warm-up", "warm",
                ("shown", "Shown", "Bars and times include the first-call warm-up each scenario paid, marked in its bar"),
                ("hidden", "Hidden", "Bars and times leave the warm-up out: each scenario's own time")));
            html.Append(TimelineSegments("Order", "order",
                ("with", "With warm-up", "Slowest first, counting the warm-up"),
                ("without", "Without warm-up", "Slowest first by each scenario's own time, warm-up left out")));
        }
        return html.Append("</div>").ToString();
    }

    /// <summary>One of the timeline's control groups: its label, then a button for each choice, the first pressed.</summary>
    private static string TimelineSegments(string label, string key, params (string Value, string Text, string Title)[] choices)
    {
        var html = new StringBuilder($"<span class=\"tl-seg\" role=\"group\" aria-label=\"{label}\"><span class=\"tl-seg-label\">{label}</span>");
        for (var i = 0; i < choices.Length; i++)
            html.Append($"<button type=\"button\" class=\"tl-seg-btn\" data-{key}=\"{choices[i].Value}\" aria-pressed=\"{(i == 0 ? "true" : "false")}\" title=\"{System.Net.WebUtility.HtmlEncode(choices[i].Title)}\">{choices[i].Text}</button>");
        return html.Append("</span>").ToString();
    }

    /// <summary>What Filtered leaves out, said under the timeline's header line while it draws fewer rows than it has
    /// (D27); report-timeline-script.js writes the words. The Components tab says it under its own header line (D28), in
    /// words report-components-script.js writes. The sentence is what a screen reader announces, and straight after it
    /// is the way back, Clear All, as where no scenario is left (5.1.0).</summary>
    private const string FilteredNote =
        "<div class=\"filtered-note\" hidden><span class=\"filtered-note-text\" role=\"status\"></span>"
        + "<button type=\"button\" class=\"filtered-note-clear\" title=\"Clear every filter\">Clear All</button></div>";

    /// <summary>
    /// The Components tab's header line (5.0.0, plans/OPTION_C_PLAN.md S11, D26, D28): its heading, which is the diagram's
    /// title (the drawing leaves the title out), with the timeline's info mark, and where <paramref name="controls"/>, the
    /// tab's controls at the right of the line, in the timeline's groups (<see cref="TimelineSegments"/>): Show, Filtered
    /// or Full, where <paramref name="usage"/> (the report knows which scenarios made each connection's calls); Direction,
    /// Left to right or Top to bottom; and Call summaries, Shown or Hidden. report-components-script.js acts on them.
    /// </summary>
    private static string ComponentHeader(string heading, bool controls, bool usage)
    {
        var info = "The Component Diagram shows the services, databases and queues the scenarios called, and the calls between them. "
            + "Each connection is labelled with the calls made along it and how many tests made them. "
            + (usage ? "Filtered draws only what the scenarios the filters show used; Full draws the whole run. " : "")
            + "Use it to see what a set of tests depends on.";
        var html = new StringBuilder($"<div class=\"timeline-header component-header\">{System.Net.WebUtility.HtmlEncode(heading)} <span class=\"timeline-info\" title=\"{System.Net.WebUtility.HtmlEncode(info)}\">&#x1F6C8;</span>");
        if (!controls)
            return html.Append("</div>").ToString();
        html.Append("<div class=\"timeline-controls\">");
        if (usage)
            html.Append(TimelineSegments("Show", "view",
                ("filtered", "Filtered", "Only the components and connections the scenarios the filters show used"),
                ("full", "Full", "Every component and connection of the run, whatever the filters")));
        html.Append(TimelineSegments("Direction", "dir",
            ("lr", "Left to right", "Each caller to the left of what it calls"),
            ("tb", "Top to bottom", "Each caller above what it calls")));
        html.Append(TimelineSegments("Call summaries", "calls",
            ("shown", "Shown", "Each connection labelled with its calls and how many tests made them"),
            ("hidden", "Hidden", "The connections without their labels")));
        return html.Append("</div></div>").ToString();
    }

    /// <summary>
    /// Which scenarios on the page made the calls each of <paramref name="diagram"/>'s connections draws, for the Components
    /// tab's Filtered (5.0.0, plans/OPTION_C_PLAN.md S11, D28): their stable ids in the page's order, and for each
    /// connection its scenarios as places in that list. Null where that is not known: a connection whose tests the report
    /// was not given, or no connection made by a scenario on the page (the calls carry ids no scenario has), where Filtered
    /// would draw nothing whatever the filters.
    /// </summary>
    private static (string[] StableIds, int[][] ByConnection)? ComponentUsage(EmbeddedComponentDiagram diagram, Feature[] features, string? suite)
    {
        if (diagram.Connections.Any(c => c.TestIds is null))
            return null;
        var used = diagram.Connections.SelectMany(c => c.TestIds!).ToHashSet(StringComparer.Ordinal);
        var stableIds = new List<string>();
        var placeOf = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var feature in features)
            foreach (var scenario in feature.Scenarios)
                if (used.Contains(scenario.Id) && placeOf.TryAdd(scenario.Id, stableIds.Count))
                    stableIds.Add(ScenarioStableId.For(suite, feature, scenario));
        var byConnection = diagram.Connections
            .Select(c => c.TestIds!.Where(placeOf.ContainsKey).Select(t => placeOf[t]).Distinct().OrderBy(i => i).ToArray())
            .ToArray();
        return byConnection.Any(s => s.Length > 0) ? (stableIds.ToArray(), byConnection) : null;
    }

    /// <summary>
    /// The Components tab's model of its diagram, for report-components-script.js (5.0.0, plans/OPTION_C_PLAN.md S11, D28):
    /// the line of the direction, of each component and of each connection in the source the tab draws, each connection's
    /// two components (places in the components) and its line without the label, and where <paramref name="usage"/> is
    /// known, each connection's scenarios and their stable ids. The page reads nothing back from the source or a name.
    /// </summary>
    private static string ComponentModelJson(EmbeddedComponentDiagram diagram, (string[] StableIds, int[][] ByConnection)? usage)
    {
        var connections = diagram.Connections.Select((c, i) =>
        {
            var connection = new Dictionary<string, object> { ["line"] = c.Line, ["from"] = c.From, ["to"] = c.To, ["bare"] = c.Bare };
            if (usage is { } known)
                connection["scenarios"] = known.ByConnection[i];
            return connection;
        }).ToArray();
        var model = new Dictionary<string, object>
        {
            ["direction"] = diagram.DirectionLine,
            ["components"] = diagram.ComponentLines,
            ["connections"] = connections
        };
        if (usage is { } all)
            model["scenarios"] = all.StableIds;
        return System.Text.Json.JsonSerializer.Serialize(model);
    }

    /// <summary>
    /// A filter chip (5.0.0, plans/OPTION_C_PLAN.md S6): off, then has, which takes the row's active class as in 4.x, and on
    /// a three-way row has not, its exclude class (report-filter-chips-script.js cycles them; a row starts two-way from
    /// 5.10.0, plans/REPORT_UI_FIXES_PLAN.md I1). The chip for the scenarios with none carries <c>data-none</c> and a
    /// reserved value, which the link writes. Its title counts the scenarios it picks out, then says what a click does in
    /// the row's start ways (D49); the count is <c>data-scenarios</c> too, from which the script rebuilds the title when the
    /// row's ways change.
    /// </summary>
    private static string FilterChip(string family, string value, string label, int scenarios, bool none = false, bool more = false) =>
        string.Create(CultureInfo.InvariantCulture,
            $"<button type=\"button\" class=\"{family}-toggle{(none ? $" {family}-none" : "")}\" data-{family}=\"{System.Net.WebUtility.HtmlEncode(value)}\"{(none ? " data-none=\"1\"" : "")}{(more ? " data-more=\"1\" hidden" : "")} data-scenarios=\"{scenarios}\" aria-pressed=\"false\" title=\"{ScenarioCount(scenarios)}. {ChipClicksIn(ChipWaysDefault)}\">{System.Net.WebUtility.HtmlEncode(label)}</button>");

    /// <summary>A filter row's start ways: two-way, a chip on or off (5.10.0, plans/REPORT_UI_FIXES_PLAN.md C2, C13).</summary>
    private const string ChipWaysDefault = "2";

    /// <summary>
    /// What a click on a filter chip does in its row's ways, after its count (D49); report-filter-chips-script.js writes the
    /// same words when a row's ways change.
    /// </summary>
    private static string ChipClicksIn(string ways) => ways == "3"
        ? "Click: only these. Click again: leave them out. Once more: off. Alt+click or right-click leaves them out at once."
        : "Click: only these. Click again: off.";

    /// <summary>
    /// A filter row's ways switch, after its ANY / ALL (5.10.0, plans/REPORT_UI_FIXES_PLAN.md C1, C9, C10): its text says
    /// the row's ways, 2-WAY or 3-WAY, and is its name, so it has no aria-label and no aria-pressed, as ANY / ALL has
    /// neither; its title names the ways and what a click on it does. report-filter-chips-script.js writes the same words.
    /// </summary>
    private static string FilterWaysToggle(string cssClass, string ways) =>
        $"<button type=\"button\" class=\"{cssClass}\" data-ways=\"{ways}\" title=\"{(ways == "3" ? ThreeWaysTitle : TwoWaysTitle)}\">{ways}-WAY</button>";

    private const string TwoWaysTitle = "Two-way chips: a click shows only a chip's scenarios, a second click shows every scenario again. "
        + "Click for three-way chips, where the second click leaves the chip's scenarios out (red) and a third shows them again.";

    private const string ThreeWaysTitle = "Three-way chips: a click shows only a chip's scenarios, a second leaves them out (red), a third shows "
        + "every scenario again. Click for two-way chips.";

    private static string ScenarioCount(int n) => n == 1 ? "1 scenario" : string.Create(CultureInfo.InvariantCulture, $"{n} scenarios");

    /// <summary>
    /// Whether a feature holds a test: one that holds only scenarios that are not tests (<see cref="Scenario.NotATest"/>,
    /// a stack's start-up) is no feature of the run's in the band, the readout and the features table's counts.
    /// </summary>
    private static bool HoldsATest(Feature feature) => feature.Scenarios.Any(s => !s.NotATest);

    /// <summary>The summary title of a scenario that is not a test, in place of its result's (plans/RUN_WINDOW_PLAN.md R1).</summary>
    private const string NotATestTitle = "Not a test: what it holds is drawn, and the run's counts and times leave it out";

    /// <summary>A scenario that is not a test says so beside its name, muted as every label is.</summary>
    private const string NotATestBadge = " <span class=\"label not-a-test\">not a test</span>";

    /// <summary>
    /// A long Tags row's More chip (plans/TAGS_PLAN.md §4.8, Q9): it shows the chips the row starts without, and hides them
    /// again but for those that are set (<c>report-filter-chips-script.js</c>).
    /// </summary>
    private static string TagMoreChip(int hidden) => string.Create(CultureInfo.InvariantCulture,
        $"<button type=\"button\" class=\"tag-more\" data-shown=\"{TagChips.MostShown}\" aria-expanded=\"false\" title=\"Show the other {hidden} {(hidden == 1 ? "tag" : "tags")}. The row starts with the {TagChips.MostShown} tags the most scenarios carry.\">{hidden} more</button>");

    /// <summary>A filter row's ANY / ALL switch; the mode underneath, OR or AND, is what the link writes (D11).</summary>
    private static string FilterModeToggle(string cssClass, string mode, string title) =>
        $"<button type=\"button\" class=\"{cssClass}\" data-mode=\"{mode}\" title=\"{title}\">{(mode == "AND" ? "ALL" : "ANY")}</button>";

    /// <summary>A filter row's Clear, shown while one of its chips is set (D12).</summary>
    private static string GroupClear(string noun) =>
        $"<button type=\"button\" class=\"group-clear\" title=\"Clear the {noun} filters\" hidden>Clear</button>";

    /// <summary>
    /// What Clear All clears (5.0.0, plans/OPTION_C_PLAN.md D49): the filter field's text, with the qualifiers in it (5.11.0
    /// adds tag:, status: and feature: to dep:, duration: and is:), and every row the box has, in their order: Status
    /// always, then Duration, Happy paths, Tags and Dependencies where the box has them. The search is no filter and stays
    /// (5.12.0).
    /// </summary>
    private static string ClearAllTitle(Feature[] features, bool hasDurations, bool hasDependencies) =>
        "Clear every filter: the filter text with its tag:, status:, feature:, dep:, duration: and is: terms, and the "
        + Listed(FilterRows(features, hasDurations, hasDependencies, "statuses"));

    /// <summary>The rows the filter box has, in their order, the first named <paramref name="status"/>.</summary>
    private static List<string> FilterRows(Feature[] features, bool hasDurations, bool hasDependencies, string status)
    {
        var runs = features.SelectMany(f => f.Scenarios).ToArray();
        var rows = new List<string> { status };
        if (hasDurations) rows.Add("duration");
        if (runs.Any(s => s.IsHappyPath)) rows.Add("happy paths");
        if (features.Any(f => f.Scenarios.Any(s => s.IsHappyPath || ScenarioTags.Effective(f, s).Length > 0))) rows.Add("tags");
        if (hasDependencies) rows.Add("dependencies");
        return rows;
    }

    /// <summary>Items in words: "a", "a and b", "a, b and c".</summary>
    private static string Listed(List<string> items) =>
        items.Count == 1 ? items[0] : string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1];

    /// <summary>A status filter's title (D49): the scenarios it shows, and that statuses picked together add up.</summary>
    private static string StatusToggleTitle(ExecutionResult status)
    {
        var what = status switch
        {
            ExecutionResult.Passed => "passed",
            ExecutionResult.Failed => "failed",
            ExecutionResult.Skipped => "were skipped",
            _ => "were bypassed"
        };
        return $"Show only the scenarios that {what}; pick more statuses to show them together";
    }

    /// <summary>The title of both readouts, the box's and the bar's (D49).</summary>
    private const string FiltersLeaveShowing = "The scenarios and features the filters leave showing";

    /// <summary>
    /// The search on the title line (plans/SEARCH_PLAN.md 4.2): GitHub's button drawn as a text box, "Type / to search",
    /// which opens the query builder; once a search is applied it shows the query (report-search-ui-script.js), and its
    /// clear, beside it in the box since a button cannot hold a button, empties the query and nothing else. The search's
    /// query is its own (report-search-function.js's search_query), never the filter field's #searchbar: a search is no
    /// filter (the owner, 2026-10-27).
    /// </summary>
    internal const string SearchBox =
        """<span class="search-box"><button type="button" class="search-button" id="search-button" aria-haspopup="dialog" aria-expanded="false" aria-controls="search-dialog" aria-keyshortcuts="/" aria-label="Search or jump to, type / to search" title="Search this report, or jump to a scenario or a feature (/)">"""
        + SearchIcon + """<span class="search-button-hint">Type <kbd aria-hidden="true">/</kbd> to search</span><span class="search-button-query" hidden></span></button>"""
        + """<button type="button" class="search-clear" id="search-clear" aria-label="Clear the search" title="Clear the search; the filters stay as they are" hidden>"""
        + ClearIcon + """</button></span>""";

    private const string SearchIcon =
        """<svg class="search-icon" viewBox="0 0 16 16" aria-hidden="true" focusable="false"><circle cx="7" cy="7" r="4.6"/><path d="M10.4 10.4l3.6 3.6"/></svg>""";

    private const string ClearIcon =
        """<svg class="clear-icon" viewBox="0 0 16 16" aria-hidden="true" focusable="false"><circle cx="8" cy="8" r="6.2"/><path d="M5.8 5.8l4.4 4.4M10.2 5.8l-4.4 4.4"/></svg>""";

    /// <summary>
    /// The query builder (plans/SEARCH_PLAN.md 4.3): a native dialog over the page, its panel placed by the script on the
    /// search box (a sheet from the top on a phone); a combobox over a listbox of suggestions, a polite live region that
    /// counts them, and the syntax tips at its foot, in the page since a report is often read offline (G18). The tips say
    /// which parts of a scenario each search reads (B3: the help said the instant search read "diagram source").
    /// </summary>
    internal const string SearchDialog =
        """<dialog id="search-dialog" class="search-dialog" aria-labelledby="search-dialog-title"><div class="search-panel">"""
        + """<h2 id="search-dialog-title" class="search-visually-hidden">Search this report</h2>"""
        + """<div class="search-input-row">""" + SearchIcon
        + """<span id="search-input-label" class="search-visually-hidden">Search</span><div class="search-input-wrap"><div class="search-mirror" aria-hidden="true"></div>"""
        + """<input id="search-input" type="text" role="combobox" aria-expanded="false" aria-controls="search-suggestions" aria-autocomplete="list" aria-labelledby="search-input-label" spellcheck="false" autocomplete="off" placeholder="Search or jump to…" title="Words to find, and tag:, status:, feature:, dep:, duration: or is:happy to narrow them; Enter searches" /></div>"""
        + """<button type="button" class="search-input-clear" aria-label="Clear" title="Clear what is typed" hidden>""" + ClearIcon + """</button></div>"""
        + """<ul id="search-suggestions" class="search-suggestions" role="listbox" aria-label="Suggestions"></ul>"""
        + """<div id="search-tips" class="search-tips" hidden>""" + SearchTips + """</div>"""
        + """<div class="search-live search-visually-hidden" role="status" aria-live="polite" aria-atomic="true"></div>"""
        + """<div class="search-foot"><button type="button" class="search-tips-toggle" aria-expanded="false" aria-controls="search-tips" title="How to write a search: the forms, and what each search reads">Search syntax tips</button></div>"""
        + """</div></dialog>""";

    /// <summary>The Search tab (plans/SEARCH_PLAN.md 4.5.1): last in the bar, a magnifier before its name and its count of
    /// results after it, hidden until a search is applied (report-search-results-script.js shows it).</summary>
    private const string SearchTab =
        """<button class="report-tab" id="tab-search" role="tab" aria-controls="panel-search" aria-selected="false" tabindex="-1" data-tab="search" title="The scenarios the search matched, with the lines where each one matched" hidden>"""
        + SearchIcon + """Search<span class="tab-n">0</span></button>""";

    /// <summary>
    /// The Search tab's panel (plans/SEARCH_PLAN.md 4.5): GitHub's results page in the report's look, a sidebar beside the
    /// results from 768.02 px, one column below; report-search-results-script.js fills the frame.
    /// </summary>
    private const string SearchPanel =
        """<div class="report-panel" id="panel-search" role="tabpanel" aria-labelledby="tab-search" data-panel="search" hidden><div class="search-results">"""
        + """<aside class="search-sidebar" aria-label="Refine the results"></aside>"""
        + """<section class="search-main" aria-labelledby="search-count"><div class="search-progress" hidden></div><div class="search-phone-row"></div>"""
        + """<div class="search-head"><h2 id="search-count" class="search-count" tabindex="-1"></h2><span class="search-time"></span><span class="search-head-tools"></span></div>"""
        + """<div class="search-warning" hidden></div><div class="search-list"></div>"""
        + """<nav class="search-pages" aria-label="Pagination"></nav><p class="search-protip">Press <kbd>/</kbd> to search again and change the query.</p>"""
        + """</section></div></div>""";

    /// <summary>The syntax tips (plans/SEARCH_PLAN.md 4.3.5): the forms of 4.4 first, the older forms beside them, then what
    /// the instant search reads and what only deep search reads.</summary>
    private const string SearchTips = QueryForms
        + """<p class="search-tips-note"><code>feature:</code>, <code>dep:</code>, <code>duration:</code> and <code>is:</code> always narrow the search: they combine with the rest of it by AND, and can be left out with <code>-</code>, <code>!!</code> or <code>not</code>. Inside a group that <code>!!</code> or <code>not</code> negates, one is read as text, and the search says so.</p>"""
        + """<p class="search-tips-note">At once, the search reads names, descriptions, tags, steps, example values, error messages, the services a scenario calls and the paths of its HTTP calls. Once a word has three letters it also reads payloads, headers, SQL, whole diagrams and stack traces, which can take a moment on a large report. Without <code>&amp;&amp;</code>, <code>||</code> or <code>!!</code>, tag expressions such as <code>@a and @b or not @c</code> work too. Press <kbd>/</kbd> to search from anywhere in the report.</p>""";

    /// <summary>The forms a query is written in, the same for the search and the filter field (plans/SEARCH_PLAN.md 4.4):
    /// the table the builder's tips and the field's ? both show.</summary>
    private const string QueryForms =
        """<table class="search-tips-table"><thead><tr><th>Write</th><th>To find</th><th>For example</th></tr></thead><tbody>"""
        + """<tr><td><code>word</code></td><td>Scenarios holding every word</td><td><code>pancake flour</code></td></tr>"""
        + """<tr><td><code>"phrase"</code></td><td>The words together, as written</td><td><code>"create order"</code></td></tr>"""
        + """<tr><td><code>tag:name</code> <code>@tag</code></td><td>A tag the scenario or its feature has; quotes for a tag with spaces</td><td><code>tag:smoke</code>, <code>tag:"Happy Path"</code>, <code>@smoke</code></td></tr>"""
        + """<tr><td><code>status:failed</code> <code>$status</code></td><td>Passed, failed, skipped or bypassed, or a history verdict; a scenario skipped after a failure counts as skipped</td><td><code>status:failed</code>, <code>$passed</code></td></tr>"""
        + """<tr><td><code>feature:name</code></td><td>Part of the feature's name; commas for any of several</td><td><code>feature:pancakes</code>, <code>feature:"order service"</code></td></tr>"""
        + """<tr><td><code>dep:name</code></td><td>A dependency the scenario calls: part of its name is enough; commas for any of several</td><td><code>dep:payment</code>, <code>dep:payment,order</code></td></tr>"""
        + """<tr><td><code>duration:&gt;2s</code></td><td>The scenario's own time: &gt;, &gt;=, &lt;, &lt;= or a range; ms, s or m; p50, p90, p95, p99 are this report's percentiles</td><td><code>duration:&gt;p90</code>, <code>duration:1s..5s</code></td></tr>"""
        + """<tr><td><code>is:happy</code></td><td>Happy paths</td><td><code>is:happy</code></td></tr>"""
        + """<tr><td><code>-key:value</code></td><td>Leaves those out, as <code>!!</code> before it does</td><td><code>-tag:slow</code>, <code>-status:passed</code></td></tr>"""
        + """<tr><td><code>&amp;&amp;</code> <code>||</code> <code>!!</code> <code>( )</code></td><td>And, or, not, and grouping</td><td><code>(payment || order) &amp;&amp; !!refund</code></td></tr>"""
        + """</tbody></table>""";

    /// <summary>The button that reaches the search from anywhere down a phone's page (plans/SEARCH_PLAN.md 4.2, Q7, B15):
    /// the bar stays in the page on a phone, so once the title line is out of view this floats above back-to-top.</summary>
    internal const string SearchFab =
        """<button type="button" class="search-fab" id="search-fab" aria-haspopup="dialog" aria-controls="search-dialog" aria-label="Search or jump to" title="Search this report, or jump to a scenario or a feature (/)" hidden>"""
        + SearchIcon + """</button>""";

    /// <summary>
    /// The filter field (5.0.0 to 5.10.1, back in 5.12.0 at the owner's word: the filter section hidden when the page opens,
    /// its field in it as before). It filters the Features list as it is typed: report-search-function.js's search_scenarios
    /// runs on input, so a paste or a drop is read at once, and Enter runs it once (B1, B2). The ? beside it shows the
    /// syntax, and the deep search's chip sits inside it. Its query is the filter's own; the search on the title line has
    /// another.
    /// </summary>
    private const string FilterField =
        """<div class="filter-search"><span class="search-field"><input id="searchbar" autocomplete="off" placeholder="Filter... (@tag, $status, dep:, duration:, &&, ||, !!)" oninput="search_scenarios()" title="Filter the scenarios by the words in their names, descriptions, tags, steps and errors, and, once a word has three letters, their payloads, SQL and diagrams. Combine words with &amp;&amp; (and), || (or) and !! (not); tag:, status:, feature:, dep:, duration: and is:happy narrow by tag, status, feature, dependency, time and happy path; ? shows the syntax" /></span>"""
        + """<button type="button" class="search-help-toggle" onclick="toggle_search_help()" title="How to write a filter: words, &quot;phrases&quot;, tag:, status:, feature:, dep:, duration:, is:happy, &amp;&amp;, ||, !! and parentheses">?</button></div>""";

    /// <summary>The filter field's syntax, shown and hidden by its ?: the forms the search's tips show, and what the filter
    /// reads.</summary>
    private const string FilterHelp =
        """<div class="search-help-panel" style="display:none">""" + QueryForms
        + """<p class="search-help-note"><code>feature:</code>, <code>dep:</code>, <code>duration:</code> and <code>is:</code> always narrow the filter: they combine with the rest of it by AND, and can be left out with <code>-</code>, <code>!!</code> or <code>not</code>. Inside a group that <code>!!</code> or <code>not</code> negates, one is read as text, and the field's edge turns amber.</p>"""
        + """<p class="search-help-note">At once, the filter reads names, descriptions, tags, steps, example values, error messages, the services a scenario calls and the paths of its HTTP calls. Once a word has three letters it also reads payloads, headers, SQL, whole diagrams and stack traces, which can take a moment on a large report; the note inside the field says when that is done. Without <code>&amp;&amp;</code>, <code>||</code> or <code>!!</code>, tag expressions such as <code>@a and @b or not @c</code> work too.</p></div>""";

    /// <summary>
    /// The report bar's filters (S7, D24): a copy of the readout, long and short, seen and not heard (the bar's status
    /// element says it, plans/SEARCH_PLAN.md F8); Clear All while anything is filtered, titled with what it clears
    /// (<paramref name="clearTitle"/>, D49); and Filters, always shown, which opens and closes the box at the top of the
    /// bar and counts the filters on (F3, F4, F6).
    /// </summary>
    private static string NavFilters(string readout, string scenarios, string clearTitle) =>
        $"""<div class="nav-filters" role="group" aria-label="Filters"><span class="nav-readout" aria-hidden="true" title="{FiltersLeaveShowing}"><span class="nav-readout-long">{readout}</span><span class="nav-readout-short">All {scenarios}</span></span><button type="button" class="nav-clear" title="{clearTitle}" hidden>Clear All</button><button type="button" class="nav-filters-toggle" aria-expanded="false" aria-controls="filtering-box" aria-label="Filters" title="Show the filters"><svg viewBox="0 0 16 16" width="14" height="14" aria-hidden="true" focusable="false"><path d="M2.5 3h11l-4.25 5v4.25l-2.5 1.25V8z" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linejoin="round"/></svg><span>Filters</span><span class="filters-count" aria-hidden="true" hidden></span></button></div>""";

    /// <summary>The box's close (S7, D24), always shown since the box is shown and hidden in its place (plans/SEARCH_PLAN.md F4).</summary>
    private const string FiltersClose =
        """<button type="button" class="filters-close" aria-label="Hide the filters" title="Hide the filters (Esc)"><svg viewBox="0 0 16 16" width="14" height="14" aria-hidden="true" focusable="false"><path d="M4 4l8 8M12 4l-8 8" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"/></svg></button>""";

    /// <summary>What the Features panel says when the filters leave no scenario, with the way back (S7, D24).</summary>
    private const string FiltersEmpty =
        """<div class="filters-empty" hidden><span>No scenarios match the filters.</span><button type="button" class="filters-empty-clear" title="Clear every filter">Clear All</button></div>""";

    /// <summary>The Features tab's views, Full (the cards) and Summary (the features table), as tabs (S4, D21).</summary>
    private static string FeaturesViewSwitch(bool summary) =>
        $"<div class=\"view-switch\" role=\"tablist\" aria-label=\"Features view\" data-start-view=\"{(summary ? "summary" : "full")}\">"
        + ViewTab("full", "Full", "report-content", "Each feature as a card with its scenarios", !summary)
        + ViewTab("summary", "Summary", "features-table-view", "One table row per feature, with its counts and times", summary)
        + "</div>";

    private static string ViewTab(string key, string label, string panel, string title, bool selected) =>
        $"<button type=\"button\" class=\"view-btn\" id=\"view-{key}\" role=\"tab\" aria-controls=\"{panel}\" aria-selected=\"{(selected ? "true" : "false")}\" tabindex=\"{(selected ? "0" : "-1")}\" data-view=\"{key}\" title=\"{title}\">{label}</button>";

    /// <summary>
    /// Expand and collapse all features, or all scenarios, as a label and two icon buttons (S4, D22, item R17.2). A button
    /// with nothing to do is dimmed, not disabled, so it keeps its place in the tab order: expand while every one starts
    /// open, collapse while none does, both when there are none.
    /// </summary>
    private static string FoldGroup(string noun, int count, bool expanded) =>
        $"<span class=\"fold-group\" role=\"group\" aria-label=\"Expand or collapse all {noun}\"><span class=\"fold-label\" aria-hidden=\"true\">{noun}</span>"
        + FoldButton("expand", noun, count == 0 || expanded, "<polyline points=\"4,6 8,2 12,6\"/><polyline points=\"4,10 8,14 12,10\"/>")
        + FoldButton("collapse", noun, count == 0 || !expanded, "<polyline points=\"4,2 8,6 12,2\"/><polyline points=\"4,14 8,10 12,14\"/>")
        + "</span>";

    private static string FoldButton(string action, string noun, bool idle, string chevrons)
    {
        var name = $"{(action == "expand" ? "Expand" : "Collapse")} all {noun}";
        return $"<button type=\"button\" class=\"fold-btn fold-{action}\" data-fold=\"{noun}\" title=\"{name}\" aria-label=\"{name}\" aria-disabled=\"{(idle ? "true" : "false")}\">"
            + "<svg viewBox=\"0 0 16 16\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\" focusable=\"false\">"
            + chevrons + "</svg></button>";
    }

    /// <summary>
    /// The sort for the feature cards and the table's rows alike (S4, D21). Report order is the order the page is written
    /// in; each other option names the table column it sorts on, so a report without step or duration columns offers
    /// only what it has (the preview sorted on fixed column numbers, and called the report's own order Feature A-Z).
    /// </summary>
    private static string FeaturesSortGroup(int? durationCol)
    {
        var options = new StringBuilder("<option value=\"order\">Report order</option><option value=\"name\" data-col=\"0\">Feature A–Z</option>"
            + "<option value=\"failed\" data-col=\"3\">Failures first</option><option value=\"scenarios\" data-col=\"1\">Most scenarios</option>");
        if (durationCol is { } d)
            options.Append(CultureInfo.InvariantCulture, $"<option value=\"duration\" data-col=\"{d}\">Slowest total</option><option value=\"average\" data-col=\"{d + 1}\">Slowest average</option><option value=\"longest\" data-col=\"{d + 2}\">Longest scenario</option>");
        return "<label class=\"sort-group\"><span class=\"sort-label\">Sort</span><select id=\"features-sort\" class=\"sort-select\" autocomplete=\"off\" aria-label=\"Sort features\" title=\"The order the features are listed in\">"
            + options + "</select></label>";
    }

    /// <summary>On a phone, the gear that shows the report bar's display settings (S4, D35).</summary>
    private const string ToolsGear =
        "<button type=\"button\" class=\"tools-gear\" aria-label=\"Display settings\" title=\"Display settings\" aria-expanded=\"false\" aria-controls=\"display-settings\">\u2699</button>";

    /// <summary>
    /// The run band (5.0.0, plans/OPTION_C_PLAN.md S2, D3): the ring, then the test execution summary (status, when,
    /// and the counts), then the CI zone when the run had one, as one row the page's CSS stacks when it does not fit.
    /// </summary>
    private static void AppendRunBand(StringBuilder body, string overallStatus, DateTime startRunTime, DateTime endRunTime,
        int features, int scenarios, int passed, int failed, int skipped, int bypassed, CiMetadata? ci)
    {
        static string Encode(string value) => System.Net.WebUtility.HtmlEncode(value);
        static string Stat(string label, string value, string? cls = null) =>
            $"<div class=\"run-stat{(cls is null ? "" : " " + cls)}\"><dt>{label}</dt><dd>{value}</dd></div>";

        body.Append("<section class=\"run-band\" aria-label=\"Test execution summary\">");
        var ring = GeneratePieChartSvg(passed, failed, skipped, bypassed);
        if (ring.Length > 0)
            body.Append("<div class=\"run-ring\">").Append(ring).Append("</div>");

        var when = startRunTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " · "
                   + startRunTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "–"
                   + endRunTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " UTC";
        body.Append("<div class=\"run-zone run-summary\"><div class=\"run-head\"><h2 class=\"run-title\">Test Execution Summary</h2>");
        body.Append($"<span class=\"run-status run-status-{overallStatus.ToLowerInvariant()}\">{overallStatus}</span>");
        body.Append($"<span class=\"run-when\">{when}</span></div>");
        body.Append("<dl class=\"run-stats\">");
        body.Append(Stat("Scenarios", scenarios.ToString(CultureInfo.InvariantCulture)));
        body.Append(Stat("Passed", passed.ToString(CultureInfo.InvariantCulture), "run-stat-pass"));
        body.Append(Stat("Failed", failed.ToString(CultureInfo.InvariantCulture), failed > 0 ? "run-stat-fail" : null));
        body.Append(Stat("Skipped", skipped.ToString(CultureInfo.InvariantCulture)));
        if (bypassed > 0)
            body.Append(Stat("Bypassed", bypassed.ToString(CultureInfo.InvariantCulture)));
        body.Append(Stat("Features", features.ToString(CultureInfo.InvariantCulture)));
        body.Append(Stat("Duration", FormatDuration(endRunTime - startRunTime)));
        body.Append("</dl></div>");

        if (ci is not null)
        {
            var provider = ci.Provider switch
            {
                CiEnvironment.GitHubActions => "GitHub Actions",
                CiEnvironment.AzureDevOps => "Azure DevOps",
                _ => ci.Provider.ToString()
            };
            body.Append($"<div class=\"run-zone run-ci\"><div class=\"run-head\"><h2 class=\"run-title\">CI</h2><span class=\"run-when\">{provider}</span></div>");
            body.Append("<dl class=\"run-stats\">");
            if (ci.BuildNumber is not null)
                body.Append(Stat("Build", "#" + Encode(ci.BuildNumber)));
            if (ci.Branch is not null)
                body.Append(Stat("Branch", Encode(ci.Branch)));
            if (ci.CommitSha is not null)
            {
                var shortSha = ci.CommitSha.Length > 7 ? ci.CommitSha[..7] : ci.CommitSha;
                body.Append(Stat("Commit", $"<code title=\"{Encode(ci.CommitSha)}\">{Encode(shortSha)}</code>"));
            }
            if (ci.Repository is not null)
                body.Append(Stat("Repository", Encode(ci.Repository)));
            if (ci.PipelineUrl is not null)
                body.Append(Stat("Run", $"<a class=\"run-link\" href=\"{Encode(ci.PipelineUrl)}\" target=\"_blank\" rel=\"noopener noreferrer\" title=\"Open the CI run this report came from\">View run</a>"));
            body.Append("</dl></div>");
        }
        body.Append("</section>");
    }

    internal static string GeneratePieChartSvg(int passed, int failed, int skipped, int bypassed) =>
        RingSvg(passed, failed, skipped, bypassed, passed + failed + skipped + bypassed, track: false, arcTitles: true);

    /// <summary>
    /// The rate a run's ring reads (5.0.0): 100 only when every scenario passed, otherwise the share that passed, rounded down
    /// and at most 99, so a run with any scenario that did not pass never reads 100% (rounding put 213 of 214 at 100 until
    /// 5.0.0). 0 for a run of no scenarios.
    /// </summary>
    internal static int RingRate(int passed, int total) =>
        total <= 0 ? 0 : passed >= total ? 100 : (int)Math.Min(99, Math.Floor(100.0 * passed / total));

    /// <summary>
    /// A run's ring (5.0.0): an arc for each outcome the run had, passed, failed, skipped and bypassed in that order, each its
    /// share of <paramref name="total"/>, with the rate inside (<see cref="RingRate"/>); the arcs and the rate take their
    /// colours from the page's tokens through their classes. The run band draws the run's own (<see cref="GeneratePieChartSvg"/>),
    /// each arc titled with its count, and nothing for a run of no scenarios. The History section draws one per run of its
    /// window (<c>HistoryHtml</c>, plans/OPTION_C_PLAN.md S11) over a track (<paramref name="track"/>), which shows the
    /// scenarios no arc counts, with untitled arcs, so the run's own tooltip shows wherever the ring is pointed at; there a run
    /// of no scenarios is the track alone, with a dash for its rate.
    /// </summary>
    internal static string RingSvg(int passed, int failed, int skipped, int bypassed, int total, bool track, bool arcTitles)
    {
        total = Math.Max(total, passed + failed + skipped + bypassed);
        if (total == 0 && !track) return "";

        var passRate = RingRate(passed, total);
        var segments = new List<(double pct, string kind, string label, int count)>();
        if (passed > 0) segments.Add((100.0 * passed / total, "pass", "Passed", passed));
        if (failed > 0) segments.Add((100.0 * failed / total, "fail", "Failed", failed));
        if (skipped > 0) segments.Add((100.0 * skipped / total, "skip", "Skipped", skipped));
        if (bypassed > 0) segments.Add((100.0 * bypassed / total, "bypass", "Bypassed", bypassed));

        const double radius = 40;
        const double circumference = 2 * Math.PI * radius;
        var sb = new StringBuilder();
        // The arcs and the rate take their colours from the page's tokens through these classes (5.0.0).
        sb.Append(total == 0
            ? "<svg viewBox=\"0 0 100 100\" role=\"img\" aria-label=\"no scenarios\">"
            : $"<svg viewBox=\"0 0 100 100\" role=\"img\" aria-label=\"{passRate}% passed\">");
        if (track)
            sb.Append(CultureInfo.InvariantCulture, $"<circle class=\"ring-track\" cx=\"50\" cy=\"50\" r=\"{radius:F1}\" fill=\"none\" stroke-width=\"12\"></circle>");

        var offset = 0.0;
        foreach (var (pct, kind, label, count) in segments)
        {
            var dash = circumference * pct / 100.0;
            var gap = circumference - dash;
            var dashOffset = -offset * circumference / 100.0;
            sb.Append(CultureInfo.InvariantCulture,
                $"<circle class=\"ring-{kind}\" cx=\"50\" cy=\"50\" r=\"{radius:F1}\" fill=\"none\" stroke-width=\"12\" " +
                $"stroke-dasharray=\"{dash:F2} {gap:F2}\" stroke-dashoffset=\"{dashOffset:F2}\" transform=\"rotate(-90 50 50)\">");
            if (arcTitles)
                sb.Append(CultureInfo.InvariantCulture, $"<title>{label}: {count} ({pct:F0}%)</title>");
            sb.Append("</circle>");
            offset += pct;
        }

        sb.Append(total == 0
            ? "<text class=\"ring-rate\" x=\"50\" y=\"50\" text-anchor=\"middle\" dominant-baseline=\"central\">&#8211;</text>"
            : $"<text class=\"ring-rate\" x=\"50\" y=\"50\" text-anchor=\"middle\" dominant-baseline=\"central\">{passRate}%</text>");
        sb.Append("</svg>");
        return sb.ToString();
    }

    /// <summary>
    /// A duration in whole units, cut and never rounded up, as <see cref="FormatDurationBadge"/> cuts: "850ms" under a second,
    /// "44s" under a minute, "2m 26s" under an hour and "1h 5m" from an hour (5.0.0; it wrote "65m 0s" until then). The run
    /// band's duration, the features table and the History section's bars write it (plans/OPTION_C_PLAN.md S11, the owner's
    /// round 17 item 30); a scenario's badge and the timeline keep <see cref="FormatDurationBadge"/>'s tenth of a second.
    /// </summary>
    internal static string FormatDuration(TimeSpan duration)
    {
        var total = duration.Duration();
        if (total.TotalSeconds < 1)
            return string.Create(CultureInfo.InvariantCulture, $"{total.Milliseconds}ms");
        if (total.TotalMinutes < 1)
            return string.Create(CultureInfo.InvariantCulture, $"{total.Seconds}s");
        if (total.TotalHours < 1)
            return string.Create(CultureInfo.InvariantCulture, $"{total.Minutes}m {total.Seconds}s");
        return string.Create(CultureInfo.InvariantCulture, $"{(long)total.TotalHours}h {total.Minutes}m");
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

        var boundaryLogs = InternalFlowSegmentBuilder.BuildWholeTestBoundaries(trackedLogs, scenarioId);

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

    /// <summary>
    /// A parameterized group's id, unique as a scenario's is (5.2.0, plans/BACK_BUTTON_NAVIGATION_PLAN.md S9): two groups
    /// of one name, or a group named as a scenario is, shared an id, and a link to the second reached the first. The
    /// first keeps the plain id, the next take <c>-2</c>, <c>-3</c> and on, as scenarios do.
    /// </summary>
    private static string UniqueAnchorId(string baseAnchor, HashSet<string>? taken)
    {
        if (taken is null)
            return baseAnchor;
        var id = baseAnchor;
        for (var n = 2; !taken.Add(id); n++)
            id = string.Create(CultureInfo.InvariantCulture, $"{baseAnchor}-{n}");
        return id;
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
        Dictionary<string, string>? scenarioAnchorIds = null,
        HashSet<string>? takenAnchorIds = null,
        string? featureDisplayName = null,
        string? featureKey = null,
        string? featureDescription = null,
        string? featureEndpoint = null,
        string[]? featureTags = null,
        TagChips? tagChips = null,
        Dictionary<string, Merge.WholeTestFlowFragment>? precomputedWholeTestContent = null,
        bool showNoInteractionsMarker = false,
        bool separateBackgroundSteps = false,
        bool collapseRepeatedStepKeywords = true,
        Dictionary<string, List<string>>? searchIndexPieces = null,
        string scenarioToolbarControls = "",
        ResolvedToggleDefaults? toggleDefaults = null,
        string? suite = null,
        HistoryVerdicts? history = null,
        Dictionary<string, int>? historySlots = null,
        WarmUpResult? warmUp = null,
        StepDeferral? stepDeferral = null)
    {
        var toggles = toggleDefaults ?? ResolvedToggleDefaults.BuiltIn;
        var scenarios = group.Scenarios;

        // Cross-run history per row: the group element carries the union of its rows' verdicts (so
        // `$broke` finds the group), its summary one pill for the worst of them, and every row its own.
        Dictionary<string, ScenarioHistory>? rowHistory = null;
        var groupHistoryAttr = "";
        var groupHistoryBadge = "";
        if (history is not null && historySlots is not null)
        {
            rowHistory = new Dictionary<string, ScenarioHistory>(StringComparer.Ordinal);
            foreach (var row in scenarios)
            {
                var rowId = ScenarioStableId.For(suite, featureKey ?? featureDisplayName ?? "", row);
                if (HistoryHtml.Entry(history, historySlots, rowId) is { } rowEntry)
                    rowHistory[row.Id] = rowEntry;
            }
            groupHistoryAttr = HistoryHtml.VerdictAttribute(rowHistory.Values);
            groupHistoryBadge = HistoryHtml.GroupPill(rowHistory.Values);
        }
        string RowHistoryAttr(Scenario row) => rowHistory is not null && rowHistory.TryGetValue(row.Id, out var entry) ? HistoryHtml.VerdictAttribute(entry) : "";

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

        // Build search text, each section marked with its kind (plans/SEARCH_PLAN.md 4.6.1)
        var searchParts = new List<string>();
        var searchKinds = new SearchKinds(searchParts);
        searchKinds.Next('n'); searchParts.Add(group.GroupDisplayName);
        searchKinds.Next('f'); if (featureDisplayName is not null) searchParts.Add(featureDisplayName);
        searchKinds.Next('d'); if (featureDescription is not null) searchParts.Add(featureDescription);
        searchKinds.Next('p'); if (featureEndpoint is not null) searchParts.Add(featureEndpoint);
        searchKinds.Next('t'); if (featureTags is { Length: > 0 }) searchParts.AddRange(featureTags);
        foreach (var s in scenarios)
        {
            searchKinds.Next('n'); searchParts.Add(s.DisplayName);
            searchKinds.Next('d'); if (!string.IsNullOrWhiteSpace(s.Description)) searchParts.Add(s.Description);
            searchKinds.Next('r'); if (s.Rule is not null) searchParts.Add(s.Rule);
            searchKinds.Next('t'); searchParts.AddRange(ScenarioTags.OwnOf(s));
            if (TagChips.RetryText(s) is { } retried) searchParts.Add(retried);
            searchKinds.Next('e'); if (s.ErrorMessage is not null) searchParts.Add(s.ErrorMessage);
            searchKinds.Next('s'); CollectStepText(s.BackgroundSteps, searchParts);
            CollectStepText(s.Steps, searchParts);
            searchKinds.Next('g');
            if (scenarioDiagramSearchTerms.TryGetValue(s.Id, out var diagramTerms) && diagramTerms.Count > 0)
                searchParts.AddRange(diagramTerms);
            searchKinds.Next('x');
            if (hasBlockStructure)
            {
                if (!string.IsNullOrEmpty(s.ExamplesBlockName)) searchParts.Add(s.ExamplesBlockName);
                if (!string.IsNullOrEmpty(s.ExamplesBlockDescription)) searchParts.Add(s.ExamplesBlockDescription);
            }
            AddExampleValueSearchParts(s, searchParts);
        }
        var groupSearchText = string.Join(" ", searchParts).ToLowerInvariant();
        var searchAttr = $" data-search=\"{System.Net.WebUtility.HtmlEncode(groupSearchText)}\" data-search-kinds=\"{searchKinds}\"";
        // Each member scenario is a deep-search doc; the group <details> is the element whose
        // data-search the client verify pass reads, so the group text is part of every member's corpus.
        if (searchIndexPieces is not null)
            foreach (var s in scenarios)
                searchIndexPieces[s.Id].Add(groupSearchText);

        // Aggregate tags and dependencies: the group matches a chip any of its rows matches (plans/TAGS_PLAN.md §4.8),
        // and shows the tags all its rows share (F23)
        var tagsAttr = tagChips?.GroupAttribute(featureTags, scenarios) ?? "";
        var groupTagBadges = TagChips.Badges(TagChips.GroupBadgeTags(featureTags, scenarios));
        var allDeps = scenarios.Where(s => scenarioDependencies.ContainsKey(s.Id)).SelectMany(s => scenarioDependencies[s.Id]).Distinct().OrderBy(d => d).ToArray();
        var depsAttr = allDeps.Length > 0 ? $" data-dependencies=\"{System.Net.WebUtility.HtmlEncode(string.Join(",", allDeps))}\"" : "";

        // Total duration
        var totalDuration = scenarios.Where(s => s.Duration.HasValue).Select(s => s.Duration!.Value).Aggregate(TimeSpan.Zero, (acc, d) => acc + d);
        var durationAttr = totalDuration > TimeSpan.Zero ? string.Create(CultureInfo.InvariantCulture, $" data-duration-ms=\"{totalDuration.TotalMilliseconds:F0}\"") : "";
        var groupWarmUpMs = scenarios.Sum(s => WarmUpMsOf(warmUp, s.Id));
        var durationBadge = totalDuration > TimeSpan.Zero
            ? groupWarmUpMs > 0
                ? WarmUpBadge(DurationClass(totalDuration.TotalMilliseconds - groupWarmUpMs), totalDuration, groupWarmUpMs, scenarios.SelectMany(s => warmUp is null ? Enumerable.Empty<WarmUpMark>() : warmUp.Calls.Values.Where(m => m.ScenarioId == s.Id)), ref durationAttr)
                : $" <span class=\"duration-badge {DurationClass(totalDuration.TotalMilliseconds)}\">{FormatDurationBadge(totalDuration)}</span>"
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

        var anchorId = UniqueAnchorId(GenerateScenarioAnchorId(group.GroupDisplayName), takenAnchorIds);
        var encodedGroupName = System.Net.WebUtility.HtmlEncode(group.GroupDisplayName);
        var isGroupHappyPath = scenarios.Any(s => s.IsHappyPath);
        var happyPathClass = isGroupHappyPath ? " happy-path" : "";
        var happyPathBadge = isGroupHappyPath ? " <span class=\"label\">Happy Path</span>" : "";

        body.Append($"<details class=\"scenario scenario-parameterized{happyPathClass}\"{(toggles.ScenariosExpanded ? " open" : "")} data-status=\"{overallStatus}\"{(scenarios.Length > 1 ? string.Create(CultureInfo.InvariantCulture, $" data-runs=\"{scenarios.Length}\"") : "")}{depsAttr}{searchAttr}{durationAttr}{tagsAttr}{groupHistoryAttr} id=\"{anchorId}\" tabindex=\"0\">");
        body.Append($"<summary class=\"h3{(hasFailure ? " failed" : hasSkipped ? " skipped" : "")}\">{encodedGroupName}{happyPathBadge}{groupTagBadges}{summaryText}{durationBadge}{groupHistoryBadge}<button class=\"copy-scenario-name\" title=\"Copy scenario name\" data-scenario-name=\"{encodedGroupName}\" onclick=\"copy_scenario_name(this, event)\">&#128203;</button><a class=\"scenario-link\" href=\"#{anchorId}\" title=\"Link to this scenario\" onclick=\"event.stopPropagation()\">&#128279;</a></summary>");

        // Parameter table — where both views exist, the configured default picks which one
        // starts visible (toggleFlattenParams reads live visibility, so it needs no seeding).
        var hasFlatView = group.FlatParameterNames is { Length: > 0 };
        var startGrouped = hasFlatView && toggles.ParameterTableView == ParameterTableView.Grouped;
        // The wrapper scrolls a table wider than the scenario, whose content-visibility would clip it out
        // of sight: the table is a scroll container itself only at phone widths.
        body.Append("<div class=\"param-table-wrapper\">");

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
                if (featureTags is { Length: > 0 }) rowSearchParts.AddRange(featureTags);
                rowSearchParts.AddRange(ScenarioTags.OwnOf(s));
                if (TagChips.RetryText(s) is { } rowRetried) rowSearchParts.Add(rowRetried);
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
                var flatRowStableId = ScenarioStableId.For(suite, featureKey ?? featureDisplayName ?? "", s);
                body.Append($"<tr class=\"{rowStatusClass}{activeClass}\" data-row-idx=\"{ri}\"{RowHistoryAttr(s)} data-stable-id=\"{flatRowStableId}\"{rowSearchAttr} onclick=\"selectRow(this,'{prefix}')\">");
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
            if (featureTags is { Length: > 0 }) rowSearchParts.AddRange(featureTags);
            rowSearchParts.AddRange(ScenarioTags.OwnOf(s));
            if (TagChips.RetryText(s) is { } rowRetried) rowSearchParts.Add(rowRetried);
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

            var rowAnchorId = scenarioAnchorIds?.GetValueOrDefault(s.Id) ?? GenerateScenarioAnchorId(ScenarioStableId.KeyName(s));
            // A row's slug is not its own: many outlines' rows share one display name, and a Reqnroll row whose name
            // was filled keys its slug on the outline's title as before (#101), so the stable id, which hashes the
            // example values, is the only handle `#sid-` can use.
            var rowStableId = ScenarioStableId.For(suite, featureKey ?? featureDisplayName ?? "", s);
            body.Append($"<tr class=\"{rowStatusClass}{activeClass}\" data-row-idx=\"{ri}\"{RowHistoryAttr(s)} data-stable-id=\"{rowStableId}\" id=\"{rowAnchorId}\" data-scenario-id=\"{rowAnchorId}\"{rowSearchAttr} onclick=\"selectRow(this,'{prefix}')\">");
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
        body.Append("</div>"); // close param-table-wrapper

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
                    toggles.StepsSectionOpen, toggles.BackgroundStepsOpen, stepDeferral);

                if (s.Attachments is { Length: > 0 })
                {
                    body.Append("""<div class="scenario-attachments">""");
                    foreach (var attachment in s.Attachments)
                    {
                        if (attachment.Problem is { } problem)
                        {
                            body.Append(MissingAttachmentHtml(attachment, problem));
                        }
                        else if (attachment.IsInlineImage)
                        {
                            body.Append(AttachmentImageLink(attachment, "scenario"));
                        }
                        else
                        {
                            body.Append(AttachmentLink(attachment, "scenario"));
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
                    body.Append($"<details class=\"failure-result\" open><summary class=\"h4\" title=\"{FailureResultTitle}\">Failure Result</summary><pre>");
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
            // The views the group has
            var showSeqToggle = hasAnySeqDiagrams;
            var showActivityToggle = hasAnyWholeTestFlow && wholeTestContents.Any(w => !string.IsNullOrEmpty(w?.ActivityHtml));
            var showFlameToggle = hasAnyWholeTestFlow && wholeTestContents.Any(w => !string.IsNullOrEmpty(w?.FlameHtml));
            var multipleTypes = (showSeqToggle ? 1 : 0) + (showActivityToggle ? 1 : 0) + (showFlameToggle ? 1 : 0) > 1;
            var activeTab = ResolveDiagramTab(toggles.DiagramTab, showSeqToggle, showActivityToggle, showFlameToggle);
            AppendDiagramSectionHead(body, toggles.DiagramsSectionOpen, showSeqToggle, showActivityToggle, showFlameToggle,
                activeTab, isPlantUmlBrowser ? scenarioToolbarControls : "", warning: "");

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
                diagramDataMap[diagramId] = diagram.CodeBehind;
                body.Append($"<div class=\"plantuml-browser\" id=\"{diagramId}\" data-diagram-type=\"plantuml\"></div>");
            }
            else if (isInlineSvg)
            {
                var svgDiagramId = $"puml-svg-{plantUmlBrowserCounter++}";
                diagramDataMap[svgDiagramId] = diagram.CodeBehind;
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
        bool backgroundStepsOpen = false,
        StepDeferral? deferral = null)
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
                body.Append("""<summary class="h4" title="The steps of the feature's background, which run before this scenario's own">Background Steps</summary>""");
                for (var bi = 0; bi < background.Length; bi++)
                {
                    var numberPrefix = showStepNumbers ? $"{bi + 1}." : null;
                    RenderStep(body, background[bi], numberPrefix, skipTabularInline: false,
                        displayKeyword: backgroundKeywords?[bi], isBackground: true, deferral: deferral);
                }
                body.Append("</details>");
            }

            if (steps.Length > 0)
                RenderStepsList(body, steps, showStepNumbers, background.Length, backgroundCount: 0, collapseRepeatedStepKeywords, stepsSectionOpen, deferral);

            return;
        }

        ScenarioStep[] combined = background.Length == 0 ? steps
            : steps.Length == 0 ? background
            : [.. background, .. steps];

        if (combined.Length == 0)
            return;

        RenderStepsList(body, combined, showStepNumbers, numberOffset: 0, backgroundCount: background.Length, collapseRepeatedStepKeywords, stepsSectionOpen, deferral);
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
        bool open = true,
        StepDeferral? deferral = null)
    {
        var displayKeywords = collapseRepeatedStepKeywords ? StepKeywordCollapser.DisplayKeywords(steps) : null;

        body.Append($"""<details class="scenario-steps"{(open ? " open" : "")}>""");
        // how long each took only where the steps say (D49)
        body.Append($"""<summary class="h4" title="This scenario's steps{(steps.Any(s => s.Duration.HasValue) ? ", with how long each took" : "")}">Steps</summary>""");

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
                displayKeyword: displayKeywords?[si], isBackground: si < backgroundCount, deferral: deferral);
        }

        if (renderCombined)
            RenderCombinedTabularParameters(body, steps);

        body.Append("</details>");
    }

    /// <summary>
    /// The tooltip of each status a step's icon can show, keyed by the icon's status class: the texts each icon carried
    /// as a title attribute until the page held them once (<c>plans/REPORT_FORMAT_PLAN.md</c> R2). A passed step whose
    /// sub-steps include a skipped or bypassed one has a class and a text of its own.
    /// </summary>
    internal static readonly IReadOnlyList<KeyValuePair<string, string>> StepStatusTitles =
    [
        new("passed", "Passed — all assertions in this step passed"),
        new("passed-skipped", "Passed (with skipped sub-steps) — all assertions passed, but one or more sub-steps were skipped. Skipped steps did not execute and also prevented execution of subsequent steps"),
        new("passed-bypassed", "Passed (with bypassed sub-steps) — all assertions passed, but one or more sub-steps were bypassed (intentionally skipped over without preventing execution of subsequent steps)"),
        new("failed", "Failed — this step threw an exception or an assertion failed"),
        new("skipped", "Skipped — this step did not execute because it was intentionally skipped, either at the scenario level, or at the step level. In the latter case the skip also prevented execution of subsequent steps"),
        new("bypassed", "Bypassed — some or all of the logic in this step was intentionally skipped over without preventing execution of subsequent steps"),
        new("skipped-after-failure", "Skipped after failure — this step was never reached because an earlier step failed")
    ];

    /// <summary>The page script that gives a status icon its title, with <see cref="StepStatusTitles"/> in it.</summary>
    private static string StepStatusTitlesScript() =>
        LoadResource("report-step-status-titles.js").Replace("__STEP_STATUS_TITLES__",
            System.Text.Json.JsonSerializer.Serialize(StepStatusTitles.ToDictionary(t => t.Key, t => t.Value),
                new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
            StringComparison.Ordinal);

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
        string? displayKeyword = null, bool isBackground = false, StepDeferral? deferral = null)
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
            // No title: the page holds each status's text once, and gives an icon its title when it is hovered
            // (StepStatusTitles, report-step-status-titles.js).
            body.Append($"<span class=\"step-status {statusClass}\">{statusIcon}</span>");
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
                            body.Append($"<button class=\"step-table-ref\" onclick=\"toggle_table_ref(this)\" data-param=\"{System.Net.WebUtility.HtmlEncode(seg.TableReference)}\" data-value=\"{System.Net.WebUtility.HtmlEncode(json)}\" title=\"Show or hide the value of {System.Net.WebUtility.HtmlEncode(seg.TableReference)}\">{System.Net.WebUtility.HtmlEncode(seg.TableReference)}</button>");
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
                            body.Append($"<button class=\"step-table-ref\" onclick=\"toggle_table_ref(this)\" data-param=\"{System.Net.WebUtility.HtmlEncode(seg.TableReference)}\" title=\"Show {System.Net.WebUtility.HtmlEncode(seg.TableReference)} in its table\">{System.Net.WebUtility.HtmlEncode(seg.TableReference)}</button>");
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

        // A bypassed step says why, as its first comment line: the markup Kronikol4J writes for the same step. The reason
        // was in the data and the query verbs, and on the page nowhere (#105).
        if (step.Status == ExecutionResult.Bypassed && !string.IsNullOrWhiteSpace(step.BypassReason))
            body.Append($"<div class=\"step-comment\">Bypassed: {System.Net.WebUtility.HtmlEncode(step.BypassReason)}</div>");

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
                if (attachment.Problem is { } problem)
                {
                    body.Append(MissingAttachmentHtml(attachment, problem));
                }
                else if (attachment.IsInlineImage)
                {
                    body.Append(AttachmentImageLink(attachment, "step"));
                    body.Append($"<span class=\"attachment-image-name\">{System.Net.WebUtility.HtmlEncode(attachment.Name)}</span>");
                }
                else
                {
                    body.Append(AttachmentLink(attachment, "step"));
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
            // A closed composite (none of its steps failed) outside a held subtree renders its sub-steps apart, and the page
            // holds them until the step is first opened when they come to StepDeferral.Threshold or more; one inside a held
            // subtree goes into the same entry (5.0.0, plans/REPORT_FORMAT_PLAN.md 4.8 part b).
            var held = deferral is { Inside: false } && !HasAnyFailed(step);
            var subSteps = held ? new StringBuilder() : body;
            if (!held)
                body.Append("<div class=\"sub-steps\">");
            if (held)
                deferral!.Inside = true;
            try
            {
                for (var ssi = 0; ssi < step.SubSteps!.Length; ssi++)
                {
                    var subPrefix = numberPrefix is not null ? $"{numberPrefix}{ssi + 1}." : null;
                    // Its tables inline: the combined table under a scenario's steps gathers top-level steps' only, so a
                    // sub-step left to the default skipped its tables and drew them nowhere (until 4.27.1).
                    RenderStep(subSteps, step.SubSteps[ssi], subPrefix, skipTabularInline: false, isBackground: isBackground, deferral: deferral);
                }
            }
            finally
            {
                if (held)
                    deferral!.Inside = false;
            }
            if (!held)
                body.Append("</div>");
            else if (subSteps.Length >= StepDeferral.Threshold)
                body.Append($"<div class=\"sub-steps\" data-steps=\"{deferral!.Add(subSteps.ToString())}\" data-count=\"{step.SubSteps!.Length}\"></div>");
            else
                body.Append("<div class=\"sub-steps\">").Append(subSteps).Append("</div>");
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

    /// <summary>The badge colour for a duration: under 2 s fast, under 5 s moderate, else slow.</summary>
    private static string DurationClass(double ms) => ms < 2000 ? "duration-fast" : ms < 5000 ? "duration-moderate" : "duration-slow";

    /// <summary>The milliseconds of a scenario's duration its first-call warm-up took; 0 when it paid none.</summary>
    private static double WarmUpMsOf(WarmUpResult? warmUp, string scenarioId) =>
        warmUp is not null && warmUp.ScenarioMs.TryGetValue(scenarioId, out var ms) ? ms : 0;

    /// <summary>Milliseconds as a <c>data-duration-ms</c> or <c>data-warmup-ms</c> attribute writes them (<c>F0</c>), which
    /// is what the page's scripts read back.</summary>
    private static double WholeMs(double ms) =>
        double.Parse(ms.ToString("F0", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    /// <summary>The value at <paramref name="p"/> of sorted values, by nearest rank: the P50 of two is the smaller, where
    /// <c>sorted[(int)(n * p)]</c> picked the larger until 4.9.0.</summary>
    private static double Percentile(double[] sorted, double p) =>
        sorted.Length == 0 ? 0 : sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1)];

    /// <summary>
    /// A duration badge that says how much of it was the run's first-call warm-up (plans/WARM_UP_PLAN.md Q5, A): the wall
    /// time stays its text, the warm-up follows as muted text, the tooltip names the calls, and its colour is the one
    /// the time left earns. <paramref name="durationAttr"/> gains <c>data-warmup-ms</c>, which the duration filter
    /// subtracts.
    /// </summary>
    private static string WarmUpBadge(string durationClass, TimeSpan duration, double warmUpMs, IEnumerable<WarmUpMark> calls, ref string durationAttr)
    {
        durationAttr += string.Create(CultureInfo.InvariantCulture, $" data-warmup-ms=\"{warmUpMs:F0}\"");
        var tooltip = System.Net.WebUtility.HtmlEncode(WarmUpTooltip(duration, warmUpMs, calls));
        return $" <span class=\"duration-badge {durationClass}\" title=\"{tooltip}\">{FormatDurationBadge(duration)}"
               + $"<span class=\"warm-up-note\"> · {FormatDurationBadge(TimeSpan.FromMilliseconds(warmUpMs))} warm-up</span></span>";
    }

    /// <summary>
    /// <c>1.27 s, 1.19 s of it first-call warm-up: POST /orders 661 ms (later calls 7.6 ms), GET /milk 82 ms (2.3 ms)</c>:
    /// the slowest call first, and a call that waited for the run's first says so.
    /// </summary>
    internal static string WarmUpTooltip(TimeSpan duration, double warmUpMs, IEnumerable<WarmUpMark> calls)
    {
        // As the badge and the timeline write it (FormatDurationBadge): under a second cut to whole milliseconds, so the
        // tooltip never reads 697 ms beside a badge reading 696ms, and from a minute whole minutes and seconds, cut, where
        // it read 62 s beside 1m 1s (until 4.16.3). Between them, to the hundredth the badge rounds to a tenth.
        static string Time(double ms)
        {
            if (ms < 1000)
                return string.Create(CultureInfo.InvariantCulture, $"{Math.Floor(ms):0} ms");
            var badge = FormatDurationBadge(TimeSpan.FromMilliseconds(ms));
            return badge.Contains('m') ? badge : string.Create(CultureInfo.InvariantCulture, $"{ms / 1000:0.##} s");
        }
        static string Baseline(double ms) => string.Create(CultureInfo.InvariantCulture, $"{ms:0.#} ms");

        var text = new StringBuilder($"{Time(duration.TotalMilliseconds)}, {Time(warmUpMs)} of it first-call warm-up: ");
        var first = true;
        foreach (var call in calls.OrderByDescending(c => c.DurationMs))
        {
            if (!first)
                text.Append(", ");
            text.Append(call.Shape).Append(' ').Append(Time(call.DurationMs))
                .Append(call.Kind == WarmUpMark.WaitedKind ? ", waiting for the run's first" : "")
                .Append(" (").Append(first ? "later calls " : "").Append(Baseline(call.BaselineMs)).Append(')');
            first = false;
        }
        return text.ToString();
    }

    internal static string FormatDurationBadge(TimeSpan duration)
    {
        var total = duration.Duration();
        if (total.TotalSeconds < 1)
            return $"{(int)total.TotalMilliseconds}ms";
        if (total.TotalMinutes < 1)
        {
            var seconds = string.Create(CultureInfo.InvariantCulture, $"{total.TotalSeconds:F1}");
            // Just under a minute the tenth rounds up to one: written as the minute it reads as, not as 60.0s (until 4.16.3).
            return seconds == "60.0" ? "1m 0s" : seconds + "s";
        }
        return $"{(int)total.TotalMinutes}m {total.Seconds}s";
    }

    internal static string GenerateScenarioAnchorId(string displayName)
    {
        // Convert to lowercase, replace non-alphanumeric with hyphens, collapse multiple hyphens
        var slug = System.Text.RegularExpressions.Regex.Replace(displayName.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return $"scenario-{slug}";
    }

    /// <summary>What the Diagnostics tab shows: its title, and its heading's in the tab (5.0.0, plans/OPTION_C_PLAN.md D49).</summary>
    private const string DiagnosticsDescription = "What Kronikol noticed while building this report, such as calls it could not put under a scenario";

    /// <summary>
    /// The "Report diagnostics" block: a <c>&lt;details&gt;</c> listing every <see cref="DiagnosticEntry"/> the
    /// generation (and the host, via <see cref="Kronikol.Ingestion.IngestRequest.HostDiagnostics"/>) recorded, by
    /// kind, message and scenario, so a dead tap or a skipped capture line is a line on a page, not only in a log.
    /// Drawn open on the labs page (<see cref="LabsReportGenerator"/>), and in <c>TestRunReport.html</c> as its
    /// Diagnostics tab when <see cref="ReportConfigurationOptions.ShowReportDiagnosticsSection"/> asks for it (the
    /// default since 5.0.0), open with its summary out of the tab order (<paramref name="inTab"/>). Empty input
    /// renders nothing.
    /// </summary>
    internal static string RenderReportDiagnostics(IReadOnlyList<DiagnosticEntry> diagnostics, bool open = false, bool inTab = false)
    {
        if (diagnostics.Count == 0)
            return string.Empty;

        var byKind = diagnostics.GroupBy(d => d.Kind)
            .OrderByDescending(g => g.Key == DiagnosticKind.CaptureDegraded)
            .ThenByDescending(g => g.Count())
            .Select(g => g.Count() == 1 ? g.Key.ToString() : $"{g.Key} ×{g.Count()}");
        var summary = $"Report diagnostics ({diagnostics.Count}: {string.Join(", ", byKind)})";

        var html = new StringBuilder();
        html.Append($"<details class=\"report-diagnostics\"{(open || inTab ? " open" : "")}>");
        // In its tab the summary is the section's heading and nothing to click: the tabs script keeps it open. Its title
        // says what the section is, and only where it is a disclosure that a click shows or hides it (D49).
        var title = inTab ? DiagnosticsDescription : "What Kronikol noticed while building this report: click to show or hide the list";
        html.Append($"<summary{(inTab ? " tabindex=\"-1\"" : "")} title=\"{title}\">{summary}</summary>"); // enum names and counts only: nothing to encode, and the × must stay a glyph
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
    /// The <c>Background calls</c> section: interactions captured under a scenario's identity after the
    /// scenario had ended (<see cref="BackgroundAttribution"/>), grouped by the scenario they were taken
    /// from and the call they made. Renders nothing when the run had none, so a report without the section
    /// is a report without background calls.
    /// </summary>
    internal static string RenderBackgroundCalls(BackgroundCalls background)
    {
        if (background.Calls == 0)
            return string.Empty;

        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in background.AfterScenarioEnd)
            names[group.ScenarioId] = group.ScenarioName;

        var rows = background.Interactions
            .Where(l => !l.IsDiagramMarker)
            .GroupBy(l => (
                Scenario: l.ExpiredFromTestId is { Length: > 0 } from ? names.GetValueOrDefault(from, from) : "(no scenario)",
                l.ServiceName,
                Method: MethodText(l.Method) ?? "",
                Path: l.Uri is null ? "" : l.Uri.IsAbsoluteUri ? l.Uri.AbsolutePath : l.Uri.ToString()))
            .Select(g => (g.Key, Calls: g.Select(l => l.RequestResponseId).Distinct().Count(), LastAt: g.Max(l => l.Timestamp)))
            .OrderBy(r => r.Key.Scenario, StringComparer.Ordinal)
            .ThenBy(r => r.Key.ServiceName, StringComparer.Ordinal)
            .ThenBy(r => r.Key.Path, StringComparer.Ordinal)
            .ThenBy(r => r.Key.Method, StringComparer.Ordinal);

        var html = new StringBuilder();
        html.Append("<details class=\"background-calls\">");
        // The heading counts what the table holds: expired calls sit under the scenario they expired from,
        // and calls kept with no identity at all (CaptureBackground) under "(no scenario)".
        var afterEnd = background.AfterScenarioEnd.Sum(g => g.Calls);
        var noScenario = Math.Max(0, background.Calls - afterEnd);
        var heading = afterEnd > 0 && noScenario > 0
            ? $"{background.Calls.ToString(CultureInfo.InvariantCulture)}: {afterEnd.ToString(CultureInfo.InvariantCulture)} after a scenario ended, {noScenario.ToString(CultureInfo.InvariantCulture)} with no scenario"
            : afterEnd > 0
                ? $"{afterEnd.ToString(CultureInfo.InvariantCulture)} after a scenario ended"
                : $"{noScenario.ToString(CultureInfo.InvariantCulture)} with no scenario";
        html.Append($"<summary>Background calls ({heading})</summary>");
        html.Append("<p class=\"background-calls-note\">Calls that belong to no scenario. A row under a scenario arrived under its identity after it had ended: a hosted service, a timer or a message consumer started inside the host and still running, whose work belongs to the host and is listed here instead of under the scenario. A row under (no scenario) was captured with no scenario at all and kept because CaptureBackground is on.</p>");
        html.Append("<table class=\"background-calls-table\"><thead><tr><th>Scenario</th><th>Service</th><th>Method</th><th>Path</th><th>Calls</th><th>Last seen (UTC)</th></tr></thead><tbody>");
        foreach (var row in rows)
        {
            html.Append("<tr>");
            html.Append($"<td>{System.Net.WebUtility.HtmlEncode(row.Key.Scenario)}</td>");
            html.Append($"<td>{System.Net.WebUtility.HtmlEncode(row.Key.ServiceName)}</td>");
            html.Append($"<td>{System.Net.WebUtility.HtmlEncode(row.Key.Method)}</td>");
            html.Append($"<td>{System.Net.WebUtility.HtmlEncode(row.Key.Path)}</td>");
            html.Append($"<td>{row.Calls.ToString(CultureInfo.InvariantCulture)}</td>");
            html.Append($"<td>{(row.LastAt is { } at ? FormatInstant(at) : "")}</td>");
            html.Append("</tr>");
        }
        html.Append("</tbody></table></details>");
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

    /// <summary>
    /// The <c>background</c> object of the data files: the count, the expired calls by the scenario they
    /// were taken from, and the interactions themselves in the shape a scenario's <c>httpInteractions</c> use.
    /// </summary>
    private static object MapBackgroundJson(BackgroundCalls background, IReadOnlyDictionary<Guid, double>? durations, ReportPayloads? payloads = null) => new Dictionary<string, object?>
    {
        ["calls"] = background.Calls,
        ["afterScenarioEnd"] = background.AfterScenarioEnd.Select(g => (object)new Dictionary<string, object?>
        {
            ["scenarioId"] = g.ScenarioId,
            ["scenario"] = g.ScenarioName,
            ["calls"] = g.Calls,
            ["lastAt"] = g.LastAt is { } at ? FormatInstant(at) : null
        }).ToArray(),
        ["interactions"] = background.Interactions.Select(l => MapLogJson(l, durations, payloads: payloads)).ToArray()
    };

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
    public static string GenerateTestRunReportData(Feature[] features, DateTime startTime, DateTime endTime, string fileName, DataFormat format, DefaultDiagramsFetcher.DiagramAsCode[]? diagrams = null, RequestResponseLog[]? trackedLogs = null, IReadOnlyList<DiagnosticEntry>? diagnostics = null, bool fullStepDetail = true, CiMetadata? ciMetadata = null, string? suite = null, RunEnvironment? environment = null) =>
        GenerateTestRunReportData(features, startTime, endTime, fileName, format, diagrams, trackedLogs, diagnostics, fullStepDetail, ciMetadata, suite, environment, attribution: null);

    /// <summary>
    /// The same, with the step attribution handed in when the run derived it already (a run derives it
    /// once and shares it with Failures.md; deriving records the mismatch diagnostic). Null derives it.
    /// </summary>
    internal static string GenerateTestRunReportData(Feature[] features, DateTime startTime, DateTime endTime, string fileName, DataFormat format, DefaultDiagramsFetcher.DiagramAsCode[]? diagrams, RequestResponseLog[]? trackedLogs, IReadOnlyList<DiagnosticEntry>? diagnostics, bool fullStepDetail, CiMetadata? ciMetadata, string? suite, RunEnvironment? environment,
        (Dictionary<string, List<string?>> StepPaths, Dictionary<string, List<ScenarioAnnotation>> Annotations)? attribution,
        bool compressPayloads = false, WarmUpResult? warmUp = null, bool compressedFile = false)
    {
        var diagramLookup = diagrams?.ToLookup(d => d.TestRuntimeId, d => d.CodeBehind);
        // Diagram markers belong to the diagram, not the interaction list: exported as-is they read as
        // content-free calls to http://override.com/ — one pair per Gherkin step and assertion.
        var logLookup = trackedLogs?.Where(l => !l.IsDiagramMarker).ToLookup(l => l.TestId);
        var durations = ComputeInteractionDurations(trackedLogs);
        var (stepPaths, annotations) = attribution ?? AttributeInteractionsToSteps(trackedLogs, features);
        // A run hands in what it found once for every output; a caller with the calls alone gets the same reading.
        warmUp ??= WarmUpCalls.Find(features, trackedLogs);

        return format switch
        {
            // Every payload as text: the file is compressed whole (plans/REPORT_FORMAT_PLAN.md 4.2, Q14).
            DataFormat.Json when compressedFile => WriteCompressedReport(BuildTestRunReportModel(features, startTime, endTime, diagramLookup, logLookup, diagnostics, fullStepDetail, durations, stepPaths, annotations, ciMetadata, suite, environment, compressPayloads: false, warmUp), fileName),
            DataFormat.Json => WriteFile(GenerateTestRunReportJson(features, startTime, endTime, diagramLookup, logLookup, diagnostics, fullStepDetail, durations, stepPaths, annotations, ciMetadata, suite, environment, compressPayloads, warmUp), fileName),
            DataFormat.Xml => WriteFile(GenerateTestRunReportXml(features, startTime, endTime, diagramLookup, logLookup, diagnostics, durations, stepPaths, annotations, ciMetadata, suite, environment, warmUp), fileName),
            DataFormat.Yaml => WriteFile(GenerateTestRunReportYaml(features, startTime, endTime, diagramLookup, logLookup, diagnostics, durations, stepPaths, annotations, ciMetadata, suite, environment, warmUp), fileName),
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
    internal static Dictionary<Guid, double> ComputeInteractionDurations(RequestResponseLog[]? trackedLogs)
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
            if (InteractionDuration(request, response) is { } elapsed)
                durations[pair.Key] = elapsed;
        }

        return durations;
    }

    /// <summary>
    /// A call's duration as the data files write it: what its capturer measured, else the time between its two records
    /// when that is more than none; null when neither says. <see cref="ComputeInteractionDurations"/> applies it to a run,
    /// and Failures.md, the sequence diagram's loop label and the component diagram's statistics to one call (until
    /// 4.16.3 the last two read the timestamps alone).
    /// </summary>
    internal static double? InteractionDuration(RequestResponseLog? request, RequestResponseLog? response)
    {
        if ((request?.DurationMs ?? response?.DurationMs) is { } measured)
            return measured;
        if (request?.Timestamp is not { } start || response?.Timestamp is not { } end)
            return null;

        // Two records stamped with one instant measured nothing (a pair logged after the call, a deferred flush, a
        // response the producer did not stamp): no duration, where until 4.7.3 every reader showed 0 ms.
        var elapsed = (end - start).TotalMilliseconds;
        return elapsed > 0 ? elapsed : null;
    }

    /// <summary>
    /// How every form of <c>TestRunReport.json</c> is serialized: the standard file, the mergeable one and a merge's
    /// output, which all go through the two writers below. The relaxed encoder writes a quote inside a string as
    /// <c>\"</c> and leaves <c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c>, <c>'</c>, <c>+</c> and non-ASCII text as they
    /// are, where the default encoder wrote each as a six-byte escape: captured bodies and PlantUML source are full
    /// of quotes, and on one measured report the escaped quotes alone were a fifth of the file. The JSON value is the
    /// same. The relaxed encoder is unsafe only for text pasted into an HTML page, and nothing pastes this file into
    /// one: every reader parses it (the page's own copy of the internal-flow map has its own encoder,
    /// <see cref="InternalFlowHtmlGenerator"/>).
    /// </summary>
    private static readonly JsonSerializerOptions TestRunReportJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static string GenerateTestRunReportJson(Feature[] features, DateTime startTime, DateTime endTime, ILookup<string, string>? diagramLookup, ILookup<string, RequestResponseLog>? logLookup, IReadOnlyList<DiagnosticEntry>? diagnostics = null, bool fullStepDetail = true, IReadOnlyDictionary<Guid, double>? durations = null, IReadOnlyDictionary<string, List<string?>>? stepPaths = null, IReadOnlyDictionary<string, List<ScenarioAnnotation>>? annotations = null, CiMetadata? ciMetadata = null, string? suite = null, RunEnvironment? environment = null, bool compressPayloads = false, WarmUpResult? warmUp = null) =>
        JsonSerializer.Serialize(BuildTestRunReportModel(features, startTime, endTime, diagramLookup, logLookup, diagnostics, fullStepDetail, durations, stepPaths, annotations, ciMetadata, suite, environment, compressPayloads, warmUp), TestRunReportJsonOptions);

    /// <summary>
    /// The standard data file's model: what <see cref="GenerateTestRunReportJson"/> writes as JSON, and a compressed report
    /// streams into its file (plans/REPORT_FORMAT_PLAN.md 4.5).
    /// </summary>
    private static Dictionary<string, object?> BuildTestRunReportModel(Feature[] features, DateTime startTime, DateTime endTime, ILookup<string, string>? diagramLookup, ILookup<string, RequestResponseLog>? logLookup, IReadOnlyList<DiagnosticEntry>? diagnostics = null, bool fullStepDetail = true, IReadOnlyDictionary<Guid, double>? durations = null, IReadOnlyDictionary<string, List<string?>>? stepPaths = null, IReadOnlyDictionary<string, List<ScenarioAnnotation>>? annotations = null, CiMetadata? ciMetadata = null, string? suite = null, RunEnvironment? environment = null, bool compressPayloads = false, WarmUpResult? warmUp = null)
    {
        var payloads = new ReportPayloads(compressPayloads);
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
            ["startTime"] = startTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["endTime"] = endTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            // Before `features`, which is nearly the whole file: a streaming reader and a person
            // running `head` both see which run this is without the megabytes after it.
            ["ciMetadata"] = MapCiMetadataJson(ciMetadata)
        };

        if (MapEnvironmentJson(environment) is { } environmentJson)
            data["environment"] = environmentJson;

        data["features"] = BuildFeaturesJsonModel(features, diagramLookup, logLookup, fullStepDetail, durations, stepPaths, annotations, resolvedSuite, payloads, warmUp);
        data["background"] = MapBackgroundJson(BackgroundAttribution.Summarise(logLookup?[TestIdentityScope.UnknownTestId], features), durations, payloads);
        data["diagnostics"] = MapDiagnosticsJson(diagnostics);
        // Known only once every payload is written; the key keeps its place at the top of the file.
        data["formatVersion"] = payloads.FormatVersion;

        return data;
    }

    /// <summary>
    /// Builds the serializable feature/scenario model shared by the standard test-run report JSON
    /// and the enriched "mergeable" JSON. Keeping a single source of truth ensures the mergeable
    /// format remains a strict superset that the merge reader can parse.
    /// </summary>
    private static object[] BuildFeaturesJsonModel(Feature[] features, ILookup<string, string>? diagramLookup, ILookup<string, RequestResponseLog>? logLookup, bool fullStepDetail = false, IReadOnlyDictionary<Guid, double>? durations = null, IReadOnlyDictionary<string, List<string?>>? stepPaths = null, IReadOnlyDictionary<string, List<ScenarioAnnotation>>? annotations = null, string? suite = null, ReportPayloads? payloads = null, WarmUpResult? warmUp = null)
    {
        Func<ScenarioStep, object> stepMapper = fullStepDetail ? MapStepJsonFull : MapStepJson;
        return features.OrderBy(f => f.DisplayName).Select(f => (object)WithStableIdName(f, new Dictionary<string, object?>
        {
            ["name"] = f.DisplayName,
            ["endpoint"] = f.Endpoint,
            ["description"] = f.Description,
            ["sourceFile"] = f.SourceFile,
            ["tags"] = f.Tags ?? [],
            ["scenarios"] = f.Scenarios.Select(s =>
            {
                var scenario = new Dictionary<string, object?>
                {
                    ["id"] = s.Id,
                    ["stableId"] = ScenarioStableId.For(suite, f, s),
                    ["name"] = s.DisplayName,
                    ["description"] = s.Description,
                    ["result"] = s.Result.ToString(),
                    ["durationSeconds"] = s.Duration?.TotalSeconds ?? 0.0,
                    ["endedAt"] = s.EndedAt is { } endedAt ? FormatInstant(endedAt) : null,
                    ["isHappyPath"] = s.IsHappyPath,
                    ["errorMessage"] = s.ErrorMessage,
                    ["errorStackTrace"] = s.ErrorStackTrace,
                    ["failureCause"] = s.FailureCause,
                    ["tags"] = s.Tags ?? [],
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

                // Beside durationSeconds, and only on a scenario that carries one (plans/WARM_UP_PLAN.md 4.4), so a run
                // with no warm-up writes the bytes it wrote before.
                if (warmUp is not null && warmUp.ScenarioMs.TryGetValue(s.Id, out var warmUpMs))
                    scenario = InsertAfter(scenario, "durationSeconds", "warmUpSeconds", Math.Round(warmUpMs / 1000, 7));

                // Beside isHappyPath, and only on a scenario that is not a test (plans/RUN_WINDOW_PLAN.md R1), so every other
                // scenario's bytes stay as they were; a merge and the query read it back, where they used to lose it.
                if (s.NotATest)
                    scenario = InsertAfter(scenario, "isHappyPath", "notATest", true);

                // Beside name, and only on a scenario whose name is shown as written (5.9.1, plans/REPORT_UI_FIXES_PLAN.md N4,
                // N5), so every other scenario's bytes stay as they were; a merge reads it back and groups as this run did.
                if (s.NameAsWritten)
                    scenario = InsertAfter(scenario, "name", "nameAsWritten", true);

                // Beside outlineId, and only on an outline row whose name Kronikol filled (#101), so every other scenario's
                // bytes stay as they were; a merge reads it back and keys the row's id on its outline's title, as this run did.
                if (s.KeyedByOutline)
                    scenario = InsertAfter(scenario, "outlineId", "keyedByOutline", true);

                if (diagramLookup != null)
                    scenario["diagrams"] = diagramLookup[s.Id].Select(d => payloads is null ? d : payloads.Write(d)).ToArray();

                if (logLookup != null)
                {
                    var paths = stepPaths is not null && stepPaths.TryGetValue(s.Id, out var p) ? p : null;
                    scenario["httpInteractions"] = logLookup[s.Id]
                        .Select((l, i) => MapLogJson(l, durations, paths is not null && i < paths.Count ? paths[i] : null, payloads, warmUp))
                        .ToArray();
                    scenario["annotations"] = (annotations is not null && annotations.TryGetValue(s.Id, out var a) ? a : [])
                        .Select(x => (object)new { x.Index, Kind = x.Kind.ToString(), x.Text })
                        .ToArray();
                }

                return scenario;
            }).ToArray()
        })).ToArray();
    }

    // Beside name, and only on a feature whose ids key on another name (plans/REPORT_UI_FIXES_PLAN.md N3: the name 5.9.0
    // showed and hashed), so every other feature's bytes stay as they were; a merge reads it back and computes the same ids.
    private static Dictionary<string, object?> WithStableIdName(Feature f, Dictionary<string, object?> feature) =>
        f.StableIdName is { } key && key != f.DisplayName ? InsertAfter(feature, "name", "stableIdName", key) : feature;

    /// <summary>
    /// Assembles the enriched "mergeable" report from in-process state captured during a test run:
    /// extracts component relationships from the tracked logs, precomputes the self-contained
    /// internal-flow segment data and per-scenario whole-test-flow fragments (inlining their payloads
    /// so no shared diagram-data map is required), then serializes everything to JSON.
    /// </summary>
    private static Dictionary<string, object?> BuildRunMergeableModel(
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
        RunEnvironment? environment = null,
        (Dictionary<string, List<string?>> StepPaths, Dictionary<string, List<ScenarioAnnotation>> Annotations)? attribution = null,
        int? suppliedSpans = null,
        WarmUpResult? warmUp = null,
        bool compressPayloads = false)
    {
        var diagramLookup = diagrams?.ToLookup(d => d.TestRuntimeId, d => d.CodeBehind);

        var componentOptions = options.ComponentDiagramOptions ?? new ComponentDiagramOptions();
        var componentLogs = (trackedLogs ?? RequestResponseLogger.RequestAndResponseLogs.Where(x => !(x?.TrackingIgnore ?? true)).ToArray());
        var relationships = ComponentDiagramGenerator.ExtractRelationships(componentLogs, componentOptions.ParticipantFilter);

        Dictionary<string, object>? internalFlowSegmentData = null;
        if (perBoundarySegments is not null)
        {
            // The page's map, filtered and stored the same way, so a merge renders what the run's page did: the
            // segments the diagrams this file carries link, which are its scenarios' own.
            var linkSources = features.SelectMany(f => f.Scenarios)
                .SelectMany(s => diagramLookup?[s.Id] ?? [])
                .Select(source => (string?)source);
            internalFlowSegmentData = InternalFlowHtmlGenerator.StoreFlowsOnce(InternalFlowHtmlGenerator.BuildSegmentData(
                InternalFlowHtmlGenerator.LinkedSegments(perBoundarySegments, linkSources),
                options.InternalFlowDiagramStyle,
                options.InternalFlowShowFlameChart,
                options.InternalFlowFlameChartPosition,
                options.InternalFlowNoDataBehavior,
                options.InternalFlowSpanGranularity,
                options.InternalFlowActivitySources,
                ReportToggleDefaultsResolver.Resolve(options, specifications: false).InternalFlowTab,
                suppliedSpans));
        }

        Dictionary<string, Merge.WholeTestFlowFragment>? wholeTestFlow = null;
        if (wholeTestSegments is not null && options.WholeTestFlowVisualization != WholeTestFlowVisualization.None)
        {
            wholeTestFlow = new Dictionary<string, Merge.WholeTestFlowFragment>();
            foreach (var scenario in features.SelectMany(f => f.Scenarios))
            {
                var boundaryLogs = InternalFlowSegmentBuilder.BuildWholeTestBoundaries(trackedLogs, scenario.Id);

                // diagramDataMap left null so payloads are inlined into the fragment HTML.
                var content = InternalFlowHtmlGenerator.GetWholeTestFlowContent(
                    wholeTestSegments, scenario.Id, boundaryLogs, options.WholeTestFlowVisualization, diagramDataMap: null);

                if (content is { } c)
                    wholeTestFlow[scenario.Id] = new Merge.WholeTestFlowFragment(c.ActivityHtml, c.FlameHtml, c.SpanCount);
            }
        }

        return BuildMergeableReportModel(
            features, startTime, endTime, diagramLookup,
            relationships, internalFlowSegmentData, wholeTestFlow,
            options.WholeTestFlowVisualization, ciMetadata, diagnostics, trackedLogs,
            stepPathsOverride: attribution?.StepPaths, annotationsOverride: attribution?.Annotations,
            suite: suite, environment: environment, compressPayloads: compressPayloads, warmUp: warmUp);
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
        RunEnvironment? environment = null,
        bool compressPayloads = false,
        WarmUpResult? warmUp = null) =>
        JsonSerializer.Serialize(BuildMergeableReportModel(features, startTime, endTime, diagramLookup, componentRelationships,
            internalFlowSegmentData, wholeTestFlow, wholeTestVisualization, ciMetadata, diagnostics, trackedLogs, kronikolVersion,
            stepPathsOverride, annotationsOverride, suite, environment, compressPayloads, warmUp), TestRunReportJsonOptions);

    /// <summary>
    /// The mergeable file's model: what <see cref="GenerateMergeableReportJson"/> writes as JSON, and a compressed report
    /// streams into its file (plans/REPORT_FORMAT_PLAN.md 4.5).
    /// </summary>
    internal static Dictionary<string, object?> BuildMergeableReportModel(
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
        RunEnvironment? environment = null,
        bool compressPayloads = false,
        WarmUpResult? warmUp = null)
    {
        var payloads = new ReportPayloads(compressPayloads);
        // A merge hands in the shards' marks, None included, and never has them recomputed here.
        warmUp ??= WarmUpCalls.Find(features, trackedLogs);

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
            ["startTime"] = startTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["endTime"] = endTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["features"] = BuildFeaturesJsonModel(features, diagramLookup, logLookup, fullStepDetail: true, durations, stepPaths, annotations, suite, payloads, warmUp),
            ["wholeTestVisualization"] = wholeTestVisualization.ToString(),
            ["componentRelationships"] = (componentRelationships ?? []).Select(r => new
            {
                r.Caller,
                r.Service,
                r.Protocol,
                // Ordinal, as the component diagram lists them: in the machine culture's order, shards of one run
                // written on different machines disagreed.
                Methods = r.Methods.OrderBy(m => m, StringComparer.Ordinal).ToArray(),
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
            ["background"] = MapBackgroundJson(BackgroundAttribution.Summarise(logLookup?[TestIdentityScope.UnknownTestId], features), durations, payloads),
            ["diagnostics"] = MapDiagnosticsJson(diagnostics)
        };
        data["formatVersion"] = payloads.FormatVersion;

        // Left out rather than written as null: a merged report whose shards disagreed has no
        // environment, and a null would read as "unknown" rather than "this file does not record one".
        if (MapEnvironmentJson(environment) is { } environmentJson)
            data["environment"] = environmentJson;

        return data;
    }

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

    /// <summary>
    /// The <c>method</c> field as the data writers emit it: an <see cref="HttpMethod"/> upper-cased,
    /// and a label left exactly as the tracker wrote it.
    /// </summary>
    /// <remarks>
    /// All three writers upper-cased the whole slot. For a real verb that is a no-op -
    /// <c>HttpMethod.Get.ToString()</c> is already <c>GET</c> - and for everything else it rewrote
    /// content the run supplied: Redis's <c>GET (Hit)</c> came back as <c>GET (HIT)</c>, and a cache
    /// label of <c>Cache Get (Hit)</c> as <c>CACHE GET (HIT)</c>. The schema's own description of the
    /// field names <c>Publish</c> as an example value, in mixed case, so the file and its contract had
    /// disagreed since the field existed.
    /// </remarks>
    internal static string? MethodText(OneOf<HttpMethod, string> method) => method.Value switch
    {
        HttpMethod verb => verb.ToString().ToUpperInvariant(),
        string label => label,
        _ => null
    };

    /// <summary>
    /// An interaction in the data files. Everything the diagram renderer reads off the record travels with
    /// it — the categorisation that decides participant shape, the phase, the W3C trace ids that bridge to
    /// OpenTelemetry and application logs, which capture path produced it, and the derived duration —
    /// so a reader of the JSON is never told less than a reader of the diagram.
    /// </summary>
    private static object MapLogJson(RequestResponseLog log, IReadOnlyDictionary<Guid, double>? durations = null, string? stepPath = null, ReportPayloads? payloads = null, WarmUpResult? warmUp = null) => new InteractionJson
    {
        Type = log.Type.ToString(),
        Method = MethodText(log.Method),
        Uri = log.Uri.ToString(),
        ServiceName = log.ServiceName,
        CallerName = log.CallerName,
        Content = payloads is null ? log.Content : payloads.Write(log.Content),
        Headers = log.Headers.Select(h => new { h.Key, h.Value }).ToArray(),
        StatusCode = InteractionStatus.Split(log.StatusCode).Code,
        StatusText = InteractionStatus.Split(log.StatusCode).Text,
        TraceId = log.TraceId.ToString(),
        RequestResponseId = log.RequestResponseId.ToString(),
        Timestamp = log.Timestamp is { } jsonAt ? FormatInstant(jsonAt) : null,
        MetaType = log.MetaType.ToString(),
        DependencyCategory = log.DependencyCategory,
        CallerDependencyCategory = log.CallerDependencyCategory,
        Phase = log.Phase.ToString(),
        AttributionSource = log.AttributionSource?.ToString(),
        ExpiredFrom = log.ExpiredFromTestId,
        Error = log.Error,
        IsUserAction = log.IsUserAction,
        ActivityTraceId = log.ActivityTraceId,
        ActivitySpanId = log.ActivitySpanId,
        CapturedBy = log.CapturedBy,
        DurationMs = durations is not null && durations.TryGetValue(log.RequestResponseId, out var ms) ? ms : (double?)null,
        // On the request record, the half that starts the call; the service is the record's own.
        WarmUp = log.Type == RequestResponseType.Request && warmUp is not null && warmUp.Calls.TryGetValue(log.RequestResponseId, out var mark)
            ? MapWarmUpJson(mark)
            : null,
        StepPath = stepPath
    };

    /// <summary>
    /// A call's record in the JSON data files. A class rather than an anonymous type only so <see cref="WarmUp"/> can be
    /// left out of a call that has none (plans/WARM_UP_PLAN.md 4.4): every other member is written as it always was, in
    /// the same order, nulls included, so <c>TestRunReport.pin.json</c> holds.
    /// </summary>
    private sealed class InteractionJson
    {
        public required string Type { get; init; }
        public string? Method { get; init; }
        public required string Uri { get; init; }
        public string? ServiceName { get; init; }
        public string? CallerName { get; init; }
        public object? Content { get; init; }
        public required object Headers { get; init; }
        public int? StatusCode { get; init; }
        public string? StatusText { get; init; }
        public required string TraceId { get; init; }
        public required string RequestResponseId { get; init; }
        public string? Timestamp { get; init; }
        public required string MetaType { get; init; }
        public string? DependencyCategory { get; init; }
        public string? CallerDependencyCategory { get; init; }
        public required string Phase { get; init; }
        public string? AttributionSource { get; init; }
        public string? ExpiredFrom { get; init; }
        public string? Error { get; init; }
        public bool IsUserAction { get; init; }
        public string? ActivityTraceId { get; init; }
        public string? ActivitySpanId { get; init; }
        public string? CapturedBy { get; init; }
        public double? DurationMs { get; init; }

        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, object?>? WarmUp { get; init; }

        public string? StepPath { get; init; }
    }

    /// <summary>A call's warm-up mark in the JSON data files: <c>first</c> names the run's first call only on a call that waited.</summary>
    private static Dictionary<string, object?> MapWarmUpJson(WarmUpMark mark)
    {
        var json = new Dictionary<string, object?>
        {
            ["kind"] = mark.Kind,
            ["shape"] = mark.Shape,
            ["baselineMs"] = Math.Round(mark.BaselineMs, 4),
            ["baselineCalls"] = mark.BaselineCalls
        };
        if (mark.First is { } first)
            json["first"] = first.ToString();
        return json;
    }

    /// <summary>A copy of <paramref name="source"/> with <paramref name="key"/> written right after <paramref name="after"/>.</summary>
    private static Dictionary<string, object?> InsertAfter(Dictionary<string, object?> source, string after, string key, object? value)
    {
        var copy = new Dictionary<string, object?>(source.Count + 1);
        foreach (var (name, existing) in source)
        {
            copy[name] = existing;
            if (name == after)
                copy[key] = value;
        }
        return copy;
    }

    /// <summary>
    /// An attachment in the data files: the display name, where it is, and the media type the producer
    /// declared (null when it declared none — the renderer then sniffs the extension). <c>problem</c> only on one the
    /// report does not hold (#148), so a report holding every file is written as before.
    /// </summary>
    private static object MapAttachmentJson(FileAttachment attachment)
    {
        var json = new Dictionary<string, object?>
        {
            ["name"] = attachment.Name,
            ["relativePath"] = attachment.RelativePath,
            ["mediaType"] = attachment.MediaType,
        };
        if (attachment.Problem is { } problem)
            json["problem"] = problem.ToString();
        return json;
    }

    /// <inheritdoc cref="MapAttachmentJson"/>
    private static XElement MapAttachmentXml(FileAttachment attachment) =>
        new("Attachment",
            new XElement("Name", attachment.Name),
            new XElement("RelativePath", attachment.RelativePath),
            attachment.MediaType != null ? new XElement("MediaType", attachment.MediaType) : null,
            attachment.Problem is { } problem ? new XElement("Problem", problem.ToString()) : null);

    private static object MapStepJson(ScenarioStep step) => step.IsAssertion
        // "assertion": true on a tracked assertion only (#145), so a report without one is written as before.
        ? new
        {
            step.Keyword,
            Assertion = true,
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
        }
        : new
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
    private static object MapStepJsonFull(ScenarioStep step) => step.IsAssertion
        // "assertion": true on a tracked assertion only (#145), so a report without one is written as before.
        ? new
        {
            step.Keyword,
            Assertion = true,
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
        }
        : new
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

    private static string GenerateTestRunReportXml(Feature[] features, DateTime startTime, DateTime endTime, ILookup<string, string>? diagramLookup, ILookup<string, RequestResponseLog>? logLookup, IReadOnlyList<DiagnosticEntry>? diagnostics = null, IReadOnlyDictionary<Guid, double>? durations = null, IReadOnlyDictionary<string, List<string?>>? stepPaths = null, IReadOnlyDictionary<string, List<ScenarioAnnotation>>? annotations = null, CiMetadata? ciMetadata = null, string? suite = null, RunEnvironment? environment = null, WarmUpResult? warmUp = null)
    {
        var resolvedSuite = suite ?? RunSuite.Current;
        var doc = new XDocument(
            new XElement("TestRunReport",
                new XElement("FormatVersion", ReportFormatVersion),
                new XElement("KronikolVersion", KronikolVersion),
                resolvedSuite is { Length: > 0 } xmlSuite ? new XElement("Suite", xmlSuite) : null,
                new XElement("StartTime", startTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
                new XElement("EndTime", endTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
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
                            (f.Tags is { Length: > 0 }) ? new XElement("Tags", f.Tags.Select(t => new XElement("Tag", t))) : null,
                            new XElement("Scenarios",
                                f.Scenarios.Select(s =>
                                {
                                    var scenarioElements = new List<object?>
                                    {
                                        new XElement("Id", s.Id),
                                        new XElement("StableId", ScenarioStableId.For(resolvedSuite, f, s)),
                                        new XElement("Name", s.DisplayName),
                                        s.Description != null ? new XElement("Description", s.Description) : null,
                                        new XElement("Result", s.Result.ToString()),
                                        new XElement("DurationSeconds", (s.Duration?.TotalSeconds ?? 0.0).ToString("F3", CultureInfo.InvariantCulture)),
                                        warmUp is not null && warmUp.ScenarioMs.TryGetValue(s.Id, out var xmlWarmUpMs)
                                            ? new XElement("WarmUpSeconds", (xmlWarmUpMs / 1000).ToString("F3", CultureInfo.InvariantCulture))
                                            : null,
                                        new XElement("IsHappyPath", s.IsHappyPath.ToString().ToLowerInvariant()),
                                        s.NotATest ? new XElement("NotATest", "true") : null,
                                        s.ErrorMessage != null ? new XElement("ErrorMessage", s.ErrorMessage) : null,
                                        s.ErrorStackTrace != null ? new XElement("ErrorStackTrace", s.ErrorStackTrace) : null,
                                        s.FailureCause != null ? new XElement("FailureCause", s.FailureCause) : null,
                                        (s.Tags is { Length: > 0 }) ? new XElement("Tags", s.Tags.Select(t => new XElement("Tag", t))) : null,
                                        s.Rule != null ? new XElement("Rule", s.Rule) : null,
                                        s.Attempt != null ? new XElement("Attempt", s.Attempt.Value.ToString(CultureInfo.InvariantCulture)) : null,
                                        s.EndedAt is { } xmlEndedAt ? new XElement("EndedAt", FormatInstant(xmlEndedAt)) : null,
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
                                            scenarioElements.Add(new XElement("HttpInteractions", logs.Select((l, i) => MapLogXml(l, durations, StepPathAt(stepPaths, s.Id, i), warmUp))));

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
                MapBackgroundXml(BackgroundAttribution.Summarise(logLookup?[TestIdentityScope.UnknownTestId], features), durations),
                MapDiagnosticsXml(diagnostics)
            )
        );
        return doc.ToString();
    }

    /// <summary>The <c>Background</c> element: the XML spelling of what <see cref="MapBackgroundJson"/> writes.</summary>
    private static XElement MapBackgroundXml(BackgroundCalls background, IReadOnlyDictionary<Guid, double>? durations) =>
        new("Background",
            new XElement("Calls", background.Calls.ToString(CultureInfo.InvariantCulture)),
            background.AfterScenarioEnd.Count == 0 ? null : new XElement("AfterScenarioEnd", background.AfterScenarioEnd.Select(g =>
                new XElement("Scenario",
                    new XElement("ScenarioId", g.ScenarioId),
                    new XElement("Name", g.ScenarioName),
                    new XElement("Calls", g.Calls.ToString(CultureInfo.InvariantCulture)),
                    g.LastAt is { } at ? new XElement("LastAt", FormatInstant(at)) : null))),
            background.Interactions.Count == 0 ? null : new XElement("Interactions", background.Interactions.Select(l => MapLogXml(l, durations))));

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
    private static XElement MapLogXml(RequestResponseLog log, IReadOnlyDictionary<Guid, double>? durations = null, string? stepPath = null, WarmUpResult? warmUp = null) =>
        new("HttpInteraction",
            new XElement("Type", log.Type.ToString()),
            // Omitted when there is none, like every other line here. It used to be written
            // unconditionally, so a bare event - which has no verb - produced <Method />: not "no
            // method" but the empty string, which is a different claim. XML has no way to tell those two
            // apart in element content, so an empty label is written as absent rather than as blank.
            MethodText(log.Method) is { Length: > 0 } xmlMethod ? new XElement("Method", xmlMethod) : null,
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
            log.Error is { } xmlError ? new XElement("Error", xmlError) : null,
            new XElement("TraceId", log.TraceId.ToString()),
            new XElement("RequestResponseId", log.RequestResponseId.ToString()),
            log.Timestamp is { } xmlAt ? new XElement("Timestamp", FormatInstant(xmlAt)) : null,
            // XML omits what carries nothing rather than writing empty elements, as the rest of this writer does.
            log.MetaType != RequestResponseMetaType.Default ? new XElement("MetaType", log.MetaType.ToString()) : null,
            log.DependencyCategory != null ? new XElement("DependencyCategory", log.DependencyCategory) : null,
            log.CallerDependencyCategory != null ? new XElement("CallerDependencyCategory", log.CallerDependencyCategory) : null,
            log.Phase != TestPhase.Unknown ? new XElement("Phase", log.Phase.ToString()) : null,
            log.AttributionSource is { } xmlSource ? new XElement("AttributionSource", xmlSource.ToString()) : null,
            log.ExpiredFromTestId is not null ? new XElement("ExpiredFrom", log.ExpiredFromTestId) : null,
            log.IsUserAction ? new XElement("IsUserAction", "true") : null,
            log.ActivityTraceId != null ? new XElement("ActivityTraceId", log.ActivityTraceId) : null,
            log.ActivitySpanId != null ? new XElement("ActivitySpanId", log.ActivitySpanId) : null,
            log.CapturedBy != null ? new XElement("CapturedBy", log.CapturedBy) : null,
            durations is not null && durations.TryGetValue(log.RequestResponseId, out var ms)
                ? new XElement("DurationMs", ms.ToString("F3", CultureInfo.InvariantCulture))
                : null,
            log.Type == RequestResponseType.Request && warmUp is not null && warmUp.Calls.TryGetValue(log.RequestResponseId, out var xmlMark)
                ? new XElement("WarmUp",
                    new XElement("Kind", xmlMark.Kind),
                    new XElement("Shape", xmlMark.Shape),
                    new XElement("BaselineMs", xmlMark.BaselineMs.ToString("F3", CultureInfo.InvariantCulture)),
                    new XElement("BaselineCalls", xmlMark.BaselineCalls.ToString(CultureInfo.InvariantCulture)),
                    xmlMark.First is { } xmlFirst ? new XElement("First", xmlFirst.ToString()) : null)
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
            step.IsAssertion ? new XElement("Assertion", "true") : null,
            new XElement("Text", step.Text),
            step.Status != null ? new XElement("Status", step.Status.ToString()) : null,
            step.Duration != null ? new XElement("DurationSeconds", step.Duration.Value.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture)) : null,
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
    /// written in flow form as <c>[]</c>. It is written at all because the JSON writer emits these keys
    /// unconditionally: the two files are the same report, and a consumer should not have to ask which
    /// format it is holding before it knows whether a missing <c>Tags</c> means "none" or "not recorded".
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

    private static string GenerateTestRunReportYaml(Feature[] features, DateTime startTime, DateTime endTime, ILookup<string, string>? diagramLookup, ILookup<string, RequestResponseLog>? logLookup, IReadOnlyList<DiagnosticEntry>? diagnostics = null, IReadOnlyDictionary<Guid, double>? durations = null, IReadOnlyDictionary<string, List<string?>>? stepPaths = null, IReadOnlyDictionary<string, List<ScenarioAnnotation>>? annotations = null, CiMetadata? ciMetadata = null, string? suite = null, RunEnvironment? environment = null, WarmUpResult? warmUp = null)
    {
        var resolvedSuite = suite ?? RunSuite.Current;
        var yml = new StringBuilder();
        yml.Append("FormatVersion: " + ReportFormatVersion + "\n");
        AppendYaml(yml, "KronikolVersion: ", KronikolVersion);
        AppendYamlIfPresent(yml, "Suite: ", resolvedSuite);
        // Quoted, by the emitter: an unquoted 2026-01-01T10:00:00Z is a timestamp to a YAML 1.1
        // parser, and the schema says these two are strings.
        AppendYaml(yml, "StartTime: ", startTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        AppendYaml(yml, "EndTime: ", endTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
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

            AppendYamlSequence(yml, "    ", "Tags", feature.Tags);

            yml.Append("    Scenarios:\n");
            foreach (var scenario in feature.Scenarios)
            {
                AppendYaml(yml, "      - Id: ", scenario.Id);
                AppendYaml(yml, "        Name: ", scenario.DisplayName);
                AppendYaml(yml, "        StableId: ", ScenarioStableId.For(resolvedSuite, feature, scenario));
                if (scenario.Description is not null)
                    AppendYaml(yml, "        Description: ", scenario.Description);
                if (scenario.Attempt is not null)
                    yml.Append("        Attempt: " + scenario.Attempt.Value.ToString(CultureInfo.InvariantCulture) + "\n");
                if (scenario.SourceFile is not null)
                    AppendYaml(yml, "        SourceFile: ", scenario.SourceFile);
                if (scenario.SourceLine is not null)
                    yml.Append("        SourceLine: " + scenario.SourceLine.Value.ToString(CultureInfo.InvariantCulture) + "\n");
                AppendYaml(yml, "        Result: ", scenario.Result.ToString());
                yml.Append("        DurationSeconds: " + (scenario.Duration?.TotalSeconds ?? 0.0).ToString("F3", CultureInfo.InvariantCulture) + "\n");
                if (warmUp is not null && warmUp.ScenarioMs.TryGetValue(scenario.Id, out var ymlWarmUpMs))
                    yml.Append("        WarmUpSeconds: " + (ymlWarmUpMs / 1000).ToString("F3", CultureInfo.InvariantCulture) + "\n");
                if (scenario.EndedAt is { } ymlEndedAt)
                    AppendYaml(yml, "        EndedAt: ", FormatInstant(ymlEndedAt));
                yml.Append("        IsHappyPath: " + scenario.IsHappyPath.ToString().ToLowerInvariant() + "\n");
                // only on a scenario that is not a test, as the JSON writes notATest (plans/RUN_WINDOW_PLAN.md R1)
                if (scenario.NotATest)
                    yml.Append("        NotATest: true\n");

                if (scenario.ErrorMessage is not null)
                    AppendYaml(yml, "        ErrorMessage: ", scenario.ErrorMessage);

                if (scenario.ErrorStackTrace is not null)
                    AppendYaml(yml, "        ErrorStackTrace: ", scenario.ErrorStackTrace);

                if (scenario.FailureCause is not null)
                    AppendYaml(yml, "        FailureCause: ", scenario.FailureCause);

                AppendYamlSequence(yml, "        ", "Tags", scenario.Tags);

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
                        if (att.Problem is { } problem)
                            AppendYaml(yml, "            Problem: ", problem.ToString());
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
                            AppendTestRunYamlLog(yml, logs[i], "          ", durations, StepPathAt(stepPaths, scenario.Id, i), warmUp);
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
        // Before the diagnostics, where the JSON has it, and written even when empty for the same reason.
        var backgroundCalls = BackgroundAttribution.Summarise(logLookup?[TestIdentityScope.UnknownTestId], features);
        yml.Append("Background:\n");
        yml.Append("  Calls: " + backgroundCalls.Calls.ToString(CultureInfo.InvariantCulture) + "\n");
        yml.Append("  AfterScenarioEnd:");
        if (backgroundCalls.AfterScenarioEnd.Count == 0)
        {
            yml.Append(" []\n");
        }
        else
        {
            yml.Append('\n');
            foreach (var group in backgroundCalls.AfterScenarioEnd)
            {
                AppendYaml(yml, "    - ScenarioId: ", group.ScenarioId);
                AppendYaml(yml, "      Scenario: ", group.ScenarioName);
                yml.Append("      Calls: " + group.Calls.ToString(CultureInfo.InvariantCulture) + "\n");
                if (group.LastAt is { } lastAt)
                    AppendYaml(yml, "      LastAt: ", FormatInstant(lastAt));
            }
        }
        yml.Append("  Interactions:");
        if (backgroundCalls.Interactions.Count == 0)
        {
            yml.Append(" []\n");
        }
        else
        {
            yml.Append('\n');
            foreach (var log in backgroundCalls.Interactions)
                AppendTestRunYamlLog(yml, log, "    ", durations);
        }

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
        // Written on a tracked assertion only (#145), so a report without one is written as before.
        if (step.IsAssertion)
            yml.Append(indent + "  Assertion: true\n");
        AppendYaml(yml, indent + "  Text: ", step.Text);
        AppendYamlNullable(yml, indent + "  Status: ", step.Status?.ToString());
        if (step.Duration != null)
            yml.Append(indent + "  DurationSeconds: " + step.Duration.Value.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture) + "\n");
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
                if (att.Problem is { } problem)
                    AppendYaml(yml, indent + "      Problem: ", problem.ToString());
            }
        }
    }

    /// <inheritdoc cref="MapLogJson"/>
    private static void AppendTestRunYamlLog(StringBuilder yml, RequestResponseLog log, string indent, IReadOnlyDictionary<Guid, double>? durations = null, string? stepPath = null, WarmUpResult? warmUp = null)
    {
        AppendYaml(yml, indent + "- Type: ", log.Type.ToString());
        AppendYamlNullable(yml, indent + "  Method: ", MethodText(log.Method));
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
        if (log.Error is not null)
            AppendYaml(yml, indent + "  Error: ", log.Error);
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
        if (log.AttributionSource is { } ymlSource)
            AppendYaml(yml, indent + "  AttributionSource: ", ymlSource.ToString());
        if (log.ExpiredFromTestId is not null)
            AppendYaml(yml, indent + "  ExpiredFrom: ", log.ExpiredFromTestId);
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
        if (log.Type == RequestResponseType.Request && warmUp is not null && warmUp.Calls.TryGetValue(log.RequestResponseId, out var ymlMark))
        {
            yml.Append(indent + "  WarmUp:\n");
            AppendYaml(yml, indent + "    Kind: ", ymlMark.Kind);
            AppendYaml(yml, indent + "    Shape: ", ymlMark.Shape);
            yml.Append(indent + "    BaselineMs: " + ymlMark.BaselineMs.ToString("F3", CultureInfo.InvariantCulture) + "\n");
            yml.Append(indent + "    BaselineCalls: " + ymlMark.BaselineCalls.ToString(CultureInfo.InvariantCulture) + "\n");
            if (ymlMark.First is { } ymlFirst)
                AppendYaml(yml, indent + "    First: ", ymlFirst.ToString());
        }
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

    /// <summary>
    /// Whether the specifications are written blank: a scenario failed, or its result is a default rather than a
    /// verdict (<see cref="Scenario.ResultDefaulted"/>), so the run cannot say the system does what they describe.
    /// </summary>
    private static bool CannotVouchForSpecifications(Feature[] features) =>
        features.Any(x => x.Scenarios.Any(y => y.Result == ExecutionResult.Failed || y.ResultDefaulted));

    /// <summary>
    /// The features the specifications describe: a scenario that is not a test (<see cref="Scenario.NotATest"/>, ingest's
    /// fold, a stack's start-up) specifies nothing, so it is left out, and a feature it leaves with no scenario goes with it
    /// (plans/RUN_WINDOW_PLAN.md R1; the Specifications used to list "Traffic outside any test"). Its result is no
    /// verdict either, so it never blanks them.
    /// </summary>
    internal static Feature[] SpecifiedFeatures(Feature[] features) =>
        features.Any(f => f.Scenarios.Any(s => s.NotATest))
            ? features
                .Where(f => f.Scenarios.Length == 0 || f.Scenarios.Any(s => !s.NotATest))
                .Select(f => f.Scenarios.Any(s => s.NotATest) ? f with { Scenarios = f.Scenarios.Where(s => !s.NotATest).ToArray() } : f)
                .ToArray()
            : features;

    public static string GenerateSpecificationsData(Feature[] features, string fileName, string title, DataFormat format, bool generateBlankOnFailedTests = false)
    {
        features = SpecifiedFeatures(features);
        if (generateBlankOnFailedTests && CannotVouchForSpecifications(features))
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

            if (feature.Tags is { Length: > 0 })
            {
                yml.Append("    Tags:\n");
                foreach (var tag in feature.Tags)
                    AppendYaml(yml, "      - ", tag);
            }

            yml.Append("    Scenarios:\n");

            var orderedScenarios = feature.Scenarios.OrderByDescending(x => x.IsHappyPath).ThenBy(ScenarioStableId.KeyName);
            foreach (var scenario in orderedScenarios)
            {
                AppendYaml(yml, "      - Scenario: ", scenario.DisplayName);
                yml.Append("        IsHappyPath: " + scenario.IsHappyPath.ToString().ToLowerInvariant() + "\n");

                if (scenario.Tags is { Length: > 0 })
                {
                    yml.Append("        Tags:\n");
                    foreach (var tag in scenario.Tags)
                        AppendYaml(yml, "          - ", tag);
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
                Tags = f.Tags ?? [],
                Scenarios = f.Scenarios.OrderByDescending(s => s.IsHappyPath).ThenBy(ScenarioStableId.KeyName).Select(s => new
                {
                    Name = s.DisplayName,
                    s.IsHappyPath,
                    Tags = s.Tags ?? [],
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
                            (f.Tags is { Length: > 0 }) ? new XElement("Tags", f.Tags.Select(t => new XElement("Tag", t))) : null,
                            new XElement("Scenarios",
                                f.Scenarios.OrderByDescending(s => s.IsHappyPath).ThenBy(ScenarioStableId.KeyName).Select(s =>
                                    new XElement("Scenario",
                                        new XElement("Name", s.DisplayName),
                                        new XElement("IsHappyPath", s.IsHappyPath.ToString().ToLowerInvariant()),
                                        (s.Tags is { Length: > 0 }) ? new XElement("Tags", s.Tags.Select(t => new XElement("Tag", t))) : null,
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
    /// Copies the files the features' attachments name into the reports directory's <c>attachments/</c> folder and
    /// rewrites each <see cref="FileAttachment.RelativePath"/> to the copy, so the report's links work wherever the folder
    /// is opened or uploaded. A URL is left as it is. A file already in that folder is linked where it is, and so is a
    /// relative <c>attachments/</c> path to a file there (the copy an earlier call over the same model made). Any other
    /// relative path is read against the working directory it was attached in, when <c>Track.Attachment</c> could not
    /// find it then, and against the binary folder.
    /// </summary>
    /// <remarks>
    /// An attachment the report cannot hold keeps its path: one whose file is not there (and one that was there when it was
    /// attached and is gone), one whose file could not be copied, one whose path is not usable on this machine. Every such
    /// attachment, and every one whose file changed after it was attached, is said in ONE
    /// <see cref="DiagnosticKind.AttachmentFailure"/> entry recorded on the current <see cref="ReportDiagnosticsScope"/>,
    /// which leads with the count (#148, plans/MISSING_ATTACHMENTS_PLAN.md).
    /// </remarks>
    public static void CopyAttachmentsToReportsFolder(Feature[] features, string reportsDirectory)
    {
        if (CopyAttachments(features, reportsDirectory).Describe() is { } message)
            ReportDiagnosticsScope.Record(DiagnosticKind.AttachmentFailure, message);
    }

    /// <summary>
    /// <see cref="CopyAttachmentsToReportsFolder"/>'s work, returning what the report could not hold for the caller to
    /// say: a run also prints it, and an ingest adds what it dropped before the model held it.
    /// </summary>
    internal static AttachmentAccount CopyAttachments(Feature[] features, string reportsDirectory)
    {
        var account = new AttachmentAccount(reportsDirectory);
        var attachmentsDir = Path.Combine(reportsDirectory, AttachmentPlacement.FolderName);
        var scenarios = features.Where(f => f.Scenarios is not null).SelectMany(f => f.Scenarios).ToArray();

        // Every path is read before anything is copied, so a file already in the folder keeps its name: copied in first, a
        // file of the same name from elsewhere would have been written over it.
        var sources = new Dictionary<FileAttachment, AttachmentPlacement.Source>(ReferenceEqualityComparer.Instance);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AttachmentPlacement.ForEachList(scenarios, attachments =>
        {
            foreach (var attachment in attachments)
            {
                if (sources.ContainsKey(attachment))
                    continue;
                string[] bases = attachment.AttachedFrom is { } attachedFrom
                    ? [attachedFrom, AppDomain.CurrentDomain.BaseDirectory]
                    : [AppDomain.CurrentDomain.BaseDirectory];
                var source = AttachmentPlacement.Classify(attachment, reportsDirectory, bases);
                sources[attachment] = source;
                if (source.Kind == AttachmentPlacement.SourceKind.InReport)
                    usedNames.Add(Path.GetFileName(source.Path));
            }

            return attachments;
        });

        var copiedFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var failedFiles = new Dictionary<string, Exception>(StringComparer.OrdinalIgnoreCase);
        AttachmentPlacement.ForEachList(scenarios, attachments => attachments.Select(Place).ToArray());
        return account;

        FileAttachment Place(FileAttachment attachment)
        {
            var source = sources[attachment];
            switch (source.Kind)
            {
                case AttachmentPlacement.SourceKind.Link:
                    return attachment;
                case AttachmentPlacement.SourceKind.Unusable:
                    account.NotUsable(source.Shown);
                    return attachment with { Problem = AttachmentProblem.NotUsable };
                case AttachmentPlacement.SourceKind.Missing:
                    account.NotFound(source.Shown, source.Path, wasThere: attachment.WhenAttached is not null);
                    return attachment with { Problem = AttachmentProblem.NotFound };
                case AttachmentPlacement.SourceKind.InReport:
                    // This run's own, though nothing is copied: left out of the run's manifest, the file would stay behind
                    // when the run is moved to runs/<run>/ and the retained report's link would dangle.
                    RunFileCollector.Record(source.Path);
                    NoteAChange(attachment, source);
                    return attachment with { RelativePath = source.Link, Problem = null };
            }

            if (failedFiles.TryGetValue(source.Path, out var failed))
            {
                account.NotCopied(source.Shown, failed);
                return attachment with { Problem = AttachmentProblem.NotCopied };
            }

            if (!copiedFiles.TryGetValue(source.Path, out var relativePath))
            {
                var destName = GetUniqueFileName(Path.GetFileName(source.Path), usedNames);
                usedNames.Add(destName);
                var destPath = Path.Combine(attachmentsDir, destName);
                try
                {
                    // Inside the guard: a file named attachments in the reports folder is one attachment that could not
                    // be copied, not a report that was never written.
                    Directory.CreateDirectory(attachmentsDir);
                    File.Copy(source.Path, destPath, overwrite: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A capturer may still hold the file, or the disk may be full: the report links where it is rather than
                    // losing the whole report over one screenshot.
                    failedFiles[source.Path] = ex;
                    account.NotCopied(source.Shown, ex);
                    return attachment with { Problem = AttachmentProblem.NotCopied };
                }

                // The destination, not the source: the copy is what the report links to and what the run owns.
                RunFileCollector.Record(destPath);
                copiedFiles[source.Path] = relativePath = $"{AttachmentPlacement.FolderName}/{destName}";
            }

            NoteAChange(attachment, source);
            return attachment with { RelativePath = relativePath, Problem = null };
        }

        void NoteAChange(FileAttachment attachment, AttachmentPlacement.Source source)
        {
            if (AttachmentPlacement.ChangedSinceAttached(attachment, source.Path))
                account.Changed(source.Shown);
        }
    }

    /// <summary>
    /// An attachment the report holds, or one that lives elsewhere, as a link to it titled with what it opens and whose it
    /// is (5.0.0, plans/OPTION_C_PLAN.md D49): <paramref name="owner"/> is "scenario" or "step".
    /// </summary>
    private static string AttachmentLink(FileAttachment attachment, string owner)
    {
        var name = System.Net.WebUtility.HtmlEncode(attachment.Name);
        return $"<a class=\"step-attachment\" href=\"{AttachmentHref(attachment)}\" title=\"Open {name}, {(attachment.IsUrl ? "a link" : "a file")} this {owner} attached\">{name}</a>";
    }

    /// <summary>
    /// An image the report holds, as a link that opens it full size (report-lightbox-function.js), titled as
    /// <see cref="AttachmentLink"/> is (D49).
    /// </summary>
    private static string AttachmentImageLink(FileAttachment attachment, string owner)
    {
        var name = System.Net.WebUtility.HtmlEncode(attachment.Name);
        return $"<a class=\"attachment-image-link\" href=\"{AttachmentHref(attachment)}\" onclick=\"openLightbox(event, this)\" title=\"Show {name}, an image this {owner} attached, at full size\"><img class=\"attachment-image\" src=\"{AttachmentHref(attachment)}\" alt=\"{name}\" /></a>";
    }

    /// <summary>The title of a scenario's Failure Result (D49).</summary>
    private const string FailureResultTitle = "Why the scenario failed: the error, with the expected and actual values where it gives them";

    /// <summary>
    /// An attachment the report does not hold (#148), drawn as its name and why, with no image or link that would be dead.
    /// The path it was given is in the title only, so a published page shows no machine path in its text.
    /// </summary>
    internal static string MissingAttachmentHtml(FileAttachment attachment, AttachmentProblem problem) =>
        $"<span class=\"attachment-missing\" title=\"{System.Net.WebUtility.HtmlEncode(attachment.RelativePath)}\">{System.Net.WebUtility.HtmlEncode(attachment.Name)} ({AttachmentProblemText.Of(problem)})</span>";

    /// <summary>
    /// An attachment's path as an <c>href</c> or <c>src</c>, HTML-encoded. A file's path is percent-encoded first for the
    /// three characters a URL reads otherwise, <c>%</c> before <c>#</c> and <c>?</c> so nothing is encoded twice: the copy
    /// keeps the file's name, and <c>attachments/shot#1.png</c> requested <c>attachments/shot</c> (#148 F11). Only those
    /// three, so a space or a letter outside ASCII is linked as it always was. A URL is linked as it was given: its
    /// query and fragment are its own.
    /// </summary>
    internal static string AttachmentHref(FileAttachment attachment)
    {
        var path = attachment.IsUrl
            ? attachment.RelativePath
            : attachment.RelativePath.Replace("%", "%25").Replace("#", "%23").Replace("?", "%3F");
        return System.Net.WebUtility.HtmlEncode(path);
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

        // Never the run's: these two describe the DIRECTORY — "you are in a Kronikol reports directory… if
        // Failures.md is absent, the run did not finish" — and a nested CLAUDE.md loads when an agent reads
        // any file beside it. Listed in Run.json they would move into runs/<run>/ with the run, and five
        // retained runs would be five files each claiming to be the newest.
        if (existing is null)
        {
            WriteFile(block + "\n", fileName, partOfTheRun: false);
            return;
        }

        if (AgentInstructionsBlock.Merge(existing, block, out var problem) is { } merged)
        {
            WriteFile(merged, fileName, partOfTheRun: false);
            return;
        }

        ReportDiagnosticsScope.Record(DiagnosticKind.OutputFailure, $"Left {fileName} alone: it {problem}");
        Console.WriteLine($"⚠ WARNING: left {fileName} alone — it {problem}");
    }

    private static string WriteFile(string text, string fileName, bool partOfTheRun = true)
    {
        var directory = CurrentReportsDirectory;
        Directory.CreateDirectory(directory);
        var filePath = Path.Combine(directory, fileName);
        try
        {
            File.WriteAllText(filePath, text);
            if (partOfTheRun)
                RunFileCollector.Record(filePath);
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
                // Under the name that was written: the manifest lists what reached disk, and the
                // canonical name on disk is still an earlier run's.
                if (partOfTheRun)
                    RunFileCollector.Record(fallback);
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

    /// <summary>
    /// Writes <paramref name="model"/> as a compressed report (plans/REPORT_FORMAT_PLAN.md 4.1 and 4.2): <c>formatVersion</c> 3,
    /// streamed through the filter that leaves out every key that holds nothing into one zstd frame behind Kronikol's marker.
    /// The JSON is never held whole, so the run's peak is the model, not three times the file on top of it (F14).
    /// </summary>
    private static string WriteCompressedReport(Dictionary<string, object?> model, string fileName) =>
        WriteFile(stream => WriteCompressedReport(model, stream), fileName);

    /// <summary>
    /// Writes <paramref name="model"/> as a compressed report into <paramref name="destination"/>, which must be seekable:
    /// a run's file, and a merge's (<see cref="Merge.MergeableReportRenderer.WriteCompressed"/>). Returns the length of the
    /// JSON the file holds.
    /// </summary>
    internal static long WriteCompressedReport(Dictionary<string, object?> model, Stream destination)
    {
        // Version 3: keys that hold nothing may be absent, and no payload is wrapped. The key keeps its place at the top.
        model["formatVersion"] = ReportPayloads.CompactFormatVersion;
        return ReportFileWriter.Write(destination, json => JsonSerializer.Serialize(json, model, CompactReportJsonOptions), ReportRequiredKeys.Value);
    }

    /// <summary>A data file's model as the JSON file holds it: indented, with the relaxed encoder.</summary>
    internal static string SerializeReportJson(Dictionary<string, object?> model) => JsonSerializer.Serialize(model, TestRunReportJsonOptions);

    /// <summary>The data file's options without the indentation, which the filter would only strip again.</summary>
    private static readonly JsonSerializerOptions CompactReportJsonOptions = new(TestRunReportJsonOptions) { WriteIndented = false };

    /// <summary>What the report's schema requires where, read once from the schema this generator writes.</summary>
    private static readonly Lazy<RequiredKeys> ReportRequiredKeys = new(() =>
    {
        using var schema = JsonDocument.Parse(GenerateTestRunReportJsonSchema());
        return RequiredKeys.FromSchema(schema.RootElement);
    });

    /// <summary>
    /// <see cref="WriteFile(string, string, bool)"/> for a file written as a stream: into a temporary file beside it, then
    /// renamed over it, so a reader never sees half a file. When the name cannot be replaced, this run's file goes to the
    /// same fallback name, and only the name that reached disk is recorded, never the temporary one (F25).
    /// </summary>
    private static string WriteFile(Action<Stream> write, string fileName, bool partOfTheRun = true)
    {
        var directory = CurrentReportsDirectory;
        Directory.CreateDirectory(directory);
        var filePath = Path.Combine(directory, fileName);
        // Named so that nothing takes it for a report or publishes it: it ends neither .krkl nor .json.
        var temporary = Path.Combine(directory, $"{fileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            using var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            write(stream);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }

        // A file this process may not write (read-only, or not its own) stays as it is, as the string writer leaves it:
        // a rename needs only the directory, so it would replace the file on Linux and macOS, and on Windows its error
        // names no path. Opening for write without truncating asks the question and changes nothing.
        try
        {
            if (File.Exists(filePath))
                using (new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { }
        }
        catch (UnauthorizedAccessException)
        {
            TryDelete(temporary);
            throw;
        }
        catch (IOException)
        {
            // Held open by a reader: the rename below replaces it where it can, and salvages where it cannot.
        }

        try
        {
            File.Move(temporary, filePath, overwrite: true);
            if (partOfTheRun)
                RunFileCollector.Record(filePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Held open where a rename cannot replace it (Windows): the same salvage, and the same rethrow, as the string
            // writer above.
            var fallback = Path.Combine(directory,
                Path.GetFileNameWithoutExtension(fileName) + "2" + Path.GetExtension(fileName));
            try
            {
                File.Move(temporary, fallback, overwrite: true);
                if (partOfTheRun)
                    RunFileCollector.Record(fallback);
                Console.WriteLine($"⚠ WARNING: {fileName} was not writable — this run's copy is in {Path.GetFileName(fallback)}; "
                                  + $"{fileName} on disk is from an earlier run.");
            }
            catch (Exception salvageFailure) when (salvageFailure is IOException or UnauthorizedAccessException)
            {
                TryDelete(temporary);
            }

            // The full path, which a failed rename's own message leaves out where the string writer's names it.
            throw new IOException($"Could not write {filePath}: {exception.Message}", exception);
        }
        return filePath;

        static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Left behind; it ends .tmp, which nothing reads, publishes or lists.
            }
        }
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
            // the participant a diagram with no call is drawn with holds its step markers: no dependency (5.0.0)
            if (match.Success && match.Groups[1].Value != Kronikol.PlantUml.PlantUmlCreator.MarkerOnlyParticipantName)
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

    // A call's label inside its internal-flow link, [[#iflow-ID GET: /path]] (PlantUmlCreator, internal-flow tracking on,
    // the default), and the break and indent PlantUmlCreator joins a long path's chunks with for display. The instant
    // search reads the label without the link and the path whole (SEARCH_PLAN B11): until then the link put the method
    // out of UrlRegex's reach, so a linked call's path was never read, and a chunked one was read to its first chunk.
    private static readonly System.Text.RegularExpressions.Regex LinkedLabelRegex = new(
        @"\[\[#\S+\s+(.*?)\]\]",
        System.Text.RegularExpressions.RegexOptions.Compiled);
    private const string PathDisplayBreak = "\\n        ";

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

            var label = LinkedLabelRegex.Replace(trimmed, "$1").Replace(PathDisplayBreak, "", StringComparison.Ordinal);
            var urlMatch = UrlRegex.Match(label);
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

    /// <summary>
    /// The agent instructions with the run's own data file in the rule not to open it: they name the JSON one, and a run
    /// that wrote XML or YAML has none. Until 4.27.1 such a run was told not to open a file it did not have
    /// (<c>plans/REPORT_FORMAT_PLAN.md</c> section 8). Here rather than in <see cref="AgentInstructionsGenerator"/>, whose
    /// one entry takes the report's file name and nothing else, so that no text from a run has a way into the file; the
    /// name here comes from the options alone.
    /// </summary>
    internal static string NamingTheDataFile(string instructions, ReportConfigurationOptions options)
    {
        var extension = DataFileExtension(options);
        if (extension == "json")
            return instructions;

        var name = options.HtmlTestRunReportFileName;
        var named = instructions.Replace($"`{name}.json`", $"`{name}.{extension}`", StringComparison.Ordinal);
        // A compressed file says nothing to a reader who opens it, so the rule gives the reason that holds for it (F26).
        var rule = $"**Never open `{name}.krkl`, `{name}.html`, or a diagram.**";
        return extension == "krkl"
            ? named.Replace(rule, rule + " The data file is compressed: `kronikol query` reads it, and `kronikol decode` writes its JSON for a script that needs the JSON itself.", StringComparison.Ordinal)
            : named;
    }

    /// <summary>Whether the run writes its data file compressed: <see cref="ReportConfigurationOptions.CompressTestRunReport"/>, read only for JSON.</summary>
    internal static bool WritesCompressedReport(ReportConfigurationOptions options) =>
        options.CompressTestRunReport && options.TestRunReportDataFormat == DataFormat.Json;

    /// <summary>The data file's extension, without the dot: <c>krkl</c> for a compressed report, else its format's.</summary>
    internal static string DataFileExtension(ReportConfigurationOptions options) =>
        WritesCompressedReport(options) ? "krkl" : GetDataFormatExtension(options.TestRunReportDataFormat);

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
                    ["relativePath"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Where the file is: attachments/<file> for a file the report holds (join it to the report's own directory to open it), the path as it was given for one it does not hold (problem says why), or an absolute URL when the attachment is a link" },
                    ["mediaType"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "IANA media type; image/* renders inline, anything else as a link" },
                    ["problem"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = Enum.GetNames(typeof(AttachmentProblem)), ["description"] = "Why the report does not hold the file: NotFound (no file at the path when the report was drawn), NotCopied (it could not be copied in) or NotUsable (not a legal path where the report was drawn). Absent when the report holds the file, and for a link" }
                }
            }
        };

        var schema = new Dictionary<string, object?>
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$comment"] = "A real TestRunReport.json runs to megabytes, with single embedded diagrams past 600 KB, so do not read it whole: `kronikol query <command> <report>` (dotnet tool install -g Kronikol.Tool) answers questions about it under a byte budget — summary, failures, steps sN, services, flow sN, http sN/iN — and with nothing installed, `dotnet run --file query.cs -- <command> <report>` from the reports directory answers the same, with the engine that wrote it. This schema is the field-level contract of that file.",
            ["title"] = "TestRunReport",
            ["description"] = "Schema for Kronikol test run report data",
            ["type"] = "object",
            // formatVersion is required because a reader that cannot find it is reading a file written
            // before the contract was versioned, and should say so rather than guess.
            ["required"] = new[] { "formatVersion", "startTime", "endTime", "features", "background" },
            ["properties"] = new Dictionary<string, object?>
            {
                ["formatVersion"] = new Dictionary<string, object?> { ["type"] = "integer", ["description"] = "Version of the report SHAPE, as distinct from the Kronikol build that wrote it. Bumped when a key changes meaning or type, not when one is added. 2 means the same shape with at least one payload (a content or a diagrams entry) written as a compressedPayload object; a file with none says 1. 3 means the JSON a compressed report holds (TestRunReport.krkl, CompressTestRunReport): the same keys, any optional one of which may be absent where it would hold null or nothing, and no payload written compressed." },
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
                ["background"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["description"] = "Interactions that belong to no scenario: captured under a scenario's identity after that scenario had ended (the test host's hosted services, timers and consumers still running with the identity the test's execution context handed them), or captured with no identity at all when CaptureBackground is on. Always present; calls is 0 when there were none",
                    ["required"] = new[] { "calls", "afterScenarioEnd", "interactions" },
                    ["additionalProperties"] = false,
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["calls"] = new Dictionary<string, object?> { ["type"] = "integer", ["description"] = "Distinct background calls; a request and its response count once" },
                        ["afterScenarioEnd"] = new Dictionary<string, object?>
                        {
                            ["type"] = "array",
                            ["description"] = "The calls that were re-attributed, by the scenario they were taken from",
                            ["items"] = new Dictionary<string, object?>
                            {
                                ["type"] = "object",
                                ["required"] = new[] { "scenarioId", "scenario", "calls" },
                                ["additionalProperties"] = false,
                                ["properties"] = new Dictionary<string, object?>
                                {
                                    ["scenarioId"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "The scenario the calls were captured under (features[].scenarios[].id)" },
                                    ["scenario"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Its display name" },
                                    ["calls"] = new Dictionary<string, object?> { ["type"] = "integer", ["description"] = "Distinct calls taken from it" },
                                    ["lastAt"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["format"] = "date-time", ["description"] = "When the latest of them was captured (UTC)" }
                                }
                            }
                        },
                        ["interactions"] = new Dictionary<string, object?>
                        {
                            ["type"] = "array",
                            ["description"] = "The background interactions themselves, in the same shape as a scenario's httpInteractions: attributionSource says why each is here and expiredFrom names the scenario it was taken from",
                            ["items"] = new Dictionary<string, object?> { ["$ref"] = "#/$defs/httpInteraction" }
                        }
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
                ["internalFlowSegments"] = new Dictionary<string, object?> { ["type"] = "object", ["description"] = "Mergeable only: precomputed internal-flow payloads keyed by segment id. From 3.35.2 a flow several segments show is held by the first of them, and each later one names it under sameAs; from 4.0.1 the segment holding a flow names its diagram and flame chart by their places (contentAt, flameAt) in a table that ends the map under the first segment's key with a ~ before it, which holds each distinct diagram and flame chart once. The values are rendering data carried through a merge unchanged, and are deliberately not described here - a merged file may hold shapes written by another version." },
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
                        ["required"] = new[] { "name", "scenarios" },
                        ["properties"] = new Dictionary<string, object?>
                        {
                            ["name"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Feature display name: the test class, or the title after Feature: in Gherkin" },
                            ["stableIdName"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Present only where the feature's scenarios' stableIds are computed from another name than name: the name 5.9.0 showed and hashed, for a feature 5.9.1 shows as written or with its apostrophe kept, so its scenarios keep their ids. A merge computes ids from it" },
                            ["endpoint"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "The endpoint or component the feature covers, when declared (an @endpoint: tag, or the adapter's attribute)" },
                            ["description"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "Free text under the Feature: line, dedented" },
                            ["sourceFile"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "Where the feature is written: a project-relative path with forward slashes, from the Gherkin document, or from the first sourceFile a tests NDJSON start record gives one of its scenarios. Null on the lanes that cannot supply one (unit-test adapters, a tests NDJSON without sourceFile). Features are grouped by display name, so two files sharing a Feature: title collapse into one entry and the first path seen wins.", ["examples"] = new[] { "Features/Cake.feature" } },
                            ["tags"] = new Dictionary<string, object?> { ["type"] = "array", ["description"] = "The feature's own tags: in Gherkin the feature's, otherwise its class's or its fixture's, and on the tests NDJSON lane those every one of its scenarios carries. They come first among each of its scenarios' effective tags. A file written before 5.0.0 holds labels here instead", ["items"] = new Dictionary<string, object?> { ["type"] = "string" } },
                            ["scenarios"] = new Dictionary<string, object?>
                            {
                                ["type"] = "array",
                                ["description"] = "The feature's scenarios in run order",
                                ["items"] = new Dictionary<string, object?>
                                {
                                    ["type"] = "object",
                                    ["required"] = new[] { "id", "stableId", "name", "result", "durationSeconds", "isHappyPath", "steps" },
                                    ["properties"] = new Dictionary<string, object?>
                                    {
                                        ["id"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "The runtime id the test framework gave this scenario (a test case id, a pickle id). Unique within the run but not stable across runs; use stableId for that." },
                                        ["stableId"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Deterministic cross-run identifier derived from feature name + scenario display name (+ outline ID and ordered example values for parameterized scenarios). Use this for matching the same test across runs. Not unique: repeated rows and retries share one.", ["examples"] = new[] { "a1b2c3d4e5f60718" } },
                                        ["name"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Scenario display name; an outline row shows its expanded name" },
                                        ["description"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "The scenario's own free-text description (the prose under Scenario:)" },
                                        ["result"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = resultEnumValues, ["description"] = "The scenario's verdict" },
                                        ["durationSeconds"] = new Dictionary<string, object?> { ["type"] = "number", ["description"] = "Wall-clock seconds the scenario took; 0 when unknown" },
                                        ["warmUpSeconds"] = new Dictionary<string, object?> { ["type"] = "number", ["minimum"] = 0, ["description"] = "Seconds of durationSeconds that the run's first-call warm-up took: the time the scenario's calls marked warmUp cover, each instant counted once, never more than durationSeconds. durationSeconds less this is the scenario's own time. Present only on a scenario that carries such a call." },
                                        ["endedAt"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["format"] = "date-time", ["description"] = "When the scenario finished (UTC), as the framework recorded it; null when the adapter did not record one. A call that inherited this scenario's context after this moment is background (see httpInteractions[].attributionSource = Expired)" },
                                        ["isHappyPath"] = new Dictionary<string, object?> { ["type"] = "boolean", ["description"] = "Marked as the happy path (an @happy-path tag or the adapter's attribute); the report lists happy paths first" },
                                        ["nameAsWritten"] = new Dictionary<string, object?> { ["type"] = "boolean", ["description"] = "Present, and true, only on a scenario whose name a person wrote and the report shows as written (a Gherkin scenario): its outline's heading is its title as written, and a name ending in brackets is not read as a parameterised test's" },
                                        ["notATest"] = new Dictionary<string, object?> { ["type"] = "boolean", ["description"] = "Present, and true, only on a scenario that is not a test, such as an ingest's \"Traffic outside any test\": it is drawn like any scenario, the run's counts and times, the timeline, the CI summary, Failures.md, the CTRF report and the specifications leave it out, and the history records it as N. Its result says only that it was drawn" },
                                        ["errorMessage"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "The failure message the framework reported, when the scenario failed" },
                                        ["errorStackTrace"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "The stack trace the framework reported, when the scenario failed" },
                                        ["failureCause"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "The framework's CLASSIFICATION of the failure (xUnit v3: Assertion, Exception, Timeout, Other; MSTest: the exception type name), null where the framework does not classify. A category, not a cause - it says an assertion failed, never which one - so it is never a grouping key" },
                                        ["tags"] = new Dictionary<string, object?> { ["type"] = "array", ["description"] = "The scenario's own tags (in Gherkin its own, its rule's and its Examples block's); its effective tags are its feature's, then these. A file written before 5.0.0 holds labels and categories here instead", ["items"] = new Dictionary<string, object?> { ["type"] = "string" } },
                                        ["rule"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "Gherkin Rule grouping this scenario belongs to" },
                                        ["attempt"] = new Dictionary<string, object?> { ["type"] = new[] { "integer", "null" }, ["description"] = "Which run of this scenario produced the result, when the runner retries: 1 for the first, 2 for the first retry. 1-based, so the report's retry badge reads retry 1 for a second attempt (Cucumber's own wire value is 0-based). Null where the runner reports nothing about attempts." },
                                        ["sourceFile"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "Where the scenario is written, matching the feature's sourceFile. NOT the same contract as a step's sourceFile, which is a bare file name from [CallerFilePath] on the build machine.", ["examples"] = new[] { "Features/Cake.feature" } },
                                        ["sourceLine"] = new Dictionary<string, object?> { ["type"] = new[] { "integer", "null" }, ["description"] = "The line the Scenario: or Scenario Outline: keyword is on - the declaration, not the Examples: row, so every row of an outline points at the same line. exampleValues is what says which row." },
                                        ["outlineId"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "Original scenario outline name for parameterized scenarios" },
                                        ["keyedByOutline"] = new Dictionary<string, object?> { ["type"] = "boolean", ["description"] = "Present, and true, only on an outline row whose name was filled from its example values (a Reqnroll row): its stableId, its place and its anchor key on outlineId in place of name, the name it bore before" },
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
                                            ["description"] = "The PlantUML source of each sequence diagram rendered for the scenario: hundreds of kilobytes each, never needed to answer a question (kronikol query flow sN tells the same story in a couple of KB). With CompressTestRunReportPayloads, a source of 8,192 characters or more (512 through 4.0.1) is a compressedPayload object instead of its text",
                                            ["items"] = new Dictionary<string, object?>
                                            {
                                                ["oneOf"] = new object[]
                                                {
                                                    new Dictionary<string, object?> { ["type"] = "string" },
                                                    new Dictionary<string, object?> { ["$ref"] = "#/$defs/compressedPayload" }
                                                }
                                            }
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
                ["compressedPayload"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["description"] = "A payload (a body or a diagram's PlantUML source) written compressed in place of its text, by CompressTestRunReportPayloads (#85), on by default from 4.0.0. kronikol query, kronikol merge and query.py read it as they read the text; to read it yourself, base64-decode $z and gunzip it, which gives the text's UTF-8. A compressed report (TestRunReport.krkl, formatVersion 3) writes none, and every reader still reads one",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["$h"] = new Dictionary<string, object?> { ["type"] = "string", ["pattern"] = "^b:[0-9a-f]{8}$", ["description"] = "The text's b: address, the one kronikol query prints for it: the first eight hex digits of the SHA-1 of its UTF-8", ["examples"] = new[] { "b:4bdea521" } },
                        ["$n"] = new Dictionary<string, object?> { ["type"] = "integer", ["minimum"] = 0, ["description"] = "The text's length in UTF-16 code units" },
                        ["$z"] = new Dictionary<string, object?> { ["type"] = "string", ["contentEncoding"] = "base64", ["contentMediaType"] = "application/gzip", ["description"] = "The base64 of the gzip of the text's UTF-8" }
                    },
                    ["required"] = new[] { "$h", "$n", "$z" }
                },
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
                        ["assertion"] = new Dictionary<string, object?> { ["type"] = "boolean", ["description"] = "true on a tracked assertion (Track.That, the assertion weave, or an ingested assertion record), and absent on every other step; a step without a keyword is not an assertion for that alone" },
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
                        ["content"] = new Dictionary<string, object?>
                        {
                            ["description"] = "The body as captured, after capture-time redaction and any MaxContentLength cap (a capped body ends with an ...truncated (N chars total) marker); null when there was none. With CompressTestRunReportPayloads, a body of 8,192 characters or more (512 through 4.0.1) is a compressedPayload object instead of its text",
                            ["oneOf"] = new object[]
                            {
                                new Dictionary<string, object?> { ["type"] = new[] { "string", "null" } },
                                new Dictionary<string, object?> { ["$ref"] = "#/$defs/compressedPayload" }
                            }
                        },
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
                        ["error"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "When the call threw instead of answering: the exception's message chain (the outer message, then each inner one after Caused by:). The exception's type is the statusText behind a bang, !HttpRequestException. Null on the request half and on a call that answered." },
                        ["traceId"] = new Dictionary<string, object?> { ["type"] = "string", ["format"] = "uuid", ["description"] = "Kronikol's own id for the request/response pair. Not the W3C trace id — that is activityTraceId." },
                        ["requestResponseId"] = new Dictionary<string, object?> { ["type"] = "string", ["format"] = "uuid", ["description"] = "Pairs a request with its response: both halves carry the same value" },
                        ["timestamp"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["format"] = "date-time", ["description"] = "When this half was captured (UTC); null when the capture path recorded none" },
                        ["metaType"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = Enum.GetNames(typeof(RequestResponseMetaType)), ["description"] = "Default for a request/response exchange, Event for a fire-and-forget publish" },
                        ["dependencyCategory"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "What kind of thing the callee is (database, cache, queue, ...) — drives participant shape and arrow colour in the diagram" },
                        ["callerDependencyCategory"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "The same, for the caller" },
                        ["phase"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = Enum.GetNames(typeof(TestPhase)), ["description"] = "Whether the call happened during Setup or the Action under test; Unknown when phase detection is off" },
                        ["attributionSource"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["enum"] = Enum.GetNames(typeof(AttributionSource)).Append(null).ToArray(), ["description"] = "How the call got its scenario: RequestHeader (the scenario's own request carried it), TestContext (the test framework's ambient context, or work that inherited it), Scope (an explicit scope or a message-carried identity), GlobalFallback, Detached (a detached flow, no scenario), None (nothing resolved), DocumentOwner (nothing named a scenario, but the call named a document a scenario had written earlier in the run: that writer's, for this one call), DocumentFlow (a detached flow had just written a scenario's document and this call came between that write and the flow's next operation on the document), Expired (inherited from a scenario that had already ended: background). Null when the capture path predates the mark." },
                        ["expiredFrom"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "When attributionSource is Expired: the id of the scenario whose context the call inherited after that scenario had ended" },
                        ["isUserAction"] = new Dictionary<string, object?> { ["type"] = "boolean", ["description"] = "A UI interaction (click, navigate) rather than a dependency call" },
                        ["activityTraceId"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "W3C trace id — the bridge to OpenTelemetry traces and application logs. Unlike traceId, which is Kronikol's own identifier for the request/response pair.", ["examples"] = new[] { "4bf92f3577b34da6a3ce929d0e0e4736" } },
                        ["activitySpanId"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "W3C span id" },
                        ["capturedBy"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" }, ["description"] = "Which capture path produced this entry: wire (proxy/TCP tap) or span (OpenTelemetry receiver)" },
                        ["warmUp"] = new Dictionary<string, object?>
                        {
                            ["type"] = "object",
                            ["additionalProperties"] = false,
                            ["required"] = new[] { "kind", "shape", "baselineMs", "baselineCalls" },
                            ["description"] = "Present on the request record of a call the run's first-call warm-up slowed down: the run's first call of its shape (service, method and templated path) when it took at least 10 times and 50 ms more than the median of the shape's calls that started after it ended, or a call that started while that one ran and waited for it. Absent on every other record.",
                            ["properties"] = new Dictionary<string, object?>
                            {
                                ["kind"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = new[] { "first", "waited" }, ["description"] = "first: the run's first call of its shape. waited: a call that started while that one ran and waited for it." },
                                ["shape"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "The method and the templated path (ids, numbers and timestamps folded, the query string left out), plus the templated statement head for a statement-shaped dependency. The service is the record's own serviceName." },
                                ["baselineMs"] = new Dictionary<string, object?> { ["type"] = "number", ["minimum"] = 0, ["description"] = "The median duration of the shape's calls that started after the first one ended." },
                                ["baselineCalls"] = new Dictionary<string, object?> { ["type"] = "integer", ["minimum"] = 1, ["description"] = "How many calls baselineMs is the median of." },
                                ["first"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "On a call that waited, the requestResponseId of the run's first call of its shape." }
                            }
                        },
                        ["durationMs"] = new Dictionary<string, object?> { ["type"] = new[] { "number", "null" }, ["description"] = "Wall-clock milliseconds between the request and its response: the capturer's own measurement when the record carried one (durationMs on the NDJSON input), otherwise derived from the two timestamps. Repeated on both halves of the pair; null when the request went unanswered and nothing was measured, when timestamps are absent, or when both records carry the same instant (a call logged after the fact, which measured nothing)." },
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
        XElement NonNegativeDecimal() => new(xs + "simpleType", new XElement(xs + "restriction", new XAttribute("base", "xs:decimal"),
            new XElement(xs + "minInclusive", new XAttribute("value", "0"))));

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
                new XElement(xs + "element", new XAttribute("name", "Assertion"), new XAttribute("type", "xs:boolean"), new XAttribute("minOccurs", "0")),
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
                                        new XElement(xs + "element", new XAttribute("name", "MediaType"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                                        new XElement(xs + "element", new XAttribute("name", "Problem"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0"))
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
                new XElement(xs + "element", new XAttribute("name", "Error"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "TraceId"), new XAttribute("type", "xs:string")),
                new XElement(xs + "element", new XAttribute("name", "RequestResponseId"), new XAttribute("type", "xs:string")),
                new XElement(xs + "element", new XAttribute("name", "Timestamp"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "MetaType"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "DependencyCategory"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "CallerDependencyCategory"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "Phase"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "AttributionSource"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "ExpiredFrom"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "IsUserAction"), new XAttribute("type", "xs:boolean"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "ActivityTraceId"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "ActivitySpanId"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "CapturedBy"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "DurationMs"), new XAttribute("type", "xs:decimal"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "WarmUp"), new XAttribute("minOccurs", "0"),
                    new XElement(xs + "complexType",
                        new XElement(xs + "sequence",
                            // Held as the JSON schema holds them (until 4.16.3 any kind and any number validated).
                            new XElement(xs + "element", new XAttribute("name", "Kind"),
                                new XElement(xs + "simpleType", new XElement(xs + "restriction", new XAttribute("base", "xs:string"),
                                    new XElement(xs + "enumeration", new XAttribute("value", WarmUpMark.FirstKind)),
                                    new XElement(xs + "enumeration", new XAttribute("value", WarmUpMark.WaitedKind))))),
                            new XElement(xs + "element", new XAttribute("name", "Shape"), new XAttribute("type", "xs:string")),
                            new XElement(xs + "element", new XAttribute("name", "BaselineMs"), NonNegativeDecimal()),
                            new XElement(xs + "element", new XAttribute("name", "BaselineCalls"), new XAttribute("type", "xs:positiveInteger")),
                            new XElement(xs + "element", new XAttribute("name", "First"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0"))))),
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
                new XElement(xs + "element", new XAttribute("name", "WarmUpSeconds"), NonNegativeDecimal(), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "IsHappyPath"), new XAttribute("type", "xs:boolean")),
                new XElement(xs + "element", new XAttribute("name", "NotATest"), new XAttribute("type", "xs:boolean"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "ErrorMessage"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "ErrorStackTrace"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "FailureCause"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "Tags"), new XAttribute("minOccurs", "0"),
                    new XElement(xs + "complexType",
                        new XElement(xs + "sequence",
                            new XElement(xs + "element", new XAttribute("name", "Tag"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"))
                        )
                    )
                ),
                new XElement(xs + "element", new XAttribute("name", "Rule"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "Attempt"), new XAttribute("type", "xs:int"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "EndedAt"), new XAttribute("type", "xs:dateTime"), new XAttribute("minOccurs", "0")),
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
                                        new XElement(xs + "element", new XAttribute("name", "MediaType"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                                        new XElement(xs + "element", new XAttribute("name", "Problem"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0"))
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
                new XElement(xs + "element", new XAttribute("name", "Tags"), new XAttribute("minOccurs", "0"),
                    new XElement(xs + "complexType",
                        new XElement(xs + "sequence",
                            new XElement(xs + "element", new XAttribute("name", "Tag"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"))
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
                            // The background block, before the diagnostics as in the JSON. minOccurs="0" for the
                            // same reason as the element after it.
                            new XElement(xs + "element", new XAttribute("name", "Background"), new XAttribute("minOccurs", "0"),
                                new XElement(xs + "complexType",
                                    new XElement(xs + "sequence",
                                        new XElement(xs + "element", new XAttribute("name", "Calls"), new XAttribute("type", "xs:int")),
                                        new XElement(xs + "element", new XAttribute("name", "AfterScenarioEnd"), new XAttribute("minOccurs", "0"),
                                            new XElement(xs + "complexType",
                                                new XElement(xs + "sequence",
                                                    new XElement(xs + "element", new XAttribute("name", "Scenario"), new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"),
                                                        new XElement(xs + "complexType",
                                                            new XElement(xs + "sequence",
                                                                new XElement(xs + "element", new XAttribute("name", "ScenarioId"), new XAttribute("type", "xs:string")),
                                                                new XElement(xs + "element", new XAttribute("name", "Name"), new XAttribute("type", "xs:string")),
                                                                new XElement(xs + "element", new XAttribute("name", "Calls"), new XAttribute("type", "xs:int")),
                                                                new XElement(xs + "element", new XAttribute("name", "LastAt"), new XAttribute("type", "xs:dateTime"), new XAttribute("minOccurs", "0"))
                                                            )
                                                        )
                                                    )
                                                )
                                            )
                                        ),
                                        new XElement(xs + "element", new XAttribute("name", "Interactions"), new XAttribute("minOccurs", "0"),
                                            new XElement(xs + "complexType",
                                                new XElement(xs + "sequence",
                                                    new XElement(xs + "element", new XAttribute("name", "HttpInteraction"), new XAttribute("type", "HttpInteractionType"), new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"))
                                                )
                                            )
                                        )
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