using Kronikol.Tracking;
using Kronikol.Reports;
using Kronikol.TabularAttributes;

namespace Kronikol.Tests.Tracking;

[Collection("DiagramsFetcher")]
public class StepCollectorTests
{
    [Fact]
    public void StartStep_and_CompleteStep_records_single_passed_step()
    {
        var testId = "test-1";
        StepCollector.StartStep(testId, "Given", "A user exists", null, null);
        StepCollector.CompleteStep(testId, passed: true);

        var steps = StepCollector.GetSteps(testId);
        Assert.Single(steps);
        Assert.Equal("Given", steps[0].Keyword);
        Assert.Equal("A user exists", steps[0].Text);
        Assert.Equal(ExecutionResult.Passed, steps[0].Status);
        Assert.NotNull(steps[0].Duration);
    }

    [Fact]
    public void StartStep_and_CompleteStep_records_failed_step()
    {
        var testId = "test-2";
        StepCollector.StartStep(testId, "When", "The user logs in", null, null);
        StepCollector.CompleteStep(testId, passed: false, errorMessage: "Login failed");

        var steps = StepCollector.GetSteps(testId);
        Assert.Single(steps);
        Assert.Equal("When", steps[0].Keyword);
        Assert.Equal(ExecutionResult.Failed, steps[0].Status);
    }

    [Fact]
    public void Multiple_steps_recorded_in_order()
    {
        var testId = "test-3";
        StepCollector.StartStep(testId, "Given", "A user exists", null, null);
        StepCollector.CompleteStep(testId, passed: true);
        StepCollector.StartStep(testId, "When", "The user logs in", null, null);
        StepCollector.CompleteStep(testId, passed: true);
        StepCollector.StartStep(testId, "Then", "The user is logged in", null, null);
        StepCollector.CompleteStep(testId, passed: true);

        var steps = StepCollector.GetSteps(testId);
        Assert.Equal(3, steps.Length);
        Assert.Equal("Given", steps[0].Keyword);
        Assert.Equal("When", steps[1].Keyword);
        Assert.Equal("Then", steps[2].Keyword);
    }

    [Fact]
    public void Nested_steps_become_substeps()
    {
        var testId = "test-4";
        StepCollector.StartStep(testId, "Given", "A user exists", null, null);
        StepCollector.StartStep(testId, "Step", "Create user in DB", null, null);
        StepCollector.CompleteStep(testId, passed: true);
        StepCollector.CompleteStep(testId, passed: true);

        var steps = StepCollector.GetSteps(testId);
        Assert.Single(steps);
        Assert.Equal("Given", steps[0].Keyword);
        Assert.NotNull(steps[0].SubSteps);
        Assert.Single(steps[0].SubSteps!);
        Assert.Equal("Create user in DB", steps[0].SubSteps![0].Text);
    }

    [Fact]
    public void Step_with_null_keyword_has_no_keyword()
    {
        var testId = "test-5";
        StepCollector.StartStep(testId, null, "Do something", null, null);
        StepCollector.CompleteStep(testId, passed: true);

        var steps = StepCollector.GetSteps(testId);
        Assert.Single(steps);
        Assert.Null(steps[0].Keyword);
        Assert.Equal("Do something", steps[0].Text);
    }

    [Fact]
    public void GetSteps_returns_empty_for_unknown_testId()
    {
        var steps = StepCollector.GetSteps("nonexistent");
        Assert.Empty(steps);
    }

    [Fact]
    public void ClearSteps_removes_all_steps_for_test()
    {
        var testId = "test-6";
        StepCollector.StartStep(testId, "Given", "Something", null, null);
        StepCollector.CompleteStep(testId, passed: true);

        StepCollector.ClearSteps(testId);

        var steps = StepCollector.GetSteps(testId);
        Assert.Empty(steps);
    }

    [Fact]
    public void Step_with_parameters_records_them()
    {
        var testId = "test-7";
        var paramNames = new[] { "name", "age" };
        var paramValues = new object?[] { "John", 42 };
        StepCollector.StartStep(testId, "Given", "A user exists", paramNames, paramValues);
        StepCollector.CompleteStep(testId, passed: true);

        var steps = StepCollector.GetSteps(testId);
        Assert.Single(steps);
        Assert.NotNull(steps[0].Parameters);
        Assert.Equal(2, steps[0].Parameters!.Length);
        Assert.Equal("name", steps[0].Parameters![0].Name);
        Assert.Equal("John", steps[0].Parameters![0].InlineValue!.Value);
        Assert.Equal("age", steps[0].Parameters![1].Name);
        Assert.Equal("42", steps[0].Parameters![1].InlineValue!.Value);
    }

    [Fact]
    public void Keyword_sequencing_Given_And_And()
    {
        var testId = "test-8";
        StepCollector.StartStep(testId, "Given", "A user exists", null, null);
        StepCollector.CompleteStep(testId, passed: true);
        StepCollector.StartStep(testId, "Given", "The user is active", null, null);
        StepCollector.CompleteStep(testId, passed: true);
        StepCollector.StartStep(testId, "Given", "The user has a subscription", null, null);
        StepCollector.CompleteStep(testId, passed: true);

        var steps = StepCollector.GetSteps(testId);
        Assert.Equal(3, steps.Length);
        Assert.Equal("Given", steps[0].Keyword);
        Assert.Equal("And", steps[1].Keyword);
        Assert.Equal("And", steps[2].Keyword);
    }

    [Fact]
    public void Keyword_sequencing_resets_for_different_keyword_types()
    {
        var testId = "test-9";
        StepCollector.StartStep(testId, "Given", "First given", null, null);
        StepCollector.CompleteStep(testId, passed: true);
        StepCollector.StartStep(testId, "Given", "Second given", null, null);
        StepCollector.CompleteStep(testId, passed: true);
        StepCollector.StartStep(testId, "When", "First when", null, null);
        StepCollector.CompleteStep(testId, passed: true);
        StepCollector.StartStep(testId, "When", "Second when", null, null);
        StepCollector.CompleteStep(testId, passed: true);
        StepCollector.StartStep(testId, "Then", "First then", null, null);
        StepCollector.CompleteStep(testId, passed: true);

        var steps = StepCollector.GetSteps(testId);
        Assert.Equal("Given", steps[0].Keyword);
        Assert.Equal("And", steps[1].Keyword);
        Assert.Equal("When", steps[2].Keyword);
        Assert.Equal("And", steps[3].Keyword);
        Assert.Equal("Then", steps[4].Keyword);
    }

    [Fact]
    public void Null_keyword_Step_does_not_affect_sequencing()
    {
        var testId = "test-10";
        StepCollector.StartStep(testId, "Given", "First given", null, null);
        StepCollector.CompleteStep(testId, passed: true);
        StepCollector.StartStep(testId, null, "A plain step", null, null);
        StepCollector.CompleteStep(testId, passed: true);
        StepCollector.StartStep(testId, "Given", "Second given", null, null);
        StepCollector.CompleteStep(testId, passed: true);

        var steps = StepCollector.GetSteps(testId);
        Assert.Equal("Given", steps[0].Keyword);
        Assert.Null(steps[1].Keyword);
        Assert.Equal("And", steps[2].Keyword); // Still "And" because previous Given was first
    }

    [Fact]
    public void StartStep_with_null_testId_is_noop()
    {
        // Should not throw
        StepCollector.StartStep(null, "Given", "Something", null, null);
        StepCollector.CompleteStep(null, passed: true);
    }

    [Fact]
    public void CompleteStep_with_no_active_step_is_noop()
    {
        // Should not throw
        StepCollector.CompleteStep("no-active-steps", passed: true);
    }

    [Fact]
    public void Step_duration_is_positive()
    {
        var testId = "test-11";
        StepCollector.StartStep(testId, "Given", "Something", null, null);
        // Small delay to ensure duration > 0
        Thread.Sleep(1);
        StepCollector.CompleteStep(testId, passed: true);

        var steps = StepCollector.GetSteps(testId);
        Assert.True(steps[0].Duration!.Value.Ticks > 0);
    }

    [Fact]
    public void AddAssertionSubStep_adds_to_active_step()
    {
        var testId = "test-12";
        StepCollector.StartStep(testId, "Then", "Verify result", null, null);
        StepCollector.AddAssertionSubStep(testId, "result.Should().Be(42)", passed: true);
        StepCollector.AddAssertionSubStep(testId, "result.Should().BePositive()", passed: true);
        StepCollector.CompleteStep(testId, passed: true);

        var steps = StepCollector.GetSteps(testId);
        Assert.Single(steps);
        Assert.NotNull(steps[0].SubSteps);
        Assert.Equal(2, steps[0].SubSteps!.Length);
        Assert.Equal("result.Should().Be(42)", steps[0].SubSteps![0].Text);
        Assert.Equal(ExecutionResult.Passed, steps[0].SubSteps![0].Status);
    }

    [Fact]
    public void AddAssertionSubStep_failed_recorded_correctly()
    {
        var testId = "test-13";
        StepCollector.StartStep(testId, "Then", "Verify result", null, null);
        StepCollector.AddAssertionSubStep(testId, "result.Should().Be(42)", passed: false);
        StepCollector.CompleteStep(testId, passed: false);

        var steps = StepCollector.GetSteps(testId);
        Assert.Single(steps);
        Assert.NotNull(steps[0].SubSteps);
        Assert.Equal("result.Should().Be(42)", steps[0].SubSteps![0].Text);
        Assert.Equal(ExecutionResult.Failed, steps[0].SubSteps![0].Status);
    }

    [Fact]
    public void HasActiveStep_returns_true_when_step_in_progress()
    {
        var testId = "test-14";
        Assert.False(StepCollector.HasActiveStep(testId));

        StepCollector.StartStep(testId, "Given", "Something", null, null);
        Assert.True(StepCollector.HasActiveStep(testId));

        StepCollector.CompleteStep(testId, passed: true);
        Assert.False(StepCollector.HasActiveStep(testId));
    }

    [Fact]
    public void Track_AssertionPassed_adds_sub_step_when_step_active()
    {
        var testId = $"step-integration-{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(testId, testId);

        StepCollector.StartStep(testId, "Then", "The result is correct", null, null);
        Track.AssertionPassed("x == 42");
        StepCollector.CompleteStep(testId, passed: true);

        var steps = StepCollector.GetSteps(testId);
        Assert.Single(steps);
        Assert.NotNull(steps[0].SubSteps);
        Assert.Single(steps[0].SubSteps!);
        Assert.Equal("x == 42", steps[0].SubSteps![0].Text);
        Assert.Equal(ExecutionResult.Passed, steps[0].SubSteps![0].Status);
    }

    [Fact]
    public void Track_AssertionFailed_adds_failed_sub_step_when_step_active()
    {
        var testId = $"step-integration-{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(testId, testId);

        StepCollector.StartStep(testId, "Then", "The result is correct", null, null);
        Track.AssertionFailed("x == 42", "Expected 42 but got 0");
        StepCollector.CompleteStep(testId, passed: false);

        var steps = StepCollector.GetSteps(testId);
        Assert.Single(steps);
        Assert.NotNull(steps[0].SubSteps);
        Assert.Single(steps[0].SubSteps!);
        Assert.Equal("x == 42", steps[0].SubSteps![0].Text);
        Assert.Equal(ExecutionResult.Failed, steps[0].SubSteps![0].Status);
    }

    [Fact]
    public void Track_AssertionPassed_does_not_add_sub_step_when_no_active_step()
    {
        var testId = $"step-integration-{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(testId, testId);

        // No step started — assertions should NOT appear in StepCollector
        Track.AssertionPassed("y == 10");

        var steps = StepCollector.GetSteps(testId);
        Assert.Empty(steps);
    }

    [Fact]
    public void StartStep_with_Given_sets_phase_to_Setup()
    {
        var testId = $"phase-{Guid.NewGuid():N}";
        TestPhaseContext.Reset();

        StepCollector.StartStep(testId, "Given", "A precondition", null, null);
        Assert.Equal(TestPhase.Setup, TestPhaseContext.Current);

        StepCollector.CompleteStep(testId, passed: true);
        StepCollector.ClearSteps(testId);
        TestPhaseContext.Reset();
    }

    [Fact]
    public void StartStep_with_When_sets_phase_to_Action()
    {
        var testId = $"phase-{Guid.NewGuid():N}";
        TestPhaseContext.Reset();

        StepCollector.StartStep(testId, "When", "An action occurs", null, null);
        Assert.Equal(TestPhase.Action, TestPhaseContext.Current);

        StepCollector.CompleteStep(testId, passed: true);
        StepCollector.ClearSteps(testId);
        TestPhaseContext.Reset();
    }

    [Fact]
    public void StartStep_with_Then_keeps_phase_as_Action()
    {
        var testId = $"phase-{Guid.NewGuid():N}";
        TestPhaseContext.Current = TestPhase.Action;

        StepCollector.StartStep(testId, "Then", "The result is X", null, null);
        Assert.Equal(TestPhase.Action, TestPhaseContext.Current);

        StepCollector.CompleteStep(testId, passed: true);
        StepCollector.ClearSteps(testId);
        TestPhaseContext.Reset();
    }

    [Fact]
    public void StartStep_phase_transition_respects_WhenTriggersAction_option()
    {
        var testId = $"phase-{Guid.NewGuid():N}";
        TestPhaseContext.Reset();
        var original = StepCollector.Options;

        try
        {
            StepCollector.Options = new StepTrackingOptions { WhenTriggersAction = false };
            StepCollector.StartStep(testId, "When", "An action", null, null);
            // Should NOT change phase when option is disabled
            Assert.Equal(TestPhase.Unknown, TestPhaseContext.Current);
        }
        finally
        {
            StepCollector.Options = original;
            StepCollector.CompleteStep(testId, passed: true);
            StepCollector.ClearSteps(testId);
            TestPhaseContext.Reset();
        }
    }

    [Fact]
    public void StartStep_with_But_sets_phase_to_Setup()
    {
        var testId = $"phase-but-{Guid.NewGuid():N}";
        TestPhaseContext.Reset();

        StepCollector.StartStep(testId, "But", "A negative precondition", null, null);
        Assert.Equal(TestPhase.Setup, TestPhaseContext.Current);

        StepCollector.CompleteStep(testId, passed: true);
        StepCollector.ClearSteps(testId);
        TestPhaseContext.Reset();
    }

    [Fact]
    public void StartStep_with_ButWhen_sets_phase_to_Action()
    {
        var testId = $"phase-butwhen-{Guid.NewGuid():N}";
        TestPhaseContext.Reset();

        StepCollector.StartStep(testId, "ButWhen", "An alternative action", null, null);
        Assert.Equal(TestPhase.Action, TestPhaseContext.Current);

        StepCollector.CompleteStep(testId, passed: true);
        StepCollector.ClearSteps(testId);
        TestPhaseContext.Reset();
    }

    [Fact]
    public void StartStep_with_ButWhen_displays_But_keyword()
    {
        var testId = $"butwhen-display-{Guid.NewGuid():N}";

        StepCollector.StartStep(testId, "ButWhen", "An alternative action", null, null);
        StepCollector.CompleteStep(testId, passed: true);

        var steps = StepCollector.GetSteps(testId);
        Assert.Single(steps);
        Assert.Equal("But", steps[0].Keyword);
        Assert.Equal("An alternative action", steps[0].Text);

        StepCollector.ClearSteps(testId);
    }

    [Fact]
    public void StartStep_with_But_then_ButWhen_sequences_to_And()
    {
        var testId = $"butwhen-seq-{Guid.NewGuid():N}";

        StepCollector.StartStep(testId, "But", "A precondition", null, null);
        StepCollector.CompleteStep(testId, passed: true);
        StepCollector.StartStep(testId, "ButWhen", "An alternative action", null, null);
        StepCollector.CompleteStep(testId, passed: true);

        var steps = StepCollector.GetSteps(testId);
        Assert.Equal(2, steps.Length);
        Assert.Equal("But", steps[0].Keyword);
        Assert.Equal("And", steps[1].Keyword);

        StepCollector.ClearSteps(testId);
    }

    [Fact]
    public void StartStep_self_resolving_uses_TestIdentityScope()
    {
        var testId = $"self-resolve-{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(testId, testId);

        // Call the overload without testId — it should resolve from ambient context
        StepCollector.StartStep("Given", "Something happens", null, null);
        StepCollector.CompleteStep(passed: true);

        var steps = StepCollector.GetSteps(testId);
        Assert.Single(steps);
        Assert.Equal("Given", steps[0].Keyword);
        Assert.Equal("Something happens", steps[0].Text);
    }

    [Fact]
    public void StartStep_emits_step_delimiter_for_top_level_step()
    {
        var testId = $"delim-{Guid.NewGuid():N}";

        StepCollector.StartStep(testId, "Given", "A user exists", null, null);
        StepCollector.CompleteStep(testId, passed: true);

        var logs = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId && l.PlantUml is not null && l.PlantUml.Contains("<<stepDelimiter>>"))
            .ToArray();

        Assert.Single(logs);
        Assert.Contains("Given A user exists", logs[0].PlantUml!);
        StepCollector.ClearSteps(testId);
    }

    [Fact]
    public void StartStep_does_not_emit_delimiter_for_sub_steps()
    {
        var testId = $"delim-sub-{Guid.NewGuid():N}";

        StepCollector.StartStep(testId, "Given", "A user exists", null, null);
        StepCollector.StartStep(testId, "Given", "A nested precondition", null, null);
        StepCollector.CompleteStep(testId, passed: true);
        StepCollector.CompleteStep(testId, passed: true);

        var logs = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId && l.PlantUml is not null && l.PlantUml.Contains("<<stepDelimiter>>"))
            .ToArray();

        // Only the top-level step should emit a delimiter, not the sub-step
        Assert.Single(logs);
        Assert.Contains("Given A user exists", logs[0].PlantUml!);
        StepCollector.ClearSteps(testId);
    }

    [Fact]
    public void StartStep_does_not_emit_delimiter_when_ShowStepDelimiters_is_false()
    {
        var testId = $"delim-off-{Guid.NewGuid():N}";
        var original = StepCollector.Options;

        try
        {
            StepCollector.Options = new StepTrackingOptions { ShowStepDelimiters = false };
            StepCollector.StartStep(testId, "Given", "A user exists", null, null);
            StepCollector.CompleteStep(testId, passed: true);

            var logs = RequestResponseLogger.RequestAndResponseLogs
                .Where(l => l.TestId == testId && l.PlantUml is not null && l.PlantUml.Contains("<<stepDelimiter>>"))
                .ToArray();

            Assert.Empty(logs);
        }
        finally
        {
            StepCollector.Options = original;
            StepCollector.ClearSteps(testId);
        }
    }

    [Fact]
    public void StartStep_delimiter_uses_effective_keyword_for_And()
    {
        var testId = $"delim-and-{Guid.NewGuid():N}";

        StepCollector.StartStep(testId, "Given", "A user exists", null, null);
        StepCollector.CompleteStep(testId, passed: true);
        StepCollector.StartStep(testId, "Given", "A product exists", null, null);
        StepCollector.CompleteStep(testId, passed: true);

        var logs = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == testId && l.PlantUml is not null && l.PlantUml.Contains("<<stepDelimiter>>"))
            .ToArray();

        Assert.Equal(2, logs.Length);
        Assert.Contains("Given A user exists", logs[0].PlantUml!);
        Assert.Contains("And A product exists", logs[1].PlantUml!);
        StepCollector.ClearSteps(testId);
    }

    [Fact]
    public void Step_with_ITabularParameterData_produces_Tabular_parameter()
    {
        var testId = $"tabular-{Guid.NewGuid():N}";
        var items = new[] { new TabularTestPerson { Name = "Alice", Age = 30 } };
        var inputs = new TabularInputs<TabularTestPerson>(items, ["Name", "Age"]);

        StepCollector.StartStep(testId, "Given", "test data",
            ["inputs"], [inputs]);
        StepCollector.CompleteStep(testId, passed: true);

        var steps = StepCollector.GetSteps(testId);
        Assert.Single(steps);
        Assert.NotNull(steps[0].Parameters);
        Assert.Single(steps[0].Parameters!);

        var param = steps[0].Parameters![0];
        Assert.Equal("inputs", param.Name);
        Assert.Equal(StepParameterKind.Tabular, param.Kind);
        Assert.NotNull(param.TabularValue);
        Assert.Equal(2, param.TabularValue!.Columns.Length);
        Assert.Equal("Name", param.TabularValue.Columns[0].Name);
        Assert.Equal("Age", param.TabularValue.Columns[1].Name);
        Assert.Single(param.TabularValue.Rows);
        Assert.Equal("Alice", param.TabularValue.Rows[0].Values[0].Value);
        Assert.Equal("30", param.TabularValue.Rows[0].Values[1].Value);

        StepCollector.ClearSteps(testId);
    }

    [Fact]
    public void Mixed_inline_and_tabular_parameters()
    {
        var testId = $"mixed-{Guid.NewGuid():N}";
        var items = new[] { new TabularTestPerson { Name = "Bob", Age = 25 } };
        var inputs = new TabularInputs<TabularTestPerson>(items, ["Name"]);

        StepCollector.StartStep(testId, "Given", "user with count",
            ["count", "inputs"], [3, inputs]);
        StepCollector.CompleteStep(testId, passed: true);

        var steps = StepCollector.GetSteps(testId);
        var parameters = steps[0].Parameters!;
        Assert.Equal(2, parameters.Length);

        Assert.Equal(StepParameterKind.Inline, parameters[0].Kind);
        Assert.Equal("3", parameters[0].InlineValue!.Value);

        Assert.Equal(StepParameterKind.Tabular, parameters[1].Kind);
        Assert.NotNull(parameters[1].TabularValue);

        StepCollector.ClearSteps(testId);
    }

    private class TabularTestPerson
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }

    private record ComplexTestData(string Name, int Temperature, string Flour);

    [Fact]
    public void Step_with_complex_object_param_truncates_to_type_name()
    {
        var testId = $"complex-param-{Guid.NewGuid():N}";
        var complexObj = new ComplexTestData("Classic", 180, "Plain Flour");
        // ToString() → "ComplexTestData { Name = Classic, Temperature = 180, Flour = Plain Flour }"

        StepCollector.StartStep(testId, "Given", "a muffin recipe",
            ["recipe"], [complexObj]);
        StepCollector.CompleteStep(testId, passed: true);

        var steps = StepCollector.GetSteps(testId);
        Assert.Single(steps);
        Assert.NotNull(steps[0].Parameters);
        var param = steps[0].Parameters![0];
        Assert.Equal("recipe", param.Name);
        Assert.Equal(StepParameterKind.Inline, param.Kind);
        // Should show truncated type name, not the full record ToString
        Assert.Equal("[ComplexTestData]", param.InlineValue!.Value);

        StepCollector.ClearSteps(testId);
    }

    [Fact]
    public void Step_with_scalar_param_keeps_full_value()
    {
        var testId = $"scalar-param-{Guid.NewGuid():N}";

        StepCollector.StartStep(testId, "Given", "a count",
            ["count"], [42]);
        StepCollector.CompleteStep(testId, passed: true);

        var steps = StepCollector.GetSteps(testId);
        Assert.Single(steps);
        var param = steps[0].Parameters![0];
        Assert.Equal("42", param.InlineValue!.Value);

        StepCollector.ClearSteps(testId);
    }

    [Fact]
    public void AddAssertionSubStep_skipped_when_IncludeTrackedAssertionsInStepList_is_false()
    {
        var testId = $"no-assertions-{Guid.NewGuid():N}";
        var original = StepCollector.Options;

        try
        {
            StepCollector.Options = new StepTrackingOptions { IncludeTrackedAssertionsInStepList = false };
            StepCollector.StartStep(testId, "Then", "Verify result", null, null);
            StepCollector.AddAssertionSubStep(testId, "result.Should().Be(42)", passed: true);
            StepCollector.AddAssertionSubStep(testId, "result.Should().BePositive()", passed: true);
            StepCollector.CompleteStep(testId, passed: true);

            var steps = StepCollector.GetSteps(testId);
            Assert.Single(steps);
            Assert.Null(steps[0].SubSteps); // No assertion sub-steps recorded
        }
        finally
        {
            StepCollector.Options = original;
            StepCollector.ClearSteps(testId);
        }
    }

    [Fact]
    public void BypassStep_records_step_as_bypassed()
    {
        var testId = $"bypass-{Guid.NewGuid():N}";
        StepCollector.StartStep(testId, "When", "Payment is submitted", null, null);
        StepCollector.BypassStep(testId, "Gateway down");

        var steps = StepCollector.GetSteps(testId);
        Assert.Single(steps);
        Assert.Equal(ExecutionResult.Bypassed, steps[0].Status);
        Assert.Equal("Gateway down", steps[0].BypassReason);
        Assert.Equal("When", steps[0].Keyword);
        Assert.Equal("Payment is submitted", steps[0].Text);

        StepCollector.ClearSteps(testId);
    }

    [Fact]
    public void BypassStep_with_null_reason_records_bypassed_without_reason()
    {
        var testId = $"bypass-no-reason-{Guid.NewGuid():N}";
        StepCollector.StartStep(testId, "Given", "External service ready", null, null);
        StepCollector.BypassStep(testId, null);

        var steps = StepCollector.GetSteps(testId);
        Assert.Single(steps);
        Assert.Equal(ExecutionResult.Bypassed, steps[0].Status);
        Assert.Null(steps[0].BypassReason);

        StepCollector.ClearSteps(testId);
    }

    [Fact]
    public void BypassStep_does_not_affect_subsequent_steps()
    {
        var testId = $"bypass-subsequent-{Guid.NewGuid():N}";
        StepCollector.StartStep(testId, "Given", "A precondition", null, null);
        StepCollector.CompleteStep(testId, passed: true);
        StepCollector.StartStep(testId, "When", "Bypassed action", null, null);
        StepCollector.BypassStep(testId, "Not applicable");
        StepCollector.StartStep(testId, "Then", "Result is checked", null, null);
        StepCollector.CompleteStep(testId, passed: true);

        var steps = StepCollector.GetSteps(testId);
        Assert.Equal(3, steps.Length);
        Assert.Equal(ExecutionResult.Passed, steps[0].Status);
        Assert.Equal(ExecutionResult.Bypassed, steps[1].Status);
        Assert.Equal("Not applicable", steps[1].BypassReason);
        Assert.Equal(ExecutionResult.Passed, steps[2].Status);

        StepCollector.ClearSteps(testId);
    }

    [Fact]
    public void BypassStep_with_null_testId_is_noop()
    {
        // Should not throw
        StepCollector.BypassStep(null, "reason");
    }

    [Fact]
    public void BypassStep_with_no_active_step_is_noop()
    {
        // Should not throw
        StepCollector.BypassStep("no-active-steps-bypass", "reason");
    }

    [Fact]
    public void BypassStep_nested_records_as_substep()
    {
        var testId = $"bypass-nested-{Guid.NewGuid():N}";
        StepCollector.StartStep(testId, "Given", "Outer step", null, null);
        StepCollector.StartStep(testId, "Given", "Inner step", null, null);
        StepCollector.BypassStep(testId, "Skipped inner");
        StepCollector.CompleteStep(testId, passed: true);

        var steps = StepCollector.GetSteps(testId);
        Assert.Single(steps);
        Assert.Equal(ExecutionResult.Passed, steps[0].Status);
        Assert.NotNull(steps[0].SubSteps);
        Assert.Single(steps[0].SubSteps!);
        Assert.Equal(ExecutionResult.Bypassed, steps[0].SubSteps![0].Status);
        Assert.Equal("Skipped inner", steps[0].SubSteps![0].BypassReason);

        StepCollector.ClearSteps(testId);
    }

    [Fact]
    public void BypassStep_self_resolving_uses_TestIdentityScope()
    {
        var testId = $"bypass-self-{Guid.NewGuid():N}";
        using var scope = TestIdentityScope.Begin(testId, testId);

        StepCollector.StartStep("When", "An action", null, null);
        StepCollector.BypassStep("Feature disabled");

        var steps = StepCollector.GetSteps(testId);
        Assert.Single(steps);
        Assert.Equal(ExecutionResult.Bypassed, steps[0].Status);
        Assert.Equal("Feature disabled", steps[0].BypassReason);

        StepCollector.ClearSteps(testId);
    }

    [Fact]
    public void BypassStep_has_duration()
    {
        var testId = $"bypass-duration-{Guid.NewGuid():N}";
        StepCollector.StartStep(testId, "Given", "Something", null, null);
        Thread.Sleep(1);
        StepCollector.BypassStep(testId, "reason");

        var steps = StepCollector.GetSteps(testId);
        Assert.NotNull(steps[0].Duration);
        Assert.True(steps[0].Duration!.Value.Ticks > 0);

        StepCollector.ClearSteps(testId);
    }
}
