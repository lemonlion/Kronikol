using System.Collections.Specialized;
using System.Globalization;
using Io.Cucumber.Messages.Types;
using Kronikol.ReqNRoll;
using Reqnroll;
using Reqnroll.Formatters.RuntimeSupport;

namespace Kronikol.Tests.ReqNRoll;

/// <summary>
/// #101: a Reqnroll outline row is named as Gherkin names its pickle. The name is the pickle's own, read from the
/// messages Reqnroll embeds, else Gherkin's rule over the title and the row's arguments
/// (plans/OUTLINE_ROW_NAMES_PLAN.md 3.2, facts N1 to N6).
/// </summary>
public class OutlineRowNameTests
{
    private const string Title = "A basket is weighed on the UK scale (<case>)";

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

    /// <summary>The issue's outline: three rows, each pickle named as Gherkin 35.0.0 compiles it.</summary>
    private static FeatureInfo Baskets(params Pickle[] pickles)
    {
        var examples = new Examples(Loc, [], "Examples", "", "", Row("header", "case", "count", "grams"),
            [Row("row-1", "empty basket", "0", "0"), Row("row-2", "one apple", "1", "150"), Row("row-3", "two apples", "2", "301")],
            "examples-1");
        var outline = new Scenario(new Location(3, 3), [], "Scenario Outline", Title, "", [], [examples], "outline-1");
        var feature = new Feature(Loc, [], "en", "Feature", "Baskets", "", [new FeatureChild(null, null, outline)]);
        return new FeatureInfo(CultureInfo.InvariantCulture, "Features", "Baskets", "", ProgrammingLanguage.CSharp, [],
            new FakeMessages(new GherkinDocument("Features/Baskets.feature", feature, []), pickles.ToList()));
    }

    private static Pickle RowPickle(string id, string name, string rowAstId) =>
        new(id, "Features/Baskets.feature", name, "en", [], [], ["outline-1", rowAstId]);

    private static readonly Pickle[] BasketPickles =
    [
        RowPickle("p0", "A basket is weighed on the UK scale (empty basket)", "row-1"),
        RowPickle("p1", "A basket is weighed on the UK scale (one apple)", "row-2"),
        RowPickle("p2", "A basket is weighed on the UK scale (two apples)", "row-3"),
    ];

    private static OrderedDictionary Arguments(params (string Key, string Value)[] args)
    {
        var arguments = new OrderedDictionary();
        foreach (var (key, value) in args)
            arguments.Add(key, value);
        return arguments;
    }

    private static ScenarioInfo Row(string title, string? pickleIdIndex, params (string Key, string Value)[] args) =>
        new(title, null, [], Arguments(args), [], pickleIdIndex!);

    private static readonly (string, string)[] TwoApples = [("case", "two apples"), ("count", "2"), ("grams", "301")];

    // N1
    [Fact]
    public void A_row_whose_pickle_is_found_is_named_as_its_pickle()
    {
        var name = ExamplesBlockResolver.ResolveRowName(Baskets(BasketPickles), Row(Title, "2", TwoApples));

        Assert.Equal("A basket is weighed on the UK scale (two apples)", name);
    }

    [Fact]
    public void The_pickle_name_is_taken_as_Gherkin_wrote_it_even_where_the_rule_would_differ()
    {
        // A pickle is Gherkin's own answer; the rule is only what stands in for it. A name the rule cannot make
        // proves the pickle was read.
        var pickles = BasketPickles.ToArray();
        pickles[2] = RowPickle("p2", "Compiled by Gherkin: two apples", "row-3");

        var name = ExamplesBlockResolver.ResolveRowName(Baskets(pickles), Row(Title, "2", TwoApples));

        Assert.Equal("Compiled by Gherkin: two apples", name);
    }

    // N2
    [Fact]
    public void Without_messages_the_row_is_named_by_Gherkin_s_rule()
    {
        var featureInfo = new FeatureInfo(CultureInfo.InvariantCulture, "Features", "Baskets", "", ProgrammingLanguage.CSharp, []);

        var name = ExamplesBlockResolver.ResolveRowName(featureInfo, Row(Title, "2", TwoApples));

        Assert.Equal("A basket is weighed on the UK scale (two apples)", name);
    }

    [Fact]
    public void Without_a_feature_the_row_is_named_by_Gherkin_s_rule()
    {
        Assert.Equal("A basket is weighed on the UK scale (two apples)",
            ExamplesBlockResolver.ResolveRowName(null, Row(Title, "2", TwoApples)));
    }

    // N3
    [Fact]
    public void A_pickle_index_that_points_at_another_row_never_gives_that_row_s_name()
    {
        // index 0 is the empty basket's pickle; its cells are not this row's values
        var name = ExamplesBlockResolver.ResolveRowName(Baskets(BasketPickles), Row(Title, "0", TwoApples));

        Assert.Equal("A basket is weighed on the UK scale (two apples)", name);
    }

    [Fact]
    public void A_pickle_of_another_outline_with_the_same_values_never_gives_its_name()
    {
        var basketExamples = new Examples(Loc, [], "Examples", "", "", Row("header", "case", "count", "grams"),
            [Row("row-3", "two apples", "2", "301")], "examples-1");
        var otherExamples = new Examples(Loc, [], "Examples", "", "", Row("header-x", "case", "count", "grams"),
            [Row("row-x", "two apples", "2", "301")], "examples-x");
        var basket = new Scenario(new Location(3, 3), [], "Scenario Outline", Title, "", [], [basketExamples], "outline-1");
        var other = new Scenario(new Location(9, 3), [], "Scenario Outline", "Another outline <case>", "", [], [otherExamples], "outline-x");
        var feature = new Feature(Loc, [], "en", "Feature", "Baskets", "",
            [new FeatureChild(null, null, other), new FeatureChild(null, null, basket)]);
        var featureInfo = new FeatureInfo(CultureInfo.InvariantCulture, "Features", "Baskets", "", ProgrammingLanguage.CSharp, [],
            new FakeMessages(new GherkinDocument("Features/Baskets.feature", feature, []),
            [
                new("px", "Features/Baskets.feature", "Another outline two apples", "en", [], [], ["outline-x", "row-x"]),
                RowPickle("p3", "A basket is weighed on the UK scale (two apples)", "row-3"),
            ]));

        // index 0 is the other outline's row, whose cells are this row's values
        var name = ExamplesBlockResolver.ResolveRowName(featureInfo, Row(Title, "0", TwoApples));

        Assert.Equal("A basket is weighed on the UK scale (two apples)", name);
    }

    [Fact]
    public void A_pickle_index_past_the_pickles_falls_back_to_the_rule()
    {
        var name = ExamplesBlockResolver.ResolveRowName(Baskets(BasketPickles), Row(Title, "7", TwoApples));

        Assert.Equal("A basket is weighed on the UK scale (two apples)", name);
    }

    [Fact]
    public void An_empty_pickle_name_falls_back_to_the_rule()
    {
        var pickles = BasketPickles.ToArray();
        pickles[2] = RowPickle("p2", "", "row-3");

        var name = ExamplesBlockResolver.ResolveRowName(Baskets(pickles), Row(Title, "2", TwoApples));

        Assert.Equal("A basket is weighed on the UK scale (two apples)", name);
    }

    // K7: the two lookups that match on the title as written keep matching a row whose name is filled
    [Fact]
    public void A_placeholder_row_still_finds_its_source_line_and_its_block()
    {
        var featureInfo = Baskets(BasketPickles);

        Assert.Equal(new ScenarioSource("Features/Baskets.feature", 3), ExamplesBlockResolver.ResolveSource(featureInfo, Row(Title, "2", TwoApples)));
        Assert.Equal(0, ExamplesBlockResolver.Resolve(featureInfo, Row(Title, "2", TwoApples)).Index);
        // a wrong index: the block is found by the row's values, under the title as written
        Assert.Equal(0, ExamplesBlockResolver.Resolve(featureInfo, Row(Title, "0", TwoApples)).Index);
    }

    // N5
    [Theory]
    [InlineData("E01 a title with no placeholder")]
    [InlineData("E03 weighs <count> on <missing>")]
    [InlineData("<Case> is not <case >")]
    public void A_title_with_no_placeholder_that_names_a_column_keeps_its_name_byte_for_byte(string title)
    {
        var name = ExamplesBlockResolver.ResolveRowName(null, Row(title, "0", ("case", "x"), ("grams", "1")));

        Assert.Equal(title, name);
    }

    [Fact]
    public void A_placeholder_with_no_column_stays_as_written_beside_one_that_is_filled()
    {
        Assert.Equal("E03 weighs 1 on <missing>",
            ExamplesBlockResolver.ResolveRowName(null, Row("E03 weighs <count> on <missing>", "0", ("count", "1"))));
    }

    // N6
    [Fact]
    public void A_plain_scenario_keeps_a_placeholder_in_its_title()
    {
        Assert.Equal("E09 a plain scenario that writes <x> in its title",
            ExamplesBlockResolver.ResolveRowName(null, Row("E09 a plain scenario that writes <x> in its title", "0")));
    }

    [Fact]
    public void A_title_whose_fill_is_empty_keeps_the_title()
    {
        Assert.Equal("<a><b>", ExamplesBlockResolver.ResolveRowName(null, Row("<a><b>", "0", ("a", ""), ("b", ""))));
    }

    [Fact]
    public void A_missing_scenario_has_an_empty_name_and_nothing_throws()
    {
        Assert.Equal("", ExamplesBlockResolver.ResolveRowName(null, null));
    }

    // Gherkin's rule (plans/OUTLINE_ROW_NAMES_PLAN.md 2.4), on its own
    [Theory]
    [InlineData("E02 <a> then <b>", "a=<b>;b=x", "E02 x then x")]
    [InlineData("E02 <a> then <b>", "a=x;b=<a>", "E02 x then <a>")]
    [InlineData("E11 <n> and <n> again", "n=1", "E11 1 and 1 again")]
    [InlineData("E07 escapes [<v>]", "v=", "E07 escapes []")]
    [InlineData("<fruit> starts E12", "fruit=apple", "apple starts E12")]
    [InlineData("E04 the <thing name> in <Größe> and <class>", "thing name=apple;Größe=klein;class=A", "E04 the apple in klein and A")]
    [InlineData("case <CASE> <case>", "case=lower", "case <CASE> lower")]
    [InlineData("E06 <a>", "a=<a>", "E06 <a>")]
    public void The_rule_fills_one_column_at_a_time_in_written_order(string title, string row, string expected)
    {
        var arguments = Arguments(row.Split(';').Select(p => p.Split('=', 2)).Select(p => (p[0], p[1])).ToArray());

        Assert.Equal(expected, ExamplesBlockResolver.FillPlaceholders(title, arguments));
    }

    // N4: the harness probe's feature files (plans/OUTLINE_ROW_NAMES_PLAN.harness/probe), compiled by Gherkin 35.0.0,
    // the compiler Reqnroll 3.3.4 names its pickles with
    public static TheoryData<string> ProbeFeatures => new() { "Baskets.feature", "Edges.feature" };

    [Theory]
    [MemberData(nameof(ProbeFeatures))]
    public void The_rule_names_every_row_as_Gherkin_s_own_compiler_does(string file)
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "ReqNRoll", "OutlineRowNames", file));
        var ids = new global::Gherkin.CucumberMessages.IncrementingIdGenerator();
        var ast = new global::Gherkin.Parser().Parse(new StringReader(text));
        var document = new global::Gherkin.CucumberMessages.AstMessagesConverter(ids).ConvertGherkinDocumentToEventArgs(ast, file);
        var pickles = new global::Gherkin.CucumberMessages.Pickles.PickleCompiler(ids).Compile(document);
        var scenarios = ScenarioNodes(document.Feature).ToDictionary(s => s.Id);

        var rows = 0;
        foreach (var pickle in pickles)
        {
            var scenario = scenarios[pickle.AstNodeIds[0]];
            var arguments = new OrderedDictionary();
            if (pickle.AstNodeIds.Count > 1)
            {
                var examples = scenario.Examples.Single(e => e.TableBody.Any(r => r.Id == pickle.AstNodeIds[1]));
                var row = examples.TableBody.Single(r => r.Id == pickle.AstNodeIds[1]);
                for (var i = 0; i < examples.TableHeader!.Cells.Count; i++)
                    arguments.Add(examples.TableHeader.Cells[i].Value, row.Cells[i].Value);
                rows++;
            }

            // a pickle Gherkin named "" is shown by its title, as the Cucumber lane shows it
            var expected = pickle.Name.Length > 0 ? pickle.Name : scenario.Name;
            Assert.Equal(expected, ExamplesBlockResolver.FillPlaceholders(scenario.Name, arguments));
            Assert.Equal(expected, ExamplesBlockResolver.ResolveRowName(null, new ScenarioInfo(scenario.Name, null, [], arguments, [], "0")));
        }

        Assert.Equal(file == "Baskets.feature" ? 3 : 26, rows);
    }

    private static IEnumerable<Scenario> ScenarioNodes(Feature feature) =>
        feature.Children.SelectMany(c => c.Scenario is { } s ? [s] : c.Rule?.Children.Select(r => r.Scenario).OfType<Scenario>() ?? []);

    [Fact]
    public void The_rule_carries_a_pipe_and_a_line_feed_through()
    {
        var arguments = Arguments(("v", "line\nbreak"), ("w", "pipe | bar"));

        Assert.Equal("E07 escapes [line\nbreak] [pipe | bar]",
            ExamplesBlockResolver.FillPlaceholders("E07 escapes [<v>] [<w>]", arguments));
    }

    [Fact]
    public void The_rule_with_no_arguments_gives_the_title()
    {
        Assert.Equal(Title, ExamplesBlockResolver.FillPlaceholders(Title, null));
        Assert.Equal(Title, ExamplesBlockResolver.FillPlaceholders(Title, new OrderedDictionary()));
    }
}
