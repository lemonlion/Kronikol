"""Teaches the driver and the readers cut 11, behind MUT_CUT, so the chain's later steps on cut 10 read as they did. One run. Writes new files, swapped in only if they compile."""
import os
import py_compile
import shutil


def swap(path, pairs):
    with open(path, encoding="utf-8", newline="") as handle:
        text = handle.read()
    for old, new in pairs:
        if text.count(old) != 1:
            raise SystemExit(f"{path}: expected one of [{old[:70]}], found {text.count(old)}")
        text = text.replace(old, new)
    fresh = path + ".new.py"
    with open(fresh, "w", encoding="utf-8", newline="") as handle:
        handle.write(text)
    py_compile.compile(fresh, doraise=True)
    os.replace(fresh, path)


swap("mutrun.py", [
    ("from mutations10 import MUTANTS, ORDER, PATCHED_FILES\n",
     'CUT = os.environ.get("MUT_CUT", "10")\nif CUT == "11":\n    from mutations11 import MUTANTS, ORDER, PATCHED_FILES\nelse:\n    from mutations10 import MUTANTS, ORDER, PATCHED_FILES\n'),
    ('PATCHED = os.path.join(MUT, "patched")\n', 'PATCHED = os.path.join(MUT, "patched11" if CUT == "11" else "patched")\n'),
    ('    "old": ["lightbdd"],\n',
     '    "rev6": ["rev6-a", "rev6-bddfy", "rev6-fetch", "rev6-lightbdd-legacy", "rev6-lightbdd-x2", "rev6-mix", "rev6-nunit", "rev6-reqnroll", "rev6-reqnroll-x3"],\n    "old": ["lightbdd"],\n'),
    ('    "rev5": os.path.join(MUT, "probes", "rev5"),\n', '    "rev5": os.path.join(MUT, "probes", "rev5"),\n    "rev6": os.path.join(MUT, "probes", "rev6"),\n'),
    ('ESCALATION = ["review", "rev4", "rev5", "old"]\n',
     '# Cut 11 has baselines over main, rev5, rev6 and rev3-plain only.\nESCALATION = ["rev5", "rev6"] if CUT == "11" else ["review", "rev4", "rev5", "old"]\n'),
    ('NEEDS_MATRIX_PACK = {("rev5", s) for s in ("rev5-reqnroll-x3", "rev5-reqnroll-tunit", "adapter-nunit4", "adapter-mstest", "adapter-tunit")}\n',
     'NEEDS_MATRIX_PACK = {("rev5", s) for s in ("rev5-reqnroll-x3", "rev5-reqnroll-tunit", "adapter-nunit4", "adapter-mstest", "adapter-tunit")}\nNEEDS_MATRIX_PACK |= {("rev6", "rev6-nunit"), ("rev6", "rev6-reqnroll-x3")}\n'),
    ('CONTROL_VERSION = "5.28.3-n00"\n', 'BASE_ROOT = "p00" if CUT == "11" else "n00"\nCONTROL_VERSION = "5.28.3-" + BASE_ROOT\n'),
    ('if not version.startswith(("5.28.3-m", "5.28.3-n")):', 'if not version.startswith(("5.28.3-m", "5.28.3-n", "5.28.3-p")):'),
    ('log = os.path.join(LOGS, "n00.pack.log")', 'log = os.path.join(LOGS, BASE_ROOT + ".pack.log")'),
    ('out = os.path.join(RESULTS, "n00", str(round_no), f"{family}-{suite}")', 'out = os.path.join(RESULTS, BASE_ROOT, str(round_no), f"{family}-{suite}")'),
])

swap("compare.py", [
    ('''    if BASELINE["root"] != "n00":
        return ()
''', '''    older = {"n00": ("m00",), "p00": ("m00", "n00")}.get(BASELINE["root"], ())
    if not older:
        return ()
'''),
    ('''        seen = collections.defaultdict(set)
        for directory in glob.glob(os.path.join(RESULTS, "m00", "*", f"{family}-{suite}")):
            path = os.path.join(directory, name)
            if os.path.exists(path):
                for test, lines in read(path).items():
                    seen[test].add(tuple(lines))
        _RACING_CACHE[key] = {test for test, outputs in seen.items() if len(outputs) > 1}
''', '''        varied = set()
        for root in older:
            seen = collections.defaultdict(set)
            for directory in glob.glob(os.path.join(RESULTS, root, "*", f"{family}-{suite}")):
                path = os.path.join(directory, name)
                if os.path.exists(path):
                    for test, lines in read(path).items():
                        seen[test].add(tuple(lines))
            varied |= {test for test, outputs in seen.items() if len(outputs) > 1}
        _RACING_CACHE[key] = varied
'''),
    ('BASELINE["root"] = "n00" if mutant_id.startswith("n") else "m00"\n', 'BASELINE["root"] = {"p": "p00", "n": "n00"}.get(mutant_id[0], "m00")\n'),
])

swap("marker.py", [
    ('compare.BASELINE["root"] = "n00" if mutant.startswith("n") else "m00"\n', 'compare.BASELINE["root"] = {"p": "p00", "n": "n00"}.get(mutant[0], "m00")\n'),
])

# LF, as the worktree has it, so the edits can be checked before the worktree moves to cut 11.
for folder, _, names in os.walk(os.path.join("..", "patched11")):
    for name in names:
        path = os.path.join(folder, name)
        with open(path, "rb") as handle:
            data = handle.read()
        with open(path, "wb") as handle:
            handle.write(data.replace(b"\r\n", b"\n"))
print("cut 11 taught")
