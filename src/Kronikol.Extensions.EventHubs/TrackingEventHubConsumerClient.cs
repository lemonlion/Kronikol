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
/// <b>Note:</b> The SDK client's <see cref="EventHubConsumerClient.EventHubName"/>,
/// <see cref="EventHubConsumerClient.ConsumerGroup"/>, <see cref="EventHubConsumerClient.FullyQualifiedNamespace"/>,
/// <see cref="EventHubConsumerClient.Identifier"/> and <see cref="EventHubConsumerClient.IsClosed"/> are not virtual.
/// The client this derives from is built with the names and identifier of the client it wraps and is closed with it,
/// so they read the same through an <see cref="EventHubConsumerClient"/> reference. That client never connects: each
/// call goes to <see cref="Inner"/>.
/// </para>
/// </summary>
public class TrackingEventHubConsumerClient : EventHubConsumerClient
{
    private readonly EventHubConsumerClient _inner;
    private readonly EventHubsTracker _tracker;
    private readonly EventHubsTrackingOptions _options;

    public TrackingEventHubConsumerClient(
        EventHubConsumerClient inner, EventHubsTrackingOptions options) : this(inner, options, UnconnectedBase.NamesOf(inner))
    {
    }

    private TrackingEventHubConsumerClient(
        EventHubConsumerClient inner, EventHubsTrackingOptions options,
        (string ConsumerGroup, string Namespace, string EventHub, string? Identifier) names)
        : base(names.ConsumerGroup, names.Namespace, names.EventHub, UnconnectedBase.Credential,
            new EventHubConsumerClientOptions { Identifier = names.Identifier })
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

    public override IAsyncEnumerable<PartitionEvent> ReadEventsAsync(
        CancellationToken cancellationToken = default)
        => Track("ReadEventsAsync", null, token => _inner.ReadEventsAsync(token), cancellationToken);

    public override IAsyncEnumerable<PartitionEvent> ReadEventsAsync(
        ReadEventOptions readOptions,
        CancellationToken cancellationToken = default)
        => Track("ReadEventsAsync", null, token => _inner.ReadEventsAsync(readOptions, token), cancellationToken);

    public override IAsyncEnumerable<PartitionEvent> ReadEventsAsync(
        bool startReadingAtEarliestEvent, ReadEventOptions? readOptions = null,
        CancellationToken cancellationToken = default)
        => Track("ReadEventsAsync", null,
            token => _inner.ReadEventsAsync(startReadingAtEarliestEvent, readOptions, token), cancellationToken);

    public override IAsyncEnumerable<PartitionEvent> ReadEventsFromPartitionAsync(
        string partitionId, EventPosition startingPosition,
        CancellationToken cancellationToken = default)
        => Track("ReadEventsFromPartitionAsync", partitionId,
            token => _inner.ReadEventsFromPartitionAsync(partitionId, startingPosition, token), cancellationToken);

    public override IAsyncEnumerable<PartitionEvent> ReadEventsFromPartitionAsync(
        string partitionId, EventPosition startingPosition, ReadEventOptions readOptions,
        CancellationToken cancellationToken = default)
        => Track("ReadEventsFromPartitionAsync", partitionId,
            token => _inner.ReadEventsFromPartitionAsync(partitionId, startingPosition, readOptions, token), cancellationToken);

    public override async Task<string[]> GetPartitionIdsAsync(
        CancellationToken cancellationToken = default)
        => await _inner.GetPartitionIdsAsync(cancellationToken);

    public override async Task<EventHubProperties> GetEventHubPropertiesAsync(
        CancellationToken cancellationToken = default)
        => await _inner.GetEventHubPropertiesAsync(cancellationToken);

    public override async Task<PartitionProperties> GetPartitionPropertiesAsync(
        string partitionId, CancellationToken cancellationToken = default)
        => await _inner.GetPartitionPropertiesAsync(partitionId, cancellationToken);

    // The client this derives from is closed with the one it wraps, as code holding this as the SDK type reads its
    // IsClosed. Given the caller's token, a close that is cancelled leaves both open.
    public override async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _inner.CloseAsync(cancellationToken);
        }
        finally
        {
            await base.CloseAsync(cancellationToken);
        }
    }

    // Nothing is read before the events are enumerated, so nothing is recorded before: the request when enumeration
    // starts, and the response, with the count of events read, when it ends or its caller leaves it, or the failure
    // when a read fails.
    private async IAsyncEnumerable<PartitionEvent> Track(
        string operation, string? partitionId, Func<CancellationToken, IAsyncEnumerable<PartitionEvent>> read,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var op = EventHubsOperationClassifier.Classify(operation, _inner.EventHubName, partitionId);
        var (reqId, traceId) = _tracker.LogRequest(op, null);

        var eventCount = 0;
        var failed = false;
        await using var events = read(cancellationToken).GetAsyncEnumerator(cancellationToken);
        try
        {
            while (true)
            {
                bool more;
                try
                {
                    more = await events.MoveNextAsync();
                }
                catch (Exception ex)
                {
                    failed = true;
                    _tracker.LogResponse(op, reqId, traceId, ex.Message);
                    throw;
                }
                if (!more)
                    break;
                eventCount++;
                EstablishTestIdentityFromProperties(events.Current.Data);
                yield return events.Current;
            }
        }
        finally
        {
            if (!failed)
                _tracker.LogResponse(op, reqId, traceId, content: null, rawContent: $"Events: {eventCount}");
        }
    }

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
