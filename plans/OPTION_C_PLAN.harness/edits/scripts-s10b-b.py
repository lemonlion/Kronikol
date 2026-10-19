# S10b-2 (the page): the role pass (P24 to P30), the preview's c-diagram-look.js section 2 re-derived for the product.
# Once the page has drawn a sequence diagram, each element is found by its structure and given a role class, which the
# stylesheet colours from the theme's tokens. Not ported: the levers (the owner took every one but the badges), the
# number badges (P29, off since round 8), the single-stereotype note kinds (F3). The shim marks a sequence diagram's
# container from its source's pragma and records its note kinds. usage: python scripts-s10b-b.py
P = 'C:/Code/Kronikol-v5/src/Kronikol/Reports/plantuml-browser-render-script.js'
s = open(P, encoding='utf-8', newline='').read()


def sub(old, new):
    global s
    assert s.count(old) == 1, (old[:70], s.count(old))
    s = s.replace(old, new)


sub(r'''            var target = { el: id ? document.getElementById(id) : null, id: id || null, opts: opts || null };
''', r'''            var target = { el: id ? document.getElementById(id) : null, id: id || null, opts: opts || null };
            // a sequence diagram's container is the role pass's to tag, with the kinds of the notes it draws: the
            // page's sequence sources open with the teoz pragma (5.0.0, plans/OPTION_C_PLAN.md S10, P23)
            if (target.el && window._diagramRoles && window._diagramRoles.isSequence(lines)) {
                target.el.setAttribute('data-k-sequence', '1');
                target.el._noteKinds = window._diagramRoles.noteKinds(lines);
            }
''')

ROLES = r'''
// The role pass (5.0.0, plans/OPTION_C_PLAN.md S10, P24 to P30). Once the page has drawn a sequence diagram, each element
// is found by its STRUCTURE and given a role class, and the stylesheet colours the roles from the theme's tokens, so a
// PlantUML theme or a recoloured source changes nothing here and a report theme is just other token values. Lifelines
// are the long vertical lines; participants are the shapes at their ends; a step bar is a black shape spanning every
// lifeline (the shim draws it as a rectangle); a note is a filled path with its fold at a corner, as the report's note
// script finds it. Note kinds (an event) come from the drawn source, in order.
(function() {
    var NS = 'http://www.w3.org/2000/svg';
    function isSequence(lines) {
        for (var i = 0; i < lines.length && i < 6; i++) if (/^!pragma teoz/.test(lines[i])) return true;
        return false;
    }
    // the kind of each note, in drawing order, from the stereotype Kronikol gives it (eventNote, a name ending in Note)
    // or ''. A note widened by its width button carries kronNoteWide as well, which is no kind: read as one stereotype,
    // the widened note was skipped, the kinds no longer matched the notes drawn, and every event card in the diagram
    // lost its colour (F3)
    function noteKinds(lines) {
        var kinds = [];
        for (var i = 0; i < lines.length; i++) {
            var m = /^note((?:<<[A-Za-z]+>>)*)\s+(?:left|right|over)\b/.exec(lines[i]);
            if (!m) continue;
            var kind = '';
            (m[1].match(/<<[A-Za-z]+>>/g) || []).forEach(function(st) {
                var name = st.slice(2, -2);
                if (!kind && /Note$/.test(name)) kind = name;
            });
            kinds.push(kind);
        }
        return kinds;
    }
    function box(e) { try { return e.getBBox(); } catch (x) { return null; } }
    function tag(e) { return e.tagName.toLowerCase(); }
    // painted at all: not none, not transparent (structure, not colour)
    function filled(e) { var f = (e.getAttribute('fill') || '').toLowerCase(); return !!f && f !== 'none' && f !== 'transparent' && !/^#[0-9a-f]{6}00$/.test(f); }
    function addClass(e, c) { e.setAttribute('class', ((e.getAttribute('class') || '') + ' ' + c).trim()); }
    function num(e, a) { return parseFloat(e.getAttribute(a)) || 0; }
    function fixed(v) { return String(Math.round(v * 100) / 100); }
    // every x coordinate of an element, mapped by fx: a rect's two sides, a line's ends, a polygon's points, a path's
    // absolute coordinates (the engine writes no relative ones; a path with one is moved whole by a transform)
    function moveX(e, fx) {
        var t = tag(e);
        if (t === 'text' || t === 'image') e.setAttribute('x', fixed(fx(num(e, 'x'))));
        else if (t === 'rect') { var x = num(e, 'x'), r = fx(x + num(e, 'width')); x = fx(x); e.setAttribute('x', fixed(x)); e.setAttribute('width', fixed(r - x)); }
        else if (t === 'line') { e.setAttribute('x1', fixed(fx(num(e, 'x1')))); e.setAttribute('x2', fixed(fx(num(e, 'x2')))); }
        else if (t === 'circle' || t === 'ellipse') e.setAttribute('cx', fixed(fx(num(e, 'cx'))));
        else if (t === 'polygon' || t === 'polyline') {
            var n = (e.getAttribute('points') || '').trim().split(/[\s,]+/).map(Number), pts = [];
            for (var i = 0; i + 1 < n.length; i += 2) pts.push(fixed(fx(n[i])) + ',' + fixed(n[i + 1]));
            e.setAttribute('points', pts.join(' '));
        } else if (t === 'path') {
            var d = e.getAttribute('d') || '';
            if (/[mlhvcsqta]/.test(d)) { e.setAttribute('transform', 'translate(' + fixed(fx(0)) + ' 0)'); return; }
            var XS = { M: [0], L: [0], T: [0], H: [0], C: [0, 2, 4], S: [0, 2], Q: [0, 2], A: [5], V: [], Z: [] };
            var SIZE = { M: 2, L: 2, T: 2, H: 1, V: 1, C: 6, S: 4, Q: 4, A: 7, Z: 1 };
            e.setAttribute('d', d.replace(/([MLHVCSQTAZ])([^MLHVCSQTAZ]*)/g, function(all, c, rest) {
                var v = rest.match(/-?(?:\d+\.?\d*|\.\d+)(?:e[-+]?\d+)?/gi);
                if (!v) return c + rest;
                return c + v.map(function(n, k) { return XS[c].indexOf(k % SIZE[c]) >= 0 ? fixed(fx(+n)) : n; }).join(' ') + ' ';
            }).trim());
        }
    }

    // Three phases, so the browser lays the drawing out once: every measurement first (READS), then the decisions,
    // then every change (WRITES). Measuring and changing element by element laid the whole diagram out again before
    // each measurement: 117 ms on a 2,400-element diagram, measured in the preview.
    function tagRoles(svg) {
        if (!svg || svg.getAttribute('data-k-roles')) return true;
        var host = svg.closest('[data-k-sequence]');
        if (!host) return true;
        var main = null;
        for (var i = 0; i < svg.children.length; i++) if (tag(svg.children[i]) === 'g') { main = svg.children[i]; break; }
        if (!main) return true;
        if (!svg.getBoundingClientRect().width) return false; // not laid out yet: tagged when it comes into view
        var started = performance.now();

        /* READS */
        // every drawn element, at any depth, leaving out the containers and what the report's note script adds
        var kids = Array.prototype.slice.call(main.querySelectorAll('*')).filter(function(e) {
            var t = tag(e);
            if (t === 'g' || t === 'a' || t === 'title' || t === 'tspan' || t === 'defs') return false;
            return !(e.closest('.note-toggle-icon') || (e.classList && e.classList.contains('note-hover-rect')));
        });
        var vb = svg.viewBox && svg.viewBox.baseVal;
        var height = vb && vb.height ? vb.height : (box(svg) || { height: 0 }).height;
        // lifelines from their attributes: the x positions whose vertical lines cover more than a third of the diagram
        var vertical = kids.filter(function(e) { return tag(e) === 'line' && Math.abs(num(e, 'x1') - num(e, 'x2')) < 0.5 && Math.abs(num(e, 'y2') - num(e, 'y1')) > 4; });
        var cover = {};
        vertical.forEach(function(e) { var x = Math.round(num(e, 'x1')); cover[x] = (cover[x] || 0) + Math.abs(num(e, 'y2') - num(e, 'y1')); });
        var lifeXs = Object.keys(cover).filter(function(x) { return cover[x] > height / 3; }).map(Number);
        var lifelines = vertical.filter(function(e) { return lifeXs.indexOf(Math.round(num(e, 'x1'))) >= 0; });
        if (!lifelines.length) { svg.setAttribute('data-k-roles', 'none'); return true; }
        var top = Infinity, bottom = -Infinity;
        lifelines.forEach(function(e) { top = Math.min(top, num(e, 'y1'), num(e, 'y2')); bottom = Math.max(bottom, num(e, 'y1'), num(e, 'y2')); });
        var minX = Math.min.apply(null, lifeXs), maxX = Math.max.apply(null, lifeXs);
        var shapes = [];
        kids.forEach(function(e) {
            if (tag(e) === 'text') return;
            var b = box(e); if (b) shapes.push({ e: e, t: tag(e), b: b, filled: filled(e) });
        });
        var texts = kids.filter(function(e) { return tag(e) === 'text'; });

        /* DECISIONS (no DOM access beyond attributes) */
        var counts = { lifelines: 0, participants: 0, bands: 0, cards: 0 };
        var participants = shapes.filter(function(o) {
            var b = o.b;
            if (!(b.y + b.height <= top + 2 || b.y >= bottom - 2)) return false;
            return lifeXs.some(function(x) { return x >= b.x - 2 && x <= b.x + b.width + 2; });
        });
        // everything filled that spans the lifelines
        var spans = lifeXs.length < 2 ? [] : shapes.filter(function(o) {
            var b = o.b;
            return (o.t === 'rect' || o.t === 'path' || o.t === 'polygon') && o.filled && b.x <= minX + 1 && b.x + b.width >= maxX - 1 && b.y > top - 2 && b.y + b.height < bottom + 2;
        });
        function within(list, x, y) { return list.some(function(o) { return o.b.x <= x && x <= o.b.x + o.b.width && o.b.y <= y && y <= o.b.y + o.b.height; }); }
        // the step bars: the shim draws every step as a rectangle in the source's black, of any height (a step
        // carrying a data table or a doc string is tall). An assertion (a green or red hexagon), the Test bar and a
        // render error span the lifelines too, and keep their own look (the preview's round 12: the assertions had
        // taken the steps' colour)
        var stepBands = spans.filter(function(o) { return o.t !== 'polygon' && /^(#000000|#000|black)$/i.test((o.e.getAttribute('fill') || '').trim()); });
        var bandTexts = texts.filter(function(t) { return within(stepBands, num(t, 'x'), num(t, 'y') - 3); });
        // the rules of a table drawn in a step bar (the report's style draws them white, for the dark bar)
        var boxIn = function(o, s) { return o.b.x >= s.b.x - 1 && o.b.x + o.b.width <= s.b.x + s.b.width + 1 && o.b.y >= s.b.y - 1 && o.b.y + o.b.height <= s.b.y + s.b.height + 1; };
        var stepRules = !stepBands.length ? [] : shapes.filter(function(o) {
            return stepBands.indexOf(o) < 0 && !o.filled && /^(line|path|polyline|rect)$/.test(o.t) && stepBands.some(function(s) { return boxIn(o, s); });
        });
        // a table drawn in a step bar: the box its rules enclose, with its header row down to the second horizontal rule
        var stepTables = [];
        if (stepRules.length) stepBands.forEach(function(s) {
            var x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity, ys = [];
            stepRules.forEach(function(o) {
                if (!boxIn(o, s)) return;
                x0 = Math.min(x0, o.b.x); y0 = Math.min(y0, o.b.y); x1 = Math.max(x1, o.b.x + o.b.width); y1 = Math.max(y1, o.b.y + o.b.height);
                if (o.b.height > 0.6 || o.b.width < 4) return;
                var y = Math.round(o.b.y * 10) / 10;
                if (ys.indexOf(y) < 0) ys.push(y);
            });
            ys.sort(function(a, c) { return a - c; });
            if (x1 > x0 && y1 > y0) stepTables.push({ s: s, x: x0, y: y0, w: x1 - x0, h: y1 - y0, headH: ys.length > 2 ? ys[1] - y0 : 0 });
        });
        // Every strip runs the drawing's full width. The engine draws a strip across the participants, or as wide as
        // its own text where that is wider, centred on the participants, so the strips stopped short of the notes
        // beside the outer participants. The step bars and the hexagons (an assertion, the Test bar, a Row marker) are
        // stretched to the drawing's left and right edges, and what is drawn in them is centred again, as the engine
        // centres it. A frame around a strip (the Setup partition, a loop) or anything else drawn in its rows stops it
        // short; a strip never narrows.
        var stretches = [];
        if (spans.length) {
            // every drawn element once, as a box: a shape's measured box, a text's from its attributes (x, the
            // baseline, the engine's textLength: nothing is measured). The lifelines cross every strip and are left out.
            var items = [], left = Infinity, right = -Infinity, k, it, o, t;
            var lifeSet = new Set(lifelines);
            for (k = 0; k < shapes.length; k++) {
                o = shapes[k];
                if (lifeSet.has(o.e)) continue;
                items.push({ o: o, e: o.e, x: o.b.x, r: o.b.x + o.b.width, y: 0, top: o.b.y, bottom: o.b.y + o.b.height, text: false });
                if (o.b.width || o.b.height) { if (o.b.x < left) left = o.b.x; if (o.b.x + o.b.width > right) right = o.b.x + o.b.width; }
            }
            for (k = 0; k < texts.length; k++) {
                t = texts[k];
                var tx = num(t, 'x'), ty = num(t, 'y'), tr = tx + num(t, 'textLength');
                items.push({ o: null, e: t, x: tx, r: tr, y: ty, top: ty - 16, bottom: ty + 4, text: true });
                if (tx < left) left = tx; if (tr > right) right = tr;
            }
            var GAP = 6;
            for (var si = 0; si < spans.length; si++) {
                var sp = spans[si];
                if (sp.t !== 'polygon' && stepBands.indexOf(sp) < 0) continue;
                var b = sp.b, x0 = b.x, x1 = b.x + b.width, y0 = b.y, y1 = b.y + b.height, L = left, R = right, inner = [];
                for (k = 0; k < items.length; k++) {
                    it = items[k];
                    if (it.o === sp) continue;
                    // drawn in the strip: moves with it (a text by where its baseline starts, a shape by its whole box)
                    if (it.text ? it.x >= x0 - 1 && it.x <= x1 + 1 && it.y - 3 >= y0 - 1 && it.y - 3 <= y1 + 1
                                : it.x >= x0 - 1 && it.r <= x1 + 1 && it.top >= y0 - 1 && it.bottom <= y1 + 1) { inner.push(it.e); continue; }
                    // anything else in the strip's rows stops it: a frame around it at its walls, the rest at their near side
                    if (it.bottom <= y0 + 1 || it.top >= y1 - 1) continue;
                    if (!it.text && it.x <= x0 + 1 && it.r >= x1 - 1) { L = Math.max(L, it.x + GAP); R = Math.min(R, it.r - GAP); }
                    else if (it.r <= x0 + 1) L = Math.max(L, it.r + GAP);
                    else if (it.x >= x1 - 1) R = Math.min(R, it.x - GAP);
                }
                L = Math.min(L, x0); R = Math.max(R, x1);
                if (x0 - L < 0.5 && R - x1 < 0.5) continue;
                stretches.push({ s: sp, x0: x0, x1: x1, L: L, R: R, dx: (L + R) / 2 - (x0 + x1) / 2, inner: inner });
            }
        }
        // notes: a filled path followed by its fold, a small filled path at one of its corners (as the note script does)
        var notes = [];
        var byEl = new Map(shapes.map(function(o) { return [o.e, o]; })), used = new Set();
        shapes.forEach(function(o) {
            if (o.t !== 'path' || !o.filled || used.has(o.e)) return;
            var f = byEl.get(o.e.nextElementSibling);
            if (!f || f.t !== 'path' || !f.filled) return;
            var b1 = o.b, b2 = f.b;
            if (!b1.width || !b2.width) return;
            var small = b2.width < 25 && b2.height < 25 && b2.width < b1.width * 0.5 && b2.height < b1.height * 0.5;
            var atR = Math.abs(b2.x + b2.width - (b1.x + b1.width)) < 3, atL = Math.abs(b2.x - b1.x) < 3;
            var atT = Math.abs(b2.y - b1.y) < 3, atB = Math.abs(b2.y + b2.height - (b1.y + b1.height)) < 3;
            if (!small || !((atR || atL) && (atT || atB))) return;
            notes.push({ body: o.e, fold: f.e, b: b1 });
            used.add(f.e);
        });
        // The kinds come from the source this picture was drawn from. A redraw draws into a temporary child and moves
        // the picture into the container, so the kinds recorded at render time can be an earlier source's: with
        // Databases hidden they were the whole diagram's, longer than the notes drawn (the preview's round 17).
        var kinds = null;
        var drawnSource = host.getAttribute('data-plantuml');
        if (drawnSource) {
            var fromSource = noteKinds(drawnSource.split('\n'));
            if (fromSource.length === notes.length) kinds = fromSource;
        }
        if (!kinds && host._noteKinds && host._noteKinds.length === notes.length) kinds = host._noteKinds;

        /* WRITES */
        lifelines.forEach(function(e) { addClass(e, 'k-lifeline'); counts.lifelines++; });
        participants.forEach(function(o) { addClass(o.e, o.filled ? 'k-participant k-participant-fill' : 'k-participant'); counts.participants++; });
        stepBands.forEach(function(o) { addClass(o.e, 'k-step'); counts.bands++; });
        bandTexts.forEach(function(t) { addClass(t, 'k-step-text'); });
        stepRules.forEach(function(o) { addClass(o.e, 'k-step-rule'); });
        stretches.forEach(function(st) {
            var e = st.s.e, cx = (st.x0 + st.x1) / 2;
            // the strip: its left side to L, its right side to R; what is drawn in it, as one, to its new centre
            moveX(e, function(x) { return x < cx ? x + (st.L - st.x0) : x + (st.R - st.x1); });
            var shift = function(x) { return x + st.dx; };
            for (var i = 0; i < st.inner.length; i++) moveX(st.inner[i], shift);
            counts.stretched = (counts.stretched || 0) + 1;
        });
        // a table in a step bar is drawn on a ground of its own, behind its rules and text, so it reads as a table and
        // not as more of the bar, with its header row shaded; it goes where the stretch moved the table
        var dxOf = new Map(stretches.map(function(st) { return [st.s.e, st.dx]; }));
        stepTables.forEach(function(tb) {
            var r = document.createElementNS(NS, 'rect');
            r.setAttribute('x', fixed(tb.x + (dxOf.get(tb.s.e) || 0)));
            r.setAttribute('y', fixed(tb.y));
            r.setAttribute('width', fixed(tb.w));
            r.setAttribute('height', fixed(tb.h));
            r.setAttribute('class', 'k-step-table');
            tb.s.e.parentNode.insertBefore(r, tb.s.e.nextSibling);
            if (tb.headH > 0) {
                var hd = document.createElementNS(NS, 'rect');
                hd.setAttribute('x', fixed(tb.x + (dxOf.get(tb.s.e) || 0)));
                hd.setAttribute('y', fixed(tb.y));
                hd.setAttribute('width', fixed(tb.w));
                hd.setAttribute('height', fixed(tb.headH));
                hd.setAttribute('class', 'k-step-table-head');
                r.parentNode.insertBefore(hd, r.nextSibling);
                bandTexts.forEach(function(t) {
                    var ty = num(t, 'y');
                    if (ty > tb.y && ty <= tb.y + tb.headH + 1) addClass(t, 'k-step-table-head-text');
                });
            }
            counts.tables = (counts.tables || 0) + 1;
            // The bar's contents are lifted 1.5 units, so the space under the table matches the space above the title
            // (7 px above the title, 6 px under the table: the diagram is drawn scaled, so 1 unit could snap back to the
            // same pixel row). A transform moves what is painted only; nothing reads these elements' geometry after this.
            var sb = tb.s.b;
            var lift = function(el) { if (!el.getAttribute('transform')) { el.setAttribute('transform', 'translate(0,-1.5)'); addClass(el, 'k-step-lift'); } };
            bandTexts.forEach(function(t) { var ty = num(t, 'y'); if (ty > sb.y && ty < sb.y + sb.height + 1) lift(t); });
            stepRules.forEach(function(o) { if (boxIn(o, tb.s)) lift(o.e); });
            lift(r);
            if (tb.headH > 0) lift(r.nextSibling);
        });
        notes.forEach(function(n, idx) {
            var b = n.b, r = Math.min(5, b.width / 4, b.height / 4), x = b.x, y = b.y, w = b.width, h = b.height;
            var f = function(v) { return v.toFixed(2); };
            // the same bounds, so the note script's geometry (its box, its fold) still holds
            n.body.setAttribute('d', ['M', f(x + r), f(y), 'H', f(x + w - r), 'A', r, r, 0, 0, 1, f(x + w), f(y + r),
                'V', f(y + h - r), 'A', r, r, 0, 0, 1, f(x + w - r), f(y + h), 'H', f(x + r), 'A', r, r, 0, 0, 1, f(x), f(y + h - r),
                'V', f(y + r), 'A', r, r, 0, 0, 1, f(x + r), f(y), 'Z'].join(' '));
            addClass(n.body, kinds && kinds[idx] ? 'k-card k-card-' + kinds[idx].replace(/Note$/, '').toLowerCase() : 'k-card');
            addClass(n.fold, 'k-fold');
            counts.cards++;
        });
        counts.ms = +(performance.now() - started).toFixed(1);
        svg.setAttribute('data-k-roles', JSON.stringify(counts));
        return true;
    }

    window._diagramRoles = { isSequence: isSequence, noteKinds: noteKinds, tagRoles: tagRoles };
    if (!window.MutationObserver) return;
    // every picture the page draws or draws again is tagged as it arrives; one drawn hidden (a fragment of a long
    // diagram, a closed scenario) is tagged when it comes into view
    var waiting = window.IntersectionObserver ? new IntersectionObserver(function(entries) {
        entries.forEach(function(e) { if (e.isIntersecting && tagRoles(e.target)) waiting.unobserve(e.target); });
    }) : null;
    function visit(root) {
        var svgs = tag(root) === 'svg' ? [root] : (root.querySelectorAll ? root.querySelectorAll('svg') : []);
        Array.prototype.forEach.call(svgs, function(svg) {
            if (!svg.closest || !svg.closest('[data-k-sequence]')) return;
            if (!tagRoles(svg) && waiting) waiting.observe(svg);
        });
    }
    new MutationObserver(function(records) {
        records.forEach(function(r) { Array.prototype.forEach.call(r.addedNodes, function(node) { if (node.nodeType === 1) visit(node); }); });
    }).observe(document.documentElement, { childList: true, subtree: true });
})();
'''

# inside the script element the resource holds, after everything else in it
s = s.rstrip()
assert s.endswith('</script>'), s[-40:]
s = s[:-len('</script>')].rstrip() + '\n' + ROLES.lstrip('\n') + '</script>\n'
open(P, 'w', encoding='utf-8', newline='').write(s)
print('ok')
