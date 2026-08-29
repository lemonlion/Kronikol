using Kronikol.Ingestion.Cucumber;
using Kronikol.Reports;

namespace Kronikol.Tests.Ingestion.Cucumber;

/// <summary>
/// Named <c>Examples:</c> blocks: the Cucumber messages importer maps each outline row's block
/// name, description and index onto the scenario, so the report can render separator bands.
/// </summary>
public class CucumberExamplesBlockTests
{
    // ---- deserialization ------------------------------------------------------------------------

    [Fact]
    public void Examples_block_name_and_description_are_deserialized()
    {
        const string line =
            """
            {"gherkinDocument":{"uri":"f.feature","feature":{"keyword":"Feature","name":"F","children":[{"scenario":{"id":"sc1","keyword":"Scenario Outline","name":"O","steps":[],"examples":[{"id":"ex1","name":"the merchant gained share","description":"  only upward movement","tags":[],"tableHeader":{"id":"h1","cells":[{"value":"Period"}]},"tableBody":[{"id":"r1","cells":[{"value":"OneWeek"}]}]}]}}]}}}
            """;

        var messages = CucumberMessagesReader.Read(new StringReader(line));
        var examples = messages.GherkinDocuments.Single().Feature!.Children!.Single().Scenario!.Examples!.Single();

        Assert.Equal("the merchant gained share", examples.Name);
        Assert.Equal("  only upward movement", examples.Description);
    }

    // ---- synthesis ------------------------------------------------------------------------------

    private static CucumberMessages BuildMultiBlockMessages()
    {
        var messages = new CucumberMessages();

        CucumberTableRow Row(string id, string value) =>
            new() { Id = id, Cells = [new CucumberTableCell { Value = value }] };

        CucumberExamples Block(string id, string? name, string? description, params CucumberTableRow[] rows) =>
            new()
            {
                Id = id,
                Name = name,
                Description = description,
                TableHeader = new CucumberTableRow { Id = $"{id}-h", Cells = [new CucumberTableCell { Value = "Period" }] },
                TableBody = rows
            };

        messages.GherkinDocuments.Add(new CucumberGherkinDocument
        {
            Uri = "market-share.feature",
            Feature = new CucumberFeatureNode
            {
                Keyword = "Feature",
                Name = "Market share",
                Children =
                [
                    new CucumberFeatureChild
                    {
                        Scenario = new CucumberScenarioNode
                        {
                            Id = "outline-1",
                            Keyword = "Scenario Outline",
                            Name = "Market share movement is reported",
                            Steps = [],
                            Examples =
                            [
                                Block("ex-0", "the merchant gained share", "movement is positive", Row("row-0a", "OneWeek"), Row("row-0b", "OneYear")),
                                Block("ex-1", "the merchant lost share", null, Row("row-1a", "FourWeeks")),
                                Block("ex-2", name: "", description: null, Row("row-2a", "RollingYear"))
                            ]
                        }
                    },
                    new CucumberFeatureChild
                    {
                        Scenario = new CucumberScenarioNode
                        {
                            Id = "plain-1",
                            Keyword = "Scenario",
                            Name = "A plain scenario",
                            Steps = []
                        }
                    }
                ]
            }
        });

        void AddPickle(string pickleId, string name, params string[] astNodeIds)
        {
            messages.Pickles.Add(new CucumberPickle { Id = pickleId, Uri = "market-share.feature", Name = name, AstNodeIds = astNodeIds, Steps = [] });
            messages.TestCases.Add(new CucumberTestCase { Id = $"tc-{pickleId}", PickleId = pickleId, TestSteps = [] });
            messages.TestCaseStarted.Add(new CucumberTestCaseStarted { Id = $"att-{pickleId}", Attempt = 0, TestCaseId = $"tc-{pickleId}", Timestamp = new CucumberTimestamp { Seconds = 1 } });
            messages.TestCaseFinished.Add(new CucumberTestCaseFinished { TestCaseStartedId = $"att-{pickleId}", Timestamp = new CucumberTimestamp { Seconds = 2 } });
        }

        AddPickle("p-0a", "Market share movement is reported", "outline-1", "row-0a");
        AddPickle("p-0b", "Market share movement is reported", "outline-1", "row-0b");
        AddPickle("p-1a", "Market share movement is reported", "outline-1", "row-1a");
        AddPickle("p-2a", "Market share movement is reported", "outline-1", "row-2a");
        AddPickle("p-plain", "A plain scenario", "plain-1");

        return messages;
    }

    private static Scenario OutlineRow(CucumberSynthesisResult result, string period) =>
        result.Features.Single().Scenarios.Single(s => s.ExampleValues is not null && s.ExampleValues["Period"] == period);

    [Fact]
    public void Outline_rows_carry_their_examples_block_name_description_and_index()
    {
        var result = CucumberFeatureSynthesizer.Build(BuildMultiBlockMessages());

        var oneWeek = OutlineRow(result, "OneWeek");
        Assert.Equal("the merchant gained share", oneWeek.ExamplesBlockName);
        Assert.Equal("movement is positive", oneWeek.ExamplesBlockDescription);
        Assert.Equal(0, oneWeek.ExamplesBlockIndex);

        var oneYear = OutlineRow(result, "OneYear");
        Assert.Equal("the merchant gained share", oneYear.ExamplesBlockName);
        Assert.Equal(0, oneYear.ExamplesBlockIndex);

        var fourWeeks = OutlineRow(result, "FourWeeks");
        Assert.Equal("the merchant lost share", fourWeeks.ExamplesBlockName);
        Assert.Null(fourWeeks.ExamplesBlockDescription);
        Assert.Equal(1, fourWeeks.ExamplesBlockIndex);
    }

    [Fact]
    public void Unnamed_block_maps_to_null_name_but_keeps_its_index()
    {
        var result = CucumberFeatureSynthesizer.Build(BuildMultiBlockMessages());

        var rollingYear = OutlineRow(result, "RollingYear");
        Assert.Null(rollingYear.ExamplesBlockName);
        Assert.Null(rollingYear.ExamplesBlockDescription);
        Assert.Equal(2, rollingYear.ExamplesBlockIndex);
    }

    [Fact]
    public void Non_outline_scenarios_have_null_block_fields()
    {
        var result = CucumberFeatureSynthesizer.Build(BuildMultiBlockMessages());

        var plain = result.Features.Single().Scenarios.Single(s => s.DisplayName == "A plain scenario");
        Assert.Null(plain.ExamplesBlockName);
        Assert.Null(plain.ExamplesBlockDescription);
        Assert.Null(plain.ExamplesBlockIndex);
    }

    [Fact]
    public void Golden_fixture_outline_rows_get_block_index_zero_for_its_single_unnamed_block()
    {
        var result = CucumberFixtures.Build();
        var rows = result.Features.SelectMany(f => f.Scenarios)
            .Where(s => s.DisplayName == CucumberFixtures.OutlineScenario)
            .ToArray();

        Assert.Equal(2, rows.Length);
        Assert.All(rows, r =>
        {
            Assert.Equal(0, r.ExamplesBlockIndex);
            Assert.Null(r.ExamplesBlockName);
        });
    }
}
