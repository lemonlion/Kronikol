#:package Kronikol@5.2.1
#:property JsonSerializerIsReflectionEnabledByDefault=true
// One call whose response JSON holds a multi-line string: 4 lines as JSON, 16 as YAML (a block scalar), so it is
// short under JSON and long under YAML at the default 10-line limit. Usage: dotnet run --file genyaml.cs -- <outDir>
using Kronikol;
using Kronikol.Reports;

var outDir = args[0];
Directory.CreateDirectory(outDir);
var query = string.Join("\\n", Enumerable.Range(1, 14).Select(i => $"  column_{i:D2}"));
var source = $$"""
    @startuml
    !pragma teoz true
    skinparam wrapWidth 800
    autonumber 1
    actor "Caller" as caller
    participant "Reports API" as api
    caller -> api : POST /api/reports/query
    note left
    <color:#686868>[Content-Type=application/json]

    { "name": "monthly" }
    end note
    api --> caller : 200 OK
    note right
    <color:#686868>[Content-Type=application/json]

    {
      "query": "SELECT\n{{query}}\nFROM sales",
      "rows": 3
    }
    end note
    @enduml
    """;
var features = new[]
{
    new Feature
    {
        DisplayName = "Probe",
        Scenarios = [new Scenario { Id = "t1", DisplayName = "Multi-line string", IsHappyPath = true, Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
            Steps = [new ScenarioStep { Keyword = "When", Text = "the report runs", Status = ExecutionResult.Passed }] }]
    }
};
var path = ReportGenerator.GenerateHtmlReport([new DefaultDiagramsFetcher.DiagramAsCode("t1", "", source)], features,
    DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(outDir, "yaml-longer.html"), "Probe", true,
    diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
Console.WriteLine(path);
