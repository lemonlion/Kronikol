using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Kronikol.Constants;

namespace Kronikol.Tracking;

/// <summary>
/// Logs non-HTTP interactions (events, messages, commands) to the
/// <see cref="RequestResponseLogger"/> so they appear in the PlantUML
/// sequence diagrams alongside regular HTTP traffic.
///
/// Register an instance in DI (typically as a singleton) and inject it
/// into any fake or stub that simulates publishing/sending messages.
/// </summary>
public class MessageTracker : ITrackingComponent
{
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly string _callerName;
    private readonly string _serviceName;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly Func<(string Name, string Id)>? _testInfoFallback;
    private readonly MessageTrackerVerbosity _verbosity;
    private readonly MessageTrackerVerbosity? _setupVerbosity;
    private readonly MessageTrackerVerbosity? _actionVerbosity;
    private readonly bool _trackDuringSetup;
    private readonly bool _trackDuringAction;
    private readonly string _dependencyCategory;
    private readonly string? _callerDependencyCategory;
    private int _invocationCount;

    /// <summary>
    /// Creates a <see cref="MessageTracker"/> using an options record.
    /// This is the recommended constructor — it follows the same pattern
    /// as all other TTD extensions.
    /// </summary>
    public MessageTracker(MessageTrackerOptions options)
        : this(options, httpContextAccessor: null)
    {
    }

    /// <summary>
    /// Creates a <see cref="MessageTracker"/> using an options record with an optional
    /// <see cref="IHttpContextAccessor"/> for dual-layer correlation when
    /// <see cref="MessageTrackerOptions.UseHttpContextCorrelation"/> is enabled.
    /// </summary>
    public MessageTracker(MessageTrackerOptions options, IHttpContextAccessor? httpContextAccessor)
    {
        _httpContextAccessor = options.UseHttpContextCorrelation ? httpContextAccessor : null;
        _callerName = options.CallerName;
        _serviceName = options.ServiceName;
        _serializerOptions = options.SerializerOptions ?? new JsonSerializerOptions();
        _testInfoFallback = options.CurrentTestInfoFetcher;
        _verbosity = options.Verbosity;
        _setupVerbosity = options.SetupVerbosity;
        _actionVerbosity = options.ActionVerbosity;
        _trackDuringSetup = options.TrackDuringSetup;
        _trackDuringAction = options.TrackDuringAction;
        _dependencyCategory = options.DependencyCategory;
        _callerDependencyCategory = options.CallerDependencyCategory;
        TrackingComponentRegistry.Register(this);
    }

    /// <summary>
    /// Legacy constructor that reads test info from <see cref="IHttpContextAccessor"/>
    /// request headers, with an optional fallback delegate.
    /// Kept for backward compatibility — prefer the <see cref="MessageTrackerOptions"/>
    /// overload for new code.
    /// </summary>
    public MessageTracker(
        IHttpContextAccessor httpContextAccessor,
        string callingServiceName,
        JsonSerializerOptions? serializerOptions = null,
        Func<(string Name, string Id)>? testInfoFallback = null)
    {
        _httpContextAccessor = httpContextAccessor;
        _callerName = callingServiceName;
        _serviceName = "MessageBus";
        _serializerOptions = serializerOptions ?? new JsonSerializerOptions();
        _testInfoFallback = testInfoFallback;
        _verbosity = MessageTrackerVerbosity.Detailed;
        _trackDuringSetup = true;
        _trackDuringAction = true;
        _dependencyCategory = DependencyCategories.MessageQueue;
        _callerDependencyCategory = null;
        TrackingComponentRegistry.Register(this);
    }

    // ─── ITrackingComponent ─────────────────────────────────────

    public string ComponentName => $"MessageTracker ({_serviceName})";
    public bool WasInvoked => _invocationCount > 0;
    public int InvocationCount => _invocationCount;
    public bool HasHttpContextAccessor => _httpContextAccessor is not null;

    /// <summary>
    /// Logs a request entry for a message being sent to a named destination.
    /// Returns a correlation ID to pass to <see cref="TrackMessageResponse"/>.
    /// </summary>
    /// <param name="protocol">The protocol or transport label shown in the diagram (e.g. "Kafka", "EventGrid", "SNS", "RabbitMQ").</param>
    /// <param name="destinationName">The name of the destination service or topic shown in the diagram.</param>
    /// <param name="destinationUri">A URI representing the destination (e.g. <c>new Uri("kafka://orders-topic")</c>).</param>
    /// <param name="payload">The message payload. Will be JSON-serialised and shown in the diagram note.</param>
    /// <param name="noteOnRight">When <c>true</c>, the note is placed on the right side of the arrow (used for consume events).</param>
    /// <returns>A correlation ID that must be passed to <see cref="TrackMessageResponse"/> to pair the request and response.</returns>
    public Guid TrackMessageRequest(string protocol, string destinationName, Uri destinationUri, object payload, bool noteOnRight = false)
    {
        Interlocked.Increment(ref _invocationCount);

        if (!PhaseConfiguration.ShouldTrack(_trackDuringSetup, _trackDuringAction))
            return Guid.Empty;

        var testInfo = GetTestInfo();
        if (testInfo is null)
            return Guid.Empty;

        var requestResponseId = Guid.NewGuid();
        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_verbosity, _setupVerbosity, _actionVerbosity);
        var content = effectiveVerbosity == MessageTrackerVerbosity.Summarised
            ? null
            : JsonSerializer.Serialize(payload, _serializerOptions);

        RequestResponseLogger.Log(new RequestResponseLog(
            testInfo.Value.TestName,
            testInfo.Value.TestId,
            protocol,
            content,
            destinationUri,
            [],
            destinationName,
            _callerName,
            RequestResponseType.Request,
            testInfo.Value.TraceId,
            requestResponseId,
            false,
            MetaType: RequestResponseMetaType.Event,
            DependencyCategory: _dependencyCategory,
            CallerDependencyCategory: _callerDependencyCategory
        )
        {
            AttributionSource = testInfo.Value.Source,
            Timestamp = DateTimeOffset.UtcNow,
            NoteOnRight = noteOnRight,
            ActivitySpanId = Activity.Current?.SpanId.ToString(),
            ActivityTraceId = Activity.Current?.TraceId.ToString(),
            Phase = TestPhaseContext.Current
        }.WithVariants(_verbosity, _setupVerbosity, _actionVerbosity,
            v => new PhaseVariant(
                protocol,
                destinationUri,
                v == MessageTrackerVerbosity.Summarised ? null : JsonSerializer.Serialize(payload, _serializerOptions),
                [], false)));

        return requestResponseId;
    }

    /// <summary>
    /// Logs a response entry for a message that was sent.
    /// </summary>
    /// <param name="protocol">The protocol or transport label (must match the value passed to <see cref="TrackMessageRequest"/>).</param>
    /// <param name="destinationName">The destination service or topic name (must match the value passed to <see cref="TrackMessageRequest"/>).</param>
    /// <param name="destinationUri">The destination URI (must match the value passed to <see cref="TrackMessageRequest"/>).</param>
    /// <param name="requestResponseId">The correlation ID returned by <see cref="TrackMessageRequest"/>.</param>
    /// <param name="responsePayload">Optional response payload (e.g. an acknowledgement). Will be JSON-serialised if provided.</param>
    /// <param name="statusLabel">The label shown on the response arrow (default: "Responded").</param>
    public void TrackMessageResponse(string protocol, string destinationName, Uri destinationUri, Guid requestResponseId, object? responsePayload = null, string statusLabel = "Responded")
    {
        if (!PhaseConfiguration.ShouldTrack(_trackDuringSetup, _trackDuringAction))
            return;

        var testInfo = GetTestInfo();
        if (testInfo is null)
            return;

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_verbosity, _setupVerbosity, _actionVerbosity);
        var content = effectiveVerbosity == MessageTrackerVerbosity.Summarised
            ? null
            : responsePayload is not null
                ? JsonSerializer.Serialize(responsePayload, _serializerOptions)
                : string.Empty;

        RequestResponseLogger.Log(new RequestResponseLog(
            testInfo.Value.TestName,
            testInfo.Value.TestId,
            protocol,
            content,
            destinationUri,
            [],
            destinationName,
            _callerName,
            RequestResponseType.Response,
            testInfo.Value.TraceId,
            requestResponseId,
            false,
            statusLabel,
            MetaType: RequestResponseMetaType.Event,
            DependencyCategory: _dependencyCategory,
            CallerDependencyCategory: _callerDependencyCategory
        )
        {
            AttributionSource = testInfo.Value.Source,
            Timestamp = DateTimeOffset.UtcNow,
            ActivitySpanId = Activity.Current?.SpanId.ToString(),
            ActivityTraceId = Activity.Current?.TraceId.ToString(),
            Phase = TestPhaseContext.Current
        }.WithVariants(_verbosity, _setupVerbosity, _actionVerbosity,
            v => new PhaseVariant(
                protocol,
                destinationUri,
                v == MessageTrackerVerbosity.Summarised
                    ? null
                    : responsePayload is not null
                        ? JsonSerializer.Serialize(responsePayload, _serializerOptions)
                        : string.Empty,
                [], false)));
    }

    /// <summary>
    /// Logs a complete send event as a fire-and-forget pair (request + response)
    /// in a single call. Ideal for event-driven patterns where there is no
    /// meaningful response to track separately.
    /// </summary>
    /// <param name="protocol">The protocol or transport label shown in the diagram.</param>
    /// <param name="destinationName">The name of the destination service or topic shown in the diagram.</param>
    /// <param name="destinationUri">A URI representing the destination.</param>
    /// <param name="payload">Optional message payload. Will be JSON-serialised when verbosity allows.</param>
    public void TrackSendEvent(string protocol, string destinationName, Uri destinationUri, object? payload = null)
    {
        var id = TrackMessageRequest(protocol, destinationName, destinationUri, payload ?? new { });
        if (id != Guid.Empty)
            TrackMessageResponse(protocol, destinationName, destinationUri, id);
    }

    /// <summary>
    /// Logs a complete send as an atomic request+response pair with standard (non-event) styling.
    /// Unlike <see cref="TrackSendEvent"/>, this produces standard arrows in diagrams rather
    /// than event-styled blue notes. Use this when you want the arrow style to match HTTP calls.
    /// </summary>
    /// <param name="protocol">The protocol or transport label shown in the diagram.</param>
    /// <param name="destinationName">The name of the destination service or topic shown in the diagram.</param>
    /// <param name="destinationUri">A URI representing the destination.</param>
    /// <param name="payload">Optional message payload. Will be JSON-serialised when verbosity allows.</param>
    /// <returns>The correlation ID for the logged pair, or <see cref="Guid.Empty"/> if tracking was skipped.</returns>
    public Guid TrackSendMessage(string protocol, string destinationName, Uri destinationUri, object? payload = null)
    {
        Interlocked.Increment(ref _invocationCount);

        if (!PhaseConfiguration.ShouldTrack(_trackDuringSetup, _trackDuringAction))
            return Guid.Empty;

        var testInfo = GetTestInfo();
        if (testInfo is null)
            return Guid.Empty;

        var requestResponseId = Guid.NewGuid();
        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_verbosity, _setupVerbosity, _actionVerbosity);
        var content = effectiveVerbosity == MessageTrackerVerbosity.Summarised
            ? null
            : JsonSerializer.Serialize(payload ?? new { }, _serializerOptions);
        var now = DateTimeOffset.UtcNow;

        RequestResponseLogger.Log(new RequestResponseLog(
            testInfo.Value.TestName,
            testInfo.Value.TestId,
            protocol,
            content,
            destinationUri,
            [],
            destinationName,
            _callerName,
            RequestResponseType.Request,
            testInfo.Value.TraceId,
            requestResponseId,
            false,
            DependencyCategory: _dependencyCategory,
            CallerDependencyCategory: _callerDependencyCategory,
            MetaType: RequestResponseMetaType.Event
        )
        {
            AttributionSource = testInfo.Value.Source,
            Timestamp = now,
            ActivitySpanId = Activity.Current?.SpanId.ToString(),
            ActivityTraceId = Activity.Current?.TraceId.ToString(),
            Phase = TestPhaseContext.Current
        });

        RequestResponseLogger.Log(new RequestResponseLog(
            testInfo.Value.TestName,
            testInfo.Value.TestId,
            protocol,
            null,
            destinationUri,
            [],
            destinationName,
            _callerName,
            RequestResponseType.Response,
            testInfo.Value.TraceId,
            requestResponseId,
            false,
            "Sent",
            DependencyCategory: _dependencyCategory,
            CallerDependencyCategory: _callerDependencyCategory,
            MetaType: RequestResponseMetaType.Event
        )
        {
            AttributionSource = testInfo.Value.Source,
            Timestamp = now,
            ActivitySpanId = Activity.Current?.SpanId.ToString(),
            ActivityTraceId = Activity.Current?.TraceId.ToString(),
            Phase = TestPhaseContext.Current
        });

        return requestResponseId;
    }

    /// <summary>
    /// Tracks consumption of an event from a message broker as a delivery + ack pair.
    /// Arrow direction: <c>CallerName</c> → <paramref name="consumerName"/> (broker → consumer).
    /// Payload appears on the delivery arrow; response uses the <paramref name="ackLabel"/>.
    /// </summary>
    /// <param name="protocol">Protocol label shown in the diagram (e.g. "Consume (Kafka)", "Consume (Pub/Sub)").</param>
    /// <param name="consumerName">The consuming service name (right side of arrow / arrow target).</param>
    /// <param name="sourceUri">URI of the topic/subscription (e.g. <c>new Uri("kafka:///my_topic")</c>).</param>
    /// <param name="payload">The consumed event payload. Will be JSON-serialised and shown in the diagram note.</param>
    /// <param name="ackLabel">Label for the acknowledgement arrow (default: <c>"Ack"</c>).</param>
    public void TrackConsumeEvent(string protocol, string consumerName, Uri sourceUri, object? payload = null, string ackLabel = "Ack")
    {
        var id = TrackMessageRequest(protocol, consumerName, sourceUri, payload ?? new { }, noteOnRight: true);
        if (id != Guid.Empty)
            TrackMessageResponse(protocol, consumerName, sourceUri, id, statusLabel: ackLabel);
    }

    /// <summary>
    /// Returns <c>true</c> only if the current <c>HttpContext</c> belongs to the same
    /// DI container that created this <see cref="MessageTracker"/> instance.
    /// Use in shared-store scenarios where multiple <c>WebApplicationFactory</c> hosts
    /// subscribe to the same event source, to prevent duplicate tracking.
    /// </summary>
    /// <remarks>
    /// Requires <see cref="MessageTrackerOptions.UseHttpContextCorrelation"/> to be <c>true</c>
    /// and the tracker to have been created with an <see cref="IHttpContextAccessor"/>.
    /// </remarks>
    public bool IsCurrentRequestFromMyHost()
    {
        var httpContext = _httpContextAccessor?.HttpContext;
        if (httpContext is null)
            return false;

        var requestAccessor = httpContext.RequestServices.GetService(typeof(IHttpContextAccessor));
        return ReferenceEquals(requestAccessor, _httpContextAccessor);
    }

    private (string TestName, string TestId, Guid TraceId, AttributionSource Source)? GetTestInfo()
    {
        var context = _httpContextAccessor?.HttpContext;
        if (context is not null)
        {
            var headers = context.Request.Headers;

            headers.TryGetValue(TestTrackingHttpHeaders.CurrentTestNameHeader, out var testNameValues);
            headers.TryGetValue(TestTrackingHttpHeaders.CurrentTestIdHeader, out var testIdValues);
            headers.TryGetValue(TestTrackingHttpHeaders.TraceIdHeader, out var traceIdValues);

            var hasHeaders = testNameValues.Count > 0 && testIdValues.Count > 0 && traceIdValues.Count > 0;
            if (hasHeaders)
            {
                return (
                    testNameValues.First()!,
                    testIdValues.First()!,
                    Guid.Parse(traceIdValues.First()!),
                    AttributionSource.RequestHeader
                );
            }
        }

        // The shared chain: the framework context, an explicit scope, the global fallback — and a detached
        // flow, which resolves to nothing unless a scope names its scenario.
        var who = TestInfoResolver.ResolveWithSource(null, _testInfoFallback);
        return who is { } identity ? (identity.Name, identity.Id, Guid.NewGuid(), identity.Source) : null;
    }
}
