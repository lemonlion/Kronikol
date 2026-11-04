using System.Collections.Concurrent;
using System.Diagnostics;
using Kronikol.Reports;
using Kronikol.Tracking.Tabular;

namespace Kronikol.Tracking;

/// <summary>
/// Collects BDD-style steps (Given/When/Then) per test, keyed by test ID.
/// Supports nested steps (sub-steps), keyword sequencing (Given/And/And),
/// parameter capture, and assertion sub-step integration.
/// Thread-safe via ConcurrentDictionary.
/// </summary>
public static class StepCollector
{
    private static readonly ConcurrentDictionary<string, TestStepState> States = new();

    /// <summary>
    /// Configuration options for step tracking behavior.
    /// Set in test base class constructor or DI initialization.
    /// </summary>
    public static StepTrackingOptions Options { get; set; } = new();

    /// <summary>
    /// Starts a new step, resolving the test ID from the ambient context.
    /// Called by the IL weaver — consumers don't need to supply the test ID.
    /// </summary>
    public static void StartStep(string? keyword, string text, string[]? paramNames, object?[]? paramValues)
    {
        var testId = ResolveTestId();
        StartStep(testId, keyword, text, paramNames, paramValues, table: null, docString: null, phaseKeyword: null, frameworkStep: false);
    }

    /// <summary>
    /// Completes the current active step, resolving the test ID from the ambient context.
    /// Called by the IL weaver.
    /// </summary>
    public static void CompleteStep(bool passed, string? errorMessage = null)
    {
        CompleteStep(ResolveTestId(), passed, errorMessage);
    }

    /// <summary>
    /// Starts a new step for the given test. If there's already an active step,
    /// the new step becomes a sub-step of the current one.
    /// </summary>
    public static void StartStep(string? testId, string? keyword, string text, string[]? paramNames, object?[]? paramValues)
        => StartStep(testId, keyword, text, paramNames, paramValues, table: null, docString: null);

    /// <summary>
    /// Starts a new step carrying a Gherkin step argument: a data <paramref name="table"/> (first row =
    /// header) and/or a <paramref name="docString"/>, both drawn inside the step's delimiter bar (the
    /// Reqnroll hooks pass them — the step text alone rarely makes sense without its table).
    /// </summary>
    public static void StartStep(string? testId, string? keyword, string text, string[]? paramNames, object?[]? paramValues,
        string[][]? table, string? docString)
        => StartStep(testId, keyword, text, paramNames, paramValues, table, docString, phaseKeyword: null, frameworkStep: false);

    /// <summary>
    /// Starts a new step whose phase a framework adapter names. <paramref name="phaseKeyword"/> stands in for
    /// <paramref name="keyword"/> where the phase is concerned, so Reqnroll's And and But are written as they are and take
    /// the phase of the keyword they continue (#195), and it holds whatever
    /// <see cref="StepTrackingOptions.WhenTriggersAction"/> says, which is about step attributes.
    /// </summary>
    internal static void StartStep(string? testId, string? keyword, string text, string[]? paramNames, object?[]? paramValues,
        string[][]? table, string? docString, string? phaseKeyword, bool frameworkStep = true)
    {
        if (testId is null)
            return;

        var state = States.GetOrAdd(testId, _ => new TestStepState());

        var step = new CollectedStep
        {
            OriginalKeyword = keyword,
            Text = text,
            StartTime = Stopwatch.GetTimestamp(),
            Parameters = BuildParameters(paramNames, paramValues)
        };

        var isTopLevel = false;
        var named = phaseKeyword is not null || FrameworkNamesThisFlow();
        var flowPhase = TestPhaseContext.Current;
        var phaseNow = TestPhase.Unknown;
        var startsAction = false;
        lock (state)
        {
            // Resolve effective keyword (Given/And/And sequencing)
            step.EffectiveKeyword = ResolveKeyword(state, keyword);

            isTopLevel = state.StepStack.Count == 0;

            // The outermost open step that says anything about phases decides the test's phase, and a keyword under it is a
            // label (#224). A step with no keyword says nothing, so the keywords under it still decide.
            var says = phaseKeyword is not null
                ? PhaseConfiguration.PhaseOfStep(phaseKeyword, flowPhase != TestPhase.Unknown ? flowPhase : state.Phase)
                : Options.WhenTriggersAction ? PhaseConfiguration.PhaseOfAttribute(keyword) : null;
            step.SaysPhase = says is not (null or TestPhase.Unknown);
            if (step.SaysPhase && !OpenStepSaysPhase(testId))
            {
                phaseNow = says!.Value;

                // A flow the framework does not name, or one that outlived the step it was forked inside, runs for the test and is not
                // the test: its step keeps its phase to itself.
                if (named && !OutlivedStepSaysPhase(testId) && (frameworkStep || !state.StepsSetPhase))
                {
                    if (frameworkStep && !state.StepsSetPhase)
                    {
                        state.ActionStepSeen = false;
                        if (!state.ExplicitBoundaryDrawn && !state.ClientDrewBoundary)
                            state.ActionStartDrawn = false;
                    }

                    // The Setup/Action boundary: the first time this test's steps go from Setup to Action, and never once an Action step
                    // has run, as a Setup partition over it would hide an act.
                    startsAction = says == TestPhase.Action && (state.Phase == TestPhase.Setup || (frameworkStep && state.CallInSetupStep)) && !state.ActionStartDrawn && !state.ActionStepSeen;
                    state.ActionStepSeen |= says == TestPhase.Action;
                    state.ActionStartDrawn |= startsAction;
                    if (startsAction && frameworkStep)
                        state.BoundaryStep = step;
                    state.StepsSetPhase |= frameworkStep;
                    state.Phase = phaseNow;
                }
            }

            state.StepStack.Push(step);
        }

        EnterFlow(testId, step);

        // Emit step delimiter hnote for top-level steps
        if (isTopLevel && Options.ShowStepDelimiters)
        {
            // No keyword is prepended, so the bar's first character is the step text's: capitalise it
            // (Reports.StepText) so the diagram reads the way the step list does.
            var label = Options.PrependKeyword && step.EffectiveKeyword is not null
                ? $"{step.EffectiveKeyword} {text}"
                : Reports.StepText.CapitaliseIfEnabled(text) ?? text;

            // The explicit table (Reqnroll's Gherkin argument) rides first; tabular parameters the IL
            // weaver captured (TabularInputs/TabularOutputs) follow, labelled by parameter name when
            // there is more than one table to tell apart.
            var tables = new List<PlantUml.StepBarTable>();
            if (table is { Length: > 0 })
                tables.Add(new PlantUml.StepBarTable(null, table));
            foreach (var parameter in step.Parameters ?? [])
                if (parameter is { Kind: StepParameterKind.Tabular, TabularValue: { } tabular })
                    tables.Add(new PlantUml.StepBarTable(parameter.Name, ToBarRows(tabular)));

            var bar = PlantUml.StepBarPlantUml.Build(label, tables.Count > 0 ? tables : null, docString);
            DefaultTrackingDiagramOverride.InsertPlantUml(testId, bar, DiagramMarkerKind.Step);
        }

        // This flow reads its own step's phase from here on, and so does a flow the framework cannot name.
        if (phaseNow != TestPhase.Unknown)
            TestPhaseContext.Current = phaseNow;

        // After the step's bar, so the bar closes the Setup partition as the first bar past the setup calls always has.
        if (startsAction)
            DefaultTrackingDiagramOverride.DrawBoundary(testId);
    }

    /// <summary>
    /// Whether an HTTP handler still has to ask its <c>CurrentStepTypeFetcher</c> about this test: no while the test's own
    /// steps set its phase, as every adapter's do, or once its boundary is drawn.
    /// </summary>
    internal static bool NeedsStepTypeFromHandler(string testId, object client)
    {
        if (!States.TryGetValue(testId, out var state))
            return true;

        lock (state)
            return !state.StepsSetPhase && !state.ClientsPastBoundary.Contains(client);
    }

    /// <summary>
    /// Draws the boundary from the step type an HTTP handler reports for a call, for a test whose framework starts no steps
    /// here: a custom <c>CurrentStepTypeFetcher</c>, or LightBDD with no step decorator. What it has seen is the test's, not
    /// the handler's: a handler shared by two tests drew the boundary for the first alone.
    /// </summary>
    internal static void NoteCallInStepType(string testId, string stepType, object client)
    {
        var inSetup = stepType.StartsWith("GIVEN", StringComparison.OrdinalIgnoreCase)
                      || stepType.StartsWith("AND", StringComparison.OrdinalIgnoreCase)
                      || (stepType.StartsWith("BUT", StringComparison.OrdinalIgnoreCase)
                          && !stepType.StartsWith("BUTWHEN", StringComparison.OrdinalIgnoreCase));

        var state = States.GetOrAdd(testId, _ => new TestStepState());
        lock (state)
        {
            if (inSetup)
            {
                state.CallInSetupStep = true;
                state.ClientsInSetup.Add(client);
                return;
            }

            if (!state.ClientsInSetup.Contains(client))
                return;

            state.ClientsPastBoundary.Add(client);
            if (state.ActionStartDrawn)
            {
                TestPhaseContext.Current = TestPhase.Action;
                return;
            }

            state.ActionStartDrawn = true;
            state.ClientDrewBoundary = true;
        }

        DefaultTrackingDiagramOverride.DrawBoundary(testId);
    }

    /// <summary>
    /// Whether the framework itself says this flow is running a test. A <see cref="TestIdentityScope"/> alone, on a thread or on the
    /// server's side of a call, runs for the test and is not the test.
    /// </summary>
    private static bool FrameworkNamesThisFlow()
    {
        // A detached flow inherits the framework's context and is nobody's test, as TestInfoResolver reads it.
        if (TestIdentityScope.IsDetached)
            return false;

        if (Track.TestIdResolver is not { } resolver)
            return true;

        try
        {
            return resolver() is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>A consumer's StartAction(): whether it is the first, as a second one asks for the same boundary again.</summary>
    internal static bool NoteExplicitBoundary(string testId)
    {
        NotePhase(testId, TestPhase.Action);
        var state = States.GetOrAdd(testId, _ => new TestStepState());
        lock (state)
        {
            var superseded = state.ClientDrewBoundary || (state.BoundaryStep is { } boundary && (boundary.EndTime > 0 || !FlowHolds(boundary)));
            var first = !state.ExplicitBoundaryDrawn && !superseded;
            state.ExplicitBoundaryDrawn = true;
            return first;
        }
    }

    /// <summary>
    /// Notes a phase the test set itself, with <c>StartSetup()</c> or <c>StartAction()</c>, so a step that continues the one
    /// before it continues that, and a test that drew its own boundary draws no second one.
    /// </summary>
    internal static void NotePhase(string? testId, TestPhase phase)
    {
        if (testId is null)
            return;

        var state = States.GetOrAdd(testId, _ => new TestStepState());
        lock (state)
        {
            state.Phase = phase;

            state.ActionStartDrawn |= phase == TestPhase.Action;
        }
    }

    /// <summary>
    /// The steps open in this flow, innermost first. A step's parent is what opened it in its own flow, where the stack in
    /// <see cref="TestStepState"/> is the whole test's: a step left running (not awaited, or in a Task.WhenAll) is above
    /// nothing its caller starts next.
    /// </summary>
    private static readonly AsyncLocal<FlowStep?> CurrentFlowStep = new();

    private sealed record FlowStep(string TestId, CollectedStep Step, FlowStep? Parent);

    internal static bool UnderOpenKeyword(string testId) => OpenStepSaysPhase(testId);

    private static bool FlowHolds(CollectedStep step)
    {
        for (var node = CurrentFlowStep.Value; node is not null; node = node.Parent)
            if (ReferenceEquals(node.Step, step))
                return true;
        return false;
    }

    private static bool OpenStepSaysPhase(string testId)
    {
        for (var node = CurrentFlowStep.Value; node is not null; node = node.Parent)
            if (node.TestId == testId && node.Step.SaysPhase && !node.Step.Bypassed && node.Step.EndTime == 0)
                return true;
        return false;
    }

    // Every completer takes its own step off its flow's chain, so a closed step still on one means this flow was forked inside it and outlived it.
    private static bool OutlivedStepSaysPhase(string testId)
    {
        for (var node = CurrentFlowStep.Value; node is not null; node = node.Parent)
            if (node.TestId == testId && node.Step.SaysPhase && !node.Step.Bypassed && node.Step.EndTime != 0)
                return true;
        return false;
    }

    /// <summary>
    /// Whether this test's framework has started a step that decided its phase, so the phase an HTTP handler's fetcher reports for a
    /// call has nothing to add.
    /// </summary>
    internal static bool FrameworkStepsSetPhase()
    {
        if (ResolveTestId() is not { } testId || !States.TryGetValue(testId, out var state))
            return false;

        lock (state)
            return state.StepsSetPhase;
    }

    /// <summary>
    /// The innermost step of this test on this flow is the one completing here, whatever the stack popped: a step left running above it
    /// (not awaited, or past a timeout) sits on the stack's top and takes this completion, and its own closes this one later.
    /// </summary>
    internal static void LeaveCompletedStep(string testId)
    {
        var node = CurrentFlowStep.Value;
        while (node is not null && (node.TestId != testId || node.Step.Bypassed))
            node = node.Parent;
        if (node is not null)
            CurrentFlowStep.Value = Without(CurrentFlowStep.Value!, node);
    }

    // Only the step that left: another test's step above it on this flow is still open.
    private static FlowStep? Without(FlowStep head, FlowStep node) =>
        ReferenceEquals(head, node) ? node.Parent : head with { Parent = Without(head.Parent!, node) };

    private static void EnterFlow(string testId, CollectedStep step) =>
        CurrentFlowStep.Value = new FlowStep(testId, step, CurrentFlowStep.Value);

    /// <summary>The step whose stub is returning its task is no longer above what the caller does next.</summary>
    private static void LeaveFlow()
    {
        var node = CurrentFlowStep.Value;

        // A stub with no test id started nothing, so it has nothing to leave.
        if (node is null || ResolveTestId() is not { } testId)
            return;

        LeaveCompletedStep(testId);
    }

    /// <summary>
    /// Completes the current active step for the given test.
    /// </summary>
    public static void CompleteStep(string? testId, bool passed, string? errorMessage = null)
    {
        if (testId is null)
            return;

        LeaveCompletedStep(testId);

        if (!States.TryGetValue(testId, out var state))
            return;

        CollectedStep? completed;
        lock (state)
        {
            if (state.StepStack.Count == 0)
                return;

            completed = state.StepStack.Pop();
            completed.EndTime = Stopwatch.GetTimestamp();
            completed.Passed = passed;
            completed.ErrorMessage = errorMessage;

            for (var node = CurrentFlowStep.Value; node is not null; node = node.Parent)
                if (ReferenceEquals(node.Step, completed))
                {
                    CurrentFlowStep.Value = node.Parent;
                    break;
                }

            if (state.StepStack.Count > 0)
            {
                // Nested: add as sub-step of parent
                state.StepStack.Peek().SubSteps.Add(completed);
            }
            else
            {
                // Top-level step
                state.CompletedSteps.Add(completed);
            }
        }
    }

    /// <summary>
    /// Bypasses the current active step, resolving the test ID from the ambient context.
    /// The step body is not executed; it is recorded as Bypassed with an optional reason.
    /// Called by the IL weaver when SkipIf evaluates to true.
    /// </summary>
    public static void BypassStep(string? reason = null)
    {
        BypassStep(ResolveTestId(), reason);
    }

    /// <summary>
    /// Bypasses the current active step for the given test.
    /// The step is popped from the stack and recorded as Bypassed.
    /// </summary>
    public static void BypassStep(string? testId, string? reason = null)
    {
        if (testId is null)
            return;

        if (!States.TryGetValue(testId, out var state))
            return;

        lock (state)
        {
            if (state.StepStack.Count == 0)
                return;

            var bypassed = state.StepStack.Pop();
            bypassed.EndTime = Stopwatch.GetTimestamp();
            bypassed.Bypassed = true;
            bypassed.BypassReason = reason;

            if (CurrentFlowStep.Value is { } head && ReferenceEquals(head.Step, bypassed))
                CurrentFlowStep.Value = head.Parent;

            if (state.StepStack.Count > 0)
            {
                // Nested: add as sub-step of parent
                state.StepStack.Peek().SubSteps.Add(bypassed);
            }
            else
            {
                // Top-level step
                state.CompletedSteps.Add(bypassed);
            }
        }
    }

    /// <summary>
    /// Adds an assertion as a sub-step of the currently active step.
    /// </summary>
    public static void AddAssertionSubStep(string? testId, string expression, bool passed,
        string? failureMessage = null, string? sourceFile = null, int sourceLine = 0)
    {
        if (testId is null)
            return;

        if (!Options.IncludeTrackedAssertionsInStepList)
            return;

        if (!States.TryGetValue(testId, out var state))
            return;

        lock (state)
        {
            if (state.StepStack.Count == 0)
                return;

            var subStep = new CollectedStep
            {
                OriginalKeyword = null,
                EffectiveKeyword = null,
                IsAssertion = true,
                Text = expression,
                StartTime = Stopwatch.GetTimestamp(),
                EndTime = Stopwatch.GetTimestamp(),
                Passed = passed,
                ErrorMessage = passed ? null : failureMessage,
                SourceFile = sourceFile,
                SourceLine = sourceLine > 0 ? sourceLine : null
            };

            state.StepStack.Peek().SubSteps.Add(subStep);
        }
    }

    /// <summary>
    /// Adds a file attachment to the currently active step, or as a scenario-level attachment
    /// when no step is active.
    /// </summary>
    public static void AddAttachment(string? testId, string filePath, string? name)
    {
        if (testId is null)
            return;

        var resolvedName = name ?? ExtractFileName(filePath);
        var attachment = AsAttached(new FileAttachment(resolvedName, filePath));
        var state = States.GetOrAdd(testId, _ => new TestStepState());

        lock (state)
        {
            if (state.StepStack.Count > 0)
            {
                var currentStep = state.StepStack.Peek();
                currentStep.Attachments ??= new List<FileAttachment>();
                currentStep.Attachments.Add(attachment);
            }
            else
            {
                state.ScenarioAttachments ??= new List<FileAttachment>();
                state.ScenarioAttachments.Add(attachment);
            }
        }
    }

    /// <summary>
    /// The attachment as it stands when it is attached (#148, plans/MISSING_ATTACHMENTS_PLAN.md F20 and F23). A relative
    /// path is read now, not when the report is drawn: against the working directory, where a test wrote the file, then
    /// against the binary folder, where a fixture shipped beside the binaries is, the first holding the file winning. They
    /// are one folder in every runner's default, and differ under NUnit's WorkDirectory or after a test moves the working
    /// directory, where the copier read the path against the binary folder alone and lost the file. When neither holds it,
    /// the path is kept as given with the working directory noted, so the copier looks there too. The file's length and
    /// last write are noted, so the report can say when it changed or went after it was attached.
    /// </summary>
    internal static FileAttachment AsAttached(FileAttachment attachment)
    {
        var path = attachment.RelativePath;
        if (attachment.IsUrl || string.IsNullOrWhiteSpace(path))
            return attachment;
        try
        {
            if (!Path.IsPathRooted(path))
            {
                var workingDirectory = Directory.GetCurrentDirectory();
                var found = new[] { workingDirectory, AppDomain.CurrentDomain.BaseDirectory }
                    .Select(folder => Path.GetFullPath(path, folder))
                    .FirstOrDefault(File.Exists);
                if (found is null)
                    return attachment with { AttachedFrom = workingDirectory };
                path = found;
            }

            return attachment with { RelativePath = path, WhenAttached = AttachedFile.Of(path) };
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException or UnauthorizedAccessException)
        {
            // Not a path this machine can read: the copier says so when the report is drawn.
            return attachment;
        }
    }

    /// <summary>
    /// Returns scenario-level attachments (those added when no step was active).
    /// </summary>
    public static FileAttachment[]? GetScenarioAttachments(string? testId)
    {
        if (testId is null)
            return null;

        if (!States.TryGetValue(testId, out var state))
            return null;

        lock (state)
        {
            return state.ScenarioAttachments is { Count: > 0 }
                ? state.ScenarioAttachments.ToArray()
                : null;
        }
    }

    private static string ExtractFileName(string filePath)
    {
        var separatorIndex = filePath.LastIndexOfAny(['/', '\\']);
        return separatorIndex >= 0 ? filePath[(separatorIndex + 1)..] : filePath;
    }

    /// <summary>
    /// Returns whether there's an active (in-progress) step for the given test.
    /// </summary>
    public static bool HasActiveStep(string? testId)
    {
        if (testId is null)
            return false;

        if (!States.TryGetValue(testId, out var state))
            return false;

        lock (state)
        {
            return state.StepStack.Count > 0;
        }
    }

    /// <summary>
    /// Returns all collected steps for a test as ScenarioStep array.
    /// </summary>
    public static ScenarioStep[] GetSteps(string? testId)
    {
        if (testId is null)
            return [];

        if (!States.TryGetValue(testId, out var state))
            return [];

        lock (state)
        {
            return state.CompletedSteps.Select(ToScenarioStep).ToArray();
        }
    }

    /// <summary>
    /// Clears all collected steps for a test.
    /// </summary>
    public static void ClearSteps(string? testId)
    {
        if (testId is null)
            return;

        States.TryRemove(testId, out _);
    }

    private static string? ResolveTestId()
    {
        try
        {
            var resolved = Track.TestIdResolver?.Invoke();
            if (resolved is not null)
                return resolved;
        }
        catch
        {
            // Resolver threw — fall through
        }

        return TestIdentityScope.Current?.Id;
    }

    private static string? ResolveKeyword(TestStepState state, string? keyword)
    {
        if (keyword is null)
            return null;

        // Map internal keywords to display form
        var displayKeyword = keyword;
        var categoryKeyword = keyword;
        if (keyword == "ButWhen")
        {
            displayKeyword = "But";
            categoryKeyword = "But"; // sequences with [ButStep]
        }

        // Only apply sequencing at the current nesting level (top-level steps)
        if (state.StepStack.Count > 0)
            return displayKeyword; // Sub-steps keep their original keyword

        if (state.LastKeywordCategory == categoryKeyword)
        {
            return "And";
        }

        state.LastKeywordCategory = categoryKeyword;
        return displayKeyword;
    }

    /// <summary>A tabular parameter as the delimiter bar draws it: header row first, then the data rows.</summary>
    private static string[][] ToBarRows(TabularParameterValue tabular) =>
    [
        tabular.Columns.Select(c => c.Name).ToArray(),
        .. tabular.Rows.Select(r => r.Values.Select(v => v.Value).ToArray()),
    ];

    private static StepParameter[]? BuildParameters(string[]? paramNames, object?[]? paramValues)
    {
        if (paramNames is null || paramValues is null || paramNames.Length == 0)
            return null;

        var count = Math.Min(paramNames.Length, paramValues.Length);
        var result = new StepParameter[count];

        for (var i = 0; i < count; i++)
        {
            if (paramValues[i] is ITabularParameterData tabular)
            {
                result[i] = new StepParameter
                {
                    Name = paramNames[i],
                    Kind = StepParameterKind.Tabular,
                    TabularValue = new TabularParameterValue(
                        tabular.GetColumns(),
                        tabular.GetRows(),
                        tabular.IsLinkedOutput)
                };
            }
            else
            {
                var stringValue = paramValues[i]?.ToString() ?? "null";
                // Truncate complex objects (record ToString, generic collections) to [TypeName]
                if (ParameterParser.IsComplexObjectString(stringValue))
                {
                    var typeName = ParameterParser.ExtractTypeNameFromComplexString(stringValue);
                    stringValue = typeName is not null ? $"[{typeName}]" : stringValue;
                }
                result[i] = new StepParameter
                {
                    Name = paramNames[i],
                    Kind = StepParameterKind.Inline,
                    InlineValue = new InlineParameterValue(
                        Value: stringValue,
                        Expectation: null,
                        Status: VerificationStatus.NotApplicable)
                };
            }
        }

        return result;
    }

    private static ScenarioStep ToScenarioStep(CollectedStep step)
    {
        var duration = step.EndTime > 0
            ? TimeSpan.FromTicks((long)((step.EndTime - step.StartTime) * ((double)TimeSpan.TicksPerSecond / Stopwatch.Frequency)))
            : (TimeSpan?)null;

        var status = step.Bypassed
            ? ExecutionResult.Bypassed
            : step.Passed ? ExecutionResult.Passed : ExecutionResult.Failed;

        return new ScenarioStep
        {
            Keyword = step.EffectiveKeyword,
            IsAssertion = step.IsAssertion,
            Text = step.Text,
            Status = status,
            BypassReason = step.BypassReason,
            FailureMessage = step.ErrorMessage,
            SourceFile = step.SourceFile,
            SourceLine = step.SourceLine,
            Duration = duration,
            Parameters = step.Parameters,
            SubSteps = step.SubSteps.Count > 0
                ? step.SubSteps.Select(ToScenarioStep).ToArray()
                : null,
            Attachments = step.Attachments is { Count: > 0 }
                ? step.Attachments.ToArray()
                : null
        };
    }

    /// <summary>
    /// Wraps an async step's returned Task so that CompleteStep is called on
    /// completion or failure. Called by the IL weaver for async step methods.
    /// The original exception is preserved (re-thrown via await).
    /// </summary>
    public static Task CompleteStepAsync(Task task)
    {
        // Not async: this runs in the woven stub, which is the caller's flow, and an async method's write never reaches it.
        LeaveFlow();
        return CompleteWhenDoneAsync(task);
    }

    private static async Task CompleteWhenDoneAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
            CompleteStep(true, null);
        }
        catch (Exception ex)
        {
            CompleteStep(false, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Generic variant for async step methods returning Task&lt;T&gt;.
    /// </summary>
    public static Task<T> CompleteStepAsync<T>(Task<T> task)
    {
        LeaveFlow();
        return CompleteWhenDoneAsync(task);
    }

    private static async Task<T> CompleteWhenDoneAsync<T>(Task<T> task)
    {
        try
        {
            var result = await task.ConfigureAwait(false);
            CompleteStep(true, null);
            return result;
        }
        catch (Exception ex)
        {
            CompleteStep(false, ex.Message);
            throw;
        }
    }

    private class TestStepState
    {
        public Stack<CollectedStep> StepStack { get; } = new();
        public List<CollectedStep> CompletedSteps { get; } = new();
        public string? LastKeywordCategory { get; set; }
        public TestPhase Phase { get; set; }
        public bool StepsSetPhase { get; set; }
        public bool ActionStepSeen { get; set; }
        public bool ActionStartDrawn { get; set; }
        public bool ExplicitBoundaryDrawn { get; set; }
        public HashSet<object> ClientsInSetup { get; } = [];
        public HashSet<object> ClientsPastBoundary { get; } = [];
        public CollectedStep? BoundaryStep { get; set; }
        public bool ClientDrewBoundary { get; set; }
        public bool CallInSetupStep { get; set; }
        public List<FileAttachment>? ScenarioAttachments { get; set; }
    }

    private class CollectedStep
    {
        public bool IsAssertion { get; set; }
        public string? OriginalKeyword { get; set; }
        public string? EffectiveKeyword { get; set; }
        public bool SaysPhase { get; set; }
        public string Text { get; set; } = "";
        public long StartTime { get; set; }
        public long EndTime { get; set; }
        public bool Passed { get; set; }
        public bool Bypassed { get; set; }
        public string? BypassReason { get; set; }
        public string? ErrorMessage { get; set; }
        public string? SourceFile { get; set; }
        public int? SourceLine { get; set; }
        public StepParameter[]? Parameters { get; set; }
        public List<CollectedStep> SubSteps { get; } = new();
        public List<FileAttachment>? Attachments { get; set; }
    }
}
