using Confluent.Kafka;
using Kronikol.Extensions.Kafka;
using Kronikol.Tracking;

namespace Kronikol.Tests.Kafka;

// plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.md 5.4: the R0 and R1 behaviour through Confluent's own clients and a real
// broker, where the fakes cannot stand in: delivery reports the broker sends, a dependent producer on its owner's
// connection, and a consumer group reading back what was produced.
public class KafkaBrokerTests(KafkaBrokerFixture broker) : IClassFixture<KafkaBrokerFixture>
{
    private readonly string _test = Guid.NewGuid().ToString();
    private readonly string _topic = $"kronikol-{Guid.NewGuid():N}";

    private KafkaTrackingOptions ProducerOptions() => new()
    {
        ServiceName = "Kafka",
        CallerName = "Orders",
        AutoCorrelateOnConsume = false,
        CurrentTestInfoFetcher = () => ("Place an order", _test),
    };

    // A host's consumer: no test runs on its flow, so the message's identity decides.
    private static KafkaTrackingOptions ConsumerOptions() => new()
    {
        ServiceName = "Kafka",
        CallerName = "Billing",
        AutoCorrelateOnConsume = false,
        CurrentTestInfoFetcher = () => throw new InvalidOperationException("No test is running"),
    };

    private RequestResponseLog[] Logs(string service) =>
        RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _test && l.ServiceName == service).ToArray();

    private ProducerConfig ProducerConfig() => new() { BootstrapServers = broker.Require(), MessageTimeoutMs = 30_000 };

    private ConsumeResult<string, string> ConsumeOne(Message<string, string> expected)
    {
        var options = ConsumerOptions();
        using var consumer = new TrackingKafkaConsumer<string, string>(
            new ConsumerBuilder<string, string>(new ConsumerConfig
            {
                BootstrapServers = broker.Require(),
                GroupId = $"kronikol-{Guid.NewGuid():N}",
                AutoOffsetReset = AutoOffsetReset.Earliest,
            }).Build(),
            new KafkaTracker(options), options);
        consumer.Subscribe(_topic);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline)
            {
                if (consumer.Consume(TimeSpan.FromSeconds(1)) is { Message.Value: var value } result && value == expected.Value)
                    return result;
            }
            throw new TimeoutException($"Nothing was consumed from {_topic} in a minute.");
        }
        finally
        {
            consumer.Close();
            TestIdentityScope.ClearMessageIdentity();
        }
    }

    [Fact]
    public async Task A_produce_and_its_consume_share_one_trace_and_the_value_is_the_body()
    {
        var options = ProducerOptions();
        using var producer = new TrackingKafkaProducer<string, string>(
            new ProducerBuilder<string, string>(ProducerConfig()).Build(), new KafkaTracker(options), options);
        var message = new Message<string, string> { Key = "order-1", Value = "{\"Status\":\"Placed\"}" };

        var delivered = await producer.ProduceAsync(_topic, message, TestContext.Current.CancellationToken);
        ConsumeOne(message);

        var produce = Logs("Kafka");
        var consume = Logs("Billing");
        Assert.Equal(2, produce.Length);
        Assert.Equal(2, consume.Length);
        var trace = Assert.Single(produce.Select(e => e.ActivityTraceId).Distinct());
        Assert.NotNull(trace);
        Assert.All(consume, e => Assert.Equal(trace, e.ActivityTraceId));
        Assert.All(consume, e => Assert.Equal(produce[0].TraceId, e.TraceId));
        Assert.Equal("{\"Status\":\"Placed\"}", consume.Single(e => e.Type == RequestResponseType.Request).Content);
        Assert.Contains(("kafka-key", "order-1"), consume.Single(e => e.Type == RequestResponseType.Request).Headers);
        Assert.Equal(PersistenceStatus.Persisted, delivered.Status);
    }

    [Fact]
    public void A_dependent_producers_produce_is_recorded_with_the_offset_the_broker_wrote()
    {
        var options = ProducerOptions();
        options.Verbosity = KafkaTrackingVerbosity.Raw;
        using var owner = new ProducerBuilder<string, string>(ProducerConfig()).Build();
        using var dependent = new TrackingKafkaProducer<string, string>(
            new DependentProducerBuilder<string, string>(owner.Handle).Build(), new KafkaTracker(options), options);
        using var delivered = new ManualResetEventSlim();
        DeliveryReport<string, string>? report = null;

        dependent.Produce(_topic, new Message<string, string> { Key = "k", Value = "v" }, r => { report = r; delivered.Set(); });

        Assert.True(delivered.Wait(TimeSpan.FromSeconds(60)), "no delivery report arrived");
        Assert.False(report!.Error.IsError, report.Error.Reason);
        var request = Assert.Single(Logs("Kafka"), e => e.Type == RequestResponseType.Request);
        Assert.Equal($"Produce {_topic}[{report.Partition.Value}]@{report.Offset.Value}", request.Method.Value!.ToString());
    }

    [Fact]
    public void A_message_the_broker_refuses_is_drawn_with_the_brokers_error_code()
    {
        var options = ProducerOptions();
        var config = ProducerConfig();
        config.MessageMaxBytes = 3_000_000;
        using var producer = new TrackingKafkaProducer<string, string>(
            new ProducerBuilder<string, string>(config).Build(), new KafkaTracker(options), options);
        using var delivered = new ManualResetEventSlim();
        DeliveryReport<string, string>? report = null;

        // Past the broker's own limit, about a megabyte, though within what the client allows.
        producer.Produce(_topic, new Message<string, string> { Key = "k", Value = new string('x', 2_000_000) }, r => { report = r; delivered.Set(); });

        Assert.True(delivered.Wait(TimeSpan.FromSeconds(60)), "no delivery report arrived");
        Assert.True(report!.Error.IsError);
        var response = Assert.Single(Logs("Kafka"), e => e.Type == RequestResponseType.Response);
        Assert.Equal("!" + report.Error.Code, response.StatusCode!.Value.ToString());
    }
}
