// Writes a corpus of PlantUML sources produced by Kronikol's own emitter (PlantUmlCreator, StepBarPlantUml,
// the assertion-note and override shapes), covering what a current report carries: header blocks, JSON,
// XML, GraphQL and form bodies, step bars in both forms, assertion notes, events, database and cache
// participants, focus emphasis, internal-flow links, setup separation, collapsed runs, user actions,
// failed sends, creole-looking payloads, very large notes and an arrow-heavy scenario.
//   dotnet run -- <out dir>                     the corpus (14 sources)
//   dotnet run -- --shim <page.html> <workers>  a bare page with the shipped render script, for worker-wedge-probe.js
//   dotnet run -- --linked-labels <dir>         one request per label shape with its internal-flow link (the S0 scan)
using System.Net;
using System.Text;
using Kronikol;
using Kronikol.PlantUml;
using Kronikol.Tracking;

if (args.Length >= 3 && args[0] == "--shim")
{
    // A bare page carrying the shipped browser render script with <workers> workers, for the worker-wedge probe.
    var workers = int.Parse(args[2]);
    var script = Kronikol.Reports.DiagramContextMenu.GetPlantUmlBrowserRenderScript(workers, 64, 12000);
    File.WriteAllText(args[1], "<!doctype html><html><head><meta charset=\"utf-8\"><title>shim</title>" + script + "</head><body></body></html>");
    Console.WriteLine($"wrote {args[1]} ({workers} workers)");
    return;
}

if (args.Length >= 2 && args[0] == "--linked-labels")
{
    // One source per request-label shape, with the internal-flow link, for statement-limits-worker-probe.js --scan
    // (plans/ENGINE_PIN_PLAN.md S0): the probe cuts the linked label to each length the way TruncateLabel does.
    // .NET's Uri percent-encodes the non-ASCII path, so that shape reaches the label as %XX runs, as it does in a report.
    var dir = args[1];
    Directory.CreateDirectory(dir);
    (string Shape, HttpMethod Method, string Url)[] shapes =
    [
        ("query", HttpMethod.Get, "http://orders.internal/orders/search?" + string.Join("&", Enumerable.Range(0, 160).Select(i => $"filter{i}=value{i}"))),
        ("segments", HttpMethod.Get, "http://catalog.internal/" + string.Join("/", Enumerable.Range(0, 500).Select(i => $"s{i}"))),
        ("token", HttpMethod.Delete, "http://cache.internal/keys/" + new string('k', 3000)),
        ("nonascii", HttpMethod.Get, "http://catalog.internal/products/" + string.Concat(Enumerable.Repeat("商品カテゴリ-café-", 40))),
        ("brackets", HttpMethod.Get, "http://api.internal/articles?" + string.Join("&", Enumerable.Range(0, 200).Select(i => $"page[{i}]=~{i}"))),
    ];
    List<RequestResponseLog> Pair(string shape, HttpMethod method, string url)
    {
        var trace = Guid.NewGuid(); var id = Guid.NewGuid();
        return
        [
            new(shape, shape, method, null, new Uri(url), [], "OrderService", "Api", RequestResponseType.Request, trace, id, false),
            new(shape, shape, method, "{ \"ok\": true }", new Uri(url), [], "OrderService", "Api", RequestResponseType.Response, trace, id, false, HttpStatusCode.OK),
        ];
    }
    foreach (var (shape, method, url) in shapes)
    {
        var source = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(Pair(shape, method, url), internalFlowTracking: true).Single().PlantUmls.First().PlainText;
        File.WriteAllText(Path.Combine(dir, $"linked-{shape}.puml"), source);
    }
    // The same query three times over, collapsed into one `loop ×3` block: the linked statement inside a block.
    var loopLogs = Enumerable.Range(0, 3).SelectMany(_ => Pair("loop", HttpMethod.Get, shapes[0].Url)).ToList();
    var loopSource = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(loopLogs, internalFlowTracking: true, collapseConsecutiveIdenticalCalls: true)
        .Single().PlantUmls.First().PlainText;
    if (!loopSource.Contains("loop ")) throw new InvalidOperationException("the collapsed run wrote no loop block");
    File.WriteAllText(Path.Combine(dir, "linked-loop.puml"), loopSource);

    // The component diagram's edge link, [[#iflow-rel-… <protocol>: <methods>]]: an edge whose calls carry 30
    // distinct method names, laid out as BrowserJs lays it out (plain shapes, no C4 include). 30 fit under the 2,000
    // statement cap, so the link closes and the probe can cut it; more are cut inside the link by the cap.
    var t0Component = new DateTimeOffset(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);
    var componentLogs = new List<RequestResponseLog>();
    for (var i = 0; i < 30; i++)
    {
        var trace = Guid.NewGuid(); var id = Guid.NewGuid();
        var at = t0Component.AddSeconds(i);
        componentLogs.Add(new RequestResponseLog("component", "component", $"pricing.v1.PricingService/GetQuoteForRegion{i:D2}", "{}",
            new Uri("grpc://pricing.internal/"), [], "PricingService", "Api", RequestResponseType.Request, trace, id, false,
            null, RequestResponseMetaType.Default, "gRPC") { Timestamp = at });
        componentLogs.Add(new RequestResponseLog("component", "component", $"pricing.v1.PricingService/GetQuoteForRegion{i:D2}", "{}",
            new Uri("grpc://pricing.internal/"), [], "PricingService", "Api", RequestResponseType.Response, trace, id, false,
            HttpStatusCode.OK, RequestResponseMetaType.Default, "gRPC") { Timestamp = at.AddMilliseconds(40) });
    }
    var relationships = Kronikol.ComponentDiagram.ComponentDiagramGenerator.ExtractRelationships(componentLogs);
    var stats = Kronikol.ComponentDiagram.ComponentFlowSegmentBuilder.ComputeRelationshipStats(relationships, componentLogs.ToArray());
    var componentSource = Kronikol.ComponentDiagram.ComponentDiagramGenerator.GeneratePlantUml(relationships, stats: stats, useC4: false);
    if (!componentSource.Contains("[[#iflow-rel-")) throw new InvalidOperationException("the component edge carries no internal-flow link");
    File.WriteAllText(Path.Combine(dir, "linked-component.puml"), componentSource);
    Console.WriteLine($"wrote {shapes.Length + 2} linked-label sources to {Path.GetFullPath(dir)}");
    return;
}

var outDir = args.Length > 0 ? args[0] : "kcorpus";
Directory.CreateDirectory(outDir);
var written = new List<string>();

var t0 = new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);
var tick = 0;
DateTimeOffset Next() => t0.AddMilliseconds(37 * ++tick);

(string, string?)[] ReqHeaders() =>
[
    ("Authorization", "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U"),
    ("Content-Type", "application/json; charset=utf-8"),
    ("x-correlation-id", "7b0c1d2e-3f40-4a5b-8c6d-7e8f9a0b1c2d"),
    ("traceparent", "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"),
];
(string, string?)[] ResHeaders() =>
[
    ("Content-Type", "application/json; charset=utf-8"),
    ("Date", "Thu, 25 Sep 2026 10:00:00 GMT"),
    ("Server", "Kestrel"),
];

List<RequestResponseLog> Call(string test, string caller, string service, OneOf<HttpMethod, string> method, string url,
    string? reqBody, OneOf<HttpStatusCode, string> status, string? resBody,
    (string, string?)[]? reqH = null, (string, string?)[]? resH = null,
    RequestResponseMetaType meta = RequestResponseMetaType.Default, string? cat = null, string? callerCat = null,
    string[]? focus = null, TestPhase phase = TestPhase.Unknown, string? error = null, bool userAction = false)
{
    var trace = Guid.NewGuid(); var id = Guid.NewGuid();
    var req = new RequestResponseLog(test, test, method, reqBody, new Uri(url), reqH ?? [], service, caller,
        RequestResponseType.Request, trace, id, false, null, meta, cat, callerCat)
    { FocusFields = focus, Timestamp = Next(), Phase = phase, IsUserAction = userAction };
    if (userAction) return [req];
    var res = new RequestResponseLog(test, test, method, resBody, new Uri(url), resH ?? [], service, caller,
        RequestResponseType.Response, trace, id, false, status, meta, cat, callerCat)
    { FocusFields = focus, Timestamp = Next(), Phase = phase, Error = error };
    return [req, res];
}

List<RequestResponseLog> Marker(string test, string plantUml, DiagramMarkerKind kind, bool actionStart = false) =>
[
    new RequestResponseLog(test, test, "", "", new Uri("http://override.com"), [], "", "", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
    { IsOverrideStart = true, MarkerKind = kind, PlantUml = "\n" + plantUml + "\n\n", Timestamp = Next(), IsActionStart = actionStart },
    new RequestResponseLog(test, test, "", "", new Uri("http://override.com"), [], "", "", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
    { IsOverrideEnd = true, MarkerKind = kind, Timestamp = Next() },
];

string Assertion(bool passed, string label, string? failure = null)
{
    var body = DiagramWidth.WrapBlockNoteBody(passed ? $"\u2713 {label}" : $"\u2717 {label}\n{failure}");
    return $"hnote across <<assertionNote>> {(passed ? "#d4edda" : "#f8d7da")}\n{body}\nend note\n'__^*__:OrderTests.cs:L42";
}

string OrderJson(int lines) =>
    "{\n  \"orderId\": \"ord-123\",\n  \"customer\": { \"name\": \"Ada Lovelace\", \"email\": \"ada@example.com\", \"tier\": \"gold\" },\n  \"lines\": [\n"
    + string.Join(",\n", Enumerable.Range(1, lines).Select(i => $"    {{ \"sku\": \"SKU-{i:D4}\", \"qty\": {i % 7 + 1}, \"price\": {i * 3.5:F2}, \"note\": \"line {i} of the order\" }}"))
    + "\n  ],\n  \"total\": 1234.50,\n  \"placedAt\": \"2026-09-25T10:00:00Z\"\n}";

void Emit(string name, IEnumerable<RequestResponseLog> logs, bool internalFlow = false, FocusEmphasis focus = FocusEmphasis.Bold,
    bool participantColors = false, bool separateSetup = false, bool collapse = false, bool excludeAllHeaders = false,
    bool clientSideSplitting = true)
{
    var result = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs.ToList(),
        internalFlowTracking: internalFlow, focusEmphasis: focus, sequenceDiagramParticipantColors: participantColors,
        separateSetup: separateSetup, collapseConsecutiveIdenticalCalls: collapse, excludeAllHeaders: excludeAllHeaders,
        clientSideSplitting: clientSideSplitting).ToList();
    foreach (var t in result)
    {
        var i = 0;
        foreach (var p in t.PlantUmls)
        {
            var file = Path.Combine(outDir, $"{name}-{i++}.puml");
            File.WriteAllText(file, p.PlainText);
            written.Add(Path.GetFileName(file));
        }
    }
}

// 1. REST calls with header blocks and JSON both ways
{
    var t = "rest";
    var logs = new List<RequestResponseLog>();
    logs.AddRange(Call(t, "Api", "OrderService", HttpMethod.Get, "http://orders.internal/orders/123?include=lines&expand=customer",
        null, HttpStatusCode.OK, OrderJson(6), ReqHeaders(), ResHeaders()));
    logs.AddRange(Call(t, "Api", "OrderService", HttpMethod.Post, "http://orders.internal/orders",
        OrderJson(3), HttpStatusCode.Created, "{ \"orderId\": \"ord-124\", \"status\": \"created\" }", ReqHeaders(), ResHeaders()));
    logs.AddRange(Call(t, "Api", "PaymentGateway", HttpMethod.Put, "https://payments.example.com/v2/payments/pay-9/capture",
        "{ \"amount\": 1234.50, \"currency\": \"GBP\" }", HttpStatusCode.BadRequest,
        "{ \"error\": \"insufficient_funds\", \"detail\": \"The card was declined.\" }", ReqHeaders(), ResHeaders()));
    logs.AddRange(Call(t, "Api", "OrderService", HttpMethod.Delete, "http://orders.internal/orders/124", null, HttpStatusCode.NoContent, null));
    Emit("rest", logs);
    Emit("rest-noheaders", logs, excludeAllHeaders: true);
    Emit("rest-server-encoded", logs, clientSideSplitting: false);
}

// 2. Databases, cache and events
{
    var t = "data";
    var logs = new List<RequestResponseLog>();
    logs.AddRange(Call(t, "Api", "OrdersDb", "Query", "sql://orders-db/Orders",
        "SELECT o.Id, o.Total FROM Orders o WHERE o.CustomerId = @customerId AND o.Status <> 'Cancelled'", "3 rows",
        "[{\"Id\":1,\"Total\":10.5},{\"Id\":2,\"Total\":99.0},{\"Id\":3,\"Total\":0}]", cat: "SQL"));
    logs.AddRange(Call(t, "Api", "Cosmos", "ReadItem", "cosmos://orders/items/ord-123", null, HttpStatusCode.OK, OrderJson(2), cat: "CosmosDB"));
    logs.AddRange(Call(t, "Api", "Cache", "GET", "redis://cache/order:123", null, "OK", "\"cached-value\"", cat: "Redis"));
    logs.AddRange(Call(t, "Api", "Cache", "SET", "redis://cache/order:123", "\"new-value\" EX 300", "OK", null, cat: "Redis"));
    logs.AddRange(Call(t, "Api", "orders-topic", "Publish", "kafka://broker/orders-topic",
        "{ \"type\": \"OrderPlaced\", \"orderId\": \"ord-123\" }", "Sent", null, meta: RequestResponseMetaType.Event, cat: "MessageQueue"));
    logs.AddRange(Call(t, "Api", "Blob", "PutBlob", "https://acct.blob.core.windows.net/receipts/ord-123.pdf", "<binary 2048 bytes>", HttpStatusCode.Created, null, cat: "BlobStorage"));
    Emit("data", logs);
    Emit("data-participant-colors", logs, participantColors: true);
}

// 3. Gherkin step bars (both forms) and assertion notes
{
    var t = "steps";
    var logs = new List<RequestResponseLog>();
    logs.AddRange(Marker(t, StepBarPlantUml.Build("Given a customer named \"Ada\" with a gold account"), DiagramMarkerKind.Step));
    logs.AddRange(Call(t, "Api", "CustomerService", HttpMethod.Get, "http://customers.internal/customers/ada", null, HttpStatusCode.OK,
        "{ \"name\": \"Ada\", \"tier\": \"gold\" }"));
    logs.AddRange(Marker(t, StepBarPlantUml.Build("When the order has these lines",
        [new StepBarTable(null, [["sku", "qty", "note"], ["SKU-1", "2", "a|b pipe"], ["SKU-2", "1", "**bold** and ~tilde~"], ["SKU-3", "10", "unicode \u00e9\u00e8 \u4e2d\u6587"]])]), DiagramMarkerKind.Step));
    logs.AddRange(Call(t, "Api", "OrderService", HttpMethod.Post, "http://orders.internal/orders", OrderJson(3), HttpStatusCode.Created, "{ \"orderId\": \"ord-125\" }"));
    logs.AddRange(Marker(t, StepBarPlantUml.Build("Then the confirmation email says", docString: "Dear Ada,\n\nYour order ord-125 is on its way.\n{ \"json\": [1, 2, 3] }\n-- signed"), DiagramMarkerKind.Step));
    logs.AddRange(Marker(t, Assertion(true, "Order status should be \"created\""), DiagramMarkerKind.Assertion));
    logs.AddRange(Marker(t, Assertion(false, "Total should be 1234.50",
        "Expected: 1234.50\nActual:   1200.00\nat OrderTests.Total_is_correct() in C:\\src\\OrderTests.cs:line 42"), DiagramMarkerKind.Assertion));
    logs.AddRange(Marker(t, "hnote across #black:<color:white>Test Example row 2", DiagramMarkerKind.Row));
    logs.AddRange(Call(t, "Api", "OrderService", HttpMethod.Get, "http://orders.internal/orders/125", null, HttpStatusCode.OK, "{ \"status\": \"created\" }"));
    Emit("steps", logs);
}

// 4. Focus emphasis on fields
{
    var t = "focus";
    var logs = new List<RequestResponseLog>();
    logs.AddRange(Call(t, "Api", "OrderService", HttpMethod.Post, "http://orders.internal/orders", OrderJson(2), HttpStatusCode.Created,
        "{ \"orderId\": \"ord-126\", \"status\": \"created\", \"total\": 99.5 }", focus: ["status", "total", "customer"]));
    Emit("focus-bold", logs);
    Emit("focus-colored", logs, focus: FocusEmphasis.Colored);
}

// 5. Internal flow links, setup separation, collapsed runs, user action, failed send
{
    var t = "flow";
    var logs = new List<RequestResponseLog>();
    logs.AddRange(Call(t, "Api", "CustomerService", HttpMethod.Get, "http://customers.internal/customers/ada", null, HttpStatusCode.OK, "{ \"name\": \"Ada\" }", phase: TestPhase.Setup));
    logs.AddRange(Call(t, "Api", "CustomerService", HttpMethod.Get, "http://customers.internal/customers/bob", null, HttpStatusCode.OK, "{ \"name\": \"Bob\" }", phase: TestPhase.Setup));
    logs.AddRange(Marker(t, "", DiagramMarkerKind.Phase, actionStart: true));
    logs.AddRange(Call(t, "User", "WebApp", "Click \"Place order\"", "http://webapp/checkout", "button#place-order", "done", null, userAction: true, phase: TestPhase.Action));
    for (var i = 0; i < 6; i++)
        logs.AddRange(Call(t, "Api", "StockService", HttpMethod.Get, "http://stock.internal/stock/SKU-1", null, HttpStatusCode.OK, "{ \"available\": 5 }", phase: TestPhase.Action));
    logs.AddRange(Call(t, "Api", "ShippingService", HttpMethod.Post, "http://shipping.internal/shipments", "{ \"orderId\": \"ord-123\" }",
        "!HttpRequestException", null, phase: TestPhase.Action, error: "Connection refused (shipping.internal:80)\nCaused by: SocketException: Connection refused"));
    Emit("flow", logs, internalFlow: true, separateSetup: true, collapse: true);
    Emit("flow-plain", logs);
}

// 6. Payloads that look like creole, and unicode
{
    var t = "creole";
    var body = "{\n  \"icon\": \"<&check> and <:smile:> and <$sprite>\",\n  \"md\": \"**bold** //italic// --strike-- __under__ \\\"\\\"mono\\\"\\\"\",\n"
        + "  \"link\": \"[[http://example.com label]] and [[#anchor]]\",\n  \"html\": \"<b>raw</b> <color:red>red</color> <size:30>big</size> <U+200B>\",\n"
        + "  \"table\": \"|= a |= b |\\n| 1 | 2 |\",\n  \"tilde\": \"~~ ~| ~<\",\n  \"emoji\": \"\ud83d\ude00 \u2713 \u2717\",\n  \"cjk\": \"\u4e2d\u6587\u6f22\u5b57\",\n"
        + "  \"rtl\": \"\u05e9\u05dc\u05d5\u05dd \u0645\u0631\u062d\u0628\u0627\",\n  \"preproc\": \"!include <C4/C4_Context>\\n!theme cerulean\\n@enduml\"\n}";
    var logs = Call(t, "Api", "Echo", HttpMethod.Post, "http://echo.internal/echo?q=<&x>&r=<:y:>", body, HttpStatusCode.OK, body);
    Emit("creole", logs);
}

// 7. Large notes and long tokens
{
    var t = "large";
    var huge = OrderJson(400);
    var token = new string('k', 950) + "." + Convert.ToBase64String(Encoding.UTF8.GetBytes(new string('x', 600)));
    var logs = new List<RequestResponseLog>();
    logs.AddRange(Call(t, "Api", "OrderService", HttpMethod.Get, "http://orders.internal/orders/big/" + new string('p', 240) + "?token=" + token, null, HttpStatusCode.OK, huge));
    logs.AddRange(Call(t, "Api", "TokenService", HttpMethod.Post, "http://tokens.internal/verify", "{ \"token\": \"" + token + "\" }", HttpStatusCode.OK, "{ \"valid\": true }"));
    Emit("large", logs);
}

// 8. XML, GraphQL and form bodies
{
    var t = "formats";
    var logs = new List<RequestResponseLog>();
    logs.AddRange(Call(t, "Api", "LegacySoap", HttpMethod.Post, "http://legacy.internal/soap",
        "<?xml version=\"1.0\"?><soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\"><soap:Body><GetOrder><Id>123</Id></GetOrder></soap:Body></soap:Envelope>",
        HttpStatusCode.OK, "<?xml version=\"1.0\"?><Order><Id>123</Id><Total currency=\"GBP\">1234.50</Total><Lines><Line sku=\"SKU-1\"/></Lines></Order>"));
    logs.AddRange(Call(t, "Api", "Graph", HttpMethod.Post, "http://graph.internal/graphql",
        "{ \"query\": \"query Order($id: ID!) { order(id: $id) { id total lines { sku qty } } }\", \"variables\": { \"id\": \"123\" }, \"operationName\": \"Order\" }",
        HttpStatusCode.OK, "{ \"data\": { \"order\": { \"id\": \"123\", \"total\": 1234.5, \"lines\": [] } } }"));
    logs.AddRange(Call(t, "Api", "Auth", HttpMethod.Post, "http://auth.internal/token", "grant_type=client_credentials&client_id=api&scope=orders.read%20orders.write",
        HttpStatusCode.OK, "{ \"access_token\": \"abc\", \"expires_in\": 3600 }", [("Content-Type", "application/x-www-form-urlencoded")]));
    Emit("formats", logs);
}

// 9. Arrow-heavy: 200 calls across four participants (the S4 budget shape, from the real emitter)
{
    var t = "arrows";
    var logs = new List<RequestResponseLog>();
    string[] services = ["OrderService", "StockService", "PricingService"];
    for (var i = 0; i < 200; i++)
        logs.AddRange(Call(t, "Api", services[i % 3], i % 2 == 0 ? HttpMethod.Get : HttpMethod.Post, $"http://svc.internal/items/{i}",
            i % 2 == 0 ? null : $"{{ \"i\": {i} }}", HttpStatusCode.OK, $"{{ \"ok\": true, \"i\": {i} }}"));
    Emit("arrows-200", logs, excludeAllHeaders: true);
}

File.WriteAllLines(Path.Combine(outDir, "_index.txt"), written);
Console.WriteLine($"wrote {written.Count} sources to {Path.GetFullPath(outDir)}");
