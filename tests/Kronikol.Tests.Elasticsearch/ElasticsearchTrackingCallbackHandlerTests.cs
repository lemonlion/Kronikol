using Kronikol.Extensions.Elasticsearch;
using Kronikol.Tracking;

namespace Kronikol.Tests.Elasticsearch;

public class ElasticsearchTrackingCallbackHandlerTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private RequestResponseLog[] GetLogsFromThisTest()
    {
        return RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == _testId)
            .ToArray();
    }

    private ElasticsearchTrackingOptions MakeOptions(
        ElasticsearchTrackingVerbosity verbosity = ElasticsearchTrackingVerbosity.Detailed,
        string serviceName = "Elasticsearch",
        string callerName = "TestCaller") => new()
    {
        ServiceName = serviceName,
        CallerName = callerName,
        Verbosity = verbosity,
        CurrentTestInfoFetcher = () => ("My ES Test", _testId),
    };

    private static void CallHandler(
        ElasticsearchTrackingCallbackHandler handler,
        string method = "POST",
        string path = "/orders/_search",
        int statusCode = 200,
        byte[]? requestBody = null,
        byte[]? responseBody = null)
    {
        handler.LogOperation(
            method,
            new Uri($"http://localhost:9200{path}"),
            statusCode,
            requestBody,
            responseBody);
    }

    // ─── Logging ────────────────────────────────────────────────

    [Fact]
    public void HandleApiCallDetails_logs_request_and_response()
    {
        var handler = new ElasticsearchTrackingCallbackHandler(MakeOptions());

        CallHandler(handler);

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal(RequestResponseType.Request, logs[0].Type);
        Assert.Equal(RequestResponseType.Response, logs[1].Type);
    }

    [Fact]
    public void LogOperation_skips_when_no_test_info()
    {
        var options = MakeOptions();
        options.CurrentTestInfoFetcher = null;
        var handler = new ElasticsearchTrackingCallbackHandler(options);

        CallHandler(handler);

        Assert.Empty(GetLogsFromThisTest());
    }

    [Fact]
    public void LogOperation_skips_excluded_operations()
    {
        var handler = new ElasticsearchTrackingCallbackHandler(MakeOptions());

        CallHandler(handler, method: "GET", path: "/_cluster/health");

        Assert.Empty(GetLogsFromThisTest());
    }

    [Fact]
    public void LogOperation_uses_correct_service_names()
    {
        var handler = new ElasticsearchTrackingCallbackHandler(
            MakeOptions(callerName: "MyApi", serviceName: "SearchCluster"));

        CallHandler(handler);

        var log = GetLogsFromThisTest().First();
        Assert.Equal("SearchCluster", log.ServiceName);
        Assert.Equal("MyApi", log.CallerName);
    }

    [Fact]
    public void LogOperation_uses_detailed_label()
    {
        var handler = new ElasticsearchTrackingCallbackHandler(MakeOptions());

        CallHandler(handler, method: "POST", path: "/orders/_search");

        var log = GetLogsFromThisTest().First();
        Assert.Equal("Search → orders", log.Method.Value?.ToString());
    }

    [Fact]
    public void LogOperation_includes_request_body_at_detailed()
    {
        var handler = new ElasticsearchTrackingCallbackHandler(MakeOptions(ElasticsearchTrackingVerbosity.Detailed));
        var body = System.Text.Encoding.UTF8.GetBytes("{\"query\":{\"match_all\":{}}}");

        CallHandler(handler, requestBody: body);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains("match_all", log.Content);
    }

    [Fact]
    public void LogOperation_omits_request_body_at_summarised()
    {
        var handler = new ElasticsearchTrackingCallbackHandler(MakeOptions(ElasticsearchTrackingVerbosity.Summarised));
        var body = System.Text.Encoding.UTF8.GetBytes("{\"query\":{\"match_all\":{}}}");

        CallHandler(handler, requestBody: body);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Null(log.Content);
    }

    [Fact]
    public void LogOperation_includes_response_body_only_at_raw()
    {
        var handler = new ElasticsearchTrackingCallbackHandler(MakeOptions(ElasticsearchTrackingVerbosity.Raw));
        var responseBody = System.Text.Encoding.UTF8.GetBytes("{\"hits\":{\"total\":42}}");

        CallHandler(handler, responseBody: responseBody);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Contains("hits", log.Content);
    }

    [Fact]
    public void LogOperation_includes_response_body_at_detailed()
    {
        var handler = new ElasticsearchTrackingCallbackHandler(MakeOptions(ElasticsearchTrackingVerbosity.Detailed));
        var responseBody = System.Text.Encoding.UTF8.GetBytes("{\"hits\":{\"total\":42}}");

        CallHandler(handler, responseBody: responseBody);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Contains("hits", log.Content);
    }

    [Fact]
    public void LogOperation_omits_response_body_at_detailed_when_LogResponseContent_is_false()
    {
        var options = MakeOptions(ElasticsearchTrackingVerbosity.Detailed);
        options.LogResponseContent = false;
        var handler = new ElasticsearchTrackingCallbackHandler(options);
        var responseBody = System.Text.Encoding.UTF8.GetBytes("{\"hits\":{\"total\":42}}");

        CallHandler(handler, responseBody: responseBody);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Null(log.Content);
    }

    [Fact]
    public void LogOperation_omits_the_response_body_at_summarised_when_LogResponseContent_is_true()
    {
        var options = MakeOptions(ElasticsearchTrackingVerbosity.Summarised);
        options.LogResponseContent = true;
        var handler = new ElasticsearchTrackingCallbackHandler(options);
        var responseBody = System.Text.Encoding.UTF8.GetBytes("{\"hits\":{\"total\":42}}");

        CallHandler(handler, responseBody: responseBody);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Null(log.Content);
    }

    // LogResponseContent decides the Detailed note alone: Raw always had the body, and false was to restore that.
    [Fact]
    public void LogOperation_keeps_the_response_body_at_raw_when_LogResponseContent_is_false()
    {
        var options = MakeOptions(ElasticsearchTrackingVerbosity.Raw);
        options.LogResponseContent = false;
        var handler = new ElasticsearchTrackingCallbackHandler(options);
        var responseBody = System.Text.Encoding.UTF8.GetBytes("{\"hits\":{\"total\":42}}");

        CallHandler(handler, responseBody: responseBody);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Contains("hits", log.Content);
    }

    [Fact]
    public void A_raw_phase_keeps_the_response_body_and_a_detailed_one_leaves_it_out_when_LogResponseContent_is_false()
    {
        var options = MakeOptions(ElasticsearchTrackingVerbosity.Detailed);
        options.ActionVerbosity = ElasticsearchTrackingVerbosity.Raw;
        options.LogResponseContent = false;
        var handler = new ElasticsearchTrackingCallbackHandler(options);
        var responseBody = System.Text.Encoding.UTF8.GetBytes("{\"hits\":{\"total\":42}}");

        CallHandler(handler, responseBody: responseBody);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Null(log.SetupVariant!.Content);
        Assert.Contains("hits", log.ActionVariant!.Content);
    }

    // ─── ITrackingComponent ─────────────────────────────────────

    [Fact]
    public void Implements_ITrackingComponent()
    {
        var handler = new ElasticsearchTrackingCallbackHandler(MakeOptions());
        Assert.IsAssignableFrom<ITrackingComponent>(handler);
    }

    [Fact]
    public void WasInvoked_IsFalse_BeforeAnyCalls()
    {
        var handler = new ElasticsearchTrackingCallbackHandler(MakeOptions());
        Assert.False(handler.WasInvoked);
    }

    [Fact]
    public void WasInvoked_IsTrue_AfterCall()
    {
        var handler = new ElasticsearchTrackingCallbackHandler(MakeOptions());
        CallHandler(handler);
        Assert.True(handler.WasInvoked);
    }

    [Fact]
    public void InvocationCount_StartsAtZero()
    {
        var handler = new ElasticsearchTrackingCallbackHandler(MakeOptions());
        Assert.Equal(0, handler.InvocationCount);
    }

    [Fact]
    public void InvocationCount_IncrementsEvenWhenNoTestInfo()
    {
        var options = MakeOptions();
        options.CurrentTestInfoFetcher = null;
        var handler = new ElasticsearchTrackingCallbackHandler(options);
        CallHandler(handler);
        Assert.Equal(1, handler.InvocationCount);
    }

    [Fact]
    public void ComponentName_MatchesServiceName()
    {
        var handler = new ElasticsearchTrackingCallbackHandler(MakeOptions(serviceName: "SearchCluster"));
        Assert.Equal("ElasticsearchTrackingCallbackHandler (SearchCluster)", handler.ComponentName);
    }

    [Fact]
    public void Constructor_AutoRegistersWithTrackingComponentRegistry()
    {
        TrackingComponentRegistry.Clear();
        var handler = new ElasticsearchTrackingCallbackHandler(MakeOptions());

        var components = TrackingComponentRegistry.GetRegisteredComponents();
        Assert.Contains(components, c => ReferenceEquals(c, handler));
    }
}
