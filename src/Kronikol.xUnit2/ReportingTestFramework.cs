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
/// Each test's result reaches its own scenario: the framework makes the scenario when xUnit starts the test and
/// pairs the result with it by the test itself, so every row of a theory and every <c>[Fact(DisplayName = …)]</c>
/// gets its own verdict, duration and calls. Tests <see cref="TestTrackingAttribute"/> applies to that are skipped,
/// or that fail before their first line runs (in the constructor, <c>InitializeAsync</c> or a fixture), are
/// reported too. The reports are written when the test assembly finishes, before the runner hears that it has.
/// </para>
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
/// The executor of <see cref="ReportingTestFramework"/>. It runs the assembly with Kronikol's results sink between
/// xUnit's message bus and the runner's sink, which pairs each test's result with its own scenario by the test's
/// identity, and writes the reports when the assembly finishes, before the runner hears that it has.
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
        using var resultsSink = new KronikolResultsSink(executionMessageSink, ReportLifecycle.GenerateReports);

        // Synchronous message reporting, on a copy of the runner's options, so the sink sees each test start on
        // the test's own flow and can hand its scenario to TestTrackingAttribute.Before.
        using var assemblyRunner = new XunitTestAssemblyRunner(
            TestAssembly, testCases, DiagnosticMessageSink, resultsSink, SynchronousReportingOptions.Over(executionOptions));
        assemblyRunner.RunAsync().GetAwaiter().GetResult();
    }
}

/// <summary>
/// Where Kronikol.xUnit2's reports take their options from, and the once-per-process report writing of
/// <see cref="ReportingTestFramework"/>.
/// </summary>
public static class ReportLifecycle
{
    private static int _reported;

    /// <summary>
    /// The <see cref="ReportConfigurationOptions"/> to use when generating reports.
    /// Set this from your test project (e.g. in a module initialiser or collection fixture)
    /// before tests run. If not set, a default configuration is used.
    /// </summary>
    public static ReportConfigurationOptions? Options { get; set; }

    /// <summary>
    /// Writes the reports for a run of <see cref="ReportingTestFramework"/>, once per process. The results sink
    /// that calls it writes any failure to <c>kronikol-error.log</c> beside the test assembly, so none reaches the
    /// test run.
    /// </summary>
    internal static void GenerateReports(IReadOnlyList<ScenarioInfo> scenarios, DateTime start, DateTime end)
    {
        if (Interlocked.Exchange(ref _reported, 1) != 0)
            return;

        WriteReports(scenarios, start, end);
    }

    /// <summary>Writes the reports for <paramref name="scenarios"/> with <see cref="Options"/>, or nothing for a run with none.</summary>
    internal static void WriteReports(IReadOnlyList<ScenarioInfo> scenarios, DateTime start, DateTime end)
    {
        if (scenarios.Count == 0)
            return;

        XUnit2ReportGenerator.Write(scenarios, start, end, Options ?? new ReportConfigurationOptions(),
            "xUnit sent no result for them (a cancelled run sends none)");
    }

    /// <summary>Appends <paramref name="exception"/> to <c>kronikol-error.log</c> beside the test assembly. Never throws.</summary>
    internal static void WriteErrorLog(Exception exception)
    {
        try
        {
            var errorPath = Path.Combine(AppContext.BaseDirectory, "kronikol-error.log");
            File.AppendAllText(errorPath, $"[{DateTime.UtcNow:O}] {exception}{Environment.NewLine}");
        }
        catch
        {
            // Nowhere left to say it: a message sink that throws stops the test run.
        }
    }
}
