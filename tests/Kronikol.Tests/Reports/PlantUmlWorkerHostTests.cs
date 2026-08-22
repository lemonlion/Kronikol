using System.Text.Json;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The Web Worker engine host (plantuml-worker-host.js): the mock DOM and serializer the TeaVM engine
/// renders into. The pure parts are exercised under Node in a sandbox that stands in for the worker
/// global (<c>self</c>); the real-engine round trip is covered by the Playwright fidelity test.
/// </summary>
public class PlantUmlWorkerHostTests
{
    private const string Driver = """
        const vm = require('vm'), fs = require('fs');
        const src = fs.readFileSync(process.argv[2], 'utf8');
        const NS = 'http://www.w3.org/2000/svg';
        function host(locationHref) {
            const sandbox = {
                postMessage: function () {},
                OffscreenCanvas: function () { return { getContext: function () { return { measureText: function (t) { return { width: t.length * 7 }; } }; } }; },
                console: console, setTimeout: setTimeout, clearTimeout: clearTimeout,
                performance: { now: function () { return Date.now(); } },
                location: { href: locationHref }
            };
            sandbox.self = sandbox;
            vm.createContext(sandbox);
            vm.runInContext(src, sandbox);
            return sandbox;
        }
        const s = host('blob:null/0f7c2a5e');
        const h = s.__kronikolWorkerHost, d = h.document;
        const out = {};
        // escaping of text and attributes
        const t = d.createElementNS(NS, 'text'); t.setAttribute('x', '1'); t.setAttribute('title', '"q"<&'); t.textContent = 'a & b < c > d e "q"';
        out.text = h.serialize(t);
        // attribute insertion order, overwrite in place
        const r = d.createElementNS(NS, 'rect'); r.setAttribute('z', '1'); r.setAttribute('a', '2'); r.setAttribute('z', '3');
        out.attrOrder = h.serialize(r);
        // processing instructions
        const g = d.createElement('g'); g.appendChild(d.createProcessingInstruction('xml-stylesheet', 'href="a.css"'));
        out.pi = h.serialize(g);
        // textContent get/set
        const p = d.createElement('p'); p.appendChild(d.createTextNode('one')); const span = d.createElement('span'); span.textContent = 'two'; p.appendChild(span);
        out.textGet = p.textContent; p.textContent = 'reset'; out.textSetChildren = p.childNodes.length; out.textSetSer = h.serialize(p);
        // cloneNode(true) copies attributes, children and PIs, independently of the original
        const c = d.createElementNS(NS, 'g'); c.setAttribute('id', 'c1'); const ct = d.createElementNS(NS, 'text'); ct.textContent = 'hi'; c.appendChild(ct); c.appendChild(d.createProcessingInstruction('pi', 'x'));
        const clone = c.cloneNode(true); ct.textContent = 'changed';
        out.cloneSer = h.serialize(clone); out.cloneIndependent = h.serialize(c) !== h.serialize(clone); out.cloneId = clone.id;
        // baseURI
        out.baseURIBlob = d.baseURI;
        out.baseURIHttp = host('http://localhost:8080/report.html').__kronikolWorkerHost.document.baseURI;
        // a render target serializes its CHILDREN (what innerHTML is), and clearing innerHTML first does not swallow them
        const target = d.createElement('div'); target.id = 't'; target.innerHTML = '';
        const svg = d.createElementNS(NS, 'svg'); svg.setAttribute('width', '10'); svg.appendChild(d.createElementNS(NS, 'g')); target.appendChild(svg);
        out.targetInner = target.innerHTML; out.targetChildren = h.serializeChildren(target);
        // DOMParser markup passes through raw; XMLSerializer uses the same serializer
        const parsed = new s.DOMParser().parseFromString('<svg><g/></svg>');
        out.raw = h.serialize(parsed.documentElement); out.xmlSer = new s.XMLSerializer().serializeToString(r);
        // getBBox is 0×0 (engine falls back to canvas metrics); canvas measures through OffscreenCanvas
        out.bboxWidth = d.createElementNS(NS, 'text').getBBox().width;
        out.measure = d.createElement('canvas').getContext('2d').measureText('abc').width;
        // Identity is checked inside the context: vm hands the code a contextified global, not the raw sandbox.
        out.globals = { window: vm.runInContext('self.window === self && window === self', s), document: s.document === d, HTMLElement: typeof s.HTMLElement, onmessage: typeof s.onmessage };
        process.stdout.write(JSON.stringify(out));
        """;

    private static readonly Lazy<JsonElement> Result = new(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), "kronikol-worker-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var hostPath = Path.Combine(dir, "plantuml-worker-host.js");
            File.WriteAllText(hostPath, DiagramContextMenu.GetPlantUmlWorkerHostScript());
            return JsonDocument.Parse(NodeProbe.Run(Driver, hostPath)).RootElement.Clone();
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    });

    private static JsonElement Out()
    {
        Assert.SkipWhen(!NodeProbe.IsAvailable, "Node.js not available on PATH");
        return Result.Value;
    }

    [Fact]
    public void Worker_host_is_an_embedded_resource_with_the_protocol_and_test_surface()
    {
        var host = DiagramContextMenu.GetPlantUmlWorkerHostScript();
        Assert.Contains("self.onmessage", host);
        Assert.Contains("type: 'ready'", host);
        Assert.Contains("type: 'done'", host);
        Assert.Contains("type: 'error'", host);
        Assert.Contains("type: 'fatal'", host);
        Assert.Contains("OffscreenCanvas", host);
        Assert.Contains("self.__kronikolWorkerHost", host);
        // The page inlines this source into a <script>: a literal close tag would end the script early.
        Assert.DoesNotContain("</script>", host, StringComparison.OrdinalIgnoreCase);
        // And it must not carry the render script's substitution tokens.
        Assert.DoesNotContain("__PLANTUML_", host);
        Assert.DoesNotContain("__BROWSER_", host);
    }

    [Fact]
    public void Serializer_escapes_text_and_attributes_like_innerHTML()
    {
        var o = Out();
        Assert.Equal("<text x=\"1\" title=\"&quot;q&quot;&lt;&amp;\">a &amp; b &lt; c &gt; d&nbsp;e \"q\"</text>", o.GetProperty("text").GetString());
    }

    [Fact]
    public void Serializer_keeps_attribute_insertion_order_and_overwrites_in_place()
    {
        Assert.Equal("<rect z=\"3\" a=\"2\"></rect>", Out().GetProperty("attrOrder").GetString());
    }

    [Fact]
    public void Serializer_emits_processing_instructions()
    {
        Assert.Equal("<g><?xml-stylesheet href=\"a.css\"?></g>", Out().GetProperty("pi").GetString());
    }

    [Fact]
    public void TextContent_getter_concatenates_descendants_and_setter_replaces_children()
    {
        var o = Out();
        Assert.Equal("onetwo", o.GetProperty("textGet").GetString());
        Assert.Equal(1, o.GetProperty("textSetChildren").GetInt32());
        Assert.Equal("<p>reset</p>", o.GetProperty("textSetSer").GetString());
    }

    [Fact]
    public void CloneNode_deep_copies_attributes_children_and_pis_independently()
    {
        var o = Out();
        Assert.Equal("<g id=\"c1\"><text>hi</text><?pi x?></g>", o.GetProperty("cloneSer").GetString());
        Assert.True(o.GetProperty("cloneIndependent").GetBoolean());
        Assert.Equal("c1", o.GetProperty("cloneId").GetString());
    }

    [Fact]
    public void BaseURI_is_https_under_a_blob_location_and_the_page_origin_under_http()
    {
        var o = Out();
        // viz-global resolves new URL("viz-global.js", document.baseURI): a blob: base throws.
        Assert.Equal("https://kronikol.invalid/", o.GetProperty("baseURIBlob").GetString());
        Assert.Equal("http://localhost:8080/report.html", o.GetProperty("baseURIHttp").GetString());
    }

    [Fact]
    public void Render_target_serializes_its_children_only_so_innerHTML_assignment_reproduces_the_engine_dom()
    {
        var o = Out();
        Assert.Equal("<svg width=\"10\"><g></g></svg>", o.GetProperty("targetInner").GetString());
        Assert.Equal("<svg width=\"10\"><g></g></svg>", o.GetProperty("targetChildren").GetString());
    }

    [Fact]
    public void DOMParser_markup_passes_through_raw_and_XMLSerializer_uses_the_same_serializer()
    {
        var o = Out();
        Assert.Equal("<svg><g/></svg>", o.GetProperty("raw").GetString());
        Assert.Equal("<rect z=\"3\" a=\"2\"></rect>", o.GetProperty("xmlSer").GetString());
    }

    [Fact]
    public void GetBBox_is_zero_and_canvas_text_is_measured_through_OffscreenCanvas()
    {
        var o = Out();
        Assert.Equal(0, o.GetProperty("bboxWidth").GetDouble());
        Assert.Equal(21, o.GetProperty("measure").GetDouble());
    }

    [Fact]
    public void Host_installs_window_document_element_globals_and_a_message_handler()
    {
        var g = Out().GetProperty("globals");
        Assert.True(g.GetProperty("window").GetBoolean());
        Assert.True(g.GetProperty("document").GetBoolean());
        Assert.Equal("function", g.GetProperty("HTMLElement").GetString());
        Assert.Equal("function", g.GetProperty("onmessage").GetString());
    }
}
