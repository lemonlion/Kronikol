using Kronikol.Extensions.Kafka;

namespace Kronikol.Tests.Kafka;

public class KafkaOperationClassifierTests
{
    // ──────────────────────────────────────────────────────────
    //  Diagram labels — Detailed
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void GetDiagramLabel_Detailed_Produce_IncludesTopic()
    {
        var op = new KafkaOperationInfo(KafkaOperation.Produce, "orders-topic");

        var label = KafkaOperationClassifier.GetDiagramLabel(op, KafkaTrackingVerbosity.Detailed);

        Assert.Equal("Produce → orders-topic", label);
    }

    [Fact]
    public void GetDiagramLabel_Detailed_ProduceAsync_IncludesTopic()
    {
        var op = new KafkaOperationInfo(KafkaOperation.ProduceAsync, "orders-topic");

        var label = KafkaOperationClassifier.GetDiagramLabel(op, KafkaTrackingVerbosity.Detailed);

        Assert.Equal("Produce → orders-topic", label);
    }

    [Fact]
    public void GetDiagramLabel_Detailed_Consume_IncludesTopic()
    {
        var op = new KafkaOperationInfo(KafkaOperation.Consume, "orders-topic");

        var label = KafkaOperationClassifier.GetDiagramLabel(op, KafkaTrackingVerbosity.Detailed);

        Assert.Equal("Consume ← orders-topic", label);
    }

    [Fact]
    public void GetDiagramLabel_Detailed_Subscribe_IncludesTopic()
    {
        var op = new KafkaOperationInfo(KafkaOperation.Subscribe, "orders-topic");

        var label = KafkaOperationClassifier.GetDiagramLabel(op, KafkaTrackingVerbosity.Detailed);

        Assert.Equal("Subscribe orders-topic", label);
    }

    [Fact]
    public void GetDiagramLabel_Detailed_Flush_ReturnsOperationName()
    {
        var op = new KafkaOperationInfo(KafkaOperation.Flush);

        var label = KafkaOperationClassifier.GetDiagramLabel(op, KafkaTrackingVerbosity.Detailed);

        Assert.Equal("Flush", label);
    }

    // ──────────────────────────────────────────────────────────
    //  Diagram labels — Summarised
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void GetDiagramLabel_Summarised_Produce()
    {
        var op = new KafkaOperationInfo(KafkaOperation.Produce, "orders-topic");

        var label = KafkaOperationClassifier.GetDiagramLabel(op, KafkaTrackingVerbosity.Summarised);

        Assert.Equal("Produce", label);
    }

    [Fact]
    public void GetDiagramLabel_Summarised_Consume()
    {
        var op = new KafkaOperationInfo(KafkaOperation.Consume, "orders-topic");

        var label = KafkaOperationClassifier.GetDiagramLabel(op, KafkaTrackingVerbosity.Summarised);

        Assert.Equal("Consume", label);
    }

    // ──────────────────────────────────────────────────────────
    //  Diagram labels — Raw
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void GetDiagramLabel_Raw_IncludesPartitionAndOffset()
    {
        var op = new KafkaOperationInfo(KafkaOperation.ProduceAsync, "orders-topic", 2, 42);

        var label = KafkaOperationClassifier.GetDiagramLabel(op, KafkaTrackingVerbosity.Raw);

        Assert.Equal("ProduceAsync orders-topic[2]@42", label);
    }

    [Fact]
    public void GetDiagramLabel_Raw_OmitsPartitionWhenNull()
    {
        var op = new KafkaOperationInfo(KafkaOperation.ProduceAsync, "orders-topic");

        var label = KafkaOperationClassifier.GetDiagramLabel(op, KafkaTrackingVerbosity.Raw);

        Assert.Equal("ProduceAsync orders-topic", label);
    }

    // F11 (plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.md): an operation without a topic is its name alone, as the wiki shows it.
    [Theory]
    [InlineData(KafkaOperation.Commit)]
    [InlineData(KafkaOperation.Flush)]
    [InlineData(KafkaOperation.Unsubscribe)]
    [InlineData(KafkaOperation.InitTransactions)]
    [InlineData(KafkaOperation.BeginTransaction)]
    [InlineData(KafkaOperation.CommitTransaction)]
    [InlineData(KafkaOperation.AbortTransaction)]
    [InlineData(KafkaOperation.SendOffsetsToTransaction)]
    public void GetDiagramLabel_Raw_is_the_operations_name_alone_when_it_has_no_topic(KafkaOperation operation)
    {
        var label = KafkaOperationClassifier.GetDiagramLabel(new KafkaOperationInfo(operation), KafkaTrackingVerbosity.Raw);

        Assert.Equal(operation.ToString(), label);
    }

    // ──────────────────────────────────────────────────────────
    //  URI building
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void BuildUri_Raw_IncludesPartition()
    {
        var op = new KafkaOperationInfo(KafkaOperation.ProduceAsync, "orders-topic", 2, 42);

        var uri = KafkaOperationClassifier.BuildUri(op, KafkaTrackingVerbosity.Raw);

        Assert.Contains("orders-topic", uri.ToString());
        Assert.Contains("2", uri.ToString());
    }

    [Fact]
    public void BuildUri_Detailed_UsesTopicOnly()
    {
        var op = new KafkaOperationInfo(KafkaOperation.ProduceAsync, "orders-topic", 2, 42);

        var uri = KafkaOperationClassifier.BuildUri(op, KafkaTrackingVerbosity.Detailed);

        Assert.Equal("kafka:///orders-topic", uri.ToString());
    }

    [Fact]
    public void BuildUri_Summarised_ReturnsBase()
    {
        var op = new KafkaOperationInfo(KafkaOperation.ProduceAsync, "orders-topic");

        var uri = KafkaOperationClassifier.BuildUri(op, KafkaTrackingVerbosity.Summarised);

        Assert.Equal("kafka:///", uri.ToString());
    }

    [Fact]
    public void BuildUri_Detailed_NoTopic_ReturnsBase()
    {
        var op = new KafkaOperationInfo(KafkaOperation.Flush);

        var uri = KafkaOperationClassifier.BuildUri(op, KafkaTrackingVerbosity.Detailed);

        Assert.Equal("kafka:///", uri.ToString());
    }

    // ──────────────────────────────────────────────────────────
    //  Diagram labels — Commit / Unsubscribe
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void GetDiagramLabel_Detailed_Commit_ReturnsOperationName()
    {
        var op = new KafkaOperationInfo(KafkaOperation.Commit);

        var label = KafkaOperationClassifier.GetDiagramLabel(op, KafkaTrackingVerbosity.Detailed);

        Assert.Equal("Commit", label);
    }

    [Fact]
    public void GetDiagramLabel_Detailed_Unsubscribe_ReturnsOperationName()
    {
        var op = new KafkaOperationInfo(KafkaOperation.Unsubscribe);

        var label = KafkaOperationClassifier.GetDiagramLabel(op, KafkaTrackingVerbosity.Detailed);

        Assert.Equal("Unsubscribe", label);
    }

    [Fact]
    public void GetDiagramLabel_Summarised_Commit()
    {
        var op = new KafkaOperationInfo(KafkaOperation.Commit);

        var label = KafkaOperationClassifier.GetDiagramLabel(op, KafkaTrackingVerbosity.Summarised);

        Assert.Equal("Commit", label);
    }

    [Fact]
    public void GetDiagramLabel_Summarised_Unsubscribe()
    {
        var op = new KafkaOperationInfo(KafkaOperation.Unsubscribe);

        var label = KafkaOperationClassifier.GetDiagramLabel(op, KafkaTrackingVerbosity.Summarised);

        Assert.Equal("Unsubscribe", label);
    }

    // ──────────────────────────────────────────────────────────
    //  Diagram labels — Transactions
    // ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(KafkaOperation.InitTransactions, "InitTransactions")]
    [InlineData(KafkaOperation.BeginTransaction, "BeginTransaction")]
    [InlineData(KafkaOperation.CommitTransaction, "CommitTransaction")]
    [InlineData(KafkaOperation.AbortTransaction, "AbortTransaction")]
    [InlineData(KafkaOperation.SendOffsetsToTransaction, "SendOffsetsToTransaction")]
    public void GetDiagramLabel_Detailed_Transactions(KafkaOperation operation, string expected)
    {
        var op = new KafkaOperationInfo(operation);

        var label = KafkaOperationClassifier.GetDiagramLabel(op, KafkaTrackingVerbosity.Detailed);

        Assert.Equal(expected, label);
    }

    [Theory]
    [InlineData(KafkaOperation.InitTransactions, "Init Txn")]
    [InlineData(KafkaOperation.BeginTransaction, "Begin Txn")]
    [InlineData(KafkaOperation.CommitTransaction, "Commit Txn")]
    [InlineData(KafkaOperation.AbortTransaction, "Abort Txn")]
    [InlineData(KafkaOperation.SendOffsetsToTransaction, "Send Offsets")]
    public void GetDiagramLabel_Summarised_Transactions(KafkaOperation operation, string expected)
    {
        var op = new KafkaOperationInfo(operation);

        var label = KafkaOperationClassifier.GetDiagramLabel(op, KafkaTrackingVerbosity.Summarised);

        Assert.Equal(expected, label);
    }
}
