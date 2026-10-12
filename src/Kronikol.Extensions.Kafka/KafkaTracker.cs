using System.Net;
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
    private readonly string? _role;
    private int _invocationCount;
    private int _registered;

    public KafkaTracker(KafkaTrackingOptions options, IHttpContextAccessor? httpContextAccessor = null)
        : this(options, httpContextAccessor, role: null)
    {
        EnsureRegistered();
    }

    /// <summary>
    /// A tracker for <see cref="KafkaTrackingInterceptor"/>, named for its direction and type pair
    /// (<paramref name="role"/>, such as <c>producer &lt;String,String&gt;</c>). It registers with
    /// <see cref="TrackingComponentRegistry"/> only when it first wraps a client (<see cref="EnsureRegistered"/>), so a
    /// tracker replaced before any client used it, or one for a direction the app never builds, is never reported as
    /// registered but never invoked.
    /// </summary>
    internal KafkaTracker(KafkaTrackingOptions options, IHttpContextAccessor? httpContextAccessor, string? role)
    {
        _options = options;
        _httpContextAccessor = httpContextAccessor ?? options.HttpContextAccessor;
        _role = role;
    }

    public string ComponentName => _role is null
        ? $"KafkaTracker ({_options.ServiceName})"
        : $"KafkaTracker ({_options.ServiceName}, {_role})";
    public bool WasInvoked => _invocationCount > 0;
    public int InvocationCount => _invocationCount;
    public bool HasHttpContextAccessor => _httpContextAccessor is not null;

    /// <summary>The accessor this tracker resolves the scenario through: the constructor's, else the options'.</summary>
    internal IHttpContextAccessor? HttpContextAccessor => _httpContextAccessor;

    /// <summary>Registers this tracker with <see cref="TrackingComponentRegistry"/>, once.</summary>
    internal void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 0)
            TrackingComponentRegistry.Register(this);
    }

    /// <summary>Logs a produce as one instant, under the scenario, phase and verbosity current at this call.</summary>
    public void LogProduce(KafkaOperationInfo op, string? content)
    {
        var call = Capture();
        LogProduce(call, op, content, call.At);
    }

    /// <summary>Logs a consume as one instant, under the scenario, phase and verbosity current at this call.</summary>
    public void LogConsume(KafkaOperationInfo op, string? content) => LogConsume(Capture(), op, content);

    /// <summary>Logs a subscribe as one instant, when <see cref="KafkaTrackingOptions.TrackSubscribe"/> is on.</summary>
    public void LogSubscribe(KafkaOperationInfo op) => LogOneInstant(op, _options.TrackSubscribe);

    /// <summary>Logs a commit as one instant, when <see cref="KafkaTrackingOptions.TrackCommit"/> is on.</summary>
    public void LogCommit(KafkaOperationInfo op) => LogOneInstant(op, _options.TrackCommit);

    /// <summary>Logs an unsubscribe as one instant, when <see cref="KafkaTrackingOptions.TrackUnsubscribe"/> is on.</summary>
    public void LogUnsubscribe(KafkaOperationInfo op) => LogOneInstant(op, _options.TrackUnsubscribe);

    /// <summary>Logs a flush as one instant, when <see cref="KafkaTrackingOptions.TrackFlush"/> is on.</summary>
    public void LogFlush(KafkaOperationInfo op) => LogOneInstant(op, _options.TrackFlush);

    /// <summary>Logs a transaction operation as one instant, when <see cref="KafkaTrackingOptions.TrackTransactions"/> is on.</summary>
    public void LogTransaction(KafkaOperationInfo op) => LogOneInstant(op, _options.TrackTransactions);

    /// <summary>
    /// What a Kafka call's entries take from the flow that made it, read at the call: Confluent runs a delivery handler on
    /// the producer's poll thread, under the context of whoever built the producer, where the scenario, the phase and so
    /// the verbosity are someone else's (plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.md F4).
    /// </summary>
    internal KafkaCall Capture() => new(
        TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher),
        TestPhaseContext.Current,
        DateTimeOffset.UtcNow);

    /// <summary>
    /// A produce, stored once its outcome is known: the request stamped at the call, the response at
    /// <paramref name="outcomeAt"/>, and a failure with its status behind a bang and its error.
    /// </summary>
    internal void LogProduce(KafkaCall call, KafkaOperationInfo op, string? content, DateTimeOffset outcomeAt, string? failureStatus = null, string? error = null)
    {
        Interlocked.Increment(ref _invocationCount);

        if (!_options.TrackProduce || !IsTracked(call.Phase))
            return;
        Log(call, op, content, incoming: false, outcomeAt, failureStatus, error);
    }

    /// <summary>A consume, as one instant: the message, or the error, was in hand when it was logged.</summary>
    internal void LogConsume(KafkaCall call, KafkaOperationInfo op, string? content, string? failureStatus = null, string? error = null)
    {
        Interlocked.Increment(ref _invocationCount);

        if (!_options.TrackConsume || !IsTracked(call.Phase))
            return;
        Log(call, op, content, incoming: true, call.At, failureStatus, error);
    }

    /// <summary>Whether any phase's verbosity shows a message's content, so a wrapper builds it (F13).</summary>
    internal static bool ShowsContent(KafkaTrackingOptions options) =>
        options.Verbosity != KafkaTrackingVerbosity.Summarised
        || options.SetupVerbosity is { } setup && setup != KafkaTrackingVerbosity.Summarised
        || options.ActionVerbosity is { } action && action != KafkaTrackingVerbosity.Summarised;

    private void LogOneInstant(KafkaOperationInfo op, bool track)
    {
        Interlocked.Increment(ref _invocationCount);

        if (!track)
            return;
        var call = Capture();
        if (!IsTracked(call.Phase))
            return;
        Log(call, op, content: null, incoming: false, call.At, failureStatus: null, error: null);
    }

    // Both halves at once. An entry logged at one instant carries that instant on both, which the data files read as a
    // call that measured nothing rather than one that took no time (F8). A consume is incoming, so caller and service swap.
    private void Log(KafkaCall call, KafkaOperationInfo op, string? content, bool incoming, DateTimeOffset outcomeAt, string? failureStatus, string? error)
    {
        if (call.Test is not { } test)
            return;

        var verbosity = Verbosity(call.Phase);
        var label = KafkaOperationClassifier.GetDiagramLabel(op, verbosity);
        var uri = KafkaOperationClassifier.BuildUri(op, verbosity);
        var (service, caller) = incoming ? (_options.CallerName, _options.ServiceName) : (_options.ServiceName, _options.CallerName);
        OneOf<HttpStatusCode, string>? status = failureStatus is null ? null : (OneOf<HttpStatusCode, string>)failureStatus;
        var traceId = Guid.NewGuid();
        var requestResponseId = Guid.NewGuid();

        var request = new RequestResponseLog(
            test.Name, test.Id,
            label, verbosity == KafkaTrackingVerbosity.Summarised ? null : content, uri, [],
            service, caller,
            RequestResponseType.Request, traceId, requestResponseId, false,
            MetaType: RequestResponseMetaType.Event,
            DependencyCategory: DependencyCategories.MessageQueue)
        {
            AttributionSource = test.Source,
            Phase = call.Phase,
            Timestamp = call.At,
        };
        AttachVariants(request, call.Phase, v => new PhaseVariant(
            KafkaOperationClassifier.GetDiagramLabel(op, v),
            KafkaOperationClassifier.BuildUri(op, v),
            v == KafkaTrackingVerbosity.Summarised ? null : content,
            [], false));

        var response = new RequestResponseLog(
            test.Name, test.Id,
            label, null, uri, [],
            service, caller,
            RequestResponseType.Response, traceId, requestResponseId, false,
            status,
            MetaType: RequestResponseMetaType.Event,
            DependencyCategory: DependencyCategories.MessageQueue)
        {
            AttributionSource = test.Source,
            Phase = call.Phase,
            Timestamp = outcomeAt,
            Error = error,
        };
        AttachVariants(response, call.Phase, v => new PhaseVariant(
            KafkaOperationClassifier.GetDiagramLabel(op, v),
            KafkaOperationClassifier.BuildUri(op, v),
            null, [], false));

        RequestResponseLogger.Log(request);
        RequestResponseLogger.Log(response);
    }

    private bool IsTracked(TestPhase phase) => phase switch
    {
        TestPhase.Setup => _options.TrackDuringSetup,
        TestPhase.Action => _options.TrackDuringAction,
        _ => true,
    };

    private KafkaTrackingVerbosity Verbosity(TestPhase phase) => phase switch
    {
        TestPhase.Setup => _options.SetupVerbosity ?? _options.Verbosity,
        TestPhase.Action => _options.ActionVerbosity ?? _options.Verbosity,
        _ => _options.Verbosity,
    };

    // PhaseVariantExtensions.AttachVariants, for the phase captured at the call instead of the current one: a call made
    // before any phase began carries both phases' renderings, for the report to pick.
    private void AttachVariants(RequestResponseLog log, TestPhase phase, Func<KafkaTrackingVerbosity, PhaseVariant> variant)
    {
        if (phase != TestPhase.Unknown || (!_options.SetupVerbosity.HasValue && !_options.ActionVerbosity.HasValue))
            return;
        log.SetupVariant = variant(_options.SetupVerbosity ?? _options.Verbosity);
        log.ActionVariant = variant(_options.ActionVerbosity ?? _options.Verbosity);
    }
}

/// <summary>What a Kafka call's entries take from the flow that made it, read at the call.</summary>
internal readonly record struct KafkaCall(TestIdentity? Test, TestPhase Phase, DateTimeOffset At);
