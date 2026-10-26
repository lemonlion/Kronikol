using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The diagram menu's PlantUML editor (plans/PLANTUML_EDITOR_TAB_PLAN.md): a tab that draws a diagram's whole source as
/// one picture, which the report draws only in parts of about 12,000 px and other renderers refuse past their size
/// limits. The tab is opened in the click, written by the report's engine shim, and draws with a worker of its own built
/// from the report's worker script, at 20 times the report's cap.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class OpenInEditorTabTests : PlaywrightTestBase
{
    public OpenInEditorTabTests(PlaywrightFixture fixture) : base(fixture) { }

    // the source the menu was opened on (its diagram's data-plantuml)
    private string _source = "";

    // Opens the first scenario diagram's menu and clicks the editor item (under its submenu where it has one); returns
    // the tab the click opened. `prepare` runs in the report page just before the menu opens.
    private async Task<IPage> OpenEditorAsync(string fileName, Func<string, string>? editSource = null,
        string? fullSource = null, string? item = null, string? prepare = null)
    {
        await Page.GotoReportAsync(GenerateReport(fileName));
        await Page.Locator("details.feature").First.WaitForAsync();
        await ExpandFirstScenarioWithDiagram();
        var svg = await WaitForDiagramSvg();
        _source = await svg.EvaluateAsync<string>("el => el.closest('[data-plantuml]').getAttribute('data-plantuml')");
        if (editSource is not null)
        {
            // the menu reads the source when it opens; nothing redraws a diagram whose data-plantuml changes
            _source = editSource(_source);
            await svg.EvaluateAsync("(el, s) => el.closest('[data-plantuml]').setAttribute('data-plantuml', s)", _source);
        }
        if (fullSource is not null)
            await svg.EvaluateAsync("(el, s) => { el.closest('[data-plantuml]')._noteOriginalSource = s; }", fullSource);
        if (prepare is not null) await Page.EvaluateAsync(prepare);
        await DispatchContextMenu(svg);
        var menu = Page.Locator(".diagram-ctx-menu");
        await menu.WaitForAsync(new() { Timeout = 5000 });

        ILocator target;
        var parent = menu.Locator(".submenu-parent", new() { HasTextRegex = new Regex("^Open in PlantUML editor") });
        if (await parent.CountAsync() > 0)
        {
            await parent.First.HoverAsync();
            // the page keeps the source it first drew, and a note toggle or its own note layout makes the current one differ
            target = parent.First.Locator(".submenu").GetByText(item ?? "Open current PlantUML in editor", new() { Exact = true }).First;
        }
        else
        {
            target = menu.GetByText(item ?? "Open in PlantUML editor", new() { Exact = true }).First;
        }

        try { await target.WaitForAsync(new() { Timeout = 5000 }); }
        catch (TimeoutException)
        {
            var box = await target.BoundingBoxAsync();
            throw new Xunit.Sdk.XunitException("the menu shows: " + await menu.InnerTextAsync() + " | target box: " +
                (box is null ? "none" : $"{box.X},{box.Y} {box.Width}x{box.Height}") + " | viewport " + Page.ViewportSize?.Height);
        }
        var popupTask = Page.Context.WaitForPageAsync();
        await target.ClickAsync(new() { Timeout = 10000 });
        var tab = await popupTask;
        await tab.WaitForFunctionAsync("() => document.title === 'PlantUML editor' && !!document.getElementById('src')",
            null, new() { Timeout = 15000, PollingInterval = 200 });
        return tab;
    }

    // Waits for the state, or for a failure, which ends the wait at once and names itself; a wait that runs out says
    // where the tab was.
    private static async Task WaitForStateAsync(IPage tab, string state, int timeoutMs = 90000)
    {
        try
        {
            await tab.WaitForFunctionAsync(
                "s => { const t = document.body.getAttribute('data-state'); return t === s || t === 'failed' || t === 'no-engine'; }", state,
                new() { Timeout = timeoutMs, PollingInterval = 200 });
        }
        catch (TimeoutException)
        {
            throw new Xunit.Sdk.XunitException($"after {timeoutMs} ms the tab is still '{await StateAsync(tab)}', not '{state}': "
                + await tab.Locator("#status").TextContentAsync());
        }
        var reached = await StateAsync(tab);
        if (reached != state)
            throw new Xunit.Sdk.XunitException($"the tab is '{reached}', not '{state}': " + await tab.Locator("#status").TextContentAsync());
    }

    // Waits until the tab has left the state; a wait that runs out says where the tab was.
    private static async Task WaitForStateToLeaveAsync(IPage tab, string state, int timeoutMs)
    {
        try
        {
            await tab.WaitForFunctionAsync("s => document.body.getAttribute('data-state') !== s", state,
                new() { Timeout = timeoutMs, PollingInterval = 200 });
        }
        catch (TimeoutException)
        {
            throw new Xunit.Sdk.XunitException($"after {timeoutMs} ms the tab is still '{state}': " + await tab.Locator("#status").TextContentAsync());
        }
    }

    private static Task<string> StateAsync(IPage tab) => tab.EvaluateAsync<string>("() => document.body.getAttribute('data-state')");

    // A sequence diagram the report draws in several parts: `messages` calls at 45 px each in the report's estimate.
    private static string LongSource(int messages, string marker = "Long", string detail = "")
    {
        var sb = new StringBuilder("@startuml\nparticipant " + marker + "Caller\nparticipant " + marker + "Service\n");
        for (var i = 1; i <= messages; i++)
            sb.Append(i % 2 == 1 ? $"{marker}Caller -> {marker}Service : call {i}{detail}\n" : $"{marker}Service --> {marker}Caller : reply {i}{detail}\n");
        return sb.Append("@enduml").ToString();
    }

    [Fact]
    public async Task The_editor_item_opens_a_tab_that_shows_the_source_and_draws_it()
    {
        var tab = await OpenEditorAsync("EditorTab_Opens.html");

        Assert.Equal(_source.Replace("\r\n", "\n"), (await tab.Locator("#src").InputValueAsync()).Replace("\r\n", "\n"));
        await WaitForStateAsync(tab, "drawn");
        Assert.Equal(1, await tab.Locator("#pic .paper > svg").CountAsync());
        Assert.False(tab.Url.StartsWith("blob:", StringComparison.Ordinal), "the tab shows " + tab.Url);
        Assert.Matches(@"Drawn in [\d.]+ s · [\d,]+ × [\d,]+ px", await tab.Locator("#status").TextContentAsync());
    }

    [Fact]
    public async Task The_whole_source_is_drawn_as_one_picture_taller_than_any_part_the_report_draws()
    {
        // 700 messages: about 31,500 px in the report's estimate, so the report draws it in three parts
        var tab = await OpenEditorAsync("EditorTab_Whole.html", _ => LongSource(700));
        var reportParts = await Page.EvaluateAsync<int>("s => window._splitWithChunkedNotes(s).length", _source);
        Assert.True(reportParts >= 3, "the report draws it in " + reportParts + " parts");

        await WaitForStateAsync(tab, "drawn", 120000);
        var size = await tab.EvaluateAsync<double[]>(
            "() => { const s = document.querySelector('#pic .paper > svg'); return [parseFloat(s.getAttribute('width')), parseFloat(s.getAttribute('height'))]; }");
        Assert.True(size[1] > 12000, "the whole picture is " + size[1] + " px tall");
        Assert.Contains("call 699", await tab.Locator("#pic svg").First.TextContentAsync());

        // Parts draws it the way the report does
        await tab.Locator("#parts").ClickAsync();
        await WaitForStateAsync(tab, "drawn", 120000);
        Assert.Equal(reportParts, await tab.Locator("#pic .paper > svg").CountAsync());
        Assert.Equal("true", await tab.Locator("#parts").GetAttributeAsync("aria-pressed"));
        Assert.True(await tab.Locator("#save-svg").IsDisabledAsync(), "saving is for the whole diagram");
    }

    [Fact]
    public async Task An_edit_redraws_the_picture()
    {
        var tab = await OpenEditorAsync("EditorTab_Edit.html");
        await WaitForStateAsync(tab, "drawn");

        await tab.Locator("#src").FillAsync("@startuml\nEditedAlice -> EditedBob : hello from the editor\n@enduml");
        await tab.WaitForFunctionAsync(
            "() => document.body.getAttribute('data-state') === 'drawn' && (document.querySelector('#pic svg')?.textContent || '').includes('hello from the editor')",
            null, new() { Timeout = 60000, PollingInterval = 200 });
    }

    [Fact]
    public async Task A_long_source_is_drawn_whole_and_drawn_again_only_when_asked()
    {
        // Over 200,000 characters, from the menu as a reader opens it. The source arrives as the tab's value, never typed:
        // Playwright's FillAsync enters a text line by line (16,000 input events, 13 s for this one, past 30 s under the
        // full suite's load), where the menu sets the value in 10 ms and a real paste lands in 50 ms as one input event.
        var longSource = LongSource(4000, "Big", " with a payload described at some length");
        Assert.True(longSource.Length > 200000, "the source is " + longSource.Length + " characters");
        var tab = await OpenEditorAsync("EditorTab_LongEdit.html", _ => longSource);

        // the tab draws what it opens with, whatever its length, as one picture taller than the report's own limit,
        // 98,304 px, at which the engine refuses it: the tab's raised limit reached the engine
        await WaitForStateAsync(tab, "drawn", 180000);
        var height = await tab.EvaluateAsync<double>("() => parseFloat(document.querySelector('#pic .paper > svg').getAttribute('height'))");
        Assert.True(height > 98304, "the picture is " + height + " px tall");
        Assert.Contains("call 3999", await tab.Locator("#pic svg").First.TextContentAsync());

        // an edit to it marks the picture out of date and waits for Render: a message typed above its last line, @enduml
        await tab.Locator("#src").PressAsync("Control+End");
        await tab.Locator("#src").PressAsync("Home");
        await tab.Locator("#src").PressSequentiallyAsync("BigCaller -> BigService : one more\n");
        await WaitForStateAsync(tab, "stale", 10000);
        await Task.Delay(1500);
        Assert.Equal("stale", await StateAsync(tab));

        await tab.Locator("#src").PressAsync("Control+Enter");
        await WaitForStateToLeaveAsync(tab, "stale", 10000);
        await WaitForStateAsync(tab, "drawn", 180000);
        Assert.Contains("one more", await tab.Locator("#pic svg").First.TextContentAsync());
    }

    [Theory]
    [InlineData("Open full PlantUML in editor", true)]
    [InlineData("Open current PlantUML in editor", false)]
    public async Task Full_and_current_items_open_their_own_source(string item, bool full)
    {
        var fullSource = "@startuml\nFullAlice -> FullBob : the full source\n@enduml";
        var tab = await OpenEditorAsync($"EditorTab_{(full ? "Full" : "Current")}.html", fullSource: fullSource, item: item);

        var text = (await tab.Locator("#src").InputValueAsync()).Replace("\r\n", "\n");
        Assert.Equal((full ? fullSource : _source).Replace("\r\n", "\n"), text);
    }

    [Fact]
    public async Task Without_an_engine_the_source_stays_in_the_editor_and_the_reason_is_named()
    {
        // the report's own diagrams are drawn already; the editor asks for an engine where no worker can start
        var tab = await OpenEditorAsync("EditorTab_NoEngine.html", prepare: "() => { window.Worker = undefined; }");

        await WaitForStateAsync(tab, "no-engine", 30000);
        Assert.Equal(_source.Replace("\r\n", "\n"), (await tab.Locator("#src").InputValueAsync()).Replace("\r\n", "\n"));
        Assert.Contains("Worker unavailable", await tab.Locator("#status").TextContentAsync());
        Assert.True(await tab.Locator("#render").IsDisabledAsync());
        Assert.False(await tab.Locator("#save-puml").IsDisabledAsync());
    }

    [Fact]
    public async Task The_tab_keeps_drawing_once_the_report_is_gone()
    {
        var tab = await OpenEditorAsync("EditorTab_ReportGone.html");
        await WaitForStateAsync(tab, "drawn");

        await Page.GotoAsync("about:blank");
        await tab.Locator("#src").FillAsync("@startuml\nLoneAlice -> LoneBob : drawn without the report\n@enduml");
        await tab.WaitForFunctionAsync(
            "() => document.body.getAttribute('data-state') === 'drawn' && (document.querySelector('#pic svg')?.textContent || '').includes('drawn without the report')",
            null, new() { Timeout = 60000, PollingInterval = 200 });
    }

    [Fact]
    public async Task The_picture_and_the_source_save_as_files()
    {
        var tab = await OpenEditorAsync("EditorTab_Save.html");
        await WaitForStateAsync(tab, "drawn");

        var png = await tab.RunAndWaitForDownloadAsync(() => tab.Locator("#save-png").ClickAsync());
        Assert.Equal("diagram.png", png.SuggestedFilename);
        var pngBytes = await File.ReadAllBytesAsync((await png.PathAsync())!);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, pngBytes.Take(4).ToArray());

        var svg = await tab.RunAndWaitForDownloadAsync(() => tab.Locator("#save-svg").ClickAsync());
        Assert.Equal("diagram.svg", svg.SuggestedFilename);
        Assert.Contains("<svg", await File.ReadAllTextAsync((await svg.PathAsync())!));

        var puml = await tab.RunAndWaitForDownloadAsync(() => tab.Locator("#save-puml").ClickAsync());
        Assert.Equal("diagram.puml", puml.SuggestedFilename);
        Assert.Equal(_source.Replace("\r\n", "\n"), (await File.ReadAllTextAsync((await puml.PathAsync())!)).Replace("\r\n", "\n"));
    }

    // Saves the tab's picture as SVG and PNG; returns the SVG text and the PNG bytes.
    private static async Task<(string Svg, byte[] Png)> SaveBothAsync(IPage tab)
    {
        var svg = await tab.RunAndWaitForDownloadAsync(() => tab.Locator("#save-svg").ClickAsync());
        var png = await tab.RunAndWaitForDownloadAsync(() => tab.Locator("#save-png").ClickAsync(), new() { Timeout = 15000 });
        return (await File.ReadAllTextAsync((await svg.PathAsync())!), await File.ReadAllBytesAsync((await png.PathAsync())!));
    }

    private static Task<bool> ParsesAsXmlAsync(IPage tab, string svg) =>
        tab.EvaluateAsync<bool>("t => !new DOMParser().parseFromString(t, 'image/svg+xml').querySelector('parsererror')", svg);

    [Fact]
    public async Task A_picture_whose_source_comment_holds_a_double_hyphen_saves_as_PNG_and_as_SVG_that_parses()
    {
        // The engine writes the source into the SVG as <?plantuml-src ...?>, which a page holds as a comment, and an
        // encoded source can hold "--", which no XML comment may (the report's menu has handled it since 5.4.1). Which
        // sources encode with "--" is the engine's business: they are drawn in turn until one does.
        var tab = await OpenEditorAsync("EditorTab_DoubleHyphen.html");
        await WaitForStateAsync(tab, "drawn");
        var found = false;
        for (var i = 0; i < 40 && !found; i++)
        {
            await tab.Locator("#src").FillAsync("@startuml\n" + string.Join("\n", Enumerable.Range(0, 8).Select(m =>
                $"Caller -> Orders : request {i} step {m}\nOrders --> Caller : 200 for {i * 31 + m}")) + "\n@enduml");
            await tab.WaitForFunctionAsync(
                "i => document.body.getAttribute('data-state') === 'drawn' && (document.querySelector('#pic svg')?.textContent || '').includes('request ' + i + ' step 7')",
                i, new() { Timeout = 30000, PollingInterval = 200 });
            found = await tab.EvaluateAsync<bool>("""
                () => {
                    const w = document.createTreeWalker(document.querySelector('#pic svg'), NodeFilter.SHOW_COMMENT);
                    for (let n = w.nextNode(); n; n = w.nextNode()) if (n.data.indexOf('--') >= 0) return true;
                    return false;
                }
                """);
        }
        Assert.True(found, "premise: the source comment of one of the forty diagrams holds a double hyphen");

        var (svg, png) = await SaveBothAsync(tab);
        Assert.True(await ParsesAsXmlAsync(tab, svg), "the saved SVG is XML");
        Assert.Contains("<?plantuml-src ", svg);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png.Take(4).ToArray());
    }

    [Fact]
    public async Task A_picture_whose_text_holds_an_escape_character_saves_as_PNG_and_as_SVG_that_parses()
    {
        var tab = await OpenEditorAsync("EditorTab_Escape.html");
        await WaitForStateAsync(tab, "drawn");
        await tab.Locator("#src").FillAsync("@startuml\nCaller -> Orders : exit \u001B[31mred\u001B[0m\nOrders --> Caller : 200\n@enduml");
        await tab.WaitForFunctionAsync(
            "() => document.body.getAttribute('data-state') === 'drawn' && (document.querySelector('#pic svg')?.textContent || '').includes('\\u001B')",
            null, new() { Timeout = 30000, PollingInterval = 200 });

        var (svg, png) = await SaveBothAsync(tab);
        Assert.True(await ParsesAsXmlAsync(tab, svg), "the saved SVG is XML");
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png.Take(4).ToArray());
    }

    [Fact]
    public async Task Leaving_an_edited_tab_asks_first_and_an_unedited_one_does_not()
    {
        var tab = await OpenEditorAsync("EditorTab_Leave.html");
        await WaitForStateAsync(tab, "drawn");

        var asked = new List<string>();
        tab.Dialog += async (_, dialog) => { asked.Add(dialog.Type); await dialog.DismissAsync(); };
        // typed keys are the user activation the browser asks for before it shows the prompt
        await tab.Locator("#src").PressAsync("End");
        await tab.Locator("#src").PressSequentiallyAsync("' edited");
        await tab.CloseAsync(new() { RunBeforeUnload = true });
        for (var i = 0; i < 50 && asked.Count == 0; i++) await Task.Delay(100);
        Assert.Contains("beforeunload", asked);
        Assert.False(tab.IsClosed, "a dismissed prompt keeps the tab");

        // put back as it was: nothing to lose, nothing asked
        await tab.Locator("#src").FillAsync(_source);
        asked.Clear();
        await tab.CloseAsync(new() { RunBeforeUnload = true });
        for (var i = 0; i < 50 && !tab.IsClosed; i++) await Task.Delay(100);
        Assert.True(tab.IsClosed, "the tab closed");
        Assert.Empty(asked);
    }

    [Fact]
    public async Task The_tab_takes_the_report_theme()
    {
        var tab = await OpenEditorAsync("EditorTab_Dark.html", prepare: "() => document.documentElement.setAttribute('data-theme', 'dark')");

        var reportGround = await Page.EvaluateAsync<string>("() => getComputedStyle(document.documentElement).getPropertyValue('--kron-ground').trim()");
        Assert.Equal("dark", await tab.EvaluateAsync<string>("() => document.documentElement.getAttribute('data-theme')"));
        Assert.Equal(reportGround, await tab.EvaluateAsync<string>("() => getComputedStyle(document.documentElement).getPropertyValue('--ground').trim()"));
        Assert.Equal(await tab.EvaluateAsync<string>("() => getComputedStyle(document.body).backgroundColor"),
            await tab.EvaluateAsync<string>("g => { const d = document.createElement('div'); d.style.background = g; document.body.appendChild(d); const c = getComputedStyle(d).backgroundColor; d.remove(); return c; }", reportGround));
    }
}
