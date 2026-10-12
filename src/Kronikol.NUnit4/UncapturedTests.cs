using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;

namespace Kronikol.NUnit4;

/// <summary>
/// The tests of a run that <see cref="DiagrammedComponentTest"/>'s <c>[TearDown]</c> never captured: a test ignored by
/// attribute, and the tests of a fixture whose constructor or <c>[OneTimeSetUp]</c> threw, which NUnit reports without
/// running a tear-down. Measured at 4.11.0 with the NUnit template, they were missing from the report while the runner
/// counted them. In the set-up fixture's <c>[OneTimeTearDown]</c>, where the report is written, the run's result tree holds
/// every test under it with its outcome, so each test of a tracked fixture that the tear-down did not capture is turned into
/// a <see cref="TestContext"/> the report reads like any other.
/// </summary>
/// <remarks>
/// The tree is <c>NUnit.Framework.Internal</c>'s, which NUnit does not promise to keep; with no tree (the report written
/// anywhere but a suite's one-time tear-down), the report has the captured tests alone, as before.
/// </remarks>
internal static class UncapturedTests
{
    public static IReadOnlyList<TestContext> In(ITestResult? root, IEnumerable<string> capturedIds)
    {
        if (root is null)
            return [];

        var captured = capturedIds.ToHashSet(StringComparer.Ordinal);
        var found = new List<TestContext>();
        Collect(root, captured, found);
        return found;
    }

    /// <summary>
    /// Whether the tests a caller passed hold every test the run captured: the run's own collection, or a copy of it. A
    /// caller that passes part of the run gets that part, as it did before these tests joined the report.
    /// </summary>
    public static bool IsTheWholeRun(IEnumerable<string> runIds, IEnumerable<string> passedIds)
    {
        var passed = passedIds.ToHashSet(StringComparer.Ordinal);
        return runIds.All(passed.Contains);
    }

    /// <summary>The result of the suite whose one-time tear-down is running, or null anywhere else.</summary>
    public static ITestResult? RunningSuiteResult()
    {
        try
        {
            return TestExecutionContext.CurrentContext.CurrentResult is { Test.IsSuite: true } result ? result : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void Collect(ITestResult result, HashSet<string> captured, List<TestContext> found)
    {
        if (result.Test.IsSuite)
        {
            foreach (var child in result.Children)
                Collect(child, captured, found);
            return;
        }

        // An [Explicit] test is skipped on every run that does not select it, by design, as TUnit leaves its own out.
        if (captured.Contains(result.Test.Id)
            || result.ResultState is { Status: TestStatus.Skipped, Label: "Explicit" }
            || result.Test.TypeInfo?.Type is not { } fixtureType
            || !typeof(DiagrammedComponentTest).IsAssignableFrom(fixtureType)
            || result.Test is not Test test
            || result is not TestResult testResult)
            return;

        found.Add(new TestContext(new TestExecutionContext { CurrentTest = test, CurrentResult = testResult }));
    }
}
