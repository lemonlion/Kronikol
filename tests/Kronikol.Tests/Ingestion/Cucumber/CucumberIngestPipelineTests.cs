using System.Text.Json;
using Kronikol.Ingestion;
using Kronikol.Ingestion.Cucumber;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Ingestion.Cucumber;

/// <summary>
/// End-to-end: a Cucumber Messages file, a tests NDJSON and interaction captures all joined on the
/// <c>kronikol-test-id</c> the fixture attached, replayed through <see cref="IngestPipeline"/>.
/// </summary>
[Collection("DiagramsFetcher")]
public class CucumberIngestPipelineTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-cucumber-ingest-" + Guid.NewGuid().ToString("N"));

    public CucumberIngestPipelineTests()
    {
        Directory.CreateDirectory(_dir);
        RequestResponseLogger.Redaction = null;
    }

    public void Dispose()
    {
        RequestResponseLogger.Redaction = null;
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private string Path_(string name) => System.IO.Path.Combine(_dir, name);

    private static ReportConfigurationOptions OptionsFor(string output)
    {
        var options = IngestPipeline.DefaultOptions();
        options.ReportsFolderPath = output;
        return options;
    }

    /// <summary>The step windows of one fixture scenario, so captures can be placed inside a Gherkin step.</summary>
    private static (string TestId, IReadOnlyList<CucumberStepWindow> Windows) ScenarioOf(string displayName)
    {
        var result = CucumberFixtures.Build();
        var scenario = result.Features.SelectMany(f => f.Scenarios).First(s => s.DisplayName == displayName);
        return (scenario.Id, result.StepWindows[scenario.Id]);
    }

    [Fact]
    public void Messages_win_for_structure_while_the_tests_file_contributes_assertions_and_identity()
    {
        var (testId, windows) = ScenarioOf(CucumberFixtures.FailingScenario);
        var background = windows.Single(w => w.Step.Text == "the catalogue is loaded");
        var failing = windows.Single(w => w.Step.Text == "the step blows up");

        // The reporter's own step event for the same test: dropped, because the messages own the structure.
        var testsFile = Path_("tests.ndjson");
        File.WriteAllLines(testsFile,
        [
            new TestRunRecord { Event = "start", TestId = testId, TestName = "reporter name", Feature = "reporter.spec.ts", Timestamp = failing.Start.AddMilliseconds(-5) }.ToJson(),
            new TestRunRecord { Event = "step", TestId = testId, Text = "a reporter step that the messages replace", Keyword = "When", Timestamp = failing.Start, Status = "passed" }.ToJson(),
            new TestRunRecord { Event = "assertion", TestId = testId, Text = "the widget is visible", Status = "failed", Error = "Expected visible, got hidden", Timestamp = failing.Start }.ToJson(),
            new TestRunRecord { Event = "end", TestId = testId, Status = "failed", Timestamp = failing.End }.ToJson(),
        ]);

        var (request, response) = InteractionRecord.Pair(testId, null, "POST", "http://localhost:8081/sidekick", "graphql", "web",
            requestContent: """{"query":"query Widget { widget }"}""", responseContent: """{"data":{}}""", statusCode: "200",
            requestTimestamp: MidPoint(background), responseTimestamp: background.End);
        var capture = Path_("captures.ndjson");
        File.WriteAllLines(capture, new[] { request.ToJson(), response.ToJson() });

        var output = Path_("Reports");
        var result = IngestPipeline.Run(new IngestRequest
        {
            InteractionFiles = [capture],
            TestsFile = testsFile,
            CucumberMessagesFiles = [CucumberFixtures.MessagesPath],
            Options = OptionsFor(output),
        });

        Assert.True(result.Generated);
        Assert.Equal(6, result.ScenarioCount);

        var scenario = result.Features.SelectMany(f => f.Scenarios).Single(s => s.Id == testId);

        // Structure is Gherkin's, not the reporter's.
        Assert.Equal(CucumberFixtures.FailingScenario, scenario.DisplayName);
        Assert.Equal(CucumberFixtures.Rule, scenario.Rule);
        Assert.Single(scenario.BackgroundSteps!);
        Assert.Equal(["Given", "When", "Then"], scenario.Steps!.Select(s => s.Keyword));
        Assert.DoesNotContain(scenario.Steps!, s => s.Text.Contains("reporter step"));
        Assert.Equal(CucumberFixtures.DemoFeature, result.Features.Single(f => f.Scenarios.Contains(scenario)).DisplayName);

        // The assertion the reporter recorded nests under the Gherkin step whose window contains it.
        var blowsUp = scenario.Steps!.Single(s => s.Text == "the step blows up");
        var assertion = Assert.Single(blowsUp.SubSteps!);
        Assert.Equal($"{Track.FailSymbol} The widget is visible", assertion.Text);
        Assert.Equal(ExecutionResult.Failed, assertion.Status);
        Assert.Contains("Expected visible, got hidden", assertion.Comments!);

        // The capture joined on the same id, so the scenario has its own diagram.
        using var json = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(output, "TestRunReport.json")));
        var diagram = FindDiagram(json, testId);
        var lines = diagram.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        var arrows = lines.Where(l => l.Contains("]> ") || l.Contains("]-> ") || l.Contains("hnote")).ToArray();

        // A step delimiter bar per Gherkin step, labelled with its keyword.
        Assert.Contains(arrows, l => l.Contains("<<stepDelimiter>>") && l.Contains("Given the catalogue is loaded"));
        var given = Array.FindIndex(arrows, l => l.Contains("<<stepDelimiter>>") && l.Contains("""Given a customer named "Ada" """.TrimEnd()));
        var when = Array.FindIndex(arrows, l => l.Contains("<<stepDelimiter>>") && l.Contains("When the step blows up"));
        var then = Array.FindIndex(arrows, l => l.Contains("<<stepDelimiter>>") && l.Contains("Then the order is confirmed"));
        var call = Array.FindIndex(arrows, l => l.Contains("(query Widget)"));
        var fail = Array.FindIndex(arrows, l => l.Contains("<<assertionNote>>") && l.Contains(Track.FailColor));
        var backgroundBar = Array.FindIndex(arrows, l => l.Contains("<<stepDelimiter>>") && l.Contains("Given the catalogue is loaded"));
        // Bars in Gherkin order; the call captured during the Background sits under its bar; the failed
        // assertion lands between the step it belongs to and the next one.
        Assert.True(backgroundBar >= 0 && call > backgroundBar && given > call && when > given && then > when
                    && fail > given && then > fail,
            "unexpected order:" + Environment.NewLine + string.Join(Environment.NewLine, arrows));

        // The ✗ note carries its message.
        Assert.Contains($"{Track.FailSymbol} The widget is visible", diagram);
        Assert.Contains("Expected visible, got hidden", diagram);
    }

    [Fact]
    public void A_messages_only_ingest_needs_no_captures_and_no_tests_file()
    {
        var output = Path_("MessagesOnly");

        var result = IngestPipeline.Run(new IngestRequest
        {
            CucumberMessagesFiles = [CucumberFixtures.MessagesPath],
            Options = OptionsFor(output),
        });

        Assert.True(result.Generated);
        Assert.Equal(6, result.ScenarioCount);
        Assert.Equal(2, result.Features.Length);
        Assert.True(File.Exists(result.TestRunReportHtml));
    }

    [Fact]
    public void Specifications_reads_as_living_documentation_for_a_green_run()
    {
        var subset = CucumberFixtures.WriteSubset(Path_("green.ndjson"),
            CucumberFixtures.TableScenario, CucumberFixtures.OutlineScenario, CucumberFixtures.SimpleScenario);
        var output = Path_("Green");

        var result = IngestPipeline.Run(new IngestRequest
        {
            CucumberMessagesFiles = [subset],
            Options = OptionsFor(output),
        });

        Assert.True(result.Generated);
        Assert.All(result.Features.SelectMany(f => f.Scenarios), s => Assert.Equal(ExecutionResult.Passed, s.Result));

        var html = File.ReadAllText(System.IO.Path.Combine(output, "Specifications.html"));

        // Feature description, rule and background all reach the living document.
        Assert.Contains("Cucumber Messages importer has to map.", html);
        Assert.Contains(CucumberFixtures.Rule, html);
        Assert.Contains("the catalogue is loaded", html);

        // Given/When/Then with the data table and the doc string.
        foreach (var keyword in new[] { "Given", "When", "Then" })
            Assert.Contains(keyword, html);
        Assert.Contains("the following order lines:", html);
        foreach (var cell in new[] { "sku", "quantity", "price", "APPLE-1", "PEAR-7", "2.25" })
            Assert.Contains(cell, html);
        Assert.Contains("""{ "channel": "web", "currency": "GBP" }""".Replace("\"", "&quot;"), html);

        // The outline renders as one parameterised group over its two rows.
        var outlineRows = result.Features.SelectMany(f => f.Scenarios)
            .Where(s => s.OutlineId == CucumberFixtures.OutlineScenario).ToArray();
        var (groups, _) = ParameterGrouper.Analyze(outlineRows);
        var group = Assert.Single(groups);
        Assert.Equal(CucumberFixtures.OutlineScenario, group.GroupDisplayName);
        Assert.Equal(["customer", "page"], group.ParameterNames);
        Assert.Equal(2, group.Scenarios.Length);
    }

    [Fact]
    public void Scenarios_the_messages_do_not_own_keep_their_place()
    {
        var testsFile = Path_("other-tests.ndjson");
        var other = "00000000000000000000000000000042";
        File.WriteAllLines(testsFile,
        [
            new TestRunRecord { Event = "start", TestId = other, TestName = "a plain xUnit test", Feature = "Unit tests", Timestamp = DateTimeOffset.UnixEpoch }.ToJson(),
            new TestRunRecord { Event = "end", TestId = other, Status = "passed", DurationMs = 12, Timestamp = DateTimeOffset.UnixEpoch.AddSeconds(1) }.ToJson(),
        ]);
        var output = Path_("Mixed");

        var result = IngestPipeline.Run(new IngestRequest
        {
            TestsFile = testsFile,
            CucumberMessagesFiles = [CucumberFixtures.MessagesPath],
            Options = OptionsFor(output),
        });

        Assert.Equal(7, result.ScenarioCount);
        var unit = result.Features.Single(f => f.DisplayName == "Unit tests");
        Assert.Equal("a plain xUnit test", Assert.Single(unit.Scenarios).DisplayName);
        // Gherkin features come first, the leftovers after them.
        Assert.Equal([CucumberFixtures.DemoFeature, CucumberFixtures.RetryFeature, "Unit tests"],
            result.Features.Select(f => f.DisplayName));
    }

    [Fact]
    public void Include_hooks_surfaces_the_hook_steps_in_the_report()
    {
        var output = Path_("Hooks");

        var result = IngestPipeline.Run(new IngestRequest
        {
            CucumberMessagesFiles = [CucumberFixtures.MessagesPath],
            IncludeHooks = true,
            Options = OptionsFor(output),
        });

        var scenario = result.Features.SelectMany(f => f.Scenarios).First(s => s.DisplayName == CucumberFixtures.SimpleScenario);
        Assert.Contains(scenario.Steps!, s => s.Text == "BeforeEach hook");
    }

    [Fact]
    public void An_unreadable_messages_file_is_a_FileNotFoundException_not_a_silent_skip()
    {
        Assert.Throws<FileNotFoundException>(() => IngestPipeline.Run(new IngestRequest
        {
            CucumberMessagesFiles = [Path_("does-not-exist.ndjson")],
            Options = OptionsFor(Path_("None")),
        }));
    }

    private static DateTimeOffset MidPoint(CucumberStepWindow window) =>
        window.Start + TimeSpan.FromTicks((window.End - window.Start).Ticks / 2);

    private static string FindDiagram(JsonDocument json, string scenarioId)
    {
        foreach (var feature in json.RootElement.GetProperty("features").EnumerateArray())
        {
            foreach (var scenario in feature.GetProperty("scenarios").EnumerateArray())
            {
                if (scenario.GetProperty("id").GetString() == scenarioId)
                    return scenario.GetProperty("diagrams")[0].GetString()!;
            }
        }

        throw new InvalidOperationException($"No diagram for scenario '{scenarioId}'.");
    }
}
