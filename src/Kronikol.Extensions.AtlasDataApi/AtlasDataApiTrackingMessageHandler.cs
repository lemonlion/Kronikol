using Kronikol.Constants;
using Microsoft.AspNetCore.Http;
using Kronikol.Tracking;

namespace Kronikol.Extensions.AtlasDataApi;

/// <summary>
/// A <see cref="DelegatingHandler" /> that intercepts and classifies AtlasDataApi HTTP operations for inclusion in test diagrams.
/// </summary>
public class AtlasDataApiTrackingMessageHandler : DelegatingHandler, ITrackingComponent
{
    private readonly AtlasDataApiTrackingMessageHandlerOptions _options;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private int _invocationCount;

    public AtlasDataApiTrackingMessageHandler(
        AtlasDataApiTrackingMessageHandlerOptions options,
        HttpMessageHandler? innerHandler = null,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _options = options;
        _httpContextAccessor = httpContextAccessor ?? options.HttpContextAccessor;
        InnerHandler = innerHandler ?? new HttpClientHandler();
        TrackingComponentRegistry.Register(this);
    }

    public string ComponentName => $"AtlasDataApiTrackingMessageHandler ({_options.ServiceName})";
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

        // Buffer the request body so it can be read for classification and logging
        string? bodyJson = null;
        if (request.Content is not null)
        {
            bodyJson = await HttpContentReader.ReadContentAsStringAsync(request.Content, cancellationToken).ConfigureAwait(false);
            request.Content = new StringContent(bodyJson!, System.Text.Encoding.UTF8, "application/json");
        }

        var apiOp = AtlasDataApiOperationClassifier.Classify(request, bodyJson);

        if (_options.ExcludedOperations.Contains(apiOp.Operation))
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!PhaseConfiguration.ShouldTrack(_options.TrackDuringSetup, _options.TrackDuringAction))
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(
            _options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity);

        // Skip unrecognised operations in Summarised mode
        if (effectiveVerbosity == AtlasDataApiTrackingVerbosity.Summarised
            && apiOp.Operation == AtlasDataApiOperation.Other)
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var testInfo = TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher);
        if (testInfo is null)
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var requestResponseId = Guid.NewGuid();
        var traceId = Guid.NewGuid();

        var label = AtlasDataApiOperationClassifier.GetDiagramLabel(apiOp, effectiveVerbosity);

        var requestContent = GetRequestContent(bodyJson, effectiveVerbosity);
        var requestHeaders = GetFilteredHeaders(request, effectiveVerbosity);

        OneOf<HttpMethod, string> method = effectiveVerbosity == AtlasDataApiTrackingVerbosity.Raw
            ? request.Method
            : label;

        var requestUri = effectiveVerbosity == AtlasDataApiTrackingVerbosity.Raw
            ? request.RequestUri!
            : BuildCleanUri(apiOp);

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
            DependencyCategory: DependencyCategories.AtlasDataApi
        )
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                v == AtlasDataApiTrackingVerbosity.Raw ? request.Method : AtlasDataApiOperationClassifier.GetDiagramLabel(apiOp, v),
                v == AtlasDataApiTrackingVerbosity.Raw ? request.RequestUri! : BuildCleanUri(apiOp),
                GetRequestContent(bodyJson, v),
                GetFilteredHeaders(request, v),
                v == AtlasDataApiTrackingVerbosity.Summarised && apiOp.Operation == AtlasDataApiOperation.Other)));

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
            DependencyCategory: DependencyCategories.AtlasDataApi
        )
        {
            AttributionSource = testInfo.Value.Source,
            Phase = TestPhaseContext.Current
        }.WithVariants(_options.Verbosity, _options.SetupVerbosity, _options.ActionVerbosity,
            v => new PhaseVariant(
                v == AtlasDataApiTrackingVerbosity.Raw ? request.Method : AtlasDataApiOperationClassifier.GetDiagramLabel(apiOp, v),
                v == AtlasDataApiTrackingVerbosity.Raw ? request.RequestUri! : BuildCleanUri(apiOp),
                v == AtlasDataApiTrackingVerbosity.Summarised ? null : responseContent,
                GetFilteredHeaders(response, v),
                v == AtlasDataApiTrackingVerbosity.Summarised && apiOp.Operation == AtlasDataApiOperation.Other)));

        return response;
    }

    private static string? GetRequestContent(string? bodyJson, AtlasDataApiTrackingVerbosity verbosity)
    {
        if (bodyJson is null) return null;
        if (verbosity == AtlasDataApiTrackingVerbosity.Summarised) return null;
        return bodyJson;
    }

    private static async Task<string?> GetResponseContent(
        HttpResponseMessage response, AtlasDataApiTrackingVerbosity verbosity, CancellationToken ct)
    {
        if (verbosity == AtlasDataApiTrackingVerbosity.Summarised) return null;
        return await HttpContentReader.ReadContentAsStringAsync(response.Content, ct);
    }

    private (string Key, string? Value)[] GetFilteredHeaders(
        HttpRequestMessage request, AtlasDataApiTrackingVerbosity verbosity)
    {
        if (verbosity == AtlasDataApiTrackingVerbosity.Summarised) return [];
        return request.Headers
            .Where(h => !_options.ExcludedHeaders.Contains(h.Key))
            .SelectMany(h => h.Value.Select(v => (h.Key, (string?)v)))
            .ToArray();
    }

    private (string Key, string? Value)[] GetFilteredHeaders(
        HttpResponseMessage response, AtlasDataApiTrackingVerbosity verbosity)
    {
        if (verbosity == AtlasDataApiTrackingVerbosity.Summarised) return [];
        return response.Headers
            .Where(h => !_options.ExcludedHeaders.Contains(h.Key))
            .SelectMany(h => h.Value.Select(v => (h.Key, (string?)v)))
            .ToArray();
    }

    internal static Uri BuildCleanUri(AtlasDataApiOperationInfo op)
    {
        var parts = new List<string>();
        if (op.DatabaseName is not null) parts.Add(op.DatabaseName);
        if (op.CollectionName is not null) parts.Add(op.CollectionName);

        return parts.Count > 0
            ? new Uri($"atlas:///{string.Join("/", parts)}")
            : new Uri("atlas:///");
    }
}
