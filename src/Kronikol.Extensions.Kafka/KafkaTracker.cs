using Kronikol.Constants;
using Microsoft.AspNetCore.Http;
using Kronikol.Tracking;

namespace Kronikol.Extensions.Kafka;

/// <summary>
/// Central logging component for Kafka operations. Implements <see cref="ITrackingComponent" /> with auto-registration.
/// </summary>
public class KafkaTracker : ITrackingComponent
{
    private readonly KafkaTrackingOptions _options;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private int _invocationCount;

    public KafkaTracker(KafkaTrackingOptions options, IHttpContextAccessor? httpContextAccessor = null)
    {
        _options = options;
        _httpContextAccessor = httpContextAccessor;
        TrackingComponentRegistry.Register(this);
    }

    public string ComponentName => $"KafkaTracker ({_options.ServiceName})";
    public bool WasInvoked => _invocationCount > 0;
    public int InvocationCount => _invocationCount;
    public bool HasHttpContextAccessor => _httpContextAccessor is not null;

    public void LogProduce(KafkaOperationInfo op, string? content)
    {
        Interlocked.Increment(ref _invocationCount);

        if (!_options.TrackProduce) return;
        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return;
        LogOutgoing(op, content);
    }

    public void LogConsume(KafkaOperationInfo op, string? content)
    {
        Interlocked.Increment(ref _invocationCount);

        if (!_options.TrackConsume) return;
        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return;
        LogIncoming(op, content);
    }

    public void LogSubscribe(KafkaOperationInfo op)
    {
        Interlocked.Increment(ref _invocationCount);

        if (!_options.TrackSubscribe) return;
        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return;
        LogOutgoing(op, null);
    }

    public void LogCommit(KafkaOperationInfo op)
    {
        Interlocked.Increment(ref _invocationCount);

        if (!_options.TrackCommit) return;
        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return;
        LogOutgoing(op, null);
    }

    public void LogUnsubscribe(KafkaOperationInfo op)
    {
        Interlocked.Increment(ref _invocationCount);

        if (!_options.TrackUnsubscribe) return;
        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return;
        LogOutgoing(op, null);
    }

    public void LogFlush(KafkaOperationInfo op)
    {
        Interlocked.Increment(ref _invocationCount);

        if (!_options.TrackFlush) return;
        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return;
        LogOutgoing(op, null);
    }

    public void LogTransaction(KafkaOperationInfo op)
    {
        Interlocked.Increment(ref _invocationCount);

        if (!_options.TrackTransactions) return;
        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return;
        LogOutgoing(op, null);
    }

    private void LogOutgoing(KafkaOperationInfo op, string? content)
    {
        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null) return;

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);
        var label = KafkaOperationClassifier.GetDiagramLabel(op, effectiveVerbosity);
        var uri = KafkaOperationClassifier.BuildUri(op, effectiveVerbosity);

        var body = effectiveVerbosity == KafkaTrackingVerbosity.Summarised ? null : content;

        var traceId = Guid.NewGuid();
        var requestResponseId = Guid.NewGuid();

        RequestResponseLogger.Log(new RequestResponseLog(
            testInfo.Value.Name, testInfo.Value.Id,
            label, body, uri, [],
            _options.ServiceName, _options.CallerName,
            RequestResponseType.Request, traceId, requestResponseId, false,
            MetaType: RequestResponseMetaType.Event,
            DependencyCategory: DependencyCategories.MessageQueue)
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                KafkaOperationClassifier.GetDiagramLabel(op, v),
                KafkaOperationClassifier.BuildUri(op, v),
                v == KafkaTrackingVerbosity.Summarised ? null : content,
                [], false)));

        RequestResponseLogger.Log(new RequestResponseLog(
            testInfo.Value.Name, testInfo.Value.Id,
            label, null, uri, [],
            _options.ServiceName, _options.CallerName,
            RequestResponseType.Response, traceId, requestResponseId, false,
            MetaType: RequestResponseMetaType.Event,
            DependencyCategory: DependencyCategories.MessageQueue)
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                KafkaOperationClassifier.GetDiagramLabel(op, v),
                KafkaOperationClassifier.BuildUri(op, v),
                null, [], false)));
    }

    private void LogIncoming(KafkaOperationInfo op, string? content)
    {
        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null) return;

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);
        var label = KafkaOperationClassifier.GetDiagramLabel(op, effectiveVerbosity);
        var uri = KafkaOperationClassifier.BuildUri(op, effectiveVerbosity);

        var body = effectiveVerbosity == KafkaTrackingVerbosity.Summarised ? null : content;

        var traceId = Guid.NewGuid();
        var requestResponseId = Guid.NewGuid();

        // Consume is incoming: swap caller/service
        RequestResponseLogger.Log(new RequestResponseLog(
            testInfo.Value.Name, testInfo.Value.Id,
            label, body, uri, [],
            _options.CallerName, _options.ServiceName,
            RequestResponseType.Request, traceId, requestResponseId, false,
            MetaType: RequestResponseMetaType.Event,
            DependencyCategory: DependencyCategories.MessageQueue)
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                KafkaOperationClassifier.GetDiagramLabel(op, v),
                KafkaOperationClassifier.BuildUri(op, v),
                v == KafkaTrackingVerbosity.Summarised ? null : content,
                [], false)));

        RequestResponseLogger.Log(new RequestResponseLog(
            testInfo.Value.Name, testInfo.Value.Id,
            label, null, uri, [],
            _options.CallerName, _options.ServiceName,
            RequestResponseType.Response, traceId, requestResponseId, false,
            MetaType: RequestResponseMetaType.Event,
            DependencyCategory: DependencyCategories.MessageQueue)
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                KafkaOperationClassifier.GetDiagramLabel(op, v),
                KafkaOperationClassifier.BuildUri(op, v),
                null, [], false)));
    }
}
