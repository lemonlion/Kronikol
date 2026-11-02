// What the warm-up rule marks on a real report as recorded, and with its statement-shaped requests read as Default, which
// is what #220's change would do to it. Published 5.27.13; WarmUpCalls.Find is internal, so it is reached by reflection.
// args: <mergeable TestRunReport.json>
#:package Kronikol@5.27.13
#:property PublishAot=false

using System.Reflection;
using Kronikol.Constants;
using Kronikol.History;
using Kronikol.Reports;
using Kronikol.Reports.Merge;
using Kronikol.Tracking;

var report = MergeableReportReader.ReadFile(args[0]);
var find = typeof(ReportGenerator).Assembly.GetType("Kronikol.Reports.WarmUpCalls")!
    .GetMethod("Find", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;

// The result and its marks are internal, so they are read by reflection and a mark is printed as the record it is.
List<string> Marks(IReadOnlyList<RequestResponseLog?> logs)
{
    var result = find.Invoke(null, [report.Features, logs, null])!;
    var calls = (System.Collections.IDictionary)result.GetType().GetProperty("Calls", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(result)!;
    var byId = logs.Where(l => l is { Type: RequestResponseType.Request }).ToLookup(l => l!.RequestResponseId);
    var marks = new List<string>();
    foreach (System.Collections.DictionaryEntry pair in calls)
    {
        var request = byId[(Guid)pair.Key].First()!;
        marks.Add($"{request.ServiceName,-20} {pair.Value}");
    }
    marks.Sort(StringComparer.Ordinal);
    return marks;
}

var asRecorded = report.Interactions;
var asDefault = report.Interactions
    .Select(l => l.MetaType == RequestResponseMetaType.Event && DependencyCategories.IsStatementShaped(l.DependencyCategory)
        ? l with { MetaType = RequestResponseMetaType.Default } : l)
    .ToArray();

Console.WriteLine($"interactions {asRecorded.Length}, statement-shaped events {asRecorded.Count(l => l.MetaType == RequestResponseMetaType.Event && DependencyCategories.IsStatementShaped(l.DependencyCategory))}");
var before = Marks(asRecorded).ToList();
var after = Marks(asDefault).ToList();
Console.WriteLine($"marks as recorded {before.Count}, with those read as Default {after.Count}");
foreach (var mark in after.Except(before))
    Console.WriteLine($"NEW   {mark}");
foreach (var mark in before.Except(after))
    Console.WriteLine($"GONE  {mark}");
