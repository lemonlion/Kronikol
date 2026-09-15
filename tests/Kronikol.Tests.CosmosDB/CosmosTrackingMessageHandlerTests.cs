using System.Net;
using Kronikol.Extensions.CosmosDB;
using Kronikol.Tracking;

namespace Kronikol.Tests.CosmosDB;

[Collection("TestCorrelationStore")]
public class CosmosTrackingMessageHandlerTests : IDisposable
{
    // ─── Test infrastructure ────────────────────────────────────

    private class StubInnerHandler : HttpMessageHandler
    {
        public HttpRequestMessage? CapturedRequest { get; private set; }
        public HttpResponseMessage ResponseToReturn { get; set; } = new(HttpStatusCode.OK) { Content = new StringContent("""{"id":"doc1","_rid":"abc=="}""") };

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

    private HttpMessageInvoker CreateInvoker(CosmosTrackingMessageHandlerOptions options)
    {
        var handler = new CosmosTrackingMessageHandler(options, _innerHandler);
        return new HttpMessageInvoker(handler);
    }

    private CosmosTrackingMessageHandlerOptions MakeOptions(
        CosmosTrackingVerbosity verbosity = CosmosTrackingVerbosity.Detailed,
        string serviceName = "CosmosDB",
        string callerName = "TestCaller") => new()
    {
        ServiceName = serviceName,
        CallerName = callerName,
        Verbosity = verbosity,
        CurrentTestInfoFetcher = () => ("My Test", _testId),
    };

    private static HttpRequestMessage MakeCreateRequest()
    {
        return new HttpRequestMessage(HttpMethod.Post,
            "https://account.documents.azure.com/dbs/mydb/colls/orders/docs")
        {
            Content = new StringContent("""{"id":"order-1","total":42.50}""")
        };
    }

    private static HttpRequestMessage MakeReadRequest()
    {
        return new HttpRequestMessage(HttpMethod.Get,
            "https://account.documents.azure.com/dbs/mydb/colls/orders/docs/order-1");
    }

    private static HttpRequestMessage MakeQueryRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            "https://account.documents.azure.com/dbs/mydb/colls/orders/docs")
        {
            Content = new StringContent("""{"query":"SELECT * FROM c WHERE c.status = 'active'","parameters":[]}""")
        };
        request.Headers.Add("x-ms-documentdb-isquery", "True");
        return request;
    }

    private static HttpRequestMessage MakeDeleteRequest()
    {
        return new HttpRequestMessage(HttpMethod.Delete,
            "https://account.documents.azure.com/dbs/mydb/colls/orders/docs/order-1");
    }

    private static HttpRequestMessage MakeMetadataRequest()
    {
        return new HttpRequestMessage(HttpMethod.Get,
            "https://account.documents.azure.com/dbs/mydb/colls/orders/pkranges");
    }

    /// <summary>
    /// The plan fetch the SDK makes before running a query: a POST to the documents resource carrying the
    /// query text, marked by its own header and not by the query header.
    /// </summary>
    private static HttpRequestMessage MakeQueryPlanRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            "https://account.documents.azure.com/dbs/mydb/colls/orders/docs")
        {
            Content = new StringContent("""{"query":"SELECT * FROM c WHERE c.status = 'active'","parameters":[]}""")
        };
        request.Headers.Add("x-ms-cosmos-is-query-plan-request", "True");
        return request;
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

        await invoker.SendAsync(MakeCreateRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal(RequestResponseType.Request, logs[0].Type);
        Assert.Equal(RequestResponseType.Response, logs[1].Type);
    }

    [Fact]
    public async Task Logs_correct_service_and_caller_names()
    {
        using var invoker = CreateInvoker(MakeOptions(callerName: "MyApi", serviceName: "OrdersCosmosDB"));

        await invoker.SendAsync(MakeCreateRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Equal("OrdersCosmosDB", logs[0].ServiceName);
        Assert.Equal("MyApi", logs[0].CallerName);
    }

    [Fact]
    public async Task Does_not_log_when_no_test_info_fetcher()
    {
        var options = MakeOptions();
        options.CurrentTestInfoFetcher = null;
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeCreateRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Empty(logs);
    }

    [Fact]
    public async Task Request_is_still_forwarded_when_no_test_info_fetcher()
    {
        var options = MakeOptions();
        options.CurrentTestInfoFetcher = null;
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeCreateRequest(), CancellationToken.None);

        Assert.NotNull(_innerHandler.CapturedRequest);
    }

    // ─── Detailed verbosity ────────────────────────────────────

    [Fact]
    public async Task Detailed_Create_UsesClassifiedLabel()
    {
        using var invoker = CreateInvoker(MakeOptions(CosmosTrackingVerbosity.Detailed));

        await invoker.SendAsync(MakeCreateRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("Create", log.Method.Value?.ToString());
    }

    [Fact]
    public async Task Detailed_Read_UsesClassifiedLabel()
    {
        using var invoker = CreateInvoker(MakeOptions(CosmosTrackingVerbosity.Detailed));

        await invoker.SendAsync(MakeReadRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("Read", log.Method.Value?.ToString());
    }

    [Fact]
    public async Task Detailed_Query_ShowsQueryTextAsContent()
    {
        using var invoker = CreateInvoker(MakeOptions(CosmosTrackingVerbosity.Detailed));

        await invoker.SendAsync(MakeQueryRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("Query", log.Method.Value?.ToString());
        Assert.Equal("SELECT * FROM c WHERE c.status = 'active'", log.Content);
    }

    [Fact]
    public async Task Detailed_IncludesResponseContent()
    {
        using var invoker = CreateInvoker(MakeOptions(CosmosTrackingVerbosity.Detailed));

        await invoker.SendAsync(MakeReadRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Contains("doc1", log.Content);
    }

    // ─── Summarised verbosity ──────────────────────────────────

    [Fact]
    public async Task Summarised_UsesOperationNameOnly()
    {
        using var invoker = CreateInvoker(MakeOptions(CosmosTrackingVerbosity.Summarised));

        await invoker.SendAsync(MakeCreateRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("Create", log.Method.Value?.ToString());
    }

    [Fact]
    public async Task Summarised_OmitsRequestContent()
    {
        using var invoker = CreateInvoker(MakeOptions(CosmosTrackingVerbosity.Summarised));

        await invoker.SendAsync(MakeCreateRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Null(log.Content);
    }

    [Fact]
    public async Task Summarised_OmitsResponseContent()
    {
        var options = MakeOptions(CosmosTrackingVerbosity.Summarised);
        options.LogResponseContent = false;
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeReadRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Null(log.Content);
    }

    [Fact]
    public async Task Summarised_OmitsHeaders()
    {
        using var invoker = CreateInvoker(MakeOptions(CosmosTrackingVerbosity.Summarised));

        await invoker.SendAsync(MakeReadRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Empty(log.Headers);
    }

    [Fact]
    public async Task Summarised_SkipsMetadataOperations()
    {
        using var invoker = CreateInvoker(MakeOptions(CosmosTrackingVerbosity.Summarised));

        await invoker.SendAsync(MakeMetadataRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Empty(logs);
    }

    [Fact]
    public async Task Summarised_SkipsTheQueryPlanFetchThatPrecedesAQuery()
    {
        // Measured on a consumer's report: every Query was preceded by a "Create" that never happened,
        // and a scenario that only polled a container was shown creating four documents.
        using var invoker = CreateInvoker(MakeOptions(CosmosTrackingVerbosity.Summarised));
        _innerHandler.ResponseToReturn = new(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"partitionedQueryExecutionInfoVersion":2,"queryInfo":{"rewrittenQuery":""}}""")
        };

        await invoker.SendAsync(MakeQueryPlanRequest(), CancellationToken.None);
        await invoker.SendAsync(MakeQueryRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.NotEmpty(logs);
        Assert.All(logs, l => Assert.Equal("Query", l.Method.Value?.ToString()));
    }

    [Fact]
    public async Task Detailed_ShowsTheQueryPlanFetchAsOther_NeverAsCreate()
    {
        using var invoker = CreateInvoker(MakeOptions(CosmosTrackingVerbosity.Detailed));

        await invoker.SendAsync(MakeQueryPlanRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.NotEmpty(logs);
        Assert.All(logs, l => Assert.Equal("Other", l.Method.Value?.ToString()));
    }

    // ─── Raw verbosity ────────────────────────────────────────

    [Fact]
    public async Task Raw_UsesHttpMethodAsMethod()
    {
        using var invoker = CreateInvoker(MakeOptions(CosmosTrackingVerbosity.Raw));

        await invoker.SendAsync(MakeCreateRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal(HttpMethod.Post, log.Method.Value);
    }

    [Fact]
    public async Task Raw_IncludesFullContent()
    {
        using var invoker = CreateInvoker(MakeOptions(CosmosTrackingVerbosity.Raw));

        await invoker.SendAsync(MakeCreateRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains("order-1", log.Content);
    }

    [Fact]
    public async Task Raw_DoesNotSkipMetadataOperations()
    {
        using var invoker = CreateInvoker(MakeOptions(CosmosTrackingVerbosity.Raw));

        await invoker.SendAsync(MakeMetadataRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
    }

    // ─── Header filtering ─────────────────────────────────────

    [Fact]
    public async Task Detailed_ExcludesDefaultNoisyHeaders()
    {
        using var invoker = CreateInvoker(MakeOptions(CosmosTrackingVerbosity.Detailed));
        var request = MakeReadRequest();
        request.Headers.Add("x-ms-date", "Tue, 29 Mar 2016 02:03:06 GMT");
        request.Headers.Add("x-ms-custom-header", "keep-me");

        await invoker.SendAsync(request, CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.DoesNotContain(log.Headers, h => h.Key == "x-ms-date");
        Assert.Contains(log.Headers, h => h.Key == "x-ms-custom-header");
    }

    // ─── Response status code ─────────────────────────────────

    [Fact]
    public async Task Response_CapturesStatusCode()
    {
        _innerHandler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("{}")
        };
        using var invoker = CreateInvoker(MakeOptions());

        await invoker.SendAsync(MakeCreateRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Equal(HttpStatusCode.Created, log.StatusCode!.Value);
    }

    // ─── URI cleaning ─────────────────────────────────────────

    [Fact]
    public async Task Detailed_Create_UsesCleanUri_WithoutDb()
    {
        using var invoker = CreateInvoker(MakeOptions(CosmosTrackingVerbosity.Detailed));

        await invoker.SendAsync(MakeCreateRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("/colls/orders", log.Uri.AbsolutePath);
    }

    [Fact]
    public async Task Detailed_Read_IncludesDocPathInUri()
    {
        using var invoker = CreateInvoker(MakeOptions(CosmosTrackingVerbosity.Detailed));

        await invoker.SendAsync(MakeReadRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("/colls/orders/docs/order-1", log.Uri.AbsolutePath);
    }

    [Fact]
    public async Task Summarised_UsesCollectionNameOnlyAsPath()
    {
        using var invoker = CreateInvoker(MakeOptions(CosmosTrackingVerbosity.Summarised));

        await invoker.SendAsync(MakeReadRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("/orders", log.Uri.AbsolutePath);
    }

    [Fact]
    public async Task Raw_UsesOriginalUri()
    {
        using var invoker = CreateInvoker(MakeOptions(CosmosTrackingVerbosity.Raw));

        await invoker.SendAsync(MakeCreateRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains("/docs", log.Uri.AbsolutePath);
    }

        // ─── ITrackingComponent ────────────────────────────────────

        [Fact]
        public void Implements_ITrackingComponent()
        {
            var handler = new CosmosTrackingMessageHandler(MakeOptions());
            Assert.IsAssignableFrom<ITrackingComponent>(handler);
        }

        [Fact]
        public void WasInvoked_IsFalse_BeforeAnyRequests()
        {
            var handler = new CosmosTrackingMessageHandler(MakeOptions());
            Assert.False(handler.WasInvoked);
        }

        [Fact]
        public async Task WasInvoked_IsTrue_AfterRequest()
        {
            var inner = new StubInnerHandler();
            var handler = new CosmosTrackingMessageHandler(MakeOptions(), inner);
            using var invoker = new HttpMessageInvoker(handler);
            await invoker.SendAsync(MakeCreateRequest(), CancellationToken.None);

            Assert.True(handler.WasInvoked);
        }

        [Fact]
        public void InvocationCount_StartsAtZero()
        {
            var handler = new CosmosTrackingMessageHandler(MakeOptions());
            Assert.Equal(0, handler.InvocationCount);
        }

        [Fact]
        public void ComponentName_MatchesServiceName()
        {
            var handler = new CosmosTrackingMessageHandler(MakeOptions(serviceName: "MyCosmosDB"));
            Assert.Equal("CosmosTrackingMessageHandler (MyCosmosDB)", handler.ComponentName);
        }

        [Fact]
        public void Constructor_AutoRegistersWithTrackingComponentRegistry()
        {
            TrackingComponentRegistry.Clear();
            var handler = new CosmosTrackingMessageHandler(MakeOptions());

            var components = TrackingComponentRegistry.GetRegisteredComponents();
            Assert.Contains(components, c => ReferenceEquals(c, handler));
        }

    // ─── Auto-Correlation ─────────────────────────────────────

    [Fact]
    public async Task Create_AutoCorrelates_DocumentId_From_Response()
    {
        TestCorrelationStore.Clear();
        _innerHandler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{"id":"order-1","total":42.50}""")
        };
        using var invoker = CreateInvoker(MakeOptions(serviceName: "Orders"));

        await invoker.SendAsync(MakeCreateRequest(), CancellationToken.None);

        var result = TestCorrelationStore.Resolve(CorrelationKeys.Cosmos("Orders", "order-1"));
        Assert.NotNull(result);
        Assert.Equal("My Test", result.Value.Name);
        Assert.Equal(_testId, result.Value.Id);
        TestCorrelationStore.Clear();
    }

    [Fact]
    public async Task Upsert_AutoCorrelates_DocumentId_From_Response()
    {
        TestCorrelationStore.Clear();
        _innerHandler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":"order-2","total":99.99}""")
        };
        using var invoker = CreateInvoker(MakeOptions(serviceName: "Orders"));

        var request = new HttpRequestMessage(HttpMethod.Post,
            "https://account.documents.azure.com/dbs/mydb/colls/orders/docs")
        {
            Content = new StringContent("""{"id":"order-2","total":99.99}""")
        };
        request.Headers.Add("x-ms-documentdb-is-upsert", "true");

        await invoker.SendAsync(request, CancellationToken.None);

        var result = TestCorrelationStore.Resolve(CorrelationKeys.Cosmos("Orders", "order-2"));
        Assert.NotNull(result);
        Assert.Equal("My Test", result.Value.Name);
        TestCorrelationStore.Clear();
    }

    [Fact]
    public async Task Replace_AutoCorrelates_DocumentId_From_Url()
    {
        TestCorrelationStore.Clear();
        _innerHandler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":"order-3","total":10.00}""")
        };
        using var invoker = CreateInvoker(MakeOptions(serviceName: "Orders"));

        var request = new HttpRequestMessage(HttpMethod.Put,
            "https://account.documents.azure.com/dbs/mydb/colls/orders/docs/order-3")
        {
            Content = new StringContent("""{"id":"order-3","total":10.00}""")
        };

        await invoker.SendAsync(request, CancellationToken.None);

        var result = TestCorrelationStore.Resolve(CorrelationKeys.Cosmos("Orders", "order-3"));
        Assert.NotNull(result);
        Assert.Equal("My Test", result.Value.Name);
        TestCorrelationStore.Clear();
    }

    [Fact]
    public async Task Read_Does_Not_AutoCorrelate()
    {
        TestCorrelationStore.Clear();
        using var invoker = CreateInvoker(MakeOptions(serviceName: "Orders"));

        await invoker.SendAsync(MakeReadRequest(), CancellationToken.None);

        var result = TestCorrelationStore.Resolve(CorrelationKeys.Cosmos("Orders", "order-1"));
        Assert.Null(result);
    }

    [Fact]
    public async Task AutoCorrelateWrites_False_Disables_Correlation()
    {
        TestCorrelationStore.Clear();
        _innerHandler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{"id":"order-1","total":42.50}""")
        };
        var options = MakeOptions(serviceName: "Orders");
        options.AutoCorrelateWrites = false;
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeCreateRequest(), CancellationToken.None);

        var result = TestCorrelationStore.Resolve(CorrelationKeys.Cosmos("Orders", "order-1"));
        Assert.Null(result);
    }

    [Fact]
    public async Task Failed_Response_Does_Not_AutoCorrelate()
    {
        TestCorrelationStore.Clear();
        _innerHandler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent("""{"id":"order-1","Errors":[]}""")
        };
        using var invoker = CreateInvoker(MakeOptions(serviceName: "Orders"));

        await invoker.SendAsync(MakeCreateRequest(), CancellationToken.None);

        var result = TestCorrelationStore.Resolve(CorrelationKeys.Cosmos("Orders", "order-1"));
        Assert.Null(result);
        TestCorrelationStore.Clear();
    }

    [Fact]
    public async Task Custom_ChangeFeedKeyExtractor_UsedForCorrelation()
    {
        TestCorrelationStore.Clear();
        _innerHandler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{"id":"order-1","total":42.50}""")
        };
        var options = MakeOptions(serviceName: "Orders");
        options.ChangeFeedKeyExtractor = (svc, docId) => $"custom:{svc}:{docId}";
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeCreateRequest(), CancellationToken.None);

        var result = TestCorrelationStore.Resolve("custom:Orders:order-1");
        Assert.NotNull(result);
        Assert.Equal("My Test", result.Value.Name);
        TestCorrelationStore.Clear();
    }

    // ─── Summarised + LogResponseContent ────────────────────────

    [Fact]
    public async Task Summarised_IncludesResponseContent_WhenLogResponseContentTrue()
    {
        var options = MakeOptions(CosmosTrackingVerbosity.Summarised);
        options.LogResponseContent = true;
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeReadRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.NotNull(log.Content);
        Assert.Contains("doc1", log.Content);
    }

    [Fact]
    public async Task Summarised_OmitsResponseContent_WhenLogResponseContentFalse()
    {
        var options = MakeOptions(CosmosTrackingVerbosity.Summarised);
        options.LogResponseContent = false;
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeReadRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Null(log.Content);
    }

    [Fact]
    public async Task Summarised_still_omits_request_content_when_LogResponseContent_true()
    {
        var options = MakeOptions(CosmosTrackingVerbosity.Summarised);
        options.LogResponseContent = true;
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeCreateRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Null(log.Content);
    }

    [Fact]
    public async Task Summarised_ResponseVariant_includes_content_when_LogResponseContent_true()
    {
        TestPhaseContext.Reset();
        var options = MakeOptions();
        options.LogResponseContent = true;
        options.SetupVerbosity = CosmosTrackingVerbosity.Summarised;
        options.ActionVerbosity = CosmosTrackingVerbosity.Detailed;
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeReadRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.NotNull(log.SetupVariant!.Content);
        Assert.Contains("doc1", log.SetupVariant!.Content);
    }

    // ─── Gzip decompression ────────────────────────────────────

    [Fact]
    public async Task Detailed_Decompresses_Gzip_Response_Content()
    {
        var json = """{"id":"doc1","_rid":"abc==","status":"created"}""";
        var compressed = GzipCompress(json);

        var responseContent = new ByteArrayContent(compressed);
        responseContent.Headers.ContentEncoding.Add("gzip");
        responseContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        _innerHandler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK) { Content = responseContent };
        using var invoker = CreateInvoker(MakeOptions(CosmosTrackingVerbosity.Detailed));

        await invoker.SendAsync(MakeReadRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Contains("doc1", log.Content!);
        Assert.Contains("created", log.Content!);
    }

    [Fact]
    public async Task Detailed_Decompresses_Gzip_Request_Content()
    {
        var json = """{"id":"order-1","total":42.50}""";
        var compressed = GzipCompress(json);

        var content = new ByteArrayContent(compressed);
        content.Headers.ContentEncoding.Add("gzip");
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        var request = new HttpRequestMessage(HttpMethod.Post,
            "https://account.documents.azure.com/dbs/mydb/colls/orders/docs")
        {
            Content = content
        };

        using var invoker = CreateInvoker(MakeOptions(CosmosTrackingVerbosity.Detailed));
        await invoker.SendAsync(request, CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains("order-1", log.Content!);
    }

    [Fact]
    public async Task Summarised_Decompresses_Gzip_Response_When_LogResponseContent()
    {
        var json = """{"id":"doc1","_rid":"abc=="}""";
        var compressed = GzipCompress(json);

        var responseContent = new ByteArrayContent(compressed);
        responseContent.Headers.ContentEncoding.Add("gzip");
        responseContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        _innerHandler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK) { Content = responseContent };
        var options = MakeOptions(CosmosTrackingVerbosity.Summarised);
        options.LogResponseContent = true;
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeReadRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Contains("doc1", log.Content!);
    }

    private static byte[] GzipCompress(string text)
    {
        using var output = new System.IO.MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.Optimal))
        using (var writer = new System.IO.StreamWriter(gzip))
            writer.Write(text);
        return output.ToArray();
    }

    // ─── Binary batch response handling ────────────────────────

    [Fact]
    public async Task Detailed_Batch_BinaryResponse_ExtractsJsonDocuments()
    {
        var json1 = """{"id":"order-1","total":42.50}""";
        var json2 = """{"id":"outbox-1","type":"OrderCreated"}""";
        var binaryPayload = BuildBinaryBatchResponse(json1, json2);

        _innerHandler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(binaryPayload)
        };
        using var invoker = CreateInvoker(MakeOptions(CosmosTrackingVerbosity.Detailed));

        await invoker.SendAsync(MakeBatchRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.NotNull(log.Content);
        Assert.Contains("order-1", log.Content);
        Assert.Contains("outbox-1", log.Content);
        Assert.DoesNotContain("\uFFFD", log.Content); // No replacement characters
    }

    [Fact]
    public async Task Raw_Batch_BinaryResponse_ExtractsJsonDocuments()
    {
        var json = """{"id":"doc1","status":"created"}""";
        var binaryPayload = BuildBinaryBatchResponse(json);

        _innerHandler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(binaryPayload)
        };
        using var invoker = CreateInvoker(MakeOptions(CosmosTrackingVerbosity.Raw));

        await invoker.SendAsync(MakeBatchRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.NotNull(log.Content);
        Assert.Contains("doc1", log.Content);
        Assert.Contains("created", log.Content);
    }

    [Fact]
    public async Task Detailed_Batch_BinaryRequest_ExtractsJsonDocuments()
    {
        var json = """{"id":"order-1","total":42.50}""";
        var binaryPayload = BuildBinaryBatchResponse(json);

        var request = new HttpRequestMessage(HttpMethod.Post,
            "https://account.documents.azure.com/dbs/mydb/colls/orders/docs/pk-value")
        {
            Content = new ByteArrayContent(binaryPayload)
        };

        using var invoker = CreateInvoker(MakeOptions(CosmosTrackingVerbosity.Detailed));
        await invoker.SendAsync(request, CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.NotNull(log.Content);
        Assert.Contains("order-1", log.Content);
    }

    [Fact]
    public async Task Summarised_Batch_BinaryResponse_LogResponseContent_ExtractsJson()
    {
        var json = """{"id":"doc1"}""";
        var binaryPayload = BuildBinaryBatchResponse(json);

        _innerHandler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(binaryPayload)
        };
        var options = MakeOptions(CosmosTrackingVerbosity.Summarised);
        options.LogResponseContent = true;
        using var invoker = CreateInvoker(options);

        await invoker.SendAsync(MakeBatchRequest(), CancellationToken.None);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.NotNull(log.Content);
        Assert.Contains("doc1", log.Content);
    }

    private static HttpRequestMessage MakeBatchRequest()
    {
        return new HttpRequestMessage(HttpMethod.Post,
            "https://account.documents.azure.com/dbs/mydb/colls/orders/docs/pk-value")
        {
            Content = new ByteArrayContent(BuildBinaryBatchResponse(
                """{"id":"order-1","total":42.50}""",
                """{"id":"outbox-1","type":"OrderCreated"}"""))
        };
    }

    private static byte[] BuildBinaryBatchResponse(params string[] jsonDocuments)
    {
        using var ms = new System.IO.MemoryStream();
        // HybridRow version byte + binary framing header
        ms.Write(new byte[] { 0x01, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00 });

        foreach (var json in jsonDocuments)
        {
            // Binary field framing (status code, sub-status)
            ms.Write(new byte[] { 0xC8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });
            // etag-like string
            var etagBytes = System.Text.Encoding.UTF8.GetBytes("$" + Guid.NewGuid().ToString());
            ms.WriteByte((byte)etagBytes.Length);
            ms.Write(etagBytes);
            // Field marker before resourceBody
            ms.Write(new byte[] { 0x06, 0x00 });
            // The JSON document bytes
            ms.Write(System.Text.Encoding.UTF8.GetBytes(json));
            // Binary trailer (request charge, retry-after)
            ms.Write(new byte[] { 0x00, 0x00, 0x80, 0x3F, 0x00, 0x00, 0x00, 0x00 });
        }

        ms.Write(new byte[] { 0x00, 0x00, 0x00, 0x00 });
        return ms.ToArray();
    }

    // ─── Document ownership (plans/DOCUMENT_OWNERSHIP_PLAN.md) ──

    private static HttpRequestMessage MakeReplaceRequest(string id = "order-1") =>
        new(HttpMethod.Put, "https://account.documents.azure.com/dbs/mydb/colls/orders/docs/" + id)
        {
            Content = new StringContent("""{"id":"order-1","status":"Processing"}""")
        };

    private static HttpResponseMessage Answer(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body) };

    [Fact]
    public async Task A_document_operation_that_resolved_no_scenario_is_the_scenario_that_wrote_the_document()
    {
        // A detached host's outbox processor claims, retries and fails a row the scenario seeded: the claim
        // names the document, and the document names the scenario that wrote it.
        _innerHandler.ResponseToReturn = Answer(HttpStatusCode.Created, """{"id":"order-1"}""");
        using var invoker = CreateInvoker(MakeOptions());
        await invoker.SendAsync(MakeCreateRequest(), CancellationToken.None);

        _innerHandler.ResponseToReturn = Answer(HttpStatusCode.OK, """{"id":"order-1","status":"Processing"}""");
        using (TestIdentityScope.Detach())
            await invoker.SendAsync(MakeReplaceRequest(), CancellationToken.None);

        var logs = GetLogsFromThisTest();
        Assert.Equal(4, logs.Length);
        Assert.Equal(AttributionSource.TestContext, logs[0].AttributionSource);
        Assert.Equal(AttributionSource.DocumentOwner, logs[2].AttributionSource);
        Assert.Equal(AttributionSource.DocumentOwner, logs[3].AttributionSource);
        Assert.Equal("My Test", logs[2].TestName);
    }

    [Fact]
    public async Task A_query_or_an_unowned_document_in_a_detached_flow_stays_the_hosts()
    {
        _innerHandler.ResponseToReturn = Answer(HttpStatusCode.Created, """{"id":"order-1"}""");
        using var invoker = CreateInvoker(MakeOptions());
        await invoker.SendAsync(MakeCreateRequest(), CancellationToken.None);
        var seeded = GetLogsFromThisTest().Length;

        _innerHandler.ResponseToReturn = Answer(HttpStatusCode.OK, "[]");
        using (TestIdentityScope.Detach())
        {
            await invoker.SendAsync(MakeQueryRequest(), CancellationToken.None);           // names no document
            await invoker.SendAsync(MakeReplaceRequest("order-9"), CancellationToken.None); // nobody wrote it
        }

        Assert.Equal(seeded, GetLogsFromThisTest().Length);
    }

    [Fact]
    public async Task Ownership_never_takes_a_call_from_a_flow_that_has_a_scenario_and_the_last_writer_owns()
    {
        _innerHandler.ResponseToReturn = Answer(HttpStatusCode.Created, """{"id":"order-1"}""");
        using var invoker = CreateInvoker(MakeOptions());
        await invoker.SendAsync(MakeCreateRequest(), CancellationToken.None);

        var otherId = Guid.NewGuid().ToString();
        var otherOptions = MakeOptions();
        otherOptions.CurrentTestInfoFetcher = () => ("Other", otherId);
        using var other = new HttpMessageInvoker(new CosmosTrackingMessageHandler(otherOptions, _innerHandler));
        _innerHandler.ResponseToReturn = Answer(HttpStatusCode.OK, """{"id":"order-1"}""");
        await other.SendAsync(MakeReplaceRequest(), CancellationToken.None);
        using (TestIdentityScope.Detach())
            await invoker.SendAsync(MakeReplaceRequest(), CancellationToken.None);

        var otherLogs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == otherId && l.Type == RequestResponseType.Request).ToArray();
        Assert.Equal(2, otherLogs.Length);
        Assert.Equal(AttributionSource.TestContext, otherLogs[0].AttributionSource);
        Assert.Equal(AttributionSource.DocumentOwner, otherLogs[1].AttributionSource);
        Assert.DoesNotContain(GetLogsFromThisTest(), l => l.AttributionSource == AttributionSource.DocumentOwner);
    }

    [Fact]
    public async Task Ownership_can_be_turned_off()
    {
        var options = MakeOptions();
        options.AttributeByDocumentOwner = false;
        _innerHandler.ResponseToReturn = Answer(HttpStatusCode.Created, """{"id":"order-1"}""");
        using var invoker = CreateInvoker(options);
        await invoker.SendAsync(MakeCreateRequest(), CancellationToken.None);

        using (TestIdentityScope.Detach())
            await invoker.SendAsync(MakeReplaceRequest(), CancellationToken.None);

        Assert.Equal(2, GetLogsFromThisTest().Length);
    }
}
