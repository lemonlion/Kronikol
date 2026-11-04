"""Cut 10: the fifth review's fixes on cut 9, by stage, plus the public StartStep keeping 5.28.2's word table.
Run on a tree with a stage's cut 9 patch applied: python -I make_cut10.py <tree> <r1|r2|r3> <path of review5/apply_trial2.py>. Each stage is cumulative."""
import subprocess
import sys

tree, stage, trial2 = sys.argv[1], sys.argv[2], sys.argv[3]
r2 = stage in ('r2', 'r3')

SC = 'src/Kronikol/Tracking/StepCollector.cs'
PC = 'src/Kronikol/Tracking/PhaseConfiguration.cs'
BD = 'src/Kronikol.BDDfy.xUnit3/BDDfyStepTrackingExecutor.cs'


def edit(path, *pairs):
    full = f'{tree}/{path}'
    with open(full, encoding='utf-8', newline='') as f:
        text = f.read()
    for old, new in pairs:
        if old not in text:
            raise SystemExit(f'not found in {path}: {old[:90]}')
        text = text.replace(old, new, 1)
    with open(full, 'w', encoding='utf-8', newline='') as f:
        f.write(text)


# T6, T7: the weaver's bypass path returns with no completion, and a SkipIf member is read before StartStep. BypassStep takes its own node off the chain, the gate goes.
subprocess.run([sys.executable, '-I', trial2, tree], check=True)

# With no completion after a bypass, nothing else needs to tell a stub's bypass from a hand-made one.
edit(SC,
     ('        if (testId is not null && CurrentFlowStep.Value is { } started)\n            started.Step.StartedByStub = true;\n', ''),
     ('        public bool StartedByStub { get; set; }\n', ''),
     ('    /// <summary>\n    /// The step whose stub is returning its task is no longer above what the caller does next. Answers false for a bypassed step,\n'
      '    /// which <see cref="BypassStep(string?, string?)"/> has closed already: completing it again would close the step above it.\n'
      '    /// </summary>\n    private static bool LeaveFlow()\n',
      '    /// <summary>The step whose stub is returning its task is no longer above what the caller does next.</summary>\n    private static void LeaveFlow()\n'),
     ('        if (node is null || ResolveTestId() is not { } testId)\n            return true;\n\n'
      '        while (node is not null && (node.TestId != testId || (node.Step.Bypassed && !node.Step.StartedByStub)))\n            node = node.Parent;\n\n'
      '        CurrentFlowStep.Value = node?.Parent;\n'
      '        return node is not { Step: { Bypassed: true, StartedByStub: true } };\n',
      '        if (node is null || ResolveTestId() is not { } testId)\n            return;\n\n        LeaveCompletedStep(testId);\n'),
     ('        return LeaveFlow() ? CompleteWhenDoneAsync(task) : task;\n    }\n\n    private static async Task CompleteWhenDoneAsync(Task task)',
      '        LeaveFlow();\n        return CompleteWhenDoneAsync(task);\n    }\n\n    private static async Task CompleteWhenDoneAsync(Task task)'),
     ('        return LeaveFlow() ? CompleteWhenDoneAsync(task) : task;\n    }\n\n    private static async Task<T> CompleteWhenDoneAsync<T>(Task<T> task)',
      '        LeaveFlow();\n        return CompleteWhenDoneAsync(task);\n    }\n\n    private static async Task<T> CompleteWhenDoneAsync<T>(Task<T> task)'))

# T8: whoever completes a step also takes off the step the stack really closed. T9: a step's parent is the flow's head, whichever test that step is for.
edit(SC,
     ('            completed.ErrorMessage = errorMessage;\n',
      '            completed.ErrorMessage = errorMessage;\n\n'
      '            for (var node = CurrentFlowStep.Value; node is not null; node = node.Parent)\n'
      '                if (ReferenceEquals(node.Step, completed))\n'
      '                {\n                    CurrentFlowStep.Value = node.Parent;\n                    break;\n                }\n'),
     ('        CurrentFlowStep.Value = new FlowStep(testId, step, ChainOf(testId));\n',
      '        CurrentFlowStep.Value = new FlowStep(testId, step, CurrentFlowStep.Value);\n'))

# The public overloads keep 5.28.2's word table: exact case, and And says nothing. Only an adapter's word goes through PhaseOfStep.
edit(SC,
     ('            var says = PhaseConfiguration.PhaseOfStep(phaseKeyword ?? (Options.WhenTriggersAction ? keyword : null),\n'
      '                flowPhase != TestPhase.Unknown ? flowPhase : state.Phase);\n',
      '            var says = phaseKeyword is not null\n'
      '                ? PhaseConfiguration.PhaseOfStep(phaseKeyword, flowPhase != TestPhase.Unknown ? flowPhase : state.Phase)\n'
      '                : Options.WhenTriggersAction ? PhaseConfiguration.PhaseOfAttribute(keyword) : null;\n'))
edit(PC,
     ('    /// <summary>\n    /// What a step\'s keyword says about its test\'s phase',
      '    /// <summary>The phase a step attribute\'s keyword, or a hand-made step\'s, says. Exact case: a word it does not know says nothing.</summary>\n'
      '    internal static TestPhase? PhaseOfAttribute(string? keyword) => keyword switch\n    {\n'
      '        "Given" or "But" => TestPhase.Setup,\n'
      '        "When" or "Then" or "ButWhen" => TestPhase.Action,\n'
      '        _ => null\n    };\n\n'
      '    /// <summary>\n    /// What a step\'s keyword says about its test\'s phase'))

# T3: BDDfy. With no test id it writes its keyword's phase, as it did. A step gives back what the flow held only under an open keyword, where a story is a label;
# at the top it ends in Unknown, as it does at 5.28.2.
edit(BD,
     ('        var phaseBefore = TestPhaseContext.Current;\n', ''),
     ('        var stepText = ExtractStepText(step.Title, keyword);\n',
      '        var phaseBefore = testId is not null && StepCollector.UnderOpenKeyword(testId) ? TestPhaseContext.Current : TestPhase.Unknown;\n'
      '        if (testId is null)\n            TestPhaseContext.Current = PhaseConfiguration.ResolvePhaseFromStepType(keyword);\n\n'
      '        var stepText = ExtractStepText(step.Title, keyword);\n'))
edit(SC,
     ('    private static bool OpenStepSaysPhase(string testId)\n',
      '    internal static bool UnderOpenKeyword(string testId) => OpenStepSaysPhase(testId);\n\n    private static bool OpenStepSaysPhase(string testId)\n'))

if r2:
    # T1: a framework's steps own the boundary. A woven keyword before its first step does not count as an act, and one on a flow with nothing open above it,
    # once its steps have begun, is the flow's own.
    edit(SC,
         ('                if (named && !OutlivedStepSaysPhase(testId))\n                {\n',
          '                if (named && !OutlivedStepSaysPhase(testId) && (frameworkStep || !state.StepsSetPhase))\n                {\n'
          '                    if (frameworkStep && !state.StepsSetPhase)\n                        state.ActionStepSeen = false;\n\n'))
    # T2: an explicit StartAction() overrides a step's boundary while that step is open. Once it has closed, on a test whose framework drew the boundary, a later one
    # asks for what is already there, as at 5.28.2. A woven step's boundary still yields.
    edit(SC,
         ('                    state.ActionStartDrawn |= startsAction;\n',
          '                    state.ActionStartDrawn |= startsAction;\n'
          '                    if (startsAction && frameworkStep)\n                        state.BoundaryStep = step;\n'),
         ('            var first = !state.ExplicitBoundaryDrawn;\n',
          '            var superseded = state.ClientDrewBoundary || state.BoundaryStep is { EndTime: > 0, Bypassed: false };\n'
          '            var first = !state.ExplicitBoundaryDrawn && !superseded;\n'),
         ('            state.ActionStartDrawn = true;\n        }\n\n        DefaultTrackingDiagramOverride.DrawBoundary(testId);\n',
          '            state.ActionStartDrawn = true;\n            state.ClientDrewBoundary = true;\n        }\n\n        DefaultTrackingDiagramOverride.DrawBoundary(testId);\n'),
         ('        public bool ExplicitBoundaryDrawn { get; set; }\n',
          '        public bool ExplicitBoundaryDrawn { get; set; }\n        public CollectedStep? BoundaryStep { get; set; }\n        public bool ClientDrewBoundary { get; set; }\n'))
    # T4b, T4c: each client asks its fetcher until it has itself passed the boundary, as the fetcher is the phase's only source on the [Obsolete] LightBDD path, and one
    # boundary is drawn. A woven step does not draw the boundary from what a fetcher saw.
    edit(SC,
         ('    internal static bool NeedsStepTypeFromHandler(string testId)\n', '    internal static bool NeedsStepTypeFromHandler(string testId, object client)\n'),
         ('            return !state.StepsSetPhase && !state.ActionStartDrawn;\n', '            return !state.StepsSetPhase && !state.ClientsPastBoundary.Contains(client);\n'),
         ('    internal static void NoteCallInStepType(string testId, string stepType)\n', '    internal static void NoteCallInStepType(string testId, string stepType, object client)\n'),
         ('                state.CallInSetupStep = true;\n                return;\n', '                state.CallInSetupStep = true;\n                state.ClientsInSetup.Add(client);\n                return;\n'),
         ('            if (!state.CallInSetupStep || state.ActionStartDrawn)\n                return;\n',
          '            if (!state.ClientsInSetup.Contains(client))\n                return;\n\n            state.ClientsPastBoundary.Add(client);\n            if (state.ActionStartDrawn)\n                return;\n'),
         ('        public bool ExplicitBoundaryDrawn { get; set; }\n',
          '        public bool ExplicitBoundaryDrawn { get; set; }\n        public HashSet<object> ClientsInSetup { get; } = [];\n        public HashSet<object> ClientsPastBoundary { get; } = [];\n'),
         ('(state.Phase == TestPhase.Setup || state.CallInSetupStep)', '(state.Phase == TestPhase.Setup || (frameworkStep && state.CallInSetupStep))'))
    edit('src/Kronikol/Tracking/TestTrackingMessageHandler.cs',
         ('!StepCollector.NeedsStepTypeFromHandler(currentTestInfo.Id))', '!StepCollector.NeedsStepTypeFromHandler(currentTestInfo.Id, this))'),
         ('StepCollector.NoteCallInStepType(currentTestInfo.Id, currentStepType);', 'StepCollector.NoteCallInStepType(currentTestInfo.Id, currentStepType, this);'))
print('cut 10 applied,', stage)
