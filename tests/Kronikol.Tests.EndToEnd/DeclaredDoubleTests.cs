using System.Net;
using Kronikol.Parties;
using Kronikol.PlantUml;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// #190 R1, Q11: a party the run declares a test double says so on its participant, «test double» in the sequence
/// diagram and in the Components tab's drawing, and keeps its dependency colour. A party not declared one is drawn as
/// before.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class DeclaredDoubleTests : DiagramNotePlaywrightBase
{
    public DeclaredDoubleTests(PlaywrightFixture fixture) : base(fixture) { }

    private static string Source(bool declared)
    {
        var pairId = Guid.NewGuid();
        RequestResponseLog Log(RequestResponseType type) =>
            new("t1", "t1", "SELECT", null, new Uri("http://stock/items"), [], "Stock", "OrderService", type,
                Guid.NewGuid(), pairId, TrackingIgnore: false, DependencyCategory: "SqlServer");
        using var scope = DiagramParties.Declare(declared ? new HashSet<string>(StringComparer.Ordinal) { "Stock" } : null);
        return PlantUmlCreator.GetPlantUmlImageTagsPerTestId([Log(RequestResponseType.Request), Log(RequestResponseType.Response)], sequenceDiagramParticipantColors: true)
            .Single().PlantUmls.First().PlainText;
    }

    private async Task OpenAsync(string fileName, string source)
    {
        Feature[] features = [new() { DisplayName = "Orders", Scenarios = [new() { Id = "s1", DisplayName = "Count the stock", Result = ExecutionResult.Passed }] }];
        var path = ReportGenerator.GenerateHtmlReport([new DefaultDiagramsFetcher.DiagramAsCode("s1", "", source)],
            features, DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(TempDir, fileName), "Bakery", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(OutputDir, fileName), true);
        await Page.GotoReportAsync(new Uri(path).AbsoluteUri);
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await Page.WaitForFunctionAsync(
            "() => { const s = document.querySelector(\"[data-diagram-type='plantuml'] svg\"); return !!s && !!s.getAttribute('data-k-roles'); }",
            null, new() { Timeout = 15000, PollingInterval = 200 });
    }

    /// <summary>What the drawn sequence diagram says, and how many of its participant shapes are painted the database's colour.</summary>
    private Task<string[]> DrawnAsync() => Page.Locator("[data-diagram-type='plantuml'] svg").First.EvaluateAsync<string[]>("""
        el => [el.textContent || '', String([...el.querySelectorAll('.k-participant')].filter(e => getComputedStyle(e).fill === 'rgb(14, 124, 134)').length)]
        """);

    [Fact]
    public async Task A_declared_double_says_test_double_on_its_participant_and_keeps_its_colour()
    {
        var source = Source(declared: true);
        Assert.Contains("database \"Stock\" as stock <<test double>> #0E7C86", source);
        await OpenAsync("DeclaredDoubleSequence.html", source);

        var drawn = await DrawnAsync();

        Assert.Contains("test double", drawn[0]);
        Assert.True(int.Parse(drawn[1]) >= 2, $"participants painted in the database's colour: {drawn[1]}");
    }

    [Fact]
    public async Task A_party_not_declared_a_double_is_drawn_as_before()
    {
        await OpenAsync("UndeclaredDoubleSequence.html", Source(declared: false));

        var drawn = await DrawnAsync();

        Assert.DoesNotContain("test double", drawn[0]);
        Assert.True(int.Parse(drawn[1]) >= 2, $"participants painted in the database's colour: {drawn[1]}");
    }

    [Fact]
    public async Task A_run_that_declares_a_double_draws_it_so_in_its_scenario_and_in_its_components_tab()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (shop, stock) = ("Shop" + tag, "Stock" + tag);
        var reportsDir = Path.Combine(TempDir, "declared-double-" + tag);
        Directory.CreateDirectory(reportsDir);
        var names = new HashSet<string> { shop, stock };
        lock (ReportTestHelper.WholePipeline)
        {
            DefaultDiagramsFetcher.Reset();
            RequestResponseLogger.LogPair("Counts", "dd-counts-" + tag, HttpMethod.Get, new Uri("http://stock/stock"), stock, shop, statusCode: HttpStatusCode.OK);
            ReportGenerator.CreateStandardReportsWithDiagrams(
                [new Feature { DisplayName = "Shop", Scenarios = [new Scenario { Id = "dd-counts-" + tag, DisplayName = "Counts", Result = ExecutionResult.Passed }] }],
                DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow,
                new ReportConfigurationOptions
                {
                    ReportsFolderPath = reportsDir,
                    ComponentDiagramOptions = new Kronikol.ComponentDiagram.ComponentDiagramOptions { ParticipantFilter = names.Contains },
                    GenerateSpecificationsReport = false,
                    GenerateSpecificationsData = false,
                    Parties = [new PartyDescriptor(stock) { Kind = PartyKind.Double, Note = "WireMock stub" }]
                });
            DefaultDiagramsFetcher.Reset();
        }
        var report = Path.Combine(reportsDir, "TestRunReport.html");
        File.Copy(report, Path.Combine(OutputDir, "DeclaredDoubleRun.html"), true);
        await Page.GotoReportAsync(new Uri(report).AbsoluteUri);
        await Page.Locator("details.feature").First.WaitForAsync();

        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        Assert.Contains("test double", await Page.Locator("[data-diagram-type='plantuml'] svg").First.TextContentAsync());

        await OpenReportTabAsync("components");
        await Page.WaitForFunctionAsync("""
            ([stock]) => {
                const svg = document.querySelector('#component-diagram .plantuml-browser > svg');
                return !!svg && (svg.textContent || '').includes(stock) && (svg.textContent || '').includes('test double');
            }
            """, new object[] { stock }, new() { Timeout = 60000, PollingInterval = 200 });
    }
}
