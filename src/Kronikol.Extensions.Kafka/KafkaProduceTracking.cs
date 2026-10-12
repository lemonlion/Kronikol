using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using Kronikol.Constants;
using Kronikol.Tracking;

namespace Kronikol.Extensions.Kafka;

/// <summary>
/// One produce being tracked, from <see cref="KafkaTracker.TrackProduce{TKey,TValue}(string, Message{TKey,TValue}, KafkaOperation)"/>:
/// send <see cref="Message"/> in place of the caller's, then report how it went, once. Its scenario, phase, verbosity,
/// trace ids and request time were taken when it was created, on the caller's thread, so it is the caller's produce
/// wherever the outcome is reported, a delivery report on Confluent's poll thread included.
/// </summary>
public sealed class KafkaProduceTracking<TKey, TValue>
{
    private readonly KafkaTracker _tracker;
    private readonly KafkaCall _call;
    private readonly KafkaPayload _payload;
    private readonly string _topic;
    private readonly KafkaOperation _operation;
    private readonly Activity? _publish;
    private readonly Activity? _previous;
    private int _reported;

    internal KafkaProduceTracking(KafkaTracker tracker, KafkaCall call, string topic, KafkaOperation operation,
        Message<TKey, TValue> message, KafkaTrackingOptions options, bool addTraceParent, Activity? publish = null,
        Activity? previous = null)
    {
        _tracker = tracker;
        _call = call;
        _topic = topic;
        _operation = operation;
        _publish = publish;
        _previous = previous;
        _payload = KafkaContent.Of(message, options, leaveOutKronikols: false);
        Message = ToSend(message, call, options, addTraceParent);
    }

    /// <summary>
    /// The message to hand to Kafka: the caller's own, or, when headers are added, a copy holding its key, value,
    /// timestamp and headers, then each header it lacks: the scenario's identity and Kronikol trace id (only an identity
    /// that names a scenario travels), and a <c>traceparent</c> under <see cref="KafkaTrackingOptions.PropagateTraceContext"/>.
    /// The caller's message is never written to.
    /// </summary>
    public Message<TKey, TValue> Message { get; }

    /// <summary>Reports the produce delivered, with the partition and offset the broker wrote it to.</summary>
    public void Delivered(DeliveryResult<TKey, TValue> result) =>
        Report(new KafkaOperationInfo(_operation, _topic, Known(result.Partition), Known(result.Offset)), DateTimeOffset.UtcNow, null, null);

    /// <summary>Reports a delivery report: delivered, or failed with Confluent's error code behind a bang and its reason.</summary>
    public void Delivered(DeliveryReport<TKey, TValue> report)
    {
        var failed = report.Error is { IsError: true };
        Report(new KafkaOperationInfo(_operation, _topic, Known(report.Partition), Known(report.Offset)), DateTimeOffset.UtcNow,
            failed ? "!" + report.Error!.Code : null, failed ? report.Error!.Reason : null);
    }

    /// <summary>
    /// Reports the produce failed with <paramref name="exception"/>: a <see cref="KafkaException"/> by its error code
    /// behind a bang, anything else by its type, with the message chain as the error. A cancelled wait says the message
    /// was queued and may still be delivered.
    /// </summary>
    public void Failed(Exception exception) =>
        Report(FailedOperation(exception), DateTimeOffset.UtcNow, Status(exception), Describe(exception));

    /// <summary>
    /// Reports the produce sent, at the time it was made and without a partition or offset, for a producer that reports
    /// no deliveries.
    /// </summary>
    public void Sent() => Report(new KafkaOperationInfo(_operation, _topic), _call.At, null, null);

    /// <summary>A delivery handler that reports the delivery, then runs <paramref name="then"/>.</summary>
    public Action<DeliveryReport<TKey, TValue>> Handler(Action<DeliveryReport<TKey, TValue>>? then = null) => report =>
    {
        Delivered(report);
        then?.Invoke(report);
    };

    /// <summary>
    /// Ends the publish span a wrapper's produce runs under (<see cref="KafkaTrackingOptions.TraceMessageProcessing"/>),
    /// when the produce call returns on the caller's flow, so a delivery report on Confluent's poll thread never ends it
    /// there: what was current when it ended stays current, unless it was the publish span, whose place what was
    /// current before it takes back.
    /// </summary>
    internal void EndPublish()
    {
        if (_publish is null || _publish.IsStopped)
            return;
        var current = Activity.Current;
        _publish.Stop();
        var restored = current != _publish ? current : _previous;
        Activity.Current = restored is { IsStopped: false } ? restored : null;
    }

    private void Report(KafkaOperationInfo op, DateTimeOffset outcomeAt, string? failureStatus, string? error)
    {
        if (Interlocked.Exchange(ref _reported, 1) != 0)
            return;
        _tracker.LogProduce(_call, op, _payload, outcomeAt, failureStatus, error, Ids());
    }

    // The last traceparent of the message sent, the caller's, the one an instrumentation wrapper inside appended during
    // the call, or Kronikol's; else the span current when the produce was made.
    private W3CIds? Ids() => W3CIds.OfLastTraceParent(Message.Headers) ?? _call.Ambient;

    private static Message<TKey, TValue> ToSend(Message<TKey, TValue> message, KafkaCall call, KafkaTrackingOptions options, bool addTraceParent)
    {
        var missing = new List<(string Key, string Value)>(4);
        if (options.PropagateTestIdentity && call.Test is { IsAttributed: true } identity)
        {
            if (!Has(message.Headers, TestTrackingMessageHeaders.TestName))
                missing.Add((TestTrackingMessageHeaders.TestName, identity.Name));
            if (!Has(message.Headers, TestTrackingMessageHeaders.TestId))
                missing.Add((TestTrackingMessageHeaders.TestId, identity.Id));
            if (!Has(message.Headers, TestTrackingMessageHeaders.TraceId))
                missing.Add((TestTrackingMessageHeaders.TraceId, call.TraceId.ToString()));
        }
        if (addTraceParent && !Has(message.Headers, W3CIds.TraceParentHeader))
            missing.Add((W3CIds.TraceParentHeader, (call.Ambient ?? W3CIds.Random()).ToTraceParent()));
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
    private KafkaOperationInfo FailedOperation(Exception exception) =>
        exception is ProduceException<TKey, TValue> { DeliveryResult: { } result }
            ? new KafkaOperationInfo(_operation, _topic, Known(result.Partition), Known(result.Offset))
            : new KafkaOperationInfo(_operation, _topic);

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
}
