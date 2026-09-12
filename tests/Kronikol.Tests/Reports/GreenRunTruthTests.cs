using Kronikol.Reports;
using Kronikol.Tool;

namespace Kronikol.Tests.Reports;

/// <summary>
/// What a run with no failures is allowed to claim.
///
/// <para>"No failures" and "everything passed" are different facts, and a suite where half the scenarios
/// were skipped satisfies only the first. Both the digest and <c>kronikol query failures</c> printed the
/// second — each from its own arithmetic, each counting every scenario that did not fail as one that
/// passed. An agent told "All 12 scenarios passed" stops looking; it is the one sentence in the file that
/// ends the investigation, so it is the one that has to be true.</para>
/// </summary>
public class GreenRunTruthTests
{
    private static Feature[] Features(params ExecutionResult[] results) =>
    [
        new Feature
        {
            DisplayName = "Checkout",
            Scenarios = [.. results.Select((r, i) => new Scenario
            {
                Id = "t" + i,
                DisplayName = "Scenario " + i,
                Result = r,
                Duration = TimeSpan.FromSeconds(1)
            })]
        }
    ];

    private static string Digest(params ExecutionResult[] results) =>
        FailuresDigestGenerator.Generate(Features(results), null, "TestRunReport", "3.1.0").Markdown;

    [Fact]
    public void A_run_where_everything_really_passed_still_says_so()
    {
        var markdown = Digest(ExecutionResult.Passed, ExecutionResult.Passed, ExecutionResult.Passed);

        Assert.Contains("# No failures", markdown, StringComparison.Ordinal);
        Assert.Contains("All 3 scenarios passed", markdown, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ExecutionResult.Skipped)]
    [InlineData(ExecutionResult.Bypassed)]
    [InlineData(ExecutionResult.SkippedAfterFailure)]
    public void A_scenario_that_did_not_run_is_not_counted_as_one_that_passed(ExecutionResult notRun)
    {
        var markdown = Digest(ExecutionResult.Passed, notRun, notRun);

        // Still no failures — that part was never in doubt, and the heading is what the agent
        // instructions tell a reader to trust.
        Assert.Contains("# No failures", markdown, StringComparison.Ordinal);

        // But not "All 3 scenarios passed", because one did.
        Assert.DoesNotContain("All 3 scenarios passed", markdown, StringComparison.Ordinal);
        Assert.Contains("1 of 3", markdown, StringComparison.Ordinal);
        Assert.Contains("2 did not run", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void A_run_where_nothing_ran_at_all_says_that_and_not_that_it_passed()
    {
        var markdown = Digest(ExecutionResult.Skipped, ExecutionResult.Skipped);

        Assert.DoesNotContain("All 2 scenarios passed", markdown, StringComparison.Ordinal);
        Assert.Contains("0 of 2", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void The_query_tool_does_not_claim_it_either()
    {
        var directory = Directory.CreateTempSubdirectory("kronikol-green").FullName;
        try
        {
            var report = ReportGenerator.GenerateTestRunReportData(
                Features(ExecutionResult.Passed, ExecutionResult.Skipped, ExecutionResult.Skipped),
                new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
                Path.Combine(directory, "TestRunReport.json"), DataFormat.Json);

            var output = new StringWriter();
            var error = new StringWriter();
            var exit = QueryCommand.Run(["failures", report], output, error);

            Assert.True(exit == 0, error.ToString());
            Assert.DoesNotContain("all passed", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("1 passed", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("2 did not run", output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void The_query_tool_still_says_all_passed_when_they_all_did()
    {
        var directory = Directory.CreateTempSubdirectory("kronikol-green-all").FullName;
        try
        {
            var report = ReportGenerator.GenerateTestRunReportData(
                Features(ExecutionResult.Passed, ExecutionResult.Passed),
                new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
                Path.Combine(directory, "TestRunReport.json"), DataFormat.Json);

            var output = new StringWriter();
            var exit = QueryCommand.Run(["failures", report], output, new StringWriter());

            Assert.Equal(0, exit);
            Assert.Contains("all passed", output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }
}
