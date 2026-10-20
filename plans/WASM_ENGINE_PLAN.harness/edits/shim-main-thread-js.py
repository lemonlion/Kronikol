# The main thread draws with the JavaScript engine: the Wasm engine's wrapper takes any error that reaches the global
# scope while a diagram waits for the engine's own crash, which inside a worker it is and on the page it may not be. So
# the Wasm engine runs in workers only, and the main-thread path is 4.x's on the new build (plans/WASM_ENGINE_PLAN.md).
# usage: python -I shim-main-thread-js.py <repo root>
import sys

root = sys.argv[1]
p = root + '/src/Kronikol/Reports/plantuml-browser-render-script.js'
t = open(p, 'rb').read().decode('utf-8')
assert '\r' not in t

pairs = [
    ("""            queue = expanded;
            loadEngineScripts();
        }
""",
     """            queue = expanded;
            // The main thread draws with the JavaScript engine. The Wasm engine's wrapper takes any error that reaches the
            // global scope while a diagram waits for the engine's own crash and starts again: in a worker every such error
            // is the engine's, on the page it can be any script's.
            if (telemetry.engine === 'wasm') { useEngine('js'); telemetry.engineProblem = 'the page draws on its main thread'; }
            loadEngineScripts();
        }
"""),
    ("""        function loadEngineScripts(engineOnly) {
            if (WEBASSEMBLY_PROBLEM === null && !engineOnly) addScript(VIZ_URL, VIZ_INTEGRITY).then(function () {
""",
     """        function loadEngineScripts() {
            if (WEBASSEMBLY_PROBLEM === null) addScript(VIZ_URL, VIZ_INTEGRITY).then(function () {
"""),
    ("""                if (!mod || typeof mod.render !== 'function') throw new Error('the engine module has no render export: ' + ENGINE_URL);
                // The Wasm engine's bundle says when its module is ready, or why it cannot load.
                var started = typeof window.__plantumlEngineReady === 'function' && telemetry.engine === 'wasm' ? window.__plantumlEngineReady() : null;
                return started ? started.then(function () { return mod; }, function (e) {
                    var notStarted = new Error(String(e && e.message ? e.message : e)); notStarted.wasmDidNotStart = true; throw notStarted;
                }) : mod;
            }).then(function (mod) {
                // Stock engine builds refuse diagrams past 8192px by default; Kronikol's limit is 98304px.
""",
     """                if (!mod || typeof mod.render !== 'function') throw new Error('the engine module has no render export: ' + ENGINE_URL);
                // Stock engine builds refuse diagrams past 8192px by default; Kronikol's limit is 98304px.
"""),
    ("""            }).catch(function (e) {
                if (e && e.wasmDidNotStart && telemetry.engine === 'wasm') fallBackToJsEngine(e.message);
                else engineFailed(e);
            });
        }
""",
     """            }).catch(function (e) { engineFailed(e); });
        }
"""),
    ("""            try {
                // the Wasm engine's render returns a promise, which rejects when its module cannot load
                var answer = engineRender(job.lines, target.id || el.id);
                if (answer && typeof answer.then === 'function') answer.then(null, function (e) { writeFailure(target, job.key, e && e.message ? e.message : e); finish(); });
            } catch (e) {
""",
     """            try {
                engineRender(job.lines, target.id || el.id);
            } catch (e) {
"""),
    ("""            esmMode = false;
            if (mode === 'main-thread') { engineReady = false; loadEngineScripts(true); return; }
            mode = null; telemetry.mode = 'starting';
            acquireEngine();
        }
""",
     """            esmMode = false;
            mode = null; telemetry.mode = 'starting';
            acquireEngine();
        }
"""),
    ("""        // The JavaScript engine in the Wasm engine's place: the workers and the main-thread engine start again on it, and
        // what they held is drawn by it.
""",
     """        // The JavaScript engine in the Wasm engine's place: the workers start again on it, and what they held is drawn by it.
"""),
    # the header: the main thread's engine
    ("""    // plantuml.js does. A Wasm engine that passes its hash check and then does not start gives way to plantuml.js, and
    // a worker that an error reaches while it draws is replaced, its diagram drawn once more and then failed.
""",
     """    // plantuml.js does. A Wasm engine that passes its hash check and then does not start gives way to plantuml.js, and
    // a worker that an error reaches while it draws is replaced, its diagram drawn once more and then failed. The main
    // thread (the fallback below) draws with plantuml.js.
"""),
]
for old, new in pairs:
    assert t.count(old) == 1, (t.count(old), old[:120])
    t = t.replace(old, new)
open(p, 'wb').write(t.encode('utf-8'))
print('ok')
