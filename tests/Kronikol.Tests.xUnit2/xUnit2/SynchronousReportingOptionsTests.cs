using System.Reflection;
using Kronikol.xUnit2;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Kronikol.Tests.xUnit2;

/// <summary>A runner's execution options, as a runner keeps them: a dictionary it reads back after the run.</summary>
internal sealed class RunnerOptions : Xunit.LongLivedMarshalByRefObject, ITestFrameworkExecutionOptions
{
    public Dictionary<string, object?> Values { get; } = [];

    public TValue GetValue<TValue>(string name) => Values.TryGetValue(name, out var value) ? (TValue)value! : default!;

    public void SetValue<TValue>(string name, TValue value) => Values[name] = value;
}

internal sealed class NoSourceInformation : Xunit.LongLivedMarshalByRefObject, ISourceInformationProvider
{
    public ISourceInformation? GetSourceInformation(ITestCase testCase) => null;

    public void Dispose() { }
}

/// <summary>T20: synchronous reporting is set on a copy of the runner's options, never on the runner's object.</summary>
[Collection("CollectedScenarios")]
public class SynchronousReportingOptionsTests
{
    [Fact]
    public void The_copy_reports_synchronously_and_reads_everything_else_from_the_runners_options()
    {
        var runner = new RunnerOptions();
        runner.SetValue("xunit.execution.MaxParallelThreads", 3);

        var copy = SynchronousReportingOptions.Over(runner);
        copy.SetValue("xunit.execution.DisableParallelization", true);

        Assert.True(copy.GetValue<bool?>(SynchronousReportingOptions.SynchronousMessageReporting));
        Assert.Equal(3, copy.GetValue<int?>("xunit.execution.MaxParallelThreads"));
        Assert.True(copy.GetValue<bool?>("xunit.execution.DisableParallelization"));
        Assert.False(runner.Values.ContainsKey(SynchronousReportingOptions.SynchronousMessageReporting));
        Assert.False(runner.Values.ContainsKey("xunit.execution.DisableParallelization"));
    }

    [Fact]
    public void Kronikols_executor_leaves_the_runners_options_as_it_found_them()
    {
        var runner = new RunnerOptions();
        var assemblyName = typeof(SynchronousReportingOptionsTests).Assembly.GetName();
        using var executor = new ReportingTestFrameworkExecutor(assemblyName, new NoSourceInformation(), new NullMessageSink());
        var messages = new RecordingSink();

        executor.RunTests([], messages, runner);

        Assert.Empty(runner.Values);
        Assert.IsAssignableFrom<ITestAssemblyFinished>(messages.Messages.Last());
    }
}
