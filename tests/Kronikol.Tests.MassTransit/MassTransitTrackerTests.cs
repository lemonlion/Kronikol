using Kronikol.Extensions.MassTransit;
using Kronikol.Tracking;
using Kronikol.Tests.History;

namespace Kronikol.Tests.MassTransit;

public class MassTransitTrackerTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private RequestResponseLog[] GetLogsFromThisTest()
    {
        return RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == _testId)
            .ToArray();
    }

    private MassTransitTrackingOptions MakeOptions(
        MassTransitTrackingVerbosity verbosity = MassTransitTrackingVerbosity.Detailed,
        string serviceName = "MassTransit",
        string callerName = "TestCaller") => new()
    {
        ServiceName = serviceName,
        CallerName = callerName,
        Verbosity = verbosity,
        CurrentTestInfoFetcher = () => ("My MassTransit Test", _testId),
    };

    private record TestMessage(string OrderId, decimal Amount);

    // ─── LogSend ─────────────────────────────────────────────

    [Fact]
    public void Every_change_to_a_recorded_call_line_moves_the_version()
    {
        // The history guard (plans/CALL_OPERATION_AND_ORIGIN_PLAN.md 4.8): what this tracker records for one fixed call, as the
        // call line history fingerprints, pinned under the rule it was pinned with (HistoryGuard).
        var tracker = new MassTransitTracker(MakeOptions(MassTransitTrackingVerbosity.Detailed));
        var op = new MassTransitOperationInfo(MassTransitOperation.Send, "OrderCreated", new Uri("rabbitmq://localhost/orders"));
        tracker.LogSend(op, new TestMessage("ORD-1", 99.99m));

        HistoryGuard.Pin(GetLogsFromThisTest(), 9,
            "TestCaller>MassTransit Send OrderCreated /orders -");
    }

    [Fact]
    public void LogSend_Logs_request_and_response()
    {
        var tracker = new MassTransitTracker(MakeOptions());
        var op = new MassTransitOperationInfo(MassTransitOperation.Send, "TestMessage",
            new Uri("rabbitmq://localhost/orders"));

        tracker.LogSend(op, new TestMessage("ORD-1", 99.99m));

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal(RequestResponseType.Request, logs[0].Type);
        Assert.Equal(RequestResponseType.Response, logs[1].Type);
    }

    [Fact]
    public void LogSend_Uses_event_metatype()
    {
        var tracker = new MassTransitTracker(MakeOptions());
        var op = new MassTransitOperationInfo(MassTransitOperation.Send, "TestMessage",
            new Uri("rabbitmq://localhost/orders"));

        tracker.LogSend(op, new TestMessage("ORD-1", 99.99m));

        var log = GetLogsFromThisTest().First();
        Assert.Equal(RequestResponseMetaType.Event, log.MetaType);
    }

    [Fact]
    public void LogSend_Includes_message_body_in_request()
    {
        var tracker = new MassTransitTracker(MakeOptions());
        var op = new MassTransitOperationInfo(MassTransitOperation.Send, "TestMessage",
            new Uri("rabbitmq://localhost/orders"));

        tracker.LogSend(op, new TestMessage("ORD-1", 99.99m));

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains("ORD-1", log.Content);
    }

    [Fact]
    public void LogSend_Omits_body_when_LogMessageBody_false()
    {
        var options = MakeOptions();
        options.LogMessageBody = false;
        var tracker = new MassTransitTracker(options);
        var op = new MassTransitOperationInfo(MassTransitOperation.Send, "TestMessage",
            new Uri("rabbitmq://localhost/orders"));

        tracker.LogSend(op, new TestMessage("ORD-1", 99.99m));

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Null(log.Content);
    }

    [Fact]
    public void LogSend_Omits_body_in_Summarised()
    {
        var tracker = new MassTransitTracker(MakeOptions(MassTransitTrackingVerbosity.Summarised));
        var op = new MassTransitOperationInfo(MassTransitOperation.Send, "TestMessage",
            new Uri("rabbitmq://localhost/orders"));

        tracker.LogSend(op, new TestMessage("ORD-1", 99.99m));

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Null(log.Content);
    }

    [Fact]
    public void LogSend_Skips_when_TrackSend_false()
    {
        var options = MakeOptions();
        options.TrackSend = false;
        var tracker = new MassTransitTracker(options);
        var op = new MassTransitOperationInfo(MassTransitOperation.Send, "TestMessage",
            new Uri("rabbitmq://localhost/orders"));

        tracker.LogSend(op, new TestMessage("ORD-1", 99.99m));

        Assert.Empty(GetLogsFromThisTest());
    }

    [Fact]
    public void LogSend_Skips_when_no_test_info()
    {
        var options = MakeOptions();
        options.CurrentTestInfoFetcher = null;
        var tracker = new MassTransitTracker(options);
        var op = new MassTransitOperationInfo(MassTransitOperation.Send, "TestMessage",
            new Uri("rabbitmq://localhost/orders"));

        tracker.LogSend(op, new TestMessage("ORD-1", 99.99m));

        Assert.Empty(GetLogsFromThisTest());
    }

    [Fact]
    public void LogSend_Uses_correct_service_and_caller()
    {
        var tracker = new MassTransitTracker(MakeOptions(callerName: "MyApi", serviceName: "RabbitMQ"));
        var op = new MassTransitOperationInfo(MassTransitOperation.Send, "TestMessage",
            new Uri("rabbitmq://localhost/orders"));

        tracker.LogSend(op, new TestMessage("ORD-1", 99.99m));

        var log = GetLogsFromThisTest().First();
        Assert.Equal("RabbitMQ", log.ServiceName);
        Assert.Equal("MyApi", log.CallerName);
    }

    [Fact]
    public void LogSend_Uses_detailed_label()
    {
        var tracker = new MassTransitTracker(MakeOptions(MassTransitTrackingVerbosity.Detailed));
        var op = new MassTransitOperationInfo(MassTransitOperation.Send, "OrderCreated",
            new Uri("rabbitmq://localhost/orders"));

        tracker.LogSend(op, new TestMessage("ORD-1", 99.99m));

        var log = GetLogsFromThisTest().First();
        Assert.Equal("Send OrderCreated", log.Method.Value?.ToString());
    }

    // ─── LogPublish ─────────────────────────────────────────

    [Fact]
    public void LogPublish_Logs_request_and_response()
    {
        var tracker = new MassTransitTracker(MakeOptions());
        var op = new MassTransitOperationInfo(MassTransitOperation.Publish, "UserRegistered",
            new Uri("rabbitmq://localhost/user-events"));

        tracker.LogPublish(op, new { UserId = "U-1" });

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
    }

    [Fact]
    public void LogPublish_Skips_when_TrackPublish_false()
    {
        var options = MakeOptions();
        options.TrackPublish = false;
        var tracker = new MassTransitTracker(options);
        var op = new MassTransitOperationInfo(MassTransitOperation.Publish, "UserRegistered",
            new Uri("rabbitmq://localhost/user-events"));

        tracker.LogPublish(op, new { UserId = "U-1" });

        Assert.Empty(GetLogsFromThisTest());
    }

    // ─── LogConsume ─────────────────────────────────────────

    [Fact]
    public void LogConsume_Logs_request_and_response()
    {
        var tracker = new MassTransitTracker(MakeOptions());
        var op = new MassTransitOperationInfo(MassTransitOperation.Consume, "OrderCreated",
            new Uri("rabbitmq://localhost/orders"));

        tracker.LogConsume(op, new TestMessage("ORD-1", 99.99m));

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
    }

    [Fact]
    public void LogConsume_Swaps_caller_and_service()
    {
        var tracker = new MassTransitTracker(MakeOptions(callerName: "MyApi", serviceName: "RabbitMQ"));
        var op = new MassTransitOperationInfo(MassTransitOperation.Consume, "OrderCreated",
            new Uri("rabbitmq://localhost/orders"));

        tracker.LogConsume(op, new TestMessage("ORD-1", 99.99m));

        var log = GetLogsFromThisTest().First();
        // Consume is incoming: caller becomes service, service becomes caller
        Assert.Equal("MyApi", log.ServiceName);
        Assert.Equal("RabbitMQ", log.CallerName);
    }

    [Fact]
    public void LogConsume_Skips_when_TrackConsume_false()
    {
        var options = MakeOptions();
        options.TrackConsume = false;
        var tracker = new MassTransitTracker(options);
        var op = new MassTransitOperationInfo(MassTransitOperation.Consume, "OrderCreated",
            new Uri("rabbitmq://localhost/orders"));

        tracker.LogConsume(op, new TestMessage("ORD-1", 99.99m));

        Assert.Empty(GetLogsFromThisTest());
    }

    // ─── Faults ─────────────────────────────────────────────

    [Fact]
    public void LogSendFault_Logs_with_exception_message()
    {
        var tracker = new MassTransitTracker(MakeOptions());
        var op = new MassTransitOperationInfo(MassTransitOperation.SendFault, "OrderCreated",
            new Uri("rabbitmq://localhost/orders"));

        tracker.LogSendFault(op, new InvalidOperationException("Connection refused"));

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains("Connection refused", log.Content);
    }

    [Fact]
    public void LogSendFault_Response_has_Fault_statuscode()
    {
        var tracker = new MassTransitTracker(MakeOptions());
        var op = new MassTransitOperationInfo(MassTransitOperation.SendFault, "OrderCreated",
            new Uri("rabbitmq://localhost/orders"));

        tracker.LogSendFault(op, new InvalidOperationException("Connection refused"));

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Equal("Fault", log.StatusCode?.Value?.ToString());
    }

    [Fact]
    public void LogSendFault_Skips_when_LogFaults_false()
    {
        var options = MakeOptions();
        options.LogFaults = false;
        var tracker = new MassTransitTracker(options);
        var op = new MassTransitOperationInfo(MassTransitOperation.SendFault, "OrderCreated",
            new Uri("rabbitmq://localhost/orders"));

        tracker.LogSendFault(op, new InvalidOperationException("Boom"));

        Assert.Empty(GetLogsFromThisTest());
    }

    [Fact]
    public void LogConsumeFault_Swaps_caller_and_service()
    {
        var tracker = new MassTransitTracker(MakeOptions(callerName: "MyApi", serviceName: "RabbitMQ"));
        var op = new MassTransitOperationInfo(MassTransitOperation.ConsumeFault, "OrderCreated",
            new Uri("rabbitmq://localhost/orders"));

        tracker.LogConsumeFault(op, new InvalidOperationException("Handler failed"));

        var log = GetLogsFromThisTest().First();
        Assert.Equal("MyApi", log.ServiceName);
        Assert.Equal("RabbitMQ", log.CallerName);
    }

    // ─── ITrackingComponent ─────────────────────────────────

    [Fact]
    public void Implements_ITrackingComponent()
    {
        var tracker = new MassTransitTracker(MakeOptions());
        Assert.IsAssignableFrom<ITrackingComponent>(tracker);
    }

    [Fact]
    public void WasInvoked_IsFalse_BeforeAnyMessages()
    {
        var tracker = new MassTransitTracker(MakeOptions());
        Assert.False(tracker.WasInvoked);
    }

    [Fact]
    public void WasInvoked_IsTrue_AfterSend()
    {
        var tracker = new MassTransitTracker(MakeOptions());
        var op = new MassTransitOperationInfo(MassTransitOperation.Send, "TestMessage",
            new Uri("rabbitmq://localhost/orders"));

        tracker.LogSend(op, new TestMessage("ORD-1", 99.99m));

        Assert.True(tracker.WasInvoked);
    }

    [Fact]
    public void InvocationCount_StartsAtZero()
    {
        var tracker = new MassTransitTracker(MakeOptions());
        Assert.Equal(0, tracker.InvocationCount);
    }

    [Fact]
    public void InvocationCount_IncrementsEvenWhenTrackingDisabled()
    {
        var options = MakeOptions();
        options.TrackSend = false;
        var tracker = new MassTransitTracker(options);
        var op = new MassTransitOperationInfo(MassTransitOperation.Send, "TestMessage",
            new Uri("rabbitmq://localhost/orders"));

        tracker.LogSend(op, new TestMessage("ORD-1", 99.99m));

        Assert.Equal(1, tracker.InvocationCount);
    }

    [Fact]
    public void ComponentName_MatchesServiceName()
    {
        var tracker = new MassTransitTracker(MakeOptions(serviceName: "OrderBus"));
        Assert.Equal("MassTransitTracker (OrderBus)", tracker.ComponentName);
    }

    [Fact]
    public void Constructor_AutoRegistersWithTrackingComponentRegistry()
    {
        TrackingComponentRegistry.Clear();
        var tracker = new MassTransitTracker(MakeOptions());

        var components = TrackingComponentRegistry.GetRegisteredComponents();
        Assert.Contains(components, c => ReferenceEquals(c, tracker));
    }
}
