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
        => Open(operation, content, rawContent: null) is { } call ? (call.Request.RequestResponseId, call.Request.TraceId) : (Guid.Empty, Guid.Empty);

    /// <summary>
    /// Logs a request whose note at Raw is <paramref name="rawContent"/> when it has one, and <paramref name="content"/>
    /// otherwise (a batch's bodies, which Raw alone shows), and opens its call (<see cref="OpenCall"/>), which the client
    /// ends with <see cref="Answer"/> or <see cref="Threw"/>. Null when the call is not tracked.
    /// </summary>
    internal OpenCall? Open(
        PubSubOperationInfo operation, string? content, string? rawContent)
    {
        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction)) return null;

        Interlocked.Increment(ref _invocationCount);

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null) return null;

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var uri = BuildUri(operation, effectiveVerbosity);
        var label = PubSubOperationClassifier.GetDiagramLabel(operation, effectiveVerbosity);
        var traceId = Guid.NewGuid();
        var requestResponseId = Guid.NewGuid();

        var logContent = Note(content, rawContent, effectiveVerbosity);

        return OpenCall.Open(new RequestResponseLog(
            testInfo.Value.Name, testInfo.Value.Id,
            label, logContent, uri, [],
            _options.ServiceName, _options.CallerName,
            RequestResponseType.Request, traceId, requestResponseId, false,
            MetaType: KindOf(operation.Operation),
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

    public void LogResponse(
        PubSubOperationInfo operation,
        Guid requestResponseId, Guid traceId, string? content)
    {
        if (Response(operation, requestResponseId, traceId, content, rawContent: null) is { } response)
            RequestResponseLogger.Log(response);
    }

    /// <summary>
    /// Ends <paramref name="call"/> with its answer, whose note at Raw is <paramref name="rawContent"/> when it has one, and
    /// <paramref name="content"/> otherwise: a publish's message id or a handler's reply, which Raw alone shows.
    /// </summary>
    internal void Answer(OpenCall? call, PubSubOperationInfo operation, string? content, string? rawContent)
    {
        if (call is not null && Response(operation, call.Request.RequestResponseId, call.Request.TraceId, content, rawContent) is { } response)
            call.Answer(response);
    }

    /// <summary>
    /// Ends <paramref name="call"/> for a call that threw: the exception's type where the status would be and its message chain
    /// as the error (<see cref="OpenCall.Threw"/>), and its message as the note at each verbosity that draws one, as a failure
    /// was drawn before #232, when it was answered with no status, which history's earlier rules read as no answer alike.
    /// </summary>
    internal void Threw(OpenCall? call, Exception exception) =>
        call?.Threw(exception, answer => answer with
        {
            Content = Note(exception.Message, null, PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity)),
            SetupVariant = answer.SetupVariant is { } setup ? setup with { Content = Note(exception.Message, null, _options.SetupVerbosity ?? _options.Verbosity) } : null,
            ActionVariant = answer.ActionVariant is { } action ? action with { Content = Note(exception.Message, null, _options.ActionVerbosity ?? _options.Verbosity) } : null,
            RecordedBefore12 = RecordedBefore.NotRecorded
        });

    private RequestResponseLog? Response(
        PubSubOperationInfo operation,
        Guid requestResponseId, Guid traceId, string? content, string? rawContent)
    {
        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction)) return null;

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null) return null;

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var uri = BuildUri(operation, effectiveVerbosity);
        var label = PubSubOperationClassifier.GetDiagramLabel(operation, effectiveVerbosity);

        var logContent = Note(content, rawContent, effectiveVerbosity);

        return new RequestResponseLog(
            testInfo.Value.Name, testInfo.Value.Id,
            label, logContent, uri, [],
            _options.ServiceName, _options.CallerName,
            RequestResponseType.Response, traceId, requestResponseId, false,
            MetaType: KindOf(operation.Operation),
            DependencyCategory: DependencyCategories.MessageQueue)
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                PubSubOperationClassifier.GetDiagramLabel(operation, v),
                BuildUri(operation, v),
                Note(content, rawContent, v),
                [], false));
    }

    // A call that moves a message is an event: settling one or managing the subscriber is not.
    private static RequestResponseMetaType KindOf(PubSubOperation operation) => operation is
        PubSubOperation.Publish or PubSubOperation.PublishBatch or PubSubOperation.Pull or PubSubOperation.Receive
        ? RequestResponseMetaType.Event
        : RequestResponseMetaType.Default;

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
