using Azure.Messaging.ServiceBus;

namespace Kronikol.Extensions.ServiceBus;

/// <summary>
/// A <see cref="ServiceBusClient"/> subclass that wraps a real client and creates
/// tracking-enabled senders and receivers for test diagram tracking.
/// </summary>
public class TrackingServiceBusClient : ServiceBusClient
{
    private readonly ServiceBusClient _inner;
    private readonly ServiceBusTracker _tracker;
    private readonly ServiceBusTrackingOptions _options;

    public TrackingServiceBusClient(ServiceBusClient inner, ServiceBusTrackingOptions options) : base()
    {
        _inner = inner;
        _options = options;
        _tracker = new ServiceBusTracker(options, options.HttpContextAccessor);
    }

    /// <summary>The underlying real <see cref="ServiceBusClient"/>.</summary>
    public ServiceBusClient Inner => _inner;

    internal ServiceBusTracker Tracker => _tracker;

    public override string FullyQualifiedNamespace => _inner.FullyQualifiedNamespace;
    public override bool IsClosed => _inner.IsClosed;
    public override string Identifier => _inner.Identifier;

    public override ServiceBusSender CreateSender(string queueOrTopicName)
    {
        var sender = _inner.CreateSender(queueOrTopicName);
        return new TrackingServiceBusSender(sender, _tracker, _options);
    }

    public override ServiceBusSender CreateSender(string queueOrTopicName, ServiceBusSenderOptions options)
    {
        var sender = _inner.CreateSender(queueOrTopicName, options);
        return new TrackingServiceBusSender(sender, _tracker, _options);
    }

    public override ServiceBusReceiver CreateReceiver(string queueName)
    {
        var receiver = _inner.CreateReceiver(queueName);
        return new TrackingServiceBusReceiver(receiver, _tracker, _options);
    }

    public override ServiceBusReceiver CreateReceiver(string queueName, ServiceBusReceiverOptions receiverOptions)
    {
        var receiver = _inner.CreateReceiver(queueName, receiverOptions);
        return new TrackingServiceBusReceiver(receiver, _tracker, _options);
    }

    public override ServiceBusReceiver CreateReceiver(string topicName, string subscriptionName)
    {
        var receiver = _inner.CreateReceiver(topicName, subscriptionName);
        return new TrackingServiceBusReceiver(receiver, _tracker, _options);
    }

    public override ServiceBusReceiver CreateReceiver(
        string topicName, string subscriptionName, ServiceBusReceiverOptions receiverOptions)
    {
        var receiver = _inner.CreateReceiver(topicName, subscriptionName, receiverOptions);
        return new TrackingServiceBusReceiver(receiver, _tracker, _options);
    }

    // The overloads without options, and the rule manager, would otherwise run against the empty client the base was
    // built as (the constructor for mocking); they come from the client this one holds, untracked as the others are.
    public override ServiceBusProcessor CreateProcessor(string queueName) => _inner.CreateProcessor(queueName);

    public override ServiceBusProcessor CreateProcessor(string topicName, string subscriptionName)
        => _inner.CreateProcessor(topicName, subscriptionName);

    public override ServiceBusRuleManager CreateRuleManager(string topicName, string subscriptionName)
        => _inner.CreateRuleManager(topicName, subscriptionName);

    public override ServiceBusProcessor CreateProcessor(string queueName, ServiceBusProcessorOptions? options = null)
        => _inner.CreateProcessor(queueName, options);

    public override ServiceBusProcessor CreateProcessor(
        string topicName, string subscriptionName, ServiceBusProcessorOptions? options = null)
        => _inner.CreateProcessor(topicName, subscriptionName, options);

    public override ServiceBusSessionProcessor CreateSessionProcessor(
        string queueName, ServiceBusSessionProcessorOptions? options = null)
        => _inner.CreateSessionProcessor(queueName, options);

    public override ServiceBusSessionProcessor CreateSessionProcessor(
        string topicName, string subscriptionName, ServiceBusSessionProcessorOptions? options = null)
        => _inner.CreateSessionProcessor(topicName, subscriptionName, options);

    public override async Task<ServiceBusSessionReceiver> AcceptNextSessionAsync(
        string queueName, ServiceBusSessionReceiverOptions? options = null,
        CancellationToken cancellationToken = default)
        => await _inner.AcceptNextSessionAsync(queueName, options, cancellationToken);

    public override async Task<ServiceBusSessionReceiver> AcceptNextSessionAsync(
        string topicName, string subscriptionName,
        ServiceBusSessionReceiverOptions? options = null,
        CancellationToken cancellationToken = default)
        => await _inner.AcceptNextSessionAsync(topicName, subscriptionName, options, cancellationToken);

    public override async Task<ServiceBusSessionReceiver> AcceptSessionAsync(
        string queueName, string sessionId,
        ServiceBusSessionReceiverOptions? options = null,
        CancellationToken cancellationToken = default)
        => await _inner.AcceptSessionAsync(queueName, sessionId, options, cancellationToken);

    public override async Task<ServiceBusSessionReceiver> AcceptSessionAsync(
        string topicName, string subscriptionName, string sessionId,
        ServiceBusSessionReceiverOptions? options = null,
        CancellationToken cancellationToken = default)
        => await _inner.AcceptSessionAsync(topicName, subscriptionName, sessionId, options, cancellationToken);

    public override async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
