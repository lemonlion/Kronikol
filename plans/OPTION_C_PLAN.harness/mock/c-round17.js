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

/* Round 17 (owner, item 23): with the report's own settings always in view at the top, each scenario's settings bar
   only invited a mismatch (a diagram drawn one way under a bar at the top that says another), so the scenarios' bars
   start hidden. "Scenario settings" on the Features tools line shows them, for the rare scenario set apart from the
   rest; the choice is remembered in this browser. */
(function () {
  var KEY = 'kronikol.scenarioSettings';
  function read() { try { return localStorage.getItem(KEY) === 'shown'; } catch (e) { return false; } }
  function write(on) { try { localStorage.setItem(KEY, on ? 'shown' : 'hidden'); } catch (e) {} }
  function run() {
    var line = document.querySelector('.features-tools-line');
    if (!line || line.querySelector('.k-scenario-settings-toggle')) return;
    var b = document.createElement('button');
    b.type = 'button'; b.className = 'k-flag k-scenario-settings-toggle'; b.textContent = 'Scenario settings';
    function set(on) {
      document.body.classList.toggle('k-scenario-settings', on);
      b.setAttribute('aria-pressed', on ? 'true' : 'false');
      b.title = on ? "Hide each scenario's payload and diagram settings (the bar at the top sets them for every scenario)"
                   : "Show payload and diagram settings on each scenario, to set one apart from the bar at the top";
    }
    set(read());
    b.addEventListener('click', function () { var on = !document.body.classList.contains('k-scenario-settings'); set(on); write(on); });
    line.insertBefore(b, line.querySelector('.sort-group'));
  }
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
