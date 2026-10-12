using Confluent.Kafka;
using Kronikol.Extensions.Kafka;
using Kronikol.Extensions.Kafka.BuildInterception;
using Kronikol.Tracking;
using static Kronikol.Tests.Kafka.BuildInterception.BuildInterceptionTestSupport;

namespace Kronikol.Tests.Kafka.BuildInterception;

// plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.md R1, ask 1 of #135: wrapper libraries share one Kafka client between
// producers with new DependentProducerBuilder<K,V>(handle).Build(), so enabling a producer pair intercepts that Build()
// as well, by the same dispatch as ProducerBuilder's (T22, T23).
public class KafkaDependentProducerTests : IDisposable
{
    // The running test, read from the flow as a framework's fetcher reads it, so a thread under another flow's context
    // reads that flow's test.
    private static readonly AsyncLocal<string?> RunningTest = new();

    public void Dispose() => KafkaBuildInterceptor.Reset();

    private static KafkaTrackingOptions MakeOptions() => new()
    {
        ServiceName = "Kafka",
        CallerName = "Orders",
        CurrentTestInfoFetcher = () => RunningTest.Value is { } id ? ($"Test {id}", id) : ("No test", "none"),
    };

    [Fact]
    public void A_dependent_producer_of_an_enabled_pair_is_tracked()
    {
        KafkaBuildInterceptor.EnableProducerTracking<string, string>(MakeOptions());
        using var owner = Built(() => new ProducerBuilder<string, string>(ProducerConfig()).Build());

        using var dependent = Built(() => new DependentProducerBuilder<string, string>(owner.Handle).Build());

        Assert.IsType<TrackingKafkaProducer<string, string>>(dependent);
    }

    [Fact]
    public void A_dependent_producer_of_a_pair_that_is_not_enabled_is_Confluents_own_of_its_pair()
    {
        KafkaBuildInterceptor.EnableProducerTracking<string, string>(MakeOptions());
        using var owner = Built(() => new ProducerBuilder<string, string>(ProducerConfig()).Build());

        using var dependent = Built(() => new DependentProducerBuilder<Null, string>(owner.Handle).Build());

        AssertConfluentClient(dependent, "Producer`2", typeof(Null), typeof(string));
    }

    [Fact]
    public void A_dependent_producer_of_an_enabled_value_type_pair_is_tracked_and_its_other_pairs_are_not()
    {
        KafkaBuildInterceptor.EnableProducerTracking<int, string>(MakeOptions());
        using var owner = Built(() => new ProducerBuilder<string, string>(ProducerConfig()).Build());

        using var ints = Built(() => new DependentProducerBuilder<int, string>(owner.Handle).Build());
        using var bytes = Built(() => new DependentProducerBuilder<int, byte[]>(owner.Handle).Build());

        Assert.IsType<TrackingKafkaProducer<int, string>>(ints);
        AssertConfluentClient(bytes, "Producer`2", typeof(int), typeof(byte[]));
    }

    [Fact]
    public void Enabling_a_producer_pair_patches_the_dependent_builder_once_per_shape()
    {
        KafkaBuildInterceptor.EnableProducerTracking<string, string>(MakeOptions());
        KafkaBuildInterceptor.EnableProducerTracking<Null, string>(MakeOptions());

        Assert.Equal(1, InterceptorPrefixes(typeof(DependentProducerBuilder<object, object>)));
    }

    [Fact]
    public void A_dependent_producers_produce_is_its_callers_though_the_owner_delivers_under_the_owners_test()
    {
        var ownerTest = Guid.NewGuid().ToString();
        var callerTest = Guid.NewGuid().ToString();
        KafkaBuildInterceptor.EnableProducerTracking<string, string>(MakeOptions());
        RunningTest.Value = ownerTest;
        using var owner = Built(() => new ProducerBuilder<string, string>(ProducerConfig()).Build());
        RunningTest.Value = callerTest;
        using var dependent = Built(() => new DependentProducerBuilder<string, string>(owner.Handle).Build());
        using var delivered = new ManualResetEventSlim();

        // No broker runs, so the owner's poll thread reports the message timed out after a second.
        dependent.Produce("orders", new Message<string, string> { Key = "k", Value = "v" }, _ => delivered.Set());

        Assert.True(delivered.Wait(TimeSpan.FromSeconds(30)), "no delivery report arrived");
        var entries = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == ownerTest || l.TestId == callerTest).ToArray();
        Assert.Equal(2, entries.Length);
        Assert.All(entries, e => Assert.Equal(callerTest, e.TestId));
    }
}
