using Kronikol.Constants;
using Microsoft.AspNetCore.Http;
using Kronikol.Tracking;

namespace Kronikol.Extensions.ServiceBus;

/// <summary>
/// Central logging component for ServiceBus operations. Implements <see cref="ITrackingComponent" /> with auto-registration.
/// </summary>
public class ServiceBusTracker : ITrackingComponent
{
    private readonly ServiceBusTrackingOptions _options;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private int _invocationCount;

    public ServiceBusTracker(ServiceBusTrackingOptions options, IHttpContextAccessor? httpContextAccessor = null)
    {
        _options = options;
        _httpContextAccessor = httpContextAccessor ?? options.HttpContextAccessor;
        TrackingComponentRegistry.Register(this);
    }

    public string ComponentName => $"ServiceBusTracker ({_options.ServiceName})";
    public bool WasInvoked => _invocationCount > 0;
    public int InvocationCount => _invocationCount;

    public (Guid RequestResponseId, Guid TraceId) LogRequest(
        ServiceBusOperationInfo operation, string? content)
    {
        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction)) return (Guid.Empty, Guid.Empty);

        Interlocked.Increment(ref _invocationCount);

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null)
            return (Guid.Empty, Guid.Empty);

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var requestResponseId = Guid.NewGuid();
        var traceId = Guid.NewGuid();

        var label = ServiceBusOperationClassifier.GetDiagramLabel(operation, effectiveVerbosity);
        var uri = BuildUri(operation, effectiveVerbosity);

        OneOf<System.Net.Http.HttpMethod, string> method = label;

        var metaType = operation.Operation is
            ServiceBusOperation.Send or ServiceBusOperation.SendBatch or
            ServiceBusOperation.Receive or ServiceBusOperation.ReceiveBatch or
            ServiceBusOperation.Schedule or ServiceBusOperation.Peek
            ? RequestResponseMetaType.Event
            : RequestResponseMetaType.Default;

        RequestResponseLogger.Log(new RequestResponseLog(
            testInfo.Value.Name,
            testInfo.Value.Id,
            method,
            effectiveVerbosity == ServiceBusTrackingVerbosity.Summarised ? null : content,
            uri,
            [],
            _options.ServiceName,
            _options.CallerName,
            RequestResponseType.Request,
            traceId,
            requestResponseId,
            false,
            MetaType: metaType,
            DependencyCategory: DependencyCategories.ServiceBus
        )
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                ServiceBusOperationClassifier.GetDiagramLabel(operation, v),
                BuildUri(operation, v),
                v == ServiceBusTrackingVerbosity.Summarised ? null : content,
                [], false)));

        return (requestResponseId, traceId);
    }

    public void LogResponse(
        ServiceBusOperationInfo operation,
        Guid requestResponseId, Guid traceId, string? content)
    {
        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction)) return;

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null)
            return;

        if (requestResponseId == Guid.Empty)
            return;

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var label = ServiceBusOperationClassifier.GetDiagramLabel(operation, effectiveVerbosity);
        var uri = BuildUri(operation, effectiveVerbosity);

        OneOf<System.Net.Http.HttpMethod, string> method = label;

        var metaType = operation.Operation is
            ServiceBusOperation.Send or ServiceBusOperation.SendBatch or
            ServiceBusOperation.Receive or ServiceBusOperation.ReceiveBatch or
            ServiceBusOperation.Schedule or ServiceBusOperation.Peek
            ? RequestResponseMetaType.Event
            : RequestResponseMetaType.Default;

        RequestResponseLogger.Log(new RequestResponseLog(
            testInfo.Value.Name,
            testInfo.Value.Id,
            method,
            effectiveVerbosity == ServiceBusTrackingVerbosity.Summarised ? null : content,
            uri,
            [],
            _options.ServiceName,
            _options.CallerName,
            RequestResponseType.Response,
            traceId,
            requestResponseId,
            false,
            MetaType: metaType,
            DependencyCategory: DependencyCategories.ServiceBus
        )
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                ServiceBusOperationClassifier.GetDiagramLabel(operation, v),
                BuildUri(operation, v),
                v == ServiceBusTrackingVerbosity.Summarised ? null : content,
                [], false)));
    }

    private Uri BuildUri(ServiceBusOperationInfo op, ServiceBusTrackingVerbosity effectiveVerbosity)
    {
        var queue = op.QueueOrTopicName ?? "unknown";
        var sub = op.SubscriptionName;

        return effectiveVerbosity switch
        {
            ServiceBusTrackingVerbosity.Summarised => new Uri($"servicebus://{queue}/"),
            ServiceBusTrackingVerbosity.Detailed => sub is not null
                ? new Uri($"servicebus://{queue}/{sub}")
                : new Uri($"servicebus://{queue}"),
            _ => sub is not null
                ? new Uri($"servicebus://{queue}/{sub}")
                : new Uri($"servicebus://{queue}")
        };
    }
}
