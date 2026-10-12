using NUnit.Framework;
using Kronikol.Reports;

namespace Kronikol.NUnit4;

/// <summary>
/// Generates test tracking reports from NUnit test execution contexts.
/// </summary>
public static class NUnitReportGenerator
{
    /// <summary>
    /// Writes the reports for the tests given. Called from the set-up fixture's <c>[OneTimeTearDown]</c> with the run's whole
    /// <see cref="DiagrammedTestRun.TestContexts"/>, the report also holds each test of a tracked class that
    /// <c>[TearDown]</c> never captured (one skipped by attribute, one whose class failed to start), read from the run.
    /// Called with part of the run, or anywhere else, it holds the tests given.
    /// </summary>
    public static void CreateStandardReportsWithDiagrams(IEnumerable<TestContext> testContexts, DateTime startRunTime, DateTime endRunTime, ReportConfigurationOptions options)
    {
        // The tests no tear-down captured (one ignored by attribute, the tests of a fixture that failed to start) come from
        // the run's result tree, which NUnit holds in the set-up fixture's [OneTimeTearDown], where the report is written.
        var contexts = testContexts.ToList();
        var passed = contexts.Select(c => c.Test.ID).ToList();
        if (UncapturedTests.IsTheWholeRun(DiagrammedTestRun.TestContexts.Select(c => c.Test.ID), passed))
            contexts.AddRange(UncapturedTests.In(UncapturedTests.RunningSuiteResult(), passed));
        ReportGenerator.CreateStandardReportsWithDiagrams(contexts.ToFeatures(), startRunTime, endRunTime, options);
    }
}