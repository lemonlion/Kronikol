#!/usr/bin/env python
"""R1, sixth part: what the second independent review found (apply_r1f.py <worktree>).

A step reads the scenario's status, not a kept outcome: Reqnroll opens a step only while the scenario is well, so the scenario failing by now failed in this
step, including through a step that never reached a hook (undefined, ambiguous, an argument that does not convert), which a kept outcome missed. It is the
rule #223's prototype reached, and it takes a field away.
A step found by its text is found under a woven helper too (E42); frames a suite's AfterStep left open are closed before a step closes its own (E45); no null
in the scenario context, as a suite's hook may read it; a step a step ran still feeds the outline's column grouping; a row's Background section holding a
failure stays open when the row is switched to (E44).
"""
import sys

root = sys.argv[1]


def edit(path, *pairs):
    full = root + "/" + path
    text = open(full, encoding="utf-8", newline="").read()
    for old, new in pairs:
        assert text.count(old) == 1, old[:90]
        text = text.replace(old, new)
    open(full, "w", encoding="utf-8", newline="").write(text)


edit(
    "src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs",
    (
        """        // Reqnroll writes a failure only to the step that failed, and the step that ran it carries on, so a step reads
        // what the steps it ran read.
        var status = stepContext.Status == ScenarioExecutionStatus.OK
            ? opened?.NestedOutcome ?? ScenarioExecutionStatus.OK
            : stepContext.Status;
        var passed = status == ScenarioExecutionStatus.OK;
        if (opened is not null)
            StepCollector.CompleteStep(scenarioId, passed);
""",
        """        // Reqnroll opens a step only while the scenario is well, so the scenario failing by now failed in this step. It writes a
        // failure only to the step that failed, the step that ran it carries on, and a step that never reached a hook
        // (undefined, ambiguous) has no frame at all.
        var status = stepContext.Status != ScenarioExecutionStatus.OK ? stepContext.Status : _scenarioContext.ScenarioExecutionStatus;
        var passed = status == ScenarioExecutionStatus.OK;
        if (opened is not null)
        {
            // A suite's AfterStep hook that threw on a step this one ran kept ours from running there.
            for (var left = InnermostOpenStep; left is not null && !ReferenceEquals(left, opened); left = left.Parent)
                StepCollector.CompleteStep(scenarioId, false);
            StepCollector.CompleteStep(scenarioId, passed);
        }
""",
    ),
    (
        """        if (opened is not null)
            _scenarioContext[ReqNRollConstants.OpenStepKey] = parent!;
        if (parent is not null)
        {
            parent.Nested.Add(info);
            if (status != ScenarioExecutionStatus.OK)
                parent.NestedOutcome ??= status;
            return;
        }
""",
        """        if (opened is not null && parent is null)
            _scenarioContext.Remove(ReqNRollConstants.OpenStepKey);
        else if (opened is not null)
            _scenarioContext[ReqNRollConstants.OpenStepKey] = parent!;

        if (parent is not null)
        {
            parent.Nested.Add(info);
            return;
        }
""",
    ),
    (
        """        public List<ReqNRollStepInfo> Nested { get; } = [];
        public ScenarioExecutionStatus? NestedOutcome { get; set; }
""",
        """        public List<ReqNRollStepInfo> Nested { get; } = [];
""",
    ),
    (
        """(exampleValues, exampleRawValues) = ExampleValueGrouper.BuildStructured(flatValues, steps);
""",
        """(exampleValues, exampleRawValues) = ExampleValueGrouper.BuildStructured(flatValues, AndWhatTheyRan(steps));
""",
    ),
    (
        """    private OpenStep? InnermostOpenStep =>
""",
        """    private static List<ReqNRollStepInfo> AndWhatTheyRan(IEnumerable<ReqNRollStepInfo> steps) =>
        steps.SelectMany(step => new[] { step }.Concat(AndWhatTheyRan(step.Nested ?? []))).ToList();

    private OpenStep? InnermostOpenStep =>
""",
    ),
)

edit(
    "src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs",
    (
        """    /// not their table, doc string or inline values. Each is found in order, by its text.
    /// </summary>
    private static ScenarioStep[]? DescribeNested(ScenarioStep[]? subSteps, IReadOnlyList<ReqNRollStepInfo>? nested)
    {
        if (subSteps is null || nested is not { Count: > 0 })
            return subSteps;

        var next = 0;
        foreach (var subStep in subSteps)
        {
            if (next >= nested.Count || !string.Equals(subStep.Text, nested[next].Text, StringComparison.Ordinal))
                continue;

            var step = nested[next++];
            subStep.Status = step.Status.ToStepResult(false);
            subStep.DocString = step.DocString;
            subStep.Parameters = ParseTableText(step.TableText) ?? subStep.Parameters;
            subStep.TextSegments = BuildTextSegments(step);
            DescribeNested(subStep.SubSteps, step.Nested);
        }

        return subSteps;
    }
""",
        """    /// not their table, doc string or inline values. Each is found in order, by its text, under any woven step between.
    /// </summary>
    private static ScenarioStep[]? DescribeNested(ScenarioStep[]? subSteps, IReadOnlyList<ReqNRollStepInfo>? nested)
    {
        if (subSteps is null || nested is not { Count: > 0 })
            return subSteps;

        var next = 0;
        Walk(subSteps);
        return subSteps;

        void Walk(ScenarioStep[] steps)
        {
            foreach (var subStep in steps)
            {
                if (next >= nested.Count || !string.Equals(subStep.Text, nested[next].Text, StringComparison.Ordinal))
                {
                    Walk(subStep.SubSteps ?? []);
                    continue;
                }

                var step = nested[next++];
                subStep.Status = step.Status.ToStepResult(false);
                subStep.DocString = step.DocString;
                subStep.Parameters = ParseTableText(step.TableText) ?? subStep.Parameters;
                subStep.TextSegments = BuildTextSegments(step);
                DescribeNested(subStep.SubSteps, step.Nested);
            }
        }
    }
""",
    ),
)

edit(
    "src/Kronikol/Reports/report-select-row-function.js",
    (
        """                incoming[i].open = detailStates[i];
""",
        """                // What the reader did to the row left carries over, but a row's Background section holding a failure stays open.
                incoming[i].open = detailStates[i] || (incoming[i].classList.contains('scenario-background')
                    && !!incoming[i].querySelector('.step-status.failed'));
""",
    ),
)

edit(
    "tests/Kronikol.Tests/ReqNRoll/BackgroundAsWrittenTests.cs",
    (
        """public class SharedStepsTests
{
""",
        """public class StepsUnderAWovenStepTests
{
    [Fact]
    public void A_step_found_under_a_woven_step_keeps_its_arguments_as_does_the_one_after_it()
    {
        var id = nameof(A_step_found_under_a_woven_step_keeps_its_arguments_as_does_the_one_after_it);
        var inHelper = new ReqNRollStepInfo("Given", "the basket is listed", ScenarioExecutionStatus.OK, null, "|sku|\\n|APPLE|");
        var afterHelper = new ReqNRollStepInfo("Given", "a note says", ScenarioExecutionStatus.OK, null, null, "a doc string");
        var outer = new ReqNRollStepInfo("Given", "a composite step", ScenarioExecutionStatus.OK) { Nested = [inHelper, afterHelper] };

        Kronikol.Tracking.StepCollector.StartStep(id, "Given", outer.Text, null, null);
        Kronikol.Tracking.StepCollector.StartStep(id, "Given", "a woven helper", null, null);
        Kronikol.Tracking.StepCollector.StartStep(id, "Given", inHelper.Text, null, null);
        Kronikol.Tracking.StepCollector.CompleteStep(id, true);
        Kronikol.Tracking.StepCollector.CompleteStep(id, true);
        Kronikol.Tracking.StepCollector.StartStep(id, "Given", afterHelper.Text, null, null);
        Kronikol.Tracking.StepCollector.CompleteStep(id, true);
        Kronikol.Tracking.StepCollector.CompleteStep(id, true);

        var info = new ReqNRollScenarioInfo
        {
            ScenarioId = id,
            ScenarioTitle = "A",
            FeatureTitle = "Orders",
            ScenarioTags = [],
            CombinedTags = [],
            Steps = [outer],
            ExecutionStatus = ScenarioExecutionStatus.OK,
        };

        var subSteps = new[] { info }.ToFeatures()[0].Scenarios[0].Steps![0].SubSteps!;

        Assert.Equal("table", Assert.Single(subSteps[0].SubSteps![0].Parameters!).Name);
        Assert.Equal("a doc string", subSteps[1].DocString);
    }

    [Fact]
    public void A_step_a_step_ran_reads_what_the_hooks_saw_not_what_the_collector_was_told()
    {
        // The collector only hears passed or not, so a pending step is failed there and skipped here.
        var id = nameof(A_step_a_step_ran_reads_what_the_hooks_saw_not_what_the_collector_was_told);
        var inner = new ReqNRollStepInfo("Given", "a pending step", ScenarioExecutionStatus.StepDefinitionPending);
        var outer = new ReqNRollStepInfo("Given", "a composite step", ScenarioExecutionStatus.StepDefinitionPending) { Nested = [inner] };

        Kronikol.Tracking.StepCollector.StartStep(id, "Given", outer.Text, null, null);
        Kronikol.Tracking.StepCollector.StartStep(id, "Given", inner.Text, null, null);
        Kronikol.Tracking.StepCollector.CompleteStep(id, false);
        Kronikol.Tracking.StepCollector.CompleteStep(id, false);

        var info = new ReqNRollScenarioInfo
        {
            ScenarioId = id,
            ScenarioTitle = "A",
            FeatureTitle = "Orders",
            ScenarioTags = [],
            CombinedTags = [],
            Steps = [outer],
            ExecutionStatus = ScenarioExecutionStatus.StepDefinitionPending,
        };

        Assert.Equal(ExecutionResult.Skipped, Assert.Single(new[] { info }.ToFeatures()[0].Scenarios[0].Steps![0].SubSteps!).Status);
    }
}

public class SharedStepsTests
{
""",
    ),
)
print("ok")
