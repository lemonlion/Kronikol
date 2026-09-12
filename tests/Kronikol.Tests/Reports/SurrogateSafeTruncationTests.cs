using System.Text;
using Kronikol.Reports;
using Kronikol.Tool.Query;

namespace Kronikol.Tests.Reports;

/// <summary>
/// Cutting a string to a length, when the string may contain an emoji.
///
/// <para>C# measures a string in UTF-16 code units, and every character outside the Basic Multilingual
/// Plane — emoji, and a great deal of CJK — occupies two of them. Cutting at an arbitrary index can
/// therefore land between the halves of a surrogate pair and leave a lone surrogate, which is not a
/// character at all. <see cref="File.WriteAllText(string,string)"/> encodes UTF-8 with the throwing
/// fallback, so a digest containing one does not come out mangled: <b>it does not come out</b>. The write
/// throws, <c>RunOutputs</c> catches everything and records a diagnostic, and the PREVIOUS run's
/// <c>Failures.md</c> stays on disk — stale, plausible, and describing a different run.</para>
///
/// <para>Assertion messages and captured third-party payloads are exactly where an emoji turns up, and
/// both are truncated at eight places in the digest and one in the query tool.</para>
/// </summary>
public class SurrogateSafeTruncationTests
{
    /// <summary>A string whose character at the cut index is a surrogate pair: "aaa…🙂…".</summary>
    private static string WithPairAt(int index, int total) =>
        new string('a', index) + "\U0001F642" + new string('b', Math.Max(0, total - index - 2));

    [Theory]
    [InlineData(80)]
    [InlineData(160)]
    [InlineData(600)]
    public void The_digest_does_not_split_a_surrogate_pair(int limit)
    {
        // The pair straddles the cut: its high half is the last code unit that fits.
        var message = WithPairAt(limit - 1, limit * 2);

        var markdown = FailuresDigestGenerator.Generate(
            [new Feature
            {
                DisplayName = "Checkout",
                Scenarios =
                [
                    new Scenario { Id = "t0", DisplayName = "Pay", Result = ExecutionResult.Failed, ErrorMessage = message }
                ]
            }],
            null, "TestRunReport", "3.1.0").Markdown;

        AssertEncodable(markdown);
    }

    [Fact]
    public void A_digest_carrying_an_emoji_can_actually_be_written_to_disk()
    {
        // The property that matters, stated as the operation that used to fail. An assertion about lone
        // surrogates is a proxy; this is the thing itself.
        var message = WithPairAt(599, 1400);
        var digest = FailuresDigestGenerator.Generate(
            [new Feature
            {
                DisplayName = "Checkout",
                Scenarios =
                [
                    new Scenario { Id = "t0", DisplayName = "Pay \U0001F642", Result = ExecutionResult.Failed, ErrorMessage = message }
                ]
            }],
            null, "TestRunReport", "3.1.0");

        var path = Path.Combine(Directory.CreateTempSubdirectory("kronikol-surrogate").FullName, "Failures.md");
        try
        {
            File.WriteAllText(path, digest.Markdown);
            File.WriteAllText(path + ".jsonl", digest.Jsonl);
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); } catch (IOException) { }
        }
    }

    [Theory]
    [InlineData(40)]
    [InlineData(160)]
    [InlineData(240)]
    public void The_query_tool_does_not_split_one_either(int max)
    {
        // QueryWriter.OneLine carries the identical defect, and its output goes to a terminal and into
        // the --json envelope rather than to a file, so it mangles rather than throws - which is worse,
        // because nothing anywhere reports it.
        var flattened = QueryWriter.OneLine(WithPairAt(max - 1, max * 2), max);

        AssertEncodable(flattened);
    }

    [Fact]
    public void A_string_that_needs_no_cutting_is_returned_unchanged()
    {
        // The non-vacuity half: a truncation that returned "" or dropped every non-ASCII character would
        // satisfy every assertion above.
        Assert.Equal("short \U0001F642 text", QueryWriter.OneLine("short \U0001F642 text", 160));
    }

    [Fact]
    public void Cutting_still_happens_and_still_marks_itself()
    {
        var cut = QueryWriter.OneLine(new string('a', 500), 160);

        Assert.EndsWith("…", cut, StringComparison.Ordinal);
        // At most the budget plus the ellipsis - a cut that kept the whole string would pass the
        // surrogate assertions trivially.
        Assert.True(cut.Length <= 161, $"cut to {cut.Length}");
    }

    /// <summary>Fails when <paramref name="text"/> holds a lone surrogate, naming where.</summary>
    private static void AssertEncodable(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                Assert.True(i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]),
                    $"lone high surrogate at {i} — this string cannot be written as UTF-8");
                i++;
            }
            else
            {
                Assert.False(char.IsLowSurrogate(text[i]), $"lone low surrogate at {i}");
            }
        }

        // And the operation that actually throws, rather than only the shape that causes it.
        new UTF8Encoding(false, throwOnInvalidBytes: true).GetBytes(text);
    }
}
