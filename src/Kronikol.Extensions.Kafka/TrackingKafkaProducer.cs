using System.Reflection;
using Confluent.Kafka;
using System.Text;
using Kronikol.Constants;
using Kronikol.Tracking;

namespace Kronikol.Extensions.Kafka;

/// <summary>
/// Tracking wrapper for Kafka operations. Intercepts calls and logs them for test diagrams.
/// </summary>
/// <remarks>
/// A produce is captured at the call (its scenario, phase, verbosity and time) and stored once its outcome is known, the
/// response stamped then, so a produce is its caller's even though Confluent runs delivery handlers on another thread.
/// A produce that fails, by a failed delivery report or by throwing, is drawn with its error. The caller's message is
/// never written to: when identity headers are added, Confluent is handed a copy.
/// </remarks>
public class TrackingKafkaProducer<TKey, TValue> : IProducer<TKey, TValue>
{
    private const string DeliveryReportsDisabled = "A delivery handler was specified, but delivery reports are disabled.";

    private const int Unread = 0, On = 1, Off = 2, Unknown = 3;

    private static readonly PropertyInfo? HandleOwner =
        typeof(Handle).GetProperty("Owner", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private readonly IProducer<TKey, TValue> _inner;
    private readonly KafkaTracker _tracker;
    private readonly KafkaTrackingOptions _options;

    // Whether Confluent calls a delivery handler for this producer's messages (On, Off, Unknown), read once.
    private int _deliveryReports;

    public TrackingKafkaProducer(IProducer<TKey, TValue> inner, KafkaTracker tracker, KafkaTrackingOptions options)
    {
        _inner = inner;
        _tracker = tracker;
        _options = options;
    }

    public Task<DeliveryResult<TKey, TValue>> ProduceAsync(
        string topic,
        Message<TKey, TValue> message,
        CancellationToken cancellationToken = default)
        => ProduceAsync(topic, (sent, token) => _inner.ProduceAsync(topic, sent, token), message, cancellationToken);

    public Task<DeliveryResult<TKey, TValue>> ProduceAsync(
        TopicPartition topicPartition,
        Message<TKey, TValue> message,
        CancellationToken cancellationToken = default)
        => ProduceAsync(topicPartition.Topic, (sent, token) => _inner.ProduceAsync(topicPartition, sent, token), message, cancellationToken);

    public void Produce(
        string topic,
        Message<TKey, TValue> message,
        Action<DeliveryReport<TKey, TValue>>? deliveryHandler = null)
        => Produce(topic, (sent, handler) => _inner.Produce(topic, sent, handler), message, deliveryHandler);

    public void Produce(
        TopicPartition topicPartition,
        Message<TKey, TValue> message,
        Action<DeliveryReport<TKey, TValue>>? deliveryHandler = null)
        => Produce(topicPartition.Topic, (sent, handler) => _inner.Produce(topicPartition, sent, handler), message, deliveryHandler);

    private async Task<DeliveryResult<TKey, TValue>> ProduceAsync(
        string topic,
        Func<Message<TKey, TValue>, CancellationToken, Task<DeliveryResult<TKey, TValue>>> produce,
        Message<TKey, TValue> message,
        CancellationToken cancellationToken)
    {
        var call = _tracker.Capture();
        var content = BuildContent(message);

        DeliveryResult<TKey, TValue> result;
        try
        {
            result = await produce(ToSend(message, call), cancellationToken);
        }
        catch (Exception exception)
        {
            _tracker.LogProduce(call, Failed(KafkaOperation.ProduceAsync, topic, exception), content, DateTimeOffset.UtcNow, Status(exception), Describe(exception));
            throw;
        }

        _tracker.LogProduce(call, new KafkaOperationInfo(KafkaOperation.ProduceAsync, topic, result.Partition.Value, result.Offset.Value),
            content, DateTimeOffset.UtcNow);
        return result;
    }

    private void Produce(
        string topic,
        Action<Message<TKey, TValue>, Action<DeliveryReport<TKey, TValue>>?> produce,
        Message<TKey, TValue> message,
        Action<DeliveryReport<TKey, TValue>>? deliveryHandler)
    {
        var call = _tracker.Capture();
        var content = BuildContent(message);
        var sent = ToSend(message, call);

        // Confluent calls no delivery handler for this producer and refuses one (F7, F24): produce as the caller asked
        // and log at the call, without the partition and offset only a delivery report holds.
        if (DeliveryReports == Off)
        {
            Send(deliveryHandler);
            _tracker.LogProduce(call, new KafkaOperationInfo(KafkaOperation.Produce, topic), content, call.At);
            return;
        }

        try
        {
            produce(sent, report =>
            {
                var failed = report.Error is { IsError: true };
                _tracker.LogProduce(call, new KafkaOperationInfo(KafkaOperation.Produce, topic, Known(report.Partition), Known(report.Offset)),
                    content, DateTimeOffset.UtcNow, failed ? "!" + report.Error!.Code : null, failed ? report.Error!.Reason : null);
                deliveryHandler?.Invoke(report);
            });
        }
        catch (InvalidOperationException refusal) when (IsDeliveryReportsRefusal(refusal, deliveryHandler))
        {
            // The setting could not be read (a decorator, a Confluent whose internals moved): Confluent's refusal comes
            // before anything is queued, so produce again without a handler, and remember.
            Volatile.Write(ref _deliveryReports, Off);
            Send(null);
            _tracker.LogProduce(call, new KafkaOperationInfo(KafkaOperation.Produce, topic), content, call.At);
        }
        catch (Exception exception)
        {
            LogFailed(exception);
            throw;
        }

        void Send(Action<DeliveryReport<TKey, TValue>>? handler)
        {
            try
            {
                produce(sent, handler);
            }
            catch (Exception exception)
            {
                LogFailed(exception);
                throw;
            }
        }

        void LogFailed(Exception exception) =>
            _tracker.LogProduce(call, Failed(KafkaOperation.Produce, topic, exception), content, DateTimeOffset.UtcNow, Status(exception), Describe(exception));
    }

    // Exactly Confluent's refusal of a handler on a producer without delivery reports, for a call that passed none of its
    // own and a producer whose setting could not be read. Not ObjectDisposedException, which derives from
    // InvalidOperationException, and not a decorator's own refusal: produced again, either would run twice.
    private bool IsDeliveryReportsRefusal(Exception exception, Action<DeliveryReport<TKey, TValue>>? callersHandler) =>
        callersHandler is null
        && DeliveryReports == Unknown
        && exception.GetType() == typeof(InvalidOperationException)
        && exception.Message == DeliveryReportsDisabled;

    private int DeliveryReports
    {
        get
        {
            if (Volatile.Read(ref _deliveryReports) == Unread)
                Interlocked.CompareExchange(ref _deliveryReports, ReadDeliveryReports(_inner) switch { true => On, false => Off, null => Unknown }, Unread);
            return Volatile.Read(ref _deliveryReports);
        }
    }

    // The setting of the producer that owns the handle: a dependent producer's own says true, while its owner, which runs
    // the delivery callbacks, may have registered none (F24). Null when it cannot be read: a producer that is not
    // Confluent's, or a Confluent whose internals moved.
    private static bool? ReadDeliveryReports(IProducer<TKey, TValue> producer)
    {
        try
        {
            var owner = HandleOwner?.GetValue(producer.Handle);
            return owner?.GetType().GetField("enableDeliveryReports", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner) as bool?;
        }
        catch (Exception)
        {
            // A fake or a decorator whose Handle throws.
            return null;
        }
    }

    // The message Confluent is handed: the caller's, or, when identity headers are added, a copy holding the caller's key,
    // value, timestamp and headers in order, then each identity header the caller's lack (F9), as gRPC copies a call's
    // metadata. Only an identity that names a scenario travels: the background identity on a message made its consumer
    // adopt it as a scenario's (F20).
    private Message<TKey, TValue> ToSend(Message<TKey, TValue> message, KafkaCall call)
    {
        if (!_options.PropagateTestIdentity || call.Test is not { IsAttributed: true } identity)
            return message;

        var missing = new List<(string Key, string Value)>(2);
        if (!Has(message.Headers, TestTrackingMessageHeaders.TestName))
            missing.Add((TestTrackingMessageHeaders.TestName, identity.Name));
        if (!Has(message.Headers, TestTrackingMessageHeaders.TestId))
            missing.Add((TestTrackingMessageHeaders.TestId, identity.Id));
        if (missing.Count == 0)
            return message;

        var headers = new Headers();
        if (message.Headers is not null)
            foreach (var header in message.Headers)
                headers.Add(header.Key, header.GetValueBytes());
        foreach (var (key, value) in missing)
            headers.Add(key, Encoding.UTF8.GetBytes(value));
        return new Message<TKey, TValue> { Key = message.Key, Value = message.Value, Timestamp = message.Timestamp, Headers = headers };
    }

    private static bool Has(Headers? headers, string key) => headers is not null && headers.Any(h => h.Key == key);

    // A failed produce, with the partition and offset its delivery result holds when Confluent knew them.
    private static KafkaOperationInfo Failed(KafkaOperation operation, string topic, Exception exception) =>
        exception is ProduceException<TKey, TValue> { DeliveryResult: { } result }
            ? new KafkaOperationInfo(operation, topic, Known(result.Partition), Known(result.Offset))
            : new KafkaOperationInfo(operation, topic);

    private static int? Known(Partition partition) => partition.Value >= 0 ? partition.Value : null;

    private static long? Known(Offset offset) => offset.Value >= 0 ? offset.Value : null;

    // Confluent's error code says what happened (!Local_MsgTimedOut); any other exception is named by its type, as
    // FailedSend names a thrown call's.
    private static string Status(Exception exception) =>
        exception is KafkaException kafka ? "!" + kafka.Error.Code : FailedSend.Status(exception);

    // Confluent queues a message before its wait can be cancelled, so a cancelled wait is not an undelivered message.
    private static string Describe(Exception exception) => exception is OperationCanceledException
        ? FailedSend.Describe(exception) + " The message was queued before the wait was cancelled, so it may still be delivered."
        : FailedSend.Describe(exception);

    private string? BuildContent(Message<TKey, TValue> message)
    {
        if (!KafkaTracker.ShowsContent(_options)) return null;

        var parts = new List<string>();
        if (_options.LogMessageKey && message.Key is not null)
            parts.Add($"Key: {message.Key}");
        if (_options.LogMessageValue && message.Value is not null)
            parts.Add($"Value: {message.Value}");
        return parts.Count > 0 ? string.Join(", ", parts) : null;
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
