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

        if (effectiveVerbosity == StorageQueueTrackingVerbosity.Summarised && queueOp.Operation == StorageQueueOperation.Other)
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null)
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var requestResponseId = Guid.NewGuid();
        var traceId = Guid.NewGuid();

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

        RequestResponseLogger.Log(new RequestResponseLog(
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
            DependencyCategory: DependencyCategories.MessageQueue
        )
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                v == StorageQueueTrackingVerbosity.Raw ? request.Method : StorageQueueOperationClassifier.GetDiagramLabel(queueOp, v),
                v == StorageQueueTrackingVerbosity.Raw ? request.RequestUri! : BuildCleanUri(queueOp),
                v == StorageQueueTrackingVerbosity.Summarised ? null : requestBody,
                GetFilteredHeaders(request, v),
                v == StorageQueueTrackingVerbosity.Summarised && queueOp.Operation == StorageQueueOperation.Other)));

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var responseBody = await ReadResponseBody(response, drawnAt, cancellationToken);
        var responseHeaders = GetFilteredHeaders(response, effectiveVerbosity);

        RequestResponseLogger.Log(new RequestResponseLog(
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
                v == StorageQueueTrackingVerbosity.Summarised && queueOp.Operation == StorageQueueOperation.Other)));

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
}
