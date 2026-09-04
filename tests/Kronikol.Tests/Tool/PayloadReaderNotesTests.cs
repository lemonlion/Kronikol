using Kronikol.Tool.Query;

namespace Kronikol.Tests.Tool;

/// <summary>
/// <see cref="PayloadReader.Notes"/> splits a diagram into its note payloads — the escape hatch behind
/// <c>query note</c>. Two classification bugs hid here:
/// <list type="bullet">
/// <item>the one-line/block decision looked for <c>" : "</c> with spaces, but a step-delimiter bar's
/// colon is unspaced (<c>#black:&lt;color:white&gt;</c> — and the styled body form's <c>&gt;&gt;: </c>
/// likewise), so a bar flipped the reader into note mode and swallowed every line up to the next
/// <c>end note</c> — including real payload notes;</item>
/// <item>an event note opens with <c>note&lt;&lt;eventNote&gt;&gt; right</c> — no space after
/// <c>note</c> — which the <c>StartsWith("note ")</c> check missed entirely, so event payloads never
/// reached <c>query note</c> at all.</item>
/// </list>
/// </summary>
public class PayloadReaderNotesTests
{
    [Fact]
    public void A_step_bar_does_not_swallow_the_notes_that_follow_it()
    {
        var diagram = """
            @startuml
            participant "Api" as api
            hnote across <<stepDelimiter>> #black:<color:white>Given a basket
            api -> api: POST /charge
            note right
            {"amount": 100}
            end note
            @enduml
            """;

        var notes = PayloadReader.Notes(diagram);

        Assert.Single(notes);
        Assert.Equal("{\"amount\": 100}", notes[0].Text);
    }

    [Fact]
    public void The_styled_step_bar_is_skipped_the_same_way()
    {
        var diagram = """
            @startuml
            hnote across <<stepDelimiter>><<stepBody>>: Given muffins\n\n|= name |\n| Blueberry |\n
            note right
            {"ok": true}
            end note
            @enduml
            """;

        var notes = PayloadReader.Notes(diagram);

        Assert.Single(notes);
        Assert.Equal("{\"ok\": true}", notes[0].Text);
    }

    [Fact]
    public void An_event_note_payload_is_extracted()
    {
        var diagram = """
            @startuml
            note<<eventNote>> right
            {"event": "charge.requested"}
            end note
            @enduml
            """;

        var notes = PayloadReader.Notes(diagram);

        Assert.Single(notes);
        Assert.Equal("{\"event\": \"charge.requested\"}", notes[0].Text);
    }

    [Fact]
    public void One_line_notes_with_a_spaced_colon_still_extract_their_text()
    {
        var diagram = """
            @startuml
            hnote across #lightyellow : Row 3
            note over api : inline text
            @enduml
            """;

        var notes = PayloadReader.Notes(diagram);

        Assert.Equal(2, notes.Count);
        Assert.Equal("Row 3", notes[0].Text);
        Assert.Equal("inline text", notes[1].Text);
    }

    [Fact]
    public void Assertion_note_blocks_still_read_as_blocks()
    {
        var diagram = """
            @startuml
            hnote across <<assertionNote>> #00AA00
            ✓ The response is valid
            end note
            @enduml
            """;

        var notes = PayloadReader.Notes(diagram);

        Assert.Single(notes);
        Assert.Equal("✓ The response is valid", notes[0].Text);
    }
}
