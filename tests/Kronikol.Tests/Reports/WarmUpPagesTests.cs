using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// A run draws its warm-up marks on every page that draws durations (plans/WARM_UP_PLAN.md 4.5, R2):
/// <c>Specifications.html</c> draws its badges, filter and timeline from the same code as <c>TestRunReport.html</c>, and
/// is handed the same reading.
/// </summary>
// Runs the report pipeline, whose diagram fetcher and log store are process-wide.
[Collection("DiagramsFetcher")]
public class WarmUpPagesTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-warm-up-pages").FullName;

    // Unique to this instance, so no other test's calls are this run's.
    private readonly string _id = Guid.NewGuid().ToString("N")[..8];

    public WarmUpPagesTests() => DefaultDiagramsFetcher.Reset();

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void The_report_and_the_specifications_draw_the_same_marks()
    {
        Log(Id("first"), 0, 600);
        Log(Id("later"), 1000, 5);
        Log(Id("later"), 1100, 5);
        var options = new ReportConfigurationOptions
        {
            ReportsFolderPath = _directory,
            GenerateSpecificationsReport = true,
            PlantUmlRendering = PlantUmlRendering.BrowserJs,
            WriteRunSummaryToConsole = false,
        };

        ReportGenerator.CreateStandardReportsWithDiagramsInEnvironment(Features(), T0.UtcDateTime, T0.UtcDateTime.AddMinutes(1), options,
            RunEnvironment.Unrecorded, Environment.GetEnvironmentVariable);

        foreach (var page in new[] { "TestRunReport.html", "Specifications.html" })
        {
            var document = Parse(File.ReadAllText(Path.Combine(_directory, page)));
            var marked = document.QuerySelectorAll("details.scenario[data-warmup-ms]").ToList();
            var scenario = Assert.Single(marked);
            Assert.Equal("600", scenario.GetAttribute("data-warmup-ms"));
            Assert.Equal(" · 600ms warm-up", scenario.QuerySelector(":scope > summary .duration-badge > .warm-up-note")!.TextContent);
            Assert.Equal("width:30.0%", document.QuerySelector("#scenario-timeline .timeline-warm-up")!.GetAttribute("style"));
        }
    }

    private string Id(string name) => $"{name}-{_id}";

    private Feature[] Features() =>
    [
        new Feature
        {
            DisplayName = "Orders",
            Scenarios =
            [
                new Scenario { Id = Id("first"), DisplayName = "Place an order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(2) },
                new Scenario { Id = Id("later"), DisplayName = "Place two orders", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(2) }
            ]
        }
    ];

    private static void Log(string testId, double startMs, double durationMs)
    {
        var (trace, id) = (Guid.NewGuid(), Guid.NewGuid());
        var uri = new Uri("http://orders/orders");
        RequestResponseLogger.Log(new RequestResponseLog(testId, testId, HttpMethod.Post, "{}", uri, [], "orders", "Test", RequestResponseType.Request, trace, id, false)
            { Timestamp = T0.AddMilliseconds(startMs), AttributionSource = AttributionSource.TestContext });
        RequestResponseLogger.Log(new RequestResponseLog(testId, testId, HttpMethod.Post, "{}", uri, [], "orders", "Test", RequestResponseType.Response, trace, id, false, HttpStatusCode.OK)
            { Timestamp = T0.AddMilliseconds(startMs + durationMs), AttributionSource = AttributionSource.TestContext });
    }

    private static IDocument Parse(string html) =>
        BrowsingContext.New(Configuration.Default).OpenAsync(req => req.Content(html)).GetAwaiter().GetResult();
}
