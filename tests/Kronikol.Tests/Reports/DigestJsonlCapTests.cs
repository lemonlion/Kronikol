using System.Text.Json;
using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// <c>Failures.jsonl</c> is the digest's machine-readable half, and the digest's whole promise is a file
/// small enough to read whole. The markdown keeps that promise — 600 characters of error, 160 of expected
/// and actual. The jsonl kept none of it: every free-text field went in at full length, and
/// <c>cluster</c> is the first line of <c>errorMessage</c>, so that line was written twice per record. A
/// measured 2,000,047-character message produced 4,000,639 bytes.
///
/// <para>An assertion message reaches that size the ordinary way: a failed comparison of two captured
/// response bodies, which is the case the digest exists for. The full text is never lost — it is in
/// <c>TestRunReport.json</c>, at the address and stableId every record already carries — so a bounded
/// field here costs a consumer one dereference and saves it the file.</para>
/// </summary>
public class DigestJsonlCapTests
{
    private static JsonElement Failure(string message, string? expected = null, string? actual = null)
    {
        var digest = FailuresDigestGenerator.Generate(
            [new Feature
            {
                DisplayName = "Checkout",
                Scenarios = [new Scenario { Id = "t0", DisplayName = "Pay", Result = ExecutionResult.Failed, ErrorMessage = message }]
            }],
            null, "TestRunReport", "3.1.0");

        return FailuresJsonl.Failures(digest.Jsonl).Single();
    }

    [Fact]
    public void A_huge_message_does_not_become_a_huge_jsonl()
    {
        var message = "Assert.Equal() Failure: Values differ\nExpected: " + new string('x', 2_000_000);

        var digest = FailuresDigestGenerator.Generate(
            [new Feature
            {
                DisplayName = "Checkout",
                Scenarios = [new Scenario { Id = "t0", DisplayName = "Pay", Result = ExecutionResult.Failed, ErrorMessage = message }]
            }],
            null, "TestRunReport", "3.1.0");

        // Generous enough that no real message is cut, small enough that one line stays one line.
        Assert.True(digest.Jsonl.Length < 32_000,
            $"one failure produced {digest.Jsonl.Length} characters of jsonl");
    }

    [Fact]
    public void The_cluster_key_is_capped_like_every_other_free_text_field()
    {
        var firstLine = "Assert.Equal() Failure: Values differ";
        var failure = Failure(firstLine + "\nExpected: 1\nActual: 2");

        // Kept verbatim when it fits, because it is what the markdown groups by and what a script groups
        // by, and the two halves of one digest must not disagree about which failures share a cause.
        Assert.Equal(firstLine, failure.GetProperty("cluster").GetString());

        // A single-line message IS its own first line, so the two fields there are the same text by
        // definition — which is exactly how the measured 2,000,047-character message came to 4,000,639
        // bytes. The duplication is not the bug and is not removed; writing it at full length was.
        var single = Failure(new string('y', 100_000));
        Assert.True(single.GetProperty("cluster").GetString()!.Length <= 4_001);
        Assert.True(single.GetProperty("errorMessage").GetString()!.Length <= 4_001);
        Assert.True(single.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public void A_truncated_record_says_so_rather_than_ending_in_an_ellipsis_and_hoping
        ()
    {
        var failure = Failure("Assert.Equal() Failure\n" + new string('z', 100_000));

        // A consumer cannot tell a cut from a message that genuinely ends in "…", and silently handing
        // back less evidence than exists is the defect this whole area keeps producing.
        Assert.True(failure.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public void A_record_that_fits_is_not_marked_truncated_and_is_not_cut()
    {
        // Non-vacuity: a cap that fires on everything reports nothing.
        var message = "Assert.Equal() Failure: Values differ\nExpected: 1\nActual: 2";
        var failure = Failure(message);

        Assert.Equal(message, failure.GetProperty("errorMessage").GetString());
        Assert.False(failure.GetProperty("truncated").GetBoolean());
    }
}
