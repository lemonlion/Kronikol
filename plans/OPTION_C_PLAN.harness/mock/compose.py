"""Compose the Option C stylesheet for the preview.

Takes the August design artifact's C layers (C_CSS, CFULL_EXTRA, the M2 phone sheet, the gear rule),
replaces every literal colour and every short custom property with a --kron-* token, and appends the
hand-written rules in c-extra.css (overrides of today's stylesheet and the surfaces added since August).
Writes c-skin.css.
"""
import pathlib
import re

here = pathlib.Path(__file__).parent
aug = here / 'aug'

c_css = (aug / 'C_CSS.css').read_text(encoding='utf-8')
cfull = (aug / 'CFULL_EXTRA.css').read_text(encoding='utf-8')
cpage = (aug / 'CPAGE_CSS.css').read_text(encoding='utf-8')
m2 = (aug / 'M2_CSS.css').read_text(encoding='utf-8')[len(cpage):]
gear = (aug / 'GEAR_CSS.css').read_text(encoding='utf-8')

def drop_root(css):
    # each layer opens with its own one-line :root block of short tokens; the token layer replaces them
    return re.sub(r'^:root \{[^}]*\}\n', '', css, count=1)

body = '\n'.join([
    '/* ---- C toolbar (August C_CSS) ---- */', drop_root(c_css),
    '/* ---- C page (August CFULL_EXTRA) ---- */', drop_root(cfull),
    '/* ---- C phone sheet (August M2) ---- */', m2,
    '/* ---- settings button on the title line (August GEAR_CSS) ---- */', gear,
])

short = {
    '--ka': '--kron-accent', '--kb': '--kron-select-border', '--ki': '--kron-ink-soft', '--km': '--kron-muted',
    '--kh': '--kron-hover', '--ksel-i': '--kron-selected-ink', '--ksel': '--kron-selected', '--kfont': '--kron-font',
    '--pg': '--kron-ground', '--cardc': '--kron-card', '--hair2': '--kron-hairline-soft', '--hair': '--kron-hairline',
    '--inkc': '--kron-ink', '--mut': '--kron-muted', '--ok2s': '--kron-pass-soft', '--ok2': '--kron-pass',
    '--bad2s': '--kron-fail-soft', '--bad2': '--kron-fail', '--warn2': '--kron-warn', '--mono': '--kron-mono',
}
for k in sorted(short, key=len, reverse=True):
    body = re.sub(r'var\(' + re.escape(k) + r'\)', 'var(' + short[k] + ')', body)

literals = {
    '#2559c7': 'accent', '#1d4fb0': 'selected-ink', '#dce8fb': 'selected', '#b3c8ef': 'selected-edge',
    '#edf1f8': 'hover', '#f5f7fb': 'ground', '#fff': 'card', '#f8fafd': 'strip', '#e6eaf2': 'strip-edge',
    '#dde3ee': 'divider', '#e8ecf4': 'divider-soft', '#e3e8f0': 'hairline', '#eef1f7': 'hairline-soft',
    '#f2f5f9': 'field', '#c9d1de': 'control-border', '#d7dde8': 'select-border', '#a7aeb9': 'dot',
    '#7b8494': 'label', '#8a93a2': 'label-soft', '#98a1ad': 'faint', '#6a7280': 'muted', '#68727f': 'muted',
    '#5a6270': 'ink-quiet', '#5a6472': 'ink-quiet', '#4a5361': 'ink-soft', '#3c4553': 'ink-soft',
    '#242b36': 'ink', '#242b35': 'ink',
    '#2e7d4f': 'pass', '#e6f3ec': 'pass-soft',
    '#b3372e': 'fail', '#fbecea': 'fail-soft', '#efc7c2': 'fail-edge', '#7c2a24': 'fail-ink',
    '#fbf1f0': 'exclude-soft', '#e6c9c5': 'exclude-edge', '#8a4a43': 'exclude-ink',
    '#8a5a17': 'warn', '#8a5417': 'warn', '#fbf3e7': 'warn-soft', '#ecd3ae': 'warn-edge',
    '#f7f9fc': 'quiet', '#e0e5ee': 'quiet-edge',
    'rgba(37,89,199,.18)': 'accent-rail', 'rgba(37,89,199,.12)': 'focus',
    'rgba(20,30,60,.06)': 'shadow', 'rgba(120,30,25,.25)': 'fail-shadow',
}
for lit in sorted(literals, key=len, reverse=True):
    pattern = re.escape(lit) + (r'(?![0-9a-fA-F])' if lit.startswith('#') else '')
    body = re.sub(pattern, 'var(--kron-' + literals[lit] + ')', body, flags=re.I)

# white ink on a solid fill stays white whatever the card colour is
body = body.replace('color: white;', 'color: var(--kron-on-solid);')
body = body.replace('margin-top: -0.75px; background: var(--kron-card);', 'margin-top: -0.75px; background: var(--kron-on-solid);')

left = sorted(set(re.findall(r'#[0-9a-fA-F]{3,6}\b|rgba\([^)]*\)', body)))
print('literals left in the August layers:', left)

tokens = (here / 'c-tokens.css').read_text(encoding='utf-8')
names = ['c-extra.css', 'c-round2.css', 'c-round3.css', 'c-round4.css', 'c-round5.css', 'c-round6.css', 'c-round7.css', 'c-diagram-look.css', 'c-round8.css', 'c-round9.css', 'c-dark.css', 'c-contrast.css', 'c-round16.css', 'c-round17.css']
# EXTRA_CSS=a.css,b.css adds sheets for a variant page (the diagram look) without touching the main one
names += [n for n in __import__('os').environ.get('EXTRA_CSS', '').split(',') if n]
extra = '\n'.join((here / n).read_text(encoding='utf-8') for n in names)
out = tokens + '\n' + body + '\n/* ---- preview additions: today\'s report, read in C ---- */\n' + extra
(here / 'c-skin.css').write_text(out, encoding='utf-8')
print('c-skin.css', len(out), 'bytes')
