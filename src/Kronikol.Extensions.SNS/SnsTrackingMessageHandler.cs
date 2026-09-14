using Kronikol.Constants;
using System.Text;
using Microsoft.AspNetCore.Http;
using Kronikol.Tracking;

namespace Kronikol.Extensions.SNS;

/// <summary>
/// A <see cref="DelegatingHandler" /> that intercepts and classifies SNS HTTP operations for inclusion in test diagrams.
/// </summary>
public class SnsTrackingMessageHandler : DelegatingHandler, ITrackingComponent
{
    private readonly SnsTrackingMessageHandlerOptions _options;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private int _invocationCount;

    public SnsTrackingMessageHandler(SnsTrackingMessageHandlerOptions options, HttpMessageHandler? innerHandler = null, IHttpContextAccessor? httpContextAccessor = null)
    {
        _options = options;
        _httpContextAccessor = httpContextAccessor ?? options.HttpContextAccessor;
        InnerHandler = innerHandler ?? new HttpClientHandler();
        TrackingComponentRegistry.Register(this);
    }

    public string ComponentName => $"SnsTrackingMessageHandler ({_options.ServiceName})";
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

        var snsOp = SnsOperationClassifier.Classify(request, requestBody);

        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
        {
            ReconstructContent(request, requestBody);
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        if (effectiveVerbosity == SnsTrackingVerbosity.Summarised && snsOp.Operation == SnsOperation.Other)
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

        var requestResponseId = Guid.NewGuid();
        var traceId = Guid.NewGuid();

        var label = SnsOperationClassifier.GetDiagramLabel(snsOp, effectiveVerbosity);

        var logRequestContent = effectiveVerbosity == SnsTrackingVerbosity.Summarised
            ? null
            : requestBody;

        var requestHeaders = GetFilteredHeaders(request, effectiveVerbosity);

        OneOf<HttpMethod, string> method = effectiveVerbosity == SnsTrackingVerbosity.Raw
            ? request.Method
            : label!;

        var requestUri = effectiveVerbosity == SnsTrackingVerbosity.Raw
            ? request.RequestUri!
            : BuildCleanUri(snsOp);

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
                v == SnsTrackingVerbosity.Raw ? request.Method : SnsOperationClassifier.GetDiagramLabel(snsOp, v)!,
                v == SnsTrackingVerbosity.Raw ? request.RequestUri! : BuildCleanUri(snsOp),
                v == SnsTrackingVerbosity.Summarised ? null : requestBody,
                GetFilteredHeaders(request, v),
                v == SnsTrackingVerbosity.Summarised && snsOp.Operation == SnsOperation.Other)));

        ReconstructContent(request, requestBody);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var responseContent = effectiveVerbosity == SnsTrackingVerbosity.Summarised
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
                v == SnsTrackingVerbosity.Raw ? request.Method : SnsOperationClassifier.GetDiagramLabel(snsOp, v)!,
                v == SnsTrackingVerbosity.Raw ? request.RequestUri! : BuildCleanUri(snsOp),
                v == SnsTrackingVerbosity.Summarised ? null : responseContent,
                GetFilteredHeaders(response, v),
                v == SnsTrackingVerbosity.Summarised && snsOp.Operation == SnsOperation.Other)));

        return response;
    }

    private static void ReconstructContent(HttpRequestMessage request, string? body)
    {
        if (body is null) return;
        var mediaType = request.Content?.Headers.ContentType?.MediaType ?? "application/x-amz-json-1.0";
        request.Content = new StringContent(body, Encoding.UTF8, mediaType);
    }

    private (string Key, string? Value)[] GetFilteredHeaders(HttpRequestMessage request, SnsTrackingVerbosity verbosity)
    {
        if (verbosity == SnsTrackingVerbosity.Summarised)
            return [];

        return request.Headers
            .Where(h => !_options.ExcludedHeaders.Contains(h.Key))
            .SelectMany(h => h.Value.Select(v => (h.Key, (string?)v)))
            .ToArray();
    }

    private (string Key, string? Value)[] GetFilteredHeaders(HttpResponseMessage response, SnsTrackingVerbosity verbosity)
    {
        if (verbosity == SnsTrackingVerbosity.Summarised)
            return [];

        return response.Headers
            .Where(h => !_options.ExcludedHeaders.Contains(h.Key))
            .SelectMany(h => h.Value.Select(v => (h.Key, (string?)v)))
            .ToArray();
    }

    private static Uri BuildCleanUri(SnsOperationInfo op)
    {
        if (op.TopicName is null)
            return new Uri("sns:///");

        return new Uri($"sns:///{op.TopicName}");
    }
}
