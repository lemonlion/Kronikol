using Confluent.Kafka;
using Kronikol.Extensions.Kafka;
using Kronikol.Tests.Tracking;
using Kronikol.Tracking;

namespace Kronikol.Tests.Kafka;

public class TrackingKafkaProducerTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private RequestResponseLog[] GetLogsFromThisTest()
    {
        return RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == _testId)
            .ToArray();
    }

    private KafkaTrackingOptions MakeOptions() => new()
    {
        ServiceName = "Kafka",
        CallerName = "TestCaller",
        Verbosity = KafkaTrackingVerbosity.Detailed,
        CurrentTestInfoFetcher = () => ("My Kafka Test", _testId),
    };

    // ─── Flush ──────────────────────────────────────────────

    [Fact]
    public void Flush_TimeSpan_Tracks_when_TrackFlush_true()
    {
        var options = MakeOptions();
        options.TrackFlush = true;
        var tracker = new KafkaTracker(options);
        var inner = new FakeProducer<string, string>();
        var producer = new TrackingKafkaProducer<string, string>(inner, tracker, options);

        producer.Flush(TimeSpan.FromSeconds(1));

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
    }

    [Fact]
    public void Flush_CancellationToken_Tracks_when_TrackFlush_true()
    {
        var options = MakeOptions();
        options.TrackFlush = true;
        var tracker = new KafkaTracker(options);
        var inner = new FakeProducer<string, string>();
        var producer = new TrackingKafkaProducer<string, string>(inner, tracker, options);

        producer.Flush(CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
    }

    [Fact]
    public void Flush_DoesNotTrack_when_TrackFlush_false()
    {
        var options = MakeOptions();
        options.TrackFlush = false;
        var tracker = new KafkaTracker(options);
        var inner = new FakeProducer<string, string>();
        var producer = new TrackingKafkaProducer<string, string>(inner, tracker, options);

        producer.Flush(TimeSpan.FromSeconds(1));

        Assert.Empty(GetLogsFromThisTest());
    }

    [Fact]
    public void Flush_Still_delegates_to_inner()
    {
        var options = MakeOptions();
        options.TrackFlush = true;
        var tracker = new KafkaTracker(options);
        var inner = new FakeProducer<string, string>();
        var producer = new TrackingKafkaProducer<string, string>(inner, tracker, options);

        producer.Flush(TimeSpan.FromSeconds(1));

        Assert.True(inner.FlushCalled);
    }

    // ─── Transactions ───────────────────────────────────────

    [Fact]
    public void InitTransactions_Tracks_when_TrackTransactions_true()
    {
        var options = MakeOptions();
        options.TrackTransactions = true;
        var tracker = new KafkaTracker(options);
        var inner = new FakeProducer<string, string>();
        var producer = new TrackingKafkaProducer<string, string>(inner, tracker, options);

        producer.InitTransactions(TimeSpan.FromSeconds(5));

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.True(inner.InitTransactionsCalled);
    }

    [Fact]
    public void BeginTransaction_Tracks_when_TrackTransactions_true()
    {
        var options = MakeOptions();
        options.TrackTransactions = true;
        var tracker = new KafkaTracker(options);
        var inner = new FakeProducer<string, string>();
        var producer = new TrackingKafkaProducer<string, string>(inner, tracker, options);

        producer.BeginTransaction();

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.True(inner.BeginTransactionCalled);
    }

    [Fact]
    public void CommitTransaction_Tracks_when_TrackTransactions_true()
    {
        var options = MakeOptions();
        options.TrackTransactions = true;
        var tracker = new KafkaTracker(options);
        var inner = new FakeProducer<string, string>();
        var producer = new TrackingKafkaProducer<string, string>(inner, tracker, options);

        producer.CommitTransaction();

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.True(inner.CommitTransactionCalled);
    }

    [Fact]
    public void CommitTransaction_WithTimeout_Tracks_when_TrackTransactions_true()
    {
        var options = MakeOptions();
        options.TrackTransactions = true;
        var tracker = new KafkaTracker(options);
        var inner = new FakeProducer<string, string>();
        var producer = new TrackingKafkaProducer<string, string>(inner, tracker, options);

        producer.CommitTransaction(TimeSpan.FromSeconds(5));

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
    }

    [Fact]
    public void AbortTransaction_Tracks_when_TrackTransactions_true()
    {
        var options = MakeOptions();
        options.TrackTransactions = true;
        var tracker = new KafkaTracker(options);
        var inner = new FakeProducer<string, string>();
        var producer = new TrackingKafkaProducer<string, string>(inner, tracker, options);

        producer.AbortTransaction();

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.True(inner.AbortTransactionCalled);
    }

    [Fact]
    public void AbortTransaction_WithTimeout_Tracks_when_TrackTransactions_true()
    {
        var options = MakeOptions();
        options.TrackTransactions = true;
        var tracker = new KafkaTracker(options);
        var inner = new FakeProducer<string, string>();
        var producer = new TrackingKafkaProducer<string, string>(inner, tracker, options);

        producer.AbortTransaction(TimeSpan.FromSeconds(5));

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
    }

    [Fact]
    public void SendOffsetsToTransaction_Tracks_when_TrackTransactions_true()
    {
        var options = MakeOptions();
        options.TrackTransactions = true;
        var tracker = new KafkaTracker(options);
        var inner = new FakeProducer<string, string>();
        var producer = new TrackingKafkaProducer<string, string>(inner, tracker, options);

        producer.SendOffsetsToTransaction(
            [new TopicPartitionOffset("orders-topic", 0, 42)],
            null!, TimeSpan.FromSeconds(5));

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.True(inner.SendOffsetsCalled);
    }

    [Fact]
    public void Transactions_DoNotTrack_when_TrackTransactions_false()
    {
        var options = MakeOptions();
        options.TrackTransactions = false;
        var tracker = new KafkaTracker(options);
        var inner = new FakeProducer<string, string>();
        var producer = new TrackingKafkaProducer<string, string>(inner, tracker, options);

        producer.InitTransactions(TimeSpan.FromSeconds(5));
        producer.BeginTransaction();
        producer.CommitTransaction();
        producer.AbortTransaction();

        Assert.Empty(GetLogsFromThisTest());
    }

    // ─── Context Propagation ────────────────────────────────

    [Fact]
    public async Task ProduceAsync_injects_test_identity_headers_when_PropagateTestIdentity_true()
    {
        var options = MakeOptions();
        options.PropagateTestIdentity = true;
        var tracker = new KafkaTracker(options);
        var inner = new FakeProducer<string, string>();
        var producer = new TrackingKafkaProducer<string, string>(inner, tracker, options);
        var message = new Message<string, string> { Key = "key1", Value = "val1" };

        await producer.ProduceAsync("topic", message, TestContext.Current.CancellationToken);

        // Confluent is handed a copy carrying the identity; the caller's message is never written to.
        Assert.Null(message.Headers);
        var sent = Assert.Single(inner.ProducedMessages).Message;
        var nameHeader = sent.Headers.GetLastBytes("kronikol-test-name");
        var idHeader = sent.Headers.GetLastBytes("kronikol-test-id");
        Assert.Equal("My Kafka Test", System.Text.Encoding.UTF8.GetString(nameHeader));
        Assert.Equal(_testId, System.Text.Encoding.UTF8.GetString(idHeader));
    }

    [Fact]
    public async Task ProduceAsync_does_not_inject_headers_when_PropagateTestIdentity_false()
    {
        var options = MakeOptions();
        options.PropagateTestIdentity = false;
        var tracker = new KafkaTracker(options);
        var inner = new FakeProducer<string, string>();
        var producer = new TrackingKafkaProducer<string, string>(inner, tracker, options);
        var message = new Message<string, string> { Key = "key1", Value = "val1" };

        await producer.ProduceAsync("topic", message, TestContext.Current.CancellationToken);

        // Nothing to add: the caller's message is handed over as it is.
        Assert.Same(message, Assert.Single(inner.ProducedMessages).Message);
        Assert.Null(message.Headers);
    }

    [Fact]
    public void Produce_sync_injects_test_identity_headers()
    {
        var options = MakeOptions();
        options.PropagateTestIdentity = true;
        var tracker = new KafkaTracker(options);
        var inner = new FakeProducer<string, string>();
        var producer = new TrackingKafkaProducer<string, string>(inner, tracker, options);
        var message = new Message<string, string> { Key = "key1", Value = "val1" };

        producer.Produce("topic", message);

        Assert.Null(message.Headers);
        var nameHeader = Assert.Single(inner.ProducedMessages).Message.Headers.GetLastBytes("kronikol-test-name");
        Assert.Equal("My Kafka Test", System.Text.Encoding.UTF8.GetString(nameHeader));
    }

    // A produce made while a host serves a request is logged under the request's scenario, and the message it sends
    // carries that scenario, so the consumer that handles it lands in the same one.
    [Fact]
    public async Task ProduceAsync_injects_the_requests_identity_when_the_options_carry_an_accessor()
    {
        var headerTestId = Guid.NewGuid().ToString();
        var options = MakeOptions();
        options.PropagateTestIdentity = true;
        options.HttpContextAccessor = RequestHeaderAccessor.For("Header Test", headerTestId);
        var tracker = new KafkaTracker(options);
        var inner = new FakeProducer<string, string>();
        var producer = new TrackingKafkaProducer<string, string>(inner, tracker, options);
        var message = new Message<string, string> { Key = "key1", Value = "val1" };

        await producer.ProduceAsync("topic", message, TestContext.Current.CancellationToken);

        var sent = Assert.Single(inner.ProducedMessages).Message;
        Assert.Equal("Header Test", System.Text.Encoding.UTF8.GetString(sent.Headers.GetLastBytes("kronikol-test-name")));
        Assert.Equal(headerTestId, System.Text.Encoding.UTF8.GetString(sent.Headers.GetLastBytes("kronikol-test-id")));
        Assert.Contains(RequestResponseLogger.RequestAndResponseLogs, l => l.TestId == headerTestId);
    }

    [Fact]
    public void Produce_injects_the_requests_identity_when_the_tracker_was_given_an_accessor()
    {
        var headerTestId = Guid.NewGuid().ToString();
        var options = MakeOptions();
        options.PropagateTestIdentity = true;
        var tracker = new KafkaTracker(options, RequestHeaderAccessor.For("Header Test", headerTestId));
        var inner = new FakeProducer<string, string>();
        var producer = new TrackingKafkaProducer<string, string>(inner, tracker, options);
        var message = new Message<string, string> { Key = "key1", Value = "val1" };

        producer.Produce("topic", message);

        var sent = Assert.Single(inner.ProducedMessages).Message;
        Assert.Equal(headerTestId, System.Text.Encoding.UTF8.GetString(sent.Headers.GetLastBytes("kronikol-test-id")));
    }

}
