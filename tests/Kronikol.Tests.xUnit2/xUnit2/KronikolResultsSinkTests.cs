using Kronikol.Reports;
using Kronikol.xUnit2;
using Xunit.Abstractions;
using static Kronikol.Tests.xUnit2.XunitMessages;

namespace Kronikol.Tests.xUnit2;

// What the sink is driven with: the test classes of a run, one tracked and one not.
[TestTracking]
public class TrackedSinkTests
{
    public void Rows(int row) { }
    public void Passes() { }
    public void Fails() { }
    public void Skipped() { }
    public void Cancelled() { }
}

public class UntrackedSinkTests
{
    public void Passes() { }
}

public class MethodTrackedSinkTests
{
    [TestTracking]
    public void Tracked() { }

    public void NotTracked() { }
}

[TestTracking]
public abstract class TrackedSinkBase;

public class DerivedSinkTests : TrackedSinkBase
{
    public void Inherits() { }
}

/// <summary>
/// The results sink driven with xUnit's own message types (plans/XUNIT2_FRAMEWORK_COMPOSITION_PLAN.md T8). The
/// fixture lane runs it under real runners; these pin what each message does.
/// </summary>
[Collection("CollectedScenarios")]
public class KronikolResultsSinkTests : IDisposable
{
    private readonly List<(IReadOnlyList<ScenarioInfo> Scenarios, DateTime Start, DateTime End)> _written = [];

    public KronikolResultsSinkTests() => XUnit2TestTrackingContext.CollectedScenarios.Clear();

    public void Dispose() => XUnit2TestTrackingContext.CollectedScenarios.Clear();

    private KronikolResultsSink Sink(IMessageSink? runner = null) =>
        new(runner ?? new RecordingSink(), (scenarios, start, end) => _written.Add((scenarios, start, end)));

    private static void Run(KronikolResultsSink sink, params IMessageSinkMessage[] messages)
    {
        foreach (var message in messages)
            sink.OnMessage(message);
    }

    [Fact]
    public void Each_row_of_a_theory_is_its_own_scenario_with_its_own_name_and_result()
    {
        var row1 = Test<TrackedSinkTests>(nameof(TrackedSinkTests.Rows), "Ns.TrackedSinkTests.Rows(row: 1)");
        var row2 = Test<TrackedSinkTests>(nameof(TrackedSinkTests.Rows), "Ns.TrackedSinkTests.Rows(row: 2)");
        using var sink = Sink();

        // Both rows start before either ends, and the second row's result comes first.
        Run(sink, AssemblyStarting(), Starting(row1), Starting(row2), Failed(row2, "row 2 failed", 0.75m), Passed(row1, 0.25m),
            Finished(row1), Finished(row2), AssemblyFinished());

        var scenarios = Assert.Single(_written).Scenarios;
        Assert.Equal(["Rows [row: 1]", "Rows [row: 2]"], scenarios.Select(s => s.ScenarioName));
        Assert.Equal([ExecutionResult.Passed, ExecutionResult.Failed], scenarios.Select(s => s.Result));
        Assert.Equal([TimeSpan.FromSeconds(0.25), TimeSpan.FromSeconds(0.75)], scenarios.Select(s => s.Duration));
        Assert.Equal([null, "row 2 failed"], scenarios.Select(s => s.ErrorMessage));
        Assert.All(scenarios, s => Assert.False(s.ResultDefaulted));
    }

    [Fact]
    public void A_test_the_attribute_does_not_apply_to_gets_no_scenario()
    {
        var test = Test<UntrackedSinkTests>(nameof(UntrackedSinkTests.Passes));
        using var sink = Sink();

        Run(sink, AssemblyStarting(), Starting(test), Passed(test), Finished(test), AssemblyFinished());

        Assert.Empty(Assert.Single(_written).Scenarios);
        Assert.Empty(XUnit2TestTrackingContext.CollectedScenarios);
    }

    [Fact]
    public void The_attribute_applies_from_the_method_and_from_a_base_class_as_xunit_applies_it()
    {
        var onMethod = Test<MethodTrackedSinkTests>(nameof(MethodTrackedSinkTests.Tracked));
        var notOnMethod = Test<MethodTrackedSinkTests>(nameof(MethodTrackedSinkTests.NotTracked));
        var inherited = Test<DerivedSinkTests>(nameof(DerivedSinkTests.Inherits));
        using var sink = Sink();

        Run(sink, AssemblyStarting(), Starting(onMethod), Starting(notOnMethod), Starting(inherited), AssemblyFinished());

        Assert.Equal(["Tracked", "Inherits"], Assert.Single(_written).Scenarios.Select(s => s.ScenarioName));
        // Named after the class running the test, as TestTrackingAttribute.Before names one.
        Assert.Equal("Derived Sink Tests", Assert.Single(_written).Scenarios[1].FeatureName);
    }

    [Fact]
    public void A_skipped_test_is_reported_skipped_with_no_error()
    {
        var test = Test<TrackedSinkTests>(nameof(TrackedSinkTests.Skipped));
        using var sink = Sink();

        Run(sink, AssemblyStarting(), Starting(test), Skipped(test, "not today"), Finished(test), AssemblyFinished());

        var scenario = Assert.Single(Assert.Single(_written).Scenarios);
        Assert.Equal(ExecutionResult.Skipped, scenario.Result);
        Assert.Null(scenario.ErrorMessage);
        Assert.Null(scenario.Duration);
    }

    [Fact]
    public void A_test_that_ends_with_no_result_is_reported_as_a_default()
    {
        var cancelled = Test<TrackedSinkTests>(nameof(TrackedSinkTests.Cancelled));
        var passes = Test<TrackedSinkTests>(nameof(TrackedSinkTests.Passes));
        using var sink = Sink();

        Run(sink, AssemblyStarting(), Starting(cancelled), Finished(cancelled), Starting(passes), Passed(passes), Finished(passes),
            AssemblyFinished());

        var scenarios = Assert.Single(_written).Scenarios;
        Assert.True(scenarios[0].ResultDefaulted);
        Assert.Equal(ExecutionResult.Passed, scenarios[0].Result);
        Assert.NotNull(scenarios[0].EndedAt);
        Assert.False(scenarios[1].ResultDefaulted);
    }

    [Fact]
    public void A_default_result_is_written_with_the_ResultDefaulted_diagnostic()
    {
        var scenario = new ScenarioInfo
        {
            Id = Guid.NewGuid().ToString(), FeatureName = "Feature", ScenarioName = "Never ended",
            MethodMatchKey = "Ns.Class.NeverEnded", ResultDefaulted = true,
        };
        var collector = new ReportDiagnosticsCollector();
        var reports = Path.Combine(Path.GetTempPath(), "kronikol-sink-" + Guid.NewGuid().ToString("N"));

        try
        {
            using (ReportDiagnosticsScope.Begin(collector))
                XUnit2ReportGenerator.Write([scenario], DateTime.UtcNow.AddSeconds(-1), DateTime.UtcNow,
                    new ReportConfigurationOptions { ReportsFolderPath = reports }, "a reason");

            var entry = Assert.Single(collector.Entries, e => e.Kind == DiagnosticKind.ResultDefaulted);
            Assert.StartsWith("1 scenario(s) recorded no result and were reported as Passed: a reason.", entry.Message);
            Assert.Contains("Never ended", entry.Message);
        }
        finally
        {
            if (Directory.Exists(reports))
                Directory.Delete(reports, recursive: true);
        }
    }

    [Fact]
    public void The_scenario_is_handed_over_on_the_tests_own_flow_and_is_gone_when_that_flow_returns()
    {
        var test = Test<TrackedSinkTests>(nameof(TrackedSinkTests.Passes));
        var method = MethodOf(test);
        using var sink = Sink();
        sink.OnMessage(AssemblyStarting());

        ScenarioInfo? seenInside = null;
        ScenarioInfo? seenForAnotherMethod = null;

        // As xUnit's TestRunner.RunAsync does under synchronous reporting: the sink is called inline, then the
        // test runs on the same flow.
        async Task RunTest()
        {
            sink.OnMessage(Starting(test));
            await Task.Yield();
            seenInside = XUnit2TestTrackingContext.HandedOverScenarioFor(method);
            seenForAnotherMethod = XUnit2TestTrackingContext.HandedOverScenarioFor(MethodOf(Test<TrackedSinkTests>(nameof(TrackedSinkTests.Fails))));
        }

        RunTest().GetAwaiter().GetResult();

        Assert.Same(Assert.Single(sink.Scenarios), seenInside);
        Assert.Null(seenForAnotherMethod);
        Assert.Null(XUnit2TestTrackingContext.HandedOverScenarioFor(method));
    }

    [Fact]
    public void TestTrackingAttribute_takes_the_handed_over_scenario_and_makes_none_of_its_own()
    {
        var test = Test<TrackedSinkTests>(nameof(TrackedSinkTests.Passes));
        using var sink = Sink();
        sink.OnMessage(AssemblyStarting());
        string? idInside = null;

        async Task RunTest()
        {
            sink.OnMessage(Starting(test));
            await Task.Yield();
            var attribute = new TestTrackingAttribute();
            attribute.Before(MethodOf(test));
            attribute.Before(MethodOf(test)); // the attribute on the assembly and the class: Before runs twice
            idInside = XUnit2TestTrackingContext.GetCurrentTestInfo().Id;
            attribute.After(MethodOf(test));
        }

        RunTest().GetAwaiter().GetResult();

        var scenario = Assert.Single(sink.Scenarios);
        Assert.Equal(scenario.Id, idInside);
        Assert.Same(scenario, Assert.Single(XUnit2TestTrackingContext.CollectedScenarios.Values));
    }

    [Fact]
    public void The_reports_are_written_before_the_runner_hears_that_the_assembly_finished()
    {
        bool? reportsWrittenWhenFinishedArrived = null;
        var runner = new RecordingSink
        {
            OnReceived = message =>
            {
                if (message is ITestAssemblyFinished)
                    reportsWrittenWhenFinishedArrived ??= _written.Count == 1;
            },
        };
        using var sink = Sink(runner);

        Run(sink, AssemblyStarting(), AssemblyFinished());

        // OnMessage returned only once the runner had it, as xUnit's bus returns from Dispose only once it has.
        Assert.Single(runner.Messages.OfType<ITestAssemblyFinished>());
        Assert.IsAssignableFrom<ITestAssemblyFinished>(runner.Messages.Last());
        Assert.True(reportsWrittenWhenFinishedArrived);
    }

    [Fact]
    public void Every_message_reaches_the_runner_in_order_whatever_the_sink_makes_of_it()
    {
        var test = Test<TrackedSinkTests>(nameof(TrackedSinkTests.Passes));
        var untracked = Test<UntrackedSinkTests>(nameof(UntrackedSinkTests.Passes));
        var runner = new RecordingSink();
        using var sink = Sink(runner);
        IMessageSinkMessage[] messages =
            [AssemblyStarting(), Starting(test), Starting(untracked), Passed(untracked), Passed(test), Finished(test), Finished(untracked), AssemblyFinished()];

        Run(sink, messages);

        Assert.Equal(messages, runner.Messages);
    }

    [Fact]
    public void Messages_sent_from_many_test_threads_reach_the_runner_from_one_thread_one_at_a_time()
    {
        // Under synchronous reporting with collections in parallel, every test thread calls the sink.
        var runner = new RecordingSink();
        using var sink = Sink(runner);
        using var go = new ManualResetEventSlim();
        var tests = Enumerable.Range(0, 20).Select(_ => Test<UntrackedSinkTests>(nameof(UntrackedSinkTests.Passes))).ToArray();

        var threads = tests.Select(test => new Thread(() =>
        {
            go.Wait();
            for (var i = 0; i < 25; i++)
                sink.OnMessage(Passed(test));
        })).ToArray();
        foreach (var thread in threads)
            thread.Start();
        go.Set();
        foreach (var thread in threads)
            thread.Join();
        sink.OnMessage(AssemblyFinished());

        Assert.Equal(20 * 25 + 1, runner.Messages.Length);
        Assert.Equal(1, runner.MostAtOnce);
        Assert.Single(runner.Received.Select(r => r.Thread).Distinct());
    }

    [Fact]
    public void A_report_that_fails_to_write_still_lets_the_runner_hear_that_the_assembly_finished()
    {
        var runner = new RecordingSink();
        using var sink = new KronikolResultsSink(runner, (_, _, _) => throw new IOException("the disk is full"));

        var answer = sink.OnMessage(AssemblyStarting()) && sink.OnMessage(AssemblyFinished());

        Assert.True(answer);
        Assert.IsAssignableFrom<ITestAssemblyFinished>(runner.Messages.Last());
    }

    [Fact]
    public void The_run_starts_when_the_assembly_starts()
    {
        using var sink = Sink();
        var before = DateTime.UtcNow;
        sink.OnMessage(AssemblyStarting());
        Thread.Sleep(50);
        var after = DateTime.UtcNow;

        sink.OnMessage(AssemblyFinished());

        var (_, start, end) = Assert.Single(_written);
        Assert.InRange(start, before, after);
        Assert.True(end >= start.AddMilliseconds(45), $"The run started {start:O} and ended {end:O}, 50 ms later");
    }
}
