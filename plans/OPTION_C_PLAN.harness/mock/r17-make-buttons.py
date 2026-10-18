# Round 17 items 13-15: writes c-round17-buttons.js, the page's createNoteButtons (as round 16 left it, extracted to
# r17-createNoteButtons.js) with the top-right cluster packed, the triangles drawn and the truncated note's pair.
import io

s = io.open('r17-createNoteButtons.js', encoding='utf-8').read()


def rep(old, new, cnt=1):
    global s
    assert s.count(old) == cnt, (s.count(old), old[:80])
    s = s.replace(old, new)


rep("""        var buttons = [];
        var formatBtn = null;
        var widthBtn = null;
""", """        var buttons = [];
        var formatBtn = null;
        var widthBtn = null;
        // Round 17 (owner): the top-right buttons sit side by side from the note's right edge, with no gap where a
        // hidden button (an ineligible format or width button) would be (item 14); each is created at the first
        // place and moved by a transform, which also carries a tall note's scroll offset. The triangles are drawn,
        // not set as text, exactly 2px clear of their button's top and bottom edges in every browser (item 13).
        var topRight = [];
        var upTop = null;
        function kTriangle(x, y, w, h, up) {
            var th = h - 4, tw = th * 1.25, cx = x + w / 2, top = y + 2, bot = y + h - 2;
            var pts = up ? [cx - tw / 2, bot, cx + tw / 2, bot, cx, top] : [cx - tw / 2, top, cx + tw / 2, top, cx, bot];
            var p = document.createElementNS(SVGNS, 'polygon');
            p.setAttribute('points', pts.map(function(v) { return +v.toFixed(2); }).join(' '));
            p.setAttribute('fill', '#666');
            p.style.pointerEvents = 'none';
            return p;
        }
        function packTopRight(dy) {
            var k = 0;
            topRight.forEach(function(b) {
                if (dy !== undefined) b._kdy = dy;
                if (b.style.display === 'none') return;
                b.setAttribute('transform', 'translate(' + (-k * (topSize + pad)) + ',' + (b._kdy || 0) + ')');
                k++;
            });
        }
""")
rep("            var gx = bbox.x + bbox.width - topSize * slot - pad * slot;",
    "            var gx = bbox.x + bbox.width - topSize - pad;")
rep("                var ax = bbox.x + bbox.width - topSize * 2 - pad * 2;",
    "                var ax = bbox.x + bbox.width - topSize - pad;")
rep("""                var symA = document.createElementNS(SVGNS, 'text');
                symA.setAttribute('x', ax + topSize / 2); symA.setAttribute('y', ay + topSize - 3);
                symA.setAttribute('text-anchor', 'middle'); symA.setAttribute('font-size', '12');
                symA.setAttribute('font-family', 'sans-serif'); symA.setAttribute('fill', '#666');
                symA.style.pointerEvents = 'none';
                symA.textContent = '\\u25B2'; // ▲
                ga.appendChild(symA);""", """                ga.setAttribute('data-note-btn', 'truncate');
                ga.appendChild(kTriangle(ax, ay, topSize, topSize, true));""")
rep("""                buttons.push(ga);
            }
""", """                buttons.push(ga);
                upTop = ga;
            }
""")
rep("""            buttons.push(gc);
""", """            buttons.push(gc);
            topRight.push(gc);
            if (upTop) topRight.push(upTop);
""")
rep("                var fx = bbox.x + bbox.width - topSize * slot - pad * slot;",
    "                var fx = bbox.x + bbox.width - topSize - pad;")
rep("""                formatBtn = gf;
                buttons.push(gf);""", """                formatBtn = gf;
                buttons.push(gf);
                topRight.push(gf);""")
rep("""                buttons.push(widthBtn);
            }
        }""", """                buttons.push(widthBtn);
                topRight.push(widthBtn);
            }
        }""")
rep("""            buttons.push(gp);
        }""", """            buttons.push(gp);
            topRight.push(gp);
        }""")
rep("""        if ((state === 'collapsed' && !kronWhole) || state === 'truncated') {
            var expandW = size * 3;
            var expandH = size;
            var ex = bbox.x + (bbox.width - expandW) / 2;""", """        if ((state === 'collapsed' && !kronWhole) || state === 'truncated') {
            // Round 17 (owner, item 15): a truncated note can go either way, so its bottom holds ▲ (collapse) and
            // ▼ (expand) side by side, each two thirds of the single button's width.
            var pair = state === 'truncated';
            var expandW = pair ? size * 2 : size * 3;
            var expandH = size;
            var ex = bbox.x + (bbox.width - (pair ? expandW * 2 + pad : expandW)) / 2 + (pair ? expandW + pad : 0);""")
rep("""            var symE = document.createElementNS(SVGNS, 'text');
            symE.setAttribute('x', ex + expandW / 2); symE.setAttribute('y', ey + expandH - 2.5);
            symE.setAttribute('text-anchor', 'middle'); symE.setAttribute('font-size', '10');
            symE.setAttribute('font-family', 'sans-serif'); symE.setAttribute('fill', '#666');
            symE.style.pointerEvents = 'none';
            symE.textContent = '\\u25BC'; // ▼
            ge.appendChild(symE);""", """            ge.setAttribute('data-note-btn', 'expand-bottom');
            ge.appendChild(kTriangle(ex, ey, expandW, expandH, false));""")
rep("""            buttons.push(ge);
        }""", """            buttons.push(ge);
            if (pair) {
                var cxp = ex - expandW - pad;
                var gcp = document.createElementNS(SVGNS, 'g');
                gcp.setAttribute('class', 'note-toggle-icon');
                gcp.setAttribute('data-note-btn', 'collapse-bottom');
                gcp.style.cursor = 'pointer';
                gcp.style.opacity = '0';
                var bgCP = document.createElementNS(SVGNS, 'rect');
                bgCP.setAttribute('x', cxp); bgCP.setAttribute('y', ey);
                bgCP.setAttribute('width', expandW); bgCP.setAttribute('height', expandH);
                bgCP.setAttribute('rx', '2'); bgCP.setAttribute('fill', '#ffffff');
                bgCP.setAttribute('stroke', '#999'); bgCP.setAttribute('stroke-width', '0.5');
                gcp.appendChild(bgCP);
                gcp.appendChild(kTriangle(cxp, ey, expandW, expandH, true));
                bgCP.addEventListener('click', function(ev) { ev.stopPropagation(); onContract(); });
                bgCP.addEventListener('dblclick', function(ev) { ev.stopPropagation(); ev.preventDefault(); });
                buttons.push(gcp);
            }
        }""")
rep("""            var symBC = document.createElementNS(SVGNS, 'text');
            symBC.setAttribute('x', cx + contractW / 2); symBC.setAttribute('y', cy + contractH - 2.5);
            symBC.setAttribute('text-anchor', 'middle'); symBC.setAttribute('font-size', '10');
            symBC.setAttribute('font-family', 'sans-serif'); symBC.setAttribute('fill', '#666');
            symBC.style.pointerEvents = 'none';
            symBC.textContent = '\\u25B2'; // ▲
            gbc.appendChild(symBC);""", """            gbc.setAttribute('data-note-btn', 'truncate-bottom');
            gbc.appendChild(kTriangle(cx, cy, contractW, contractH, true));""")
a = s.index("            // For tall notes, reposition top-right buttons to the visible")
b = s.index("            buttons.forEach(function(b) { if (b.style.display !== 'none') b.style.opacity = '1'; });")
s = s[:a] + """            // For tall notes, the top-right cluster follows the visible part of the note, so it is not scrolled off.
            var dy;
            if (bbox.height > 500 && svg.getScreenCTM) {
                try {
                    var ctm = svg.getScreenCTM();
                    if (ctm) {
                        var noteScreenTop = bbox.y * ctm.d + ctm.f;
                        var visTop = Math.max(0, noteScreenTop);
                        var visSvgY = (visTop - ctm.f) / ctm.d;
                        var btnY = Math.max(bbox.y + pad, Math.min(visSvgY + pad, bbox.y + bbox.height - topSize * 2 - pad));
                        dy = btnY - (bbox.y + pad);
                    }
                } catch(e) {}
            }
            packTopRight(dy);
""" + s[b:]
rep("""        // Hover detection via the note's own SVG elements (paths = background, texts = content).""",
    """        packTopRight();

        // Hover detection via the note's own SVG elements (paths = background, texts = content).""")
io.open('c-round17-buttons.js', 'w', encoding='utf-8').write(s)
print('ok', len(s.splitlines()))
