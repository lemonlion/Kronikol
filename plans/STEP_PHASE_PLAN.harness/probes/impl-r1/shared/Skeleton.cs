using System.Text;
using Kronikol;
using Kronikol.PlantUml;
using Kronikol.Tracking;

namespace Probe;

/// <summary>Writes each test's phases (every record in log order) and the skeleton of its diagram under the given options.</summary>
public static class Skeleton
{
    public static void Write(string name)
    {
        var dir = Environment.GetEnvironmentVariable("PHASE_PROBE_OUT") ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder();
        var logs = RequestResponseLogger.RequestAndResponseLogs.ToArray();
        var names = logs.GroupBy(l => l.TestId).ToDictionary(g => g.Key, g => g.FirstOrDefault(l => !l.IsDiagramMarker)?.TestName ?? g.First().TestName);
        foreach (var test in logs.GroupBy(l => l.TestId).OrderBy(g => Short(names[g.Key]), StringComparer.Ordinal))
        {
            sb.AppendLine($"## {Short(names[test.Key])}");
            foreach (var log in test)
                sb.AppendLine("  " + Row(log));
            sb.AppendLine("  steps:");
            AppendSteps(sb, StepCollector.GetSteps(test.Key), "    ");
            var kept = test.Where(l => !l.TrackingIgnore).ToArray();
            sb.AppendLine("  diagram separateSetup:");
            AppendDiagrams(sb, kept, separate: true, collapse: false);
            sb.AppendLine();
        }
        File.WriteAllText(Path.Combine(dir, $"{name}.txt"), sb.ToString());
    }

    public static string Short(string name)
    {
        var i = name.LastIndexOf('.');
        return i >= 0 ? name[(i + 1)..] : name;
    }

    public static string Row(RequestResponseLog log)
    {
        if (log.IsActionStart) return "--- MARKER ---";
        if (log.IsOverrideStart) return $"[{log.MarkerKind}] {Text(log.PlantUml)}";
        if (log.IsOverrideEnd) return "";
        var variants = log.SetupVariant is null && log.ActionVariant is null ? "" : " +variants";
        var ignored = log.TrackingIgnore ? " IGNORED" : "";
        return $"{log.Type,-8} {log.Uri.Scheme,-5} {log.Uri.AbsolutePath.Trim('/'),-34} phase={log.Phase,-7}{variants}{ignored}";
    }

    private static string Text(string? plantUml)
    {
        var text = (plantUml ?? "").Replace("\r", "").Split('\n').FirstOrDefault(l => l.Trim().Length > 0) ?? "";
        var colon = text.IndexOf(':');
        return (colon >= 0 ? text[(colon + 1)..] : text).Trim();
    }

    public static void AppendDiagrams(StringBuilder sb, IEnumerable<RequestResponseLog> logs, bool separate, bool collapse, int maxEncoded = PlantUmlCreator.DefaultMaxEncodedDiagramLength, int? maxArrows = null)
    {
        var results = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, separateSetup: separate, collapseConsecutiveIdenticalCalls: collapse, maxEncodedDiagramLength: maxEncoded, maxArrowsPerDiagram: maxArrows, lazyLoadImages: false).ToArray();
        foreach (var result in results)
        {
            var count = 0;
            foreach (var d in result.PlantUmls)
            {
                count++;
                sb.AppendLine($"    diagram {count}:");
                foreach (var line in d.PlainText.Replace("\r", "").Split('\n'))
                {
                    var t = line.Trim();
                    if (t.StartsWith("partition") || t == "end" || t.StartsWith("loop") || t.Contains("-->") || t.Contains("->") || t.StartsWith("hnote") || t.StartsWith("note") && false)
                        sb.AppendLine("      " + Trim(t));
                    else if (t.StartsWith("<b>") || t.Contains("VARIANT") || t.Contains("PAYLOAD"))
                        sb.AppendLine("        note: " + Trim(t));
                }
            }
            if (count == 0) sb.AppendLine("    (no diagram)");
        }
        if (results.Length == 0) sb.AppendLine("    (no result)");
    }

    private static string Trim(string line)
    {
        if (line.StartsWith("partition")) return "partition Setup";
        return line.Length > 110 ? line[..110] : line;
    }

    private static void AppendSteps(StringBuilder lines, Kronikol.Reports.ScenarioStep[] steps, string indent)
    {
        foreach (var step in steps)
        {
            if (step.IsAssertion) continue;
            lines.AppendLine($"{indent}{step.Keyword ?? "(none)"} {step.Text} [{step.Status}]{(step.FailureMessage is { } m ? " msg=" + m : "")}");
            if (step.SubSteps is { Length: > 0 } sub) AppendSteps(lines, sub, indent + "  ");
        }
    }
}

/// <summary>Answers every request after a short yield.</summary>
public sealed class Answer : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await Task.Delay(1, cancellationToken);
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"p\":1}") };
    }
}
