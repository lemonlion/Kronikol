using System.Net;
using System.Text.Json;
using Kronikol.PlantUml;
using Kronikol.Reports;
using Kronikol.Tracking;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Reports for the page's diagram API (<c>window.kronikol_diagrams</c>, plans/SEARCH_PLAN.md 4.6.5) and the defects found
/// beside it, written by the real generator from captured calls as a BrowserJs run writes them, so each diagram has the
/// generator's prefix, its notes, its header lines and its internal-flow links.
/// </summary>
internal static class DiagramApiReports
{
    private static readonly DateTimeOffset At = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    private static readonly (string Key, string? Value)[] JsonHeaders = [("Content-Type", "application/json")];

    /// <summary>A JSON object of <paramref name="lines"/> properties, each named after <paramref name="key"/>, one a line.</summary>
    internal static string Body(string key, int lines) =>
        JsonSerializer.Serialize(Enumerable.Range(1, lines).ToDictionary(i => $"{key}_{i:D2}", i => $"value {i} of {key}"),
            new JsonSerializerOptions { WriteIndented = true });

    /// <summary>
    /// One call and its response: <paramref name="method"/> of <paramref name="path"/> to <paramref name="service"/>,
    /// answered with <paramref name="body"/> and a JSON content type; to a database participant when
    /// <paramref name="database"/>, else of <paramref name="category"/> when given (a ServiceBus is drawn as a queue).
    /// </summary>
    internal static RequestResponseLog[] Call(string testId, int index, string method, string path, string? body,
        string service = "Orders API", bool database = false, string? category = null)
    {
        var pairId = Guid.NewGuid();
        var trace = Guid.NewGuid();
        var uri = new Uri((database ? "http://orders-db.internal" : "http://orders.internal") + path);
        category = database ? Kronikol.Constants.DependencyCategories.SQL : category;
        var request = new RequestResponseLog(testId, testId, new HttpMethod(method), null, uri, JsonHeaders, service, "Caller",
            RequestResponseType.Request, trace, pairId, false, DependencyCategory: category) { Timestamp = At.AddSeconds(index) };
        var response = new RequestResponseLog(testId, testId, new HttpMethod(method), body, uri, JsonHeaders, service, "Caller",
            RequestResponseType.Response, trace, pairId, false, HttpStatusCode.OK, DependencyCategory: category)
            { Timestamp = At.AddSeconds(index).AddMilliseconds(30) };
        return [request, response];
    }

    /// <summary>
    /// A marker the generator draws into a diagram at <paramref name="index"/>'s second: <paramref name="plantUml"/> (a
    /// step's bar, an assertion's note) at its start, on lines of its own as the tracking writes it, and its end.
    /// </summary>
    internal static RequestResponseLog[] Marker(string testId, int index, DiagramMarkerKind kind, string plantUml) =>
    [
        new("Probe", testId, "", "", new Uri("http://override.com"), [], "", "", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
        {
            IsOverrideStart = true, MarkerKind = kind, PlantUml = $"\n{plantUml}\n\n", Timestamp = At.AddSeconds(index),
        },
        new("Probe", testId, "", "", new Uri("http://override.com"), [], "", "", RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
        {
            IsOverrideEnd = true, MarkerKind = kind, Timestamp = At.AddSeconds(index).AddMilliseconds(1),
        },
    ];

    /// <summary>A scenario of the report: its id, its name and its calls; an outline's row when <c>Outline</c> names it.</summary>
    internal sealed record Scenario(string Id, string Name, RequestResponseLog[] Calls, string? Outline = null, string? Row = null);

    /// <summary>
    /// Writes a report of one feature holding <paramref name="scenarios"/>, each diagram drawn by the generator from its
    /// calls with internal-flow links on its labels, and returns its URL. Rows of one outline are grouped as the report
    /// groups them. <paramref name="partHeight"/> is the height past which the page draws a diagram in parts.
    /// <paramref name="rendering"/> is where the diagrams are drawn: in the page unless given (a server's draws an <c>img</c>).
    /// <paramref name="moreDiagrams"/> are drawn as further diagrams of the scenarios with their ids, after their first.
    /// </summary>
    internal static string Write(string tempDir, string outputDir, string fileName, IReadOnlyList<Scenario> scenarios,
        ResolvedToggleDefaults? toggleDefaults = null, int partHeight = Kronikol.Constants.TrackingDefaults.BrowserFragmentMaxHeight,
        PlantUmlRendering rendering = PlantUmlRendering.BrowserJs, IReadOnlyList<Scenario>? moreDiagrams = null)
    {
        var diagrams = new List<DiagramAsCode>();
        foreach (var scenario in scenarios.Concat(moreDiagrams ?? []).Where(s => s.Calls.Length > 0))
        {
            var perTest = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(scenario.Calls, maxEncodedDiagramLength: 8000,
                clientSideSplitting: true, systemUiFont: true, internalFlowTracking: true);
            foreach (var plantUml in perTest.SelectMany(t => t.PlantUmls))
                diagrams.Add(new DiagramAsCode(scenario.Id, "", plantUml.PlainText));
        }

        var features = new[]
        {
            new Feature
            {
                DisplayName = "Orders",
                Scenarios = scenarios.Select(s => new Kronikol.Reports.Scenario
                {
                    Id = s.Id, DisplayName = s.Name, IsHappyPath = true, Result = ExecutionResult.Passed,
                    Duration = TimeSpan.FromSeconds(1), OutlineId = s.Outline,
                    ExampleValues = s.Row is null ? null : new Dictionary<string, string> { ["tier"] = s.Row },
                    ExampleFlatValues = s.Row is null ? null : new Dictionary<string, string> { ["tier"] = s.Row },
                    Steps = [new ScenarioStep { Keyword = "When", Text = "the orders are read", Status = ExecutionResult.Passed }]
                }).ToArray()
            }
        };

        var path = ReportGenerator.GenerateHtmlReport(
            diagrams.ToArray(), features,
            At.UtcDateTime, At.UtcDateTime.AddSeconds(30),
            null, Path.Combine(tempDir, fileName), "Test Run Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: rendering,
            browserFragmentMaxHeight: partHeight,
            toggleDefaults: toggleDefaults);

        File.Copy(path, Path.Combine(outputDir, fileName), true);
        return new Uri(path).AbsoluteUri;
    }
}
