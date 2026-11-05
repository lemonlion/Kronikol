using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using Kronikol.BDDfy.xUnit3;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace PhaseProbe;

/// <summary>What a test noted by hand, and its name by id.</summary>
public static class Known
{
    public static readonly ConcurrentDictionary<string, string> Names = new();
    public static readonly ConcurrentDictionary<string, ConcurrentQueue<string>> Notes = new();
    public static string Id => Xunit.TestContext.Current.Test!.UniqueID;

    public static void Register()
    {
        var test = Xunit.TestContext.Current.Test;
        if (test is not null) Names[test.UniqueID] = test.TestDisplayName;
    }

    public static void Note(string note, string? id = null)
    {
        id ??= Id;
        Notes.GetOrAdd(id, _ => new()).Enqueue(note);
    }

    public static string Attachable(string label)
    {
        var path = Path.Combine(AppContext.BaseDirectory, $"att-{label}.txt");
        File.WriteAllText(path, label);
        return path;
    }
}

/// <summary>Every test: calls with their phases, bars and markers as logged, the collector's tree, and the tree the report is built from (the adapter's own ToFeatures).</summary>
public static class Dump
{
    public static void Write(string name)
    {
        var dir = Environment.GetEnvironmentVariable("PHASE_PROBE_OUT") ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(dir);
        var lines = new StringBuilder();

        Scenario[] reported = [];
        try
        {
            var to = typeof(BDDfyScenarioCollector).Assembly.GetType("Kronikol.BDDfy.xUnit3.ScenarioInfoExtensions")!
                .GetMethod("ToFeatures", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
            var features = (Feature[])to.Invoke(null, [BDDfyScenarioCollector.GetAll().AsEnumerable()])!;
            reported = features.SelectMany(f => f.Scenarios).ToArray();
        }
        catch (Exception ex)
        {
            lines.AppendLine($"!! ToFeatures threw {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}");
        }

        var logs = RequestResponseLogger.RequestAndResponseLogs.ToArray();
        var ids = Known.Names.Keys.Union(logs.Select(l => l.TestId)).Union(reported.Select(s => s.Id)).Distinct();
        string NameOf(string id) => Known.Names.TryGetValue(id, out var n) ? n : logs.FirstOrDefault(l => l.TestId == id && !l.IsDiagramMarker)?.TestName ?? id;

        foreach (var id in ids.OrderBy(NameOf, StringComparer.Ordinal))
        {
            lines.AppendLine($"## {NameOf(id)}");
            var rows = new List<string>();
            foreach (var log in logs.Where(l => l.TestId == id))
            {
                if (log.IsActionStart) { rows.Add("--- ACTION START MARKER ---"); continue; }
                if (log.MarkerKind == DiagramMarkerKind.Step && log.IsOverrideStart) { rows.Add($"[bar] {Bar(log.PlantUml)}"); continue; }
                if (log.IsDiagramMarker || log.Type != RequestResponseType.Request) continue;
                var label = log.Uri.AbsolutePath.Trim('/');
                rows.Add($"{(log.Uri.Scheme == "bus" ? "msg " : "http")} {label,-30} {log.Phase,-8}{(log.TrackingIgnore ? " IGNORED" : "")}");
            }
            foreach (var row in rows) lines.AppendLine("  " + row);

            if (Known.Notes.TryGetValue(id, out var notes))
                foreach (var note in notes) lines.AppendLine("  note: " + note);

            lines.AppendLine($"  open at end: {StepCollector.HasActiveStep(id)}");
            lines.AppendLine("  collector:");
            AppendSteps(lines, StepCollector.GetSteps(id), "    ", false);
            var scenarioAttachments = StepCollector.GetScenarioAttachments(id);
            if (scenarioAttachments is { Length: > 0 })
                lines.AppendLine("    scenario attachments: " + string.Join(", ", scenarioAttachments.Select(a => a.Name)));

            var scenario = reported.FirstOrDefault(s => s.Id == id);
            if (scenario is null)
            {
                lines.AppendLine("  report: (no scenario)");
            }
            else
            {
                lines.AppendLine($"  report: {scenario.DisplayName} [{scenario.Result}] dur={Ms(scenario.Duration)}");
                if (scenario.ErrorMessage is { } error)
                    lines.AppendLine($"    error: {FailureText.FirstLine(error)}");
                if (scenario.ErrorStackTrace is { } stack)
                {
                    var thrown = FailureText.ThrownAt(stack);
                    var frames = stack.Replace("\r", "").Split('\n').Count(l => l.TrimStart().StartsWith("at "));
                    lines.AppendLine($"    stack: frames={frames} closeWhenDone={stack.Contains("CloseWhenDone")} thrownAt={(thrown is { } t ? t.Method + ":" + Path.GetFileName(t.File) : "-")}");
                }
                AppendSteps(lines, scenario.Steps ?? [], "    ", true);
                if (scenario.Attachments is { Length: > 0 })
                    lines.AppendLine("    scenario attachments: " + string.Join(", ", scenario.Attachments.Select(a => a.Name)));
            }
            lines.AppendLine();
        }
        File.WriteAllText(Path.Combine(dir, $"{name}.txt"), lines.ToString());
    }

    private static string Ms(TimeSpan? d) => d is null ? "null" : d.Value.TotalMilliseconds < 5 ? "<5ms" : $"~{Math.Round(d.Value.TotalMilliseconds / 100) * 100}ms";

    private static string Bar(string? plantUml)
    {
        var text = (plantUml ?? "").Replace("\r", "").Split('\n').FirstOrDefault(l => l.Trim().Length > 0) ?? "";
        var colon = text.IndexOf(':');
        return (colon >= 0 ? text[(colon + 1)..] : text).Trim();
    }

    private static void AppendSteps(StringBuilder lines, ScenarioStep[] steps, string indent, bool withDuration)
    {
        foreach (var step in steps)
        {
            var att = step.Attachments is { Length: > 0 } a ? "  @" + string.Join(",", a.Select(x => x.Name)) : "";
            var dur = withDuration ? " " + Ms(step.Duration) : "";
            var fail = step.FailureMessage is { } f ? " msg=" + FailureText.FirstLine(f) : "";
            lines.AppendLine($"{indent}{(step.IsAssertion ? "A:" : "")}{step.Keyword ?? "(none)"} {step.Text}  [{step.Status}]{dur}{att}{fail}");
            if (step.SubSteps is { Length: > 0 } sub)
                AppendSteps(lines, sub, indent + "  ", withDuration);
        }
    }
}
