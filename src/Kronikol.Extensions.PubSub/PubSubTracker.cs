using Kronikol.Constants;
using Microsoft.AspNetCore.Http;
using Kronikol.Tracking;

namespace Kronikol.Extensions.PubSub;

/// <summary>
/// Central logging component for PubSub operations. Implements <see cref="ITrackingComponent" /> with auto-registration.
/// </summary>
public class PubSubTracker : ITrackingComponent
{
    private readonly PubSubTrackingOptions _options;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private int _invocationCount;

    public PubSubTracker(PubSubTrackingOptions options, IHttpContextAccessor? httpContextAccessor = null)
    {
        _options = options;
        _httpContextAccessor = httpContextAccessor;
        TrackingComponentRegistry.Register(this);
    }

    public string ComponentName => $"PubSubTracker ({_options.ServiceName})";
    public bool WasInvoked => _invocationCount > 0;
    public int InvocationCount => _invocationCount;
    public bool HasHttpContextAccessor => _httpContextAccessor is not null;

    public (Guid RequestResponseId, Guid TraceId) LogRequest(
        PubSubOperationInfo operation, string? content)
    {
        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction)) return (Guid.Empty, Guid.Empty);

        Interlocked.Increment(ref _invocationCount);

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null) return (Guid.Empty, Guid.Empty);

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var uri = BuildUri(operation, effectiveVerbosity);
        var label = PubSubOperationClassifier.GetDiagramLabel(operation, effectiveVerbosity);
        var traceId = Guid.NewGuid();
        var requestResponseId = Guid.NewGuid();

        var logContent = effectiveVerbosity == PubSubTrackingVerbosity.Summarised ? null : content;

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
                PubSubOperationClassifier.GetDiagramLabel(operation, v),
                BuildUri(operation, v),
                v == PubSubTrackingVerbosity.Summarised ? null : content,
                [], false)));

        return (requestResponseId, traceId);
    }

    public void LogResponse(
        PubSubOperationInfo operation,
        Guid requestResponseId, Guid traceId, string? content)
    {
        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction)) return;

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null) return;

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var uri = BuildUri(operation, effectiveVerbosity);
        var label = PubSubOperationClassifier.GetDiagramLabel(operation, effectiveVerbosity);

        var logContent = effectiveVerbosity == PubSubTrackingVerbosity.Summarised ? null : content;

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
                PubSubOperationClassifier.GetDiagramLabel(operation, v),
                BuildUri(operation, v),
                v == PubSubTrackingVerbosity.Summarised ? null : content,
                [], false)));
    }

    private Uri BuildUri(PubSubOperationInfo op, PubSubTrackingVerbosity effectiveVerbosity)
    {
        var topic = ShortName(op.TopicName);
        var sub = ShortName(op.SubscriptionName);
        var name = topic ?? sub ?? "unknown";

        return effectiveVerbosity switch
        {
            PubSubTrackingVerbosity.Raw =>
                new Uri($"pubsub:///{op.TopicName ?? op.SubscriptionName ?? "unknown"}"),
            _ =>
                new Uri($"pubsub:///{name}")
        };
    }

    private static string? ShortName(string? fullName) =>
        fullName?.Split('/').LastOrDefault() ?? fullName;
}
