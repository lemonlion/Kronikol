namespace Kronikol.Tests.RealEngines;

/// <summary>
/// What LightBDD's real step decorators do to a scenario's steps. A step method outside a LightBDD step makes a call like any other,
/// and every call of a scenario stays in it, the last one too (plans/STEP_IDENTITY_PLAN.md F13, F14).
/// </summary>
public class LightBddStepsTests
{
    private static void Reads(string scenario, string expected, params string[] withoutCalls) =>
        Assert.Equal(expected.ReplaceLineEndings("\n"), EngineLane.Run("Engine.LightBDD").Rows.Of(scenario, 0, withoutCalls));

    // Its bar draws the type's name where its row lists the value, so a walk loses it (F17). A call's own stamp does not.
    [Fact]
    public void L5_a_step_with_an_object_parameter_keeps_its_calls()
    {
        var read = EngineLane.Run("Engine.LightBDD").Rows.Of("L5");

        Assert.True(read.Contains("0 /customers", StringComparison.Ordinal), read);
        Assert.True(read.Contains("1 /orders", StringComparison.Ordinal), read);
    }

    [Fact]
    public void L0_the_control() => Reads("L0", """
        0 GIVEN Given a registered customer
          0.0 A customer is registered
        1 WHEN When the customer places the order
          1.0 An order is submitted
        2 THEN Then the order is confirmed
          2.0 The order is read back
        calls: 0 /customers, 1 /orders, 2 /orders/1
        """);

    [Fact]
    public void L1_a_step_method_in_a_scenario_decorator_stands_first_and_its_call_is_a_call() => Reads("L1", """
        0 Reference data is seeded
        1 GIVEN Given a registered customer
          1.0 A customer is registered
        2 WHEN When the customer places the order
          2.0 An order is submitted
        3 THEN Then the order is confirmed
          3.0 The order is read back
        calls: 0 /seed, 1 /customers, 2 /orders, 3 /orders/1
        """);

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
        calls: 0 /seed, 0 /customers, 0 /orders, 1 /orders/1
        """);

    [Fact]
    public void L3_a_failing_composite_sub_step_is_listed_with_what_did_not_run() => Reads("L3", """
        0 GIVEN Given a composite step whose first step fails [Failed]
          0.0 Native sub step that fails [Failed]
          0.1 Native sub step two [SkippedAfterFailure]
        1 THEN Then the order is confirmed [SkippedAfterFailure]
        calls: 0 /payments
        """);

    // A step method left running makes its call when it wakes, under whichever step is open by then, so that call is not read here.
    [Fact]
    public void L4_a_step_method_left_running_keeps_its_place() => Reads("L4", """
        0 GIVEN Given a step method is left running
          0.0 A slow background check
        1 WHEN When the customer places the order
          1.0 An order is submitted
        2 THEN Then the order is confirmed
          2.0 The order is read back
        calls: 0 /started, 1 /orders, 2 /orders/1
        """, "/background");

    [Fact]
    public void L9_a_step_method_that_fails_in_a_scenario_decorator_is_the_failed_row_and_its_call_stays_on_it() => Reads("L9", """
        0 Seeding fails [Failed]
        1 GIVEN Given a registered customer [SkippedAfterFailure]
        2 WHEN When the customer places the order [SkippedAfterFailure]
        3 THEN Then the order is confirmed [SkippedAfterFailure]
        calls: 0 /seed
        """);

    [Fact]
    public void V1_plain_calls_read_their_step() => Reads("V1", """
        0 GIVEN Plain given
        1 WHEN Plain when
        2 THEN Plain then
        calls: 0 /v1, 1 /v2, 2 /v3
        """);

    [Fact]
    public void M4_a_row_that_did_not_run_leaves_the_rows_that_ran_read_by_their_ids() => Reads("M4", """
        0 GIVEN Given a step whose title is longer than any bar draws it whole so its bar cannot be matched against its text by anyone
        1 WHEN When the next step fails [Failed]
        2 THEN Then the order is confirmed [SkippedAfterFailure]
        calls: 0 /long, 1 /fails
        """);
}
