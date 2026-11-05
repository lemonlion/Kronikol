"""The harness's mutants in the words of the port onto #225's flow and #223's frames: what still means something there, with the ids they had."""
SC = 'src/Kronikol/Tracking/StepCollector.cs'
BD = 'src/Kronikol.BDDfy.xUnit3/BDDfyStepTrackingExecutor.cs'
LB = 'src/Kronikol.LightBDD.Core/StepTrackingStepDecorator.cs'
RQ = 'src/Kronikol.ReqNRoll.Core/ReqNRollTrackingHooks.cs'
TD = 'src/Kronikol/TrackingDiagramOverride.cs'
LH = 'src/Kronikol.LightBDD.Core/LightBddTestTrackingMessageHandlerOptions.cs'
HA = 'src/Kronikol/Tracking/TestTrackingMessageHandler.cs'

DECIDES = 'if (named && (isFrame || (!OutlivedStepSaysPhase(state) && !state.StepsSetPhase)))'

MUTANTS = {
    'pm01': {'desc': 'OpenStepSaysPhase dropped', 'edits': [(SC, 'if (step.SaysPhase && !OpenStepSaysPhase(state))', 'if (step.SaysPhase)')]},
    'pm04': {'desc': 'OutlivedStepSaysPhase dropped', 'edits': [(SC, DECIDES, 'if (named && (isFrame || !state.StepsSetPhase))')]},
    'q23': {'desc': "a framework's step on a flow holding a closed step keeps its phase to itself", 'edits': [(SC, DECIDES, 'if (named && !OutlivedStepSaysPhase(state) && (isFrame || !state.StepsSetPhase))')]},
    'pm13': {'desc': 'flowPhase dropped, always state.Phase', 'edits': [(SC, 'PhaseOfStep(phaseKeyword, !state.StepsSetPhase && flowPhase != TestPhase.Unknown ? flowPhase : state.Phase)', 'PhaseOfStep(phaseKeyword, state.Phase)')]},
    'pm13b': {'desc': 'state.Phase dropped, always flowPhase', 'edits': [(SC, 'PhaseOfStep(phaseKeyword, !state.StepsSetPhase && flowPhase != TestPhase.Unknown ? flowPhase : state.Phase)', 'PhaseOfStep(phaseKeyword, flowPhase)')]},
    'q25': {'desc': "the flow's word over the test's steps", 'edits': [(SC, 'PhaseOfStep(phaseKeyword, !state.StepsSetPhase && flowPhase != TestPhase.Unknown ? flowPhase : state.Phase)', 'PhaseOfStep(phaseKeyword, flowPhase != TestPhase.Unknown ? flowPhase : state.Phase)')]},
    'pm15c': {'desc': "LeaveFlowStep's walk no longer skips a step bypassed elsewhere", 'edits': [(SC, '        while (step is { IsOpen: false, Bypassed: true })\n            step = step.Parent;\n', '')]},
    'pm15d': {'desc': "OpenStepSaysPhase's IsOpen dropped", 'edits': [(SC, 'step.SaysPhase && step.IsOpen)', 'step.SaysPhase)')]},
    'pm15e': {'desc': "OutlivedStepSaysPhase's !Bypassed dropped", 'edits': [(SC, 'step.SaysPhase && !step.Bypassed && !step.IsOpen)', 'step.SaysPhase && !step.IsOpen)')]},
    'pm15e2': {'desc': "OutlivedStepSaysPhase's !IsOpen dropped", 'edits': [(SC, 'step.SaysPhase && !step.Bypassed && !step.IsOpen)', 'step.SaysPhase && !step.Bypassed)')]},
    'pm17': {'desc': 'CallInSetupStep dropped from the boundary', 'edits': [(SC, '(state.Phase == TestPhase.Setup || (isFrame && state.CallInSetupStep))', 'state.Phase == TestPhase.Setup')]},
    'pm18': {'desc': "a framework's word is not used", 'edits': [(SC, 'var says = phaseKeyword is not null\n', 'var says = false\n')]},
    'pm19': {'desc': 'WhenTriggersAction gate dropped', 'edits': [(SC, ': Options.WhenTriggersAction ? PhaseConfiguration.PhaseOfAttribute(keyword) : null;', ': PhaseConfiguration.PhaseOfAttribute(keyword);')]},
    'pm20': {'desc': 'state.ActionStartDrawn dropped from the boundary', 'edits': [(SC, '&& !state.ActionStartDrawn && !state.ActionStepSeen', '&& !state.ActionStepSeen')]},
    'pm21': {'desc': 'ActionStepSeen dropped from the boundary', 'edits': [(SC, '&& !state.ActionStartDrawn && !state.ActionStepSeen', '&& !state.ActionStartDrawn')]},
    'pm22': {'desc': 'every step sets StepsSetPhase', 'edits': [(SC, 'state.StepsSetPhase |= isFrame;', 'state.StepsSetPhase = true;')]},
    'pm22b': {'desc': 'no step sets StepsSetPhase', 'edits': [(SC, 'state.StepsSetPhase |= isFrame;', '')]},
    'pm23': {'desc': "what the handler saw is one test's for all, as a handler's fields were", 'edits': [(SC, '                          && !stepType.StartsWith("BUTWHEN", StringComparison.OrdinalIgnoreCase));\n\n        var state = States.GetOrAdd(testId, _ => new TestStepState());', '                          && !stepType.StartsWith("BUTWHEN", StringComparison.OrdinalIgnoreCase));\n\n        var state = States.GetOrAdd(client.GetHashCode().ToString(), _ => new TestStepState());')]},
    'pm24': {'desc': 'bar and boundary swapped', 'edits': [(SC, '        // After the step\'s bar, so the bar closes the Setup partition as the first bar past the setup calls always has.\n        if (startsAction)\n            DefaultTrackingDiagramOverride.DrawBoundary(testId, replacesBoundary);\n', ''), (SC, '        // Emit step delimiter hnote for top-level steps\n', '        if (startsAction)\n            DefaultTrackingDiagramOverride.DrawBoundary(testId, replacesBoundary);\n\n        // Emit step delimiter hnote for top-level steps\n')]},
    'pm30': {'desc': "the handler's guard of the fetcher dropped", 'edits': [(HA, '_currentStepTypeFetcher is null || !StepCollector.NeedsStepTypeFromHandler(currentTestInfo.Id, this)', '_currentStepTypeFetcher is null')]},
    'pm31b': {'desc': "BDDfy's AsyncVoidSupportEnabled gate dropped", 'edits': [(BD, 'if (Configurator.AsyncVoidSupportEnabled && result is Task task)', 'if (result is Task task)')]},
    'pm33': {'desc': 'every explicit StartAction logs', 'edits': [(TD, '        if (StepCollector.NoteExplicitBoundary(testId, out var replacesBoundary))\n            LogBoundary(testId, replacesBoundary);', '        StepCollector.NoteExplicitBoundary(testId, out var replacesBoundary);\n        LogBoundary(testId, replacesBoundary);')]},
    'pn01': {'desc': '(isFrame || !StepsSetPhase) dropped from the decision', 'edits': [(SC, DECIDES, 'if (named && (isFrame || !OutlivedStepSaysPhase(state)))')]},
    'pn02': {'desc': 'the ActionStepSeen reset dropped', 'edits': [(SC, '                        state.ActionStepSeen = false;\n', '')]},
    'pn02b': {'desc': "a framework's first step does not take a woven step's boundary", 'edits': [(SC, '                        if (!state.ExplicitBoundaryDrawn && !state.ClientDrewBoundary)\n                            state.ActionStartDrawn = false;\n', '')]},
    'pn04': {'desc': "BDDfy's phaseBefore always the flow's", 'edits': [(BD, 'var phaseBefore = underKeyword ? TestPhaseContext.Current : TestPhase.Unknown;', 'var phaseBefore = TestPhaseContext.Current;')]},
    'pn05': {'desc': "BDDfy's phaseBefore always Unknown", 'edits': [(BD, 'var phaseBefore = underKeyword ? TestPhaseContext.Current : TestPhase.Unknown;', 'var phaseBefore = TestPhase.Unknown;')]},
    'pn07': {'desc': 'isFrame dropped from CallInSetupStep', 'edits': [(SC, '(isFrame && state.CallInSetupStep)', 'state.CallInSetupStep')]},
    'pn13': {'desc': 'BDDfy with no test id no longer writes its keyword\'s phase', 'edits': [(BD, 'if (testId is null || (keyword is null && !underKeyword))', 'if (keyword is null && !underKeyword)')]},
    'pn13b': {'desc': 'BDDfy writes Unknown under a keyword as well', 'edits': [(BD, 'if (testId is null || (keyword is null && !underKeyword))', 'if (testId is null || keyword is null)')]},
    'pp06': {'desc': '!FlowHolds(boundary) removed', 'edits': [(SC, '(!boundary.IsOpen || !FlowHolds(boundary))', '!boundary.IsOpen')]},
    'pp06b': {'desc': '!boundary.IsOpen removed', 'edits': [(SC, '(!boundary.IsOpen || !FlowHolds(boundary))', '!FlowHolds(boundary)')]},
    'prw01': {'desc': 'a woven Then draws the boundary', 'edits': [(SC, '\n                        && (isFrame || keyword != "Then");', ';')]},
    'prw05': {'desc': "a step's marker never says it replaces", 'edits': [(SC, '                    if (startsAction)\n                    {\n                        replacesBoundary = state.BoundaryLogged;\n', '                    if (startsAction)\n                    {\n')]},
    # What the port adds.
    'q01': {'desc': "a frame's completion stays on this flow", 'edits': [(SC, '        LeaveInThisFlow(frame);\n', '')]},
    'q02': {'desc': "a step's completion stays on this flow", 'edits': [(SC, '        LeaveInThisFlow(completed);\n', '')]},
    'q03': {'desc': "a close under another test's step takes that step off the flow", 'edits': [(SC, '                if (anotherTests is null)\n                    FlowStep.Value = FlowAfter(closed);\n                else\n                    anotherTests.CameFrom = FlowAfter(closed);\n', '                FlowStep.Value = FlowAfter(closed);\n')]},
    'q04': {'desc': "a close under another test's step leaves the closed step on the flow", 'edits': [(SC, '                else\n                    anotherTests.CameFrom = FlowAfter(closed);\n', '')]},
    'q05': {'desc': 'a named flow is any flow', 'edits': [(SC, 'var named = phaseKeyword is not null || FrameworkNamesThisFlow();', 'var named = true;')]},
    'q06': {'desc': "a framework's own word does not name its flow", 'edits': [(SC, 'var named = phaseKeyword is not null || FrameworkNamesThisFlow();', 'var named = FrameworkNamesThisFlow();')]},
    'q07': {'desc': 'a detached flow counts as named', 'edits': [(SC, '        if (TestIdentityScope.IsDetached)\n            return false;\n\n        if (Track.TestIdResolver is not { } resolver)', '        if (Track.TestIdResolver is not { } resolver)')]},
    'q08': {'desc': 'a resolver that throws names the flow', 'edits': [(SC, '            return resolver() is not null;\n        }\n        catch\n        {\n            return false;', '            return resolver() is not null;\n        }\n        catch\n        {\n            return true;')]},
    'q09': {'desc': 'a null answer names the flow', 'edits': [(SC, '            return resolver() is not null;', '            resolver();\n            return true;')]},
    'q10': {'desc': "the flow does not read its own step's phase", 'edits': [(SC, '        if (phaseNow != TestPhase.Unknown)\n            TestPhaseContext.Current = phaseNow;\n', '')]},
    'q11': {'desc': 'a step with Unknown says a phase', 'edits': [(SC, 'step.SaysPhase = says is not (null or TestPhase.Unknown);', 'step.SaysPhase = says is not null;')]},
    'q12': {'desc': "the walkers read another test's steps", 'edits': [(SC, 'if (ReferenceEquals(step.State, state) && step.SaysPhase && step.IsOpen)', 'if (step.SaysPhase && step.IsOpen)')]},
    'q13': {'desc': "the walkers stop at another test's step", 'edits': [(SC, 'for (var step = FlowStep.Value; step is not null; step = step.Parent ?? step.CameFrom)\n            if (ReferenceEquals(step.State, state) && step.SaysPhase && step.IsOpen)', 'for (var step = FlowStep.Value; step is not null; step = step.Parent)\n            if (ReferenceEquals(step.State, state) && step.SaysPhase && step.IsOpen)')]},
    'q14': {'desc': 'UnderOpenKeyword always false', 'edits': [(SC, 'internal static bool UnderOpenKeyword(string testId) => States.TryGetValue(testId, out var state) && OpenStepSaysPhase(state);', 'internal static bool UnderOpenKeyword(string testId) => false;')]},
    'q15': {'desc': 'NotePhase leaves the phase', 'edits': [(SC, '            state.Phase = phase;\n            state.ActionStartDrawn |= phase == TestPhase.Action;', '            state.ActionStartDrawn |= phase == TestPhase.Action;')]},
    'q16': {'desc': "NotePhase's StartAction leaves the boundary to draw again", 'edits': [(SC, '            state.Phase = phase;\n            state.ActionStartDrawn |= phase == TestPhase.Action;', '            state.Phase = phase;')]},
    'q17': {'desc': "LightBDD's But reads a But, not an And", 'edits': [(LB, 'phaseKeyword: phaseKeyword)', 'phaseKeyword: keyword)')]},
    'q18': {'desc': "Reqnroll's frame reads the word as written", 'edits': [(RQ, 'phaseKeyword: stepInfo.StepDefinitionType.ToString(),', 'phaseKeyword: stepType,')]},
    'q19': {'desc': "BDDfy's frame says no word", 'edits': [(BD, 'StepCollector.StartFrame(testId, keyword, stepText, phaseKeyword: keyword)', 'StepCollector.StartFrame(testId, keyword, stepText)')]},
    'q20': {'desc': "LightBDD's handler writes under a framework's steps", 'edits': [(LH, 'if (stepType is not null && !StepCollector.FrameworkStepsSetPhase())', 'if (stepType is not null)')]},
    'q21': {'desc': "BDDfy's step does not get back what its flow held", 'edits': [(BD, '            TestPhaseContext.Current = phaseBefore;\n', '')]},
    'q24': {'desc': "the signature an adapter of 5.29.3 binds to is gone", 'edits': [(SC, '    internal static int? StartFrame(string? testId, string? keyword, string text, string[][]? table = null, string? docString = null)\n        => StartFrame(testId, keyword, text, phaseKeyword: null, table, docString);\n', '')]},
    'q27': {'desc': "a flow that outlived a step forgets it once it leaves a step of its own", 'edits': [(SC, '(!ReferenceEquals(cameFrom.State, state) || (!isFrame && cameFrom is { SaysPhase: true, Bypassed: false, IsOpen: false }))', '!ReferenceEquals(cameFrom.State, state)')]},
    'q28': {'desc': "a step bypassed on the flow counts as one it outlived", 'edits': [(SC, 'cameFrom is { SaysPhase: true, Bypassed: false, IsOpen: false }', 'cameFrom is { SaysPhase: true, IsOpen: false }')]},
    'q29': {'desc': "LightBDD's fetcher writes a keyword-free step's Unknown", 'edits': [(LH, 'if (stepType is not null && !StepCollector.FrameworkStepsSetPhase())', 'if (!StepCollector.FrameworkStepsSetPhase())')]},
    'q22': {'desc': 'a boundary in a step drawn by a framework with StepsSetPhase off', 'edits': [(SC, '                    state.StepsSetPhase |= isFrame;\n                    state.Phase = phaseNow;', '                    state.Phase = phaseNow;')]},
}
