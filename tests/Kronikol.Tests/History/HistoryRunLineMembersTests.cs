using System.Text.Json;
using Kronikol.History;
using Kronikol.Reports;

namespace Kronikol.Tests.History;

/// <summary>
/// What a run line records from #192 R1 on (plans/CALL_OPERATION_AND_ORIGIN_PLAN.md 4.8): the Kronikol that recorded the
/// run (<c>generator</c>), the consumer's patterns beside the hash that names them (<c>shapeTemplates</c>), so a later run
/// can bridge an edit of them, and every member a newer writer added, carried through as it was by every writer that
/// builds a line from others.
/// </summary>
public class HistoryRunLineMembersTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

    private static Feature[] Features() =>
        [new Feature { DisplayName = "Menu", Scenarios = [new Scenario { Id = "t1", DisplayName = "Order", Result = ExecutionResult.Passed }] }];

    private static HistoryRun Built(HistoryShapeRules? rules = null) =>
        HistoryRunBuilder.Build(Features(), [], "Suite", null, At, new HistoryBuildOptions { ShapeRules = rules }, "gh:1:1").Run;

    private static HistoryRun RoundTrip(HistoryRun run) => HistoryJson.Parse(HistoryJson.RunLine(run)).Run!;

    [Fact]
    public void The_run_line_names_the_tool_that_wrote_it()
    {
        var run = Built() with { Generator = "5.17.0" };

        Assert.Contains("\"generator\":\"5.17.0\"", HistoryJson.RunLine(run), StringComparison.Ordinal);
        Assert.Equal("5.17.0", RoundTrip(run).Generator);
        Assert.Null(RoundTrip(Built()).Generator);
        Assert.DoesNotContain("generator", HistoryJson.RunLine(Built()), StringComparison.Ordinal);
        using var document = JsonDocument.Parse(HistoryJson.RunLine(run with { ServiceId = "menu" }));
        Assert.Equal("serviceId", document.RootElement.EnumerateObject().Last().Name);
    }

    [Fact]
    public void The_run_line_records_the_patterns_its_hash_names()
    {
        HistoryShapeTemplate[] templates = [new(@"menu-[a-z]+", "{menu}"), new(@"(?<=key-)\d+", "{key}")];
        var run = Built(HistoryShapeRules.Create(templates));

        Assert.Equal(templates, run.ShapeTemplates);
        Assert.Equal(templates, RoundTrip(run).ShapeTemplates);
        using (var document = JsonDocument.Parse(HistoryJson.RunLine(run)))
        {
            var first = document.RootElement.GetProperty("shapeTemplates")[0];
            Assert.Equal("menu-[a-z]+", first.GetProperty("pattern").GetString());
            Assert.Equal("{menu}", first.GetProperty("placeholder").GetString());
        }
        Assert.Null(Built().ShapeTemplates);
        Assert.DoesNotContain("shapeTemplates", HistoryJson.RunLine(Built()), StringComparison.Ordinal);
    }

    [Fact]
    public void A_member_a_newer_writer_added_is_carried_through()
    {
        var line = HistoryJson.RunLine(Built());
        var newer = line[..^1] + ",\"origin\":{\"t1\":[\"test\",\"subject\"]},\"note\":\"from 9.0\"}";

        var read = HistoryJson.Parse(newer).Run!;
        var written = HistoryJson.RunLine(read);

        Assert.Contains("\"origin\":{\"t1\":[\"test\",\"subject\"]}", written, StringComparison.Ordinal);
        Assert.Contains("\"note\":\"from 9.0\"", written, StringComparison.Ordinal);
        Assert.Equal(newer.Length, written.Length);
    }

    [Fact]
    public void A_bridges_counts_holding_what_is_no_count_are_none()
    {
        // Dropping the entry that is no count would read every count after it at the position before; the bridge's
        // counts are read as none instead, and its positions count as the run's own.
        var run = Built() with
        {
            Bridge = new Dictionary<string, HistoryBridge>(StringComparer.Ordinal) { ["7"] = new() { ShapeSet = ["s"], ShapeOrdered = ["o"], Calls = [3] } }
        };
        var line = HistoryJson.RunLine(run);
        Assert.Contains("\"calls\":[3]", line, StringComparison.Ordinal);

        Assert.Equal([3], RoundTrip(run).Bridge!["7"].Calls);
        Assert.Null(HistoryJson.Parse(line.Replace("\"calls\":[3]", "\"calls\":[\"3\"]", StringComparison.Ordinal)).Run!.Bridge!["7"].Calls);
        Assert.Null(HistoryJson.Parse(line.Replace("\"calls\":[3]", "\"calls\":[3.5]", StringComparison.Ordinal)).Run!.Bridge!["7"].Calls);
    }

    [Fact]
    public void A_member_carried_from_an_indented_fragment_stays_on_its_run_line()
    {
        // A fragment is written indented, so a newer writer's member read from one spans lines as it was read; the ledger
        // holds one run per line, ASCII-escaped, and a member copied as it was read would split the run across lines.
        var built = HistoryRunBuilder.Build(Features(), [], "Suite", null, At, new HistoryBuildOptions(), "gh:7:1");
        var node = System.Text.Json.Nodes.JsonNode.Parse(HistoryFragment.Write(built.Roster, built.Run, "9.0.0", built.Shapes))!;
        node["run"]!["origin"] = System.Text.Json.Nodes.JsonNode.Parse("""{"t1": ["test", "café"], "nested": {"deep": [1, 2]}}""");
        var newer = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var fragment = HistoryFragment.Parse(newer);
        var dir = Directory.CreateTempSubdirectory("kronikol-run-members").FullName;
        try
        {
            var ledger = Path.Combine(dir, "history.jsonl");

            Assert.Equal(HistoryAppendOutcome.Appended, HistoryLedgerWriter.Append(ledger, fragment.Roster, fragment.Run, "5.17.0", shapes: fragment.Shapes).Outcome);

            var lines = File.ReadAllLines(ledger);
            foreach (var line in lines)
                using (JsonDocument.Parse(line)) { }
            var runLine = Assert.Single(lines, l => l.Contains("\"t\":\"run\"", StringComparison.Ordinal));
            Assert.All(runLine, c => Assert.True(c < 128, $"a raw '{c}' on the run line"));
            using var run = JsonDocument.Parse(runLine);
            Assert.Equal("café", run.RootElement.GetProperty("origin").GetProperty("t1")[1].GetString());
            Assert.Equal(2, run.RootElement.GetProperty("origin").GetProperty("nested").GetProperty("deep").GetArrayLength());
            Assert.Equal(0, HistoryLedgerReader.Read(ledger, 10).Ledger!.Stats.DamagedLines);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>A run as a newer writer wrote it: a member this build does not know, the consumer's patterns and its writer.</summary>
    private static HistoryFragment Newer(string runId, string testId)
    {
        var features = new[] { new Feature { DisplayName = "Menu", Scenarios = [new Scenario { Id = testId, DisplayName = "Order " + testId, Result = ExecutionResult.Passed }] } };
        var built = HistoryRunBuilder.Build(features, [], "Suite", null, At, new HistoryBuildOptions
        {
            ShapeRules = HistoryShapeRules.Create([new HistoryShapeTemplate("menu-[a-z]+", "{menu}")])
        }, runId);
        var line = HistoryJson.RunLine(built.Run with { Generator = "9.0.0" });
        var run = HistoryJson.Parse(line[..^1] + ",\"origin\":[\"test\"]}").Run!;
        return new HistoryFragment(HistoryFormat.Version, built.Roster, run, built.Shapes);
    }

    [Fact]
    public void Prune_compact_attempts_and_fold_keep_members_they_do_not_know()
    {
        void Holds(HistoryRun run, string where)
        {
            var line = HistoryJson.RunLine(run);
            Assert.True(line.Contains("\"origin\":[\"test\"]", StringComparison.Ordinal), where + ": " + line);
            Assert.True(run.ShapeTemplates is [{ Pattern: "menu-[a-z]+" }], where);
            Assert.True(run.Generator == "9.0.0", where);
        }

        Holds(HistoryFold.Attempts([Newer("gh:1:1", "t1"), Newer("gh:1:1", "t1")]).Run, "attempts");
        Holds(Assert.Single(HistoryFold.Fold([Newer("gh:2:1", "t1"), Newer("gh:2:1", "t2")])).Run, "a fold of shards");

        var dir = Directory.CreateTempSubdirectory("kronikol-members").FullName;
        try
        {
            var ledger = Path.Combine(dir, "history.jsonl");
            foreach (var n in new[] { 3, 4, 5 })
            {
                var fragment = Newer($"gh:{n}:1", "t1");
                HistoryLedgerWriter.Append(ledger, fragment.Roster, fragment.Run with { At = At.AddHours(n) }, "5.17.0", shapes: fragment.Shapes);
            }
            HistoryLedgerWriter.Prune(ledger, 2, "5.17.0");
            Holds(HistoryLedgerReader.Read(ledger, 50).Ledger!.LatestRun("Suite")!, "prune");
            HistoryLedgerWriter.Compact(ledger, 1, "5.17.0");
            Holds(HistoryLedgerReader.Read(ledger, 50).Ledger!.LatestRun("Suite")!, "compact");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
