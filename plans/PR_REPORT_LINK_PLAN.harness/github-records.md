# GitHub's own records, read 2026-09-27 (plan §1.10)

Read-only, through the GitHub MCP tools and REST GETs (marked REST), by a research agent of the session that
deepened the plan, and relayed by it. Ids are given so each fact can be re-read. Nothing was written to GitHub.

## PR #73 and issue #72

- Head `e31b42fac2e4b022114e31ff2386c8ff9d5a7e19`, one commit (2026-09-15T14:09:55Z, parent `49f5ea87`), +892 −1 in
  six files. Open, not a draft, no labels. The body's "## Related issue" section reads "Closes #72", and #72's
  `closed_by_pull_requests` lists #73.
- REST: `mergeable: false`, `mergeable_state: "dirty"`, `rebaseable: false`, `merge_commit_sha: null`,
  `maintainer_can_modify: false`. The base sha GitHub stores is `071df346` (main on 2026-09-21); the merge base
  is `49f5ea87`; main at `5ca0878a` is 76 commits past it.
- No reviews, no review comments, no issue comments, no requested reviewers.
- Issue #72: open, label `enhancement`, no comments, created 2026-09-15T13:59:06Z.

## PR #73's checks

- 30 check runs on the head, all `completed/success`: the 28 jobs of CI run 34979831198 (CI #873,
  `pull_request`, attempt 1, 14:10:19Z to 14:18:37Z), the CodeQL workflow's "CodeQL Analysis" job (run
  34979830961) and the code-scanning check "CodeQL" ("No new alerts in code changed by this pull request").
- **CodeQL analyses C# only.** No "Analyze (actions)" check; the job uploaded `csharp.sarif` alone; the
  repository's five workflows include no default-setup CodeQL workflow. REST code-scanning/default-setup and
  /analyses answered 403 "Resource not accessible by integration".

## Re-runs in this repository

- The 500 newest runs of every workflow (2026-09-03T08:10:40Z to 2026-09-27T20:40:40Z; 3,129 runs in all), and
  every CI Summary Preview run (run numbers 1 to 723, 2026-04-09 to 2026-09-27), were read.
- Four re-runs, none with an artifact: 35704781520 (CI, attempt 2), 34948759738 (Release, attempt 2), 34899211911
  (CI, attempt 2), 34748893265 (Release, attempt 4). CI Summary Preview, the one workflow that uploads, has never
  been re-run (723 of 723 at attempt 1; the `kronikol-history` ledger's 84 "Record run <id>:<attempt>" commits
  all end ":1").
- So this repository holds no evidence of what GitHub does with a same-name upload in a later attempt.
- Run 36348913868 (CI Summary Preview, four artifacts): ids do not follow `created_at` order (history-3,
  10941691604, was created first), and the API lists them by id, descending. Every `expires_at` is
  2026-12-26T20:40:40Z: the run's `created_at` plus the 90 retention days, to the second, not each upload's time
  plus 90 days.

## How pull requests were merged

- 23 pull requests: 20 merged, two closed unmerged (#7, #92), #73 open. Every merged one (#1 to #6, #8 to #13,
  #15 to #20, #26, #27) landed as a two-parent "Merge pull request #N from …" commit made by GitHub. No squash
  merge and no rebase merge.
- Since #27 (2026-05-03), main has moved by direct push: 517 first-parent commits, one local merge (`81dfe93a`,
  2026-08-22).
- REST repository settings: `allow_rebase_merge: true`, `allow_merge_commit: true`, `allow_squash_merge: true`,
  `allow_auto_merge: false`, `allow_update_branch: false`, `delete_branch_on_merge: false`.

## Protection on main

- `protected: true`, the only protected branch. REST (no auth) `branches/main`: `required_status_checks`
  enforcement "off", no contexts and no checks. No rulesets (`rules/branches/main` and `/rulesets` are empty).
- Required reviews could not be read (REST `branches/main/protection`: 403 "Resource not accessible by
  integration"). Direct pushes to main are routine, so none is enforced on the owner.

## main's CI on 2026-09-27 (read again by the plan's author)

| Run | Commit | Conclusion |
|---|---|---|
| 36321789234 | `b31463e6` (3.31.8) | success |
| 36323047132 | `e4c9e360` (3.31.9) | failure |
| 36330985179 | `2de961ec` (the base of S1's first rehearsal) | failure |
| 36331441144 | `dc619d35` | failure |
| 36342769330 | `b03d8767` (3.31.10's release commit) | failure |
| 36344296790 | `44b222d5` | failure |
| 36345166781 | `571a98dc` (3.31.10) | success |
| 36348913904 | `5ca0878a` (the base of the S2 and S3 rehearsal) | failure |

The four from `e4c9e360` to `b03d8767` failed E2E (Popups & Flows), per the commit message of `44b222d5`. That
commit, a first fix, failed `ProcessGlobalStoreTests` instead, per the message of `571a98dc`, which turned CI
green. `5ca0878a` failed one Core Tests fact of
5,951, `NodeJsPlantUmlRendererTests.Code_cache_is_created_on_first_run_reused_afterwards_and_regenerated_when_v8_rejects_it`
(line 165, "miss" expected, "hit" found; job 108703601424's log, excerpt in `results-s2-s3-rehearsal.txt` §9),
which passed in the local full run the same evening.
