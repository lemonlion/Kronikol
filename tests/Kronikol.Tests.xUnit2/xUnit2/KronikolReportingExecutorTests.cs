using System.Reflection;
using Kronikol.Reports;
using Kronikol.xUnit2;
using Xunit.Abstractions;
using static Kronikol.Tests.xUnit2.XunitMessages;

namespace Kronikol.Tests.xUnit2;

/// <summary>Another framework's executor: it hands back whatever sink and options it is given to whoever runs it.</summary>
internal sealed class FakeExecutor : Xunit.LongLivedMarshalByRefObject, ITestFrameworkExecutor
{
    public IMessageSink? Sink { get; private set; }
    public ITestFrameworkExecutionOptions? Options { get; private set; }
    public int Disposed { get; private set; }

    public ITestCase Deserialize(string value) => throw new NotSupportedException(value);

    public void RunAll(IMessageSink executionMessageSink, ITestFrameworkDiscoveryOptions discoveryOptions, ITestFrameworkExecutionOptions executionOptions)
    {
        Sink = executionMessageSink;
        Options = executionOptions;
    }

    public void RunTests(IEnumerable<ITestCase> testCases, IMessageSink executionMessageSink, ITestFrameworkExecutionOptions executionOptions)
    {
        Sink = executionMessageSink;
        Options = executionOptions;
    }

    public void Dispose() => Disposed++;
}

/// <summary>
/// <see cref="TestFrameworkExecutorExtensions.WithKronikolReporting"/> (plans/XUNIT2_FRAMEWORK_COMPOSITION_PLAN.md
/// 4.7, T17 and T20) and the pairing for a framework whose bus delivers on a thread of its own (4.4, T18).
/// </summary>
[Collection("CollectedScenarios")]
public class KronikolReportingExecutorTests : IDisposable
{
    public KronikolReportingExecutorTests() => XUnit2TestTrackingContext.CollectedScenarios.Clear();

    public void Dispose() => XUnit2TestTrackingContext.CollectedScenarios.Clear();

    [Fact]
    public void Wrapping_twice_gives_the_first_wrapper_back()
    {
        var once = new FakeExecutor().WithKronikolReporting();

        Assert.Same(once, once.WithKronikolReporting());
    }

    [Fact]
    public void Kronikols_own_executor_is_not_wrapped()
    {
        using var own = new ReportingTestFrameworkExecutor(
            typeof(KronikolReportingExecutorTests).Assembly.GetName(), new NoSourceInformation(), new NullMessageSink());

        Assert.Same(own, own.WithKronikolReporting());
    }

    [Fact]
    public void Wrapping_nothing_is_refused() =>
        Assert.Throws<ArgumentNullException>(() => ((ITestFrameworkExecutor)null!).WithKronikolReporting());

    [Fact]
    public void A_run_reaches_the_runner_through_Kronikols_sink_with_synchronous_reporting_on_a_copy_of_its_options()
    {
        var inner = new FakeExecutor();
        var runner = new RecordingSink();
        var options = new RunnerOptions();
        var wrapped = inner.WithKronikolReporting();

        wrapped.RunTests([], runner, options);
        inner.Sink!.OnMessage(AssemblyStarting());
        inner.Sink.OnMessage(AssemblyFinished());

        Assert.NotSame(runner, inner.Sink);
        Assert.True(inner.Options!.GetValue<bool?>(SynchronousReportingOptions.SynchronousMessageReporting));
        Assert.Empty(options.Values);
        Assert.Equal(2, runner.Messages.Length);
    }

    [Fact]
    public void RunAll_goes_through_Kronikols_sink_too()
    {
        var inner = new FakeExecutor();
        var runner = new RecordingSink();

        inner.WithKronikolReporting().RunAll(runner, null!, new RunnerOptions());

        Assert.NotSame(runner, inner.Sink);
        Assert.True(inner.Options!.GetValue<bool?>(SynchronousReportingOptions.SynchronousMessageReporting));
    }

    [Fact]
    public void The_framework_is_disposed_once_however_often_xunit_disposes_the_wrapper()
    {
        var inner = new FakeExecutor();
        var wrapped = inner.WithKronikolReporting();

        wrapped.Dispose();
        wrapped.Dispose();

        Assert.Equal(1, inner.Disposed);
    }

    [Fact]
    public void When_nothing_is_handed_over_the_attributes_own_scenarios_take_their_results_by_test_method_in_order()
    {
        var row1 = Test<TrackedSinkTests>(nameof(TrackedSinkTests.Rows), "Ns.TrackedSinkTests.Rows(row: 1)");
        var row2 = Test<TrackedSinkTests>(nameof(TrackedSinkTests.Rows), "Ns.TrackedSinkTests.Rows(row: 2)");
        var named = Test<TrackedSinkTests>(nameof(TrackedSinkTests.Fails), "A failing fact with a display name");
        var skipped = Test<TrackedSinkTests>(nameof(TrackedSinkTests.Skipped));
        var written = new List<(IReadOnlyList<ScenarioInfo> Scenarios, IReadOnlyList<DiagnosticEntry> Diagnostics)>();
        using var sink = new KronikolResultsSink(new RecordingSink(), (scenarios, _, _, diagnostics) => written.Add((scenarios, diagnostics)));
        sink.OnMessage(AssemblyStarting());

        // An asynchronous bus: each test runs, and TestTrackingAttribute.Before finds nothing handed over, before the
        // bus's own thread tells the sink the test started.
        var ids = new List<string>();
        foreach (var test in new[] { row1, row2, named })
        {
            var attribute = new TestTrackingAttribute();
            attribute.Before(MethodOf(test));
            ids.Add(XUnit2TestTrackingContext.GetCurrentTestInfo().Id);
            attribute.After(MethodOf(test));
        }

        Task.Run(() =>
        {
            foreach (var message in new IMessageSinkMessage[]
                     {
                         Starting(row1), Passed(row1), Finished(row1), Starting(row2), Failed(row2, "row 2 failed"), Finished(row2),
                         Starting(named), Failed(named, "the named one failed"), Finished(named), Starting(skipped), Skipped(skipped),
                         Finished(skipped), AssemblyFinished(),
                     })
                sink.OnMessage(message);
        }).GetAwaiter().GetResult();

        var (reported, diagnostics) = Assert.Single(written);
        // The calls were made under the attribute's ids, so those are the scenarios reported, with the sink's results.
        Assert.Equal([ids[0], ids[1], ids[2]], reported.Take(3).Select(s => s.Id));
        Assert.Equal(["Rows [row: 1]", "Rows [row: 2]", "A failing fact with a display name", "Skipped"], reported.Select(s => s.ScenarioName));
        Assert.Equal([ExecutionResult.Passed, ExecutionResult.Failed, ExecutionResult.Failed, ExecutionResult.Skipped], reported.Select(s => s.Result));
        Assert.Equal("row 2 failed", reported[1].ErrorMessage);
        Assert.All(reported, s => Assert.False(s.ResultDefaulted));
        var paired = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticKind.Other, paired.Kind);
        Assert.StartsWith("3 scenario(s) were paired with their results by test method", paired.Message);
    }

    [Fact]
    public void A_run_that_hands_its_scenarios_over_records_no_pairing()
    {
        var test = Test<TrackedSinkTests>(nameof(TrackedSinkTests.Passes));
        var written = new List<IReadOnlyList<DiagnosticEntry>>();
        using var sink = new KronikolResultsSink(new RecordingSink(), (_, _, _, diagnostics) => written.Add(diagnostics));
        sink.OnMessage(AssemblyStarting());

        async Task RunTest()
        {
            sink.OnMessage(Starting(test));
            await Task.Yield();
            var attribute = new TestTrackingAttribute();
            attribute.Before(MethodOf(test));
            attribute.After(MethodOf(test));
            sink.OnMessage(Passed(test));
        }

        RunTest().GetAwaiter().GetResult();
        sink.OnMessage(AssemblyFinished());

        Assert.Empty(Assert.Single(written));
    }
}
