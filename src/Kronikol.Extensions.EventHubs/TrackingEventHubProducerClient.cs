using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using Kronikol.Constants;
using Kronikol.Tracking;

namespace Kronikol.Extensions.EventHubs;

/// <summary>
/// An <see cref="EventHubProducerClient"/> subclass that intercepts send operations
/// for test diagram tracking.
/// <para>
/// <b>Note:</b> The base class properties (<see cref="EventHubName"/>, <see cref="FullyQualifiedNamespace"/>,
/// <see cref="IsClosed"/>) are not virtual in the Azure SDK and are shadowed with <c>new</c>.
/// They work correctly when accessed through the <see cref="TrackingEventHubProducerClient"/> type,
/// but accessing them through a polymorphic <see cref="EventHubProducerClient"/> reference will
/// access the base implementation which may throw if no real connection was established.
/// </para>
/// </summary>
public class TrackingEventHubProducerClient : EventHubProducerClient
{
    private readonly EventHubProducerClient _inner;
    private readonly EventHubsTracker _tracker;
    private readonly EventHubsTrackingOptions _options;

    public TrackingEventHubProducerClient(
        EventHubProducerClient inner, EventHubsTrackingOptions options) : base()
    {
        _inner = inner;
        _tracker = new EventHubsTracker(options, options.HttpContextAccessor);
        _options = options;
    }

    /// <summary>The underlying real <see cref="EventHubProducerClient"/>.</summary>
    public EventHubProducerClient Inner => _inner;

    public new string EventHubName => _inner.EventHubName;
    public new string FullyQualifiedNamespace => _inner.FullyQualifiedNamespace;
    public new bool IsClosed => _inner.IsClosed;

    public override async Task SendAsync(
        IEnumerable<EventData> eventBatch,
        CancellationToken cancellationToken = default)
    {
        var events = eventBatch.ToList();
        InjectTestIdentityProperties(events);
        var op = EventHubsOperationClassifier.Classify(
            "SendAsync", _inner.EventHubName, null, events.Count);
        var content = _options.Verbosity != EventHubsTrackingVerbosity.Summarised
            ? SerializeEvents(events) : null;
        var (reqId, traceId) = _tracker.LogRequest(op, content);

        try
        {
            await _inner.SendAsync(events, cancellationToken);
            _tracker.LogResponse(op, reqId, traceId, null);
        }
        catch (Exception ex)
        {
            _tracker.LogResponse(op, reqId, traceId, ex.Message);
            throw;
        }
    }

    public override async Task SendAsync(
        IEnumerable<EventData> eventBatch,
        SendEventOptions sendOptions,
        CancellationToken cancellationToken = default)
    {
        var events = eventBatch.ToList();
        InjectTestIdentityProperties(events);
        var partitionId = sendOptions?.PartitionId;
        var op = EventHubsOperationClassifier.Classify(
            "SendAsync", _inner.EventHubName, partitionId, events.Count);
        var content = _options.Verbosity != EventHubsTrackingVerbosity.Summarised
            ? SerializeEvents(events) : null;
        var (reqId, traceId) = _tracker.LogRequest(op, content);

        try
        {
            await _inner.SendAsync(events, sendOptions, cancellationToken);
            _tracker.LogResponse(op, reqId, traceId, null);
        }
        catch (Exception ex)
        {
            _tracker.LogResponse(op, reqId, traceId, ex.Message);
            throw;
        }
    }

    public override async Task SendAsync(
        EventDataBatch eventBatch,
        CancellationToken cancellationToken = default)
    {
        var op = EventHubsOperationClassifier.Classify(
            "SendAsync", _inner.EventHubName, null, eventBatch.Count);
        var (reqId, traceId) = _tracker.LogRequest(op, null);

        try
        {
            await _inner.SendAsync(eventBatch, cancellationToken);
            _tracker.LogResponse(op, reqId, traceId, null);
        }
        catch (Exception ex)
        {
            _tracker.LogResponse(op, reqId, traceId, ex.Message);
            throw;
        }
    }

    public override async ValueTask<EventDataBatch> CreateBatchAsync(
        CancellationToken cancellationToken = default)
        => await _inner.CreateBatchAsync(cancellationToken);

    public override async ValueTask<EventDataBatch> CreateBatchAsync(
        CreateBatchOptions options,
        CancellationToken cancellationToken = default)
        => await _inner.CreateBatchAsync(options, cancellationToken);

    public override async Task<EventHubProperties> GetEventHubPropertiesAsync(
        CancellationToken cancellationToken = default)
        => await _inner.GetEventHubPropertiesAsync(cancellationToken);

    public override async Task<string[]> GetPartitionIdsAsync(
        CancellationToken cancellationToken = default)
        => await _inner.GetPartitionIdsAsync(cancellationToken);

    public override async Task<PartitionProperties> GetPartitionPropertiesAsync(
        string partitionId, CancellationToken cancellationToken = default)
        => await _inner.GetPartitionPropertiesAsync(partitionId, cancellationToken);

    public override async Task CloseAsync(CancellationToken cancellationToken = default)
        => await _inner.CloseAsync(cancellationToken);

    private static string? SerializeEvents(IList<EventData> events)
    {
        if (events.Count == 0) return null;
        if (events.Count == 1) return events[0].EventBody?.ToString();
        return $"[{string.Join(", ", events.Select(e => e.EventBody?.ToString() ?? "null"))}]";
    }

    private void InjectTestIdentityProperties(IList<EventData> events)
    {
        if (!_options.PropagateTestIdentity) return;

        var testInfo = TestInfoResolver.ResolveWithSource(_options.HttpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null) return;

        foreach (var evt in events)
        {
            evt.Properties[TestTrackingMessageHeaders.TestName] = testInfo.Value.Name;
            evt.Properties[TestTrackingMessageHeaders.TestId] = testInfo.Value.Id;
        }
    }
}
