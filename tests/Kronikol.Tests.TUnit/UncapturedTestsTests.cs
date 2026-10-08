using Kronikol.TUnit;

namespace Kronikol.Tests.TUnit;

/// <summary>
/// The tests a run's <c>[After(Test)]</c> never sees reach the report from the assembly hook's list of every test
/// (plans/ADAPTER_CAPTURE_GAPS_PLAN.md R2). Measured at 4.11.0 with the TUnit template: a test skipped by attribute and a
/// test whose class's constructor threw were missing from the report; <c>AssemblyHookContext.AllTests</c>, in the
/// <c>[After(Assembly)]</c> hook where the report is written, held them with their results.
/// </summary>
public class UncapturedTestsTests
{
    private sealed class TrackedTests : DiagrammedComponentTest;

    private sealed class UntrackedTests;

    private sealed record Test(string Id, Type? ClassType, bool HasResult);

    private static IReadOnlyList<Test> Select(IEnumerable<Test> all, params string[] captured) =>
        UncapturedTests.Select(all, t => new UncapturedTests.Candidate(t.Id, t.ClassType, t.HasResult), captured);

    [Fact]
    public void A_test_of_a_tracked_class_that_was_not_captured_is_found()
    {
        var skipped = new Test("skipped-by-attribute", typeof(TrackedTests), HasResult: true);
        var constructorThrew = new Test("constructor-threw", typeof(TrackedTests), HasResult: true);

        Assert.Equal([skipped, constructorThrew], Select([skipped, constructorThrew]));
    }

    [Fact]
    public void A_test_the_hook_captured_is_not_found_again()
    {
        var captured = new Test("captured", typeof(TrackedTests), HasResult: true);

        Assert.Empty(Select([captured], "captured"));
    }

    [Fact]
    public void A_test_of_a_class_that_is_not_tracked_is_left_out()
    {
        // A project may hold tests that do not derive from DiagrammedComponentTest; they never reached the report and must not now.
        Assert.Empty(Select([new Test("plain", typeof(UntrackedTests), HasResult: true), new Test("unknown", null, HasResult: true)]));
    }

    [Fact]
    public void A_test_with_no_result_is_left_out()
    {
        // A test the run did not execute, such as one a filter left out, has no result to report.
        Assert.Empty(Select([new Test("not-run", typeof(TrackedTests), HasResult: false)]));
    }

    [Fact]
    public void Without_the_hook_s_list_nothing_is_found()
    {
        Assert.Empty(UncapturedTests.In(null, []));
    }
}
