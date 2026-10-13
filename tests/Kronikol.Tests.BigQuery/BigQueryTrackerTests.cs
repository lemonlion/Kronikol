using Kronikol.Extensions.BigQuery;
using Kronikol.Tests.Tracking;
using Kronikol.Tracking;

namespace Kronikol.Tests.BigQuery;

public class BigQueryTrackerTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private RequestResponseLog[] GetLogsFromThisTest()
    {
        return RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == _testId)
            .ToArray();
    }

    private BigQueryTrackingMessageHandlerOptions MakeOptions(
        BigQueryTrackingVerbosity verbosity = BigQueryTrackingVerbosity.Detailed,
        string serviceName = "BigQuery",
        string callerName = "TestCaller") => new()
    {
        ServiceName = serviceName,
        CallerName = callerName,
        Verbosity = verbosity,
        CurrentTestInfoFetcher = () => ("My BigQuery Test", _testId),
    };

    // ─── HttpContextAccessor ────────────────────────────────

    [Fact]
    public void Tracker_reads_HttpContextAccessor_from_options_when_not_passed_directly()
    {
        var headerTestId = Guid.NewGuid().ToString();
        var options = MakeOptions();
        options.HttpContextAccessor = RequestHeaderAccessor.For("Header Test", headerTestId);
        var tracker = new BigQueryTracker(options);

        tracker.LogRequest(new BigQueryOperationInfo(BigQueryOperation.Query, "query", null, "my-project", null), "SELECT 1");

        // The request's headers outrank the fetcher, which answers this class's own test.
        var log = Assert.Single(RequestResponseLogger.RequestAndResponseLogs, l => l.TestId == headerTestId);
        Assert.Equal(AttributionSource.RequestHeader, log.AttributionSource);
        Assert.Empty(GetLogsFromThisTest());
        Assert.True(tracker.HasHttpContextAccessor);
    }

    [Fact]
    public void Tracker_explicit_accessor_takes_precedence_over_options()
    {
        var explicitTestId = Guid.NewGuid().ToString();
        var optionsTestId = Guid.NewGuid().ToString();
        var options = MakeOptions();
        options.HttpContextAccessor = RequestHeaderAccessor.For("Options Test", optionsTestId);
        var tracker = new BigQueryTracker(options, RequestHeaderAccessor.For("Explicit Test", explicitTestId));

        tracker.LogRequest(new BigQueryOperationInfo(BigQueryOperation.Query, "query", null, "my-project", null), "SELECT 1");

        Assert.Contains(RequestResponseLogger.RequestAndResponseLogs, l => l.TestId == explicitTestId);
        Assert.DoesNotContain(RequestResponseLogger.RequestAndResponseLogs, l => l.TestId == optionsTestId);
    }

    [Fact]
    public void Tracker_has_no_accessor_when_neither_options_nor_parameter()
    {
        Assert.False(new BigQueryTracker(MakeOptions()).HasHttpContextAccessor);
    }

    // ─── LogRequest ─────────────────────────────────────────

    [Fact]
    public void LogRequest_Logs_request_entry()
    {
        var tracker = new BigQueryTracker(MakeOptions());
        var op = new BigQueryOperationInfo(BigQueryOperation.Query, "query", null, "my-project", null);

        tracker.LogRequest(op, "SELECT * FROM dataset.table");

        var logs = GetLogsFromThisTest();
        Assert.Single(logs);
        Assert.Equal(RequestResponseType.Request, logs[0].Type);
    }

    [Fact]
    public void LogRequest_Returns_ids_for_pairing()
    {
        var tracker = new BigQueryTracker(MakeOptions());
        var op = new BigQueryOperationInfo(BigQueryOperation.Query, "query", null, "my-project", null);

        var (reqId, traceId) = tracker.LogRequest(op, "SELECT 1");

        Assert.NotEqual(Guid.Empty, reqId);
        Assert.NotEqual(Guid.Empty, traceId);
    }

    [Fact]
    public void LogRequest_NoTestInfo_NoLog()
    {
        var options = MakeOptions();
        options.CurrentTestInfoFetcher = null;
        var tracker = new BigQueryTracker(options);
        var op = new BigQueryOperationInfo(BigQueryOperation.Query, "query", null, "p", null);

        var (reqId, traceId) = tracker.LogRequest(op, "SELECT 1");

        Assert.Empty(GetLogsFromThisTest());
        Assert.Equal(Guid.Empty, reqId);
    }

    [Fact]
    public void LogRequest_Uses_event_metatype()
    {
        var tracker = new BigQueryTracker(MakeOptions());
        var op = new BigQueryOperationInfo(BigQueryOperation.Query, "query", null, "p", null);

        tracker.LogRequest(op, "SELECT 1");

        var log = GetLogsFromThisTest().First();
        Assert.Equal(RequestResponseMetaType.Event, log.MetaType);
    }

    [Fact]
    public void LogRequest_Uses_BigQuery_dependency_category()
    {
        var tracker = new BigQueryTracker(MakeOptions());
        var op = new BigQueryOperationInfo(BigQueryOperation.Query, "query", null, "p", null);

        tracker.LogRequest(op, "SELECT 1");

        var log = GetLogsFromThisTest().First();
        Assert.Equal("BigQuery", log.DependencyCategory);
    }

    // ─── LogResponse ────────────────────────────────────────

    [Fact]
    public void LogResponse_Logs_response_entry()
    {
        var tracker = new BigQueryTracker(MakeOptions());
        var op = new BigQueryOperationInfo(BigQueryOperation.Query, "query", null, "p", null);
        var (reqId, traceId) = tracker.LogRequest(op, "SELECT 1");

        tracker.LogResponse(op, reqId, traceId, "100 rows");

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal(RequestResponseType.Response, logs[1].Type);
    }

    [Fact]
    public void LogResponse_MatchesTraceId()
    {
        var tracker = new BigQueryTracker(MakeOptions());
        var op = new BigQueryOperationInfo(BigQueryOperation.Query, "query", null, "p", null);
        var (reqId, traceId) = tracker.LogRequest(op, "SELECT 1");

        tracker.LogResponse(op, reqId, traceId, null);

        var logs = GetLogsFromThisTest();
        Assert.Equal(logs[0].TraceId, logs[1].TraceId);
    }

    // ─── Verbosity ──────────────────────────────────────────

    [Fact]
    public void LogRequest_Detailed_IncludesContent()
    {
        var tracker = new BigQueryTracker(MakeOptions(BigQueryTrackingVerbosity.Detailed));
        var op = new BigQueryOperationInfo(BigQueryOperation.Query, "query", null, "p", null);

        tracker.LogRequest(op, "SELECT * FROM dataset.table");

        var log = GetLogsFromThisTest().First();
        Assert.Equal("SELECT * FROM dataset.table", log.Content);
    }

    [Fact]
    public void LogRequest_Summarised_OmitsContent()
    {
        var tracker = new BigQueryTracker(MakeOptions(BigQueryTrackingVerbosity.Summarised));
        var op = new BigQueryOperationInfo(BigQueryOperation.Query, "query", null, "p", null);

        tracker.LogRequest(op, "SELECT * FROM dataset.table");

        var log = GetLogsFromThisTest().First();
        Assert.Null(log.Content);
    }

    // ─── URI ────────────────────────────────────────────────

    [Fact]
    public void LogRequest_URI_uses_bigquery_scheme()
    {
        var tracker = new BigQueryTracker(MakeOptions());
        var op = new BigQueryOperationInfo(BigQueryOperation.Query, "query", null, "p", null);

        tracker.LogRequest(op, "SELECT 1");

        var log = GetLogsFromThisTest().First();
        Assert.StartsWith("bigquery://", log.Uri.ToString());
    }

    [Fact]
    public void LogRequest_URI_Raw_includes_project()
    {
        var tracker = new BigQueryTracker(MakeOptions(BigQueryTrackingVerbosity.Raw));
        var op = new BigQueryOperationInfo(BigQueryOperation.Read, "table", "my-table", "my-project", "my-dataset");

        tracker.LogRequest(op, null);

        var log = GetLogsFromThisTest().First();
        Assert.Contains("my-project", log.Uri.ToString());
    }

    // ─── Service/caller names ──────────────────────────────

    [Fact]
    public void LogRequest_Uses_configured_service_and_caller()
    {
        var tracker = new BigQueryTracker(MakeOptions(serviceName: "MyBQ", callerName: "MyApi"));
        var op = new BigQueryOperationInfo(BigQueryOperation.Query, "query", null, "p", null);

        tracker.LogRequest(op, "SELECT 1");

        var log = GetLogsFromThisTest().First();
        Assert.Equal("MyBQ", log.ServiceName);
        Assert.Equal("MyApi", log.CallerName);
    }

    // ─── ITrackingComponent ────────────────────────────────

    [Fact]
    public void Implements_ITrackingComponent()
    {
        var tracker = new BigQueryTracker(MakeOptions());
        Assert.IsAssignableFrom<ITrackingComponent>(tracker);
    }

    [Fact]
    public void WasInvoked_IsFalse_BeforeAnyCalls()
    {
        var tracker = new BigQueryTracker(MakeOptions());
        Assert.False(tracker.WasInvoked);
    }

    [Fact]
    public void WasInvoked_IsTrue_AfterLogRequest()
    {
        var tracker = new BigQueryTracker(MakeOptions());
        var op = new BigQueryOperationInfo(BigQueryOperation.Query, "query", null, "p", null);

        tracker.LogRequest(op, "SELECT 1");

        Assert.True(tracker.WasInvoked);
    }

    [Fact]
    public void InvocationCount_Increments()
    {
        var tracker = new BigQueryTracker(MakeOptions());
        var op = new BigQueryOperationInfo(BigQueryOperation.Query, "query", null, "p", null);

        tracker.LogRequest(op, "q1");
        tracker.LogRequest(op, "q2");

        Assert.Equal(2, tracker.InvocationCount);
    }

    [Fact]
    public void ComponentName_IncludesServiceName()
    {
        var tracker = new BigQueryTracker(MakeOptions(serviceName: "MyBQ"));
        Assert.Contains("MyBQ", tracker.ComponentName);
    }
}
