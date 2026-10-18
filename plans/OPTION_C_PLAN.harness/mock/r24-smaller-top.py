# Round 17 item 24: the top-right toggle's triangle was 10 x 12.5 px in its 14 px button (2 px clear, item 13) and
# looked too big; it is drawn at the bottom buttons' size, 8 x 10 px, centred (3 px clear). Post-processes
# c-round17-buttons.js after r17-make-buttons.py and r17-keepplace.py; idempotent.
import io
p = 'c-round17-buttons.js'
s = io.open(p, encoding='utf-8', newline='').read()
if 'inset' in s:
    print('already patched'); raise SystemExit
old1 = "function kTriangle(x, y, w, h, up) {\n            var th = h - 4, tw = th * 1.25, cx = x + w / 2, top = y + 2, bot = y + h - 2;"
new1 = ("function kTriangle(x, y, w, h, up, inset) {\n"
        "            // inset: clearance from the button's top and bottom edges (2 px; the top-right toggle's 3 px, item 24)\n"
        "            var i = inset || 2;\n"
        "            var th = h - 2 * i, tw = th * 1.25, cx = x + w / 2, top = y + i, bot = y + h - i;")
nl = '\r\n' if '\r\n' in s else '\n'
old1 = old1.replace('\n', nl); new1 = new1.replace('\n', nl)
assert s.count(old1) == 1, s.count(old1)
s = s.replace(old1, new1)
old2 = "ga.appendChild(kTriangle(ax, ay, topSize, topSize, true));"
assert s.count(old2) == 1, s.count(old2)
s = s.replace(old2, "ga.appendChild(kTriangle(ax, ay, topSize, topSize, true, 3));")
io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
