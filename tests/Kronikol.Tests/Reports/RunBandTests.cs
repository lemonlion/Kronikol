using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The run band (5.0.0, plans/OPTION_C_PLAN.md S2, D3 and D5): the ring, the test execution summary and the CI zone on
/// one row, written by the generator in place of the 4.x header row with its two tables, and the filter box on a row of
/// its own after it.
/// </summary>
public class RunBandTests
{
    private static readonly DateTime Start = new(2026, 10, 16, 17, 27, 11, DateTimeKind.Utc);

    private static string GenerateReport(string fileName, Feature[] features, CiMetadata? ci = null)
    {
        var path = ReportGenerator.GenerateHtmlReport(
            [], features, Start, Start.AddSeconds(5), null, fileName, "Test", includeTestRunData: true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs, ciMetadata: ci);
        return ReportHtml.Read(path);
    }

    private static Feature[] Features(params ExecutionResult[] results) =>
    [
        new Feature
        {
            DisplayName = "Orders",
            Scenarios = results.Select((r, i) => new Scenario { Id = $"s{i}", DisplayName = $"Scenario {i}", Result = r }).ToArray()
        }
    ];

    private static readonly CiMetadata GitHub = new(CiEnvironment.GitHubActions, "322", "main",
        "0ee43e8c0b5d3e85a4a099a20ee4ce37305b6b3f", "https://github.com/lemonlion/BreakfastProvider/actions/runs/38868550876",
        "lemonlion/BreakfastProvider", "38868550876");

    [Fact]
    public void The_band_holds_the_ring_the_summary_and_the_ci_zone_in_that_order()
    {
        var html = GenerateReport("RunBand_Full.html",
            Features(ExecutionResult.Passed, ExecutionResult.Failed, ExecutionResult.Skipped), GitHub);

        var band = html.IndexOf("<section class=\"run-band\" aria-label=\"Test execution summary\"><div class=\"run-ring\"><svg ", StringComparison.Ordinal);
        Assert.True(band >= 0, "the band opens with the ring");
        Assert.Contains("<div class=\"run-zone run-summary\"><div class=\"run-head\"><h2 class=\"run-title\">Test Execution Summary</h2>"
                        + "<span class=\"run-status run-status-failed\">Failed</span>"
                        + "<span class=\"run-when\">2026-10-16 · 17:27:11–17:27:16 UTC</span></div>", html);
        Assert.Contains("<dl class=\"run-stats\"><div class=\"run-stat\"><dt>Scenarios</dt><dd>3</dd></div>"
                        + "<div class=\"run-stat run-stat-pass\"><dt>Passed</dt><dd>1</dd></div>"
                        + "<div class=\"run-stat run-stat-fail\"><dt>Failed</dt><dd>1</dd></div>"
                        + "<div class=\"run-stat\"><dt>Skipped</dt><dd>1</dd></div>"
                        + "<div class=\"run-stat\"><dt>Features</dt><dd>1</dd></div>"
                        + "<div class=\"run-stat\"><dt>Duration</dt><dd>5s</dd></div></dl></div>", html);
        Assert.Contains("<div class=\"run-zone run-ci\"><div class=\"run-head\"><h2 class=\"run-title\">CI</h2><span class=\"run-when\">GitHub Actions</span></div>"
                        + "<dl class=\"run-stats\"><div class=\"run-stat\"><dt>Build</dt><dd>#322</dd></div>"
                        + "<div class=\"run-stat\"><dt>Branch</dt><dd>main</dd></div>"
                        + "<div class=\"run-stat\"><dt>Commit</dt><dd><code title=\"0ee43e8c0b5d3e85a4a099a20ee4ce37305b6b3f\">0ee43e8</code></dd></div>"
                        + "<div class=\"run-stat\"><dt>Repository</dt><dd>lemonlion/BreakfastProvider</dd></div>"
                        + "<div class=\"run-stat\"><dt>Run</dt><dd><a class=\"run-link\" href=\"https://github.com/lemonlion/BreakfastProvider/actions/runs/38868550876\" target=\"_blank\" rel=\"noopener noreferrer\" title=\"Open the CI run this report came from\">View run</a></dd></div>"
                        + "</dl></div></section>", html);
        Assert.DoesNotContain("class=\"header-row\"", html);
        Assert.DoesNotContain("class=\"test-execution-summary\"", html);
        Assert.DoesNotContain("class=\"ci-metadata\"", html);
    }

    [Fact]
    public void A_run_without_failures_has_no_fail_mark_and_a_passed_status()
    {
        var html = GenerateReport("RunBand_Passed.html", Features(ExecutionResult.Passed, ExecutionResult.Passed));

        Assert.Contains("<span class=\"run-status run-status-passed\">Passed</span>", html);
        Assert.Contains("<div class=\"run-stat\"><dt>Failed</dt><dd>0</dd></div>", html);
        Assert.DoesNotContain("class=\"run-stat run-stat-fail\"", html);
    }

    [Fact]
    public void A_bypassed_scenario_is_counted_in_the_band()
    {
        var html = GenerateReport("RunBand_Bypassed.html", Features(ExecutionResult.Passed, ExecutionResult.Bypassed));

        Assert.Contains("<div class=\"run-stat\"><dt>Skipped</dt><dd>0</dd></div><div class=\"run-stat\"><dt>Bypassed</dt><dd>1</dd></div>", html);
    }

    /// <summary>4.x left a scenario skipped after a failure out of the summary and the ring altogether, so their counts
    /// did not add up to the scenarios; the features table already counted it as skipped.</summary>
    [Fact]
    public void A_scenario_skipped_after_a_failure_counts_as_skipped_in_the_band_and_the_ring()
    {
        var html = GenerateReport("RunBand_SkippedAfterFailure.html",
            Features(ExecutionResult.Passed, ExecutionResult.Failed, ExecutionResult.SkippedAfterFailure));

        Assert.Contains("<div class=\"run-stat\"><dt>Skipped</dt><dd>1</dd></div>", html);
        Assert.Contains("aria-label=\"33% passed\"", html);
    }

    [Fact]
    public void Without_ci_metadata_there_is_no_ci_zone()
    {
        var html = GenerateReport("RunBand_NoCi.html", Features(ExecutionResult.Passed));

        Assert.DoesNotContain("<div class=\"run-zone run-ci\">", html);
        Assert.Contains("</dl></div></section>", html);
    }

    [Fact]
    public void Azure_devops_is_named_as_it_names_itself()
    {
        var html = GenerateReport("RunBand_Azure.html", Features(ExecutionResult.Passed),
            new CiMetadata(CiEnvironment.AzureDevOps, "20261016.3", "main", null, null, null, null));

        Assert.Contains("<h2 class=\"run-title\">CI</h2><span class=\"run-when\">Azure DevOps</span>", html);
        Assert.Contains("<div class=\"run-stat\"><dt>Build</dt><dd>#20261016.3</dd></div>", html);
    }

    [Fact]
    public void The_filter_box_follows_the_band_on_its_own_row_with_the_ids_the_bar_points_at()
    {
        var html = GenerateReport("RunBand_FilterRow.html", Features(ExecutionResult.Passed), GitHub);

        var end = html.IndexOf("</section>", html.IndexOf("<section class=\"run-band\"", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.StartsWith("<div class=\"filtering-box rows-folded\" id=\"filtering-box\">", html[(end + "</section>".Length)..].TrimStart()); // folded under More filters (S7)
        Assert.Contains("<div class=\"filters\" id=\"filter-rows\">", html);
    }

    /// <summary>D4: whether the CI zone shares the ring's row is decided by a script, on the zones' measured widths,
    /// so the band's layout cannot oscillate as the row wraps; it is in the page after the theme switch.</summary>
    [Fact]
    public void The_band_layout_script_is_in_the_page_after_the_theme_switch()
    {
        var html = GenerateReport("Band_Script.html", Features(ExecutionResult.Passed), GitHub);
        var at = html.IndexOf("<script id=\"kronikol-run-band\">", StringComparison.Ordinal);

        Assert.True(at >= 0, "the band's layout script is in the page");
        Assert.True(at > html.IndexOf("<script id=\"kronikol-theme-switch\">", StringComparison.Ordinal), "after the theme switch");
        var script = html[at..html.IndexOf("</script>", at, StringComparison.Ordinal)];
        Assert.Contains("classList.toggle('ci-below'", script);
        Assert.Contains("addEventListener('resize'", script);
    }

    [Fact]
    public void The_kronikol_version_stays_in_the_generator_meta()
    {
        var html = GenerateReport("RunBand_Version.html", Features(ExecutionResult.Passed));

        Assert.Contains("<meta name=\"generator\" content=\"Kronikol v", html);
        Assert.DoesNotContain("Kronikol Version:", html);
    }

    [Theory]
    [InlineData(213, 1, 0, 0, "99%")]
    [InlineData(213, 0, 1, 0, "99%")]
    [InlineData(214, 0, 0, 0, "100%")]
    [InlineData(1, 1, 0, 0, "50%")]
    [InlineData(1, 999, 0, 0, "0%")]
    public void The_ring_never_reads_100_percent_unless_every_scenario_passed(int passed, int failed, int skipped, int bypassed, string rate)
    {
        var svg = ReportGenerator.GeneratePieChartSvg(passed, failed, skipped, bypassed);

        Assert.Contains($">{rate}</text>", svg);
    }

    [Fact]
    public void The_ring_draws_its_arcs_and_rate_with_classes_the_tokens_colour()
    {
        var svg = ReportGenerator.GeneratePieChartSvg(2, 1, 1, 1);

        Assert.StartsWith("<svg viewBox=\"0 0 100 100\" role=\"img\" aria-label=\"40% passed\">", svg);
        foreach (var kind in new[] { "pass", "fail", "skip", "bypass" })
            Assert.Contains($"<circle class=\"ring-{kind}\" cx=\"50\" cy=\"50\" r=\"40.0\"", svg);
        Assert.Contains("<text class=\"ring-rate\" x=\"50\" y=\"50\" text-anchor=\"middle\" dominant-baseline=\"central\">40%</text>", svg);
        Assert.DoesNotContain("#1daf26", svg);
    }
}
