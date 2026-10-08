using System.Reflection;
using Kronikol.Tracking;
using Xunit.Sdk;

namespace Kronikol.xUnit2;

/// <summary>
/// xUnit v2 <see cref="BeforeAfterTestAttribute"/> that sets the current test identity
/// in <see cref="AsyncLocal{T}"/> before each test runs, and collects test metadata for reports.
/// <para>
/// Apply this attribute to individual test classes, or apply it at the assembly level
/// to cover all tests: <c>[assembly: TestTracking]</c>.
/// </para>
/// <para>
/// When using <see cref="DiagrammedComponentTest"/> as a base class, this attribute
/// is already applied automatically.
/// </para>
/// <para>
/// Under <see cref="ReportingTestFramework"/> the scenario is made when xUnit starts the test, and this
/// attribute ties the calls the test makes to it, from <c>Before</c> to <c>After</c>.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class TestTrackingAttribute : BeforeAfterTestAttribute
{
    public override void Before(MethodInfo methodUnderTest)
    {
        // Enable Track.That() assertions to resolve the current test ID.
        Track.TestIdResolver ??= () =>
        {
            var (_, id) = XUnit2TestTrackingContext.GetCurrentTestInfo();
            return string.Equals(id, "unknown", StringComparison.OrdinalIgnoreCase) ? null : id;
        };

        var className = (methodUnderTest.ReflectedType ?? methodUnderTest.DeclaringType)?.Name ?? TestIdentityScope.UnknownTestName;
        var methodName = methodUnderTest.Name;

        // Kronikol's results sink made this test's scenario when xUnit sent its ITestStarting, and keyed its
        // result to it, so the calls only need its id. A test can have the attribute more than once (assembly,
        // class, method), and each Before finds the same scenario.
        if (XUnit2TestTrackingContext.HandedOverScenarioFor(methodUnderTest) is { } handedOver)
        {
            handedOver.TakenByBefore = true;
            XUnit2TestTrackingContext.SetCurrentTest($"{className}.{methodName}", handedOver.Id);
            return;
        }

        // No sink handed one over: the reports are written some other way (a collection fixture), or the bus
        // did not deliver on the test's flow. The scenario is this attribute's own, and its result, if any, is
        // found by name (plans/XUNIT2_FRAMEWORK_COMPOSITION_PLAN.md 4.4).
        var testId = Guid.NewGuid().ToString();
        var featureName = ScenarioTitleResolver.FormatFeatureName(className);
        var scenarioName = ScenarioTitleResolver.FormatScenarioDisplayName(methodName);

        XUnit2TestTrackingContext.SetCurrentTest($"{className}.{methodName}", testId);

        var endpointAttr = (methodUnderTest.ReflectedType ?? methodUnderTest.DeclaringType)?
            .GetCustomAttributes(inherit: true)
            .OfType<EndpointAttribute>()
            .FirstOrDefault();

        var isHappyPath = methodUnderTest
            .GetCustomAttributes(inherit: true)
            .OfType<HappyPathAttribute>()
            .Any();

        var methodMatchKey = $"{(methodUnderTest.ReflectedType ?? methodUnderTest.DeclaringType)!.FullName}.{methodUnderTest.Name}";

        XUnit2TestTrackingContext.CollectedScenarios[testId] = new ScenarioInfo
        {
            Id = testId,
            FeatureName = featureName,
            ScenarioName = scenarioName,
            MethodMatchKey = methodMatchKey,
            Endpoint = endpointAttr?.Endpoint,
            IsHappyPath = isHappyPath,
        };
    }

    public override void After(MethodInfo methodUnderTest)
    {
        XUnit2TestTrackingContext.ClearCurrentTest();
    }
}
