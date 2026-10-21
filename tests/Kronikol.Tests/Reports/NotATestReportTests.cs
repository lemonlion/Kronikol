using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml.Schema;
using Kronikol.Reports;
using Kronikol.Reports.Merge;

namespace Kronikol.Tests.Reports;

/// <summary>
/// A scenario that is not a test (<see cref="Scenario.NotATest"/>: ingest's "Traffic outside any test", a stack's
/// start-up) is drawn like any scenario and counted as no test (plans/RUN_WINDOW_PLAN.md R1, section 3.10). Until then
/// only the history read the flag, so the fold scenario counted as a passed test in the run band, the ring, the features
/// table, the timeline, the percentiles, the CI summary, Failures.md, the console summary, Run.json and the CTRF report,
/// and the Specifications listed it; the data files did not carry it, so a merge lost it.
/// </summary>
[Collection("DiagramsFetcher")]
public class NotATestReportTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-not-a-test").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Two tests in Orders; a test and ingest's fold scenario in Ingested; and a feature that holds nothing but a stack's
    /// start-up. Three tests, two scenarios that are not tests, and the not-a-test scenarios are the slowest by far.
    /// </summary>
    private static Feature[] Features(ExecutionResult reject = ExecutionResult.Passed) =>
    [
        new Feature
        {
            DisplayName = "Orders",
            Scenarios =
            [
                new Scenario { Id = "create", DisplayName = "Create order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(2) },
                new Scenario
                {
                    Id = "reject", DisplayName = "Reject order", Result = reject, Duration = TimeSpan.FromSeconds(1),
                    ErrorMessage = reject == ExecutionResult.Failed ? "boom" : null
                },
            ]
        },
        new Feature
        {
            DisplayName = "Ingested",
            Scenarios =
            [
                new Scenario { Id = "logged", DisplayName = "Logged only", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(500) },
                new Scenario { Id = "session", DisplayName = "Traffic outside any test", Result = ExecutionResult.Passed, NotATest = true, Duration = TimeSpan.FromSeconds(780) },
            ]
        },
        new Feature
        {
            DisplayName = "Stack start-up",
            Scenarios = [new Scenario { Id = "stack-start-up", DisplayName = "Stack start-up", Result = ExecutionResult.Passed, NotATest = true, Duration = TimeSpan.FromSeconds(25) }]
        },
    ];

    private static string Report(string fileName, Feature[] features, bool includeTestRunData = true) =>
        File.ReadAllText(ReportGenerator.GenerateHtmlReport([], features, Start, Start.AddSeconds(9), null, fileName, "Run", includeTestRunData,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs));

    /// <summary>A scenario's opening tag and summary, up to its copy button, which carries its name.</summary>
    private static string ScenarioHead(string html, string name)
    {
        var copy = html.IndexOf($"data-scenario-name=\"{name}\"", StringComparison.Ordinal);
        Assert.True(copy >= 0, $"no scenario named {name} in the page");
        var open = html.LastIndexOf("<details class=\"scenario", copy, StringComparison.Ordinal);
        return html[open..copy];
    }

    /// <summary>The features table's row whose name button says <paramref name="feature"/>.</summary>
    private static string TableRow(string html, string feature)
    {
        var name = html.IndexOf($"class=\"feature-open\" title=\"Open this feature's card in the Full view\">{feature}</button>", StringComparison.Ordinal);
        Assert.True(name >= 0, $"no table row for {feature}");
        var open = html.LastIndexOf("<tr", name, StringComparison.Ordinal);
        var close = html.IndexOf("</tr>", name, StringComparison.Ordinal);
        return html[open..(close + "</tr>".Length)];
    }

    [Fact]
    public void The_run_band_and_the_ring_count_the_tests_and_the_features_that_hold_one()
    {
        var html = Report("NotATest_Band.html", Features(reject: ExecutionResult.Failed));

        Assert.Contains("<dl class=\"run-stats\"><div class=\"run-stat\"><dt>Scenarios</dt><dd>3</dd></div>"
                        + "<div class=\"run-stat run-stat-pass\"><dt>Passed</dt><dd>2</dd></div>"
                        + "<div class=\"run-stat run-stat-fail\"><dt>Failed</dt><dd>1</dd></div>"
                        + "<div class=\"run-stat\"><dt>Skipped</dt><dd>0</dd></div>"
                        + "<div class=\"run-stat\"><dt>Features</dt><dd>2</dd></div>", html);
        Assert.Contains("aria-label=\"66% passed\"", html);
        Assert.Contains("<title>Passed: 2 (67%)</title>", html);
        Assert.Contains("<title>Failed: 1 (33%)</title>", html);
    }

    [Fact]
    public void A_run_of_nothing_but_scenarios_that_are_not_tests_counts_none_and_draws_no_ring()
    {
        var html = Report("NotATest_None.html", [Features()[2]]);

        Assert.Contains("<div class=\"run-stat\"><dt>Scenarios</dt><dd>0</dd></div>", html);
        Assert.Contains("<div class=\"run-stat\"><dt>Features</dt><dd>0</dd></div>", html);
        Assert.Contains("<span class=\"run-status run-status-passed\">Passed</span>", html);
        Assert.DoesNotContain("<div class=\"run-ring\">", html);
        Assert.Contains(">All 0 scenarios, 0 features</span>", html);
    }

    [Fact]
    public void The_readouts_count_the_tests_and_the_features_that_hold_one()
    {
        var html = Report("NotATest_Readout.html", Features());

        Assert.Contains("<span class=\"filter-readout\" role=\"status\" title=\"The scenarios and features the filters leave showing\">All 3 scenarios, 2 features</span>", html);
        Assert.Contains("<span class=\"nav-readout-short\">All 3 scenarios</span>", html);
    }

    [Fact]
    public void The_features_table_counts_tests_and_a_feature_of_no_test_says_so_with_no_time()
    {
        var html = Report("NotATest_Table.html", Features(reject: ExecutionResult.Failed));

        Assert.Equal("<tr class=\"failed\"><td><button type=\"button\" class=\"feature-open\" title=\"Open this feature's card in the Full view\">Orders</button></td>"
                     + "<td>2</td><td>1</td><td>1</td><td>0</td>"
                     + "<td class=\"grp-start\" data-sort=\"3000\">3s</td><td data-sort=\"1500\">1s</td><td data-sort=\"2000\">2s</td></tr>",
            TableRow(html, "Orders"));
        // The fold scenario is in no count and no time of its feature: the 13 minutes it spans are not the test's.
        Assert.Equal("<tr><td><button type=\"button\" class=\"feature-open\" title=\"Open this feature's card in the Full view\">Ingested</button></td>"
                     + "<td>1</td><td>1</td><td>0</td><td>0</td>"
                     + "<td class=\"grp-start\" data-sort=\"500\">500ms</td><td data-sort=\"500\">500ms</td><td data-sort=\"500\">500ms</td></tr>",
            TableRow(html, "Ingested"));
        // A feature of no test: its counts say so, and its time cells are empty, so a sort by time puts it last.
        Assert.Equal("<tr class=\"not-a-test\"><td><button type=\"button\" class=\"feature-open\" title=\"Open this feature's card in the Full view\">Stack start-up</button></td>"
                     + "<td data-sort=\"0\">not a test</td><td></td><td></td><td></td>"
                     + "<td class=\"grp-start\"></td><td></td><td></td></tr>",
            TableRow(html, "Stack start-up"));
    }

    [Fact]
    public void A_scenario_that_is_not_a_test_is_drawn_with_a_badge_and_carries_no_test_time()
    {
        var html = Report("NotATest_Scenario.html", Features());

        var startUp = ScenarioHead(html, "Stack start-up");
        Assert.Contains(" data-status=\"Passed\" data-not-a-test=\"1\" ", startUp);
        // No data-duration-ms: the duration filter, the duration: qualifier and the CSV read a test's time from it.
        Assert.DoesNotContain("data-duration-ms=", startUp);
        Assert.DoesNotContain("data-warmup-ms=", startUp);
        Assert.Contains("title=\"Not a test: what it holds is drawn, and the run's counts and times leave it out\">Stack start-up"
                        + " <span class=\"label not-a-test\">not a test</span> <span class=\"duration-badge\">25.0s</span>", startUp);

        var test = ScenarioHead(html, "Create order");
        Assert.DoesNotContain("data-not-a-test", test);
        Assert.Contains(" data-duration-ms=\"2000\"", test);
        Assert.Contains("<span class=\"duration-badge duration-moderate\">2.0s</span>", test);
    }

    [Fact]
    public void The_timeline_and_the_percentiles_leave_out_a_scenario_that_is_not_a_test()
    {
        var html = Report("NotATest_Timeline.html", Features());

        var labels = Regex.Matches(html, "<div class=\"timeline-label\" title=\"([^\"]*)\">").Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(["Create order", "Reject order", "Logged only"], labels);
        // The tests' 500 ms, 1 s and 2 s: with the fold's 13 minutes and the start-up's 25 s, P50 was 2 s and P90 13 minutes.
        Assert.Contains("<div class=\"duration-filters\" data-p50=\"1000\" data-p90=\"2000\" data-p95=\"2000\" data-p99=\"2000\">", html);
    }

    [Fact]
    public void The_ci_summary_counts_the_tests_and_draws_no_diagram_of_a_scenario_that_is_not_a_test()
    {
        var features = Features();
        var diagrams = features.SelectMany(f => f.Scenarios)
            .Select(s => new DefaultDiagramsFetcher.DiagramAsCode(s.Id, "", $"@startuml\nA -> B : {s.Id}\n@enduml")).ToArray();

        var markdown = CiSummaryGenerator.GenerateMarkdown(features, diagrams, diagrams, Start, Start.AddSeconds(9)).Replace("\r\n", "\n");

        Assert.Contains("| Scenarios | 3 |\n| Passed | 3 |\n| Failed | 0 |\n| Skipped | 0 |\n", markdown);
        Assert.Contains("Create order", markdown);
        Assert.Contains("Logged only", markdown);
        Assert.DoesNotContain("Stack start-up", markdown);
        Assert.DoesNotContain("Traffic outside any test", markdown);
    }

    [Fact]
    public void Failures_md_the_console_summary_and_the_digest_header_count_the_tests()
    {
        var features = Features();

        var digest = FailuresDigestGenerator.Generate(features, [], "TestRunReport.html", "5.2.5");
        Assert.Contains("All 3 scenarios passed.", digest.Markdown);
        using var header = JsonDocument.Parse(digest.Jsonl.Split('\n')[0]);
        // Run.json of a kept run reads its count from here (RunRotation)
        Assert.Equal(3, header.RootElement.GetProperty("scenarios").GetInt32());

        var summary = RunSummaryConsoleWriter.Summarise(features, _dir, [], agentInstructionsWritten: false);
        Assert.Equal(3, summary.ScenarioCount);
    }

    [Fact]
    public void A_scenario_that_is_not_a_test_is_never_a_failure_of_the_run()
    {
        // NotATest is public: a host can set it on any scenario, and its result is then no verdict of the run's.
        var features = Features();
        features[1].Scenarios[1].Result = ExecutionResult.Failed;
        features[1].Scenarios[1].ErrorMessage = "the seeder timed out";

        var html = Report("NotATest_NotAFailure.html", features);
        Assert.Contains("<span class=\"run-status run-status-passed\">Passed</span>", html);
        Assert.Contains("<div class=\"run-stat\"><dt>Failed</dt><dd>0</dd></div>", html);
        Assert.Contains("All 3 scenarios passed.", FailuresDigestGenerator.Generate(features, [], "TestRunReport.html", "5.2.5").Markdown);
        Assert.Empty(RunSummaryConsoleWriter.Summarise(features, _dir, [], agentInstructionsWritten: false).Failures);
        Assert.Contains("| Failed | 0 |", CiSummaryGenerator.GenerateMarkdown(features, [], [], Start, Start.AddSeconds(9)));
    }

    [Fact]
    public void The_ctrf_report_leaves_it_out_and_keeps_every_other_test_at_its_address()
    {
        using var ctrf = JsonDocument.Parse(CtrfReportGenerator.Generate(Features(), Start, Start.AddSeconds(9), null, "5.2.5"));
        var results = ctrf.RootElement.GetProperty("results");

        Assert.Equal(3, results.GetProperty("summary").GetProperty("tests").GetInt32());
        Assert.Equal(3, results.GetProperty("summary").GetProperty("passed").GetInt32());
        // Features by name, scenarios in file order: Ingested s0 and s1 (the fold), Orders s2 and s3, the start-up s4.
        var tests = results.GetProperty("tests").EnumerateArray()
            .Select(t => (Name: t.GetProperty("name").GetString(), Address: t.GetProperty("extra").GetProperty("kronikolAddress").GetString()))
            .ToArray();
        Assert.Equal([("Logged only", "s0"), ("Create order", "s2"), ("Reject order", "s3")], tests);
    }

    [Fact]
    public void The_specifications_leave_out_what_specifies_nothing()
    {
        var features = Features();

        var html = Report("NotATest_Specs.html", features, includeTestRunData: false);
        Assert.Contains("Logged only", html);
        Assert.DoesNotContain("Traffic outside any test", html);
        Assert.DoesNotContain("Stack start-up", html);

        var yaml = File.ReadAllText(ReportGenerator.GenerateSpecificationsData(features, Path.Combine(_dir, "Specifications.yml"), "Specs", DataFormat.Yaml));
        Assert.Contains("Logged only", yaml);
        Assert.DoesNotContain("Traffic outside any test", yaml);
        Assert.DoesNotContain("Stack start-up", yaml);

        var markdown = SpecificationsMarkdownGenerator.Generate(features, "Specs");
        Assert.Contains("Logged only", markdown);
        Assert.DoesNotContain("Traffic outside any test", markdown);
        Assert.DoesNotContain("Stack start-up", markdown);
    }

    [Fact]
    public void A_failed_scenario_that_is_not_a_test_leaves_the_specifications_written()
    {
        var features = Features();
        features[2].Scenarios[0].Result = ExecutionResult.Failed;

        var yaml = File.ReadAllText(ReportGenerator.GenerateSpecificationsData(features, Path.Combine(_dir, "Specifications.yml"), "Specs", DataFormat.Yaml,
            generateBlankOnFailedTests: true));
        Assert.Contains("Create order", yaml);
        var page = File.ReadAllText(ReportGenerator.GenerateHtmlReport([], features, Start, Start.AddSeconds(9), null, "NotATest_SpecsWritten.html",
            "Specs", includeTestRunData: false, generateBlankOnFailedTests: true));
        Assert.Contains("Create order", page);
    }

    [Fact]
    public void The_data_files_carry_not_a_test_on_such_a_scenario_only()
    {
        var features = Features();

        using var json = JsonDocument.Parse(File.ReadAllText(ReportGenerator.GenerateTestRunReportData(features, Start, Start.AddSeconds(9),
            Path.Combine(_dir, "TestRunReport.json"), DataFormat.Json)));
        var flagged = json.RootElement.GetProperty("features").EnumerateArray()
            .SelectMany(f => f.GetProperty("scenarios").EnumerateArray())
            .ToDictionary(s => s.GetProperty("name").GetString()!, s => s.TryGetProperty("notATest", out var n) ? n.ValueKind : JsonValueKind.Undefined);
        Assert.Equal(JsonValueKind.True, flagged["Traffic outside any test"]);
        Assert.Equal(JsonValueKind.True, flagged["Stack start-up"]);
        Assert.Equal(JsonValueKind.Undefined, flagged["Create order"]);
        Assert.Equal(JsonValueKind.Undefined, flagged["Logged only"]);

        var xml = XDocument.Load(ReportGenerator.GenerateTestRunReportData(features, Start, Start.AddSeconds(9),
            Path.Combine(_dir, "TestRunReport.xml"), DataFormat.Xml));
        Assert.Equal(["Traffic outside any test", "Stack start-up"],
            xml.Descendants("NotATest").Select(n => (Value: n.Value, Name: n.Parent!.Element("Name")!.Value))
                .Select(n => { Assert.Equal("true", n.Value); return n.Name; }).ToArray());

        var yaml = File.ReadAllText(ReportGenerator.GenerateTestRunReportData(features, Start, Start.AddSeconds(9),
            Path.Combine(_dir, "TestRunReport.yml"), DataFormat.Yaml));
        Assert.Equal(2, Regex.Matches(yaml, "^        NotATest: true$", RegexOptions.Multiline).Count);
    }

    [Fact]
    public void The_data_files_validate_against_their_schemas_with_the_key_present()
    {
        var features = Features();

        var jsonPath = ReportGenerator.GenerateTestRunReportData(features, Start, Start.AddSeconds(9), Path.Combine(_dir, "Valid.json"), DataFormat.Json);
        var schemaPath = ReportGenerator.GenerateTestRunReportSchema(Path.Combine(_dir, "Valid.schema.json"), DataFormat.Json);
        Assert.Empty(SchemaValidationTests.Validate(schemaPath, jsonPath));
        using (var schema = JsonDocument.Parse(File.ReadAllText(schemaPath)))
        {
            var scenario = schema.RootElement.GetProperty("properties").GetProperty("features").GetProperty("items")
                .GetProperty("properties").GetProperty("scenarios").GetProperty("items").GetProperty("properties");
            Assert.Equal("boolean", scenario.GetProperty("notATest").GetProperty("type").GetString());
        }

        var xmlPath = ReportGenerator.GenerateTestRunReportData(features, Start, Start.AddSeconds(9), Path.Combine(_dir, "Valid.xml"), DataFormat.Xml);
        var xsdPath = ReportGenerator.GenerateTestRunReportSchema(Path.Combine(_dir, "Valid.xsd"), DataFormat.Xml);
        var schemaSet = new XmlSchemaSet();
        schemaSet.Add("", System.Xml.XmlReader.Create(new StringReader(File.ReadAllText(xsdPath))));
        var errors = new List<string>();
        XDocument.Load(xmlPath).Validate(schemaSet, (_, e) => errors.Add(e.Message));
        Assert.Empty(errors);

        var ymlPath = ReportGenerator.GenerateTestRunReportData(features, Start, Start.AddSeconds(9), Path.Combine(_dir, "Valid.yml"), DataFormat.Yaml);
        var ymlSchemaPath = ReportGenerator.GenerateTestRunReportSchema(Path.Combine(_dir, "Valid.yml.schema.json"), DataFormat.Yaml);
        Assert.Empty(YamlSchemaValidationTests.ValidateYaml(ymlSchemaPath, ymlPath));
    }

    [Fact]
    public void A_mergeable_file_hands_not_a_test_back()
    {
        var json = ReportGenerator.GenerateMergeableReportJson(Features(), Start, Start.AddSeconds(9), diagramLookup: null,
            componentRelationships: null, internalFlowSegmentData: null, wholeTestFlow: null, WholeTestFlowVisualization.None, ciMetadata: null);

        var scenarios = MergeableReportReader.Parse(json).Features.SelectMany(f => f.Scenarios).ToDictionary(s => s.DisplayName);
        Assert.True(scenarios["Traffic outside any test"].NotATest);
        Assert.True(scenarios["Stack start-up"].NotATest);
        Assert.False(scenarios["Create order"].NotATest);
        Assert.False(scenarios["Logged only"].NotATest);
    }
}
