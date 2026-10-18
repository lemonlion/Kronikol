
/* Diagram look (a separate preview, 2026-10-17): sequence diagrams drawn the modern way the Mermaid spike measured,
   in the report's palette and font. Two halves, and neither depends on a colour:
   1. The SOURCE gets proportions and a font, never colours. Every render passes through here (first render, the
      note controls' re-renders, internal-flow popups, prefetches), so every drawing gets the same look.
   2. Once drawn, each element is found by its STRUCTURE and given a role class, and the stylesheet colours the roles
      from theme tokens. Lifelines are the long vertical lines; participants are the shapes at their ends; a step
      band is a filled shape spanning every lifeline; a note is a filled path with the fold triangle at a corner (the
      report's own note script finds notes the same way); a message number is a bold run of digits sitting on a
      message arrow. Note kinds (event, assertion) come from the source, in order. So a PlantUML theme, a skinparam,
      or a recoloured emitter changes nothing here, and a report theme is just another set of token values.
   Seven levers; the six taken are on (not the badges); window.__kronikolLookLevers (set before the page loads) picks a set:
     font      the report's face. `system-ui`: the engine measures and paints the same face. A font LIST is quoted by
               the engine into one unknown name and paints Times New Roman.
     geometry  rounded participant boxes with room around their names, thin solid light lifelines, centred message
               labels, a little more air between messages, no shadows
     services  services as named boxes, as Mermaid draws participants, in place of UML entity circles
     cards     notes drawn as rounded cards: PlantUML still draws its folded note (the report's note controls find
               notes by the fold), then the body is redrawn as a rounded rectangle of the same bounds and the fold
               is hidden but kept
     bands     step bars as light bands with dark text, in place of black hexagons with white text
     strips    every strip (the steps, the assertions, the Test bar, a Row marker) runs the drawing's full width
     badges    message numbers in pills, as Mermaid draws them */
(function () {
  // the owner took every lever but the badges (2026-10-17): they stay available to __kronikolLookLevers
  // the owner kept every lever but the numbered pills (round 8); the pills stay available through
  // window.__kronikolLookLevers. The strips (round 15) are their own lever, on, so they can be measured on and off.
  var DEFAULT = ['font', 'geometry', 'services', 'cards', 'bands', 'strips'];
  var levers = {};
  (window.__kronikolLookLevers || DEFAULT).forEach(function (l) { levers[l] = true; });

  /* ---------- 1. the source ---------- */
  function sequenceStyle() {
    var out = [];
    if (levers.font) out.push('skinparam defaultFontName system-ui');
    if (levers.geometry) out.push('skinparam shadowing false', 'skinparam SequenceMessagePadding 4', 'skinparam sequenceMessageAlignment center');
    var body = [];
    if (levers.geometry) body.push(
      '  participant {', '    RoundCorner 12', '    Padding 8 14', '    Margin 0 10', '    LineThickness 1', '  }',
      '  actor, database, queue, collections, boundary, control, entity {', '    Margin 0 10', '    LineThickness 1', '  }',
      '  lifeLine {', '    LineStyle 0', '    LineThickness 1', '  }');
    if (levers.cards) body.push('  note {', '    LineThickness 1', '  }');
    if (body.length) out = out.concat(['<style>', 'sequenceDiagram {'], body, ['}', '</style>']);
    return out;
  }
  function isSequence(lines) {
    for (var i = 0; i < lines.length && i < 6; i++) if (/^!pragma teoz/.test(lines[i])) return true;
    return false;
  }
  function insertAfterHeader(lines, extra) {
    if (!extra.length) return lines.slice();
    var out = lines.slice(), at = -1;
    for (var i = 0; i < out.length; i++) if (/^!pragma teoz/.test(out[i])) { at = i; break; }
    if (at < 0) for (var j = 0; j < out.length; j++) if (/^@start/.test(out[j])) { at = j; break; }
    if (at < 0) return lines.slice();
    Array.prototype.splice.apply(out, [at + 1, 0].concat(extra));
    return out;
  }
  // the kind of each note, in drawing order: the stereotype Kronikol gives it (eventNote, assertionNote) or ''
  function noteKinds(lines) {
    var kinds = [];
    lines.forEach(function (l) {
      var m = /^note(?:<<([A-Za-z]+)>>)? (?:left|right|over)\b/.exec(l);
      if (m) kinds.push(m[1] || '');
    });
    return kinds;
  }
  function transform(lines) {
    if (typeof lines === 'string') return transform(lines.split('\n')).join('\n');
    if (!Array.isArray(lines) || !lines.length || lines.__kronikolLook) return lines;
    var out;
    if (isSequence(lines)) {
      out = insertAfterHeader(lines, sequenceStyle()).map(function (l) {
        if (levers.services) l = l.replace(/^entity (".*?"|\S+)/, 'participant $1');
        // a step bar ending in a line break draws an empty line under its table (round 17, owner: no gap under it)
        if (/^hnote across <<stepDelimiter>><<stepBody>>:/.test(l)) l = l.replace(/(?:\\n)+(\r?)$/, '$1');
        // a rectangle band in place of the hexagon; its colours come from the stylesheet, by role
        if (levers.bands) l = l.replace(/^hnote across (<<stepDelimiter>> #black:<color:white>)/, 'rnote across $1<b>');
        // a step carrying a data table, a doc string or several lines takes the styled form (<<stepBody>>, black
        // from the report's style): the same rectangle, its label in bold as the one-line bar's is (round 14: it
        // stayed a dark hexagon among the light bands)
        if (levers.bands) l = l.replace(/^hnote across (<<stepDelimiter>><<stepBody>>: )/, 'rnote across $1<b>');
        return l;
      });
      out.__kronikolSequence = true;
      out.__kronikolNoteKinds = noteKinds(out);
    } else if (levers.font && lines.some(function (l) { return /^(left to right|top to bottom) direction/.test(l); })) {
      out = insertAfterHeader(lines, ['skinparam defaultFontName system-ui']);
    } else {
      return lines;
    }
    out.__kronikolLook = true;
    return out;
  }
  function wrap(pu) {
    if (!pu || pu.__kronikolLook) return;
    var render = pu.render, prefetch = pu.prefetch;
    pu.render = function (lines, id, opts) {
      var drawn = transform(lines);
      // the role pass finds sequence diagrams by their container, and reads the note kinds from it
      if (drawn && drawn.__kronikolSequence && id) {
        var host = document.getElementById(id);
        if (host) { host.setAttribute('data-k-sequence', '1'); host._kNoteKinds = drawn.__kronikolNoteKinds; }
      }
      return render.call(this, drawn, id, opts);
    };
    if (prefetch) pu.prefetch = function (sources) { return prefetch.call(this, (sources || []).map(function (s) { return typeof s === 'string' ? transform(s) : s; })); };
    pu.__kronikolLook = true;
  }
  if (window.plantuml) wrap(window.plantuml);
  else {
    var held;
    Object.defineProperty(window, 'plantuml', { configurable: true, enumerable: true,
      get: function () { return held; },
      set: function (v) { held = v; wrap(v); } });
  }

  /* ---------- 2. roles, found by structure ---------- */
  var NS = 'http://www.w3.org/2000/svg';
  function box(e) { try { return e.getBBox(); } catch (x) { return null; } }
  function tag(e) { return e.tagName.toLowerCase(); }
  // painted at all: not none, not transparent (structure, not colour)
  function filled(e) { var f = (e.getAttribute('fill') || '').toLowerCase(); return !!f && f !== 'none' && f !== 'transparent' && !/^#[0-9a-f]{6}00$/.test(f); }
  function addClass(e, c) { e.setAttribute('class', ((e.getAttribute('class') || '') + ' ' + c).trim()); }
  function num(e, a) { return parseFloat(e.getAttribute(a)) || 0; }
  function inside(b, x, y) { return b && b.x <= x && x <= b.x + b.width && b.y <= y && y <= b.y + b.height; }
  // every x coordinate of an element, mapped by fx: a rect's two sides, a line's ends, a polygon's points, a path's
  // absolute coordinates (the engine writes no relative ones; a path with one is moved whole by a transform)
  function fixed(v) { return String(Math.round(v * 100) / 100); }
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
      if (/[mlhvcsqta]/.test(d)) { var dx = fx(0); e.setAttribute('transform', 'translate(' + fixed(dx) + ' 0)'); return; }
      var XS = { M: [0], L: [0], T: [0], H: [0], C: [0, 2, 4], S: [0, 2], Q: [0, 2], A: [5], V: [], Z: [] };
      var SIZE = { M: 2, L: 2, T: 2, H: 1, V: 1, C: 6, S: 4, Q: 4, A: 7, Z: 1 };
      e.setAttribute('d', d.replace(/([MLHVCSQTAZ])([^MLHVCSQTAZ]*)/g, function (all, c, rest) {
        var v = rest.match(/-?(?:\d+\.?\d*|\.\d+)(?:e[-+]?\d+)?/gi);
        if (!v) return c + rest;
        return c + v.map(function (s, k) { return XS[c].indexOf(k % SIZE[c]) >= 0 ? fixed(fx(+s)) : s; }).join(' ') + ' ';
      }).trim());
    }
  }

  // Three phases, so the browser lays the drawing out once: every measurement first (READS), then the decisions,
  // then every change (WRITES). Measuring and changing element by element made the browser lay the whole diagram out
  // again before each measurement: 117ms on a 2,400-element diagram, measured with look-perf.js.
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
    // every drawn element, at any depth (lifelines and participants sit in nested groups; notes do not), leaving
    // out the containers and what the report's note script adds
    var kids = Array.prototype.slice.call(main.querySelectorAll('*')).filter(function (e) {
      var t = tag(e);
      if (t === 'g' || t === 'a' || t === 'title' || t === 'tspan' || t === 'defs') return false;
      return !(e.closest('.note-toggle-icon') || (e.classList && e.classList.contains('note-hover-rect')));
    });
    var vb = svg.viewBox && svg.viewBox.baseVal;
    var height = vb && vb.height ? vb.height : (box(svg) || { height: 0 }).height;
    // lifelines from their attributes: the x positions whose vertical lines cover more than a third of the diagram
    var vertical = kids.filter(function (e) { return tag(e) === 'line' && Math.abs(num(e, 'x1') - num(e, 'x2')) < 0.5 && Math.abs(num(e, 'y2') - num(e, 'y1')) > 4; });
    var cover = {};
    vertical.forEach(function (e) { var x = Math.round(num(e, 'x1')); cover[x] = (cover[x] || 0) + Math.abs(num(e, 'y2') - num(e, 'y1')); });
    var lifeXs = Object.keys(cover).filter(function (x) { return cover[x] > height / 3; }).map(Number);
    var lifelines = vertical.filter(function (e) { return lifeXs.indexOf(Math.round(num(e, 'x1'))) >= 0; });
    if (!lifelines.length) { svg.setAttribute('data-k-roles', 'none'); return true; }
    var top = Infinity, bottom = -Infinity;
    lifelines.forEach(function (e) { top = Math.min(top, num(e, 'y1'), num(e, 'y2')); bottom = Math.max(bottom, num(e, 'y1'), num(e, 'y2')); });
    var minX = Math.min.apply(null, lifeXs), maxX = Math.max.apply(null, lifeXs);
    // the shapes' boxes, measured once
    var shapes = [];
    kids.forEach(function (e) {
      var t = tag(e);
      if (t === 'text') return;
      var b = box(e); if (b) shapes.push({ e: e, t: t, b: b, filled: filled(e) });
    });
    var texts = kids.filter(function (e) { return tag(e) === 'text'; });
    // message numbers' candidates: bold runs of digits; their own box, and the boxes of the next texts on the line
    var numberCandidates = [];
    if (levers.badges) texts.forEach(function (t, ti) {
      var w = t.getAttribute('font-weight');
      if ((w !== 'bold' && w !== '700') || !/^[0-9]{1,4}$/.test(t.textContent.trim())) return;
      var b = box(t); if (!b || !b.width) return;
      var y = num(t, 'y'), label = null;
      for (var j = ti + 1; j < texts.length && j < ti + 6; j++) {
        if (Math.abs(num(texts[j], 'y') - y) > 1) break;
        if (texts[j].textContent.trim()) { label = texts[j]; break; }
      }
      numberCandidates.push({ t: t, b: b, labelX: label ? (box(label) || { x: null }).x : null });
    });

    /* DECISIONS (no DOM access beyond attributes) */
    var counts = { lifelines: 0, participants: 0, bands: 0, cards: 0, numbers: 0 };
    var participants = !levers.geometry ? [] : shapes.filter(function (o) {
      var b = o.b;
      if (!(b.y + b.height <= top + 2 || b.y >= bottom - 2)) return false;
      return lifeXs.some(function (x) { return x >= b.x - 2 && x <= b.x + b.width + 2; });
    });
    // everything filled that spans the lifelines; a band is one line high
    var spans = lifeXs.length < 2 ? [] : shapes.filter(function (o) {
      var b = o.b;
      return (o.t === 'rect' || o.t === 'path' || o.t === 'polygon') && o.filled && b.x <= minX + 1 && b.x + b.width >= maxX - 1 && b.y > top - 2 && b.y + b.height < bottom + 2;
    });
    var bands = spans.filter(function (o) { return o.b.height < 60; });
    // a text belongs to a band when its baseline lies within the band (its y attribute: no measuring)
    function within(list, x, y) { return list.some(function (o) { return o.b.x <= x && x <= o.b.x + o.b.width && o.b.y <= y && y <= o.b.y + o.b.height; }); }
    // the step bars: the lever draws every step as a rectangle (rnote) in the source's black, of any height (a step
    // carrying a data table or a doc string is tall: round 14). An assertion (a green or red hexagon), the Test bar
    // and a render error span the lifelines too, and keep their own look (round 12: the assertions had taken the
    // steps' colour)
    var stepBands = spans.filter(function (o) { return o.t !== 'polygon' && /^(#000000|#000|black)$/i.test((o.e.getAttribute('fill') || '').trim()); });
    function inBand(x, y) { return within(bands, x, y) || within(stepBands, x, y); }
    var bandTexts = !levers.bands ? [] : texts.filter(function (t) { return within(stepBands, num(t, 'x'), num(t, 'y') - 3); });
    // the rules of a table drawn in a step bar (the report's style draws them white, for the dark bar)
    var boxIn = function (o, s) { return o.b.x >= s.b.x - 1 && o.b.x + o.b.width <= s.b.x + s.b.width + 1 && o.b.y >= s.b.y - 1 && o.b.y + o.b.height <= s.b.y + s.b.height + 1; };
    var stepRules = !levers.bands || !stepBands.length ? [] : shapes.filter(function (o) {
      return stepBands.indexOf(o) < 0 && !o.filled && /^(line|path|polyline|rect)$/.test(o.t) && stepBands.some(function (s) { return boxIn(o, s); });
    });
    // a table drawn in a step bar: the box its rules enclose, which gets a background of its own (round 16)
    var stepTables = [];
    if (stepRules.length) stepBands.forEach(function (s) {
      var x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
      stepRules.forEach(function (o) {
        if (!boxIn(o, s)) return;
        x0 = Math.min(x0, o.b.x); y0 = Math.min(y0, o.b.y); x1 = Math.max(x1, o.b.x + o.b.width); y1 = Math.max(y1, o.b.y + o.b.height);
      });
      // the header row: down to the second horizontal rule (round 17, owner: shaded as the parameter table's header)
      var ys = [];
      stepRules.forEach(function (o) {
        if (!boxIn(o, s) || o.b.height > 0.6 || o.b.width < 4) return;
        var y = Math.round(o.b.y * 10) / 10;
        if (ys.indexOf(y) < 0) ys.push(y);
      });
      ys.sort(function (a, c) { return a - c; });
      if (x1 > x0 && y1 > y0) stepTables.push({ s: s, x: x0, y: y0, w: x1 - x0, h: y1 - y0, headH: ys.length > 2 ? ys[1] - y0 : 0 });
    });
    // Every strip runs the drawing's full width (round 15). The engine draws a strip across the participants, or as
    // wide as its own text where that is wider, centred on the participants: with two participants each strip took
    // its own text's width, and in every diagram the strips stopped short of the notes beside the outer participants.
    // The step bands and the hexagons (an assertion, the Test bar, a Row marker) are stretched to the drawing's left
    // and right edges, and what is drawn in them is re-centred, as the engine centres it. A frame around a strip (the
    // Setup partition, a loop) or anything else drawn in its rows stops it short; a strip never narrows.
    var stretches = [], stretchStarted = performance.now();
    if (levers.strips && spans.length) {
      // every drawn element once, as a box: a shape's measured box, a text's from its attributes (x, the baseline,
      // the engine's textLength: nothing is measured). The lifelines cross every strip and are left out.
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
        var s = spans[si];
        if (s.t !== 'polygon' && stepBands.indexOf(s) < 0) continue;
        var b = s.b, x0 = b.x, x1 = b.x + b.width, y0 = b.y, y1 = b.y + b.height, L = left, R = right, inner = [];
        for (k = 0; k < items.length; k++) {
          it = items[k];
          if (it.o === s) continue;
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
        stretches.push({ s: s, x0: x0, x1: x1, L: L, R: R, dx: (L + R) / 2 - (x0 + x1) / 2, inner: inner });
      }
      counts.stretchDecideMs = +(performance.now() - stretchStarted).toFixed(3);
    }
    // notes: a filled path followed by its fold, a small filled path at one of its corners (as the note script does)
    var notes = [];
    if (levers.cards) {
      var byEl = new Map(shapes.map(function (o) { return [o.e, o]; })), used = new Set();
      shapes.forEach(function (o) {
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
    }
    // The kinds come from the source this picture was drawn from. A re-render draws into a temporary child and moves
    // the picture into the container, so the kinds recorded at render time can be an earlier source's: with Databases
    // hidden they were the whole diagram's, longer than the notes drawn, and the event cards lost their colour (round 17).
    var kinds = null;
    var drawnSource = host.getAttribute('data-plantuml');
    if (drawnSource) {
      var fromSource = noteKinds(drawnSource.split('\n'));
      if (fromSource.length === notes.length) kinds = fromSource;
    }
    if (!kinds && host._kNoteKinds && host._kNoteKinds.length === notes.length) kinds = host._kNoteKinds;
    // message numbers: on a message arrow (a horizontal line just below), outside the bands
    var arrows = shapes.filter(function (o) { return o.t === 'line' && Math.abs(num(o.e, 'y1') - num(o.e, 'y2')) < 0.5 && o.b.width > 8; });
    var numbers = numberCandidates.filter(function (c) {
      var cx = c.b.x + c.b.width / 2, base = c.b.y + c.b.height;
      if (inBand(cx, c.b.y + c.b.height / 2)) return false;
      return arrows.some(function (a) { var ay = a.b.y; return ay >= base - 2 && ay <= base + 16 && cx >= a.b.x - 4 && cx <= a.b.x + a.b.width + 4; });
    });

    /* WRITES */
    if (levers.geometry) {
      lifelines.forEach(function (e) { addClass(e, 'k-lifeline'); counts.lifelines++; });
      participants.forEach(function (o) { addClass(o.e, o.filled ? 'k-participant k-participant-fill' : 'k-participant'); counts.participants++; });
    }
    if (levers.bands) {
      stepBands.forEach(function (o) { addClass(o.e, 'k-step'); counts.bands++; });
      bandTexts.forEach(function (t) { addClass(t, 'k-step-text'); });
      stepRules.forEach(function (o) { addClass(o.e, 'k-step-rule'); });
    }
    var writeStarted = performance.now();
    stretches.forEach(function (st) {
      var e = st.s.e, cx = (st.x0 + st.x1) / 2;
      // the strip: its left side to L, its right side to R
      moveX(e, function (x) { return x < cx ? x + (st.L - st.x0) : x + (st.R - st.x1); });
      // what is drawn in it: moved as one, to the strip's new centre
      var shift = function (x) { return x + st.dx; };
      for (var i = 0; i < st.inner.length; i++) moveX(st.inner[i], shift);
      counts.stretched = (counts.stretched || 0) + 1;
      counts.moved = (counts.moved || 0) + st.inner.length;
    });
    if (stretches.length) counts.stretchWriteMs = +(performance.now() - writeStarted).toFixed(3);
    // a table in a step bar is drawn on a background of its own, behind its rules and text, so it reads as a table and
    // not as more of the bar (round 16, owner: white). It goes where the stretch moved the table.
    if (stepTables.length) {
      var dxOf = new Map(stretches.map(function (st) { return [st.s.e, st.dx]; }));
      stepTables.forEach(function (tb) {
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
          bandTexts.forEach(function (t) {
            var ty = num(t, 'y');
            if (ty > tb.y && ty <= tb.y + tb.headH + 1) addClass(t, 'k-step-table-head-text');
          });
        }
        counts.tables = (counts.tables || 0) + 1;
        // Lift the bar's contents 1.5 units (round 17, owner: the space under the table as the space above the title; the
        // diagram is drawn scaled, so 1 unit could snap back to the same pixel row): 7px above the title, 6px under the table.
        // A transform moves what is painted only; nothing reads these elements' geometry after this pass.
        var sb = tb.s.b;
        var lift = function (el) { if (!el.getAttribute('transform')) { el.setAttribute('transform', 'translate(0,-1.5)'); addClass(el, 'k-step-lift'); } };
        bandTexts.forEach(function (t) { var ty = num(t, 'y'); if (ty > sb.y && ty < sb.y + sb.height + 1) lift(t); });
        stepRules.forEach(function (o) { if (boxIn(o, tb.s)) lift(o.e); });
        lift(r);
        if (tb.headH > 0) lift(r.nextSibling);
      });
    }
    notes.forEach(function (n, idx) {
      var b = n.b, r = Math.min(5, b.width / 4, b.height / 4), x = b.x, y = b.y, w = b.width, h = b.height;
      var f = function (v) { return v.toFixed(2); };
      // the same bounds, so the note script's geometry (bounding box, fold) still holds
      n.body.setAttribute('d', ['M', f(x + r), f(y), 'H', f(x + w - r), 'A', r, r, 0, 0, 1, f(x + w), f(y + r),
        'V', f(y + h - r), 'A', r, r, 0, 0, 1, f(x + w - r), f(y + h), 'H', f(x + r), 'A', r, r, 0, 0, 1, f(x), f(y + h - r),
        'V', f(y + r), 'A', r, r, 0, 0, 1, f(x + r), f(y), 'Z'].join(' '));
      addClass(n.body, kinds && kinds[idx] ? 'k-card k-card-' + kinds[idx].replace(/Note$/, '').toLowerCase() : 'k-card');
      addClass(n.fold, 'k-fold');
      counts.cards++;
    });
    numbers.forEach(function (c) {
      var H = 17, PAD = 5, GAP = 4, wd = Math.max(H, c.b.width + 2 * PAD), cx = c.b.x + c.b.width / 2;
      var right = c.labelX !== null ? c.labelX - GAP : cx + wd / 2, left = right - wd;
      var shift = (left + wd / 2) - cx;
      if (shift) c.t.setAttribute('x', (num(c.t, 'x') + shift).toFixed(2));
      var pill = document.createElementNS(NS, 'rect');
      pill.setAttribute('x', left.toFixed(2));
      pill.setAttribute('y', (c.b.y + c.b.height / 2 - H / 2).toFixed(2));
      pill.setAttribute('width', wd.toFixed(2));
      pill.setAttribute('height', H);
      pill.setAttribute('rx', H / 2);
      pill.setAttribute('class', 'k-badge');
      c.t.parentNode.insertBefore(pill, c.t);
      // a class, never the fill attribute: the internal-flow link binder copies the first text's fill on a
      // message line onto its link labels
      addClass(c.t, 'k-number');
      counts.numbers++;
    });
    counts.ms = +(performance.now() - started).toFixed(1);
    svg.setAttribute('data-k-roles', JSON.stringify(counts));
    return true;
  }

  if (window.MutationObserver && (levers.geometry || levers.cards || levers.bands || levers.strips || levers.badges)) {
    var waiting = window.IntersectionObserver ? new IntersectionObserver(function (entries) {
      entries.forEach(function (e) { if (e.isIntersecting && tagRoles(e.target)) waiting.unobserve(e.target); });
    }) : null;
    var visit = function (root) {
      var svgs = tag(root) === 'svg' ? [root] : (root.querySelectorAll ? root.querySelectorAll('svg') : []);
      Array.prototype.forEach.call(svgs, function (svg) {
        if (!svg.closest || !svg.closest('[data-k-sequence]')) return;
        if (!tagRoles(svg) && waiting) waiting.observe(svg);
      });
    };
    new MutationObserver(function (records) {
      records.forEach(function (r) { Array.prototype.forEach.call(r.addedNodes, function (node) { if (node.nodeType === 1) visit(node); }); });
    }).observe(document.documentElement, { childList: true, subtree: true });
  }
  window.__kronikolDiagramLook = { transform: transform, levers: levers, tagRoles: tagRoles };
})();
