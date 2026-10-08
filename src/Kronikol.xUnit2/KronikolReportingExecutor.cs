using Xunit;
using Xunit.Abstractions;

namespace Kronikol.xUnit2;

/// <summary>
/// Another framework's executor with Kronikol's results sink in front of the runner's sink
/// (<see cref="TestFrameworkExecutorExtensions.WithKronikolReporting"/>).
/// </summary>
/// <remarks>
/// Each run gets a sink of its own, which writes that run's reports when its test assembly finishes, so the
/// reports do not wait for <c>RunTests</c> to return: most frameworks' executors return before their tests have
/// run. Nothing is written when the executor is disposed, which xUnit does twice and also for a session that
/// only lists the tests.
/// </remarks>
internal sealed class KronikolReportingExecutor(ITestFrameworkExecutor inner) : LongLivedMarshalByRefObject, ITestFrameworkExecutor
{
    private int _disposed;

    public ITestCase Deserialize(string value) => inner.Deserialize(value);

    public void RunAll(IMessageSink executionMessageSink, ITestFrameworkDiscoveryOptions discoveryOptions, ITestFrameworkExecutionOptions executionOptions) =>
        inner.RunAll(ResultsSink(executionMessageSink), discoveryOptions, SynchronousReportingOptions.Over(executionOptions));

    public void RunTests(IEnumerable<ITestCase> testCases, IMessageSink executionMessageSink, ITestFrameworkExecutionOptions executionOptions) =>
        inner.RunTests(testCases, ResultsSink(executionMessageSink), SynchronousReportingOptions.Over(executionOptions));

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            inner.Dispose();
    }

    private static KronikolResultsSink ResultsSink(IMessageSink runnerSink) => new(runnerSink, ReportLifecycle.WriteReports);
}
