"""Reads the run files run.sh writes and prints, per file, each run's outcome and the notes its report holds.

    python -I outcomes.py results/<label>-<config>-<name>.txt ...

passed / failed: <first line of the message> / crashed: <the frame above the test's own, the process exit code
the runner printed, if any>. Notes are printed once per distinct set across the three runs.
"""
import re
import sys

sys.stdout.reconfigure(encoding="utf-8")


def runs(text):
    parts = re.split(r"^=+ run (\d) of 3: .*$", text, flags=re.M)
    # parts: [preamble, n, body, n, body, ...]
    for k in range(1, len(parts), 2):
        yield int(parts[k]), parts[k + 1]


def outcome(body):
    if "Test host process crashed" in body or "Test process crashed" in body or "Catastrophic failure" in body:
        # the first stack the runtime printed, from the innermost frame to the test's own
        frames = re.findall(r"^\s+at (\S+?)\(", body, flags=re.M)
        chain = []
        for f in frames:
            chain.append(f.split(".")[-1] if not f.startswith("WeaveRepro.") else f.replace("WeaveRepro.", ""))
            if f.startswith("WeaveRepro."):
                break
        code = re.search(r"exit code (-?\d+)", body.split("exit code:")[0])
        return "crashed (AccessViolationException%s: %s%s)" % (
            "" if "AccessViolationException" in body else " NOT SEEN",
            " < ".join(chain),
            "; process exit code %s" % code.group(1) if code else "")
    passed = re.findall(r"^\s+Passed (WeaveRepro\.\S+)", body, flags=re.M)
    failed = re.findall(r"^\s+Failed (WeaveRepro\.\S+)", body, flags=re.M)
    if failed:
        msg = re.search(r"Error Message:\s*\n\s*(.+)", body)
        return "failed: %s (%s)" % (", ".join(failed), msg.group(1).strip() if msg else "?")
    if passed:
        return "passed (%d test%s)" % (len(passed), "" if len(passed) == 1 else "s")
    return "no result"


def notes(body):
    if "report: written" not in body:
        return None
    return tuple(re.findall(r"^--- (s\d+/d0/n\d+) in full\n(.+)$", body, flags=re.M))


for path in sys.argv[1:]:
    text = open(path, encoding="utf-8", errors="replace").read()
    print("#", path.replace("\\", "/").split("/")[-1])
    seen = {}
    for n, body in runs(text):
        exit_code = re.search(r"^exit code: (\d+)", body, flags=re.M)
        print("  run %d: %s; dotnet test exit %s" % (n, outcome(body), exit_code.group(1) if exit_code else "?"))
        ns = notes(body)
        if ns is not None:
            seen.setdefault(ns, []).append(n)
    for ns, which in seen.items():
        print("  notes (run%s %s):" % ("s" if len(which) > 1 else "", ", ".join(map(str, which))))
        for addr, text_ in ns:
            print("    %s  %s" % (addr, text_))
