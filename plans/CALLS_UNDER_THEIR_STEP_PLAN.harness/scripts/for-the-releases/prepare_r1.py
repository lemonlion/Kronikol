"""Local steps of R1's release in its worktree: the plan's record, results kept, version bumped, changelog. Nothing is committed or pushed here.

Usage: prepare_r1.py <worktree> <version> <previous> <date>
"""
import pathlib
import shutil
import subprocess
import sys

WT, version, previous, date = pathlib.Path(sys.argv[1]), sys.argv[2], sys.argv[3], sys.argv[4]
HERE = pathlib.Path(__file__).parent
H = WT / "plans/CALLS_UNDER_THEIR_STEP_PLAN.harness"
results = H / "results"
scripts = H / "scripts/for-the-releases"


def edit(path, reps):
    p = WT / path
    s = p.read_bytes().decode("utf-8")
    crlf = "\r\n" in s
    s = s.replace("\r\n", "\n")
    for old, new in reps:
        assert s.count(old) == 1, (path, old[:80], s.count(old))
        s = s.replace(old, new)
    p.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))


for source, target in [
    ("r1-review-red.txt", "r1-red-proof-first-review.txt"),
    ("r1-review2-red.txt", "r1-red-proof-second-review.txt"),
    ("r1-foreign-red.txt", "r1-red-proof-foreign-thread.txt"),
    ("r1-limits-red.txt", "r1-red-proof-by-id.txt"),
    ("mutations-land-r1.txt", "r1-mutations.txt"),
    ("mutations-land-r1b.txt", "r1-mutations-last-three.txt"),
    ("mutations-land-r1h.txt", "r1-mutations-real-engines.txt"),
    ("full-r1.txt", "r1-whole-suite.txt"),
    ("examples-r1.txt", "r1-examples.txt"),
    ("e2e-r1.txt", "r1-e2e.txt"),
    ("lanes-r1.txt", "r1-lanes.txt"),
    ("r1-bp-release.txt", "r1-bp-release.txt"),
    ("perf-r1.txt", "r1-perf.txt"),
]:
    if (HERE / source).exists():
        text = (HERE / source).read_text(encoding="utf-8", errors="replace")
        (results / target).write_text(text, encoding="utf-8", newline="\n")
    else:
        print("missing", source)

for name in ["mutations_r1.py", "resolve_223r1.py", "resolve_223r1b.py", "r1_on_529.py", "prepare_r1.py", "r1_docs.py"]:
    shutil.copyfile(HERE / name, scripts / name)

log = (HERE / "r1_log.txt").read_text(encoding="utf-8").replace("@VERSION@", version).replace("@DATE@", date).strip()
edit("plans/CALLS_UNDER_THEIR_STEP_PLAN.md", [
    ("""R2 released as 5.28.4 and R5 as 5.28.5. R1 and R3 follow #223's R1, as §7.2 orders them, and R4 stays out, as Q2 recommends.**""",
     f"""R2 released as 5.28.4, R5 as 5.28.5 and R1 as {version}. R3 follows, as §7.2 orders it, and R4 stays out, as Q2 recommends.**"""),
    ("""## 12. Log

""", f"""## 12. Log

{log}
"""),
])
edit("plans/CALLS_UNDER_THEIR_STEP_PLAN.md", [
    ("| What logging a record costs (`perf/`) | §6.3 | On `10bbbe81`, within the noise on each of the three paths, at most +0.17 µs on a record of 1.4 to 1.7 µs (`results/perf-final.txt`).",
     "| What logging a record costs (`perf/`) | §6.3 | Measured again for R1, as this row's project ignored the version it was given, so it compared one version with itself (§12). Written first as: on `10bbbe81`, within the noise on each of the three paths, at most +0.17 µs on a record of 1.4 to 1.7 µs (`results/perf-final.txt`)."),
])
edit("plans/CALLS_UNDER_THEIR_STEP_PLAN.harness/README.md", [
    ("""`r5-bp-release.txt` for R5, 5.28.5 | R2 as released, 5.28.4:""", f"""`r5-bp-release.txt` for R5, 5.28.5, and the `r1-*` files for R1, {version} | R2 as released, 5.28.4:"""),
])
edit("plans/PLANS_STATUS.md", [
    ("""**R1** (a patch: the current step follows the flow, the stamp reads it, a trace join, a skip's completion taken by the skip) waits for #223's R1, and **R3** (a minor: a call names its sub-step, closes #225 but for ingest, F14) follows R1.""",
     f"""**R1 released as {version}**: the current step follows the flow, the stamp reads it, and a trace join reads a service's onward call. **R3** (a minor: a call names its sub-step, closes #225 but for ingest, F14) follows."""),
    ("""R4, a sub-step's own bar, stays out (Q2). | 5.28.4, 5.28.5 |""", f"""R4, a sub-step's own bar, stays out (Q2). | 5.28.4, 5.28.5, {version} |"""),
])
s = (WT / "plans/ROADMAP.md").read_bytes().decode("utf-8")
eol = "\r\n" if "\r\n" in s else "\n"
lines = s.split(eol)
last = max(i for i, l in enumerate(lines) if l.startswith("| 1.3") and l.split("|")[1].strip().startswith("1."))
number = int(lines[last].split("|")[1].strip().split(".")[1]) + 1
lines.insert(last + 1, f"| 1.{number} | **R2 5.28.4 (#128 closed), R5 5.28.5 (#129 closed) and R1 {version}, {date}; R3 follows and closes #225 but for ingest.** **#225** A call made in a sub-step could only ever read its top-level step, as a call's step was guessed back from the bars drawn, and steps that overlap were kept on one stack, so they nested and took each other's failures (`CALLS_UNDER_THEIR_STEP_PLAN.md`). |")
plan_row = next(i for i, l in enumerate(lines) if l.startswith("| `STEP_IDENTITY_PLAN`"))
lines.insert(plan_row + 1, f"| `CALLS_UNDER_THEIR_STEP_PLAN` (2026-11-02) | EXECUTING; green-lit in full 2026-11-04 by the owner, every §9 question as recommended | 5.28.4 (R2), 5.28.5 (R5), {version} (R1) | 1.{number} | #225, #128 and #129: a call's step is known when it is logged, and steps that overlap keep their own places. R3 names a call's sub-step and closes #225 but for ingest. R4, a sub-step's own bar, stays out. |")
for i, l in enumerate(lines):
    if "#223 → 1.36" in l:
        lines[i] = l.replace("#223 → 1.36", f"#223 → 1.36 · #225 → 1.{number}")
    if "`STEP_IDENTITY` → 1.36" in l:
        lines[i] = l.replace("`STEP_IDENTITY` → 1.36", f"`STEP_IDENTITY` → 1.36 · `CALLS_UNDER_THEIR_STEP` → 1.{number}")
(WT / "plans/ROADMAP.md").write_bytes(eol.join(lines).encode("utf-8"))

subprocess.run([sys.executable, "-I", str(HERE / "r1_docs.py"), "changelog", str(WT), version, previous, date], check=True)

for path, old, new in [
    ("Directory.Build.props", f"<Version>{previous}</Version>", f"<Version>{version}</Version>"),
    (".claude-plugin/marketplace.json", previous, version),
    (".claude-plugin/plugin.json", previous, version),
    ("templates/github-actions/kronikol-history/VERSION", previous, version),
]:
    p = WT / path
    t = p.read_bytes().decode("utf-8")
    assert t.count(old) >= 1, path
    p.write_bytes(t.replace(old, new).encode("utf-8"))
print("prepared", version)
