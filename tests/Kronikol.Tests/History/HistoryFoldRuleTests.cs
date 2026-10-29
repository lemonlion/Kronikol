using System.Net;
using Kronikol.History;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.History;

/// <summary>
/// A fold builds one run line from several (the attempts of a retried run, the shards of a sharded one), and a line
/// claims one rule for its fingerprints. Until #192 R1 a fold of attempts dropped the consumer's rules (3.9), and a fold
/// of shards made under two rules, one shard on an older package, blanked its fingerprints. A shard made under the
/// newer rule bridges the older (plans/CALL_OPERATION_AND_ORIGIN_PLAN.md 4.8), so the fold records the older rule,
/// through that bridge, and keeps every fingerprint.
/// </summary>
public class HistoryFoldRuleTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);
    private const string Menu = """{"query":"query GetMenu { menu { items } }"}""";

    private static Feature[] Features(params string[] ids) =>
    [
        new Feature
        {
            DisplayName = "Menu",
            Scenarios = ids.Select(id => new Scenario { Id = id, DisplayName = "Scenario " + id, Result = ExecutionResult.Passed }).ToArray()
        }
    ];

    /// <summary>A GraphQL POST: rule 8 names its operation, rule 7 did not, so the two rules make different lines.</summary>
    private static RequestResponseLog[] GraphQl(string testId)
    {
        var pair = Guid.NewGuid();
        var uri = new Uri("http://menu/graphql");
        return
        [
            new RequestResponseLog("t", testId, HttpMethod.Post, Menu, uri, [], "Menu", "Caller", RequestResponseType.Request, Guid.NewGuid(), pair, TrackingIgnore: false),
            new RequestResponseLog("t", testId, HttpMethod.Post, "{}", uri, [], "Menu", "Caller", RequestResponseType.Response, Guid.NewGuid(), pair, TrackingIgnore: false, StatusCode: HttpStatusCode.OK)
        ];
    }

    /// <summary>A run this build records: rule 8, bridging 6 and 7.</summary>
    private static HistoryFragment Now(string runId, Feature[] features, HistoryShapeRules? rules = null, params RequestResponseLog[][] calls)
    {
        var built = HistoryRunBuilder.Build(features, calls.SelectMany(c => c).ToArray(), "Suite", null, At, new HistoryBuildOptions { Bridges = true, ShapeRules = rules }, runId);
        return new HistoryFragment(HistoryFormat.Version, built.Roster, built.Run, built.Shapes);
    }

    /// <summary>The same calls as 5.16.1 recorded them: rule 7, its own fingerprints, and its bridge for rule 6.</summary>
    private static HistoryFragment Then(string runId, Feature[] features, params RequestResponseLog[][] calls)
    {
        var now = Now(runId, features, null, calls);
        var under7 = now.Run.Bridge!["7"];
        return now with
        {
            Run = now.Run with
            {
                ShapeVersion = 7,
                ShapeSet = under7.ShapeSet ?? now.Run.ShapeSet,
                ShapeOrdered = under7.ShapeOrdered ?? now.Run.ShapeOrdered,
                CallSets = under7.CallSets ?? now.Run.CallSets,
                ShapesHash = under7.ShapesHash ?? now.Run.ShapesHash,
                // As a 5.16.1 fragment bridges every rule it kept: 6, which templates these calls as 7 does.
                Bridge = new Dictionary<string, HistoryBridge>(StringComparer.Ordinal) { ["6"] = new HistoryBridge() }
            },
            Shapes = under7.Shapes ?? now.Shapes
        };
    }

    [Fact]
    public void A_retried_run_keeps_its_consumer_rules()
    {
        var rules = HistoryShapeRules.Create([new HistoryShapeTemplate("menu-[a-z]+", "{menu}")]);
        var first = Now("gh:1:1", Features("a"), rules, GraphQl("a"));
        var retry = Now("gh:1:1", Features("a"), rules, GraphQl("a"));

        var folded = HistoryFold.Attempts([first, retry]).Run;

        Assert.Equal(rules!.Hash, folded.ShapeRules);
        Assert.Equal(11, folded.ShapeVersion);
    }

    [Fact]
    public void Attempts_on_two_rules_fold_under_the_one_both_reach()
    {
        // The first attempt on 5.16.1, the retry after the package moved: the line claims rule 7, and the retried
        // scenario's fingerprint is its retry's under 7, through the retry's bridge, not its rule 8 one.
        var first = Then("gh:2:1", Features("a", "b"), GraphQl("a"), GraphQl("b"));
        var retry = Now("gh:2:1", Features("a"), null, GraphQl("a"));

        var folded = HistoryFold.Attempts([first, retry]).Run;

        Assert.Equal(7, folded.ShapeVersion);
        Assert.Equal(first.Run.ShapeSet![0], folded.ShapeSet![0]);
        Assert.NotEqual(retry.Run.ShapeSet![0], folded.ShapeSet[0]);
        Assert.Equal(first.Run.ShapeSet[1], folded.ShapeSet[1]);
        Assert.Null(folded.Bridge?.GetValueOrDefault("8"));
    }

    [Fact]
    public void A_fold_of_shards_on_two_versions_keeps_its_fingerprints()
    {
        var older = Then("gh:3:1", Features("a"), GraphQl("a"));
        var newer = Now("gh:3:1", Features("b"), null, GraphQl("b"));

        var folded = Assert.Single(HistoryFold.Fold([older, newer]));

        Assert.Null(folded.Note);
        Assert.Equal(7, folded.Run.ShapeVersion);
        Assert.Equal([older.Run.ShapeSet![0], newer.Run.Bridge!["7"].ShapeSet![0]], folded.Run.ShapeSet);
        // Rule 6 both shards reach as well, each through its bridge, so the folded line bridges it.
        Assert.Contains("6", folded.Run.Bridge!.Keys);
    }

    /// <summary>A run recorded with <c>HistoryShapes</c> off: no fingerprints, so no rule and no bridge.</summary>
    private static HistoryFragment Unshaped(string runId, Feature[] features, params RequestResponseLog[][] calls)
    {
        var built = HistoryRunBuilder.Build(features, calls.SelectMany(c => c).ToArray(), "Suite", null, At, new HistoryBuildOptions { Shapes = false }, runId);
        return new HistoryFragment(HistoryFormat.Version, built.Roster, built.Run, built.Shapes);
    }

    [Fact]
    public void A_shard_without_fingerprints_folds_beside_one_that_has_them()
    {
        // The fold's rule and bridges are the fingerprinted shards'; a shard with none has none under any of them, and
        // its positions are recorded empty under each, where reading its bridge for them threw.
        var shaped = Now("gh:5:1", Features("a"), null, GraphQl("a"));
        var plain = Unshaped("gh:5:1", Features("b"), GraphQl("b"));

        var folded = Assert.Single(HistoryFold.Fold([shaped, plain]));

        Assert.Equal(11, folded.Run.ShapeVersion);
        Assert.Equal([shaped.Run.ShapeSet![0], ""], folded.Run.ShapeSet);
        Assert.Equal(shaped.Run.Bridge!["7"].ShapeSet?[0] ?? shaped.Run.ShapeSet[0], folded.Run.Bridge!["7"].ShapeSet?[0] ?? folded.Run.ShapeSet![0]);
    }

    [Fact]
    public void An_attempt_without_fingerprints_folds_beside_one_that_has_them()
    {
        var first = Unshaped("gh:6:1", Features("a", "b"), GraphQl("a"), GraphQl("b"));
        var retry = Now("gh:6:1", Features("a"), null, GraphQl("a"));

        var folded = HistoryFold.Attempts([first, retry]).Run;

        Assert.Equal(11, folded.ShapeVersion);
        Assert.Equal([retry.Run.ShapeSet![0], ""], folded.ShapeSet);
    }

    [Fact]
    public void Shards_that_share_no_rule_still_record_no_fingerprints_and_say_so()
    {
        var plain = Now("gh:4:1", Features("a"), null, GraphQl("a"));
        var ruled = Now("gh:4:1", Features("b"), HistoryShapeRules.Create([new HistoryShapeTemplate("menu-[a-z]+", "{menu}")]), GraphQl("b"));

        var folded = Assert.Single(HistoryFold.Fold([plain, ruled]));

        Assert.Null(folded.Run.ShapeSet);
        Assert.Contains("different templating rules", folded.Note, StringComparison.Ordinal);
    }
}
