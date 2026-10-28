using System.Net;
using Kronikol.Extensions.CloudStorage;
using Kronikol.Tracking;

namespace Kronikol.Tests.CloudStorage;

public class CloudStorageTrackingMessageHandlerTests : IDisposable
{
    private class StubInnerHandler : HttpMessageHandler
    {
        public HttpRequestMessage? CapturedRequest { get; private set; }
        public HttpResponseMessage ResponseToReturn { get; set; } = new(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"kind": "storage#object", "name": "file.txt"}""")
        };

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

    private HttpMessageInvoker CreateInvoker(CloudStorageTrackingMessageHandlerOptions options)
    {
        var handler = new CloudStorageTrackingMessageHandler(options, _innerHandler);
        return new HttpMessageInvoker(handler);
    }

    private CloudStorageTrackingMessageHandlerOptions MakeOptions(
        CloudStorageTrackingVerbosity verbosity = CloudStorageTrackingVerbosity.Detailed,
        string serviceName = "CloudStorage",
        string callerName = "TestCaller") => new()
    {
        ServiceName = serviceName,
        CallerName = callerName,
        Verbosity = verbosity,
        CurrentTestInfoFetcher = () => ("My Test", _testId),
    };

    private static HttpRequestMessage MakeUploadRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            "https://storage.googleapis.com/upload/storage/v1/b/my-bucket/o?uploadType=multipart");
        request.Content = new StringContent("file-content", System.Text.Encoding.UTF8, "application/octet-stream");
        return request;
    }

    private static HttpRequestMessage MakeDownloadRequest()
    {
        return new HttpRequestMessage(HttpMethod.Get,
            "https://storage.googleapis.com/storage/v1/b/my-bucket/o/file.txt?alt=media");
    }

    private static HttpRequestMessage MakeDeleteRequest()
    {
        return new HttpRequestMessage(HttpMethod.Delete,
            "https://storage.googleapis.com/storage/v1/b/my-bucket/o/file.txt");
    }

    public void Dispose()
    {
        _innerHandler.Dispose();
    }

    // ─── Basic logging ─────────────────────────────────────────

    [Fact]
    public async Task Logs_request_and_response_for_each_call()
    {
        using var invoker = CreateInvoker(MakeOptions());

        await invoker.SendAsync(MakeUploadRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal(RequestResponseType.Request, logs[0].Type);
        Assert.Equal(RequestResponseType.Response, logs[1].Type);
    }

    [Fact]
    public async Task Logs_correct_service_and_caller_names()
    {
        using var invoker = CreateInvoker(MakeOptions(callerName: "MyApi", serviceName: "GCS"));

        await invoker.SendAsync(MakeUploadRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Equal("GCS", logs[0].ServiceName);
        Assert.Equal("MyApi", logs[0].CallerName);
    }

    [Fact]
    public async Task Does_not_log_when_no_test_info_fetcher()
    {
        var options = MakeOptions();
        options.CurrentTestInfoFetcher = null;
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeUploadRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Empty(logs);
    }

    [Fact]
    public async Task Request_is_still_forwarded_when_no_test_info_fetcher()
    {
        var options = MakeOptions();
        options.CurrentTestInfoFetcher = null;
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeUploadRequest(), CancellationToken.None);

        Assert.NotNull(_innerHandler.CapturedRequest);
    }

    // ─── Detailed verbosity ────────────────────────────────────

    [Fact]
    public async Task Detailed_Upload_UsesClassifiedLabel()
    {
        using var invoker = CreateInvoker(MakeOptions(CloudStorageTrackingVerbosity.Detailed));

        await invoker.SendAsync(MakeUploadRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains("Upload", log.Method.Value?.ToString());
    }

    [Fact]
    public async Task Detailed_UsesGcsUriScheme()
    {
        using var invoker = CreateInvoker(MakeOptions(CloudStorageTrackingVerbosity.Detailed));

        await invoker.SendAsync(MakeDownloadRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.StartsWith("gcs://", log.Uri.ToString());
    }

    [Fact]
    public async Task Detailed_IncludesBucketInUri()
    {
        using var invoker = CreateInvoker(MakeOptions(CloudStorageTrackingVerbosity.Detailed));

        await invoker.SendAsync(MakeDownloadRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains("my-bucket", log.Uri.ToString());
    }

    [Fact]
    public async Task Detailed_IncludesResponseContent()
    {
        using var invoker = CreateInvoker(MakeOptions(CloudStorageTrackingVerbosity.Detailed));

        await invoker.SendAsync(MakeDownloadRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Contains("file.txt", log.Content!);
    }

    // ─── Summarised verbosity ──────────────────────────────────

    [Fact]
    public async Task Summarised_UsesOperationNameOnly()
    {
        using var invoker = CreateInvoker(MakeOptions(CloudStorageTrackingVerbosity.Summarised));

        await invoker.SendAsync(MakeUploadRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("Upload", log.Method.Value?.ToString());
    }

    [Fact]
    public async Task Summarised_OmitsContent()
    {
        using var invoker = CreateInvoker(MakeOptions(CloudStorageTrackingVerbosity.Summarised));

        await invoker.SendAsync(MakeUploadRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Null(log.Content);
    }

    [Fact]
    public async Task Summarised_OmitsHeaders()
    {
        using var invoker = CreateInvoker(MakeOptions(CloudStorageTrackingVerbosity.Summarised));

        await invoker.SendAsync(MakeUploadRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Empty(log.Headers);
    }

    [Fact]
    public async Task Summarised_SkipsOtherOperations()
    {
        using var invoker = CreateInvoker(MakeOptions(CloudStorageTrackingVerbosity.Summarised));

        var request = new HttpRequestMessage(HttpMethod.Get, "https://storage.googleapis.com/discovery/v1/apis");
        await invoker.SendAsync(request, CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Empty(logs);
    }

    // ─── Raw verbosity ────────────────────────────────────────

    [Fact]
    public async Task Raw_UsesHttpMethodAsMethod()
    {
        using var invoker = CreateInvoker(MakeOptions(CloudStorageTrackingVerbosity.Raw));

        await invoker.SendAsync(MakeUploadRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal(HttpMethod.Post, log.Method.Value);
    }

    [Fact]
    public async Task Raw_UsesOriginalUri()
    {
        using var invoker = CreateInvoker(MakeOptions(CloudStorageTrackingVerbosity.Raw));

        await invoker.SendAsync(MakeUploadRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains("googleapis.com", log.Uri.ToString());
    }

    // ─── Phase variants ───────────────────────────────────────

    // A phase's own verbosity decides its notes: a Detailed phase of a Summarised handler shows the bodies a Detailed
    // handler shows, though the call was made before its phase was known.
    [Fact]
    public async Task A_detailed_phase_of_a_summarised_handler_shows_both_bodies()
    {
        TestPhaseContext.Reset();
        var options = MakeOptions(CloudStorageTrackingVerbosity.Summarised);
        options.ActionVerbosity = CloudStorageTrackingVerbosity.Detailed;
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeUploadRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        var request = logs.First(l => l.Type == RequestResponseType.Request);
        var response = logs.First(l => l.Type == RequestResponseType.Response);
        Assert.Null(request.Content);
        Assert.Null(request.SetupVariant!.Content);
        Assert.Equal("file-content", request.ActionVariant!.Content);
        Assert.Null(response.Content);
        Assert.Null(response.SetupVariant!.Content);
        Assert.Contains("storage#object", response.ActionVariant!.Content);
    }

    [Fact]
    public async Task At_Summarised_in_every_phase_a_body_nothing_shows_is_not_read()
    {
        TestPhaseContext.Reset();
        var reply = new CountingContent("""{"kind": "storage#object", "name": "file.txt"}""");
        _innerHandler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK) { Content = reply };
        var options = MakeOptions(CloudStorageTrackingVerbosity.Summarised);
        options.SetupVerbosity = CloudStorageTrackingVerbosity.Summarised;
        using var invoker = CreateInvoker(options);
        var upload = new CountingContent("file-content");

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Post,
            "https://storage.googleapis.com/upload/storage/v1/b/my-bucket/o?uploadType=multipart") { Content = upload }, CancellationToken.None);

        Assert.Equal(0, upload.Reads);
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
        using var invoker = CreateInvoker(MakeOptions(CloudStorageTrackingVerbosity.Detailed));
        var request = MakeDownloadRequest();
        request.Headers.Add("x-goog-api-client", "gl-dotnet/8.0");
        request.Headers.Add("x-custom-header", "keep-me");

        await invoker.SendAsync(request, CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.DoesNotContain(log.Headers, h => h.Key == "x-goog-api-client");
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

        await invoker.SendAsync(MakeUploadRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Equal(HttpStatusCode.OK, log.StatusCode?.Value);
    }

    // ─── ITrackingComponent ────────────────────────────────────

    [Fact]
    public void Implements_ITrackingComponent()
    {
        var handler = new CloudStorageTrackingMessageHandler(MakeOptions());
        Assert.IsAssignableFrom<ITrackingComponent>(handler);
    }

    [Fact]
    public void WasInvoked_IsFalse_BeforeAnyRequests()
    {
        var handler = new CloudStorageTrackingMessageHandler(MakeOptions());
        Assert.False(handler.WasInvoked);
    }

    [Fact]
    public async Task WasInvoked_IsTrue_AfterRequest()
    {
        var handler = new CloudStorageTrackingMessageHandler(MakeOptions(), _innerHandler);
        using var invoker = new HttpMessageInvoker(handler);
        await invoker.SendAsync(MakeUploadRequest(), CancellationToken.None);

        Assert.True(handler.WasInvoked);
    }

    [Fact]
    public void InvocationCount_StartsAtZero()
    {
        var handler = new CloudStorageTrackingMessageHandler(MakeOptions());
        Assert.Equal(0, handler.InvocationCount);
    }

    [Fact]
    public async Task InvocationCount_IncreasesWithEachCall()
    {
        var handler = new CloudStorageTrackingMessageHandler(MakeOptions(), _innerHandler);
        using var invoker = new HttpMessageInvoker(handler);

        await invoker.SendAsync(MakeUploadRequest(), CancellationToken.None);
        await invoker.SendAsync(MakeDownloadRequest(), CancellationToken.None);
        await invoker.SendAsync(MakeDeleteRequest(), CancellationToken.None);

        Assert.Equal(3, handler.InvocationCount);
    }

    [Fact]
    public void ComponentName_MatchesServiceName()
    {
        var handler = new CloudStorageTrackingMessageHandler(MakeOptions(serviceName: "MyGCS"));
        Assert.Equal("CloudStorageTrackingMessageHandler (MyGCS)", handler.ComponentName);
    }

    [Fact]
    public void Constructor_AutoRegistersWithTrackingComponentRegistry()
    {
        TrackingComponentRegistry.Clear();
        var handler = new CloudStorageTrackingMessageHandler(MakeOptions());

        var components = TrackingComponentRegistry.GetRegisteredComponents();
        Assert.Contains(components, c => ReferenceEquals(c, handler));
    }
}
