using System.Diagnostics;
using System.Runtime.CompilerServices;
using Reqnroll;
using Reqnroll.Events;
using Kronikol.Tracking;

namespace Kronikol.ReqNRoll;

/// <summary>
/// Reqnroll binding hooks that collect scenario and step execution information for test tracking diagrams and reports.
/// </summary>
[Binding]
public class ReqNRollTrackingHooks
{
    private readonly ScenarioContext _scenarioContext;
    private readonly FeatureContext _featureContext;
    private Stopwatch? _stopwatch;

    // Track which event publishers we've already subscribed to, so we only subscribe
    // once per test-thread publisher. Without this, BeforeScenario would add a new
    // handler on every scenario, causing attachments to be duplicated N times
    // (where N is the number of scenarios that have run on that thread).
    private static readonly ConditionalWeakTable<ITestThreadExecutionEventPublisher, object> SubscribedPublishers = new();

    public ReqNRollTrackingHooks(ScenarioContext scenarioContext, FeatureContext featureContext)
    {
        _scenarioContext = scenarioContext;
        _featureContext = featureContext;
    }

    /// <summary>
    /// True on the hook instance that initialized this scenario. When both the base and a
    /// derived [Binding] class are discovered (both assemblies listed in reqnroll.json's
    /// bindingAssemblies), ReqNRoll runs every hook on one instance of each class; only the
    /// owner records anything, so steps and scenarios are never captured twice.
    /// </summary>
    private bool IsOwner =>
        _scenarioContext.TryGetValue(ReqNRollConstants.OwnerHooksKey, out object? owner)
        && ReferenceEquals(owner, this);

    [BeforeScenario(Order = int.MinValue)]
    public void BeforeScenario()
    {
        // Idempotency guard: if hooks have already run for this scenario (e.g. both base
        // and derived [Binding] classes discovered), skip duplicate execution. The instance
        // that passes becomes the scenario's owner; every other hook no-ops on non-owners.
        if (_scenarioContext.ContainsKey(ReqNRollConstants.ScenarioRuntimeIdKey))
            return;

        _stopwatch = Stopwatch.StartNew();
        var scenarioId = Guid.NewGuid().ToString();
        _scenarioContext[ReqNRollConstants.ScenarioRuntimeIdKey] = scenarioId;
        _scenarioContext[ReqNRollConstants.OwnerHooksKey] = this;
        _scenarioContext[ReqNRollConstants.StepsCollectionKey] = new List<ReqNRollStepInfo>();
        ReqNRollTestContext.CurrentTestInfo = (_scenarioContext.ScenarioInfo.Title, scenarioId);

        // Subscribe to AttachmentAddedEvent to capture attachments for the report.
        // We use the event system instead of wrapping IReqnrollOutputHelper because
        // BoDi's RegisterInstanceAs throws if the type has already been resolved,
        // and swallowing that exception silently caused attachments to never be captured.
        // Only subscribe once per test-thread publisher to avoid duplicate attachments.
        try
        {
            var eventPublisher = _scenarioContext.ScenarioContainer.Resolve<ITestThreadExecutionEventPublisher>();
            var alreadySubscribed = true;
            SubscribedPublishers.GetValue(eventPublisher, _ => { alreadySubscribed = false; return new object(); });
            if (!alreadySubscribed)
            {
                eventPublisher.AddHandler<AttachmentAddedEvent>(evt =>
                {
                    var testId = ReqNRollTestContext.CurrentTestInfo?.Id;
                    if (testId is not null)
                        StepCollector.AddAttachment(testId, evt.FilePath, name: null);
                });
            }
        }
        catch
        {
            // Event publisher may not be available in all test runner configurations
        }
    }

    [BeforeStep(Order = int.MinValue)]
    public void BeforeStep()
    {
        if (!IsOwner)
            return;

        var stepType = _scenarioContext.StepContext.StepInfo.StepInstance.StepDefinitionKeyword.ToString();
        ReqNRollTestContext.CurrentStepType = stepType;
        TestPhaseContext.Current = PhaseConfiguration.ResolvePhaseFromStepType(stepType);
        _scenarioContext[ReqNRollConstants.StepStopwatchKey] = Stopwatch.StartNew();

        // Push a step onto StepCollector so assertion tracking sub-steps attach here
        var scenarioId = (string)_scenarioContext[ReqNRollConstants.ScenarioRuntimeIdKey];
        StepCollector.StartStep(scenarioId, stepType, _scenarioContext.StepContext.StepInfo.Text, null, null);
    }

    [AfterStep(Order = int.MaxValue)]
    public void AfterStep()
    {
        if (!IsOwner)
            return;

        var stepStopwatch = (Stopwatch)_scenarioContext[ReqNRollConstants.StepStopwatchKey];
        stepStopwatch.Stop();
        var stepContext = _scenarioContext.StepContext;
        var steps = (List<ReqNRollStepInfo>)_scenarioContext[ReqNRollConstants.StepsCollectionKey];
        var tableText = stepContext.StepInfo.Table?.ToString();
        var docString = stepContext.StepInfo.MultilineText;

        // Capture inline parameter positions from binding match
        InlineParamCapture[]? inlineParams = null;
        var bindingMatch = stepContext.StepInfo.BindingMatch;
        if (bindingMatch is { Success: true, Arguments.Length: > 0 })
        {
            var stepText = stepContext.StepInfo.Text;
            var captures = new List<InlineParamCapture>();
            var methodParams = bindingMatch.StepBinding?.Method?.Parameters?.ToArray();
            for (var i = 0; i < bindingMatch.Arguments.Length; i++)
            {
                var arg = bindingMatch.Arguments[i];
                if (arg.StartOffset is not null && arg.Value is string strValue)
                {
                    var offset = arg.StartOffset.Value;
                    var length = strValue.Length;
                    if (offset >= 0 && offset + length <= stepText.Length)
                    {
                        string? name = null;
                        if (methodParams != null && i < methodParams.Length)
                            name = methodParams[i].ParameterName;
                        captures.Add(new InlineParamCapture(offset, length, strValue, name));
                    }
                }
            }
            if (captures.Count > 0)
                inlineParams = captures.ToArray();
        }

        // Pop the StepCollector step so assertion sub-steps are captured
        var scenarioId = (string)_scenarioContext[ReqNRollConstants.ScenarioRuntimeIdKey];
        var passed = stepContext.Status == ScenarioExecutionStatus.OK;
        StepCollector.CompleteStep(scenarioId, passed);

        steps.Add(new ReqNRollStepInfo(
            stepContext.StepInfo.StepInstance.StepDefinitionKeyword.ToString(),
            stepContext.StepInfo.Text,
            stepContext.Status,
            stepStopwatch.Elapsed,
            tableText,
            docString,
            inlineParams));
    }

    [AfterScenario(Order = int.MaxValue)]
    public void AfterScenario()
    {
        if (!IsOwner)
            return;

        _stopwatch?.Stop();
        var scenarioId = (string)_scenarioContext[ReqNRollConstants.ScenarioRuntimeIdKey];
        var steps = (List<ReqNRollStepInfo>)_scenarioContext[ReqNRollConstants.StepsCollectionKey];

        var arguments = _scenarioContext.ScenarioInfo.Arguments;
        Dictionary<string, string>? exampleValues = null;
        Dictionary<string, object?>? exampleRawValues = null;
        Dictionary<string, string>? exampleFlatValues = null;
        if (arguments is { Count: > 0 })
        {
            var flatValues = new Dictionary<string, string>();
            foreach (System.Collections.DictionaryEntry entry in arguments)
                flatValues[entry.Key?.ToString() ?? ""] = entry.Value?.ToString() ?? "";

            // Build structured ExampleValues/ExampleRawValues by grouping flat columns
            // based on step table data
            (exampleValues, exampleRawValues) = ExampleValueGrouper.BuildStructured(flatValues, steps);

            // Preserve original flat values when grouping changed the key set
            // (enables the flatten/unflatten toggle in the report)
            if (exampleRawValues is not null && !exampleValues.Keys.OrderBy(k => k).SequenceEqual(flatValues.Keys.OrderBy(k => k)))
                exampleFlatValues = flatValues;
        }

        var examplesBlock = exampleValues is not null
            ? ExamplesBlockResolver.Resolve(_featureContext.FeatureInfo, _scenarioContext.ScenarioInfo)
            : ExamplesBlock.None;

        ReqNRollScenarioCollector.Collect(new ReqNRollScenarioInfo
        {
            ScenarioId = scenarioId,
            ScenarioTitle = _scenarioContext.ScenarioInfo.Title,
            ScenarioDescription = _scenarioContext.ScenarioInfo.Description,
            FeatureTitle = _featureContext.FeatureInfo.Title,
            FeatureDescription = _featureContext.FeatureInfo.Description,
            ScenarioTags = _scenarioContext.ScenarioInfo.Tags,
            CombinedTags = _scenarioContext.ScenarioInfo.CombinedTags,
            TestError = _scenarioContext.TestError,
            ExecutionStatus = _scenarioContext.ScenarioExecutionStatus,
            Duration = _stopwatch?.Elapsed,
            Steps = steps,
            Rule = _scenarioContext.RuleInfo?.Title,
            OutlineId = exampleValues is not null ? _scenarioContext.ScenarioInfo.Title : null,
            ExampleValues = exampleValues,
            ExampleRawValues = exampleRawValues,
            ExampleFlatValues = exampleFlatValues,
            ExamplesBlockName = examplesBlock.Name,
            ExamplesBlockDescription = examplesBlock.Description,
            ExamplesBlockIndex = examplesBlock.Index
        });

        ReqNRollTestContext.CurrentTestInfo = null;
    }
}