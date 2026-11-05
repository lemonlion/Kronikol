namespace Kronikol.Tests.RealEngines;

/// <summary>
/// What a BDDfy test's scenario reads, on the real engine: the result xUnit gave it when it ran more than one story or row, how long it took (#122), and
/// a step's own words. The report read the first story alone, and BDDfy's stopwatch stopped at a step's first <c>await</c>.
/// </summary>
public class BddfyOutcomeTests
{
    private static ReportRows Rows => EngineLane.Run("Engine.BDDfy").Rows;

    // BDDfy takes xUnit's skip for a failure, so the test xUnit skipped read Failed, with xUnit's marker in its message.
    [Fact]
    public void S1_a_step_that_skips_the_test_reads_Skipped_with_its_reason()
    {
        Assert.Equal("Skipped", Rows.ResultOf("S1"));
        Assert.Null(Rows.ErrorOf("S1"));
        Assert.Equal("not on this platform", Rows.StepBypassReasonOf("S1", 1));
        Assert.Contains("1 When the step skips the test [Skipped]", Rows.Of("S1"));
        Assert.DoesNotContain("SkippedAfterFailure", Rows.Of("S1"));
    }

    [Fact]
    public void O0_the_control()
    {
        Assert.Equal("Passed", Rows.ResultOf("O0"));
        Assert.Null(Rows.ErrorOf("O0"));
    }

    [Fact]
    public void O1_the_second_storys_failure_fails_the_test()
    {
        Assert.Equal("Failed", Rows.ResultOf("O1"));
        Assert.Contains("the second story's failure", Rows.ErrorOf("O1"));
    }

    [Fact]
    public void O2_a_story_that_passes_when_run_again_passes_the_test()
    {
        Assert.Equal("Passed", Rows.ResultOf("O2"));
        Assert.Null(Rows.ErrorOf("O2"));
    }

    [Fact]
    public void O3_a_later_rows_failure_fails_the_test()
    {
        Assert.Equal("Failed", Rows.ResultOf("O3"));
        Assert.Contains("the second row's failure", Rows.ErrorOf("O3"));
    }

    [Fact]
    public void D1_an_async_step_reads_as_long_as_its_task_took()
    {
        Assert.True(Rows.StepDurationOf("D1", 1) >= TimeSpan.FromMilliseconds(350), $"When read {Rows.StepDurationOf("D1", 1)}");
        Assert.True(Rows.StepDurationOf("D1", 2) >= TimeSpan.FromMilliseconds(150), $"Then read {Rows.StepDurationOf("D1", 2)}");
    }

    [Fact]
    public void D1_a_scenario_reads_as_long_as_its_steps_took() =>
        Assert.True(Rows.DurationOf("D1") >= TimeSpan.FromMilliseconds(550), $"read {Rows.DurationOf("D1")}");

    [Fact]
    public void D2_a_test_of_two_stories_reads_as_long_as_both_took() =>
        Assert.True(Rows.DurationOf("D2") >= TimeSpan.FromMilliseconds(550), $"read {Rows.DurationOf("D2")}");

    [Fact]
    public void K1_a_step_named_by_an_attribute_reads_its_keyword() =>
        Assert.Equal("""
            0 Given a thing is there
            1 And another is too
            2 When the thing is used
            3 Then it worked
            calls:
            """.ReplaceLineEndings("\n"), Rows.Of("K1").TrimEnd());
}
