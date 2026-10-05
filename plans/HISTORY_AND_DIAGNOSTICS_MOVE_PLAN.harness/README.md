# Harness for `HISTORY_AND_DIAGNOSTICS_MOVE_PLAN.md`

Read on 2026-10-05 at Kronikol 4.5.0 (`main` at `425d9bad`). Every line number in these files is for that commit.
Nothing was run except the checks the plan marks RUN, so there is no output here yet. S0's measuring scripts and their
results go in this folder when the plan is executed.

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
