using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// Clustering earns its place by turning twenty scenarios broken by one connection refusal into one fact.
/// It pays for that with suppression, and suppression is only safe while the key is right — which it will
/// not always be, because grouping by a first line is a heuristic. It was measurably wrong while the
/// xUnit v3 adapter prefixed every message with its failure cause: fifteen unrelated failures became one
/// group, and the digest worked through one of them.
///
/// <para>So the protection is not a better key, which is the thing that cannot be relied on. It is that no
/// one cluster may absorb the run — a big group is sampled rather than represented by a single instance,
/// so a key that merged unrelated failures shows more than one kind — and that every cap says what it left
/// out, because a truncated list that looks complete is how a reader concludes there was nothing else.</para>
/// </summary>
public class DigestSuppressionCapTests
{
    private static FailuresDigest DigestOf(int count, Func<int, string> message) =>
        FailuresDigestGenerator.Generate(
            [new Feature
            {
                DisplayName = "Checkout",
                Scenarios = Enumerable.Range(0, count).Select(i => new Scenario
                {
                    Id = $"t{i}",
                    DisplayName = $"Scenario {i}",
                    Result = ExecutionResult.Failed,
                    ErrorMessage = message(i)
                }).ToArray()
            }],
            null, "TestRunReport", "3.1.0");

    private static int WorkedExamples(string markdown) =>
        markdown.ReplaceLineEndings("\n").Split('\n').Count(l => l.StartsWith("### ", StringComparison.Ordinal)
                                                                 && l.Contains('›', StringComparison.Ordinal));

    [Fact]
    public void A_cluster_is_sampled_rather_than_represented_by_one_example()
    {
        // Fifteen failures sharing a first line: the exact shape the broken key produced.
        var digest = DigestOf(15, i => $"Assert.Equal() Failure\nExpected: {i}\nActual: {i + 1}");

        Assert.True(WorkedExamples(digest.Markdown) > 1,
            $"one cluster of 15 was worked through {WorkedExamples(digest.Markdown)} time(s)");
    }

    [Fact]
    public void A_suppressed_failure_still_carries_its_own_error_text()
    {
        var digest = DigestOf(15, i => $"Assert.Equal() Failure\nExpected: {i}\nActual: {i + 1}");

        // Every failure's own message reaches the file, not just the line they share. Otherwise a group is
        // a claim about fifteen failures backed by the text of one. Two shapes, because a worked example
        // keeps the message's own line breaks inside its fenced block while the closing table flattens it.
        for (var i = 0; i < 15; i++)
        {
            var flattened = $"Expected: {i} Actual: {i + 1}";
            var fenced = $"Expected: {i}\nActual: {i + 1}";
            Assert.True(
                digest.Markdown.Contains(flattened, StringComparison.Ordinal)
                || digest.Markdown.ReplaceLineEndings("\n").Contains(fenced, StringComparison.Ordinal),
                $"failure {i} is in the file only as a line it shares with fourteen others");
        }
    }

    [Fact]
    public void A_run_that_fails_wholesale_still_produces_a_file_small_enough_to_read()
    {
        // The digest's whole promise. Unbounded, a 1,200-failure run listed every cluster member and then
        // every remaining failure, and the file that is meant to be read whole could not be.
        var digest = DigestOf(1_200, i => $"Assert.Equal() Failure\nExpected: {i}\nActual: {i + 1}");

        Assert.True(digest.Markdown.Length < 80_000,
            $"1,200 failures produced {digest.Markdown.Length} characters of markdown");
    }

    [Fact]
    public void Every_cap_says_how_many_it_left_out()
    {
        var digest = DigestOf(1_200, i => $"Assert.Equal() Failure\nExpected: {i}\nActual: {i + 1}");

        // A list that stops without saying so reads as "that was all of them".
        Assert.Contains("more not listed", digest.Markdown, StringComparison.Ordinal);

        // ...and the jsonl, which has no display budget, still has every one of them.
        Assert.Equal(1_200, FailuresJsonl.Failures(digest.Jsonl).Count());
    }

    [Fact]
    public void A_small_run_is_listed_in_full_with_nothing_elided()
    {
        // Non-vacuity: a cap that fires on ordinary runs would make every digest say it hid something.
        var digest = DigestOf(4, i => $"Assert.Equal() Failure\nExpected: {i}\nActual: {i + 1}");

        Assert.DoesNotContain("more not listed", digest.Markdown, StringComparison.Ordinal);
        for (var i = 0; i < 4; i++)
            Assert.Contains($"Scenario {i}", digest.Markdown, StringComparison.Ordinal);
    }
}
