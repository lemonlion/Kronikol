"""One line per scenario: what a report marks as background against what the feature file writes, and whose copy each step is.

python -I shapes-table.py <kronikol-exe> <reports-dir> [--filter-feature PREFIX]

Reads the report only through `kronikol query`. A step's owner comes from its `Ran for` sub-step, whose `who` parameter names the
scenario that really ran it when the step definition ran (see shapes-suite/Steps/ShapeSteps.cs).
"""
import re
import subprocess
import sys

sys.stdout.reconfigure(encoding="utf-8")
kronikol, reports = sys.argv[1], sys.argv[2]

# What each feature file writes: leading Background steps per scenario. Filled by hand from shapes-suite/Features and checked
# against what Reqnroll's own pickles say in results/pickle-probe-*.tsv.
WRITTEN = {
    "01": lambda name: 1,
    "02": lambda name: 1,
    "03": lambda name: 1,
    "04": lambda name: 0,
    "05": lambda name: 0,
    "06": lambda name: 1,
    "07": lambda name: 2 if " a " in name else 1,
    "08": lambda name: 1,
    "09": lambda name: 0,
    "10": lambda name: 1,
    "11": lambda name: 1,
    "12": lambda name: 1,
    "13": lambda name: 1,
    "14": lambda name: 0,
    "15": lambda name: 1,
    "16": lambda name: 1 if "file a" in name else 0,
    "17": lambda name: 1,
    "18": lambda name: 0,
    "19": lambda name: 1,
    "20": lambda name: 1,
}


def query(*args):
    done = subprocess.run([kronikol, "query", *args], capture_output=True, encoding="utf-8", errors="replace")
    return done.stdout


scenario_line = re.compile(r"^(s\d+)\s+(pass|FAIL|skip\w*)\s+\S+\s+.*?calls?\s\s(.*)$", re.I)
# The first scenario to make a slow call carries its warm-up time after its name.
warm_up = re.compile(r"\s+\d+(\.\d+)?s warm-up$")
# Not "4 calls belong to no step", which is a count the same narrative prints under the steps.
step_line = re.compile(r"^(?P<mark>[✗!Ø·↷ ]?)\s*(?P<addr>b?\d+)\s+(?!calls? belong to no step)(?P<text>.+?)(?:\s{2,}.*)?$")



def owns(name, who):
    """An outline row's sub-step says `<title> #<row value>`; a plain scenario's starts with its name."""
    if " #" in who:
        return who.split(" #", 1)[1].split(" ")[0] in name.split(" ")
    return who.startswith(name)


rows = []
for line in query("scenarios", reports).splitlines():
    m = scenario_line.match(line)
    if not m:
        continue
    address, result, name = m.groups()
    name = warm_up.sub("", name)
    prefix = name[:2]
    tree = query("steps", reports, address).splitlines()
    top = []
    for i, raw in enumerate(tree):
        step = step_line.match(raw)
        if not step or raw[:1] not in ("✗", "!", "Ø", " ", "·", "↷"):
            continue
        # top-level steps are the two-column indent, with or without a status mark
        if not re.match(r"^.?\s?\s?(b?\d+)\s", raw) or "." in step.group("addr"):
            continue
        who = None
        for later in tree[i + 1:]:
            nested = step_line.match(later)
            if nested and "." not in nested.group("addr") and re.match(r"^.?\s?\s?(b?\d+)\s", later):
                break
            if later.strip().startswith("·") and later.strip()[1:].strip() == "who":
                continue
            who_match = re.match(r"^\s*·\s(\d\d [^·]+)$", later)
            if who_match and who is None:
                who = who_match.group(1).strip()
        top.append((step.group("addr"), step.group("mark").strip(), step.group("text"), who))
    marked = [t for t in top if t[0].startswith("b")]
    written = WRITTEN[prefix](name) if prefix in WRITTEN else None
    foreign = [t for t in top if t[3] and not owns(name, t[3])]
    rows.append((name, result, written, len(marked), len(top), top, foreign))

print("written = leading steps the feature file's Background gives this scenario; marked = steps the report addresses b0, b1, ...; borrowed = steps whose sub-step names another scenario\n")
print(f"{'scenario':<28}{'result':<7}{'written':<8}{'marked':<7}{'steps':<6}{'borrowed':<9}verdict")
for name, result, written, marked, total, top, foreign in sorted(rows):
    verdict = "ok" if written == marked and not foreign else ""
    if written is not None and written > marked:
        verdict = "MISSED" if marked == 0 else "PARTLY MISSED"
    if written is not None and marked > written:
        verdict = "INVENTED"
    if foreign:
        verdict = (verdict + " BORROWED").strip() if verdict != "ok" else "BORROWED"
    print(f"{name[:27]:<28}{result:<7}{str(written):<8}{marked:<7}{total:<6}{len(foreign):<9}{verdict}")

print("\nThe steps:")
for name, result, written, marked, total, top, foreign in sorted(rows):
    print(f"\n{name} [{result}]")
    for addr, mark, text, who in top:
        print(f"  {mark or ' '} {addr:<4} {text}  <- {who}")
