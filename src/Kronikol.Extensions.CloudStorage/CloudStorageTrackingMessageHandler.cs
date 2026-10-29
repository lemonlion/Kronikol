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

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null)
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var requestResponseId = Guid.NewGuid();
        var traceId = Guid.NewGuid();

        var label = CloudStorageOperationClassifier.GetDiagramLabel(gcsOp, effectiveVerbosity);

        // Each note is made at its own verbosity from bodies read once: a phase's variant was given the effective
        // verbosity's note, so a Detailed phase of a Summarised handler had neither body.
        var drawnAt = PhaseConfiguration.DrawnAt(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);
        var requestBody = await ReadRequestBody(request, drawnAt, cancellationToken);
        var requestContent = effectiveVerbosity == CloudStorageTrackingVerbosity.Summarised ? null : requestBody;
        var requestHeaders = GetFilteredHeaders(request, effectiveVerbosity);

        OneOf<HttpMethod, string> method = effectiveVerbosity == CloudStorageTrackingVerbosity.Raw
            ? request.Method
            : label;

        var requestUri = effectiveVerbosity == CloudStorageTrackingVerbosity.Raw
            ? request.RequestUri!
            : BuildCleanUri(gcsOp, request.RequestUri!);

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
            Phase = TestPhaseContext.Current,
            RecordedBefore9 = RecordedBefore9(request, effectiveVerbosity, method, requestUri)
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                v == CloudStorageTrackingVerbosity.Raw ? request.Method : CloudStorageOperationClassifier.GetDiagramLabel(gcsOp, v),
                v == CloudStorageTrackingVerbosity.Raw ? request.RequestUri! : BuildCleanUri(gcsOp, request.RequestUri!),
                v == CloudStorageTrackingVerbosity.Summarised ? null : requestBody,
                GetFilteredHeaders(request, v),
                Skip: false)));

        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A send that throws is still an answer, as the HTTP handler records it: the exception's type where the
            // status would be, its message chain in Error, and the exception on its way (FailedSend). Before #125 it left
            // a request with no response, which history's earlier rules still read.
            RequestResponseLogger.Log(new RequestResponseLog(
                testInfo.Value.Name,
                testInfo.Value.Id,
                method,
                null,
                requestUri,
                [],
                _options.ServiceName,
                _options.CallerName,
                RequestResponseType.Response,
                traceId,
                requestResponseId,
                false,
                FailedSend.Status(ex),
                DependencyCategory: DependencyCategories.CloudStorage
            )
            {
                Error = FailedSend.Describe(ex),
                AttributionSource = testInfo.Value.Source,
                Phase = TestPhaseContext.Current,
                RecordedBefore9 = RecordedBefore.NotRecorded
            }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
                v => new PhaseVariant(
                    v == CloudStorageTrackingVerbosity.Raw ? request.Method : CloudStorageOperationClassifier.GetDiagramLabel(gcsOp, v),
                    v == CloudStorageTrackingVerbosity.Raw ? request.RequestUri! : BuildCleanUri(gcsOp, request.RequestUri!),
                    null,
                    [],
                    Skip: false)));
            throw;
        }

        var responseBody = await ReadResponseBody(response, drawnAt, cancellationToken);
        var responseHeaders = GetFilteredHeaders(response, effectiveVerbosity);

        RequestResponseLogger.Log(new RequestResponseLog(
            testInfo.Value.Name,
            testInfo.Value.Id,
            method,
            effectiveVerbosity == CloudStorageTrackingVerbosity.Summarised ? null : responseBody,
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
                v == CloudStorageTrackingVerbosity.Raw ? request.RequestUri! : BuildCleanUri(gcsOp, request.RequestUri!),
                v == CloudStorageTrackingVerbosity.Summarised ? null : responseBody,
                GetFilteredHeaders(response, v),
                Skip: false)));

        return response;
    }

    // Read when any verbosity the call may be drawn at shows a body, as Detailed and Raw do and Summarised does not.
    private async Task<string?> ReadRequestBody(HttpRequestMessage request, CloudStorageTrackingVerbosity[] drawnAt, CancellationToken ct)
    {
        if (request.Content is null) return null;
        if (drawnAt.All(v => v == CloudStorageTrackingVerbosity.Summarised)) return null;
        return await HttpContentReader.ReadContentAsStringAsync(request.Content, ct);
    }

    private async Task<string?> ReadResponseBody(HttpResponseMessage response, CloudStorageTrackingVerbosity[] drawnAt, CancellationToken ct)
    {
        if (drawnAt.All(v => v == CloudStorageTrackingVerbosity.Summarised)) return null;
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

    /// <summary>
    /// How every release before #125's fix recorded this call, when that differs from how it is recorded now: its label
    /// and URI, or that it was not recorded at all (Summarised dropped a call it could not name). History's rules 6 to 8
    /// read the call from here (<see cref="RequestResponseLog.RecordedBefore9"/>). Raw records the call as sent, then
    /// and now.
    /// </summary>
    private static RecordedBefore? RecordedBefore9(HttpRequestMessage request, CloudStorageTrackingVerbosity verbosity,
        OneOf<HttpMethod, string> method, Uri uri)
    {
        if (verbosity == CloudStorageTrackingVerbosity.Raw)
            return null;
        var then = CloudStorageOperationClassifierBefore9.Classify(request);
        if (verbosity == CloudStorageTrackingVerbosity.Summarised && then.Operation == CloudStorageOperation.Other)
            return RecordedBefore.NotRecorded;
        var label = CloudStorageOperationClassifierBefore9.GetDiagramLabel(then, verbosity);
        var thenUri = CloudStorageOperationClassifierBefore9.BuildCleanUri(then);
        return label == method.Value?.ToString() && thenUri.ToString() == uri.ToString() ? null : new RecordedBefore(label, thenUri);
    }

    // A call no member names is drawn with the API's own path, without the client's base or its query (#125); a named
    // one with its bucket and object.
    private static Uri BuildCleanUri(CloudStorageOperationInfo op, Uri originalUri)
    {
        if (op.Operation == CloudStorageOperation.Other)
            return new Uri("gcs://" + CloudStorageOperationClassifier.ApiPathOf(originalUri));

        if (op.BucketName is null)
            return new Uri("gcs:///");

        if (op.ObjectName is not null)
            return new Uri($"gcs:///{op.BucketName}/{op.ObjectName}");

        return new Uri($"gcs:///{op.BucketName}");
    }
}
