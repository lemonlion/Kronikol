using Kronikol.Ingestion.Cucumber;
using Kronikol.Reports;

namespace Kronikol.Tests.Ingestion.Cucumber;

/// <summary>
/// An <c>Examples:</c> table that names a column twice (plans/OUTLINE_ROW_NAMES_PLAN.md section 8). Gherkin fills each
/// <c>&lt;size&gt;</c> of the title and the steps from the leftmost <c>size</c>: its compiler replaces one column at a
/// time, left to right, and once the leftmost has filled them there is none left for the rightmost. The row's values took
/// the rightmost, so the value table, the step's parameter and the row's name disagreed. Gherkin accepts such a table;
/// Reqnroll refuses it at generation, so only the Cucumber lane meets it.
/// </summary>
public class CucumberRepeatedColumnTests
{
    private static CucumberTableCell Cell(string value) => new() { Value = value };

    /// <summary>One outline row, <c>| size | size |</c> over <c>| small | large |</c>, compiled as Gherkin compiles it.</summary>
    private static Scenario Row()
    {
        var messages = new CucumberMessages();
        messages.GherkinDocuments.Add(new CucumberGherkinDocument
        {
            Uri = "boxes.feature",
            Feature = new CucumberFeatureNode
            {
                Keyword = "Feature",
                Name = "Boxes",
                Children =
                [
                    new CucumberFeatureChild
                    {
                        Scenario = new CucumberScenarioNode
                        {
                            Id = "outline-1",
                            Keyword = "Scenario Outline",
                            Name = "Pick a <size> box",
                            Steps = [new CucumberGherkinStep { Id = "step-1", Keyword = "Given ", Text = "a <size> box" }],
                            Examples =
                            [
                                new CucumberExamples
                                {
                                    Id = "ex-1",
                                    TableHeader = new CucumberTableRow { Id = "h", Cells = [Cell("size"), Cell("size")] },
                                    TableBody = [new CucumberTableRow { Id = "r1", Cells = [Cell("small"), Cell("large")] }]
                                }
                            ]
                        }
                    }
                ]
            }
        });
        messages.Pickles.Add(new CucumberPickle
        {
            Id = "p1", Uri = "boxes.feature", Name = "Pick a small box", AstNodeIds = ["outline-1", "r1"],
            Steps = [new CucumberPickleStep { Id = "ps-1", Text = "a small box", AstNodeIds = ["step-1", "r1"] }]
        });
        messages.TestCases.Add(new CucumberTestCase { Id = "tc-1", PickleId = "p1", TestSteps = [new CucumberTestStep { Id = "ts-1", PickleStepId = "ps-1" }] });
        messages.TestCaseStarted.Add(new CucumberTestCaseStarted { Id = "att-1", Attempt = 0, TestCaseId = "tc-1", Timestamp = new CucumberTimestamp { Seconds = 1 } });
        messages.TestCaseFinished.Add(new CucumberTestCaseFinished { TestCaseStartedId = "att-1", Timestamp = new CucumberTimestamp { Seconds = 2 } });

        return CucumberFeatureSynthesizer.Build(messages).Features.Single().Scenarios.Single();
    }

    [Fact]
    public void The_row_keeps_the_value_Gherkin_named_it_by()
    {
        var row = Row();

        Assert.Equal("Pick a small box", row.DisplayName);
        Assert.Equal(new Dictionary<string, string> { ["size"] = "small" }, row.ExampleValues);
        Assert.Equal("small", row.ExampleRawValues!["size"]);
    }

    [Fact]
    public void The_step_s_parameter_is_the_value_its_text_shows()
    {
        var segments = Row().Steps!.Single().TextSegments;

        Assert.NotNull(segments);
        Assert.Equal("small", Assert.Single(segments!, s => s.Parameter is not null).Parameter!.Value);
    }
}
