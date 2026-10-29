using Kronikol.Extensions.EventHubs;
using Kronikol.Tracking;
using Kronikol.Tests.History;

namespace Kronikol.Tests.EventHubs;

public class EventHubsTrackerTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private RequestResponseLog[] GetLogsFromThisTest()
    {
        return RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == _testId)
            .ToArray();
    }

    private EventHubsTrackingOptions MakeOptions(
        EventHubsTrackingVerbosity verbosity = EventHubsTrackingVerbosity.Detailed,
        string serviceName = "EventHubs",
        string callerName = "TestCaller") => new()
    {
        ServiceName = serviceName,
        CallerName = callerName,
        Verbosity = verbosity,
        CurrentTestInfoFetcher = () => ("My EventHubs Test", _testId),
    };

    // ─── LogRequest ─────────────────────────────────────────

    [Fact]
    public void Every_change_to_a_recorded_call_line_moves_the_version()
    {
        // The history guard (plans/CALL_OPERATION_AND_ORIGIN_PLAN.md 4.8): what this tracker records for one fixed call, as the
        // call line history fingerprints, pinned under the rule it was pinned with (HistoryGuard).
        var tracker = new EventHubsTracker(MakeOptions());
        var op = new EventHubsOperationInfo(EventHubsOperation.Send, "telemetry", null, 1);
        var (reqId, traceId) = tracker.LogRequest(op, "hello");
        tracker.LogResponse(op, reqId, traceId, null);

        HistoryGuard.Pin(GetLogsFromThisTest(), 10,
            "TestCaller>EventHubs Send → telemetry /telemetry -");
    }

    [Fact]
    public void LogRequest_Logs_request_entry()
    {
        var tracker = new EventHubsTracker(MakeOptions());
        var op = new EventHubsOperationInfo(EventHubsOperation.Send, "telemetry", null, 1);

        tracker.LogRequest(op, "hello world");

        var logs = GetLogsFromThisTest();
        Assert.Single(logs);
        Assert.Equal(RequestResponseType.Request, logs[0].Type);
    }

    [Fact]
    public void LogRequest_Returns_ids_for_pairing()
    {
        var tracker = new EventHubsTracker(MakeOptions());
        var op = new EventHubsOperationInfo(EventHubsOperation.Send, "telemetry", null, 1);

        var (reqId, traceId) = tracker.LogRequest(op, "hello");

        Assert.NotEqual(Guid.Empty, reqId);
        Assert.NotEqual(Guid.Empty, traceId);
    }

    [Fact]
    public void LogRequest_NoTestInfo_NoLog()
    {
        var options = MakeOptions();
        options.CurrentTestInfoFetcher = null;
        var tracker = new EventHubsTracker(options);
        var op = new EventHubsOperationInfo(EventHubsOperation.Send, "telemetry", null, 1);

        var (reqId, traceId) = tracker.LogRequest(op, "hello");

        Assert.Empty(GetLogsFromThisTest());
        Assert.Equal(Guid.Empty, reqId);
    }

    [Fact]
    public void LogRequest_Uses_event_metatype()
    {
        var tracker = new EventHubsTracker(MakeOptions());
        var op = new EventHubsOperationInfo(EventHubsOperation.Send, "telemetry", null, 1);

        tracker.LogRequest(op, "hello");

        var log = GetLogsFromThisTest().First();
        Assert.Equal(RequestResponseMetaType.Event, log.MetaType);
    }

    // ─── LogResponse ───────────────────────────────────────

    [Fact]
    public void LogResponse_Logs_response_entry()
    {
        var tracker = new EventHubsTracker(MakeOptions());
        var op = new EventHubsOperationInfo(EventHubsOperation.Send, "telemetry", null, 1);
        var (reqId, traceId) = tracker.LogRequest(op, "hello");

        tracker.LogResponse(op, reqId, traceId, null);

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal(RequestResponseType.Response, logs[1].Type);
    }

    [Fact]
    public void LogResponse_MatchesTraceId()
    {
        var tracker = new EventHubsTracker(MakeOptions());
        var op = new EventHubsOperationInfo(EventHubsOperation.Send, "telemetry", null, 1);
        var (reqId, traceId) = tracker.LogRequest(op, "hello");

        tracker.LogResponse(op, reqId, traceId, null);

        var logs = GetLogsFromThisTest();
        Assert.Equal(logs[0].TraceId, logs[1].TraceId);
    }

    // ─── Verbosity ──────────────────────────────────────────

    [Fact]
    public void LogRequest_Detailed_IncludesContent()
    {
        var tracker = new EventHubsTracker(MakeOptions(EventHubsTrackingVerbosity.Detailed));
        var op = new EventHubsOperationInfo(EventHubsOperation.Send, "telemetry", null, 1);

        tracker.LogRequest(op, "event data");

        var log = GetLogsFromThisTest().First();
        Assert.Equal("event data", log.Content);
    }

    [Fact]
    public void LogRequest_Summarised_OmitsContent()
    {
        var tracker = new EventHubsTracker(MakeOptions(EventHubsTrackingVerbosity.Summarised));
        var op = new EventHubsOperationInfo(EventHubsOperation.Send, "telemetry", null, 1);

        tracker.LogRequest(op, "event data");

        var log = GetLogsFromThisTest().First();
        Assert.Null(log.Content);
    }

    // ─── URI ────────────────────────────────────────────────

    [Fact]
    public void LogRequest_URI_uses_eventhubs_scheme()
    {
        var tracker = new EventHubsTracker(MakeOptions());
        var op = new EventHubsOperationInfo(EventHubsOperation.Send, "telemetry", null, 1);

        tracker.LogRequest(op, "hello");

        var log = GetLogsFromThisTest().First();
        Assert.StartsWith("eventhubs://", log.Uri.ToString());
    }

    [Fact]
    public void LogRequest_URI_includes_hub_name()
    {
        var tracker = new EventHubsTracker(MakeOptions());
        var op = new EventHubsOperationInfo(EventHubsOperation.Send, "my-hub", null, 1);

        tracker.LogRequest(op, "hello");

        var log = GetLogsFromThisTest().First();
        Assert.Contains("my-hub", log.Uri.ToString());
    }

    [Fact]
    public void LogRequest_URI_includes_partition_when_present()
    {
        var tracker = new EventHubsTracker(MakeOptions(EventHubsTrackingVerbosity.Detailed));
        var op = new EventHubsOperationInfo(EventHubsOperation.ReadEventsFromPartition, "telemetry", "2");

        tracker.LogRequest(op, null);

        var log = GetLogsFromThisTest().First();
        Assert.Contains("2", log.Uri.ToString());
    }

    // ─── Service/caller names ──────────────────────────────

    [Fact]
    public void LogRequest_Uses_configured_service_and_caller()
    {
        var tracker = new EventHubsTracker(MakeOptions(serviceName: "MyHub", callerName: "MyApi"));
        var op = new EventHubsOperationInfo(EventHubsOperation.Send, "telemetry", null, 1);

        tracker.LogRequest(op, "hello");

        var log = GetLogsFromThisTest().First();
        Assert.Equal("MyHub", log.ServiceName);
        Assert.Equal("MyApi", log.CallerName);
    }

    // ─── ITrackingComponent ────────────────────────────────

    [Fact]
    public void Implements_ITrackingComponent()
    {
        var tracker = new EventHubsTracker(MakeOptions());
        Assert.IsAssignableFrom<ITrackingComponent>(tracker);
    }

    [Fact]
    public void WasInvoked_IsFalse_BeforeAnyCalls()
    {
        var tracker = new EventHubsTracker(MakeOptions());
        Assert.False(tracker.WasInvoked);
    }

    [Fact]
    public void WasInvoked_IsTrue_AfterCall()
    {
        var tracker = new EventHubsTracker(MakeOptions());
        var op = new EventHubsOperationInfo(EventHubsOperation.Send, "telemetry", null, 1);

        tracker.LogRequest(op, "hello");

        Assert.True(tracker.WasInvoked);
    }

    [Fact]
    public void InvocationCount_StartsAtZero()
    {
        var tracker = new EventHubsTracker(MakeOptions());
        Assert.Equal(0, tracker.InvocationCount);
    }

    [Fact]
    public void InvocationCount_IncreasesWithEachCall()
    {
        var tracker = new EventHubsTracker(MakeOptions());
        var op = new EventHubsOperationInfo(EventHubsOperation.Send, "telemetry", null, 1);

        tracker.LogRequest(op, "1");
        tracker.LogRequest(op, "2");
        tracker.LogRequest(op, "3");

        Assert.Equal(3, tracker.InvocationCount);
    }

    [Fact]
    public void ComponentName_MatchesServiceName()
    {
        var tracker = new EventHubsTracker(MakeOptions(serviceName: "MyHub"));
        Assert.Equal("EventHubsTracker (MyHub)", tracker.ComponentName);
    }

    [Fact]
    public void Constructor_AutoRegistersWithTrackingComponentRegistry()
    {
        TrackingComponentRegistry.Clear();
        var tracker = new EventHubsTracker(MakeOptions());

        var components = TrackingComponentRegistry.GetRegisteredComponents();
        Assert.Contains(components, c => ReferenceEquals(c, tracker));
    }
}
