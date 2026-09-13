using Kronikol.Reports;
using Kronikol.Tool;

namespace Kronikol.Tests.Tool;

/// <summary>
/// The header <c>kronikol query</c> writes before an answer, when the answer has to be read differently
/// from how it looks.
///
/// <para>One of the thirteen <see cref="DiagnosticKind"/>s reached it. The other twelve were recorded in
/// the report, carried through the merge, printed in <c>summary</c>'s inventory — and never warned anyone
/// reading any other answer. Two of them are what the header's own oldest line claims to be about: a
/// capture that lost data, and a step marker that did not line up. A degraded capture is the sharpest,
/// because <c>services</c> is the one view whose whole purpose is to answer a negative question — a
/// service absent from the table was never called — and a lost record makes that answer false with
/// nothing on screen to say so.</para>
///
/// <para>The line is deliberately not drawn at "every diagnostic". Capitalisation diagnostics are about
/// how the suite writes English: they change no answer, they fire in bulk on a healthy run, and a header
/// that pushes the answer off the byte budget is a worse failure than the one it warns about.</para>
/// </summary>
public class ProvenanceBannerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-banner-" + Guid.NewGuid().ToString("N"));

    public ProvenanceBannerTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    [Theory]
    [InlineData(nameof(DiagnosticKind.CaptureDegraded), "the tap gave up decoding after 3 resets")]
    [InlineData(nameof(DiagnosticKind.StepAttributionMismatch), "4 interaction(s) carry no stepPath")]
    [InlineData(nameof(DiagnosticKind.UnattributedInteractions), "7 interaction(s) belong to no scenario")]
    [InlineData(nameof(DiagnosticKind.DroppedUnattributed), "7 unattributed interaction(s) were discarded")]
    [InlineData(nameof(DiagnosticKind.DroppedOutsideRunWindow), "2 interaction(s) happened outside the run window")]
    [InlineData(nameof(DiagnosticKind.MalformedLine), "capture.ndjson line 88 could not be parsed")]
    [InlineData(nameof(DiagnosticKind.AttachmentFailure), "screenshot.png could not be copied")]
    [InlineData(nameof(DiagnosticKind.RenderFailure), "the sequence diagram for s0 is a placeholder")]
    [InlineData(nameof(DiagnosticKind.OutputFailure), "Specifications.html was not written")]
    [InlineData(nameof(DiagnosticKind.Other), "the shards disagree about the run environment")]
    [InlineData(nameof(DiagnosticKind.ResultDefaulted), "1 scenario(s) recorded no result")]
    public void A_diagnostic_that_makes_an_answer_a_lower_bound_is_said_before_the_answer(string kind, string message)
    {
        var output = Run("services", ReportWith((kind, message)));

        Assert.Contains("! " + message, output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The two kinds that are about the suite's prose rather than about its data. They are also the two
    /// that fire in bulk — one entry per offending step — so promoting them would be the change that
    /// makes the header useless.
    /// </summary>
    [Theory]
    [InlineData(nameof(DiagnosticKind.StepsNotStartingWithCapital))]
    [InlineData(nameof(DiagnosticKind.TitlesNotStartingWithCapital))]
    public void A_diagnostic_about_wording_is_not_a_header(string kind)
    {
        var report = ReportWith((kind, "3 step(s) do not start with a capital letter"));

        Assert.DoesNotContain("!", Run("services", report), StringComparison.Ordinal);

        // It is not dropped, only demoted: `summary` still carries the whole inventory.
        Assert.Contains(kind, Run("summary", report), StringComparison.Ordinal);
    }

    /// <summary>
    /// A malformed-line diagnostic is recorded once per line, so a capture file that went wrong early can
    /// produce thousands. Printed one per entry they are not a header, they are the answer.
    /// </summary>
    [Fact]
    public void Many_entries_of_one_kind_are_one_line_with_a_count()
    {
        var output = Run("services", ReportWith(
            (nameof(DiagnosticKind.MalformedLine), "capture.ndjson line 88 could not be parsed"),
            (nameof(DiagnosticKind.MalformedLine), "capture.ndjson line 89 could not be parsed"),
            (nameof(DiagnosticKind.MalformedLine), "capture.ndjson line 90 could not be parsed")));

        Assert.Equal(1, output.Split('\n').Count(l => l.StartsWith("! ", StringComparison.Ordinal)));
        Assert.Contains("×3", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The message half of the header is the tool's voice wrapped around text the report supplied, and a
    /// host hands its own strings in through <c>HostDiagnostics</c>. An uncapped one pushes the answer off
    /// the byte budget, which is the failure the header exists to prevent.
    /// </summary>
    [Fact]
    public void A_long_message_is_capped_to_one_line()
    {
        var output = Run("services", ReportWith(
            (nameof(DiagnosticKind.Other), new string('x', 400) + "\nand a second line")));

        var banner = output.Split('\n').Single(l => l.StartsWith("! ", StringComparison.Ordinal));
        Assert.True(banner.Length < 200, $"banner was {banner.Length} characters");
        Assert.DoesNotContain("and a second line", output, StringComparison.Ordinal);
    }

    private string Run(string command, string report)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run([command, report], output, error);
        Assert.True(exit == 0, $"exit {exit}: {error}");
        return output.ToString();
    }

    private string ReportWith(params (string Kind, string Message)[] diagnostics)
    {
        var entries = string.Join(",\n    ", diagnostics.Select(d =>
            $$"""{ "kind": {{System.Text.Json.JsonSerializer.Serialize(d.Kind)}}, "message": {{System.Text.Json.JsonSerializer.Serialize(d.Message)}} }"""));

        var path = Path.Combine(_dir, "TestRunReport.json");
        File.WriteAllText(path, $$"""
            {
              "kronikolVersion": "3.4.1",
              "formatVersion": 1,
              "startTime": "2026-01-01T10:00:00Z",
              "endTime": "2026-01-01T10:05:00Z",
              "features": [
                { "name": "Orders", "labels": [], "scenarios": [
                  { "id": "t0", "stableId": "aaaabbbbccccdddd", "name": "Checkout", "result": "Passed",
                    "durationSeconds": 1.0, "labels": [], "categories": [], "steps": [], "httpInteractions": [] }
                ] }
              ],
              "diagnostics": [
                {{entries}}
              ]
            }
            """);
        return path;
    }
}
