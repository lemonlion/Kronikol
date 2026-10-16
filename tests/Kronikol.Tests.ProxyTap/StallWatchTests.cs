using System.Runtime.CompilerServices;
using Kronikol.Extensions.ProxyTap;
using Microsoft.Extensions.Time.Testing;

namespace Kronikol.Tests.ProxyTap;

/// <summary>
/// The watch on its own, with a fake clock (plans/TAP_CALLER_TIME_LIMITS_PLAN.md §5 W-1; the three taps compile the same
/// file). Actions run on the thread pool, so every fact polls for their effect.
/// </summary>
public class StallWatchTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task An_entry_past_its_limit_acts_once_and_never_again()
    {
        var clock = new FakeTimeProvider();
        using var watch = new StallWatch(clock, static () => false, _ => { });
        var acted = new StrongBox<int>();
        var entry = watch.Watch("body", Limit, () => Interlocked.Increment(ref acted.Value))!;

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True(await WaitUntilAsync(() => Volatile.Read(ref acted.Value) == 1), "the entry never acted");
        clock.Advance(TimeSpan.FromSeconds(5));
        await Task.Delay(300);

        Assert.Equal(1, Volatile.Read(ref acted.Value));
        Assert.True(entry.HasFired);
        Assert.False(entry.TryEnd()); // the transfer reads its lost move as the limit's
        Assert.Empty(watch.Entries);
    }

    [Fact]
    public async Task Progress_starts_the_wait_again()
    {
        var clock = new FakeTimeProvider();
        using var watch = new StallWatch(clock, static () => false, _ => { });
        var acted = new StrongBox<int>();
        var entry = watch.Watch("body", Limit, () => Interlocked.Increment(ref acted.Value))!;

        for (var i = 0; i < 5; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(600));
            entry.Progress();
        }

        await Task.Delay(300);
        Assert.Equal(0, Volatile.Read(ref acted.Value));
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True(await WaitUntilAsync(() => Volatile.Read(ref acted.Value) == 1), "a stall after the progress never acted");
    }

    [Fact]
    public async Task An_entry_ended_before_its_limit_never_acts()
    {
        var clock = new FakeTimeProvider();
        using var watch = new StallWatch(clock, static () => false, _ => { });
        var acted = new StrongBox<int>();
        var entry = watch.Watch("body", Limit, () => Interlocked.Increment(ref acted.Value))!;

        Assert.True(entry.TryEnd());
        clock.Advance(TimeSpan.FromSeconds(5));
        await Task.Delay(300);

        Assert.Equal(0, Volatile.Read(ref acted.Value));
        Assert.Empty(watch.Entries);
    }

    [Fact]
    public async Task An_action_that_throws_is_swallowed_and_logged()
    {
        var clock = new FakeTimeProvider();
        var log = new List<string>();
        using var watch = new StallWatch(clock, static () => false, line => { lock (log) log.Add(line); });
        watch.Watch("answer", Limit, () => throw new InvalidOperationException("boom"));

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True(await WaitUntilAsync(() => { lock (log) return log.Count == 1; }), "the action's throw was not logged");
        lock (log)
            Assert.Equal("cutting a stalled answer threw InvalidOperationException: boom", log[0]);

        // The heartbeat lives on.
        var acted = new StrongBox<int>();
        watch.Watch("answer", Limit, () => Interlocked.Increment(ref acted.Value));
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True(await WaitUntilAsync(() => Volatile.Read(ref acted.Value) == 1), "the heartbeat stopped after a throw");
    }

    [Fact]
    public async Task While_a_debugger_is_attached_only_an_entry_that_acts_under_one_does()
    {
        var clock = new FakeTimeProvider();
        var attached = new StrongBox<bool>(true);
        using var watch = new StallWatch(clock, () => Volatile.Read(ref attached.Value), _ => { });
        var stall = new StrongBox<int>();
        var reaper = new StrongBox<int>();
        watch.Watch("body", Limit, () => Interlocked.Increment(ref stall.Value));
        watch.WatchWhen("unanswered command", Limit, static () => true, () => Interlocked.Increment(ref reaper.Value), evenUnderDebugger: true);

        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.True(await WaitUntilAsync(() => Volatile.Read(ref reaper.Value) == 1), "the entry that acts under a debugger did not");
        await Task.Delay(300);
        Assert.Equal(0, Volatile.Read(ref stall.Value));

        Volatile.Write(ref attached.Value, false);
        clock.Advance(StallWatch.Beat);
        Assert.True(await WaitUntilAsync(() => Volatile.Read(ref stall.Value) == 1), "a stall that outlived the detach was not cut at the next beat");
    }

    [Fact]
    public async Task After_disposal_no_entry_acts()
    {
        var clock = new FakeTimeProvider();
        var watch = new StallWatch(clock, static () => false, _ => { });
        var acted = new StrongBox<int>();
        watch.Watch("body", Limit, () => Interlocked.Increment(ref acted.Value));

        watch.Dispose();
        clock.Advance(TimeSpan.FromSeconds(5));
        await Task.Delay(300);

        Assert.Equal(0, Volatile.Read(ref acted.Value));
        Assert.Null(watch.Watch("body", Limit, () => Interlocked.Increment(ref acted.Value))); // and none is watched
    }

    [Fact]
    public async Task An_action_queued_before_disposal_does_not_act_after_it()
    {
        var clock = new FakeTimeProvider();
        var watch = new StallWatch(clock, static () => false, _ => { });
        var acted = new StrongBox<int>();
        watch.Watch("body", Limit, () => Interlocked.Increment(ref acted.Value));
        watch.BeforeFire = _ => watch.Dispose(); // the tap stops between the heartbeat's look and its action

        clock.Advance(TimeSpan.FromSeconds(2));
        await Task.Delay(500);

        Assert.Equal(0, Volatile.Read(ref acted.Value));
    }

    [Fact]
    public async Task An_entry_added_during_a_beat_is_seen_at_the_next()
    {
        var clock = new FakeTimeProvider();
        using var watch = new StallWatch(clock, static () => false, _ => { });
        var later = new StrongBox<int>();
        StallWatch.Entry? added = null;
        watch.Watch("body", Limit, static () => { });
        watch.BeforeFire = _ => added ??= watch.Watch("answer", Limit, () => Interlocked.Increment(ref later.Value));

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(await WaitUntilAsync(() => added is not null), "the first entry never came due");
        Assert.Equal(0, Volatile.Read(ref later.Value));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(await WaitUntilAsync(() => Volatile.Read(ref later.Value) == 1), "the entry added during a beat was never seen");
    }

    [Fact]
    public async Task A_limit_of_the_largest_span_never_acts()
    {
        var clock = new FakeTimeProvider();
        using var watch = new StallWatch(clock, static () => false, _ => { });
        var acted = new StrongBox<int>();
        watch.Watch("body", TimeSpan.MaxValue, () => Interlocked.Increment(ref acted.Value));

        clock.Advance(TimeSpan.FromHours(1));
        await Task.Delay(300);

        Assert.Equal(0, Volatile.Read(ref acted.Value));
    }

    [Fact]
    public async Task A_condition_entry_acts_once_its_check_says_so()
    {
        var clock = new FakeTimeProvider();
        using var watch = new StallWatch(clock, static () => false, _ => { });
        var due = new StrongBox<bool>();
        var acted = new StrongBox<int>();
        watch.WatchWhen("unanswered command", Limit, () => Volatile.Read(ref due.Value), () => Interlocked.Increment(ref acted.Value), evenUnderDebugger: false);

        clock.Advance(TimeSpan.FromSeconds(3));
        await Task.Delay(300);
        Assert.Equal(0, Volatile.Read(ref acted.Value));

        Volatile.Write(ref due.Value, true);
        clock.Advance(StallWatch.Beat);
        Assert.True(await WaitUntilAsync(() => Volatile.Read(ref acted.Value) == 1), "the entry did not act once due");
        clock.Advance(TimeSpan.FromSeconds(3));
        await Task.Delay(300);
        Assert.Equal(1, Volatile.Read(ref acted.Value));
    }

    [Fact]
    public async Task A_condition_whose_check_throws_ends_its_entry_and_is_logged()
    {
        var clock = new FakeTimeProvider();
        var log = new List<string>();
        using var watch = new StallWatch(clock, static () => false, line => { lock (log) log.Add(line); });
        var acted = new StrongBox<int>();
        var entry = watch.WatchWhen("unanswered command", Limit, () => throw new InvalidOperationException("bad check"), () => Interlocked.Increment(ref acted.Value), evenUnderDebugger: true)!;

        clock.Advance(StallWatch.Beat);
        Assert.True(await WaitUntilAsync(() => !entry.IsActive), "the entry outlived its check's throw");
        clock.Advance(TimeSpan.FromSeconds(3));
        await Task.Delay(300);

        Assert.Equal(0, Volatile.Read(ref acted.Value));
        lock (log)
            Assert.Equal(["stopped watching the unanswered command: its check threw InvalidOperationException: bad check"], log);
    }

    [Fact]
    public async Task A_watch_made_while_the_flow_is_suppressed_beats()
    {
        // A host may start a tap with the context's flow already suppressed, to keep a test's identity out of it.
        var clock = new FakeTimeProvider();
        var acted = new StrongBox<int>();
        StallWatch watch;
        using (ExecutionContext.SuppressFlow())
            watch = new StallWatch(clock, static () => false, _ => { });

        using (watch)
        {
            watch.Watch("body", Limit, () => Interlocked.Increment(ref acted.Value));
            clock.Advance(TimeSpan.FromSeconds(2));
            Assert.True(await WaitUntilAsync(() => Volatile.Read(ref acted.Value) == 1), "the entry never acted");
        }
    }

    [Fact]
    public async Task A_check_that_blocks_holds_up_no_other_entry()
    {
        // A tap's reaper asks the decoder, which is the host's code: one that blocks costs one thread, not every limit. On
        // the real clock, since a fake one runs no beat while another is still running.
        using var watch = new StallWatch(TimeProvider.System, static () => false, _ => { });
        using var release = new ManualResetEventSlim();
        var asked = new StrongBox<int>();
        watch.WatchWhen("unanswered command", Limit, () =>
        {
            Interlocked.Increment(ref asked.Value);
            release.Wait(Patience);
            return false;
        }, () => { }, evenUnderDebugger: false);
        var acted = new StrongBox<int>();
        for (var i = 0; i < 20; i++) // entries are walked in hash order: some come after the blocked one
            watch.Watch("body", Limit, () => Interlocked.Increment(ref acted.Value));

        try
        {
            Assert.True(await WaitUntilAsync(() => Volatile.Read(ref acted.Value) == 20), "an entry past its limit waited on another entry's check");
            await Task.Delay(StallWatch.Beat * 2); // two more beats
            Assert.Equal(1, Volatile.Read(ref asked.Value)); // a check still running is not asked again
        }
        finally
        {
            release.Set();
        }
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                return false;
            await Task.Delay(20);
        }

        return true;
    }
}
