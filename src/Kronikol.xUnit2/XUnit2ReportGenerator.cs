using Kronikol.Reports;

namespace Kronikol.xUnit2;

/// <summary>
/// Generates test tracking reports from xUnit v2 test scenario execution data.
/// </summary>
public static class XUnit2ReportGenerator
{
    /// <summary>
    /// Writes the reports for every scenario <see cref="TestTrackingAttribute"/> has made in this process, as the
    /// wiki's collection-fixture alternative does from its fixture's <c>Dispose</c>.
    /// </summary>
    /// <remarks>
    /// xUnit v2 shows a test's result only to the test framework, so on this path no scenario has one: each is
    /// reported as <see cref="ExecutionResult.Passed"/> and marked as a default, not a verdict, with a
    /// <see cref="DiagnosticKind.ResultDefaulted"/> diagnostic, and the specifications are written blank.
    /// <see cref="ReportingTestFramework"/> writes the reports with each test's own result.
    /// </remarks>
    public static void CreateStandardReportsWithDiagrams(DateTime startRunTime, DateTime endRunTime, ReportConfigurationOptions options)
    {
        var scenarios = XUnit2TestTrackingContext.GetAllScenarios();
        foreach (var scenario in scenarios.Where(s => !s.HasResult))
            scenario.ResultDefaulted = true;

        Write(scenarios, startRunTime, endRunTime, options,
            "xUnit v2 shows a test's result only to the test framework, and these reports were written without "
            + "Kronikol's (ReportingTestFramework)");
    }

    public static void CreateStandardReportsWithDiagrams(IEnumerable<ScenarioInfo> scenarios, DateTime startRunTime, DateTime endRunTime, ReportConfigurationOptions options)
    {
        Write(scenarios.ToArray(), startRunTime, endRunTime, options, "no result was recorded for them");
    }

    /// <summary>
    /// Writes the reports, with a <see cref="DiagnosticKind.ResultDefaulted"/> diagnostic when any scenario's result
    /// is a default, saying <paramref name="whyDefaulted"/>.
    /// </summary>
    internal static void Write(IReadOnlyList<ScenarioInfo> scenarios, DateTime startRunTime, DateTime endRunTime,
        ReportConfigurationOptions options, string whyDefaulted)
    {
        // The generator records into the collector in scope, or scopes one of its own when there is none.
        var collector = ReportDiagnosticsScope.Current is null ? new ReportDiagnosticsCollector() : null;
        using var scope = collector is null ? null : ReportDiagnosticsScope.Begin(collector);

        var defaulted = scenarios.Where(s => s.ResultDefaulted).ToArray();
        if (defaulted.Length > 0)
            ReportDiagnosticsScope.Record(DiagnosticKind.ResultDefaulted,
                $"{defaulted.Length} scenario(s) recorded no result and were reported as {ExecutionResult.Passed}: "
                + $"{whyDefaulted}. First: {string.Join(", ", defaulted.Take(3).Select(s => s.ScenarioName))}.");

        ReportGenerator.CreateStandardReportsWithDiagrams(scenarios.ToFeatures(), startRunTime, endRunTime, options);
    }
}
