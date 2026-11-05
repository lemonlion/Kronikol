namespace Kronikol.Tests.RealEngines;

/// <summary>
/// What LightBDD's real step decorators do to a scenario's steps. A scenario's last calls can be read out of the scenario by
/// LightBDD's own timer, by a margin that varies from run to run (plans/STEP_IDENTITY_PLAN.md F14), so until that is fixed these
/// facts hold the rows exactly, and each call listed to the step it must read, whichever calls are listed.
/// </summary>
public class LightBddStepsTests
{
    private static void Reads(string scenario, string rows, params string[] calls)
    {
        var read = EngineLane.Run("Engine.LightBDD").Rows.Of(scenario);
        var split = read.LastIndexOf("calls: ", StringComparison.Ordinal);

        Assert.Equal(rows.ReplaceLineEndings("\n") + "\n", read[..split]);
        foreach (var call in read[(split + "calls: ".Length)..].Split(", ", StringSplitOptions.RemoveEmptyEntries))
            Assert.Contains(call, calls);
    }

    // Its bar draws the type's name where its row lists the value, so a walk loses it (F17). A call's own stamp does not. Only the
    // first step's call is read: on this lane a later one can be listed as background, which is #223's R3 (its F14).
    [Fact]
    public void L5_a_step_with_an_object_parameter_keeps_its_calls()
    {
        var read = EngineLane.Run("Engine.LightBDD").Rows.Of("L5");

        Assert.True(read.Contains("0 /customers", StringComparison.Ordinal), read);
    }

    [Fact]
    public void L0_the_control() => Reads("L0", """
        0 GIVEN Given a registered customer
          0.0 A customer is registered
        1 WHEN When the customer places the order
          1.0 An order is submitted
        2 THEN Then the order is confirmed
          2.0 The order is read back
        """, "0 /customers", "1 /orders", "2 /orders/1");

    [Fact]
    public void L2_a_step_method_before_a_composite_steps_own_steps_stands_first_among_them() => Reads("L2", """
        0 GIVEN Given a composite step that seeds first
          0.0 Reference data is seeded
          0.1 Native sub step one
            0.1.0 A customer is registered
          0.2 Native sub step two
            0.2.0 An order is submitted
        1 THEN Then the order is confirmed
          1.0 The order is read back
        """, "0 /seed", "0 /customers", "0 /orders", "1 /orders/1");

    [Fact]
    public void L3_a_failing_composite_sub_step_is_listed_with_what_did_not_run() => Reads("L3", """
        0 GIVEN Given a composite step whose first step fails [Failed]
          0.0 Native sub step that fails [Failed]
          0.1 Native sub step two [SkippedAfterFailure]
        1 THEN Then the order is confirmed [SkippedAfterFailure]
        """, "0 /payments");

    [Fact]
    public void L4_a_step_method_left_running_keeps_its_place() => Reads("L4", """
        0 GIVEN Given a step method is left running
          0.0 A slow background check
        1 WHEN When the customer places the order
          1.0 An order is submitted
        2 THEN Then the order is confirmed
          2.0 The order is read back
        """, "0 /started", "1 /orders", "2 /orders/1", "2 /background", "1 /background", "0 /background");

    [Fact]
    public void V1_plain_calls_read_their_step() => Reads("V1", """
        0 GIVEN Plain given
        1 WHEN Plain when
        2 THEN Plain then
        """, "0 /v1", "1 /v2", "2 /v3");

    [Fact]
    public void M4_a_row_that_did_not_run_leaves_the_rows_that_ran_read_by_their_ids() => Reads("M4", """
        0 GIVEN Given a step whose title is longer than any bar draws it whole so its bar cannot be matched against its text by anyone
        1 WHEN When the next step fails [Failed]
        2 THEN Then the order is confirmed [SkippedAfterFailure]
        """, "0 /long", "1 /fails");

    [Fact]
    public void L1_a_step_method_in_a_scenario_decorator_stands_first_and_no_step_that_never_ran_takes_its_failure()
    {
        var read = EngineLane.Run("Engine.LightBDD").Rows.Of("L1");

        Assert.StartsWith("0 Reference data is seeded [Failed]\n1 GIVEN Given a registered customer", read);
    }
}
