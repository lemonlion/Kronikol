using System.Net;
using Kronikol.Extensions.StorageQueues;
using Kronikol.Tracking;
using Kronikol.Tests.History;

namespace Kronikol.Tests.StorageQueues;

public class StorageQueueTrackingMessageHandlerTests : IDisposable
{
    // ─── Test infrastructure ────────────────────────────────────

    private class StubInnerHandler : HttpMessageHandler
    {
        public HttpRequestMessage? CapturedRequest { get; private set; }
        public HttpResponseMessage ResponseToReturn { get; set; } = new(HttpStatusCode.OK) { Content = new StringContent("queue-response") };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedRequest = request;
            return Task.FromResult(ResponseToReturn);
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

    private HttpMessageInvoker CreateInvoker(StorageQueueTrackingMessageHandlerOptions options)
    {
        var handler = new StorageQueueTrackingMessageHandler(options, _innerHandler);
        return new HttpMessageInvoker(handler);
    }

    private StorageQueueTrackingMessageHandlerOptions MakeOptions(
        StorageQueueTrackingVerbosity verbosity = StorageQueueTrackingVerbosity.Detailed,
        string serviceName = "StorageQueue",
        string callerName = "TestCaller") => new()
    {
        ServiceName = serviceName,
        CallerName = callerName,
        Verbosity = verbosity,
        CurrentTestInfoFetcher = () => ("My Test", _testId),
    };

    private static HttpRequestMessage MakeSendRequest()
    {
        return new HttpRequestMessage(HttpMethod.Post,
            "https://account.queue.core.windows.net/my-queue/messages")
        {
            Content = new StringContent("<QueueMessage><MessageText>hello</MessageText></QueueMessage>")
        };
    }

    private static HttpRequestMessage MakeReceiveRequest()
    {
        return new HttpRequestMessage(HttpMethod.Get,
            "https://account.queue.core.windows.net/my-queue/messages");
    }

    private static HttpRequestMessage MakeDeleteMessageRequest()
    {
        return new HttpRequestMessage(HttpMethod.Delete,
            "https://account.queue.core.windows.net/my-queue/messages/msg-1?popreceipt=abc");
    }

    private static HttpRequestMessage MakeCreateQueueRequest()
    {
        return new HttpRequestMessage(HttpMethod.Put,
            "https://account.queue.core.windows.net/my-queue");
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
        await invoker.SendAsync(MakeReceiveRequest(), CancellationToken.None);

        HistoryGuard.Pin(GetLogsFromThisTest(), 8,
            "TestCaller>StorageQueue Receive ← my-queue /my-queue 200");
    }

    [Fact]
    public async Task Logs_request_and_response_for_each_call()
    {
        using var invoker = CreateInvoker(MakeOptions());

        await invoker.SendAsync(MakeSendRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal(RequestResponseType.Request, logs[0].Type);
        Assert.Equal(RequestResponseType.Response, logs[1].Type);
    }

    [Fact]
    public async Task Logs_correct_service_and_caller_names()
    {
        using var invoker = CreateInvoker(MakeOptions(callerName: "MyApi", serviceName: "OrdersQueue"));

        await invoker.SendAsync(MakeSendRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Equal("OrdersQueue", logs[0].ServiceName);
        Assert.Equal("MyApi", logs[0].CallerName);
    }

    [Fact]
    public async Task Does_not_log_when_no_test_info_fetcher()
    {
        var options = MakeOptions();
        options.CurrentTestInfoFetcher = null;
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeSendRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Empty(logs);
    }

    [Fact]
    public async Task Request_is_still_forwarded_when_no_test_info_fetcher()
    {
        var options = MakeOptions();
        options.CurrentTestInfoFetcher = null;
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeSendRequest(), CancellationToken.None);

        Assert.NotNull(_innerHandler.CapturedRequest);
    }

    // ─── Detailed verbosity ────────────────────────────────────

    [Fact]
    public async Task Detailed_SendMessage_UsesClassifiedLabel()
    {
        using var invoker = CreateInvoker(MakeOptions(StorageQueueTrackingVerbosity.Detailed));

        await invoker.SendAsync(MakeSendRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("Send → my-queue", log.Method.Value?.ToString());
    }

    [Fact]
    public async Task Detailed_ReceiveMessages_UsesClassifiedLabel()
    {
        using var invoker = CreateInvoker(MakeOptions(StorageQueueTrackingVerbosity.Detailed));

        await invoker.SendAsync(MakeReceiveRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("Receive ← my-queue", log.Method.Value?.ToString());
    }

    [Fact]
    public async Task Detailed_IncludesResponseContent()
    {
        using var invoker = CreateInvoker(MakeOptions(StorageQueueTrackingVerbosity.Detailed));

        await invoker.SendAsync(MakeReceiveRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Contains("queue-response", log.Content);
    }

    [Fact]
    public async Task Detailed_UsesCleanUri()
    {
        using var invoker = CreateInvoker(MakeOptions(StorageQueueTrackingVerbosity.Detailed));

        await invoker.SendAsync(MakeReceiveRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("storagequeue:///my-queue", log.Uri.ToString());
    }

    // ─── Summarised verbosity ──────────────────────────────────

    [Fact]
    public async Task Summarised_UsesOperationNameOnly()
    {
        using var invoker = CreateInvoker(MakeOptions(StorageQueueTrackingVerbosity.Summarised));

        await invoker.SendAsync(MakeSendRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("SendMessage", log.Method.Value?.ToString());
    }

    [Fact]
    public async Task Summarised_OmitsRequestContent()
    {
        using var invoker = CreateInvoker(MakeOptions(StorageQueueTrackingVerbosity.Summarised));

        await invoker.SendAsync(MakeSendRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Null(log.Content);
    }

    [Fact]
    public async Task Summarised_OmitsResponseContent()
    {
        using var invoker = CreateInvoker(MakeOptions(StorageQueueTrackingVerbosity.Summarised));

        await invoker.SendAsync(MakeReceiveRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Null(log.Content);
    }

    [Fact]
    public async Task Summarised_OmitsHeaders()
    {
        using var invoker = CreateInvoker(MakeOptions(StorageQueueTrackingVerbosity.Summarised));

        await invoker.SendAsync(MakeReceiveRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Empty(log.Headers);
    }

    [Fact]
    public async Task Summarised_SkipsOtherOperations()
    {
        using var invoker = CreateInvoker(MakeOptions(StorageQueueTrackingVerbosity.Summarised));

        // GET on queue without comp=metadata → Other
        var request = new HttpRequestMessage(HttpMethod.Get,
            "https://account.queue.core.windows.net/my-queue");
        await invoker.SendAsync(request, CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Empty(logs);
    }

    // ─── Raw verbosity ────────────────────────────────────────

    [Fact]
    public async Task Raw_UsesHttpMethodAsMethod()
    {
        using var invoker = CreateInvoker(MakeOptions(StorageQueueTrackingVerbosity.Raw));

        await invoker.SendAsync(MakeSendRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal(HttpMethod.Post, log.Method.Value);
    }

    [Fact]
    public async Task Raw_IncludesFullContent()
    {
        using var invoker = CreateInvoker(MakeOptions(StorageQueueTrackingVerbosity.Raw));

        await invoker.SendAsync(MakeSendRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains("hello", log.Content);
    }

    [Fact]
    public async Task Raw_DoesNotSkipOtherOperations()
    {
        using var invoker = CreateInvoker(MakeOptions(StorageQueueTrackingVerbosity.Raw));

        var request = new HttpRequestMessage(HttpMethod.Get,
            "https://account.queue.core.windows.net/my-queue");
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
        var options = MakeOptions(StorageQueueTrackingVerbosity.Summarised);
        options.ActionVerbosity = StorageQueueTrackingVerbosity.Detailed;
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeSendRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        var request = logs.First(l => l.Type == RequestResponseType.Request);
        var response = logs.First(l => l.Type == RequestResponseType.Response);
        Assert.Null(request.Content);
        Assert.Null(request.SetupVariant!.Content);
        Assert.Equal("<QueueMessage><MessageText>hello</MessageText></QueueMessage>", request.ActionVariant!.Content);
        Assert.Null(response.Content);
        Assert.Null(response.SetupVariant!.Content);
        Assert.Equal("queue-response", response.ActionVariant!.Content);
    }

    [Fact]
    public async Task At_Summarised_in_every_phase_a_body_nothing_shows_is_not_read()
    {
        TestPhaseContext.Reset();
        var reply = new CountingContent("queue-response");
        _innerHandler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK) { Content = reply };
        var options = MakeOptions(StorageQueueTrackingVerbosity.Summarised);
        options.SetupVerbosity = StorageQueueTrackingVerbosity.Summarised;
        using var invoker = CreateInvoker(options);
        var message = new CountingContent("<QueueMessage><MessageText>hello</MessageText></QueueMessage>");

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Post,
            "https://account.queue.core.windows.net/my-queue/messages") { Content = message }, CancellationToken.None);

        Assert.Equal(0, message.Reads);
        Assert.Equal(0, reply.Reads);
    }

    /// <summary>Content that counts how often its bytes are read.</summary>
    private sealed class CountingContent(string text) : HttpContent
    {
        private readonly byte[] _bytes = System.Text.Encoding.UTF8.GetBytes(text);
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
        using var invoker = CreateInvoker(MakeOptions(StorageQueueTrackingVerbosity.Detailed));
        var request = MakeReceiveRequest();
        request.Headers.Add("x-ms-date", "Tue, 29 Mar 2016 02:03:06 GMT");
        request.Headers.Add("x-ms-custom-header", "keep-me");

        await invoker.SendAsync(request, CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.DoesNotContain(log.Headers, h => h.Key == "x-ms-date");
        Assert.Contains(log.Headers, h => h.Key == "x-ms-custom-header");
    }

    // ─── Status code ──────────────────────────────────────────

    [Fact]
    public async Task Response_IncludesStatusCode()
    {
        _innerHandler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("")
        };
        using var invoker = CreateInvoker(MakeOptions());

        await invoker.SendAsync(MakeSendRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Equal(HttpStatusCode.Created, log.StatusCode?.Value);
    }

    // ─── ITrackingComponent ────────────────────────────────────

    [Fact]
    public void Implements_ITrackingComponent()
    {
        var handler = new StorageQueueTrackingMessageHandler(MakeOptions());
        Assert.IsAssignableFrom<ITrackingComponent>(handler);
    }

    [Fact]
    public void WasInvoked_IsFalse_BeforeAnyRequests()
    {
        var handler = new StorageQueueTrackingMessageHandler(MakeOptions());
        Assert.False(handler.WasInvoked);
    }

    [Fact]
    public async Task WasInvoked_IsTrue_AfterRequest()
    {
        var handler = new StorageQueueTrackingMessageHandler(MakeOptions(), _innerHandler);
        using var invoker = new HttpMessageInvoker(handler);
        await invoker.SendAsync(MakeSendRequest(), CancellationToken.None);

        Assert.True(handler.WasInvoked);
    }

    [Fact]
    public void InvocationCount_StartsAtZero()
    {
        var handler = new StorageQueueTrackingMessageHandler(MakeOptions());
        Assert.Equal(0, handler.InvocationCount);
    }

    [Fact]
    public void ComponentName_MatchesServiceName()
    {
        var handler = new StorageQueueTrackingMessageHandler(MakeOptions(serviceName: "OrdersQueue"));
        Assert.Equal("StorageQueueTrackingMessageHandler (OrdersQueue)", handler.ComponentName);
    }

    [Fact]
    public void Constructor_AutoRegistersWithTrackingComponentRegistry()
    {
        TrackingComponentRegistry.Clear();
        var handler = new StorageQueueTrackingMessageHandler(MakeOptions());

        var components = TrackingComponentRegistry.GetRegisteredComponents();
        Assert.Contains(components, c => ReferenceEquals(c, handler));
    }
}
