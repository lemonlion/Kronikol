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

    // Under TraceMessageProcessing, the last message's scope, open until this consumer's next call.
    private KafkaTracker.ConsumeScope? _processing;

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
        // The last message was handled once the loop polls again: its consume is answered before this poll waits.
        EndProcessing();

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

        // The message's identity stays current until the next poll (F10); under TraceMessageProcessing so do its span and
        // its open consume.
        if (_options.TraceMessageProcessing)
            _processing = _tracker.BeginConsume(result, _options);
        else
            _tracker.Consumed(result, _options);
    }

    // Closes the last message's scope, when TraceMessageProcessing opened one, at the consumer's next call.
    private void EndProcessing() => Interlocked.Exchange(ref _processing, null)?.CloseAtNextCall();

    // A consume that throws (a deserializer's error, a broker's) is drawn from the record the exception carries, as the
    // consume of its message's test (F22). Its key and value are still bytes, so it shows no content.
    private void LogFailedConsume(ConsumeException exception)
    {
        var record = exception.ConsumerRecord;
        KafkaTracker.EstablishTestIdentity(record?.Message?.Headers, _options);

        var op = record is null
            ? new KafkaOperationInfo(KafkaOperation.Consume)
            : new KafkaOperationInfo(KafkaOperation.Consume, record.Topic,
                record.Partition.Value >= 0 ? record.Partition.Value : null,
                record.Offset.Value >= 0 ? record.Offset.Value : null);

        var (call, ids) = KafkaTracker.ConsumedTrace(_tracker.Capture(), record?.Message?.Headers);
        _tracker.LogConsume(call, op, KafkaPayload.Empty, "!" + exception.Error.Code, FailedSend.Describe(exception), ids);
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
    public void StoreOffset(ConsumeResult<TKey, TValue> result)
    {
        EndProcessing();
        _inner.StoreOffset(result);
    }

    public void StoreOffset(TopicPartitionOffset offset)
    {
        EndProcessing();
        _inner.StoreOffset(offset);
    }

    public List<TopicPartitionOffset> Commit()
    {
        EndProcessing();
        var result = _inner.Commit();
        LogCommit();
        return result;
    }

    public void Commit(IEnumerable<TopicPartitionOffset> offsets)
    {
        EndProcessing();
        _inner.Commit(offsets);
        LogCommit();
    }

    public void Commit(ConsumeResult<TKey, TValue> result)
    {
        EndProcessing();
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
    public void Close()
    {
        EndProcessing();
        _inner.Close();
    }

    public void Dispose()
    {
        EndProcessing();
        _inner.Dispose();
    }
}
