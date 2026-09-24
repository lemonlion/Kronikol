using System.Diagnostics;
using System.Text.Json;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A Chromium of the sweep's own, launched without Playwright's <c>--hide-scrollbars</c>, so a tall page
/// gets the 15 px classic scrollbar a desktop Chrome on Windows or Linux draws. It takes layout width
/// while the media queries still see the whole window, which moves every band by up to 20 px; injected
/// <c>::-webkit-scrollbar</c> CSS does not bring it back under the default launch. Every other test keeps
/// the shared, scrollbar-less browser.
/// </summary>
public sealed class ClassicScrollbarBrowser : IAsyncLifetime
{
    private IPlaywright _playwright = null!;

    public IBrowser Browser { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            IgnoreDefaultArgs = ["--hide-scrollbars"]
        });
    }

    public async ValueTask DisposeAsync()
    {
        await Browser.DisposeAsync();
        _playwright.Dispose();
    }
}

/// <summary>
/// The report at every width from 320 to 1400 px, reloaded at each (the init script decides the phone
/// layout once, at load), with the phone-hidden filters and scenario toolbar opened the way a reader
/// opens them: no sideways scroll, no label wrapped inside a toolbar button, the export buttons inside
/// the filtering box, every scenario-toolbar control inside its toolbar, and the header laid out as
/// the breakpoint says (plans/TOOLBAR_AT_EVERY_WIDTH_PLAN.md §4). The page's scroll width cannot see
/// a clipped control: <c>.feature</c> and <c>.scenario</c> carry <c>content-visibility: auto</c>,
/// whose paint containment cuts off whatever overflows them, so the toolbar is measured against its
/// own edge. Every bad width is collected and reported together.
/// </summary>
[Collection(PlaywrightCollections.Mobile)]
public class ViewportSweepTests : IClassFixture<ClassicScrollbarBrowser>, IDisposable
{
    /// <summary>The breakpoint below which the filtering box takes a row of its own; stylesheets.css
    /// states it once, in the band block.</summary>
    private const int Breakpoint = 1160;

    /// <summary>20 px steps (the narrowest band ever measured is 770 to 780 px wide) plus the first width
    /// of the band and the first of the row layout, where the filtering box is narrowest.</summary>
    private static readonly int[] Widths =
        [.. Enumerable.Range(0, 55).Select(i => 320 + i * 20).Append(769).Append(Breakpoint + 1).Order()];

    private readonly ClassicScrollbarBrowser _browser;
    private readonly ITestOutputHelper _output;
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "kronikol-sweep-" + Guid.NewGuid().ToString("N")[..8]);
    private static readonly string OutputDir = Path.Combine(
        Path.GetDirectoryName(typeof(ViewportSweepTests).Assembly.Location)!, "PlaywrightOutput");

    public ViewportSweepTests(ClassicScrollbarBrowser browser, ITestOutputHelper output)
    {
        _browser = browser;
        _output = output;
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(OutputDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { /* best effort */ }
    }

    [Fact]
    public Task Run_report_with_internal_flow_tracking_fits_every_width() =>
        Sweep(ReportTestHelper.GenerateReportWithWideHeader(_tempDir, OutputDir, "SweepRunFlow.html", specifications: false, internalFlowTracking: true), runReport: true);

    [Fact]
    public Task Violet_specifications_with_internal_flow_tracking_fit_every_width() =>
        Sweep(ReportTestHelper.GenerateReportWithWideHeader(_tempDir, OutputDir, "SweepSpecificationsFlow.html", specifications: true, internalFlowTracking: true), runReport: false);

    /// <summary>The path where the scenario toolbar was an unstyled block: no internal-flow sheet.</summary>
    [Fact]
    public Task Run_report_without_internal_flow_tracking_fits_every_width() =>
        Sweep(ReportTestHelper.GenerateReportWithWideHeader(_tempDir, OutputDir, "SweepRunNoFlow.html", specifications: false, internalFlowTracking: false), runReport: true);

    private async Task Sweep(string url, bool runReport)
    {
        await using var context = await _browser.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = Widths[0], Height = 900 }
        });
        var page = await context.NewPageAsync();
        var problems = new List<string>();
        var scrollbars = new SortedSet<int>();
        var clock = Stopwatch.StartNew();
        var loaded = false;

        foreach (var width in Widths)
        {
            await page.SetViewportSizeAsync(width, 900);
            if (loaded) await page.ReloadAsync();
            else { await page.GotoAsync(url); loaded = true; }
            await page.WaitForFunctionAsync("() => document.querySelector('details.feature') !== null",
                null, new() { Timeout = 30000, PollingInterval = 200 });
            await page.EvaluateAsync("""
                () => document.querySelectorAll('details.feature, details.scenario, details.example-diagrams')
                    .forEach(d => d.open = true)
                """);
            if (width <= 768)
            {
                // The init script hides the filters and every scenario toolbar on a phone; open them
                // as a reader would, or the sweep measures nothing of them.
                await page.Locator(".mobile-filter-toggle").ClickAsync();
                await page.Locator(".scenario-diagram-controls-toggle").First.ClickAsync();
            }
            await page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))");

            var json = await page.EvaluateAsync<string>(Measure, new { breakpoint = Breakpoint, runReport });
            using var result = JsonDocument.Parse(json);
            scrollbars.Add(result.RootElement.GetProperty("scrollbar").GetInt32());
            foreach (var problem in result.RootElement.GetProperty("problems").EnumerateArray())
                problems.Add($"{width} px: {problem.GetString()}");
        }

        _output.WriteLine($"{Widths.Length} widths in {clock.Elapsed.TotalSeconds:F1} s; scrollbar widths seen: {string.Join("/", scrollbars)} px");
        Assert.True(problems.Count == 0, $"{problems.Count} problem(s):\n" + string.Join("\n", problems));
    }

    private const string Measure = """
        ({ breakpoint, runReport }) => {
            const problems = [];
            const vis = el => !!el && el.getBoundingClientRect().width > 0;
            const lines = el => { const r = document.createRange(); r.selectNodeContents(el); return r.getClientRects().length; };
            const label = el => (el.textContent || el.getAttribute('aria-label') || el.className || el.tagName).trim().slice(0, 40);
            const de = document.documentElement;
            const width = window.innerWidth;

            // 1. No sideways scroll (clientWidth, so a classic scrollbar is not counted as overflow).
            if (de.scrollWidth > de.clientWidth + 1)
                problems.push(`the page scrolls sideways by ${de.scrollWidth - de.clientWidth} px`);

            // 2. No label wrapped inside a button of the export cluster, the top bar or a scenario toolbar.
            for (const b of document.querySelectorAll('.filtering-box-export button, .toolbar-row button, .diagram-toggle button'))
                if (vis(b) && lines(b) > 1) problems.push(`"${label(b)}" wraps its label inside its button (${lines(b)} lines)`);

            // 3. The export cluster inside the filtering box.
            const box = document.querySelector('.filtering-box');
            const cluster = document.querySelector('.filtering-box-export');
            if (!vis(box) || !vis(cluster)) problems.push('the filtering box or its export buttons are not visible');
            else {
                const over = cluster.getBoundingClientRect().right - box.getBoundingClientRect().right;
                if (over > 1) problems.push(`the export buttons run ${Math.round(over)} px past the filtering box`);
            }

            // 4. Every scenario-toolbar control inside its toolbar: past the edge it is clipped out of sight.
            const toolbars = [...document.querySelectorAll('.diagram-toggle')].filter(vis);
            if (toolbars.length === 0) problems.push('no scenario toolbar is visible, so none was measured');
            for (const t of toolbars) {
                const edge = t.getBoundingClientRect().right;
                for (const k of t.querySelectorAll('button, select')) {
                    if (!vis(k)) continue;
                    const over = k.getBoundingClientRect().right - edge;
                    if (over > 1) problems.push(`"${label(k)}" ends ${Math.round(over)} px past its toolbar's edge, clipped out of sight`);
                }
            }

            // 5 and 6. The header's composition, and the CI box's cap, on a run report.
            if (runReport) {
                const row = document.querySelector('.header-row');
                const summary = document.querySelector('.header-row > .test-execution-summary');
                if (width <= 768) {
                    if (getComputedStyle(row).flexDirection !== 'column') problems.push('the header row is not a column');
                } else {
                    const b = box.getBoundingClientRect(), s = summary.getBoundingClientRect();
                    if (width <= breakpoint) {
                        if (b.top < s.bottom - 1) problems.push('the filtering box is not on a row of its own');
                        if (Math.abs(b.width - row.clientWidth) > 1)
                            problems.push(`the filtering box is ${Math.round(b.width)} px wide, not the header row's ${row.clientWidth}`);
                    } else if (Math.abs(b.top - s.top) > 1) problems.push('the filtering box is not beside the summary');

                    const ci = document.querySelector('.ci-metadata');
                    const cs = getComputedStyle(ci);
                    const cap = 20 * parseFloat(cs.fontSize) + parseFloat(cs.paddingLeft) + parseFloat(cs.paddingRight)
                        + parseFloat(cs.borderLeftWidth) + parseFloat(cs.borderRightWidth);
                    const ciWidth = ci.getBoundingClientRect().width;
                    if (ciWidth > cap + 1) problems.push(`the CI box is ${Math.round(ciWidth)} px wide, past its ${Math.round(cap)} px cap`);
                    for (const td of ci.querySelectorAll('td:first-child'))
                        if (lines(td) > 1) problems.push(`the CI label "${label(td)}" wraps`);
                }
            }
            return JSON.stringify({ problems, scrollbar: width - de.clientWidth });
        }
        """;
}
