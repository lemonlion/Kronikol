using Microsoft.Playwright;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// The report's tabs (5.0.0, plans/OPTION_C_PLAN.md S3, D20): one panel at a time, chosen by a click or the arrow keys,
/// the component diagram drawn when its tab first shows, and anything that goes to a scenario (Next Failure, a history
/// link, a scenario's address) taken to the Features tab first. The bar sticks to the top on a desktop and tells the
/// page its height, so a scenario scrolled to is not left under it.
/// </summary>
[Collection(PlaywrightCollections.Reports)]
public class ReportTabsTests : PlaywrightTestBase
{
    public ReportTabsTests(PlaywrightFixture fixture) : base(fixture) { }

    protected override int ViewportWidth => 1400;
    protected override int ViewportHeight => 900;

    private async Task OpenAsync(string fileName, string? hash = null)
    {
        var url = ReportTestHelper.GenerateReportWithEverySection(TempDir, OutputDir, fileName);
        await Page.GotoAsync(url + (hash ?? ""));
        await Page.Locator(".report-tabs").WaitForAsync();
    }

    private Task<string[]> ShownPanelsAsync() =>
        Page.EvaluateAsync<string[]>("() => Array.from(document.querySelectorAll('.report-panel')).filter(p => !p.hidden).map(p => p.dataset.panel)");

    private Task<string?> SelectedTabAsync() =>
        Page.EvaluateAsync<string?>("() => { const t = document.querySelector('.report-tab[aria-selected=\"true\"]'); return t && t.dataset.tab; }");

    [Fact]
    public async Task A_tab_shows_its_panel_alone_and_takes_the_tab_order()
    {
        await OpenAsync("Tabs_Click.html");
        Assert.Equal(["features"], await ShownPanelsAsync());
        Assert.Equal(["Features", "Timeline", "Components", "History", "Diagnostics"],
            (await Page.Locator(".report-tab").AllTextContentsAsync()).Select(t => new string(t.TakeWhile(char.IsLetter).ToArray())).ToArray());

        await Page.Locator("#tab-history").ClickAsync();
        Assert.Equal(["history"], await ShownPanelsAsync());
        Assert.Equal("history", await SelectedTabAsync());
        Assert.Equal(["-1", "-1", "-1", "0", "-1"], await Page.EvaluateAsync<string[]>("() => Array.from(document.querySelectorAll('.report-tab')).map(t => t.getAttribute('tabindex'))"));
        Assert.False(await Page.Locator(".toolbar-row.report-nav").EvaluateAsync<bool>("b => b.classList.contains('on-features')"));
        await Expect(Page.Locator(".features-tools")).ToBeHiddenAsync();

        await Page.Locator("#tab-features").ClickAsync();
        Assert.Equal(["features"], await ShownPanelsAsync());
        await Expect(Page.Locator(".features-tools")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task The_arrow_keys_home_and_end_move_between_the_tabs()
    {
        await OpenAsync("Tabs_Keys.html");
        await Page.Locator("#tab-features").FocusAsync();

        await Page.Keyboard.PressAsync("ArrowRight");
        Assert.Equal("timeline", await SelectedTabAsync());
        Assert.Equal("tab-timeline", await Page.EvaluateAsync<string>("() => document.activeElement.id"));
        await Page.Keyboard.PressAsync("End");
        Assert.Equal("diagnostics", await SelectedTabAsync());
        await Page.Keyboard.PressAsync("ArrowRight");
        Assert.Equal("features", await SelectedTabAsync());
        await Page.Keyboard.PressAsync("ArrowLeft");
        Assert.Equal("diagnostics", await SelectedTabAsync());
        await Page.Keyboard.PressAsync("Home");
        Assert.Equal("features", await SelectedTabAsync());
        Assert.Equal(["features"], await ShownPanelsAsync());
    }

    // a tab keeps its arrow keys, and / goes to the search from it as from anywhere else: from 5.2.0 to 5.2.6 the guard
    // that leaves a tab its keys took / as well
    [Fact]
    public async Task Slash_on_a_tab_goes_to_the_search()
    {
        await OpenAsync("Tabs_Slash.html");
        await Page.Locator("#tab-features").FocusAsync();

        await Page.Keyboard.PressAsync("/");

        await Expect(Page.Locator("#searchbar")).ToBeFocusedAsync();
        await Expect(Page.Locator("#searchbar")).ToHaveValueAsync("");
    }

    [Fact]
    public async Task The_component_diagram_is_drawn_when_its_tab_first_shows()
    {
        await OpenAsync("Tabs_Components.html");
        Assert.Equal(0, await Page.Locator("#component-diagram svg").CountAsync());

        await Page.Locator("#tab-components").ClickAsync();
        await Page.Locator("#component-diagram svg").First.WaitForAsync(new() { Timeout = 30000 });
    }

    [Fact]
    public async Task Next_failure_from_another_tab_goes_to_the_features_tab_and_the_failure()
    {
        await OpenAsync("Tabs_NextFailure.html");
        await Page.Locator("#tab-timeline").ClickAsync();

        await Page.Locator(".jump-to-failure").ClickAsync();
        Assert.Equal("features", await SelectedTabAsync());
        await Page.WaitForFunctionAsync("() => { const s = document.querySelector('details.scenario > summary.failed'); if (!s) return false; const r = s.getBoundingClientRect(); return r.top >= 0 && r.top < innerHeight; }",
            null, new() { PollingInterval = 200, Timeout = 10000 });
    }

    [Fact]
    public async Task A_history_link_goes_to_the_features_tab_and_its_scenario()
    {
        await OpenAsync("Tabs_HistoryLink.html");
        await Page.Locator("#tab-history").ClickAsync();
        var link = Page.Locator("#panel-history a.history-link").First;
        await Expect(link).ToBeVisibleAsync();

        await link.ClickAsync();
        Assert.Equal("features", await SelectedTabAsync());
        Assert.Contains("#sid-", Page.Url);
    }

    [Fact]
    public async Task A_scenario_address_opens_on_the_features_tab_even_when_another_tab_was_showing()
    {
        await OpenAsync("Tabs_Hash.html");
        await Page.Locator("#tab-components").ClickAsync();

        var anchor = await Page.Locator("details.scenario").First.GetAttributeAsync("id");
        await Page.EvaluateAsync("a => { location.hash = a; }", anchor);
        await Page.WaitForFunctionAsync("() => document.querySelector('.report-tab[aria-selected=\"true\"]').dataset.tab === 'features'",
            null, new() { PollingInterval = 200, Timeout = 5000 });
    }

    [Fact]
    public async Task The_history_and_diagnostics_sections_stay_open_in_their_tabs()
    {
        await OpenAsync("Tabs_Inert.html");
        foreach (var key in new[] { "history", "diagnostics" })
        {
            await Page.Locator($"#tab-{key}").ClickAsync();
            var summary = Page.Locator($"#panel-{key} > details > summary");
            await summary.ClickAsync();
            Assert.True(await Page.Locator($"#panel-{key} > details").EvaluateAsync<bool>("d => d.open"), $"the {key} section closed on a click");
        }
    }

    [Fact]
    public async Task The_bar_sticks_to_the_top_and_gives_the_page_its_height()
    {
        await OpenAsync("Tabs_Sticky.html");
        // every feature and scenario open, so the page is tall enough to scroll the bar's own place well away
        await Page.EvaluateAsync("() => { document.querySelectorAll('details').forEach(d => d.open = true); window.scrollTo(0, document.documentElement.scrollHeight); }");
        Assert.True(await Page.EvaluateAsync<double>("() => window.scrollY") > 600, "the page should scroll well past the bar");
        await TwoAnimationFramesAsync(Page);

        var top = await Page.Locator(".toolbar-row.report-nav").EvaluateAsync<double>("b => b.getBoundingClientRect().top");
        Assert.True(Math.Abs(top) < 1, $"the bar's top is at {top} after scrolling");
        var height = await Page.Locator(".toolbar-row.report-nav").EvaluateAsync<double>("b => b.getBoundingClientRect().height");
        var navH = await Page.EvaluateAsync<string>("() => getComputedStyle(document.documentElement).getPropertyValue('--kron-nav-h').trim()");
        // rounded as the page's Math.round does: .5 up, where Math.Round's default takes it to the even number
        Assert.Equal($"{Math.Round(height, MidpointRounding.AwayFromZero)}px", navH);
    }

    [Fact]
    public async Task On_a_phone_the_bar_stays_in_the_page()
    {
        await Page.SetViewportSizeAsync(400, 800);
        await OpenAsync("Tabs_Phone.html");

        Assert.Equal("static", await Page.Locator(".toolbar-row.report-nav").EvaluateAsync<string>("b => getComputedStyle(b).position"));
        Assert.Equal("0px", await Page.EvaluateAsync<string>("() => getComputedStyle(document.documentElement).getPropertyValue('--kron-nav-h').trim()"));
    }

    /// <summary>Q4: the failure clusters and the background calls open the Features tab as cards drawn like a feature's,
    /// in both themes.</summary>
    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task The_failure_clusters_and_background_calls_are_cards_at_the_top_of_the_features_tab(string theme)
    {
        await Context.AddInitScriptAsync($"try {{ localStorage.setItem('kronikol.theme', '{theme}'); }} catch (e) {{ }}");
        await OpenAsync($"Tabs_Cards_{theme}.html");

        var styles = await Page.EvaluateAsync<string[]>(
            """
            () => {
                const token = (name, prop) => { const p = document.createElement('div'); p.style.setProperty(prop, `var(${name})`); document.body.appendChild(p); const v = getComputedStyle(p).getPropertyValue(prop); p.remove(); return v; };
                const c = sel => getComputedStyle(document.querySelector(sel));
                const order = ['.failure-clusters', '.background-calls', '#report-content'].map(s => document.querySelector(s))
                    .every((el, i, all) => el.closest('#panel-features') && (i === 0 || (all[i - 1].compareDocumentPosition(el) & Node.DOCUMENT_POSITION_FOLLOWING)));
                return [String(order),
                        c('.failure-clusters').backgroundColor, c('.background-calls').backgroundColor, token('--kron-card', 'background-color'),
                        c('.failure-clusters').borderTopColor, token('--kron-hairline', 'border-top-color'), c('.failure-clusters').borderTopLeftRadius,
                        c('.failure-clusters > summary').color, token('--kron-fail', 'color'),
                        c('.failure-cluster-feature').color, token('--kron-muted', 'color')];
            }
            """);
        Assert.Equal("true", styles[0]);
        Assert.Equal(styles[3], styles[1]);
        Assert.Equal(styles[3], styles[2]);
        Assert.Equal(styles[5], styles[4]);
        Assert.Equal("12px", styles[6]);
        Assert.Equal(styles[8], styles[7]);
        Assert.Equal(styles[10], styles[9]);
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task The_selected_tab_is_marked_in_the_accent_and_the_panels_sit_on_the_panel_tint(string theme)
    {
        await Context.AddInitScriptAsync($"try {{ localStorage.setItem('kronikol.theme', '{theme}'); }} catch (e) {{ }}");
        await OpenAsync($"Tabs_Look_{theme}.html");

        var styles = await Page.EvaluateAsync<string[]>(
            """
            () => {
                const token = (name, prop) => { const p = document.createElement('div'); p.style.setProperty(prop, `var(${name})`); document.body.appendChild(p); const v = getComputedStyle(p).getPropertyValue(prop); p.remove(); return v; };
                const on = getComputedStyle(document.querySelector('.report-tab[aria-selected="true"]'));
                const off = getComputedStyle(document.querySelector('.report-tab[aria-selected="false"]'));
                const panels = getComputedStyle(document.querySelector('.report-panels'));
                return [on.color, token('--kron-accent', 'color'), on.boxShadow, off.color, token('--kron-ink-quiet', 'color'),
                        panels.backgroundColor, token('--kron-panel', 'background-color')];
            }
            """);
        Assert.Equal(styles[1], styles[0]);
        Assert.Contains("inset", styles[2]);
        Assert.Equal(styles[4], styles[3]);
        Assert.Equal(styles[6], styles[5]);
    }
}
