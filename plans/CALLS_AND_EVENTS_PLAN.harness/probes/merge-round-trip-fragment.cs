// A real report with no fragment beside it (written by a Kronikol older than the rule, or copied without it): the fragment
// its own run would have written under the current rule, made from the file's own strings and not through the merge's
// reader, so that a ledger can hold it. The call lines come from the fields as written: the uri field is the run's
// log.Uri.ToString(), the method field its label, and so on. Everything else on the run line comes from the library's own
// build. args: <report file> <out dir> <copies>: writes <out dir>/run<N>/History.run.json, each with its own GitHub run id.
#:package Kronikol@5.27.13
#:property PublishAot=false

using System.Reflection;
using System.Text;
using System.Text.Json;
using Kronikol.History;
using Kronikol.Reports;
using Kronikol.Reports.Merge;

const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
var core = typeof(ReportGenerator).Assembly;
var callOperation = core.GetType("Kronikol.History.CallOperation", true)!;
var untemplated = callOperation.GetMethod("Untemplated", Any)!;
var keyOf = callOperation.GetMethods(Any).First(m => m.Name == "Key");
var inflate = core.GetType("Kronikol.Reports.ReportPayloads", true)!.GetMethod("Inflate", Any)!;
var (reportPath, outDir, copies) = (args[0], args[1], int.Parse(args[2]));

var text = File.ReadAllText(reportPath);
var report = MergeableReportReader.Parse(text);
var merged = MergeableReportMerger.Merge([report]);
var rules = HistoryShapeRules.Create(merged.ShapeTemplates);
var at = new DateTimeOffset(merged.EndTime.ToUniversalTime());
var built = HistoryRunBuilder.Build(merged.Features, merged.Interactions, merged.Suite, merged.CiMetadata, at, new HistoryBuildOptions { ServiceId = merged.ServiceId, ShapeRules = rules });

using var document = JsonDocument.Parse(text);
var byId = new Dictionary<string, List<ShapeCall>>(StringComparer.Ordinal);
foreach (var feature in document.RootElement.GetProperty("features").EnumerateArray())
    foreach (var scenario in feature.GetProperty("scenarios").EnumerateArray())
    {
        var id = scenario.GetProperty("id").GetString() ?? "";
        if (byId.ContainsKey(id) || !scenario.TryGetProperty("httpInteractions", out var interactions) || interactions.ValueKind != JsonValueKind.Array)
            continue;
        var responses = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var requests = new List<JsonElement>();
        foreach (var element in interactions.EnumerateArray())
        {
            if (Str(element, "type") == "Response") responses.TryAdd(Str(element, "requestResponseId") ?? "", element);
            else requests.Add(element);
        }
        var calls = new List<ShapeCall>();
        foreach (var request in requests)
        {
            var method = Str(request, "method") ?? "";
            var raw = untemplated.Invoke(null, [method, Str(request, "uri") ?? "", Str(request, "dependencyCategory"), Body(request), true])!;
            string? status = null;
            if (responses.TryGetValue(Str(request, "requestResponseId") ?? "", out var response))
                status = response.TryGetProperty("statusCode", out var code) && code.ValueKind == JsonValueKind.Number ? code.GetRawText() : Str(response, "statusText");
            calls.Add(new ShapeCall(Str(request, "callerName") ?? "", Str(request, "serviceName") ?? "", method, (string)keyOf.Invoke(null, [raw, rules])!, status));
        }
        byId[id] = calls;
    }

// The fingerprints from the strings, position by position, in the roster's order.
var scenarios = merged.Features.OrderBy(f => f.DisplayName).SelectMany(f => f.Scenarios ?? []).ToArray();
var set = new string[scenarios.Length];
var ordered = new string[scenarios.Length];
var counts = new int[scenarios.Length];
var lines = new string[scenarios.Length][];
for (var i = 0; i < scenarios.Length; i++)
{
    var calls = byId.TryGetValue(scenarios[i].Id, out var found) ? found : [];
    (set[i], ordered[i], counts[i]) = InteractionShape.Fingerprint(calls);
    lines[i] = calls.Select(c => c.ToString()).Distinct(StringComparer.Ordinal).ToArray();
}
var shapes = HistoryShapes.Create(lines.SelectMany(l => l));
var index = shapes.Calls.Select((line, n) => (line, n)).ToDictionary(p => p.line, p => p.n, StringComparer.Ordinal);
var callSets = lines.Select(l => (IReadOnlyList<int>)l.Select(line => index[line]).OrderBy(n => n).ToArray()).ToArray();

for (var copy = 1; copy <= copies; copy++)
{
    var run = built.Run with
    {
        Id = $"gh:{7000 + copy}:1",
        At = at.AddHours(copy - copies - 1),
        ShapeSet = set,
        ShapeOrdered = ordered,
        Calls = counts,
        CallSets = callSets,
        ShapesHash = shapes.Hash,
        Partial = false,
    };
    var folder = Path.Combine(outDir, "run" + copy);
    Directory.CreateDirectory(folder);
    File.WriteAllText(Path.Combine(folder, HistoryFormat.FragmentFileName), HistoryFragment.Write(built.Roster, run, "5.27.13", shapes));
}
Console.WriteLine($"{copies} fragments of {scenarios.Length} positions, {shapes.Count} distinct lines, suite {built.Run.Suite ?? "(none)"}, stream {built.Run.Stream}, the report's own id {built.Run.Id}");
return 0;

string? Str(JsonElement parent, string name) => parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

string? Body(JsonElement request)
{
    if (!request.TryGetProperty("content", out var content))
        return null;
    if (content.ValueKind == JsonValueKind.String)
        return content.GetString();
    return content.ValueKind == JsonValueKind.Object && content.TryGetProperty("$z", out var z) ? (string)inflate.Invoke(null, [z.GetString()!])! : null;
}
