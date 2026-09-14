using Kronikol.Constants;
using Microsoft.AspNetCore.Http;
using Kronikol.Tracking;

namespace Kronikol.Extensions.CloudStorage;

/// <summary>
/// A <see cref="DelegatingHandler" /> that intercepts and classifies CloudStorage HTTP operations for inclusion in test diagrams.
/// </summary>
public class CloudStorageTrackingMessageHandler : DelegatingHandler, ITrackingComponent
{
    private readonly CloudStorageTrackingMessageHandlerOptions _options;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private int _invocationCount;

    public CloudStorageTrackingMessageHandler(CloudStorageTrackingMessageHandlerOptions options, HttpMessageHandler? innerHandler = null, IHttpContextAccessor? httpContextAccessor = null)
    {
        _options = options;
        _httpContextAccessor = httpContextAccessor ?? options.HttpContextAccessor;
        InnerHandler = innerHandler ?? new HttpClientHandler();
        TrackingComponentRegistry.Register(this);
    }

    public string ComponentName => $"CloudStorageTrackingMessageHandler ({_options.ServiceName})";
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

        var gcsOp = CloudStorageOperationClassifier.Classify(request);

        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        if (effectiveVerbosity == CloudStorageTrackingVerbosity.Summarised && gcsOp.Operation == CloudStorageOperation.Other)
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null)
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var requestResponseId = Guid.NewGuid();
        var traceId = Guid.NewGuid();

        var label = CloudStorageOperationClassifier.GetDiagramLabel(gcsOp, effectiveVerbosity);

        var requestContent = await GetRequestContent(request, effectiveVerbosity, cancellationToken);
        var requestHeaders = GetFilteredHeaders(request, effectiveVerbosity);

        OneOf<HttpMethod, string> method = effectiveVerbosity == CloudStorageTrackingVerbosity.Raw
            ? request.Method
            : label;

        var requestUri = effectiveVerbosity == CloudStorageTrackingVerbosity.Raw
            ? request.RequestUri!
            : BuildCleanUri(gcsOp);

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
            DependencyCategory: DependencyCategories.CloudStorage
        )
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                v == CloudStorageTrackingVerbosity.Raw ? request.Method : CloudStorageOperationClassifier.GetDiagramLabel(gcsOp, v),
                v == CloudStorageTrackingVerbosity.Raw ? request.RequestUri! : BuildCleanUri(gcsOp),
                v == CloudStorageTrackingVerbosity.Summarised ? null : requestContent,
                GetFilteredHeaders(request, v),
                v == CloudStorageTrackingVerbosity.Summarised && gcsOp.Operation == CloudStorageOperation.Other)));

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var responseContent = await GetResponseContent(response, effectiveVerbosity, cancellationToken);
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
            DependencyCategory: DependencyCategories.CloudStorage
        )
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                v == CloudStorageTrackingVerbosity.Raw ? request.Method : CloudStorageOperationClassifier.GetDiagramLabel(gcsOp, v),
                v == CloudStorageTrackingVerbosity.Raw ? request.RequestUri! : BuildCleanUri(gcsOp),
                v == CloudStorageTrackingVerbosity.Summarised ? null : responseContent,
                GetFilteredHeaders(response, v),
                v == CloudStorageTrackingVerbosity.Summarised && gcsOp.Operation == CloudStorageOperation.Other)));

        return response;
    }

    private async Task<string?> GetRequestContent(HttpRequestMessage request, CloudStorageTrackingVerbosity verbosity, CancellationToken ct)
    {
        if (request.Content is null) return null;
        if (verbosity == CloudStorageTrackingVerbosity.Summarised) return null;
        return await HttpContentReader.ReadContentAsStringAsync(request.Content, ct);
    }

    private async Task<string?> GetResponseContent(HttpResponseMessage response, CloudStorageTrackingVerbosity verbosity, CancellationToken ct)
    {
        if (verbosity == CloudStorageTrackingVerbosity.Summarised) return null;
        return await HttpContentReader.ReadContentAsStringAsync(response.Content, ct);
    }

    private (string Key, string? Value)[] GetFilteredHeaders(HttpRequestMessage request, CloudStorageTrackingVerbosity verbosity)
    {
        if (verbosity == CloudStorageTrackingVerbosity.Summarised) return [];
        return request.Headers
            .Where(h => !_options.ExcludedHeaders.Contains(h.Key))
            .SelectMany(h => h.Value.Select(v => (h.Key, (string?)v)))
            .ToArray();
    }

    private (string Key, string? Value)[] GetFilteredHeaders(HttpResponseMessage response, CloudStorageTrackingVerbosity verbosity)
    {
        if (verbosity == CloudStorageTrackingVerbosity.Summarised) return [];
        return response.Headers
            .Where(h => !_options.ExcludedHeaders.Contains(h.Key))
            .SelectMany(h => h.Value.Select(v => (h.Key, (string?)v)))
            .ToArray();
    }

    private static Uri BuildCleanUri(CloudStorageOperationInfo op)
    {
        if (op.BucketName is null)
            return new Uri("gcs:///");

        if (op.ObjectName is not null)
            return new Uri($"gcs:///{op.BucketName}/{op.ObjectName}");

        return new Uri($"gcs:///{op.BucketName}");
    }
}
