using Kronikol.Reports;
using Kronikol.Reports.Merge;

namespace Kronikol.Tests.Reports;

/// <summary>
/// Markup and script-token coverage for the configurable toggle defaults
/// (TOGGLE_DEFAULTS_PLAN M3–M6): every seeded control's markup is computed from the resolved
/// record, the collapsible-notes globals substitute their tokens (no <c>__…__</c> leak), gating
/// flags still win over configuration, and the merge renderer forwards the defaults.
/// </summary>
public class ToggleDefaultsMarkupTests
{
    private const string AllGatesDiagramSource = """
        @startuml
        actor "Caller" as caller
        participant "OrderService" as svc
        database "OrderDb" as db

        hnote across #black <<stepDelimiter>>: <color:white>Given an order request</color>
        caller -> svc : POST /api/orders
        note left
        <color:gray>[content-type=application/json]</color>

        {
          "query": "SELECT o.id,\nFROM orders o"
        }
        end note
        svc -> db : INSERT INTO orders
        db --> svc : 1 row
        svc --> caller : 200 OK
        note right <<assertionNote>>: ✓ status code should be OK
        @enduml
        """;

    private const string NoGatesDiagramSource = """
        @startuml
        actor "Caller" as caller
        participant "Svc" as svc
        caller -> svc : GET /health
        svc --> caller : 200 OK
        @enduml
        """;

    private static Feature[] SimpleFeatures =>
    [
        new Feature
        {
            DisplayName = "F1",
            Scenarios =
            [
                new Scenario
                {
                    Id = "s1", DisplayName = "S1", Result = ExecutionResult.Passed,
                    Duration = TimeSpan.FromMilliseconds(500),
                    Steps = [new ScenarioStep { Keyword = "Given", Text = "a step", Status = ExecutionResult.Passed }]
                }
            ]
        }
    ];

    private static string Generate(Action<ReportToggleDefaults>? configure = null, string? diagramSource = null,
        Feature[]? features = null)
    {
        var options = new ReportConfigurationOptions();
        configure?.Invoke(options.TestRunReportToggleDefaults);
        var diagrams = new[] { new DefaultDiagramsFetcher.DiagramAsCode("s1", "", diagramSource ?? AllGatesDiagramSource) };
        var path = ReportGenerator.GenerateHtmlReport(
            diagrams, features ?? SimpleFeatures,
            DateTime.UtcNow, DateTime.UtcNow,
            null, $"ToggleMarkup_{Guid.NewGuid():N}.html", "Test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            toggleDefaults: ReportToggleDefaultsResolver.Resolve(options, specifications: false));
        return File.ReadAllText(path);
    }

    // ═══════════════════════════════════════════════════════════
    // Collapsible-notes script tokens
    // ═══════════════════════════════════════════════════════════

    private static readonly string[] ScriptTokens =
    [
        "__NOTE_FORMAT_DEFAULT__", "__HEADERS_HIDDEN_DEFAULT__", "__TRUNCATE_LINES_DEFAULT__",
        "__DETAILS_DEFAULT__", "__ASSERTIONS_VISIBLE_DEFAULT__", "__STEPS_VISIBLE_DEFAULT__",
        "__DATABASES_VISIBLE_DEFAULT__"
    ];

    [Fact]
    public void Script_tokens_substitute_to_the_builtin_globals()
    {
        var script = DiagramContextMenu.GetCollapsibleNotesScript(ResolvedToggleDefaults.BuiltIn);
        Assert.Contains("window._headersHidden = false", script);
        Assert.Contains("window._truncateLinesDefault = 40", script);
        Assert.Contains("window._truncateLines = window._truncateLinesDefault", script);
        Assert.Contains("window._detailsDefault = 'truncated'", script);
        Assert.Contains("window._assertionsVisible = false", script);
        Assert.Contains("window._stepsVisible = true", script);
        Assert.Contains("window._databasesVisible = true", script);
        Assert.Contains("window._noteFormatDefault = 'json'", script);
        foreach (var token in ScriptTokens)
            Assert.DoesNotContain(token, script);
    }

    [Fact]
    public void Script_tokens_substitute_to_the_configured_globals()
    {
        var script = DiagramContextMenu.GetCollapsibleNotesScript(new ResolvedToggleDefaults
        {
            Details = ReportDetailsState.Expanded,
            TruncateLines = TruncateLineCount.Lines10,
            HeadersShown = false,
            AssertionsShown = true,
            StepsShown = false,
            DatabasesShown = false,
            NotePayloadFormat = NotePayloadFormat.Yaml
        });
        Assert.Contains("window._headersHidden = true", script);
        Assert.Contains("window._truncateLinesDefault = 10", script);
        Assert.Contains("window._detailsDefault = 'expanded'", script);
        Assert.Contains("window._assertionsVisible = true", script);
        Assert.Contains("window._stepsVisible = false", script);
        Assert.Contains("window._databasesVisible = false", script);
        Assert.Contains("window._noteFormatDefault = 'yaml'", script);
        foreach (var token in ScriptTokens)
            Assert.DoesNotContain(token, script);
    }

    [Fact]
    public void Legacy_note_format_overloads_seed_builtin_globals()
    {
        foreach (var script in new[]
        {
            DiagramContextMenu.GetCollapsibleNotesScript(),
            DiagramContextMenu.GetCollapsibleNotesScript(NotePayloadFormat.Yaml)
        })
        {
            Assert.Contains("window._detailsDefault = 'truncated'", script);
            Assert.Contains("window._truncateLinesDefault = 40", script);
            foreach (var token in ScriptTokens)
                Assert.DoesNotContain(token, script);
        }
    }

    [Fact]
    public void Truncate_lines_fallbacks_use_the_seeded_default_not_a_literal()
    {
        var script = DiagramContextMenu.GetCollapsibleNotesScript(ResolvedToggleDefaults.BuiltIn);
        // The old hard-coded `|| 20` fallbacks in _setTruncateLines/_setScenarioTruncateLines
        // must route through the seeded default instead.
        Assert.DoesNotContain("|| 20", script);
        Assert.Contains("|| window._truncateLinesDefault", script);
    }

    // ═══════════════════════════════════════════════════════════
    // Report toolbar markup
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void No_token_leaks_into_a_generated_report()
    {
        var content = Generate(t =>
        {
            t.Details = ReportDetailsState.Collapsed;
            t.HeadersShown = false;
        });
        foreach (var token in ScriptTokens)
            Assert.DoesNotContain(token, content);
        Assert.DoesNotContain("__DEP_MODE_DEFAULT__", content);
        Assert.DoesNotContain("__CAT_MODE_DEFAULT__", content);
    }

    [Fact]
    public void Details_default_seeds_active_button_and_disables_the_truncate_select()
    {
        var content = Generate(t => t.Details = ReportDetailsState.Expanded);
        Assert.Contains("<button class=\"details-radio-btn details-active\" data-state=\"expanded\" onclick=\"window._setReportDetails('expanded')\">Expand</button>", content);
        Assert.Contains("<button class=\"details-radio-btn\" data-state=\"truncated\" onclick=\"window._setReportDetails('truncated')\">Truncate</button>", content);
        // syncRadioButtons disables the select whenever the state is not truncated — the
        // seeded markup must agree before the first click.
        Assert.Contains("<select class=\"truncate-lines-select\" autocomplete=\"off\" disabled onchange=\"window._setTruncateLines(this)\">", content);
        // Scenario-level twin seeds consistently
        Assert.Contains("<button class=\"details-radio-btn details-active\" data-state=\"expanded\" onclick=\"window._setAllNotes(this,'expanded')\">Expand</button>", content);
    }

    [Fact]
    public void Truncate_lines_default_selects_the_configured_option()
    {
        var content = Generate(t => t.TruncateLines = TruncateLineCount.Lines10);
        Assert.Contains("<option value=\"10\" selected>10</option>", content);
        Assert.DoesNotContain("<option value=\"40\" selected>40</option>", content);
    }

    [Fact]
    public void Truncate_option_list_is_built_from_the_enum()
    {
        var content = Generate();
        Assert.Contains(
            "<option value=\"3\">3</option><option value=\"4\">4</option><option value=\"5\">5</option>" +
            "<option value=\"10\">10</option><option value=\"15\">15</option><option value=\"20\">20</option>" +
            "<option value=\"25\">25</option><option value=\"30\">30</option><option value=\"35\">35</option>" +
            "<option value=\"40\" selected>40</option><option value=\"50\">50</option><option value=\"60\">60</option>" +
            "<option value=\"80\">80</option><option value=\"100\">100</option>", content);
    }

    [Fact]
    public void Headers_hidden_default_seeds_both_toolbar_levels()
    {
        var content = Generate(t => t.HeadersShown = false);
        Assert.Contains("<button class=\"details-radio-btn toggle-btn\" data-toggle=\"headers\" data-shown=\"false\" onclick=\"window._toggleHeaders(this)\">Headers Hidden</button>", content);
        Assert.Contains("<button class=\"details-radio-btn toggle-btn\" data-toggle=\"headers\" data-shown=\"false\" onclick=\"window._toggleScenarioHeaders(this)\">Headers Hidden</button>", content);
    }

    [Fact]
    public void Assertions_shown_default_seeds_the_gated_button()
    {
        var content = Generate(t => t.AssertionsShown = true);
        Assert.Contains("<button class=\"details-radio-btn toggle-btn details-active\" data-toggle=\"assertions\" data-shown=\"true\" onclick=\"window._toggleAssertions(this)\">Assertions Shown</button>", content);
    }

    [Fact]
    public void Steps_hidden_default_seeds_the_gated_button()
    {
        var content = Generate(t => t.StepsShown = false);
        Assert.Contains("<button class=\"details-radio-btn toggle-btn\" data-toggle=\"steps\" data-shown=\"false\" onclick=\"window._toggleSteps(this)\">Steps Hidden</button>", content);
    }

    [Fact]
    public void Databases_hidden_default_seeds_the_gated_button()
    {
        var content = Generate(t => t.DatabasesShown = false);
        Assert.Contains("<button class=\"details-radio-btn toggle-btn\" data-toggle=\"databases\" data-shown=\"false\" onclick=\"window._toggleDatabases(this)\">Databases Hidden</button>", content);
    }

    [Fact]
    public void Gated_controls_stay_absent_however_configured()
    {
        // A diagram with no assertion notes / step delimiters / database participants emits no
        // toggle buttons — the configured defaults resolve normally and are simply inert.
        var content = Generate(t =>
        {
            t.AssertionsShown = true;
            t.StepsShown = false;
            t.DatabasesShown = false;
        }, diagramSource: NoGatesDiagramSource);
        // The embedded scripts mention the selectors; only the BUTTON markup must be absent.
        Assert.DoesNotContain("data-toggle=\"assertions\" data-shown", content);
        Assert.DoesNotContain("data-toggle=\"steps\" data-shown", content);
        Assert.DoesNotContain("data-toggle=\"databases\" data-shown", content);
    }

    [Fact]
    public void Note_format_group_default_seeds_selects_and_script()
    {
        var content = Generate(t => t.NotePayloadFormat = NotePayloadFormat.Yaml);
        Assert.Contains("<option value=\"json\">JSON</option><option value=\"yaml\" selected>YAML</option>", content);
        Assert.Contains("window._noteFormatDefault = 'yaml'", content);
    }

    // ═══════════════════════════════════════════════════════════
    // Filter modes (M6 C# side)
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Dependency_filter_mode_default_seeds_button_text_and_script()
    {
        var content = Generate(t => t.DependencyFilterMode = FilterCombinationMode.Or);
        Assert.Contains("onclick=\"toggle_dep_mode(this)\">OR</button>", content);
        Assert.Contains("var _depModeDefault = 'OR'", content);
    }

    [Fact]
    public void Category_filter_mode_default_seeds_button_text_and_script()
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
                        Categories = ["smoke"]
                    }
                ]
            }
        };
        var content = Generate(t => t.CategoryFilterMode = FilterCombinationMode.And, features: features);
        Assert.Contains("onclick=\"toggle_cat_mode(this)\">AND</button>", content);
        Assert.Contains("var _catModeDefault = 'AND'", content);
    }

    [Fact]
    public void Filter_mode_builtins_seed_the_historical_texts()
    {
        var content = Generate();
        Assert.Contains("var _depModeDefault = 'AND'", content);
        Assert.Contains("var _catModeDefault = 'OR'", content);
        Assert.Contains("onclick=\"toggle_dep_mode(this)\">AND</button>", content);
    }

    // ═══════════════════════════════════════════════════════════
    // Structure (M4), panels + diagram tab (M5), Tier 2 (M7)
    // ═══════════════════════════════════════════════════════════

    /// <summary>Test-run shape over the rich baseline fixture with configured toggle defaults.</summary>
    private static string GenerateRich(Action<ReportToggleDefaults> configure, bool specifications = false)
    {
        var (diagrams, specFeatures, testRunFeatures, segments, cleanup) = ToggleDefaultsBaselineTests.BuildRichFixture();
        using (cleanup)
        {
            var options = new ReportConfigurationOptions();
            configure(options.TestRunReportToggleDefaults);
            var resolved = ReportToggleDefaultsResolver.Resolve(options, specifications);
            var path = specifications
                ? ToggleDefaultsBaselineTests.GenerateSpecShape(diagrams, specFeatures, segments, $"ToggleRich_{Guid.NewGuid():N}.html", resolved)
                : ToggleDefaultsBaselineTests.GenerateTestRunShape(diagrams, testRunFeatures, segments, $"ToggleRich_{Guid.NewGuid():N}.html", resolved);
            return File.ReadAllText(path);
        }
    }

    [Fact]
    public void Features_expanded_default_opens_features_and_seeds_the_button()
    {
        var content = Generate(t => t.FeaturesExpanded = true);
        Assert.Contains("<details class=\"feature\" open>", content);
        Assert.Contains(">Collapse All Features</button>", content);
        Assert.Contains(">Expand All Scenarios</button>", content);
    }

    [Fact]
    public void Scenarios_expanded_default_opens_scenarios_and_seeds_the_button()
    {
        var content = Generate(t => t.ScenariosExpanded = true);
        Assert.Contains("<details class=\"scenario\" open", content);
        Assert.Contains(">Collapse All Scenarios</button>", content);
        Assert.Contains(">Expand All Features</button>", content);
    }

    [Fact]
    public void Scenarios_expanded_default_opens_parameterized_groups_too()
    {
        var content = GenerateRich(t => t.ScenariosExpanded = true);
        Assert.Contains("<details class=\"scenario scenario-parameterized\" open", content);
    }

    [Fact]
    public void Activity_diagram_tab_default_seeds_active_button_and_view_visibility()
    {
        var content = GenerateRich(t => t.DiagramTab = DiagramTabKind.Activity);
        // Where seq + activity + flame all exist, activity starts active and seq is hidden
        Assert.Contains("<button class=\"diagram-toggle-btn diagram-toggle-active\" data-dtype=\"activity\">Activity Diagrams</button>", content);
        Assert.Contains("<button class=\"diagram-toggle-btn\" data-dtype=\"seq\">Sequence Diagrams</button>", content);
        Assert.Contains("<div class=\"diagram-view diagram-view-seq\" style=\"display:none\">", content);
        Assert.Contains("<div class=\"diagram-view diagram-view-activity\">", content);
    }

    [Fact]
    public void Flame_chart_tab_default_seeds_where_flame_exists()
    {
        var content = GenerateRich(t => t.DiagramTab = DiagramTabKind.FlameChart);
        Assert.Contains("<button class=\"diagram-toggle-btn diagram-toggle-active\" data-dtype=\"flame\">Flame Chart</button>", content);
        Assert.Contains("<div class=\"diagram-view diagram-view-flame\">", content);
        Assert.Contains("<div class=\"diagram-view diagram-view-activity\" style=\"display:none\">", content);
    }

    [Fact]
    public void Failure_clusters_closed_default_drops_the_open_attribute()
    {
        var content = GenerateRich(t => t.FailureClustersOpen = false);
        Assert.Contains("<details class=\"failure-clusters\">", content);
        Assert.DoesNotContain("<details class=\"failure-clusters\" open>", content);
    }

    [Fact]
    public void Rules_closed_default_drops_the_open_attribute()
    {
        var content = GenerateRich(t => t.RulesOpen = false);
        Assert.Contains("<details class=\"rule\"><summary", content);
        Assert.DoesNotContain("<details class=\"rule\" open>", content);
    }

    [Fact]
    public void Diagnostics_open_default_opens_the_disclosure()
    {
        var content = GenerateRich(t => t.DiagnosticsOpen = true);
        Assert.Contains("<details class=\"report-diagnostics\" open>", content);
    }

    [Fact]
    public void Steps_section_closed_default_drops_the_open_attribute()
    {
        var content = Generate(t => t.StepsSectionOpen = false);
        Assert.Contains("<details class=\"scenario-steps\">", content);
        Assert.DoesNotContain("<details class=\"scenario-steps\" open>", content);
    }

    [Fact]
    public void Background_steps_open_default_opens_the_separate_section()
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
                        BackgroundSteps = [new ScenarioStep { Keyword = "Given", Text = "background", Status = ExecutionResult.Passed }],
                        Steps = [new ScenarioStep { Keyword = "Then", Text = "a step", Status = ExecutionResult.Passed }]
                    }
                ]
            }
        };
        var options = new ReportConfigurationOptions { TestRunReportToggleDefaults = { BackgroundStepsOpen = true } };
        var path = ReportGenerator.GenerateHtmlReport(
            [new DefaultDiagramsFetcher.DiagramAsCode("s1", "", NoGatesDiagramSource)], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, $"ToggleBg_{Guid.NewGuid():N}.html", "Test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            separateBackgroundSteps: true,
            toggleDefaults: ReportToggleDefaultsResolver.Resolve(options, specifications: false));
        var content = File.ReadAllText(path);
        Assert.Contains("<details class=\"scenario-background\" open>", content);
    }

    [Fact]
    public void Raw_plantuml_open_default_opens_the_example_disclosures_in_server_mode()
    {
        var options = new ReportConfigurationOptions { TestRunReportToggleDefaults = { RawPlantUmlOpen = true } };
        var path = ReportGenerator.GenerateHtmlReport(
            [new DefaultDiagramsFetcher.DiagramAsCode("s1", "", NoGatesDiagramSource)], SimpleFeatures,
            DateTime.UtcNow, DateTime.UtcNow,
            null, $"ToggleRaw_{Guid.NewGuid():N}.html", "Test", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.Server,
            toggleDefaults: ReportToggleDefaultsResolver.Resolve(options, specifications: false));
        var content = File.ReadAllText(path);
        Assert.Contains("<details class=\"example\" open>", content);
    }

    [Fact]
    public void Grouped_parameter_table_default_swaps_which_table_starts_visible()
    {
        var content = GenerateRich(t => t.ParameterTableView = ParameterTableView.Grouped);
        Assert.Contains("<table class=\"param-test-table param-table-flat\" style=\"display:none\"", content);
        Assert.Contains("<table class=\"param-test-table param-table-grouped\" data-prefix=", content);
    }

    [Fact]
    public void Flat_parameter_table_stays_the_builtin_default()
    {
        var content = GenerateRich(_ => { });
        Assert.Contains("<table class=\"param-test-table param-table-flat\" data-prefix=", content);
        Assert.Contains("<table class=\"param-test-table param-table-grouped\" style=\"display:none\"", content);
    }

    [Fact]
    public void Internal_flow_tab_default_seeds_the_whole_test_flow_toggle()
    {
        var (_, _, _, segments, cleanup) = ToggleDefaultsBaselineTests.BuildRichFixture();
        using (cleanup)
        {
            var html = Kronikol.InternalFlow.InternalFlowHtmlGenerator.GenerateWholeTestFlowHtml(
                segments, "s2", [], WholeTestFlowVisualization.Both, InternalFlowTab.FlameChart);
            Assert.Contains("<button class=\"iflow-toggle-btn iflow-toggle-active\" data-view=\"flame\">Flame Chart</button>", html);
            Assert.Contains("<button class=\"iflow-toggle-btn\" data-view=\"main\">Activity</button>", html);
            Assert.Contains("<div class=\"iflow-view iflow-view-main\" style=\"display:none\">", html);

            var builtin = Kronikol.InternalFlow.InternalFlowHtmlGenerator.GenerateWholeTestFlowHtml(
                segments, "s2", [], WholeTestFlowVisualization.Both);
            Assert.Contains("<button class=\"iflow-toggle-btn iflow-toggle-active\" data-view=\"main\">Activity</button>", builtin);
            Assert.Contains("<div class=\"iflow-view iflow-view-flame\" style=\"display:none\">", builtin);
        }
    }

    [Fact]
    public void Internal_flow_tab_default_seeds_the_popup_segment_data()
    {
        var (_, _, _, segments, cleanup) = ToggleDefaultsBaselineTests.BuildRichFixture();
        using (cleanup)
        {
            // BuildSegmentData returns the pre-serialization map — assert on the content HTML
            // directly (the script wrapper JSON-escapes the quotes).
            var data = Kronikol.InternalFlow.InternalFlowHtmlGenerator.BuildSegmentData(
                segments, InternalFlowDiagramStyle.ActivityDiagram,
                showFlameChart: true,
                startTab: InternalFlowTab.FlameChart);
            var entry = data["iflow-test-s2"];
            var content = (string)entry.GetType().GetProperty("content")!.GetValue(entry)!;
            Assert.Contains("<button class=\"iflow-toggle-btn iflow-toggle-active\" data-view=\"flame\">Flame Chart</button>", content);
            Assert.Contains("<button class=\"iflow-toggle-btn\" data-view=\"main\">Activity</button>", content);
            Assert.Contains("<div class=\"iflow-view iflow-view-main\" style=\"display:none\">", content);
            Assert.Contains("<div class=\"iflow-view iflow-view-flame\">", content);
        }
    }

    [Fact]
    public void Single_match_reveal_opens_every_section_that_can_hide_matched_content()
    {
        // The M7 rule: a disclosure default that can hide deep-search-covered content must be
        // opened by the single-match reveal — rule ancestors, steps, background steps, diagrams.
        var content = Generate();
        Assert.Contains("function revealSingleMatch(", content);
        Assert.Contains("closest('details.rule')", content);
        Assert.Contains("'details.example-diagrams', 'details.scenario-steps', 'details.scenario-background'", content);
    }

    // ═══════════════════════════════════════════════════════════
    // Merge renderer forwards the defaults
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Merge_renderer_forwards_toggle_defaults()
    {
        var report = new MergeableReport
        {
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow,
            Features = SimpleFeatures,
            Diagrams = [new DefaultDiagramsFetcher.DiagramAsCode("s1", "", AllGatesDiagramSource)]
        };
        var options = new ReportConfigurationOptions
        {
            TestRunReportToggleDefaults = { Details = ReportDetailsState.Expanded, HeadersShown = false }
        };
        var output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Reports", $"ToggleMerge_{Guid.NewGuid():N}.html");
        var written = MergeableReportRenderer.Render(report, output, options: options);
        var content = File.ReadAllText(written);
        Assert.Contains("<button class=\"details-radio-btn details-active\" data-state=\"expanded\" onclick=\"window._setReportDetails('expanded')\">Expand</button>", content);
        Assert.Contains("window._detailsDefault = 'expanded'", content);
        Assert.Contains("Headers Hidden</button>", content);
    }
}
