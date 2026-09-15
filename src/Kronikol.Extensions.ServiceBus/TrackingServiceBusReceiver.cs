using Azure.Messaging.ServiceBus;
using Kronikol.Constants;
using Kronikol.Tracking;

namespace Kronikol.Extensions.ServiceBus;

/// <summary>
/// A <see cref="ServiceBusReceiver"/> subclass that intercepts receive operations
/// for test diagram tracking.
/// </summary>
public class TrackingServiceBusReceiver : ServiceBusReceiver
{
    private readonly ServiceBusReceiver _inner;
    private readonly ServiceBusTracker _tracker;
    private readonly ServiceBusTrackingOptions _options;

    public TrackingServiceBusReceiver(
        ServiceBusReceiver inner, ServiceBusTracker tracker, ServiceBusTrackingOptions options) : base()
    {
        _inner = inner;
        _tracker = tracker;
        _options = options;
    }

    /// <summary>The underlying real <see cref="ServiceBusReceiver"/>.</summary>
    public ServiceBusReceiver Inner => _inner;

    public override string EntityPath => _inner.EntityPath;
    public override string FullyQualifiedNamespace => _inner.FullyQualifiedNamespace;
    public override string Identifier => _inner.Identifier;
    public override bool IsClosed => _inner.IsClosed;
    public override ServiceBusReceiveMode ReceiveMode => _inner.ReceiveMode;
    public override int PrefetchCount => _inner.PrefetchCount;

    public override async Task<ServiceBusReceivedMessage?> ReceiveMessageAsync(
        TimeSpan? maxWaitTime = null, CancellationToken cancellationToken = default)
    {
        var op = ServiceBusOperationClassifier.Classify("ReceiveMessageAsync", EntityPath, null);
        var (reqId, traceId) = _tracker.LogRequest(op, null);
        try
        {
            var result = await _inner.ReceiveMessageAsync(maxWaitTime, cancellationToken);
            if (result is not null) EstablishTestIdentityFromProperties(result);
            var content = result is not null ? GetReceivedMessageContent(result) : null;
            _tracker.LogResponse(op, reqId, traceId, content);
            return result;
        }
        catch (Exception ex)
        {
            _tracker.LogResponse(op, reqId, traceId, ex.Message);
            throw;
        }
    }

    public override async Task<IReadOnlyList<ServiceBusReceivedMessage>> ReceiveMessagesAsync(
        int maxMessages, TimeSpan? maxWaitTime = null, CancellationToken cancellationToken = default)
    {
        var op = ServiceBusOperationClassifier.Classify("ReceiveMessagesAsync", EntityPath, null);
        var (reqId, traceId) = _tracker.LogRequest(op, null);
        try
        {
            var result = await _inner.ReceiveMessagesAsync(maxMessages, maxWaitTime, cancellationToken);
            if (result.Count > 0) EstablishTestIdentityFromProperties(result[0]);
            var content = _options.Verbosity != ServiceBusTrackingVerbosity.Summarised
                ? $"[{result.Count} messages received]"
                : null;
            _tracker.LogResponse(op, reqId, traceId, content);
            return result;
        }
        catch (Exception ex)
        {
            _tracker.LogResponse(op, reqId, traceId, ex.Message);
            throw;
        }
    }

    public override async Task<ServiceBusReceivedMessage?> PeekMessageAsync(
        long? fromSequenceNumber = null, CancellationToken cancellationToken = default)
    {
        var op = ServiceBusOperationClassifier.Classify("PeekMessageAsync", EntityPath, null);
        var (reqId, traceId) = _tracker.LogRequest(op, null);
        try
        {
            var result = await _inner.PeekMessageAsync(fromSequenceNumber, cancellationToken);
            var content = result is not null ? GetReceivedMessageContent(result) : null;
            _tracker.LogResponse(op, reqId, traceId, content);
            return result;
        }
        catch (Exception ex)
        {
            _tracker.LogResponse(op, reqId, traceId, ex.Message);
            throw;
        }
    }

    public override async Task<IReadOnlyList<ServiceBusReceivedMessage>> PeekMessagesAsync(
        int maxMessages, long? fromSequenceNumber = null, CancellationToken cancellationToken = default)
        => await _inner.PeekMessagesAsync(maxMessages, fromSequenceNumber, cancellationToken);

    public override async Task CompleteMessageAsync(
        ServiceBusReceivedMessage message, CancellationToken cancellationToken = default)
    {
        var op = ServiceBusOperationClassifier.Classify("CompleteMessageAsync", EntityPath, null);
        var (reqId, traceId) = _tracker.LogRequest(op, null);
        try
        {
            await _inner.CompleteMessageAsync(message, cancellationToken);
            _tracker.LogResponse(op, reqId, traceId, null);
        }
        catch (Exception ex)
        {
            _tracker.LogResponse(op, reqId, traceId, ex.Message);
            throw;
        }
    }

    public override async Task AbandonMessageAsync(
        ServiceBusReceivedMessage message,
        IDictionary<string, object>? propertiesToModify = null,
        CancellationToken cancellationToken = default)
    {
        var op = ServiceBusOperationClassifier.Classify("AbandonMessageAsync", EntityPath, null);
        var (reqId, traceId) = _tracker.LogRequest(op, null);
        try
        {
            await _inner.AbandonMessageAsync(message, propertiesToModify, cancellationToken);
            _tracker.LogResponse(op, reqId, traceId, null);
        }
        catch (Exception ex)
        {
            _tracker.LogResponse(op, reqId, traceId, ex.Message);
            throw;
        }
    }

    public override async Task DeadLetterMessageAsync(
        ServiceBusReceivedMessage message,
        string? deadLetterReason = null,
        string? deadLetterErrorDescription = null,
        CancellationToken cancellationToken = default)
    {
        var op = ServiceBusOperationClassifier.Classify("DeadLetterMessageAsync", EntityPath, null);
        var content = deadLetterReason is not null && _options.Verbosity != ServiceBusTrackingVerbosity.Summarised
            ? $"Reason: {deadLetterReason}"
            : null;
        var (reqId, traceId) = _tracker.LogRequest(op, content);
        try
        {
            await _inner.DeadLetterMessageAsync(message, deadLetterReason, deadLetterErrorDescription, cancellationToken);
            _tracker.LogResponse(op, reqId, traceId, null);
        }
        catch (Exception ex)
        {
            _tracker.LogResponse(op, reqId, traceId, ex.Message);
            throw;
        }
    }

    public override async Task DeadLetterMessageAsync(
        ServiceBusReceivedMessage message,
        IDictionary<string, object>? propertiesToModify = null,
        CancellationToken cancellationToken = default)
        => await _inner.DeadLetterMessageAsync(message, propertiesToModify, cancellationToken);

    public override async Task DeferMessageAsync(
        ServiceBusReceivedMessage message,
        IDictionary<string, object>? propertiesToModify = null,
        CancellationToken cancellationToken = default)
    {
        var op = ServiceBusOperationClassifier.Classify("DeferMessageAsync", EntityPath, null);
        var (reqId, traceId) = _tracker.LogRequest(op, null);
        try
        {
            await _inner.DeferMessageAsync(message, propertiesToModify, cancellationToken);
            _tracker.LogResponse(op, reqId, traceId, null);
        }
        catch (Exception ex)
        {
            _tracker.LogResponse(op, reqId, traceId, ex.Message);
            throw;
        }
    }

    public override async Task<ServiceBusReceivedMessage> ReceiveDeferredMessageAsync(
        long sequenceNumber, CancellationToken cancellationToken = default)
        => await _inner.ReceiveDeferredMessageAsync(sequenceNumber, cancellationToken);

    public override async Task<IReadOnlyList<ServiceBusReceivedMessage>> ReceiveDeferredMessagesAsync(
        IEnumerable<long> sequenceNumbers, CancellationToken cancellationToken = default)
        => await _inner.ReceiveDeferredMessagesAsync(sequenceNumbers, cancellationToken);

    public override async Task RenewMessageLockAsync(
        ServiceBusReceivedMessage message, CancellationToken cancellationToken = default)
    {
        var op = ServiceBusOperationClassifier.Classify("RenewMessageLockAsync", EntityPath, null);
        var (reqId, traceId) = _tracker.LogRequest(op, null);
        try
        {
            await _inner.RenewMessageLockAsync(message, cancellationToken);
            _tracker.LogResponse(op, reqId, traceId, null);
        }
        catch (Exception ex)
        {
            _tracker.LogResponse(op, reqId, traceId, ex.Message);
            throw;
        }
    }

    public override async Task CloseAsync(CancellationToken cancellationToken = default)
        => await _inner.CloseAsync(cancellationToken);

    private void EstablishTestIdentityFromProperties(ServiceBusReceivedMessage message)
    {
        if (!_options.PropagateTestIdentity) return;
        TestIdentityScope.ClearMessageIdentity();

        if (message.ApplicationProperties.TryGetValue(TestTrackingMessageHeaders.TestName, out var nameObj) &&
            message.ApplicationProperties.TryGetValue(TestTrackingMessageHeaders.TestId, out var idObj) &&
            nameObj is string testName && idObj is string testId)
        {
            TestIdentityScope.SetFromMessage(testName, testId);
            AutoCorrelateOnConsume(message.MessageId, testName, testId);
        }
    }

    private void AutoCorrelateOnConsume(string? messageId, string testName, string testId)
    {
        if (!_options.AutoCorrelateOnConsume) return;
        if (messageId is null) return;

        var key = CorrelationKeys.ServiceBus(_options.ServiceName, messageId);
        TestCorrelationStore.Correlate(key, testName, testId);
    }

    private string? GetReceivedMessageContent(ServiceBusReceivedMessage message)
    {
        if (_options.Verbosity == ServiceBusTrackingVerbosity.Summarised)
            return null;

        try
        {
            return message.Body?.ToString();
        }
        catch
        {
            return null;
        }
    }
}
