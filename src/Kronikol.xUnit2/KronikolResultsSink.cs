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
    private int _finished;

    /// <summary>Writes the reports for the run's scenarios, between the run's start and end, both UTC.</summary>
    internal delegate void ReportWriter(IReadOnlyList<ScenarioInfo> scenarios, DateTime start, DateTime end);

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

    /// <summary>The end of the run: a tracked test with no result is reported as a default, then the reports are written.</summary>
    private void Finish()
    {
        if (Interlocked.Exchange(ref _finished, 1) != 0)
            return;

        var scenarios = _inOrder.ToArray();
        foreach (var scenario in scenarios.Where(s => !s.HasResult))
        {
            scenario.Result = ExecutionResult.Passed;
            scenario.ResultDefaulted = true;
        }

        _writeReports(scenarios, _start ?? DateTime.UtcNow, DateTime.UtcNow);
    }
}
