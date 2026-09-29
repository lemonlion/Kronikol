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
/// follows the name. A map written before, with every flow inline, renders as it did.
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

        Assert.Contains("SELECT orders", map[Key(outer)].GetProperty("content").GetString());
        Assert.True(map[Key(outer)].TryGetProperty("flameData", out _));
        Assert.Equal(Key(outer), map[Key(inner)].GetProperty("sameAs").GetString());
        Assert.False(map[Key(inner)].TryGetProperty("content", out _), "the later segment names the copy, it holds none");
        Assert.Equal("Internal Flow (2 spans)", map[Key(inner)].GetProperty("title").GetString());
    }

    [Fact]
    public void The_map_keeps_its_keys_and_their_order()
    {
        // No key is added: a merge meets each call's key once, so every copy a segment names survives it. And the copy
        // stays where its flow is first shown, beside the flows like it that gzip compresses it against.
        var spans = new[] { Span("SELECT orders", 60) };
        Guid[] ids = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        var segments = ids.ToDictionary(Key, id => Segment(id, spans));

        var stored = InternalFlowHtmlGenerator.StoreFlowsOnce(BuildSegmentData(segments));

        Assert.Equal(segments.Keys, stored.Keys);
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
        Assert.Contains("SELECT lines", map[Key(y[0])].GetProperty("content").GetString());
    }

    [Fact]
    public void A_flow_one_segment_shows_stays_as_it_was()
    {
        var id = Guid.NewGuid();
        var data = BuildSegmentData(new() { [Key(id)] = Segment(id, Span("SELECT orders", 60)) });

        var stored = InternalFlowHtmlGenerator.StoreFlowsOnce(data);

        Assert.Equal(JsonSerializer.Serialize(data), JsonSerializer.Serialize(stored));
    }

    [Fact]
    public void A_message_stays_with_its_segment()
    {
        Guid[] ids = [Guid.NewGuid(), Guid.NewGuid()];

        var map = Stored(ids.ToDictionary(Key, id => Segment(id) with { SpansLeftOut = 2 }));

        Assert.All(ids, id => Assert.Contains("2 spans that started during this call are left out", map[Key(id)].GetProperty("message").GetString()));
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

    private Activity Span(string name, int startMs)
    {
        Activity.Current = null;
        var span = _source.StartActivity(name, ActivityKind.Internal,
            new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded))!;
        span.SetStartTime(T0.UtcDateTime.AddMilliseconds(startMs));
        span.SetEndTime(T0.UtcDateTime.AddMilliseconds(startMs + 5));
        _activities.Add(span);
        return span;
    }
}
