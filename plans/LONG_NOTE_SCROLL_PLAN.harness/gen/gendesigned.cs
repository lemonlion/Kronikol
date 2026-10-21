#:package Kronikol@5.2.1
#:property JsonSerializerIsReflectionEnabledByDefault=true
// M1b: the 40-call shape of gen.cs drawn as the plan designs the cap, beside the same calls uncapped, both with the JSON
// view and the headers shown (so the page draws the source as written) and every payload expanded.
// A capped body: its header line, the blank line, the first 40 body lines, "...", and one row coloured #FFFFFF00 holding
// the widest body line. Usage: dotnet run --file gendesigned.cs -- <absolute out dir>
using System.Text;
using Kronikol;
using Kronikol.Reports;

var outDir = args[0];
Directory.CreateDirectory(outDir);
const int Cap = 40;

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

static string Body(string json, bool capped)
{
    var lines = json.Split('\n');
    if (!capped || lines.Length <= Cap) return json;
    var widest = lines.OrderByDescending(l => l.Length).First();
    return string.Join("\n", lines.Take(Cap)) + "\n...\n<color:#FFFFFF00>" + widest;
}

static string Source(int[] responseLines, bool capped)
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
        sb.Append(Body(Json(responseLines[i], i), capped)).Append("\nend note\n");
    }
    sb.Append("@enduml\n");
    return sb.ToString();
}

var mixed = Enumerable.Range(0, 40).Select(i => i % 10 == 3 ? 1500 : i % 10 is 1 or 5 or 8 ? 150 : 12).ToArray();
foreach (var (name, capped) in new[] { ("mixed-json", false), ("mixed-designed", true) })
{
    var source = Source(mixed, capped);
    File.WriteAllText(Path.Combine(outDir, name + ".puml"), source);
    var features = new[]
    {
        new Feature
        {
            DisplayName = "Probe",
            Scenarios = [new Scenario { Id = "t1", DisplayName = "Long notes " + name, IsHappyPath = true, Result = ExecutionResult.Passed,
                Duration = TimeSpan.FromSeconds(1), Steps = [new ScenarioStep { Keyword = "When", Text = "the items are listed", Status = ExecutionResult.Passed }] }]
        }
    };
    var path = ReportGenerator.GenerateHtmlReport([new DefaultDiagramsFetcher.DiagramAsCode("t1", "", source)], features,
        DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(outDir, name + "-expanded.html"), "Probe", true,
        diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
        toggleDefaults: ResolvedToggleDefaults.BuiltIn with
        {
            Details = ReportDetailsState.Expanded, NotePayloadFormat = NotePayloadFormat.Json, HeadersShown = true
        });
    Console.WriteLine($"{path}  ({source.Length:N0} characters of source)");
}
