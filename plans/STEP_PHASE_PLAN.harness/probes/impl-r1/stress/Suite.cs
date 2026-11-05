using System.Diagnostics;
using Kronikol;
using Kronikol.Tracking;
using Xunit;

[assembly: TrackSteps]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Probe.Stress;

/// <summary>What a long loop of skipped steps costs. A skipped step stays on its flow's chain: N of them above a parent is N frames of Without when it closes.</summary>
public class Suite
{
    private static readonly int N = int.Parse(Environment.GetEnvironmentVariable("PROBE_N") ?? "1000");
    public static bool Always => true;

    public Suite() { Track.TestIdResolver ??= () => TestContext.Current.Test?.UniqueID; }

    private static void Note(string shape, Stopwatch clock) =>
        File.AppendAllText(Environment.GetEnvironmentVariable("PROBE_TIME") ?? "times.txt", $"{shape} N={N} {clock.ElapsedMilliseconds} ms\n");

    [Step(SkipIf = nameof(Always), SkipReason = "skipped")] public void Skipped() { }
    [Step(SkipIf = nameof(Always), SkipReason = "skipped")] public Task SkippedAsync() => Task.CompletedTask;
    [GivenStep(SkipIf = nameof(Always), SkipReason = "skipped")] public void GivenSkipped() { }
    [Step] public void Plain() { }

    [Step]
    public void SyncParentOfSkipped()
    {
        for (var i = 0; i < N; i++)
            Skipped();
    }

    [Step]
    public async Task AsyncParentOfSkipped()
    {
        for (var i = 0; i < N; i++)
            await SkippedAsync();
        await Task.Yield();
    }

    [Fact]
    public void A_skips_under_a_sync_parent()
    {
        var clock = Stopwatch.StartNew();
        SyncParentOfSkipped();
        Note("skips-under-sync-parent", clock);
    }

    [Fact]
    public async Task B_skips_under_an_async_parent()
    {
        var clock = Stopwatch.StartNew();
        await AsyncParentOfSkipped();
        Note("skips-under-async-parent", clock);
    }

    [Fact]
    public async Task C_async_skips_under_a_hand_step_on_the_test_flow()
    {
        var clock = Stopwatch.StartNew();
        var id = TestContext.Current.Test!.UniqueID;
        StepCollector.StartStep(id, null, "a hand step around a loop", null, null);
        for (var i = 0; i < N; i++)
            await SkippedAsync();
        StepCollector.CompleteStep(id, true);
        Note("async-skips-under-hand-step", clock);
    }

    [Fact]
    public void D_skips_at_the_top()
    {
        var clock = Stopwatch.StartNew();
        for (var i = 0; i < N; i++)
            Skipped();
        Note("skips-at-top", clock);
    }

    [Fact]
    public void E_keyworded_skips_at_the_top()
    {
        var clock = Stopwatch.StartNew();
        for (var i = 0; i < N; i++)
            GivenSkipped();
        Note("keyworded-skips-at-top", clock);
    }

    [Fact]
    public void F_plain_steps_at_the_top()
    {
        var clock = Stopwatch.StartNew();
        for (var i = 0; i < N; i++)
            Plain();
        Note("plain-at-top", clock);
    }
}
