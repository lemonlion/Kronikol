using Kronikol.Constants;
using Microsoft.AspNetCore.Http;
using Kronikol.Tracking;

namespace Kronikol.Extensions.CosmosDB;

/// <summary>
/// A <see cref="DelegatingHandler" /> that intercepts and classifies CosmosDB HTTP operations for inclusion in test diagrams.
/// </summary>
public class CosmosTrackingMessageHandler : DelegatingHandler, ITrackingComponent
{
    private readonly CosmosTrackingMessageHandlerOptions _options;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private int _invocationCount;

    public CosmosTrackingMessageHandler(CosmosTrackingMessageHandlerOptions options, HttpMessageHandler? innerHandler = null, IHttpContextAccessor? httpContextAccessor = null)
    {
        _options = options;
        _httpContextAccessor = httpContextAccessor ?? options.HttpContextAccessor;
        InnerHandler = innerHandler ?? new HttpClientHandler();
        TrackingComponentRegistry.Register(this);
    }

    public string ComponentName => $"CosmosTrackingMessageHandler ({_options.ServiceName})";
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

        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        var cosmosOp = CosmosOperationClassifier.Classify(request);

        // Skip internal/metadata operations when in Summarised mode
        if (effectiveVerbosity == CosmosTrackingVerbosity.Summarised && cosmosOp.Operation == CosmosOperation.Other)
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null)
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var requestResponseId = Guid.NewGuid();
        var traceId = Guid.NewGuid();

        var label = CosmosOperationClassifier.GetDiagramLabel(cosmosOp, effectiveVerbosity);

        var requestContent = await GetRequestContent(request, cosmosOp, effectiveVerbosity, cancellationToken);
        var requestHeaders = GetFilteredHeaders(request, effectiveVerbosity);

        // Use the label as the "method" for Detailed/Summarised, or the real HTTP method for Raw
        OneOf<HttpMethod, string> method = effectiveVerbosity == CosmosTrackingVerbosity.Raw
            ? request.Method
            : label;

        var requestUri = effectiveVerbosity == CosmosTrackingVerbosity.Raw
            ? request.RequestUri!
            : BuildCleanUri(request.RequestUri!, cosmosOp, effectiveVerbosity);

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
            DependencyCategory: DependencyCategories.CosmosDB
        )
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                v == CosmosTrackingVerbosity.Raw ? request.Method : CosmosOperationClassifier.GetDiagramLabel(cosmosOp, v),
                v == CosmosTrackingVerbosity.Raw ? request.RequestUri! : BuildCleanUri(request.RequestUri!, cosmosOp, v),
                v == CosmosTrackingVerbosity.Summarised ? null : requestContent,
                GetFilteredHeaders(request, v),
                v == CosmosTrackingVerbosity.Summarised && cosmosOp.Operation == CosmosOperation.Other)));

        HttpResponseMessage response;
        string? responseContent;
        try
        {
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            responseContent = await GetResponseContent(response, effectiveVerbosity, cancellationToken);
        }
        catch (Exception ex)
        {
            // A call that threw (an emulator refusing the connection, a timeout) is still an answer: the
            // exception's type where the status would be and its message chain in Error. See FailedSend.
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
                DependencyCategory: DependencyCategories.CosmosDB)
            {
                Error = FailedSend.Describe(ex),
                AttributionSource = testInfo.Value.Source,
                Phase = TestPhaseContext.Current
            });
            throw;
        }
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
            DependencyCategory: DependencyCategories.CosmosDB
        )
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                v == CosmosTrackingVerbosity.Raw ? request.Method : CosmosOperationClassifier.GetDiagramLabel(cosmosOp, v),
                v == CosmosTrackingVerbosity.Raw ? request.RequestUri! : BuildCleanUri(request.RequestUri!, cosmosOp, v),
                v == CosmosTrackingVerbosity.Summarised && !_options.LogResponseContent ? null : responseContent,
                GetFilteredHeaders(response, v),
                v == CosmosTrackingVerbosity.Summarised && cosmosOp.Operation == CosmosOperation.Other)));

        AutoCorrelateIfWrite(cosmosOp, testInfo.Value, response, responseContent);

        return response;
    }

    private void AutoCorrelateIfWrite(CosmosOperationInfo cosmosOp, TestIdentity testInfo, HttpResponseMessage response, string? responseContent)
    {
        if (!_options.AutoCorrelateWrites) return;
        if (!response.IsSuccessStatusCode) return;

        var isWrite = cosmosOp.Operation is CosmosOperation.Create
            or CosmosOperation.Upsert
            or CosmosOperation.Replace;
        if (!isWrite) return;

        var documentId = cosmosOp.DocumentId ?? ExtractIdFromResponseContent(responseContent);
        if (documentId is null) return;

        var key = _options.ChangeFeedKeyExtractor is not null
            ? _options.ChangeFeedKeyExtractor(_options.ServiceName, documentId)
            : CorrelationKeys.Cosmos(_options.ServiceName, documentId);

        TestCorrelationStore.Correlate(key, testInfo.Name, testInfo.Id);
    }

    private static string? ExtractIdFromResponseContent(string? content)
    {
        if (string.IsNullOrEmpty(content)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("id", out var idElement))
                return idElement.GetString();
        }
        catch { }
        return null;
    }

    private async Task<string?> GetRequestContent(HttpRequestMessage request, CosmosOperationInfo op, CosmosTrackingVerbosity verbosity, CancellationToken ct)
    {
        if (request.Content is null)
            return null;

        if (verbosity == CosmosTrackingVerbosity.Summarised)
            return op.QueryText;

        var content = await HttpContentReader.ReadContentAsStringAsync(request.Content, ct);

        if (verbosity == CosmosTrackingVerbosity.Detailed && op.Operation == CosmosOperation.Query)
            return op.QueryText;

        return content;
    }

    private async Task<string?> GetResponseContent(HttpResponseMessage response, CosmosTrackingVerbosity verbosity, CancellationToken ct)
    {
        if (verbosity == CosmosTrackingVerbosity.Summarised && !_options.LogResponseContent)
            return null;

        return await HttpContentReader.ReadContentAsStringAsync(response.Content, ct);
    }

    private (string Key, string? Value)[] GetFilteredHeaders(HttpRequestMessage request, CosmosTrackingVerbosity verbosity)
    {
        if (verbosity == CosmosTrackingVerbosity.Summarised)
            return [];

        return request.Headers
            .Where(h => !_options.ExcludedHeaders.Contains(h.Key))
            .SelectMany(h => h.Value.Select(v => (h.Key, (string?)v)))
            .ToArray();
    }

    private (string Key, string? Value)[] GetFilteredHeaders(HttpResponseMessage response, CosmosTrackingVerbosity verbosity)
    {
        if (verbosity == CosmosTrackingVerbosity.Summarised)
            return [];

        return response.Headers
            .Where(h => !_options.ExcludedHeaders.Contains(h.Key))
            .SelectMany(h => h.Value.Select(v => (h.Key, (string?)v)))
            .ToArray();
    }

    private static Uri BuildCleanUri(Uri originalUri, CosmosOperationInfo op, CosmosTrackingVerbosity verbosity)
    {
        if (op.CollectionName is null)
            return originalUri;

        if (verbosity == CosmosTrackingVerbosity.Summarised)
        {
            var builder = new UriBuilder(originalUri) { Path = $"/{op.CollectionName}" };
            return builder.Uri;
        }

        // Detailed: /colls/{coll} with optional resource path, no db prefix
        var parts = new List<string> { $"colls/{op.CollectionName}" };

        if (op.DocumentId is not null)
        {
            var resourceType = op.Operation == CosmosOperation.ExecStoredProc ? "sprocs" : "docs";
            parts.Add($"{resourceType}/{op.DocumentId}");
        }

        var cleanPath = "/" + string.Join("/", parts);
        var uriBuilder = new UriBuilder(originalUri) { Path = cleanPath };
        return uriBuilder.Uri;
    }
}
