# Harness for `HISTORY_AND_DIAGNOSTICS_MOVE_PLAN.md`

Read on 2026-10-05 at Kronikol 4.5.0 (`main` at `425d9bad`). Every line number in these files is for that commit.
The research below ran nothing except the checks the plan marks RUN; what was run while executing the plan is in the
second table.

`research/` holds the four inventories the plan was written from. Each was read from the code, the tests, the wiki and
the plans without opening a generated report. They are more detailed than the plan, which cites the parts it relies on.

| File | What it is |
|---|---|
| `research/server-side.md` | Where the history and diagnostics markup is drawn, the public API that carries it, the output list and its bookkeeping (`PlannedFiles`, `Run.json`, the rotation, the pointer, the Azure DevOps upload), the merge and ingest paths, DiagnosticReport.html, and a checklist of everything a new file in the reports directory must be known to |
| `research/client-side.md` | The CSS rules with their byte sizes, the `$verdict` search code line by line, what the export does with history, and how the report resolves `#sid-` links |
| `research/tests.md` | Every unit, search-engine and Playwright fact that touches either feature, each classified as moving, turning round or unaffected, with the fixtures, helpers and CI lanes involved |
| `research/docs-and-consumers.md` | The wiki pages and lines to change, the changelog history of both features, the repository docs and automation, the plans that overlap (DASHBOARD_PLAN.md above all), Kronikol4J's divergence ledger with an example entry, and BreakfastProvider |

The plan's section 11 records the owner's decisions of 2026-10-05, which came after these inventories: they describe
the code as it stood, not the design.

Added while executing the plan:

| Path | What it is |
|---|---|
| `mutations/mutate.py` | `apply <name>...` / `revert` / `list`: the source mutations behind the proofs (exact replacements, each matching once), `f1-*` for R1 and `r2-*` for R2. A file is copied aside before its first mutation and `revert` restores the copy, so it can run in a checkout with uncommitted work; `KRONIKOL_ROOT` names the checkout |
| `mutations/r2-results.txt` | Each `r2-*` mutation applied alone in a snapshot of R2's tree: the facts it turned red |
| `r1/red-4.5.0-unit.txt`, `r1/red-4.5.0-e2e.txt` | R1's new facts run in a worktree at v4.5.0 (tests copied in, one stub for the new seam): which failed |
| `r2/red-4.5.1-unit.txt`, `r2/red-4.5.1-e2e.txt` | R2's behaviour facts run in a worktree at v4.5.1, with the two new options stubbed as properties that do nothing: which failed, and why |
| `r2/cascade.txt` | The cascade check: every computed style of the history and diagnostics elements of the every-section report, 4.5.1 against 4.6.0 |
| `s0/` | S0: `run.sh` runs the ReqNRoll xUnit v3 example once against a ledger (`S0`, `KRONIKOL_ROOT`), `measure.py` sizes the copies it keeps, `results.txt` is what it measured on 4.5.1 with ten earlier runs, `runs.txt` the runs |
| `k4j/ledger-4.5.1.md`, `k4j/ledger-4.6.0.md` | The Kronikol4J divergence ledger entries, appended after each release was published |
| `wiki/r1_wiki.py` | R1's wiki edits, applied to a wiki checkout after 4.5.1 is published |
