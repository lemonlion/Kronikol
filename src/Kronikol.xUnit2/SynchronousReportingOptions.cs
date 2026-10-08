using System.Collections.Concurrent;
using Xunit;
using Xunit.Abstractions;

namespace Kronikol.xUnit2;

/// <summary>
/// The runner's execution options with xUnit's synchronous message reporting turned on, as a copy: what is set
/// on it stays on it, and the runner's own object is never written to (VS 2.8.2 hands over the object it keeps).
/// </summary>
internal sealed class SynchronousReportingOptions : LongLivedMarshalByRefObject, ITestFrameworkExecutionOptions
{
    /// <summary>xUnit v2's option name (<c>TestOptionsNames.Execution.SynchronousMessageReporting</c>).</summary>
    public const string SynchronousMessageReporting = "xunit.execution.SynchronousMessageReporting";

    private readonly ITestFrameworkExecutionOptions _runner;
    private readonly ConcurrentDictionary<string, object?> _values = new();

    private SynchronousReportingOptions(ITestFrameworkExecutionOptions runner)
    {
        _runner = runner;
        _values[SynchronousMessageReporting] = true;
    }

    public static ITestFrameworkExecutionOptions Over(ITestFrameworkExecutionOptions runner) =>
        runner as SynchronousReportingOptions ?? new SynchronousReportingOptions(runner);

    public TValue GetValue<TValue>(string name) =>
        _values.TryGetValue(name, out var value) ? (TValue)value! : _runner.GetValue<TValue>(name);

    public void SetValue<TValue>(string name, TValue value) => _values[name] = value;
}
