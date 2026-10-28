"""Per URI scheme in a decoded data file: request counts by (scheme, caller-or-callee side, first word of the method,
whether the URI has an authority, whether its path has a segment, dependency category, meta type). Counts only; prints
no body. Usage: python -I -X utf8 revision-schemes.py <decoded.json>..."""
import collections, json, sys
from urllib.parse import urlsplit
for path in sys.argv[1:]:
    d = json.load(open(path, encoding="utf-8"))
    rows = collections.Counter()
    def take(lst):
        for i in lst or []:
            if i.get("type") != "Request": continue
            uri = i.get("uri") or ""
            try:
                u = urlsplit(uri)
                scheme, auth, p = u.scheme, u.netloc, u.path
            except ValueError:
                scheme, auth, p = "?", "", ""
            seg = [s for s in p.split("/") if s]
            m = (i.get("method") or "").split(" ")[0]
            rows[(scheme, m, bool(auth), len(seg), i.get("dependencyCategory") or "-", i.get("callerDependencyCategory") or "-", i.get("metaType") or "-")] += 1
    for f in d.get("features") or []:
        for s in f.get("scenarios") or []:
            take(s.get("httpInteractions"))
    bg = d.get("background") or {}
    take(bg.get("interactions"))
    print("==", path.replace("\\", "/").split("/")[-1])
    for k, v in sorted(rows.items(), key=lambda kv: (kv[0][0], -kv[1])):
        if k[0] in ("http", "https"): continue
        print(f"  {v:5}  scheme={k[0]} method={k[1]} authority={k[2]} segments={k[3]} cat={k[4]} callerCat={k[5]} meta={k[6]}")
    http = sum(v for k, v in rows.items() if k[0] in ("http", "https"))
    print(f"  http/https requests: {http}")
