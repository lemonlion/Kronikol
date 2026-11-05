using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

/// <summary>
/// R2 of plans/STEP_PHASE_PLAN.md (5.6): the Setup/Action boundary is drawn at the step, once per test, whoever makes the calls.
/// Before this it lived in two private fields of an HTTP handler, so a handler shared by two tests drew it for the first alone, a
/// Given with no HTTP call got none, and a suite of step attributes never got one.
/// </summary>
[Collection("DiagramsFetcher")]
public class StepBoundaryTests
{
    public StepBoundaryTests() => TestPhaseContext.Reset();

    private static string NewTestId() => $"step-boundary-{Guid.NewGuid():N}";

    private static void Attribute(string id, string? keyword)
    {
        StepCollector.StartStep(id, keyword, "a step", null, null);
        OpenedBy(id).Push(null);
    }

    // What each test's open steps are, innermost first: a framework's closes by its id, and any other last in, first out.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentStack<int?>> Opened = new();

    private static System.Collections.Concurrent.ConcurrentStack<int?> OpenedBy(string id) => Opened.GetOrAdd(id, _ => new());

    private static void Adapter(string id, string? keyword) =>
        OpenedBy(id).Push(StepCollector.StartFrame(id, keyword, "a step", phaseKeyword: keyword));

    private static void Done(string id)
    {
        if (OpenedBy(id).TryPop(out var frame) && frame is not null)
            StepCollector.CompleteFrame(id, frame, passed: true);
        else
            StepCollector.CompleteStep(id, passed: true);
    }

    private static void AttributeStep(string id, string keyword)
    {
        Attribute(id, keyword);
        Done(id);
    }

    private static void AdapterStep(string id, string keyword)
    {
        Adapter(id, keyword);
        Done(id);
    }

    private static RequestResponseLog[] Logs(string id) => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == id).ToArray();

    private static int Markers(string id) => Logs(id).Count(l => l.IsActionStart);

    [Fact]
    public void The_first_Action_step_after_a_Setup_step_draws_one_boundary_after_its_bar()
    {
        var id = NewTestId();

        AttributeStep(id, "Given");
        Attribute(id, "When");

        var logs = Logs(id);
        Assert.Equal(1, Markers(id));
        var marker = Array.FindIndex(logs, l => l.IsActionStart);
        var lastBar = Array.FindLastIndex(logs, l => l.MarkerKind == DiagramMarkerKind.Step);
        Assert.True(lastBar >= 0 && lastBar < marker, "the When's bar comes first, and closes the Setup partition");
        Done(id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_When_first_draws_none(bool adapter)
    {
        var id = NewTestId();

        if (adapter)
            AdapterStep(id, "WHEN");
        else
            AttributeStep(id, "When");

        Assert.Equal(0, Markers(id));
    }

    // review/E05: a Setup partition over the first act would have the page's Setup filter hide it.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void When_Then_Given_When_draws_none(bool adapter)
    {
        var id = NewTestId();
        string[] steps = ["When", "Then", "Given", "When"];

        foreach (var step in steps)
            if (adapter)
                AdapterStep(id, step);
            else
                AttributeStep(id, step);

        Assert.Equal(0, Markers(id));
    }

    [Fact]
    public void Given_When_Given_When_draws_one()
    {
        var id = NewTestId();
        string[] steps = ["Given", "When", "Given", "When"];

        foreach (var step in steps)
            AttributeStep(id, step);

        Assert.Equal(1, Markers(id));
    }

    // review2/F1: a plain suite can hold its act in the test's body, between a [GivenStep] and a [ThenStep], and a Setup partition up to the Then would
    // hide that act. A framework has no body between its steps.
    [Fact]
    public void A_woven_Then_after_a_Given_draws_none_and_a_frameworks_does()
    {
        var woven = NewTestId();
        AttributeStep(woven, "Given");
        Attribute(woven, "Then");
        Assert.Equal(0, Markers(woven));

        var framework = NewTestId();
        AdapterStep(framework, "Given");
        Adapter(framework, "Then");
        Assert.Equal(1, Markers(framework));
    }

    [Fact]
    public void A_woven_When_after_a_woven_Then_draws_none()
    {
        var id = NewTestId();

        AttributeStep(id, "Given");
        AttributeStep(id, "Then");
        Attribute(id, "When");

        Assert.Equal(0, Markers(id));
    }

    [Fact]
    public void A_nested_When_under_a_Given_draws_none()
    {
        var id = NewTestId();

        Attribute(id, "Given");
        AttributeStep(id, "When");
        Done(id);

        Assert.Equal(0, Markers(id));
    }

    [Fact]
    public void A_nested_Given_under_a_When_draws_none_and_the_When_after_it_decides_nothing_new()
    {
        var id = NewTestId();

        AttributeStep(id, "Given");
        Attribute(id, "When");
        AttributeStep(id, "Given");
        AttributeStep(id, "When");
        Done(id);

        Assert.Equal(1, Markers(id));
    }

    // review5/B17, review6/B22.
    [Fact]
    public void An_explicit_StartAction_then_a_When_draws_no_second()
    {
        var id = NewTestId();

        DefaultTrackingDiagramOverride.StartAction(id);
        AttributeStep(id, "When");

        Assert.Equal(1, Markers(id));
    }

    [Fact]
    public void An_explicit_StartAction_then_a_Given_and_a_When_draws_no_second()
    {
        var id = NewTestId();

        DefaultTrackingDiagramOverride.StartAction(id);
        AttributeStep(id, "Given");
        AttributeStep(id, "When");

        Assert.Equal(1, Markers(id));
    }

    // review4/M01, Q01.
    [Fact]
    public void A_second_StartAction_logs_nothing()
    {
        var id = NewTestId();

        DefaultTrackingDiagramOverride.StartAction(id);
        DefaultTrackingDiagramOverride.StartAction(id);

        Assert.Equal(1, Markers(id));
    }

    // review/Q12: the wiki's "or to override automatic detection".
    [Fact]
    public void A_StartAction_inside_the_framework_step_that_drew_the_boundary_overrides_it()
    {
        var id = NewTestId();

        AdapterStep(id, "GIVEN");
        Adapter(id, "WHEN");
        DefaultTrackingDiagramOverride.StartAction(id);

        Assert.Equal(2, Markers(id));
        Done(id);
    }

    // review5/R01: a marking check in a Then does not swallow the When into Setup.
    [Fact]
    public void A_StartAction_after_that_step_ended_logs_nothing()
    {
        var id = NewTestId();

        AdapterStep(id, "GIVEN");
        AdapterStep(id, "WHEN");
        Adapter(id, "THEN");
        DefaultTrackingDiagramOverride.StartAction(id);

        Assert.Equal(1, Markers(id));
        Done(id);
    }

    // review6/R40: a step left running above the When does not keep the When open once its own completion came.
    [Fact]
    public async Task A_StartAction_after_that_steps_completion_with_a_step_left_running_logs_nothing()
    {
        var id = NewTestId();
        AdapterStep(id, "GIVEN");
        var when = StepCollector.StartFrame(id, "WHEN", "a step", phaseKeyword: "WHEN");
        await Task.Run(() => StepCollector.StartStep(id, null, "left running", null, null), TestContext.Current.CancellationToken);

        StepCollector.CompleteFrame(id, when, passed: true);
        DefaultTrackingDiagramOverride.StartAction(id);

        Assert.Equal(1, Markers(id));
    }

    // A flow forked inside that step and still running after it does not reach back into it.
    [Fact]
    public async Task A_StartAction_on_a_flow_that_outlived_that_step_logs_nothing()
    {
        var id = NewTestId();
        AdapterStep(id, "GIVEN");
        var when = StepCollector.StartFrame(id, "WHEN", "a step", phaseKeyword: "WHEN");
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var outlives = Task.Run(async () =>
        {
            await ended.Task;
            DefaultTrackingDiagramOverride.StartAction(id);
        }, TestContext.Current.CancellationToken);

        StepCollector.CompleteFrame(id, when, passed: true);
        ended.SetResult();
        await outlives;

        Assert.Equal(1, Markers(id));
    }

    // A flow that was never in it, with the step still open, is not inside it either.
    [Fact]
    public void A_StartAction_on_a_flow_outside_that_open_step_logs_nothing()
    {
        var id = NewTestId();
        AdapterStep(id, "GIVEN");
        var when = StepCollector.StartFrame(id, "WHEN", "a step", phaseKeyword: "WHEN");

        // A thread of its own, with no flow.
        var thread = new Thread(() => DefaultTrackingDiagramOverride.StartAction(id));
        thread.UnsafeStart();
        thread.Join();
        StepCollector.CompleteFrame(id, when, passed: true);

        Assert.Equal(1, Markers(id));
    }

    // review4/M04: on a plain suite that call is the only boundary a test had.
    [Fact]
    public void A_StartAction_after_a_woven_steps_boundary_overrides_it()
    {
        var id = NewTestId();

        AttributeStep(id, "Given");
        AttributeStep(id, "When");
        DefaultTrackingDiagramOverride.StartAction(id);

        Assert.Equal(2, Markers(id));
    }

    // review5/R04.
    [Fact]
    public void A_woven_Then_before_a_frameworks_first_step_does_not_stop_the_boundary()
    {
        var id = NewTestId();

        AttributeStep(id, "Then");
        AdapterStep(id, "GIVEN");
        Adapter(id, "WHEN");

        Assert.Equal(1, Markers(id));
        Done(id);
    }

    // review6/R41, B24: the log holds both, and the last draws.
    [Fact]
    public void A_woven_Given_and_When_before_a_frameworks_first_step_leave_the_boundary_to_the_framework()
    {
        var id = NewTestId();

        AttributeStep(id, "Given");
        AttributeStep(id, "When");
        Assert.Equal(1, Markers(id));

        AdapterStep(id, "GIVEN");
        Adapter(id, "WHEN");

        Assert.Equal(2, Markers(id));
        Done(id);
    }

    // review5/R05, B02, B16: once a framework's steps have begun, a woven keyword on a flow with nothing open above it is its own.
    [Fact]
    public void A_woven_When_after_a_frameworks_steps_began_draws_none()
    {
        var id = NewTestId();

        AdapterStep(id, "GIVEN");
        AttributeStep(id, "When");

        Assert.Equal(0, Markers(id));
    }

    // X19.
    [Fact]
    public void StartSetup_then_a_When_draws_one()
    {
        var id = NewTestId();

        DefaultTrackingDiagramOverride.StartSetup(id);
        AttributeStep(id, "When");

        Assert.Equal(1, Markers(id));
    }

    [Fact]
    public void With_WhenTriggersAction_off_a_step_attribute_draws_none_and_an_adapters_step_does()
    {
        var options = StepCollector.Options;
        try
        {
            StepCollector.Options = new StepTrackingOptions { WhenTriggersAction = false };

            var attributes = NewTestId();
            AttributeStep(attributes, "Given");
            AttributeStep(attributes, "When");
            Assert.Equal(0, Markers(attributes));

            var adapter = NewTestId();
            AdapterStep(adapter, "GIVEN");
            AdapterStep(adapter, "WHEN");
            Assert.Equal(1, Markers(adapter));
        }
        finally
        {
            StepCollector.Options = options;
        }
    }

    // matrix/T01.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_keyword_on_a_flow_the_framework_does_not_name_draws_none_and_a_When_on_the_tests_own_flow_then_does(bool resolverThrows)
    {
        var id = NewTestId();
        AttributeStep(id, "Given");

        var resolver = Track.TestIdResolver;
        try
        {
            Track.TestIdResolver = resolverThrows ? () => throw new InvalidOperationException("no test here") : () => null;
            AttributeStep(id, "When");
        }
        finally
        {
            Track.TestIdResolver = resolver;
        }

        Assert.Equal(0, Markers(id));

        AttributeStep(id, "When");
        Assert.Equal(1, Markers(id));
    }

    // review4/G03.
    [Fact]
    public void A_keyword_under_Detach_draws_none_and_a_When_on_the_tests_own_flow_then_does()
    {
        var id = NewTestId();
        AttributeStep(id, "Given");

        using (TestIdentityScope.Detach())
            AttributeStep(id, "When");

        Assert.Equal(0, Markers(id));

        AttributeStep(id, "When");
        Assert.Equal(1, Markers(id));
    }

    // review/Q05: a flow forked inside a closed step.
    [Fact]
    public async Task A_keyword_on_a_flow_that_outlived_its_step_draws_none_and_a_When_on_the_tests_own_flow_then_does()
    {
        var id = NewTestId();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Attribute(id, "Given");
        var forked = Task.Run(async () =>
        {
            await gate.Task;
            AttributeStep(id, "Then");
        }, TestContext.Current.CancellationToken);
        Done(id);
        gate.SetResult();
        await forked;

        Assert.Equal(0, Markers(id));

        AttributeStep(id, "When");
        Assert.Equal(1, Markers(id));
    }

    // X24, Q9: it moves the phase, as it does today, so it draws the boundary.
    [Fact]
    public void A_bypassed_When_draws_the_boundary()
    {
        var id = NewTestId();

        AttributeStep(id, "Given");
        Attribute(id, "When");
        StepCollector.BypassStep(id, "skipped");

        Assert.Equal(1, Markers(id));
    }

    // L16: an And first says nothing, and the handler saw a setup call in it, which the When's boundary closes.
    [Fact]
    public void An_And_first_with_a_call_keeps_its_boundary()
    {
        var id = NewTestId();
        var client = new object();

        Adapter(id, "AND");
        StepCollector.NoteCallInStepType(id, "AND", client);
        Done(id);
        Adapter(id, "WHEN");

        Assert.Equal(1, Markers(id));
        Done(id);
    }

    // A retried test's attempts keep one id, and it has one boundary across them (matrix/R06).
    // review2/F2: a marker says whether it replaces an earlier one, as a capture written before this has one for each client and each explicit call, and the
    // first of those is its boundary.
    [Fact]
    public void A_marker_says_whether_it_replaces_an_earlier_boundary()
    {
        var first = NewTestId();
        Adapter(first, "Given");
        DefaultTrackingDiagramOverride.StartAction(first);
        Assert.Null(Assert.Single(Logs(first), l => l.IsActionStart).PlantUml);

        var replaced = NewTestId();
        AdapterStep(replaced, "Given");
        Adapter(replaced, "When");
        DefaultTrackingDiagramOverride.StartAction(replaced);
        var markers = Logs(replaced).Where(l => l.IsActionStart).ToArray();
        Assert.Equal(2, markers.Length);
        Assert.Null(markers[0].PlantUml);
        Assert.Equal(DefaultTrackingDiagramOverride.ReplacesBoundary, markers[1].PlantUml);

        var woven = NewTestId();
        AttributeStep(woven, "Given");
        AttributeStep(woven, "When");
        AdapterStep(woven, "Given");
        Adapter(woven, "When");
        var wovenMarkers = Logs(woven).Where(l => l.IsActionStart).ToArray();
        Assert.Equal(2, wovenMarkers.Length);
        Assert.Null(wovenMarkers[0].PlantUml);
        Assert.Equal(DefaultTrackingDiagramOverride.ReplacesBoundary, wovenMarkers[1].PlantUml);
    }

    // review2/N01, N02: as a call does not fail for want of a step type, nor for want of a test id.
    [Fact]
    public void StartAction_with_no_test_id_logs_nothing_and_throws_nothing()
    {
        var before = RequestResponseLogger.RequestAndResponseLogs.Count(l => l.TestId is null);

        DefaultTrackingDiagramOverride.StartAction((string)null!);
        DefaultTrackingDiagramOverride.StartAction(() => null!);

        Assert.Equal(TestPhase.Action, TestPhaseContext.Current);
        Assert.Equal(before, RequestResponseLogger.RequestAndResponseLogs.Count(l => l.TestId is null));
    }

    [Fact]
    public void A_retried_tests_second_attempt_draws_no_second()
    {
        var id = NewTestId();
        string[] attempt = ["GIVEN", "WHEN"];

        foreach (var step in attempt.Concat(attempt))
            AdapterStep(id, step);

        Assert.Equal(1, Markers(id));
    }
}
