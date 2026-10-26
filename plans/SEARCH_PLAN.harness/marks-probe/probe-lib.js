// Page-side helpers for the highlight probe. Loaded into markup.html with page.addScriptTag.
(function () {
  'use strict';
  var NS = 'http://www.w3.org/2000/svg';
  var P = {};
  P.roots = { d1: '#d1', d2: '#d2' };
  P.raf2 = function () { return new Promise(function (r) { requestAnimationFrame(function () { requestAnimationFrame(r); }); }); };

  function textNodeOf(el) { for (var i = 0; i < el.childNodes.length; i++) if (el.childNodes[i].nodeType === 3) return el.childNodes[i]; return null; }
  function rect(r) { return { x: r.left, y: r.top, w: r.width, h: r.height }; }
  function union(rs) {
    rs = rs.filter(function (r) { return r && (r.w > 0 || r.h > 0); });
    if (!rs.length) return null;
    var x1 = Math.min.apply(null, rs.map(function (r) { return r.x; }));
    var y1 = Math.min.apply(null, rs.map(function (r) { return r.y; }));
    var x2 = Math.max.apply(null, rs.map(function (r) { return r.x + r.w; }));
    var y2 = Math.max.apply(null, rs.map(function (r) { return r.y + r.h; }));
    return { x: x1, y: y1, w: x2 - x1, h: y2 - y1 };
  }
  P.union = union;
  function mapRect(el, ur) {
    var m = el.getScreenCTM();
    var pts = [[ur.x, ur.y], [ur.x + ur.w, ur.y], [ur.x, ur.y + ur.h], [ur.x + ur.w, ur.y + ur.h]].map(function (p) { return new DOMPoint(p[0], p[1]).matrixTransform(m); });
    var xs = pts.map(function (p) { return p.x; }), ys = pts.map(function (p) { return p.y; });
    return { x: Math.min.apply(null, xs), y: Math.min.apply(null, ys), w: Math.max.apply(null, xs) - Math.min.apply(null, xs), h: Math.max.apply(null, ys) - Math.min.apply(null, ys) };
  }
  P.mapRect = mapRect;
  function extentUser(el, s, e) {
    var rs = [];
    for (var i = s; i < e; i++) { var r = el.getExtentOfChar(i); rs.push({ x: r.x, y: r.y, w: r.width, h: r.height }); }
    return union(rs);
  }
  function substrUser(el, s, e) {
    var p = el.getStartPositionOfChar(s);
    var w = el.getSubStringLength(s, e - s);
    var ex = el.getExtentOfChar(s);
    return { x: p.x, y: ex.y, w: w, h: ex.height };
  }
  P.extentUser = extentUser; P.substrUser = substrUser;

  function findText(sel, pred, needle) {
    var all = document.querySelectorAll(sel + ' text');
    for (var i = 0; i < all.length; i++) {
      var t = all[i];
      if (pred(t)) { var n = textNodeOf(t); var s = n.data.indexOf(needle); return { kind: 'svg', el: t, node: n, s: s, e: s + needle.length, svg: t.ownerSVGElement }; }
    }
    return null;
  }

  P.resolve = function (id) {
    var t = null;
    if (id === 'ctl') { var p = document.getElementById('ctl'); var n = textNodeOf(p); var s = n.data.indexOf('Fresh'); t = { kind: 'html', el: p, node: n, s: s, e: s + 5, svg: null }; }
    else if (/^s\d$/.test(id)) { var el = document.getElementById(id); var n2 = textNodeOf(el); var s2 = n2.data.indexOf('Fresh'); t = { kind: 'svg', el: el, node: n2, s: s2, e: s2 + 5, svg: el.ownerSVGElement }; }
    else if (id === 'm-json') t = findText(P.roots.d1 + ' svg', function (x) { return x.textContent === '"Some_Fresh_Milk"'; }, 'Fresh');
    else if (id === 'm-part') t = findText(P.roots.d1 + ' svg', function (x) { return x.textContent === 'Breakfast Provider'; }, 'Provider');
    else if (id === 'm-step') t = findText(P.roots.d1 + ' svg', function (x) { return x.textContent === 'cinnamon' && x.classList.contains('k-step-text'); }, 'namon');
    else if (id === 'm-link') t = findText(P.roots.d1 + ' svg', function (x) { return !!x.closest('a') && x.textContent === '/milk'; }, 'milk');
    else if (id === 'o-zwsp') t = findText(P.roots.d2 + ' svg', function (x) { return x.textContent.indexOf('chars):') === 0; }, 'chars):');
    else if (id === 'o-box' || id === 'o-deep') {
      var div = document.querySelector(P.roots.d2 + ' .note-scroll');
      // the text node holding the needle (the box is one text node in the captured markup; be general on the live page)
      var needle = id === 'o-box' ? 'BreakfastProvider.Api' : '"$ref"', n3 = null, s3 = -1;
      var tw = document.createTreeWalker(div, NodeFilter.SHOW_TEXT), cand, total = div.textContent.length, seen = 0;
      while ((cand = tw.nextNode())) {
        var from = id === 'o-box' ? 0 : Math.max(0, Math.floor(total * 0.55) - seen);
        var k = cand.data.indexOf(needle, from);
        if (k >= 0) { n3 = cand; s3 = k; break; }
        seen += cand.data.length;
      }
      if (!n3) return null;
      t = { kind: 'box', el: div, node: n3, s: s3, e: s3 + (id === 'o-box' ? 21 : 6), svg: div.closest('svg'), fo: div.closest('foreignObject') };
    }
    else if (id === 'm-cross') {
      // '"milk": "Some' across three <text> siblings: '"milk":', ' ', '"Some_Fresh_Milk"'
      var all = document.querySelectorAll(P.roots.d1 + ' svg text');
      for (var i = 0; i < all.length - 2; i++) {
        if (all[i].textContent === '"milk":' && all[i + 1].textContent === ' ' && all[i + 2].textContent === '"Some_Fresh_Milk"') {
          t = { kind: 'cross', el: all[i], el2: all[i + 2], els: [all[i], all[i + 1], all[i + 2]], node: textNodeOf(all[i]), node2: textNodeOf(all[i + 2]), s: 0, e: 5, svg: all[i].ownerSVGElement, domAdjacent: all[i].nextSibling === all[i + 1] && all[i + 1].nextSibling === all[i + 2] };
          break;
        }
      }
    }
    if (t) t.id = id;
    return t;
  };

  P.range = function (t) {
    var r = document.createRange();
    if (t.kind === 'cross') { r.setStart(t.node, 0); r.setEnd(t.node2, 5); }
    else { r.setStart(t.node, t.s); r.setEnd(t.node, t.e); }
    return r;
  };

  P.measure = function (t) {
    var out = { id: t.id, kind: t.kind };
    var rg = P.range(t);
    var list = Array.prototype.map.call(rg.getClientRects(), rect);
    out.R = { n: list.length, list: list, bb: rect(rg.getBoundingClientRect()), u: union(list) };
    if (t.kind === 'svg') {
      try { var ux = extentUser(t.el, t.s, t.e); out.Xu = ux; out.X = mapRect(t.el, ux); } catch (e) { out.Xerr = String(e); }
      try { var us = substrUser(t.el, t.s, t.e); out.Su = us; out.S = mapRect(t.el, us); } catch (e) { out.Serr = String(e); }
      try { var b = t.el.getBBox(); out.bboxUser = { x: b.x, y: b.y, w: b.width, h: b.height }; } catch (e) { }
      var m = t.el.getScreenCTM(); out.ctm = [m.a, m.b, m.c, m.d, m.e, m.f];
      out.numChars = t.el.getNumberOfChars(); out.compLen = t.el.getComputedTextLength();
      out.perChar = [];
      for (var i = 0; i < t.node.data.length; i++) { var ex = t.el.getExtentOfChar(i); out.perChar.push([t.node.data.charCodeAt(i), +ex.x.toFixed(2), +ex.width.toFixed(2)]); }
    }
    if (t.kind === 'cross') {
      out.parts = t.els.map(function (el) { return rect(el.getBoundingClientRect()); });
      out.domAdjacent = t.domAdjacent;
      try {
        var a = mapRect(t.el, extentUser(t.el, 0, t.node.data.length));
        var c = mapRect(t.el2, extentUser(t.el2, 0, 5));
        out.X = union([a, c]);
      } catch (e) { out.Xerr = String(e); }
    }
    out.elBox = rect(t.el.getBoundingClientRect());
    return out;
  };

  // Paint only the matched characters red (the ink truth), everything else on the page hidden (so no other text's
  // subpixel fringes read as red). Returns a restore function.
  P.inkPrep = function (t, mode) {
    var st = document.createElement('style');
    // hide only text (a hidden svg or positioned box can make Chromium skip a visible descendant's layer entirely)
    st.textContent = 'svg text, svg tspan, .label, p, .note-scroll { visibility: hidden !important; } .probe-ink-on.probe-ink-on, .probe-ink-on.probe-ink-on * { visibility: visible !important; }';
    document.head.appendChild(st);
    var on = t.kind === 'cross' ? t.els : [t.el];
    on.forEach(function (e) { e.classList.add('probe-ink-on'); });
    var undo = P._inkPrep(t, mode);
    return function () { undo(); on.forEach(function (e) { e.classList.remove('probe-ink-on'); }); st.remove(); };
  };
  P._inkPrep = function (t, mode) {
    if (t.kind === 'svg') {
      var el = t.el, txt = t.node.data;
      if (mode === 'whole-style') { var prev = el.getAttribute('style'); el.style.fill = '#ff0000'; return function () { if (prev == null) el.removeAttribute('style'); else el.setAttribute('style', prev); }; }
      var allRed = mode === 'whole-tspan';
      var mk = function (str, red) { var s = document.createElementNS(NS, 'tspan'); if (red) s.setAttribute('fill', '#ff0000'); else { s.setAttribute('fill', '#000000'); s.setAttribute('fill-opacity', '0'); } s.textContent = str; return s; };
      el.removeChild(t.node);
      var parts = [];
      if (t.s > 0) parts.push(mk(txt.slice(0, t.s), allRed));
      parts.push(mk(txt.slice(t.s, t.e), true));
      if (t.e < txt.length) parts.push(mk(txt.slice(t.e), allRed));
      parts.forEach(function (p) { el.appendChild(p); });
      return function () { parts.forEach(function (p) { p.remove(); }); el.appendChild(t.node); };
    }
    if (t.kind === 'cross') {
      // the three elements: first whole red, middle (space) nothing visible, third: first 5 chars red
      var saved = t.els.map(function (e) { return e.getAttribute('style'); });
      t.els[0].style.fill = '#ff0000';
      var e3 = t.el2, n3 = t.node2, txt3 = n3.data;
      var mk3 = function (str, red) { var s = document.createElementNS(NS, 'tspan'); if (red) s.setAttribute('fill', '#ff0000'); else { s.setAttribute('fill', '#000000'); s.setAttribute('fill-opacity', '0'); } s.textContent = str; return s; };
      e3.removeChild(n3);
      var p1 = mk3(txt3.slice(0, 5), true), p2 = mk3(txt3.slice(5), false);
      e3.appendChild(p1); e3.appendChild(p2);
      return function () { p1.remove(); p2.remove(); e3.appendChild(n3); t.els.forEach(function (e, i) { if (saved[i] == null) e.removeAttribute('style'); else e.setAttribute('style', saved[i]); }); };
    }
    // html paragraph or the box's div: one text node
    var host = t.el, txtH = t.node.data, prevColor = host.style.color, st = host.scrollTop, sl = host.scrollLeft;
    var span = document.createElement('span'); span.style.color = '#ff0000'; span.textContent = txtH.slice(t.s, t.e);
    var pre = document.createTextNode(txtH.slice(0, t.s)), post = document.createTextNode(txtH.slice(t.e));
    host.style.color = 'transparent';
    host.replaceChild(post, t.node); host.insertBefore(span, post); host.insertBefore(pre, span);
    host.scrollTop = st; host.scrollLeft = sl;
    return function () { host.style.color = prevColor; pre.remove(); span.remove(); host.replaceChild(t.node, post); host.scrollTop = st; host.scrollLeft = sl; };
  };

  P.reset = function () {
    document.querySelectorAll('.plantuml-browser > svg').forEach(function (s) {
      s.style.transform = ''; s.style.transformOrigin = ''; s.style.width = ''; s.style.maxWidth = '';
      if (s.dataset.vb0) { s.setAttribute('viewBox', s.dataset.vb0); delete s.dataset.vb0; }
      s.parentElement.style.overflowX = ''; s.parentElement.scrollLeft = 0; s.parentElement.scrollTop = 0;
    });
    var st = document.querySelector('.stage'); if (st) { st.style.transform = ''; st.style.transformOrigin = ''; }
    var div = document.querySelector(P.roots.d2 + ' .note-scroll'); if (div) div.scrollTop = 0;
  };

  P.centerUser = function (t) {
    if (t.kind === 'box') { var fo = t.fo; return { x: fo.x.baseVal.value + 160, y: fo.y.baseVal.value + 60 }; }
    var b = t.el.getBBox(); return { x: b.x + b.width / 2, y: b.y + b.height / 2 };
  };

  P.zoom = function (state, t) {
    P.reset();
    var svg = t.svg; if (!svg && state !== 'Zanc' && state !== 'Z0') return false;
    var nat = svg ? parseFloat(svg.getAttribute('width')) : 0;
    if (state === 'Zin') { svg.style.maxWidth = 'none'; svg.style.width = (nat * 1.6) + 'px'; svg.parentElement.style.overflowX = 'auto'; }
    else if (state === 'Zout') { svg.style.maxWidth = '100%'; svg.style.width = (nat * 0.5) + 'px'; }
    else if (state === 'Ztf') { svg.style.transformOrigin = '0 0'; svg.style.transform = 'scale(1.5)'; }
    else if (state === 'Zvb') {
      var vb = svg.getAttribute('viewBox'); svg.dataset.vb0 = vb;
      var a = vb.split(/[\s,]+/).map(Number), c = P.centerUser(t);
      svg.setAttribute('viewBox', (c.x - a[2] / 4) + ' ' + (c.y - a[3] / 4) + ' ' + (a[2] / 2) + ' ' + (a[3] / 2));
    }
    else if (state === 'Zanc') { var st = document.querySelector('.stage'); st.style.transformOrigin = '0 0'; st.style.transform = 'scale(0.75)'; }
    return true;
  };

  // Bring a target into view (scrolling the window and the diagram's container).
  P.show = function (t) {
    if (t.kind === 'box') {
      t.fo.scrollIntoView({ block: 'start', inline: 'start' });
      window.scrollBy(0, -120);
      var c = t.svg.parentElement; c.scrollLeft = Math.max(0, c.scrollLeft - 60);
    } else {
      t.el.scrollIntoView({ block: 'center', inline: 'center' });
    }
  };

  // --- overlays (technique e) ---
  P.placeOverlays = function (t) {
    P.clearOverlays();
    var res = {};
    var svg = t.svg;
    var rg = P.range(t);
    var rr = union(Array.prototype.map.call(rg.getClientRects(), rect)) || rect(rg.getBoundingClientRect());
    // 1. a <rect> in the SVG, in user space
    var ur;
    if (t.kind === 'svg') { ur = extentUser(t.el, t.s, t.e); }
    else {
      var inv = svg.getScreenCTM().inverse();
      var p1 = new DOMPoint(rr.x, rr.y).matrixTransform(inv), p2 = new DOMPoint(rr.x + rr.w, rr.y + rr.h).matrixTransform(inv);
      ur = { x: Math.min(p1.x, p2.x), y: Math.min(p1.y, p2.y), w: Math.abs(p2.x - p1.x), h: Math.abs(p2.y - p1.y) };
    }
    var r = document.createElementNS(NS, 'rect');
    r.setAttribute('x', ur.x); r.setAttribute('y', ur.y); r.setAttribute('width', ur.w); r.setAttribute('height', ur.h);
    r.setAttribute('fill', '#ff00ff'); r.setAttribute('fill-opacity', '0.35'); r.setAttribute('class', 'probe-ov');
    if (t.kind === 'svg') t.el.parentNode.insertBefore(r, t.el);
    else t.fo.parentNode.insertBefore(r, t.fo); // behind the box: its div has no background, so the rect shows through
    res.userRect = ur;
    // 2. an absolutely positioned HTML overlay in the diagram's container
    var cont = svg.parentElement; cont.style.position = 'relative';
    var cr = cont.getBoundingClientRect();
    var d = document.createElement('div'); d.className = 'probe-ov';
    d.style.cssText = 'position:absolute;pointer-events:none;outline:2px solid #00c8c8;outline-offset:0;box-sizing:border-box;' +
      'left:' + (rr.x - cr.left - cont.clientLeft + cont.scrollLeft) + 'px;top:' + (rr.y - cr.top - cont.clientTop + cont.scrollTop) + 'px;width:' + rr.w + 'px;height:' + rr.h + 'px';
    cont.appendChild(d);
    res.htmlRect = rr;
    P._ov = { rect: r, div: d };
    return res;
  };
  P.overlayRects = function () { if (!P._ov) return null; return { svgRect: rect(P._ov.rect.getBoundingClientRect()), html: rect(P._ov.div.getBoundingClientRect()) }; };
  P.overlayVisible = function (v) { document.querySelectorAll('.probe-ov').forEach(function (e) { e.style.visibility = v ? '' : 'hidden'; }); };
  P.clearOverlays = function () { document.querySelectorAll('.probe-ov').forEach(function (e) { e.remove(); }); P._ov = null; };

  // fixed-position outlines at client rects, for the curated screenshots of technique b/c
  P.drawOutlines = function (rects) {
    P.clearOutlines();
    rects.forEach(function (o) {
      if (!o.r) return;
      var d = document.createElement('div'); d.className = 'probe-outline';
      d.style.cssText = 'position:fixed;pointer-events:none;z-index:99999;box-sizing:border-box;border:1px ' + (o.dash ? 'dashed ' : 'solid ') + o.color +
        ';left:' + o.r.x + 'px;top:' + o.r.y + 'px;width:' + o.r.w + 'px;height:' + o.r.h + 'px';
      document.body.appendChild(d);
    });
  };
  P.clearOutlines = function () { document.querySelectorAll('.probe-outline').forEach(function (e) { e.remove(); }); };

  // --- custom highlight API (technique a) ---
  P.hlSupported = function () { return typeof CSS !== 'undefined' && !!CSS.highlights && typeof Highlight === 'function'; };
  P.setHL = function (name, css, ranges) {
    var st = document.getElementById('probe-hl-style');
    if (!st) { st = document.createElement('style'); st.id = 'probe-hl-style'; document.head.appendChild(st); }
    st.textContent = css ? '::highlight(' + name + ') { ' + css + ' }' : '';
    if (!P.hlSupported()) return false;
    CSS.highlights.clear();
    if (ranges && ranges.length) { var h = new Highlight(); ranges.forEach(function (r) { h.add(r); }); CSS.highlights.set(name, h); }
    return true;
  };

  // --- deep match in the box: scrolling (technique d) ---
  P.boxScroll = function (t, method) {
    var div = t.el; div.scrollTop = 0; window.scrollTo(0, 0);
    var res = { method: method };
    var t0 = performance.now();
    if (method === 'mark') {
      var txt = t.node.data, mark = document.createElement('mark');
      var pre = document.createTextNode(txt.slice(0, t.s)), post = document.createTextNode(txt.slice(t.e));
      mark.textContent = txt.slice(t.s, t.e);
      div.replaceChild(post, t.node); div.insertBefore(mark, post); div.insertBefore(pre, mark);
      mark.scrollIntoView({ block: 'center', inline: 'nearest' });
      res.ms = performance.now() - t0;
      var mr = rect(mark.getBoundingClientRect());
      res.target = mr;
      P._undoMark = function () { pre.remove(); mark.remove(); div.replaceChild(t.node, post); };
    } else {
      var rg = P.range(t);
      var rr = union(Array.prototype.map.call(rg.getClientRects(), rect)) || rect(rg.getBoundingClientRect());
      var dr = div.getBoundingClientRect();
      var scale = method === 'range-naive' ? 1 : dr.height / div.clientHeight;
      div.scrollTop = div.scrollTop + (rr.y + rr.h / 2 - (dr.top + dr.height / 2)) / scale;
      // then the window: put the box's middle in the middle of the window
      var dr2 = div.getBoundingClientRect();
      window.scrollBy(0, dr2.top + dr2.height / 2 - innerHeight / 2);
      res.ms = performance.now() - t0;
      res.target = union(Array.prototype.map.call(P.range(t).getClientRects(), rect));
      P._undoMark = null;
    }
    var d = rect(div.getBoundingClientRect());
    res.box = d; res.scrollTop = div.scrollTop; res.scrollHeight = div.scrollHeight; res.clientHeight = div.clientHeight;
    var tg = res.target;
    res.inBox = !!tg && tg.y >= d.y - 0.5 && tg.y + tg.h <= d.y + d.h + 0.5;
    res.inWindow = !!tg && tg.y >= 0 && tg.y + tg.h <= innerHeight;
    res.offCenterPx = tg ? (tg.y + tg.h / 2) - (d.y + d.h / 2) : null;
    return res;
  };
  P.undoMark = function () { if (P._undoMark) P._undoMark(); P._undoMark = null; };

  // --- timing (technique f) ---
  P.timing = async function (pattern, label) {
    var svg = document.querySelector(P.roots.d1 + ' svg');
    svg.scrollIntoView({ block: 'start' });
    var texts = Array.prototype.slice.call(svg.querySelectorAll('text'));
    var re = new RegExp(pattern, 'g');
    var med = function (a) { var b = a.slice().sort(function (x, y) { return x - y; }); return +b[Math.floor(b.length / 2)].toFixed(3); };
    var res = { label: label, texts: texts.length };
    var matches, a = [];
    for (var k = 0; k < 7; k++) {
      var t0 = performance.now(); matches = [];
      for (var i = 0; i < texts.length; i++) { var s = texts[i].textContent; re.lastIndex = 0; var m; while ((m = re.exec(s))) matches.push({ el: texts[i], node: textNodeOf(texts[i]), s: m.index, e: m.index + m[0].length }); }
      a.push(performance.now() - t0);
    }
    res.matches = matches.length; res.search = med(a);
    var R = function () { return matches.map(function (m) { var r = document.createRange(); r.setStart(m.node, m.s); r.setEnd(m.node, m.e); return r.getClientRects(); }); };
    var X = function () { return matches.map(function (m) { var c = m.el.getScreenCTM(); var a1 = m.el.getExtentOfChar(m.s), b1 = m.el.getExtentOfChar(m.e - 1); return [a1.x, a1.y, b1.x + b1.width - a1.x, a1.height, c.a]; }); };
    var S = function () { return matches.map(function (m) { var p = m.el.getStartPositionOfChar(m.s); return [p.x, m.el.getSubStringLength(m.s, m.e - m.s)]; }); };
    var base = svg.getBoundingClientRect().width;
    // totals over many runs, divided: Firefox and WebKit clamp performance.now() to 1 ms here
    var timeIt = function (fn, cold) {
      var K = cold ? 20 : 100, t1 = performance.now();
      for (var k2 = 0; k2 < K; k2++) {
        if (cold) svg.style.width = (base - 1 - (k2 % 2)) + 'px';
        fn();
      }
      var per = (performance.now() - t1) / K;
      svg.style.width = ''; svg.getBoundingClientRect();
      return +per.toFixed(3);
    };
    svg.getBoundingClientRect();
    res.layoutOnly = timeIt(function () { svg.getBoundingClientRect(); texts[0].getBBox(); }, true);
    res.R_warm = timeIt(R, false); res.R_cold = timeIt(R, true);
    res.X_warm = timeIt(X, false); res.X_cold = timeIt(X, true);
    res.S_warm = timeIt(S, false); res.S_cold = timeIt(S, true);
    // insert one <rect> per match (user space from getExtentOfChar), then lay out
    var ins = [];
    for (var k3 = 0; k3 < 7; k3++) {
      var t2 = performance.now(); var made = [];
      matches.forEach(function (m) {
        var a2 = m.el.getExtentOfChar(m.s), b2 = m.el.getExtentOfChar(m.e - 1);
        var r = document.createElementNS(NS, 'rect');
        r.setAttribute('x', a2.x); r.setAttribute('y', a2.y); r.setAttribute('width', b2.x + b2.width - a2.x); r.setAttribute('height', a2.height);
        r.setAttribute('fill', '#ffd400'); m.el.parentNode.insertBefore(r, m.el); made.push(r);
      });
      svg.getBoundingClientRect(); made[0] && made[0].getBBox();
      ins.push(performance.now() - t2);
      await P.raf2();
      made.forEach(function (r) { r.remove(); });
      await P.raf2();
    }
    res.insertRects = +(ins.reduce(function (a, b) { return a + b; }, 0) / ins.length).toFixed(2);
    // the same rects in ONE <g> appended last, drawn over the text with mix-blend-mode: multiply
    var lay = [];
    for (var k5 = 0; k5 < 7; k5++) {
      var t5 = performance.now();
      var g = document.createElementNS(NS, 'g'); g.setAttribute('style', 'mix-blend-mode: multiply; pointer-events: none');
      matches.forEach(function (m) {
        var a3 = m.el.getExtentOfChar(m.s), b3 = m.el.getExtentOfChar(m.e - 1);
        var r = document.createElementNS(NS, 'rect');
        r.setAttribute('x', a3.x); r.setAttribute('y', a3.y); r.setAttribute('width', b3.x + b3.width - a3.x); r.setAttribute('height', a3.height);
        r.setAttribute('fill', '#ffd400'); g.appendChild(r);
      });
      svg.appendChild(g);
      svg.getBoundingClientRect(); g.getBBox();
      lay.push(performance.now() - t5);
      await P.raf2();
      g.remove();
      await P.raf2();
    }
    res.insertLayer = +(lay.reduce(function (a, b) { return a + b; }, 0) / lay.length).toFixed(2);
    // custom highlights: register, then two frames
    if (P.hlSupported()) {
      var base2 = [], hl = [];
      for (var k4 = 0; k4 < 5; k4++) {
        var t3 = performance.now(); await P.raf2(); base2.push(performance.now() - t3);
        var ranges = matches.map(function (m) { var r = document.createRange(); r.setStart(m.node, m.s); r.setEnd(m.node, m.e); return r; });
        var t4 = performance.now();
        CSS.highlights.set('perf', new Highlight(...ranges));
        var setMs = performance.now() - t4;
        await P.raf2(); hl.push(performance.now() - t4);
        CSS.highlights.delete('perf'); await P.raf2();
      }
      res.hlTwoFrames = med(hl); res.idleTwoFrames = med(base2);
    }
    return res;
  };

  P.boxTiming = function (needle, max) {
    var div = document.querySelector(P.roots.d2 + ' .note-scroll'), n = textNodeOf(div), out = {};
    var t0 = performance.now(), idx = [], i = -1;
    while ((i = n.data.indexOf(needle, i + 1)) >= 0 && idx.length < max) idx.push(i);
    out.search = performance.now() - t0; out.matches = idx.length;
    div.style.width = '99.9%'; div.getBoundingClientRect(); div.style.width = '';
    var t1 = performance.now();
    idx.map(function (s) { var r = document.createRange(); r.setStart(n, s); r.setEnd(n, s + needle.length); return r.getClientRects().length; });
    out.R_first = performance.now() - t1;
    var t2 = performance.now();
    for (var k = 0; k < 20; k++) idx.map(function (s) { var r = document.createRange(); r.setStart(n, s); r.setEnd(n, s + needle.length); return r.getClientRects().length; });
    out.R_warm = +((performance.now() - t2) / 20).toFixed(3);
    return out;
  };

  window.P = P;
})();
