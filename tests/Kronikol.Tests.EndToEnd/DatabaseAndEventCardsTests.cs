using Kronikol.Extensions.Spanner;
using Kronikol.PlantUml;
using Kronikol.Reports;
using Kronikol.Tracking;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// #220, as a reader sees it (plans/CALLS_AND_EVENTS_PLAN.md 6.5). <c>Event</c> means the call moves a message, so a
/// database query's note is on the plain card and a publish's on the event card, in one diagram, written by the real
/// trackers and painted by the page. A decision pinned on the emitted source alone would not see the page's own role pass,
/// which paints the theme's <c>--dg-event</c> and not the <c>#cfecf7</c> the source holds.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class DatabaseAndEventCardsTests : DiagramNotePlaywrightBase
{
    public DatabaseAndEventCardsTests(PlaywrightFixture fixture) : base(fixture) { }

    private static string DiagramSource(string testId)
    {
        var fetcher = () => ("Place an order", testId);
        var spanner = new SpannerTracker(new SpannerTrackingOptions
        {
            ServiceName = "Spanner", CallerName = "Orders Api", Verbosity = SpannerTrackingVerbosity.Detailed, CurrentTestInfoFetcher = fetcher
        });
        var operation = new SpannerOperationInfo(SpannerOperation.Query, "Users");
        var (id, trace) = spanner.LogRequest(operation, "SELECT * FROM Users");
        spanner.LogResponse(operation, id, trace, "2 rows returned");

        new MessageTracker(new MessageTrackerOptions { CallerName = "Orders Api", CurrentTestInfoFetcher = fetcher })
            .TrackSendEvent("Publish", "Orders Topic", new Uri("event:///orders"), new { placed = "OrderPlaced" });

        var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId).ToArray();
        return PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs).Single().PlantUmls.First().PlainText;
    }

    private async Task OpenAsync(string fileName)
    {
        var testId = Guid.NewGuid().ToString();
        Feature[] features =
        [
            new()
            {
                DisplayName = "Orders",
                Scenarios = [new() { Id = testId, DisplayName = "Place an order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(10) }]
            }
        ];
        var path = ReportGenerator.GenerateHtmlReport([new DefaultDiagramsFetcher.DiagramAsCode(testId, "", DiagramSource(testId))],
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

    /// <summary>The painted fill of the card around a text, and the theme's two colours, as the browser resolves each.</summary>
    private Task<string[]> PaintAsync(string query, string publish) =>
        Page.Locator("[data-diagram-type='plantuml'] svg").First.EvaluateAsync<string[]>("""
            (el, words) => {
                const token = name => {
                    const p = document.createElement('div'); p.style.color = 'var(' + name + ')'; document.body.appendChild(p);
                    const c = getComputedStyle(p).color; p.remove(); return c;
                };
                // A note's line may be drawn in several pieces, and the arrow's label names the table too, so this reads the
                // first piece holding the word that sits inside a card.
                const cardAround = word => {
                    const cards = Array.from(el.querySelectorAll('.k-card'));
                    for (const text of el.querySelectorAll('text')) {
                        if (!text.textContent.includes(word)) continue;
                        const b = text.getBBox();
                        const card = cards.find(c => {
                            const r = c.getBBox();
                            return r.x <= b.x && r.y <= b.y && r.x + r.width >= b.x + b.width && r.y + r.height >= b.y + b.height;
                        });
                        if (card) return card;
                    }
                    return null;
                };
                const fill = card => card ? getComputedStyle(card).fill : 'none';
                return [fill(cardAround(words[0])), fill(cardAround(words[1])), token('--dg-note'), token('--dg-event')];
            }
            """, new[] { query, publish });

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task A_database_note_is_on_the_plain_card_and_a_publish_on_the_event_card(string scheme)
    {
        await Page.EmulateMediaAsync(new() { ColorScheme = scheme == "dark" ? ColorScheme.Dark : ColorScheme.Light });

        await OpenAsync($"DatabaseAndEventCards-{scheme}.html");
        Assert.Equal(scheme, await Page.EvaluateAsync<string?>("() => document.documentElement.getAttribute('data-theme')"));

        var paint = await PaintAsync("Users", "OrderPlaced");
        var (query, publish, note, card) = (paint[0], paint[1], paint[2], paint[3]);
        Assert.NotEqual(note, card); // what lets the two facts below tell the cards apart
        Assert.Equal(note, query);
        Assert.Equal(card, publish);
    }
}
