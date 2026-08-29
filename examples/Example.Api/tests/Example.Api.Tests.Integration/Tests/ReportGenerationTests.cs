using Example.Api.Tests.Integration.Helpers;

namespace Example.Api.Tests.Integration.Tests;

[Collection("SequentialTests")]
public class ReportGenerationTests
{
    public static TheoryData<string> AllProjects()
    {
        var data = new TheoryData<string>();
        foreach (var project in TestProjects.All)
            data.Add(project);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllProjects))]
    public async Task Project_generates_all_report_files(string projectName)
    {
        var result = await TestProjectRunner.RunAsync(projectName, new Dictionary<string, string>
        {
            ["KRONIKOL_SEPARATE_SETUP"] = "true",
            ["KRONIKOL_SPECIFICATIONS_TITLE"] = "Dessert Provider Specifications"
        });

        Assert.True(result.Success, $"{projectName} failed:\n{result.StandardError}\n{result.StandardOutput}");

        var reports = ReportParser.GetReportFiles(result.ReportsFolderPath);

        Assert.NotNull(reports.SpecificationsHtml);
        Assert.NotNull(reports.TestRunReportHtml);
        Assert.NotNull(reports.SpecificationsYaml);
    }

    [Theory]
    [MemberData(nameof(AllProjects))]
    public async Task Project_report_contains_expected_scenarios(string projectName)
    {
        var result = await TestProjectRunner.RunAsync(projectName, new Dictionary<string, string>
        {
            ["KRONIKOL_SEPARATE_SETUP"] = "true",
            ["KRONIKOL_SPECIFICATIONS_TITLE"] = "Dessert Provider Specifications"
        });

        Assert.True(result.Success, $"{projectName} failed:\n{result.StandardError}");

        var reports = ReportParser.GetReportFiles(result.ReportsFolderPath);
        Assert.NotNull(reports.TestRunReportHtml);

        var scenarios = await ReportParser.ExtractScenariosAsync(reports.TestRunReportHtml);
        Assert.True(scenarios.Length >= 2, $"Expected at least 2 scenarios, got {scenarios.Length}");
        Assert.Contains(scenarios, s => s.IsHappyPath);
    }

    [Theory]
    [MemberData(nameof(AllProjects))]
    public async Task Project_plantuml_contains_expected_participants_and_arrows(string projectName)
    {
        var result = await TestProjectRunner.RunAsync(projectName, new Dictionary<string, string>
        {
            ["KRONIKOL_SEPARATE_SETUP"] = "true",
            ["KRONIKOL_SPECIFICATIONS_TITLE"] = "Dessert Provider Specifications"
        });

        Assert.True(result.Success, $"{projectName} failed:\n{result.StandardError}");

        var reports = ReportParser.GetReportFiles(result.ReportsFolderPath);
        Assert.NotNull(reports.TestRunReportHtml);

        var plantUmlSources = await ReportParser.ExtractPlantUmlSourcesAsync(reports.TestRunReportHtml);
        Assert.NotEmpty(plantUmlSources);

        // All diagrams with real traffic should contain the expected service participants.
        // Diagnostic scenarios that deliberately make no HTTP calls (the ReqNRoll.xUnit3
        // Muffins feature) render the "(no interactions)" placeholder instead.
        var diagramsWithTraffic = plantUmlSources
            .Where(p => !p.Contains("(no interactions)"))
            .ToArray();
        Assert.NotEmpty(diagramsWithTraffic);
        foreach (var puml in diagramsWithTraffic)
        {
            PlantUmlAssertions.AssertContainsParticipants(puml, "Dessert Provider");
        }

        // The happy path diagram involves the Cow Service (milk call). Search only the
        // traffic-bearing diagrams — a "(no interactions)" placeholder can still mention
        // the cow service in a step delimiter bar.
        var happyPathDiagram = diagramsWithTraffic.First(p => p.Contains("Cow Service", StringComparison.OrdinalIgnoreCase));
        PlantUmlAssertions.AssertContainsParticipants(happyPathDiagram, "Dessert Provider", "Cow Service");
        PlantUmlAssertions.AssertContainsSequenceArrow(happyPathDiagram, "dessertProvider", "cowService");
    }

    [Theory]
    [MemberData(nameof(AllProjects))]
    public async Task Project_yaml_spec_has_valid_structure(string projectName)
    {
        var result = await TestProjectRunner.RunAsync(projectName, new Dictionary<string, string>
        {
            ["KRONIKOL_SEPARATE_SETUP"] = "true",
            ["KRONIKOL_SPECIFICATIONS_TITLE"] = "Dessert Provider Specifications"
        });

        Assert.True(result.Success, $"{projectName} failed:\n{result.StandardError}");

        var reports = ReportParser.GetReportFiles(result.ReportsFolderPath);
        Assert.NotNull(reports.SpecificationsYaml);

        var yaml = await ReportParser.ReadYamlAsync(reports.SpecificationsYaml);
        Assert.Contains("Title: Dessert Provider Specifications", yaml);
        Assert.Contains("Features:", yaml);
        Assert.Contains("Scenarios:", yaml);
        Assert.Contains("IsHappyPath: true", yaml);
    }
}
