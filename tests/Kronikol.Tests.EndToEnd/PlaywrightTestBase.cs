using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// Base class for all report-level Playwright tests.
/// Each test gets its own BrowserContext and Page (full isolation, parallel-safe).
/// Concrete test classes must apply [Collection("Playwright.Xxx")] to assign a parallel group.
/// </summary>
public abstract class PlaywrightTestBase : IAsyncLifetime
{
    private readonly PlaywrightFixture _fixture;
    private IBrowserContext _context = null!;
    protected IBrowserContext Context => _context;
    protected IPage Page { get; private set; } = null!;
    protected readonly string TempDir;
    protected static readonly string OutputDir = Path.Combine(
        Path.GetDirectoryName(typeof(PlaywrightTestBase).Assembly.Location)!,
        "PlaywrightOutput");

    protected virtual int ViewportWidth => 1920;
    protected virtual int ViewportHeight => 1080;

    protected PlaywrightTestBase(PlaywrightFixture fixture)
    {
        _fixture = fixture;
        TempDir = Path.Combine(Path.GetTempPath(), "kronikol-pw-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(TempDir);
        Directory.CreateDirectory(OutputDir);
    }

    public async ValueTask InitializeAsync()
    {
        _context = await _fixture.NewContextAsync(ViewportWidth, ViewportHeight);
        Page = await _context.NewPageAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
        try { Directory.Delete(TempDir, true); } catch { /* best effort */ }
    }

    protected string GenerateReport(string fileName) =>
        ReportTestHelper.GenerateReport(TempDir, OutputDir, fileName);

    protected string GenerateReportWithWideDiagram(string fileName) =>
        ReportTestHelper.GenerateReportWithWideDiagram(TempDir, OutputDir, fileName);

    protected string GenerateReportWithWideNoteDiagram(string fileName) =>
        ReportTestHelper.GenerateReportWithWideNoteDiagram(TempDir, OutputDir, fileName);

    protected string GenerateReportWithEmbeddedComponentDiagram(string fileName) =>
        ReportTestHelper.GenerateReportWithEmbeddedComponentDiagram(TempDir, OutputDir, fileName);

    protected string GenerateMergedReport(string fileName) =>
        ReportTestHelper.GenerateMergedReport(TempDir, OutputDir, fileName);

    protected string GenerateReportWithStepTableToggle(string fileName) =>
        ReportTestHelper.GenerateReportWithStepTableToggle(TempDir, OutputDir, fileName);

    protected string GenerateReportWithRules(string fileName) =>
        ReportTestHelper.GenerateReportWithRules(TempDir, OutputDir, fileName);

    protected string GenerateReportWithBackground(string fileName) =>
        ReportTestHelper.GenerateReportWithBackground(TempDir, OutputDir, fileName);

    protected string GenerateReportWithSeparatedBackground(string fileName) =>
        ReportTestHelper.GenerateReportWithSeparatedBackground(TempDir, OutputDir, fileName);

    protected string GenerateReportWithAttachments(string fileName) =>
        ReportTestHelper.GenerateReportWithAttachments(TempDir, OutputDir, fileName);

    protected string GenerateReportWithCopiedAttachment(string fileName) =>
        ReportTestHelper.GenerateReportWithCopiedAttachment(TempDir, OutputDir, fileName);

    protected string GenerateReportWithComplexInlineParams(string fileName) =>
        ReportTestHelper.GenerateReportWithComplexInlineParams(TempDir, OutputDir, fileName);

    protected string ServePage(string html, [System.Runtime.CompilerServices.CallerMemberName] string? testName = null)
    {
        var path = Path.Combine(TempDir, $"{testName}.html");
        File.WriteAllText(path, html);
        var outputPath = Path.Combine(OutputDir, $"{testName}.html");
        File.WriteAllText(outputPath, html);
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>
    /// The diagram's PAINTED display lines, rebuilt from the baselines its words share.
    /// PlantUML emits one <c>&lt;text&gt;</c> per word (spaces are their own elements), so
    /// <c>textContent</c> alone cannot tell "one line" from "two lines that happen to
    /// concatenate" — any assertion about a note's line structure has to go through this.
    /// </summary>
    protected Task<string[]> GetPaintedSvgLines(string svgSelector = "[data-diagram-type='plantuml'] svg")
        => Page.EvaluateAsync<string[]>(
            """
            sel => {
                const rows = {};
                document.querySelectorAll(sel + ' text').forEach(t => {
                    const y = Math.round(parseFloat(t.getAttribute('y') || '0'));
                    const x = parseFloat(t.getAttribute('x') || '0');
                    (rows[y] = rows[y] || []).push([x, t.textContent]);
                });
                return Object.keys(rows).sort((a, b) => a - b)
                    .map(y => rows[y].sort((a, b) => a[0] - b[0]).map(p => p[1]).join('').trim())
                    .filter(s => s.length);
            }
            """, svgSelector);

    protected async Task ExpandFirstScenarioWithDiagram()
    {
        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Features" }).ClickAsync();
        await Page.Locator("button.collapse-expand-all", new() { HasTextString = "Expand All Scenarios" }).ClickAsync();
    }

    protected async Task<ILocator> WaitForDiagramSvg(int timeoutMs = 20000)
    {
        // Force rendering — IntersectionObserver doesn't fire reliably in headless Chrome
        await Page.EvaluateAsync(
            "() => { if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(document.body); }");

        // The first svg in DOM order may live in a hidden container (the embedded component diagram
        // section is display:none until toggled); wait for the first *visible* one — the worker path
        // renders hidden diagrams at the same time as visible ones, so a DOM-order `.First` would race.
        var svg = Page.Locator("[data-diagram-type='plantuml'] svg:visible");
        await svg.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs });
        return svg.First;
    }

    protected async Task DispatchContextMenu(ILocator element)
    {
        // Playwright's ClickAsync(Button=Right) doesn't reliably fire 'contextmenu' on SVG elements.
        // Dispatch the event via JavaScript instead.
        await element.EvaluateAsync("""
            (el) => {
                var rect = el.getBoundingClientRect();
                var e = new MouseEvent('contextmenu', {
                    bubbles: true, cancelable: true,
                    clientX: rect.left + rect.width / 2,
                    clientY: rect.top + rect.height / 2,
                    pageX: rect.left + rect.width / 2 + window.scrollX,
                    pageY: rect.top + rect.height / 2 + window.scrollY
                });
                el.dispatchEvent(e);
            }
        """);
    }

    protected async Task WaitForNoteElements()
    {
        await Page.WaitForFunctionAsync("""
            () => {
                var container = document.querySelector('[data-plantuml]');
                if (!container || container._noteRendering || window._plantumlRendering) return false;
                return document.querySelectorAll('.note-hover-rect').length > 0
                    && document.querySelectorAll('.note-toggle-icon').length > 0;
            }
        """, null, new() { Timeout = 15000, PollingInterval = 200 });
    }

    protected async Task FillSearchBar(string query)
    {
        await Page.Locator("#searchbar").FillAsync(query);
        await Page.Locator("#searchbar").DispatchEventAsync("keyup");
    }

    protected static ILocatorAssertions Expect(ILocator locator) =>
        Assertions.Expect(locator);

    protected static IPageAssertions Expect(IPage page) =>
        Assertions.Expect(page);
}
