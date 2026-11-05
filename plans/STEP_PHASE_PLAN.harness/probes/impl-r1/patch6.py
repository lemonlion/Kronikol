"""Adds the failing, skipped and retried shapes to the probes. Run once, from the probes folder."""
import sys

p = 'bddfy/Scenarios.cs'
s = open(p, encoding='utf-8', newline='').read()
if 'NestedSteps' not in s:
    s = s.replace('this.When(_ => When_two_plain_calls(label + "-story-when")).BDDfy("nested in a given " + label);',
                  'new NestedSteps(_http).When(x => x.When_two_plain_calls(label + "-story-when")).BDDfy("nested in a given " + label);')
    new = '''
    private static string TestId => Xunit.TestContext.Current.Test!.UniqueID;

    [Step] public async Task WovenSlow(string label, Task gate) { await gate; await Call(label); }

    public void Then_a_hand_step_left_open_and_it_fails(string label)
    {
        CallSync(label + "-first");
        StepCollector.StartStep(TestId, null, "a sub-step never closed", null, null);
        CallSync(label + "-in-open-sub");
        throw new InvalidOperationException("planned");
    }

    public void Then_a_sub_step_outlives_a_timeout(string label)
    {
        CallSync(label + "-first");
        WovenSlow(label + "-slow", new TaskCompletionSource().Task).WaitAsync(TimeSpan.FromMilliseconds(20)).GetAwaiter().GetResult();
    }

    [Fact]
    public void D09_a_failing_Then_leaves_a_hand_step_open_and_a_second_Then_runs()
    {
        Assert.NotNull(Record.Exception(() => this
            .Given(_ => Given_a_call("d09-given"))
            .When(_ => When_a_call("d09-when"))
            .Then(_ => Then_a_hand_step_left_open_and_it_fails("d09-then-1"))
            .And(_ => Then_a_call("d09-then-2"))
            .BDDfy()));
    }

    [Fact]
    public void D10_a_Then_times_out_with_a_woven_sub_step_running_and_a_second_Then_runs()
    {
        Assert.NotNull(Record.Exception(() => this
            .Given(_ => Given_a_call("d10-given"))
            .When(_ => When_a_call("d10-when"))
            .Then(_ => Then_a_sub_step_outlives_a_timeout("d10-then-1"))
            .And(_ => Then_a_call("d10-then-2"))
            .BDDfy()));
    }
}

public class NestedSteps(HttpClient http)
{
    public void When_two_plain_calls(string label)
    {
        http.GetAsync($"http://probe.local/{label}-1").GetAwaiter().GetResult();
        http.GetAsync($"http://probe.local/{label}-2").GetAwaiter().GetResult();
    }
}
'''
    idx = s.rstrip().rfind('}')
    s = s[:idx].rstrip() + '\n' + new
    open(p, 'w', encoding='utf-8', newline='').write(s)

p = 'lightbdd/Scenarios.cs'
s = open(p, encoding='utf-8', newline='').read()
if 'L15_a_failing_step' not in s:
    new = '''
    private static string ScenarioId => LightBddTestTrackingMessageHandlerOptions.TestInfoFetcher().Id;

    [Step] public async Task WovenSlow(string label, Task gate) { await gate; await Call(label); }

    private async Task When_a_hand_step_is_left_open_and_it_fails(string label)
    {
        await Call(label + "-first");
        StepCollector.StartStep(ScenarioId, null, "a sub-step never closed", null, null);
        await Call(label + "-in-open-sub");
        throw new InvalidOperationException("planned");
    }

    private async Task When_a_sub_step_outlives_a_timeout(string label)
    {
        await Call(label + "-first");
        await WovenSlow(label + "-slow", new TaskCompletionSource().Task).WaitAsync(TimeSpan.FromMilliseconds(20));
    }

    private Task<CompositeStep> When_a_composite_with_a_hand_step_left_open(string label) => Task.FromResult(CompositeStep.DefineNew()
        .AddAsyncSteps(_ => Given_a_call(label + "-sub-one"), _ => When_a_hand_step_is_left_open_and_it_fails(label + "-sub-fails"), _ => Then_a_call(label + "-sub-never"))
        .Build());

    [Scenario]
    public async Task L15_a_failing_step_leaves_a_hand_step_open() => await Assert_fails(Runner.RunScenarioAsync(
        _ => Given_a_call("l15-given"),
        _ => When_a_hand_step_is_left_open_and_it_fails("l15-when"),
        _ => Then_a_call("l15-never")));

    [Scenario]
    public async Task L16_a_step_times_out_with_a_woven_sub_step_running() => await Assert_fails(Runner.RunScenarioAsync(
        _ => Given_a_call("l16-given"),
        _ => When_a_sub_step_outlives_a_timeout("l16-when"),
        _ => Then_a_call("l16-never")));

    [Scenario]
    public async Task L17_a_composite_whose_sub_step_fails_leaving_a_hand_step_open() => await Assert_fails(Runner.RunScenarioAsync(
        _ => Given_a_call("l17-given"),
        _ => When_a_composite_with_a_hand_step_left_open("l17"),
        _ => Then_a_call("l17-never")));

    private static async Task Assert_fails(Task run)'''
    anchor = '    private static async Task Assert_fails(Task run)'
    if anchor not in s:
        sys.exit('lightbdd anchor not found')
    s = s.replace('\n' + anchor if ('\n' + anchor) in s else anchor, new, 1)
    open(p, 'w', encoding='utf-8', newline='').write(s)

p = 'plain/Weaver.cs'
s = open(p, encoding='utf-8', newline='').read()
if 'W10_a_But' not in s:
    marker = '''    [Fact]
    public async Task W01_a_Task_Given_throws_before_returning_under_ThrowsAsync_then_When_Then()'''
    new = '''    public static bool Always => true;
    [ButWhenStep] public async Task ButWhenAsync(string l) => await Call(l);
    [ButStep] public async Task ButAsync(string l) => await Call(l);
    [GivenStep(SkipIf = nameof(Always), SkipReason = "skipped")] public async Task GivenSkipped(string l) => await Call(l);
    [WhenStep(SkipIf = nameof(Always), SkipReason = "skipped")] public async Task WhenSkipped(string l) => await Call(l);
    [WhenStep] public async Task WhenWithSkippedGiven(string l) { await GivenSkipped(l + "-skipped"); await Call(l + "-after-skipped"); }
    [GivenStep] public async Task GivenWithSkippedWhen(string l) { await WhenSkipped(l + "-skipped"); await Call(l + "-after-skipped"); }
    [WhenStep] public async Task WhenFlaky(string l, int attempt) { await GivenAsync($"{l}-{attempt}-nested"); await Call($"{l}-{attempt}-body"); if (attempt < 3) throw new InvalidOperationException("flaky"); }
    [WhenStep] public void WhenFlakySync(string l, int attempt) { GivenSync($"{l}-{attempt}-nested"); CallSync($"{l}-{attempt}-body"); if (attempt < 3) throw new InvalidOperationException("flaky"); }

    [Fact]
    public async Task W10_a_But_and_a_ButWhen_after_a_Then()
    {
        await GivenAsync("w10-given"); await WhenAsync("w10-when"); await ThenAsync("w10-then");
        await ButWhenAsync("w10-butwhen"); await Call("w10-after-butwhen");
        await ButAsync("w10-but"); await Call("w10-after-but");
    }

    [Fact]
    public async Task W11_a_skipped_Given_at_the_top_after_a_When()
    {
        await WhenAsync("w11-when");
        await GivenSkipped("w11-skipped"); await Call("w11-after-skipped");
        await ThenAsync("w11-then");
    }

    [Fact]
    public async Task W12_a_skipped_Given_under_a_When_and_a_skipped_When_under_a_Given()
    {
        await GivenWithSkippedWhen("w12-given"); await Call("w12-after-given");
        await WhenWithSkippedGiven("w12-when"); await Call("w12-after-when");
    }

    [Fact]
    public async Task W13_a_skipped_When_at_the_top_after_a_Given()
    {
        await GivenAsync("w13-given");
        await WhenSkipped("w13-skipped"); await Call("w13-after-skipped");
        await GivenAsync("w13-given-2"); await Call("w13-after-given-2");
    }

    [Fact]
    public async Task W14_a_When_retried_in_a_loop_with_a_nested_Given()
    {
        await GivenAsync("w14-given");
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try { await WhenFlaky("w14-try", attempt); break; }
            catch (InvalidOperationException) { await Call($"w14-between-{attempt}"); }
        }
        await Call("w14-after");
    }

    [Fact]
    public async Task W15_a_sync_When_retried_in_a_loop_with_a_nested_Given()
    {
        await GivenAsync("w15-given");
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try { WhenFlakySync("w15-try", attempt); break; }
            catch (InvalidOperationException) { await Call($"w15-between-{attempt}"); }
        }
        await Call("w15-after");
    }

'''
    crlf = marker.replace('\n', '\r\n')
    if marker in s:
        s = s.replace(marker, new + marker, 1)
    elif crlf in s:
        s = s.replace(crlf, new.replace('\n', '\r\n') + crlf, 1)
    else:
        sys.exit('plain marker not found')
    open(p, 'w', encoding='utf-8', newline='').write(s)
print('patched')
