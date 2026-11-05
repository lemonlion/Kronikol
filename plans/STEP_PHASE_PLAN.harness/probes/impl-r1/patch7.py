"""Applies the close-through fix to my own copy of the source (wt), and adds an evidence shape to the LightBDD probe. Run once from the k224-review1 folder."""
import sys

# 1. wt: StepCollector gets a way to close what a failing framework step left above it, and the step itself.
p = 'wt/src/Kronikol/Tracking/StepCollector.cs'
s = open(p, encoding='utf-8', newline='').read()
if 'CompleteDownTo' not in s:
    anchor = '    /// <summary>\n    /// Completes the current active step for the given test.\n    /// </summary>'
    crlf = anchor.replace('\n', '\r\n')
    use = anchor if anchor in s else crlf if crlf in s else None
    if use is None:
        sys.exit('StepCollector anchor not found')
    new = '''    // REVIEW PATCH: a framework step that failed closes what it left open above it, and itself, whichever flow those were opened on.
    internal static int OpenStepCount(string testId)
    {
        if (!States.TryGetValue(testId, out var state))
            return 0;
        lock (state)
            return state.StepStack.Count;
    }

    // REVIEW PATCH
    internal static void CompleteDownTo(string testId, int depth, bool passed)
    {
        if (!States.TryGetValue(testId, out var state))
            return;
        lock (state)
        {
            while (state.StepStack.Count > depth)
            {
                var completed = state.StepStack.Pop();
                completed.EndTime = Stopwatch.GetTimestamp();
                completed.Passed = passed;
                for (var node = CurrentFlowStep.Value; node is not null; node = node.Parent)
                    if (ReferenceEquals(node.Step, completed))
                    {
                        CurrentFlowStep.Value = Without(CurrentFlowStep.Value!, node);
                        break;
                    }
                if (state.StepStack.Count > 0)
                    state.StepStack.Peek().SubSteps.Add(completed);
                else
                    state.CompletedSteps.Add(completed);
            }
        }
    }

'''
    if use is crlf:
        new = new.replace('\n', '\r\n')
    s = s.replace(use, new + use, 1)
    open(p, 'w', encoding='utf-8', newline='').write(s)

# 2. wt: LightBDD's decorator.
p = 'wt/src/Kronikol.LightBDD.Core/StepTrackingStepDecorator.cs'
s = open(p, encoding='utf-8', newline='').read()
if 'CompleteDownTo' not in s:
    nl = '\r\n' if '\r\n' in s else '\n'
    a = 'if (testId is not null)' + nl + '        {' + nl + '            // A suite\'s BUT continues'
    if a not in s:
        sys.exit('decorator anchor 1 not found')
    s = s.replace(a, 'var depth = testId is null ? 0 : StepCollector.OpenStepCount(testId); // REVIEW PATCH' + nl + '        ' + a, 1)
    b = 'StepCollector.CompleteStep(testId, passed: false);'
    if b not in s:
        sys.exit('decorator anchor 2 not found')
    s = s.replace(b, 'StepCollector.CompleteDownTo(testId, depth, passed: false); // REVIEW PATCH', 1)
    open(p, 'w', encoding='utf-8', newline='').write(s)

# 3. wt: BDDfy's executor.
p = 'wt/src/Kronikol.BDDfy.xUnit3/BDDfyStepTrackingExecutor.cs'
s = open(p, encoding='utf-8', newline='').read()
if 'CompleteDownTo' not in s:
    a = 'var stepText = ExtractStepText(step.Title, keyword);'
    if a not in s:
        sys.exit('executor anchor 1 not found')
    s = s.replace(a, 'var depth = testId is null ? 0 : StepCollector.OpenStepCount(testId); // REVIEW PATCH\n        ' + a, 1)
    b = 'StepCollector.CompleteStep(testId, passed: false);'
    if b not in s:
        sys.exit('executor anchor 2 not found')
    s = s.replace(b, 'StepCollector.CompleteDownTo(testId, depth, passed: false); // REVIEW PATCH', 1)
    open(p, 'w', encoding='utf-8', newline='').write(s)

# 4. the probe: evidence attached to the failing step, as a suite does before a wait it does not trust.
p = 'probes/lightbdd/Scenarios.cs'
s = open(p, encoding='utf-8', newline='').read()
if 'L18_a_step' not in s:
    new = '''
    private async Task When_evidence_is_attached_and_a_sub_step_outlives_a_timeout(string label)
    {
        await Call(label + "-first");
        var file = Path.Combine(Path.GetTempPath(), "k224-evidence.txt");
        await File.WriteAllTextAsync(file, "evidence");
        Track.Attachment(file, "evidence of " + label);
        await WovenSlow(label + "-slow", new TaskCompletionSource().Task).WaitAsync(TimeSpan.FromMilliseconds(20));
    }

    [Scenario]
    public async Task L18_a_step_attaches_evidence_then_times_out_with_a_woven_sub_step_running() => await Assert_fails(Runner.RunScenarioAsync(
        _ => Given_a_call("l18-given"),
        _ => When_evidence_is_attached_and_a_sub_step_outlives_a_timeout("l18-when"),
        _ => Then_a_call("l18-never")));

    private static async Task Assert_fails(Task run)'''
    anchor = '    private static async Task Assert_fails(Task run)'
    if anchor not in s:
        sys.exit('probe anchor not found')
    s = s.replace('\n' + anchor if ('\n' + anchor) in s else anchor, new, 1)
    open(p, 'w', encoding='utf-8', newline='').write(s)
print('patched')
