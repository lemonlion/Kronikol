using System.Diagnostics;
using System.Text.Json;
using Kronikol.InternalFlow;
using Kronikol.Tracking;

namespace Kronikol.Tests.InternalFlow;

/// <summary>
/// #86 (<c>plans/V4_PLAN.md</c> R5): a report's segment map stores each distinct flow once. Calls that show the same
/// spans (a call and the calls nested in it) each carried a whole copy of the flow, which differed from the others
/// only in the call's id: up to 34 copies of one flow on BreakfastProvider's lanes, 702 in #86's report. The first
/// segment in the map that shows a flow keeps it, each later one keeps its title and names that segment, and the popup
/// follows the name. A map written before, with every flow inline, renders as it did. From 4.0.1 the segment that
/// holds a flow names its diagram and its flame chart by their places in one table at the end of the map, where each
/// distinct diagram and flame chart is stored once: the same calls starting at other moments draw one diagram and
/// flame charts of their own, and diagrams beside diagrams and flame charts beside flame charts compress further.
/// </summary>
public class FlowStoredOnceTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly ActivitySource _source = new("Kronikol.Tests.FlowStoredOnce");
    private readonly ActivityListener _listener;
    private readonly List<Activity> _activities = [];

    public FlowStoredOnceTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "Kronikol.Tests.FlowStoredOnce",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        foreach (var a in _activities) a.Dispose();
        _listener.Dispose();
        _source.Dispose();
    }

    [Fact]
    public void Segments_that_show_the_same_spans_keep_one_copy_the_others_name()
    {
        var spans = new[] { Span("SELECT orders", 60), Span("SELECT lines", 70) };
        var outer = Guid.NewGuid();
        var inner = Guid.NewGuid();

        var map = Stored(new() { [Key(outer)] = Segment(outer, spans), [Key(inner)] = Segment(inner, spans) });

        var shown = SegmentMapText.Resolve(map, Key(outer));
        Assert.Contains("SELECT orders", shown.Content);
        Assert.NotNull(shown.FlameData);
        Assert.Equal(Key(outer), map[Key(inner)].GetProperty("sameAs").GetString());
        Assert.False(map[Key(inner)].TryGetProperty("content", out _), "the later segment names the copy, it holds none");
        Assert.Equal("Internal Flow (2 spans)", map[Key(inner)].GetProperty("title").GetString());
    }

    [Fact]
    public void The_segments_keep_their_keys_and_order_and_one_table_follows_them()
    {
        // No segment key is added or moved: a merge meets each call's key once, so every copy a segment names survives
        // it. The table's key is the first segment's with a '~' before it, which no other shard's map holds, so a merge
        // keeps every shard's table too.
        var spans = new[] { Span("SELECT orders", 60) };
        Guid[] ids = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        var segments = ids.ToDictionary(Key, id => Segment(id, spans));

        var stored = InternalFlowHtmlGenerator.StoreFlowsOnce(BuildSegmentData(segments));

        Assert.Equal(segments.Keys.Append("~" + Key(ids[0])), stored.Keys);
    }

    [Fact]
    public void Different_flows_are_stored_apart()
    {
        var orders = new[] { Span("SELECT orders", 60) };
        var lines = new[] { Span("SELECT lines", 60) };
        Guid[] x = [Guid.NewGuid(), Guid.NewGuid()];
        Guid[] y = [Guid.NewGuid(), Guid.NewGuid()];

        var map = Stored(x.ToDictionary(Key, id => Segment(id, orders)).Concat(y.ToDictionary(Key, id => Segment(id, lines))).ToDictionary());

        Assert.Equal(Key(x[0]), map[Key(x[1])].GetProperty("sameAs").GetString());
        Assert.Equal(Key(y[0]), map[Key(y[1])].GetProperty("sameAs").GetString());
        Assert.Contains("SELECT orders", SegmentMapText.Resolve(map, Key(x[0])).Content);
        Assert.Contains("SELECT lines", SegmentMapText.Resolve(map, Key(y[0])).Content);
        Assert.NotEqual(map[Key(x[0])].GetProperty("contentAt").GetInt32(), map[Key(y[0])].GetProperty("contentAt").GetInt32());
    }

    [Fact]
    public void A_flow_one_segment_shows_comes_back_as_it_was_built()
    {
        // Until 4.0.1 such a segment kept its flow inline; now its diagram and flame chart sit in the table and it names
        // their places, and the popup's rule gives them back unchanged.
        var id = Guid.NewGuid();
        var data = BuildSegmentData(new() { [Key(id)] = Segment(id, Span("SELECT orders", 60)) });
        var built = Parse(data);

        var map = Parse(InternalFlowHtmlGenerator.StoreFlowsOnce(data));

        Assert.False(map[Key(id)].TryGetProperty("content", out _), "the diagram is in the table");
        Assert.Equal(SegmentMapText.Built(built[Key(id)]), SegmentMapText.Resolve(map, Key(id)));
    }

    [Fact]
    public void A_message_stays_with_its_segment_and_no_table_is_written_for_none_to_name()
    {
        Guid[] ids = [Guid.NewGuid(), Guid.NewGuid()];

        var map = Stored(ids.ToDictionary(Key, id => Segment(id) with { SpansLeftOut = 2 }));

        Assert.All(ids, id => Assert.Contains("2 spans that started during this call are left out", map[Key(id)].GetProperty("message").GetString()));
        Assert.DoesNotContain(map.Keys, key => key.StartsWith('~'));
    }

    [Fact]
    public void The_activity_diagram_names_its_element_by_the_flow_not_the_call()
    {
        // The call's id in the diagram element's id was the one difference between two copies of a flow (#86).
        var spans = new[] { Span("SELECT orders", 60) };
        var x = Guid.NewGuid();
        var y = Guid.NewGuid();

        var data = Parse(BuildSegmentData(new() { [Key(x)] = Segment(x, spans), [Key(y)] = Segment(y, spans) }));

        var content = data[Key(x)].GetProperty("content").GetString()!;
        Assert.Equal(content, data[Key(y)].GetProperty("content").GetString());
        Assert.DoesNotContain(x.ToString(), content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_diagram_that_calls_at_other_moments_show_is_stored_once_and_each_keeps_its_flame_chart()
    {
        // The same calls, one starting later: one activity diagram, which shows each span's duration in whole
        // milliseconds, and two flame charts, which place each span in time. Both segments hold their flows, at the
        // same place among the table's diagrams and at places of their own among its flame charts (4.0.1).
        Guid[] ids = [Guid.NewGuid(), Guid.NewGuid()];
        var segments = new Dictionary<string, InternalFlowSegment>
        {
            [Key(ids[0])] = Segment(ids[0], Span("SELECT orders", 60), Span("SELECT lines", 70)),
            [Key(ids[1])] = Segment(ids[1], Span("SELECT orders", 60), Span("SELECT lines", 90))
        };
        var built = Parse(BuildSegmentData(segments));
        Assert.Equal(built[Key(ids[0])].GetProperty("content").GetString(), built[Key(ids[1])].GetProperty("content").GetString());
        Assert.NotEqual(built[Key(ids[0])].GetProperty("flameData").GetRawText(), built[Key(ids[1])].GetProperty("flameData").GetRawText());

        var map = Parse(InternalFlowHtmlGenerator.StoreFlowsOnce(BuildSegmentData(segments)));

        var table = map["~" + Key(ids[0])];
        Assert.Equal(1, table.GetProperty("contents").GetArrayLength());
        Assert.Equal(2, table.GetProperty("flames").GetArrayLength());
        Assert.Equal(map[Key(ids[0])].GetProperty("contentAt").GetInt32(), map[Key(ids[1])].GetProperty("contentAt").GetInt32());
        Assert.NotEqual(map[Key(ids[0])].GetProperty("flameAt").GetInt32(), map[Key(ids[1])].GetProperty("flameAt").GetInt32());
        Assert.All(ids, id => Assert.Equal(SegmentMapText.Built(built[Key(id)]), SegmentMapText.Resolve(map, Key(id))));
    }

    [Fact]
    public void Following_the_names_gives_every_segment_back_as_it_was_built()
    {
        // A nested call repeats a flow whole (sameAs), the same calls at other moments repeat its diagram (one place in
        // the table's diagrams, flame charts of their own), and a call nested in one of those names its flow. The popup's
        // rule, followed from every key, gives each segment's title, diagram, flame chart and message back, and the table
        // holds each distinct diagram and flame chart once.
        Guid[] ids = [.. Enumerable.Range(0, 7).Select(_ => Guid.NewGuid())];
        Activity[] first = [Span("SELECT orders", 60), Span("SELECT lines", 70)];
        Activity[] later = [Span("SELECT orders", 60), Span("SELECT lines", 90)];
        var segments = new Dictionary<string, InternalFlowSegment>
        {
            [Key(ids[0])] = Segment(ids[0], first),
            [Key(ids[1])] = Segment(ids[1], first),
            [Key(ids[2])] = Segment(ids[2], later),
            [Key(ids[3])] = Segment(ids[3], later),
            [Key(ids[4])] = Segment(ids[4]) with { SpansLeftOut = 1 },
            [Key(ids[5])] = Segment(ids[5], Span("SELECT menu", 60)),
            [Key(ids[6])] = Segment(ids[6], [Span("SELECT orders", 60), Span("SELECT lines", 75, durationMs: 40)]) with { SpansLeftOut = 1 }
        };
        var built = Parse(BuildSegmentData(segments));

        var map = Parse(InternalFlowHtmlGenerator.StoreFlowsOnce(BuildSegmentData(segments)));

        Assert.Equal(Key(ids[0]), map[Key(ids[1])].GetProperty("sameAs").GetString());
        Assert.Equal(map[Key(ids[0])].GetProperty("contentAt").GetInt32(), map[Key(ids[2])].GetProperty("contentAt").GetInt32());
        Assert.Equal(Key(ids[2]), map[Key(ids[3])].GetProperty("sameAs").GetString());
        Assert.All(built.Keys, key => Assert.Equal(SegmentMapText.Built(built[key]), SegmentMapText.Resolve(map, key)));
        var table = map["~" + Key(ids[0])];
        var contents = built.Values.Select(v => v.TryGetProperty("content", out var c) ? c.GetString() : null).OfType<string>().Distinct().Count();
        var flames = built.Values.Select(v => v.TryGetProperty("flameData", out var f) ? f.GetRawText() : null).OfType<string>().Distinct().Count();
        Assert.Equal(contents, table.GetProperty("contents").GetArrayLength());
        Assert.Equal(flames, table.GetProperty("flames").GetArrayLength());
    }

    [Fact]
    public void Without_the_flame_chart_a_segment_names_only_its_diagram()
    {
        Guid[] ids = [Guid.NewGuid(), Guid.NewGuid()];
        var segments = new Dictionary<string, InternalFlowSegment>
        {
            [Key(ids[0])] = Segment(ids[0], Span("SELECT orders", 60)),
            [Key(ids[1])] = Segment(ids[1], Span("SELECT lines", 60))
        };
        var data = InternalFlowHtmlGenerator.BuildSegmentData(segments, InternalFlowDiagramStyle.ActivityDiagram, showFlameChart: false);
        var built = Parse(data);

        var map = Parse(InternalFlowHtmlGenerator.StoreFlowsOnce(data));

        Assert.All(ids, id => Assert.False(map[Key(id)].TryGetProperty("flameAt", out _), "no flame chart to name"));
        Assert.Equal(0, map["~" + Key(ids[0])].GetProperty("flames").GetArrayLength());
        Assert.All(ids, id => Assert.Equal(SegmentMapText.Built(built[Key(id)]), SegmentMapText.Resolve(map, Key(id))));
    }

    // ─── Fixture ───────────────────────────────────────────────

    private static string Key(Guid id) => $"iflow-{id}";

    private static Dictionary<string, object> BuildSegmentData(Dictionary<string, InternalFlowSegment> segments) =>
        InternalFlowHtmlGenerator.BuildSegmentData(segments, InternalFlowDiagramStyle.ActivityDiagram, showFlameChart: true);

    private static Dictionary<string, JsonElement> Stored(Dictionary<string, InternalFlowSegment> segments) =>
        Parse(InternalFlowHtmlGenerator.StoreFlowsOnce(BuildSegmentData(segments)));

    private static Dictionary<string, JsonElement> Parse(Dictionary<string, object> data) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(data))!;

    private static InternalFlowSegment Segment(Guid id, params Activity[] spans) =>
        new(id, RequestResponseType.Request, "test-a", T0, T0.AddMilliseconds(200), spans);

    private Activity Span(string name, int startMs, int durationMs = 5)
    {
        Activity.Current = null;
        var span = _source.StartActivity(name, ActivityKind.Internal,
            new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded))!;
        span.SetStartTime(T0.UtcDateTime.AddMilliseconds(startMs));
        span.SetEndTime(T0.UtcDateTime.AddMilliseconds(startMs + durationMs));
        _activities.Add(span);
        return span;
    }
}
