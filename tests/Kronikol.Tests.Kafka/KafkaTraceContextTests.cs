using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using Kronikol.Constants;
using Kronikol.Extensions.Kafka;
using Kronikol.InternalFlow;
using Kronikol.Tests.Tracking;
using Kronikol.Tracking;

namespace Kronikol.Tests.Kafka;

// plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.md section 4.3, R1 (ask 2 of #135): Kafka entries record W3C trace ids, a
// produced message carries a traceparent where Kronikol's wrapper is the last before Confluent, and the Kronikol trace
// id of the request a message was produced while serving (Q19).
public class KafkaTraceContextTests
{
    private const string ValidTraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

    // The running test, read from the flow as a framework's fetcher reads it.
    private static readonly AsyncLocal<string?> RunningTest = new();

    private static KafkaTrackingOptions MakeOptions() => new()
    {
        ServiceName = "Kafka",
        CallerName = "Orders",
        Verbosity = KafkaTrackingVerbosity.Detailed,
        AutoCorrelateOnConsume = false,
        CurrentTestInfoFetcher = () => RunningTest.Value is { } id ? ($"Test {id}", id) : throw new InvalidOperationException("No test is running"),
    };

    private static RequestResponseLog[] LogsOf(string testId) =>
        RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId).ToArray();

    // A consume is incoming, so its entries name the caller as the service.
    private static RequestResponseLog[] ProducesOf(string testId) => LogsOf(testId).Where(l => l.ServiceName == "Kafka").ToArray();

    private static RequestResponseLog[] ConsumesOf(string testId) => LogsOf(testId).Where(l => l.ServiceName == "Orders").ToArray();

    private static string? Header(Headers? headers, string key) =>
        headers is not null && headers.TryGetLastBytes(key, out var bytes) ? Encoding.UTF8.GetString(bytes) : null;

    private static string[] All(Headers? headers, string key) =>
        headers?.Where(h => h.Key == key).Select(h => Encoding.UTF8.GetString(h.GetValueBytes())).ToArray() ?? [];

    // Confluent's own producer with no broker: a produce is queued and times out after a second.
    private static IProducer<string, string> ConfluentProducer() =>
        new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = "localhost:9092", MessageTimeoutMs = 1000 }).Build();

    // What Confluent was handed, read back from the timeout's delivery result.
    private static async Task<Message<string, string>> SentThroughConfluent(KafkaTrackingOptions options, Message<string, string> message)
    {
        using var confluent = ConfluentProducer();
        var producer = new TrackingKafkaProducer<string, string>(confluent, new KafkaTracker(options), options);
        var failure = await Assert.ThrowsAsync<ProduceException<string, string>>(() => producer.ProduceAsync("orders", message, TestContext.Current.CancellationToken));
        return failure.DeliveryResult.Message;
    }

    private static void AssertIds(RequestResponseLog[] entries, string? traceId, string? spanId)
    {
        Assert.Equal(2, entries.Length);
        Assert.All(entries, e =>
        {
            Assert.Equal(traceId, e.ActivityTraceId);
            Assert.Equal(spanId, e.ActivitySpanId);
        });
    }

    // ─── Produce ───────────────────────────────────────────────────────────────

    [Fact]
    public void Under_an_ambient_span_a_sync_produce_records_the_ids_taken_at_the_call()
    {
        var test = Guid.NewGuid().ToString();
        RunningTest.Value = test;
        var options = MakeOptions();
        var inner = new FakeProducer<string, string>();
        var producer = new TrackingKafkaProducer<string, string>(inner, new KafkaTracker(options), options);
        var pollThreadContext = ExecutionContext.Capture()!;
        using var span = new Activity("orders publish").SetIdFormat(ActivityIdFormat.W3C).Start();

        producer.Produce("orders", new Message<string, string> { Key = "k", Value = "v" }, _ => { });
        span.Stop();
        inner.Deliver(0, new DeliveryReport<string, string>
        {
            TopicPartitionOffset = new TopicPartitionOffset("orders", new Partition(0), new Offset(1)),
            Error = new Error(ErrorCode.NoError),
            Message = inner.ProducedMessages[0].Message,
        }, pollThreadContext);

        AssertIds(LogsOf(test), span.TraceId.ToHexString(), span.SpanId.ToHexString());
    }

    [Fact]
    public async Task Confluents_own_producer_is_handed_a_traceparent_the_entry_records_and_the_callers_message_keeps_none()
    {
        var test = Guid.NewGuid().ToString();
        RunningTest.Value = test;
        var message = new Message<string, string> { Key = "k", Value = "v" };
        Assert.Null(Activity.Current);

        var sent = await SentThroughConfluent(MakeOptions(), message);

        var traceParent = Assert.Single(All(sent.Headers, "traceparent"));
        Assert.Matches("^00-[0-9a-f]{32}-[0-9a-f]{16}-00$", traceParent);
        AssertIds(LogsOf(test), traceParent[3..35], traceParent[36..52]);
        Assert.Null(message.Headers);
    }

    [Fact]
    public async Task With_PropagateTraceContext_off_no_traceparent_is_added()
    {
        RunningTest.Value = Guid.NewGuid().ToString();
        var options = MakeOptions();
        options.PropagateTraceContext = false;

        var sent = await SentThroughConfluent(options, new Message<string, string> { Key = "k", Value = "v" });

        Assert.Empty(All(sent.Headers, "traceparent"));
    }

    [Fact]
    public async Task A_traceparent_says_sampled_when_the_apps_tracing_records_the_span_and_not_when_only_Kronikol_listens()
    {
        using var recorded = new ActivitySource("Kronikol.Tests.Kafka.Recorded");
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source == recorded,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        InternalFlowActivityListener.EnsureStarted();
        using var heardByKronikolAlone = new ActivitySource("Kronikol.Tests.Kafka.NotRecorded");

        RunningTest.Value = Guid.NewGuid().ToString();
        Message<string, string> sampled, unsampled;
        using (recorded.StartActivity("publish"))
            sampled = await SentThroughConfluent(MakeOptions(), new Message<string, string> { Key = "k", Value = "v" });
        using (heardByKronikolAlone.StartActivity("publish"))
            unsampled = await SentThroughConfluent(MakeOptions(), new Message<string, string> { Key = "k", Value = "v" });

        Assert.EndsWith("-01", Header(sampled.Headers, "traceparent"));
        Assert.EndsWith("-00", Header(unsampled.Headers, "traceparent"));
    }

    [Fact]
    public async Task A_message_that_carries_a_traceparent_is_sent_with_it_alone_and_the_entry_records_it()
    {
        var test = Guid.NewGuid().ToString();
        RunningTest.Value = test;
        var message = new Message<string, string> { Key = "k", Value = "v", Headers = new Headers { { "traceparent", Encoding.UTF8.GetBytes(ValidTraceParent) } } };

        var sent = await SentThroughConfluent(MakeOptions(), message);

        Assert.Equal([ValidTraceParent], All(sent.Headers, "traceparent"));
        AssertIds(LogsOf(test), "0af7651916cd43dd8448eb211c80319c", "b7ad6b7169203331");
    }

    // An instrumentation package's wrapper inside Kronikol's appends a traceparent naming its own send span: Kronikol
    // adds none, so a consumer that parents on the first traceparent parents on the wrapper's (4.3, T54).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_wrapper_inside_that_appends_a_traceparent_is_the_one_sent_and_recorded(bool callerSetOne)
    {
        const string wrappers = "00-11111111111111111111111111111111-2222222222222222-01";
        var test = Guid.NewGuid().ToString();
        RunningTest.Value = test;
        var options = MakeOptions();
        var inner = new FakeProducer<string, string> { OnProduce = m => (m.Headers ??= []).Add("traceparent", Encoding.UTF8.GetBytes(wrappers)) };
        var producer = new TrackingKafkaProducer<string, string>(inner, new KafkaTracker(options), options);
        var message = new Message<string, string> { Key = "k", Value = "v" };
        if (callerSetOne)
            message.Headers = new Headers { { "traceparent", Encoding.UTF8.GetBytes(ValidTraceParent) } };

        await producer.ProduceAsync("orders", message, TestContext.Current.CancellationToken);

        var handed = Assert.Single(inner.ProducedMessages).Message;
        Assert.Equal(callerSetOne ? [ValidTraceParent, wrappers] : [wrappers], All(handed.Headers, "traceparent"));
        AssertIds(LogsOf(test), "11111111111111111111111111111111", "2222222222222222");
    }

    [Fact]
    public void A_hand_written_log_records_the_span_current_at_the_call()
    {
        var test = Guid.NewGuid().ToString();
        RunningTest.Value = test;
        var tracker = new KafkaTracker(MakeOptions());
        using var span = new Activity("handle order").SetIdFormat(ActivityIdFormat.W3C).Start();

        tracker.LogProduce(new KafkaOperationInfo(KafkaOperation.Produce, "orders"), "v");
        tracker.LogConsume(new KafkaOperationInfo(KafkaOperation.Consume, "orders"), "v");

        var entries = LogsOf(test);
        Assert.Equal(4, entries.Length);
        Assert.All(entries, e => Assert.Equal((span.TraceId.ToHexString(), span.SpanId.ToHexString()), (e.ActivityTraceId, e.ActivitySpanId)));
    }

    // ActivityContext.TryParse, as the runtime parses a traceparent when it parents a span: what it refuses, a produce
    // records the span current at the call for, and a consume nothing. The runtime takes a later version only at the
    // length of version 00, so a version 01 value with more fields is refused, as it is when a span is parented on it.
    [Theory]
    [InlineData(ValidTraceParent, true)]
    [InlineData("01-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01", true)]
    [InlineData("01-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01-what-a-later-version-adds", false)]
    [InlineData("00-0AF7651916CD43DD8448EB211C80319C-B7AD6B7169203331-01", false)]
    [InlineData("ff-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01", false)]
    [InlineData("00-00000000000000000000000000000000-b7ad6b7169203331-01", false)]
    [InlineData("00-0af7651916cd43dd8448eb211c80319c-0000000000000000-01", false)]
    [InlineData("00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01-trailing", false)]
    public async Task A_traceparent_is_read_as_the_runtime_reads_it(string traceParent, bool parents)
    {
        var producing = Guid.NewGuid().ToString();
        RunningTest.Value = producing;
        var options = MakeOptions();
        var inner = new FakeProducer<string, string>();
        var producer = new TrackingKafkaProducer<string, string>(inner, new KafkaTracker(options), options);
        var message = new Message<string, string> { Key = "k", Value = "v", Headers = new Headers { { "traceparent", Encoding.UTF8.GetBytes(traceParent) } } };
        using (var span = new Activity("caller").SetIdFormat(ActivityIdFormat.W3C).Start())
        {
            await producer.ProduceAsync("orders", message, TestContext.Current.CancellationToken);
            AssertIds(ProducesOf(producing), parents ? "0af7651916cd43dd8448eb211c80319c" : span.TraceId.ToHexString(), parents ? "b7ad6b7169203331" : span.SpanId.ToHexString());
        }

        var consuming = Consume(Assert.Single(inner.ProducedMessages).Message, fetcherTest: null);

        AssertIds(ConsumesOf(consuming), parents ? "0af7651916cd43dd8448eb211c80319c" : null, parents ? "b7ad6b7169203331" : null);
    }

    // ─── Consume ───────────────────────────────────────────────────────────────

    // Consumes the message on a flow of its own, as a host's consumer loop does: the fetcher answers fetcherTest, or
    // throws as it does where no test runs, so the message's identity decides. Returns the test the message names.
    private static string Consume(Message<string, string> message, string? fetcherTest, Activity? pollingLoopSpan = null)
    {
        var testId = Header(message.Headers, TestTrackingMessageHeaders.TestId)!;
        var thread = new Thread(() =>
        {
            RunningTest.Value = fetcherTest;
            pollingLoopSpan?.Start();
            var options = MakeOptions();
            var consumer = new TrackingKafkaConsumer<string, string>(
                new ScriptedConsumer<string, string>().Returns(new ConsumeResult<string, string>
                {
                    TopicPartitionOffset = new TopicPartitionOffset("orders", new Partition(0), new Offset(3)),
                    Message = message,
                }),
                new KafkaTracker(options), options);
            consumer.Consume(TimeSpan.Zero);
            pollingLoopSpan?.Stop();
        });
        thread.Start();
        thread.Join();
        return testId;
    }

    private static async Task<Message<string, string>> ProducedUnder(string test, Message<string, string>? message = null)
    {
        RunningTest.Value = test;
        var options = MakeOptions();
        var inner = new FakeProducer<string, string>();
        var producer = new TrackingKafkaProducer<string, string>(inner, new KafkaTracker(options), options);
        await producer.ProduceAsync("orders", message ?? new Message<string, string> { Key = "k", Value = "v" }, TestContext.Current.CancellationToken);
        RunningTest.Value = null;
        return Assert.Single(inner.ProducedMessages).Message;
    }

    [Fact]
    public async Task A_consume_under_the_test_its_message_names_records_the_messages_trace_and_parent_span()
    {
        var test = Guid.NewGuid().ToString();
        var sent = await ProducedUnder(test, new Message<string, string>
        {
            Key = "k", Value = "v", Headers = new Headers { { "traceparent", Encoding.UTF8.GetBytes(ValidTraceParent) } },
        });

        Consume(sent, fetcherTest: null);

        AssertIds(ConsumesOf(test), "0af7651916cd43dd8448eb211c80319c", "b7ad6b7169203331");
        Assert.All(LogsOf(test), e => Assert.Equal("0af7651916cd43dd8448eb211c80319c", e.ActivityTraceId));
    }

    [Fact]
    public async Task A_consume_recorded_under_another_test_than_its_message_names_records_no_ids()
    {
        var producing = Guid.NewGuid().ToString();
        var other = Guid.NewGuid().ToString();
        var sent = await ProducedUnder(producing, new Message<string, string>
        {
            Key = "k", Value = "v", Headers = new Headers { { "traceparent", Encoding.UTF8.GetBytes(ValidTraceParent) } },
        });

        Consume(sent, fetcherTest: other);

        AssertIds(LogsOf(other), null, null);
    }

    [Fact]
    public async Task A_consume_of_a_message_with_no_traceparent_records_no_ids_whatever_span_the_polling_loop_runs_in()
    {
        var test = Guid.NewGuid().ToString();
        var sent = await ProducedUnder(test);
        Assert.Empty(All(sent.Headers, "traceparent"));

        Consume(sent, fetcherTest: null, pollingLoopSpan: new Activity("poll loop").SetIdFormat(ActivityIdFormat.W3C));

        AssertIds(ConsumesOf(test), null, null);
    }

    // ─── The Kronikol trace id (Q19) ───────────────────────────────────────────

    [Fact]
    public async Task A_message_produced_while_serving_a_request_carries_its_trace_id_and_the_consume_takes_it()
    {
        var test = Guid.NewGuid().ToString();
        var served = Guid.NewGuid();
        var accessor = RequestHeaderAccessor.For($"Test {test}", test);
        accessor.HttpContext!.Request.Headers[TestTrackingHttpHeaders.TraceIdHeader] = served.ToString();
        var options = MakeOptions();
        options.CurrentTestInfoFetcher = null;
        var inner = new FakeProducer<string, string>();
        var producer = new TrackingKafkaProducer<string, string>(inner, new KafkaTracker(options, accessor), options);

        await producer.ProduceAsync("orders", new Message<string, string> { Key = "k", Value = "v" }, TestContext.Current.CancellationToken);
        var sent = Assert.Single(inner.ProducedMessages).Message;
        Consume(sent, fetcherTest: null);

        Assert.Equal(served.ToString(), Header(sent.Headers, TestTrackingMessageHeaders.TraceId));
        var entries = LogsOf(test);
        Assert.Equal(4, entries.Length);
        Assert.All(entries, e => Assert.Equal(served, e.TraceId));
    }

    [Fact]
    public async Task A_message_produced_outside_a_request_carries_the_produces_own_trace_id()
    {
        var test = Guid.NewGuid().ToString();

        var sent = await ProducedUnder(test);

        var produce = LogsOf(test);
        Assert.Equal(produce[0].TraceId.ToString(), Header(sent.Headers, TestTrackingMessageHeaders.TraceId));
    }

    // ─── The whole-test flow (T31) ─────────────────────────────────────────────

    // A consumer on a flow of its own, as a host's is: one that parents its handler's span on the message
    // (Confluent.Kafka.Extensions.Diagnostics does) puts the handler's spans in the producer's trace, which the consume
    // claims for the message's test, so the scenario's whole-test flow shows them; one that only links to it
    // (OpenTelemetry's contrib package does) starts a trace no call of the test claims.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_consumers_handler_spans_are_in_the_scenarios_flow_exactly_when_it_parents_on_the_message(bool parentsOnTheMessage)
    {
        using var source = new ActivitySource($"Kronikol.Tests.Kafka.Flow.{Guid.NewGuid():N}");
        var stopped = new System.Collections.Concurrent.ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s == source,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);
        var test = Guid.NewGuid().ToString();

        Message<string, string> sent;
        using (var request = source.StartActivity("POST /orders", ActivityKind.Server)!)
        {
            sent = await ProducedUnder(test, new Message<string, string>
            {
                Key = "k", Value = "v",
                Headers = new Headers { { "traceparent", Encoding.UTF8.GetBytes(request.Id!) } },
            });
        }

        var consumer = new Thread(() =>
        {
            Consume(sent, fetcherTest: null);
            var message = ActivityContext.Parse(Header(sent.Headers, "traceparent")!, null);
            using var handler = parentsOnTheMessage
                ? source.StartActivity("handle order", ActivityKind.Consumer, message)
                : source.StartActivity("handle order", ActivityKind.Consumer, default(ActivityContext), links: [new ActivityLink(message)]);
            using var query = source.StartActivity("SELECT orders");
        });
        consumer.Start();
        consumer.Join();

        var segments = InternalFlowSegmentBuilder.BuildWholeTestSegments(LogsOf(test), stopped.ToArray());
        var names = segments.TryGetValue($"iflow-test-{test}", out var flow) ? flow.Spans.Select(s => s.DisplayName).ToArray() : [];
        Assert.Contains("POST /orders", names);
        if (parentsOnTheMessage)
            Assert.Contains("handle order", names);
        else
            Assert.DoesNotContain("handle order", names);
        Assert.Equal(parentsOnTheMessage, names.Contains("SELECT orders"));
    }

    // The audit's P2 shape: a consume recorded under another test than its message names. Had it recorded the message's
    // trace, two tests would claim the producer's trace and the whole-test flow keeps such a trace for neither; it
    // records none, so the producing test's flow keeps its request and the request's spans (T55).
    [Fact]
    public async Task A_consume_under_another_test_leaves_the_producing_tests_flow_its_spans()
    {
        using var source = new ActivitySource($"Kronikol.Tests.Kafka.Flow.{Guid.NewGuid():N}");
        var stopped = new System.Collections.Concurrent.ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s == source,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);
        var producing = Guid.NewGuid().ToString();
        var other = Guid.NewGuid().ToString();

        Message<string, string> sent;
        using (var request = source.StartActivity("POST /orders", ActivityKind.Server)!)
        {
            using (source.StartActivity("INSERT orders")) { }
            sent = await ProducedUnder(producing, new Message<string, string>
            {
                Key = "k", Value = "v",
                Headers = new Headers { { "traceparent", Encoding.UTF8.GetBytes(request.Id!) } },
            });
        }
        Consume(sent, fetcherTest: other);

        var logs = LogsOf(producing).Concat(LogsOf(other)).ToArray();
        var segments = InternalFlowSegmentBuilder.BuildWholeTestSegments(logs, stopped.ToArray());
        var names = segments.TryGetValue($"iflow-test-{producing}", out var flow) ? flow.Spans.Select(s => s.DisplayName).ToArray() : [];
        Assert.Contains("POST /orders", names);
        Assert.Contains("INSERT orders", names);
    }

    // The listener (F14) has its facts in KafkaListenerStartTests, which reset it first.
}
