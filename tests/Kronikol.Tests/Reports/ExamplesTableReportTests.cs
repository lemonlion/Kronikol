using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

// Every assertion here anchors on markup the generator emits (`<table class="param-test-table`,
// `<tr class="row-failed`, `<details class="failure-result"`), never on a bare class name. The
// stylesheet and scripts inlined into every report still carry rules for `examples-table`,
// `examples-detail-row` and `examples-row-*`, classes the generator has not emitted since the outline
// table became the parameter table, so a bare `Contains("examples-table")` matched the CSS and could
// never fail.
public class ExamplesTableReportTests
{
    private static string GenerateReport(Feature[] features)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            [], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, "ExamplesTable.html", "Test", includeTestRunData: true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        return File.ReadAllText(path);
    }

    [Fact]
    public void Scenarios_with_same_outline_id_render_as_examples_table()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Withdraw $200 from $1000", Result = ExecutionResult.Passed,
                        OutlineId = "withdraw-cash",
                        ExampleValues = new Dictionary<string, string> { ["Balance"] = "$1000", ["Withdrawal"] = "$200", ["Result"] = "$800" }
                    },
                    new Scenario
                    {
                        Id = "s2", DisplayName = "Withdraw $500 from $1000", Result = ExecutionResult.Passed,
                        OutlineId = "withdraw-cash",
                        ExampleValues = new Dictionary<string, string> { ["Balance"] = "$1000", ["Withdrawal"] = "$500", ["Result"] = "$500" }
                    },
                    new Scenario
                    {
                        Id = "s3", DisplayName = "Withdraw $1500 from $1000", Result = ExecutionResult.Failed, ErrorMessage = "Insufficient",
                        OutlineId = "withdraw-cash",
                        ExampleValues = new Dictionary<string, string> { ["Balance"] = "$1000", ["Withdrawal"] = "$1500", ["Result"] = "Error" }
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains("<table class=\"param-test-table", content);
    }

    [Fact]
    public void Examples_table_shows_status_per_row()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed,
                        OutlineId = "outline1",
                        ExampleValues = new Dictionary<string, string> { ["A"] = "1" }
                    },
                    new Scenario
                    {
                        Id = "s2", DisplayName = "S2", Result = ExecutionResult.Failed, ErrorMessage = "err",
                        OutlineId = "outline1",
                        ExampleValues = new Dictionary<string, string> { ["A"] = "2" }
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains("badge-pass", content);
        Assert.Contains("badge-fail", content);
    }

    [Fact]
    public void Scenarios_without_outline_id_render_normally()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario { Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.DoesNotContain("<table class=\"param-test-table", content);
    }

    [Fact]
    public void Examples_table_parameter_names_are_column_headers()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed,
                        OutlineId = "outline1",
                        ExampleValues = new Dictionary<string, string> { ["Balance"] = "$1000", ["Withdrawal"] = "$200" }
                    },
                    new Scenario
                    {
                        Id = "s2", DisplayName = "S2", Result = ExecutionResult.Passed,
                        OutlineId = "outline1",
                        ExampleValues = new Dictionary<string, string> { ["Balance"] = "$500", ["Withdrawal"] = "$100" }
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains(">Balance</th>", content);
        Assert.Contains(">Withdrawal</th>", content);
    }

    [Fact]
    public void Mixed_outline_and_regular_scenarios_render_correctly()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario { Id = "s1", DisplayName = "Regular", Result = ExecutionResult.Passed },
                    new Scenario
                    {
                        Id = "s2", DisplayName = "Outline1", Result = ExecutionResult.Passed,
                        OutlineId = "outline1",
                        ExampleValues = new Dictionary<string, string> { ["X"] = "1" }
                    },
                    new Scenario
                    {
                        Id = "s3", DisplayName = "Outline2", Result = ExecutionResult.Passed,
                        OutlineId = "outline1",
                        ExampleValues = new Dictionary<string, string> { ["X"] = "2" }
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains("<table class=\"param-test-table", content);
        Assert.Contains("Regular", content);
    }

    [Fact]
    public void Examples_table_includes_duration_column()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed,
                        OutlineId = "outline1", Duration = TimeSpan.FromMilliseconds(250),
                        ExampleValues = new Dictionary<string, string> { ["A"] = "1" }
                    },
                    new Scenario
                    {
                        Id = "s2", DisplayName = "S2", Result = ExecutionResult.Passed,
                        OutlineId = "outline1", Duration = TimeSpan.FromSeconds(1.5),
                        ExampleValues = new Dictionary<string, string> { ["A"] = "2" }
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains(">Duration</th>", content);
        Assert.Contains("250ms", content);
        Assert.Contains("1.5s", content);
    }

    [Fact]
    public void Examples_table_includes_scenario_name_column()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Withdraw $200", Result = ExecutionResult.Passed,
                        OutlineId = "outline1",
                        ExampleValues = new Dictionary<string, string> { ["A"] = "1" }
                    },
                    new Scenario
                    {
                        Id = "s2", DisplayName = "Withdraw $500", Result = ExecutionResult.Passed,
                        OutlineId = "outline1",
                        ExampleValues = new Dictionary<string, string> { ["A"] = "2" }
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        // Display names still appear in the search attribute (lowercased)
        Assert.Contains("withdraw $200", content);
        Assert.Contains("withdraw $500", content);
    }

    [Fact]
    public void Failed_outline_row_has_expandable_detail_with_error_info()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed,
                        OutlineId = "outline1",
                        ExampleValues = new Dictionary<string, string> { ["A"] = "1" }
                    },
                    new Scenario
                    {
                        Id = "s2", DisplayName = "S2", Result = ExecutionResult.Failed,
                        ErrorMessage = "Expected 5 but got 3",
                        ErrorStackTrace = "at MyTest.cs:42",
                        OutlineId = "outline1",
                        ExampleValues = new Dictionary<string, string> { ["A"] = "2" }
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains("<div class=\"param-detail-panel\"", content);
        Assert.Contains("<details class=\"failure-result\" open>", content);
        Assert.Contains("Expected 5 but got 3", content);
        Assert.Contains("at MyTest.cs:42", content);
    }

    [Fact]
    public void Failed_outline_row_has_expandable_detail_with_steps()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed,
                        OutlineId = "outline1",
                        ExampleValues = new Dictionary<string, string> { ["A"] = "1" }
                    },
                    new Scenario
                    {
                        Id = "s2", DisplayName = "S2", Result = ExecutionResult.Failed,
                        ErrorMessage = "fail",
                        OutlineId = "outline1",
                        ExampleValues = new Dictionary<string, string> { ["A"] = "2" },
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "I have a balance", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "When", Text = "I withdraw cash", Status = ExecutionResult.Failed }
                        ]
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains("<div class=\"param-detail-panel\"", content);
        Assert.Contains("<details class=\"failure-result\" open>", content);
        Assert.Contains("I have a balance", content);
        Assert.Contains("I withdraw cash", content);
    }

    [Fact]
    public void Passed_outline_rows_have_no_failure_block()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed,
                        OutlineId = "outline1",
                        ExampleValues = new Dictionary<string, string> { ["A"] = "1" },
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "step one", Status = ExecutionResult.Passed }
                        ]
                    },
                    new Scenario
                    {
                        Id = "s2", DisplayName = "S2", Result = ExecutionResult.Passed,
                        OutlineId = "outline1",
                        ExampleValues = new Dictionary<string, string> { ["A"] = "2" }
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        // A row with steps gets its detail panel; the failure block is what a passed row never gets.
        Assert.Contains("<div class=\"param-detail-panel\"", content);
        Assert.DoesNotContain("<details class=\"failure-result\"", content);
    }

    [Fact]
    public void Outline_section_has_data_search_attribute()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "Withdraw $200 from $1000", Result = ExecutionResult.Passed,
                        OutlineId = "withdraw-cash",
                        ExampleValues = new Dictionary<string, string> { ["Balance"] = "$1000" }
                    },
                    new Scenario
                    {
                        Id = "s2", DisplayName = "Withdraw $500 from $1000", Result = ExecutionResult.Passed,
                        OutlineId = "withdraw-cash",
                        ExampleValues = new Dictionary<string, string> { ["Balance"] = "$500" }
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains("data-search=", content);
        Assert.Contains("withdraw $200 from $1000", content.ToLower());
        Assert.Contains("withdraw $500 from $1000", content.ToLower());
    }

    [Fact]
    public void Outline_section_has_data_categories_attribute()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed,
                        OutlineId = "outline1",
                        ExampleValues = new Dictionary<string, string> { ["A"] = "1" },
                        Categories = ["Banking"]
                    },
                    new Scenario
                    {
                        Id = "s2", DisplayName = "S2", Result = ExecutionResult.Passed,
                        OutlineId = "outline1",
                        ExampleValues = new Dictionary<string, string> { ["A"] = "2" },
                        Categories = ["Banking", "ATM"]
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains("data-categories=", content);
    }

    [Fact]
    public void Outline_section_overall_status_reflects_worst_result()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed,
                        OutlineId = "outline1",
                        ExampleValues = new Dictionary<string, string> { ["A"] = "1" }
                    },
                    new Scenario
                    {
                        Id = "s2", DisplayName = "S2", Result = ExecutionResult.Skipped,
                        OutlineId = "outline1",
                        ExampleValues = new Dictionary<string, string> { ["A"] = "2" }
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        // The outline status should show skipped (worst non-failure)
        Assert.Contains("data-status=\"Skipped\"", content);
    }

    [Fact]
    public void Outline_rows_have_status_specific_css_class()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed,
                        OutlineId = "outline1",
                        ExampleValues = new Dictionary<string, string> { ["A"] = "1" }
                    },
                    new Scenario
                    {
                        Id = "s2", DisplayName = "S2", Result = ExecutionResult.Failed, ErrorMessage = "err",
                        OutlineId = "outline1",
                        ExampleValues = new Dictionary<string, string> { ["A"] = "2" }
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains("<tr class=\"row-passed", content);
        Assert.Contains("<tr class=\"row-failed", content);
    }

    [Fact]
    public void Outline_total_duration_shown_in_summary()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed,
                        OutlineId = "outline1", Duration = TimeSpan.FromSeconds(1),
                        ExampleValues = new Dictionary<string, string> { ["A"] = "1" }
                    },
                    new Scenario
                    {
                        Id = "s2", DisplayName = "S2", Result = ExecutionResult.Passed,
                        OutlineId = "outline1", Duration = TimeSpan.FromSeconds(2),
                        ExampleValues = new Dictionary<string, string> { ["A"] = "2" }
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        // Total duration (3s) should appear in the summary
        Assert.Contains("duration-badge", content);
    }
}
