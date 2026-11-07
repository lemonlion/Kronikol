using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Kronikol.PlantUml;
using Kronikol.Reports;
using Kronikol.Tests.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.PlantUml;

/// <summary>
/// A failed answer's reason drawn in the note on its return (plans/FAILED_CALLS_PLAN.md R3, §5.5): its first payload lines, in
/// an ink the page paints from the theme. The ink is a named colour, so the scripts' header test, which the page hides by
/// default, never reads a reason line as a header.
/// </summary>
public class FailedAnswerReasonTests
{
    private static readonly string Nl = Environment.NewLine;

    private static RequestResponseLog Request() => new(
        TestName: "My Test",
        TestId: "test-1",
        Method: HttpMethod.Get,
        Content: null,
        Uri: new Uri("http://example.com/api/orders"),
        Headers: [],
        ServiceName: "OrderService",
        CallerName: "WebApp",
        Type: RequestResponseType.Request,
        TraceId: Guid.NewGuid(),
        RequestResponseId: Guid.NewGuid(),
        TrackingIgnore: false);

    private static RequestResponseLog Answer(
        string? error, string? content = null, (string Key, string? Value)[]? headers = null, HttpStatusCode status = HttpStatusCode.OK) =>
        new(
            TestName: "My Test",
            TestId: "test-1",
            Method: HttpMethod.Get,
            Content: content,
            Uri: new Uri("http://example.com/api/orders"),
            Headers: headers ?? [],
            ServiceName: "OrderService",
            CallerName: "WebApp",
            Type: RequestResponseType.Response,
            TraceId: Guid.NewGuid(),
            RequestResponseId: Guid.NewGuid(),
            TrackingIgnore: false,
            StatusCode: status)
        {
            Error = error,
        };

    // As a tracker answers a call that threw (OpenCall): the exception's type as the status, the message chain as the reason.
    private static RequestResponseLog Threw(string error, string? content = null, (string Key, string? Value)[]? headers = null) =>
        Answer(error, content, headers) with { StatusCode = "!HttpRequestException" };

    private static string Source(RequestResponseLog answer) =>
        PlantUmlCreator.GetPlantUmlImageTagsPerTestId([Request(), answer]).Single().PlantUmls.First().PlainText;

    // The lines of the note on the return arrow.
    private static string[] ReturnNote(string source)
    {
        var lines = source.ReplaceLineEndings("\n").Split('\n');
        var arrow = Array.FindIndex(lines, l => l.StartsWith("orderService -", StringComparison.Ordinal) && l.Contains("-> webApp", StringComparison.Ordinal));
        Assert.True(arrow >= 0, "no return arrow");
        Assert.Equal("note right", lines[arrow + 1]);
        var end = Array.IndexOf(lines, "end note", arrow + 2);
        return lines[(arrow + 2)..end];
    }

    [Fact]
    public void A_call_that_threw_has_its_reason_drawn_as_the_note_on_its_return()
    {
        // A call that threw has no body (OpenCall writes none, and LogResponseContent = false keeps any out): the reason is the
        // whole note. It is not a body, so nothing that keeps bodies out of notes hides it.
        var source = Source(Threw("No connection could be made because the target machine actively refused it. (localhost:5001)"));

        Assert.Contains(
            $"orderService -[#438DD5]-> webApp: !HttpRequestException{Nl}note right{Nl}" +
            $"<color:firebrick>No connection could be made because the target machine actively refused it. (localhost:5001)</color>{Nl}" +
            "end note",
            source);
    }

    [Fact]
    public void A_success_has_no_reason()
    {
        var source = Source(Answer(error: null));

        Assert.DoesNotContain("note right", source);
        Assert.DoesNotContain(NotePalette.ReasonTag, source);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void A_blank_reason_draws_nothing(string error)
    {
        Assert.DoesNotContain(NotePalette.ReasonTag, Source(Threw(error)));
    }

    [Fact]
    public void A_failed_status_without_a_reason_draws_its_body_only()
    {
        var source = Source(Answer(error: null, content: "boom", status: HttpStatusCode.InternalServerError));

        Assert.Equal(["boom"], ReturnNote(source));
    }

    [Fact]
    public void The_reason_comes_after_the_headers_and_before_the_body()
    {
        var note = ReturnNote(Source(Threw("Deadline exceeded", content: "late", headers: [("grpc-status", "4")])));

        Assert.Equal([NotePalette.HeaderTag + "[grpc-status=4]", "", "<color:firebrick>Deadline exceeded</color>", "", "late"], note);
    }

    [Fact]
    public void A_reason_is_escaped_as_a_payload_is_so_it_cannot_close_its_ink()
    {
        const string reason = "**not bold** </color> <b>x</b> //not italic//";
        var escaped = PlantUmlCreator.EscapeCreoleMarkup(reason);
        Assert.NotEqual(reason, escaped);

        Assert.Equal(["<color:firebrick>" + escaped + "</color>"], ReturnNote(Source(Threw(reason))));
    }

    [Fact]
    public void Each_line_of_a_reason_is_drawn_in_its_ink()
    {
        Assert.Equal(
            ["<color:firebrick>Line one</color>", "<color:firebrick>Line two</color>"],
            ReturnNote(Source(Threw("Line one\r\nLine two\n"))));
    }

    [Fact]
    public void A_long_unbroken_run_in_a_reason_is_broken_with_its_ink_on_every_piece_and_reads_back_whole()
    {
        var reason = "Unreachable https://example.com/" + new string('a', 300);

        var note = ReturnNote(Source(Threw(reason)));

        Assert.True(note.Length > 2, string.Join("\n", note));
        Assert.All(note, line =>
        {
            Assert.StartsWith("<color:firebrick>", line, StringComparison.Ordinal);
            Assert.EndsWith("</color>", line, StringComparison.Ordinal);
        });
        Assert.Equal(reason, NoteSourceText.Decode(string.Join("\n", note)));
    }

    [Fact]
    public void A_body_that_is_the_reason_is_drawn_once_as_the_reason()
    {
        // The OTLP tap answers a failed span with its status message as both the body and the reason.
        Assert.Equal(["<color:firebrick>deadline exceeded</color>"], ReturnNote(Source(Threw("deadline exceeded", content: " deadline exceeded "))));
    }

    [Fact]
    public void A_body_that_is_the_first_message_of_the_reasons_chain_is_drawn_once_in_the_reason()
    {
        // The SQL family answers a failure with the exception's message as the body and its whole chain as the reason.
        var exception = new InvalidOperationException("relation \"orders\" does not exist", new IOException("the socket closed"));
        var source = Source(Threw(FailedSend.Describe(exception), content: exception.Message));

        Assert.Equal(["<color:firebrick>" + PlantUmlCreator.EscapeCreoleMarkup(FailedSend.Describe(exception)) + "</color>"], ReturnNote(source));
    }

    [Fact]
    public void A_body_that_only_starts_like_the_reason_is_still_drawn()
    {
        Assert.Equal(["<color:firebrick>timeout after 30 s</color>", "", "timeout"], ReturnNote(Source(Threw("timeout after 30 s", content: "timeout"))));
    }

    [Fact]
    public void A_reason_line_is_no_header_to_the_page()
    {
        // DiagramContextMenuTests pins this literal in both scripts. The page hides header lines unless Headers is on.
        var headerTag = new Regex("^<color:(?:gray|#[0-9A-Fa-f]{6})>");
        Assert.Contains("var NOTE_HEADER_TAG = /^<color:(?:gray|#[0-9A-Fa-f]{6})>/;", DiagramContextMenu.GetCollapsibleNotesScript());

        Assert.DoesNotMatch(headerTag, NotePalette.ReasonTag + "Deadline exceeded</color>");
        Assert.Matches(headerTag, NotePalette.HeaderTag);
    }

    [Fact]
    public void The_query_reads_a_reason_without_its_ink()
    {
        Assert.Equal("Deadline exceeded", NoteSourceText.Decode("<color:firebrick>Deadline exceeded</color>"));
    }

    [Fact]
    public void The_query_reads_a_focused_line_without_its_emphasis()
    {
        // A FocusFields payload's emphasis is Kronikol's markup (JsonFocusFormatter writes it inside a line's indent), which
        // the page's copies drop since 5.4.0.
        Assert.Equal("  \"id\": 1,\n  \"name\": \"x\"",
            NoteSourceText.Decode("  <b>\"id\": 1,</b>\n  <color:lightgray>\"name\": \"x\"</color>"));
    }

    [Fact]
    public void The_query_reads_a_key_line_and_a_form_divider_as_the_page_copies_them()
    {
        // A message's key line reads as its word, and a form body's divider as its '&' (context-menu-script.js).
        Assert.Equal("Key order-1\na=1&\nb=2", NoteSourceText.Decode("<b>Key</b> order-1\na=1<font color=\"lightgray\">&\nb=2"));
    }

    [Fact]
    public void The_query_keeps_a_pair_that_does_not_wrap_its_whole_line()
    {
        Assert.Equal("<b>x</b> y", NoteSourceText.Decode("<b>x</b> y"));
    }

    [Fact]
    public void A_cut_run_under_emphasis_joins_with_one_pair_around_it()
    {
        // As the page's copies lift emphasis over a join (liftEmphasisOverJoins); the search's normaliser runs the same rule.
        Assert.Equal("<color:firebrick>aaabbb</color>",
            DiagramWidth.RejoinMarkedLines("<color:firebrick>aaa" + DiagramWidth.JoinMarker + "</color>\n<color:firebrick>bbb</color>"));
        Assert.Equal("<b>aaa" + DiagramWidth.JoinMarker + "</b>\n<i>bbb</i>",
            DiagramWidth.RejoinMarkedLines("<b>aaa" + DiagramWidth.JoinMarker + "</b>\n<i>bbb</i>"));
    }

    [Fact]
    public void The_query_joins_a_cut_run_whose_one_pair_spans_its_pieces()
    {
        // With CRLF line ends FocusFields puts one pair around the pieces of a cut run (collapsible-notes-script.js).
        Assert.Equal("aaabbb", NoteSourceText.Decode("<b>aaa" + DiagramWidth.JoinMarker + "\r\nbbb</b>"));
    }

    // ─── The ink, unthemed and themed ───────────────────────────

    [Fact]
    public void The_reason_ink_clears_aa_on_the_engines_note_fills()
    {
        // What a picture drawn away from the page shows (a server-drawn report, an exported diagram): the engine's firebrick.
        Assert.Equal("firebrick", NotePalette.ReasonInk);
        Assert.Equal("<color:firebrick>", NotePalette.ReasonTag);
        Assert.Equal("#B22222", NotePalette.ReasonInkDrawn);
        Assert.True(WcagContrast.Ratio(NotePalette.ReasonInkDrawn, NotePalette.DefaultNoteFill) >= NotePalette.HeaderContrastFloor);
        Assert.True(WcagContrast.Ratio(NotePalette.ReasonInkDrawn, NotePalette.EventNoteFill) >= NotePalette.HeaderContrastFloor);
    }

    [Theory]
    [InlineData(":root", null)]
    [InlineData(":root[data-theme=\"dark\"]", null)]
    [InlineData(":root:not([data-theme])", "(prefers-color-scheme: dark)")]
    public void The_reason_ink_reads_on_both_note_fills_in_each_theme(string block, string? media)
    {
        var css = Stylesheets.ReportTokensStyleSheet;
        var ink = CssRules.Value(css, block, "--dg-fail-ink", media);
        Assert.NotNull(ink);

        foreach (var fill in new[] { "--dg-note", "--dg-event" })
        {
            var ground = CssRules.Value(css, block, fill, media)!;
            var ratio = WcagContrast.Ratio(ink!, ground);
            Assert.True(ratio >= 4.5, $"{block}: {ink} on {fill} {ground} is {ratio.ToString("0.00", CultureInfo.InvariantCulture)}:1");
        }
    }

    [Fact]
    public void The_reason_ink_is_the_reports_own_failure_red_in_each_theme()
    {
        var css = Stylesheets.ReportTokensStyleSheet;
        Assert.Equal(CssRules.Value(css, ":root", "--kron-fail"), CssRules.Value(css, ":root", "--dg-fail-ink"));
        Assert.Equal(CssRules.Value(css, ":root[data-theme=\"dark\"]", "--kron-fail"), CssRules.Value(css, ":root[data-theme=\"dark\"]", "--dg-fail-ink"));
    }

    [Fact]
    public void The_page_paints_the_engines_firebrick_from_the_theme()
    {
        Assert.Contains(
            ".plantuml-browser:where(:not(.iflow-diagram)) svg text[fill=\"#B22222\" i] { fill: var(--dg-fail-ink, #B22222); }",
            DiagramContextMenu.GetInlineSvgStyles());
    }
}
