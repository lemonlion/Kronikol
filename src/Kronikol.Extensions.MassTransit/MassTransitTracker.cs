using Kronikol.Constants;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Kronikol.Tracking;

namespace Kronikol.Extensions.MassTransit;

/// <summary>
/// Central logging component for MassTransit operations. Implements <see cref="ITrackingComponent" /> with auto-registration.
/// </summary>
public class MassTransitTracker : ITrackingComponent
{
    private readonly MassTransitTrackingOptions _options;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private int _invocationCount;

    public MassTransitTracker(MassTransitTrackingOptions options, IHttpContextAccessor? httpContextAccessor = null)
    {
        _options = options;
        _httpContextAccessor = httpContextAccessor ?? options.HttpContextAccessor;
        TrackingComponentRegistry.Register(this);
    }

    public string ComponentName => $"MassTransitTracker ({_options.ServiceName})";
    public bool WasInvoked => _invocationCount > 0;
    public int InvocationCount => _invocationCount;
    public bool HasHttpContextAccessor => _httpContextAccessor is not null;

    /// <summary>The accessor this tracker resolves the scenario through: the constructor's, else the options'.</summary>
    internal IHttpContextAccessor? HttpContextAccessor => _httpContextAccessor;

    public void LogSend(MassTransitOperationInfo op, object? message)
    {
        Interlocked.Increment(ref _invocationCount);

        if (!_options.TrackSend) return;
        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return;
        LogOutgoing(op, message);
    }

    public void LogPublish(MassTransitOperationInfo op, object? message)
    {
        Interlocked.Increment(ref _invocationCount);

        if (!_options.TrackPublish) return;
        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return;
        LogOutgoing(op, message);
    }

    public void LogConsume(MassTransitOperationInfo op, object? message)
    {
        Interlocked.Increment(ref _invocationCount);

        if (!_options.TrackConsume) return;
        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return;
        LogIncoming(op, message);
    }

    public void LogSendFault(MassTransitOperationInfo op, Exception exception)
    {
        Interlocked.Increment(ref _invocationCount);

        if (!_options.LogFaults) return;
        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return;
        LogFault(op, exception, outgoing: true);
    }

    public void LogPublishFault(MassTransitOperationInfo op, Exception exception)
    {
        Interlocked.Increment(ref _invocationCount);

        if (!_options.LogFaults) return;
        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return;
        LogFault(op, exception, outgoing: true);
    }

    public void LogConsumeFault(MassTransitOperationInfo op, Exception exception)
    {
        Interlocked.Increment(ref _invocationCount);

        if (!_options.LogFaults) return;
        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return;
        LogFault(op, exception, outgoing: false);
    }

    private void LogOutgoing(MassTransitOperationInfo op, object? message)
    {
        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null) return;

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);
        var label = MassTransitOperationClassifier.GetDiagramLabel(op, effectiveVerbosity);
        var uri = MassTransitOperationClassifier.BuildUri(op, effectiveVerbosity);

        var body = _options.LogMessageBody && effectiveVerbosity != MassTransitTrackingVerbosity.Summarised && message is not null
            ? JsonSerializer.Serialize(message, message.GetType())
            : null;

        var traceId = Guid.NewGuid();
        var requestResponseId = Guid.NewGuid();

        RequestResponseLogger.Log(new RequestResponseLog(
            testInfo.Value.Name, testInfo.Value.Id,
            label, body, uri, [],
            _options.ServiceName, _options.CallerName,
            RequestResponseType.Request, traceId, requestResponseId, false,
            MetaType: KindOf(op.Operation),
            DependencyCategory: DependencyCategories.MessageQueue)
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                MassTransitOperationClassifier.GetDiagramLabel(op, v),
                MassTransitOperationClassifier.BuildUri(op, v),
                _options.LogMessageBody && v != MassTransitTrackingVerbosity.Summarised && message is not null
                    ? JsonSerializer.Serialize(message, message.GetType()) : null,
                [], false)));

        RequestResponseLogger.Log(new RequestResponseLog(
            testInfo.Value.Name, testInfo.Value.Id,
            label, null, uri, [],
            _options.ServiceName, _options.CallerName,
            RequestResponseType.Response, traceId, requestResponseId, false,
            MetaType: KindOf(op.Operation),
            DependencyCategory: DependencyCategories.MessageQueue)
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                MassTransitOperationClassifier.GetDiagramLabel(op, v),
                MassTransitOperationClassifier.BuildUri(op, v),
                null, [], false)));
    }

    private void LogIncoming(MassTransitOperationInfo op, object? message)
    {
        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null) return;

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);
        var label = MassTransitOperationClassifier.GetDiagramLabel(op, effectiveVerbosity);
        var uri = MassTransitOperationClassifier.BuildUri(op, effectiveVerbosity);

        var body = _options.LogMessageBody && effectiveVerbosity != MassTransitTrackingVerbosity.Summarised && message is not null
            ? JsonSerializer.Serialize(message, message.GetType())
            : null;

        var traceId = Guid.NewGuid();
        var requestResponseId = Guid.NewGuid();

        // Consume is incoming: swap caller/service to reflect message direction
        RequestResponseLogger.Log(new RequestResponseLog(
            testInfo.Value.Name, testInfo.Value.Id,
            label, body, uri, [],
            _options.CallerName, _options.ServiceName,
            RequestResponseType.Request, traceId, requestResponseId, false,
            MetaType: KindOf(op.Operation),
            DependencyCategory: DependencyCategories.MessageQueue)
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                MassTransitOperationClassifier.GetDiagramLabel(op, v),
                MassTransitOperationClassifier.BuildUri(op, v),
                _options.LogMessageBody && v != MassTransitTrackingVerbosity.Summarised && message is not null
                    ? JsonSerializer.Serialize(message, message.GetType()) : null,
                [], false)));

        RequestResponseLogger.Log(new RequestResponseLog(
            testInfo.Value.Name, testInfo.Value.Id,
            label, null, uri, [],
            _options.CallerName, _options.ServiceName,
            RequestResponseType.Response, traceId, requestResponseId, false,
            MetaType: KindOf(op.Operation),
            DependencyCategory: DependencyCategories.MessageQueue)
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                MassTransitOperationClassifier.GetDiagramLabel(op, v),
                MassTransitOperationClassifier.BuildUri(op, v),
                null, [], false)));
    }

    // A call that moves a message is an event. A fault record holds an exception and no message, and Other names nothing.
    private static RequestResponseMetaType KindOf(MassTransitOperation operation) => operation is
        MassTransitOperation.Send or MassTransitOperation.Publish or MassTransitOperation.Consume
        ? RequestResponseMetaType.Event
        : RequestResponseMetaType.Default;

    private void LogFault(MassTransitOperationInfo op, Exception exception, bool outgoing)
    {
        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null) return;

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);
        var label = MassTransitOperationClassifier.GetDiagramLabel(op, effectiveVerbosity);
        var uri = MassTransitOperationClassifier.BuildUri(op, effectiveVerbosity);

        var traceId = Guid.NewGuid();
        var requestResponseId = Guid.NewGuid();

        var svc = outgoing ? _options.ServiceName : _options.CallerName;
        var caller = outgoing ? _options.CallerName : _options.ServiceName;

        RequestResponseLogger.Log(new RequestResponseLog(
            testInfo.Value.Name, testInfo.Value.Id,
            label, exception.Message, uri, [],
            svc, caller,
            RequestResponseType.Request, traceId, requestResponseId, false,
            DependencyCategory: DependencyCategories.MessageQueue)
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                MassTransitOperationClassifier.GetDiagramLabel(op, v),
                MassTransitOperationClassifier.BuildUri(op, v),
                exception.Message, [], false)));

        RequestResponseLogger.Log(new RequestResponseLog(
            testInfo.Value.Name, testInfo.Value.Id,
            label, null, uri, [],
            svc, caller,
            RequestResponseType.Response, traceId, requestResponseId, false,
            StatusCode: "Fault",
            DependencyCategory: DependencyCategories.MessageQueue)
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current,
            // Why the message faulted, as every failed answer says (FAILED_CALLS_PLAN.md F23).
            Error = FailedSend.Describe(exception)
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                MassTransitOperationClassifier.GetDiagramLabel(op, v),
                MassTransitOperationClassifier.BuildUri(op, v),
                null, [], false)));
    }
}
