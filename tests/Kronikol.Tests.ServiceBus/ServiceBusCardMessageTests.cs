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
}
