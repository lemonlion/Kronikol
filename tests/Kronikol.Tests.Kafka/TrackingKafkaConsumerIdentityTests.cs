using System.Collections;
using System.Text;
using Confluent.Kafka;
using Kronikol.Extensions.Kafka;
using Kronikol.Tracking;

namespace Kronikol.Tests.Kafka;

// plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.md section 4.2: a consumed message's identity lasts until the next poll,
// whatever that poll returns (F10); a consume that throws is drawn as the message's test's (F22); a phase override shows
// content (F13); and the paths F17 found untested. Reads the correlation store and the identity scope, which other
// classes here clear (ProcessWideTrackingStateTests).
[Collection("TestCorrelationStore")]
public class TrackingKafkaConsumerIdentityTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    // Outside any test: the consumer's own fetcher answers nothing, so the message's identity is what attributes it.
    private static KafkaTrackingOptions OutsideATest() => new()
    {
        ServiceName = "Kafka",
        CallerName = "Payments",
        Verbosity = KafkaTrackingVerbosity.Raw,
        CurrentTestInfoFetcher = () => throw new InvalidOperationException("No test is running"),
    };

    private KafkaTrackingOptions InThisTest() => new()
    {
        ServiceName = "Kafka",
        CallerName = "Payments",
        Verbosity = KafkaTrackingVerbosity.Detailed,
        CurrentTestInfoFetcher = () => ("Consumer Test", _testId),
    };

    private static Headers IdentityOf(string testId) => new()
    {
        { "kronikol-test-name", Encoding.UTF8.GetBytes($"Test {testId}") },
        { "kronikol-test-id", Encoding.UTF8.GetBytes(testId) },
    };

    private static ConsumeResult<string, string> MessageOf(string testId, string key = "k") => new()
    {
        TopicPartitionOffset = new TopicPartitionOffset("orders", new Partition(0), new Offset(1)),
        Message = new Message<string, string> { Key = key, Value = "v", Headers = IdentityOf(testId) },
    };

    private static RequestResponseLog[] LogsOf(string testId) =>
        RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId).ToArray();

    // ─── F10 ─────────

    [Fact]
    public void An_empty_poll_ends_the_previous_messages_identity()
    {
        var sender = Guid.NewGuid().ToString();
        var options = OutsideATest();
        var consumer = new TrackingKafkaConsumer<string, string>(
            new ScriptedConsumer<string, string>().Returns(MessageOf(sender)).Returns(null),
            new KafkaTracker(options), options);
        try
        {
            consumer.Consume(TimeSpan.Zero);
            Assert.Equal(sender, TestIdentityScope.Current?.Id);

            consumer.Consume(TimeSpan.Zero);

            Assert.Null(TestIdentityScope.Current);
        }
        finally
        {
            TestIdentityScope.ClearMessageIdentity();
        }
    }

    [Fact]
    public void A_partition_EOF_ends_the_previous_messages_identity()
    {
        var sender = Guid.NewGuid().ToString();
        var options = OutsideATest();
        var consumer = new TrackingKafkaConsumer<string, string>(
            new ScriptedConsumer<string, string>()
                .Returns(MessageOf(sender))
                .Returns(new ConsumeResult<string, string>
                {
                    TopicPartitionOffset = new TopicPartitionOffset("orders", new Partition(0), new Offset(2)),
                    IsPartitionEOF = true,
                }),
            new KafkaTracker(options), options);
        try
        {
            consumer.Consume(TimeSpan.Zero);
            consumer.Consume(TimeSpan.Zero);

            Assert.Null(TestIdentityScope.Current);
            Assert.Equal(2, LogsOf(sender).Length);
        }
        finally
        {
            TestIdentityScope.ClearMessageIdentity();
        }
    }

    // ─── F22 ─────────

    [Fact]
    public void A_consume_that_throws_is_drawn_as_a_failed_consume_of_the_record_under_the_test_its_headers_name()
    {
        var sender = Guid.NewGuid().ToString();
        var options = OutsideATest();
        var record = new ConsumeResult<byte[], byte[]>
        {
            TopicPartitionOffset = new TopicPartitionOffset("orders", new Partition(3), new Offset(42)),
            Message = new Message<byte[], byte[]> { Key = [1], Value = [2], Headers = IdentityOf(sender) },
        };
        var consumer = new TrackingKafkaConsumer<string, string>(
            new ScriptedConsumer<string, string>().Throws(new ConsumeException(record, new Error(ErrorCode.Local_ValueDeserialization, "Local: Value deserialization error"))),
            new KafkaTracker(options), options);
        try
        {
            Assert.Throws<ConsumeException>(() => consumer.Consume(TimeSpan.Zero));

            var entries = LogsOf(sender);
            Assert.Equal(2, entries.Length);
            Assert.Equal("Consume orders[3]@42", entries[0].Method.Value!.ToString());
            var response = Assert.Single(entries, l => l.Type == RequestResponseType.Response);
            Assert.Equal("!Local_ValueDeserialization", response.StatusCode?.Value?.ToString());
            Assert.Equal("Local: Value deserialization error", response.Error);
        }
        finally
        {
            TestIdentityScope.ClearMessageIdentity();
        }
    }

    // ─── F13 ─────────

    [Fact]
    public async Task An_action_phase_override_shows_content_though_the_base_verbosity_is_summarised()
    {
        var options = InThisTest();
        options.Verbosity = KafkaTrackingVerbosity.Summarised;
        options.ActionVerbosity = KafkaTrackingVerbosity.Detailed;
        var tracker = new KafkaTracker(options);
        var producer = new TrackingKafkaProducer<string, string>(new FakeProducer<string, string>(), tracker, options);
        var consumer = new TrackingKafkaConsumer<string, string>(
            new ScriptedConsumer<string, string>().Returns(new ConsumeResult<string, string>
            {
                TopicPartitionOffset = new TopicPartitionOffset("payments", new Partition(0), new Offset(1)),
                Message = new Message<string, string> { Key = "p", Value = "paid" },
            }),
            tracker, options);
        TestPhaseContext.Current = TestPhase.Action;

        await producer.ProduceAsync("orders", new Message<string, string> { Key = "k", Value = "v" }, TestContext.Current.CancellationToken);
        consumer.Consume(TimeSpan.Zero);

        var requests = LogsOf(_testId).Where(l => l.Type == RequestResponseType.Request).ToArray();
        Assert.Equal(["Key: k, Value: v", "Key: p, Value: paid"], requests.Select(r => r.Content));
    }

    // ─── F17 ─────────

    [Fact]
    public void Consume_by_milliseconds_and_by_token_are_logged_and_empty_and_EOF_polls_are_not()
    {
        var options = InThisTest();
        var inner = new ScriptedConsumer<string, string>()
            .Returns(new ConsumeResult<string, string> { TopicPartitionOffset = new("orders", new Partition(0), new Offset(1)), Message = new Message<string, string> { Key = "a", Value = "1" } })
            .Returns(null)
            .Returns(new ConsumeResult<string, string> { TopicPartitionOffset = new("orders", new Partition(0), new Offset(2)), IsPartitionEOF = true })
            .Returns(new ConsumeResult<string, string> { TopicPartitionOffset = new("orders", new Partition(0), new Offset(2)), Message = new Message<string, string> { Key = "b", Value = "2" } });
        var consumer = new TrackingKafkaConsumer<string, string>(inner, new KafkaTracker(options), options);

        consumer.Consume(1);
        consumer.Consume(TestContext.Current.CancellationToken);
        consumer.Consume(1);
        consumer.Consume(TestContext.Current.CancellationToken);

        var requests = LogsOf(_testId).Where(l => l.Type == RequestResponseType.Request).ToArray();
        Assert.Equal(["Key: a, Value: 1", "Key: b, Value: 2"], requests.Select(r => r.Content));
    }

    [Fact]
    public void Subscribing_to_a_sequence_that_can_be_read_once_subscribes_and_logs_every_topic()
    {
        var options = InThisTest();
        options.TrackSubscribe = true;
        var inner = new ScriptedConsumer<string, string>();
        var consumer = new TrackingKafkaConsumer<string, string>(inner, new KafkaTracker(options), options);

        consumer.Subscribe(new ReadOnce(["orders", "payments"]));

        Assert.Equal(["orders", "payments"], inner.Subscribed);
        Assert.Equal(["Subscribe orders", "Subscribe payments"],
            LogsOf(_testId).Where(l => l.Type == RequestResponseType.Request).Select(l => l.Method.Value!.ToString()));
    }

    [Fact]
    public void A_consumed_message_is_correlated_under_its_key_and_under_the_extractors_key()
    {
        var sender = Guid.NewGuid().ToString();
        var key = Guid.NewGuid().ToString();
        var options = OutsideATest();
        var extracted = OutsideATest();
        extracted.ConsumeKeyExtractor = (service, messageKey) => $"custom:{service}:{messageKey}";
        try
        {
            new TrackingKafkaConsumer<string, string>(new ScriptedConsumer<string, string>().Returns(MessageOf(sender, key)), new KafkaTracker(options), options)
                .Consume(TimeSpan.Zero);
            new TrackingKafkaConsumer<string, string>(new ScriptedConsumer<string, string>().Returns(MessageOf(sender, key)), new KafkaTracker(extracted), extracted)
                .Consume(TimeSpan.Zero);

            Assert.Equal(sender, TestCorrelationStore.Lookup(CorrelationKeys.Kafka("Kafka", key))?.Id);
            Assert.Equal(sender, TestCorrelationStore.Lookup($"custom:Kafka:{key}")?.Id);
        }
        finally
        {
            TestIdentityScope.ClearMessageIdentity();
            TestCorrelationStore.Remove(CorrelationKeys.Kafka("Kafka", key));
            TestCorrelationStore.Remove($"custom:Kafka:{key}");
        }
    }

    [Fact]
    public async Task TrackDuringSetup_false_leaves_a_setup_produce_out_and_an_action_produce_in()
    {
        var options = InThisTest();
        options.TrackDuringSetup = false;
        var producer = new TrackingKafkaProducer<string, string>(new FakeProducer<string, string>(), new KafkaTracker(options), options);

        TestPhaseContext.Current = TestPhase.Setup;
        await producer.ProduceAsync("orders", new Message<string, string> { Key = "setup", Value = "1" }, TestContext.Current.CancellationToken);
        TestPhaseContext.Current = TestPhase.Action;
        await producer.ProduceAsync("orders", new Message<string, string> { Key = "action", Value = "2" }, TestContext.Current.CancellationToken);

        var request = Assert.Single(LogsOf(_testId), l => l.Type == RequestResponseType.Request);
        Assert.Equal("Key: action, Value: 2", request.Content);
    }

    [Fact]
    public async Task ProduceAsync_to_a_partition_is_logged_with_the_partition_the_broker_wrote_to()
    {
        var options = InThisTest();
        options.Verbosity = KafkaTrackingVerbosity.Raw;
        var producer = new TrackingKafkaProducer<string, string>(new FakeProducer<string, string>(), new KafkaTracker(options), options);

        await producer.ProduceAsync(new TopicPartition("orders", new Partition(4)), new Message<string, string> { Key = "k", Value = "v" }, TestContext.Current.CancellationToken);

        Assert.Equal("ProduceAsync orders[4]@0", LogsOf(_testId)[0].Method.Value!.ToString());
    }

    // A sequence that throws when it is read a second time, as a forward-only source does.
    private sealed class ReadOnce(IEnumerable<string> items) : IEnumerable<string>
    {
        private bool _read;

        public IEnumerator<string> GetEnumerator()
        {
            if (_read) throw new InvalidOperationException("This sequence can be read once.");
            _read = true;
            return items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
