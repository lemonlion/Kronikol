"""Byte anatomy of a Kronikol HTML report. Prints sizes and short labels only, never content."""
import re
import sys
from collections import Counter, defaultdict

path = sys.argv[1]
raw = open(path, 'rb').read()
total = len(raw)
text = raw.decode('utf-8', errors='replace')
print(f"file bytes: {total:,}  chars: {len(text):,}  non-ascii bytes: {sum(1 for b in raw if b >= 0x80):,}")

def b(s):
    return len(s.encode('utf-8'))

# Raw-text elements first: script, style, template, textarea
raw_re = re.compile(r'<(script|style|textarea|title)(\s[^>]*)?>(.*?)</\1\s*>', re.S | re.I)
spans = []
rows = []
for m in raw_re.finditer(text):
    tag = m.group(1).lower()
    attrs = m.group(2) or ''
    body = m.group(3)
    idm = re.search(r'\bid\s*=\s*"([^"]*)"', attrs)
    tym = re.search(r'\btype\s*=\s*"([^"]*)"', attrs)
    label = f"{tag}#{idm.group(1) if idm else '-'} type={tym.group(1) if tym else '-'}"
    rows.append((b(m.group(0)), label, m.start()))
    spans.append((m.start(), m.end()))

by_label = defaultdict(lambda: [0, 0])
for size, label, _ in rows:
    by_label[label][0] += size
    by_label[label][1] += 1
print("\n== raw-text elements (bytes, count) ==")
raw_total = 0
for label, (size, n) in sorted(by_label.items(), key=lambda kv: -kv[1][0])[:40]:
    raw_total += size
    print(f"{size:>12,}  {n:>6}  {label}")
print(f"{sum(s for s,_,_ in rows):>12,}  total in raw-text elements")

# The rest: markup outside raw-text elements
rest_parts = []
pos = 0
for s, e in spans:
    rest_parts.append(text[pos:s])
    pos = e
rest_parts.append(text[pos:])
rest = ''.join(rest_parts)
print(f"\n== markup outside raw-text elements: {b(rest):,} bytes ==")

tag_re = re.compile(r'<(/?)([A-Za-z][A-Za-z0-9:-]*)([^>]*)>', re.S)
attr_re = re.compile(r'([^\s=/>]+)(?:\s*=\s*("([^"]*)"|\'([^\']*)\'|([^\s>]+)))?', re.S)
tag_bytes = Counter()
tag_count = Counter()
attr_bytes = Counter()
attr_count = Counter()
text_bytes = 0
last = 0
for m in tag_re.finditer(rest):
    text_bytes += b(rest[last:m.start()])
    last = m.end()
    name = m.group(2).lower()
    whole = b(m.group(0))
    tag_bytes[name] += whole
    if not m.group(1):
        tag_count[name] += 1
        for a in attr_re.finditer(m.group(3)):
            an = a.group(1).lower()
            val = a.group(3) if a.group(3) is not None else (a.group(4) if a.group(4) is not None else (a.group(5) or ''))
            attr_bytes[an] += b(a.group(0)) + 1
            attr_count[an] += 1
text_bytes += b(rest[last:])
print(f"text between tags: {text_bytes:,} bytes")
print("\n-- tags (bytes incl. attributes, start count) --")
for name, size in tag_bytes.most_common(25):
    print(f"{size:>12,}  {tag_count[name]:>8,}  <{name}>")
print("\n-- attributes (bytes incl. name, count) --")
for name, size in attr_bytes.most_common(30):
    print(f"{size:>12,}  {attr_count[name]:>8,}  {name}")

# base64-looking runs anywhere
b64 = re.compile(r'[A-Za-z0-9+/]{400,}={0,2}')
runs = [len(m.group(0)) for m in b64.finditer(text)]
print(f"\nbase64-like runs >= 400 chars: {len(runs):,}, {sum(runs):,} bytes, largest {max(runs) if runs else 0:,}")
dataurl = re.compile(r'data:([a-z0-9.+/-]+)(;[a-z0-9=-]+)*;base64,')
du = Counter(m.group(1) for m in dataurl.finditer(text))
print("data: URIs by type:", dict(du))
gen = re.findall(r'Kronikol[ /]v?(\d+\.\d+\.\d+)', text[:200000])
print("version strings near the top:", Counter(gen).most_common(3))
