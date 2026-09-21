using System.Net;
using Kronikol.History;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.History;

/// <summary>
/// #75 sections 1d and 1f (plans/HISTORY_VERDICT_NOISE_PLAN.md S5). Cache-key formats are the application's
/// own and no built-in rule covers them all, so a consumer can say what is variable in theirs
/// (<c>HistoryShapeTemplates</c>); and because a wrong or missing rule is otherwise silent, the evidence
/// says when two sets of calls differ only in what looks like an id.
/// </summary>
public class ShapeTemplateRulesTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    // ─── The rules ──────────────────────────────────────────

    [Fact]
    public void A_rule_runs_before_the_built_in_ones()
    {
        // The built-in number rule would have made this app:_report_{n}-Weekly-eu; the consumer's rule takes
        // the whole tail first, and the built-in rules then see only what it left.
        var rules = HistoryShapeRules.Create([new HistoryShapeTemplate(@"(?<=app:_report_)\d+-\w+-\w+", "{tenant-report}")]);

        Assert.Equal("/cache/app:_report_{tenant-report}/{n}", InteractionShape.Template("/cache/app:_report_100000000000001-Weekly-eu/4711", rules));
        Assert.Equal("/cache/app:_report_{n}-Weekly-eu/{n}", InteractionShape.Template("/cache/app:_report_100000000000001-Weekly-eu/4711"));
    }

    [Fact]
    public void Rules_apply_in_the_order_given_and_a_placeholder_may_use_the_match()
    {
        var rules = HistoryShapeRules.Create(
        [
            new HistoryShapeTemplate(@"key-([a-z]+)-\w+", "key-$1-{rest}"),
            new HistoryShapeTemplate(@"key-orders-\{rest\}", "{orders-key}"),
        ]);

        Assert.Equal("/k/{orders-key}", InteractionShape.Template("/k/key-orders-zzz", rules));
        Assert.Equal("/k/key-users-{rest}", InteractionShape.Template("/k/key-users-zzz", rules));
    }

    [Fact]
    public void A_rule_reaches_a_statement_head_too()
    {
        var rules = HistoryShapeRules.Create([new HistoryShapeTemplate(@"tmp_[a-z]+_view", "{temp-view}")]);

        Assert.Equal("SELECT * FROM {temp-view}", InteractionShape.TemplateStatement("SELECT * FROM tmp_qwerty_view", rules));
    }

    [Fact]
    public void A_pattern_that_does_not_compile_is_skipped_and_said_and_the_others_still_apply()
    {
        var rules = HistoryShapeRules.Create([new HistoryShapeTemplate("(unclosed", "{x}"), new HistoryShapeTemplate("zzz", "{z}")]);

        Assert.NotNull(rules);
        Assert.Equal("/a/{z}", InteractionShape.Template("/a/zzz", rules));
        var problem = Assert.Single(rules.Problems);
        Assert.Contains("(unclosed", problem);
        Assert.Contains("skipped", problem);
    }

    [Fact]
    public void A_rule_that_times_out_is_skipped_for_that_text_and_said_once()
    {
        // The regexes are the consumer's. One that backtracks for ever must cost a rule, never a test run.
        var rules = HistoryShapeRules.Create([new HistoryShapeTemplate("(q+)+$", "{q}"), new HistoryShapeTemplate("zzz", "{z}")], TimeSpan.FromMilliseconds(20));
        var evil = "/x/" + new string('q', 40) + "!/zzz";

        Assert.Equal("/x/" + new string('q', 40) + "!/{z}", InteractionShape.Template(evil, rules));
        InteractionShape.Template(evil, rules);

        var problem = Assert.Single(rules!.Problems);
        Assert.Contains("(q+)+$", problem);
        Assert.Contains("timed out", problem);
    }

    [Fact]
    public void The_rules_have_a_hash_and_no_rules_have_none()
    {
        var one = HistoryShapeRules.Create([new HistoryShapeTemplate("a", "{a}"), new HistoryShapeTemplate("b", "{b}")])!;
        var same = HistoryShapeRules.Create([new HistoryShapeTemplate("a", "{a}"), new HistoryShapeTemplate("b", "{b}")])!;
        var reordered = HistoryShapeRules.Create([new HistoryShapeTemplate("b", "{b}"), new HistoryShapeTemplate("a", "{a}")])!;

        Assert.Equal(one.Hash, same.Hash);
        Assert.NotEqual(one.Hash, reordered.Hash);
        Assert.Matches("^[0-9a-f]{8}$", one.Hash);
        Assert.Null(HistoryShapeRules.Create([]));
        Assert.Null(HistoryShapeRules.Create(null));
    }

    // ─── The run line ───────────────────────────────────────

    private static RequestResponseLog[] Call(string testId, string uri)
    {
        var pair = Guid.NewGuid();
        return
        [
            new RequestResponseLog("Scenario", testId, HttpMethod.Get, null, new Uri("http://cache" + uri), [], "cache", "Api", RequestResponseType.Request, Guid.NewGuid(), pair, TrackingIgnore: false),
            new RequestResponseLog("Scenario", testId, HttpMethod.Get, null, new Uri("http://cache/"), [], "cache", "Api", RequestResponseType.Response, Guid.NewGuid(), pair, TrackingIgnore: false, StatusCode: HttpStatusCode.OK),
        ];
    }

    private static Feature[] OneScenario() => [new Feature { DisplayName = "Cache", Scenarios = [new Scenario { Id = "t1", DisplayName = "Reads a report", Result = ExecutionResult.Passed }] }];

    [Fact]
    public void A_run_records_which_rules_made_its_fingerprints_and_they_make_two_keys_one_call()
    {
        var rules = HistoryShapeRules.Create([new HistoryShapeTemplate(@"rep_[A-Za-z]+", "rep_{key}")]);
        var first = HistoryRunBuilder.Build(OneScenario(), Call("t1", "/rep_qwerty"), "Suite", null, At, new HistoryBuildOptions { ShapeRules = rules });
        var second = HistoryRunBuilder.Build(OneScenario(), Call("t1", "/rep_asdfgh"), "Suite", null, At, new HistoryBuildOptions { ShapeRules = rules });
        var plain = HistoryRunBuilder.Build(OneScenario(), Call("t1", "/rep_qwerty"), "Suite", null, At, new HistoryBuildOptions());

        Assert.Equal(rules!.Hash, first.Run.ShapeRules);
        Assert.Equal(first.Run.ShapeSetAt(0), second.Run.ShapeSetAt(0));
        Assert.Equal(["Api>cache GET /rep_{key} 200"], first.Shapes!.Calls);
        Assert.Null(plain.Run.ShapeRules);
        Assert.NotEqual(plain.Run.ShapeSetAt(0), first.Run.ShapeSetAt(0));
    }

    [Fact]
    public void The_rules_hash_rides_on_the_run_line_and_a_line_without_one_reads_as_none()
    {
        var rules = HistoryShapeRules.Create([new HistoryShapeTemplate("a", "{a}")]);
        var (_, run, _) = HistoryRunBuilder.Build(OneScenario(), Call("t1", "/x"), "Suite", null, At, new HistoryBuildOptions { ShapeRules = rules });

        var line = HistoryJson.RunLine(run);
        Assert.Contains($"\"shapeVersion\":{InteractionShape.Version},\"shapeRules\":\"{rules!.Hash}\"", line);

        var roster = HistoryRoster.Create("Suite", [new HistoryRosterEntry("aaaa000000000001", "Reads a report", "Cache", null)]);
        var ledger = HistoryLedgerReader.Parse(HistoryJson.HeaderLine("t") + "\n" + HistoryJson.RosterLine(roster) + "\n" + line.Replace(run.RosterHash, roster.Hash) + "\n", 0).Ledger!;
        Assert.Equal(rules.Hash, ledger.LatestRun("Suite")!.ShapeRules);

        var (_, plain, _) = HistoryRunBuilder.Build(OneScenario(), Call("t1", "/x"), "Suite", null, At, new HistoryBuildOptions());
        Assert.DoesNotContain("shapeRules", HistoryJson.RunLine(plain));
    }

    // ─── The analyzer ───────────────────────────────────────

    private static readonly string[] Ids = ["eeee000000000001", "eeee000000000002"];

    private static HistoryRoster Roster() =>
        HistoryRoster.Create("Suite", Ids.Select(id => new HistoryRosterEntry(id, "Scenario " + id[^1], "Feature", null)).ToArray());

    private static HistoryRun Run(HistoryRoster roster, int n, string shape, string? rules, HistoryShapes? shapes = null, string[]? calls = null) => new()
    {
        Id = $"local:{n}", Suite = roster.Suite, Partial = false, At = At.AddHours(n), Branch = null, Commit = null, Provider = null, Url = null, Shards = 1,
        RosterHash = roster.Hash, Results = "PP", Attempts = "--", Durations = null, Errors = new string?[2],
        ShapeSet = [shape, "cccccccc"], ShapeOrdered = [shape, "cccccccc"], Calls = [calls?.Length ?? 1, 1], ShapeVersion = InteractionShape.Version, ShapeRules = rules,
        ShapesHash = shapes?.Hash,
        CallSets = shapes is null ? null : [calls!.Select(line => ((IList<string>)shapes.Calls).IndexOf(line)).OrderBy(i => i).ToArray(), []],
        ErrorText = new Dictionary<string, string>(), Deps = []
    };

    private static HistoryLedger Ledger(HistoryRoster roster, IEnumerable<HistoryRun> runs, params HistoryShapes[] shapes) =>
        HistoryLedgerReader.Parse(HistoryJson.HeaderLine("t") + "\n" + string.Concat(shapes.Select(s => HistoryJson.ShapesLine(s) + "\n")) + HistoryJson.RosterLine(roster) + "\n"
                                  + string.Concat(runs.Select(r => HistoryJson.RunLine(r) + "\n")), 50).Ledger!;

    [Fact]
    public void A_change_of_rules_costs_one_quiet_run_instead_of_flagging_every_scenario()
    {
        var roster = Roster();
        var before = Enumerable.Range(1, 3).Select(n => Run(roster, n, "aaaaaaaa", rules: null)).ToList();

        // The first run under the new rules: every fingerprint moved, and none of it is behaviour.
        var first = HistoryAnalyzer.Analyse(Ledger(roster, before), roster, Run(roster, 4, "bbbbbbbb", rules: "11112222"), new HistoryAnalysisOptions()).Scenarios[0];
        Assert.DoesNotContain(HistoryVerdictKind.BehaviourChanged, first.Verdicts);
        Assert.Contains("fingerprinted by an earlier rule", first.Evidence);

        // The second compares with the first, and a real change under the same rules is still one.
        before.Add(Run(roster, 4, "bbbbbbbb", rules: "11112222"));
        var same = HistoryAnalyzer.Analyse(Ledger(roster, before), roster, Run(roster, 5, "bbbbbbbb", rules: "11112222"), new HistoryAnalysisOptions()).Scenarios[0];
        var changed = HistoryAnalyzer.Analyse(Ledger(roster, before), roster, Run(roster, 5, "dddddddd", rules: "11112222"), new HistoryAnalysisOptions()).Scenarios[0];
        Assert.Equal("stable", same.VerdictNames);
        Assert.Contains(HistoryVerdictKind.BehaviourChanged, changed.Verdicts);
    }

    // ─── The shard fold ─────────────────────────────────────

    [Fact]
    public void The_fold_carries_the_rules_and_refuses_to_pick_between_shards_that_disagree()
    {
        var a = HistoryRoster.Create("Suite", [new HistoryRosterEntry(Ids[0], "Scenario 1", "Feature", null)]);
        var b = HistoryRoster.Create("Suite", [new HistoryRosterEntry(Ids[1], "Scenario 2", "Feature", null)]);
        HistoryRun Shard(HistoryRoster roster, string? rules) => new()
        {
            Id = "gh:7:1", Suite = "Suite", Partial = false, At = At, Branch = "main", Commit = "c", Provider = "GitHubActions", Url = null, Shards = 1,
            RosterHash = roster.Hash, Results = "P", Attempts = "-", Durations = [10], Errors = new string?[1],
            ShapeSet = ["aaaaaaaa"], ShapeOrdered = ["aaaaaaaa"], Calls = [1], ShapeVersion = InteractionShape.Version, ShapeRules = rules,
            ErrorText = new Dictionary<string, string>(), Deps = []
        };

        var agreed = Assert.Single(HistoryFold.Fold([new HistoryFragment(1, a, Shard(a, "11112222")), new HistoryFragment(1, b, Shard(b, "11112222"))]));
        Assert.Equal("11112222", agreed.Run.ShapeRules);
        Assert.NotNull(agreed.Run.ShapeSet);
        Assert.Null(agreed.Note);

        // Eight shards configured differently must not fold into one line claiming one rule: the
        // fingerprints are not comparable with each other, let alone with the next run's.
        var split = Assert.Single(HistoryFold.Fold([new HistoryFragment(1, a, Shard(a, "11112222")), new HistoryFragment(1, b, Shard(b, null))]));
        Assert.Null(split.Run.ShapeSet);
        Assert.Null(split.Run.ShapeOrdered);
        Assert.Null(split.Run.ShapeVersion);
        Assert.Null(split.Run.ShapeRules);
        Assert.Equal("PP", split.Run.Results);
        Assert.NotNull(split.Run.Durations);
        Assert.Contains("templating rules", split.Note);
        Assert.Contains("gh:7:1", split.Note);
    }

    // ─── The hint ───────────────────────────────────────────

    [Fact]
    public void Calls_that_differ_only_in_what_looks_like_an_id_say_so_and_the_verdict_stands()
    {
        var roster = Roster();
        var was = HistoryShapes.Create(["Api>cache GET /k/sess_ab12cd34xyz 200", "Api>db QUERY /orders 200"]);
        var now = HistoryShapes.Create(["Api>cache GET /k/sess_zz98yy76qrs 200", "Api>db QUERY /orders 200"]);
        var ledger = Ledger(roster, [Run(roster, 1, "aaaaaaaa", null, was, [.. was.Calls])], was, now);

        var scenario = HistoryAnalyzer.Analyse(ledger, roster, Run(roster, 2, "bbbbbbbb", null, now, [.. now.Calls]), new HistoryAnalysisOptions(), shapes: now).Scenarios[0];

        // Still a change: the mask cannot tell a missed id from /v2/ becoming /v3/, which pairs one to one too.
        Assert.Contains(HistoryVerdictKind.BehaviourChanged, scenario.Verdicts);
        Assert.Contains("the calls differ only in what looks like an id (sess_ab1… → sess_zz9…): a HistoryShapeTemplates rule would make them compare equal", scenario.Evidence);
    }

    [Fact]
    public void A_changed_status_or_a_new_route_is_not_an_id()
    {
        var roster = Roster();
        var was = HistoryShapes.Create(["Api>cache GET /k/orders 200"]);
        var status = HistoryShapes.Create(["Api>cache GET /k/orders 404"]);
        var route = HistoryShapes.Create(["Api>cache GET /k/customers 200"]);
        var ledger = Ledger(roster, [Run(roster, 1, "aaaaaaaa", null, was, [.. was.Calls])], was, status, route);

        foreach (var list in new[] { status, route })
        {
            var scenario = HistoryAnalyzer.Analyse(ledger, roster, Run(roster, 2, "bbbbbbbb", null, list, [.. list.Calls]), new HistoryAnalysisOptions(), shapes: list).Scenarios[0];
            Assert.Contains(HistoryVerdictKind.BehaviourChanged, scenario.Verdicts);
            Assert.DoesNotContain("looks like an id", scenario.Evidence);
        }
    }
}
