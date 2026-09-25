using Kronikol.PlantUml;

namespace Kronikol.Tests.PlantUml;

/// <summary>
/// NOTE_COPY_FIDELITY_PLAN M1. A break Kronikol writes into a note body is a display decision, and
/// every path that hands note text back to a reader — Copy box text, Open box text in new tab, Copy
/// all caller request payloads — reads those source lines. Without a marker they are
/// indistinguishable from a newline the payload really had, so a token cut mid-way comes back
/// corrupted. These facts pin the marker onto every site that inserts one.
/// </summary>
public class NoteCopyFidelityTests
{
    private const string Join = DiagramWidth.JoinMarker;
    private const string JoinSpace = DiagramWidth.JoinSpaceMarker;

    private const string Jwt = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9."
        + "eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkFkYSBMb3ZlbGFjZSIsImlhdCI6MTUxNjIzOTAyMiwicm9sZXMiOlsiYWRtaW4iXX0"
        + ".SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";

    // ── The marker itself ──────────────────────────────────────────────────

    [Fact]
    public void The_join_markers_are_the_escape_form_so_a_payloads_own_text_can_never_be_mistaken_for_one()
    {
        // EscapeCreoleMarkup escapes '<', so a payload literally containing "<U+200B>" reaches the
        // source as "~<U+200B>". An UNESCAPED marker is therefore provably Kronikol's own — the same
        // argument reconstructNoteJson already makes about focus-emphasis tags. A literal U+200B
        // character would be indistinguishable from one in the captured bytes.
        Assert.Equal("<U+200B>", Join);
        Assert.Equal("<U+200B><U+200B>", JoinSpace);
        Assert.StartsWith(Join, JoinSpace);
        Assert.Contains("~<U+200B>", PlantUmlCreator.EscapeCreoleMarkup("<U+200B>"));
    }

    // ── Site 1 & 2: WrapUnbreakableRuns (payload notes, user-action notes) ──

    [Fact]
    public void A_token_cut_mid_way_carries_the_no_space_marker()
    {
        var wrapped = PlantUmlCreator.WrapUnbreakableRuns(Jwt);
        var lines = wrapped.Split('\n');

        Assert.True(lines.Length > 1, "the fixture must actually wrap");
        Assert.All(lines[..^1], l => Assert.EndsWith(Join, l));
        Assert.DoesNotContain(JoinSpace, wrapped);
        Assert.Equal(Jwt, DiagramWidth.RejoinMarkedLines(wrapped));
    }

    [Fact]
    public void A_payload_that_never_wraps_is_byte_identical_to_before()
    {
        // What keeps every ordinary report unchanged: no wrap, no marker, same reference.
        const string sql = "SELECT o.id, o.placed_at FROM orders AS o WHERE o.status = 'PENDING'";
        Assert.Same(sql, PlantUmlCreator.WrapUnbreakableRuns(sql));
    }

    [Fact]
    public void A_real_newline_in_the_payload_is_left_unmarked_and_survives_the_rejoin()
    {
        var json = "{\n  \"token\": \"" + Jwt + "\",\n  \"expiresIn\": 3600\n}";
        var wrapped = PlantUmlCreator.WrapUnbreakableRuns(json);

        Assert.NotEqual(json, wrapped);
        Assert.Equal(json, DiagramWidth.RejoinMarkedLines(wrapped));
    }

    // ── Site 6: WrapBlockNoteBody (assertion notes) ─────────────────────────

    [Fact]
    public void An_assertion_note_broken_between_words_carries_the_space_marker()
    {
        var message = "Expected the audit header " + string.Join(" ", Enumerable.Range(0, 24)
            .Select(i => $"field{i:00}")) + " to be present";

        var wrapped = DiagramWidth.WrapBlockNoteBody(message);

        Assert.True(wrapped.Contains('\n'), "the fixture must actually wrap");
        Assert.Contains(JoinSpace, wrapped);
        Assert.Equal(message, DiagramWidth.RejoinMarkedLines(wrapped));
    }

    [Fact]
    public void An_assertion_note_that_hard_cuts_a_token_carries_the_no_space_marker()
    {
        var wrapped = DiagramWidth.WrapBlockNoteBody("Expected: " + Jwt);

        Assert.Contains(Join, wrapped);
        Assert.Equal("Expected: " + Jwt, DiagramWidth.RejoinMarkedLines(wrapped));
    }

    [Fact]
    public void A_long_padded_line_keeps_its_runs_of_spaces()
    {
        // The monospace note control exists so padded columns line up. Atoms used to split with
        // RemoveEmptyEntries, which collapsed every run of spaces to one BEFORE any break was
        // inserted — a loss no rejoin can undo, and one that silently defeated that control.
        var padded = "id      name                 status\n"
            + "1       " + new string('x', 60) + "       ACTIVE   " + new string('y', 60);

        var wrapped = DiagramWidth.WrapBlockNoteBody(padded);

        Assert.Equal(padded, DiagramWidth.RejoinMarkedLines(wrapped));
    }

    [Fact]
    public void Multi_line_assertion_messages_keep_their_own_line_breaks()
    {
        const string body = "✗ Expected the response to contain the audit header\nActual:   <missing>";
        var wrapped = DiagramWidth.WrapBlockNoteBody(body);

        Assert.Equal(body, wrapped);
        Assert.Equal(body, DiagramWidth.RejoinMarkedLines(wrapped));
    }

    // ── Sites 3, 4, 5: the 80-character chunkers ───────────────────────────

    [Fact]
    public void A_chunked_full_path_rejoins_to_the_original_url()
    {
        var url = "/api/v2/tenants/7f3a9c21-4b8e-4d55-9a10-c6e2f8b13d47/orders?" + string.Join("&",
            Enumerable.Range(0, 12).Select(i => $"include{i:00}=lineItems.product.category"));

        var note = PlantUmlCreator.AppendFullPathToNote("", url);

        Assert.Contains(Join, note);
        var payload = DiagramWidth.RejoinMarkedLines(note).Split('\n')[^1];
        Assert.Equal(url, payload);
    }

    [Fact]
    public void A_chunked_form_field_rejoins_but_the_field_divider_does_not()
    {
        // The `&` divider is a deliberate, visible decomposition of the captured body into fields;
        // the 80-character chunking inside one value is the arbitrary break that corrupts. Only the
        // second is marked.
        var value = new string('v', 200);
        var formatted = PlantUmlCreator.FormatFormUrlEncodedContent($"alpha={value}&beta=2");

        Assert.Contains(Join, formatted);
        var rejoined = DiagramWidth.RejoinMarkedLines(formatted);
        Assert.Contains($"alpha={value}", rejoined);
        Assert.True(rejoined.Contains('\n'), "the field divider stays a real line break");
    }

    [Fact]
    public void A_chunked_header_value_rejoins_to_the_original_value()
    {
        var value = "Bearer " + Jwt;
        var lines = PlantUmlCreator.BatchGray(value).ToArray();

        Assert.True(lines.Length > 1, "the fixture must actually chunk");
        Assert.All(lines[..^1], l => Assert.EndsWith(Join, l));

        // A consumer strips the per-line header tag first, then rejoins - which is why the rejoin needs
        // no knowledge of it.
        Assert.All(lines, l => Assert.StartsWith(NotePalette.HeaderTag, l));
        var stripped = lines.Select(l => l[NotePalette.HeaderTag.Length..]);
        Assert.Equal(value, DiagramWidth.RejoinMarkedLines(string.Join("\n", stripped)));
    }

    // ── The rejoin itself ──────────────────────────────────────────────────

    [Theory]
    [InlineData("plain text", "plain text")]
    [InlineData("a\nb", "a\nb")]
    [InlineData("ab<U+200B>\ncd", "abcd")]
    [InlineData("ab<U+200B><U+200B>\ncd", "ab cd")]
    [InlineData("ab<U+200B>\ncd<U+200B>\nef", "abcdef")]
    [InlineData("a~<U+200B>\nb", "a~<U+200B>\nb")]
    // No marker, nothing to undo: the bytes are returned as they came, CRLF included.
    [InlineData("ab\r\ncd", "ab\r\ncd")]
    [InlineData("ab<U+200B>\r\ncd", "abcd")]
    [InlineData("ab<U+200B>\r\ncd\r\nef", "abcd\nef")]
    public void RejoinMarkedLines_undoes_exactly_the_marked_breaks(string marked, string expected)
    {
        Assert.Equal(expected, DiagramWidth.RejoinMarkedLines(marked));
    }
}
