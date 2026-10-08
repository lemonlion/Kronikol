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

    // The running test's scenario id, worked out once when it starts (TestIdOf) and read by every site that names it: the
    // fetcher, the assertion tracker's resolver, the diagram override and the scenario this class enqueues.
    private static readonly AsyncLocal<string?> CurrentTestId = new();
    private Stopwatch? _stopwatch;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void TestTrackingInitialize()
    {
        // Enable Track.That() assertions to resolve the current test ID.
        Track.TestIdResolver ??= GetCurrentTestId;
        _stopwatch = Stopwatch.StartNew();
        CurrentContext.Value = TestContext;
        CurrentTestId.Value = TestIdOf(GetType(), TestContext);
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
        var failed = TestContext.CurrentTestOutcome.ToExecutionResult() == ExecutionResult.Failed;

        DiagrammedTestRun.TestContexts.Enqueue(new MSTestScenarioInfo
        {
            TestClassSimpleName = type.Name,
            TestMethodName = TestContext.TestName!,
            TestDisplayName = TestContext.TestDisplayName,
            TestId = CurrentTestId.Value ?? TestIdOf(type, TestContext),
            Outcome = TestContext.CurrentTestOutcome,
            // What was actually thrown. Until 3.1.0 this was the constant sentence "Test failed — see
            // ErrorStackTrace for details", which made every MSTest failure in a run identical to every
            // other — the failures digest groups on the first line of this field, so a whole suite
            // collapsed into one cluster with one worked example. It also pointed the reader at
            // ErrorStackTrace, which nothing ever assigned, so the field it named was always null.
            // Gated on the outcome, not merely on there being an exception to report. `Assert.Inconclusive`
            // throws, so TestException is set on a SKIPPED test too — and a skipped scenario carrying an
            // errorMessage is the same defect this commit removed from xUnit v3, arriving by another door.
            // The gate is the mapped result rather than `== Failed`, so Error, Timeout and Aborted, which
            // are failures, keep their message.
            ErrorMessage = failed ? FailureText.OrNull(TestContext.TestException?.Message) : null,
            ErrorStackTrace = failed ? FailureText.OrNull(TestContext.TestException?.StackTrace) : null,
            FailureCause = failed ? TestContext.TestException?.GetType().Name : null,
            Endpoint = endpoint,
            IsHappyPath = isHappyPath,
            Duration = _stopwatch?.Elapsed,
            EndedAt = DateTimeOffset.UtcNow,
            ParameterNames = parameterNames
        });
    }

    internal static TestContext? GetCurrentTestContext() => CurrentContext.Value;

    /// <summary>The running test's scenario id, or null outside a test.</summary>
    internal static string? GetCurrentTestId() => CurrentTestId.Value;

    /// <summary>
    /// A test's scenario id: <c>{class}.{method}</c>, and for a data row <c>{class}.{display name}</c>. Every row of a
    /// <c>[DataRow]</c> test has the same method name, so with the method's id the report kept the first row and dropped
    /// the rest, a failing row included. A test without data keeps the id it always had, and with it its history, even
    /// when it has a display name of its own.
    /// </summary>
    internal static string TestIdOf(Type testClass, TestContext context)
    {
        var method = context.TestName is { } name ? testClass.GetMethod(name) : null;
        var isDataRow = method?.GetParameters().Length > 0;
        return isDataRow && !string.IsNullOrEmpty(context.TestDisplayName) && context.TestDisplayName != context.TestName
            ? $"{context.FullyQualifiedTestClassName}.{context.TestDisplayName}"
            : $"{context.FullyQualifiedTestClassName}.{context.TestName}";
    }
}