using System.Text.RegularExpressions;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

public class ParameterizedGroupRenderTests
{
    private static Feature[] MakeFeature(params Scenario[] scenarios) =>
    [
        new Feature
        {
            DisplayName = "Test Feature",
            Scenarios = scenarios
        }
    ];

    private static Scenario MakeScenario(string id, string displayName, ExecutionResult result = ExecutionResult.Passed,
        string? outlineId = null, Dictionary<string, string>? exampleValues = null,
        Dictionary<string, object?>? exampleRawValues = null,
        string? exampleDisplayName = null, string? errorMessage = null, string? errorStackTrace = null,
        TimeSpan? duration = null, ScenarioStep[]? steps = null, bool isHappyPath = false,
        Dictionary<string, string>? exampleFlatValues = null) =>
        new()
        {
            Id = id,
            DisplayName = displayName,
            Result = result,
            OutlineId = outlineId,
            ExampleValues = exampleValues,
            ExampleRawValues = exampleRawValues,
            ExampleDisplayName = exampleDisplayName,
            ExampleFlatValues = exampleFlatValues,
            ErrorMessage = errorMessage,
            ErrorStackTrace = errorStackTrace,
            Duration = duration,
            Steps = steps,
            IsHappyPath = isHappyPath
        };

    private static DefaultDiagramsFetcher.DiagramAsCode[] MakeDiagrams(params (string testId, string plantuml)[] diagrams) =>
        diagrams.Select(d => new DefaultDiagramsFetcher.DiagramAsCode(d.testId, "", d.plantuml)).ToArray();

    private string GenerateReport(Feature[] features, DefaultDiagramsFetcher.DiagramAsCode[]? diagrams = null, string? fileName = null,
        bool groupParameterizedTests = true, int maxParameterColumns = 10, bool titleizeParameterNames = true,
        RequestResponseLog[]? trackedLogs = null)
    {
        diagrams ??= features.SelectMany(f => f.Scenarios).Select(s => new DefaultDiagramsFetcher.DiagramAsCode(s.Id, "", "")).ToArray();
        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, fileName ?? $"ParamGroup_{Guid.NewGuid():N}.html", "Test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            groupParameterizedTests: groupParameterizedTests,
            maxParameterColumns: maxParameterColumns,
            titleizeParameterNames: titleizeParameterNames,
            trackedLogs: trackedLogs);
        return File.ReadAllText(path);
    }

    [Fact]
    public void Grouped_scenarios_render_param_test_table()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Process(region: UK)", outlineId: "Process", exampleValues: new() { ["region"] = "UK" }),
            MakeScenario("s2", "Process(region: US)", outlineId: "Process", exampleValues: new() { ["region"] = "US" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains("param-test-table", content);
        Assert.Contains("scenario-parameterized", content);
    }

    [Fact]
    public void R1_scalar_columns_shows_input_parameters_header()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Calc(a: 1, b: 2)", outlineId: "Calc",
                exampleValues: new() { ["a"] = "1", ["b"] = "2" }),
            MakeScenario("s2", "Calc(a: 3, b: 4)", outlineId: "Calc",
                exampleValues: new() { ["a"] = "3", ["b"] = "4" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains("Input Parameters", content);
        Assert.Contains("sub-header", content);
    }

    [Fact]
    public void R1_renders_parameter_name_columns()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Calc(x: 1, y: 2)", outlineId: "Calc",
                exampleValues: new() { ["x"] = "1", ["y"] = "2" }),
            MakeScenario("s2", "Calc(x: 3, y: 4)", outlineId: "Calc",
                exampleValues: new() { ["x"] = "3", ["y"] = "4" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        // Titleized by default: x → X, y → Y
        Assert.Contains(">X</th>", content);
        Assert.Contains(">Y</th>", content);
    }

    [Fact]
    public void R1_renders_parameter_values_in_cells()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Calc(x: 1)", outlineId: "Calc", exampleValues: new() { ["x"] = "1" }),
            MakeScenario("s2", "Calc(x: 42)", outlineId: "Calc", exampleValues: new() { ["x"] = "42" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains(">1</td>", content);
        Assert.Contains(">42</td>", content);
    }

    [Fact]
    public void R0_fallback_shows_test_case_column()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Happy path UK", outlineId: "Scenarios",
                exampleDisplayName: "Happy path UK"),
            MakeScenario("s2", "Happy path US", outlineId: "Scenarios",
                exampleDisplayName: "Happy path US")
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains("Test Case", content);
        Assert.Contains("Happy path UK", content);
        Assert.Contains("Happy path US", content);
    }

    [Fact]
    public void Grouped_scenarios_show_status_badges()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Test(a: 1)", outlineId: "Test",
                exampleValues: new() { ["a"] = "1" }, result: ExecutionResult.Passed),
            MakeScenario("s2", "Test(a: 2)", outlineId: "Test",
                exampleValues: new() { ["a"] = "2" }, result: ExecutionResult.Failed,
                errorMessage: "Expected true")
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains("badge-pass", content);
        Assert.Contains("badge-fail", content);
    }

    [Fact]
    public void Row_click_calls_selectRow_with_prefix()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Test(a: 1)", outlineId: "Test", exampleValues: new() { ["a"] = "1" }),
            MakeScenario("s2", "Test(a: 2)", outlineId: "Test", exampleValues: new() { ["a"] = "2" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains("selectRow(this,", content);
        Assert.Contains("data-row-idx=\"0\"", content);
        Assert.Contains("data-row-idx=\"1\"", content);
    }

    [Fact]
    public void First_row_has_active_class()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Test(a: 1)", outlineId: "Test", exampleValues: new() { ["a"] = "1" }),
            MakeScenario("s2", "Test(a: 2)", outlineId: "Test", exampleValues: new() { ["a"] = "2" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains("row-active", content);
    }

    [Fact]
    public void Failed_scenario_renders_failure_detail_in_panel()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Test(a: 1)", outlineId: "Test",
                exampleValues: new() { ["a"] = "1" }, result: ExecutionResult.Passed),
            MakeScenario("s2", "Test(a: 2)", outlineId: "Test",
                exampleValues: new() { ["a"] = "2" }, result: ExecutionResult.Failed,
                errorMessage: "Expected true but got false",
                errorStackTrace: "at Test.cs:42")
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains("param-detail-panel", content);
        Assert.Contains("failure-result", content);
        Assert.Contains("Expected true but got false", content);
    }

    [Fact]
    public void Steps_render_in_detail_panel()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Test(a: 1)", outlineId: "Test",
                exampleValues: new() { ["a"] = "1" },
                steps: [new ScenarioStep { Text = "Given a setup", Keyword = "Given" }]),
            MakeScenario("s2", "Test(a: 2)", outlineId: "Test",
                exampleValues: new() { ["a"] = "2" },
                steps: [new ScenarioStep { Text = "Given a setup", Keyword = "Given" }])
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains("param-detail-panel", content);
        Assert.Contains("scenario-steps", content);
    }

    [Fact]
    public void Parameter_table_renders_above_detail_panels()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Test(a: 1)", outlineId: "Test",
                exampleValues: new() { ["a"] = "1" },
                steps: [new ScenarioStep { Text = "Given a setup", Keyword = "Given" }]),
            MakeScenario("s2", "Test(a: 2)", outlineId: "Test",
                exampleValues: new() { ["a"] = "2" },
                steps: [new ScenarioStep { Text = "Given a setup", Keyword = "Given" }])
        };
        var content = GenerateReport(MakeFeature(scenarios));

        // Find the actual rendered HTML elements (not CSS/JS references)
        var tableIndex = content.IndexOf("<table class=\"param-test-table\"");
        var panelIndex = content.IndexOf("<div class=\"param-detail-panels\">");
        Assert.True(tableIndex > 0, "<table class=\"param-test-table\" not found");
        Assert.True(panelIndex > 0, "<div class=\"param-detail-panels\"> not found");
        Assert.True(tableIndex < panelIndex, "Parameter table should render before (above) detail panels");
    }

    [Fact]
    public void Identical_diagrams_show_badge()
    {
        var puml = "@startuml\nA -> B: hello\n@enduml";
        var scenarios = new[]
        {
            MakeScenario("s1", "Test(a: 1)", outlineId: "Test", exampleValues: new() { ["a"] = "1" }),
            MakeScenario("s2", "Test(a: 2)", outlineId: "Test", exampleValues: new() { ["a"] = "2" })
        };
        var diagrams = MakeDiagrams(("s1", puml), ("s2", puml));
        var content = GenerateReport(MakeFeature(scenarios), diagrams);

        Assert.Contains("param-diagram-identical-badge", content);
        Assert.Contains("All diagrams identical", content);
    }

    [Fact]
    public void Single_test_case_does_not_show_identical_diagrams_badge()
    {
        var puml = "@startuml\nA -> B: hello\n@enduml";
        var scenarios = new[]
        {
            MakeScenario("s1", "Test(a: 1)", outlineId: "Test", exampleValues: new() { ["a"] = "1" })
        };
        var diagrams = MakeDiagrams(("s1", puml));
        var content = GenerateReport(MakeFeature(scenarios), diagrams);

        Assert.DoesNotContain("class=\"param-diagram-identical-badge\"", content);
        Assert.DoesNotContain("All diagrams identical across test cases", content);
    }

    [Fact]
    public void Different_diagrams_render_per_row_switching()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Test(a: 1)", outlineId: "Test", exampleValues: new() { ["a"] = "1" }),
            MakeScenario("s2", "Test(a: 2)", outlineId: "Test", exampleValues: new() { ["a"] = "2" })
        };
        var diagrams = MakeDiagrams(
            ("s1", "@startuml\nA -> B: msg1\n@enduml"),
            ("s2", "@startuml\nA -> B: msg2\n@enduml"));
        var content = GenerateReport(MakeFeature(scenarios), diagrams);

        Assert.DoesNotContain("class=\"param-diagram-identical-badge\"", content);
        Assert.Contains("class=\"example-diagrams\"", content);
        // Two diagram divs with different IDs
        var diagramDivs = Regex.Matches(content, @"id=""pgrp\d+-diagram-\d+""");
        Assert.True(diagramDivs.Count >= 2);
    }

    [Fact]
    public void Grouping_disabled_renders_individual_scenarios_for_non_outline_groups()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Test(a: 1)"),
            MakeScenario("s2", "Test(a: 2)")
        };
        var content = GenerateReport(MakeFeature(scenarios), groupParameterizedTests: false);

        // Should NOT have param-test-table since these have no OutlineId and grouping is disabled
        Assert.DoesNotContain("<table class=\"param-test-table\"", content);
    }

    [Fact]
    public void Grouping_disabled_still_groups_OutlineId_scenarios()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Outline(a: 1)", outlineId: "Outline", exampleValues: new() { ["a"] = "1" }),
            MakeScenario("s2", "Outline(a: 2)", outlineId: "Outline", exampleValues: new() { ["a"] = "2" })
        };
        var content = GenerateReport(MakeFeature(scenarios), groupParameterizedTests: false);

        // OutlineId groups should still render as grouped
        Assert.Contains("param-test-table", content);
    }

    [Fact]
    public void Ungrouped_scenario_renders_individually()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Create order", result: ExecutionResult.Passed),
            MakeScenario("s2", "Delete order", result: ExecutionResult.Passed)
        };
        var content = GenerateReport(MakeFeature(scenarios));

        // These should be individual scenario sections, not grouped
        Assert.DoesNotContain("<table class=\"param-test-table\"", content);
        Assert.Contains("Create order", content);
        Assert.Contains("Delete order", content);
    }

    [Fact]
    public void Group_summary_shows_pass_fail_counts()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Test(a: 1)", outlineId: "Test",
                exampleValues: new() { ["a"] = "1" }, result: ExecutionResult.Passed),
            MakeScenario("s2", "Test(a: 2)", outlineId: "Test",
                exampleValues: new() { ["a"] = "2" }, result: ExecutionResult.Failed,
                errorMessage: "fail"),
            MakeScenario("s3", "Test(a: 3)", outlineId: "Test",
                exampleValues: new() { ["a"] = "3" }, result: ExecutionResult.Passed)
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains("1 failed", content);
        Assert.Contains("2/3 passed", content);
    }

    [Fact]
    public void Group_has_aggregate_duration()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Test(a: 1)", outlineId: "Test",
                exampleValues: new() { ["a"] = "1" }, duration: TimeSpan.FromMilliseconds(500)),
            MakeScenario("s2", "Test(a: 2)", outlineId: "Test",
                exampleValues: new() { ["a"] = "2" }, duration: TimeSpan.FromMilliseconds(300))
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains("duration-badge", content);
        Assert.Contains("data-duration-ms=", content);
    }

    [Fact]
    public void SelectRow_javascript_function_is_present()
    {
        var scenarios = new[] { MakeScenario("s1", "Test", result: ExecutionResult.Passed) };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains("function selectRow(", content);
    }

    [Fact]
    public void Display_name_based_grouping_produces_param_table()
    {
        // Two scenarios with same method prefix but no OutlineId - should be grouped via display name parsing
        var scenarios = new[]
        {
            MakeScenario("s1", "Calculate(x: 10, y: 20)"),
            MakeScenario("s2", "Calculate(x: 30, y: 40)")
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains("param-test-table", content);
        Assert.Contains("scenario-parameterized", content);
        // Titleized by default
        Assert.Contains(">X</th>", content);
        Assert.Contains(">Y</th>", content);
    }

    [Fact]
    public void Skipped_row_has_correct_status_badge()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Test(a: 1)", outlineId: "Test",
                exampleValues: new() { ["a"] = "1" }, result: ExecutionResult.Passed),
            MakeScenario("s2", "Test(a: 2)", outlineId: "Test",
                exampleValues: new() { ["a"] = "2" }, result: ExecutionResult.Skipped)
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains("badge-skip", content);
        Assert.Contains("row-skipped", content);
    }

    [Fact]
    public void MaxParameterColumns_exceeded_falls_back_to_R0()
    {
        var exampleValues = new Dictionary<string, string>();
        for (var i = 0; i < 5; i++)
            exampleValues[$"p{i}"] = $"v{i}";

        var scenarios = new[]
        {
            MakeScenario("s1", "Big(p0: v0, p1: v1, p2: v2, p3: v3, p4: v4)",
                outlineId: "Big", exampleValues: new(exampleValues)),
            MakeScenario("s2", "Big(p0: x0, p1: x1, p2: x2, p3: x3, p4: x4)",
                outlineId: "Big", exampleValues: new(exampleValues.ToDictionary(kv => kv.Key, kv => "x" + kv.Value[1..])))
        };

        // Set maxParameterColumns=2 to force fallback
        var content = GenerateReport(MakeFeature(scenarios), maxParameterColumns: 2);

        Assert.Contains("Test Case", content);
        Assert.DoesNotContain("Input Parameters", content);
    }

    [Fact]
    public void HTML_encodes_parameter_values()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Test(x: <script>)", outlineId: "Test",
                exampleValues: new() { ["x"] = "<script>alert(1)</script>" }),
            MakeScenario("s2", "Test(x: safe)", outlineId: "Test",
                exampleValues: new() { ["x"] = "safe" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.DoesNotContain("<script>alert(1)</script>", content);
        Assert.Contains("&lt;script&gt;", content);
    }

    // ── Copy scenario name button on groups ──

    [Fact]
    public void Group_summary_has_copy_scenario_name_button()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Proc(x: 1)", outlineId: "Proc", exampleValues: new() { ["x"] = "1" }),
            MakeScenario("s2", "Proc(x: 2)", outlineId: "Proc", exampleValues: new() { ["x"] = "2" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains("copy-scenario-name", content);
        Assert.Contains("data-scenario-name=\"Proc\"", content);
    }

    [Fact]
    public void Group_summary_has_scenario_link()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Proc(x: 1)", outlineId: "Proc", exampleValues: new() { ["x"] = "1" }),
            MakeScenario("s2", "Proc(x: 2)", outlineId: "Proc", exampleValues: new() { ["x"] = "2" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains("class=\"scenario-link\"", content);
        Assert.Contains("href=\"#scenario-proc\"", content);
    }

    // ── Deep link per row ──

    [Fact]
    public void Rows_have_data_scenario_id_for_deep_linking()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Test(a: 1)", outlineId: "Test", exampleValues: new() { ["a"] = "1" }),
            MakeScenario("s2", "Test(a: 2)", outlineId: "Test", exampleValues: new() { ["a"] = "2" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        // Each row should have data-scenario-id attribute in table HTML
        var dataScenarioIds = Regex.Matches(content, @"<tr[^>]+data-scenario-id=""([^""]+)""");
        Assert.Equal(2, dataScenarioIds.Count);
    }

    [Fact]
    public void Deep_link_JS_handles_row_inside_parameterized_group()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Test(a: 1)", outlineId: "Test", exampleValues: new() { ["a"] = "1" }),
            MakeScenario("s2", "Test(a: 2)", outlineId: "Test", exampleValues: new() { ["a"] = "2" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        // The JS should have code for finding rows by data-scenario-id
        Assert.Contains("data-scenario-id", content);
        Assert.Contains("scenario-parameterized", content);
    }

    // ── Search row highlighting ──

    [Fact]
    public void Rows_have_data_row_search_for_search_matching()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Test(a: foo)", outlineId: "Test", exampleValues: new() { ["a"] = "foo" }),
            MakeScenario("s2", "Test(a: bar)", outlineId: "Test", exampleValues: new() { ["a"] = "bar" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        var rowSearchAttrs = Regex.Matches(content, @"data-row-search=""[^""]+""");
        Assert.Equal(2, rowSearchAttrs.Count);
        // Each row's search text includes its display name
        Assert.Contains("test(a: foo)", content.ToLowerInvariant());
        Assert.Contains("test(a: bar)", content.ToLowerInvariant());
    }

    [Fact]
    public void Search_JS_includes_row_search_match_logic()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Test(a: 1)", outlineId: "Test", exampleValues: new() { ["a"] = "1" }),
            MakeScenario("s2", "Test(a: 2)", outlineId: "Test", exampleValues: new() { ["a"] = "2" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains("row-search-match", content);
        Assert.Contains("data-row-search", content);
    }

    // ── SelectRow JS switching for activity/flame ──

    [Fact]
    public void SelectRow_JS_switches_activity_and_flame_divs()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Test(a: 1)", outlineId: "Test", exampleValues: new() { ["a"] = "1" }),
            MakeScenario("s2", "Test(a: 2)", outlineId: "Test", exampleValues: new() { ["a"] = "2" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        // The selectRow JS should handle activity and flame divs
        Assert.Contains("-activity-", content);
        Assert.Contains("-flame-", content);
    }

    // ── Outline removal ──

    [Fact]
    public void Search_match_style_does_not_use_outline()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Test(a: 1)", outlineId: "Test", exampleValues: new() { ["a"] = "1" }),
            MakeScenario("s2", "Test(a: 2)", outlineId: "Test", exampleValues: new() { ["a"] = "2" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        // The row-search-match style should use box-shadow, not outline
        Assert.DoesNotContain("row-search-match { outline:", content);
        Assert.Contains("row-search-match { box-shadow:", content);
    }

    // ── Parameter name titleization ──

    [Fact]
    public void Parameter_names_are_titleized_by_default()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Calc(region: UK, amount: 100)", outlineId: "Calc",
                exampleValues: new() { ["region"] = "UK", ["amount"] = "100" }),
            MakeScenario("s2", "Calc(region: US, amount: 200)", outlineId: "Calc",
                exampleValues: new() { ["region"] = "US", ["amount"] = "200" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains(">Region</th>", content);
        Assert.Contains(">Amount</th>", content);
        Assert.DoesNotContain(">region</th>", content);
        Assert.DoesNotContain(">amount</th>", content);
    }

    [Fact]
    public void Parameter_names_titleization_handles_camelCase()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Test(firstName: Jo)", outlineId: "Test",
                exampleValues: new() { ["firstName"] = "Jo" }),
            MakeScenario("s2", "Test(firstName: Al)", outlineId: "Test",
                exampleValues: new() { ["firstName"] = "Al" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains(">First Name</th>", content);
    }

    [Fact]
    public void Parameter_names_titleization_can_be_disabled()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Calc(region: UK)", outlineId: "Calc",
                exampleValues: new() { ["region"] = "UK" }),
            MakeScenario("s2", "Calc(region: US)", outlineId: "Calc",
                exampleValues: new() { ["region"] = "US" })
        };
        var content = GenerateReport(MakeFeature(scenarios), titleizeParameterNames: false);

        Assert.Contains(">region</th>", content);
        Assert.DoesNotContain(">Region</th>", content);
    }

    // ── Single-instance parameterized scenarios use table format ──

    [Fact]
    public void Single_scenario_with_parameters_renders_as_parameterized_group()
    {
        var scenario = MakeScenario("s1", "Process(region: UK, amount: 100)",
            outlineId: "Process",
            exampleValues: new() { ["region"] = "UK", ["amount"] = "100" });
        var content = GenerateReport(MakeFeature(scenario));

        Assert.Contains("scenario-parameterized", content);
        Assert.Contains("param-test-table", content);
    }

    [Fact]
    public void Single_scenario_with_parsed_parameters_renders_as_parameterized_group()
    {
        var scenario = MakeScenario("s1", "Calculate(x: 10, y: 20)");
        var content = GenerateReport(MakeFeature(scenario));

        Assert.Contains("scenario-parameterized", content);
        Assert.Contains("param-test-table", content);
    }

    [Fact]
    public void Single_scenario_with_parameters_shows_titleized_column_headers()
    {
        var scenario = MakeScenario("s1", "Process(region: UK, amount: 100)",
            outlineId: "Process",
            exampleValues: new() { ["region"] = "UK", ["amount"] = "100" });
        var content = GenerateReport(MakeFeature(scenario));

        Assert.Contains(">Region</th>", content);
        Assert.Contains(">Amount</th>", content);
    }

    [Fact]
    public void Single_scenario_without_parameters_renders_normally()
    {
        var scenario = MakeScenario("s1", "Create order");
        var content = GenerateReport(MakeFeature(scenario));

        // Should be a regular scenario, not a parameterized group
        Assert.DoesNotContain("<details class=\"scenario scenario-parameterized\"", content);
        Assert.DoesNotContain("<table class=\"param-test-table\"", content);
    }

    [Fact]
    public void Single_scenario_with_bracket_parameters_renders_as_parameterized_group()
    {
        var scenario = MakeScenario("s1", "Scenario name [count: 5]");
        var content = GenerateReport(MakeFeature(scenario));

        Assert.Contains("scenario-parameterized", content);
        Assert.Contains("param-test-table", content);
    }

    [Fact]
    public void Parameterized_group_with_happy_path_scenarios_gets_happy_path_class()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Reset(version: V1)", outlineId: "Reset",
                exampleValues: new() { ["version"] = "V1" }, isHappyPath: true),
            MakeScenario("s2", "Reset(version: V2)", outlineId: "Reset",
                exampleValues: new() { ["version"] = "V2" }, isHappyPath: true)
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains("scenario-parameterized happy-path", content);
        Assert.Contains(">Happy Path</span>", content);
    }

    [Fact]
    public void Parameterized_group_without_happy_path_scenarios_does_not_get_happy_path_class()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Reset(version: V1)", outlineId: "Reset",
                exampleValues: new() { ["version"] = "V1" }),
            MakeScenario("s2", "Reset(version: V2)", outlineId: "Reset",
                exampleValues: new() { ["version"] = "V2" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.DoesNotContain("scenario-parameterized happy-path", content);
        Assert.DoesNotContain(">Happy Path</span>", content);
    }

    [Fact]
    public void Parameterized_group_with_mixed_happy_path_gets_happy_path_class()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Reset(version: V1)", outlineId: "Reset",
                exampleValues: new() { ["version"] = "V1" }, isHappyPath: true),
            MakeScenario("s2", "Reset(version: V2)", outlineId: "Reset",
                exampleValues: new() { ["version"] = "V2" }, isHappyPath: false)
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains("scenario-parameterized happy-path", content);
    }

    // ── R2: FlattenedObject rendering ──

    private record MerchantRequest(string Region, int Amount, string Currency);

    [Fact]
    public void R2_flattened_object_renders_property_columns()
    {
        var obj1 = new MerchantRequest("UK", 100, "GBP");
        var obj2 = new MerchantRequest("US", 200, "USD");
        var scenarios = new[]
        {
            MakeScenario("s1", "Process(request)", outlineId: "Process",
                exampleValues: new() { ["request"] = obj1.ToString()! },
                exampleRawValues: new() { ["request"] = obj1 }),
            MakeScenario("s2", "Process(request)", outlineId: "Process",
                exampleValues: new() { ["request"] = obj2.ToString()! },
                exampleRawValues: new() { ["request"] = obj2 })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        // Should show flattened property columns
        Assert.Contains("Input Parameters", content);
        Assert.Contains(">Region</th>", content);
        Assert.Contains(">Amount</th>", content);
        Assert.Contains(">Currency</th>", content);
        // Should show flattened values
        Assert.Contains(">UK</td>", content);
        Assert.Contains(">100</td>", content);
        Assert.Contains(">GBP</td>", content);
    }

    // ── R3: SubTable cell rendering ──

    private record SmallAddress(string Street, string City, string PostCode);

    [Fact]
    public void R3_small_complex_object_renders_subtable_in_cell()
    {
        var addr = new SmallAddress("1 Main St", "London", "SW1A 1AA");
        var scenarios = new[]
        {
            MakeScenario("s1", "Send(amount: 100, address: ...)", outlineId: "Send",
                exampleValues: new() { ["amount"] = "100", ["address"] = addr.ToString()! },
                exampleRawValues: new() { ["amount"] = (object)100, ["address"] = addr }),
            MakeScenario("s2", "Send(amount: 200, address: ...)", outlineId: "Send",
                exampleValues: new() { ["amount"] = "200", ["address"] = new SmallAddress("2 Oak Ave", "Paris", "75001").ToString()! },
                exampleRawValues: new() { ["amount"] = (object)200, ["address"] = new SmallAddress("2 Oak Ave", "Paris", "75001") })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains("cell-subtable", content);
        Assert.Contains(">Street</th>", content);
        Assert.Contains(">City</th>", content);
        Assert.Contains(">PostCode</th>", content);
        Assert.Contains("1 Main St", content);
    }

    // ── R4: ExpandableComplex cell rendering ──

    private record ComplexBatch(string Currency, string Priority, List<string> Payments);

    [Fact]
    public void R4_deeply_complex_object_renders_expandable_details()
    {
        var batch = new ComplexBatch("GBP", "Normal", ["P001", "P002"]);
        var scenarios = new[]
        {
            MakeScenario("s1", "Process(id: batch-001, batch: ...)", outlineId: "Process",
                exampleValues: new() { ["id"] = "batch-001", ["batch"] = batch.ToString()! },
                exampleRawValues: new() { ["id"] = (object)"batch-001", ["batch"] = batch }),
            MakeScenario("s2", "Process(id: batch-002, batch: ...)", outlineId: "Process",
                exampleValues: new() { ["id"] = "batch-002", ["batch"] = batch.ToString()! },
                exampleRawValues: new() { ["id"] = (object)"batch-002", ["batch"] = batch })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains("param-expand", content);
        Assert.Contains("expand-body", content);
        Assert.Contains("prop-key", content);
        Assert.Contains("prop-val", content);
        // The scalar column "id" should still be plain text
        Assert.Contains(">batch-001</td>", content);
    }

    // ── R4: CSS and JS present ──

    [Fact]
    public void Report_contains_param_expand_css()
    {
        var scenarios = new[] { MakeScenario("s1", "Test") };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains("details.param-expand", content);
        Assert.Contains(".expand-body", content);
        Assert.Contains(".prop-key", content);
        Assert.Contains(".prop-val", content);
    }

    [Fact]
    public void Report_contains_cell_subtable_css()
    {
        var scenarios = new[] { MakeScenario("s1", "Test") };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains(".cell-subtable", content);
    }

    [Fact]
    public void Report_contains_param_expand_js()
    {
        var scenarios = new[] { MakeScenario("s1", "Test") };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains("param-expand", content);
        Assert.Contains("cell-subtable", content);
        Assert.Contains("stopPropagation", content);
    }

    // ── Nested object renders expandable ──

    private record NestedPayment(string Name, SmallAddress Address);

    [Fact]
    public void Nested_complex_object_flattens_via_string_parsing_with_nested_subtable()
    {
        var nested = new NestedPayment("Acme", new SmallAddress("1 Main St", "London", "SW1A 1AA"));
        var scenarios = new[]
        {
            MakeScenario("s1", "Test(target: ...)", outlineId: "Test",
                exampleValues: new() { ["target"] = nested.ToString()! },
                exampleRawValues: new() { ["target"] = nested }),
            MakeScenario("s2", "Test(target: ...)", outlineId: "Test",
                exampleValues: new() { ["target"] = nested.ToString()! },
                exampleRawValues: new() { ["target"] = nested })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        // Reflection-based R2 rejects nested complex, string-based R2 flattens to Name + Address
        Assert.Contains(">Name</th>", content);
        Assert.Contains(">Address</th>", content);
        Assert.Contains("Acme", content);
        // The Address column value is itself a record string → rendered as R3 sub-table
        Assert.Contains("cell-subtable", content);
    }

    // ── R2 not applied when single param is already scalar ──

    [Fact]
    public void Single_scalar_param_uses_ScalarColumns_not_FlattenedObject()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Test(value: 42)", outlineId: "Test",
                exampleValues: new() { ["value"] = "42" },
                exampleRawValues: new() { ["value"] = (object)42 }),
            MakeScenario("s2", "Test(value: 99)", outlineId: "Test",
                exampleValues: new() { ["value"] = "99" },
                exampleRawValues: new() { ["value"] = (object)99 })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        // Should render as normal scalar column, not flattened
        Assert.Contains("Input Parameters", content);
        Assert.Contains(">Value</th>", content);
        Assert.Contains(">42</td>", content);
    }

    [Fact]
    public void Steps_in_detail_panel_use_details_summary_pattern()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Test(a: 1)", outlineId: "Test",
                exampleValues: new() { ["a"] = "1" },
                steps: [new ScenarioStep { Text = "Given a setup", Keyword = "Given" }]),
            MakeScenario("s2", "Test(a: 2)", outlineId: "Test",
                exampleValues: new() { ["a"] = "2" },
                steps: [new ScenarioStep { Text = "Given a setup", Keyword = "Given" }])
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains("param-detail-panel", content);
        // Should use <details> with <summary>, not a plain <div>
        Assert.Contains("<details class=\"scenario-steps\" open>", content);
        Assert.Contains("<summary class=\"h4\">Steps</summary>", content);
        Assert.DoesNotContain("<div class=\"scenario-steps\">", content);
    }

    // ── String-based R2: record ToString() flattening without raw values ──

    [Fact]
    public void R2_string_based_record_ToString_flattens_into_columns()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Test(scenario)", outlineId: "Test",
                exampleValues: new() { ["scenario"] = "Score { Age = 89, Score = 320, Band = E }" }),
            MakeScenario("s2", "Test(scenario)", outlineId: "Test",
                exampleValues: new() { ["scenario"] = "Score { Age = 25, Score = 500, Band = A }" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        // Should flatten into individual columns, not show the raw string
        Assert.Contains(">Age</th>", content);
        Assert.Contains(">Score</th>", content);
        Assert.Contains(">Band</th>", content);
        Assert.Contains(">89</td>", content);
        Assert.Contains(">500</td>", content);
        Assert.Contains(">E</td>", content);
        // Should NOT show the full record string as a single cell
        Assert.DoesNotContain("Score { Age = 89", content);
    }

    [Fact]
    public void R2_string_based_with_null_values_renders_correctly()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Test(scenario)", outlineId: "Test",
                exampleValues: new() { ["scenario"] = "Risk { Score = 320, Band = null }" }),
            MakeScenario("s2", "Test(scenario)", outlineId: "Test",
                exampleValues: new() { ["scenario"] = "Risk { Score = 500, Band = A }" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains(">Score</th>", content);
        Assert.Contains(">Band</th>", content);
        Assert.Contains("><pre>null</pre></td>", content);
    }

    // ── String-based R3: sub-table from record ToString() ──

    [Fact]
    public void R3_string_based_record_renders_subtable_in_cell()
    {
        // When a cell value (not single-key R2) is a record string with ≤5 props,
        // it should render as a sub-table
        var scenarios = new[]
        {
            MakeScenario("s1", "Test(id: 1, addr: ...)", outlineId: "Test",
                exampleValues: new() { ["id"] = "1", ["addr"] = "Place { Street = 1 Main St, City = London }" }),
            MakeScenario("s2", "Test(id: 2, addr: ...)", outlineId: "Test",
                exampleValues: new() { ["id"] = "2", ["addr"] = "Place { Street = 2 Oak Ave, City = Paris }" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains("cell-subtable", content);
        Assert.Contains("Street", content);
        Assert.Contains("1 Main St", content);
        Assert.Contains("City", content);
        Assert.Contains("London", content);
    }

    // ── String-based R4: expandable from record ToString() ──

    [Fact]
    public void R4_string_based_record_renders_expandable_in_cell()
    {
        // A record string value with >5 properties should render as expandable details
        var bigRecord = "Scenario { A = 1, B = 2, C = 3, D = 4, E = 5, F = 6 }";
        var scenarios = new[]
        {
            MakeScenario("s1", "Test(id: x, data: ...)", outlineId: "Test",
                exampleValues: new() { ["id"] = "x", ["data"] = bigRecord }),
            MakeScenario("s2", "Test(id: y, data: ...)", outlineId: "Test",
                exampleValues: new() { ["id"] = "y", ["data"] = bigRecord })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        Assert.Contains("param-expand", content);
        Assert.Contains("expand-body", content);
        Assert.Contains("prop-key", content);
        Assert.Contains("prop-val", content);
    }

    // ── String-based fallback: plain string stays plain text ──

    [Fact]
    public void Non_record_string_value_renders_as_plain_text()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Test(name: hello)", outlineId: "Test",
                exampleValues: new() { ["name"] = "hello" }),
            MakeScenario("s2", "Test(name: world)", outlineId: "Test",
                exampleValues: new() { ["name"] = "world" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        // No sub-table or expandable elements inside the table body
        Assert.DoesNotContain("<table class=\"cell-subtable\">", content);
        Assert.DoesNotContain("<details class=\"param-expand\">", content);
        Assert.Contains(">hello</td>", content);
        Assert.Contains(">world</td>", content);
    }

    [Fact]
    public void Parameterized_group_renders_assertion_toggles_when_diagrams_have_assertion_notes()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Verify(input: A)", outlineId: "Verify", exampleValues: new() { ["input"] = "A" }),
            MakeScenario("s2", "Verify(input: B)", outlineId: "Verify", exampleValues: new() { ["input"] = "B" })
        };
        var diagrams = MakeDiagrams(
            ("s1", "@startuml\nhnote over Caller <<assertionNote>> #d4edda\nAssert OK\nend note\n@enduml"),
            ("s2", "@startuml\nhnote over Caller <<assertionNote>> #d4edda\nAssert OK\nend note\n@enduml"));
        var content = GenerateReport(MakeFeature(scenarios), diagrams);

        // Verify the parameterized group has scenario-level assertion toggle button in the diagram section
        Assert.Contains("scenario-parameterized", content);
        // The actual button element (not just the JS function definition) must be inside the parameterized group
        var paramGroupStart = content.IndexOf("<details class=\"scenario scenario-parameterized");
        Assert.True(paramGroupStart >= 0, "parameterized group not found");
        var paramGroupHtml = content.Substring(paramGroupStart);
        Assert.Contains("onclick=\"window._toggleScenarioAssertions(this)\">Assertions Hidden</button>", paramGroupHtml);
    }

    [Fact]
    public void Parameterized_group_renders_step_toggles_when_diagrams_have_step_delimiters()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Steps(input: A)", outlineId: "Steps", exampleValues: new() { ["input"] = "A" }),
            MakeScenario("s2", "Steps(input: B)", outlineId: "Steps", exampleValues: new() { ["input"] = "B" })
        };
        var diagrams = MakeDiagrams(
            ("s1", "@startuml\nhnote across <<stepDelimiter>> #black\nGiven something\nend note\n@enduml"),
            ("s2", "@startuml\nhnote across <<stepDelimiter>> #black\nGiven something\nend note\n@enduml"));
        var content = GenerateReport(MakeFeature(scenarios), diagrams);

        Assert.Contains("scenario-parameterized", content);
        var paramGroupStart = content.IndexOf("<details class=\"scenario scenario-parameterized");
        Assert.True(paramGroupStart >= 0, "parameterized group not found");
        var paramGroupHtml = content.Substring(paramGroupStart);
        Assert.Contains("onclick=\"window._toggleScenarioSteps(this)\">Steps Shown</button>", paramGroupHtml);
    }

    [Fact]
    public void Flat_values_renders_two_tables_with_toggle_buttons()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Recipe(recipe)", outlineId: "Recipe",
                exampleValues: new() { ["Recipe"] = "{ Flour = Plain }" },
                exampleFlatValues: new() { ["RecipeName"] = "Classic", ["Flour"] = "Plain" }),
            MakeScenario("s2", "Recipe(recipe)", outlineId: "Recipe",
                exampleValues: new() { ["Recipe"] = "{ Flour = Whole }" },
                exampleFlatValues: new() { ["RecipeName"] = "Rustic", ["Flour"] = "Whole" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        // Should have a wrapper div
        Assert.Contains("param-table-wrapper", content);
        // Should have grouped table (visible) with + button
        Assert.Contains("param-table-grouped", content);
        Assert.Contains("flatten-toggle", content);
        // Should have flat table (hidden) with - button
        Assert.Contains("param-table-flat", content);
    }

    [Fact]
    public void Flat_table_renders_original_column_headers()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Recipe(recipe)", outlineId: "Recipe",
                exampleValues: new() { ["Recipe"] = "{ Flour = Plain }" },
                exampleFlatValues: new() { ["RecipeName"] = "Classic", ["Flour"] = "Plain" }),
            MakeScenario("s2", "Recipe(recipe)", outlineId: "Recipe",
                exampleValues: new() { ["Recipe"] = "{ Flour = Whole }" },
                exampleFlatValues: new() { ["RecipeName"] = "Rustic", ["Flour"] = "Whole" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        // Extract the flat table section
        var flatTableStart = content.IndexOf("param-table-flat");
        Assert.True(flatTableStart >= 0, "flat table not found");
        var flatTableHtml = content.Substring(flatTableStart);

        // Should have the original flat column headers
        Assert.Contains("Recipe Name", flatTableHtml); // "RecipeName" titleized
        Assert.Contains("Flour", flatTableHtml);
    }

    [Fact]
    public void Flat_table_renders_scalar_values_only()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Recipe(recipe)", outlineId: "Recipe",
                exampleValues: new() { ["Recipe"] = "{ Flour = Plain }" },
                exampleRawValues: new() { ["Recipe"] = new Dictionary<string, object?> { ["Flour"] = "Plain" } },
                exampleFlatValues: new() { ["RecipeName"] = "Classic", ["Flour"] = "Plain" }),
            MakeScenario("s2", "Recipe(recipe)", outlineId: "Recipe",
                exampleValues: new() { ["Recipe"] = "{ Flour = Whole }" },
                exampleRawValues: new() { ["Recipe"] = new Dictionary<string, object?> { ["Flour"] = "Whole" } },
                exampleFlatValues: new() { ["RecipeName"] = "Rustic", ["Flour"] = "Whole" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        // Find the flat table element in HTML (not CSS)
        var marker = "<table class=\"param-test-table param-table-flat\"";
        var flatStart = content.IndexOf(marker);
        Assert.True(flatStart >= 0, "flat table element not found");
        var flatEnd = content.IndexOf("</table>", flatStart);
        var flatTableHtml = content.Substring(flatStart, flatEnd - flatStart + "</table>".Length);
        Assert.DoesNotContain("cell-subtable", flatTableHtml);
        Assert.DoesNotContain("param-expand", flatTableHtml);
        // But should have mono scalar values
        Assert.Contains("Classic", flatTableHtml);
        Assert.Contains("Rustic", flatTableHtml);
    }

    [Fact]
    public void Flat_table_rows_have_no_id_attribute()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Recipe(recipe)", outlineId: "Recipe",
                exampleValues: new() { ["Recipe"] = "{ Flour = Plain }" },
                exampleFlatValues: new() { ["RecipeName"] = "Classic", ["Flour"] = "Plain" }),
            MakeScenario("s2", "Recipe(recipe)", outlineId: "Recipe",
                exampleValues: new() { ["Recipe"] = "{ Flour = Whole }" },
                exampleFlatValues: new() { ["RecipeName"] = "Rustic", ["Flour"] = "Whole" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        // Extract the flat table
        var flatStart = content.IndexOf("param-table-flat");
        var flatEnd = content.IndexOf("</table>", flatStart);
        var flatTableHtml = content.Substring(flatStart, flatEnd - flatStart + "</table>".Length);

        // Rows should have data-row-idx but not id
        Assert.Contains("data-row-idx", flatTableHtml);
        Assert.DoesNotContain("id=\"scenario-", flatTableHtml);
    }

    [Fact]
    public void Grouped_table_hidden_by_default_flat_table_visible_when_no_flat_values()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Process(region: UK)", outlineId: "Process",
                exampleValues: new() { ["region"] = "UK" }),
            MakeScenario("s2", "Process(region: US)", outlineId: "Process",
                exampleValues: new() { ["region"] = "US" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        // Extract just the scenario HTML (after the <style> block)
        var scenarioStart = content.IndexOf("<details class=\"scenario scenario-parameterized");
        Assert.True(scenarioStart >= 0, "parameterized group not found");
        var scenarioHtml = content.Substring(scenarioStart);

        // Should not have a wrapper, toggle, or grouped class in the actual HTML
        Assert.DoesNotContain("param-table-wrapper", scenarioHtml);
        Assert.DoesNotContain("flatten-toggle", scenarioHtml);
        Assert.DoesNotContain("param-table-grouped", scenarioHtml);
    }

    [Fact]
    public void Grouped_table_is_hidden_by_default_when_flat_view_available()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Recipe(recipe)", outlineId: "Recipe",
                exampleValues: new() { ["Recipe"] = "{ Flour = Plain }" },
                exampleFlatValues: new() { ["RecipeName"] = "Classic", ["Flour"] = "Plain" }),
            MakeScenario("s2", "Recipe(recipe)", outlineId: "Recipe",
                exampleValues: new() { ["Recipe"] = "{ Flour = Whole }" },
                exampleFlatValues: new() { ["RecipeName"] = "Rustic", ["Flour"] = "Whole" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        // Grouped table should be hidden by default
        var groupedMarker = "<table class=\"param-test-table param-table-grouped\"";
        var groupedIdx = content.IndexOf(groupedMarker);
        Assert.True(groupedIdx >= 0, "grouped table element not found");
        var groupedTagEnd = content.IndexOf(">", groupedIdx);
        var groupedTag = content.Substring(groupedIdx, groupedTagEnd - groupedIdx + 1);
        Assert.Contains("display:none", groupedTag);

        // Flat table should be visible by default (no display:none)
        var flatMarker = "<table class=\"param-test-table param-table-flat\"";
        var flatIdx = content.IndexOf(flatMarker);
        Assert.True(flatIdx >= 0, "flat table element not found");
        var flatTagEnd = content.IndexOf(">", flatIdx);
        var flatTag = content.Substring(flatIdx, flatTagEnd - flatIdx + 1);
        Assert.DoesNotContain("display:none", flatTag);
    }

    [Fact]
    public void SelectRow_syncs_details_open_state_from_outgoing_to_incoming_panel()
    {
        var scenarios = new[]
        {
            MakeScenario("s1", "Test(x: 1)", outlineId: "Test", exampleValues: new() { ["x"] = "1" }),
            MakeScenario("s2", "Test(x: 2)", outlineId: "Test", exampleValues: new() { ["x"] = "2" })
        };
        var content = GenerateReport(MakeFeature(scenarios));

        // selectRow JS should capture outgoing panel's <details> open states and apply them to incoming panel
        Assert.Contains("querySelectorAll('details')", content);
        Assert.Contains(".open", content); // reads open property
    }
    // ── Background steps inside a parameterized group's detail panels ────────────────────────────

    private static Scenario WithBackground(string id, string x, ScenarioStep[] background, ScenarioStep[]? steps = null)
    {
        var scenario = MakeScenario(id, $"Test(x: {x})", outlineId: "Test",
            exampleValues: new() { ["x"] = x }, steps: steps);
        scenario.BackgroundSteps = background;
        return scenario;
    }

    private string GenerateGroupReport(Feature[] features, bool separateBackgroundSteps = false,
        bool collapseRepeatedStepKeywords = true, bool showStepNumbers = false)
    {
        var diagrams = features.SelectMany(f => f.Scenarios).Select(s => new DefaultDiagramsFetcher.DiagramAsCode(s.Id, "", "")).ToArray();
        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, $"ParamGroupBg_{Guid.NewGuid():N}.html", "Test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            showStepNumbers: showStepNumbers,
            separateBackgroundSteps: separateBackgroundSteps,
            collapseRepeatedStepKeywords: collapseRepeatedStepKeywords);
        return File.ReadAllText(path);
    }

    [Fact]
    public void Group_detail_panels_combine_background_steps_with_regular_steps_by_default()
    {
        var background = new ScenarioStep[] { new() { Keyword = "Given", Text = "the system is running" } };
        var scenarios = new[]
        {
            WithBackground("s1", "1", background, [new ScenarioStep { Keyword = "When", Text = "one happens" }]),
            WithBackground("s2", "2", background, [new ScenarioStep { Keyword = "When", Text = "two happens" }])
        };
        var content = GenerateGroupReport(MakeFeature(scenarios));

        Assert.DoesNotContain("<details class=\"scenario-background\">", content);
        Assert.Equal(2, ReportMarkup.ElementsWithClass(content, "step-background").Count);
        Assert.Contains("the system is running", content);
    }

    [Fact]
    public void Group_detail_panels_separate_background_steps_when_option_enabled()
    {
        var background = new ScenarioStep[] { new() { Keyword = "Given", Text = "the system is running" } };
        var scenarios = new[]
        {
            WithBackground("s1", "1", background, [new ScenarioStep { Keyword = "When", Text = "one happens" }]),
            WithBackground("s2", "2", background, [new ScenarioStep { Keyword = "When", Text = "two happens" }])
        };
        var content = GenerateGroupReport(MakeFeature(scenarios), separateBackgroundSteps: true);

        Assert.Equal(2, Regex.Matches(content, "<details class=\"scenario-background\">").Count);
        Assert.Contains("<summary class=\"h4\">Background Steps</summary>", content);
    }

    [Fact]
    public void Group_detail_panels_collapse_a_repeated_given_across_the_seam()
    {
        var background = new ScenarioStep[] { new() { Keyword = "Given", Text = "the system is running" } };
        var scenarios = new[]
        {
            WithBackground("s1", "1", background, [new ScenarioStep { Keyword = "Given", Text = "a request for 1" }]),
            WithBackground("s2", "2", background, [new ScenarioStep { Keyword = "Given", Text = "a request for 2" }])
        };
        var content = GenerateGroupReport(MakeFeature(scenarios));

        var keywords = ReportMarkup.InnerTextsOfClass(content, "step-keyword");
        Assert.Equal(["Given", "And", "Given", "And"], keywords);
    }

    [Fact]
    public void Group_detail_panels_number_combined_steps_continuously()
    {
        var background = new ScenarioStep[]
        {
            new() { Keyword = "Given", Text = "bg one" },
            new() { Keyword = "And", Text = "bg two" }
        };
        var scenarios = new[]
        {
            WithBackground("s1", "1", background, [new ScenarioStep { Keyword = "When", Text = "one happens" }]),
            WithBackground("s2", "2", background, [new ScenarioStep { Keyword = "When", Text = "two happens" }])
        };
        var content = GenerateGroupReport(MakeFeature(scenarios), showStepNumbers: true);

        var numbers = ReportMarkup.InnerTextsOfClass(content, "step-number");
        Assert.Equal(["1.", "2.", "3.", "1.", "2.", "3."], numbers);
    }

    [Fact]
    public void Group_renders_detail_panels_for_scenarios_whose_steps_are_all_background()
    {
        // BackgroundStepsDetector deliberately allows the whole step list to become background — a
        // scenario outline whose rows share identical step prose hits exactly this. The detail panel
        // used to be skipped entirely, so the steps vanished from the report.
        var background = new ScenarioStep[]
        {
            new() { Keyword = "Given", Text = "the system is running", Status = ExecutionResult.Passed },
            new() { Keyword = "And", Text = "the database is available", Status = ExecutionResult.Passed }
        };
        var scenarios = new[]
        {
            WithBackground("s1", "1", background, []),
            WithBackground("s2", "2", background, [])
        };
        var content = GenerateGroupReport(MakeFeature(scenarios));

        Assert.Contains("param-detail-panel", content);
        Assert.Contains("the database is available", content);
    }

    [Fact]
    public void Group_row_search_index_covers_text_that_only_appears_in_a_background_step()
    {
        var background = new ScenarioStep[] { new() { Keyword = "Given", Text = "the zephyr service is armed" } };
        var scenarios = new[]
        {
            WithBackground("s1", "1", background, [new ScenarioStep { Keyword = "When", Text = "one happens" }]),
            WithBackground("s2", "2", background, [new ScenarioStep { Keyword = "When", Text = "two happens" }])
        };
        var content = GenerateGroupReport(MakeFeature(scenarios));

        var rowSearch = ReportMarkup.AttributeValue(content, "tr", "data-row-search");
        Assert.Contains("zephyr", rowSearch);
    }
}
