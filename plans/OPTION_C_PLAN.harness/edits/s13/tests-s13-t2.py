# S13's tests: the two new classes, copied from this folder, and the pins S13 moves, rewritten.
#   tests/Kronikol.Tests/Reports/ThemedPagesHtmlTests.cs        (core, new)
#   tests/Kronikol.Tests.EndToEnd/ThemedPagesTests.cs           (Playwright, new)
#   tests/Kronikol.Tests/Reports/LabsReportTests.cs             (the page's scripts: none, now the theme's two)
#   tests/Kronikol.Tests/History/HistoryRingsMarkupTests.cs     (every colour a token with a fallback, now with none)
#   tests/Kronikol.Tests/Reports/StylesheetRulesTests.cs        (T4: the violet's literal colours, now its tokens)
#   tests/Kronikol.Tests.EndToEnd/LabsPageTests.cs              (no script, now the theme's two)
#   tests/Kronikol.Tests.EndToEnd/HistoryRingsTests.cs          (the labs fact: no script, fallback probes)
#   tests/Kronikol.Tests.EndToEnd/DiagnosticsCardTests.cs       (the labs card in its 4.x colours, now the tokens)
#   tests/Kronikol.Tests.EndToEnd/ToolbarStylingTests.cs        (a violet case for the report tab; two comments)
# Every anchor is checked to occur exactly once, in memory, before anything is written; a second run is refused.
# BOMs and line endings are kept: a file read with CRLF gets its new text with CRLF.
# usage: python tests-s13.py <worktree root>
import os
import sys

ROOT = sys.argv[1].rstrip('/\\') + '/'
HERE = os.path.dirname(os.path.abspath(__file__))

copies = {
    'ThemedPagesHtmlTests.cs': 'tests/Kronikol.Tests/Reports/ThemedPagesHtmlTests.cs',
    'ThemedPagesTests.cs': 'tests/Kronikol.Tests.EndToEnd/ThemedPagesTests.cs',
}
for target in copies.values():
    assert not os.path.exists(ROOT + target), ('already there: the S13 tests are in already', target)
    assert os.path.isdir(os.path.dirname(ROOT + target)), ('no such folder: is this a worktree root?', target)

EDITS = {}


def edit(path, old, new):
    EDITS.setdefault(path, []).append((old, new))


LABS = 'tests/Kronikol.Tests/Reports/LabsReportTests.cs'
edit(LABS, r'''/// by the renderers a report's opt-ins use. It has no script; its scenario names link into the report by the
/// <c>#sid-</c> anchor the report resolves on load.
''', r'''/// by the renderers a report's opt-ins use. Of the report's scripts it runs only the theme's two (5.0.0,
/// plans/OPTION_C_PLAN.md S13); its scenario names link into the report by the <c>#sid-</c> anchor the report resolves on
/// load.
''')
edit(LABS, r'''    public void The_names_link_into_the_report_by_stable_id_and_the_page_has_no_script()
    {
        var page = Page(Broke(), []);

        Assert.Contains($"<a class=\"history-link\" href=\"TestRunReport.html#sid-{PayId}\" title=\"Open this scenario in the report\">Checkout &rsaquo; Pay by card</a>", page);
        Assert.Contains($"href=\"TestRunReport.html#sid-{RefundId}\"", Table(page));
        Assert.DoesNotContain("onclick", page);
        Assert.DoesNotContain("<script", page);
    }
''', r'''    public void The_names_link_into_the_report_by_stable_id_and_the_page_runs_only_the_theme_scripts()
    {
        var page = Page(Broke(), []);

        Assert.Contains($"<a class=\"history-link\" href=\"TestRunReport.html#sid-{PayId}\" title=\"Open this scenario in the report\">Checkout &rsaquo; Pay by card</a>", page);
        Assert.Contains($"href=\"TestRunReport.html#sid-{RefundId}\"", Table(page));
        Assert.DoesNotContain("onclick", page);
        // 5.0.0: the boot script that chooses light or dark and the one that works the switch, and no other
        // (plans/OPTION_C_PLAN.md S13)
        Assert.Equal(["<script id=\"kronikol-theme-boot\">", "<script id=\"kronikol-theme-switch\">"],
            Regex.Matches(page, "<script[^>]*>").Select(m => m.Value).ToArray());
    }
''')

RINGS_MARKUP = 'tests/Kronikol.Tests/History/HistoryRingsMarkupTests.cs'
edit(RINGS_MARKUP, r'''/// the tokens, each with its light value as a fallback for the labs page. In 4.x both charts were bars in a stretched svg.
''', r'''/// the tokens, with no fallback: since S13 the labs page carries the token sheet too. In 4.x both charts were bars in a
/// stretched svg.
''')
edit(RINGS_MARKUP, r'''    /// The verdict pills and the card take their colours from the tokens, each with its light value as a fallback for the
    /// labs page, which carries no token sheet until S13. Every kind keeps a look of its own, where the preview's card rule
    /// drew six kinds as the quiet pill (stylesheet inventory 3.8 item 4), and always-failing keeps the failure's look.
    /// </summary>
    [Fact]
    public void Every_verdict_kind_keeps_its_own_look_and_every_colour_is_a_token_with_a_fallback()
''', r'''    /// The verdict pills and the card take their colours from the tokens, with no fallback: since S13 every page that
    /// carries this sheet carries the token sheet, the labs page too, so a fallback would only hide a missing token. Every
    /// kind keeps a look of its own, where the preview's card rule drew six kinds as the quiet pill (stylesheet inventory
    /// 3.8 item 4), and always-failing keeps the failure's look.
    /// </summary>
    [Fact]
    public void Every_verdict_kind_keeps_its_own_look_and_every_colour_is_a_token()
''')
edit(RINGS_MARKUP, r'''        Assert.DoesNotMatch(@"var\(--[\w-]+\s*\)", css);
''', r'''        Assert.DoesNotMatch(@"var\(--[\w-]+\s*,", css);
''')

RULES = 'tests/Kronikol.Tests/Reports/StylesheetRulesTests.cs'
edit(RULES, r'''    // ── T4: the violet theme's hovers come before its active states (S4) ──
''', r'''    // ── T4: the violet theme's hover edges come before its active states (S4); since 5.0.0 it sets the report's tokens (S13) ──
''')
edit(RULES, r'''    private static readonly string[] IdleHovers =
    [
        ".happy-seg:hover", ".tag-toggle:hover", ".dependency-toggle:hover", ".status-toggle:hover",
        ".export-btn:hover", ".fold-btn:hover", ".percentile-btn:hover", ".timeline-toggle:hover",
        ".details-radio-btn:hover", ".scenario-diagram-controls-toggle:hover", ".diagram-toggle-btn:hover",
        ".iflow-toggle-btn:hover", ".dep-mode-toggle:hover", ".iflow-rel-summary-table tr:hover td",
    ];

    private static readonly string[] ActiveStates =
    [
        ".happy-seg[aria-pressed=\"true\"]", ".tag-toggle.tag-active", ".dependency-toggle.dependency-active", ".status-toggle.status-active",
        ".percentile-btn.percentile-active", ".details-radio-btn.details-active",
        ".timeline-toggle-active", ".timeline-toggle-active:hover", ".iflow-toggle-active", ".iflow-toggle-active:hover",
        ".diagram-toggle-active", ".diagram-toggle-active:hover",
    ];

    [Fact]
    public void Every_violet_idle_hover_precedes_every_violet_active_state()
    {
        var lastIdle = IdleHovers.Max(s =>
        {
            var i = CssRules.IndexOf(Violet, s);
            Assert.True(i >= 0, $"the violet theme has no {s} rule");
            return i;
        });
        foreach (var s in ActiveStates)
        {
            var i = CssRules.IndexOf(Violet, s);
            Assert.True(i >= 0, $"the violet theme has no {s} rule");
            Assert.True(i > lastIdle, $"{s} must follow every idle hover, or a hover of equal specificity shadows it");
        }
    }

    [Fact]
    public void Violet_idle_hovers_use_the_themes_own_tint()
    {
        foreach (var s in IdleHovers)
            Assert.Equal("#ede9fe", CssRules.Value(Violet, s, "background")?.ToLowerInvariant());
    }

    [Fact]
    public void Violet_active_states_are_violet_and_their_hovers_darker()
    {
        Assert.Equal("#8b5cf6", CssRules.Value(Violet, ".percentile-btn.percentile-active", "border-color")?.ToLowerInvariant());
        Assert.Equal("#8b5cf6", CssRules.Value(Violet, ".timeline-toggle-active", "background")?.ToLowerInvariant());
        Assert.Equal("#8b5cf6", CssRules.Value(Violet, ".timeline-toggle-active", "border-color")?.ToLowerInvariant());
        Assert.Equal("#7c3aed", CssRules.Value(Violet, ".timeline-toggle-active:hover", "background")?.ToLowerInvariant());
        Assert.Equal("#7c3aed", CssRules.Value(Violet, ".diagram-toggle-active:hover", "background")?.ToLowerInvariant());
    }

    /// <summary>Q3: the blue the base sheet paints on the scenario's "#" link, its copy-name button and
    /// a search-matched parameterized row. (The row hover and selected tints are not overridden: every
    /// emitted row carries a status class, and each status has its own tints, the same in both themes.)</summary>
    [Fact]
    public void The_violet_theme_recolours_the_blue_the_base_sheet_paints_outside_the_toolbars()
    {
        Assert.Equal("#ede9fe", CssRules.Value(Violet, ".scenario-link:hover", "background")?.ToLowerInvariant());
        Assert.Equal("#ede9fe", CssRules.Value(Violet, ".copy-scenario-name:hover", "background")?.ToLowerInvariant());
        Assert.Equal("inset 4px 0 0 #8b5cf6", CssRules.Value(Violet, ".param-test-table tbody tr.row-search-match", "box-shadow")?.ToLowerInvariant());
    }
''', r'''    // the hovers whose edge the theme sets; their tint is the base sheet's, read from --kron-hover, which the theme sets
    private static readonly string[] HoverEdges =
    [
        ".happy-seg:hover", ".tag-toggle:hover", ".tag-more:hover", ".dependency-toggle:hover", ".status-toggle:hover",
        ".export-btn:hover", ".fold-btn:hover", ".percentile-btn:hover", ".details-radio-btn:hover",
        ".scenario-diagram-controls-toggle:hover", ".tag-mode-toggle:hover", ".dep-mode-toggle:hover",
    ];

    // 5.0.0: the Scenario Timeline toggle is a tab (plans/OPTION_C_PLAN.md S3), so the theme has no timeline-toggle rule
    private static readonly string[] ActiveStates =
    [
        ".happy-seg[aria-pressed=\"true\"]", ".tag-toggle.tag-active", ".dependency-toggle.dependency-active", ".status-toggle.status-active",
        ".percentile-btn.percentile-active", ".details-radio-btn.details-active",
        ".iflow-toggle-active", ".iflow-toggle-active:hover", ".diagram-toggle-active", ".diagram-toggle-active:hover",
    ];

    [Fact]
    public void Every_violet_hover_edge_precedes_every_violet_active_state()
    {
        var lastEdge = HoverEdges.Max(s =>
        {
            var i = CssRules.IndexOf(Violet, s);
            Assert.True(i >= 0, $"the violet theme has no {s} rule");
            return i;
        });
        foreach (var s in ActiveStates)
        {
            var i = CssRules.IndexOf(Violet, s);
            Assert.True(i >= 0, $"the violet theme has no {s} rule");
            Assert.True(i > lastEdge, $"{s} must follow every hover edge, or a hover of equal specificity shadows it");
        }
    }

    /// <summary>Since 5.0.0 a hovered control takes the violet tint from the base sheets' own hover rule, which reads
    /// <c>--kron-hover</c>: the theme sets the token (plans/OPTION_C_PLAN.md S13), and of a hover only its edge.</summary>
    [Fact]
    public void Violet_hovers_take_the_themes_tint_from_its_hover_token()
    {
        Assert.Equal("#ede9fe", CssRules.Value(Violet, ":root", "--kron-hover")?.ToLowerInvariant());
        var sheets = new[] { Base, Popup, DiagramContextMenu.GetCollapsibleNotesStyles() };
        string[] tinted = [.. HoverEdges.Where(s => s != ".scenario-diagram-controls-toggle:hover"),
            ".diagram-toggle-btn:hover", ".iflow-toggle-btn:hover", ".iflow-rel-summary-table tr:hover td", ".scenario-link:hover", ".copy-scenario-name:hover"];
        foreach (var s in tinted)
        {
            var background = sheets.Select(css => CssRules.Value(css, s, "background")).LastOrDefault(v => v is not null);
            Assert.True(background?.StartsWith("var(--kron-hover", StringComparison.Ordinal) == true, $"the base sheets give {s} the background {background ?? "of nothing"}");
            Assert.Null(CssRules.Value(Violet, s, "background"));
        }
        Assert.Equal("var(--violet-edge)", CssRules.Value(Violet, ".happy-seg:hover", "border-color"));
    }

    [Fact]
    public void Violet_active_states_are_violet_and_their_hovers_darker()
    {
        Assert.Equal("#8b5cf6", CssRules.Value(Violet, ":root", "--violet-solid")?.ToLowerInvariant());
        Assert.Equal("#7c3aed", CssRules.Value(Violet, ":root", "--violet-solid-hover")?.ToLowerInvariant());
        Assert.Equal("var(--violet-solid)", CssRules.Value(Violet, ".percentile-btn.percentile-active", "border-color"));
        Assert.Equal("var(--violet-solid)", CssRules.Value(Violet, ".diagram-toggle-active", "background"));
        Assert.Equal("var(--violet-solid-hover)", CssRules.Value(Violet, ".diagram-toggle-active:hover", "background"));
        Assert.Equal("var(--violet-solid-hover)", CssRules.Value(Violet, ".iflow-toggle-active:hover", "background"));
    }

    /// <summary>Q3: the blue the base sheet paints on the scenario's "#" link, its copy-name button and a search-matched
    /// parameterized row. The link and the button read <c>--kron-hover</c>, which the theme sets since 5.0.0 (S13); the row's
    /// edge is the theme's own. (A row's hover and selected tints read <c>--kron-hover</c> and <c>--kron-selected</c>, which
    /// the theme sets too.)</summary>
    [Fact]
    public void The_violet_theme_recolours_the_blue_the_base_sheet_paints_outside_the_toolbars()
    {
        Assert.Equal("var(--kron-hover)", CssRules.Value(Base, ".scenario-link:hover", "background"));
        Assert.Equal("var(--kron-hover)", CssRules.Value(Base, ".copy-scenario-name:hover", "background"));
        Assert.Equal("#ede9fe", CssRules.Value(Violet, ":root", "--kron-hover")?.ToLowerInvariant());
        Assert.Equal("inset 4px 0 0 var(--violet-solid)", CssRules.Value(Violet, ".param-test-table tbody tr.row-search-match", "box-shadow"));
    }
''')

LABS_PAGE = 'tests/Kronikol.Tests.EndToEnd/LabsPageTests.cs'
edit(LABS_PAGE, r'''    [Fact]
    public async Task The_page_runs_no_script()
    {
        await Open("LabsPage_NoScript.html");

        Assert.Equal(0, await Page.Locator("script").CountAsync());
    }
''', r'''    /// <summary>Of the report's scripts the page runs only the theme's: the one that chooses light or dark before the first
    /// style and the one that works the switch on its title line (5.0.0, plans/OPTION_C_PLAN.md S13).</summary>
    [Fact]
    public async Task The_page_runs_only_the_theme_scripts()
    {
        await Open("LabsPage_ThemeScripts.html");

        Assert.Equal(["kronikol-theme-boot", "kronikol-theme-switch"],
            await Page.Locator("script").EvaluateAllAsync<string[]>("scripts => scripts.map(s => s.id)"));
    }
''')

RINGS = 'tests/Kronikol.Tests.EndToEnd/HistoryRingsTests.cs'
edit(RINGS, r'''/// of fifty runs keeping every time legible and opening on the newest; and the labs page, which runs no script, drawing them
/// the same. In 4.x both charts were bars in a stretched svg.
''', r'''/// of fifty runs keeping every time legible and opening on the newest; and the labs page, which runs none of the report's
/// scripts but the theme's, drawing them the same. In 4.x both charts were bars in a stretched svg.
''')
edit(RINGS, r'''    public async Task The_labs_page_draws_the_rings_and_the_timed_bars_with_no_script()
    {
        var labs = HistoryReportHelper.GenerateWithLabsPage(TempDir, OutputDir, "HistoryRings_Labs.html").Page;
        await Page.GotoAsync(labs);
        await Page.Locator("header.labs-header").WaitForAsync();

        Assert.Equal(0, await Page.Locator("script").CountAsync());
''', r'''    public async Task The_labs_page_draws_the_rings_and_the_timed_bars_with_none_of_the_reports_scripts()
    {
        var labs = HistoryReportHelper.GenerateWithLabsPage(TempDir, OutputDir, "HistoryRings_Labs.html").Page;
        await Page.GotoAsync(labs);
        await Page.Locator("header.labs-header").WaitForAsync();

        // the theme's two scripts and none that draws: the rings and the bars are markup (5.0.0, plans/OPTION_C_PLAN.md S13)
        Assert.Equal(["kronikol-theme-boot", "kronikol-theme-switch"],
            await Page.Locator("script").EvaluateAllAsync<string[]>("scripts => scripts.map(s => s.id)"));
''')
edit(RINGS, r'''        // The page carries no token sheet until S13: each colour is the token's light value, through the rule's fallback (a
        // rule with no fallback would paint no stroke at all).
''', r'''        // Since S13 the page carries the token sheet: each colour is the token's, with no fallback (a token the page lacked
        // would paint no stroke at all), and in a light page the light theme's.
''')
edit(RINGS, r'''                return [getComputedStyle(ring.querySelector('.ring-pass')).stroke, probe('var(--kron-pass-ring, #3f9a62)'),
                        getComputedStyle(ring.querySelector('.ring-track')).stroke, probe('var(--kron-track, #edf0f5)'),
                        getComputedStyle(ring.querySelector('.ring-rate')).fill, probe('var(--kron-ink, #242b36)'),
                        ring.querySelector('.ring-rate').textContent];
            }
            """);
        Assert.Equal(paint[1], paint[0]);
''', r'''                return [getComputedStyle(ring.querySelector('.ring-pass')).stroke, probe('var(--kron-pass-ring)'),
                        getComputedStyle(ring.querySelector('.ring-track')).stroke, probe('var(--kron-track)'),
                        getComputedStyle(ring.querySelector('.ring-rate')).fill, probe('var(--kron-ink)'),
                        ring.querySelector('.ring-rate').textContent];
            }
            """);
        Assert.Equal("rgb(63, 154, 98)", paint[0]);
        Assert.Equal(paint[1], paint[0]);
''')

CARD = 'tests/Kronikol.Tests.EndToEnd/DiagnosticsCardTests.cs'
edit(CARD, r'''    // The labs page has no tabs and no token sheet: there the diagnostics stay a disclosure, in their 4.x colours.
    [Fact]
    public async Task On_the_labs_page_the_diagnostics_stay_a_disclosure_in_their_4x_colours()
    {
        ReportTestHelper.GenerateReportWithEverySection(TempDir, OutputDir, "DiagnosticsCard-labs-report.html", labsPageFileName: "DiagnosticsCard-labs.html");
        await Page.GotoAsync(new Uri(Path.Combine(TempDir, "DiagnosticsCard-labs.html")).AbsoluteUri);

        var r = await Page.Locator("details.report-diagnostics > summary").First.EvaluateAsync<string[]>("""
            s => { const cs = getComputedStyle(s); return [cs.listStyleType, cs.cursor, getComputedStyle(s.parentElement).backgroundColor]; }
            """);

        Assert.StartsWith("disclosure", r[0]);
        Assert.Equal("pointer", r[1]);
        Assert.Equal("rgb(255, 251, 243)", r[2]);
    }
''', r'''    // The labs page has no tabs: there the diagnostics stay a disclosure. Since it carries the token sheet (5.0.0,
    // plans/OPTION_C_PLAN.md S13) they are drawn in the theme's tokens there too, where they kept their 4.x colours.
    [Fact]
    public async Task On_the_labs_page_the_diagnostics_stay_a_disclosure_in_the_themes_tokens()
    {
        ReportTestHelper.GenerateReportWithEverySection(TempDir, OutputDir, "DiagnosticsCard-labs-report.html", labsPageFileName: "DiagnosticsCard-labs.html");
        await Page.GotoAsync(new Uri(Path.Combine(TempDir, "DiagnosticsCard-labs.html")).AbsoluteUri);

        var r = await Page.Locator("details.report-diagnostics > summary").First.EvaluateAsync<string[]>("""
            s => {
                const p = document.createElement('div'); p.style.color = 'var(--kron-card)'; document.body.appendChild(p);
                const card = getComputedStyle(p).color; p.remove();
                const cs = getComputedStyle(s); return [cs.listStyleType, cs.cursor, getComputedStyle(s.parentElement).backgroundColor, card];
            }
            """);

        Assert.StartsWith("disclosure", r[0]);
        Assert.Equal("pointer", r[1]);
        Assert.Equal(r[3], r[2]);
        Assert.Equal("rgb(255, 255, 255)", r[2]);
    }
''')

TOOLBAR = 'tests/Kronikol.Tests.EndToEnd/ToolbarStylingTests.cs'
edit(TOOLBAR, r'''        // 5.0.0: the Scenario Timeline toggle is a tab (plans/OPTION_C_PLAN.md S3); a tab not selected takes the hover
        // tint and has no border. The violet theme's tabs are S13's.
        { false, Hover, null, ".report-tab[aria-selected=\"false\"]" },
''', r'''        // 5.0.0: the Scenario Timeline toggle is a tab (plans/OPTION_C_PLAN.md S3); a tab not selected takes the hover
        // tint and has no border. The violet theme's tint is the hover token it sets (S13).
        { false, Hover, null, ".report-tab[aria-selected=\"false\"]" },
        { true, Tint, null, ".report-tab[aria-selected=\"false\"]" },
''')
edit(TOOLBAR, r'''        // 5.0.0: the violet theme paints over the C base, whose idle tab has no fill (S8; the theme is S13's)
''', r'''        // 5.0.0: the violet theme paints over the C base, whose idle tab has no fill (S8), with the tokens it sets (S13)
''')

# TAGS_PLAN T2's fact on the two filter switches: they stay alike under the token theme, which grounds neither
edit(RULES, r"""        Assert.Equal("#F5F3FF", CssRules.Value(Violet, ".dep-mode-toggle", "background"));
        Assert.Equal("#F5F3FF", CssRules.Value(Violet, ".tag-mode-toggle", "background"));
""", r"""        // 5.0.0 (plans/OPTION_C_PLAN.md S13): the theme sets the report's tokens and grounds neither switch, so the base
        // sheet's ground holds for both, and both take the theme's hover edge
        Assert.Equal(CssRules.Value(Violet, ".dep-mode-toggle", "background"), CssRules.Value(Violet, ".tag-mode-toggle", "background"));
        Assert.NotNull(CssRules.Value(Violet, ".tag-mode-toggle:hover", "border-color"));
        Assert.Equal(CssRules.Value(Violet, ".dep-mode-toggle:hover", "border-color"), CssRules.Value(Violet, ".tag-mode-toggle:hover", "border-color"));
""")

# a second run would find the new class names; the copies are checked above
texts = {}
for path, changes in EDITS.items():
    with open(ROOT + path, encoding='utf-8', newline='') as f:
        s = f.read()
    crlf = '\r\n' in s
    for old, new in changes:
        if crlf:
            old, new = old.replace('\n', '\r\n'), new.replace('\n', '\r\n')
        n = s.count(old)
        assert n == 1, (path, n, old[:120])
        s = s.replace(old, new)
    texts[path] = s

sources = {}
for name in copies:
    with open(os.path.join(HERE, name), encoding='utf-8', newline='') as f:
        sources[name] = f.read()

# written only once every check above has held
for path, s in texts.items():
    with open(ROOT + path, 'w', encoding='utf-8', newline='') as f:
        f.write(s)
for name, target in copies.items():
    with open(ROOT + target, 'w', encoding='utf-8', newline='') as f:
        f.write(sources[name])
print('ok: ' + ', '.join(sorted(list(texts) + list(copies.values()))))
