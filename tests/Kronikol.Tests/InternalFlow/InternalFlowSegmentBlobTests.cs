using System.IO.Compression;
using System.Text.Json;
using Kronikol.InternalFlow;
using Kronikol.Reports;
using Kronikol.Reports.Merge;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Tests.InternalFlow;

/// <summary>
/// The segment map as one gzip blob (#89, INTERNAL_FLOW_BLOB_PLAN §3): the element, the round trip of the map
/// through <c>z</c>, and the membership list the render script binds links from without decoding.
/// </summary>
public class InternalFlowSegmentBlobTests
{
    private const string ElementStart = "<script id=\"iflow-segments\" type=\"application/json\">";

    private sealed record Element(string List, string[] Ids, string Z);

    private static Element Parse(string html)
    {
        Assert.StartsWith(ElementStart, html, StringComparison.Ordinal);
        Assert.EndsWith("</script>", html, StringComparison.Ordinal);
        using var payload = JsonDocument.Parse(html[ElementStart.Length..^"</script>".Length]);
        var properties = payload.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(2, properties.Length);
        Assert.Equal("z", properties[1]);
        var list = properties[0];
        Assert.Contains(list, new[] { "has", "hidden" });
        return new Element(
            list,
            payload.RootElement.GetProperty(list).EnumerateArray().Select(e => e.GetString()!).ToArray(),
            payload.RootElement.GetProperty("z").GetString()!);
    }

    private static string Gunzip(string base64)
    {
        using var gzip = new GZipStream(new MemoryStream(Convert.FromBase64String(base64)), CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        return reader.ReadToEnd();
    }

    private static Dictionary<string, object> Map(params string[] keys) =>
        keys.ToDictionary(k => k, k => (object)new { title = "Internal Flow (1 span)", content = $"<div>{k}</div>" });

    private static string Linked(params string[] ids) =>
        "@startuml\n" + string.Concat(ids.Select(id => $"A -> B: [[#{id} GET /x]]\n")) + "@enduml";

    [Fact]
    public void The_map_round_trips_through_z_as_the_same_json()
    {
        // The decoded text is byte for byte the object literal the page used to parse: the popup reads the same map.
        var data = new Dictionary<string, object>
        {
            ["iflow-a"] = new { title = "Internal Flow (2 spans)", content = "<div data-x=\"1\">a < b & 'c' + d</div>", flameData = new { s = new[] { "src" }, f = new[] { new object[] { 0, 1.5, "op" } } } },
            ["iflow-b"] = new { message = "No internal activity captured for this segment." },
        };

        var element = Parse(InternalFlowHtmlGenerator.WrapSegmentData(data));

        Assert.Equal(JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = false }), Gunzip(element.Z));
    }

    [Fact]
    public void The_element_is_a_json_script_that_sets_no_global()
    {
        var html = InternalFlowHtmlGenerator.WrapSegmentData(Map("iflow-a"));

        Assert.StartsWith(ElementStart, html, StringComparison.Ordinal);
        Assert.DoesNotContain("window.__iflowSegments", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_base64_is_written_raw()
    {
        // The serializer's default encoder writes '+' as +; the blob is base64, which needs no escape in JSON or
        // in a script element. Enough keys that the base64 holds a '+' at all.
        var data = Enumerable.Range(0, 200).ToDictionary(i => $"iflow-{i:D4}", i => (object)new { title = $"t{i}", content = $"<p>{Guid.NewGuid()}</p>" });

        var html = InternalFlowHtmlGenerator.WrapSegmentData(data);
        var element = Parse(html);

        Assert.Contains('+', element.Z);
        Assert.DoesNotContain("\\u002B", html, StringComparison.Ordinal);
        Assert.Contains(element.Z, html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_public_wrapper_lists_every_key_as_has()
    {
        var element = Parse(InternalFlowHtmlGenerator.WrapSegmentData(Map("iflow-a", "iflow-b", "iflow-c")));

        Assert.Equal("has", element.List);
        Assert.Equal(["iflow-a", "iflow-b", "iflow-c"], element.Ids);
    }

    [Fact]
    public void No_element_when_the_map_is_empty()
    {
        Assert.Equal("", InternalFlowHtmlGenerator.WrapSegmentData(new Dictionary<string, object>()));
        Assert.Equal("", InternalFlowHtmlGenerator.WrapSegmentData(new Dictionary<string, object>(), [Linked("iflow-a")]));
        Assert.Equal("", InternalFlowHtmlGenerator.GenerateSegmentDataScript([], InternalFlowDiagramStyle.ActivityDiagram));
    }

    [Fact]
    public void The_list_is_hidden_when_fewer_links_lack_a_segment_than_have_one()
    {
        var element = Parse(InternalFlowHtmlGenerator.WrapSegmentData(
            Map("iflow-a", "iflow-b", "iflow-c"), [Linked("iflow-a", "iflow-b", "iflow-c", "iflow-d")]));

        Assert.Equal("hidden", element.List);
        Assert.Equal(["iflow-d"], element.Ids);
    }

    [Fact]
    public void The_list_is_has_when_fewer_links_have_a_segment_than_lack_one()
    {
        var element = Parse(InternalFlowHtmlGenerator.WrapSegmentData(
            Map("iflow-a"), [Linked("iflow-a", "iflow-x", "iflow-y", "iflow-z")]));

        Assert.Equal("has", element.List);
        Assert.Equal(["iflow-a"], element.Ids);
    }

    [Fact]
    public void A_tie_lists_has()
    {
        var element = Parse(InternalFlowHtmlGenerator.WrapSegmentData(
            Map("iflow-a", "iflow-b"), [Linked("iflow-a", "iflow-x", "iflow-b", "iflow-y")]));

        Assert.Equal("has", element.List);
        Assert.Equal(["iflow-a", "iflow-b"], element.Ids);
    }

    [Fact]
    public void A_key_no_source_links_is_in_neither_list()
    {
        // Only what a diagram can present needs an answer: a segment nothing links is never asked about.
        var data = Map("iflow-a", "iflow-orphan");

        var has = Parse(InternalFlowHtmlGenerator.WrapSegmentData(data, [Linked("iflow-a", "iflow-x", "iflow-y")]));
        var hidden = Parse(InternalFlowHtmlGenerator.WrapSegmentData(Map("iflow-a", "iflow-b", "iflow-orphan"), [Linked("iflow-a", "iflow-b", "iflow-x")]));

        Assert.Equal("has", has.List);
        Assert.Equal(["iflow-a"], has.Ids);
        Assert.Equal("hidden", hidden.List);
        Assert.Equal(["iflow-x"], hidden.Ids);
    }

    [Fact]
    public void The_lists_cover_every_source_handed_in_once_each_in_first_seen_order()
    {
        // Sequence diagrams and the component diagram, whose edges link iflow-rel- ids; a source may repeat an id and
        // be null (no component diagram).
        var component = "@startuml\nrectangle A\nrectangle B\nA --> B : [[#iflow-rel-a-b GET]]\\n12 ms\n@enduml";
        var element = Parse(InternalFlowHtmlGenerator.WrapSegmentData(
            Map("iflow-2", "iflow-1", "iflow-rel-a-b", "iflow-3"),
            [Linked("iflow-1", "iflow-2", "iflow-1"), null, Linked("iflow-2", "iflow-miss"), component]));

        Assert.Equal("hidden", element.List);
        Assert.Equal(["iflow-miss"], element.Ids);
        Assert.Equal(
            ["iflow-1", "iflow-2", "iflow-miss", "iflow-rel-a-b"],
            InternalFlowHtmlGenerator.LinkedIds([Linked("iflow-1", "iflow-2", "iflow-1"), null, Linked("iflow-2", "iflow-miss"), component]));
    }

    [Fact]
    public void An_id_ends_where_the_page_ends_it()
    {
        // extractIflowMap reads iflow-[^\s\]]+ : a link with no label ends at the brackets.
        Assert.Equal(["iflow-a", "iflow-b"], InternalFlowHtmlGenerator.LinkedIds(["A -> B: [[#iflow-a]]\nA -> B: [[#iflow-b\tGET]]"]));
    }

    [Fact]
    public void The_live_and_the_merge_emit_sites_write_the_same_element()
    {
        // The merge renderer reads the map back from the data file as JsonElement values; boxed, they serialize to the
        // text the live run's objects did, so a merged report carries the element the shard's own report did.
        var data = new Dictionary<string, object>
        {
            ["iflow-a"] = new { title = "Internal Flow (2 spans)", content = "<div class=\"plantuml-browser\" data-plantuml-z=\"H4sI+/=\"></div>", flameData = new { s = new[] { "Microsoft.AspNetCore" }, f = new[] { new object[] { 0, 0, 12.5, 3.25, "GET /x" } } } },
            ["iflow-b"] = new { message = "No internal activity captured for this segment.<br/>" },
        };
        var diagrams = new[] { new DiagramAsCode("t1", "", Linked("iflow-a", "iflow-b", "iflow-c")) };
        var options = new ReportConfigurationOptions();

        var live = DiagramContextMenu.GetInternalFlowConfigScript(options.InternalFlowHasDataBehavior)
                   + InternalFlowHtmlGenerator.WrapSegmentData(data, diagrams.Select(d => (string?)d.CodeBehind).Append(null).ToArray());

        var json = JsonSerializer.Serialize(data);
        using var document = JsonDocument.Parse(json);
        var report = new MergeableReport
        {
            Diagrams = diagrams,
            InternalFlowSegments = document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()),
        };
        var merged = MergeableReportRenderer.BuildInternalFlowDataScript(report, options, componentDiagramPlantUml: null);

        Assert.Equal(live, merged);
    }
}
