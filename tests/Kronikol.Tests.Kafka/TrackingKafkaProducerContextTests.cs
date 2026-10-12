using System.Text;
using Confluent.Kafka;
using Kronikol.Extensions.Kafka;
using Kronikol.Tracking;

namespace Kronikol.Tests.Kafka;

// plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.md section 4.2. Confluent runs a delivery handler on the producer's poll
// thread, under the context of whoever built the producer, so a produce is captured at the call: its test, its phase
// and its verbosity, and its request time (F4, F8). The caller's message is never written to (F9).
public class TrackingKafkaProducerContextTests
{
    // The running test, as a framework's fetcher reads it: from the flow, so a thread under another flow's context
    // reads that flow's test.
    private static readonly AsyncLocal<string?> RunningTest = new();

    private static KafkaTrackingOptions MakeOptions() => new()
    {
        ServiceName = "Kafka",
        CallerName = "Orders",
        Verbosity = KafkaTrackingVerbosity.Detailed,
        CurrentTestInfoFetcher = () => RunningTest.Value is { } id ? ($"Test {id}", id) : throw new InvalidOperationException("No test is running"),
    };

    private static RequestResponseLog[] LogsOf(params string[] testIds) =>
        RequestResponseLogger.RequestAndResponseLogs.Where(l => testIds.Contains(l.TestId)).ToArray();

    private static DeliveryReport<TKey, TValue> Delivered<TKey, TValue>(Message<TKey, TValue> message, int partition = 1, long offset = 5) => new()
    {
        TopicPartitionOffset = new TopicPartitionOffset("orders", new Partition(partition), new Offset(offset)),
        Error = new Error(ErrorCode.NoError),
        Status = PersistenceStatus.Persisted,
        Message = message,
    };

    private static string? Header(Message<string, string> message, string key) =>
        message.Headers is { } headers && headers.TryGetLastBytes(key, out var bytes) ? Encoding.UTF8.GetString(bytes) : null;

    [Fact]
    public void A_sync_produce_is_its_callers_though_its_delivery_handler_runs_under_the_builders_context()
    {
        var builder = Guid.NewGuid().ToString();
        var caller = Guid.NewGuid().ToString();
        var options = MakeOptions();
        var inner = new FakeProducer<string, string>();
        var producer = new TrackingKafkaProducer<string, string>(inner, new KafkaTracker(options), options);
        RunningTest.Value = builder;
        var pollThreadContext = ExecutionContext.Capture()!;
        RunningTest.Value = caller;
        DeliveryReport<string, string>? seen = null;

        producer.Produce("orders", new Message<string, string> { Key = "k", Value = "v" }, report => seen = report);
        inner.Deliver(0, Delivered(inner.ProducedMessages[0].Message), pollThreadContext);

        var entries = LogsOf(builder, caller);
        Assert.Equal(2, entries.Length);
        Assert.All(entries, e => Assert.Equal(caller, e.TestId));
        Assert.Equal(caller, Header(inner.ProducedMessages[0].Message, "kronikol-test-id"));
        Assert.NotNull(seen);
    }

    [Fact]
    public void A_produce_called_in_setup_and_delivered_once_the_action_began_is_a_setup_entry_at_setup_verbosity()
    {
        var test = Guid.NewGuid().ToString();
        RunningTest.Value = test;
        var options = MakeOptions();
        options.SetupVerbosity = KafkaTrackingVerbosity.Raw;
        options.ActionVerbosity = KafkaTrackingVerbosity.Summarised;
        var inner = new FakeProducer<string, string>();
        var producer = new TrackingKafkaProducer<string, string>(inner, new KafkaTracker(options), options);
        TestPhaseContext.Current = TestPhase.Action;
        var pollThreadContext = ExecutionContext.Capture()!;
        TestPhaseContext.Current = TestPhase.Setup;

        producer.Produce("orders", new Message<string, string> { Key = "k", Value = "v" });
        inner.Deliver(0, Delivered(inner.ProducedMessages[0].Message, partition: 2, offset: 9), pollThreadContext);

        var request = Assert.Single(LogsOf(test), l => l.Type == RequestResponseType.Request);
        Assert.Equal(TestPhase.Setup, request.Phase);
        Assert.Equal("Produce orders[2]@9", request.Method.Value!.ToString());
        Assert.Equal("Key: k, Value: v", request.Content);
    }

    [Fact]
    public void A_produce_is_stamped_at_the_call_and_at_delivery_and_stored_at_delivery()
    {
        var test = Guid.NewGuid().ToString();
        RunningTest.Value = test;
        var options = MakeOptions();
        var inner = new FakeProducer<string, string>();
        var producer = new TrackingKafkaProducer<string, string>(inner, new KafkaTracker(options), options);

        producer.Produce("orders", new Message<string, string> { Key = "k", Value = "v" });
        Assert.Empty(LogsOf(test));
        Thread.Sleep(60);
        inner.Deliver(0, Delivered(inner.ProducedMessages[0].Message), ExecutionContext.Capture()!);

        var entries = LogsOf(test);
        var request = Assert.Single(entries, l => l.Type == RequestResponseType.Request);
        var response = Assert.Single(entries, l => l.Type == RequestResponseType.Response);
        Assert.True(response.Timestamp - request.Timestamp >= TimeSpan.FromMilliseconds(50),
            $"request at {request.Timestamp:O}, response at {response.Timestamp:O}");
    }

    [Fact]
    public async Task ProduceAsync_is_stamped_at_the_call_and_when_the_broker_answered()
    {
        var test = Guid.NewGuid().ToString();
        RunningTest.Value = test;
        var options = MakeOptions();
        var inner = new FakeProducer<string, string>
        {
            ProduceAsyncResult = async (topic, message, ct) =>
            {
                await Task.Delay(60, ct);
                return new DeliveryResult<string, string> { Topic = topic, Partition = new Partition(0), Offset = new Offset(1), Message = message };
            },
        };
        var producer = new TrackingKafkaProducer<string, string>(inner, new KafkaTracker(options), options);

        await producer.ProduceAsync("orders", new Message<string, string> { Key = "k", Value = "v" }, TestContext.Current.CancellationToken);

        var entries = LogsOf(test);
        var request = Assert.Single(entries, l => l.Type == RequestResponseType.Request);
        var response = Assert.Single(entries, l => l.Type == RequestResponseType.Response);
        Assert.True(response.Timestamp - request.Timestamp >= TimeSpan.FromMilliseconds(50),
            $"request at {request.Timestamp:O}, response at {response.Timestamp:O}");
    }

    [Fact]
    public void A_consume_carries_one_instant_on_both_halves_so_no_duration_is_invented()
    {
        var test = Guid.NewGuid().ToString();
        RunningTest.Value = test;
        var options = MakeOptions();
        var inner = new ScriptedConsumer<string, string>().Returns(new ConsumeResult<string, string>
        {
            TopicPartitionOffset = new TopicPartitionOffset("orders", new Partition(0), new Offset(3)),
            Message = new Message<string, string> { Key = "k", Value = "v" },
        });
        var consumer = new TrackingKafkaConsumer<string, string>(inner, new KafkaTracker(options), options);

        consumer.Consume(TimeSpan.Zero);

        var entries = LogsOf(test);
        Assert.Equal(2, entries.Length);
        Assert.Equal(entries[0].Timestamp, entries[1].Timestamp);
    }

    [Fact]
    public async Task The_callers_message_is_never_written_and_each_produce_hands_Confluent_its_own_tests_identity()
    {
        var first = Guid.NewGuid().ToString();
        var second = Guid.NewGuid().ToString();
        var options = MakeOptions();
        var inner = new FakeProducer<string, string>();
        var producer = new TrackingKafkaProducer<string, string>(inner, new KafkaTracker(options), options);
        var reused = new Message<string, string> { Key = "k", Value = "v" };

        RunningTest.Value = first;
        await producer.ProduceAsync("orders", reused, TestContext.Current.CancellationToken);
        RunningTest.Value = second;
        producer.Produce("orders", reused);

        Assert.Null(reused.Headers);
        var sent = inner.ProducedMessages.Select(p => p.Message).ToArray();
        Assert.All(sent, m => Assert.NotSame(reused, m));
        Assert.Equal(first, Header(sent[0], "kronikol-test-id"));
        Assert.Equal(second, Header(sent[1], "kronikol-test-id"));
        Assert.All(sent, m => Assert.Equal(["kronikol-test-name", "kronikol-test-id"], m.Headers.Select(h => h.Key)));
        Assert.All(sent, m => Assert.Equal(("k", "v"), (m.Key, m.Value)));
    }

    [Fact]
    public void An_identity_header_the_caller_set_is_kept_and_only_the_missing_one_is_added()
    {
        var test = Guid.NewGuid().ToString();
        RunningTest.Value = test;
        var options = MakeOptions();
        var inner = new FakeProducer<string, string>();
        var producer = new TrackingKafkaProducer<string, string>(inner, new KafkaTracker(options), options);
        var timestamp = new Timestamp(new DateTime(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc));
        var message = new Message<string, string>
        {
            Key = "k",
            Value = "v",
            Timestamp = timestamp,
            Headers = new Headers { { "traceparent", "00-x"u8.ToArray() }, { "kronikol-test-id", "set-by-caller"u8.ToArray() } },
        };

        producer.Produce("orders", message);

        Assert.Equal(["traceparent", "kronikol-test-id"], message.Headers.Select(h => h.Key));
        var sent = Assert.Single(inner.ProducedMessages).Message;
        Assert.Equal(["traceparent", "kronikol-test-id", "kronikol-test-name"], sent.Headers.Select(h => h.Key));
        Assert.Equal("set-by-caller", Header(sent, "kronikol-test-id"));
        Assert.Equal($"Test {test}", Header(sent, "kronikol-test-name"));
        Assert.Equal(timestamp, sent.Timestamp);
    }

    [Fact]
    public void A_consume_recorded_between_a_produces_call_and_its_delivery_is_stored_before_it()
    {
        var test = Guid.NewGuid().ToString();
        RunningTest.Value = test;
        var options = MakeOptions();
        var inner = new FakeProducer<string, string>();
        var producer = new TrackingKafkaProducer<string, string>(inner, new KafkaTracker(options), options);
        var consumer = new TrackingKafkaConsumer<string, string>(
            new ScriptedConsumer<string, string>().Returns(new ConsumeResult<string, string>
            {
                TopicPartitionOffset = new TopicPartitionOffset("payments", new Partition(0), new Offset(1)),
                Message = new Message<string, string> { Key = "p", Value = "paid" },
            }),
            new KafkaTracker(options), options);

        producer.Produce("orders", new Message<string, string> { Key = "k", Value = "v" });
        Thread.Sleep(20);
        consumer.Consume(TimeSpan.Zero);
        Thread.Sleep(20);
        inner.Deliver(0, Delivered(inner.ProducedMessages[0].Message), ExecutionContext.Capture()!);

        // Both halves of the produce are stored at delivery, after the consume: `flow` reads a call as open from its
        // request to its response in store order, so storing the request at the call would nest the consume under it.
        var entries = LogsOf(test);
        Assert.Equal(["Consume ← payments", "Consume ← payments", "Produce → orders", "Produce → orders"],
            entries.Select(e => e.Method.Value!.ToString()));
        Assert.True(entries[2].Timestamp < entries[0].Timestamp, "the produce's request is stamped at its call, before the consume");
    }
}
