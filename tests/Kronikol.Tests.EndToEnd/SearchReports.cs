using Kronikol.Reports;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The reports the search's facts run on (plans/SEARCH_PLAN.md 6.1): a bakery whose scenarios carry tags, every status, a
/// scenario skipped after a failure, times, and diagrams naming services, so the builder has scenarios, features, tags,
/// dependencies, statuses and durations to suggest, and the Search tab has names, steps, diagrams and errors to match in.
/// </summary>
internal static class SearchReports
{
    private static readonly DateTime T0 = new(2026, 10, 26, 10, 0, 0, DateTimeKind.Utc);

    private const string CowSource = "@startuml\nactor \"Caller\" as caller\nparticipant \"Cow Service\" as cow\ncaller -> cow : GET: /api/milk\ncow --> caller : 200 OK\n@enduml";
    private const string SupplierSource = "@startuml\nactor \"Caller\" as caller\nparticipant \"Supplier Service\" as sup\ncaller -> sup : POST: /api/flour\n@enduml";

    private static Scenario Run(string id, string name, ExecutionResult result, int ms, string[]? tags = null, string? error = null, bool happy = false, string[]? steps = null) => new()
    {
        Id = id, DisplayName = name, Result = result, Duration = TimeSpan.FromMilliseconds(ms), Tags = tags, ErrorMessage = error, IsHappyPath = happy,
        Steps = steps?.Select(s => new ScenarioStep { Keyword = "Given", Text = s, Status = result == ExecutionResult.Failed ? ExecutionResult.Failed : ExecutionResult.Passed }).ToArray(),
    };

    public static Feature[] Bakery() =>
    [
        new Feature
        {
            DisplayName = "Pancakes Creation",
            Scenarios =
            [
                Run("p1", "A valid pancake request returns a batch", ExecutionResult.Passed, 120, ["smoke"], happy: true, steps: ["a pancake order for 3 people"]),
                Run("p2", "Pancakes without flour are rejected", ExecutionResult.Failed, 2400, ["regression"], error: "Expected status 400 but the bakery said 500", steps: ["an order without flour"]),
                Run("p3", "Pancake toppings are listed", ExecutionResult.Passed, 60, ["smoke", "menu"]),
            ]
        },
        new Feature
        {
            DisplayName = "Waffles",
            Scenarios =
            [
                Run("w1", "A waffle is made", ExecutionResult.Passed, 300, ["regression"]),
                Run("w2", "A waffle is served after the failure", ExecutionResult.SkippedAfterFailure, 0),
                Run("w3", "Waffle irons warm up", ExecutionResult.Skipped, 0),
            ]
        },
        new Feature
        {
            DisplayName = "Toast",
            Scenarios = Enumerable.Range(1, 12).Select(i => Run($"t{i}", $"Toast slice {i} is browned", ExecutionResult.Passed, 10 * i)).ToArray()
        },
    ];

    public static string Write(string tempDir, string outputDir, string fileName, Feature[]? features = null, bool includeTestRunData = true)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            [new DefaultDiagramsFetcher.DiagramAsCode("p1", "", CowSource), new DefaultDiagramsFetcher.DiagramAsCode("p2", "", SupplierSource),
             new DefaultDiagramsFetcher.DiagramAsCode("w1", "", CowSource)],
            features ?? Bakery(), T0, T0.AddMinutes(1), null, Path.Combine(tempDir, fileName), "Bakery", includeTestRunData,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }
}
