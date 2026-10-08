using Kronikol.InternalFlow;

namespace Kronikol.Tests.InternalFlow;

/// <summary>
/// <see cref="InternalFlowActivityListener.EnsureStarted"/>, which every handler and gRPC interceptor calls on first
/// use. These facts reset the process-wide listener, so they run in <see cref="SpanStoreClearCollection"/>, after every
/// parallel class: a class whose handler had started it would record no span until another handler started it again.
/// </summary>
[Collection(SpanStoreClearCollection.Name)]
public class InternalFlowActivityListenerStartTests
{
    [Fact]
    public void EnsureStarted_returns_to_each_concurrent_caller_once_the_listener_is_registered()
    {
        // A caller that returned while another was still registering the listener started its next span unsampled: a
        // gRPC call made then logged a trace id no span carried (Kronikol.Tests.Grpc on CI, run 37779628618). One round
        // is a race the caller can win or lose, so the fact runs fifty.
        const int threadCount = 8;
        for (var round = 0; round < 50; round++)
        {
            InternalFlowActivityListener.ResetForTesting();
            var barrier = new Barrier(threadCount);
            var registeredOnReturn = new bool[threadCount];
            var threads = Enumerable.Range(0, threadCount).Select(idx => new Thread(() =>
            {
                barrier.SignalAndWait();
                InternalFlowActivityListener.EnsureStarted();
                registeredOnReturn[idx] = InternalFlowActivityListener.IsStarted;
            })).ToArray();

            foreach (var thread in threads)
                thread.Start();
            foreach (var thread in threads)
                Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "EnsureStarted blocked a concurrent caller");

            Assert.All(registeredOnReturn, registered =>
                Assert.True(registered, $"round {round}: EnsureStarted returned before the listener was registered"));
        }
    }

    [Fact]
    public void EnsureStarted_does_not_block_concurrent_callers()
    {
        // Issue #70: On constrained thread pools (Linux CI), multiple handlers
        // calling EnsureStarted() concurrently must not block on a lock.
        // All threads should return immediately (only one performs initialization).
        InternalFlowActivityListener.ResetForTesting();

        const int threadCount = 8;
        var barrier = new Barrier(threadCount);
        var completionFlags = new bool[threadCount];
        var threads = new Thread[threadCount];

        for (var i = 0; i < threadCount; i++)
        {
            var idx = i;
            threads[idx] = new Thread(() =>
            {
                barrier.SignalAndWait(); // Ensure all threads start simultaneously
                InternalFlowActivityListener.EnsureStarted();
                completionFlags[idx] = true;
            });
            threads[idx].Start();
        }

        // All threads must complete within 5 seconds (would hang with old lock pattern
        // if thread pool were exhausted, but here we use dedicated threads to prove non-blocking)
        foreach (var thread in threads)
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "EnsureStarted blocked a concurrent caller");

        Assert.All(completionFlags, flag => Assert.True(flag));
    }
}
