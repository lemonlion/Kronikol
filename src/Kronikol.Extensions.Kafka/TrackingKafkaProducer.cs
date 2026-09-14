using Confluent.Kafka;
using System.Text;
using Kronikol.Constants;
using Kronikol.Tracking;

namespace Kronikol.Extensions.Kafka;

/// <summary>
/// Tracking wrapper for Kafka operations. Intercepts calls and logs them for test diagrams.
/// </summary>
public class TrackingKafkaProducer<TKey, TValue> : IProducer<TKey, TValue>
{
    private readonly IProducer<TKey, TValue> _inner;
    private readonly KafkaTracker _tracker;
    private readonly KafkaTrackingOptions _options;

    public TrackingKafkaProducer(IProducer<TKey, TValue> inner, KafkaTracker tracker, KafkaTrackingOptions options)
    {
        _inner = inner;
        _tracker = tracker;
        _options = options;
    }

    public async Task<DeliveryResult<TKey, TValue>> ProduceAsync(
        string topic,
        Message<TKey, TValue> message,
        CancellationToken cancellationToken = default)
    {
        InjectTestIdentityHeaders(message);
        var result = await _inner.ProduceAsync(topic, message, cancellationToken);

        var op = new KafkaOperationInfo(KafkaOperation.ProduceAsync, topic,
            result.Partition.Value, result.Offset.Value);
        _tracker.LogProduce(op, BuildContent(message));

        return result;
    }

    public async Task<DeliveryResult<TKey, TValue>> ProduceAsync(
        TopicPartition topicPartition,
        Message<TKey, TValue> message,
        CancellationToken cancellationToken = default)
    {
        InjectTestIdentityHeaders(message);
        var result = await _inner.ProduceAsync(topicPartition, message, cancellationToken);

        var op = new KafkaOperationInfo(KafkaOperation.ProduceAsync, topicPartition.Topic,
            result.Partition.Value, result.Offset.Value);
        _tracker.LogProduce(op, BuildContent(message));

        return result;
    }

    public void Produce(
        string topic,
        Message<TKey, TValue> message,
        Action<DeliveryReport<TKey, TValue>>? deliveryHandler = null)
    {
        InjectTestIdentityHeaders(message);
        var content = BuildContent(message);

        Action<DeliveryReport<TKey, TValue>>? wrappedHandler = report =>
        {
            var op = new KafkaOperationInfo(KafkaOperation.Produce, topic,
                report.Partition.Value, report.Offset.Value);
            _tracker.LogProduce(op, content);
            deliveryHandler?.Invoke(report);
        };

        _inner.Produce(topic, message, wrappedHandler);
    }

    public void Produce(
        TopicPartition topicPartition,
        Message<TKey, TValue> message,
        Action<DeliveryReport<TKey, TValue>>? deliveryHandler = null)
    {
        InjectTestIdentityHeaders(message);
        var content = BuildContent(message);

        Action<DeliveryReport<TKey, TValue>>? wrappedHandler = report =>
        {
            var op = new KafkaOperationInfo(KafkaOperation.Produce, topicPartition.Topic,
                report.Partition.Value, report.Offset.Value);
            _tracker.LogProduce(op, content);
            deliveryHandler?.Invoke(report);
        };

        _inner.Produce(topicPartition, message, wrappedHandler);
    }

    private string? BuildContent(Message<TKey, TValue> message)
    {
        if (_options.Verbosity == KafkaTrackingVerbosity.Summarised) return null;

        var parts = new List<string>();
        if (_options.LogMessageKey && message.Key is not null)
            parts.Add($"Key: {message.Key}");
        if (_options.LogMessageValue && message.Value is not null)
            parts.Add($"Value: {message.Value}");
        return parts.Count > 0 ? string.Join(", ", parts) : null;
    }

    private void InjectTestIdentityHeaders(Message<TKey, TValue> message)
    {
        if (!_options.PropagateTestIdentity) return;

        var testInfo = TestInfoResolver.ResolveWithSource(null, _options.CurrentTestInfoFetcher);
        if (testInfo is null) return;

        message.Headers ??= new Headers();
        message.Headers.Add(TestTrackingMessageHeaders.TestName, Encoding.UTF8.GetBytes(testInfo.Value.Name));
        message.Headers.Add(TestTrackingMessageHeaders.TestId, Encoding.UTF8.GetBytes(testInfo.Value.Id));
    }

    // ─── IProducer<TKey, TValue> delegation ───────────────────

    public Handle Handle => _inner.Handle;
    public string Name => _inner.Name;
    public int AddBrokers(string brokers) => _inner.AddBrokers(brokers);
    public void SetSaslCredentials(string username, string password) => _inner.SetSaslCredentials(username, password);
    public int Poll(TimeSpan timeout) => _inner.Poll(timeout);
    public int Flush(TimeSpan timeout)
    {
        var result = _inner.Flush(timeout);
        LogFlush();
        return result;
    }

    public void Flush(CancellationToken cancellationToken = default)
    {
        _inner.Flush(cancellationToken);
        LogFlush();
    }

    private void LogFlush()
    {
        if (_options.TrackFlush)
        {
            var op = new KafkaOperationInfo(KafkaOperation.Flush);
            _tracker.LogFlush(op);
        }
    }
    public void InitTransactions(TimeSpan timeout)
    {
        _inner.InitTransactions(timeout);
        LogTransaction(KafkaOperation.InitTransactions);
    }

    public void BeginTransaction()
    {
        _inner.BeginTransaction();
        LogTransaction(KafkaOperation.BeginTransaction);
    }

    public void CommitTransaction(TimeSpan timeout)
    {
        _inner.CommitTransaction(timeout);
        LogTransaction(KafkaOperation.CommitTransaction);
    }

    public void CommitTransaction()
    {
        _inner.CommitTransaction();
        LogTransaction(KafkaOperation.CommitTransaction);
    }

    public void AbortTransaction(TimeSpan timeout)
    {
        _inner.AbortTransaction(timeout);
        LogTransaction(KafkaOperation.AbortTransaction);
    }

    public void AbortTransaction()
    {
        _inner.AbortTransaction();
        LogTransaction(KafkaOperation.AbortTransaction);
    }

    public void SendOffsetsToTransaction(IEnumerable<TopicPartitionOffset> offsets, IConsumerGroupMetadata groupMetadata, TimeSpan timeout)
    {
        _inner.SendOffsetsToTransaction(offsets, groupMetadata, timeout);
        LogTransaction(KafkaOperation.SendOffsetsToTransaction);
    }

    private void LogTransaction(KafkaOperation operation)
    {
        if (_options.TrackTransactions)
        {
            var op = new KafkaOperationInfo(operation);
            _tracker.LogTransaction(op);
        }
    }
    public void Dispose() => _inner.Dispose();
}
