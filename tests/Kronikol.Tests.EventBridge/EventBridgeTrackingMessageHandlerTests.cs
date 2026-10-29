using System.Net;
using Kronikol.Extensions.EventBridge;
using Kronikol.Tracking;
using Kronikol.Tests.History;

namespace Kronikol.Tests.EventBridge;

public class EventBridgeTrackingMessageHandlerTests : IDisposable
{
    private class StubInnerHandler : HttpMessageHandler
    {
        public HttpRequestMessage? CapturedRequest { get; private set; }
        public string? CapturedRequestBody { get; private set; }
        public HttpResponseMessage ResponseToReturn { get; set; } = new(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"FailedEntryCount": 0, "Entries": []}""")
        };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedRequest = request;
            if (request.Content is not null)
                CapturedRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
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

    private HttpMessageInvoker CreateInvoker(EventBridgeTrackingMessageHandlerOptions options)
    {
        var handler = new EventBridgeTrackingMessageHandler(options, _innerHandler);
        return new HttpMessageInvoker(handler);
    }

    private EventBridgeTrackingMessageHandlerOptions MakeOptions(
        EventBridgeTrackingVerbosity verbosity = EventBridgeTrackingVerbosity.Detailed,
        string serviceName = "EventBridge",
        string callerName = "TestCaller") => new()
    {
        ServiceName = serviceName,
        CallerName = callerName,
        Verbosity = verbosity,
        CurrentTestInfoFetcher = () => ("My Test", _testId),
        ExcludedOperations = [],
    };

    private static HttpRequestMessage MakePutEventsRequest(string? eventBusName = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://events.us-east-1.amazonaws.com/");
        request.Headers.Add("X-Amz-Target", "AWSEvents.PutEvents");
        var busFragment = eventBusName is not null
            ? $", \"EventBusName\": \"{eventBusName}\""
            : "";
        var body = "{\"Entries\": [{\"Source\": \"my.service\", \"DetailType\": \"OrderCreated\", \"Detail\": \"{}\"" + busFragment + "}]}";
        request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/x-amz-json-1.1");
        return request;
    }

    private static HttpRequestMessage MakePutRuleRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://events.us-east-1.amazonaws.com/");
        request.Headers.Add("X-Amz-Target", "AWSEvents.PutRule");
        request.Content = new StringContent(
            """{"Name": "my-rule", "EventBusName": "orders-bus", "State": "ENABLED"}""",
            System.Text.Encoding.UTF8, "application/x-amz-json-1.1");
        return request;
    }

    private static HttpRequestMessage MakeCreateEventBusRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://events.us-east-1.amazonaws.com/");
        request.Headers.Add("X-Amz-Target", "AWSEvents.CreateEventBus");
        request.Content = new StringContent(
            """{"Name": "orders-bus"}""",
            System.Text.Encoding.UTF8, "application/x-amz-json-1.1");
        return request;
    }

    private static HttpRequestMessage MakeTagResourceRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://events.us-east-1.amazonaws.com/");
        request.Headers.Add("X-Amz-Target", "AWSEvents.TagResource");
        request.Content = new StringContent(
            """{"ResourceARN": "arn:aws:events:us-east-1:123456789012:rule/my-rule"}""",
            System.Text.Encoding.UTF8, "application/x-amz-json-1.1");
        return request;
    }

    public void Dispose()
    {
        _innerHandler.Dispose();
    }

    // ─── Basic logging ─────────────────────────────────────────

    [Fact]
    public async Task Every_change_to_a_recorded_call_line_moves_the_version()
    {
        // The history guard (plans/CALL_OPERATION_AND_ORIGIN_PLAN.md 4.8): what this tracker records for one fixed call, as the
        // call line history fingerprints, pinned under the rule it was pinned with (HistoryGuard).
        using var invoker = CreateInvoker(MakeOptions());
        await invoker.SendAsync(MakePutEventsRequest(), CancellationToken.None);

        HistoryGuard.Pin(GetLogsFromThisTest(), 10,
            "TestCaller>EventBridge PutEvents [OrderCreated] /default/ 200");
    }

    [Fact]
    public async Task Logs_request_and_response_for_each_call()
    {
        using var invoker = CreateInvoker(MakeOptions());

        await invoker.SendAsync(MakePutEventsRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal(RequestResponseType.Request, logs[0].Type);
        Assert.Equal(RequestResponseType.Response, logs[1].Type);
    }

    [Fact]
    public async Task Logs_correct_service_and_caller_names()
    {
        using var invoker = CreateInvoker(MakeOptions(callerName: "MyApi", serviceName: "OrdersEB"));

        await invoker.SendAsync(MakePutEventsRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Equal("OrdersEB", logs[0].ServiceName);
        Assert.Equal("MyApi", logs[0].CallerName);
    }

    [Fact]
    public async Task Does_not_log_when_no_test_info_fetcher()
    {
        var options = MakeOptions();
        options.CurrentTestInfoFetcher = null;
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakePutEventsRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Empty(logs);
    }

    [Fact]
    public async Task Request_is_still_forwarded_when_no_test_info_fetcher()
    {
        var options = MakeOptions();
        options.CurrentTestInfoFetcher = null;
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakePutEventsRequest(), CancellationToken.None);

        Assert.NotNull(_innerHandler.CapturedRequest);
    }

    // ─── Excluded operations ──────────────────────────────────

    [Fact]
    public async Task Excluded_operation_skips_logging()
    {
        var options = MakeOptions();
        options.ExcludedOperations = [EventBridgeOperation.PutEvents];
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakePutEventsRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Empty(logs);
    }

    [Fact]
    public async Task Excluded_operation_still_forwards_request()
    {
        var options = MakeOptions();
        options.ExcludedOperations = [EventBridgeOperation.PutEvents];
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakePutEventsRequest(), CancellationToken.None);

        Assert.NotNull(_innerHandler.CapturedRequest);
    }

    // ─── Request body reconstruction ──────────────────────────

    [Fact]
    public async Task Request_body_is_reconstructed_after_classification()
    {
        using var invoker = CreateInvoker(MakeOptions());

        await invoker.SendAsync(MakePutEventsRequest(), CancellationToken.None);

        Assert.NotNull(_innerHandler.CapturedRequestBody);
        Assert.Contains("OrderCreated", _innerHandler.CapturedRequestBody);
    }

    [Fact]
    public async Task Request_body_is_reconstructed_even_when_no_test_info()
    {
        var options = MakeOptions();
        options.CurrentTestInfoFetcher = null;
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakePutEventsRequest(), CancellationToken.None);

        Assert.NotNull(_innerHandler.CapturedRequestBody);
        Assert.Contains("OrderCreated", _innerHandler.CapturedRequestBody);
    }

    // ─── Detailed verbosity ────────────────────────────────────

    [Fact]
    public async Task Detailed_PutEvents_UsesClassifiedLabel()
    {
        using var invoker = CreateInvoker(MakeOptions(EventBridgeTrackingVerbosity.Detailed));

        await invoker.SendAsync(MakePutEventsRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("PutEvents [OrderCreated]", log.Method.Value?.ToString());
    }

    [Fact]
    public async Task Detailed_PutRule_UsesClassifiedLabel()
    {
        using var invoker = CreateInvoker(MakeOptions(EventBridgeTrackingVerbosity.Detailed));

        await invoker.SendAsync(MakePutRuleRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("PutRule my-rule", log.Method.Value?.ToString());
    }

    [Theory]
    [InlineData("arn:aws:events:us-east-1:123456789012:event-bus/orders-bus", "eventbridge://orders-bus/")]
    [InlineData("orders-bus", "eventbridge://orders-bus/")]
    [InlineData("orders_bus", "eventbridge://orders_bus/")]
    [InlineData("aws.partner/example.com/123", "eventbridge://aws.partner/example.com/123/")]
    public async Task A_bus_given_by_its_arn_is_named_by_its_name_and_the_call_goes_through(string bus, string uri)
    {
        // An ARN's colons read as a port where a host goes: until #192 R1 every such PutEvents threw UriFormatException out
        // of the consumer's own call, at Detailed and Summarised.
        foreach (var verbosity in new[] { EventBridgeTrackingVerbosity.Detailed, EventBridgeTrackingVerbosity.Summarised })
        {
            using var invoker = CreateInvoker(MakeOptions(verbosity));

            using var response = await invoker.SendAsync(MakePutEventsRequest(bus), CancellationToken.None);

            Assert.NotNull(response);
            var log = GetLogsFromThisTest().Last(l => l.Type == RequestResponseType.Request);
            Assert.Equal(uri, log.Uri.ToString());
        }
    }

    [Fact]
    public async Task Detailed_UsesEventBridgeUriScheme()
    {
        using var invoker = CreateInvoker(MakeOptions(EventBridgeTrackingVerbosity.Detailed));

        await invoker.SendAsync(MakePutEventsRequest("my-bus"), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.StartsWith("eventbridge://", log.Uri.ToString());
    }

    [Fact]
    public async Task Detailed_IncludesBusNameInUri()
    {
        using var invoker = CreateInvoker(MakeOptions(EventBridgeTrackingVerbosity.Detailed));

        await invoker.SendAsync(MakePutEventsRequest("my-bus"), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains("my-bus", log.Uri.ToString());
    }

    [Fact]
    public async Task Detailed_DefaultBusWhenNoBusName()
    {
        using var invoker = CreateInvoker(MakeOptions(EventBridgeTrackingVerbosity.Detailed));

        await invoker.SendAsync(MakePutEventsRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains("default", log.Uri.ToString());
    }

    [Fact]
    public async Task Detailed_IncludesRequestBody()
    {
        using var invoker = CreateInvoker(MakeOptions(EventBridgeTrackingVerbosity.Detailed));

        await invoker.SendAsync(MakePutEventsRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains("OrderCreated", log.Content!);
    }

    [Fact]
    public async Task Detailed_IncludesResponseContent()
    {
        using var invoker = CreateInvoker(MakeOptions(EventBridgeTrackingVerbosity.Detailed));

        await invoker.SendAsync(MakePutEventsRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Contains("FailedEntryCount", log.Content!);
    }

    // ─── Summarised verbosity ──────────────────────────────────

    [Fact]
    public async Task Summarised_UsesOperationNameOnly()
    {
        using var invoker = CreateInvoker(MakeOptions(EventBridgeTrackingVerbosity.Summarised));

        await invoker.SendAsync(MakePutEventsRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("PutEvents", log.Method.Value?.ToString());
    }

    [Fact]
    public async Task Summarised_OmitsRequestContent()
    {
        using var invoker = CreateInvoker(MakeOptions(EventBridgeTrackingVerbosity.Summarised));

        await invoker.SendAsync(MakePutEventsRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Null(log.Content);
    }

    [Fact]
    public async Task Summarised_OmitsResponseContent()
    {
        using var invoker = CreateInvoker(MakeOptions(EventBridgeTrackingVerbosity.Summarised));

        await invoker.SendAsync(MakePutEventsRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Null(log.Content);
    }

    [Fact]
    public async Task Summarised_OmitsHeaders()
    {
        using var invoker = CreateInvoker(MakeOptions(EventBridgeTrackingVerbosity.Summarised));

        await invoker.SendAsync(MakePutEventsRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Empty(log.Headers);
    }

    [Fact]
    public async Task Summarised_SkipsOtherOperations()
    {
        using var invoker = CreateInvoker(MakeOptions(EventBridgeTrackingVerbosity.Summarised));

        var request = new HttpRequestMessage(HttpMethod.Post, "https://events.us-east-1.amazonaws.com/");
        request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        await invoker.SendAsync(request, CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Empty(logs);
    }

    // ─── Raw verbosity ────────────────────────────────────────

    [Fact]
    public async Task Raw_UsesHttpMethodAsMethod()
    {
        using var invoker = CreateInvoker(MakeOptions(EventBridgeTrackingVerbosity.Raw));

        await invoker.SendAsync(MakePutEventsRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal(HttpMethod.Post, log.Method.Value);
    }

    [Fact]
    public async Task Raw_IncludesFullContent()
    {
        using var invoker = CreateInvoker(MakeOptions(EventBridgeTrackingVerbosity.Raw));

        await invoker.SendAsync(MakePutEventsRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains("OrderCreated", log.Content!);
    }

    [Fact]
    public async Task Raw_UsesOriginalUri()
    {
        using var invoker = CreateInvoker(MakeOptions(EventBridgeTrackingVerbosity.Raw));

        await invoker.SendAsync(MakePutEventsRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains("amazonaws.com", log.Uri.ToString());
    }

    [Fact]
    public async Task Raw_DoesNotSkipOtherOperations()
    {
        using var invoker = CreateInvoker(MakeOptions(EventBridgeTrackingVerbosity.Raw));

        var request = new HttpRequestMessage(HttpMethod.Post, "https://events.us-east-1.amazonaws.com/");
        request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        await invoker.SendAsync(request, CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
    }

    [Fact]
    public async Task Raw_UsesEventBridgeUriWithBusAndOperation()
    {
        using var invoker = CreateInvoker(MakeOptions(EventBridgeTrackingVerbosity.Raw));

        await invoker.SendAsync(MakePutEventsRequest("my-bus"), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains("amazonaws.com", log.Uri.ToString());
    }

    // ─── Phase variants ───────────────────────────────────────

    // A phase's own verbosity decides its notes: a Detailed phase of a Summarised handler shows the bodies a Detailed
    // handler shows, though the call was made before its phase was known.
    [Fact]
    public async Task A_detailed_phase_of_a_summarised_handler_shows_both_bodies()
    {
        TestPhaseContext.Reset();
        var options = MakeOptions(EventBridgeTrackingVerbosity.Summarised);
        options.ActionVerbosity = EventBridgeTrackingVerbosity.Detailed;
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakePutEventsRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        var request = logs.First(l => l.Type == RequestResponseType.Request);
        var response = logs.First(l => l.Type == RequestResponseType.Response);
        Assert.Null(request.Content);
        Assert.Null(request.SetupVariant!.Content);
        Assert.Contains("OrderCreated", request.ActionVariant!.Content);
        Assert.Null(response.Content);
        Assert.Null(response.SetupVariant!.Content);
        Assert.Contains("FailedEntryCount", response.ActionVariant!.Content);
    }

    // The request body is always read, to classify the call; the response body only when a note shows it.
    [Fact]
    public async Task At_Summarised_in_every_phase_a_response_body_nothing_shows_is_not_read()
    {
        TestPhaseContext.Reset();
        var reply = new CountingContent("""{"FailedEntryCount": 0, "Entries": []}""");
        _innerHandler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK) { Content = reply };
        var options = MakeOptions(EventBridgeTrackingVerbosity.Summarised);
        options.SetupVerbosity = EventBridgeTrackingVerbosity.Summarised;
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakePutEventsRequest(), CancellationToken.None);

        Assert.Equal(0, reply.Reads);
    }

    /// <summary>JSON content that counts how often its bytes are read.</summary>
    private sealed class CountingContent(string json) : HttpContent
    {
        private readonly byte[] _bytes = System.Text.Encoding.UTF8.GetBytes(json);
        public int Reads { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            Reads++;
            return stream.WriteAsync(_bytes).AsTask();
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _bytes.Length;
            return true;
        }
    }

    // ─── Header filtering ─────────────────────────────────────

    [Fact]
    public async Task Detailed_ExcludesDefaultNoisyHeaders()
    {
        using var invoker = CreateInvoker(MakeOptions(EventBridgeTrackingVerbosity.Detailed));
        var request = MakePutEventsRequest();
        request.Headers.Add("x-amz-date", "20240101T000000Z");
        request.Headers.Add("x-custom-header", "keep-me");

        await invoker.SendAsync(request, CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.DoesNotContain(log.Headers, h => h.Key == "x-amz-date");
        Assert.Contains(log.Headers, h => h.Key == "x-custom-header");
    }

    // ─── Status code ──────────────────────────────────────────

    [Fact]
    public async Task Response_IncludesStatusCode()
    {
        _innerHandler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}")
        };
        using var invoker = CreateInvoker(MakeOptions());

        await invoker.SendAsync(MakePutEventsRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Equal(HttpStatusCode.OK, log.StatusCode?.Value);
    }

    // ─── Trace & RequestResponse ID pairing ───────────────────

    [Fact]
    public async Task Request_and_response_share_same_traceId()
    {
        using var invoker = CreateInvoker(MakeOptions());

        await invoker.SendAsync(MakePutEventsRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Equal(logs[0].TraceId, logs[1].TraceId);
    }

    [Fact]
    public async Task Request_and_response_share_same_requestResponseId()
    {
        using var invoker = CreateInvoker(MakeOptions());

        await invoker.SendAsync(MakePutEventsRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Equal(logs[0].RequestResponseId, logs[1].RequestResponseId);
    }

    // ─── ITrackingComponent ────────────────────────────────────

    [Fact]
    public void Implements_ITrackingComponent()
    {
        var handler = new EventBridgeTrackingMessageHandler(MakeOptions());
        Assert.IsAssignableFrom<ITrackingComponent>(handler);
    }

    [Fact]
    public void WasInvoked_IsFalse_BeforeAnyRequests()
    {
        var handler = new EventBridgeTrackingMessageHandler(MakeOptions());
        Assert.False(handler.WasInvoked);
    }

    [Fact]
    public async Task WasInvoked_IsTrue_AfterRequest()
    {
        var handler = new EventBridgeTrackingMessageHandler(MakeOptions(), _innerHandler);
        using var invoker = new HttpMessageInvoker(handler);
        await invoker.SendAsync(MakePutEventsRequest(), CancellationToken.None);

        Assert.True(handler.WasInvoked);
    }

    [Fact]
    public void InvocationCount_StartsAtZero()
    {
        var handler = new EventBridgeTrackingMessageHandler(MakeOptions());
        Assert.Equal(0, handler.InvocationCount);
    }

    [Fact]
    public async Task InvocationCount_IncreasesWithEachCall()
    {
        var handler = new EventBridgeTrackingMessageHandler(MakeOptions(), _innerHandler);
        using var invoker = new HttpMessageInvoker(handler);

        await invoker.SendAsync(MakePutEventsRequest(), CancellationToken.None);
        await invoker.SendAsync(MakePutRuleRequest(), CancellationToken.None);
        await invoker.SendAsync(MakeCreateEventBusRequest(), CancellationToken.None);

        Assert.Equal(3, handler.InvocationCount);
    }

    [Fact]
    public void ComponentName_MatchesServiceName()
    {
        var handler = new EventBridgeTrackingMessageHandler(MakeOptions(serviceName: "MyEB"));
        Assert.Equal("EventBridgeTrackingMessageHandler (MyEB)", handler.ComponentName);
    }

    [Fact]
    public void Constructor_AutoRegistersWithTrackingComponentRegistry()
    {
        TrackingComponentRegistry.Clear();
        var handler = new EventBridgeTrackingMessageHandler(MakeOptions());

        var components = TrackingComponentRegistry.GetRegisteredComponents();
        Assert.Contains(components, c => ReferenceEquals(c, handler));
    }
}
