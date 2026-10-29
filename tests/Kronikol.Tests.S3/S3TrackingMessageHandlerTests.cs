using System.Net;
using Kronikol.Extensions.S3;
using Kronikol.Tracking;
using Kronikol.Tests.History;

namespace Kronikol.Tests.S3;

public class S3TrackingMessageHandlerTests : IDisposable
{
    // ─── Test infrastructure ────────────────────────────────────

    private class StubInnerHandler : HttpMessageHandler
    {
        public HttpRequestMessage? CapturedRequest { get; private set; }
        public HttpResponseMessage ResponseToReturn { get; set; } = new(HttpStatusCode.OK) { Content = new StringContent("s3-response-body") };

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

    private HttpMessageInvoker CreateInvoker(S3TrackingMessageHandlerOptions options)
    {
        var handler = new S3TrackingMessageHandler(options, _innerHandler);
        return new HttpMessageInvoker(handler);
    }

    private S3TrackingMessageHandlerOptions MakeOptions(
        S3TrackingVerbosity verbosity = S3TrackingVerbosity.Detailed,
        string serviceName = "S3",
        string callerName = "TestCaller") => new()
    {
        ServiceName = serviceName,
        CallerName = callerName,
        Verbosity = verbosity,
        CurrentTestInfoFetcher = () => ("My Test", _testId),
    };

    private static HttpRequestMessage MakePutObjectRequest()
    {
        return new HttpRequestMessage(HttpMethod.Put,
            "https://my-bucket.s3.us-east-1.amazonaws.com/path/to/file.json")
        {
            Content = new StringContent("{\"hello\":\"world\"}")
        };
    }

    private static HttpRequestMessage MakeGetObjectRequest()
    {
        return new HttpRequestMessage(HttpMethod.Get,
            "https://my-bucket.s3.us-east-1.amazonaws.com/path/to/file.json");
    }

    private static HttpRequestMessage MakeDeleteObjectRequest()
    {
        return new HttpRequestMessage(HttpMethod.Delete,
            "https://my-bucket.s3.us-east-1.amazonaws.com/path/to/file.json");
    }

    private static HttpRequestMessage MakeListObjectsRequest()
    {
        return new HttpRequestMessage(HttpMethod.Get,
            "https://my-bucket.s3.us-east-1.amazonaws.com/?list-type=2");
    }

    private static HttpRequestMessage MakeCreateBucketRequest()
    {
        return new HttpRequestMessage(HttpMethod.Put,
            "https://new-bucket.s3.us-east-1.amazonaws.com/");
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
        await invoker.SendAsync(MakeGetObjectRequest(), CancellationToken.None);

        HistoryGuard.Pin(GetLogsFromThisTest(), 10,
            "TestCaller>S3 GetObject /my-bucket/path/to/file.json 200");
    }

    [Fact]
    public async Task Logs_request_and_response_for_each_call()
    {
        using var invoker = CreateInvoker(MakeOptions());

        await invoker.SendAsync(MakePutObjectRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal(RequestResponseType.Request, logs[0].Type);
        Assert.Equal(RequestResponseType.Response, logs[1].Type);
    }

    [Fact]
    public async Task Logs_correct_service_and_caller_names()
    {
        using var invoker = CreateInvoker(MakeOptions(callerName: "MyApi", serviceName: "DocumentsS3"));

        await invoker.SendAsync(MakePutObjectRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Equal("DocumentsS3", logs[0].ServiceName);
        Assert.Equal("MyApi", logs[0].CallerName);
    }

    [Fact]
    public async Task Does_not_log_when_no_test_info_fetcher()
    {
        var options = MakeOptions();
        options.CurrentTestInfoFetcher = null;
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakePutObjectRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Empty(logs);
    }

    [Fact]
    public async Task Request_is_still_forwarded_when_no_test_info_fetcher()
    {
        var options = MakeOptions();
        options.CurrentTestInfoFetcher = null;
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakePutObjectRequest(), CancellationToken.None);

        Assert.NotNull(_innerHandler.CapturedRequest);
    }

    // ─── Detailed verbosity ────────────────────────────────────

    [Fact]
    public async Task Detailed_PutObject_UsesClassifiedLabel()
    {
        using var invoker = CreateInvoker(MakeOptions(S3TrackingVerbosity.Detailed));

        await invoker.SendAsync(MakePutObjectRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("PutObject", log.Method.Value?.ToString());
    }

    [Fact]
    public async Task Detailed_GetObject_UsesClassifiedLabel()
    {
        using var invoker = CreateInvoker(MakeOptions(S3TrackingVerbosity.Detailed));

        await invoker.SendAsync(MakeGetObjectRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("GetObject", log.Method.Value?.ToString());
    }

    [Fact]
    public async Task Detailed_IncludesResponseContent()
    {
        using var invoker = CreateInvoker(MakeOptions(S3TrackingVerbosity.Detailed));

        await invoker.SendAsync(MakeGetObjectRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Contains("s3-response-body", log.Content);
    }

    [Fact]
    public async Task Detailed_UsesS3UriScheme()
    {
        using var invoker = CreateInvoker(MakeOptions(S3TrackingVerbosity.Detailed));

        await invoker.SendAsync(MakeGetObjectRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.StartsWith("s3://", log.Uri.ToString());
    }

    [Fact]
    public async Task Detailed_IncludesBucketAndKeyInUri()
    {
        using var invoker = CreateInvoker(MakeOptions(S3TrackingVerbosity.Detailed));

        await invoker.SendAsync(MakeGetObjectRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains("my-bucket", log.Uri.ToString());
        Assert.Contains("path/to/file.json", log.Uri.ToString());
    }

    // ─── Summarised verbosity ──────────────────────────────────

    [Fact]
    public async Task Summarised_UsesOperationNameOnly()
    {
        using var invoker = CreateInvoker(MakeOptions(S3TrackingVerbosity.Summarised));

        await invoker.SendAsync(MakePutObjectRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("PutObject", log.Method.Value?.ToString());
    }

    [Fact]
    public async Task Summarised_OmitsRequestContent()
    {
        using var invoker = CreateInvoker(MakeOptions(S3TrackingVerbosity.Summarised));

        await invoker.SendAsync(MakePutObjectRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Null(log.Content);
    }

    [Fact]
    public async Task Summarised_OmitsResponseContent()
    {
        using var invoker = CreateInvoker(MakeOptions(S3TrackingVerbosity.Summarised));

        await invoker.SendAsync(MakeGetObjectRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Null(log.Content);
    }

    [Fact]
    public async Task Summarised_OmitsHeaders()
    {
        using var invoker = CreateInvoker(MakeOptions(S3TrackingVerbosity.Summarised));

        await invoker.SendAsync(MakeGetObjectRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Empty(log.Headers);
    }

    [Fact]
    public async Task Summarised_SkipsOtherOperations()
    {
        using var invoker = CreateInvoker(MakeOptions(S3TrackingVerbosity.Summarised));

        // PATCH is not a valid S3 method → Other
        var request = new HttpRequestMessage(HttpMethod.Patch,
            "https://my-bucket.s3.us-east-1.amazonaws.com/file.txt");
        await invoker.SendAsync(request, CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Empty(logs);
    }

    [Fact]
    public async Task Summarised_UsesBucketOnlyUri()
    {
        using var invoker = CreateInvoker(MakeOptions(S3TrackingVerbosity.Summarised));

        await invoker.SendAsync(MakePutObjectRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("s3://my-bucket/", log.Uri.ToString());
    }

    // ─── Raw verbosity ────────────────────────────────────────

    [Fact]
    public async Task Raw_UsesHttpMethodAsMethod()
    {
        using var invoker = CreateInvoker(MakeOptions(S3TrackingVerbosity.Raw));

        await invoker.SendAsync(MakePutObjectRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal(HttpMethod.Put, log.Method.Value);
    }

    [Fact]
    public async Task Raw_IncludesFullContent()
    {
        using var invoker = CreateInvoker(MakeOptions(S3TrackingVerbosity.Raw));

        await invoker.SendAsync(MakePutObjectRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains("{\"hello\":\"world\"}", log.Content);
    }

    [Fact]
    public async Task Raw_DoesNotSkipOtherOperations()
    {
        using var invoker = CreateInvoker(MakeOptions(S3TrackingVerbosity.Raw));

        var request = new HttpRequestMessage(HttpMethod.Patch,
            "https://my-bucket.s3.us-east-1.amazonaws.com/file.txt");
        await invoker.SendAsync(request, CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
    }

    [Fact]
    public async Task Raw_UsesOriginalUri()
    {
        using var invoker = CreateInvoker(MakeOptions(S3TrackingVerbosity.Raw));

        await invoker.SendAsync(MakeGetObjectRequest(), CancellationToken.None);

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
        var options = MakeOptions(S3TrackingVerbosity.Summarised);
        options.ActionVerbosity = S3TrackingVerbosity.Detailed;
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakePutObjectRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        var request = logs.First(l => l.Type == RequestResponseType.Request);
        var response = logs.First(l => l.Type == RequestResponseType.Response);
        Assert.Null(request.Content);
        Assert.Null(request.SetupVariant!.Content);
        Assert.Equal("{\"hello\":\"world\"}", request.ActionVariant!.Content);
        Assert.Null(response.Content);
        Assert.Null(response.SetupVariant!.Content);
        Assert.Equal("s3-response-body", response.ActionVariant!.Content);
    }

    [Fact]
    public async Task At_Summarised_in_every_phase_a_body_nothing_shows_is_not_read()
    {
        TestPhaseContext.Reset();
        var reply = new CountingContent("s3-response-body");
        _innerHandler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK) { Content = reply };
        var options = MakeOptions(S3TrackingVerbosity.Summarised);
        options.SetupVerbosity = S3TrackingVerbosity.Summarised;
        using var invoker = CreateInvoker(options);
        var upload = new CountingContent("{\"hello\":\"world\"}");

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Put,
            "https://my-bucket.s3.us-east-1.amazonaws.com/path/to/file.json") { Content = upload }, CancellationToken.None);

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
        using var invoker = CreateInvoker(MakeOptions(S3TrackingVerbosity.Detailed));
        var request = MakeGetObjectRequest();
        request.Headers.Add("x-amz-date", "20240101T000000Z");
        request.Headers.Add("x-amz-custom-header", "keep-me");

        await invoker.SendAsync(request, CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.DoesNotContain(log.Headers, h => h.Key == "x-amz-date");
        Assert.Contains(log.Headers, h => h.Key == "x-amz-custom-header");
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

        await invoker.SendAsync(MakePutObjectRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Equal(HttpStatusCode.Created, log.StatusCode?.Value);
    }

    // ─── ITrackingComponent ────────────────────────────────────

    [Fact]
    public void Implements_ITrackingComponent()
    {
        var handler = new S3TrackingMessageHandler(MakeOptions());
        Assert.IsAssignableFrom<ITrackingComponent>(handler);
    }

    [Fact]
    public void WasInvoked_IsFalse_BeforeAnyRequests()
    {
        var handler = new S3TrackingMessageHandler(MakeOptions());
        Assert.False(handler.WasInvoked);
    }

    [Fact]
    public async Task WasInvoked_IsTrue_AfterRequest()
    {
        var handler = new S3TrackingMessageHandler(MakeOptions(), _innerHandler);
        using var invoker = new HttpMessageInvoker(handler);
        await invoker.SendAsync(MakePutObjectRequest(), CancellationToken.None);

        Assert.True(handler.WasInvoked);
    }

    [Fact]
    public void InvocationCount_StartsAtZero()
    {
        var handler = new S3TrackingMessageHandler(MakeOptions());
        Assert.Equal(0, handler.InvocationCount);
    }

    [Fact]
    public async Task InvocationCount_IncreasesWithEachCall()
    {
        var handler = new S3TrackingMessageHandler(MakeOptions(), _innerHandler);
        using var invoker = new HttpMessageInvoker(handler);

        await invoker.SendAsync(MakePutObjectRequest(), CancellationToken.None);
        await invoker.SendAsync(MakeGetObjectRequest(), CancellationToken.None);
        await invoker.SendAsync(MakeDeleteObjectRequest(), CancellationToken.None);

        Assert.Equal(3, handler.InvocationCount);
    }

    [Fact]
    public void ComponentName_MatchesServiceName()
    {
        var handler = new S3TrackingMessageHandler(MakeOptions(serviceName: "MyS3"));
        Assert.Equal("S3TrackingMessageHandler (MyS3)", handler.ComponentName);
    }

    [Fact]
    public void Constructor_AutoRegistersWithTrackingComponentRegistry()
    {
        TrackingComponentRegistry.Clear();
        var handler = new S3TrackingMessageHandler(MakeOptions());

        var components = TrackingComponentRegistry.GetRegisteredComponents();
        Assert.Contains(components, c => ReferenceEquals(c, handler));
    }
}
