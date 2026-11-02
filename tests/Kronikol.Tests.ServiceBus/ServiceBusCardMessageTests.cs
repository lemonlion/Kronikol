using Kronikol.Extensions.ServiceBus;
using Kronikol.Tests.CallGraph;
using Kronikol.Tracking;

namespace Kronikol.Tests.ServiceBus;

/// <summary>
/// plans/SERVICE_CARD_PLAN.md 5.1.7, this package's row of the service card's message table: every call the tracker
/// records, for each operation at each verbosity, reads as its operation. A send or a schedule publishes; a receive or a
/// peek consumes; settlement, lock renewal, session state and the processor's start and stop are no message. The queue
/// or topic is the URI's authority, which #192's operation key drops from the target.
/// </summary>
[Collection("TrackingComponentRegistry")]
public class ServiceBusCardMessageTests : IDisposable
{
    private static readonly Dictionary<ServiceBusOperation, string> Directions = new()
    {
        [ServiceBusOperation.Send] = "publishes",
        [ServiceBusOperation.SendBatch] = "publishes",
        [ServiceBusOperation.Schedule] = "publishes",
        [ServiceBusOperation.CancelSchedule] = "no message",
        [ServiceBusOperation.Receive] = "consumes",
        [ServiceBusOperation.ReceiveBatch] = "consumes",
        [ServiceBusOperation.Peek] = "consumes",
        [ServiceBusOperation.Complete] = "no message",
        [ServiceBusOperation.Abandon] = "no message",
        [ServiceBusOperation.DeadLetter] = "no message",
        [ServiceBusOperation.Defer] = "no message",
        [ServiceBusOperation.RenewMessageLock] = "no message",
        [ServiceBusOperation.RenewSessionLock] = "no message",
        [ServiceBusOperation.GetSessionState] = "no message",
        [ServiceBusOperation.SetSessionState] = "no message",
        [ServiceBusOperation.StartProcessing] = "no message",
        [ServiceBusOperation.StopProcessing] = "no message",
        [ServiceBusOperation.Other] = "no direction"
    };

    private readonly string _testId = Guid.NewGuid().ToString();

    public ServiceBusCardMessageTests() => TrackingComponentRegistry.Clear();

    public void Dispose() => TrackingComponentRegistry.Clear();

    public static TheoryData<ServiceBusOperation, ServiceBusTrackingVerbosity> Calls()
    {
        var calls = new TheoryData<ServiceBusOperation, ServiceBusTrackingVerbosity>();
        foreach (var operation in Enum.GetValues<ServiceBusOperation>())
            foreach (var verbosity in Enum.GetValues<ServiceBusTrackingVerbosity>())
                calls.Add(operation, verbosity);
        return calls;
    }

    private RequestResponseLog Track(ServiceBusOperationInfo operation, ServiceBusTrackingVerbosity verbosity)
    {
        var tracker = new ServiceBusTracker(new ServiceBusTrackingOptions
        {
            ServiceName = "Service Bus",
            CallerName = TrackedMessages.Subject,
            Verbosity = verbosity,
            CurrentTestInfoFetcher = () => ("Order pancakes", _testId)
        });
        tracker.LogRequest(operation, """{"orderId":"order-1"}""");
        return RequestResponseLogger.RequestAndResponseLogs.Last(l => l.TestId == _testId && l.Type == RequestResponseType.Request);
    }

    [Theory]
    [MemberData(nameof(Calls))]
    public void Each_call_the_tracker_records_reads_as_its_operation(ServiceBusOperation operation, ServiceBusTrackingVerbosity verbosity)
    {
        var call = Track(new ServiceBusOperationInfo(operation, "orders", "billing", 3), verbosity);

        Assert.Equal(Directions[operation], TrackedMessages.Direction(call));
        if (Directions[operation] != "no message")
            Assert.Equal("orders", TrackedMessages.Topic(call));
    }

    [Theory]
    [InlineData(ServiceBusTrackingVerbosity.Raw)]
    [InlineData(ServiceBusTrackingVerbosity.Detailed)]
    [InlineData(ServiceBusTrackingVerbosity.Summarised)]
    public void A_send_to_a_queue_the_client_did_not_name_names_no_destination(ServiceBusTrackingVerbosity verbosity)
    {
        var call = Track(new ServiceBusOperationInfo(ServiceBusOperation.Send, null), verbosity);

        Assert.Equal("publishes", TrackedMessages.Direction(call));
        Assert.Null(TrackedMessages.Topic(call));
        Assert.True(TrackedMessages.EmptyTarget(call));
    }

    // plans/CALLS_AND_EVENTS_PLAN.md 6.7: an event exactly when the call moves a message, at every verbosity, and a response
    // carries its request's kind. The oracle is this class's own reading of the operation, not MessageTable: the table
    // cannot read a call recorded as its HTTP request or under its transport's address.
    [Theory]
    [MemberData(nameof(Calls))]
    public void A_call_is_an_event_exactly_when_it_moves_a_message(ServiceBusOperation operation, ServiceBusTrackingVerbosity verbosity)
    {
        var tracker = new ServiceBusTracker(new ServiceBusTrackingOptions
        {
            ServiceName = "Service Bus",
            CallerName = TrackedMessages.Subject,
            Verbosity = verbosity,
            CurrentTestInfoFetcher = () => ("Order pancakes", _testId)
        });
        var info = new ServiceBusOperationInfo(operation, "orders", "billing", 3);
        var (id, trace) = tracker.LogRequest(info, "{}");
        tracker.LogResponse(info, id, trace, "{}");

        var expected = Directions[operation] is "publishes" or "consumes" ? RequestResponseMetaType.Event : RequestResponseMetaType.Default;
        var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();
        Assert.Equal(2, logs.Length);
        Assert.All(logs, log => Assert.Equal(expected, log.MetaType));
    }
}
