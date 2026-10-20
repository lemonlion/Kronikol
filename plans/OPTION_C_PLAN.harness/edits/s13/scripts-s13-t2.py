# S13's product edits (plans/OPTION_C_PLAN.md S13, the owner's Q3: the token sheet, the theme, and the violet theme as
# token overrides, no layout redesign).
#   src/Kronikol/Reports/LabsReportGenerator.cs   the boot script before the first style, the token sheet first, the
#                                                 report's switch on the title line, the switch script; the remarks
#   src/Kronikol/Reports/ReportGenerator.cs       LoadResource and ThemeSwitchButton internal (two lines, 76 and 95 at
#                                                 d385c715; 75 and 94 at db663d88), and one comment line; nothing else
#   src/Kronikol/Constants/Stylesheets.cs         the violet theme restated on the tokens, light and dark; two summaries
#   src/Kronikol/ReportConfigurationOptions.cs    HtmlSpecificationsCustomStyleSheet's doc comment
#   src/Kronikol/Reports/labs-styles.css          the labs page's parts on the tokens; its table a card
#   src/Kronikol/Reports/history-styles.css       no fallback colours (72), the History card's font, three comments
#   src/Kronikol/Reports/report-diagnostics-styles.css  no fallback colours (12), its comment
#   src/Kronikol/Reports/stylesheets.css          the search help on the tokens (a dark-mode defect found on the way)
# Every anchor is checked to occur exactly once, in memory, before anything is written, and nothing is written unless
# all hold. A second run is refused. S1 (the token sheet, the boot and switch scripts) must be in. BOMs are kept (read
# as ﻿ and written back), and a file read with CRLF gets its new text with CRLF.
# usage: python scripts-s13.py <worktree root>
import os
import re
import sys

ROOT = sys.argv[1].rstrip('/\\') + '/'

LABS = 'src/Kronikol/Reports/LabsReportGenerator.cs'
RG = 'src/Kronikol/Reports/ReportGenerator.cs'
SHEETS = 'src/Kronikol/Constants/Stylesheets.cs'
OPTIONS = 'src/Kronikol/ReportConfigurationOptions.cs'
LABS_CSS = 'src/Kronikol/Reports/labs-styles.css'
HISTORY_CSS = 'src/Kronikol/Reports/history-styles.css'
DIAGNOSTICS_CSS = 'src/Kronikol/Reports/report-diagnostics-styles.css'
BASE_CSS = 'src/Kronikol/Reports/stylesheets.css'
TOKENS_CSS = 'src/Kronikol/Reports/report-tokens.css'

texts = {}
crlf = {}


def read(path):
    if path not in texts:
        with open(ROOT + path, encoding='utf-8', newline='') as f:
            s = f.read()
        crlf[path] = '\r\n' in s
        texts[path] = s.replace('\r\n', '\n') if crlf[path] else s
    return texts[path]


def edit(path, old, new):
    s = read(path)
    n = s.count(old)
    assert n == 1, (path, n, old[:160])
    texts[path] = s.replace(old, new)


# S1 must be in, and S13 must not be
assert os.path.exists(ROOT + TOKENS_CSS), 'no token sheet: S1 is not in this tree'
assert os.path.exists(ROOT + 'src/Kronikol/Reports/report-theme-boot-script.js'), 'no boot script: S1 is not in this tree'
assert os.path.exists(ROOT + 'src/Kronikol/Reports/report-theme-switch-script.js'), 'no switch script: S1 is not in this tree'
assert 'kronikol-theme-boot' not in read(LABS), 'S13 is in already: the labs page loads the boot script'
assert '--violet-solid' not in read(SHEETS), 'S13 is in already: the violet theme has its variables'

# ── the labs page ──

edit(LABS, r'''/// the diagnostic entries. It carries no script: its scenario names link into the report by the <c>#sid-</c> anchor
/// the report resolves on load, and are text when no report is written beside it.
''', r'''/// the diagnostic entries. Of the report's scripts it runs only the theme's (5.0.0, plans/OPTION_C_PLAN.md S13): the boot
/// script chooses light or dark before the first style, as in the report, whose token sheet opens its styles, and the
/// switch on its title line flips it. Its scenario names link into the report by the <c>#sid-</c> anchor the report
/// resolves on load, and are text when no report is written beside it.
''')
edit(LABS, r'''        html.Append("<title>Labs · ").Append(title).Append("</title>\n");
        html.Append("<style>\n")
            .Append(Stylesheets.HtmlReportStyleSheet).Append('\n')
''', r'''        html.Append("<title>Labs · ").Append(title).Append("</title>\n");
        // light or dark before anything is styled, and the token sheet first, as in the report (5.0.0, plans/OPTION_C_PLAN.md S13)
        html.Append("<script id=\"kronikol-theme-boot\">").Append(ReportGenerator.LoadResource("report-theme-boot-script.js")).Append("</script>\n");
        html.Append("<style>\n")
            .Append(Stylesheets.ReportTokensStyleSheet)
            .Append(Stylesheets.HtmlReportStyleSheet).Append('\n')
''')
edit(LABS, r'''        html.Append("<link rel=\"icon\" href=\"").Append(page.CustomFaviconBase64 ?? Constants.DefaultFavicon.DataUri).Append("\">\n");
        html.Append("</head>\n<body>\n");
''', r'''        html.Append("<link rel=\"icon\" href=\"").Append(page.CustomFaviconBase64 ?? Constants.DefaultFavicon.DataUri).Append("\">\n");
        html.Append("<script id=\"kronikol-theme-switch\">").Append(ReportGenerator.LoadResource("report-theme-switch-script.js")).Append("</script>\n");
        html.Append("</head>\n<body>\n");
''')
edit(LABS, r'''        html.Append("<h1>").Append(title).Append(" <span class=\"labs-badge\">Labs</span></h1>");
''', r'''        html.Append("<div class=\"title-line\"><h1>").Append(title).Append(" <span class=\"labs-badge\">Labs</span></h1>")
            .Append(ReportGenerator.ThemeSwitchButton).Append("</div>");
''')

# ── the report generator: two members the labs page uses, internal now ──

edit(RG, r'''    private static string LoadResource(string name) => ResourceCache.GetOrAdd(name, n =>
''', r'''    internal static string LoadResource(string name) => ResourceCache.GetOrAdd(name, n =>
''')
edit(RG, r'''    // the title in step with the theme the boot script chose.
    private const string ThemeSwitchButton =
''', r'''    // the title in step with the theme the boot script chose. The labs page's title line carries it too (S13).
    internal const string ThemeSwitchButton =
''')

# ── the violet theme ──

edit(SHEETS, r'''    /// <summary>The report's colours and type as custom properties, light and dark (5.0.0, plans/OPTION_C_PLAN.md S1). It
    /// opens a report's styles, so every sheet after it, a theme's and a <see cref="ReportConfigurationOptions.CustomCss"/>
    /// sheet included, can use the tokens or set them again.</summary>
''', r'''    /// <summary>The report's colours and type as custom properties, light and dark (5.0.0, plans/OPTION_C_PLAN.md S1). It
    /// opens a report's styles and the labs page's (S13), so every sheet after it, a theme's and a
    /// <see cref="ReportConfigurationOptions.CustomCss"/> sheet included, can use the tokens or set them again.</summary>
''')

VIOLET_OLD = r'''    /// <summary>The violet overlay that is the default <see cref="ReportConfigurationOptions.HtmlSpecificationsCustomStyleSheet"/>:
    /// it recolours <see cref="HtmlReportStyleSheet"/> and the component sheets rather than replacing them, and is
    /// emitted after them. Its idle hover rules come before its active rules, which must win over them.</summary>
    public const string VioletThemeStyleSheet =
        """
                .feature { background-color: #DDD6FE; }
                .features-table-view { background-color: #DDD6FE; }
                .test-execution-summary { background-color: #DDD6FE; }
                .ci-metadata { background-color: #DDD6FE; }
                .filtering-box { background-color: #DDD6FE; }
                .example-diagrams { border-color: #DDD6FE; }

                /* Idle hovers first: an active rule of the same specificity must come after them. */
                .happy-seg:hover,
                .tag-toggle:hover,
                .dependency-toggle:hover,
                .tag-more:hover,
                .status-toggle:hover {
                    background: #EDE9FE;
                    border-color: #A78BFA;
                }

                .export-btn:hover, .fold-btn:hover, .percentile-btn:hover, .timeline-toggle:hover,
                .details-radio-btn:hover, .scenario-diagram-controls-toggle:hover { background: #EDE9FE; border-color: #A78BFA; }
                .diagram-toggle-btn:hover, .iflow-toggle-btn:hover { background: #EDE9FE; }
                .dep-mode-toggle:hover, .tag-mode-toggle:hover { background: #EDE9FE; border-color: #A78BFA; }
                .iflow-rel-summary-table tr:hover td { background: #EDE9FE; }
                .scenario-link:hover, .copy-scenario-name:hover { background: #EDE9FE; }

                .happy-seg[aria-pressed="true"],
                .tag-toggle.tag-active,
                .dependency-toggle.dependency-active,
                .status-toggle.status-active {
                    background: #8B5CF6;
                    color: white;
                    border-color: #8B5CF6;
                }
                .percentile-btn.percentile-active {
                    background: #8B5CF6;
                    color: white;
                    border-color: #8B5CF6;
                }
                .timeline-toggle-active { background: #8B5CF6; color: white; border-color: #8B5CF6; }
                .timeline-toggle-active:hover { background: #7C3AED; }

                .dep-mode-toggle, .tag-mode-toggle { background: #F5F3FF; }
                .scenario-focused { outline-color: #8B5CF6; }
                .step-attachment { color: #8B5CF6; }
                .attachment-image { border-color: #A78BFA; }
                .attachment-image-name { color: #A78BFA; }

                .details-radio-btn.details-active {
                    background: #8B5CF6;
                    color: white;
                    border-color: #8B5CF6;
                }

                .iflow-toggle-active { background: #8B5CF6; color: #fff; border-color: #8B5CF6; }
                .iflow-toggle-active:hover { background: #7C3AED; }
                .diagram-toggle-active { background: #8B5CF6; color: #fff; border-color: #8B5CF6; }
                .diagram-toggle-active:hover { background: #7C3AED; }
                .iflow-rel-list li:hover { background: #EDE9FE; border-color: #8B5CF6; }

                .step-status.passed { background: #8B5CF6; }
                .step-status.bypassed { background: #DDD6FE; color: #5B21B6; }
                .step-status.passed-bypassed { background: #DDD6FE; color: #5B21B6; }

                .rule { border-left-color: #8B5CF6; }
                span.label { background-color: #C4B5FD; }
                #searchbar { border-color: #C4B5FD; }
                .search-help-toggle { border-color: #C4B5FD; color: #C4B5FD; }
                .search-help-toggle:hover { background: #3B2F63; color: #E9D5FF; }
                .search-help-panel { border-color: #5B21B6; background: #1E1534; }
                .search-help-table th { border-bottom-color: #5B21B6; }
                .search-help-table td { border-bottom-color: #2E2048; }
                .search-help-table code { }
                .search-help-note { color: #A78BFA; }
                .search-help-note kbd { border-color: #5B21B6; }
                .sub-steps { border-left-color: #DDD6FE; }
                .feature-summary-table th { background: #F5F3FF; }
                .param-success { background: #EDE9FE; }
                .param-test-table tbody tr.row-search-match { box-shadow: inset 4px 0 0 #8B5CF6; }
                .duration-fast { background: #EDE9FE; color: #5B21B6; }
                @media (max-width: 768px) {
                    .filter-search { background: #DDD6FE; }
                }
                .back-to-top { background: #8B5CF6; }
                .back-to-top:hover { background: #7C3AED; }
        """;
'''

# The dark values: each is the token sheet's own dark value (report-tokens.css 117-214) turned, in OKLCH, from its hue to
# the theme's (292.7, the hue of #8b5cf6) at the same lightness and chroma, so the contrasts the token sheet was audited
# at hold; --violet-rail's is the dark --kron-hairline turned. The solids step one shade darker in the dark theme
# (violet-600 and -700), white on them 5.70:1 and 7.10:1. Measured by checks/violet-palette.py.
DARK = '''--kron-accent: #a998eb;
--kron-accent-rail: rgba(169, 152, 235, .30);
--kron-hover: #2b2837;
--kron-selected: #342b51;
--kron-selected-ink: #cac0f6;
--kron-selected-edge: #8774c8;
--kron-field: #272431;
--kron-strip: #22202b;
--kron-pass: #b7a3ff;
--kron-pass-soft: #2a253e;
--kron-bypass: #b3a4f0;
--kron-bypass-soft: #2c2543;
--violet-card: #1d1b25;
--violet-rail: #3e3b4c;
--violet-edge: #a78bfa;
--violet-solid: #7c3aed;
--violet-solid-hover: #6d28d9;'''


def indent(block, spaces):
    return '\n'.join(' ' * spaces + line for line in block.split('\n'))


VIOLET_NEW = r'''    /// <summary>The violet theme, the default <see cref="ReportConfigurationOptions.HtmlSpecificationsCustomStyleSheet"/>
    /// (5.0.0, plans/OPTION_C_PLAN.md S13): it sets the report's colour tokens again, light and dark, in the blocks the
    /// token sheet uses, and paints in variables of its own what no token carries: the violet cards, and the solid violet
    /// of a chosen control, kept from 4.x under the ink made for it. It is emitted after the built-in sheets, and its hover
    /// edges come before its chosen states, which must win over them.</summary>
    public const string VioletThemeStyleSheet =
        """
                /* The violet theme (5.0.0, plans/OPTION_C_PLAN.md S13). The report's tokens, light on :root and dark on
                   :root[data-theme="dark"] and again under prefers-color-scheme, as the token sheet has them: each dark value
                   is the token sheet's own turned to the theme's hue at the same lightness, and the two dark blocks must stay
                   identical. Then the theme's own variables, for what no token carries. */
                :root {
                    --kron-accent: #6d28d9;
                    --kron-accent-rail: rgba(109, 40, 217, .18);
                    --kron-hover: #ede9fe;
                    --kron-selected: #c4b5fd;
                    --kron-selected-ink: #5b21b6;
                    --kron-selected-edge: #8b5cf6;
                    --kron-field: #f5f3ff;
                    --kron-strip: #f5f3ff;
                    --kron-pass: #5b21b6;
                    --kron-pass-soft: #ede9fe;
                    --kron-bypass: #5b21b6;
                    --kron-bypass-soft: #ddd6fe;
                    --violet-card: #ddd6fe;
                    --violet-rail: #a78bfa;
                    --violet-edge: #a78bfa;
                    --violet-solid: #8b5cf6;
                    --violet-solid-hover: #7c3aed;
                }
                :root[data-theme="dark"] {
''' + indent(DARK, 20) + r'''
                }
                @media (prefers-color-scheme: dark) {
                    :root:not([data-theme]) {
''' + indent(DARK, 24) + r'''
                    }
                }

                .feature, .features-table-view, .filtering-box { background-color: var(--violet-card); }
                @media (max-width: 768px) {
                    .filter-search { background: var(--violet-card); }
                }
                .sub-steps { border-left-color: var(--violet-rail); }
                .attachment-image { border-color: var(--violet-rail); }
                .attachment-image-name { color: var(--kron-selected-ink); }
                span.label { background-color: var(--kron-selected); border-color: var(--kron-selected); color: var(--kron-selected-ink); }
                #searchbar { border-color: var(--kron-selected-edge); }

                /* Hover edges first: a chosen state of the same specificity must come after them. A hover's tint is the
                   base sheet's, from --kron-hover. */
                .happy-seg:hover,
                .tag-toggle:hover,
                .dependency-toggle:hover,
                .status-toggle:hover,
                .tag-more:hover,
                .export-btn:hover, .fold-btn:hover, .percentile-btn:hover, .details-radio-btn:hover,
                .scenario-diagram-controls-toggle:hover, .tag-mode-toggle:hover, .dep-mode-toggle:hover {
                    border-color: var(--violet-edge);
                }
                .iflow-rel-list li:hover { border-color: var(--violet-solid); }

                .happy-seg[aria-pressed="true"],
                .tag-toggle.tag-active,
                .dependency-toggle.dependency-active,
                .status-toggle.status-active,
                .percentile-btn.percentile-active,
                .details-radio-btn.details-active {
                    background: var(--violet-solid);
                    color: var(--kron-on-solid);
                    border-color: var(--violet-solid);
                }
                .details-radio-btn.toggle-btn.details-active::before { background: var(--kron-on-solid); border-color: var(--kron-on-solid); }
                .iflow-toggle-active, .diagram-toggle-active { background: var(--violet-solid); color: var(--kron-on-solid); border-color: var(--violet-solid); }
                .iflow-toggle-active:hover, .diagram-toggle-active:hover { background: var(--violet-solid-hover); color: var(--kron-on-solid); }

                .step-status.passed { background: var(--violet-solid); color: var(--kron-on-solid); }
                .param-test-table tbody tr.row-search-match { box-shadow: inset 4px 0 0 var(--violet-solid); }
                .back-to-top { background: var(--violet-solid); border-color: var(--violet-solid); color: var(--kron-on-solid); }
                .back-to-top:hover { background: var(--violet-solid-hover); }
        """;
'''
edit(SHEETS, VIOLET_OLD, VIOLET_NEW)

edit(OPTIONS, r'''    /// <see cref="CustomCss"/>. Default: <see cref="Stylesheets.VioletThemeStyleSheet"/>, a violet overlay on the built-in
    /// sheets; <c>null</c> leaves the report in the built-in blue.</summary>
''', r'''    /// <see cref="CustomCss"/>. Default: <see cref="Stylesheets.VioletThemeStyleSheet"/>, the violet theme, which sets the
    /// report's colour tokens for the light and the dark theme; <c>null</c> leaves the report in the built-in blue.</summary>
''')

# ── the labs page's own sheet ──

LABS_CSS_OLD = r'''        /* The labs page beside the report: its header, and the table of every scenario's history */
        .labs-header h1 {
            overflow-wrap: anywhere;
        }
        .labs-badge {
            display: inline-block;
            vertical-align: middle;
            font-size: 0.45em;
            font-weight: 600;
            padding: 0.2em 0.7em;
            border-radius: 1em;
            background: rgb(255, 243, 205);
            color: rgb(122, 92, 0);
            border: 1px solid rgb(232, 200, 110);
        }
        .labs-about, .labs-meta {
            color: rgb(90, 90, 90);
            overflow-wrap: anywhere;
        }
        .labs-meta code {
            overflow-wrap: anywhere;
        }
        .labs-scenarios {
            margin: 1em 0;
        }
        .labs-table-wrap {
            max-width: 100%;
            overflow-x: auto;
        }
        .labs-scenario-table {
            border-collapse: collapse;
            width: 100%;
            font-size: 0.9em;
        }
        .labs-scenario-table th, .labs-scenario-table td {
            text-align: left;
            vertical-align: top;
            padding: 0.3em 0.6em;
            border-bottom: 1px solid rgb(225, 225, 225);
            overflow-wrap: anywhere;
        }
        .labs-scenario-table .labs-feature th {
            background: rgb(245, 245, 245);
        }
        .labs-stable {
            color: rgb(120, 120, 120);
        }
'''
LABS_CSS_NEW = r'''        /* The labs page beside the report, on the report's tokens (5.0.0, plans/OPTION_C_PLAN.md S13): its header, whose
           title line carries the report's theme switch, and the table of every scenario's history, a card as the History
           section and the diagnostics are, since its rules would vanish on the page's ground (1.07:1) */
        .labs-header h1 {
            overflow-wrap: anywhere;
        }
        .labs-badge {
            display: inline-block;
            vertical-align: middle;
            font-size: var(--kron-fs-label);
            font-weight: 600;
            padding: 0.2em 0.7em;
            border-radius: 1em;
            background: var(--kron-warn-soft);
            color: var(--kron-warn);
            border: 1px solid var(--kron-warn-edge);
        }
        .labs-about, .labs-meta {
            color: var(--kron-muted);
            overflow-wrap: anywhere;
        }
        .labs-meta code {
            overflow-wrap: anywhere;
        }
        .labs-scenarios {
            margin: 1em 0;
            border: 1px solid var(--kron-hairline);
            border-radius: 12px;
            background: var(--kron-card);
            box-shadow: var(--kron-card-shadow);
            overflow: hidden;
        }
        .labs-scenarios > summary {
            padding: 12px 16px;
        }
        .labs-scenarios[open] > summary {
            border-bottom: 1px solid var(--kron-hairline-soft);
        }
        .labs-table-wrap {
            max-width: 100%;
            box-sizing: border-box;
            padding: 0 8px 8px;
            overflow-x: auto;
        }
        .labs-scenario-table {
            border-collapse: collapse;
            width: 100%;
            font-size: 0.9em;
        }
        .labs-scenario-table th, .labs-scenario-table td {
            text-align: left;
            vertical-align: top;
            padding: 0.3em 0.6em;
            border-bottom: 1px solid var(--kron-hairline-soft);
            overflow-wrap: anywhere;
        }
        .labs-scenario-table .labs-feature th {
            background: var(--kron-strip);
        }
        .labs-stable {
            color: var(--kron-faint);
        }
'''
assert read(LABS_CSS) == LABS_CSS_OLD, 'labs-styles.css is not the sheet this script was written against'
texts[LABS_CSS] = LABS_CSS_NEW

# ── the sheets the labs page shares with the report: no fallback colours ──


def strip_fallbacks(css):
    """Every var(--name, fallback) becomes var(--name), the fallback read to its own closing parenthesis, so a fallback
    holding rgba(...) or a whole shadow list goes whole. Returns the sheet and how many were stripped."""
    out, i, n = [], 0, 0
    pattern = re.compile(r'var\((--[\w-]+)\s*,')
    while True:
        m = pattern.search(css, i)
        if not m:
            out.append(css[i:])
            return ''.join(out), n
        out.append(css[i:m.start()])
        depth, j = 0, m.start() + len('var')
        while True:
            if css[j] == '(':
                depth += 1
            elif css[j] == ')':
                depth -= 1
                if depth == 0:
                    break
            j += 1
        out.append(f'var({m.group(1)})')
        n += 1
        i = j + 1


edit(HISTORY_CSS, r'''           item 4). Every token in this sheet falls back to its light value: the labs page has no token sheet until S13. */
''', r'''           item 4). Every page that carries this sheet carries the token sheet, the labs page too since S13, so no token here
           falls back to a value of its own. */
''')
edit(HISTORY_CSS, r'''           from c-round7.css and its shadow from c-round4.css): the heading over a hairline, then the body. It sets its own
           font, for the labs page, where the base sheet's font token resolves to nothing. */
''', r'''           from c-round7.css and its shadow from c-round4.css): the heading over a hairline, then the body. */
''')
edit(HISTORY_CSS, r'''            font-family: var(--kron-font, -apple-system, BlinkMacSystemFont, "Segoe UI", "Noto Sans", Roboto, "Helvetica Neue", Arial, sans-serif);
''', '')
edit(HISTORY_CSS, r'''        /* The run trend (5.0.0; the owner's round 17 items 29 and 30), written by HistoryHtml as markup, so the labs page,
           which runs no script, draws it too: a ring per run, and a bar per run carrying its time */
''', r'''        /* The run trend (5.0.0; the owner's round 17 items 29 and 30), written by HistoryHtml as markup, so the labs page,
           which runs none of the report's scripts but the theme's, draws it too: a ring per run, and a bar per run carrying
           its time */
''')
texts[HISTORY_CSS], stripped = strip_fallbacks(texts[HISTORY_CSS])
assert stripped == 71, ('history-styles.css: fallbacks stripped', stripped)

edit(DIAGNOSTICS_CSS, r'''           and a degraded capture amber. Each colour falls back to its 4.x value where a page has no token sheet (the
           labs page, where the heading stays a disclosure). */
''', r'''           and a degraded capture amber. Every page that carries this sheet carries the token sheet, the labs page too
           since S13, where the heading stays a disclosure. */
''')
texts[DIAGNOSTICS_CSS], stripped = strip_fallbacks(texts[DIAGNOSTICS_CSS])
assert stripped == 12, ('report-diagnostics-styles.css: fallbacks stripped', stripped)

# ── the search help: 4.x's light greys, which the dark theme drew under its light ink ──

edit(BASE_CSS, r'''        .search-help-toggle {
            flex-shrink: 0;
            width: 1.6em;
            height: 1.6em;
            border-radius: 50%;
            border: 1px solid rgb(180, 180, 180);
            background: transparent;
            cursor: pointer;
            font-weight: bold;
            font-size: 0.9em;
            line-height: 1;
            color: rgb(100, 100, 100);
            display: flex;
            align-items: center;
            justify-content: center;
        }
        .search-help-toggle:hover {
            background: rgb(240, 240, 240);
            color: rgb(50, 50, 50);
        }

        .search-help-panel {
            margin-top: 0.5em;
            padding: 0.8em 1em;
            border: 1px solid rgb(200, 200, 200);
            border-radius: 0.5em;
            background: rgb(250, 250, 250);
            font-size: 0.85em;
            line-height: 1.6;
            overflow-x: auto;
        }
        .search-help-table {
            width: 100%;
            border-collapse: collapse;
            margin-bottom: 0.5em;
        }
        .search-help-table th {
            text-align: left;
            border-bottom: 1px solid rgb(200, 200, 200);
            padding: 0.3em 0.6em;
            font-size: 0.9em;
        }
        .search-help-table td {
            padding: 0.25em 0.6em;
            border-bottom: 1px solid rgb(230, 230, 230);
        }
        .search-help-table code {
            background: rgb(230, 230, 230);
            padding: 0.1em 0.3em;
            border-radius: 3px;
            font-size: 0.95em;
        }
        .search-help-note {
            margin: 0.4em 0 0;
            color: rgb(100, 100, 100);
            font-size: 0.9em;
        }
        .search-help-note kbd {
            background: rgb(230, 230, 230);
            border: 1px solid rgb(200, 200, 200);
            border-radius: 3px;
            padding: 0.1em 0.4em;
            font-size: 0.9em;
        }
''', r'''        /* The search help, on the theme's tokens (5.0.0, plans/OPTION_C_PLAN.md S13), where its 4.x greys drew a light
           panel under the dark theme's light ink: the preview's colours (c-extra.css 44-47, c-round5.css 166-169), but
           for the code, which keeps its chip */
        .search-help-toggle {
            flex-shrink: 0;
            width: 1.6em;
            height: 1.6em;
            border-radius: 50%;
            border: 1px solid var(--kron-control-border);
            background: transparent;
            cursor: pointer;
            font-weight: bold;
            font-size: 0.9em;
            line-height: 1;
            color: var(--kron-muted);
            display: flex;
            align-items: center;
            justify-content: center;
        }
        .search-help-toggle:hover {
            background: var(--kron-hover);
            color: var(--kron-ink);
        }

        .search-help-panel {
            margin-top: 0.5em;
            padding: 0.8em 1em;
            border: 1px solid var(--kron-strip-edge);
            border-radius: 0.5em;
            background: var(--kron-strip);
            font-size: 0.85em;
            line-height: 1.6;
            overflow-x: auto;
        }
        .search-help-table {
            width: 100%;
            border-collapse: collapse;
            margin-bottom: 0.5em;
        }
        .search-help-table th {
            text-align: left;
            border-bottom: 1px solid var(--kron-hairline);
            padding: 0.3em 0.6em;
            font-size: 0.9em;
        }
        .search-help-table td {
            padding: 0.25em 0.6em;
            border-bottom: 1px solid var(--kron-hairline-soft);
        }
        .search-help-table code {
            background: var(--kron-code-bg);
            padding: 0.1em 0.3em;
            border-radius: 3px;
            font-size: 0.95em;
        }
        .search-help-note {
            margin: 0.4em 0 0;
            color: var(--kron-muted);
            font-size: 0.9em;
        }
        .search-help-note kbd {
            background: var(--kron-code-bg);
            border: 1px solid var(--kron-control-border);
            border-radius: 3px;
            padding: 0.1em 0.4em;
            font-size: 0.9em;
            color: var(--kron-ink);
        }
''')

# written only once every check above has held
for path, s in texts.items():
    if crlf[path]:
        s = s.replace('\n', '\r\n')
    with open(ROOT + path, 'w', encoding='utf-8', newline='') as f:
        f.write(s)
print('ok: ' + ', '.join(sorted(texts)))
