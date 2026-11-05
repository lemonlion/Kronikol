namespace Kronikol.Tests.RealEngines;

/// <summary>
/// What one scenario does to the others in its feature: nothing, whether the difference is a failed step, a plain hook, a skip or a hook's own step method,
/// and whether the feature writes a Background or only opens each scenario alike. An earlier cut let a hook's row and a row that did not run reach the guess
/// at a shared Background, and a passing sibling moved, or read another's outcome (plans/STEP_IDENTITY_PLAN.md 5.4, BG1 to BG12). What a scenario that stops
/// early lists after the row that stopped it is the concern of the steps that did not run, so those facts read that row alone.
/// </summary>
public class SiblingsTests
{
    public static TheoryData<string> Lanes => ReqnrollStepsTests.Lanes;

    private static string Of(string lane, string scenario) => EngineLane.Run(lane).Rows.Of(scenario);

    private static void Reads(string lane, string scenario, string expected) => Assert.Equal(expected.ReplaceLineEndings("\n"), Of(lane, scenario));

    private static string Opening(int amount) => $"""
        0 Given the shop is ready
        1 When the customer places the order
          1.0 An order is submitted
        2 Then order {amount} is read back
        calls: 0 /ready, 1 /orders, 2 /orders/{amount}
        """;

    private static string UnderTheBackground(int amount) => $"""
        b0 Given the shop is ready
        0 When an order of {amount} is placed
        1 Then the order is confirmed
          1.0 The order is read back
        calls: b0 /ready, 0 /orders/{amount}, 1 /orders/1
        """;

    [Theory, MemberData(nameof(Lanes))]
    public void BG1_a_Given_that_fails_in_the_scenario_after_it_leaves_a_passing_scenario_its_rows(string lane)
    {
        Reads(lane, "BG1a", Opening(11));
        Assert.StartsWith("0 Given the shop is ready [Failed]", Of(lane, "BG1b"));
    }

    [Theory, MemberData(nameof(Lanes))]
    public void BG2_a_Given_that_fails_in_the_scenario_before_it_leaves_a_passing_scenario_its_rows(string lane)
    {
        Reads(lane, "BG2b", Opening(22));
        Assert.StartsWith("0 Given the shop is ready [Failed]", Of(lane, "BG2a"));
    }

    [Theory, MemberData(nameof(Lanes))]
    public void BG3_a_plain_hook_that_fails_leaves_the_others_their_rows(string lane)
    {
        Assert.DoesNotContain("no scenario", Of(lane, "BG3a"));
        Reads(lane, "BG3b", Opening(32));
        Reads(lane, "BG3c", Opening(33));
    }

    [Theory, MemberData(nameof(Lanes))]
    public void BG5_a_hook_step_in_every_scenario_keeps_its_own_argument(string lane)
    {
        Reads(lane, "BG5a", """
            0 Seed for {name alice}
            1 Given the shop is ready
            2 When the customer places the order
              2.0 An order is submitted
            3 Then order 51 is read back
            calls: 0 /seed/alice, 1 /ready, 2 /orders, 3 /orders/51
            """);
        Reads(lane, "BG5b", """
            0 Seed for {name bob}
            1 Given the shop is ready
            2 When the customer places the order
              2.0 An order is submitted
            3 Then order 52 is read back
            calls: 0 /seed/bob, 1 /ready, 2 /orders, 3 /orders/52
            """);
    }

    [Theory, MemberData(nameof(Lanes))]
    public void BG7_a_hook_step_that_fails_in_one_scenario_passes_in_the_other(string lane)
    {
        Reads(lane, "BG7a", """
            0 Reference data is seeded
            1 Given the shop is ready
            2 When the customer places the order
              2.0 An order is submitted
            3 Then order 71 is read back
            calls: 0 /seed, 1 /ready, 2 /orders, 3 /orders/71
            """);
        Assert.StartsWith("0 A seed that fails [Failed]", Of(lane, "BG7b"));
    }

    [Theory, MemberData(nameof(Lanes))]
    public void BG11_a_scenario_that_skips_itself_in_a_hook_leaves_the_others_their_rows(string lane)
    {
        Assert.DoesNotContain("no scenario", Of(lane, "BG11a"));
        Reads(lane, "BG11b", Opening(112));
        Reads(lane, "BG11c", Opening(113));
    }

    [Theory, MemberData(nameof(Lanes))]
    public void WB1_a_scenario_ahead_of_the_failure_keeps_its_rows_under_a_written_Background(string lane) => Reads(lane, "WB1 ", UnderTheBackground(61));

    [Theory, MemberData(nameof(Lanes))]
    public void WB4_a_scenario_behind_them_all_keeps_its_rows_under_a_written_Background(string lane) => Reads(lane, "WB4 ", UnderTheBackground(68));

    [Theory, MemberData(nameof(Lanes))]
    public void WB2_the_scenario_that_fails_in_the_Background_reads_its_own_row_failed(string lane) =>
        Assert.StartsWith("b0 Given the shop is ready [Failed]", Of(lane, "WB2 "));

    [Theory, MemberData(nameof(Lanes))]
    public void WB5_a_hook_step_keeps_its_own_argument_and_stands_after_the_written_Background(string lane)
    {
        Reads(lane, "WB5a", """
            b0 Given the shop is ready
            0 Seed for {name alice}
            1 When an order of 65 is placed
            2 Then the order is confirmed
              2.0 The order is read back
            calls: 0 /seed/alice, b0 /ready, 1 /orders/65, 2 /orders/1
            """);
        Reads(lane, "WB5b", """
            b0 Given the shop is ready
            0 Seed for {name bob}
            1 When an order of 66 is placed
            2 Then the order is confirmed
              2.0 The order is read back
            calls: 0 /seed/bob, b0 /ready, 1 /orders/66, 2 /orders/1
            """);
    }

    [Theory, MemberData(nameof(Lanes))]
    public void WB7_a_hook_step_that_fails_is_the_failed_row_and_takes_no_Background(string lane) =>
        Assert.Contains("0 A seed that fails [Failed]", Of(lane, "WB7 "));

    [Theory, MemberData(nameof(Lanes))]
    public void WB3_a_plain_hook_that_fails_leaves_its_scenario_in_the_report(string lane) =>
        Assert.DoesNotContain("no scenario", Of(lane, "WB3 "));

    [Theory, MemberData(nameof(Lanes))]
    public void WB11_a_scenario_that_skips_itself_in_a_hook_stays_in_the_report(string lane) =>
        Assert.DoesNotContain("no scenario", Of(lane, "WB11 "));
}
