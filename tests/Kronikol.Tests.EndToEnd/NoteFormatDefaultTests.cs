namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// E2E tests for <c>ReportConfigurationOptions.NotePayloadFormat</c>: a report
/// generated with the YAML default renders eligible JSON note payloads as YAML
/// with zero clicks, seeds the toolbar dropdowns, and keeps every toggle
/// available to go back.
/// </summary>
[Collection(PlaywrightCollections.Notes)]
public class NoteFormatDefaultTests : DiagramNotePlaywrightBase
{
    public NoteFormatDefaultTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task NavigateYamlDefault(string fileName)
    {
        await Page.GotoAsync(ReportTestHelper.GenerateReportWithJsonYamlNotes(
            TempDir, OutputDir, fileName, notePayloadFormat: Kronikol.Reports.NotePayloadFormat.Yaml));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        await WaitForDiagramSvg();
        await WaitForNoteElements();
    }

    [Fact]
    public async Task Yaml_default_renders_eligible_notes_as_yaml_with_zero_clicks()
    {
        await NavigateYamlDefault("FormatDefault_ZeroClicks.html");

        var text = await GetNormalizedSvgText();
        Assert.Contains("query: |-", text);
        Assert.Contains("FROM orders o", text);
        Assert.Contains("id: 9007199254740993", text);
        // The plain-text note is untouched by the default
        Assert.Contains("plain text response body", text);
        // The literal \n escapes of the JSON view are gone
        Assert.DoesNotContain("SELECT o.id,\\n", text);
    }

    [Fact]
    public async Task Yaml_default_seeds_the_toolbar_dropdowns()
    {
        await NavigateYamlDefault("FormatDefault_DropdownSeed.html");

        Assert.Equal("yaml", await Page.Locator(".toolbar-right .note-format-select").InputValueAsync());
        Assert.Equal("yaml", await Page.Locator("details.scenario .note-format-select").First.InputValueAsync());
        Assert.Equal("yaml", await Page.EvaluateAsync<string>("() => window._noteFormatDefault"));
    }

    [Fact]
    public async Task Per_note_toggle_back_to_json_works_under_a_yaml_default()
    {
        await NavigateYamlDefault("FormatDefault_ToggleBack.html");

        // The format button now offers the switch back to JSON
        await HoverNoteRect(0);
        await WaitForNoteFormatButtonVisible();
        var glyph = await Page.EvaluateAsync<string>("""
            () => Array.from(document.querySelectorAll('[data-note-btn="format"]'))
                .find(b => b.style.display !== 'none' && b.style.opacity === '1')
                .querySelector('text').textContent
        """);
        Assert.Equal("J", glyph);

        await ClickNoteFormatButton();

        var text = await GetNormalizedSvgText();
        Assert.DoesNotContain("query: |-", text);
        Assert.Contains("\"query\":", text);
        Assert.Contains("\"id\": 9007199254740993", text);
    }
}
