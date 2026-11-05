namespace Kronikol.Tests.RealEngines;

/// <summary>
/// Plain xUnit v3 has no framework steps, so no frames: its collector runs as it always has, and a stamp gives what the walk over the
/// bars gives (plans/STEP_IDENTITY_PLAN.md 5.9). With the bars hidden a call inside a step keeps its step, which is what they cost.
/// </summary>
public class PlainLaneTests
{
    private static string Read(string scenario, bool barsHidden) =>
        (barsHidden
            ? EngineLane.Run("Engine.xUnit3", new Dictionary<string, string> { ["NO_BARS"] = "1" }, "bars hidden")
            : EngineLane.Run("Engine.xUnit3")).Rows.Of(scenario);

    private static string Calls(string read) => read[(read.LastIndexOf("calls: ", StringComparison.Ordinal) + "calls: ".Length)..];

    [Theory]
    [InlineData("P0", false, "0 /customers, 1 /orders")]
    [InlineData("P0", true, "0 /customers, 1 /orders")]
    [InlineData("P1", false, "0 /customers, 0 /between, 1 /orders")]
    [InlineData("P1", true, "0 /customers, 0 /between, 1 /orders")]
    [InlineData("P2", false, "- /before, 0 /customers, 0 /after")]
    [InlineData("P2", true, "- /before, 0 /customers, 0 /after")]
    [InlineData("P3", false, "0 /customers, 1 /after-empty-step")]
    [InlineData("P4", false, "0 /customers, 0 /teardown")]
    [InlineData("P4", true, "0 /customers, 0 /teardown")]
    [InlineData("P6", false, "0 /long, 1 /orders")]
    [InlineData("P6", true, "0 /long, 1 /orders")]
    [InlineData("P7", false, "0 /own, 0 /slow, 1 /orders")]
    [InlineData("P7", true, "0 /own, 0 /slow, 1 /orders")]
    public void Each_call_reads_the_step_it_read_before_there_were_stamps(string scenario, bool barsHidden, string calls) =>
        Assert.Equal(calls, Calls(Read(scenario, barsHidden)));

    // What the flow decides where a stack could not (#225): each of two steps at once reads its own calls, hidden bars read what
    // shown bars read, and a call beside a step left running reads that step while it runs.
    [Theory]
    [InlineData("P3", true, "0 /customers, 1 /after-empty-step")]
    [InlineData("P5", false, "0 /fast, 1 /slow, 2 /orders")]
    [InlineData("P5", true, "0 /fast, 1 /slow, 2 /orders")]
    [InlineData("P8", false, "0 /customers, 1 /open, 1 /after-open, 2 /orders, 1 /after-nested")]
    [InlineData("P8", true, "0 /customers, 1 /open, 1 /after-open, 2 /orders, 1 /after-nested")]
    public void Each_call_reads_the_step_its_flow_was_in(string scenario, bool barsHidden, string calls) =>
        Assert.Equal(calls, Calls(Read(scenario, barsHidden)));

    [Fact]
    public void P5_two_step_methods_in_parallel_are_siblings_in_the_order_they_began() => Assert.Equal("""
        0 A fast parallel
        1 A slow parallel
        2 An order is submitted
        calls: 0 /fast, 1 /slow, 2 /orders
        """.ReplaceLineEndings("\n"), Read("P5", barsHidden: false));

    [Fact]
    public void P8_a_step_still_open_at_report_time_is_listed_in_its_place() => Assert.Equal("""
        0 A customer is registered
        1 A step that never ends
        2 An order is submitted
        calls: 0 /customers, 1 /open, 1 /after-open, 2 /orders, 1 /after-nested
        """.ReplaceLineEndings("\n"), Read("P8", barsHidden: false));
}
