using System.Net;
using System.Reflection;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

/// <summary>
/// A step a framework's adapter opens for a framework step is a frame: it has an id, only its id closes it, and whatever it finds
/// open or leaves open keeps a row of its own (plans/STEP_IDENTITY_PLAN.md 5.1). With no frame, the stack is what it always was.
/// </summary>
[Collection("DiagramsFetcher")]
public class StepFrameTests
{
    private static string NewTest() => "frame-" + Guid.NewGuid();

    [Fact]
    public void A_frame_is_closed_by_its_id_and_listed_with_it()
    {
        var test = NewTest();

        var frame = StepCollector.StartFrame(test, "Given", "a basket");
        Assert.True(StepCollector.HasActiveStep(test));
        StepCollector.CompleteFrame(test, frame, passed: true);

        var step = Assert.Single(StepCollector.GetSteps(test));
        Assert.NotNull(frame);
        Assert.Equal(frame, step.CollectedId);
        Assert.Equal("a basket", step.Text);
        Assert.Equal(ExecutionResult.Passed, step.Status);
        Assert.NotNull(step.Duration);
        Assert.False(StepCollector.HasActiveStep(test));
    }

    [Fact]
    public void A_failed_frame_keeps_its_message()
    {
        var test = NewTest();

        var frame = StepCollector.StartFrame(test, "When", "it fails");
        StepCollector.CompleteFrame(test, frame, passed: false, "declined");

        var step = Assert.Single(StepCollector.GetSteps(test));
        Assert.Equal(ExecutionResult.Failed, step.Status);
    }

    [Fact]
    public void No_test_opens_no_frame_and_closing_it_does_nothing()
    {
        Assert.Null(StepCollector.StartFrame(null, "Given", "a basket"));
        StepCollector.CompleteFrame(null, null, passed: true);
    }

    [Fact]
    public void Closing_with_no_id_or_a_stranger_id_leaves_the_frame_open()
    {
        var test = NewTest();

        var frame = StepCollector.StartFrame(test, "Given", "a basket");
        StepCollector.CompleteFrame(test, null, passed: false);
        StepCollector.CompleteFrame(test, frame + 1_000_000, passed: false);

        Assert.True(StepCollector.HasActiveStep(test));
        Assert.Null(Assert.Single(StepCollector.GetSteps(test)).Status);
    }

    [Fact]
    public void Closing_a_frame_twice_leaves_its_outcome_and_the_frame_below_it()
    {
        // BDDfy's and LightBDD's adapters close in a catch, then in a finally.
        var test = NewTest();

        var outer = StepCollector.StartFrame(test, "Given", "the outer step");
        var inner = StepCollector.StartFrame(test, "Given", "the inner step");
        StepCollector.CompleteFrame(test, inner, passed: false);
        StepCollector.CompleteFrame(test, inner, passed: true);

        Assert.True(StepCollector.HasActiveStep(test));
        StepCollector.CompleteFrame(test, outer, passed: false);

        var step = Assert.Single(StepCollector.GetSteps(test));
        var sub = Assert.Single(step.SubSteps!);
        Assert.Equal("the inner step", sub.Text);
        Assert.Equal(inner, sub.CollectedId);
        Assert.Equal(ExecutionResult.Failed, sub.Status);
    }

    [Fact]
    public void A_frame_closes_through_a_step_left_running_which_keeps_its_place_under_it()
    {
        var test = NewTest();

        var first = StepCollector.StartFrame(test, "Given", "a step leaves one running");
        StepCollector.StartStep(test, null, "A slow check", null, null);
        StepCollector.CompleteFrame(test, first, passed: true);

        Assert.True(StepCollector.HasActiveStep(test));
        var second = StepCollector.StartFrame(test, "When", "the next step");
        StepCollector.CompleteFrame(test, second, passed: true);

        var steps = StepCollector.GetSteps(test);
        Assert.Equal(["a step leaves one running", "the next step"], steps.Select(s => s.Text));
        var leftRunning = Assert.Single(steps[0].SubSteps!);
        Assert.Equal("A slow check", leftRunning.Text);
        Assert.Null(leftRunning.Status);
        Assert.Null(leftRunning.Duration);
        Assert.Null(steps[1].SubSteps);
    }

    [Fact]
    public void Only_CompleteFrame_closes_a_frame()
    {
        var test = NewTest();

        var frame = StepCollector.StartFrame(test, "Given", "a basket");
        StepCollector.CompleteStep(test, passed: false, "not its own");

        Assert.True(StepCollector.HasActiveStep(test));
        StepCollector.CompleteFrame(test, frame, passed: true);

        Assert.Equal(ExecutionResult.Passed, Assert.Single(StepCollector.GetSteps(test)).Status);
    }

    [Fact]
    public void A_completion_that_finds_a_frame_on_top_finishes_the_step_left_last()
    {
        var test = NewTest();

        var first = StepCollector.StartFrame(test, "Given", "a step leaves one running");
        StepCollector.StartStep(test, null, "A slow check", null, null);
        StepCollector.CompleteFrame(test, first, passed: true);
        var second = StepCollector.StartFrame(test, "When", "the next step");

        StepCollector.CompleteStep(test, passed: false, "too slow");

        Assert.True(StepCollector.HasActiveStep(test));
        StepCollector.CompleteFrame(test, second, passed: true);
        var steps = StepCollector.GetSteps(test);
        Assert.Equal(ExecutionResult.Passed, steps[1].Status);
        var leftRunning = Assert.Single(steps[0].SubSteps!);
        Assert.Equal(ExecutionResult.Failed, leftRunning.Status);
        Assert.NotNull(leftRunning.Duration);
    }

    [Fact]
    public void A_completion_that_finds_nothing_finishes_the_step_left_last()
    {
        var test = NewTest();

        var frame = StepCollector.StartFrame(test, "Given", "a step leaves one running");
        StepCollector.StartStep(test, null, "A slow check", null, null);
        StepCollector.CompleteFrame(test, frame, passed: true);

        StepCollector.CompleteStep(test, passed: true);

        Assert.NotNull(Assert.Single(StepCollector.GetSteps(test)[0].SubSteps!).Duration);
    }

    [Fact]
    public void Two_left_running_one_ending_before_the_frame_closes_keep_their_rows()
    {
        var test = NewTest();

        var frame = StepCollector.StartFrame(test, "Given", "two are left running");
        StepCollector.StartStep(test, null, "The slow one", null, null);
        StepCollector.StartStep(test, null, "The quick one", null, null);
        StepCollector.CompleteStep(test, passed: true);
        StepCollector.CompleteFrame(test, frame, passed: true);
        StepCollector.CompleteStep(test, passed: false);

        var outer = Assert.Single(StepCollector.GetSteps(test));
        var slow = Assert.Single(outer.SubSteps!);
        Assert.Equal("The slow one", slow.Text);
        Assert.Equal(ExecutionResult.Failed, slow.Status);
        var quick = Assert.Single(slow.SubSteps!);
        Assert.Equal("The quick one", quick.Text);
        Assert.Equal(ExecutionResult.Passed, quick.Status);
    }

    [Fact]
    public void A_frame_opening_with_no_frame_below_it_treats_what_is_open_as_left_running()
    {
        // A hook's step not awaited: it is not the Gherkin step's parent.
        var test = NewTest();

        StepCollector.StartStep(test, null, "A hook left me running", null, null);
        var frame = StepCollector.StartFrame(test, "Given", "the first step");
        StepCollector.CompleteFrame(test, frame, passed: true);
        StepCollector.CompleteStep(test, passed: true);

        var steps = StepCollector.GetSteps(test);
        Assert.Equal(["A hook left me running", "the first step"], steps.Select(s => s.Text));
        Assert.NotNull(steps[0].Duration);
        Assert.Null(steps[1].SubSteps);
    }

    [Fact]
    public void A_frame_opening_under_a_frame_nests_and_does_not_treat_it_as_left_running()
    {
        var test = NewTest();

        var outer = StepCollector.StartFrame(test, "Given", "a step that runs another");
        var inner = StepCollector.StartFrame(test, "Given", "the one it runs");
        StepCollector.CompleteFrame(test, inner, passed: true);
        StepCollector.CompleteFrame(test, outer, passed: true);

        var step = Assert.Single(StepCollector.GetSteps(test));
        Assert.Equal(inner, Assert.Single(step.SubSteps!).CollectedId);
    }

    [Fact]
    public void A_step_method_inside_a_frame_is_its_sub_step_with_an_id_of_its_own()
    {
        var test = NewTest();

        var frame = StepCollector.StartFrame(test, "Given", "a basket");
        StepCollector.StartStep(test, null, "An item is added", null, null);
        var inside = StepCollector.OpenStepOf(test);
        StepCollector.CompleteStep(test, passed: true);
        StepCollector.CompleteFrame(test, frame, passed: true);

        var sub = Assert.Single(Assert.Single(StepCollector.GetSteps(test)).SubSteps!);
        Assert.NotEqual(frame, inside);
        Assert.Equal(inside, sub.CollectedId);
    }

    [Fact]
    public void A_second_bypass_does_not_close_a_frame()
    {
        var test = NewTest();

        var frame = StepCollector.StartFrame(test, "Given", "a basket");
        StepCollector.StartStep(test, null, "Skipped if not needed", null, null);
        StepCollector.BypassStep(test, "not needed");
        StepCollector.BypassStep(test, "not needed");

        Assert.True(StepCollector.HasActiveStep(test));
        StepCollector.CompleteFrame(test, frame, passed: true);

        var step = Assert.Single(StepCollector.GetSteps(test));
        Assert.Equal(ExecutionResult.Passed, step.Status);
        Assert.Single(step.SubSteps!);
    }

    [Fact]
    public void OpenStepOf_is_the_deepest_step_open_then_the_last_top_level_step_and_null_before_the_first()
    {
        var test = NewTest();

        Assert.Null(StepCollector.OpenStepOf(test));

        var frame = StepCollector.StartFrame(test, "Given", "a basket");
        Assert.Equal(frame, StepCollector.OpenStepOf(test));

        StepCollector.StartStep(test, null, "An item is added", null, null);
        var inner = StepCollector.OpenStepOf(test);
        Assert.NotNull(inner);
        Assert.NotEqual(frame, inner);

        StepCollector.CompleteStep(test, passed: true);
        Assert.Equal(frame, StepCollector.OpenStepOf(test));

        StepCollector.CompleteFrame(test, frame, passed: true);
        Assert.Equal(frame, StepCollector.OpenStepOf(test));

        Assert.Null(StepCollector.OpenStepOf(null));
        Assert.Null(StepCollector.OpenStepOf(NewTest()));
    }

    [Fact]
    public void Ids_never_repeat_across_ClearSteps_or_across_tests()
    {
        var test = NewTest();

        var first = StepCollector.StartFrame(test, "Given", "a basket");
        StepCollector.CompleteFrame(test, first, passed: true);
        StepCollector.ClearSteps(test);
        var second = StepCollector.StartFrame(test, "Given", "a basket");
        var other = StepCollector.StartFrame(NewTest(), "Given", "a basket");

        Assert.NotEqual(first, second);
        Assert.NotEqual(second, other);
        Assert.NotEqual(first, other);
    }

    [Fact]
    public void With_no_frame_two_step_methods_overlap_as_they_always_have()
    {
        // The plain lanes have no frames: the top of the stack closes, whichever step that is.
        var test = NewTest();

        StepCollector.StartStep(test, null, "The first", null, null);
        StepCollector.StartStep(test, null, "The second", null, null);
        StepCollector.CompleteStep(test, passed: true);
        StepCollector.CompleteStep(test, passed: false);
        StepCollector.CompleteStep(test, passed: false);

        var step = Assert.Single(StepCollector.GetSteps(test));
        Assert.Equal("The first", step.Text);
        Assert.Equal(ExecutionResult.Failed, step.Status);
        var sub = Assert.Single(step.SubSteps!);
        Assert.Equal("The second", sub.Text);
        Assert.Equal(ExecutionResult.Passed, sub.Status);
    }

    [Fact]
    public void A_frame_shares_one_keyword_sequence_with_a_step_method()
    {
        // The bar and the painted list read alike (STEP_IDENTITY_PLAN.md 5.2).
        var test = NewTest();

        StepCollector.StartStep(test, "Given", "A tenant exists", null, null);
        StepCollector.CompleteStep(test, passed: true);
        var frame = StepCollector.StartFrame(test, "Given", "a registered customer");
        StepCollector.CompleteFrame(test, frame, passed: true);

        Assert.Equal(["Given", "And"], StepCollector.GetSteps(test).Select(s => s.Keyword));
    }

    [Theory]
    [InlineData(nameof(StepCollector.StartStep), new[] { typeof(string), typeof(string), typeof(string[]), typeof(object[]) })]
    [InlineData(nameof(StepCollector.StartStep), new[] { typeof(string), typeof(string), typeof(string), typeof(string[]), typeof(object[]) })]
    [InlineData(nameof(StepCollector.StartStep), new[] { typeof(string), typeof(string), typeof(string), typeof(string[]), typeof(object[]), typeof(string[][]), typeof(string) })]
    [InlineData(nameof(StepCollector.CompleteStep), new[] { typeof(bool), typeof(string) })]
    [InlineData(nameof(StepCollector.CompleteStep), new[] { typeof(string), typeof(bool), typeof(string) })]
    [InlineData(nameof(StepCollector.BypassStep), new[] { typeof(string) })]
    [InlineData(nameof(StepCollector.BypassStep), new[] { typeof(string), typeof(string) })]
    public void What_the_weaver_binds_to_keeps_its_public_signature_and_returns_nothing(string name, Type[] parameters)
    {
        var method = typeof(StepCollector).GetMethod(name, BindingFlags.Public | BindingFlags.Static, parameters);

        Assert.NotNull(method);
        Assert.Equal(typeof(void), method.ReturnType);
    }

    [Fact]
    public void Nothing_about_a_frame_is_public()
    {
        var members = typeof(StepCollector).GetMembers(BindingFlags.Public | BindingFlags.Static).Select(m => m.Name);

        Assert.DoesNotContain(nameof(StepCollector.StartFrame), members);
        Assert.DoesNotContain(nameof(StepCollector.CompleteFrame), members);
        Assert.DoesNotContain(nameof(StepCollector.OpenStepOf), members);
        Assert.Null(typeof(ScenarioStep).GetProperty(nameof(ScenarioStep.CollectedId), BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(typeof(ScenarioStep).GetProperty(nameof(ScenarioStep.OutsideFrameworkStep), BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(typeof(ScenarioStep).GetProperty(nameof(ScenarioStep.DidNotRun), BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(typeof(RequestResponseLog).GetProperty(nameof(RequestResponseLog.OpenStep), BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void A_record_is_stamped_with_the_step_open_when_it_is_logged_and_between_steps_with_the_one_above_it()
    {
        var test = NewTest();

        RequestResponseLogger.Log(Call(test, "before"));
        var frame = StepCollector.StartFrame(test, "Given", "a basket");
        RequestResponseLogger.Log(Call(test, "inside"));
        StepCollector.StartStep(test, null, "An item is added", null, null);
        var deepest = StepCollector.OpenStepOf(test);
        RequestResponseLogger.Log(Call(test, "deeper"));
        StepCollector.CompleteStep(test, passed: true);
        StepCollector.CompleteFrame(test, frame, passed: true);
        RequestResponseLogger.Log(Call(test, "after"));

        var stamps = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == test && !l.IsDiagramMarker)
            .ToDictionary(l => l.Uri.Host, l => l.OpenStep);
        Assert.Null(stamps["before"]);
        Assert.Equal(frame, stamps["inside"]);
        Assert.Equal(deepest, stamps["deeper"]);
        Assert.Equal(frame, stamps["after"]);
    }

    [Fact]
    public void A_tracked_assertion_has_no_id_as_no_call_can_be_logged_under_it()
    {
        var test = NewTest();

        var frame = StepCollector.StartFrame(test, "Then", "it holds");
        StepCollector.AddAssertionSubStep(test, "it holds", passed: true);
        StepCollector.CompleteFrame(test, frame, passed: true);

        Assert.Null(Assert.Single(Assert.Single(StepCollector.GetSteps(test)).SubSteps!).CollectedId);
    }

    [Fact]
    public void A_bar_carries_the_id_of_the_step_that_drew_it()
    {
        var test = NewTest();

        var frame = StepCollector.StartFrame(test, "Given", "a basket");
        StepCollector.CompleteFrame(test, frame, passed: true);

        var bar = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == test && l.MarkerKind == DiagramMarkerKind.Step).ToArray();
        Assert.NotEmpty(bar);
        Assert.All(bar, half => Assert.Equal(frame, half.OpenStep));
    }

    private static RequestResponseLog Call(string test, string host) =>
        new("Test", test, HttpMethod.Get, null, new Uri("http://" + host + "/x"), [], host, "api",
            RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false);

    [Fact]
    public void A_frame_says_it_was_one_and_a_step_method_does_not()
    {
        var test = NewTest();
        var frame = StepCollector.StartFrame(test, "Given", "a basket");
        StepCollector.CompleteFrame(test, frame, true);
        StepCollector.StartStep(test, null, "a step method", null, null);
        StepCollector.CompleteStep(test, true);

        var steps = StepCollector.GetSteps(test);

        Assert.True(steps[0].OpenedAsFrame);
        Assert.False(steps[1].OpenedAsFrame);
    }

    [Fact]
    public void A_step_method_that_skips_an_xUnit_test_reads_skipped_with_the_reason_alone()
    {
        var test = NewTest();
        StepCollector.StartStep(test, null, "Skips itself", null, null);
        StepCollector.CompleteStep(test, false, "$XunitDynamicSkip$not on this platform");

        var step = Assert.Single(StepCollector.GetSteps(test));

        Assert.Equal(ExecutionResult.Skipped, step.Status);
        Assert.Equal("not on this platform", step.FailureMessage);
    }

    [Fact]
    public void A_step_method_that_fails_with_those_words_further_in_its_message_still_failed()
    {
        var test = NewTest();
        StepCollector.StartStep(test, null, "Fails", null, null);
        StepCollector.CompleteStep(test, false, "expected $XunitDynamicSkip$ in the output");

        var step = Assert.Single(StepCollector.GetSteps(test));

        Assert.Equal(ExecutionResult.Failed, step.Status);
        Assert.Equal("expected $XunitDynamicSkip$ in the output", step.FailureMessage);
    }

    [Fact]
    public void LastStepIdOf_is_the_step_opened_last_and_0_with_none_opened()
    {
        var test = NewTest();
        Assert.Equal(0, StepCollector.LastStepIdOf(test));

        var frame = StepCollector.StartFrame(test, "Given", "a basket");
        Assert.Equal(frame, StepCollector.LastStepIdOf(test));

        StepCollector.CompleteFrame(test, frame, true);
        Assert.Equal(frame, StepCollector.LastStepIdOf(test));
    }

    [Fact]
    public void OpenFrameOf_is_the_framework_step_open_under_any_step_method_and_null_with_none()
    {
        var test = NewTest();
        Assert.Null(StepCollector.OpenFrameOf(test));

        var outer = StepCollector.StartFrame(test, "Given", "outer");
        var inner = StepCollector.StartFrame(test, "Given", "inner");
        StepCollector.StartStep(test, null, "a step method", null, null);
        Assert.Equal(inner, StepCollector.OpenFrameOf(test));

        StepCollector.CompleteStep(test, true);
        StepCollector.CompleteFrame(test, inner, true);
        Assert.Equal(outer, StepCollector.OpenFrameOf(test));

        StepCollector.CompleteFrame(test, outer, true);
        Assert.Null(StepCollector.OpenFrameOf(test));
    }

    [Fact]
    public void With_only_a_step_method_open_there_is_no_open_frame()
    {
        var test = NewTest();
        StepCollector.StartStep(test, null, "a step method", null, null);

        Assert.Null(StepCollector.OpenFrameOf(test));
    }
}
