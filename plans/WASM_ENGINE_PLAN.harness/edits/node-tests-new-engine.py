# NodeJsPlantUmlRendererTests on 5.0.0's engine (plans/WASM_ENGINE_PLAN.md): its TeaVM no longer runs out of stack on a
# long statement, so the facts that pinned a crash past a cap pin the drawing instead, the batch's handling of a stack
# failure is driven by sources that still overflow (deep nesting) or by a stub engine drawing the 4.x engine's picture,
# and a themed source is compared without the source lines the engine now writes. usage: python -I node-tests-new-engine.py <repo root>
import sys

root = sys.argv[1]
p = root + '/tests/Kronikol.Tests/PlantUml/NodeJsPlantUmlRendererTests.cs'
t = open(p, 'rb').read().decode('utf-8')
assert '\r' not in t

pairs = [
    # --- the stack picture: a stub engine draws the one the 4.x engine drew --------------------------------------------
    ("""        // Past its stack the engine draws a picture of its own error (RangeError: Maximum call stack size exceeded), an
        // SVG the batch returned as the drawn diagram.
        var results = NodeJsPlantUmlRenderer.RenderMany([Seq("First", "One"), OverflowingComponentDiagram(4000), Seq("Third", "Three")]);
""",
     """        // Past its stack the 4.x engine drew a picture of its own error (RangeError: Maximum call stack size exceeded), an
        // SVG the batch returned as the drawn diagram. 5.0.0's engine draws that picture for no source Kronikol writes (its
        // TeaVM's regular expressions no longer recurse once per character, plans/WASM_ENGINE_PLAN.md), so a stub engine
        // draws the 4.x engine's picture for the overflowing diagram and the sources' arrows for the others.
        var engine = StubEngineCache(\"\"\"
            var t=document.getElementById(id), source=lines.join('\\n');
            var texts = source.indexOf('INSERT INTO') >= 0
                ? ['PlantUML version 1.2026.8', '[From textarea (line 6) ]', '@startuml', 'caller -[#E74C3C]-&gt; warehouse : "ClickHouse: INSERT INTO ...',
                   ' java.lang.RuntimeException: (JavaScript) RangeError: Maximum call stack size exceeded (Assumed diagram type: component)']
                : lines.filter(function(l){ return l.indexOf('->') >= 0; });
            t.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg"><g>' + texts.map(function(l,i){ return '<text x="5" y="' + (14*(i+1)) + '">' + l + '</text>'; }).join('') + '</g></svg>';
            \"\"\", out var directory);
        IReadOnlyList<NodeRenderResult> results;
        try
        {
            results = NodeJsPlantUmlRenderer.RenderMany([Seq("First", "One"), OverflowingComponentDiagram(4000), Seq("Third", "Three")], engine);
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch { /* best effort */ }
        }
"""),
    # --- the stack text: deep nesting still overflows the engine --------------------------------------------------------
    ("""        // A coloured note bar past the stack fails as text, which the batch already returned as the diagram's error,
        // with nothing to say that the engine ran out of stack.
        var bar = "hnote across #black:<color:white>" + string.Concat(Enumerable.Repeat("{\\"sku\\":\\"SKU-0001\\",\\"n\\":1},", 200)) + "</color>";
        var results = NodeJsPlantUmlRenderer.RenderMany(["@startuml\\nparticipant Api\\nparticipant Orders\\nApi -> Orders: GET /orders\\n" + bar + "\\n@enduml"]);
""",
     """        // A diagram past the engine's stack fails as text, which the batch already returned as the diagram's error, with
        // nothing to say that the engine ran out of stack. On the 4.x engine a long coloured note bar did it; 5.0.0's
        // engine draws any length of one (plans/WASM_ENGINE_PLAN.md), and runs out of stack only on nesting this deep.
        var results = NodeJsPlantUmlRenderer.RenderMany([NestedGroupsDiagram(3000)]);
"""),
    ("""            drawn = ComponentDiagramReportGenerator.DrawEmbedded(OverflowingComponentDiagram(4000),
                new ReportConfigurationOptions { PlantUmlRendering = PlantUmlRendering.NodeJs, InlineSvgRendering = true });
""",
     """            drawn = ComponentDiagramReportGenerator.DrawEmbedded(NestedBoldComponentDiagram(5000),
                new ReportConfigurationOptions { PlantUmlRendering = PlantUmlRendering.NodeJs, InlineSvgRendering = true });
"""),
    ("""    private static string Picture(params string[] lines) =>
""",
     """    /// <summary>
    /// A sequence diagram whose one message sits inside <paramref name="depth"/> nested groups: from about 2,000 deep it is
    /// past node's stack on 5.0.0's engine, which fails it as text (measured 2026-10-20, plans/WASM_ENGINE_PLAN.md).
    /// </summary>
    private static string NestedGroupsDiagram(int depth) =>
        "@startuml\\n" + string.Concat(Enumerable.Repeat("group g\\n", depth)) + "a -> b: x\\n" + string.Concat(Enumerable.Repeat("end\\n", depth)) + "@enduml";

    /// <summary>
    /// A component diagram whose one edge's label is bold <paramref name="depth"/> times over: at 5,000 it is past node's
    /// stack on 5.0.0's engine, which fails it as text (measured 2026-10-20).
    /// </summary>
    private static string NestedBoldComponentDiagram(int depth) =>
        "@startuml\\nleft to right direction\\nrectangle \\"Caller\\" as caller\\ndatabase \\"Warehouse\\" as warehouse\\ncaller --> warehouse : \\""
        + string.Concat(Enumerable.Repeat("<b>", depth)) + "x" + string.Concat(Enumerable.Repeat("</b>", depth)) + "\\"\\n@enduml";

    /// <summary>
    /// The engine files of a stub engine whose render runs <paramref name="renderBody"/> (<c>lines</c>, <c>id</c> and
    /// <c>options</c> in scope), kept in <paramref name="directory"/>, which the caller deletes: for the batch's handling
    /// of an answer 5.0.0's engine gives for no source Kronikol writes.
    /// </summary>
    private static EngineCache StubEngineCache(string renderBody, out string directory)
    {
        directory = Directory.CreateTempSubdirectory("kronikol-stub-batch-").FullName;
        var files = new Dictionary<string, byte[]>
        {
            ["viz-global.js"] = System.Text.Encoding.UTF8.GetBytes(
                "globalThis.Viz = { instance: function () { return Promise.resolve({ renderString: function () { return '<svg xmlns=\\"http://www.w3.org/2000/svg\\"/>'; } }); } };"),
            ["plantuml.js"] = System.Text.Encoding.UTF8.GetBytes(
                "\\"use strict\\";\\nlet C=(lines,id,options)=>{" + renderBody + "},D=(lines,options)=>'unused';\\nexport{C as render,D as renderToString};\\n"),
        };
        return new EngineCache(directory, url => files[url[(url.LastIndexOf('/') + 1)..]], "https://stub.invalid",
            files.ToDictionary(f => f.Key, f => EngineCache.Integrity(f.Value)));
    }

    private static string Picture(params string[] lines) =>
"""),
    # --- a themed source: the engine writes each element's source line, one more for the !theme line ------------------
    ("""            // The theme bundle cannot load here, so the engine draws the diagram without it, exactly as the
            // unthemed source draws.
            Assert.Equal(WithoutProcessingInstructions(results[1].Svg!), WithoutProcessingInstructions(results[0].Svg!));
""",
     """            // The theme bundle cannot load here, so the engine draws the diagram without it, exactly as the
            // unthemed source draws but for the source line 5.0.0's engine writes on each element, one more for the
            // !theme line.
            Assert.Equal(WithoutSourceLines(WithoutProcessingInstructions(results[1].Svg!)), WithoutSourceLines(WithoutProcessingInstructions(results[0].Svg!)));
"""),
    ("""    private static string WithoutProcessingInstructions(string svg) =>
        System.Text.RegularExpressions.Regex.Replace(svg, @"<\\?[\\s\\S]*?\\?>", "");
""",
     """    private static string WithoutProcessingInstructions(string svg) =>
        System.Text.RegularExpressions.Regex.Replace(svg, @"<\\?[\\s\\S]*?\\?>", "");

    private static string WithoutSourceLines(string svg) =>
        System.Text.RegularExpressions.Regex.Replace(svg, " data-source-line=\\"[0-9]+\\"", "");
"""),
    # --- the caps: 5.0.0's engine draws far past them ---------------------------------------------------------------------
    ("""    public void A_block_opener_caps_lower_than_a_message_statement()
    {
        Assert.SkipWhen(!IsNodeAvailable(), "Node.js not available on PATH");

        // On the 1.2026.6 build this was a parse limit around 1476 (loop) to 1484 (opt); from the
        // 1.2026.8beta1 build the parse accepts far more but the engine crashes on its own JS stack
        // instead (RangeError): about 2,000 on node 25.9 with its default stack on both 1.2026.8 builds,
        // 3660 to 5641 on the runtime of 2026-09-04 (plans/ENGINE_PIN_PLAN.md §1.11). A stack edge moves
        // with the runtime and wobbles between processes. The constant sits well under the lowest measurement
        // rather than on it, so the pins are the two facts that matter and survive a small engine
        // drift: the constant parses, and a runaway block label still kills the whole diagram.
        var safe = StatementOf("loop ", PlantUmlStatementLimits.MaxBlockLabelChars);
        var runaway = StatementOf("loop ", 8000);

        Assert.True(Renders($"a -> b: x\\n{safe}\\na -> b: y\\nend"), $"{PlantUmlStatementLimits.MaxBlockLabelChars} should parse");
        Assert.False(Renders($"a -> b: x\\n{runaway}\\na -> b: y\\nend"),
            "8000 should not — an over-long block opener still takes the diagram down");
    }
""",
     """    public void A_block_opener_draws_at_its_cap_and_far_past_it()
    {
        Assert.SkipWhen(!IsNodeAvailable(), "Node.js not available on PATH");

        // On the 1.2026.6 build this was a parse limit around 1476 (loop) to 1484 (opt); from the
        // 1.2026.8beta1 build the parse accepts far more but the engine crashed on its own JS stack
        // instead (RangeError): about 2,000 on node 25.9 with its default stack on both 1.2026.8 builds,
        // 3660 to 5641 on the runtime of 2026-09-04 (plans/ENGINE_PIN_PLAN.md §1.11). 5.0.0's engine, on TeaVM
        // 0.17.0-dev-2, draws an opener of 16,000 characters in node, and fails past that only when the diagram is
        // wider than its size limit (measured 2026-10-20, plans/WASM_ENGINE_PLAN.md). The cap stays where the 4.x
        // engine's render worker put it, so the pins are that the capped opener draws, and that one far past it does too.
        var safe = StatementOf("loop ", PlantUmlStatementLimits.MaxBlockLabelChars);
        var long8000 = StatementOf("loop ", 8000);

        Assert.True(Renders($"a -> b: x\\n{safe}\\na -> b: y\\nend"), $"{PlantUmlStatementLimits.MaxBlockLabelChars} should parse");
        Assert.True(Renders($"a -> b: x\\n{long8000}\\na -> b: y\\nend"), "an opener of 8000 draws on 5.0.0's engine");
    }
"""),
    ("""    public void A_coloured_note_bar_crashes_the_engine_rather_than_reporting_a_syntax_error()
    {
        Assert.SkipWhen(!IsNodeAvailable(), "Node.js not available on PATH");

        // The step-delimiter bar's own form. Past the cap the engine throws `RangeError: Maximum call
        // stack size exceeded` and returns no SVG at all, so the scenario loses every diagram it had
        // rather than one statement. Measured 1458 on the 1.2026.6 build, around 4124 on 1.2026.8beta1
        // on 2026-09-04 and 2005 to 2008 on node 25.9 with both 1.2026.8 builds, and in the render worker,
        // whose stack is half node's, from 880 with V8's optimizing compilers off (plans/LONG_COMPONENT_EDGE_PLAN.md
        // R3): a stack-overflow edge, so it moves with the runtime; the crash probe sits far past it and the
        // constant far under it. The bar is a step written as one token, a JSON array, which keeps the
        // coloured form at any length: a step with a space wraps, and a wrapped step takes the styled form.
""",
     """    public void A_coloured_note_bar_draws_at_its_cap_and_far_past_it()
    {
        Assert.SkipWhen(!IsNodeAvailable(), "Node.js not available on PATH");

        // The step-delimiter bar's own form. Past its stack the 4.x engine threw `RangeError: Maximum call
        // stack size exceeded` and returned no SVG at all, so the scenario lost every diagram it had
        // rather than one statement. Measured 1458 on the 1.2026.6 build, around 4124 on 1.2026.8beta1
        // on 2026-09-04 and 2005 to 2008 on node 25.9 with both 1.2026.8 builds, and in the render worker,
        // whose stack is half node's, from 880 with V8's optimizing compilers off (plans/LONG_COMPONENT_EDGE_PLAN.md
        // R3). 5.0.0's engine, on TeaVM 0.17.0-dev-2, draws a bar of 6,000 and fails past that only when the diagram
        // is wider than its size limit (measured 2026-10-20, plans/WASM_ENGINE_PLAN.md); the constant stays where the
        // 4.x worker put it. The bar is a step written as one token, a JSON array, which keeps the
        // coloured form at any length: a step with a space wraps, and a wrapped step takes the styled form.
"""),
    ("""        Assert.True(Renders($"a -> b: x\\n{safe}"), "the capped bar renders");
        Assert.Equal("", RenderBody("a -> b: x\\nhnote across <<stepDelimiter>> #black:<color:white>" + new string('s', 6000)));
""",
     """        Assert.True(Renders($"a -> b: x\\n{safe}"), "the capped bar renders");
        Assert.True(Renders("a -> b: x\\nhnote across <<stepDelimiter>> #black:<color:white>" + new string('s', 6000)), "a bar of 6000 draws on 5.0.0's engine");
"""),
    # --- the link: an <a> round its text, as the Java engine draws it --------------------------------------------------
    ("""        // The engine draws a link as underlined text (the page binds it by its text, there is no <a>), and the
        // whole path is in the note beside the arrow.
        Assert.Matches("text-decoration=\\"underline\\"[^>]*>GET: /orders/search\\\\?filter0=value0", svg);
""",
     """        // 5.0.0's engine draws a link as the Java engine does, an <a> round its underlined text (the page binds it by its
        // href), and the whole path is in the note beside the arrow. The SVG declares the xlink prefix the link uses.
        Assert.Matches($"<a [^>]*href=\\"#iflow-{pairId}\\"[^>]*><text [^>]*text-decoration=\\"underline\\"[^>]*>GET: /orders/search\\\\?filter0=value0", svg);
        Assert.NotNull(System.Xml.Linq.XDocument.Parse(svg).Root);
"""),
]
for old, new in pairs:
    assert t.count(old) == 1, (t.count(old), old[:120])
    t = t.replace(old, new)
open(p, 'wb').write(t.encode('utf-8'))
print('ok')
