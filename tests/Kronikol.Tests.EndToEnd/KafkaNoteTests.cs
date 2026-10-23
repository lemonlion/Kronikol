using System.Text.RegularExpressions;
using Kronikol.Reports;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A Kafka note as #167 asked for it (plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.md 4.4, T60): the message's key on a line
/// of its own above the body, whatever the header toggle says (headers are hidden by default since 4.0.0, and the key
/// was part of the content before), the message's headers below the key following the toggle, and the value as an
/// indented body the YAML view is offered for. Read from the painted text, not the markup.
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class KafkaNoteTests : DiagramNotePlaywrightBase
{
    public KafkaNoteTests(PlaywrightFixture fixture) : base(fixture) { }

    private static readonly Regex KeyLine = new(@"Key\s*order-1");

    private async Task Navigate(string fileName, bool headersShown, NotePayloadFormat format = NotePayloadFormat.Json)
    {
        await Page.GotoReportAsync(ReportTestHelper.GenerateReportWithKafkaNote(TempDir, OutputDir, fileName, headersShown, format));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();
    }

    private static int At(string text, string part)
    {
        var at = text.IndexOf(part, StringComparison.Ordinal);
        Assert.True(at >= 0, $"'{part}' is not painted in: {text}");
        return at;
    }

    private static int KeyAt(string text)
    {
        var match = KeyLine.Match(text);
        Assert.True(match.Success, $"the key line is not painted in: {text}");
        return match.Index;
    }

    [Fact]
    public async Task With_headers_hidden_the_key_line_stays_above_the_indented_body()
    {
        await Navigate("KafkaNote_HeadersHidden.html", headersShown: false);

        var text = await GetNormalizedSvgText();
        Assert.True(KeyAt(text) < At(text, "\"Status\": \"Placed\""), text);
        Assert.DoesNotContain("source=checkout", text);
        Assert.DoesNotContain("kafka-key", text);
    }

    // A collapsed note paints a one-line preview of its body, and the key line is left out of it as the headers are.
    [Fact]
    public async Task A_collapsed_note_previews_its_body_without_the_key_line()
    {
        await Navigate("KafkaNote_Collapsed.html", headersShown: true);

        var before = await GetSvgHtml();
        await OpenScenarioSettingsAsync();
        var collapse = Page.Locator(".diagram-toggle .details-radio-btn[data-state='collapsed']").First;
        await collapse.ClickAsync();
        await Expect(collapse).ToHaveClassAsync(new Regex("details-active"));
        await WaitForSvgReRender(before);

        // The whole note goes to a <title> tooltip, which svg.textContent would mix in, so the painted text is read alone.
        var svg = Page.Locator("[data-diagram-type='plantuml'] svg").First;
        var painted = string.Join(" ", await svg.EvaluateAsync<string[]>("s => Array.from(s.querySelectorAll('text')).map(t => t.textContent)"));
        Assert.Contains("{", painted);
        Assert.DoesNotMatch(KeyLine, painted);
        Assert.DoesNotContain("source=checkout", painted);
    }

    [Fact]
    public async Task With_headers_shown_they_are_drawn_between_the_key_line_and_the_body()
    {
        await Navigate("KafkaNote_HeadersShown.html", headersShown: true);

        var text = await GetNormalizedSvgText();
        var key = KeyAt(text);
        var header = At(text, "source=checkout");
        Assert.True(key < header && header < At(text, "\"Status\": \"Placed\""), text);
        Assert.DoesNotContain("kafka-key", text);
    }

    [Fact]
    public async Task Hiding_headers_with_the_toggle_takes_the_headers_and_leaves_the_key_line()
    {
        await Navigate("KafkaNote_HideHeaders.html", headersShown: true);
        var renderCount = await Page.EvaluateAsync<int>("() => window._renderCompleteCount || 0");

        await OpenScenarioSettingsAsync();
        await Page.Locator("details.scenario .toggle-btn[data-toggle='headers'][data-shown='true']").First.ClickAsync();
        await WaitForRenderCountIncrease(renderCount);

        var text = await GetNormalizedSvgText();
        Assert.DoesNotContain("source=checkout", text);
        Assert.True(KeyAt(text) < At(text, "\"Status\": \"Placed\""), text);
    }

    [Fact]
    public async Task The_YAML_view_is_offered_and_flips_the_body_while_the_key_line_stays()
    {
        await Navigate("KafkaNote_YamlFlip.html", headersShown: false);

        await ClickNoteFormatButton(0);

        var text = await GetNormalizedSvgText();
        Assert.True(KeyAt(text) < At(text, "Status: Placed"), text);
        Assert.Contains("Sku: A1", text);
        Assert.DoesNotContain("\"Status\":", text);
    }

    private async Task<string> CopyFromMenu(ILocator target, string item)
    {
        await Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        await DispatchContextMenu(target);
        await Page.Locator(".diagram-ctx-menu").WaitForAsync(new() { Timeout = 5000 });
        var menuItem = Page.Locator(".diagram-ctx-menu").GetByText(item).First;
        await menuItem.WaitForAsync(new() { Timeout = 5000 });
        await menuItem.ClickAsync();
        return (await Page.EvaluateAsync<string>("() => navigator.clipboard.readText()")).Replace("\r\n", "\n");
    }

    // The box text reads as the note does: the key as text on the first line, its markup gone.
    [Fact]
    public async Task Copy_box_text_gives_the_key_line_as_text_above_the_body()
    {
        await Navigate("KafkaNote_CopyBoxText.html", headersShown: true);

        var copied = await CopyFromMenu(Page.Locator(".note-hover-rect").First, "Copy box text");

        var lines = copied.Split('\n');
        Assert.Equal("Key order-1", lines[0]);
        Assert.Contains(lines, l => l.Contains("\"Status\": \"Placed\"", StringComparison.Ordinal));
        Assert.DoesNotContain("<b>", copied, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Under_the_YAML_default_the_body_is_YAML_with_no_click_and_the_key_line_above_it()
    {
        await Navigate("KafkaNote_YamlDefault.html", headersShown: false, NotePayloadFormat.Yaml);

        var text = await GetNormalizedSvgText();
        Assert.True(KeyAt(text) < At(text, "Status: Placed"), text);
    }
}
