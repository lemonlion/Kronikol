using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Tests.Reports;

namespace Kronikol.Tests.Tool;

/// <summary>
/// #101, fact D2: <c>kronikol query</c> names a Reqnroll outline row as Gherkin names its pickle, read from the data file
/// the report writes, and finds it by its filled name (plans/OUTLINE_ROW_NAMES_PLAN.md 4.3). On 5.4.0 every row of the
/// issue's outline read <c>A basket is weighed on the UK scale (&lt;case&gt;)</c> (harness results/before-5.4.0).
/// </summary>
public class QueryOutlineRowNameTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-query-outline-row").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private string Report()
    {
        using var _ = ReportGenerator.ScopeReportsDirectory(_directory);
        var at = new DateTime(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc);
        return ReportGenerator.GenerateTestRunReportData(OutlineRowKeySiteTests.Baskets(), at, at.AddSeconds(1),
            $"TestRunReport-{Guid.NewGuid():N}.json", DataFormat.Json, suite: "Probe101.xUnit2");
    }

    private static string Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run(args, output, error);
        Assert.True(exit == 0, $"exit {exit}: {error}");
        return output.ToString().Replace("\r\n", "\n");
    }

    [Fact]
    public void Scenarios_names_every_row_as_Gherkin_does()
    {
        var output = Run("scenarios", Report());

        Assert.Contains("A basket is weighed on the UK scale (empty basket)", output);
        Assert.Contains("A basket is weighed on the UK scale (one apple)", output);
        Assert.Contains(OutlineRowKeySiteTests.Filled, output);
        Assert.DoesNotContain("(<case>)", output);
    }

    [Fact]
    public void Failures_and_summary_name_the_failing_row_at_its_address()
    {
        var report = Report();

        foreach (var verb in new[] { "failures", "summary" })
        {
            var output = Run(verb, report);
            Assert.Contains(OutlineRowKeySiteTests.Filled, output);
            Assert.DoesNotContain("(<case>)", output);
        }
        // the row's address is its place in the data file, which the adapter's order keeps
        Assert.Contains($"s2  Baskets › {OutlineRowKeySiteTests.Filled}", Run("failures", report));
    }

    [Fact]
    public void Grep_finds_a_row_by_its_filled_name()
    {
        var output = Run("scenarios", "--grep", "two apples", Report());

        Assert.Contains(OutlineRowKeySiteTests.Filled, output);
        Assert.DoesNotContain("(empty basket)", output);
        Assert.DoesNotContain("(one apple)", output);
    }
}
