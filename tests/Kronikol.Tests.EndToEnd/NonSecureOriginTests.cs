using System.Text.Json;
using Kronikol.Constants;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A report a team serves over plain http from another machine (a LAN address, an internal host name) opens where the
/// browser grants no secure context: <c>isSecureContext</c> is false, and every API that needs one is missing,
/// <c>crypto.subtle</c> and <c>navigator.clipboard</c> among them. <c>file://</c> and <c>http://localhost</c> are secure
/// contexts, so no other test reaches this case. Each fact launches a Chromium that resolves a test host name to the
/// loopback file server, which gives the page an origin the browser treats as another machine's.
/// </summary>
[Collection(PlaywrightCollections.Diagrams)]
public class NonSecureOriginTests : PlaywrightTestBase
{
    private const string LanHost = "kronikol-lan.test";

    public NonSecureOriginTests(PlaywrightFixture fixture) : base(fixture) { }

    /// <summary>A report opened from the LAN host in a browser of its own, with what its page logged.</summary>
    private sealed class LanReport : IAsyncDisposable
    {
        public required LoopbackFileServer Server { get; init; }
        public required IPlaywright Playwright { get; init; }
        public required IBrowser Browser { get; init; }
        public required IBrowserContext Context { get; init; }
        public required IPage Page { get; init; }
        public List<string> ConsoleErrors { get; } = [];
        public List<string> PageErrors { get; } = [];

        /// <summary>
        /// A page on <c>http://127.0.0.1</c> of the same server, a secure context allowed to read the clipboard, so a
        /// test can see what the report page copied.
        /// </summary>
        public async Task<IPage> ClipboardReader()
        {
            await Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"], new() { Origin = Server.UrlOf("").TrimEnd('/') });
            var reader = await PlaywrightTestBase.OpenPageAsync(Context);
            await reader.GotoAsync(Server.UrlOf("blank.html"));
            return reader;
        }

        public async ValueTask DisposeAsync()
        {
            await Browser.DisposeAsync();
            Playwright.Dispose();
            await Server.DisposeAsync();
        }
    }

    private static async Task<LanReport> OpenOnTheLanHost(string reportUrl)
    {
        var file = new Uri(reportUrl).LocalPath;
        var root = Path.GetDirectoryName(file)!;
        File.WriteAllText(Path.Combine(root, "blank.html"), "<!DOCTYPE html><title>blank</title>");
        var server = LoopbackFileServer.Start(root);
        var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, Args = [$"--host-resolver-rules=MAP {LanHost} 127.0.0.1"] });
        var context = await PlaywrightTestBase.OpenContextAsync(browser, new() { ViewportSize = new() { Width = 1920, Height = 1080 } });
        var page = await PlaywrightTestBase.OpenPageAsync(context);
        var lan = new LanReport { Server = server, Playwright = playwright, Browser = browser, Context = context, Page = page };
        page.Console += (_, m) => { if (m.Type == "error") lock (lan.ConsoleErrors) lan.ConsoleErrors.Add(m.Text); };
        page.PageError += (_, error) => { lock (lan.PageErrors) lan.PageErrors.Add(error); };

        await page.GotoAsync(server.UrlOf(Path.GetFileName(file), LanHost));

        // The control: the page is the case these facts are for, with none of the secure context's APIs.
        var origin = await page.EvaluateAsync<JsonElement>(
            "() => ({ secure: window.isSecureContext, subtle: typeof (window.crypto && window.crypto.subtle), clipboard: typeof navigator.clipboard })");
        Assert.False(origin.GetProperty("secure").GetBoolean(), $"{LanHost} should not be a secure context: {origin}");
        Assert.Equal("undefined", origin.GetProperty("subtle").GetString());
        Assert.Equal("undefined", origin.GetProperty("clipboard").GetString());
        return lan;
    }

    private static async Task ExpandEveryScenario(IPage page)
    {
        await page.WaitForFunctionAsync("() => document.body.classList.contains('plantuml-ready')", null, new() { Timeout = 60000, PollingInterval = 200 });
        await page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Features" }).ClickAsync();
        await page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Scenarios" }).ClickAsync();
    }

    private sealed class Drawn
    {
        public string Text { get; set; } = "";
        public bool HasSvg { get; set; }
    }

    private static async Task<Drawn[]> RenderEveryDiagram(IPage page)
    {
        await ExpandEveryScenario(page);
        await page.EvaluateAsync("() => window._renderDiagramsInContainer(document.body)");
        await page.WaitForFunctionAsync(BrowserRenderWorkerTests.AllRenderedJs, null, new() { Timeout = 120000, PollingInterval = 200 });
        return await page.EvaluateAsync<Drawn[]>(
            "() => Array.from(document.querySelectorAll('.plantuml-browser')).map(el => ({ text: el.textContent, hasSvg: !!el.querySelector('svg') }))");
    }

    private static async Task<ILocator> FirstDrawnDiagram(IPage page)
    {
        await ExpandEveryScenario(page);
        await page.EvaluateAsync("() => window._renderDiagramsInContainer(document.body)");
        var svg = page.Locator("[data-diagram-type='plantuml'] svg:visible").First;
        await svg.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60000 });
        return svg;
    }

    /// <summary>Opens the diagram menu on <paramref name="svg"/> and clicks <paramref name="path"/>, through its submenu when it has two parts.</summary>
    private static async Task ClickMenuItem(IPage page, ILocator svg, params string[] path)
    {
        await svg.EvaluateAsync("""
            (el) => {
                var rect = el.getBoundingClientRect();
                el.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true,
                    clientX: rect.left + rect.width / 2, clientY: rect.top + rect.height / 2 }));
            }
            """);
        var menu = page.Locator(".diagram-ctx-menu");
        await menu.WaitForAsync(new() { Timeout = 5000 });
        if (path.Length > 1)
        {
            var parent = menu.Locator(".submenu-parent", new() { HasTextRegex = new System.Text.RegularExpressions.Regex("^" + System.Text.RegularExpressions.Regex.Escape(path[0])) }).First;
            await parent.HoverAsync();
            await parent.Locator(".submenu").GetByText(path[^1], new() { Exact = true }).First.ClickAsync();
            return;
        }
        await menu.GetByText(path[^1], new() { Exact = true }).First.ClickAsync();
    }

    private static Task SetClipboard(IPage reader, string text) => reader.EvaluateAsync("t => navigator.clipboard.writeText(t)", text);

    // Windows keeps text on its clipboard with CR LF line breaks.
    private static async Task<string> ReadClipboard(IPage reader) =>
        (await reader.EvaluateAsync<string>("() => navigator.clipboard.readText()")).Replace("\r\n", "\n");

    [Fact]
    public async Task A_report_on_a_lan_host_renders_in_workers_with_both_engine_files_verified()
    {
        // plans/ENGINE_PIN_PLAN.md §5 names three origins: file://, http://localhost and a LAN address. The LAN address is
        // the one that differs: no secure context, so no crypto.subtle, which is why the check is the browser's own
        // integrity check and the page carries no digest code of its own (§1.7, §1.10). A probe measured it; this holds it.
        await using var lan = await OpenOnTheLanHost(GenerateReport("LanHostRenders.html"));

        var diagrams = await RenderEveryDiagram(lan.Page);

        Assert.NotEmpty(diagrams);
        Assert.All(diagrams, d => Assert.True(d.HasSvg, d.Text[..Math.Min(300, d.Text.Length)]));
        var r = await lan.Page.EvaluateAsync<JsonElement>("() => window.__kronikolRender");
        Assert.Equal("worker", r.GetProperty("mode").GetString());
        Assert.Equal("verified", r.GetProperty("engineIntegrity").GetString());
        Assert.Equal("verified", r.GetProperty("vizIntegrity").GetString());
    }

    [Fact]
    public async Task A_report_on_a_lan_host_refuses_an_engine_that_fails_its_hash()
    {
        // The refusal is told from a network failure by a plain fetch of the same URL, which needs no secure context either.
        await using var lan = await OpenOnTheLanHost(
            BrowserRenderWorkerTests.WithWrongHash(GenerateReport("LanHostWrongHash.html"), TrackingDefaults.PlantUmlJsIntegrity));

        var diagrams = await RenderEveryDiagram(lan.Page);

        BrowserRenderWorkerTests.AssertEveryDiagramNamesTheRefusedEngine(diagrams.Select(d => d.Text).ToArray());
        BrowserRenderWorkerTests.AssertTheConsoleNamesTheRefusedEngine(lan.ConsoleErrors);
        var r = await lan.Page.EvaluateAsync<JsonElement>("() => window.__kronikolRender");
        Assert.Equal("mismatch", r.GetProperty("engineIntegrity").GetString());
    }

    [Fact]
    public async Task The_copy_actions_copy_on_a_lan_host()
    {
        // navigator.clipboard exists only in a secure context. On a LAN host every copy the diagram menu offers and the
        // scenario's copy button threw a TypeError, and nothing reached the clipboard.
        await using var lan = await OpenOnTheLanHost(GenerateReport("LanHostCopies.html"));
        var reader = await lan.ClipboardReader();
        var svg = await FirstDrawnDiagram(lan.Page);
        var source = await svg.EvaluateAsync<string>("el => el.closest('[data-diagram-type]').getAttribute('data-plantuml')");
        Assert.StartsWith("@startuml", source);

        // From 4.0.0 a report opens with headers hidden and notes as YAML, so the drawn source, which data-plantuml holds
        // once drawn, differs from the report's, and the menu offers both: "current" is the drawn one.
        await SetClipboard(reader, "nothing copied yet");
        await ClickMenuItem(lan.Page, svg, "Copy PlantUML source", "Copy current PlantUML source");
        Assert.Equal(source, await ReadClipboard(reader));

        await SetClipboard(reader, "nothing copied yet");
        await ClickMenuItem(lan.Page, svg, "Copy image", "Copy as SVG");
        Assert.StartsWith("<svg", await ReadClipboard(reader));

        await SetClipboard(reader, "nothing copied yet");
        var button = lan.Page.Locator(".copy-scenario-name").First;
        var name = await button.GetAttributeAsync("data-scenario-name");
        await button.ClickAsync();
        await Assertions.Expect(button).ToHaveTextAsync("✓", new() { Timeout = 5000 });
        Assert.Equal(name, await ReadClipboard(reader));

        lock (lan.PageErrors) Assert.Empty(lan.PageErrors);
    }

    [Fact]
    public async Task The_diagram_menu_offers_image_copies_only_where_the_page_can_copy_an_image()
    {
        // An image reaches the clipboard only through navigator.clipboard and ClipboardItem, both missing on a LAN host
        // (and ClipboardItem in Firefox before 127): Copy as PNG threw there. The menu leaves the PNG copies out where the
        // page cannot copy an image, and keeps Copy as SVG, which is text; Save as PNG is still offered.
        await using var lan = await OpenOnTheLanHost(GenerateReport("LanHostImageCopies.html"));
        var svg = await FirstDrawnDiagram(lan.Page);

        var items = await MenuItems(lan.Page, svg);

        Assert.Contains("Copy as SVG", items);
        Assert.Contains("Save as PNG", items);
        Assert.DoesNotContain("Copy as PNG", items);
        Assert.DoesNotContain("Copy as PNG (no transparency)", items);

        // The control: the same report from the loopback address, a secure context, offers them.
        var loopback = await OpenPageAsync(lan.Context);
        await loopback.GotoAsync(lan.Server.UrlOf("LanHostImageCopies.html"));
        var loopbackItems = await MenuItems(loopback, await FirstDrawnDiagram(loopback));
        Assert.Contains("Copy as PNG", loopbackItems);
        Assert.Contains("Copy as PNG (no transparency)", loopbackItems);
    }

    [Fact]
    public async Task The_flame_chart_menu_offers_its_png_copy_only_where_the_page_can_copy_an_image()
    {
        // A flame chart or call tree is HTML, so its menu offers a PNG only: Copy as PNG, which threw on a LAN host, and
        // Save as PNG, which works there.
        var file = Path.Combine(OutputDir, "LanHostFlameMenu.html");
        File.WriteAllText(file, TestPageGenerator.GenerateIflowPopupTestPage(includeToggle: true, includeContextMenu: true));
        await using var lan = await OpenOnTheLanHost(new Uri(file).AbsoluteUri);
        await lan.Page.Locator("#trigger-seg-1").ClickAsync();
        await Assertions.Expect(lan.Page.Locator(".iflow-popup")).ToBeVisibleAsync();
        await lan.Page.Locator(".iflow-diagram svg").First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = PopupFirstDrawTimeout });
        await lan.Page.Locator(".iflow-toggle-btn").Nth(1).ClickAsync();
        var bar = lan.Page.Locator(".iflow-flame-bar").First;
        await bar.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15000 });

        var items = await MenuItems(lan.Page, bar);

        Assert.Contains("Save as PNG", items);
        Assert.DoesNotContain("Copy as PNG", items);
        lock (lan.PageErrors) Assert.Empty(lan.PageErrors);
    }

    /// <summary>Every item of the diagram menu opened on <paramref name="svg"/>, submenus included, one entry each.</summary>
    private static async Task<string[]> MenuItems(IPage page, ILocator svg)
    {
        await svg.EvaluateAsync("""
            (el) => {
                var rect = el.getBoundingClientRect();
                el.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true,
                    clientX: rect.left + rect.width / 2, clientY: rect.top + rect.height / 2 }));
            }
            """);
        await page.Locator(".diagram-ctx-menu").WaitForAsync(new() { Timeout = 5000 });
        return await page.EvaluateAsync<string[]>("""
            () => Array.from(document.querySelectorAll('.diagram-ctx-menu div'))
                .filter(d => !d.classList.contains('submenu'))
                .map(d => (d.childNodes[0] && d.childNodes[0].nodeType === 3 ? d.childNodes[0].textContent : d.textContent).trim())
            """);
    }
}
