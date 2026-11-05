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
    private bool _collected;

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
        // The row's name as Gherkin names its pickle (#101), resolved once, before any step: the test identity every call
        // carries and the row the report shows have one name.
        var rowName = ExamplesBlockResolver.ResolveRowName(_featureContext.FeatureInfo, _scenarioContext.ScenarioInfo);
        _scenarioContext[ReqNRollConstants.RowNameKey] = rowName;
        ReqNRollTestContext.CurrentTestInfo = (rowName, scenarioId);

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
                // A step Reqnroll runs no hooks for (skipped after a failure, undefined) is known only here. Reqnroll runs this
                // inline, so a throw would fail the step it reports on.
                eventPublisher.AddHandler<StepFinishedEvent>(evt =>
                {
                    if (evt.ScenarioContext is ScenarioContext scenario)
                    {
                        try
                        {
                            NoteStepThatDidNotRun(scenario);
                        }
                        catch
                        {
                            // The step is missing from the report, as it was before this handler.
                        }
                    }
                });

                // A suite's AfterScenario hook that throws stops ours, and Reqnroll 3.0.1 runs none for a scenario ignored at
                // run time, but this is published either way. Reqnroll runs it inline, so a throw would replace the test's failure.
                eventPublisher.AddHandler<ScenarioFinishedEvent>(evt =>
                {
                    if (evt.ScenarioContext is ScenarioContext scenario
                        && scenario.TryGetValue(ReqNRollConstants.OwnerHooksKey, out object? owner)
                        && owner is ReqNRollTrackingHooks hooks)
                    {
                        try
                        {
                            hooks.CollectScenario();
                        }
                        catch
                        {
                            // The scenario is lost from the report, as it was before this handler.
                        }
                    }
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
        _scenarioContext[ReqNRollConstants.StepStopwatchKey] = Open(stepType).Stopwatch;
    }

    private OpenStep Open(string stepType)
    {
        var stopwatch = Stopwatch.StartNew();

        // Push a step onto StepCollector so assertion tracking sub-steps attach here. The step's
        // Gherkin arguments ride along so the delimiter bar can draw them — the table Reqnroll hands
        // an outline step already has its <placeholders> substituted.
        var scenarioId = (string)_scenarioContext[ReqNRollConstants.ScenarioRuntimeIdKey];
        var stepInfo = _scenarioContext.StepContext.StepInfo;
        // The keyword as written is the step's label. Its phase is the type Reqnroll resolved, And and But to the keyword
        // they continue (#195).
        var frame = StepCollector.StartFrame(scenarioId, stepType, stepInfo.Text,
            phaseKeyword: stepInfo.StepDefinitionType.ToString(),
            table: ReqNRollStepArguments.ToBarRows(stepInfo.Table), docString: stepInfo.MultilineText);

        var opened = new OpenStep(stopwatch, InnermostOpenStep, stepInfo, frame);
        _scenarioContext[ReqNRollConstants.OpenStepKey] = opened;
        _scenarioContext.StepContext[ReqNRollConstants.OpenStepKey] = opened;
        return opened;
    }

    [AfterStep(Order = int.MaxValue)]
    public void AfterStep()
    {
        if (!IsOwner)
            return;

        // None when a suite's hook sorted ahead of BeforeStep and threw: Reqnroll still runs this, and the step's finished
        // event, which comes first, has listed it.
        var stepContext = _scenarioContext.StepContext;
        if (!stepContext.TryGetValue(ReqNRollConstants.OpenStepKey, out OpenStep? opened) || opened is null)
            return;
        opened.Stopwatch.Stop();
        var steps = (List<ReqNRollStepInfo>)_scenarioContext[ReqNRollConstants.StepsCollectionKey];

        // Pop the StepCollector step so assertion sub-steps are captured
        var scenarioId = (string)_scenarioContext[ReqNRollConstants.ScenarioRuntimeIdKey];
        // Reqnroll opens a step only while the scenario is well, so the scenario failing by now failed in this step. It writes a
        // failure only to the step that failed, the step that ran it carries on, and a step that never reached a hook
        // (undefined, ambiguous) has no frame at all.
        var status = stepContext.Status != ScenarioExecutionStatus.OK ? stepContext.Status : _scenarioContext.ScenarioExecutionStatus;
        CloseLeftOpen(opened, scenarioId, steps);
        StepCollector.CompleteFrame(scenarioId, opened.Frame, status == ScenarioExecutionStatus.OK);

        if (opened.Parent is null)
            _scenarioContext.Remove(ReqNRollConstants.OpenStepKey);
        else
            _scenarioContext[ReqNRollConstants.OpenStepKey] = opened.Parent;

        steps.Add(Describe(stepContext.StepInfo, status, opened));
    }

    /// <summary>
    /// A step Reqnroll ran no hooks of ours for (skipped after a failure, undefined, or a suite's BeforeStep hook sorted ahead of
    /// ours threw): listed as it came in the scenario, after the step opened before it, and under the step that ran it if one did.
    /// </summary>
    private static void NoteStepThatDidNotRun(ScenarioContext scenario)
    {
        var stepContext = scenario.StepContext;
        if (stepContext.ContainsKey(ReqNRollConstants.OpenStepKey)
            || !scenario.TryGetValue(ReqNRollConstants.ScenarioRuntimeIdKey, out string? scenarioId))
            return;

        // StepInstance is set after the match on Reqnroll 3.0.1, so an undefined step has none there.
        var keyword = stepContext.StepInfo.StepInstance?.StepDefinitionKeyword.ToString() ?? stepContext.StepInfo.StepDefinitionType.ToString();
        ((List<ReqNRollStepInfo>)scenario[ReqNRollConstants.StepsCollectionKey]).Add(new ReqNRollStepInfo(
            keyword,
            stepContext.StepInfo.Text,
            stepContext.Status,
            null,
            stepContext.StepInfo.Table?.ToString(),
            stepContext.StepInfo.MultilineText)
        {
            NotRunAfter = StepCollector.LastStepIdOf(scenarioId),
            NotRunUnder = StepCollector.OpenFrameOf(scenarioId),
            // A failure Reqnroll recorded stopped it. A step that skipped itself, or one that failed and was caught, did not.
            AfterFailure = stepContext.Status is not (ScenarioExecutionStatus.TestError or ScenarioExecutionStatus.BindingError)
                           && scenario.ScenarioExecutionStatus is ScenarioExecutionStatus.TestError or ScenarioExecutionStatus.BindingError,
        });
    }

    private static ReqNRollStepInfo Describe(StepInfo stepInfo, ScenarioExecutionStatus status, OpenStep opened)
    {
        // Capture inline parameter positions from binding match
        InlineParamCapture[]? inlineParams = null;
        var bindingMatch = stepInfo.BindingMatch;
        if (bindingMatch is { Success: true, Arguments.Length: > 0 })
        {
            var stepText = stepInfo.Text;
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

        return new ReqNRollStepInfo(
            stepInfo.StepInstance.StepDefinitionKeyword.ToString(),
            stepInfo.Text,
            status,
            opened.Stopwatch.Elapsed,
            stepInfo.Table?.ToString(),
            stepInfo.MultilineText,
            inlineParams) { FrameId = opened.Frame };
    }

    /// <summary>
    /// A suite's AfterStep hook that threw kept ours from running, as Reqnroll stops a type's hooks at the first throw. Those steps
    /// are the ones that failed, and nothing else is left to list them. They are closed when the step that ran them closes, or
    /// as the scenario's AfterScenario hooks begin.
    /// </summary>
    private void CloseLeftOpen(OpenStep? upTo, string scenarioId, List<ReqNRollStepInfo> steps)
    {
        for (var left = InnermostOpenStep; left is not null && !ReferenceEquals(left, upTo); left = left.Parent)
        {
            left.Stopwatch.Stop();
            StepCollector.CompleteFrame(scenarioId, left.Frame, false);
            steps.Add(Describe(left.StepInfo, _scenarioContext.ScenarioExecutionStatus, left));
        }
    }

    private OpenStep? InnermostOpenStep =>
        _scenarioContext.TryGetValue(ReqNRollConstants.OpenStepKey, out OpenStep? open) ? open : null;

    /// <summary>A step under way. Its definition can run more of them, which finish inside it.</summary>
    private sealed class OpenStep(Stopwatch stopwatch, OpenStep? parent, StepInfo stepInfo, int? frame)
    {
        public Stopwatch Stopwatch { get; } = stopwatch;
        public OpenStep? Parent { get; } = parent;
        public StepInfo StepInfo { get; } = stepInfo;
        public int? Frame { get; } = frame;
    }

    // Ahead of a suite's own at any later order, so a step left open takes neither their time nor their woven steps, and they
    // find it in the list. AfterScenario comes back to it for a step one of their hooks ran and left open.
    [AfterScenario(Order = int.MinValue)]
    internal void CloseStepsLeftOpen()
    {
        if (!IsOwner || InnermostOpenStep is null)
            return;

        CloseLeftOpen(null, (string)_scenarioContext[ReqNRollConstants.ScenarioRuntimeIdKey], (List<ReqNRollStepInfo>)_scenarioContext[ReqNRollConstants.StepsCollectionKey]);
        _scenarioContext.Remove(ReqNRollConstants.OpenStepKey);
    }

    [AfterScenario(Order = int.MaxValue)]
    public void AfterScenario()
    {
        if (!IsOwner)
            return;

        CollectScenario();
    }

    private void CollectScenario()
    {
        if (_collected)
            return;
        _collected = true;

        _stopwatch?.Stop();
        CloseStepsLeftOpen();
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
        // Unconditional, unlike the Examples: lookup - every scenario has a source, outline or not.
        var source = ExamplesBlockResolver.ResolveSource(_featureContext.FeatureInfo, _scenarioContext.ScenarioInfo);

        ReqNRollScenarioCollector.Collect(new ReqNRollScenarioInfo
        {
            ScenarioId = scenarioId,
            ScenarioTitle = _scenarioContext.ScenarioInfo.Title,
            ScenarioDescription = _scenarioContext.ScenarioInfo.Description,
            FeatureTitle = _featureContext.FeatureInfo.Title,
            FeatureDescription = _featureContext.FeatureInfo.Description,
            ScenarioTags = _scenarioContext.ScenarioInfo.Tags,
            CombinedTags = _scenarioContext.ScenarioInfo.CombinedTags,
            FeatureTags = _featureContext.FeatureInfo.Tags,
            TestError = _scenarioContext.TestError,
            ExecutionStatus = _scenarioContext.ScenarioExecutionStatus,
            Duration = _stopwatch?.Elapsed,
            EndedAt = DateTimeOffset.UtcNow,
            Steps = steps,
            Rule = _scenarioContext.RuleInfo?.Title,
            OutlineId = exampleValues is not null ? _scenarioContext.ScenarioInfo.Title : null,
            ExampleValues = exampleValues,
            ExampleRawValues = exampleRawValues,
            ExampleFlatValues = exampleFlatValues,
            ExamplesBlockName = examplesBlock.Name,
            ExamplesBlockDescription = examplesBlock.Description,
            ExamplesBlockIndex = examplesBlock.Index,
            SourceFile = source.File,
            SourceLine = source.Line,
            WrittenBackground = ExamplesBlockResolver.ResolveBackgroundSteps(_featureContext.FeatureInfo, _scenarioContext.ScenarioInfo),
            RowName = _scenarioContext.TryGetValue(ReqNRollConstants.RowNameKey, out string? rowName)
                ? rowName
                : ExamplesBlockResolver.ResolveRowName(_featureContext.FeatureInfo, _scenarioContext.ScenarioInfo)
        });

        ReqNRollTestContext.CurrentTestInfo = null;
    }
}