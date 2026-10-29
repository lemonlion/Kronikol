using System.Net;
using Kronikol.Extensions.SNS;
using Kronikol.Tracking;
using Kronikol.Tests.History;

namespace Kronikol.Tests.SNS;

public class SnsTrackingMessageHandlerTests : IDisposable
{
    private class StubInnerHandler : HttpMessageHandler
    {
        public HttpRequestMessage? CapturedRequest { get; private set; }
        public string? CapturedRequestBody { get; private set; }
        public HttpResponseMessage ResponseToReturn { get; set; } = new(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"MessageId": "msg-123"}""")
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

    private HttpMessageInvoker CreateInvoker(SnsTrackingMessageHandlerOptions options)
    {
        var handler = new SnsTrackingMessageHandler(options, _innerHandler);
        return new HttpMessageInvoker(handler);
    }

    private SnsTrackingMessageHandlerOptions MakeOptions(
        SnsTrackingVerbosity verbosity = SnsTrackingVerbosity.Detailed,
        string serviceName = "SNS",
        string callerName = "TestCaller") => new()
    {
        ServiceName = serviceName,
        CallerName = callerName,
        Verbosity = verbosity,
        CurrentTestInfoFetcher = () => ("My Test", _testId),
    };

    private static HttpRequestMessage MakePublishRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://sns.us-east-1.amazonaws.com/");
        request.Headers.Add("X-Amz-Target", "AmazonSimpleNotificationService.Publish");
        request.Content = new StringContent(
            """{"TopicArn": "arn:aws:sns:us-east-1:123456789012:my-topic", "Message": "hello"}""",
            System.Text.Encoding.UTF8, "application/x-amz-json-1.0");
        return request;
    }

    private static HttpRequestMessage MakeSubscribeRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://sns.us-east-1.amazonaws.com/");
        request.Headers.Add("X-Amz-Target", "AmazonSimpleNotificationService.Subscribe");
        request.Content = new StringContent(
            """{"TopicArn": "arn:aws:sns:us-east-1:123456789012:my-topic", "Protocol": "sqs", "Endpoint": "arn:aws:sqs:us-east-1:123456789012:my-queue"}""",
            System.Text.Encoding.UTF8, "application/x-amz-json-1.0");
        return request;
    }

    private static HttpRequestMessage MakeDeleteTopicRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://sns.us-east-1.amazonaws.com/");
        request.Headers.Add("X-Amz-Target", "AmazonSimpleNotificationService.DeleteTopic");
        request.Content = new StringContent(
            """{"TopicArn": "arn:aws:sns:us-east-1:123456789012:my-topic"}""",
            System.Text.Encoding.UTF8, "application/x-amz-json-1.0");
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
        await invoker.SendAsync(MakePublishRequest(), CancellationToken.None);

        HistoryGuard.Pin(GetLogsFromThisTest(), 9,
            "TestCaller>SNS Publish /my-topic 200");
    }

    [Fact]
    public async Task Logs_request_and_response_for_each_call()
    {
        using var invoker = CreateInvoker(MakeOptions());

        await invoker.SendAsync(MakePublishRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal(RequestResponseType.Request, logs[0].Type);
        Assert.Equal(RequestResponseType.Response, logs[1].Type);
    }

    [Fact]
    public async Task Logs_correct_service_and_caller_names()
    {
        using var invoker = CreateInvoker(MakeOptions(callerName: "MyApi", serviceName: "OrdersSNS"));

        await invoker.SendAsync(MakePublishRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Equal("OrdersSNS", logs[0].ServiceName);
        Assert.Equal("MyApi", logs[0].CallerName);
    }

    [Fact]
    public async Task Does_not_log_when_no_test_info_fetcher()
    {
        var options = MakeOptions();
        options.CurrentTestInfoFetcher = null;
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakePublishRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Empty(logs);
    }

    [Fact]
    public async Task Request_is_still_forwarded_when_no_test_info_fetcher()
    {
        var options = MakeOptions();
        options.CurrentTestInfoFetcher = null;
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakePublishRequest(), CancellationToken.None);

        Assert.NotNull(_innerHandler.CapturedRequest);
    }

    // ─── Request body reconstruction ──────────────────────────

    [Fact]
    public async Task Request_body_is_reconstructed_after_classification()
    {
        using var invoker = CreateInvoker(MakeOptions());

        await invoker.SendAsync(MakePublishRequest(), CancellationToken.None);

        Assert.NotNull(_innerHandler.CapturedRequestBody);
        Assert.Contains("my-topic", _innerHandler.CapturedRequestBody);
    }

    [Fact]
    public async Task Request_body_is_reconstructed_even_when_no_test_info()
    {
        var options = MakeOptions();
        options.CurrentTestInfoFetcher = null;
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakePublishRequest(), CancellationToken.None);

        Assert.NotNull(_innerHandler.CapturedRequestBody);
        Assert.Contains("my-topic", _innerHandler.CapturedRequestBody);
    }

    // ─── Detailed verbosity ────────────────────────────────────

    [Fact]
    public async Task Detailed_Publish_UsesClassifiedLabel()
    {
        using var invoker = CreateInvoker(MakeOptions(SnsTrackingVerbosity.Detailed));

        await invoker.SendAsync(MakePublishRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("Publish", log.Method.Value?.ToString());
    }

    [Fact]
    public async Task Detailed_Subscribe_UsesClassifiedLabel()
    {
        using var invoker = CreateInvoker(MakeOptions(SnsTrackingVerbosity.Detailed));

        await invoker.SendAsync(MakeSubscribeRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("Subscribe", log.Method.Value?.ToString());
    }

    [Fact]
    public async Task Detailed_UsesSnsUriScheme()
    {
        using var invoker = CreateInvoker(MakeOptions(SnsTrackingVerbosity.Detailed));

        await invoker.SendAsync(MakePublishRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.StartsWith("sns://", log.Uri.ToString());
    }

    [Fact]
    public async Task Detailed_IncludesTopicNameInUri()
    {
        using var invoker = CreateInvoker(MakeOptions(SnsTrackingVerbosity.Detailed));

        await invoker.SendAsync(MakePublishRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains("my-topic", log.Uri.ToString());
    }

    [Fact]
    public async Task Detailed_IncludesRequestBody()
    {
        using var invoker = CreateInvoker(MakeOptions(SnsTrackingVerbosity.Detailed));

        await invoker.SendAsync(MakePublishRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains("my-topic", log.Content!);
    }

    [Fact]
    public async Task Detailed_IncludesResponseContent()
    {
        using var invoker = CreateInvoker(MakeOptions(SnsTrackingVerbosity.Detailed));

        await invoker.SendAsync(MakePublishRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Contains("msg-123", log.Content!);
    }

    // ─── Summarised verbosity ──────────────────────────────────

    [Fact]
    public async Task Summarised_UsesOperationNameOnly()
    {
        using var invoker = CreateInvoker(MakeOptions(SnsTrackingVerbosity.Summarised));

        await invoker.SendAsync(MakePublishRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("Publish", log.Method.Value?.ToString());
    }

    [Fact]
    public async Task Summarised_OmitsRequestContent()
    {
        using var invoker = CreateInvoker(MakeOptions(SnsTrackingVerbosity.Summarised));

        await invoker.SendAsync(MakePublishRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Null(log.Content);
    }

    [Fact]
    public async Task Summarised_OmitsResponseContent()
    {
        using var invoker = CreateInvoker(MakeOptions(SnsTrackingVerbosity.Summarised));

        await invoker.SendAsync(MakePublishRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Null(log.Content);
    }

    [Fact]
    public async Task Summarised_OmitsHeaders()
    {
        using var invoker = CreateInvoker(MakeOptions(SnsTrackingVerbosity.Summarised));

        await invoker.SendAsync(MakePublishRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Empty(log.Headers);
    }

    [Fact]
    public async Task Summarised_SkipsOtherOperations()
    {
        using var invoker = CreateInvoker(MakeOptions(SnsTrackingVerbosity.Summarised));

        // No X-Amz-Target header → Other
        var request = new HttpRequestMessage(HttpMethod.Post, "https://sns.us-east-1.amazonaws.com/");
        request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        await invoker.SendAsync(request, CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Empty(logs);
    }

    // ─── Raw verbosity ────────────────────────────────────────

    [Fact]
    public async Task Raw_UsesHttpMethodAsMethod()
    {
        using var invoker = CreateInvoker(MakeOptions(SnsTrackingVerbosity.Raw));

        await invoker.SendAsync(MakePublishRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal(HttpMethod.Post, log.Method.Value);
    }

    [Fact]
    public async Task Raw_IncludesFullContent()
    {
        using var invoker = CreateInvoker(MakeOptions(SnsTrackingVerbosity.Raw));

        await invoker.SendAsync(MakePublishRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains("my-topic", log.Content!);
    }

    [Fact]
    public async Task Raw_UsesOriginalUri()
    {
        using var invoker = CreateInvoker(MakeOptions(SnsTrackingVerbosity.Raw));

        await invoker.SendAsync(MakePublishRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains("amazonaws.com", log.Uri.ToString());
    }

    [Fact]
    public async Task Raw_DoesNotSkipOtherOperations()
    {
        using var invoker = CreateInvoker(MakeOptions(SnsTrackingVerbosity.Raw));

        var request = new HttpRequestMessage(HttpMethod.Post, "https://sns.us-east-1.amazonaws.com/");
        request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        await invoker.SendAsync(request, CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
    }

    // ─── Phase variants ───────────────────────────────────────

    // A phase's own verbosity decides its notes: a Detailed phase of a Summarised handler shows the bodies a Detailed
    // handler shows, though the call was made before its phase was known.
    [Fact]
    public async Task A_detailed_phase_of_a_summarised_handler_shows_both_bodies()
    {
        TestPhaseContext.Reset();
        var options = MakeOptions(SnsTrackingVerbosity.Summarised);
        options.ActionVerbosity = SnsTrackingVerbosity.Detailed;
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakePublishRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        var request = logs.First(l => l.Type == RequestResponseType.Request);
        var response = logs.First(l => l.Type == RequestResponseType.Response);
        Assert.Null(request.Content);
        Assert.Null(request.SetupVariant!.Content);
        Assert.Contains("\"Message\": \"hello\"", request.ActionVariant!.Content);
        Assert.Null(response.Content);
        Assert.Null(response.SetupVariant!.Content);
        Assert.Contains("msg-123", response.ActionVariant!.Content);
    }

    // The request body is always read, to classify the call; the response body only when a note shows it.
    [Fact]
    public async Task At_Summarised_in_every_phase_a_response_body_nothing_shows_is_not_read()
    {
        TestPhaseContext.Reset();
        var reply = new CountingContent("""{"MessageId": "msg-123"}""");
        _innerHandler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK) { Content = reply };
        var options = MakeOptions(SnsTrackingVerbosity.Summarised);
        options.SetupVerbosity = SnsTrackingVerbosity.Summarised;
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakePublishRequest(), CancellationToken.None);

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
        using var invoker = CreateInvoker(MakeOptions(SnsTrackingVerbosity.Detailed));
        var request = MakePublishRequest();
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

        await invoker.SendAsync(MakePublishRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Equal(HttpStatusCode.OK, log.StatusCode?.Value);
    }

    // ─── ITrackingComponent ────────────────────────────────────

    [Fact]
    public void Implements_ITrackingComponent()
    {
        var handler = new SnsTrackingMessageHandler(MakeOptions());
        Assert.IsAssignableFrom<ITrackingComponent>(handler);
    }

    [Fact]
    public void WasInvoked_IsFalse_BeforeAnyRequests()
    {
        var handler = new SnsTrackingMessageHandler(MakeOptions());
        Assert.False(handler.WasInvoked);
    }

    [Fact]
    public async Task WasInvoked_IsTrue_AfterRequest()
    {
        var handler = new SnsTrackingMessageHandler(MakeOptions(), _innerHandler);
        using var invoker = new HttpMessageInvoker(handler);
        await invoker.SendAsync(MakePublishRequest(), CancellationToken.None);

        Assert.True(handler.WasInvoked);
    }

    [Fact]
    public void InvocationCount_StartsAtZero()
    {
        var handler = new SnsTrackingMessageHandler(MakeOptions());
        Assert.Equal(0, handler.InvocationCount);
    }

    [Fact]
    public async Task InvocationCount_IncreasesWithEachCall()
    {
        var handler = new SnsTrackingMessageHandler(MakeOptions(), _innerHandler);
        using var invoker = new HttpMessageInvoker(handler);

        await invoker.SendAsync(MakePublishRequest(), CancellationToken.None);
        await invoker.SendAsync(MakeSubscribeRequest(), CancellationToken.None);
        await invoker.SendAsync(MakeDeleteTopicRequest(), CancellationToken.None);

        Assert.Equal(3, handler.InvocationCount);
    }

    [Fact]
    public void ComponentName_MatchesServiceName()
    {
        var handler = new SnsTrackingMessageHandler(MakeOptions(serviceName: "MySNS"));
        Assert.Equal("SnsTrackingMessageHandler (MySNS)", handler.ComponentName);
    }

    [Fact]
    public void Constructor_AutoRegistersWithTrackingComponentRegistry()
    {
        TrackingComponentRegistry.Clear();
        var handler = new SnsTrackingMessageHandler(MakeOptions());

        var components = TrackingComponentRegistry.GetRegisteredComponents();
        Assert.Contains(components, c => ReferenceEquals(c, handler));
    }
}
