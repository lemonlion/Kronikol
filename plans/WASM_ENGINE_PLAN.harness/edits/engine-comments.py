# Comments that named plantuml.js as the page's one engine name both of 5.0.0's (plans/WASM_ENGINE_PLAN.md).
# usage: python -I engine-comments.py <repo root>
import sys

root = sys.argv[1]


def edit(rel, pairs):
    p = root + '/' + rel
    t = open(p, 'rb').read().decode('utf-8')
    assert '\r' not in t, rel
    for old, new in pairs:
        assert t.count(old) == 1, (rel, old[:100])
        t = t.replace(old, new)
    open(p, 'wb').write(t.encode('utf-8'))


edit('src/Kronikol/ComponentDiagram/ComponentDiagramReportGenerator.cs', [
    ("""    /// engine draws it (Server, Local). Both JavaScript engines (the page's and the Node renderer's) run plantuml.js,
    /// which cannot load the C4 stdlib: until 3.29.6 the include hung the render until its timeout, and since then it
    /// draws the engine's error picture, so they are given the plain component syntax.
""",
     """    /// engine draws it (Server, Local). The engines that run in a browser or in node (the page's, plantuml.js or from
    /// 5.0.0 its Wasm build, and the Node renderer's plantuml.js) cannot load the C4 stdlib: until 3.29.6 the include hung
    /// the render until its timeout, and since then it draws the engine's error picture, so they are given the plain
    /// component syntax.
"""),
])
edit('src/Kronikol/Reports/DiagramContextMenu.cs', [
    ("""    /// The Web Worker host for the TeaVM plantuml.js engine: the minimal DOM the engine needs, an SVG
""",
     """    /// The Web Worker host for the TeaVM engine (plantuml.js, or from 5.0.0 its Wasm build wherever the browser runs
    /// Wasm GC): the minimal DOM the engine needs, an SVG
"""),
])
edit('src/Kronikol/Reports/plantuml-worker-host.js', [
    ("""// Kronikol — Web Worker host for the TeaVM-compiled plantuml.js engine.
""",
     """// Kronikol — Web Worker host for the TeaVM-compiled PlantUML engine (plantuml.js, or its Wasm build).
"""),
])
print('ok')
