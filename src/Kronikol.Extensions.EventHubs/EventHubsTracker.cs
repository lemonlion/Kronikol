using Kronikol.Constants;
using Microsoft.AspNetCore.Http;
using Kronikol.Tracking;

namespace Kronikol.Extensions.EventHubs;

/// <summary>
/// Central logging component for EventHubs operations. Implements <see cref="ITrackingComponent" /> with auto-registration.
/// </summary>
public class EventHubsTracker : ITrackingComponent
{
    private readonly EventHubsTrackingOptions _options;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private int _invocationCount;

    public EventHubsTracker(EventHubsTrackingOptions options, IHttpContextAccessor? httpContextAccessor = null)
    {
        _options = options;
        _httpContextAccessor = httpContextAccessor ?? options.HttpContextAccessor;
        TrackingComponentRegistry.Register(this);
    }

    public string ComponentName => $"EventHubsTracker ({_options.ServiceName})";
    public bool WasInvoked => _invocationCount > 0;
    public int InvocationCount => _invocationCount;
    public bool HasHttpContextAccessor => _httpContextAccessor is not null;

    public (Guid RequestResponseId, Guid TraceId) LogRequest(
        EventHubsOperationInfo operation, string? content)
    {
        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction)) return (Guid.Empty, Guid.Empty);

        Interlocked.Increment(ref _invocationCount);

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null) return (Guid.Empty, Guid.Empty);

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var uri = BuildUri(operation, effectiveVerbosity);
        var label = EventHubsOperationClassifier.GetDiagramLabel(operation, effectiveVerbosity);
        var traceId = Guid.NewGuid();
        var requestResponseId = Guid.NewGuid();

        var logContent = effectiveVerbosity == EventHubsTrackingVerbosity.Summarised ? null : content;

        RequestResponseLogger.Log(new RequestResponseLog(
            testInfo.Value.Name, testInfo.Value.Id,
            label, logContent, uri, [],
            _options.ServiceName, _options.CallerName,
            RequestResponseType.Request, traceId, requestResponseId, false,
            MetaType: RequestResponseMetaType.Event,
            DependencyCategory: DependencyCategories.MessageQueue)
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                EventHubsOperationClassifier.GetDiagramLabel(operation, v),
                BuildUri(operation, v),
                v == EventHubsTrackingVerbosity.Summarised ? null : content,
                [], false)));

        return (requestResponseId, traceId);
    }

    public void LogResponse(
        EventHubsOperationInfo operation,
        Guid requestResponseId, Guid traceId, string? content)
    {
        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction)) return;

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null) return;

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var uri = BuildUri(operation, effectiveVerbosity);
        var label = EventHubsOperationClassifier.GetDiagramLabel(operation, effectiveVerbosity);

        var logContent = effectiveVerbosity == EventHubsTrackingVerbosity.Summarised ? null : content;

        RequestResponseLogger.Log(new RequestResponseLog(
            testInfo.Value.Name, testInfo.Value.Id,
            label, logContent, uri, [],
            _options.ServiceName, _options.CallerName,
            RequestResponseType.Response, traceId, requestResponseId, false,
            DependencyCategory: DependencyCategories.MessageQueue)
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                EventHubsOperationClassifier.GetDiagramLabel(operation, v),
                BuildUri(operation, v),
                v == EventHubsTrackingVerbosity.Summarised ? null : content,
                [], false)));
    }

    private Uri BuildUri(EventHubsOperationInfo op, EventHubsTrackingVerbosity effectiveVerbosity)
    {
        var hub = op.EventHubName ?? "unknown";

        return effectiveVerbosity switch
        {
            EventHubsTrackingVerbosity.Raw when op.PartitionId is not null =>
                new Uri($"eventhubs:///{hub}/{op.PartitionId}"),
            EventHubsTrackingVerbosity.Detailed when op.PartitionId is not null =>
                new Uri($"eventhubs:///{hub}/{op.PartitionId}"),
            _ => new Uri($"eventhubs:///{hub}")
        };
    }
}
