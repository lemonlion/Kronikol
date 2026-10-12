using Confluent.Kafka;
using System.Text;
using Kronikol.Constants;
using Kronikol.Tracking;

namespace Kronikol.Extensions.Kafka;

/// <summary>
/// Tracking wrapper for Kafka operations. Intercepts calls and logs them for test diagrams.
/// </summary>
public class TrackingKafkaConsumer<TKey, TValue> : IConsumer<TKey, TValue>
{
    private readonly IConsumer<TKey, TValue> _inner;
    private readonly KafkaTracker _tracker;
    private readonly KafkaTrackingOptions _options;

    public TrackingKafkaConsumer(IConsumer<TKey, TValue> inner, KafkaTracker tracker, KafkaTrackingOptions options)
    {
        _inner = inner;
        _tracker = tracker;
        _options = options;
    }

    public ConsumeResult<TKey, TValue> Consume(int millisecondsTimeout) => ConsumeCore(() => _inner.Consume(millisecondsTimeout));

    public ConsumeResult<TKey, TValue> Consume(TimeSpan timeout) => ConsumeCore(() => _inner.Consume(timeout));

    public ConsumeResult<TKey, TValue> Consume(CancellationToken cancellationToken = default) => ConsumeCore(() => _inner.Consume(cancellationToken));

    private ConsumeResult<TKey, TValue> ConsumeCore(Func<ConsumeResult<TKey, TValue>> consume)
    {
        // The previous message's identity ends at the next poll, whatever that poll returns (F10): an empty poll or a
        // partition's end left it on everything the loop did next.
        if (_options.PropagateTestIdentity)
            TestIdentityScope.ClearMessageIdentity();

        ConsumeResult<TKey, TValue> result;
        try
        {
            result = consume();
        }
        catch (ConsumeException exception)
        {
            LogFailedConsume(exception);
            throw;
        }

        LogConsumeResult(result);
        return result;
    }

    public void Subscribe(string topic)
    {
        _inner.Subscribe(topic);

        if (_options.TrackSubscribe)
        {
            var op = new KafkaOperationInfo(KafkaOperation.Subscribe, topic);
            _tracker.LogSubscribe(op);
        }
    }

    public void Subscribe(IEnumerable<string> topics)
    {
        // Read once: the caller's sequence may be one that can only be read once.
        var subscribed = topics.ToList();
        _inner.Subscribe(subscribed);

        if (_options.TrackSubscribe)
        {
            foreach (var topic in subscribed)
            {
                var op = new KafkaOperationInfo(KafkaOperation.Subscribe, topic);
                _tracker.LogSubscribe(op);
            }
        }
    }

    private void LogConsumeResult(ConsumeResult<TKey, TValue>? result)
    {
        if (result is null || result.IsPartitionEOF) return;

        EstablishTestIdentity(result.Message?.Headers);
        AutoCorrelateOnConsume(result);

        var op = new KafkaOperationInfo(KafkaOperation.Consume, result.Topic,
            result.Partition.Value, result.Offset.Value);

        _tracker.LogConsume(_tracker.Capture(), op, BuildContent(result.Message));
    }

    // A consume that throws (a deserializer's error, a broker's) is drawn from the record the exception carries, as the
    // consume of its message's test (F22). Its key and value are still bytes, so it shows no content.
    private void LogFailedConsume(ConsumeException exception)
    {
        var record = exception.ConsumerRecord;
        EstablishTestIdentity(record?.Message?.Headers);

        var op = record is null
            ? new KafkaOperationInfo(KafkaOperation.Consume)
            : new KafkaOperationInfo(KafkaOperation.Consume, record.Topic,
                record.Partition.Value >= 0 ? record.Partition.Value : null,
                record.Offset.Value >= 0 ? record.Offset.Value : null);

        _tracker.LogConsume(_tracker.Capture(), op, content: null, "!" + exception.Error.Code, FailedSend.Describe(exception));
    }

    // A message that carries no identity is nobody's: the previous one's ended when this poll began.
    private void EstablishTestIdentity(Headers? headers)
    {
        if (!_options.PropagateTestIdentity || headers is null) return;

        string? testName = null;
        string? testId = null;

        if (headers.TryGetLastBytes(TestTrackingMessageHeaders.TestName, out var nameBytes))
            testName = Encoding.UTF8.GetString(nameBytes);
        if (headers.TryGetLastBytes(TestTrackingMessageHeaders.TestId, out var idBytes))
            testId = Encoding.UTF8.GetString(idBytes);

        if (testName is not null && testId is not null)
            TestIdentityScope.SetFromMessage(testName, testId);
    }

    private void AutoCorrelateOnConsume(ConsumeResult<TKey, TValue> result)
    {
        if (!_options.AutoCorrelateOnConsume) return;

        var identity = TestIdentityScope.Current;
        if (identity is null) return;

        var messageKey = result.Message?.Key?.ToString();
        if (messageKey is null) return;

        var correlationKey = _options.ConsumeKeyExtractor is not null
            ? _options.ConsumeKeyExtractor(_options.ServiceName, messageKey)
            : CorrelationKeys.Kafka(_options.ServiceName, messageKey);

        TestCorrelationStore.Correlate(correlationKey, identity.Value.Name, identity.Value.Id);
    }

    private string? BuildContent(Message<TKey, TValue>? message)
    {
        if (message is null) return null;
        if (!KafkaTracker.ShowsContent(_options)) return null;

        var parts = new List<string>();
        if (_options.LogMessageKey && message.Key is not null)
            parts.Add($"Key: {message.Key}");
        if (_options.LogMessageValue && message.Value is not null)
            parts.Add($"Value: {message.Value}");
        return parts.Count > 0 ? string.Join(", ", parts) : null;
    }

    // ─── IConsumer<TKey, TValue> delegation ───────────────────

    public Handle Handle => _inner.Handle;
    public string Name => _inner.Name;
    public string MemberId => _inner.MemberId;
    public List<TopicPartition> Assignment => _inner.Assignment;
    public List<string> Subscription => _inner.Subscription;
    public IConsumerGroupMetadata ConsumerGroupMetadata => _inner.ConsumerGroupMetadata;
    public int AddBrokers(string brokers) => _inner.AddBrokers(brokers);
    public void SetSaslCredentials(string username, string password) => _inner.SetSaslCredentials(username, password);
    public void Unsubscribe()
    {
        _inner.Unsubscribe();

        if (_options.TrackUnsubscribe)
        {
            var op = new KafkaOperationInfo(KafkaOperation.Unsubscribe);
            _tracker.LogUnsubscribe(op);
        }
    }
    public void Assign(TopicPartition partition) => _inner.Assign(partition);
    public void Assign(TopicPartitionOffset partition) => _inner.Assign(partition);
    public void Assign(IEnumerable<TopicPartition> partitions) => _inner.Assign(partitions);
    public void Assign(IEnumerable<TopicPartitionOffset> partitions) => _inner.Assign(partitions);
    public void IncrementalAssign(IEnumerable<TopicPartitionOffset> partitions) => _inner.IncrementalAssign(partitions);
    public void IncrementalAssign(IEnumerable<TopicPartition> partitions) => _inner.IncrementalAssign(partitions);
    public void IncrementalUnassign(IEnumerable<TopicPartition> partitions) => _inner.IncrementalUnassign(partitions);
    public void Unassign() => _inner.Unassign();
    public void StoreOffset(ConsumeResult<TKey, TValue> result) => _inner.StoreOffset(result);
    public void StoreOffset(TopicPartitionOffset offset) => _inner.StoreOffset(offset);
    public List<TopicPartitionOffset> Commit()
    {
        var result = _inner.Commit();
        LogCommit();
        return result;
    }

    public void Commit(IEnumerable<TopicPartitionOffset> offsets)
    {
        _inner.Commit(offsets);
        LogCommit();
    }

    public void Commit(ConsumeResult<TKey, TValue> result)
    {
        _inner.Commit(result);
        LogCommit();
    }

    private void LogCommit()
    {
        if (_options.TrackCommit)
        {
            var op = new KafkaOperationInfo(KafkaOperation.Commit);
            _tracker.LogCommit(op);
        }
    }
    public void Seek(TopicPartitionOffset tpo) => _inner.Seek(tpo);
    public void Pause(IEnumerable<TopicPartition> partitions) => _inner.Pause(partitions);
    public void Resume(IEnumerable<TopicPartition> partitions) => _inner.Resume(partitions);
    public List<TopicPartitionOffset> Committed(IEnumerable<TopicPartition> partitions, TimeSpan timeout) => _inner.Committed(partitions, timeout);
    public List<TopicPartitionOffset> Committed(TimeSpan timeout) => _inner.Committed(timeout);
    public Offset Position(TopicPartition partition) => _inner.Position(partition);
    public List<TopicPartitionOffset> OffsetsForTimes(IEnumerable<TopicPartitionTimestamp> timestampsToSearch, TimeSpan timeout) => _inner.OffsetsForTimes(timestampsToSearch, timeout);
    public WatermarkOffsets GetWatermarkOffsets(TopicPartition topicPartition) => _inner.GetWatermarkOffsets(topicPartition);
    public WatermarkOffsets QueryWatermarkOffsets(TopicPartition topicPartition, TimeSpan timeout) => _inner.QueryWatermarkOffsets(topicPartition, timeout);
    public void Close() => _inner.Close();
    public void Dispose() => _inner.Dispose();
}
