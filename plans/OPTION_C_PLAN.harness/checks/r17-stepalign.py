# Round 17 item 18: reads an r17-stepalign.js screenshot + json and prints, in paint, the left and right ink of the
# Steps summary's marker and of its 'S', and per step row the marker's ink and the status circle's left ink.
# usage: python r17-stepalign.py <prefix> [<prefix> ...]
import json
import sys
from PIL import Image


def cols(im, x0, x1, y0, y1, thr):
    """Columns in [x0, x1) whose strongest pixel in rows [y0, y1) differs from the ground by more than thr."""
    px = im.load()
    W, H = im.size
    ground = px[8, (y0 + y1) // 2]
    out = []
    for x in range(max(0, x0), min(W, x1)):
        m = 0
        for y in range(max(0, y0), min(H, y1)):
            p = px[x, y]
            d = max(abs(p[0] - ground[0]), abs(p[1] - ground[1]), abs(p[2] - ground[2]))
            m = max(m, d)
        out.append((x, m))
    return out


def clusters(cs, thr):
    res, cur = [], None
    for x, m in cs:
        if m > thr:
            if cur is None:
                cur = [x, x, m]
            else:
                cur[1] = x
                cur[2] = max(cur[2], m)
        elif cur is not None:
            res.append(tuple(cur))
            cur = None
    if cur is not None:
        res.append(tuple(cur))
    return res


def edge(cs, x, thr):
    """Sub-pixel-ish ink start: first column over thr, plus how strong the column before it is (0..1)."""
    return x


for pre in sys.argv[1:]:
    im = Image.open(pre + '.png').convert('RGB')
    info = json.load(open(pre + '.json'))
    s = info['summary']
    y0, y1 = int(s['top']) + 2, int(s['top'] + s['h']) - 2
    cl = clusters(cols(im, 4, 120, y0, y1, 0), 60)
    S = info['S']
    print('== ' + pre)
    print('summary ink clusters (x0..x1, strength):', cl[:4], " 'S' box x=%.2f" % S['x'])
    for r in info['rows']:
        ry0, ry1 = int(r['top']) + 1, int(r['top'] + r['h']) - 1
        c = r['circle']
        cs = cols(im, 4, int(c['x']) + 20, ry0, ry1, 0)
        marker = [k for k in clusters(cs, 60) if k[1] < c['x'] - 1]
        # the circle's disc is pale: any change from white counts
        disc = [k for k in clusters(cs, 6) if k[0] >= c['x'] - 3]
        print('  %-40s marker %s  circle box x=%.2f ink from %s' % (r['text'][:40], marker[-1:] if marker else '-', c['x'], disc[0][:2] if disc else '-'))
