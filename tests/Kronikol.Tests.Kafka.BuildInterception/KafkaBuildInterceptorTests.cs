using Confluent.Kafka;
using Kronikol.Extensions.Kafka;
using Kronikol.Extensions.Kafka.BuildInterception;
using Kronikol.Tracking;
using static Kronikol.Tests.Kafka.BuildInterception.BuildInterceptionTestSupport;

namespace Kronikol.Tests.Kafka.BuildInterception;

public class KafkaBuildInterceptorTests : IDisposable
{
    private readonly string _testId = Guid.NewGuid().ToString();

    public void Dispose() => KafkaBuildInterceptor.Reset();

    private KafkaTrackingOptions MakeOptions() => new()
    {
        ServiceName = "Kafka",
        CallerName = "TestCaller",
        CurrentTestInfoFetcher = () => ("BuildInterception Test", _testId),
    };

    private static ConsumerConfig MakeConsumerConfig() => new()
    {
        GroupId = "test-group",
        BootstrapServers = "localhost:9092",
    };

    private static ProducerConfig MakeProducerConfig() => new()
    {
        BootstrapServers = "localhost:9092",
    };

    private RequestResponseLog[] GetLogsFromThisTest()
    {
        return RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == _testId)
            .ToArray();
    }

    // ─── Consumer: Build() interception ──────────────────────

    [Fact]
    public void ConsumerBuilder_Build_Returns_TrackingConsumer_When_Enabled()
    {
        KafkaBuildInterceptor.EnableConsumerTracking<string, string>(MakeOptions());

        using var consumer = Built(() => new ConsumerBuilder<string, string>(MakeConsumerConfig()).Build());

        Assert.IsType<TrackingKafkaConsumer<string, string>>(consumer);
    }

    [Fact]
    public void ConsumerBuilder_Build_Returns_Original_When_Not_Enabled()
    {
        using var consumer = Built(() => new ConsumerBuilder<string, string>(MakeConsumerConfig()).Build());

        Assert.IsNotType<TrackingKafkaConsumer<string, string>>(consumer);
    }

    // ─── Producer: Build() interception ──────────────────────

    [Fact]
    public void ProducerBuilder_Build_Returns_TrackingProducer_When_Enabled()
    {
        KafkaBuildInterceptor.EnableProducerTracking<string, string>(MakeOptions());

        using var producer = Built(() => new ProducerBuilder<string, string>(MakeProducerConfig()).Build());

        Assert.IsType<TrackingKafkaProducer<string, string>>(producer);
    }

    [Fact]
    public void ProducerBuilder_Build_Returns_Original_When_Not_Enabled()
    {
        using var producer = Built(() => new ProducerBuilder<string, string>(MakeProducerConfig()).Build());

        Assert.IsNotType<TrackingKafkaProducer<string, string>>(producer);
    }

    // ─── EnableTracking: convenience method ──────────────────

    [Fact]
    public void EnableTracking_Patches_Both_Consumer_And_Producer()
    {
        KafkaBuildInterceptor.EnableTracking<string, string>(MakeOptions());

        using var consumer = Built(() => new ConsumerBuilder<string, string>(MakeConsumerConfig()).Build());
        using var producer = Built(() => new ProducerBuilder<string, string>(MakeProducerConfig()).Build());

        Assert.IsType<TrackingKafkaConsumer<string, string>>(consumer);
        Assert.IsType<TrackingKafkaProducer<string, string>>(producer);
    }

    // ─── Action<> overload ──────────────────────────────────

    [Fact]
    public void EnableTracking_Action_Overload_Works()
    {
        KafkaBuildInterceptor.EnableTracking<string, string>(opts =>
        {
            opts.ServiceName = "Kafka";
            opts.CallerName = "TestCaller";
            opts.CurrentTestInfoFetcher = () => ("BuildInterception Test", _testId);
        });

        using var consumer = Built(() => new ConsumerBuilder<string, string>(MakeConsumerConfig()).Build());

        Assert.IsType<TrackingKafkaConsumer<string, string>>(consumer);
    }

    // ─── Disable / Reset ────────────────────────────────────

    [Fact]
    public void DisableConsumerTracking_Stops_Wrapping()
    {
        KafkaBuildInterceptor.EnableConsumerTracking<string, string>(MakeOptions());
        KafkaBuildInterceptor.DisableConsumerTracking<string, string>();

        using var consumer = Built(() => new ConsumerBuilder<string, string>(MakeConsumerConfig()).Build());

        Assert.IsNotType<TrackingKafkaConsumer<string, string>>(consumer);
    }

    [Fact]
    public void DisableProducerTracking_Stops_Wrapping()
    {
        KafkaBuildInterceptor.EnableProducerTracking<string, string>(MakeOptions());
        KafkaBuildInterceptor.DisableProducerTracking<string, string>();

        using var producer = Built(() => new ProducerBuilder<string, string>(MakeProducerConfig()).Build());

        Assert.IsNotType<TrackingKafkaProducer<string, string>>(producer);
    }

    [Fact]
    public void Reset_Stops_All_Wrapping()
    {
        KafkaBuildInterceptor.EnableTracking<string, string>(MakeOptions());
        KafkaBuildInterceptor.Reset();

        using var consumer = Built(() => new ConsumerBuilder<string, string>(MakeConsumerConfig()).Build());
        using var producer = Built(() => new ProducerBuilder<string, string>(MakeProducerConfig()).Build());

        Assert.IsNotType<TrackingKafkaConsumer<string, string>>(consumer);
        Assert.IsNotType<TrackingKafkaProducer<string, string>>(producer);
    }

    // ─── Idempotency ────────────────────────────────────────

    [Fact]
    public void Double_Enable_Is_Idempotent()
    {
        KafkaBuildInterceptor.EnableConsumerTracking<string, string>(MakeOptions());
        KafkaBuildInterceptor.EnableConsumerTracking<string, string>(MakeOptions());

        using var consumer = Built(() => new ConsumerBuilder<string, string>(MakeConsumerConfig()).Build());

        Assert.IsType<TrackingKafkaConsumer<string, string>>(consumer);
    }

    // ─── Type independence ──────────────────────────────────

    [Fact]
    public void Different_Type_Pairs_Are_Independent()
    {
        KafkaBuildInterceptor.EnableConsumerTracking<string, string>(MakeOptions());

        using var stringConsumer = Built(() => new ConsumerBuilder<string, string>(MakeConsumerConfig()).Build());
        using var intConsumer = Built(() => new ConsumerBuilder<int, string>(MakeConsumerConfig())
            .SetKeyDeserializer(Deserializers.Int32)
            .Build());

        Assert.IsType<TrackingKafkaConsumer<string, string>>(stringConsumer);
        Assert.IsNotType<TrackingKafkaConsumer<int, string>>(intConsumer);
    }

    // ─── Compatibility with BuildTracked() ──────────────────

    [Fact]
    public void BuildTracked_Does_Not_Double_Wrap_When_Harmony_Patch_Active()
    {
        KafkaBuildInterceptor.EnableConsumerTracking<string, string>(MakeOptions());

        using var consumer = new ConsumerBuilder<string, string>(MakeConsumerConfig()).BuildTracked();

        Assert.IsType<TrackingKafkaConsumer<string, string>>(consumer);
    }

    // ─── End-to-end tracking ────────────────────────────────

    [Fact]
    public void Subscribe_Is_Tracked_Through_Patched_Build()
    {
        var options = MakeOptions();
        options.TrackSubscribe = true;
        KafkaBuildInterceptor.EnableConsumerTracking<string, string>(options);

        using var consumer = Built(() => new ConsumerBuilder<string, string>(MakeConsumerConfig()).Build());
        consumer.Subscribe("test-topic");

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
    }

    // ─── Re-enable after disable ────────────────────────────

    [Fact]
    public void Enable_After_Disable_Restores_Tracking()
    {
        KafkaBuildInterceptor.EnableConsumerTracking<string, string>(MakeOptions());
        KafkaBuildInterceptor.DisableConsumerTracking<string, string>();
        KafkaBuildInterceptor.EnableConsumerTracking<string, string>(MakeOptions());

        using var consumer = Built(() => new ConsumerBuilder<string, string>(MakeConsumerConfig()).Build());

        Assert.IsType<TrackingKafkaConsumer<string, string>>(consumer);
    }

    // ─── Enable after Reset restores tracking ───────────────

    [Fact]
    public void Enable_After_Reset_Restores_Tracking()
    {
        KafkaBuildInterceptor.EnableConsumerTracking<string, string>(MakeOptions());
        KafkaBuildInterceptor.Reset();
        KafkaBuildInterceptor.EnableConsumerTracking<string, string>(MakeOptions());

        using var consumer = Built(() => new ConsumerBuilder<string, string>(MakeConsumerConfig()).Build());

        Assert.IsType<TrackingKafkaConsumer<string, string>>(consumer);
    }

    // ─── Consumer and producer disable independently ─────────

    [Fact]
    public void DisableConsumer_Does_Not_Affect_Producer()
    {
        KafkaBuildInterceptor.EnableTracking<string, string>(MakeOptions());
        KafkaBuildInterceptor.DisableConsumerTracking<string, string>();

        using var consumer = Built(() => new ConsumerBuilder<string, string>(MakeConsumerConfig()).Build());
        using var producer = Built(() => new ProducerBuilder<string, string>(MakeProducerConfig()).Build());

        Assert.IsNotType<TrackingKafkaConsumer<string, string>>(consumer);
        Assert.IsType<TrackingKafkaProducer<string, string>>(producer);
    }

    [Fact]
    public void DisableProducer_Does_Not_Affect_Consumer()
    {
        KafkaBuildInterceptor.EnableTracking<string, string>(MakeOptions());
        KafkaBuildInterceptor.DisableProducerTracking<string, string>();

        using var consumer = Built(() => new ConsumerBuilder<string, string>(MakeConsumerConfig()).Build());
        using var producer = Built(() => new ProducerBuilder<string, string>(MakeProducerConfig()).Build());

        Assert.IsType<TrackingKafkaConsumer<string, string>>(consumer);
        Assert.IsNotType<TrackingKafkaProducer<string, string>>(producer);
    }
}
