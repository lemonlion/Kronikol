// Shared source: also compiled into Kronikol.Extensions.ProxyTap and Kronikol.Extensions.Otlp as a linked file, in each
// package's own namespace.
using System.Collections.Concurrent;

#if KRONIKOL_OTLP_SHARED
namespace Kronikol.Extensions.Otlp;
#elif KRONIKOL_PROXYTAP_SHARED
namespace Kronikol.Extensions.ProxyTap;
#else
namespace Kronikol.Extensions.TcpTap;
#endif

/// <summary>
/// Cuts a transfer whose peer has moved no bytes for its limit (plans/TAP_CALLER_TIME_LIMITS_PLAN.md §4.1, §4.2). A
/// transfer that can stall is watched: it records progress as each of its reads or writes completes, and a heartbeat,
/// once a second, fires an entry whose last progress is older than its limit, once. A limit is compared as an elapsed
/// time, never added to a timestamp, so <see cref="TimeSpan.MaxValue"/> never fires. While a debugger is attached to the
/// process no limit fires: a stall that ends meanwhile is forgotten, and one that outlives the detach is cut at the next
/// beat. An entry can be watched for a condition the heartbeat checks instead (a command left unanswered, for
/// <c>TcpTap</c>'s stuck-connection reaper), and can act under a debugger too (the reaper's, which did before any limit
/// existed, §4.5 item 5).
/// </summary>
/// <remarks>
/// <para>The heartbeat only decides. It moves a due entry from active to fired and queues the entry's action to the
/// thread pool, so an action that blocks (an abort that writes, a host's callback) holds one pool thread, not every
/// limit. A transfer that ends moves its entry from active to ended before it looks at anything else, and whoever moves
/// first decides: the transfer reads a lost move as the limit's, whatever its own I/O returned.</para>
/// <para>The heartbeat keeps no test's context: a timer keeps the context it is made in, and a tap starts inside a test,
/// so it is made with the context's flow suppressed. Beats may overlap, and a beat holds no lock.</para>
/// </remarks>
internal sealed class StallWatch : IDisposable
{
    /// <summary>How often the heartbeat looks: a stall is cut between its limit and its limit plus a beat.</summary>
    internal static readonly TimeSpan Beat = TimeSpan.FromSeconds(1);

    private readonly TimeProvider _clock;
    private readonly Func<bool> _debuggerAttached;
    private readonly Action<string> _log;
    private readonly ConcurrentDictionary<Entry, byte> _entries = new();
    private readonly ITimer _heartbeat;
    private int _disposed;

    /// <param name="clock">The tap's clock: <see cref="TimeProvider.System"/>, or a fake one in facts.</param>
    /// <param name="debuggerAttached">Whether a debugger is attached to the process.</param>
    /// <param name="log">Writes one line; the tap adds its name and guards the host's callback.</param>
    public StallWatch(TimeProvider clock, Func<bool> debuggerAttached, Action<string> log)
    {
        _clock = clock;
        _debuggerAttached = debuggerAttached;
        _log = log;
        using (ExecutionContext.SuppressFlow())
            _heartbeat = clock.CreateTimer(static state => ((StallWatch)state!).Look(), this, Beat, Beat);
    }

    // Called between the heartbeat's look at a due entry and its move to fired (Kronikol.Tests.*): a fact completes the
    // transfer in that gap. Null in production.
    internal Action<Entry>? BeforeFire { get; set; }

    // Called on the pool before a fired entry's action (Kronikol.Tests.*): a fact holds the action while the transfer it
    // raced completes. Null in production.
    internal Action<Entry>? BeforeAct { get; set; }

    // The entries being watched (Kronikol.Tests.*): a fact orders its steps by their stamps.
    internal IReadOnlyList<Entry> Entries => [.. _entries.Keys];

    /// <summary>
    /// Starts watching a transfer, its limit counting from now; null when there is no limit. <paramref name="fired"/> runs
    /// on the thread pool once the limit fires, and whatever it throws is logged.
    /// </summary>
    public Entry? Watch(string phase, TimeSpan? limit, Action fired)
    {
        if (limit is not { } span || Volatile.Read(ref _disposed) == 1)
            return null;
        var entry = new Entry(this, phase, span, fired);
        _entries.TryAdd(entry, 0);
        return entry;
    }

    /// <summary>
    /// Starts watching a condition rather than a stall: the heartbeat asks <paramref name="due"/> at each beat, on its own
    /// thread, and fires the entry once it says so; <paramref name="limit"/> only names the wait in messages. A check that
    /// throws ends the entry. With <paramref name="evenUnderDebugger"/> the entry acts while a debugger is attached too.
    /// </summary>
    public Entry? WatchWhen(string phase, TimeSpan limit, Func<bool> due, Action fired, bool evenUnderDebugger)
    {
        if (Volatile.Read(ref _disposed) == 1)
            return null;
        var entry = new Entry(this, phase, limit, fired, due, evenUnderDebugger);
        _entries.TryAdd(entry, 0);
        return entry;
    }

    private void Look()
    {
        if (Volatile.Read(ref _disposed) == 1)
            return;
        bool debugger;
        try
        {
            debugger = _debuggerAttached();
        }
        catch (Exception)
        {
            debugger = false; // a debugger check that throws is no debugger
        }

        foreach (var entry in _entries.Keys)
        {
            if (!entry.IsActive || (debugger && !entry.EvenUnderDebugger) || !IsDue(entry))
                continue;
            BeforeFire?.Invoke(entry);
            if (!entry.TryFire())
                continue; // the transfer ended first: it is not the limit's
            _entries.TryRemove(entry, out _);
            ThreadPool.UnsafeQueueUserWorkItem(static fired => fired.Owner.Act(fired), entry, preferLocal: false);
        }
    }

    private bool IsDue(Entry entry)
    {
        if (entry.Due is not { } due)
            return _clock.GetElapsedTime(entry.LastProgress) >= entry.Limit;
        try
        {
            return due();
        }
        catch (Exception ex)
        {
            if (entry.TryEnd())
                _log($"stopped watching the {entry.Phase}: its check threw {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private void Act(Entry entry)
    {
        if (Volatile.Read(ref _disposed) == 1)
            return; // the tap is stopping, and settles its transfers itself
        try
        {
            BeforeAct?.Invoke(entry);
            entry.Fired();
        }
        catch (Exception ex)
        {
            _log($"cutting a stalled {entry.Phase} threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Stops the heartbeat. A beat already running finishes; an action it queued does nothing.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;
        _heartbeat.Dispose();
    }

    /// <summary>One watched transfer: its phase, its limit, the time of its last progress, and one state.</summary>
    internal sealed class Entry
    {
        private const int Active = 0, Ended = 1, FiredState = 2;

        private int _state;
        private long _lastProgress;

        internal Entry(StallWatch owner, string phase, TimeSpan limit, Action fired, Func<bool>? due = null, bool evenUnderDebugger = false)
        {
            Owner = owner;
            Phase = phase;
            Limit = limit;
            Fired = fired;
            Due = due;
            EvenUnderDebugger = evenUnderDebugger;
            _lastProgress = owner._clock.GetTimestamp();
        }

        internal StallWatch Owner { get; }

        /// <summary>What the transfer waits on, for the tap's messages: a request body, an answer.</summary>
        public string Phase { get; }

        public TimeSpan Limit { get; }

        internal Action Fired { get; }

        /// <summary>The condition the heartbeat checks in place of the stall, or null for a stall.</summary>
        internal Func<bool>? Due { get; }

        /// <summary>Whether the entry acts while a debugger is attached: the reaper's does, as it did before the limits.</summary>
        public bool EvenUnderDebugger { get; }

        /// <summary>The clock's timestamp at the transfer's last progress.</summary>
        public long LastProgress => Volatile.Read(ref _lastProgress);

        public bool IsActive => Volatile.Read(ref _state) == Active;

        /// <summary>Whether the limit fired: the transfer is the limit's.</summary>
        public bool HasFired => Volatile.Read(ref _state) == FiredState;

        /// <summary>Records that a read or write moved bytes: the wait starts again.</summary>
        public void Progress() => Volatile.Write(ref _lastProgress, Owner._clock.GetTimestamp());

        /// <summary>Ends the watch as the transfer ends; false when the limit fired first.</summary>
        public bool TryEnd()
        {
            var ended = Interlocked.CompareExchange(ref _state, Ended, Active) == Active;
            Owner._entries.TryRemove(this, out _);
            return ended;
        }

        internal bool TryFire() => Interlocked.CompareExchange(ref _state, FiredState, Active) == Active;
    }
}
