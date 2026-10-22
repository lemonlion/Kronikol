using System.Collections.Specialized;
using System.Globalization;
using System.Reflection;
using Io.Cucumber.Messages.Types;
using Kronikol.ReqNRoll;
using Reqnroll;
using Reqnroll.BoDi;
using Reqnroll.Configuration;
using Reqnroll.Formatters.RuntimeSupport;

namespace Kronikol.Tests.ReqNRoll;

/// <summary>
/// #101, fact N7: the tracking hooks, run on real Reqnroll contexts, name an outline row once, in
/// <c>BeforeScenario</c>, and the test identity and the collected row carry that one name
/// (plans/OUTLINE_ROW_NAMES_PLAN.md 3.2 and 3.5). Reqnroll's context constructors are internal, so they are
/// called by reflection, as Reqnroll's own runner calls them.
/// </summary>
public class OutlineRowHooksTests
{
    private const string Title = "A basket is weighed on the UK scale (<case>)";

    private sealed class FakeMessages(GherkinDocument document, List<Pickle> pickles) : IFeatureLevelCucumberMessages
    {
        public bool HasMessages => true;
        public GherkinDocument GherkinDocument => document;
        public IEnumerable<Pickle> Pickles => pickles;
        public Source Source => null!;
    }

    private static Location Loc => new(1, 1);

    private static TableRow Row(string id, params string[] cells) =>
        new(Loc, cells.Select(c => new TableCell(Loc, c)).ToList(), id);

    private static FeatureInfo Baskets(bool withMessages)
    {
        if (!withMessages)
            return new FeatureInfo(CultureInfo.InvariantCulture, "Features", "Baskets", "", ProgrammingLanguage.CSharp, []);
        var examples = new Examples(Loc, [], "Examples", "", "", Row("header", "case", "count", "grams"),
            [Row("row-1", "empty basket", "0", "0"), Row("row-2", "two apples", "2", "301")], "examples-1");
        var outline = new Scenario(new Location(3, 3), [], "Scenario Outline", Title, "", [], [examples], "outline-1");
        var feature = new Feature(Loc, [], "en", "Feature", "Baskets", "", [new FeatureChild(null, null, outline)]);
        return new FeatureInfo(CultureInfo.InvariantCulture, "Features", "Baskets", "", ProgrammingLanguage.CSharp, [],
            new FakeMessages(new GherkinDocument("Features/Baskets.feature", feature, []),
            [
                new("p0", "Features/Baskets.feature", "A basket is weighed on the UK scale (empty basket)", "en", [], [], ["outline-1", "row-1"]),
                new("p1", "Features/Baskets.feature", "A basket is weighed on the UK scale (two apples)", "en", [], [], ["outline-1", "row-2"]),
            ]));
    }

    private static ScenarioInfo ScenarioInfoOf(string title, string pickleIndex, params (string Key, string Value)[] args)
    {
        var arguments = new OrderedDictionary();
        foreach (var (key, value) in args)
            arguments.Add(key, value);
        return new ScenarioInfo(title, null, [], arguments, [], pickleIndex);
    }

    private static T Internal<T>(params object?[] args) =>
        (T)Activator.CreateInstance(typeof(T), BindingFlags.Instance | BindingFlags.NonPublic, null, args, CultureInfo.InvariantCulture)!;

    /// <summary>Runs one scenario through the hooks: the identity seen between them, and the row they collected.</summary>
    private static ((string Name, string Id) Identity, ReqNRollScenarioInfo Collected) Run(FeatureInfo featureInfo, ScenarioInfo scenarioInfo)
    {
        var featureContext = Internal<FeatureContext>(new ObjectContainer(), featureInfo, ConfigurationLoader.GetDefault());
        var scenarioContext = Internal<ScenarioContext>(new ObjectContainer(), scenarioInfo, null, null);
        var hooks = new ReqNRollTrackingHooks(scenarioContext, featureContext);

        hooks.BeforeScenario();
        var identity = ReqNRollTestContext.CurrentTestInfo ?? throw new InvalidOperationException("no identity");
        hooks.AfterScenario();

        return (identity, ReqNRollScenarioCollector.GetAll().Single(s => s.ScenarioId == identity.Id));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_identity_and_the_collected_row_carry_the_row_s_name(bool withMessages)
    {
        var (identity, collected) = Run(Baskets(withMessages),
            ScenarioInfoOf(Title, "1", ("case", "two apples"), ("count", "2"), ("grams", "301")));

        Assert.Equal("A basket is weighed on the UK scale (two apples)", identity.Name);
        Assert.Equal(identity.Name, collected.RowName);
        Assert.Equal(Title, collected.ScenarioTitle);
        Assert.Equal(Title, collected.OutlineId);
        Assert.Equal("Features/Baskets.feature", withMessages ? collected.SourceFile : "Features/Baskets.feature");
    }

    [Fact]
    public void A_plain_scenario_keeps_its_title_in_the_identity_and_the_row()
    {
        var (identity, collected) = Run(Baskets(false), ScenarioInfoOf("A plain scenario with <x>", "0"));

        Assert.Equal("A plain scenario with <x>", identity.Name);
        Assert.Equal("A plain scenario with <x>", collected.RowName);
        Assert.Null(collected.OutlineId);
    }

    [Fact]
    public void The_identity_is_gone_after_the_scenario()
    {
        Run(Baskets(true), ScenarioInfoOf(Title, "0", ("case", "empty basket"), ("count", "0"), ("grams", "0")));

        Assert.Null(ReqNRollTestContext.CurrentTestInfo);
    }
}
