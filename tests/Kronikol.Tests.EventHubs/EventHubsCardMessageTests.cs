using Kronikol.Extensions.EventHubs;
using Kronikol.Tests.CallGraph;
using Kronikol.Tracking;

namespace Kronikol.Tests.EventHubs;

/// <summary>
/// plans/SERVICE_CARD_PLAN.md 5.1.7, this package's row of the service card's message table: every call the tracker
/// records, for each operation at each verbosity, reads as its operation. A send publishes; a read or a processed event
/// consumes; making a batch, the property reads and the processor's start and stop are no message. The hub is the first
/// path segment, never the partition after it.
/// </summary>
public class EventHubsCardMessageTests
{
    private static readonly Dictionary<EventHubsOperation, string> Directions = new()
    {
        [EventHubsOperation.Send] = "publishes",
        [EventHubsOperation.SendBatch] = "publishes",
        [EventHubsOperation.CreateBatch] = "no message",
        [EventHubsOperation.ReadEvents] = "consumes",
        [EventHubsOperation.ReadEventsFromPartition] = "consumes",
        [EventHubsOperation.GetPartitionIds] = "no message",
        [EventHubsOperation.GetEventHubProperties] = "no message",
        [EventHubsOperation.GetPartitionProperties] = "no message",
        [EventHubsOperation.StartProcessing] = "no message",
        [EventHubsOperation.StopProcessing] = "no message",
        [EventHubsOperation.ProcessEvent] = "consumes",
        [EventHubsOperation.Other] = "no direction"
    };

    private readonly string _testId = Guid.NewGuid().ToString();

    public static TheoryData<EventHubsOperation, EventHubsTrackingVerbosity> Calls()
    {
        var calls = new TheoryData<EventHubsOperation, EventHubsTrackingVerbosity>();
        foreach (var operation in Enum.GetValues<EventHubsOperation>())
            foreach (var verbosity in Enum.GetValues<EventHubsTrackingVerbosity>())
                calls.Add(operation, verbosity);
        return calls;
    }

    private RequestResponseLog Track(EventHubsOperationInfo operation, EventHubsTrackingVerbosity verbosity)
    {
        var tracker = new EventHubsTracker(new EventHubsTrackingOptions
        {
            ServiceName = "Event Hubs",
            CallerName = TrackedMessages.Subject,
            Verbosity = verbosity,
            CurrentTestInfoFetcher = () => ("Order pancakes", _testId)
        });
        tracker.LogRequest(operation, """{"orderId":"order-1"}""");
        return RequestResponseLogger.RequestAndResponseLogs.Last(l => l.TestId == _testId && l.Type == RequestResponseType.Request);
    }

    [Theory]
    [MemberData(nameof(Calls))]
    public void Each_call_the_tracker_records_reads_as_its_operation(EventHubsOperation operation, EventHubsTrackingVerbosity verbosity)
    {
        var call = Track(new EventHubsOperationInfo(operation, "telemetry", "0", 3), verbosity);

        Assert.Equal(Directions[operation], TrackedMessages.Direction(call));
        if (Directions[operation] != "no message")
            Assert.Equal("telemetry", TrackedMessages.Topic(call));
    }

    [Theory]
    [InlineData(EventHubsTrackingVerbosity.Raw)]
    [InlineData(EventHubsTrackingVerbosity.Detailed)]
    [InlineData(EventHubsTrackingVerbosity.Summarised)]
    public void A_send_to_a_hub_the_client_did_not_name_names_no_destination(EventHubsTrackingVerbosity verbosity)
    {
        var call = Track(new EventHubsOperationInfo(EventHubsOperation.Send, null), verbosity);

        Assert.Equal("publishes", TrackedMessages.Direction(call));
        Assert.Null(TrackedMessages.Topic(call));
        Assert.True(TrackedMessages.EmptyTarget(call));
    }
}
