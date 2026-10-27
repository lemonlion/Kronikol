using System.Net;
using Kronikol.History;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.History;

/// <summary>
/// #190 R0 renamed what the HTTP handler's fallback calls a remote host: <c>pricing.example.com:443</c>, where 5.12.1
/// recorded <c>localhost:443</c>. The callee is part of every call's fingerprint, so history bridges the change
/// (plans/CALL_OPERATION_AND_ORIGIN_PLAN.md §4.8): while runs fingerprinted under rule 6 are in the window, a run also
/// fingerprints its own calls under it, and is compared with them through that. The rename then reads no change, no
/// new dependency and no quiet run, while a real change across it still reads as one.
/// </summary>
public class HistoryBridgeTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);
    private const string Prices = "https://pricing.example.com/v1/prices";
    private const string Quotes = "https://pricing.example.com/v1/quotes";
    private const string Chat = "https://api.openai.example/v1/chat";
    private const string Orders = "http://localhost:5000/orders";

    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-bridge").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static Feature[] Features() =>
    [
        new Feature
        {
            DisplayName = "Pricing",
            Scenarios =
            [
                new Scenario { Id = "t1", DisplayName = "Quote a price", Result = ExecutionResult.Passed },
                new Scenario { Id = "t2", DisplayName = "List orders", Result = ExecutionResult.Passed }
            ]
        }
    ];

    /// <summary>One call and its answer, named as the handler named it.</summary>
    private static RequestResponseLog[] Call(string testId, string url, string service, bool namedByFallback)
    {
        var pair = Guid.NewGuid();
        var uri = new Uri(url);
        return
        [
            new RequestResponseLog("t", testId, HttpMethod.Get, null, uri, [], service, "Caller", RequestResponseType.Request, Guid.NewGuid(), pair, TrackingIgnore: false)
                { NamedByRemoteHostFallback = namedByFallback },
            new RequestResponseLog("t", testId, HttpMethod.Get, null, uri, [], service, "Caller", RequestResponseType.Response, Guid.NewGuid(), pair, TrackingIgnore: false, StatusCode: HttpStatusCode.OK)
                { NamedByRemoteHostFallback = namedByFallback }
        ];
    }

    /// <summary>A call to a remote host nothing maps, as 5.12.1 named it.</summary>
    private static RequestResponseLog[] Then(string testId, string url) => Call(testId, url, "localhost:" + new Uri(url).Port, namedByFallback: false);

    /// <summary>The same call as #190 names it.</summary>
    private static RequestResponseLog[] Now(string testId, string url) =>
        Call(testId, url, TestTrackingMessageHandler.FallbackName(new Uri(url)).Name, namedByFallback: true);

    private static RequestResponseLog[] Mapped(string testId, string url, string service) => Call(testId, url, service, namedByFallback: false);

    /// <summary>A run 5.12.1 recorded: no bridge, fingerprinted under rule 6 (whose templating is 7's).</summary>
    private static HistoryBuildResult Before(int n, params RequestResponseLog[][] calls)
    {
        var built = HistoryRunBuilder.Build(Features(), calls.SelectMany(c => c).ToArray(), "Suite", null, At.AddHours(n), new HistoryBuildOptions(), $"gh:{n}:1");
        return built with { Run = built.Run with { ShapeVersion = 6, Bridge = null } };
    }

    /// <summary>A run this build records in process: rule 7, bridging rule 6.</summary>
    private static HistoryBuildResult After(int n, params RequestResponseLog[][] calls) =>
        HistoryRunBuilder.Build(Features(), calls.SelectMany(c => c).ToArray(), "Suite", null, At.AddHours(n), new HistoryBuildOptions { Bridges = true }, $"gh:{n}:1");

    private string LedgerOf(params HistoryBuildResult[] runs)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".jsonl");
        foreach (var run in runs)
            Assert.Equal(HistoryAppendOutcome.Appended, HistoryLedgerWriter.Append(path, run.Roster, run.Run, "test", shapes: run.Shapes).Outcome);
        return path;
    }

    private static HistoryVerdicts Analyse(string ledgerPath, HistoryBuildResult current, HistoryAnalysisOptions? options = null) =>
        HistoryAnalyzer.Analyse(HistoryLedgerReader.Read(ledgerPath, 50).Ledger!, current.Roster, current.Run, options ?? new HistoryAnalysisOptions(), shapes: current.Shapes);

    private static ScenarioHistory Scenario(HistoryVerdicts verdicts, string name) => verdicts.Scenarios.Single(s => s.Name == name);

    private string ThreeRunsBefore() => LedgerOf(
        Before(1, Then("t1", Prices), Mapped("t2", Orders, "Orders API")),
        Before(2, Then("t1", Prices), Mapped("t2", Orders, "Orders API")),
        Before(3, Then("t1", Prices), Mapped("t2", Orders, "Orders API")));

    [Fact]
    public void A_host_the_fallback_renamed_reads_no_change_across_the_move()
    {
        var verdicts = Analyse(ThreeRunsBefore(), After(4, Now("t1", Prices), Mapped("t2", Orders, "Orders API")));

        var quote = Scenario(verdicts, "Quote a price");
        Assert.DoesNotContain(HistoryVerdictKind.BehaviourChanged, quote.Verdicts);
        Assert.DoesNotContain("earlier rule", quote.Evidence, StringComparison.Ordinal);
        Assert.NotNull(quote.PreviousShapeSet);
        Assert.Empty(verdicts.NewDependencies);
    }

    [Fact]
    public void Without_a_bridge_the_move_reads_no_verdict_and_says_why()
    {
        // The run a writer with no bridge leaves (a run rebuilt from a report, an older fold): as every move read before.
        var current = After(4, Now("t1", Prices), Mapped("t2", Orders, "Orders API"));
        current = current with { Run = current.Run with { Bridge = null } };

        var quote = Scenario(Analyse(ThreeRunsBefore(), current), "Quote a price");

        Assert.DoesNotContain(HistoryVerdictKind.BehaviourChanged, quote.Verdicts);
        Assert.Contains("fingerprinted by an earlier rule", quote.Evidence, StringComparison.Ordinal);
        Assert.Null(quote.PreviousShapeSet);
    }

    [Fact]
    public void A_real_change_across_the_move_reads_changed_and_names_the_call()
    {
        var verdicts = Analyse(ThreeRunsBefore(), After(4, Now("t1", Prices), Now("t1", Quotes), Mapped("t2", Orders, "Orders API")));

        var quote = Scenario(verdicts, "Quote a price");
        Assert.Contains(HistoryVerdictKind.BehaviourChanged, quote.Verdicts);
        // Named as the rule both runs are compared under spells it.
        Assert.Equal(["Caller>localhost:443 GET /v1/quotes 200"], quote.NewCalls);
        Assert.DoesNotContain(HistoryVerdictKind.BehaviourChanged, Scenario(verdicts, "List orders").Verdicts);
    }

    [Fact]
    public void A_run_an_older_tool_writes_after_a_bridged_run_is_compared_through_that_runs_bridge()
    {
        // A lane still on 5.12.1 appends to a ledger a newer lane has bridged: its run is under rule 6 and the run before
        // it under 7, so the newer run's bridge is what they are compared through, and only the real change is named.
        var path = LedgerOf(
            Before(1, Then("t1", Prices), Mapped("t2", Orders, "Orders API")),
            Before(2, Then("t1", Prices), Mapped("t2", Orders, "Orders API")),
            Before(3, Then("t1", Prices), Mapped("t2", Orders, "Orders API")),
            After(4, Now("t1", Prices), Mapped("t2", Orders, "Orders API")));

        var verdicts = Analyse(path, Before(5, Then("t1", Prices), Then("t1", Quotes), Mapped("t2", Orders, "Orders API")));

        var quote = Scenario(verdicts, "Quote a price");
        Assert.Contains(HistoryVerdictKind.BehaviourChanged, quote.Verdicts);
        Assert.Equal(["Caller>localhost:443 GET /v1/quotes 200"], quote.NewCalls);
        Assert.Empty(quote.GoneCalls!);
        Assert.DoesNotContain(HistoryVerdictKind.BehaviourChanged, Scenario(verdicts, "List orders").Verdicts);
    }

    [Fact]
    public void A_renamed_party_is_not_a_new_dependency_and_a_new_one_still_is()
    {
        var verdicts = Analyse(ThreeRunsBefore(), After(4, Now("t1", Prices), Now("t1", "https://risk.example:8443/v1/score"), Mapped("t2", Orders, "Orders API")));

        Assert.Equal(["Caller>risk.example:8443"], verdicts.NewDependencies);
    }

    [Fact]
    public void A_run_an_older_tool_writes_reads_a_bridged_runs_dependencies_as_its_rule_spells_them()
    {
        // Only the bridged run called the pricing service before, and an older tool's run after it calls it too: that
        // pair is the bridged run's, spelt as rule 6 spells it, so it is not new again. A pair no run made still is.
        var path = LedgerOf(
            Before(1, Mapped("t2", Orders, "Orders API")),
            Before(2, Mapped("t2", Orders, "Orders API")),
            After(3, Now("t1", Prices), Mapped("t2", Orders, "Orders API")));

        var verdicts = Analyse(path, Before(4, Then("t1", Prices), Then("t1", "https://risk.example:8443/v1/score"), Mapped("t2", Orders, "Orders API")));

        Assert.Equal(["Caller>localhost:8443"], verdicts.NewDependencies);
    }

    [Fact]
    public void A_new_host_on_the_port_of_a_renamed_one_is_not_new_on_the_first_run_across_the_move()
    {
        // The limit of reading across the move as rule 6 read: 6 called every remote host on 443 localhost:443, so a new
        // one there is the party the old runs already had, and the next run has this one in its history. What it does is
        // still news: the scenario that calls it reads changed, naming the call.
        var verdicts = Analyse(ThreeRunsBefore(), After(4, Now("t1", Prices), Now("t1", "https://risk.example/v1/score"), Mapped("t2", Orders, "Orders API")));

        Assert.Empty(verdicts.NewDependencies);
        var quote = Scenario(verdicts, "Quote a price");
        Assert.Contains(HistoryVerdictKind.BehaviourChanged, quote.Verdicts);
        Assert.Equal(["Caller>localhost:443 GET /v1/score 200"], quote.NewCalls);
    }

    [Fact]
    public void Two_hosts_that_were_one_party_compare_as_one_across_the_move()
    {
        // 5.12.1 called both localhost:443. Rule 7 sees two parties; across the move they are compared as rule 6 saw them,
        // so splitting them is not a change.
        var ledger = LedgerOf(
            Before(1, Then("t1", Prices), Then("t1", Chat), Mapped("t2", Orders, "Orders API")),
            Before(2, Then("t1", Prices), Then("t1", Chat), Mapped("t2", Orders, "Orders API")));

        var quote = Scenario(Analyse(ledger, After(3, Now("t1", Prices), Now("t1", Chat), Mapped("t2", Orders, "Orders API"))), "Quote a price");

        Assert.DoesNotContain(HistoryVerdictKind.BehaviourChanged, quote.Verdicts);
        Assert.DoesNotContain("earlier rule", quote.Evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void The_alternating_memory_survives_the_move()
    {
        // A cache warm or cold: two sets of calls, each the scenario's own. Read across the move, the return to the first
        // set is the alternation it was, not a change, and not a quiet run that forgets the second set.
        var cold = new[] { Then("t1", Prices), Then("t1", Quotes) };
        var warm = new[] { Then("t1", Prices) };
        var ledger = LedgerOf(
            Before(1, [.. cold, Mapped("t2", Orders, "Orders API")]),
            Before(2, [.. warm, Mapped("t2", Orders, "Orders API")]),
            Before(3, [.. cold, Mapped("t2", Orders, "Orders API")]),
            Before(4, [.. warm, Mapped("t2", Orders, "Orders API")]));

        var quote = Scenario(Analyse(ledger, After(5, Now("t1", Prices), Now("t1", Quotes), Mapped("t2", Orders, "Orders API"))), "Quote a price");

        Assert.Contains(HistoryVerdictKind.Alternating, quote.Verdicts);
        Assert.DoesNotContain(HistoryVerdictKind.BehaviourChanged, quote.Verdicts);
    }

    [Fact]
    public void Runs_under_both_rules_interleaved_are_not_an_unstable_shape()
    {
        // Two lanes of one suite on two packages: 5.12.1 and this build take turns. Every pair of earlier runs is compared
        // through the bridge the later one wrote, so the same calls make no change between them.
        var ledger = LedgerOf(
            Before(1, Then("t1", Prices), Mapped("t2", Orders, "Orders API")),
            After(2, Now("t1", Prices), Mapped("t2", Orders, "Orders API")),
            Before(3, Then("t1", Prices), Mapped("t2", Orders, "Orders API")),
            After(4, Now("t1", Prices), Mapped("t2", Orders, "Orders API")),
            Before(5, Then("t1", Prices), Mapped("t2", Orders, "Orders API")));

        var quote = Scenario(Analyse(ledger, After(6, Now("t1", Prices), Mapped("t2", Orders, "Orders API")), new HistoryAnalysisOptions { MinRuns = 3 }), "Quote a price");

        Assert.DoesNotContain(HistoryVerdictKind.UnstableShape, quote.Verdicts);
        Assert.DoesNotContain(HistoryVerdictKind.BehaviourChanged, quote.Verdicts);
        Assert.Contains(HistoryVerdictKind.Stable, quote.Verdicts);
    }

    [Fact]
    public void A_run_the_change_does_not_touch_carries_an_empty_bridge()
    {
        var run = After(1, Mapped("t1", Prices, "Pricing"), Mapped("t2", Orders, "Orders API")).Run;

        var bridge = Assert.Single(run.Bridge!);
        Assert.Equal("6", bridge.Key);
        Assert.True(bridge.Value.IsEmpty);
        Assert.Contains("\"bridge\":{\"6\":{}}", HistoryJson.RunLine(run), StringComparison.Ordinal);
    }

    [Fact]
    public void A_bridge_holds_only_what_the_rule_changes()
    {
        var run = After(1, Now("t1", Prices), Mapped("t2", Orders, "Orders API")).Run;

        var bridge = run.Bridge!["6"];
        Assert.Equal(2, bridge.ShapeSet!.Count);
        Assert.NotEqual(run.ShapeSet![0], bridge.ShapeSet[0]);
        Assert.Equal(run.ShapeSet[1], bridge.ShapeSet[1]);
        Assert.Contains("Caller>localhost:443 GET /v1/prices 200", bridge.Shapes!.Calls);
        Assert.Equal(new Dictionary<string, string> { ["Caller>pricing.example.com:443"] = "Caller>localhost:443" }, bridge.Deps);
    }

    [Fact]
    public void A_bridge_survives_the_line_and_the_fragment()
    {
        var built = After(1, Now("t1", Prices), Mapped("t2", Orders, "Orders API"));

        var parsed = HistoryJson.Parse(HistoryJson.RunLine(built.Run)).Run!;
        var bridge = parsed.Bridge!["6"];
        Assert.Equal(built.Run.Bridge!["6"].ShapeSet, bridge.ShapeSet);
        Assert.Equal(built.Run.Bridge["6"].ShapeOrdered, bridge.ShapeOrdered);
        Assert.Equal(built.Run.Bridge["6"].ShapesHash, bridge.ShapesHash);
        Assert.Equal(built.Run.Bridge["6"].CallSets!.Select(s => s.ToArray()), bridge.CallSets!.Select(s => s.ToArray()));
        Assert.Equal(built.Run.Bridge["6"].Deps, bridge.Deps);
        Assert.Null(bridge.Shapes);

        var fragment = HistoryFragment.Parse(HistoryFragment.Write(built.Roster, built.Run, "test", built.Shapes));
        Assert.Equal(built.Run.Bridge["6"].Shapes!.Calls, fragment.Run.Bridge!["6"].Shapes!.Calls);
    }

    [Fact]
    public void The_ledger_holds_a_bridges_calls_and_verify_finds_it_sound()
    {
        var path = LedgerOf(After(1, Now("t1", Prices), Mapped("t2", Orders, "Orders API")));

        var ledger = HistoryLedgerReader.Read(path, 50).Ledger!;
        var line = ledger.Runs("Suite").Single();
        Assert.NotNull(ledger.Shapes(line.Bridge!["6"].ShapesHash!));
        Assert.Empty(HistoryLedgerReader.Verify(path));
    }

    [Fact]
    public void Verify_checks_a_bridges_length_against_its_roster()
    {
        var built = After(1, Now("t1", Prices), Mapped("t2", Orders, "Orders API"));
        var bridge = built.Run.Bridge!["6"];
        var broken = built with { Run = built.Run with { Bridge = new Dictionary<string, HistoryBridge> { ["6"] = bridge with { ShapeSet = [bridge.ShapeSet![0]] } } } };

        var findings = HistoryLedgerReader.Verify(LedgerOf(broken));

        Assert.Contains(findings, f => f.Contains("bridge for rule 6 has 1 shapes against a roster of 2", StringComparison.Ordinal));
    }

    [Fact]
    public void Prune_and_compact_keep_a_bridge_and_its_calls()
    {
        // The run before also asked for a quote, so the bridge's calls (rule 6's spelling of the second run's) are a
        // shapes line no run of its own references: only the bridge keeps it.
        var path = LedgerOf(Before(1, Then("t1", Prices), Then("t1", Quotes), Mapped("t2", Orders, "Orders API")), After(2, Now("t1", Prices), Mapped("t2", Orders, "Orders API")));
        var bridged = HistoryLedgerReader.Read(path, 50).Ledger!.Runs("Suite").Single(r => r.Id == "gh:2:1").Bridge!["6"].ShapesHash!;
        Assert.DoesNotContain(HistoryLedgerReader.Read(path, 50).Ledger!.Runs("Suite"), r => r.ShapesHash == bridged);

        HistoryLedgerWriter.Prune(path, 50, "test");
        HistoryLedgerWriter.Compact(path, 50, "test");

        Assert.Empty(HistoryLedgerReader.Verify(path));
        var ledger = HistoryLedgerReader.Read(path, 50).Ledger!;
        var after = ledger.Runs("Suite").Single(r => r.Id == "gh:2:1");
        Assert.Equal(bridged, after.Bridge!["6"].ShapesHash);
        Assert.NotNull(ledger.Shapes(bridged));
    }

    [Fact]
    public void A_fold_of_shards_carries_the_bridge_position_by_position()
    {
        var features = Features();
        HistoryFragment Shard(Scenario scenario, params RequestResponseLog[][] calls)
        {
            var shardFeatures = new[] { new Feature { DisplayName = features[0].DisplayName, Scenarios = [scenario] } };
            var built = HistoryRunBuilder.Build(shardFeatures, calls.SelectMany(c => c).ToArray(), "Suite", null, At, new HistoryBuildOptions { Bridges = true }, "gh:9:1");
            return HistoryFragment.Parse(HistoryFragment.Write(built.Roster, built.Run, "test", built.Shapes));
        }

        var folded = HistoryFold.Fold([Shard(features[0].Scenarios![0], Now("t1", Prices)), Shard(features[0].Scenarios![1], Mapped("t2", Orders, "Orders API"))]).Single();

        var whole = After(9, Now("t1", Prices), Mapped("t2", Orders, "Orders API"));
        var bridge = folded.Run.Bridge!["6"];
        Assert.Equal(whole.Run.Bridge!["6"].ShapeSet, bridge.ShapeSet);
        Assert.Equal(whole.Run.Bridge["6"].Shapes!.Calls, bridge.Shapes!.Calls);
        Assert.Equal(whole.Run.Bridge["6"].Deps, bridge.Deps);
    }

    [Fact]
    public void Attempts_of_one_run_carry_the_bridge_of_the_attempt_each_position_came_from()
    {
        var first = After(1, Then("t1", Prices), Mapped("t2", Orders, "Orders API"));
        var retry = HistoryRunBuilder.Build([new Feature { DisplayName = "Pricing", Scenarios = [new Scenario { Id = "t1", DisplayName = "Quote a price", Result = ExecutionResult.Passed }] }],
            Now("t1", Prices), "Suite", null, At.AddHours(2), new HistoryBuildOptions { Bridges = true }, "gh:1:1");

        var combined = HistoryFold.Attempts([
            new HistoryFragment(HistoryFormat.Version, first.Roster, first.Run, first.Shapes),
            new HistoryFragment(HistoryFormat.Version, retry.Roster, retry.Run, retry.Shapes)]);

        var bridge = combined.Run.Bridge!["6"];
        // The retried position is the retry's, renamed and bridged; the other is the first attempt's own.
        Assert.Equal(retry.Run.Bridge!["6"].ShapeSet![0], bridge.ShapeSet![0]);
        Assert.Equal(combined.Run.ShapeSet![1], bridge.ShapeSet[1]);
    }

    [Fact]
    public void A_run_appends_its_bridge_while_the_window_needs_it_and_its_fragment_always_carries_it()
    {
        RequestResponseLog[] logs = [.. Now("t1", Prices), .. Mapped("t2", Orders, "Orders API")];
        var path = LedgerOf(Before(1, Then("t1", Prices), Mapped("t2", Orders, "Orders API")));
        var options = new ReportConfigurationOptions { HistoryFilePath = path };

        // The window holds a run of rule 6: the line carries the bridge, the verdict reads through it.
        var first = HistoryRunContext.Create(Features(), logs, "Suite", null, At.AddHours(2), options, _dir, "test", _ => null, _dir)!;
        first.Append();
        Assert.DoesNotContain("earlier rule", first.Verdicts!.Scenarios.Single(s => s.Name == "Quote a price").Evidence, StringComparison.Ordinal);
        Assert.NotNull(HistoryLedgerReader.Read(path, 50).Ledger!.Runs("Suite")[^1].Bridge);

        // A ledger whose window holds only rule 7: the line carries none, the fragment still does.
        var fresh = LedgerOf(After(1, Now("t1", Prices), Mapped("t2", Orders, "Orders API")));
        var second = HistoryRunContext.Create(Features(), logs, "Suite", null, At.AddHours(3), new ReportConfigurationOptions { HistoryFilePath = fresh }, _dir, "test", _ => null, _dir)!;
        second.Append();
        Assert.Null(HistoryLedgerReader.Read(fresh, 50).Ledger!.Runs("Suite")[^1].Bridge);
        Assert.NotNull(HistoryFragment.Parse(second.Fragment()).Run.Bridge!["6"].Shapes);
        Assert.Empty(HistoryLedgerReader.Verify(path));
        Assert.Empty(HistoryLedgerReader.Verify(fresh));
    }

    [Fact]
    public void A_ledger_keeps_only_the_bridges_its_window_was_made_under()
    {
        var run = After(3, Now("t1", Prices), Mapped("t2", Orders, "Orders API")).Run;
        var older = Before(1, Then("t1", Prices), Mapped("t2", Orders, "Orders API")).Run;
        var sameRule = After(2, Now("t1", Prices), Mapped("t2", Orders, "Orders API")).Run;

        Assert.Equal(["6"], HistoryBridges.ForWindow(run, [older, sameRule]).Bridge!.Keys);
        Assert.Null(HistoryBridges.ForWindow(run, [sameRule]).Bridge);
    }
}
