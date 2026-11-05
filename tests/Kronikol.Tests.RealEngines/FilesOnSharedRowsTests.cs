namespace Kronikol.Tests.RealEngines;

/// <summary>
/// A file on a row a Background shares. On 5.28.2 the shift of #223 moved it off that row and the report was written, and a fix
/// of the shift alone puts it back on a row two scenarios hold, which stops the report: so each scenario keeps its own rows, and
/// a walk visits a step once (plans/STEP_IDENTITY_PLAN.md 5.4). One shape stopping the report takes every fact here with it.
/// </summary>
public class FilesOnSharedRowsTests
{
    private static ReportRows Rows => EngineLane.Run("Engine.Reqnroll.Files").Rows;

    private static void HaveEachTheirOwnFile(string first, string second, string row)
    {
        var a = Rows.FilesOn(first, row);
        var b = Rows.FilesOn(second, row);
        Assert.NotEmpty(a);
        Assert.NotEmpty(b);
        Assert.Empty(a.Intersect(b));
    }

    [Fact]
    public void S1_a_file_in_a_background_step_with_a_hook_step_in_every_scenario() => HaveEachTheirOwnFile("S1a", "S1b", "b0");

    [Fact]
    public void S2_a_file_after_every_step_with_a_hook_step_in_every_scenario() => HaveEachTheirOwnFile("S2a", "S2b", "b0");

    [Fact]
    public void S3_a_file_in_a_background_step_with_no_hook_step_at_all() => HaveEachTheirOwnFile("S3a", "S3b", "b0");

    [Fact]
    public void S4_a_file_in_a_background_step_with_a_hook_step_on_the_first_scenario_only() => HaveEachTheirOwnFile("S4a", "S4b", "b0");

    [Fact]
    public void S5_a_failure_screenshot_stays_on_the_scenario_that_failed()
    {
        Assert.Single(Rows.FilesOn("S5a", "b0"));
        Assert.Empty(Rows.FilesOn("S5b", "b0"));
        Assert.Contains("b0 Given a customer that fails when tagged [Failed]", Rows.Of("S5a"));
        Assert.DoesNotContain("[Failed]", Rows.Of("S5b"));
    }

    [Fact]
    public void S6_a_file_after_every_step_where_no_Background_is_written() => HaveEachTheirOwnFile("S6a", "S6b", "1");

    [Fact]
    public void BG8_a_failure_screenshot_at_the_step_they_share_stays_on_the_scenario_that_failed()
    {
        Assert.Single(Rows.FilesOn("BG8a", "0"));
        Assert.Empty(Rows.FilesOn("BG8b", "0"));
        Assert.Contains("0 Given a customer that fails when tagged [Failed]", Rows.Of("BG8a"));
        Assert.DoesNotContain("[Failed]", Rows.Of("BG8b"));
    }

    [Fact]
    public void BG12_a_file_after_every_step_stays_on_each_scenarios_own_rows_when_one_skips_itself_in_a_hook()
    {
        HaveEachTheirOwnFile("BG12a", "BG12c", "0");
        Assert.StartsWith("0 Given a registered customer", Rows.Of("BG12a"));
    }

    [Fact]
    public void BG13_a_file_on_a_hook_steps_row_stays_on_that_scenarios_row() => HaveEachTheirOwnFile("BG13a", "BG13b", "0");

    [Fact]
    public void S7_a_hook_that_attaches_then_throws_writes_a_report()
    {
        Assert.Contains("S7b", string.Join(' ', Rows.ScenarioNames));
    }
}
