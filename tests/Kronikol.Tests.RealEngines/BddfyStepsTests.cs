namespace Kronikol.Tests.RealEngines;

/// <summary>What BDDfy's real executor does to a test's steps: a step method before <c>.BDDfy()</c>, in a constructor, after it, or left running.</summary>
public class BddfyStepsTests
{
    private const string TheStepsAsWritten = """
        0 Given a registered customer
          0.0 A customer is registered
        1 When the customer places the order
          1.0 An order is submitted
        2 Then the order is confirmed
          2.0 The order is read back
        """;

    private static void Reads(string scenario, string expected) =>
        Assert.Equal(expected.ReplaceLineEndings("\n"), EngineLane.Run("Engine.BDDfy").Rows.Of(scenario));

    [Fact]
    public void B0_the_control() => Reads("B0", TheStepsAsWritten + """

        calls: 0 /customers, 1 /orders, 2 /orders/1
        """);

    [Fact]
    public void B1_a_step_method_before_BDDfy_stands_first() => Reads("B1", """
        0 Reference data is seeded
        1 Given a registered customer
          1.0 A customer is registered
        2 When the customer places the order
          2.0 An order is submitted
        3 Then the order is confirmed
          3.0 The order is read back
        calls: 0 /seed, 1 /customers, 2 /orders, 3 /orders/1
        """);

    [Fact]
    public void B2_a_step_method_after_BDDfy_stands_last() => Reads("B2", TheStepsAsWritten + """

        3 Reference data is cleaned
        calls: 0 /customers, 1 /orders, 2 /orders/1
        """);

    [Fact]
    public void B3_a_step_method_left_running_keeps_its_place() => Reads("B3", """
        0 Given a step is left running
          0.0 A slow background check
        1 When the customer places the order
          1.0 An order is submitted
        2 Then the order is confirmed
          2.0 The order is read back
        calls: 0 /started, 1 /orders, 2 /orders/1
        """);

    [Fact]
    public void B4_a_step_method_in_the_constructor_stands_first() => Reads("B4", """
        0 Reference data is seeded
        1 Given a registered customer
          1.0 A customer is registered
        2 When the customer places the order
          2.0 An order is submitted
        3 Then the order is confirmed
          3.0 The order is read back
        calls: 0 /seed, 1 /customers, 2 /orders, 3 /orders/1
        """);

    [Fact]
    public void B5_an_example_table_lists_its_first_row_and_later_rows_calls_read_no_step() => Reads("B5", """
        0 Given the customer is named <customer>
          0.0 Customer named {name alice}
        1 Then the order is confirmed
          1.0 The order is read back
        calls: 0 /customers/alice, 1 /orders/1, - /customers/bob, - /orders/1
        """);

    [Fact]
    public void B6_a_call_after_an_async_steps_first_await_stays_under_it() => Reads("B6", """
        0 Given a registered customer
          0.0 A customer is registered
        1 When the customer places the order later
        2 Then the order is confirmed
          2.0 The order is read back
        calls: 0 /customers, 1 /orders-after-await, 2 /orders/1
        """);

    [Fact]
    public void B7_a_teardown_the_report_does_not_list_takes_no_row_and_its_call_no_step() => Reads("B7", """
        0 Given a registered customer
          0.0 A customer is registered
        1 Then the order is confirmed
          1.0 The order is read back
        calls: 0 /customers, 1 /orders/1, - /teardown-with
        """);

    [Fact]
    public void Z8_an_example_table_whose_first_row_stopped_early_lists_that_row_and_the_next_rows_frames_take_nothing() => Reads("Z8", """
        0 Given the customer is named <customer> [Failed]
          0.0 Customer named {name alice}
        1 Then the order is confirmed [SkippedAfterFailure]
        calls: 0 /customers/alice, - /customers/bob, - /orders/1
        """);

    [Fact]
    public void Z9_a_second_BDDfy_call_in_one_test_lists_the_first_and_keeps_its_calls() => Reads("Z9", """
        0 Given a registered customer
          0.0 A customer is registered
        1 Then the order is confirmed
          1.0 The order is read back
        calls: 0 /customers, 1 /orders/1, - /customers, - /orders, - /orders/1
        """);

    [Fact]
    public void Z10_a_reflective_setup_and_teardown_list_no_row_and_their_calls_no_step() => Reads("Z10", """
        0 Given a registered customer
          0.0 A customer is registered
        1 When the customer places the order
          1.0 An order is submitted
        2 Then the order is confirmed
          2.0 The order is read back
        calls: - /seed, 0 /customers, 1 /orders, 2 /orders/1, - /clean
        """);

    [Fact]
    public void Z11_a_row_that_did_not_run_leaves_the_rows_that_ran_read_by_their_ids() => Reads("Z11", """
        0 Given a step whose title is longer than any bar draws it whole so its bar cannot be matched against its text by anyone reading the diagram later
        1 When the next step fails [Failed]
        2 Then the order is confirmed [SkippedAfterFailure]
        calls: 0 /long, 1 /fails
        """);

    [Fact]
    public void Z12_a_retried_test_lists_its_first_attempt_and_no_later_attempts_rows() => Reads("Z12", """
        0 Given a registered customer
          0.0 A customer is registered
        1 When a flaky step runs [Failed]
          1.0 An order is submitted
        2 Then the order is read back [SkippedAfterFailure]
        calls: 0 /customers, 1 /orders, - /customers, - /orders, - /orders/1
        """);

    [Theory]
    [InlineData("B0", "0 /customers, 1 /orders, 2 /orders/1")]
    [InlineData("B1", "0 /seed, 1 /customers, 2 /orders, 3 /orders/1")]
    [InlineData("B5", "0 /customers/alice, 1 /orders/1, - /customers/bob, - /orders/1")]
    [InlineData("B7", "0 /customers, 1 /orders/1, - /teardown-with")]
    [InlineData("Z8", "0 /customers/alice, - /customers/bob, - /orders/1")]
    [InlineData("Z10", "- /seed, 0 /customers, 1 /orders, 2 /orders/1, - /clean")]
    public void With_the_bars_hidden_a_call_keeps_what_it_reads_with_them(string scenario, string calls)
    {
        var read = EngineLane.Run("Engine.BDDfy", new Dictionary<string, string> { ["NO_BARS"] = "1" }, "bars hidden").Rows.Of(scenario);

        Assert.EndsWith("calls: " + calls, read);
    }

    [Fact]
    public void F1_a_file_in_a_teardown_the_report_does_not_list_stays_in_the_report() =>
        Assert.Equal(["teardown-file"], EngineLane.Run("Engine.BDDfy").Rows.AllFilesOf("F1"));

    [Fact]
    public void F2_a_file_in_a_later_row_of_an_example_table_stays_in_the_report() =>
        Assert.Equal(["row-alice", "row-bob"], EngineLane.Run("Engine.BDDfy").Rows.AllFilesOf("F2"));

    [Fact]
    public void F3_a_hidden_setups_files_stay_in_the_report_and_it_lists_no_row()
    {
        var rows = EngineLane.Run("Engine.BDDfy").Rows;

        Assert.Equal(["seed-file", "setup-file"], rows.AllFilesOf("F3"));
        Assert.StartsWith("0 Given a registered customer", rows.Of("F3"));
    }

    [Fact]
    public void F4_steps_cleared_after_the_run_leave_every_call_its_step() => Reads("F4", """
        0 Given a registered customer
        1 Then the order is confirmed
        calls: 0 /customers, 1 /orders/1
        """);

    [Fact]
    public void F5_a_row_a_consumer_rebuilt_stands_once_with_its_sub_step_and_nothing_comes_back() => Reads("F5", """
        0 Given a registered customer
          0.0 A customer is registered
        1 Then the order is ***
          1.0 The order is read back
        calls: 0 /customers, - /orders/1
        """);

    private const string AsyncOpening = """
        0 Given a registered customer
          0.0 A customer is registered

        """;

    private const string AsyncClosing = """

        2 Then the order is confirmed
          2.0 The order is read back

        """;

    private static ReportRows Rows => EngineLane.Run("Engine.BDDfy").Rows;

    // BDDfy's runner waits on what its executor returns, so an async step is open until its task ends, and what it opens after its first await is under it.
    [Fact]
    public void AN1_an_async_step_keeps_what_its_step_method_runs_after_a_call_that_suspends() => Reads("AN1", AsyncOpening + """
        1 When a facade runs two steps
          1.0 A facade that runs two steps
            1.0.0 Inner one
            1.0.1 Inner two
        """ + AsyncClosing + """
        calls: 0 /customers, 1 /facade, 1 /inner-1, 1 /inner-2, 2 /orders/1
        """);

    [Fact]
    public void AN2_an_async_step_keeps_an_assertion_made_after_a_call_that_suspends() => Reads("AN2", AsyncOpening + """
        1 When a facade asserts
          1.0 A facade that asserts
            1.0.0 Assert.Equal(2, 1 + 1)
        """ + AsyncClosing + """
        calls: 0 /customers, 1 /facade, 2 /orders/1
        """);

    [Fact]
    public void AN3_an_async_step_keeps_a_file_attached_after_a_call_that_suspends()
    {
        Assert.Equal(["facade-file"], Rows.AllFilesOf("AN3"));
        Assert.Empty(Rows.ScenarioFilesOf("AN3"));
    }

    [Fact]
    public void AN4_an_async_step_reads_failed_over_its_failed_inner_step_and_what_follows_did_not_run() => Reads("AN4", AsyncOpening + """
        1 When a facades inner step fails [Failed]
          1.0 A facade whose inner step fails [Failed]
            1.0.0 Inner that fails [Failed]
        2 Then the order is confirmed [SkippedAfterFailure]
        calls: 0 /customers, 1 /facade, 1 /inner-fails
        """);

    [Fact]
    public void AN7_an_async_step_keeps_what_runs_when_nothing_suspended_first() => Reads("AN7", AsyncOpening + """
        1 When a facade starts with its inner step
          1.0 A facade that starts with its inner step
            1.0.0 Inner one
            1.0.1 Inner two
        """ + AsyncClosing + """
        calls: 0 /customers, 1 /inner-1, 1 /inner-2, 2 /orders/1
        """);

    [Fact]
    public void AN8_the_last_step_keeps_what_its_step_method_runs() => Reads("AN8", AsyncOpening + """
        1 Then a facade runs two steps
          1.0 A facade that runs two steps
            1.0.0 Inner one
            1.0.1 Inner two
        calls: 0 /customers, 1 /facade, 1 /inner-1, 1 /inner-2
        """);

    [Fact]
    public void AN9_the_digest_names_an_assertion_that_fails_after_a_call_that_suspends() =>
        Assert.Contains("Assert.Equal(3, 1 + 1)", File.ReadAllText(Path.Combine(EngineLane.Run("Engine.BDDfy").ReportsDirectory!, "Failures.md")));

    [Fact]
    public void AN10_a_hidden_async_tear_down_lists_no_row_for_what_its_step_method_runs() => Reads("AN10", """
        0 Given a registered customer
          0.0 A customer is registered
        1 Then the order is confirmed
          1.0 The order is read back
        calls: 0 /customers, 1 /orders/1, - /facade, - /inner-1, - /inner-2
        """);

    [Fact]
    public void HS1_a_hidden_setup_that_throws_lists_its_files_once()
    {
        Assert.Equal(["seed-file", "setup-file"], Rows.AllFilesOf("HS1"));
        Assert.Empty(Rows.ScenarioFilesOf("HS1"));
    }

    [Fact]
    public void HS2_a_row_a_consumer_rebuilt_lists_its_file_once()
    {
        Assert.Equal(["rebuilt-row-file"], Rows.FilesOn("HS2", "1"));
        Assert.Empty(Rows.ScenarioFilesOf("HS2"));
    }

    [Fact]
    public void HS3_a_scenario_run_inside_a_step_of_another_keeps_its_rows_and_its_file()
    {
        Reads("HS3", """
            0 Given the inner step runs
              0.0 A customer is registered
            1 Then the inner step ends
              1.0 The order is read back
            calls: 0 /customers, 1 /orders/1, - /seed
            """);
        Assert.Equal(["inner-file"], Rows.FilesOn("HS3", "0"));
        Assert.Empty(Rows.ScenarioFilesOf("HS3"));
    }

    // BDDfy lists its steps by kind, and runs them as written: a list that is not in the order its steps ran says nothing about where what did not run belongs.
    [Fact]
    public void OR2_a_list_not_in_run_order_lists_each_row_once_and_what_did_not_run_last() => Reads("OR2", """
        0 Given the start
          0.0 A customer is registered
        1 When one
        2 Then one
        3 When two
        4 Then two fails [Failed]
        5 When three [SkippedAfterFailure]
        6 Then three [SkippedAfterFailure]
        calls: 0 /customers, 1 /w1, 2 /t1, 3 /w2, 4 /t2
        """);

    [Fact]
    public void HS4_a_file_on_a_hidden_step_past_the_last_row_stays_in_the_report_once()
    {
        Assert.Equal(["context-file", "second-setup-file"], Rows.AllFilesOf("HS4"));
        Assert.Equal(["second-setup-file"], Rows.ScenarioFilesOf("HS4"));
    }

    [Fact]
    public void OR3_a_step_that_did_not_run_between_two_that_did_stands_after_the_last_that_ran() => Reads("OR3", """
        0 Given the start
          0.0 A customer is registered
        1 When one
        2 Then one fails [Failed]
        3 When two [SkippedAfterFailure]
        4 Then two [SkippedAfterFailure]
        calls: 0 /customers
        """);
}
