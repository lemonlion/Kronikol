using Kronikol.History;
using Kronikol.Tool;

namespace Kronikol.Tests.Tool;

/// <summary>
/// #84 explained a near-miss by a bar the scenario had met, and on a real CI ledger that explanation
/// would have been wrong every time it was printed: the reason was one failing episode for every
/// near-miss in it. This replays a real ledger - one suite of a consumer's CI history, trimmed to what the
/// status verdicts read - run by run, each read in place against the runs before it, and holds two things:
/// the analyzer's <see cref="HistoryFlakyShortfall"/> is what an independent count of the scenario's own
/// points gives, and the hint the tool prints never names a reason the analyzer did not.
/// </summary>
public class QueryHistoryShortfallReplayTests
{
    private const string Suite = "xunit-in-docker";

    private static readonly string LedgerPath = Path.Combine(AppContext.BaseDirectory, "TestData", "History", "ci-ledger.xunit-in-docker.jsonl");

    [Fact]
    public void The_shortfall_the_analyzer_records_is_what_the_scenario_s_own_points_say()
    {
        var ledger = HistoryLedgerReader.Parse(File.ReadAllText(LedgerPath), 0).Ledger!;
        var options = new HistoryAnalysisOptions();
        var nearMisses = 0;

        foreach (var run in ledger.Runs(Suite))
        {
            var verdicts = HistoryAnalyzer.Analyse(ledger, ledger.Roster(run.RosterHash)!, run, options);
            foreach (var entry in verdicts.Scenarios)
            {
                var real = entry.Points.Where(p => HistoryFormat.IsRealVerdict(p.Result)).ToList();
                var flips = 0;
                var episodes = 0;
                for (var i = 0; i < real.Count; i++)
                {
                    if (i > 0 && real[i].Result != real[i - 1].Result) flips++;
                    if (real[i].Result == HistoryFormat.Failed && (i == 0 || real[i - 1].Result != HistoryFormat.Failed)) episodes++;
                }

                var flaky = entry.Verdicts.Contains(HistoryVerdictKind.Flaky);
                var expected = flaky || flips == 0
                    ? HistoryFlakyShortfall.None
                    : (episodes < 2 ? HistoryFlakyShortfall.OneEpisode : 0)
                      | (real.Count < options.MinRuns ? HistoryFlakyShortfall.TooFewVerdicts : 0)
                      | (entry.FlipRate < options.FlakyRate ? HistoryFlakyShortfall.BelowRate : 0);

                Assert.True(expected == entry.FlakyShortfall, $"{run.Id} {entry.StableId}: expected {expected}, the analyzer says {entry.FlakyShortfall} ({entry.Series})");
                Assert.Equal(episodes, entry.FailingEpisodes);
                if (entry.FlakyShortfall != HistoryFlakyShortfall.None)
                    nearMisses++;
            }
        }

        // The fixture has to be able to fail: three of its runs hold a failure, and each echoes for as
        // long as it stays in the window.
        Assert.True(nearMisses > 20, $"only {nearMisses} near-miss readings in the fixture");
    }

    [Fact]
    public void No_hint_names_a_reason_the_analyzer_did_not_give()
    {
        var ledger = HistoryLedgerReader.Parse(File.ReadAllText(LedgerPath), 0).Ledger!;
        var options = new HistoryAnalysisOptions();
        var hints = 0;

        foreach (var run in ledger.Runs(Suite))
        {
            var verdicts = HistoryAnalyzer.Analyse(ledger, ledger.Roster(run.RosterHash)!, run, options);
            var shortfalls = verdicts.Scenarios.Select(s => s.FlakyShortfall).Where(s => s != HistoryFlakyShortfall.None).ToList();

            var output = new StringWriter();
            var error = new StringWriter();
            var exit = QueryCommand.Run(["history", "--history", LedgerPath, "--run", run.Id, "--flaky"], output, error, _ => null, workingDirectory: Path.GetTempPath());
            Assert.True(exit == 0, error.ToString());
            var text = output.ToString();

            if (text.Contains("not flaky", StringComparison.Ordinal))
                hints++;
            // The bar and its flag are named only when the bar is the sole obstacle for some scenario.
            if (!shortfalls.Contains(HistoryFlakyShortfall.TooFewVerdicts))
                Assert.True(!text.Contains("--min-runs", StringComparison.Ordinal), $"{run.Id}: the hint names --min-runs and no scenario is held back by it alone\n{text}");
            if (!shortfalls.Any(s => s.HasFlag(HistoryFlakyShortfall.OneEpisode)))
                Assert.DoesNotContain("one failing episode", text);
            if (shortfalls.Count == 0)
                Assert.DoesNotContain("not flaky", text);
        }

        Assert.True(hints > 0, "no run of the fixture printed a near-miss hint");
    }
}
