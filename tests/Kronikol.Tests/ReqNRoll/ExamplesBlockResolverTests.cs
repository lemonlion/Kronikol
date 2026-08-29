using System.Collections.Specialized;
using System.Globalization;
using System.Reflection;
using Io.Cucumber.Messages.Types;
using Kronikol.ReqNRoll;
using Reqnroll;
using Reqnroll.Formatters.RuntimeSupport;

namespace Kronikol.Tests.ReqNRoll;

/// <summary>
/// <see cref="ExamplesBlockResolver"/>: maps a running scenario back to the <c>Examples:</c>
/// block it came from via the feature-level Cucumber messages Reqnroll's generated code embeds.
/// </summary>
public class ExamplesBlockResolverTests
{
    private const string OutlineName = "Market share movement is reported";

    private sealed class FakeMessages(GherkinDocument? document, List<Pickle> pickles) : IFeatureLevelCucumberMessages
    {
        public bool HasMessages => document is not null;
        public GherkinDocument GherkinDocument => document!;
        public IEnumerable<Pickle> Pickles => pickles;
        public Source Source => null!;
    }

    private static Location Loc => new(1, 1);

    private static TableRow Row(string id, params string[] cells) =>
        new(Loc, cells.Select(c => new TableCell(Loc, c)).ToList(), id);

    private static Examples Block(string id, string name, string description, params TableRow[] rows) =>
        new(Loc, [], "Examples", name, description, Row($"{id}-header", "Period", "Change"), rows.ToList(), id);

    private static Pickle OutlinePickle(string id, string scenarioAstId, string rowAstId) =>
        new(id, "market-share.feature", OutlineName, "en", [], [], [scenarioAstId, rowAstId]);

    /// <summary>
    /// The fake mirrors the shape Reqnroll embeds: a plain scenario (pickle 0), a three-block
    /// outline (pickles 1-4) and a rule-nested outline (pickle 5).
    /// </summary>
    private static FeatureInfo BuildFeatureInfo()
    {
        var outline = new Scenario(Loc, [], "Scenario Outline", OutlineName, "", [],
            [
                Block("ex-0", "the merchant gained share", "  only upward movement", Row("row-0a", "OneWeek", "2.50"), Row("row-0b", "OneYear", "5.00")),
                Block("ex-1", "the merchant lost share", "", Row("row-1a", "FourWeeks", "-2.50")),
                Block("ex-2", "", "", Row("row-2a", "RollingYear", "0.00"))
            ], "outline-1");

        var plain = new Scenario(Loc, [], "Scenario", "A plain scenario", "", [], [], "plain-1");

        var ruled = new Scenario(Loc, [], "Scenario Outline", "Ruled outline", "", [],
            [Block("ex-r", "ruled block", "", Row("row-r1", "X", "1"))], "outline-2");
        var rule = new Rule(Loc, [], "Rule", "Some rule", "", [new RuleChild(null, ruled)], "rule-1");

        var feature = new Feature(Loc, [], "en", "Feature", "Market share", "",
        [
            new FeatureChild(null, null, plain),
            new FeatureChild(null, null, outline),
            new FeatureChild(rule, null, null)
        ]);

        var pickles = new List<Pickle>
        {
            new("p0", "market-share.feature", "A plain scenario", "en", [], [], ["plain-1"]),
            OutlinePickle("p1", "outline-1", "row-0a"),
            OutlinePickle("p2", "outline-1", "row-0b"),
            OutlinePickle("p3", "outline-1", "row-1a"),
            OutlinePickle("p4", "outline-1", "row-2a"),
            new("p5", "market-share.feature", "Ruled outline", "en", [], [], ["outline-2", "row-r1"])
        };

        return new FeatureInfo(CultureInfo.InvariantCulture, "Features", "Market share", "",
            ProgrammingLanguage.CSharp, [], new FakeMessages(new GherkinDocument("market-share.feature", feature, []), pickles));
    }

    private static ScenarioInfo MakeScenarioInfo(string title, string? pickleIdIndex, params (string Key, string Value)[] args)
    {
        var arguments = new OrderedDictionary();
        foreach (var (key, value) in args)
            arguments.Add(key, value);
        return new ScenarioInfo(title, null, [], arguments, [], pickleIdIndex!);
    }

    [Fact]
    public void Pickle_index_route_resolves_name_description_and_index()
    {
        var block = ExamplesBlockResolver.Resolve(BuildFeatureInfo(),
            MakeScenarioInfo(OutlineName, "1", ("Period", "OneWeek"), ("Change", "2.50")));

        Assert.Equal("the merchant gained share", block.Name);
        Assert.Equal("only upward movement", block.Description);
        Assert.Equal(0, block.Index);
    }

    [Fact]
    public void Second_block_resolves_with_null_description()
    {
        var block = ExamplesBlockResolver.Resolve(BuildFeatureInfo(),
            MakeScenarioInfo(OutlineName, "3", ("Period", "FourWeeks"), ("Change", "-2.50")));

        Assert.Equal("the merchant lost share", block.Name);
        Assert.Null(block.Description);
        Assert.Equal(1, block.Index);
    }

    [Fact]
    public void Unnamed_block_resolves_to_null_name_with_its_index()
    {
        var block = ExamplesBlockResolver.Resolve(BuildFeatureInfo(),
            MakeScenarioInfo(OutlineName, "4", ("Period", "RollingYear"), ("Change", "0.00")));

        Assert.Null(block.Name);
        Assert.Null(block.Description);
        Assert.Equal(2, block.Index);
    }

    [Fact]
    public void Rule_nested_outline_rows_resolve()
    {
        var block = ExamplesBlockResolver.Resolve(BuildFeatureInfo(),
            MakeScenarioInfo("Ruled outline", "5", ("Period", "X"), ("Change", "1")));

        Assert.Equal("ruled block", block.Name);
        Assert.Equal(0, block.Index);
    }

    [Fact]
    public void Mismatched_pickle_index_falls_back_to_value_match()
    {
        // Index points at block 0's first row, but the argument values belong to block 1.
        var block = ExamplesBlockResolver.Resolve(BuildFeatureInfo(),
            MakeScenarioInfo(OutlineName, "1", ("Period", "FourWeeks"), ("Change", "-2.50")));

        Assert.Equal("the merchant lost share", block.Name);
        Assert.Equal(1, block.Index);
    }

    [Fact]
    public void Unparsable_pickle_index_falls_back_to_value_match()
    {
        var block = ExamplesBlockResolver.Resolve(BuildFeatureInfo(),
            MakeScenarioInfo(OutlineName, "not-a-number", ("Period", "OneYear"), ("Change", "5.00")));

        Assert.Equal("the merchant gained share", block.Name);
        Assert.Equal(0, block.Index);
    }

    [Fact]
    public void Identical_rows_in_two_blocks_give_up_with_nulls()
    {
        var outline = new Scenario(Loc, [], "Scenario Outline", OutlineName, "", [],
            [
                Block("ex-0", "first", "", Row("row-0a", "Same", "1")),
                Block("ex-1", "second", "", Row("row-1a", "Same", "1"))
            ], "outline-1");
        var feature = new Feature(Loc, [], "en", "Feature", "F", "", [new FeatureChild(null, null, outline)]);
        var featureInfo = new FeatureInfo(CultureInfo.InvariantCulture, "Features", "F", "",
            ProgrammingLanguage.CSharp, [], new FakeMessages(new GherkinDocument("f.feature", feature, []), []));

        var block = ExamplesBlockResolver.Resolve(featureInfo,
            MakeScenarioInfo(OutlineName, null, ("Period", "Same"), ("Change", "1")));

        Assert.Null(block.Name);
        Assert.Null(block.Index);
    }

    [Fact]
    public void Plain_scenario_pickle_resolves_to_nulls()
    {
        var block = ExamplesBlockResolver.Resolve(BuildFeatureInfo(),
            MakeScenarioInfo("A plain scenario", "0"));

        Assert.Null(block.Name);
        Assert.Null(block.Description);
        Assert.Null(block.Index);
    }

    [Fact]
    public void Feature_without_embedded_messages_resolves_to_nulls()
    {
        var featureInfo = new FeatureInfo(CultureInfo.InvariantCulture, "Features", "F", "", ProgrammingLanguage.CSharp, []);

        var block = ExamplesBlockResolver.Resolve(featureInfo,
            MakeScenarioInfo(OutlineName, "1", ("Period", "OneWeek"), ("Change", "2.50")));

        Assert.Null(block.Name);
        Assert.Null(block.Index);
    }

    /// <summary>
    /// The resolver reads two internal Reqnroll properties by reflection. When a Reqnroll upgrade
    /// renames them the feature silently degrades to nulls — this test makes the drift loud in CI.
    /// </summary>
    [Fact]
    public void Reqnroll_internal_members_the_resolver_depends_on_still_exist()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        var featureMessages = typeof(FeatureInfo).GetProperty("FeatureCucumberMessages", flags);
        Assert.NotNull(featureMessages);
        Assert.Equal(typeof(IFeatureLevelCucumberMessages), featureMessages!.PropertyType);

        var pickleIdIndex = typeof(ScenarioInfo).GetProperty("PickleIdIndex", flags);
        Assert.NotNull(pickleIdIndex);
        Assert.Equal(typeof(string), pickleIdIndex!.PropertyType);

        var pickleId = typeof(ScenarioInfo).GetProperty("PickleId", flags);
        Assert.NotNull(pickleId);
        Assert.Equal(typeof(string), pickleId!.PropertyType);
    }
}
