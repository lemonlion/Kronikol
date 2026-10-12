using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using Kronikol.Extensions.Kafka;
using Kronikol.InternalFlow;
using Kronikol.Tracking;

namespace Kronikol.Tests.Kafka;

// plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.md 4.5, moved into R1 (the plan's log says why): TrackConsume's scope is the
// window a consumed message is handled in. It starts a Kronikol.Kafka consumer span, parented by the message's
// traceparent when the consume is the message's own test's, logs the consume's request at its start and its response
// at its end, so the handler's spans are the span's children, its calls lie inside the consume and nest under it, and
// the consume's duration is the processing time (T40, T41, T42, T63).
public class KafkaConsumeScopeTests
{
    private const string TraceId = "0af7651916cd43dd8448eb211c80319c";
    private const string ProducerSpanId = "b7ad6b7169203331";

    private static readonly AsyncLocal<string?> RunningTest = new();

    private static KafkaTrackingOptions MakeOptions() => new()
    {
        ServiceName = "Kafka",
        CallerName = "Orders",
        AutoCorrelateOnConsume = false,
        CurrentTestInfoFetcher = () => RunningTest.Value is { } id ? ($"Test {id}", id) : throw new InvalidOperationException("No test is running"),
    };

    private static RequestResponseLog[] LogsOf(string testId) =>
        RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId).ToArray();

    // A message the test named `test` produced, with its traceparent when one is given.
    private static ConsumeResult<string, string> Consumed(string test, string? traceParent = $"00-{TraceId}-{ProducerSpanId}-01")
    {
        var headers = new Headers
        {
            { "kronikol-test-name", Encoding.UTF8.GetBytes("Place an order") },
            { "kronikol-test-id", Encoding.UTF8.GetBytes(test) },
        };
        if (traceParent is not null)
            headers.Add("traceparent", Encoding.UTF8.GetBytes(traceParent));
        return new ConsumeResult<string, string>
        {
            TopicPartitionOffset = new TopicPartitionOffset("orders", new Partition(0), new Offset(3)),
            Message = new Message<string, string> { Key = "order-1", Value = "{\"Status\":\"Placed\"}", Headers = headers },
        };
    }

    // The handler's own source, and every span stopped under it or Kronikol.Kafka.
    private sealed class Spans : IDisposable
    {
        public readonly ActivitySource Handler = new($"Kronikol.Tests.Kafka.Handler.{Guid.NewGuid():N}");
        public readonly ConcurrentQueue<Activity> Stopped = new();
        private readonly ActivityListener _listener;

        public Spans()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = s => s == Handler || s.Name == "Kronikol.Kafka",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = Stopped.Enqueue,
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public void Dispose()
        {
            _listener.Dispose();
            Handler.Dispose();
        }
    }

    [Fact]
    public void Inside_the_scope_a_Kronikol_Kafka_span_parented_by_the_messages_traceparent_is_current_and_the_handlers_spans_are_its_children()
    {
        using var spans = new Spans();
        var test = Guid.NewGuid().ToString();
        RunningTest.Value = null;
        var tracker = new KafkaTracker(MakeOptions());

        Activity? scope, child;
        using (tracker.TrackConsume(Consumed(test)))
        {
            scope = Activity.Current;
            using (child = spans.Handler.StartActivity("handle order")) { }
        }

        Assert.NotNull(scope);
        Assert.Equal("Kronikol.Kafka", scope.Source.Name);
        Assert.Equal("orders process", scope.DisplayName);
        Assert.Equal(ActivityKind.Consumer, scope.Kind);
        Assert.Equal(TraceId, scope.TraceId.ToHexString());
        Assert.Equal(ProducerSpanId, scope.ParentSpanId.ToHexString());
        Assert.Equal(scope.SpanId, child!.ParentSpanId);
        Assert.All(LogsOf(test), entry =>
        {
            Assert.Equal(TraceId, entry.ActivityTraceId);
            Assert.Equal(scope.SpanId.ToHexString(), entry.ActivitySpanId);
        });
    }

    // T55 for the scope: a consume recorded under another test than its message names claims a trace of its own, so the
    // producer's trace stays one test's; and it never takes the polling loop's span as its parent (T56).
    [Fact]
    public void A_scope_under_another_test_than_its_message_names_starts_a_trace_of_its_own_not_the_loops()
    {
        using var spans = new Spans();
        var producing = Guid.NewGuid().ToString();
        var consuming = Guid.NewGuid().ToString();
        var tracker = new KafkaTracker(MakeOptions());
        using var loop = spans.Handler.StartActivity("poll loop");

        RunningTest.Value = consuming;
        Activity? scope;
        using (tracker.TrackConsume(Consumed(producing)))
            scope = Activity.Current;

        Assert.NotNull(scope);
        Assert.Equal("Kronikol.Kafka", scope.Source.Name);
        Assert.NotEqual(TraceId, scope.TraceId.ToHexString());
        Assert.NotEqual(loop!.TraceId, scope.TraceId);
        Assert.Equal(default, scope.ParentSpanId);
        var consume = Assert.Single(LogsOf(consuming), l => l.Type == RequestResponseType.Request);
        Assert.Equal(scope.TraceId.ToHexString(), consume.ActivityTraceId);
        Assert.Empty(LogsOf(producing));
    }

    [Fact]
    public void A_scope_over_a_message_with_no_traceparent_starts_a_trace_of_its_own_not_the_loops()
    {
        using var spans = new Spans();
        var test = Guid.NewGuid().ToString();
        RunningTest.Value = null;
        var tracker = new KafkaTracker(MakeOptions());
        using var loop = spans.Handler.StartActivity("poll loop");

        Activity? scope;
        using (tracker.TrackConsume(Consumed(test, traceParent: null)))
            scope = Activity.Current;

        Assert.NotNull(scope);
        Assert.NotEqual(loop!.TraceId, scope.TraceId);
        Assert.Equal(default, scope.ParentSpanId);
    }

    [Fact]
    public void After_the_scope_the_span_current_before_it_is_current_again()
    {
        using var spans = new Spans();
        RunningTest.Value = null;
        var tracker = new KafkaTracker(MakeOptions());
        using var loop = spans.Handler.StartActivity("poll loop");

        using (tracker.TrackConsume(Consumed(Guid.NewGuid().ToString())))
            Assert.NotSame(loop, Activity.Current);

        Assert.Same(loop, Activity.Current);
    }

    // T41 and the window: the consume's request is stored when the scope starts and its response when it ends, so what
    // the handler logs lies between them and the consume's duration is the time the message took to handle.
    [Fact]
    public async Task The_consume_is_logged_when_the_scope_starts_and_answered_when_it_ends()
    {
        var test = Guid.NewGuid().ToString();
        RunningTest.Value = null;
        var tracker = new KafkaTracker(MakeOptions());
        var invoices = new TrackingKafkaProducer<string, string>(new FakeProducer<string, string>(), new KafkaTracker(MakeOptions()), MakeOptions());

        using (tracker.TrackConsume(Consumed(test)))
        {
            await Task.Delay(40);
            await invoices.ProduceAsync("invoices", new Message<string, string> { Key = "i", Value = "v" });
        }

        var entries = LogsOf(test);
        Assert.Equal(4, entries.Length);
        Assert.Equal((RequestResponseType.Request, "Orders"), (entries[0].Type, entries[0].ServiceName));
        Assert.Equal("Kafka", entries[1].ServiceName);
        Assert.Equal("Kafka", entries[2].ServiceName);
        Assert.Equal((RequestResponseType.Response, "Orders"), (entries[3].Type, entries[3].ServiceName));
        Assert.Equal(entries[0].RequestResponseId, entries[3].RequestResponseId);
        Assert.True(entries[3].Timestamp - entries[0].Timestamp >= TimeSpan.FromMilliseconds(35),
            $"the consume took {entries[3].Timestamp - entries[0].Timestamp}");
    }

    // T63: the response is logged after the handler ran, and what it says was captured with the request.
    [Fact]
    public void The_response_carries_the_test_phase_and_ids_captured_with_the_request()
    {
        using var spans = new Spans();
        var test = Guid.NewGuid().ToString();
        RunningTest.Value = null;
        var options = MakeOptions() with { SetupVerbosity = KafkaTrackingVerbosity.Raw, ActionVerbosity = KafkaTrackingVerbosity.Summarised };
        var tracker = new KafkaTracker(options);

        TestPhaseContext.Current = TestPhase.Setup;
        using (tracker.TrackConsume(Consumed(test)))
        {
            TestPhaseContext.Current = TestPhase.Action;
            RunningTest.Value = Guid.NewGuid().ToString();
            TestIdentityScope.ClearMessageIdentity();
        }
        RunningTest.Value = null;

        var request = Assert.Single(LogsOf(test), l => l.Type == RequestResponseType.Request);
        var response = Assert.Single(LogsOf(test), l => l.Type == RequestResponseType.Response);
        Assert.Equal(TestPhase.Setup, response.Phase);
        Assert.Equal("Consume orders[0]@3", request.Method.Value);
        Assert.Equal("Consume orders[0]@3", response.Method.Value);
        Assert.Equal((request.ActivityTraceId, request.ActivitySpanId), (response.ActivityTraceId, response.ActivitySpanId));
    }

    [Fact]
    public void Under_the_background_a_scope_logs_nothing_and_starts_no_span()
    {
        using var spans = new Spans();
        RunningTest.Value = null;
        var tracker = new KafkaTracker(MakeOptions());
        var result = Consumed(Guid.NewGuid().ToString());
        result.Message.Headers = new Headers { { "traceparent", Encoding.UTF8.GetBytes($"00-{TraceId}-{ProducerSpanId}-01") } };
        var before = Activity.Current;

        using (tracker.TrackConsume(result))
            Assert.Same(before, Activity.Current);

        Assert.DoesNotContain(spans.Stopped, s => s.Source.Name == "Kronikol.Kafka" && s.TraceId.ToHexString() == TraceId);
    }

    [Fact]
    public void Disposing_a_scope_twice_answers_the_consume_once()
    {
        var test = Guid.NewGuid().ToString();
        RunningTest.Value = null;
        var scope = new KafkaTracker(MakeOptions()).TrackConsume(Consumed(test));

        scope.Dispose();
        scope.Dispose();

        Assert.Single(LogsOf(test), l => l.Type == RequestResponseType.Response);
    }

    [Fact]
    public void With_consumes_untracked_the_scope_still_makes_the_messages_identity_current_and_logs_nothing()
    {
        var test = Guid.NewGuid().ToString();
        RunningTest.Value = null;
        var tracker = new KafkaTracker(MakeOptions() with { TrackConsume = false });

        using (tracker.TrackConsume(Consumed(test)))
            Assert.Equal(test, TestIdentityScope.Current?.Id);

        Assert.Empty(LogsOf(test));
    }

    // Kronikol.Kafka is a well-known source, so the default granularity keeps a trace holding the scope's span.
    [Fact]
    public void Kronikol_Kafka_is_a_well_known_source() =>
        Assert.Contains("Kronikol.Kafka", InternalFlowSpanCollector.WellKnownAutoInstrumentationSources);

    // T42: the consume arrow's segment holds the handler's spans; a producer's span that ended before the consume began
    // is not in it.
    [Fact]
    public async Task The_consumes_segment_holds_the_handlers_spans_and_not_the_producers()
    {
        using var spans = new Spans();
        var test = Guid.NewGuid().ToString();
        RunningTest.Value = null;
        var tracker = new KafkaTracker(MakeOptions());

        string traceParent;
        using (var publish = spans.Handler.StartActivity("orders publish", ActivityKind.Producer)!)
            traceParent = publish.Id!;
        await Task.Delay(120);

        using (tracker.TrackConsume(Consumed(test, traceParent)))
        {
            using (spans.Handler.StartActivity("handle order")) { }
            using (spans.Handler.StartActivity("SELECT orders")) { }
        }

        var consume = Assert.Single(LogsOf(test), l => l.Type == RequestResponseType.Request);
        var segments = InternalFlowSegmentBuilder.BuildSegments(LogsOf(test), spans.Stopped.ToArray());
        var names = segments[$"iflow-{consume.RequestResponseId}"].Spans.Select(s => s.DisplayName).ToArray();
        Assert.Contains("orders process", names);
        Assert.Contains("handle order", names);
        Assert.Contains("SELECT orders", names);
        Assert.DoesNotContain("orders publish", names);
    }
}
