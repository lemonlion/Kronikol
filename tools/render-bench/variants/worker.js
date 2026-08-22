'use strict';
// Variant: render via N Web Workers (engine never loaded on the main thread).
// env WORKERS=N (default 1), ENGINE=<file> (default old-plantuml.js), MAXH=<px> fragment height,
//     LAZYVIZ=1 (load Graphviz in the worker on first use), PREFETCH=1 (source-keyed cache + parallel prefetch of note-toggle fragments)
//     FILEMODE=1 (page served from file://, worker from Blob URL, engine via importScripts from BASE)
//     FILEMODE=2 (page from file://, engine text fetch()ed on the main thread and inlined into the worker Blob — no network from the worker)
//     CDNENGINE=1 (engine/viz URLs point at the real jsDelivr fork — classic build only)
const fs = require('fs'), path = require('path');
const N = parseInt(process.env.WORKERS || '1', 10);
const engine = process.env.ENGINE || 'old-plantuml.js';
const PREFETCH = process.env.PREFETCH === '1';
const FILEMODE = process.env.FILEMODE || '';
const INLINE = FILEMODE === '2';
const CDNENGINE = process.env.CDNENGINE === '1';
const ESM = !/^old-/.test(engine);
const BASE = CDNENGINE ? 'https://cdn.jsdelivr.net/gh/lemonlion/plantuml-js-plantuml_limit_size_98304@v1.2026.3beta6-patched' : FILEMODE ? 'http://127.0.0.1:18765' : '';
const ENGDIR = CDNENGINE ? BASE : BASE + '/__engine';
const workerSrc = fs.readFileSync(path.join(__dirname, '..', 'puml-worker.js'), 'utf8');
const workerCtor = FILEMODE ? 'new Worker(URL.createObjectURL(new Blob([' + JSON.stringify(workerSrc) + '], { type: "application/javascript" })))' : "new Worker('/__sp/puml-worker.js')";

const shim = `<script>
(function(){
  var N = ${N}; var PREFETCH = ${PREFETCH}; var INLINE = ${INLINE}; var ESM = ${ESM};
  var VIZ_URL = '${ENGDIR}/viz-global.js', ENGINE_URL = '${ENGDIR}/plantuml.js';
  var workers = [], pending = {}, seq = 0, readyCount = 0, cache = {}, inflight = {}, preQueue = [];
  window.__workerStats = { renders: 0, workerMs: 0, injectMs: 0, cacheHits: 0, engineFetchMs: 0 };
  function onMsg(ev) {
    var m = ev.data;
    if (m.type === 'ready') { readyCount++; return; }
    if (m.type === 'fatal') { console.error('worker fatal', m.message); return; }
    var p = pending[m.seq]; if (!p) return; delete pending[m.seq]; p.w._busy--;
    var t0 = performance.now();
    if (m.type === 'done') { window.__workerStats.renders++; window.__workerStats.workerMs += m.ms; if (PREFETCH) cache[p.key] = m.svg; }
    var ids = inflight[p.key] || [p.id]; delete inflight[p.key];
    for (var k = 0; k < ids.length; k++) {
      var el = ids[k] ? document.getElementById(ids[k]) : null; if (!el) continue;
      if (m.type === 'done') el.innerHTML = m.svg; else el.textContent = 'Render error: ' + m.message;
    }
    window.__workerStats.injectMs += performance.now() - t0;
  }
  function startWorkers(blobUrl) {
    for (var i = 0; i < N; i++) {
      var w = blobUrl ? new Worker(blobUrl) : ${workerCtor}; w._busy = 0; w.onmessage = onMsg;
      w.onerror = function (e) { console.error('[worker error] ' + (e && e.message)); };
      w.postMessage({ type: 'init', viz: VIZ_URL, engine: ENGINE_URL, esm: ESM, lazyViz: ${process.env.LAZYVIZ === '1' ? 'true' : 'false'}, inline: INLINE });
      workers.push(w);
    }
    var q = preQueue; preQueue = [];
    for (var j = 0; j < q.length; j++) dispatch(q[j].key, q[j].lines, q[j].id);
  }
  if (INLINE) {
    var tf = performance.now();
    Promise.all([fetch(VIZ_URL).then(function (r) { return r.text(); }), fetch(ENGINE_URL).then(function (r) { return r.text(); })]).then(function (parts) {
      window.__workerStats.engineFetchMs = Math.round(performance.now() - tf);
      var eng = parts[1];
      if (ESM) eng = eng.replace(/export\\s*\\{\\s*C as render\\s*,\\s*D as renderToString\\s*\\};?\\s*$/, 'self.__plantumlExports = { render: C, renderToString: D };');
      var src = ${JSON.stringify(workerSrc)} + '\\n;try{\\n' + parts[0] + '\\n}catch(e){console.error("viz-global failed: "+e);}\\n;(function(){\\n' + eng + '\\n})();\\n';
      startWorkers(URL.createObjectURL(new Blob([src], { type: 'application/javascript' })));
    }).catch(function (e) { console.error('engine fetch failed: ' + e); });
  } else {
    startWorkers(null);
  }
  function dispatch(key, lines, id) {
    if (PREFETCH && cache[key] !== undefined) { window.__workerStats.cacheHits++; var el = id ? document.getElementById(id) : null; if (el) el.innerHTML = cache[key]; return; }
    if (PREFETCH && inflight[key]) { inflight[key].push(id); return; }
    if (!workers.length) { preQueue.push({ key: key, lines: lines, id: id }); return; }
    inflight[key] = [id];
    var w = workers[0]; for (var i = 1; i < workers.length; i++) if (workers[i]._busy < w._busy) w = workers[i];
    seq++; pending[seq] = { id: id, w: w, key: key }; w._busy++;
    w.postMessage({ type: 'render', seq: seq, id: id || ('__pf' + seq), lines: lines });
  }
  window.plantuml = {
    render: function (lines, id) { dispatch(PREFETCH ? lines.join('\\n') : String(seq + 1), lines, id); },
    prefetch: function (sources) { if (!PREFETCH) return; for (var i = 0; i < sources.length; i++) dispatch(sources[i], sources[i].split('\\n'), null); }
  };
  window.plantumlLoad = function () {};
})();
</script>`;

const replace = [
  ['<script defer src="/__engine/viz-global.js"></script>', ''],
  ['<script defer src="/__engine/plantuml.js"></script>', shim],
];
if (process.env.MAXH) replace.push(['        var _maxDiagramHeight = 12000;\n', '        var _maxDiagramHeight = ' + parseInt(process.env.MAXH, 10) + ';\n']);
if (N > 1) {
  replace.push(
    ['        var rendering = false;\n', '        var rendering = 0; var __MAXPAR = ' + N + ';\n'],
    ['if (rendering || window._plantumlRendering || renderQueue.length === 0) return;', 'if (rendering >= __MAXPAR || renderQueue.length === 0) return;'],
    ['            rendering = true;\n            window._plantumlRendering = true;\n            var item = renderQueue.shift();', '            rendering++;\n            window._plantumlRendering = true;\n            var item = renderQueue.shift();'],
  );
}

module.exports = {
  engine, viz: 'viz-global.js', replace,
  transform(html) {
    if (N > 1) {
      const start = html.indexOf("document.addEventListener('DOMContentLoaded', function() {\n        document.body.classList.add('plantuml-ready');");
      const end = html.indexOf('</script>', start);
      let s = html.slice(start, end);
      s = s.split('rendering = false;').join('rendering = Math.max(0, rendering - 1); if (!rendering) window._plantumlRendering = false;');
      s = s.replace("            try {\n                window.plantuml.render(lines, item.el.id);\n            } catch(e) {", "            try {\n                window.plantuml.render(lines, item.el.id);\n                setTimeout(processQueue, 0);\n            } catch(e) {");
      html = html.slice(0, start) + s + html.slice(end);
    }
    if (PREFETCH) {
      const a1 = '                // Process fragment render queue sequentially\n                var fragIdx = 0;';
      const a2 = '                    var fragI = 0;\n                    function renderNextFragment() {';
      if (!html.includes(a1) || !html.includes(a2)) { console.error('PREFETCH anchors not found', html.includes(a1), html.includes(a2)); process.exit(2); }
      html = html.split(a1).join('                if (window.plantuml.prefetch) window.plantuml.prefetch(fragQueue.map(function (f) { return f.source; }));\n' + a1);
      html = html.split(a2).join('                    if (window.plantuml.prefetch) window.plantuml.prefetch(fragList.map(function (f) { return f.source; }));\n' + a2);
    }
    return html;
  }
};
