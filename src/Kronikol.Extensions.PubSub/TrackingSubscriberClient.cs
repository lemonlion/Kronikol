using Google.Cloud.PubSub.V1;
using Microsoft.AspNetCore.Http;
using Kronikol.Constants;
using Kronikol.Tracking;

namespace Kronikol.Extensions.PubSub;

/// <summary>
/// A <see cref="SubscriberClient"/> subclass that intercepts message processing
/// for test diagram tracking.
/// </summary>
public class TrackingSubscriberClient : SubscriberClient
{
    private readonly SubscriberClient _inner;
    private readonly PubSubTracker _tracker;
    private readonly PubSubTrackingOptions _options;

    public TrackingSubscriberClient(
        SubscriberClient inner, PubSubTrackingOptions options,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _inner = inner;
        _tracker = new PubSubTracker(options, httpContextAccessor ?? options.HttpContextAccessor);
        _options = options;
    }

    /// <summary>The underlying real <see cref="SubscriberClient"/>.</summary>
    public SubscriberClient Inner => _inner;

    public override SubscriptionName SubscriptionName => _inner.SubscriptionName;

    public override Task StartAsync(Func<PubsubMessage, CancellationToken, Task<Reply>> messageHandler)
    {
        async Task<Reply> wrappedHandler(PubsubMessage msg, CancellationToken ct)
        {
            // A scope, not a sticky set: the wrapper owns the handler, so the identity lasts exactly as
            // long as the message is being handled and the subscriber's own loop never carries it.
            using var identity = EstablishTestIdentityFromAttributes(msg);

            var op = PubSubOperationClassifier.Classify(
                "Receive", null, _inner.SubscriptionName?.ToString(), 1);
            var content = _options.Verbosity != PubSubTrackingVerbosity.Summarised
                ? msg.Data?.ToStringUtf8() : null;
            var (reqId, traceId) = _tracker.LogRequest(op, content);

            var reply = await messageHandler(msg, ct);

            var replyLabel = reply == Reply.Ack ? "Ack" : "Nack";
            _tracker.LogResponse(op, reqId, traceId,
                _options.Verbosity == PubSubTrackingVerbosity.Raw ? replyLabel : null);

            return reply;
        }

        return _inner.StartAsync(wrappedHandler);
    }

    public override Task StopAsync(CancellationToken cancellationToken) => _inner.StopAsync(cancellationToken);
    public override ValueTask DisposeAsync() => _inner.DisposeAsync();

    private IDisposable? EstablishTestIdentityFromAttributes(PubsubMessage message)
    {
        if (!_options.PropagateTestIdentity) return null;

        if (message.Attributes.TryGetValue(TestTrackingMessageHeaders.TestName, out var testName) &&
            message.Attributes.TryGetValue(TestTrackingMessageHeaders.TestId, out var testId) &&
            !string.IsNullOrEmpty(testName) && !string.IsNullOrEmpty(testId))
        {
            AutoCorrelateOnConsume(message.MessageId, testName, testId);
            return TestIdentityScope.Begin(testName, testId);
        }

        return null;
    }

    private void AutoCorrelateOnConsume(string? messageId, string testName, string testId)
    {
        if (!_options.AutoCorrelateOnConsume) return;
        if (string.IsNullOrEmpty(messageId)) return;

        var key = CorrelationKeys.PubSub(_options.ServiceName, messageId);
        TestCorrelationStore.Correlate(key, testName, testId);
    }
}
