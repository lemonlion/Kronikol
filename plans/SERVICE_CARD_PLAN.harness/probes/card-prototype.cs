#:project ../../../src/Kronikol/Kronikol.csproj
#:property PublishAot=false
// The service card's behaviour section, built from a decoded data file (`kronikol decode <report> -o x.json`), to measure
// what the plan claims: how the calls split by origin, what a subject's calls outside any inbound call are, how many
// operations the shared templater leaves, and how big the file is. The operation key is InteractionShape's (the
// product's, version printed), the nesting is CallNesting's rule ported, and the test client and subject are inferred
// by the prototype rule written below, which is #190's to decide. Prints counts and names of parties and operations.
//
// Usage: dotnet run --file card-prototype.cs -- <decoded.json> <card-out.json>
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kronikol.Constants;
using Kronikol.History;

var input = args[0];
var output = args[1];
using var stream = File.OpenRead(input);
using var document = JsonDocument.Parse(stream);
var root = document.RootElement;

var calls = new List<Call>();
var scenarios = new List<(string Sid, string Feature, string Name)>();
foreach (var feature in root.GetProperty("features").EnumerateArray())
{
    var featureName = Text(feature, "name") ?? "";
    if (!feature.TryGetProperty("scenarios", out var list))
        continue;
    foreach (var scenario in list.EnumerateArray())
    {
        var ordinal = scenarios.Count;
        scenarios.Add((Text(scenario, "stableId") ?? "", featureName, Text(scenario, "name") ?? ""));
        if (!scenario.TryGetProperty("httpInteractions", out var interactions))
            continue;
        var index = 0;
        foreach (var i in interactions.EnumerateArray())
            calls.Add(new Call(ordinal, index++, Text(i, "type") ?? "", Text(i, "method") ?? "", Text(i, "uri") ?? "",
                Text(i, "serviceName") ?? "", Text(i, "callerName") ?? "", Text(i, "dependencyCategory"),
                Text(i, "callerDependencyCategory"), Text(i, "phase"), Text(i, "requestResponseId"), Text(i, "traceId"),
                StatusOf(i), Text(i, "content"), Text(i, "metaType")));
    }
}

var requests = calls.Where(c => c.Type == "Request").ToList();
var responses = calls.Where(c => c.Type == "Response" && c.Rrid is not null)
    .GroupBy(c => (c.Scenario, c.Rrid)).ToDictionary(g => g.Key, g => g.First());

// Prototype inference (#190 owns the real rule): the test client is a party that only ever calls, on calls that carry no
// caller category (a broker's delivery carries one); the subject is the party the test client calls most.
var callees = requests.Select(r => r.Service).ToHashSet(StringComparer.Ordinal);
var testClients = requests.Where(r => !callees.Contains(r.Caller) && r.CallerCategory is null)
    .Select(r => r.Caller).ToHashSet(StringComparer.Ordinal);
var subject = requests.Where(r => testClients.Contains(r.Caller)).GroupBy(r => r.Service)
    .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key).FirstOrDefault();

// CallNesting's rule (src/Kronikol/Query/CallNesting.cs), per scenario, in file order.
var parentOf = new Dictionary<Call, Call?>();
foreach (var group in calls.GroupBy(c => c.Scenario))
{
    var list = group.ToList();
    var answeredAt = new Dictionary<string, int>(StringComparer.Ordinal);
    for (var k = 0; k < list.Count; k++)
        if (list[k].Type == "Response" && list[k].Rrid is { } id)
            answeredAt.TryAdd(id, k);
    var open = new List<int>();
    for (var k = 0; k < list.Count; k++)
    {
        var entry = list[k];
        if (entry.Type == "Request")
        {
            Call? parent = null;
            for (var o = open.Count - 1; o >= 0; o--)
            {
                var candidate = list[open[o]];
                if (candidate.Service == entry.Caller
                    || (candidate.Caller != entry.Caller && entry.TraceId is not null && candidate.TraceId == entry.TraceId))
                {
                    parent = candidate;
                    break;
                }
            }
            parentOf[entry] = parent;
            if (entry.Rrid is { } rid && answeredAt.TryGetValue(rid, out var at) && at > k)
                open.Add(k);
        }
        else if (entry.Type == "Response" && entry.Rrid is { } rid)
        {
            open.RemoveAll(o => list[o].Rrid == rid);
        }
    }
}

string Origin(Call request)
{
    if (testClients.Contains(request.Caller))
        return "test";
    if (request.Caller == subject)
    {
        // Inside an inbound call to the subject, at any depth: the subject was serving a request or a delivery.
        for (var p = parentOf.GetValueOrDefault(request); p is not null; p = parentOf.GetValueOrDefault(p))
            if (p.Service == subject)
                return "subject";
        return "subject-unprompted";
    }
    return "other";
}

string Target(Call c)
{
    var path = Uri.TryCreate(c.Uri, UriKind.Absolute, out var u) ? u.PathAndQuery : c.Uri;
    var templated = InteractionShape.Template(path);
    if (!DependencyCategories.IsStatementShaped(c.Category) || string.IsNullOrWhiteSpace(c.Content))
        return templated;
    var first = c.Content.Split('\n')[0];
    if (first.Length > 2000) first = first[..2000];
    var head = InteractionShape.TemplateStatement(first);
    return templated + " " + (head.Length > 120 ? head[..120] : head);
}

var ops = new SortedDictionary<string, Op>(StringComparer.Ordinal);
var originTotals = new SortedDictionary<string, int>(StringComparer.Ordinal);
var unprompted = new SortedDictionary<string, int>(StringComparer.Ordinal);
var unpromptedPhase = new SortedDictionary<string, int>(StringComparer.Ordinal);
var phaseByOrigin = new SortedDictionary<string, int>(StringComparer.Ordinal);
var rawBuckets = new HashSet<string>(StringComparer.Ordinal);
foreach (var r in requests)
{
    var origin = Origin(r);
    var protocol = r.Category ?? (r.MetaType == "Event" ? r.Method : "HTTP");
    var target = Target(r);
    var key = $"{r.Caller}\u001f{r.Service}\u001f{protocol}\u001f{r.Method}\u001f{target}";
    if (!ops.TryGetValue(key, out var op))
        ops[key] = op = new Op(r.Caller, r.Service, protocol, r.Method, target);
    op.Calls++;
    responses.TryGetValue((r.Scenario, r.Rrid), out var response);
    if (IsError(response?.Status)) op.Errors++;
    op.Origins[origin] = op.Origins.GetValueOrDefault(origin) + 1;
    var phase = string.IsNullOrEmpty(r.Phase) ? "unknown" : r.Phase.ToLowerInvariant();
    op.Phases[phase] = op.Phases.GetValueOrDefault(phase) + 1;
    op.Scenarios.Add(r.Scenario);
    originTotals[origin] = originTotals.GetValueOrDefault(origin) + 1;
    phaseByOrigin[$"{origin}/{phase}"] = phaseByOrigin.GetValueOrDefault($"{origin}/{phase}") + 1;
    if (origin == "subject-unprompted")
    {
        var label = $"{r.Service} {r.Method} {target}";
        unprompted[label] = unprompted.GetValueOrDefault(label) + 1;
        unpromptedPhase[phase] = unpromptedPhase.GetValueOrDefault(phase) + 1;
    }
    var raw = Uri.TryCreate(r.Uri, UriKind.Absolute, out var ru) ? ru.AbsolutePath : r.Uri.Split('?')[0];
    rawBuckets.Add($"{r.Service}\u001f{r.Method}\u001f{raw}");
}

Console.WriteLine($"InteractionShape.Version {InteractionShape.Version}; scenarios {scenarios.Count}; requests {requests.Count}");
Console.WriteLine($"test client(s): {string.Join(", ", testClients.Order(StringComparer.Ordinal))}; subject: {subject}");
Console.WriteLine($"parties: {callees.Union(requests.Select(r => r.Caller)).Count()}; edges: {ops.Values.Select(o => (o.From, o.To, o.Protocol)).Distinct().Count()}; "
                  + $"operations: {ops.Count} (service+method+raw path buckets: {rawBuckets.Count})");
Console.WriteLine("calls by origin: " + string.Join(", ", originTotals.Select(e => $"{e.Key} {e.Value}")));
Console.WriteLine("calls by origin/phase: " + string.Join(", ", phaseByOrigin.Select(e => $"{e.Key} {e.Value}")));
Console.WriteLine("subject calls outside any inbound call, by phase: " + string.Join(", ", unpromptedPhase.Select(e => $"{e.Key} {e.Value}")));
foreach (var (label, n) in unprompted.OrderByDescending(e => e.Value).ThenBy(e => e.Key, StringComparer.Ordinal).Take(8))
    Console.WriteLine($"  {n,4}  {label}");

// The card's behaviour section, in the plan's total order, three ways: with counts, without counts, and without scenarios.
JsonObject Card(bool counts, bool withScenarios)
{
    var edges = new JsonArray();
    foreach (var edge in ops.Values.GroupBy(o => (o.From, o.To, o.Protocol)).OrderBy(g => g.Key.From, StringComparer.Ordinal)
                 .ThenBy(g => g.Key.To, StringComparer.Ordinal).ThenBy(g => g.Key.Protocol, StringComparer.Ordinal))
    {
        var operations = new JsonArray();
        foreach (var op in edge.OrderBy(o => o.Method, StringComparer.Ordinal).ThenBy(o => o.Target, StringComparer.Ordinal))
        {
            var o = new JsonObject { ["method"] = op.Method, ["target"] = op.Target, ["origins"] = new JsonArray(op.Origins.Keys.Select(k => (JsonNode)k).ToArray()) };
            if (counts)
            {
                o["calls"] = op.Calls;
                o["errors"] = op.Errors;
                o["scenarios"] = op.Scenarios.Count;
                o["phases"] = new JsonObject(op.Phases.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)p.Value)));
            }
            operations.Add(o);
        }
        edges.Add(new JsonObject { ["from"] = edge.Key.From, ["to"] = edge.Key.To, ["protocol"] = edge.Key.Protocol, ["operations"] = operations });
    }
    var card = new JsonObject
    {
        ["formatVersion"] = 1,
        ["subject"] = new JsonObject { ["name"] = subject, ["source"] = "inferred" },
        ["edges"] = edges
    };
    if (withScenarios)
    {
        var index = ops.Keys.Select((k, n) => (k, n)).ToDictionary(e => e.k, e => e.n, StringComparer.Ordinal);
        var list = new JsonArray();
        foreach (var (s, n) in scenarios.Select((s, n) => (s, n)).OrderBy(e => e.s.Sid, StringComparer.Ordinal))
        {
            var touched = ops.Where(o => o.Value.Scenarios.Contains(n)).Select(o => (JsonNode)index[o.Key]).ToArray();
            list.Add(new JsonObject { ["sid"] = s.Sid, ["feature"] = s.Feature, ["scenario"] = s.Name, ["operations"] = new JsonArray(touched) });
        }
        card["scenarios"] = list;
    }
    return card;
}

var indented = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
var compact = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
var full = Card(counts: true, withScenarios: true);
File.WriteAllText(output, full.ToJsonString(indented) + "\n");
int Bytes(JsonObject o, JsonSerializerOptions opts) => Encoding.UTF8.GetByteCount(o.ToJsonString(opts));
Console.WriteLine($"card bytes (indented / compact): full {Bytes(full, indented)} / {Bytes(full, compact)}; "
                  + $"no scenarios {Bytes(Card(true, false), indented)} / {Bytes(Card(true, false), compact)}; "
                  + $"no counts, no scenarios {Bytes(Card(false, false), indented)} / {Bytes(Card(false, false), compact)}");

static string? Text(JsonElement e, string name) =>
    e.TryGetProperty(name, out var v) ? v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Number => v.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null
    } : null;

static string? StatusOf(JsonElement e) => Text(e, "statusCode") ?? Text(e, "statusText");

static bool IsError(string? status) =>
    status is not null && int.TryParse(status, out var code) ? code >= 400 : status is not null && status.StartsWith('!');

sealed record Call(int Scenario, int Index, string Type, string Method, string Uri, string Service, string Caller, string? Category,
    string? CallerCategory, string? Phase, string? Rrid, string? TraceId, string? Status, string? Content, string? MetaType);

sealed class Op(string from, string to, string protocol, string method, string target)
{
    public string From { get; } = from;
    public string To { get; } = to;
    public string Protocol { get; } = protocol;
    public string Method { get; } = method;
    public string Target { get; } = target;
    public int Calls { get; set; }
    public int Errors { get; set; }
    public SortedDictionary<string, int> Origins { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> Phases { get; } = new(StringComparer.Ordinal);
    public HashSet<int> Scenarios { get; } = [];
}
