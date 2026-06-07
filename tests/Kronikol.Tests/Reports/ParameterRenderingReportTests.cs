using Kronikol.Constants;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

public class ParameterRenderingReportTests
{
    private static string GenerateReport(Feature[] features)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            [], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, "ParamRender.html", "Test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        return File.ReadAllText(path);
    }

    [Fact]
    public void Report_renders_inline_parameter()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Test",
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Given", Text = "user named <name>",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "name",
                                        Kind = StepParameterKind.Inline,
                                        InlineValue = new InlineParameterValue("Alice", null, VerificationStatus.NotApplicable)
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains("step-param-inline", content);
        Assert.Contains("Alice", content);
    }

    [Fact]
    public void Report_renders_inline_parameter_with_expectation()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Test",
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Then", Text = "result is <expected>",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "expected",
                                        Kind = StepParameterKind.Inline,
                                        InlineValue = new InlineParameterValue("42", "42", VerificationStatus.Success)
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains("param-success", content);
    }

    [Fact]
    public void Report_renders_tabular_parameter()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Test",
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Given", Text = "users",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "users",
                                        Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("Name", false), new TabularColumn("Age", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("Alice", null, VerificationStatus.NotApplicable),
                                                 new TabularCell("30", null, VerificationStatus.NotApplicable)])])
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.DoesNotContain("<div class=\"step-param-combined-table\"", content);
        Assert.Contains("step-param-table", content);
        Assert.Contains("Alice", content);
        Assert.Contains("Age", content);
        // All rows are Matching → no row indicator column
        Assert.DoesNotContain("<td>=</td>", content);
    }

    [Fact]
    public void Report_renders_tree_parameter()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Test",
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Then", Text = "result matches",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "result",
                                        Kind = StepParameterKind.Tree,
                                        TreeValue = new TreeParameterValue(
                                            new TreeNode("$", "root", "obj", null, VerificationStatus.NotApplicable,
                                                [new TreeNode("$.name", "name", "Alice", "Alice", VerificationStatus.Success, null)]))
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains("step-param-tree", content);
        Assert.Contains("Alice", content);
    }

    [Fact]
    public void Report_does_not_render_parameters_when_none()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Test",
                        Steps = [new ScenarioStep { Keyword = "Given", Text = "something" }]
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.DoesNotContain("<span class=\"step-param-inline", content);
        Assert.DoesNotContain("<div class=\"step-param-combined-table\">", content);
        Assert.DoesNotContain("<div class=\"step-param-tree\">", content);
    }

    [Fact]
    public void Tabular_parameters_strip_param_suffix_from_step_text()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Test",
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Given", Text = "invalid label requests with invalid fields [invalidFields: \"<$invalidFields>\"]",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "invalidFields",
                                        Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("Field", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("Name", null, VerificationStatus.NotApplicable)])])
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains("invalid label requests with invalid fields", content);
        Assert.DoesNotContain("[invalidFields:", content);
        Assert.DoesNotContain("&lt;$invalidFields&gt;", content);
    }

    [Fact]
    public void Single_row_tables_without_keys_render_inline_not_combined()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Test",
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Given", Text = "requests with data",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "inputs",
                                        Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("Field", false), new TabularColumn("Value", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("Name", null, VerificationStatus.NotApplicable),
                                                 new TabularCell("Alice", null, VerificationStatus.NotApplicable)])])
                                    }
                                ]
                            },
                            new ScenarioStep { Keyword = "When", Text = "the requests are sent" },
                            new ScenarioStep
                            {
                                Keyword = "Then", Text = "the responses should match",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "expectedOutputs",
                                        Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("Status", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("200", "200", VerificationStatus.Success)])])
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };

        var content = GenerateReport(features);

        Assert.DoesNotContain("<div class=\"step-param-combined-table\">", content);
        Assert.Contains("<div class=\"step-param-table\" data-param=\"inputs\"", content);
        Assert.Contains("<div class=\"step-param-table\" data-param=\"expectedOutputs\"", content);
        Assert.Contains("Alice", content);
    }

    [Fact]
    public void Given_and_Then_tables_both_render_inline()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Test",
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Given", Text = "I have data [inputs: \"<$inputs>\"]",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "inputs",
                                        Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("ReportDate", false), new TabularColumn("Cadence", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("2025-10-01", null, VerificationStatus.NotApplicable),
                                                 new TabularCell("Monthly", null, VerificationStatus.NotApplicable)])])
                                    }
                                ]
                            },
                            new ScenarioStep { Keyword = "When", Text = "I call the endpoint" },
                            new ScenarioStep
                            {
                                Keyword = "Then", Text = "response matches [results: \"<$results>\"]",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "results",
                                        Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("Status", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("200", "200", VerificationStatus.Success)])])
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };

        var content = GenerateReport(features);

        // No combined table
        Assert.DoesNotContain("<div class=\"step-param-combined-table\">", content);

        // Given step's table renders inline
        Assert.Contains("<div class=\"step-param-table\" data-param=\"inputs\"", content);
        var inlineTableIdx = content.IndexOf("data-param=\"inputs\"");
        var inlineSection = content.Substring(inlineTableIdx, 500);
        Assert.Contains("2025-10-01", inlineSection);

        // Then step's table also renders inline
        Assert.Contains("<div class=\"step-param-table\" data-param=\"results\"", content);
    }

    [Fact]
    public void Then_step_table_renders_inline()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Test",
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Given", Text = "I have data [inputs: \"<$inputs>\"]",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "inputs",
                                        Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("Field", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("Value", null, VerificationStatus.NotApplicable)])])
                                    }
                                ]
                            },
                            new ScenarioStep { Keyword = "When", Text = "I call the endpoint" },
                            new ScenarioStep
                            {
                                Keyword = "Then", Text = "response matches [results: \"<$results>\"]",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "results",
                                        Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("Status", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("200", "200", VerificationStatus.Success)])])
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };

        var content = GenerateReport(features);

        // No combined table
        Assert.DoesNotContain("<div class=\"step-param-combined-table\">", content);

        // Then step's table renders inline
        Assert.Contains("<div class=\"step-param-table\" data-param=\"results\"", content);
    }

    [Fact]
    public void And_after_given_table_renders_inline_when_combined_table_exists()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Test",
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "I have a request" },
                            new ScenarioStep
                            {
                                Keyword = "And", Text = "with parameters [params: \"<$params>\"]",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "params",
                                        Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("Key", false), new TabularColumn("Value", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("limit", null, VerificationStatus.NotApplicable),
                                                 new TabularCell("10", null, VerificationStatus.NotApplicable)])])
                                    }
                                ]
                            },
                            new ScenarioStep { Keyword = "When", Text = "I call the endpoint" },
                            new ScenarioStep
                            {
                                Keyword = "Then", Text = "response matches [results: \"<$results>\"]",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "results",
                                        Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("Status", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("200", "200", VerificationStatus.Success)])])
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };

        var content = GenerateReport(features);

        // No combined table
        Assert.DoesNotContain("<div class=\"step-param-combined-table\">", content);

        // And step's table (which follows Given) should render inline
        Assert.Contains("<div class=\"step-param-table\" data-param=\"params\"", content);
        // Then step's table also renders inline
        Assert.Contains("<div class=\"step-param-table\" data-param=\"results\"", content);
    }

    [Fact]
    public void Then_inline_table_preserves_verification_status_css_classes()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Test",
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Given", Text = "input data",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "data",
                                        Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("Name", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("Alice", null, VerificationStatus.NotApplicable)])])
                                    }
                                ]
                            },
                            new ScenarioStep
                            {
                                Keyword = "Then", Text = "result matches",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "result",
                                        Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("Status", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("Fail", "Pass", VerificationStatus.Failure)])])
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.DoesNotContain("<div class=\"step-param-combined-table\">", content);
        Assert.Contains("param-failure", content);
        Assert.Contains("Fail/Pass", content);
    }

    [Fact]
    public void Single_tabular_parameter_renders_inline_without_combined_table()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Test",
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Given", Text = "users",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "users",
                                        Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("Name", false), new TabularColumn("Age", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("Alice", null, VerificationStatus.NotApplicable),
                                                 new TabularCell("30", null, VerificationStatus.NotApplicable)])])
                                    }
                                ]
                            },
                            new ScenarioStep
                            {
                                Keyword = "When", Text = "they are saved"
                            }
                        ]
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.DoesNotContain("<div class=\"step-param-combined-table\">", content);
        Assert.Contains("step-param-table", content);
        Assert.Contains("Alice", content);
    }

    [Fact]
    public void Then_inline_table_handles_row_type_indicators()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Test",
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Given", Text = "input data",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "data",
                                        Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("Name", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("Alice", null, VerificationStatus.NotApplicable)])])
                                    }
                                ]
                            },
                            new ScenarioStep
                            {
                                Keyword = "Then", Text = "results match",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "result",
                                        Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("Status", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("OK", "OK", VerificationStatus.Success)]),
                                             new TabularRow(TableRowType.Surplus,
                                                [new TabularCell("Extra", null, VerificationStatus.NotApplicable)])])
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.DoesNotContain("<div class=\"step-param-combined-table\">", content);
        Assert.Contains("<div class=\"step-param-table\" data-param=\"result\"", content);
        Assert.Contains("row-surplus", content);
        Assert.Contains("<td>+</td>", content);
    }

    // === Tier 1: Tabular Attributes (IsLinkedOutput) ===

    [Fact]
    public void Combined_table_renders_when_Then_table_has_IsLinkedOutput()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Test",
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Given", Text = "inputs",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "inputs",
                                        Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("Field", false), new TabularColumn("Value", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("Name", null, VerificationStatus.NotApplicable),
                                                 new TabularCell("Alice", null, VerificationStatus.NotApplicable)])])
                                    }
                                ]
                            },
                            new ScenarioStep { Keyword = "When", Text = "sent" },
                            new ScenarioStep
                            {
                                Keyword = "Then", Text = "matches",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "outputs",
                                        Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("Status", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("200", "200", VerificationStatus.Success)])],
                                            IsLinkedOutput: true)
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains("step-param-combined-table", content);
        Assert.Contains("<th class=\"combined-separator\">=</th>", content);
        Assert.Contains("<div class=\"step-param-table\" data-param=\"inputs\"", content);
        var beforeCombined = content.Split("step-param-combined-table")[0];
        Assert.DoesNotContain("data-param=\"outputs\"", beforeCombined);
    }

    // === Tier 2: Key-based alignment (LightBDD-style) ===

    [Fact]
    public void Combined_table_renders_when_tables_share_key_columns()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Test",
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Given", Text = "users",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "users",
                                        Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("Name", false), new TabularColumn("Email", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("Alice", null, VerificationStatus.NotApplicable),
                                                 new TabularCell("a@test.com", null, VerificationStatus.NotApplicable)]),
                                             new TabularRow(TableRowType.Matching,
                                                [new TabularCell("Bob", null, VerificationStatus.NotApplicable),
                                                 new TabularCell("b@test.com", null, VerificationStatus.NotApplicable)])])
                                    }
                                ]
                            },
                            new ScenarioStep { Keyword = "When", Text = "processed" },
                            new ScenarioStep
                            {
                                Keyword = "Then", Text = "results",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "results",
                                        Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("Name", true), new TabularColumn("Status", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("Bob", "Bob", VerificationStatus.Success),
                                                 new TabularCell("OK", "OK", VerificationStatus.Success)]),
                                             new TabularRow(TableRowType.Matching,
                                                [new TabularCell("Alice", "Alice", VerificationStatus.Success),
                                                 new TabularCell("OK", "OK", VerificationStatus.Success)])])
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains("step-param-combined-table", content);
        Assert.Contains("<th class=\"combined-separator\">=</th>", content);
    }

    [Fact]
    public void Key_based_alignment_reorders_input_rows_to_match_output()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Test",
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Given", Text = "data",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "input",
                                        Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("Id", false), new TabularColumn("Value", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("1", null, VerificationStatus.NotApplicable),
                                                 new TabularCell("AAA", null, VerificationStatus.NotApplicable)]),
                                             new TabularRow(TableRowType.Matching,
                                                [new TabularCell("2", null, VerificationStatus.NotApplicable),
                                                 new TabularCell("BBB", null, VerificationStatus.NotApplicable)])])
                                    }
                                ]
                            },
                            new ScenarioStep { Keyword = "When", Text = "processed" },
                            new ScenarioStep
                            {
                                Keyword = "Then", Text = "results",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "output",
                                        Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("Id", true), new TabularColumn("Status", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("2", "2", VerificationStatus.Success),
                                                 new TabularCell("OK", "OK", VerificationStatus.Success)]),
                                             new TabularRow(TableRowType.Matching,
                                                [new TabularCell("1", "1", VerificationStatus.Success),
                                                 new TabularCell("OK", "OK", VerificationStatus.Success)])])
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains("step-param-combined-table", content);

        // Output row 1 has Id=2, so input row with Id=2 (BBB) should be first
        var combinedSection = content.Substring(content.IndexOf("<div class=\"step-param-combined-table\">"));
        var bbbIdx = combinedSection.IndexOf("BBB");
        var aaaIdx = combinedSection.IndexOf("AAA");
        Assert.True(bbbIdx < aaaIdx, "Input row with Id=2 (BBB) should appear before Id=1 (AAA) due to key-based alignment");
    }

    [Fact]
    public void Key_based_alignment_handles_surplus_output_row()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Test",
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Given", Text = "data",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "input",
                                        Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("Id", false), new TabularColumn("Val", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("1", null, VerificationStatus.NotApplicable),
                                                 new TabularCell("A", null, VerificationStatus.NotApplicable)])])
                                    }
                                ]
                            },
                            new ScenarioStep { Keyword = "When", Text = "processed" },
                            new ScenarioStep
                            {
                                Keyword = "Then", Text = "results",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "output",
                                        Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("Id", true), new TabularColumn("Status", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("1", "1", VerificationStatus.Success),
                                                 new TabularCell("OK", "OK", VerificationStatus.Success)]),
                                             new TabularRow(TableRowType.Surplus,
                                                [new TabularCell("99", null, VerificationStatus.NotApplicable),
                                                 new TabularCell("Extra", null, VerificationStatus.NotApplicable)])])
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains("step-param-combined-table", content);
        Assert.Contains("row-surplus", content);
    }

    // === Tier 3: Row-count fallback ===

    [Fact]
    public void Combined_table_renders_with_matching_row_count_greater_than_one()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Test",
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Given", Text = "data",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "input",
                                        Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("Ingredient", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("Eggs", null, VerificationStatus.NotApplicable)]),
                                             new TabularRow(TableRowType.Matching,
                                                [new TabularCell("Flour", null, VerificationStatus.NotApplicable)])])
                                    }
                                ]
                            },
                            new ScenarioStep { Keyword = "When", Text = "baked" },
                            new ScenarioStep
                            {
                                Keyword = "Then", Text = "results",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "output",
                                        Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("Status", false), new TabularColumn("Message", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("OK", "OK", VerificationStatus.Success),
                                                 new TabularCell("Good", "Good", VerificationStatus.Success)]),
                                             new TabularRow(TableRowType.Matching,
                                                [new TabularCell("OK", "OK", VerificationStatus.Success),
                                                 new TabularCell("Fine", "Fine", VerificationStatus.Success)])])
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains("step-param-combined-table", content);
        Assert.Contains("<th class=\"combined-separator\">=</th>", content);
    }

    [Fact]
    public void No_combined_table_when_single_row_and_no_keys_or_linked()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Test",
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Given", Text = "data",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "input",
                                        Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("Field", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("X", null, VerificationStatus.NotApplicable)])])
                                    }
                                ]
                            },
                            new ScenarioStep { Keyword = "When", Text = "processed" },
                            new ScenarioStep
                            {
                                Keyword = "Then", Text = "results",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "output",
                                        Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("Status", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("OK", "OK", VerificationStatus.Success)])])
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.DoesNotContain("<div class=\"step-param-combined-table\">", content);
        Assert.Contains("<div class=\"step-param-table\" data-param=\"output\"", content);
    }

    [Fact]
    public void No_combined_table_when_different_row_counts_and_no_keys()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Test",
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Given", Text = "data",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "input",
                                        Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("Field", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("A", null, VerificationStatus.NotApplicable)]),
                                             new TabularRow(TableRowType.Matching,
                                                [new TabularCell("B", null, VerificationStatus.NotApplicable)]),
                                             new TabularRow(TableRowType.Matching,
                                                [new TabularCell("C", null, VerificationStatus.NotApplicable)])])
                                    }
                                ]
                            },
                            new ScenarioStep { Keyword = "When", Text = "processed" },
                            new ScenarioStep
                            {
                                Keyword = "Then", Text = "results",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "output",
                                        Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("Status", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("OK", "OK", VerificationStatus.Success)]),
                                             new TabularRow(TableRowType.Matching,
                                                [new TabularCell("OK", "OK", VerificationStatus.Success)])])
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.DoesNotContain("<div class=\"step-param-combined-table\">", content);
        Assert.Contains("<div class=\"step-param-table\" data-param=\"input\"", content);
        Assert.Contains("<div class=\"step-param-table\" data-param=\"output\"", content);
    }

    [Fact]
    public void Step_param_table_left_margin_aligns_with_step_text()
    {
        // The step-param-table should be indented to align with the start of the step text,
        // not with the tick/status indicator. The text starts after: ::before (0.3em) +
        // status margin-left (0.5em) + status width (1.2em) + status margin-right (0.3em) = ~2.3em
        Assert.Contains(".step-param-table", Stylesheets.HtmlReportStyleSheet);
        Assert.Contains("margin: 4px 0 4px 2.3em", Stylesheets.HtmlReportStyleSheet);
    }

    [Fact]
    public void Report_renders_null_tabular_cell_as_pre_null()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Test",
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Given", Text = "data",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "data",
                                        Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("Value", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("null", null, VerificationStatus.NotApplicable)])])
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains("<pre>null</pre>", content);
    }

    [Fact]
    public void Report_renders_empty_string_tabular_cell_without_pre_null()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Test",
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Given", Text = "data",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "data",
                                        Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("Value", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("", null, VerificationStatus.NotApplicable)])])
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.DoesNotContain("<pre>null</pre>", content);
    }

    [Fact]
    public void Report_renders_null_inline_parameter_as_pre_null()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Test",
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Given", Text = "value is <v>",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "v",
                                        Kind = StepParameterKind.Inline,
                                        InlineValue = new InlineParameterValue("null", null, VerificationStatus.NotApplicable)
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains("<pre>null</pre>", content);
    }

    [Fact]
    public void Report_renders_null_tree_node_value_as_pre_null()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Test",
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Then", Text = "result matches",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "result",
                                        Kind = StepParameterKind.Tree,
                                        TreeValue = new TreeParameterValue(
                                            new TreeNode("$", "root", "null", null, VerificationStatus.NotApplicable, null))
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains("<pre>null</pre>", content);
    }

    [Fact]
    public void Report_renders_null_combined_table_cell_as_pre_null()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Test",
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Given", Text = "input data",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "data",
                                        Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("Name", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("null", null, VerificationStatus.NotApplicable)])])
                                    }
                                ]
                            },
                            new ScenarioStep
                            {
                                Keyword = "Then", Text = "results match",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "result",
                                        Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("Status", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("OK", "OK", VerificationStatus.Success)])])
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.DoesNotContain("<div class=\"step-param-combined-table\">", content);
        Assert.Contains("<pre>null</pre>", content);
    }

    [Fact]
    public void Report_renders_null_step_text_segment_parameter_as_pre_null()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Test",
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Given", Text = "value is <v>",
                                TextSegments =
                                [
                                    StepTextSegment.Literal("value is "),
                                    StepTextSegment.Param("v", new InlineParameterValue("null", null, VerificationStatus.NotApplicable))
                                ]
                            }
                        ]
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains("<pre>null</pre>", content);
    }

    [Fact]
    public void Report_renders_null_expectation_in_failure_cell_as_pre_null()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Test",
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Then", Text = "results match",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "result",
                                        Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("Value", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("hello", "null", VerificationStatus.Failure)])])
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains("hello/<pre>null</pre>", content);
    }

    [Fact]
    public void Input_parameter_table_renders_null_as_pre_null()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Test(null)",
                        OutlineId = "outline1",
                        ExampleValues = new Dictionary<string, string> { ["value"] = "null" }
                    },
                    new Scenario
                    {
                        Id = "s2", DisplayName = "Test(hello)",
                        OutlineId = "outline1",
                        ExampleValues = new Dictionary<string, string> { ["value"] = "hello" }
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains("<pre>null</pre>", content);
    }

    [Fact]
    public void Input_parameter_table_renders_whitespace_in_pre_element()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Test( )",
                        OutlineId = "outline1",
                        ExampleValues = new Dictionary<string, string> { ["value"] = " " }
                    },
                    new Scenario
                    {
                        Id = "s2", DisplayName = "Test(hello)",
                        OutlineId = "outline1",
                        ExampleValues = new Dictionary<string, string> { ["value"] = "hello" }
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains("<pre> </pre>", content);
    }

    [Fact]
    public void Input_parameter_table_renders_empty_string_without_pre()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Test()",
                        OutlineId = "outline1",
                        ExampleValues = new Dictionary<string, string> { ["value"] = "" }
                    },
                    new Scenario
                    {
                        Id = "s2", DisplayName = "Test(hello)",
                        OutlineId = "outline1",
                        ExampleValues = new Dictionary<string, string> { ["value"] = "hello" }
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.DoesNotContain("<pre>null</pre>", content);
        Assert.DoesNotContain("<pre></pre>", content);
        Assert.DoesNotContain("<pre> </pre>", content);
    }

    [Fact]
    public void Parameterized_scenario_step_param_table_should_not_be_targeted_by_column_highlighting()
    {
        // When a step-param-table column name matches an outline parameter name
        // (e.g., both have "Name"), the highlightColumns JS should NOT apply col-highlight
        // to that column. The highlighting is only intended for the examples table columns,
        // not for independent DataTable step arguments.
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Place order",
                        OutlineId = "Place order",
                        ExampleValues = new Dictionary<string, string> { ["Name"] = "Alice", ["Amount"] = "10" },
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Given", Text = "a user",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "table",
                                        Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("Name", false), new TabularColumn("Email", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("Alice", null, VerificationStatus.NotApplicable),
                                                 new TabularCell("alice@test.com", null, VerificationStatus.NotApplicable)])])
                                    }
                                ]
                            },
                            new ScenarioStep { Keyword = "When", Text = "placing an order" },
                            new ScenarioStep { Keyword = "Then", Text = "order confirmed" }
                        ]
                    },
                    new Scenario
                    {
                        Id = "s2", DisplayName = "Place order",
                        OutlineId = "Place order",
                        ExampleValues = new Dictionary<string, string> { ["Name"] = "Bob", ["Amount"] = "5" },
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Given", Text = "a user",
                                Parameters =
                                [
                                    new StepParameter
                                    {
                                        Name = "table",
                                        Kind = StepParameterKind.Tabular,
                                        TabularValue = new TabularParameterValue(
                                            [new TabularColumn("Name", false), new TabularColumn("Email", false)],
                                            [new TabularRow(TableRowType.Matching,
                                                [new TabularCell("Bob", null, VerificationStatus.NotApplicable),
                                                 new TabularCell("bob@test.com", null, VerificationStatus.NotApplicable)])])
                                    }
                                ]
                            },
                            new ScenarioStep { Keyword = "When", Text = "placing an order" },
                            new ScenarioStep { Keyword = "Then", Text = "order confirmed" }
                        ]
                    }
                ]
            }
        };

        var content = GenerateReport(features);

        // The highlightColumns JS should NOT iterate over .step-param-table elements
        Assert.DoesNotContain("'.step-param-table'", content);
    }
}
