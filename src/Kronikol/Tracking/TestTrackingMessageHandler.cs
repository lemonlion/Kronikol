using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Kronikol.Constants;

namespace Kronikol.Tracking;

/// <summary>
/// A <see cref="DelegatingHandler"/> that intercepts HTTP requests and responses,
/// logging them as <see cref="RequestResponseLog"/> entries for inclusion in test sequence diagrams.
/// This is the primary mechanism for tracking HTTP dependencies in tests.
/// </summary>
public class TestTrackingMessageHandler : DelegatingHandler, ITrackingComponent
{
    private readonly string? _fixedServiceName;
    private readonly Func<int, string> _getServiceNameFromPortTranslator;
    private readonly Dictionary<string, string> _clientNamesToServiceNames;
    private readonly string? _clientName;
    private readonly string? _callerName;
    private readonly Func<(string Name, string Id)>? _currentTestInfoFetcher;
    private readonly Func<string?>? _currentStepTypeFetcher;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly IEnumerable<string> _headersToForward;
    private readonly string[]? _internalFlowActivitySources;
    private readonly bool _trackDuringSetup;
    private readonly bool _trackDuringAction;
    private readonly IReadOnlyCollection<string> _excludedHosts;
    private string? _lastStepType;
    private bool _wasInGivenSection;
    private bool _actionStartInjected;
    private int _invocationCount;
    private bool _listenerStarted;

    public TestTrackingMessageHandler(TestTrackingMessageHandlerOptions options, IHttpContextAccessor? httpContextAccessor = null, string? clientName = null)
    {
        _fixedServiceName = options.FixedNameForReceivingService;
        _getServiceNameFromPortTranslator = GetPortTranslator(options.PortsToServiceNames);
        _clientNamesToServiceNames = options.ClientNamesToServiceNames;
        _clientName = clientName;
        _currentTestInfoFetcher = options.CurrentTestInfoFetcher;
        _currentStepTypeFetcher = options.CurrentStepTypeFetcher;
        _callerName = options.CallerName;
        _httpContextAccessor = httpContextAccessor ?? options.HttpContextAccessor;
        _headersToForward = options.HeadersToForward;
        _internalFlowActivitySources = options.InternalFlowActivitySources;
        _trackDuringSetup = options.TrackDuringSetup;
        _trackDuringAction = options.TrackDuringAction;
        _excludedHosts = options.ExcludedHosts;

        TrackingComponentRegistry.Register(this);
    }

    public string ComponentName => $"TestTrackingMessageHandler ({_callerName})";
    public bool WasInvoked => _invocationCount > 0;
    public int InvocationCount => _invocationCount;
    public bool HasHttpContextAccessor => _httpContextAccessor is not null;

    private static Func<int, string> GetPortTranslator(Dictionary<int, string> serviceNamesForEachPort)
    {
        return port => serviceNamesForEachPort.TryGetValue(port, out var serviceName) ? serviceName : $"localhost:{port}";
    }

    private string ResolveServiceName(int port)
    {
        // 1. FixedNameForReceivingService (highest priority)
        if (_fixedServiceName is not null)
            return _fixedServiceName;

        // 2. Client name mapping — exact match (set via constructor clientName parameter)
        if (_clientName is not null && _clientNamesToServiceNames.TryGetValue(_clientName, out var mapped))
            return mapped;

        // 3. Client name mapping — suffix/contains match (for Refit/generated client names)
        //    Steps: strip assembly qualification, try EndsWith with boundary, then Contains fallback
        //    (Contains only when assembly-qualified, to avoid false positives on simple names).
        if (_clientName is not null && _clientNamesToServiceNames.Count > 0)
        {
            // Strip assembly qualification (e.g. ", Data.Insights.Api, Version=...")
            var commaIndex = _clientName.IndexOf(", ", StringComparison.Ordinal);
            var isAssemblyQualified = commaIndex >= 0;
            var effectiveName = isAssemblyQualified ? _clientName.AsSpan(0, commaIndex) : _clientName.AsSpan();

            foreach (var kvp in _clientNamesToServiceNames)
            {
                // 3a. EndsWith with non-alphanumeric boundary (e.g. Namespace+IClient)
                if (effectiveName.Length > kvp.Key.Length
                    && effectiveName.EndsWith(kvp.Key.AsSpan(), StringComparison.Ordinal)
                    && !char.IsLetterOrDigit(effectiveName[effectiveName.Length - kvp.Key.Length - 1]))
                    return kvp.Value;

                // 3b. Contains fallback — only for assembly-qualified names (Refit v9)
                //     to avoid false positives on simple names like "Client" matching "MyBetterClient"
                if (isAssemblyQualified
                    && effectiveName.Length > kvp.Key.Length
                    && effectiveName.Contains(kvp.Key.AsSpan(), StringComparison.Ordinal))
                    return kvp.Value;
            }

            // Record unmatched client name for diagnostic reporting
            UnmatchedClientNameRegistry.Record(_clientName);
        }

        // 4. Port-based mapping or fallback to localhost:port
        return _getServiceNameFromPortTranslator(port);
    }

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Forward without tracking. Synchronous callers are typically infrastructure
        // (TestContainers, Docker, config fetches) — not test-initiated requests.
        // The previous .GetAwaiter().GetResult() pattern caused deadlocks (issue #69).
        InnerHandler ??= new HttpClientHandler();
        return base.Send(request, cancellationToken);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Lazy-initialize InnerHandler for standalone usage (new HttpClient(handler)).
        // When used with HttpClientFactory / IHttpMessageHandlerBuilderFilter, the pipeline
        // builder sets InnerHandler before first use — so this only fires for direct construction.
        InnerHandler ??= new HttpClientHandler();

        Interlocked.Increment(ref _invocationCount);

        // Deferred start — registering an ActivityListener during DI resolution
        // can alter ActivitySource.HasListeners() state before the host and
        // Application Insights' DependencyTrackingTelemetryModule have fully
        // initialised, breaking HTTP dependency telemetry. Starting on first
        // use guarantees all other services are ready.
        if (!Volatile.Read(ref _listenerStarted))
        {
            InternalFlow.InternalFlowActivityListener.EnsureStarted(_internalFlowActivitySources);
            Volatile.Write(ref _listenerStarted, true);
        }

        // Skip tracking entirely for excluded hosts (e.g. ASP.NET Core TestServer's internal "override.com")
        if (request.RequestUri is not null && _excludedHosts.Count > 0 &&
            _excludedHosts.Contains(request.RequestUri.Host, StringComparer.OrdinalIgnoreCase))
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        ForwardHeaders(request);

        var requestResponseId = Guid.NewGuid();

        // Ensure trace context propagation for in-process (TestServer) scenarios
        // where no framework DiagnosticsHandler exists in the pipeline.
        // When Activity.Current IS present, a framework handler (e.g.
        // DiagnosticsHandler inside SocketsHttpHandler) will create a proper
        // child Activity and inject traceparent itself — pre-empting it here
        // would inject the PARENT's span ID, breaking AI SDK dependency
        // correlation. We therefore only inject when no ambient Activity exists.
        string? activityTraceId;
        string? activitySpanId;
        if (Activity.Current != null)
        {
            activityTraceId = Activity.Current.TraceId.ToString();
            activitySpanId = Activity.Current.SpanId.ToString();
        }
        else
        {
            activityTraceId = ActivityTraceId.CreateRandom().ToString();
            activitySpanId = ActivitySpanId.CreateRandom().ToString();
            if (!request.Headers.Contains("traceparent"))
            {
                request.Headers.TryAddWithoutValidation("traceparent",
                    $"00-{activityTraceId}-{activitySpanId}-00");
            }
        }

        var requestContentString = request.Content is null ? null : await HttpContentReader.ReadContentAsStringAsync(request.Content!, cancellationToken);
        var requestHeaders = request.Headers.SelectMany(x => x.Value.Select(value => (x.Key, (string?)value))).ToArray();

        StringValues currentTestNameHeaders = new();
        var hasCurrentTestNameHeader = false;

        StringValues currentTestIdHeaders = new();
        var hasCurrentTestIdHeader = false;

        StringValues callerNameHeaders = new();
        var hasCallerNameHeader = false;

        StringValues traceIdHeaders = new();
        var hasTraceIdHeader = false;

        if (_httpContextAccessor?.HttpContext is not null)
        {
            hasTraceIdHeader = _httpContextAccessor.HttpContext.Request.Headers.TryGetValue(TestTrackingHttpHeaders.TraceIdHeader, out traceIdHeaders);
            hasCurrentTestNameHeader = _httpContextAccessor.HttpContext.Request.Headers.TryGetValue(TestTrackingHttpHeaders.CurrentTestNameHeader, out currentTestNameHeaders);
            hasCurrentTestIdHeader = _httpContextAccessor.HttpContext.Request.Headers.TryGetValue(TestTrackingHttpHeaders.CurrentTestIdHeader, out currentTestIdHeaders);
            hasCallerNameHeader = _httpContextAccessor.HttpContext.Request.Headers.TryGetValue(TestTrackingHttpHeaders.CallerNameHeader, out callerNameHeaders);
        }

        // Resolve test info once. A request that carries the scenario's own headers is that scenario's
        // (RequestHeader); anything else goes through the shared chain, which honours a detached flow
        // and says which level answered. Nothing resolved: skip all tracking and just forward.
        TestIdentity currentTestInfo;
        if (hasCurrentTestNameHeader && hasCurrentTestIdHeader)
        {
            currentTestInfo = new TestIdentity(currentTestNameHeaders.First()!, currentTestIdHeaders.First()!, AttributionSource.RequestHeader);
        }
        else
        {
            var resolved = TestInfoResolver.ResolveWithSource(null, _currentTestInfoFetcher);
            if (resolved is null)
                return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            currentTestInfo = resolved.Value;
        }

        var traceId = hasTraceIdHeader ? Guid.Parse(traceIdHeaders.First()!) : Guid.NewGuid();

        if (!hasTraceIdHeader)
            request.Headers.Add(TestTrackingHttpHeaders.TraceIdHeader, new[] { traceId.ToString() });

        if (!hasCurrentTestNameHeader)
            request.Headers.Add(TestTrackingHttpHeaders.CurrentTestNameHeader, new[] { currentTestInfo.Name });

        if (!hasCurrentTestIdHeader)
            request.Headers.Add(TestTrackingHttpHeaders.CurrentTestIdHeader, new[] { currentTestInfo.Id.ToString() });

        if (!hasCallerNameHeader)
            request.Headers.Add(TestTrackingHttpHeaders.CallerNameHeader, new[] { _callerName! });

        var serviceName = ResolveServiceName(request.RequestUri!.Port);

        if (!hasCurrentTestNameHeader)
            InjectImplicitActionStartIfNeeded(currentTestInfo);

        var requestFocusFields = !hasCurrentTestNameHeader ? DiagramFocus.ConsumePendingRequestFocus() : null;
        var responseFocusFields = !hasCurrentTestNameHeader ? DiagramFocus.ConsumePendingResponseFocus() : null;

        var trackingIgnore = requestHeaders.Any(x => x.Key == TestTrackingHttpHeaders.Ignore)
                             || !PhaseConfiguration.ShouldTrack(_trackDuringSetup, _trackDuringAction);
        var currentPhase = TestPhaseContext.Current;

        RequestResponseLogger.Log(new RequestResponseLog(
            currentTestInfo.Name,
            currentTestInfo.Id,
            request.Method,
            requestContentString,
            request.RequestUri!,
            requestHeaders,
            serviceName,
            _callerName!,
            RequestResponseType.Request,
            traceId,
            requestResponseId,
            trackingIgnore
        )
        {
            FocusFields = requestFocusFields,
            Timestamp = DateTimeOffset.UtcNow,
            ActivitySpanId = activitySpanId,
            ActivityTraceId = activityTraceId,
            Phase = currentPhase,
            AttributionSource = currentTestInfo.Source
        });

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var responseContentString = await HttpContentReader.ReadContentAsStringAsync(response.Content, cancellationToken);
        var responseHeaders = response.Headers.SelectMany(x => x.Value.Select(value => (x.Key, (string?)value))).ToArray();

        RequestResponseLogger.Log(new RequestResponseLog(
            currentTestInfo.Name,
            currentTestInfo.Id,
            request.Method,
            responseContentString,
            request.RequestUri!,
            responseHeaders,
            serviceName,
            _callerName!,
            RequestResponseType.Response,
            traceId,
            requestResponseId,
            trackingIgnore,
            response.StatusCode
            )
        {
            FocusFields = responseFocusFields,
            Timestamp = DateTimeOffset.UtcNow,
            ActivitySpanId = activitySpanId,
            ActivityTraceId = activityTraceId,
            Phase = currentPhase,
            AttributionSource = currentTestInfo.Source
        });

        return response;
    }

    private void ForwardHeaders(HttpRequestMessage request)
    {
        if(!_headersToForward.Any())
            return;

        var contextHeaders = _httpContextAccessor?.HttpContext?.Request.Headers;
        if (contextHeaders is null)
            return;

        foreach (var header in _headersToForward)
        {
            if (contextHeaders.TryGetValue(header, out var value))
                request.Headers.Add(header, (IEnumerable<string?>)value);
        }
    }

    private void InjectImplicitActionStartIfNeeded(TestIdentity currentTestInfo)
    {
        if (_actionStartInjected || _currentStepTypeFetcher is null)
            return;

        var currentStepType = _currentStepTypeFetcher();
        if (currentStepType is null)
        {
            _lastStepType = null;
            return;
        }

        var isGivenOrAnd = currentStepType.StartsWith("GIVEN", StringComparison.OrdinalIgnoreCase)
                           || currentStepType.StartsWith("AND", StringComparison.OrdinalIgnoreCase)
                           || currentStepType.StartsWith("BUT", StringComparison.OrdinalIgnoreCase);

        if (isGivenOrAnd)
        {
            _wasInGivenSection = true;
        }
        else if (_wasInGivenSection)
        {
            _actionStartInjected = true;
            DefaultTrackingDiagramOverride.StartAction(currentTestInfo.Id);
        }

        _lastStepType = currentStepType;
    }
}