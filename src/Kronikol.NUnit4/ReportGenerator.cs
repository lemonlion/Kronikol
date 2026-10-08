using NUnit.Framework;
using Kronikol.Reports;

namespace Kronikol.NUnit4;

/// <summary>
/// Generates test tracking reports from NUnit test execution contexts.
/// </summary>
public static class NUnitReportGenerator
{
    public static void CreateStandardReportsWithDiagrams(IEnumerable<TestContext> testContexts, DateTime startRunTime, DateTime endRunTime, ReportConfigurationOptions options)
    {
        // The tests no tear-down captured (one ignored by attribute, the tests of a fixture that failed to start) come from
        // the run's result tree, which NUnit holds in the set-up fixture's [OneTimeTearDown], where the report is written.
        var contexts = testContexts.ToList();
        contexts.AddRange(UncapturedTests.In(UncapturedTests.RunningSuiteResult(), contexts.Select(c => c.Test.ID)));
        ReportGenerator.CreateStandardReportsWithDiagrams(contexts.ToFeatures(), startRunTime, endRunTime, options);
    }
}