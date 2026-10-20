# The report page draws with PlantUML's Wasm GC engine where the browser can run it, and with the JavaScript engine of the
# same build everywhere else; an engine that verifies but does not start gives way to the JavaScript one; a worker that an
# error reaches while it draws is replaced, its diagram drawn again once and failed on a second crash (5.0.0,
# plans/WASM_ENGINE_PLAN.md). usage: python -I shim-two-engines.py <repo root>
import sys

root = sys.argv[1]


def edit(rel, pairs):
    p = root + '/' + rel
    t = open(p, 'rb').read().decode('utf-8')
    assert '\r' not in t
    for old, new in pairs:
        assert t.count(old) == 1, (rel, old[:90], t.count(old))
        t = t.replace(old, new)
    open(p, 'wb').write(t.encode('utf-8'))


edit('src/Kronikol/Reports/plantuml-browser-render-script.js', [
    ("""    //       engineIntegrity, vizIntegrity, webAssembly }. `errors` counts the engine's stack failures too (since
    //       4.14.6): its RangeError text or picture, which is described in the page and never cached.
""",
     """    //       engineIntegrity, vizIntegrity, webAssembly, engine, engineProblem, workerCrashes }. `errors` counts the
    //       engine's stack failures too (since 4.14.6): its RangeError text or picture, which is described in the page
    //       and never cached. `engine` is 'wasm' or 'js' (5.0.0), `engineProblem` says why the page is not on the Wasm
    //       engine (null while it is), and `workerCrashes` counts the workers an error took down while they drew.
"""),
    ("""    // where a module compiles; `webAssembly` in the telemetry says which, and the console says why.
""",
     """    // where a module compiles; `webAssembly` in the telemetry says which, and the console says why.
    //
    // The engine (5.0.0, plans/WASM_ENGINE_PLAN.md) is PlantUML's Wasm GC build wherever the browser runs Wasm GC and
    // lets a script build functions from text, and plantuml.js of the same build everywhere else; the two draw the same
    // SVG. The Wasm build comes packed into one script, plantuml-wasm-bundle.js, which inlines into the worker Blob as
    // plantuml.js does. A Wasm engine that passes its hash check and then does not start gives way to plantuml.js, and
    // a worker that an error reaches while it draws is replaced, its diagram drawn once more and then failed.
"""),
    ("""        var VIZ_URL = '__PLANTUML_CDN_BASE__/viz-global.js';
        var ENGINE_URL = '__PLANTUML_CDN_BASE__/plantuml.js';
        var VIZ_INTEGRITY = '__PLANTUML_VIZ_INTEGRITY__';
        var ENGINE_INTEGRITY = '__PLANTUML_ENGINE_INTEGRITY__';
""",
     """        var VIZ_URL = '__PLANTUML_CDN_BASE__/viz-global.js';
        var JS_ENGINE_URL = '__PLANTUML_CDN_BASE__/plantuml.js';
        var WASM_ENGINE_URL = '__PLANTUML_CDN_BASE__/plantuml-wasm-bundle.js';
        var VIZ_INTEGRITY = '__PLANTUML_VIZ_INTEGRITY__';
        var JS_ENGINE_INTEGRITY = '__PLANTUML_ENGINE_INTEGRITY__';
        var WASM_ENGINE_INTEGRITY = '__PLANTUML_WASM_ENGINE_INTEGRITY__';
        // The engine the page draws with (useEngine): PlantUML's Wasm GC build where it can run here, and the JavaScript
        // build of the same commit everywhere else; the two draw the same SVG (5.0.0, plans/WASM_ENGINE_PLAN.md).
        var ENGINE_URL, ENGINE_INTEGRITY, ENGINE_FILE;
"""),
    ("""        var WEBASSEMBLY_PROBLEM = webAssemblyUnavailable();
""",
     """        var WEBASSEMBLY_PROBLEM = webAssemblyUnavailable();
        // Why the Wasm GC engine cannot run here, or null when it can.
        var WASM_ENGINE_PROBLEM = wasmEngineUnavailable();
"""),
    ("""            engineIntegrity: null, vizIntegrity: null,
            webAssembly: WEBASSEMBLY_PROBLEM === null
        };
""",
     """            engineIntegrity: null, vizIntegrity: null,
            webAssembly: WEBASSEMBLY_PROBLEM === null,
            // 'wasm' | 'js', and why the page is not on the Wasm engine (null while it is); workers replaced after a crash
            engine: null, engineProblem: WASM_ENGINE_PROBLEM, workerCrashes: 0
        };
        useEngine(WASM_ENGINE_PROBLEM === null ? 'wasm' : 'js');
        if (WASM_ENGINE_PROBLEM !== null) {
            console.info('Kronikol: the PlantUML Wasm engine cannot run here (' + WASM_ENGINE_PROBLEM + '), so the page draws'
                + ' with its JavaScript build.');
        }
"""),
    ("""
        var esmMode = false;
""",
     """
        var esmMode = false;
        var anyWorkerReady = false; // whether a worker has started on the engine the page draws with
"""),
    ("""

        // --- cache (bytes-bounded, least-recently-used first)""",
     """
        // The Wasm GC engine needs WebAssembly, the GC proposal (Chrome 119, Firefox 120, Safari 18.2) and functions
        // built from text, which its runtime's glue is made of and which a content security policy without
        // 'unsafe-eval' refuses. A blob worker inherits the page's policy and runs on the same browser, so here too the
        // page's answer is the worker's.
        function wasmEngineUnavailable() {
            if (WEBASSEMBLY_PROBLEM !== null) return WEBASSEMBLY_PROBLEM;
            try {
                // the smallest module that declares a GC struct type: it validates only where the browser runs Wasm GC
                if (!WebAssembly.validate(new Uint8Array([0, 97, 115, 109, 1, 0, 0, 0, 1, 5, 1, 95, 1, 120, 0]))) return 'Wasm GC is not supported';
            } catch (e) {
                return 'Wasm GC is not supported: ' + (e && e.message ? e.message : e);
            }
            try {
                new Function('return 0');
            } catch (e) {
                return 'functions cannot be built from text: ' + (e && e.message ? e.message : e);
            }
            return null;
        }
        function useEngine(kind) {
            ENGINE_URL = kind === 'wasm' ? WASM_ENGINE_URL : JS_ENGINE_URL;
            ENGINE_INTEGRITY = kind === 'wasm' ? WASM_ENGINE_INTEGRITY : JS_ENGINE_INTEGRITY;
            ENGINE_FILE = kind === 'wasm' ? 'plantuml-wasm-bundle.js' : 'plantuml.js';
            telemetry.engine = kind;
        }

        // --- cache (bytes-bounded, least-recently-used first)"""),
    ("""                if (!rec.ready) workerDied(rec, msg); else console.error('Kronikol render worker: ' + msg);
""",
     """                if (!rec.ready) workerDied(rec, msg); else workerCrashed(rec, msg);
"""),
    ("""        function workerDied(rec, message) {
            console.error('Kronikol render worker failed: ' + message);
""",
     """        function workerDied(rec, message) {
            // The Wasm engine fetched and checked, and did not start: the page draws with the JavaScript engine instead.
            if (telemetry.engine === 'wasm' && !anyWorkerReady) { fallBackToJsEngine('it did not start in a worker: ' + message); return; }
            console.error('Kronikol render worker failed: ' + message);
"""),
    ("""        function onWorkerMessage(rec, m) {
            if (m.type === 'ready') {
                rec.ready = true; telemetry.workers = readyCount();
""",
     """        // An error that reaches the page from a worker while it draws. The engine in it may never answer again (a Wasm
        // engine's stack overflow in Firefox reaches no handler inside the worker, only this one), so the worker goes and
        // another takes its place. What it was drawing is drawn again once, and a diagram that takes a second worker
        // down fails alone, naming the error.
        function workerCrashed(rec, message) {
            console.error('Kronikol render worker crashed: ' + message);
            telemetry.workerCrashes++;
            var i = workers.indexOf(rec);
            if (i >= 0) workers.splice(i, 1);
            try { rec.w.terminate(); } catch (e) { /* best effort */ }
            var keys = Object.keys(pending);
            for (var k = 0; k < keys.length; k++) {
                var job = pending[keys[k]];
                if (job.worker !== rec) continue;
                delete pending[keys[k]]; job.worker = null;
                job.crashes = (job.crashes || 0) + 1;
                if (job.crashes < 2) { queue.unshift(job); continue; }
                if (inflightByKey[job.key] === job) delete inflightByKey[job.key];
                telemetry.errors++;
                for (var t = 0; t < job.targets.length; t++) writeFailure(job.targets[t], job.key, 'the PlantUML engine crashed drawing this diagram: ' + message);
            }
            telemetry.workers = readyCount(); telemetry.inFlight = busyCount();
            if (workers.length < targetWorkers) startWorker();
            pumpWorkers();
        }
        // The JavaScript engine in the Wasm engine's place: the workers and the main-thread engine start again on it, and
        // what they held is drawn by it.
        function fallBackToJsEngine(reason) {
            console.warn('Kronikol: the PlantUML Wasm engine did not start (' + reason + '), so the page draws with its JavaScript build.');
            telemetry.engineProblem = reason;
            useEngine('js');
            for (var i = 0; i < workers.length; i++) { try { workers[i].w.terminate(); } catch (e) { /* best effort */ } }
            workers = []; telemetry.workers = 0; telemetry.inFlight = 0;
            if (blobUrl) { try { URL.revokeObjectURL(blobUrl); } catch (e) { /* best effort */ } blobUrl = null; }
            var requeue = [];
            var keys = Object.keys(pending);
            for (var k = 0; k < keys.length; k++) { pending[keys[k]].worker = null; requeue.push(pending[keys[k]]); }
            pending = {};
            requeue.sort(function (a, b) { return a.seq - b.seq; });
            queue = requeue.concat(queue);
            esmMode = false;
            if (mode === 'main-thread') { engineReady = false; loadEngineScripts(true); return; }
            mode = null; telemetry.mode = 'starting';
            acquireEngine();
        }
        function onWorkerMessage(rec, m) {
            // A worker the page let go of (crashed, or on the engine the page no longer draws with) may still have
            // answers on their way; a diagram it held is drawn again elsewhere under the same number.
            if (workers.indexOf(rec) < 0) return;
            if (m.type === 'ready') {
                rec.ready = true; anyWorkerReady = true; telemetry.workers = readyCount();
"""),
    ("""                    engineFailed(new Error(integrityMessage('plantuml.js', ENGINE_URL, ENGINE_INTEGRITY)));
""",
     """                    engineFailed(new Error(integrityMessage(ENGINE_FILE, ENGINE_URL, ENGINE_INTEGRITY)));
"""),
    ("""        function loadEngineScripts() {
            if (WEBASSEMBLY_PROBLEM === null) addScript(VIZ_URL, VIZ_INTEGRITY).then(function () {
""",
     """        function loadEngineScripts(engineOnly) {
            if (WEBASSEMBLY_PROBLEM === null && !engineOnly) addScript(VIZ_URL, VIZ_INTEGRITY).then(function () {
"""),
    ("""                return tagFailure(ENGINE_URL, ENGINE_INTEGRITY, 'plantuml.js', 'engineIntegrity', e).then(function (failure) {
                    throw failure;
                });
            }).then(function (mod) {
                if (!mod || typeof mod.render !== 'function') throw new Error('the engine module has no render export: ' + ENGINE_URL);
""",
     """                return tagFailure(ENGINE_URL, ENGINE_INTEGRITY, ENGINE_FILE, 'engineIntegrity', e).then(function (failure) {
                    throw failure;
                });
            }).then(function (mod) {
                if (!mod || typeof mod.render !== 'function') throw new Error('the engine module has no render export: ' + ENGINE_URL);
                // The Wasm engine's bundle says when its module is ready, or why it cannot load.
                var started = typeof window.__plantumlEngineReady === 'function' && telemetry.engine === 'wasm' ? window.__plantumlEngineReady() : null;
                return started ? started.then(function () { return mod; }, function (e) {
                    var notStarted = new Error(String(e && e.message ? e.message : e)); notStarted.wasmDidNotStart = true; throw notStarted;
                }) : mod;
            }).then(function (mod) {
"""),
    ("""                pumpMain();
            }).catch(function (e) { engineFailed(e); });
        }
""",
     """                pumpMain();
            }).catch(function (e) {
                if (e && e.wasmDidNotStart && telemetry.engine === 'wasm') fallBackToJsEngine(e.message);
                else engineFailed(e);
            });
        }
"""),
    ("""            try {
                engineRender(job.lines, target.id || el.id);
            } catch (e) {
                writeFailure(target, job.key, e && e.message ? e.message : e);
                finish();
            }
""",
     """            try {
                // the Wasm engine's render returns a promise, which rejects when its module cannot load
                var answer = engineRender(job.lines, target.id || el.id);
                if (answer && typeof answer.then === 'function') answer.then(null, function (e) { writeFailure(target, job.key, e && e.message ? e.message : e); finish(); });
            } catch (e) {
                writeFailure(target, job.key, e && e.message ? e.message : e);
                finish();
            }
"""),
])

edit('src/Kronikol/Reports/plantuml-worker-host.js', [
    ("""// The report page (plantuml-browser-render-script.js) fetches viz-global.js + plantuml.js as text and
// builds each worker from ONE Blob: this file, then viz, then the engine. Nothing here touches the
// network — a Blob worker created by a file:// document cannot (importScripts of any origin fails).
""",
     """// The report page (plantuml-browser-render-script.js) fetches viz-global.js + plantuml.js as text and
// builds each worker from ONE Blob: this file, then viz, then the engine. Nothing here touches the
// network — a Blob worker created by a file:// document cannot (importScripts of any origin fails).
// From 5.0.0 the engine is plantuml-wasm-bundle.js wherever the browser runs Wasm GC: PlantUML's Wasm
// engine with the module embedded, the same API, and a render that answers with a promise.
"""),
    ("""            self.plantuml = { render: function (lines, id) { return ex.render(lines, id, { maxSvgSize: 98304 }); } };
            return Promise.resolve();
""",
     """            self.plantuml = { render: function (lines, id) { return ex.render(lines, id, { maxSvgSize: 98304 }); } };
            // The Wasm engine's bundle says when its module is ready, or why it cannot load (5.0.0): the worker answers
            // 'ready' only then, and a module that does not load is the worker's 'fatal', so the page can draw with the
            // JavaScript engine instead.
            return typeof self.__plantumlEngineReady === 'function' ? self.__plantumlEngineReady() : Promise.resolve();
"""),
    ("""        try {
            self.plantuml.render(job.lines, id);
        } catch (e) {
            fail(e && e.message ? e.message : e);
            return;
        }
""",
     """        try {
            // the Wasm engine's render returns a promise, which rejects when its module cannot load
            var answer = self.plantuml.render(job.lines, id);
            if (answer && typeof answer.then === 'function') answer.then(null, function (e) { fail(e && e.message ? e.message : e); });
        } catch (e) {
            fail(e && e.message ? e.message : e);
            return;
        }
"""),
])

edit('src/Kronikol/Reports/DiagramContextMenu.cs', [
    ("""            .Replace("__PLANTUML_ENGINE_INTEGRITY__", TrackingDefaults.PlantUmlJsIntegrity)
""",
     """            .Replace("__PLANTUML_ENGINE_INTEGRITY__", TrackingDefaults.PlantUmlJsIntegrity)
            .Replace("__PLANTUML_WASM_ENGINE_INTEGRITY__", TrackingDefaults.PlantUmlWasmEngineIntegrity)
"""),
])
print('ok')
