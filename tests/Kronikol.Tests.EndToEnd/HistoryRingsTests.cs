using Kronikol.History;
using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The History tab's run trend in a browser (5.0.0, plans/OPTION_C_PLAN.md S11; the owner's round 17 items 29 and 30): a ring
/// per run with its rate inside and a fail arc where the run failed, and a bar per run carrying its time, in the middle of a
/// tall bar and just above a short one; the rings', bars' and card's colours from the theme's tokens, light and dark; a window
/// of fifty runs keeping every time legible and opening on the newest; and the labs page, which runs none of the report's
/// scripts but the theme's, drawing them the same. In 4.x both charts were bars in a stretched svg.
/// </summary>
// The labs fact runs the report pipeline (HistoryReportHelper.GenerateWithLabsPage), whose diagram fetcher and log store are
// process-wide: in the collection of the other classes that run it, so no two pipelines overlap.
[Collection(PlaywrightCollections.Diagrams)]
public class HistoryRingsTests : PlaywrightTestBase
{
    public HistoryRingsTests(PlaywrightFixture fixture) : base(fixture) { }

    private async Task OpenHistoryAsync(string url)
    {
        await Page.GotoReportAsync(url);
        await Page.Locator("details.feature").First.WaitForAsync();
        await OpenReportTabAsync("history");
    }

    [Fact]
    public async Task The_history_tab_draws_a_ring_and_a_timed_bar_for_every_run()
    {
        await OpenHistoryAsync(HistoryReportHelper.Generate(TempDir, OutputDir, "HistoryRings_Drawn.html"));
        var section = Page.Locator("#history-section");

        // six earlier runs and this one, and no stretched svg chart left; the lists shown are the newest-first ones (5.10.0)
        await Expect(section.Locator(".history-order-newest-first .history-rings > .history-ring")).ToHaveCountAsync(7);
        await Expect(section.Locator(".history-order-newest-first .history-bars > .history-col")).ToHaveCountAsync(7);
        await Expect(section.Locator("svg.history-chart-svg")).ToHaveCountAsync(0);

        var rings = await Page.EvaluateAsync<string[][]>(
            """
            () => Array.from(document.querySelectorAll('#history-section .history-order-newest-first .history-ring')).map(r => {
                const box = r.querySelector('svg').getBoundingClientRect();
                return [r.getAttribute('title'), r.querySelector('.ring-rate').textContent, String(r.querySelectorAll('.ring-fail').length),
                        String(Math.round(box.width)), String(Math.round(box.height))];
            })
            """);
        foreach (var ring in rings)
        {
            // Retry failed in every other earlier run and Pay broke in this one: three of four passed in each of those
            var failed = !ring[0].Contains(" 0 failed of 4", StringComparison.Ordinal);
            Assert.Equal(failed ? "75%" : "100%", ring[1]);
            Assert.Equal(failed ? "1" : "0", ring[2]);
            Assert.Equal(["56", "56"], ring[3..]);
        }
        Assert.Equal(4, rings.Count(r => r[2] == "1"));

        var times = await section.Locator(".history-order-newest-first .history-col .history-time").AllInnerTextsAsync();
        Assert.Equal(["310ms", "290ms", "290ms", "290ms", "290ms", "290ms", "290ms"], times);
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task The_rings_the_bars_and_the_card_take_the_themes_tokens(string theme)
    {
        await Context.AddInitScriptAsync($"try {{ localStorage.setItem('kronikol.theme', '{theme}'); }} catch (e) {{ }}");
        await OpenHistoryAsync(HistoryReportHelper.Generate(TempDir, OutputDir, $"HistoryRings_Theme_{theme}.html"));
        await Expect(Page.Locator("#history-section .history-ring-failed").First).ToBeVisibleAsync();

        var pairs = await Page.EvaluateAsync<string[]>(
            """
            () => {
                const token = (value, prop) => { const p = document.createElement('div'); p.style.setProperty(prop, value); document.body.appendChild(p); const v = getComputedStyle(p).getPropertyValue(prop); p.remove(); return v; };
                const s = sel => getComputedStyle(document.querySelector('#history-section ' + sel));
                return [
                    s('.history-ring .ring-pass').stroke, token('var(--kron-pass-ring)', 'color'),
                    s('.history-ring-failed .ring-fail').stroke, token('var(--kron-fail)', 'color'),
                    s('.history-ring .ring-track').stroke, token('var(--kron-track)', 'color'),
                    s('.history-ring:not(.history-ring-failed) .ring-rate').fill, token('var(--kron-ink)', 'color'),
                    s('.history-ring-failed .ring-rate').fill, token('var(--kron-fail-ink)', 'color'),
                    s('.history-col .history-bar').backgroundColor, token('color-mix(in srgb, var(--kron-accent) 26%, var(--kron-card))', 'background-color'),
                    s('.history-col .history-time').color, token('var(--kron-ink)', 'color'),
                    getComputedStyle(document.querySelector('#history-section')).backgroundColor, token('var(--kron-card)', 'background-color'),
                    s('.history-verdict-broke').backgroundColor, token('var(--kron-fail-soft)', 'background-color'),
                    s('.history-verdict-flaky').color, token('var(--kron-warn)', 'color')
                ];
            }
            """);
        string[] names = ["pass arc", "fail arc", "track", "rate", "failed run's rate", "bar", "time", "card", "broke pill", "flaky pill"];
        for (var i = 0; i < names.Length; i++)
            Assert.True(pairs[2 * i] == pairs[2 * i + 1], $"{theme}: the {names[i]} is {pairs[2 * i]}, where its token gives {pairs[2 * i + 1]}");
    }

    [Fact]
    public async Task A_bars_time_sits_in_the_middle_of_a_tall_bar_and_just_above_a_short_one()
    {
        // Four earlier runs of 850 ms, 44 s, 2 m 26 s and 1 h 5 m (one scenario holding all of it), so the hour-long run's bar
        // is the chart's full height and every other bar is short.
        int[] totals = [850, 44_000, 146_000, 3_900_000];
        await OpenHistoryAsync(HistoryReportHelper.Generate(TempDir, OutputDir, "HistoryRings_Times.html",
            (i, run) => i < totals.Length ? run with { Durations = [totals[i], 0, 0, 0] } : run));

        var texts = await Page.Locator("#history-section .history-order-newest-first .history-col .history-time").AllInnerTextsAsync();
        Assert.Equal(["310ms", "290ms", "290ms", "1h 5m", "2m 26s", "44s", "850ms"], texts);

        // each bar's box, its time's box, the time's painted text (its box spans the bar), the row's top, and whether short
        var boxes = await Page.EvaluateAsync<double[][]>(
            """
            () => Array.from(document.querySelectorAll('#history-section .history-order-newest-first .history-col')).map(c => {
                const bar = c.querySelector('.history-bar'), time = bar.querySelector('.history-time');
                const range = document.createRange(); range.selectNodeContents(time);
                const b = bar.getBoundingClientRect(), t = time.getBoundingClientRect(), ink = range.getBoundingClientRect();
                return [b.left, b.right, b.top, b.bottom, t.top, t.bottom, ink.left, ink.right, c.parentElement.getBoundingClientRect().top,
                        bar.classList.contains('history-bar-short') ? 1 : 0];
            })
            """);
        Assert.Equal(7, boxes.Length);
        for (var i = 0; i < boxes.Length; i++)
        {
            double[] x = boxes[i];
            double barLeft = x[0], barRight = x[1], barTop = x[2], barBottom = x[3], timeTop = x[4], timeBottom = x[5], inkLeft = x[6], inkRight = x[7], rowTop = x[8];
            Assert.True(Math.Abs((inkLeft + inkRight) / 2 - (barLeft + barRight) / 2) <= 1, $"{texts[i]}: the time is not centred across its bar");
            Assert.True(inkLeft >= barLeft - 0.5 && inkRight <= barRight + 0.5, $"{texts[i]}: the time is wider than its bar");
            Assert.True(timeTop >= rowTop - 0.5, $"{texts[i]}: the time rises out of the chart");
            if (texts[i] == "1h 5m")
            {
                Assert.True(x[9] == 0, "the chart's tallest bar is marked short");
                Assert.True(timeTop >= barTop && timeBottom <= barBottom, "the hour-long run's time is not inside its bar");
                Assert.True(Math.Abs((timeTop + timeBottom) / 2 - (barTop + barBottom) / 2) <= 1, "the hour-long run's time is not in the middle of its bar");
            }
            else
            {
                Assert.True(x[9] == 1, $"{texts[i]}: a bar {barBottom - barTop} px high is not marked short");
                Assert.True(timeBottom <= barTop + 0.5 && timeBottom >= barTop - 4, $"{texts[i]}: the time is not just above its bar ({timeBottom} against {barTop})");
            }
        }
    }

    // what the shown lists hold, read in the page: each shown list's name and its first and last run, and whether the radio
    // that shows it is checked (5.10.0, plans/REPORT_UI_FIXES_PLAN.md H1, H3)
    private const string ShownListsJs = """
        () => Array.from(document.querySelectorAll('#history-section [role=list], .history-charts [role=list]'))
            .filter(l => l.getClientRects().length > 0)
            .map(l => {
                const items = l.querySelectorAll('[role=listitem]');
                return l.getAttribute('aria-label') + '|' + items[0].getAttribute('title').split(' ')[0] + '|' + items[items.length - 1].getAttribute('title').split(' ')[0];
            })
        """;

    private static string[] Shown(string order, string newest, string oldest) => order == "newest"
        ? [$"Pass rate, newest run first|{newest}|{oldest}", $"Total duration, newest run first|{newest}|{oldest}"]
        : [$"Pass rate, oldest run first|{oldest}|{newest}", $"Total duration, oldest run first|{oldest}|{newest}"];

    private static async Task ChooseOrderAsync(IPage page, string label) =>
        await page.Locator(".history-order label", new() { HasTextString = label }).ClickAsync();

    // 51 runs (the default window of 50 and this one) at 1000 px: more than either card holds. Both cards keep their runs on
    // one line that scrolls inside the card, open on the newest run in both orders, and keep every time legible (H2; until
    // 5.10.0 the rings wrapped onto rows, oldest first, and the bars opened on the newest at the row's right end)
    [Theory]
    [InlineData("chromium")]
    [InlineData("firefox")]
    [InlineData("webkit")]
    public async Task A_window_of_fifty_runs_keeps_both_cards_on_one_line_and_opens_on_the_newest_run(string engine)
    {
        var at = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        var runs = Enumerable.Range(1, 51)
            .Select(i => new RunPoint($"t:{i}:1", at.AddHours(i), null, 200, i % 9 == 0 ? 1 : 0, i % 9 == 0 ? 201 : 200, 60_000L + i * 1_000L, false))
            .ToArray();
        var verdicts = new HistoryVerdicts
        {
            Suite = "Rings", Stream = "local", RunId = runs[^1].RunId, RunsRecorded = 50, MinRuns = 5, ColdStart = false, ColdStartMessage = null,
            Partial = false, Scenarios = [], Absent = [], Counts = new Dictionary<HistoryVerdictKind, int>(), Runs = runs, NewDependencies = [],
            FirstSeen = 0, Compare = null
        };
        var url = ServePage("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><style>" + Stylesheets.ReportTokensStyleSheet
            + Stylesheets.HtmlReportStyleSheet + Stylesheets.HistoryStyleSheet + "</style></head><body>" + HistoryHtml.Section(verdicts, null)
            + "</body></html>", $"FiftyRuns_{engine}");

        await InEngineAsync(engine, async page =>
        {
            await page.SetViewportSizeAsync(1000, 900);
            await page.GotoReportAsync(url);
            await page.Locator(".history-bars-scroll").First.WaitForAsync();
            foreach (var order in new[] { "newest", "oldest" })
            {
                if (order == "oldest") await ChooseOrderAsync(page, "Oldest first");
                Assert.Equal(Shown(order, "t:51:1", "t:1:1"), await page.EvaluateAsync<string[]>(ShownListsJs));
                // for each card: how far its row runs past the card, the lines it takes, the newest run's gaps to the card's
                // edges, how far the oldest run lies outside the card, and the runs it holds; then the page's own overflow
                var r = await page.EvaluateAsync<double[]>(
                    """
                    order => {
                        const shown = sel => Array.from(document.querySelectorAll(sel)).filter(e => e.getClientRects().length > 0)[0];
                        const out = [];
                        for (const [scroller, item] of [[shown('.history-rings-scroll'), '.history-ring'], [shown('.history-bars-scroll'), '.history-col']]) {
                            const view = scroller.getBoundingClientRect();
                            const boxes = Array.from(scroller.querySelectorAll(item)).map(i => i.getBoundingClientRect());
                            const newest = order === 'newest' ? boxes[0] : boxes[boxes.length - 1];
                            const oldest = order === 'newest' ? boxes[boxes.length - 1] : boxes[0];
                            out.push(scroller.scrollWidth - scroller.clientWidth, new Set(boxes.map(b => Math.round(b.top))).size,
                                newest.left - view.left, view.right - newest.right,
                                order === 'newest' ? oldest.left - view.right : view.left - oldest.right, boxes.length);
                        }
                        out.push(document.documentElement.scrollWidth - document.documentElement.clientWidth);
                        return out;
                    }
                    """, order);
                foreach (var (card, k) in new[] { ("rings", 0), ("bars", 6) })
                {
                    Assert.True(r[k] > 0, $"{engine} {order}: the 51 {card} fit their card, so this fact tests nothing");
                    Assert.True(r[k + 1] == 1, $"{engine} {order}: the {card} take {r[k + 1]} lines");
                    Assert.True(r[k + 2] >= -0.5 && r[k + 3] >= -0.5, $"{engine} {order}: the newest run's {card} entry is not in view when the card opens");
                    Assert.True(r[k + 4] > 0, $"{engine} {order}: the oldest run's {card} entry is in view: the card did not open on its newest");
                    Assert.True(r[k + 5] == 51, $"{engine} {order}: {r[k + 5]} {card} for 51 runs");
                }
                Assert.True(r[12] <= 0, $"{engine} {order}: the page scrolls sideways by {r[12]} px");

                // every time is legible: none runs into the next run's, or past its own bar
                var legible = await page.EvaluateAsync<int[]>(
                    """
                    () => {
                        const scroller = Array.from(document.querySelectorAll('.history-bars-scroll')).filter(e => e.getClientRects().length > 0)[0];
                        const cols = Array.from(scroller.querySelectorAll('.history-col')).map(c => c.getBoundingClientRect());
                        const ink = Array.from(scroller.querySelectorAll('.history-time')).map(t => { const g = document.createRange(); g.selectNodeContents(t); return g.getBoundingClientRect(); });
                        let overlaps = 0, spills = 0;
                        for (let i = 0; i < ink.length; i++) {
                            if (i > 0 && ink[i].left < ink[i - 1].right) overlaps++;
                            if (ink[i].left < cols[i].left - 0.5 || ink[i].right > cols[i].right + 0.5) spills++;
                        }
                        return [overlaps, spills];
                    }
                    """);
                Assert.True(legible[0] == 0, $"{engine} {order}: {legible[0]} times overlap the next run's");
                Assert.True(legible[1] == 0, $"{engine} {order}: {legible[1]} times run past their own bar");
            }
        });
    }

    // H1: one control turns both cards round, and back
    [Theory]
    [InlineData("chromium")]
    [InlineData("firefox")]
    [InlineData("webkit")]
    public async Task The_order_switch_turns_both_cards_round(string engine)
    {
        var url = HistoryReportHelper.Generate(TempDir, OutputDir, $"HistoryRings_Order_{engine}.html");
        await InEngineAsync(engine, async page =>
        {
            await page.GotoReportAsync(url);
            await page.Locator("details.feature").First.WaitForAsync(new() { State = WaitForSelectorState.Attached });
            await OpenReportTabAsync(page, "history");
            var ends = await page.EvaluateAsync<string[]>(
                "() => { const i = document.querySelectorAll('#history-section .history-order-oldest-first .history-ring'); return [i[0].title.split(' ')[0], i[i.length - 1].title.split(' ')[0]]; }");
            string oldest = ends[0], newest = ends[1];
            Assert.NotEqual(oldest, newest);

            Assert.Equal(Shown("newest", newest, oldest), await page.EvaluateAsync<string[]>(ShownListsJs));
            await ChooseOrderAsync(page, "Oldest first");
            Assert.Equal(Shown("oldest", newest, oldest), await page.EvaluateAsync<string[]>(ShownListsJs));
            await ChooseOrderAsync(page, "Newest first");
            Assert.Equal(Shown("newest", newest, oldest), await page.EvaluateAsync<string[]>(ShownListsJs));
        });
    }

    // H4: nothing keeps the order, so a reload opens newest first again, in Firefox too, which restores a radio's state
    // across a reload unless the radio says autocomplete="off". On the labs page: its markup is parsed as written, which is
    // where Firefox restores a form's state (a probe of 2026-10-26: a plain radio kept Oldest first across the reload, one
    // with autocomplete="off" did not); the report draws its page from a script, which Firefox never restores.
    [Fact]
    public async Task A_reload_opens_newest_first_again()
    {
        var labs = HistoryReportHelper.GenerateWithLabsPage(TempDir, OutputDir, "HistoryRings_Reload.html").Page;
        await InEngineAsync("firefox", async page =>
        {
            await page.GotoReportAsync(labs);
            await page.Locator("header.labs-header").WaitForAsync();
            await ChooseOrderAsync(page, "Oldest first");
            Assert.StartsWith("Pass rate, oldest run first|", (await page.EvaluateAsync<string[]>(ShownListsJs))[0]);

            await page.ReloadAsync();
            await page.Locator("header.labs-header").WaitForAsync();
            Assert.True(await page.Locator(".history-order input[value='newest']").IsCheckedAsync());
            var shown = await page.EvaluateAsync<string[]>(ShownListsJs);
            Assert.StartsWith("Pass rate, newest run first|", shown[0]);
            Assert.StartsWith("Total duration, newest run first|", shown[1]);
        });
    }

    // H3: the labs page runs none of the report's scripts, and its control turns the cards round all the same
    [Fact]
    public async Task The_labs_page_orders_the_cards_with_none_of_the_reports_scripts()
    {
        var labs = HistoryReportHelper.GenerateWithLabsPage(TempDir, OutputDir, "HistoryRings_LabsOrder.html").Page;
        await Page.GotoReportAsync(labs);
        await Page.Locator("header.labs-header").WaitForAsync();
        Assert.Equal(["kronikol-theme-boot", "kronikol-theme-switch"],
            await Page.Locator("script").EvaluateAllAsync<string[]>("scripts => scripts.map(s => s.id)"));

        Assert.StartsWith("Pass rate, newest run first|", (await Page.EvaluateAsync<string[]>(ShownListsJs))[0]);
        await ChooseOrderAsync(Page, "Oldest first");
        var shown = await Page.EvaluateAsync<string[]>(ShownListsJs);
        Assert.Equal(2, shown.Length);
        Assert.StartsWith("Pass rate, oldest run first|", shown[0]);
        Assert.StartsWith("Total duration, oldest run first|", shown[1]);
    }

    [Fact]
    public async Task The_labs_page_draws_the_rings_and_the_timed_bars_with_none_of_the_reports_scripts()
    {
        var labs = HistoryReportHelper.GenerateWithLabsPage(TempDir, OutputDir, "HistoryRings_Labs.html").Page;
        await Page.GotoReportAsync(labs);
        await Page.Locator("header.labs-header").WaitForAsync();

        // the theme's two scripts and none that draws: the rings and the bars are markup (5.0.0, plans/OPTION_C_PLAN.md S13)
        Assert.Equal(["kronikol-theme-boot", "kronikol-theme-switch"],
            await Page.Locator("script").EvaluateAllAsync<string[]>("scripts => scripts.map(s => s.id)"));
        var rings = Page.Locator("#history-section .history-order-newest-first .history-ring");
        await Expect(rings).ToHaveCountAsync(7);
        await Expect(rings.First.Locator("svg")).ToBeVisibleAsync();
        await Expect(Page.Locator("#history-section .history-order-newest-first .history-col .history-time")).ToHaveCountAsync(7);

        // Since S13 the page carries the token sheet: each colour is the token's, with no fallback (a token the page lacked
        // would paint no stroke at all), and in a light page the light theme's.
        var paint = await Page.EvaluateAsync<string[]>(
            """
            () => {
                const probe = v => { const p = document.createElement('div'); p.style.color = v; document.body.appendChild(p); const c = getComputedStyle(p).color; p.remove(); return c; };
                // a run that passed: a failed run's rate is drawn in the failure ink
                const ring = document.querySelector('#history-section .history-ring:not(.history-ring-failed)');
                return [getComputedStyle(ring.querySelector('.ring-pass')).stroke, probe('var(--kron-pass-ring)'),
                        getComputedStyle(ring.querySelector('.ring-track')).stroke, probe('var(--kron-track)'),
                        getComputedStyle(ring.querySelector('.ring-rate')).fill, probe('var(--kron-ink)'),
                        ring.querySelector('.ring-rate').textContent];
            }
            """);
        Assert.Equal("rgb(63, 154, 98)", paint[0]);
        Assert.Equal(paint[1], paint[0]);
        Assert.Equal(paint[3], paint[2]);
        Assert.Equal(paint[5], paint[4]);
        Assert.Matches(@"^\d{1,3}%$", paint[6]);
    }
}
