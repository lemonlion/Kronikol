using Kronikol.PlantUml;
using Kronikol.Reports;
using Kronikol.Tracking;
using Microsoft.Playwright;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A failed answer's reason drawn in the note on its return (plans/FAILED_CALLS_PLAN.md R3, §6.6), on the page as it starts,
/// headers hidden: the engine draws it in firebrick and the page paints that from the theme in each theme, a success gets no
/// reason, and the note folds and unfolds as other notes do with the reason in view.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class FailedAnswerReasonTests : DiagramNotePlaywrightBase
{
    public FailedAnswerReasonTests(PlaywrightFixture fixture) : base(fixture) { }

    private const string Reason = "No connection could be made because the target machine actively refused it. (orders:5001)";

    private static RequestResponseLog Log(RequestResponseType type, Guid id, string method, string path, string? content = null) => new(
        TestName: "Create an order",
        TestId: "t1",
        Method: new HttpMethod(method),
        Content: content,
        Uri: new Uri("http://orders" + path),
        Headers: [],
        ServiceName: "OrderService",
        CallerName: "Caller",
        Type: type,
        TraceId: Guid.NewGuid(),
        RequestResponseId: id,
        TrackingIgnore: false,
        StatusCode: type == RequestResponseType.Response ? (OneOf<System.Net.HttpStatusCode, string>)System.Net.HttpStatusCode.OK : null);

    // The creator's own source: a call that threw, answered as a tracker answers one (the exception's type, its message as
    // the reason; a header, which the page starts hidden), then a call that was answered.
    private static string Source()
    {
        Guid failed = Guid.NewGuid(), answered = Guid.NewGuid();
        RequestResponseLog[] logs =
        [
            Log(RequestResponseType.Request, failed, "POST", "/api/orders", """{"item":"Widget"}"""),
            Log(RequestResponseType.Response, failed, "POST", "/api/orders") with
            {
                StatusCode = "!HttpRequestException", Error = Reason, Headers = [("x-attempt", "1")],
            },
            Log(RequestResponseType.Request, answered, "GET", "/api/orders/1"),
            Log(RequestResponseType.Response, answered, "GET", "/api/orders/1", """{"id":1}"""),
        ];
        return PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs).Single().PlantUmls.First().PlainText;
    }

    private async Task Open(ColorScheme scheme = ColorScheme.Light, [System.Runtime.CompilerServices.CallerMemberName] string? testName = null)
    {
        await Page.EmulateMediaAsync(new() { ColorScheme = scheme });
        var fileName = $"FailedAnswerReason_{testName}_{scheme}.html";
        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "t1", DisplayName = "Create an order", IsHappyPath = true,
                        Result = ExecutionResult.Failed, Duration = TimeSpan.FromSeconds(1),
                        Steps = [new ScenarioStep { Keyword = "When", Text = "I create an order", Status = ExecutionResult.Failed }]
                    }
                ]
            }
        ];
        var path = ReportGenerator.GenerateHtmlReport(
            [new DiagramAsCode("t1", "", Source())], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(TempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(OutputDir, fileName), true);

        await Page.GotoReportAsync(new Uri(path).AbsoluteUri);
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();
    }

    private sealed class Painted
    {
        public string Text { get; set; } = "";
        public string Fill { get; set; } = "";
        public string Shown { get; set; } = "";
    }

    // Every text the engine drew in the reason's ink: what it says, the fill the engine wrote and the fill the page paints.
    private Task<Painted[]> InkTexts() =>
        Page.Locator("[data-diagram-type='plantuml'] svg").First.EvaluateAsync<Painted[]>(
            "s => Array.from(s.querySelectorAll('text')).filter(t => (t.getAttribute('fill') || '').toLowerCase() === '"
            + NotePalette.ReasonInkDrawn.ToLowerInvariant()
            + "').map(t => ({ text: t.textContent.replace(/\\u200b/g, ''), fill: t.getAttribute('fill'), shown: getComputedStyle(t).fill }))");

    // The painted text with each run of spaces as one: the engine draws a note's words as texts of their own.
    private async Task<string> PaintedText() =>
        string.Join(" ", string.Join(" ", await Page.Locator("[data-diagram-type='plantuml'] svg").First
                .EvaluateAsync<string[]>("s => Array.from(s.querySelectorAll('text')).map(t => t.textContent.replace(/\\u200b/g, ''))"))
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Words(IEnumerable<Painted> texts) =>
        string.Join(" ", string.Join(" ", texts.Select(t => t.Text)).Split(' ', StringSplitOptions.RemoveEmptyEntries));

    [Theory]
    [InlineData(ColorScheme.Light, "rgb(179, 55, 46)")]
    [InlineData(ColorScheme.Dark, "rgb(242, 139, 129)")]
    public async Task The_reason_is_painted_in_the_themes_failure_ink(ColorScheme scheme, string ink)
    {
        await Open(scheme);

        var texts = await InkTexts();

        Assert.NotEmpty(texts);
        Assert.Equal(Reason, Words(texts));
        Assert.All(texts, t => Assert.Equal(ink, t.Shown));
        // the theme's own token, as the page resolves it
        Assert.Equal(ink, await Page.EvaluateAsync<string>(
            "() => { const p = document.createElement('div'); p.style.color = 'var(--dg-fail-ink)'; document.body.appendChild(p); "
            + "const c = getComputedStyle(p).color; p.remove(); return c; }"));
    }

    [Fact]
    public async Task A_success_gets_no_reason_and_the_hidden_headers_leave_the_reason_in_view()
    {
        await Open();

        // The success's body, drawn as YAML, the page's start (NotePayloadFormat).
        var painted = await PaintedText();
        Assert.Contains("id: 1", painted);
        Assert.DoesNotContain("x-attempt", painted);
        // The one text in the ink is the failed call's reason: the success's note has none.
        Assert.Equal(Reason, Words(await InkTexts()));
    }

    [Fact]
    public async Task The_reasons_note_folds_and_unfolds_as_other_notes_do()
    {
        await Open();

        var before = await GetSvgHtml();
        await SetScenarioState("collapsed");
        await WaitForSvgReRender(before);
        // Collapsed, a note paints a one-line preview of its payload: the reason's start, still in its ink.
        var folded = Words(await InkTexts());
        Assert.StartsWith("No connection", folded);
        Assert.True(folded.Length < Reason.Length, folded);

        before = await GetSvgHtml();
        await SetScenarioState("expanded");
        await WaitForSvgReRender(before);
        Assert.Equal(Reason, Words(await InkTexts()));

        before = await GetSvgHtml();
        await Page.Locator(".toolbar-row .toggle-btn[data-toggle='headers']").First.ClickAsync();
        await WaitForSvgReRender(before);
        Assert.Contains("x-attempt=1", await PaintedText());
        Assert.Equal(Reason, Words(await InkTexts()));
    }
}
