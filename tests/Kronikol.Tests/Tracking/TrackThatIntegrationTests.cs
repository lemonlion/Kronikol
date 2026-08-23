using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

[Collection("DiagramsFetcher")]
public class TrackThatIntegrationTests : IDisposable
{
    private readonly string _reportDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Reports");

    public void Dispose()
    {
        foreach (var file in Directory.GetFiles(_reportDir, "TrackThat*"))
            File.Delete(file);
    }

    private static Feature[] MakeFeatures(string scenarioId = "assertion-test") =>
    [
        new Feature
        {
            DisplayName = "Assertion Feature",
            Scenarios =
            [
                new Scenario
                {
                    Id = scenarioId,
                    DisplayName = "Verifies response",
                    IsHappyPath = true,
                    Result = ExecutionResult.Passed
                }
            ]
        }
    ];

    [Fact]
    public void Report_contains_assertionNote_in_PlantUML_source_when_Track_That_used()
    {
        const string testId = "assertion-test";

        // Simulate a tracked HTTP call + assertion
        using (TestIdentityScope.Begin("Verifies response", testId))
        {
            // Log a normal request/response pair so we have a diagram
            var requestLog = new RequestResponseLog(
                "Verifies response", testId, HttpMethod.Get, "{}",
                new Uri("http://api.example.com/orders"),
                [], "OrderService", "Caller",
                RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false);
            RequestResponseLogger.Log(requestLog);

            // Now track an assertion
            Track.That(() => { });
        }

        var trackedLogs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId).ToArray();
        var diagrams = new[]
        {
            new DefaultDiagramsFetcher.DiagramAsCode(testId, "",
                "@startuml\nCaller -> OrderService: GET /orders\n@enduml\n")
        };

        var html = ReportGenerator.GenerateHtmlReport(
            diagrams, MakeFeatures(),
            DateTime.UtcNow, DateTime.UtcNow,
            null, "TrackThatAssertion.html", "Test", true,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            trackedLogs: trackedLogs);

        var content = File.ReadAllText(html);
        Assert.Contains("data-toggle=\"assertions\"", content);
        Assert.Contains("Assertions Hidden", content);
    }

    [Fact]
    public void Report_does_not_contain_assertions_radio_when_no_assertions_tracked()
    {
        const string testId = "no-assertions";

        // Just log a normal request, no assertions
        using (TestIdentityScope.Begin("Normal test", testId))
        {
            var requestLog = new RequestResponseLog(
                "Normal test", testId, HttpMethod.Get, "{}",
                new Uri("http://api.example.com/orders"),
                [], "OrderService", "Caller",
                RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false);
            RequestResponseLogger.Log(requestLog);
        }

        var trackedLogs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId).ToArray();
        var diagrams = new[]
        {
            new DefaultDiagramsFetcher.DiagramAsCode(testId, "",
                "@startuml\nCaller -> OrderService: GET /orders\n@enduml\n")
        };

        var html = ReportGenerator.GenerateHtmlReport(
            diagrams, MakeFeatures(testId),
            DateTime.UtcNow, DateTime.UtcNow,
            null, "TrackThatNoAssertions.html", "Test", true,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            trackedLogs: trackedLogs);

        var content = File.ReadAllText(html);
        // The button markup should not be present. The JS and CSS that drive it always are, and the
        // scripts select on this very attribute, so the search has to skip them.
        Assert.Empty(Reports.ReportMarkup.Elements(content, "button", "data-toggle=\"assertions\""));
    }

    [Fact]
    public void Report_contains_stripAssertionNotes_function()
    {
        const string testId = "strip-test";

        using (TestIdentityScope.Begin("Strip test", testId))
        {
            Track.That(() => { });
        }

        var trackedLogs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId).ToArray();
        var diagrams = new[]
        {
            new DefaultDiagramsFetcher.DiagramAsCode(testId, "",
                "@startuml\nCaller -> OrderService: GET\n@enduml\n")
        };

        var html = ReportGenerator.GenerateHtmlReport(
            diagrams, MakeFeatures(testId),
            DateTime.UtcNow, DateTime.UtcNow,
            null, "TrackThatStrip.html", "Test", true,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            trackedLogs: trackedLogs);

        var content = File.ReadAllText(html);
        Assert.Contains("stripAssertionNotes", content);
        Assert.Contains("_assertionsVisible", content);
    }

    [Fact]
    public void PlantUML_source_includes_assertionNote_style_when_assertions_present()
    {
        const string testId = "style-test";

        using (TestIdentityScope.Begin("Style test", testId))
        {
            var requestLog = new RequestResponseLog(
                "Style test", testId, HttpMethod.Get, "{}",
                new Uri("http://api.example.com/check"),
                [], "CheckService", "Caller",
                RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false);
            RequestResponseLogger.Log(requestLog);

            Track.That(() => { });
        }

        var trackedLogs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId).ToArray();

        // Verify that assertion logs have the assertionNote stereotype
        var assertionLogs = trackedLogs.Where(l => l.PlantUml != null && l.PlantUml.Contains("<<assertionNote>>")).ToArray();
        Assert.NotEmpty(assertionLogs);
        Assert.Contains("#d4edda", assertionLogs[0].PlantUml!);
    }
}
