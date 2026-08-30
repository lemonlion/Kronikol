using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Firefox restores &lt;select&gt;/&lt;input&gt; state across a plain reload; Chromium does not. A reader
/// who had once picked YAML came back after a refresh to a scenario dropdown reading YAML sitting over
/// notes the freshly-loaded script had rendered as JSON — the control lied about the report's actual
/// state, and no click could resolve it (re-picking YAML on an already-YAML select fires no change).
/// The report's state lives in its script and, when shared, in the URL hash; the browser must not
/// second-guess it. These tests drive the real Firefox, so they skip where it is not installed
/// (CI installs chromium only) — the markup contract itself is pinned by the unit tests in
/// Kronikol.Tests/Reports/FormStateRestorationTests.
/// </summary>
public class FormStateRestorationTests : IAsyncLifetime
{
    private readonly string _tempDir;
    private readonly string _outputDir;
    private IPlaywright _playwright = null!;
    private IBrowser? _browser;
    private IPage _page = null!;

    public FormStateRestorationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "kronikol-ff-" + Guid.NewGuid().ToString("N")[..8]);
        _outputDir = Path.Combine(
            Path.GetDirectoryName(typeof(FormStateRestorationTests).Assembly.Location)!, "PlaywrightOutput");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_outputDir);
    }

    public async ValueTask InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        try
        {
            _browser = await _playwright.Firefox.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            _page = await _browser.NewPageAsync();
        }
        catch (PlaywrightException)
        {
            _browser = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null) await _browser.DisposeAsync();
        _playwright.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { /* best effort */ }
    }

    private async Task NavigateAndRender(string fileName)
    {
        await _page.GotoAsync(ReportTestHelper.GenerateReportWithJsonYamlNotes(_tempDir, _outputDir, fileName));
        await RenderAndWait();
    }

    private async Task RenderAndWait()
    {
        await _page.Locator("details.feature").First.WaitForAsync();
        await _page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Features" }).ClickAsync();
        await _page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Scenarios" }).ClickAsync();
        await _page.EvaluateAsync(
            "() => { if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(document.body); }");
        await _page.WaitForFunctionAsync("""
            () => {
                var container = document.querySelector('[data-plantuml]');
                if (!container || container._noteRendering || window._plantumlRendering) return false;
                return document.querySelectorAll('.note-hover-rect').length > 0;
            }
        """, null, new() { Timeout = 60000, PollingInterval = 200 });
    }

    private ILocator ScenarioNoteFormatSelect => _page.Locator("details.scenario .note-format-select").First;

    private ILocator ScenarioTruncateSelect => _page.Locator("details.scenario .truncate-lines-select").First;

    private async Task<string> SvgText()
    {
        var text = await _page.Locator("[data-diagram-type='plantuml'] svg").First
            .EvaluateAsync<string>("el => el.textContent");
        return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
    }

    private async Task SelectAndWaitForRerender(ILocator select, string value)
    {
        var renderCount = await _page.EvaluateAsync<int>("() => window._renderCompleteCount || 0");
        await select.SelectOptionAsync(value);
        await _page.WaitForFunctionAsync(
            "(prev) => !window._plantumlRendering && (window._renderCompleteCount || 0) > prev",
            renderCount, new() { Timeout = 60000, PollingInterval = 200 });
    }

    [Fact]
    public async Task Scenario_note_format_dropdown_reads_json_after_a_reload_that_re_renders_json_notes()
    {
        Assert.SkipWhen(_browser is null, "Firefox is not installed (CI installs chromium only)");

        await NavigateAndRender("FormRestore_ScenarioNoteFormat.html");
        await SelectAndWaitForRerender(ScenarioNoteFormatSelect, "yaml");
        Assert.Contains("query: |-", await SvgText());

        await _page.ReloadAsync();
        await RenderAndWait();

        // The reloaded script rendered the notes as JSON; the dropdown must say so too
        Assert.DoesNotContain("query: |-", await SvgText());
        Assert.Equal("json", await _page.EvaluateAsync<string>("() => window._noteFormatDefault"));
        Assert.Equal("json", await ScenarioNoteFormatSelect.InputValueAsync());
        Assert.Equal("json", await _page.Locator(".toolbar-right .note-format-select").InputValueAsync());
    }

    [Fact]
    public async Task Scenario_truncate_lines_dropdown_reads_the_default_after_a_reload()
    {
        Assert.SkipWhen(_browser is null, "Firefox is not installed (CI installs chromium only)");

        await NavigateAndRender("FormRestore_ScenarioTruncateLines.html");
        await SelectAndWaitForRerender(ScenarioTruncateSelect, "3");

        await _page.ReloadAsync();
        await RenderAndWait();

        Assert.Equal("40", await _page.EvaluateAsync<string>("() => String(window._truncateLines)"));
        Assert.Equal("40", await ScenarioTruncateSelect.InputValueAsync());
    }
}
