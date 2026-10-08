using Kronikol.xUnit2;
using Xunit.Abstractions;
using static Kronikol.Tests.xUnit2.XunitMessages;

namespace Kronikol.Tests.xUnit2;

/// <summary>
/// The queue between Kronikol's sink and the runner's (plans/XUNIT2_FRAMEWORK_COMPOSITION_PLAN.md 4.2, T9): the
/// runner's sink gets what xUnit's own asynchronous bus would give it, whatever thread sends.
/// </summary>
public class OrderedMessageForwarderTests
{
    [Fact]
    public void Twenty_threads_at_once_reach_the_runner_one_at_a_time_and_each_in_its_own_order()
    {
        var runner = new RecordingSink();
        var test = Test<TrackedSinkTests>(nameof(TrackedSinkTests.Passes));
        const int threads = 20, perThread = 50;
        var sent = new IMessageSinkMessage[threads][];
        using var forwarder = new OrderedMessageForwarder(runner);
        using var go = new ManualResetEventSlim();

        var senders = Enumerable.Range(0, threads).Select(t =>
        {
            sent[t] = Enumerable.Range(0, perThread).Select(_ => (IMessageSinkMessage)Passed(test)).ToArray();
            var thread = new Thread(() =>
            {
                go.Wait();
                foreach (var message in sent[t])
                    forwarder.Send(message);
            });
            thread.Start();
            return thread;
        }).ToArray();
        go.Set();
        foreach (var sender in senders)
            sender.Join();
        forwarder.Complete();

        Assert.Equal(1, runner.MostAtOnce);
        Assert.Single(runner.Received.Select(r => r.Thread).Distinct());
        var received = runner.Messages;
        Assert.Equal(threads * perThread, received.Length);
        foreach (var messages in sent)
            Assert.Equal(messages, received.Where(m => messages.Contains(m)));
    }

    [Fact]
    public void A_false_from_the_runner_reaches_the_bus_on_a_later_message_and_stays()
    {
        using var received = new ManualResetEventSlim();
        var stop = Passed(Test<TrackedSinkTests>(nameof(TrackedSinkTests.Passes)));
        var runner = new RecordingSink
        {
            Answer = message => !ReferenceEquals(message, stop),
            OnReceived = message => { if (ReferenceEquals(message, stop)) received.Set(); },
        };
        using var forwarder = new OrderedMessageForwarder(runner);

        Assert.True(forwarder.Send(AssemblyStarting()));
        forwarder.Send(stop);
        Assert.True(received.Wait(TimeSpan.FromSeconds(10)));
        SpinWait.SpinUntil(() => !forwarder.ContinueRunning, TimeSpan.FromSeconds(10));

        Assert.False(forwarder.Send(Passed(Test<TrackedSinkTests>(nameof(TrackedSinkTests.Fails)))));
        Assert.False(forwarder.Send(AssemblyFinished()));
    }

    [Fact]
    public void An_exception_from_the_runner_becomes_an_ErrorMessage_and_the_next_message_still_arrives()
    {
        var test = Test<TrackedSinkTests>(nameof(TrackedSinkTests.Passes));
        var throws = Passed(test);
        var runner = new RecordingSink
        {
            OnReceived = message =>
            {
                if (ReferenceEquals(message, throws))
                    throw new InvalidOperationException("the runner's sink broke");
            },
        };
        using var forwarder = new OrderedMessageForwarder(runner);

        forwarder.Send(throws);
        forwarder.Send(AssemblyFinished());
        forwarder.Complete();

        var messages = runner.Messages;
        var error = Assert.IsAssignableFrom<IErrorMessage>(messages[0]);
        Assert.Equal(typeof(InvalidOperationException).FullName, error.ExceptionTypes[0]);
        Assert.IsAssignableFrom<ITestAssemblyFinished>(messages[1]);
        Assert.True(forwarder.ContinueRunning);
    }

    [Fact]
    public void A_message_sent_after_the_run_finished_is_still_delivered()
    {
        var runner = new RecordingSink();
        using var forwarder = new OrderedMessageForwarder(runner);
        forwarder.Send(AssemblyFinished());
        forwarder.Complete();

        var late = Passed(Test<TrackedSinkTests>(nameof(TrackedSinkTests.Passes)));
        forwarder.Send(late);

        Assert.Same(late, runner.Messages.Last());
    }
}
