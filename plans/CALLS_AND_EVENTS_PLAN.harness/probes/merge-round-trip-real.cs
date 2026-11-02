// Real reports: what the merge's reader gives history, against what the run itself gave it. Two comparisons per report:
//  A. The file's own strings, keyed without the reader (the uri field is log.Uri.ToString() as the run read it, the method
//     field the run's label, and so on), against the same calls through MergeableReportReader. Needs no fragment.
//  B. Where a History.run.json sits beside the report: its roster, results, attempts, counts, fingerprints and call lines
//     against the reader's, under the rule the fragment was made by (a rule from 6 on is kept, so the reader's calls can be
//     fingerprinted under it). A report that is not mergeable is read as one by adding the key, since its calls are the same.
// Prints counts and generic labels only; pass a path as MRT_REAL_DUMP to keep the differing lines for a reader of this machine.
// args: <report dir or file>...   Published 5.27.13; for the prototype, the same build with the prototype's Kronikol.dll beside it.
#:package Kronikol@5.27.13
#:property PublishAot=false

using System.Reflection;
using System.Text;
using System.Text.Json;
using Kronikol.History;
using Kronikol.Reports;
using Kronikol.Reports.Merge;
using Kronikol.Tracking;

const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
var core = typeof(ReportGenerator).Assembly;
var fileFormat = core.GetType("Kronikol.Compression.ReportFileFormat", true)!;
var openFile = fileFormat.GetMethods(Any).First(m => m.Name == "Open");
var inflate = core.GetType("Kronikol.Reports.ReportPayloads", true)!.GetMethod("Inflate", Any)!;
var callOperation = core.GetType("Kronikol.History.CallOperation", true)!;
var untemplated = callOperation.GetMethod("Untemplated", Any)!;
var keyOf = callOperation.GetMethods(Any).First(m => m.Name == "Key");
var underRule = typeof(InteractionShape).GetMethods(Any).First(m => m.Name == "Calls" && m.GetParameters().Length == 3 && m.GetParameters()[2].ParameterType == typeof(int) && m.GetParameters()[0].ParameterType != typeof(IReadOnlyList<>).MakeGenericType(core.GetType("Kronikol.History.HistoryRawCall", true)!));

var dump = Environment.GetEnvironmentVariable("MRT_REAL_DUMP") is { Length: > 0 } dumpPath ? new StreamWriter(dumpPath, append: true, new UTF8Encoding(false)) : null;
var q = ((char)34).ToString();
Console.WriteLine($"reader: {core.GetName().Version}, {Path.GetDirectoryName(core.Location)}");
Console.WriteLine("lane | written by | scenarios | calls | A: scenarios whose lines differ from the file's strings (method / target / status / other) | B: rule, positions compared, set same, ordered same, count same, roster same, result same, attempt same, deps only in fragment / only in reader | dup runtime ids, copies dropped by merge");

var totals = new Dictionary<string, int>();
void Add(string key, int n = 1) => totals[key] = totals.GetValueOrDefault(key) + n;

foreach (var input in args)
{
    var path = File.Exists(input) ? input : new[] { "TestRunReport.krkl", "TestRunReport.json" }.Select(n => Path.Combine(input, n)).First(File.Exists);
    var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
    var lane = Path.GetFileName(directory);
    string text;
    using (var file = File.OpenRead(path))
    {
        var content = openFile.Invoke(null, [file, openFile.GetParameters()[1].DefaultValue])!;
        var stream = (Stream)content.GetType().GetProperty("Stream", Any)!.GetValue(content)!;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
        text = reader.ReadToEnd();
    }
    var mergeable = text.Contains(q + "mergeableFormatVersion" + q, StringComparison.Ordinal) || text.Contains(q + "componentRelationships" + q, StringComparison.Ordinal);
    if (!mergeable)
    {
        var at = text.IndexOf('{');
        text = text[..(at + 1)] + q + "mergeableFormatVersion" + q + ":1," + text[(at + 1)..];
    }

    var report = MergeableReportReader.Parse(text);
    var merged = MergeableReportMerger.Merge([report]);
    var rules = HistoryShapeRules.Create(merged.ShapeTemplates);
    var built = HistoryRunBuilder.Build(merged.Features, merged.Interactions, merged.Suite, merged.CiMetadata, new DateTimeOffset(merged.EndTime.ToUniversalTime()),
        new HistoryBuildOptions { ServiceId = merged.ServiceId, ShapeRules = rules });

    // A: the file's strings, keyed without the reader.
    using var document = JsonDocument.Parse(text);
    var fromStrings = new Dictionary<string, List<ShapeCall>>(StringComparer.Ordinal);
    var runtimeIdCounts = new Dictionary<string, int>(StringComparer.Ordinal);
    var interactionsTotal = 0;
    var labels = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (var feature in document.RootElement.GetProperty("features").EnumerateArray())
        foreach (var scenario in feature.GetProperty("scenarios").EnumerateArray())
        {
            var id = scenario.GetProperty("id").GetString() ?? "";
            runtimeIdCounts[id] = runtimeIdCounts.GetValueOrDefault(id) + 1;
            if (!scenario.TryGetProperty("httpInteractions", out var interactions) || interactions.ValueKind != JsonValueKind.Array)
                continue;
            if (fromStrings.ContainsKey(id))
                continue;
            var responses = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            var requests = new List<JsonElement>();
            foreach (var element in interactions.EnumerateArray())
            {
                interactionsTotal++;
                if (Str(element, "type") == "Response")
                    responses.TryAdd(Str(element, "requestResponseId") ?? "", element);
                else
                    requests.Add(element);
            }
            var calls = new List<ShapeCall>();
            foreach (var request in requests)
            {
                var method = Str(request, "method") ?? "";
                labels[method] = labels.GetValueOrDefault(method) + 1;
                var uri = Str(request, "uri") ?? "";
                var raw = untemplated.Invoke(null, [method, uri, Str(request, "dependencyCategory"), Body(request), true])!;
                var key = (string)keyOf.Invoke(null, [raw, rules])!;
                string? status = null;
                if (responses.TryGetValue(Str(request, "requestResponseId") ?? "", out var response))
                {
                    if (response.TryGetProperty("statusCode", out var code) && code.ValueKind == JsonValueKind.Number)
                        status = code.GetRawText();
                    else
                        status = Str(response, "statusText");
                }
                calls.Add(new ShapeCall(Str(request, "callerName") ?? "", Str(request, "serviceName") ?? "", method, key, status));
            }
            fromStrings[id] = calls;
        }

    var byTest = merged.Interactions.GroupBy(l => l.TestId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
    var aDiffer = 0;
    int aMethod = 0, aTarget = 0, aStatus = 0, aOther = 0;
    foreach (var (id, expected) in fromStrings)
    {
        var read = InteractionShape.Calls(byTest.TryGetValue(id, out var logs) ? logs : [], rules);
        if (expected.Select(c => c.ToString()).SequenceEqual(read.Select(c => c.ToString()), StringComparer.Ordinal))
            continue;
        aDiffer++;
        if (expected.Count != read.Count)
        {
            aOther++;
            dump?.WriteLine($"{lane} A count {expected.Count} -> {read.Count} in {id}");
            continue;
        }
        for (var i = 0; i < expected.Count; i++)
        {
            var (e, r) = (expected[i], read[i]);
            if (e.ToString() == r.ToString()) continue;
            if (e.Method != r.Method) aMethod++;
            else if (e.Uri != r.Uri) aTarget++;
            else if (e.Status != r.Status) aStatus++;
            else aOther++;
            dump?.WriteLine($"{lane} A file: {e}");
            dump?.WriteLine($"{lane} A read: {r}");
        }
    }

    // B: the run's own fragment.
    var fragmentPath = Path.Combine(directory, HistoryFormat.FragmentFileName);
    var part = "no fragment";
    if (File.Exists(fragmentPath))
    {
        var fragment = HistoryFragment.Parse(File.ReadAllText(fragmentPath));
        var (ownRoster, ownRun, ownShapes) = (fragment.Roster, fragment.Run, fragment.Shapes);
        var version = ownRun.ShapeVersion ?? 0;
        var ownAt = new Dictionary<(string, int), int>();
        for (var i = 0; i < ownRoster.Count; i++) ownAt[(ownRoster.Ids[i], ownRoster.Slots[i])] = i;
        var scenarios = merged.Features.OrderBy(f => f.DisplayName).SelectMany(f => f.Scenarios ?? []).ToArray();
        int compared = 0, setSame = 0, orderedSame = 0, countSame = 0, resultSame = 0, attemptSame = 0, lineCasing = 0, lineOther = 0, rosterOnlyOwn = 0, rosterOnlyRead = 0;
        var seen = new HashSet<(string, int)>();
        for (var m = 0; m < built.Roster.Count; m++)
        {
            var key = (built.Roster.Ids[m], built.Roster.Slots[m]);
            seen.Add(key);
            if (!ownAt.TryGetValue(key, out var o))
            {
                rosterOnlyRead++;
                dump?.WriteLine($"{lane} B only in the reader's roster: {built.Roster.Names[m]}");
                continue;
            }
            compared++;
            if (ownRun.ResultAt(o) == built.Run.ResultAt(m)) resultSame++;
            else dump?.WriteLine($"{lane} B result {ownRun.ResultAt(o)} -> {built.Run.ResultAt(m)} {built.Roster.Names[m]}");
            if (ownRun.AttemptAt(o) == built.Run.AttemptAt(m)) attemptSame++;
            var scenario = scenarios[m];
            var calls = (IReadOnlyList<ShapeCall>)underRule.Invoke(null, [byTest.TryGetValue(scenario.Id, out var logs) ? logs : Array.Empty<RequestResponseLog>(), rules, version])!;
            var (set, ordered, count) = InteractionShape.Fingerprint(calls);
            if (set == ownRun.ShapeSetAt(o)) setSame++;
            if (ordered == ownRun.ShapeOrderedAt(o)) orderedSame++;
            if (count == ownRun.CallsAt(o)) countSame++;
            else dump?.WriteLine($"{lane} B count {ownRun.CallsAt(o)} -> {count} {built.Roster.Names[m]}");
            if (set != ownRun.ShapeSetAt(o) && ownRun.CallSetAt(o) is { } indices && ownShapes is not null)
            {
                var own = indices.Select(ownShapes.At).OfType<string>().ToHashSet(StringComparer.Ordinal);
                var mine = calls.Select(c => c.ToString()).ToHashSet(StringComparer.Ordinal);
                var ownOnly = own.Except(mine).ToList();
                var mineOnly = mine.Except(own).ToList();
                foreach (var line in ownOnly)
                {
                    if (mineOnly.Contains(line, StringComparer.OrdinalIgnoreCase)) lineCasing++;
                    else
                    {
                        lineOther++;
                        dump?.WriteLine($"{lane} B own : {line}");
                    }
                }
                foreach (var line in mineOnly.Where(l => !ownOnly.Contains(l, StringComparer.OrdinalIgnoreCase)))
                    dump?.WriteLine($"{lane} B read: {line}");
            }
        }
        rosterOnlyOwn = ownAt.Keys.Count(k => !seen.Contains(k));
        foreach (var key in ownAt.Keys.Where(k => !seen.Contains(k)))
            dump?.WriteLine($"{lane} B only in the fragment's roster: {ownRoster.Names[ownAt[key]]}");
        var ownDeps = new HashSet<string>(ownRun.Deps ?? [], StringComparer.Ordinal);
        var readDeps = new HashSet<string>(built.Run.Deps ?? [], StringComparer.Ordinal);
        part = $"rule {version}: {compared}/{ownRoster.Count} positions, set {setSame}, ordered {orderedSame}, count {countSame}, roster only-fragment {rosterOnlyOwn} only-reader {rosterOnlyRead}, result {resultSame}, attempt {attemptSame}, lines casing {lineCasing} other {lineOther}, deps {ownDeps.Except(readDeps).Count()}/{readDeps.Except(ownDeps).Count()}";
        Add("B positions", compared);
        Add("B set same", setSame);
        Add("B ordered same", orderedSame);
        Add("B count same", countSame);
        Add("B result same", resultSame);
        Add("B lines differing by casing", lineCasing);
        Add("B lines differing otherwise", lineOther);
        Add("B roster only in fragment", rosterOnlyOwn);
        Add("B roster only in reader", rosterOnlyRead);
    }

    var repeatedIds = runtimeIdCounts.Count(p => p.Value > 1);
    var dropped = report.Features.Sum(f => f.Scenarios?.Length ?? 0) - merged.Features.Sum(f => f.Scenarios?.Length ?? 0);
    var verbLike = labels.Where(p => IsVerbInAnotherCase(p.Key)).Sum(p => p.Value);
    Console.WriteLine($"{lane} | {report.KronikolVersion.Split('+')[0]}{(mergeable ? "" : " (not mergeable, key added)")} | {runtimeIdCounts.Values.Sum()} | {interactionsTotal} | A: {aDiffer} ({aMethod} / {aTarget} / {aStatus} / {aOther}), verb-cased labels {verbLike} | B: {part} | dup ids {repeatedIds}, copies dropped {dropped}");
    Add("scenarios", runtimeIdCounts.Values.Sum());
    Add("interactions", interactionsTotal);
    Add("A scenarios differing", aDiffer);
    Add("A method", aMethod);
    Add("A target", aTarget);
    Add("A status", aStatus);
    Add("A other", aOther);
    Add("verb-cased label calls", verbLike);
    Add("duplicate runtime ids", repeatedIds);
    Add("copies dropped by merge", dropped);
}
dump?.Dispose();
Console.WriteLine("totals: " + string.Join(", ", totals.Select(p => $"{p.Key} {p.Value}")));
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

static bool IsVerbInAnotherCase(string label) =>
    label != label.ToUpperInvariant() && label.ToUpperInvariant() is "GET" or "POST" or "PUT" or "DELETE" or "PATCH" or "HEAD" or "OPTIONS" or "TRACE" or "CONNECT";
