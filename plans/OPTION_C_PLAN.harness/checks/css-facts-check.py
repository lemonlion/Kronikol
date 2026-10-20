# S13's stylesheet facts, read the way the core tests read them, without a build: CssRules (tests/Kronikol.Tests/Reports/
# CssRules.cs) rewritten in Python, the violet theme taken out of Constants/Stylesheets.cs as the C# raw string literal
# gives it, and each fact of ThemedPagesHtmlTests, StylesheetRulesTests' T4 and HistoryRingsMarkupTests that reads only
# sheets, asserted as written. It runs on a tree before and after scripts-s13.py: before, every fact S13 adds or turns
# round fails; after, all pass. The labs page's markup facts need the generator and are left to the core run.
# usage: python css-facts-check.py [<root> [<root for the sheets it only reads>]]   (default: this checkout; prints one
#        line per fact and exits 0 when all pass; a sheet missing under <root> is read from the second root)
import os
import re
import sys

ROOT = (sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), '..', '..', '..')).rstrip('/\\') + '/'
REF = (sys.argv[2].rstrip('/\\') + '/') if len(sys.argv) > 2 else ROOT


def load(path):
    with open((ROOT if os.path.exists(ROOT + path) else REF) + path, encoding='utf-8-sig', newline='') as f:
        return f.read()


def raw_literal(cs, name):
    """The value of a C# raw string literal const: the lines between the delimiters, each less the closing line's indent."""
    m = re.search(name + r' =\s*\n(\s*)"""\n(.*?)\n(\s*)"""', cs, re.S)
    indent = len(m.group(3))
    return '\n'.join(line[indent:] if line.strip() else '' for line in m.group(2).split('\n'))


def norm(s):
    return re.sub(r'\s+', ' ', s).strip()


def matching(css, i):
    depth = 0
    for j in range(i, len(css)):
        if css[j] == '{':
            depth += 1
        elif css[j] == '}':
            depth -= 1
            if depth == 0:
                return j
    raise ValueError('unbalanced brace at %d' % i)


def selector_list(prelude):
    out, depth, start = [], 0, 0
    for i, c in enumerate(prelude):
        if c in '([':
            depth += 1
        elif c in ')]':
            depth -= 1
        elif c == ',' and depth == 0:
            out.append(norm(prelude[start:i]))
            start = i + 1
    out.append(norm(prelude[start:]))
    return out


def declarations(body):
    d = {}
    for part in body.split(';'):
        if ':' not in part:
            continue
        k, v = part.split(':', 1)
        d[norm(k).lower()] = norm(v)
    return d


def parse(css):
    text = re.sub(r'/\*.*?\*/', '', css, flags=re.S)
    rules = []

    def block(start, end, media):
        i = start
        while i < end:
            o = text.find('{', i, end)
            if o < 0:
                break
            prelude = norm(text[i:o])
            c = matching(text, o)
            if prelude.startswith('@media'):
                block(o + 1, c, norm(prelude[len('@media'):]))
            elif prelude and not prelude.startswith('@'):
                rules.append({'index': len(rules), 'media': media, 'selectors': selector_list(prelude), 'decl': declarations(text[o + 1:c])})
            i = c + 1

    block(0, len(text), None)
    return rules


def rules_for(css, selector, media=None):
    return [r for r in parse(css) if r['media'] == media and selector in r['selectors']]


def value(css, selector, prop, media=None):
    vals = [r['decl'].get(prop) for r in rules_for(css, selector, media)]
    vals = [v for v in vals if v is not None]
    return vals[-1] if vals else None


def index_of(css, selector):
    rs = rules_for(css, selector)
    return rs[0]['index'] if rs else -1


LITERAL = re.compile(r'#[0-9a-fA-F]{3,8}\b|\b(?:rgba?|hsla?|oklch|lab|lch)\(|\b(?:white|black)\b', re.I)

sheets = load('src/Kronikol/Constants/Stylesheets.cs')
VIOLET = raw_literal(sheets, 'VioletThemeStyleSheet')
BASE = load('src/Kronikol/Reports/stylesheets.css')
TOKENS = load('src/Kronikol/Reports/report-tokens.css')
HISTORY = load('src/Kronikol/Reports/history-styles.css')
DIAGNOSTICS = load('src/Kronikol/Reports/report-diagnostics-styles.css')
LABS = load('src/Kronikol/Reports/labs-styles.css')
POPUP = load('src/Kronikol/Reports/internal-flow-popup-styles.css')
NOTES = load('src/Kronikol/Reports/collapsible-notes-styles.css')


class Fail(Exception):
    pass


def check(cond, message):
    if not cond:
        raise Fail(message)


def block_of(css, selector, media=None):
    rs = rules_for(css, selector, media)
    check(len(rs) == 1, f'{len(rs)} rules for {selector}' + ('' if media is None else ' in @media ' + media))
    return {k: v.lower() for k, v in rs[0]['decl'].items()}


FACTS = []


def fact(name):
    def wrap(f):
        FACTS.append((name, f))
        return f
    return wrap


@fact('ThemedPagesHtmlTests.The_labs_pages_own_parts_are_drawn_in_the_tokens')
def _():
    check(value(LABS, '.labs-badge', 'background') == 'var(--kron-warn-soft)', 'badge background')
    check(value(LABS, '.labs-badge', 'color') == 'var(--kron-warn)', 'badge colour')
    check(value(LABS, '.labs-badge', 'border') == '1px solid var(--kron-warn-edge)', 'badge border')
    check(value(LABS, '.labs-badge', 'font-size') == 'var(--kron-fs-label)', 'badge size')
    check(value(LABS, '.labs-about', 'color') == 'var(--kron-muted)', 'about')
    check(value(LABS, '.labs-meta', 'color') == 'var(--kron-muted)', 'meta')
    check(value(LABS, '.labs-scenarios', 'background') == 'var(--kron-card)', 'table card')
    check(value(LABS, '.labs-scenarios', 'border') == '1px solid var(--kron-hairline)', 'table card edge')
    check(value(LABS, '.labs-scenario-table td', 'border-bottom') == '1px solid var(--kron-hairline-soft)', 'row rule')
    check(value(LABS, '.labs-scenario-table .labs-feature th', 'background') == 'var(--kron-strip)', 'feature row')
    check(value(LABS, '.labs-stable', 'color') == 'var(--kron-faint)', 'stable')
    for r in parse(LABS):
        for k, v in r['decl'].items():
            check(not LITERAL.search(v), f"{r['selectors'][0]} {{ {k}: {v} }}")


for name, css in (('history-styles.css', HISTORY), ('report-diagnostics-styles.css', DIAGNOSTICS)):
    @fact(f'ThemedPagesHtmlTests.A_sheet_carried_only_beside_the_token_sheet_has_no_colour_of_its_own_to_fall_back_on({name})')
    def _(name=name, css=css):
        text = re.sub(r'/\*.*?\*/', '', css, flags=re.S)
        check('var(--kron-card)' in text, 'no var(--kron-card)')
        found = re.findall(r'var\(--[\w-]+\s*,[^)]*\)?', text)
        check(not found, f'{len(found)} fallbacks, the first {found[:1]}')


@fact('ThemedPagesHtmlTests.The_violet_theme_sets_the_reports_tokens_for_the_light_and_the_dark_theme')
def _():
    light = block_of(VIOLET, ':root')
    dark = block_of(VIOLET, ':root[data-theme="dark"]')
    system = block_of(VIOLET, ':root:not([data-theme])', '(prefers-color-scheme: dark)')
    for k, v in {'--kron-hover': '#ede9fe', '--kron-accent': '#6d28d9', '--violet-card': '#ddd6fe', '--violet-solid': '#8b5cf6',
                 '--violet-solid-hover': '#7c3aed'}.items():
        check(light.get(k) == v, f'light {k} {light.get(k)}')
    check(sorted(light) == sorted(dark), 'light and dark names differ')
    check(dark == system, 'the two dark blocks differ')
    for k, v in {'--kron-hover': '#2b2837', '--kron-accent': '#a998eb', '--violet-card': '#1d1b25', '--violet-solid': '#7c3aed',
                 '--violet-solid-hover': '#6d28d9'}.items():
        check(dark.get(k) == v, f'dark {k} {dark.get(k)}')
    tokens = set(block_of(TOKENS, ':root'))
    check(any(k.startswith('--kron-') for k in light), 'no --kron- token')
    for k in light:
        check(k.startswith('--violet-') or k in tokens, f'{k} is no token of the token sheet')


@fact('ThemedPagesHtmlTests.Every_colour_the_violet_theme_draws_outside_its_token_blocks_is_a_variable')
def _():
    rules = [r for r in parse(VIOLET) if not any(s.startswith(':root') for s in r['selectors'])]
    check(len(rules) >= 10, f'{len(rules)} rules')
    for r in rules:
        for k, v in r['decl'].items():
            check(not LITERAL.search(v) and 'var(--' in v, f"{r['selectors'][0]} {{ {k}: {v} }}")


@fact('ThemedPagesHtmlTests.Every_solid_violet_the_theme_paints_carries_the_ink_made_for_it')
def _():
    solid = [r for r in parse(VIOLET) if any(k in ('background', 'background-color') and v.startswith('var(--violet-solid') for k, v in r['decl'].items())]
    check(len(solid) >= 6, f'{len(solid)} rules paint a solid violet')
    for r in solid:
        if all(s.startswith('.back-to-top:hover') for s in r['selectors']):
            continue
        check(r['decl'].get('color') == 'var(--kron-on-solid)', f"{r['selectors'][0]} under {r['decl'].get('color')}")
    check(value(VIOLET, '.details-radio-btn.toggle-btn.details-active::before', 'background') == 'var(--kron-on-solid)', 'radio dot')


@fact('ThemedPagesHtmlTests.The_search_help_is_drawn_in_the_tokens_and_the_violet_leaves_it_to_them')
def _():
    for sel in ['.search-help-toggle', '.search-help-toggle:hover', '.search-help-panel', '.search-help-table th', '.search-help-table td',
                '.search-help-table code', '.search-help-note', '.search-help-note kbd']:
        rs = rules_for(BASE, sel)
        check(rs, f'no {sel} rule')
        for r in rs:
            for k, v in r['decl'].items():
                check(not LITERAL.search(v), f'{sel} {{ {k}: {v} }}')
    check(value(BASE, '.search-help-panel', 'background') == 'var(--kron-strip)', 'panel')
    check(value(BASE, '.search-help-panel', 'border') == '1px solid var(--kron-strip-edge)', 'panel edge')
    check(value(BASE, '.search-help-table code', 'background') == 'var(--kron-code-bg)', 'code')
    check(value(BASE, '.search-help-toggle:hover', 'background') == 'var(--kron-hover)', 'toggle hover')
    check(not any(s.startswith('.search-help') for r in parse(VIOLET) for s in r['selectors']), 'the violet still has search help rules')


FILTER_TOGGLES = ['tag', 'dependency', 'status']
HOVER_EDGES = ['.happy-seg:hover', '.tag-toggle:hover', '.dependency-toggle:hover', '.status-toggle:hover', '.tag-more:hover',
               '.export-btn:hover', '.fold-btn:hover', '.percentile-btn:hover', '.details-radio-btn:hover',
               '.scenario-diagram-controls-toggle:hover', '.tag-mode-toggle:hover', '.dep-mode-toggle:hover']
ACTIVE = ['.happy-seg[aria-pressed="true"]', '.tag-toggle.tag-active', '.dependency-toggle.dependency-active', '.status-toggle.status-active',
          '.percentile-btn.percentile-active', '.details-radio-btn.details-active',
          '.iflow-toggle-active', '.iflow-toggle-active:hover', '.diagram-toggle-active', '.diagram-toggle-active:hover']


@fact('StylesheetRulesTests.A_violet_filter_toggle_keeps_its_active_colour_while_hovered (unchanged)')
def _():
    for t in FILTER_TOGGLES:
        hover, active = index_of(VIOLET, f'.{t}-toggle:hover'), index_of(VIOLET, f'.{t}-toggle.{t}-active')
        check(hover >= 0 and active >= 0 and hover < active, t)


@fact('StylesheetRulesTests.Every_violet_hover_edge_precedes_every_violet_active_state')
def _():
    last = -1
    for s in HOVER_EDGES:
        i = index_of(VIOLET, s)
        check(i >= 0, f'no {s}')
        last = max(last, i)
    for s in ACTIVE:
        i = index_of(VIOLET, s)
        check(i >= 0 and i > last, f'{s} at {i}, the last edge at {last}')


@fact('StylesheetRulesTests.Violet_hovers_take_the_themes_tint_from_its_hover_token')
def _():
    check(value(VIOLET, ':root', '--kron-hover') == '#ede9fe', 'the hover token')
    tinted = [s for s in HOVER_EDGES if s != '.scenario-diagram-controls-toggle:hover'] + [
        '.diagram-toggle-btn:hover', '.iflow-toggle-btn:hover', '.iflow-rel-summary-table tr:hover td', '.scenario-link:hover', '.copy-scenario-name:hover']
    for s in tinted:
        bg = [v for v in (value(css, s, 'background') for css in (BASE, POPUP, NOTES)) if v is not None]
        check(bg and bg[-1].startswith('var(--kron-hover'), f'{s}: {bg}')
        check(value(VIOLET, s, 'background') is None, f'the violet paints {s}')
    check(value(VIOLET, '.happy-seg:hover', 'border-color') == 'var(--violet-edge)', 'edge')


@fact('StylesheetRulesTests.Violet_active_states_are_violet_and_their_hovers_darker')
def _():
    check(value(VIOLET, ':root', '--violet-solid') == '#8b5cf6', 'solid')
    check(value(VIOLET, ':root', '--violet-solid-hover') == '#7c3aed', 'solid hover')
    check(value(VIOLET, '.percentile-btn.percentile-active', 'border-color') == 'var(--violet-solid)', 'percentile')
    check(value(VIOLET, '.diagram-toggle-active', 'background') == 'var(--violet-solid)', 'tab')
    check(value(VIOLET, '.diagram-toggle-active:hover', 'background') == 'var(--violet-solid-hover)', 'tab hover')
    check(value(VIOLET, '.iflow-toggle-active:hover', 'background') == 'var(--violet-solid-hover)', 'popup hover')


@fact('StylesheetRulesTests.The_violet_theme_recolours_the_blue_the_base_sheet_paints_outside_the_toolbars')
def _():
    check(value(BASE, '.scenario-link:hover', 'background') == 'var(--kron-hover)', 'link')
    check(value(BASE, '.copy-scenario-name:hover', 'background') == 'var(--kron-hover)', 'copy')
    check(value(VIOLET, ':root', '--kron-hover') == '#ede9fe', 'hover token')
    check(value(VIOLET, '.param-test-table tbody tr.row-search-match', 'box-shadow') == 'inset 4px 0 0 var(--violet-solid)', 'row')


@fact('StylesheetRulesTests.Every_selector_in_the_built_in_sheets_is_well_formed (the violet)')
def _():
    sel = re.compile(r'^(?:\s*[>+~]\s*|\s+|\*|[a-zA-Z][\w-]*|\.[\w-]+|#[\w-]+|\[[^\[\]]*\]|::?[\w-]+(?:\((?:[^()]|\([^()]*\))*\))?)+$')
    for r in parse(VIOLET):
        for s in r['selectors']:
            check(sel.match(s), s)


@fact('StylesheetRulesTests.The_phone_breakpoint_is_one_in_every_sheet_and_every_script (the violet)')
def _():
    for r in parse(VIOLET):
        for m in re.finditer(r'(min|max)-width:\s*([\d.]+)px', r['media'] or ''):
            b = float(m.group(2))
            if 760 <= b <= 780:
                check(b == (768 if m.group(1) == 'max' else 768.02), r['media'])


@fact('HistoryRingsMarkupTests.Every_verdict_kind_keeps_its_own_look_and_every_colour_is_a_token')
def _():
    def look(kind):
        return ' | '.join(value(HISTORY, '.history-verdict-' + kind, p) or '' for p in ('background', 'color', 'border-color'))
    check(look('broke') == look('always-failing'), 'always-failing')
    kinds = ['broke', 'fixed', 'flaky', 'new', 'slower', 'behaviour-changed', 'quarantined']
    check(len(set(map(look, kinds))) == len(kinds), 'looks not distinct')
    for k in ('reordered', 'unstable-shape', 'alternating'):
        check(look('behaviour-changed') == look(k), k)
    check(not rules_for(HISTORY, '.history-verdict-faster'), 'faster')
    painted = ['.history-verdict', '.history-section', '.history-summary-line', '.history-meta', '.history-note', '.history-compare',
               '.history-chart', '.history-ring', '.history-bar', '.history-time', '.history-list', '.history-count', '.history-link',
               '.history-evidence', '.history-series', '.history-more']
    for r in parse(HISTORY):
        if any(any(s.startswith(p) for p in painted) for s in r['selectors']):
            for k, v in r['decl'].items():
                if k in ('background', 'color', 'border', 'border-color', 'border-top', 'fill', 'stroke'):
                    check('var(--' in v or v == 'transparent', f"{r['selectors'][0]} {{ {k}: {v} }}")
    check(not re.search(r'var\(--[\w-]+\s*,', HISTORY), 'a fallback')


failed = 0
for name, f in FACTS:
    try:
        f()
        print('pass  ' + name)
    except Fail as e:
        failed += 1
        print(f'FAIL  {name}: {e}')
print(f'{len(FACTS) - failed} of {len(FACTS)} pass')
sys.exit(1 if failed else 0)
