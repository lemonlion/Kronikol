using System.Net;
using Kronikol.PlantUml;
using Kronikol.Reports;
using Kronikol.Tracking;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Reports for <c>plans/LONG_NOTE_SCROLL_PLAN.md</c>, written by the real generator from captured calls as a BrowserJs run
/// writes them (the page splits, the system-ui look). A source written by hand would miss the generator's prefix
/// (<c>skinparam wrapWidth</c>, the teoz pragma, the style block), and that prefix decides how a note paints: under
/// <c>wrapWidth</c> the engine draws every word as its own <c>&lt;text&gt;</c>.
/// </summary>
internal static class LongPayloadReports
{
    private static readonly DateTimeOffset At = new(2026, 10, 21, 10, 0, 0, TimeSpan.Zero);

    private static readonly (string Key, string? Value)[] JsonHeaders = [("Content-Type", "application/json")];

    /// <summary>One GET call, its response carrying <paramref name="responseBody"/>.</summary>
    internal static RequestResponseLog[] Call(string testId, int index, string? responseBody, string service = "Orders API")
    {
        var pairId = Guid.NewGuid();
        var trace = Guid.NewGuid();
        var uri = new Uri($"http://orders.internal/api/items?page={index}");
        var request = new RequestResponseLog(testId, testId, HttpMethod.Get, null, uri, JsonHeaders, service, "Caller",
            RequestResponseType.Request, trace, pairId, false) { Timestamp = At.AddSeconds(index) };
        var response = new RequestResponseLog(testId, testId, HttpMethod.Get, responseBody, uri, JsonHeaders, service, "Caller",
            RequestResponseType.Response, trace, pairId, false, HttpStatusCode.OK) { Timestamp = At.AddSeconds(index).AddMilliseconds(40) };
        return [request, response];
    }

    /// <summary>
    /// Writes a report holding one scenario per entry, each drawn from its calls by the generator, and returns its URL.
    /// </summary>
    internal static string Write(string tempDir, string outputDir, string fileName,
        IReadOnlyList<(string Id, string Name, RequestResponseLog[] Logs)> scenarios, ResolvedToggleDefaults? toggleDefaults = null)
    {
        var diagrams = new List<DiagramAsCode>();
        foreach (var scenario in scenarios)
        {
            var perTest = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(scenario.Logs, maxEncodedDiagramLength: 8000,
                clientSideSplitting: true, systemUiFont: true);
            foreach (var plantUml in perTest.SelectMany(t => t.PlantUmls))
                diagrams.Add(new DiagramAsCode(scenario.Id, "", plantUml.PlainText));
        }

        var features = new[]
        {
            new Feature
            {
                DisplayName = "Long payloads",
                Scenarios = scenarios.Select(s => new Scenario
                {
                    Id = s.Id, DisplayName = s.Name, IsHappyPath = true, Result = ExecutionResult.Passed,
                    Duration = TimeSpan.FromSeconds(1),
                    Steps = [new ScenarioStep { Keyword = "When", Text = "the items are listed", Status = ExecutionResult.Passed }]
                }).ToArray()
            }
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams.ToArray(), features,
            At.UtcDateTime, At.UtcDateTime.AddSeconds(30),
            null, Path.Combine(tempDir, fileName), "Test Run Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            toggleDefaults: toggleDefaults);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>One SQL call whose query is <paramref name="query"/>, as a ClickHouse client's capture draws it.</summary>
    private static RequestResponseLog[] SqlCall(string testId, int index, string query)
    {
        var trace = Guid.NewGuid();
        var pairId = Guid.NewGuid();
        var uri = new Uri("clickhouse://insights/location_performance_weekly");
        return
        [
            new(testId, testId, "SELECT", query, uri, [], "ClickHouse", "DataInsights", RequestResponseType.Request, trace, pairId,
                TrackingIgnore: false, DependencyCategory: Kronikol.Constants.DependencyCategories.SQL) { Timestamp = At.AddSeconds(index) },
            new(testId, testId, "SELECT", "[{\"period\":\"2026-09-07\"}]", uri, [], "ClickHouse", "DataInsights", RequestResponseType.Response,
                trace, pairId, TrackingIgnore: false, DependencyCategory: Kronikol.Constants.DependencyCategories.SQL)
                { Timestamp = At.AddSeconds(index).AddMilliseconds(25) },
        ];
    }

    /// <summary>
    /// One scenario of <paramref name="calls"/> SQL calls whose queries are one long line each, which the note width wraps:
    /// with 80 calls the page draws the diagram in parts (taller than <c>BrowserFragmentMaxHeight</c>), with 4 whole. The
    /// first <paramref name="shortFirst"/> queries are short, so a first part drawn from them is narrower than the rest
    /// (plan F3).
    /// </summary>
    internal static string WideQueries(string tempDir, string outputDir, string fileName, int calls, NoteWidthMode noteWidth,
        int shortFirst = 0)
    {
        var query = "SELECT " + string.Join(", ", Enumerable.Range(0, 70).Select(i => $"metric_column_{i:D2}")) + " FROM sales_weekly";
        var logs = Enumerable.Range(0, calls)
            .SelectMany(i => SqlCall("wide-queries", i, i < shortFirst ? "SELECT count() FROM sales_weekly" : query)).ToArray();
        return Write(tempDir, outputDir, fileName, [("wide-queries", $"{calls} wide queries", logs)],
            ResolvedToggleDefaults.BuiltIn with { NoteWidth = noteWidth });
    }

    /// <summary>
    /// One call whose JSON response holds a string with fifteen lines: four lines as JSON and eighteen in the YAML view the
    /// report starts in (the string becomes a block, a line each), so the body is short as JSON and long as YAML at the
    /// default 10-line limit (plan F1).
    /// </summary>
    internal static string YamlLongerThanJson(string tempDir, string outputDir, string fileName, int scenarios = 1)
    {
        var query = "SELECT\n" + string.Join("\n", Enumerable.Range(1, 14).Select(i => $"  column_{i:D2}")) + "\nFROM sales";
        var body = System.Text.Json.JsonSerializer.Serialize(new { query, rows = 3 });
        return Write(tempDir, outputDir, fileName, Enumerable.Range(1, scenarios)
            .Select(n => ($"yaml-longer-{n}", $"A string with line breaks {n}", Call($"yaml-longer-{n}", 0, body))).ToList());
    }
}
