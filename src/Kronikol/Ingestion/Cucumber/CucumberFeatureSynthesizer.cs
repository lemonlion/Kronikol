using System.Text;
using Kronikol.Reports;

namespace Kronikol.Ingestion.Cucumber;

/// <summary>Knobs for <see cref="CucumberFeatureSynthesizer.Build(CucumberMessages, CucumberSynthesisOptions?)"/>.</summary>
public sealed class CucumberSynthesisOptions
{
    /// <summary>
    /// Keep hook steps (<c>BeforeEach hook</c>, <c>AfterEach hook</c>, …) as sub-steps of the scenario.
    /// Default <c>false</c>: hooks are plumbing, and a living document reads better without them. Their
    /// attachments are kept either way — they are attributed to the scenario instead of to a hook step.
    /// </summary>
    public bool IncludeHooks { get; init; }

    /// <summary>
    /// Where inline attachment bodies are written before the report generator copies them into
    /// <c>&lt;reports&gt;/attachments/</c>. Defaults to a fresh folder under the temp directory: the
    /// report's own attachment copier (<see cref="ReportGenerator.CopyAttachmentsToReportsFolder"/>) is
    /// what moves them next to the HTML, so nothing here needs to know the reports directory.
    /// </summary>
    public string? AttachmentsDirectory { get; init; }

    /// <summary>Feature name for pickles whose Gherkin document could not be found.</summary>
    public string DefaultFeatureName { get; init; } = "Ingested";

    /// <summary>
    /// Name of the attachment carrying the Kronikol test id (32 hex characters), written by the test
    /// fixture so that captured interactions, UI actions and assertions all join this scenario (§4.4).
    /// </summary>
    public string TestIdAttachmentName { get; init; } = "kronikol-test-id";

    /// <summary>Write plain-text attachment bodies out as files too. Default <c>false</c> — text attachments are usually diagnostics noise.</summary>
    public bool WriteTextAttachments { get; init; }
}

/// <summary>One executed Gherkin step and the wall-clock window it occupied.</summary>
/// <param name="Step">The step as it appears in the report (mutable — the merger grafts assertions onto it).</param>
/// <param name="Start">When the step started (where its <c>&lt;&lt;stepDelimiter&gt;&gt;</c> bar is drawn).</param>
/// <param name="End">Where the next step's bar is drawn, or the scenario end — a step owns the diagram up to the next boundary.</param>
/// <param name="KeywordType">
/// The step's resolved phase from <see cref="CucumberPickleStep.Type"/>: <c>Context</c>, <c>Action</c>,
/// <c>Outcome</c> or <c>Unknown</c> — an <c>And</c> carries the phase of the step it continues. The step's marker
/// carries the same value as its <see cref="TestRunRecord.KeywordType"/>, which phase attribution and the Setup/Action
/// boundary read, so a step in any Gherkin language gets its phase; <c>null</c> for a hook step.
/// </param>
public sealed record CucumberStepWindow(ScenarioStep Step, DateTimeOffset Start, DateTimeOffset End, string? KeywordType = null);

/// <summary>Outcome of <see cref="CucumberFeatureSynthesizer.Build(CucumberMessages, CucumberSynthesisOptions?)"/>.</summary>
/// <param name="Features">The Gherkin structure as Kronikol's report model.</param>
/// <param name="Start">Run start (UTC).</param>
/// <param name="End">Run end (UTC).</param>
/// <param name="Markers">
/// <c>start</c>/<c>step</c>/<c>end</c> records equivalent to a tests NDJSON, so the messages travel the
/// same path as any other external capture: step delimiter bars, names and outcomes all come out of
/// <see cref="IngestPipeline"/>'s existing machinery instead of a second implementation.
/// </param>
/// <param name="TestNames">Scenario id → display name, for every scenario the messages own.</param>
/// <param name="StepWindows">Scenario id → its executed steps with their time windows (used to nest assertions).</param>
/// <param name="JoinedTestIds">The scenario ids that came from a <c>kronikol-test-id</c> attachment — the ones interactions can join.</param>
/// <param name="Warnings">Diagnostics: unreadable lines, scenarios that cannot join, unknown envelopes.</param>
public sealed record CucumberSynthesisResult(
    Feature[] Features,
    DateTime Start,
    DateTime End,
    IReadOnlyList<TestRunRecord> Markers,
    IReadOnlyDictionary<string, string> TestNames,
    IReadOnlyDictionary<string, IReadOnlyList<CucumberStepWindow>> StepWindows,
    IReadOnlySet<string> JoinedTestIds,
    IReadOnlyList<string> Warnings)
{
    /// <summary>Every scenario id the messages own — what the merger replaces in the tests-file model.</summary>
    public IEnumerable<string> ScenarioIds => TestNames.Keys;

    /// <summary>
    /// The <c>kronikol-test-id</c> of each earlier attempt of a retried scenario that carried an id of its own, not its
    /// scenario's: a fixture that mints an id per attempt. The pipeline leaves out every record and call with one, as it
    /// leaves out an earlier attempt that shares its scenario's id, so a test that passed on its retry is one scenario.
    /// Empty when every attempt carried its scenario's id, as the Kronikol fixture's do.
    /// </summary>
    public IReadOnlySet<string> EarlierAttemptTestIds { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// The tests file's bypassed step records of scenarios these messages own that matched no Gherkin step by text, so
    /// their bypass was not applied. The pipeline reports the count as a diagnostic.
    /// </summary>
    internal int UnmatchedTestsFileBypasses { get; init; }

    /// <summary>
    /// What the producing run executed on, as the messages' own <c>meta</c> envelope reports it, or
    /// <see cref="RunEnvironment.Unrecorded"/> when it does not report it.
    /// </summary>
    /// <remarks>
    /// The one place in the ingest lane that can answer the question honestly. Every other source
    /// Kronikol ingests says nothing about its environment, and the reading process's own is not an
    /// answer — a playwright-bdd run is node.js, and `kronikol ingest` is .NET 10.
    /// </remarks>
    public RunEnvironment Environment { get; init; } = RunEnvironment.Unrecorded;
}

/// <summary>
/// Turns Cucumber Messages envelopes into Kronikol's <see cref="Feature"/> model: features with their
/// description and tags, rules, background steps, scenario outlines with their example values, steps
/// with keywords, data tables, doc strings, outcomes, exceptions and attachments.
/// </summary>
/// <remarks>
/// <para>
/// The target producer is <c>playwright-bdd</c> 9.x (<c>cucumberReporter('message')</c>); every other
/// producer of the protocol — cucumber-js, Cucumber-JVM — works for free, because the mapping is written
/// against the protocol and not against any runner.
/// </para>
/// <para>
/// Retries: each <c>TestCaseStarted</c> is one attempt. The last attempt is the verdict and the only one
/// that contributes steps and diagram markers; earlier attempts leave a <c>retry N</c> label on the
/// scenario so a flaky test stays visible.
/// </para>
/// <para>
/// Bypasses: a Gherkin step reported <c>SKIPPED</c> while a later Gherkin step of the same attempt finished
/// <c>PASSED</c> or <c>FAILED</c> was skipped over, not skipped with the rest, and is
/// <see cref="ExecutionResult.Bypassed"/>, its message the step's <see cref="ScenarioStep.BypassReason"/>. Hooks and
/// steps that never ran (<c>UNDEFINED</c>, <c>AMBIGUOUS</c>, <c>PENDING</c>) do not count as a later step that ran. A
/// skipped last step stays <c>Skipped</c>: nothing in the messages tells it from one that skipped the rest. A scenario's
/// result is its worst step's, Failed over Skipped over Bypassed over Passed.
/// </para>
/// </remarks>
public static class CucumberFeatureSynthesizer
{
    private const string CategoryTagPrefix = "category:";
    private const string EndpointTagPrefix = "endpoint:";

    /// <summary>Reads the files and synthesises in one step.</summary>
    public static CucumberSynthesisResult BuildFromFiles(IEnumerable<string> paths, CucumberSynthesisOptions? options = null) =>
        Build(CucumberMessagesReader.ReadFiles(paths), options);

    /// <summary>
    /// As <see cref="BuildFromFiles(IEnumerable{string}, CucumberSynthesisOptions?)"/>, with a tests file's bypassed step
    /// records to apply to the Gherkin steps they report on, which the pipeline drops with the reporter's other step
    /// records of a scenario the messages own.
    /// </summary>
    internal static CucumberSynthesisResult BuildFromFiles(
        IEnumerable<string> paths, CucumberSynthesisOptions? options, IReadOnlyList<CucumberTestsFileBypass> testsFileBypasses) =>
        Build(CucumberMessagesReader.ReadFiles(paths), options, testsFileBypasses);

    /// <summary>Synthesises the report model from already-read messages.</summary>
    public static CucumberSynthesisResult Build(CucumberMessages messages, CucumberSynthesisOptions? options = null) =>
        Build(messages, options, []);

    /// <summary>As <see cref="Build(CucumberMessages, CucumberSynthesisOptions?)"/>, with a tests file's bypassed step records.</summary>
    internal static CucumberSynthesisResult Build(
        CucumberMessages messages, CucumberSynthesisOptions? options, IReadOnlyList<CucumberTestsFileBypass> testsFileBypasses)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(testsFileBypasses);
        options ??= new CucumberSynthesisOptions();

        var warnings = new List<string>(messages.Warnings);
        if (messages.MalformedLines > 0)
            warnings.Add($"Cucumber messages: {messages.MalformedLines} malformed line(s) skipped.");
        // Not the envelopes every producer writes and no report shows (the feature text, step definitions): only a type
        // whose content could have mattered, since the warnings reach the run's diagnostics.
        if (messages.UnreadEnvelopeTypes.Count > 0)
            warnings.Add($"Cucumber messages: envelopes of a type Kronikol does not read were ignored: {string.Join(", ", messages.UnreadEnvelopeTypes)}.");

        var gherkin = GherkinIndex.Build(messages);
        var pickles = ToDictionary(messages.Pickles, p => p.Id);
        var hooks = ToDictionary(messages.Hooks, h => h.Id);
        var testCases = ToDictionary(messages.TestCases, t => t.Id);

        var stepStarts = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        foreach (var started in messages.TestStepStarted)
        {
            if (Key(started.TestCaseStartedId, started.TestStepId) is { } key && started.Timestamp is { } ts)
                stepStarts[key] = ts.ToInstant();
        }

        var stepFinishes = new Dictionary<string, CucumberTestStepFinished>(StringComparer.Ordinal);
        foreach (var finished in messages.TestStepFinished)
        {
            if (Key(finished.TestCaseStartedId, finished.TestStepId) is { } key)
                stepFinishes[key] = finished;
        }

        var caseFinishes = new Dictionary<string, CucumberTestCaseFinished>(StringComparer.Ordinal);
        foreach (var finished in messages.TestCaseFinished)
        {
            if (finished.TestCaseStartedId is { Length: > 0 } id)
                caseFinishes[id] = finished;
        }

        var attachmentsByAttempt = new Dictionary<string, List<CucumberAttachment>>(StringComparer.Ordinal);
        foreach (var attachment in messages.Attachments)
        {
            if (attachment.TestCaseStartedId is not { Length: > 0 } id)
                continue;
            if (!attachmentsByAttempt.TryGetValue(id, out var list))
                attachmentsByAttempt[id] = list = [];
            list.Add(attachment);
        }

        // Attempts grouped by test case, in start order: the last attempt is the verdict.
        var attemptsByCase = new Dictionary<string, List<CucumberTestCaseStarted>>(StringComparer.Ordinal);
        var caseOrder = new List<string>();
        foreach (var attempt in messages.TestCaseStarted.OrderBy(a => a.Attempt))
        {
            if (attempt.TestCaseId is not { Length: > 0 } id)
                continue;
            if (!attemptsByCase.TryGetValue(id, out var list))
            {
                attemptsByCase[id] = list = [];
                caseOrder.Add(id);
            }

            list.Add(attempt);
        }

        var attachmentWriter = new AttachmentWriter(options, warnings);
        var featureGroups = new Dictionary<string, FeatureAccumulator>(StringComparer.Ordinal);
        var featureOrder = new List<string>();
        var markers = new List<TestRunRecord>();
        var testNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var stepWindows = new Dictionary<string, IReadOnlyList<CucumberStepWindow>>(StringComparer.Ordinal);
        var joined = new HashSet<string>(StringComparer.Ordinal);
        var unjoined = new List<string>();
        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        var earlierIds = new HashSet<string>(StringComparer.Ordinal);
        var bypassesByTest = testsFileBypasses.GroupBy(b => b.TestId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<CucumberTestsFileBypass>)g.ToList(), StringComparer.Ordinal);
        var unmatchedBypasses = 0;
        DateTimeOffset? runStart = messages.TestRunStarted?.Timestamp?.ToInstant();
        DateTimeOffset? runEnd = messages.TestRunFinished?.Timestamp?.ToInstant();

        foreach (var testCaseId in caseOrder)
        {
            if (!testCases.TryGetValue(testCaseId, out var testCase))
            {
                warnings.Add($"Cucumber messages: no testCase envelope for id '{testCaseId}' — its attempts were skipped.");
                continue;
            }

            if (testCase.PickleId is not { Length: > 0 } pickleId || !pickles.TryGetValue(pickleId, out var pickle))
            {
                warnings.Add($"Cucumber messages: no pickle for testCase '{testCaseId}' — skipped.");
                continue;
            }

            var attempts = attemptsByCase[testCaseId];
            var winner = attempts[^1];
            var scenarioId = ResolveScenarioId(pickle, winner, attachmentsByAttempt, options, usedIds, joined, unjoined, warnings);

            var node = gherkin.FindScenario(pickle);
            var featureName = node?.Feature?.Name is { Length: > 0 } fn ? fn : options.DefaultFeatureName;
            var bypasses = bypassesByTest.GetValueOrDefault(scenarioId) ?? [];
            var built = BuildScenario(scenarioId, pickle, node, testCase, winner, attempts, gherkin, hooks,
                stepStarts, stepFinishes, caseFinishes, attachmentsByAttempt, attachmentWriter, options, bypasses, warnings);
            unmatchedBypasses += bypasses.Count - built.MatchedBypasses;

            if (!featureGroups.TryGetValue(featureName, out var group))
            {
                // The accumulator is created once per feature NAME, so where two files share a
                // `Feature:` title the first path seen wins - the same first-wins rule the display-name
                // grouping already imposes on description, endpoint and tags.
                featureGroups[featureName] = group = new FeatureAccumulator(featureName, node?.Feature, NormalisePath(node?.Uri));
                featureOrder.Add(featureName);
            }

            group.Scenarios.Add(built.Scenario);
            group.Endpoint ??= built.Endpoint;

            testNames[scenarioId] = built.Scenario.DisplayName;
            stepWindows[scenarioId] = built.Windows;
            // Each earlier attempt that carried this scenario's id opens an attempt of its own, so the pipeline leaves
            // out that attempt's calls (they carry the same id); one that minted another id is left out by that id.
            foreach (var earlier in attempts.Take(attempts.Count - 1))
            {
                var earlierId = TestIdOf(earlier, attachmentsByAttempt, options);
                if (earlierId is not null && earlierId != scenarioId)
                    earlierIds.Add(earlierId);
                else if (earlierId is not null && earlier.Timestamp?.ToInstant() is { } earlierStart)
                    markers.Add(new TestRunRecord
                    {
                        Event = "start",
                        TestId = scenarioId,
                        TestName = built.Scenario.DisplayName,
                        Feature = featureName,
                        Timestamp = earlierStart,
                        Attempt = earlier.Attempt + 1,
                    });
            }

            markers.AddRange(built.Markers);

            if (built.Start is { } s)
                runStart = runStart is null || s < runStart ? s : runStart;
            if (built.End is { } e)
                runEnd = runEnd is null || e > runEnd ? e : runEnd;
        }

        // One warning for every scenario that cannot join, not one each: a producer with no Kronikol fixture writes the
        // attachment for none of them, and the warnings reach the run's diagnostics.
        if (unjoined.Count > 0)
        {
            const int named = 5;
            var names = string.Join(", ", unjoined.Take(named).Select(n => $"'{n}'"))
                        + (unjoined.Count > named ? $" and {unjoined.Count - named} more" : "");
            warnings.Add(
                $"Cucumber messages: {unjoined.Count} scenario(s) have no '{options.TestIdAttachmentName}' attachment, so " +
                $"captured interactions cannot be joined to them: {names}.");
        }

        var features = featureOrder.Select(name => featureGroups[name].ToFeature()).ToArray();
        var now = DateTimeOffset.UtcNow;
        return new CucumberSynthesisResult(
            features,
            (runStart ?? now).UtcDateTime,
            (runEnd ?? runStart ?? now).UtcDateTime,
            markers,
            testNames,
            stepWindows,
            joined,
            warnings)
        {
            Environment = messages.Meta?.ToRunEnvironment() ?? RunEnvironment.Unrecorded,
            EarlierAttemptTestIds = earlierIds,
            UnmatchedTestsFileBypasses = unmatchedBypasses,
        };
    }

    private sealed record BuiltScenario(
        Scenario Scenario,
        IReadOnlyList<CucumberStepWindow> Windows,
        IReadOnlyList<TestRunRecord> Markers,
        DateTimeOffset? Start,
        DateTimeOffset? End,
        string? Endpoint,
        int MatchedBypasses);

    private static BuiltScenario BuildScenario(
        string scenarioId,
        CucumberPickle pickle,
        GherkinScenario? node,
        CucumberTestCase testCase,
        CucumberTestCaseStarted winner,
        List<CucumberTestCaseStarted> attempts,
        GherkinIndex gherkin,
        Dictionary<string, CucumberHook> hooks,
        Dictionary<string, DateTimeOffset> stepStarts,
        Dictionary<string, CucumberTestStepFinished> stepFinishes,
        Dictionary<string, CucumberTestCaseFinished> caseFinishes,
        Dictionary<string, List<CucumberAttachment>> attachmentsByAttempt,
        AttachmentWriter attachmentWriter,
        CucumberSynthesisOptions options,
        IReadOnlyList<CucumberTestsFileBypass> bypasses,
        List<string> warnings)
    {
        var attemptId = winner.Id ?? "";
        var pickleSteps = ToDictionary(pickle.Steps ?? [], s => s.Id);
        var exampleRow = FindExampleRow(pickle, gherkin);
        var plan = testCase.TestSteps ?? [];

        // The last Gherkin step of this attempt that ran its body. A step reported SKIPPED before it was skipped over
        // while the steps after it ran: Kronikol's own meaning of a bypass, and no Cucumber runner measured runs a step
        // after skipping one for any other reason. Hooks run after skipped steps, and an UNDEFINED, AMBIGUOUS or PENDING
        // step never ran its body, so none of them counts.
        var lastRan = -1;
        for (var i = 0; i < plan.Length; i++)
        {
            if (plan[i].Id is { Length: > 0 } id && plan[i].HookId is not { Length: > 0 }
                && plan[i].PickleStepId is { Length: > 0 } psid && pickleSteps.ContainsKey(psid)
                && RawStatus(stepFinishes.GetValueOrDefault(Key(attemptId, id)!)?.TestStepResult) is "PASSED" or "FAILED")
                lastRan = i;
        }

        // A tests-file bypass finds its step by text: the k-th record with a text is the k-th Gherkin step with it.
        var bypassByText = new Dictionary<(string Text, int Occurrence), CucumberTestsFileBypass>();
        foreach (var bypass in bypasses)
        {
            if (bypass.Occurrence > 0)
                bypassByText.TryAdd((bypass.Text, bypass.Occurrence), bypass);
        }

        var textOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var matchedBypasses = 0;

        var attachments = attachmentsByAttempt.TryGetValue(attemptId, out var list) ? list : [];
        var attachmentsByStep = new Dictionary<string, List<FileAttachment>>(StringComparer.Ordinal);
        var scenarioAttachments = new List<FileAttachment>();

        foreach (var attachment in attachments)
        {
            if (IsTestIdAttachment(attachment, options))
                continue;
            if (attachmentWriter.Materialise(attachment) is not { } file)
                continue;
            if (attachment.TestStepId is { Length: > 0 } stepId)
            {
                if (!attachmentsByStep.TryGetValue(stepId, out var forStep))
                    attachmentsByStep[stepId] = forStep = [];
                forStep.Add(file);
            }
            else
            {
                scenarioAttachments.Add(file);
            }
        }

        var consumedAttachments = new HashSet<string>(StringComparer.Ordinal);
        var backgroundSteps = new List<ScenarioStep>();
        var steps = new List<ScenarioStep>();
        var hookSteps = new List<ScenarioStep>();
        var windows = new List<CucumberStepWindow>();
        var markers = new List<TestRunRecord>();

        var featureName = node?.Feature?.Name is { Length: > 0 } fn ? fn : options.DefaultFeatureName;
        var displayName = pickle.Name is { Length: > 0 } pn ? pn : node?.Node.Name ?? scenarioId;

        DateTimeOffset? scenarioStart = winner.Timestamp?.ToInstant();
        DateTimeOffset? lastFinish = null;
        // Producers do not all stamp every step honestly — playwright-bdd 9.2 gives a step that was never
        // reached (SKIPPED after a failure) the test case's own start time, which would sort its delimiter
        // bar before the steps that actually ran. Step starts are therefore kept monotonic.
        var lastStart = DateTimeOffset.MinValue;
        string? errorMessage = null;
        string? errorStack = null;
        var worst = ExecutionResult.Passed;
        var sawStep = false;

        markers.Add(new TestRunRecord
        {
            Event = "start",
            TestId = scenarioId,
            TestName = displayName,
            Feature = featureName,
            Timestamp = scenarioStart,
            // 1-based, like the scenario's Attempt; an earlier attempt's start is written beside it by the caller.
            Attempt = winner.Attempt + 1,
        });

        for (var position = 0; position < plan.Length; position++)
        {
            var testStep = plan[position];
            if (testStep.Id is not { Length: > 0 } testStepId)
                continue;
            var key = Key(attemptId, testStepId)!;
            stepStarts.TryGetValue(key, out var started);
            if (started != default)
            {
                // A repaired stamp is nudged one tick past the previous step so the two never collide:
                // a step bar and everything that belongs to it must stay on the right side of the boundary.
                if (started < lastStart)
                    started = lastStart.AddTicks(1);
                lastStart = started;
            }

            stepFinishes.TryGetValue(key, out var finished);
            var result = finished?.TestStepResult;
            var status = MapStatus(result?.Status);
            var duration = result?.Duration?.ToDuration();
            var finishedAt = finished?.Timestamp?.ToInstant();
            if (finishedAt is { } raw && raw < started)
                finishedAt = started;
            if (finishedAt is { } f && (lastFinish is null || f > lastFinish))
                lastFinish = f;

            FileAttachment[]? stepAttachments = null;
            if (attachmentsByStep.TryGetValue(testStepId, out var att) && att.Count > 0)
            {
                stepAttachments = att.ToArray();
                consumedAttachments.Add(testStepId);
            }

            var (message, stackTrace) = ExtractError(result);

            if (testStep.HookId is { Length: > 0 } hookId)
            {
                var hook = hooks.GetValueOrDefault(hookId);
                if (!options.IncludeHooks)
                {
                    // Hooks are dropped, but what they attached is not: it belongs to the scenario.
                    if (stepAttachments is not null)
                        scenarioAttachments.AddRange(stepAttachments);
                    if (status == ExecutionResult.Failed)
                    {
                        errorMessage ??= message;
                        errorStack ??= stackTrace;
                        worst = ExecutionResult.Failed;
                    }

                    continue;
                }

                var hookStep = new ScenarioStep
                {
                    Keyword = null,
                    Text = HookName(hook, hookId),
                    Status = status,
                    Duration = duration,
                    Attachments = stepAttachments,
                    Comments = message is null ? null : [message],
                    FailureMessage = status == ExecutionResult.Failed ? message : null,
                };
                hookSteps.Add(hookStep);
                if (started != default)
                {
                    windows.Add(new CucumberStepWindow(hookStep, started, finishedAt ?? started));
                    markers.Add(StepMarkerRecord(scenarioId, hookStep, started, duration, status, message));
                }

                if (status == ExecutionResult.Failed)
                {
                    errorMessage ??= message;
                    errorStack ??= stackTrace;
                }

                worst = Worse(worst, status);
                continue;
            }

            if (testStep.PickleStepId is not { Length: > 0 } pickleStepId
                || !pickleSteps.TryGetValue(pickleStepId, out var pickleStep))
                continue;

            sawStep = true;
            var gherkinStep = gherkin.FindStep(pickleStep);
            var keyword = NormaliseKeyword(gherkinStep?.Keyword);
            var isBackground = gherkinStep?.Id is { Length: > 0 } gid && gherkin.IsBackgroundStep(gid);
            var text = pickleStep.Text ?? gherkinStep?.Text ?? "(step)";

            var occurrence = textOccurrences[text.Trim()] = textOccurrences.GetValueOrDefault(text.Trim()) + 1;
            var fromTestsFile = bypassByText.GetValueOrDefault((text.Trim(), occurrence));
            if (fromTestsFile is not null)
                matchedBypasses++;
            var (stepStatus, bypassReason) = StepVerdict(status, RawStatus(result), position < lastRan, fromTestsFile, message);
            var bypassed = stepStatus == ExecutionResult.Bypassed;

            var step = new ScenarioStep
            {
                Keyword = keyword,
                Text = text,
                Status = stepStatus,
                BypassReason = bypassReason,
                Duration = duration,
                Attachments = stepAttachments,
                // A bypassed step's message is its reason and is said once, as the reason; one that says something else
                // (a tests file gave the reason) stays a comment.
                Comments = message is null || (bypassed && message == bypassReason) ? null : [message],
                // What Failures.md prints under a failing step; the comments are what the HTML renders.
                FailureMessage = stepStatus == ExecutionResult.Failed ? message : null,
                // The step contract: the feature file's name, and the step's own line in it (#76).
                SourceFile = SourcePaths.FileName(node?.Uri ?? pickle.Uri),
                SourceLine = gherkinStep?.Location?.Line is > 0 and var line ? line : null,
                // The pickle argument carries an outline row's placeholders already substituted — what
                // the step actually received; the authored Gherkin argument is the fallback for older
                // message streams that omit it.
                DocString = pickleStep.Argument?.DocString?.Content ?? gherkinStep?.DocString?.Content,
                DocStringMediaType = NullIfBlank(pickleStep.Argument?.DocString?.MediaType ?? gherkinStep?.DocString?.MediaType),
                Parameters = BuildTableParameter(pickleStep.Argument?.DataTable ?? gherkinStep?.DataTable),
                TextSegments = BuildTextSegments(gherkinStep?.Text, pickleStep.Text, exampleRow),
            };

            if (isBackground)
                backgroundSteps.Add(step);
            else
                steps.Add(step);

            if (started != default)
            {
                windows.Add(new CucumberStepWindow(step, started, finishedAt ?? started, NullIfBlank(pickleStep.Type)));
                // The pickle's resolved type, so a phase is found for a keyword in any language (Angenommen, Wenn). A
                // bypassed step's marker carries its reason as the tests file's own record does, and no error.
                markers.Add(StepMarkerRecord(scenarioId, step, started, duration, stepStatus, bypassed ? null : message,
                    NullIfBlank(pickleStep.Type)));
            }

            if (stepStatus == ExecutionResult.Failed)
            {
                errorMessage ??= message;
                errorStack ??= stackTrace;
            }

            worst = Worse(worst, stepStatus);
        }

        // Attachments the producer pinned to a step that is not in the plan (a run-level hook, a step the
        // runner added after the fact) still belong to this scenario rather than nowhere.
        foreach (var (stepId, orphans) in attachmentsByStep)
        {
            if (!consumedAttachments.Contains(stepId))
                scenarioAttachments.AddRange(orphans);
        }

        if (!sawStep)
            worst = ExecutionResult.Skipped;

        // playwright-bdd 9.2 stamps testCaseFinished before the last step reports back, so the later of
        // the two is the honest end of the scenario.
        var caseFinish = caseFinishes.GetValueOrDefault(attemptId)?.Timestamp?.ToInstant();
        var scenarioEnd = lastFinish is { } l && caseFinish is { } c ? (l > c ? l : c) : lastFinish ?? caseFinish;

        // A step owns the diagram from its own delimiter bar to the next one — not merely until its own
        // reported finish. Without that, an interaction made between two steps (and a step the producer
        // reported as instantaneous) would fall outside every window and no assertion could be placed.
        for (var i = 0; i < windows.Count; i++)
        {
            var boundary = i + 1 < windows.Count ? windows[i + 1].Start : scenarioEnd ?? windows[i].End;
            if (boundary > windows[i].End)
                windows[i] = windows[i] with { End = boundary };
        }

        var ownTags = node is null ? [] : node.OwnTagNames(exampleRow?.ExamplesTags);
        var pickleTags = (pickle.Tags ?? []).Select(t => Strip(t.Name)).Where(t => t is not null).Select(t => t!).ToArray();

        var labels = ownTags
            .Where(t => !HappyPathDetection.IsHappyPathTag(t)
                        && !t.StartsWith(EndpointTagPrefix, StringComparison.OrdinalIgnoreCase)
                        && !t.StartsWith(CategoryTagPrefix, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Earlier attempts are not shown as scenarios; a label keeps the flakiness visible.
        for (var i = 0; i < attempts.Count - 1; i++)
            labels.Add($"retry {attempts[i].Attempt + 1}");

        var categories = pickleTags
            .Where(t => t.StartsWith(CategoryTagPrefix, StringComparison.OrdinalIgnoreCase))
            .Select(t => t[CategoryTagPrefix.Length..])
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var endpoint = pickleTags
            .FirstOrDefault(t => t.StartsWith(EndpointTagPrefix, StringComparison.OrdinalIgnoreCase))
            ?[EndpointTagPrefix.Length..];

        var (exampleValues, exampleRaw) = BuildExampleValues(exampleRow);

        var scenario = new Scenario
        {
            Id = scenarioId,
            DisplayName = displayName,
            Description = NullIfBlank(Dedent(node?.Node.Description)),
            IsHappyPath = HappyPathDetection.AnyHappyPathTag(ownTags),
            Result = worst,
            ErrorMessage = errorMessage,
            ErrorStackTrace = errorStack,
            Duration = scenarioStart is { } ss && scenarioEnd is { } se && se >= ss ? se - ss : null,
            EndedAt = scenarioEnd,
            Steps = steps.Count > 0 || hookSteps.Count > 0 ? steps.Concat(hookSteps).ToArray() : null,
            BackgroundSteps = backgroundSteps.Count > 0 ? backgroundSteps.ToArray() : null,
            Attachments = scenarioAttachments.Count > 0 ? scenarioAttachments.ToArray() : null,
            Labels = labels.Count > 0 ? labels.ToArray() : null,
            Categories = categories.Length > 0 ? categories : null,
            Rule = NullIfBlank(node?.Rule),
            // 1-based, like the `retry N` label above; Cucumber's wire value counts from 0.
            Attempt = winner.Attempt + 1,
            // The pickle carries the uri too, and is the fallback when the gherkinDocument for this
            // scenario never arrived (a producer may emit pickles without documents).
            SourceFile = NormalisePath(node?.Uri ?? pickle.Uri),
            SourceLine = node?.Node.Location?.Line,
            OutlineId = exampleRow is null ? null : node?.Node.Name,
            ExampleValues = exampleValues,
            ExampleRawValues = exampleRaw,
            ExamplesBlockName = exampleRow?.BlockName,
            ExamplesBlockDescription = exampleRow is null ? null : NullIfBlank(Dedent(exampleRow.BlockDescription)),
            ExamplesBlockIndex = exampleRow?.BlockIndex,
        };

        markers.Add(new TestRunRecord
        {
            Event = "end",
            TestId = scenarioId,
            TestName = displayName,
            Feature = featureName,
            Timestamp = scenarioEnd,
            Status = worst.ToString().ToLowerInvariant(),
            DurationMs = scenario.Duration?.TotalMilliseconds,
            Error = errorMessage,
        });

        return new BuiltScenario(scenario, windows, markers, scenarioStart, scenarioEnd, endpoint, matchedBypasses);
    }

    /// <summary>
    /// A Gherkin step's verdict once the bypass signals are read (#105). A step that did not fail is
    /// <see cref="ExecutionResult.Bypassed"/> when the tests file says so, or when its producer reported it
    /// <c>SKIPPED</c> and a later Gherkin step of the same attempt ran. Its reason is the tests file's
    /// <c>bypassReason</c>, else the step's own message. A failed step stays failed whatever is said about it.
    /// </summary>
    private static (ExecutionResult Status, string? BypassReason) StepVerdict(
        ExecutionResult status, string? rawStatus, bool aLaterStepRan, CucumberTestsFileBypass? fromTestsFile, string? message)
    {
        if (status == ExecutionResult.Failed)
            return (status, null);
        if (fromTestsFile is not null)
            return (ExecutionResult.Bypassed, fromTestsFile.Reason ?? message);
        if (rawStatus == "SKIPPED" && aLaterStepRan)
            return (ExecutionResult.Bypassed, message);
        return (status, null);
    }

    /// <summary>The producer's status word as written, upper-cased; <see cref="MapStatus"/> folds it into a verdict.</summary>
    private static string? RawStatus(CucumberTestStepResult? result) => result?.Status?.Trim().ToUpperInvariant();

    private static TestRunRecord StepMarkerRecord(
        string scenarioId, ScenarioStep step, DateTimeOffset started, TimeSpan? duration,
        ExecutionResult status, string? error, string? keywordType = null) => new()
        {
            Event = "step",
            TestId = scenarioId,
            Text = step.Text,
            Keyword = step.Keyword,
            KeywordType = keywordType,
            Timestamp = started,
            DurationMs = duration?.TotalMilliseconds,
            Status = status.ToString().ToLowerInvariant(),
            Error = error,
            BypassReason = step.BypassReason,
            Level = 0,
            Table = MarkerTable(step),
            DocString = step.DocString,
        };

    /// <summary>
    /// The step's data table as the delimiter bar draws it — header row first — recovered from the
    /// tabular parameter <see cref="BuildTableParameter"/> built (so the bar shows exactly what the
    /// step list shows, substituted values included).
    /// </summary>
    private static string[][]? MarkerTable(ScenarioStep step)
    {
        var tabular = step.Parameters?.FirstOrDefault(p => p.Kind == StepParameterKind.Tabular)?.TabularValue;
        if (tabular is null)
            return null;

        return
        [
            tabular.Columns.Select(c => c.Name).ToArray(),
            .. tabular.Rows.Select(r => r.Values.Select(v => v.Value).ToArray()),
        ];
    }

    /// <summary>
    /// The scenario id interactions join on: the <c>kronikol-test-id</c> attachment when the fixture wrote
    /// one, otherwise a minted <c>&lt;pickleId&gt;#&lt;attempt&gt;</c>, its scenario named in
    /// <paramref name="unjoined"/> for the one warning that lists them — without the attachment nothing
    /// captured on the wire can be attributed to this scenario.
    /// </summary>
    private static string ResolveScenarioId(
        CucumberPickle pickle,
        CucumberTestCaseStarted winner,
        Dictionary<string, List<CucumberAttachment>> attachmentsByAttempt,
        CucumberSynthesisOptions options,
        HashSet<string> usedIds,
        HashSet<string> joined,
        List<string> unjoined,
        List<string> warnings)
    {
        string? fromAttachment = null;
        if (winner.Id is { Length: > 0 } attemptId && attachmentsByAttempt.TryGetValue(attemptId, out var list))
        {
            foreach (var attachment in list)
            {
                if (!IsTestIdAttachment(attachment, options))
                    continue;
                var value = DecodeText(attachment)?.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    fromAttachment = value;
                    break;
                }
            }
        }

        if (fromAttachment is not null)
        {
            if (usedIds.Add(fromAttachment))
            {
                joined.Add(fromAttachment);
                return fromAttachment;
            }

            warnings.Add(
                $"Cucumber messages: the '{options.TestIdAttachmentName}' attachment value '{fromAttachment}' is used by " +
                $"more than one scenario ('{pickle.Name}') — the duplicate falls back to a minted id and its interactions cannot join.");
        }
        else
        {
            unjoined.Add(pickle.Name ?? pickle.Id ?? "(unnamed)");
        }

        var minted = $"{pickle.Id}#{winner.Attempt}";
        usedIds.Add(minted);
        return minted;
    }

    /// <summary>The <c>kronikol-test-id</c> an attempt carried, or null.</summary>
    private static string? TestIdOf(CucumberTestCaseStarted attempt, Dictionary<string, List<CucumberAttachment>> attachmentsByAttempt,
        CucumberSynthesisOptions options)
    {
        if (attempt.Id is not { Length: > 0 } attemptId || !attachmentsByAttempt.TryGetValue(attemptId, out var list))
            return null;
        foreach (var attachment in list)
        {
            if (IsTestIdAttachment(attachment, options) && DecodeText(attachment)?.Trim() is { Length: > 0 } value)
                return value;
        }

        return null;
    }

    private static bool IsTestIdAttachment(CucumberAttachment attachment, CucumberSynthesisOptions options) =>
        string.Equals(attachment.FileName, options.TestIdAttachmentName, StringComparison.OrdinalIgnoreCase);

    private static string? DecodeText(CucumberAttachment attachment)
    {
        if (attachment.Body is not { Length: > 0 } body)
            return null;
        if (!string.Equals(attachment.ContentEncoding, "BASE64", StringComparison.OrdinalIgnoreCase))
            return body;
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(body));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Worst-wins: Failed beats Skipped beats Bypassed beats Passed, the order LightBDD's own status takes. A bypassed
    /// scenario ran, all but what it skipped over; a skipped one did not run the rest of its steps.
    /// </summary>
    private static ExecutionResult Worse(ExecutionResult current, ExecutionResult candidate) =>
        Rank(candidate) > Rank(current) ? candidate : current;

    private static int Rank(ExecutionResult result) => result switch
    {
        ExecutionResult.Failed => 3,
        ExecutionResult.Skipped or ExecutionResult.SkippedAfterFailure => 2,
        ExecutionResult.Bypassed => 1,
        _ => 0,
    };

    /// <summary>
    /// Maps a Cucumber <c>TestStepResultStatus</c> to Kronikol's verdict vocabulary. The word alone never says
    /// <see cref="ExecutionResult.Bypassed"/>: whether a <c>SKIPPED</c> step was bypassed is read from the steps after it
    /// (see the remarks on <see cref="CucumberFeatureSynthesizer"/>).
    /// </summary>
    public static ExecutionResult MapStatus(string? status) => status?.Trim().ToUpperInvariant() switch
    {
        "PASSED" => ExecutionResult.Passed,
        "SKIPPED" or "PENDING" => ExecutionResult.Skipped,
        "FAILED" or "UNDEFINED" or "AMBIGUOUS" => ExecutionResult.Failed,
        null or "" => ExecutionResult.Skipped,
        _ => ExecutionResult.Failed,
    };

    private static (string? Message, string? StackTrace) ExtractError(CucumberTestStepResult? result)
    {
        if (result is null)
            return (null, null);
        var message = NullIfBlank(result.Exception?.Message) ?? NullIfBlank(result.Message);
        var stack = NullIfBlank(result.Exception?.StackTrace) ?? NullIfBlank(result.Message);
        return (message, message is null ? null : stack);
    }

    private static string HookName(CucumberHook? hook, string hookId)
    {
        if (NullIfBlank(hook?.Name) is { } name)
            return name;
        return hook?.Type?.ToUpperInvariant() switch
        {
            "BEFORE_TEST_CASE" => "Before hook",
            "AFTER_TEST_CASE" => "After hook",
            "BEFORE_TEST_RUN" => "Before all hook",
            "AFTER_TEST_RUN" => "After all hook",
            "BEFORE_TEST_STEP" => "Before step hook",
            "AFTER_TEST_STEP" => "After step hook",
            _ => $"hook {hookId}",
        };
    }

    /// <summary>
    /// The literal Gherkin keyword without its trailing space: <c>Given</c>, <c>When</c>, <c>Then</c>,
    /// <c>And</c>, <c>But</c>. Gherkin authors write the And/But sequencing themselves, so the display
    /// keyword is taken as authored (the phase an <c>And</c> inherits lives in
    /// <see cref="CucumberPickleStep.Type"/>, which is where phase attribution reads it).
    /// </summary>
    private static string? NormaliseKeyword(string? keyword) => NullIfBlank(keyword?.Trim());

    private static StepParameter[]? BuildTableParameter(CucumberDataTable? table)
    {
        if (table?.Rows is not { Length: > 1 } rows)
            return null;

        var headers = (rows[0].Cells ?? []).Select(c => c.Value ?? "").ToArray();
        if (headers.Length == 0)
            return null;

        var columns = headers.Select(h => new TabularColumn(h, false)).ToArray();
        var body = new List<TabularRow>();
        for (var i = 1; i < rows.Length; i++)
        {
            var cells = (rows[i].Cells ?? []).Select(c => c.Value ?? "").ToArray();
            var values = new TabularCell[columns.Length];
            for (var j = 0; j < columns.Length; j++)
                values[j] = new TabularCell(j < cells.Length ? cells[j] : "", null, VerificationStatus.NotApplicable);
            body.Add(new TabularRow(TableRowType.Matching, values));
        }

        if (body.Count == 0)
            return null;

        return
        [
            new StepParameter
            {
                Name = "table",
                Kind = StepParameterKind.Tabular,
                TabularValue = new TabularParameterValue(columns, body.ToArray()),
            }
        ];
    }

    /// <summary>
    /// For a scenario outline row, splits the authored text (<c>a customer named "&lt;customer&gt;"</c>)
    /// into literal segments and highlighted parameter values, so the report renders the substituted
    /// value the way LightBDD and ReqNRoll do. Returns null for a plain scenario.
    /// </summary>
    private static StepTextSegment[]? BuildTextSegments(string? authored, string? expanded, ExampleRow? row)
    {
        if (row is null || authored is null || expanded is null || authored == expanded)
            return null;

        var segments = new List<StepTextSegment>();
        var i = 0;
        var literal = new StringBuilder();
        while (i < authored.Length)
        {
            if (authored[i] == '<')
            {
                var close = authored.IndexOf('>', i + 1);
                if (close > i)
                {
                    var name = authored[(i + 1)..close];
                    if (row.Values.TryGetValue(name, out var value))
                    {
                        if (literal.Length > 0)
                        {
                            segments.Add(StepTextSegment.Literal(literal.ToString()));
                            literal.Clear();
                        }

                        segments.Add(StepTextSegment.Param(name, new InlineParameterValue(value, null, VerificationStatus.NotApplicable)));
                        i = close + 1;
                        continue;
                    }
                }
            }

            literal.Append(authored[i]);
            i++;
        }

        if (literal.Length > 0)
            segments.Add(StepTextSegment.Literal(literal.ToString()));

        return segments.Any(s => s.Parameter is not null) ? segments.ToArray() : null;
    }

    private static (Dictionary<string, string>? Values, Dictionary<string, object?>? Raw) BuildExampleValues(ExampleRow? row)
    {
        if (row is null || row.Values.Count == 0)
            return (null, null);
        var values = new Dictionary<string, string>(row.Values, StringComparer.Ordinal);
        var raw = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in row.Values)
            raw[key] = value;
        return (values, raw);
    }

    private static ExampleRow? FindExampleRow(CucumberPickle pickle, GherkinIndex gherkin)
    {
        if (pickle.AstNodeIds is not { Length: > 1 })
            return null;
        return gherkin.FindExampleRow(pickle.AstNodeIds[1]);
    }

    private static string? Key(string? attemptId, string? stepId) =>
        attemptId is { Length: > 0 } && stepId is { Length: > 0 } ? attemptId + "" + stepId : null;

    private static Dictionary<string, T> ToDictionary<T>(IEnumerable<T> items, Func<T, string?> key)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (key(item) is { Length: > 0 } k)
                result[k] = item;
        }

        return result;
    }

    /// <summary>
    /// Feature-file paths as one shape whatever produced them. The golden fixture from playwright-bdd on
    /// Windows says <c>features\kronikol-demo.feature</c>; cucumber-js on Linux and Reqnroll both say
    /// <c>Features/Cake.feature</c>. A consumer globbing or grepping for the path should not have to know
    /// which machine ran the tests, so separators are normalised to <c>/</c>. The trade: a POSIX file name
    /// that genuinely contains a backslash would be rewritten - vanishingly rare, and the alternative is a
    /// field whose shape depends on the producer's OS.
    /// </summary>
    private static string? NormalisePath(string? uri) =>
        NullIfBlank(uri)?.Replace('\\', '/');

    internal static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    internal static string? Strip(string? tag) =>
        tag is null ? null : NullIfBlank(tag.TrimStart('@'));

    /// <summary>Removes the common leading indentation Gherkin keeps on description blocks.</summary>
    internal static string? Dedent(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var indent = lines
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Length - l.TrimStart().Length)
            .DefaultIfEmpty(0)
            .Min();
        return string.Join('\n', lines.Select(l => l.Length >= indent ? l[indent..] : l.TrimStart())).Trim('\n');
    }

    /// <summary>Collects the scenarios of one feature while they arrive out of the message stream.</summary>
    private sealed class FeatureAccumulator(string name, CucumberFeatureNode? node, string? sourceFile)
    {
        public List<Scenario> Scenarios { get; } = [];
        public string? Endpoint { get; set; }

        public Feature ToFeature()
        {
            var tags = (node?.Tags ?? []).Select(t => Strip(t.Name)).Where(t => t is not null).Select(t => t!).ToArray();
            var labels = tags
                .Where(t => !HappyPathDetection.IsHappyPathTag(t)
                            && !t.StartsWith(EndpointTagPrefix, StringComparison.OrdinalIgnoreCase)
                            && !t.StartsWith(CategoryTagPrefix, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            return new Feature
            {
                DisplayName = name,
                Description = NullIfBlank(Dedent(node?.Description)),
                Endpoint = Endpoint,
                SourceFile = sourceFile,
                Labels = labels.Length > 0 ? labels : null,
                Scenarios = Scenarios.ToArray(),
            };
        }
    }

    /// <summary>Materialises attachment bodies as files the report generator can copy next to the HTML.</summary>
    private sealed class AttachmentWriter(CucumberSynthesisOptions options, List<string> warnings)
    {
        private string? _directory;
        private int _counter;

        public FileAttachment? Materialise(CucumberAttachment attachment)
        {
            var name = NullIfBlank(attachment.FileName) ?? "attachment";
            var extension = ExtensionFor(attachment.MediaType);
            // The report renders an attachment inline as an image when its *name* carries an image
            // extension; Cucumber file names usually carry none, so the media type supplies it.
            var displayName = Path.HasExtension(name) ? name : name + extension;

            if (NullIfBlank(attachment.Url) is { } url)
                return new FileAttachment(displayName, ToLocalPath(url), NullIfBlank(attachment.MediaType));

            if (attachment.Body is not { Length: > 0 } body)
                return null;

            var isBase64 = string.Equals(attachment.ContentEncoding, "BASE64", StringComparison.OrdinalIgnoreCase);
            if (!isBase64 && !options.WriteTextAttachments)
                return null;

            try
            {
                var directory = _directory ??= options.AttachmentsDirectory
                    ?? Path.Combine(Path.GetTempPath(), "kronikol-cucumber-attachments", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                var fileName = $"{_counter++:D4}-{Sanitise(displayName)}";
                var path = Path.Combine(directory, fileName);
                if (isBase64)
                    File.WriteAllBytes(path, Convert.FromBase64String(body));
                else
                    File.WriteAllText(path, body);
                return new FileAttachment(displayName, path, NullIfBlank(attachment.MediaType));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
            {
                warnings.Add($"Cucumber messages: attachment '{displayName}' could not be written ({ex.Message}).");
                return null;
            }
        }

        private static string ToLocalPath(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsFile)
                return uri.LocalPath;
            return url;
        }

        private static string Sanitise(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(name.Length);
            foreach (var c in name)
                builder.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return builder.ToString();
        }

        private static string ExtensionFor(string? mediaType) => mediaType?.Split(';')[0].Trim().ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/svg+xml" => ".svg",
            "application/pdf" => ".pdf",
            "application/json" => ".json",
            "application/zip" => ".zip",
            "video/webm" => ".webm",
            "video/mp4" => ".mp4",
            "text/html" => ".html",
            "text/markdown" => ".md",
            "text/plain" => ".txt",
            _ => ".bin",
        };
    }
}
