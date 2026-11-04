"""R1, third part, after the independent review: a step a step ran keeps its arguments and its outcome, and the walk visits a scenario once. python -I apply_r1c.py <checkout with apply_r1 and apply_r1b applied>"""
import pathlib
import sys

root = pathlib.Path(sys.argv[1])


def edit(path, *pairs):
    p = root / path
    t = p.read_text(encoding="utf-8")
    newline = "\r\n" if "\r\n" in t else "\n"
    t = t.replace("\r\n", "\n")
    for old, new in pairs:
        assert old in t, f"{path}: not found: {old[:70]!r}"
        t = t.replace(old, new, 1)
    p.write_text(t.replace("\n", newline), encoding="utf-8", newline="")


edit(
    "src/Kronikol.ReqNRoll.Core/ReqNRollStepInfo.cs",
    (
        """    InlineParamCapture[]? InlineParams = null);

/// <summary>
/// Captures a single inline""",
        """    InlineParamCapture[]? InlineParams = null)
{
    /// <summary>The steps this step's definition ran, in the order they finished.</summary>
    internal IReadOnlyList<ReqNRollStepInfo>? Nested { get; init; }
}

/// <summary>
/// Captures a single inline""",
    ),
)

edit(
    "src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs",
    (
        """        if (!_scenarioContext.TryGetValue(ReqNRollConstants.OpenStepsKey, out Stack<Stopwatch>? open))
            _scenarioContext[ReqNRollConstants.OpenStepsKey] = open = new Stack<Stopwatch>();
        open!.Push((Stopwatch)_scenarioContext[ReqNRollConstants.StepStopwatchKey]);
""",
        """        if (!_scenarioContext.TryGetValue(ReqNRollConstants.OpenStepsKey, out Stack<OpenStep>? open))
            _scenarioContext[ReqNRollConstants.OpenStepsKey] = open = new Stack<OpenStep>();
        open!.Push(new OpenStep((Stopwatch)_scenarioContext[ReqNRollConstants.StepStopwatchKey]));
""",
    ),
    (
        """        var open = (Stack<Stopwatch>)_scenarioContext[ReqNRollConstants.OpenStepsKey];
        var stepStopwatch = open.Pop();
        stepStopwatch.Stop();
""",
        """        var open = (Stack<OpenStep>)_scenarioContext[ReqNRollConstants.OpenStepsKey];
        var opened = open.Pop();
        var stepStopwatch = opened.Stopwatch;
        stepStopwatch.Stop();
""",
    ),
    (
        """        var passed = stepContext.Status == ScenarioExecutionStatus.OK;
        StepCollector.CompleteStep(scenarioId, passed);

        // A step a step definition ran is that step's sub-step, which StepCollector has made it; it is not one of the
        // scenario's own, whose list and whose Background count run by the feature file.
        if (open.Count > 0)
            return;

        steps.Add(new ReqNRollStepInfo(
            stepContext.StepInfo.StepInstance.StepDefinitionKeyword.ToString(),
            stepContext.StepInfo.Text,
            stepContext.Status,
            stepStopwatch.Elapsed,
            tableText,
            docString,
            inlineParams));
""",
        """        // Reqnroll writes a failure only to the step that failed, and the step that ran it carries on, so a step reads
        // what the steps it ran read.
        var status = stepContext.Status == ScenarioExecutionStatus.OK
            ? opened.NestedOutcome ?? ScenarioExecutionStatus.OK
            : stepContext.Status;
        var passed = status == ScenarioExecutionStatus.OK;
        StepCollector.CompleteStep(scenarioId, passed);

        var info = new ReqNRollStepInfo(
            stepContext.StepInfo.StepInstance.StepDefinitionKeyword.ToString(),
            stepContext.StepInfo.Text,
            status,
            stepStopwatch.Elapsed,
            tableText,
            docString,
            inlineParams) { Nested = opened.Nested };

        // A step a step definition ran is that step's sub-step, which StepCollector has made it; it is not one of the
        // scenario's own, whose list and whose Background count run by the feature file.
        if (open.TryPeek(out var parent))
        {
            parent.Nested.Add(info);
            if (status != ScenarioExecutionStatus.OK)
                parent.NestedOutcome ??= status;
            return;
        }

        steps.Add(info);
""",
    ),
    (
        """    [AfterScenario(Order = int.MaxValue)]
""",
        """    /// <summary>A step under way. Its definition can run more of them, which finish inside it.</summary>
    private sealed class OpenStep(Stopwatch stopwatch)
    {
        public Stopwatch Stopwatch { get; } = stopwatch;
        public List<ReqNRollStepInfo> Nested { get; } = [];
        public ScenarioExecutionStatus? NestedOutcome { get; set; }
    }

    [AfterScenario(Order = int.MaxValue)]
""",
    ),
)

edit(
    "src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs",
    (
        """                SubSteps = collectedSteps is not null && i < collectedSteps.Length
                    ? collectedSteps[i].SubSteps
                    : null,
""",
        """                SubSteps = collectedSteps is not null && i < collectedSteps.Length
                    ? DescribeNested(collectedSteps[i].SubSteps, steps[i].Nested)
                    : null,
""",
    ),
    (
        """    private static StepTextSegment[]? BuildTextSegments(ReqNRollStepInfo step)
""",
        """    /// <summary>
    /// The steps a step definition ran, as the hooks saw them. The collector holds their place among the woven steps, but
    /// not their table, doc string or inline values. Each is found in order, by its text.
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

    private static StepTextSegment[]? BuildTextSegments(ReqNRollStepInfo step)
""",
    ),
)

edit(
    "src/Kronikol/Reports/AttachmentPlacement.cs",
    (
        """        // Each step once, however many scenarios hold it: a list replaced twice would hand replace the copies it made.
        var visited = new HashSet<ScenarioStep>(ReferenceEqualityComparer.Instance);
        foreach (var scenario in scenarios)
        {
            if (scenario.Attachments""",
        """        // Each scenario and step once, however many lists hold it: a list replaced twice would hand replace the copies it made.
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var scenario in scenarios)
        {
            if (!visited.Add(scenario))
                continue;
            if (scenario.Attachments""",
    ),
)

edit(
    "tests/Kronikol.Tests/ReqNRoll/BackgroundAsWrittenTests.cs",
    (
        """public class SharedStepsTests
""",
        """public class StepsAStepRanTests
{
    private static ReqNRollScenarioInfo Ran(string id, ReqNRollStepInfo step, Action collect) => new()
    {
        ScenarioId = id,
        ScenarioTitle = "A",
        FeatureTitle = "Orders",
        ScenarioTags = [],
        CombinedTags = [],
        Steps = Collecting(id, step, collect),
        ExecutionStatus = step.Status,
    };

    private static List<ReqNRollStepInfo> Collecting(string id, ReqNRollStepInfo step, Action collect)
    {
        Kronikol.Tracking.StepCollector.StartStep(id, "Given", step.Text, null, null);
        collect();
        Kronikol.Tracking.StepCollector.CompleteStep(id, step.Status == ScenarioExecutionStatus.OK);
        return [step];
    }

    [Fact]
    public void A_step_a_step_ran_keeps_its_table_doc_string_and_outcome()
    {
        var id = nameof(A_step_a_step_ran_keeps_its_table_doc_string_and_outcome);
        var inner = new ReqNRollStepInfo("Given", "the basket is listed", ScenarioExecutionStatus.TestError, null, "|sku|\\n|APPLE|", "a doc string");
        var outer = new ReqNRollStepInfo("Given", "a composite step", ScenarioExecutionStatus.TestError) { Nested = [inner] };

        var mapped = new[]
        {
            Ran(id, outer, () =>
            {
                Kronikol.Tracking.StepCollector.StartStep(id, "Given", "woven first", null, null);
                Kronikol.Tracking.StepCollector.CompleteStep(id, true);
                Kronikol.Tracking.StepCollector.StartStep(id, "Given", inner.Text, null, null);
                Kronikol.Tracking.StepCollector.CompleteStep(id, false);
            }),
        }.ToFeatures()[0].Scenarios[0].Steps![0];

        Assert.Equal(ExecutionResult.Failed, mapped.Status);
        Assert.Equal(["woven first", inner.Text], mapped.SubSteps!.Select(s => s.Text));
        Assert.Null(mapped.SubSteps![0].DocString);
        Assert.Equal("a doc string", mapped.SubSteps![1].DocString);
        Assert.Equal("table", Assert.Single(mapped.SubSteps![1].Parameters!).Name);
        Assert.Equal(ExecutionResult.Failed, mapped.SubSteps![1].Status);
    }
}

public class SharedStepsTests
""",
    ),
)
print("ok")
