namespace Kronikol.Tests.Reports;

/// <summary>
/// <see cref="Console.SetOut"/> is process-wide. A test that captures the console to read its own run's
/// pointer also captures whatever any test in another collection prints meanwhile — and a report
/// written by that test prints its own pointer, sized and naming files, under this test's capture.
/// Measured once on CI: a stale-output assertion read <c>TestRunReport.json 3.1 KB</c> from a different
/// run's pointer and failed. Every assertion on captured console text is scoped to the lines that name
/// this run's own directory, which every line the run prints about itself does.
/// </summary>
internal static class ConsoleLines
{
    public static string About(string console, string directory) =>
        string.Join('\n', console.Split('\n').Where(l => l.Contains(directory, StringComparison.Ordinal)));
}
