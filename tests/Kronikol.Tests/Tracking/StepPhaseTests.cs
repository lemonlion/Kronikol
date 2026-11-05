using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

/// <summary>
/// #224 and #195: what moves a test's phase (plans/STEP_PHASE_PLAN.md 5.1). The outermost step open in a flow that says
/// anything about phases decides, and a keyword under it is a label. A step on a flow that is not the test's own keeps its
/// phase to itself, and a call reads what its flow holds.
/// </summary>
[Collection("DiagramsFetcher")]
public class StepPhaseTests
{
    public StepPhaseTests() => TestPhaseContext.Reset();

    private static string NewTestId() => $"step-phase-{Guid.NewGuid():N}";

    private static TestPhase PhaseOf(string keyword) => keyword is "Given" or "But" ? TestPhase.Setup : TestPhase.Action;

    /// <summary>A step attribute's call, or a hand-made step's: the public overload.</summary>
    private static void Attribute(string? id, string? keyword)
    {
        StepCollector.StartStep(id, keyword, "a step", null, null);
        OpenedBy(id).Push(null);
    }

    // What each test's open steps are, innermost first: a framework's closes by its id, and any other last in, first out.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentStack<int?>> Opened = new();

    private static System.Collections.Concurrent.ConcurrentStack<int?> OpenedBy(string? id) => Opened.GetOrAdd(id ?? "", _ => new());

    /// <summary>A framework adapter's call, which names the word for the step's phase.</summary>
    private static void Adapter(string id, string? keyword, string? phaseKeyword = null) =>
        OpenedBy(id).Push(StepCollector.StartFrame(id, keyword, "a step", phaseKeyword: phaseKeyword ?? keyword));

    private static void Done(string id, bool passed = true)
    {
        if (OpenedBy(id).TryPop(out var frame) && frame is not null)
            StepCollector.CompleteFrame(id, frame, passed);
        else
            StepCollector.CompleteStep(id, passed);
    }

    /// <summary>
    /// The phase the test holds, read as a LightBDD <c>AND</c> reads it: on a flow with nothing of the test's in it, where a
    /// flow's own value and its chain of open steps say nothing.
    /// </summary>
    private static TestPhase TestsOwnPhase(string id)
    {
        var phase = TestPhase.Unknown;

        // A thread of its own: a waited Task.Run can run inline on the waiter, under the waiter's flow.
        var thread = new Thread(() =>
        {
            Adapter(id, "AND");
            phase = TestPhaseContext.Current;
            Done(id);
        });
        thread.UnsafeStart();
        thread.Join();
        return phase;
    }

    public static TheoryData<string, string> EveryPairOfKeywords()
    {
        string[] keywords = ["Given", "When", "Then", "But", "ButWhen"];
        var data = new TheoryData<string, string>();
        foreach (var outer in keywords)
            foreach (var inner in keywords)
                data.Add(outer, inner);
        return data;
    }

    // ---- A keyword under a keyword ----

    [Fact]
    public void A_Given_under_a_When_leaves_the_phase_at_Action()
    {
        var id = NewTestId();

        Attribute(id, "When");
        Attribute(id, "Given");

        Assert.Equal(TestPhase.Action, TestPhaseContext.Current);
    }

    [Fact]
    public void A_When_under_a_Given_leaves_it_at_Setup()
    {
        var id = NewTestId();

        Attribute(id, "Given");
        Attribute(id, "When");

        Assert.Equal(TestPhase.Setup, TestPhaseContext.Current);
    }

    [Theory]
    [MemberData(nameof(EveryPairOfKeywords))]
    public void A_keyword_under_a_keyword_is_a_label_while_it_runs_and_after(string outer, string inner)
    {
        var id = NewTestId();

        Attribute(id, outer);
        Attribute(id, inner);
        Assert.Equal(PhaseOf(outer), TestPhaseContext.Current);

        Done(id);
        Assert.Equal(PhaseOf(outer), TestPhaseContext.Current);

        Done(id);
        Assert.Equal(PhaseOf(outer), TestPhaseContext.Current);
        Assert.Equal(PhaseOf(outer), TestsOwnPhase(id));
    }

    [Theory]
    [InlineData("Given", "When", "Then")]
    [InlineData("When", "Given", "Given")]
    public void The_next_top_level_step_moves_the_phase_after_a_nested_one_ended(string outer, string inner, string next)
    {
        var id = NewTestId();

        Attribute(id, outer);
        Attribute(id, inner);
        Done(id);
        Done(id);
        Attribute(id, next);

        Assert.Equal(PhaseOf(next), TestPhaseContext.Current);
    }

    [Fact]
    public void A_nested_step_that_fails_leaves_the_phase_alone()
    {
        var id = NewTestId();

        Attribute(id, "When");
        Attribute(id, "Given");
        StepCollector.CompleteStep(id, passed: false, "it failed");

        Assert.Equal(TestPhase.Action, TestPhaseContext.Current);
    }

    [Fact]
    public void A_nested_step_that_is_bypassed_leaves_the_phase_alone()
    {
        var id = NewTestId();

        Attribute(id, "Given");
        Attribute(id, "When");
        StepCollector.BypassStep(id, "skipped");

        Assert.Equal(TestPhase.Setup, TestPhaseContext.Current);
    }

    [Fact]
    public void A_keyword_after_a_nested_step_ended_is_still_a_label()
    {
        var id = NewTestId();

        Attribute(id, "When");
        Attribute(id, "Given");
        Done(id);
        Attribute(id, "Given");

        Assert.Equal(TestPhase.Action, TestPhaseContext.Current);
    }

    // Q9: a bypassed When moves the phase, as it does today (X15).
    [Fact]
    public void A_bypassed_top_level_When_moves_the_phase()
    {
        var id = NewTestId();

        Attribute(id, "When");
        StepCollector.BypassStep(id, "skipped");

        Assert.Equal(TestPhase.Action, TestPhaseContext.Current);
    }

    // ---- A step with no keyword says nothing ----

    [Theory]
    [InlineData("Given", "When")]
    [InlineData("When", "Given")]
    [InlineData("Then", "But")]
    [InlineData("ButWhen", "Given")]
    public void A_keyword_under_a_step_with_no_keyword_moves_the_phase(string first, string second)
    {
        var id = NewTestId();

        Attribute(id, null);
        Attribute(id, first);
        Assert.Equal(PhaseOf(first), TestPhaseContext.Current);
        Done(id);

        Attribute(id, second);
        Assert.Equal(PhaseOf(second), TestPhaseContext.Current);
    }

    [Fact]
    public void A_When_under_a_Given_under_a_step_with_no_keyword_is_a_label()
    {
        var id = NewTestId();

        Attribute(id, "Given");
        Attribute(id, null);
        Attribute(id, "When");

        Assert.Equal(TestPhase.Setup, TestPhaseContext.Current);
    }

    // ---- An adapter's word ----

    [Theory]
    [InlineData("GIVEN", TestPhase.Setup)]
    [InlineData("WHEN", TestPhase.Action)]
    [InlineData("THEN", TestPhase.Action)]
    [InlineData("SETUP", TestPhase.Setup)]
    [InlineData("setup", TestPhase.Setup)]
    [InlineData("given", TestPhase.Setup)]
    public void Upper_case_keywords_move_the_phase(string word, TestPhase expected)
    {
        var id = NewTestId();

        Adapter(id, word);

        Assert.Equal(expected, TestPhaseContext.Current);
    }

    [Theory]
    [InlineData("GIVEN", TestPhase.Setup)]
    [InlineData("WHEN", TestPhase.Action)]
    public void And_continues_the_step_before_it(string before, TestPhase expected)
    {
        var id = NewTestId();
        Adapter(id, before);
        Done(id);

        // Each LightBDD step runs in an async frame of its own, so the flow holds nothing of the step before.
        TestPhaseContext.Reset();
        Adapter(id, "AND");

        Assert.Equal(expected, TestPhaseContext.Current);
    }

    [Fact]
    public void An_And_with_nothing_before_it_says_nothing()
    {
        var id = NewTestId();

        Adapter(id, "AND");
        Assert.Equal(TestPhase.Unknown, TestPhaseContext.Current);

        // So a typed step under it decides (L15).
        Adapter(id, "GIVEN");
        Assert.Equal(TestPhase.Setup, TestPhaseContext.Current);
    }

    [Fact]
    public void An_And_reads_a_phase_its_flow_wrote()
    {
        var id = NewTestId();
        TestPhaseContext.Current = TestPhase.Action;

        Adapter(id, "AND");
        Assert.Equal(TestPhase.Action, TestPhaseContext.Current);

        // And that And then says a phase, so what is under it is a label.
        Adapter(id, "GIVEN");
        Assert.Equal(TestPhase.Action, TestPhaseContext.Current);
    }

    [Theory]
    [InlineData("GIVEN")]
    [InlineData("WHEN")]
    [InlineData("And")]
    [InlineData("Setup")]
    [InlineData("given")]
    [InlineData("Because")]
    public void Through_the_public_overloads_only_todays_five_words_say_anything(string word)
    {
        var id = NewTestId();

        Attribute(id, word);
        Assert.Equal(TestPhase.Unknown, TestPhaseContext.Current);

        // It said nothing, so what is under it decides.
        Attribute(id, "When");
        Assert.Equal(TestPhase.Action, TestPhaseContext.Current);
    }

    [Fact]
    public void A_word_that_says_nothing_moves_nothing_and_leaves_the_decision_to_the_keywords_under_it()
    {
        var id = NewTestId();

        Adapter(id, "Because");
        Assert.Equal(TestPhase.Unknown, TestPhaseContext.Current);

        Adapter(id, "GIVEN");
        Assert.Equal(TestPhase.Setup, TestPhaseContext.Current);
    }

    [Theory]
    [InlineData("And", "When", TestPhase.Action)]
    [InlineData("But", "Then", TestPhase.Action)]
    [InlineData("And", "Given", TestPhase.Setup)]
    [InlineData("*", "Given", TestPhase.Setup)]
    public void The_step_list_keeps_the_keyword_as_written_and_the_phase_is_the_adapters_word(string written, string phaseWord, TestPhase expected)
    {
        var id = NewTestId();

        Adapter(id, written, phaseWord);
        Assert.Equal(expected, TestPhaseContext.Current);
        Done(id);

        Assert.Equal(written, StepCollector.GetSteps(id).Single().Keyword);
    }

    [Fact]
    public void A_step_attribute_under_an_adapters_step_says_nothing()
    {
        var id = NewTestId();

        Adapter(id, "WHEN");
        Attribute(id, "Given");

        Assert.Equal(TestPhase.Action, TestPhaseContext.Current);
    }

    // ---- WhenTriggersAction, which is about step attributes ----

    private static void WithoutStepAttributePhases(Action act)
    {
        var options = StepCollector.Options;
        try
        {
            StepCollector.Options = new StepTrackingOptions { WhenTriggersAction = false };
            act();
        }
        finally
        {
            StepCollector.Options = options;
        }
    }

    [Fact]
    public void An_adapters_word_holds_with_WhenTriggersAction_off() => WithoutStepAttributePhases(() =>
    {
        var id = NewTestId();

        Adapter(id, "When");
        Assert.Equal(TestPhase.Action, TestPhaseContext.Current);

        Attribute(id, "Given");
        Assert.Equal(TestPhase.Action, TestPhaseContext.Current);
    });

    [Fact]
    public void With_WhenTriggersAction_off_a_step_attribute_moves_nothing() => WithoutStepAttributePhases(() =>
    {
        var id = NewTestId();

        Attribute(id, "When");
        Assert.Equal(TestPhase.Unknown, TestPhaseContext.Current);

        Attribute(id, "Given");
        Assert.Equal(TestPhase.Unknown, TestPhaseContext.Current);
    });

    [Fact]
    public void With_WhenTriggersAction_off_a_step_attribute_leaves_the_decision_to_an_adapter_under_it() => WithoutStepAttributePhases(() =>
    {
        var id = NewTestId();

        Attribute(id, "When");
        Adapter(id, "GIVEN");

        Assert.Equal(TestPhase.Setup, TestPhaseContext.Current);
    });

    // ---- A phase the test set itself ----

    [Fact]
    public void An_And_after_an_explicit_StartAction_continues_Action()
    {
        var id = NewTestId();
        Adapter(id, "GIVEN");
        Done(id);

        DefaultTrackingDiagramOverride.StartAction(id);
        TestPhaseContext.Reset();
        Adapter(id, "AND");

        Assert.Equal(TestPhase.Action, TestPhaseContext.Current);
    }

    [Fact]
    public void An_And_after_an_explicit_StartSetup_continues_Setup()
    {
        var id = NewTestId();
        Adapter(id, "WHEN");
        Done(id);

        DefaultTrackingDiagramOverride.StartSetup(id);
        TestPhaseContext.Reset();
        Adapter(id, "AND");

        Assert.Equal(TestPhase.Setup, TestPhaseContext.Current);
    }

    [Fact]
    public void A_flows_own_write_lasts_until_the_next_step_that_decides()
    {
        var id = NewTestId();
        Attribute(id, "When");
        Done(id);

        TestPhaseContext.Current = TestPhase.Setup;
        Assert.Equal(TestPhase.Setup, TestPhaseContext.Current);

        Attribute(id, "Then");
        Assert.Equal(TestPhase.Action, TestPhaseContext.Current);
    }

    // ---- The steps open in a flow ----

    [Fact]
    public async Task A_step_on_another_flow_is_not_above_a_When_started_here()
    {
        var id = NewTestId();
        await Task.Run(() => Attribute(id, "Given"), TestContext.Current.CancellationToken);

        Attribute(id, "When");

        Assert.Equal(TestPhase.Action, TestPhaseContext.Current);
    }

    [Fact]
    public void Each_step_still_nests_on_the_tests_own_stack()
    {
        var id = NewTestId();

        Attribute(id, "When");
        Attribute(id, "Given");
        Done(id);
        Done(id);

        var step = StepCollector.GetSteps(id).Single();
        Assert.Equal("When", step.Keyword);
        Assert.Equal("Given", step.SubSteps!.Single().Keyword);
    }

    [Fact]
    public void A_keyword_on_a_flow_forked_inside_a_closed_step_moves_nothing_of_the_tests()
    {
        var id = NewTestId();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = TestPhase.Unknown;

        Attribute(id, "Given");
        var forked = Task.Run(async () =>
        {
            await gate.Task;
            Attribute(id, "Then");
            read = TestPhaseContext.Current;
            Done(id);
        }, TestContext.Current.CancellationToken);
        Done(id);

        gate.SetResult();
        forked.GetAwaiter().GetResult();

        // In that flow it reads as its own step, as it does today, and the test stays where its own steps put it (review/C01).
        Assert.Equal(TestPhase.Action, read);
        Assert.Equal(TestPhase.Setup, TestsOwnPhase(id));
    }

    [Fact]
    public void A_keyword_under_Detach_moves_nothing_of_the_tests()
    {
        var id = NewTestId();
        Attribute(id, "Given");
        Done(id);

        var read = TestPhase.Unknown;
        using (TestIdentityScope.Begin(id, id))
        using (TestIdentityScope.Detach())
        {
            StepCollector.StartStep(id, "When", "detached", null, null);
            read = TestPhaseContext.Current;
            Done(id);
        }

        Assert.Equal(TestPhase.Action, read);
        Assert.Equal(TestPhase.Setup, TestsOwnPhase(id));
    }

    // A thread, or a server's side of a call, with only a TestIdentityScope (matrix/T01, N30, S31): the framework names no test there.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_keyword_on_a_flow_the_framework_does_not_name_moves_nothing_of_the_tests(bool resolverThrows)
    {
        var id = NewTestId();
        Attribute(id, "Given");
        Done(id);

        var resolver = Track.TestIdResolver;
        try
        {
            Track.TestIdResolver = resolverThrows ? () => throw new InvalidOperationException("no test here") : () => null;
            Attribute(id, "When");
            Assert.Equal(TestPhase.Action, TestPhaseContext.Current);
            Done(id);
        }
        finally
        {
            Track.TestIdResolver = resolver;
        }

        Assert.Equal(TestPhase.Setup, TestsOwnPhase(id));
    }

    [Fact]
    public void A_keyword_on_a_flow_the_framework_names_moves_the_test()
    {
        var id = NewTestId();
        Attribute(id, "Given");
        Done(id);

        var resolver = Track.TestIdResolver;
        try
        {
            Track.TestIdResolver = () => id;
            Attribute(id, "When");
            Done(id);
        }
        finally
        {
            Track.TestIdResolver = resolver;
        }

        Assert.Equal(TestPhase.Action, TestsOwnPhase(id));
    }

    // With no resolver a scope is all a suite has, as a plain suite of step attributes has, and it is the test's own.
    [Fact]
    public void A_keyword_with_no_resolver_at_all_moves_the_test()
    {
        var id = NewTestId();
        var resolver = Track.TestIdResolver;
        try
        {
            Track.TestIdResolver = null;
            Attribute(id, "When");
            Done(id);
        }
        finally
        {
            Track.TestIdResolver = resolver;
        }

        Assert.Equal(TestPhase.Action, TestsOwnPhase(id));
    }

    [Fact]
    public void An_adapters_word_moves_the_test_on_any_flow_as_the_adapter_named_it()
    {
        var id = NewTestId();
        var resolver = Track.TestIdResolver;
        try
        {
            Track.TestIdResolver = () => null;
            Adapter(id, "WHEN");
            Done(id);
        }
        finally
        {
            Track.TestIdResolver = resolver;
        }

        Assert.Equal(TestPhase.Action, TestsOwnPhase(id));
    }

    [Fact]
    public void A_hand_step_for_another_test_inside_a_Given_does_not_lose_this_tests_chain()
    {
        var id = NewTestId();
        var other = NewTestId();

        Attribute(id, "Given");
        Attribute(other, "When");
        Done(other);
        Attribute(id, "When");

        // The other test's When says its phase in this flow, as a step for any test does (review4/F05). This test's When is a label.
        Assert.Equal(TestPhase.Setup, TestsOwnPhase(id));
    }

    [Fact]
    public void A_hand_step_for_another_test_closed_after_this_tests_step_leaves_its_own_node()
    {
        var id = NewTestId();
        var other = NewTestId();

        Attribute(other, "When");
        Attribute(id, "Given");
        Done(other);

        // The Given is still open in this flow, so a When under it is a label (review6/I02, I03).
        Attribute(id, "When");
        Assert.Equal(TestPhase.Setup, TestPhaseContext.Current);
    }

    [Fact]
    public void A_nested_step_with_no_test_id_leaves_its_parent_on_the_chain()
    {
        var id = NewTestId();

        Attribute(id, "Given");
        Attribute(null, "When");
        StepCollector.CompleteStep(null, passed: true);
        Attribute(id, "When");

        Assert.Equal(TestPhase.Setup, TestPhaseContext.Current);
    }

    [Fact]
    public async Task A_hand_step_closed_on_another_flow_takes_the_step_the_stack_closed_off_this_flows_chain()
    {
        var id = NewTestId();

        Attribute(id, "Given");
        var below = TestPhaseContext.Current;
        Attribute(id, "Then");

        // The completion on the other flow took the step on the stack's top, the Then, and cannot reach this flow's chain.
        await Task.Run(() => Done(id), TestContext.Current.CancellationToken);

        // Here the Given completes, which the stack gives to the Given, and the chain must lose both (review5/R09b).
        Done(id);
        Attribute(id, "When");

        Assert.Equal(TestPhase.Setup, below);
        Assert.Equal(TestPhase.Action, TestPhaseContext.Current);
    }

    [Fact]
    public async Task A_flow_that_outlived_a_step_it_was_forked_in_can_read_its_own_phase_without_moving_the_test()
    {
        var id = NewTestId();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Attribute(id, "When");
        var forked = Task.Run(async () =>
        {
            await gate.Task;
            Attribute(id, "Given");
            var read = TestPhaseContext.Current;
            Done(id);
            return read;
        }, TestContext.Current.CancellationToken);
        Done(id);
        gate.SetResult();

        Assert.Equal(TestPhase.Setup, await forked);
        Assert.Equal(TestPhase.Action, TestsOwnPhase(id));
    }

    // ---- A bypassed step has closed already ----

    // The check in OutlivedStepSaysPhase (mut/X92, review6/K08): a step bypassed from another flow stays on this one's chain, and says nothing of it.
    [Fact]
    public async Task A_step_bypassed_on_another_flow_does_not_make_this_flow_one_that_outlived_it()
    {
        var id = NewTestId();
        Attribute(id, "Given");
        await Task.Run(() => StepCollector.BypassStep(id, "elsewhere"), TestContext.Current.CancellationToken);

        Attribute(id, "When");

        Assert.Equal(TestPhase.Action, TestsOwnPhase(id));
    }

    // The check in LeaveCompletedStep's walk (X37, review5/H13, review/A02): a stub's completion leaves its own step, not the bypassed one above it,
    // which stays on this flow when it was bypassed on another.
    [Fact]
    public async Task A_stubs_completion_leaves_its_own_step_past_a_step_bypassed_by_hand()
    {
        var id = NewTestId();
        using var scope = TestIdentityScope.Begin(id, id);
        Attribute(id, "Given");
        Attribute(id, null);
        await Task.Run(() => StepCollector.BypassStep(id, "by hand"), TestContext.Current.CancellationToken);

        await StepCollector.CompleteStepAsync(Task.CompletedTask);
        Attribute(id, "When");

        Assert.Equal(TestPhase.Action, TestsOwnPhase(id));
    }

    // review5/R09b: the hand step closes on another flow, so this flow's completion leaves the hand step's node and the stack closes the framework's
    // step. Left on the chain, that step would read as one this flow outlived, and no keyword after it would move the test.
    [Fact]
    public async Task A_hand_step_closed_on_another_flow_leaves_nothing_of_the_framework_step_on_this_one()
    {
        var id = NewTestId();
        using var scope = TestIdentityScope.Begin(id, id);
        Adapter(id, "Given");
        Attribute(id, "Given");
        await Task.Run(() => Done(id), TestContext.Current.CancellationToken);
        Done(id);

        Adapter(id, "When");

        Assert.Equal(TestPhase.Action, TestsOwnPhase(id));
    }

    // The fix-up took the chain back to that step's parent, and another test's step above it, still open, went with it.
    [Fact]
    public async Task A_close_the_stack_gave_another_step_leaves_another_tests_step_on_this_flow()
    {
        var id = NewTestId();
        var other = NewTestId();
        using var scope = TestIdentityScope.Begin(id, id);
        Adapter(id, "Given");
        Attribute(id, null);
        Attribute(other, "Given");
        await Task.Run(() => Done(id), TestContext.Current.CancellationToken);
        Done(id);

        Attribute(other, "When");

        Assert.Equal(TestPhase.Setup, TestsOwnPhase(other));
    }

    // Another test's step on this flow does not hide this test's open step under it.
    [Fact]
    public void A_keyword_above_another_tests_step_above_an_open_When_is_a_label()
    {
        var id = NewTestId();
        var other = NewTestId();
        using var scope = TestIdentityScope.Begin(id, id);
        Attribute(id, "When");
        Attribute(other, null);

        Attribute(id, "Given");

        Assert.Equal(TestPhase.Action, TestsOwnPhase(id));
    }

    // Nor does a step closed under it stay as one this flow outlived.
    [Fact]
    public void A_close_under_another_tests_step_lets_the_next_keyword_decide()
    {
        var id = NewTestId();
        var other = NewTestId();
        using var scope = TestIdentityScope.Begin(id, id);
        var given = StepCollector.StartFrame(id, "Given", "a step", phaseKeyword: "Given");
        Attribute(other, null);
        StepCollector.CompleteFrame(id, given, passed: true);

        Adapter(id, "When");

        Assert.Equal(TestPhase.Action, TestsOwnPhase(id));
    }

    // review4/F7: each LightBDD step runs in an async frame of its own, so all the flow holds is what was written before the runner.
    [Theory]
    [InlineData(TestPhase.Setup, "WHEN", TestPhase.Action)]
    [InlineData(TestPhase.Action, "GIVEN", TestPhase.Setup)]
    public void An_And_continues_the_tests_steps_and_not_what_the_flow_held_before_them(TestPhase written, string before, TestPhase expected)
    {
        var id = NewTestId();
        TestPhaseContext.Current = written;
        Frame(before);

        var and = StepCollector.StartFrame(id, "AND", "a step", phaseKeyword: "AND");
        var read = TestPhaseContext.Current;
        StepCollector.CompleteFrame(id, and, passed: true);

        Assert.Equal(expected, read);

        void Frame(string keyword)
        {
            var saved = TestPhaseContext.Current;
            var opened = StepCollector.StartFrame(id, keyword, "a step", phaseKeyword: keyword);
            StepCollector.CompleteFrame(id, opened, passed: true);
            TestPhaseContext.Current = saved;
        }
    }

    // Before any step of the test's has said a phase the flow is all there is, as an explicit word by hand before the first And.
    [Fact]
    public void An_And_with_no_step_before_it_continues_what_the_flow_holds()
    {
        var id = NewTestId();
        TestPhaseContext.Current = TestPhase.Action;

        Adapter(id, "AND");

        Assert.Equal(TestPhase.Action, TestPhaseContext.Current);
    }

    // review4/F8: leaving its first keyword step must not make it a flow the test owns: main is still in its Given.
    [Fact]
    public async Task A_flow_forked_in_a_Given_keeps_each_of_its_keywords_to_itself()
    {
        var id = NewTestId();
        using var scope = TestIdentityScope.Begin(id, id);
        var given = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = new List<TestPhase>();
        Attribute(id, "Given");
        var forked = Task.Run(async () =>
        {
            await given.Task;
            for (var tick = 0; tick < 3; tick++)
            {
                StepCollector.StartStep(id, "When", "a tick", null, null);
                read.Add(TestPhaseContext.Current);
                StepCollector.CompleteStep(id, passed: true);
            }
        }, TestContext.Current.CancellationToken);
        Done(id);

        given.SetResult();
        await forked;

        Assert.Equal([TestPhase.Action, TestPhase.Action, TestPhase.Action], read);
        Assert.Equal(TestPhase.Setup, TestsOwnPhase(id));
    }

    // ---- What an adapter of an earlier release calls ----

    // review4/F4: a package can move ahead of another, and a framework's every step goes through this.
    [Fact]
    public void A_frame_opens_through_the_signature_an_adapter_of_an_earlier_release_binds_to()
    {
        var method = typeof(StepCollector).GetMethod("StartFrame", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic,
            [typeof(string), typeof(string), typeof(string), typeof(string[][]), typeof(string)]);

        Assert.NotNull(method);

        var id = NewTestId();
        var frame = (int?)method.Invoke(null, [id, "When", "a step", null, null]);
        Assert.Equal(TestPhase.Action, TestPhaseContext.Current);
        StepCollector.CompleteFrame(id, frame, passed: true);
        Assert.False(StepCollector.HasActiveStep(id));
    }

    // ---- A step cleared while open ----

    // review1/H03: it is not open, and the next keyword decides.
    [Fact]
    public void ClearSteps_with_a_Given_open_leaves_the_next_keyword_to_decide()
    {
        var id = NewTestId();
        using var scope = TestIdentityScope.Begin(id, id);
        Attribute(id, "Given");

        StepCollector.ClearSteps(id);
        Attribute(id, "When");

        Assert.Equal(TestPhase.Action, TestsOwnPhase(id));
    }

    [Fact]
    public async Task ClearSteps_on_another_flow_leaves_no_step_open_on_this_one()
    {
        var id = NewTestId();
        using var scope = TestIdentityScope.Begin(id, id);
        Attribute(id, "Given");
        await Task.Run(() => StepCollector.ClearSteps(id), TestContext.Current.CancellationToken);

        Attribute(id, "When");

        Assert.Equal(TestPhase.Action, TestPhaseContext.Current);
    }

    // ---- What a long run costs ----

    // review1/C: a skipped step is over where it is skipped. Left on the chain, a loop of them was walked at each close and rebuilt by
    // recursion, which overflowed the stack at about 20,000, and that takes the test process and the run's report with it.
    [Fact]
    public void A_long_loop_of_skipped_steps_under_an_open_step_costs_no_stack()
    {
        var id = NewTestId();
        using var scope = TestIdentityScope.Begin(id, id);
        Attribute(id, null);
        for (var i = 0; i < 100_000; i++)
        {
            Attribute(id, null);
            StepCollector.BypassStep(id, "skipped");
        }

        Done(id);

        Assert.False(StepCollector.HasActiveStep(id));
        Assert.Equal(100_000, StepCollector.GetSteps(id).Single().SubSteps!.Length);
        StepCollector.ClearSteps(id);
    }

    // review1/E: and it is not walked again at each keyword. Left on the chain, 20,000 took 3.5 s, and the time goes up with the square of the count.
    [Fact]
    public void A_long_loop_of_skipped_keyword_steps_costs_no_more_each_time()
    {
        var id = NewTestId();
        using var scope = TestIdentityScope.Begin(id, id);
        Attribute(id, null);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 100_000; i++)
        {
            Attribute(id, "Given");
            StepCollector.BypassStep(id, "skipped");
        }

        var elapsed = clock.Elapsed;
        StepCollector.ClearSteps(id);

        Assert.InRange(elapsed.TotalSeconds, 0, 30);
    }

    [Fact]
    public void A_long_run_of_another_tests_steps_above_a_closing_step_costs_no_stack()
    {
        var id = NewTestId();
        var other = NewTestId();
        using var scope = TestIdentityScope.Begin(id, id);
        Attribute(id, null);
        for (var i = 0; i < 100_000; i++)
            Attribute(other, null);

        Done(id);

        Assert.False(StepCollector.HasActiveStep(id));
        StepCollector.ClearSteps(id);
        StepCollector.ClearSteps(other);
    }

    // ---- A step with no test id ----

    [Fact]
    public void A_step_with_no_test_id_writes_no_phase()
    {
        Attribute(null, "When");

        Assert.Equal(TestPhase.Unknown, TestPhaseContext.Current);
    }

    // ---- A step never completed (plans/STEP_PHASE_PLAN.md 9): a consumer's bug, which pins its flow ----

    [Fact]
    public void A_step_never_completed_pins_its_flow()
    {
        var id = NewTestId();

        Attribute(id, "Given");
        Attribute(id, "When");

        Assert.Equal(TestPhase.Setup, TestPhaseContext.Current);
    }

    [Fact]
    public void The_steps_recorded_status_is_unchanged_by_the_phase_rule()
    {
        var id = NewTestId();

        Attribute(id, "When");
        Attribute(id, "Given");
        StepCollector.CompleteStep(id, passed: false, "sub-step failed");
        Done(id);

        var when = StepCollector.GetSteps(id).Single();
        Assert.Equal(ExecutionResult.Passed, when.Status);
        Assert.Equal(ExecutionResult.Failed, when.SubSteps!.Single().Status);
    }
}
