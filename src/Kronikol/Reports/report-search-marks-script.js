// The marks (plans/SEARCH_PLAN.md 4.6.5): where a search's match is drawn, the place it is in (a note, an arrow's label, a
// step, the failure's line, a table row, a flow bar, a whole diagram) gets an outline in the accent that pulses twice as the
// reader arrives, and every occurrence of the query's terms inside the place a mark: the --kron-mark ground with a
// --kron-mark-edge underline, and the occurrence the line named an outline of the edge in place of the underline.
//
// In a drawn diagram a <rect pointer-events="none"> lies behind each run of marked characters on a row, measured with
// getExtentOfChar from the run's first character to its last in each <text>, merged along the row, in the SVG's own
// coordinates, so it follows every zoom, transform and viewBox with nothing measured again; it goes just before the <text>
// it marks, so the note's card stays under it and the words over it. Every extent is read before any rect is inserted:
// drawn so, 312 rects take 1.4 to 3 ms, against 24 to 261 ms when each insert forces the next read (the plan's
// marks-probe/). Not ::highlight on SVG text, which WebKit does not paint, nor a Range's rects, which Firefox misplaces once
// the diagram is scaled, nor an overlay in the page, 50 px and more off after a zoom. A <text> is never split into
// <tspan>s: WebKit lays a textLength text out differently once split.
//
// In the page's HTML (a step, the failure's pre, a table cell, a long note's scroll box, a Raw PlantUML section) the
// words are marked with the CSS Custom Highlight API, which leaves the page's text alone, so copies, exports, the note
// scripts and the navigation's place see what they saw. A browser without it gets the place's outline and no word marks.
//
// The marks are drawn again when their diagram is (a toggle, a note opened, a scroll box built as its note nears the
// window), and stay until the reader moves on: Escape with no layer open, another search or mark, a click outside the
// scenario, the scenario closed or left. Copies of the diagram and exports of the page carry none (context-menu-script.js's
// lookClone, report-export-function.js's export_undrawn).
(function () {
    'use strict';
    if (typeof document === 'undefined') return;
    var doc = document, SVGNS = 'http://www.w3.org/2000/svg';
    // the generator's join marker, which the engine resolves to a real zero-width space in the painted text
    var ZW = String.fromCharCode(0x200b), NBSP = String.fromCharCode(0xa0);
    // Between two rows: a row the engine wrapped in a one-line form (an arrow's label, a step bar), which the search reads
    // joined with nothing where the generator broke it (report-search-index.js, rule 5a) and with a space where the engine
    // wrapped it: a term's space matches it, or nothing does. And a line the text breaks itself (a pre's, the scroll
    // box's), which no term runs across.
    var GAP = String.fromCharCode(1), LINE = String.fromCharCode(10);
    var MARK = 'kron-mark', CURRENT = 'kron-mark-current';
    // what the marks add to a drawn diagram, which a copy of it leaves out
    var OWN = 'kron-mark-svg';

    // ---- the terms, and the text they are found in ----

    // the search's own fold (report-search-index.js): ASCII letters lowered, a run of spaces one space; nothing changes a
    // character's length, so a place in the folded text is a place in the drawn one
    function fold(c) {
        var k = c.charCodeAt(0);
        return k >= 65 && k <= 90 ? String.fromCharCode(k + 32) : c;
    }
    function isSpace(c) { return c === ' ' || c === '\t' || c === NBSP || c === '\n' || c === '\r' || c === '\f'; }
    function foldTerm(t) {
        var s = String(t === null || t === undefined ? '' : t), out = '';
        for (var i = 0; i < s.length; i++) {
            var c = s.charAt(i);
            if (c === ZW) continue;
            if (isSpace(c)) { if (out && out.charAt(out.length - 1) !== ' ') out += ' '; }
            else out += fold(c);
        }
        return out.charAt(out.length - 1) === ' ' ? out.slice(0, -1) : out;
    }

    // A place's text as folded characters, each with where it is drawn: { t, i, row } in a diagram (a <text> and its
    // character), { node, i } in the HTML. A run of spaces is one space; a join between rows is GAP, LINE or a space where
    // nothing is drawn.
    function stream() { return { chars: [], at: [] }; }
    function push(s, c, where) {
        if (c === ' ') {
            var last = s.chars[s.chars.length - 1];
            if (!s.chars.length || last === ' ' || last === GAP || last === LINE) return;
        }
        s.chars.push(c);
        s.at.push(where);
    }
    function join(s, how) {
        while (s.chars.length && s.chars[s.chars.length - 1] === ' ') { s.chars.pop(); s.at.pop(); }
        if (!s.chars.length || !how || (how === ' ' && s.chars[s.chars.length - 1] === LINE)) return;
        s.chars.push(how);
        s.at.push(null);
    }

    // the end of a term found at `at`, a GAP matching the term's space or nothing; -1 where it is not there
    function matchAt(text, at, term) {
        var i = 0, j = at;
        while (i < term.length) {
            if (j >= text.length) return -1;
            var c = text.charAt(j);
            if (c === GAP) { if (term.charAt(i) === ' ') i++; j++; continue; }
            if (c !== term.charAt(i)) return -1;
            i++;
            j++;
        }
        return j;
    }
    // every occurrence of the terms, in reading order; occurrences that overlap are one
    function occurrences(s, terms) {
        var text = s.chars.join(''), found = [];
        terms.forEach(function (term) {
            var first = term.charAt(0), from = 0;
            while (from < text.length) {
                var at = text.indexOf(first, from);
                if (at < 0) break;
                var end = matchAt(text, at, term);
                if (end > at) { found.push({ s: at, e: end }); from = end; }
                else from = at + 1;
            }
        });
        found.sort(function (a, b) { return a.s - b.s || b.e - a.e; });
        var merged = [];
        found.forEach(function (o) {
            var last = merged[merged.length - 1];
            if (last && o.s < last.e) { if (o.e > last.e) last.e = o.e; }
            else merged.push({ s: o.s, e: o.e, stream: s });
        });
        return merged;
    }

    // ---- a drawn diagram ----

    function isOwn(e) { return !!(e && e.classList && e.classList.contains(OWN)); }
    function num(e, a) { return parseFloat(e.getAttribute(a)) || 0; }
    function tagOf(e) { return e.tagName.toLowerCase(); }
    // a text the diagram shows: not one in a clear ink (a capped note's widest line), not a note button's glyph, not hidden
    // (a row a scroll box lies over, collapsible-notes-styles.css)
    function shownText(t) {
        if (/^#[0-9a-f]{6}00$/i.test(t.getAttribute('fill') || '')) return false;
        if (t.closest('.note-toggle-icon, title, defs, .' + OWN)) return false;
        var cs = getComputedStyle(t);
        return cs.visibility !== 'hidden' && cs.display !== 'none';
    }
    // a text's width as laid out (the engine writes no textLength)
    function widthOf(t) {
        try { return t.getComputedTextLength(); } catch (x) { return 0; }
    }
    // The rows a place's texts are drawn on: the engine draws each word and each run of spaces as a <text>, and a row is the
    // texts with one y that touch, top to bottom, each left to right; texts with one y and a gap between them wider than
    // half their font (two participants side by side, 58 px apart) are rows of their own, which nothing joins. How a row
    // ends says what joins it to the next: a pair of zero-width spaces, the space the generator's wrap took; one, nothing (a
    // run it cut); none, a wrap of the engine's, which took a space, or in a one-line form a line of its source too
    // (`rowJoin`).
    function rowsOf(texts, rowJoin) {
        var by = {}, lines = [], rows = [];
        texts.forEach(function (t) {
            var key = String(Math.round(num(t, 'y') * 10) / 10), line = by[key];
            if (!line) { line = by[key] = { y: num(t, 'y'), texts: [] }; lines.push(line); }
            line.texts.push(t);
        });
        lines.sort(function (a, b) { return a.y - b.y; });
        lines.forEach(function (line) {
            line.texts.sort(function (a, b) { return num(a, 'x') - num(b, 'x'); });
            var row = null, end = 0;
            line.texts.forEach(function (t) {
                var x = num(t, 'x');
                if (row && x - end > (num(t, 'font-size') || 12) / 2) { row.apart = true; row = null; }
                if (!row) { row = { y: line.y, texts: [] }; rows.push(row); end = x; }
                row.texts.push(t);
                end = Math.max(end, x + widthOf(t));
            });
        });
        rows.forEach(function (row) {
            var end = row.texts.map(function (t) { return t.textContent || ''; }).join('').replace(/[ \t\n\r]+$/, '');
            row.join = row.apart ? LINE : end.slice(-2) === ZW + ZW ? ' ' : end.slice(-1) === ZW ? '' : rowJoin;
        });
        return rows;
    }
    function svgStream(rows) {
        var s = stream();
        rows.forEach(function (row, r) {
            if (r > 0) join(s, rows[r - 1].join);
            row.texts.forEach(function (t) {
                var text = t.textContent || '';
                for (var i = 0; i < text.length; i++) {
                    var c = text.charAt(i);
                    if (c !== ZW) push(s, isSpace(c) ? ' ' : fold(c), { t: t, i: i, row: r });
                }
            });
        });
        return s;
    }
    // an occurrence as the runs it is drawn in, one a row: the part of each <text>, [from, to]
    function svgRuns(o) {
        var runs = [], run = null;
        for (var k = o.s; k < o.e; k++) {
            var at = o.stream.at[k];
            if (!at || !at.t) continue;
            if (!run || run.row !== at.row) { run = { row: at.row, parts: [] }; runs.push(run); }
            var part = run.parts[run.parts.length - 1];
            if (!part || part.t !== at.t) run.parts.push({ t: at.t, from: at.i, to: at.i });
            else part.to = at.i;
        }
        return runs;
    }
    // two occurrences in reading order, each from the group it was found in: an earlier picture first, then top to bottom
    // and left to right by where each begins
    function readingOrder(a, b) {
        var ta = firstText(a), tb = firstText(b);
        if (!ta || !tb || ta === tb) return a.s - b.s;
        if (ta.ownerSVGElement !== tb.ownerSVGElement) return earlier(ta, tb) === ta ? -1 : 1;
        return num(ta, 'y') - num(tb, 'y') || num(ta, 'x') - num(tb, 'x');
    }
    function firstText(o) {
        for (var k = o.s; k < o.e; k++) if (o.stream.at[k] && o.stream.at[k].t) return o.stream.at[k].t;
        return null;
    }
    function earlier(a, b) { return a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_PRECEDING ? b : a; }
    function later(a, b) { return a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING ? b : a; }
    function unite(box, r) {
        if (!r || !(r.width > 0 || r.height > 0)) return box;
        if (!box) return { x: r.x, y: r.y, r: r.x + r.width, b: r.y + r.height };
        box.x = Math.min(box.x, r.x);
        box.y = Math.min(box.y, r.y);
        box.r = Math.max(box.r, r.x + r.width);
        box.b = Math.max(box.b, r.y + r.height);
        return box;
    }
    function bboxOf(e) { try { return e.getBBox(); } catch (x) { return null; } }
    function fixed(v) { return String(Math.round(v * 100) / 100); }
    function svgElement(name, cls, attrs) {
        var e = doc.createElementNS(SVGNS, name);
        e.setAttribute('class', OWN + ' ' + cls);
        e.setAttribute('pointer-events', 'none');
        e.setAttribute('vector-effect', 'non-scaling-stroke');
        for (var k in attrs) if (attrs[k] !== null && attrs[k] !== undefined) e.setAttribute(k, attrs[k]);
        return e;
    }

    // ---- the page's HTML ----

    function cached(cache, el, read) {
        var v = cache.get(el);
        if (v === undefined) { v = read(el); cache.set(el, v); }
        return v;
    }
    function htmlStream(root) {
        var s = stream(), shown = new Map(), lines = new Map();
        // a picture's own text is marked as a picture's is (its rects), and a button's is no part of a place
        var visible = function (el) {
            return el.namespaceURI !== SVGNS && !el.closest('script, style, template, noscript, title, button, .diagram-zoom-controls')
                && el.getClientRects().length > 0;
        };
        var keepsLines = function (el) { return /^(pre|pre-wrap|pre-line|break-spaces)$/.test(getComputedStyle(el).whiteSpace); };
        var walker = doc.createTreeWalker(root, NodeFilter.SHOW_TEXT, null), node;
        while ((node = walker.nextNode())) {
            var el = node.parentElement;
            if (!el || !cached(shown, el, visible)) continue;
            var text = node.data, pre = cached(lines, el, keepsLines);
            for (var i = 0; i < text.length; i++) {
                var c = text.charAt(i);
                if (c === ZW) continue;
                if ((c === '\n' || c === '\r') && pre) join(s, LINE);
                else push(s, isSpace(c) ? ' ' : fold(c), { node: node, i: i });
            }
        }
        return s;
    }
    function rangeOf(o) {
        var first = null, last = null;
        for (var k = o.s; k < o.e; k++) {
            var at = o.stream.at[k];
            if (!at || !at.node) continue;
            if (!first) first = at;
            last = at;
        }
        if (!first) return null;
        var r = doc.createRange();
        r.setStart(first.node, first.i);
        r.setEnd(last.node, last.i + 1);
        return r;
    }
    function canHighlight() { return !!(window.CSS && CSS.highlights && typeof window.Highlight === 'function'); }
    function highlight(ranges) {
        var h = new window.Highlight();
        ranges.forEach(function (r) { h.add(r); });
        return h;
    }

    // A long note's scroll box brought to a range in it, once its text is built. The box is drawn at its diagram's scale, so
    // a distance on the screen is the box's own distance times that scale.
    function scrollBoxTo(box, range) {
        if (!box || !range) return false;
        var b = box.getBoundingClientRect(), r = range.getBoundingClientRect();
        if (!b.height || !box.clientHeight || !(r.width || r.height)) return false;
        box.scrollTop += ((r.top + r.bottom) / 2 - (b.top + b.bottom) / 2) / (b.height / box.clientHeight);
        return true;
    }
    // the scroll box a node of the page's text is in, if any
    function boxAround(node) {
        var el = node.nodeType === 1 ? node : node.parentElement;
        return el ? el.closest('.note-scroll') : null;
    }

    // ---- the place ----

    function hostOf(e) { return e && e.closest ? e.closest('[data-diagram-type], .plantuml-browser') : null; }
    function scopeOf(e) {
        if (!e || !e.closest) return doc.body;
        return e.closest('details.scenario') || e.closest('.report-panel') || hostOf(e) || doc.body;
    }
    // the pictures drawn inside an element (a diagram's: one, or one a part), not a button's icon
    function svgsOf(el) {
        return el ? Array.prototype.filter.call(el.querySelectorAll('svg'), function (s) {
            return !s.parentNode.closest('svg') && !s.closest('button, .diagram-zoom-controls') && !!hostOf(s);
        }) : [];
    }
    function noteGroups(svg) {
        try { return window._findNoteGroups ? window._findNoteGroups(svg) : []; } catch (x) { return []; }
    }
    function noteIndexOf(grp) {
        var ix = window._noteIndicesOf ? window._noteIndicesOf(grp) : null;
        return ix ? ix.original : null;
    }
    function boxOfAll(elements) {
        var box = null;
        elements.forEach(function (e) { box = unite(box, bboxOf(e)); });
        return box;
    }
    // a capped note's scroll box: by the note's index in the original source, else the box laid over its card
    function scrollBoxOf(grp, svg) {
        var idx = noteIndexOf(grp), frame = idx === null ? null : svg.querySelector('.note-scroll-frame[data-note="' + idx + '"]');
        if (!frame) {
            var nb = boxOfAll(grp.paths);
            frame = nb ? Array.prototype.filter.call(svg.querySelectorAll('.note-scroll-frame'), function (f) {
                var x = num(f, 'x'), y = num(f, 'y');
                return x >= nb.x - 1 && x <= nb.r + 1 && y >= nb.y - 1 && y <= nb.b + 1;
            })[0] : null;
        }
        return frame ? frame.querySelector('.note-scroll') : null;
    }
    // the texts an element draws: a text itself, a group's, or those drawn in a shape's box, by where their baselines start
    // (a step bar's, an assertion's)
    function textsOf(e) {
        var tag = tagOf(e);
        if (tag === 'text') return [e];
        if (tag === 'g' || tag === 'a') return Array.prototype.slice.call(e.querySelectorAll('text'));
        var b = bboxOf(e), svg = e.ownerSVGElement;
        if (!b || !svg) return [];
        return Array.prototype.filter.call(svg.querySelectorAll('text'), function (t) {
            var x = num(t, 'x'), y = num(t, 'y');
            return x >= b.x - 1 && x <= b.x + b.width + 1 && y >= b.y - 1 && y <= b.y + b.height + 1;
        });
    }
    function textKey(texts) { return foldTerm(texts.map(function (t) { return t.textContent || ''; }).join(' ')); }
    function alive(place) {
        if (!place || typeof place === 'function') return !!place;
        if (place.paths) return place.paths.length > 0 && place.paths[0].isConnected;
        if (place.nodeType) return place.isConnected;
        return Array.prototype.every.call(place, function (e) { return e && e.isConnected; });
    }

    // What a place is: its <text>s in groups, each read as one text with how its rows join, what its outline goes round and
    // the element it goes after, its scroll box; or the HTML element it is, with the pictures it holds. A place given as an
    // element carries how to find it again in a picture drawn anew: a note by its index in the original source, any other
    // element by its kind, its words and which of those it is.
    function resolve(place) {
        if (typeof place === 'function') {
            try { place = place(); } catch (x) { place = null; }
        }
        if (!place || !alive(place)) return null;
        if (place.paths && place.texts) return noteOf(place);
        if (!place.nodeType) {
            var list = Array.prototype.filter.call(place, function (e) { return e && e.nodeType === 1 && e.ownerSVGElement; });
            return list.length ? svgOf(list) : null;
        }
        if (place.nodeType !== 1) return null;
        // a long note's scroll box by its frame: HTML, though the frame is the picture's
        var inFrame = place.classList.contains('note-scroll-frame') ? place.querySelector('.note-scroll') : null;
        if (inFrame) place = inFrame;
        if (place.ownerSVGElement) {
            // a note's card or its fold is the note
            if (tagOf(place) === 'path') {
                var grp = noteGroups(place.ownerSVGElement).filter(function (g) { return g.paths.indexOf(place) >= 0; })[0];
                if (grp) return noteOf(grp);
            }
            return svgOf([place]);
        }
        // an HTML element: its words, and those of the pictures it holds (a diagram given whole) as a picture's are; a scroll
        // box found again, in a picture drawn anew, by its note
        var html = place, groups = [], host = hostOf(place);
        (tagOf(html) === 'svg' ? [html] : svgsOf(html)).forEach(function (svg) { groups = groups.concat(pictureGroups(svg)); });
        var frame = html.closest('.note-scroll-frame'), note = frame ? frame.getAttribute('data-note') : null;
        return {
            kind: 'html', html: html, groups: groups, box: html.classList.contains('note-scroll') ? html : null, host: host,
            scope: scopeOf(place),
            refind: note === null || !host ? null : function () { return host.querySelector('.note-scroll-frame[data-note="' + note + '"]'); }
        };
    }
    // A whole picture's texts as groups each read as one text, as each is a place of its own: every note, every arrow's
    // label without its number, and the rest (a participant, a step bar, an assertion) row by row.
    function pictureGroups(svg) {
        var groups = [], taken = new Set();
        noteGroups(svg).forEach(function (grp) {
            grp.texts.forEach(function (t) { taken.add(t); });
            groups.push({ svg: svg, texts: grp.texts.filter(shownText), rowJoin: ' ' });
        });
        Array.prototype.forEach.call(svg.querySelectorAll('g.message'), function (g) {
            var inner = textsOf(g);
            inner.forEach(function (t) { taken.add(t); });
            groups.push({ svg: svg, texts: wordsOf(g, inner).filter(shownText), rowJoin: GAP });
        });
        groups.push({ svg: svg, texts: Array.prototype.filter.call(svg.querySelectorAll('text'), function (t) {
            return !taken.has(t) && shownText(t);
        }), rowJoin: LINE });
        return groups.filter(function (g) { return g.texts.length > 0; });
    }
    function noteOf(grp) {
        var svg = grp.paths[0].ownerSVGElement;
        if (!svg) return null;
        var host = hostOf(svg), idx = noteIndexOf(grp);
        return {
            kind: 'svg', groups: [{ svg: svg, texts: grp.texts.filter(shownText), rowJoin: ' ' }], outline: grp.paths,
            after: grp.paths.concat(grp.texts).reduce(later), box: scrollBoxOf(grp, svg), host: host, scope: scopeOf(svg),
            refind: idx === null ? null : function () {
                var found = null;
                svgsOf(host).forEach(function (s) { noteGroups(s).forEach(function (g) { if (!found && noteIndexOf(g) === idx) found = g; }); });
                return found;
            }
        };
    }
    // An arrow's number, which the engine draws left of its label and, on a label of several rows, half way down between
    // them: no part of the label's text, which the source and the search hold without it, nor of what the label is known
    // by in a picture drawn anew, where a toggle that took an arrow out has numbered the arrows after it anew.
    function numberOf(g, texts) {
        var first = texts[0];
        if (!first || !g.classList.contains('message') || !/^\d+$/.test((first.textContent || '').trim())) return null;
        return texts.every(function (t) { return t === first || num(t, 'x') > num(first, 'x'); }) ? first : null;
    }
    // the texts an element's words are drawn in: its texts, an arrow's number left out
    function wordsOf(e, inner) {
        inner = inner || textsOf(e);
        var number = tagOf(e) === 'g' ? numberOf(e, inner) : null;
        return number ? inner.filter(function (t) { return t !== number; }) : inner;
    }
    function svgOf(elements) {
        var svg = elements[0].ownerSVGElement, texts = [], outline = [], block = false;
        elements.forEach(function (e) {
            var tag = tagOf(e), inner = textsOf(e);
            texts = texts.concat(wordsOf(e, inner));
            // an arrow's label is outlined round its words and its number, a text or a shape round itself
            if ((tag === 'g' || tag === 'a') && inner.length) outline = outline.concat(inner); else outline.push(e);
            // an assertion is a note of lines, as a payload is; a label, a step bar, a participant are one-line forms
            if (tag === 'polygon') block = true;
        });
        var host = hostOf(svg);
        return {
            kind: 'svg', groups: [{ svg: svg, texts: texts.filter(shownText), rowJoin: block ? ' ' : GAP }], outline: outline,
            after: elements.concat(texts).reduce(later), box: null, host: host, scope: scopeOf(svg),
            refind: elements.length === 1 ? finderOf(elements[0], host) : null
        };
    }
    // One element found again: the same tag, class and paint, the same words, and the same one of those in the diagram.
    function finderOf(e, host) {
        var tag = tagOf(e), cls = e.getAttribute('class') || '', fill = (e.getAttribute('fill') || '').toLowerCase();
        var keyOf = function (c) { return textKey(wordsOf(c)); }, key = keyOf(e);
        var like = function () {
            var out = [];
            svgsOf(host).forEach(function (s) {
                Array.prototype.forEach.call(s.querySelectorAll(tag), function (c) {
                    if (!isOwn(c) && (c.getAttribute('class') || '') === cls && (c.getAttribute('fill') || '').toLowerCase() === fill
                        && keyOf(c) === key) out.push(c);
                });
            });
            return out;
        };
        var nth = like().indexOf(e);
        return nth < 0 ? null : function () { return like()[nth] || null; };
    }

    // ---- drawing ----

    var active = null;

    function appliedQuery() {
        var bar = doc.getElementById('searchbar');
        return bar ? bar.value.trim() : '';
    }
    function rendered(e) { return !!e && e.isConnected && e.getClientRects().length > 0; }
    function layerOpen() {
        var all = window.kronikol_layers || {};
        for (var k in all) if (all[k] && all[k].isOpen && all[k].isOpen()) return true;
        // the diagram's menu, opened on the marked diagram: a copy from it is no step away
        return !!doc.querySelector('.diagram-ctx-menu');
    }

    // takes a drawing off the page: its rects, its outline, its highlights
    function undraw(rec) {
        var drawn = rec.drawn;
        rec.drawn = null;
        if (!drawn) return;
        drawn.own.forEach(function (e) { if (e.parentNode) e.parentNode.removeChild(e); });
        if (drawn.html) drawn.html.classList.remove('kron-mark-place', 'kron-mark-arrive');
        if (drawn.highlights) { CSS.highlights.delete(MARK); CSS.highlights.delete(CURRENT); }
    }

    // the place as it is now: the function given called again, the element given while it is on the page, else found anew
    function placeOf(rec) {
        if (rec.spec) return resolve(rec.spec);
        var p = alive(rec.place) ? resolve(rec.place) : null;
        return p || (rec.refind ? resolve(rec.refind) : null);
    }

    // Draws a record's marks on its place as it is now: every occurrence found, every extent read, then every mark written.
    function draw(rec, arrive) {
        undraw(rec);
        var p = placeOf(rec);
        rec.found = p;
        if (!p) return null;
        if (p.refind) rec.refind = p.refind;
        rec.host = p.host || rec.host;
        rec.scope = p.scope || rec.scope;
        var svgs = [], texts = [];
        p.groups.forEach(function (g) {
            if (svgs.indexOf(g.svg) < 0) svgs.push(g.svg);
            texts = texts.concat(g.texts);
        });
        var drawn = rec.drawn = { own: [], html: null, highlights: false, svgs: svgs, texts: texts, box: p.box };

        // the role pass reads a picture and writes its classes once: it goes first, so the reads below come after it
        if (window._diagramRoles && window._diagramRoles.tagRoles) {
            svgs.forEach(function (svg) {
                try { window._diagramRoles.tagRoles(svg); } catch (x) { /* drawn without its roles */ }
            });
        }
        // the occurrences in reading order: each group's rows (a whole picture's groups top to bottom), then the text of
        // the HTML place, or of the note's scroll box
        var svgOcc = [];
        p.groups.forEach(function (g) {
            occurrences(svgStream(rowsOf(g.texts, g.rowJoin)), rec.terms).forEach(function (o) { svgOcc.push(o); });
        });
        if (p.groups.length > 1) svgOcc.sort(readingOrder);
        var htmlRoot = p.kind === 'html' ? p.html : p.box;
        var htmlOcc = htmlRoot ? occurrences(htmlStream(htmlRoot), rec.terms) : [];
        var count = svgOcc.length + htmlOcc.length;
        var current = rec.current < count ? rec.current : -1, aim = current >= 0 ? current : 0;
        var result = { count: count, current: current, target: null, place: null };
        rec.count = count;

        // a picture not laid out (a closed section, a tab not shown) is drawn on once it is
        var sized = svgs.filter(function (svg) { return svg.getBoundingClientRect().width > 0; });
        svgs.forEach(function (svg) { if (sized.indexOf(svg) < 0) waitForSize(rec, svg); });

        /* READS */
        var marks = [];
        svgOcc.forEach(function (o, n) {
            svgRuns(o).forEach(function (run) {
                var box = null, anchor = null;
                run.parts.forEach(function (part) {
                    if (sized.indexOf(part.t.ownerSVGElement) < 0) return;
                    try {
                        box = unite(box, part.t.getExtentOfChar(part.from));
                        if (part.to !== part.from) box = unite(box, part.t.getExtentOfChar(part.to));
                    } catch (x) { /* a character the text does not draw */ }
                    anchor = anchor ? earlier(anchor, part.t) : part.t;
                });
                if (box) marks.push({ box: box, anchor: anchor, n: n });
            });
        });
        var ring = p.kind === 'svg' && sized.length ? boxOfAll(p.outline) : null;
        /* WRITES */
        marks.forEach(function (m) {
            var named = m.n === current, transform = m.anchor.getAttribute('transform');
            var ground = svgElement('rect', 'kron-mark-ground' + (named ? ' ' + CURRENT : ''), {
                x: fixed(m.box.x), y: fixed(m.box.y), width: fixed(m.box.r - m.box.x), height: fixed(m.box.b - m.box.y),
                rx: '2', transform: transform
            });
            m.anchor.parentNode.insertBefore(ground, m.anchor);
            drawn.own.push(ground);
            if (!named) {
                var edge = svgElement('line', 'kron-mark-edge', {
                    x1: fixed(m.box.x), y1: fixed(m.box.b), x2: fixed(m.box.r), y2: fixed(m.box.b), transform: transform
                });
                m.anchor.parentNode.insertBefore(edge, m.anchor);
                drawn.own.push(edge);
            }
            if (m.n === aim && !result.target) result.target = ground;
        });
        if (ring) {
            var outline = svgElement('rect', 'kron-mark-place' + (arrive ? ' kron-mark-arrive' : ''), {
                x: fixed(ring.x - 3), y: fixed(ring.y - 3), width: fixed(ring.r - ring.x + 6), height: fixed(ring.b - ring.y + 6), rx: '6'
            });
            p.after.parentNode.insertBefore(outline, p.after.nextSibling);
            drawn.own.push(outline);
            result.place = outline;
        }
        if (p.kind === 'html') {
            drawn.html = p.html;
            p.html.classList.remove('kron-mark-arrive');
            p.html.classList.add('kron-mark-place');
            if (arrive) {
                // its style read once between, so the pulse starts again on an element marked before
                void getComputedStyle(p.html).outlineWidth;
                p.html.classList.add('kron-mark-arrive');
            }
            result.place = p.html;
        }

        // the HTML's words, the one named in a highlight of its own; a scroll box brought to the one aimed at
        if (htmlOcc.length) {
            var words = [], named = [];
            htmlOcc.forEach(function (o, k) {
                var r = rangeOf(o);
                if (!r) return;
                if (svgOcc.length + k === current) named.push(r); else words.push(r);
                if (svgOcc.length + k === aim) result.target = r;
            });
            if (canHighlight()) {
                CSS.highlights.set(MARK, highlight(words));
                CSS.highlights.set(CURRENT, highlight(named));
                drawn.highlights = true;
            }
            var target = result.target, inBox = target && target.startContainer ? boxAround(target.startContainer) : null;
            if (inBox) scrollBoxTo(inBox, target);
        }
        if (rec.observer) rec.observer.takeRecords();
        return result;
    }

    // ---- staying, and going ----

    // A redraw takes the marks' picture or its texts off the page, a scroll box built as its note nears the window lies over
    // rows that were marked, and a picture is drawn in an HTML place marked before it was: each draws the marks again on the
    // place found anew. The marks' own rects are no change.
    function boxNode(n) {
        return n.nodeType === 1 && !isOwn(n) && (n.classList.contains('note-scroll-frame') || !!n.querySelector('.note-scroll-frame'));
    }
    function pictureNode(n) {
        return n.nodeType === 1 && !isOwn(n) && (tagOf(n) === 'svg' || !!n.querySelector('svg'));
    }
    function stale(rec, records) {
        var d = rec.drawn;
        if (!d) return true;
        if (d.svgs.some(function (s) { return !s.isConnected; }) || (d.box && !d.box.isConnected)) return true;
        if (d.texts.some(function (t) { return !t.isConnected; })) return true;
        return records.some(function (r) {
            return Array.prototype.some.call(r.addedNodes, function (n) { return boxNode(n) || (!!d.html && pictureNode(n)); })
                || Array.prototype.some.call(r.removedNodes, boxNode);
        });
    }
    function redraw(rec) {
        if (rec !== active) return;
        // a picture still being drawn is waited for
        if (rec.host && rec.host._noteRendering) {
            if (!rec.retry) rec.retry = setTimeout(function () { rec.retry = 0; redraw(rec); }, 100);
            return;
        }
        draw(rec, false);
    }
    function watch(rec) {
        if (!rec.host || !window.MutationObserver) return;
        rec.observer = new MutationObserver(function (records) {
            if (rec === active && !rec.retry && stale(rec, records)) redraw(rec);
        });
        rec.observer.observe(rec.host, { childList: true, subtree: true });
    }
    function waitForSize(rec, svg) {
        if (!window.ResizeObserver) return;
        if (!rec.sized) {
            rec.sized = new ResizeObserver(function (entries) {
                if (rec !== active) return;
                if (entries.some(function (e) { return e.target.isConnected && e.target.getBoundingClientRect().width > 0; })) {
                    rec.sized.disconnect();
                    draw(rec, false);
                }
            });
        }
        rec.sized.observe(svg);
    }

    function clear() {
        var rec = active;
        active = null;
        if (!rec) return;
        if (rec.observer) rec.observer.disconnect();
        if (rec.sized) rec.sized.disconnect();
        if (rec.retry) clearTimeout(rec.retry);
        undraw(rec);
        // what the way to the match said beside them goes with them (report-search-hit-script.js)
        doc.dispatchEvent(new Event('kron-marks-cleared'));
    }

    // Marks the terms in a place: an element of the page's HTML, and the pictures it holds (a diagram's host, a diagram given
    // whole); an element of a drawn diagram (a <text>, a <g> such as an arrow's label, a shape such as a step bar or an
    // assertion, a note's card); a note as _findNoteGroups gives it ({ paths, texts }); a list of a diagram's elements; or a
    // function that returns one of these, called again to find the place in each picture drawn anew. `terms`: a term or a list of them, the query's positive terms. `current`: which of
    // the place's occurrences, in reading order, the line named (none when below 0 or left out). Returns the occurrences
    // found, the one named (-1 for none), the mark or the Range of that one, else of the first, to bring into view, and the
    // element outlined; null where the place is not found. The marks before are taken off first.
    function mark(place, terms, current) {
        clear();
        var rec = {
            spec: typeof place === 'function' ? place : null, place: typeof place === 'function' ? null : place, refind: null,
            terms: (Array.isArray(terms) ? terms : [terms]).map(foldTerm).filter(function (t) { return t.length > 0; }),
            current: typeof current === 'number' && current >= 0 ? current : -1, query: appliedQuery(), at: performance.now(),
            host: null, scope: null, drawn: null, found: null, count: 0
        };
        active = rec;
        var result = draw(rec, true);
        if (!result) { active = null; return null; }
        watch(rec);
        return result;
    }

    // Escape, once no layer is open: read as the key goes down, before a layer's own handler closes it. Pressed in the filter
    // box, or on the bar's Filters while the box is open, it is the box's: it closes the box and leaves the marks
    // (plans/SEARCH_PLAN.md F4).
    function boxEscape(t) {
        var box = doc.getElementById('filtering-box');
        return !!(box && !box.hidden && t && t.closest && t.closest('#filtering-box, .nav-filters'));
    }
    window.addEventListener('keydown', function (e) {
        if (active && e.key === 'Escape' && !e.isComposing && !layerOpen() && !boxEscape(e.target)) clear();
    }, true);
    // a click outside the scenario: on the page's ground, another section, a tab; not one in a layer or the diagram's menu,
    // nor the page's scrollbar, nor the bar's own controls other than its tabs (the search button, Filters and the filter
    // box, the theme switch: none leaves the scenario, and a filter that hides it takes the marks off below)
    function inBar(t) {
        return !!(t.closest && t.closest('.report-nav, #filtering-box') && !t.closest('.report-tabs'));
    }
    function pressed(e) {
        var rec = active, t = e.target;
        if (!rec || !t || t === doc.documentElement || layerOpen() || inBar(t) || (rec.scope && rec.scope.contains(t))) return;
        clear();
    }
    window.addEventListener('pointerdown', pressed, true);
    window.addEventListener('click', pressed, true);
    // the scenario, or a section holding the place, closed
    doc.addEventListener('toggle', function (e) {
        var rec = active, d = e.target;
        if (!rec || !d || d.open || !d.contains) return;
        var at = rec.drawn ? rec.drawn.html || rec.drawn.svgs[0] : null;
        if (d.contains(at || rec.scope)) clear();
    }, true);
    // another search, or a filter that hides the scenario
    doc.addEventListener('kron-filter-change', function () {
        if (active && (appliedQuery() !== active.query || !rendered(active.scope))) clear();
    });
    // Back, Forward or a link that leaves the scenario: once the page has moved, the scenario is not shown
    function moved(e) {
        var rec = active;
        if (!rec || e.timeStamp < rec.at) return;
        requestAnimationFrame(function () {
            requestAnimationFrame(function () {
                if (active === rec && !layerOpen() && !rendered(rec.scope)) clear();
            });
        });
    }
    window.addEventListener('popstate', moved);
    window.addEventListener('hashchange', moved);

    // A search run with another query takes them off. The search function is wrapped as the Search tab's script wraps it: a
    // search that shows the same scenarios sends no kron-filter-change.
    function watchSearch() {
        var run = window.run_search_scenarios;
        if (typeof run !== 'function' || run.kronMarks) return;
        var wrapped = function () {
            var out = run.apply(this, arguments);
            if (active && appliedQuery() !== active.query) clear();
            return out;
        };
        wrapped.kronMarks = true;
        window.run_search_scenarios = wrapped;
    }
    if (doc.readyState === 'loading') doc.addEventListener('DOMContentLoaded', watchSearch);
    else watchSearch();

    window.kronikol_marks = {
        mark: mark,
        clear: clear,
        scrollBoxTo: scrollBoxTo,
        // the marks on the page: the terms and the occurrences found, or null
        marked: function () { return active ? { terms: active.terms.slice(), count: active.count } : null; }
    };
})();
