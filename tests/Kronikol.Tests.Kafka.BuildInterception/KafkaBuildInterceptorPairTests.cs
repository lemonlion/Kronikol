using System.Reflection;
using Confluent.Kafka;
using Kronikol.Extensions.Kafka;
using Kronikol.Extensions.Kafka.BuildInterception;
using Kronikol.Tracking;
using static Kronikol.Tests.Kafka.BuildInterception.BuildInterceptionTestSupport;

namespace Kronikol.Tests.Kafka.BuildInterception;

// plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.md section 4.1. The runtime shares one body of Build() among every pair whose
// type arguments agree on value types and are otherwise reference types, so a patch lands on a code SHAPE, not a pair:
// every builder of a patched shape must still return its own pair's client, tracked exactly when its pair is enabled.
public class KafkaBuildInterceptorPairTests : IDisposable
{
    private readonly string _testId = Guid.NewGuid().ToString();

    public void Dispose() => KafkaBuildInterceptor.Reset();

    private KafkaTrackingOptions MakeOptions() => new()
    {
        ServiceName = "Kafka",
        CallerName = "TestCaller",
        CurrentTestInfoFetcher = () => ("BuildInterception Pair Test", _testId),
    };

    // ─── F1: other reference-type pairs of an enabled pair's shape ─────────

    [Fact]
    public void A_reference_type_pair_that_is_not_enabled_builds_its_own_producer_beside_an_enabled_one()
    {
        KafkaBuildInterceptor.EnableProducerTracking<string, string>(MakeOptions());

        using var producer = Built(() => new ProducerBuilder<Null, string>(ProducerConfig()).Build());

        AssertConfluentClient(producer, "Producer`2", typeof(Null), typeof(string));
        producer.Produce("t135", new Message<Null, string> { Value = "v" });
    }

    [Fact]
    public void A_byte_array_producer_and_a_null_keyed_consumer_build_their_own_clients_beside_an_enabled_pair()
    {
        KafkaBuildInterceptor.EnableTracking<string, string>(MakeOptions());

        using var producer = Built(() => new ProducerBuilder<string, byte[]>(ProducerConfig()).Build());
        using var consumer = Built(() => new ConsumerBuilder<Null, string>(ConsumerConfig()).Build());

        AssertConfluentClient(producer, "Producer`2", typeof(string), typeof(byte[]));
        AssertConfluentClient(consumer, "Consumer`2", typeof(Null), typeof(string));
        producer.Produce("t135", new Message<string, byte[]> { Key = "k", Value = [1] });
        consumer.Subscribe("t135");
    }

    [Fact]
    public void Two_enabled_pairs_of_one_shape_each_build_their_own_tracked_client()
    {
        KafkaBuildInterceptor.EnableTracking<string, string>(MakeOptions());
        KafkaBuildInterceptor.EnableTracking<string, byte[]>(MakeOptions());

        using var strings = Built(() => new ProducerBuilder<string, string>(ProducerConfig()).Build());
        using var bytes = Built(() => new ProducerBuilder<string, byte[]>(ProducerConfig()).Build());
        using var stringConsumer = Built(() => new ConsumerBuilder<string, string>(ConsumerConfig()).Build());
        using var bytesConsumer = Built(() => new ConsumerBuilder<string, byte[]>(ConsumerConfig()).Build());

        Assert.IsType<TrackingKafkaProducer<string, string>>(strings);
        Assert.IsType<TrackingKafkaProducer<string, byte[]>>(bytes);
        Assert.IsType<TrackingKafkaConsumer<string, string>>(stringConsumer);
        Assert.IsType<TrackingKafkaConsumer<string, byte[]>>(bytesConsumer);
    }

    [Fact]
    public void Reset_leaves_the_patch_in_place_and_every_pair_builds_its_own_untracked_client_until_enabled_again()
    {
        KafkaBuildInterceptor.EnableProducerTracking<string, string>(MakeOptions());
        KafkaBuildInterceptor.Reset();

        // The patch stays for the process: removing it cannot restore the code the other pairs share.
        Assert.Equal(1, InterceptorPrefixes(typeof(ProducerBuilder<object, object>)));
        using (var strings = Built(() => new ProducerBuilder<string, string>(ProducerConfig()).Build()))
            AssertConfluentClient(strings, "Producer`2", typeof(string), typeof(string));
        using (var nullKeyed = Built(() => new ProducerBuilder<Null, string>(ProducerConfig()).Build()))
        {
            AssertConfluentClient(nullKeyed, "Producer`2", typeof(Null), typeof(string));
            nullKeyed.Produce("t135", new Message<Null, string> { Value = "v" });
        }

        KafkaBuildInterceptor.EnableProducerTracking<string, string>(MakeOptions());
        using var again = Built(() => new ProducerBuilder<string, string>(ProducerConfig()).Build());

        Assert.IsType<TrackingKafkaProducer<string, string>>(again);
    }

    [Fact]
    public void Disabling_one_pair_leaves_its_builder_correctly_typed_and_another_enabled_pair_tracked()
    {
        KafkaBuildInterceptor.EnableProducerTracking<string, string>(MakeOptions());
        KafkaBuildInterceptor.EnableProducerTracking<Null, string>(MakeOptions());
        KafkaBuildInterceptor.DisableProducerTracking<string, string>();

        using var strings = Built(() => new ProducerBuilder<string, string>(ProducerConfig()).Build());
        using var nullKeyed = Built(() => new ProducerBuilder<Null, string>(ProducerConfig()).Build());

        AssertConfluentClient(strings, "Producer`2", typeof(string), typeof(string));
        strings.Produce("t135", new Message<string, string> { Key = "k", Value = "v" });
        Assert.IsType<TrackingKafkaProducer<Null, string>>(nullKeyed);
    }

    [Fact]
    public void A_value_type_pair_enabled_alone_is_tracked_and_leaves_reference_type_pairs_alone()
    {
        KafkaBuildInterceptor.EnableProducerTracking<int, string>(MakeOptions());

        using var ints = Built(() => new ProducerBuilder<int, string>(ProducerConfig()).Build());
        using var strings = Built(() => new ProducerBuilder<string, string>(ProducerConfig()).Build());

        Assert.IsType<TrackingKafkaProducer<int, string>>(ints);
        AssertConfluentClient(strings, "Producer`2", typeof(string), typeof(string));
        strings.Produce("t135", new Message<string, string> { Key = "k", Value = "v" });
    }

    // ─── Builder subclasses (the pair comes from the nearest generic base type) ─────────

    private sealed class OrdersBuilder(ProducerConfig config) : ProducerBuilder<Null, string>(config);

    private sealed class DelegatingBuilder<TKey, TValue>(ProducerConfig config) : ProducerBuilder<TKey, TValue>(config)
    {
        public override IProducer<TKey, TValue> Build() => base.Build();
    }

    [Fact]
    public void A_builder_subclass_returns_one_wrapper_of_its_own_pair_when_that_pair_is_enabled()
    {
        KafkaBuildInterceptor.EnableProducerTracking<Null, string>(MakeOptions());

        using var orders = Built(() => new OrdersBuilder(ProducerConfig()).Build());
        using var delegating = Built(() => new DelegatingBuilder<Null, string>(ProducerConfig()).Build());

        Assert.IsType<TrackingKafkaProducer<Null, string>>(orders);
        Assert.IsType<TrackingKafkaProducer<Null, string>>(delegating);
    }

    [Fact]
    public void A_builder_subclass_of_a_pair_that_is_not_enabled_builds_its_own_producer_beside_an_enabled_pair()
    {
        KafkaBuildInterceptor.EnableProducerTracking<string, string>(MakeOptions());

        using var orders = Built(() => new OrdersBuilder(ProducerConfig()).Build());
        using var delegating = Built(() => new DelegatingBuilder<Null, string>(ProducerConfig()).Build());

        AssertConfluentClient(orders, "Producer`2", typeof(Null), typeof(string));
        AssertConfluentClient(delegating, "Producer`2", typeof(Null), typeof(string));
        orders.Produce("t135", new Message<Null, string> { Value = "v" });
    }

    // ─── F18: pairs that share a value-type argument share code too ─────────

    [Fact]
    public void Pairs_sharing_an_enabled_pairs_value_type_key_build_their_own_clients()
    {
        KafkaBuildInterceptor.EnableTracking<int, string>(MakeOptions());

        using var bytes = Built(() => new ProducerBuilder<int, byte[]>(ProducerConfig()).Build());
        using var nulls = Built(() => new ProducerBuilder<int, Null>(ProducerConfig()).Build());
        using var consumer = Built(() => new ConsumerBuilder<int, byte[]>(ConsumerConfig()).Build());
        using var tracked = Built(() => new ProducerBuilder<int, string>(ProducerConfig()).Build());

        AssertConfluentClient(bytes, "Producer`2", typeof(int), typeof(byte[]));
        AssertConfluentClient(nulls, "Producer`2", typeof(int), typeof(Null));
        AssertConfluentClient(consumer, "Consumer`2", typeof(int), typeof(byte[]));
        Assert.IsType<TrackingKafkaProducer<int, string>>(tracked);
        bytes.Produce("t135", new Message<int, byte[]> { Key = 1, Value = [1] });
        nulls.Produce("t135", new Message<int, Null> { Key = 1 });
        consumer.Subscribe("t135");
    }

    [Fact]
    public void Pairs_sharing_an_enabled_pairs_value_type_value_build_their_own_clients()
    {
        KafkaBuildInterceptor.EnableProducerTracking<string, int>(MakeOptions());

        using var nullKeyed = Built(() => new ProducerBuilder<Null, int>(ProducerConfig()).Build());
        using var bytesKeyed = Built(() => new ProducerBuilder<byte[], int>(ProducerConfig()).Build());

        AssertConfluentClient(nullKeyed, "Producer`2", typeof(Null), typeof(int));
        AssertConfluentClient(bytesKeyed, "Producer`2", typeof(byte[]), typeof(int));
        nullKeyed.Produce("t135", new Message<Null, int> { Value = 1 });
        bytesKeyed.Produce("t135", new Message<byte[], int> { Key = [1], Value = 1 });
    }

    [Fact]
    public void A_generic_value_type_key_shares_by_its_own_arguments_and_builds_its_own_client()
    {
        KafkaBuildInterceptor.EnableProducerTracking<KeyValuePair<string, string>, string>(MakeOptions());

        using var uris = Built(() => new ProducerBuilder<KeyValuePair<Uri, Uri>, string>(ProducerConfig())
            .SetKeySerializer(new ToStringSerializer<KeyValuePair<Uri, Uri>>())
            .Build());
        using var tracked = Built(() => new ProducerBuilder<KeyValuePair<string, string>, string>(ProducerConfig())
            .SetKeySerializer(new ToStringSerializer<KeyValuePair<string, string>>())
            .Build());

        AssertConfluentClient(uris, "Producer`2", typeof(KeyValuePair<Uri, Uri>), typeof(string));
        Assert.IsType<TrackingKafkaProducer<KeyValuePair<string, string>, string>>(tracked);
        uris.Produce("t135", new Message<KeyValuePair<Uri, Uri>, string>
        {
            Key = new(new Uri("http://a"), new Uri("http://b")),
            Value = "v",
        });
    }

    [Fact]
    public void Two_enabled_pairs_whose_generic_value_type_keys_share_a_shape_share_one_patch_and_each_is_tracked()
    {
        KafkaBuildInterceptor.EnableProducerTracking<KeyValuePair<string, string>, string>(MakeOptions());
        KafkaBuildInterceptor.EnableProducerTracking<KeyValuePair<Uri, Uri>, string>(MakeOptions());

        using var strings = Built(() => new ProducerBuilder<KeyValuePair<string, string>, string>(ProducerConfig())
            .SetKeySerializer(new ToStringSerializer<KeyValuePair<string, string>>())
            .Build());
        using var uris = Built(() => new ProducerBuilder<KeyValuePair<Uri, Uri>, string>(ProducerConfig())
            .SetKeySerializer(new ToStringSerializer<KeyValuePair<Uri, Uri>>())
            .Build());

        Assert.IsType<TrackingKafkaProducer<KeyValuePair<string, string>, string>>(strings);
        Assert.IsType<TrackingKafkaProducer<KeyValuePair<Uri, Uri>, string>>(uris);
        Assert.Equal(1, InterceptorPrefixes(typeof(ProducerBuilder<KeyValuePair<object, object>, object>)));
    }

    [Fact]
    public void An_enabled_pair_whose_shape_no_other_patch_covers_is_tracked()
    {
        KafkaBuildInterceptor.EnableProducerTracking<int, string>(MakeOptions());
        KafkaBuildInterceptor.EnableProducerTracking<long, string>(MakeOptions());

        using var longs = Built(() => new ProducerBuilder<long, string>(ProducerConfig()).Build());

        Assert.IsType<TrackingKafkaProducer<long, string>>(longs);
    }

    // ─── T37: Confluent's refusals pass through the patched Build() unchanged ─────────

    public static TheoryData<string, string, Type> Refusals => new()
    {
        { "no.such.property", "x", typeof(InvalidOperationException) },
        { "acks", "banana", typeof(ArgumentException) },
        { "delivery.report.only.error", "true", typeof(ArgumentException) },
        { "dotnet.producer.enable.delivery.reports", "maybe", typeof(FormatException) },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void A_configuration_Confluent_refuses_throws_from_the_patched_Build_what_Build_throws(string property, string value, Type expected)
    {
        KafkaBuildInterceptor.EnableProducerTracking<string, string>(MakeOptions());
        var config = ProducerConfig();
        config.Set(property, value);

        var unpatched = ThrownByConfluentsConstructor(new ProducerBuilder<string, string>(config));
        var thrown = Record.Exception(() => new ProducerBuilder<string, string>(config).Build());

        Assert.NotNull(unpatched);
        Assert.IsType(expected, unpatched);
        Assert.NotNull(thrown);
        Assert.IsType(unpatched.GetType(), thrown);
        Assert.Equal(unpatched.Message, thrown.Message);
    }

    [Fact]
    public void A_key_type_with_no_serializer_throws_from_the_patched_Build_what_Build_throws()
    {
        KafkaBuildInterceptor.EnableProducerTracking<string, string>(MakeOptions());

        var unpatched = ThrownByConfluentsConstructor(new ProducerBuilder<Uri, string>(ProducerConfig()));
        var thrown = Record.Exception(() => new ProducerBuilder<Uri, string>(ProducerConfig()).Build());

        Assert.IsType<ArgumentNullException>(unpatched);
        Assert.NotNull(thrown);
        Assert.IsType<ArgumentNullException>(thrown);
        Assert.Equal(unpatched.Message, thrown.Message);
    }

    // What Confluent's own Build() throws: its body is `newobj Producer<K,V>(ProducerBuilder<K,V>)` and nothing else.
    private static Exception? ThrownByConfluentsConstructor<TKey, TValue>(ProducerBuilder<TKey, TValue> builder)
    {
        var producerType = typeof(IProducer<,>).Assembly.GetType("Confluent.Kafka.Producer`2", throwOnError: true)!
            .MakeGenericType(typeof(TKey), typeof(TValue));
        var constructor = producerType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
            [typeof(ProducerBuilder<TKey, TValue>)], null)!;
        try
        {
            ((IDisposable)constructor.Invoke(BindingFlags.DoNotWrapExceptions, null, [builder], null)).Dispose();
            return null;
        }
        catch (Exception e)
        {
            return e;
        }
    }

    // ─── F23: hosts that enable at once ─────────

    [Fact]
    public void Ten_threads_enabling_pairs_of_one_new_shape_at_once_each_track_their_first_build()
    {
        Func<bool>[] hosts =
        [
            EnableAndBuildTracked<RaceKeyA, string>, EnableAndBuildTracked<RaceKeyA, byte[]>,
            EnableAndBuildTracked<RaceKeyA, Null>, EnableAndBuildTracked<RaceKeyA, Uri>,
            EnableAndBuildTracked<RaceKeyA, Version>, EnableAndBuildTracked<RaceKeyA, int[]>,
            EnableAndBuildTracked<RaceKeyA, long[]>, EnableAndBuildTracked<RaceKeyA, List<int>>,
            EnableAndBuildTracked<RaceKeyA, object>, EnableAndBuildTracked<RaceKeyA, Exception>,
        ];

        var tracked = RunAtOnce(hosts);

        Assert.Equal(hosts.Length, tracked.Count(t => t));
        Assert.Equal(1, InterceptorPrefixes(typeof(ProducerBuilder<RaceKeyA, object>)));
    }

    [Fact]
    public void Ten_hosts_enabling_one_pair_at_once_each_track_their_first_build()
    {
        var hosts = Enumerable.Repeat<Func<bool>>(EnableAndBuildTracked<RaceKeyB, string>, 10).ToArray();

        var tracked = RunAtOnce(hosts);

        Assert.Equal(hosts.Length, tracked.Count(t => t));
        Assert.Equal(1, InterceptorPrefixes(typeof(ProducerBuilder<RaceKeyB, object>)));
    }

    // One host: enable its pair with options of its own, then build its producer once, as a host does at startup.
    private bool EnableAndBuildTracked<TKey, TValue>()
    {
        KafkaBuildInterceptor.EnableProducerTracking<TKey, TValue>(MakeOptions());
        using var producer = Built(() => new ProducerBuilder<TKey, TValue>(ProducerConfig())
            .SetKeySerializer(new ToStringSerializer<TKey>())
            .SetValueSerializer(new ToStringSerializer<TValue>())
            .Build());
        return producer is TrackingKafkaProducer<TKey, TValue>;
    }

    private static bool[] RunAtOnce(Func<bool>[] hosts)
    {
        using var start = new Barrier(hosts.Length);
        var results = new bool[hosts.Length];
        var errors = new Exception?[hosts.Length];
        var threads = hosts.Select((host, i) => new Thread(() =>
        {
            // An exception left on a thread of its own ends the test process instead of failing the fact.
            try
            {
                start.SignalAndWait();
                results[i] = host();
            }
            catch (Exception exception)
            {
                errors[i] = exception;
            }
        })).ToArray();
        foreach (var thread in threads) thread.Start();
        foreach (var thread in threads) Assert.True(thread.Join(TimeSpan.FromMinutes(2)), "a host was still enabling after two minutes");
        Assert.All(errors, Assert.Null);
        return results;
    }

    // ─── F19: one tracker per enabled pair, registered when it is used ─────────

    // A flush invokes the tracker without a broker.
    private KafkaTrackingOptions FlushTrackingOptions()
    {
        var options = MakeOptions();
        options.TrackFlush = true;
        return options;
    }

    [Fact]
    public void Enabling_a_pair_twice_leaves_no_tracker_the_run_would_call_unused()
    {
        TrackingComponentRegistry.Clear();
        var options = FlushTrackingOptions();
        KafkaBuildInterceptor.EnableProducerTracking<string, string>(options);
        KafkaBuildInterceptor.EnableProducerTracking<string, string>(options);
        KafkaBuildInterceptor.EnableProducerTracking<string, string>(FlushTrackingOptions());

        using (var producer = Built(() => new ProducerBuilder<string, string>(ProducerConfig()).Build()))
            producer.Flush(TimeSpan.FromMilliseconds(1));

        var kafkaTrackers = TrackingComponentRegistry.GetRegisteredComponents().OfType<KafkaTracker>().ToArray();
        Assert.Single(kafkaTrackers);
        Assert.Empty(TrackingComponentRegistry.GetUnusedComponents().OfType<KafkaTracker>());
        Assert.Equal("KafkaTracker (Kafka, producer <String,String>)", kafkaTrackers[0].ComponentName);
    }

    [Fact]
    public void Enabling_both_directions_in_an_app_that_only_produces_leaves_no_unused_consumer_tracker()
    {
        TrackingComponentRegistry.Clear();
        KafkaBuildInterceptor.EnableTracking<string, string>(FlushTrackingOptions());

        using (var producer = Built(() => new ProducerBuilder<string, string>(ProducerConfig()).Build()))
            producer.Flush(TimeSpan.FromMilliseconds(1));

        Assert.Empty(TrackingComponentRegistry.GetUnusedComponents().OfType<KafkaTracker>());
        Assert.Equal(new[] { "KafkaTracker (Kafka, producer <String,String>)" },
            TrackingComponentRegistry.GetRegisteredComponents().OfType<KafkaTracker>().Select(t => t.ComponentName));
    }

    [Fact]
    public void A_consumer_tracker_is_named_for_its_direction_and_pair()
    {
        TrackingComponentRegistry.Clear();
        KafkaBuildInterceptor.EnableConsumerTracking<Null, byte[]>(MakeOptions());

        using var consumer = Built(() => new ConsumerBuilder<Null, byte[]>(ConsumerConfig()).Build());

        Assert.Equal(new[] { "KafkaTracker (Kafka, consumer <Null,Byte[]>)" },
            TrackingComponentRegistry.GetRegisteredComponents().OfType<KafkaTracker>().Select(t => t.ComponentName));
    }

    // ─── Q3: Confluent without the internal constructors ─────────

    [Fact]
    public void Without_Confluents_internal_constructor_enabling_throws_and_patches_nothing()
    {
        var lookup = KafkaBuildInterceptor.ConstructorLookup;
        KafkaBuildInterceptor.ConstructorLookup = (_, _) => null;
        try
        {
            var thrown = Record.Exception(() => KafkaBuildInterceptor.EnableProducerTracking<MissingConstructorKey, string>(MakeOptions()));

            Assert.IsType<NotSupportedException>(thrown);
            Assert.Contains("Confluent.Kafka", thrown.Message);
            Assert.Contains(".BuildTracked()", thrown.Message);
        }
        finally
        {
            KafkaBuildInterceptor.ConstructorLookup = lookup;
        }

        Assert.Equal(0, InterceptorPrefixes(typeof(ProducerBuilder<MissingConstructorKey, object>)));
        using var producer = new ProducerBuilder<MissingConstructorKey, string>(ProducerConfig())
            .SetKeySerializer(new ToStringSerializer<MissingConstructorKey>())
            .BuildTracked();
        AssertConfluentClient(producer, "Producer`2", typeof(MissingConstructorKey), typeof(string));
    }
}
