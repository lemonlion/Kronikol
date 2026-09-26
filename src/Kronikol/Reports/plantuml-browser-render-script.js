<script>
    // Kronikol browser rendering — engine bootstrap and render shim.
    //
    // The TeaVM plantuml.js engine (7 MB, plus 1.4 MB of Graphviz) used to be two deferred script tags:
    // the page was not interactive until it had compiled, every diagram rendered on the main thread,
    // and a note toggle on a big diagram froze the page for seconds. Now the engine runs in Web
    // Workers: this shim fetches viz-global.js + plantuml.js as text, inlines them with the worker host
    // (WORKER_HOST_SOURCE, plantuml-worker-host.js) into ONE Blob and creates workers from it — a Blob
    // worker created by a file:// page cannot load anything over the network, and Chrome refuses
    // new Worker('file://…'), which is why the page does the fetching. One worker starts immediately;
    // the rest (up to BrowserRenderWorkers, capped by hardwareConcurrency) start lazily once the first
    // render has completed, so parallel engine compiles never delay the first diagram.
    //
    // Public surface (unchanged for every caller — the render queue below, collapsible-notes-script.js,
    // internal-flow-popup-script.js):
    //   window.plantuml.render(lines, targetId[, { onError }])  — renders into #targetId; the result
    //       lands via `el.innerHTML = svg`, which fires the caller's MutationObserver exactly as the
    //       engine's own DOM insertion did. A cache hit is served synchronously. A failure either goes
    //       to onError(message) (return true to take over the element) or is written into the element
    //       as the same markup the synchronous engine throw produced.
    //   window.plantuml.prefetch(sources)  — render every uncached source with no target; results go to
    //       the cache (the note-toggle paths call this with their new fragment list).
    //   window.plantuml.maxParallel  — how many renders the queue may keep in flight.
    //   window.plantumlLoad()  — a no-op kept for compatibility; the shim owns the engine lifecycle.
    //   window.__kronikolRender  — telemetry: { mode: 'worker' | 'main-thread', workers, renders,
    //       cacheHits, workerMs, injectMs, errors, inFlight, maxInFlight, engineFetchMs, fallbackReason,
    //       engineIntegrity, vizIntegrity }.
    //
    // Both files are checked against their known hashes (TrackingDefaults) by the browser itself: the
    // worker path's fetch and the fallback's tags carry `integrity`, and a file whose bytes differ is never
    // evaluated. A refused engine refuses every diagram, naming the file and the hash it should have had; a
    // refused viz-global.js drops Graphviz only: sequence diagrams never use it, and the engine lays out a
    // component diagram with its Smetana port when Graphviz is absent (plans/ENGINE_PIN_PLAN.md S2).
    //
    // Fallback: when Workers, fetch, Blob URLs or OffscreenCanvas are unavailable, when
    // BrowserRenderWorkers is 0, or when the engine cannot be fetched (offline with a cold cache, a CDN
    // host without CORS), the shim injects a viz script tag and an engine module tag and renders on the
    // main thread — the pre-3.0.45 path, one render at a time.
    (function () {
        var VIZ_URL = '__PLANTUML_CDN_BASE__/viz-global.js';
        var ENGINE_URL = '__PLANTUML_CDN_BASE__/plantuml.js';
        var VIZ_INTEGRITY = '__PLANTUML_VIZ_INTEGRITY__';
        var ENGINE_INTEGRITY = '__PLANTUML_ENGINE_INTEGRITY__';
        var WORKERS_REQUESTED = __BROWSER_RENDER_WORKERS__;
        var CACHE_MEGABYTES = __BROWSER_RENDER_CACHE_MB__;
        var WORKER_HOST_SOURCE = __PLANTUML_WORKER_HOST_SOURCE__;
        var CACHE_LIMIT = Math.max(0, CACHE_MEGABYTES) * 1024 * 1024;
        var hardware = (typeof navigator !== 'undefined' && navigator.hardwareConcurrency) || 2;
        var targetWorkers = WORKERS_REQUESTED > 0 ? Math.max(1, Math.min(WORKERS_REQUESTED, hardware)) : 0;

        var telemetry = window.__kronikolRender = {
            mode: 'starting', workers: 0, workersRequested: WORKERS_REQUESTED, workersTarget: targetWorkers,
            renders: 0, cacheHits: 0, cacheEntries: 0, cacheBytes: 0, workerMs: 0, injectMs: 0, errors: 0,
            inFlight: 0, maxInFlight: 0, engineFetchMs: null, engineReadyAt: null, fallbackReason: null,
            // 'verified' | 'mismatch' | null: null until the file's fetch settles, and after a network failure.
            engineIntegrity: null, vizIntegrity: null
        };

        var mode = null;            // null (undecided) | 'worker' | 'main-thread'
        var workers = [];           // { w, busy, ready }
        var pending = {};           // seq -> job handed to a worker
        var queue = [];             // jobs waiting for a free worker / the main-thread engine
        var inflightByKey = {};     // source -> job (dedupe: a second render of the same source just adds a target)
        var seq = 0;
        var firstDone = false;
        var blobUrl = null;
        var esmMode = false;
        var engineRender = null;    // main-thread mode: the real engine's render
        var engineReady = false;
        var engineError = null;
        var mainBusy = false;
        var cache = new Map();      // source -> svg string, insertion order = LRU order
        var cacheBytes = 0;

        function noop() {}
        function now() { return (typeof performance !== 'undefined' && performance.now) ? performance.now() : Date.now(); }
        function workerCapable() { return mode !== 'main-thread' && targetWorkers > 0; }
        function normalizeLines(lines) {
            if (Array.isArray(lines)) return lines.map(function (l) { return String(l); });
            return String(lines == null ? '' : lines).split('\n');
        }
        function newJob(key, lines, targets) { return { seq: ++seq, key: key, lines: lines, targets: targets, worker: null }; }

        // --- cache (bytes-bounded, least-recently-used first) ---------------------------------
        function cacheGet(key) {
            if (!cache.has(key)) return undefined;
            var v = cache.get(key); cache.delete(key); cache.set(key, v);
            return v;
        }
        function cacheEvict() {
            while (cacheBytes > CACHE_LIMIT && cache.size > 0) {
                var oldest = cache.keys().next().value;
                cacheBytes -= oldest.length + cache.get(oldest).length;
                cache.delete(oldest);
            }
            telemetry.cacheEntries = cache.size; telemetry.cacheBytes = cacheBytes;
        }
        function cachePut(key, svg) {
            // Only real results: a failure the engine wrote as text is described per element, never cached.
            if (CACHE_LIMIT <= 0 || typeof svg !== 'string' || svg.indexOf('<svg') < 0) return;
            var size = key.length + svg.length;
            if (size > CACHE_LIMIT) return;
            if (cache.has(key)) { cacheBytes -= key.length + cache.get(key).length; cache.delete(key); }
            cache.set(key, svg); cacheBytes += size;
            cacheEvict();
        }

        // --- writing results and failures into the page ---------------------------------------
        function resolveTarget(t) { return t.el || (t.id ? document.getElementById(t.id) : null); }
        function inject(targets, svg) {
            var t0 = now();
            for (var i = 0; i < targets.length; i++) {
                var el = resolveTarget(targets[i]);
                if (el) el.innerHTML = svg;
            }
            telemetry.injectMs += now() - t0;
        }
        // Source text shown inside markup: `&` first, or a `&lt;`, `&copy` or `&amp;` in the source would
        // be read by the page and shown as something else.
        function escapeMarkupText(s) { return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;'); }
        function tooLargeMarkup(source) {
            return '<div style="color:#c00;padding:1em;border:1px solid #c00;border-radius:6px;margin:0.5em 0;">'
                + '<strong>Diagram too large for client-side rendering.</strong><br>'
                + 'Use <code>PlantUmlRendering.Server</code> or <code>PlantUmlRendering.Local</code> for large diagrams.'
                + '<details style="margin-top:0.5em"><summary>Raw PlantUML</summary><pre style="white-space:pre-wrap">'
                + escapeMarkupText(source || '') + '</pre></details></div>';
        }
        function writeFailure(target, source, message) {
            message = String(message == null ? '' : message);
            if (target.opts && typeof target.opts.onError === 'function') {
                try { if (target.opts.onError(message) === true) return; } catch (e) { console.error('Kronikol render onError failed:', e); }
            }
            var el = resolveTarget(target);
            if (!el) return;
            if (/too large/i.test(message)) el.innerHTML = tooLargeMarkup(source);
            else el.textContent = 'Render error: ' + message;
        }

        // --- worker mode ----------------------------------------------------------------------
        function busyCount() { var n = 0; for (var i = 0; i < workers.length; i++) if (workers[i].busy) n++; return n; }
        function readyCount() { var n = 0; for (var i = 0; i < workers.length; i++) if (workers[i].ready) n++; return n; }
        function idleWorker() { for (var i = 0; i < workers.length; i++) if (workers[i].ready && !workers[i].busy) return workers[i]; return null; }

        function startWorker() {
            var w;
            try { w = new Worker(blobUrl); } catch (e) { useMainThreadEngine('worker creation failed: ' + (e && e.message ? e.message : e)); return null; }
            var rec = { w: w, busy: false, ready: false };
            w.onmessage = function (ev) { onWorkerMessage(rec, ev.data || {}); };
            w.onerror = function (ev) {
                var msg = (ev && ev.message) ? ev.message : 'worker error';
                if (!rec.ready) workerDied(rec, msg); else console.error('Kronikol render worker: ' + msg);
            };
            workers.push(rec);
            w.postMessage({ type: 'init', esm: esmMode });
            return rec;
        }
        function workerDied(rec, message) {
            console.error('Kronikol render worker failed: ' + message);
            var i = workers.indexOf(rec);
            if (i >= 0) workers.splice(i, 1);
            try { rec.w.terminate(); } catch (e) { /* best effort */ }
            // Whatever it was rendering goes back to the front of the queue.
            var keys = Object.keys(pending);
            for (var k = 0; k < keys.length; k++) {
                var job = pending[keys[k]];
                if (job.worker === rec) { delete pending[keys[k]]; job.worker = null; queue.unshift(job); }
            }
            telemetry.workers = readyCount(); telemetry.inFlight = busyCount();
            if (workers.length === 0) useMainThreadEngine('render worker failed: ' + message);
            else pumpWorkers();
        }
        function onWorkerMessage(rec, m) {
            if (m.type === 'ready') {
                rec.ready = true; telemetry.workers = readyCount();
                if (telemetry.engineReadyAt === null) telemetry.engineReadyAt = now();
                pumpWorkers();
                return;
            }
            if (m.type === 'fatal') { workerDied(rec, m.message); return; }
            var job = pending[m.seq];
            if (!job) return;
            delete pending[m.seq];
            rec.busy = false; job.worker = null;
            if (inflightByKey[job.key] === job) delete inflightByKey[job.key];
            telemetry.inFlight = busyCount();
            if (m.type === 'done') {
                telemetry.renders++; telemetry.workerMs += m.ms || 0;
                cachePut(job.key, m.svg);
                inject(job.targets, m.svg);
                if (!firstDone) firstDone = true;
            } else if (m.type === 'error') {
                telemetry.errors++;
                for (var i = 0; i < job.targets.length; i++) writeFailure(job.targets[i], job.key, m.message);
            }
            pumpWorkers();
        }
        function pumpWorkers() {
            if (mode !== 'worker') return;
            while (queue.length > 0) {
                var rec = idleWorker();
                if (!rec) {
                    // Start further workers only once the first render is done (parallel engine compiles
                    // would delay the first diagram) and only when there is work waiting for them.
                    if (firstDone && workers.length < targetWorkers) startWorker();
                    return;
                }
                var job = queue.shift();
                rec.busy = true; job.worker = rec; pending[job.seq] = job;
                telemetry.inFlight = busyCount();
                if (telemetry.inFlight > telemetry.maxInFlight) telemetry.maxInFlight = telemetry.inFlight;
                rec.w.postMessage({ type: 'render', seq: job.seq, id: 'k' + job.seq, lines: job.lines });
            }
        }

        function integrityMessage(file, url, expected) {
            return 'engine integrity check failed: ' + file + ' from ' + url + ' does not match ' + expected
                + '; the browser console names the hash it computed';
        }
        // A fetch or a tag the browser refused fails the same way whether the hash was wrong or the network
        // was: a generic TypeError, or an error event. A plain fetch of the same URL tells them apart; the
        // browser answers it from the cache the refused one filled. Resolves true when the file is there.
        function fileIsServed(url) {
            if (typeof fetch !== 'function') return Promise.resolve(false);
            return fetch(url).then(function (r) { return r.ok; }, function () { return false; });
        }
        // Fetches a file under its known hash. The promise resolves only once the whole body has been
        // received and checked, so the text is never unverified bytes. Resolves the text, or null when the
        // hash did not match; rejects when the file could not be fetched at all.
        function fetchVerified(url, integrity, field) {
            return fetch(url, { integrity: integrity }).then(function (r) {
                if (!r.ok) throw new Error(url + ' -> HTTP ' + r.status);
                return r.text();
            }).then(function (text) {
                telemetry[field] = 'verified';
                return text;
            }, function (e) {
                return fileIsServed(url).then(function (served) {
                    if (!served) throw e;
                    telemetry[field] = 'mismatch';
                    return null;
                });
            });
        }
        function acquireEngine() {
            var tf = now();
            Promise.all([fetchVerified(VIZ_URL, VIZ_INTEGRITY, 'vizIntegrity'), fetchVerified(ENGINE_URL, ENGINE_INTEGRITY, 'engineIntegrity')]).then(function (parts) {
                telemetry.engineFetchMs = Math.round(now() - tf);
                if (mode === 'main-thread') return;
                var viz = parts[0], engine = parts[1];
                if (engine === null) {
                    // No fallback: the script tags would fetch the same bytes and fail the same check.
                    engineFailed(new Error(integrityMessage('plantuml.js', ENGINE_URL, ENGINE_INTEGRITY)));
                    return;
                }
                if (viz === null) {
                    console.error('Kronikol: ' + integrityMessage('viz-global.js', VIZ_URL, VIZ_INTEGRITY)
                        + '. Graphviz is not loaded: the engine lays out what it can with its Smetana port instead.');
                }
                // An ES-module engine build (the npm @plantuml/core line) ends in `export { X as render,
                // Y as renderToString }`; a classic worker cannot evaluate that, so expose the exports instead.
                var tail = engine.slice(-300);
                var em = /export\s*\{\s*([A-Za-z_$][\w$]*)\s+as\s+render\s*,\s*([A-Za-z_$][\w$]*)\s+as\s+renderToString\s*\}\s*;?\s*$/.exec(tail);
                if (em) {
                    esmMode = true;
                    engine = engine.slice(0, engine.length - tail.length + em.index) + 'self.__plantumlExports = { render: ' + em[1] + ', renderToString: ' + em[2] + ' };\n';
                }
                // Graphviz is only used for non-sequence diagrams: a failure there, or a refused file, must
                // not take the sequence diagrams down with it.
                var src = WORKER_HOST_SOURCE
                    + (viz === null ? '' : '\n;try {\n' + viz + '\n} catch (e) { console.error("Kronikol: viz-global failed: " + e); }\n')
                    + ';(function () {\n' + engine + '\n})();\n';
                blobUrl = URL.createObjectURL(new Blob([src], { type: 'application/javascript' }));
                mode = 'worker'; telemetry.mode = 'worker';
                startWorker();
            }).catch(function (e) {
                useMainThreadEngine('engine fetch failed: ' + (e && e.message ? e.message : e));
            });
        }

        // --- main-thread mode (the legacy path) -----------------------------------------------
        function useMainThreadEngine(reason) {
            if (mode === 'main-thread') return;
            mode = 'main-thread'; telemetry.mode = 'main-thread'; telemetry.fallbackReason = reason || null;
            for (var i = 0; i < workers.length; i++) { try { workers[i].w.terminate(); } catch (e) { /* best effort */ } }
            workers = []; telemetry.workers = 0; telemetry.inFlight = 0;
            if (blobUrl) { try { URL.revokeObjectURL(blobUrl); } catch (e) { /* best effort */ } blobUrl = null; }
            // Everything in flight goes back to the front of the queue. Dedupe and prefetch only make
            // sense with workers: fan jobs out to one per target and drop the target-less prefetches.
            var requeue = [];
            var keys = Object.keys(pending);
            for (var k = 0; k < keys.length; k++) requeue.push(pending[keys[k]]);
            pending = {};
            requeue.sort(function (a, b) { return a.seq - b.seq; });
            queue = requeue.concat(queue);
            inflightByKey = {};
            var expanded = [];
            for (var q = 0; q < queue.length; q++) {
                for (var t = 0; t < queue[q].targets.length; t++) expanded.push(newJob(queue[q].key, queue[q].lines, [queue[q].targets[t]]));
            }
            queue = expanded;
            loadEngineScripts();
        }
        function addScript(src, integrity, type) {
            return new Promise(function (resolve, reject) {
                var s = document.createElement('script');
                if (type) s.type = type;
                s.integrity = integrity;
                s.crossOrigin = 'anonymous';
                s.src = src; s.async = false;
                s.onload = function () { resolve(); };
                s.onerror = function () { reject(new Error('failed to load ' + src)); };
                (document.head || document.documentElement).appendChild(s);
            });
        }
        // A tag the browser refused: the file's integrity field and the error to report, told apart by
        // whether the file is served at all.
        function tagFailure(url, integrity, file, field, e) {
            return fileIsServed(url).then(function (served) {
                if (!served) return e;
                telemetry[field] = 'mismatch';
                return new Error(integrityMessage(file, url, integrity));
            });
        }
        function loadEngineScripts() {
            addScript(VIZ_URL, VIZ_INTEGRITY).then(function () {
                telemetry.vizIntegrity = 'verified';
            }, function (e) {
                return tagFailure(VIZ_URL, VIZ_INTEGRITY, 'viz-global.js', 'vizIntegrity', e).then(function (failure) {
                    console.error('Kronikol: ' + failure.message);
                });
            });
            // The engine is an ES module (the npm @plantuml/core line). The module tag fetches and checks
            // it; import() then reuses the record the tag verified, since the module map keys on the URL,
            // and a refused tag leaves a failed entry the import rejects.
            addScript(ENGINE_URL, ENGINE_INTEGRITY, 'module').then(function () {
                telemetry.engineIntegrity = 'verified';
                return import(ENGINE_URL);
            }, function (e) {
                return tagFailure(ENGINE_URL, ENGINE_INTEGRITY, 'plantuml.js', 'engineIntegrity', e).then(function (failure) {
                    throw failure;
                });
            }).then(function (mod) {
                if (!mod || typeof mod.render !== 'function') throw new Error('the engine module has no render export: ' + ENGINE_URL);
                // Stock engine builds refuse diagrams past 8192px by default; Kronikol's limit is 98304px.
                engineRender = function (lines, id) { return mod.render(lines, id, { maxSvgSize: 98304 }); };
                engineReady = true;
                if (telemetry.engineReadyAt === null) telemetry.engineReadyAt = now();
                pumpMain();
            }).catch(function (e) { engineFailed(e); });
        }
        function engineFailed(e) {
            engineError = e;
            var message = 'PlantUML engine unavailable: ' + (e && e.message ? e.message : e);
            telemetry.fallbackReason = (telemetry.fallbackReason ? telemetry.fallbackReason + '; ' : '') + message;
            console.error('Kronikol: ' + message);
            var failed = queue; queue = [];
            for (var i = 0; i < failed.length; i++) {
                for (var t = 0; t < failed[i].targets.length; t++) writeFailure(failed[i].targets[t], failed[i].key, message);
            }
        }
        function pumpMain() {
            if (mainBusy || !engineReady || queue.length === 0) return;
            var job = queue.shift();
            var target = job.targets[0];
            var el = target ? resolveTarget(target) : null;
            if (!el) { pumpMain(); return; }
            mainBusy = true;
            var done = false;
            var mo = new MutationObserver(function () { finish(); });
            var timer = setTimeout(finish, 60000);
            function finish() {
                if (done) return;
                done = true; mainBusy = false;
                clearTimeout(timer); mo.disconnect();
                setTimeout(pumpMain, 0);
            }
            mo.observe(el, { childList: true, subtree: true });
            telemetry.renders++;
            try {
                engineRender(job.lines, target.id || el.id);
            } catch (e) {
                writeFailure(target, job.key, e && e.message ? e.message : e);
                finish();
            }
        }

        // --- the shim -------------------------------------------------------------------------
        function shimRender(lines, id, opts) {
            lines = normalizeLines(lines);
            var key = lines.join('\n');
            var target = { el: id ? document.getElementById(id) : null, id: id || null, opts: opts || null };
            if (engineError) { writeFailure(target, key, 'PlantUML engine unavailable: ' + (engineError.message || engineError)); return; }
            if (workerCapable()) {
                var hit = cacheGet(key);
                if (hit !== undefined) { telemetry.cacheHits++; inject([target], hit); return; }
                var running = inflightByKey[key];
                if (running) { running.targets.push(target); return; }
                var job = newJob(key, lines, [target]);
                inflightByKey[key] = job;
                queue.push(job);
                pumpWorkers();
                return;
            }
            queue.push(newJob(key, lines, [target]));
            pumpMain();
        }
        function shimPrefetch(sources) {
            if (engineError || !workerCapable() || !sources || !sources.length) return;
            for (var i = 0; i < sources.length; i++) {
                var src = sources[i];
                if (typeof src !== 'string' || !src) continue;
                if (cache.has(src) || inflightByKey[src]) continue;
                var job = newJob(src, src.split('\n'), []);
                inflightByKey[src] = job;
                queue.push(job);
            }
            pumpWorkers();
        }
        var shim = {
            render: shimRender,
            prefetch: shimPrefetch,
            get maxParallel() { return (mode === 'main-thread' || targetWorkers === 0) ? 1 : targetWorkers; },
            cacheStats: function () { return { entries: cache.size, bytes: cacheBytes, limit: CACHE_LIMIT, hits: telemetry.cacheHits }; },
            setCacheLimit: function (bytes) { CACHE_LIMIT = Math.max(0, bytes | 0); cacheEvict(); },
            clearCache: function () { cache.clear(); cacheBytes = 0; telemetry.cacheEntries = 0; telemetry.cacheBytes = 0; }
        };
        window.plantuml = shim;
        // The classic engine builds' loader, kept as a no-op for anything outside the shim that still calls
        // it. Nothing in the shim compares against it any more: the engine is an ES module, imported.
        window.plantumlLoad = noop;

        // --- go -------------------------------------------------------------------------------
        function canUseWorkers() {
            if (typeof Worker !== 'function') return 'Worker unavailable';
            if (typeof fetch !== 'function') return 'fetch unavailable';
            if (typeof Blob !== 'function' || typeof URL === 'undefined' || typeof URL.createObjectURL !== 'function') return 'Blob URLs unavailable';
            if (typeof OffscreenCanvas !== 'function') return 'OffscreenCanvas unavailable';
            if (typeof Promise !== 'function' || typeof Map !== 'function') return 'ES2015 unavailable';
            return null;
        }
        if (targetWorkers === 0) {
            useMainThreadEngine('BrowserRenderWorkers = ' + WORKERS_REQUESTED);
        } else {
            var unavailable = canUseWorkers();
            if (unavailable) useMainThreadEngine(unavailable);
            else acquireEngine();
        }
    })();
</script>
<script>
    document.addEventListener('DOMContentLoaded', function() {
        document.body.classList.add('plantuml-ready');
        var renderQueue = [];
        // How many renders are in flight. The shim's maxParallel bounds it (the worker count, or 1 on
        // the main-thread path). window._plantumlRendering stays a boolean ("anything in flight") because
        // collapsible-notes-script.js reads it to keep its own re-renders out of the initial render.
        var inFlight = 0;
        window._plantumlRendering = false;
        function maxParallel() {
            var p = window.plantuml && window.plantuml.maxParallel;
            return (typeof p === 'number' && p > 0) ? p : 1;
        }
        function setInFlight(delta) {
            inFlight = Math.max(0, inFlight + delta);
            window._plantumlRendering = inFlight > 0;
        }
        var _pumlData = null;
        var _maxDiagramHeight = __BROWSER_FRAGMENT_MAX_HEIGHT__;
        var _maxNoteChars = 15000;
        var _estimatedArrowHeight = 45;
        var _estimatedNoteLineHeight = 18;
        window._splitDiagramSource = splitDiagramSource;
        window._chunkLargeNotes = chunkLargeNotes;
        window._countArrows = function(lines) { return countArrows(lines); };
        // Regex arrow detection: matches ->, -->, -[#color]>, -[#color]->
        var _arrowRx = /-(?:\[[^\]]*\])?-?>/;
        // Regex return arrow detection: matches --> and -[#color]->
        var _returnArrowRx = /-(?:\[[^\]]*\])?->/;
        function isArrowLine(trimmed) { return _arrowRx.test(trimmed); }
        function isReturnArrow(trimmed) { return _returnArrowRx.test(trimmed); }
        function getPumlZ(el) {
            if (!_pumlData) {
                var s = document.getElementById('puml-data');
                _pumlData = s ? JSON.parse(s.textContent) : {};
            }
            return _pumlData[el.id] || el.getAttribute('data-plantuml-z') || null;
        }
        window._getPumlZ = getPumlZ;
        function extractIflowMap(source) {
            var map = {};
            var re = /\[\[#(iflow-[^\s\]]+)\s+([^\]]+)\]\]/g;
            var m;
            while ((m = re.exec(source)) !== null) {
                var key = decodeLabelCodePoints(m[2]).split('\\n').join('').replace(/\s+/g, '');
                map[key] = m[1];
            }
            return map;
        }
        // A request label writes captured text PlantUML would act on as code points, a `]` among them (3.30.2), and
        // the painted text holds the characters, so the key does too. A zero-width space is painted as one, so it
        // stays; a code point that is no character stays as written, as the engine leaves it.
        function decodeLabelCodePoints(text) {
            return text.replace(/<U\+([0-9A-Fa-f]{4,6})>/g, function(m, hex) {
                var cp = parseInt(hex, 16);
                if ((cp >= 0xd800 && cp <= 0xdfff) || cp > 0x10ffff) return m;
                return String.fromCodePoint(cp);
            });
        }

        // --- Client-side diagram splitting ---

        // Parse PlantUML source into prefix (header/participants), body lines, and find trace boundaries
        function parseDiagramStructure(source) {
            var lines = source.split('\n');
            var prefixEnd = -1;
            var bodyStart = -1;
            var endumlIdx = lines.length - 1;

            // Find end of prefix: after last participant/actor/entity/database/queue/collections/boundary declaration
            // and after autonumber, skinparam, !pragma, style blocks
            var inStyle = false;
            var _styleOpen = '<' + 'style>';
            var _styleClose = '</' + 'style>';
            for (var i = 0; i < lines.length; i++) {
                var trimmed = lines[i].trim();
                if (trimmed === '@enduml') { endumlIdx = i; break; }
                if (trimmed === _styleOpen) { inStyle = true; continue; }
                if (trimmed === _styleClose) { inStyle = false; prefixEnd = i; continue; }
                if (inStyle) continue;
                if (trimmed === '' || trimmed.startsWith('@startuml') || trimmed.startsWith('!pragma') ||
                    trimmed.startsWith('skinparam') || trimmed.startsWith('autonumber') ||
                    trimmed.startsWith('participant ') || trimmed.startsWith('actor ') ||
                    trimmed.startsWith('entity ') || trimmed.startsWith('database ') ||
                    trimmed.startsWith('queue ') || trimmed.startsWith('collections ') ||
                    trimmed.startsWith('boundary ') || trimmed.startsWith('control ') ||
                    trimmed.startsWith('!theme ')) {
                    prefixEnd = i;
                } else {
                    bodyStart = i;
                    break;
                }
            }

            if (bodyStart < 0) bodyStart = prefixEnd + 1;
            var prefix = lines.slice(0, bodyStart).join('\n');
            var body = lines.slice(bodyStart, endumlIdx).join('\n');
            return { prefix: prefix, body: body, lines: lines, bodyStart: bodyStart, endumlIdx: endumlIdx };
        }

        // Parse body into trace units (a request arrow + notes + response arrow + notes)
        function parseTraceUnits(bodyText) {
            var lines = bodyText.split('\n');
            var units = [];
            var currentUnit = [];
            var currentUnitHasArrow = false;
            var inNote = false;

            for (var i = 0; i < lines.length; i++) {
                var trimmed = lines[i].trim();

                if (trimmed.startsWith('note') && (trimmed.indexOf(' left') >= 0 || trimmed.indexOf(' right') >= 0) && !trimmed.startsWith('note over')) {
                    inNote = true;
                    currentUnit.push(lines[i]);
                } else if (trimmed === 'end note') {
                    inNote = false;
                    currentUnit.push(lines[i]);
                } else if (inNote) {
                    currentUnit.push(lines[i]);
                } else if (isArrowLine(trimmed)) {
                    // Arrow line — this starts a new trace unit if we have response from previous
                    // Heuristic: arrows with -> (request) or --> (return) alternate
                    // Start new unit on request arrows (not return arrows). A unit that so far holds
                    // only a block opener (`loop …`, `alt …`, `partition …`) is kept: the opener belongs
                    // with the pair it wraps, never with the previous unit.
                    var isReturn = isReturnArrow(trimmed);
                    if (!isReturn && currentUnitHasArrow) {
                        units.push(currentUnit);
                        currentUnit = [];
                        currentUnitHasArrow = false;
                    }
                    currentUnit.push(lines[i]);
                    currentUnitHasArrow = true;
                } else if (isBlockOpener(trimmed)) {
                    // Block opener (loop/alt/opt/group/par/partition) — starts a fresh unit so a split
                    // boundary never separates it from the first pair inside it
                    if (currentUnitHasArrow) {
                        units.push(currentUnit);
                        currentUnit = [];
                        currentUnitHasArrow = false;
                    }
                    currentUnit.push(lines[i]);
                } else if (trimmed === 'end') {
                    // Block close — attach to current unit (the last pair inside the block)
                    currentUnit.push(lines[i]);
                } else {
                    currentUnit.push(lines[i]);
                }
            }
            if (currentUnit.length > 0) units.push(currentUnit);
            return units;
        }

        // `partition X`, `loop ×3 · 12–40 ms`, `alt …`, `opt …`, `group …`, `par …`, `critical …` — the
        // block statements that are closed by a bare `end` and must be closed/re-opened when a diagram
        // is split into height-bounded fragments (the server-side builder does the same).
        var _blockOpenerRx = /^(partition\s|loop\b|alt\b|opt\b|group\b|par\b|critical\b|break\b)/;
        function isBlockOpener(trimmed) { return _blockOpenerRx.test(trimmed); }
        window._isBlockOpener = isBlockOpener;

        // Estimate height of a trace unit
        function estimateUnitHeight(unitLines) {
            var height = 0;
            var inNote = false;
            for (var i = 0; i < unitLines.length; i++) {
                var trimmed = unitLines[i].trim();
                if (isArrowLine(trimmed)) {
                    height += _estimatedArrowHeight;
                } else if (trimmed.startsWith('note') && (trimmed.indexOf(' left') >= 0 || trimmed.indexOf(' right') >= 0)) {
                    inNote = true;
                    height += _estimatedArrowHeight; // note header
                } else if (trimmed === 'end note') {
                    inNote = false;
                } else if (inNote) {
                    height += _estimatedNoteLineHeight;
                }
            }
            return height;
        }

        // Split diagram source into fragments based on estimated height
        function splitDiagramSource(source, maxHeight) {
            if (!maxHeight) maxHeight = _maxDiagramHeight;
            var structure = parseDiagramStructure(source);
            if (!structure.body.trim()) return [source];

            var units = parseTraceUnits(structure.body);
            if (units.length === 0) return [source];

            var fragments = [];
            var currentLines = [];
            var currentHeight = 0;
            var stepCount = 0;
            // Blocks (partition / loop / alt / …) open at the current position. A fragment boundary
            // inside a block closes it (`end`) and the next fragment re-opens it with the same line,
            // so every fragment is a complete diagram — a stranded `end` or a never-closed `loop` is
            // a PlantUML syntax error.
            var openBlocks = [];

            // Extract the autonumber start from prefix
            var autoMatch = structure.prefix.match(/autonumber\s+(\d+)/);
            var baseStep = autoMatch ? parseInt(autoMatch[1], 10) : 1;

            for (var u = 0; u < units.length; u++) {
                var unitHeight = estimateUnitHeight(units[u]);

                // If adding this unit exceeds max and we have content, split here
                if (currentHeight > 0 && currentHeight + unitHeight > maxHeight) {
                    for (var ob = openBlocks.length - 1; ob >= 0; ob--) currentLines.push('end');
                    fragments.push({ lines: currentLines, startStep: baseStep + stepCount - countArrows(currentLines) });
                    currentLines = [];
                    currentHeight = 0;
                    for (var rb = 0; rb < openBlocks.length; rb++) currentLines.push(openBlocks[rb]);
                }

                // Track block state through this unit
                var inNoteTrack = false;
                for (var li = 0; li < units[u].length; li++) {
                    var t = units[u][li].trim();
                    if (!inNoteTrack && t.startsWith('note') && (t.indexOf(' left') >= 0 || t.indexOf(' right') >= 0 || t.indexOf(' over') >= 0) && t.indexOf(':') < 0) { inNoteTrack = true; continue; }
                    if (inNoteTrack) { if (t === 'end note') inNoteTrack = false; continue; }
                    if (isBlockOpener(t)) openBlocks.push(units[u][li]);
                    else if (t === 'end' && openBlocks.length > 0) openBlocks.pop();
                }

                for (var li2 = 0; li2 < units[u].length; li2++) {
                    currentLines.push(units[u][li2]);
                }
                currentHeight += unitHeight;
                stepCount += countArrowsInUnit(units[u]);
            }

            // Final fragment
            if (currentLines.length > 0) {
                for (var fb = openBlocks.length - 1; fb >= 0; fb--) currentLines.push('end');
                fragments.push({ lines: currentLines, startStep: baseStep + stepCount - countArrowsInLines(currentLines) });
            }

            if (fragments.length <= 1) return [source];

            // Build complete PlantUML sources for each fragment
            var result = [];
            var cumulativeSteps = baseStep;
            for (var f = 0; f < fragments.length; f++) {
                var fragPrefix = structure.prefix.replace(/autonumber\s+\d+/, 'autonumber ' + cumulativeSteps);
                result.push(fragPrefix + '\n' + fragments[f].lines.join('\n') + '\n@enduml');
                cumulativeSteps += countArrowsInLines(fragments[f].lines);
            }
            return result;
        }

        function countArrows(lines) {
            var c = 0;
            for (var i = 0; i < lines.length; i++) {
                var t = lines[i].trim();
                if (isArrowLine(t) && !t.startsWith('note') && !t.startsWith('end note')) c++;
            }
            return c;
        }
        function countArrowsInUnit(unitLines) {
            var c = 0;
            for (var i = 0; i < unitLines.length; i++) {
                var t = unitLines[i].trim();
                if (isArrowLine(t) && !t.startsWith('note')) c++;
            }
            return c;
        }
        function countArrowsInLines(lines) {
            return countArrows(lines);
        }

        // Chunk large notes in PlantUML source — returns modified source with forced split markers
        function chunkLargeNotes(source, maxChars) {
            if (!maxChars) maxChars = _maxNoteChars;
            var lines = source.split('\n');
            var result = [];
            var inNote = false;
            var noteLines = [];
            var noteHeader = '';

            for (var i = 0; i < lines.length; i++) {
                var trimmed = lines[i].trim();
                if (!inNote && (trimmed.startsWith('note') && (trimmed.indexOf(' left') >= 0 || trimmed.indexOf(' right') >= 0) && !trimmed.startsWith('note over'))) {
                    inNote = true;
                    noteHeader = lines[i];
                    noteLines = [];
                } else if (inNote && trimmed === 'end note') {
                    inNote = false;
                    var noteContent = noteLines.join('\n');
                    if (noteContent.length > maxChars) {
                        // Find the last arrow before this note to determine the anchor participant
                        var anchorParticipant = '';
                        var noteDir = /\bright\b/.test(noteHeader) ? 'right' : 'left';
                        for (var ra = result.length - 1; ra >= 0; ra--) {
                            if (isArrowLine(result[ra].trim())) {
                                var am = result[ra].match(/^\s*(\S+)\s+.*?>\s*([^\s:]+)/);
                                if (am) {
                                    // 'note right' anchors to target; 'note left' anchors to source
                                    anchorParticipant = noteDir === 'right' ? am[2] : am[1];
                                }
                                break;
                            }
                        }
                        // Chunk the note content
                        var chunks = chunkString(noteContent, maxChars);
                        for (var ci = 0; ci < chunks.length; ci++) {
                            var chunk = chunks[ci];
                            if (ci > 0) chunk = '..Continued From Previous Diagram..\n' + chunk;
                            if (ci < chunks.length - 1) chunk = chunk + '\n..Continued On Next Diagram..';
                            // For continuation chunks, anchor note to participant so
                            // PlantUML renders it even without a preceding message
                            if (ci > 0 && anchorParticipant) {
                                result.push(noteHeader.replace(/\b(left|right)\b(?!\s+of\b)/, '$1 of ' + anchorParticipant));
                            } else {
                                result.push(noteHeader);
                            }
                            var chunkLines = chunk.split('\n');
                            for (var cl = 0; cl < chunkLines.length; cl++) result.push(chunkLines[cl]);
                            result.push('end note');
                            if (ci < chunks.length - 1) {
                                result.push('== __SPLIT_BOUNDARY__ ==');
                            }
                        }
                    } else {
                        result.push(noteHeader);
                        for (var nl = 0; nl < noteLines.length; nl++) result.push(noteLines[nl]);
                        result.push('end note');
                    }
                } else if (inNote) {
                    noteLines.push(lines[i]);
                } else {
                    result.push(lines[i]);
                }
            }
            return result.join('\n');
        }

        function chunkString(str, maxLen) {
            var chunks = [];
            var lines = str.split('\n');
            var current = '';
            for (var i = 0; i < lines.length; i++) {
                var candidate = current ? current + '\n' + lines[i] : lines[i];
                if (candidate.length > maxLen && current.length > 0) {
                    chunks.push(current);
                    current = lines[i];
                } else {
                    current = candidate;
                }
            }
            if (current) chunks.push(current);
            return chunks.length > 0 ? chunks : [str];
        }

        // Enhanced split that handles forced split boundaries from chunkLargeNotes
        // Walk lines (skipping note bodies) and push block openers / pop on bare `end`, so a caller
        // knows which blocks are still open at the end of a chunk of diagram text.
        function scanOpenBlocks(lines, stack) {
            var inNote = false;
            for (var i = 0; i < lines.length; i++) {
                var t = lines[i].trim();
                if (!inNote && /^h?note\b/.test(t) && t.indexOf(':') < 0) { inNote = true; continue; }
                if (inNote) { if (t === 'end note' || t === 'endhnote' || t === 'endrnote') inNote = false; continue; }
                if (isBlockOpener(t)) stack.push(lines[i]);
                else if (t === 'end' && stack.length > 0) stack.pop();
            }
            return stack;
        }

        function splitWithChunkedNotes(source, maxHeight) {
            // First chunk any oversized notes
            var chunked = chunkLargeNotes(source, _maxNoteChars);
            // Check for forced split boundaries
            if (chunked.indexOf('__SPLIT_BOUNDARY__') >= 0) {
                var parts = chunked.split(/\n== __SPLIT_BOUNDARY__ ==\n/);
                var allFragments = [];
                // Blocks (loop/alt/partition) open when a forced boundary cut the text: the part
                // after the boundary re-opens them, the part before closes them, so each part is a
                // complete diagram before it is split further by height.
                var carried = [];
                for (var p = 0; p < parts.length; p++) {
                    // Each part gets wrapped as complete PlantUML and further split by height
                    var partSource = parts[p].trim();
                    // Whether the part has its own header or footer is read off a whole line: a captured
                    // payload can quote either word mid-line (a JSON string holding a diagram), and until
                    // 3.30.1 such a part was taken for one that had its header and was drawn without it.
                    var hasStart = /^@startuml\b/m.test(partSource);
                    if (carried.length > 0) {
                        var reopen = carried.join('\n');
                        if (!hasStart) partSource = reopen + '\n' + partSource;
                    }
                    var openAfter = scanOpenBlocks(partSource.split('\n'), []);
                    if (openAfter.length > 0) {
                        var closers = '';
                        for (var oc = 0; oc < openAfter.length; oc++) closers += '\nend';
                        partSource = /^@enduml\s*$/m.test(partSource)
                            ? partSource.replace(/\n@enduml\s*$/, closers + '\n@enduml')
                            : partSource + closers;
                    }
                    carried = openAfter;
                    // Ensure it has @startuml/@enduml
                    if (!hasStart) {
                        var structure = parseDiagramStructure(source);
                        // Count steps from previous fragments
                        var prevSteps = 1;
                        for (var pf = 0; pf < allFragments.length; pf++) {
                            prevSteps += countArrows(allFragments[pf].split('\n'));
                        }
                        partSource = structure.prefix.replace(/autonumber\s+\d+/, 'autonumber ' + prevSteps) + '\n' + partSource + '\n@enduml';
                    } else if (!/^@enduml\s*$/m.test(partSource)) {
                        // Part has @startuml but no @enduml (first part in chunked split).
                        // Without @enduml, parseDiagramStructure treats the last line as
                        // the end marker and excludes it from the body, which breaks
                        // note blocks whose 'end note' happens to be on the last line.
                        partSource = partSource + '\n@enduml';
                    }
                    var heightFrags = splitDiagramSource(partSource, maxHeight);
                    for (var hf = 0; hf < heightFrags.length; hf++) {
                        allFragments.push(heightFrags[hf]);
                    }
                }
                return allFragments;
            }
            // No forced boundaries — just split by height
            return splitDiagramSource(chunked, maxHeight);
        }
        window._splitWithChunkedNotes = splitWithChunkedNotes;

        // Render fragments into a container, creating child divs as needed
        function renderFragments(el, source) {
            var fragments = splitWithChunkedNotes(source);
            el._fragments = fragments;
            el._fullSource = source;

            if (fragments.length <= 1) {
                // Single fragment — render directly into container (existing behavior)
                renderQueue.push({ el: el, source: fragments[0] || source, isFragment: false });
            } else {
                // Multiple fragments — create child divs
                el.innerHTML = '';
                el.dataset.rendered = '1';
                for (var f = 0; f < fragments.length; f++) {
                    var fragDiv = document.createElement('div');
                    fragDiv.className = 'puml-fragment';
                    fragDiv.id = el.id + '-frag-' + f;
                    fragDiv.dataset.fragment = f;
                    fragDiv.setAttribute('data-plantuml', fragments[f]);
                    el.appendChild(fragDiv);
                    renderQueue.push({ el: fragDiv, source: fragments[f], isFragment: true, parentEl: el });
                }
            }
            processQueue();
        }

        // Header-only lines: what a diagram is made of once every arrow, participant and note has been
        // filtered away (assertion notes / step bars hidden). plantuml.js cannot draw an empty body —
        // it answers "Syntax Error? (Assumed diagram type: class)" — so such a fragment is shown as a
        // note instead of being sent to the engine.
        var _headerOnlyRx = /^(@startuml|@enduml|!pragma\b|skinparam\b|autonumber\b|hide\b|scale\b|title\b)/;
        // The one participant the generator declares for a markers-only diagram (so `hnote across` has a
        // lifeline to span — see PlantUmlCreator.MarkerOnlyParticipant); on its own it is nothing to draw.
        var _markerOnlyParticipantRx = /^participant "\(no interactions\)" as noInteractions$/;
        // plantuml.js reports two failures by WRITING into the target instead of throwing:
        // "java.lang.RuntimeException: Diagram too large for browser rendering: WxH (max N)" as plain
        // text (a note wider than the engine's canvas — no <svg> is produced), and "Syntax Error?" as
        // an error image. Neither must wedge a render queue that waits for an <svg>, and both must stay
        // diagnosable: the too-large case becomes a legible message, and every failure keeps the raw
        // PlantUML source reachable in a <details>. Returns true when the element holds a failure.
        var _engineTooLargeRx = /Diagram too large for browser rendering/;
        var _engineSyntaxErrorRx = /Syntax Error\?/;
        // The engine loads OpenIconic (`<&name>`) and emoji (`<:name:>`) by script tag, which the render
        // worker answers with an error at once (plantuml-worker-host.js); the engine then writes
        // "java.lang.RuntimeException: Failed to load openiconic.js" (or emoji.js) as the diagram's text.
        // Kronikol escapes both forms in everything it copies in, so this is a user's own markup, or a
        // source merged from a report written before 3.29.6.
        var _engineBundleLoadRx = /Failed to load (openiconic|emoji)\.js/;

        // The engine's measured statement-length limits (PlantUmlStatementLimits, C# side). A statement
        // past its limit matches no parse rule, so the parser gives up on the entire diagram and draws
        // "Syntax Error?" with no hint at what was wrong. Naming the offending line here turns a
        // recurrence into a one-glance diagnosis.
        var _arrowRx = /<{1,2}[-=.]{1,2}(?:\[[^\]]*\])?[-=.]{0,2}|[-=.]{1,2}(?:\[[^\]]*\])?[-=.]{0,2}>{1,2}/;
        var _blockOpenerRx = /^(loop|alt|else|opt|group|par|critical|break|partition|also)\b/i;
        var _noteStartRx = /^[hrn]?note\b/i;
        var _noteEndRx = /^end\s*[hrn]?note$/i;
        function findOverLongStatement(source) {
            var lines = String(source).split('\n');
            var noteDepth = 0;
            for (var i = 0; i < lines.length; i++) {
                var t = lines[i].replace(/\r$/, '').trim();
                if (noteDepth > 0) {
                    if (_noteEndRx.test(t)) noteDepth--;
                    continue;
                }
                if (!t || t[0] === "'" || t[0] === '!' || t[0] === '@') continue;
                if (_noteStartRx.test(t)) {
                    var stripped = t.replace(/<<[^>]*>>/g, '');
                    var colon = stripped.indexOf(':'), angle = stripped.indexOf('<');
                    var singleLine = colon >= 0 && (angle < 0 || colon < angle);
                    if (!singleLine) noteDepth++;
                    continue;
                }
                if (_blockOpenerRx.test(t)) {
                    if (t.length > 1471) return { line: i + 1, kind: 'block label', length: t.length, limit: 1471 };
                    continue;
                }
                var m = _arrowRx.exec(t);
                if (m && t.indexOf(':', m.index + m[0].length) >= 0 && t.length > 2000)
                    return { line: i + 1, kind: 'message statement', length: t.length, limit: 2000 };
            }
            return null;
        }
        window._findOverLongStatement = findOverLongStatement;
        // Source text shown inside markup: `&` first, or a `&lt;`, `&copy` or `&amp;` in the source would
        // be read by the page and shown as something else.
        function escapeMarkupText(s) { return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;'); }
        function describeEngineFailure(el, source) {
            if (!el) return false;
            var text = el.textContent || '';
            var svg = el.querySelector('svg');
            var rawDetails = '<details style="margin-top:0.5em"><summary>Raw PlantUML</summary><pre style="white-space:pre-wrap">'
                + escapeMarkupText(source || el.getAttribute('data-plantuml') || '') + '</pre></details>';
            if (!svg && _engineTooLargeRx.test(text)) {
                el.innerHTML = '<div class="engine-failure" data-engine-failure="too-large" style="color:#c00;padding:1em;border:1px solid #c00;border-radius:6px;margin:0.5em 0;">'
                    + '<strong>Diagram too large for client-side rendering.</strong> '
                    + 'One note is wider than the engine can draw — usually a single very long unbreakable line in a captured body. '
                    + '<code>' + escapeMarkupText(text.slice(0, 200)) + '</code>'
                    + rawDetails + '</div>';
                return true;
            }
            var bundle = !svg && _engineBundleLoadRx.exec(text);
            if (bundle) {
                el.innerHTML = '<div class="engine-failure" data-engine-failure="loader" style="color:#c00;padding:1em;border:1px solid #c00;border-radius:6px;margin:0.5em 0;">'
                    + '<strong>This diagram uses PlantUML ' + (bundle[1] === 'emoji' ? 'emoji' : 'icons') + ', which this report\'s renderer does not load.</strong> '
                    + 'OpenIconic icons (<code>&lt;&amp;name&gt;</code>) and emoji (<code>&lt;:name:&gt;</code>) need a bundle ('
                    + bundle[1] + '.js) that the engine in the page cannot fetch. <code>PlantUmlRendering.Server</code> and <code>PlantUmlRendering.Local</code> draw them.'
                    + rawDetails + '</div>';
                return true;
            }
            if (svg && _engineSyntaxErrorRx.test(text) && !el.querySelector('[data-engine-failure]')) {
                var note = document.createElement('div');
                note.className = 'engine-failure';
                note.setAttribute('data-engine-failure', 'syntax');
                var overLong = findOverLongStatement(source || el.getAttribute('data-plantuml') || '');
                note.innerHTML = (overLong
                    ? '<div style="color:#c00;margin-bottom:0.5em"><strong>A statement is longer than the engine parses.</strong> '
                      + 'Line ' + overLong.line + ' is a ' + overLong.kind + ' of ' + overLong.length + ' characters (limit ' + overLong.limit + '). '
                      + 'The parser reports nothing for this — it abandons the whole diagram, which is why every other statement in this fragment is gone too.</div>'
                    : '') + rawDetails;
                el.appendChild(note);
                return true;
            }
            return false;
        }
        window._describeEngineFailure = describeEngineFailure;

        function hasDrawableBody(lines) {
            var inStyle = false;
            for (var i = 0; i < lines.length; i++) {
                var t = lines[i].trim();
                if (!t) continue;
                if (/^<\/?style>$/i.test(t)) { inStyle = t.charAt(1) !== '/'; continue; }
                if (inStyle) continue;
                if (_headerOnlyRx.test(t)) continue;
                if (_markerOnlyParticipantRx.test(t)) continue;
                return true;
            }
            return false;
        }
        window._hasDrawableBody = hasDrawableBody;

        function processQueue() {
            if (inFlight >= maxParallel() || renderQueue.length === 0) return;
            var item = renderQueue.shift();
            var lines = item.source.split('\n');
            var queueDone = false;
            if (!hasDrawableBody(lines)) {
                item.el.innerHTML = '<div class="no-interactions" data-nothing-to-draw="true">Nothing to draw with the current filters — this diagram is only assertion notes and/or step bars (use Assertions Shown / Steps Shown to see them).</div>';
                item.el.dataset.rendered = '1';
                processQueue();
                return;
            }
            setInFlight(1);
            function onQueueItemDone() {
                if (queueDone) return;
                queueDone = true;
                clearInterval(qPoll);
                item.el.dataset.rendered = '1';
                var hookTarget = item.isFragment ? item.el : item.el;
                var iflowSource = item.parentEl ? item.parentEl._fullSource || item.source : item.source;
                try {
                    describeEngineFailure(item.el, item.source);
                    bindIflowLinks(hookTarget, iflowSource);
                    if (window._makeNotesCollapsible) window._makeNotesCollapsible(hookTarget);
                    if (window._addAssertionTooltips) window._addAssertionTooltips(hookTarget);
                    requestAnimationFrame(function() { if (window._addZoomButton) window._addZoomButton(hookTarget); });
                } catch(hookErr) { console.error('Post-render hook error:', hookErr); }
                setInFlight(-1);
                processQueue();
            }
            var mo = new MutationObserver(function() {
                mo.disconnect();
                onQueueItemDone();
            });
            mo.observe(item.el, { childList: true, subtree: true });
            // Timeout: if nothing has been produced within 60s, give this item up and continue
            var qPollCount = 0;
            var qPoll = setInterval(function() {
                qPollCount++;
                if (queueDone) { clearInterval(qPoll); return; }
                if (qPollCount > 240) { clearInterval(qPoll); mo.disconnect(); queueDone = true; setInFlight(-1); processQueue(); }
            }, 250);
            // A failure — thrown synchronously by a main-thread engine, or reported asynchronously by a
            // worker through the shim's onError — takes one path: the too-large re-split retry, or the
            // failure markup with the raw PlantUML. Returns true: the element has been taken care of.
            function handleRenderFailure(msg) {
                if (queueDone) return true;
                queueDone = true;
                clearInterval(qPoll);
                mo.disconnect();
                item.el.dataset.rendered = '1';
                setInFlight(-1);
                msg = String(msg == null ? '' : msg);
                if (msg.indexOf('too large') >= 0) {
                    // Try re-splitting with a smaller max height
                    if (!item._retried && !item.isFragment) {
                        item._retried = true;
                        var smallerFrags = splitWithChunkedNotes(item.source, _maxDiagramHeight / 2);
                        if (smallerFrags.length > 1) {
                            item.el.innerHTML = '';
                            item.el.dataset.rendered = '1';
                            for (var rf = 0; rf < smallerFrags.length; rf++) {
                                var rDiv = document.createElement('div');
                                rDiv.className = 'puml-fragment';
                                rDiv.id = item.el.id + '-frag-' + rf;
                                rDiv.dataset.fragment = rf;
                                rDiv.setAttribute('data-plantuml', smallerFrags[rf]);
                                item.el.appendChild(rDiv);
                                renderQueue.unshift({ el: rDiv, source: smallerFrags[rf], isFragment: true, parentEl: item.el, _retried: true });
                            }
                            processQueue();
                            return true;
                        }
                    }
                    item.el.innerHTML = '<div style="color:#c00;padding:1em;border:1px solid #c00;border-radius:6px;margin:0.5em 0;">'
                        + '<strong>Diagram too large for client-side rendering.</strong><br>'
                        + 'Use <code>PlantUmlRendering.Server</code> or <code>PlantUmlRendering.Local</code> for large diagrams.'
                        + '<details style="margin-top:0.5em"><summary>Raw PlantUML</summary><pre style="white-space:pre-wrap">'
                        + escapeMarkupText(item.source) + '</pre></details></div>';
                } else {
                    item.el.textContent = 'Render error: ' + msg;
                }
                processQueue();
                return true;
            }
            try {
                window.plantuml.render(lines, item.el.id, { onError: handleRenderFailure });
                // Fill every free slot (the worker count) rather than waiting for this render to finish.
                setTimeout(processQueue, 0);
            } catch(e) {
                handleRenderFailure((e && e.message) ? e.message : String(e));
            }
        }
        window._iflowBindLinks = function(container, source) { bindIflowLinks(container, source); };
        function bindIflowLinks(container, source) {
            if (!container) return;
            var iflowData = window.__iflowSegments || {};
            var config = window.__iflowConfig || {};
            var hoverOnly = config.hasDataBehavior === 'showLinkOnHover';
            var bound = 0;
            container.querySelectorAll('a').forEach(function(a) {
                var href = a.getAttribute('xlink:href') || a.getAttribute('href') || '';
                if (href.indexOf('#iflow-') !== 0) return;
                var segId = href.substring(1);
                if (!iflowData[segId]) return;
                if (hoverOnly) {
                    a.removeAttribute('xlink:href');
                    a.removeAttribute('href');
                    a.classList.add('iflow-link-hover');
                } else {
                    a.style.cursor = 'pointer';
                }
                a.addEventListener('click', function(ev) {
                    ev.preventDefault();
                    ev.stopPropagation();
                    if (window._iflowShowPopup) window._iflowShowPopup(segId);
                });
                bound++;
            });
            if (bound > 0) return;
            if (!source) return;
            var iflowMap = extractIflowMap(source);
            if (Object.keys(iflowMap).length === 0) return;
            // Link text is found by the engine's link colour. Nothing is recoloured until a group of it is known
            // to be one of Kronikol's links: blue text no [[#iflow-…]] markup names (a FocusEmphasis.Colored field,
            // a hyperlink in a payload) keeps the colour it was painted in (DIAGRAM_COLOURS_PLAN S2).
            var allTexts = Array.from(container.querySelectorAll('text'));
            function isLinkFill(fill) { return (fill || '').toLowerCase() === '#0000ff'; }
            var blueIndices = [];
            allTexts.forEach(function(t, idx) {
                if (isLinkFill(t.getAttribute('fill'))) blueIndices.push(idx);
            });
            // A link rests in the ink of the text around it: the nearest earlier text that is not link-coloured,
            // else the ink most of the diagram's text uses, else black. On the default theme every branch answers
            // #000000; under a theme whose text is light, the link stays readable.
            var commonInk = null;
            function mostCommonInk() {
                if (commonInk !== null) return commonInk;
                var counts = {};
                commonInk = '#000000';
                var best = 0;
                allTexts.forEach(function(t) {
                    var fill = t.getAttribute('fill');
                    if (!fill || isLinkFill(fill)) return;
                    counts[fill] = (counts[fill] || 0) + 1;
                    if (counts[fill] > best) { best = counts[fill]; commonInk = fill; }
                });
                return commonInk;
            }
            function restFillBefore(index) {
                for (var i = index - 1; i >= 0; i--) {
                    var fill = allTexts[i].getAttribute('fill');
                    if (fill && !isLinkFill(fill)) return fill;
                }
                return mostCommonInk();
            }
            var groups = [];
            var curGrp = [];
            var sorted = blueIndices;
            for (var gi = 0; gi < sorted.length; gi++) {
                if (curGrp.length === 0 || sorted[gi] === curGrp[curGrp.length - 1] + 1) {
                    curGrp.push(sorted[gi]);
                } else {
                    groups.push(curGrp);
                    curGrp = [sorted[gi]];
                }
            }
            if (curGrp.length > 0) groups.push(curGrp);
            groups.forEach(function(group) {
                var combined = group.map(function(idx) { return allTexts[idx].textContent; }).join('');
                var key = combined.replace(/\s+/g, '');
                var segId = iflowMap[key] || null;
                if (!segId) return; // not Kronikol's markup: left exactly as painted
                var groupEls = group.map(function(idx) { return allTexts[idx]; });
                // The highlight is the fill the engine painted the link in, so a theme's link colour survives.
                var linkFills = groupEls.map(function(el) { return el.getAttribute('fill'); });
                var rest = restFillBefore(group[0]);
                function atRest() {
                    groupEls.forEach(function(el) {
                        el.setAttribute('fill', rest);
                        el.removeAttribute('text-decoration');
                    });
                }
                if (!iflowData[segId]) {
                    // Kronikol wrote the link, but its popup would be empty: it must not look like one.
                    atRest();
                    return;
                }
                groupEls.forEach(function(textEl) {
                    textEl.style.pointerEvents = 'all';
                    if (hoverOnly) {
                        textEl.setAttribute('fill', rest);
                        textEl.removeAttribute('text-decoration');
                        textEl.style.cursor = 'default';
                        textEl.addEventListener('mouseenter', function() {
                            groupEls.forEach(function(el, i) {
                                el.setAttribute('fill', linkFills[i]);
                                el.setAttribute('text-decoration', 'underline');
                                el.style.cursor = 'pointer';
                            });
                        });
                        textEl.addEventListener('mouseleave', function() {
                            atRest();
                            groupEls.forEach(function(el) { el.style.cursor = 'default'; });
                        });
                    } else {
                        textEl.setAttribute('text-decoration', 'underline');
                        textEl.style.cursor = 'pointer';
                    }
                    textEl.addEventListener('click', function(ev) {
                        ev.preventDefault();
                        ev.stopPropagation();
                        if (window._iflowShowPopup) window._iflowShowPopup(segId);
                    });
                });
                bound++;
            });
        }
        function enqueueElement(el) {
            var source = el.getAttribute('data-plantuml');
            if (source) {
                if (window._preProcessSource) source = window._preProcessSource(el, source);
                el.setAttribute('data-plantuml', source);
                renderFragments(el, source);
            } else {
                var pumlZ = getPumlZ(el);
                if (pumlZ) {
                    decompressGzipBase64(pumlZ).then(function(decoded) {
                        el.setAttribute('data-plantuml', decoded);
                        var src = decoded;
                        if (window._preProcessSource) src = window._preProcessSource(el, decoded);
                        el.setAttribute('data-plantuml', src);
                        renderFragments(el, src);
                    }).catch(function() { el.textContent = 'Decompression error'; });
                }
            }
        }
        var observer = new IntersectionObserver(function(entries) {
            entries.forEach(function(entry) {
                if (!entry.isIntersecting) return;
                var el = entry.target;
                if (el.dataset.queued) return;
                el.dataset.queued = '1';
                observer.unobserve(el);
                enqueueElement(el);
            });
        }, { rootMargin: '200px' });
        // decompressGzipBase64 comes from the always-included shared helper
        // (report-decompress-helper.js).
        window._renderDiagramsInContainer = function(container) {
            if (!container) return;
            container.querySelectorAll('.plantuml-browser').forEach(function(el) {
                if (el.dataset.queued) return;
                el.dataset.queued = '1';
                observer.unobserve(el);
                enqueueElement(el);
            });
        };
        document.querySelectorAll('.plantuml-browser').forEach(function(el) {
            observer.observe(el);
        });
        // Preload first scenario's diagrams immediately
        var firstScenario = document.querySelector('.scenario');
        if (firstScenario) {
            firstScenario.querySelectorAll('.plantuml-browser').forEach(function(el) {
                if (el.dataset.queued) return;
                el.dataset.queued = '1';
                observer.unobserve(el);
                enqueueElement(el);
            });
            // Also render first scenario's flame charts
            if (window._renderFlameCharts) window._renderFlameCharts(firstScenario);
        }
    });
</script>
