"""#213 mutations (plan 6.3) on a worktree holding the fix: each mutant is applied to AssertionWeaver.cs, the weaver
test project built, CapturedFieldValueTests run, and the failing facts recorded; the source is restored from a copy
(with a fresh mtime) after every mutant.

Usage: python -I -u mutate213.py <worktree> <pristine AssertionWeaver.cs> <results file> [mutant ids...]
"""
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from collections import Counter
from pathlib import Path

W = Path(sys.argv[1])
PRISTINE = Path(sys.argv[2])
OUT = Path(sys.argv[3])
ONLY = set(sys.argv[4:])
SRC = W / "src/Kronikol.AssertionTracking/AssertionWeaver.cs"
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}

M1_FROM = "FieldOfInstance(method, lambdaFieldRef, lambdaVarName, outerThisField) is not { } variable ||"
M1_TO = ("(lambdaFieldRef.Name.EndsWith(\">P\", StringComparison.Ordinal) ? new CapturedVariable { Name = lambdaVarName, "
         "StateField = lambdaFieldRef, NeedsBoxing = lambdaFieldRef.FieldType.IsValueType, Type = lambdaFieldRef.FieldType } "
         ": FieldOfInstance(method, lambdaFieldRef, lambdaVarName, outerThisField)) is not { } variable ||")
DROP_FROM = """        foreach (var unloadable in assertion.CapturedVariables.Where(cv => !CanLoad(method, cv)).ToList())
        {
            assertion.CapturedVariables.Remove(unloadable);
            assertion.CarriedAfter.Remove(unloadable);
        }"""

DETECT_FROM = "captured.RemoveAll(variable => !CanBeRead(variable.Type, method) || !CanLoad(method, variable));"
DETECT_TO = "captured.RemoveAll(variable => !CanBeRead(variable.Type, method));"

MUTANTS = {
    "M1": [(M1_FROM, M1_TO)],
    "M2": [("!(ownThis && receiver.OpCode == OpCodes.Ldarg_0 ||", "!(true ||")],
    "M3": [("return method.HasThis && IsFieldAccessibleOnType(variable.StateField, method.DeclaringType) &&",
            "return IsFieldAccessibleOnType(variable.StateField, method.DeclaringType) &&")],
    "M4": [(M1_FROM, M1_TO), (DROP_FROM, ""), (DETECT_FROM, DETECT_TO)],
    "M13": [("var ownThis = lambdaMethod.HasThis && HeldByTheMethod(lambdaMethod.DeclaringType);",
             "var ownThis = lambdaMethod.HasThis && !lambdaMethod.DeclaringType.Name.StartsWith(\"<\", StringComparison.Ordinal);")],
    "M14": [(" || IsClosureLink(receiverField) && HeldByTheMethod(receiverField.FieldType)", "")],
    "M15": [("(prefix.OpCode == OpCodes.Volatile || prefix.OpCode == OpCodes.Unaligned)", "false")],
    "M16": [("                     !targets.Contains(statementInstructions[i + 1]))", "                     true)")],
    "M17": [(" ||\n                      statementInstructions[i + 1].OpCode == OpCodes.Ldflda && IsThisProxy(fieldRef))", ")")],
    "M18": [("if (lambdaMethod?.HasBody == true && IsCompilerLambda(lambdaMethod))", "if (lambdaMethod?.HasBody == true)")],
    "M19": [(DETECT_FROM, DETECT_TO)],
    "M5": [("!(IsCall(receiver, \"Constant\") &&", "!(true ||")],
    "M6": [(" ||\n                      receiver.OpCode == OpCodes.Ldnull && IsStaticField(tokenFieldRef)))", "))")],
    "M7": [("!HoldsTheInstance(method, receiverLocal))", "false)")],
    "M8": [("var fieldType = ResolveGenericFieldType(field, method.DeclaringType);", "var fieldType = field.FieldType;")],
    "M9": [("StateField = outerThis is FieldDefinition definition ? OwnField(definition) : outerThis,", "StateField = outerThis,")],
    "M11": [("if (method.HasThis && IsFieldAccessibleOnType(field, method.DeclaringType))",
             "if (outerThis == null && method.HasThis && IsFieldAccessibleOnType(field, method.DeclaringType))")],
    "M12": [("            assertion.CarriedAfter.Remove(unloadable);\n", "")],
}


def run(args, log):
    with open(log, "wb") as f:
        return subprocess.run(args, cwd=W, stdout=f, stderr=subprocess.STDOUT).returncode


def restore():
    SRC.write_bytes(PRISTINE.read_bytes())


pristine = PRISTINE.read_bytes().decode("utf-8")
results = []
for mid, edits in MUTANTS.items():
    if ONLY and mid not in ONLY:
        continue
    text = pristine
    for old, new in edits:
        count = text.count(old)
        if count != 1:
            sys.exit(f"{mid}: expected one match, found {count}: {old[:80]!r}")
        text = text.replace(old, new)
    SRC.write_bytes(text.encode("utf-8"))
    started = time.time()
    logs = OUT.parent / f"mutant-{mid}"
    logs.mkdir(exist_ok=True)
    build = run(["dotnet", "build", "tests/Kronikol.Tests.AssertionTracking", "-c", "Debug", "-f", "net10.0", "-nologo"],
                logs / "build.txt")
    if build != 0:
        restore()
        results.append(f"{mid}: BUILD FAILED (see {logs / 'build.txt'})")
        print(results[-1], flush=True)
        continue
    trx = logs / "run.trx"
    run(["dotnet", "test", "tests/Kronikol.Tests.AssertionTracking", "-c", "Debug", "-f", "net10.0", "--no-build",
         "--filter", "FullyQualifiedName~CapturedFieldValueTests", "--logger", f"trx;LogFileName={trx}"], logs / "run.txt")
    restore()
    failed = Counter()
    if trx.exists():
        for r in ET.parse(trx).getroot().iterfind(".//t:UnitTestResult", NS):
            if r.get("outcome") == "Failed":
                failed[r.get("testName").split("(")[0].split(".")[-1]] += 1
    verdict = "CAUGHT" if failed else ("SURVIVED" if trx.exists() else "NO RESULTS (crash?)")
    results.append(f"{mid}: {verdict} in {time.time() - started:.0f}s; " +
                   ", ".join(f"{k} x{v}" for k, v in sorted(failed.items())))
    print(results[-1], flush=True)

restore()
OUT.write_text("\n".join(results) + "\n", encoding="utf-8")
print("restored; rebuild before any --no-build run", flush=True)
