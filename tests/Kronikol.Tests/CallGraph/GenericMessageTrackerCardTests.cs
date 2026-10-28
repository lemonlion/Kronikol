using Kronikol.Tracking;

namespace Kronikol.Tests.CallGraph;

/// <summary>
/// The message table held to the generic <see cref="MessageTracker"/>, whose calls the fact reads back from the request
/// log: in the DiagramsFetcher collection, beside the ingests that clear it (<c>ProcessGlobalStoreTests</c>).
/// </summary>
[Collection("DiagramsFetcher")]
public class GenericMessageTrackerCardTests
{
    // The generic MessageTracker with its default options, as Example.Api uses it: a consume is logged broker to consumer,
    // with MessageQueue on the delivery, and a send event from the service to the broker.
    [Fact]
    public void The_generic_trackers_consume_is_a_delivery_and_its_send_a_publish()
    {
        using var isolation = RequestResponseLogger.IsolateForTests();
        var testId = Guid.NewGuid().ToString();
        new MessageTracker(new MessageTrackerOptions { CallerName = "Event broker", CurrentTestInfoFetcher = () => ("Order pancakes", testId) })
            .TrackConsumeEvent("Consume", TrackedMessages.Subject, new Uri("event://cake_events"), new { Cake = "Victoria sponge" });
        new MessageTracker(new MessageTrackerOptions { CallerName = TrackedMessages.Subject, CurrentTestInfoFetcher = () => ("Order pancakes", testId) })
            .TrackSendEvent("Publish", "Event broker", new Uri("event://cake_events"), new { Cake = "Victoria sponge" });

        var (consume, send) = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId && l.Type == RequestResponseType.Request).ToArray() switch
        {
            [var first, var second] => (first, second),
            var logs => throw new InvalidOperationException($"{logs.Length} requests")
        };
        Assert.Equal(("consumes", "cake_events"), (TrackedMessages.Direction(consume), TrackedMessages.Topic(consume)));
        Assert.False(TrackedMessages.Exposed(consume));
        Assert.Equal(("publishes", "cake_events"), (TrackedMessages.Direction(send), TrackedMessages.Topic(send)));
    }
}
