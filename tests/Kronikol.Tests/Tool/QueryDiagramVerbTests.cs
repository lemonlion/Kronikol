using System.Text.Json;
using Kronikol.Query;
using Kronikol.Reports;

namespace Kronikol.Tests.Tool;

/// <summary>
/// #184 R2 (plans/QUERY_FOR_PROGRAMS_PLAN.md 5.2): the diagram verb lists a scenario's diagrams, writes them all in one
/// call, writes every diagram of the run, and answers <c>--json</c>. A host checking its capture probed <c>sN/dK</c>
/// until a refusal to count them, one process a diagram.
/// </summary>
public class QueryDiagramVerbTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-diagram-verb").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private const string First = "@startuml\nparticipant A\nA -> A : first\n@enduml";
    private const string Second = "@startuml\nparticipant B\nB -> B : second, a longer one\n@enduml";

    // s0 (t-1) holds two diagrams, as a server-side rendering splits a long one; s1 (t-2) made no calls and holds none.
    private string Write()
    {
        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    new Scenario { Id = "t-1", DisplayName = "Lists orders", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1) },
                    new Scenario { Id = "t-2", DisplayName = "Cancels an order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1) }
                ]
            }
        ];
        DefaultDiagramsFetcher.DiagramAsCode[] diagrams = [new("t-1", "first", First), new("t-1", "second", Second)];
        var written = ReportGenerator.GenerateTestRunReportData(features,
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
            "Diagrams_" + Guid.NewGuid().ToString("N")[..8] + ".json", DataFormat.Json, diagrams, []);
        var path = Path.Combine(_directory, "TestRunReport.json");
        File.Move(written, path, overwrite: true);
        return path;
    }

    private static (string Output, string Error, int Exit) Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run(args, output, error);
        return (output.ToString(), error.ToString(), exit);
    }

    private static string[] Files(string directory) =>
        Directory.GetFiles(directory).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;

    [Fact]
    public void A_scenario_address_lists_the_scenarios_diagrams()
    {
        var report = Write();

        var (output, error, exit) = Run("diagram", report, "s0");

        Assert.True(exit == 0, error);
        Assert.Contains("s0/d0", output, StringComparison.Ordinal);
        Assert.Contains("s0/d1", output, StringComparison.Ordinal);
        Assert.DoesNotContain("@startuml", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_scenario_with_no_diagrams_says_so()
    {
        var report = Write();

        var (output, error, exit) = Run("diagram", report, "id:t-2");

        Assert.True(exit == 0, error);
        Assert.Contains("s1 has no diagrams", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("s0")]
    [InlineData("id:t-1")]
    public void A_scenario_address_with_out_writes_each_diagram_as_its_address(string address)
    {
        var report = Write();
        var directory = Path.Combine(_directory, "out-" + Guid.NewGuid().ToString("N")[..6], "nested");

        var (output, error, exit) = Run("diagram", report, address, "--out", directory);

        Assert.True(exit == 0, error);
        Assert.Equal(["s0-d0.puml", "s0-d1.puml"], Files(directory));
        foreach (var k in new[] { 0, 1 })
        {
            var one = Path.Combine(_directory, $"one-{k}.puml");
            Assert.Equal(0, Run("diagram", report, $"s0/d{k}", "--out", one).Exit);
            Assert.Equal(File.ReadAllBytes(one), File.ReadAllBytes(Path.Combine(directory, $"s0-d{k}.puml")));
        }
        Assert.Contains("wrote 2 diagrams", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Out_with_no_address_writes_every_diagram_in_the_run()
    {
        var report = Write();
        var directory = Path.Combine(_directory, "run");

        var (_, error, exit) = Run("diagram", report, "--out", directory);

        Assert.True(exit == 0, error);
        Assert.Equal(["s0-d0.puml", "s0-d1.puml"], Files(directory));
    }

    [Fact]
    public void Json_lists_and_writes_one_item_per_diagram()
    {
        var report = Write();

        var (listed, listError, listExit) = Run("diagram", report, "s0", "--json");
        Assert.True(listExit == 0, listError);
        using var listing = JsonDocument.Parse(listed);
        var items = listing.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["s0/d0", "s0/d1"], items.Select(i => i.GetProperty("address").GetString()));
        Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(First), items[0].GetProperty("bytes").GetInt32());
        Assert.Equal(0, items[0].GetProperty("notes").GetInt32());
        Assert.False(items[0].TryGetProperty("path", out _));

        var directory = Path.Combine(_directory, "json");
        var (written, writeError, writeExit) = Run("diagram", report, "--out", directory, "--json");
        Assert.True(writeExit == 0, writeError);
        using var writing = JsonDocument.Parse(written);
        var paths = writing.RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("path").GetString()).ToList();
        Assert.Equal([Path.GetFullPath(Path.Combine(directory, "s0-d0.puml")), Path.GetFullPath(Path.Combine(directory, "s0-d1.puml"))], paths);
    }

    [Fact]
    public void A_scenario_address_refuses_an_out_that_is_a_file()
    {
        var report = Write();
        var file = Path.Combine(_directory, "already.txt");
        File.WriteAllText(file, "kept");

        var (_, error, exit) = Run("diagram", report, "s0", "--out", file);

        Assert.Equal(2, exit);
        Assert.Contains("directory", error, StringComparison.Ordinal);
        Assert.Equal("kept", File.ReadAllText(file));
    }

    [Fact]
    public void One_diagram_is_still_never_printed_and_past_the_end_is_still_refused()
    {
        var report = Write();

        var (printed, refusal, exit) = Run("diagram", report, "s0/d0");
        Assert.Equal(2, exit);
        Assert.Equal("", printed);
        Assert.Contains("--out FILE", refusal, StringComparison.Ordinal);

        var (_, past, pastExit) = Run("diagram", report, "s0/d2");
        Assert.Equal(2, pastExit);
        Assert.Contains("No diagram d2 in s0", past, StringComparison.Ordinal);
    }

    [Fact]
    public void No_address_and_no_out_is_refused_naming_both_forms()
    {
        var report = Write();

        var (_, error, exit) = Run("diagram", report);

        Assert.Equal(2, exit);
        Assert.Contains("s3/d0", error, StringComparison.Ordinal);
        Assert.Contains("--out DIR", error, StringComparison.Ordinal);
    }

    [Fact]
    public void The_verb_table_describes_the_new_forms_and_the_json_answer()
    {
        var diagram = VerbTable.Find("diagram")!;

        Assert.True(diagram.Json);
        Assert.Contains("<report> s3", diagram.Forms);
        Assert.Contains("<report> s3 --out DIR", diagram.Forms);
        Assert.Contains("<report> --out DIR", diagram.Forms);
    }
}
