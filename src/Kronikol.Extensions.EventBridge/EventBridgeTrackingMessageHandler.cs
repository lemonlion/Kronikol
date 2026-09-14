using Kronikol.Constants;
using System.Text;
using Microsoft.AspNetCore.Http;
using Kronikol.Tracking;

namespace Kronikol.Extensions.EventBridge;

/// <summary>
/// A <see cref="DelegatingHandler" /> that intercepts and classifies EventBridge HTTP operations for inclusion in test diagrams.
/// </summary>
public class EventBridgeTrackingMessageHandler : DelegatingHandler, ITrackingComponent
{
    private readonly EventBridgeTrackingMessageHandlerOptions _options;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private int _invocationCount;

    public EventBridgeTrackingMessageHandler(EventBridgeTrackingMessageHandlerOptions options, HttpMessageHandler? innerHandler = null, IHttpContextAccessor? httpContextAccessor = null)
    {
        _options = options;
        _httpContextAccessor = httpContextAccessor ?? options.HttpContextAccessor;
        InnerHandler = innerHandler ?? new HttpClientHandler();
        TrackingComponentRegistry.Register(this);
    }

    public string ComponentName => $"EventBridgeTrackingMessageHandler ({_options.ServiceName})";
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

        string? requestBody = null;
        if (request.Content is not null)
            requestBody = await HttpContentReader.ReadContentAsStringAsync(request.Content, cancellationToken);

        var ebOp = EventBridgeOperationClassifier.Classify(request, requestBody);

        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
        {
            ReconstructContent(request, requestBody);
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        if (effectiveVerbosity == EventBridgeTrackingVerbosity.Summarised && ebOp.Operation == EventBridgeOperation.Other)
        {
            ReconstructContent(request, requestBody);
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null)
        {
            ReconstructContent(request, requestBody);
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        if (_options.ExcludedOperations.Contains(ebOp.Operation))
        {
            ReconstructContent(request, requestBody);
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var requestResponseId = Guid.NewGuid();
        var traceId = Guid.NewGuid();

        var label = EventBridgeOperationClassifier.GetDiagramLabel(ebOp, effectiveVerbosity);

        var logRequestContent = effectiveVerbosity == EventBridgeTrackingVerbosity.Summarised
            ? null
            : requestBody;

        var requestHeaders = GetFilteredHeaders(request, effectiveVerbosity);

        OneOf<HttpMethod, string> method = effectiveVerbosity == EventBridgeTrackingVerbosity.Raw
            ? request.Method
            : label;

        var requestUri = effectiveVerbosity == EventBridgeTrackingVerbosity.Raw
            ? request.RequestUri!
            : BuildCleanUri(ebOp);

        RequestResponseLogger.Log(new RequestResponseLog(
            testInfo.Value.Name,
            testInfo.Value.Id,
            method,
            logRequestContent,
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
                v == EventBridgeTrackingVerbosity.Raw ? request.Method : EventBridgeOperationClassifier.GetDiagramLabel(ebOp, v),
                v == EventBridgeTrackingVerbosity.Raw ? request.RequestUri! : BuildCleanUri(ebOp),
                v == EventBridgeTrackingVerbosity.Summarised ? null : requestBody,
                GetFilteredHeaders(request, v),
                v == EventBridgeTrackingVerbosity.Summarised && ebOp.Operation == EventBridgeOperation.Other)));

        ReconstructContent(request, requestBody);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var responseContent = effectiveVerbosity == EventBridgeTrackingVerbosity.Summarised
            ? null
            : await HttpContentReader.ReadContentAsStringAsync(response.Content, cancellationToken);

        var responseHeaders = GetFilteredHeaders(response, effectiveVerbosity);

        RequestResponseLogger.Log(new RequestResponseLog(
            testInfo.Value.Name,
            testInfo.Value.Id,
            method,
            responseContent,
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
                v == EventBridgeTrackingVerbosity.Raw ? request.Method : EventBridgeOperationClassifier.GetDiagramLabel(ebOp, v),
                v == EventBridgeTrackingVerbosity.Raw ? request.RequestUri! : BuildCleanUri(ebOp),
                v == EventBridgeTrackingVerbosity.Summarised ? null : responseContent,
                GetFilteredHeaders(response, v),
                v == EventBridgeTrackingVerbosity.Summarised && ebOp.Operation == EventBridgeOperation.Other)));

        return response;
    }

    private static void ReconstructContent(HttpRequestMessage request, string? body)
    {
        if (body is null) return;
        var mediaType = request.Content?.Headers.ContentType?.MediaType ?? "application/x-amz-json-1.1";
        request.Content = new StringContent(body, Encoding.UTF8, mediaType);
    }

    private (string Key, string? Value)[] GetFilteredHeaders(HttpRequestMessage request, EventBridgeTrackingVerbosity verbosity)
    {
        if (verbosity == EventBridgeTrackingVerbosity.Summarised)
            return [];

        return request.Headers
            .Where(h => !_options.ExcludedHeaders.Contains(h.Key))
            .SelectMany(h => h.Value.Select(v => (h.Key, (string?)v)))
            .ToArray();
    }

    private (string Key, string? Value)[] GetFilteredHeaders(HttpResponseMessage response, EventBridgeTrackingVerbosity verbosity)
    {
        if (verbosity == EventBridgeTrackingVerbosity.Summarised)
            return [];

        return response.Headers
            .Where(h => !_options.ExcludedHeaders.Contains(h.Key))
            .SelectMany(h => h.Value.Select(v => (h.Key, (string?)v)))
            .ToArray();
    }

    private static Uri BuildCleanUri(EventBridgeOperationInfo op)
    {
        var busName = op.EventBusName ?? "default";
        return new Uri($"eventbridge://{busName}/");
    }
}
