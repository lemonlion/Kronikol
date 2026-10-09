#:package Kronikol@4.14.4
#:property PublishAot=false
#:property InvariantGlobalization=true
#:property AssemblyName=formatter-probe
#:property Nullable=enable
// Kronikol 4.14.4's own emitters, for the #162 follow-up (the other statement caps in the worker). The assembly is named
// formatter-probe because Kronikol grants that name its internals (StepBarPlantUml, ComponentFlowSegmentBuilder).
//   dotnet run --file gen.cs -- <out dir>
// Writes <out>/<kind>/<k>.puml for the length-driven shapes (k = the name or span-name length) and the fixed shapes
// at the top of <out>: the collapsed-run loop, the setup partition, both step-bar forms, the stats-linked edge.
using System.Net;
using Kronikol;
using Kronikol.ComponentDiagram;
using Kronikol.InternalFlow;
using Kronikol.PlantUml;
using Kronikol.Tracking;

var outDir = Path.GetFullPath(args[0]);
Directory.CreateDirectory(outDir);
var t0 = new DateTimeOffset(2026, 10, 9, 10, 0, 0, TimeSpan.Zero);
var tick = 0;
DateTimeOffset Next() => t0.AddMilliseconds(37 * ++tick);

List<RequestResponseLog> Call(string test, string caller, string service, OneOf<HttpMethod, string> method, string url,
    string? cat = null, TestPhase phase = TestPhase.Unknown)
{
    var trace = Guid.NewGuid(); var id = Guid.NewGuid();
    var req = new RequestResponseLog(test, test, method, null, new Uri(url), [], service, caller, RequestResponseType.Request, trace, id, false,
        null, RequestResponseMetaType.Default, cat) { Timestamp = Next(), Phase = phase };
    var res = new RequestResponseLog(test, test, method, "{ \"ok\": true }", new Uri(url), [], service, caller, RequestResponseType.Response, trace, id, false,
        HttpStatusCode.OK, RequestResponseMetaType.Default, cat) { Timestamp = Next(), Phase = phase };
    return [req, res];
}
List<RequestResponseLog> Marker(string test, string plantUml, DiagramMarkerKind kind, bool actionStart = false) =>
[
    new RequestResponseLog(test, test, "", "", new Uri("http://override.com"), [], "", "", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
    { IsOverrideStart = true, MarkerKind = kind, PlantUml = "\n" + plantUml + "\n\n", Timestamp = Next(), IsActionStart = actionStart },
    new RequestResponseLog(test, test, "", "", new Uri("http://override.com"), [], "", "", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
    { IsOverrideEnd = true, MarkerKind = kind, Timestamp = Next() },
];
// As a BrowserJs report calls it (clientSideSplitting: the page splits).
string Sequence(List<RequestResponseLog> logs, bool collapse = false, bool separateSetup = false) =>
    string.Join("\n@@FRAGMENT@@\n", PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, separateSetup: separateSetup,
        collapseConsecutiveIdenticalCalls: collapse, clientSideSplitting: true).Single().PlantUmls.Select(p => p.PlainText));
void Write(string name, string source) => File.WriteAllText(Path.Combine(outDir, name), source);

// 1. The collapsed-run loop (the only loop Kronikol writes) and the setup partition (the only partition).
{
    var logs = new List<RequestResponseLog>();
    for (var i = 0; i < 6; i++) logs.AddRange(Call("loop", "Api", "StockService", HttpMethod.Get, "http://stock.internal/stock/SKU-1"));
    logs.AddRange(Call("loop", "Api", "OrderService", HttpMethod.Post, "http://orders.internal/orders"));
    Write("seq-loop.puml", Sequence(logs, collapse: true));
}
{
    var logs = new List<RequestResponseLog>();
    logs.AddRange(Call("setup", "Api", "CustomerService", HttpMethod.Get, "http://customers.internal/customers/ada", phase: TestPhase.Setup));
    logs.AddRange(Marker("setup", "", DiagramMarkerKind.Phase, actionStart: true));
    logs.AddRange(Call("setup", "Api", "OrderService", HttpMethod.Post, "http://orders.internal/orders", phase: TestPhase.Action));
    Write("seq-partition.puml", Sequence(logs, separateSetup: true));
}
// 2. Step bars: a one-line step (the coloured legacy form), a long step with spaces (wrapped at 110, so the styled
// form), and a long step that is one unbreakable token holding markup (no break possible, so the legacy form, long).
string StepDiagram(string bar)
{
    var logs = new List<RequestResponseLog>();
    logs.AddRange(Marker("steps", bar, DiagramMarkerKind.Step));
    logs.AddRange(Call("steps", "Api", "OrderService", HttpMethod.Post, "http://orders.internal/orders"));
    return Sequence(logs);
}
Write("seq-bar-short.puml", StepDiagram(StepBarPlantUml.Build("Given a customer named \"Ada\" with a gold account")));
var words = string.Join(" ", Enumerable.Range(1, 400).Select(i => $"the order line {i} has quantity {i % 7 + 1}"));
Write("seq-bar-rich.puml", StepDiagram(StepBarPlantUml.Build("Given " + words)));
var token = "[" + string.Join(",", Enumerable.Range(1, 300).Select(i => $"{{\"sku\":\"SKU-{i:D4}\",\"qty\":{i % 7 + 1}}}")) + "]";
Write("seq-bar-token.puml", StepDiagram(StepBarPlantUml.Build(token)));

// 3. Sequence participants with a long name: an HTTP service (entity) and a database, wrapped every 80 by the emitter.
string LongName(int k)
{
    // A host-and-type-shaped name with no whitespace, as the long ones are (hosts, type names, descriptors).
    var unit = "orders-archive-replica.eu-west-1.internal.Kronikol.Example.Warehouse.";
    var s = string.Concat(Enumerable.Repeat(unit, k / unit.Length + 1));
    return "Svc" + s[..(k - 3)];
}
string SpanName(int k)
{
    var sql = "SELECT o.Id, o.CustomerId, o.Total, o.Status, l.Sku, l.Quantity, l.Price FROM Orders AS o INNER JOIN OrderLines AS l ON l.OrderId = o.Id WHERE o.CustomerId = @p0 AND o.Status <> @p1 ";
    var s = string.Concat(Enumerable.Repeat(sql, k / sql.Length + 1));
    return s[..k].TrimEnd() + new string('x', k - s[..k].TrimEnd().Length);
}
foreach (var kind in new[] { "seq-entity", "seq-database", "comp-database", "comp-system", "activity" })
    Directory.CreateDirectory(Path.Combine(outDir, kind));
var index = new List<string>();
for (var k = 10; k <= 2100; k += 5)
{
    var name = LongName(k);
    {
        var logs = Call("p", "Api", name, HttpMethod.Get, "http://orders.internal/orders/123");
        File.WriteAllText(Path.Combine(outDir, "seq-entity", $"{k}.puml"), Sequence(logs));
    }
    {
        var logs = Call("p", "Api", name, "Query", "sql://orders-db/Orders", cat: "SQL");
        File.WriteAllText(Path.Combine(outDir, "seq-database", $"{k}.puml"), Sequence(logs));
    }
    // 4. Component participants: a ClickHouse database (database shape) and an HTTP service (<<system>> rectangle).
    {
        var logs = Enumerable.Range(1, 3).Select(i => new RequestResponseLog("c", "t1", $"INSERT INTO orders_archive_{i:000}", null,
            new Uri("clickhouse://warehouse/default"), [], name, "Caller", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false,
            DependencyCategory: "ClickHouse")).ToList();
        var rel = ComponentDiagramGenerator.ExtractRelationships(logs);
        File.WriteAllText(Path.Combine(outDir, "comp-database", $"{k}.puml"), ComponentDiagramGenerator.GeneratePlantUml(rel, useC4: false));
    }
    {
        var logs = new List<RequestResponseLog>
        {
            new("c", "t1", HttpMethod.Get, null, new Uri("http://orders.internal/orders/1"), [], name, "Caller", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false),
            new("c", "t1", HttpMethod.Get, null, new Uri("http://stock.internal/stock/1"), [], "StockService", name, RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false),
        };
        var rel = ComponentDiagramGenerator.ExtractRelationships(logs);
        File.WriteAllText(Path.Combine(outDir, "comp-system", $"{k}.puml"), ComponentDiagramGenerator.GeneratePlantUml(rel, useC4: false));
    }
    // 6. The internal-flow popup's activity diagram: a request span with one long child span (SQL text, EF-shaped).
    {
        var start = new DateTime(2026, 10, 9, 10, 0, 0, DateTimeKind.Utc);
        FlowSpan[] spans =
        [
            new("trace1", "s1", null, "POST /orders", "OrderService", start, TimeSpan.FromMilliseconds(40)),
            new("trace1", "s2", "s1", SpanName(k), "Microsoft.EntityFrameworkCore", start.AddMilliseconds(5), TimeSpan.FromMilliseconds(12)),
        ];
        var segment = new InternalFlowSegment(Guid.NewGuid(), RequestResponseType.Request, "t1", null, null, []) { FlowSpans = spans };
        File.WriteAllText(Path.Combine(outDir, "activity", $"{k}.puml"), string.Join("\n@@FRAGMENT@@\n", InternalFlowRenderer.RenderActivityDiagramBatched(segment)));
    }
}

// 5. The stats-linked component edge, as GeneratePlantUml writes it when stats are passed: 48 INSERT statements (the link
// text is capped at MaxLinkedLabelChars), P50 45 ms, P95 120 ms, P99 999 ms, 12% errors, 48 calls across 5 tests.
{
    var methods = new HashSet<string>(Enumerable.Range(1, 48).Select(i => $"INSERT INTO orders_archive_{i:000}"));
    var rel = new ComponentRelationship("Caller", "Warehouse", "ClickHouse", methods, 48, 5, "ClickHouse");
    var key = $"iflow-rel-{ComponentFlowSegmentBuilder.SanitizeKey("Caller")}-{ComponentFlowSegmentBuilder.SanitizeKey("Warehouse")}";
    var stats = new Dictionary<string, RelationshipStats>
    {
        [key] = new RelationshipStats(48, 5, 60, 45, 120, 999, 3, 1200, 0.12, [], [], null, null, false, 0.5, [], null, 10),
    };
    Write("comp-stats.puml", ComponentDiagramGenerator.GeneratePlantUml([rel], stats: stats, useC4: false));
}
Console.WriteLine($"wrote the sources to {outDir}");
