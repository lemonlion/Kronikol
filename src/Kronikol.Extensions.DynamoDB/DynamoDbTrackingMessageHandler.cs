using Kronikol.Constants;
using System.Text;
using Microsoft.AspNetCore.Http;
using Kronikol.Tracking;

namespace Kronikol.Extensions.DynamoDB;

/// <summary>
/// A <see cref="DelegatingHandler" /> that intercepts and classifies DynamoDB HTTP operations for inclusion in test diagrams.
/// </summary>
public class DynamoDbTrackingMessageHandler : DelegatingHandler, ITrackingComponent
{
    private readonly DynamoDbTrackingMessageHandlerOptions _options;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private int _invocationCount;

    public DynamoDbTrackingMessageHandler(DynamoDbTrackingMessageHandlerOptions options, HttpMessageHandler? innerHandler = null, IHttpContextAccessor? httpContextAccessor = null)
    {
        _options = options;
        _httpContextAccessor = httpContextAccessor ?? options.HttpContextAccessor;
        InnerHandler = innerHandler ?? new HttpClientHandler();
        TrackingComponentRegistry.Register(this);
    }

    public string ComponentName => $"DynamoDbTrackingMessageHandler ({_options.ServiceName})";
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

        // Read request body first — needed for classification (table name is in body)
        string? requestBody = null;
        if (request.Content is not null)
            requestBody = await HttpContentReader.ReadContentAsStringAsync(request.Content, cancellationToken);

        var dynamoOp = DynamoDbOperationClassifier.Classify(request, requestBody);

        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
        {
            ReconstructContent(request, requestBody);
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        if (effectiveVerbosity == DynamoDbTrackingVerbosity.Summarised && dynamoOp.Operation == DynamoDbOperation.Other)
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

        var label = DynamoDbOperationClassifier.GetDiagramLabel(dynamoOp, effectiveVerbosity);

        var logRequestContent = effectiveVerbosity == DynamoDbTrackingVerbosity.Summarised
            ? null
            : requestBody;

        var requestHeaders = GetFilteredHeaders(request, effectiveVerbosity);

        OneOf<HttpMethod, string> method = effectiveVerbosity == DynamoDbTrackingVerbosity.Raw
            ? request.Method
            : label!;

        var requestUri = effectiveVerbosity == DynamoDbTrackingVerbosity.Raw
            ? request.RequestUri!
            : BuildCleanUri(dynamoOp, effectiveVerbosity);

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
            DependencyCategory: DependencyCategories.DynamoDB
        )
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                v == DynamoDbTrackingVerbosity.Raw ? request.Method : DynamoDbOperationClassifier.GetDiagramLabel(dynamoOp, v)!,
                v == DynamoDbTrackingVerbosity.Raw ? request.RequestUri! : BuildCleanUri(dynamoOp, v),
                v == DynamoDbTrackingVerbosity.Summarised ? null : requestBody,
                GetFilteredHeaders(request, v),
                v == DynamoDbTrackingVerbosity.Summarised && dynamoOp.Operation == DynamoDbOperation.Other)));

        // Reconstruct content before forwarding since ReadAsStringAsync consumed the stream
        ReconstructContent(request, requestBody);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var responseContent = effectiveVerbosity == DynamoDbTrackingVerbosity.Summarised
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
            DependencyCategory: DependencyCategories.DynamoDB
        )
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                v == DynamoDbTrackingVerbosity.Raw ? request.Method : DynamoDbOperationClassifier.GetDiagramLabel(dynamoOp, v)!,
                v == DynamoDbTrackingVerbosity.Raw ? request.RequestUri! : BuildCleanUri(dynamoOp, v),
                v == DynamoDbTrackingVerbosity.Summarised ? null : responseContent,
                GetFilteredHeaders(response, v),
                v == DynamoDbTrackingVerbosity.Summarised && dynamoOp.Operation == DynamoDbOperation.Other)));

        return response;
    }

    private static void ReconstructContent(HttpRequestMessage request, string? body)
    {
        if (body is null) return;
        var mediaType = request.Content?.Headers.ContentType?.MediaType ?? "application/x-amz-json-1.0";
        request.Content = new StringContent(body, Encoding.UTF8, mediaType);
    }

    private (string Key, string? Value)[] GetFilteredHeaders(HttpRequestMessage request, DynamoDbTrackingVerbosity verbosity)
    {
        if (verbosity == DynamoDbTrackingVerbosity.Summarised)
            return [];

        return request.Headers
            .Where(h => !_options.ExcludedHeaders.Contains(h.Key))
            .SelectMany(h => h.Value.Select(v => (h.Key, (string?)v)))
            .ToArray();
    }

    private (string Key, string? Value)[] GetFilteredHeaders(HttpResponseMessage response, DynamoDbTrackingVerbosity verbosity)
    {
        if (verbosity == DynamoDbTrackingVerbosity.Summarised)
            return [];

        return response.Headers
            .Where(h => !_options.ExcludedHeaders.Contains(h.Key))
            .SelectMany(h => h.Value.Select(v => (h.Key, (string?)v)))
            .ToArray();
    }

    private static Uri BuildCleanUri(DynamoDbOperationInfo op, DynamoDbTrackingVerbosity verbosity)
    {
        if (op.TableName is null)
            return new Uri("dynamodb:///");

        return new Uri($"dynamodb:///{op.TableName}");
    }
}
