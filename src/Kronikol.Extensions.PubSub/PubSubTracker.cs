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
        _httpContextAccessor = httpContextAccessor ?? options.HttpContextAccessor;
        TrackingComponentRegistry.Register(this);
    }

    public string ComponentName => $"PubSubTracker ({_options.ServiceName})";
    public bool WasInvoked => _invocationCount > 0;
    public int InvocationCount => _invocationCount;
    public bool HasHttpContextAccessor => _httpContextAccessor is not null;

    /// <summary>The accessor this tracker resolves the scenario through: the constructor's, else the options'.</summary>
    internal IHttpContextAccessor? HttpContextAccessor => _httpContextAccessor;

    /// <summary>
    /// The verbosities the call may be drawn at: the phase's, and each phase's while the phase is not known
    /// (<see cref="PhaseConfiguration.DrawnAt{T}"/>). The publisher and subscriber make a note's content only when one of
    /// them shows it, and each note shows it at its own verbosity.
    /// </summary>
    internal static PubSubTrackingVerbosity[] DrawnAt(PubSubTrackingOptions options) =>
        PhaseConfiguration.DrawnAt(options.Verbosity, options.SetupVerbosity, options.ActionVerbosity);

    public (Guid RequestResponseId, Guid TraceId) LogRequest(
        PubSubOperationInfo operation, string? content)
        => LogRequest(operation, content, rawContent: null);

    /// <summary>
    /// A request whose note at Raw is <paramref name="rawContent"/> when it has one, and <paramref name="content"/>
    /// otherwise: a batch's bodies, which Raw alone shows.
    /// </summary>
    internal (Guid RequestResponseId, Guid TraceId) LogRequest(
        PubSubOperationInfo operation, string? content, string? rawContent)
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

        var logContent = Note(content, rawContent, effectiveVerbosity);

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
                Note(content, rawContent, v),
                [], false)));

        return (requestResponseId, traceId);
    }

    public void LogResponse(
        PubSubOperationInfo operation,
        Guid requestResponseId, Guid traceId, string? content)
        => LogResponse(operation, requestResponseId, traceId, content, rawContent: null);

    /// <summary>
    /// A response whose note at Raw is <paramref name="rawContent"/> when it has one, and <paramref name="content"/>
    /// otherwise: a publish's message id or a handler's reply, which Raw alone shows.
    /// </summary>
    internal void LogResponse(
        PubSubOperationInfo operation,
        Guid requestResponseId, Guid traceId, string? content, string? rawContent)
    {
        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction)) return;

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null) return;

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var uri = BuildUri(operation, effectiveVerbosity);
        var label = PubSubOperationClassifier.GetDiagramLabel(operation, effectiveVerbosity);

        var logContent = Note(content, rawContent, effectiveVerbosity);

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
                Note(content, rawContent, v),
                [], false)));
    }

    /// <summary>A note at <paramref name="verbosity"/>: none at Summarised, the Raw content where there is one at Raw.</summary>
    private static string? Note(string? content, string? rawContent, PubSubTrackingVerbosity verbosity) =>
        verbosity == PubSubTrackingVerbosity.Summarised ? null
        : verbosity == PubSubTrackingVerbosity.Raw ? rawContent ?? content
        : content;

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
