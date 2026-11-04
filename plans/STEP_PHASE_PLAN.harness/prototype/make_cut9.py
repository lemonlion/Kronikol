"""Cut 9: no read of the test's decision in TestPhaseContext.Current. The test owns the decision, flows hold what they read, as they do today.
Run on a tree with a stage's cut 8 patch applied: python -I make_cut9.py <tree> <r1|r2|r3>. Each stage is cumulative."""
import subprocess
import sys

tree, stage = sys.argv[1], sys.argv[2]
r2 = stage in ('r2', 'r3')


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


# The getter goes back to what it is at 5.28.2.
subprocess.run(['git', '-C', tree, 'checkout', '-q', '--', 'src/Kronikol/Tracking/TestPhaseContext.cs'], check=True)

SC = 'src/Kronikol/Tracking/StepCollector.cs'
edit(SC,
     ('                    state.Phase = state.DecidedPhase = step.DecidedPhase = phaseNow;\n'
      '                    state.DecidedAt = step.DecidedAt = TestPhaseContext.NextTick();\n',
      '                    state.Phase = phaseNow;\n'),
     ('    private static bool FrameworkNamesThisFlow()\n    {\n        if (Track.TestIdResolver is not { } resolver)\n',
      '    private static bool FrameworkNamesThisFlow()\n    {\n'
      '        // A detached flow inherits the framework\'s context and is nobody\'s test, as TestInfoResolver reads it.\n'
      '        if (TestIdentityScope.IsDetached)\n            return false;\n\n'
      '        if (Track.TestIdResolver is not { } resolver)\n'),
     ('    private static CollectedStep? DecidingStepOpenInFlow(string testId)\n    {\n'
      '        for (var node = CurrentFlowStep.Value; node is not null; node = node.Parent)\n'
      '            if (node.TestId == testId && node.Step.EndTime == 0 && node.Step.DecidedAt != 0)\n'
      '                return node.Step;\n\n        return null;\n    }\n\n', ''),
     ('        public TestPhase DecidedPhase { get; set; }\n        public long DecidedAt { get; set; }\n        public TestPhase Phase { get; set; }\n',
      '        public TestPhase Phase { get; set; }\n'),
     ('        public TestPhase DecidedPhase { get; set; }\n        public long DecidedAt { get; set; }\n        public string Text { get; set; } = "";\n',
      '        public string Text { get; set; } = "";\n'))

# PhaseDecidedSince, whole, and NotePhase's decision.
with open(f'{tree}/{SC}', encoding='utf-8', newline='') as f:
    text = f.read()
start = text.index("    /// <summary>\n    /// What overrides this flow's own write,")
end = text.index('    /// <summary>\n    /// Notes a phase the test set itself,')
text = text[:start] + text[end:]
old = ("            // The boundary is the test's, so everything after an explicit StartAction() reads Action. A StartSetup() is its flow's alone.\n"
       '            if (phase == TestPhase.Action)\n            {\n'
       '                state.DecidedPhase = phase;\n'
       '                state.DecidedAt = TestPhaseContext.NextTick();\n            }\n')
if old not in text:
    raise SystemExit('NotePhase block not found')
text = text.replace(old, '', 1)
with open(f'{tree}/{SC}', 'w', encoding='utf-8', newline='') as f:
    f.write(text)

# A BDDfy step's phase ends with the step, as it does today: the flow goes back to what it held, which on a story is Unknown.
edit('src/Kronikol.BDDfy.xUnit3/BDDfyStepTrackingExecutor.cs',
     ('        CurrentStepTypeLocal.Value = keyword?.ToUpperInvariant();\n',
      '        CurrentStepTypeLocal.Value = keyword?.ToUpperInvariant();\n        var phaseBefore = TestPhaseContext.Current;\n'),
     ('            CurrentStepTypeLocal.Value = null;\n        }\n',
      '            CurrentStepTypeLocal.Value = null;\n            TestPhaseContext.Current = phaseBefore;\n        }\n'))

# The sync path of the weaver, as its Task path (the fourth review's S02, and F#'s tail call).
edit('src/Kronikol.StepTracking/StepWeaver.cs',
     ('        // Append catch block after the body\n        body.Instructions.Add(catchStart);\n',
      '        // A tail call cannot sit in a try, and a handler the method ended in would swallow what is appended.\n'
      '        foreach (var prefix in body.Instructions.Where(i => i.OpCode == OpCodes.Tail).ToList())\n'
      '        {\n            prefix.OpCode = OpCodes.Nop;\n            prefix.Operand = null;\n        }\n\n'
      '        foreach (var inner in body.ExceptionHandlers.Where(h => h.HandlerEnd == null))\n'
      '            inner.HandlerEnd = catchStart;\n\n'
      '        // Append catch block after the body\n        body.Instructions.Add(catchStart);\n'))

if r2:
    edit(SC,
         ('        if (startsAction)\n            DefaultTrackingDiagramOverride.StartAction(testId);\n',
          '        if (startsAction)\n            DefaultTrackingDiagramOverride.DrawBoundary(testId);\n'),
         ('            state.ActionStartDrawn = true;\n        }\n\n        DefaultTrackingDiagramOverride.StartAction(testId);\n',
          '            state.ActionStartDrawn = true;\n        }\n\n        DefaultTrackingDiagramOverride.DrawBoundary(testId);\n'),
         ('    /// <summary>\n    /// Notes a phase the test set itself,',
          "    /// <summary>A consumer's StartAction(): whether it is the first, as a second one asks for the same boundary again.</summary>\n"
          '    internal static bool NoteExplicitBoundary(string testId)\n    {\n'
          '        NotePhase(testId, TestPhase.Action);\n'
          '        var state = States.GetOrAdd(testId, _ => new TestStepState());\n'
          '        lock (state)\n        {\n'
          '            var first = !state.ExplicitBoundaryDrawn;\n'
          '            state.ExplicitBoundaryDrawn = true;\n'
          '            return first;\n        }\n    }\n\n'
          '    /// <summary>\n    /// Notes a phase the test set itself,'),
         ('        public bool ActionStartDrawn { get; set; }\n',
          '        public bool ActionStartDrawn { get; set; }\n        public bool ExplicitBoundaryDrawn { get; set; }\n'))
    edit('src/Kronikol/TrackingDiagramOverride.cs',
         ('        TestPhaseContext.Current = TestPhase.Action;\n        StepCollector.NotePhase(testId, TestPhase.Action);\n        var log = new RequestResponseLog(',
          '        TestPhaseContext.Current = TestPhase.Action;\n'
          '        if (StepCollector.NoteExplicitBoundary(testId))\n            LogBoundary(testId);\n    }\n\n'
          '    /// <summary>The boundary a step or a client draws, which an explicit <see cref="StartAction(string)"/> overrides.</summary>\n'
          '    internal static void DrawBoundary(string testId)\n    {\n'
          '        TestPhaseContext.Current = TestPhase.Action;\n'
          '        StepCollector.NotePhase(testId, TestPhase.Action);\n'
          '        LogBoundary(testId);\n    }\n\n'
          '    private static void LogBoundary(string testId)\n    {\n'
          '        var log = new RequestResponseLog('))
    edit('src/Kronikol/PlantUml/PlantUmlCreator.cs',
         ('var heldBoundary = !separateSetup && collapseConsecutiveIdenticalCalls ? tracesForTest.FindLastIndex(t => t.IsActionStart) : -1;',
          '// Nor with a call that takes its phase variant from its place: there it is on the page after all.\n'
          '        var heldBoundary = !separateSetup && collapseConsecutiveIdenticalCalls && !tracesForTest.Any(t => t.SetupVariant is not null || t.ActionVariant is not null)\n'
          '            ? tracesForTest.FindLastIndex(t => t.IsActionStart) : -1;'),
         ('var lastTrace = tracesForTest[^1];',
          '// The boundary draws nothing: a diagram split at the last call before it would leave one more, empty.\n'
          '        var lastTrace = tracesForTest.Last(t => !t.IsActionStart);'),
         ('// The last marker wins: an explicit StartAction() overrides the one a step or a client drew before it.',
          '// The last marker wins: at most one a step or a client drew, and the first explicit StartAction() after it.'))
print('cut 9 applied,', stage)
