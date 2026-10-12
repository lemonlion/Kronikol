using Confluent.Kafka;
using Kronikol.Extensions.Kafka;
using Kronikol.Tracking;

namespace Kronikol.Tests.Kafka;

public class KafkaTrackingInterceptorTests : IDisposable
{
    private readonly string _testId = Guid.NewGuid().ToString();

    public void Dispose() => KafkaTrackingInterceptor.Reset();

    private KafkaTrackingOptions MakeOptions() => new()
    {
        ServiceName = "Kafka",
        CallerName = "TestCaller",
        CurrentTestInfoFetcher = () => ("Interceptor Test", _testId),
    };

    private RequestResponseLog[] GetLogsFromThisTest()
    {
        return RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == _testId)
            .ToArray();
    }

    // ─── Consumer: Tracked() extension ──────────────────────

    [Fact]
    public void Tracked_Consumer_Returns_TrackingKafkaConsumer_When_Enabled()
    {
        KafkaTrackingInterceptor.EnableConsumerTracking<string, string>(MakeOptions());

        var inner = new FakeConsumer<string, string>();
        var result = inner.Tracked();

        Assert.IsType<TrackingKafkaConsumer<string, string>>(result);
    }

    [Fact]
    public void Tracked_Consumer_Returns_Original_When_Not_Enabled()
    {
        var inner = new FakeConsumer<string, string>();
        var result = inner.Tracked();

        Assert.Same(inner, result);
    }

    [Fact]
    public void Tracked_Consumer_Does_Not_Double_Wrap()
    {
        KafkaTrackingInterceptor.EnableConsumerTracking<string, string>(MakeOptions());

        var inner = new FakeConsumer<string, string>();
        var first = inner.Tracked();
        var second = first.Tracked();

        Assert.Same(first, second);
    }

    [Fact]
    public void Tracked_Consumer_Tracks_Consume_Operations()
    {
        KafkaTrackingInterceptor.EnableConsumerTracking<string, string>(MakeOptions());

        var inner = new FakeConsumer<string, string>();
        inner.NextConsumeResult = new ConsumeResult<string, string>
        {
            Topic = "orders-topic",
            Partition = new Partition(0),
            Offset = new Offset(42),
            Message = new Message<string, string> { Key = "k1", Value = "v1" }
        };

        var tracked = inner.Tracked();
        tracked.Consume(TimeSpan.FromSeconds(1));

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length); // request + response
    }

    // ─── Producer: Tracked() extension ──────────────────────

    [Fact]
    public void Tracked_Producer_Returns_TrackingKafkaProducer_When_Enabled()
    {
        KafkaTrackingInterceptor.EnableProducerTracking<string, string>(MakeOptions());

        var inner = new FakeProducer<string, string>();
        var result = inner.Tracked();

        Assert.IsType<TrackingKafkaProducer<string, string>>(result);
    }

    [Fact]
    public void Tracked_Producer_Returns_Original_When_Not_Enabled()
    {
        var inner = new FakeProducer<string, string>();
        var result = inner.Tracked();

        Assert.Same(inner, result);
    }

    [Fact]
    public void Tracked_Producer_Does_Not_Double_Wrap()
    {
        KafkaTrackingInterceptor.EnableProducerTracking<string, string>(MakeOptions());

        var inner = new FakeProducer<string, string>();
        var first = inner.Tracked();
        var second = first.Tracked();

        Assert.Same(first, second);
    }

    // ─── Disable / Reset ────────────────────────────────────

    [Fact]
    public void DisableConsumerTracking_Returns_Original()
    {
        KafkaTrackingInterceptor.EnableConsumerTracking<string, string>(MakeOptions());
        KafkaTrackingInterceptor.DisableConsumerTracking<string, string>();

        var inner = new FakeConsumer<string, string>();
        var result = inner.Tracked();

        Assert.Same(inner, result);
    }

    [Fact]
    public void DisableProducerTracking_Returns_Original()
    {
        KafkaTrackingInterceptor.EnableProducerTracking<string, string>(MakeOptions());
        KafkaTrackingInterceptor.DisableProducerTracking<string, string>();

        var inner = new FakeProducer<string, string>();
        var result = inner.Tracked();

        Assert.Same(inner, result);
    }

    [Fact]
    public void Reset_Clears_All_Tracking_State()
    {
        KafkaTrackingInterceptor.EnableConsumerTracking<string, string>(MakeOptions());
        KafkaTrackingInterceptor.EnableProducerTracking<string, string>(MakeOptions());
        KafkaTrackingInterceptor.Reset();

        var consumer = new FakeConsumer<string, string>();
        var producer = new FakeProducer<string, string>();

        Assert.Same(consumer, consumer.Tracked());
        Assert.Same(producer, producer.Tracked());
    }

    // ─── Multiple type combinations ─────────────────────────

    [Fact]
    public void Different_Type_Combinations_Are_Independent()
    {
        KafkaTrackingInterceptor.EnableConsumerTracking<string, string>(MakeOptions());
        // Do NOT enable for Guid, string

        var stringConsumer = new FakeConsumer<string, string>();
        var guidConsumer = new FakeConsumer<Guid, string>();

        Assert.IsType<TrackingKafkaConsumer<string, string>>(stringConsumer.Tracked());
        Assert.Same(guidConsumer, guidConsumer.Tracked());
    }

    // ─── WrapConsumer / WrapProducer direct API ─────────────

    [Fact]
    public void WrapConsumer_Returns_Tracked_When_Enabled()
    {
        KafkaTrackingInterceptor.EnableConsumerTracking<string, string>(MakeOptions());

        var inner = new FakeConsumer<string, string>();
        var result = KafkaTrackingInterceptor.WrapConsumer(inner);

        Assert.IsType<TrackingKafkaConsumer<string, string>>(result);
    }

    [Fact]
    public void WrapProducer_Returns_Tracked_When_Enabled()
    {
        KafkaTrackingInterceptor.EnableProducerTracking<string, string>(MakeOptions());

        var inner = new FakeProducer<string, string>();
        var result = KafkaTrackingInterceptor.WrapProducer(inner);

        Assert.IsType<TrackingKafkaProducer<string, string>>(result);
    }

    #region Test Doubles

    internal class FakeConsumer<TKey, TValue> : IConsumer<TKey, TValue>
    {
        public ConsumeResult<TKey, TValue>? NextConsumeResult { get; set; }

        public Handle Handle => throw new NotImplementedException();
        public string Name => "fake";
        public string MemberId => "fake";
        public List<TopicPartition> Assignment => [];
        public List<string> Subscription => [];
        public IConsumerGroupMetadata ConsumerGroupMetadata => throw new NotImplementedException();
        public void SetSaslCredentials(string username, string password) { }
        public void Dispose() { }
        public int AddBrokers(string brokers) => 0;
        public ConsumeResult<TKey, TValue> Consume(int millisecondsTimeout) => NextConsumeResult!;
        public ConsumeResult<TKey, TValue> Consume(CancellationToken cancellationToken = default) => NextConsumeResult!;
        public ConsumeResult<TKey, TValue> Consume(TimeSpan timeout) => NextConsumeResult!;
        public void Subscribe(IEnumerable<string> topics) { }
        public void Subscribe(string topic) { }
        public void Unsubscribe() { }
        public void Assign(TopicPartition partition) { }
        public void Assign(TopicPartitionOffset partition) { }
        public void Assign(IEnumerable<TopicPartition> partitions) { }
        public void Assign(IEnumerable<TopicPartitionOffset> partitions) { }
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


    #endregion
}
