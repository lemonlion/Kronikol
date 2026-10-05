"""S0 baseline: what cross-run history costs TestRunReport.html (Kronikol 4.5.1).

Reads run-01.html .. run-11.html and run-off.html beside this script and prints small numbers only:
sizes, gzip sizes, counts, histograms. It never prints report content beyond short excerpts.

  run-01.html  first run, empty ledger
  run-11.html  history from ten earlier runs
  run-off.html KRONIKOL_HISTORY=off (no ledger read, nothing appended)

Usage: python measure.py [dir]      (dir defaults to this script's folder)
"""
import collections
import gzip
import os
import re
import sys

S0 = sys.argv[1] if len(sys.argv) > 1 else os.path.dirname(os.path.abspath(__file__))

BLOCK = re.compile(rb'<(script|style)\b([^>]*)>(.*?)</\1>', re.S)
# A scenario's element: <details class="scenario ..." ... data-stable-id="..."
DETAILS_OPEN = re.compile(rb'<details\b[^>]*>')
TR_OPEN = re.compile(rb'<tr\b[^>]*>')
CLASS_ATTR = re.compile(rb'\bclass="([^"]*)"')
VERDICT_ATTR = re.compile(rb' data-history-verdicts="([^"]*)"')
SPARKLINE = re.compile(rb' <span class="history-sparkline"[^>]*></span>')
PILL = re.compile(rb' <span class="history-verdict [^"]*"[^>]*>[^<]*</span>')
SECTION = re.compile(rb'<details id="history-section".*?</details>', re.S)
STABLE_ID = re.compile(rb'data-stable-id="([^"]*)"')


def load(name):
    with open(os.path.join(S0, name), 'rb') as f:
        return f.read()


def gz(data, level):
    return len(gzip.compress(data, compresslevel=level, mtime=0))


def sizes(data):
    return len(data), gz(data, 9), gz(data, 6)


def split_blocks(data):
    """(code blocks, payload blocks): every <style> and every <script> that is not a JSON payload is code."""
    code, payload = [], []
    for m in BLOCK.finditer(data):
        whole = m.group(0)
        if m.group(1) == b'script' and b'application/json' in m.group(2):
            ident = re.search(rb'id="([^"]*)"', m.group(2))
            payload.append(((ident.group(1) if ident else b'?').decode(), whole))
        else:
            code.append((m.group(1).decode(), whole))
    return code, payload


def markup_only(data):
    return BLOCK.sub(b'', data)


def scenario_elements(markup):
    """The scenario elements, classified:
    'scenario' = <details> whose class has the token 'scenario' and that carries data-stable-id (one test);
    'group'    = <details class="scenario scenario-parameterized"> (an outline; no stable id of its own,
                 its verdict attribute is the union of its rows');
    'tr'       = an outline example row <tr ... data-stable-id> (each row is drawn twice: flat and grouped table).
    """
    out = []
    for m in DETAILS_OPEN.finditer(markup):
        tag = m.group(0)
        cls = CLASS_ATTR.search(tag)
        tokens = cls.group(1).decode().split() if cls else []
        if 'scenario' not in tokens:
            continue
        sid = STABLE_ID.search(tag)
        verdict = VERDICT_ATTR.search(tag)
        kind = 'group' if 'scenario-parameterized' in tokens else ('scenario' if sid else 'other')
        out.append({'kind': kind, 'class': ' '.join(tokens), 'sid': sid.group(1).decode() if sid else None,
                    'verdicts': None if verdict is None else verdict.group(1).decode()})
    for m in TR_OPEN.finditer(markup):
        tag = m.group(0)
        sid = STABLE_ID.search(tag)
        if not sid:
            continue
        verdict = VERDICT_ATTR.search(tag)
        out.append({'kind': 'tr', 'class': '', 'sid': sid.group(1).decode(),
                    'verdicts': None if verdict is None else verdict.group(1).decode()})
    return out


def leaf_scenarios(elements):
    """One entry per test: plain scenarios plus outline rows, deduplicated by stable id (rows are drawn twice).
    Returns {sid: verdicts}; a row whose two drawings disagree keeps both values joined by ' | '."""
    leaf = {}
    for e in elements:
        if e['kind'] not in ('scenario', 'tr'):
            continue
        v = e['verdicts']
        if e['sid'] in leaf and leaf[e['sid']] != v:
            leaf[e['sid']] = f'{leaf[e["sid"]]} | {v}'
        else:
            leaf.setdefault(e['sid'], v)
    return leaf


def history_markup_bytes(markup):
    parts = {
        'data-history-verdicts attrs': sum(len(m.group(0)) for m in VERDICT_ATTR.finditer(markup)),
        'history-sparkline spans': sum(len(m.group(0)) for m in SPARKLINE.finditer(markup)),
        'history-verdict pills': sum(len(m.group(0)) for m in PILL.finditer(markup)),
        'history section': sum(len(m.group(0)) for m in SECTION.finditer(markup)),
    }
    return parts


def strip_history(data):
    """The file with every history attribute, sparkline, pill and section removed (scripts untouched)."""
    out = VERDICT_ATTR.sub(b'', data)
    out = SPARKLINE.sub(b'', out)
    out = PILL.sub(b'', out)
    out = SECTION.sub(b'', out)
    return out


def is_stable(value):
    """An empty value or 'stable' (alone or only stables) is stable; anything else is not."""
    if value is None or value == '':
        return True
    return all(v.strip() == 'stable' for v in value.split(','))


def describe(name, data, out):
    raw, g9, g6 = sizes(data)
    markup = markup_only(data)
    code, payload = split_blocks(data)
    elements = scenario_elements(markup)
    p = out.append
    p(f'== {name}')
    p(f'  raw {raw:,} B   gzip-9 {g9:,} B   gzip-6 {g6:,} B')
    p('  counts, whole file / markup only (script and style blocks removed):')
    for label, needle in (('<details class="scenario (prefix)', b'<details class="scenario'),
                          ('data-stable-id="', b'data-stable-id="'),
                          ('id="sid-', b'id="sid-'),
                          ('class="history-sparkline"', b'class="history-sparkline"'),
                          ('data-history-verdicts="', b'data-history-verdicts="'),
                          ('history-pill', b'history-pill'),
                          ('class="history-verdict ', b'class="history-verdict '),
                          ('id="history-section"', b'id="history-section"')):
        p(f'    {label:30s} {data.count(needle):5d} / {markup.count(needle):5d}')
    kinds = collections.Counter(e['kind'] for e in elements)
    p(f'  scenario elements (markup): {dict(kinds)}; distinct data-stable-id on scenario/tr: '
      f'{len({e["sid"] for e in elements if e["sid"]})}')
    p(f'  scenario <details> class values: {dict(collections.Counter(e["class"] for e in elements if e["kind"] != "tr"))}')
    for kind in ('scenario', 'group', 'tr', 'other'):
        dist = collections.Counter('(no attribute)' if e['verdicts'] is None else e['verdicts']
                                   for e in elements if e['kind'] == kind)
        if dist:
            p(f'  data-history-verdicts on {kind:8s}: {dict(dist)}')
    code_bytes = sum(len(b) for _, b in code)
    css = sum(len(b) for k, b in code if k == 'style')
    js = sum(len(b) for k, b in code if k == 'script')
    p(f'  code blocks: {len(code)} ({sum(1 for k, _ in code if k == "style")} style, {sum(1 for k, _ in code if k == "script")} script), '
      f'{code_bytes:,} B raw (CSS {css:,}, JS {js:,}); gzip-9 of their concatenation {gz(b"".join(b for _, b in code), 9):,} B')
    p('  payload blocks (script type=application/json): '
      + ', '.join(f'{i} {len(b):,} B' for i, b in payload))
    p(f'  markup outside script/style: {len(markup):,} B')
    hm = history_markup_bytes(markup)
    p(f'  history markup bytes: {hm} total {sum(hm.values()):,} B')
    return {'raw': raw, 'g9': g9, 'g6': g6, 'code': code, 'payload': payload, 'elements': elements,
            'markup': markup, 'history_bytes': sum(hm.values()), 'history_parts': hm}


def main():
    out = []
    p = out.append
    files = {n: load(n) for n in ('run-01.html', 'run-11.html', 'run-off.html')}
    r = {n: describe(n, d, out) for n, d in files.items()}

    on, off = r['run-11.html'], r['run-off.html']
    leaf = leaf_scenarios(on['elements'])
    n_leaf = len(leaf)
    n_plain = sum(1 for e in on['elements'] if e['kind'] == 'scenario')
    n_rows = len({e['sid'] for e in on['elements'] if e['kind'] == 'tr'})
    n_headers = sum(1 for e in on['elements'] if e['kind'] in ('scenario', 'group'))
    n_spark = len(SPARKLINE.findall(on['markup']))
    assert n_leaf == len(leaf_scenarios(off['elements'])), 'on and off reports hold different scenarios'

    p('')
    p('== Scenario count used')
    p(f'  leaf scenarios (one per test) = distinct data-stable-id on <details class="scenario ..."> ({n_plain})'
      f' + on outline rows <tr> ({n_rows}, each drawn twice: flat and grouped table) = {n_leaf}')
    p(f'  scenario headers = <details> whose class has the token "scenario" (plain {n_plain} + outline groups '
      f'{n_headers - n_plain}) = {n_headers}; headers with a sparkline = {n_spark} (groups and rows get none)')

    p('')
    p('== Verdicts in run-11, per leaf scenario (outline rows deduplicated; a group carries the union of its rows)')
    raw_dist = collections.Counter('(no attribute)' if v is None else v for v in leaf.values())
    p(f'  raw distribution: {dict(raw_dist)}')
    not_stable = [v for v in leaf.values() if not is_stable(v)]
    p(f'  classified: stable = empty, absent or only "stable"; not stable = anything else')
    p(f'  not stable: {len(not_stable)} of {n_leaf}; by first (primary) verdict: '
      f'{dict(collections.Counter(v.split(",")[0] for v in not_stable))}')
    group_dist = collections.Counter('(no attribute)' if e['verdicts'] is None else e['verdicts']
                                     for e in on['elements'] if e['kind'] == 'group')
    p(f'  outline group elements: {dict(group_dist)}')

    p('')
    p('== Difference, run-11 (history on, 10 earlier runs) minus run-off (history off)')
    for key, label in (('raw', 'raw'), ('g9', 'gzip-9'), ('g6', 'gzip-6')):
        d = on[key] - off[key]
        p(f'  {label:7s}: {d:+,} B total = {d / n_leaf:+,.1f} B per leaf scenario ({n_leaf}) = {d / n_headers:+,.1f} B per header ({n_headers})'
          f' = {d / n_spark:+,.1f} B per sparkline ({n_spark})  ({d / off[key] * 100:+.2f}% of the off report)')
    # The CSS/JS share: are the code blocks the same bytes?
    code_on = [b for _, b in on['code']]
    code_off = [b for _, b in off['code']]
    same = code_on == code_off
    p(f'  code blocks identical with and without history: {same}; CSS+JS raw delta '
      f'{sum(map(len, code_on)) - sum(map(len, code_off)):+,} B')
    if not same:
        for i, (a, b) in enumerate(zip(code_on, code_off)):
            if a != b:
                p(f'    block {i}: {len(a):,} vs {len(b):,} B')
    pay_on = dict(on['payload'])
    pay_off = dict(off['payload'])
    for k in sorted(set(pay_on) | set(pay_off)):
        a, b = len(pay_on.get(k, b'')), len(pay_off.get(k, b''))
        p(f'  payload {k}: {a:,} vs {b:,} B ({a - b:+,})')
    p(f'  markup outside script/style: {len(on["markup"]):,} vs {len(off["markup"]):,} B ({len(on["markup"]) - len(off["markup"]):+,})')
    p(f'  of which history markup in run-11: {on["history_bytes"]:,} B = {on["history_bytes"] / n_leaf:,.1f} B per leaf scenario; '
      f'parts {on["history_parts"]}')
    attr_by_kind = collections.Counter()
    for m in DETAILS_OPEN.finditer(on['markup']):
        v = VERDICT_ATTR.search(m.group(0))
        if v:
            attr_by_kind['group' if b'scenario-parameterized' in m.group(0) else 'scenario'] += len(v.group(0))
    for m in TR_OPEN.finditer(on['markup']):
        v = VERDICT_ATTR.search(m.group(0))
        if v:
            attr_by_kind['tr'] += len(v.group(0))
    p(f'  verdict attribute bytes by element: {dict(attr_by_kind)}')
    stripped = strip_history(files['run-11.html'])
    s_raw, s_g9, s_g6 = sizes(stripped)
    p(f'  run-11 with its history markup stripped: raw {s_raw:,} B (vs run-off {off["raw"]:,}, {s_raw - off["raw"]:+,} B left over = '
      f'timing/run noise, the JSON payloads above)')
    p(f'  gzip cost of the history markup inside run-11 (run-11 minus run-11 stripped, same run so no timing noise): '
      f'gzip-9 {on["g9"] - s_g9:+,} B ({(on["g9"] - s_g9) / n_leaf:+,.1f}/leaf, {(on["g9"] - s_g9) / n_spark:+,.1f}/sparkline), '
      f'gzip-6 {on["g6"] - s_g6:+,} B ({(on["g6"] - s_g6) / n_leaf:+,.1f}/leaf, {(on["g6"] - s_g6) / n_spark:+,.1f}/sparkline)')
    spark = SPARKLINE.findall(on['markup'])
    if spark:
        lens = sorted(len(s) for s in spark)
        p(f'  sparkline span lengths (B): min {lens[0]}, median {lens[len(lens) // 2]}, max {lens[-1]}; '
          f'excerpt: {spark[0][:200].decode(errors="replace")!r}')
        for attr in (b'title', b'style', b'aria-label'):
            ls = [len(re.search(attr + rb'="[^"]*"', s).group(0)) for s in spark]
            p(f'    {attr.decode()} attribute bytes per sparkline: {ls}')
        lines = re.search(rb'title="([^"]*)"', spark[0]).group(1).decode().split('\n')
        p(f'    tooltip lines: {len(lines)}, lengths {[len(x) for x in lines]}; a run line: {lines[2][:160]!r}')
    pills = PILL.findall(on['markup'])
    if pills:
        p(f'  pill excerpt: {pills[0][:200].decode(errors="replace")!r}')

    p('')
    p('== History code every report carries, history on or off (measured in run-off.html)')
    style = re.search(rb'<style>(.*?)</style>', files['run-off.html'], re.S).group(1)
    rules = re.findall(rb'(?:/\*[^*]*\*/\s*)?[^{}]*history[^{}]*\{[^{}]*\}', style)
    p(f'  CSS rules whose selector names history: {len(rules)}, {sum(map(len, rules)):,} B of the {len(style):,} B style block')
    js_lines = [ln for m in re.finditer(rb'<script>(.*?)</script>', files['run-off.html'], re.S)
                for ln in m.group(1).split(b'\n') if re.search(rb'verdict|\bhv\b', ln, re.I)]
    p(f'  JS lines handling history verdicts (verdict|hv): {len(js_lines)}, {sum(map(len, js_lines)):,} B')

    p('')
    p('== First run (empty ledger) vs off')
    first = r['run-01.html']
    for key, label in (('raw', 'raw'), ('g9', 'gzip-9'), ('g6', 'gzip-6')):
        p(f'  {label:7s}: run-01 {first[key]:,} B vs run-off {off[key]:,} B ({first[key] - off[key]:+,})')
    p(f'  history markup in run-01: {first["history_bytes"]:,} B, parts {first["history_parts"]}')

    p('')
    p('== Run-to-run, history on')
    p('  name: raw B, gzip-9 B, history markup B | stripped of history: raw B, gzip-9 B | in-file gzip-9 cost of history B')
    prev = None
    stripped_g9 = []
    for i in range(1, 12):
        name = f'run-{i:02d}.html'
        path = os.path.join(S0, name)
        if not os.path.exists(path):
            continue
        d = load(name)
        hb = sum(history_markup_bytes(markup_only(d)).values())
        g9 = gz(d, 9)
        st = strip_history(d)
        st9 = gz(st, 9)
        stripped_g9.append(st9)
        delta = '' if prev is None else f'  ({len(d) - prev:+,} raw vs previous)'
        p(f'  {name}: {len(d):,}  {g9:,}  {hb:,} | {len(st):,}  {st9:,} | {g9 - st9:+,}{delta}')
        prev = len(d)
    p(f'  run noise with history removed: stripped gzip-9 ranges {min(stripped_g9):,} to {max(stripped_g9):,} '
      f'({max(stripped_g9) - min(stripped_g9):,} B spread); run-off gzip-9 {off["g9"]:,}')
    ten, eleven = load('run-10.html'), load('run-11.html')
    p(f'  run-10 vs run-11: raw {len(ten):,} vs {len(eleven):,} ({len(eleven) - len(ten):+,}); '
      f'stripped of history {len(strip_history(ten)):,} vs {len(strip_history(eleven)):,} '
      f'({len(strip_history(eleven)) - len(strip_history(ten)):+,})')

    log = os.path.join(S0, 'runs.log')
    if os.path.exists(log):
        p('')
        p('== Test runner summary per run (runs.log; the ledger path shortened)')
        with open(log, encoding='utf-8') as f:
            for line in f:
                line = re.sub(r'KRONIKOL_HISTORY=\S*ledger\.jsonl', 'KRONIKOL_HISTORY=<s0>/ledger.jsonl', line.rstrip())
                p('  ' + re.sub(r' - Example\.Api\.Tests\.Component\.ReqNRoll\.xUnit3\.dll \(net10\.0\)', '', line))

    text = '\n'.join(out)
    print(text)


if __name__ == '__main__':
    main()
