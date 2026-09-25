using System.Diagnostics;
using System.Text;

namespace Kronikol.InternalFlow;

/// <summary>
/// Renders internal flow segments as PlantUML activity diagrams,
/// call trees, or flame charts.
/// </summary>
public static class InternalFlowRenderer
{
    public static string RenderActivityDiagram(InternalFlowSegment segment)
    {
        if (segment.Spans.Length == 0)
            return string.Empty;

        var roots = BuildSpanTree(segment.Spans);
        var sb = new StringBuilder();
        sb.AppendLine("@startuml");
        sb.AppendLine("skinparam ActivityBackgroundColor #f0f4ff");
        sb.AppendLine("skinparam ActivityBorderColor #666");
        sb.AppendLine("skinparam SwimlaneBorderColor #ccc");
        // Activity diagrams were the one family Kronikol emitted with no wrapWidth and no character
        // cap of any kind, so one EF-shaped DisplayName drew the whole diagram thousands of pixels
        // wide (measured: a 120-word label drew 5 962 px, and 828 with this line).
        sb.AppendLine($"skinparam wrapWidth {PlantUml.DiagramWidth.DefaultWrapWidthPx}");

        var currentSwimlane = "";
        foreach (var root in roots)
            RenderActivityNode(sb, root, ref currentSwimlane, 0);

        sb.AppendLine("@enduml");
        return sb.ToString();
    }

    public static string[] RenderActivityDiagramBatched(InternalFlowSegment segment, int maxSpansPerBatch = 100)
    {
        if (segment.Spans.Length == 0)
            return [];

        var roots = BuildSpanTree(segment.Spans);

        // If total spans fit in one batch, return a single diagram (no header label)
        if (segment.Spans.Length <= maxSpansPerBatch)
            return [RenderActivityDiagram(segment)];

        // Split roots into batches — each root and all its descendants stay together
        var batches = new List<List<SpanNode>>();
        var currentBatch = new List<SpanNode>();
        var currentCount = 0;

        foreach (var root in roots)
        {
            var nodeCount = CountNodes(root);
            if (currentBatch.Count > 0 && currentCount + nodeCount > maxSpansPerBatch)
            {
                batches.Add(currentBatch);
                currentBatch = [];
                currentCount = 0;
            }
            currentBatch.Add(root);
            currentCount += nodeCount;
        }
        if (currentBatch.Count > 0)
            batches.Add(currentBatch);

        const int maxBatches = 3;
        var totalBatches = batches.Count;
        var truncated = totalBatches > maxBatches;
        var renderCount = truncated ? maxBatches : totalBatches;

        var results = new string[renderCount];
        for (var i = 0; i < renderCount; i++)
        {
            var sb = new StringBuilder();
            sb.AppendLine("@startuml");
            sb.AppendLine("skinparam ActivityBackgroundColor #f0f4ff");
            sb.AppendLine("skinparam ActivityBorderColor #666");
            sb.AppendLine("skinparam SwimlaneBorderColor #ccc");
            sb.AppendLine($"skinparam wrapWidth {PlantUml.DiagramWidth.DefaultWrapWidthPx}");
            var title = truncated
                ? $"title Part {i + 1} of {totalBatches} (showing first {maxBatches})"
                : $"title Part {i + 1} of {totalBatches}";
            sb.AppendLine(title);

            var currentSwimlane = "";
            foreach (var root in batches[i])
                RenderActivityNode(sb, root, ref currentSwimlane, 0);

            sb.AppendLine("@enduml");
            results[i] = sb.ToString();
        }

        return results;
    }

    private static int CountNodes(SpanNode node)
    {
        var count = 1;
        foreach (var child in node.Children)
            count += CountNodes(child);
        return count;
    }

    private static void RenderActivityNode(StringBuilder sb, SpanNode node, ref string currentSwimlane, int depth)
    {
        var source = string.IsNullOrEmpty(node.Span.Source.Name) ? "Unknown" : node.Span.Source.Name;
        if (source != currentSwimlane)
        {
            sb.AppendLine($"|{WrapForWidth(EscapePlantUml(source))}|");
            currentSwimlane = source;
        }

        // wrapWidth breaks at whitespace only, and the labels that get long here are SQL text and
        // fully-qualified type names — the character budget is what bounds the ones that have none.
        var label = WrapForWidth(EscapePlantUml(node.Span.DisplayName ?? node.Span.OperationName));
        var duration = node.Span.Duration.TotalMilliseconds;
        sb.AppendLine(duration >= 1
            ? $":{label} ({duration:F0}ms);"
            : $":{label};");

        foreach (var child in node.Children)
            RenderActivityNode(sb, child, ref currentSwimlane, depth + 1);
    }

    public static string RenderCallTree(InternalFlowSegment segment)
    {
        if (segment.Spans.Length == 0)
            return string.Empty;

        var roots = BuildSpanTree(segment.Spans);
        var sb = new StringBuilder();
        sb.AppendLine("<ul class=\"iflow-call-tree\" data-diagram-type=\"calltree\">");
        foreach (var root in roots)
            RenderTreeNode(sb, root);
        sb.AppendLine("</ul>");
        return sb.ToString();
    }

    private static void RenderTreeNode(StringBuilder sb, SpanNode node)
    {
        var duration = node.Span.Duration.TotalMilliseconds;
        var durationText = duration >= 1 ? $" <span class=\"iflow-duration\">({duration:F0}ms)</span>" : "";
        var source = string.IsNullOrEmpty(node.Span.Source.Name) ? "" : $"<span class=\"iflow-source\">[{System.Net.WebUtility.HtmlEncode(node.Span.Source.Name)}]</span> ";
        var name = System.Net.WebUtility.HtmlEncode(node.Span.DisplayName ?? node.Span.OperationName);

        sb.AppendLine($"<li>{source}{name}{durationText}");
        if (node.Children.Count > 0)
        {
            sb.AppendLine("<ul>");
            foreach (var child in node.Children)
                RenderTreeNode(sb, child);
            sb.AppendLine("</ul>");
        }
        sb.AppendLine("</li>");
    }

    internal static List<SpanNode> BuildSpanTree(Activity[] spans)
    {
        var nodesById = new Dictionary<string, SpanNode>(spans.Length);
        foreach (var span in spans)
            nodesById.TryAdd(span.SpanId.ToString(), new SpanNode(span));

        var roots = new List<SpanNode>();

        foreach (var node in nodesById.Values)
        {
            var parentId = node.Span.ParentSpanId.ToString();
            if (parentId != null && nodesById.TryGetValue(parentId, out var parent))
                parent.Children.Add(node);
            else
                roots.Add(node);
        }

        roots.Sort((a, b) => a.Span.StartTimeUtc.CompareTo(b.Span.StartTimeUtc));
        foreach (var node in nodesById.Values)
            node.Children.Sort((a, b) => a.Span.StartTimeUtc.CompareTo(b.Span.StartTimeUtc));

        return roots;
    }

    private static string EscapePlantUml(string text)
    {
        // A span name quoting an OpenIconic icon, an emoji or a sprite would make the engine load a bundle
        // the report's renderers cannot, or drop the text (PlantUmlCreator.EscapeLoaderMarkup).
        return PlantUml.PlantUmlCreator.EscapeLoaderMarkup(text.Replace("|", "\\|").Replace(";", "\\;"));
    }

    /// <summary>
    /// Breaks an already-escaped activity label or swimlane name onto display lines of at most
    /// <see cref="PlantUml.DiagramWidth.MaxLabelLineChars"/> characters. Escaping runs first so the
    /// wrapper never cuts an escape sequence in half.
    /// </summary>
    private static string WrapForWidth(string escaped) =>
        PlantUml.DiagramWidth.Wrap(escaped, PlantUml.DiagramWidth.MaxLabelLineChars);

    /// <summary>
    /// Returns compact flame chart data for client-side rendering.
    /// </summary>
    public static FlameChartData GetFlameChartData(InternalFlowSegment segment, int maxSpans = 2000)
    {
        if (segment.Spans.Length == 0)
            return FlameChartData.Empty;

        var roots = BuildSpanTree(segment.Spans);
        var earliest = segment.Spans.Min(s => s.StartTimeUtc);
        var latest = segment.Spans.Max(s => s.StartTimeUtc + s.Duration);
        var totalMs = (latest - earliest).TotalMilliseconds;
        if (totalMs <= 0) totalMs = 1;

        var sources = new List<string>();
        var sourceIndex = new Dictionary<string, int>();
        var spans = new List<object[]>();
        var spanCount = 0;

        void FlattenNode(SpanNode node, int depth)
        {
            var source = string.IsNullOrEmpty(node.Span.Source.Name) ? "Unknown" : node.Span.Source.Name;
            if (!sourceIndex.TryGetValue(source, out var srcIdx))
            {
                srcIdx = sources.Count;
                sourceIndex[source] = srcIdx;
                sources.Add(source);
            }

            var offsetMs = (node.Span.StartTimeUtc - earliest).TotalMilliseconds;
            var durationMs = node.Span.Duration.TotalMilliseconds;
            var leftPct = Math.Round((offsetMs / totalMs) * 100, 2);
            var widthPct = Math.Round(Math.Max((durationMs / totalMs) * 100, 0.5), 2);
            var name = node.Span.DisplayName ?? node.Span.OperationName;
            var durMs = durationMs >= 1 ? (int)Math.Round(durationMs) : 0;

            // [srcIdx, name, leftPct, widthPct, depth, durationMs]
            spans.Add([srcIdx, name, leftPct, widthPct, depth, durMs]);
            spanCount++;

            foreach (var child in node.Children)
                FlattenNode(child, depth + 1);
        }

        foreach (var root in roots)
        {
            if (spanCount >= maxSpans)
                break;
            FlattenNode(root, 0);
        }

        return new FlameChartData(sources.ToArray(), spans.ToArray());
    }

    /// <summary>
    /// Returns compact flame chart data with boundary markers for client-side rendering.
    /// </summary>
    public static FlameChartData GetFlameChartDataWithMarkers(
        InternalFlowSegment segment,
        (string Label, DateTimeOffset Timestamp)[] boundaryLogs)
    {
        var data = GetFlameChartData(segment);
        if (data == FlameChartData.Empty)
            return data;

        var earliest = segment.Spans.Min(s => s.StartTimeUtc);
        var latest = segment.Spans.Max(s => s.StartTimeUtc + s.Duration);
        var totalMs = (latest - earliest).TotalMilliseconds;
        if (totalMs <= 0) totalMs = 1;

        var markers = new List<object[]>();
        foreach (var (label, timestamp) in boundaryLogs)
        {
            var offsetMs = (timestamp.UtcDateTime - earliest).TotalMilliseconds;
            if (offsetMs >= 0 && offsetMs <= totalMs)
            {
                var leftPct = Math.Round((offsetMs / totalMs) * 100, 2);
                markers.Add([leftPct, label]);
            }
        }

        return new FlameChartData(data.Sources, data.Spans, markers.Count > 0 ? markers.ToArray() : null);
    }

    /// <summary>
    /// Returns compact flame chart data for a sequential test flame chart.
    /// </summary>
    public static SequentialFlameChartData GetSequentialFlameChartData(
        Dictionary<string, InternalFlowSegment> wholeTestSegments)
    {
        if (wholeTestSegments.Count == 0)
            return SequentialFlameChartData.Empty;

        var globalSources = new List<string>();
        var globalSourceIndex = new Dictionary<string, int>();
        var bands = new List<object>();

        foreach (var (key, segment) in wholeTestSegments.OrderBy(kv => kv.Value.StartTime))
        {
            if (segment.Spans.Length == 0) continue;

            var bandData = GetFlameChartData(segment);
            // Remap source indices to global list
            var localToGlobal = new int[bandData.Sources.Length];
            for (var i = 0; i < bandData.Sources.Length; i++)
            {
                if (!globalSourceIndex.TryGetValue(bandData.Sources[i], out var gIdx))
                {
                    gIdx = globalSources.Count;
                    globalSourceIndex[bandData.Sources[i]] = gIdx;
                    globalSources.Add(bandData.Sources[i]);
                }
                localToGlobal[i] = gIdx;
            }

            var remappedSpans = bandData.Spans.Select(s =>
                new object[] { localToGlobal[(int)s[0]], s[1], s[2], s[3], s[4], s[5] }).ToArray();

            bands.Add(new { id = segment.TestId, f = remappedSpans });
        }

        return new SequentialFlameChartData(globalSources.ToArray(), bands.ToArray());
    }

    /// <summary>
    /// Renders a flame chart as HTML horizontal bars showing relative duration.
    /// Each bar's width is proportional to the span's duration relative to the
    /// total segment time window.
    /// </summary>
    public static string RenderFlameChart(InternalFlowSegment segment)
    {
        if (segment.Spans.Length == 0)
            return string.Empty;

        var roots = BuildSpanTree(segment.Spans);
        var earliest = segment.Spans.Min(s => s.StartTimeUtc);
        var latest = segment.Spans.Max(s => s.StartTimeUtc + s.Duration);
        var totalMs = (latest - earliest).TotalMilliseconds;
        if (totalMs <= 0) totalMs = 1;

        var sb = new StringBuilder();
        sb.AppendLine("<div class=\"iflow-flame\" data-diagram-type=\"flamechart\">");
        foreach (var root in roots)
            RenderFlameNode(sb, root, earliest, totalMs, 0);
        sb.AppendLine("</div>");
        return sb.ToString();
    }

    private static void RenderFlameNode(StringBuilder sb, SpanNode node, DateTime earliest, double totalMs, int depth)
    {
        var offsetMs = (node.Span.StartTimeUtc - earliest).TotalMilliseconds;
        var durationMs = node.Span.Duration.TotalMilliseconds;
        var leftPct = (offsetMs / totalMs) * 100;
        var widthPct = Math.Max((durationMs / totalMs) * 100, 0.5); // min 0.5% so it's visible

        var source = System.Net.WebUtility.HtmlEncode(
            string.IsNullOrEmpty(node.Span.Source.Name) ? "Unknown" : node.Span.Source.Name);
        var name = System.Net.WebUtility.HtmlEncode(node.Span.DisplayName ?? node.Span.OperationName);
        var durationText = durationMs >= 1 ? $" ({durationMs:F0}ms)" : "";

        var hue = Math.Abs(source.GetHashCode()) % 360;
        var color = $"hsl({hue}, 60%, {70 + Math.Min(depth * 5, 20)}%)";

        sb.Append($"<div class=\"iflow-flame-bar\" style=\"margin-left:{leftPct:F2}%;width:{widthPct:F2}%;background:{color}\" ");
        sb.Append($"title=\"[{source}] {name}{durationText}\">");
        sb.Append($"<span class=\"iflow-flame-label\">{name}{durationText}</span>");
        sb.AppendLine("</div>");

        foreach (var child in node.Children)
            RenderFlameNode(sb, child, earliest, totalMs, depth + 1);
    }

    /// <summary>
    /// Renders a flame chart with vertical dashed boundary markers at HTTP request timestamps.
    /// </summary>
    public static string RenderFlameChartWithBoundaryMarkers(
        InternalFlowSegment segment,
        (string Label, DateTimeOffset Timestamp)[] boundaryLogs)
    {
        if (segment.Spans.Length == 0)
            return string.Empty;

        var roots = BuildSpanTree(segment.Spans);
        var earliest = segment.Spans.Min(s => s.StartTimeUtc);
        var latest = segment.Spans.Max(s => s.StartTimeUtc + s.Duration);
        var totalMs = (latest - earliest).TotalMilliseconds;
        if (totalMs <= 0) totalMs = 1;

        var sb = new StringBuilder();
        sb.AppendLine("<div class=\"iflow-flame\" data-diagram-type=\"flamechart\" style=\"position:relative\">");

        foreach (var (label, timestamp) in boundaryLogs)
        {
            var offsetMs = (timestamp.UtcDateTime - earliest).TotalMilliseconds;
            if (offsetMs >= 0 && offsetMs <= totalMs)
            {
                var leftPct = (offsetMs / totalMs) * 100;
                var encodedLabel = System.Net.WebUtility.HtmlEncode(label);
                sb.Append($"<div class=\"iflow-boundary-marker\" style=\"left:{leftPct:F2}%\" ");
                sb.AppendLine($"title=\"{encodedLabel}\"></div>");
            }
        }

        foreach (var root in roots)
            RenderFlameNode(sb, root, earliest, totalMs, 0);

        sb.AppendLine("</div>");
        return sb.ToString();
    }

    /// <summary>
    /// Renders a PlantUML Gantt chart from the spans in a segment.
    /// </summary>
    [Obsolete("Gantt rendering is no longer used by the component diagram report. Use the flame chart approach instead.")]
    public static string RenderGantt(InternalFlowSegment segment)
    {
        if (segment.Spans.Length == 0)
            return string.Empty;

        var roots = BuildSpanTree(segment.Spans);
        var earliest = segment.Spans.Min(s => s.StartTimeUtc);

        var sb = new StringBuilder();
        sb.AppendLine("@startgantt");
        sb.AppendLine("printscale daily");
        sb.AppendLine("hide footbox");

        var taskIndex = 0;
        foreach (var root in roots)
            RenderGanttNode(sb, root, earliest, ref taskIndex);

        sb.AppendLine("@endgantt");
        return sb.ToString();
    }

    private static void RenderGanttNode(StringBuilder sb, SpanNode node, DateTime earliest, ref int taskIndex)
    {
        var name = EscapeGantt(node.Span.DisplayName ?? node.Span.OperationName);
        var durationMs = node.Span.Duration.TotalMilliseconds;
        var offsetMs = (node.Span.StartTimeUtc - earliest).TotalMilliseconds;
        var durationDays = Math.Max(durationMs / 86400000.0, 0.001);

        sb.AppendLine($"[{name} ({durationMs:F0}ms)] lasts {Math.Max(1, (int)Math.Ceiling(durationMs / 1000.0))} days");

        taskIndex++;

        foreach (var child in node.Children)
            RenderGanttNode(sb, child, earliest, ref taskIndex);
    }

    private static string EscapeGantt(string text)
    {
        return PlantUml.PlantUmlCreator.EscapeLoaderMarkup(text.Replace("[", "(").Replace("]", ")"));
    }

    /// <summary>
    /// Renders a sequential test flame chart where each test occupies its own
    /// horizontal band with a label divider.
    /// </summary>
    public static string RenderSequentialTestFlameChart(
        Dictionary<string, InternalFlowSegment> wholeTestSegments)
    {
        if (wholeTestSegments.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("<div class=\"iflow-flame iflow-sequential-tests\" data-diagram-type=\"flamechart\">");

        foreach (var (key, segment) in wholeTestSegments.OrderBy(kv => kv.Value.StartTime))
        {
            if (segment.Spans.Length == 0) continue;

            var testId = System.Net.WebUtility.HtmlEncode(segment.TestId);
            sb.AppendLine($"<div class=\"iflow-test-band\">");
            sb.AppendLine($"<div class=\"iflow-test-band-label\">{testId}</div>");

            var earliest = segment.Spans.Min(s => s.StartTimeUtc);
            var latest = segment.Spans.Max(s => s.StartTimeUtc + s.Duration);
            var totalMs = (latest - earliest).TotalMilliseconds;
            if (totalMs <= 0) totalMs = 1;

            var roots = BuildSpanTree(segment.Spans);
            foreach (var root in roots)
                RenderFlameNode(sb, root, earliest, totalMs, 0);

            sb.AppendLine("</div>");
        }

        sb.AppendLine("</div>");
        return sb.ToString();
    }

    /// <summary>Compact flame chart data for client-side rendering.</summary>
    /// <param name="Sources">Deduplicated source/component names.</param>
    /// <param name="Spans">Each element: [srcIdx, name, leftPct, widthPct, depth, durationMs].</param>
    /// <param name="Markers">Optional boundary markers: [leftPct, label].</param>
    public record FlameChartData(string[] Sources, object[][] Spans, object[][]? Markers = null)
    {
        public static readonly FlameChartData Empty = new([], []);
    }

    /// <summary>Compact sequential flame chart data for client-side rendering.</summary>
    /// <param name="Sources">Global deduplicated source/component names.</param>
    /// <param name="Bands">Each element: { id, f: [[srcIdx, name, leftPct, widthPct, depth, durationMs], ...] }.</param>
    public record SequentialFlameChartData(string[] Sources, object[] Bands)
    {
        public static readonly SequentialFlameChartData Empty = new([], []);
    }

    internal record SpanNode(Activity Span)
    {
        public List<SpanNode> Children { get; } = [];
    }
}
