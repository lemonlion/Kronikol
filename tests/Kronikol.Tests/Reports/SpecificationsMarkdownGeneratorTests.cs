using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// <c>Specifications.md</c> is the living documentation without the chrome: what the system does, in the
/// order a reader wants it, in a file small enough to read whole. The HTML answers the same question and
/// costs six figures of tokens to open; the YAML/JSON/XML trio is a data structure rather than a
/// narrative. This is the third answer, and the facts below are what stop it drifting into either of the
/// other two — no run data, no interactions, no diagrams, and no scenario left out because the run was
/// red.
/// </summary>
public class SpecificationsMarkdownGeneratorTests
{
    private static string Generate(Feature[] features, string title = "Service Specifications") =>
        SpecificationsMarkdownGenerator.Generate(features, title);

    private static Feature[] Shop() =>
    [
        new Feature
        {
            DisplayName = "Checkout",
            Endpoint = "payments",
            Description = "How a customer pays.",
            Labels = ["billing"],
            Scenarios =
            [
                new Scenario
                {
                    Id = "c1", DisplayName = "Pay with an expired card", Result = ExecutionResult.Failed,
                    Rule = "A card must not be expired",
                    Steps =
                    [
                        new ScenarioStep { Keyword = "Given", Text = "an expired card" },
                        new ScenarioStep { Keyword = "Then", Text = "the charge is declined" }
                    ]
                },
                new Scenario
                {
                    Id = "c2", DisplayName = "Pay with a valid card", IsHappyPath = true,
                    Result = ExecutionResult.Passed,
                    Description = "The ordinary path, and the one most orders take.",
                    Labels = ["smoke"], Categories = ["payments"],
                    BackgroundSteps = [new ScenarioStep { Keyword = "Given", Text = "the shop is open" }],
                    Steps =
                    [
                        new ScenarioStep
                        {
                            Keyword = "When", Text = "the customer pays",
                            SubSteps = [new ScenarioStep { Text = "the card is charged" }]
                        }
                    ]
                }
            ]
        },
        new Feature
        {
            DisplayName = "Browse",
            Scenarios = [new Scenario { Id = "b1", DisplayName = "See the catalogue", Result = ExecutionResult.Passed }]
        }
    ];

    // ─── Shape ─────────────────────────────────────────────────

    [Fact]
    public void It_opens_with_the_title_and_says_what_it_is()
    {
        var markdown = Generate(Shop());

        Assert.StartsWith("# Service Specifications", markdown);
        // The directory already holds Failures.md, CiSummary.md, CLAUDE.md and AGENTS.md, so .md says
        // nothing about which file this is. The document has to identify itself.
        Assert.Contains("Living documentation", markdown);
        // And says where the run data went, so nobody reads its silence as "nothing happened".
        Assert.Contains("kronikol query", markdown);
    }

    [Fact]
    public void Features_are_second_level_headings_in_the_order_every_other_writer_uses()
    {
        var markdown = Generate(Shop());

        Assert.True(markdown.IndexOf("## Browse", StringComparison.Ordinal)
                    < markdown.IndexOf("## Checkout", StringComparison.Ordinal));
    }

    [Fact]
    public void The_feature_order_uses_the_comparer_every_other_writer_uses()
    {
        // The comparer, not just "sorted": under Ordinal every upper-case initial beats every lower-case
        // one, so "Order API" would come first here and last in the JSON, XML and YAML views of the same
        // suite. Same trap, same fix, as FailuresDigestGenerator.Enumerate.
        Feature[] features =
        [
            new Feature { DisplayName = "Order API", Scenarios = [new Scenario { Id = "b", DisplayName = "Place", Result = ExecutionResult.Passed }] },
            new Feature { DisplayName = "Order api", Scenarios = [new Scenario { Id = "a", DisplayName = "Cancel", Result = ExecutionResult.Passed }] }
        ];

        var markdown = Generate(features);

        Assert.True(markdown.IndexOf("## Order api", StringComparison.Ordinal)
                    < markdown.IndexOf("## Order API", StringComparison.Ordinal));
    }

    [Fact]
    public void A_feature_carries_its_endpoint_description_and_labels()
    {
        var markdown = Generate(Shop());

        Assert.Contains("`payments`", markdown);
        // Author prose is quoted, so a heading inside somebody's Gherkin description cannot restructure
        // the document around it.
        Assert.Contains("> How a customer pays.", markdown);
        Assert.Contains("`billing`", markdown);
    }

    [Fact]
    public void Scenarios_are_happy_path_first_then_alphabetical()
    {
        var markdown = Generate(Shop());

        Assert.True(markdown.IndexOf("Pay with a valid card", StringComparison.Ordinal)
                    < markdown.IndexOf("Pay with an expired card", StringComparison.Ordinal));
    }

    [Fact]
    public void A_scenario_carries_its_own_description_labels_and_categories()
    {
        var markdown = Generate(Shop());

        Assert.Contains("> The ordinary path, and the one most orders take.", markdown);
        Assert.Contains("`smoke`", markdown);
        Assert.Contains("`payments`", markdown);
    }

    // ─── Steps ─────────────────────────────────────────────────

    [Fact]
    public void Steps_are_a_list_with_the_keyword_set_apart_and_sub_steps_nested()
    {
        var markdown = Generate(Shop());

        Assert.Contains("- **When** the customer pays", markdown);
        Assert.Contains("  - the card is charged", markdown);
    }

    [Fact]
    public void Background_steps_stay_a_section_of_their_own()
    {
        // Same reason the data writers keep them apart: merging them loses the b{i}/{i} split every step
        // path and interaction attribution depends on, and a reader cannot tell setup from the scenario.
        var markdown = Generate(Shop());

        Assert.Contains("Background", markdown);
        var background = markdown.IndexOf("Background", StringComparison.Ordinal);
        Assert.True(background < markdown.IndexOf("- **When** the customer pays", StringComparison.Ordinal));
        Assert.Contains("- **Given** the shop is open", markdown);
    }

    [Fact]
    public void A_step_written_across_several_lines_stays_one_list_item()
    {
        Feature[] features =
        [
            new Feature { DisplayName = "F", Scenarios = [new Scenario { Id = "s", DisplayName = "S", Result = ExecutionResult.Passed,
                Steps = [new ScenarioStep { Keyword = "Given", Text = "a card\nthat expired\r\nlast year" }] }] }
        ];

        var markdown = Generate(features);

        Assert.Contains("- **Given** a card that expired last year", markdown);
    }

    // ─── Rules ─────────────────────────────────────────────────

    [Fact]
    public void A_rule_groups_the_scenarios_written_under_it()
    {
        // The Rule is real Gherkin structure the HTML renders and the YAML/JSON/XML trio drops entirely.
        // A narrative that drops it too would read as a flat list of scenarios nobody grouped.
        var markdown = Generate(Shop());

        Assert.Contains("### Rule: A card must not be expired", markdown);
        Assert.True(markdown.IndexOf("### Rule:", StringComparison.Ordinal)
                    < markdown.IndexOf("Pay with an expired card", StringComparison.Ordinal));
        // Scenarios with no rule are not swept under one.
        Assert.True(markdown.IndexOf("Pay with a valid card", StringComparison.Ordinal)
                    < markdown.IndexOf("### Rule:", StringComparison.Ordinal));
    }

    // ─── What it deliberately is not ───────────────────────────

    [Fact]
    public void It_is_a_specification_so_it_carries_no_run_data_at_all()
    {
        // The same features, run red and run green, produce the same document. That is what makes it a
        // specification rather than a second report - and what makes it safe to commit to a docs site.
        var passing = Shop();
        foreach (var scenario in passing.SelectMany(f => f.Scenarios))
            scenario.Result = ExecutionResult.Passed;

        Assert.Equal(Generate(passing), Generate(Shop()));
        Assert.DoesNotContain("Failed", Generate(Shop()));
        Assert.DoesNotContain("Duration", Generate(Shop()));
    }

    [Fact]
    public void A_failed_run_still_writes_the_whole_narrative()
    {
        // Specifications.html and the data trio blank themselves on a red run, so a broken build cannot
        // publish half-truths as living documentation. This file deliberately does not: an agent reading
        // it is trying to understand the system it is debugging, and a blank file at that moment is the
        // least useful thing it could find. Stated here because it reads as a bug otherwise.
        var markdown = Generate(Shop());

        Assert.Contains("Pay with an expired card", markdown);
        Assert.True(markdown.Length > 200);
    }
}
