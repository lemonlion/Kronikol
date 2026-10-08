"""R2 mutations: each undoes one behaviour in a snapshot worktree at R2's tip, rebuilds, runs the facts that should
catch it, and records whether they went red. The source is backed up and restored (and touched) around every
mutation. Plan section 4.3's M1 to M9 and M15 to M18 (M10 to M14 were R1's), then M19 on for what R2 added beyond
the plan's list. Usage: python mutate_r2.py [name-prefix ...]"""
import io, os, shutil, subprocess, sys

MUT = 'C:/Code/Kronikol-shouldly-mut'
OUT = 'C:/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/19eb1bc1-2d7b-43ae-8364-4eb3eecabfcc/scratchpad/r2-mutations.txt'
WEAVER = 'tests/Kronikol.Tests.AssertionTracking/Kronikol.Tests.AssertionTracking.csproj'
CORE = 'tests/Kronikol.Tests/Kronikol.Tests.csproj'
W = 'src/Kronikol.AssertionTracking/AssertionWeaver.cs'
T = 'src/Kronikol.AssertionTracking/WeaveAssertionsTask.cs'
F = 'src/Kronikol/Tracking/AssertionExpressionFormatter.cs'
CV = 'src/Kronikol/Tracking/ClosureValueResolver.cs'
G = 'src/Kronikol/Reports/ReportGenerator.cs'
SC = 'src/Kronikol/Query/ReportScanner.cs'
IX = 'src/Kronikol/Query/ReportIndex.cs'
IR = 'src/Kronikol/Ingestion/InteractionRecord.cs'
STEP = 'src/Kronikol/Tracking/StepCollector.cs'
FS = 'src/Kronikol/Ingestion/FeatureSynthesizer.cs'
CU = 'src/Kronikol/Ingestion/Cucumber/CucumberFeatureMerger.cs'

SHOULDLY = 'FullyQualifiedName~ShouldlyWeaveTests'
TASK = 'FullyQualifiedName~WeaveAssertionsTaskTests'
STACK = 'FullyQualifiedName~StackAcrossStatementsTests|FullyQualifiedName~ShouldlyWeaveTests'
MARK = 'FullyQualifiedName~AssertionMarkTests'

# (name, file, old, new, project, filter[, replace_all])
MUTATIONS = [
    ('M1 the fast path without Shouldly', W,
     'r.Name == "TUnit.Assertions" || r.Name == "TUnit.Assertions.Should" || r.Name == "Shouldly"))',
     'r.Name == "TUnit.Assertions" || r.Name == "TUnit.Assertions.Should"))',
     WEAVER, SHOULDLY + '|' + TASK),
    ('M2 an entry point by the Should prefix alone', W,
     '        return IsShouldlyMethod(method);\n    }\n\n    /// <summary>\n    /// A Shouldly assertion',
     '        return method.Name.StartsWith("Should", StringComparison.Ordinal);\n    }\n\n    /// <summary>\n    /// A Shouldly assertion',
     WEAVER, SHOULDLY),
    ('M2b namespace Shouldly and the Should prefix, without the marker', W,
     '        return resolved != null ? HasShouldlyMethodsAttribute(resolved) : type.Scope?.Name == "Shouldly";',
     '        return method.Name.StartsWith("Should", StringComparison.Ordinal);',
     WEAVER, SHOULDLY),
    ('M3 the stack before the statement taken as empty', W,
     '        entry = before;\n        exit = after;\n        return null;',
     '        entry = Array.Empty<StackAnalysis.Slot>();\n        exit = after;\n        return null;',
     WEAVER, STACK),
    ('M4 every spilled slot typed object', W,
     '        var local = new VariableDefinition(method.Module.ImportReference(StackAnalysis.LocalType(slot.Type!, method.Module)));',
     '        var local = new VariableDefinition(method.Module.TypeSystem.Object);',
     WEAVER, STACK),
    ('M5 the sequence point left on the first original instruction', W,
     '        MoveSequencePoint(method, assertion.SequencePoint, prologueStart);',
     '        _ = prologueStart;',
     WEAVER, SHOULDLY),
    ('M6 values scanned after the entry call', W,
     '        var start = hasSubject && count > 0 ? IndexOf(0) + 1 : 0;',
     '        var start = statement.IndexOf(callInstruction) + 1;',
     WEAVER, SHOULDLY),
    ('M7 the customMessage and [CallerArgumentExpression] arguments captured', W,
     '            if (!notAValue)\n                continue;',
     '            if (true)\n                continue;',
     WEAVER, SHOULDLY + '|FullyQualifiedName~Shouldly5PreviewTests'),
    ('M8 the formatter\'s Shouldly branch removed', F,
     '            return FormatShouldly(expr, resolvedValues) ?? FormatTUnit(expr, resolvedValues) ?? expr;',
     '            return FormatTUnit(expr, resolvedValues) ?? expr;',
     CORE, 'FullyQualifiedName~AssertionExpressionFormatterTests|FullyQualifiedName~TrackThatTests'),
    ('M9 ClosureValueResolver without Shouldly', CV,
     '        else if (ShouldlyCall(expr) is var shouldly and >= 0)',
     '        else if (false && ShouldlyCall(expr) is var shouldly and >= 0)',
     CORE, 'FullyQualifiedName~ClosureValueResolverTests|FullyQualifiedName~TrackThatTests'),
    ('M15 the warning logged at Low', T,
     '            Log.LogWarning(subcategory: null, warningCode: "KRONIKOL001", helpKeyword: null, file: null,\n'
     '                lineNumber: 0, columnNumber: 0, endLineNumber: 0, endColumnNumber: 0,\n'
     '                message: "Kronikol.AssertionTracking: [assembly: TrackAssertions] is declared, but no assertion was instrumented: {0}.",\n'
     '                reason);',
     '            Log.LogMessage(MessageImportance.Low,\n'
     '                "Kronikol.AssertionTracking: [assembly: TrackAssertions] is declared, but no assertion was instrumented: {0}.",\n'
     '                reason);',
     WEAVER, TASK),
    ('M16 the warning logged without the attribute', T,
     '        _ => null,\n    };',
     '        _ => "mutated",\n    };',
     WEAVER, TASK),
    ('M17 [ShouldlyMethods] helpers woven inside', W,
     '            if (IsInOwnShouldlyMethodsType(type))\n                continue;\n',
     '',
     WEAVER, SHOULDLY),
    ('M18 #145 read but not written', G,
     '            Assertion = true,\n            step.Text,',
     '            step.Text,',
     CORE, MARK, True),
    ('M18b #145 written but not read', SC,
     '                case "assertion": step.Assertion = reader.TokenType == JsonTokenType.True; break;\n',
     '',
     CORE, MARK),
    ('M18c #145 not set in process', STEP,
     '                IsAssertion = true,\n',
     '',
     CORE, MARK),
    ('M18d #145 not set by the tests feed', FS,
     '            IsAssertion = true,\n',
     '',
     CORE, 'FullyQualifiedName~FeatureSynthesizerTests'),
    ('M18e #145 not set by the Cucumber merge', CU,
     '            IsAssertion = true,\n',
     '',
     CORE, 'FullyQualifiedName~CucumberFeatureMergerTests'),
    ('M18f #145 read on any version', IX,
     '        parsed >= AssertionMarkSince;',
     '        true;',
     CORE, MARK),
    ('M19 the marker constructor without its base call', W,
     '        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));\n'
     '        ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Call,\n'
     '            new MethodReference(".ctor", module.TypeSystem.Void, attributeType) { HasThis = true }));\n',
     '',
     WEAVER, 'FullyQualifiedName~StackAcrossStatementsTests|FullyQualifiedName~WovenIlVerifiesTests'),
    ('M20 no localsinit for a method the weave gave locals', W,
     '        if (method.Body.Variables.Count > 0)\n            method.Body.InitLocals = true;\n',
     '',
     WEAVER, SHOULDLY + '|FullyQualifiedName~AssertionWeaverTests'),
    ('M21 a null on the stack stored in a typed local', W,
     '        StackAnalysis.IsNull(slot) ? null : NewSlotLocal(method, slot);',
     '        NewSlotLocal(method, slot);',
     WEAVER, SHOULDLY),
    ('M22 a return inside the statement left as ret', W,
     '        foreach (var ret in Range(firstInstr, lastInstr).Where(i => i.OpCode == OpCodes.Ret).ToList())',
     '        foreach (var ret in Range(firstInstr, lastInstr).Where(i => false).ToList())',
     WEAVER, SHOULDLY + '|FullyQualifiedName~AssertionWeaverTests'),
    ('M23 a statement entered part-way woven', W,
     '        if (!awaited && EntersPartWay(method, start, end))',
     '        if (false && EntersPartWay(method, start, end))',
     WEAVER, 'FullyQualifiedName~StackAnalysisTests'),
    ('M24 the current directory searched before the references', W,
     '        var references = _searchDirectories.Length > 0;\n        if (references)\n',
     '        var references = _searchDirectories.Length > 0;\n        if (false)\n',
     WEAVER, 'FullyQualifiedName~Shouldly5PreviewTests'),
    ('M25 TUnit labels as raw code', F,
     '            return FormatShouldly(expr, resolvedValues) ?? FormatTUnit(expr, resolvedValues) ?? expr;',
     '            return FormatShouldly(expr, resolvedValues) ?? expr;',
     CORE, 'FullyQualifiedName~AssertionExpressionFormatterTests'),
    ('M26 Track.That counts brackets inside literals', CV,
     '        var literal = AssertionExpressionFormatter.LiteralMask(afterShould);',
     '        var literal = new bool[afterShould.Length];',
     CORE, 'FullyQualifiedName~ClosureValueResolverTests'),
    ('M27 a feed assertion record drawn without its source', IR,
     '            : AssertionNotePlantUml(Text, Passed ?? true, Message) + SourceComment();',
     '            : AssertionNotePlantUml(Text, Passed ?? true, Message);',
     CORE, 'FullyQualifiedName~InteractionRecordTests'),
    ('M28 unwoven statements listed at Low', T,
     '                Log.LogMessage(MessageImportance.Normal, "  {0}", unwoven);',
     '                Log.LogMessage(MessageImportance.Low, "  {0}", unwoven);',
     WEAVER, TASK),
]

def run(args, cwd):
    p = subprocess.run(args, cwd=cwd, capture_output=True, text=True, encoding='utf-8', errors='replace')
    return p.returncode, p.stdout + p.stderr

def main():
    only = sys.argv[1:]
    log = io.open(OUT, 'a', encoding='utf-8')
    for m in MUTATIONS:
        name, path, old, new, project, flt = m[:6]
        replace_all = len(m) > 6 and m[6]
        if only and not any(name.startswith(o + ' ') or name == o for o in only):
            continue
        full = os.path.join(MUT, path)
        backup = full + '.mutbak'
        text = io.open(full, encoding='utf-8', newline='').read()
        count = text.count(old)
        if count == 0 or (count != 1 and not replace_all):
            log.write(f'{name}: NOT APPLIED (pattern found {count} times)\n'); log.flush(); continue
        shutil.copy2(full, backup)
        try:
            io.open(full, 'w', encoding='utf-8', newline='').write(text.replace(old, new))
            code, out = run(['dotnet', 'build', project, '-f', 'net10.0', '-v:q', '-nologo'], MUT)
            if code != 0:
                errs = [l for l in out.splitlines() if ' error ' in l][:3]
                log.write(f'{name}: BUILD FAILED {errs}\n'); log.flush(); continue
            code, out = run(['dotnet', 'test', project, '-f', 'net10.0', '--no-build', '--filter', flt], MUT)
            summary = [l.strip() for l in out.splitlines() if l.strip().startswith(('Failed!', 'Passed!'))]
            failed = [l.strip()[len('Failed '):].split(' [')[0] for l in out.splitlines() if l.strip().startswith('Failed Kronikol')]
            verdict = 'KILLED' if code != 0 and failed else 'SURVIVED'
            log.write(f'{name}: {verdict} {summary} {failed[:4]}\n'); log.flush()
        finally:
            shutil.move(backup, full)
            os.utime(full, None)
    log.write('DONE\n'); log.close()

if __name__ == '__main__':
    main()
