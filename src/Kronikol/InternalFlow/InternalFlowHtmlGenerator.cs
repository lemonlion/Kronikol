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
        InternalFlowTab startTab = InternalFlowTab.Activity)
    {
        var data = new Dictionary<string, object>();

        foreach (var (key, segment) in segments)
        {
            if (segment.Spans.Length > 0)
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

                var totalSpans = InternalFlowSpanStore.GetSpans().Length;
                var diagnosticHtml = BuildEmptyDiagnosticMessage(totalSpans, granularity, configuredActivitySources);

                data[key] = new
                {
                    message = diagnosticHtml
                };
            }
        }

        return data;
    }

    private static string Title(InternalFlowSegment segment) =>
        $"Internal Flow ({segment.Spans.Length} span{(segment.Spans.Length == 1 ? "" : "s")}"
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

    private static string RenderActivityDiagramHtml(InternalFlowSegment segment, Dictionary<string, string>? diagramDataMap = null, bool rawSource = false)
    {
        var plantuml = InternalFlowRenderer.RenderActivityDiagram(segment);
        var id = $"iflow-puml-{segment.RequestResponseId}-{segment.BoundaryType.ToString().ToLowerInvariant()}";
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
        if (!wholeTestSegments.TryGetValue(segmentKey, out var segment) || segment.Spans.Length == 0)
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

        return (activityHtml, flameHtml, segment.Spans.Length);
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
        if (!wholeTestSegments.TryGetValue(segmentKey, out var segment) || segment.Spans.Length == 0)
            return string.Empty;

        var spanCount = segment.Spans.Length;
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
        if (style == InternalFlowDiagramStyle.CallTree || flowData.AggregatedSegment.Spans.Length > maxActivityDiagramSpans)
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
