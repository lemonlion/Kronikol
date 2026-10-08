using Kronikol.NUnit4;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace Kronikol.Tests.NUnit4;

/// <summary>
/// What an NUnit result gives its scenario besides the verdict (plans/ADAPTER_CAPTURE_GAPS_PLAN.md R1). Measured at 4.11.0:
/// every passing scenario carried an empty error message, and a test that called <c>Assert.Pass</c>, <c>Assert.Ignore</c> or
/// <c>Assert.Inconclusive</c> with a reason carried it and a stack trace, so a passed or skipped scenario read as a failure.
/// </summary>
public class ScenarioOutcomeTests
{
    [Test]
    public void A_failed_scenario_carries_the_message_and_the_stack_trace()
    {
        Assert.That(ScenarioOutcome.Message(TestStatus.Failed, "Expected: 3 But was: 5"), Is.EqualTo("Expected: 3 But was: 5"));
        Assert.That(ScenarioOutcome.StackTrace(TestStatus.Failed, "   at Orders.Place()"), Is.EqualTo("   at Orders.Place()"));
    }

    [TestCase(TestStatus.Passed)]
    [TestCase(TestStatus.Skipped)]
    [TestCase(TestStatus.Inconclusive)]
    public void A_scenario_that_did_not_fail_carries_no_message_and_no_stack_trace(TestStatus status)
    {
        // Assert.Pass, Assert.Ignore and Assert.Inconclusive put their reason in the result's message.
        Assert.That(ScenarioOutcome.Message(status, "a reason"), Is.Null);
        Assert.That(ScenarioOutcome.StackTrace(status, "   at Orders.Place()"), Is.Null);
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void A_failure_with_a_blank_message_carries_none(string? text)
    {
        Assert.That(ScenarioOutcome.Message(TestStatus.Failed, text), Is.Null);
        Assert.That(ScenarioOutcome.StackTrace(TestStatus.Failed, text), Is.Null);
    }

    [Test]
    public void A_warning_keeps_its_text()
    {
        // Assert.Warn: Warning maps to Passed, and its text is the only trace of the warning the report has. How a warning
        // should show is the plan's Q5; until the owner answers, the text stays where it was.
        Assert.That(ScenarioOutcome.Message(TestStatus.Warning, "check the stock"), Is.EqualTo("check the stock"));
        Assert.That(ScenarioOutcome.StackTrace(TestStatus.Warning, "   at Orders.Place()"), Is.EqualTo("   at Orders.Place()"));
    }
}
