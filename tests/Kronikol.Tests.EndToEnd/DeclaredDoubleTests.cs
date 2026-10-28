using System.Net;
using Kronikol.Parties;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A party the run declares a test double is drawn by its name alone (5.16.2, reversing #190 R1's Q11): neither its
/// scenario's sequence diagram nor the Components tab's drawing says «test double». What the party is stays in the data
/// file and in <c>kronikol query parties</c>.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class DeclaredDoubleTests : DiagramNotePlaywrightBase
{
    public DeclaredDoubleTests(PlaywrightFixture fixture) : base(fixture) { }

    [Fact]
    public async Task A_run_that_declares_a_double_draws_it_by_its_name_alone_in_its_scenario_and_in_its_components_tab()
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
        var scenario = await Page.Locator("[data-diagram-type='plantuml'] svg").First.TextContentAsync();
        Assert.Contains(stock, scenario);
        Assert.DoesNotContain("test double", scenario);

        await OpenReportTabAsync("components");
        await Page.WaitForFunctionAsync("""
            ([stock]) => {
                const svg = document.querySelector('#component-diagram .plantuml-browser > svg');
                return !!svg && (svg.textContent || '').includes(stock);
            }
            """, new object[] { stock }, new() { Timeout = 60000, PollingInterval = 200 });
        Assert.DoesNotContain("test double", await Page.Locator("#component-diagram .plantuml-browser > svg").TextContentAsync());
    }
}
