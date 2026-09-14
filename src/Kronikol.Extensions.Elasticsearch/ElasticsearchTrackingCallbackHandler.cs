using Kronikol.Constants;
using System.Text;
using Elastic.Transport;
using Microsoft.AspNetCore.Http;
using Kronikol.Tracking;

namespace Kronikol.Extensions.Elasticsearch;

/// <summary>
/// Callback handler for Elasticsearch that classifies and logs operations for test diagrams.
/// </summary>
public class ElasticsearchTrackingCallbackHandler : ITrackingComponent
{
    private readonly ElasticsearchTrackingOptions _options;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private int _invocationCount;

    public ElasticsearchTrackingCallbackHandler(ElasticsearchTrackingOptions options, IHttpContextAccessor? httpContextAccessor = null)
    {
        _options = options;
        _httpContextAccessor = httpContextAccessor ?? options.HttpContextAccessor;
        TrackingComponentRegistry.Register(this);
    }

    public string ComponentName => $"ElasticsearchTrackingCallbackHandler ({_options.ServiceName})";
    public bool WasInvoked => _invocationCount > 0;
    public int InvocationCount => _invocationCount;
    public bool HasHttpContextAccessor => _httpContextAccessor is not null;

    public void HandleApiCallDetails(ApiCallDetails details)
    {
        if (details.Uri is null)
            return;

        LogOperation(
            details.HttpMethod.ToString().ToUpperInvariant(),
            details.Uri,
            details.HttpStatusCode,
            details.RequestBodyInBytes,
            details.ResponseBodyInBytes);
    }

    internal void LogOperation(
        string httpMethod,
        Uri requestUri,
        int? statusCode,
        byte[]? requestBodyBytes,
        byte[]? responseBodyBytes)
    {
        Interlocked.Increment(ref _invocationCount);

        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return;
        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null) return;

        var method = new System.Net.Http.HttpMethod(httpMethod);
        var op = ElasticsearchOperationClassifier.Classify(method, requestUri);

        if (_options.ExcludedOperations.Contains(op.Operation)) return;

        var label = ElasticsearchOperationClassifier.GetDiagramLabel(op, effectiveVerbosity);
        var uri = ElasticsearchOperationClassifier.BuildUri(op, effectiveVerbosity, requestUri);

        string? requestBody = null;
        if (effectiveVerbosity != ElasticsearchTrackingVerbosity.Summarised
            && requestBodyBytes is not null)
        {
            requestBody = Encoding.UTF8.GetString(requestBodyBytes);
        }

        string? responseBody = null;
        if (effectiveVerbosity != ElasticsearchTrackingVerbosity.Summarised
            && _options.LogResponseContent
            && responseBodyBytes is not null)
        {
            responseBody = Encoding.UTF8.GetString(responseBodyBytes);
        }

        var traceId = Guid.NewGuid();
        var requestResponseId = Guid.NewGuid();

        RequestResponseLogger.Log(new RequestResponseLog(
            testInfo.Value.Name, testInfo.Value.Id,
            label,
            requestBody,
            uri,
            [],
            _options.ServiceName, _options.CallerName,
            RequestResponseType.Request, traceId, requestResponseId, false,
            StatusCode: statusCode.HasValue
                ? (System.Net.HttpStatusCode)statusCode.Value
                : null,
            DependencyCategory: DependencyCategories.Elasticsearch)
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v =>
            {
                var vRequestBody = v != ElasticsearchTrackingVerbosity.Summarised && requestBodyBytes is not null
                    ? Encoding.UTF8.GetString(requestBodyBytes) : null;
                return new PhaseVariant(
                    ElasticsearchOperationClassifier.GetDiagramLabel(op, v),
                    ElasticsearchOperationClassifier.BuildUri(op, v, requestUri),
                    vRequestBody, [], false);
            }));

        RequestResponseLogger.Log(new RequestResponseLog(
            testInfo.Value.Name, testInfo.Value.Id,
            label,
            responseBody,
            uri,
            [],
            _options.ServiceName, _options.CallerName,
            RequestResponseType.Response, traceId, requestResponseId, false,
            StatusCode: statusCode.HasValue
                ? (System.Net.HttpStatusCode)statusCode.Value
                : null,
            DependencyCategory: DependencyCategories.Elasticsearch)
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v =>
            {
                var vResponseBody = v != ElasticsearchTrackingVerbosity.Summarised
                    && _options.LogResponseContent && responseBodyBytes is not null
                    ? Encoding.UTF8.GetString(responseBodyBytes) : null;
                return new PhaseVariant(
                    ElasticsearchOperationClassifier.GetDiagramLabel(op, v),
                    ElasticsearchOperationClassifier.BuildUri(op, v, requestUri),
                    vResponseBody, [], false);
            }));
    }
}
