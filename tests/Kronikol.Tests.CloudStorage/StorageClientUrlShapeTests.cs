using System.Net;
using System.Reflection;
using System.Text;
using Google.Apis.Http;
using Google.Cloud.Storage.V1;
using Kronikol.Extensions.CloudStorage;
using Kronikol.Tracking;

namespace Kronikol.Tests.CloudStorage;

/// <summary>
/// The real <see cref="StorageClient"/> writes every URL here, so these facts read what the SDK sends rather than what a
/// test's author believed it sends (#125). The client goes through the shipped handler to a stub server under each base
/// a suite points it at: Google's, an emulator's <c>/storage/v1/</c> path, an emulator's root, and the SDK's own
/// <c>STORAGE_EMULATOR_HOST</c> support, which sets the root. They assert how each request was named, never which
/// requests a client version sends, so they hold on the package's floor too.
/// </summary>
public class StorageClientUrlShapeTests
{
    // The SDK reads STORAGE_EMULATOR_HOST through StorageClientBuilder.EmulatorDetection, which the package's floor
    // (4.0.0) lacks: a client without it has no such setup to meet.
    private static readonly PropertyInfo? EmulatorDetection = typeof(StorageClientBuilder).GetProperty("EmulatorDetection");

    public static TheoryData<string> Bases()
    {
        var bases = new TheoryData<string> { "google", "http://127.0.0.1:4443/storage/v1/", "http://127.0.0.1:4443/" };
        if (EmulatorDetection is not null)
            bases.Add("STORAGE_EMULATOR_HOST");
        return bases;
    }

    [Fact]
    public void Every_operation_reaches_the_server()
    {
        var sent = Run("google", CloudStorageTrackingVerbosity.Detailed);

        Assert.Equal(["CreateBucket", "GetBucket", "UploadObject", "GetObject", "DownloadObject", "ListObjects", "CopyObject", "DeleteObject", "DeleteBucket"],
            sent.Select(s => s.Operation).Distinct());
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Every_call_the_client_sends_is_named_at_Detailed(string baseUri)
    {
        var sent = Run(baseUri, CloudStorageTrackingVerbosity.Detailed);

        Assert.NotEmpty(sent);
        Assert.All(sent, s => Assert.NotNull(s.Recorded));
        Assert.DoesNotContain(sent, s => s.Recorded!.Value.Label.StartsWith("Other", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void Every_call_the_client_sends_is_recorded_at_Summarised(string baseUri)
    {
        var sent = Run(baseUri, CloudStorageTrackingVerbosity.Summarised);

        Assert.All(sent, s => Assert.NotNull(s.Recorded));
        Assert.DoesNotContain(sent, s => s.Recorded!.Value.Label == "Other");
    }

    [Theory]
    [MemberData(nameof(Bases))]
    public void A_call_reads_the_same_under_any_base_as_under_Googles(string baseUri)
    {
        var google = Run("google", CloudStorageTrackingVerbosity.Detailed);
        var other = Run(baseUri, CloudStorageTrackingVerbosity.Detailed);

        Assert.Equal(google.Select(s => (s.Operation, s.Recorded)), other.Select(s => (s.Operation, s.Recorded)));
    }

    [Fact]
    public void A_bucket_whose_name_starts_with_o_is_the_bucket()
    {
        var sent = Run("google", CloudStorageTrackingVerbosity.Detailed);

        Assert.Equal(("GetBucket → orders", "gcs:///orders"), Assert.Single(sent, s => s.Operation == "GetBucket").Recorded);
        Assert.Equal(("DeleteBucket → orders", "gcs:///orders"), Assert.Single(sent, s => s.Operation == "DeleteBucket").Recorded);
    }

    [Fact]
    public void CopyObject_is_a_copy()
    {
        var sent = Run("http://127.0.0.1:4443/", CloudStorageTrackingVerbosity.Detailed);

        Assert.All(sent.Where(s => s.Operation == "CopyObject"), s => Assert.StartsWith("Copy → orders/order-1.json", s.Recorded!.Value.Label));
    }

    [Fact]
    public void An_uploads_data_is_part_of_the_upload()
    {
        var sent = Run("google", CloudStorageTrackingVerbosity.Detailed);

        var upload = sent.Where(s => s.Operation == "UploadObject").ToArray();
        Assert.Contains(upload, s => s.Method == "POST" && s.Recorded!.Value.Label == "Upload → orders");
        Assert.Contains(upload, s => s.Method == "PUT" && s.Recorded!.Value.Label == "Upload → orders");
    }

    private static readonly object EnvironmentLock = new();

    private static List<SentRequest> Run(string baseUri, CloudStorageTrackingVerbosity verbosity)
    {
        var testId = Guid.NewGuid().ToString();
        var stub = new StubStorageServer(() => RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId && l.Type == RequestResponseType.Request).ToList());
        var options = new CloudStorageTrackingMessageHandlerOptions
        {
            Verbosity = verbosity,
            CurrentTestInfoFetcher = () => ("Cloud Storage URL shapes", testId),
        };

        StorageClient client;
        if (baseUri == "STORAGE_EMULATOR_HOST")
        {
            // The variable is process-wide: set it only for the build that reads it.
            lock (EnvironmentLock)
            {
                var before = Environment.GetEnvironmentVariable("STORAGE_EMULATOR_HOST");
                Environment.SetEnvironmentVariable("STORAGE_EMULATOR_HOST", "localhost:4443");
                try
                {
                    var builder = new StorageClientBuilder { HttpClientFactory = new TrackingFactoryOverStub(options, stub) };
                    EmulatorDetection!.SetValue(builder, Enum.Parse(EmulatorDetection.PropertyType, "EmulatorOnly"));
                    client = builder.Build();
                }
                finally
                {
                    Environment.SetEnvironmentVariable("STORAGE_EMULATOR_HOST", before);
                }
            }
        }
        else
        {
            var builder = new StorageClientBuilder { UnauthenticatedAccess = true, HttpClientFactory = new TrackingFactoryOverStub(options, stub) };
            if (baseUri != "google")
                builder.BaseUri = baseUri;
            client = builder.Build();
        }

        var operations = new (string Name, Action Call)[]
        {
            ("CreateBucket", () => client.CreateBucket("proj", "orders")),
            ("GetBucket", () => client.GetBucket("orders")),
            ("UploadObject", () => client.UploadObject("orders", "order-1.json", "application/json", new MemoryStream(Encoding.UTF8.GetBytes("{}")))),
            ("GetObject", () => client.GetObject("orders", "order-1.json")),
            ("DownloadObject", () => client.DownloadObject("orders", "order-1.json", new MemoryStream())),
            ("ListObjects", () => client.ListObjects("orders").Take(1).ToList()),
            ("CopyObject", () => client.CopyObject("orders", "order-1.json", "archive", "order-1.json")),
            ("DeleteObject", () => client.DeleteObject("orders", "order-1.json")),
            ("DeleteBucket", () => client.DeleteBucket("orders")),
        };

        var sent = new List<SentRequest>();
        foreach (var (name, call) in operations)
        {
            var before = stub.Sent.Count;
            // The stub's one body cannot satisfy every response type, and what a fact reads is what was sent and
            // recorded, which happens before the client parses the answer.
            try { call(); }
            catch (Exception) { }
            sent.AddRange(stub.Sent.Skip(before).Select(s => s with { Operation = name }));
        }
        return sent;
    }

    internal sealed record SentRequest(string Operation, string Method, Uri Uri, (string Label, string Path)? Recorded);

    private sealed class TrackingFactoryOverStub(CloudStorageTrackingMessageHandlerOptions options, HttpMessageHandler stub) : HttpClientFactory
    {
        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => new CloudStorageTrackingMessageHandler(options, stub);
    }

    // The handler logs a request before it calls its inner handler, so the stub knows, request by request, whether the
    // handler recorded the call it is about to answer, and as what.
    private sealed class StubStorageServer(Func<List<RequestResponseLog>> requestLogs) : HttpMessageHandler
    {
        private int _seen;
        public List<SentRequest> Sent { get; } = [];

        private const string Body = """
            {"kind":"storage#object","name":"order-1.json","bucket":"orders","id":"orders/order-1.json","size":"2",
             "generation":"1","contentType":"application/json","items":[],"done":true,
             "resource":{"name":"order-1.json","bucket":"archive"}}
            """;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var logs = requestLogs();
            (string, string)? recorded = logs.Count > _seen ? (logs[^1].Method.Value?.ToString() ?? "", logs[^1].Uri.ToString()) : null;
            _seen = logs.Count;
            Sent.Add(new SentRequest("", request.Method.Method, uri, recorded));
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
            if (request.Method == HttpMethod.Post && uri.Query.Contains("uploadType=resumable"))
                response.Headers.Location = new Uri(uri, uri.AbsolutePath + "?uploadType=resumable&upload_id=u1");
            return Task.FromResult(response);
        }
    }
}
