using System.Net;
using Kronikol.Parties;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// R1 of plans/PARTICIPANT_ORDER_PLAN.md (#228): in a run that declares the app under test, the drawn diagram puts the
/// app right after the test, left of the stores the test set up first; a run that declares nothing draws them as
/// before, the stores first. Read from where the default renderer draws each name.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class ParticipantOrderTests : DiagramNotePlaywrightBase
{
    public ParticipantOrderTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task<Dictionary<string, double>> DrawnLeftEdges(bool declareApp)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (caller, disk, git, app) = ("Caller" + tag, "Disk" + tag, "Git" + tag, "App" + tag);
        var testId = "participant-order-" + tag;
        var reportsDir = Path.Combine(TempDir, testId);
        Directory.CreateDirectory(reportsDir);
        lock (ReportTestHelper.WholePipeline)
        {
            DefaultDiagramsFetcher.Reset();
            // The issue's scenario: the test sets up the disk and git, then runs the app, which uses both.
            RequestResponseLogger.LogPair("Run", testId, "POST", new Uri("http://disk/work"), disk, caller, dependencyCategory: "Database");
            RequestResponseLogger.LogPair("Run", testId, "POST", new Uri("http://git/repo"), git, caller, dependencyCategory: "Database");
            RequestResponseLogger.LogPair("Run", testId, HttpMethod.Post, new Uri("http://app/run"), app, caller, statusCode: HttpStatusCode.OK);
            RequestResponseLogger.LogPair("Run", testId, "GET", new Uri("http://git/repo"), git, app, dependencyCategory: "Database");
            RequestResponseLogger.LogPair("Run", testId, "PUT", new Uri("http://disk/work/out"), disk, app, dependencyCategory: "Database");
            ReportGenerator.CreateStandardReportsWithDiagrams(
                [new Feature { DisplayName = "App " + tag, Scenarios = [new Scenario { Id = testId, DisplayName = "Runs the app", Result = ExecutionResult.Passed }] }],
                DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow,
                new ReportConfigurationOptions
                {
                    ReportsFolderPath = reportsDir,
                    GenerateSpecificationsReport = false,
                    GenerateSpecificationsData = false,
                    Parties = declareApp ? [new PartyDescriptor(app) { Kind = PartyKind.UnderTest }] : []
                });
            DefaultDiagramsFetcher.Reset();
        }
        var report = Path.Combine(reportsDir, "TestRunReport.html");
        File.Copy(report, Path.Combine(OutputDir, $"ParticipantOrder{(declareApp ? "Declared" : "Undeclared")}.html"), true);
        await Page.GotoReportAsync(new Uri(report).AbsoluteUri);
        await Page.Locator("details.feature").First.WaitForAsync();

        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        // Each name's leftmost drawn text, once every one of them is drawn.
        var edges = await Page.WaitForFunctionAsync("""
            ([names]) => {
                const svg = document.querySelector("[data-diagram-type='plantuml'] svg");
                if (!svg) return null;
                const left = {};
                for (const t of svg.querySelectorAll('text')) {
                    const name = (t.textContent || '').trim();
                    if (names.includes(name)) left[name] = Math.min(left[name] ?? Infinity, t.getBBox().x);
                }
                return names.every(n => n in left) ? names.map(n => left[n]) : null;
            }
            """, new object[] { new[] { caller, disk, git, app } }, new() { Timeout = 60000, PollingInterval = 200 });
        // An array in the names' order: the JSON a handle gives back camel-cases an object's keys.
        var drawn = await edges.JsonValueAsync<double[]>();
        return new Dictionary<string, double> { ["caller"] = drawn[0], ["disk"] = drawn[1], ["git"] = drawn[2], ["app"] = drawn[3] };
    }

    [Fact]
    public async Task A_run_that_declares_the_app_under_test_draws_it_right_after_the_test()
    {
        var left = await DrawnLeftEdges(declareApp: true);

        Assert.True(left["caller"] < left["app"], $"caller {left["caller"]}, app {left["app"]}");
        Assert.True(left["app"] < left["disk"], $"app {left["app"]}, disk {left["disk"]}");
        Assert.True(left["app"] < left["git"], $"app {left["app"]}, git {left["git"]}");
    }

    [Fact]
    public async Task A_run_that_declares_nothing_draws_the_stores_the_test_set_up_first_as_before()
    {
        var left = await DrawnLeftEdges(declareApp: false);

        Assert.True(left["disk"] < left["app"], $"disk {left["disk"]}, app {left["app"]}");
        Assert.True(left["git"] < left["app"], $"git {left["git"]}, app {left["app"]}");
    }
}
