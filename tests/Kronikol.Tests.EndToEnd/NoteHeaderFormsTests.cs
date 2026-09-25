using Kronikol.PlantUml;
using Kronikol.Reports;
using Microsoft.Playwright;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Every report behaviour that reads a note's header lines, on both header forms (DIAGRAM_COLOURS_PLAN S1).
/// From 3.30.0 the emitter writes them in a computed ink, <see cref="NotePalette.HeaderTag"/>
/// (<c>&lt;color:#686868&gt;</c>), because <c>gray</c> fell below AA contrast on both note fills. The scripts
/// recognised a header line by the <c>gray</c> literal alone, so on a note in the new ink the headers stayed
/// up when hidden, came out as payload when copied, and made the body fail the JSON check that offers the
/// YAML view. A merged report holds sources written before and after 3.30.0, so <c>gray</c> must keep working.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class NoteHeaderFormsTests : DiagramNotePlaywrightBase
{
    public NoteHeaderFormsTests(PlaywrightFixture fixture) : base(fixture) { }

    public static TheoryData<string> HeaderTags => [NotePalette.HeaderTag, "<color:gray>"];

    /// <summary>The emitter's shape: coloured arrows, header lines, a blank line, then the body.</summary>
    private static string Source(string headerTag) => $$"""
        @startuml
        actor "Caller" as caller
        participant "OrderService" as svc

        caller -[#438DD5]> svc : POST /api/orders
        note left
        {{headerTag}}[Content-Type=application/json]
        {{headerTag}}[Authorization=Bearer token123]

        {"item":"Widget","qty":2}
        end note

        svc -[#438DD5]-> caller : 201 Created
        note right
        {{headerTag}}[X-Request-Id=abc-123]

        {"id":"abc-123","status":"created"}
        end note
        @enduml
        """;

    private async Task Open(string headerTag, [System.Runtime.CompilerServices.CallerMemberName] string? testName = null)
    {
        var fileName = $"NoteHeaderForms_{testName}_{(headerTag == "<color:gray>" ? "gray" : "ink")}.html";
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
                        Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                        Steps = [new ScenarioStep { Keyword = "When", Text = "I create an order", Status = ExecutionResult.Passed }]
                    }
                ]
            }
        ];
        var path = ReportGenerator.GenerateHtmlReport(
            [new DiagramAsCode("t1", "", Source(headerTag))], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(TempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);
        File.Copy(path, Path.Combine(OutputDir, fileName), true);

        await Page.GotoAsync(new Uri(path).AbsoluteUri);
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();
    }

    private async Task<string> CopyFromMenu(ILocator target, string item)
    {
        await Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        await DispatchContextMenu(target);
        var entry = Page.Locator(".diagram-ctx-menu").GetByText(item, new() { Exact = true });
        await entry.WaitForAsync(new() { Timeout = 5000 });
        await entry.ClickAsync();
        return await Page.EvaluateAsync<string>("() => navigator.clipboard.readText()");
    }

    [Theory]
    [MemberData(nameof(HeaderTags))]
    public async Task Hiding_headers_hides_every_header_line_and_keeps_the_body(string headerTag)
    {
        await Open(headerTag);
        Assert.Contains("Content-Type=application/json", await GetNormalizedSvgText());

        var before = await GetSvgHtml();
        await Page.Locator(".toolbar-row .toggle-btn[data-toggle='headers'][data-shown='true']").First.ClickAsync();
        await WaitForSvgReRender(before);

        var text = await GetNormalizedSvgText();
        Assert.DoesNotContain("Content-Type", text);
        Assert.DoesNotContain("Authorization", text);
        Assert.DoesNotContain("X-Request-Id", text);
        Assert.Contains("Widget", text);
        Assert.Contains("created", text);
    }

    [Theory]
    [MemberData(nameof(HeaderTags))]
    public async Task The_yaml_view_is_offered_and_keeps_the_header_lines(string headerTag)
    {
        await Open(headerTag);

        await ClickNoteFormatButton(0);

        var text = await GetNormalizedSvgText();
        Assert.Contains("item: Widget", text);
        Assert.Contains("qty: 2", text);
        Assert.Contains("Content-Type=application/json", text);
    }

    [Theory]
    [MemberData(nameof(HeaderTags))]
    public async Task Copy_box_text_gives_the_headers_and_the_body_without_the_tag(string headerTag)
    {
        await Open(headerTag);

        var clipboard = await CopyFromMenu(Page.Locator(".note-hover-rect").First, "Copy box text");

        Assert.Contains("[Content-Type=application/json]", clipboard);
        Assert.Contains("{\"item\":\"Widget\",\"qty\":2}", clipboard);
        Assert.DoesNotContain("<color", clipboard);
    }

    [Theory]
    [MemberData(nameof(HeaderTags))]
    public async Task Copy_all_caller_request_payloads_leaves_the_headers_out(string headerTag)
    {
        await Open(headerTag);

        var clipboard = await CopyFromMenu(Page.Locator("[data-diagram-type='plantuml'] svg").First, "Copy all caller request payloads");

        Assert.Contains("{\"item\":\"Widget\",\"qty\":2}", clipboard);
        Assert.DoesNotContain("Content-Type", clipboard);
        Assert.DoesNotContain("<color", clipboard);
    }

    [Theory]
    [MemberData(nameof(HeaderTags))]
    public async Task A_collapsed_note_previews_its_body_and_its_tooltip_shows_the_headers_untagged(string headerTag)
    {
        await Open(headerTag);

        var before = await GetSvgHtml();
        var collapse = Page.Locator(".diagram-toggle .details-radio-btn[data-state='collapsed']").First;
        await collapse.ClickAsync();
        await Expect(collapse).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("details-active"));
        await WaitForSvgReRender(before);

        // The collapsed note paints a one-line preview of its body; the whole note goes to a <title> tooltip,
        // which svg.textContent would mix in, so the two are read apart.
        var svg = Page.Locator("[data-diagram-type='plantuml'] svg").First;
        var painted = string.Join(" ", await svg.EvaluateAsync<string[]>("s => Array.from(s.querySelectorAll('text')).map(t => t.textContent)"));
        Assert.Contains("{\"item\":\"Widget\",\"qty\":2}", painted);
        Assert.DoesNotContain("Content-Type", painted);
        Assert.DoesNotContain("color:", painted);

        // The engine gives each participant a <title> too; the note's own is on its outline path.
        var tooltip = await svg.EvaluateAsync<string>("s => (s.querySelector('path > title') || {}).textContent || ''");
        Assert.Contains("[Content-Type=application/json]", tooltip);
        Assert.DoesNotContain("<color", tooltip);
    }
}
