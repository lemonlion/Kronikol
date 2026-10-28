#:project ../../../src/Kronikol/Kronikol.csproj
#:property PublishAot=false
// The service card's `behaviour` part as SERVICE_CARD_PLAN.md 4.3 lays it out, built from a decoded data file, with the
// parties #190 R1's resolver gives (`kronikol query parties <report> --json`, 5.14.0), so two runs' cards can be compared
// byte for byte. Written for the plan's deep dive B (re-run stability, Q6); a probe, not the design.
//
// What it follows from the plan: op ids (16 hex of SHA-256 over from, to, protocol, method, target joined by U+001F),
// sets only, ordinal order; origin = the caller's role (#192 4.3) from R1's kinds; effective phase = recorded Setup or
// Action, else the parent's effective phase, else unknown (4.2); exposes, messages, scenarios, unresolved, capture.
// Where #192's key is not built yet, it approximates: the target is the templated path without the query (#192's
// `queryKeys` off for the card), plus the statement head (InteractionShape.StatementHead, the product's) for a
// statement-shaped call, plus the GraphQL operation (a light reader: the named operation, else the sorted root fields).
// `cause` is not designed in detail by #192 (its Q4); two readings are computed and their disagreement printed:
//   nesting first:     test-origin -> test; inside a call the caller received -> request; else background when the
//                      record has an attribution or the caller is the subject; else unknown
//   attribution first: test-origin -> test; RequestHeader -> request; any other attribution -> background; none ->
//                      the nesting reading
// The card uses the nesting-first reading. Prints counts only, never bodies.
//
// Usage: dotnet run --file card-behaviour.cs -- <decoded.json> <parties-envelope.json> <out-prefix> [--with-background-section]
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kronikol.Constants;
using Kronikol.History;

var input = args[0];
var partiesPath = args[1];
var prefix = args[2];
var withBackgroundSection = args.Contains("--with-background-section");
// --cause-attribution: the card's causes read attributionSource first (#192 4.3's wording), and a call whose cause is
// background keeps the phase it recorded (the plan's 4.2 sentence), so a false parent cannot lend it one.
var attributionFirst = args.Contains("--cause-attribution");
// --scenario-ops-no-background: a scenario's ops leave out its calls whose cause is background (Q6's option).
var scenarioOpsNoBackground = args.Contains("--scenario-ops-no-background");
// --scenario-ops-test-only: a scenario's ops are only the operations the test itself called (origin test).
var scenarioOpsTestOnly = args.Contains("--scenario-ops-test-only");
// --phase-any-parent: the effective phase comes from any nesting parent, whatever the call's cause (4.2 read literally).
var phaseAnyParent = args.Contains("--phase-any-parent");

var statementHead = typeof(InteractionShape).GetMethod("StatementHead", BindingFlags.NonPublic | BindingFlags.Static)
    ?? throw new InvalidOperationException("InteractionShape.StatementHead not found");

// R1's parties, as `query parties --json` printed them.
using var partiesDoc = JsonDocument.Parse(File.ReadAllText(partiesPath));
var partyRows = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
foreach (var item in partiesDoc.RootElement.GetProperty("items").EnumerateArray())
    partyRows[item.GetProperty("party").GetString()!] = item.Clone();
string KindOf(string name) => partyRows.TryGetValue(name, out var row) ? row.GetProperty("kind").GetString() ?? "unknown" : "unknown";
var subject = partyRows.Values.Where(r => r.GetProperty("kind").GetString() == "underTest")
    .OrderByDescending(r => r.GetProperty("callsReceived").GetInt32())
    .ThenBy(r => r.GetProperty("party").GetString(), StringComparer.Ordinal)
    .Select(r => r.GetProperty("party").GetString()).FirstOrDefault();

using var stream = File.OpenRead(input);
using var document = JsonDocument.Parse(stream);
var root = document.RootElement;

var scenarios = new List<Scenario>();
var calls = new List<Call>();
foreach (var feature in root.GetProperty("features").EnumerateArray())
{
    var featureName = Text(feature, "name") ?? "";
    if (!feature.TryGetProperty("scenarios", out var list))
        continue;
    foreach (var s in list.EnumerateArray())
    {
        var ordinal = scenarios.Count;
        var tags = s.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Array
            ? t.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString()! : x.GetRawText()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList()
            : [];
        scenarios.Add(new Scenario(ordinal, Text(s, "stableId") ?? "", featureName, Text(s, "name") ?? "", tags, Text(s, "sourceFile"), Text(s, "sourceLine"), Text(s, "result")));
        if (s.TryGetProperty("httpInteractions", out var interactions))
            Read(interactions, ordinal);
    }
}
var backgroundSectionRequests = 0;
if (withBackgroundSection && root.TryGetProperty("background", out var bg) && bg.ValueKind == JsonValueKind.Object
    && bg.TryGetProperty("interactions", out var bgi) && bgi.ValueKind == JsonValueKind.Array)
{
    var before = calls.Count(c => c.Type == "Request");
    Read(bgi, -1);
    backgroundSectionRequests = calls.Count(c => c.Type == "Request") - before;
}

void Read(JsonElement interactions, int ordinal)
{
    var index = 0;
    foreach (var i in interactions.EnumerateArray())
        calls.Add(new Call(ordinal, index++, Text(i, "type") ?? "", Text(i, "method") ?? "", Text(i, "uri") ?? "",
            Text(i, "serviceName") ?? "", Text(i, "callerName") ?? "", NullIfEmpty(Text(i, "dependencyCategory")),
            NullIfEmpty(Text(i, "callerDependencyCategory")), Text(i, "phase"), Text(i, "requestResponseId"), Text(i, "traceId"),
            Text(i, "statusCode") ?? Text(i, "statusText"), Text(i, "content"), Text(i, "metaType"), Text(i, "attributionSource")));
}

var requests = calls.Where(c => c.Type == "Request" && c.Service.Length > 0 && c.Caller.Length > 0).ToList();
var responses = calls.Where(c => c.Type == "Response" && c.Rrid is not null)
    .GroupBy(c => (c.Scenario, c.Rrid)).ToDictionary(g => g.Key, g => g.First());

// CallNesting's rule (src/Kronikol/Query/CallNesting.cs at 8d7d1e1d), per scenario, in file order.
var parentOf = new Dictionary<Call, Call?>(ReferenceEqualityComparer.Instance);
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

string Origin(Call r) => KindOf(r.Caller) switch { "test" => "test", "underTest" => "subject", _ => "other" };

bool Nested(Call r)
{
    for (var p = parentOf.GetValueOrDefault(r); p is not null; p = parentOf.GetValueOrDefault(p))
        if (p.Service == r.Caller)
            return true;
    return false;
}

bool IsDelivery(Call r) => r.CallerCategory is not null;

bool ViaDelivery(Call r)
{
    for (Call? p = r; p is not null; p = parentOf.GetValueOrDefault(p))
        if (IsDelivery(p))
            return true;
    return false;
}

string CauseNesting(Call r) =>
    Origin(r) == "test" ? "test"
    : Nested(r) ? "request"
    : r.Attribution is not null || r.Caller == subject ? "background"
    : "unknown";

string CauseAttribution(Call r) =>
    Origin(r) == "test" ? "test"
    : r.Attribution == "RequestHeader" ? "request"
    : r.Attribution is not null ? "background"
    : Nested(r) ? "request" : "unknown";

string Cause(Call r) => attributionFirst ? CauseAttribution(r) : CauseNesting(r);

var effective = new Dictionary<Call, string>(ReferenceEqualityComparer.Instance);
string EffectivePhase(Call r)
{
    if (effective.TryGetValue(r, out var known))
        return known;
    var recorded = r.Phase;
    string value = recorded is "Setup" or "Action" ? recorded.ToLowerInvariant()
        : (!attributionFirst || phaseAnyParent || Cause(r) != "background") && parentOf.GetValueOrDefault(r) is { } p ? EffectivePhase(p)
        : recorded is null or "" or "Unknown" ? "unknown" : recorded.ToLowerInvariant();
    effective[r] = value;
    return value;
}

string Protocol(Call r) => r.Category ?? (r.MetaType == "Event" ? r.Method : "HTTP");

string PathOf(string uri)
{
    // WarmUpCalls.PathOf's cut: an absolute URI loses scheme and authority, any URI its query and fragment.
    var text = uri;
    if (Uri.TryCreate(uri, UriKind.Absolute, out var u) && !string.IsNullOrEmpty(u.Scheme) && uri.Contains("://", StringComparison.Ordinal))
    {
        var start = uri.IndexOf("://", StringComparison.Ordinal) + 3;
        var slash = uri.IndexOf('/', start);
        text = slash < 0 ? "/" : uri[slash..];
    }
    var cut = text.IndexOfAny(['?', '#']);
    return cut < 0 ? text : text[..cut];
}

string Target(Call r)
{
    var templated = InteractionShape.Template(PathOf(r.Uri));
    if (DependencyCategories.IsStatementShaped(r.Category) && !string.IsNullOrWhiteSpace(r.Content))
        return templated + " " + (string)statementHead.Invoke(null, [r.Content, null])!;
    return templated;
}

var graphQlCount = 0;
string? GraphQl(Call r)
{
    if (r.Category is not null && r.Category != "HTTP" || string.IsNullOrWhiteSpace(r.Content) || r.Content.TrimStart()[0] != '{')
        return null;
    try
    {
        using var body = JsonDocument.Parse(r.Content);
        if (body.RootElement.ValueKind != JsonValueKind.Object || !body.RootElement.TryGetProperty("query", out var q) || q.ValueKind != JsonValueKind.String)
            return null;
        var name = body.RootElement.TryGetProperty("operationName", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
        var op = GraphQlDocument.Operation(q.GetString()!, name);
        if (op is not null) graphQlCount++;
        return op;
    }
    catch (JsonException)
    {
        return null;
    }
}

static string OpId(string from, string to, string protocol, string method, string target, string? graphQl) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001f', from, to, protocol, method, graphQl is null ? target : target + " " + graphQl))))[..16].ToLowerInvariant();

var ops = new SortedDictionary<string, Op>(StringComparer.Ordinal);
var perScenario = new SortedDictionary<int, SortedDictionary<string, OpInScenario>>();
var causeDisagree = new SortedDictionary<string, int>(StringComparer.Ordinal);
var partyCategories = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
var derivedPhases = 0;
var cardScenarioOps = new SortedDictionary<int, SortedSet<string>>();
var opOf = new Dictionary<Call, string>(ReferenceEqualityComparer.Instance);
var testScenarioOps = new SortedDictionary<int, SortedSet<string>>();
foreach (var r in requests)
{
    var protocol = Protocol(r);
    var target = Target(r);
    var graphQl = GraphQl(r);
    var id = OpId(r.Caller, r.Service, protocol, r.Method, target, graphQl);
    opOf[r] = id;
    if (!ops.TryGetValue(id, out var op))
        ops[id] = op = new Op(id, r.Caller, r.Service, protocol, r.Method, target, graphQl);
    op.Calls++;
    op.FirstAddress ??= $"s{r.Scenario}/i{r.Index}";
    if (IsDelivery(r)) op.Delivery = true;
    if (r.MetaType == "Event") op.Event = true;
    responses.TryGetValue((r.Scenario, r.Rrid), out var response);
    var status = response?.Status ?? "none";
    var origin = Origin(r);
    var cause = Cause(r);
    var causeAttr = CauseAttribution(r);
    if (CauseNesting(r) != causeAttr)
        Bump(causeDisagree, $"{origin}: nesting {CauseNesting(r)} / attribution {causeAttr} ({r.Attribution ?? "none"})");
    if ((!scenarioOpsNoBackground || cause != "background") && (!scenarioOpsTestOnly || origin == "test"))
    {
        if (!cardScenarioOps.TryGetValue(r.Scenario, out var set)) cardScenarioOps[r.Scenario] = set = new SortedSet<string>(StringComparer.Ordinal);
        set.Add(id);
    }
    var phase = EffectivePhase(r);
    if (r.Phase is not ("Setup" or "Action") && phase is "setup" or "action")
    {
        derivedPhases++;
        op.DerivedPhases++;
    }
    if (origin == "test")
    {
        if (!testScenarioOps.TryGetValue(r.Scenario, out var own)) testScenarioOps[r.Scenario] = own = new SortedSet<string>(StringComparer.Ordinal);
        own.Add(id);
    }
    Bump(op.Statuses, status);
    Bump(op.Origins, origin);
    Bump(op.Causes, cause);
    Bump(op.CausesAttr, causeAttr);
    Bump(op.Phases, phase);
    Bump(op.Attributions, r.Attribution ?? "none");
    if (ViaDelivery(r)) op.ViaDelivery++;
    op.Scenarios.Add(r.Scenario);
    if (!partyCategories.TryGetValue(r.Service, out var sc)) partyCategories[r.Service] = sc = new SortedSet<string>(StringComparer.Ordinal);
    if (r.Category is not null) sc.Add(r.Category);
    if (!partyCategories.TryGetValue(r.Caller, out var cc)) partyCategories[r.Caller] = cc = new SortedSet<string>(StringComparer.Ordinal);
    if (r.CallerCategory is not null) cc.Add(r.CallerCategory);
    if (!perScenario.TryGetValue(r.Scenario, out var map)) perScenario[r.Scenario] = map = new SortedDictionary<string, OpInScenario>(StringComparer.Ordinal);
    if (!map.TryGetValue(id, out var ois)) map[id] = ois = new OpInScenario();
    ois.Calls++;
    Bump(ois.Causes, cause);
    Bump(ois.CausesAttr, causeAttr);
    Bump(ois.Attributions, r.Attribution ?? "none");
    if (ViaDelivery(r)) ois.ViaDelivery++;
}

JsonArray Arr(IEnumerable<string> values) => new(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());

JsonObject Behaviour(bool withScenarios, SortedDictionary<int, SortedSet<string>>? scenarioOps = null)
{
    scenarioOps ??= cardScenarioOps;
    var parties = new JsonArray();
    foreach (var name in partyCategories.Keys)
    {
        partyRows.TryGetValue(name, out var row);
        var hasRow = row.ValueKind == JsonValueKind.Object;
        parties.Add(new JsonObject
        {
            ["name"] = name,
            ["id"] = hasRow && row.TryGetProperty("id", out var pid) && pid.ValueKind == JsonValueKind.String ? pid.GetString() : null,
            ["kind"] = hasRow ? row.GetProperty("kind").GetString() : "unknown",
            ["kindSource"] = hasRow && row.TryGetProperty("kindSource", out var ks) && ks.ValueKind == JsonValueKind.String ? ks.GetString() : null,
            ["note"] = hasRow && row.TryGetProperty("note", out var pn) && pn.ValueKind == JsonValueKind.String ? pn.GetString() : null,
            ["categories"] = Arr(partyCategories[name])
        });
    }

    var edges = new JsonArray();
    foreach (var edge in ops.Values.GroupBy(o => (o.From, o.To, o.Protocol))
                 .OrderBy(g => g.Key.From, StringComparer.Ordinal).ThenBy(g => g.Key.To, StringComparer.Ordinal).ThenBy(g => g.Key.Protocol, StringComparer.Ordinal))
    {
        var operations = new JsonArray();
        foreach (var op in edge.OrderBy(o => o.Method, StringComparer.Ordinal).ThenBy(o => o.Target, StringComparer.Ordinal).ThenBy(o => o.GraphQl ?? "", StringComparer.Ordinal))
            operations.Add(new JsonObject
            {
                ["op"] = op.Id, ["method"] = op.Method, ["target"] = op.Target, ["graphqlOperation"] = op.GraphQl,
                ["statuses"] = Arr(op.Statuses.Keys), ["origins"] = Arr(op.Origins.Keys), ["causes"] = Arr(op.Causes.Keys),
                ["effectivePhases"] = Arr(op.Phases.Keys)
            });
        edges.Add(new JsonObject { ["from"] = edge.Key.From, ["to"] = edge.Key.To, ["protocol"] = edge.Key.Protocol, ["operations"] = operations });
    }

    var exposes = ops.Values.Where(o => o.To == subject && !o.Delivery).Select(o => o.Id).Order(StringComparer.Ordinal);
    var messages = new JsonArray();
    foreach (var m in ops.Values.Select(o => (Op: o, Message: o.Message(subject))).Where(x => x.Message is not null)
                 .OrderBy(x => x.Message!.Value.Party, StringComparer.Ordinal).ThenBy(x => x.Message!.Value.Topic, StringComparer.Ordinal)
                 .ThenBy(x => x.Message!.Value.Direction, StringComparer.Ordinal).ThenBy(x => x.Op.Id, StringComparer.Ordinal))
        messages.Add(new JsonObject { ["op"] = m.Op.Id, ["direction"] = m.Message!.Value.Direction, ["party"] = m.Message!.Value.Party, ["topic"] = m.Message!.Value.Topic });

    var card = new JsonObject
    {
        ["rules"] = new JsonObject { ["operationKey"] = "probe-1", ["templateRules"] = null, ["inference"] = 1 },
        ["subject"] = new JsonObject { ["name"] = subject, ["id"] = null, ["source"] = subject is null ? null : partyRows[subject].TryGetProperty("kindSource", out var s) ? s.GetString() : null },
        ["parties"] = parties,
        ["edges"] = edges,
        ["exposes"] = new JsonObject { ["basis"] = "tested", ["ops"] = Arr(exposes) },
        ["messages"] = messages
    };
    if (withScenarios)
    {
        var rows = new JsonArray();
        foreach (var bySid in scenarios.GroupBy(s => s.Sid).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var first = bySid.First();
            var touched = bySid.SelectMany(s => scenarioOps.TryGetValue(s.Ordinal, out var m) ? m : Enumerable.Empty<string>())
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
            rows.Add(new JsonObject
            {
                ["sid"] = bySid.Key, ["feature"] = first.Feature, ["scenario"] = first.Name, ["tags"] = Arr(first.Tags),
                ["source"] = first.SourceFile is null ? null : new JsonObject { ["file"] = first.SourceFile, ["line"] = first.SourceLine is null ? null : int.Parse(first.SourceLine) },
                ["ops"] = Arr(touched)
            });
        }
        card["scenarios"] = rows;
    }
    card["unresolved"] = Arr(partyCategories.Keys.Where(n => !(partyRows.TryGetValue(n, out var row) && row.TryGetProperty("id", out var pid) && pid.ValueKind == JsonValueKind.String)));
    card["capture"] = new JsonObject
    {
        ["calls"] = requests.Count > 0 ? "recorded" : "notRecorded",
        ["subjectCalls"] = requests.Any(r => r.Caller == subject) ? "recorded" : "notRecorded",
        ["emptyTargets"] = Arr(ops.Values.Where(o => o.Target.Length == 0 || o.Target == "/").Select(o => o.Id).Order(StringComparer.Ordinal)),
        ["degraded"] = new JsonArray()
    };
    return card;
}

var compact = new JsonSerializerOptions { Encoder = JavaScriptEncoder.Default };
var indented = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.Default };
var behaviour = Behaviour(withScenarios: true);
var compactBytes = Encoding.UTF8.GetBytes(behaviour.ToJsonString(compact));
var hash = Convert.ToHexString(SHA256.HashData(compactBytes)).ToLowerInvariant();
File.WriteAllText(prefix + ".behaviour.json", behaviour.ToJsonString(indented).Replace("\r\n", "\n") + "\n", new UTF8Encoding(false));
var noScenarios = Behaviour(withScenarios: false);
var noScenariosHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(noScenarios.ToJsonString(compact)))).ToLowerInvariant();

// The detail beside it: what the diff needs to classify a difference. Not part of any card.
var detail = new JsonObject
{
    ["subject"] = subject,
    ["ops"] = new JsonObject(ops.Values.Select(o => KeyValuePair.Create(o.Id, (JsonNode?)new JsonObject
    {
        ["label"] = $"{o.From} > {o.To} [{o.Protocol}] {o.Method} {Cut(o.Target, 140)}{(o.GraphQl is null ? "" : " :: " + o.GraphQl)}",
        ["calls"] = o.Calls, ["scenarios"] = o.Scenarios.Count, ["viaDelivery"] = o.ViaDelivery,
        ["statuses"] = Obj(o.Statuses), ["origins"] = Obj(o.Origins), ["causes"] = Obj(o.Causes), ["causesAttr"] = Obj(o.CausesAttr),
        ["phases"] = Obj(o.Phases), ["attributions"] = Obj(o.Attributions)
    }))),
    ["scenarios"] = new JsonObject(perScenario.Where(e => e.Key >= 0).Select(e => KeyValuePair.Create(scenarios[e.Key].Sid + "#" + e.Key, (JsonNode?)new JsonObject
    {
        ["sid"] = scenarios[e.Key].Sid, ["name"] = Cut(scenarios[e.Key].Name, 100), ["result"] = scenarios[e.Key].Result,
        ["ops"] = new JsonObject(e.Value.Select(x => KeyValuePair.Create(x.Key, (JsonNode?)new JsonObject
        {
            ["calls"] = x.Value.Calls, ["causes"] = Obj(x.Value.Causes), ["causesAttr"] = Obj(x.Value.CausesAttr),
            ["attributions"] = Obj(x.Value.Attributions), ["viaDelivery"] = x.Value.ViaDelivery
        })))
    }))),
    ["scenarioResults"] = new JsonObject(scenarios.GroupBy(s => s.Sid).Select(g => KeyValuePair.Create(g.Key, (JsonNode?)JsonValue.Create(g.First().Result)))),
    ["backgroundSectionOps"] = perScenario.TryGetValue(-1, out var bgOps) ? Arr(bgOps.Keys) : new JsonArray()
};
File.WriteAllText(prefix + ".detail.json", detail.ToJsonString(indented).Replace("\r\n", "\n") + "\n", new UTF8Encoding(false));
File.WriteAllText(prefix + ".hash.txt", $"{hash}\n{noScenariosHash}\n");

// The whole file as 4.3 lays it out: `run` (this run's facts, counts and example addresses) then `behaviour`.
JsonObject RunPart(string behaviourHash)
{
    var ci = root.TryGetProperty("ciMetadata", out var c) && c.ValueKind == JsonValueKind.Object ? c : default;
    string? Ci(string name) => ci.ValueKind == JsonValueKind.Object ? Text(ci, name) : null;
    var results = scenarios.GroupBy(x => x.Sid).Select(g => g.First().Result).ToList();
    return new JsonObject
    {
        ["id"] = Ci("runId") is { } runId ? $"gh:{runId}:{Ci("runAttempt") ?? "1"}" : "local",
        ["at"] = Text(root, "endTime"),
        ["repository"] = Ci("repository"), ["commit"] = Ci("commitSha"), ["branch"] = Ci("branch"),
        ["suite"] = Text(root, "suite"), ["framework"] = null, ["kronikolVersion"] = "5.N.0",
        ["report"] = new JsonObject { ["file"] = "TestRunReport.krkl", ["formatVersion"] = Text(root, "formatVersion") is { } fv ? int.Parse(fv) : null, ["sha256"] = new string('0', 64) },
        ["results"] = new JsonObject { ["scenarios"] = results.Count, ["passed"] = results.Count(x => x == "Passed"), ["failed"] = results.Count(x => x == "Failed"), ["skipped"] = results.Count(x => x is not ("Passed" or "Failed")) },
        ["behaviourHash"] = behaviourHash,
        ["counts"] = new JsonArray(ops.Values.Select(o => (JsonNode?)new JsonObject
        {
            ["op"] = o.Id, ["calls"] = o.Calls, ["statuses"] = new JsonObject(o.Statuses.Select(e => KeyValuePair.Create(e.Key, (JsonNode?)e.Value))), ["derivedPhases"] = o.DerivedPhases
        }).ToArray()),
        ["examples"] = new JsonArray(ops.Values.Select(o => (JsonNode?)new JsonObject { ["op"] = o.Id, ["address"] = o.FirstAddress }).ToArray())
    };
}
JsonObject WholeFile(JsonObject behaviourPart, string behaviourHash) => new() { ["formatVersion"] = 1, ["run"] = RunPart(behaviourHash), ["behaviour"] = behaviourPart };
string Sha(JsonObject o) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(o.ToJsonString(compact)))).ToLowerInvariant();
int Size(JsonObject o, JsonSerializerOptions opts) => Encoding.UTF8.GetByteCount(o.ToJsonString(opts).Replace(Environment.NewLine, "\n"));
var noBg = new SortedDictionary<int, SortedSet<string>>();
foreach (var r in requests)
    if (Cause(r) != "background")
    {
        if (!noBg.TryGetValue(r.Scenario, out var set)) noBg[r.Scenario] = set = new SortedSet<string>(StringComparer.Ordinal);
        set.Add(opOf[r]);
    }
var sizeLines = new List<string>();
foreach (var (name, part) in new[]
         {
             ("scenario ops: every call", Behaviour(true, cardScenarioOps)),
             ("scenario ops: background out", Behaviour(true, noBg)),
             ("scenario ops: the test's own calls", Behaviour(true, testScenarioOps)),
             ("no scenario index", Behaviour(false))
         })
{
    var whole = WholeFile(part, Sha(part));
    sizeLines.Add($"  {name,-36} file indented {Size(whole, indented),7} compact {Size(whole, compact),7} | behaviour indented {Size(part, indented),7} compact {Size(part, compact),7}");
}
File.WriteAllText(prefix + ".service.json", WholeFile(behaviour, hash).ToJsonString(indented).Replace("\r\n", "\n") + "\n", new UTF8Encoding(false));

var byCause = requests.GroupBy(Cause).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key} {g.Count()}");
var byOriginCause = requests.GroupBy(r => Origin(r) + "/" + Cause(r)).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key} {g.Count()}");
Console.WriteLine($"subject {subject}; tests {string.Join(",", partyRows.Values.Where(r => r.GetProperty("kind").GetString() == "test").Select(r => r.GetProperty("party").GetString()))}; "
                  + $"scenarios {scenarios.Count} (sids {scenarios.Select(s => s.Sid).Distinct().Count()}); requests {requests.Count}; background-section requests {backgroundSectionRequests}");
Console.WriteLine($"parties {partyCategories.Count}; edges {ops.Values.Select(o => (o.From, o.To, o.Protocol)).Distinct().Count()}; ops {ops.Count}; exposes {((JsonArray)behaviour["exposes"]!["ops"]!).Count}; "
                  + $"messages {((JsonArray)behaviour["messages"]!).Count}; graphql-labelled calls {graphQlCount}; derived phases {derivedPhases}");
Console.WriteLine($"calls by cause ({(attributionFirst ? "attribution" : "nesting")} first): " + string.Join(", ", byCause));
Console.WriteLine("calls by origin/cause: " + string.Join(", ", byOriginCause));
Console.WriteLine("cause readings disagree: " + (causeDisagree.Count == 0 ? "none" : string.Join("; ", causeDisagree.Select(e => $"{e.Key} x{e.Value}"))));
Console.WriteLine($"behaviour bytes compact {compactBytes.Length}, indented {Encoding.UTF8.GetByteCount(behaviour.ToJsonString(indented).Replace(Environment.NewLine, "\n"))}; "
                  + $"without scenarios compact {Encoding.UTF8.GetByteCount(noScenarios.ToJsonString(compact))}");
Console.WriteLine($"behaviourHash {hash}; without scenarios {noScenariosHash}");
Console.WriteLine("sizes (bytes, UTF-8):");
foreach (var line in sizeLines) Console.WriteLine(line);

static void Bump(SortedDictionary<string, int> map, string key) => map[key] = map.GetValueOrDefault(key) + 1;
static JsonObject Obj(SortedDictionary<string, int> map) => new(map.Select(e => KeyValuePair.Create(e.Key, (JsonNode?)e.Value)));
static string Cut(string text, int n) => text.Length <= n ? text : text[..n] + "...";
static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

static string? Text(JsonElement e, string name) =>
    e.TryGetProperty(name, out var v) ? v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Number => v.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null
    } : null;

sealed record Scenario(int Ordinal, string Sid, string Feature, string Name, List<string> Tags, string? SourceFile, string? SourceLine, string? Result);

sealed record Call(int Scenario, int Index, string Type, string Method, string Uri, string Service, string Caller, string? Category,
    string? CallerCategory, string? Phase, string? Rrid, string? TraceId, string? Status, string? Content, string? MetaType, string? Attribution);

sealed class OpInScenario
{
    public int Calls { get; set; }
    public int ViaDelivery { get; set; }
    public SortedDictionary<string, int> Causes { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> CausesAttr { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> Attributions { get; } = new(StringComparer.Ordinal);
}

sealed class Op(string id, string from, string to, string protocol, string method, string target, string? graphQl)
{
    public string Id { get; } = id;
    public string From { get; } = from;
    public string To { get; } = to;
    public string Protocol { get; } = protocol;
    public string Method { get; } = method;
    public string Target { get; } = target;
    public string? GraphQl { get; } = graphQl;
    public int Calls { get; set; }
    public int ViaDelivery { get; set; }
    public int DerivedPhases { get; set; }
    public string? FirstAddress { get; set; }
    public SortedDictionary<string, int> Statuses { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> Origins { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> Causes { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> CausesAttr { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> Phases { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> Attributions { get; } = new(StringComparer.Ordinal);
    public HashSet<int> Scenarios { get; } = [];

    // A delivery is a broker's call into a consumer: its record carries the caller's category.
    public bool Delivery { get; set; }
    public bool Event { get; set; }

    public (string Direction, string Party, string Topic)? Message(string? subject)
    {
        // The plan's 4.2: a message category, or meta type Event; a delivery is one too.
        var isMessage = Protocol is "MessageQueue" || Event || Delivery;
        if (!isMessage)
            return null;
        var path = Target.Split(' ')[0].TrimEnd('/');
        var topic = path.Contains('/') ? path[(path.LastIndexOf('/') + 1)..] : path;
        if (From == subject)
            return (Method.Equals("Consume", StringComparison.OrdinalIgnoreCase) ? "consumes" : "publishes", To, topic);
        if (To == subject)
            return ("consumes", From, topic);
        return ("other", From + ">" + To, topic);
    }
}

static class GraphQlDocument
{
    // A light reader for the probe: the operation named by operationName or the document's first operation, typed;
    // an anonymous one is named by its root fields, sorted and de-duplicated, aliases and arguments dropped.
    public static string? Operation(string query, string? operationName)
    {
        var text = string.Join("\n", query.Split('\n').Select(l => l.Contains('#') ? l[..l.IndexOf('#')] : l)).Trim();
        if (text.Length == 0)
            return null;
        var type = "query";
        var pos = 0;
        string? name = null;
        foreach (var keyword in new[] { "query", "mutation", "subscription" })
        {
            if (text.StartsWith(keyword, StringComparison.Ordinal) && (text.Length == keyword.Length || !char.IsLetterOrDigit(text[keyword.Length])))
            {
                type = keyword;
                pos = keyword.Length;
                while (pos < text.Length && char.IsWhiteSpace(text[pos])) pos++;
                var start = pos;
                while (pos < text.Length && (char.IsLetterOrDigit(text[pos]) || text[pos] == '_')) pos++;
                if (pos > start) name = text[start..pos];
                break;
            }
        }
        if (pos == 0 && text[0] != '{')
            return null;
        if (operationName is not null)
            return $"{type} {operationName}";
        if (name is not null)
            return $"{type} {name}";
        var open = text.IndexOf('{', pos);
        if (open < 0)
            return null;
        var fields = new SortedSet<string>(StringComparer.Ordinal);
        var depth = 0;
        var paren = 0;
        var token = new StringBuilder();
        string? pending = null;
        for (var i = open; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '(') { paren++; continue; }
            if (c == ')') { paren--; continue; }
            if (paren > 0) continue;
            if (c == '{') { Flush(); depth++; continue; }
            if (c == '}') { Flush(); depth--; if (depth == 0) break; continue; }
            if (depth != 1) continue;
            if (char.IsLetterOrDigit(c) || c == '_') { token.Append(c); continue; }
            if (c == ':') { token.Clear(); pending = null; continue; }
            Flush();
        }
        Flush();
        return fields.Count == 0 ? null : $"{type} {{ {string.Join(" ", fields)} }}";

        void Flush()
        {
            if (depth == 1 && token.Length > 0)
            {
                var t = token.ToString();
                // An alias is followed by ':'; peek is not possible here, so an alias's name is cleared at ':' above.
                if (t != "on") fields.Add(t);
            }
            token.Clear();
            _ = pending;
        }
    }
}
