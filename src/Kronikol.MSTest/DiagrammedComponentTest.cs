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
        var methodInfo = TestMethodOf(type, TestContext);
        var isHappyPath = methodInfo?.GetCustomAttribute<HappyPathAttribute>() is not null;
        var parameterNames = methodInfo?.GetParameters().Select(p => p.Name).ToArray();
        var failed = TestContext.CurrentTestOutcome.ToExecutionResult() == ExecutionResult.Failed;
        var (featureTags, tags) = TestCategories(type, methodInfo);

        DiagrammedTestRun.TestContexts.Enqueue(new MSTestScenarioInfo
        {
            TestClassSimpleName = type.Name,
            TestMethodName = TestContext.TestName!,
            TestDisplayName = TestContext.TestDisplayName,
            MethodDisplayName = methodInfo?.GetCustomAttribute<TestMethodAttribute>()?.DisplayName,
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
            ParameterNames = parameterNames,
            FeatureTags = featureTags,
            Tags = tags
        });
    }

    /// <summary>
    /// A test's <c>[TestCategory]</c> values by level (plans/TAGS_PLAN.md §4.7): the class's, its base classes' included,
    /// then the assembly's (Q12) are the feature's; the method's are the scenario's. Any
    /// <see cref="TestCategoryBaseAttribute"/> counts, as MSTest counts it.
    /// </summary>
    internal static (string[]? Feature, string[]? Scenario) TestCategories(Type type, MethodInfo? method)
    {
        var feature = type.GetCustomAttributes<TestCategoryBaseAttribute>(inherit: true)
            .Concat(type.Assembly.GetCustomAttributes<TestCategoryBaseAttribute>())
            .SelectMany(a => a.TestCategories)
            .ToArray();
        var scenario = method?.GetCustomAttributes<TestCategoryBaseAttribute>(inherit: true).SelectMany(a => a.TestCategories).ToArray() ?? [];
        return (feature.Length > 0 ? feature : null, scenario.Length > 0 ? scenario : null);
    }

    internal static TestContext? GetCurrentTestContext() => CurrentContext.Value;

    /// <summary>The running test's scenario id, or null outside a test.</summary>
    internal static string? GetCurrentTestId() => CurrentTestId.Value;

    /// <summary>
    /// A test's scenario id: <c>{class}.{method}</c>, and for a data row <c>{class}.{display name}</c> when MSTest named
    /// the row after its method (<c>Place (1)</c>), or <c>{class}.{method} ({display name})</c> when the row has a display
    /// name of its own. Every row of a <c>[DataRow]</c> test has the same method name, so with the method's id the report
    /// kept the first row and dropped the rest, a failing row included; and a display name of its own, without the method,
    /// gave rows of two methods with the same display name one id (4.13.3). A test without data keeps the id it always had,
    /// and with it its history, even when it has a display name of its own.
    /// </summary>
    internal static string TestIdOf(Type testClass, TestContext context)
    {
        var name = context.TestName;
        var display = context.TestDisplayName;
        var isDataRow = TestMethodOf(testClass, context)?.GetParameters().Length > 0;
        if (!isDataRow || name is null || string.IsNullOrEmpty(display) || display == name)
            return $"{context.FullyQualifiedTestClassName}.{name}";
        return display.StartsWith(name + " (", StringComparison.Ordinal) || display.StartsWith(name + "(", StringComparison.Ordinal)
            ? $"{context.FullyQualifiedTestClassName}.{display}"
            : $"{context.FullyQualifiedTestClassName}.{name} ({display})";
    }

    /// <summary>
    /// The running test's method. By name and by the arguments its row passes (<c>TestData</c>, null without data), where
    /// <c>GetMethod(name)</c> threw <c>AmbiguousMatchException</c> for a class that overloads a test method, failing each
    /// overload in <c>[TestCleanup]</c> (measured on 4.13.3). Of overloads taking as many arguments, the one whose
    /// parameters accept the row's values; a row that matches no count passes its values to a <c>params</c> array.
    /// </summary>
    internal static MethodInfo? TestMethodOf(Type testClass, TestContext context)
    {
        var candidates = testClass.GetMethods().Where(m => m.Name == context.TestName).ToArray();
        if (candidates.Length <= 1)
            return candidates.FirstOrDefault();
        var data = context.TestData ?? [];
        var sameCount = candidates.Where(m => m.GetParameters().Length == data.Length).ToArray();
        return sameCount.FirstOrDefault(m => Accepts(m, data))
            ?? sameCount.FirstOrDefault()
            ?? (data.Length > 0 ? candidates.FirstOrDefault(TakesParams) : null)
            ?? candidates[0];
    }

    private static bool Accepts(MethodInfo method, object?[] data) =>
        method.GetParameters().Zip(data).All(p => p.Second is null
            ? !p.First.ParameterType.IsValueType || Nullable.GetUnderlyingType(p.First.ParameterType) is not null
            : p.First.ParameterType.IsInstanceOfType(p.Second));

    private static bool TakesParams(MethodInfo method) =>
        method.GetParameters() is [.., var last] && last.IsDefined(typeof(ParamArrayAttribute), inherit: false);
}