using Kronikol.Constants;
using System.Text;
using Microsoft.AspNetCore.Http;
using Kronikol.Tracking;

namespace Kronikol.Extensions.SQS;

/// <summary>
/// A <see cref="DelegatingHandler" /> that intercepts and classifies SQS HTTP operations for inclusion in test diagrams.
/// </summary>
public class SqsTrackingMessageHandler : DelegatingHandler, ITrackingComponent
{
    private readonly SqsTrackingMessageHandlerOptions _options;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private int _invocationCount;

    public SqsTrackingMessageHandler(SqsTrackingMessageHandlerOptions options, HttpMessageHandler? innerHandler = null, IHttpContextAccessor? httpContextAccessor = null)
    {
        _options = options;
        _httpContextAccessor = httpContextAccessor ?? options.HttpContextAccessor;
        InnerHandler = innerHandler ?? new HttpClientHandler();
        TrackingComponentRegistry.Register(this);
    }

    public string ComponentName => $"SqsTrackingMessageHandler ({_options.ServiceName})";
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

        var sqsOp = SqsOperationClassifier.Classify(request, requestBody);

        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
        {
            ReconstructContent(request, requestBody);
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        if (effectiveVerbosity == SqsTrackingVerbosity.Summarised && sqsOp.Operation == SqsOperation.Other)
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

        var label = SqsOperationClassifier.GetDiagramLabel(sqsOp, effectiveVerbosity);

        var logRequestContent = effectiveVerbosity == SqsTrackingVerbosity.Summarised
            ? null
            : requestBody;

        var requestHeaders = GetFilteredHeaders(request, effectiveVerbosity);

        OneOf<HttpMethod, string> method = effectiveVerbosity == SqsTrackingVerbosity.Raw
            ? request.Method
            : label!;

        var requestUri = effectiveVerbosity == SqsTrackingVerbosity.Raw
            ? request.RequestUri!
            : BuildCleanUri(sqsOp);

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
                v == SqsTrackingVerbosity.Raw ? request.Method : SqsOperationClassifier.GetDiagramLabel(sqsOp, v)!,
                v == SqsTrackingVerbosity.Raw ? request.RequestUri! : BuildCleanUri(sqsOp),
                v == SqsTrackingVerbosity.Summarised ? null : requestBody,
                GetFilteredHeaders(request, v),
                v == SqsTrackingVerbosity.Summarised && sqsOp.Operation == SqsOperation.Other)));

        ReconstructContent(request, requestBody);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var responseContent = effectiveVerbosity == SqsTrackingVerbosity.Summarised
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
                v == SqsTrackingVerbosity.Raw ? request.Method : SqsOperationClassifier.GetDiagramLabel(sqsOp, v)!,
                v == SqsTrackingVerbosity.Raw ? request.RequestUri! : BuildCleanUri(sqsOp),
                v == SqsTrackingVerbosity.Summarised ? null : responseContent,
                GetFilteredHeaders(response, v),
                v == SqsTrackingVerbosity.Summarised && sqsOp.Operation == SqsOperation.Other)));

        return response;
    }

    private static void ReconstructContent(HttpRequestMessage request, string? body)
    {
        if (body is null) return;
        var mediaType = request.Content?.Headers.ContentType?.MediaType ?? "application/x-amz-json-1.0";
        request.Content = new StringContent(body, Encoding.UTF8, mediaType);
    }

    private (string Key, string? Value)[] GetFilteredHeaders(HttpRequestMessage request, SqsTrackingVerbosity verbosity)
    {
        if (verbosity == SqsTrackingVerbosity.Summarised)
            return [];

        return request.Headers
            .Where(h => !_options.ExcludedHeaders.Contains(h.Key))
            .SelectMany(h => h.Value.Select(v => (h.Key, (string?)v)))
            .ToArray();
    }

    private (string Key, string? Value)[] GetFilteredHeaders(HttpResponseMessage response, SqsTrackingVerbosity verbosity)
    {
        if (verbosity == SqsTrackingVerbosity.Summarised)
            return [];

        return response.Headers
            .Where(h => !_options.ExcludedHeaders.Contains(h.Key))
            .SelectMany(h => h.Value.Select(v => (h.Key, (string?)v)))
            .ToArray();
    }

    private static Uri BuildCleanUri(SqsOperationInfo op)
    {
        if (op.QueueName is null)
            return new Uri("sqs:///");

        return new Uri($"sqs:///{op.QueueName}");
    }
}
