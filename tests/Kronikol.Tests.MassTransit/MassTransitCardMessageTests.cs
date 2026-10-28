using Kronikol.Extensions.MassTransit;
using Kronikol.Tests.CallGraph;
using Kronikol.Tracking;

namespace Kronikol.Tests.MassTransit;

/// <summary>
/// plans/SERVICE_CARD_PLAN.md 5.1.7, this package's row of the service card's message table: every call the tracker
/// records, for each operation at each verbosity, reads as its operation. A send or a publish publishes; a consume, and a
/// consume's fault, are the broker's delivery into the service and no operation it exposes; a fault is no message. At Raw
/// the URI is the transport's own address, whose scheme is outside the table, so the service's call publishes and a
/// delivery consumes whatever the label says: the documented limit. The destination is the queue at Detailed, the message
/// type at Summarised and the address's last segment at Raw.
/// </summary>
public class MassTransitCardMessageTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    public static TheoryData<MassTransitOperation, MassTransitTrackingVerbosity> Calls()
    {
        var calls = new TheoryData<MassTransitOperation, MassTransitTrackingVerbosity>();
        foreach (var operation in Enum.GetValues<MassTransitOperation>())
            foreach (var verbosity in Enum.GetValues<MassTransitTrackingVerbosity>())
                calls.Add(operation, verbosity);
        return calls;
    }

    private static string Expected(MassTransitOperation operation, MassTransitTrackingVerbosity verbosity) => (operation, verbosity) switch
    {
        (MassTransitOperation.Send or MassTransitOperation.Publish, _) => "publishes",
        (MassTransitOperation.Consume or MassTransitOperation.ConsumeFault, MassTransitTrackingVerbosity.Raw) => "consumes",
        (_, MassTransitTrackingVerbosity.Raw) => "publishes",
        (MassTransitOperation.Consume, _) => "consumes",
        (MassTransitOperation.Other, _) => "no direction",
        _ => "no message"
    };

    private RequestResponseLog Track(MassTransitOperation operation, MassTransitTrackingVerbosity verbosity)
    {
        var tracker = new MassTransitTracker(new MassTransitTrackingOptions
        {
            ServiceName = "RabbitMQ",
            CallerName = TrackedMessages.Subject,
            Verbosity = verbosity,
            CurrentTestInfoFetcher = () => ("Order pancakes", _testId)
        });
        var op = new MassTransitOperationInfo(operation, "OrderPlaced", new Uri("rabbitmq://localhost/orders"), new Uri("rabbitmq://localhost/kitchen"), Guid.NewGuid(), Guid.NewGuid());
        var message = new { OrderId = "order-1" };
        var fault = new InvalidOperationException("The kitchen is closed");
        switch (operation)
        {
            case MassTransitOperation.Publish:
                tracker.LogPublish(op, message);
                break;
            case MassTransitOperation.Consume:
                tracker.LogConsume(op, message);
                break;
            case MassTransitOperation.SendFault:
                tracker.LogSendFault(op, fault);
                break;
            case MassTransitOperation.PublishFault:
                tracker.LogPublishFault(op, fault);
                break;
            case MassTransitOperation.ConsumeFault:
                tracker.LogConsumeFault(op, fault);
                break;
            default:
                // a send, and an operation no observer names
                tracker.LogSend(op, message);
                break;
        }

        return RequestResponseLogger.RequestAndResponseLogs.Last(l => l.TestId == _testId && l.Type == RequestResponseType.Request);
    }

    [Theory]
    [MemberData(nameof(Calls))]
    public void Each_call_the_tracker_records_reads_as_its_operation(MassTransitOperation operation, MassTransitTrackingVerbosity verbosity)
    {
        var call = Track(operation, verbosity);

        Assert.Equal(Expected(operation, verbosity), TrackedMessages.Direction(call));
        Assert.False(TrackedMessages.Exposed(call));
    }

    [Theory]
    [InlineData(MassTransitTrackingVerbosity.Raw, "orders")]
    [InlineData(MassTransitTrackingVerbosity.Detailed, "orders")]
    [InlineData(MassTransitTrackingVerbosity.Summarised, "OrderPlaced")]
    public void A_send_and_a_consume_name_their_destination_where_the_tracker_writes_it(MassTransitTrackingVerbosity verbosity, string topic)
    {
        Assert.Equal(topic, TrackedMessages.Topic(Track(MassTransitOperation.Send, verbosity)));
        Assert.Equal(topic, TrackedMessages.Topic(Track(MassTransitOperation.Consume, verbosity)));
    }

    // masstransit:///unknown when the operation knows neither its address nor its type
    [Fact]
    public void A_send_with_no_address_and_no_type_names_no_destination()
    {
        var tracker = new MassTransitTracker(new MassTransitTrackingOptions
        {
            ServiceName = "RabbitMQ",
            CallerName = TrackedMessages.Subject,
            Verbosity = MassTransitTrackingVerbosity.Detailed,
            CurrentTestInfoFetcher = () => ("Order pancakes", _testId)
        });
        tracker.LogSend(new MassTransitOperationInfo(MassTransitOperation.Send, null), null);

        var call = RequestResponseLogger.RequestAndResponseLogs.Last(l => l.TestId == _testId && l.Type == RequestResponseType.Request);
        Assert.Null(TrackedMessages.Topic(call));
        Assert.True(TrackedMessages.EmptyTarget(call));
    }
}
