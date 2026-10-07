"""Executable spec and measurements for plans/WARM_UP_PLAN.md (#113).

Reads Kronikol TestRunReport.json files, or the reduced corpus `extract` writes (*.calls.json.gz), and
applies the warm-up rule of the plan's section 4.1. Python 3.10+, standard library only.

  python warmup.py extract <src-root> <out-dir>     reduce every TestRunReport.json under src-root
  python warmup.py marks <root> [--variant P]       each lane's warm-up calls
  python warmup.py variants <root>                  calls marked and scenarios carrying one, per variant
  python warmup.py truth <root> --lanes a,b,c       cross-framework ground truth (plan section 4.2)
  python warmup.py stability <run> <run> ...        wall and time-left swings across runs of one lane
  python warmup.py table <root>                     the issue's six scenarios, wall / left per framework
  python warmup.py slowest <root>                   the ten slowest by wall time and by time left
  python warmup.py misses <root>                    slow first calls with no baseline, and late outliers
  python warmup.py totals <root>                    the whole-call measure against the excess over baseline
  python warmup.py ledger <history.jsonl> [since] [until]   the issue's first table from a history ledger

A <root> is searched recursively; a lane is the folder that holds its report, relative to the root.
"""
import argparse
import collections
import datetime as dt
import glob
import gzip
import json
import os
import re
import statistics
import sys

# --- InteractionShape.Template (src/Kronikol/History/InteractionShape.cs, Version 4), ported -------------
# Order: binary, id, timestamp, number. Python's re has no variable-width look-behind, so the binary
# pattern's (?<=^|[/\-_:.]) is written as two look-behinds; the id pattern's named back-reference is
# (?P=sep). Everything else is the C# pattern verbatim.
BINARY = re.compile(r'(?:(?<=^)|(?<=[/\-_:.]))[^/\-_:.]*(?:%EF%BF%BD|%[01][0-9A-Fa-f])[^/]*')
IDS = re.compile(
    r'(?<![0-9A-Za-z])(?:[0-9A-Fa-f]{8}(?P<sep>[-_])[0-9A-Fa-f]{4}(?P=sep)[0-9A-Fa-f]{4}(?P=sep)[0-9A-Fa-f]{4}'
    r'(?P=sep)[0-9A-Fa-f]{12}|[0-9A-Fa-f]{16,}|[0-9A-HJKMNP-TV-Z]{26}|(?=[0-9A-Fa-f]*\d)(?=[0-9A-Fa-f]*[A-Fa-f])'
    r'[0-9A-Fa-f]{8,15})(?![0-9A-Za-z])')
TIMESTAMP = re.compile(
    r'(?<![0-9A-Za-z])\d{4}-\d{2}-\d{2}(?:T\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:Z|[+-]\d{2}:?\d{2})?)?(?![0-9A-Za-z])')
NUMBER = re.compile(r'(?<![0-9A-Za-z])\d+(?![0-9A-Za-z])')


def template_path(path):
    path = BINARY.sub('{bin}', path)
    path = IDS.sub('{id}', path)
    path = TIMESTAMP.sub('{ts}', path)
    return NUMBER.sub('{n}', path)


def path_of(uri):
    """The path, query and fragment dropped. An absolute URI loses its scheme and authority."""
    m = re.match(r'^[a-zA-Z][a-zA-Z0-9+.-]*://[^/?#]*(.*)$', uri or '')
    rest = m.group(1) if m else (uri or '')
    return re.split(r'[?#]', rest, maxsplit=1)[0] or '/'


def shape_of(call):
    # Statement-shaped calls (SQL and the like) also carry their templated statement head in the plan's rule
    # (4.1, step 2). No test-made call in the corpus is statement-shaped, so the head is not modelled here.
    return (call['service'], call['method'].upper(), template_path(path_of(call['uri'])))


# --- Loading ------------------------------------------------------------------------------------------------

def ts(s):
    return dt.datetime.fromisoformat(s.replace('Z', '+00:00')) if s else None


def reduce_report(path):
    """The fields the rule reads, one entry per call (a request and its response)."""
    d = json.load(open(path, encoding='utf-8'))
    out = {'kronikolVersion': d.get('kronikolVersion'), 'suite': d.get('suite'), 'scenarios': []}
    for f in d.get('features') or []:
        for s in f.get('scenarios') or []:
            by = collections.OrderedDict()
            for i in s.get('httpInteractions') or []:
                by.setdefault(i['requestResponseId'], []).append(i)
            calls = []
            for rid, recs in by.items():
                req = next((r for r in recs if r['type'] == 'Request'), None)
                res = next((r for r in recs if r['type'] == 'Response'), None)
                if req is None:
                    continue
                dur = req.get('durationMs')
                if dur is None and res is not None and req.get('timestamp') and res.get('timestamp'):
                    dur = (ts(res['timestamp']) - ts(req['timestamp'])).total_seconds() * 1000.0
                calls.append({
                    'rid': rid, 'caller': req.get('callerName') or '', 'service': req.get('serviceName') or '',
                    'method': req.get('method') or '', 'uri': req.get('uri') or '', 'meta': req.get('metaType') or 'Default',
                    'category': req.get('dependencyCategory'), 'start': req.get('timestamp'), 'durationMs': dur,
                    'status': res.get('statusCode') if res else None,
                })
            out['scenarios'].append({
                'feature': f.get('name'), 'name': s.get('name'), 'id': s.get('id'), 'result': s.get('result'),
                'durationSeconds': s.get('durationSeconds'), 'endedAt': s.get('endedAt'), 'calls': calls,
            })
    return out


def lanes_under(root):
    """(lane, loader) for every report or reduced corpus file under root; root may also be one such file."""
    if os.path.isfile(root):
        if root.endswith('.calls.json.gz'):
            return [(os.path.basename(root)[:-len('.calls.json.gz')], lambda: json.load(gzip.open(root, 'rt', encoding='utf-8')))]
        return [(os.path.basename(os.path.dirname(os.path.abspath(root))), lambda: reduce_report(root))]
    found = []
    for p in sorted(glob.glob(os.path.join(root, '**', 'TestRunReport.json'), recursive=True)):
        lane = os.path.relpath(os.path.dirname(p), root).replace(os.sep, '/')
        found.append((lane if lane != '.' else os.path.basename(os.path.abspath(root)), lambda p=p: reduce_report(p)))
    for p in sorted(glob.glob(os.path.join(root, '**', '*.calls.json.gz'), recursive=True)):
        rel = os.path.relpath(p, root).replace(os.sep, '/')[:-len('.calls.json.gz')]
        found.append((rel, lambda p=p: json.load(gzip.open(p, 'rt', encoding='utf-8'))))
    return found


def load(loader):
    data = loader()
    scenarios, calls = [], []
    for s in data['scenarios']:
        sc = {'feature': s['feature'], 'name': s['name'], 'id': s.get('id'), 'result': s.get('result'),
              'wall': (s.get('durationSeconds') or 0.0) * 1000.0, 'calls': []}
        services = {c['service'] for c in s['calls']}
        for c in s['calls']:
            start = ts(c['start'])
            dur = c['durationMs']
            call = dict(c)
            call.update(scenario=sc, start=start, dur=dur,
                        end=(start + dt.timedelta(milliseconds=dur)) if (start is not None and dur is not None) else None,
                        # Plan 4.1 step 1: the test's own call. Its caller never appears as a service in the
                        # scenario (the rule PlantUmlCreator uses to pick the diagram's actor), and it is a
                        # request, not an event a broker delivered.
                        test=(c['caller'] not in services and c['meta'] != 'Event'))
            call['shape'] = shape_of(call)
            sc['calls'].append(call)
            calls.append(call)
        scenarios.append(sc)
    return scenarios, calls


# --- The rule -------------------------------------------------------------------------------------------------

VARIANTS = {
    # name: (ratio, floor ms, minimum baseline calls, baseline, waiter tests, a 5xx first call judged only against 5xx calls)
    'V0': (10, 50, 2, 'later', ('ratio',), False),             # the issue's rule as written
    'V3': (10, 50, 1, 'after', ('ratio',), False),             # one baseline call, baseline from warm calls only
    'PU': (10, 50, 1, 'after', ('ratio', 'release'), False),   # V3 plus released-together waiters
    'P': (10, 50, 1, 'after', ('ratio', 'release'), True),     # the plan's rule (4.1): PU with the 5xx guard (Q11)
    'P5': (5, 25, 1, 'after', ('ratio', 'release'), True),     # sensitivity: looser
    'P20': (20, 100, 1, 'after', ('ratio', 'release'), True),  # sensitivity: stricter
    'PS': (10, 50, 1, 'after', ('ratio', 'release'), True),    # Q10: P with the response's status class in the shape
    'PL': (10, 50, 1, 'after', ('ratio', 'release'), True),    # Q10: P, and the first call of each status class judged too
}
RELEASE_MS = 50.0


def status_class(call):
    return (str(call['status'])[0] + 'xx') if call.get('status') is not None else 'none'


def detect(calls, variant='P'):
    """Marks warm-up calls in place (call['warm'] = (kind, baseline median, baseline count)); returns them."""
    if variant == 'PL':
        return detect_layered(calls)
    ratio, floor, min_base, baseline, waiters, guard = VARIANTS[variant]
    for c in calls:
        c.pop('warm', None)
    groups = collections.defaultdict(list)
    for c in calls:
        if c['test']:
            key = c['shape'] + ((status_class(c),) if variant == 'PS' else ())
            groups[key].append(c)
    marked = []
    for shape, cs in groups.items():
        if any(c['start'] is None or c['dur'] is None for c in cs):
            continue  # 4.1 step 3: a shape with an untimed call is not judged; the untimed one may have been first
        cs.sort(key=lambda c: (c['start'], c['dur']))
        first = cs[0]
        later = [c for c in cs[1:] if c['start'] >= first['end']] if baseline == 'after' else cs[1:]
        if guard and status_class(first) == '5xx':
            later = [c for c in later if status_class(c) == '5xx']
        if len(later) < min_base:
            continue
        med = statistics.median(c['dur'] for c in later)
        if not (first['dur'] >= ratio * med and first['dur'] - med >= floor):
            continue
        first['warm'] = ('first', med, len(later), None)
        marked.append(first)
        for c in cs[1:]:
            if not (first['start'] <= c['start'] < first['end']):
                continue
            by_ratio = 'ratio' in waiters and c['dur'] >= ratio * med and c['dur'] - med >= floor
            released = ('release' in waiters and abs((c['end'] - first['end']).total_seconds() * 1000.0) <= RELEASE_MS
                        and c['dur'] - med >= floor)
            if by_ratio or released:
                c['warm'] = ('waited', med, len(later), first['rid'])
                marked.append(c)
    return marked


def detect_layered(calls):
    """Q10's second candidate: P, plus the first call of each other status class of a shape, each judged against
    the shape's warm calls of any status that start after it ends and are not themselves such a first call."""
    ratio, floor, min_base, _, _, _ = VARIANTS['P']
    marked = detect(calls, 'P')
    groups = collections.defaultdict(list)
    for c in calls:
        if c['test']:
            groups[c['shape']].append(c)
    for shape, cs in groups.items():
        if any(c['start'] is None or c['dur'] is None for c in cs):
            continue
        cs.sort(key=lambda c: (c['start'], c['dur']))
        firsts = {}
        for c in cs:
            firsts.setdefault(status_class(c), c)
        heads = {id(c) for c in firsts.values()}
        for c in firsts.values():
            if c is cs[0] or c.get('warm'):
                continue
            later = [o for o in cs if id(o) not in heads and o['start'] >= c['end']]
            if len(later) < min_base:
                continue
            med = statistics.median(o['dur'] for o in later)
            if c['dur'] >= ratio * med and c['dur'] - med >= floor:
                c['warm'] = ('first-of-status', med, len(later))
                marked.append(c)
    return marked


def union_ms(intervals):
    total, cur_s, cur_e = 0.0, None, None
    for s, e in sorted(intervals):
        if cur_e is None or s > cur_e:
            if cur_e is not None:
                total += (cur_e - cur_s).total_seconds() * 1000.0
            cur_s, cur_e = s, e
        elif e > cur_e:
            cur_e = e
    if cur_e is not None:
        total += (cur_e - cur_s).total_seconds() * 1000.0
    return total


def warm_up_ms(sc, clip=True):
    """4.1 step 7: the time the scenario's warm-up calls cover, counted once, at most its own duration."""
    cc = [c for c in sc['calls'] if c.get('warm')]
    if not cc:
        return 0.0
    u = union_ms([(c['start'], c['end']) for c in cc])
    return min(u, sc['wall']) if clip else u


def norm(s):
    return re.sub(r'[^a-z0-9]', '', (s or '').lower())


# --- Commands -------------------------------------------------------------------------------------------------

def cmd_extract(a):
    os.makedirs(a.out, exist_ok=True)
    for lane, loader in lanes_under(a.src):
        data = loader()
        name = lane.replace('/', '_') + '.calls.json.gz'
        with gzip.open(os.path.join(a.out, name), 'wt', encoding='utf-8') as fh:
            json.dump(data, fh, separators=(',', ':'))
        print(f"{lane}: {len(data['scenarios'])} scenarios, {sum(len(s['calls']) for s in data['scenarios'])} calls -> {name}")


def cmd_marks(a):
    if a.json:
        out = {}
        for lane, loader in lanes_under(a.root):
            scenarios, calls = load(loader)
            marked = detect(calls, a.variant)
            out[lane] = {
                'calls': {c['rid']: {'kind': c['warm'][0], 'shape': f"{c['shape'][1]} {c['shape'][2]}", 'baselineMs': c['warm'][1],
                                     'baselineCalls': c['warm'][2], 'first': c['warm'][3] if len(c['warm']) > 3 else None}
                          for c in marked},
                'scenarios': {sc['id']: warm_up_ms(sc) for sc in scenarios if warm_up_ms(sc) > 0},
            }
        json.dump(out, sys.stdout, indent=1, sort_keys=True)
        print()
        return
    for lane, loader in lanes_under(a.root):
        scenarios, calls = load(loader)
        marked = detect(calls, a.variant)
        carrying = {id(c['scenario']) for c in marked}
        print(f"== {lane}: {len(scenarios)} scenarios, {sum(c['test'] for c in calls)} test calls, "
              f"{len(marked)} warm-up calls in {len(carrying)} scenarios")
        for c in sorted(marked, key=lambda c: c['start']):
            print(f"   {c['warm'][0]:6} {c['shape'][1]} {c['shape'][2]}  {c['dur']:.0f} ms (baseline median {c['warm'][1]:.1f} ms "
                  f"over {c['warm'][2]})  in '{c['scenario']['name']}' ({c['scenario']['wall']:.0f} ms)")


def cmd_variants(a):
    names = list(VARIANTS)
    print('| Lane | ' + ' | '.join(names) + ' |')
    print('|---|' + '---|' * len(names))
    for lane, loader in lanes_under(a.root):
        scenarios, calls = load(loader)
        cells = []
        for v in names:
            marked = detect(calls, v)
            cells.append(f"{len(marked)} in {len({id(c['scenario']) for c in marked})}")
        print(f"| {lane} | " + ' | '.join(cells) + ' |')


def truth(root, lanes, variant):
    per = {}
    for lane in lanes:
        loader = dict(lanes_under(root)).get(lane)
        if loader is None:
            continue
        scenarios, calls = load(loader)
        detect(calls, variant)
        for sc in scenarios:
            per.setdefault(norm(sc['name']), {})[lane] = (sc['wall'], warm_up_ms(sc), sc['name'])
    anomalies = explained = over = harmless = 0
    rows = []
    for key, by in per.items():
        if len(by) < 3:
            continue
        for lane, (wall, warm, name) in by.items():
            others = [w for l, (w, c, n) in by.items() if l != lane and c == 0]
            if len(others) < 2:
                continue
            ref = statistics.median(others)
            left = max(0.0, wall - warm)
            anomaly = wall >= 3 * ref and wall - ref >= 100
            if anomaly:
                anomalies += 1
                if abs(left - ref) <= max(50.0, ref):
                    explained += 1
                else:
                    rows.append(('unexplained', lane, name, wall, warm, left, ref))
            if warm > 0 and left < ref - max(50.0, ref / 2):
                over += 1
                rows.append(('over-subtracted', lane, name, wall, warm, left, ref))
            if warm > 0 and not anomaly:
                harmless += 1
    return anomalies, explained, over, harmless, rows


def cmd_truth(a):
    lanes = a.lanes.split(',')
    print(f"lanes: {', '.join(lanes)}")
    print('| Variant | Anomalies | Explained | Over-subtracted | Warm-up on a scenario that is not an anomaly |')
    print('|---|---|---|---|---|')
    detail = {}
    for v in VARIANTS:
        an, ex, ov, ha, rows = truth(a.root, lanes, v)
        detail[v] = rows
        print(f"| {v} | {an} | {ex} | {ov} | {ha} |")
    if a.list:
        for kind, lane, name, wall, warm, left, ref in sorted(detail[a.list]):
            print(f"   {kind:15} {lane:10} {name[:70]:70} wall {wall:6.0f} warm-up {warm:6.0f} left {left:6.0f} reference {ref:5.0f}")


def cmd_stability(a):
    print('| Variant | wall moves by 200 ms or more | time left moves by 200 ms or more | scenario names |')
    print('|---|---|---|---|')
    for v in ['V0', 'V3', 'PU', 'P', 'PS', 'PL']:
        walls, lefts = collections.defaultdict(list), collections.defaultdict(list)
        for run in a.runs:
            for lane, loader in lanes_under(run):
                scenarios, calls = load(loader)
                detect(calls, v)
                for sc in scenarios:
                    walls[sc['name']].append(sc['wall'])
                    lefts[sc['name']].append(max(0.0, sc['wall'] - warm_up_ms(sc)))
        sp = lambda xs: max(xs) - min(xs)
        print(f"| {v} | {sum(1 for k in walls if sp(walls[k]) >= 200)} | {sum(1 for k in lefts if sp(lefts[k]) >= 200)} | {len(walls)} |")


SIX = [
    'Order status via grpc should return order details',
    'Equipment alerts should contain data ingested via event hub consumer',
    'Audit logs should be filterable by entity id',
    'Audit logs should be returned in descending timestamp order',
    'Batch completions should contain data ingested via pubsub consumer',
    'Retrieving non-existent customer preferences should return not found',
]
FRAMEWORKS = ['xunit', 'nunit', 'tunit', 'reqnroll', 'lightbdd', 'bddfy']


def cmd_table(a):
    lanes = dict(lanes_under(a.root))
    cells = collections.defaultdict(dict)
    for fw in FRAMEWORKS:
        loader = lanes.get(fw)
        if loader is None:
            continue
        scenarios, calls = load(loader)
        detect(calls, a.variant)
        for sc in scenarios:
            for w in SIX:
                if norm(sc['name']) == norm(w):
                    cells[w][fw] = (sc['wall'], warm_up_ms(sc))
    print(f"variant {a.variant}; wall ms, then ' / time left' where the scenario carries a warm-up")
    print('| Scenario | ' + ' | '.join(FRAMEWORKS) + ' |')
    print('|---|' + '---|' * len(FRAMEWORKS))
    for w in SIX:
        row = []
        for fw in FRAMEWORKS:
            if fw not in cells[w]:
                row.append('?')
                continue
            wall, warm = cells[w][fw]
            row.append(f"{wall:,.0f} / {max(0.0, wall - warm):,.0f}" if warm else f"{wall:,.0f}")
        print(f"| {w} | " + ' | '.join(row) + ' |')


def cmd_slowest(a):
    for lane, loader in lanes_under(a.root):
        scenarios, calls = load(loader)
        detect(calls, a.variant)
        by_wall = sorted(scenarios, key=lambda s: -s['wall'])[:10]
        by_left = sorted(scenarios, key=lambda s: -(s['wall'] - warm_up_ms(s)))[:10]
        carrying = [s for s in by_wall if warm_up_ms(s) > 0]
        shares = ', '.join(f"{100 * warm_up_ms(s, clip=False) / s['wall']:.0f}%" for s in carrying if s['wall'] > 0)
        left_list = [s for s in by_wall if s not in by_left]
        print(f"{lane:22} of the 10 slowest by wall time {len(carrying)} carry a warm-up ({shares}); "
              f"{len(left_list)} leave the ten when ranked by time left")


def cmd_misses(a):
    for lane, loader in lanes_under(a.root):
        scenarios, calls = load(loader)
        detect(calls, a.variant)
        groups = collections.defaultdict(list)
        for c in calls:
            if c['test'] and c['start'] is not None and c['dur'] is not None:
                groups[c['shape']].append(c)
        nobase, late = [], []
        for shape, cs in groups.items():
            cs.sort(key=lambda c: c['start'])
            first = cs[0]
            warm = [c for c in cs[1:] if c['start'] >= first['end']]
            if not warm and first['dur'] >= 50:
                nobase.append(first)
            if len(cs) >= 3:
                med = statistics.median(c['dur'] for c in cs[1:])
                late += [c for c in cs[1:] if not c.get('warm') and c['dur'] >= 10 * med and c['dur'] - med >= 50]
        print(f"== {lane}: {len(nobase)} first calls of 50 ms or more with no warm call after them; {len(late)} late outliers")
        for c in sorted(nobase, key=lambda c: -c['dur'])[:a.top]:
            print(f"   no baseline  {c['shape'][1]} {c['shape'][2]} {c['dur']:.0f} ms in '{c['scenario']['name'][:60]}'")
        for c in sorted(late, key=lambda c: -c['dur'])[:a.top]:
            print(f"   late outlier {c['shape'][1]} {c['shape'][2]} {c['dur']:.0f} ms in '{c['scenario']['feature'][:30]}' / '{c['scenario']['name'][:50]}'")


def cmd_totals(a):
    whole = excess = n = clipped = over = 0
    for lane, loader in lanes_under(a.root):
        scenarios, calls = load(loader)
        marked = detect(calls, a.variant)
        whole += sum(c['dur'] for c in marked)
        excess += sum(c['dur'] - c['warm'][1] for c in marked)
        n += len(marked)
        for sc in scenarios:
            u = warm_up_ms(sc, clip=False)
            if u > sc['wall'] > 0:
                over += 1
                clipped += u - sc['wall']
    print(f"{n} warm-up calls; whole calls {whole / 1000:.2f} s; excess over the baseline {excess / 1000:.2f} s "
          f"({100 * (whole - excess) / whole:.1f}% apart); scenarios whose warm-up outlasts their recorded duration: {over} "
          f"({clipped / 1000:.2f} s clipped)")


def cmd_ledger(a):
    rosters, vals, runs = {}, collections.defaultdict(list), collections.Counter()
    for line in open(a.path, encoding='utf-8'):
        o = json.loads(line)
        if o.get('t') == 'roster':
            rosters[o['hash']] = o
        elif o.get('t') == 'run' and o['suite'].endswith('-in-memory') and a.since <= o['at'] <= a.until and not o.get('partial'):
            lane = o['suite'][:-len('-in-memory')]
            r = rosters[o['roster']]
            runs[lane] += 1
            for i, name in enumerate(r['names']):
                if i < len(o['durations']) and o['durations'][i] is not None and o['results'][i] == 'P':
                    for w in SIX:
                        if norm(name) == norm(w):
                            vals[(w, lane)].append(o['durations'][i])
    print(f"window {a.since} .. {a.until}; passing runs per in-memory lane: {dict(runs)}")
    print('| Scenario | ' + ' | '.join(FRAMEWORKS) + ' |')
    print('|---|' + '---|' * len(FRAMEWORKS))
    for w in SIX:
        row = []
        for fw in FRAMEWORKS:
            v = vals.get((w, fw))
            row.append(f"{statistics.median(v):,.0f} ({min(v):,} to {max(v):,})" if v else '?')
        print(f"| {w} | " + ' | '.join(row) + ' |')


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest='cmd', required=True)
    p = sub.add_parser('extract'); p.add_argument('src'); p.add_argument('out'); p.set_defaults(f=cmd_extract)
    p = sub.add_parser('marks'); p.add_argument('root'); p.add_argument('--variant', default='P', choices=list(VARIANTS)); p.add_argument('--json', action='store_true'); p.set_defaults(f=cmd_marks)
    p = sub.add_parser('variants'); p.add_argument('root'); p.set_defaults(f=cmd_variants)
    p = sub.add_parser('truth'); p.add_argument('root'); p.add_argument('--lanes', default='xunit,nunit,tunit,reqnroll,lightbdd')
    p.add_argument('--list', choices=list(VARIANTS)); p.set_defaults(f=cmd_truth)
    p = sub.add_parser('stability'); p.add_argument('runs', nargs='+'); p.set_defaults(f=cmd_stability)
    p = sub.add_parser('table'); p.add_argument('root'); p.add_argument('--variant', default='P', choices=list(VARIANTS)); p.set_defaults(f=cmd_table)
    p = sub.add_parser('slowest'); p.add_argument('root'); p.add_argument('--variant', default='P', choices=list(VARIANTS)); p.set_defaults(f=cmd_slowest)
    p = sub.add_parser('misses'); p.add_argument('root'); p.add_argument('--variant', default='P', choices=list(VARIANTS))
    p.add_argument('--top', type=int, default=6); p.set_defaults(f=cmd_misses)
    p = sub.add_parser('totals'); p.add_argument('root'); p.add_argument('--variant', default='P', choices=list(VARIANTS)); p.set_defaults(f=cmd_totals)
    p = sub.add_parser('ledger'); p.add_argument('path'); p.add_argument('since', nargs='?', default='2026-09-14')
    p.add_argument('until', nargs='?', default='2026-09-30T23:59:59Z'); p.set_defaults(f=cmd_ledger)
    a = ap.parse_args()
    a.f(a)


if __name__ == '__main__':
    main()
