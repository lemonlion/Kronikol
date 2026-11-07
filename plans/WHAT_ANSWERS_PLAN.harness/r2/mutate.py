# R2's mutations (plans/WHAT_ANSWERS_PLAN.md 6.2): each is applied alone, the four classes nearest the change run, and the
# file restored. A mutation is caught when at least one fact fails. usage: python -I mutate-r2.py <worktree> > out.txt
import os, re, subprocess, sys

root = sys.argv[1]
handler = "src/Kronikol/Tracking/TestTrackingMessageHandler.cs"
classes = "FullyQualifiedName~HandlerTestDoubleTests|FullyQualifiedName~HandlerChainTests|FullyQualifiedName~TestTrackingMessageHandlerTests|FullyQualifiedName~CallOwnSpanThroughTheHandlerTests"
line = "        var testDouble = _testDouble is null ? chain.AnsweredBy : string.IsNullOrWhiteSpace(_testDouble) ? null : _testDouble;"

mutations = [
    ("the option ignored", handler, line, "        var testDouble = chain.AnsweredBy;"),
    ("empty read as a name", handler,
     "string.IsNullOrWhiteSpace(_testDouble) ? null", "(_testDouble.Length > 0 && string.IsNullOrWhiteSpace(_testDouble)) ? null"),
    ("white space read as a name", handler,
     "string.IsNullOrWhiteSpace(_testDouble) ? null", "string.IsNullOrEmpty(_testDouble) ? null"),
    ("request half only", handler,
     "            NamedByRemoteHostFallback = namedByRemoteHostFallback,\n            TestDouble = testDouble\n        });\n\n        return response;",
     "            NamedByRemoteHostFallback = namedByRemoteHostFallback,\n            TestDouble = chain.AnsweredBy\n        });\n\n        return response;"),
    ("the walk's mark beating a declared one", handler, line,
     "        var testDouble = chain.AnsweredBy ?? (_testDouble is null || string.IsNullOrWhiteSpace(_testDouble) ? null : _testDouble);"),
    ("the thrown send unmarked", handler,
     "                TestDouble = testDouble\n            });\n            throw;", "                TestDouble = null\n            });\n            throw;"),
]

for name, rel, old, new in mutations:
    path = os.path.join(root, rel)
    original = open(path, encoding="utf-8").read()
    assert original.count(old) == 1, (name, old)
    open(path, "w", encoding="utf-8", newline="").write(original.replace(old, new))
    try:
        run = subprocess.run(["dotnet", "test", "tests/Kronikol.Tests/Kronikol.Tests.csproj", "--filter", classes],
                             cwd=root, capture_output=True, text=True, encoding="utf-8", errors="replace")
        out = run.stdout + run.stderr
        summary = re.findall(r"(Failed!|Passed!)\s+-\s+Failed:\s+(\d+), Passed:\s+(\d+)", out)
        failed = sorted({m.split(".")[-1].split("(")[0] for m in re.findall(r"^\s+Failed (\S+)", out, re.M)})
        errors = re.findall(r"error CS\d+[^\[]*", out)
        verdict = "BUILD ERROR " + errors[0][:150] if errors else (f"caught: {summary[-1][1]} failed" if summary and summary[-1][1] != "0" else "NOT CAUGHT")
        print(f"- {name}: {verdict}")
        for f in failed[:6]:
            print(f"    {f}")
        sys.stdout.flush()
    finally:
        open(path, "w", encoding="utf-8", newline="").write(original)
