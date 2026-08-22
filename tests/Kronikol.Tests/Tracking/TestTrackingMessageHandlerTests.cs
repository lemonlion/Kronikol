using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Http;
using Kronikol.Constants;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

[Collection("DiagramsFetcher")]
public class TestTrackingMessageHandlerTests : IDisposable
{
    // ─── Test infrastructure ────────────────────────────────────

    /// <summary>
    /// A simple inner handler that captures the request and returns a canned response.
    /// This replaces the real HTTP call so we can inspect what the handler does.
    /// </summary>
    private class StubInnerHandler : HttpMessageHandler
    {
        public HttpRequestMessage? CapturedRequest { get; private set; }
        public HttpResponseMessage ResponseToReturn { get; set; } = new(HttpStatusCode.OK) { Content = new StringContent("response-body") };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedRequest = request;
            return Task.FromResult(ResponseToReturn);
        }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedRequest = request;
            return ResponseToReturn;
        }
    }

    private readonly StubInnerHandler _innerHandler = new();
    private readonly string _testId = Guid.NewGuid().ToString();

    private RequestResponseLog[] GetLogsFromThisTest()
    {
        return RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == _testId)
            .ToArray();
    }

    private HttpMessageInvoker CreateInvoker(TestTrackingMessageHandlerOptions options, IHttpContextAccessor? httpContextAccessor = null, string? clientName = null)
    {
        var handler = new TestTrackingMessageHandler(options, httpContextAccessor, clientName)
        {
            InnerHandler = _innerHandler
        };
        return new HttpMessageInvoker(handler);
    }

    private TestTrackingMessageHandlerOptions DefaultOptions(
        string callerName = "TestCaller",
        string? fixedServiceName = "TargetService") => new()
    {
        CallerName = callerName,
        FixedNameForReceivingService = fixedServiceName,
        CurrentTestInfoFetcher = () => ("My Test", _testId),
    };

    private static HttpRequestMessage MakeGetRequest(string url = "http://target-service:5000/api/items")
    {
        return new HttpRequestMessage(HttpMethod.Get, url);
    }

    public void Dispose()
    {
        _innerHandler.Dispose();
    }

    // ─── Basic request/response logging ─────────────────────────

    [Fact]
    public async Task Logs_both_a_request_and_a_response_for_each_call()
    {
        using var invoker = CreateInvoker(DefaultOptions());

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal(RequestResponseType.Request, logs[0].Type);
        Assert.Equal(RequestResponseType.Response, logs[1].Type);
    }

    [Fact]
    public async Task Request_log_contains_correct_method_and_uri()
    {
        using var invoker = CreateInvoker(DefaultOptions());
        var request = new HttpRequestMessage(HttpMethod.Post, "http://target-service:5000/api/items");

        await invoker.SendAsync(request, CancellationToken.None);

        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal(HttpMethod.Post, requestLog.Method.Value);
        Assert.Equal(new Uri("http://target-service:5000/api/items"), requestLog.Uri);
    }

    [Fact]
    public async Task Request_log_captures_request_body()
    {
        using var invoker = CreateInvoker(DefaultOptions());
        var request = new HttpRequestMessage(HttpMethod.Post, "http://target-service:5000/api/items")
        {
            Content = new StringContent("request-body-content")
        };

        await invoker.SendAsync(request, CancellationToken.None);

        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("request-body-content", requestLog.Content);
    }

    [Fact]
    public async Task Request_log_has_null_content_when_no_body()
    {
        using var invoker = CreateInvoker(DefaultOptions());

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Null(requestLog.Content);
    }

    [Fact]
    public async Task Response_log_captures_response_body()
    {
        _innerHandler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("custom-response-body")
        };
        using var invoker = CreateInvoker(DefaultOptions());

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var responseLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Equal("custom-response-body", responseLog.Content);
    }

    [Fact]
    public async Task Response_log_captures_status_code()
    {
        _innerHandler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("")
        };
        using var invoker = CreateInvoker(DefaultOptions());

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var responseLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Equal(HttpStatusCode.NotFound, responseLog.StatusCode!.Value);
    }

    [Fact]
    public async Task Response_log_captures_response_headers()
    {
        _innerHandler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("")
        };
        _innerHandler.ResponseToReturn.Headers.Add("X-Custom-Response", "resp-value");
        using var invoker = CreateInvoker(DefaultOptions());

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var responseLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Contains(responseLog.Headers, h => h.Key == "X-Custom-Response" && h.Value == "resp-value");
    }

    // ─── Test info from CurrentTestInfoFetcher ──────────────────

    [Fact]
    public async Task Logs_test_name_and_id_from_current_test_info_fetcher()
    {
        var options = new TestTrackingMessageHandlerOptions
        {
            FixedNameForReceivingService = "Svc",
            CallerName = "Caller",
            CurrentTestInfoFetcher = () => ("Fetched Test Name", _testId),
        };
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("Fetched Test Name", requestLog.TestName);
        Assert.Equal(_testId, requestLog.TestId);
    }

    // ─── Service name resolution ────────────────────────────────

    [Fact]
    public async Task Uses_fixed_service_name_when_configured()
    {
        var options = DefaultOptions(fixedServiceName: "MyFixedService");
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("MyFixedService", requestLog.ServiceName);
    }

    [Fact]
    public async Task Uses_port_to_service_name_mapping_when_no_fixed_name()
    {
        var options = new TestTrackingMessageHandlerOptions
        {
            PortsToServiceNames = new Dictionary<int, string> { { 5000, "MappedService" } },
            CallerName = "Caller",
            CurrentTestInfoFetcher = () => ("Test", _testId),
        };
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://localhost:5000/api"), CancellationToken.None);

        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("MappedService", requestLog.ServiceName);
    }

    [Fact]
    public async Task Falls_back_to_localhost_port_when_port_not_in_mapping()
    {
        var options = new TestTrackingMessageHandlerOptions
        {
            PortsToServiceNames = new Dictionary<int, string> { { 9999, "Other" } },
            CallerName = "Caller",
            CurrentTestInfoFetcher = () => ("Test", _testId),
        };
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://localhost:7777/api"), CancellationToken.None);

        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("localhost:7777", requestLog.ServiceName);
    }

    [Fact]
    public async Task Uses_client_name_to_service_name_mapping_when_client_name_provided()
    {
        var options = new TestTrackingMessageHandlerOptions
        {
            ClientNamesToServiceNames = new Dictionary<string, string> { { "AccountClient", "Account Service" } },
            CallerName = "Caller",
            CurrentTestInfoFetcher = () => ("Test", _testId),
        };
        using var invoker = CreateInvoker(options, clientName: "AccountClient");

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://localhost:5000/api"), CancellationToken.None);

        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("Account Service", requestLog.ServiceName);
    }

    [Fact]
    public async Task Client_name_mapping_takes_priority_over_port_mapping()
    {
        var options = new TestTrackingMessageHandlerOptions
        {
            ClientNamesToServiceNames = new Dictionary<string, string> { { "PaymentClient", "Payment Service" } },
            PortsToServiceNames = new Dictionary<int, string> { { 5000, "Port-Based Service" } },
            CallerName = "Caller",
            CurrentTestInfoFetcher = () => ("Test", _testId),
        };
        using var invoker = CreateInvoker(options, clientName: "PaymentClient");

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://localhost:5000/api"), CancellationToken.None);

        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("Payment Service", requestLog.ServiceName);
    }

    [Fact]
    public async Task Falls_back_to_port_mapping_when_client_name_not_in_dictionary()
    {
        var options = new TestTrackingMessageHandlerOptions
        {
            ClientNamesToServiceNames = new Dictionary<string, string> { { "OtherClient", "Other" } },
            PortsToServiceNames = new Dictionary<int, string> { { 5000, "Port Service" } },
            CallerName = "Caller",
            CurrentTestInfoFetcher = () => ("Test", _testId),
        };
        using var invoker = CreateInvoker(options, clientName: "UnknownClient");

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://localhost:5000/api"), CancellationToken.None);

        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("Port Service", requestLog.ServiceName);
    }

    [Fact]
    public async Task Fixed_service_name_takes_priority_over_client_name_mapping()
    {
        var options = new TestTrackingMessageHandlerOptions
        {
            FixedNameForReceivingService = "Fixed Service",
            ClientNamesToServiceNames = new Dictionary<string, string> { { "MyClient", "Client Service" } },
            CallerName = "Caller",
            CurrentTestInfoFetcher = () => ("Test", _testId),
        };
        using var invoker = CreateInvoker(options, clientName: "MyClient");

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://localhost:5000/api"), CancellationToken.None);

        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("Fixed Service", requestLog.ServiceName);
    }

    // ─── Ends-with matching for client names (#65) ────────────

    [Fact]
    public async Task EndsWith_matching_resolves_client_name_when_exact_match_fails()
    {
        var options = new TestTrackingMessageHandlerOptions
        {
            ClientNamesToServiceNames = new Dictionary<string, string> { { "IIntelligenceAiApiClient", "Intelligence AI" } },
            CallerName = "Caller",
            CurrentTestInfoFetcher = () => ("Test", _testId),
        };
        using var invoker = CreateInvoker(options, clientName: "Refit.Implementation.Generated+SomeModule+IIntelligenceAiApiClient");

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://localhost:5000/api"), CancellationToken.None);

        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("Intelligence AI", requestLog.ServiceName);
    }

    [Fact]
    public async Task Exact_match_takes_priority_over_endswith_match()
    {
        var options = new TestTrackingMessageHandlerOptions
        {
            ClientNamesToServiceNames = new Dictionary<string, string>
            {
                { "IIntelligenceAiApiClient", "EndsWith Match" },
                { "Refit.Implementation.Generated+SomeModule+IIntelligenceAiApiClient", "Exact Match" },
            },
            CallerName = "Caller",
            CurrentTestInfoFetcher = () => ("Test", _testId),
        };
        using var invoker = CreateInvoker(options, clientName: "Refit.Implementation.Generated+SomeModule+IIntelligenceAiApiClient");

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://localhost:5000/api"), CancellationToken.None);

        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("Exact Match", requestLog.ServiceName);
    }

    [Fact]
    public async Task EndsWith_matching_does_not_record_unmatched_client_name()
    {
        UnmatchedClientNameRegistry.Clear();
        var options = new TestTrackingMessageHandlerOptions
        {
            ClientNamesToServiceNames = new Dictionary<string, string> { { "IIntelligenceAiApiClient", "Intelligence AI" } },
            CallerName = "Caller",
            CurrentTestInfoFetcher = () => ("Test", _testId),
        };
        using var invoker = CreateInvoker(options, clientName: "Refit.Implementation.Generated+SomeModule+IIntelligenceAiApiClient");

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://localhost:5000/api"), CancellationToken.None);

        Assert.Empty(UnmatchedClientNameRegistry.GetRecordedNames());
        UnmatchedClientNameRegistry.Clear();
    }

    [Fact]
    public async Task EndsWith_matching_requires_non_alphanumeric_boundary()
    {
        var options = new TestTrackingMessageHandlerOptions
        {
            ClientNamesToServiceNames = new Dictionary<string, string> { { "Client", "Should Not Match" } },
            PortsToServiceNames = new Dictionary<int, string> { { 5000, "Port Fallback" } },
            CallerName = "Caller",
            CurrentTestInfoFetcher = () => ("Test", _testId),
        };
        // "MyBetterClient" ends with "Client" but the preceding char 'r' is a letter — should NOT match
        using var invoker = CreateInvoker(options, clientName: "MyBetterClient");

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://localhost:5000/api"), CancellationToken.None);

        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("Port Fallback", requestLog.ServiceName);
    }

    [Fact]
    public async Task Contains_matching_resolves_assembly_qualified_refit_v9_names()
    {
        var options = new TestTrackingMessageHandlerOptions
        {
            ClientNamesToServiceNames = new Dictionary<string, string> { { "IIntelligenceAiApiClient", "Intelligence AI" } },
            CallerName = "Caller",
            CurrentTestInfoFetcher = () => ("Test", _testId),
        };
        // Refit v9 assembly-qualified name: type portion concatenates without separators before the interface
        using var invoker = CreateInvoker(options, clientName: "Refit.Implementation.Generated+DataInsightsApiHttpClientsIntelligenceAiIIntelligenceAiApiClient, Data.Insights.Api, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://localhost:5000/api"), CancellationToken.None);

        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("Intelligence AI", requestLog.ServiceName);
    }

    [Fact]
    public async Task Contains_matching_does_not_apply_to_non_assembly_qualified_names()
    {
        var options = new TestTrackingMessageHandlerOptions
        {
            ClientNamesToServiceNames = new Dictionary<string, string> { { "Client", "Should Not Match" } },
            PortsToServiceNames = new Dictionary<int, string> { { 5000, "Port Fallback" } },
            CallerName = "Caller",
            CurrentTestInfoFetcher = () => ("Test", _testId),
        };
        // Simple name without assembly qualification — Contains fallback should NOT activate
        using var invoker = CreateInvoker(options, clientName: "MyBetterClient");

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://localhost:5000/api"), CancellationToken.None);

        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("Port Fallback", requestLog.ServiceName);
    }

    // ─── Unmatched client name tracking (#10) ───────────────────

    [Fact]
    public async Task Unmatched_client_name_is_recorded_when_not_in_dictionary()
    {
        UnmatchedClientNameRegistry.Clear();
        var options = new TestTrackingMessageHandlerOptions
        {
            ClientNamesToServiceNames = new Dictionary<string, string> { { "OtherClient", "Other" } },
            PortsToServiceNames = new Dictionary<int, string> { { 5000, "Port Service" } },
            CallerName = "Caller",
            CurrentTestInfoFetcher = () => ("Test", _testId),
        };
        using var invoker = CreateInvoker(options, clientName: "TenantHierarchyHttpClient");

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://localhost:5000/api"), CancellationToken.None);

        var unmatched = UnmatchedClientNameRegistry.GetRecordedNames();
        Assert.Contains(unmatched, e => e.ClientName == "TenantHierarchyHttpClient");
        UnmatchedClientNameRegistry.Clear();
    }

    [Fact]
    public async Task Matched_client_name_is_not_recorded()
    {
        UnmatchedClientNameRegistry.Clear();
        var options = new TestTrackingMessageHandlerOptions
        {
            ClientNamesToServiceNames = new Dictionary<string, string> { { "AccountClient", "Account Service" } },
            CallerName = "Caller",
            CurrentTestInfoFetcher = () => ("Test", _testId),
        };
        using var invoker = CreateInvoker(options, clientName: "AccountClient");

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://localhost:5000/api"), CancellationToken.None);

        var unmatched = UnmatchedClientNameRegistry.GetRecordedNames();
        Assert.DoesNotContain(unmatched, e => e.ClientName == "AccountClient");
        UnmatchedClientNameRegistry.Clear();
    }

    [Fact]
    public async Task No_client_name_does_not_record_unmatched()
    {
        UnmatchedClientNameRegistry.Clear();
        var options = new TestTrackingMessageHandlerOptions
        {
            ClientNamesToServiceNames = new Dictionary<string, string> { { "X", "Y" } },
            CallerName = "Caller",
            CurrentTestInfoFetcher = () => ("Test", _testId),
        };
        using var invoker = CreateInvoker(options, clientName: null);

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://localhost:5000/api"), CancellationToken.None);

        Assert.Empty(UnmatchedClientNameRegistry.GetRecordedNames());
        UnmatchedClientNameRegistry.Clear();
    }

    // ─── Caller name ────────────────────────────────────────────

    [Fact]
    public async Task Logs_calling_service_name_from_options()
    {
        var options = DefaultOptions(callerName: "MyApp");
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("MyApp", requestLog.CallerName);
    }

    // ─── TraceId generation and propagation ─────────────────────

    [Fact]
    public async Task Adds_trace_id_header_to_outgoing_request()
    {
        using var invoker = CreateInvoker(DefaultOptions());

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        Assert.True(_innerHandler.CapturedRequest!.Headers.Contains(TestTrackingHttpHeaders.TraceIdHeader));
    }

    [Fact]
    public async Task Request_and_response_logs_share_the_same_trace_id()
    {
        using var invoker = CreateInvoker(DefaultOptions());

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Equal(logs[0].TraceId, logs[1].TraceId);
    }

    [Fact]
    public async Task Request_and_response_logs_share_the_same_request_response_id()
    {
        using var invoker = CreateInvoker(DefaultOptions());

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Equal(logs[0].RequestResponseId, logs[1].RequestResponseId);
    }

    // ─── Tracking headers added to outgoing request ─────────────

    [Fact]
    public async Task Adds_test_name_header_to_outgoing_request()
    {
        var options = new TestTrackingMessageHandlerOptions
        {
            FixedNameForReceivingService = "Svc",
            CallerName = "Caller",
            CurrentTestInfoFetcher = () => ("Test Name Here", "id-42"),
        };
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var headerValue = _innerHandler.CapturedRequest!.Headers.GetValues(TestTrackingHttpHeaders.CurrentTestNameHeader).Single();
        Assert.Equal("Test Name Here", headerValue);
    }

    [Fact]
    public async Task Adds_test_id_header_to_outgoing_request()
    {
        var options = new TestTrackingMessageHandlerOptions
        {
            FixedNameForReceivingService = "Svc",
            CallerName = "Caller",
            CurrentTestInfoFetcher = () => ("Test", "my-test-id-99"),
        };
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var headerValue = _innerHandler.CapturedRequest!.Headers.GetValues(TestTrackingHttpHeaders.CurrentTestIdHeader).Single();
        Assert.Equal("my-test-id-99", headerValue);
    }

    [Fact]
    public async Task Adds_caller_name_header_to_outgoing_request()
    {
        var options = DefaultOptions(callerName: "OriginApp");
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var headerValue = _innerHandler.CapturedRequest!.Headers.GetValues(TestTrackingHttpHeaders.CallerNameHeader).Single();
        Assert.Equal("OriginApp", headerValue);
    }

    // ─── Tracking ignore header ─────────────────────────────────

    [Fact]
    public async Task Request_without_ignore_header_has_tracking_ignore_false()
    {
        using var invoker = CreateInvoker(DefaultOptions());

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.False(requestLog.TrackingIgnore);
    }

    [Fact]
    public async Task Request_with_ignore_header_has_tracking_ignore_true()
    {
        using var invoker = CreateInvoker(DefaultOptions());
        var request = MakeGetRequest();
        request.Headers.Add(TestTrackingHttpHeaders.Ignore, "true");

        await invoker.SendAsync(request, CancellationToken.None);

        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.True(requestLog.TrackingIgnore);
    }

    [Fact]
    public async Task Response_also_gets_tracking_ignore_from_request_headers()
    {
        using var invoker = CreateInvoker(DefaultOptions());
        var request = MakeGetRequest();
        request.Headers.Add(TestTrackingHttpHeaders.Ignore, "true");

        await invoker.SendAsync(request, CancellationToken.None);

        var responseLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.True(responseLog.TrackingIgnore);
    }

    // ─── Excluded hosts ─────────────────────────────────────────

    [Fact]
    public async Task Request_to_excluded_host_is_not_logged()
    {
        var options = DefaultOptions();
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://override.com/api/test"), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Empty(logs);
    }

    [Fact]
    public async Task Request_to_excluded_host_still_forwards_the_request()
    {
        var options = DefaultOptions();
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://override.com/api/test"), CancellationToken.None);

        Assert.NotNull(_innerHandler.CapturedRequest);
        Assert.Equal(new Uri("http://override.com/api/test"), _innerHandler.CapturedRequest!.RequestUri);
    }

    [Fact]
    public async Task Custom_excluded_hosts_are_respected()
    {
        var options = DefaultOptions();
        options.ExcludedHosts = ["my-internal-host.local", "override.com"];
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://my-internal-host.local/health"), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Empty(logs);
    }

    [Fact]
    public async Task Request_to_non_excluded_host_is_still_logged()
    {
        var options = DefaultOptions();
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://real-service:5000/api/data"), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
    }

    [Fact]
    public async Task Empty_excluded_hosts_disables_host_filtering()
    {
        var options = DefaultOptions();
        options.ExcludedHosts = [];
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://override.com/api/test"), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
    }

    // ─── Request headers captured in log ────────────────────────

    [Fact]
    public async Task Request_log_captures_original_request_headers()
    {
        using var invoker = CreateInvoker(DefaultOptions());
        var request = MakeGetRequest();
        request.Headers.Add("X-Custom", "my-value");

        await invoker.SendAsync(request, CancellationToken.None);

        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains(requestLog.Headers, h => h.Key == "X-Custom" && h.Value == "my-value");
    }

    // ─── HttpContextAccessor: headers from incoming context ─────

    private IHttpContextAccessor CreateHttpContextAccessor(params (string Key, string Value)[] headers)
    {
        var context = new DefaultHttpContext();
        foreach (var (key, value) in headers)
        {
            context.Request.Headers[key] = value;
        }
        return new StubHttpContextAccessor(context);
    }

    /// <summary>
    /// A simple accessor that avoids the static AsyncLocal in <see cref="HttpContextAccessor"/>
    /// which leaks context between parallel tests.
    /// </summary>
    private class StubHttpContextAccessor(HttpContext context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }

    [Fact]
    public async Task Uses_test_name_from_http_context_when_header_present()
    {
        var accessor = CreateHttpContextAccessor(
            (TestTrackingHttpHeaders.CurrentTestNameHeader, "Context Test Name"),
            (TestTrackingHttpHeaders.CurrentTestIdHeader, _testId));
        var options = DefaultOptions();
        using var invoker = CreateInvoker(options, accessor);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("Context Test Name", requestLog.TestName);
        Assert.Equal(_testId, requestLog.TestId);
    }

    [Fact]
    public async Task Uses_trace_id_from_http_context_when_header_present()
    {
        var traceGuid = Guid.NewGuid();
        var accessor = CreateHttpContextAccessor(
            (TestTrackingHttpHeaders.TraceIdHeader, traceGuid.ToString()),
            (TestTrackingHttpHeaders.CurrentTestNameHeader, "Test"),
            (TestTrackingHttpHeaders.CurrentTestIdHeader, _testId));
        using var invoker = CreateInvoker(DefaultOptions(), accessor);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal(traceGuid, requestLog.TraceId);
    }

    [Fact]
    public async Task Does_not_add_trace_id_header_when_already_present_in_context()
    {
        var traceGuid = Guid.NewGuid();
        var accessor = CreateHttpContextAccessor(
            (TestTrackingHttpHeaders.TraceIdHeader, traceGuid.ToString()),
            (TestTrackingHttpHeaders.CurrentTestNameHeader, "Test"),
            (TestTrackingHttpHeaders.CurrentTestIdHeader, _testId));
        using var invoker = CreateInvoker(DefaultOptions(), accessor);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        // The header should be present (forwarded), but only one value
        var traceHeaders = _innerHandler.CapturedRequest!.Headers
            .Where(h => h.Key == TestTrackingHttpHeaders.TraceIdHeader)
            .SelectMany(h => h.Value)
            .ToList();

        // Should NOT have added a duplicate — the handler skips adding when hasTraceIdHeader is true
        Assert.DoesNotContain(traceHeaders, v => v != traceGuid.ToString());
    }

    [Fact]
    public async Task Uses_caller_name_from_http_context_when_header_present()
    {
        var accessor = CreateHttpContextAccessor(
            (TestTrackingHttpHeaders.CallerNameHeader, "UpstreamCaller"),
            (TestTrackingHttpHeaders.CurrentTestNameHeader, "Test"),
            (TestTrackingHttpHeaders.CurrentTestIdHeader, _testId));
        using var invoker = CreateInvoker(DefaultOptions(), accessor);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        // When caller name header is present, it does NOT add a new one to the outgoing request
        // (the hasCallerNameHeader flag suppresses the add)
        var callerHeaders = _innerHandler.CapturedRequest!.Headers
            .Where(h => h.Key == TestTrackingHttpHeaders.CallerNameHeader)
            .SelectMany(h => h.Value)
            .ToList();
        Assert.DoesNotContain("TestCaller", callerHeaders);
    }

    [Fact]
    public async Task Without_http_context_accessor_uses_fetcher_and_generates_trace_id()
    {
        var options = new TestTrackingMessageHandlerOptions
        {
            FixedNameForReceivingService = "Svc",
            CallerName = "Caller",
            CurrentTestInfoFetcher = () => ("Fetcher Test", _testId),
        };
        using var invoker = CreateInvoker(options, httpContextAccessor: null);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("Fetcher Test", requestLog.TestName);
        Assert.Equal(_testId, requestLog.TestId);
        Assert.NotEqual(Guid.Empty, requestLog.TraceId);
    }

    // ─── Header forwarding ──────────────────────────────────────

    [Fact]
    public async Task Forwards_configured_headers_from_http_context_to_outgoing_request()
    {
        var accessor = CreateHttpContextAccessor(
            ("X-Correlation-Id", "corr-123"),
            (TestTrackingHttpHeaders.CurrentTestNameHeader, "Test"),
            (TestTrackingHttpHeaders.CurrentTestIdHeader, "id-1"));
        var options = new TestTrackingMessageHandlerOptions
        {
            FixedNameForReceivingService = "Svc",
            CallerName = "Caller",
            CurrentTestInfoFetcher = () => ("Test", "id-1"),
            HeadersToForward = ["X-Correlation-Id"],
        };
        using var invoker = CreateInvoker(options, accessor);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var forwardedValue = _innerHandler.CapturedRequest!.Headers.GetValues("X-Correlation-Id").Single();
        Assert.Equal("corr-123", forwardedValue);
    }

    [Fact]
    public async Task Does_not_forward_headers_that_are_not_in_context()
    {
        var accessor = CreateHttpContextAccessor(
            (TestTrackingHttpHeaders.CurrentTestNameHeader, "Test"),
            (TestTrackingHttpHeaders.CurrentTestIdHeader, "id-1"));
        var options = new TestTrackingMessageHandlerOptions
        {
            FixedNameForReceivingService = "Svc",
            CallerName = "Caller",
            CurrentTestInfoFetcher = () => ("Test", "id-1"),
            HeadersToForward = ["X-Missing-Header"],
        };
        using var invoker = CreateInvoker(options, accessor);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        Assert.False(_innerHandler.CapturedRequest!.Headers.Contains("X-Missing-Header"));
    }

    [Fact]
    public async Task Does_not_forward_headers_when_no_http_context_accessor()
    {
        var options = new TestTrackingMessageHandlerOptions
        {
            FixedNameForReceivingService = "Svc",
            CallerName = "Caller",
            CurrentTestInfoFetcher = () => ("Test", "id-1"),
            HeadersToForward = ["X-Forward-Me"],
        };
        using var invoker = CreateInvoker(options, httpContextAccessor: null);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        Assert.False(_innerHandler.CapturedRequest!.Headers.Contains("X-Forward-Me"));
    }

    [Fact]
    public async Task Does_not_forward_headers_when_empty_forwarding_list()
    {
        var accessor = CreateHttpContextAccessor(
            ("X-Something", "val"),
            (TestTrackingHttpHeaders.CurrentTestNameHeader, "Test"),
            (TestTrackingHttpHeaders.CurrentTestIdHeader, "id-1"));
        var options = new TestTrackingMessageHandlerOptions
        {
            FixedNameForReceivingService = "Svc",
            CallerName = "Caller",
            CurrentTestInfoFetcher = () => ("Test", "id-1"),
            HeadersToForward = [],
        };
        using var invoker = CreateInvoker(options, accessor);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        Assert.False(_innerHandler.CapturedRequest!.Headers.Contains("X-Something"));
    }

    // ─── Multiple concurrent requests ───────────────────────────

    [Fact]
    public async Task Multiple_requests_each_get_their_own_request_response_id()
    {
        using var invoker = CreateInvoker(DefaultOptions());

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);
        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        var firstPairId = logs[0].RequestResponseId;
        var secondPairId = logs[2].RequestResponseId;
        Assert.NotEqual(firstPairId, secondPairId);
    }

    // ─── Return value ───────────────────────────────────────────

    [Fact]
    public async Task Returns_the_response_from_the_inner_handler()
    {
        _innerHandler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("created-item")
        };
        using var invoker = CreateInvoker(DefaultOptions());

        var response = await invoker.SendAsync(MakeGetRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("created-item", body);
    }

    // ─── Context headers suppress adding new ones ───────────────

    [Fact]
    public async Task Does_not_add_test_name_header_when_already_in_context()
    {
        var accessor = CreateHttpContextAccessor(
            (TestTrackingHttpHeaders.CurrentTestNameHeader, "Existing Name"),
            (TestTrackingHttpHeaders.CurrentTestIdHeader, _testId));
        using var invoker = CreateInvoker(DefaultOptions(), accessor);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        // When context has the header, the handler skips adding it to the outgoing request.
        // The header from context is NOT auto-forwarded to HttpRequestMessage headers
        // (unless explicitly listed in HeadersToForward), so it won't appear.
        Assert.False(_innerHandler.CapturedRequest!.Headers.Contains(TestTrackingHttpHeaders.CurrentTestNameHeader));

        // But the log still uses the value from context
        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("Existing Name", requestLog.TestName);
    }

    [Fact]
    public async Task Does_not_add_test_id_header_when_already_in_context()
    {
        var accessor = CreateHttpContextAccessor(
            (TestTrackingHttpHeaders.CurrentTestNameHeader, "Test"),
            (TestTrackingHttpHeaders.CurrentTestIdHeader, _testId));
        using var invoker = CreateInvoker(DefaultOptions(), accessor);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        // Same as test name: context header suppresses adding, but isn't auto-forwarded
        Assert.False(_innerHandler.CapturedRequest!.Headers.Contains(TestTrackingHttpHeaders.CurrentTestIdHeader));

        // But the log still uses the value from context
        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal(_testId, requestLog.TestId);
    }

    // ─── Port-based service name with multiple mappings ─────────

    [Fact]
    public async Task Resolves_different_service_names_for_different_ports()
    {
        var options = new TestTrackingMessageHandlerOptions
        {
            PortsToServiceNames = new Dictionary<int, string>
            {
                { 5001, "ServiceA" },
                { 5002, "ServiceB" },
            },
            CallerName = "Caller",
            CurrentTestInfoFetcher = () => ("Test", _testId),
        };

        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://localhost:5001/api"), CancellationToken.None);

        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("ServiceA", requestLog.ServiceName);
    }

    // ─── Both request and response reference the same service ───

    [Fact]
    public async Task Request_and_response_logs_reference_same_service_and_caller()
    {
        var options = DefaultOptions(callerName: "MyCaller", fixedServiceName: "MyService");
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.All(logs, log =>
        {
            Assert.Equal("MyService", log.ServiceName);
            Assert.Equal("MyCaller", log.CallerName);
        });
    }

    // ─── Response logs same URI and method as request ────────────

    [Fact]
    public async Task Response_log_has_same_uri_and_method_as_request()
    {
        using var invoker = CreateInvoker(DefaultOptions());
        var request = new HttpRequestMessage(HttpMethod.Put, "http://target-service:5000/api/items/42");

        await invoker.SendAsync(request, CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Equal(logs[0].Uri, logs[1].Uri);
        Assert.Equal(logs[0].Method.Value, logs[1].Method.Value);
    }

    // ─── Forwarding multiple headers ────────────────────────────

    [Fact]
    public async Task Forwards_multiple_configured_headers()
    {
        var accessor = CreateHttpContextAccessor(
            ("X-Header-A", "val-a"),
            ("X-Header-B", "val-b"),
            (TestTrackingHttpHeaders.CurrentTestNameHeader, "Test"),
            (TestTrackingHttpHeaders.CurrentTestIdHeader, "id-1"));
        var options = new TestTrackingMessageHandlerOptions
        {
            FixedNameForReceivingService = "Svc",
            CallerName = "Caller",
            CurrentTestInfoFetcher = () => ("Test", "id-1"),
            HeadersToForward = ["X-Header-A", "X-Header-B"],
        };
        using var invoker = CreateInvoker(options, accessor);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        Assert.Equal("val-a", _innerHandler.CapturedRequest!.Headers.GetValues("X-Header-A").Single());
        Assert.Equal("val-b", _innerHandler.CapturedRequest!.Headers.GetValues("X-Header-B").Single());
    }

    // ─── HttpContextAccessor with null HttpContext ───────────────

    [Fact]
    public async Task Handles_http_context_accessor_with_null_context_gracefully()
    {
        var accessor = new HttpContextAccessor { HttpContext = null };
        var options = new TestTrackingMessageHandlerOptions
        {
            FixedNameForReceivingService = "Svc",
            CallerName = "Caller",
            CurrentTestInfoFetcher = () => ("Test", _testId),
            HeadersToForward = ["X-Something"],
        };
        using var invoker = CreateInvoker(options, accessor);

        // Should not throw — both SendAsync and ForwardHeaders now null-check HttpContext
        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        Assert.False(_innerHandler.CapturedRequest!.Headers.Contains("X-Something"));
        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("Test", requestLog.TestName);
    }

    // ─── Synchronous Send() method ──────────────────────────────

    [Fact]
    public void Sync_send_forwards_without_tracking()
    {
        var handler = new TestTrackingMessageHandler(DefaultOptions())
        {
            InnerHandler = _innerHandler
        };
        using var client = new HttpClient(handler);

        // Synchronous Send() forwards without tracking to avoid deadlocks (issue #69).
        // Infrastructure callers (Docker, TestContainers) use the sync path and don't need tracking.
        var response = client.Send(MakeGetRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Empty(logs);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ─── Request headers snapshot taken before tracking headers ──

    [Fact]
    public async Task Logged_request_headers_do_not_include_injected_tracking_headers()
    {
        using var invoker = CreateInvoker(DefaultOptions());
        var request = MakeGetRequest();
        request.Headers.Add("X-Original", "original-value");

        await invoker.SendAsync(request, CancellationToken.None);

        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);

        // The original header should be logged
        Assert.Contains(requestLog.Headers, h => h.Key == "X-Original" && h.Value == "original-value");

        // The tracking headers injected by the handler should NOT be in the logged headers
        // because the snapshot is taken before they're added
        Assert.DoesNotContain(requestLog.Headers, h => h.Key == TestTrackingHttpHeaders.TraceIdHeader);
        Assert.DoesNotContain(requestLog.Headers, h => h.Key == TestTrackingHttpHeaders.CurrentTestNameHeader);
        Assert.DoesNotContain(requestLog.Headers, h => h.Key == TestTrackingHttpHeaders.CurrentTestIdHeader);
        Assert.DoesNotContain(requestLog.Headers, h => h.Key == TestTrackingHttpHeaders.CallerNameHeader);
    }

    // ─── Multi-value headers ────────────────────────────────────

    [Fact]
    public async Task Multi_value_request_header_is_flattened_to_separate_entries()
    {
        using var invoker = CreateInvoker(DefaultOptions());
        var request = MakeGetRequest();
        request.Headers.Add("Accept", ["text/html", "application/json"]);

        await invoker.SendAsync(request, CancellationToken.None);

        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        var acceptHeaders = requestLog.Headers.Where(h => h.Key == "Accept").ToList();
        Assert.Equal(2, acceptHeaders.Count);
        Assert.Contains(acceptHeaders, h => h.Value == "text/html");
        Assert.Contains(acceptHeaders, h => h.Value == "application/json");
    }

    [Fact]
    public async Task Multi_value_response_header_is_flattened_to_separate_entries()
    {
        _innerHandler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("")
        };
        _innerHandler.ResponseToReturn.Headers.Add("X-Multi", ["val-1", "val-2"]);
        using var invoker = CreateInvoker(DefaultOptions());

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var responseLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        var multiHeaders = responseLog.Headers.Where(h => h.Key == "X-Multi").ToList();
        Assert.Equal(2, multiHeaders.Count);
        Assert.Contains(multiHeaders, h => h.Value == "val-1");
        Assert.Contains(multiHeaders, h => h.Value == "val-2");
    }

    // ─── Ignore header on response does not affect TrackingIgnore ──

    [Fact]
    public async Task Ignore_header_on_response_only_does_not_set_tracking_ignore()
    {
        _innerHandler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("")
        };
        _innerHandler.ResponseToReturn.Headers.Add(TestTrackingHttpHeaders.Ignore, "true");
        using var invoker = CreateInvoker(DefaultOptions());

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        // TrackingIgnore is derived from request headers, not response headers
        var responseLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.False(responseLog.TrackingIgnore);
    }

    // ─── Partial context headers (only name, no ID) ─────────────

    [Fact]
    public async Task Context_with_test_name_but_no_test_id_falls_back_to_fetcher()
    {
        // When only the name header is present (no ID header), the handler must NOT build
        // a partial lambda — both headers must be present for the context-header path to
        // activate. With only the name header, the handler falls back to _currentTestInfoFetcher
        // and tracking proceeds normally.
        var accessor = CreateHttpContextAccessor(
            (TestTrackingHttpHeaders.CurrentTestNameHeader, "Context Name Only"));
        var options = new TestTrackingMessageHandlerOptions
        {
            FixedNameForReceivingService = "Svc",
            CallerName = "Caller",
            CurrentTestInfoFetcher = () => ("Fetcher Name", _testId),
        };
        using var invoker = CreateInvoker(options, accessor);

        var response = await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal("Fetcher Name", logs[0].TestName);
    }

    // ─── Fixed service name ignores port completely ─────────────

    [Fact]
    public async Task Fixed_service_name_is_used_regardless_of_port()
    {
        var options = DefaultOptions(fixedServiceName: "AlwaysThisService");

        using var invoker1 = CreateInvoker(options);
        await invoker1.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://localhost:3000/api"), CancellationToken.None);

        using var invoker2 = CreateInvoker(options);
        await invoker2.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://localhost:9999/api"), CancellationToken.None);

        var logs = GetLogsFromThisTest().Where(l => l.Type == RequestResponseType.Request).ToList();
        Assert.All(logs, l => Assert.Equal("AlwaysThisService", l.ServiceName));
    }

    // ─── Implicit setup step detection ──────────────────────────

    private TestTrackingMessageHandlerOptions OptionsWithStepFetcher(Func<string?> stepTypeFetcher) => new()
    {
        CallerName = "Caller",
        FixedNameForReceivingService = "Svc",
        CurrentTestInfoFetcher = () => ("Test", _testId),
        CurrentStepTypeFetcher = stepTypeFetcher,
    };

    [Fact]
    public async Task Injects_IsActionStart_marker_when_step_transitions_from_GIVEN_to_WHEN()
    {
        var currentStep = "GIVEN";
        var options = OptionsWithStepFetcher(() => currentStep);
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        currentStep = "WHEN";
        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Contains(logs, l => l.IsActionStart);
    }

    [Fact]
    public async Task No_IsActionStart_marker_when_no_step_type_fetcher()
    {
        var options = DefaultOptions();
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);
        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.DoesNotContain(logs, l => l.IsActionStart);
    }

    [Fact]
    public async Task No_IsActionStart_marker_when_all_steps_are_GIVEN()
    {
        var options = OptionsWithStepFetcher(() => "GIVEN");
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);
        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.DoesNotContain(logs, l => l.IsActionStart);
    }

    [Fact]
    public async Task IsActionStart_marker_injected_before_first_WHEN_request()
    {
        var currentStep = "GIVEN";
        var options = OptionsWithStepFetcher(() => currentStep);
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        currentStep = "WHEN";
        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        var markerIndex = Array.FindIndex(logs, l => l.IsActionStart);
        var whenRequestIndex = Array.FindIndex(logs, markerIndex + 1, l => l.Type == RequestResponseType.Request && !l.IsActionStart);
        Assert.True(markerIndex < whenRequestIndex, "IsActionStart marker should appear before the WHEN request");
    }

    [Fact]
    public async Task Only_one_IsActionStart_marker_injected_across_multiple_WHEN_requests()
    {
        var currentStep = "GIVEN";
        var options = OptionsWithStepFetcher(() => currentStep);
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        currentStep = "WHEN";
        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);
        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Single(logs, l => l.IsActionStart);
    }

    [Fact]
    public async Task AND_steps_after_GIVEN_do_not_trigger_IsActionStart()
    {
        var currentStep = "GIVEN";
        var options = OptionsWithStepFetcher(() => currentStep);
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        currentStep = "AND";
        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.DoesNotContain(logs, l => l.IsActionStart);
    }

    [Fact]
    public async Task WHEN_after_AND_given_triggers_IsActionStart()
    {
        var currentStep = "GIVEN";
        var options = OptionsWithStepFetcher(() => currentStep);
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        currentStep = "AND";
        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        currentStep = "WHEN";
        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Contains(logs, l => l.IsActionStart);
    }

    [Fact]
    public async Task Step_type_detection_is_case_insensitive()
    {
        var currentStep = "given";
        var options = OptionsWithStepFetcher(() => currentStep);
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        currentStep = "when";
        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Contains(logs, l => l.IsActionStart);
    }

    [Fact]
    public async Task No_IsActionStart_when_first_step_is_WHEN_without_prior_GIVEN()
    {
        var options = OptionsWithStepFetcher(() => "WHEN");
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);
        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.DoesNotContain(logs, l => l.IsActionStart);
    }

    [Fact]
    public async Task THEN_after_GIVEN_triggers_IsActionStart()
    {
        var currentStep = "GIVEN";
        var options = OptionsWithStepFetcher(() => currentStep);
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        currentStep = "THEN";
        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Contains(logs, l => l.IsActionStart);
    }

    [Fact]
    public async Task BUT_steps_after_GIVEN_do_not_trigger_IsActionStart()
    {
        var currentStep = "GIVEN";
        var options = OptionsWithStepFetcher(() => currentStep);
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        currentStep = "BUT";
        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.DoesNotContain(logs, l => l.IsActionStart);
    }

    [Fact]
    public async Task Null_step_type_does_not_trigger_IsActionStart()
    {
        string? currentStep = "GIVEN";
        var options = OptionsWithStepFetcher(() => currentStep);
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        currentStep = null;
        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.DoesNotContain(logs, l => l.IsActionStart);
    }

    [Fact]
    public async Task Multiple_GIVEN_requests_before_WHEN_produces_single_marker()
    {
        var currentStep = "GIVEN";
        var options = OptionsWithStepFetcher(() => currentStep);
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);
        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);
        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        currentStep = "WHEN";
        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Single(logs, l => l.IsActionStart);
    }

    [Fact]
    public async Task Full_GIVEN_AND_WHEN_THEN_flow_injects_marker_before_WHEN()
    {
        var currentStep = "GIVEN";
        var options = OptionsWithStepFetcher(() => currentStep);
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        currentStep = "AND";
        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        currentStep = "WHEN";
        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        currentStep = "THEN";
        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        // 4 requests + 4 responses + 1 action start marker = 9
        Assert.Equal(9, logs.Length);
        Assert.Single(logs, l => l.IsActionStart);
        var markerIndex = Array.FindIndex(logs, l => l.IsActionStart);
        // GIVEN request(0) + response(1) + AND request(2) + response(3) + marker(4) + WHEN request(5) ...
        Assert.Equal(4, markerIndex);
    }

    [Fact]
    public async Task IsActionStart_marker_has_correct_test_id()
    {
        var currentStep = "GIVEN";
        var options = OptionsWithStepFetcher(() => currentStep);
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        currentStep = "WHEN";
        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var marker = GetLogsFromThisTest().Single(l => l.IsActionStart);
        Assert.Equal(_testId, marker.TestId);
    }

    [Fact]
    public async Task No_implicit_detection_on_server_side_when_http_context_has_test_headers()
    {
        var stepFetcherCalled = false;
        var options = new TestTrackingMessageHandlerOptions
        {
            CallerName = "Caller",
            FixedNameForReceivingService = "Svc",
            CurrentTestInfoFetcher = () => ("Test", _testId),
            CurrentStepTypeFetcher = () =>
            {
                stepFetcherCalled = true;
                return "WHEN";
            },
        };
        var accessor = CreateHttpContextAccessor(
            (TestTrackingHttpHeaders.CurrentTestNameHeader, "Server Test"),
            (TestTrackingHttpHeaders.CurrentTestIdHeader, _testId));
        using var invoker = CreateInvoker(options, accessor);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        Assert.False(stepFetcherCalled, "CurrentStepTypeFetcher should not be called on the server side");
        Assert.DoesNotContain(GetLogsFromThisTest(), l => l.IsActionStart);
    }

    [Fact]
    public async Task Step_fetcher_that_throws_does_not_crash_when_server_side_headers_present()
    {
        var options = new TestTrackingMessageHandlerOptions
        {
            CallerName = "Caller",
            FixedNameForReceivingService = "Svc",
            CurrentTestInfoFetcher = () => ("Test", _testId),
            CurrentStepTypeFetcher = () => throw new InvalidOperationException("Not in scenario context"),
        };
        var accessor = CreateHttpContextAccessor(
            (TestTrackingHttpHeaders.CurrentTestNameHeader, "Server Test"),
            (TestTrackingHttpHeaders.CurrentTestIdHeader, _testId));
        using var invoker = CreateInvoker(options, accessor);

        // Should NOT throw — the fetcher is never called on the server side
        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length); // request + response, no crash
    }

    // ─── DiagramFocus propagation ───────────────────────────────

    [Fact]
    public async Task Request_FocusFields_populated_when_DiagramFocus_Request_is_set()
    {
        using var invoker = CreateInvoker(DefaultOptions());
        DiagramFocus.Request<SamplePayload>(x => x.Name, x => x.Age);

        await invoker.SendAsync(MakePostRequest("""{"name":"Alice","age":30}"""), CancellationToken.None);

        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.NotNull(requestLog.FocusFields);
        Assert.Contains("Name", requestLog.FocusFields);
        Assert.Contains("Age", requestLog.FocusFields);
    }

    [Fact]
    public async Task Response_FocusFields_populated_when_DiagramFocus_Response_is_set()
    {
        _innerHandler.ResponseToReturn = new(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":"123","status":"ok"}""")
        };
        using var invoker = CreateInvoker(DefaultOptions());
        DiagramFocus.Response<SampleResult>(x => x.Status);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var responseLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.NotNull(responseLog.FocusFields);
        Assert.Single(responseLog.FocusFields);
        Assert.Equal("Status", responseLog.FocusFields[0]);
    }

    [Fact]
    public async Task FocusFields_is_null_when_no_focus_set()
    {
        using var invoker = CreateInvoker(DefaultOptions());

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.All(logs, l => Assert.Null(l.FocusFields));
    }

    [Fact]
    public async Task Focus_is_consumed_after_one_HTTP_call()
    {
        using var invoker = CreateInvoker(DefaultOptions());
        DiagramFocus.Request<SamplePayload>(x => x.Name);
        DiagramFocus.Response<SampleResult>(x => x.Status);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);
        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var secondRequestLog = GetLogsFromThisTest()
            .Where(l => l.Type == RequestResponseType.Request).Skip(1).First();
        var secondResponseLog = GetLogsFromThisTest()
            .Where(l => l.Type == RequestResponseType.Response).Skip(1).First();
        Assert.Null(secondRequestLog.FocusFields);
        Assert.Null(secondResponseLog.FocusFields);
    }

    [Fact]
    public async Task Focus_not_consumed_by_downstream_handler_when_HttpContext_is_set()
    {
        DiagramFocus.Request<SamplePayload>(x => x.Name);
        DiagramFocus.Response<SampleResult>(x => x.Status);

        var accessor = CreateHttpContextAccessor(
            (TestTrackingHttpHeaders.CurrentTestNameHeader, "Server Test"),
            (TestTrackingHttpHeaders.CurrentTestIdHeader, _testId));
        using var invoker = CreateInvoker(DefaultOptions(), accessor);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        // When HttpContext is set, the handler is on the server side — focus should NOT be consumed
        var logs = GetLogsFromThisTest();
        Assert.All(logs, l => Assert.Null(l.FocusFields));

        // And focus should still be pending (not consumed)
        Assert.NotNull(DiagramFocus.ConsumePendingRequestFocus());
        Assert.NotNull(DiagramFocus.ConsumePendingResponseFocus());
    }

    [Fact]
    public async Task Both_request_and_response_focus_set_independently()
    {
        using var invoker = CreateInvoker(DefaultOptions());
        DiagramFocus.Request<SamplePayload>(x => x.Name);
        DiagramFocus.Response<SampleResult>(x => x.Id, x => x.Status);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        var responseLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);

        Assert.NotNull(requestLog.FocusFields);
        Assert.Single(requestLog.FocusFields);
        Assert.Equal("Name", requestLog.FocusFields[0]);

        Assert.NotNull(responseLog.FocusFields);
        Assert.Equal(2, responseLog.FocusFields.Length);
        Assert.Contains("Id", responseLog.FocusFields);
        Assert.Contains("Status", responseLog.FocusFields);
    }

    // ─── Focus helpers ──────────────────────────────────────────

    // ReSharper disable once ClassNeverInstantiated.Local
    private record SamplePayload(string Name, int Age);
    // ReSharper disable once ClassNeverInstantiated.Local
    private record SampleResult(string Id, string Status);

    private static HttpRequestMessage MakePostRequest(string body)
    {
        return new HttpRequestMessage(HttpMethod.Post, "http://target-service:5000/api/items")
        {
            Content = new StringContent(body)
        };
    }

    // ─── traceparent propagation ────────────────────────────────

    [Fact]
    public async Task Injects_traceparent_header_when_no_activity_current()
    {
        using var invoker = CreateInvoker(DefaultOptions());
        Activity.Current = null; // ensure no ambient activity

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        Assert.True(_innerHandler.CapturedRequest!.Headers.Contains("traceparent"));
        var traceparent = _innerHandler.CapturedRequest.Headers.GetValues("traceparent").Single();
        Assert.Matches(@"^00-[0-9a-f]{32}-[0-9a-f]{16}-0[01]$", traceparent);
    }

    [Fact]
    public async Task Traceparent_uses_existing_activity_current_when_present()
    {
        using var invoker = CreateInvoker(DefaultOptions());
        using var existingActivity = new Activity("ExistingTest");
        existingActivity.SetIdFormat(ActivityIdFormat.W3C);
        existingActivity.Start();

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        // When an ambient Activity exists, TTD should NOT inject traceparent —
        // the framework's own DiagnosticsHandler handles trace propagation.
        Assert.False(_innerHandler.CapturedRequest!.Headers.Contains("traceparent"));

        existingActivity.Stop();
    }

    [Fact]
    public async Task Does_not_inject_traceparent_when_already_present()
    {
        using var invoker = CreateInvoker(DefaultOptions());
        var request = MakeGetRequest();
        request.Headers.TryAddWithoutValidation("traceparent", "00-aaaabbbbccccddddaaaabbbbccccdddd-1122334455667788-01");

        await invoker.SendAsync(request, CancellationToken.None);

        var traceparent = _innerHandler.CapturedRequest!.Headers.GetValues("traceparent").Single();
        Assert.Equal("00-aaaabbbbccccddddaaaabbbbccccdddd-1122334455667788-01", traceparent);
    }

    [Fact]
    public async Task ActivityTraceId_is_recorded_in_request_log()
    {
        using var invoker = CreateInvoker(DefaultOptions());
        Activity.Current = null;

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        var requestLog = logs.First(l => l.Type == RequestResponseType.Request);
        Assert.NotNull(requestLog.ActivityTraceId);
        Assert.Matches(@"^[0-9a-f]{32}$", requestLog.ActivityTraceId);
    }

    [Fact]
    public async Task Traceparent_trace_id_matches_logged_activity_trace_id()
    {
        using var invoker = CreateInvoker(DefaultOptions());
        Activity.Current = null;

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var traceparent = _innerHandler.CapturedRequest!.Headers.GetValues("traceparent").Single();
        var traceparentTraceId = traceparent.Split('-')[1];

        var logs = GetLogsFromThisTest();
        var requestLog = logs.First(l => l.Type == RequestResponseType.Request);
        Assert.Equal(traceparentTraceId, requestLog.ActivityTraceId);
    }

    [Fact]
    public async Task Does_not_modify_activity_current_when_no_ambient_activity()
    {
        using var invoker = CreateInvoker(DefaultOptions());
        Activity.Current = null;

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        // SendAsync must not leave a new Activity on Activity.Current — doing so
        // interferes with Application Insights' DependencyTelemetry correlation.
        Assert.Null(Activity.Current);
    }

    [Fact]
    public async Task Does_not_modify_activity_current_when_ambient_activity_exists()
    {
        using var invoker = CreateInvoker(DefaultOptions());
        using var existingActivity = new Activity("ExistingTest");
        existingActivity.SetIdFormat(ActivityIdFormat.W3C);
        existingActivity.Start();

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        // SendAsync must preserve the ambient Activity.
        Assert.Same(existingActivity, Activity.Current);

        existingActivity.Stop();
    }

    [Fact]
    public async Task ActivitySpanId_is_recorded_in_request_log()
    {
        using var invoker = CreateInvoker(DefaultOptions());
        Activity.Current = null;

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        var requestLog = logs.First(l => l.Type == RequestResponseType.Request);
        Assert.NotNull(requestLog.ActivitySpanId);
        Assert.Matches(@"^[0-9a-f]{16}$", requestLog.ActivitySpanId);
    }

    // ─── Fetcher exception safety ───────────────────────────────

    [Fact]
    public async Task Forwards_request_without_tracking_when_fetcher_throws()
    {
        var options = new TestTrackingMessageHandlerOptions
        {
            FixedNameForReceivingService = "Svc",
            CallerName = "Caller",
            CurrentTestInfoFetcher = () => throw new InvalidOperationException("No test context"),
        };
        using var invoker = CreateInvoker(options);

        var response = await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(GetLogsFromThisTest());
    }

    [Fact]
    public async Task Response_body_is_readable_when_fetcher_throws()
    {
        _innerHandler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("expected-body")
        };
        var options = new TestTrackingMessageHandlerOptions
        {
            FixedNameForReceivingService = "Svc",
            CallerName = "Caller",
            CurrentTestInfoFetcher = () => throw new InvalidOperationException("No test context"),
        };
        using var invoker = CreateInvoker(options);

        var response = await invoker.SendAsync(MakeGetRequest(), TestContext.Current.CancellationToken);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("expected-body", body);
    }

    // ─── InnerHandler not pre-set (Issue #62) ───────────────────

    [Fact]
    public void Constructor_does_not_pre_set_InnerHandler()
    {
        var handler = new TestTrackingMessageHandler(DefaultOptions());

        // InnerHandler should be null after construction, allowing HttpClientFactory
        // or IHttpMessageHandlerBuilderFilter to set it before first use.
        Assert.Null(handler.InnerHandler);
    }

    [Fact]
    public async Task InnerHandler_is_lazily_initialized_on_first_SendAsync_when_not_set()
    {
        var handler = new TestTrackingMessageHandler(DefaultOptions());
        using var invoker = new HttpMessageInvoker(handler);

        // SendAsync should lazily set InnerHandler to HttpClientHandler.
        // We can't easily test the network call, but we can verify it doesn't throw
        // NullReferenceException — the lazy init should create an HttpClientHandler.
        // Instead, verify InnerHandler is set by checking after construction + set.
        Assert.Null(handler.InnerHandler);

        // Set InnerHandler manually (simulating HttpClientFactory pipeline wiring)
        handler.InnerHandler = _innerHandler;
        Assert.Same(_innerHandler, handler.InnerHandler);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        // The lazy init should NOT have overwritten our explicitly-set InnerHandler
        Assert.Same(_innerHandler, handler.InnerHandler);
    }

    [Fact]
    public async Task HttpClientFactory_pipeline_can_set_InnerHandler_after_construction()
    {
        // Simulate what HttpClientFactory.CreateHandlerPipeline() does:
        // it expects InnerHandler to be null, then sets it.
        var handler = new TestTrackingMessageHandler(DefaultOptions());
        Assert.Null(handler.InnerHandler);

        // HttpClientFactory sets InnerHandler to the primary handler
        handler.InnerHandler = _innerHandler;
        using var invoker = new HttpMessageInvoker(handler);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        // Verify our StubInnerHandler received the request (not a default HttpClientHandler)
        Assert.NotNull(_innerHandler.CapturedRequest);
        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
    }

    // ─── TestIdentityScope fallback (Issue #63) ─────────────────

    [Fact]
    public async Task Falls_back_to_TestIdentityScope_when_no_fetcher_and_no_http_context()
    {
        var options = new TestTrackingMessageHandlerOptions
        {
            FixedNameForReceivingService = "Svc",
            CallerName = "Caller",
            CurrentTestInfoFetcher = null,
        };
        using var invoker = CreateInvoker(options);

        using (TestIdentityScope.Begin("Scope Test", _testId))
        {
            await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);
        }

        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("Scope Test", requestLog.TestName);
        Assert.Equal(_testId, requestLog.TestId);
    }

    [Fact]
    public async Task Falls_back_to_TestIdentityScope_when_fetcher_throws()
    {
        var options = new TestTrackingMessageHandlerOptions
        {
            FixedNameForReceivingService = "Svc",
            CallerName = "Caller",
            CurrentTestInfoFetcher = () => throw new InvalidOperationException("No context"),
        };
        using var invoker = CreateInvoker(options);

        using (TestIdentityScope.Begin("Fallback Test", _testId))
        {
            await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);
        }

        var requestLog = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("Fallback Test", requestLog.TestName);
        Assert.Equal(_testId, requestLog.TestId);
    }

    [Fact]
    public async Task Skips_tracking_when_no_fetcher_no_http_context_and_no_scope()
    {
        TestIdentityScope.Reset();

        var options = new TestTrackingMessageHandlerOptions
        {
            FixedNameForReceivingService = "Svc",
            CallerName = "Caller",
            CurrentTestInfoFetcher = null,
        };
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        // Request should still be forwarded to InnerHandler
        Assert.NotNull(_innerHandler.CapturedRequest);
        // But no tracking logs should be generated
        var logs = GetLogsFromThisTest();
        Assert.Empty(logs);
    }

    // ─── Synchronous Send path (issue #69 deadlock fix) ───────

    [Fact]
    public void Sync_Send_completes_without_deadlock()
    {
        // The synchronous Send() override must not call .GetAwaiter().GetResult()
        // on SendAsync(), which causes deadlocks when infrastructure HTTP clients
        // (e.g. TestContainers, Docker) use the sync path.
        var options = DefaultOptions();
        var handler = new TestTrackingMessageHandler(options)
        {
            InnerHandler = _innerHandler
        };
        using var invoker = new HttpMessageInvoker(handler);
        var request = MakeGetRequest();

        // Must complete without hanging. If the old .GetAwaiter().GetResult()
        // pattern is still in place, this could deadlock in certain contexts.
        var response = invoker.Send(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void Sync_Send_does_not_produce_tracking_logs()
    {
        // Synchronous calls are infrastructure (Docker, TestContainers, config fetches).
        // They should forward without tracking to avoid deadlocks.
        var options = DefaultOptions();
        var handler = new TestTrackingMessageHandler(options)
        {
            InnerHandler = _innerHandler
        };
        using var invoker = new HttpMessageInvoker(handler);
        var request = MakeGetRequest();

        invoker.Send(request, CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Empty(logs);
    }

    [Fact]
    public async Task Fetcher_is_used_when_only_test_name_header_present_without_id_header()
    {
        // When the incoming HttpContext has the name header but NOT the ID header,
        // the handler must NOT build a lambda that only references the name header —
        // instead it should fall through to _currentTestInfoFetcher so tracking
        // still works (rather than throwing and skipping all tracking).
        TestIdentityScope.Reset();

        var accessor = CreateHttpContextAccessor(
            (TestTrackingHttpHeaders.CurrentTestNameHeader, "Context Name"));

        var options = new TestTrackingMessageHandlerOptions
        {
            FixedNameForReceivingService = "Svc",
            CallerName = "Caller",
            CurrentTestInfoFetcher = () => ("Fetcher Test", _testId),
        };
        using var invoker = CreateInvoker(options, accessor);

        await invoker.SendAsync(MakeGetRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal("Fetcher Test", logs[0].TestName);
        Assert.Equal(_testId, logs[0].TestId);
    }
}
