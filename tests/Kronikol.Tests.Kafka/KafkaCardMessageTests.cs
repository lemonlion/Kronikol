using Kronikol.Extensions.Kafka;
using Kronikol.Tests.CallGraph;
using Kronikol.Tracking;

namespace Kronikol.Tests.Kafka;

/// <summary>
/// plans/SERVICE_CARD_PLAN.md 5.1.7, this package's row of the service card's message table: every call the tracker
/// records, for each operation at each verbosity, reads as its operation. A produce publishes; a consume is the broker's
/// delivery into the service, so it consumes and is no operation the service exposes; subscriptions, commits, flushes and
/// the transaction calls are no message. The topic is the first path segment, never Raw's partition and offset.
/// </summary>
public class KafkaCardMessageTests
{
    private static readonly Dictionary<KafkaOperation, string> Directions = new()
    {
        [KafkaOperation.Produce] = "publishes",
        [KafkaOperation.ProduceAsync] = "publishes",
        [KafkaOperation.Consume] = "consumes",
        [KafkaOperation.Subscribe] = "no message",
        [KafkaOperation.Unsubscribe] = "no message",
        [KafkaOperation.Commit] = "no message",
        [KafkaOperation.Flush] = "no message",
        [KafkaOperation.InitTransactions] = "no message",
        [KafkaOperation.BeginTransaction] = "no message",
        [KafkaOperation.CommitTransaction] = "no message",
        [KafkaOperation.AbortTransaction] = "no message",
        [KafkaOperation.SendOffsetsToTransaction] = "no message",
        [KafkaOperation.Other] = "no direction"
    };

    private readonly string _testId = Guid.NewGuid().ToString();

    public static TheoryData<KafkaOperation, KafkaTrackingVerbosity> Calls()
    {
        var calls = new TheoryData<KafkaOperation, KafkaTrackingVerbosity>();
        foreach (var operation in Enum.GetValues<KafkaOperation>())
            foreach (var verbosity in Enum.GetValues<KafkaTrackingVerbosity>())
                calls.Add(operation, verbosity);
        return calls;
    }

    private RequestResponseLog Track(KafkaOperation operation, KafkaTrackingVerbosity verbosity)
    {
        var tracker = new KafkaTracker(new KafkaTrackingOptions
        {
            ServiceName = "Kafka",
            CallerName = TrackedMessages.Subject,
            Verbosity = verbosity,
            TrackSubscribe = true,
            TrackUnsubscribe = true,
            TrackCommit = true,
            TrackFlush = true,
            TrackTransactions = true,
            CurrentTestInfoFetcher = () => ("Order pancakes", _testId)
        });
        var op = new KafkaOperationInfo(operation, "orders", 2, 17);
        switch (operation)
        {
            case KafkaOperation.Produce or KafkaOperation.ProduceAsync:
                tracker.LogProduce(op, "Key: order-1, Value: {}");
                break;
            case KafkaOperation.Consume:
                tracker.LogConsume(op, "Key: order-1, Value: {}");
                break;
            case KafkaOperation.Subscribe:
                tracker.LogSubscribe(op);
                break;
            case KafkaOperation.Unsubscribe:
                tracker.LogUnsubscribe(op);
                break;
            case KafkaOperation.Commit:
                tracker.LogCommit(op);
                break;
            case KafkaOperation.Flush:
                tracker.LogFlush(op);
                break;
            default:
                // the transaction calls, and an operation no client call names
                tracker.LogTransaction(op);
                break;
        }

        return RequestResponseLogger.RequestAndResponseLogs.Last(l => l.TestId == _testId && l.Type == RequestResponseType.Request);
    }

    [Theory]
    [MemberData(nameof(Calls))]
    public void Each_call_the_tracker_records_reads_as_its_operation(KafkaOperation operation, KafkaTrackingVerbosity verbosity)
    {
        var call = Track(operation, verbosity);

        Assert.Equal(Directions[operation], TrackedMessages.Direction(call));
        Assert.False(TrackedMessages.Exposed(call));
    }

    [Theory]
    [InlineData(KafkaTrackingVerbosity.Raw, "orders")]
    [InlineData(KafkaTrackingVerbosity.Detailed, "orders")]
    [InlineData(KafkaTrackingVerbosity.Summarised, null)]
    public void A_produce_names_its_topic_where_the_tracker_writes_one(KafkaTrackingVerbosity verbosity, string? topic)
    {
        var call = Track(KafkaOperation.Produce, verbosity);

        Assert.Equal(topic, TrackedMessages.Topic(call));
        Assert.Equal(topic is null, TrackedMessages.EmptyTarget(call));
    }

    [Theory]
    [InlineData(KafkaTrackingVerbosity.Raw, "orders")]
    [InlineData(KafkaTrackingVerbosity.Detailed, "orders")]
    [InlineData(KafkaTrackingVerbosity.Summarised, null)]
    public void A_consume_names_its_topic_where_the_tracker_writes_one(KafkaTrackingVerbosity verbosity, string? topic) =>
        Assert.Equal(topic, TrackedMessages.Topic(Track(KafkaOperation.Consume, verbosity)));
}
