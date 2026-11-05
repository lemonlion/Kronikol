using System.Text.Json;
using Example.Api.Tests.Integration.Helpers;

namespace Example.Api.Tests.Integration.Tests;

/// <summary>
/// Regression tests for duplicated steps under double binding-assembly discovery.
///
/// The example ReqNRoll projects INTENTIONALLY list both the framework assembly
/// (e.g. <c>Kronikol.ReqNRoll.xUnit3</c>) and <c>Kronikol.ReqNRoll.Core</c> in
/// <c>reqnroll.json</c>'s <c>bindingAssemblies</c>, and must stay that way: ReqNRoll then
/// discovers both the base <c>ReqNRollTrackingHooks</c> and its framework subclass and runs
/// every hook on both instances, which is exactly the configuration existing consumers have
/// (Kronikol's own templates shipped it until the fix). Without the owner-instance guard in
/// <c>ReqNRollTrackingHooks</c>, every step is recorded twice with identical durations
/// (rendered as a duplicate "And" line in HTML reports).
/// </summary>
[Collection("SequentialTests")]
public class ReqNRollDuplicateStepsTests
{
    public static TheoryData<string> ReqNRollProjects() =>
        new() { TestProjects.ReqNRollXUnit2, TestProjects.ReqNRollXUnit3 };

    private static async Task<ReportParser.ParsedYamlScenario[]> RunAndParseScenariosAsync(string projectName)
    {
        var result = await TestProjectRunner.RunAsync(projectName);
        Assert.True(result.Success, $"{projectName} failed:\n{result.StandardError}\n{result.StandardOutput}");

        var reports = ReportParser.GetReportFiles(result.ReportsFolderPath);
        Assert.NotNull(reports.SpecificationsYaml);

        var scenarios = await ReportParser.ExtractScenarioStepsFromYamlAsync(reports.SpecificationsYaml);
        Assert.NotEmpty(scenarios);
        return scenarios;
    }

    private static void AssertNoAdjacentDuplicates(string projectName, ReportParser.ParsedYamlScenario scenario)
    {
        // No example feature legitimately repeats the same step twice in a row, so any
        // adjacent duplicate is the double-hook-execution bug. The exact-step assertions
        // in the other tests are the authoritative check.
        foreach (var stepList in new[] { scenario.BackgroundSteps, scenario.Steps })
        {
            for (var i = 1; i < stepList.Length; i++)
            {
                Assert.False(stepList[i] == stepList[i - 1],
                    $"{projectName} / '{scenario.Name}': step '{stepList[i]}' appears twice in a row — " +
                    "hooks executed on both discovered binding instances");
            }
        }
    }

    [Theory]
    [MemberData(nameof(ReqNRollProjects))]
    public async Task No_scenario_records_adjacent_duplicate_steps(string projectName)
    {
        var scenarios = await RunAndParseScenariosAsync(projectName);

        foreach (var scenario in scenarios)
            AssertNoAdjacentDuplicates(projectName, scenario);
    }

    [Theory]
    [MemberData(nameof(ReqNRollProjects))]
    public async Task Plain_cake_scenarios_record_exactly_the_feature_file_steps(string projectName)
    {
        var scenarios = await RunAndParseScenariosAsync(projectName);

        var happyPath = Assert.Single(scenarios, s => s.Name == "Calling Create Cake Endpoint Successfully");
        Assert.Equal(
            [
                "Given a valid post request for the Cake endpoint",
                "When the request is sent to the cake post endpoint",
                "Then the response should be successful"
            ],
            happyPath.Steps);

        var missingEggs = Assert.Single(scenarios, s => s.Name == "Calling Create Cake Endpoint Without Eggs");
        Assert.Equal(
            [
                "Given a valid post request for the Cake endpoint",
                "But the request body is missing eggs",
                "When the request is sent to the cake post endpoint",
                "Then the response http status should be bad request"
            ],
            missingEggs.Steps);
    }

    // The CI "Integration (ReqNRoll)" filter matches the project-name theory argument
    // (FullyQualifiedName~Component.ReqNRoll.xUnit3), so even single-project tests take
    // the project as a theory parameter.
    [Theory]
    [MemberData(nameof(ReqNRollProjects))]
    public async Task A_Background_is_what_the_feature_file_writes_and_a_step_a_step_ran_is_no_row(string projectName)
    {
        var scenarios = await RunAndParseScenariosAsync(projectName);

        var own = Assert.Single(scenarios, s => s.Name == "A scenario's own steps come after its Background");
        Assert.Equal(["Given a step that passes", "And a step runs a step with a table and a doc string"], own.BackgroundSteps);
        Assert.Equal(["When another step that passes", "Then a third step that passes"], own.Steps);

        var inARule = Assert.Single(scenarios, s => s.Name == "A scenario in a rule runs both Backgrounds");
        Assert.Equal(["Given a step that passes", "And a step runs a step with a table and a doc string", "Given a rule's step that passes"], inARule.BackgroundSteps);
        Assert.Equal(["When another step that passes"], inARule.Steps);
    }

    [Theory]
    [MemberData(nameof(ReqNRollProjects))]
    public async Task A_step_a_step_ran_keeps_its_doc_string(string projectName)
    {
        var result = await TestProjectRunner.RunAsync(projectName);
        Assert.True(result.Success, $"{projectName} failed:\n{result.StandardError}\n{result.StandardOutput}");

        var html = Kronikol.Reports.HtmlReportFile.ToPlain(await File.ReadAllTextAsync(ReportParser.GetReportFiles(result.ReportsFolderPath).TestRunReportHtml!));

        Assert.Contains("<pre class=\"step-docstring\"><code>a doc string</code></pre>", html);
    }

    [Theory]
    [MemberData(nameof(ReqNRollProjects))]
    public async Task A_failure_is_read_on_the_step_that_failed(string projectName)
    {
        var result = await TestProjectRunner.RunAsync(projectName, new Dictionary<string, string> { ["KRONIKOL_EXAMPLE_STEP_FAILURES"] = "true", ["GITHUB_STEP_SUMMARY"] = "" });
        Assert.False(result.Success, $"{projectName} was asked to fail and did not:\n{result.StandardOutput}");

        var failing = (await File.ReadAllLinesAsync(Path.Combine(result.ReportsFolderPath, "Failures.jsonl")))
            .Select(line => JsonDocument.Parse(line).RootElement)
            .Where(row => row.GetProperty("kind").GetString() == "failure")
            .ToDictionary(
                row => row.GetProperty("scenario").GetString()!,
                row => row.GetProperty("failingSteps").EnumerateArray().Select(step => step.GetProperty("text").GetString()!).ToArray());

        Assert.Equal(9, failing.Count);
        Assert.Empty(failing["An AfterScenario hook fails"]);
        Assert.Equal(["Given a Background step that fails when asked"], failing["The Background step is the one that failed"]);
        Assert.Equal(["Given a step runs a step that fails when asked", "Given an inner step that fails when asked"], failing["An inner step fails"]);
        Assert.Contains("Given a step runs a step whose argument does not convert when asked", failing["An inner argument does not convert"]);
        Assert.Equal(["And a step a hook stops when asked"], failing["A BeforeStep hook ahead of the adapter's fails"]);
        Assert.Equal(["Given a step runs a step a hook stops when asked", "Given a step a hook stops when asked"], failing["A BeforeStep hook ahead of the adapter's fails on an inner step"]);
        Assert.Equal(["Given a step runs a step an AfterStep hook fails when asked", "Given a step with a doc string an AfterStep hook fails when asked"], failing["An AfterStep hook fails on an inner step"]);
        Assert.Equal(["When a step an AfterStep hook fails when asked"], failing["An AfterStep hook fails on a scenario's own step"]);

        Assert.Equal(["Given a step an AfterStep hook fails when asked"], failing["An AfterScenario hook runs a step an AfterStep hook fails"]);

        // The hook stopped ours from closing that inner step, and it is still described.
        var html = Kronikol.Reports.HtmlReportFile.ToPlain(await File.ReadAllTextAsync(ReportParser.GetReportFiles(result.ReportsFolderPath).TestRunReportHtml!));
        Assert.Contains("<pre class=\"step-docstring\"><code>its doc string</code></pre>", html);
    }

    public static TheoryData<string> XUnit3Only() => new() { TestProjects.ReqNRollXUnit3 };

    [Theory]
    [MemberData(nameof(XUnit3Only))]
    public async Task Background_scenarios_record_exactly_the_feature_file_steps(string projectName)
    {
        // The Cake Quality feature writes one Background step: doubled steps would miscount it
        // and would push the scenario's own steps out of Steps.
        var scenarios = await RunAndParseScenariosAsync(projectName);

        var milk = Assert.Single(scenarios, s => s.Name == "The baked cake contains the requested milk");
        Assert.Equal(["Given a valid post request for the Cake endpoint"], milk.BackgroundSteps);
        Assert.Equal(
            [
                "When the request is sent to the cake post endpoint",
                "Then the cake should contain the requested milk"
            ],
            milk.Steps);

        var flour = Assert.Single(scenarios, s => s.Name == "The baked cake contains the requested flour");
        Assert.Equal(["Given a valid post request for the Cake endpoint"], flour.BackgroundSteps);
        Assert.Equal(
            [
                "When the request is sent to the cake post endpoint",
                "Then the cake should contain the requested flour"
            ],
            flour.Steps);
    }

    [Theory]
    [MemberData(nameof(XUnit3Only))]
    public async Task Outline_example_records_exactly_the_feature_file_steps(string projectName)
    {
        // Scenario Outline example rows feed ExampleValueGrouper.BuildStructured, so
        // doubled steps can distort outline grouping — pin one stable example row.
        var scenarios = await RunAndParseScenariosAsync(projectName);

        var classic = Assert.Single(
            scenarios,
            s => s.Name == "Different muffin recipes should produce the expected batch"
                 && s.Steps.Any(step => step.Contains("\"Classic\"")));

        Assert.Equal(
            [
                "Given a muffin recipe \"Classic\" with the following ingredients:",
                "And the following baking:",
                "And the following muffin toppings:",
                "When the muffins are prepared",
                "Then the muffin batch should have 5 ingredients",
                "And the muffin response should include 2 toppings",
                "And the muffin response should have baking info True"
            ],
            classic.Steps);
    }
}
