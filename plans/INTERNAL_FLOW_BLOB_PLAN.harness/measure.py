# Size and composition of the internal-flow segment block, per report.
#
#   PYTHONUTF8=1 python measure.py <TestRunReport.html> [more reports]
#
# Finds the block before this plan (`window.__iflowSegments = {...}`) or after it (the
# `iflow-segments` element), parses it, and prints: the block's share of the file; what the block
# is made of (content HTML, flame data, the gzip+base64 activity-diagram islands inside content);
# gzip (python zlib level 6, within 4% of .NET Optimal) of the block as emitted, of the block
# re-serialised by python (the like-for-like baseline), and of the block with the islands
# replaced by raw PlantUML in a data-plantuml attribute (the §9 alternative); and, from the
# puml-data block, the diagram links against the map's keys and the loop labels (F8).
import re, gzip, base64, sys, os, json, html


def find_block(d):
    m = re.search(rb'window\.__iflowSegments = ', d)
    if m:
        end = d.index(b'</script>', m.end())
        return d[m.end():end].rstrip().rstrip(b';'), 'before'
    for m in re.finditer(rb'<script id="iflow-segments"[^>]*>', d):
        start = m.end()
        if d[start:start + 1] == b'{':
            end = d.index(b'</script>', start)
            payload = json.loads(d[start:end])
            return gzip.decompress(base64.b64decode(payload['z'])), 'after'
    return None, None


def find_puml_data(d):
    # The first `<script id="puml-data"` in a report is inside a comment in the export script;
    # the element is the one whose text starts with `{`.
    for m in re.finditer(rb'<script id="puml-data"[^>]*>', d):
        start = m.end()
        if d[start:start + 1] == b'{':
            end = d.index(b'</script>', start)
            return json.loads(d[start:end])
    return None


ISLAND = r'data-plantuml-z="([A-Za-z0-9+/=]+)"'


def b64len(n):
    return 4 * ((n + 2) // 3)


for p in sys.argv[1:]:
    d = open(p, 'rb').read()
    blob, form = find_block(d)
    size = os.path.getsize(p)
    if blob is None:
        print(f"\n== {p}\n   file {size:,}  no segment block")
        continue
    print(f"\n== {p}\n   file {size:,}  block ({form}) {len(blob):,} ({100 * len(blob) / size:.1f}% of the file)")
    seg = json.loads(blob)
    contents = [v.get('content', '') for v in seg.values()]
    content_bytes = sum(len(c) for c in contents)
    flame_bytes = sum(len(json.dumps(v['flameData'], separators=(',', ':'))) for v in seg.values() if 'flameData' in v)
    messages = sum(1 for v in seg.values() if 'message' in v)
    islands = re.findall(ISLAND, ''.join(contents))
    distinct = set(islands)
    decoded = {x: gzip.decompress(base64.b64decode(x)).decode('utf-8') for x in distinct}
    island_bytes = sum(len(x) for x in islands)
    raw_bytes = sum(len(decoded[x]) for x in islands)
    print(f"   segments {len(seg):,}  distinct contents {len(set(contents)):,}  with flameData "
          f"{sum(1 for v in seg.values() if 'flameData' in v):,}  message-only {messages:,}")
    print(f"   content {content_bytes:,} ({100 * content_bytes / len(blob):.1f}% of the block)  "
          f"flameData {flame_bytes:,} ({100 * flame_bytes / len(blob):.1f}%)")
    print(f"   data-plantuml-z islands {len(islands):,} ({len(distinct):,} distinct) = {island_bytes:,} bytes "
          f"({100 * island_bytes / len(blob):.1f}% of the block); decoded {raw_bytes:,} bytes")

    gz_emitted = len(gzip.compress(blob, 6))
    reserialised = json.dumps(seg, separators=(',', ':')).encode()
    gz_reserialised = len(gzip.compress(reserialised, 6))

    def raw_attr(m):
        return 'data-plantuml="' + html.escape(decoded[m.group(1)], quote=True).replace('\n', '&#10;') + '"'

    alt = {k: dict(v) for k, v in seg.items()}
    for v in alt.values():
        if 'content' in v:
            v['content'] = re.sub(ISLAND, raw_attr, v['content'])
    alt_bytes = json.dumps(alt, separators=(',', ':')).encode()
    gz_alt = len(gzip.compress(alt_bytes, 6))
    print(f"   gzip of the block as emitted        {gz_emitted:>9,}  base64 {b64len(gz_emitted):>9,}  ({100 * b64len(gz_emitted) / len(blob):.1f}% of the block)")
    print(f"   gzip of the block re-serialised     {gz_reserialised:>9,}  (python json, the like-for-like baseline)")
    print(f"   gzip with raw PlantUML inside       {gz_alt:>9,}  base64 {b64len(gz_alt):>9,}  (raw block {len(alt_bytes):,}; "
          f"{100 * gz_alt / gz_reserialised:.1f}% of the baseline)")

    pd = find_puml_data(d)
    if pd:
        sources = [gzip.decompress(base64.b64decode(v)).decode('utf-8') for v in pd.values()]
        loops = [int(x) for s in sources for x in re.findall(r'loop ×(\d+)', s)]
        links = set(x for s in sources for x in re.findall(r'\[\[#(iflow-[^\s\]]+)', s))
        keys = set(seg.keys())
        print(f"   puml-data diagrams {len(pd):,}; distinct links {len(links):,}; links not in the map {len(links - keys):,}; "
              f"keys linked from no diagram {len(keys - links):,}")
        print(f"   loop labels {len(loops):,}; sum of (N-1) over them {sum(n - 1 for n in loops):,}; "
              f"largest N {max(loops) if loops else 0}")
