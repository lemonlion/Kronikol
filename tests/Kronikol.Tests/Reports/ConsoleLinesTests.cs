namespace Kronikol.Tests.Reports;

public class ConsoleLinesTests
{
    [Fact]
    public void Only_the_lines_that_name_this_runs_directory_survive()
    {
        var mine = Path.Combine(Path.GetTempPath(), "kronikol-mine");
        var theirs = Path.Combine(Path.GetTempPath(), "kronikol-theirs");
        var console = string.Join('\n',
        [
            $"Kronikol: reports written to {theirs} (TestRunReport.json 3.1 KB)",
            $"⚠ WARNING: could not write TestRunReport.json: UnauthorizedAccessException: Access to the path '{Path.Combine(mine, "TestRunReport.json")}' is denied.",
            $"Kronikol: reports written to {mine} (Failures.md 0.3 KB)",
            "1 failed — kronikol query failures " + theirs,
        ]);

        var own = ConsoleLines.About(console, mine);

        Assert.Contains("could not write TestRunReport.json", own, StringComparison.Ordinal);
        Assert.Contains($"reports written to {mine}", own, StringComparison.Ordinal);
        Assert.DoesNotContain("3.1 KB", own, StringComparison.Ordinal);
        Assert.DoesNotContain("failed —", own, StringComparison.Ordinal);
    }
}
