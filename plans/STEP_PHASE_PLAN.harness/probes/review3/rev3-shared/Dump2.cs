using System.Collections.Concurrent;
using System.Text;
using Kronikol.PlantUml;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace PhaseProbe;

/// <summary>Every test's logs in order with phases, markers, the step tree (with open steps) and its PlantUML's structural lines.</summary>
public static class Dump2
{
    public static readonly ConcurrentDictionary<string, string> Names = new();

    public static void Write(string name, bool withPlantUml = true)
    {
        var dir = Environment.GetEnvironmentVariable("PHASE_PROBE_OUT") ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(dir);
        var lines = new StringBuilder();
        var logs = RequestResponseLogger.RequestAndResponseLogs;
        var ids = logs.Select(l => l.TestId).Concat(Names.Keys).Distinct().ToArray();
        string NameOf(string id) => Names.TryGetValue(id, out var n) ? n : (logs.FirstOrDefault(l => l.TestId == id && !l.IsDiagramMarker)?.TestName ?? id);
        foreach (var id in ids.OrderBy(NameOf, StringComparer.Ordinal))
        {
            lines.AppendLine($"## {NameOf(id)}");
            foreach (var log in logs.Where(l => l.TestId == id))
            {
                if (log.IsActionStart) { lines.AppendLine("  --- ACTION START MARKER ---"); continue; }
                if (log.IsDiagramMarker)
                {
                    if (log.IsOverrideStart) lines.AppendLine($"  [{log.MarkerKind}] {Bar(log.PlantUml)}");
                    continue;
                }
                if (log.Type != RequestResponseType.Request) continue;
                var label = log.Uri.AbsolutePath.Trim('/');
                lines.AppendLine($"  {label,-44} {(log.Uri.Scheme == "bus" ? "msg " : "http")} {log.Phase,-8} {(log.TrackingIgnore ? "IGNORED" : "")}");
            }
            lines.AppendLine("  steps:");
            AppendSteps(lines, StepCollector.GetSteps(id), "    ");
            if (StepCollector.HasActiveStep(id)) lines.AppendLine("    !!! A STEP IS STILL OPEN AT THE END OF THE RUN");
            lines.AppendLine();
        }
        File.WriteAllText(Path.Combine(dir, $"{name}.txt"), lines.ToString());

        if (!withPlantUml) return;
        var uml = new StringBuilder();
        var tracked = logs.Where(l => !l.TrackingIgnore).ToArray();
        foreach (var test in PlantUmlCreator.GetPlantUmlImageTagsPerTestId(tracked, separateSetup: true, clientSideSplitting: true, maxEncodedDiagramLength: int.MaxValue,
                     collapseConsecutiveIdenticalCalls: true).OrderBy(t => NameOf(t.TestId), StringComparer.Ordinal))
        {
            uml.AppendLine($"## {NameOf(test.TestId)} (TestName field: {test.TestName})");
            foreach (var diagram in test.PlantUmls)
                foreach (var raw in diagram.PlainText.Replace("\r", "").Split('\n'))
                {
                    var l = raw.Trim();
                    if (l.StartsWith("partition") || l == "end" || l.StartsWith("hnote") || l.Contains("->") || l.Contains("-->") || l.StartsWith("@startuml") || l.StartsWith("@enduml") || l.StartsWith("loop"))
                        uml.AppendLine("  " + (l.Length > 150 ? l[..150] : l));
                }
            uml.AppendLine();
        }
        File.WriteAllText(Path.Combine(dir, $"{name}.plantuml.txt"), uml.ToString());
    }

    private static string Bar(string? plantUml)
    {
        var text = (plantUml ?? "").Replace("\r", "").Split('\n').FirstOrDefault(l => l.Trim().Length > 0) ?? "";
        var colon = text.IndexOf(':');
        return (colon >= 0 ? text[(colon + 1)..] : text).Trim();
    }

    private static void AppendSteps(StringBuilder lines, ScenarioStep[] steps, string indent)
    {
        foreach (var step in steps)
        {
            if (step.IsAssertion) continue;
            lines.AppendLine($"{indent}{step.Keyword ?? "(none)"} {step.Text}  [{step.Status}]");
            if (step.SubSteps is { Length: > 0 } sub) AppendSteps(lines, sub, indent + "  ");
        }
    }
}
