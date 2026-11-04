"""T6 and T7 on a tree that holds r1-r2-r3 (with or without T1-T5). Evidence, not code to merge. Usage: python -I apply_trial2.py <tree>.
T6: a bypass path ends in a plain ret, with no completion to guard, and BypassStep takes its own step off the chain, so no gate and no StartedByStub are needed.
T7: the SkipIf member is read before StartStep, so a getter that throws leaves nothing open."""
import sys

tree = sys.argv[1]


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


W = 'src/Kronikol.StepTracking/StepWeaver.cs'
SC = 'src/Kronikol/Tracking/StepCollector.cs'

# T6, weaver: the bypass ret sits before the try in the sync path, so it stays a ret; in the async path it returns its task with no completion.
edit(W,
     ('''        var rets = body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList();
        foreach (var ret in rets)
        {
            ret.OpCode = OpCodes.Leave;
''',
      '''        var syncBodyStart = body.Instructions.IndexOf(originalFirst);
        var rets = body.Instructions.Where(i => i.OpCode == OpCodes.Ret && body.Instructions.IndexOf(i) >= syncBodyStart).ToList();
        foreach (var ret in rets)
        {
            ret.OpCode = OpCodes.Leave;
'''),
     ('''        var rets = body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList();

        // A method that returns its task without being async can throw before it has one, and the step would then stay open for the
''',
      '''        var bodyStartAll = body.Instructions.IndexOf(originalFirst);
        var rets = body.Instructions.Where(i => i.OpCode == OpCodes.Ret && body.Instructions.IndexOf(i) >= bodyStartAll).ToList();

        // A method that returns its task without being async can throw before it has one, and the step would then stay open for the
'''))

# T6, collector.
edit(SC,
     ('''        // A sync stub's success path comes here after its own bypass too, and the bypass has closed the step: this would close the one above it.
        if (CurrentFlowStep.Value is { Step: { Bypassed: true, StartedByStub: true } } bypassed)
        {
            CurrentFlowStep.Value = bypassed.Parent;
            return;
        }

''', ''),
     ('''            var bypassed = state.StepStack.Pop();
            bypassed.EndTime = Stopwatch.GetTimestamp();
            bypassed.Bypassed = true;
            bypassed.BypassReason = reason;
''',
      '''            var bypassed = state.StepStack.Pop();
            bypassed.EndTime = Stopwatch.GetTimestamp();
            bypassed.Bypassed = true;
            bypassed.BypassReason = reason;

            if (CurrentFlowStep.Value is { } head && ReferenceEquals(head.Step, bypassed))
                CurrentFlowStep.Value = head.Parent;
'''))

# T7: read the member first, in both paths.
READ_SYNC_OLD = '''        // Call StepCollector.StartStep(keyword, text, paramNames, paramValues)
        if (keyword != null)
            preamble.Add(il.Create(OpCodes.Ldstr, keyword));
'''
READ_NEW = '''        // A getter that throws must find no step open.
        VariableDefinition? skipLocal = null;
        if (skipIf != null)
        {
            skipLocal = new VariableDefinition(method.Module.TypeSystem.Boolean);
            body.Variables.Add(skipLocal);
            if (skipIf.IsField)
            {
                if (!skipIf.IsStatic)
                    preamble.Add(il.Create(OpCodes.Ldarg_0));
                preamble.Add(il.Create(skipIf.IsStatic ? OpCodes.Ldsfld : OpCodes.Ldfld, method.Module.ImportReference(skipIf.FieldRef!)));
            }
            else
            {
                if (!skipIf.IsStatic)
                    preamble.Add(il.Create(OpCodes.Ldarg_0));
                preamble.Add(il.Create(skipIf.IsStatic ? OpCodes.Call : OpCodes.Callvirt, method.Module.ImportReference(skipIf.PropertyGetterRef!)));
            }
            preamble.Add(il.Create(OpCodes.Stloc, skipLocal));
        }

        // Call StepCollector.StartStep(keyword, text, paramNames, paramValues)
        if (keyword != null)
            preamble.Add(il.Create(OpCodes.Ldstr, keyword));
'''
LOAD_OLD = '''            // Load the bool property/field
            if (skipIf.IsField)
            {
                if (!skipIf.IsStatic)
                    preamble.Add(il.Create(OpCodes.Ldarg_0)); // this
                preamble.Add(il.Create(skipIf.IsStatic ? OpCodes.Ldsfld : OpCodes.Ldfld,
                    method.Module.ImportReference(skipIf.FieldRef!)));
            }
            else
            {
                if (!skipIf.IsStatic)
                    preamble.Add(il.Create(OpCodes.Ldarg_0)); // this
                preamble.Add(il.Create(skipIf.IsStatic ? OpCodes.Call : OpCodes.Callvirt,
                    method.Module.ImportReference(skipIf.PropertyGetterRef!)));
            }
'''
LOAD_NEW = '''            preamble.Add(il.Create(OpCodes.Ldloc, skipLocal!));
'''
# Twice each: once for the sync wrapper, once for the async one.
edit(W, (READ_SYNC_OLD, READ_NEW), (LOAD_OLD, LOAD_NEW))
edit(W, (READ_SYNC_OLD.replace('', ''), READ_SYNC_OLD))  # no-op guard: the first replace leaves the marker text in place
full = f'{tree}/{W}'
text = open(full, encoding='utf-8', newline='').read()
first = text.index(READ_SYNC_OLD)
second = text.find(READ_SYNC_OLD, first + len(READ_NEW))
if second < 0:
    raise SystemExit('second StartStep site not found')
text = text[:second] + READ_NEW + text[second + len(READ_SYNC_OLD):]
if LOAD_OLD not in text:
    raise SystemExit('second load site not found')
text = text.replace(LOAD_OLD, LOAD_NEW, 1)
open(full, 'w', encoding='utf-8', newline='').write(text)
print('T6 T7 applied')
