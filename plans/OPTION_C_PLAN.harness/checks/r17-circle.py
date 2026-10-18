# Circle centre (intensity-weighted, sub-pixel) against the label's first capital (centre of its crisp rows), for the
# hidden toggles in each scenario-flags shot. Usage: python r17-circle.py <dir>...  (dirs from r17-variants.js)
import json, sys, os, collections
from PIL import Image
def lum(p): return 0.299 * p[0] + 0.587 * p[1] + 0.114 * p[2]
res = collections.defaultdict(dict)
for d in sys.argv[1:]:
    BAR = os.environ.get('BAR', 'scenario-flags')
    meta = json.load(open(os.path.join(d, 'meta.json')))['meta'][BAR]
    img = Image.open(os.path.join(d, BAR + '.png')).convert('RGB'); px = img.load(); W, H = img.size
    outs = []
    for e in meta:
        if e['label'] not in ('Headers', 'Assertions'): continue
        x0, y0 = int(round(e['x'])), max(0, int(round(e['y'])))
        x1, y1 = min(W, int(round(e['x'] + e['w']))), min(H, int(round(e['y'] + e['h'])))
        bg = collections.Counter(px[x, y] for y in range(y0, y1) for x in range(x0, x1)).most_common(1)[0][0]; bl = lum(bg)
        # circle: columns 4..20 of the button, weighted by darkness
        tot = wy = 0
        for y in range(y0, y1):
            for x in range(x0 + 4, x0 + 21):
                w = max(0, bl - lum(px[x, y])); tot += w; wy += w * (y + 0.5)
        cc = wy / tot if tot else float('nan')
        # first capital after the circle: crisp rows (darkness > 60% of max)
        cols = [x for x in range(x0 + 22, x1) if any(bl - lum(px[x, y]) > 70 for y in range(y0, y1))]
        g = [cols[0]]
        for c in cols[1:]:
            if c == g[-1] + 1: g.append(c)
            else: break
        rows = [y for y in range(y0, y1) if any(bl - lum(px[x, y]) > 70 for x in g)]
        gc = (rows[0] + rows[-1] + 1) / 2
        outs.append(cc - gc)
    br, rest = os.path.normpath(d).split(os.sep)[-2:]
    var, fr = rest.rsplit('-', 1)
    res[(br, var)][float(fr)] = sum(outs) / len(outs)
for k in sorted(res):
    vals = [res[k][f] for f in sorted(res[k])]
    print('%-12s %-8s %s  spread %.2f' % (k[0][:12], k[1], ' '.join('%+.2f' % v for v in vals), max(vals) - min(vals)))
