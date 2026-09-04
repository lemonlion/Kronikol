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
        StartStep(ResolveTestId(), keyword, text, paramNames, paramValues);
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
        lock (state)
        {
            // Resolve effective keyword (Given/And/And sequencing)
            step.EffectiveKeyword = ResolveKeyword(state, keyword);

            isTopLevel = state.StepStack.Count == 0;
            state.StepStack.Push(step);
        }

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
    }

    /// <summary>
    /// Completes the current active step for the given test.
    /// </summary>
    public static void CompleteStep(string? testId, bool passed, string? errorMessage = null)
    {
        if (testId is null)
            return;

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
        var attachment = new FileAttachment(resolvedName, filePath);
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
    public static async Task CompleteStepAsync(Task task)
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
    public static async Task<T> CompleteStepAsync<T>(Task<T> task)
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
        public List<FileAttachment>? ScenarioAttachments { get; set; }
    }

    private class CollectedStep
    {
        public string? OriginalKeyword { get; set; }
        public string? EffectiveKeyword { get; set; }
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
