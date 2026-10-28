using System.Net;
using Kronikol.CallGraph;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.CallGraph;

/// <summary>
/// plans/SERVICE_CARD_PLAN.md 5.1.2: over the same calls and with no participant filter, the card's edges are the
/// component diagram's, since both take the protocol from one rule. Parity cannot see a change to the rule both sides
/// share, so the rule itself is pinned shape by shape.
/// </summary>
[Collection("DiagramsFetcher")]
public class CallGraphEdgeTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-card-edges").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static readonly CallScenario S0 = Records.Scenario(0, "Order pancakes");

    [Fact]
    public void The_protocol_is_the_category_else_an_events_method_else_Event_else_HTTP()
    {
        var records = new Records();
        records.Call(S0, "Caller", "Breakfast Provider", "GET", "http://localhost/menu", attribution: "TestContext");
        records.Call(S0, "Breakfast Provider", "Orders Db", "SELECT", "sql://localhost/orders", attribution: "RequestHeader", category: "SQL", content: "SELECT * FROM orders");
        records.Call(S0, "Breakfast Provider", "Ledger", "OrderPlaced", "event://ledger", attribution: "RequestHeader", isEvent: true);
        records.Call(S0, "Breakfast Provider", "Audit", null!, "event://audit", attribution: "RequestHeader", isEvent: true);

        var edges = CallGraphBuilder.Build([S0], records.All, PartiesInput.Inferred).Edges
            .Select(e => (e.From, e.To, e.Protocol)).ToList();

        Assert.Equal(
        [
            ("Breakfast Provider", "Audit", "Event"),
            ("Breakfast Provider", "Ledger", "OrderPlaced"),
            ("Breakfast Provider", "Orders Db", "SQL"),
            ("Caller", "Breakfast Provider", "HTTP")
        ], edges);
    }

    // BreakfastProvider sets its consume trackers' DependencyCategory to "" so the service is not drawn as a queue: an empty
    // category is no category, so the delivery is drawn with its event's method, where the edge's label read ": Consume (Kafka)".
    [Fact]
    public void An_empty_category_is_no_category_in_the_diagram_and_the_card()
    {
        var (trace, pair) = (Guid.NewGuid(), Guid.NewGuid());
        RequestResponseLog[] logs =
        [
            new("scenario", "t1", "Consume (Kafka)", null, new Uri("kafka:///orders"), [], "Breakfast Provider", "Kafka Broker", RequestResponseType.Request, trace, pair, false,
                null, RequestResponseMetaType.Event, DependencyCategory: "", CallerDependencyCategory: "MessageQueue")
        ];
        Assert.Equal("Consume (Kafka)", Assert.Single(Kronikol.ComponentDiagram.ComponentDiagramGenerator.ExtractRelationships(logs)).Protocol);

        var records = new Records();
        records.Call(S0, "Kafka Broker", "Breakfast Provider", "Consume (Kafka)", "kafka:///orders", attribution: "RequestHeader", category: "", callerCategory: "MessageQueue", isEvent: true);
        Assert.Equal("Consume (Kafka)", Assert.Single(CallGraphBuilder.Build([S0], records.All, PartiesInput.Inferred).Edges).Protocol);
    }

    [Fact]
    public void Over_a_written_data_file_the_cards_edges_are_the_component_diagrams()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Breakfast",
                Scenarios =
                [
                    new Scenario { Id = "t1", DisplayName = "Order pancakes", Result = ExecutionResult.Passed },
                    new Scenario { Id = "t2", DisplayName = "Order waffles", Result = ExecutionResult.Failed }
                ]
            }
        };
        RequestResponseLog[] logs =
        [
            .. Pair("t1", "Caller", "Breakfast Provider", HttpMethod.Post, "http://localhost/orders"),
            .. Pair("t1", "Breakfast Provider", "Orders Db", "INSERT", "sql://localhost/orders", category: "SQL"),
            .. Pair("t2", "Breakfast Provider", "Ledger", "OrderPlaced", "event://ledger", metaType: RequestResponseMetaType.Event),
            .. Pair("t2", "Kafka", "Breakfast Provider", "Consume ← orders", "kafka:///orders", category: "MessageQueue"),
            .. Pair(TestIdentityScope.UnknownTestId, "Breakfast Provider", "Outbox Store", HttpMethod.Post, "http://localhost:5002/outbox"),
            // a call whose test id names no scenario: the data file leaves it out, and the diagram's input here does too
            .. Pair("t-gone", "Breakfast Provider", "Stray", HttpMethod.Get, "http://localhost:5009/stray")
        ];
        var path = ReportGenerator.GenerateTestRunReportData(features, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, Path.Combine(_dir, "TestRunReport.json"), DataFormat.Json, null, logs);

        using var index = ReportScanner.Scan(path);
        var card = ServiceCard.GraphOf(index).Edges.Select(e => (e.From, e.To, e.Protocol)).ToHashSet();

        var carried = new HashSet<string>(["t1", "t2", TestIdentityScope.UnknownTestId], StringComparer.Ordinal);
        var diagram = Kronikol.ComponentDiagram.ComponentDiagramGenerator.ExtractRelationships(logs.Where(l => carried.Contains(l.TestId)))
            .Select(r => (r.Caller, r.Service, r.Protocol)).ToHashSet();

        Assert.Equal(4 + 1, card.Count);
        Assert.Equal(diagram, card);
    }

    private static RequestResponseLog[] Pair(string testId, string caller, string service, OneOf<HttpMethod, string> method, string url,
        string? category = null, RequestResponseMetaType metaType = default)
    {
        var (trace, pair) = (Guid.NewGuid(), Guid.NewGuid());
        return
        [
            new RequestResponseLog("scenario", testId, method, null, new Uri(url), [], service, caller, RequestResponseType.Request, trace, pair, false, null, metaType, category)
                { Timestamp = DateTimeOffset.UtcNow },
            new RequestResponseLog("scenario", testId, method, null, new Uri(url), [], service, caller, RequestResponseType.Response, trace, pair, false, HttpStatusCode.OK, metaType, category)
                { Timestamp = DateTimeOffset.UtcNow }
        ];
    }
}
