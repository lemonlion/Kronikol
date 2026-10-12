using Kronikol.Reports;
using TUnit.Core;

namespace Kronikol.TUnit;

/// <summary>
/// Generates test tracking reports from TUnit test execution contexts.
/// </summary>
public static class TUnitReportGenerator
{
    /// <summary>
    /// Writes the reports for the tests given. Called from the <c>[After(Assembly)]</c> hook with the run's whole
    /// <see cref="DiagrammedTestRun.TestContexts"/>, the report also holds each test of a tracked class that
    /// <c>[After(Test)]</c> never captured (one skipped by attribute, one whose class failed to start), read from the run.
    /// Called with part of the run, or anywhere else, it holds the tests given.
    /// </summary>
    public static void CreateStandardReportsWithDiagrams(IEnumerable<TestContext> testContexts, DateTime startRunTime, DateTime endRunTime, ReportConfigurationOptions options)
    {
        // The tests no [After(Test)] captured (one skipped by attribute, one whose class's constructor threw) come from the
        // assembly hook's list of every test, which TUnit holds in the [After(Assembly)] hook, where the report is written.
        var contexts = testContexts.ToList();
        var passed = contexts.Select(c => c.Id).ToList();
        if (UncapturedTests.IsTheWholeRun(DiagrammedTestRun.TestContexts.Select(c => c.Id), passed))
            contexts.AddRange(UncapturedTests.In(AssemblyHookContext.Current?.AllTests, passed));
        ReportGenerator.CreateStandardReportsWithDiagrams(contexts.ToFeatures(), startRunTime, endRunTime, options);
    }
}