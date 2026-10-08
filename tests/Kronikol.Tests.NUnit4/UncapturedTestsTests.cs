using Kronikol.NUnit4;
using Kronikol.Reports;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;

namespace Kronikol.Tests.NUnit4;

/// <summary>
/// The tests a run's tear-down never sees reach the report from the run's result tree (plans/ADAPTER_CAPTURE_GAPS_PLAN.md
/// R2). Measured at 4.11.0 with the NUnit template: a statically ignored test, and the tests of a fixture whose constructor
/// or <c>[OneTimeSetUp]</c> threw, were missing from the report, since <see cref="DiagrammedComponentTest"/> captures a test
/// in its <c>[TearDown]</c>, which those never reach; the run's result tree, in the set-up fixture's
/// <c>[OneTimeTearDown]</c>, holds them all.
/// </summary>
public class UncapturedTestsTests
{
    private sealed class TrackedFixture : DiagrammedComponentTest
    {
        public void Ignored() { }

        public void Throws() { }

        public void Captured() { }
    }

    private sealed class UntrackedFixture
    {
        public void Plain() { }
    }

    private static (TestSuite Fixture, TestSuiteResult FixtureResult) Fixture<T>()
    {
        var fixture = new TestFixture(new TypeWrapper(typeof(T)));
        return (fixture, (TestSuiteResult)fixture.MakeTestResult());
    }

    private static TestMethod Method<T>(TestSuite fixture, string name)
    {
        var method = new TestMethod(new MethodWrapper(typeof(T), name), fixture);
        fixture.Add(method);
        return method;
    }

    private static TestResult Result(TestMethod test, ResultState state, string? message = null)
    {
        var result = test.MakeTestResult();
        result.SetResult(state, message, message is null ? null : "   at the probe");
        return result;
    }

    private static TestSuiteResult Run(params TestSuiteResult[] fixtures)
    {
        var run = new TestSuite("Run");
        var result = (TestSuiteResult)run.MakeTestResult();
        foreach (var fixture in fixtures)
            result.AddResult(fixture);
        return result;
    }

    [Test]
    public void A_statically_ignored_test_and_one_whose_fixture_failed_to_start_are_found()
    {
        var (fixture, fixtureResult) = Fixture<TrackedFixture>();
        var ignored = Method<TrackedFixture>(fixture, nameof(TrackedFixture.Ignored));
        var throws = Method<TrackedFixture>(fixture, nameof(TrackedFixture.Throws));
        fixtureResult.AddResult(Result(ignored, ResultState.Ignored, "static ignore reason"));
        fixtureResult.AddResult(Result(throws, ResultState.Error.WithSite(FailureSite.Parent), "OneTimeSetUp: probe: the constructor threw"));

        var found = UncapturedTests.In(Run(fixtureResult), capturedIds: []);

        Assert.That(found.Select(c => c.Test.ID), Is.EquivalentTo(new[] { ignored.Id, throws.Id }));
    }

    [Test]
    public void A_test_the_tear_down_captured_is_not_found_again()
    {
        var (fixture, fixtureResult) = Fixture<TrackedFixture>();
        var captured = Method<TrackedFixture>(fixture, nameof(TrackedFixture.Captured));
        fixtureResult.AddResult(Result(captured, ResultState.Success));

        Assert.That(UncapturedTests.In(Run(fixtureResult), capturedIds: [captured.Id]), Is.Empty);
    }

    [Test]
    public void A_test_of_a_fixture_that_is_not_tracked_is_left_out()
    {
        // A project may hold tests that do not derive from DiagrammedComponentTest; they never reached the report and must not now.
        var (fixture, fixtureResult) = Fixture<UntrackedFixture>();
        var plain = Method<UntrackedFixture>(fixture, nameof(UntrackedFixture.Plain));
        fixtureResult.AddResult(Result(plain, ResultState.Ignored, "not tracked"));

        Assert.That(UncapturedTests.In(Run(fixtureResult), capturedIds: []), Is.Empty);
    }

    [Test]
    public void Without_a_result_tree_nothing_is_found()
    {
        Assert.That(UncapturedTests.In(null, capturedIds: []), Is.Empty);
    }

    /// <summary>
    /// The result tree is <c>NUnit.Framework.Internal</c>'s, which NUnit does not promise to keep, and the facts above build
    /// it by hand. That the set-up fixture's <c>[OneTimeTearDown]</c> sees the whole run in it was measured on NUnit 4.6.0
    /// (plans/ADAPTER_CAPTURE_GAPS_PLAN.harness/r2/nunit4-before-after.txt); before this pin moves, run the probe again.
    /// </summary>
    [Test]
    public void The_result_tree_is_read_on_the_NUnit_version_it_was_measured_on()
    {
        Assert.That(typeof(TestExecutionContext).Assembly.GetName().Version, Is.EqualTo(new Version(4, 6, 0, 0)));
    }

    [Test]
    public void A_found_test_becomes_a_scenario_with_its_verdict_and_only_a_failure_carries_a_message()
    {
        var (fixture, fixtureResult) = Fixture<TrackedFixture>();
        var ignored = Method<TrackedFixture>(fixture, nameof(TrackedFixture.Ignored));
        var throws = Method<TrackedFixture>(fixture, nameof(TrackedFixture.Throws));
        fixtureResult.AddResult(Result(ignored, ResultState.Ignored, "static ignore reason"));
        fixtureResult.AddResult(Result(throws, ResultState.Error.WithSite(FailureSite.Parent), "OneTimeSetUp: probe: the constructor threw"));

        var scenarios = UncapturedTests.In(Run(fixtureResult), capturedIds: []).ToFeatures().SelectMany(f => f.Scenarios).ToArray();

        var skipped = scenarios.Single(s => s.Id == ignored.Id);
        Assert.That(skipped.Result, Is.EqualTo(ExecutionResult.Skipped));
        Assert.That(skipped.ErrorMessage, Is.Null);
        var failed = scenarios.Single(s => s.Id == throws.Id);
        Assert.That(failed.Result, Is.EqualTo(ExecutionResult.Failed));
        Assert.That(failed.ErrorMessage, Is.EqualTo("OneTimeSetUp: probe: the constructor threw"));
    }
}
