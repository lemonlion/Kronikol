# DiagramContextMenuTests on the two-engine shim (5.0.0, plans/WASM_ENGINE_PLAN.md): the hashes, the engine's own file
# name in a refusal, the one Function constructor (the probe), the probe's answers, and the main thread's engine.
# usage: python -I shim-tests.py <repo root>
import sys

root = sys.argv[1]
p = root + '/tests/Kronikol.Tests/Reports/DiagramContextMenuTests.cs'
t = open(p, 'rb').read().decode('utf-8')
assert '\r' not in t

pairs = [
    ("""    [Fact]
    public void The_shim_carries_both_engines_with_their_known_hashes()
    {
        // From 5.0.0 the page draws with the Wasm GC engine where the browser can run it, and with the JavaScript engine of
        // the same build everywhere else (plans/WASM_ENGINE_PLAN.md), so it holds both files' hashes, each in its own place.
        Assert.Contains($"var JS_ENGINE_INTEGRITY = '{Kronikol.Constants.TrackingDefaults.PlantUmlJsIntegrity}';", _plantUmlScript);
        Assert.Contains($"var WASM_ENGINE_INTEGRITY = '{Kronikol.Constants.TrackingDefaults.PlantUmlWasmEngineIntegrity}';", _plantUmlScript);
        Assert.DoesNotContain("__PLANTUML_", _plantUmlScript);
    }

""", ""),
    ("""        Assert.Contains($"var VIZ_INTEGRITY = '{Kronikol.Constants.TrackingDefaults.VizGlobalJsIntegrity}';", _plantUmlScript);
        Assert.Contains($"var ENGINE_INTEGRITY = '{Kronikol.Constants.TrackingDefaults.PlantUmlJsIntegrity}';", _plantUmlScript);
        Assert.DoesNotContain("__PLANTUML_", _plantUmlScript);
""",
     """        // From 5.0.0 the page draws with the Wasm GC engine where the browser can run it, and with the JavaScript engine of
        // the same build everywhere else (plans/WASM_ENGINE_PLAN.md), so it holds both engines' hashes.
        Assert.Contains($"var VIZ_INTEGRITY = '{Kronikol.Constants.TrackingDefaults.VizGlobalJsIntegrity}';", _plantUmlScript);
        Assert.Contains($"var JS_ENGINE_INTEGRITY = '{Kronikol.Constants.TrackingDefaults.PlantUmlJsIntegrity}';", _plantUmlScript);
        Assert.Contains($"var WASM_ENGINE_INTEGRITY = '{Kronikol.Constants.TrackingDefaults.PlantUmlWasmEngineIntegrity}';", _plantUmlScript);
        Assert.DoesNotContain("__PLANTUML_", _plantUmlScript);
"""),
    ("""        Assert.Contains("the browser console names the hash it computed", message);
        Assert.Contains("integrityMessage('plantuml.js', ENGINE_URL, ENGINE_INTEGRITY)", _plantUmlScript);
        Assert.Contains("integrityMessage('viz-global.js', VIZ_URL, VIZ_INTEGRITY)", _plantUmlScript);
    }
""",
     """        Assert.Contains("the browser console names the hash it computed", message);
        // the engine the page draws with names its own file (The_engine_the_page_draws_with_brings_its_file_url_and_hash)
        Assert.Contains("integrityMessage(ENGINE_FILE, ENGINE_URL, ENGINE_INTEGRITY)", _plantUmlScript);
        Assert.Contains("tagFailure(ENGINE_URL, ENGINE_INTEGRITY, ENGINE_FILE, 'engineIntegrity', e)", ShimFunction("loadEngineScripts"));
        Assert.Contains("integrityMessage('viz-global.js', VIZ_URL, VIZ_INTEGRITY)", _plantUmlScript);
    }

    [Fact]
    public void The_engine_the_page_draws_with_brings_its_file_url_and_hash()
    {
        Assert.SkipWhen(!NodeProbe.IsAvailable, "Node.js not available on PATH");

        // Every fetch, tag, hash check and refusal reads the engine's file, address and hash from one place, which
        // useEngine sets, and the telemetry says which engine it is.
        const string constants = "var JS_ENGINE_URL = 'base/plantuml.js', WASM_ENGINE_URL = 'base/plantuml-wasm-bundle.js',"
            + " JS_ENGINE_INTEGRITY = 'sha256-js', WASM_ENGINE_INTEGRITY = 'sha256-wasm', ENGINE_URL, ENGINE_INTEGRITY, ENGINE_FILE, telemetry = {};\\n";
        const string report = "\\nvar seen = [];\\nuseEngine('wasm'); seen.push([ENGINE_FILE, ENGINE_URL, ENGINE_INTEGRITY, telemetry.engine]);"
            + "\\nuseEngine('js'); seen.push([ENGINE_FILE, ENGINE_URL, ENGINE_INTEGRITY, telemetry.engine]);"
            + "\\nprocess.stdout.write(JSON.stringify(seen));";

        Assert.Equal(
            "[[\\"plantuml-wasm-bundle.js\\",\\"base/plantuml-wasm-bundle.js\\",\\"sha256-wasm\\",\\"wasm\\"],"
            + "[\\"plantuml.js\\",\\"base/plantuml.js\\",\\"sha256-js\\",\\"js\\"]]",
            NodeProbe.Run(constants + ShimFunction("useEngine") + report));
        Assert.Contains("useEngine(WASM_ENGINE_PROBLEM === null ? 'wasm' : 'js');", _plantUmlScript);
        Assert.Contains("var WASM_ENGINE_URL = 'https://cdn.jsdelivr.net/gh/", _plantUmlScript);
    }

    [Fact]
    public void The_page_finds_out_whether_the_wasm_engine_can_run_before_it_fetches_an_engine()
    {
        Assert.SkipWhen(!NodeProbe.IsAvailable, "Node.js not available on PATH");

        // The Wasm engine needs WebAssembly, the GC proposal (Chrome 119, Firefox 120, Safari 18.2), and functions built
        // from text, which its runtime is made of and a content security policy without 'unsafe-eval' refuses. Where one
        // is missing the page fetches the JavaScript engine instead, and the probe's answer says why.
        var probe = ShimFunction("webAssemblyUnavailable") + ShimFunction("wasmEngineUnavailable");
        const string report = "\\nvar WEBASSEMBLY_PROBLEM = webAssemblyUnavailable();\\nprocess.stdout.write(JSON.stringify(wasmEngineUnavailable()));";

        Assert.Equal("null", NodeProbe.Run(probe + report));
        Assert.Equal("\\"WebAssembly is not defined\\"", NodeProbe.RunCaptured(["--jitless"], probe + report, null).Stdout);
        Assert.Equal("\\"Wasm GC is not supported\\"", NodeProbe.Run(
            "WebAssembly.validate = function () { return false; };\\n" + probe + report));
        Assert.Equal("\\"functions cannot be built from text: refused by the page's policy\\"", NodeProbe.Run(
            "globalThis.Function = function () { throw new EvalError(\\"refused by the page's policy\\"); };\\n" + probe + report));
    }

    [Fact]
    public void The_main_thread_draws_with_the_javascript_engine()
    {
        // The Wasm engine's wrapper takes any error that reaches the global scope while a diagram waits for the engine's
        // own crash, and starts the engine again. In a worker every such error is the engine's; on the page it can be any
        // script's, so the fallback that draws on the page's own thread draws with the JavaScript engine.
        var fallback = ShimFunction("useMainThreadEngine");
        Assert.Contains("if (telemetry.engine === 'wasm') { useEngine('js'); telemetry.engineProblem = 'the page draws on its main thread'; }", fallback);
        Assert.True(fallback.IndexOf("useEngine('js')", StringComparison.Ordinal) < fallback.IndexOf("loadEngineScripts();", StringComparison.Ordinal),
            "the engine is chosen before its tags are added");
        Assert.DoesNotContain("__plantumlEngineReady", ShimFunction("loadEngineScripts"));
    }
"""),
    ("""        // A Function constructor is refused by any content security policy without unsafe-eval, one of the
        // hosted-report cases the fallback exists for. The classic-build branch (realLoad) cannot run behind a
        // module tag, and the pin has been an ES-module build since 3.0.76 (plans/ENGINE_PIN_PLAN.md S2).
        Assert.DoesNotContain("new Function", _plantUmlScript);
        Assert.DoesNotContain("realLoad", _plantUmlScript);
""",
     """        // A Function constructor is refused by any content security policy without unsafe-eval, one of the
        // hosted-report cases the fallback exists for. The classic-build branch (realLoad) cannot run behind a
        // module tag, and the pin has been an ES-module build since 3.0.76 (plans/ENGINE_PIN_PLAN.md S2). The one
        // Function constructor left is the probe that asks whether such a policy is there, inside a try (5.0.0).
        var at = _plantUmlScript.IndexOf("new Function", StringComparison.Ordinal);
        Assert.True(at >= 0 && _plantUmlScript.IndexOf("new Function", at + 1, StringComparison.Ordinal) < 0, "one Function constructor, the probe's");
        var probe = ShimFunction("wasmEngineUnavailable");
        Assert.Contains("try {\\n                new Function('return 0');\\n            } catch (e) {", probe);
        Assert.DoesNotContain("realLoad", _plantUmlScript);
"""),
]
for old, new in pairs:
    assert t.count(old) == 1, (t.count(old), old[:120])
    t = t.replace(old, new)
open(p, 'wb').write(t.encode('utf-8'))
print('ok')
