# Round 17 item 18: reads r17-sections.js output and prints each heading's ink clusters (marker, first letter).
# usage: python r17-sections.py <prefix> [<prefix> ...]
import json
import sys
from PIL import Image


def clusters(im, thr):
    px = im.load()
    W, H = im.size
    ground = px[2, H // 2]
    res, cur = [], None
    for x in range(0, min(W, 140)):
        m = 0
        for y in range(2, H - 2):
            p = px[x, y]
            m = max(m, abs(p[0] - ground[0]), abs(p[1] - ground[1]), abs(p[2] - ground[2]))
        if m > thr:
            if cur is None:
                cur = [x, x]
            else:
                cur[1] = x
        elif cur is not None:
            res.append(tuple(cur))
            cur = None
    if cur is not None:
        res.append(tuple(cur))
    return res


for pre in sys.argv[1:]:
    info = json.load(open(pre + '.json'))
    print('== ' + pre)
    for r in info:
        im = Image.open(pre + '-' + r['name'] + '.png').convert('RGB')
        cl = clusters(im, 60)
        sx = r['sum']['x']
        cl = [(a + sx, b + sx) for a, b in cl]
        print('  %-20s marker ink %s first %r box x=%.2f ink %s  (summary pl %s, %s; card-relative)' % (r['name'], cl[0] if cl else '-', r['first']['ch'], r['first']['x'], cl[1] if len(cl) > 1 else '-', r['sum']['pl'], r['sum']['list']))
