using Kronikol.Extensions.Kafka;
using Kronikol.Tests.Tracking;
using Kronikol.Tracking;

namespace Kronikol.Tests.Kafka;

// Clears the process-wide TrackingComponentRegistry, which KafkaServiceCollectionExtensionsTests reads.
[Collection("TrackingComponentRegistry")]
public class KafkaTrackerTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private RequestResponseLog[] GetLogsFromThisTest()
    {
        return RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == _testId)
            .ToArray();
    }

    private KafkaTrackingOptions MakeOptions(
        KafkaTrackingVerbosity verbosity = KafkaTrackingVerbosity.Detailed,
        string serviceName = "Kafka",
        string callerName = "TestCaller") => new()
    {
        ServiceName = serviceName,
        CallerName = callerName,
        Verbosity = verbosity,
        CurrentTestInfoFetcher = () => ("My Kafka Test", _testId),
    };

    // ─── LogProduce ─────────────────────────────────────────

    [Fact]
    public void LogProduce_Logs_request_and_response()
    {
        var tracker = new KafkaTracker(MakeOptions());
        var op = new KafkaOperationInfo(KafkaOperation.ProduceAsync, "orders-topic", 0, 42);

        tracker.LogProduce(op, "Key: order-1, Value: {\"amount\":99}");

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal(RequestResponseType.Request, logs[0].Type);
        Assert.Equal(RequestResponseType.Response, logs[1].Type);
    }

    [Fact]
    public void LogProduce_Uses_event_metatype()
    {
        var tracker = new KafkaTracker(MakeOptions());
        var op = new KafkaOperationInfo(KafkaOperation.ProduceAsync, "orders-topic");

        tracker.LogProduce(op, "hello");

        var log = GetLogsFromThisTest().First();
        Assert.Equal(RequestResponseMetaType.Event, log.MetaType);
    }

    [Fact]
    public void LogProduce_Includes_content()
    {
        var tracker = new KafkaTracker(MakeOptions());
        var op = new KafkaOperationInfo(KafkaOperation.ProduceAsync, "orders-topic");

        tracker.LogProduce(op, "Key: k1, Value: v1");

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains("k1", log.Content);
    }

    [Fact]
    public void LogProduce_Omits_content_in_Summarised()
    {
        var tracker = new KafkaTracker(MakeOptions(KafkaTrackingVerbosity.Summarised));
        var op = new KafkaOperationInfo(KafkaOperation.ProduceAsync, "orders-topic");

        tracker.LogProduce(op, "Key: k1, Value: v1");

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Null(log.Content);
    }

    [Fact]
    public void LogProduce_Skips_when_TrackProduce_false()
    {
        var options = MakeOptions();
        options.TrackProduce = false;
        var tracker = new KafkaTracker(options);
        var op = new KafkaOperationInfo(KafkaOperation.ProduceAsync, "orders-topic");

        tracker.LogProduce(op, "hello");

        Assert.Empty(GetLogsFromThisTest());
    }

    [Fact]
    public void LogProduce_Skips_when_no_test_info()
    {
        var options = MakeOptions();
        options.CurrentTestInfoFetcher = null;
        var tracker = new KafkaTracker(options);
        var op = new KafkaOperationInfo(KafkaOperation.ProduceAsync, "orders-topic");

        tracker.LogProduce(op, "hello");

        Assert.Empty(GetLogsFromThisTest());
    }

    [Fact]
    public void LogProduce_Uses_correct_service_and_caller()
    {
        var tracker = new KafkaTracker(MakeOptions(callerName: "MyApi", serviceName: "OrderEvents"));
        var op = new KafkaOperationInfo(KafkaOperation.ProduceAsync, "orders-topic");

        tracker.LogProduce(op, "hello");

        var log = GetLogsFromThisTest().First();
        Assert.Equal("OrderEvents", log.ServiceName);
        Assert.Equal("MyApi", log.CallerName);
    }

    [Fact]
    public void LogProduce_Uses_detailed_label()
    {
        var tracker = new KafkaTracker(MakeOptions(KafkaTrackingVerbosity.Detailed));
        var op = new KafkaOperationInfo(KafkaOperation.ProduceAsync, "orders-topic");

        tracker.LogProduce(op, "hello");

        var log = GetLogsFromThisTest().First();
        Assert.Equal("Produce → orders-topic", log.Method.Value?.ToString());
    }

    [Fact]
    public void LogProduce_Uses_detailed_uri()
    {
        var tracker = new KafkaTracker(MakeOptions(KafkaTrackingVerbosity.Detailed));
        var op = new KafkaOperationInfo(KafkaOperation.ProduceAsync, "orders-topic");

        tracker.LogProduce(op, "hello");

        var log = GetLogsFromThisTest().First();
        Assert.Equal("kafka:///orders-topic", log.Uri.ToString());
    }

    // ─── LogConsume ─────────────────────────────────────────

    [Fact]
    public void LogConsume_Logs_request_and_response()
    {
        var tracker = new KafkaTracker(MakeOptions());
        var op = new KafkaOperationInfo(KafkaOperation.Consume, "orders-topic", 0, 42);

        tracker.LogConsume(op, "message body");

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
    }

    [Fact]
    public void LogConsume_Swaps_caller_and_service()
    {
        var tracker = new KafkaTracker(MakeOptions(callerName: "MyApi", serviceName: "Kafka"));
        var op = new KafkaOperationInfo(KafkaOperation.Consume, "orders-topic");

        tracker.LogConsume(op, "body");

        var log = GetLogsFromThisTest().First();
        Assert.Equal("MyApi", log.ServiceName);
        Assert.Equal("Kafka", log.CallerName);
    }

    [Fact]
    public void LogConsume_Skips_when_TrackConsume_false()
    {
        var options = MakeOptions();
        options.TrackConsume = false;
        var tracker = new KafkaTracker(options);
        var op = new KafkaOperationInfo(KafkaOperation.Consume, "orders-topic");

        tracker.LogConsume(op, "body");

        Assert.Empty(GetLogsFromThisTest());
    }

    // ─── LogSubscribe ───────────────────────────────────────

    [Fact]
    public void LogSubscribe_Skips_when_TrackSubscribe_false()
    {
        var options = MakeOptions();
        options.TrackSubscribe = false; // default
        var tracker = new KafkaTracker(options);
        var op = new KafkaOperationInfo(KafkaOperation.Subscribe, "orders-topic");

        tracker.LogSubscribe(op);

        Assert.Empty(GetLogsFromThisTest());
    }

    [Fact]
    public void LogSubscribe_Logs_when_TrackSubscribe_true()
    {
        var options = MakeOptions();
        options.TrackSubscribe = true;
        var tracker = new KafkaTracker(options);
        var op = new KafkaOperationInfo(KafkaOperation.Subscribe, "orders-topic");

        tracker.LogSubscribe(op);

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
    }

    // ─── ITrackingComponent ─────────────────────────────────

    [Fact]
    public void Implements_ITrackingComponent()
    {
        var tracker = new KafkaTracker(MakeOptions());
        Assert.IsAssignableFrom<ITrackingComponent>(tracker);
    }

    [Fact]
    public void WasInvoked_IsFalse_BeforeAnyMessages()
    {
        var tracker = new KafkaTracker(MakeOptions());
        Assert.False(tracker.WasInvoked);
    }

    [Fact]
    public void WasInvoked_IsTrue_AfterProduce()
    {
        var tracker = new KafkaTracker(MakeOptions());
        var op = new KafkaOperationInfo(KafkaOperation.ProduceAsync, "orders-topic");

        tracker.LogProduce(op, "hello");

        Assert.True(tracker.WasInvoked);
    }

    [Fact]
    public void InvocationCount_StartsAtZero()
    {
        var tracker = new KafkaTracker(MakeOptions());
        Assert.Equal(0, tracker.InvocationCount);
    }

    [Fact]
    public void InvocationCount_IncrementsEvenWhenTrackingDisabled()
    {
        var options = MakeOptions();
        options.TrackProduce = false;
        var tracker = new KafkaTracker(options);
        var op = new KafkaOperationInfo(KafkaOperation.ProduceAsync, "orders-topic");

        tracker.LogProduce(op, "hello");

        Assert.Equal(1, tracker.InvocationCount);
    }

    [Fact]
    public void ComponentName_MatchesServiceName()
    {
        var tracker = new KafkaTracker(MakeOptions(serviceName: "OrderEvents"));
        Assert.Equal("KafkaTracker (OrderEvents)", tracker.ComponentName);
    }

    [Fact]
    public void Constructor_AutoRegistersWithTrackingComponentRegistry()
    {
        TrackingComponentRegistry.Clear();
        var tracker = new KafkaTracker(MakeOptions());

        var components = TrackingComponentRegistry.GetRegisteredComponents();
        Assert.Contains(components, c => ReferenceEquals(c, tracker));
    }

    // ─── LogCommit ──────────────────────────────────────────

    [Fact]
    public void LogCommit_Skips_when_TrackCommit_false()
    {
        var options = MakeOptions();
        options.TrackCommit = false; // default
        var tracker = new KafkaTracker(options);
        var op = new KafkaOperationInfo(KafkaOperation.Commit);

        tracker.LogCommit(op);

        Assert.Empty(GetLogsFromThisTest());
    }

    [Fact]
    public void LogCommit_Logs_when_TrackCommit_true()
    {
        var options = MakeOptions();
        options.TrackCommit = true;
        var tracker = new KafkaTracker(options);
        var op = new KafkaOperationInfo(KafkaOperation.Commit);

        tracker.LogCommit(op);

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
    }

    // ─── LogUnsubscribe ─────────────────────────────────────

    [Fact]
    public void LogUnsubscribe_Skips_when_TrackUnsubscribe_false()
    {
        var options = MakeOptions();
        options.TrackUnsubscribe = false; // default
        var tracker = new KafkaTracker(options);
        var op = new KafkaOperationInfo(KafkaOperation.Unsubscribe);

        tracker.LogUnsubscribe(op);

        Assert.Empty(GetLogsFromThisTest());
    }

    [Fact]
    public void LogUnsubscribe_Logs_when_TrackUnsubscribe_true()
    {
        var options = MakeOptions();
        options.TrackUnsubscribe = true;
        var tracker = new KafkaTracker(options);
        var op = new KafkaOperationInfo(KafkaOperation.Unsubscribe);

        tracker.LogUnsubscribe(op);

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
    }

    // ─── LogFlush ───────────────────────────────────────────

    [Fact]
    public void LogFlush_Skips_when_TrackFlush_false()
    {
        var options = MakeOptions();
        options.TrackFlush = false; // default
        var tracker = new KafkaTracker(options);
        var op = new KafkaOperationInfo(KafkaOperation.Flush);

        tracker.LogFlush(op);

        Assert.Empty(GetLogsFromThisTest());
    }

    [Fact]
    public void LogFlush_Logs_when_TrackFlush_true()
    {
        var options = MakeOptions();
        options.TrackFlush = true;
        var tracker = new KafkaTracker(options);
        var op = new KafkaOperationInfo(KafkaOperation.Flush);

        tracker.LogFlush(op);

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
    }

    // ─── LogTransaction ─────────────────────────────────────

    [Fact]
    public void LogTransaction_Skips_when_TrackTransactions_false()
    {
        var options = MakeOptions();
        options.TrackTransactions = false; // default
        var tracker = new KafkaTracker(options);
        var op = new KafkaOperationInfo(KafkaOperation.InitTransactions);

        tracker.LogTransaction(op);

        Assert.Empty(GetLogsFromThisTest());
    }

    [Fact]
    public void LogTransaction_Logs_when_TrackTransactions_true()
    {
        var options = MakeOptions();
        options.TrackTransactions = true;
        var tracker = new KafkaTracker(options);
        var op = new KafkaOperationInfo(KafkaOperation.BeginTransaction);

        tracker.LogTransaction(op);

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
    }

    [Fact]
    public void LogTransaction_Uses_event_metatype()
    {
        var options = MakeOptions();
        options.TrackTransactions = true;
        var tracker = new KafkaTracker(options);
        var op = new KafkaOperationInfo(KafkaOperation.CommitTransaction);

        tracker.LogTransaction(op);

        var log = GetLogsFromThisTest().First();
        Assert.Equal(RequestResponseMetaType.Event, log.MetaType);
    }

    // ─── LogConsume content ─────────────────────────────────

    [Fact]
    public void LogConsume_Includes_content()
    {
        var tracker = new KafkaTracker(MakeOptions());
        var op = new KafkaOperationInfo(KafkaOperation.Consume, "orders-topic", 0, 42);

        tracker.LogConsume(op, "Key: k1, Value: v1");

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains("k1", log.Content);
    }

    [Fact]
    public void LogConsume_Omits_content_in_Summarised()
    {
        var tracker = new KafkaTracker(MakeOptions(KafkaTrackingVerbosity.Summarised));
        var op = new KafkaOperationInfo(KafkaOperation.Consume, "orders-topic");

        tracker.LogConsume(op, "Key: k1, Value: v1");

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Null(log.Content);
    }

    // ─── HttpContextAccessor ────────────────────────────────

    [Fact]
    public void Tracker_reads_HttpContextAccessor_from_options_when_not_passed_directly()
    {
        var headerTestId = Guid.NewGuid().ToString();
        var options = MakeOptions();
        options.HttpContextAccessor = RequestHeaderAccessor.For("Header Test", headerTestId);
        var tracker = new KafkaTracker(options);

        tracker.LogProduce(new KafkaOperationInfo(KafkaOperation.ProduceAsync, "orders-topic"), "hello");

        // The request's headers outrank the fetcher, which answers this class's own test.
        var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == headerTestId).ToArray();
        Assert.Equal(2, logs.Length);
        Assert.All(logs, l => Assert.Equal(AttributionSource.RequestHeader, l.AttributionSource));
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
        var tracker = new KafkaTracker(options, RequestHeaderAccessor.For("Explicit Test", explicitTestId));

        tracker.LogProduce(new KafkaOperationInfo(KafkaOperation.ProduceAsync, "orders-topic"), "hello");

        Assert.Contains(RequestResponseLogger.RequestAndResponseLogs, l => l.TestId == explicitTestId);
        Assert.DoesNotContain(RequestResponseLogger.RequestAndResponseLogs, l => l.TestId == optionsTestId);
    }

    [Fact]
    public void Tracker_has_no_accessor_when_neither_options_nor_parameter()
    {
        Assert.False(new KafkaTracker(MakeOptions()).HasHttpContextAccessor);
    }
}
