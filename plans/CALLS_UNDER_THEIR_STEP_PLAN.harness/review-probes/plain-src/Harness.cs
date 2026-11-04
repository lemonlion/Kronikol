using System.Net;
using System.Reflection;
using Kronikol.Reports;
using Kronikol.Tracking;

[assembly: TrackSteps]

namespace Probe;

public static class H
{
    public static string Id => TestIdentityScope.Current?.Id;

    public static void Call(string path, string id = null)
    {
        id ??= Id;
        RequestResponseLogger.LogPair(id, id, "GET", new Uri("http://svc/" + path), "svc", "caller");
    }

    public static async Task Scenario(string title, Func<string, Task> body, int settleMs = 0, Action<string> extra = null)
    {
        var id = "t-" + Guid.NewGuid().ToString("N")[..8];
        RequestResponseLogger.Clear();
        Exception thrown = null;
        Task t;
        using (ExecutionContext.SuppressFlow())
        {
            t = Task.Run(async () =>
            {
                using (TestIdentityScope.Begin(id, id))
                {
                    try { await body(id); }
                    catch (Exception e) { thrown = e; }
                }
            });
        }
        await t;
        if (settleMs > 0) await Task.Delay(settleMs);
        Print(title, id, thrown, StepCollector.GetSteps(id));
        extra?.Invoke(id);
    }

    public static void Print(string title, string id, Exception thrown, ScenarioStep[] steps)
    {
        Console.WriteLine();
        Console.WriteLine($"=== {title}");
        if (thrown is not null) Console.WriteLine($"  (test threw: {thrown.GetType().Name}: {thrown.Message})");
        Tree(steps, "", "  ");
        var scenario = new Scenario { Id = id, DisplayName = title, Steps = steps.Length > 0 ? steps : null, Result = thrown is null ? ExecutionResult.Passed : ExecutionResult.Failed };
        var feature = new Feature { DisplayName = "F", Scenarios = [scenario] };
        var logs = RequestResponseLogger.RequestAndResponseLogs;
        var collector = new ReportDiagnosticsCollector();
        using (ReportDiagnosticsScope.Begin(collector))
        {
            var paths = ReportGenerator.AttributeInteractionsToStepPaths(logs, [feature]);
            if (paths.TryGetValue(id, out var list))
            {
                var records = logs.Where(l => l.TestId == id && !l.IsDiagramMarker).ToArray();
                for (var i = 0; i < records.Length; i++)
                    if (records[i].Type == RequestResponseType.Request)
                        Console.WriteLine($"  call {records[i].Uri.AbsolutePath,-34} -> stepPath {list[i] ?? "null"}");
            }
        }
        foreach (var e in collector.Entries.Where(e => e.Kind == DiagnosticKind.StepAttributionMismatch))
            Console.WriteLine($"  diagnostic: {e.Message}");
        var bars = logs.Count(l => l.TestId == id && l.IsDiagramMarker && l.IsOverrideStart && l.MarkerKind == DiagramMarkerKind.Step);
        Console.WriteLine($"  bars drawn: {bars}; scenario attachments: {StepCollector.GetScenarioAttachments(id)?.Length ?? 0}");
    }

    public static void Tree(ScenarioStep[] steps, string prefix, string indent)
    {
        if (steps is null) return;
        for (var i = 0; i < steps.Length; i++)
        {
            var s = steps[i];
            var path = prefix + i;
            var dur = s.Duration is { } d ? $" {d.TotalMilliseconds:0}ms" : " (no duration)";
            Console.WriteLine($"{indent}{path,-8} [{s.Status}] {(s.Keyword is null ? "" : s.Keyword + " ")}{s.Text}{dur}{(s.FailureMessage is null ? "" : "  msg=" + s.FailureMessage)}{(s.Attachments is { Length: > 0 } ? "  attachments=" + s.Attachments.Length : "")}");
            Tree(s.SubSteps, path + ".", indent + "  ");
        }
    }
}
