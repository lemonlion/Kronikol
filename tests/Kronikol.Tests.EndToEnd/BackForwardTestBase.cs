using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// What the facts of Back and Forward as the report's navigation share (5.2.0, plans/BACK_BUTTON_NAVIGATION_PLAN.md
/// section 7). The report is opened after a page of its own, so "Back left the report" is that page and not a test that
/// cannot go back. Back and Forward press the browser's own buttons (Playwright's GoBack and GoForward, which work for the
/// report's entries in every engine) and then wait until the report has put the place back: the navigation script raises
/// a counter on <c>&lt;html data-kron-nav&gt;</c> once the tab, the filters, the scroll position and the focus are back. A
/// page without the script (the report before 5.2.0) is read at once, so a fact fails at what it checks, not at a wait.
/// The facts press real controls, never <c>pushState</c> from an evaluation: Playwright's evaluations count as a user
/// action in every engine (plan section 3), so an entry pushed from one would pass where the reader's page would not.
/// </summary>
public abstract class BackForwardTestBase : PlaywrightTestBase
{
    protected BackForwardTestBase(PlaywrightFixture fixture) : base(fixture) { }

    protected const string BeforeTitle = "Before the report";

    /// <summary>Writes the page the report is opened from, beside it, and returns its address.</summary>
    protected string BeforePage()
    {
        var path = Path.Combine(TempDir, "before.html");
        File.WriteAllText(path, $"<!DOCTYPE html><html><head><title>{BeforeTitle}</title></head><body><p>The page before the report.</p></body></html>");
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>Opens the page before the report, then the report at <paramref name="hash"/>, and waits for it to settle.</summary>
    protected Task OpenAsync(string url, string hash = "") => OpenAsync(Page, url, hash);

    protected async Task OpenAsync(IPage page, string url, string hash = "")
    {
        await page.GotoReportAsync(BeforePage());
        await page.GotoReportAsync(url + hash);
        // in the page, not on screen: a page that opens on another tab, or at an address naming one, hides the features
        await page.Locator("details.feature").First.WaitForAsync(new() { State = WaitForSelectorState.Attached });
        await SettledAsync(page, 0);
    }

    /// <summary>The navigation script's counter: how many arrivals the page has finished (0 before the first).</summary>
    protected static Task<int> CountAsync(IPage page) =>
        page.EvaluateAsync<int>("() => parseInt(document.documentElement.getAttribute('data-kron-nav') || '0', 10)");

    /// <summary>Waits until the page has finished an arrival after the <paramref name="after"/>th, or has left the report.</summary>
    protected static Task SettledAsync(IPage page, int after) =>
        page.WaitForFunctionAsync(
            "n => !window.kronikol_nav || parseInt(document.documentElement.getAttribute('data-kron-nav') || '0', 10) > n",
            after, new() { PollingInterval = 200, Timeout = 30000 });

    /// <summary>
    /// Brings <paramref name="element"/> into the open, under the report's bar and above the window's bottom, and waits
    /// until it has held its place there for half a second, bringing it back whenever the page moves it away. WebKit
    /// anchors no scroll: after the features far above are opened, the scenarios near the screen are laid out and drawn
    /// as they come near, and what is on the screen moves while the page's scroll holds still. A scroll into view then
    /// left the element far below the window, and the click aimed at it went to whatever had moved into its place (a
    /// feature's title, in R16 under WebKit, about one run in three).
    /// </summary>
    protected static async Task InViewAndStillAsync(IPage page, ILocator element)
    {
        await element.EvaluateAsync("el => { window.__heldStill = el; window.__heldSince = null; }");
        await page.WaitForFunctionAsync(
            """
            () => {
                const el = window.__heldStill, bar = document.querySelector('.toolbar-row.report-nav');
                const r = el.getBoundingClientRect(), now = performance.now(), last = window.__heldSince;
                const under = bar ? bar.getBoundingClientRect().bottom + 8 : 8;
                if (r.top < under || r.bottom > innerHeight - 8) {
                    el.scrollIntoView({ block: 'center', inline: 'nearest' });
                    window.__heldSince = null;
                    return false;
                }
                if (!last || Math.abs(last.top - r.top) > 0.5) { window.__heldSince = { top: r.top, since: now }; return false; }
                return now - last.since >= 500;
            }
            """,
            null, new() { PollingInterval = 200, Timeout = 20000 });
    }

    /// <summary>Waits until <paramref name="page"/> has held one scroll offset for half a second (WaitForScrollToSettleAsync, for any page).</summary>
    protected static Task ScrollSettledAsync(IPage page) =>
        page.WaitForFunctionAsync(
            """
            () => {
                const now = performance.now(), last = window.__scrollSettle;
                if (!last || last.y !== scrollY) { window.__scrollSettle = { y: scrollY, since: now }; return false; }
                return now - last.since >= 500;
            }
            """,
            null, new() { PollingInterval = 200, Timeout = 10000 });

    /// <summary>Runs a reader's action that goes somewhere, and waits until the page has arrived.</summary>
    protected Task ActAsync(Func<Task> action) => ActAsync(Page, action);

    protected static async Task ActAsync(IPage page, Func<Task> action)
    {
        var n = await CountAsync(page);
        await action();
        await SettledAsync(page, n);
    }

    protected Task BackAsync() => BackAsync(Page);

    protected static async Task BackAsync(IPage page)
    {
        var n = await CountAsync(page);
        await page.GoBackReportAsync(new() { Timeout = 30000 });
        await SettledAsync(page, n);
    }

    protected Task ForwardAsync() => ForwardAsync(Page);

    protected static async Task ForwardAsync(IPage page)
    {
        var n = await CountAsync(page);
        await page.GoForwardReportAsync(new() { Timeout = 30000 });
        await SettledAsync(page, n);
    }

    /// <summary>Fails with the page Back left for, when it left the report.</summary>
    protected static async Task StillInTheReportAsync(IPage page, string because)
    {
        var title = await page.TitleAsync();
        Assert.True(title != BeforeTitle, $"{because}: Back left the report for the page before it ({page.Url})");
    }

    protected Task StillInTheReportAsync(string because) => StillInTheReportAsync(Page, because);

    protected static Task<string?> SelectedTabAsync(IPage page) =>
        page.EvaluateAsync<string?>("() => { const t = document.querySelector('.report-tab[aria-selected=\"true\"]'); return t && t.dataset.tab; }");

    protected Task<string?> SelectedTabAsync() => SelectedTabAsync(Page);

    protected Task<string?> SelectedViewAsync() => SelectedViewAsync(Page);

    protected static Task<string> HashAsync(IPage page) => page.EvaluateAsync<string>("() => location.hash");

    protected Task<string> HashAsync() => HashAsync(Page);

    /// <summary>
    /// The address once the page has written <paramref name="expected"/>, or what it is after three seconds: the next change
    /// of the same control waits its turn (120 ms), and a fact then says what the address was instead.
    /// </summary>
    protected async Task<string> HashSoonAsync(string expected)
    {
        try { await Page.WaitForFunctionAsync("h => location.hash === h", expected, new() { PollingInterval = 200, Timeout = 3000 }); }
        catch (PlaywrightException) { }
        return await HashAsync();
    }

    protected Task<int> HistoryLengthAsync() => Page.EvaluateAsync<int>("() => history.length");

    /// <summary>Where an element's top is in the window, in CSS pixels.</summary>
    protected static Task<double> TopAsync(IPage page, string selector) =>
        page.Locator(selector).First.EvaluateAsync<double>("el => el.getBoundingClientRect().top");

    protected Task<double> TopAsync(string selector) => TopAsync(Page, selector);

    /// <summary>The focused element, named by its id, else its link's href, else its tag.</summary>
    protected static Task<string> FocusedAsync(IPage page) =>
        page.EvaluateAsync<string>("() => { const a = document.activeElement; return !a ? '' : a.id ? '#' + a.id : a.getAttribute('href') ? 'href=' + a.getAttribute('href') : a.tagName.toLowerCase(); }");

    protected Task<string> FocusedAsync() => FocusedAsync(Page);

    /// <summary>Scrolls the window as a reader does, with the wheel, and waits until it has held still.</summary>
    protected Task WheelAsync(int deltaY) => WheelAsync(Page, deltaY);

    protected Task<bool> IsOpenAsync(string selector) => IsOpenAsync(Page, selector);

    protected static Task<bool> IsOpenAsync(IPage page, string selector) =>
        page.Locator(selector).First.EvaluateAsync<bool>("d => d.hasAttribute('open')");

    protected static Task<string?> SelectedViewAsync(IPage page) =>
        page.EvaluateAsync<string?>("() => { const b = document.querySelector('.view-switch > .view-btn[aria-selected=\"true\"]'); return b && b.dataset.view; }");

    /// <summary>Whether the title of the scenario (or the element) <paramref name="selector"/> names is in the window.</summary>
    protected static Task<bool> TitleOnScreenAsync(IPage page, string selector) =>
        page.Locator(selector).First.EvaluateAsync<bool>(
            "el => { const r = (el.querySelector(':scope > summary') || el).getBoundingClientRect(); return r.top >= 0 && r.bottom <= innerHeight; }");

    /// <summary>Scrolls a page as a reader does, with the wheel, and waits until it has held one position for half a second.</summary>
    protected static async Task WheelAsync(IPage page, int deltaY)
    {
        var size = page.ViewportSize;
        await page.Mouse.MoveAsync((size?.Width ?? 1280) / 2f, (size?.Height ?? 700) / 2f);
        await page.Mouse.WheelAsync(0, deltaY);
        await page.WaitForFunctionAsync(
            """
            () => {
                const now = performance.now(), last = window.__scrollSettle;
                if (!last || last.y !== scrollY) { window.__scrollSettle = { y: scrollY, since: now }; return false; }
                return now - last.since >= 500;
            }
            """,
            null, new() { PollingInterval = 200, Timeout = 10000 });
    }
}
