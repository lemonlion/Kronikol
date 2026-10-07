using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The CI summary's rows add up to its Scenarios row (#105, CUCUMBER_BYPASS_PLAN.md F4): a bypassed or
/// skipped-after-failure scenario was in the total and in no row, which LightBDD runs have shown since the adapter
/// mapped a bypass and ingested runs would show once ingest reads one.
/// </summary>
public class CiSummaryCountsTests
{
    private static readonly DateTime Start = new(2026, 10, 7, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 10, 7, 10, 2, 34, DateTimeKind.Utc);

    private static string Table(params ExecutionResult[] results)
    {
        var feature = new Feature
        {
            DisplayName = "Orders",
            Scenarios = results.Select((r, i) => new Scenario { Id = $"s{i}", DisplayName = $"Scenario {i}", Result = r }).ToArray(),
        };
        var markdown = CiSummaryGenerator.GenerateMarkdown([feature], [], [], Start, End).Replace("\r\n", "\n");
        var from = markdown.IndexOf("| Metric | Value |", StringComparison.Ordinal);
        var to = markdown.IndexOf("| Duration |", from, StringComparison.Ordinal);
        return markdown[from..markdown.IndexOf('\n', to)];
    }

    [Fact]
    public void A_bypassed_scenario_has_a_row_of_its_own()
    {
        var table = Table(ExecutionResult.Passed, ExecutionResult.Bypassed, ExecutionResult.Bypassed);

        Assert.Contains("| Scenarios | 3 |\n| Passed | 1 |\n| Failed | 0 |\n| Skipped | 0 |\n| Bypassed | 2 |\n| Duration |", table);
    }

    [Fact]
    public void A_scenario_skipped_after_a_failure_counts_as_skipped()
    {
        var table = Table(ExecutionResult.Failed, ExecutionResult.SkippedAfterFailure, ExecutionResult.Skipped);

        Assert.Contains("| Scenarios | 3 |\n| Passed | 0 |\n| Failed | 1 |\n| Skipped | 2 |\n| Duration |", table);
    }

    [Fact]
    public void A_run_with_no_bypass_writes_the_rows_it_always_wrote()
    {
        Assert.Equal(
            "| Metric | Value |\n|---|---|\n| Status | ❌ Failed |\n| Scenarios | 3 |\n| Passed | 1 |\n| Failed | 1 |\n| Skipped | 1 |\n| Duration | 2m 34s |",
            Table(ExecutionResult.Passed, ExecutionResult.Failed, ExecutionResult.Skipped));
    }

    [Theory]
    [InlineData(new[] { ExecutionResult.Passed, ExecutionResult.Bypassed })]
    [InlineData(new[] { ExecutionResult.Skipped, ExecutionResult.SkippedAfterFailure, ExecutionResult.Failed, ExecutionResult.Bypassed })]
    [InlineData(new[] { ExecutionResult.Passed, ExecutionResult.Passed })]
    public void The_rows_add_up_to_the_scenarios_row(ExecutionResult[] results)
    {
        var rows = Table(results).Split('\n')
            .Select(l => l.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Where(c => c.Length == 2 && int.TryParse(c[1], out _))
            .ToDictionary(c => c[0], c => int.Parse(c[1]));

        Assert.Equal(rows["Scenarios"], rows.Where(r => r.Key != "Scenarios").Sum(r => r.Value));
    }
}
