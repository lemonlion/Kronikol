using System.Net;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The Components tab's diagram zoomed: the filters draw it again with a new picture, and it keeps its zoom, the level
/// and its place across the picture (5.2.5: 5.2.3 kept the level, but the swap took the scroll, and the diagram came back
/// at its left edge).
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class ComponentDiagramZoomTests : PlaywrightTestBase
{
    public ComponentDiagramZoomTests(PlaywrightFixture fixture) : base(fixture) { }

    /// <summary>
    /// A run report written by the whole pipeline: Pays (passed) calls along a chain of ten services from the shop, and
    /// Ships (failed) along the first nine and then Orders, so the diagram drawn left to right is wider than the page with
    /// every scenario and with the failed one alone.
    /// </summary>
    private string Write(string fileName)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var reportsDir = Path.Combine(TempDir, "components-zoom-" + tag);
        Directory.CreateDirectory(reportsDir);
        var (pays, ships) = ("cz-pays-" + tag, "cz-ships-" + tag);
        var shop = "Shop" + tag;
        var chain = Enumerable.Range(1, 10).Select(i => $"Service{i}x{tag}").ToArray();
        var orders = "Orders" + tag;
        var names = new HashSet<string>(chain) { shop, orders };
        lock (ReportTestHelper.WholePipeline)
        {
            DefaultDiagramsFetcher.Reset();
            var caller = shop;
            for (var i = 0; i < chain.Length; i++)
            {
                RequestResponseLogger.LogPair("Pays", pays, HttpMethod.Post, new Uri($"http://service{i + 1}/pay"), chain[i], caller, statusCode: HttpStatusCode.OK);
                if (i < 9)
                    RequestResponseLogger.LogPair("Ships", ships, HttpMethod.Post, new Uri($"http://service{i + 1}/ship"), chain[i], caller, statusCode: HttpStatusCode.OK);
                caller = chain[i];
            }
            RequestResponseLogger.LogPair("Ships", ships, HttpMethod.Post, new Uri("http://orders/orders"), orders, chain[8], statusCode: HttpStatusCode.Created);
            ReportGenerator.CreateStandardReportsWithDiagrams(
                [new Feature
                {
                    DisplayName = "Shop",
                    Scenarios =
                    [
                        new Scenario { Id = pays, DisplayName = "Pays", Result = ExecutionResult.Passed },
                        new Scenario { Id = ships, DisplayName = "Ships", Result = ExecutionResult.Failed }
                    ]
                }],
                DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow,
                new ReportConfigurationOptions
                {
                    ReportsFolderPath = reportsDir,
                    ComponentDiagramOptions = new Kronikol.ComponentDiagram.ComponentDiagramOptions { ParticipantFilter = names.Contains },
                    GenerateSpecificationsReport = false,
                    GenerateSpecificationsData = false,
                });
            DefaultDiagramsFetcher.Reset();
        }
        var report = Path.Combine(reportsDir, "TestRunReport.html");
        File.Copy(report, Path.Combine(OutputDir, fileName), true);
        return new Uri(report).AbsoluteUri;
    }

    [Fact]
    public async Task The_filters_drawing_a_zoomed_component_diagram_again_keep_its_level_and_its_place()
    {
        await Page.GotoReportAsync(Write("ComponentsZoomKept.html"));
        await Page.Locator("details.feature").First.WaitForAsync();
        await OpenReportTabAsync("components");
        await Page.WaitForFunctionAsync(
            "() => !!document.querySelector('#component-diagram .plantuml-browser > svg') && !!document.querySelector('#component-diagram .plantuml-browser > .diagram-zoom-controls')",
            null, new() { Timeout = 60000, PollingInterval = 200 });
        var level = await Page.EvaluateAsync<int>("""
            () => {
                var c = document.querySelector('#component-diagram .plantuml-browser');
                var s = c.querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
                s.value = String(Math.round((parseInt(s.min) + 100) / 2));
                s.dispatchEvent(new Event('input', { bubbles: true }));
                c.scrollLeft = 150;
                return parseInt(s.value);
            }
            """);
        Assert.InRange(level, 2, 99);
        await TwoAnimationFramesAsync(Page);

        // only the failed scenario: the diagram is drawn again without the tenth service
        await Page.EvaluateAsync("() => { window.__before = document.querySelector('#component-diagram .plantuml-browser > svg'); }");
        await OpenFilterRowsAsync();
        await Page.Locator(".status-toggle[data-status='Failed']").ClickAsync();
        await Page.WaitForFunctionAsync("""
            () => {
                var c = document.querySelector('#component-diagram .plantuml-browser');
                return !document.contains(window.__before) && c.querySelector(':scope > svg') && c.querySelector(':scope > .diagram-zoom-controls');
            }
            """, null, new() { Timeout = 60000, PollingInterval = 200 });
        await TwoAnimationFramesAsync(Page);

        var after = await Page.EvaluateAsync<double[]>("""
            () => {
                var c = document.querySelector('#component-diagram .plantuml-browser');
                var s = c.querySelector(':scope > .diagram-zoom-controls .diagram-zoom-slider');
                return [c.classList.contains('diagram-natural-size') ? 1 : 0, parseInt(s.value), c.scrollLeft];
            }
            """);
        Assert.Equal(1, after[0]);
        Assert.Equal(level, (int)after[1]);
        Assert.Equal(150, Math.Round(after[2]));
    }
}
