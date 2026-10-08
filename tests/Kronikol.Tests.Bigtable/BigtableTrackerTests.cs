using Kronikol.Extensions.Bigtable;
using Kronikol.Tests.Tracking;
using Kronikol.Tracking;

namespace Kronikol.Tests.Bigtable;

public class BigtableTrackerTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private RequestResponseLog[] GetLogsFromThisTest()
    {
        return RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == _testId)
            .ToArray();
    }

    private BigtableTrackingOptions MakeOptions(
        BigtableTrackingVerbosity verbosity = BigtableTrackingVerbosity.Detailed,
        string serviceName = "Bigtable",
        string callerName = "TestCaller") => new()
    {
        ServiceName = serviceName,
        CallerName = callerName,
        Verbosity = verbosity,
        CurrentTestInfoFetcher = () => ("My Bigtable Test", _testId),
    };

    // ─── LogRequest ─────────────────────────────────────────

    [Fact]
    public void LogRequest_Logs_request_entry()
    {
        var tracker = new BigtableTracker(MakeOptions());
        var op = new BigtableOperationInfo(BigtableOperation.ReadRows, "projects/p/instances/i/tables/my-table");

        tracker.LogRequest(op, "filter expression");

        var logs = GetLogsFromThisTest();
        Assert.Single(logs);
        Assert.Equal(RequestResponseType.Request, logs[0].Type);
    }

    [Fact]
    public void LogRequest_Returns_ids_for_pairing()
    {
        var tracker = new BigtableTracker(MakeOptions());
        var op = new BigtableOperationInfo(BigtableOperation.ReadRows, "projects/p/instances/i/tables/my-table");

        var (reqId, traceId) = tracker.LogRequest(op, null);

        Assert.NotEqual(Guid.Empty, reqId);
        Assert.NotEqual(Guid.Empty, traceId);
    }

    [Fact]
    public void LogRequest_NoTestInfo_NoLog()
    {
        var options = MakeOptions();
        options.CurrentTestInfoFetcher = null;
        var tracker = new BigtableTracker(options);
        var op = new BigtableOperationInfo(BigtableOperation.ReadRows, "projects/p/instances/i/tables/t");

        var (reqId, traceId) = tracker.LogRequest(op, null);

        Assert.Empty(GetLogsFromThisTest());
        Assert.Equal(Guid.Empty, reqId);
    }

    [Fact]
    public void LogRequest_Uses_event_metatype()
    {
        var tracker = new BigtableTracker(MakeOptions());
        var op = new BigtableOperationInfo(BigtableOperation.MutateRow, "projects/p/instances/i/tables/t", "row1");

        tracker.LogRequest(op, null);

        var log = GetLogsFromThisTest().First();
        Assert.Equal(RequestResponseMetaType.Event, log.MetaType);
    }

    [Fact]
    public void LogRequest_Uses_Database_dependency_category()
    {
        var tracker = new BigtableTracker(MakeOptions());
        var op = new BigtableOperationInfo(BigtableOperation.MutateRow, "projects/p/instances/i/tables/t", "row1");

        tracker.LogRequest(op, null);

        var log = GetLogsFromThisTest().First();
        Assert.Equal("Bigtable", log.DependencyCategory);
    }

    // ─── LogResponse ────────────────────────────────────────

    [Fact]
    public void LogResponse_Logs_response_entry()
    {
        var tracker = new BigtableTracker(MakeOptions());
        var op = new BigtableOperationInfo(BigtableOperation.ReadRows, "projects/p/instances/i/tables/my-table");
        var (reqId, traceId) = tracker.LogRequest(op, null);

        tracker.LogResponse(op, reqId, traceId, "5 rows");

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal(RequestResponseType.Response, logs[1].Type);
    }

    [Fact]
    public void LogResponse_MatchesTraceId()
    {
        var tracker = new BigtableTracker(MakeOptions());
        var op = new BigtableOperationInfo(BigtableOperation.MutateRow, "projects/p/instances/i/tables/t", "row1");
        var (reqId, traceId) = tracker.LogRequest(op, null);

        tracker.LogResponse(op, reqId, traceId, null);

        var logs = GetLogsFromThisTest();
        Assert.Equal(logs[0].TraceId, logs[1].TraceId);
    }

    // ─── Verbosity ──────────────────────────────────────────

    [Fact]
    public void LogRequest_Detailed_IncludesContent()
    {
        var tracker = new BigtableTracker(MakeOptions(BigtableTrackingVerbosity.Detailed));
        var op = new BigtableOperationInfo(BigtableOperation.MutateRow, "projects/p/instances/i/tables/t", "row1");

        tracker.LogRequest(op, "mutation data");

        var log = GetLogsFromThisTest().First();
        Assert.Equal("mutation data", log.Content);
    }

    [Fact]
    public void LogRequest_Summarised_OmitsContent()
    {
        var tracker = new BigtableTracker(MakeOptions(BigtableTrackingVerbosity.Summarised));
        var op = new BigtableOperationInfo(BigtableOperation.MutateRow, "projects/p/instances/i/tables/t", "row1");

        tracker.LogRequest(op, "mutation data");

        var log = GetLogsFromThisTest().First();
        Assert.Null(log.Content);
    }

    // ─── URI ────────────────────────────────────────────────

    [Fact]
    public void LogRequest_URI_uses_bigtable_scheme()
    {
        var tracker = new BigtableTracker(MakeOptions());
        var op = new BigtableOperationInfo(BigtableOperation.ReadRows, "projects/p/instances/i/tables/my-table");

        tracker.LogRequest(op, null);

        var log = GetLogsFromThisTest().First();
        Assert.StartsWith("bigtable://", log.Uri.ToString());
    }

    [Fact]
    public void LogRequest_URI_Detailed_uses_short_table_name()
    {
        var tracker = new BigtableTracker(MakeOptions(BigtableTrackingVerbosity.Detailed));
        var op = new BigtableOperationInfo(BigtableOperation.ReadRows, "projects/p/instances/i/tables/my-table");

        tracker.LogRequest(op, null);

        var log = GetLogsFromThisTest().First();
        Assert.Contains("my-table", log.Uri.ToString());
    }

    [Fact]
    public void LogRequest_URI_Raw_uses_full_resource_name()
    {
        var tracker = new BigtableTracker(MakeOptions(BigtableTrackingVerbosity.Raw));
        var op = new BigtableOperationInfo(BigtableOperation.ReadRows, "projects/p/instances/i/tables/my-table");

        tracker.LogRequest(op, null);

        var log = GetLogsFromThisTest().First();
        Assert.Contains("projects/p/instances/i/tables/my-table", log.Uri.ToString());
    }

    // ─── Service/caller names ──────────────────────────────

    [Fact]
    public void LogRequest_Uses_configured_service_and_caller()
    {
        var tracker = new BigtableTracker(MakeOptions(serviceName: "MyBigtable", callerName: "MyApi"));
        var op = new BigtableOperationInfo(BigtableOperation.ReadRows, "projects/p/instances/i/tables/t");

        tracker.LogRequest(op, null);

        var log = GetLogsFromThisTest().First();
        Assert.Equal("MyBigtable", log.ServiceName);
        Assert.Equal("MyApi", log.CallerName);
    }

    // ─── ExcludedOperations ───────────────────────────────

    [Fact]
    public void LogRequest_ExcludedOperation_NoLog()
    {
        var options = MakeOptions();
        options.ExcludedOperations = [BigtableOperation.SampleRowKeys];
        var tracker = new BigtableTracker(options);
        var op = new BigtableOperationInfo(BigtableOperation.SampleRowKeys, "projects/p/instances/i/tables/t");

        var (reqId, traceId) = tracker.LogRequest(op, "data");

        Assert.Empty(GetLogsFromThisTest());
        Assert.Equal(Guid.Empty, reqId);
    }

    [Fact]
    public void LogResponse_ExcludedOperation_NoLog()
    {
        var options = MakeOptions();
        options.ExcludedOperations = [BigtableOperation.ReadRows];
        var tracker = new BigtableTracker(options);
        var op = new BigtableOperationInfo(BigtableOperation.ReadRows, "projects/p/instances/i/tables/t");

        tracker.LogResponse(op, Guid.NewGuid(), Guid.NewGuid(), "response");

        Assert.Empty(GetLogsFromThisTest());
    }

    [Fact]
    public void LogRequest_NonExcludedOperation_StillLogs()
    {
        var options = MakeOptions();
        options.ExcludedOperations = [BigtableOperation.SampleRowKeys];
        var tracker = new BigtableTracker(options);
        var op = new BigtableOperationInfo(BigtableOperation.ReadRows, "projects/p/instances/i/tables/t");

        tracker.LogRequest(op, null);

        Assert.Single(GetLogsFromThisTest());
    }

    // ─── ITrackingComponent ────────────────────────────────

    [Fact]
    public void Implements_ITrackingComponent()
    {
        var tracker = new BigtableTracker(MakeOptions());
        Assert.IsAssignableFrom<ITrackingComponent>(tracker);
    }

    [Fact]
    public void WasInvoked_IsFalse_BeforeAnyCalls()
    {
        var tracker = new BigtableTracker(MakeOptions());
        Assert.False(tracker.WasInvoked);
    }

    [Fact]
    public void WasInvoked_IsTrue_AfterLogRequest()
    {
        var tracker = new BigtableTracker(MakeOptions());
        var op = new BigtableOperationInfo(BigtableOperation.MutateRow, "projects/p/instances/i/tables/t", "row1");

        tracker.LogRequest(op, null);

        Assert.True(tracker.WasInvoked);
    }

    [Fact]
    public void InvocationCount_Increments()
    {
        var tracker = new BigtableTracker(MakeOptions());
        var op = new BigtableOperationInfo(BigtableOperation.MutateRow, "projects/p/instances/i/tables/t", "row1");

        tracker.LogRequest(op, null);
        tracker.LogRequest(op, null);

        Assert.Equal(2, tracker.InvocationCount);
    }

    [Fact]
    public void ComponentName_IncludesServiceName()
    {
        var tracker = new BigtableTracker(MakeOptions(serviceName: "MyBigtable"));
        Assert.Contains("MyBigtable", tracker.ComponentName);
    }

    // ─── HttpContextAccessor ────────────────────────────────

    [Fact]
    public void Tracker_reads_HttpContextAccessor_from_options_when_not_passed_directly()
    {
        var headerTestId = Guid.NewGuid().ToString();
        var options = MakeOptions();
        options.HttpContextAccessor = RequestHeaderAccessor.For("Header Test", headerTestId);
        var tracker = new BigtableTracker(options);

        tracker.LogRequest(new BigtableOperationInfo(BigtableOperation.ReadRows, "projects/p/instances/i/tables/t"), "filter");

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
        var tracker = new BigtableTracker(options, RequestHeaderAccessor.For("Explicit Test", explicitTestId));

        tracker.LogRequest(new BigtableOperationInfo(BigtableOperation.ReadRows, "projects/p/instances/i/tables/t"), "filter");

        Assert.Contains(RequestResponseLogger.RequestAndResponseLogs, l => l.TestId == explicitTestId);
        Assert.DoesNotContain(RequestResponseLogger.RequestAndResponseLogs, l => l.TestId == optionsTestId);
    }

    [Fact]
    public void Tracker_has_no_accessor_when_neither_options_nor_parameter()
    {
        Assert.False(new BigtableTracker(MakeOptions()).HasHttpContextAccessor);
    }
}
