"""Small fixes tried by the sixth review, on a tree holding a cut 10 stage. Evidence, not code to merge. Usage: python -I apply_fix.py <tree> <r1|r2>.
F-T7: the SkipIf member is read after StartStep again, as at 5.28.2, in a try whose catch closes the step: a getter that throws leaves nothing open, and a getter that looks or calls sees its step.
F-T3: a keyword-free BDDfy step at the top of a story reads Unknown, as at 5.28.2, whatever the flow held.
F-T9: a completion takes off its own node and leaves another test's nodes above it.
F-T1 (r2): a boundary a woven step drew before a framework's first step does not count once the framework's steps begin.
F-T2 (r2): a framework's boundary stands once its step has closed, or this flow is no longer inside it; EndTime alone is what F21 says not to ask."""
import re
import sys

tree, stage = sys.argv[1], sys.argv[2]
r2 = stage != 'r1'

W = 'src/Kronikol.StepTracking/StepWeaver.cs'
SC = 'src/Kronikol/Tracking/StepCollector.cs'
BD = 'src/Kronikol.BDDfy.xUnit3/BDDfyStepTrackingExecutor.cs'


def edit(path, *pairs):
    full = f'{tree}/{path}'
    with open(full, encoding='utf-8', newline='') as f:
        text = f.read()
    for old, new in pairs:
        if old not in text:
            raise SystemExit(f'not found in {path}: {old[:100]}')
        text = text.replace(old, new, 1)
    with open(full, 'w', encoding='utf-8', newline='') as f:
        f.write(text)


# F-T7, both paths.
full = f'{tree}/{W}'
with open(full, encoding='utf-8', newline='') as f:
    text = f.read()
block = re.compile(r"        // A getter that throws must find no step open\.\r?\n        VariableDefinition\? skipLocal = null;\r?\n        if \(skipIf != null\)\r?\n        \{.*?preamble\.Add\(il\.Create\(OpCodes\.Stloc, skipLocal\)\);\r?\n        \}\r?\n\r?\n", re.S)
text, n = block.subn('', text)
if n != 2:
    raise SystemExit(f'getter blocks: {n}')
load = 'preamble.Add(il.Create(OpCodes.Ldloc, skipLocal!));'
if text.count(load) != 2:
    raise SystemExit('load sites')
text = text.replace(load, 'preamble.Add(il.Create(OpCodes.Ldloc, ReadSkipMemberWithTheStepOpen(method, il, preamble, skipIf, stepMethods.CompleteStepFailed)));')
helper = '''    private static VariableDefinition ReadSkipMemberWithTheStepOpen(MethodDefinition method, ILProcessor il, List<Instruction> preamble, SkipIfInfo skipIf, MethodReference completeStepFailed)
    {
        var body = method.Body;
        var module = method.Module;
        var skipLocal = new VariableDefinition(module.TypeSystem.Boolean);
        body.Variables.Add(skipLocal);
        var exceptionType = module.ImportReference(typeof(Exception));
        var exceptionLocal = new VariableDefinition(exceptionType);
        body.Variables.Add(exceptionLocal);
        var getMessage = module.ImportReference(typeof(Exception).GetProperty(nameof(Exception.Message))!.GetGetMethod()!);

        var first = preamble.Count;
        if (skipIf.IsField)
        {
            if (!skipIf.IsStatic)
                preamble.Add(il.Create(OpCodes.Ldarg_0));
            preamble.Add(il.Create(skipIf.IsStatic ? OpCodes.Ldsfld : OpCodes.Ldfld, module.ImportReference(skipIf.FieldRef!)));
        }
        else
        {
            if (!skipIf.IsStatic)
                preamble.Add(il.Create(OpCodes.Ldarg_0));
            preamble.Add(il.Create(skipIf.IsStatic ? OpCodes.Call : OpCodes.Callvirt, module.ImportReference(skipIf.PropertyGetterRef!)));
        }

        preamble.Add(il.Create(OpCodes.Stloc, skipLocal));
        var after = il.Create(OpCodes.Nop);
        preamble.Add(il.Create(OpCodes.Leave, after));
        var tryStart = preamble[first];
        var catchStart = il.Create(OpCodes.Stloc, exceptionLocal);
        preamble.Add(catchStart);
        preamble.Add(il.Create(OpCodes.Ldc_I4_0));
        preamble.Add(il.Create(OpCodes.Ldloc, exceptionLocal));
        preamble.Add(il.Create(OpCodes.Callvirt, getMessage));
        preamble.Add(il.Create(OpCodes.Call, completeStepFailed));
        preamble.Add(il.Create(OpCodes.Rethrow));
        preamble.Add(after);
        body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            TryStart = tryStart,
            TryEnd = catchStart,
            HandlerStart = catchStart,
            HandlerEnd = after,
            CatchType = exceptionType
        });
        return skipLocal;
    }

    private static MethodReference GetCompleteStepAsyncReference('''
anchor = '    private static MethodReference GetCompleteStepAsyncReference('
if text.count(anchor) != 1:
    raise SystemExit('helper anchor')
text = text.replace(anchor, helper)
with open(full, 'w', encoding='utf-8', newline='') as f:
    f.write(text)

# F-T3.
edit(BD,
     ('        var phaseBefore = testId is not null && StepCollector.UnderOpenKeyword(testId) ? TestPhaseContext.Current : TestPhase.Unknown;\n'
      '        if (testId is null)\n',
      '        var underKeyword = testId is not null && StepCollector.UnderOpenKeyword(testId);\n'
      '        var phaseBefore = underKeyword ? TestPhaseContext.Current : TestPhase.Unknown;\n'
      '        if (testId is null || (keyword is null && !underKeyword))\n'))

# F-T9.
edit(SC,
     ('        if (node is not null)\n            CurrentFlowStep.Value = node.Parent;\n    }\n',
      '        if (node is not null)\n            CurrentFlowStep.Value = Without(CurrentFlowStep.Value!, node);\n    }\n\n'
      '    // Only the step that left: another test\'s step above it on this flow is still open.\n'
      '    private static FlowStep? Without(FlowStep head, FlowStep node) =>\n'
      '        ReferenceEquals(head, node) ? node.Parent : head with { Parent = Without(head.Parent!, node) };\n'))

if r2:
    edit(SC,
         # F-T1.
         ('                    if (frameworkStep && !state.StepsSetPhase)\n                        state.ActionStepSeen = false;\n',
          '                    if (frameworkStep && !state.StepsSetPhase)\n                    {\n'
          '                        state.ActionStepSeen = false;\n'
          '                        if (!state.ExplicitBoundaryDrawn && !state.ClientDrewBoundary)\n'
          '                            state.ActionStartDrawn = false;\n'
          '                    }\n'),
         # F-T2.
         ('            var superseded = state.ClientDrewBoundary || state.BoundaryStep is { EndTime: > 0, Bypassed: false };\n',
          '            var superseded = state.ClientDrewBoundary || (state.BoundaryStep is { } boundary && (boundary.EndTime > 0 || !FlowHolds(boundary)));\n'),
         ('    private static bool OpenStepSaysPhase(string testId)\n',
          '    private static bool FlowHolds(CollectedStep step)\n    {\n'
          '        for (var node = CurrentFlowStep.Value; node is not null; node = node.Parent)\n'
          '            if (ReferenceEquals(node.Step, step))\n                return true;\n'
          '        return false;\n    }\n\n'
          '    private static bool OpenStepSaysPhase(string testId)\n'))
print('fix applied', stage)
