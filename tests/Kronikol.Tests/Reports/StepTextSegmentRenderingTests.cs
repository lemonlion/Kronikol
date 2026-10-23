using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

public class StepTextSegmentRenderingTests
{
    private static string GenerateReport(Feature[] features)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            [], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, "TextSegments.html", "Test", includeTestRunData: true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        return ReportHtml.Read(path);
    }

    private static Feature[] FeaturesWithStep(ScenarioStep step) =>
    [
        new Feature
        {
            DisplayName = "F1",
            Scenarios =
            [
                new Scenario
                {
                    Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed,
                    Steps = [step]
                }
            ]
        }
    ];

    [Fact]
    public void TextSegments_renders_inline_param_within_step_text()
    {
        var step = new ScenarioStep
        {
            Keyword = "Given",
            Text = "customer has \"105\" in account",
            Status = ExecutionResult.Passed,
            TextSegments =
            [
                StepTextSegment.Literal("customer has "),
                StepTextSegment.Param("amount", new InlineParameterValue("105", null, VerificationStatus.Success)),
                StepTextSegment.Literal(" in account")
            ]
        };

        var content = GenerateReport(FeaturesWithStep(step));

        // The parameter should be embedded WITHIN the step text span area, not as a separate badge
        Assert.Contains("customer has ", content);
        Assert.Contains("step-param-inline", content);
        Assert.Contains(">105<", content);
        Assert.Contains("param-success", content);
        Assert.Contains(" in account", content);
    }

    [Fact]
    public void TextSegments_renders_failed_verification_inline()
    {
        var step = new ScenarioStep
        {
            Keyword = "Then",
            Text = "price should be \"99/100\"",
            Status = ExecutionResult.Failed,
            TextSegments =
            [
                StepTextSegment.Literal("price should be "),
                StepTextSegment.Param("price", new InlineParameterValue("99", "100", VerificationStatus.Failure))
            ]
        };

        var content = GenerateReport(FeaturesWithStep(step));

        Assert.Contains("param-failure", content);
        Assert.Contains("99/100", content);
    }

    [Fact]
    public void TextSegments_renders_multiple_params_inline()
    {
        var step = new ScenarioStep
        {
            Keyword = "When",
            Text = "user \"Alice\" buys \"3\" items",
            Status = ExecutionResult.Passed,
            TextSegments =
            [
                StepTextSegment.Literal("user "),
                StepTextSegment.Param("name", new InlineParameterValue("Alice", null, VerificationStatus.NotApplicable)),
                StepTextSegment.Literal(" buys "),
                StepTextSegment.Param("count", new InlineParameterValue("3", null, VerificationStatus.NotApplicable)),
                StepTextSegment.Literal(" items")
            ]
        };

        var content = GenerateReport(FeaturesWithStep(step));

        // Both params should be inline
        Assert.Contains(">Alice<", content);
        Assert.Contains(">3<", content);
        // Should NOT contain the old-style step-text with quoted values
        Assert.DoesNotContain("&quot;Alice&quot;", content);
    }

    [Fact]
    public void TextSegments_param_has_name_tooltip()
    {
        var step = new ScenarioStep
        {
            Keyword = "Given",
            Text = "balance is \"500\"",
            Status = ExecutionResult.Passed,
            TextSegments =
            [
                StepTextSegment.Literal("balance is "),
                StepTextSegment.Param("amount", new InlineParameterValue("500", null, VerificationStatus.Success))
            ]
        };

        var content = GenerateReport(FeaturesWithStep(step));

        Assert.Contains("title=\"amount\"", content);
    }

    [Fact]
    public void TextSegments_null_falls_back_to_plain_text()
    {
        var step = new ScenarioStep
        {
            Keyword = "Given",
            Text = "a plain step",
            Status = ExecutionResult.Passed,
            TextSegments = null
        };

        var content = GenerateReport(FeaturesWithStep(step));

        Assert.Contains("a plain step", content);
    }

    [Fact]
    public void TextSegments_html_encodes_literal_text()
    {
        var step = new ScenarioStep
        {
            Keyword = "Given",
            Text = "value <script>xss</script>",
            Status = ExecutionResult.Passed,
            TextSegments =
            [
                StepTextSegment.Literal("value <script>xss</script>")
            ]
        };

        var content = GenerateReport(FeaturesWithStep(step));

        Assert.Contains("&lt;script&gt;xss&lt;/script&gt;", content);
        Assert.DoesNotContain("<script>xss</script>", content);
    }

    [Fact]
    public void TextSegments_html_encodes_param_value()
    {
        var step = new ScenarioStep
        {
            Keyword = "Given",
            Text = "input is \"<b>bold</b>\"",
            Status = ExecutionResult.Passed,
            TextSegments =
            [
                StepTextSegment.Literal("input is "),
                StepTextSegment.Param("val", new InlineParameterValue("<b>bold</b>", null, VerificationStatus.NotApplicable))
            ]
        };

        var content = GenerateReport(FeaturesWithStep(step));

        Assert.Contains("&lt;b&gt;bold&lt;/b&gt;", content);
        Assert.DoesNotContain("<b>bold</b>", content);
    }

    [Fact]
    public void TextSegments_does_not_render_separate_inline_param_badges()
    {
        // When TextSegments is used, inline parameters should NOT also appear as separate badges
        var step = new ScenarioStep
        {
            Keyword = "Given",
            Text = "customer has \"105\" in account",
            Status = ExecutionResult.Passed,
            TextSegments =
            [
                StepTextSegment.Literal("customer has "),
                StepTextSegment.Param("amount", new InlineParameterValue("105", null, VerificationStatus.Success)),
                StepTextSegment.Literal(" in account")
            ],
            Parameters =
            [
                new StepParameter
                {
                    Name = "amount",
                    Kind = StepParameterKind.Inline,
                    InlineValue = new InlineParameterValue("105", null, VerificationStatus.Success)
                }
            ]
        };

        var content = GenerateReport(FeaturesWithStep(step));

        // Count occurrences of rendered param spans - should be exactly 1 (inline in text), not 2
        var paramCount = content.Split("<span class=\"step-param-inline").Length - 1;
        Assert.Equal(1, paramCount);
    }

    [Fact]
    public void TableRef_segment_renders_toggle_button()
    {
        var step = new ScenarioStep
        {
            Keyword = "Then",
            Text = "step verifies items",
            Status = ExecutionResult.Passed,
            TextSegments =
            [
                StepTextSegment.Literal("step verifies "),
                StepTextSegment.TableRef("items")
            ],
            Parameters =
            [
                new StepParameter
                {
                    Name = "items",
                    Kind = StepParameterKind.Tabular,
                    TabularValue = new TabularParameterValue(
                        [new TabularColumn("Name", false)],
                        [new TabularRow(TableRowType.Matching, [new TabularCell("X", null, VerificationStatus.NotApplicable)])])
                }
            ]
        };

        var content = GenerateReport(FeaturesWithStep(step));

        // Verify toggle button is rendered
        Assert.Contains("step-table-ref", content);
        Assert.Contains("data-param=\"items\"", content);
        Assert.Contains("toggle_table_ref", content);
    }

    [Fact]
    public void TableRef_segment_makes_table_start_expanded()
    {
        var step = new ScenarioStep
        {
            Keyword = "Then",
            Text = "step verifies items",
            Status = ExecutionResult.Passed,
            TextSegments =
            [
                StepTextSegment.Literal("step verifies "),
                StepTextSegment.TableRef("items")
            ],
            Parameters =
            [
                new StepParameter
                {
                    Name = "items",
                    Kind = StepParameterKind.Tabular,
                    TabularValue = new TabularParameterValue(
                        [new TabularColumn("Name", false)],
                        [new TabularRow(TableRowType.Matching, [new TabularCell("X", null, VerificationStatus.NotApplicable)])])
                }
            ]
        };

        var content = GenerateReport(FeaturesWithStep(step));

        // Table should always be visible (no collapse behavior)
        Assert.Contains("class=\"step-param-table\"", content);
        Assert.DoesNotContain("step-param-table-collapsed", content);
    }

    [Fact]
    public void Toggle_table_ref_JS_uses_scrollIntoView_and_highlight()
    {
        var step = new ScenarioStep
        {
            Keyword = "Given",
            Text = "a recipe",
            Status = ExecutionResult.Passed,
            TextSegments =
            [
                StepTextSegment.Literal("a "),
                StepTextSegment.TableRef("recipe")
            ]
        };

        var content = GenerateReport(FeaturesWithStep(step));

        // The toggle_table_ref function should scroll to and highlight, not toggle visibility
        Assert.Contains("scrollIntoView", content);
        Assert.Contains("step-param-highlight", content);
        Assert.Contains("step-param-combined-table", content);
        Assert.Contains("btn.closest('.scenario')", content);

        // Should NOT contain toggle/collapse logic
        Assert.DoesNotContain("step-param-table-collapsed", content);
    }

    [Fact]
    public void TableRef_with_small_inline_value_renders_as_inline_span()
    {
        var step = new ScenarioStep
        {
            Keyword = "Given",
            Text = "a recipe",
            Status = ExecutionResult.Passed,
            TextSegments =
            [
                StepTextSegment.Literal("a "),
                StepTextSegment.TableRef("recipe")
            ],
            Parameters =
            [
                new StepParameter
                {
                    Name = "recipe",
                    Kind = StepParameterKind.Inline,
                    InlineValue = new InlineParameterValue(
                        "MuffinRecipeTestData { Name = Classic, Flour = Plain Flour }",
                        null, VerificationStatus.NotApplicable)
                }
            ]
        };

        var content = GenerateReport(FeaturesWithStep(step));

        // Should render as inline span, NOT as a button
        Assert.Contains("step-param-inline", content);
        Assert.Contains("{ Name: Classic, Flour: Plain Flour }", content);
        Assert.DoesNotContain("data-value=", content);
    }

    [Fact]
    public void TableRef_with_large_inline_value_renders_as_expandable_button()
    {
        var step = new ScenarioStep
        {
            Keyword = "Given",
            Text = "a config",
            Status = ExecutionResult.Passed,
            TextSegments =
            [
                StepTextSegment.Literal("a "),
                StepTextSegment.TableRef("config")
            ],
            Parameters =
            [
                new StepParameter
                {
                    Name = "config",
                    Kind = StepParameterKind.Inline,
                    InlineValue = new InlineParameterValue(
                        "Config { A = 1, B = 2, C = 3, D = 4, E = 5 }",
                        null, VerificationStatus.NotApplicable)
                }
            ]
        };

        var content = GenerateReport(FeaturesWithStep(step));

        // Should render as button with data-value attribute
        Assert.Contains("step-table-ref", content);
        Assert.Contains("data-value=", content);
        Assert.Contains("data-param=\"config\"", content);
        // The JSON should be in the data-value
        Assert.Contains("&quot;A&quot;: 1", content);
    }

    [Fact]
    public void TableRef_with_tabular_param_still_renders_as_table_button()
    {
        var step = new ScenarioStep
        {
            Keyword = "Then",
            Text = "step verifies items",
            Status = ExecutionResult.Passed,
            TextSegments =
            [
                StepTextSegment.Literal("step verifies "),
                StepTextSegment.TableRef("items")
            ],
            Parameters =
            [
                new StepParameter
                {
                    Name = "items",
                    Kind = StepParameterKind.Tabular,
                    TabularValue = new TabularParameterValue(
                        [new TabularColumn("Name", false)],
                        [new TabularRow(TableRowType.Matching, [new TabularCell("X", null, VerificationStatus.NotApplicable)])])
                }
            ]
        };

        var content = GenerateReport(FeaturesWithStep(step));

        // Should render as regular button (no data-value), with backing table
        Assert.Contains("step-table-ref", content);
        Assert.DoesNotContain("data-value=", content);
        Assert.Contains("step-param-table", content);
    }

    [Fact]
    public void Toggle_table_ref_JS_handles_data_value_expansion()
    {
        var step = new ScenarioStep
        {
            Keyword = "Given",
            Text = "a config",
            Status = ExecutionResult.Passed,
            TextSegments =
            [
                StepTextSegment.Literal("a "),
                StepTextSegment.TableRef("config")
            ],
            Parameters =
            [
                new StepParameter
                {
                    Name = "config",
                    Kind = StepParameterKind.Inline,
                    InlineValue = new InlineParameterValue(
                        "Config { A = 1, B = 2, C = 3, D = 4, E = 5 }",
                        null, VerificationStatus.NotApplicable)
                }
            ]
        };

        var content = GenerateReport(FeaturesWithStep(step));

        // JS should handle expansion when no table found
        Assert.Contains("step-param-expand", content);
        Assert.Contains("data-value", content);
        Assert.Contains("step-table-ref-active", content);
    }

    [Fact]
    public void TableRef_with_simple_inline_value_renders_as_inline_span()
    {
        var step = new ScenarioStep
        {
            Keyword = "Given",
            Text = "a client with grantTypes",
            Status = ExecutionResult.Passed,
            TextSegments =
            [
                StepTextSegment.Literal("a client with "),
                StepTextSegment.TableRef("grantTypes")
            ],
            Parameters =
            [
                new StepParameter
                {
                    Name = "grantTypes",
                    Kind = StepParameterKind.Inline,
                    InlineValue = new InlineParameterValue(
                        "authorisationcode",
                        null, VerificationStatus.NotApplicable)
                }
            ]
        };

        var content = GenerateReport(FeaturesWithStep(step));

        // Simple inline value should render as inline span, NOT as a dead button
        Assert.Contains("step-param-inline", content);
        Assert.Contains("authorisationcode", content);
        Assert.DoesNotContain("toggle_table_ref(this)\" data-param=\"grantTypes\"", content);
    }

    [Fact]
    public void TableRef_with_no_matching_parameter_renders_as_plain_text()
    {
        var step = new ScenarioStep
        {
            Keyword = "Given",
            Text = "a client with grants",
            Status = ExecutionResult.Passed,
            TextSegments =
            [
                StepTextSegment.Literal("a client with "),
                StepTextSegment.TableRef("grants")
            ],
            // No parameters at all
            Parameters = null
        };

        var content = GenerateReport(FeaturesWithStep(step));

        // No matching parameter: should render as plain text, NOT as a dead button
        Assert.Contains("grants", content);
        Assert.DoesNotContain("toggle_table_ref(this)\" data-param=\"grants\"", content);
    }

    [Fact]
    public void TableRef_with_formatted_value_and_no_matching_parameter_renders_value_as_inline_span()
    {
        var step = new ScenarioStep
        {
            Keyword = "Given",
            Text = "A client valid for authenticated authorisation requests",
            Status = ExecutionResult.Passed,
            TextSegments =
            [
                StepTextSegment.Literal("A client valid for authenticated authorisation requests "),
                StepTextSegment.TableRef("grantTypes", "client_credentials"),
                StepTextSegment.Literal(" "),
                StepTextSegment.TableRef("scopes", "openid, profile")
            ],
            // No parameters — CompositeStep bracket params don't have IParameterResult entries
            Parameters = null
        };

        var content = GenerateReport(FeaturesWithStep(step));

        // Should render formatted values as inline param spans, not the param names
        Assert.Contains("step-param-inline", content);
        Assert.Contains("client_credentials", content);
        Assert.Contains("openid, profile", content);
        Assert.Contains("title=\"grantTypes\"", content);
        Assert.Contains("title=\"scopes\"", content);
        // Should NOT render as plain text param names
        Assert.DoesNotContain(">grantTypes<", content);
        Assert.DoesNotContain(">scopes<", content);
        // Should NOT render as dead buttons
        Assert.DoesNotContain("toggle_table_ref(this)\" data-param=\"grantTypes\"", content);
    }

    [Fact]
    public void TableRef_with_formatted_value_but_matching_tabular_param_still_renders_as_button()
    {
        var step = new ScenarioStep
        {
            Keyword = "Then",
            Text = "step verifies items",
            Status = ExecutionResult.Passed,
            TextSegments =
            [
                StepTextSegment.Literal("step verifies "),
                StepTextSegment.TableRef("items", "some value")
            ],
            Parameters =
            [
                new StepParameter
                {
                    Name = "items",
                    Kind = StepParameterKind.Tabular,
                    TabularValue = new TabularParameterValue(
                        [new TabularColumn("Name", false)],
                        [new TabularRow(TableRowType.Matching, [new TabularCell("X", null, VerificationStatus.NotApplicable)])])
                }
            ]
        };

        var content = GenerateReport(FeaturesWithStep(step));

        // When a matching tabular param exists, it should still render as button (ignore formatted value)
        Assert.Contains("step-table-ref", content);
        Assert.Contains("step-param-table", content);
    }
}
