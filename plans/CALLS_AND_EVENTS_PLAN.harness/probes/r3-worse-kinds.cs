// How many rows of results/kinds-by-tracker.tsv get a worse span kind on R3 alone, R2 not applied. Each row is rebuilt as its tracker writes
// it at R1's kind and read by the real OtlpSpanMapper: a MessageTracker row through the real MessageTracker, the rest as the record the table
// describes. "today" is the table's own otlpSpanKind column, measured on 5.27.13. The oracle, "truth", is by hand from OTel's tables and each
// tracker's code: a statement, a settlement and a management call are CLIENT; a pull is CLIENT; a delivery CONSUMER; a send PRODUCER, as the
// plan's section 5.5 reads it under Q6 (a).
//
// Then it tries to break the answer: the same rows through NDJSON, where the marks do not survive, and hand-logged records on real schemes,
// which the table does not hold.
//
// Usage, from the harness directory: probes/on-prototype.sh <worktree> probes/r3-worse-kinds.cs <scratch> results/kinds-by-tracker.tsv > results/r3-worse-kinds.txt
#:package Kronikol@5.27.13
#:package Kronikol.Extensions.Otlp@5.27.13
#:property PublishAot=false

using Kronikol.Extensions.Otlp;
using Kronikol.Ingestion;
using Kronikol.Tracking;

const string Client = nameof(OtlpSpanKind.Client), Producer = nameof(OtlpSpanKind.Producer), Consumer = nameof(OtlpSpanKind.Consumer);
var otlp = new OtlpExportOptions { Endpoint = new Uri("http://localhost:4318/v1/traces") };

var lines = File.ReadAllLines(args[0]);
var column = lines[0].Split('\t').Select((name, i) => (name, i)).ToDictionary(c => c.name, c => c.i);
var rows = lines.Skip(1).Select(line => line.Split('\t')).ToArray();
string Cell(string[] row, string name) => row[column[name]];

// R1 moves these three trackers to Default (prototype/r1-first-cut.patch).
static bool MovedByR1(string package) => package is "Kronikol.Extensions.Spanner" or "Kronikol.Extensions.BigQuery" or "Kronikol.Extensions.Bigtable";

// The oracle, written out by hand from each tracker's code and OTel's tables. It does not read R3's table, so it can catch it out.
static string Truth(string package, string route, string operation, string statementShaped)
{
    if (statementShaped == "true")
        return Client;
    if (package == "Kronikol")
        return route.Contains("TrackConsumeEvent", StringComparison.Ordinal) ? Consumer : Producer;
    var name = operation.Split('[')[0];
    if (name is "Publish" or "PublishBatch" or "Send" or "SendBatch" or "Schedule" or "Produce" or "ProduceAsync" or "SendMessage" or "SendMessageBatch" or "PutEvents" or "PutPartnerEvents")
        return Producer;
    // Kafka's Consume is the owner's call: a poll by the letter, read as a delivery since the label cannot separate the routes (plan section 5.5).
    if ((package, name) is ("Kronikol.Extensions.Kafka", "Consume") or ("Kronikol.Extensions.MassTransit", "Consume")
        or ("Kronikol.Extensions.PubSub", "Receive") or ("Kronikol.Extensions.EventHubs", "ProcessEvent"))
        return Consumer;
    return Client;
}

RequestResponseLog[] Records(string[] row, RequestResponseMetaType kind)
{
    var label = Cell(row, "label");
    var scheme = Cell(row, "uriScheme");
    var route = Cell(row, "route");
    var uri = new Uri(scheme is "event" or "http" or "https" ? (scheme == "event" ? "event://orders" : $"{scheme}://host.example/orders") : $"{scheme}:///orders");
    var test = Guid.NewGuid().ToString("N");
    if (route.StartsWith("MessageTracker.", StringComparison.Ordinal))
    {
        var tracker = new MessageTracker(new MessageTrackerOptions { CallerName = "App", ServiceName = "Broker", CurrentTestInfoFetcher = () => ("t", test) });
        if (route.Contains("TrackConsumeEvent", StringComparison.Ordinal))
            tracker.TrackConsumeEvent(label, "Consumer", uri, new { A = 1 });
        else if (route.Contains("TrackSendMessage", StringComparison.Ordinal))
            tracker.TrackSendMessage(label, "Broker", uri, new { A = 1 });
        else if (route.Contains("TrackMessageRequest", StringComparison.Ordinal))
            tracker.TrackMessageResponse(label, "Broker", uri, tracker.TrackMessageRequest(label, "Broker", uri, new { A = 1 }));
        else
            tracker.TrackSendEvent(label, "Broker", uri, new { A = 1 });
        return RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == test).ToArray();
    }

    var id = Guid.NewGuid();
    var at = DateTimeOffset.UtcNow;
    var category = Cell(row, "category");
    OneOf<HttpMethod, string> method = label;
    return
    [
        new RequestResponseLog("t", test, method, null, uri, [], "Broker", "App", RequestResponseType.Request, id, id, false, MetaType: kind, DependencyCategory: category.Length == 0 || category == "-" ? null : category) { Timestamp = at },
        new RequestResponseLog("t", test, method, null, uri, [], "Broker", "App", RequestResponseType.Response, id, id, false, MetaType: kind, DependencyCategory: category.Length == 0 || category == "-" ? null : category) { Timestamp = at.AddMilliseconds(1) },
    ];
}

string KindOf(IEnumerable<RequestResponseLog> logs) => OtlpSpanMapper.MapAll(logs, otlp, DateTimeOffset.UtcNow).Spans.Single().Kind.ToString();
RequestResponseLog ReadBack(RequestResponseLog log) => InteractionRecord.FromJson(InteractionRecord.FromLog(log).ToJson()).ToLog();

Console.WriteLine($"{rows.Length} rows of {Path.GetFileName(args[0])}");
var results = new List<(string[] Row, string Today, string R1Kind, string Alone, string ViaNdjson, string Truth)>();
foreach (var row in rows)
{
    var package = Cell(row, "package");
    var r1Kind = MovedByR1(package) ? "Default" : Cell(row, "requestKind");
    var logs = Records(row, Enum.Parse<RequestResponseMetaType>(r1Kind));
    results.Add((row, Cell(row, "otlpSpanKind"), r1Kind, KindOf(logs), KindOf(logs.Select(ReadBack)),
        Truth(package, Cell(row, "route"), Cell(row, "operation"), Cell(row, "statementShaped"))));
}

Console.WriteLine("today -> R3 alone, by what OTel's table makes the row:");
foreach (var group in results.GroupBy(r => (r.Today, r.Alone, r.Truth)).OrderBy(g => g.Key.Today).ThenBy(g => g.Key.Alone).ThenBy(g => g.Key.Truth))
    Console.WriteLine($"  {group.Key.Today} -> {group.Key.Alone}, truth {group.Key.Truth}: {group.Count()}");

var worse = results.Where(r => r.Today == r.Truth && r.Alone != r.Truth).ToArray();
var sideways = results.Where(r => r.Today != r.Truth && r.Alone != r.Truth && r.Alone != r.Today).ToArray();
var better = results.Where(r => r.Today != r.Truth && r.Alone == r.Truth).ToArray();
var wrongStill = results.Where(r => r.Today != r.Truth && r.Alone == r.Today).ToArray();
Console.WriteLine("Every row R3 alone moves:");
foreach (var r in results.Where(r => r.Alone != r.Today).OrderBy(r => Cell(r.Row, "package")))
    Console.WriteLine($"  {Cell(r.Row, "package")} {Cell(r.Row, "route")} {Cell(r.Row, "operation")} '{Cell(r.Row, "label")}': {r.Today} -> {r.Alone}, truth {r.Truth}");
Console.WriteLine($"Right today and wrong on R3 alone (worse): {worse.Length}");
foreach (var r in worse)
    Console.WriteLine($"  {Cell(r.Row, "package")} {Cell(r.Row, "route")} {Cell(r.Row, "operation")} '{Cell(r.Row, "label")}': today {r.Today}, R3 alone {r.Alone}, truth {r.Truth}");
Console.WriteLine($"Wrong today and differently wrong on R3 alone: {sideways.Length}");
foreach (var r in sideways)
    Console.WriteLine($"  {Cell(r.Row, "package")} {Cell(r.Row, "route")} {Cell(r.Row, "operation")} '{Cell(r.Row, "label")}': today {r.Today}, R3 alone {r.Alone}, truth {r.Truth}");
Console.WriteLine($"Wrong today and right on R3 alone: {better.Length}");
Console.WriteLine($"Wrong today and the same on R3 alone, for R2 to right: {wrongStill.Length}");
foreach (var group in wrongStill.GroupBy(r => (Package: Cell(r.Row, "package"), r.Today, r.Truth)).OrderBy(g => g.Key.Package))
    Console.WriteLine($"  {group.Key.Package}: {group.Count()}, {group.Key.Today} where the table says {group.Key.Truth} ({string.Join(", ", group.Select(r => Cell(r.Row, "operation")).Distinct().Take(6))})");

Console.WriteLine();
Console.WriteLine("Trying to break it. The same rows through NDJSON, where neither mark survives:");
var worseThrough = results.Where(r => r.Today == r.Truth && r.ViaNdjson != r.Truth).ToArray();
var differ = results.Where(r => r.ViaNdjson != r.Alone).ToArray();
Console.WriteLine($"  right today and wrong through NDJSON on R3 alone: {worseThrough.Length}; another kind than in process: {differ.Length}");
foreach (var r in worseThrough.Concat(differ).Distinct())
    Console.WriteLine($"    {Cell(r.Row, "package")} {Cell(r.Row, "route")} {Cell(r.Row, "operation")} '{Cell(r.Row, "label")}': today {r.Today}, in process {r.Alone}, through NDJSON {r.ViaNdjson}, truth {r.Truth}");

Console.WriteLine("Hand-logged, on real schemes, which the table does not hold. service.name beside each kind:");
Console.WriteLine("  call\t5.27.13's rule\tin process\tthrough NDJSON");
foreach (var (label, destination, uri, consume) in new[]
{
    ("Consume (Pub/Sub)", "OrderService", "pubsub:///orders-sub", true),
    ("Consume (Event Hub)", "OrderService", "eventhub:///orders", true),
    ("Consume (Event Hub)", "OrderService", "eventhubs:///orders", true),
    ("Consume (Kafka)", "OrderService", "kafka:///orders", true),
    ("Kafka", "OrderService", "kafka:///orders", true),
    ("Process order", "Orders", "event://orders", false),
    ("Consume", "Orders", "event://orders", false),
    ("ProcessOrder", "Orders", "event://orders", false),
    ("Process", "Payments", "https://payments.example/process", false),
    ("Consume (Kafka)", "OrderService", "kafka:///orders", false),
    ("Receive order", "PubSub", "pubsub:///orders", false),
    ("Publish (Pub/Sub)", "PubSub", "pubsub:///orders", false),
    ("Kafka", "OrderService", "kafka:///orders", false),
})
{
    var test = Guid.NewGuid().ToString("N");
    var tracker = new MessageTracker(new MessageTrackerOptions { CallerName = consume ? "Broker" : "App", ServiceName = "Broker", CurrentTestInfoFetcher = () => ("t", test) });
    if (consume)
        tracker.TrackConsumeEvent(label, destination, new Uri(uri), new { A = 1 });
    else
        tracker.TrackSendEvent(label, destination, new Uri(uri), new { A = 1 });
    var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == test).ToArray();
    var inProcess = OtlpSpanMapper.MapAll(logs, otlp, DateTimeOffset.UtcNow).Spans.Single();
    var through = OtlpSpanMapper.MapAll(logs.Select(ReadBack), otlp, DateTimeOffset.UtcNow).Spans.Single();
    var call = $"{(consume ? "TrackConsumeEvent" : "TrackSendEvent")}(\"{label}\", \"{destination}\", {uri})";
    Console.WriteLine($"  {call}\t{Producer} {logs[0].CallerName}\t{inProcess.Kind} {inProcess.ResourceServiceName}\t{through.Kind} {through.ResourceServiceName}");
}

// One more way to break it: a MessageTracker whose options name a category. A statement-shaped one wins over the call's own words (step 1 of
// KindOf), and so the parties stay as logged; a broker's category changes nothing.
Console.WriteLine("A MessageTracker whose options name a category:");
Console.WriteLine("  category\tcall\t5.27.13's rule\tin process\tthrough NDJSON\tmessaging.system");
foreach (var (category, consume) in new[] { ("SQL", false), ("SQL", true), ("Redis", false), ("Redis", true), ("MessageQueue", false), ("MessageQueue", true) })
{
    var test = Guid.NewGuid().ToString("N");
    var tracker = new MessageTracker(new MessageTrackerOptions { CallerName = consume ? "Broker" : "App", ServiceName = "Broker", DependencyCategory = category, CurrentTestInfoFetcher = () => ("t", test) });
    if (consume)
        tracker.TrackConsumeEvent("Consume", "OrderService", new Uri("event://orders"), new { A = 1 });
    else
        tracker.TrackSendEvent("Publish", "Orders", new Uri("event://orders"), new { A = 1 });
    var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == test).ToArray();
    var inProcess = OtlpSpanMapper.MapAll(logs, otlp, DateTimeOffset.UtcNow).Spans.Single();
    var through = OtlpSpanMapper.MapAll(logs.Select(ReadBack), otlp, DateTimeOffset.UtcNow).Spans.Single();
    var system = inProcess.Attributes.Where(a => a.Key == "messaging.system").Select(a => a.Value?.ToString()).FirstOrDefault() ?? "-";
    Console.WriteLine($"  {category}\t{(consume ? "TrackConsumeEvent" : "TrackSendEvent")}\t{Producer} {logs[0].CallerName}\t{inProcess.Kind} {inProcess.ResourceServiceName}\t{through.Kind} {through.ResourceServiceName}\t{system}");
}
