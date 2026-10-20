# The engine pin moves to the self-built PlantUML master + TeaVM 0.17.0-dev-2 build on the engine fork, with the Wasm
# engine's hash beside the JavaScript engine's (5.0.0, plans/WASM_ENGINE_PLAN.md).
# usage: python -I tracking-defaults.py <repo root>
import sys

root = sys.argv[1]
p = root + '/src/Kronikol/Constants/TrackingDefaults.cs'
t = open(p, 'rb').read().decode('utf-8')
assert '\r' not in t

old = '''    /// <summary>
    /// CDN base URL for the PlantUML JavaScript renderer used in HTML reports: the published
    /// <c>@plantuml/core</c> package at its release version 1.2026.8, on jsDelivr's <c>/npm/</c> route. The
    /// registry never lets a published name@version hold other bytes, so nobody can change what this URL
    /// serves. It is a stock ES-module build of PlantUML's release commit <c>149874a1</c>, nine commits past
    /// the <c>0e4f452e</c> build that was pinned before it, none of them reachable from a Kronikol diagram, and
    /// it draws every diagram Kronikol emits byte-identical to that build (plans/ENGINE_PIN_PLAN.md §1.3,
    /// §1.16). The engine's default 8192px size limit is raised at render time: every ES-module render call
    /// site passes <c>{ maxSvgSize: 98304 }</c>. Every consumer (the browser shim, the worker host,
    /// <c>plantuml-render.js</c>) rewrites the engine's trailing <c>export</c> statement into an assignment,
    /// since Workers and <c>vm</c> contexts cannot evaluate ES modules. The previous engine builds stay
    /// published on the <c>lemonlion/plantuml-js-plantuml_limit_size_98304</c> fork's tags, for the reports
    /// that reference them.
    /// <para>
    /// A <c>const</c> is compiled into the code that reads it: a consumer that copied this value into its
    /// own assembly keeps the old URL, which still works, until it is rebuilt.
    /// </para>
    /// </summary>
    public const string PlantUmlJsCdnBase = "https://cdn.jsdelivr.net/npm/@plantuml/core@1.2026.8";

    /// <summary>
    /// The Subresource Integrity value of <c>plantuml.js</c> under <see cref="PlantUmlJsCdnBase"/>: the base64 SHA-256
    /// of the file's bytes, as jsDelivr lists it and as the registry tarball's file hashes (plans/ENGINE_PIN_PLAN.md
    /// §1.1, §1.13). The report page hands it to the browser's own integrity check, and the Node renderer checks its
    /// cached copy against it. It moves with the CDN base; the fork build's values are in the plan's A.3.
    /// </summary>
    internal const string PlantUmlJsIntegrity = "sha256-rejxXtfyoyJYFDMtOsbQW8fr277IYwUSzHz2eLwMTVI=";
'''
new = '''    /// <summary>
    /// CDN base URL for the PlantUML engine used in HTML reports: a build of PlantUML's master at <c>f44b054d1</c>
    /// (1.2026.10beta1, MIT flavor) compiled with TeaVM 0.17.0-dev-2, published under the tag of the same name on the
    /// <c>lemonlion/plantuml-js-plantuml_limit_size_98304</c> fork and served by jsDelivr's <c>/gh/</c> route (5.0.0,
    /// plans/WASM_ENGINE_PLAN.md). It is the stock <c>npmPackage</c> build but for its TeaVM version, and it holds
    /// two engines that draw the same SVG: <c>plantuml.js</c>, and PlantUML's Wasm GC engine packed into one script,
    /// <c>plantuml-wasm-bundle.js</c>. The report page draws with the Wasm one wherever the browser can run it, and
    /// the Node renderer runs <c>plantuml.js</c>. jsDelivr keeps a tag's files once it has served them, and every
    /// file is checked against its known hash (<see cref="PlantUmlJsIntegrity"/>), so a file that changed on the
    /// fork is refused rather than drawn with. The engine's default 8192px size limit is raised at render time:
    /// every render call site passes <c>{ maxSvgSize: 98304 }</c>. Every consumer (the browser shim, the worker
    /// host, <c>plantuml-render.js</c>) rewrites the engine's trailing <c>export</c> statement into an assignment,
    /// since Workers and <c>vm</c> contexts cannot evaluate ES modules. Through 4.x the base was the published
    /// <c>@plantuml/core</c> 1.2026.8 package on jsDelivr's <c>/npm/</c> route (plans/ENGINE_PIN_PLAN.md), and the
    /// builds before that stay on the fork's other tags, for the reports that reference them.
    /// <para>
    /// A <c>const</c> is compiled into the code that reads it: a consumer that copied this value into its
    /// own assembly keeps the old URL, which still works, until it is rebuilt.
    /// </para>
    /// </summary>
    public const string PlantUmlJsCdnBase = "https://cdn.jsdelivr.net/gh/lemonlion/plantuml-js-plantuml_limit_size_98304@v1.2026.10beta1-f44b054-teavm0.17.0-dev-2";

    /// <summary>
    /// The Subresource Integrity value of <c>plantuml.js</c> under <see cref="PlantUmlJsCdnBase"/>: the base64 SHA-256
    /// of the file's bytes as the fork's tag holds them (plans/ENGINE_PIN_PLAN.md §1.1, plans/WASM_ENGINE_PLAN.md).
    /// The report page hands it to the browser's own integrity check, and the Node renderer checks its cached copy
    /// against it. It moves with the CDN base.
    /// </summary>
    internal const string PlantUmlJsIntegrity = "sha256-/vtFyFB1p3iF7f2rj0da7UNXYle2np+qILBnXWqAEgg=";

    /// <summary>
    /// The Subresource Integrity value of <c>plantuml-wasm-bundle.js</c> under <see cref="PlantUmlJsCdnBase"/> (see
    /// <see cref="PlantUmlJsIntegrity"/>): PlantUML's Wasm GC engine with its runtime and its module in one script, which
    /// the report page draws with wherever the browser runs Wasm GC (5.0.0, plans/WASM_ENGINE_PLAN.md).
    /// </summary>
    internal const string PlantUmlWasmEngineIntegrity = "sha256-Cnk+sDwumDPD/bLkAL7f5dDvyXrY8CowxReQJ8AgxzE=";
'''
assert t.count(old) == 1
t = t.replace(old, new)
open(p, 'wb').write(t.encode('utf-8'))
print('ok')
