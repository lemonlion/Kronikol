using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Kronikol.Constants;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

[Collection("DiagramsFetcher")]
public class MessageTrackerTests
{
    private static RequestResponseLog[] GetLogsById(Guid requestResponseId)
    {
        return RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.RequestResponseId == requestResponseId)
            .ToArray();
    }

    private static IHttpContextAccessor CreateHttpContextAccessor(
        string testName = "My Test",
        string testId = "test-id-1",
        string traceId = "7c9e6679-7425-40de-944b-e07fc1f90ae7")
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[TestTrackingHttpHeaders.CurrentTestNameHeader] = testName;
        context.Request.Headers[TestTrackingHttpHeaders.CurrentTestIdHeader] = testId;
        context.Request.Headers[TestTrackingHttpHeaders.TraceIdHeader] = traceId;

        return new HttpContextAccessor { HttpContext = context };
    }

    private static MessageTracker CreateTracker(
        IHttpContextAccessor? accessor = null,
        string callingServiceName = "MyService",
        Func<(string Name, string Id)>? testInfoFallback = null)
    {
        return new MessageTracker(accessor ?? CreateHttpContextAccessor(), callingServiceName, testInfoFallback: testInfoFallback);
    }

    // ─── TrackMessageRequest ────────────────────────────────────

    [Fact]
    public void TrackMessageRequest_logs_a_request_entry()
    {
        var tracker = CreateTracker();

        var id = tracker.TrackMessageRequest("Kafka", "OrderService", new Uri("kafka://orders-topic"), new { Id = 1 });

        var logs = GetLogsById(id);
        Assert.Single(logs);
        Assert.Equal(RequestResponseType.Request, logs[0].Type);
    }

    [Fact]
    public void TrackMessageRequest_returns_a_non_empty_correlation_id()
    {
        var tracker = CreateTracker();

        var id = tracker.TrackMessageRequest("Kafka", "OrderService", new Uri("kafka://orders-topic"), new { Id = 1 });

        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public void TrackMessageRequest_sets_protocol_as_method()
    {
        var tracker = CreateTracker();

        var id = tracker.TrackMessageRequest("EventGrid", "NotificationService", new Uri("eventgrid://events"), new { Msg = "hi" });

        var log = GetLogsById(id).Single();
        Assert.Equal("EventGrid", log.Method.Value);
    }

    [Fact]
    public void TrackMessageRequest_sets_destination_as_service_name()
    {
        var tracker = CreateTracker();

        var id = tracker.TrackMessageRequest("Kafka", "OrderService", new Uri("kafka://orders"), new { });

        var log = GetLogsById(id).Single();
        Assert.Equal("OrderService", log.ServiceName);
    }

    [Fact]
    public void TrackMessageRequest_sets_calling_service_name()
    {
        var tracker = CreateTracker(callingServiceName: "PublisherApp");

        var id = tracker.TrackMessageRequest("Kafka", "OrderService", new Uri("kafka://orders"), new { });

        var log = GetLogsById(id).Single();
        Assert.Equal("PublisherApp", log.CallerName);
    }

    [Fact]
    public void TrackMessageRequest_serialises_payload_as_content()
    {
        var tracker = CreateTracker();

        var id = tracker.TrackMessageRequest("Kafka", "OrderService", new Uri("kafka://orders"), new { Name = "Widget" });

        var log = GetLogsById(id).Single();
        Assert.Contains("Widget", log.Content);
    }

    [Fact]
    public void TrackMessageRequest_sets_uri()
    {
        var uri = new Uri("kafka://my-topic");
        var tracker = CreateTracker();

        var id = tracker.TrackMessageRequest("Kafka", "OrderService", uri, new { });

        var log = GetLogsById(id).Single();
        Assert.Equal(uri, log.Uri);
    }

    [Fact]
    public void TrackMessageRequest_sets_meta_type_to_event()
    {
        var tracker = CreateTracker();

        var id = tracker.TrackMessageRequest("Kafka", "OrderService", new Uri("kafka://orders"), new { });

        var log = GetLogsById(id).Single();
        Assert.Equal(RequestResponseMetaType.Event, log.MetaType);
    }

    [Fact]
    public void TrackMessageRequest_reads_test_info_from_http_context()
    {
        var accessor = CreateHttpContextAccessor("SpecificTest", "id-42", "7c9e6679-7425-40de-944b-e07fc1f90ae7");
        var tracker = CreateTracker(accessor);

        var id = tracker.TrackMessageRequest("SNS", "NotifySvc", new Uri("sns://topic"), new { });

        var log = GetLogsById(id).Single();
        Assert.Equal("SpecificTest", log.TestName);
        Assert.Equal("id-42", log.TestId);
        Assert.Equal(Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7"), log.TraceId);
    }

    // ─── TrackMessageResponse ───────────────────────────────────

    [Fact]
    public void TrackMessageResponse_logs_a_response_entry()
    {
        var tracker = CreateTracker();
        var correlationId = tracker.TrackMessageRequest("Kafka", "OrderService", new Uri("kafka://orders"), new { });

        tracker.TrackMessageResponse("Kafka", "OrderService", new Uri("kafka://orders"), correlationId);

        var logs = GetLogsById(correlationId);
        Assert.Equal(2, logs.Length);
        Assert.Equal(RequestResponseType.Response, logs[1].Type);
    }

    [Fact]
    public void TrackMessageResponse_uses_same_correlation_id_as_request()
    {
        var tracker = CreateTracker();
        var correlationId = tracker.TrackMessageRequest("Kafka", "OrderService", new Uri("kafka://orders"), new { });

        tracker.TrackMessageResponse("Kafka", "OrderService", new Uri("kafka://orders"), correlationId);

        var logs = GetLogsById(correlationId);
        Assert.Equal(logs[0].RequestResponseId, logs[1].RequestResponseId);
    }

    [Fact]
    public void TrackMessageResponse_serialises_response_payload_when_provided()
    {
        var tracker = CreateTracker();
        var id = tracker.TrackMessageRequest("MQ", "Worker", new Uri("mq://queue"), new { });

        tracker.TrackMessageResponse("MQ", "Worker", new Uri("mq://queue"), id, new { Ack = true });

        var responseLog = GetLogsById(id).Last();
        Assert.Contains("true", responseLog.Content);
    }

    [Fact]
    public void TrackMessageResponse_sets_empty_content_when_no_response_payload()
    {
        var tracker = CreateTracker();
        var id = tracker.TrackMessageRequest("MQ", "Worker", new Uri("mq://queue"), new { });

        tracker.TrackMessageResponse("MQ", "Worker", new Uri("mq://queue"), id);

        var responseLog = GetLogsById(id).Last();
        Assert.Equal(string.Empty, responseLog.Content);
    }

    [Fact]
    public void TrackMessageResponse_sets_meta_type_to_event()
    {
        var tracker = CreateTracker();
        var id = tracker.TrackMessageRequest("Kafka", "Svc", new Uri("kafka://t"), new { });

        tracker.TrackMessageResponse("Kafka", "Svc", new Uri("kafka://t"), id);

        var responseLog = GetLogsById(id).Last();
        Assert.Equal(RequestResponseMetaType.Event, responseLog.MetaType);
    }

    // ─── Fallback test info ─────────────────────────────────────

    [Fact]
    public void TrackMessageRequest_uses_fallback_when_no_http_context()
    {
        var accessor = new HttpContextAccessor { HttpContext = null };
        var tracker = CreateTracker(accessor, testInfoFallback: () => ("FallbackTest", "fallback-id-1"));

        var id = tracker.TrackMessageRequest("ServiceBus", "Queue", new Uri("servicebus://queue"), new { Msg = "hello" });

        var log = GetLogsById(id).Single();
        Assert.Equal("FallbackTest", log.TestName);
        Assert.Equal("fallback-id-1", log.TestId);
    }

    [Fact]
    public void TrackMessageResponse_uses_fallback_when_no_http_context()
    {
        var accessor = new HttpContextAccessor { HttpContext = null };
        var tracker = CreateTracker(accessor, testInfoFallback: () => ("FallbackTest", "fallback-id-2"));

        var id = tracker.TrackMessageRequest("ServiceBus", "Queue", new Uri("servicebus://queue"), new { });
        tracker.TrackMessageResponse("ServiceBus", "Queue", new Uri("servicebus://queue"), id);

        var logs = GetLogsById(id);
        Assert.Equal(2, logs.Length);
        Assert.All(logs, l => Assert.Equal("FallbackTest", l.TestName));
        Assert.All(logs, l => Assert.Equal("fallback-id-2", l.TestId));
    }

    [Fact]
    public void TrackMessageRequest_prefers_http_context_over_fallback()
    {
        var accessor = CreateHttpContextAccessor("HttpTest", "http-id", "7c9e6679-7425-40de-944b-e07fc1f90ae7");
        var tracker = CreateTracker(accessor, testInfoFallback: () => ("FallbackTest", "fallback-id"));

        var id = tracker.TrackMessageRequest("ServiceBus", "Queue", new Uri("servicebus://queue"), new { });

        var log = GetLogsById(id).Single();
        Assert.Equal("HttpTest", log.TestName);
        Assert.Equal("http-id", log.TestId);
    }

    [Fact]
    public void TrackMessageRequest_returns_empty_guid_when_no_http_context_and_no_fallback()
    {
        var accessor = new HttpContextAccessor { HttpContext = null };
        var tracker = CreateTracker(accessor);

        var id = tracker.TrackMessageRequest("ServiceBus", "Queue", new Uri("servicebus://queue"), new { });

        Assert.Equal(Guid.Empty, id);
    }

    // ─── Options-based constructor ──────────────────────────────

    private static MessageTracker CreateOptionsTracker(
        MessageTrackerOptions? options = null)
    {
        return new MessageTracker(options ?? new MessageTrackerOptions
        {
            CallerName = "TestCaller",
            CurrentTestInfoFetcher = () => ("Options Test", "opts-id-1")
        });
    }

    [Fact]
    public void Options_constructor_creates_functional_tracker()
    {
        var tracker = CreateOptionsTracker();

        var id = tracker.TrackMessageRequest("Kafka", "OrderService", new Uri("kafka://orders"), new { Id = 1 });

        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public void Options_constructor_uses_CurrentTestInfoFetcher()
    {
        var testId = Guid.NewGuid().ToString();
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "MySvc",
            CurrentTestInfoFetcher = () => ("OptionTest", testId)
        });

        var id = tracker.TrackMessageRequest("Kafka", "Svc", new Uri("kafka://t"), new { });

        var log = RequestResponseLogger.RequestAndResponseLogs
            .First(l => l.RequestResponseId == id);
        Assert.Equal("OptionTest", log.TestName);
        Assert.Equal(testId, log.TestId);
    }

    [Fact]
    public void Options_constructor_returns_empty_guid_when_no_fetcher()
    {
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "MySvc",
            CurrentTestInfoFetcher = null
        });

        var id = tracker.TrackMessageRequest("Kafka", "Svc", new Uri("kafka://t"), new { });

        Assert.Equal(Guid.Empty, id);
    }

    [Fact]
    public void Options_constructor_uses_CallerName()
    {
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "BillingService",
            CurrentTestInfoFetcher = () => ("T", "id")
        });

        var id = tracker.TrackMessageRequest("SB", "Queue", new Uri("sb://q"), new { });

        var log = RequestResponseLogger.RequestAndResponseLogs
            .First(l => l.RequestResponseId == id);
        Assert.Equal("BillingService", log.CallerName);
    }

    // ─── ITrackingComponent ─────────────────────────────────────

    [Fact]
    public void Implements_ITrackingComponent()
    {
        var tracker = CreateOptionsTracker();
        Assert.IsAssignableFrom<ITrackingComponent>(tracker);
    }

    [Fact]
    public void WasInvoked_IsFalse_BeforeAnyTracking()
    {
        var tracker = CreateOptionsTracker();
        Assert.False(tracker.WasInvoked);
    }

    [Fact]
    public void WasInvoked_IsTrue_AfterTrackMessageRequest()
    {
        var tracker = CreateOptionsTracker();

        tracker.TrackMessageRequest("Kafka", "Svc", new Uri("kafka://t"), new { });

        Assert.True(tracker.WasInvoked);
    }

    [Fact]
    public void InvocationCount_StartsAtZero()
    {
        var tracker = CreateOptionsTracker();
        Assert.Equal(0, tracker.InvocationCount);
    }

    [Fact]
    public void InvocationCount_IncreasesWithEachTrack()
    {
        var tracker = CreateOptionsTracker();

        tracker.TrackMessageRequest("Kafka", "A", new Uri("kafka://a"), new { });
        tracker.TrackMessageRequest("Kafka", "B", new Uri("kafka://b"), new { });
        tracker.TrackMessageRequest("Kafka", "C", new Uri("kafka://c"), new { });

        Assert.Equal(3, tracker.InvocationCount);
    }

    [Fact]
    public void ComponentName_UsesDefaultServiceName()
    {
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CurrentTestInfoFetcher = () => ("T", "id")
        });
        Assert.Equal("MessageTracker (MessageBus)", tracker.ComponentName);
    }

    [Fact]
    public void ComponentName_UsesCustomServiceName()
    {
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            ServiceName = "Custom Bus",
            CurrentTestInfoFetcher = () => ("T", "id")
        });
        Assert.Equal("MessageTracker (Custom Bus)", tracker.ComponentName);
    }

    [Fact]
    public void Constructor_AutoRegistersWithTrackingComponentRegistry()
    {
        TrackingComponentRegistry.Clear();
        var tracker = CreateOptionsTracker();

        var components = TrackingComponentRegistry.GetRegisteredComponents();
        Assert.Contains(components, c => ReferenceEquals(c, tracker));
    }

    // ─── Legacy constructor still works as ITrackingComponent ───

    [Fact]
    public void Legacy_constructor_also_implements_ITrackingComponent()
    {
        var tracker = CreateTracker();
        Assert.IsAssignableFrom<ITrackingComponent>(tracker);
    }

    [Fact]
    public void Legacy_constructor_ComponentName_uses_default()
    {
        var tracker = CreateTracker();
        Assert.Equal("MessageTracker (MessageBus)", tracker.ComponentName);
    }

    [Fact]
    public void Legacy_constructor_registers_with_TrackingComponentRegistry()
    {
        TrackingComponentRegistry.Clear();
        var tracker = CreateTracker();

        var components = TrackingComponentRegistry.GetRegisteredComponents();
        Assert.Contains(components, c => ReferenceEquals(c, tracker));
    }

    // ─── Verbosity ──────────────────────────────────────────────

    [Fact]
    public void Summarised_omits_request_content()
    {
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "Svc",
            Verbosity = MessageTrackerVerbosity.Summarised,
            CurrentTestInfoFetcher = () => ("T", "id")
        });

        var id = tracker.TrackMessageRequest("Kafka", "Dest", new Uri("kafka://t"), new { Secret = "data" });

        var log = RequestResponseLogger.RequestAndResponseLogs
            .First(l => l.RequestResponseId == id && l.Type == RequestResponseType.Request);
        Assert.Null(log.Content);
    }

    [Fact]
    public void Summarised_omits_response_content()
    {
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "Svc",
            Verbosity = MessageTrackerVerbosity.Summarised,
            CurrentTestInfoFetcher = () => ("T", "id")
        });

        var id = tracker.TrackMessageRequest("Kafka", "Dest", new Uri("kafka://t"), new { });
        tracker.TrackMessageResponse("Kafka", "Dest", new Uri("kafka://t"), id, new { Ack = true });

        var log = RequestResponseLogger.RequestAndResponseLogs
            .First(l => l.RequestResponseId == id && l.Type == RequestResponseType.Response);
        Assert.Null(log.Content);
    }

    [Fact]
    public void Detailed_includes_request_content()
    {
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "Svc",
            Verbosity = MessageTrackerVerbosity.Detailed,
            CurrentTestInfoFetcher = () => ("T", "id")
        });

        var id = tracker.TrackMessageRequest("Kafka", "Dest", new Uri("kafka://t"), new { Data = "visible" });

        var log = RequestResponseLogger.RequestAndResponseLogs
            .First(l => l.RequestResponseId == id && l.Type == RequestResponseType.Request);
        Assert.Contains("visible", log.Content!);
    }

    [Fact]
    public void Raw_includes_request_content()
    {
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "Svc",
            Verbosity = MessageTrackerVerbosity.Raw,
            CurrentTestInfoFetcher = () => ("T", "id")
        });

        var id = tracker.TrackMessageRequest("Kafka", "Dest", new Uri("kafka://t"), new { Data = "raw-data" });

        var log = RequestResponseLogger.RequestAndResponseLogs
            .First(l => l.RequestResponseId == id && l.Type == RequestResponseType.Request);
        Assert.Contains("raw-data", log.Content!);
    }

    [Fact]
    public void Legacy_constructor_always_includes_content()
    {
        var tracker = CreateTracker();

        var id = tracker.TrackMessageRequest("Kafka", "Dest", new Uri("kafka://t"), new { Data = "legacy-data" });

        var log = RequestResponseLogger.RequestAndResponseLogs
            .First(l => l.RequestResponseId == id && l.Type == RequestResponseType.Request);
        Assert.Contains("legacy-data", log.Content!);
    }

    // ─── TrackSendEvent (fire-and-forget one-shot) ──────────────

    [Fact]
    public void TrackSendEvent_logs_request_and_response_pair()
    {
        var tracker = CreateOptionsTracker();
        var testId = "opts-id-1";

        tracker.TrackSendEvent("Kafka", "OrderService", new Uri("kafka://orders"), new { Id = 1 });

        var logs = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId)
            .ToArray();
        var pair = logs.Where(l => l.Method.Value?.ToString() == "Kafka").ToArray();
        Assert.True(pair.Length >= 2);
        Assert.Contains(pair, l => l.Type == RequestResponseType.Request);
        Assert.Contains(pair, l => l.Type == RequestResponseType.Response);
    }

    [Fact]
    public void TrackSendEvent_shares_same_requestResponseId()
    {
        var testId = Guid.NewGuid().ToString();
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "Svc",
            CurrentTestInfoFetcher = () => ("T", testId)
        });

        tracker.TrackSendEvent("SB", "Queue", new Uri("sb://q"), new { Msg = "hi" });

        var logs = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId)
            .ToArray();
        Assert.Equal(2, logs.Length);
        Assert.Equal(logs[0].RequestResponseId, logs[1].RequestResponseId);
    }

    [Fact]
    public void TrackSendEvent_sets_meta_type_to_event()
    {
        var testId = Guid.NewGuid().ToString();
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "Svc",
            CurrentTestInfoFetcher = () => ("T", testId)
        });

        tracker.TrackSendEvent("SB", "Queue", new Uri("sb://q"), new { });

        var logs = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId)
            .ToArray();
        Assert.All(logs, l => Assert.Equal(RequestResponseMetaType.Event, l.MetaType));
    }

    [Fact]
    public void TrackSendEvent_increments_invocation_count()
    {
        var tracker = CreateOptionsTracker();
        var before = tracker.InvocationCount;

        tracker.TrackSendEvent("Kafka", "Svc", new Uri("kafka://t"), new { });

        Assert.Equal(before + 1, tracker.InvocationCount);
    }

    [Fact]
    public void TrackSendEvent_respects_summarised_verbosity()
    {
        var testId = Guid.NewGuid().ToString();
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "Svc",
            Verbosity = MessageTrackerVerbosity.Summarised,
            CurrentTestInfoFetcher = () => ("T", testId)
        });

        tracker.TrackSendEvent("Kafka", "Dest", new Uri("kafka://t"), new { Secret = "hidden" });

        var logs = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId)
            .ToArray();
        Assert.All(logs, l => Assert.Null(l.Content));
    }

    [Fact]
    public void TrackSendEvent_does_nothing_when_no_test_info()
    {
        // If the framework's execution context or a parallel test has set identity,
        // this test's premise (no test info available anywhere) is invalid — skip.
        if (TestIdentityScope.Current is not null || TestIdentityScope.GlobalFallback is not null)
            return;

        var marker = $"NoLog-SendEvent-{Guid.NewGuid():N}";
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = marker,
            CurrentTestInfoFetcher = null
        });

        tracker.TrackSendEvent("Kafka", "Dest", new Uri("kafka://t"), new { });

        var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.CallerName == marker).ToArray();
        Assert.Empty(logs);
    }

    // ─── ServiceName in options ─────────────────────────────────

    [Fact]
    public void Options_ServiceName_used_as_destination_in_logs()
    {
        var testId = Guid.NewGuid().ToString();
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            ServiceName = "My Custom Bus",
            CallerName = "OrderService",
            CurrentTestInfoFetcher = () => ("T", testId)
        });

        tracker.TrackSendEvent("Send", "My Custom Bus", new Uri("bus://q"), new { });

        var log = RequestResponseLogger.RequestAndResponseLogs
            .First(l => l.TestId == testId && l.Type == RequestResponseType.Request);
        Assert.Equal("My Custom Bus", log.ServiceName);
    }

    // ─── UseHttpContextCorrelation ──────────────────────────────

    [Fact]
    public void Options_with_UseHttpContextCorrelation_reads_headers_from_HttpContext()
    {
        var accessor = CreateHttpContextAccessor(testName: "HeaderTest", testId: "header-id-1");
        var options = new MessageTrackerOptions
        {
            CallerName = "MySvc",
            UseHttpContextCorrelation = true,
            CurrentTestInfoFetcher = () => ("FallbackTest", "fallback-id")
        };
        var tracker = new MessageTracker(options, accessor);

        var id = tracker.TrackMessageRequest("Kafka", "Svc", new Uri("kafka://t"), new { });

        var log = RequestResponseLogger.RequestAndResponseLogs.First(l => l.RequestResponseId == id);
        Assert.Equal("HeaderTest", log.TestName);
        Assert.Equal("header-id-1", log.TestId);
    }

    [Fact]
    public void Options_with_UseHttpContextCorrelation_falls_back_to_fetcher_when_no_HttpContext()
    {
        var accessor = new HttpContextAccessor { HttpContext = null };
        var testId = Guid.NewGuid().ToString();
        var options = new MessageTrackerOptions
        {
            CallerName = "MySvc",
            UseHttpContextCorrelation = true,
            CurrentTestInfoFetcher = () => ("FallbackTest", testId)
        };
        var tracker = new MessageTracker(options, accessor);

        var id = tracker.TrackMessageRequest("Kafka", "Svc", new Uri("kafka://t"), new { });

        var log = RequestResponseLogger.RequestAndResponseLogs.First(l => l.RequestResponseId == id);
        Assert.Equal("FallbackTest", log.TestName);
        Assert.Equal(testId, log.TestId);
    }

    [Fact]
    public void Options_without_UseHttpContextCorrelation_ignores_HttpContext_headers()
    {
        var accessor = CreateHttpContextAccessor(testName: "HeaderTest", testId: "header-id");
        var testId = Guid.NewGuid().ToString();
        var options = new MessageTrackerOptions
        {
            CallerName = "MySvc",
            UseHttpContextCorrelation = false,
            CurrentTestInfoFetcher = () => ("FetcherTest", testId)
        };
        var tracker = new MessageTracker(options, accessor);

        var id = tracker.TrackMessageRequest("Kafka", "Svc", new Uri("kafka://t"), new { });

        var log = RequestResponseLogger.RequestAndResponseLogs.First(l => l.RequestResponseId == id);
        Assert.Equal("FetcherTest", log.TestName);
        Assert.Equal(testId, log.TestId);
    }

    // ─── DependencyCategory ─────────────────────────────────────

    [Fact]
    public void TrackMessageRequest_sets_DependencyCategory_to_MessageQueue_by_default()
    {
        var tracker = CreateOptionsTracker();

        var id = tracker.TrackMessageRequest("Kafka", "Svc", new Uri("kafka://t"), new { });

        var log = GetLogsById(id).Single();
        Assert.Equal("MessageQueue", log.DependencyCategory);
    }

    [Fact]
    public void TrackMessageResponse_sets_DependencyCategory_to_MessageQueue_by_default()
    {
        var tracker = CreateOptionsTracker();
        var id = tracker.TrackMessageRequest("Kafka", "Svc", new Uri("kafka://t"), new { });

        tracker.TrackMessageResponse("Kafka", "Svc", new Uri("kafka://t"), id);

        var responseLog = GetLogsById(id).Last();
        Assert.Equal("MessageQueue", responseLog.DependencyCategory);
    }

    [Fact]
    public void TrackMessageRequest_uses_custom_DependencyCategory_from_options()
    {
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "Svc",
            DependencyCategory = "CustomCategory",
            CurrentTestInfoFetcher = () => ("T", "id")
        });

        var id = tracker.TrackMessageRequest("Kafka", "Svc", new Uri("kafka://t"), new { });

        var log = GetLogsById(id).Single();
        Assert.Equal("CustomCategory", log.DependencyCategory);
    }

    [Fact]
    public void Legacy_constructor_sets_DependencyCategory_to_MessageQueue()
    {
        var tracker = CreateTracker();

        var id = tracker.TrackMessageRequest("Kafka", "Svc", new Uri("kafka://t"), new { });

        var log = GetLogsById(id).Single();
        Assert.Equal("MessageQueue", log.DependencyCategory);
    }

    [Fact]
    public void TrackSendEvent_sets_DependencyCategory_to_MessageQueue()
    {
        var testId = Guid.NewGuid().ToString();
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "Svc",
            CurrentTestInfoFetcher = () => ("T", testId)
        });

        tracker.TrackSendEvent("SB", "Queue", new Uri("sb://q"), new { });

        var logs = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId)
            .ToArray();
        Assert.All(logs, l => Assert.Equal("MessageQueue", l.DependencyCategory));
    }

    // ─── Throwing delegate safety ───────────────────────────────

    [Fact]
    public void TrackMessageRequest_returns_empty_guid_when_fetcher_throws()
    {
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "Svc",
            CurrentTestInfoFetcher = () => throw new NullReferenceException("TestContext.Current.Test is null")
        });

        var id = tracker.TrackMessageRequest("Kafka", "Svc", new Uri("kafka://t"), new { });

        Assert.Equal(Guid.Empty, id);
    }

    [Fact]
    public void TrackMessageRequest_does_not_log_when_fetcher_throws()
    {
        var marker = $"NoLog-Request-{Guid.NewGuid():N}";
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = marker,
            CurrentTestInfoFetcher = () => throw new NullReferenceException("TestContext.Current.Test is null")
        });

        tracker.TrackMessageRequest("Kafka", "Svc", new Uri("kafka://t"), new { });

        var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.CallerName == marker).ToArray();
        Assert.Empty(logs);
    }

    [Fact]
    public void TrackSendEvent_does_not_throw_when_fetcher_throws()
    {
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "Svc",
            CurrentTestInfoFetcher = () => throw new InvalidOperationException("No scenario context")
        });

        var exception = Record.Exception(() =>
            tracker.TrackSendEvent("Kafka", "Dest", new Uri("kafka://t"), new { }));

        Assert.Null(exception);
    }

    [Fact]
    public void TrackMessageRequest_with_UseHttpContextCorrelation_returns_empty_guid_when_no_context_and_fetcher_throws()
    {
        var accessor = new HttpContextAccessor { HttpContext = null };
        var tracker = new MessageTracker(
            new MessageTrackerOptions
            {
                CallerName = "Svc",
                UseHttpContextCorrelation = true,
                CurrentTestInfoFetcher = () => throw new NullReferenceException("TestContext.Current.Test is null")
            },
            accessor);

        var id = tracker.TrackMessageRequest("Kafka", "Svc", new Uri("kafka://t"), new { });

        Assert.Equal(Guid.Empty, id);
    }

    [Fact]
    public void Legacy_constructor_returns_empty_guid_when_no_http_context_and_fallback_throws()
    {
        // If the framework's execution context or a parallel test has set identity,
        // this test's premise (no test info available anywhere) is invalid — skip.
        if (TestIdentityScope.Current is not null || TestIdentityScope.GlobalFallback is not null)
            return;

        var accessor = new HttpContextAccessor { HttpContext = null };
        var tracker = new MessageTracker(
            accessor,
            "Svc",
            testInfoFallback: () => throw new NullReferenceException("TestContext.Current.Test is null"));

        var id = tracker.TrackMessageRequest("Kafka", "Svc", new Uri("kafka://t"), new { });

        Assert.Equal(Guid.Empty, id);
    }

    // ─── TrackConsumeEvent ──────────────────────────────────────

    [Fact]
    public void TrackConsumeEvent_logs_request_and_response_pair()
    {
        var testId = Guid.NewGuid().ToString();
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "Kafka Broker",
            ServiceName = "Breakfast Provider",
            CurrentTestInfoFetcher = () => ("T", testId)
        });

        tracker.TrackConsumeEvent("Consume (Kafka)", "Breakfast Provider", new Uri("kafka:///recipe_logs"), new { OrderId = "123" });

        var logs = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId)
            .ToArray();
        Assert.Equal(2, logs.Length);
        Assert.Contains(logs, l => l.Type == RequestResponseType.Request);
        Assert.Contains(logs, l => l.Type == RequestResponseType.Response);
    }

    [Fact]
    public void TrackConsumeEvent_sets_caller_as_arrow_source()
    {
        var testId = Guid.NewGuid().ToString();
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "Kafka Broker",
            ServiceName = "Breakfast Provider",
            CurrentTestInfoFetcher = () => ("T", testId)
        });

        tracker.TrackConsumeEvent("Consume (Kafka)", "Breakfast Provider", new Uri("kafka:///t"), new { });

        var request = RequestResponseLogger.RequestAndResponseLogs
            .First(l => l.TestId == testId && l.Type == RequestResponseType.Request);
        Assert.Equal("Kafka Broker", request.CallerName);
    }

    [Fact]
    public void TrackConsumeEvent_sets_consumer_as_destination()
    {
        var testId = Guid.NewGuid().ToString();
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "Kafka Broker",
            ServiceName = "Breakfast Provider",
            CurrentTestInfoFetcher = () => ("T", testId)
        });

        tracker.TrackConsumeEvent("Consume (Kafka)", "Breakfast Provider", new Uri("kafka:///t"), new { });

        var request = RequestResponseLogger.RequestAndResponseLogs
            .First(l => l.TestId == testId && l.Type == RequestResponseType.Request);
        Assert.Equal("Breakfast Provider", request.ServiceName);
    }

    [Fact]
    public void TrackConsumeEvent_puts_payload_on_request_arrow()
    {
        var testId = Guid.NewGuid().ToString();
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "Kafka Broker",
            ServiceName = "Breakfast Provider",
            CurrentTestInfoFetcher = () => ("T", testId)
        });

        tracker.TrackConsumeEvent("Consume (Kafka)", "Breakfast Provider", new Uri("kafka:///t"), new { OrderId = "abc" });

        var request = RequestResponseLogger.RequestAndResponseLogs
            .First(l => l.TestId == testId && l.Type == RequestResponseType.Request);
        Assert.Contains("abc", request.Content!);
    }

    [Fact]
    public void TrackConsumeEvent_uses_Ack_as_default_response_label()
    {
        var testId = Guid.NewGuid().ToString();
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "Kafka Broker",
            ServiceName = "Breakfast Provider",
            CurrentTestInfoFetcher = () => ("T", testId)
        });

        tracker.TrackConsumeEvent("Consume (Kafka)", "Breakfast Provider", new Uri("kafka:///t"), new { });

        var response = RequestResponseLogger.RequestAndResponseLogs
            .First(l => l.TestId == testId && l.Type == RequestResponseType.Response);
        Assert.Equal("Ack", response.StatusCode!.Value);
    }

    [Fact]
    public void TrackConsumeEvent_uses_custom_ack_label()
    {
        var testId = Guid.NewGuid().ToString();
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "Kafka Broker",
            ServiceName = "Breakfast Provider",
            CurrentTestInfoFetcher = () => ("T", testId)
        });

        tracker.TrackConsumeEvent("Consume (Kafka)", "Breakfast Provider", new Uri("kafka:///t"), new { }, ackLabel: "Processed");

        var response = RequestResponseLogger.RequestAndResponseLogs
            .First(l => l.TestId == testId && l.Type == RequestResponseType.Response);
        Assert.Equal("Processed", response.StatusCode!.Value);
    }

    [Fact]
    public void TrackConsumeEvent_shares_same_requestResponseId()
    {
        var testId = Guid.NewGuid().ToString();
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "Kafka Broker",
            ServiceName = "Breakfast Provider",
            CurrentTestInfoFetcher = () => ("T", testId)
        });

        tracker.TrackConsumeEvent("Consume (Kafka)", "Breakfast Provider", new Uri("kafka:///t"), new { });

        var logs = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId)
            .ToArray();
        Assert.Equal(logs[0].RequestResponseId, logs[1].RequestResponseId);
    }

    [Fact]
    public void TrackConsumeEvent_sets_meta_type_to_event()
    {
        var testId = Guid.NewGuid().ToString();
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "Kafka Broker",
            ServiceName = "Breakfast Provider",
            CurrentTestInfoFetcher = () => ("T", testId)
        });

        tracker.TrackConsumeEvent("Consume (Kafka)", "Breakfast Provider", new Uri("kafka:///t"), new { });

        var logs = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId)
            .ToArray();
        Assert.All(logs, l => Assert.Equal(RequestResponseMetaType.Event, l.MetaType));
    }

    [Fact]
    public void TrackConsumeEvent_increments_invocation_count()
    {
        var tracker = CreateOptionsTracker();
        var before = tracker.InvocationCount;

        tracker.TrackConsumeEvent("Consume (Kafka)", "Svc", new Uri("kafka:///t"), new { });

        Assert.Equal(before + 1, tracker.InvocationCount);
    }

    [Fact]
    public void TrackConsumeEvent_respects_summarised_verbosity()
    {
        var testId = Guid.NewGuid().ToString();
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "Broker",
            Verbosity = MessageTrackerVerbosity.Summarised,
            CurrentTestInfoFetcher = () => ("T", testId)
        });

        tracker.TrackConsumeEvent("Consume", "Consumer", new Uri("kafka:///t"), new { Secret = "hidden" });

        var logs = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId)
            .ToArray();
        Assert.All(logs, l => Assert.Null(l.Content));
    }

    [Fact]
    public void TrackConsumeEvent_does_nothing_when_no_test_info()
    {
        if (TestIdentityScope.Current is not null || TestIdentityScope.GlobalFallback is not null)
            return;

        var marker = $"NoLog-Consume-{Guid.NewGuid():N}";
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = marker,
            CurrentTestInfoFetcher = null
        });

        tracker.TrackConsumeEvent("Consume", "Consumer", new Uri("kafka:///t"), new { });

        var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.CallerName == marker).ToArray();
        Assert.Empty(logs);
    }

    [Fact]
    public void TrackConsumeEvent_sets_DependencyCategory_from_options()
    {
        var testId = Guid.NewGuid().ToString();
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "Broker",
            DependencyCategory = "MessageQueue",
            CurrentTestInfoFetcher = () => ("T", testId)
        });

        tracker.TrackConsumeEvent("Consume", "Consumer", new Uri("kafka:///t"), new { });

        var logs = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId)
            .ToArray();
        Assert.All(logs, l => Assert.Equal("MessageQueue", l.DependencyCategory));
    }

    // ─── CallerDependencyCategory ───────────────────────────────

    [Fact]
    public void CallerDependencyCategory_defaults_to_null()
    {
        var options = new MessageTrackerOptions();
        Assert.Null(options.CallerDependencyCategory);
    }

    [Fact]
    public void TrackMessageRequest_sets_CallerDependencyCategory_on_log()
    {
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "Kafka Broker",
            CallerDependencyCategory = "MessageQueue",
            CurrentTestInfoFetcher = () => ("T", "id")
        });

        var id = tracker.TrackMessageRequest("Kafka", "Svc", new Uri("kafka://t"), new { });

        var log = GetLogsById(id).Single();
        Assert.Equal("MessageQueue", log.CallerDependencyCategory);
    }

    [Fact]
    public void TrackMessageResponse_sets_CallerDependencyCategory_on_log()
    {
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "Kafka Broker",
            CallerDependencyCategory = "MessageQueue",
            CurrentTestInfoFetcher = () => ("T", "id")
        });
        var id = tracker.TrackMessageRequest("Kafka", "Svc", new Uri("kafka://t"), new { });

        tracker.TrackMessageResponse("Kafka", "Svc", new Uri("kafka://t"), id);

        var responseLog = GetLogsById(id).Last();
        Assert.Equal("MessageQueue", responseLog.CallerDependencyCategory);
    }

    [Fact]
    public void TrackConsumeEvent_sets_CallerDependencyCategory_on_log()
    {
        var testId = Guid.NewGuid().ToString();
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "Kafka Broker",
            CallerDependencyCategory = "MessageQueue",
            CurrentTestInfoFetcher = () => ("T", testId)
        });

        tracker.TrackConsumeEvent("Consume", "Consumer", new Uri("kafka:///t"), new { });

        var logs = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId)
            .ToArray();
        Assert.All(logs, l => Assert.Equal("MessageQueue", l.CallerDependencyCategory));
    }

    [Fact]
    public void CallerDependencyCategory_null_by_default_on_log()
    {
        var tracker = CreateOptionsTracker();

        var id = tracker.TrackMessageRequest("Kafka", "Svc", new Uri("kafka://t"), new { });

        var log = GetLogsById(id).Single();
        Assert.Null(log.CallerDependencyCategory);
    }

    [Fact]
    public void Legacy_constructor_sets_CallerDependencyCategory_to_null_on_log()
    {
        var tracker = CreateTracker();

        var id = tracker.TrackMessageRequest("Kafka", "Svc", new Uri("kafka://t"), new { });

        var log = GetLogsById(id).Single();
        Assert.Null(log.CallerDependencyCategory);
    }

    // ─── IsCurrentRequestFromMyHost ─────────────────────────────

    [Fact]
    public void IsCurrentRequestFromMyHost_returns_false_when_no_HttpContext()
    {
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "Svc",
            UseHttpContextCorrelation = true,
            CurrentTestInfoFetcher = () => ("T", "id")
        }, new HttpContextAccessor { HttpContext = null });

        Assert.False(tracker.IsCurrentRequestFromMyHost());
    }

    [Fact]
    public void IsCurrentRequestFromMyHost_returns_false_when_UseHttpContextCorrelation_is_false()
    {
        var accessor = CreateHttpContextAccessor();
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "Svc",
            UseHttpContextCorrelation = false,
            CurrentTestInfoFetcher = () => ("T", "id")
        }, accessor);

        Assert.False(tracker.IsCurrentRequestFromMyHost());
    }

    [Fact]
    public void IsCurrentRequestFromMyHost_returns_true_when_same_tracker_is_in_request_services()
    {
        var services = new ServiceCollection();
        var accessor = new HttpContextAccessor();

        var options = new MessageTrackerOptions
        {
            CallerName = "Svc",
            UseHttpContextCorrelation = true,
            CurrentTestInfoFetcher = () => ("T", "id")
        };

        services.AddSingleton<IHttpContextAccessor>(accessor);
        services.AddSingleton(sp => new MessageTracker(options, sp.GetRequiredService<IHttpContextAccessor>()));

        var provider = services.BuildServiceProvider();
        var tracker = provider.GetRequiredService<MessageTracker>();

        var context = new DefaultHttpContext { RequestServices = provider };
        accessor.HttpContext = context;

        Assert.True(tracker.IsCurrentRequestFromMyHost());
    }

    [Fact]
    public void IsCurrentRequestFromMyHost_returns_false_when_different_host_in_request_services()
    {
        var accessor = new HttpContextAccessor();
        var options = new MessageTrackerOptions
        {
            CallerName = "Svc",
            UseHttpContextCorrelation = true,
            CurrentTestInfoFetcher = () => ("T", "id")
        };

        // Create tracker from one DI container
        var tracker = new MessageTracker(options, accessor);

        // Create a different DI container with a different IHttpContextAccessor
        var otherServices = new ServiceCollection();
        otherServices.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
        var otherProvider = otherServices.BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = otherProvider };
        accessor.HttpContext = context;

        Assert.False(tracker.IsCurrentRequestFromMyHost());
    }

    [Fact]
    public void IsCurrentRequestFromMyHost_returns_true_for_keyed_singleton_tracker()
    {
        var services = new ServiceCollection();
        var accessor = new HttpContextAccessor();
        var options = new MessageTrackerOptions
        {
            CallerName = "Kafka Broker",
            UseHttpContextCorrelation = true,
            CurrentTestInfoFetcher = () => ("T", "id")
        };

        services.AddSingleton<IHttpContextAccessor>(accessor);
        services.AddKeyedSingleton("Kafka", (sp, _) => new MessageTracker(options, sp.GetRequiredService<IHttpContextAccessor>()));

        var provider = services.BuildServiceProvider();
        var tracker = provider.GetRequiredKeyedService<MessageTracker>("Kafka");

        var context = new DefaultHttpContext { RequestServices = provider };
        accessor.HttpContext = context;

        Assert.True(tracker.IsCurrentRequestFromMyHost());
    }

    [Fact]
    public void IsCurrentRequestFromMyHost_returns_false_for_keyed_tracker_from_different_host()
    {
        // Host A: creates tracker and accessor
        var servicesA = new ServiceCollection();
        var accessorA = new HttpContextAccessor();
        servicesA.AddSingleton<IHttpContextAccessor>(accessorA);
        servicesA.AddKeyedSingleton("Kafka", (sp, _) => new MessageTracker(
            new MessageTrackerOptions
            {
                CallerName = "Kafka Broker",
                UseHttpContextCorrelation = true,
                CurrentTestInfoFetcher = () => ("T", "id")
            },
            sp.GetRequiredService<IHttpContextAccessor>()));
        var providerA = servicesA.BuildServiceProvider();
        var trackerA = providerA.GetRequiredKeyedService<MessageTracker>("Kafka");

        // Host B: different DI container
        var servicesB = new ServiceCollection();
        var accessorB = new HttpContextAccessor();
        servicesB.AddSingleton<IHttpContextAccessor>(accessorB);
        servicesB.AddKeyedSingleton("Kafka", (sp, _) => new MessageTracker(
            new MessageTrackerOptions
            {
                CallerName = "Kafka Broker",
                UseHttpContextCorrelation = true,
                CurrentTestInfoFetcher = () => ("T2", "id2")
            },
            sp.GetRequiredService<IHttpContextAccessor>()));
        var providerB = servicesB.BuildServiceProvider();

        // Request comes from Host B
        var context = new DefaultHttpContext { RequestServices = providerB };
        accessorA.HttpContext = context;

        // Tracker A should NOT match Host B's request
        Assert.False(trackerA.IsCurrentRequestFromMyHost());
    }

    // ─── TrackConsumeEvent note placement ───────────────────────

    [Fact]
    public void TrackConsumeEvent_sets_NoteOnRight_on_request_log()
    {
        var testId = Guid.NewGuid().ToString();
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "Kafka Broker",
            CurrentTestInfoFetcher = () => ("T", testId)
        });

        tracker.TrackConsumeEvent("Consume (Kafka)", "Consumer", new Uri("kafka:///t"), new { OrderId = "123" });

        var request = RequestResponseLogger.RequestAndResponseLogs
            .First(l => l.TestId == testId && l.Type == RequestResponseType.Request);
        Assert.True(request.NoteOnRight);
    }

    [Fact]
    public void TrackSendEvent_does_not_set_NoteOnRight()
    {
        var testId = Guid.NewGuid().ToString();
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "Svc",
            CurrentTestInfoFetcher = () => ("T", testId)
        });

        tracker.TrackSendEvent("Kafka", "Broker", new Uri("kafka:///t"), new { OrderId = "123" });

        var request = RequestResponseLogger.RequestAndResponseLogs
            .First(l => l.TestId == testId && l.Type == RequestResponseType.Request);
        Assert.False(request.NoteOnRight);
    }

    [Fact]
    public void TrackMessageRequest_does_not_set_NoteOnRight()
    {
        var tracker = CreateOptionsTracker();

        var id = tracker.TrackMessageRequest("Kafka", "Svc", new Uri("kafka://t"), new { });

        var log = GetLogsById(id).Single();
        Assert.False(log.NoteOnRight);
    }

    // ─── TrackSendMessage (atomic, non-event arrows) ────────────

    [Fact]
    public void TrackSendMessage_logs_request_and_response_pair()
    {
        var testId = Guid.NewGuid().ToString();
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "Svc",
            CurrentTestInfoFetcher = () => ("T", testId)
        });

        var id = tracker.TrackSendMessage("Send (SB)", "Service Bus",
            new Uri("servicebus://sb/queue"), new { Msg = "hello" });

        var logs = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.RequestResponseId == id).ToArray();
        Assert.Equal(2, logs.Length);
        Assert.Contains(logs, l => l.Type == RequestResponseType.Request);
        Assert.Contains(logs, l => l.Type == RequestResponseType.Response);
    }

    [Fact]
    public void TrackSendMessage_shares_same_requestResponseId()
    {
        var testId = Guid.NewGuid().ToString();
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "Svc",
            CurrentTestInfoFetcher = () => ("T", testId)
        });

        var id = tracker.TrackSendMessage("Send", "Queue",
            new Uri("sb://q"), new { });

        var logs = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.RequestResponseId == id).ToArray();
        Assert.Equal(2, logs.Length);
        Assert.Equal(logs[0].RequestResponseId, logs[1].RequestResponseId);
    }

    [Fact]
    public void TrackSendMessage_uses_Event_meta_type()
    {
        var testId = Guid.NewGuid().ToString();
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = "Svc",
            CurrentTestInfoFetcher = () => ("T", testId)
        });

        var id = tracker.TrackSendMessage("Send", "Queue",
            new Uri("sb://q"), new { });

        var logs = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.RequestResponseId == id).ToArray();
        Assert.All(logs, l => Assert.Equal(RequestResponseMetaType.Event, l.MetaType));
    }

    [Fact]
    public void TrackSendMessage_returns_non_empty_guid()
    {
        var tracker = CreateOptionsTracker();

        var id = tracker.TrackSendMessage("Send", "Queue",
            new Uri("sb://q"), new { });

        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public void TrackSendMessage_increments_invocation_count()
    {
        var tracker = CreateOptionsTracker();
        var before = tracker.InvocationCount;

        tracker.TrackSendMessage("Send", "Queue",
            new Uri("sb://q"), new { });

        Assert.Equal(before + 1, tracker.InvocationCount);
    }

    [Fact]
    public void TrackSendMessage_does_nothing_when_no_test_info()
    {
        if (TestIdentityScope.Current is not null || TestIdentityScope.GlobalFallback is not null)
            return;

        var marker = $"NoLog-SendMessage-{Guid.NewGuid():N}";
        var tracker = new MessageTracker(new MessageTrackerOptions
        {
            CallerName = marker,
            CurrentTestInfoFetcher = null
        });

        var id = tracker.TrackSendMessage("Send", "Queue",
            new Uri("sb://q"), new { });

        Assert.Equal(Guid.Empty, id);
        var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.CallerName == marker).ToArray();
        Assert.Empty(logs);
    }

    // ─── UnknownIdentity fallthrough ─────────────────────────────

    [Fact]
    public void TrackSendEvent_falls_through_to_GlobalFallback_when_fetcher_returns_UnknownIdentity()
    {
        TestIdentityScope.Reset();
        var testId = Guid.NewGuid().ToString();
        try
        {
            TestIdentityScope.SetGlobalFallback("GlobalTest", testId);
            var tracker = new MessageTracker(new MessageTrackerOptions
            {
                CallerName = "Svc",
                CurrentTestInfoFetcher = () => TestIdentityScope.UnknownIdentity
            });

            tracker.TrackSendEvent("Send", "Dest", new Uri("kafka://t"), new { Id = 1 });

            // Verify UnknownIdentity was NOT used — logs should exist with non-Unknown test ID
            var allNewLogs = RequestResponseLogger.RequestAndResponseLogs
                .Where(l => l.CallerName == "Svc" && l.ServiceName == "Dest")
                .Where(l => l.TestId != TestIdentityScope.UnknownTestId)
                .ToArray();
            Assert.NotEmpty(allNewLogs);
        }
        finally
        {
            TestIdentityScope.ClearGlobalFallback();
        }
    }

    [Fact]
    public void TrackSendEvent_falls_through_to_TestIdentityScope_when_fetcher_returns_UnknownIdentity()
    {
        using (TestIdentityScope.Begin("ScopeTest", "scope-test-id"))
        {
            var tracker = new MessageTracker(new MessageTrackerOptions
            {
                CallerName = "Svc",
                CurrentTestInfoFetcher = () => TestIdentityScope.UnknownIdentity
            });

            tracker.TrackSendEvent("Send", "Dest", new Uri("kafka://t"), new { Id = 1 });

            var logs = RequestResponseLogger.RequestAndResponseLogs
                .Where(l => l.TestId == "scope-test-id").ToArray();
            Assert.NotEmpty(logs);
            Assert.Equal("ScopeTest", logs[0].TestName);
        }
    }
}
