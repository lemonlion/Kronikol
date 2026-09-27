# PR_REPORT_LINK_PLAN harness

The probe behind §1.5 of [`../PR_REPORT_LINK_PLAN.md`](../PR_REPORT_LINK_PLAN.md), with the output it printed on
2026-09-27. Node 18 or later, no packages.

| Script | What it answers | Output |
|---|---|---|
| `probe.js` | Runs the script of PR #73's `action.yml` the way actions/github-script does, as an async function given `github`, `context`, `core` and `process`, against an in-memory GitHub, as `PrReportLinkActionTests`' own driver does. P1: the comment it writes for the README's two lanes. P2: whether a line whose tag carries a field this version does not know still stops an older run. P3: whether a `label` with a line break keeps its line. P4: whether text outside the lines survives a rewrite | `results-probe.txt` |
| (commands in the file's header) | S1 rehearsed (plan §1.2, §1.4): the PR rebased onto `main` at `2de961ec`, its 17 facts, all of `Kronikol.Tests` with the skip reasons and the three root-only failures, the same three on `main` without the PR, and the `Kronikol.Templates` pack | `results-s1-rehearsal.txt` |
| (a research agent, web) | §1.6's platform facts: each question's answer, verification mark, addresses and quotes | `web-sources.md` |

Run it against the pull request's head:

```bash
git fetch origin pr-report-link-template
git show origin/pr-report-link-template:templates/github-actions/kronikol-pr-report-link/action.yml > action.yml
node probe.js action.yml
```

Against a later version of the action, point it at that `action.yml`. After S2 of the plan, P2 and P3 should
report no write by the older run, and P1 should equal the README's sample.
