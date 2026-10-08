using Xunit.Abstractions;

namespace Kronikol.xUnit2;

/// <summary>
/// Kronikol's reports for a suite whose <c>[assembly: TestFramework]</c> is already taken by another xUnit v2
/// framework, such as Xunit.Extensions.AssemblyFixture's or Xunit.DependencyInjection's.
/// </summary>
public static class TestFrameworkExecutorExtensions
{
    /// <summary>
    /// Wraps another test framework's executor so that its runs write Kronikol's reports, as
    /// <see cref="ReportingTestFramework"/> does: each test's result paired with its own scenario, and the reports
    /// written with <see cref="ReportLifecycle.Options"/> when the test assembly finishes, before the runner hears
    /// that it has.
    /// </summary>
    /// <remarks>
    /// <para>Call it where the framework creates its executor, in a framework class of your own:</para>
    /// <code>
    /// [assembly: TestFramework("MyTests.ReportingAssemblyFixtureFramework", "MyTests")]
    ///
    /// public sealed class ReportingAssemblyFixtureFramework(IMessageSink messageSink) : AssemblyFixtureFramework(messageSink)
    /// {
    ///     protected override ITestFrameworkExecutor CreateExecutor(AssemblyName assemblyName) =>
    ///         base.CreateExecutor(assemblyName).WithKronikolReporting();
    /// }
    /// </code>
    /// <para>A sealed framework is wrapped by composition: an <see cref="ITestFramework"/> of your own holds it and
    /// returns its <c>GetExecutor</c> wrapped. The wrapper turns on xUnit's synchronous message reporting for the
    /// run, on a copy of the runner's options, and passes messages to the runner's sink from one thread, in order, as
    /// xUnit's own message bus does. Wrapping an executor that already writes Kronikol's reports, Kronikol's own or
    /// one already wrapped, returns it unchanged.</para>
    /// </remarks>
    /// <param name="executor">The executor the framework would hand xUnit.</param>
    /// <returns>An executor that runs the same tests and writes Kronikol's reports.</returns>
    public static ITestFrameworkExecutor WithKronikolReporting(this ITestFrameworkExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        return executor is ReportingTestFrameworkExecutor or KronikolReportingExecutor
            ? executor
            : new KronikolReportingExecutor(executor);
    }
}
