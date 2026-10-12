using Confluent.Kafka;
using Kronikol.Extensions.Kafka;
using Kronikol.Tracking;

namespace Kronikol.Tests.Kafka;

// plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.md F20 and Q18: with CaptureBackground on, a produce made outside any scenario
// resolves the background identity, and stamping that on the message made its consumer adopt it as a scenario's (source
// Scope, a correlation stored under "unknown"). Only an identity that names a scenario travels, as gRPC's since 4.13.0.
[Collection(BackgroundCaptureCollection.Name)]
public class KafkaBackgroundIdentityTests
{
    private static KafkaTrackingOptions OutsideATest() => new()
    {
        ServiceName = "Kafka",
        CallerName = "Worker",
        CurrentTestInfoFetcher = () => throw new InvalidOperationException("No test is running"),
    };

    [Fact]
    public async Task A_produce_outside_any_scenario_carries_no_identity_and_its_consume_stays_the_backgrounds()
    {
        var topic = $"background-{Guid.NewGuid():N}";
        var key = Guid.NewGuid().ToString();
        var options = OutsideATest();
        var inner = new FakeProducer<string, string>();
        var producer = new TrackingKafkaProducer<string, string>(inner, new KafkaTracker(options), options);
        var capture = RequestResponseLogger.CaptureBackground;
        RequestResponseLogger.CaptureBackground = true;
        try
        {
            await producer.ProduceAsync(topic, new Message<string, string> { Key = key, Value = "v" }, TestContext.Current.CancellationToken);
            var sent = Assert.Single(inner.ProducedMessages).Message;
            Assert.Null(sent.Headers);

            var consumer = new TrackingKafkaConsumer<string, string>(
                new ScriptedConsumer<string, string>().Returns(new ConsumeResult<string, string>
                {
                    TopicPartitionOffset = new TopicPartitionOffset(topic, new Partition(0), new Offset(0)),
                    Message = sent,
                }),
                new KafkaTracker(options), options);
            consumer.Consume(TimeSpan.Zero);

            Assert.Null(TestIdentityScope.Current);
            Assert.Null(TestCorrelationStore.Lookup(CorrelationKeys.Kafka("Kafka", key)));
            var consume = Assert.Single(RequestResponseLogger.RequestAndResponseLogs,
                l => l.Type == RequestResponseType.Request && l.Method.Value!.ToString() == $"Consume ← {topic}");
            Assert.Equal(AttributionSource.None, consume.AttributionSource);
        }
        finally
        {
            RequestResponseLogger.CaptureBackground = capture;
            TestIdentityScope.ClearMessageIdentity();
            TestCorrelationStore.Remove(CorrelationKeys.Kafka("Kafka", key));
        }
    }
}

/// <summary>
/// Facts that switch on <see cref="RequestResponseLogger.CaptureBackground"/>, a process-wide setting every capture
/// reads, run here, after every parallel collection.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BackgroundCaptureCollection
{
    public const string Name = "BackgroundCapture";
}
