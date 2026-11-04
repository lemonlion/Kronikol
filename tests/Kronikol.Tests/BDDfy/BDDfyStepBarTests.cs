using Kronikol.BDDfy.xUnit3;
using Kronikol.Reports;
using Kronikol.Tracking;
using ExecutionOrder = TestStack.BDDfy.ExecutionOrder;
using IStepExecutor = TestStack.BDDfy.IStepExecutor;
using Step = TestStack.BDDfy.Step;
using StepTitle = TestStack.BDDfy.StepTitle;

namespace Kronikol.Tests.BDDfy;

/// <summary>
/// <c>plans/CALLS_UNDER_THEIR_STEP_PLAN.md</c> R2: BDDfy's And and But keep the word in the step's title and take the keyword from
/// the step's order, so a bar can read And And. Published read such a bar only where the bar drew it unchanged.
/// </summary>
[Collection("DiagramsFetcher")]
public class BDDfyStepBarTests
{
    private const string PastABarsWrap = "with a title that goes on past the point where a bar wraps its label at one hundred and ten characters or so";

    [Theory]
    [InlineData("Given a user", "And a second", "But not a third", "When they order", "And they pay", "Then it ships", "And it arrives", @" at C:\temp")]
    [InlineData("Given a user", "And a second", "But not a third", "When they order", "And they pay", "Then it ships", "And it arrives", " " + PastABarsWrap)]
    // By a reflective name, where AndGiven keeps its And and the rest of the name.
    [InlineData("Given a user", "And given a second", "But given no third", "When they order", "And when they pay", "Then it ships", "And then it arrives", " " + PastABarsWrap)]
    public void A_steps_own_bar_is_read_where_its_title_keeps_its_And_or_But(
        string given, string and, string but, string when, string andWhen, string then, string andThen, string tail)
    {
        var testId = TestContext.Current.Test!.UniqueID;
        StepCollector.ClearSteps(testId);
        (ExecutionOrder Order, string Title)[] ran =
        [
            (ExecutionOrder.SetupState, given + tail),
            (ExecutionOrder.ConsecutiveSetupState, and + tail),
            (ExecutionOrder.ConsecutiveSetupState, but + tail),
            (ExecutionOrder.Transition, when + tail),
            (ExecutionOrder.ConsecutiveTransition, andWhen + tail),
            (ExecutionOrder.Assertion, then + tail),
            (ExecutionOrder.ConsecutiveAssertion, andThen + tail),
        ];

        var executor = new BDDfyStepTrackingExecutor(new CallingExecutor(testId));
        var steps = ran.Select(run => new Step(_ => null!, new StepTitle(run.Title), false, run.Order, true, [])).ToArray();
        foreach (var step in steps)
            executor.Execute(step, this);

        try
        {
            var info = new BDDfyScenarioInfo
            {
                TestId = testId,
                StoryTitle = "Story",
                ScenarioTitle = "Scenario",
                Tags = [],
                Steps = [.. steps.Select(step => step.ToBDDfyStepInfo())],
                Result = TestStack.BDDfy.Result.Passed,
            };
            var features = new[] { info }.ToFeatures();

            var paths = ReportGenerator.AttributeInteractionsToStepPaths([.. RequestResponseLogger.RequestAndResponseLogs.Where(log => log.TestId == testId)], features);

            Assert.Equal(["0", "1", "2", "3", "4", "5", "6"], paths[testId]);
        }
        finally
        {
            StepCollector.ClearSteps(testId);
        }
    }

    /// <summary>A step's body, as BDDfy's own executor would run it: one call.</summary>
    private sealed class CallingExecutor(string testId) : IStepExecutor
    {
        public object Execute(Step step, object testObject)
        {
            RequestResponseLogger.Log(new RequestResponseLog("Test", testId, HttpMethod.Get, null, new Uri("http://svc/x"), [], "svc", "api",
                RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false));
            return null!;
        }
    }
}
