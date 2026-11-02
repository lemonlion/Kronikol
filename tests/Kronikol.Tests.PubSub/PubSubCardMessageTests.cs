using Kronikol.Extensions.PubSub;
using Kronikol.Tests.CallGraph;
using Kronikol.Tracking;

namespace Kronikol.Tests.PubSub;

/// <summary>
/// plans/SERVICE_CARD_PLAN.md 5.1.7, this package's row of the service card's message table: every call the tracker
/// records, for each operation at each verbosity, reads as its operation. A publish publishes; a pull or a streamed
/// receive consumes; acknowledgements and the subscriber's start and stop are no message. The topic or subscription is
/// the URI's last path segment: Raw writes the full resource name, <c>projects/{project}/topics/{topic}</c>.
/// </summary>
public class PubSubCardMessageTests
{
    private const string Topic = "projects/breakfast/topics/orders";
    private const string Subscription = "projects/breakfast/subscriptions/orders-kitchen";

    private static readonly Dictionary<PubSubOperation, string> Directions = new()
    {
        [PubSubOperation.Publish] = "publishes",
        [PubSubOperation.PublishBatch] = "publishes",
        [PubSubOperation.Pull] = "consumes",
        [PubSubOperation.Acknowledge] = "no message",
        [PubSubOperation.ModifyAckDeadline] = "no message",
        [PubSubOperation.Receive] = "consumes",
        [PubSubOperation.StartSubscriber] = "no message",
        [PubSubOperation.StopSubscriber] = "no message",
        [PubSubOperation.Other] = "no direction"
    };

    private readonly string _testId = Guid.NewGuid().ToString();

    public static TheoryData<PubSubOperation, PubSubTrackingVerbosity> Calls()
    {
        var calls = new TheoryData<PubSubOperation, PubSubTrackingVerbosity>();
        foreach (var operation in Enum.GetValues<PubSubOperation>())
            foreach (var verbosity in Enum.GetValues<PubSubTrackingVerbosity>())
                calls.Add(operation, verbosity);
        return calls;
    }

    private RequestResponseLog Track(PubSubOperationInfo operation, PubSubTrackingVerbosity verbosity)
    {
        var tracker = new PubSubTracker(new PubSubTrackingOptions
        {
            ServiceName = "Pub/Sub",
            CallerName = TrackedMessages.Subject,
            Verbosity = verbosity,
            CurrentTestInfoFetcher = () => ("Order pancakes", _testId)
        });
        tracker.LogRequest(operation, """{"orderId":"order-1"}""");
        return RequestResponseLogger.RequestAndResponseLogs.Last(l => l.TestId == _testId && l.Type == RequestResponseType.Request);
    }

    [Theory]
    [MemberData(nameof(Calls))]
    public void Each_call_the_tracker_records_reads_as_its_operation(PubSubOperation operation, PubSubTrackingVerbosity verbosity)
    {
        var call = Track(new PubSubOperationInfo(operation, Topic, Subscription, 2), verbosity);

        Assert.Equal(Directions[operation], TrackedMessages.Direction(call));
        if (Directions[operation] != "no message")
            Assert.Equal("orders", TrackedMessages.Topic(call));
    }

    [Theory]
    [InlineData(PubSubTrackingVerbosity.Raw)]
    [InlineData(PubSubTrackingVerbosity.Detailed)]
    [InlineData(PubSubTrackingVerbosity.Summarised)]
    public void A_pull_names_its_subscription(PubSubTrackingVerbosity verbosity)
    {
        var call = Track(new PubSubOperationInfo(PubSubOperation.Pull, null, Subscription), verbosity);

        Assert.Equal("consumes", TrackedMessages.Direction(call));
        Assert.Equal("orders-kitchen", TrackedMessages.Topic(call));
    }

    [Theory]
    [InlineData(PubSubTrackingVerbosity.Raw)]
    [InlineData(PubSubTrackingVerbosity.Detailed)]
    [InlineData(PubSubTrackingVerbosity.Summarised)]
    public void A_publish_to_a_topic_the_client_did_not_name_names_no_destination(PubSubTrackingVerbosity verbosity)
    {
        var call = Track(new PubSubOperationInfo(PubSubOperation.Publish, null, null), verbosity);

        Assert.Null(TrackedMessages.Topic(call));
        Assert.True(TrackedMessages.EmptyTarget(call));
    }

    // plans/CALLS_AND_EVENTS_PLAN.md 6.7: an event exactly when the call moves a message, at every verbosity, and a response
    // carries its request's kind. The oracle is this class's own reading of the operation, not MessageTable: the table
    // cannot read a call recorded as its HTTP request or under its transport's address.
    [Theory]
    [MemberData(nameof(Calls))]
    public void A_call_is_an_event_exactly_when_it_moves_a_message(PubSubOperation operation, PubSubTrackingVerbosity verbosity)
    {
        var tracker = new PubSubTracker(new PubSubTrackingOptions
        {
            ServiceName = "Pub/Sub",
            CallerName = TrackedMessages.Subject,
            Verbosity = verbosity,
            CurrentTestInfoFetcher = () => ("Order pancakes", _testId)
        });
        var info = new PubSubOperationInfo(operation, Topic, Subscription, 2);
        var (id, trace) = tracker.LogRequest(info, "{}");
        tracker.LogResponse(info, id, trace, "{}");

        var expected = Directions[operation] is "publishes" or "consumes" ? RequestResponseMetaType.Event : RequestResponseMetaType.Default;
        var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();
        Assert.Equal(2, logs.Length);
        Assert.All(logs, log => Assert.Equal(expected, log.MetaType));
    }
}
