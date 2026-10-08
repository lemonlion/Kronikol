using Confluent.Kafka;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Kronikol.Constants;
using Kronikol.Tests.Tracking;
using Kronikol.Tracking;

namespace Kronikol.Extensions.Kafka.Tests;

// Reads the process-wide TrackingComponentRegistry, which KafkaTrackerTests clears.
[Collection("TrackingComponentRegistry")]
public class KafkaServiceCollectionExtensionsTests
{
    [Fact]
    public void AddKafkaProducerTestTracking_decorates_registered_producer()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IProducer<string, string>>(new FakeProducer<string, string>());

        services.AddKafkaProducerTestTracking<string, string>(options =>
        {
            options.ServiceName = "TestKafka";
        });

        var provider = services.BuildServiceProvider();
        var producer = provider.GetRequiredService<IProducer<string, string>>();

        Assert.IsType<TrackingKafkaProducer<string, string>>(producer);
    }

    [Fact]
    public void AddKafkaProducerTestTracking_preserves_service_lifetime()
    {
        var services = new ServiceCollection();
        services.AddScoped<IProducer<string, string>>(_ => new FakeProducer<string, string>());

        services.AddKafkaProducerTestTracking<string, string>();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IProducer<string, string>));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void AddKafkaProducerTestTracking_does_not_duplicate_registrations()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IProducer<string, string>>(new FakeProducer<string, string>());

        services.AddKafkaProducerTestTracking<string, string>();

        Assert.Single(services, d => d.ServiceType == typeof(IProducer<string, string>));
    }

    [Fact]
    public void AddKafkaProducerTestTracking_is_noop_when_no_producer_registered()
    {
        var services = new ServiceCollection();

        services.AddKafkaProducerTestTracking<string, string>();

        Assert.Empty(services);
    }

    [Fact]
    public void AddKafkaProducerTestTracking_applies_options_configuration()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IProducer<string, string>>(new FakeProducer<string, string>());

        services.AddKafkaProducerTestTracking<string, string>(options =>
        {
            options.ServiceName = "CustomKafka";
            options.CallerName = "MySvc";
            options.Verbosity = KafkaTrackingVerbosity.Summarised;
        });

        var provider = services.BuildServiceProvider();
        var producer = provider.GetRequiredService<IProducer<string, string>>();

        // Just verify it resolves without error — options are applied internally
        Assert.IsType<TrackingKafkaProducer<string, string>>(producer);
    }

    [Fact]
    public void AddKafkaConsumerTestTracking_decorates_registered_consumer()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConsumer<string, string>>(new FakeConsumer<string, string>());

        services.AddKafkaConsumerTestTracking<string, string>(options =>
        {
            options.ServiceName = "TestKafka";
        });

        var provider = services.BuildServiceProvider();
        var consumer = provider.GetRequiredService<IConsumer<string, string>>();

        Assert.IsType<TrackingKafkaConsumer<string, string>>(consumer);
    }

    [Fact]
    public void AddKafkaConsumerTestTracking_preserves_service_lifetime()
    {
        var services = new ServiceCollection();
        services.AddScoped<IConsumer<string, string>>(_ => new FakeConsumer<string, string>());

        services.AddKafkaConsumerTestTracking<string, string>();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IConsumer<string, string>));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void AddKafkaConsumerTestTracking_is_noop_when_no_consumer_registered()
    {
        var services = new ServiceCollection();

        services.AddKafkaConsumerTestTracking<string, string>();

        Assert.Empty(services);
    }

    // ─── Consumer Factory DI ────────────────────────────────

    [Fact]
    public void AddKafkaConsumerFactoryTestTracking_decorates_registered_factory()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IKafkaConsumerFactory<string, string>>(new FakeConsumerFactory<string, string>());

        services.AddKafkaConsumerFactoryTestTracking<string, string>(options =>
        {
            options.ServiceName = "TestKafka";
        });

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IKafkaConsumerFactory<string, string>>();

        Assert.IsType<TrackingKafkaConsumerFactory<string, string>>(factory);
    }

    [Fact]
    public void AddKafkaConsumerFactoryTestTracking_registers_default_when_none_exists()
    {
        var services = new ServiceCollection();

        services.AddKafkaConsumerFactoryTestTracking<string, string>();

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IKafkaConsumerFactory<string, string>>();

        Assert.IsType<TrackingKafkaConsumerFactory<string, string>>(factory);
    }

    [Fact]
    public void AddKafkaConsumerFactoryTestTracking_created_consumer_is_tracked()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IKafkaConsumerFactory<string, string>>(new FakeConsumerFactory<string, string>());

        services.AddKafkaConsumerFactoryTestTracking<string, string>(options =>
        {
            options.ServiceName = "TestKafka";
        });

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IKafkaConsumerFactory<string, string>>();
        var consumer = factory.Create(config => { });

        Assert.IsType<TrackingKafkaConsumer<string, string>>(consumer);
    }

    // ─── Producer Factory DI ────────────────────────────────

    [Fact]
    public void AddKafkaProducerFactoryTestTracking_decorates_registered_factory()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IKafkaProducerFactory<string, string>>(new FakeProducerFactory<string, string>());

        services.AddKafkaProducerFactoryTestTracking<string, string>(options =>
        {
            options.ServiceName = "TestKafka";
        });

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IKafkaProducerFactory<string, string>>();

        Assert.IsType<TrackingKafkaProducerFactory<string, string>>(factory);
    }

    [Fact]
    public void AddKafkaProducerFactoryTestTracking_registers_default_when_none_exists()
    {
        var services = new ServiceCollection();

        services.AddKafkaProducerFactoryTestTracking<string, string>();

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IKafkaProducerFactory<string, string>>();

        Assert.IsType<TrackingKafkaProducerFactory<string, string>>(factory);
    }

    [Fact]
    public void AddKafkaProducerFactoryTestTracking_created_producer_is_tracked()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IKafkaProducerFactory<string, string>>(new FakeProducerFactory<string, string>());

        services.AddKafkaProducerFactoryTestTracking<string, string>(options =>
        {
            options.ServiceName = "TestKafka";
        });

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IKafkaProducerFactory<string, string>>();
        var producer = factory.Create(config => { });

        Assert.IsType<TrackingKafkaProducer<string, string>>(producer);
    }

    #region Test Doubles

    // ─── HttpContextAccessor: the options' first, then the container's ───

    public static TheoryData<string> Registrations => ["producer", "consumer", "consumer factory", "producer factory"];

    [Theory]
    [MemberData(nameof(Registrations))]
    public void Registration_prefers_the_options_accessor_to_the_containers(string registration)
    {
        var optionsTestId = Guid.NewGuid().ToString();
        var containerTestId = Guid.NewGuid().ToString();

        var tracker = TrackerBuiltBy(registration, RequestHeaderAccessor.For("Container Test", containerTestId),
            RequestHeaderAccessor.For("Options Test", optionsTestId));
        tracker.LogProduce(new KafkaOperationInfo(KafkaOperation.ProduceAsync, "orders"), "v");

        Assert.Contains(RequestResponseLogger.RequestAndResponseLogs,
            l => l.TestId == optionsTestId && l.AttributionSource == AttributionSource.RequestHeader);
        Assert.DoesNotContain(RequestResponseLogger.RequestAndResponseLogs, l => l.TestId == containerTestId);
    }

    [Theory]
    [MemberData(nameof(Registrations))]
    public void Registration_uses_the_containers_accessor_when_the_options_carry_none(string registration)
    {
        var containerTestId = Guid.NewGuid().ToString();

        var tracker = TrackerBuiltBy(registration, RequestHeaderAccessor.For("Container Test", containerTestId), null);
        tracker.LogProduce(new KafkaOperationInfo(KafkaOperation.ProduceAsync, "orders"), "v");

        Assert.Contains(RequestResponseLogger.RequestAndResponseLogs,
            l => l.TestId == containerTestId && l.AttributionSource == AttributionSource.RequestHeader);
    }

    // Registers the service the registration decorates, resolves it so that the decorator builds its tracker, and
    // returns that tracker from the registry by its unique service name.
    private static KafkaTracker TrackerBuiltBy(string registration, IHttpContextAccessor container, IHttpContextAccessor? fromOptions)
    {
        var serviceName = "Kafka " + Guid.NewGuid();
        Action<KafkaTrackingOptions> configure = o =>
        {
            o.ServiceName = serviceName;
            o.HttpContextAccessor = fromOptions;
        };
        var services = new ServiceCollection();
        services.AddSingleton(container);
        switch (registration)
        {
            case "producer":
                services.AddSingleton<IProducer<string, string>>(new FakeProducer<string, string>());
                services.AddKafkaProducerTestTracking<string, string>(configure);
                services.BuildServiceProvider().GetRequiredService<IProducer<string, string>>();
                break;
            case "consumer":
                services.AddSingleton<IConsumer<string, string>>(new FakeConsumer<string, string>());
                services.AddKafkaConsumerTestTracking<string, string>(configure);
                services.BuildServiceProvider().GetRequiredService<IConsumer<string, string>>();
                break;
            case "consumer factory":
                services.AddSingleton<IKafkaConsumerFactory<string, string>>(new FakeConsumerFactory<string, string>());
                services.AddKafkaConsumerFactoryTestTracking<string, string>(configure);
                services.BuildServiceProvider().GetRequiredService<IKafkaConsumerFactory<string, string>>();
                break;
            case "producer factory":
                services.AddSingleton<IKafkaProducerFactory<string, string>>(new FakeProducerFactory<string, string>());
                services.AddKafkaProducerFactoryTestTracking<string, string>(configure);
                services.BuildServiceProvider().GetRequiredService<IKafkaProducerFactory<string, string>>();
                break;
        }
        return TrackingComponentRegistry.GetRegisteredComponents().OfType<KafkaTracker>()
            .Single(t => t.ComponentName == $"KafkaTracker ({serviceName})");
    }

    private class FakeProducer<TKey, TValue> : IProducer<TKey, TValue>
    {
        public Handle Handle => throw new NotImplementedException();
        public string Name => "fake";
        public void SetSaslCredentials(string username, string password) { }
        public void Dispose() { }
        public int AddBrokers(string brokers) => 0;
        public void InitTransactions(TimeSpan timeout) { }
        public void BeginTransaction() { }
        public void CommitTransaction(TimeSpan timeout) { }
        public void CommitTransaction() { }
        public void AbortTransaction(TimeSpan timeout) { }
        public void AbortTransaction() { }
        public void SendOffsetsToTransaction(IEnumerable<TopicPartitionOffset> offsets, IConsumerGroupMetadata groupMetadata, TimeSpan timeout) { }
        public int Flush(TimeSpan timeout) => 0;
        public void Flush(CancellationToken cancellationToken = default) { }
        public int Poll(TimeSpan timeout) => 0;
        public void Produce(string topic, Message<TKey, TValue> message, Action<DeliveryReport<TKey, TValue>>? deliveryHandler = null) { }
        public void Produce(TopicPartition topicPartition, Message<TKey, TValue> message, Action<DeliveryReport<TKey, TValue>>? deliveryHandler = null) { }
        public Task<DeliveryResult<TKey, TValue>> ProduceAsync(string topic, Message<TKey, TValue> message, CancellationToken cancellationToken = default)
            => Task.FromResult(new DeliveryResult<TKey, TValue>());
        public Task<DeliveryResult<TKey, TValue>> ProduceAsync(TopicPartition topicPartition, Message<TKey, TValue> message, CancellationToken cancellationToken = default)
            => Task.FromResult(new DeliveryResult<TKey, TValue>());
    }

    private class FakeConsumer<TKey, TValue> : IConsumer<TKey, TValue>
    {
        public Handle Handle => throw new NotImplementedException();
        public string Name => "fake";
        public string MemberId => "fake";
        public List<TopicPartition> Assignment => [];
        public List<string> Subscription => [];
        public IConsumerGroupMetadata ConsumerGroupMetadata => throw new NotImplementedException();
        public void SetSaslCredentials(string username, string password) { }
        public void Dispose() { }
        public int AddBrokers(string brokers) => 0;
        public ConsumeResult<TKey, TValue> Consume(int millisecondsTimeout) => null!;
        public ConsumeResult<TKey, TValue> Consume(CancellationToken cancellationToken = default) => null!;
        public ConsumeResult<TKey, TValue> Consume(TimeSpan timeout) => null!;
        public void Subscribe(IEnumerable<string> topics) { }
        public void Subscribe(string topic) { }
        public void Unsubscribe() { }
        public void Assign(TopicPartition partition) { }
        public void Assign(TopicPartitionOffset partition) { }
        public void Assign(IEnumerable<TopicPartitionOffset> partitions) { }
        public void Assign(IEnumerable<TopicPartition> partitions) { }
        public void IncrementalAssign(IEnumerable<TopicPartitionOffset> partitions) { }
        public void IncrementalAssign(IEnumerable<TopicPartition> partitions) { }
        public void IncrementalUnassign(IEnumerable<TopicPartition> partitions) { }
        public void Unassign() { }
        public void StoreOffset(ConsumeResult<TKey, TValue> result) { }
        public void StoreOffset(TopicPartitionOffset offset) { }
        public List<TopicPartitionOffset> Commit() => [];
        public void Commit(IEnumerable<TopicPartitionOffset> offsets) { }
        public void Commit(ConsumeResult<TKey, TValue> result) { }
        public void Seek(TopicPartitionOffset tpo) { }
        public void Pause(IEnumerable<TopicPartition> partitions) { }
        public void Resume(IEnumerable<TopicPartition> partitions) { }
        public List<TopicPartitionOffset> Committed(TimeSpan timeout) => [];
        public List<TopicPartitionOffset> Committed(IEnumerable<TopicPartition> partitions, TimeSpan timeout) => [];
        public Offset Position(TopicPartition partition) => Offset.Unset;
        public List<TopicPartitionOffset> OffsetsForTimes(IEnumerable<TopicPartitionTimestamp> timestampsToSearch, TimeSpan timeout) => [];
        public WatermarkOffsets GetWatermarkOffsets(TopicPartition topicPartition) => new(Offset.Unset, Offset.Unset);
        public WatermarkOffsets QueryWatermarkOffsets(TopicPartition topicPartition, TimeSpan timeout) => new(Offset.Unset, Offset.Unset);
        public void Close() { }
    }

    private class FakeConsumerFactory<TKey, TValue> : IKafkaConsumerFactory<TKey, TValue>
    {
        public IConsumer<TKey, TValue> Create(Action<ConsumerConfig> configure)
        {
            var config = new ConsumerConfig();
            configure(config);
            return new FakeConsumer<TKey, TValue>();
        }
    }

    private class FakeProducerFactory<TKey, TValue> : IKafkaProducerFactory<TKey, TValue>
    {
        public IProducer<TKey, TValue> Create(Action<ProducerConfig> configure)
        {
            var config = new ProducerConfig();
            configure(config);
            return new FakeProducer<TKey, TValue>();
        }
    }

    #endregion
}
