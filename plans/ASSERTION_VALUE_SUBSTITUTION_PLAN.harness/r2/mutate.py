# Mutation runs for ASSERTION_VALUE_SUBSTITUTION_PLAN R2 (Q3), in a snapshot worktree, one per behaviour R2 adds.
# Usage: python mutate_r2.py <worktree> [N1 N2 ...]. Each mutation edits one or more files (exact-string replace,
# asserted to match once), runs the facts it should turn red, records the summary and the failed names, and restores
# the files from copies taken before the edit (never git checkout).
import os
import re
import subprocess
import sys

WT = sys.argv[1]
ONLY = set(sys.argv[2:])
CORE = 'tests/Kronikol.Tests/Kronikol.Tests.csproj'
WEAVE = 'tests/Kronikol.Tests.AssertionTracking/Kronikol.Tests.AssertionTracking.csproj'
FMT = 'src/Kronikol/Tracking/AssertionExpressionFormatter.cs'
TRK = 'src/Kronikol/Tracking/Track.cs'
WV = 'src/Kronikol.AssertionTracking/AssertionWeaver.cs'
TRACKING = 'FullyQualifiedName~TrackValueResolutionTests'
WEAVE_FILTER = 'FullyQualifiedName~AssertionValueWeaveTests'
WEAVE_AND_SHOULDLY = 'FullyQualifiedName~AssertionValueWeaveTests|FullyQualifiedName~ShouldlyWeaveTests'

MUTATIONS = {
    'N1': ('no argument captured', [(WV,
        '''        if (captured.Count == 0)
            return null;''',
        '''        if (captured.Count >= 0)
            return null;''')], [(WEAVE, WEAVE_FILTER)]),
    'N2': ("a not-evaluated argument's value starts as null, not a plain object", [(WV,
        '''                il.InsertBefore(firstInstr, il.Create(OpCodes.Newobj,
                    module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes))));''',
        '''                il.InsertBefore(firstInstr, il.Create(OpCodes.Ldnull));''')], [(WEAVE, WEAVE_FILTER)]),
    'N3': ('the whole-argument pass skipped', [(FMT,
        '''            if (values.TryGetValue(AccessChain.CanonicalKey(args[start..end]), out var value))
                replacements.Add((start, end, value));''',
        '''            if (false && values.TryGetValue(AccessChain.CanonicalKey(args[start..end]), out var value))
                replacements.Add((start, end, value));''')], [(CORE, TRACKING), (WEAVE, WEAVE_FILTER)]),
    'N4': ("an argument's text read as a variable's name", [(TRK,
        '''                if (AccessChain.IsIdentifier(varNames[i]))''',
        '''                if (true || AccessChain.IsIdentifier(varNames[i]))''')], [(CORE, TRACKING), (WEAVE, WEAVE_FILTER)]),
    'N5': ("a value read off a variable afterwards wins over the argument's", [(TRK,
        '''            var values = new ChainValues();
            for (var i = 0; i < varNames.Length; i++)''',
        '''            var values = new ChainValues();
            var late = new List<(string, string)>();
            for (var i = 0; i < varNames.Length; i++)'''), (TRK,
        '''                    values.TryAdd(AccessChain.CanonicalKey(varNames[i]), formatted);''',
        '''                    late.Add((AccessChain.CanonicalKey(varNames[i]), formatted));'''), (TRK,
        '''            return values.Count > 0 ? values : null;''',
        '''            foreach (var (k, v) in late)
                values.TryAdd(k, v);
            return values.Count > 0 ? values : null;''')], [(CORE, TRACKING)]),
    'N6': ('constants captured', [(WV,
        '''            if (name.StartsWith("ldc.", StringComparison.Ordinal) || producer.OpCode.Code is Code.Ldstr or Code.Ldnull)
                return true;''',
        '''            if (name.StartsWith("ldc.", StringComparison.Ordinal) || producer.OpCode.Code is Code.Ldstr or Code.Ldnull)
                return false;''')], [(WEAVE, WEAVE_FILTER)]),
    'N7': ('a custom message or a because captured', [(WV,
        '''            if (parameter.Name is "customMessage" or "because" or "becauseArgs" ||
                HasAttribute(parameter, "CallerArgumentExpressionAttribute"))
                continue;''',
        '''            if (HasAttribute(parameter, "CallerArgumentExpressionAttribute"))
                continue;''')], [(WEAVE, WEAVE_FILTER)]),
    'N8': ("a value boxed as the stack's type, not the parameter's", [(WV,
        '''        if (concrete is not GenericParameter && !concrete.ContainsGenericParameter && concrete.IsValueType)
            return concrete;''',
        '''        if (concrete is not GenericParameter && !concrete.ContainsGenericParameter && concrete.IsValueType && false)
            return concrete;''')], [(WEAVE, WEAVE_FILTER)]),
    'N9': ('a branch to the call not sent to the stores', [(WV,
        '''            if (ReferenceEquals(instruction.Operand, capture.Call))
                instruction.Operand = inserted[0];''',
        '''            if (ReferenceEquals(instruction.Operand, capture.Call) && false)
                instruction.Operand = inserted[0];'''), (WV,
        '''                    if (ReferenceEquals(targets[t], capture.Call))
                        targets[t] = inserted[0];''',
        '''                    if (ReferenceEquals(targets[t], capture.Call) && false)
                        targets[t] = inserted[0];''')], [(WEAVE, WEAVE_FILTER)]),
    'N10': ("the arguments matched against the text without Shouldly's message", [(WV,
        '''DetectCapturedArguments(method, statementInstructions, writtenText, capturedVariables,''',
        '''DetectCapturedArguments(method, statementInstructions, sourceText, capturedVariables,''')],
        [(WEAVE, WEAVE_FILTER)]),
    'N11': ('code with nothing to evaluate captured', [(WV,
        '''    private static bool EvaluatesSomething(string text)
    {''',
        '''    private static bool EvaluatesSomething(string text)
    {
        if (text.Length > 0)
            return true;''')], [(WEAVE, WEAVE_AND_SHOULDLY)]),
    'N12': ('a variable already captured passed again as an argument', [(WV,
        '''            if (!EvaluatesSomething(text) || variables.Any(v => v.Name == text) || slot.Type == null ||''',
        '''            if (!EvaluatesSomething(text) || slot.Type == null ||''')], [(WEAVE, WEAVE_FILTER)]),
    'N13': ('an argument whose value does not print kept as null', [(TRK,
        '''                else if (FormatValue(value) is { } formatted)
                    values.TryAdd(AccessChain.CanonicalKey(varNames[i]), formatted);''',
        '''                else
                    values.TryAdd(AccessChain.CanonicalKey(varNames[i]), FormatValue(value) ?? "null");''')],
        [(CORE, TRACKING), (WEAVE, WEAVE_FILTER)]),
    'N14': ('the argument list not split at its commas', [(FMT,
        '''                    case not ',':
                        continue;''',
        '''                    default:
                        continue;''')], [(CORE, TRACKING)]),
    'N15': ('an interpolated string argument read as constant text', [(WV,
        """                if (interpolated && text.IndexOf('{', i, close - i + 1) >= 0)""",
        """                if (false && interpolated && text.IndexOf('{', i, close - i + 1) >= 0)""")],
        [(WEAVE, WEAVE_FILTER)]),
}


def run(project, flt):
    framework = ['-f', 'net10.0']
    p = subprocess.run(['dotnet', 'test', project, *framework, '--filter', flt], cwd=WT, capture_output=True,
                       text=True, encoding='utf-8', errors='replace')
    out = p.stdout + p.stderr
    summary = [l.strip() for l in out.splitlines() if re.search(r'(Passed|Failed)!\s+-', l)]
    errors = sorted({l.strip()[:160] for l in out.splitlines() if 'error CS' in l})
    failed = sorted({re.sub(r'\s*\[[^\]]*\]$', '', l.strip()[len('Failed '):])[:150]
                     for l in out.splitlines() if l.strip().startswith('Failed Kronikol')})
    return summary, errors, failed


for name, (what, edits, runs) in MUTATIONS.items():
    if ONLY and name not in ONLY:
        continue
    backups = {}
    for path, _, _ in edits:
        full = os.path.join(WT, path)
        if full not in backups:
            backups[full] = open(full, encoding='utf-8', newline='').read()
    try:
        for path, a, b in edits:
            full = os.path.join(WT, path)
            s = open(full, encoding='utf-8', newline='').read()
            assert s.count(a) == 1, (name, a[:80])
            open(full, 'w', encoding='utf-8', newline='').write(s.replace(a, b))
        print(f'== {name}: {what}', flush=True)
        for project, flt in runs:
            summary, errors, failed = run(project, flt)
            print(f'   {os.path.basename(project)}: {summary or "NO SUMMARY"}', flush=True)
            for e in errors[:5]:
                print(f'     COMPILE {e}', flush=True)
            for f in failed[:40]:
                print(f'     red: {f}', flush=True)
    finally:
        for full, text in backups.items():
            open(full, 'w', encoding='utf-8', newline='').write(text)
print('done', flush=True)
