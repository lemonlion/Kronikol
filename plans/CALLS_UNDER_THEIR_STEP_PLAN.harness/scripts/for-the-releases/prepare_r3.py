"""Local steps of R3's release in its worktree: the plan's record, results kept, version bumped, changelog. Nothing is committed or pushed here.

Usage: prepare_r3.py <worktree> <version> <previous> <date> <R1's version> <ingest issue> <Kronikol4J issue>
"""
import pathlib
import shutil
import subprocess
import sys

WT, version, previous, date, r1, f14, k4j = pathlib.Path(sys.argv[1]), sys.argv[2], sys.argv[3], sys.argv[4], sys.argv[5], sys.argv[6], sys.argv[7]
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
    ("mutations-land-r3.txt", "r3-mutations.txt"),
    ("full-r3.txt", "r3-whole-suite.txt"),
    ("examples-r3.txt", "r3-examples.txt"),
    ("e2e-r3.txt", "r3-e2e.txt"),
    ("r3-bp-release.txt", "r3-bp-release.txt"),
    ("l0-loop.txt", "r3-lightbdd-timer.txt"),
    ("release-slnf-r3.txt", "r3-release-slnf.txt"),
]:
    if (HERE / source).exists():
        text = (HERE / source).read_text(encoding="utf-8", errors="replace")
        (results / target).write_text(text, encoding="utf-8", newline="\n")
    else:
        print("missing", source)

for name in ["mutations_r3.py", "resolve_r3_on_223.py", "prepare_r3.py", "r3_docs.py", "r1_wiki_sections.py", "r1_docs.py"]:
    shutil.copyfile(HERE / name, scripts / name)

log = (HERE / "r3_log.txt").read_text(encoding="utf-8").replace("@VERSION@", version).replace("@DATE@", date).replace("@F14ISSUE@", f"#{f14}").replace("@K4JISSUE@", f"lemonlion/Kronikol4J#{k4j}").strip()
edit("plans/CALLS_UNDER_THEIR_STEP_PLAN.md", [
    (f"""R2 released as 5.28.4, R5 as 5.28.5 and R1 as {r1}. R3 follows, as §7.2 orders it, and R4 stays out, as Q2 recommends.**""",
     f"""R2 released as 5.28.4, R5 as 5.28.5, R1 as {r1} and R3 as {version}, which closes #225 but for ingest (F14, its own issue). R4 stays out, as Q2 recommends.**"""),
    ("""## 12. Log

""", f"""## 12. Log

{log}
"""),
])
edit("plans/CALLS_UNDER_THEIR_STEP_PLAN.harness/README.md", [
    (f"""and the `r1-*` files for R1, {r1} | R2 as released, 5.28.4:""", f"""the `r1-*` files for R1, {r1}, and the `r3-*` files for R3, {version} | R2 as released, 5.28.4:"""),
])
edit("plans/PLANS_STATUS.md", [
    ("""🟡 Executing, on the owner's word of 2026-11-04. **R2 released as 5.28.4**""", """✅ Executed, on the owner's word of 2026-11-04. **R2 released as 5.28.4**"""),
    ("""**R3** (a minor: a call names its sub-step, closes #225 but for ingest, F14) follows.""",
     f"""**R3 released as {version}**: a call names its sub-step, and #225 is closed but for ingest (F14, its own issue)."""),
    (f"""R4, a sub-step's own bar, stays out (Q2). | 5.28.4, 5.28.5, {r1} |""", f"""R4, a sub-step's own bar, stays out (Q2). | 5.28.4, 5.28.5, {r1}, {version} |"""),
])
edit("plans/ROADMAP.md", [
    (f"""| **R2 5.28.4 (#128 closed), R5 5.28.5 (#129 closed) and R1 {r1}, """, f"""| **Done: R2 5.28.4 (#128 closed), R5 5.28.5 (#129 closed), R1 {r1} and R3 {version}, """),
    ("""; R3 follows and closes #225 but for ingest.** **#225**""", """; #225 closed but for ingest, which has its own issue.** **#225**"""),
    (f"""| EXECUTING; green-lit in full 2026-11-04 by the owner, every §9 question as recommended | 5.28.4 (R2), 5.28.5 (R5), {r1} (R1) |""",
     f"""| SHIPPED; green-lit in full 2026-11-04 by the owner, every §9 question as recommended | 5.28.4 (R2), 5.28.5 (R5), {r1} (R1), {version} (R3) |"""),
    ("""R3 names a call's sub-step and closes #225 but for ingest. R4, a sub-step's own bar, stays out. |""", """A call names its sub-step. R4, a sub-step's own bar, stays out. |"""),
])

subprocess.run([sys.executable, "-I", str(HERE / "r3_docs.py"), "changelog", str(WT), version, previous, date], check=True)
edit("CHANGELOG.md", [("""its records have no flow to read (filed as its own issue).""", f"""its records have no flow to read (#{f14}).""")])

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
