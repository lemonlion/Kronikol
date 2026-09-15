using System.Collections.Concurrent;
using System.Reflection;
using Kronikol.Reports;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Kronikol.xUnit2;

/// <summary>
/// Custom xUnit v2 test framework that generates Kronikol reports
/// after all tests complete but before the testhost process exits.
/// <para>
/// This is necessary because <c>Environment.Exit</c> (called by the testhost)
/// terminates foreground threads and gives <c>ProcessExit</c> only ~2 seconds,
/// which is insufficient for report generation.
/// </para>
/// <para>
/// To use, add the following to your test project (e.g. in <c>GlobalUsings.cs</c>):
/// <code>[assembly: TestFramework("Kronikol.xUnit2.ReportingTestFramework", "Kronikol.xUnit2")]</code>
/// </para>
/// </summary>
public class ReportingTestFramework : XunitTestFramework
{
    public ReportingTestFramework(IMessageSink messageSink) : base(messageSink) { }

    protected override ITestFrameworkExecutor CreateExecutor(AssemblyName assemblyName)
        => new ReportingTestFrameworkExecutor(assemblyName, SourceInformationProvider, DiagnosticMessageSink);
}

/// <summary>
/// Custom executor that wraps the execution message sink to capture test results,
/// then generates reports after all tests in the assembly have finished executing.
/// </summary>
public class ReportingTestFrameworkExecutor : XunitTestFrameworkExecutor
{
    public ReportingTestFrameworkExecutor(
        AssemblyName assemblyName,
        ISourceInformationProvider sourceInformationProvider,
        IMessageSink diagnosticMessageSink)
        : base(assemblyName, sourceInformationProvider, diagnosticMessageSink) { }

    protected override void RunTestCases(
        IEnumerable<IXunitTestCase> testCases,
        IMessageSink executionMessageSink,
        ITestFrameworkExecutionOptions executionOptions)
    {
        var resultSink = new TestResultCapturingSink(executionMessageSink);

        using (var assemblyRunner = new XunitTestAssemblyRunner(
            TestAssembly, testCases, DiagnosticMessageSink, resultSink, executionOptions))
        {
            assemblyRunner.RunAsync().GetAwaiter().GetResult();
        }

        // Update collected scenarios with actual test results
        resultSink.ApplyResults();

        // Generate reports
        ReportLifecycle.GenerateReports();
    }
}

/// <summary>
/// Wraps an <see cref="IMessageSink"/> to intercept test result messages
/// (<see cref="ITestFailed"/>, <see cref="ITestSkipped"/>) and record outcomes
/// so that <see cref="ScenarioInfo.Result"/> can be updated after execution.
/// </summary>
internal sealed class TestResultCapturingSink : IMessageSink
{
    private readonly IMessageSink _inner;

    internal readonly ConcurrentBag<TestOutcome> Outcomes = [];

    public TestResultCapturingSink(IMessageSink inner) => _inner = inner;

    public bool OnMessage(IMessageSinkMessage message)
    {
        switch (message)
        {
            case ITestPassed passed:
                Outcomes.Add(new TestOutcome
                {
                    DisplayName = passed.Test.DisplayName,
                    Result = ExecutionResult.Passed,
                    ExecutionTime = passed.ExecutionTime,
                    FinishedAt = DateTimeOffset.UtcNow,
                });
                break;

            case ITestFailed failed:
                Outcomes.Add(new TestOutcome
                {
                    DisplayName = failed.Test.DisplayName,
                    Result = ExecutionResult.Failed,
                    ErrorMessage = string.Join(Environment.NewLine, failed.Messages),
                    ErrorStackTrace = string.Join(Environment.NewLine, failed.StackTraces),
                    ExecutionTime = failed.ExecutionTime,
                    FinishedAt = DateTimeOffset.UtcNow,
                });
                break;

            case ITestSkipped skipped:
                Outcomes.Add(new TestOutcome
                {
                    DisplayName = skipped.Test.DisplayName,
                    Result = ExecutionResult.Skipped,
                    FinishedAt = DateTimeOffset.UtcNow,
                });
                break;
        }

        return _inner.OnMessage(message);
    }

    /// <summary>
    /// Matches captured test outcomes back to the <see cref="ScenarioInfo"/> entries
    /// collected by <see cref="TestTrackingAttribute"/>. Matching uses the
    /// <see cref="ScenarioInfo.MethodMatchKey"/> as a prefix of the xUnit display name.
    /// </summary>
    internal void ApplyResults()
    {
        var matchedTestIds = new HashSet<string>();

        foreach (var outcome in Outcomes)
        {
            // Find the matching scenario(s) by prefix match on the xUnit display name
            foreach (var (testId, scenario) in XUnit2TestTrackingContext.CollectedScenarios)
            {
                if (matchedTestIds.Contains(testId))
                    continue;

                if (outcome.DisplayName == scenario.MethodMatchKey ||
                    outcome.DisplayName.StartsWith(scenario.MethodMatchKey + "("))
                {
                    scenario.Result = outcome.Result;
                    scenario.ErrorMessage = outcome.ErrorMessage;
                    scenario.ErrorStackTrace = outcome.ErrorStackTrace;
                    scenario.Duration = outcome.ExecutionTime > 0
                        ? TimeSpan.FromSeconds((double)outcome.ExecutionTime)
                        : null;

                    scenario.EndedAt = outcome.FinishedAt;

                    // Update scenario name from the full xUnit display name so that
                    // [InlineData] / [Theory] test cases include their parameters
                    // (e.g. "Loads successfully [type: \"Purchase\"]") instead of
                    // all sharing the bare method name.
                    scenario.ScenarioName = ScenarioTitleResolver.FormatScenarioDisplayName(outcome.DisplayName);

                    matchedTestIds.Add(testId);
                    break;
                }
            }
        }
    }
}

internal record TestOutcome
{
    public required string DisplayName { get; init; }
    public required ExecutionResult Result { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorStackTrace { get; init; }
    public decimal ExecutionTime { get; init; }

    /// <summary>When the result message arrived, which is as close to the finish as xUnit v2 lets a sink get.</summary>
    public DateTimeOffset? FinishedAt { get; init; }
}

/// <summary>
/// Lifecycle helper that generates reports exactly once.
/// Called by <see cref="ReportingTestFrameworkExecutor"/> after all tests complete.
/// </summary>
public static class ReportLifecycle
{
    private static readonly DateTime StartTime = DateTime.UtcNow;
    private static int _reported;

    /// <summary>
    /// The <see cref="ReportConfigurationOptions"/> to use when generating reports.
    /// Set this from your test project (e.g. in a module initialiser or collection fixture)
    /// before tests run. If not set, a default configuration is used.
    /// </summary>
    public static ReportConfigurationOptions? Options { get; set; }

    internal static void GenerateReports()
    {
        if (Interlocked.Exchange(ref _reported, 1) != 0)
            return;

        try
        {
            var scenarios = XUnit2TestTrackingContext.GetAllScenarios();
            if (scenarios.Length == 0)
                return;

            var options = Options ?? new ReportConfigurationOptions();

            XUnit2ReportGenerator.CreateStandardReportsWithDiagrams(
                scenarios, StartTime, DateTime.UtcNow, options);
        }
        catch (Exception ex)
        {
            var errorPath = Path.Combine(AppContext.BaseDirectory, "kronikol-error.log");
            File.WriteAllText(errorPath, $"[{DateTime.UtcNow:O}] {ex}");
        }
    }
}
