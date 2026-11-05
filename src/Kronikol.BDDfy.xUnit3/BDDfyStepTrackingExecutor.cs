using System.Text.RegularExpressions;
using TestStack.BDDfy;
using TestStack.BDDfy.Configuration;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.BDDfy.xUnit3;

internal class BDDfyStepTrackingExecutor(IStepExecutor inner) : IStepExecutor
{
    private static readonly AsyncLocal<string?> CurrentStepTypeLocal = new();

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string TestId, string StepId), int> Frames = new();

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, string> StepOfFrame = new();

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> RanAgain = new();

    private sealed record Running(int Frame, Running? Outer);

    // The steps this flow is inside, innermost first, and the frames each test handed back on a task.
    private static readonly AsyncLocal<Running?> RunningIn = new();

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<int>> WaitingOnATask = new();

    /// <summary>
    /// The files of the frames BDDfy does not list for this scenario, which have nowhere else to be but the scenario: a step it does not report (a tear
    /// down), which draws a bar and lists no row, and a later run of the test's steps (the next row of an example table, a retry, a second BDDfy call), as
    /// the report lists the first.
    /// </summary>
    internal static IEnumerable<FileAttachment> FilesNotListed(ScenarioStep[] collected, string testId, IReadOnlyCollection<string?> stepsOfTheScenario) =>
        collected.Where(c => !IsListed(c, testId, stepsOfTheScenario)).SelectMany(c => FilesOf(c, testId, stepsOfTheScenario));

    private static bool IsListed(ScenarioStep collected, string testId, IReadOnlyCollection<string?> stepsOfTheScenario) =>
        collected.CollectedId is not { } id
        || !StepOfFrame.TryGetValue(id, out var stepId)
        || (stepsOfTheScenario.Contains(stepId) && FrameOf(testId, stepId) == id);

    // A frame the report lists inside one it does not (a story run in a step of another) keeps its own files.
    private static IEnumerable<FileAttachment> FilesOf(ScenarioStep step, string testId, IReadOnlyCollection<string?> stepsOfTheScenario) =>
        (step.Attachments ?? []).Concat((step.SubSteps ?? [])
            .Where(sub => !sub.OpenedAsFrame || !IsListed(sub, testId, stepsOfTheScenario))
            .SelectMany(sub => FilesOf(sub, testId, stepsOfTheScenario)));

    /// <summary>
    /// Closes what a test's steps left open on a task nothing waited for: an executor around this one that drops the task, or stops waiting at a time limit.
    /// Called when the steps are read, as nothing runs after that.
    /// </summary>
    internal static void CloseLeftRunning(string? testId) => CloseWaiting(testId, _ => true);

    private static void CloseWaiting(string? testId, Func<int, bool> left)
    {
        if (testId is null || !WaitingOnATask.TryGetValue(testId, out var waiting))
            return;

        int[] frames;
        lock (waiting)
        {
            frames = waiting.Where(left).ToArray();
            waiting.RemoveAll(frames.Contains);
        }

        foreach (var frame in frames)
            StepCollector.CompleteFrame(testId, frame, passed: true);
    }

    private static bool RunsInside(int frame)
    {
        for (var running = RunningIn.Value; running is not null; running = running.Outer)
            if (running.Frame == frame)
                return true;
        return false;
    }

    /// <summary>The frame opened for a BDDfy step of a test, if it ran.</summary>
    internal static int? FrameOf(string testId, string? stepId) =>
        stepId is not null && Frames.TryGetValue((testId, stepId), out var frame) ? frame : null;

    public static string? CurrentStepType => CurrentStepTypeLocal.Value;

    // Matches record-style ToString: "TypeName { Prop = Val, ... }" (possibly nested)
    private static readonly Regex RecordPattern = new(@"\b(\w+)\s*\{(?:[^{}]|\{(?:[^{}]|\{[^{}]*\})*\})*\}", RegexOptions.Compiled);

    public object Execute(Step step, object testObject)
    {
        var keyword = step.ExecutionOrder switch
        {
            ExecutionOrder.SetupState or ExecutionOrder.ConsecutiveSetupState => "Given",
            ExecutionOrder.Transition or ExecutionOrder.ConsecutiveTransition => "When",
            ExecutionOrder.Assertion or ExecutionOrder.ConsecutiveAssertion => "Then",
            _ => null
        };

        CurrentStepTypeLocal.Value = keyword?.ToUpperInvariant();

        // Resolve the xUnit test ID for StepCollector
        string? testId = null;
        try { testId = Xunit.TestContext.Current.Test?.UniqueID; } catch { /* not in xUnit context */ }

        // StartFrame decides a keyword's phase, but for a story with no test id, which it skips. A step with no keyword at the top of a
        // story reads Unknown, whatever the flow held before the story. Under a step that said a phase, a story is a label, and
        // the step gets back what its flow held.
        var underKeyword = testId is not null && StepCollector.UnderOpenKeyword(testId);
        var phaseBefore = underKeyword ? TestPhaseContext.Current : TestPhase.Unknown;
        if (testId is null || (keyword is null && !underKeyword))
            TestPhaseContext.Current = PhaseConfiguration.ResolvePhaseFromStepType(keyword);

        // BDDfy waits on a step's task before the next step, so a step still waiting here was left by an executor around this one, and is over.
        // A story run inside that step runs in its flow, and stays under it.
        CloseWaiting(testId, waiting => !RunsInside(waiting));

        var stepText = ExtractStepText(step.Title, keyword);
        var frame = StepCollector.StartFrame(testId, keyword, stepText, phaseKeyword: keyword);
        var outer = RunningIn.Value;
        if (frame is { } running)
            RunningIn.Value = new Running(running, outer);
        if (testId is not null && frame is { } opened)
        {
            StepOfFrame[opened] = step.Id;
            // An example table runs the same steps once per row under one test, and the report lists the first row's: a step
            // running again is the next row, and what runs from there on is not the first row's.
            if (Frames.ContainsKey((testId, step.Id)))
                RanAgain.TryAdd(testId, true);
            else if (!RanAgain.ContainsKey(testId))
                Frames.TryAdd((testId, step.Id), opened);
        }

        var closesWithItsTask = false;
        try
        {
            var result = inner.Execute(step, testObject);

            // BDDfy's async runner waits on what this returns, so an async step stays open until its task ends, with what it opens
            // after its first await under it (#122). With async void support off BDDfy waits on nothing, and the next step would start
            // with this one open, so it closes here.
            if (Configurator.AsyncVoidSupportEnabled && result is Task task)
            {
                closesWithItsTask = true;
                if (testId is not null && frame is { } handedBack)
                {
                    var waiting = WaitingOnATask.GetOrAdd(testId, _ => []);
                    lock (waiting)
                        waiting.Add(handedBack);
                }
                return CloseWhenDone(testId, frame, task);
            }

            return result;
        }
        catch (Exception)
        {
            StepCollector.CompleteFrame(testId, frame, passed: false);
            throw;
        }
        finally
        {
            if (!closesWithItsTask)
                StepCollector.CompleteFrame(testId, frame, passed: true);
            RunningIn.Value = outer;
            CurrentStepTypeLocal.Value = null;
            TestPhaseContext.Current = phaseBefore;
        }
    }

    private static async Task CloseWhenDone(string? testId, int? frame, Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            StepCollector.CompleteFrame(testId, frame, passed: false);
            throw;
        }

        StepCollector.CompleteFrame(testId, frame, passed: true);
    }

    /// <summary>
    /// Extracts the step text from BDDfy's step title, stripping the keyword prefix
    /// (e.g. "Given a user exists" → "a user exists"). Falls back to the full title
    /// if no keyword prefix is present. Also truncates complex object parameters
    /// (record-style ToString) to [TypeName] for cleaner hnote display.
    /// </summary>
    private static string ExtractStepText(string title, string? keyword)
    {
        var text = title.Trim();
        if (keyword is not null && text.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
        {
            var afterKeyword = text[keyword.Length..];
            if (afterKeyword.Length > 0 && afterKeyword[0] == ' ')
                text = afterKeyword[1..];
        }

        // Truncate record-style complex objects: "TypeName { ... }" → "[TypeName]"
        text = RecordPattern.Replace(text, m =>
        {
            var fullMatch = m.Value;
            return ParameterParser.IsComplexObjectString(fullMatch)
                ? $"[{ParameterParser.ExtractTypeNameFromComplexString(fullMatch)}]"
                : fullMatch;
        });

        return text;
    }
}
