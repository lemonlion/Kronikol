using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using Kronikol.Extensions.Kafka;
using Kronikol.Tracking;

namespace Kronikol.Tests.Kafka;

// plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.md Q17, #160: a library that wraps Kafka behind its own interfaces decorates
// them with KafkaTracker.TrackProduce and TrackConsume, and gets what the wrappers do: the produce is its caller's, a
// failure is drawn, the caller's message is not written to, the ids are recorded, and a consumed message's identity is
// current while it is handled (T61).
public class KafkaHandWrittenDecoratorTests
{
    private static readonly AsyncLocal<string?> RunningTest = new();

    private static KafkaTrackingOptions MakeOptions() => new()
    {
        ServiceName = "Kafka",
        CallerName = "Orders",
        AutoCorrelateOnConsume = false,
        CurrentTestInfoFetcher = () => RunningTest.Value is { } id ? ($"Test {id}", id) : throw new InvalidOperationException("No test is running"),
    };

    private static RequestResponseLog[] LogsOf(params string[] testIds) =>
        RequestResponseLogger.RequestAndResponseLogs.Where(l => testIds.Contains(l.TestId)).ToArray();

    private static string? Header(Headers? headers, string key) =>
        headers is not null && headers.TryGetLastBytes(key, out var bytes) ? Encoding.UTF8.GetString(bytes) : null;

    // What a library's own publisher looks like, decorated as #160 does it.
    private sealed class OrdersPublisher(IProducer<string, string> inner, KafkaTracker tracker)
    {
        public async Task PublishAsync(string topic, Message<string, string> message)
        {
            var produce = tracker.TrackProduce(topic, message, KafkaOperation.ProduceAsync);
            try
            {
                produce.Delivered(await inner.ProduceAsync(topic, produce.Message));
            }
            catch (Exception exception)
            {
                produce.Failed(exception);
                throw;
            }
        }

        public void Publish(string topic, Message<string, string> message, Action<DeliveryReport<string, string>>? handler = null)
        {
            var produce = tracker.TrackProduce(topic, message);
            inner.Produce(topic, produce.Message, produce.Handler(handler));
        }
    }

    private static DeliveryReport<string, string> Report(Message<string, string> message, ErrorCode code = ErrorCode.NoError) => new()
    {
        TopicPartitionOffset = new TopicPartitionOffset("orders", new Partition(1), new Offset(5)),
        Error = new Error(code),
        Message = message,
    };

    [Fact]
    public void A_produce_is_its_callers_though_its_delivery_handler_runs_under_the_builders_context()
    {
        var builder = Guid.NewGuid().ToString();
        var caller = Guid.NewGuid().ToString();
        var inner = new FakeProducer<string, string>();
        var publisher = new OrdersPublisher(inner, new KafkaTracker(MakeOptions()));
        RunningTest.Value = builder;
        var pollThreadContext = ExecutionContext.Capture()!;
        RunningTest.Value = caller;
        var handled = false;

        publisher.Publish("orders", new Message<string, string> { Key = "k", Value = "v" }, _ => handled = true);
        inner.Deliver(0, Report(inner.ProducedMessages[0].Message), pollThreadContext);

        var entries = LogsOf(builder, caller);
        Assert.Equal(2, entries.Length);
        Assert.All(entries, e => Assert.Equal(caller, e.TestId));
        Assert.True(handled);
    }

    [Fact]
    public void A_failed_delivery_is_drawn_with_its_error_code()
    {
        var test = Guid.NewGuid().ToString();
        RunningTest.Value = test;
        var inner = new FakeProducer<string, string>();
        new OrdersPublisher(inner, new KafkaTracker(MakeOptions())).Publish("orders", new Message<string, string> { Key = "k", Value = "v" });

        inner.Deliver(0, Report(inner.ProducedMessages[0].Message, ErrorCode.Local_MsgTimedOut), ExecutionContext.Capture()!);

        var response = Assert.Single(LogsOf(test), l => l.Type == RequestResponseType.Response);
        Assert.Equal("!Local_MsgTimedOut", response.StatusCode!.Value.ToString());
    }

    [Fact]
    public async Task A_ProduceAsync_that_throws_is_drawn_and_rethrown()
    {
        var test = Guid.NewGuid().ToString();
        RunningTest.Value = test;
        var inner = new FakeProducer<string, string> { ThrowOnNextProduce = new KafkaException(ErrorCode.Local_QueueFull) };

        await Assert.ThrowsAsync<KafkaException>(() =>
            new OrdersPublisher(inner, new KafkaTracker(MakeOptions())).PublishAsync("orders", new Message<string, string> { Key = "k", Value = "v" }));

        var response = Assert.Single(LogsOf(test), l => l.Type == RequestResponseType.Response);
        Assert.Equal("!Local_QueueFull", response.StatusCode!.Value.ToString());
    }

    // A library's catch can report a produce its try already reported, when code after the delivery throws: the produce
    // is drawn once, as first reported (T61).
    [Fact]
    public void A_produce_reported_again_is_drawn_once_as_first_reported()
    {
        var test = Guid.NewGuid().ToString();
        RunningTest.Value = test;
        var produce = new KafkaTracker(MakeOptions()).TrackProduce("orders", new Message<string, string> { Key = "k", Value = "v" });

        produce.Delivered(new DeliveryResult<string, string> { Topic = "orders", Partition = new Partition(1), Offset = new Offset(5), Message = produce.Message });
        produce.Failed(new KafkaException(ErrorCode.Local_QueueFull));
        produce.Delivered(Report(produce.Message, ErrorCode.Local_MsgTimedOut));
        produce.Sent();

        var entries = LogsOf(test);
        Assert.Single(entries, l => l.Type == RequestResponseType.Request);
        var response = Assert.Single(entries, l => l.Type == RequestResponseType.Response);
        Assert.Null(response.StatusCode);
    }

    [Fact]
    public async Task The_callers_message_is_not_written_to_and_the_message_sent_carries_the_identity()
    {
        var test = Guid.NewGuid().ToString();
        RunningTest.Value = test;
        var inner = new FakeProducer<string, string>();
        var message = new Message<string, string> { Key = "k", Value = "v" };

        await new OrdersPublisher(inner, new KafkaTracker(MakeOptions())).PublishAsync("orders", message);

        Assert.Null(message.Headers);
        var sent = Assert.Single(inner.ProducedMessages).Message;
        Assert.NotSame(message, sent);
        Assert.Equal(test, Header(sent.Headers, "kronikol-test-id"));
    }

    [Fact]
    public async Task Under_an_ambient_span_the_produce_records_its_ids()
    {
        var test = Guid.NewGuid().ToString();
        RunningTest.Value = test;
        var inner = new FakeProducer<string, string>();
        using var span = new Activity("publish order").SetIdFormat(ActivityIdFormat.W3C).Start();

        await new OrdersPublisher(inner, new KafkaTracker(MakeOptions())).PublishAsync("orders", new Message<string, string> { Key = "k", Value = "v" });

        Assert.All(LogsOf(test), e => Assert.Equal((span.TraceId.ToHexString(), span.SpanId.ToHexString()), (e.ActivityTraceId, e.ActivitySpanId)));
    }

    [Fact]
    public void A_consumed_messages_identity_is_current_inside_the_scope_and_cleared_after()
    {
        var test = Guid.NewGuid().ToString();
        RunningTest.Value = null;
        var tracker = new KafkaTracker(MakeOptions());
        var result = new ConsumeResult<string, string>
        {
            TopicPartitionOffset = new TopicPartitionOffset("orders", new Partition(0), new Offset(9)),
            Message = new Message<string, string>
            {
                Key = "k", Value = "v",
                Headers = new Headers
                {
                    { "kronikol-test-name", Encoding.UTF8.GetBytes("Place an order") },
                    { "kronikol-test-id", Encoding.UTF8.GetBytes(test) },
                },
            },
        };

        (string Name, string Id)? inside;
        using (tracker.TrackConsume(result))
            inside = TestIdentityScope.Current;

        Assert.Equal(test, inside?.Id);
        Assert.Null(TestIdentityScope.Current);
        var consume = Assert.Single(LogsOf(test), l => l.Type == RequestResponseType.Request);
        Assert.Equal("v", consume.Content);
        Assert.Contains(("kafka-key", "k"), consume.Headers);
    }
}
