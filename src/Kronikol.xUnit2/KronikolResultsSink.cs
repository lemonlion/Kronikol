using System.Collections.Concurrent;
using System.Reflection;
using Kronikol.Reports;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Kronikol.xUnit2;

/// <summary>
/// The sink between xUnit's message bus and the runner's sink, which every result of the run passes through
/// (plans/XUNIT2_FRAMEWORK_COMPOSITION_PLAN.md 4.1 to 4.3).
/// </summary>
/// <remarks>
/// <para>When xUnit sends a test's <c>ITestStarting</c>, the sink makes the scenario of every test
/// <see cref="TestTrackingAttribute"/> applies to, keyed by the test's <see cref="ITest"/> object, and hands it
/// to the attribute's <c>Before</c> on the test's own flow. Each result message carries the same object, so it
/// reaches its own scenario: every row of a theory and every <c>[Fact(DisplayName = …)]</c>, and the tests that
/// fail or are skipped before <c>Before</c> runs, which have a scenario of their own.</para>
/// <para>When the assembly finishes, the sink writes the reports and only then passes the message on, since
/// runners let the test host exit once they have it. Nothing the sink does reaches the bus as an exception: a
/// failure is written to <c>kronikol-error.log</c>, and every message is passed on.</para>
/// </remarks>
internal sealed class KronikolResultsSink : LongLivedMarshalByRefObject, IMessageSink, IDisposable
{
    private static readonly ConcurrentDictionary<(Type Type, MethodInfo Method), bool> Tracked = new();

    private readonly OrderedMessageForwarder _forwarder;
    private readonly ReportWriter _writeReports;
    private readonly ConcurrentDictionary<ITest, ScenarioInfo> _byTest = new(ReferenceEqualityComparer.Instance);
    private readonly ConcurrentQueue<ScenarioInfo> _inOrder = new();
    private DateTime? _start;
    private long _runFrom = XUnit2TestTrackingContext.CurrentSequence;
    private int _finished;

    /// <summary>
    /// Writes the reports for the run's scenarios, between the run's start and end, both UTC, with the diagnostics
    /// the run recorded.
    /// </summary>
    internal delegate void ReportWriter(IReadOnlyList<ScenarioInfo> scenarios, DateTime start, DateTime end, IReadOnlyList<DiagnosticEntry> diagnostics);

    public KronikolResultsSink(IMessageSink runnerSink, ReportWriter writeReports)
    {
        _forwarder = new OrderedMessageForwarder(runnerSink);
        _writeReports = writeReports;
    }

    /// <summary>The scenarios made so far, in the order their tests started.</summary>
    internal IReadOnlyList<ScenarioInfo> Scenarios => _inOrder.ToArray();

    public bool OnMessage(IMessageSinkMessage message)
    {
        if (message is ITestAssemblyFinished)
        {
            try
            {
                Finish();
            }
            catch (Exception ex)
            {
                ReportLifecycle.WriteErrorLog(ex);
            }
            finally
            {
                _forwarder.Send(message);
                _forwarder.Complete();
            }

            return _forwarder.ContinueRunning;
        }

        try
        {
            Observe(message);
        }
        catch (Exception ex)
        {
            ReportLifecycle.WriteErrorLog(ex);
        }

        return _forwarder.Send(message);
    }

    public void Dispose() => _forwarder.Dispose();

    private void Observe(IMessageSinkMessage message)
    {
        switch (message)
        {
            case ITestAssemblyStarting:
                _start ??= DateTime.UtcNow;
                _runFrom = XUnit2TestTrackingContext.CurrentSequence;
                break;

            case ITestStarting starting:
                Start(starting.Test);
                break;

            case ITestResultMessage result when _byTest.TryGetValue(result.Test, out var scenario):
                Record(scenario, result);
                break;

            case ITestFinished finished when _byTest.TryGetValue(finished.Test, out var scenario) && !scenario.HasResult:
                // A cancelled run sends no result: the test ends here, with no verdict.
                scenario.EndedAt = DateTimeOffset.UtcNow;
                break;
        }
    }

    private void Start(ITest test)
    {
        var testMethod = test.TestCase.TestMethod;
        var type = testMethod.TestClass.Class.ToRuntimeType();
        var method = testMethod.Method.ToRuntimeMethod();
        if (type is null || method is null || !Tracked.GetOrAdd((type, method), key => AppliesTo(key.Type, key.Method, testMethod)))
            return;

        var scenario = new ScenarioInfo
        {
            Id = Guid.NewGuid().ToString(),
            // As TestTrackingAttribute.Before names a scenario it makes: the class running the test, which for an
            // inherited test method is the derived class.
            FeatureName = ScenarioTitleResolver.FormatFeatureName(type.Name),
            ScenarioName = ScenarioTitleResolver.FormatScenarioDisplayName(test.DisplayName),
            MethodMatchKey = $"{type.FullName}.{method.Name}",
            Endpoint = type.GetCustomAttributes(inherit: true).OfType<EndpointAttribute>().FirstOrDefault()?.Endpoint,
            IsHappyPath = method.GetCustomAttributes(inherit: true).OfType<HappyPathAttribute>().Any(),
            MadeAtTestStarting = true,
        };

        _byTest[test] = scenario;
        _inOrder.Enqueue(scenario);
        XUnit2TestTrackingContext.CollectedScenarios[scenario.Id] = scenario;
        XUnit2TestTrackingContext.HandOver(scenario, method);
    }

    private static void Record(ScenarioInfo scenario, ITestResultMessage result)
    {
        scenario.Result = result switch
        {
            ITestFailed => ExecutionResult.Failed,
            ITestSkipped => ExecutionResult.Skipped,
            _ => ExecutionResult.Passed,
        };
        scenario.ErrorMessage = result is ITestFailed failed ? FailureText.Join(failed.Messages) : null;
        scenario.ErrorStackTrace = result is ITestFailed failure ? FailureText.Join(failure.StackTraces) : null;
        scenario.Duration = result.ExecutionTime > 0 ? TimeSpan.FromSeconds((double)result.ExecutionTime) : null;
        // When the result reached the sink, which is as close to the test's end as xUnit v2 lets a sink get.
        scenario.EndedAt = DateTimeOffset.UtcNow;
        scenario.HasResult = true;
    }

    /// <summary>
    /// Whether xUnit runs <see cref="TestTrackingAttribute"/>'s <c>Before</c> for the test: xUnit takes the
    /// attributes of the test collection's definition, the class, the method and the assembly
    /// (<c>XunitTestCaseRunner</c>), each with inherited ones.
    /// </summary>
    private static bool AppliesTo(Type type, MethodInfo method, ITestMethod testMethod) =>
        method.IsDefined(typeof(TestTrackingAttribute), inherit: true)
        || type.IsDefined(typeof(TestTrackingAttribute), inherit: true)
        || type.Assembly.IsDefined(typeof(TestTrackingAttribute))
        || testMethod.TestClass.TestCollection.CollectionDefinition?.ToRuntimeType() is { } definition
           && definition.IsDefined(typeof(TestTrackingAttribute), inherit: true);

    /// <summary>
    /// The end of the run: scenarios the attribute made on its own are paired with their results, a tracked test
    /// with no result is reported as a default, and the reports are written.
    /// </summary>
    private void Finish()
    {
        if (Interlocked.Exchange(ref _finished, 1) != 0)
            return;

        var diagnostics = new List<DiagnosticEntry>();
        var scenarios = PairTheAttributesOwn(_inOrder.ToArray(), diagnostics);
        foreach (var scenario in scenarios.Where(s => !s.HasResult))
        {
            scenario.Result = ExecutionResult.Passed;
            scenario.ResultDefaulted = true;
        }

        _writeReports(scenarios, _start ?? DateTime.UtcNow, DateTime.UtcNow, diagnostics);
    }

    /// <summary>
    /// Pairs the scenarios <see cref="TestTrackingAttribute"/> made on its own during this run with the sink's, by test
    /// method and in the order the tests started (plans/XUNIT2_FRAMEWORK_COMPOSITION_PLAN.md 4.4).
    /// </summary>
    /// <remarks>
    /// The attribute makes its own when nothing was handed over on the test's flow: a framework whose message bus
    /// delivers from a thread of its own, after the test may already have run. Its scenario holds the test's calls,
    /// and the sink's holds its result, so the attribute's takes the result and is reported in the sink's place.
    /// The rows of a theory pair in the order they started, which is their order unless they ran in parallel.
    /// </remarks>
    private List<ScenarioInfo> PairTheAttributesOwn(ScenarioInfo[] scenarios, List<DiagnosticEntry> diagnostics)
    {
        var own = XUnit2TestTrackingContext.CollectedScenarios.Values
            .Where(s => !s.MadeAtTestStarting && !s.Claimed && s.Sequence > _runFrom)
            .OrderBy(s => s.Sequence)
            .ToList();

        var reported = new List<ScenarioInfo>(scenarios.Length + own.Count);
        var paired = 0;
        foreach (var scenario in scenarios)
        {
            var partner = scenario.TakenByBefore ? null : own.FirstOrDefault(o => o.MethodMatchKey == scenario.MethodMatchKey);
            if (partner is null)
            {
                reported.Add(scenario);
                continue;
            }

            own.Remove(partner);
            partner.Claimed = true;
            partner.ScenarioName = scenario.ScenarioName;
            partner.Result = scenario.Result;
            partner.ErrorMessage = scenario.ErrorMessage;
            partner.ErrorStackTrace = scenario.ErrorStackTrace;
            partner.Duration = scenario.Duration;
            partner.EndedAt = scenario.EndedAt;
            partner.HasResult = scenario.HasResult;
            reported.Add(partner);
            paired++;
        }

        // Any left were made for tests the sink never saw start, which no framework Kronikol knows of does: they are
        // reported with no verdict, so their calls are not dropped.
        foreach (var leftOver in own)
        {
            leftOver.Claimed = true;
            reported.Add(leftOver);
        }

        if (paired + own.Count > 0)
            diagnostics.Add(new DiagnosticEntry(DiagnosticKind.Other,
                $"{paired + own.Count} scenario(s) were paired with their results by test method, in the order the tests "
                + "started, because the test framework's message bus did not deliver xUnit's messages on each test's own "
                + "flow, which is how Kronikol hands a test its scenario. A theory's rows that ran in parallel could show "
                + "each other's results. Turn on xunit.execution.SynchronousMessageReporting, or see the wiki's "
                + "Integration-xUnit2 page, \"Already using another test framework\"."));

        return reported;
    }
}
