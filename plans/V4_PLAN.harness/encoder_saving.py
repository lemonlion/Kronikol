"""R1 (relaxed encoder) saving per report, computed from the escapes actually in the file, plus the
indentation share (Q1) and the internal-flow map's share when the file is mergeable. Sizes only."""
import json, re, sys

BS = chr(92)
ESC = re.compile(re.escape(BS.encode()) + rb'u([0-9A-Fa-f]{4})')

for p in sys.argv[1:]:
    raw = open(p, 'rb').read()
    save = 0
    for m in ESC.finditer(raw):
        cp = int(m.group(1), 16)
        if cp == 0x22:
            save += 4                      # " -> \"
        elif cp < 0x20 or 0xD800 <= cp <= 0xDFFF or cp in (0x2028, 0x2029):
            pass                           # still escaped by the relaxed encoder
        else:
            save += 6 - len(chr(cp).encode('utf-8'))
    d = json.loads(raw)
    indented = len(json.dumps(d, ensure_ascii=False, indent=2).encode('utf-8'))
    compact = len(json.dumps(d, ensure_ascii=False, separators=(',', ':')).encode('utf-8'))
    segs = d.get('internalFlowSegments')
    seg_bytes = len(json.dumps(segs, ensure_ascii=False).encode('utf-8')) if segs else 0
    print(f'{len(raw):>11,}  R1 saves {save:>10,} ({100 * save / len(raw):4.1f}%)  '
          f'indent share after R1 {100 * (indented - compact) / indented:4.1f}%  '
          f'mergeable={"mergeableFormatVersion" in d}  map={seg_bytes:,}  '
          f'v={str(d.get("kronikolVersion"))[:6]}  {p[-75:]}')
