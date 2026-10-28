using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kronikol.CallGraph;
using Kronikol.Parties;
using Kronikol.Reports;

namespace Kronikol.Tests.CallGraph;

/// <summary>
/// plans/SERVICE_CARD_PLAN.md 5.2: <c>service.json</c> from hand-written records. Its <c>behaviour</c> part is sets in a
/// total order and is byte-identical whenever the behaviour is; counts, order and what moves between scenarios through
/// shared state are the run's (4.3). Every card a fact writes is also checked for its hash (5.2.9).
/// </summary>
public class ServiceCardTests
{
    private static readonly CallScenario Pancakes = Records.Scenario(0, "Order pancakes");
    private static readonly CallScenario Menu = Records.Scenario(1, "Read the menu");

    internal static string Card(Graph graph, ServiceCardRun? run = null)
    {
        var text = ServiceCardWriter.Write(graph, run ?? ServiceCardRun.Empty).Text;
        AssertHashHolds(text);
        return text;
    }

    /// <summary>
    /// 5.2.9: <c>behaviourHash</c> is the SHA-256 of the bytes from the <c>{</c> after the last <c>"behaviour":</c> to the
    /// byte before the file's closing <c>}</c>, read with no JSON serializer, as a reader in any language would.
    /// </summary>
    internal static void AssertHashHolds(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        Assert.EndsWith("}\n", text, StringComparison.Ordinal);
        var marker = Encoding.UTF8.GetBytes("\"behaviour\":");
        var at = bytes.AsSpan().LastIndexOf(marker);
        Assert.True(at > 0, "the file holds a behaviour member");
        var range = bytes.AsSpan(at + marker.Length, bytes.Length - 2 - (at + marker.Length));
        Assert.Equal((byte)'{', range[0]);
        using var document = JsonDocument.Parse(text);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(range)), document.RootElement.GetProperty("run").GetProperty("behaviourHash").GetString());
    }

    internal static string Behaviour(string card)
    {
        using var document = JsonDocument.Parse(card);
        return document.RootElement.GetProperty("behaviour").GetRawText();
    }

    internal static JsonElement Root(string card)
    {
        using var document = JsonDocument.Parse(card);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// A small in-process run: the test orders pancakes (the service asks Cow Service for milk and publishes to Kafka inside
    /// that request) and reads the menu (the service asks its supplier), and the service reads Cosmos DB's metadata outside
    /// any request, in the test's context, which is background work.
    /// </summary>
    private static Records Run(int orders = 1, bool kafkaFirst = false, bool metadataInMenu = false, bool supplierInPancakes = false)
    {
        var r = new Records();
        for (var i = 0; i < orders; i++)
        {
            var order = r.Request(Pancakes, "Caller", "Breakfast Provider", "POST", $"http://localhost/orders/{100 + i}", attribution: "TestContext", phase: "Action");
            if (kafkaFirst)
                r.Call(Pancakes, "Breakfast Provider", "Kafka", "Produce → orders", "kafka:///orders", attribution: "RequestHeader", category: "MessageQueue");
            r.Call(Pancakes, "Breakfast Provider", "Cow Service", "GET", "http://localhost:5001/milk", attribution: "RequestHeader");
            if (!kafkaFirst)
                r.Call(Pancakes, "Breakfast Provider", "Kafka", "Produce → orders", "kafka:///orders", attribution: "RequestHeader", category: "MessageQueue");
            if (supplierInPancakes)
                r.Call(Pancakes, "Breakfast Provider", "Supplier Service", "GET", "http://localhost:5004/supplies", attribution: "RequestHeader");
            r.Answer(order, "201");
        }

        if (!metadataInMenu)
            r.Call(Pancakes, "Breakfast Provider", "CosmosDB", "Other", "https://localhost:8081/dbs/BreakfastDb", attribution: "TestContext", category: "CosmosDB");

        var menu = r.Request(Menu, "Caller", "Breakfast Provider", "GET", "http://localhost/menu", attribution: "TestContext", phase: "Action");
        if (!supplierInPancakes)
            r.Call(Menu, "Breakfast Provider", "Supplier Service", "GET", "http://localhost:5004/supplies", attribution: "RequestHeader");
        r.Answer(menu);
        if (metadataInMenu)
            r.Call(Menu, "Breakfast Provider", "CosmosDB", "Other", "https://localhost:8081/dbs/BreakfastDb", attribution: "TestContext", category: "CosmosDB");
        return r;
    }

    private static Graph Build(Records records, PartiesInput? parties = null) =>
        CallGraphBuilder.Build([Pancakes, Menu], records.All, parties ?? PartiesInput.Inferred);

    // 5.2.1
    [Fact]
    public void Counts_order_and_run_id_change_the_run_part_and_never_the_behaviour()
    {
        var once = Card(Build(Run()), new ServiceCardRun { Id = "run-1" });
        var twice = Card(Build(Run(orders: 2, kafkaFirst: true)), new ServiceCardRun { Id = "run-2" });

        Assert.Equal(Behaviour(once), Behaviour(twice));
        Assert.Equal(Root(once).GetProperty("run").GetProperty("behaviourHash").GetString(), Root(twice).GetProperty("run").GetProperty("behaviourHash").GetString());
        Assert.NotEqual(Root(once).GetProperty("run").GetRawText(), Root(twice).GetProperty("run").GetRawText());
    }

    // 5.2.1: parallel steps reorder a scenario's calls, each call's records keeping their order, and the scenarios come in
    // any order.
    [Fact]
    public void Calls_reordered_within_each_scenario_give_the_same_behaviour()
    {
        var records = Run().All;
        var pancakes = records.Where(r => r.Scenario == Pancakes).ToList();
        // the background read first, then the order with its calls inside it, each record keeping its own position
        var metadata = pancakes.Where(r => r.Service == "CosmosDB").ToList();
        var reordered = metadata.Concat(pancakes.Except(metadata)).Concat(records.Where(r => r.Scenario == Menu)).ToList();

        var straight = Card(CallGraphBuilder.Build([Pancakes, Menu], records, PartiesInput.Inferred));
        var shuffled = Card(CallGraphBuilder.Build([Menu, Pancakes], [.. records.Where(r => r.Scenario == Menu), .. reordered.Where(r => r.Scenario == Pancakes)], PartiesInput.Inferred));

        Assert.Equal(Behaviour(straight), Behaviour(shuffled));
    }

    // 5.2.1, Q6: a background call lands in whichever scenario is running. It is in no scenario's operations, in either
    // part, so only its first address moves.
    [Fact]
    public void A_background_call_moving_between_scenarios_moves_its_example_only()
    {
        var before = Card(Build(Run()));
        var after = Card(Build(Run(metadataInMenu: true)));

        Assert.Equal(Behaviour(before), Behaviour(after));
        Assert.Equal(Root(before).GetProperty("run").GetProperty("scenarios").GetRawText(), Root(after).GetProperty("run").GetProperty("scenarios").GetRawText());
        Assert.NotEqual(Root(before).GetProperty("run").GetProperty("examples").GetRawText(), Root(after).GetProperty("run").GetProperty("examples").GetRawText());
    }

    // 5.2.1, Q16: which scenario misses a host-wide cache, and so calls the supplier, depends on order and timing (2.4).
    [Fact]
    public void A_downstream_call_moving_between_scenarios_moves_the_run_part_only()
    {
        var before = Card(Build(Run()));
        var after = Card(Build(Run(supplierInPancakes: true)));

        Assert.Equal(Behaviour(before), Behaviour(after));
        Assert.NotEqual(Root(before).GetProperty("run").GetProperty("scenarios").GetRawText(), Root(after).GetProperty("run").GetProperty("scenarios").GetRawText());
    }

    // 5.2.2: an operation's id is a hash of what it is, never a position, so a new one moves no other reference.
    [Fact]
    public void One_new_operation_adds_one_op_and_changes_nothing_else_in_the_behaviour()
    {
        var before = Run();
        var after = Run();
        var cream = after.Request(Pancakes, "Caller", "Breakfast Provider", "POST", "http://localhost/orders/7", attribution: "TestContext", phase: "Action");
        after.Call(Pancakes, "Breakfast Provider", "Cow Service", "GET", "http://localhost:5001/cream", attribution: "RequestHeader");
        after.Answer(cream, "201");

        var old = JsonNode.Parse(Behaviour(Card(Build(before))))!;
        var added = JsonNode.Parse(Behaviour(Card(Build(after))))!;

        var cow = added["edges"]!.AsArray().Single(e => (string?)e!["to"] == "Cow Service")!["operations"]!.AsArray();
        var creamOp = cow.Single(o => (string?)o!["target"] == "/cream")!;
        cow.Remove(creamOp);
        Assert.True(JsonNode.DeepEquals(old, added), $"before:\n{old}\nafter, the new op taken out:\n{added}");
    }

    // 5.2.3: names that sort differently there: "ch" after "h" in Czech, "Æ" after "z" in Danish, the dotted and dotless I in
    // Turkish, and capitals, which a culture sorts beside their lower case and ordinal comparison before every lower case.
    [Theory]
    [InlineData("cs-CZ")]
    [InlineData("da-DK")]
    [InlineData("tr-TR")]
    public void The_card_is_the_same_bytes_under_any_culture(string culture)
    {
        static Records Named()
        {
            var r = new Records();
            var order = r.Request(Pancakes, "caller", "Hotel", "GET", "http://localhost/rooms/1", attribution: "TestContext", phase: "Action");
            foreach (var name in new[] { "Ch Service", "India", "ındex", "Æble", "alpha", "Bravo" })
                r.Call(Pancakes, "Hotel", name, "GET", $"http://localhost/{name.Length}/item", attribution: "RequestHeader", status: "200");
            // one operation answered two ways: "Responded" before "none" by ordinal comparison, after it in every culture
            r.Call(Pancakes, "Hotel", "Rooms Queue", "Send", "event://rooms", attribution: "RequestHeader", status: "Responded");
            r.Request(Pancakes, "Hotel", "Rooms Queue", "Send", "event://rooms", attribution: "RequestHeader");
            r.Answer(order, "200");
            return r;
        }

        var invariant = CultureRun.Under("en-US", () => Card(Build(Named()), new ServiceCardRun { Scenarios = 1, Passed = 1 }));
        var there = CultureRun.Under(culture, () => Card(Build(Named()), new ServiceCardRun { Scenarios = 1, Passed = 1 }));

        Assert.Equal(invariant, there);
        var parties = Root(there).GetProperty("behaviour").GetProperty("parties").EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToList();
        Assert.Equal(["Bravo", "Ch Service", "Hotel", "India", "Rooms Queue", "alpha", "caller", "Æble", "ındex"], parties);
        var send = Root(there).GetProperty("behaviour").GetProperty("edges").EnumerateArray().Single(e => e.GetProperty("to").GetString() == "Rooms Queue")
            .GetProperty("operations").EnumerateArray().Single();
        Assert.Equal(["Responded", "none"], send.GetProperty("statuses").EnumerateArray().Select(s => s.GetString()));
    }

    // 5.2.4: the issue's fixture, as a run that declares its double and its real database writes its block.
    [Fact]
    public void The_issues_fixture_gives_each_party_its_kind_and_each_call_its_origin()
    {
        var r = new Records();
        var seed = r.Call(Pancakes, "Caller", "Orders Db", "INSERT", "sql://localhost/orders", attribution: "TestContext", phase: "Setup", category: "SQL", content: "INSERT INTO orders (id) VALUES (123)");
        foreach (var id in new[] { 123, 456 })
        {
            var get = r.Request(Pancakes, "Caller", "Breakfast Provider", "GET", $"http://localhost/orders/{id}", attribution: "TestContext", phase: "Action");
            r.Call(Pancakes, "Breakfast Provider", "Orders Db", "SELECT", "sql://localhost/orders", attribution: "RequestHeader", category: "SQL", content: $"SELECT * FROM orders WHERE id = {id}");
            r.Call(Pancakes, "Breakfast Provider", "Cow Service", "GET", "http://localhost:5001/milk", attribution: "RequestHeader", testDouble: "WireMock");
            r.Answer(get);
        }

        var graph = Build(r, Declared(r, ("Cow Service", PartyKind.Double), ("Orders Db", PartyKind.Real)));

        Assert.Equal(
        [
            ("Breakfast Provider", PartyKind.UnderTest), ("Caller", PartyKind.Test), ("Cow Service", PartyKind.Double), ("Orders Db", PartyKind.Real)
        ], graph.Parties.Select(p => (p.Name, p.Kind)));
        Assert.Equal(("test", "test"), (graph.Calls.Single(c => c.Request.RequestResponseId == seed.RequestResponseId).Origin, graph.Calls.Single(c => c.Request.RequestResponseId == seed.RequestResponseId).Cause));
        Assert.All(graph.Calls.Where(c => c.Request.Caller == "Breakfast Provider"), c => Assert.Equal("subject", c.Origin));
        Assert.Equal(["/orders/{n}"], graph.Operations.Where(o => o.From == "Caller" && o.To == "Breakfast Provider").Select(o => o.Target));
        Assert.Equal(2, graph.Parties.Single(p => p.Name == "Cow Service").DoubleCalls);
        Card(graph);
    }

    // 5.2.4, the same fixture seeding through the service's own client: the documented limit (4.2).
    [Fact]
    public void Seeding_through_the_subjects_client_reads_origin_subject_and_cause_background()
    {
        var r = new Records();
        var seed = r.Call(Pancakes, "Breakfast Provider", "Orders Db", "INSERT", "sql://localhost/orders", attribution: "TestContext", phase: "Setup", category: "SQL", content: "INSERT INTO orders (id) VALUES (123)");
        var get = r.Request(Pancakes, "Caller", "Breakfast Provider", "GET", "http://localhost/orders/123", attribution: "TestContext", phase: "Action");
        r.Call(Pancakes, "Breakfast Provider", "Orders Db", "SELECT", "sql://localhost/orders", attribution: "RequestHeader", category: "SQL", content: "SELECT * FROM orders WHERE id = 123");
        r.Answer(get);

        var graph = Build(r, Declared(r, ("Orders Db", PartyKind.Real)));
        var call = graph.Calls.Single(c => c.Request.RequestResponseId == seed.RequestResponseId);

        Assert.Equal(("subject", "background"), (call.Origin, call.Cause));
        Assert.DoesNotContain(call.Op, graph.Scenarios.Single(s => s.Scenario == Pancakes).Ops);
    }

    /// <summary>The block a run that declares <paramref name="kinds"/> writes: the resolver over its calls, as the run end resolves it.</summary>
    private static PartiesInput Declared(Records records, params (string Name, PartyKind Kind)[] kinds)
    {
        var calls = records.All.Where(c => c.IsRequest).Select(c => new PartyCall(c.Caller, c.Service, c.Category, c.CallerCategory, c.AttributionSource, c.TestDouble));
        var table = PartyResolver.Resolve(calls, new PartyDeclarations("breakfast-provider", [.. kinds.Select(k => new PartyDescriptor(k.Name) { Kind = k.Kind })], [], []), PartyObservations.None);
        return new PartiesInput(table.Parties, table.ServiceId);
    }

    // 5.2.5: an external-SUT lane records none of the service's calls, which is not a service with no dependencies.
    [Fact]
    public void A_run_that_recorded_none_of_the_subjects_calls_says_so()
    {
        var r = new Records();
        r.Call(Pancakes, "Caller", "Breakfast Provider", "GET", "http://localhost/menu", attribution: "TestContext", phase: "Action");

        var capture = Root(Card(Build(r))).GetProperty("behaviour").GetProperty("capture");

        Assert.Equal(("recorded", "notRecorded"), (capture.GetProperty("calls").GetString(), capture.GetProperty("subjectCalls").GetString()));
    }

    [Fact]
    public void A_run_that_made_no_call_recorded_its_calls_and_has_no_edges()
    {
        var card = Root(Card(CallGraphBuilder.Build([Pancakes], [], PartiesInput.Inferred)));

        Assert.Equal("recorded", card.GetProperty("behaviour").GetProperty("capture").GetProperty("calls").GetString());
        Assert.Equal(0, card.GetProperty("behaviour").GetProperty("edges").GetArrayLength());
    }

    [Fact]
    public void A_report_that_cannot_carry_calls_says_not_recorded()
    {
        var card = Root(Card(CallGraphBuilder.Build([Pancakes], [], PartiesInput.Inferred, callsRecorded: false)));

        Assert.Equal("notRecorded", card.GetProperty("behaviour").GetProperty("capture").GetProperty("calls").GetString());
    }

    [Fact]
    public void Only_a_destination_the_capture_did_not_record_is_an_empty_target()
    {
        var r = Run();
        r.Call(Pancakes, "Breakfast Provider", "Kafka", "Produce", "kafka:///", attribution: "RequestHeader", category: "MessageQueue");
        r.Call(Pancakes, "Caller", "Breakfast Provider", "GET", "http://localhost/", attribution: "TestContext", phase: "Action");
        r.Call(Pancakes, "Breakfast Provider", "Service Bus", "Send", "servicebus://orders/", attribution: "RequestHeader", category: "ServiceBus");

        var graph = Build(r);
        var empty = Root(Card(graph)).GetProperty("behaviour").GetProperty("capture").GetProperty("emptyTargets").EnumerateArray().Select(e => e.GetString()).ToList();

        var produce = graph.Operations.Single(o => o.To == "Kafka" && o.Method == "Produce").Op;
        Assert.Equal([produce], empty);
    }

    // 5.2.6
    [Fact]
    public void What_the_report_does_not_say_is_null_never_guessed()
    {
        var r = Run();
        var card = Root(Card(Build(r)));
        var run = card.GetProperty("run");
        var behaviour = card.GetProperty("behaviour");

        Assert.Equal(JsonValueKind.Null, run.GetProperty("framework").ValueKind);
        Assert.Equal(JsonValueKind.Null, run.GetProperty("id").ValueKind);
        Assert.Equal(JsonValueKind.Null, run.GetProperty("at").ValueKind);
        Assert.Equal(JsonValueKind.Null, behaviour.GetProperty("scenarios")[0].GetProperty("source").ValueKind);
        Assert.Equal(JsonValueKind.Null, behaviour.GetProperty("rules").GetProperty("templateRules").ValueKind);
        var parties = behaviour.GetProperty("parties").EnumerateArray().ToDictionary(p => p.GetProperty("name").GetString()!);
        Assert.All(parties.Values, p => Assert.Equal(JsonValueKind.Null, p.GetProperty("id").ValueKind));
        Assert.Equal("inferred", parties["Caller"].GetProperty("kindSource").GetString());
        Assert.Equal("inferred", parties["Breakfast Provider"].GetProperty("kindSource").GetString());
        Assert.Equal(JsonValueKind.Null, parties["Cow Service"].GetProperty("kindSource").ValueKind);
        Assert.Equal(PartyResolver.InferenceVersion, behaviour.GetProperty("rules").GetProperty("inference").GetInt32());
        Assert.Equal(["Breakfast Provider", "Caller", "CosmosDB", "Cow Service", "Kafka", "Supplier Service"],
            behaviour.GetProperty("unresolved").EnumerateArray().Select(n => n.GetString()));
    }

    [Fact]
    public void A_scenario_with_a_source_gives_its_file_and_line_and_one_with_only_its_features_file_a_null_line()
    {
        var own = Pancakes with { SourceFile = "Features/Orders.feature", SourceLine = 12 };
        var feature = Menu with { FeatureSourceFile = "Features/Menu.feature" };
        var r = new Records();
        r.Call(own, "Caller", "Breakfast Provider", "GET", "http://localhost/orders/1", attribution: "TestContext", phase: "Action");

        var scenarios = Root(Card(CallGraphBuilder.Build([own, feature], r.All, PartiesInput.Inferred))).GetProperty("behaviour").GetProperty("scenarios");

        Assert.Equal(("Features/Orders.feature", 12), (scenarios[0].GetProperty("source").GetProperty("file").GetString(), scenarios[0].GetProperty("source").GetProperty("line").GetInt32()));
        Assert.Equal("Features/Menu.feature", scenarios[1].GetProperty("source").GetProperty("file").GetString());
        Assert.Equal(JsonValueKind.Null, scenarios[1].GetProperty("source").GetProperty("line").ValueKind);
    }

    // Every list is sorted: a tag moved from the feature to its scenarios changes no behaviour.
    [Fact]
    public void A_scenarios_tags_are_sorted_whatever_level_they_came_from()
    {
        var tagged = Pancakes with { Tags = ["SkipUnlessFakesControllable", "MenuCaching"] };

        var tags = Root(Card(CallGraphBuilder.Build([tagged], [], PartiesInput.Inferred))).GetProperty("behaviour").GetProperty("scenarios")[0].GetProperty("tags");

        Assert.Equal(["MenuCaching", "SkipUnlessFakesControllable"], tags.EnumerateArray().Select(t => t.GetString()));
    }

    [Fact]
    public void Every_row_read_from_the_block_leaves_the_inference_version_null()
    {
        var r = Run();
        var card = Root(Card(Build(r, Declared(r, ("Cow Service", PartyKind.Double)))));

        Assert.Equal(JsonValueKind.Null, card.GetProperty("behaviour").GetProperty("rules").GetProperty("inference").ValueKind);
        Assert.Equal("breakfast-provider", card.GetProperty("behaviour").GetProperty("subject").GetProperty("id").GetString());
    }

    [Fact]
    public void Scenarios_that_are_not_tests_are_left_out_and_rows_sharing_a_stable_id_are_one()
    {
        var retry = Pancakes with { Ordinal = 2, Name = "Order pancakes (retry)" };
        var startUp = Records.Scenario(3, "Start the stack") with { IsTest = false };
        var r = new Records();
        r.Call(Pancakes, "Caller", "Breakfast Provider", "GET", "http://localhost/orders/1", attribution: "TestContext", phase: "Action");
        r.Call(retry, "Caller", "Breakfast Provider", "DELETE", "http://localhost/orders/1", attribution: "TestContext", phase: "Action");
        r.Call(startUp, "Caller", "Breakfast Provider", "GET", "http://localhost/health", attribution: "TestContext");

        var scenarios = Root(Card(CallGraphBuilder.Build([Pancakes, retry, startUp], r.All, PartiesInput.Inferred))).GetProperty("behaviour").GetProperty("scenarios");

        var row = Assert.Single(scenarios.EnumerateArray());
        Assert.Equal("Order pancakes", row.GetProperty("scenario").GetString());
        Assert.Equal(2, row.GetProperty("ops").GetArrayLength());
    }

    // 5.2.9: the default encoder writes `"` as " and escapes `+` and any non-ASCII letter, which another serializer
    // need not, so the hash is over the bytes the file holds, never over bytes written again.
    [Fact]
    public void The_hash_is_over_the_bytes_the_file_holds_with_escapes_another_serializer_would_not_write()
    {
        var r = new Records();
        var order = r.Request(Pancakes, "Caller", "Café \"Plus\" + Co", "GET", "http://localhost/crêpes", attribution: "TestContext", phase: "Action");
        r.Answer(order);

        var card = Card(Build(r));

        Assert.Contains("\\u0022Plus\\u0022", card, StringComparison.Ordinal);
        Assert.Contains("\\u002B", card, StringComparison.Ordinal);
        Assert.Contains("Caf\\u00E9", card, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", card.TrimEnd('\n'), StringComparison.Ordinal);
    }

    // 5.2.8: a card of a large run in one pass, the nesting's open list included (one scenario of a thousand calls).
    [Fact]
    public void A_large_run_is_written_within_its_budget()
    {
        var scenarios = Enumerable.Range(0, 2000).Select(i => Records.Scenario(i, $"Scenario {i}")).ToList();
        var r = new Records();
        foreach (var scenario in scenarios)
        {
            var call = r.Request(scenario, "Caller", "Breakfast Provider", "POST", $"http://localhost/orders/{scenario.Ordinal}", attribution: "TestContext", phase: "Action");
            r.Call(scenario, "Breakfast Provider", "Orders Db", "INSERT", "sql://localhost/orders", attribution: "RequestHeader", category: "SQL", content: "INSERT INTO orders (id) VALUES (1)");
            r.Answer(call, "201");
        }
        // a thousand calls open at once, each found inside the batch past every one opened before it
        var wide = scenarios[0];
        var batch = r.Request(wide, "Caller", "Breakfast Provider", "POST", "http://localhost/batch", attribution: "TestContext", phase: "Action");
        var milk = Enumerable.Range(0, 1000).Select(i => r.Request(wide, "Breakfast Provider", "Cow Service", "GET", $"http://localhost:5001/milk/{i}", attribution: "RequestHeader")).ToList();
        foreach (var call in milk)
            r.Answer(call);
        r.Answer(batch);

        var scale = ContentionScale.Measure();
        var watch = Stopwatch.StartNew();
        var card = Card(CallGraphBuilder.Build(scenarios, r.All, PartiesInput.Inferred));
        watch.Stop();

        Assert.Equal(2000, Root(card).GetProperty("behaviour").GetProperty("scenarios").GetArrayLength());
        Assert.True(watch.Elapsed.TotalMilliseconds < BudgetMs * scale, $"{watch.Elapsed.TotalMilliseconds:F0} ms against {BudgetMs * scale:F0}");
    }

    /// <summary>Set from R1's first measurement of <see cref="A_large_run_is_written_within_its_budget"/>.</summary>
    private const double BudgetMs = 5000;
}
