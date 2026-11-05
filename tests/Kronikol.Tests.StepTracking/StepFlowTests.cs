using System.Reflection;
using Kronikol.Reports;
using Kronikol.StepTracking;
using Kronikol.Tracking;

namespace Kronikol.Tests.StepTracking;

/// <summary>
/// Steps that overlap, are left running or are skipped, woven and run for real: which step a flow is in is decided by the woven
/// stub's own frame, so a hand-called StartStep would not reach the rule. Each shape reads the tree a report would list.
/// </summary>
public class StepFlowTests
{
    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.IO;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using Kronikol.Tracking;

        [assembly: TrackSteps]

        public class Steps
        {
            public string Id;
            public bool Skip => true;
            private Task _late;
            private readonly TaskCompletionSource _go = new();
            private readonly TaskCompletionSource _never = new();

            // Two started together
            public async Task TwoAtOnce() => await Together();
            [Step] async Task Together() => await Task.WhenAll(Slow(), Fast());
            [Step] async Task Slow() { await _go.Task; await Task.Delay(5); }
            [Step] async Task Fast() { await Task.Yield(); _go.SetResult(); }

            // One of two fails
            public async Task OneOfTwoFails() { try { await Holds(); } catch (InvalidOperationException) { } }
            [Step] async Task Holds() => await Task.WhenAll(Good(), Bad());
            [Step] async Task Good() { await _go.Task; }
            [Step] async Task Bad() { await Task.Yield(); _go.SetResult(); throw new InvalidOperationException("Bad went wrong"); }

            // A sub-step left running as its parent ends
            public async Task ChildOutlivesParent() { await Parent(); await Next(); _go.SetResult(); await _late; }
            [Step] async Task Parent() { _late = Child(); await Task.Yield(); }
            [Step] async Task Child() { await _go.Task; }
            [Step] async Task Next() { await Task.Yield(); }

            // A straggler starts a step
            public async Task Straggler() { await Starts(); await Open(); }
            [Step] async Task Starts() { _late = Task.Run(async () => { await _go.Task; await Late(); }); await Task.Yield(); }
            [Step] async Task Late() { await Task.Yield(); }
            [Step] async Task Open() { _go.SetResult(); await _late; }

            // A step opened by hand in a helper, whose flow does not reach the caller
            public async Task OpenedInAHelper() { await Begin(); await Task.Yield(); StepCollector.CompleteStep(Id, false, "setup failed"); }
            private async Task Begin() { StepCollector.StartStep(Id, "Given", "opened in a helper", null, null); await Task.Yield(); }

            // A stub with no test
            public async Task NoIdentity() => await HoldsOpen();
            [Step] async Task HoldsOpen()
            {
                Task other;
                using (ExecutionContext.SuppressFlow())
                    other = Task.Run(NoTestHere);
                await other;
                await Task.Yield();
            }
            [Step] async Task NoTestHere() => await Task.Yield();

            // Top-level steps at once
            public async Task TopLevelAtOnce() { var a = TopFirst(); var b = TopSecond(); await Task.WhenAll(a, b); }
            [Step] async Task TopFirst() { await _go.Task; }
            [Step] async Task TopSecond() { await Task.Yield(); _go.SetResult(); }

            // An assertion from a flow whose step has ended
            public async Task LateAssertion() { await StartsAsserter(); await StillOpen(); }
            [Step] async Task StartsAsserter()
            {
                _late = Task.Run(async () => { await _go.Task; StepCollector.AddAssertionSubStep(Id, "late one", false, "it was late"); });
                await Task.Yield();
            }
            [Step] async Task StillOpen() { _go.SetResult(); await _late; }

            // Left open when the report is drawn
            public async Task Hangs() { _ = Forever(); await Task.Yield(); }
            [Step] async Task Forever() { await _never.Task; }

            public async Task ParentGivesUp() => await Gives();
            [Step] async Task Gives() => await Task.WhenAny(Quick(), Forever());
            [Step] async Task Quick() => await Task.Yield();

            // Durations
            public async Task Durations() => await Times();
            [Step] async Task Times() => await Task.WhenAll(Longer(), Shorter());
            [Step] async Task Longer() { await _go.Task; await Task.Delay(5); }
            [Step] async Task Shorter() { await Task.Yield(); _go.SetResult(); }

            // An assertion and an attachment belong to the flow that made them
            public async Task EachItsOwn() => await Both();
            [Step] async Task Both() => await Task.WhenAll(First(), Second());
            [Step] async Task First()
            {
                await _go.Task;
                StepCollector.AddAssertionSubStep(Id, "in first", true);
                StepCollector.AddAttachment(Id, Attachment(), "from first");
            }
            [Step] async Task Second() { await Task.Yield(); _go.SetResult(); await Task.Delay(30); }
            private static string Attachment() { var path = Path.GetTempFileName(); File.WriteAllText(path, "x"); return path; }

            // A flow with no step of this test
            public async Task FromASuppressedFlow() => await Waits();
            [Step] async Task Waits()
            {
                var id = Id;
                Task other;
                using (ExecutionContext.SuppressFlow())
                    other = Task.Run(() => { using var _ = TestIdentityScope.Begin(id, id); OnOtherFlow(); StepCollector.AddAssertionSubStep(id, "elsewhere", true); });
                await other;
            }
            [Step] void OnOtherFlow() { }

            // The same beside a sub-step left running: the step its caller has not moved on from is the parent
            public Task BesideASubStepLeftRunning() { ParentOfALeftStep(); return Task.CompletedTask; }
            [Step] void ParentOfALeftStep()
            {
                var left = LeftSub();
                var id = Id;
                // A thread, as Task.Wait can run a task on the thread waiting for it, in that thread's flow.
                Thread other;
                using (ExecutionContext.SuppressFlow())
                {
                    other = new Thread(() => { using var _ = TestIdentityScope.Begin(id, id); OnOtherFlow(); });
                    other.Start();
                }
                other.Join();
                _go.SetResult();
                left.Wait();
            }
            [Step] async Task LeftSub() => await _go.Task;

            // A flow in a scope of its own that has left its steps: its next step is no child of a woven step running elsewhere
            public async Task OwnScopeLeftItsSteps()
            {
                await EarlierInTheBody();
                var id = Id;
                var first = new TaskCompletionSource();
                var opened = new TaskCompletionSource();
                Task other;
                using (ExecutionContext.SuppressFlow())
                    other = Task.Run(async () =>
                    {
                        using var _ = TestIdentityScope.Begin(id, id);
                        await FirstOfItsOwn();
                        first.SetResult();
                        await opened.Task;
                        await SecondOfItsOwn();
                    });
                await first.Task;
                var running = RunningElsewhere();
                opened.SetResult();
                await other;
                _go.SetResult();
                await running;
            }
            [Step] async Task EarlierInTheBody() => await Task.Yield();
            [Step] async Task FirstOfItsOwn() => await Task.Yield();
            [Step] async Task SecondOfItsOwn() => await Task.Yield();
            [Step] async Task RunningElsewhere() => await _go.Task;

            // A spare completion by id, beside a step left running
            public async Task SpareBesideALeftStep()
            {
                var left = LeftToItself();
                StepCollector.CompleteStep(Id, false, "spare");
                _go.SetResult();
                await left;
            }
            [Step] async Task LeftToItself() => await _go.Task;

            // A step that bypasses itself by its id, under a parent
            public async Task BypassesItselfByIdUnderAParent() { await ParentOfABypass(); await NextAfterTheParent(); }
            [Step] async Task ParentOfABypass() { await Task.Yield(); BypassedItselfById(); AfterTheBypass(); }
            [Step] void BypassedItselfById() => StepCollector.BypassStep(Id, "not needed");
            [Step] void AfterTheBypass() { }
            [Step] async Task NextAfterTheParent() => await Task.Yield();

            // A stretch under another test's id, inside a step: the flow comes back to its step
            public async Task UnderAnotherTestsId() { await Seeds(); await AfterTheSeed(); }
            [Step] async Task Seeds()
            {
                await Task.Yield();
                var other = Id + "-other";
                StepCollector.StartStep(other, "Given", "another test's step", null, null);
                StepCollector.CompleteStep(other, true);
                using (TestIdentityScope.Begin(other, other))
                    await Seeding();
                await SubAfterTheSeed();
                SyncSubAfterTheSeed();
            }
            [Step] async Task Seeding() => await Task.Yield();
            [Step] async Task SubAfterTheSeed() => await Task.Yield();
            [Step] void SyncSubAfterTheSeed() { }
            [Step] async Task AfterTheSeed() => await Task.Yield();

            // A step opened and bypassed by hand inside a sync step: the step around it closes as usual
            public async Task HandBypassInASyncStep() { HandOpensAndBypasses(); await AfterTheHandBypass(); }
            [Step] void HandOpensAndBypasses() { StepCollector.StartStep(null, "a hand's", null, null); StepCollector.BypassStep("why"); }
            public async Task HandBypassByIdInASyncStep() { HandOpensByIdAndBypasses(); await AfterTheHandBypass(); }
            [Step] void HandOpensByIdAndBypasses() { StepCollector.StartStep(Id, null, "a hand's", null, null); StepCollector.BypassStep("why"); }
            public async Task HandBypassFromAnotherFlow() { HandOpensAnotherFlowBypasses(); await AfterTheHandBypass(); }
            [Step] void HandOpensAnotherFlowBypasses()
            {
                StepCollector.StartStep(null, "a hand's", null, null);
                var id = Id;
                Thread other;
                using (ExecutionContext.SuppressFlow())
                {
                    other = new Thread(() => StepCollector.BypassStep(id, "why"));
                    other.Start();
                }
                other.Join();
            }
            [Step] async Task AfterTheHandBypass() => await Task.Yield();

            // A hand's pair after a hand's bypass: the completion closes the step it opened, and the parent stays open
            public async Task HandPairAfterABypass() { await ParentOfTheHandPair(); }
            [Step] async Task ParentOfTheHandPair()
            {
                await Task.Yield();
                StepCollector.StartStep(null, "bypassed by hand", null, null);
                StepCollector.BypassStep("why");
                StepCollector.StartStep(null, "closed by hand", null, null);
                StepCollector.CompleteStep(true);
                SubAfterTheHandPair();
            }
            [Step] void SubAfterTheHandPair() { }

            // A step that throws at once, from no await of its own
            public async Task ThrowsAtOnceThenNext()
            {
                try { await ThrowsAtOnce(); } catch (InvalidOperationException) { }
                await Next();
            }
            [Step] Task ThrowsAtOnce() => throw new InvalidOperationException("at once");

            // Task<T>
            public async Task<int> Generic() { var n = await Gen(); return n + await Gen2(); }
            [Step] async Task<int> Gen() { var kid = SubGen(); var other = SubGen2(); return await kid + await other; }
            [Step] async Task<int> SubGen() { await _go.Task; return 1; }
            [Step] async Task<int> SubGen2() { await Task.Yield(); _go.SetResult(); return 2; }
            [Step] async Task<int> Gen2() { await Task.Yield(); return 3; }

            // Skips
            public async Task AsyncSkipUnderAParent() { await SkipParent(); await AfterSkip(); }
            [Step] async Task SkipParent() { await Task.Yield(); await Skipped(); await Task.Yield(); await Kid(); }
            [Step(SkipIf = nameof(Skip), SkipReason = "nope")] async Task Skipped() => await Task.Yield();
            [Step] async Task Kid() => await Task.Yield();
            [Step] async Task AfterSkip() => await Task.Yield();

            public void SyncSkipUnderAParent() { SyncParent(); SyncAfter(); }
            [Step] void SyncParent() { SyncSkipped(); SyncKid(); }
            [Step(SkipIf = nameof(Skip), SkipReason = "nope")] void SyncSkipped() { }
            [Step] void SyncKid() { }
            [Step] void SyncAfter() { }

            public async Task AsyncSkipWithASiblingRunning() { var bg = Background(); await Skipped(); _go.SetResult(); await bg; }
            [Step] async Task Background() { await _go.Task; }

            public async Task SyncSkipWithASiblingRunning() { var bg = Background(); SyncSkipped(); _go.SetResult(); await bg; }
            public async Task SkipThenNextWithASiblingRunning() { var bg = Background(); await Skipped(); await Next(); _go.SetResult(); await bg; }
            public async Task SyncSkipThenNextWithASiblingRunning() { var bg = Background(); SyncSkipped(); await Next(); _go.SetResult(); await bg; }

            // A parent's last act is a skip
            public async Task AsyncLastActSkip() { await LastAct(); await AfterSkip(); }
            [Step] async Task LastAct() { await Task.Yield(); await Skipped(); }

            public void SyncLastActSkip() { SyncLastAct(); SyncAfter(); }
            [Step] void SyncLastAct() { SyncSkipped(); }

            // BypassStep by hand
            public async Task BypassByHand() { await ByHand(); await AfterSkip(); }
            [Step] async Task ByHand()
            {
                StepCollector.StartStep(Id, null, "by hand", null, null);
                StepCollector.BypassStep(Id, "not needed");
                await Task.Yield();
            }

            // The next attempt's flow
            public Task LeftForTheNextAttempt() { _ = Forever(); return Task.CompletedTask; }
            public async Task NextAttempt() => await Next();

            // Under a framework's step
            public async Task FailsUnderAFrame() { try { await FailingKid(); } catch (InvalidOperationException) { } }
            [Step] async Task FailingKid() { await Task.Yield(); throw new InvalidOperationException("Kid went wrong"); }

            public async Task LeftRunningFailsUnderAFrame()
            {
                var bad = FailsLater();
                await Kid();
                _go.SetResult();
                try { await bad; } catch (InvalidOperationException) { }
            }
            [Step] async Task FailsLater() { await _go.Task; throw new InvalidOperationException("Late failure"); }

            public async Task AsyncSkipUnderAFrame() { await Skipped(); await Kid(); }
            public void SyncSkipUnderAFrame() { SyncSkipped(); SyncKid(); }
            public void SyncBypassedByItsIdUnderAFrame() => BypassesItselfSync();
            [Step] void BypassesItselfSync() { StepCollector.BypassStep(Id, "by its id"); }

            public Task BypassedByItsIdUnderAFrame() => BypassesItself();
            public Task ClosedByItsIdUnderAFrame() => ClosesItself();
            [Step] async Task ClosesItself() { StepCollector.CompleteStep(Id, true); await Task.Yield(); }
            public Task ClosedPlainlyByItsIdUnderAFrame() => ClosesItselfPlainly();
            [Step] Task ClosesItselfPlainly() { StepCollector.CompleteStep(Id, true); return Task.CompletedTask; }
            [Step] Task BypassesItself() { StepCollector.BypassStep(Id, "by its id"); return Task.CompletedTask; }

            // A straggler beside a step left running under one its caller is still in
            private Task _left;
            private readonly TaskCompletionSource _end = new();
            public async Task StragglerBesideAStepLeftRunning() { await StartsLate(); SyncHolder(); _end.SetResult(); await _left; }
            [Step] async Task StartsLate() { _late = Task.Run(async () => { await _go.Task; await LateToo(); }); await Task.Yield(); }
            [Step] void SyncHolder() { _left = LeftBeside(); _go.SetResult(); _late.GetAwaiter().GetResult(); }
            [Step] async Task LeftBeside() { await _end.Task; }
            [Step] async Task LateToo() => await Task.Yield();

            // A step that runs another through a step method
            public Func<Task> Inside;
            public async Task RunsAnotherThroughAStepMethod() => await Helper();
            [Step] async Task Helper() { await Task.Yield(); await Inside(); }

            // Steps at once, each running its own
            public async Task ManyAtOnce() => await Task.WhenAll(Enumerable.Range(0, 8).Select(Check));
            [Step] async Task Check(int i) { await Task.Yield(); await CheckAsync(); CheckSync(); }
            [Step] async Task CheckAsync() => await Task.Yield();
            [Step] void CheckSync() { }

            // The next step starts while a step left running is inside a sync step of its own
            private readonly TaskCompletionSource _entered = new();
            private readonly TaskCompletionSource _release = new();
            public async Task NextWhileASyncSubStepRuns()
            {
                var first = LeftWithASyncSubStep();
                await _entered.Task;
                await SecondOfTwo();
                _release.SetResult();
                await first;
            }
            [Step] async Task LeftWithASyncSubStep() { await Task.Yield(); BlocksInASyncStep(); }
            [Step] void BlocksInASyncStep() { _entered.SetResult(); _release.Task.Wait(); }
            [Step] async Task SecondOfTwo() => await Task.Yield();

            // Started on threads of their own, from a body with no step yet
            public async Task TaskRunsFromABody() => await Task.WhenAll(Task.Run(RunFirst), Task.Run(RunSecond));
            [Step] async Task RunFirst() { _entered.SetResult(); await _release.Task; }
            [Step] async Task RunSecond() { await _entered.Task; _release.SetResult(); await Task.Yield(); }

            public void ParallelFromABody()
            {
                using var bothIn = new CountdownEvent(2);
                Parallel.Invoke(() => ParallelStep(bothIn), () => ParallelStep(bothIn));
            }
            [Step] void ParallelStep(CountdownEvent bothIn) { bothIn.Signal(); bothIn.Wait(); }

            public async Task AsyncSkipAloneUnderAFrame() => await Skipped();
            public void SyncSkipAloneUnderAFrame() => SyncSkipped();
        }
        """;

    private static readonly Lazy<Type> Woven = new(() =>
    {
        var path = TestAssemblyBuilder.Build("StepFlows", Source);
        new StepWeaver().Weave(path, Path.ChangeExtension(path, ".pdb"));
        return Assembly.LoadFrom(path).GetType("Steps")!;
    });

    private static async Task<(string Id, ScenarioStep[] Steps)> Run(string shape)
    {
        var id = $"flow-{shape}-{Guid.NewGuid():N}";
        await RunIn(id, shape);
        return (id, StepCollector.GetSteps(id));
    }

    private static async Task RunIn(string id, string shape)
    {
        using var scope = TestIdentityScope.Begin(id, id);
        await Invoke(id, shape);
    }

    private static async Task Invoke(string id, string shape)
    {
        var steps = Activator.CreateInstance(Woven.Value)!;
        Woven.Value.GetField("Id")!.SetValue(steps, id);
        if (Woven.Value.GetMethod(shape)!.Invoke(steps, null) is Task task)
            await task;
    }

    // A framework's step around a shape: the adapter opens it, the shape runs in the same flow, and only the adapter closes it.
    private static async Task<(string[] Rows, bool OpenAfterTheShape)> InAFrame(string shape)
    {
        var id = $"flow-frame-{shape}-{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(id, id);
        var frame = StepCollector.StartFrame(id, "When", "the frame");

        await Invoke(id, shape);

        var open = StepCollector.HasActiveStep(id);
        StepCollector.CompleteFrame(id, frame, true);
        return (Rows(StepCollector.GetSteps(id)), open);
    }

    private static string[] Rows(ScenarioStep[] steps, string indent = "") =>
        steps.SelectMany(step => new[] { $"{indent}{step.Text}: {step.Status?.ToString() ?? "no result"}" }.Concat(Rows(step.SubSteps ?? [], indent + "  "))).ToArray();

    private static async Task<string[]> RowsOf(string shape) => Rows((await Run(shape)).Steps);

    private static ScenarioStep Find(ScenarioStep[] steps, string text) =>
        steps.Concat(steps.SelectMany(s => s.SubSteps ?? [])).First(s => s.Text == text);

    private static string[] BarsOf(string id) =>
        RequestResponseLogger.RequestAndResponseLogs
            .Where(log => log.TestId == id && log.IsOverrideStart && log.MarkerKind == DiagramMarkerKind.Step)
            .Select(log => log.PlantUml!)
            .ToArray();

    [Fact]
    public async Task Two_steps_started_together_are_siblings_in_the_order_they_began_whichever_finishes_first()
    {
        Assert.Equal(["Together: Passed", "  Slow: Passed", "  Fast: Passed"], await RowsOf("TwoAtOnce"));
    }

    [Fact]
    public async Task A_failure_lands_on_the_step_that_threw_and_the_other_keeps_its_own_result()
    {
        var (_, steps) = await Run("OneOfTwoFails");

        Assert.Equal(["Holds: Failed", "  Good: Passed", "  Bad: Failed"], Rows(steps));
        Assert.Equal("Bad went wrong", Find(steps, "Bad").FailureMessage);
        Assert.Null(Find(steps, "Good").FailureMessage);
    }

    [Fact]
    public async Task A_sub_step_left_running_leaves_the_next_step_a_sibling_of_its_parent_with_a_bar_of_its_own()
    {
        var (id, steps) = await Run("ChildOutlivesParent");

        Assert.Equal(["Parent: Passed", "  Child: Passed", "Next: Passed"], Rows(steps));
        Assert.Equal(2, BarsOf(id).Length);
        Assert.Contains("Next", BarsOf(id)[1]);
    }

    [Fact]
    public async Task Eight_steps_at_once_each_running_its_own_stay_siblings()
    {
        for (var round = 0; round < 40; round++)
        {
            var rows = await RowsOf("ManyAtOnce");

            Assert.Equal(24, rows.Length);
            Assert.Equal(8, rows.Count(row => !row.StartsWith(' ')));
            Assert.All(rows, row => Assert.DoesNotContain("    ", row));
        }
    }

    [Fact]
    public async Task The_next_step_is_no_child_of_a_sync_step_running_elsewhere()
    {
        Assert.Equal(["Left with a sync sub step: Passed", "  Blocks in a sync step: Passed", "Second of two: Passed"], await RowsOf("NextWhileASyncSubStepRuns"));
    }

    [Fact]
    public async Task Steps_on_threads_of_their_own_from_a_body_with_no_step_yet_are_siblings()
    {
        Assert.Equal(["Run first: Passed", "Run second: Passed"], (await RowsOf("TaskRunsFromABody")).Order());
    }

    [Fact]
    public async Task Sync_steps_in_parallel_from_a_body_with_no_step_yet_are_siblings()
    {
        Assert.Equal(["Parallel step: Passed", "Parallel step: Passed"], await RowsOf("ParallelFromABody"));
    }

    [Fact]
    public async Task A_stragglers_step_goes_under_the_step_open_now_and_draws_no_bar()
    {
        var (id, steps) = await Run("Straggler");

        Assert.Equal(["Starts: Passed", "Open: Passed", "  Late: Passed"], Rows(steps));
        Assert.Equal(2, BarsOf(id).Length);
    }

    [Fact]
    public async Task A_stragglers_step_goes_under_what_is_running_now_even_a_step_left_running()
    {
        Assert.Equal(["Starts late: Passed", "Sync holder: Passed", "  Left beside: Passed", "    Late too: Passed"], await RowsOf("StragglerBesideAStepLeftRunning"));
    }

    [Fact]
    public async Task A_step_opened_in_a_helper_is_found_and_closed_with_its_failure_by_its_test_id()
    {
        var (_, steps) = await Run("OpenedInAHelper");

        Assert.Equal(["opened in a helper: Failed"], Rows(steps));
        Assert.Equal("setup failed", steps[0].FailureMessage);
    }

    [Fact]
    public async Task A_stub_that_found_no_test_closes_nothing_and_no_step_is_listed_twice()
    {
        Assert.Equal(["Holds open: Passed"], await RowsOf("NoIdentity"));
    }

    [Fact]
    public async Task Top_level_steps_at_once_read_in_the_order_they_began_and_agree_with_their_bars()
    {
        var (id, steps) = await Run("TopLevelAtOnce");

        Assert.Equal(["Top first: Passed", "Top second: Passed"], Rows(steps));
        var bars = BarsOf(id);
        Assert.Equal(2, bars.Length);
        Assert.Contains("Top first", bars[0]);
        Assert.Contains("Top second", bars[1]);
    }

    [Fact]
    public async Task A_failed_assertion_from_a_flow_whose_step_has_ended_is_listed_under_the_step_open_now()
    {
        var (_, steps) = await Run("LateAssertion");

        Assert.Empty(Find(steps, "Starts asserter").SubSteps ?? []);
        var late = Assert.Single(Find(steps, "Still open").SubSteps!);
        Assert.Equal(ExecutionResult.Failed, late.Status);
        Assert.Equal("it was late", late.FailureMessage);
    }

    [Fact]
    public async Task A_step_that_hung_is_listed_with_no_result()
    {
        Assert.Equal(["Forever: no result"], await RowsOf("Hangs"));
    }

    [Fact]
    public async Task A_step_its_parent_gave_up_on_is_listed_with_no_result()
    {
        Assert.Equal(["Gives: Passed", "  Quick: Passed", "  Forever: no result"], await RowsOf("ParentGivesUp"));
    }

    [Fact]
    public async Task Each_of_two_steps_at_once_reads_its_own_duration()
    {
        var (_, steps) = await Run("Durations");

        // Longer began first and ended last, whatever the machine's load.
        Assert.True(Find(steps, "Longer").Duration > Find(steps, "Shorter").Duration);
    }

    [Fact]
    public async Task An_assertion_and_an_attachment_belong_to_the_flow_that_made_them()
    {
        var (_, steps) = await Run("EachItsOwn");

        var first = Find(steps, "First");
        Assert.Equal("in first", Assert.Single(first.SubSteps!).Text);
        Assert.Equal("from first", Assert.Single(first.Attachments!).Name);
        Assert.Empty(Find(steps, "Second").SubSteps ?? []);
        Assert.Empty(Find(steps, "Second").Attachments ?? []);
    }

    [Fact]
    public async Task A_step_and_an_assertion_from_a_flow_with_no_step_of_this_test_go_under_the_step_running()
    {
        var (_, steps) = await Run("FromASuppressedFlow");

        var waits = Assert.Single(steps);
        Assert.Equal(["On other flow", "elsewhere"], waits.SubSteps!.Select(s => s.Text));
    }

    [Fact]
    public async Task A_flow_with_no_step_of_this_test_goes_under_the_parent_and_not_a_sub_step_left_running()
    {
        var (_, steps) = await Run("BesideASubStepLeftRunning");

        var parent = Assert.Single(steps);
        Assert.Equal(["Left sub", "On other flow"], parent.SubSteps!.Select(s => s.Text));
    }

    [Fact]
    public async Task A_flow_in_a_scope_of_its_own_that_left_its_steps_goes_under_no_woven_step_running_elsewhere()
    {
        var (_, steps) = await Run("OwnScopeLeftItsSteps");

        Assert.Equal(["Earlier in the body", "First of its own", "Running elsewhere", "Second of its own"], steps.Select(s => s.Text));
    }

    [Fact]
    public async Task A_step_that_bypasses_itself_by_its_id_leaves_its_parent_open()
    {
        var (_, steps) = await Run("BypassesItselfByIdUnderAParent");

        Assert.Equal(["Parent of a bypass", "Next after the parent"], steps.Select(s => s.Text));
        Assert.Equal(["Bypassed itself by id", "After the bypass"], steps[0].SubSteps!.Select(s => s.Text));
    }

    [Fact]
    public async Task A_spare_completion_by_test_id_leaves_a_step_left_running_to_close_itself()
    {
        var (_, steps) = await Run("SpareBesideALeftStep");

        Assert.Equal(["Left to itself: Passed"], Rows(steps));
    }

    [Fact]
    public async Task A_flow_comes_back_to_its_step_after_a_stretch_under_another_tests_id()
    {
        var (id, steps) = await Run("UnderAnotherTestsId");

        Assert.Equal(["Seeds", "After the seed"], steps.Select(s => s.Text));
        Assert.Equal(["Sub after the seed", "Sync sub after the seed"], steps[0].SubSteps!.Select(s => s.Text));
        Assert.Equal(["Another test's step", "Seeding"], StepCollector.GetSteps(id + "-other").Select(s => s.Text), StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("HandBypassInASyncStep", "Hand opens and bypasses")]
    [InlineData("HandBypassByIdInASyncStep", "Hand opens by id and bypasses")]
    [InlineData("HandBypassFromAnotherFlow", "Hand opens another flow bypasses")]
    public async Task A_step_bypassed_by_hand_inside_a_sync_step_leaves_the_step_around_it_to_close(string shape, string outer)
    {
        var (_, steps) = await Run(shape);

        Assert.Equal([$"{outer}: Passed", "  a hand's: Bypassed", "After the hand bypass: Passed"], Rows(steps));
    }

    [Fact]
    public async Task A_pair_by_hand_after_a_bypass_by_hand_closes_its_own_and_the_parent_stays_open()
    {
        var (_, steps) = await Run("HandPairAfterABypass");

        var parent = Assert.Single(steps);
        Assert.Equal(["Bypassed by hand", "Closed by hand", "Sub after the hand pair"], parent.SubSteps!.Select(s => s.Text), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(ExecutionResult.Passed, parent.Status);
    }

    [Fact]
    public async Task A_step_that_throws_at_once_reads_failed_and_the_next_step_is_its_sibling()
    {
        var (_, steps) = await Run("ThrowsAtOnceThenNext");

        Assert.Equal(["Throws at once: Failed", "Next: Passed"], Rows(steps));
        Assert.Equal("at once", steps[0].FailureMessage);
    }

    [Fact]
    public async Task Task_of_T_steps_read_as_plain_Task_steps_do()
    {
        Assert.Equal(["Gen: Passed", "  Sub gen: Passed", "  Sub gen2: Passed", "Gen2: Passed"], await RowsOf("Generic"));
    }

    [Theory]
    [InlineData("AsyncSkipUnderAParent", "Skip parent", "Skipped", "Kid", "After skip")]
    [InlineData("SyncSkipUnderAParent", "Sync parent", "Sync skipped", "Sync kid", "Sync after")]
    public async Task A_skipped_step_is_a_sibling_the_parent_stays_open_and_the_next_sub_step_is_its_child(
        string shape, string parent, string skipped, string kid, string after)
    {
        Assert.Equal([$"{parent}: Passed", $"  {skipped}: Bypassed", $"  {kid}: Passed", $"{after}: Passed"], await RowsOf(shape));
    }

    [Theory]
    [InlineData("AsyncSkipWithASiblingRunning", "Skipped")]
    [InlineData("SyncSkipWithASiblingRunning", "Sync skipped")]
    public async Task A_skip_beside_a_step_left_running_leaves_that_step_open(string shape, string skipped)
    {
        Assert.Equal(["Background: Passed", $"{skipped}: Bypassed"], await RowsOf(shape));
    }

    [Theory]
    [InlineData("SkipThenNextWithASiblingRunning", "Skipped")]
    [InlineData("SyncSkipThenNextWithASiblingRunning", "Sync skipped")]
    public async Task The_step_after_a_skip_beside_a_step_left_running_is_no_child_of_it(string shape, string skipped)
    {
        Assert.Equal(["Background: Passed", $"{skipped}: Bypassed", "Next: Passed"], await RowsOf(shape));
    }

    [Theory]
    [InlineData("AsyncLastActSkip", "Last act", "Skipped", "After skip")]
    [InlineData("SyncLastActSkip", "Sync last act", "Sync skipped", "Sync after")]
    public async Task A_parent_whose_last_act_is_a_skip_closes_and_the_next_step_is_its_sibling(
        string shape, string parent, string skipped, string after)
    {
        Assert.Equal([$"{parent}: Passed", $"  {skipped}: Bypassed", $"{after}: Passed"], await RowsOf(shape));
    }

    [Fact]
    public async Task A_hand_called_bypass_moves_the_flow_up_and_the_parents_completion_closes_the_parent()
    {
        var rows = await RowsOf("BypassByHand");

        Assert.Equal(["By hand: Passed", "  by hand: Bypassed", "After skip: Passed"], rows);
    }

    [Fact]
    public async Task A_failing_sub_step_leaves_a_frame_open()
    {
        var (rows, open) = await InAFrame("FailsUnderAFrame");

        Assert.True(open);
        Assert.Equal(["the frame: Passed", "  Failing kid: Failed"], rows);
    }

    [Fact]
    public async Task A_failing_step_left_running_paints_itself_and_not_a_later_sub_step()
    {
        var (rows, _) = await InAFrame("LeftRunningFailsUnderAFrame");

        Assert.Equal(["the frame: Passed", "  Fails later: Failed", "  Kid: Passed"], rows);
    }

    [Theory]
    [InlineData("AsyncSkipUnderAFrame", "Skipped", "Kid")]
    [InlineData("SyncSkipUnderAFrame", "Sync skipped", "Sync kid")]
    public async Task A_skip_under_a_frame_leaves_the_frame_open_and_the_next_sub_step_its_child(string shape, string skipped, string kid)
    {
        var (rows, open) = await InAFrame(shape);

        Assert.True(open);
        Assert.Equal(["the frame: Passed", $"  {skipped}: Bypassed", $"  {kid}: Passed"], rows);
    }

    [Theory]
    [InlineData("AsyncSkipAloneUnderAFrame", "Skipped")]
    [InlineData("SyncSkipAloneUnderAFrame", "Sync skipped")]
    public async Task A_woven_completion_never_closes_a_frame(string shape, string skipped)
    {
        var (rows, open) = await InAFrame(shape);

        Assert.True(open);
        Assert.Equal(["the frame: Passed", $"  {skipped}: Bypassed"], rows);
    }

    [Theory]
    [InlineData("BypassedByItsIdUnderAFrame", "Bypasses itself")]
    [InlineData("SyncBypassedByItsIdUnderAFrame", "Bypasses itself sync")]
    public async Task A_step_that_bypasses_itself_by_its_id_leaves_the_frame_open(string shape, string step)
    {
        var (rows, open) = await InAFrame(shape);

        Assert.True(open);
        Assert.Equal(["the frame: Passed", $"  {step}: Bypassed"], rows);
    }

    [Theory]
    [InlineData("ClosedByItsIdUnderAFrame", "Closes itself")]
    [InlineData("ClosedPlainlyByItsIdUnderAFrame", "Closes itself plainly")]
    public async Task A_step_that_closes_itself_by_its_id_leaves_the_frame_open(string shape, string step)
    {
        var (rows, open) = await InAFrame(shape);

        Assert.True(open);
        Assert.Equal(["the frame: Passed", $"  {step}: Passed"], rows);
    }

    [Fact]
    public void A_spare_completion_by_test_id_leaves_the_frame_open()
    {
        var id = $"flow-spare-{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(id, id);

        var frame = StepCollector.StartFrame(id, "When", "the frame");
        StepCollector.CompleteStep(id, false, "a spare");

        Assert.True(StepCollector.HasActiveStep(id));
        StepCollector.CompleteFrame(id, frame, true);
        Assert.Equal(["the frame: Passed"], Rows(StepCollector.GetSteps(id)));
    }

    [Fact]
    public async Task A_framework_step_started_from_a_step_method_stands_under_it()
    {
        var id = $"flow-nested-{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(id, id);
        var frame = StepCollector.StartFrame(id, "When", "the frame");
        var steps = Activator.CreateInstance(Woven.Value)!;
        Woven.Value.GetField("Id")!.SetValue(steps, id);
        Woven.Value.GetField("Inside")!.SetValue(steps, (Func<Task>)(() =>
        {
            StepCollector.CompleteFrame(id, StepCollector.StartFrame(id, "Given", "a nested step"), true);
            return Task.CompletedTask;
        }));

        await (Task)Woven.Value.GetMethod("RunsAnotherThroughAStepMethod")!.Invoke(steps, null)!;
        StepCollector.CompleteFrame(id, frame, true);

        Assert.Equal(["the frame: Passed", "  Helper: Passed", "    a nested step: Passed"], Rows(StepCollector.GetSteps(id)));
    }

    [Fact]
    public async Task A_step_a_hook_left_open_holds_no_row_after_the_first_frame()
    {
        var id = $"flow-hook-left-{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(id, id);

        StepCollector.StartStep(id, "Given", "a hook's step", null, null);
        StepCollector.CompleteFrame(id, StepCollector.StartFrame(id, "When", "the frame"), true);
        await Invoke(id, "NextAttempt");

        Assert.Equal(["a hook's step: no result", "the frame: Passed", "Next: Passed"], Rows(StepCollector.GetSteps(id)));
    }

    [Fact]
    public void A_call_after_the_last_frame_reads_that_frame_and_not_a_hooks_step_left_open()
    {
        var id = $"flow-hook-call-{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(id, id);

        StepCollector.StartStep(id, "Given", "a hook's step", null, null);
        StepCollector.CompleteFrame(id, StepCollector.StartFrame(id, "When", "the frame"), true);

        var steps = StepCollector.GetSteps(id);
        Assert.Equal(steps[1].CollectedId, StepCollector.OpenStepOf(id));
    }

    [Fact]
    public void A_step_opened_by_hand_in_a_sync_hook_does_not_hold_the_rows_under_it()
    {
        var id = $"flow-hook-{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(id, id);

        StepCollector.StartStep(id, "Given", "a hook's step", null, null);
        var frame = StepCollector.StartFrame(id, "When", "the frame");
        StepCollector.CompleteFrame(id, frame, true);

        Assert.Equal(["a hook's step: no result", "the frame: Passed"], Rows(StepCollector.GetSteps(id)));
    }

    [Fact]
    public async Task A_new_attempts_steps_are_its_own_whatever_the_last_attempt_left_running()
    {
        var first = $"flow-attempt-{Guid.NewGuid():N}";
        var second = $"flow-attempt-{Guid.NewGuid():N}";

        await RunIn(first, "LeftForTheNextAttempt");
        await RunIn(second, "NextAttempt");

        Assert.Equal(["Forever: no result"], Rows(StepCollector.GetSteps(first)));
        Assert.Equal(["Next: Passed"], Rows(StepCollector.GetSteps(second)));
    }
}
