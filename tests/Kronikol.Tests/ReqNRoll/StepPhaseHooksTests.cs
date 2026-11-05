using System.Collections.Specialized;
using System.Globalization;
using System.Reflection;
using Kronikol.ReqNRoll;
using Kronikol.Tracking;
using Reqnroll;
using Reqnroll.Bindings;
using Reqnroll.BoDi;
using Reqnroll.Configuration;
using Reqnroll.Infrastructure;

namespace Kronikol.Tests.ReqNRoll;

/// <summary>
/// #195 and #224: the tracking hooks, run on real Reqnroll contexts, give a Gherkin step's keyword as its label and the type
/// Reqnroll resolved as its phase (plans/STEP_PHASE_PLAN.md 5.3). Reqnroll's context constructors are internal, so they are
/// called by reflection, as Reqnroll's own runner calls them.
/// </summary>
[Collection("DiagramsFetcher")]
public class StepPhaseHooksTests
{
    private sealed class OneStepAtATime : IContextManager
    {
        public ScenarioStepContext? Step { get; set; }
        public TestThreadContext TestThreadContext => throw new NotSupportedException();
        public FeatureContext FeatureContext => throw new NotSupportedException();
        public ScenarioContext ScenarioContext => throw new NotSupportedException();
        public ScenarioStepContext StepContext => Step!;
        public StepDefinitionType? CurrentTopLevelStepDefinitionType => null;
        public void InitializeFeatureContext(FeatureInfo featureInfo) => throw new NotSupportedException();
        public void CleanupFeatureContext() => throw new NotSupportedException();
        public void InitializeScenarioContext(ScenarioInfo scenarioInfo, RuleInfo ruleInfo) => throw new NotSupportedException();
        public void CleanupScenarioContext() => throw new NotSupportedException();
        public void InitializeStepContext(StepInfo stepInfo) => throw new NotSupportedException();
        public void CleanupStepContext() => throw new NotSupportedException();
    }

    private static T Internal<T>(params object?[] args) =>
        (T)Activator.CreateInstance(typeof(T), BindingFlags.Instance | BindingFlags.NonPublic, null, args, CultureInfo.InvariantCulture)!;

    private sealed class Scenario : IDisposable
    {
        private readonly OneStepAtATime _steps = new();
        private readonly ReqNRollTrackingHooks _hooks;

        public Scenario()
        {
            TestPhaseContext.Reset();
            var container = new ObjectContainer();
            container.RegisterInstanceAs<IContextManager>(_steps);
            var feature = new FeatureInfo(CultureInfo.InvariantCulture, "Features", "Phases", "", ProgrammingLanguage.CSharp, []);
            var featureContext = Internal<FeatureContext>(new ObjectContainer(), feature, ConfigurationLoader.GetDefault());
            var scenarioContext = Internal<ScenarioContext>(container, new ScenarioInfo("A scenario", null, [], new OrderedDictionary(), [], "0"), null, null);
            _hooks = new ReqNRollTrackingHooks(scenarioContext, featureContext);
            _hooks.BeforeScenario();
            Id = ReqNRollTestContext.CurrentTestInfo!.Value.Id;
        }

        public string Id { get; }

        public void BeforeStep(StepDefinitionKeyword keyword, StepDefinitionType type)
        {
            var info = new StepInfo(type, "a step", null, null);
            typeof(StepInfo).GetProperty(nameof(StepInfo.StepInstance))!.SetValue(info, new StepInstance(type, keyword, keyword + " ", "a step", null!));
            _steps.Step = Internal<ScenarioStepContext>(info);
            _hooks.BeforeStep();
        }

        public void AfterStep() => _hooks.AfterStep();

        public void Dispose() => _hooks.AfterScenario();
    }

    [Fact]
    public void Every_keyword_as_written_reads_the_type_Reqnroll_resolved_and_the_step_list_keeps_the_keyword()
    {
        (StepDefinitionKeyword Keyword, StepDefinitionType Type, TestPhase Phase)[] rows =
        [
            (StepDefinitionKeyword.Given, StepDefinitionType.Given, TestPhase.Setup),
            (StepDefinitionKeyword.And, StepDefinitionType.Given, TestPhase.Setup),
            (StepDefinitionKeyword.But, StepDefinitionType.Given, TestPhase.Setup),
            (StepDefinitionKeyword.When, StepDefinitionType.When, TestPhase.Action),
            (StepDefinitionKeyword.And, StepDefinitionType.When, TestPhase.Action),
            (StepDefinitionKeyword.But, StepDefinitionType.When, TestPhase.Action),
            (StepDefinitionKeyword.Then, StepDefinitionType.Then, TestPhase.Action),
            (StepDefinitionKeyword.And, StepDefinitionType.Then, TestPhase.Action),
            (StepDefinitionKeyword.But, StepDefinitionType.Then, TestPhase.Action),
        ];
        using var scenario = new Scenario();

        foreach (var (keyword, type, phase) in rows)
        {
            scenario.BeforeStep(keyword, type);
            Assert.Equal(phase, TestPhaseContext.Current);
            scenario.AfterStep();
            Assert.Equal(phase, TestPhaseContext.Current);
        }

        Assert.Equal(rows.Select(row => row.Keyword.ToString()), StepCollector.GetSteps(scenario.Id).Select(step => step.Keyword));
    }

    [Fact]
    public void The_keyword_as_written_stays_public_for_a_consumers_hook()
    {
        using var scenario = new Scenario();

        scenario.BeforeStep(StepDefinitionKeyword.And, StepDefinitionType.When);

        Assert.Equal("And", ReqNRollTestContext.CurrentStepType);
        scenario.AfterStep();
    }

    [Theory]
    [InlineData(StepDefinitionKeyword.When, StepDefinitionType.When, "Given", TestPhase.Action)]
    [InlineData(StepDefinitionKeyword.Given, StepDefinitionType.Given, "When", TestPhase.Setup)]
    public void A_step_attribute_between_the_hooks_is_a_label(StepDefinitionKeyword keyword, StepDefinitionType type, string nested, TestPhase phase)
    {
        using var scenario = new Scenario();
        scenario.BeforeStep(keyword, type);

        StepCollector.StartStep(scenario.Id, nested, "nested", null, null);
        Assert.Equal(phase, TestPhaseContext.Current);
        StepCollector.CompleteStep(scenario.Id, passed: true);
        Assert.Equal(phase, TestPhaseContext.Current);

        scenario.AfterStep();
        Assert.Equal(phase, TestPhaseContext.Current);
    }

    // A nested Gherkin step through an injected ITestRunner fires the hooks once, as a label (matrix/N01 to N08).
    [Fact]
    public void A_nested_Gherkin_step_is_a_label()
    {
        using var scenario = new Scenario();
        scenario.BeforeStep(StepDefinitionKeyword.When, StepDefinitionType.When);

        scenario.BeforeStep(StepDefinitionKeyword.Given, StepDefinitionType.Given);

        Assert.Equal(TestPhase.Action, TestPhaseContext.Current);
        scenario.AfterStep();
        scenario.AfterStep();
    }

    [Fact]
    public void A_first_position_And_reads_the_type_Reqnroll_resolved()
    {
        using var scenario = new Scenario();

        scenario.BeforeStep(StepDefinitionKeyword.And, StepDefinitionType.Given);

        Assert.Equal(TestPhase.Setup, TestPhaseContext.Current);
        scenario.AfterStep();
    }
}
