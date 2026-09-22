using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

// The collection every class in this assembly that clears the process-wide tracking state is in
// (TestIdentityScopeCollection.cs says why): two collections run in parallel and clear each other's state.
[Collection("DiagramsFetcher")]
public class CorrelationKeysTests
{
    [Fact]
    public void Cosmos_two_part_key()
    {
        var key = CorrelationKeys.Cosmos("Orders", "doc-123");
        Assert.Equal("cosmos:Orders:doc-123", key);
    }

    [Fact]
    public void Cosmos_three_part_key_with_partition()
    {
        var key = CorrelationKeys.Cosmos("Orders", "pk-value", "doc-123");
        Assert.Equal("cosmos:Orders:pk-value:doc-123", key);
    }

    [Fact]
    public void Mongo_key()
    {
        var key = CorrelationKeys.Mongo("Users", "507f1f77bcf86cd799439011");
        Assert.Equal("mongo:Users:507f1f77bcf86cd799439011", key);
    }

    [Fact]
    public void Kafka_key()
    {
        var key = CorrelationKeys.Kafka("Order Events", "order-456");
        Assert.Equal("kafka:Order Events:order-456", key);
    }

    [Fact]
    public void ServiceBus_key()
    {
        var key = CorrelationKeys.ServiceBus("order-queue", "msg-789");
        Assert.Equal("servicebus:order-queue:msg-789", key);
    }

    [Fact]
    public void EventHubs_key()
    {
        var key = CorrelationKeys.EventHubs("telemetry-hub", "seq-42");
        Assert.Equal("eventhubs:telemetry-hub:seq-42", key);
    }

    [Fact]
    public void PubSub_key()
    {
        var key = CorrelationKeys.PubSub("notifications", "pub-msg-1");
        Assert.Equal("pubsub:notifications:pub-msg-1", key);
    }

    [Fact]
    public void Sqs_key()
    {
        var key = CorrelationKeys.Sqs("processing-queue", "sqs-msg-1");
        Assert.Equal("sqs:processing-queue:sqs-msg-1", key);
    }

    [Fact]
    public void Sns_key()
    {
        var key = CorrelationKeys.Sns("alerts-topic", "sns-msg-1");
        Assert.Equal("sns:alerts-topic:sns-msg-1", key);
    }

    [Fact]
    public void StorageQueue_key()
    {
        var key = CorrelationKeys.StorageQueue("work-items", "queue-msg-1");
        Assert.Equal("storagequeue:work-items:queue-msg-1", key);
    }

    [Fact]
    public void Custom_key()
    {
        var key = CorrelationKeys.Custom("hangfire", "email-sender", "job-123");
        Assert.Equal("hangfire:email-sender:job-123", key);
    }

    [Fact]
    public void Keys_are_consistent_between_correlate_and_resolve()
    {
        // Simulate write-time correlation
        var writeKey = CorrelationKeys.Cosmos("Orders", "doc-1");
        TestCorrelationStore.Correlate(writeKey, "Test A", "id-a");

        // Simulate processing-time resolution with same helper
        var resolveKey = CorrelationKeys.Cosmos("Orders", "doc-1");
        var result = TestCorrelationStore.Resolve(resolveKey);

        Assert.NotNull(result);
        Assert.Equal("Test A", result.Value.Name);

        TestCorrelationStore.Clear();
    }
}
