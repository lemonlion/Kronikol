using System.Collections.Concurrent;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Kronikol.xUnit2;

/// <summary>
/// Passes messages to the runner's sink from one thread, first in first out, as xUnit's own asynchronous
/// <c>MessageBus</c> does (plans/XUNIT2_FRAMEWORK_COMPOSITION_PLAN.md 4.2).
/// </summary>
/// <remarks>
/// Kronikol turns on synchronous message reporting so that its sink runs on each test's own flow, and with
/// collections in parallel that means many test threads at once. A runner's sink is written for the delivery
/// xUnit gives it by default: the VS adapters turn synchronous reporting on only when collections run one at a
/// time, and 2.8.2's sink keeps a counter without a lock. So only Kronikol's own bookkeeping runs on the test's
/// thread, and the runner's sink sees what it sees without Kronikol: one thread, in order. As in xUnit's bus, a
/// <c>false</c> from the sink cancels the run for good, and an exception from it becomes an
/// <see cref="ErrorMessage"/>.
/// </remarks>
internal sealed class OrderedMessageForwarder : IDisposable
{
    private readonly IMessageSink _sink;
    private readonly BlockingCollection<IMessageSinkMessage> _queue = new();
    private readonly Thread _worker;
    private volatile bool _continueRunning = true;

    public OrderedMessageForwarder(IMessageSink sink)
    {
        _sink = sink;
        _worker = new Thread(Deliver) { IsBackground = true, Name = "Kronikol xUnit v2 message forwarder" };

        // The worker carries no test's AsyncLocals: it starts before any test, and flows nothing in.
        using (ExecutionContext.SuppressFlow())
            _worker.Start();
    }

    /// <summary>The runner's answer so far: false once its sink has asked to stop.</summary>
    public bool ContinueRunning => _continueRunning;

    /// <summary>Queues <paramref name="message"/> and answers what the runner said so far, as xUnit's bus does.</summary>
    public bool Send(IMessageSinkMessage message)
    {
        try
        {
            _queue.Add(message);
        }
        catch (InvalidOperationException)
        {
            // Sent after the run finished, which no framework Kronikol knows of does: the worker is gone, so
            // deliver it here rather than drop it.
            Deliver(message);
        }

        return _continueRunning;
    }

    /// <summary>Waits until everything sent so far has reached the runner's sink, then stops the worker.</summary>
    public void Complete()
    {
        _queue.CompleteAdding();
        if (Thread.CurrentThread != _worker)
            _worker.Join();
    }

    public void Dispose() => Complete();

    private void Deliver()
    {
        foreach (var message in _queue.GetConsumingEnumerable())
            Deliver(message);
    }

    private void Deliver(IMessageSinkMessage message)
    {
        try
        {
            if (!_sink.OnMessage(message))
                _continueRunning = false;
        }
        catch (Exception ex)
        {
            try
            {
                if (!_sink.OnMessage(new ErrorMessage(Enumerable.Empty<ITestCase>(), ex)))
                    _continueRunning = false;
            }
            catch
            {
                // The runner's sink refused the error too; xUnit's bus gives up on it the same way.
            }
        }
    }
}
