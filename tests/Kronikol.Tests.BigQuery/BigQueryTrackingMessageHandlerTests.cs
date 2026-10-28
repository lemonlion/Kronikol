using System.Net;
using Kronikol.Extensions.BigQuery;
using Kronikol.Tracking;
using Kronikol.Tests.History;

namespace Kronikol.Tests.BigQuery;

public class BigQueryTrackingMessageHandlerTests : IDisposable
{
    private const string BaseUrl = "https://bigquery.googleapis.com/bigquery/v2/projects/my-project";
    private readonly StubInnerHandler _innerHandler = new();
    private readonly string _testId = Guid.NewGuid().ToString();

    private RequestResponseLog[] GetLogsFromThisTest()
    {
        return RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == _testId)
            .ToArray();
    }

    private HttpMessageInvoker CreateInvoker(BigQueryTrackingMessageHandlerOptions options)
    {
        var handler = new BigQueryTrackingMessageHandler(options, _innerHandler);
        return new HttpMessageInvoker(handler);
    }

    private BigQueryTrackingMessageHandlerOptions MakeOptions(
        BigQueryTrackingVerbosity verbosity = BigQueryTrackingVerbosity.Detailed,
        string serviceName = "BigQuery",
        string callerName = "TestCaller") => new()
    {
        ServiceName = serviceName,
        CallerName = callerName,
        Verbosity = verbosity,
        CurrentTestInfoFetcher = () => ("My Test", _testId),
    };

    private static HttpRequestMessage MakeQueryRequest()
    {
        return new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/queries")
        {
            Content = new StringContent("""{"query":"SELECT * FROM dataset.table","useLegacySql":false}""")
        };
    }

    private static HttpRequestMessage MakeInsertRequest()
    {
        return new HttpRequestMessage(HttpMethod.Post,
            $"{BaseUrl}/datasets/mydataset/tables/mytable/insertAll")
        {
            Content = new StringContent("""{"rows":[{"json":{"id":"1","name":"test"}}]}""")
        };
    }

    private static HttpRequestMessage MakeReadTableRequest()
    {
        return new HttpRequestMessage(HttpMethod.Get,
            $"{BaseUrl}/datasets/mydataset/tables/mytable");
    }

    private static HttpRequestMessage MakeUnrecognisedRequest()
    {
        return new HttpRequestMessage(HttpMethod.Get,
            "https://bigquery.googleapis.com/discovery/v1/apis");
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
        await invoker.SendAsync(MakeQueryRequest(), CancellationToken.None);

        HistoryGuard.Pin(GetLogsFromThisTest(), 8,
            "TestCaller>BigQuery Query /query SELECT * FROM dataset.table 200");
    }

    [Fact]
    public async Task Logs_request_and_response_for_each_call()
    {
        using var invoker = CreateInvoker(MakeOptions());

        await invoker.SendAsync(MakeQueryRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal(RequestResponseType.Request, logs[0].Type);
        Assert.Equal(RequestResponseType.Response, logs[1].Type);
    }

    [Fact]
    public async Task Logs_correct_service_and_caller_names()
    {
        using var invoker = CreateInvoker(MakeOptions(callerName: "MyApi", serviceName: "OrdersBigQuery"));

        await invoker.SendAsync(MakeQueryRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Equal("OrdersBigQuery", logs[0].ServiceName);
        Assert.Equal("MyApi", logs[0].CallerName);
    }

    [Fact]
    public async Task Does_not_log_when_no_test_info_fetcher()
    {
        var options = MakeOptions();
        options.CurrentTestInfoFetcher = null;
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeQueryRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Empty(logs);
    }

    [Fact]
    public async Task Request_is_still_forwarded_when_no_test_info_fetcher()
    {
        var options = MakeOptions();
        options.CurrentTestInfoFetcher = null;
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeQueryRequest(), CancellationToken.None);

        Assert.NotNull(_innerHandler.CapturedRequest);
    }

    [Fact]
    public async Task Request_and_response_share_same_traceId_and_requestResponseId()
    {
        using var invoker = CreateInvoker(MakeOptions());

        await invoker.SendAsync(MakeQueryRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Equal(logs[0].TraceId, logs[1].TraceId);
        Assert.Equal(logs[0].RequestResponseId, logs[1].RequestResponseId);
    }

    // ─── Detailed verbosity ────────────────────────────────────

    [Fact]
    public async Task Detailed_Query_UsesClassifiedLabel()
    {
        using var invoker = CreateInvoker(MakeOptions(BigQueryTrackingVerbosity.Detailed));

        await invoker.SendAsync(MakeQueryRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("Query", log.Method.Value?.ToString());
    }

    [Fact]
    public async Task Detailed_Insert_UsesClassifiedLabel()
    {
        using var invoker = CreateInvoker(MakeOptions(BigQueryTrackingVerbosity.Detailed));

        await invoker.SendAsync(MakeInsertRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("Insert", log.Method.Value?.ToString());
    }

    [Fact]
    public async Task Detailed_Read_UsesClassifiedLabel()
    {
        using var invoker = CreateInvoker(MakeOptions(BigQueryTrackingVerbosity.Detailed));

        await invoker.SendAsync(MakeReadTableRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("Read", log.Method.Value?.ToString());
    }

    [Fact]
    public async Task Detailed_IncludesRequestContent()
    {
        using var invoker = CreateInvoker(MakeOptions(BigQueryTrackingVerbosity.Detailed));

        await invoker.SendAsync(MakeQueryRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains("SELECT * FROM dataset.table", log.Content);
    }

    [Fact]
    public async Task Detailed_IncludesResponseContent()
    {
        using var invoker = CreateInvoker(MakeOptions(BigQueryTrackingVerbosity.Detailed));

        await invoker.SendAsync(MakeReadTableRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Contains("queryResponse", log.Content);
    }

    [Fact]
    public async Task Detailed_BuildsCleanUri()
    {
        using var invoker = CreateInvoker(MakeOptions(BigQueryTrackingVerbosity.Detailed));

        await invoker.SendAsync(MakeReadTableRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains("mydataset", log.Uri.AbsolutePath);
        Assert.Contains("mytable", log.Uri.AbsolutePath);
    }

    // ─── Summarised verbosity ──────────────────────────────────

    [Fact]
    public async Task Summarised_UsesOperationNameOnly()
    {
        using var invoker = CreateInvoker(MakeOptions(BigQueryTrackingVerbosity.Summarised));

        await invoker.SendAsync(MakeQueryRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("Query", log.Method.Value?.ToString());
    }

    [Fact]
    public async Task Summarised_OmitsRequestContent()
    {
        using var invoker = CreateInvoker(MakeOptions(BigQueryTrackingVerbosity.Summarised));

        await invoker.SendAsync(MakeQueryRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Null(log.Content);
    }

    [Fact]
    public async Task Summarised_OmitsResponseContent()
    {
        using var invoker = CreateInvoker(MakeOptions(BigQueryTrackingVerbosity.Summarised));

        await invoker.SendAsync(MakeReadTableRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Null(log.Content);
    }

    [Fact]
    public async Task Summarised_OmitsHeaders()
    {
        using var invoker = CreateInvoker(MakeOptions(BigQueryTrackingVerbosity.Summarised));

        await invoker.SendAsync(MakeReadTableRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Empty(log.Headers);
    }

    [Fact]
    public async Task Summarised_SkipsOtherOperations()
    {
        using var invoker = CreateInvoker(MakeOptions(BigQueryTrackingVerbosity.Summarised));

        await invoker.SendAsync(MakeUnrecognisedRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Empty(logs);
    }

    [Fact]
    public async Task Summarised_BuildsSimpleUri()
    {
        using var invoker = CreateInvoker(MakeOptions(BigQueryTrackingVerbosity.Summarised));

        await invoker.SendAsync(MakeInsertRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        // Summarised: just resource type and name
        Assert.Contains("mytable", log.Uri.AbsolutePath);
    }

    // ─── Raw verbosity ────────────────────────────────────────

    [Fact]
    public async Task Raw_UsesHttpMethodAsMethod()
    {
        using var invoker = CreateInvoker(MakeOptions(BigQueryTrackingVerbosity.Raw));

        await invoker.SendAsync(MakeQueryRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal(HttpMethod.Post, log.Method.Value);
    }

    [Fact]
    public async Task Raw_IncludesFullContent()
    {
        using var invoker = CreateInvoker(MakeOptions(BigQueryTrackingVerbosity.Raw));

        await invoker.SendAsync(MakeInsertRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains("rows", log.Content);
    }

    [Fact]
    public async Task Raw_DoesNotSkipOtherOperations()
    {
        using var invoker = CreateInvoker(MakeOptions(BigQueryTrackingVerbosity.Raw));

        await invoker.SendAsync(MakeUnrecognisedRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
    }

    [Fact]
    public async Task Raw_UsesOriginalUri()
    {
        using var invoker = CreateInvoker(MakeOptions(BigQueryTrackingVerbosity.Raw));

        await invoker.SendAsync(MakeReadTableRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal($"{BaseUrl}/datasets/mydataset/tables/mytable", log.Uri.ToString());
    }

    // ─── Phase variants ───────────────────────────────────────

    // A phase's own verbosity decides its notes: a Detailed phase of a Summarised handler shows the bodies a Detailed
    // handler shows, though the call was made before its phase was known.
    [Fact]
    public async Task A_detailed_phase_of_a_summarised_handler_shows_both_bodies()
    {
        TestPhaseContext.Reset();
        var options = MakeOptions(BigQueryTrackingVerbosity.Summarised);
        options.ActionVerbosity = BigQueryTrackingVerbosity.Detailed;
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeQueryRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        var request = logs.First(l => l.Type == RequestResponseType.Request);
        var response = logs.First(l => l.Type == RequestResponseType.Response);
        Assert.Null(request.Content);
        Assert.Null(request.SetupVariant!.Content);
        Assert.Contains("SELECT * FROM dataset.table", request.ActionVariant!.Content);
        Assert.Null(response.Content);
        Assert.Null(response.SetupVariant!.Content);
        Assert.Contains("bigquery#queryResponse", response.ActionVariant!.Content);
    }

    [Fact]
    public async Task At_Summarised_in_every_phase_a_body_nothing_shows_is_not_read()
    {
        TestPhaseContext.Reset();
        var reply = new CountingContent("""{"kind":"bigquery#queryResponse","rows":[]}""");
        var options = MakeOptions(BigQueryTrackingVerbosity.Summarised);
        options.SetupVerbosity = BigQueryTrackingVerbosity.Summarised;
        using var invoker = new HttpMessageInvoker(new BigQueryTrackingMessageHandler(options,
            new StubInnerHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = reply })));
        var query = new CountingContent("""{"query":"SELECT * FROM dataset.table","useLegacySql":false}""");

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/queries") { Content = query }, CancellationToken.None);

        Assert.Equal(0, query.Reads);
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
        using var invoker = CreateInvoker(MakeOptions(BigQueryTrackingVerbosity.Detailed));
        var request = MakeReadTableRequest();
        request.Headers.Add("x-goog-api-client", "gl-dotnet/10.0.0");
        request.Headers.Add("x-custom-header", "keep-me");

        await invoker.SendAsync(request, CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.DoesNotContain(log.Headers, h => h.Key == "x-goog-api-client");
        Assert.Contains(log.Headers, h => h.Key == "x-custom-header");
    }

    // ─── StatusCode ───────────────────────────────────────────

    [Fact]
    public async Task Response_log_includes_status_code()
    {
        using var invoker = CreateInvoker(MakeOptions());

        await invoker.SendAsync(MakeQueryRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.NotNull(log.StatusCode);
        Assert.Equal(HttpStatusCode.OK, log.StatusCode!.Value);
    }

    // ─── ITrackingComponent ────────────────────────────────────

    [Fact]
    public void Implements_ITrackingComponent()
    {
        var handler = new BigQueryTrackingMessageHandler(MakeOptions());
        Assert.IsAssignableFrom<ITrackingComponent>(handler);
    }

    [Fact]
    public void WasInvoked_IsFalse_BeforeAnyRequests()
    {
        var handler = new BigQueryTrackingMessageHandler(MakeOptions());
        Assert.False(handler.WasInvoked);
    }

    [Fact]
    public async Task WasInvoked_IsTrue_AfterRequest()
    {
        var inner = new StubInnerHandler();
        var handler = new BigQueryTrackingMessageHandler(MakeOptions(), inner);
        using var invoker = new HttpMessageInvoker(handler);
        await invoker.SendAsync(MakeQueryRequest(), CancellationToken.None);

        Assert.True(handler.WasInvoked);
    }

    [Fact]
    public void InvocationCount_StartsAtZero()
    {
        var handler = new BigQueryTrackingMessageHandler(MakeOptions());
        Assert.Equal(0, handler.InvocationCount);
    }

    [Fact]
    public void ComponentName_MatchesServiceName()
    {
        var handler = new BigQueryTrackingMessageHandler(MakeOptions(serviceName: "MyBQ"));
        Assert.Equal("BigQueryTrackingMessageHandler (MyBQ)", handler.ComponentName);
    }

    [Fact]
    public void Constructor_AutoRegistersWithTrackingComponentRegistry()
    {
        TrackingComponentRegistry.Clear();
        var handler = new BigQueryTrackingMessageHandler(MakeOptions());

        var components = TrackingComponentRegistry.GetRegisteredComponents();
        Assert.Contains(components, c => ReferenceEquals(c, handler));
    }

    // ─── Gzip decompression ────────────────────────────────────

    [Fact]
    public async Task Decompresses_gzip_request_body_before_logging()
    {
        var json = """{"rows":[{"json":{"id":"1","name":"gzipped"}}]}""";
        var compressed = GzipCompress(json);

        var content = new ByteArrayContent(compressed);
        content.Headers.ContentEncoding.Add("gzip");
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        var request = new HttpRequestMessage(HttpMethod.Post,
            $"{BaseUrl}/datasets/mydataset/tables/mytable/insertAll")
        {
            Content = content
        };

        using var invoker = CreateInvoker(MakeOptions());
        await invoker.SendAsync(request, CancellationToken.None);

        var logs = GetLogsFromThisTest();
        var requestLog = logs.First(l => l.Type == RequestResponseType.Request);

        Assert.Contains("gzipped", requestLog.Content!);
    }

    [Fact]
    public async Task Decompresses_gzip_response_body_before_logging()
    {
        var json = """{"kind":"bigquery#tableDataInsertAllResponse","insertErrors":null}""";
        var compressed = GzipCompress(json);

        var responseContent = new ByteArrayContent(compressed);
        responseContent.Headers.ContentEncoding.Add("gzip");
        responseContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = responseContent };
        var innerHandler = new StubInnerHandler(response);
        var handler = new BigQueryTrackingMessageHandler(MakeOptions(), innerHandler);
        using var invoker = new HttpMessageInvoker(handler);

        var request = new HttpRequestMessage(HttpMethod.Post,
            $"{BaseUrl}/datasets/mydataset/tables/mytable/insertAll")
        {
            Content = new StringContent("""{"rows":[]}""")
        };
        await invoker.SendAsync(request, CancellationToken.None);

        var logs = GetLogsFromThisTest();
        var responseLog = logs.First(l => l.Type == RequestResponseType.Response);

        Assert.Contains("insertErrors", responseLog.Content!);
    }

    private static byte[] GzipCompress(string text)
    {
        using var output = new System.IO.MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.Optimal))
        using (var writer = new System.IO.StreamWriter(gzip))
            writer.Write(text);
        return output.ToArray();
    }
}
