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

    /// <summary>The accessor this tracker resolves the scenario through: the constructor's, else the options'.</summary>
    internal IHttpContextAccessor? HttpContextAccessor => _httpContextAccessor;

    /// <summary>
    /// Whether a verbosity the call may be drawn at shows an event's body, as Detailed and Raw do and Summarised does not,
    /// so the producer writes the bodies only then: the phase's verbosity, and each phase's while the phase is not known
    /// (<see cref="PhaseConfiguration.DrawnAt{T}"/>). Each note then shows them at its own verbosity.
    /// </summary>
    internal static bool ShowsContent(EventHubsTrackingOptions options) =>
        PhaseConfiguration.DrawnAt(options.Verbosity, options.SetupVerbosity, options.ActionVerbosity)
            .Any(v => v != EventHubsTrackingVerbosity.Summarised);

    public (Guid RequestResponseId, Guid TraceId) LogRequest(
        EventHubsOperationInfo operation, string? content)
        => Open(operation, content) is { } call ? (call.Request.RequestResponseId, call.Request.TraceId) : (Guid.Empty, Guid.Empty);

    /// <summary>
    /// Logs a request and opens its call (<see cref="OpenCall"/>), which the client ends with <see cref="Answer"/> or
    /// <see cref="Threw"/>. Null when the call is not tracked.
    /// </summary>
    internal OpenCall? Open(EventHubsOperationInfo operation, string? content)
    {
        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction)) return null;

        Interlocked.Increment(ref _invocationCount);

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null) return null;

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var uri = BuildUri(operation, effectiveVerbosity);
        var label = EventHubsOperationClassifier.GetDiagramLabel(operation, effectiveVerbosity);
        var traceId = Guid.NewGuid();
        var requestResponseId = Guid.NewGuid();

        var logContent = effectiveVerbosity == EventHubsTrackingVerbosity.Summarised ? null : content;

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
                EventHubsOperationClassifier.GetDiagramLabel(operation, v),
                BuildUri(operation, v),
                v == EventHubsTrackingVerbosity.Summarised ? null : content,
                [], false)));
    }

    public void LogResponse(
        EventHubsOperationInfo operation,
        Guid requestResponseId, Guid traceId, string? content)
    {
        if (Response(operation, requestResponseId, traceId, content, rawContent: null) is { } response)
            RequestResponseLogger.Log(response);
    }

    /// <summary>
    /// Ends <paramref name="call"/> with its answer, whose note at Raw is <paramref name="rawContent"/> when it has one, and
    /// <paramref name="content"/> otherwise: the consumer's count of the events it read, which Raw alone shows.
    /// </summary>
    internal void Answer(OpenCall? call, EventHubsOperationInfo operation, string? content, string? rawContent)
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
        EventHubsOperationInfo operation,
        Guid requestResponseId, Guid traceId, string? content, string? rawContent)
    {
        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction)) return null;

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null) return null;

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var uri = BuildUri(operation, effectiveVerbosity);
        var label = EventHubsOperationClassifier.GetDiagramLabel(operation, effectiveVerbosity);

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
                EventHubsOperationClassifier.GetDiagramLabel(operation, v),
                BuildUri(operation, v),
                Note(content, rawContent, v),
                [], false));
    }

    // A call that moves a message is an event: making a batch, reading a hub's properties or starting a processor is not.
    private static RequestResponseMetaType KindOf(EventHubsOperation operation) => operation is
        EventHubsOperation.Send or EventHubsOperation.SendBatch or EventHubsOperation.ReadEvents or
        EventHubsOperation.ReadEventsFromPartition or EventHubsOperation.ProcessEvent
        ? RequestResponseMetaType.Event
        : RequestResponseMetaType.Default;

    /// <summary>A note at <paramref name="verbosity"/>: none at Summarised, the Raw content where there is one at Raw.</summary>
    private static string? Note(string? content, string? rawContent, EventHubsTrackingVerbosity verbosity) =>
        verbosity == EventHubsTrackingVerbosity.Summarised ? null
        : verbosity == EventHubsTrackingVerbosity.Raw ? rawContent ?? content
        : content;

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
