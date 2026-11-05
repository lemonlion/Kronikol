using Kronikol;
using Kronikol.Tracking;
using Kronikol.xUnit3;
using Probe;
using Xunit;

namespace Probe.Plain;

/// <summary>Shapes where the 5.28.2 weaver and the prototype disagree: run with the old weaver on the new core, and each with its own core.</summary>
[Collection(DiagrammedComponentTest.DiagrammedTestCollectionName)]
public class WeaverShapes : Base
{
    public bool Throws => throw new InvalidOperationException("skip getter");

    [GivenStep] public Task GivenGuarded(string l) { if (l.Length > 0) throw new ArgumentException(l); return Call(l); }
    [GivenStep(SkipIf = nameof(Throws))] public async Task GivenGetterThrows(string l) => await Call(l);
    [GivenStep(SkipIf = nameof(Throws))] public void GivenGetterThrowsSync(string l) => CallSync(l);
    [WhenStep] public async ValueTask WhenValueTask(string l) { await Call(l + "-before"); await GivenAsync(l + "-nested"); await Call(l + "-after"); }
    [WhenStep] public async ValueTask<int> WhenValueTaskOfT(string l) { await Call(l + "-before"); await GivenAsync(l + "-nested"); await Call(l + "-after"); return 1; }
    [GivenStep] public IEnumerable<string> GivenIterator(string l) { CallSync(l + "-in-iterator"); yield return l; }
    [WhenStep] public async void WhenAsyncVoid(string l, TaskCompletionSource done) { await Call(l); done.SetResult(); }
    [WhenStep] public void WhenThrows(string l) => throw new InvalidOperationException(l);

    public static bool Always => true;
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

    [Fact]
    public async Task W01_a_Task_Given_throws_before_returning_under_ThrowsAsync_then_When_Then()
    {
        await Call("w01-first");
        await Assert.ThrowsAsync<ArgumentException>(() => GivenGuarded("w01-guard"));
        await Call("w01-after-guard");
        await WhenAsync("w01-when");
        await Call("w01-after-when");
        await ThenAsync("w01-then");
    }

    [Fact]
    public async Task W02_the_same_Task_Given_called_directly_in_a_try_then_When_Then()
    {
        await Call("w02-first");
        try { await GivenGuarded("w02-guard"); } catch (ArgumentException) { }
        await Call("w02-after-guard");
        await WhenAsync("w02-when");
        await Call("w02-after-when");
        await ThenAsync("w02-then");
    }

    [Fact]
    public async Task W03_a_SkipIf_getter_throws_caught_then_When_Then()
    {
        try { await GivenGetterThrows("w03-guard"); } catch (InvalidOperationException) { }
        await WhenAsync("w03-when");
        await Call("w03-after-when");
    }

    [Fact]
    public async Task W04_a_sync_SkipIf_getter_throws_caught_then_When_Then()
    {
        try { GivenGetterThrowsSync("w04-guard"); } catch (InvalidOperationException) { }
        await WhenAsync("w04-when");
        await Call("w04-after-when");
    }

    [Fact]
    public async Task W05_a_ValueTask_When_with_a_nested_Given_after_an_await()
    {
        await GivenAsync("w05-given");
        await WhenValueTask("w05");
        await Call("w05-after-when");
    }

    [Fact]
    public async Task W06_a_ValueTask_of_T_When_with_a_nested_Given_after_an_await()
    {
        await GivenAsync("w06-given");
        await WhenValueTaskOfT("w06");
        await Call("w06-after-when");
    }

    [Fact]
    public async Task W07_an_iterator_Given_then_a_call_then_a_When()
    {
        await Call("w07-first");
        foreach (var _ in GivenIterator("w07")) { }
        await Call("w07-after-iterator");
        await WhenAsync("w07-when");
    }

    [Fact]
    public async Task W08_an_async_void_When_after_a_Given_then_a_call()
    {
        await GivenAsync("w08-given");
        var done = new TaskCompletionSource();
        WhenAsyncVoid("w08-when", done);
        await done.Task;
        await Call("w08-after-when");
    }

    [Fact]
    public async Task W09_a_sync_When_throws_caught_in_a_Given_then_a_call()
    {
        await GivenAsync("w09-given");
        try { WhenThrows("w09-when"); } catch (InvalidOperationException) { }
        await Call("w09-after-when");
        await ThenAsync("w09-then");
    }
}
