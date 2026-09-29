"""R4 (#86): how much of a report's internal-flow segment map is the same flow stored again. Prints numbers only.

python dedup.py <TestRunReport.html>...

Reads the page's segment element (its `z`, decoded), as #86 read `__iflowSegments`: per segment, `content` with every
GUID normalised (the segment's id appears inside its rendered content), and `flameData`. For each, the segments that
carry one, the distinct values, the bytes shipped and the bytes if each distinct value were stored once. The page's map
and the mergeable file's are one map rendered twice (InternalFlowSegmentMapReportTests), so the page suffices."""
import base64
import gzip
import json
import re
import sys

GUID = re.compile(r'[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}')
HEAD = '<script id="iflow-segments" type="application/json">'


def segment_map(path):
    html = open(path, encoding='utf-8').read()
    at = html.find(HEAD)
    if at < 0:
        raise SystemExit(f'{path}: no segment element')
    start = at + len(HEAD)
    element = json.loads(html[start:html.index('</script>', start)])
    return json.loads(gzip.decompress(base64.b64decode(element['z'])).decode('utf-8'))


def table(name, values):
    shipped = sum(len(v.encode('utf-8')) for v in values)
    distinct = set(values)
    once = sum(len(v.encode('utf-8')) for v in distinct)
    copies = max((values.count(v) for v in distinct), default=0) if len(values) < 20000 else 'n/a'
    redundant = shipped - once
    share = f'{redundant / shipped * 100:.0f}%' if shipped else '-'
    print(f'  {name}: {len(values)} carried, {len(distinct)} distinct'
          f' ({len(values) / len(distinct) if distinct else 0:.1f}x), most copies {copies},'
          f' {shipped:,} bytes shipped, {once:,} if stored once, redundant {redundant:,} ({share})')


def packed(obj):
    """The element's `z` for a map: base64 of the gzip of its compact JSON (the writer's form, near enough)."""
    return base64.b64encode(gzip.compress(json.dumps(obj, separators=(',', ':'), ensure_ascii=False).encode('utf-8'), 6))


for path in sys.argv[1:]:
    data = segment_map(path)
    contents, flames = [], []
    # A layout that stores each distinct flow once: the segments keep their title and point at a content and a flame.
    content_ids, flame_ids, pointers = {}, {}, {}
    for key, value in data.items():
        if isinstance(value, dict):
            pointer = {k: v for k, v in value.items() if k not in ('content', 'flameData')}
            if isinstance(value.get('content'), str):
                normal = GUID.sub('<guid>', value['content'])
                contents.append(normal)
                pointer['c'] = content_ids.setdefault(normal, len(content_ids))
            if value.get('flameData') is not None:
                flame = json.dumps(value['flameData'], separators=(',', ':'), ensure_ascii=False)
                flames.append(flame)
                pointer['f'] = flame_ids.setdefault(flame, len(flame_ids))
            pointers[key] = pointer
        else:
            pointers[key] = value
    print(f'{path.replace(chr(92), "/").split("/")[-1]}: {len(data)} segments')
    table('content', contents)
    table('flameData', flames)
    once = {'s': pointers, 'c': list(content_ids), 'f': [json.loads(f) for f in flame_ids]}
    as_is_json = len(json.dumps(data, separators=(',', ':'), ensure_ascii=False).encode('utf-8'))
    once_json = len(json.dumps(once, separators=(',', ':'), ensure_ascii=False).encode('utf-8'))
    as_is_z, once_z = len(packed(data)), len(packed(once))
    print(f'  element z: {as_is_z:,} bytes as shipped, {once_z:,} storing each flow once ({once_z / as_is_z * 100:.0f}%);'
          f' decoded: {as_is_json:,} against {once_json:,} ({once_json / as_is_json * 100:.0f}%)')
