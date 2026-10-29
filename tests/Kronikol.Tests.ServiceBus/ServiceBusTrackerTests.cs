using Kronikol.Extensions.ServiceBus;
using Kronikol.Tracking;
using Kronikol.Tests.History;

namespace Kronikol.Tests.ServiceBus;

[Collection("TrackingComponentRegistry")]
public class ServiceBusTrackerTests : IDisposable
{
    private readonly string _testId = Guid.NewGuid().ToString();

    public ServiceBusTrackerTests()
    {
        TrackingComponentRegistry.Clear();
    }

    public void Dispose()
    {
        TrackingComponentRegistry.Clear();
    }

    private ServiceBusTrackingOptions MakeOptions(
        ServiceBusTrackingVerbosity verbosity = ServiceBusTrackingVerbosity.Detailed,
        string serviceName = "ServiceBus",
        string callerName = "TestCaller") => new()
    {
        ServiceName = serviceName,
        CallerName = callerName,
        Verbosity = verbosity,
        CurrentTestInfoFetcher = () => ("My Test", _testId),
    };

    private RequestResponseLog[] GetLogs() =>
        RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == _testId)
            .ToArray();

    // ─── Basic logging ─────────────────────────────────────────

    [Fact]
    public void Every_change_to_a_recorded_call_line_moves_the_version()
    {
        // The history guard (plans/CALL_OPERATION_AND_ORIGIN_PLAN.md 4.8): what this tracker records for one fixed call, as the
        // call line history fingerprints, pinned under the rule it was pinned with (HistoryGuard).
        var tracker = new ServiceBusTracker(MakeOptions());
        var op = new ServiceBusOperationInfo(ServiceBusOperation.Send, "orders-queue");
        var (reqId, traceId) = tracker.LogRequest(op, "message body");
        tracker.LogResponse(op, reqId, traceId, null);

        HistoryGuard.Pin(GetLogs(), 10,
            "TestCaller>ServiceBus Send → orders-queue /orders-queue/ -");
    }

    [Fact]
    public void LogRequest_CreatesRequestEntry()
    {
        var tracker = new ServiceBusTracker(MakeOptions());
        var op = new ServiceBusOperationInfo(ServiceBusOperation.Send, "orders-queue");

        tracker.LogRequest(op, "message body");

        var logs = GetLogs();
        Assert.Single(logs);
        Assert.Equal(RequestResponseType.Request, logs[0].Type);
    }

    [Fact]
    public void LogResponse_CreatesResponseEntry()
    {
        var tracker = new ServiceBusTracker(MakeOptions());
        var op = new ServiceBusOperationInfo(ServiceBusOperation.Send, "orders-queue");

        var (reqId, traceId) = tracker.LogRequest(op, "message body");
        tracker.LogResponse(op, reqId, traceId, null);

        var logs = GetLogs();
        Assert.Equal(2, logs.Length);
        Assert.Equal(RequestResponseType.Response, logs[1].Type);
    }

    [Fact]
    public void LogRequest_And_LogResponse_ShareSameIds()
    {
        var tracker = new ServiceBusTracker(MakeOptions());
        var op = new ServiceBusOperationInfo(ServiceBusOperation.Send, "orders-queue");

        var (reqId, traceId) = tracker.LogRequest(op, "body");
        tracker.LogResponse(op, reqId, traceId, null);

        var logs = GetLogs();
        Assert.Equal(logs[0].TraceId, logs[1].TraceId);
        Assert.Equal(logs[0].RequestResponseId, logs[1].RequestResponseId);
    }

    [Fact]
    public void LogRequest_ServiceAndCallerNames_Propagated()
    {
        var tracker = new ServiceBusTracker(MakeOptions(serviceName: "OrdersBus", callerName: "OrdersApi"));
        var op = new ServiceBusOperationInfo(ServiceBusOperation.Send, "orders-queue");

        tracker.LogRequest(op, null);

        var log = GetLogs().Single();
        Assert.Equal("OrdersBus", log.ServiceName);
        Assert.Equal("OrdersApi", log.CallerName);
    }

    [Fact]
    public void LogRequest_NoTestInfoFetcher_DoesNotLog()
    {
        var options = MakeOptions();
        options.CurrentTestInfoFetcher = null;
        var tracker = new ServiceBusTracker(options);
        var op = new ServiceBusOperationInfo(ServiceBusOperation.Send, "orders-queue");

        var (reqId, traceId) = tracker.LogRequest(op, "body");

        Assert.Empty(GetLogs());
        Assert.Equal(Guid.Empty, reqId);
        Assert.Equal(Guid.Empty, traceId);
    }

    [Fact]
    public void LogResponse_EmptyGuids_DoesNotLog()
    {
        var tracker = new ServiceBusTracker(MakeOptions());
        var op = new ServiceBusOperationInfo(ServiceBusOperation.Send, "orders-queue");

        tracker.LogResponse(op, Guid.Empty, Guid.Empty, null);

        Assert.Empty(GetLogs());
    }

    // ─── URI building ──────────────────────────────────────────

    [Fact]
    public void LogRequest_Detailed_UriContainsQueueName()
    {
        var tracker = new ServiceBusTracker(MakeOptions(ServiceBusTrackingVerbosity.Detailed));
        var op = new ServiceBusOperationInfo(ServiceBusOperation.Send, "orders-queue");

        tracker.LogRequest(op, null);

        var log = GetLogs().Single();
        Assert.Contains("orders-queue", log.Uri.ToString());
        Assert.StartsWith("servicebus://", log.Uri.ToString());
    }

    [Fact]
    public void LogRequest_Detailed_WithSubscription_UriContainsBoth()
    {
        var tracker = new ServiceBusTracker(MakeOptions(ServiceBusTrackingVerbosity.Detailed));
        var op = new ServiceBusOperationInfo(ServiceBusOperation.Receive, "orders-topic", "my-subscription");

        tracker.LogRequest(op, null);

        var log = GetLogs().Single();
        Assert.Contains("orders-topic", log.Uri.ToString());
        Assert.Contains("my-subscription", log.Uri.ToString());
    }

    [Fact]
    public void LogRequest_Summarised_UriHasTrailingSlash()
    {
        var tracker = new ServiceBusTracker(MakeOptions(ServiceBusTrackingVerbosity.Summarised));
        var op = new ServiceBusOperationInfo(ServiceBusOperation.Send, "orders-queue");

        tracker.LogRequest(op, null);

        var log = GetLogs().Single();
        Assert.Equal("servicebus://orders-queue/", log.Uri.ToString());
    }

    // ─── Content suppression at Summarised ─────────────────────

    [Fact]
    public void LogRequest_Summarised_OmitsContent()
    {
        var tracker = new ServiceBusTracker(MakeOptions(ServiceBusTrackingVerbosity.Summarised));
        var op = new ServiceBusOperationInfo(ServiceBusOperation.Send, "orders-queue");

        tracker.LogRequest(op, "should be omitted");

        var log = GetLogs().Single();
        Assert.Null(log.Content);
    }

    [Fact]
    public void LogRequest_Detailed_IncludesContent()
    {
        var tracker = new ServiceBusTracker(MakeOptions(ServiceBusTrackingVerbosity.Detailed));
        var op = new ServiceBusOperationInfo(ServiceBusOperation.Send, "orders-queue");

        tracker.LogRequest(op, "hello world");

        var log = GetLogs().Single();
        Assert.Equal("hello world", log.Content);
    }

    // ─── MetaType ──────────────────────────────────────────────

    [Theory]
    [InlineData(ServiceBusOperation.Send)]
    [InlineData(ServiceBusOperation.SendBatch)]
    [InlineData(ServiceBusOperation.Receive)]
    [InlineData(ServiceBusOperation.ReceiveBatch)]
    [InlineData(ServiceBusOperation.Schedule)]
    [InlineData(ServiceBusOperation.Peek)]
    public void LogRequest_MessagingOps_HaveEventMetaType(ServiceBusOperation operation)
    {
        var tracker = new ServiceBusTracker(MakeOptions());
        var op = new ServiceBusOperationInfo(operation, "q");

        tracker.LogRequest(op, null);

        var log = GetLogs().Single();
        Assert.Equal(RequestResponseMetaType.Event, log.MetaType);
    }

    [Theory]
    [InlineData(ServiceBusOperation.Complete)]
    [InlineData(ServiceBusOperation.Abandon)]
    [InlineData(ServiceBusOperation.DeadLetter)]
    [InlineData(ServiceBusOperation.Defer)]
    [InlineData(ServiceBusOperation.RenewMessageLock)]
    public void LogRequest_ManagementOps_HaveDefaultMetaType(ServiceBusOperation operation)
    {
        var tracker = new ServiceBusTracker(MakeOptions());
        var op = new ServiceBusOperationInfo(operation, "q");

        tracker.LogRequest(op, null);

        var log = GetLogs().Single();
        Assert.Equal(RequestResponseMetaType.Default, log.MetaType);
    }

    // ─── Diagram label ─────────────────────────────────────────

    [Fact]
    public void LogRequest_Detailed_MethodLabelMatchesClassifier()
    {
        var tracker = new ServiceBusTracker(MakeOptions(ServiceBusTrackingVerbosity.Detailed));
        var op = new ServiceBusOperationInfo(ServiceBusOperation.Send, "orders-queue");

        tracker.LogRequest(op, null);

        var log = GetLogs().Single();
        Assert.Equal("Send → orders-queue", log.Method.Value?.ToString());
    }

    // ─── ITrackingComponent ────────────────────────────────────

    [Fact]
    public void Implements_ITrackingComponent()
    {
        var tracker = new ServiceBusTracker(MakeOptions());

        Assert.IsAssignableFrom<ITrackingComponent>(tracker);
    }

    [Fact]
    public void WasInvoked_IsFalse_BeforeAnyRequests()
    {
        var tracker = new ServiceBusTracker(MakeOptions());

        Assert.False(tracker.WasInvoked);
    }

    [Fact]
    public void WasInvoked_IsTrue_AfterLogRequest()
    {
        var tracker = new ServiceBusTracker(MakeOptions());
        var op = new ServiceBusOperationInfo(ServiceBusOperation.Send, "q");

        tracker.LogRequest(op, null);

        Assert.True(tracker.WasInvoked);
    }

    [Fact]
    public void InvocationCount_StartsAtZero()
    {
        var tracker = new ServiceBusTracker(MakeOptions());

        Assert.Equal(0, tracker.InvocationCount);
    }

    [Fact]
    public void InvocationCount_IncreasesWithEachCall()
    {
        var tracker = new ServiceBusTracker(MakeOptions());
        var op = new ServiceBusOperationInfo(ServiceBusOperation.Send, "q");

        tracker.LogRequest(op, null);
        tracker.LogRequest(op, null);
        tracker.LogRequest(op, null);

        Assert.Equal(3, tracker.InvocationCount);
    }

    [Fact]
    public void ComponentName_MatchesServiceName()
    {
        var tracker = new ServiceBusTracker(MakeOptions(serviceName: "OrdersBus"));

        Assert.Equal("ServiceBusTracker (OrdersBus)", tracker.ComponentName);
    }

    [Fact]
    public void Constructor_AutoRegistersWithTrackingComponentRegistry()
    {
        var tracker = new ServiceBusTracker(MakeOptions());

        var registered = TrackingComponentRegistry.GetRegisteredComponents();
        Assert.Contains(registered, c => ReferenceEquals(c, tracker));
    }
}
