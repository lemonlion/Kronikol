using Kronikol.PlantUml;
using Kronikol.Tracking;

namespace Kronikol.Tests.PlantUml;

// plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.md 4.4, #167: a message's key travels as the entry's kafka-key header, and a
// note draws it above the body as a line of its own, not in the header block, so the page's header toggle (headers
// hidden by default since 4.0.0) leaves the key in view, where the Key: … content line had it.
public class MessageKeyLineTests
{
    private const string Json = "{\"Status\":\"Placed\"}";

    private static RequestResponseLog[] Produce(params (string Key, string? Value)[] headers) =>
    [
        new RequestResponseLog(
            TestName: "Keys", TestId: "keys-1", Method: "Produce → orders", Content: Json, Uri: new Uri("kafka:///orders"),
            Headers: headers, ServiceName: "Kafka", CallerName: "Orders", Type: RequestResponseType.Request,
            TraceId: Guid.Empty, RequestResponseId: Guid.Empty, TrackingIgnore: false, MetaType: RequestResponseMetaType.Event),
        new RequestResponseLog(
            TestName: "Keys", TestId: "keys-1", Method: "Produce → orders", Content: null, Uri: new Uri("kafka:///orders"),
            Headers: [], ServiceName: "Kafka", CallerName: "Orders", Type: RequestResponseType.Response,
            TraceId: Guid.Empty, RequestResponseId: Guid.Empty, TrackingIgnore: false, MetaType: RequestResponseMetaType.Event),
    ];

    // The first note's lines: the produce's.
    private static string[] FirstNote(RequestResponseLog[] logs, bool excludeAllHeaders = false)
    {
        var source = PlantUmlCreator.GetPlantUmlImageTagsPerTestId(logs, excludeAllHeaders: excludeAllHeaders).Single().PlantUmls.Single().PlainText;
        var lines = new List<string>();
        var inNote = false;
        foreach (var line in source.Split('\n').Select(l => l.TrimEnd('\r')))
        {
            var trimmed = line.Trim();
            if (!inNote && trimmed.StartsWith("note", StringComparison.Ordinal)) { inNote = true; continue; }
            if (inNote && trimmed == "end note") break;
            if (inNote) lines.Add(trimmed);
        }
        return lines.ToArray();
    }

    [Fact]
    public void The_key_is_drawn_first_on_a_line_of_its_own_and_the_other_headers_below_it_in_header_ink()
    {
        var note = FirstNote(Produce(("kafka-key", "order-1"), ("source", "checkout")));

        Assert.Equal("<b>Key</b> order-1", note[0]);
        Assert.Matches("^<color:#[0-9A-Fa-f]{6}>", note[1]);
        Assert.Contains("[source=checkout]", note[1]);
        Assert.Equal("", note[2]);
        Assert.Equal("{", note[3]);
        Assert.DoesNotContain(note, l => l.Contains("kafka-key", StringComparison.Ordinal));
    }

    [Fact]
    public void With_every_header_excluded_the_key_line_stays()
    {
        var note = FirstNote(Produce(("kafka-key", "order-1"), ("source", "checkout")), excludeAllHeaders: true);

        Assert.Equal("<b>Key</b> order-1", note[0]);
        Assert.Equal("", note[1]);
        Assert.Equal("{", note[2]);
        Assert.DoesNotContain(note, l => l.StartsWith("<color:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_key_holding_creole_markup_is_escaped_so_the_line_starts_with_Kronikols_label_alone()
    {
        var note = FirstNote(Produce(("kafka-key", "<b>Key</b> forged")));

        Assert.Equal("<b>Key</b> " + PlantUmlCreator.EscapeCreoleMarkup("<b>Key</b> forged"), note[0]);
        Assert.NotEqual("<b>Key</b> forged", PlantUmlCreator.EscapeCreoleMarkup("<b>Key</b> forged"));
    }

    // A second line would not start with the label, and the page would read it as the payload's first line.
    [Fact]
    public void A_key_holding_line_breaks_stays_on_one_line()
    {
        var note = FirstNote(Produce(("kafka-key", "order\n1\r\nretry")));

        Assert.Equal("<b>Key</b> order 1 retry", note[0]);
        Assert.Equal("", note[1]);
    }

    [Fact]
    public void A_long_key_is_cut_to_keep_the_note_drawable()
    {
        var note = FirstNote(Produce(("kafka-key", new string('k', 5_000))));

        Assert.StartsWith("<b>Key</b> kkk", note[0]);
        Assert.True(note[0].Length < 300, $"the key line is {note[0].Length} characters");
    }

    [Fact]
    public void A_note_without_a_key_draws_its_headers_first_as_before()
    {
        var note = FirstNote(Produce(("source", "checkout")));

        Assert.Matches("^<color:#[0-9A-Fa-f]{6}>", note[0]);
        Assert.DoesNotContain(note, l => l.StartsWith("<b>Key</b>", StringComparison.Ordinal));
    }
}
