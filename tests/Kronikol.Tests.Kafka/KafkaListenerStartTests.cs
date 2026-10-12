using Confluent.Kafka;
using Kronikol.Extensions.Kafka;
using Kronikol.InternalFlow;

namespace Kronikol.Tests.Kafka;

/// <summary>
/// plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.md F14, T31: a suite that tracks only Kafka has spans for its entries' W3C ids
/// to join, because a Kafka tracker starts the internal-flow listener on first use, as the HTTP handler and the gRPC
/// interceptor do. Each fact resets the process-wide listener first, or one another class had started would pass it, so
/// they run in <see cref="SpanStoreClearCollection"/>, after every parallel class.
/// </summary>
[Collection(SpanStoreClearCollection.Name)]
public class KafkaListenerStartTests
{
    private static readonly AsyncLocal<string?> RunningTest = new();

    private static KafkaTrackingOptions MakeOptions() => new()
    {
        ServiceName = "Kafka",
        CallerName = "Orders",
        AutoCorrelateOnConsume = false,
        CurrentTestInfoFetcher = () => RunningTest.Value is { } id ? ($"Test {id}", id) : throw new InvalidOperationException("No test is running"),
    };

    [Fact]
    public void A_Kafka_tracker_starts_the_internal_flow_listener_when_it_first_logs()
    {
        InternalFlowActivityListener.ResetForTesting();
        RunningTest.Value = Guid.NewGuid().ToString();

        new KafkaTracker(MakeOptions()).LogProduce(new KafkaOperationInfo(KafkaOperation.Produce, "orders"), "v");

        Assert.True(InternalFlowActivityListener.IsStarted);
    }

    [Fact]
    public void A_wrapper_starts_the_internal_flow_listener_when_it_first_produces()
    {
        InternalFlowActivityListener.ResetForTesting();
        RunningTest.Value = Guid.NewGuid().ToString();
        var options = MakeOptions();
        var producer = new TrackingKafkaProducer<string, string>(new FakeProducer<string, string>(), new KafkaTracker(options), options);

        producer.Produce("orders", new Message<string, string> { Key = "k", Value = "v" });

        Assert.True(InternalFlowActivityListener.IsStarted);
    }
}

/// <summary>
/// Facts that reset the process-wide <see cref="InternalFlowActivityListener"/> run here, after every parallel
/// collection: a class whose tracker had started it would record no span until another tracker started it again.
/// <c>ProcessGlobalStoreTests</c> in Kronikol.Tests keeps every reset in a collection of this name.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SpanStoreClearCollection
{
    public const string Name = "SpanStoreClear";
}
