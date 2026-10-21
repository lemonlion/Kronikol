#:package Kronikol@5.2.1
#:property JsonSerializerIsReflectionEnabledByDefault=true
// Writes probe reports with long notes, in the shape the generator writes (wrapWidth 800, header line, blank, body).
// Usage: dotnet run --file gen.cs -- <outDir>
using System.Text;
using Kronikol;
using Kronikol.Reports;

var outDir = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
Directory.CreateDirectory(outDir);

static string Json(int lines, int seed)
{
    var sb = new StringBuilder();
    sb.Append("{\n  \"items\": [\n");
    var items = Math.Max(1, (lines - 4) / 6);
    for (var i = 0; i < items; i++)
    {
        sb.Append("    {\n");
        sb.Append($"      \"id\": \"{seed:D3}-{i:D5}\",\n");
        sb.Append($"      \"name\": \"Breakfast item number {i} with a longer descriptive name\",\n");
        sb.Append($"      \"price\": {(i * 37 % 1000) / 100.0:0.00},\n");
        sb.Append($"      \"tags\": [\"warm\", \"sweet\", \"batch-{i % 7}\"]\n");
        sb.Append(i < items - 1 ? "    },\n" : "    }\n");
    }
    sb.Append("  ],\n  \"total\": " + items + "\n}");
    return sb.ToString();
}

static string Source(int[] responseLines)
{
    var sb = new StringBuilder();
    sb.Append("@startuml\n!pragma teoz true\nskinparam wrapWidth 800\nautonumber 1\n");
    sb.Append("actor \"Caller\" as caller\nparticipant \"Orders API\" as orders\nparticipant \"Menu API\" as menu\n");
    for (var i = 0; i < responseLines.Length; i++)
    {
        var target = i % 2 == 0 ? "orders" : "menu";
        sb.Append($"caller -> {target} : GET /api/items?page={i}\n");
        sb.Append("note left\n<color:#686868>[Accept=application/json]\n\n{ \"page\": " + i + " }\nend note\n");
        sb.Append($"{target} --> caller : 200 OK\n");
        sb.Append("note right\n<color:#686868>[Content-Type=application/json]\n\n");
        sb.Append(Json(responseLines[i], i)).Append("\nend note\n");
    }
    sb.Append("@enduml\n");
    return sb.ToString();
}

var shapes = new Dictionary<string, int[]>
{
    // 40 calls: 4 huge (1,500 lines), 12 medium (150), the rest short (12)
    ["mixed"] = Enumerable.Range(0, 40).Select(i => i % 10 == 3 ? 1500 : i % 10 is 1 or 5 or 8 ? 150 : 12).ToArray(),
    // one call with one huge body
    ["one-huge"] = [1500],
    // six calls, one diagram part once capped: two huge bodies, two medium, two short
    ["six"] = [1500, 12, 150, 12, 1500, 12],
    ["six-capped"] = [38, 12, 38, 12, 38, 12],
    // the same 40 calls with every body capped at 38 lines (header + blank + 38 = the prototype's 40-line cap)
    ["mixed-capped"] = Enumerable.Range(0, 40).Select(i => Math.Min(38, i % 10 == 3 ? 1500 : i % 10 is 1 or 5 or 8 ? 150 : 12)).ToArray(),
};

foreach (var (name, shape) in shapes)
{
    var source = Source(shape);
    File.WriteAllText(Path.Combine(outDir, name + ".puml"), source);
    foreach (var details in new[] { ReportDetailsState.Truncated, ReportDetailsState.Expanded })
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Probe",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "t1", DisplayName = "Long notes " + name, IsHappyPath = true,
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                        Steps = [new ScenarioStep { Keyword = "When", Text = "the items are listed", Status = ExecutionResult.Passed }]
                    }
                ]
            }
        };
        var file = Path.Combine(outDir, $"{name}-{details.ToString().ToLowerInvariant()}.html");
        var path = ReportGenerator.GenerateHtmlReport(
            [new DefaultDiagramsFetcher.DiagramAsCode("t1", "", source)], features,
            DateTime.UtcNow, DateTime.UtcNow, null, file, "Probe", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            toggleDefaults: ResolvedToggleDefaults.BuiltIn with { Details = details });
        Console.WriteLine($"{path}  ({source.Length:N0} chars of source)");
    }
}
