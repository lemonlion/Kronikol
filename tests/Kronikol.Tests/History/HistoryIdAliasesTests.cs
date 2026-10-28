using Kronikol.History;
using Kronikol.Reports;

namespace Kronikol.Tests.History;

/// <summary>
/// Kronikol's own scenario aliases (plans/CALL_OPERATION_AND_ORIGIN_PLAN.md 4.8 part 5, Q15). When Kronikol changes what
/// a scenario's id is computed from, a run computes each id the old way as well and writes the pairs that differ on its
/// line, which every reader applies as it applies the alias file beside the ledger. Until then such a change either cut
/// every scenario's history or had to keep the old computation forever, as the frozen keys do.
/// </summary>
public class HistoryIdAliasesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-id-aliases").FullName;
    private string Ledger => Path.Combine(_dir, "history.jsonl");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static Feature[] Features() =>
    [
        new Feature
        {
            DisplayName = "Checkout",
            Scenarios =
            [
                new Scenario { Id = "t1", DisplayName = "Pay by card", Result = ExecutionResult.Passed },
                new Scenario { Id = "t2", DisplayName = "Pay by voucher", Result = ExecutionResult.Passed }
            ]
        }
    ];

    /// <summary>An earlier way of computing an id: the scenario's name without the feature.</summary>
    private static string Earlier(string? suite, Feature feature, Scenario scenario) => ScenarioStableId.Compute(suite, "", scenario.DisplayName);

    private static HistoryBuildResult Build(params Func<string?, Feature, Scenario, string>[] earlier) =>
        HistoryRunBuilder.Build(Features(), [], "Suite", null, new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero),
            new HistoryBuildOptions { Durations = true, EarlierIds = earlier });

    private static HistoryRoster Roster(params string[] ids) =>
        HistoryRoster.Create("Suite", ids.Select((id, i) => new HistoryRosterEntry(id, "Pay " + i, "Checkout", null)).ToArray());

    private static HistoryRun Line(HistoryRoster roster, int n, string results, IReadOnlyDictionary<string, string>? aliases = null, bool? partial = false) => new()
    {
        Id = $"gh:{n}:1", Suite = "Suite", Partial = partial, At = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero).AddHours(n),
        Branch = "main", Commit = "abc1234", Provider = "GitHubActions", Url = null, Shards = 1, RosterHash = roster.Hash,
        Results = results, Attempts = new string('-', results.Length), Durations = results.Select(_ => (int?)100).ToArray(),
        Calls = null, ShapeSet = null, ShapeOrdered = null, Errors = results.Select(r => r == 'F' ? "e1" : null).ToArray(),
        ErrorText = results.Contains('F') ? new Dictionary<string, string> { ["e1"] = "boom" } : new Dictionary<string, string>(),
        Deps = [], Aliases = aliases
    };

    private HistoryLedger Seed(HistoryRoster roster, int runs)
    {
        for (var n = 1; n <= runs; n++)
            Assert.Equal(HistoryAppendOutcome.Appended, HistoryLedgerWriter.Append(Ledger, roster, Line(roster, n, "PP"), "5.17.0").Outcome);
        return HistoryLedgerReader.Read(Ledger, 50).Ledger!;
    }

    [Fact]
    public void A_run_writes_the_ids_an_earlier_rule_gave_its_scenarios()
    {
        var built = Build(Earlier);

        var aliases = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(built.Run.Aliases);
        Assert.Equal(2, aliases.Count);
        Assert.Equal(built.Roster.Ids[0], aliases[ScenarioStableId.Compute("Suite", "", "Pay by card")]);
        Assert.Equal(built.Roster.Ids[1], aliases[ScenarioStableId.Compute("Suite", "", "Pay by voucher")]);
    }

    [Fact]
    public void A_run_writes_no_aliases_while_every_id_is_computed_as_it_was()
    {
        Assert.Null(Build().Run.Aliases);
        Assert.Null(Build((suite, feature, scenario) => ScenarioStableId.For(suite, feature, scenario)).Run.Aliases);
        Assert.Empty(ScenarioStableId.EarlierRules);
    }

    [Fact]
    public void The_aliases_ride_on_the_run_line()
    {
        var roster = Roster("aaaa000000000001");
        var line = Line(roster, 1, "P", new Dictionary<string, string> { ["bbbb000000000001"] = "aaaa000000000001" });

        var text = HistoryJson.RunLine(line);
        var read = Assert.Single(HistoryLedgerReader.Read(Write(roster, line), 50).Ledger!.Runs("Suite"));

        Assert.Contains("\"aliases\":{\"bbbb000000000001\":\"aaaa000000000001\"}", text, StringComparison.Ordinal);
        Assert.Equal("aaaa000000000001", read.Aliases!["bbbb000000000001"]);
        Assert.DoesNotContain("aliases", HistoryJson.RunLine(Line(roster, 1, "P")), StringComparison.Ordinal);
    }

    private string Write(HistoryRoster roster, HistoryRun line)
    {
        Assert.Equal(HistoryAppendOutcome.Appended, HistoryLedgerWriter.Append(Ledger, roster, line, "5.17.0").Outcome);
        return Ledger;
    }

    [Fact]
    public void A_changed_id_rule_keeps_every_scenarios_history()
    {
        var ledger = Seed(Roster("0ld0000000000001", "0ld0000000000002"), 3);
        var roster = Roster("new0000000000001", "new0000000000002");
        var current = Line(roster, 4, "FP", new Dictionary<string, string> { ["0ld0000000000001"] = "new0000000000001", ["0ld0000000000002"] = "new0000000000002" }, partial: null);

        var verdicts = HistoryAnalyzer.Analyse(ledger, roster, current, new HistoryAnalysisOptions { Window = 50, MinRuns = 2 });

        Assert.False(verdicts.Partial);
        Assert.Contains(HistoryVerdictKind.Broke, verdicts.Scenarios[0].Verdicts);
        Assert.DoesNotContain(verdicts.Scenarios, s => s.Has(HistoryVerdictKind.New));
    }

    [Fact]
    public void A_renamed_scenario_does_not_make_the_run_partial()
    {
        // The alias file's renames were applied to each scenario's history but not to the partial check, so renaming more
        // than a tenth of a suite read the next run as a partial one, whose durations are not compared.
        var ledger = Seed(Roster("aaaa000000000001", "aaaa000000000002"), 3);
        var roster = Roster("renamed000000001", "aaaa000000000002");
        var aliases = new HistoryAliases();
        aliases.Add("aaaa000000000001", "renamed000000001");

        var verdicts = HistoryAnalyzer.Analyse(ledger, roster, Line(roster, 4, "PP", partial: null), new HistoryAnalysisOptions { Window = 50, MinRuns = 2 }, aliases: aliases);

        Assert.False(verdicts.Partial);
    }

    [Fact]
    public void A_run_lines_aliases_read_past_a_name_given_twice_and_an_empty_one()
    {
        // A line edited by hand, or written by another tool: a name given twice is read once, its later value as a JSON
        // reader reads one, where it threw out of the whole read; a name or a value that is empty maps nothing, where it
        // threw out of the analysis.
        var roster = Roster("new0000000000001", "new0000000000002");
        var line = HistoryJson.RunLine(Line(roster, 1, "PP"));
        var edited = line[..^1] + ""","aliases":{"0ld1":"newA","0ld1":"new0000000000001","0ld2":"","":"new0000000000002","0ld3":" "}}""";

        var run = HistoryJson.Parse(edited).Run!;

        Assert.Equal(new Dictionary<string, string> { ["0ld1"] = "new0000000000001" }, run.Aliases);
        Assert.Equal("new0000000000001", HistoryAliases.With(null, [run])!.Apply(Roster("0ld1")).Ids[0]);
    }

    [Fact]
    public void A_streams_aliases_are_read_when_another_stream_crowds_the_suites_last_lines()
    {
        // Main's runs under the earlier ids, the run that changed them writing its aliases, then more of a branch's runs
        // than the window holds: main's next run is read against main's own window (#95), and so are the aliases. Without
        // them its scenarios have one earlier result where they have three, too few to read the flips as flaky.
        var old = Roster("0ld0000000000001", "0ld0000000000002");
        var roster = Roster("new0000000000001", "new0000000000002");
        Write(old, Line(old, 1, "PP"));
        Write(old, Line(old, 2, "PP"));
        Write(old, Line(old, 3, "FF"));
        Write(roster, Line(roster, 4, "PP", new Dictionary<string, string> { ["0ld0000000000001"] = "new0000000000001", ["0ld0000000000002"] = "new0000000000002" }));
        for (var n = 5; n <= 8; n++)
            Write(roster, Line(roster, n, "PP") with { Branch = "feature/menu" });
        var ledger = HistoryLedgerReader.Read(Ledger, 3).Ledger!;
        var current = Line(roster, 9, "FF");

        var verdicts = HistoryAnalyzer.Analyse(ledger, roster, current, new HistoryAnalysisOptions { Window = 3, MinRuns = 3 });

        Assert.All(verdicts.Scenarios, s => Assert.Contains(HistoryVerdictKind.Flaky, s.Verdicts));
    }

    [Fact]
    public void A_fold_keeps_every_shards_aliases()
    {
        var first = Roster("new0000000000001");
        var second = Roster("new0000000000002");

        var folded = Assert.Single(HistoryFold.Fold(
        [
            new HistoryFragment(HistoryFormat.Version, first, Line(first, 4, "P", new Dictionary<string, string> { ["0ld0000000000001"] = "new0000000000001" })),
            new HistoryFragment(HistoryFormat.Version, second, Line(second, 4, "P", new Dictionary<string, string> { ["0ld0000000000002"] = "new0000000000002" }))
        ]));

        Assert.Equal(2, folded.Run.Aliases!.Count);
    }
}
