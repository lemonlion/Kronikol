using System.Diagnostics;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Kronikol.Tracking;
using Kronikol.Reports;

namespace Kronikol.MSTest;

/// <summary>
/// Abstract base class for MSTest component tests that integrates with the test tracking diagram system to capture test execution context and timing.
/// </summary>
public abstract class DiagrammedComponentTest
{
    private static readonly AsyncLocal<TestContext?> CurrentContext = new();
    private Stopwatch? _stopwatch;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void TestTrackingInitialize()
    {
        // Enable Track.That() assertions to resolve the current test ID.
        Track.TestIdResolver ??= () =>
        {
            var ctx = GetCurrentTestContext();
            return ctx is not null ? $"{ctx.FullyQualifiedTestClassName}.{ctx.TestName}" : null;
        };
        _stopwatch = Stopwatch.StartNew();
        CurrentContext.Value = TestContext;
    }

    [TestCleanup]
    public void TestTrackingCleanup()
    {
        _stopwatch?.Stop();
        var type = GetType();
        var endpoint = type.GetCustomAttribute<EndpointAttribute>()?.Endpoint;
        var methodInfo = type.GetMethod(TestContext.TestName!);
        var isHappyPath = methodInfo?.GetCustomAttribute<HappyPathAttribute>() is not null;
        var parameterNames = methodInfo?.GetParameters().Select(p => p.Name).ToArray();

        DiagrammedTestRun.TestContexts.Enqueue(new MSTestScenarioInfo
        {
            TestClassSimpleName = type.Name,
            TestMethodName = TestContext.TestName!,
            TestDisplayName = TestContext.TestDisplayName,
            TestId = $"{TestContext.FullyQualifiedTestClassName}.{TestContext.TestName}",
            Outcome = TestContext.CurrentTestOutcome,
            // What was actually thrown. Until 3.1.0 this was the constant sentence "Test failed — see
            // ErrorStackTrace for details", which made every MSTest failure in a run identical to every
            // other — the failures digest groups on the first line of this field, so a whole suite
            // collapsed into one cluster with one worked example. It also pointed the reader at
            // ErrorStackTrace, which nothing ever assigned, so the field it named was always null.
            ErrorMessage = FailureText.OrNull(TestContext.TestException?.Message),
            ErrorStackTrace = FailureText.OrNull(TestContext.TestException?.StackTrace),
            FailureCause = TestContext.CurrentTestOutcome == UnitTestOutcome.Failed
                ? TestContext.TestException?.GetType().Name
                : null,
            Endpoint = endpoint,
            IsHappyPath = isHappyPath,
            Duration = _stopwatch?.Elapsed,
            ParameterNames = parameterNames
        });
    }

    internal static TestContext? GetCurrentTestContext() => CurrentContext.Value;
}