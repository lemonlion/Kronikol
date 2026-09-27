# PR_REPORT_LINK_PLAN harness

The evidence behind [`../PR_REPORT_LINK_PLAN.md`](../PR_REPORT_LINK_PLAN.md), gathered on 2026-09-27: the probes
of §1.5, the rehearsals of §1.2 and §1.9, GitHub's records of §1.10 and the web research of §1.6. Node 18 or
later for the probe, no packages.

| File | What it answers | Plan |
|---|---|---|
| `probe.js` | Runs the script of PR #73's `action.yml` the way actions/github-script does, as an async function given `github`, `context`, `core` and `process`, against an in-memory GitHub, as `PrReportLinkActionTests`' own driver does. P1: the comment it writes for the README's two lanes. P2: whether a line whose tag carries a field this version does not know still stops an older run. P3: whether a `label` with a line break keeps its line. P4: whether text outside the lines survives a rewrite. P5: what the comment shows when two lanes pass different `heading` and `report-file` inputs. P6: what a field written before the run id does | §1.5 |
| `results-probe.txt` | `probe.js`'s output for the PR's `action.yml`, and for the one with S2's fixes | §1.5 |
| `results-s1-rehearsal.txt` | S1 rehearsed: the PR rebased onto `main` at `2de961ec`, its 17 facts, all of `Kronikol.Tests` with the skip reasons and the three root-only failures, the same three on `main` without the PR, and the `Kronikol.Templates` pack | §1.2, §1.4 |
| `rehearsal/*.patch` | S2 and S3 as rehearsed on the PR rebased onto `5ca0878a`: the facts (`s2-1`), the fixes (`s2-2`), Q5's end marker (`s2-3`) and the live lane (`s3`). Applied in that order they rebuild the rehearsal's tree exactly | §1.9, §4.2, §4.3 |
| `results-s2-s3-rehearsal.txt` | What the patches did: the five red facts and why, 22, 23 and 24 green, all of `Kronikol.Tests`, actionlint on each workflow, the lane's test step run locally, two projects in one `dotnet test` (F12), and `main`'s failing CI log line | §1.9 |
| `github-records.md` | PR #73's state and checks, every re-run in the repository, how pull requests were merged, `main`'s protection and its CI on 2026-09-27 | §1.10 |
| `web-sources.md` | §1.6's platform facts: each question's answer, verification mark, addresses and quotes | §1.6 |
| `wiki-draft.md` | The words S6 adds to the wiki's CI-Artifact-Upload page, for the owner to read before the plan is green-lit | §4.6 |

Run the probe against the pull request's head:

```bash
git fetch origin pr-report-link-template
git show origin/pr-report-link-template:templates/github-actions/kronikol-pr-report-link/action.yml > action.yml
node probe.js action.yml
```

Against a later version of the action, point it at that `action.yml`. After S2 of the plan, P2 and P3 report no
write by the older run, and P1 equals the README's sample.

Apply the rehearsal to the rebased head, from the repository's root (plan §4.0 step 7 checks they still apply;
leave `s2-3` out if Q5 is no, and apply `s2-1` alone first to see its five failures):

```bash
for p in s2-1-facts s2-2-fixes s2-3-q5-end-marker s3-lane; do
  git apply --index plans/PR_REPORT_LINK_PLAN.harness/rehearsal/$p.patch
done
```
