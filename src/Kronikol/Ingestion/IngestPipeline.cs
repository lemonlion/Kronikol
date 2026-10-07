using Kronikol.Ingestion.Cucumber;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Ingestion;

/// <summary>What to ingest and how to render it. See <see cref="IngestPipeline.Run"/>.</summary>
public sealed class IngestRequest
{
    /// <summary>NDJSON files of <see cref="InteractionRecord"/> lines (in addition to <see cref="Interactions"/>).</summary>
    public IReadOnlyList<string> InteractionFiles { get; init; } = [];

    /// <summary>In-memory interaction records (in addition to <see cref="InteractionFiles"/>).</summary>
    public IEnumerable<InteractionRecord>? Interactions { get; init; }

    /// <summary>Optional tests NDJSON file (<see cref="TestRunRecord"/> lines) supplying outcomes, durations and steps.</summary>
    public string? TestsFile { get; init; }

    /// <summary>
    /// Cucumber Messages NDJSON files (playwright-bdd's <c>cucumberReporter('message')</c>, cucumber-js
    /// <c>--format message</c>, Cucumber-JVM <c>--plugin message:…</c>). When given, the Gherkin structure
    /// they carry — feature description, rules, background, keywords, tables, doc strings, example values —
    /// <em>wins</em> for every scenario they own; <see cref="TestsFile"/> still contributes assertions, UI
    /// actions, attachments, a <c>bypassed</c> step's status and reason (matched to its Gherkin step by text)
    /// and the identity captured interactions join on. See <see cref="Cucumber.CucumberFeatureMerger"/>.
    /// </summary>
    public IReadOnlyList<string> CucumberMessagesFiles { get; init; } = [];

    /// <summary>Keep Cucumber hook steps (<c>BeforeEach hook</c>, …) in the step list. Default <c>false</c>.</summary>
    public bool IncludeHooks { get; init; }

    /// <summary>In-memory test-run records (in addition to <see cref="TestsFile"/>).</summary>
    public IEnumerable<TestRunRecord>? TestRecords { get; init; }

    /// <summary>Report options. <c>ReportsFolderPath</c> decides the output directory. Defaults to <see cref="IngestPipeline.DefaultOptions"/>.</summary>
    public ReportConfigurationOptions? Options { get; init; }

    /// <summary>Clear the in-process store (<see cref="RequestResponseLogger.Clear"/>) before replaying. Default <c>true</c> — ingest is a whole-run replay.</summary>
    public bool ClearExistingLogs { get; init; } = true;

    /// <summary>Feature name for scenarios that carry none.</summary>
    public string DefaultFeatureName { get; init; } = "Ingested";

    /// <summary>
    /// Verdict for tests that started but never reported an <c>end</c> record — a worker that crashed, a
    /// run that was killed, a capturer that only ever writes <c>start</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The default is <see cref="ExecutionResult.Passed"/>, and that is a compatibility decision, not a
    /// judgement.</b> Ingest predates the tests file: a capture that is nothing but interactions has no
    /// verdicts at all, and marking every such scenario red would make the common case — replaying a
    /// proxy-tap capture to see the diagrams — look like a catastrophic failure. So an absent verdict
    /// reads as "nothing said otherwise".
    /// </para>
    /// <para>
    /// The consequence is worth stating plainly: <b>a test whose process died mid-run renders as
    /// passed</b>. If your producer always writes an <c>end</c> record (Kronikol's Playwright reporter
    /// does, on failure and on timeout), set this to <see cref="ExecutionResult.Failed"/> and a missing
    /// verdict becomes the alarm it should be. CI pipelines that gate on the report should.
    /// </para>
    /// </remarks>
    public ExecutionResult ResultWhenUnknown { get; init; } = ExecutionResult.Passed;

    /// <summary>When true, the report is generated even if no scenario could be synthesised (it will be empty). Default <c>false</c>.</summary>
    public bool AllowEmpty { get; init; }

    /// <summary>
    /// Order each test's interactions as a call tree instead of strictly by timestamp: every response sits
    /// directly after its request, calls a service made while handling a request (their interval lies inside
    /// the parent's and their caller is the parent's service) sit between that request and its response,
    /// and siblings keep request-time order. Default <c>true</c>: externally captured traffic is usually
    /// concurrent, and a chronologically interleaved diagram makes it impossible to tell which reply answers
    /// which call (and defeats <c>CollapseConsecutiveIdenticalCalls</c>, which works on adjacent pairs).
    /// Set <c>false</c> for a strict timeline.
    /// </summary>
    public bool CallTreeOrdering { get; init; } = true;

    /// <summary>
    /// When set, interactions whose <c>testId</c> is not a test in the tests records — or every
    /// interaction, when there are no tests records — are re-attributed to this one scenario (typically
    /// "Traffic outside any test": warm-ups, health probes, manual browsing, background jobs). Leave
    /// null to render each unknown test id as its own scenario.
    /// </summary>
    public UnknownTestFold? FoldUnknownTestsInto { get; init; }

    /// <summary>
    /// Fold the wire and span views of the same call into one arrow before rendering (see
    /// <see cref="InteractionMerger"/>): the span-sourced record's test/trace ids win, the wire-sourced
    /// record's body, status and label win, and the merged request carries an
    /// <c>x-kronikol-captured-by: wire + span</c> pseudo-header. Only useful when a stack is captured from
    /// both sides. Default <c>false</c>.
    /// </summary>
    public bool MergeDuplicateInteractions { get; init; }

    /// <summary>Fraction (0–1] of the shorter interval two calls must share to be treated as the same call by <see cref="MergeDuplicateInteractions"/>. Default 0.8.</summary>
    public double MergeOverlapThreshold { get; init; } = InteractionMerger.DefaultOverlapThreshold;

    /// <summary>
    /// Fail the whole ingest with a <see cref="FormatException"/> on the first capture line that cannot
    /// be parsed. Default <c>false</c>: malformed lines are skipped and counted in
    /// <see cref="IngestResult.Diagnostics"/> instead, because a process killed mid-write leaves a
    /// truncated last line and losing an entire run's report to it helps nobody. Set it for CI, where a
    /// producer emitting garbage should be loud.
    /// </summary>
    public bool StrictParsing { get; init; }

    /// <summary>
    /// Attribute interactions that carry no test identity to the test that was running when they
    /// happened — see <see cref="IngestAttribution.AttributeByWindow(IReadOnlyList{InteractionRecord}, IReadOnlyList{IngestAttribution.TestWindow}, string?)"/> for the exact rule. Default
    /// <c>false</c>; turn it on for capturers that cannot see a test header (a database tee, a shared
    /// sidecar, an OTLP exporter).
    /// </summary>
    public bool AttributeByTestWindow { get; init; }

    /// <summary>
    /// How <see cref="AttributeByTestWindow"/> resolves a record whose timestamp lies inside more
    /// than one test window. <see cref="WindowAttributionMode.InnermostWins"/> (the default, and the
    /// historical behaviour) suits nested tests; <see cref="WindowAttributionMode.ExclusiveOnly"/>
    /// suits parallel workers — an ambiguous record stays unattributed (and counted in
    /// <see cref="IngestResult.Diagnostics"/>) instead of being filed under whichever test started
    /// last. With one worker the two modes are identical, because windows never overlap.
    /// </summary>
    public WindowAttributionMode WindowAttribution { get; init; } = WindowAttributionMode.InnermostWins;

    /// <summary>
    /// Before window attribution, attribute records by <em>content</em>: a record goes to the test
    /// whose window contains its timestamp and whose declared <see cref="TestRunRecord.Claims"/>
    /// appear literally in the record's URI or body — when exactly one such test exists (see
    /// <see cref="IngestAttribution.AttributeByClaims"/>). Exact even under fully overlapping
    /// windows, provided concurrent tests touch disjoint data. Records no claim matches fall
    /// through to <see cref="AttributeByTestWindow"/> untouched. Default <c>false</c>.
    /// </summary>
    public bool AttributeByClaims { get; init; }

    /// <summary>
    /// The placeholder test id a capturer stamps on traffic it could not attribute (a tap's
    /// <c>FallbackTestId</c>, e.g. <c>"session"</c>). Records carrying it are treated as unattributed by
    /// <see cref="AttributeByTestWindow"/> and by <see cref="DropUnattributed"/>.
    /// </summary>
    public string? WindowAttributionFallbackId { get; init; }

    /// <summary>
    /// Decides, per record, whether an interaction that is <em>still</em> unattributed — after
    /// <see cref="AttributeByTestWindow"/> has run, and by the same definition it uses (no test id, or the
    /// <see cref="WindowAttributionFallbackId"/> marker; also an id that matches no test when
    /// <see cref="FoldUnknownTestsInto"/> is set) — should be dropped rather than folded and rendered.
    /// Returning <c>true</c> discards the record <em>and</em> its paired response.
    /// </summary>
    /// <remarks>
    /// The use case is a capturer that runs for the whole session and cannot be switched off per test —
    /// a database tee that also sees the seeder's traffic, a sidecar that sees health probes. Dropping
    /// them at ingest keeps "Traffic outside any test" about the traffic a reader actually cares about.
    /// Drops are counted in <see cref="IngestResult.Diagnostics"/> as
    /// <see cref="DiagnosticKind.DroppedUnattributed"/>. Default null — nothing is dropped.
    /// </remarks>
    public Func<InteractionRecord, bool>? DropUnattributed { get; init; }

    /// <summary>
    /// Keep only the traffic of <em>this</em> run: every interaction whose request happened before the run
    /// began, or after it ended, is dropped before attribution (with its paired response), and counted as
    /// <see cref="DiagnosticKind.DroppedOutsideRunWindow"/>. Default <c>false</c>.
    /// </summary>
    /// <remarks>
    /// <para>The use case is a capturer whose files outlive a run — taps that append for as long as the
    /// stack is up — read against a tests file that is per run: without a window, the previous run's
    /// traffic (its test ids are no longer in the tests file) and the stack's start-up traffic are all
    /// folded into "Traffic outside any test", which then dwarfs the run it is supposed to describe.</para>
    /// <para>The window is <see cref="RunStartedAt"/> → <see cref="RunEndedAt"/> when given; otherwise it
    /// is derived from the tests records: start = the earliest record of any kind (a host that writes a
    /// <c>testrun</c>/<c>started</c> marker before the runner starts — see
    /// <see cref="TestRunRecord.Events.TestRun"/> — therefore keeps the runner's own set-up traffic, such as
    /// a global login, inside the run), end = the latest <c>testrun</c> marker that is not
    /// <see cref="TestRunRecord.RunStartedStatus"/>, or open when there is none (a run that died never
    /// wrote one, and its late traffic must not vanish). With no tests records and no explicit start nothing
    /// is dropped, and a diagnostic says so.</para>
    /// <para>Traffic inside the window that belongs to no test — a runner's global set-up, a health probe —
    /// is untouched: it still reaches <see cref="FoldUnknownTestsInto"/> / <see cref="DropUnattributed"/>.</para>
    /// </remarks>
    public bool DropOutsideRunWindow { get; init; }

    /// <summary>Explicit start of the run window (UTC); implies <see cref="DropOutsideRunWindow"/>. Default null — derived from the tests records.</summary>
    public DateTimeOffset? RunStartedAt { get; init; }

    /// <summary>Explicit end of the run window (UTC); implies <see cref="DropOutsideRunWindow"/>. Default null — derived from a <c>testrun</c> end marker, else open.</summary>
    public DateTimeOffset? RunEndedAt { get; init; }

    /// <summary>
    /// Give interactions the phase of the top-level step their call started in: <c>Given</c>/<c>Context</c>
    /// becomes <see cref="TestPhase.Setup"/>, <c>When</c>/<c>Then</c> becomes <see cref="TestPhase.Action"/>,
    /// <c>And</c>/<c>But</c> inherit. A request and its response take one phase, so a response answered after its step
    /// ended takes its request's, and a phase a capturer wrote on either half wins
    /// (<see cref="IngestAttribution.ApplyPhaseFromSteps"/> states the rule). The phase is each call's <c>phase</c> in the
    /// data files and for <c>kronikol query</c>. <c>kronikol ingest</c> prints how many records took a phase; the count is
    /// not a diagnostic. Default <c>false</c>.
    /// </summary>
    /// <remarks>
    /// A phase draws nothing on its own. The Setup partition is <see cref="ReportConfigurationOptions.SeparateSetup"/>'s:
    /// with it on, each test gets the Setup/Action boundary an in-process run draws, at the start of its first
    /// <c>When</c>/<c>Then</c> step that follows a <c>Given</c> step whether or not this is set, else just before its
    /// first call phased Action that follows one phased Setup, the phases this sets among them. Until 4.1.0 this option
    /// was documented as partitioning the diagram, and never did.
    /// </remarks>
    public bool PhaseFromSteps { get; init; }

    /// <summary>
    /// Directory that relative <c>attachment</c> paths in the tests file are resolved against. Default:
    /// the current directory.
    /// </summary>
    public string? AttachmentsBase { get; init; }

    /// <summary>
    /// The directory a tests record's absolute <see cref="TestRunRecord.SourceFile"/> is made relative to, so a
    /// build machine's paths stay out of the report. Default: the current directory, which in CI is the checkout. An
    /// absolute path outside it is kept as written, and a diagnostic counts them. Relative paths are taken as relative
    /// to the repository already; backslashes become <c>/</c> either way.
    /// </summary>
    public string? SourceRoot { get; init; }

    /// <summary>
    /// The spans the services under test recorded, for internal flow: each call's popup, flame chart and whole-test flow
    /// are drawn from these as an in-process run draws them from its activities. A call joins its spans by its
    /// <see cref="InteractionRecord.ActivityTraceId"/> (the trace of the span it was made under) and
    /// <see cref="InteractionRecord.ActivitySpanId"/> (that span), and a span must start inside the call's time, from
    /// 50 ms before its request to its response. <c>kronikol ingest --spans</c> reads them from OTLP/JSON lines with
    /// <c>Kronikol.Extensions.Otlp</c> (<c>OtlpTraceReader.ReadJsonLines</c>, then <c>OtlpSpan.ToFlowSpan()</c>).
    /// </summary>
    /// <remarks>
    /// <para>Given (even empty), the report draws internal flow from these alone, with
    /// <see cref="ReportConfigurationOptions.InternalFlowTracking"/> on for the run whatever the options say (an ingest
    /// turns it off only because a capture holds no spans), and this process's span store
    /// (<see cref="Kronikol.InternalFlow.InternalFlowSpanStore"/>) is neither read nor written. Ids are lowercased, a span
    /// given twice (the same trace id and span id) is drawn once, and the spans are all drawn unless
    /// <see cref="ReportConfigurationOptions.InternalFlowSpanGranularity"/> is
    /// <see cref="Kronikol.InternalFlowSpanGranularity.Manual"/> with
    /// <see cref="ReportConfigurationOptions.InternalFlowActivitySources"/> naming sources: the default's list is .NET's
    /// instrumentation, and the capturer chose what to export. <see cref="IngestResult.Diagnostics"/> counts the calls
    /// whose trace no span has and the spans no call's internal flow holds.</para>
    /// <para>Null, the default, draws no internal flow unless the options turn it on, as before 4.4.0.</para>
    /// </remarks>
    public IReadOnlyList<Kronikol.InternalFlow.FlowSpan>? Spans { get; init; }

    /// <summary>
    /// Empty the report's <c>attachments/</c> folder before generating, so it holds exactly this run's
    /// artefacts. Default <c>false</c> — nothing has ever removed stale copies, and a host that renders
    /// several runs into one folder relies on that.
    /// </summary>
    public bool CleanAttachments { get; init; }

    /// <summary>
    /// Diagnostics the host already knows before the ingest starts — typically the capture health of its
    /// taps (<see cref="DiagnosticKind.CaptureDegraded"/>: a decoder that gave up on a connection, oversize
    /// payloads skipped, export payloads dropped), but any <see cref="DiagnosticEntry"/> is accepted. They
    /// are carried verbatim into <see cref="IngestResult.Diagnostics"/> (first, ahead of what the ingest
    /// itself records), into the top-level <c>diagnostics</c> array of <c>TestRunReport.json</c>, onto the labs
    /// page beside the report (<c>TestRunReport.labs.html</c>, unless
    /// <see cref="ReportConfigurationOptions.GenerateLabsReport"/> is off), and into the "Report diagnostics" section
    /// of <c>TestRunReport.html</c> when <see cref="ReportConfigurationOptions.ShowReportDiagnosticsSection"/> asks
    /// for that section.
    /// Default empty — nothing changes when a host hands in nothing.
    /// <c>kronikol ingest --diagnostic "&lt;kind&gt;:&lt;message&gt;" --diagnostics-section</c> is the CLI form.
    /// </summary>
    public IReadOnlyList<DiagnosticEntry> HostDiagnostics { get; init; } = [];
}

/// <summary>The single scenario that collects interactions of unknown tests (<see cref="IngestRequest.FoldUnknownTestsInto"/>).</summary>
/// <param name="ScenarioName">Display name, e.g. "Traffic outside any test".</param>
/// <param name="ScenarioId">Scenario id (also the <c>testId</c> the folded interactions take).</param>
public sealed record UnknownTestFold(string ScenarioName, string ScenarioId = "outside-any-test");

/// <summary>Outcome of <see cref="IngestPipeline.Run"/>.</summary>
/// <param name="InteractionCount">Interaction lines replayed into the store.</param>
/// <param name="ScenarioCount">Scenarios in the synthesised model.</param>
/// <param name="Features">The synthesised model.</param>
/// <param name="ReportsDirectory">Where the report files were written.</param>
/// <param name="Start">Run start (UTC).</param>
/// <param name="End">Run end (UTC).</param>
/// <param name="Generated">False when nothing was ingested and the report was skipped.</param>
public sealed record IngestResult(
    int InteractionCount,
    int ScenarioCount,
    Feature[] Features,
    string ReportsDirectory,
    DateTime Start,
    DateTime End,
    bool Generated)
{
    /// <summary>Path of the main HTML report (may not exist when <see cref="Generated"/> is false).</summary>
    public string TestRunReportHtml => Path.Combine(ReportsDirectory, "TestRunReport.html");

    /// <summary>
    /// Everything that went wrong, or is worth knowing, about this ingest and the report generation it
    /// drove: skipped malformed lines, diagrams that could not be produced, outputs that failed, step
    /// labels that still do not read as sentences. Empty is the happy path.
    /// </summary>
    /// <remarks>
    /// This is the contract a host renders: <c>kronikol ingest</c> prints it, and a dashboard can show
    /// per-scenario counts instead of leaving an empty diagram unexplained. Report generation is
    /// diagnostics, never a reason for a run to fail, so nothing here throws.
    /// </remarks>
    public IReadOnlyList<DiagnosticEntry> Diagnostics { get; init; } = [];

    /// <summary>
    /// How many interaction records <see cref="IngestRequest.PhaseFromSteps"/> gave a phase; 0 when it is off. Printed by
    /// <c>kronikol ingest</c>, and not a diagnostic: it is the run doing what it was asked.
    /// </summary>
    internal int PhasedRecords { get; init; }
}

/// <summary>
/// Replays NDJSON captures into the in-process store and generates the standard Kronikol reports from
/// them — the programmatic form of <c>kronikol ingest</c>. Use it when a capturer (an out-of-process
/// proxy tap, a Java/Node service, any tool that can write <see cref="InteractionRecord"/> lines) runs
/// outside the test process, or when a host wants to regenerate a report from files after the fact.
/// </summary>
/// <remarks>
/// Order of operations: read everything → attribute and phase-tag records that need it → (optionally)
/// clear the store → replay records in timestamp order (the sequence diagram follows enqueue order, not
/// timestamps) → normalise each log's <c>TestName</c> from the tests file → reset the diagram cache →
/// synthesise <see cref="Feature"/>s → <see cref="ReportGenerator.CreateStandardReportsWithDiagrams"/>.
/// Capture-time redaction (<see cref="RequestResponseLogger.Redaction"/>) applies during replay, so
/// secrets present in a raw capture file can still be kept out of the report data files.
/// </remarks>
public static class IngestPipeline
{
    /// <summary>
    /// Defaults suited to externally captured traffic: browser-side PlantUML rendering, no internal-flow
    /// tracking (a capture holds no spans; <see cref="IngestRequest.Spans"/> turns it on for the run), component
    /// diagram on, consecutive identical calls collapsed.
    /// </summary>
    public static ReportConfigurationOptions DefaultOptions() => new()
    {
        PlantUmlRendering = PlantUmlRendering.BrowserJs,
        InternalFlowTracking = false,
        GenerateComponentDiagram = true,
        CollapseConsecutiveIdenticalCalls = true,
        ShowNoInteractionsMarker = true,
    };

    /// <summary>
    /// Runs the pipeline. Throws <see cref="FileNotFoundException"/> for missing files, and
    /// <see cref="FormatException"/> for malformed input lines only when
    /// <see cref="IngestRequest.StrictParsing"/> is set (otherwise they are skipped and reported in
    /// <see cref="IngestResult.Diagnostics"/>).
    /// </summary>
    public static IngestResult Run(IngestRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var options = request.Options ?? DefaultOptions();
        // An ingest turns internal flow off only for want of spans; given some, the run draws them, and the caller's
        // options are left as they were.
        if (request.Spans is not null && !options.InternalFlowTracking)
            options = options with { InternalFlowTracking = true };

        // Step delimiter bars and ✓/✗ assertion notes are baked into PlantUML as the records are replayed,
        // by static emitters that know nothing about report options — so the diagram-side switch has to
        // agree with this run's options for as long as the replay lasts, and be handed back afterwards.
        var previousCapitalisation = StepText.CapitaliseEnabled;
        StepText.CapitaliseEnabled = options.CapitaliseStepText;
        try
        {
            return RunCore(request, options);
        }
        finally
        {
            StepText.CapitaliseEnabled = previousCapitalisation;
        }
    }

    private static IngestResult RunCore(IngestRequest request, ReportConfigurationOptions options)
    {
        var diagnostics = new ReportDiagnosticsCollector();
        // The host's entries go in first, so they reach the report's diagnostics section (the collector is
        // the scope the generator renders from) and lead IngestResult.Diagnostics.
        diagnostics.AddRange(request.HostDiagnostics);

        var records = ReadInteractions(request, diagnostics);
        var testRecords = NormaliseSources(ReadTestRecords(request, diagnostics), request.SourceRoot, diagnostics);

        // The contract puts a failed call's error on its response half, beside the error's type as the status; one on a
        // request is kept where it was written, and the producer told.
        var errorsOnRequests = records.Count(r => !string.Equals(r.Type, "Response", StringComparison.OrdinalIgnoreCase)
                                                  && !r.IsMarker && !string.IsNullOrWhiteSpace(r.Error));
        if (errorsOnRequests > 0)
            diagnostics.Add(DiagnosticKind.Other,
                $"{errorsOnRequests} request record(s) carried an error, kept on the request. Write a failed call's error on its "
                + "response half, with the error's type behind a bang as the statusCode (!TypeError), so the call reads as failed.");

        // Cucumber Messages (playwright-bdd's cucumberReporter('message') and friends): synthesised into the
        // same start/step/end records the tests file uses, so the Gherkin steps travel the existing marker,
        // attribution and naming paths untouched; the reporter's own step events for the scenarios the
        // messages own are dropped so a diagram never grows two sets of delimiter bars. What a dropped step
        // record said about a bypass is read first and applied to the Gherkin step it reports on.
        var cucumber = request.CucumberMessagesFiles.Count == 0 ? null
            : CucumberFeatureSynthesizer.BuildFromFiles(request.CucumberMessagesFiles,
                new CucumberSynthesisOptions { IncludeHooks = request.IncludeHooks, DefaultFeatureName = request.DefaultFeatureName },
                CucumberTestsFileBypass.Collect(testRecords));
        var earlierAttemptRecords = 0;
        if (cucumber is not null)
        {
            // The synthesis's own account: malformed lines, scenarios captured traffic cannot join, attachments that
            // could not be written.
            foreach (var warning in cucumber.Warnings)
                diagnostics.Add(DiagnosticKind.Other, warning);
            if (cucumber.UnmatchedTestsFileBypasses > 0)
                diagnostics.Add(DiagnosticKind.Other,
                    $"{cucumber.UnmatchedTestsFileBypasses} bypassed step record(s) of scenarios the Cucumber messages own matched no "
                    + "Gherkin step by text; their bypass was not applied. A record finds its step by its text, at level 0.");

            testRecords.RemoveAll(r => CucumberFeatureMerger.IsReplacedStep(r, cucumber));
            testRecords.AddRange(cucumber.Markers);
            // An attempt that minted a test id of its own is not the scenario the messages built: its reporter records
            // and its calls would make a scenario of their own, failed, of a test that passed on its retry.
            if (cucumber.EarlierAttemptTestIds.Count > 0)
            {
                testRecords.RemoveAll(r => cucumber.EarlierAttemptTestIds.Contains(r.TestId));
                earlierAttemptRecords += records.RemoveAll(r => cucumber.EarlierAttemptTestIds.Contains(r.TestId));
            }
        }

        // Every start of a test opens an attempt, and the report shows the last. For a scenario the messages own, only
        // the messages' starts count, one per attempt: the reporter writes its own for the same attempts.
        var cucumberStarts = cucumber is null ? null : new HashSet<TestRunRecord>(cucumber.Markers, ReferenceEqualityComparer.Instance);
        var attempts = TestAttempts.Of(testRecords, cucumberStarts is null ? null
            : start => !cucumber!.TestNames.ContainsKey(start.TestId) || cucumberStarts.Contains(start));
        if (attempts.InferredRetries > 0)
            diagnostics.Add(DiagnosticKind.Other,
                $"{attempts.InferredRetries} test(s) had more than one start without an attempt number; each start after a test's first "
                + "was read as a retry, and the report shows the test's last attempt with a retry label for each earlier one. "
                + "Write attempt on start records to say which run each is.");
        var current = attempts.Current(testRecords);

        records = DropOutsideRunWindow(records, testRecords, request, diagnostics);
        records = Attribute(records, testRecords, current, request, diagnostics, out var phasedRecords);
        records = DropEarlierAttempts(records, attempts, ref earlierAttemptRecords);
        if (earlierAttemptRecords > 0)
            diagnostics.Add(DiagnosticKind.Other,
                $"{earlierAttemptRecords} interaction record(s) of earlier attempts of retried tests left out: a retried test's "
                + "scenario shows its last attempt, with a retry label for each earlier one.");
        AddDiagramMarkers(records, current);
        if (options.SeparateSetup)
            AddSetupBoundaries(records, current);

        var reportsDirectory = ReportGenerator.ResolveReportsDirectory(options);

        if (request.ClearExistingLogs)
            RequestResponseLogger.Clear();

        var ordered = Order(records, current, request);
        var logs = Replay(ordered, current);

        var synthesised = FeatureSynthesizer.Build(
            testRecords, logs, request.DefaultFeatureName, request.ResultWhenUnknown, request.AttachmentsBase, attempts);
        if (cucumber is not null)
        {
            // Messages win for structure (feature/rule/background/outline/tags/tables/doc strings);
            // the tests file still contributes assertions, attachments and the identity join.
            synthesised = CucumberFeatureMerger.Merge(cucumber, synthesised, current);
        }

        if (request.FoldUnknownTestsInto is { } foldedInto)
        {
            // The fold scenario is not a test: nothing ran and nothing ended, so ResultWhenUnknown (meant
            // for tests that started but never reported an end) must not mark it failed.
            foreach (var scenario in synthesised.Features.SelectMany(f => f.Scenarios))
            {
                if (scenario.Id == foldedInto.ScenarioId)
                {
                    scenario.Result = ExecutionResult.Passed;
                    // And history must not read it as one: it comes and goes with whether unattributed
                    // traffic survived, which made it "absent" in one run and "new" in the next.
                    scenario.NotATest = true;
                }
            }
        }

        // A scenario that started and never ended took ResultWhenUnknown rather than a verdict. With the
        // compatibility default of Passed that is invisible in the report — a crashed worker looks green —
        // so it is recorded as a diagnostic, which every reader of the file (and `kronikol query`) surfaces.
        if (synthesised.DefaultedResultCount > 0)
            diagnostics.Add(DiagnosticKind.ResultDefaulted,
                $"{synthesised.DefaultedResultCount} scenario(s) recorded no result and were reported as "
                + $"{request.ResultWhenUnknown}. Set IngestRequest.ResultWhenUnknown = ExecutionResult.Failed if your "
                + "producer always writes an end record.");

        var scenarioCount = synthesised.Features.Sum(f => f.Scenarios.Length);

        if (scenarioCount == 0 && !request.AllowEmpty)
            return new IngestResult(logs.Count, 0, synthesised.Features, reportsDirectory, synthesised.Start, synthesised.End, Generated: false)
            {
                Diagnostics = diagnostics.Entries,
                PhasedRecords = phasedRecords,
            };

        // Cleaned by the generator's beforeFirstWrite hook below, not here: a run with scenarios first
        // moves the previous run to runs/<run>/, attachments included, and emptying the folder before that
        // deleted the screenshots of the very run that was about to be kept. A pass with no scenarios
        // rotates nothing and never reaches the hook, so it is cleaned as it always was.
        if (request.CleanAttachments && scenarioCount == 0)
            CleanAttachmentsFolder(reportsDirectory, diagnostics);

        DefaultDiagramsFetcher.Reset();
        using (ReportDiagnosticsScope.Begin(diagnostics))
        {
            // What the SOURCE said the run ran on, never this process's. `kronikol ingest` is a net10.0
            // tool reading files a suite in another language may have produced - the Cucumber fixture in
            // this repo is playwright-bdd on node.js - so writing RunEnvironment.Current here named the
            // machine doing the reading and called it the machine that ran the tests.
            ReportGenerator.CreateStandardReportsWithDiagramsInEnvironment(synthesised.Features, synthesised.Start, synthesised.End, options, cucumber?.Environment ?? RunEnvironment.Unrecorded,
                Environment.GetEnvironmentVariable,
                beforeFirstWrite: request.CleanAttachments ? directory => CleanAttachmentsFolder(directory, diagnostics) : null,
                spans: DistinctSpans(request.Spans, diagnostics));
        }

        DefaultDiagramsFetcher.Reset();

        return new IngestResult(logs.Count, scenarioCount, synthesised.Features, reportsDirectory, synthesised.Start, synthesised.End, Generated: true)
        {
            Diagnostics = diagnostics.Entries,
            PhasedRecords = phasedRecords,
        };
    }

    /// <summary>
    /// The spans the report draws (<see cref="IngestRequest.Spans"/>): their ids lowercased, as a call's are, an empty
    /// parent read as none, and each trace id and span id once, the first kept. A span with no trace id or span id is
    /// left out, since no call can join it. Each is said in a diagnostic.
    /// </summary>
    private static Kronikol.InternalFlow.FlowSpan[]? DistinctSpans(IReadOnlyList<Kronikol.InternalFlow.FlowSpan>? spans, ReportDiagnosticsCollector diagnostics)
    {
        if (spans is null)
            return null;

        var seen = new HashSet<(string Trace, string Span)>();
        var distinct = new List<Kronikol.InternalFlow.FlowSpan>(spans.Count);
        var repeated = 0;
        var withoutIds = 0;
        foreach (var span in spans)
        {
            if (LowerHex(span.TraceId) is not { } trace || LowerHex(span.SpanId) is not { } id)
            {
                withoutIds++;
                continue;
            }

            if (!seen.Add((trace, id)))
            {
                repeated++;
                continue;
            }

            var parent = LowerHex(span.ParentSpanId);
            distinct.Add(trace == span.TraceId && id == span.SpanId && parent == span.ParentSpanId
                ? span
                : span with { TraceId = trace, SpanId = id, ParentSpanId = parent });
        }

        if (repeated > 0)
            diagnostics.Add(DiagnosticKind.Other,
                $"{repeated} span(s) were supplied more than once (the same trace id and span id) and are drawn once.");
        if (withoutIds > 0)
            diagnostics.Add(DiagnosticKind.Other,
                $"{withoutIds} supplied span(s) had no trace id or span id and were left out: no call can join them.");
        return distinct.ToArray();
    }

    private static string? LowerHex(string? id) => string.IsNullOrWhiteSpace(id) ? null : id.Trim().ToLowerInvariant();

    /// <summary>Reads the interaction captures, tolerating (and counting) torn lines unless strict parsing was asked for.</summary>
    private static List<InteractionRecord> ReadInteractions(IngestRequest request, ReportDiagnosticsCollector diagnostics)
    {
        var malformed = request.StrictParsing ? null : new List<MalformedLine>();
        var records = new List<InteractionRecord>();

        foreach (var file in request.InteractionFiles)
        {
            if (!File.Exists(file))
                throw new FileNotFoundException("Interaction file not found.", file);
            records.AddRange(NdjsonInteractionReader.ReadFile(file, malformed));
        }

        if (request.Interactions is not null)
            records.AddRange(request.Interactions);

        ReportMalformed(malformed, diagnostics);
        return records;
    }

    /// <summary>Reads the tests file, tolerating (and counting) torn lines unless strict parsing was asked for.</summary>
    private static List<TestRunRecord> ReadTestRecords(IngestRequest request, ReportDiagnosticsCollector diagnostics)
    {
        var malformed = request.StrictParsing ? null : new List<MalformedLine>();
        var testRecords = new List<TestRunRecord>();

        if (!string.IsNullOrWhiteSpace(request.TestsFile))
        {
            if (!File.Exists(request.TestsFile))
                throw new FileNotFoundException("Tests file not found.", request.TestsFile);
            testRecords.AddRange(NdjsonTestRunReader.ReadFile(request.TestsFile, malformed));
        }

        if (request.TestRecords is not null)
            testRecords.AddRange(request.TestRecords);

        ReportMalformed(malformed, diagnostics);
        return testRecords;
    }

    private static void ReportMalformed(List<MalformedLine>? malformed, ReportDiagnosticsCollector diagnostics)
    {
        foreach (var line in malformed ?? [])
            diagnostics.Add(DiagnosticKind.MalformedLine, line.ToString());
    }

    /// <summary>
    /// The run window (<see cref="IngestRequest.DropOutsideRunWindow"/>): explicit bounds win, else the tests
    /// records supply them. Returns null when no window can be established.
    /// </summary>
    public static (DateTimeOffset Start, DateTimeOffset? End)? ResolveRunWindow(IngestRequest request, IReadOnlyList<TestRunRecord> testRecords)
    {
        var start = request.RunStartedAt;
        if (start is null)
        {
            var earliest = testRecords.Where(r => r.Timestamp is not null).Select(r => r.Timestamp!.Value).DefaultIfEmpty().Min();
            if (earliest == default)
                return null;
            start = earliest;
        }

        var end = request.RunEndedAt;
        if (end is null)
        {
            var endMarkers = testRecords
                .Where(r => r.IsRunMarker && r.Timestamp is not null
                            && !string.Equals(r.Status, TestRunRecord.RunStartedStatus, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.Timestamp!.Value)
                .ToArray();
            if (endMarkers.Length > 0)
                end = endMarkers.Max();
        }

        return (start.Value, end);
    }

    /// <summary>
    /// Drops every interaction pair whose request lies outside the run window — the previous run's
    /// traffic, the stack's start-up, anything after the run ended — before attribution gets a chance to
    /// fold it. A pair is judged on its earliest record, so a late response to an in-run request stays.
    /// </summary>
    private static List<InteractionRecord> DropOutsideRunWindow(
        List<InteractionRecord> records, List<TestRunRecord> testRecords, IngestRequest request, ReportDiagnosticsCollector diagnostics)
    {
        if (!request.DropOutsideRunWindow && request.RunStartedAt is null && request.RunEndedAt is null)
            return records;

        var window = ResolveRunWindow(request, testRecords);
        if (window is null)
        {
            diagnostics.Add(DiagnosticKind.Other, "DropOutsideRunWindow: no run window could be derived (no tests records and no explicit start); nothing was dropped.");
            return records;
        }

        var (start, end) = window.Value;

        // Pairs are judged by their earliest timestamp (the request); a record with no pair id stands alone.
        var pairStart = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            if (record.RequestResponseId is { Length: > 0 } id && record.Timestamp is { } ts
                && (!pairStart.TryGetValue(id, out var known) || ts < known))
                pairStart[id] = ts;
        }

        var kept = new List<InteractionRecord>(records.Count);
        int before = 0, after = 0;
        foreach (var record in records)
        {
            DateTimeOffset? at = record.RequestResponseId is { Length: > 0 } id && pairStart.TryGetValue(id, out var ps) ? ps : record.Timestamp;
            if (at is null)
            {
                kept.Add(record);
                continue;
            }

            if (at < start)
            {
                before++;
                continue;
            }

            if (end is { } e && at > e)
            {
                after++;
                continue;
            }

            kept.Add(record);
        }

        if (before + after > 0)
        {
            var endText = end is { } e2 ? e2.ToString("o") : "open";
            diagnostics.Add(DiagnosticKind.DroppedOutsideRunWindow,
                $"{before + after} interaction record(s) outside the run window ({start:o} → {endText}) dropped: {before} before the run began, {after} after it ended.");
        }

        return kept;
    }

    /// <summary>
    /// Applies the ingest-time attribution passes — content claims, window attribution and
    /// phase-from-steps — then folds wire and span duplicates (<see cref="IngestRequest.MergeDuplicateInteractions"/>),
    /// and only then the <see cref="IngestRequest.DropUnattributed"/> filter: a record only reaches the
    /// filter once every chance to identify it has been taken, and a span twin is such a chance.
    /// </summary>
    /// <remarks>
    /// The merge sits between the passes and the filter deliberately. After the filter, a wire record
    /// whose claim was contested is gone before its twin could name its test. Before the passes, a
    /// record the filter would have removed competes for a span it has no right to: the passes are what
    /// say which twin is whose, and the merger pairs records that agree on their test first.
    /// </remarks>
    private static List<InteractionRecord> Attribute(
        List<InteractionRecord> records, List<TestRunRecord> testRecords, List<TestRunRecord> currentAttempts,
        IngestRequest request, ReportDiagnosticsCollector diagnostics, out int phasedRecords)
    {
        phasedRecords = 0;
        if (request.AttributeByClaims)
        {
            var claimWindows = IngestAttribution.BuildClaimWindows(testRecords);
            var (claimedRecords, claimed, contested, contests) = IngestAttribution.AttributeByClaimsNamingContests(records, claimWindows, request.WindowAttributionFallbackId);
            records = claimedRecords;
            if (claimed > 0)
                diagnostics.Add(DiagnosticKind.UnattributedInteractions, $"{claimed} interaction record(s) attributed to a test by content claims.");
            if (contested > 0)
            {
                // Named, because the cause is nearly always one test: a sweep that claims every seeded
                // customer contests every record of every other test for as long as it runs.
                const int shown = 3;
                var most = string.Join(", ", contests.Take(shown).Select(c => $"{c.Contest} ({c.Records})"));
                var more = contests.Count > shown ? $" and {contests.Count - shown} more" : "";
                diagnostics.Add(DiagnosticKind.Other, $"{contested} interaction record(s) matched the claims of more than one in-flight test and were left for window attribution; most contested: {most}{more}.");
            }
        }

        if (request.AttributeByTestWindow)
        {
            var windows = IngestAttribution.BuildWindows(testRecords);
            var (attributedRecords, attributed, ambiguous) = IngestAttribution.AttributeByWindow(
                records, windows, request.WindowAttribution, request.WindowAttributionFallbackId);
            records = attributedRecords;
            if (attributed > 0)
                diagnostics.Add(DiagnosticKind.UnattributedInteractions, $"{attributed} interaction record(s) attributed to a test by time window.");
            if (request.WindowAttribution == WindowAttributionMode.ExclusiveOnly)
            {
                // Always, even at zero: the line is what proves the mode ran (a parallel suite whose
                // taps died would otherwise be indistinguishable from one whose attribution is exact).
                diagnostics.Add(DiagnosticKind.Other,
                    $"WindowAttribution ExclusiveOnly: {ambiguous} interaction record(s) fell inside more than one test window and stayed unattributed.");
            }
        }

        if (request.PhaseFromSteps)
        {
            // A retried test's phases come from the attempt the report shows: an earlier attempt's last step would carry
            // its phase into the next attempt's first And.
            var stepWindows = IngestAttribution.BuildStepWindows(currentAttempts);
            // The count is the run working as asked, so it is not a diagnostic (an empty list is the happy path, and every
            // query answer would carry it): kronikol ingest prints it beside "Replayed".
            (records, phasedRecords) = IngestAttribution.ApplyPhaseFromSteps(records, stepWindows);
        }

        if (request.MergeDuplicateInteractions)
            records = InteractionMerger.Merge(ByTimestamp(records), request.MergeOverlapThreshold, request.WindowAttributionFallbackId);

        records = DropUnattributed(records, testRecords, request, diagnostics);

        var stillUnattributed = records.Count(r => IngestAttribution.NeedsAttribution(r, request.WindowAttributionFallbackId));
        if (stillUnattributed > 0)
            diagnostics.Add(DiagnosticKind.UnattributedInteractions, $"{stillUnattributed} interaction record(s) could not be attributed to a test.");

        return records;
    }

    /// <summary>
    /// Leaves out the calls of a retried test's earlier attempts: every record of the test whose call began before the
    /// test's last attempt started. A pair is judged on its earliest timestamp, as the run window judges it, so a late
    /// response to an earlier attempt's request goes with it. Records without a timestamp stay.
    /// </summary>
    private static List<InteractionRecord> DropEarlierAttempts(List<InteractionRecord> records, TestAttempts attempts, ref int dropped)
    {
        var pairStart = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            if (record.RequestResponseId is { Length: > 0 } id && record.Timestamp is { } ts
                && (!pairStart.TryGetValue(id, out var known) || ts < known))
                pairStart[id] = ts;
        }

        var kept = new List<InteractionRecord>(records.Count);
        foreach (var record in records)
        {
            var at = record.RequestResponseId is { Length: > 0 } id && pairStart.TryGetValue(id, out var ps) ? ps : record.Timestamp;
            if (at is { } when && !string.IsNullOrWhiteSpace(record.TestId)
                && attempts.LastAttemptStart(record.TestId) is { } lastStarted && when < lastStarted)
            {
                dropped++;
                continue;
            }

            kept.Add(record);
        }

        return kept;
    }

    /// <summary>
    /// The tests records' source paths as the report holds them (<see cref="IngestRequest.SourceRoot"/>): normalised, and
    /// relative to the source root when under it. Absolute paths outside it are kept, and one diagnostic counts them.
    /// </summary>
    private static List<TestRunRecord> NormaliseSources(List<TestRunRecord> testRecords, string? sourceRoot, ReportDiagnosticsCollector diagnostics)
    {
        if (!testRecords.Any(r => r.SourceFile is not null))
            return testRecords;

        var root = SourcePaths.ResolveRoot(sourceRoot);
        var outside = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<TestRunRecord>(testRecords.Count);
        foreach (var record in testRecords)
        {
            if (record.SourceFile is null)
            {
                result.Add(record);
                continue;
            }

            var path = SourcePaths.UnderRoot(SourcePaths.Normalise(record.SourceFile), root, out var isOutside);
            if (isOutside)
                outside.Add(path!);
            result.Add(record with { SourceFile = path });
        }

        if (outside.Count > 0)
            diagnostics.Add(DiagnosticKind.Other,
                $"{outside.Count} source path(s) are absolute and outside the source root ({root}), and were kept as written, "
                + $"so the report carries a build machine's paths (e.g. {outside.First()}). Pass --source-root (IngestRequest.SourceRoot) "
                + "or write sourceFile relative to the repository.");
        return result;
    }

    /// <summary>
    /// Drops the records the host does not want in the report at all — evaluated only on records that are
    /// still unattributed, and dropping a request takes its paired response with it.
    /// </summary>
    private static List<InteractionRecord> DropUnattributed(
        List<InteractionRecord> records, List<TestRunRecord> testRecords, IngestRequest request, ReportDiagnosticsCollector diagnostics)
    {
        if (request.DropUnattributed is not { } predicate)
            return records;

        var knownTestIds = request.FoldUnknownTestsInto is null
            ? null
            : new HashSet<string>(
                testRecords.Where(t => !string.IsNullOrWhiteSpace(t.TestId)).Select(t => t.TestId),
                StringComparer.Ordinal);

        bool IsUnattributed(InteractionRecord record) =>
            IngestAttribution.NeedsAttribution(record, request.WindowAttributionFallbackId)
            || (knownTestIds is not null && !knownTestIds.Contains(record.TestId)
                && record.TestId != request.FoldUnknownTestsInto!.ScenarioId);

        // First pass decides on the records that carry the identity; the second removes their partners,
        // so a dropped request never leaves an orphaned response arrow behind.
        var droppedPairs = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<InteractionRecord>(records.Count);
        var dropped = 0;

        foreach (var record in records)
        {
            if (IsUnattributed(record) && Evaluate(predicate, record, diagnostics))
            {
                dropped++;
                if (record.RequestResponseId is { Length: > 0 } id)
                    droppedPairs.Add(id);
                continue;
            }

            kept.Add(record);
        }

        if (droppedPairs.Count > 0)
        {
            var survivors = new List<InteractionRecord>(kept.Count);
            foreach (var record in kept)
            {
                if (record.RequestResponseId is { Length: > 0 } id && droppedPairs.Contains(id))
                {
                    dropped++;
                    continue;
                }

                survivors.Add(record);
            }

            kept = survivors;
        }

        if (dropped > 0)
            diagnostics.Add(DiagnosticKind.DroppedUnattributed, $"{dropped} unattributed interaction record(s) dropped by DropUnattributed.");

        return kept;
    }

    /// <summary>A host predicate must never break an ingest: a throwing predicate keeps the record and is reported.</summary>
    private static bool Evaluate(Func<InteractionRecord, bool> predicate, InteractionRecord record, ReportDiagnosticsCollector diagnostics)
    {
        try
        {
            return predicate(record);
        }
        catch (Exception ex)
        {
            diagnostics.Add(DiagnosticKind.Other, $"DropUnattributed threw for {record.ServiceName} {record.Uri}: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Turns the tests file's step and assertion events into diagram markers — top-level step delimiter
    /// bars and ✓/✗ assertion notes — placed by timestamp among the interactions (and nested inside
    /// whatever call was in flight). Background steps draw nothing: they belong to the step list only.
    /// Nor do the events of a scenario whose capture already carries its step bars as raw
    /// <c>marker</c> records (a store projected from an in-process run): those draw, the tests file fills
    /// the step list, or the diagram grows two sets of bars. The same rule the Cucumber path applies to the
    /// reporter's own step events for the scenarios the messages own.
    /// </summary>
    private static void AddDiagramMarkers(List<InteractionRecord> records, List<TestRunRecord> testRecords)
    {
        // The kind as the replay will read it: a comparison of its own would miss a kind the replay draws
        // as a Step bar (one written by number), and the scenario would get both sets of bars.
        var drawnByCapture = new HashSet<string>(
            records
                .Where(r => r.IsRawMarker && r.ResolvedMarkerKind == DiagramMarkerKind.Step)
                .Select(r => r.TestId),
            StringComparer.Ordinal);

        foreach (var record in testRecords)
        {
            if (!record.IsDiagramMarker || string.IsNullOrWhiteSpace(record.TestId) || drawnByCapture.Contains(record.TestId))
                continue;
            records.Add(record.Is(TestRunRecord.Events.Assertion)
                ? InteractionRecord.AssertionMarker(record.TestId, record.Text ?? "assertion",
                    passed: !string.Equals(record.Status, "failed", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(record.Status, "fail", StringComparison.OrdinalIgnoreCase),
                    record.Timestamp!.Value, record.Error)
                : InteractionRecord.StepMarker(record.TestId, record.Text ?? "step", record.Timestamp!.Value, record.Keyword,
                    table: record.Table, docString: record.DocString));
        }
    }

    /// <summary>
    /// Gives each test the Setup/Action boundary an in-process run would have drawn, so
    /// <see cref="ReportConfigurationOptions.SeparateSetup"/> partitions an ingested diagram (the drawing needs the
    /// boundary, a <see cref="DiagramMarkerKind.Phase"/> marker; a call's phase alone draws nothing). Called only when
    /// that option is on: a marker also ends a run of identical calls the collapser would fold, so every ingest without
    /// it stays as it was.
    /// </summary>
    /// <remarks>
    /// <para>The boundary is the record <see cref="InteractionRecord.FromLog"/> writes for <c>StartAction()</c>. It goes
    /// at the start of the test's first Action step that follows a Setup step (<see cref="IngestAttribution.BuildStepWindows"/>),
    /// placed ahead of every record at that instant; when the steps place none, just before the test's first request
    /// phased Action that follows one phased Setup (phases the capturer wrote, or <see cref="IngestRequest.PhaseFromSteps"/>
    /// set). As in-process, only after something happened in setup: from the steps, a boundary with no call of the test
    /// before it is not added.</para>
    /// <para>A test whose capture carries its own boundary keeps it, and so does one whose capture draws its own step
    /// bars: that is a store projected from an in-process run, which drew whatever boundary it had.</para>
    /// </remarks>
    private static void AddSetupBoundaries(List<InteractionRecord> records, List<TestRunRecord> testRecords)
    {
        // The kind as the replay reads it, as AddDiagramMarkers reads it.
        var captureOwns = new HashSet<string>(
            records
                .Where(r => r.IsRawMarker && r.ResolvedMarkerKind is DiagramMarkerKind.Phase or DiagramMarkerKind.Step)
                .Select(r => r.TestId),
            StringComparer.Ordinal);

        // Per test, the start of its first Action step that follows a Setup step.
        var fromSteps = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        var afterSetup = new HashSet<string>(StringComparer.Ordinal);
        foreach (var window in IngestAttribution.BuildStepWindows(testRecords))
        {
            if (fromSteps.ContainsKey(window.TestId))
                continue;
            if (window.Phase == TestPhase.Setup)
                afterSetup.Add(window.TestId);
            else if (window.Phase == TestPhase.Action && afterSetup.Contains(window.TestId))
                fromSteps[window.TestId] = window.Start;
        }

        // Where each boundary goes in the list: the sort that follows is stable, so a record sorts ahead of any other
        // at its instant that comes after it in the list.
        var boundaries = new List<(int Index, InteractionRecord Boundary)>();
        var calls = records
            .Select((record, index) => (Record: record, Index: index))
            .Where(x => !x.Record.IsMarker && !string.IsNullOrWhiteSpace(x.Record.TestId))
            .GroupBy(x => x.Record.TestId, StringComparer.Ordinal);
        foreach (var test in calls)
        {
            if (captureOwns.Contains(test.Key))
                continue;

            if (fromSteps.TryGetValue(test.Key, out var at))
            {
                if (test.Any(x => (x.Record.Timestamp ?? DateTimeOffset.MinValue) < at))
                    boundaries.Add((0, SetupBoundary(test.Key, at)));
                continue;
            }

            var setupSeen = false;
            foreach (var (record, index) in test.OrderBy(x => x.Record.Timestamp ?? DateTimeOffset.MinValue).ThenBy(x => x.Index))
            {
                if (string.Equals(record.Type, "Response", StringComparison.OrdinalIgnoreCase))
                    continue;
                var phase = record.ResolvedPhase;
                if (phase == TestPhase.Setup)
                    setupSeen = true;
                else if (phase == TestPhase.Action && setupSeen)
                {
                    boundaries.Add((index, SetupBoundary(test.Key, record.Timestamp)));
                    break;
                }
            }
        }

        // Latest place first, so each insertion leaves the places still to come where they were.
        foreach (var (index, boundary) in boundaries.OrderByDescending(b => b.Index))
            records.Insert(index, boundary);
    }

    /// <summary>The Setup/Action boundary as <see cref="InteractionRecord.FromLog"/> writes the one <c>StartAction()</c> logs.</summary>
    private static InteractionRecord SetupBoundary(string testId, DateTimeOffset? at) => new()
    {
        Kind = InteractionRecord.Kinds.Marker,
        MarkerKind = nameof(DiagramMarkerKind.Phase),
        Type = "Request",
        Uri = "http://override.com/",
        ServiceName = "",
        CallerName = "",
        TestId = testId,
        Timestamp = at,
    };

    /// <summary>
    /// Stable sort by timestamp: entries without a timestamp keep their file order relative to each
    /// other and sort first. The merger breaks ties by index, so what it is given is sorted this way too.
    /// </summary>
    private static List<InteractionRecord> ByTimestamp(List<InteractionRecord> records) =>
        records
            .Select((record, index) => (record, index))
            .OrderBy(x => x.record.Timestamp ?? DateTimeOffset.MinValue)
            .ThenBy(x => x.index)
            .Select(x => x.record)
            .ToList();

    /// <summary>Sorts by timestamp, folds unknown tests, then (optionally) rewrites the order as a call tree.</summary>
    private static List<InteractionRecord> Order(List<InteractionRecord> records, List<TestRunRecord> testRecords, IngestRequest request)
    {
        var ordered = ByTimestamp(records);

        if (request.FoldUnknownTestsInto is { } fold)
        {
            var known = new HashSet<string>(
                testRecords.Where(t => !string.IsNullOrWhiteSpace(t.TestId)).Select(t => t.TestId!),
                StringComparer.Ordinal);
            ordered = ordered
                .Select(r => known.Contains(r.TestId) || r.TestId == fold.ScenarioId
                    ? r
                    : r with { TestId = fold.ScenarioId, TestName = fold.ScenarioName })
                .ToList();
        }

        return request.CallTreeOrdering ? OrderAsCallTree(ordered) : ordered;
    }

    /// <summary>Replays the ordered records into the store, applying the tests file's names to every hop of one test.</summary>
    private static List<RequestResponseLog> Replay(List<InteractionRecord> ordered, List<TestRunRecord> testRecords)
    {
        // Names from the tests file win over whatever each hop knew.
        var namesFromTests = testRecords
            .Where(t => !string.IsNullOrWhiteSpace(t.TestId) && !string.IsNullOrWhiteSpace(t.TestName))
            .GroupBy(t => t.TestId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last().TestName!, StringComparer.Ordinal);

        // First non-empty name per test id among the records themselves, so every hop of one test
        // carries the same label even without a tests file. Any other record's name before a raw marker's:
        // the store names its markers by the test id (DefaultTrackingDiagramOverride), and a run's first
        // record is usually one, so a projected store came back with every scenario named after its id.
        var namesFromRecords = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var record in ordered.Where(r => !r.IsRawMarker).Concat(ordered.Where(r => r.IsRawMarker)))
        {
            if (!string.IsNullOrWhiteSpace(record.TestName) && record.TestName != TestIdentityScope.UnknownTestName
                && !namesFromRecords.ContainsKey(record.TestId))
                namesFromRecords[record.TestId] = record.TestName;
        }

        var logs = new List<RequestResponseLog>(ordered.Count);
        foreach (var record in ordered)
        {
            var name = namesFromTests.TryGetValue(record.TestId, out var fromTests) ? fromTests
                : namesFromRecords.TryGetValue(record.TestId, out var fromRecords) ? fromRecords
                : null;
            foreach (var log in record.ToLogs(name))
            {
                RequestResponseLogger.Log(log);
                logs.Add(log);
            }
        }

        return logs;
    }

    /// <summary>
    /// Empties the report's <c>attachments/</c> folder so it holds exactly this run's artefacts.
    /// A file another process still holds open is skipped and reported, never thrown.
    /// </summary>
    private static void CleanAttachmentsFolder(string reportsDirectory, ReportDiagnosticsCollector diagnostics)
    {
        var attachmentsDir = Path.Combine(reportsDirectory, "attachments");
        if (!Directory.Exists(attachmentsDir))
            return;

        foreach (var file in Directory.EnumerateFiles(attachmentsDir))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(DiagnosticKind.AttachmentFailure, $"Could not delete stale attachment {file}: {ex.Message}");
            }
        }
    }

    /// <summary>Above this many units in one test, nesting is skipped (pairs only) to keep ingest linear.</summary>
    private const int NestingLimit = 5000;

    /// <summary>
    /// Orders a timestamp-sorted list as a call tree. A unit is a request with its response (matched by
    /// <c>requestResponseId</c>) or a lone record. Each response sits directly after its request; a call a
    /// service made while handling a request — its interval lies inside the parent's and its caller is the
    /// parent's service — sits between that request and its response; siblings keep request-time order.
    /// Responses with no earlier request and records without an id keep their place. Records are grouped
    /// by <c>testId</c> first, so nesting never crosses tests.
    /// </summary>
    internal static List<InteractionRecord> OrderAsCallTree(IReadOnlyList<InteractionRecord> ordered)
    {
        var open = new Dictionary<string, CallUnit>(StringComparer.Ordinal);
        var groups = new Dictionary<string, List<CallUnit>>(StringComparer.Ordinal);
        var groupOrder = new List<string>();
        foreach (var record in ordered)
        {
            var isResponse = string.Equals(record.Type, "Response", StringComparison.OrdinalIgnoreCase);
            if (isResponse && record.RequestResponseId is { Length: > 0 } id && open.Remove(id, out var unit))
            {
                unit.Response = record;
                continue;
            }

            var fresh = new CallUnit(record, isRequest: !isResponse);
            if (!isResponse && record.RequestResponseId is { Length: > 0 } requestId)
                open[requestId] = fresh;
            if (!groups.TryGetValue(record.TestId, out var group))
            {
                group = [];
                groups[record.TestId] = group;
                groupOrder.Add(record.TestId);
            }

            group.Add(fresh);
        }

        var result = new List<InteractionRecord>(ordered.Count);
        foreach (var testId in groupOrder)
        {
            var group = groups[testId]; // in start order: the input is timestamp-sorted
            if (group.Count <= NestingLimit)
            {
                for (var i = 0; i < group.Count; i++)
                {
                    var unit = group[i];
                    if (unit.Start is null)
                        continue;
                    // Parent: the latest-started earlier request whose interval contains this one and
                    // whose service is this unit's caller — the innermost call that can have caused it.
                    // Markers (step bars, assertion notes) are test-level: they nest under the user
                    // action in flight at their timestamp (never inside a backend call, which would
                    // bury a "Then …" bar in the middle of a query), else they stay top level, placed by
                    // time among the roots. Markers themselves never parent anything.
                    for (var j = i - 1; j >= 0; j--)
                    {
                        var candidate = group[j];
                        if (!candidate.IsRequest || candidate.IsMarker || candidate.Start is null || candidate.End is null)
                            continue;
                        if (unit.IsMarker && !candidate.Request.IsUserAction)
                            continue;
                        if (candidate.Start <= unit.Start && candidate.End >= unit.End
                            && (unit.IsMarker || string.Equals(candidate.Request.ServiceName, unit.Request.CallerName, StringComparison.Ordinal)))
                        {
                            unit.Parent = candidate;
                            candidate.Children.Add(unit);
                            break;
                        }
                    }
                }
            }

            foreach (var root in group)
            {
                if (root.Parent is null)
                    Emit(root, result);
            }
        }

        return result;

        static void Emit(CallUnit unit, List<InteractionRecord> into)
        {
            into.Add(unit.Request);
            foreach (var child in unit.Children)
                Emit(child, into);
            if (unit.Response is not null)
                into.Add(unit.Response);
        }
    }

    private sealed class CallUnit(InteractionRecord request, bool isRequest)
    {
        public InteractionRecord Request { get; } = request;
        public bool IsRequest { get; } = isRequest;
        public InteractionRecord? Response { get; set; }
        public bool IsMarker => Request.IsMarker;
        public DateTimeOffset? Start => Request.Timestamp;
        // A lone record with a duration (a user action) owns that interval; a paired call ends at its
        // response; anything else is instantaneous.
        public DateTimeOffset? End => Response?.Timestamp
            ?? (Request.DurationMs is { } ms && Request.Timestamp is { } start ? start.AddMilliseconds(ms) : Request.Timestamp);
        public CallUnit? Parent { get; set; }
        public List<CallUnit> Children { get; } = [];
    }
}
