# What each variant of a report weighs on disk and on the wire, and whether it binds the same arrows.
#
#   PYTHONUTF8=1 python variants.py <TestRunReport.html> [more reports] [--write DIR]
#
# Builds, from a report written before the plan (the `window.__iflowSegments = {...}` block):
#   today   the file as it is
#   fix     today's format without the segments no diagram links: the marker fix alone (§6 F8)
#   s1      the block replaced by the iflow-segments element: the map as emitted, gzip level 6,
#           base64, and the shorter of the two membership lists (§3.3)
#   s1+fix  s1 over the fixed map
#   q7      s1 with the map's data-plantuml-z islands carried as raw PlantUML (§3.7)
#   q7+fix  q7 over the fixed map
# and prints each one's size and its size gzipped at level 6, which is what a GitHub Pages visitor
# downloads (Pages serves gzip, never brotli, measured 2026-09-25; python level 6 came within 2% of
# the served Content-Length) and about what a zipped CI artifact holds. The map is serialised the way
# System.Text.Json's default encoder writes it (checked byte for byte against the report's own block),
# so the only approximation is the gzip: python zlib level 6, 2% to 4% under .NET's Optimal.
#
# For every variant it also checks the arrows that would open a popup: today, a link whose id is a
# key of the map; after, the id the element's list admits (§3.3). "binds the same arrows" must say yes.
# --write keeps the variant files, for load-pages.js and popup-smoke.js.
#
# On a report written after R2 (the iflow-segments element) it builds nothing. It prints the file, its
# gzip and the segments no diagram links (0 after R1, apart from calls whose test id no scenario has),
# and whether the element's list admits exactly the links whose id is a key: the §8.6 check.
import re, gzip, base64, sys, os, json, html

ISLAND = r'data-plantuml-z="([A-Za-z0-9+/=]+)"'
ESCAPES = {'"': '\\u0022', '\\': '\\\\', '<': '\\u003C', '>': '\\u003E', '&': '\\u0026', "'": '\\u0027',
           '+': '\\u002B', '`': '\\u0060', '\n': '\\n', '\r': '\\r', '\t': '\\t', '\b': '\\b', '\f': '\\f'}


def net_str(s):
    out = []
    for ch in s:
        if ch in ESCAPES:
            out.append(ESCAPES[ch])
        elif ord(ch) < 0x20 or ord(ch) > 0x7E:
            b = ch.encode('utf-16-be')
            out.extend('\\u%04X' % int.from_bytes(b[i:i + 2], 'big') for i in range(0, len(b), 2))
        else:
            out.append(ch)
    return '"' + ''.join(out) + '"'


def net_json(o):
    """System.Text.Json, default (HTML-safe) encoder, not indented."""
    if isinstance(o, dict):
        return '{' + ','.join(net_str(k) + ':' + net_json(v) for k, v in o.items()) + '}'
    if isinstance(o, list):
        return '[' + ','.join(net_json(v) for v in o) + ']'
    if isinstance(o, str):
        return net_str(o)
    if o is True:
        return 'true'
    if o is False:
        return 'false'
    if o is None:
        return 'null'
    if isinstance(o, float) and o.is_integer():
        return str(int(o))
    return json.dumps(o)


def find_block(d):
    m = re.search(rb'<script>window\.__iflowSegments = ', d)
    if not m:
        return None
    end = d.index(b'</script>', m.end()) + len(b'</script>')
    body = d[m.end():end - len(b'</script>')].rstrip().rstrip(b';')
    return m.start(), end, body


def puml_links(d):
    for m in re.finditer(rb'<script id="puml-data"[^>]*>', d):
        start = m.end()
        if d[start:start + 1] == b'{':
            end = d.index(b'</script>', start)
            pd = json.loads(d[start:end])
            sources = [gzip.decompress(base64.b64decode(v)).decode('utf-8') for v in pd.values()]
            return [x for s in sources for x in re.findall(r'\[\[#(iflow-[^\s\]]+)', s)]
    return []


def literal(seg):
    return b'<script>window.__iflowSegments = ' + net_json(seg).encode() + b';</script>'


def element(seg, links_in_order):
    """(bytes, payload); no segments, no element (§3.1)."""
    if not seg:
        return b'', None
    keys = set(seg)
    seen, linked = set(), []
    for x in links_in_order:
        if x not in seen:
            seen.add(x)
            linked.append(x)
    hidden = [x for x in linked if x not in keys]
    has = [x for x in linked if x in keys]
    payload = {'hidden': hidden} if len(hidden) < len(has) else {'has': has}
    payload['z'] = base64.b64encode(gzip.compress(net_json(seg).encode(), 6)).decode()
    return (b'<script id="iflow-segments" type="application/json">'
            + json.dumps(payload, separators=(',', ':')).encode() + b'</script>'), payload


def find_element(d):
    for m in re.finditer(rb'<script id="iflow-segments" type="application/json">', d):
        start = m.end()
        if d[start:start + 1] == b'{':
            end = d.index(b'</script>', start)
            payload = json.loads(d[start:end])
            return payload, json.loads(gzip.decompress(base64.b64decode(payload['z'])))
    return None, None


def after_report(p, d):
    payload, seg = find_element(d)
    linked = set(puml_links(d))
    gz = len(gzip.compress(d, 6))
    if payload is None:
        print(f'\n== {p}\n   no segment block and no iflow-segments element (an empty map emits none)'
              f'\n   file {len(d):,}   gzip-6 {gz:,}   diagram links {len(linked):,}, none of them bound')
        return
    kind = 'hidden' if 'hidden' in payload else 'has'
    exact = binds(seg, payload, linked, 'element') == binds(seg, None, linked, 'literal')
    print(f'\n== {p}\n   element: {len(seg):,} segments; list {kind} of {len(payload[kind]):,} ids; '
          f'no diagram links {len(set(seg) - linked):,}\n   file {len(d):,}   gzip-6 {gz:,}   '
          f'the list admits exactly the linked keys: {"yes" if exact else "NO"}')


def binds(seg, payload, links, form):
    """The link ids that would open a popup."""
    if form == 'literal':
        return {x for x in links if x in seg}
    if payload is None:
        return set()
    if 'has' in payload:
        return {x for x in links if x in set(payload['has'])}
    hidden = set(payload['hidden'])
    return {x for x in links if x not in hidden}


def raw_sources(seg):
    cache = {}

    def sub(m):
        z = m.group(1)
        if z not in cache:
            cache[z] = gzip.decompress(base64.b64decode(z)).decode('utf-8')
        return 'data-plantuml="' + html.escape(cache[z], quote=True).replace('\n', '&#10;') + '"'

    out = {}
    for k, v in seg.items():
        v = dict(v)
        if 'content' in v:
            v['content'] = re.sub(ISLAND, sub, v['content'])
        out[k] = v
    return out


def main(args):
    out_dir = None
    if '--write' in args:
        i = args.index('--write')
        out_dir = args[i + 1]
        del args[i:i + 2]
        os.makedirs(out_dir, exist_ok=True)
    for p in args:
        d = open(p, 'rb').read()
        found = find_block(d)
        if not found:
            after_report(p, d)
            continue
        start, end, body = found
        seg = json.loads(body)
        exact = net_json(seg).encode() == body
        links = puml_links(d)
        linked = set(links)
        fixed = {k: v for k, v in seg.items() if k in linked}
        q7 = raw_sources(seg)
        q7_fixed = {k: v for k, v in q7.items() if k in linked}
        today_binds = binds(seg, None, linked, 'literal')

        variants = [('today', d, today_binds)]
        fix_block = literal(fixed) if fixed else b''
        variants.append(('fix', d[:start] + fix_block + d[end:], binds(fixed, None, linked, 'literal')))
        s1 = None
        for name, m in (('s1', seg), ('s1+fix', fixed), ('q7', q7), ('q7+fix', q7_fixed)):
            el, payload = element(m, links)
            if name == 's1':
                s1 = payload
            variants.append((name, d[:start] + el + d[end:], binds(m, payload, linked, 'element')))

        kind = ('hidden' if 'hidden' in s1 else 'has') if s1 else 'none'
        print(f'\n== {p}\n   segments {len(seg):,}; no diagram links {len(seg) - len(fixed):,}; '
              f's1 list {kind} of {len(s1[kind]) if s1 else 0:,} ids; serialiser matches the block: {exact}')
        base = None
        for name, data, bound in variants:
            raw, gz = len(data), len(gzip.compress(data, 6))
            if base is None:
                base = (raw, gz)
            same = 'yes' if bound == today_binds else f'NO ({len(bound ^ today_binds)} differ)'
            print(f'   {name:<7} file {raw:>11,} ({100 * raw / base[0]:5.1f}%)   gzip-6 {gz:>10,} ({100 * gz / base[1]:5.1f}%)'
                  f'   binds the same arrows: {same}')
            if out_dir:
                stem = os.path.splitext(os.path.basename(p))[0]
                open(os.path.join(out_dir, f'{stem}.{name.replace("+", "-")}.html'), 'wb').write(data)


if __name__ == '__main__':
    main(sys.argv[1:])
