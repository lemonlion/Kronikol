using Kronikol.History;
using Kronikol.Reports;

namespace Kronikol.Tests.History;

/// <summary>
/// A ledger is often shared by projects that upgrade Kronikol at different times, so a line of a kind a newer Kronikol
/// writes reaches older builds (plans/CALL_OPERATION_AND_ORIGIN_PLAN.md 4.8). Until #192 R1 the loader counted such a
/// line damaged, <c>history doctor</c> and <c>verify</c> called it unreadable, and <c>prune</c> and <c>compact</c> dropped
/// it. From R1 a line of a kind the build does not know is read past and kept as it is, so a later change can add one.
/// </summary>
public class HistoryOtherKindTests : IDisposable
{
    private const string Other = """{"t":"origins","id":"o1","suite":"Suite","rows":[["test","subject"]]}""";
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-other-kind").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string Ledger()
    {
        var path = Path.Combine(_dir, "history.jsonl");
        var features = new[] { new Feature { DisplayName = "Menu", Scenarios = [new Scenario { Id = "t1", DisplayName = "Order", Result = ExecutionResult.Passed }] } };
        for (var n = 1; n <= 3; n++)
        {
            var built = HistoryRunBuilder.Build(features, [], "Suite", null, new DateTimeOffset(2026, 10, 28, n, 0, 0, TimeSpan.Zero), new HistoryBuildOptions(), $"gh:{n}:1");
            HistoryLedgerWriter.Append(path, built.Roster, built.Run, "5.16.1");
            if (n == 2)
                File.AppendAllText(path, Other + "\n");
        }
        return path;
    }

    [Fact]
    public void The_loader_and_the_line_parser_skip_a_kind_they_do_not_know()
    {
        var path = Ledger();

        var ledger = HistoryLedgerReader.Read(path, 50).Ledger!;

        Assert.Equal(0, ledger.Stats.DamagedLines);
        Assert.Equal(1, ledger.Stats.OtherLines);
        Assert.Equal(3, ledger.Runs("Suite").Count);
        Assert.Equal(HistoryLineKind.Other, HistoryJson.Parse(Other).Kind);
        Assert.Equal(HistoryLineKind.Other, HistoryJson.Peek(Other).Kind);
        Assert.Empty(HistoryLedgerReader.Verify(path));
    }

    [Fact]
    public void A_torn_line_of_a_kind_this_build_does_not_know_is_damage()
    {
        // A writer killed mid-line leaves a torn tail of whatever kind it wrote. Of a known kind it is damage; of a newer
        // kind it was read past as whole and kept for good, while verify called it unreadable.
        var path = Ledger();
        File.AppendAllText(path, Other[..^12] + "\n");

        var ledger = HistoryLedgerReader.Read(path, 50).Ledger!;

        Assert.Equal(1, ledger.Stats.DamagedLines);
        Assert.Equal(1, ledger.Stats.OtherLines);
        Assert.Null(HistoryJson.Peek(Other[..^12]).Kind);
        Assert.Equal(HistoryLineKind.Other, HistoryJson.Peek(Other).Kind);
    }

    [Fact]
    public void The_doctor_says_a_newer_kind_was_read_past_and_finds_nothing_damaged()
    {
        var path = Ledger();
        File.WriteAllText(Path.Combine(_dir, ".gitattributes"), HistoryFormat.GitAttributesLine + "\n");
        var output = new StringWriter();

        var exit = Kronikol.Tool.HistoryCommand.Run(["doctor", "--history", path], output, new StringWriter(), _ => null);

        Assert.Equal(0, exit);
        Assert.Contains("none damaged", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("1 line of a kind this Kronikol does not know, a newer one's, read past and kept", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Prune_and_compact_keep_a_kind_they_do_not_know_and_the_ledgers_writer()
    {
        var path = Ledger();

        HistoryLedgerWriter.Prune(path, 2, "5.17.0");
        Assert.Contains(Other, File.ReadAllLines(path));
        HistoryLedgerWriter.Compact(path, 1, "5.17.0");

        var lines = File.ReadAllLines(path);
        Assert.Contains(Other, lines);
        // The header names the Kronikol that made the ledger; a rewrite by a later tool keeps it.
        Assert.Equal("5.16.1", HistoryLedgerReader.Read(path, 50).Ledger!.Generator);
    }
}
