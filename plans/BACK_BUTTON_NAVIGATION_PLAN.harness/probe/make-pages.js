'use strict';
// Writes the probe pages into ./pages. Every page is a single self-contained HTML file (inline CSS and JS),
// like a Kronikol report. Run from this folder: node make-pages.js
const fs = require('fs');
const path = require('path');

const PAGES = path.join(__dirname, 'pages');
fs.mkdirSync(PAGES, { recursive: true });

// Records every history-related event into window.__log (drained by the harness) and echoes it to the console
// as "EVT {json}" so that events survive a cross-document navigation (reload, bfcache).
const LOGGER = `<script>
(function () {
  var log = [];
  window.__log = log;
  window.__jsState = { createdAt: Date.now(), counter: 0, rand: Math.random() };
  window.__interceptMode = false;
  var t0 = performance.now();
  var where;
  try { where = window === window.top ? 'top' : 'child'; } catch (e) { where = 'child'; }
  function st() { try { return history.state; } catch (e) { return 'ERR:' + e.name; } }
  function rec(type, extra) {
    var e = { type: type, where: where, t: Math.round(performance.now() - t0), hash: location.hash, state: st(),
              len: history.length, y: Math.round(window.scrollY) };
    if (extra) for (var k in extra) e[k] = extra[k];
    log.push(e);
    try { console.log('EVT ' + JSON.stringify(e)); } catch (x) {}
  }
  window.__rec = rec;
  addEventListener('popstate', function (ev) { rec('popstate', { evState: ev.state }); });
  addEventListener('hashchange', function (ev) { rec('hashchange', { oldURL: ev.oldURL, newURL: ev.newURL }); });
  addEventListener('pageshow', function (ev) { rec('pageshow', { persisted: ev.persisted }); });
  addEventListener('pagehide', function (ev) { rec('pagehide', { persisted: ev.persisted }); });
  var nav = window.navigation;
  if (nav && typeof nav.addEventListener === 'function') {
    nav.addEventListener('navigate', function (ev) {
      var d = { navigationType: ev.navigationType, destUrl: ev.destination ? ev.destination.url : null,
                destSameDocument: ev.destination ? ev.destination.sameDocument : null, hashChange: ev.hashChange,
                canIntercept: ev.canIntercept, userInitiated: ev.userInitiated };
      try { d.destState = ev.destination.getState(); } catch (x) { d.destStateErr = x.name; }
      if (window.__interceptMode) {
        try { ev.intercept({ handler: function () { return Promise.resolve(); } }); d.intercepted = true; }
        catch (x) { d.interceptError = x.name + ': ' + x.message; }
      }
      rec('navigate', d);
    });
    nav.addEventListener('navigatesuccess', function () { rec('navigatesuccess'); });
    nav.addEventListener('navigateerror', function (ev) { rec('navigateerror', { message: ev.message }); });
    nav.addEventListener('currententrychange', function (ev) { rec('currententrychange', { navigationType: ev.navigationType }); });
  }
})();
</script>`;

const page = (title, head, body) => `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>${title}</title>
${head}
${LOGGER}
</head>
<body>
${body}
</body>
</html>
`;

// A tall page (12000 px) with fixed-position targets: #c at y=3000, #target at y=6000, a link to #target at y=1000
// and a link to other.html near the top. No element has id a, b, p, r, x, y, z, n<i>, f1, f2, bf, so those
// fragments never scroll.
const REPORT_STYLE = `<style>
  html, body { margin: 0; padding: 0; }
  body { font: 14px/1.4 sans-serif; }
  #spacer { position: relative; height: 12000px; }
  .mark { position: absolute; left: 0; width: 300px; height: 40px; background: #ddd; }
  #link { position: absolute; top: 1000px; left: 10px; }
  #to-other { position: absolute; top: 40px; left: 10px; }
</style>`;
const REPORT_BODY = `<div id="spacer">
  <a id="to-other" href="other.html">to other.html</a>
  <a id="link" href="#target">to #target</a>
  <div id="c" class="mark" style="top:3000px">#c</div>
  <div id="target" class="mark" style="top:6000px">#target</div>
</div>`;

const files = {};
files['report.html'] = page('Report probe', REPORT_STYLE, REPORT_BODY);

files['other.html'] = page('Other', '<style>body{margin:0;font:14px sans-serif}</style>',
  '<p id="other">other.html</p><a id="to-report" href="report.html">back to report.html</a>');

// History entries added from timers after load, with no user activation (no input, no page.evaluate before Back).
const noact = (selfBack) => page('No activation', REPORT_STYLE, REPORT_BODY + `
<script>
addEventListener('load', function () {
  setTimeout(function () {
    history.pushState({ na: 1 }, '', '#na1'); __rec('pushed', { n: 1 });
    setTimeout(function () {
      history.pushState({ na: 2 }, '', '#na2'); __rec('pushed', { n: 2 });
      ${selfBack ? "setTimeout(function () { __rec('selfback-called'); history.back(); }, 150);" : ''}
    }, 50);
  }, 50);
});
</script>`);
files['noact.html'] = noact(false);
files['noact-selfback.html'] = noact(true);

// One real click on the first entry, then two entries pushed 6 s later from timers (past every engine's transient
// activation window), so only the first entry carries user interaction.
files['noact-click.html'] = page('Click then timers', REPORT_STYLE, REPORT_BODY + `
<button id="go" style="position:fixed;top:8px;right:8px">go</button>
<script>
document.getElementById('go').addEventListener('click', function () {
  __rec('clicked');
  setTimeout(function () {
    history.pushState({ na: 1 }, '', '#na1'); __rec('pushed', { n: 1 });
    setTimeout(function () { history.pushState({ na: 2 }, '', '#na2'); __rec('pushed', { n: 2 }); }, 50);
  }, 6000);
});
</script>`);

// 300 sections, each 60 px tall when rendered, with a 500 px placeholder while skipped.
const sections = [];
for (let i = 0; i < 300; i++) sections.push(`<section id="s${i}"><div class="row">Section ${i}</div></section>`);
files['cv.html'] = page('Content visibility probe', `<style>
  html, body { margin: 0; padding: 0; }
  body { font: 14px/1.4 sans-serif; }
  section { content-visibility: auto; contain-intrinsic-size: auto 500px; }
  .row { height: 60px; box-sizing: border-box; border-bottom: 1px solid #ccc; padding: 4px 8px; }
</style>
<script>
  // The section at the top edge of the viewport and how far into it the viewport starts.
  window.__topSection = function () {
    var secs = document.querySelectorAll('section');
    for (var i = 0; i < secs.length; i++) {
      var r = secs[i].getBoundingClientRect();
      if (r.bottom > 0.5) return { id: secs[i].id, offset: Math.round(-r.top), height: Math.round(r.height) };
    }
    return null;
  };
  window.__layout = function () {
    var secs = document.querySelectorAll('section'), n60 = 0, n500 = 0, other = 0;
    for (var i = 0; i < secs.length; i++) {
      var h = Math.round(secs[i].getBoundingClientRect().height);
      if (h === 60) n60++; else if (h === 500) n500++; else other++;
    }
    var el = document.elementFromPoint(5, 1);
    var sec = el && el.closest ? el.closest('section') : null;
    return { n60: n60, n500: n500, other: other, docH: document.documentElement.scrollHeight,
             y: Math.round(scrollY), fromPoint: sec ? sec.id : null };
  };
</script>`, sections.join('\n'));

const parent = (title, sandbox) => page(title, '<style>body{margin:0;font:14px sans-serif}</style>',
  `<p>parent page</p>\n<iframe id="f" src="report.html"${sandbox === null ? '' : ` sandbox="${sandbox}"`} style="width:900px;height:500px"></iframe>`);
files['parent.html'] = parent('Parent', null);
files['parent-sb.html'] = parent('Parent sandbox', 'allow-scripts');
files['parent-sbso.html'] = parent('Parent sandbox same-origin', 'allow-scripts allow-same-origin');

for (const [name, html] of Object.entries(files)) fs.writeFileSync(path.join(PAGES, name), html);
console.log('wrote', Object.keys(files).join(', '));
