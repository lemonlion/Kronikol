# ink middle of each control's text, from the bar screenshot and the boxes r31-align.js measured
import json, sys
from PIL import Image
png, js = sys.argv[1], sys.argv[2]
im = Image.open(png).convert('L'); W, H = im.size
items = json.load(open(js))
px = im.load()
for it in items:
    x0, x1 = it['x0'] + 5, it['x1'] - 5
    top, bot = int(it['top'] + 3), int(it['top'] + it['h'] - 3)
    if it['tag'] == 'SPAN': top, bot = max(0, int(it['top']) - 2), min(H, int(it['top'] + it['h']) + 2)
    if it['tag'] == 'SELECT': x1 = it['x1'] - 24   # leave the arrow out
    rows = []
    for y in range(max(0, top), min(H, bot)):
        dark = sum(1 for x in range(max(0, x0), min(W, x1)) if px[x, y] < 140)
        if dark: rows.append(y)
    if rows: print('%-8s %-14s ink %d-%d mid %.1f' % (it['tag'], it['text'][:12], rows[0], rows[-1], (rows[0] + rows[-1]) / 2))
