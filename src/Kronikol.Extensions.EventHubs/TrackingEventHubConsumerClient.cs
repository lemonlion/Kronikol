using System.Runtime.CompilerServices;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Kronikol.Constants;
using Kronikol.Tracking;

namespace Kronikol.Extensions.EventHubs;

/// <summary>
/// An <see cref="EventHubConsumerClient"/> subclass that intercepts read operations
/// for test diagram tracking.
/// <para>
/// <b>Note:</b> The base class properties (<see cref="EventHubName"/>, <see cref="ConsumerGroup"/>,
/// <see cref="FullyQualifiedNamespace"/>) are not virtual and are shadowed with <c>new</c>.
/// See property-level remarks on <see cref="TrackingEventHubProducerClient"/> for limitations.
/// </para>
/// </summary>
public class TrackingEventHubConsumerClient : EventHubConsumerClient
{
    private readonly EventHubConsumerClient _inner;
    private readonly EventHubsTracker _tracker;
    private readonly EventHubsTrackingOptions _options;

    public TrackingEventHubConsumerClient(
        EventHubConsumerClient inner, EventHubsTrackingOptions options) : base()
    {
        _inner = inner;
        _tracker = new EventHubsTracker(options, options.HttpContextAccessor);
        _options = options;
    }

    /// <summary>The underlying real <see cref="EventHubConsumerClient"/>.</summary>
    public EventHubConsumerClient Inner => _inner;

    public new string EventHubName => _inner.EventHubName;
    public new string ConsumerGroup => _inner.ConsumerGroup;
    public new string FullyQualifiedNamespace => _inner.FullyQualifiedNamespace;
    public new bool IsClosed => _inner.IsClosed;

    public override async IAsyncEnumerable<PartitionEvent> ReadEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var op = EventHubsOperationClassifier.Classify(
            "ReadEventsAsync", _inner.EventHubName);
        var (reqId, traceId) = _tracker.LogRequest(op, null);

        var eventCount = 0;
        await foreach (var partitionEvent in _inner.ReadEventsAsync(cancellationToken))
        {
            eventCount++;
            EstablishTestIdentityFromProperties(partitionEvent.Data);
            yield return partitionEvent;
        }

        _tracker.LogResponse(op, reqId, traceId,
            _options.Verbosity == EventHubsTrackingVerbosity.Raw ? $"Events: {eventCount}" : null);
    }

    public override async IAsyncEnumerable<PartitionEvent> ReadEventsAsync(
        ReadEventOptions readOptions,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var op = EventHubsOperationClassifier.Classify(
            "ReadEventsAsync", _inner.EventHubName);
        var (reqId, traceId) = _tracker.LogRequest(op, null);

        var eventCount = 0;
        await foreach (var partitionEvent in _inner.ReadEventsAsync(readOptions, cancellationToken))
        {
            eventCount++;
            EstablishTestIdentityFromProperties(partitionEvent.Data);
            yield return partitionEvent;
        }

        _tracker.LogResponse(op, reqId, traceId,
            _options.Verbosity == EventHubsTrackingVerbosity.Raw ? $"Events: {eventCount}" : null);
    }

    public override async IAsyncEnumerable<PartitionEvent> ReadEventsFromPartitionAsync(
        string partitionId, EventPosition startingPosition,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var op = EventHubsOperationClassifier.Classify(
            "ReadEventsFromPartitionAsync", _inner.EventHubName, partitionId);
        var (reqId, traceId) = _tracker.LogRequest(op, null);

        await foreach (var partitionEvent in _inner.ReadEventsFromPartitionAsync(
            partitionId, startingPosition, cancellationToken))
        {
            EstablishTestIdentityFromProperties(partitionEvent.Data);
            yield return partitionEvent;
        }

        _tracker.LogResponse(op, reqId, traceId, null);
    }

    public override async IAsyncEnumerable<PartitionEvent> ReadEventsFromPartitionAsync(
        string partitionId, EventPosition startingPosition, ReadEventOptions readOptions,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var op = EventHubsOperationClassifier.Classify(
            "ReadEventsFromPartitionAsync", _inner.EventHubName, partitionId);
        var (reqId, traceId) = _tracker.LogRequest(op, null);

        await foreach (var partitionEvent in _inner.ReadEventsFromPartitionAsync(
            partitionId, startingPosition, readOptions, cancellationToken))
        {
            EstablishTestIdentityFromProperties(partitionEvent.Data);
            yield return partitionEvent;
        }

        _tracker.LogResponse(op, reqId, traceId, null);
    }

    public override async Task<string[]> GetPartitionIdsAsync(
        CancellationToken cancellationToken = default)
        => await _inner.GetPartitionIdsAsync(cancellationToken);

    public override async Task<EventHubProperties> GetEventHubPropertiesAsync(
        CancellationToken cancellationToken = default)
        => await _inner.GetEventHubPropertiesAsync(cancellationToken);

    public override async Task<PartitionProperties> GetPartitionPropertiesAsync(
        string partitionId, CancellationToken cancellationToken = default)
        => await _inner.GetPartitionPropertiesAsync(partitionId, cancellationToken);

    public override async Task CloseAsync(CancellationToken cancellationToken = default)
        => await _inner.CloseAsync(cancellationToken);

    private void EstablishTestIdentityFromProperties(EventData? eventData)
    {
        if (!_options.PropagateTestIdentity) return;
        TestIdentityScope.ClearMessageIdentity();
        if (eventData?.Properties is null) return;

        if (eventData.Properties.TryGetValue(TestTrackingMessageHeaders.TestName, out var nameObj) &&
            eventData.Properties.TryGetValue(TestTrackingMessageHeaders.TestId, out var idObj) &&
            nameObj is string testName && idObj is string testId)
        {
            TestIdentityScope.SetFromMessage(testName, testId);
            AutoCorrelateOnConsume(eventData, testName, testId);
        }
    }

    private void AutoCorrelateOnConsume(EventData eventData, string testName, string testId)
    {
        if (!_options.AutoCorrelateOnConsume) return;

        var eventId = eventData.MessageId ?? eventData.SequenceNumber.ToString();
        var key = CorrelationKeys.EventHubs(_options.ServiceName, eventId);
        TestCorrelationStore.Correlate(key, testName, testId);
    }
}
