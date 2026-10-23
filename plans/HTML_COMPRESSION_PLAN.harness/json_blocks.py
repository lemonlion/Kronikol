"""Shape of the JSON script blocks in a Kronikol HTML report: types, counts, string-size classes. No content."""
import json
import re
import sys
from collections import Counter

path = sys.argv[1]
text = open(path, 'rb').read().decode('utf-8')
B64 = re.compile(r'^[A-Za-z0-9+/_-]+={0,2}$')

def walk(v, stats, depth=0, keypath='$'):
    if isinstance(v, dict):
        stats['dict'] += 1
        for k, x in v.items():
            stats['keys'][re.sub(r'\d+', '#', k)[:40]] += 1
            walk(x, stats, depth + 1, keypath)
    elif isinstance(v, list):
        stats['list'] += 1
        for x in v:
            walk(x, stats, depth + 1, keypath)
    elif isinstance(v, str):
        n = len(v.encode('utf-8'))
        stats['str'] += 1
        stats['strbytes'] += n
        if n >= 200 and B64.match(v):
            stats['b64'] += 1
            stats['b64bytes'] += n
        if v.startswith('@start') or '@startuml' in v[:200]:
            stats['puml'] += 1
            stats['pumlbytes'] += n
        if n > stats['maxstr']:
            stats['maxstr'] = n
    else:
        stats[type(v).__name__] += 1

for m in re.finditer(r'<script id="([^"]+)" type="application/json"[^>]*>(.*?)</script>', text, re.S):
    sid, body = m.group(1), m.group(2)
    stats = Counter()
    stats['keys'] = Counter()
    stats['maxstr'] = 0
    try:
        data = json.loads(body)
    except Exception as e:
        print(f"== {sid}: {len(body):,} chars, NOT JSON ({type(e).__name__}); first 40 chars class: "
              f"{'base64-like' if B64.match(body.strip()[:4000]) else 'other'}")
        continue
    walk(data, stats)
    top = type(data).__name__
    n = len(data) if hasattr(data, '__len__') else '-'
    print(f"== {sid}: {len(body.encode('utf-8')):,} bytes, top={top} len={n}")
    print(f"   dicts={stats['dict']:,} lists={stats['list']:,} strings={stats['str']:,} ({stats['strbytes']:,} B, max {stats['maxstr']:,})"
          f" base64-like>=200B={stats['b64']:,} ({stats['b64bytes']:,} B) plantuml={stats['puml']:,} ({stats['pumlbytes']:,} B)"
          f" int={stats['int']:,} float={stats['float']:,} bool={stats['bool']:,} null={stats['NoneType']:,}")
    print("   keys:", ', '.join(f"{k}={c:,}" for k, c in stats['keys'].most_common(12)))
