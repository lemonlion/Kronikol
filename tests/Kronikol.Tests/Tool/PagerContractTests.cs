using System.Text.Json;
using Kronikol.Tool;

namespace Kronikol.Tests.Tool;

/// <summary>
/// A paged answer's contract with the caller that follows its <c>next:</c> pointer.
///
/// <para>Two ways the pointer led somewhere other than where the walk had been. <c>--limit 50</c> on
/// <c>failures</c> was silently clamped to 25 with nothing said, so a consumer that advanced by the 50 it
/// had asked for skipped rows 25 to 49 — the same silent skip the pager was built to remove, arriving
/// through the flag instead of through the footer. And <c>--max-bytes</c> was absent from the re-run
/// arguments, so a walk begun at one budget resumed at the default: the row count changes underneath a
/// reader who did nothing but follow the pointer they were given.</para>
///
/// <para>Both are the same failure as a row that is simply missing, except that nothing on screen says
/// so. An answer that is narrower than the question is only safe when it says it is.</para>
/// </summary>
public class PagerContractTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-pager-" + Guid.NewGuid().ToString("N"));

    public PagerContractTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    [Fact]
    public void A_limit_above_the_verbs_ceiling_says_it_was_lowered()
    {
        var output = Run("failures", Report(60), "--limit", "50", "--max-bytes", "200000");

        Assert.Contains("--limit 50", output, StringComparison.Ordinal);
        Assert.Contains("25", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The consequence, stated as a walk: advancing by the limit that was asked for must not step over
    /// rows the verb never showed. With the note in place the caller can see the page size changed; the
    /// footer's own offset has always been right, and this pins that the two agree.
    /// </summary>
    [Fact]
    public void The_footers_offset_is_the_page_that_was_actually_shown()
    {
        var output = Run("failures", Report(60), "--limit", "50", "--max-bytes", "200000");

        // The pointer carries the budget it began at, so the offset is the tail of the line rather than
        // the whole of it.
        Assert.Contains("failures: 1-25 of 60", output, StringComparison.Ordinal);
        Assert.EndsWith("--offset 25", output.TrimEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_limit_within_the_ceiling_says_nothing()
    {
        Assert.DoesNotContain("ceiling", Run("failures", Report(60), "--limit", "10", "--max-bytes", "200000"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// `next` is an argument vector meant to be re-run verbatim. Without the budget it began at, page two
    /// is counted against the 6 KB default and holds a different number of rows than page one did.
    /// </summary>
    [Fact]
    public void The_json_next_pointer_carries_the_budget_the_walk_began_at()
    {
        var envelope = JsonDocument.Parse(Run("scenarios", Report(60), "--json", "--max-bytes", "3000"));

        Assert.True(envelope.RootElement.TryGetProperty("next", out var next), "no next pointer on a truncated page");
        var argv = next.EnumerateArray().Select(a => a.GetString()).ToList();
        Assert.Contains("--max-bytes", argv);
        Assert.Contains("3000", argv);
    }

    [Fact]
    public void A_walk_at_the_default_budget_does_not_carry_it()
    {
        var envelope = JsonDocument.Parse(Run("scenarios", Report(400), "--json"));

        Assert.True(envelope.RootElement.TryGetProperty("next", out var next), "no next pointer on a truncated page");
        Assert.DoesNotContain("--max-bytes", next.EnumerateArray().Select(a => a.GetString()).ToList());
    }

    /// <summary>
    /// <c>--out ""</c> reached <c>File.WriteAllText</c>, whose <c>ArgumentException</c> is in no catch
    /// clause in the tool: an unhandled exception, a stack trace, and a CLR exit code outside the 0-255
    /// range a shell can read — from the flag whose entire purpose is to keep a large answer OUT of the
    /// caller's context. The path is the caller's input, so every way it can be invalid belongs to the
    /// same sentence.
    ///
    /// <para>The whitespace case is the one that has to be checked rather than caught, and it is the one
    /// that shipped broken: Windows rejects <c>"   "</c> with <c>ArgumentException</c> and POSIX accepts
    /// it as a file literally named three spaces, which the caller will never find again. Both rows here
    /// come from the same mistake — a variable that was empty when it was interpolated — and a tool
    /// documented once must not answer it two ways depending on where it runs.</para>
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_out_path_that_is_not_a_path_is_a_sentence_not_a_stack_trace(string path)
    {
        var (_, error, exit) = RunFull("scenarios", Report(3), "--out", path);

        Assert.Equal(1, exit);
        Assert.Contains("--out", error, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// Five writes of a caller-supplied path existed and two were guarded, both of them for
    /// <c>IOException</c> and <c>UnauthorizedAccessException</c> only. The payload verbs handle
    /// <c>--out</c> themselves, so each one was its own hole; this walks all four.
    /// </summary>
    [Theory]
    [InlineData("failures")]
    [InlineData("steps")]
    [InlineData("grep")]
    public void No_verb_answers_an_empty_out_path_with_a_stack_trace(string verb)
    {
        var report = Report(3);
        var args = verb switch
        {
            "steps" => new[] { "s0", "--out", "" },
            "grep" => ["Expected", "--out", ""],
            _ => ["--out", ""]
        };

        var (_, error, exit) = RunFull(verb, report, args);

        Assert.True(exit is 1 or 2, $"exit {exit}");
        Assert.DoesNotContain("   at ", error, StringComparison.Ordinal);
        Assert.NotEqual("", error.Trim());
    }

    private string Run(string command, string report, params string[] args)
    {
        var (output, error, exit) = RunFull(command, report, args);
        Assert.True(exit == 0, $"exit {exit}: {error}");
        return output;
    }

    private (string Output, string Error, int Exit) RunFull(string command, string report, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run([command, report, .. args], output, error);
        return (output.ToString(), error.ToString(), exit);
    }

    /// <summary>A run of <paramref name="count"/> failing scenarios — enough to page more than once.</summary>
    private string Report(int count)
    {
        var scenarios = string.Join(",\n            ", Enumerable.Range(0, count).Select(i =>
            $$"""
              { "id": "t{{i}}", "stableId": "{{i:x16}}", "name": "Scenario number {{i}}", "result": "Failed",
                "durationSeconds": 0.5, "errorMessage": "Expected {{i}} but found {{i + 1}}",
                "labels": [], "categories": [],
                "steps": [ { "keyword": "Then", "text": "the total is right {{i}}", "status": "Failed",
                             "failureMessage": "Expected {{i}} but found {{i + 1}}", "subSteps": [], "attachments": [] } ],
                "httpInteractions": [], "attachments": [] }
              """));

        var path = Path.Combine(_dir, $"Report{count}.json");
        File.WriteAllText(path, $$"""
            {
              "kronikolVersion": "3.4.1",
              "formatVersion": 1,
              "startTime": "2026-01-01T10:00:00Z",
              "endTime": "2026-01-01T10:05:00Z",
              "features": [ { "name": "Orders", "labels": [], "scenarios": [
            {{scenarios}}
              ] } ]
            }
            """);
        return path;
    }
}
