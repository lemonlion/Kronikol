// Copy of the probe class used to write fixtures/ (2026-11-07). To run: copy into tests/Kronikol.Tests.EndToEnd,
// build, dotnet test --filter "FullyQualifiedName~Probe227Fixtures", then delete it again; it is never committed there.
using Kronikol.PlantUml;
using Kronikol.Reports;
using Kronikol.Tracking;
using static Kronikol.DefaultDiagramsFetcher;
namespace Kronikol.Tests.EndToEnd;

// PROBE ONLY (#227 investigation, kronikol-8b): writes fixture reports for the node probes in
// plans/TOGGLE_JUMP_PLAN.harness. Never committed; run with --filter "FullyQualifiedName~Probe227Fixtures".
public class Probe227Fixtures
{
    private static readonly string Out = Path.Combine(
        Path.GetDirectoryName(typeof(Probe227Fixtures).Assembly.Location)!, "..", "..", "..", "..", "..",
        "plans", "TOGGLE_JUMP_PLAN.harness", "fixtures");

    private static void Keep(string name, string url)
    {
        var html = new Uri(url).LocalPath;
        var dir = Path.GetDirectoryName(html)!;
        var dest = Path.Combine(Out, name);
        Directory.CreateDirectory(dest);
        foreach (var f in Directory.GetFiles(dir)) File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), true);
        File.WriteAllText(Path.Combine(dest, "ENTRY.txt"), Path.GetFileName(html));
    }

    private static (string temp, string output) Dirs(string name)
    {
        var root = Path.Combine(Path.GetTempPath(), "k227-" + name + "-" + Guid.NewGuid().ToString("N"));
        var temp = Path.Combine(root, "temp");
        var output = Path.Combine(root, "out");
        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(output);
        return (temp, output);
    }

    [Fact]
    public void Write_fixtures()
    {
        var d = Dirs("dense");
        Keep("dense", DenseReportFixture.GenerateWithFlows(d.temp, d.output, "Dense.html", features: 3,
            scenariosPerFeature: 6, callsPerScenario: 6, everyFilter: true, internalFlow: false));
        var w = Dirs("wide");
        Keep("wide-long-note", ReportTestHelper.GenerateReportWithWideLongNoteDiagram(w.temp, w.output, "WideLongNote.html", 30, true));
        var p = Dirs("parts");
        Keep("wide-in-parts", ReportTestHelper.GenerateReportWithWideDiagramInParts(p.temp, p.output, "WideInParts.html", true));
        var m = Dirs("many");
        Keep("wide-many", WideMany(m.temp, m.output, "WideMany.html", scenarios: 8, calls: 40));
    }

    // Eight scenarios, each a wide (fourteen participants), tall diagram drawn whole (well under the 12,000 px at which
    // the page draws parts), every call's request note starting with a header line: the headers toggle draws them all.
    private static string WideMany(string tempDir, string outputDir, string fileName, int scenarios, int calls)
    {
        var participants = string.Join("\n", Enumerable.Range(1, 14).Select(i => $"participant \"WideParticipantNumber{i}\" as a{i}"));
        string Body(int s) => string.Join("\n", Enumerable.Range(1, calls).Select(i =>
            $"a{1 + (i + s) % 13} -> a{2 + (i + s) % 13} : call {i}\nnote left\n<color:gray>[X-Request=s{s}c{i}]\n\n{{\n  \"field\": \"value {i}\"\n}}\nend note\n"
            + $"a{2 + (i + s) % 13} --> a{1 + (i + s) % 13} : reply {i}"));
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Wide Feature",
                Scenarios = Enumerable.Range(1, scenarios).Select(s => new Scenario
                {
                    Id = "w" + s, DisplayName = $"Wide diagram {s:00}", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1)
                }).ToArray()
            }
        };
        var diagrams = Enumerable.Range(1, scenarios)
            .Select(s => new DiagramAsCode("w" + s, "", $"@startuml\n{participants}\n{Body(s)}\n@enduml")).ToArray();
        var path = ReportGenerator.GenerateHtmlReport(diagrams, features, DateTime.UtcNow, DateTime.UtcNow, null,
            Path.Combine(tempDir, fileName), "Test Report", true, diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }
}
