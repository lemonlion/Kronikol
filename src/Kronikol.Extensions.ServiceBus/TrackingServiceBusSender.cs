using Azure.Messaging.ServiceBus;
using Kronikol.Constants;
using Kronikol.Tracking;

namespace Kronikol.Extensions.ServiceBus;

/// <summary>
/// A <see cref="ServiceBusSender"/> subclass that intercepts send operations
/// for test diagram tracking.
/// </summary>
public class TrackingServiceBusSender : ServiceBusSender
{
    private readonly ServiceBusSender _inner;
    private readonly ServiceBusTracker _tracker;
    private readonly ServiceBusTrackingOptions _options;

    public TrackingServiceBusSender(
        ServiceBusSender inner, ServiceBusTracker tracker, ServiceBusTrackingOptions options) : base()
    {
        _inner = inner;
        _tracker = tracker;
        _options = options;
    }

    /// <summary>The underlying real <see cref="ServiceBusSender"/>.</summary>
    public ServiceBusSender Inner => _inner;

    public override string EntityPath => _inner.EntityPath;
    public override string FullyQualifiedNamespace => _inner.FullyQualifiedNamespace;
    public override string Identifier => _inner.Identifier;
    public override bool IsClosed => _inner.IsClosed;

    public override async Task SendMessageAsync(ServiceBusMessage message, CancellationToken cancellationToken = default)
    {
        InjectTestIdentityProperties(message);
        var op = ServiceBusOperationClassifier.Classify("SendMessageAsync", EntityPath, null);
        var content = GetMessageContent(message);
        var (reqId, traceId) = _tracker.LogRequest(op, content);
        try
        {
            await _inner.SendMessageAsync(message, cancellationToken);
            _tracker.LogResponse(op, reqId, traceId, null);
        }
        catch (Exception ex)
        {
            _tracker.LogResponse(op, reqId, traceId, ex.Message);
            throw;
        }
    }

    public override async Task SendMessagesAsync(
        IEnumerable<ServiceBusMessage> messages, CancellationToken cancellationToken = default)
    {
        var messageList = messages as IReadOnlyList<ServiceBusMessage> ?? messages.ToList();
        foreach (var msg in messageList) InjectTestIdentityProperties(msg);
        var op = ServiceBusOperationClassifier.Classify("SendMessagesAsync", EntityPath, null) with
        {
            MessageCount = messageList.Count
        };
        var content = _options.Verbosity != ServiceBusTrackingVerbosity.Summarised
            ? $"[{messageList.Count} messages]"
            : null;
        var (reqId, traceId) = _tracker.LogRequest(op, content);
        try
        {
            await _inner.SendMessagesAsync(messageList, cancellationToken);
            _tracker.LogResponse(op, reqId, traceId, null);
        }
        catch (Exception ex)
        {
            _tracker.LogResponse(op, reqId, traceId, ex.Message);
            throw;
        }
    }

    public override async Task SendMessagesAsync(
        ServiceBusMessageBatch messageBatch, CancellationToken cancellationToken = default)
        => await _inner.SendMessagesAsync(messageBatch, cancellationToken);

    public override async Task<long> ScheduleMessageAsync(
        ServiceBusMessage message, DateTimeOffset scheduledEnqueueTime, CancellationToken cancellationToken = default)
    {
        InjectTestIdentityProperties(message);
        var op = ServiceBusOperationClassifier.Classify("ScheduleMessageAsync", EntityPath, null);
        var content = GetMessageContent(message);
        var (reqId, traceId) = _tracker.LogRequest(op, content);
        try
        {
            var result = await _inner.ScheduleMessageAsync(message, scheduledEnqueueTime, cancellationToken);
            _tracker.LogResponse(op, reqId, traceId, $"SequenceNumber: {result}");
            return result;
        }
        catch (Exception ex)
        {
            _tracker.LogResponse(op, reqId, traceId, ex.Message);
            throw;
        }
    }

    public override async Task<IReadOnlyList<long>> ScheduleMessagesAsync(
        IEnumerable<ServiceBusMessage> messages, DateTimeOffset scheduledEnqueueTime,
        CancellationToken cancellationToken = default)
        => await _inner.ScheduleMessagesAsync(messages, scheduledEnqueueTime, cancellationToken);

    public override async Task CancelScheduledMessageAsync(long sequenceNumber, CancellationToken cancellationToken = default)
    {
        var op = ServiceBusOperationClassifier.Classify("CancelScheduledMessageAsync", EntityPath, null);
        var (reqId, traceId) = _tracker.LogRequest(op, $"SequenceNumber: {sequenceNumber}");
        try
        {
            await _inner.CancelScheduledMessageAsync(sequenceNumber, cancellationToken);
            _tracker.LogResponse(op, reqId, traceId, null);
        }
        catch (Exception ex)
        {
            _tracker.LogResponse(op, reqId, traceId, ex.Message);
            throw;
        }
    }

    public override async Task CancelScheduledMessagesAsync(
        IEnumerable<long> sequenceNumbers, CancellationToken cancellationToken = default)
        => await _inner.CancelScheduledMessagesAsync(sequenceNumbers, cancellationToken);

    public override async ValueTask<ServiceBusMessageBatch> CreateMessageBatchAsync(CancellationToken cancellationToken = default)
        => await _inner.CreateMessageBatchAsync(cancellationToken);

    public override async ValueTask<ServiceBusMessageBatch> CreateMessageBatchAsync(
        CreateMessageBatchOptions options, CancellationToken cancellationToken = default)
        => await _inner.CreateMessageBatchAsync(options, cancellationToken);

    public override async Task CloseAsync(CancellationToken cancellationToken = default)
        => await _inner.CloseAsync(cancellationToken);

    private string? GetMessageContent(ServiceBusMessage message)
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

    private void InjectTestIdentityProperties(ServiceBusMessage message)
    {
        if (!_options.PropagateTestIdentity) return;

        var testInfo = TestInfoResolver.ResolveWithSource(_options.HttpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null) return;

        message.ApplicationProperties[TestTrackingMessageHeaders.TestName] = testInfo.Value.Name;
        message.ApplicationProperties[TestTrackingMessageHeaders.TestId] = testInfo.Value.Id;
    }
}
