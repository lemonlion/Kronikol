using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Kronikol.PlantUml;
using Kronikol.Tracking;

namespace PhaseProbe;

/// <summary>Every test's calls, phases and markers in the order logged, a skeleton of each diagram, and its step tree.</summary>
public static class Dump
{
    public static readonly ConcurrentDictionary<string, string> Names = new();
    public static readonly ConcurrentDictionary<string, ConcurrentQueue<string>> Notes = new();

    public static void Note(string testId, string note) => Notes.GetOrAdd(testId, _ => new()).Enqueue(note);

    private static string NameOf(IGrouping<string, RequestResponseLog> logs) =>
        Names.TryGetValue(logs.Key, out var name) ? name : logs.FirstOrDefault(l => !l.IsDiagramMarker)?.TestName ?? logs.Key;

    public static void Write(string name)
    {
        var dir = Environment.GetEnvironmentVariable("PHASE_PROBE_OUT") ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(dir);
        var lines = new StringBuilder();
        var all = RequestResponseLogger.RequestAndResponseLogs;
        foreach (var test in all.GroupBy(l => l.TestId ?? "(null id)").OrderBy(g => NameOf(g), StringComparer.Ordinal))
        {
            lines.AppendLine($"## {NameOf(test)}");
            if (Notes.TryGetValue(test.Key, out var notes))
                foreach (var note in notes)
                    lines.AppendLine("note: " + note);
            foreach (var log in test)
            {
                if (log.IsActionStart) { lines.AppendLine("  --- MARKER ---"); continue; }
                if (log.IsOverrideStart && log.MarkerKind == DiagramMarkerKind.Step) { lines.AppendLine($"  [bar] {Bar(log.PlantUml)}"); continue; }
                if (log.IsOverrideStart) { lines.AppendLine($"  [{log.MarkerKind}] {Bar(log.PlantUml)}"); continue; }
                if (log.IsDiagramMarker) continue;
                var label = log.Uri.AbsolutePath.Trim('/');
                var variant = log.SetupVariant is not null || log.ActionVariant is not null ? " variants" : "";
                lines.AppendLine($"  {log.Type,-8} {label,-28} {log.Phase,-7} {(log.TrackingIgnore ? "IGNORED" : "")}{variant}");
            }
            lines.AppendLine($"  markers={test.Count(l => l.IsActionStart)}");
            foreach (var (setup, collapse) in Configs())
            {
                var tracked = test.Where(l => l is not null && !l.TrackingIgnore).ToArray();
                lines.AppendLine($"  diagram(separateSetup={setup},collapse={collapse}):");
                try
                {
                    var per = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(tracked, separateSetup: setup, collapseConsecutiveIdenticalCalls: collapse, maxArrowsPerDiagram: int.TryParse(Environment.GetEnvironmentVariable("PROBE_CAP"), out var cap) ? cap : null).ToArray();
                    if (per.Length == 0) lines.AppendLine("    (no group)");
                    foreach (var one in per)
                    {
                        var d = 0;
                        foreach (var plant in one.PlantUmls)
                        {
                            d++;
                            lines.AppendLine($"    -- diagram {d}");
                            foreach (var s in Skeleton(plant.PlainText))
                                lines.AppendLine("    " + s);
                        }
                        if (d == 0) lines.AppendLine("    (group with no diagram)");
                    }
                }
                catch (Exception ex)
                {
                    lines.AppendLine($"    THREW {ex.GetType().Name}: {ex.Message}");
                }
            }
            lines.AppendLine("  steps:");
            AppendSteps(lines, StepCollector.GetSteps(test.Key), "    ");
            lines.AppendLine();
        }
        foreach (var notes in Notes.Where(n => all.All(l => l.TestId != n.Key)))
        {
            lines.AppendLine($"## {(Names.TryGetValue(notes.Key, out var n2) ? n2 : notes.Key)} (no logs)");
            foreach (var note in notes.Value) lines.AppendLine("note: " + note);
            lines.AppendLine();
        }
        File.WriteAllText(Path.Combine(dir, $"{name}.txt"), lines.ToString());
    }

    private static IEnumerable<(bool, bool)> Configs()
    {
        var wanted = Environment.GetEnvironmentVariable("PROBE_CONFIGS") ?? "10";
        foreach (var part in wanted.Split(','))
            yield return (part[0] == '1', part[1] == '1');
    }

    private static readonly Regex Arrow = new(@"^(\S+)\s+-+(?:\[[^\]]*\])?-*>+\s*(\S+)\s*:\s*(.*)$");

    public static IEnumerable<string> Skeleton(string source)
    {
        var inNote = false;
        foreach (var raw in source.Replace("\r", "").Split('\n'))
        {
            var t = raw.Trim();
            if (inNote) { if (t is "end note" or "endnote" or "end hnote" or "endhnote") inNote = false; else if (t.Contains("body-")) yield return "(detailed " + Clean(t) + ")"; continue; }
            if (t.StartsWith("hnote across"))
            {
                var c = t.IndexOf(':');
                yield return "[" + Clean(c >= 0 ? t[(c + 1)..] : t) + "]";
                if (c < 0) inNote = true;
                continue;
            }
            if (Regex.IsMatch(t, @"^(note|hnote|rnote)\b")) { if (!t.Contains(':')) inNote = true; continue; }
            if (Regex.IsMatch(t, @"^partition\b.*\bSetup$")) { yield return "partition Setup {"; continue; }
            if (t == "end") { yield return "}"; continue; }
            if (t.StartsWith("loop ")) { yield return t; continue; }
            var m = Arrow.Match(t);
            if (m.Success) yield return $"{m.Groups[1].Value}->{m.Groups[2].Value}: {Clean(m.Groups[3].Value)}";
        }
    }

    private static string Clean(string s)
    {
        s = Regex.Replace(s, @"<[^>]+>", "");
        return s.Length > 70 ? s[..70] : s;
    }

    private static string Bar(string? plantUml)
    {
        var text = (plantUml ?? "").Replace("\r", "").Split('\n').FirstOrDefault(l => l.Trim().Length > 0) ?? "";
        var colon = text.IndexOf(':');
        return Clean((colon >= 0 ? text[(colon + 1)..] : text).Trim());
    }

    private static void AppendSteps(StringBuilder lines, Kronikol.Reports.ScenarioStep[] steps, string indent)
    {
        foreach (var step in steps)
        {
            if (step.IsAssertion) continue;
            lines.AppendLine($"{indent}{step.Keyword ?? "(none)"} {step.Text}  [{step.Status}]");
            if (step.SubSteps is { Length: > 0 } sub)
                AppendSteps(lines, sub, indent + "  ");
        }
    }
}
