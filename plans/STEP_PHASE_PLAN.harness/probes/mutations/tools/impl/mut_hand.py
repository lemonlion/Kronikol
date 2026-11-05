"""The harness's mutants the release's code words differently, by hand, for mut_facts.py. Each is its namesake's edit on this code."""
SC = 'src/Kronikol/Tracking/StepCollector.cs'
BD = 'src/Kronikol.BDDfy.xUnit3/BDDfyStepTrackingExecutor.cs'
TH = 'src/Kronikol/Tracking/TestTrackingMessageHandler.cs'
PU = 'src/Kronikol/PlantUml/PlantUmlCreator.cs'

MUTANTS = {
    'hm02': {'desc': 'LeaveFlow dropped from each CompleteStepAsync',
             'edits': [(SC, 'LeaveFlow();\n        return CompleteWhenDoneAsync(task);', 'return CompleteWhenDoneAsync(task);', -1)]},
    'hm13': {'desc': 'flowPhase dropped, an And reads state.Phase always',
             'edits': [(SC, 'flowPhase != TestPhase.Unknown ? flowPhase : state.Phase', 'state.Phase')]},
    'hm13b': {'desc': 'state.Phase dropped, an And reads its flow always',
              'edits': [(SC, 'flowPhase != TestPhase.Unknown ? flowPhase : state.Phase', 'flowPhase')]},
    'hm17': {'desc': 'CallInSetupStep dropped from the boundary',
             'edits': [(SC, '(state.Phase == TestPhase.Setup || (frameworkStep && state.CallInSetupStep))', 'state.Phase == TestPhase.Setup')]},
    'hm18': {'desc': "a framework's word is not used, its keyword is",
             'edits': [(SC, 'PhaseConfiguration.PhaseOfStep(phaseKeyword,', 'PhaseConfiguration.PhaseOfStep(keyword ?? phaseKeyword,')]},
    'hm19': {'desc': 'WhenTriggersAction gate dropped',
             'edits': [(SC, ': Options.WhenTriggersAction ? PhaseConfiguration.PhaseOfAttribute(keyword) : null;', ': PhaseConfiguration.PhaseOfAttribute(keyword);')]},
    'hm23': {'desc': "the handler's state back to one per test and not per client",
             'edits': [(TH, 'StepCollector.NeedsStepTypeFromHandler(currentTestInfo.Id, this)', 'StepCollector.NeedsStepTypeFromHandler(currentTestInfo.Id, string.Empty)'),
                       (TH, 'StepCollector.NoteCallInStepType(currentTestInfo.Id, currentStepType, this)', 'StepCollector.NoteCallInStepType(currentTestInfo.Id, currentStepType, string.Empty)')]},
    'hm30': {'desc': "the handler's guard of the fetcher dropped",
             'edits': [(TH, 'catch (Exception)', 'catch (ArithmeticException)')]},
    'hn02': {'desc': 'the ActionStepSeen = false reset dropped',
             'edits': [(SC, '                        state.ActionStepSeen = false;\n', '')]},
    'hn04': {'desc': "BDDfy's phaseBefore always TestPhaseContext.Current",
             'edits': [(BD, 'var phaseBefore = underKeyword ? TestPhaseContext.Current : TestPhase.Unknown;', 'var phaseBefore = TestPhaseContext.Current;')]},
    'hn05': {'desc': "BDDfy's phaseBefore always Unknown",
             'edits': [(BD, 'var phaseBefore = underKeyword ? TestPhaseContext.Current : TestPhase.Unknown;', 'var phaseBefore = TestPhase.Unknown;')]},
    'hn09': {'desc': 'the ReferenceEquals(node.Step, completed) loop dropped',
             'edits': [(SC, 'if (ReferenceEquals(node.Step, completed))', 'if (false && ReferenceEquals(node.Step, completed))')]},
    'hn13': {'desc': 'BDDfy with no test id no longer writes its keyword phase',
             'edits': [(BD, 'if (testId is null || (keyword is null && !underKeyword))', 'if (testId is not null && keyword is null && !underKeyword)')]},
    'hm27': {'desc': 'TrackingIgnore dropped from the Setup partition guard, if present',
             'edits': [(PU, '&& !t.TrackingIgnore)', ')')]},
}
