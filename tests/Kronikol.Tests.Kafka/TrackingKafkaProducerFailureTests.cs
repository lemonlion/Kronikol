using Confluent.Kafka;
using Kronikol.Extensions.Kafka;
using Kronikol.Tracking;

namespace Kronikol.Tests.Kafka;

// plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.md section 4.2: a produce that fails is drawn with its error, on every path
// (F5, F6, F22), with the status Confluent's error code gives (behind a bang, as FailedSend writes a thrown call's), and
// a producer with delivery reports off is neither refused nor silently missed (F7, F24).
public class TrackingKafkaProducerFailureTests
{
    private const string DeliveryReportsDisabled = "A delivery handler was specified, but delivery reports are disabled.";

    private readonly string _testId = Guid.NewGuid().ToString();

    private KafkaTrackingOptions MakeOptions() => new()
    {
        ServiceName = "Kafka",
        CallerName = "Orders",
        Verbosity = KafkaTrackingVerbosity.Detailed,
        CurrentTestInfoFetcher = () => ("Failure Test", _testId),
    };

    private RequestResponseLog[] Logs() => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    private RequestResponseLog Response() => Assert.Single(Logs(), l => l.Type == RequestResponseType.Response);

    private static string? Status(RequestResponseLog log) => log.StatusCode?.Value?.ToString();

    // No broker runs: a produce through a real client is queued and times out after a second.
    private static ProducerConfig Config(bool deliveryReports) => new()
    {
        BootstrapServers = "localhost:9092",
        MessageTimeoutMs = 1000,
        EnableDeliveryReports = deliveryReports,
    };

    // ─── F5: a failed delivery ─────────

    [Fact]
    public void A_failed_delivery_is_drawn_with_its_error_code_and_reason_and_the_callers_handler_still_runs()
    {
        var options = MakeOptions();
        var inner = new FakeProducer<string, string>();
        var producer = new TrackingKafkaProducer<string, string>(inner, new KafkaTracker(options), options);
        DeliveryReport<string, string>? seen = null;
        var report = new DeliveryReport<string, string>
        {
            TopicPartitionOffset = new TopicPartitionOffset("orders", Partition.Any, Offset.Unset),
            Error = new Error(ErrorCode.Local_MsgTimedOut, "Local: Message timed out"),
            Status = PersistenceStatus.NotPersisted,
        };

        producer.Produce("orders", new Message<string, string> { Key = "k", Value = "v" }, r => seen = r);
        inner.Deliver(0, report, ExecutionContext.Capture()!);

        var response = Response();
        Assert.Equal("!Local_MsgTimedOut", Status(response));
        Assert.Equal("Local: Message timed out", response.Error);
        Assert.Same(report, seen);
        Assert.Single(Logs(), l => l.Type == RequestResponseType.Request);
    }

    [Fact]
    public void A_successful_delivery_keeps_no_status()
    {
        var options = MakeOptions();
        var inner = new FakeProducer<string, string>();
        var producer = new TrackingKafkaProducer<string, string>(inner, new KafkaTracker(options), options);

        producer.Produce("orders", new Message<string, string> { Key = "k", Value = "v" });
        inner.Deliver(0, new DeliveryReport<string, string>
        {
            TopicPartitionOffset = new TopicPartitionOffset("orders", new Partition(0), new Offset(7)),
            Error = new Error(ErrorCode.NoError),
        }, ExecutionContext.Capture()!);

        var response = Response();
        Assert.Null(response.StatusCode);
        Assert.Null(response.Error);
    }

    // ─── F6: ProduceAsync that throws ─────────

    public static TheoryData<bool> BothOverloads => [true, false];

    [Theory]
    [MemberData(nameof(BothOverloads))]
    public async Task A_ProduceAsync_that_throws_ProduceException_is_drawn_with_its_error_code_and_rethrown(bool byTopic)
    {
        var options = MakeOptions();
        var inner = new FakeProducer<string, string>
        {
            ProduceAsyncResult = (topic, message, _) => Task.FromException<DeliveryResult<string, string>>(
                new ProduceException<string, string>(
                    new Error(ErrorCode.Local_MsgTimedOut, "Local: Message timed out"),
                    new DeliveryResult<string, string> { Topic = topic, Partition = Partition.Any, Offset = Offset.Unset, Message = message })),
        };
        var producer = new TrackingKafkaProducer<string, string>(inner, new KafkaTracker(options), options);
        var message = new Message<string, string> { Key = "k", Value = "v" };

        var thrown = await Assert.ThrowsAsync<ProduceException<string, string>>(() => byTopic
            ? producer.ProduceAsync("orders", message, TestContext.Current.CancellationToken)
            : producer.ProduceAsync(new TopicPartition("orders", new Partition(2)), message, TestContext.Current.CancellationToken));

        var response = Response();
        Assert.Equal("!Local_MsgTimedOut", Status(response));
        Assert.Equal(FailedSend.Describe(thrown), response.Error);
        Assert.Equal("Key: k, Value: v", Assert.Single(Logs(), l => l.Type == RequestResponseType.Request).Content);
    }

    [Fact]
    public async Task A_ProduceAsync_that_throws_something_else_is_drawn_by_the_exceptions_type()
    {
        var options = MakeOptions();
        var inner = new FakeProducer<string, string>
        {
            ProduceAsyncResult = (_, _, _) => Task.FromException<DeliveryResult<string, string>>(new InvalidOperationException("a decorator gave up")),
        };
        var producer = new TrackingKafkaProducer<string, string>(inner, new KafkaTracker(options), options);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            producer.ProduceAsync("orders", new Message<string, string> { Key = "k", Value = "v" }, TestContext.Current.CancellationToken));

        var response = Response();
        Assert.Equal("!InvalidOperationException", Status(response));
        Assert.Equal("a decorator gave up", response.Error);
    }

    [Fact]
    public async Task A_ProduceAsync_whose_wait_was_cancelled_says_the_message_may_still_be_delivered()
    {
        var options = MakeOptions();
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var inner = new FakeProducer<string, string>
        {
            ProduceAsyncResult = (_, _, token) => Task.FromCanceled<DeliveryResult<string, string>>(token),
        };
        var producer = new TrackingKafkaProducer<string, string>(inner, new KafkaTracker(options), options);

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            producer.ProduceAsync("orders", new Message<string, string> { Key = "k", Value = "v" }, cancel.Token));

        var response = Response();
        Assert.Equal("!TaskCanceledException", Status(response));
        Assert.Contains("may still be delivered", response.Error, StringComparison.Ordinal);
    }

    // ─── F22: a sync produce that throws ─────────

    [Fact]
    public void A_sync_Produce_that_throws_is_drawn_at_the_call_with_its_error_code_and_rethrown()
    {
        var options = MakeOptions();
        var inner = new FakeProducer<string, string>
        {
            ThrowOnNextProduce = new KafkaException(new Error(ErrorCode.Local_QueueFull, "Local: Queue full")),
        };
        var producer = new TrackingKafkaProducer<string, string>(inner, new KafkaTracker(options), options);

        Assert.Throws<KafkaException>(() => producer.Produce("orders", new Message<string, string> { Key = "k", Value = "v" }));

        var response = Response();
        Assert.Equal("!Local_QueueFull", Status(response));
        Assert.Equal("Local: Queue full", response.Error);
    }

    [Fact]
    public void A_sync_Produce_whose_serializer_fails_is_drawn_with_the_serialization_error_code()
    {
        var options = MakeOptions();
        var inner = new FakeProducer<string, string>
        {
            ThrowOnNextProduce = new ProduceException<string, string>(
                new Error(ErrorCode.Local_ValueSerialization, "Local: Value serialization error"),
                new DeliveryResult<string, string>()),
        };
        var producer = new TrackingKafkaProducer<string, string>(inner, new KafkaTracker(options), options);

        Assert.Throws<ProduceException<string, string>>(() => producer.Produce("orders", new Message<string, string> { Key = "k", Value = "v" }));

        Assert.Equal("!Local_ValueSerialization", Status(Response()));
    }

    // ─── F7, F24: delivery reports off ─────────

    [Fact]
    public void A_producer_with_delivery_reports_off_produces_and_is_logged_once_at_the_call()
    {
        var options = MakeOptions();
        using var real = new ProducerBuilder<string, string>(Config(deliveryReports: false)).Build();
        var producer = new TrackingKafkaProducer<string, string>(real, new KafkaTracker(options), options);

        producer.Produce("orders", new Message<string, string> { Key = "k", Value = "v" });

        var entries = Logs();
        Assert.Equal(2, entries.Length);
        Assert.Equal(entries[0].Timestamp, entries[1].Timestamp);
        Assert.Equal("Produce → orders", entries[0].Method.Value!.ToString());
        Assert.Null(entries[1].StatusCode);
    }

    [Fact]
    public void A_dependent_producer_whose_owner_has_delivery_reports_off_is_logged_at_the_call()
    {
        var options = MakeOptions();
        using var owner = new ProducerBuilder<string, string>(Config(deliveryReports: false)).Build();
        using var dependent = new DependentProducerBuilder<Null, string>(owner.Handle).Build();
        var producer = new TrackingKafkaProducer<Null, string>(dependent, new KafkaTracker(options), options);

        producer.Produce("orders", new Message<Null, string> { Value = "v" });

        Assert.Equal(2, Logs().Length);
    }

    [Fact]
    public void A_dependent_producer_whose_owner_reports_deliveries_is_logged_at_delivery()
    {
        var options = MakeOptions();
        using var owner = new ProducerBuilder<string, string>(Config(deliveryReports: true)).Build();
        using var dependent = new DependentProducerBuilder<Null, string>(owner.Handle).Build();
        var producer = new TrackingKafkaProducer<Null, string>(dependent, new KafkaTracker(options), options);

        producer.Produce("orders", new Message<Null, string> { Value = "v" });
        Assert.Empty(Logs());
        owner.Flush(TimeSpan.FromSeconds(10));

        // No broker: the delivery report is the timeout's, on the owner's poll thread.
        Assert.Equal("!Local_MsgTimedOut", Status(Response()));
    }

    [Fact]
    public void A_producer_that_refuses_the_handler_is_retried_once_without_one_and_remembered()
    {
        var options = MakeOptions();
        var inner = new FakeProducer<string, string>
        {
            Refuse = handler => handler is null ? null : new InvalidOperationException(DeliveryReportsDisabled),
        };
        var producer = new TrackingKafkaProducer<string, string>(inner, new KafkaTracker(options), options);

        producer.Produce("orders", new Message<string, string> { Key = "k", Value = "1" });
        producer.Produce("orders", new Message<string, string> { Key = "k", Value = "2" });

        Assert.Equal([true, false, false], inner.ProducedMessages.Select(p => p.Handler is not null));
        Assert.Equal(4, Logs().Length);
        Assert.All(Logs(), l => Assert.Null(l.StatusCode));
    }

    [Fact]
    public void Another_refusal_is_not_taken_for_delivery_reports_off_and_is_drawn_and_rethrown()
    {
        var options = MakeOptions();
        var inner = new FakeProducer<string, string> { Refuse = _ => new InvalidOperationException("the decorator is closed") };
        var producer = new TrackingKafkaProducer<string, string>(inner, new KafkaTracker(options), options);

        Assert.Throws<InvalidOperationException>(() => producer.Produce("orders", new Message<string, string> { Key = "k", Value = "v" }));

        Assert.Single(inner.ProducedMessages);
        Assert.Equal("!InvalidOperationException", Status(Response()));
    }

    [Fact]
    public void A_disposed_producer_is_not_taken_for_delivery_reports_off_and_is_drawn_and_rethrown()
    {
        var options = MakeOptions();
        var inner = new FakeProducer<string, string> { ThrowOnNextProduce = new ObjectDisposedException("producer", DeliveryReportsDisabled) };
        var producer = new TrackingKafkaProducer<string, string>(inner, new KafkaTracker(options), options);

        Assert.Throws<ObjectDisposedException>(() => producer.Produce("orders", new Message<string, string> { Key = "k", Value = "v" }));

        Assert.Single(inner.ProducedMessages);
        Assert.Equal("!ObjectDisposedException", Status(Response()));
    }
}
