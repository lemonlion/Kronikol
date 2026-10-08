using Kronikol.Reports;
using TUnit.Core;

namespace Kronikol.TUnit;

/// <summary>
/// Generates test tracking reports from TUnit test execution contexts.
/// </summary>
public static class TUnitReportGenerator
{
    public static void CreateStandardReportsWithDiagrams(IEnumerable<TestContext> testContexts, DateTime startRunTime, DateTime endRunTime, ReportConfigurationOptions options)
    {
        // The tests no [After(Test)] captured (one skipped by attribute, one whose class's constructor threw) come from the
        // assembly hook's list of every test, which TUnit holds in the [After(Assembly)] hook, where the report is written.
        var contexts = testContexts.ToList();
        contexts.AddRange(UncapturedTests.In(AssemblyHookContext.Current?.AllTests, contexts.Select(c => c.Id)));
        ReportGenerator.CreateStandardReportsWithDiagrams(contexts.ToFeatures(), startRunTime, endRunTime, options);
    }
}