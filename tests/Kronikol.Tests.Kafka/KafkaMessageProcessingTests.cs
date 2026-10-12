using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using Kronikol.Extensions.Kafka;
using Kronikol.Tracking;

namespace Kronikol.Tests.Kafka;

// plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.md 4.5, R2: KafkaTrackingOptions.TraceMessageProcessing traces message
// processing with no change to the consuming code. Consume returns with the message's scope open, the scope ends at the
// consumer's next call, and a produce runs under a publish span its traceparent names (T41, T43, T62, T63, T64).
public class KafkaMessageProcessingTests
{
    private const string TraceId = "0af7651916cd43dd8448eb211c80319c";
    private const string ProducerSpanId = "b7ad6b7169203331";

    private static readonly AsyncLocal<string?> RunningTest = new();

    private static KafkaTrackingOptions MakeOptions(bool traced = true) => new()
    {
        ServiceName = "Kafka",
        CallerName = "Orders",
        AutoCorrelateOnConsume = false,
        TraceMessageProcessing = traced,
        CurrentTestInfoFetcher = () => RunningTest.Value is { } id ? ($"Test {id}", id) : throw new InvalidOperationException("No test is running"),
    };

    private static RequestResponseLog[] LogsOf(string testId) =>
        RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId).ToArray();

    private static ConsumeResult<string, string> Message(string test, long offset = 3)
    {
        var headers = new Headers
        {
            { "kronikol-test-name", Encoding.UTF8.GetBytes("Place an order") },
            { "kronikol-test-id", Encoding.UTF8.GetBytes(test) },
            { "traceparent", Encoding.UTF8.GetBytes($"00-{TraceId}-{ProducerSpanId}-01") },
        };
        return new ConsumeResult<string, string>
        {
            TopicPartitionOffset = new TopicPartitionOffset("orders", new Partition(0), new Offset(offset)),
            Message = new Message<string, string> { Key = "order-1", Value = "{\"Status\":\"Placed\"}", Headers = headers },
        };
    }

    // Ends a span on a thread of its own: Activity.Stop makes the span's parent current on the thread that stops it, so
    // a span ended here leaves what is current on the caller's flow as it was.
    private static void StopElsewhere(Activity span)
    {
        var stopper = new Thread(span.Stop);
        stopper.Start();
        stopper.Join();
    }

    private static TrackingKafkaConsumer<string, string> Consumer(ScriptedConsumer<string, string> inner, bool traced = true)
    {
        var options = MakeOptions(traced);
        return new TrackingKafkaConsumer<string, string>(inner, new KafkaTracker(options), options);
    }

    // The loop's own source, and every Kronikol.Kafka span started and stopped.
    private sealed class Spans : IDisposable
    {
        public readonly ActivitySource Loop = new($"Kronikol.Tests.Kafka.Loop.{Guid.NewGuid():N}");
        public readonly ConcurrentQueue<Activity> Started = new();
        public readonly ConcurrentQueue<Activity> Stopped = new();
        private readonly ActivityListener _listener;

        public Spans()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = s => s == Loop || s.Name == "Kronikol.Kafka",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStarted = a => { if (a.Source.Name == "Kronikol.Kafka") Started.Enqueue(a); },
                ActivityStopped = a => { if (a.Source.Name == "Kronikol.Kafka") Stopped.Enqueue(a); },
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public void Dispose()
        {
            _listener.Dispose();
            Loop.Dispose();
        }
    }

    [Fact]
    public void Consume_returns_with_the_messages_span_current_and_the_consume_not_yet_answered()
    {
        using var spans = new Spans();
        var test = Guid.NewGuid().ToString();
        RunningTest.Value = null;
        var consumer = Consumer(new ScriptedConsumer<string, string>().Returns(Message(test)));

        consumer.Consume(TimeSpan.Zero);

        var span = Activity.Current;
        Assert.NotNull(span);
        Assert.Equal("Kronikol.Kafka", span.Source.Name);
        Assert.Equal("orders process", span.DisplayName);
        Assert.Equal(ProducerSpanId, span.ParentSpanId.ToHexString());
        var entry = Assert.Single(LogsOf(test));
        Assert.Equal(RequestResponseType.Request, entry.Type);
        Assert.Equal(span.SpanId.ToHexString(), entry.ActivitySpanId);
        consumer.Close();
        Assert.Null(Activity.Current);
    }

    // T41: the next Consume answers the last message's consume at its start, before it waits.
    [Fact]
    public void The_next_Consume_answers_the_consume_before_it_polls()
    {
        using var spans = new Spans();
        var test = Guid.NewGuid().ToString();
        RunningTest.Value = null;
        int? answeredWhenPolled = null;
        var consumer = Consumer(new ScriptedConsumer<string, string>()
            .Returns(Message(test))
            .ReturnsFrom(() => { answeredWhenPolled = LogsOf(test).Count(l => l.Type == RequestResponseType.Response); return null; }));

        consumer.Consume(TimeSpan.Zero);
        Thread.Sleep(30);
        consumer.Consume(TimeSpan.Zero);

        Assert.Equal(1, answeredWhenPolled);
        var entries = LogsOf(test);
        Assert.True(entries[1].Timestamp - entries[0].Timestamp >= TimeSpan.FromMilliseconds(25), $"{entries[1].Timestamp - entries[0].Timestamp}");
        Assert.Null(Activity.Current);
    }

    [Theory]
    [InlineData("Commit")]
    [InlineData("Commit(result)")]
    [InlineData("Commit(offsets)")]
    [InlineData("StoreOffset(result)")]
    [InlineData("StoreOffset(offset)")]
    [InlineData("Close")]
    [InlineData("Dispose")]
    public void The_consumers_next_call_answers_the_consume_and_ends_its_span(string call)
    {
        using var spans = new Spans();
        var test = Guid.NewGuid().ToString();
        RunningTest.Value = null;
        var result = Message(test);
        var consumer = Consumer(new ScriptedConsumer<string, string>().Returns(result));
        consumer.Consume(TimeSpan.Zero);
        var span = Activity.Current!;

        switch (call)
        {
            case "Commit": consumer.Commit(); break;
            case "Commit(result)": consumer.Commit(result); break;
            case "Commit(offsets)": consumer.Commit([result.TopicPartitionOffset]); break;
            case "StoreOffset(result)": consumer.StoreOffset(result); break;
            case "StoreOffset(offset)": consumer.StoreOffset(result.TopicPartitionOffset); break;
            case "Close": consumer.Close(); break;
            case "Dispose": consumer.Dispose(); break;
        }

        Assert.Single(LogsOf(test), l => l.Type == RequestResponseType.Response);
        Assert.True(span.IsStopped);
        Assert.Null(Activity.Current);
    }

    // T63: the next Consume clears the message's identity (F10) before the response is written, and the phase may have
    // moved on: the response keeps what its request captured.
    [Fact]
    public void The_deferred_response_keeps_the_test_phase_and_ids_of_its_request()
    {
        using var spans = new Spans();
        var test = Guid.NewGuid().ToString();
        RunningTest.Value = null;
        var consumer = Consumer(new ScriptedConsumer<string, string>().Returns(Message(test)).Returns(null));

        TestPhaseContext.Current = TestPhase.Setup;
        consumer.Consume(TimeSpan.Zero);
        TestPhaseContext.Current = TestPhase.Action;
        consumer.Consume(TimeSpan.Zero);

        var request = Assert.Single(LogsOf(test), l => l.Type == RequestResponseType.Request);
        var response = Assert.Single(LogsOf(test), l => l.Type == RequestResponseType.Response);
        Assert.Equal(TestPhase.Setup, response.Phase);
        Assert.Equal((request.ActivityTraceId, request.ActivitySpanId), (response.ActivityTraceId, response.ActivitySpanId));
    }

    // T62: the shapes the audit's probe found the first draft failing in (C/probe-output.txt P3).
    [Fact]
    public async Task A_Consume_inside_Task_Run_leaves_the_awaiting_code_its_own_span_and_never_a_stopped_one()
    {
        using var spans = new Spans();
        var test = Guid.NewGuid().ToString();
        RunningTest.Value = null;
        var consumer = Consumer(new ScriptedConsumer<string, string>().Returns(Message(test)).Returns(null));
        using var loop = spans.Loop.StartActivity("worker");

        await Task.Run(() => consumer.Consume(TimeSpan.Zero), TestContext.Current.CancellationToken);
        Assert.Same(loop, Activity.Current);
        await Task.Run(() => consumer.Consume(TimeSpan.Zero), TestContext.Current.CancellationToken);

        Assert.Same(loop, Activity.Current);
        Assert.Single(LogsOf(test), l => l.Type == RequestResponseType.Response);
    }

    [Fact]
    public void A_loop_that_wraps_each_Consume_in_its_own_span_never_parents_one_on_a_stopped_span()
    {
        using var spans = new Spans();
        RunningTest.Value = null;
        var tests = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid().ToString()).ToArray();
        Activity? currentWhenPolled = null;
        var inner = new ScriptedConsumer<string, string>();
        foreach (var (test, i) in tests.Select((t, i) => (t, i)))
            inner.ReturnsFrom(() => { currentWhenPolled = Activity.Current; return Message(test, i); });
        var consumer = Consumer(inner);
        using var root = spans.Loop.StartActivity("worker");

        for (var i = 0; i < tests.Length; i++)
        {
            using var iteration = spans.Loop.StartActivity("iteration")!;
            Assert.Same(root, iteration.Parent);
            consumer.Consume(TimeSpan.Zero);
            Assert.Same(iteration, currentWhenPolled);
            Assert.Equal("orders process", Activity.Current!.DisplayName);
        }

        Assert.Same(root, Activity.Current);
        consumer.Close();
        Assert.Same(root, Activity.Current);
        Assert.All(tests, t => Assert.Single(LogsOf(t), l => l.Type == RequestResponseType.Response));
    }

    [Fact]
    public void Two_consumers_in_one_loop_never_leave_a_stopped_span_current()
    {
        using var spans = new Spans();
        RunningTest.Value = null;
        // Whether the span current at each inner poll had ended, read at the poll: every span has ended by the end.
        var stoppedWhenPolled = new List<bool>();
        ScriptedConsumer<string, string> Polls() => new ScriptedConsumer<string, string>()
            .ReturnsFrom(() => { stoppedWhenPolled.Add(Activity.Current?.IsStopped ?? false); return Message(Guid.NewGuid().ToString()); })
            .ReturnsFrom(() => { stoppedWhenPolled.Add(Activity.Current?.IsStopped ?? false); return Message(Guid.NewGuid().ToString()); })
            .ReturnsFrom(() => { stoppedWhenPolled.Add(Activity.Current?.IsStopped ?? false); return null; });
        var a = Consumer(Polls());
        var b = Consumer(Polls());

        for (var i = 0; i < 3; i++)
        {
            a.Consume(TimeSpan.Zero);
            Assert.False(Activity.Current?.IsStopped ?? false);
            b.Consume(TimeSpan.Zero);
            Assert.False(Activity.Current?.IsStopped ?? false);
        }
        a.Close();
        b.Close();

        Assert.Equal(6, stoppedWhenPolled.Count);
        Assert.DoesNotContain(true, stoppedWhenPolled);
        Assert.False(Activity.Current?.IsStopped ?? false);
    }

    // T64: a produce runs under a publish span, and the traceparent Kronikol adds names it.
    [Fact]
    public async Task A_produce_runs_under_a_publish_span_that_its_traceparent_names()
    {
        using var spans = new Spans();
        var test = Guid.NewGuid().ToString();
        RunningTest.Value = test;
        var options = MakeOptions();
        using var confluent = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = "localhost:9092", MessageTimeoutMs = 1000 }).Build();
        var producer = new TrackingKafkaProducer<string, string>(confluent, new KafkaTracker(options), options);
        var before = Activity.Current;

        var failure = await Assert.ThrowsAsync<ProduceException<string, string>>(() =>
            producer.ProduceAsync("orders", new Message<string, string> { Key = "k", Value = "v" }, TestContext.Current.CancellationToken));

        var publish = Assert.Single(spans.Stopped, s => s.DisplayName == "orders publish");
        Assert.Equal(ActivityKind.Producer, publish.Kind);
        failure.DeliveryResult.Message.Headers.TryGetLastBytes("traceparent", out var traceParent);
        Assert.Equal(publish.Id, Encoding.UTF8.GetString(traceParent));
        Assert.All(LogsOf(test), entry => Assert.Equal(publish.SpanId.ToHexString(), entry.ActivitySpanId));
        Assert.Same(before, Activity.Current);
        RunningTest.Value = null;
    }

    // T43: with the option off (the default) the wrappers start no span of their own and answer a consume at once.
    [Fact]
    public async Task Untraced_the_wrappers_start_no_span_and_answer_a_consume_at_once()
    {
        using var spans = new Spans();
        var test = Guid.NewGuid().ToString();
        RunningTest.Value = null;
        Assert.False(new KafkaTrackingOptions().TraceMessageProcessing);
        var consumer = Consumer(new ScriptedConsumer<string, string>().Returns(Message(test)), traced: false);
        var before = Activity.Current;

        consumer.Consume(TimeSpan.Zero);
        RunningTest.Value = test;
        var options = MakeOptions(traced: false);
        await new TrackingKafkaProducer<string, string>(new FakeProducer<string, string>(), new KafkaTracker(options), options)
            .ProduceAsync("invoices", new Message<string, string> { Key = "i", Value = "v" }, TestContext.Current.CancellationToken);
        RunningTest.Value = null;

        Assert.Same(before, Activity.Current);
        Assert.Empty(spans.Started);
        var consume = LogsOf(test).Where(l => l.ServiceName == "Orders").ToArray();
        Assert.Equal(2, consume.Length);
        Assert.Equal(consume[0].Timestamp, consume[1].Timestamp);
        Assert.Equal(ProducerSpanId, consume[0].ActivitySpanId);
    }

    // A hand-written decorator's produce (#160) is the decorator's to trace: under TraceMessageProcessing TrackProduce
    // starts no publish span, which only the wrappers end, so none is left current or open after it, and the produce
    // records the caller's span.
    [Fact]
    public void A_hand_written_TrackProduce_starts_no_publish_span()
    {
        var test = Guid.NewGuid().ToString();
        RunningTest.Value = test;
        using var spans = new Spans();
        using var caller = spans.Loop.StartActivity("publish order")!;

        var produce = new KafkaTracker(MakeOptions()).TrackProduce("orders", new Message<string, string> { Key = "k", Value = "v" });
        produce.Sent();

        Assert.Same(caller, Activity.Current);
        Assert.All(LogsOf(test), e => Assert.Equal(caller.SpanId.ToHexString(), e.ActivitySpanId));
    }

    // T64, the sync form, where what the wrapper leaves current is what the caller has next (an async method's caller
    // gets its own context back whatever the method set): Produce returns before any delivery report, its publish span
    // has ended by then, and what was current before it is current again.
    [Fact]
    public void A_sync_produce_ends_its_publish_span_when_the_call_returns()
    {
        using var spans = new Spans();
        RunningTest.Value = Guid.NewGuid().ToString();
        var options = MakeOptions();
        using var caller = spans.Loop.StartActivity("handle request")!;
        var producer = new TrackingKafkaProducer<string, string>(new FakeProducer<string, string>(), new KafkaTracker(options), options);

        producer.Produce("orders", new Message<string, string> { Key = "k", Value = "v" }, _ => { });

        var publish = Assert.Single(spans.Stopped, s => s.DisplayName == "orders publish" && s.ParentSpanId == caller.SpanId);
        Assert.Equal(ActivityKind.Producer, publish.Kind);
        Assert.Same(caller, Activity.Current);
        RunningTest.Value = null;
    }

    // What is current when the publish span ends stays current unless it is the publish span: a span the producer inside
    // left current is that producer's to end, and the caller's span is not forced back over it.
    [Fact]
    public void A_span_the_inner_producer_leaves_current_stays_current_after_the_publish_span_ends()
    {
        using var spans = new Spans();
        RunningTest.Value = Guid.NewGuid().ToString();
        var options = MakeOptions();
        using var caller = spans.Loop.StartActivity("handle request")!;
        Activity? left = null;
        var inner = new FakeProducer<string, string> { OnProduce = _ => left = spans.Loop.StartActivity("inner send") };
        var producer = new TrackingKafkaProducer<string, string>(inner, new KafkaTracker(options), options);

        producer.Produce("orders", new Message<string, string> { Key = "k", Value = "v" }, _ => { });

        Assert.NotNull(left);
        Assert.Same(left, Activity.Current);
        left!.Stop();
        Activity.Current = caller;
        RunningTest.Value = null;
    }

    // A span that has ended is never left current: here the caller's own span ends while the produce is in flight, on
    // another thread, since Activity.Stop makes the stopped span's parent current on the thread that stops it.
    [Fact]
    public void A_produce_leaves_no_ended_span_current_when_the_callers_span_ended_during_it()
    {
        using var spans = new Spans();
        RunningTest.Value = Guid.NewGuid().ToString();
        var options = MakeOptions();
        var caller = spans.Loop.StartActivity("handle request")!;
        var inner = new FakeProducer<string, string> { OnProduce = _ => StopElsewhere(caller) };
        var producer = new TrackingKafkaProducer<string, string>(inner, new KafkaTracker(options), options);

        producer.Produce("orders", new Message<string, string> { Key = "k", Value = "v" }, _ => { });

        Assert.True(caller.IsStopped);
        Assert.Null(Activity.Current);
        RunningTest.Value = null;
    }

    // T62: at the next call the scope's own span, still current, gives way to what was current before the scope, here
    // the loop's span, never to nothing.
    [Fact]
    public void At_the_next_call_the_span_current_before_the_scope_is_current_again()
    {
        using var spans = new Spans();
        RunningTest.Value = null;
        using var loop = spans.Loop.StartActivity("poll loop")!;
        var consumer = Consumer(new ScriptedConsumer<string, string>().Returns(Message(Guid.NewGuid().ToString())));

        consumer.Consume(TimeSpan.Zero);
        Assert.Equal("orders process", Activity.Current?.DisplayName);
        consumer.Commit();

        Assert.Same(loop, Activity.Current);
    }

    // T62: a span the loop polled under, ended on another thread while the message was handled, is not made current
    // again when the scope closes.
    [Fact]
    public void A_span_that_ended_while_the_message_was_handled_is_not_made_current_again()
    {
        using var spans = new Spans();
        RunningTest.Value = null;
        var consumer = Consumer(new ScriptedConsumer<string, string>().Returns(Message(Guid.NewGuid().ToString())));
        var iteration = spans.Loop.StartActivity("iteration")!;
        consumer.Consume(TimeSpan.Zero);
        StopElsewhere(iteration);
        Assert.Equal("orders process", Activity.Current?.DisplayName);

        consumer.Commit();

        Assert.Null(Activity.Current);
    }
}
