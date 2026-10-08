using Kronikol.Reports;
using Kronikol.TUnit;

namespace Kronikol.Tests.TUnit;

/// <summary>
/// What a TUnit result gives its scenario besides the verdict (plans/ADAPTER_CAPTURE_GAPS_PLAN.md R1). Measured at 4.11.0:
/// a test skipped with <c>Skip.Test</c> carried its skip reason as an error message, and a test whose
/// <c>[Before(Test)]</c> hook threw had a duration below zero.
/// </summary>
public class ScenarioOutcomeTests
{
    private static Exception Thrown(string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    [Fact]
    public void A_failed_scenario_carries_the_exceptions_message_and_stack_trace()
    {
        var exception = Thrown("the order was not placed");

        Assert.Equal("the order was not placed", ScenarioOutcome.Message(ExecutionResult.Failed, exception));
        Assert.Equal(exception.StackTrace, ScenarioOutcome.StackTrace(ExecutionResult.Failed, exception));
    }

    [Theory]
    [InlineData(ExecutionResult.Skipped)]
    [InlineData(ExecutionResult.Passed)]
    public void A_scenario_that_did_not_fail_carries_no_message_even_when_an_exception_came_with_it(ExecutionResult result)
    {
        // Skip.Test reports the skip reason on an exception; it is not a failure.
        var exception = Thrown("dynamic skip reason");

        Assert.Null(ScenarioOutcome.Message(result, exception));
        Assert.Null(ScenarioOutcome.StackTrace(result, exception));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_failure_with_a_blank_message_carries_none(string message)
    {
        Assert.Null(ScenarioOutcome.Message(ExecutionResult.Failed, new InvalidOperationException(message)));
    }

    [Fact]
    public void A_failure_without_an_exception_carries_no_message()
    {
        Assert.Null(ScenarioOutcome.Message(ExecutionResult.Failed, null));
        Assert.Null(ScenarioOutcome.StackTrace(ExecutionResult.Failed, null));
    }

    private static readonly DateTimeOffset Started = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_duration_below_zero_is_no_duration()
    {
        Assert.Null(ScenarioOutcome.Duration(TimeSpan.FromTicks(-8), Started));
    }

    [Fact]
    public void A_duration_of_zero_or_more_is_kept_and_none_stays_none()
    {
        Assert.Equal(TimeSpan.Zero, ScenarioOutcome.Duration(TimeSpan.Zero, Started));
        Assert.Equal(TimeSpan.FromMilliseconds(250), ScenarioOutcome.Duration(TimeSpan.FromMilliseconds(250), Started));
        Assert.Null(ScenarioOutcome.Duration(null, Started));
    }

    [Fact]
    public void A_test_that_never_started_has_no_duration()
    {
        // R2: a test whose class's constructor threw has no start, and TUnit measured its duration from the start of time
        // (739,896 days on the probe).
        Assert.Null(ScenarioOutcome.Duration(TimeSpan.FromDays(739896), start: null));
    }
}
