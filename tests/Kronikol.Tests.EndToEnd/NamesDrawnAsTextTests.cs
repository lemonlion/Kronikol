using Microsoft.Playwright;
using Kronikol.Reports;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A feature's name, a scenario's name and the report's title are text the page shows as written. Until 4.5.1
/// the headers and the title wrote them raw, so a scenario named "Place an order for &lt;item&gt;" showed
/// "Place an order for " and the browser built an <c>&lt;item&gt;</c> element out of the rest; a name from
/// <c>kronikol ingest</c> holding a tag drew the tag.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class NamesDrawnAsTextTests : PlaywrightTestBase
{
    public NamesDrawnAsTextTests(PlaywrightFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Names_with_markup_characters_are_shown_as_written()
    {
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Orders <v2> & co",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "names-1", DisplayName = "Place an order for <item>", Result = ExecutionResult.Passed,
                        Duration = TimeSpan.FromMilliseconds(5),
                    },
                    new Scenario
                    {
                        Id = "names-2", DisplayName = "Refund <img src=x onerror=\"window.__drawn=1\"> \"quoted\"",
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(6),
                    },
                ]
            }
        };
        var path = ReportGenerator.GenerateHtmlReport([], features, DateTime.UtcNow, DateTime.UtcNow, null,
            Path.Combine(TempDir, "NamesAsText.html"), "Shop <beta>", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(OutputDir, "NamesAsText.html"), true);

        await Page.GotoAsync(new Uri(path).AbsoluteUri);
        await Page.Locator("details.feature").First.WaitForAsync();
        await Page.EvaluateAsync("() => document.querySelectorAll('details.feature').forEach(d => d.setAttribute('open', ''))");

        Assert.Equal("Shop <beta>", await Page.TitleAsync());
        await Expect(Page.Locator("h1").First).ToHaveTextAsync("Shop <beta>");
        await Expect(Page.Locator("details.feature > summary.h2").First).ToContainTextAsync("Orders <v2> & co");
        await Expect(Page.Locator("details.scenario[data-stable-id] > summary.h3").First).ToContainTextAsync("Place an order for <item>");
        await Expect(Page.Locator("details.scenario[data-stable-id] > summary.h3").Nth(1))
            .ToContainTextAsync("Refund <img src=x onerror=\"window.__drawn=1\"> \"quoted\"");

        // Nothing the names hold became an element, or ran.
        Assert.Equal(0, await Page.EvaluateAsync<int>("() => document.querySelectorAll('item, v2, beta, summary img').length"));
        Assert.False(await Page.EvaluateAsync<bool>("() => window.__drawn === 1"));
    }
}
