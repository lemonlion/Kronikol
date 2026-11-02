// The warm-up rule over a real mergeable report as recorded, and with R2's kinds applied (plans/CALLS_AND_EVENTS_PLAN.md 5.4) by what
// each record's scheme and label say, since the report was written before R2. Published 5.27.13; WarmUpCalls.Find and MessageTable are
// internal, so they are reached by reflection. It prints only counts and each message row's scheme and label, never a path, a service's
// name or a shape: the report is a consumer's data.
//
// Views, each a reading of the same records:
//   recorded                 as written
//   R1                       statement-shaped requests read as Default (#220's three lines)
//   R1 + R2                  also R2's kinds by label: Publishes, Receives and a delivery are Event; Neither and a label the table
//                            cannot read under a scheme it knows (Other) are Default; a scheme outside the table keeps what it says
//   R1 + R2 + publishes      also every Event the table reads as Publishes read as Default: warm-up with Q5's (b) built on the table
//   R1 + R2 + every message  also every Event of a message category read as Default: what the rule would say with no test on kind at all
//
// args: <mergeable TestRunReport.json>
#:package Kronikol@5.27.13
#:property PublishAot=false

using System.Collections;
using System.Reflection;
using Kronikol.Constants;
using Kronikol.Reports;
using Kronikol.Reports.Merge;
using Kronikol.Tracking;

var core = typeof(ReportGenerator).Assembly;
var find = core.GetType("Kronikol.Reports.WarmUpCalls", throwOnError: true)!.GetMethod("Find", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
var table = core.GetType("Kronikol.CallGraph.MessageTable", throwOnError: true)!;
var hasScheme = table.GetMethod("HasScheme", BindingFlags.Static | BindingFlags.NonPublic)!;
var read = table.GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic)!;
string Verdict(RequestResponseLog log) => read.Invoke(null, [log.Uri.Scheme, log.Method.Value?.ToString()])!.ToString()!;
bool KnownScheme(RequestResponseLog log) => (bool)hasScheme.Invoke(null, [log.Uri.Scheme])!;

var report = MergeableReportReader.ReadFile(args[0]);
var scenarioIds = report.Features.SelectMany(f => f.Scenarios).Select(s => s.Id).ToHashSet();
var logs = report.Interactions.ToArray();
var requests = logs.Where(l => l is { Type: RequestResponseType.Request, TrackingIgnore: false, IsDiagramMarker: false } && scenarioIds.Contains(l.TestId)).ToArray();
var servicesOf = requests.GroupBy(r => r.TestId).ToDictionary(g => g.Key, g => g.Select(r => r.ServiceName).ToHashSet(StringComparer.Ordinal));
Console.WriteLine($"interactions {logs.Length}, requests in a scenario {requests.Length}");

bool IsDelivery(RequestResponseLog log) =>
    (log.Method.Value?.ToString() ?? "") is var label && (label.StartsWith("Consume", StringComparison.Ordinal) || label.StartsWith('←'));

RequestResponseMetaType R2(RequestResponseLog log)
{
    if (!KnownScheme(log))
        return log.MetaType;
    return Verdict(log) switch
    {
        "Publishes" or "Receives" => RequestResponseMetaType.Event,
        "Neither" => RequestResponseMetaType.Default,
        _ => IsDelivery(log) ? RequestResponseMetaType.Event : RequestResponseMetaType.Default,
    };
}

RequestResponseLog Read(RequestResponseLog log, RequestResponseMetaType kind) => kind == log.MetaType ? log : log with { MetaType = kind };
RequestResponseLog R1View(RequestResponseLog log) =>
    log.MetaType == RequestResponseMetaType.Event && DependencyCategories.IsStatementShaped(log.DependencyCategory) ? log with { MetaType = RequestResponseMetaType.Default } : log;
RequestResponseLog R2View(RequestResponseLog log) => Read(R1View(log), R2(R1View(log)));

Console.WriteLine();
Console.WriteLine("message rows: category, scheme, label, kind as recorded -> by R2's rule, requests, of which the test made (the caller is no service of its scenario), with a measured duration");
var durations = (IDictionary)typeof(ReportGenerator).GetMethod("ComputeInteractionDurations", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [logs])!;
foreach (var group in requests
    .Where(r => r.MetaType == RequestResponseMetaType.Event || KnownScheme(r) || r.DependencyCategory == DependencyCategories.MessageQueue)
    .Where(r => !DependencyCategories.IsStatementShaped(r.DependencyCategory))
    .GroupBy(r => (Category: r.DependencyCategory ?? "-", r.Uri.Scheme, Label: r.Method.Value?.ToString() ?? "-", Recorded: r.MetaType, After: R2(r)))
    .OrderBy(g => g.Key.Category, StringComparer.Ordinal).ThenBy(g => g.Key.Scheme, StringComparer.Ordinal).ThenBy(g => g.Key.Label, StringComparer.Ordinal))
{
    var byTest = group.Count(r => !servicesOf[r.TestId].Contains(r.CallerName));
    var timed = group.Count(r => durations.Contains(r.RequestResponseId));
    Console.WriteLine($"  {group.Key.Category,-13} {group.Key.Scheme,-10} {group.Key.Label,-24} {group.Key.Recorded,-7} -> {group.Key.After,-7} {group.Count(),4}  by the test {byTest,4}  timed {timed,4}");
}

var moved = logs.Count(l => R2(R1View(l)) != R1View(l).MetaType);
Console.WriteLine($"records whose kind R2's rule moves, R1 aside: {moved}");

// A mark, by its call's category and the first word of its label, so no path, name or statement is printed.
Dictionary<string, int> Marks(IReadOnlyList<RequestResponseLog> view)
{
    var result = find.Invoke(null, [report.Features, view.ToArray<RequestResponseLog?>(), null])!;
    var calls = (IDictionary)result.GetType().GetProperty("Calls", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(result)!;
    var byId = view.Where(l => l.Type == RequestResponseType.Request).ToLookup(l => l.RequestResponseId);
    var marks = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (DictionaryEntry pair in calls)
    {
        var request = byId[(Guid)pair.Key].First();
        var word = (request.Method.Value?.ToString() ?? "-").Split(' ')[0];
        var key = $"{request.DependencyCategory ?? "-"} {word}";
        marks[key] = marks.GetValueOrDefault(key) + 1;
    }
    return marks;
}

var views = new (string Name, IReadOnlyList<RequestResponseLog> Logs)[]
{
    ("recorded", logs),
    ("R1", logs.Select(R1View).ToArray()),
    ("R1 + R2", logs.Select(R2View).ToArray()),
    ("R1 + R2 + publishes", logs.Select(R2View).Select(l => l.MetaType == RequestResponseMetaType.Event && KnownScheme(l) && Verdict(l) == "Publishes" ? l with { MetaType = RequestResponseMetaType.Default } : l).ToArray()),
    ("R1 + R2 + every message", logs.Select(R2View).Select(l => l.MetaType == RequestResponseMetaType.Event && !DependencyCategories.IsStatementShaped(l.DependencyCategory) ? l with { MetaType = RequestResponseMetaType.Default } : l).ToArray()),
};

Console.WriteLine();
Dictionary<string, int>? previous = null;
foreach (var (name, view) in views)
{
    var marks = Marks(view);
    Console.WriteLine($"{name,-26} {marks.Values.Sum()} marks");
    if (previous is not null)
        foreach (var key in marks.Keys.Union(previous.Keys).Order(StringComparer.Ordinal))
            if (marks.GetValueOrDefault(key) != previous.GetValueOrDefault(key))
                Console.WriteLine($"    {key}: {previous.GetValueOrDefault(key)} -> {marks.GetValueOrDefault(key)}");
    previous = marks;
}
