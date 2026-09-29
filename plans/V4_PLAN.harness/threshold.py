"""R2 (#85): which payloads are worth compressing. Prints sizes only, never content.

usage: python threshold.py <TestRunReport.json>...

For every `diagrams` entry and every `content` string it compares the bytes the plain string takes in the file (as
the relaxed encoder writes it, with its quotes) against the bytes a wrapper takes, {"$h": "b:xxxxxxxx", "$n": N,
"$z": "<base64 of gzip>"}, indented as the writer indents an interaction's `content` (four lines at depth 8 or so,
estimated at 90 bytes of keys, indentation and punctuation), at gzip levels 6 (.NET's Optimal) and 9. It then
prints the total the file would save for each size threshold (a payload is compressed when its text has at least
that many characters), so the threshold can be read off where the saving flattens.
"""
import base64
import gzip
import json
import sys

WRAPPER_OVERHEAD = 90
THRESHOLDS = [0, 64, 128, 192, 256, 384, 512, 768, 1024, 2048]


def relaxed_len(s):
    # The relaxed encoder: quote and backslash take two bytes, control characters six, the rest their UTF-8.
    n = 2
    for ch in s:
        if ch in '"\\':
            n += 2
        elif ord(ch) < 0x20:
            n += 2 if ch in '\n\r\t\b\f' else 6
        elif ord(ch) > 0xFFFF:
            n += 12
        else:
            n += len(ch.encode('utf-8'))
    return n


def payloads(o, out):
    if isinstance(o, dict):
        for k, v in o.items():
            if k == 'diagrams' and isinstance(v, list):
                out.extend(('diagram', x) for x in v if isinstance(x, str))
            elif k == 'content' and isinstance(v, str):
                out.append(('content', v))
            else:
                payloads(v, out)
    elif isinstance(o, list):
        for x in o:
            payloads(x, out)


for path in sys.argv[1:]:
    data = json.load(open(path, encoding='utf-8'))
    found = []
    payloads(data, found)
    rows = []
    for kind, s in found:
        raw = s.encode('utf-8')
        plain = relaxed_len(s)
        z6 = len(base64.b64encode(gzip.compress(raw, 6))) + WRAPPER_OVERHEAD
        z9 = len(base64.b64encode(gzip.compress(raw, 9))) + WRAPPER_OVERHEAD
        rows.append((kind, len(s), plain, z6, z9))
    print(path)
    print(f'  payloads {len(rows):,}: diagrams {sum(1 for r in rows if r[0] == "diagram"):,}, '
          f'contents {sum(1 for r in rows if r[0] == "content"):,}; plain bytes {sum(r[2] for r in rows):,}')
    for t in THRESHOLDS:
        chosen = [r for r in rows if r[1] >= t]
        saved6 = sum(r[2] - r[3] for r in chosen)
        saved9 = sum(r[2] - r[4] for r in chosen)
        losers = sum(1 for r in chosen if r[3] >= r[2])
        print(f'  threshold {t:>5}: {len(chosen):>6,} compressed, saves {saved6:>12,} at level 6, '
              f'{saved9:>12,} at level 9; {losers:,} of them larger compressed')
