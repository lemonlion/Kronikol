using System.Net;
using System.Text;
using Azure.Storage.Queues;
using Kronikol.Extensions.StorageQueues;
using Kronikol.Tracking;

namespace Kronikol.Tests.StorageQueues;

/// <summary>
/// The real <see cref="QueueClient"/> writes every URL here, so these facts read what the SDK sends rather than what a
/// test's author believed it sends (#125, plans/EMULATOR_URL_SHAPES_PLAN.md section 6.1). The client goes through the
/// shipped handler to a stub server, under each connection a suite gives it: a production account, Azurite through
/// <c>UseDevelopmentStorage=true</c>, and Azurite's account on <c>127.0.0.1</c> on another port, on a docker compose
/// name on the emulator's port, on <c>localhost</c> on another port and on a Docker host's name on another port. They
/// assert how each request was named, never which requests a client version sends, so they hold on the floor too.
/// </summary>
public class QueueClientUrlShapeTests
{
    // Azurite's well-known key for its built-in account.
    private const string Key = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

    private const string Production = "DefaultEndpointsProtocol=https;AccountName=shop;AccountKey=" + Key + ";EndpointSuffix=core.windows.net";

    public static TheoryData<string> Connections => new()
    {
        Production,
        "UseDevelopmentStorage=true",
        Azurite("http://127.0.0.1:54322"),
        Azurite("http://azurite:10001"),
        Azurite("http://localhost:54322"),
        Azurite("http://docker-host.internal:54322"),
    };

    private static string Azurite(string host) =>
        $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey={Key};QueueEndpoint={host}/devstoreaccount1;";

    [Fact]
    public void Every_operation_reaches_the_server()
    {
        var run = Run(Production, StorageQueueTrackingVerbosity.Detailed);

        Assert.Equal(run.Operations, run.Sent.Select(s => s.Operation).Distinct());
    }

    [Theory]
    [MemberData(nameof(Connections))]
    public void Every_call_is_named_at_Detailed(string connection)
    {
        var run = Run(connection, StorageQueueTrackingVerbosity.Detailed);

        Assert.All(run.Sent, s => Assert.NotNull(s.Recorded));
        Assert.DoesNotContain(run.Sent, s => s.Recorded!.Value.Label == "Other");
    }

    [Theory]
    [MemberData(nameof(Connections))]
    public void Every_call_is_recorded_at_Summarised(string connection)
    {
        var run = Run(connection, StorageQueueTrackingVerbosity.Summarised);

        Assert.All(run.Sent, s => Assert.NotNull(s.Recorded));
        Assert.DoesNotContain(run.Sent, s => s.Recorded!.Value.Label == "Other");
    }

    [Theory]
    [MemberData(nameof(Connections))]
    public void A_call_reads_the_same_under_any_connection_as_on_a_production_account(string connection)
    {
        var production = Run(Production, StorageQueueTrackingVerbosity.Detailed);
        var other = Run(connection, StorageQueueTrackingVerbosity.Detailed);

        Assert.Equal(production.Sent.Select(s => (s.Operation, s.Recorded)), other.Sent.Select(s => (s.Operation, s.Recorded)));
    }

    [Theory]
    [MemberData(nameof(Connections))]
    public void A_message_is_sent_to_and_received_from_its_queue(string connection)
    {
        var run = Run(connection, StorageQueueTrackingVerbosity.Detailed);

        Assert.Equal(("Send → orders", "/orders"), Assert.Single(run.Sent, s => s.Operation == "SendMessage").Recorded);
        Assert.Equal(("Receive ← orders", "/orders"), Assert.Single(run.Sent, s => s.Operation == "ReceiveMessages").Recorded);
        Assert.Equal(("ListQueues", "/"), Assert.Single(run.Sent, s => s.Operation == "ListQueues").Recorded);
    }

    // One run of the client's operations under a connection and a verbosity, each request paired with what the handler
    // recorded for it: its label and the path and query of its URI.
    private static Recording Run(string connection, StorageQueueTrackingVerbosity verbosity)
    {
        var testId = Guid.NewGuid().ToString();
        var stub = new StubQueueServer(() => RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId && l.Type == RequestResponseType.Request).ToList());
        var options = new QueueClientOptions().WithTestTracking(new StorageQueueTrackingMessageHandlerOptions
        {
            Verbosity = verbosity,
            CurrentTestInfoFetcher = () => ("Queue URL shapes", testId),
        }, stub);
        options.Retry.MaxRetries = 0;
        var service = new QueueServiceClient(connection, options);
        var queue = service.GetQueueClient("orders");

        var operations = new (string Name, Action Call)[]
        {
            ("CreateQueue", () => queue.Create()),
            ("SendMessage", () => queue.SendMessage("hello")),
            ("ReceiveMessages", () => queue.ReceiveMessages()),
            ("PeekMessages", () => queue.PeekMessages()),
            ("DeleteMessage", () => queue.DeleteMessage("m1", "pop1")),
            ("ClearMessages", () => queue.ClearMessages()),
            ("DeleteQueue", () => queue.Delete()),
            ("ListQueues", () => service.GetQueues().Take(1).ToList()),
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
    private sealed class StubQueueServer(Func<List<RequestResponseLog>> requestLogs) : HttpMessageHandler
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
                "DELETE" => uri.AbsolutePath.Contains("/messages") ? HttpStatusCode.NoContent : HttpStatusCode.Accepted,
                _ => HttpStatusCode.OK,
            };
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("", Encoding.UTF8, "application/xml") });
        }
    }
}
