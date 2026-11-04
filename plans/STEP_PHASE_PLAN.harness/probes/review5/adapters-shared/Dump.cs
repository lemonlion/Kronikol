using System.Text;
using Kronikol.Tracking;

namespace PhaseProbe;

/// <summary>Writes every test's calls, phases and diagram markers in the order they were logged, and its step tree.</summary>
public static class Dump
{
    /// <summary>A test's name by id, for an adapter whose call logs carry its fixture's name (NUnit).</summary>
    public static Dictionary<string, string> Names { get; } = new();

    public static void Write(string name)
    {
        var dir = Environment.GetEnvironmentVariable("PHASE_PROBE_OUT") ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(dir);
        var lines = new StringBuilder();
        // A marker carries the test's id as its name, so the name comes from the first real call.
        static string NameOf(IGrouping<string, RequestResponseLog> logs) => Names.TryGetValue(logs.Key, out var named) ? named : logs.FirstOrDefault(l => !l.IsDiagramMarker)?.TestName ?? logs.First().TestName;
        foreach (var test in RequestResponseLogger.RequestAndResponseLogs.GroupBy(l => l.TestId).OrderBy(g => NameOf(g), StringComparer.Ordinal))
        {
            lines.AppendLine($"## {NameOf(test)}");
            lines.AppendLine($"{"call",-34} {"http",-8} {"message",-8} ignored");
            var rows = new List<string>();
            var open = new Dictionary<string, (string? http, string? msg, string? ignored, int index)>();
            foreach (var log in test)
            {
                if (log.IsActionStart)
                {
                    rows.Add("--- ACTION START MARKER ---");
                    continue;
                }
                if (log.MarkerKind == DiagramMarkerKind.Step && log.IsOverrideStart)
                {
                    rows.Add($"[bar] {Bar(log.PlantUml)}");
                    continue;
                }
                if (log.IsDiagramMarker || log.Type != RequestResponseType.Request)
                    continue;
                var label = log.Uri.AbsolutePath.Trim('/');
                var isMessage = log.Uri.Scheme == "bus";
                if (!open.TryGetValue(label, out var row))
                {
                    row = (null, null, null, rows.Count);
                    rows.Add("");
                }
                row = isMessage ? row with { msg = log.Phase.ToString() } : row with { http = log.Phase.ToString(), ignored = log.TrackingIgnore ? "IGNORED" : "" };
                open[label] = row;
                rows[row.index] = $"{label,-34} {row.http ?? "-",-8} {row.msg ?? "-",-8} {row.ignored}";
            }
            foreach (var row in rows)
                lines.AppendLine(row);
            lines.AppendLine("steps:");
            AppendSteps(lines, StepCollector.GetSteps(test.Key), "  ");
            lines.AppendLine();
        }
        File.WriteAllText(Path.Combine(dir, $"{name}.txt"), lines.ToString());
        File.WriteAllLines(Path.Combine(dir, $"{name}.trace.txt"), FlowTrace.All);
    }

    private static string Bar(string? plantUml)
    {
        var text = (plantUml ?? "").Replace("\r", "").Split('\n').FirstOrDefault(l => l.Trim().Length > 0) ?? "";
        var colon = text.IndexOf(':');
        return (colon >= 0 ? text[(colon + 1)..] : text).Trim();
    }

    private static void AppendSteps(StringBuilder lines, Kronikol.Reports.ScenarioStep[] steps, string indent)
    {
        foreach (var step in steps)
        {
            if (step.IsAssertion)
                continue;
            lines.AppendLine($"{indent}{step.Keyword ?? "(none)"} {step.Text}  [{step.Status}]");
            if (step.SubSteps is { Length: > 0 } sub)
                AppendSteps(lines, sub, indent + "  ");
        }
    }
}
