using Kronikol.Constants;
using Microsoft.AspNetCore.Http;
using Kronikol.Tracking;

namespace Kronikol.Extensions.BlobStorage;

/// <summary>
/// A <see cref="DelegatingHandler" /> that intercepts and classifies BlobStorage HTTP operations for inclusion in test diagrams.
/// </summary>
public class BlobTrackingMessageHandler : DelegatingHandler, ITrackingComponent
{
    private readonly BlobTrackingMessageHandlerOptions _options;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private int _invocationCount;

    public BlobTrackingMessageHandler(BlobTrackingMessageHandlerOptions options, HttpMessageHandler? innerHandler = null, IHttpContextAccessor? httpContextAccessor = null)
    {
        _options = options;
        _httpContextAccessor = httpContextAccessor ?? options.HttpContextAccessor;
        InnerHandler = innerHandler ?? new HttpClientHandler();
        TrackingComponentRegistry.Register(this);
    }

    public string ComponentName => $"BlobTrackingMessageHandler ({_options.ServiceName})";
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

        var blobOp = BlobOperationClassifier.Classify(request);

        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        // Skip internal/metadata operations when in Summarised mode
        if (effectiveVerbosity == BlobTrackingVerbosity.Summarised && blobOp.Operation == BlobOperation.Other)
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null)
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var requestResponseId = Guid.NewGuid();
        var traceId = Guid.NewGuid();

        var label = BlobOperationClassifier.GetDiagramLabel(blobOp, effectiveVerbosity);

        var requestContent = await GetRequestContent(request, effectiveVerbosity, cancellationToken);
        var requestHeaders = GetFilteredHeaders(request, effectiveVerbosity);

        OneOf<HttpMethod, string> method = effectiveVerbosity == BlobTrackingVerbosity.Raw
            ? request.Method
            : label;

        var requestUri = effectiveVerbosity == BlobTrackingVerbosity.Raw
            ? request.RequestUri!
            : BuildCleanUri(request.RequestUri!, blobOp, effectiveVerbosity);

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
            DependencyCategory: DependencyCategories.BlobStorage
        )
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                v == BlobTrackingVerbosity.Raw ? request.Method : BlobOperationClassifier.GetDiagramLabel(blobOp, v),
                v == BlobTrackingVerbosity.Raw ? request.RequestUri! : BuildCleanUri(request.RequestUri!, blobOp, v),
                v == BlobTrackingVerbosity.Summarised ? null : requestContent,
                GetFilteredHeaders(request, v),
                v == BlobTrackingVerbosity.Summarised && blobOp.Operation == BlobOperation.Other)));

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
            DependencyCategory: DependencyCategories.BlobStorage
        )
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                v == BlobTrackingVerbosity.Raw ? request.Method : BlobOperationClassifier.GetDiagramLabel(blobOp, v),
                v == BlobTrackingVerbosity.Raw ? request.RequestUri! : BuildCleanUri(request.RequestUri!, blobOp, v),
                v == BlobTrackingVerbosity.Summarised ? null : responseContent,
                GetFilteredHeaders(response, v),
                v == BlobTrackingVerbosity.Summarised && blobOp.Operation == BlobOperation.Other)));

        return response;
    }

    private async Task<string?> GetRequestContent(HttpRequestMessage request, BlobTrackingVerbosity verbosity, CancellationToken ct)
    {
        if (request.Content is null)
            return null;

        if (verbosity == BlobTrackingVerbosity.Summarised)
            return null;

        return await HttpContentReader.ReadContentAsStringAsync(request.Content, ct);
    }

    private async Task<string?> GetResponseContent(HttpResponseMessage response, BlobTrackingVerbosity verbosity, CancellationToken ct)
    {
        if (verbosity == BlobTrackingVerbosity.Summarised)
            return null;

        return await HttpContentReader.ReadContentAsStringAsync(response.Content, ct);
    }

    private (string Key, string? Value)[] GetFilteredHeaders(HttpRequestMessage request, BlobTrackingVerbosity verbosity)
    {
        if (verbosity == BlobTrackingVerbosity.Summarised)
            return [];

        return request.Headers
            .Where(h => !_options.ExcludedHeaders.Contains(h.Key))
            .SelectMany(h => h.Value.Select(v => (h.Key, (string?)v)))
            .ToArray();
    }

    private (string Key, string? Value)[] GetFilteredHeaders(HttpResponseMessage response, BlobTrackingVerbosity verbosity)
    {
        if (verbosity == BlobTrackingVerbosity.Summarised)
            return [];

        return response.Headers
            .Where(h => !_options.ExcludedHeaders.Contains(h.Key))
            .SelectMany(h => h.Value.Select(v => (h.Key, (string?)v)))
            .ToArray();
    }

    private static Uri BuildCleanUri(Uri originalUri, BlobOperationInfo op, BlobTrackingVerbosity verbosity)
    {
        if (op.ContainerName is null)
            return originalUri;

        if (verbosity == BlobTrackingVerbosity.Summarised)
        {
            var builder = new UriBuilder(originalUri)
            {
                Path = op.BlobName is not null
                    ? $"/{op.ContainerName}/{op.BlobName}"
                    : $"/{op.ContainerName}",
                Query = ""
            };
            return builder.Uri;
        }

        // Detailed: container/blob, strip query params
        var cleanPath = op.BlobName is not null
            ? $"/{op.ContainerName}/{op.BlobName}"
            : $"/{op.ContainerName}";

        var uriBuilder = new UriBuilder(originalUri) { Path = cleanPath, Query = "" };
        return uriBuilder.Uri;
    }
}
