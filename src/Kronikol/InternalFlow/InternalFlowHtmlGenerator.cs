using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kronikol.ComponentDiagram;
using Kronikol.Reports;

namespace Kronikol.InternalFlow;

/// <summary>
/// Generates the JSON data block that the popup JavaScript reads
/// to display internal flow diagrams.
/// </summary>
public static class InternalFlowHtmlGenerator
{
    /// <summary>The id of the element that carries the segment map in a report.</summary>
    internal const string SegmentsElementId = "iflow-segments";

    /// <summary>
    /// Generates the segment map as one <c>&lt;script id="iflow-segments" type="application/json"&gt;</c> element
    /// (see <see cref="WrapSegmentData(Dictionary{string, object})"/>), or an empty string when no segment has
    /// anything to show.
    /// </summary>
    public static string GenerateSegmentDataScript(
        Dictionary<string, InternalFlowSegment> segments,
        InternalFlowDiagramStyle diagramStyle,
        bool showFlameChart = false,
        InternalFlowFlameChartPosition flameChartPosition = InternalFlowFlameChartPosition.BehindWithToggle,
        InternalFlowNoDataBehavior noDataBehavior = InternalFlowNoDataBehavior.HideLink,
        InternalFlowSpanGranularity granularity = InternalFlowSpanGranularity.AutoInstrumentation,
        string[]? configuredActivitySources = null,
        InternalFlowTab startTab = InternalFlowTab.Activity)
    {
        var data = BuildSegmentData(segments, diagramStyle, showFlameChart, flameChartPosition, noDataBehavior, granularity, configuredActivitySources, startTab);
        return WrapSegmentData(data);
    }

    /// <summary>
    /// Wraps a precomputed segment-data map in the element the report's popup reads:
    /// <c>&lt;script id="iflow-segments" type="application/json"&gt;{"has":[…],"z":"…"}&lt;/script&gt;</c>, where
    /// <c>z</c> is the map's JSON gzipped and base64'd, decoded by the page once, on the first popup, and
    /// <c>has</c> lists every key, so the page can tell which links open something without decoding. An empty map
    /// gives an empty string: the page reads a missing element as a map with no segment. Used both by
    /// <see cref="GenerateSegmentDataScript"/> and when re-rendering a merged report from previously serialized
    /// segment data. Before 3.31.9 this was a script setting <c>window.__iflowSegments</c> to the map as a
    /// JavaScript object; the page still reads that global when a page sets it.
    /// </summary>
    public static string WrapSegmentData(Dictionary<string, object> data) => WrapSegmentData(data, linkSources: null);

    /// <summary>
    /// The element <see cref="WrapSegmentData(Dictionary{string, object})"/> writes, with the shorter of the two
    /// exact membership lists over <paramref name="linkSources"/>, every PlantUML source the page embeds:
    /// <c>hidden</c>, the link ids the sources hold that have no segment, when there are fewer of those than
    /// <c>has</c>, the segment keys some source links. Either answers "does this link open a segment?" for every
    /// link the page can show. Null lists every key under <c>has</c>, which is exact for any page.
    /// </summary>
    internal static string WrapSegmentData(Dictionary<string, object> data, IEnumerable<string?>? linkSources)
    {
        if (data.Count == 0)
            return "";

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = false });
        var (list, ids) = MembershipList(data, linkSources);

        // The ids go through the serializer, which escapes anything HTML-sensitive; the base64 needs no escape at
        // all, and the serializer's default encoder would write every '+' in it as six characters.
        return new StringBuilder()
            .Append("<script id=\"").Append(SegmentsElementId).Append("\" type=\"application/json\">{\"")
            .Append(list).Append("\":").Append(JsonSerializer.Serialize(ids))
            .Append(",\"z\":\"").Append(CompressToBase64(json)).Append("\"}</script>")
            .ToString();
    }

    /// <summary>
    /// Which list the element carries and the ids in it, in the order the sources first link them (or the map's
    /// order, with no sources). A tie gives <c>has</c>.
    /// </summary>
    internal static (string List, IReadOnlyList<string> Ids) MembershipList(
        IReadOnlyDictionary<string, object> data, IEnumerable<string?>? linkSources)
    {
        if (linkSources is null)
            return ("has", data.Keys.ToList());

        var linked = LinkedIds(linkSources);
        var has = linked.Where(data.ContainsKey).ToList();
        var hidden = linked.Where(id => !data.ContainsKey(id)).ToList();
        return hidden.Count < has.Count ? ("hidden", hidden) : ("has", has);
    }

    /// <summary>
    /// The segments some arrow of <paramref name="linkSources"/> links (<c>plans/V4_PLAN.md</c> R6). The builder makes
    /// a segment of every call, but a segment no drawn arrow links can never be opened: the calls an arrow cap or a
    /// collapsed run leaves undrawn, those a <c>Skip</c> phase variant hides or an override block holds, and every
    /// call of a test the page does not show. Before 3.35.3 a report carried them all.
    /// </summary>
    internal static Dictionary<string, InternalFlowSegment> LinkedSegments(
        Dictionary<string, InternalFlowSegment> segments, IEnumerable<string?> linkSources)
    {
        var linked = LinkedIds(linkSources).ToHashSet(StringComparer.Ordinal);
        return segments.Where(s => linked.Contains(s.Key)).ToDictionary(s => s.Key, s => s.Value, StringComparer.Ordinal);
    }

    /// <summary>
    /// Every internal-flow link id (<c>[[#iflow-… …]]</c>) in the sources, once each, in first-seen order. The id
    /// ends at whitespace or a bracket, as the report's own link reader (<c>extractIflowMap</c>) reads it.
    /// </summary>
    internal static List<string> LinkedIds(IEnumerable<string?> sources)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ids = new List<string>();
        foreach (var source in sources)
        {
            if (string.IsNullOrEmpty(source))
                continue;
            foreach (Match match in LinkIdPattern.Matches(source))
                if (seen.Add(match.Groups[1].Value))
                    ids.Add(match.Groups[1].Value);
        }
        return ids;
    }

    private static readonly Regex LinkIdPattern = new(@"\[\[#(iflow-[^\s\]]+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Builds the per-segment data map (segment key → { title, content, flameData }) that the popup
    /// JavaScript consumes. Each fragment carries its payload inline (no shared <c>diagramDataMap</c>), so the map
    /// is fully self-contained and safe to serialize and merge: an activity diagram as its PlantUML in a
    /// <c>data-plantuml</c> attribute, since the report gzips the map whole (<see cref="WrapSegmentData(Dictionary{string, object})"/>).
    /// </summary>
    public static Dictionary<string, object> BuildSegmentData(
        Dictionary<string, InternalFlowSegment> segments,
        InternalFlowDiagramStyle diagramStyle,
        bool showFlameChart = false,
        InternalFlowFlameChartPosition flameChartPosition = InternalFlowFlameChartPosition.BehindWithToggle,
        InternalFlowNoDataBehavior noDataBehavior = InternalFlowNoDataBehavior.HideLink,
        InternalFlowSpanGranularity granularity = InternalFlowSpanGranularity.AutoInstrumentation,
        string[]? configuredActivitySources = null,
        InternalFlowTab startTab = InternalFlowTab.Activity) =>
        BuildSegmentData(segments, diagramStyle, showFlameChart, flameChartPosition, noDataBehavior, granularity,
            configuredActivitySources, startTab, suppliedSpans: null);

    /// <summary>
    /// <see cref="BuildSegmentData(Dictionary{string, InternalFlowSegment}, InternalFlowDiagramStyle, bool, InternalFlowFlameChartPosition, InternalFlowNoDataBehavior, InternalFlowSpanGranularity, string[], InternalFlowTab)"/>
    /// for a report whose spans were handed to it: <paramref name="suppliedSpans"/> is how many an ingest was given
    /// (<c>IngestRequest.Spans</c>), which an empty popup's diagnostic names in place of this process's span store;
    /// null reads the store, as an in-process report always has.
    /// </summary>
    internal static Dictionary<string, object> BuildSegmentData(
        Dictionary<string, InternalFlowSegment> segments,
        InternalFlowDiagramStyle diagramStyle,
        bool showFlameChart,
        InternalFlowFlameChartPosition flameChartPosition,
        InternalFlowNoDataBehavior noDataBehavior,
        InternalFlowSpanGranularity granularity,
        string[]? configuredActivitySources,
        InternalFlowTab startTab,
        int? suppliedSpans)
    {
        var data = new Dictionary<string, object>();

        foreach (var (key, segment) in segments)
        {
            if (segment.FlowSpans.Length > 0)
            {
                var mainContent = diagramStyle switch
                {
                    InternalFlowDiagramStyle.CallTree => InternalFlowRenderer.RenderCallTree(segment),
                    InternalFlowDiagramStyle.ActivityDiagram => RenderActivityDiagramHtml(segment, rawSource: true),
                    _ => RenderActivityDiagramHtml(segment, rawSource: true)
                };

                var content = mainContent;
                object? flameData = null;
                if (showFlameChart)
                {
                    flameData = InternalFlowRenderer.GetFlameChartData(segment);
                    var flamePlaceholder = "<div class=\"iflow-flame\" data-diagram-type=\"flamechart\"></div>";
                    var flameFirst = startTab == InternalFlowTab.FlameChart;
                    content = flameChartPosition switch
                    {
                        InternalFlowFlameChartPosition.Underneath =>
                            mainContent + "<hr style=\"margin:12px 0\">" + flamePlaceholder,
                        _ => // BehindWithToggle — the configured tab starts active
                            "<div class=\"iflow-toggle\">"
                            + $"<button class=\"iflow-toggle-btn{(flameFirst ? "" : " iflow-toggle-active")}\" data-view=\"main\">Activity</button>"
                            + $"<button class=\"iflow-toggle-btn{(flameFirst ? " iflow-toggle-active" : "")}\" data-view=\"flame\">Flame Chart</button>"
                            + "</div>"
                            + $"<div class=\"iflow-view iflow-view-main\"{(flameFirst ? " style=\"display:none\"" : "")}>" + mainContent + "</div>"
                            + $"<div class=\"iflow-view iflow-view-flame\"{(flameFirst ? "" : " style=\"display:none\"")}>" + flamePlaceholder + "</div>"
                    };
                }

                // Above whatever the popup shows, in each of its layouts.
                content = LeftOutNote(segment) + content;

                if (flameData is InternalFlowRenderer.FlameChartData { Spans.Length: > 0 } fd)
                {
                    data[key] = new
                    {
                        title = Title(segment),
                        content,
                        flameData = new { s = fd.Sources, f = fd.Spans }
                    };
                }
                else
                {
                    data[key] = new
                    {
                        title = Title(segment),
                        content
                    };
                }
            }
            else if (segment.SpansLeftOut > 0)
            {
                // Spans did start during this call, and none could be told from another test's: say so whatever the
                // setting, since a hidden link would read as "nothing happened" (#87).
                data[key] = new
                {
                    message = LeftOutNote(segment)
                };
            }
            else
            {
                if (noDataBehavior == InternalFlowNoDataBehavior.HideLink)
                    continue;

                var diagnosticHtml = suppliedSpans is { } supplied
                    ? BuildEmptySuppliedDiagnosticMessage(supplied, granularity, configuredActivitySources)
                    : BuildEmptyDiagnosticMessage(InternalFlowSpanStore.GetSpans().Length, granularity, configuredActivitySources);

                data[key] = new
                {
                    message = diagnosticHtml
                };
            }
        }

        return data;
    }

    /// <summary>
    /// The segment map a report writes, with each flow that more than one segment shows stored once (#86,
    /// <c>plans/V4_PLAN.md</c> R5): the first segment in the map that shows it keeps it, and each later one keeps its
    /// title and names that segment under <c>sameAs</c>. No segment's key is added or moved, so a merge, which keeps
    /// the first value it meets for a key and meets each call's key once, keeps every segment a segment names. A message
    /// stays with its segment.
    /// From 4.0.1 the segment that holds a flow keeps its title and names its diagram and its flame chart by their places
    /// in one table, <c>{"contents": […], "flames": […]}</c>, which ends the map under the first segment's key with a
    /// <c>~</c> before it: <c>{"title", "table", "contentAt", "flameAt"}</c>, <c>flameAt</c> left out with the flame chart
    /// off. The table holds each distinct diagram and flame chart once, in the order the map first shows them. The same
    /// calls starting at other moments draw one diagram, which gives each span's duration in whole milliseconds, and
    /// flame charts of their own, which place each span in time, so a diagram repeats where its flow does not; and
    /// diagrams beside diagrams and flame charts beside flame charts compress better than the two interleaved (on
    /// BreakfastProvider's lanes the element's gzip fell 8.0% and 8.7%, where storing each diagram once in place took
    /// 2.1% and 2.3%). The table's key is the first segment's, which no other shard holds, so a merge keeps every shard's
    /// table beside the segments that name it. The popup follows <c>sameAs</c>, then the table, and a map with each flow
    /// inline, as written before 4.0.1, or every flow, as written before 3.35.2, renders as it did.
    /// </summary>
    internal static Dictionary<string, object> StoreFlowsOnce(Dictionary<string, object> data)
    {
        var stored = new Dictionary<string, object>(data.Count + 1);
        var holders = new Dictionary<(string Content, string Flame), string>();
        var contentAt = new Dictionary<string, int>(StringComparer.Ordinal);
        var flameAt = new Dictionary<string, int>(StringComparer.Ordinal);
        var contents = new List<string>();
        var flames = new List<JsonElement>();
        string? table = null;
        foreach (var (key, value) in data)
        {
            table ??= "~" + key;
            var entry = JsonSerializer.SerializeToElement(value);
            if (!entry.TryGetProperty("content", out var content))
            {
                stored[key] = value;
                continue;
            }

            var text = content.GetString() ?? "";
            var hasFlame = entry.TryGetProperty("flameData", out var flame);
            var flameText = hasFlame ? flame.GetRawText() : "";
            var title = entry.TryGetProperty("title", out var t) ? t.GetString() : null;
            var flow = (text, flameText);
            if (!holders.TryAdd(flow, key))
            {
                stored[key] = title is null
                    ? new { sameAs = holders[flow] }
                    : new { title, sameAs = holders[flow] };
                continue;
            }

            var holder = new Dictionary<string, object>();
            if (title is not null)
                holder["title"] = title;
            holder["table"] = table;
            holder["contentAt"] = PlaceOf(text, contentAt, contents, text);
            if (hasFlame)
                holder["flameAt"] = PlaceOf(flameText, flameAt, flames, flame);
            stored[key] = holder;
        }

        if (contents.Count > 0)
            stored[table!] = new { contents, flames };
        return stored;
    }

    /// <summary>Where <paramref name="item"/> sits in <paramref name="list"/>, added at the end the first time it is seen.</summary>
    private static int PlaceOf<T>(string identity, Dictionary<string, int> places, List<T> list, T item)
    {
        if (places.TryGetValue(identity, out var at))
            return at;
        places[identity] = list.Count;
        list.Add(item);
        return list.Count - 1;
    }

    /// <summary>The first 16 hex digits of the SHA-256 of <paramref name="text"/>'s UTF-8: the name of a flow.</summary>
    private static string FlowHash(string text) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text)), 0, 8).ToLowerInvariant();

    private static string Title(InternalFlowSegment segment) =>
        $"Internal Flow ({segment.FlowSpans.Length} span{(segment.FlowSpans.Length == 1 ? "" : "s")}"
        + (segment.SpansLeftOut > 0 ? $", {segment.SpansLeftOut} left out)" : ")");

    /// <summary>
    /// What a popup says when spans that started during its call were left out, because a call of another test running
    /// at the same time would have taken them too and nothing in them says whose they are (#87,
    /// <c>plans/SPAN_ATTRIBUTION_PLAN.md</c>). Empty when none were.
    /// </summary>
    private static string LeftOutNote(InternalFlowSegment segment) =>
        segment.SpansLeftOut == 0
            ? ""
            : $"<p class=\"iflow-left-out\" style=\"font-size:0.85em;color:#666;margin:0 0 8px\">{segment.SpansLeftOut} span{(segment.SpansLeftOut == 1 ? "" : "s")} "
              + $"that started during this call {(segment.SpansLeftOut == 1 ? "is" : "are")} left out: a request of another test ran at the same time, "
              + "and neither the trace nor the span tree says which of the two they belong to.</p>";

    private static string BuildEmptyDiagnosticMessage(
        int totalSpansInStore,
        InternalFlowSpanGranularity granularity,
        string[]? configuredActivitySources)
    {
        var sb = new StringBuilder();
        sb.Append("No internal activity captured for this segment.");
        sb.Append("<br/><br/><details style=\"font-size:0.85em;color:#666\"><summary>Diagnostic info</summary><ul>");
        sb.Append($"<li>InternalFlowSpanStore: {totalSpansInStore} total span(s) globally</li>");
        sb.Append($"<li>Granularity: {granularity}</li>");

        if (configuredActivitySources is { Length: > 0 })
            sb.Append($"<li>Configured activity sources: {System.Net.WebUtility.HtmlEncode(string.Join(", ", configuredActivitySources))}</li>");
        else if (granularity == InternalFlowSpanGranularity.Manual)
            sb.Append("<li>⚠ Granularity is Manual but no InternalFlowActivitySources configured</li>");

        sb.Append("</ul><p>Common causes:</p><ul>");
        sb.Append("<li>ActivityListener not registered for the expected source</li>");
        sb.Append("<li>Activity.Stop() not called before InternalFlowSpanStore.Add()</li>");
        sb.Append("<li>Wrong InternalFlowSpanGranularity for your setup</li>");
        sb.Append("</ul></details>");
        return sb.ToString();
    }

    /// <summary>
    /// What an empty popup says in a report whose spans an ingest was handed: how many, and what leaves a call with
    /// none of them, which is the capture's join (the call's trace id and span id) or its clock, never a listener.
    /// </summary>
    private static string BuildEmptySuppliedDiagnosticMessage(
        int suppliedSpans,
        InternalFlowSpanGranularity granularity,
        string[]? configuredActivitySources)
    {
        var sb = new StringBuilder();
        sb.Append("No internal activity captured for this segment.");
        sb.Append("<br/><br/><details style=\"font-size:0.85em;color:#666\"><summary>Diagnostic info</summary><ul>");
        sb.Append($"<li>Spans supplied to the ingest: {suppliedSpans}</li>");
        if (granularity == InternalFlowSpanGranularity.Manual && configuredActivitySources is { Length: > 0 })
            sb.Append($"<li>Only the spans of these sources are drawn: {System.Net.WebUtility.HtmlEncode(string.Join(", ", configuredActivitySources))}</li>");
        sb.Append("</ul><p>Common causes:</p><ul>");
        sb.Append("<li>The call's activityTraceId names no trace in the span files, or the call carries none</li>");
        sb.Append("<li>The trace is shared with another test's calls and the call's activitySpanId is not a span of it</li>");
        sb.Append("<li>The spans started more than 50 ms before the request or after its response (clocks apart)</li>");
        sb.Append("</ul></details>");
        return sb.ToString();
    }

    private static string RenderActivityDiagramHtml(InternalFlowSegment segment, Dictionary<string, string>? diagramDataMap = null, bool rawSource = false)
    {
        var plantuml = InternalFlowRenderer.RenderActivityDiagram(segment);
        // Named for the flow, not the call: two calls that show the same spans get the same markup, which the report's
        // segment map then stores once (#86). One popup is open at a time, so the id is still one element's.
        var id = $"iflow-puml-{FlowHash(plantuml)}";
        // A popup's diagram inside the segment map, which is gzipped whole: gzip cannot shrink an island that is gzip
        // already, and base64 had grown it by a third (INTERNAL_FLOW_BLOB_PLAN §3.7, Q7). The popup renders the
        // attribute as it is; line breaks stay literal, which an attribute returns as written.
        if (rawSource)
            return $"<div class=\"plantuml-browser iflow-diagram\" id=\"{id}\" data-plantuml=\"{EscapeAttribute(plantuml)}\" data-diagram-type=\"plantuml\"></div>";
        var compressed = CompressToBase64(plantuml);
        if (diagramDataMap is not null)
        {
            diagramDataMap[id] = compressed;
            return $"<div class=\"plantuml-browser iflow-diagram\" id=\"{id}\" data-diagram-type=\"plantuml\"></div>";
        }
        return $"<div class=\"plantuml-browser iflow-diagram\" id=\"{id}\" data-plantuml-z=\"{compressed}\" data-diagram-type=\"plantuml\"></div>";
    }

    /// <summary>
    /// Returns the activity diagram and flame chart HTML content for a specific test,
    /// without any wrapper elements or toggle buttons. Returns null if no data is available.
    /// </summary>
    public static (string ActivityHtml, string FlameHtml, int SpanCount)? GetWholeTestFlowContent(
        Dictionary<string, InternalFlowSegment> wholeTestSegments,
        string testId,
        (string Label, DateTimeOffset Timestamp)[] boundaryLogs,
        WholeTestFlowVisualization visualization,
        Dictionary<string, string>? diagramDataMap = null)
    {
        if (visualization == WholeTestFlowVisualization.None)
            return null;

        var segmentKey = $"iflow-test-{testId}";
        if (!wholeTestSegments.TryGetValue(segmentKey, out var segment) || segment.FlowSpans.Length == 0)
            return null;

        var activityHtml = "";
        var flameHtml = "";

        if (visualization is WholeTestFlowVisualization.ActivityDiagram or WholeTestFlowVisualization.Both)
            activityHtml = RenderWholeTestActivityDiagramHtml(segment, diagramDataMap);

        if (visualization is WholeTestFlowVisualization.FlameChart or WholeTestFlowVisualization.Both)
        {
            var flameData = InternalFlowRenderer.GetFlameChartDataWithMarkers(segment, boundaryLogs);
            var flameJson = JsonSerializer.Serialize(
                flameData.Markers != null
                    ? (object)new { s = flameData.Sources, f = flameData.Spans, m = flameData.Markers }
                    : new { s = flameData.Sources, f = flameData.Spans },
                new JsonSerializerOptions { WriteIndented = false });
            var compressedFlame = CompressToBase64(flameJson);
            flameHtml = $"<div class=\"iflow-flame\" data-diagram-type=\"flamechart\" data-flame-z=\"{compressedFlame}\"></div>";
        }

        return (activityHtml, flameHtml, segment.FlowSpans.Length);
    }

    /// <summary>
    /// Generates an inline collapsed &lt;details&gt; block containing the whole-test
    /// flamechart and/or activity diagram for a specific test.
    /// </summary>
    public static string GenerateWholeTestFlowHtml(
        Dictionary<string, InternalFlowSegment> wholeTestSegments,
        string testId,
        (string Label, DateTimeOffset Timestamp)[] boundaryLogs,
        WholeTestFlowVisualization visualization,
        InternalFlowTab startTab = InternalFlowTab.Activity)
    {
        if (visualization == WholeTestFlowVisualization.None)
            return string.Empty;

        var segmentKey = $"iflow-test-{testId}";
        if (!wholeTestSegments.TryGetValue(segmentKey, out var segment) || segment.FlowSpans.Length == 0)
            return string.Empty;

        var spanCount = segment.FlowSpans.Length;
        var sb = new StringBuilder();
        sb.AppendLine($"<details class=\"whole-test-flow\">");
        sb.AppendLine($"<summary class=\"h4\">Whole Test Flow ({spanCount} span{(spanCount == 1 ? "" : "s")})</summary>");

        switch (visualization)
        {
            case WholeTestFlowVisualization.FlameChart:
            {
                var flameData = InternalFlowRenderer.GetFlameChartDataWithMarkers(segment, boundaryLogs);
                var flameJson = JsonSerializer.Serialize(
                    flameData.Markers != null
                        ? (object)new { s = flameData.Sources, f = flameData.Spans, m = flameData.Markers }
                        : new { s = flameData.Sources, f = flameData.Spans },
                    new JsonSerializerOptions { WriteIndented = false });
                var compressedFlame = CompressToBase64(flameJson);
                sb.Append($"<div class=\"iflow-flame\" data-diagram-type=\"flamechart\" data-flame-z=\"{compressedFlame}\"></div>");
                break;
            }

            case WholeTestFlowVisualization.ActivityDiagram:
                sb.Append(RenderWholeTestActivityDiagramHtml(segment));
                break;

            case WholeTestFlowVisualization.Both:
            {
                var activityHtml = RenderWholeTestActivityDiagramHtml(segment);
                var flameData = InternalFlowRenderer.GetFlameChartDataWithMarkers(segment, boundaryLogs);
                var flameJson = JsonSerializer.Serialize(
                    flameData.Markers != null
                        ? (object)new { s = flameData.Sources, f = flameData.Spans, m = flameData.Markers }
                        : new { s = flameData.Sources, f = flameData.Spans },
                    new JsonSerializerOptions { WriteIndented = false });
                var compressedFlame = CompressToBase64(flameJson);
                var wtfFlameFirst = startTab == InternalFlowTab.FlameChart;
                sb.Append("<div class=\"iflow-toggle\">");
                sb.Append($"<button class=\"iflow-toggle-btn{(wtfFlameFirst ? "" : " iflow-toggle-active")}\" data-view=\"main\">Activity</button>");
                sb.Append($"<button class=\"iflow-toggle-btn{(wtfFlameFirst ? " iflow-toggle-active" : "")}\" data-view=\"flame\">Flame Chart</button>");
                sb.Append("</div>");
                sb.Append($"<div class=\"iflow-view iflow-view-main\"{(wtfFlameFirst ? " style=\"display:none\"" : "")}>{activityHtml}</div>");
                sb.Append($"<div class=\"iflow-view iflow-view-flame\"{(wtfFlameFirst ? "" : " style=\"display:none\"")}><div class=\"iflow-flame\" data-diagram-type=\"flamechart\" data-flame-z=\"{compressedFlame}\"></div></div>");
                break;
            }
        }

        sb.AppendLine("</details>");
        return sb.ToString();
    }

    private static string RenderWholeTestActivityDiagramHtml(InternalFlowSegment segment, Dictionary<string, string>? diagramDataMap = null)
    {
        var batches = InternalFlowRenderer.RenderActivityDiagramBatched(segment);
        if (batches.Length == 0)
            return string.Empty;

        var sb = new StringBuilder();
        for (var i = 0; i < batches.Length; i++)
        {
            var id = batches.Length == 1
                ? $"iflow-puml-whole-{segment.TestId}"
                : $"iflow-puml-whole-{segment.TestId}-{i}";
            var compressed = CompressToBase64(batches[i]);
            if (diagramDataMap is not null)
            {
                diagramDataMap[id] = compressed;
                sb.Append($"<div class=\"plantuml-browser iflow-diagram\" id=\"{id}\" data-diagram-type=\"plantuml\"></div>");
            }
            else
            {
                sb.Append($"<div class=\"plantuml-browser iflow-diagram\" id=\"{id}\" data-plantuml-z=\"{compressed}\" data-diagram-type=\"plantuml\"></div>");
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Generates HTML content for a relationship flow popup, including the aggregated
    /// flow diagram and a summary table of tests sorted by duration.
    /// </summary>
    public static string GenerateRelationshipPopupContent(
        RelationshipFlowData flowData,
        InternalFlowDiagramStyle style)
    {
        var sb = new StringBuilder();

        const int maxActivityDiagramSpans = 2000;
        if (style == InternalFlowDiagramStyle.CallTree || flowData.AggregatedSegment.FlowSpans.Length > maxActivityDiagramSpans)
            sb.Append(InternalFlowRenderer.RenderCallTree(flowData.AggregatedSegment));
        else
            sb.Append(RenderActivityDiagramHtml(flowData.AggregatedSegment));

        sb.AppendLine("<table class=\"iflow-rel-summary-table\">");
        sb.AppendLine("<tr><th>Test</th><th>Spans</th><th>Duration</th></tr>");

        var top20 = flowData.TestSummaries.Take(20);
        foreach (var summary in top20)
        {
            var name = System.Net.WebUtility.HtmlEncode(summary.TestName);
            sb.AppendLine($"<tr><td>{name}</td><td>{summary.SpanCount}</td><td>{summary.DurationMs:F0}ms</td></tr>");
        }

        if (flowData.TestSummaries.Length > 20)
            sb.AppendLine($"<tr><td colspan=\"3\" style=\"color:#888;font-style:italic\">...and {flowData.TestSummaries.Length - 20} more tests</td></tr>");

        sb.AppendLine("</table>");
        return sb.ToString();
    }

    /// <summary>A value for a double-quoted attribute: the three characters that could end it or start markup.</summary>
    private static string EscapeAttribute(string text) =>
        text.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;");

    internal static string CompressToBase64(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(bytes, 0, bytes.Length);
        }
        return Convert.ToBase64String(output.ToArray());
    }

    internal static string DecompressFromBase64(string base64)
    {
        using var input = new MemoryStream(Convert.FromBase64String(base64));
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
