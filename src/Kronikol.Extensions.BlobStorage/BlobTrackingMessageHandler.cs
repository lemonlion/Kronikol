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

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null)
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var requestResponseId = Guid.NewGuid();
        var traceId = Guid.NewGuid();

        var label = BlobOperationClassifier.GetDiagramLabel(blobOp, effectiveVerbosity);

        // Each note is made at its own verbosity from bodies read once: a phase's variant was given the effective
        // verbosity's note, so a Detailed phase of a Summarised handler had neither body.
        var drawnAt = PhaseConfiguration.DrawnAt(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);
        var requestBody = await ReadRequestBody(request, drawnAt, cancellationToken);
        var requestContent = effectiveVerbosity == BlobTrackingVerbosity.Summarised ? null : requestBody;
        var requestHeaders = GetFilteredHeaders(request, effectiveVerbosity);

        OneOf<HttpMethod, string> method = effectiveVerbosity == BlobTrackingVerbosity.Raw
            ? request.Method
            : label;

        var requestUri = effectiveVerbosity == BlobTrackingVerbosity.Raw
            ? request.RequestUri!
            : BuildCleanUri(request.RequestUri!, blobOp, effectiveVerbosity);

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
            DependencyCategory: DependencyCategories.BlobStorage
        )
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current,
            RecordedBefore11 = RecordedBefore11(request, effectiveVerbosity, method, requestUri)
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                v == BlobTrackingVerbosity.Raw ? request.Method : BlobOperationClassifier.GetDiagramLabel(blobOp, v),
                v == BlobTrackingVerbosity.Raw ? request.RequestUri! : BuildCleanUri(request.RequestUri!, blobOp, v),
                v == BlobTrackingVerbosity.Summarised ? null : requestBody,
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
            effectiveVerbosity == BlobTrackingVerbosity.Summarised ? null : responseBody,
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
                v == BlobTrackingVerbosity.Summarised ? null : responseBody,
                GetFilteredHeaders(response, v),
                Skip: false)));

        return response;
    }

    // Read when any verbosity the call may be drawn at shows a body, as Detailed and Raw do and Summarised does not.
    private async Task<string?> ReadRequestBody(HttpRequestMessage request, BlobTrackingVerbosity[] drawnAt, CancellationToken ct)
    {
        if (request.Content is null)
            return null;

        if (drawnAt.All(v => v == BlobTrackingVerbosity.Summarised))
            return null;

        return await HttpContentReader.ReadContentAsStringAsync(request.Content, ct);
    }

    private async Task<string?> ReadResponseBody(HttpResponseMessage response, BlobTrackingVerbosity[] drawnAt, CancellationToken ct)
    {
        if (drawnAt.All(v => v == BlobTrackingVerbosity.Summarised))
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
        // A call on the account keeps its URL as sent, query and all, less the account segment Azurite puts in the
        // path, so it reads on Azurite as it does on Azure.
        if (op.ContainerName is null)
        {
            var path = BlobOperationClassifier.ApiPathOf(originalUri);
            return path == originalUri.AbsolutePath ? originalUri : new UriBuilder(originalUri) { Path = path }.Uri;
        }

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

    /// <summary>
    /// How every release before #125's R2 recorded this call, when that differs from how it is recorded now: its label
    /// and URI, or that it was not recorded at all (Summarised dropped a call it could not name). History's rules 6 to
    /// 10 read the call from here (<see cref="RequestResponseLog.RecordedBefore11"/>). Raw records the call as sent, then
    /// and now.
    /// </summary>
    private static RecordedBefore? RecordedBefore11(HttpRequestMessage request, BlobTrackingVerbosity verbosity,
        OneOf<HttpMethod, string> method, Uri uri)
    {
        if (verbosity == BlobTrackingVerbosity.Raw)
            return null;
        var then = BlobOperationClassifierBefore11.Classify(request);
        if (verbosity == BlobTrackingVerbosity.Summarised && then.Operation == BlobOperation.Other)
            return RecordedBefore.NotRecorded;
        var label = BlobOperationClassifierBefore11.GetDiagramLabel(then, verbosity);
        var thenUri = BlobOperationClassifierBefore11.BuildCleanUri(request.RequestUri!, then, verbosity);
        return label == method.Value?.ToString() && thenUri.ToString() == uri.ToString() ? null : new RecordedBefore(label, thenUri);
    }
}
