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
/// never written to: when headers are added, Confluent is handed a copy. The entry records the W3C ids of the last
/// <c>traceparent</c> the message sent carries, else those of <see cref="System.Diagnostics.Activity.Current"/> at the call.
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

    // Whether this wrapper is the last before Confluent's own producer, the only place it adds a traceparent: a wrapper
    // inside it (an instrumentation package's) writes its own, which a consumer should parent on (4.3).
    private readonly bool _lastBeforeConfluent;

    // Whether Confluent calls a delivery handler for this producer's messages (On, Off, Unknown), read once.
    private int _deliveryReports;

    public TrackingKafkaProducer(IProducer<TKey, TValue> inner, KafkaTracker tracker, KafkaTrackingOptions options)
    {
        _inner = inner;
        _tracker = tracker;
        _options = options;
        _lastBeforeConfluent = inner.GetType() is { IsGenericType: true } type
            && type.Assembly == typeof(IProducer<,>).Assembly
            && type.GetGenericTypeDefinition().FullName == "Confluent.Kafka.Producer`2";
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
        var tracking = _tracker.TrackProduce(topic, message, KafkaOperation.ProduceAsync, _options, AddsTraceParent);

        DeliveryResult<TKey, TValue> result;
        try
        {
            result = await produce(tracking.Message, cancellationToken);
        }
        catch (Exception exception)
        {
            tracking.Failed(exception);
            throw;
        }

        tracking.Delivered(result);
        return result;
    }

    private void Produce(
        string topic,
        Action<Message<TKey, TValue>, Action<DeliveryReport<TKey, TValue>>?> produce,
        Message<TKey, TValue> message,
        Action<DeliveryReport<TKey, TValue>>? deliveryHandler)
    {
        var tracking = _tracker.TrackProduce(topic, message, KafkaOperation.Produce, _options, AddsTraceParent);

        // Confluent calls no delivery handler for this producer and refuses one (F7, F24): produce as the caller asked
        // and log at the call, without the partition and offset only a delivery report holds.
        if (DeliveryReports == Off)
        {
            Send(deliveryHandler);
            tracking.Sent();
            return;
        }

        try
        {
            produce(tracking.Message, tracking.Handler(deliveryHandler));
        }
        catch (InvalidOperationException refusal) when (IsDeliveryReportsRefusal(refusal, deliveryHandler))
        {
            // The setting could not be read (a decorator, a Confluent whose internals moved): Confluent's refusal comes
            // before anything is queued, so produce again without a handler, and remember.
            Volatile.Write(ref _deliveryReports, Off);
            Send(null);
            tracking.Sent();
        }
        catch (Exception exception)
        {
            tracking.Failed(exception);
            throw;
        }

        void Send(Action<DeliveryReport<TKey, TValue>>? handler)
        {
            try
            {
                produce(tracking.Message, handler);
            }
            catch (Exception exception)
            {
                tracking.Failed(exception);
                throw;
            }
        }
    }

    // A traceparent is added only where this wrapper is the last before Confluent (4.3).
    private bool AddsTraceParent => _options.PropagateTraceContext && _lastBeforeConfluent;

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
