using System.Collections.Concurrent;

namespace Kronikol.Tracking;

/// <summary>
/// The state of one detached flow (<see cref="TestIdentityScope.Detach"/>): the object the flow's
/// <see cref="AsyncLocal{T}"/> slot refers to, so that what a callee learns deep inside a database SDK's
/// async call is visible to the caller and to everything the flow does next. An <c>AsyncLocal</c> value
/// set inside an awaited method is restored when the method returns to its caller; a field on an object
/// the slot refers to is not. It carries the owner window (plans/OWNER_WINDOW_AND_COUNT_CONFIRMATION_PLAN.md):
/// the scenario whose document the flow last wrote, and the calls made under that window that no operation
/// on the document has confirmed yet.
/// </summary>
internal sealed class DetachedFlow
{
    /// <summary>Every flow holding calls that nothing has confirmed or dropped yet, for report time.</summary>
    private static readonly ConcurrentDictionary<DetachedFlow, byte> Unsettled = new();

    private readonly object _lock = new();
    private (string Name, string Id)? _window;
    private List<RequestResponseLog>? _held;

    /// <summary>The open window's owner, or null.</summary>
    public (string Name, string Id)? Window
    {
        get { lock (_lock) { return _window; } }
    }

    /// <summary>Opens the window for an owner. A window held for another owner ends first, and what it held is nobody's.</summary>
    public void Open(string name, string id)
    {
        List<RequestResponseLog>? dropped = null;
        lock (_lock)
        {
            if (_window is { } current && !string.Equals(current.Id, id, StringComparison.Ordinal))
                dropped = Take();
            _window = (name, id);
        }
        Drop(dropped);
    }

    /// <summary>The flow came back to the owner's document: what it did in between is the owner's.</summary>
    public void Confirm()
    {
        List<RequestResponseLog>? held;
        lock (_lock) { held = Take(); }
        if (held is null)
            return;
        foreach (var log in held)
            RequestResponseLogger.Enqueue(log);
    }

    /// <summary>The flow moved on to something that is not the owner's: the window ends, and what it held is nobody's.</summary>
    public void Close()
    {
        List<RequestResponseLog>? held;
        lock (_lock)
        {
            held = Take();
            _window = null;
        }
        Drop(held);
    }

    /// <summary>
    /// Holds a call the window answered for, when the window is still that owner's. False when it has
    /// closed or moved on since the call resolved: the call is nobody's.
    /// </summary>
    public bool TryHold(RequestResponseLog log)
    {
        lock (_lock)
        {
            if (_window is not { } window || !string.Equals(window.Id, log.TestId, StringComparison.Ordinal))
                return false;
            (_held ??= []).Add(log);
            Unsettled[this] = 0;
            return true;
        }
    }

    /// <summary>
    /// Report time: nothing further can confirm what any flow holds for this report, so it is nobody's.
    /// The windows themselves stay as they are.
    /// </summary>
    public static void SettleAll()
    {
        foreach (var flow in Unsettled.Keys.ToArray())
        {
            List<RequestResponseLog>? held;
            lock (flow._lock) { held = flow.Take(); }
            Drop(held);
        }
    }

    /// <summary>Forgets what every flow holds, without recording it: the store it would have gone to was cleared.</summary>
    public static void ForgetAll()
    {
        foreach (var flow in Unsettled.Keys.ToArray())
            lock (flow._lock) { flow.Take(); }
    }

    /// <summary>Takes what is held; under the lock.</summary>
    private List<RequestResponseLog>? Take()
    {
        var held = _held;
        _held = null;
        Unsettled.TryRemove(this, out _);
        return held;
    }

    /// <summary>What a closed window held is background when background is captured, and gone otherwise.</summary>
    private static void Drop(List<RequestResponseLog>? held)
    {
        if (held is null || !RequestResponseLogger.CaptureBackground)
            return;
        foreach (var log in held)
            RequestResponseLogger.Enqueue(log with
            {
                TestId = TestIdentityScope.UnknownTestId,
                TestName = TestIdentityScope.UnknownTestName,
                AttributionSource = AttributionSource.Detached
            });
    }
}
