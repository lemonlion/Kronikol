using Kronikol.ComponentDiagram;
using Kronikol.Reports;
using Kronikol.Tool;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.Reports.Merge;

public class MergeCommandTests
{
    private static string WriteMergeableJson(string dir, string name, Feature[] features, ComponentRelationship[] rels, DateTime start, DateTime end)
    {
        var json = ReportGenerator.GenerateMergeableReportJson(
            features, start, end,
            features.SelectMany(f => f.Scenarios)
                .Select(s => new DiagramAsCode(s.Id, "", $"@startuml\n{s.Id}->X\n@enduml"))
                .ToLookup(d => d.TestRuntimeId, d => d.CodeBehind),
            rels, internalFlowSegmentData: null, wholeTestFlow: null,
            WholeTestFlowVisualization.None, ciMetadata: null);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Merge_command_combines_directory_of_reports_into_html()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kronikol-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            WriteMergeableJson(dir, "runner1.json",
                [ new Feature { DisplayName = "Orders", Scenarios = [ new Scenario { Id = "r1s1", DisplayName = "Place order", Result = ExecutionResult.Passed } ] } ],
                [ new ComponentRelationship("Test", "OrdersApi", "HTTP", new HashSet<string> { "POST /orders" }, 1, 1, "http") ],
                new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 10, 2, 0, DateTimeKind.Utc));

            WriteMergeableJson(dir, "runner2.json",
                [ new Feature { DisplayName = "Inventory", Scenarios = [ new Scenario { Id = "r2s1", DisplayName = "Adjust stock", Result = ExecutionResult.Failed, ErrorMessage = "oops" } ] } ],
                [ new ComponentRelationship("OrdersApi", "InventoryDb", "SQL", new HashSet<string> { "SELECT" }, 3, 1, "sql") ],
                new DateTime(2026, 1, 1, 9, 55, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc));

            // A schema file in the same directory must be ignored.
            File.WriteAllText(Path.Combine(dir, "TestRunReport.schema.json"), "{}");

            var output = Path.Combine(dir, "Combined.html");
            var outWriter = new StringWriter();
            var errWriter = new StringWriter();

            var exit = MergeCommand.Run([dir, "-o", output, "-t", "Combined CLI"], outWriter, errWriter);

            Assert.Equal(0, exit);
            Assert.True(File.Exists(output), "Combined HTML should be written. Stderr: " + errWriter);
            var html = File.ReadAllText(output);
            Assert.Contains("Combined CLI", html);
            Assert.Contains("Orders", html);
            Assert.Contains("Inventory", html);
            Assert.Contains("Place order", html);
            Assert.Contains("Adjust stock", html);
            // Both runners' reports were picked up (schema file excluded).
            Assert.Contains("runner1.json", outWriter.ToString());
            Assert.Contains("runner2.json", outWriter.ToString());
            Assert.DoesNotContain("schema.json", outWriter.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Merge_command_errors_when_no_inputs()
    {
        var exit = MergeCommand.Run([], new StringWriter(), new StringWriter());
        Assert.Equal(2, exit);
    }

    [Fact]
    public void Merge_command_errors_on_non_mergeable_input()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kronikol-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // A standard (non-mergeable) report should be rejected with a clear error.
            var standardPath = Path.Combine(dir, "standard.json");
            var written = ReportGenerator.GenerateTestRunReportData(
                [ new Feature { DisplayName = "F", Scenarios = [ new Scenario { Id = "x", DisplayName = "s", Result = ExecutionResult.Passed } ] } ],
                new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 10, 1, 0, DateTimeKind.Utc),
                "standard.json", DataFormat.Json);
            File.Copy(written, standardPath, overwrite: true);

            var err = new StringWriter();
            var exit = MergeCommand.Run([standardPath, "-o", Path.Combine(dir, "out.html")], new StringWriter(), err);

            Assert.Equal(1, exit);
            Assert.Contains("GenerateMergeableData", err.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
