"""Split a Kronikol HTML report into its content classes with each gzip+base64 layer removed.

Writes, into the output directory:
  markup.html        the page with every blob's value emptied (scripts, CSS, markup, plain attributes)
  puml.txt           every diagram source, raw, in page order, separated by a NUL-free marker line
  steps.txt          step-data payloads, raw
  iflow.txt          the internal-flow segment payload, raw
  flame.txt          data-flame-z payloads, raw
  search.bin         the deep-search index, raw binary
  normalized.html    one page with the raw content written back in place of each blob (JSON- or
                     attribute-escaped as the page would carry it); search index kept base64
Prints per-class sizes: as stored in the page, gzip inside, raw.
"""
import base64
import gzip
import html
import json
import os
import re
import sys

src, outdir = sys.argv[1], sys.argv[2]
text = open(src, 'rb').read().decode('utf-8')
total = len(text.encode('utf-8'))
stats = {}

def add(cls, stored, gz, raw):
    s = stats.setdefault(cls, [0, 0, 0, 0])
    s[0] += stored; s[1] += gz; s[2] += raw; s[3] += 1

def ungz(b64):
    data = base64.b64decode(b64)
    assert data[:2] == b'\x1f\x8b', 'not gzip'
    return data, gzip.decompress(data)

def json_escape_like_stj(s):
    # System.Text.Json default encoder: escapes non-ASCII, <, >, &, ', +, and quotes/control chars
    out = json.dumps(s, ensure_ascii=True)
    for ch, rep in (('<', '\\u003C'), ('>', '\\u003E'), ('&', '\\u0026'), ("'", '\\u0027'), ('+', '\\u002B')):
        out = out.replace(ch, rep)
    return out

parts = {'puml': [], 'steps': [], 'iflow': [], 'flame': []}
search_raw = b''

def block(sid):
    m = re.search(r'<script id="' + sid + r'" type="application/json">(.*?)</script>', text, re.S)
    return m

norm = text
markup = text

# puml-data and step-data: {id: base64(gzip(text))}
for sid, cls in (('puml-data', 'puml'), ('step-data', 'steps')):
    m = block(sid)
    if not m:
        continue
    d = json.loads(m.group(1))
    newd_parts = []
    for k, v in d.items():
        gz, raw = ungz(v)
        add(cls, len(json_escape_like_stj(v).encode()), len(gz), len(raw))
        rawtext = raw.decode('utf-8')
        parts[cls].append(rawtext)
        newd_parts.append(json_escape_like_stj(k) + ':' + json_escape_like_stj(rawtext))
    newbody = '{' + ','.join(newd_parts) + '}'
    norm = norm.replace(m.group(0), m.group(0).replace(m.group(1), newbody), 1)
    markup = markup.replace(m.group(0), m.group(0).replace(m.group(1), '{}'), 1)

# iflow-segments: {"has": [...], "z": base64(gzip(json))}
m = block('iflow-segments')
if m:
    d = json.loads(m.group(1))
    gz, raw = ungz(d['z'])
    add('iflow', len(json_escape_like_stj(d['z']).encode()), len(gz), len(raw))
    parts['iflow'].append(raw.decode('utf-8'))
    has_len = len(m.group(1).encode()) - len(json_escape_like_stj(d['z']).encode())
    d2 = dict(d); d2['z'] = raw.decode('utf-8')
    norm = norm.replace(m.group(0), m.group(0).replace(m.group(1), json.dumps(d2)), 1)
    d3 = dict(d); d3['z'] = ''
    markup = markup.replace(m.group(0), m.group(0).replace(m.group(1), json.dumps(d3)), 1)

# kron-search-index: "base64(gzip(binary))"
m = block('kron-search-index')
if m:
    v = json.loads(m.group(1))
    gz, raw = ungz(v)
    add('search', len(m.group(1).encode()), len(gz), len(raw))
    search_raw = raw
    markup = markup.replace(m.group(0), m.group(0).replace(m.group(1), '""'), 1)

# data-flame-z="base64(gzip(json))" (and any data-*-z attribute)
def attr_sub(mm, keep_raw):
    name, v = mm.group(1), mm.group(2)
    gz, raw = ungz(html.unescape(v))
    rawtext = raw.decode('utf-8')
    if keep_raw:
        add('flame' if name == 'data-flame-z' else name, len(v.encode()), len(gz), len(raw))
        parts['flame'].append(rawtext)
        return f'{name}="{html.escape(rawtext, quote=True)}"'
    return f'{name}=""'
attr_re = re.compile(r'(data-[a-z-]+-z)="([A-Za-z0-9+/=&#;]+)"')
norm = attr_re.sub(lambda mm: attr_sub(mm, True), norm)
markup = attr_re.sub(lambda mm: attr_sub(mm, False), markup)

os.makedirs(outdir, exist_ok=True)
def w(name, data):
    with open(os.path.join(outdir, name), 'wb') as f:
        f.write(data if isinstance(data, bytes) else data.encode('utf-8'))
SEP = '\n\x1e\n'
w('markup.html', markup)
for cls, lst in parts.items():
    w(f'{cls}.txt', SEP.join(lst))
w('search.bin', search_raw)
w('normalized.html', norm)

print(f"page: {total:,} bytes")
print(f"{'class':<10}{'items':>7}{'stored':>14}{'gzip inside':>14}{'raw':>14}")
stored_sum = 0
for cls, (stored, gz, raw, n) in stats.items():
    stored_sum += stored
    print(f"{cls:<10}{n:>7,}{stored:>14,}{gz:>14,}{raw:>14,}")
mk = len(markup.encode())
print(f"{'markup':<10}{'':>7}{mk:>14,}{'':>14}{mk:>14,}   (page minus every blob value)")
print(f"blob values stored: {stored_sum:,}  markup+blobs: {stored_sum + mk:,} (page {total:,})")
print(f"normalized.html: {len(norm.encode()):,} bytes")
