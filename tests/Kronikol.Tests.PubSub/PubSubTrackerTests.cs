using Kronikol.Extensions.PubSub;
using Kronikol.Tests.Tracking;
using Kronikol.Tracking;

namespace Kronikol.Tests.PubSub;

public class PubSubTrackerTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private RequestResponseLog[] GetLogsFromThisTest()
    {
        return RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == _testId)
            .ToArray();
    }

    private PubSubTrackingOptions MakeOptions(
        PubSubTrackingVerbosity verbosity = PubSubTrackingVerbosity.Detailed,
        string serviceName = "PubSub",
        string callerName = "TestCaller") => new()
    {
        ServiceName = serviceName,
        CallerName = callerName,
        Verbosity = verbosity,
        CurrentTestInfoFetcher = () => ("My PubSub Test", _testId),
    };

    // ─── HttpContextAccessor ────────────────────────────────

    [Fact]
    public void Tracker_reads_HttpContextAccessor_from_options_when_not_passed_directly()
    {
        var headerTestId = Guid.NewGuid().ToString();
        var options = MakeOptions();
        options.HttpContextAccessor = RequestHeaderAccessor.For("Header Test", headerTestId);
        var tracker = new PubSubTracker(options);

        tracker.LogRequest(new PubSubOperationInfo(PubSubOperation.Publish, "projects/p/topics/my-topic", null, 1), "hello");

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
        var tracker = new PubSubTracker(options, RequestHeaderAccessor.For("Explicit Test", explicitTestId));

        tracker.LogRequest(new PubSubOperationInfo(PubSubOperation.Publish, "projects/p/topics/my-topic", null, 1), "hello");

        Assert.Contains(RequestResponseLogger.RequestAndResponseLogs, l => l.TestId == explicitTestId);
        Assert.DoesNotContain(RequestResponseLogger.RequestAndResponseLogs, l => l.TestId == optionsTestId);
    }

    [Fact]
    public void Tracker_has_no_accessor_when_neither_options_nor_parameter()
    {
        Assert.False(new PubSubTracker(MakeOptions()).HasHttpContextAccessor);
    }

    // ─── LogRequest ─────────────────────────────────────────

    [Fact]
    public void LogRequest_Logs_request_entry()
    {
        var tracker = new PubSubTracker(MakeOptions());
        var op = new PubSubOperationInfo(PubSubOperation.Publish, "projects/p/topics/my-topic", null, 1);

        tracker.LogRequest(op, "hello world");

        var logs = GetLogsFromThisTest();
        Assert.Single(logs);
        Assert.Equal(RequestResponseType.Request, logs[0].Type);
    }

    [Fact]
    public void LogRequest_Returns_ids_for_pairing()
    {
        var tracker = new PubSubTracker(MakeOptions());
        var op = new PubSubOperationInfo(PubSubOperation.Publish, "projects/p/topics/my-topic", null, 1);

        var (reqId, traceId) = tracker.LogRequest(op, "hello world");

        Assert.NotEqual(Guid.Empty, reqId);
        Assert.NotEqual(Guid.Empty, traceId);
    }

    [Fact]
    public void LogRequest_NoTestInfo_NoLog()
    {
        var options = MakeOptions();
        options.CurrentTestInfoFetcher = null;
        var tracker = new PubSubTracker(options);
        var op = new PubSubOperationInfo(PubSubOperation.Publish, "projects/p/topics/t", null, 1);

        var (reqId, traceId) = tracker.LogRequest(op, "hello");

        Assert.Empty(GetLogsFromThisTest());
        Assert.Equal(Guid.Empty, reqId);
    }

    [Fact]
    public void LogRequest_Uses_event_metatype()
    {
        var tracker = new PubSubTracker(MakeOptions());
        var op = new PubSubOperationInfo(PubSubOperation.Publish, "projects/p/topics/t", null, 1);

        tracker.LogRequest(op, "hello");

        var log = GetLogsFromThisTest().First();
        Assert.Equal(RequestResponseMetaType.Event, log.MetaType);
    }

    // ─── LogResponse ───────────────────────────────────────

    [Fact]
    public void LogResponse_Logs_response_entry()
    {
        var tracker = new PubSubTracker(MakeOptions());
        var op = new PubSubOperationInfo(PubSubOperation.Publish, "projects/p/topics/my-topic", null, 1);
        var (reqId, traceId) = tracker.LogRequest(op, "hello");

        tracker.LogResponse(op, reqId, traceId, "MessageId: abc");

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal(RequestResponseType.Response, logs[1].Type);
    }

    [Fact]
    public void LogResponse_MatchesTraceId()
    {
        var tracker = new PubSubTracker(MakeOptions());
        var op = new PubSubOperationInfo(PubSubOperation.Publish, "projects/p/topics/t", null, 1);
        var (reqId, traceId) = tracker.LogRequest(op, "hello");

        tracker.LogResponse(op, reqId, traceId, null);

        var logs = GetLogsFromThisTest();
        Assert.Equal(logs[0].TraceId, logs[1].TraceId);
    }

    // ─── Verbosity ──────────────────────────────────────────

    [Fact]
    public void LogRequest_Detailed_IncludesContent()
    {
        var tracker = new PubSubTracker(MakeOptions(PubSubTrackingVerbosity.Detailed));
        var op = new PubSubOperationInfo(PubSubOperation.Publish, "projects/p/topics/t", null, 1);

        tracker.LogRequest(op, "hello world");

        var log = GetLogsFromThisTest().First();
        Assert.Equal("hello world", log.Content);
    }

    [Fact]
    public void LogRequest_Summarised_OmitsContent()
    {
        var tracker = new PubSubTracker(MakeOptions(PubSubTrackingVerbosity.Summarised));
        var op = new PubSubOperationInfo(PubSubOperation.Publish, "projects/p/topics/t", null, 1);

        tracker.LogRequest(op, "hello world");

        var log = GetLogsFromThisTest().First();
        Assert.Null(log.Content);
    }

    // ─── URI ────────────────────────────────────────────────

    [Fact]
    public void LogRequest_URI_uses_pubsub_scheme()
    {
        var tracker = new PubSubTracker(MakeOptions());
        var op = new PubSubOperationInfo(PubSubOperation.Publish, "projects/p/topics/my-topic", null, 1);

        tracker.LogRequest(op, "hello");

        var log = GetLogsFromThisTest().First();
        Assert.StartsWith("pubsub://", log.Uri.ToString());
    }

    [Fact]
    public void LogRequest_URI_Detailed_uses_short_topic_name()
    {
        var tracker = new PubSubTracker(MakeOptions(PubSubTrackingVerbosity.Detailed));
        var op = new PubSubOperationInfo(PubSubOperation.Publish, "projects/p/topics/my-topic", null, 1);

        tracker.LogRequest(op, "hello");

        var log = GetLogsFromThisTest().First();
        Assert.Contains("my-topic", log.Uri.ToString());
    }

    [Fact]
    public void LogRequest_URI_Raw_uses_full_resource_name()
    {
        var tracker = new PubSubTracker(MakeOptions(PubSubTrackingVerbosity.Raw));
        var op = new PubSubOperationInfo(PubSubOperation.Publish, "projects/p/topics/my-topic", null, 1);

        tracker.LogRequest(op, "hello");

        var log = GetLogsFromThisTest().First();
        Assert.Contains("projects/p/topics/my-topic", log.Uri.ToString());
    }

    // ─── Service/caller names ──────────────────────────────

    [Fact]
    public void LogRequest_Uses_configured_service_and_caller()
    {
        var tracker = new PubSubTracker(MakeOptions(serviceName: "MyPubSub", callerName: "MyApi"));
        var op = new PubSubOperationInfo(PubSubOperation.Publish, "projects/p/topics/t", null, 1);

        tracker.LogRequest(op, "hello");

        var log = GetLogsFromThisTest().First();
        Assert.Equal("MyPubSub", log.ServiceName);
        Assert.Equal("MyApi", log.CallerName);
    }

    // ─── ITrackingComponent ────────────────────────────────

    [Fact]
    public void Implements_ITrackingComponent()
    {
        var tracker = new PubSubTracker(MakeOptions());
        Assert.IsAssignableFrom<ITrackingComponent>(tracker);
    }

    [Fact]
    public void WasInvoked_IsFalse_BeforeAnyCalls()
    {
        var tracker = new PubSubTracker(MakeOptions());
        Assert.False(tracker.WasInvoked);
    }

    [Fact]
    public void WasInvoked_IsTrue_AfterCall()
    {
        var tracker = new PubSubTracker(MakeOptions());
        var op = new PubSubOperationInfo(PubSubOperation.Publish, "projects/p/topics/t", null, 1);

        tracker.LogRequest(op, "hello");

        Assert.True(tracker.WasInvoked);
    }

    [Fact]
    public void InvocationCount_StartsAtZero()
    {
        var tracker = new PubSubTracker(MakeOptions());
        Assert.Equal(0, tracker.InvocationCount);
    }

    [Fact]
    public void InvocationCount_IncreasesWithEachCall()
    {
        var tracker = new PubSubTracker(MakeOptions());
        var op = new PubSubOperationInfo(PubSubOperation.Publish, "projects/p/topics/t", null, 1);

        tracker.LogRequest(op, "1");
        tracker.LogRequest(op, "2");
        tracker.LogRequest(op, "3");

        Assert.Equal(3, tracker.InvocationCount);
    }

    [Fact]
    public void ComponentName_MatchesServiceName()
    {
        var tracker = new PubSubTracker(MakeOptions(serviceName: "MyPubSub"));
        Assert.Equal("PubSubTracker (MyPubSub)", tracker.ComponentName);
    }

    [Fact]
    public void Constructor_AutoRegistersWithTrackingComponentRegistry()
    {
        TrackingComponentRegistry.Clear();
        var tracker = new PubSubTracker(MakeOptions());

        var components = TrackingComponentRegistry.GetRegisteredComponents();
        Assert.Contains(components, c => ReferenceEquals(c, tracker));
    }
}
