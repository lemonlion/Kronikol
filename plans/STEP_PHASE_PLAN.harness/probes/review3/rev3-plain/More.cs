using Kronikol;
using Kronikol.Tracking;
using Kronikol.xUnit3;
using PhaseProbe;
using Xunit;

namespace PhaseProbe.Rev3;

public class G_More : Base
{
    public bool Skip => true;
    [WhenStep(SkipIf = nameof(Skip), SkipReason = "not today")] public async Task WhenSkippedAsync(string l) => await C.Make(l);

    [Fact] public async Task G01_a_skipped_When_then_a_Given_and_a_real_When()
    {
        await GivenAsync("g01-first-given");
        await WhenSkippedAsync("g01-skipped");
        await GivenAsync("g01-second-given");
        await WhenAsync("g01-when");
    }

    [Fact] public async Task G02_a_manual_keyworded_start_never_completed_then_When()
    {
        StepCollector.StartStep(Id, "Given", "g02-manual-given", null, null);
        await C.Make("g02-in-manual");
        await WhenAsync("g02-when");
        await NoSetup.Make("g02-nosetup");
        await ThenAsync("g02-then");
    }

    [Fact] public async Task G03_manual_lower_case_and_setup_keywords()
    {
        StepCollector.StartStep(Id, "given", "g03-lower-given", null, null);
        StepCollector.CompleteStep(Id, true);
        await C.Make("g03-after-lower-given");
        StepCollector.StartStep(Id, "Setup", "g03-setup", null, null);
        StepCollector.CompleteStep(Id, true);
        await C.Make("g03-after-setup");
        StepCollector.StartStep(Id, "WHEN", "g03-upper-when", null, null);
        StepCollector.CompleteStep(Id, true);
        await C.Make("g03-after-upper-when");
    }

    [Fact] public async Task G04_manual_And_after_a_phase_set_directly()
    {
        await GivenAsync("g04-given");
        TestPhaseContext.Current = TestPhase.Action;
        StepCollector.StartStep(Id, "And", "g04-and", null, null);
        await NoSetup.Make("g04-in-and-nosetup");
        StepCollector.CompleteStep(Id, true);
    }

    [Fact] public async Task G05_manual_And_after_a_background_Given()
    {
        await WhenAsync("g05-when");
        await Task.Run(() => GivenAsync("g05-bg-given"));
        StepCollector.StartStep(Id, "And", "g05-and", null, null);
        await NoSetup.Make("g05-in-and-nosetup");
        StepCollector.CompleteStep(Id, true);
    }

    // The only Given on a child flow, then keyword-free on the main flow.
    [Fact] public async Task G06_the_only_Given_on_a_child_flow_then_keyword_free_on_the_main_flow()
    {
        await Task.Run(() => NsGivenAsync("g06-given-in-run"));
        await NsPlainAsync("g06-plain-in-main");
        await NoSetup.Make("g06-raw-in-main");
    }

    // A wrapper holding only a Given, async: the phase cannot flow back out; the next keyword-free step fills it.
    [Step] public async Task WrapperHoldingGiven(string l) { await NsGivenAsync(l + "-wrapped-given"); }

    [Fact] public async Task G07_an_async_wrapper_holding_a_Given_then_keyword_free()
    {
        await WrapperHoldingGiven("g07");
        await NoSetup.Make("g07-raw-after-wrapper");
        await NsPlainAsync("g07-plain");
        await NoSetup.Make("g07-raw-after-plain");
    }

    // Same step re-entered: recursion with a keyword
    [GivenStep] public async Task GivenRecursive(string l, int depth) { await C.Make($"{l}-d{depth}"); if (depth > 0) await GivenRecursive(l, depth - 1); await C.Make($"{l}-d{depth}-after"); }

    [Fact] public async Task G08_a_recursive_Given_then_When()
    {
        await GivenRecursive("g08", 2);
        await WhenAsync("g08-when");
    }

    // A sync Given whose async sub-step is awaited by blocking: the common sync-over-async shape.
    [GivenStep] public void GivenBlocks(string l) { GivenAsync(l + "-sub").GetAwaiter().GetResult(); C.Make(l + "-after").GetAwaiter().GetResult(); }

    [Fact] public async Task G09_a_sync_Given_blocks_on_an_async_Given_then_When()
    {
        GivenBlocks("g09");
        await WhenAsync("g09-when");
    }

    // ConfigureAwait(false), a Then with a sub-step on another thread.
    [ThenStep] public async Task ThenWithSub(string l) { await C.Make(l).ConfigureAwait(false); await PlainAsync(l + "-sub").ConfigureAwait(false); await C.Make(l + "-after").ConfigureAwait(false); }

    [Fact] public async Task G10_ConfigureAwait_false()
    {
        await GivenAsync("g10-given").ConfigureAwait(false);
        await WhenAsync("g10-when").ConfigureAwait(false);
        await ThenWithSub("g10-then").ConfigureAwait(false);
    }

    // StartAction on another flow than the Given; then a When: no second marker, phases.
    [Fact] public async Task G11_StartAction_on_a_child_flow_then_When()
    {
        await GivenAsync("g11-given");
        await Task.Run(() => TrackingDiagramOverride.StartAction());
        await C.Make("g11-raw-after");
        await WhenAsync("g11-when");
    }

    // A When on a thread that did not inherit the test's context: no id, so no step.
    [Fact] public async Task G13_a_When_on_a_detached_thread_with_no_identity()
    {
        await GivenAsync("g13-given");
        var t = new Thread(() => WhenSync("g13-when-on-detached-thread"));
        t.UnsafeStart();
        t.Join();
        await C.Make("g13-raw-after");
        await WhenAsync("g13-when");
    }

    // SkipIf's getter throwing: the preamble is outside the try, so the Given stays open.
    [GivenStep(SkipIf = nameof(Throws))] public void Given_skipif_throws() { }

    [Fact] public async Task G14_a_Given_whose_SkipIf_getter_throws_is_caught_then_When()
    {
        try { Given_skipif_throws(); } catch (InvalidOperationException) { }
        await WhenAsync("g14-when");
        await NoSetup.Make("g14-nosetup");
        await NsThenAsync("g14-then");
    }

    // Keyword steps inside a TestIdentityScope for another test id, in the middle of this test.
    [Fact] public async Task G12_a_scope_for_another_identity_inside_a_test()
    {
        await GivenAsync("g12-given");
        using (TestIdentityScope.Begin("other", "other-id"))
        {
            await C.Make("g12-in-scope");
            await WhenAsync("g12-when-in-scope");
        }
        await C.Make("g12-after-scope");
        await ThenAsync("g12-then");
    }
}
