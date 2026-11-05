namespace Kronikol.Tests.RealEngines;

/// <summary>
/// What Reqnroll's real engine does to a scenario's steps, each scenario read from the report of a run on every lane the
/// adapter ships for, and on the floor it asks for. A hook's step method, one left running, and a Gherkin step that runs another
/// are the ways into a list of steps and a list of what ran that disagree (plans/STEP_IDENTITY_PLAN.md).
/// </summary>
public class ReqnrollStepsTests
{
    public static TheoryData<string> Lanes => new() { "Engine.Reqnroll.xUnit2", "Engine.Reqnroll.xUnit3", "Engine.Reqnroll.TUnit", "Engine.Reqnroll.Floor" };

    private const string TheStepsAsWritten = """
        0 Given a registered customer
          0.0 A customer is registered
        1 When the customer places the order
          1.0 An order is submitted
        2 Then the order is confirmed
          2.0 The order is read back
        """;

    private const string AfterAHookStep = """
        1 Given a registered customer
          1.0 A customer is registered
        2 When the customer places the order
          2.0 An order is submitted
        3 Then the order is confirmed
          3.0 The order is read back
        """;

    private static void Reads(string lane, string scenario, string expected, params string[] withoutCalls) =>
        Assert.Equal(expected.ReplaceLineEndings("\n"), EngineLane.Run(lane).Rows.Of(scenario, 0, withoutCalls));

    [Theory, MemberData(nameof(Lanes))]
    public void T00_no_hook_is_the_control(string lane) => Reads(lane, "T00", TheStepsAsWritten + """

        calls: 0 /customers, 1 /orders, 2 /orders/1
        """);

    [Theory, MemberData(nameof(Lanes))]
    public void T01_a_step_method_in_BeforeScenario_stands_first_and_each_step_keeps_its_own(string lane) => Reads(lane, "T01", """
        0 Reference data is seeded

        """ + AfterAHookStep + """

        calls: 0 /seed, 1 /customers, 2 /orders, 3 /orders/1
        """);

    [Theory, MemberData(nameof(Lanes))]
    public void T02_a_step_method_in_BeforeScenarioBlock_stands_before_each_block(string lane) => Reads(lane, "T02", """
        0 Block prepared
        1 Given a registered customer
          1.0 A customer is registered
        2 Block prepared
        3 When the customer places the order
          3.0 An order is submitted
        4 Block prepared
        5 Then the order is confirmed
          5.0 The order is read back
        calls: 0 /block, 1 /customers, 2 /block, 3 /orders, 4 /block, 5 /orders/1
        """);

    [Theory, MemberData(nameof(Lanes))]
    public void T03_a_step_method_in_AfterScenarioBlock_stands_after_each_block(string lane) => Reads(lane, "T03", """
        0 Given a registered customer
          0.0 A customer is registered
        1 Block prepared
        2 When the customer places the order
          2.0 An order is submitted
        3 Block prepared
        4 Then the order is confirmed
          4.0 The order is read back
        5 Block prepared
        calls: 0 /customers, 1 /block, 2 /orders, 3 /block, 4 /orders/1, 5 /block
        """);

    [Theory, MemberData(nameof(Lanes))]
    public void T04_a_step_method_in_AfterScenario_stands_last(string lane) => Reads(lane, "T04", TheStepsAsWritten + """

        3 Reference data is cleaned
        calls: 0 /customers, 1 /orders, 2 /orders/1, 3 /clean
        """);

    [Theory, MemberData(nameof(Lanes))]
    public void T05_a_step_method_that_fails_in_BeforeScenario_is_the_failed_row_with_what_did_not_run_after_it(string lane) => Reads(lane, "T05", """
        0 Seeding fails [Failed]
        1 Given a registered customer [SkippedAfterFailure]
        2 When the customer places the order [SkippedAfterFailure]
        3 Then the order is confirmed [SkippedAfterFailure]
        calls: 0 /seed
        """);

    [Theory, MemberData(nameof(Lanes))]
    public void T05_the_digest_names_the_hook_step_as_the_failing_step_and_lists_its_call(string lane)
    {
        var digest = File.ReadAllText(Path.Combine(EngineLane.Run(lane).ReportsDirectory!, "Failures.md"));
        var start = digest.IndexOf("T05 a step method that fails", digest.IndexOf("## Failures", StringComparison.Ordinal), StringComparison.Ordinal);
        var end = digest.IndexOf("\n### ", start, StringComparison.Ordinal);
        var section = digest[start..(end < 0 ? digest.Length : end)];

        Assert.Matches(@"\*\*Failing step\*\* — `s\d+/0` Seeding fails", section);
        Assert.Contains("Calls in the failing step", section);
        Assert.Contains("`POST /seed`", section);
    }

    [Theory, MemberData(nameof(Lanes))]
    public void T06_an_AfterStep_hook_that_throws_leaves_the_failed_step_listed(string lane) => Reads(lane, "T06", """
        0 Given a registered customer [Failed]
          0.0 A customer is registered
        calls: 0 /customers
        """);

    [Theory, MemberData(nameof(Lanes))]
    public void T07_a_BeforeStep_hook_that_throws_is_the_failed_row_with_what_did_not_run_after_it(string lane) => Reads(lane, "T07", """
        0 Given a registered customer [Failed]
        1 When the customer places the order [SkippedAfterFailure]
        2 Then the order is confirmed [SkippedAfterFailure]
        calls:
        """.TrimEnd() + " ");

    [Theory, MemberData(nameof(Lanes))]
    public void T08_a_failing_step_lists_the_step_after_it(string lane) => Reads(lane, "T08", """
        0 Given a registered customer
          0.0 A customer is registered
        1 When the payment fails [Failed]
        2 Then the order is confirmed [SkippedAfterFailure]
        calls: 0 /customers, 1 /payments
        """);

    [Theory, MemberData(nameof(Lanes))]
    public void T29_a_step_with_no_frame_takes_no_hook_steps_row(string lane) => Assert.StartsWith("""
        0 Reference data is seeded
        1 Given a registered customer [Failed]
        """.ReplaceLineEndings("\n"), EngineLane.Run(lane).Rows.Of("T29"));

    [Theory, MemberData(nameof(Lanes))]
    public void T09_a_plain_call_in_a_hook_reads_no_step(string lane) => Reads(lane, "T09", TheStepsAsWritten + """

        calls: - /seed, 0 /customers, 1 /orders, 2 /orders/1
        """);

    [Theory, MemberData(nameof(Lanes))]
    public void T10_a_step_method_in_BeforeStep_nests_under_the_step_it_found_open(string lane) => Reads(lane, "T10", """
        0 Given a registered customer
          0.0 Step observed
          0.1 A customer is registered
        1 When the customer places the order
          1.0 Step observed
          1.1 An order is submitted
        2 Then the order is confirmed
          2.0 Step observed
          2.1 The order is read back
        calls: 0 /observe, 0 /customers, 1 /observe, 1 /orders, 2 /observe, 2 /orders/1
        """);

    [Theory, MemberData(nameof(Lanes))]
    public void T11_a_Given_step_method_in_BeforeScenario_keeps_its_keyword(string lane) => Reads(lane, "T11", """
        0 Given A tenant exists

        """ + AfterAHookStep + """

        calls: 0 /tenant, 1 /customers, 2 /orders, 3 /orders/1
        """);

    [Theory, MemberData(nameof(Lanes))]
    public void T12_a_step_method_in_AfterStep_nests_under_the_step_it_found_open(string lane) => Reads(lane, "T12", """
        0 Given a registered customer
          0.0 A customer is registered
          0.1 Step observed
        1 When the customer places the order
          1.0 An order is submitted
          1.1 Step observed
        2 Then the order is confirmed
          2.0 The order is read back
          2.1 Step observed
        calls: 0 /customers, 0 /observe, 1 /orders, 1 /observe, 2 /orders/1, 2 /observe
        """);

    [Theory, MemberData(nameof(Lanes))]
    public void T13_a_hook_step_and_a_failing_step_leave_the_failing_step_its_own_call(string lane) => Reads(lane, "T13", """
        0 Reference data is seeded
        1 Given a registered customer
          1.0 A customer is registered
        2 When the payment fails [Failed]
        3 Then the order is confirmed [SkippedAfterFailure]
        calls: 0 /seed, 1 /customers, 2 /payments
        """);

    [Theory, MemberData(nameof(Lanes))]
    public void T18_an_undefined_step_and_the_step_after_it_are_listed(string lane) => Reads(lane, "T18", """
        0 Given a registered customer
          0.0 A customer is registered
        1 When nobody has written this step [Skipped]
        2 Then the order is confirmed [Skipped]
        calls: 0 /customers
        """);

    [Theory, MemberData(nameof(Lanes))]
    public void T14_an_attachment_and_a_plain_call_in_a_hook_stay_on_the_scenario(string lane) => Reads(lane, "T14", TheStepsAsWritten + """

        calls: - /seed, 0 /customers, 1 /orders, 2 /orders/1
        """);

    [Theory, MemberData(nameof(Lanes))]
    public void T15_an_AfterScenario_hook_that_throws_leaves_the_scenario_in_the_report(string lane) => Reads(lane, "T15", TheStepsAsWritten + """

        calls: 0 /customers, 1 /orders, 2 /orders/1
        """);

    [Theory, MemberData(nameof(Lanes))]
    public void T16_a_hook_ahead_of_the_scenarios_identity_records_nothing(string lane) => Reads(lane, "T16", TheStepsAsWritten + """

        calls: 0 /customers, 1 /orders, 2 /orders/1
        """);

    [Theory, MemberData(nameof(Lanes))]
    public void T17_a_step_method_left_running_leaves_every_later_step_its_own(string lane) => Reads(lane, "T17", """
        0 Given a step method is left running
          0.0 A slow background check
        1 When the customer places the order
          1.0 An order is submitted
        2 Then the order is confirmed
          2.0 The order is read back
        calls: 0 /started, 1 /orders, 2 /orders/1
        """, "/background");

    [Theory, MemberData(nameof(Lanes))]
    public void T30_two_step_methods_at_once_in_a_hook_each_stand_first_and_each_call_reads_its_own(string lane)
    {
        var read = EngineLane.Run(lane).Rows.Of("T30");

        Assert.Contains("0 First parallel", read);
        Assert.Contains("1 Second parallel", read);
        Assert.Contains("2 Given a registered customer", read);
        Assert.Contains("3 When the customer places the order", read);
        Assert.Contains("0 /first", read);
        Assert.Contains("1 /second", read);
        Assert.Contains("2 /customers", read);
        Assert.Contains("3 /orders", read);
    }

    [Theory, MemberData(nameof(Lanes))]
    public void T31_a_step_opened_by_hand_in_an_async_hook_is_closed_by_its_test_id_with_its_failure(string lane) =>
        Assert.Contains("A step opened by hand [Failed]", EngineLane.Run(lane).Rows.Of("T31"), StringComparison.OrdinalIgnoreCase);

    [Theory, MemberData(nameof(Lanes))]
    public void T19_two_step_methods_in_parallel_read_under_their_step(string lane)
    {
        Reads(lane, "T19", """
            0 Given two step methods run in parallel
              0.0 First parallel
              0.1 Second parallel
            1 When the customer places the order
              1.0 An order is submitted
            2 Then the order is confirmed
              2.0 The order is read back
            calls: 1 /orders, 2 /orders/1
            """, "/first", "/second");
        var read = EngineLane.Run(lane).Rows.Of("T19");
        Assert.Contains("0 /first", read);
        Assert.Contains("0 /second", read);
    }

    [Theory, MemberData(nameof(Lanes))]
    public void T20_a_plain_call_in_AfterScenario_stays_under_the_last_step(string lane) => Reads(lane, "T20", TheStepsAsWritten + """

        calls: 0 /customers, 1 /orders, 2 /orders/1, 2 /teardown
        """);

    [Theory, MemberData(nameof(Lanes))]
    public void T21_a_step_that_runs_another_lists_it_under_itself(string lane) => Reads(lane, "T21", """
        0 Given a step that runs another step
          0.0 Given a registered customer
            0.0.0 A customer is registered
        1 When the customer places the order
          1.0 An order is submitted
        2 Then the order is confirmed
          2.0 The order is read back
        calls: 0 /customers, 0 /outer, 1 /orders, 2 /orders/1
        """);

    [Theory, MemberData(nameof(Lanes))]
    public void T22_a_block_hook_that_throws_leaves_what_ran(string lane) => Reads(lane, "T22", """
        0 Given a registered customer
          0.0 A customer is registered
        calls: 0 /customers
        """);

    [Theory, MemberData(nameof(Lanes))]
    public void T23_two_step_methods_left_running_keep_their_place(string lane) => Reads(lane, "T23", """
        0 Given two step methods are left running
          0.0 A quick check
          0.1 A slow background check
        1 When the order takes a while
          1.0 An order is submitted slowly
        2 Then the order is confirmed
          2.0 The order is read back
        calls: 0 /started, 1 /orders, 2 /orders/1
        """, "/background", "/quick");

    [Theory, MemberData(nameof(Lanes))]
    public void T24_a_step_method_a_hook_left_running_keeps_its_row(string lane) => Reads(lane, "T24", """
        0 A slow background check
        1 Given a registered customer
          1.0 A customer is registered
        2 When the order takes a while
          2.0 An order is submitted slowly
        3 Then the order is confirmed
          3.0 The order is read back
        calls: 1 /customers, 2 /orders, 3 /orders/1
        """, "/background");

    [Theory, MemberData(nameof(Lanes))]
    public void T25_a_nested_step_carries_its_table_and_doc_string(string lane) => Reads(lane, "T25", """"
        0 Given a step that runs steps with arguments
          0.0 Given the basket is listed {table sku count APPLE 3}
          0.1 Given the note "kept" says """a doc string"""
        1 When the customer places the order
          1.0 An order is submitted
        2 Then the order is confirmed
          2.0 The order is read back
        calls: 0 /basket, 0 /note, 1 /orders, 2 /orders/1
        """");

    [Theory, MemberData(nameof(Lanes))]
    public void T26_a_step_over_a_failed_inner_step_reads_failed_and_its_skipped_inner_step_stays_under_it(string lane) => Reads(lane, "T26", """
        0 Given a step that runs two steps, the first failing [Failed]
          0.0 Given an inner step fails [Failed]
          0.1 Given a registered customer [SkippedAfterFailure]
        1 When the customer places the order [SkippedAfterFailure]
        2 Then the order is confirmed [SkippedAfterFailure]
        calls: 0 /inner
        """);

    [Theory, MemberData(nameof(Lanes))]
    public void T27_a_hook_that_runs_a_step_lists_it_as_a_step(string lane) => Reads(lane, "T27", """
        0 Given a registered customer
          0.0 A customer is registered

        """ + AfterAHookStep + """

        calls: 0 /customers, 1 /customers, 2 /orders, 3 /orders/1
        """);

    [Theory, MemberData(nameof(Lanes))]
    public void E01_an_outlines_rows_each_have_the_hook_step(string lane)
    {
        var rows = EngineLane.Run(lane).Rows;
        foreach (var (occurrence, amount) in new[] { (0, 10), (1, 20) })
        {
            Assert.Equal($"""
                0 Reference data is seeded
                1 Given a registered customer
                  1.0 A customer is registered
                2 When an order of {amount} is placed
                3 Then the order is confirmed
                  3.0 The order is read back
                calls: 0 /seed, 1 /customers, 2 /orders/{amount}, 3 /orders/1
                """.ReplaceLineEndings("\n"), rows.Of("E01", occurrence));
        }
    }

    [Theory, MemberData(nameof(Lanes))]
    public void E02_a_hook_step_on_one_row_leaves_the_other_row_as_it_was(string lane)
    {
        var rows = EngineLane.Run(lane).Rows;
        Assert.Equal("""
            0 Given a registered customer
              0.0 A customer is registered
            1 When an order of 10 is placed
            2 Then the order is confirmed
              2.0 The order is read back
            calls: 0 /customers, 1 /orders/10, 2 /orders/1
            """.ReplaceLineEndings("\n"), rows.Of("E02", 0));
        Assert.Equal("""
            0 Reference data is seeded
            1 Given a registered customer
              1.0 A customer is registered
            2 When an order of 20 is placed
            3 Then the order is confirmed
              3.0 The order is read back
            calls: 0 /seed, 1 /customers, 2 /orders/20, 3 /orders/1
            """.ReplaceLineEndings("\n"), rows.Of("E02", 1));
    }

    [Theory, MemberData(nameof(Lanes))]
    public void E06_a_step_method_in_an_argument_transformation_stands_before_its_step(string lane) => Reads(lane, "E06", """
        0 Given a registered customer
          0.0 A customer is registered
        1 Reference data is seeded
        2 When a transformed value of 7 is used
        3 Then the order is confirmed
          3.0 The order is read back
        calls: 0 /customers, 1 /seed, 2 /used/7, 3 /orders/1
        """);

    [Theory, MemberData(nameof(Lanes))]
    public void E09_a_step_method_in_a_hook_at_Kronikols_own_order_stands_last(string lane) => Reads(lane, "E09", """
        0 Given a registered customer
          0.0 A customer is registered
        1 Then the order is confirmed
          1.0 The order is read back
        2 Reference data is cleaned
        calls: 0 /customers, 1 /orders/1, 2 /clean
        """);

    [Theory, MemberData(nameof(Lanes))]
    public void T28_the_public_stopwatch_key_stays_on_the_scenario(string lane) => Reads(lane, "T28", """
        0 Given a step that runs another step
          0.0 Given a registered customer
            0.0.0 A customer is registered
        1 When the customer places the order
          1.0 An order is submitted
        calls: 0 /customers, 0 /outer, 1 /orders
        """);

    [Theory]
    [InlineData("T00", "0 /customers, 1 /orders, 2 /orders/1")]
    [InlineData("T01", "0 /seed, 1 /customers, 2 /orders, 3 /orders/1")]
    [InlineData("T09", "- /seed, 0 /customers, 1 /orders, 2 /orders/1")]
    [InlineData("T20", "0 /customers, 1 /orders, 2 /orders/1, 2 /teardown")]
    public void With_the_bars_hidden_every_call_inside_a_step_keeps_it(string scenario, string calls)
    {
        var read = EngineLane.Run("Engine.Reqnroll.xUnit2", new Dictionary<string, string> { ["NO_BARS"] = "1" }, "bars hidden").Rows.Of(scenario);

        Assert.EndsWith("calls: " + calls, read);
    }

    [Theory, MemberData(nameof(Lanes))]
    public void E03_a_hook_step_in_a_rule_with_a_Background_stands_after_it(string lane) => Reads(lane, "E03 ", """
        b0 Given a registered customer
          b0.0 A customer is registered
        0 Reference data is seeded
        1 When an order of 30 is placed
        2 Then the order is confirmed
          2.0 The order is read back
        calls: 0 /seed, b0 /customers, 1 /orders/30, 2 /orders/1
        """);

    [Theory, MemberData(nameof(Lanes))]
    public void E03b_its_sibling_with_no_hook_keeps_its_addresses(string lane) => Reads(lane, "E03b", """
        b0 Given a registered customer
          b0.0 A customer is registered
        0 When an order of 31 is placed
        1 Then the order is confirmed
          1.0 The order is read back
        calls: b0 /customers, 0 /orders/31, 1 /orders/1
        """);

    [Theory, MemberData(nameof(Lanes))]
    public void E16_a_Background_step_that_runs_another_counts_as_one_row(string lane) => Reads(lane, "E16 ", """
        b0 Given a step that runs another
          b0.0 Given a registered customer
            b0.0.0 A customer is registered
        b1 And a registered customer
          b1.0 A customer is registered
        0 Reference data is seeded
        1 When an order of 40 is placed
        2 Then the order is confirmed
          2.0 The order is read back
        calls: 0 /seed, b0 /customers, b1 /customers, 1 /orders/40, 2 /orders/1
        """);

    [Theory, MemberData(nameof(Lanes))]
    public void E16b_its_sibling_with_no_hook_keeps_its_addresses(string lane) => Reads(lane, "E16b", """
        b0 Given a step that runs another
          b0.0 Given a registered customer
            b0.0.0 A customer is registered
        b1 And a registered customer
          b1.0 A customer is registered
        0 When an order of 41 is placed
        1 Then the order is confirmed
          1.0 The order is read back
        calls: b0 /customers, b1 /customers, 0 /orders/41, 1 /orders/1
        """);

    [Fact]
    public void X2_a_hook_step_method_that_skips_the_test_reads_skipped_not_failed() =>
        Assert.StartsWith("0 Skips itself [Skipped]\n", EngineLane.Run("Engine.Reqnroll.xUnit3").Rows.Of("X2"));

    [Theory, MemberData(nameof(Lanes))]
    public void Q4_a_step_with_no_frame_stands_after_a_block_hooks_step_method_that_ran_ahead_of_it(string lane) =>
        Assert.StartsWith("""
            0 Block prepared
            1 Given a registered customer
              1.0 A customer is registered
            2 Block prepared
            3 When the customer places the order [Failed]
            """.ReplaceLineEndings("\n"), EngineLane.Run(lane).Rows.Of("Q4"));

    [Theory, MemberData(nameof(Lanes))]
    public void E07_a_BeforeStep_hook_that_throws_on_the_second_step_is_that_steps_failure(string lane) => Reads(lane, "E07", """
        0 Given a registered customer
          0.0 A customer is registered
        1 When an order of 70 is placed [Failed]
        2 Then the order is confirmed [SkippedAfterFailure]
        calls: 0 /customers
        """);

    [Theory, MemberData(nameof(Lanes))]
    public void E08_a_pending_step_and_the_step_after_it_are_listed(string lane) => Reads(lane, "E08", """
        0 Given a registered customer
          0.0 A customer is registered
        1 When a pending step runs [Skipped]
        2 Then the order is confirmed [Skipped]
        calls: 0 /customers
        """);

    [Theory, MemberData(nameof(Lanes))]
    public void E21_a_row_that_fails_groups_its_values_as_a_row_that_passes(string lane)
    {
        var rows = EngineLane.Run(lane).Rows;

        Assert.DoesNotContain("sku", rows.ExampleValueNames("E21", 1));
        Assert.Equal(rows.ExampleValueNames("E21", 0), rows.ExampleValueNames("E21", 1));
    }

    [Fact]
    public void N1_a_step_method_a_hook_caught_blames_no_skip_on_itself() => Reads("Engine.Reqnroll.xUnit3", "N1", """
        0 Fails in a hook [Failed]
        1 Given a registered customer
          1.0 A customer is registered
        2 When a step skips itself [Skipped]
        3 Then the order is confirmed [Skipped]
        calls: 1 /customers
        """);

    [Fact]
    public void N2_a_failure_after_a_skip_blames_no_skip_on_itself() => Reads("Engine.Reqnroll.xUnit3", "N2", """
        0 Given a registered customer
          0.0 A customer is registered
        1 When a step skips itself [Skipped]
        2 Then the order is confirmed [Skipped]
        3 Fails in a hook [Failed]
        calls: 0 /customers
        """);

    [Theory, MemberData(nameof(Lanes))]
    public void N3_what_a_plain_hooks_failure_stopped_reads_skipped_after_it(string lane) => Reads(lane, "N3", """
        0 Given a registered customer [SkippedAfterFailure]
        1 When the customer places the order [SkippedAfterFailure]
        calls:
        """.TrimEnd() + " ");

    [Theory, MemberData(nameof(Lanes))]
    public void N4_an_ambiguous_step_after_a_failure_did_not_run(string lane) => Reads(lane, "N4", """
        0 When the payment fails [Failed]
        1 When this step is ambiguous [SkippedAfterFailure]
        2 Then the order is confirmed [SkippedAfterFailure]
        calls: 0 /payments
        """);

    [Theory, MemberData(nameof(Lanes))]
    public void N5_an_ambiguous_step_is_the_failure(string lane) => Reads(lane, "N5", """
        0 Given a registered customer
          0.0 A customer is registered
        1 When this step is ambiguous [Failed]
        2 Then the order is confirmed [SkippedAfterFailure]
        calls: 0 /customers
        """);

    [Theory, MemberData(nameof(Lanes))]
    public void N6_an_inner_step_a_hook_stopped_keeps_its_place_among_what_ran_there(string lane) => Reads(lane, "N6", """
        0 Given a step that runs an inner step a hook stops, then a step method [Failed]
          0.0 Given an inner step a hook stops [Failed]
          0.1 A customer is registered
        1 Then the order is confirmed [SkippedAfterFailure]
        calls: 0 /customers
        """);
}
