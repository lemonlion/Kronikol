using System.Diagnostics;
using System.Net;
using System.Text;
using Confluent.Kafka;
using Kronikol.Constants;
using Kronikol.InternalFlow;
using Microsoft.AspNetCore.Http;
using Kronikol.Tracking;

namespace Kronikol.Extensions.Kafka;

/// <summary>
/// Central logging component for Kafka operations. Implements <see cref="ITrackingComponent" /> with auto-registration.
/// </summary>
public class KafkaTracker : ITrackingComponent
{
    /// <summary>The source of the span <see cref="TrackConsume{TKey,TValue}"/> handles a message under.</summary>
    internal const string ActivitySourceName = "Kronikol.Kafka";

    private static readonly ActivitySource Source = new(ActivitySourceName);

    private readonly KafkaTrackingOptions _options;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly string? _role;
    private int _invocationCount;
    private int _registered;
    private int _listenerStarted;

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

    /// <summary>
    /// Logs a produce as one instant, under the scenario, phase and verbosity current at this call, with the W3C trace and
    /// span id of <see cref="Activity.Current"/>.
    /// </summary>
    public void LogProduce(KafkaOperationInfo op, string? content)
    {
        var call = Capture();
        LogProduce(call, op, content, call.At, ids: call.Ambient);
    }

    /// <summary>
    /// Logs a consume as one instant, under the scenario, phase and verbosity current at this call, with the W3C trace and
    /// span id of <see cref="Activity.Current"/>.
    /// </summary>
    public void LogConsume(KafkaOperationInfo op, string? content)
    {
        var call = Capture();
        LogConsume(call, op, content, ids: call.Ambient);
    }

    /// <summary>
    /// Begins tracking a produce through a Kafka client this package does not wrap, as a decorator of a library's own
    /// publisher does (#160): hand Kafka <see cref="KafkaProduceTracking{TKey,TValue}.Message"/> in place of
    /// <paramref name="message"/>, then report the outcome on the result. The produce is recorded under the scenario,
    /// phase and verbosity current at this call, with the W3C ids of the message's <c>traceparent</c> or of
    /// <see cref="Activity.Current"/>, whichever thread reports the outcome. <paramref name="operation"/> is the label it
    /// is drawn under: <see cref="KafkaOperation.Produce"/> or <see cref="KafkaOperation.ProduceAsync"/>.
    /// </summary>
    public KafkaProduceTracking<TKey, TValue> TrackProduce<TKey, TValue>(string topic, Message<TKey, TValue> message,
        KafkaOperation operation = KafkaOperation.Produce) =>
        TrackProduce(topic, message, operation, _options, _options.PropagateTraceContext);

    /// <summary><see cref="TrackProduce{TKey,TValue}(string, Message{TKey,TValue}, KafkaOperation)"/> to a partition.</summary>
    public KafkaProduceTracking<TKey, TValue> TrackProduce<TKey, TValue>(TopicPartition topicPartition, Message<TKey, TValue> message,
        KafkaOperation operation = KafkaOperation.Produce) =>
        TrackProduce(topicPartition.Topic, message, operation, _options, _options.PropagateTraceContext);

    // The wrappers' path: their own options decide the content and what travels, and a traceparent is added only where
    // the wrapper is the last before Confluent.
    internal KafkaProduceTracking<TKey, TValue> TrackProduce<TKey, TValue>(string topic, Message<TKey, TValue> message,
        KafkaOperation operation, KafkaTrackingOptions options, bool addTraceParent) =>
        new(this, Capture(), topic, operation, message, options, addTraceParent);

    /// <summary>
    /// Tracks a message consumed through a Kafka client this package does not wrap, as a decorator of a library's own
    /// consumer does (#160), for as long as it is handled: handle the message inside the scope,
    /// <c>using (tracker.TrackConsume(result)) Handle(result.Message);</c>. The scope makes the scenario the message's
    /// identity headers name current and stores its correlation; logs the consume's request at its start and its
    /// response when it is disposed, so the handler's calls lie inside the consume and its duration is the time the
    /// message took to handle; and runs the handler under a <c>Kronikol.Kafka</c> consumer span,
    /// <c>{topic} process</c>, whose ids the consume records. The span is a child of the message's <c>traceparent</c>
    /// when the consume is recorded under the test the message names, else the root of a trace of its own, never a
    /// child of the span current where the loop polls; the span current before the scope is current again after it.
    /// Disposing ends the message's identity. A null result or a partition end tracks nothing.
    /// </summary>
    public IDisposable TrackConsume<TKey, TValue>(ConsumeResult<TKey, TValue>? result)
    {
        if (result is null || result.IsPartitionEOF)
            return ConsumeScope.None;
        return BeginConsume(result, _options);
    }

    // TrackConsume's scope: the identity, correlation and request at once, the response and the span's end at dispose.
    private ConsumeScope BeginConsume<TKey, TValue>(ConsumeResult<TKey, TValue> result, KafkaTrackingOptions options)
    {
        var identified = EstablishTestIdentity(result.Message?.Headers, options);
        AutoCorrelate(result, options);

        var (call, messageIds) = ConsumedTrace(Capture(), result.Message?.Headers);
        Interlocked.Increment(ref _invocationCount);
        if (!_options.TrackConsume || !IsTracked(call.Phase) || call.Test is null)
            return new ConsumeScope(identified, entry: null, span: null, previous: null);

        var previous = Activity.Current;
        Activity.Current = null;
        var span = Source.StartActivity($"{result.Topic} process", ActivityKind.Consumer, messageIds?.ToContext() ?? default);
        if (span is null)
            Activity.Current = previous;

        var op = new KafkaOperationInfo(KafkaOperation.Consume, result.Topic, result.Partition.Value, result.Offset.Value);
        var entry = Open(call, op, KafkaContent.Of(result.Message, options, leaveOutKronikols: true), incoming: true, W3CIds.Of(span) ?? messageIds);
        return new ConsumeScope(identified, entry, span, previous);
    }

    // A consumed message, the wrappers' path: its identity made current (left so until the next poll), its correlation
    // stored, and its consume logged with the trace it names. Whether an identity was made current.
    internal bool Consumed<TKey, TValue>(ConsumeResult<TKey, TValue> result, KafkaTrackingOptions options)
    {
        var identified = EstablishTestIdentity(result.Message?.Headers, options);
        AutoCorrelate(result, options);

        var op = new KafkaOperationInfo(KafkaOperation.Consume, result.Topic, result.Partition.Value, result.Offset.Value);
        var (call, ids) = ConsumedTrace(Capture(), result.Message?.Headers);
        LogConsume(call, op, KafkaContent.Of(result.Message, options, leaveOutKronikols: true), ids: ids);
        return identified;
    }

    /// <summary>Makes the scenario a message's identity headers name current, when the options propagate it.</summary>
    internal static bool EstablishTestIdentity(Headers? headers, KafkaTrackingOptions options)
    {
        if (!options.PropagateTestIdentity || headers is null)
            return false;

        string? testName = null;
        string? testId = null;
        if (headers.TryGetLastBytes(TestTrackingMessageHeaders.TestName, out var nameBytes))
            testName = Encoding.UTF8.GetString(nameBytes);
        if (headers.TryGetLastBytes(TestTrackingMessageHeaders.TestId, out var idBytes))
            testId = Encoding.UTF8.GetString(idBytes);
        if (testName is null || testId is null)
            return false;

        TestIdentityScope.SetFromMessage(testName, testId);
        return true;
    }

    // Stores the message key's correlation with the current scenario, for processing that loses the message's headers.
    private static void AutoCorrelate<TKey, TValue>(ConsumeResult<TKey, TValue> result, KafkaTrackingOptions options)
    {
        if (!options.AutoCorrelateOnConsume || TestIdentityScope.Current is not { } identity)
            return;

        var messageKey = result.Message?.Key?.ToString();
        if (messageKey is null)
            return;

        var correlationKey = options.ConsumeKeyExtractor is not null
            ? options.ConsumeKeyExtractor(options.ServiceName, messageKey)
            : CorrelationKeys.Kafka(options.ServiceName, messageKey);
        TestCorrelationStore.Correlate(correlationKey, identity.Name, identity.Id);
    }

    // A message being handled: at dispose, once, the consume's response, the span's end with the span current before it
    // made current again (unless it has ended), and the end of the message's identity.
    private sealed class ConsumeScope(bool identified, OpenEntry? entry, Activity? span, Activity? previous) : IDisposable
    {
        internal static readonly ConsumeScope None = new(identified: false, entry: null, span: null, previous: null);

        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            entry?.Answer(DateTimeOffset.UtcNow, failureStatus: null, error: null);
            if (span is not null)
            {
                span.Stop();
                Activity.Current = previous is { IsStopped: false } ? previous : null;
            }
            if (identified)
                TestIdentityScope.ClearMessageIdentity();
        }
    }

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
    internal KafkaCall Capture()
    {
        EnsureListenerStarted();
        return new KafkaCall(
            TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher),
            TestPhaseContext.Current,
            DateTimeOffset.UtcNow,
            InboundTraceId() ?? Guid.NewGuid(),
            W3CIds.Of(Activity.Current));
    }

    // The internal-flow listener captures the spans an entry's W3C ids join; the HTTP handler and the gRPC interceptor
    // start it too, so a suite that tracks only Kafka still has spans to show (F14).
    private void EnsureListenerStarted()
    {
        if (Interlocked.Exchange(ref _listenerStarted, 1) == 0)
            InternalFlowActivityListener.EnsureStarted();
    }

    /// <summary>
    /// The Kronikol trace id of the request the host is serving, when it carries one that is a GUID, as the gRPC
    /// interceptor reads it: a message produced while serving it carries it, so the consume nests under that call.
    /// </summary>
    private Guid? InboundTraceId()
    {
        try
        {
            var headers = _httpContextAccessor?.HttpContext?.Request.Headers;
            if (headers is not null &&
                headers.TryGetValue(TestTrackingHttpHeaders.TraceIdHeader, out var values) &&
                Guid.TryParse(values.FirstOrDefault(), out var traceId))
                return traceId;
        }
        catch
        {
            // HttpContext access can fail in edge cases, as TestInfoResolver notes: the call gets a trace id of its own.
        }

        return null;
    }

    /// <summary>
    /// A produce, stored once its outcome is known: the request stamped at the call, the response at
    /// <paramref name="outcomeAt"/>, and a failure with its status behind a bang and its error.
    /// </summary>
    internal void LogProduce(KafkaCall call, KafkaOperationInfo op, KafkaPayload payload, DateTimeOffset outcomeAt, string? failureStatus = null, string? error = null, W3CIds? ids = null)
    {
        Interlocked.Increment(ref _invocationCount);

        if (!_options.TrackProduce || !IsTracked(call.Phase))
            return;
        Log(call, op, payload, incoming: false, outcomeAt, failureStatus, error, ids);
    }

    /// <summary>A consume, as one instant: the message, or the error, was in hand when it was logged.</summary>
    internal void LogConsume(KafkaCall call, KafkaOperationInfo op, KafkaPayload payload, string? failureStatus = null, string? error = null, W3CIds? ids = null)
    {
        Interlocked.Increment(ref _invocationCount);

        if (!_options.TrackConsume || !IsTracked(call.Phase))
            return;
        Log(call, op, payload, incoming: true, call.At, failureStatus, error, ids);
    }

    /// <summary>
    /// A consume's trace: the message's own <c>traceparent</c> and Kronikol trace id, and only when the consume is recorded
    /// under the test the message's identity headers name. Recorded under another test, or the background, the producer's
    /// trace would be claimed by two tests and the whole-test flow would keep it for neither; and never the polling
    /// loop's <see cref="Activity.Current"/>, which every consume of every test inherits.
    /// </summary>
    internal static (KafkaCall Call, W3CIds? Ids) ConsumedTrace(KafkaCall call, Headers? headers)
    {
        if (call.Test is not { } test || HeaderText(headers, TestTrackingMessageHeaders.TestId) != test.Id)
            return (call, null);

        var traced = Guid.TryParse(HeaderText(headers, TestTrackingMessageHeaders.TraceId), out var traceId)
            ? call with { TraceId = traceId }
            : call;
        return (traced, W3CIds.OfLastTraceParent(headers));
    }

    private static string? HeaderText(Headers? headers, string key)
    {
        if (headers is null || !headers.TryGetLastBytes(key, out var bytes))
            return null;
        try
        {
            return W3CIds.StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
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
        Log(call, op, KafkaPayload.Empty, incoming: false, call.At, failureStatus: null, error: null, ids: null);
    }

    // Both halves at once. An entry logged at one instant carries that instant on both, which the data files read as a
    // call that measured nothing rather than one that took no time (F8). A consume is incoming, so caller and service swap.
    private void Log(KafkaCall call, KafkaOperationInfo op, KafkaPayload payload, bool incoming, DateTimeOffset outcomeAt, string? failureStatus, string? error, W3CIds? ids) =>
        Open(call, op, payload, incoming, ids)?.Answer(outcomeAt, failureStatus, error);

    // Stores an entry's request, stamped at the call, and returns what stores its response; null when the call was made
    // under no scenario.
    private OpenEntry? Open(KafkaCall call, KafkaOperationInfo op, KafkaPayload payload, bool incoming, W3CIds? ids)
    {
        if (call.Test is not { } test)
            return null;

        var verbosity = Verbosity(call.Phase);
        var (service, caller) = incoming ? (_options.CallerName, _options.ServiceName) : (_options.ServiceName, _options.CallerName);
        var entry = new OpenEntry(this, call, test, op, KafkaOperationClassifier.GetDiagramLabel(op, verbosity),
            KafkaOperationClassifier.BuildUri(op, verbosity), service, caller, Guid.NewGuid(), ids);

        var request = new RequestResponseLog(
            test.Name, test.Id,
            entry.Label, verbosity == KafkaTrackingVerbosity.Summarised ? null : payload.Body, entry.Uri,
            verbosity == KafkaTrackingVerbosity.Summarised ? [] : payload.Headers,
            service, caller,
            RequestResponseType.Request, call.TraceId, entry.RequestResponseId, false,
            MetaType: RequestResponseMetaType.Event,
            DependencyCategory: DependencyCategories.MessageQueue)
        {
            AttributionSource = test.Source,
            Phase = call.Phase,
            Timestamp = call.At,
            ActivityTraceId = ids?.TraceId,
            ActivitySpanId = ids?.SpanId,
        };
        AttachVariants(request, call.Phase, v => new PhaseVariant(
            KafkaOperationClassifier.GetDiagramLabel(op, v),
            KafkaOperationClassifier.BuildUri(op, v),
            v == KafkaTrackingVerbosity.Summarised ? null : payload.Body,
            v == KafkaTrackingVerbosity.Summarised ? [] : payload.Headers, false));

        RequestResponseLogger.Log(request);
        return entry;
    }

    // An entry whose request is stored. Its response takes the test, phase, labels and ids the call captured, whenever
    // and wherever it is answered; each caller answers once (KafkaProduceTracking and ConsumeScope see to it).
    private sealed class OpenEntry(KafkaTracker tracker, KafkaCall call, TestIdentity test, KafkaOperationInfo op, string label,
        Uri uri, string service, string caller, Guid requestResponseId, W3CIds? ids)
    {
        internal string Label => label;
        internal Uri Uri => uri;
        internal Guid RequestResponseId => requestResponseId;

        internal void Answer(DateTimeOffset at, string? failureStatus, string? error)
        {
            OneOf<HttpStatusCode, string>? status = failureStatus is null ? null : (OneOf<HttpStatusCode, string>)failureStatus;
            var response = new RequestResponseLog(
                test.Name, test.Id,
                label, null, uri, [],
                service, caller,
                RequestResponseType.Response, call.TraceId, requestResponseId, false,
                status,
                MetaType: RequestResponseMetaType.Event,
                DependencyCategory: DependencyCategories.MessageQueue)
            {
                AttributionSource = test.Source,
                Phase = call.Phase,
                Timestamp = at,
                Error = error,
                ActivityTraceId = ids?.TraceId,
                ActivitySpanId = ids?.SpanId,
            };
            tracker.AttachVariants(response, call.Phase, v => new PhaseVariant(
                KafkaOperationClassifier.GetDiagramLabel(op, v),
                KafkaOperationClassifier.BuildUri(op, v),
                null, [], false));

            RequestResponseLogger.Log(response);
        }
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

/// <summary>
/// What a Kafka call's entries take from the flow that made it, read at the call: its test, phase and time, its Kronikol
/// trace id (the request being served's, else a new one) and the W3C ids of <see cref="Activity.Current"/>.
/// </summary>
internal readonly record struct KafkaCall(TestIdentity? Test, TestPhase Phase, DateTimeOffset At, Guid TraceId, W3CIds? Ambient);

/// <summary>A W3C trace and span id an entry records, and whether the span is recorded (a <c>traceparent</c>'s flags).</summary>
internal readonly record struct W3CIds(string TraceId, string SpanId, bool Recorded)
{
    internal const string TraceParentHeader = "traceparent";

    internal static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    internal static W3CIds? Of(Activity? activity) =>
        activity is null ? null : new W3CIds(activity.TraceId.ToHexString(), activity.SpanId.ToHexString(), activity.Recorded);

    /// <summary>New ids, not recorded, for a message produced where no span is current, as the HTTP handler and gRPC do.</summary>
    internal static W3CIds Random() => new(ActivityTraceId.CreateRandom().ToHexString(), ActivitySpanId.CreateRandom().ToHexString(), false);

    /// <summary>
    /// The ids of the last <c>traceparent</c> header, parsed as the runtime parses one when it parents a span
    /// (<see cref="ActivityContext.TryParse(string, string, out ActivityContext)"/>): null when there is none or the
    /// runtime would refuse it (upper case, version <c>ff</c>, a zero id, trailing text).
    /// </summary>
    internal static W3CIds? OfLastTraceParent(Headers? headers)
    {
        if (headers is null || !headers.TryGetLastBytes(TraceParentHeader, out var bytes))
            return null;
        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
        return ActivityContext.TryParse(text, null, out var context)
            ? new W3CIds(context.TraceId.ToHexString(), context.SpanId.ToHexString(), (context.TraceFlags & ActivityTraceFlags.Recorded) != 0)
            : null;
    }

    /// <summary>The <c>traceparent</c> naming this span, version <c>00</c>, flags <c>01</c> when it is recorded.</summary>
    internal string ToTraceParent() => $"00-{TraceId}-{SpanId}-{(Recorded ? "01" : "00")}";

    /// <summary>This span as a remote parent, for a span started under it in this process.</summary>
    internal ActivityContext ToContext() => new(ActivityTraceId.CreateFromString(TraceId), ActivitySpanId.CreateFromString(SpanId),
        Recorded ? ActivityTraceFlags.Recorded : ActivityTraceFlags.None, isRemote: true);
}
