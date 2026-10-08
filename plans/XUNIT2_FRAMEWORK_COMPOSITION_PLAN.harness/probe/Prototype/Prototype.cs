using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Kronikol;
using Kronikol.Reports;
using Kronikol.xUnit2;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Probe.Prototype;

/// <summary>
/// Issue #132's workaround as a decorator over any framework's executor: RunAll/RunTests ask for
/// synchronous message reporting and wrap the execution sink; the sink keys a scenario by the ITest
/// instance on ITestStarting, hands it to Before through an AsyncLocal, matches the result message by
/// ITest reference, and writes the reports on ITestAssemblyFinished before forwarding it (the runner lets
/// the process exit once it has that message).
/// </summary>
public static class Prototype
{
    public const string SyncOption = "xunit.execution.SynchronousMessageReporting";

    public static ITestFrameworkExecutor Wrap(ITestFrameworkExecutor inner)
    {
        ProbeLog.Init();
        return new ReportingExecutor(inner);
    }
}

// LongLivedMarshalByRefObject: xunit.analyzers (in the xunit metapackage) fails the build with xUnit3000 for
// an ITestFrameworkExecutor or IMessageSink that does not derive from it. On .NET (Core) it is a plain object.
internal sealed class ReportingExecutor(ITestFrameworkExecutor inner) : LongLivedMarshalByRefObject, ITestFrameworkExecutor
{
    public ITestCase Deserialize(string value) => inner.Deserialize(value);

    public void Dispose() => inner.Dispose();

    public void RunAll(IMessageSink executionMessageSink, ITestFrameworkDiscoveryOptions discoveryOptions, ITestFrameworkExecutionOptions executionOptions)
    {
        Configure("RunAll", executionOptions);
        inner.RunAll(new ScenarioSink(executionMessageSink), discoveryOptions, executionOptions);
    }

    public void RunTests(IEnumerable<ITestCase> testCases, IMessageSink executionMessageSink, ITestFrameworkExecutionOptions executionOptions)
    {
        Configure("RunTests", executionOptions);
        inner.RunTests(testCases, new ScenarioSink(executionMessageSink), executionOptions);
    }

    // PROBE_SYNC=0 leaves the runner's choice alone (asynchronous reporting, unless the runner asked otherwise).
    private static void Configure(string entry, ITestFrameworkExecutionOptions executionOptions)
    {
        var before = executionOptions.GetValue<bool?>(Prototype.SyncOption);
        if (Environment.GetEnvironmentVariable("PROBE_SYNC") != "0")
            executionOptions.SetValue(Prototype.SyncOption, true);
        ProbeLog.Write("options", "-",
            $"entry={entry} runnerValue={before?.ToString() ?? "unset"} value={executionOptions.GetValue<bool?>(Prototype.SyncOption)?.ToString() ?? "unset"}");
    }
}

/// <summary>The scenario the sink created for the test running on this flow. Set on ITestStarting.</summary>
public static class PrototypeContext
{
    private static readonly AsyncLocal<ProbeScenario?> Scenario = new();

    public static ProbeScenario? Current
    {
        get => Scenario.Value;
        internal set => Scenario.Value = value;
    }
}

/// <summary>What the sink knows about one ITest, from its ITestStarting to its result message.</summary>
public sealed class ProbeScenario
{
    public required ITest Test { get; init; }
    public string OwnId { get; } = Guid.NewGuid().ToString();
    /// <summary>The id TestTracking's Before put on this test's calls; PrototypeTracking copies it here.</summary>
    public string? TrackingId { get; internal set; }
    public required string FeatureName { get; init; }
    public required string ScenarioName { get; init; }
    public required string MethodMatchKey { get; init; }
    public string? Endpoint { get; init; }
    public bool IsHappyPath { get; init; }
    public ExecutionResult? Result { get; internal set; }
    public string? ErrorMessage { get; internal set; }
    public string? ErrorStackTrace { get; internal set; }
    public decimal ExecutionTime { get; internal set; }
    public DateTimeOffset? EndedAt { get; internal set; }
    public bool SawStarting { get; init; }
    internal ConcurrentQueue<string> BeforeAttributes { get; } = new();

    internal static ProbeScenario For(ITest test, bool sawStarting)
    {
        var testMethod = test.TestCase.TestMethod;
        var type = (testMethod.TestClass.Class as IReflectionTypeInfo)?.Type;
        var method = (testMethod.Method as IReflectionMethodInfo)?.MethodInfo;
        var className = testMethod.TestClass.Class.Name;
        return new ProbeScenario
        {
            Test = test,
            SawStarting = sawStarting,
            // The ScenarioTitleResolver calls TestTrackingAttribute.Before makes, on the same class name; the
            // scenario name comes from the ITest's display name, so rows and [Fact(DisplayName)] keep theirs.
            FeatureName = ScenarioTitleResolver.FormatFeatureName(type?.Name ?? className[(className.LastIndexOf('.') + 1)..]),
            ScenarioName = FormatName(test.DisplayName),
            MethodMatchKey = $"{type?.FullName ?? className}.{testMethod.Method.Name}",
            Endpoint = type?.GetCustomAttributes(inherit: true).OfType<EndpointAttribute>().FirstOrDefault()?.Endpoint,
            IsHappyPath = method?.GetCustomAttributes(inherit: true).OfType<HappyPathAttribute>().Any() ?? false,
        };
    }

    // PROBE_FORMAT_GUARD=1 falls back to the display name as written when the formatter throws; unset, the
    // exception leaves OnMessage(ITestStarting) as an unguarded design's would. Either way it is written to
    // prototype-formatter.log in the test output at once, so a run that dies still leaves it.
    private static readonly bool GuardFormatter = Environment.GetEnvironmentVariable("PROBE_FORMAT_GUARD") == "1";

    private static string FormatName(string displayName)
    {
        try
        {
            return ScenarioTitleResolver.FormatScenarioDisplayName(displayName);
        }
        catch (Exception ex)
        {
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "prototype-formatter.log"),
                $"{DateTime.UtcNow:O}\tguard={(GuardFormatter ? 1 : 0)}\t{displayName}\t{ex.GetType().FullName}: {ex.Message}{Environment.NewLine}");
            ProbeLog.Write("formatter-threw", displayName, ex.GetType().Name);
            if (!GuardFormatter)
                throw;
            return displayName;
        }
    }

    internal ScenarioInfo ToScenarioInfo() => new()
    {
        Id = TrackingId ?? OwnId,
        FeatureName = FeatureName,
        ScenarioName = ScenarioName,
        MethodMatchKey = MethodMatchKey,
        Endpoint = Endpoint,
        IsHappyPath = IsHappyPath,
        Result = Result ?? ExecutionResult.Passed,
        ErrorMessage = ErrorMessage,
        ErrorStackTrace = ErrorStackTrace,
        Duration = ExecutionTime > 0 ? TimeSpan.FromSeconds((double)ExecutionTime) : null,
        EndedAt = EndedAt,
    };
}

internal sealed class ScenarioSink(IMessageSink inner) : LongLivedMarshalByRefObject, IMessageSink
{
    private readonly ConcurrentDictionary<ITest, ProbeScenario> _byTest = new(ReferenceEqualityComparer.Instance);
    private readonly ConcurrentQueue<ProbeScenario> _inOrder = new();
    private DateTime _start = DateTime.UtcNow;

    public bool OnMessage(IMessageSinkMessage message)
    {
        switch (message)
        {
            case ITestAssemblyStarting starting:
                _start = starting.StartTime.ToUniversalTime();
                break;

            case ITestStarting starting:
                var previous = PrototypeContext.Current;
                var scenario = ProbeScenario.For(starting.Test, sawStarting: true);
                _byTest[starting.Test] = scenario;
                _inOrder.Enqueue(scenario);
                PrototypeContext.Current = scenario;
                ProbeLog.Write("starting", starting.Test.DisplayName, $"previous={previous?.Test.DisplayName ?? "null"}");
                break;

            case IBeforeTestStarting before:
                if (_byTest.TryGetValue(before.Test, out var running))
                    running.BeforeAttributes.Enqueue(before.AttributeName);
                break;

            case ITestResultMessage result:
                Record(result);
                break;

            case ITestAssemblyFinished finished:
                WriteReports(finished);
                break;
        }

        return inner.OnMessage(message);
    }

    private void Record(ITestResultMessage result)
    {
        var scenario = _byTest.GetOrAdd(result.Test, test =>
        {
            var late = ProbeScenario.For(test, sawStarting: false);
            _inOrder.Enqueue(late);
            return late;
        });
        scenario.Result = result switch
        {
            ITestFailed => ExecutionResult.Failed,
            ITestSkipped => ExecutionResult.Skipped,
            _ => ExecutionResult.Passed,
        };
        if (result is ITestFailed failed)
        {
            scenario.ErrorMessage = string.Join(Environment.NewLine, failed.Messages);
            scenario.ErrorStackTrace = string.Join(Environment.NewLine, failed.StackTraces);
        }
        scenario.ExecutionTime = result.ExecutionTime;
        scenario.EndedAt = DateTimeOffset.UtcNow;

        var seen = PrototypeContext.Current;
        ProbeLog.Write("result", result.Test.DisplayName,
            $"visible={(ReferenceEquals(seen, scenario) ? "same" : seen is null ? "null" : "other:" + seen.Test.DisplayName)}");
    }

    private void WriteReports(ITestAssemblyFinished finished)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            var scenarios = _inOrder.Select(s => s.ToScenarioInfo()).ToArray();
            var options = new ReportConfigurationOptions
            {
                ReportsFolderPath = Environment.GetEnvironmentVariable("PROBE_REPORTS") ?? Path.Combine(AppContext.BaseDirectory, "Reports"),
            };
            XUnit2ReportGenerator.CreateStandardReportsWithDiagrams(scenarios, _start, DateTime.UtcNow, options);
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "prototype-error.log"), ex.ToString());
        }

        ProbeLog.Timing(finished.ExecutionTime, clock.ElapsedMilliseconds, _inOrder.Count);
        ProbeLog.Flush(_inOrder);
    }
}

/// <summary>
/// Harness stand-in for the real design's TestTrackingAttribute.Before: reads the scenario the sink put on
/// this flow and ties the test's calls to it. Without changing Kronikol, the tie is the id TestTracking's
/// Before (assembly-level, declared first) has just set on the flow: public API, no reflection.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method)]
public sealed class PrototypeTrackingAttribute : BeforeAfterTestAttribute
{
    public override void Before(MethodInfo methodUnderTest)
    {
        var scenario = PrototypeContext.Current;
        var (name, id) = XUnit2TestTrackingContext.GetCurrentTestInfo();
        var trackingRanFirst = name != "Unknown Test";
        if (scenario is not null && trackingRanFirst)
            scenario.TrackingId = id;
        ProbeLog.Write("before", scenario?.Test.DisplayName ?? "?",
            $"visible={(scenario is null ? 0 : 1)} trackingIdSeen={(trackingRanFirst ? 1 : 0)} trackingId={(trackingRanFirst ? id : "-")} method={methodUnderTest.DeclaringType?.Name}.{methodUnderTest.Name}");
    }
}
