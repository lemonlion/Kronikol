"""Every storage figure in HISTORY_DASHBOARD_STORE_PLAN.md, from real reports and a real ledger.

    py -3.14 measure.py <reports-root> [<history.jsonl>]

<reports-root> is any tree holding TestRunReport.json files; the plan used
C:\\Code\\BreakfastProvider\\tests. The ledger argument is optional:

    git -C <consumer> fetch origin kronikol-history
    git -C <consumer> show FETCH_HEAD:history.jsonl > history.jsonl

Needs Python 3.14 (compression.zstd). Prints numbers only, never report content.
"""
import datetime
import gzip
import json
import os
import random
import re
import sys
import uuid
from collections import Counter, defaultdict
from compression import zstd


def J(value):
    return json.dumps(value, separators=(',', ':'))


def zs(payload, level=19, window_log=31):
    """zstd with long-distance matching, so cross-run redundancy is actually found."""
    return len(zstd.compress(payload, options={
        zstd.CompressionParameter.compression_level: level,
        zstd.CompressionParameter.enable_long_distance_matching: 1,
        zstd.CompressionParameter.window_log: window_log}))


GUID = re.compile(r'[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}')
HEX = re.compile(r'(?<![0-9A-Za-z])[0-9a-fA-F]{16,}(?![0-9A-Za-z])')
TS = re.compile(r'\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?')
DUR = re.compile(r'"durationSeconds":\d+\.\d+')
ANY_ID = re.compile('|'.join('(?:%s)' % p.pattern for p in (GUID, HEX)))
VAR = re.compile('|'.join('(?:%s)' % p.pattern for p in (GUID, HEX, TS)))

# InteractionShape.Target and .Calls read exactly these fields; a statement head is
# taken only for a statement-shaped dependency.
SHAPE_FIELDS = ('uri', 'method', 'serviceName', 'callerName', 'statusCode', 'dependencyCategory')
STATEMENT_HINTS = ('database', 'sql', 'cosmos', 'mongo', 'clickhouse', 'bigquery', 'storage', 'queue')
NEWLINE = chr(10)


def reports(root):
    found = [os.path.join(r, n) for r, _, ns in os.walk(root) for n in ns if n == 'TestRunReport.json']
    return sorted(found, key=lambda p: -os.path.getsize(p))


def lane(path):
    for part in path.split(os.sep):
        if part.startswith('BreakfastProvider.Tests.'):
            return part.replace('BreakfastProvider.Tests.Component.', '')
    return os.path.basename(os.path.dirname(os.path.dirname(path)))


def interactions(doc):
    for feature in doc['features']:
        for scenario in feature['scenarios']:
            yield from (scenario.get('httpInteractions') or [])


def joined(values):
    return NEWLINE.join(values).encode('utf8', 'replace')


def renumber(values):
    """Ids are identity, not data: within a run they only have to join."""
    seen = {}
    return [seen.setdefault(v, str(len(seen))) for v in values]


def section(title):
    print(NEWLINE + '=' * 72)
    print(title)
    print('=' * 72)


def s1_sizes(paths):
    section('1. Raw report sizes')
    for path in paths:
        print(f"  {os.path.getsize(path):>12,}  {lane(path)}")


def s2_composition(doc, raw_len):
    section('2. Where the bytes are (compact JSON)')
    compact = len(J(doc))
    print(f"  raw {raw_len:,}  compact {compact:,}  (indent overhead {100 - 100 * compact / raw_len:.0f}%)")
    per_field = Counter()
    count = 0
    for feature in doc['features']:
        for scenario in feature['scenarios']:
            count += 1
            for key, value in scenario.items():
                per_field[key] += len(J(value))
    print(f"  over {count} scenarios:")
    for key, size in per_field.most_common(6):
        print(f"    {size:>10,}  {100 * size / compact:5.1f}%  {key}")


def s3_compression(doc, raw):
    section('3. One run, every storage model')
    compact = J(doc).encode()
    ids = {}
    renumbered = ANY_ID.sub(lambda m: ids.setdefault(m.group(0), 'k%d' % len(ids)), J(doc))
    stripped = json.loads(J(doc))
    for feature in stripped['features']:
        for scenario in feature['scenarios']:
            scenario.pop('diagrams', None)
    no_diagrams = ANY_ID.sub(lambda m: ids.get(m.group(0), m.group(0)), J(stripped))
    rows = [
        ('raw pretty JSON', len(raw)),
        ('compact JSON', len(compact)),
        ('gzip(raw)', len(gzip.compress(raw, 6))),
        ('zstd-19(compact)', zs(compact)),
        ('  + ids renumbered', zs(renumbered.encode())),
        ('  + diagrams dropped (derived)', zs(no_diagrams.encode())),
    ]
    for label, size in rows:
        print(f"  {label:<32} {size:>10,}")
    print(f"  {len(ids):,} distinct ids renumbered")


def s3b_lossless(doc):
    """Which id renumbering is lossless, and what the lossy one really costs (plan section 2.1).

    Renumbering an id inside a payload stores a redacted run. Restoring it needs the original
    values back, and that mapping costs nearly everything the renumbering saved.
    """
    section('3b. Lossless vs lossy id renumbering')
    stripped = json.loads(J(doc))
    for feature in stripped['features']:
        for scenario in feature['scenarios']:
            scenario.pop('diagrams', None)
    lossless_base = zs(J(stripped).encode())
    print(f"  B. minus derived diagrams                     {lossless_base:>9,}  lossless")

    ids = {}
    lossy = ANY_ID.sub(lambda m: ids.setdefault(m.group(0), 'k%d' % len(ids)), J(stripped))
    lossy_size = zs(lossy.encode())
    # The mapping back, stored as raw bytes rather than hex text: the cheapest honest form.
    raw_map = b''.join(bytes.fromhex(k.replace('-', '')) for k in ids
                       if len(k.replace('-', '')) % 2 == 0)
    map_size = zs(raw_map)
    print(f"  C. + EVERY id renumbered                      {lossy_size:>9,}  LOSSY ({len(ids):,} ids)")
    print(f"     mapping back to the originals              {map_size:>9,}")
    print(f"     C + mapping, the true cost of C            {lossy_size + map_size:>9,}"
          f"  ({100 - 100 * (lossy_size + map_size) / lossless_base:.1f}% saved, not"
          f" {100 - 100 * lossy_size / lossless_base:.0f}%)")

    # Kronikol's own join keys: renumberable because their literal values mean nothing outside the run.
    keys = ('requestResponseId', 'traceId')
    safe = json.loads(J(stripped))
    maps = {k: {} for k in keys}
    for feature in safe['features']:
        for scenario in feature['scenarios']:
            for record in (scenario.get('httpInteractions') or []):
                for key in keys:
                    value = record.get(key)
                    if isinstance(value, str) and value:
                        record[key] = maps[key].setdefault(value, str(len(maps[key])))
    print(f"  D. + join keys renumbered only                {zs(J(safe).encode()):>9,}  LOSSLESS"
          f"  <-- the engineered figure")

    # The safety check that makes D lossless: do those ids appear in the evidence?
    blob = NEWLINE.join(str(r.get('content') or '') + J(r.get('headers') or [])
                        + str(r.get('uri') or '') for r in interactions(stripped))
    leaked = sum(1 for v in maps['requestResponseId'] if v in blob)
    print(f"  requestResponseIds appearing inside content/headers/uri: {leaked:,}"
          f" of {len(maps['requestResponseId']):,}"
          f"  ({'SAFE' if leaked == 0 else 'NOT SAFE, D is lossy'})")


def s4_entropy_floor(doc):
    section('4. Irreducible entropy per run')
    distinct = list(dict.fromkeys(VAR.findall(J(doc))))
    guids = sum(1 for v in distinct if GUID.fullmatch(v))
    hexes = sum(1 for v in distinct if HEX.fullmatch(v))
    stamps = sum(1 for v in distinct if TS.fullmatch(v))
    floor = guids * 16 + hexes * 8 + stamps * 3
    print(f"  {guids} guids x16B + {hexes} hex x8B + {stamps} timestamps x~3B delta = {floor:,} B")


def s5_reruns(doc, count=8):
    """Cost of storing N re-runs in which nothing behavioural changed: only ids and clocks moved."""
    section(f'5. {count} re-runs, nothing changed, naive zstd over the concatenation')
    random.seed(7)
    base = J(doc)

    def rerun(text, minutes):
        mapping = {}

        def fresh(match):
            value = match.group(0)
            if value not in mapping:
                mapping[value] = (str(uuid.UUID(int=random.getrandbits(128))) if GUID.fullmatch(value)
                                  else ('%x' % random.getrandbits(4 * len(value)))[:len(value)].ljust(len(value), '0'))
            return mapping[value]

        shifted = ANY_ID.sub(fresh, text)
        shifted = TS.sub(lambda m: (datetime.datetime.fromisoformat(m.group(0)[:19])
                                    + datetime.timedelta(minutes=minutes)).isoformat(), shifted)
        return DUR.sub(lambda _: '"durationSeconds":%.4f' % random.uniform(0.002, 0.4), shifted)

    stream = bytearray(base.encode())
    previous = zs(base.encode())
    print(f"  {'runs':>5} {'cumulative':>12} {'incremental':>12} {'per run':>10}")
    print(f"  {1:>5} {previous:>12,} {previous:>12,} {previous:>10,}")
    for i in range(2, count + 1):
        stream += rerun(base, 37 * i).encode()
        total = zs(bytes(stream))
        print(f"  {i:>5} {total:>12,} {total - previous:>12,} {total // i:>10,}")
        previous = total


def s6_columns(doc):
    section('6. Columnar layout, and what the correlation ids cost')
    columns = defaultdict(list)
    count = 0
    for record in interactions(doc):
        count += 1
        for key, value in record.items():
            columns[key].append(value if isinstance(value, str) else J(value))
    rows = sorted(((zs(joined(vs)), key, len(set(vs))) for key, vs in columns.items()), reverse=True)
    print(f"  {count:,} interactions -> {len(columns)} columns")
    for size, key, distinct in rows[:10]:
        print(f"    zstd {size:>8,}  {distinct:>6,} distinct  {key}")
    total = sum(row[0] for row in rows)
    before = after = 0
    for key in ('requestResponseId', 'traceId', 'activityTraceId', 'activitySpanId'):
        if key in columns:
            before += zs(joined(columns[key]))
            after += zs(joined(renumber(columns[key])))
    print(f"  columnar total {total:,}; correlation ids {before:,} ({100 * before / total:.0f}%)"
          f" -> {after:,} renumbered")
    row_wise = zs(J(list(interactions(doc))).encode())
    print(f"  row-wise, same data: zstd {row_wise:,}"
          f"  (columnar advantage on SIZE: {100 - 100 * total / row_wise:.0f}%)")


def s7_retemplatable(doc):
    """What must survive for a fingerprint to be rebuildable under a FUTURE InteractionShape.Version."""
    section('7. The re-templatable core: the columns a rule change needs back')
    columns = defaultdict(list)
    count = statements = 0
    for record in interactions(doc):
        count += 1
        for key in SHAPE_FIELDS:
            columns[key].append(str(record.get(key) or ''))
        columns['pairId'].append(str(record.get('requestResponseId') or ''))
        category = str(record.get('dependencyCategory') or '').lower()
        content = record.get('content') or ''
        statement_shaped = bool(content) and any(hint in category for hint in STATEMENT_HINTS)
        if statement_shaped:
            statements += 1
        columns['stmtHead'].append(content.split(NEWLINE, 1)[0][:2000] if statement_shaped else '')
    total = 0
    for key in list(SHAPE_FIELDS) + ['pairId', 'stmtHead']:
        size = zs(joined(columns[key]))
        total += size
        print(f"    zstd {size:>8,}  {key}")
    core = total - zs(joined(columns['pairId'])) + zs(joined(renumber(columns['pairId'])))
    print(f"  {count:,} interactions, {statements:,} statement-shaped")
    print(f"  RE-TEMPLATABLE CORE {total:,}; with pairId renumbered {core:,}")


def s8_cross_lane(paths):
    section('8. Cross-run redundancy: a second lane given the first is stored')
    docs = {lane(p): J(json.loads(open(p, 'rb').read())).encode() for p in paths}
    first = list(docs)[0]
    base = zs(docs[first])
    print(f"  {first} alone: {base:,}")
    for name, body in docs.items():
        if name == first:
            continue
        alone = zs(body)
        both = zs(docs[first] + body)
        print(f"    +{name:<10} alone {alone:>9,}  incremental {both - base:>9,}"
              f"  ({100 * (both - base) / alone:.0f}% of alone)")


def s9_ledger(path):
    section('9. The ledger: the verdict layer, on real CI data')
    raw = open(path, 'rb').read()
    lines = [line for line in raw.split(b'\n') if line.strip()]
    kinds, sizes, runs = Counter(), Counter(), []
    for line in lines:
        obj = json.loads(line)
        kind = obj.get('t', 'run' if 'runId' in obj else '?')
        kinds[kind] += 1
        sizes[kind] += len(line)
        if kind == 'run':
            runs.append(obj)
    print(f"  {len(raw):,} B, {len(lines):,} lines")
    for kind, count in kinds.most_common():
        print(f"    {kind:<8} {count:>5} lines  {sizes[kind]:>10,} B ({100 * sizes[kind] / len(raw):4.1f}%)")
    compressed = zs(raw)
    print(f"  gzip {len(gzip.compress(raw, 9)):,}   zstd-19 {compressed:,}"
          f"   ({len(raw) / compressed:.0f}x, {compressed // max(len(runs), 1):,} B per run)")
    stamps = sorted(run['at'] for run in runs if run.get('at'))
    if stamps:
        first = datetime.datetime.fromisoformat(stamps[0].replace('Z', '+00:00'))
        last = datetime.datetime.fromisoformat(stamps[-1].replace('Z', '+00:00'))
        span = (last - first).total_seconds() / 86400
        suites = len({run['suite'] for run in runs})
        scenario_runs = sum(len(run.get('results', '')) for run in runs)
        print(f"  {len(runs)} runs over {span:.1f} days = {len(runs) / span:.1f} runs/day"
              f" across {suites} suites; {scenario_runs:,} scenario-runs")
    if runs:
        last_line = runs[-1]
        fingerprints = sum(len(J(last_line.get(key, ''))) for key in ('callSets', 'shapeSet', 'shapeOrdered'))
        line_bytes = len(J(last_line))
        print(f"  last run line {line_bytes:,} B; fingerprints (callSets+shapeSet+shapeOrdered)"
              f" {fingerprints:,} = {100 * fingerprints / line_bytes:.0f}% of it")


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    paths = reports(sys.argv[1])
    if not paths:
        print(f"no TestRunReport.json under {sys.argv[1]}")
        return 2
    raw = open(paths[0], 'rb').read()
    doc = json.loads(raw)
    print(f"largest report: {lane(paths[0])}")
    s1_sizes(paths)
    s2_composition(doc, len(raw))
    s3_compression(doc, raw)
    s3b_lossless(doc)
    s4_entropy_floor(doc)
    s5_reruns(doc)
    s6_columns(doc)
    s7_retemplatable(doc)
    if len(paths) > 1:
        s8_cross_lane(paths)
    if len(sys.argv) > 2 and os.path.exists(sys.argv[2]):
        s9_ledger(sys.argv[2])
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
