"""Composition of TestRunReport.json files: prints sizes only, never content.

usage: python measure_json.py <TestRunReport.json>...
"""
import base64, collections, gzip, json, sys


def ser(o):
    return len(json.dumps(o, separators=(',', ':'), ensure_ascii=False).encode('utf-8'))


def z(s):
    return {"$z": base64.b64encode(gzip.compress(s.encode('utf-8'), 9)).decode()}


def by_key(o, acc, cnt):
    """Total UTF-8 bytes of string values, by the key that holds them (any depth)."""
    if isinstance(o, dict):
        for k, v in o.items():
            if isinstance(v, str):
                acc[k] += len(v.encode('utf-8'))
                cnt[k] += 1
            elif isinstance(v, list) and v and all(isinstance(x, str) for x in v):
                acc[k + '[]'] += sum(len(x.encode('utf-8')) for x in v)
                cnt[k + '[]'] += len(v)
            else:
                by_key(v, acc, cnt)
    elif isinstance(o, list):
        for x in o:
            by_key(x, acc, cnt)


def option1(o, skip_segments):
    """#85's reproduction: diagrams entries and content strings over 256 chars become $z."""
    if isinstance(o, dict):
        for k, v in list(o.items()):
            if skip_segments and k == 'internalFlowSegments':
                continue
            if k == 'diagrams' and isinstance(v, list):
                o[k] = [z(x) if isinstance(x, str) and len(x) > 256 else x for x in v]
            elif k == 'content' and isinstance(v, str) and len(v) > 256:
                o[k] = z(v)
            else:
                option1(v, skip_segments)
    elif isinstance(o, list):
        for x in o:
            option1(x, skip_segments)


for path in sys.argv[1:]:
    raw = open(path, 'rb').read()
    d = json.loads(raw)
    print(f'== {path}')
    print(f'   bytes {len(raw):,}  formatVersion={d.get("formatVersion")}  '
          f'mergeableFormatVersion={d.get("mergeableFormatVersion")}  '
          f'generator={str(d.get("generator") or d.get("kronikolVersion") or "")[:40]}')
    tops = sorted(((ser(v), k) for k, v in d.items()), reverse=True)
    print('   top-level keys by serialized bytes:')
    for n, k in tops[:10]:
        print(f'     {k:32} {n:>12,}  {100 * n / len(raw):5.1f}%')
    acc, cnt = collections.Counter(), collections.Counter()
    by_key(d, acc, cnt)
    print('   string bytes by holding key (any depth):')
    for k, n in acc.most_common(12):
        print(f'     {k:32} {n:>12,}  x{cnt[k]:<7,}  {100 * n / len(raw):5.1f}%')
    if 'internalFlowSegments' in d:
        segs = d['internalFlowSegments']
        inner = collections.Counter()
        for v in segs.values():
            if isinstance(v, dict):
                for k2, v2 in v.items():
                    inner[k2] += ser(v2)
        print(f'   internalFlowSegments: {len(segs):,} entries; by field: '
              + ', '.join(f'{k}={n:,}' for k, n in inner.most_common(6)))
    for skip in (True, False):
        d2 = json.loads(raw)
        option1(d2, skip)
        o1 = json.dumps(d2, separators=(',', ':'), ensure_ascii=False).encode('utf-8')
        label = 'option 1 (segments untouched)' if skip else 'option 1 (segments content too)'
        print(f'   {label:34} {len(o1):>12,}  {100 * len(o1) / len(raw):5.1f}%')
    print(f'   {"whole-file gzip of today":34} {len(gzip.compress(raw, 6)):>12,}')

    # The same data re-written: ensure_ascii=False writes quotes as \" and leaves < > & ' + as they are, which is
    # what the relaxed encoder writes (encoders.cs), with LF line ends. It models R1.
    def dump(o, **kw):
        return json.dumps(o, ensure_ascii=False, **kw).encode('utf-8')
    d3 = json.loads(raw)
    option1(d3, True)
    print(f'   lines {raw.count(b"\n"):,}, of them CRLF {raw.count(b"\r\n"):,}')
    for label, size in (('indent 2, relaxed, LF (R1 model)', len(dump(d, indent=2))),
                        ('indent 1, relaxed, LF', len(dump(d, indent=1))),
                        ('no indent, relaxed', len(dump(d, separators=(',', ':')))),
                        ('option 1, indent 2 (R1 + R2 model)', len(dump(d3, indent=2))),
                        ('option 1, no indent', len(dump(d3, separators=(',', ':'))))):
        print(f'   {label:34} {size:>12,}  {100 * size / len(raw):5.1f}%')
