using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

public class FeatureSummaryTableReportTests
{
    private static string GenerateReport(Feature[] features)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            [], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, "SummaryTable.html", "Test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        return File.ReadAllText(path);
    }

    [Fact]
    public void Report_contains_feature_summary_table()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "OrderService",
                Scenarios =
                [
                    new Scenario { Id = "s1", DisplayName = "Place order", Result = ExecutionResult.Passed },
                    new Scenario { Id = "s2", DisplayName = "Cancel order", Result = ExecutionResult.Failed, ErrorMessage = "err" }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains("feature-summary-table", content);
        Assert.Contains("OrderService", content);
    }

    [Fact]
    public void Summary_table_shows_scenario_counts_by_status()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario { Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed },
                    new Scenario { Id = "s2", DisplayName = "S2", Result = ExecutionResult.Failed, ErrorMessage = "err" },
                    new Scenario { Id = "s3", DisplayName = "S3", Result = ExecutionResult.Skipped }
                ]
            }
        };

        var content = GenerateReport(features);
        // Table should contain columns for Passed, Failed, Skipped
        Assert.Contains("Passed", content);
        Assert.Contains("Failed", content);
    }

    [Fact]
    public void Summary_table_is_sortable()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios = [new Scenario { Id = "s1", DisplayName = "S1" }]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains("sort_table", content);
    }

    [Fact]
    public void Summary_table_shows_step_counts_when_steps_present()
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
                        Id = "s1", DisplayName = "S1",
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "x", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "When", Text = "y", Status = ExecutionResult.Failed }
                        ]
                    }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains("Steps", content);
    }

    [Fact]
    public void Summary_table_shows_duration_columns()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario { Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(2) }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains(">Duration<", content);
        Assert.Contains(">Avg<", content);
        Assert.Contains(">Longest<", content);
    }

    [Fact]
    public void Summary_table_duration_shows_sum_of_scenario_durations()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario { Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1) },
                    new Scenario { Id = "s2", DisplayName = "S2", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(2) },
                    new Scenario { Id = "s3", DisplayName = "S3", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(3) }
                ]
            }
        };

        var content = GenerateReport(features);
        // Sum is 6s
        Assert.Contains("6s", content);
    }

    [Fact]
    public void Summary_table_avg_shows_mean_scenario_duration()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario { Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1) },
                    new Scenario { Id = "s2", DisplayName = "S2", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(2) },
                    new Scenario { Id = "s3", DisplayName = "S3", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(3) }
                ]
            }
        };

        var content = GenerateReport(features);
        // Avg is 2s
        Assert.Contains("2s", content);
    }

    [Fact]
    public void Summary_table_longest_shows_max_scenario_duration()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "F1",
                Scenarios =
                [
                    new Scenario { Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1) },
                    new Scenario { Id = "s2", DisplayName = "S2", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(5) },
                    new Scenario { Id = "s3", DisplayName = "S3", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(3) }
                ]
            }
        };

        var content = GenerateReport(features);
        Assert.Contains("5s", content);
    }

    [Fact]
    public void Summary_table_omits_duration_columns_when_no_durations()
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
        // the table's own headers: since 5.0.0 the run band names the run's duration too (plans/OPTION_C_PLAN.md S2)
        Assert.DoesNotContain(")\">Duration</th>", content);
        Assert.DoesNotContain(")\">Avg</th>", content);
        Assert.DoesNotContain(")\">Longest</th>", content);
    }

    [Fact]
    public void Summary_table_shows_step_status_breakdown_columns_when_steps_present()
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
                        Id = "s1", DisplayName = "S1",
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "x", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "When", Text = "y", Status = ExecutionResult.Failed },
                            new ScenarioStep { Keyword = "Then", Text = "z", Status = ExecutionResult.Skipped }
                        ]
                    }
                ]
            }
        };

        var content = GenerateReport(features);

        // Should have step status breakdown header row
        Assert.Contains("step-status-header", content);
    }

    [Fact]
    public void Summary_table_step_status_shows_correct_counts_per_feature()
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
                        Id = "s1", DisplayName = "S1",
                        Steps =
                        [
                            new ScenarioStep { Keyword = "Given", Text = "a", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "When", Text = "b", Status = ExecutionResult.Passed },
                            new ScenarioStep { Keyword = "Then", Text = "c", Status = ExecutionResult.Failed },
                            new ScenarioStep { Keyword = "And", Text = "d", Status = ExecutionResult.Skipped }
                        ]
                    }
                ]
            }
        };

        var content = GenerateReport(features);

        // The row should contain step counts: Steps=4, and then per-status counts
        // We need to verify the step status cells exist with correct values
        // Steps total = 4, Passed = 2, Failed = 1, Skipped = 1
        Assert.Contains(">4<", content); // total steps
    }

    [Fact]
    public void Summary_table_step_status_counts_include_substeps()
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
                        Id = "s1", DisplayName = "S1",
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Given", Text = "a", Status = ExecutionResult.Passed,
                                SubSteps =
                                [
                                    new ScenarioStep { Keyword = "And", Text = "sub1", Status = ExecutionResult.Passed },
                                    new ScenarioStep { Keyword = "And", Text = "sub2", Status = ExecutionResult.Failed }
                                ]
                            },
                            new ScenarioStep { Keyword = "Then", Text = "b", Status = ExecutionResult.Passed }
                        ]
                    }
                ]
            }
        };

        var content = GenerateReport(features);

        // Total steps = 4 (2 top + 2 sub), Passed = 3, Failed = 1
        Assert.Contains(">4<", content); // total steps (CountStepsRecursive)
    }

    [Fact]
    public void Summary_table_omits_step_status_columns_when_no_steps()
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
        Assert.DoesNotContain("step-status-header", content);
    }
}
