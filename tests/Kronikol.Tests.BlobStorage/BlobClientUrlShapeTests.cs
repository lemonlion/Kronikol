using System.Net;
using System.Text;
using Azure.Storage.Blobs;
using Kronikol.Extensions.BlobStorage;
using Kronikol.Tracking;

namespace Kronikol.Tests.BlobStorage;

/// <summary>
/// The real <see cref="BlobServiceClient"/> writes every URL here, so these facts read what the SDK sends rather than
/// what a test's author believed it sends (#125, plans/EMULATOR_URL_SHAPES_PLAN.md section 6.1). The client goes through
/// the shipped handler to a stub server, under each connection a suite gives it: a production account, Azurite through
/// <c>UseDevelopmentStorage=true</c>, and Azurite's account on <c>127.0.0.1</c> on another port, on a docker compose
/// name on the emulator's port, on <c>localhost</c> on another port and on a Docker host's name on another port. They
/// assert how each request was named, never which requests a client version sends, so they hold on the floor too. On a
/// host name with another port the SDK itself reads Azurite's account as a container, and the URL it builds for a blob
/// from a container's leaves the container out (<see cref="HostNameConnections"/>); what it sends there is read as sent.
/// </summary>
public class BlobClientUrlShapeTests
{
    // Azurite's well-known key for its built-in account.
    private const string Key = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

    private const string Production = "DefaultEndpointsProtocol=https;AccountName=shop;AccountKey=" + Key + ";EndpointSuffix=core.windows.net";

    // The connections whose every URL the SDK writes as it would for a production account.
    private static readonly string[] PathStyle =
    [
        Production,
        "UseDevelopmentStorage=true",
        Azurite("http://127.0.0.1:54321"),
        Azurite("http://azurite:10000"),
    ];

    // A host name with a port the SDK does not take for path-style (F11): the SDK reads Azurite's account as the
    // container, so the blob client it makes from a container client puts the blob where the container belongs and
    // sends /devstoreaccount1/order-1.json.
    private static readonly string[] HostNames =
    [
        Azurite("http://localhost:54321"),
        Azurite("http://docker-host.internal:54321"),
    ];

    public static TheoryData<string> Connections => new(PathStyle);

    public static TheoryData<string> HostNameConnections => new(HostNames);

    public static TheoryData<string> AllConnections => new(PathStyle.Concat(HostNames));

    private static string Azurite(string host) =>
        $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey={Key};BlobEndpoint={host}/devstoreaccount1;";

    [Fact]
    public void Every_operation_reaches_the_server()
    {
        var run = Run(Production, BlobTrackingVerbosity.Detailed);

        Assert.Equal(run.Operations, run.Sent.Select(s => s.Operation).Distinct());
    }

    [Theory]
    [MemberData(nameof(Connections))]
    public void Every_call_on_a_container_or_a_blob_is_named_at_Detailed(string connection)
    {
        var run = Run(connection, BlobTrackingVerbosity.Detailed);

        Assert.All(run.Sent, s => Assert.NotNull(s.Recorded));
        Assert.DoesNotContain(run.Sent, s => s.Operation != "ListContainers" && s.Recorded!.Value.Label == "Other");
    }

    [Theory]
    [MemberData(nameof(AllConnections))]
    public void Every_call_is_recorded_at_Summarised(string connection)
    {
        var run = Run(connection, BlobTrackingVerbosity.Summarised);

        Assert.All(run.Sent, s => Assert.NotNull(s.Recorded));
    }

    [Theory]
    [MemberData(nameof(Connections))]
    public void A_call_reads_the_same_under_any_connection_as_on_a_production_account(string connection)
    {
        var production = Run(Production, BlobTrackingVerbosity.Detailed);
        var other = Run(connection, BlobTrackingVerbosity.Detailed);

        Assert.Equal(production.Sent.Select(s => (s.Operation, s.Recorded)), other.Sent.Select(s => (s.Operation, s.Recorded)));
    }

    [Theory]
    [MemberData(nameof(AllConnections))]
    public void A_container_is_created_listed_and_deleted_as_a_container(string connection)
    {
        var run = Run(connection, BlobTrackingVerbosity.Detailed);

        Assert.Equal(("CreateContainer", "/orders"), Assert.Single(run.Sent, s => s.Operation == "CreateContainer").Recorded);
        Assert.Equal(("ListBlobs", "/orders"), Assert.Single(run.Sent, s => s.Operation == "ListBlobs").Recorded);
        Assert.Equal(("DeleteContainer", "/orders"), Assert.Single(run.Sent, s => s.Operation == "DeleteContainer").Recorded);
    }

    [Theory]
    [MemberData(nameof(AllConnections))]
    public void A_call_on_the_account_keeps_its_query_and_loses_Azurites_account_segment(string connection)
    {
        var run = Run(connection, BlobTrackingVerbosity.Detailed);

        Assert.Equal(("Other", "/?comp=list"), Assert.Single(run.Sent, s => s.Operation == "ListContainers").Recorded);
    }

    [Theory]
    [MemberData(nameof(HostNameConnections))]
    public void On_a_host_name_and_another_port_a_blob_is_read_where_the_SDK_sent_it(string connection)
    {
        var production = Run(Production, BlobTrackingVerbosity.Detailed);
        var run = Run(connection, BlobTrackingVerbosity.Detailed);
        string[] onTheBlob = ["Upload", "Download", "GetProperties", "DeleteBlob"];

        // The container is not in the URL, so the call is read as Azurite reads it: on a container named after the blob.
        Assert.All(run.Sent.Where(s => onTheBlob.Contains(s.Operation)), s =>
        {
            Assert.Equal("/devstoreaccount1/order-1.json", s.Uri.AbsolutePath);
            Assert.Equal(("Other", "/order-1.json"), s.Recorded);
        });
        Assert.Equal(production.Sent.Where(s => !onTheBlob.Contains(s.Operation)).Select(s => (s.Operation, s.Recorded)),
            run.Sent.Where(s => !onTheBlob.Contains(s.Operation)).Select(s => (s.Operation, s.Recorded)));
    }

    // One run of the client's operations under a connection and a verbosity, each request paired with what the handler
    // recorded for it: its label and the path and query of its URI.
    private static Recording Run(string connection, BlobTrackingVerbosity verbosity)
    {
        var testId = Guid.NewGuid().ToString();
        var stub = new StubBlobServer(() => RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId && l.Type == RequestResponseType.Request).ToList());
        var options = new BlobClientOptions().WithTestTracking(new BlobTrackingMessageHandlerOptions
        {
            Verbosity = verbosity,
            CurrentTestInfoFetcher = () => ("Blob URL shapes", testId),
        }, stub);
        options.Retry.MaxRetries = 0;
        var service = new BlobServiceClient(connection, options);
        var container = service.GetBlobContainerClient("orders");
        var blob = container.GetBlobClient("order-1.json");

        var operations = new (string Name, Action Call)[]
        {
            ("CreateContainer", () => container.Create()),
            ("Upload", () => blob.Upload(BinaryData.FromString("{}"))),
            ("Download", () => blob.DownloadContent()),
            ("GetProperties", () => blob.GetProperties()),
            ("ListBlobs", () => container.GetBlobs().Take(1).ToList()),
            ("DeleteBlob", () => blob.Delete()),
            ("DeleteContainer", () => container.Delete()),
            ("ListContainers", () => service.GetBlobContainers().Take(1).ToList()),
        };

        var sent = new List<SentRequest>();
        foreach (var (name, call) in operations)
        {
            var before = stub.Sent.Count;
            // The stub's empty answers cannot satisfy every response type, and what a fact reads is what was sent and
            // recorded, which happens before the client parses the answer.
            try { call(); }
            catch (Exception) { }
            sent.AddRange(stub.Sent.Skip(before).Select(s => s with { Operation = name }));
        }
        return new Recording(sent, operations.Select(o => o.Name).ToArray());
    }

    private sealed record Recording(IReadOnlyList<SentRequest> Sent, IReadOnlyList<string> Operations);

    internal sealed record SentRequest(string Operation, string Method, Uri Uri, (string Label, string Path)? Recorded);

    // The handler logs a request before it calls its inner handler, so the stub knows, request by request, whether the
    // handler recorded the call it is about to answer, and as what.
    private sealed class StubBlobServer(Func<List<RequestResponseLog>> requestLogs) : HttpMessageHandler
    {
        private int _seen;
        public List<SentRequest> Sent { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var logs = requestLogs();
            (string, string)? recorded = logs.Count > _seen ? (logs[^1].Method.Value?.ToString() ?? "", logs[^1].Uri.PathAndQuery) : null;
            _seen = logs.Count;
            Sent.Add(new SentRequest("", request.Method.Method, uri, recorded));
            var status = request.Method.Method switch
            {
                "PUT" or "POST" => HttpStatusCode.Created,
                "DELETE" => HttpStatusCode.Accepted,
                _ => HttpStatusCode.OK,
            };
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("", Encoding.UTF8, "application/xml") });
        }
    }
}
