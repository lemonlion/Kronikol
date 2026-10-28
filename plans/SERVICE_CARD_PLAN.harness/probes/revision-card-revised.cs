#:project ../../../src/Kronikol/Kronikol.csproj
#:property PublishAot=false
// The whole service.json in SERVICE_CARD_PLAN.md 4.3's revised shape (the plan's revision of 2026-10-27, after its three
// audits), built from a decoded data file, for the card's sizes. A probe, not the design. Grown from audit B's
// card-behaviour.cs (results/audit-B-*.txt): the same op ids, targets, GraphQL reader, nesting, origin and cause, with
// these changes, each the revised plan's:
//   - the data file's root background.interactions are read beside the scenarios' calls (#190 R3's index); a call from
//     there is in no scenario and has no example address;
//   - cause reads attributionSource first, then nesting (B's P1), and a call whose cause is background keeps the phase it
//     recorded (B's P2);
//   - behaviour.scenarios[].ops holds the test's own operations (B's P4; with no test party, the scenario's top-level
//     calls that are not background); run.scenarios holds every operation the scenario's calls reached, background
//     calls left out;
//   - run gains report.kronikolVersion, environment, results.bypassed, parties (hosts, doubleCalls), errors and phase
//     counts per op, and degraded (moved from behaviour.capture);
//   - messages take direction and topic from a table per URI scheme (4.2), and capture.emptyTargets names only the
//     operations whose message destination the URI does not hold;
//   - unknown parties carry kindSource null; rules.templateRules is null (the reports predate #192's R2) and
//     rules.inference 1.
// The file is written compact with behaviour last, and indented for comparison; behaviourHash is the SHA-256 of the
// behaviour value's bytes as the compact file holds them. Prints sizes and counts only, never a body.
//
// Without a flag the probe reads a message as the revision of 2026-10-27 did: a call whose category is MessageQueue,
// whose meta type is Event, or that carries a caller category. That reading counted Spanner's calls (meta type Event) as
// publishes, and the results/revision-*.txt files were written with it. --corrected-broker-rule applies the rule of the
// plan's verification pass (2026-10-28, plan 4.2 "Messages"): a call between the subject and another party is a
// message call when it carries the category MessageQueue, as its own category or as its caller category, or when its
// URI scheme is a row of the message table, and the other party is then the broker; a meta type of Event makes none. A
// delivery is a message call into the subject, whichever side its tracker put the category on, and its category counts
// for the broker, not the subject. With the flag the probe also prints the brokers and the messages rows per broker
// (results/verify-sizes.txt).
//
// Usage: dotnet run --file revision-card-revised.cs -- <decoded.json> <parties-envelope.json> <Run.json or -> <report file or -> <out-prefix> [--corrected-broker-rule]
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
var runJsonPath = args[2];
var reportPath = args[3];
var prefix = args[4];

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
var hasTestParty = partyRows.Values.Any(r => r.GetProperty("kind").GetString() == "test");

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
        var notATest = s.TryGetProperty("notATest", out var nat) && nat.ValueKind == JsonValueKind.True;
        scenarios.Add(new Scenario(ordinal, Text(s, "stableId") ?? "", featureName, Text(s, "name") ?? "", tags, Text(s, "sourceFile"), Text(s, "sourceLine"), Text(s, "result"), notATest));
        if (s.TryGetProperty("httpInteractions", out var interactions))
            Read(interactions, ordinal);
    }
}
var backgroundSectionRequests = 0;
if (root.TryGetProperty("background", out var bg) && bg.ValueKind == JsonValueKind.Object
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
            Text(i, "statusCode"), Text(i, "statusText"), Text(i, "content"), Text(i, "metaType"), Text(i, "attributionSource"),
            Text(i, "testDouble")));
}

var requests = calls.Where(c => c.Type == "Request" && c.Service.Length > 0 && c.Caller.Length > 0).ToList();
var responses = calls.Where(c => c.Type == "Response" && c.Rrid is not null)
    .GroupBy(c => (c.Scenario, c.Rrid)).ToDictionary(g => g.Key, g => g.First());

// The verification pass's rule (--corrected-broker-rule): a call between the subject and another party is a message
// call when it carries MessageQueue on either side or its URI scheme is a row of the table, and the other party is then
// its broker. The category can sit on a delivery itself: Kronikol's Kafka and MassTransit trackers put it there, and so
// does the generic MessageTracker unless its CallerDependencyCategory is set.
var correctedBrokerRule = args.Skip(5).Contains("--corrected-broker-rule");
bool IsMessageCall(Call r) =>
    subject is not null && (r.Service == subject) != (r.Caller == subject)
    && (r.Category == "MessageQueue" || r.CallerCategory == "MessageQueue" || MessageTable.InTable(r.Uri));
var brokers = new SortedSet<string>(StringComparer.Ordinal);
if (correctedBrokerRule)
    foreach (var r in requests.Where(IsMessageCall))
        brokers.Add(r.Service == subject ? r.Caller : r.Service);

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

bool IsDelivery(Call r) => correctedBrokerRule
    ? r.Service == subject && IsMessageCall(r)
    : r.CallerCategory is not null;

// Attribution first, then nesting (audit B's P1 to P4): the reading the revised plan takes.
string Cause(Call r) =>
    Origin(r) == "test" ? "test"
    : r.Attribution == "RequestHeader" ? "request"
    : r.Attribution is not null ? "background"
    : Nested(r) ? "request" : "unknown";

// A background call keeps the phase it recorded; any other call that recorded none takes its parent's.
var effective = new Dictionary<Call, string>(ReferenceEqualityComparer.Instance);
string EffectivePhase(Call r)
{
    if (effective.TryGetValue(r, out var known))
        return known;
    var recorded = r.Phase;
    string value = recorded is "Setup" or "Action" ? recorded.ToLowerInvariant()
        : Cause(r) != "background" && parentOf.GetValueOrDefault(r) is { } p ? EffectivePhase(p)
        : "unknown";
    effective[r] = value;
    return value;
}

string Protocol(Call r) => r.Category ?? (r.MetaType == "Event" ? r.Method : "HTTP");

string PathOf(string uri)
{
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
        return GraphQlDocument.Operation(q.GetString()!, name);
    }
    catch (JsonException)
    {
        return null;
    }
}

static string OpId(string from, string to, string protocol, string method, string target, string? graphQl) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001f', from, to, protocol, method, graphQl is null ? target : target + " " + graphQl))))[..16].ToLowerInvariant();

// The status as the card writes it: the code's digits, else the label, else "none" for a call with no response.
static string StatusOf(Call? response) =>
    response is null ? "none" : response.StatusCode ?? response.StatusText ?? "none";

// InteractionStatus.IsError at 8d7d1e1d: a code of 400 or more, or a label that is no success.
static bool IsError(Call? response)
{
    if (response is null) return false;
    if (int.TryParse(response.StatusCode, out var code)) return code >= 400;
    var text = response.StatusText;
    return text is { Length: > 0 } && !text.StartsWith("OK", StringComparison.OrdinalIgnoreCase)
           && !new[] { "Created", "Accepted", "NoContent", "Sent", "Ack", "Responded", "Hit", "Miss", "Committed" }.Contains(text, StringComparer.OrdinalIgnoreCase);
}

var ops = new SortedDictionary<string, Op>(StringComparer.Ordinal);
var opOf = new Dictionary<Call, string>(ReferenceEqualityComparer.Instance);
var behaviourScenarioOps = new SortedDictionary<int, SortedSet<string>>();
var runScenarioOps = new SortedDictionary<int, SortedSet<string>>();
var partyCategories = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
var partyHosts = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
var partyDoubleCalls = new Dictionary<string, int>(StringComparer.Ordinal);
var derivedPhases = 0;
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
    if (r.Scenario >= 0)
        op.FirstAddress ??= $"s{r.Scenario}/i{r.Index}";
    if (IsDelivery(r)) op.Delivery = true;
    if (correctedBrokerRule && IsMessageCall(r)) op.MessageCall = true;
    if (r.MetaType == "Event") op.Event = true;
    op.Uris.Add(r.Uri);
    responses.TryGetValue((r.Scenario, r.Rrid), out var response);
    var status = StatusOf(response);
    if (IsError(response)) op.Errors++;
    var origin = Origin(r);
    var cause = Cause(r);
    var phase = EffectivePhase(r);
    if (r.Phase is not ("Setup" or "Action") && phase is "setup" or "action")
    {
        derivedPhases++;
        op.DerivedPhases++;
    }
    var scenarioRow = r.Scenario >= 0 && !scenarios[r.Scenario].NotATest;
    if (scenarioRow && cause != "background")
    {
        if (!runScenarioOps.TryGetValue(r.Scenario, out var all)) runScenarioOps[r.Scenario] = all = new SortedSet<string>(StringComparer.Ordinal);
        all.Add(id);
        var own = hasTestParty ? origin == "test" : parentOf.GetValueOrDefault(r) is null;
        if (own)
        {
            if (!behaviourScenarioOps.TryGetValue(r.Scenario, out var mine)) behaviourScenarioOps[r.Scenario] = mine = new SortedSet<string>(StringComparer.Ordinal);
            mine.Add(id);
        }
    }
    Bump(op.Statuses, status);
    Bump(op.Origins, origin);
    Bump(op.Causes, cause);
    Bump(op.Phases, phase);
    if (!partyCategories.TryGetValue(r.Service, out var sc)) partyCategories[r.Service] = sc = new SortedSet<string>(StringComparer.Ordinal);
    if (!partyCategories.TryGetValue(r.Caller, out var cc)) partyCategories[r.Caller] = cc = new SortedSet<string>(StringComparer.Ordinal);
    if (r.Category is not null) (correctedBrokerRule && IsDelivery(r) ? cc : sc).Add(r.Category);
    if (r.CallerCategory is not null) cc.Add(r.CallerCategory);
    if (!partyHosts.TryGetValue(r.Service, out var hosts)) partyHosts[r.Service] = hosts = new SortedSet<string>(StringComparer.Ordinal);
    if (MessageTable.HostOf(r.Uri) is { } host) hosts.Add(host);
    if (!partyHosts.ContainsKey(r.Caller)) partyHosts[r.Caller] = new SortedSet<string>(StringComparer.Ordinal);
    if (r.TestDouble is not null) partyDoubleCalls[r.Service] = partyDoubleCalls.GetValueOrDefault(r.Service) + 1;
}

JsonArray Arr(IEnumerable<string> values) => new(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());

JsonObject Behaviour(bool withScenarios)
{
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
    var messageRows = new SortedSet<(string Party, string Topic, string Direction, string Op)>();
    foreach (var op in ops.Values)
        foreach (var m in MessageTable.Messages(op, subject, correctedBrokerRule))
            messageRows.Add((m.Party, m.Topic, m.Direction, op.Id));
    var messages = new JsonArray();
    foreach (var m in messageRows)
        messages.Add(new JsonObject { ["op"] = m.Op, ["direction"] = m.Direction, ["party"] = m.Party, ["topic"] = m.Topic });

    var card = new JsonObject
    {
        ["rules"] = new JsonObject { ["operationKey"] = 8, ["templateRules"] = null, ["inference"] = 1 },
        ["subject"] = new JsonObject { ["name"] = subject, ["id"] = null, ["source"] = subject is null ? null : partyRows[subject].TryGetProperty("kindSource", out var s) ? s.GetString() : null },
        ["parties"] = parties,
        ["edges"] = edges,
        ["exposes"] = new JsonObject { ["basis"] = "tested", ["ops"] = Arr(exposes) },
        ["messages"] = messages
    };
    if (withScenarios)
    {
        var rows = new JsonArray();
        foreach (var bySid in scenarios.Where(s => !s.NotATest).GroupBy(s => s.Sid).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var first = bySid.First();
            var own = bySid.SelectMany(s => behaviourScenarioOps.TryGetValue(s.Ordinal, out var m) ? m : Enumerable.Empty<string>())
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
            rows.Add(new JsonObject
            {
                ["sid"] = bySid.Key, ["feature"] = first.Feature, ["scenario"] = first.Name, ["tags"] = Arr(first.Tags),
                ["source"] = first.SourceFile is null ? null : new JsonObject { ["file"] = first.SourceFile, ["line"] = first.SourceLine is null ? null : int.Parse(first.SourceLine) },
                ["ops"] = Arr(own)
            });
        }
        card["scenarios"] = rows;
    }
    card["unresolved"] = Arr(partyCategories.Keys.Where(n => !(partyRows.TryGetValue(n, out var row) && row.TryGetProperty("id", out var pid) && pid.ValueKind == JsonValueKind.String)));
    card["capture"] = new JsonObject
    {
        ["calls"] = "recorded",
        ["subjectCalls"] = requests.Any(r => r.Caller == subject) ? "recorded" : "notRecorded",
        ["emptyTargets"] = Arr(ops.Values.Where(o => o.Uris.Any(MessageTable.DestinationMissing)).Select(o => o.Id).Order(StringComparer.Ordinal))
    };
    return card;
}

JsonObject RunPart(string behaviourHash)
{
    string? runId = null, runAt = null;
    if (runJsonPath != "-" && File.Exists(runJsonPath))
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(runJsonPath));
        runId = Text(manifest.RootElement, "run");
        runAt = Text(manifest.RootElement, "at");
    }
    var ci = root.TryGetProperty("ciMetadata", out var c) && c.ValueKind == JsonValueKind.Object ? c : default;
    string? Ci(string name) => ci.ValueKind == JsonValueKind.Object ? Text(ci, name) : null;
    var env = root.TryGetProperty("environment", out var e) && e.ValueKind == JsonValueKind.Object
        ? new JsonObject { ["os"] = Text(e, "os"), ["runtime"] = Text(e, "runtime") }
        : null;
    var tests = scenarios.Where(s => !s.NotATest).ToList();
    var sha = reportPath != "-" && File.Exists(reportPath) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(reportPath))).ToLowerInvariant() : null;
    var degradedKinds = new[] { "CaptureDegraded", "DroppedUnattributed", "DroppedOutsideRunWindow", "UnattributedInteractions", "MalformedLine", "BackgroundCalls", "StepAttributionMismatch" };
    var degraded = new SortedDictionary<string, int>(StringComparer.Ordinal);
    if (root.TryGetProperty("diagnostics", out var diags) && diags.ValueKind == JsonValueKind.Array)
        foreach (var d in diags.EnumerateArray())
            if (Text(d, "kind") is { } kind && degradedKinds.Contains(kind))
                degraded[kind] = degraded.GetValueOrDefault(kind) + 1;
    return new JsonObject
    {
        ["id"] = runId,
        ["at"] = runAt,
        ["repository"] = Ci("repository"), ["commit"] = Ci("commitSha"), ["branch"] = Ci("branch"),
        ["suite"] = Text(root, "suite"), ["framework"] = null, ["kronikolVersion"] = "5.N.0",
        ["report"] = new JsonObject
        {
            ["file"] = reportPath == "-" ? null : Path.GetFileName(reportPath),
            ["formatVersion"] = Text(root, "formatVersion") is { } fv ? int.Parse(fv) : null,
            ["kronikolVersion"] = Text(root, "kronikolVersion"),
            ["sha256"] = sha
        },
        ["environment"] = env,
        ["results"] = new JsonObject
        {
            ["scenarios"] = tests.Count, ["passed"] = tests.Count(x => x.Result == "Passed"), ["failed"] = tests.Count(x => x.Result == "Failed"),
            ["skipped"] = tests.Count(x => x.Result is "Skipped" or "SkippedAfterFailure"), ["bypassed"] = tests.Count(x => x.Result == "Bypassed")
        },
        ["behaviourHash"] = behaviourHash,
        ["parties"] = new JsonArray(partyHosts.Select(p => (JsonNode?)new JsonObject
        {
            ["name"] = p.Key, ["hosts"] = Arr(p.Value), ["doubleCalls"] = partyDoubleCalls.GetValueOrDefault(p.Key)
        }).ToArray()),
        ["counts"] = new JsonArray(ops.Values.Select(o => (JsonNode?)new JsonObject
        {
            ["op"] = o.Id, ["calls"] = o.Calls, ["errors"] = o.Errors,
            ["statuses"] = new JsonObject(o.Statuses.Select(x => KeyValuePair.Create(x.Key, (JsonNode?)x.Value))),
            ["phases"] = new JsonObject { ["setup"] = o.Phases.GetValueOrDefault("setup"), ["action"] = o.Phases.GetValueOrDefault("action"), ["unknown"] = o.Phases.GetValueOrDefault("unknown") },
            ["derivedPhases"] = o.DerivedPhases
        }).ToArray()),
        ["examples"] = new JsonArray(ops.Values.Select(o => (JsonNode?)new JsonObject { ["op"] = o.Id, ["address"] = o.FirstAddress }).ToArray()),
        ["scenarios"] = new JsonArray(scenarios.Where(s => !s.NotATest).GroupBy(s => s.Sid).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => (JsonNode?)new JsonObject
        {
            ["sid"] = g.Key,
            ["ops"] = Arr(g.SelectMany(s => runScenarioOps.TryGetValue(s.Ordinal, out var m) ? m : Enumerable.Empty<string>()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        }).ToArray()),
        ["degraded"] = new JsonObject(degraded.Select(x => KeyValuePair.Create(x.Key, (JsonNode?)x.Value)))
    };
}

var compact = new JsonSerializerOptions { Encoder = JavaScriptEncoder.Default };
var indented = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.Default };
string Compact(JsonNode n) => n.ToJsonString(compact);
string Indented(JsonNode n) => n.ToJsonString(indented).Replace("\r\n", "\n");
int Bytes(string s) => Encoding.UTF8.GetByteCount(s);

var behaviour = Behaviour(withScenarios: true);
var behaviourText = Compact(behaviour);
var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(behaviourText))).ToLowerInvariant();
var runPart = RunPart(hash);
var whole = new JsonObject { ["formatVersion"] = 1, ["run"] = runPart, ["behaviour"] = behaviour };
var compactFile = Compact(whole) + "\n";
var indentedFile = Indented(whole) + "\n";
File.WriteAllText(prefix + ".service.json", compactFile, new UTF8Encoding(false));
File.WriteAllText(prefix + ".service.indented.json", indentedFile, new UTF8Encoding(false));

// The hashed bytes are the behaviour value as the compact file holds it: behaviour is the last member, so they run from
// after "behaviour": to the byte before the file's closing brace and its newline.
var key = "\"behaviour\":";
var start = compactFile.LastIndexOf(key, StringComparison.Ordinal) + key.Length;
var inFile = compactFile[start..^2];
var hashFromFile = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(inFile))).ToLowerInvariant();

var noIndex = Behaviour(withScenarios: false);
int Part(JsonObject o, string name) => o[name] is { } n ? Bytes(Compact(n)) : 0;

Console.WriteLine($"subject {subject}; scenarios {scenarios.Count(s => !s.NotATest)} (sids {scenarios.Where(s => !s.NotATest).Select(s => s.Sid).Distinct().Count()}); requests {requests.Count}; background-section requests {backgroundSectionRequests}");
Console.WriteLine($"parties {partyCategories.Count}; edges {ops.Values.Select(o => (o.From, o.To, o.Protocol)).Distinct().Count()}; ops {ops.Count}; exposes {((JsonArray)behaviour["exposes"]!["ops"]!).Count}; messages {((JsonArray)behaviour["messages"]!).Count}; emptyTargets {((JsonArray)behaviour["capture"]!["emptyTargets"]!).Count}; derived phases {derivedPhases}");
Console.WriteLine($"behaviour index refs {behaviourScenarioOps.Values.Sum(s => s.Count)}; run index refs {runScenarioOps.Values.Sum(s => s.Count)}; ops with errors {ops.Values.Count(o => o.Errors > 0)}; hosts {partyHosts.Values.Sum(h => h.Count)}; degraded {Compact(runPart["degraded"]!)}");
Console.WriteLine($"hash from the file's bytes equals behaviourHash: {hashFromFile == hash}");
Console.WriteLine($"sizes (bytes, UTF-8): file compact {Bytes(compactFile)} indented {Bytes(indentedFile)}; behaviour compact {Bytes(behaviourText)} indented {Bytes(Indented(behaviour))}; "
                  + $"behaviour without its index compact {Bytes(Compact(noIndex))} indented {Bytes(Indented(noIndex))}; run compact {Bytes(Compact(runPart))}");
Console.WriteLine($"parts (compact): run.counts {Part(runPart, "counts")}, run.examples {Part(runPart, "examples")}, run.scenarios {Part(runPart, "scenarios")}, run.parties {Part(runPart, "parties")}; "
                  + $"behaviour.scenarios {Part(behaviour, "scenarios")}, behaviour.edges {Part(behaviour, "edges")}, behaviour.parties {Part(behaviour, "parties")}, behaviour.messages {Part(behaviour, "messages")}");
if (correctedBrokerRule)
{
    var byBroker = ((JsonArray)behaviour["messages"]!).GroupBy(m => (string)m!["party"]!).OrderBy(g => g.Key, StringComparer.Ordinal)
        .Select(g => $"{g.Key} {g.Count()}");
    Console.WriteLine($"brokers ({brokers.Count}): {string.Join(", ", brokers)}; messages rows by broker: {string.Join(", ", byBroker)}");
}

static void Bump(SortedDictionary<string, int> map, string key) => map[key] = map.GetValueOrDefault(key) + 1;
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

sealed record Scenario(int Ordinal, string Sid, string Feature, string Name, List<string> Tags, string? SourceFile, string? SourceLine, string? Result, bool NotATest);

sealed record Call(int Scenario, int Index, string Type, string Method, string Uri, string Service, string Caller, string? Category,
    string? CallerCategory, string? Phase, string? Rrid, string? TraceId, string? StatusCode, string? StatusText, string? Content, string? MetaType,
    string? Attribution, string? TestDouble);

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
    public int Errors { get; set; }
    public int DerivedPhases { get; set; }
    public string? FirstAddress { get; set; }
    public SortedDictionary<string, int> Statuses { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> Origins { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> Causes { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> Phases { get; } = new(StringComparer.Ordinal);
    public SortedSet<string> Uris { get; } = new(StringComparer.Ordinal);
    public bool Delivery { get; set; }
    public bool Event { get; set; }
    public bool MessageCall { get; set; }
}

// The revised plan's 4.2 table, as far as a probe needs it: per URI scheme, where the destination sits and which
// operations publish or receive; any other message (category MessageQueue or meta type Event) by the arrow.
static class MessageTable
{
    private static readonly Dictionary<string, (string Where, string[] Publish, string[] Receive)> Schemes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["kafka"] = ("path", ["Produce"], ["Consume"]),
        ["eventhubs"] = ("path", ["Send"], ["Read", "ReadEvents", "ProcessEvent"]),
        ["servicebus"] = ("authority", ["Send", "Schedule"], ["Receive", "Peek"]),
        ["pubsub"] = ("path", ["Publish"], ["Pull", "Receive"]),
        ["sqs"] = ("path", ["SendMessage", "SendMessageBatch"], ["ReceiveMessage"]),
        ["sns"] = ("path", ["Publish", "PublishBatch"], []),
        ["storagequeue"] = ("path", ["Send"], ["Receive", "Peek"]),
        ["masstransit"] = ("path", ["→"], ["←"]),
        ["eventbridge"] = ("authority", ["PutEvents"], []),
    };

    private static (string Scheme, string Authority, string[] Segments) Split(string uri)
    {
        var at = uri.IndexOf("://", StringComparison.Ordinal);
        if (at < 0) return ("", "", []);
        var scheme = uri[..at];
        var rest = uri[(at + 3)..];
        var cut = rest.IndexOfAny(['?', '#']);
        if (cut >= 0) rest = rest[..cut];
        var slash = rest.IndexOf('/');
        var authority = slash < 0 ? rest : rest[..slash];
        var path = slash < 0 ? "" : rest[slash..];
        return (scheme, authority, path.Split('/', StringSplitOptions.RemoveEmptyEntries));
    }

    public static string? HostOf(string uri)
    {
        var (scheme, authority, _) = Split(uri);
        if (authority.Length == 0) return null;
        return Schemes.TryGetValue(scheme, out var s) && s.Where == "authority" ? null : authority;
    }

    private static string? Destination(string uri)
    {
        var (scheme, authority, segments) = Split(uri);
        if (Schemes.TryGetValue(scheme, out var s))
            return s.Where == "authority" ? (authority.Length > 0 ? authority : null) : segments.FirstOrDefault() is { } first && first != "unknown" ? first : null;
        return segments.LastOrDefault() ?? (authority.Length > 0 ? authority : null);
    }

    public static bool DestinationMissing(string uri)
    {
        var (scheme, _, _) = Split(uri);
        return Schemes.ContainsKey(scheme) && Destination(uri) is null;
    }

    public static bool InTable(string uri) => Schemes.ContainsKey(Split(uri).Scheme);

    // corrected false: the revision's reading (2026-10-27); true: the verification pass's (2026-10-28).
    public static IEnumerable<(string Direction, string Party, string Topic)> Messages(Op op, string? subject, bool corrected = false)
    {
        var isMessage = corrected
            ? op.MessageCall
            : op.Protocol is "MessageQueue" || op.Event || op.Delivery;
        if (!isMessage || subject is null || (op.From != subject && op.To != subject))
            yield break;
        foreach (var uri in op.Uris)
        {
            var (scheme, _, _) = Split(uri);
            var topic = Destination(uri) ?? "";
            string? direction;
            if (op.To == subject)
                direction = "consumes";
            else if (Schemes.TryGetValue(scheme, out var s))
            {
                var word = op.Method.Split(' ')[0];
                direction = s.Publish.Contains(word) ? "publishes" : s.Receive.Contains(word) ? "consumes" : null;
            }
            else
                direction = "publishes";
            if (direction is not null)
                yield return (direction, op.To == subject ? op.From : op.To, topic);
        }
    }
}

static class GraphQlDocument
{
    // Audit B's light reader: the operation named by operationName or the document's first operation, typed; an
    // anonymous one is named by its root fields, sorted and de-duplicated, aliases and arguments dropped.
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
            if (c == ':') { token.Clear(); continue; }
            Flush();
        }
        Flush();
        return fields.Count == 0 ? null : $"{type} {{ {string.Join(" ", fields)} }}";

        void Flush()
        {
            if (depth == 1 && token.Length > 0)
            {
                var t = token.ToString();
                if (t != "on") fields.Add(t);
            }
            token.Clear();
        }
    }
}
