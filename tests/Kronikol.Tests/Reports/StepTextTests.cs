using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

public class StepTextTests
{
    [Theory]
    // Plain prose: the first letter is upper-cased.
    [InlineData("the mock answers 200", "The mock answers 200")]
    [InlineData("Already capital", "Already capital")]
    // Marker glyphs and the spaces between them are skipped, so an assertion label reads as a sentence.
    [InlineData("✓ the envelope carried no text part", "✓ The envelope carried no text part")]
    [InlineData("✗ the customers figure equals 42", "✗ The customers figure equals 42")]
    [InlineData("⚠  something is off", "⚠  Something is off")]
    [InlineData("• bullet point", "• Bullet point")]
    [InlineData("- dashed", "- Dashed")]
    [InlineData("✓ ✓ doubled markers", "✓ ✓ Doubled markers")]
    // Leading whitespace is preserved exactly.
    [InlineData("   indented text", "   Indented text")]
    // Quoted and bracketed literals belong to the producer: never re-cased.
    [InlineData("\"dialog\" to be hidden", "\"dialog\" to be hidden")]
    [InlineData("'single' quoted", "'single' quoted")]
    [InlineData("(parenthesised)", "(parenthesised)")]
    [InlineData("[bracketed]", "[bracketed]")]
    [InlineData("{braced}", "{braced}")]
    [InlineData("“typographic” quotes", "“typographic” quotes")]
    [InlineData("‘single typographic’", "‘single typographic’")]
    [InlineData("«guillemets»", "«guillemets»")]
    [InlineData("`code`", "`code`")]
    [InlineData("✓ \"Overview\" is visible", "✓ \"Overview\" is visible")]
    // Unicode, culture-invariantly.
    [InlineData("étape terminée", "Étape terminée")]
    [InlineData("łódź is a city", "Łódź is a city")]
    [InlineData("über alles", "Über alles")]
    // Nothing to change.
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    [InlineData("✓", "✓")]
    [InlineData("42 customers are listed", "42 customers are listed")]
    // A camelCase first word is an identifier: producer content, like a quoted literal.
    [InlineData("graphqlErrorMessages reads both shapes", "graphqlErrorMessages reads both shapes")]
    [InlineData("✓ insightsLocationReportDates returned dates", "✓ insightsLocationReportDates returned dates")]
    [InlineData("iPhone renders the page", "iPhone renders the page")]
    [InlineData("page.goBack is called", "page.goBack is called")]
    [InlineData("graphql rejects the request", "Graphql rejects the request")]
    [InlineData("the Overview page renders", "The Overview page renders")]
    public void Capitalise_upper_cases_the_first_letter_after_markers_but_never_a_quoted_literal(string input, string expected) =>
        Assert.Equal(expected, StepText.Capitalise(input));

    [Theory]
    [InlineData(null, "graphqlErrorMessages reads both shapes", true)]
    [InlineData(null, "✗ insightsLocationReportDates returned nothing", true)]
    [InlineData(null, "graphql rejects the request", false)]
    [InlineData("Given", "graphqlErrorMessages reads both shapes", true)]
    public void A_camel_case_identifier_is_not_a_capitalisation_violation(string? keyword, string text, bool ok) =>
        Assert.Equal(ok, StepText.StartsWithCapitalOrQuote(keyword, text));

    [Fact]
    public void Capitalise_is_null_safe_and_idempotent()
    {
        Assert.Null(StepText.Capitalise(null));

        var once = StepText.Capitalise("✓ the mock answers 200");
        Assert.Equal(once, StepText.Capitalise(once));
    }

    [Fact]
    public void Capitalise_if_enabled_honours_the_diagram_side_switch()
    {
        var previous = StepText.CapitaliseEnabled;
        try
        {
            StepText.CapitaliseEnabled = false;
            Assert.Equal("the mock answers 200", StepText.CapitaliseIfEnabled("the mock answers 200"));
            StepText.CapitaliseEnabled = true;
            Assert.Equal("The mock answers 200", StepText.CapitaliseIfEnabled("the mock answers 200"));
        }
        finally
        {
            StepText.CapitaliseEnabled = previous;
        }
    }

    [Fact]
    public void A_step_with_a_keyword_keeps_the_author_s_casing()
    {
        var steps = new[]
        {
            new ScenarioStep { Keyword = "Given", Text = "the mock is armed" },
            new ScenarioStep { Keyword = "  ", Text = "a blank keyword is no keyword" },
        };

        StepText.ApplyToSteps(steps);

        Assert.Equal("the mock is armed", steps[0].Text);
        Assert.Equal("A blank keyword is no keyword", steps[1].Text);
    }

    [Fact]
    public void Sub_steps_and_background_steps_are_capitalised_too()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "f",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "1",
                        DisplayName = "s",
                        BackgroundSteps = [new ScenarioStep { Text = "the seed data exists" }],
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Text = "the user accepts the trial",
                                SubSteps = [new ScenarioStep { Text = "✓ the trial banner is visible" }],
                            },
                        ],
                    },
                ],
            },
        };

        StepText.ApplyToFeatures(features);

        var scenario = features[0].Scenarios[0];
        Assert.Equal("The seed data exists", scenario.BackgroundSteps![0].Text);
        Assert.Equal("The user accepts the trial", scenario.Steps![0].Text);
        Assert.Equal("✓ The trial banner is visible", scenario.Steps![0].SubSteps![0].Text);
    }

    [Fact]
    public void Inline_parameter_highlighting_still_lines_up_after_capitalising()
    {
        // The rendered line comes from the segments, so the leading literal has to change with the text.
        var step = new ScenarioStep
        {
            Text = "the user 'ada' signs in",
            TextSegments =
            [
                StepTextSegment.Literal("the user "),
                StepTextSegment.Param("user", new InlineParameterValue("ada", null, VerificationStatus.NotApplicable)),
                StepTextSegment.Literal(" signs in"),
            ],
        };

        StepText.ApplyToSteps([step]);

        Assert.Equal("The user 'ada' signs in", step.Text);
        Assert.Equal("The user ", step.TextSegments![0].Text);
        Assert.Equal(" signs in", step.TextSegments![2].Text);
    }

    [Fact]
    public void A_step_whose_first_segment_is_a_parameter_is_left_alone_entirely()
    {
        // Re-casing the text but not the leading parameter value would make the two disagree.
        var step = new ScenarioStep
        {
            Text = "ada signs in",
            TextSegments =
            [
                StepTextSegment.Param("user", new InlineParameterValue("ada", null, VerificationStatus.NotApplicable)),
                StepTextSegment.Literal(" signs in"),
            ],
        };

        StepText.ApplyToSteps([step]);

        Assert.Equal("ada signs in", step.Text);
    }

    [Fact]
    public void Find_not_starting_with_capital_counts_what_the_rule_deliberately_left()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "f",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "1",
                        DisplayName = "s",
                        Steps =
                        [
                            new ScenarioStep { Text = "Capitalised already" },
                            new ScenarioStep { Keyword = "Given", Text = "the mock is armed" },
                            new ScenarioStep { Text = "\"dialog\" to be hidden" },
                            new ScenarioStep
                            {
                                Text = "still lowercase",
                                SubSteps = [new ScenarioStep { Text = "✓ also lowercase" }],
                            },
                        ],
                    },
                ],
            },
        };

        var (count, examples) = StepText.FindNotStartingWithCapital(features);

        // The keyword step and the quoted literal are both fine; the two lower-case ones are not.
        Assert.Equal(2, count);
        Assert.Equal(["still lowercase", "✓ also lowercase"], examples);
    }

    [Fact]
    public void Find_not_starting_with_capital_caps_its_examples()
    {
        var steps = Enumerable.Range(0, 9).Select(i => new ScenarioStep { Text = $"lowercase {i}" }).ToArray();
        var features = new[]
        {
            new Feature { DisplayName = "f", Scenarios = [new Scenario { Id = "1", DisplayName = "s", Steps = steps }] },
        };

        var (count, examples) = StepText.FindNotStartingWithCapital(features);

        Assert.Equal(9, count);
        Assert.Equal(5, examples.Length);
    }
    [Fact]
    public void Apply_to_titles_capitalises_feature_rule_scenario_and_outline_titles_but_not_example_data()
    {
        var feature = new Feature
        {
            DisplayName = "intelligence overview",
            Scenarios =
            [
                new Scenario { Id = "s", DisplayName = "the selected location is the canonical target", Rule = "the location the app picks must work" },
                new Scenario { Id = "s", DisplayName = "the /overview route loads", OutlineId = "the <path> route loads", ExampleDisplayName = "path=/overview" },
                new Scenario { Id = "s", DisplayName = "\"Deep dive\" is listed" },
                new Scenario { Id = "s", DisplayName = "42 locations render" },
            ],
        };

        StepText.ApplyToTitles([feature]);

        Assert.Equal("Intelligence overview", feature.DisplayName);
        Assert.Equal("The selected location is the canonical target", feature.Scenarios[0].DisplayName);
        Assert.Equal("The location the app picks must work", feature.Scenarios[0].Rule);
        Assert.Equal("The /overview route loads", feature.Scenarios[1].DisplayName);
        Assert.Equal("The <path> route loads", feature.Scenarios[1].OutlineId);
        Assert.Equal("path=/overview", feature.Scenarios[1].ExampleDisplayName);
        Assert.Equal("\"Deep dive\" is listed", feature.Scenarios[2].DisplayName);
        Assert.Equal("42 locations render", feature.Scenarios[3].DisplayName);

        StepText.ApplyToTitles(null); // null-safe
    }

    [Fact]
    public void Find_titles_not_starting_with_capital_counts_each_rule_once()
    {
        var feature = new Feature
        {
            DisplayName = "intelligence overview",
            Scenarios =
            [
                new Scenario { Id = "s", DisplayName = "the first", Rule = "the same rule" },
                new Scenario { Id = "s", DisplayName = "The second", Rule = "the same rule" },
                new Scenario { Id = "s", DisplayName = "\"quoted\" is fine" },
            ],
        };

        var (count, examples) = StepText.FindTitlesNotStartingWithCapital([feature]);

        Assert.Equal(3, count); // the feature, "the first", the rule (once)
        Assert.Equal(["intelligence overview", "the first", "the same rule"], examples);
        var (noneCount, noneExamples) = StepText.FindTitlesNotStartingWithCapital(null);
        Assert.Equal(0, noneCount);
        Assert.Empty(noneExamples);
    }
}
