using Kronikol.Constants;
using Microsoft.AspNetCore.Http;
using Kronikol.Tracking;

namespace Kronikol.Extensions.StorageQueues;

/// <summary>
/// A <see cref="DelegatingHandler" /> that intercepts and classifies StorageQueues HTTP operations for inclusion in test diagrams.
/// </summary>
public class StorageQueueTrackingMessageHandler : DelegatingHandler, ITrackingComponent
{
    private readonly StorageQueueTrackingMessageHandlerOptions _options;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private int _invocationCount;

    public StorageQueueTrackingMessageHandler(StorageQueueTrackingMessageHandlerOptions options, HttpMessageHandler? innerHandler = null, IHttpContextAccessor? httpContextAccessor = null)
    {
        _options = options;
        _httpContextAccessor = httpContextAccessor ?? options.HttpContextAccessor;
        InnerHandler = innerHandler ?? new HttpClientHandler();
        TrackingComponentRegistry.Register(this);
    }

    public string ComponentName => $"StorageQueueTrackingMessageHandler ({_options.ServiceName})";
    public bool WasInvoked => _invocationCount > 0;
    public int InvocationCount => _invocationCount;
    public bool HasHttpContextAccessor => _httpContextAccessor is not null;

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return SendAsync(request, cancellationToken).GetAwaiter().GetResult();
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _invocationCount);

        var queueOp = StorageQueueOperationClassifier.Classify(request);

        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null)
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var requestResponseId = Guid.NewGuid();
        var traceId = Guid.NewGuid();

        // A call that moves a message is an event, and a response carries its request's kind. By the operation: at Raw the
        // record is the HTTP request itself, whose verb and path do not name one.
        var metaType = queueOp.Operation is StorageQueueOperation.SendMessage or StorageQueueOperation.ReceiveMessages or StorageQueueOperation.PeekMessages
            ? RequestResponseMetaType.Event
            : RequestResponseMetaType.Default;

        var label = StorageQueueOperationClassifier.GetDiagramLabel(queueOp, effectiveVerbosity);

        // Each note is made at its own verbosity from bodies read once: a phase's variant was given the effective
        // verbosity's note, so a Detailed phase of a Summarised handler had neither body.
        var drawnAt = PhaseConfiguration.DrawnAt(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);
        var requestBody = await ReadRequestBody(request, drawnAt, cancellationToken);
        var requestContent = effectiveVerbosity == StorageQueueTrackingVerbosity.Summarised ? null : requestBody;
        var requestHeaders = GetFilteredHeaders(request, effectiveVerbosity);

        OneOf<HttpMethod, string> method = effectiveVerbosity == StorageQueueTrackingVerbosity.Raw
            ? request.Method
            : label;

        var requestUri = effectiveVerbosity == StorageQueueTrackingVerbosity.Raw
            ? request.RequestUri!
            : BuildCleanUri(queueOp);

        var call = OpenCall.Open(new RequestResponseLog(
            testInfo.Value.Name,
            testInfo.Value.Id,
            method,
            requestContent,
            requestUri,
            requestHeaders,
            _options.ServiceName,
            _options.CallerName,
            RequestResponseType.Request,
            traceId,
            requestResponseId,
            false,
            MetaType: metaType,
            DependencyCategory: DependencyCategories.MessageQueue
        )
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current,
            RecordedBefore11 = RecordedBefore11(request, effectiveVerbosity, method, requestUri)
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                v == StorageQueueTrackingVerbosity.Raw ? request.Method : StorageQueueOperationClassifier.GetDiagramLabel(queueOp, v),
                v == StorageQueueTrackingVerbosity.Raw ? request.RequestUri! : BuildCleanUri(queueOp),
                v == StorageQueueTrackingVerbosity.Summarised ? null : requestBody,
                GetFilteredHeaders(request, v),
                Skip: false)));

        HttpResponseMessage response;
        string? responseBody;
        var sent = false;
        try
        {
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            sent = true;
            responseBody = await ReadResponseBody(response, drawnAt, cancellationToken);
        }
        catch (Exception ex)
        {
            // A call that throws is still an answer, as the HTTP handler records it: the exception's type where the status
            // would be, its message chain in Error, and the exception on its way (OpenCall, FailedSend). Before #125 a send
            // that threw left a request with no response, and before #232 so did a body that broke off as it was read, which
            // history's earlier rules still read.
            call.Threw(ex, sent ? OpenCall.UnansweredBefore12 : answer => answer with { RecordedBefore11 = RecordedBefore.NotRecorded });
            throw;
        }

        var responseHeaders = GetFilteredHeaders(response, effectiveVerbosity);

        call.Answer(new RequestResponseLog(
            testInfo.Value.Name,
            testInfo.Value.Id,
            method,
            effectiveVerbosity == StorageQueueTrackingVerbosity.Summarised ? null : responseBody,
            requestUri,
            responseHeaders,
            _options.ServiceName,
            _options.CallerName,
            RequestResponseType.Response,
            traceId,
            requestResponseId,
            false,
            response.StatusCode,
            MetaType: metaType,
            DependencyCategory: DependencyCategories.MessageQueue
        )
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                v == StorageQueueTrackingVerbosity.Raw ? request.Method : StorageQueueOperationClassifier.GetDiagramLabel(queueOp, v),
                v == StorageQueueTrackingVerbosity.Raw ? request.RequestUri! : BuildCleanUri(queueOp),
                v == StorageQueueTrackingVerbosity.Summarised ? null : responseBody,
                GetFilteredHeaders(response, v),
                Skip: false)));

        return response;
    }

    // Read when any verbosity the call may be drawn at shows a body, as Detailed and Raw do and Summarised does not.
    private async Task<string?> ReadRequestBody(HttpRequestMessage request, StorageQueueTrackingVerbosity[] drawnAt, CancellationToken ct)
    {
        if (request.Content is null) return null;
        if (drawnAt.All(v => v == StorageQueueTrackingVerbosity.Summarised)) return null;
        return await HttpContentReader.ReadContentAsStringAsync(request.Content, ct);
    }

    private async Task<string?> ReadResponseBody(HttpResponseMessage response, StorageQueueTrackingVerbosity[] drawnAt, CancellationToken ct)
    {
        if (drawnAt.All(v => v == StorageQueueTrackingVerbosity.Summarised)) return null;
        return await HttpContentReader.ReadContentAsStringAsync(response.Content, ct);
    }

    private (string Key, string? Value)[] GetFilteredHeaders(HttpRequestMessage request, StorageQueueTrackingVerbosity verbosity)
    {
        if (verbosity == StorageQueueTrackingVerbosity.Summarised) return [];
        return request.Headers
            .Where(h => !_options.ExcludedHeaders.Contains(h.Key))
            .SelectMany(h => h.Value.Select(v => (h.Key, (string?)v)))
            .ToArray();
    }

    private (string Key, string? Value)[] GetFilteredHeaders(HttpResponseMessage response, StorageQueueTrackingVerbosity verbosity)
    {
        if (verbosity == StorageQueueTrackingVerbosity.Summarised) return [];
        return response.Headers
            .Where(h => !_options.ExcludedHeaders.Contains(h.Key))
            .SelectMany(h => h.Value.Select(v => (h.Key, (string?)v)))
            .ToArray();
    }

    private static Uri BuildCleanUri(StorageQueueOperationInfo op)
    {
        if (op.QueueName is null)
            return new Uri("storagequeue:///");

        return new Uri($"storagequeue:///{op.QueueName}");
    }

    /// <summary>
    /// How every release before #125's R2 recorded this call, when that differs from how it is recorded now: its label
    /// and URI, or that it was not recorded at all (Summarised dropped a call it could not name). History's rules 6 to
    /// 10 read the call from here (<see cref="RequestResponseLog.RecordedBefore11"/>). Raw records the call as sent, then
    /// and now.
    /// </summary>
    private static RecordedBefore? RecordedBefore11(HttpRequestMessage request, StorageQueueTrackingVerbosity verbosity,
        OneOf<HttpMethod, string> method, Uri uri)
    {
        if (verbosity == StorageQueueTrackingVerbosity.Raw)
            return null;
        var then = StorageQueueOperationClassifierBefore11.Classify(request);
        if (verbosity == StorageQueueTrackingVerbosity.Summarised && then.Operation == StorageQueueOperation.Other)
            return RecordedBefore.NotRecorded;
        var label = StorageQueueOperationClassifierBefore11.GetDiagramLabel(then, verbosity);
        var thenUri = StorageQueueOperationClassifierBefore11.BuildCleanUri(then);
        return label == method.Value?.ToString() && thenUri.ToString() == uri.ToString() ? null : new RecordedBefore(label, thenUri);
    }
}
