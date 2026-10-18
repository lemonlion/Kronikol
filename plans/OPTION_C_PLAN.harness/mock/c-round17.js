/* Round 17 (owner, item 21): Firefox could not open a diagram's image in a new tab from the diagram's menu. The PNG
   items opened the tab only once the picture was drawn, after the click had ended, which a popup blocker refuses (the
   component diagram, the largest, takes longest to draw), and every item opened a blob: URL made by a file:// page,
   whose origin is opaque. The tab is now opened while the click is handled and the picture (or the source) is
   written into it; where no tab could be opened, the old way is tried. */
(function () {
  function esc(s) { return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;'); }
  function write(w, title, body, bg) {
    w.document.open();
    w.document.write('<!doctype html><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>' + esc(title) +
      '</title><body style="margin:0;padding:16px;background:' + bg + '">' + body + '</body>');
    w.document.close();
  }
  window.__kOpenTab = function () { try { return window.open('', '_blank'); } catch (e) { return null; } };
  window.__kOpenPng = function (w, canvas) {
    if (!w || w.closed) { canvas.toBlob(function (blob) { window.open(URL.createObjectURL(blob)); }, 'image/png'); return; }
    write(w, 'Diagram (PNG)', '<img alt="Diagram" style="max-width:none" src="' + canvas.toDataURL('image/png') + '">', '#f5f7fb');
  };
  window.__kOpenSvg = function (markup) {
    var w = window.__kOpenTab();
    if (!w) { window.open(URL.createObjectURL(new Blob([markup], { type: 'image/svg+xml' }))); return; }
    write(w, 'Diagram (SVG)', '<img alt="Diagram" style="max-width:none" src="data:image/svg+xml;base64,' + btoa(unescape(encodeURIComponent(markup))) + '">', '#ffffff');
  };
  window.__kOpenText = function (text) {
    var w = window.__kOpenTab();
    if (!w) { window.open(URL.createObjectURL(new Blob([text], { type: 'text/plain;charset=utf-8' }))); return; }
    write(w, 'Diagram source', '<pre style="margin:0;white-space:pre-wrap;font:13px/1.45 ui-monospace,Consolas,monospace">' + esc(text) + '</pre>', '#ffffff');
  };
})();

/* Round 17 (owner, item 2): "Expand All Features" and "Expand All Scenarios" each flipped their own label, so a
   reader who had opened a few by hand had to open everything before the button offered to close them. Each level now
   has a label and two icon buttons, expand and collapse, both always a click away, in less width than the two words
   took. A button that has nothing to do (everything already open, or already closed) is dimmed. Expanding the
   scenarios opens their features too: inside a closed feature an opened scenario shows nothing. */
(function () {
  var NS = 'http://www.w3.org/2000/svg';
  var PATHS = { open: ['4,6 8,2 12,6', '4,10 8,14 12,10'], close: ['4,2 8,6 12,2', '4,14 8,10 12,14'] };
  function icon(kind) {
    var s = document.createElementNS(NS, 'svg');
    s.setAttribute('viewBox', '0 0 16 16'); s.setAttribute('aria-hidden', 'true'); s.setAttribute('focusable', 'false');
    PATHS[kind].forEach(function (pts) {
      var l = document.createElementNS(NS, 'polyline');
      l.setAttribute('points', pts); l.setAttribute('fill', 'none'); l.setAttribute('stroke', 'currentColor');
      l.setAttribute('stroke-width', '1.6'); l.setAttribute('stroke-linecap', 'round'); l.setAttribute('stroke-linejoin', 'round');
      s.appendChild(l);
    });
    return s;
  }
  var groups = [];
  function state(g) {
    var els = document.querySelectorAll(g.sel), open = 0;
    for (var i = 0; i < els.length; i++) if (els[i].open) open++;
    g.expand.setAttribute('aria-disabled', open === els.length ? 'true' : 'false');
    g.collapse.setAttribute('aria-disabled', open === 0 ? 'true' : 'false');
  }
  var pending = false;
  function update() { if (pending) return; pending = true; requestAnimationFrame(function () { pending = false; groups.forEach(state); }); }
  function act(g, opening) {
    var els = document.querySelectorAll(g.sel);
    for (var i = 0; i < els.length; i++) {
      if (opening) {
        els[i].setAttribute('open', '');
        if (g.sel === 'details.scenario') { var f = els[i].closest('details.feature'); if (f) f.setAttribute('open', ''); }
      } else els[i].removeAttribute('open');
    }
    update();
  }
  function run() {
    var olds = Array.prototype.slice.call(document.querySelectorAll('button.collapse-expand-all'));
    olds.forEach(function (old) {
      var m = /'(details\.(feature|scenario))'/.exec(old.getAttribute('onclick') || '');
      if (!m) return;
      var noun = m[2] === 'feature' ? 'features' : 'scenarios';
      var g = { sel: m[1] };
      var wrap = document.createElement('span');
      wrap.className = 'k-fold-group'; wrap.setAttribute('role', 'group'); wrap.setAttribute('aria-label', 'Expand or collapse all ' + noun);
      var lab = document.createElement('span'); lab.className = 'k-fold-label'; lab.textContent = noun; lab.setAttribute('aria-hidden', 'true');
      wrap.appendChild(lab);
      [['expand', 'open', 'Expand all ' + noun], ['collapse', 'close', 'Collapse all ' + noun]].forEach(function (d) {
        var b = document.createElement('button');
        b.type = 'button'; b.className = 'k-fold-btn k-fold-' + d[0]; b.title = d[2]; b.setAttribute('aria-label', d[2]);
        b.appendChild(icon(d[1]));
        b.addEventListener('click', function () { if (b.getAttribute('aria-disabled') !== 'true') act(g, d[0] === 'expand'); });
        g[d[0]] = b; wrap.appendChild(b);
      });
      old.parentNode.replaceChild(wrap, old);
      groups.push(g);
    });
    if (!groups.length) return;
    document.addEventListener('toggle', update, true);
    update();
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', function () { setTimeout(run, 0); });
  else setTimeout(run, 0);
})();

/* Round 17 (owner, item 26, in place of item 23's report-wide switch): each scenario's settings bar is its own. A
   gear at the top right of the scenario's diagram, grey until pointed at, opens that scenario's bar, where the gear is
   the right-most control, in the same place, so a second click closes it. While open, the bar stays under the report's
   bar as the reader scrolls through the scenario, and it starts "This scenario:" so it reads apart from the report's. */
(function () {
  var NS = 'http://www.w3.org/2000/svg';
  // a cog: eight teeth around a ring, the hole cut by the even-odd rule
  function cogPath() {
    var n = 8, ro = 9.6, ri = 7.2, tw = 0.2, tb = 0.13, d = '';
    for (var i = 0; i < n; i++) {
      var a = i * 2 * Math.PI / n;
      [[ri, a - tw - tb], [ro, a - tw], [ro, a + tw], [ri, a + tw + tb]].forEach(function (pt, k) {
        d += (i === 0 && k === 0 ? 'M' : 'L') + (pt[0] * Math.cos(pt[1])).toFixed(2) + ' ' + (pt[0] * Math.sin(pt[1])).toFixed(2);
      });
    }
    return d + 'Z M3.4 0A3.4 3.4 0 1 0 -3.4 0A3.4 3.4 0 1 0 3.4 0Z';
  }
  var COG = cogPath();
  function setup(sec) {
    var bar = sec.querySelector(':scope > .diagram-options-bar') || sec.querySelector(':scope > .diagram-toggle:not(.diagram-toggle-tabs)');
    if (!bar || bar.parentNode.classList.contains('k-scn-settings')) return;
    // A scenario with only a sequence diagram has no tab bar, so the report's layout script floated this bar onto the
    // title line; the gear stands in for it there, so it goes back to a row of its own (and, wrapped, the script's
    // 'summary + .diagram-toggle' no longer finds it).
    bar.removeAttribute('data-layout');
    bar.style.marginTop = '';
    // and it takes the options bar's look, so the row reads the same in both kinds of scenario
    bar.classList.add('diagram-options-bar');
    var wrap = document.createElement('div');
    wrap.className = 'k-scn-settings';
    bar.parentNode.insertBefore(wrap, bar);
    wrap.appendChild(bar);
    var lab = document.createElement('span');
    lab.className = 'k-this-scenario';
    lab.textContent = 'This scenario:';
    bar.insertBefore(lab, bar.firstChild);
    var g = document.createElement('button');
    g.type = 'button';
    g.className = 'k-scn-gear';
    g.setAttribute('aria-label', "This scenario's settings");
    var svg = document.createElementNS(NS, 'svg');
    svg.setAttribute('viewBox', '-12 -12 24 24'); svg.setAttribute('aria-hidden', 'true'); svg.setAttribute('focusable', 'false');
    var path = document.createElementNS(NS, 'path');
    path.setAttribute('d', COG); path.setAttribute('fill-rule', 'evenodd');
    svg.appendChild(path);
    g.appendChild(svg);
    function set(on) {
      wrap.classList.toggle('k-on', on);
      g.setAttribute('aria-expanded', on ? 'true' : 'false');
      g.title = on ? "Hide this scenario's payload and diagram settings" : "This scenario's payload and diagram settings, to set it apart from the report's";
    }
    set(false);
    g.addEventListener('click', function () { set(!wrap.classList.contains('k-on')); });
    wrap.appendChild(g);
  }
  function run() { Array.prototype.forEach.call(document.querySelectorAll('details.example-diagrams'), setup); }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', function () { setTimeout(run, 0); });
  else setTimeout(run, 0);
})();

/* Round 17 (owner, item 25): each payload and diagram control's label sits in a .k-lbl that also holds, invisibly, the
   same words in bold (c-round17.css), so the control is as wide off as on and switching it does not shift its row. */
(function () {
  function wrap(root) {
    Array.prototype.forEach.call((root || document).querySelectorAll('.details-radio-btn'), function (b) {
      if (b.querySelector('.k-lbl')) return;
      var text = b.textContent.trim();
      if (!text || b.children.length) return;
      var s = document.createElement('span');
      s.className = 'k-lbl'; s.setAttribute('data-label', text); s.textContent = text;
      b.textContent = ''; b.appendChild(s);
    });
  }
  // the report's toggle functions write a control's label again as plain text when it switches: wrap it again, once
  // a frame, for every control whose words changed
  var dirty = [], queued = false;
  function flush() {
    queued = false;
    var list = dirty; dirty = [];
    list.forEach(function (b) { if (b.isConnected && !b.querySelector('.k-lbl')) wrap(b.parentNode); });
  }
  function run() {
    wrap(document);
    new MutationObserver(function (ms) {
      for (var i = 0; i < ms.length; i++) {
        var t = ms[i].target, el = t.nodeType === 1 ? t : t.parentElement;
        var b = el && el.closest ? el.closest('.details-radio-btn') : null;
        if (b && dirty.indexOf(b) < 0) dirty.push(b);
      }
      if (dirty.length && !queued) { queued = true; requestAnimationFrame(flush); }
    }).observe(document.body, { childList: true, characterData: true, subtree: true });
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', function () { setTimeout(run, 0); });
  else setTimeout(run, 0);
})();

/* Round 17 (owner, items 29 and 30): the History tab's two run charts. Pass rate becomes a row of rings drawn like
   the summary's (pass, fail, skipped and bypassed arcs, the rate in the middle), one per run; total duration keeps
   its bars, now drawn as HTML so each can carry its time ("44s", "2m 26s") undistorted. Each keeps its run's tooltip. */
(function () {
  var NS = 'http://www.w3.org/2000/svg';
  function svgEl(n, a) { var e = document.createElementNS(NS, n); for (var k in a) e.setAttribute(k, a[k]); return e; }
  function count(t, word) { var m = new RegExp('(\\d+) ' + word).exec(t); return m ? +m[1] : 0; }
  function ring(title) {
    var p = count(title, 'passed'), f = count(title, 'failed'), s = count(title, 'skipped'), b = count(title, 'bypassed');
    var m = /of (\d+)/.exec(title), total = m ? +m[1] : p + f + s + b;
    var svg = svgEl('svg', { viewBox: '0 0 100 100', role: 'img' });
    var C = 2 * Math.PI * 40, at = 0;
    svg.appendChild(svgEl('circle', { cx: 50, cy: 50, r: 40, fill: 'none', 'class': 'k-ring-track', 'stroke-width': 12 }));
    [['pass', p], ['fail', f], ['skip', s], ['bypass', b]].forEach(function (seg) {
      if (!seg[1] || !total) return;
      var len = C * seg[1] / total;
      svg.appendChild(svgEl('circle', { cx: 50, cy: 50, r: 40, fill: 'none', 'class': 'k-ring-' + seg[0], 'stroke-width': 12,
        'stroke-dasharray': len.toFixed(2) + ' ' + (C - len).toFixed(2), 'stroke-dashoffset': (-at).toFixed(2), transform: 'rotate(-90 50 50)' }));
      at += len;
    });
    // a run with a failure never reads 100%
    var pct = total ? (f || s || b ? Math.min(99, Math.floor(100 * p / total)) : 100) : 0;
    var t = svgEl('text', { x: 50, y: 50, 'text-anchor': 'middle', 'dominant-baseline': 'central' });
    t.textContent = pct + '%';
    svg.appendChild(t);
    svg.setAttribute('aria-label', pct + '% passed');
    return { svg: svg, failed: f > 0 };
  }
  function seconds(t) {
    var m = /([\d.]+)\s*(ms|s|min|m|h)\b/.exec(t.split('·').pop());
    if (!m) return null;
    var v = +m[1];
    return m[2] === 'ms' ? v / 1000 : m[2] === 'h' ? v * 3600 : m[2] === 's' ? v : v * 60;
  }
  function short(sec) {
    if (sec < 1) return Math.round(sec * 1000) + 'ms';
    var r = Math.round(sec);
    if (r < 60) return r + 's';
    if (r < 3600) return Math.floor(r / 60) + 'm ' + (r % 60) + 's';
    return Math.floor(r / 3600) + 'h ' + Math.round((r % 3600) / 60) + 'm';
  }
  function convert(svg) {
    if (svg.hasAttribute('data-k-done')) return;
    svg.setAttribute('data-k-done', '1');
    var label = svg.getAttribute('aria-label') || '';
    var bars = Array.prototype.slice.call(svg.querySelectorAll('rect.history-bar'))
      .sort(function (a, b) { return +a.getAttribute('x') - +b.getAttribute('x'); });
    if (!bars.length) return;
    var row = document.createElement('div');
    row.setAttribute('role', 'list');
    row.setAttribute('aria-label', label);
    if (/^Pass rate/i.test(label)) {
      row.className = 'k-hist-rings';
      bars.forEach(function (r) {
        var title = (r.querySelector('title') || {}).textContent || '';
        var item = document.createElement('div');
        item.className = 'k-hist-ring';
        item.setAttribute('role', 'listitem');
        item.title = title;
        var made = ring(title);
        if (made.failed) item.classList.add('k-hist-ring-failed');
        item.appendChild(made.svg);
        row.appendChild(item);
      });
    } else if (/duration/i.test(label)) {
      row.className = 'k-hist-bars';
      var secs = bars.map(function (r) { return seconds((r.querySelector('title') || {}).textContent || ''); });
      var max = Math.max.apply(null, secs.filter(function (v) { return v != null; }).concat([0]));
      bars.forEach(function (r, i) {
        var title = (r.querySelector('title') || {}).textContent || '';
        var col = document.createElement('div');
        col.className = 'k-hist-col';
        col.setAttribute('role', 'listitem');
        col.title = title;
        var bar = document.createElement('div');
        bar.className = 'k-hist-bar';
        bar.style.height = (max && secs[i] != null ? Math.max(8, 100 * secs[i] / max) : 0) + '%';
        var t = document.createElement('span');
        t.className = 'k-hist-time';
        t.textContent = secs[i] != null ? short(secs[i]) : '';
        bar.appendChild(t);
        col.appendChild(bar);
        row.appendChild(col);
      });
    } else return;
    svg.parentNode.insertBefore(row, svg);
    // an svg element has no hidden property, only the attribute
    svg.setAttribute('hidden', '');
    svg.style.display = 'none';
  }
  function run(root) { Array.prototype.forEach.call((root || document).querySelectorAll('svg.history-chart-svg'), convert); }
  var queued = false;
  new MutationObserver(function () {
    if (queued) return;
    queued = true;
    requestAnimationFrame(function () { queued = false; run(); });
  }).observe(document.documentElement, { childList: true, subtree: true });
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', function () { run(); });
  else run();
})();
