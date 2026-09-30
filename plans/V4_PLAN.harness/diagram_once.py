"""4.0.1 (each distinct diagram and flame chart stored once, in a table at the end of the segment map): check and
measure a page written by 4.0.1 or later. Prints numbers only.

python diagram_once.py <TestRunReport.html>...

Decodes the page's segment element; counts the segments that hold a flow (naming their places in the table), name
another's flow (sameAs) or carry a message, and the tables; resolves every segment as the popup script does (sameAs to
the flow, then the flow's places in the table it names) and fails if any name or place leads nowhere; then puts each
flow back inline, which is the map 4.0.0 wrote for the same run, and compares the two element sizes and decoded sizes."""
import importlib.util
import json
import os
import sys

spec = importlib.util.spec_from_file_location('dedup', os.path.join(os.path.dirname(os.path.abspath(__file__)), 'dedup.py'))
dedup = importlib.util.module_from_spec(spec)
spec.loader.exec_module(dedup)


def resolve(data, key):
    """The popup script's rule: the flow's diagram and flame chart, or None where a name or a place leads nowhere."""
    segment = data[key]
    flow = data.get(segment['sameAs']) if 'sameAs' in segment else segment
    if flow is None:
        return None
    if 'table' not in flow:
        return (flow['content'], flow.get('flameData')) if 'content' in flow else None
    table = data.get(flow['table'])
    try:
        return table['contents'][flow['contentAt']], (table['flames'][flow['flameAt']] if 'flameAt' in flow else None)
    except (TypeError, KeyError, IndexError):
        return None


failures = 0
for path in sys.argv[1:]:
    data = dedup.segment_map(path)
    counts = {'holds a flow': 0, 'sameAs': 0, 'message': 0, 'tables': 0}
    broken = 0
    for key, value in data.items():
        if key.startswith('~'):
            counts['tables'] += 1
            continue
        if 'sameAs' in value:
            counts['sameAs'] += 1
        elif 'table' in value:
            counts['holds a flow'] += 1
        elif 'message' in value:
            counts['message'] += 1
        if 'message' not in value and resolve(data, key) is None:
            broken += 1
    failures += broken

    # 4.0.0's map for the same run: each flow back inline in its holder, in the segment's place, and no table.
    inline = {}
    for key, value in data.items():
        if key.startswith('~'):
            continue
        if 'table' in value:
            content, flame = resolve(data, key)
            inline[key] = {**({'title': value['title']} if 'title' in value else {}), 'content': content,
                           **({'flameData': flame} if flame is not None else {})}
        else:
            inline[key] = value
    tables = [v for k, v in data.items() if k.startswith('~')]
    z, z_before = len(dedup.packed(data)), len(dedup.packed(inline))
    decoded = len(json.dumps(data, separators=(',', ':'), ensure_ascii=False).encode('utf-8'))
    decoded_before = len(json.dumps(inline, separators=(',', ':'), ensure_ascii=False).encode('utf-8'))
    print(f'{path.replace(chr(92), "/").split("/Reports/")[0].split("/")[-4] if "/Reports/" in path.replace(chr(92), "/") else path}:'
          f' {len(data)} keys {counts}, {sum(len(t["contents"]) for t in tables)} distinct diagrams and'
          f' {sum(len(t["flames"]) for t in tables)} flame charts in the table; names that lead nowhere {broken};'
          f' element z {z_before:,} as 4.0.0 wrote it -> {z:,} ({(z_before - z) / z_before * 100:.1f}% less);'
          f' decoded {decoded_before:,} -> {decoded:,} ({(decoded_before - decoded) / decoded_before * 100:.1f}% less);'
          f' page {os.path.getsize(path):,}')
sys.exit(1 if failures else 0)
