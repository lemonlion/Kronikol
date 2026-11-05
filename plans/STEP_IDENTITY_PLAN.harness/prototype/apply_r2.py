"""First cut of R2 for plans/STEP_IDENTITY_PLAN.md, on top of apply_r1.py: the Reqnroll adapter stops depending on its hooks
being reached. usage: python apply_r2.py <repo root>. Every replacement must match exactly once or the script stops."""
import pathlib
import sys

root = pathlib.Path(sys.argv[1])


def edit(path, *pairs):
    file = root / path
    text = file.read_text(encoding="utf-8")
    newline = "\r\n" if "\r\n" in text else "\n"
    text = text.replace("\r\n", "\n")
    for old, new in pairs:
        count = text.count(old)
        if count != 1:
            sys.exit(f"{path}: expected one match, found {count}:\n{old}")
        text = text.replace(old, new)
    file.write_text(text.replace("\n", newline), encoding="utf-8", newline="")


edit(
    "src/Kronikol/Tracking/StepCollector.cs",
    (
        """    private static int? OpenStep(string? testId, string? keyword,""",
        """    /// <summary>The id of the step this test opened last, 0 if none; a step that did not run is placed after it.</summary>
    internal static int LastStepIdOf(string? testId)
    {
        if (testId is null || !States.TryGetValue(testId, out var state))
            return 0;

        lock (state)
            return state.LastStepId;
    }

    private static int? OpenStep(string? testId, string? keyword,""",
    ),
)

edit(
    "src/Kronikol.ReqNRoll.Core/ReqNRollStepInfo.cs",
    (
        """    internal int? FrameId { get; init; }
""",
        """    internal int? FrameId { get; init; }

    /// <summary>For a step that did not run (skipped, undefined): the id of the step opened last before it.</summary>
    internal int? NotRunAfter { get; init; }
""",
    ),
)

edit(
    "src/Kronikol.ReqNRoll.Core/ReqNRollConstants.cs",
    (
        """    internal const string StepFrameKey = "Kronikol.StepFrame";
""",
        """    internal const string StepFrameKey = "Kronikol.StepFrame";

    /// <summary>When the scenario started, for its duration.</summary>
    internal const string ScenarioStopwatchKey = "Kronikol.ScenarioStopwatch";

    /// <summary>Set once the scenario has been collected: the last hook and the finished event both offer to.</summary>
    internal const string CollectedKey = "Kronikol.Collected";
""",
    ),
)

edit(
    "src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs",
    (
        """using Reqnroll;
using Reqnroll.Events;
""",
        """using Reqnroll;
using Reqnroll.Bindings;
using Reqnroll.Events;
""",
    ),
    (
        """                    if (testId is not null)
                        StepCollector.AddAttachment(testId, evt.FilePath, name: null);
                });
""",
        """                    if (testId is not null)
                        StepCollector.AddAttachment(testId, evt.FilePath, name: null);
                });

                // Reqnroll runs none of a type's later hooks once one throws, so a suite's own hook can keep this class's
                // last ones from running. These events come whatever the hooks did.
                eventPublisher.AddHandler<HookFinishedEvent>(evt =>
                {
                    if (evt.HookType == HookType.AfterStep && evt.ScenarioContext is ScenarioContext scenario)
                        CloseStep(scenario, evt.HookException);
                });
                eventPublisher.AddHandler<StepFinishedEvent>(evt =>
                {
                    if (evt.ScenarioContext is ScenarioContext scenario)
                        NoteStepThatDidNotRun(scenario);
                });
                eventPublisher.AddHandler<ScenarioFinishedEvent>(evt =>
                {
                    if (evt.ScenarioContext is ScenarioContext scenario && evt.FeatureContext is FeatureContext feature)
                        CollectScenario(scenario, feature);
                });
""",
    ),
    (
        """        _stopwatch = Stopwatch.StartNew();
        var scenarioId = Guid.NewGuid().ToString();
""",
        """        _stopwatch = Stopwatch.StartNew();
        _scenarioContext[ReqNRollConstants.ScenarioStopwatchKey] = _stopwatch;
        var scenarioId = Guid.NewGuid().ToString();
""",
    ),
    (
        """        if (!IsOwner)
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
""",
        """        if (!IsOwner)
            return;

        CloseStep(_scenarioContext, hookException: null);
    }

    /// <summary>
    /// Closes the running Gherkin step's frame and records the step: from <see cref="AfterStep"/>, or, when a suite's hook
    /// threw before that was reached, from the hooks' finished event. A hook's failure is the step's failure.
    /// </summary>
    private static void CloseStep(ScenarioContext scenario, Exception? hookException)
    {
        if (!scenario.TryGetValue(ReqNRollConstants.StepFrameKey, out int? frame) || frame is null)
            return;
        scenario.Remove(ReqNRollConstants.StepFrameKey);

        var stepStopwatch = (Stopwatch)scenario[ReqNRollConstants.StepStopwatchKey];
        stepStopwatch.Stop();
        var stepContext = scenario.StepContext;
        var status = hookException is null ? stepContext.Status : scenario.ScenarioExecutionStatus;
        var steps = (List<ReqNRollStepInfo>)scenario[ReqNRollConstants.StepsCollectionKey];
        var tableText = stepContext.StepInfo.Table?.ToString();
        var docString = stepContext.StepInfo.MultilineText;

        var inlineParams = InlineParams(stepContext);

        // Pop the StepCollector step so assertion sub-steps are captured
        var scenarioId = (string)scenario[ReqNRollConstants.ScenarioRuntimeIdKey];
        StepCollector.CompleteFrame(scenarioId, frame, status == ScenarioExecutionStatus.OK);

        steps.Add(new ReqNRollStepInfo(
            stepContext.StepInfo.StepInstance.StepDefinitionKeyword.ToString(),
            stepContext.StepInfo.Text,
            status,
            stepStopwatch.Elapsed,
            tableText,
            docString,
            inlineParams) { FrameId = frame });
    }

    /// <summary>A step Reqnroll ran no hooks for (skipped after a failure, undefined): listed as it came in the scenario.</summary>
    private static void NoteStepThatDidNotRun(ScenarioContext scenario)
    {
        if (scenario.ContainsKey(ReqNRollConstants.StepFrameKey)
            || !scenario.TryGetValue(ReqNRollConstants.ScenarioRuntimeIdKey, out string? scenarioId)
            || scenario.StepContext.Status == ScenarioExecutionStatus.OK)
            return;

        var stepContext = scenario.StepContext;
        var keyword = stepContext.StepInfo.StepInstance?.StepDefinitionKeyword.ToString()
                      ?? stepContext.StepInfo.StepDefinitionType.ToString();
        ((List<ReqNRollStepInfo>)scenario[ReqNRollConstants.StepsCollectionKey]).Add(new ReqNRollStepInfo(
            keyword,
            stepContext.StepInfo.Text,
            stepContext.Status,
            null,
            stepContext.StepInfo.Table?.ToString(),
            stepContext.StepInfo.MultilineText) { NotRunAfter = StepCollector.LastStepIdOf(scenarioId) });
    }

    private static InlineParamCapture[]? InlineParams(ScenarioStepContext stepContext)
    {
        // Capture inline parameter positions from binding match
        InlineParamCapture[]? inlineParams = null;
        var bindingMatch = stepContext.StepInfo.BindingMatch;
""",
    ),
    (
        """            if (captures.Count > 0)
                inlineParams = captures.ToArray();
        }

        // Pop the StepCollector step so assertion sub-steps are captured
        var scenarioId = (string)_scenarioContext[ReqNRollConstants.ScenarioRuntimeIdKey];
        var passed = stepContext.Status == ScenarioExecutionStatus.OK;
        var frame = _scenarioContext.TryGetValue(ReqNRollConstants.StepFrameKey, out int? opened) ? opened : null;
        StepCollector.CompleteFrame(scenarioId, frame, passed);

        steps.Add(new ReqNRollStepInfo(
            stepContext.StepInfo.StepInstance.StepDefinitionKeyword.ToString(),
            stepContext.StepInfo.Text,
            stepContext.Status,
            stepStopwatch.Elapsed,
            tableText,
            docString,
            inlineParams) { FrameId = frame });
    }
""",
        """            if (captures.Count > 0)
                inlineParams = captures.ToArray();
        }

        return inlineParams;
    }
""",
    ),
    (
        """        if (!IsOwner)
            return;

        _stopwatch?.Stop();
        var scenarioId = (string)_scenarioContext[ReqNRollConstants.ScenarioRuntimeIdKey];
        var steps = (List<ReqNRollStepInfo>)_scenarioContext[ReqNRollConstants.StepsCollectionKey];

        var arguments = _scenarioContext.ScenarioInfo.Arguments;
""",
        """        if (!IsOwner)
            return;

        CollectScenario(_scenarioContext, _featureContext);
        ReqNRollTestContext.CurrentTestInfo = null;
    }

    /// <summary>
    /// Hands the scenario to the report: from <see cref="AfterScenario"/>, or, when a suite's hook threw before that was
    /// reached, from the scenario's finished event, without which a failed scenario left the report altogether.
    /// </summary>
    private static void CollectScenario(ScenarioContext _scenarioContext, FeatureContext _featureContext)
    {
        if (_scenarioContext.ContainsKey(ReqNRollConstants.CollectedKey)
            || !_scenarioContext.ContainsKey(ReqNRollConstants.ScenarioRuntimeIdKey))
            return;
        _scenarioContext[ReqNRollConstants.CollectedKey] = true;

        var _stopwatch = _scenarioContext.TryGetValue(ReqNRollConstants.ScenarioStopwatchKey, out Stopwatch? started) ? started : null;
        _stopwatch?.Stop();
        var scenarioId = (string)_scenarioContext[ReqNRollConstants.ScenarioRuntimeIdKey];
        var steps = (List<ReqNRollStepInfo>)_scenarioContext[ReqNRollConstants.StepsCollectionKey];

        var arguments = _scenarioContext.ScenarioInfo.Arguments;
""",
    ),
    (
        """                : ExamplesBlockResolver.ResolveRowName(_featureContext.FeatureInfo, _scenarioContext.ScenarioInfo)
        });

        ReqNRollTestContext.CurrentTestInfo = null;
    }
""",
        """                : ExamplesBlockResolver.ResolveRowName(_featureContext.FeatureInfo, _scenarioContext.ScenarioInfo)
        });
    }
""",
    ),
)

edit(
    "src/Kronikol.ReqNRoll.Core/ScenarioInfoEnumerableExtensions.cs",
    (
        """        if (steps.Count > 0 && steps.All(s => s.FrameId is null))
            return MapStepsByPosition(steps, collectedSteps);

        var byFrame = steps.ToDictionary(s => s.FrameId!.Value);
        var mapped = new List<ScenarioStep>();
        var priorFailure = false;
        foreach (var collected in collectedSteps)
        {
            if (collected.CollectedId is not { } id || !byFrame.TryGetValue(id, out var step))
            {
                mapped.Add(collected);
                continue;
            }

            mapped.Add(MapStep(step, collected, priorFailure));
            if (step.Status == ScenarioExecutionStatus.TestError || step.Status == ScenarioExecutionStatus.BindingError)
                priorFailure = true;
        }

        return mapped.Count > 0 ? mapped.ToArray() : null;
    }

    private static ScenarioStep MapStep(ReqNRollStepInfo step, ScenarioStep collected, bool priorFailure) => new()
    {
        CollectedId = collected.CollectedId,
""",
        """        if (steps.Count > 0 && steps.All(s => s.FrameId is null && s.NotRunAfter is null))
            return MapStepsByPosition(steps, collectedSteps);

        var byFrame = steps.Where(s => s.FrameId is not null).ToDictionary(s => s.FrameId!.Value);
        var notRun = new Queue<ReqNRollStepInfo>(steps.Where(s => s.FrameId is null));
        var mapped = new List<ScenarioStep>();
        var priorFailure = false;
        foreach (var collected in collectedSteps)
        {
            // A step that did not run stands where it came in the scenario: after the step opened last before it.
            while (notRun.TryPeek(out var next) && next.NotRunAfter < collected.CollectedId)
                mapped.Add(MapStep(notRun.Dequeue(), null, priorFailure));

            if (collected.CollectedId is not { } id || !byFrame.TryGetValue(id, out var step))
            {
                mapped.Add(collected);
                priorFailure |= collected.Status == ExecutionResult.Failed;
                continue;
            }

            mapped.Add(MapStep(step, collected, priorFailure));
            if (step.Status == ScenarioExecutionStatus.TestError || step.Status == ScenarioExecutionStatus.BindingError)
                priorFailure = true;
        }

        while (notRun.TryDequeue(out var rest))
            mapped.Add(MapStep(rest, null, priorFailure));

        return mapped.Count > 0 ? mapped.ToArray() : null;
    }

    private static ScenarioStep MapStep(ReqNRollStepInfo step, ScenarioStep? collected, bool priorFailure) => new()
    {
        CollectedId = collected?.CollectedId,
""",
    ),
    (
        """        SubSteps = collected.SubSteps,
        Attachments = collected.Attachments,
    };
""",
        """        SubSteps = collected?.SubSteps,
        Attachments = collected?.Attachments,
    };
""",
    ),
)
print("applied")
