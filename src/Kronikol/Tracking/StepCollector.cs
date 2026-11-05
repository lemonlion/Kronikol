using System.Collections.Concurrent;
using System.Diagnostics;
using Kronikol.Reports;
using Kronikol.Tracking.Tabular;

namespace Kronikol.Tracking;

/// <summary>
/// Collects BDD-style steps (Given/When/Then) per test, keyed by test ID.
/// Supports nested steps (sub-steps), keyword sequencing (Given/And/And),
/// parameter capture, and assertion sub-step integration.
/// Every step has an id, from one counter per process, never written to a report. A step a framework's adapter opens for a
/// step of the framework's own is a frame: only its id closes it, so a step method left running cannot take its place.
/// Thread-safe via ConcurrentDictionary.
/// </summary>
public static class StepCollector
{
    private static readonly ConcurrentDictionary<string, TestStepState> States = new();

    // The step this flow of execution is inside. It follows awaits, so two sub-steps running at once each see their own.
    private static readonly AsyncLocal<CollectedStep?> FlowStep = new();

    // Process-wide: a per-test count restarts when ClearSteps runs, and a stale stamp would read a new step.
    private static int _lastOrdinal;

    private const string XunitSkipMarker = "$XunitDynamicSkip$";

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
        OpenStep(ResolveTestId(), keyword, text, paramNames, paramValues, null, null);
    }

    /// <summary>
    /// Completes the current active step, resolving the test ID from the ambient context.
    /// Called by the IL weaver.
    /// </summary>
    public static void CompleteStep(bool passed, string? errorMessage = null)
    {
        if (LeftABypass())
            return;

        CompleteStep(ResolveTestId(), passed, errorMessage, fromTheStepsFlow: true);
    }

    /// <summary>
    /// Starts a new step for the given test. A flow inside an open step starts a sub-step of it. Called by hand with an
    /// async helper around it, whose flow does not reach its caller, the step is found by the steps its caller starts next.
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
        => OpenStep(testId, keyword, text, paramNames, paramValues, table, docString, byHand: true);

    /// <summary>
    /// Starts a step that a framework's adapter opens for a step of the framework's own, and returns the id the adapter
    /// closes it by (<see cref="CompleteFrame"/>) and finds it by (<see cref="ScenarioStep.CollectedId"/>).
    /// </summary>
    internal static int? StartFrame(string? testId, string? keyword, string text, string[][]? table = null, string? docString = null)
        => OpenStep(testId, keyword, text, null, null, table, docString, isFrame: true);

    /// <summary>Closes the frame <paramref name="frameId"/>, by id. A step still running under it finishes by itself.</summary>
    internal static void CompleteFrame(string? testId, int? frameId, bool passed, string? errorMessage = null)
    {
        if (testId is null || frameId is null || !States.TryGetValue(testId, out var state))
            return;

        CollectedStep? frame;
        lock (state)
        {
            frame = state.Open.FirstOrDefault(s => s.Ordinal == frameId);
            if (frame is null)
                return;

            Complete(state, frame, passed, errorMessage);
        }

        if (ReferenceEquals(FlowStep.Value, frame))
            FlowStep.Value = FlowAfter(frame);
    }

    /// <summary>The step a call logged now happened under.</summary>
    internal static int? OpenStepOf(string? testId) => OpenStepOf(testId, out _);

    /// <summary>The step a call logged now happened under, and whether the flow logging it was in that step.</summary>
    internal static int? OpenStepOf(string? testId, out bool isItsFlows)
    {
        isItsFlows = false;
        if (testId is null || !States.TryGetValue(testId, out var state))
            return null;

        // A flow inside an open step of this test answers for itself. Every record asks this, and the walk takes no lock: a
        // step's parent and test never change, and the flow holds nothing it did not set itself.
        if (NearestOpenInFlow(state) is { } own)
        {
            isItsFlows = true;
            return own.Ordinal;
        }

        return OrdinalOfOpenStep(testId);
    }

    /// <summary>The id of the step this test opened last, 0 if none; a step listed with no frame stands after it.</summary>
    internal static int LastStepIdOf(string? testId)
    {
        if (testId is null || !States.TryGetValue(testId, out var state))
            return 0;

        lock (state)
            return state.LastStepId;
    }

    private static CollectedStep? LastOpenFrame(TestStepState state) => state.Open.LastOrDefault(s => s.IsFrame);

    private static int? OpenStep(string? testId, string? keyword, string text, string[]? paramNames, object?[]? paramValues,
        string[][]? table, string? docString, bool isFrame = false, bool byHand = false)
    {
        if (testId is null)
            return null;

        var state = States.GetOrAdd(testId, _ => new TestStepState());
        // Not under the lock: a resolver is a consumer's code.
        var isTheTestsFlow = !isFrame && FlowStep.Value?.State != state && FlowIsTheTests(testId, state);

        var step = new CollectedStep
        {
            OriginalKeyword = keyword,
            Text = text,
            StartTime = Stopwatch.GetTimestamp(),
            Parameters = BuildParameters(paramNames, paramValues)
        };

        step.State = state;
        var isTopLevel = false;
        lock (state)
        {
            var parent = isFrame ? ParentOfNewFrame(state) : ParentOfNewStep(state, isTheTestsFlow);
            if (parent is null && FlowStep.Value is { } cameFrom && !ReferenceEquals(cameFrom.State, state))
                step.CameFrom = cameFrom;
            // A framework's step with none open: what a hook left open is under no step of the scenario, and holds no row.
            if (isFrame && parent is null)
                foreach (var left in state.Open)
                    left.Left = left.PassedOver = true;
            step.Parent = parent;
            step.ByHand = byHand;
            if (parent is not null)
            {
                step.Depth = parent.Depth + 1;
                parent.OpenChildren++;
            }
            step.IsFrame = isFrame;
            step.Ordinal = Interlocked.Increment(ref _lastOrdinal);
            state.LastStepId = step.Ordinal;
            if (parent is null)
                state.LastTopLevel = step;

            // Resolve effective keyword (Given/And/And sequencing)
            step.EffectiveKeyword = ResolveKeyword(state, keyword, isSubStep: parent is not null);

            isTopLevel = parent is null;
            // Its place among its siblings is kept from the start, so steps read in the order they began.
            (parent?.SubSteps ?? state.TopLevel).Add(step);
            state.Open.Add(step);
        }

        FlowStep.Value = step;

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

        // Phase transitions: set ambient test phase based on keyword
        if (Options.WhenTriggersAction && keyword is not null)
        {
            switch (keyword)
            {
                case "Given":
                case "But":
                    TestPhaseContext.Current = TestPhase.Setup;
                    break;
                case "When":
                case "Then":
                case "ButWhen":
                    TestPhaseContext.Current = TestPhase.Action;
                    break;
            }
        }

        return step.Ordinal;
    }

    /// <summary>
    /// Completes the current active step for the given test.
    /// </summary>
    public static void CompleteStep(string? testId, bool passed, string? errorMessage = null) =>
        CompleteStep(testId, passed, errorMessage, fromTheStepsFlow: false);

    // A woven method ends in its own flow. Anyone else names only the test, so last in, first out, as they always had it.
    private static void CompleteStep(string? testId, bool passed, string? errorMessage, bool fromTheStepsFlow)
    {
        if (testId is null)
            return;

        if (!States.TryGetValue(testId, out var state))
            return;

        CollectedStep? completed;
        lock (state)
        {
            completed = fromTheStepsFlow ? NearestOpenInFlow(state) ?? LastOpenInItsCaller(state) : LastOpenInItsCaller(state);
            // Below a frame, or with every caller moved on. A woven step left running closes by itself, with its own result.
            if (completed is null || completed.IsFrame)
                completed = state.Open.LastOrDefault(s => !s.IsFrame && (!s.Left || s.ByHand || s.PassedOver));
            if (completed is null)
                return;

            Complete(state, completed, passed, errorMessage);
        }

        if (ReferenceEquals(FlowStep.Value, completed))
            FlowStep.Value = FlowAfter(completed);
    }

    private static void Complete(TestStepState state, CollectedStep step, bool passed, string? errorMessage)
    {
        if (!step.IsOpen)
            return;

        step.EndTime = Stopwatch.GetTimestamp();
        step.Passed = passed;
        step.ErrorMessage = errorMessage;
        state.Open.Remove(step);
        if (step.Parent is { } parent)
            parent.OpenChildren--;
    }

    /// <summary>This flow's own step of this test, or its nearest ancestor still open; null when it has none. A flow whose step has ended knows nothing about now.</summary>
    private static CollectedStep? NearestOpenInFlow(TestStepState state)
    {
        for (var step = FlowStep.Value; step is not null && ReferenceEquals(step.State, state); step = step.Parent)
            if (step.IsOpen)
                return step;

        return null;
    }

    /// <summary>
    /// Whether a flow with no step of this test is the test's own: it has the test's identity from the scope the test began in, or from the
    /// framework, and nothing has detached it or handed it another. A thread with no context of the test's, a consumer's for one, is not.
    /// </summary>
    private static bool FlowIsTheTests(string testId, TestStepState state) =>
        !TestIdentityScope.IsDetached
        && ReferenceEquals(TestIdentityScope.Mark, state.IdentityMark)
        && string.Equals(ResolveTestId(), testId, StringComparison.Ordinal);

    /// <summary>Keeps the step a request was made in, by the trace it carries, until it is answered: a step on the far side of it stands under that one.</summary>
    internal static void NoteCall(RequestResponseLog log)
    {
        if (log.IsDiagramMarker || log.TestId is null || !States.TryGetValue(log.TestId, out var state))
            return;

        if (log.Type != RequestResponseType.Request)
        {
            if (state.CallsInFlight.TryGetValue(log.TraceId, out var call) && call.Request == log.RequestResponseId)
                state.CallsInFlight.TryRemove(log.TraceId, out _);
        }
        else if (log.OpenStepIsItsFlows && NearestOpenInFlow(state) is { } step)
        {
            state.CallsInFlight[log.TraceId] = (step, log.RequestResponseId);
        }
    }

    /// <summary>The step last opened whose caller has not moved on: what a stack would have on top, less a step left running, which closes by itself.</summary>
    private static CollectedStep? LastOpenInItsCaller(TestStepState state)
    {
        for (var i = state.Open.Count - 1; i >= 0; i--)
            if (!state.Open[i].Left)
                return state.Open[i];

        return null;
    }

    /// <summary>
    /// The step last opened by a hand or a framework's adapter, whose caller has not moved on. A woven stub's never answers here: its
    /// caller is in it and knows, and to anyone else it is a step running elsewhere. Call under the lock.
    /// </summary>
    private static CollectedStep? LastOpenByACaller(TestStepState state)
    {
        for (var i = state.Open.Count - 1; i >= 0; i--)
            if (state.Open[i] is { Left: false } open && (open.ByHand || open.IsFrame))
                return open;

        return null;
    }

    /// <summary>
    /// The parent of a step starting now. A flow inside an open step: that step. A service's flow serving a call: the step that call was made
    /// in, by the trace it carries. A flow that has left its steps, or that is the test's own with no step yet, passes over what runs beside
    /// it, and stands under a step opened by hand or by an adapter, if one is open. A flow whose steps have all ended is a straggler, and goes
    /// under whatever is running now, as a stack had it. Any other flow knows nothing and does the same, passing over a step left running
    /// only when something else is open. Call under the lock.
    /// </summary>
    private static CollectedStep? ParentOfNewStep(TestStepState state, bool isTheTestsFlow)
    {
        if (NearestOpenInFlow(state) is { } own)
            return own;

        if (TestIdentityScope.InboundTrace is { } trace && state.CallsInFlight.TryGetValue(trace, out var call) && call.Step.IsOpen)
            return call.Step;

        var running = state.Open.Count > 0 ? state.Open[^1] : null;
        if (FlowStep.Value is { } flow && ReferenceEquals(flow.State, state))
            return ReferenceEquals(flow, state.OutOfItsSteps) ? LastOpenByACaller(state) : running;

        if (isTheTestsFlow)
            return LastOpenByACaller(state);

        return LastOpenInItsCaller(state) ?? running;
    }

    /// <summary>
    /// The parent of a framework's step starting now: the frame open now, or none. A step method stands between them only where this
    /// flow is in one under that frame, as it is when a step runs another through a step method. One a hook left behind is under no
    /// frame, and holds nothing. Call under the lock.
    /// </summary>
    private static CollectedStep? ParentOfNewFrame(TestStepState state)
    {
        if (LastOpenFrame(state) is not { } frame)
            return null;

        var own = NearestOpenInFlow(state);
        for (var step = own; step is not null; step = step.Parent)
            if (ReferenceEquals(step, frame))
                return own;

        return frame;
    }

    /// <summary>The step an assertion or an attachment belongs to. Call under the lock.</summary>
    private static CollectedStep? CurrentOf(TestStepState state) =>
        NearestOpenInFlow(state) ?? (state.Open.Count > 0 ? state.Open[^1] : null);

    /// <summary>
    /// The step a record logged now was made under, as a number the step keeps when the report is drawn (null: none). A
    /// flow inside an open step of this test answers for itself. A record from elsewhere, a service's own thread or a loop whose
    /// step has ended, reads what is open: the deepest step when one chain is open, and the nearest step they share when several run
    /// at once, which is the most that can be said of it. With several running and nothing shared it reads no step (0 is no step's
    /// id). With nothing open it stays with the last top-level step, the bar above it.
    /// </summary>
    internal static int? OrdinalOfOpenStep(string? testId)
    {
        if (testId is null || !States.TryGetValue(testId, out var state))
            return null;

        lock (state)
        {
            if (NearestOpenInFlow(state) is { } own)
                return own.Ordinal;

            CollectedStep? shared = null;
            var any = false;
            foreach (var step in state.Open)
            {
                if (step.OpenChildren > 0 || step.PassedOver)
                    continue;
                shared = any ? (shared is null ? null : NearestShared(shared, step)) : step;
                any = true;
            }

            if (any)
                return shared?.Ordinal ?? 0;

            return state.LastTopLevel?.Ordinal;
        }
    }

    private static CollectedStep? NearestShared(CollectedStep a, CollectedStep b)
    {
        while (!ReferenceEquals(a, b))
        {
            if (a.Depth < b.Depth)
                (a, b) = (b, a);
            if (a.Parent is not { } up)
                return null;
            a = up;
        }

        return a;
    }

    /// <summary>
    /// Bypasses the current active step, resolving the test ID from the ambient context.
    /// The step body is not executed; it is recorded as Bypassed with an optional reason.
    /// Called by the IL weaver when SkipIf evaluates to true.
    /// </summary>
    public static void BypassStep(string? reason = null)
    {
        // The stub's completion comes through next, with nothing to close, so the flow stays on this step until it does (LeftABypass).
        if (CloseBypassed(ResolveTestId(), reason) is { } bypassed && ReferenceEquals(FlowStep.Value, bypassed))
            FlowStep.Value = FlowAfter(bypassed);
    }

    /// <summary>
    /// Bypasses the current active step for the given test.
    /// The step ends here and is recorded as Bypassed.
    /// </summary>
    public static void BypassStep(string? testId, string? reason = null)
    {
        if (CloseBypassed(testId, reason) is not { } bypassed || !ReferenceEquals(FlowStep.Value, bypassed))
            return;

        // A woven step that bypasses itself by its id is still in its stub, whose completion must close nothing. A hand has no stub.
        if (bypassed.ByHand)
            FlowStep.Value = FlowAfter(bypassed);
        else
            bypassed.StubStillToComplete = true;
    }

    private static CollectedStep? CloseBypassed(string? testId, string? reason)
    {
        if (testId is null || !States.TryGetValue(testId, out var state))
            return null;

        lock (state)
        {
            var bypassed = NearestOpenInFlow(state) ?? LastOpenInItsCaller(state);
            // Only CompleteFrame closes a frame: a second bypass of a step already gone must not take the one under it
            if (bypassed is null or { IsFrame: true })
                return null;

            bypassed.Bypassed = true;
            bypassed.BypassReason = reason;
            Complete(state, bypassed, passed: false, errorMessage: null);
            return bypassed;
        }
    }

    // The stub of a woven step that bypassed itself by its id, on its way out: the step has closed already, and a completion would close the one
    // above it or one running beside it. A stub's own bypass skips its completion, as the weave returns past it.
    private static bool LeftABypass()
    {
        if (FlowStep.Value is not { StubStillToComplete: true } bypassed)
            return false;

        FlowStep.Value = FlowAfter(bypassed);
        return true;
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
            var current = CurrentOf(state);
            if (current is null)
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

            current.SubSteps.Add(subStep);
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
            if (CurrentOf(state) is { } currentStep)
            {
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
            return state.Open.Count > 0;
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
            return state.TopLevel.Select(ToScenarioStep).ToArray();
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

    private static string? ResolveKeyword(TestStepState state, string? keyword, bool isSubStep)
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
        if (isSubStep)
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
            if (paramValues[i] is ITabularParameterData tabular && ReadTabular(tabular) is { } table)
            {
                result[i] = new StepParameter
                {
                    Name = paramNames[i],
                    Kind = StepParameterKind.Tabular,
                    TabularValue = table
                };
            }
            else
            {
                var stringValue = Printed(paramValues[i]);
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

    /// <summary>A table's rows, or null when a row's own getter throws: the step reads it as its type, as for <see cref="Printed"/>.</summary>
    private static TabularParameterValue? ReadTabular(ITabularParameterData tabular)
    {
        try
        {
            return new TabularParameterValue(tabular.GetColumns(), tabular.GetRows(), tabular.IsLinkedOutput);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>A parameter's text. Its ToString is the test's own code, run as the step starts: if it throws, the test
    /// must not fail for being tracked, so the value reads as its type.</summary>
    private static string Printed(object? value)
    {
        try
        {
            return value?.ToString() ?? "null";
        }
        catch (Exception)
        {
            return $"[{value!.GetType().Name}]";
        }
    }

    private static ScenarioStep ToScenarioStep(CollectedStep step)
    {
        var duration = step.EndTime > 0
            ? TimeSpan.FromTicks((long)((step.EndTime - step.StartTime) * ((double)TimeSpan.TicksPerSecond / Stopwatch.Frequency)))
            : (TimeSpan?)null;

        // xUnit v3 skips a test by throwing this in front of the reason, and all a step method hands on is the message.
        var skipped = !step.Passed && !step.Bypassed && step.ErrorMessage?.StartsWith(XunitSkipMarker, StringComparison.Ordinal) == true;
        // A step still running when the report is drawn has no result yet, and says so by having none.
        ExecutionResult? status = step.Bypassed
            ? ExecutionResult.Bypassed
            : step.IsOpen ? null : skipped ? ExecutionResult.Skipped : step.Passed ? ExecutionResult.Passed : ExecutionResult.Failed;

        return new ScenarioStep
        {
            CollectedId = step.Ordinal > 0 ? step.Ordinal : null,
            OpenedAsFrame = step.IsFrame,
            Keyword = step.EffectiveKeyword,
            IsAssertion = step.IsAssertion,
            Text = step.Text,
            Status = status,
            BypassReason = step.BypassReason,
            FailureMessage = skipped ? step.ErrorMessage![XunitSkipMarker.Length..] : step.ErrorMessage,
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

    /// <summary>An exception's message, or null if reading it throws: a step fails with no message, and the caller gets what the method threw.</summary>
    private static string? MessageOf(Exception ex)
    {
        try
        {
            return ex.Message;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Wraps an async step's returned Task so that CompleteStep is called on
    /// completion or failure. Called by the IL weaver for async step methods.
    /// The original exception is preserved (re-thrown via await).
    /// </summary>
    public static Task CompleteStepAsync(Task task) => LeftABypass() || FlowIsInAFrame() ? task : AwaitStep(task, LeaveFlowStep());

    private static async Task AwaitStep(Task task, CollectedStep? step)
    {
        try
        {
            await task.ConfigureAwait(false);
            CompleteAwaited(step, true, null);
        }
        catch (Exception ex)
        {
            CompleteAwaited(step, false, MessageOf(ex));
            throw;
        }
    }

    // A woven stub's own step would be on top of it, so this stub has none to complete: it was bypassed, or never opened.
    private static bool FlowIsInAFrame() => FlowStep.Value is { IsFrame: true, IsOpen: true };

    // Where a flow goes from a step it leaves: up to the parent, or back to the step of another test it was in when this one opened, or
    // out of the test's steps.
    private static CollectedStep FlowAfter(CollectedStep step) => step.Parent ?? step.CameFrom ?? step.State!.OutOfItsSteps;

    // Not async, on purpose: the caller's flow must leave the step before the caller carries on, so that the next
    // step it starts is this one's sibling, while the step's own body keeps it across its awaits.
    private static CollectedStep? LeaveFlowStep()
    {
        var step = FlowStep.Value;
        if (step is null || !step.IsOpen)
            return null;
        // A stub whose StartStep found no test opened nothing: the flow's step is someone else's.
        if (ResolveTestId() is not { } testId || !States.TryGetValue(testId, out var state) || !ReferenceEquals(step.State, state))
            return null;
        step.Left = true;
        FlowStep.Value = FlowAfter(step);
        return step;
    }

    private static void CompleteAwaited(CollectedStep? step, bool passed, string? errorMessage)
    {
        if (step is null)
        {
            CompleteStep(passed, errorMessage);
            return;
        }

        lock (step.State!)
            Complete(step.State!, step, passed, errorMessage);
    }

    /// <summary>
    /// Generic variant for async step methods returning Task&lt;T&gt;.
    /// </summary>
    public static Task<T> CompleteStepAsync<T>(Task<T> task) => LeftABypass() || FlowIsInAFrame() ? task : AwaitStep(task, LeaveFlowStep());

    private static async Task<T> AwaitStep<T>(Task<T> task, CollectedStep? step)
    {
        try
        {
            var result = await task.ConfigureAwait(false);
            CompleteAwaited(step, true, null);
            return result;
        }
        catch (Exception ex)
        {
            CompleteAwaited(step, false, MessageOf(ex));
            throw;
        }
    }

    private class TestStepState
    {
        public TestStepState() => OutOfItsSteps = new CollectedStep { State = this, EndTime = 1 };

        /// <summary>Which identity scope the test's steps began under. A flow under another began its own.</summary>
        public object? IdentityMark { get; } = TestIdentityScope.Mark;
        public ConcurrentDictionary<Guid, (CollectedStep Step, Guid Request)> CallsInFlight { get; } = new();

        /// <summary>What a flow holds once it has moved up out of a top-level step: closed from the start, with no parent.</summary>
        public CollectedStep OutOfItsSteps { get; }

        public CollectedStep? LastTopLevel { get; set; }
        public int LastStepId { get; set; }
        public List<CollectedStep> Open { get; } = new();
        public List<CollectedStep> TopLevel { get; } = new();
        public string? LastKeywordCategory { get; set; }
        public List<FileAttachment>? ScenarioAttachments { get; set; }
    }

    private class CollectedStep
    {
        public int Ordinal { get; set; }
        public bool IsFrame { get; set; }
        public bool ByHand { get; set; }
        public bool StubStillToComplete { get; set; }

        // The step of another test this flow was in when this one opened with no parent of its own.
        public CollectedStep? CameFrom { get; set; }

        // Open since before a framework's step that is not under it: a hook's, or one left behind by the step before.
        public bool PassedOver { get; set; }
        public CollectedStep? Parent { get; set; }
        public int Depth { get; set; }
        public int OpenChildren { get; set; }
        public TestStepState? State { get; set; }

        // A flow asks about its own step with no lock, while whoever ends it holds one.
        public bool IsOpen => EndTime == 0;
        public long EndTime { get => Volatile.Read(ref _endTime); set => Volatile.Write(ref _endTime, value); }
        private long _endTime;

        // Set by the step's own stub with no lock. A reader that races it answers as if it had read a moment earlier.
        public bool Left { get; set; }
        public bool IsAssertion { get; set; }
        public string? OriginalKeyword { get; set; }
        public string? EffectiveKeyword { get; set; }
        public string Text { get; set; } = "";
        public long StartTime { get; set; }
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
