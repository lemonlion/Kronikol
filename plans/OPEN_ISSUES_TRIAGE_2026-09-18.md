# Open issues triage — 2026-09-18

A reading of the 16 issues open on `lemonlion/Kronikol` on 2026-09-18, grouped into the plans they
should be worked as. It is a grouping, not a plan: each row below still needs its own plan file.

**Basis.** Every issue body and comment was read. Nothing here was checked against the source, so
claims the issues make about the code (`IngestPipeline` ordering, `RunSpeeds`, `ReportIndex`
seeking) are the issues' claims, not verified facts.

## The grouping

16 issues become 7 plans, one standalone and one parked.

| # | Plan | Issues | Why they belong together |
|---|---|---|---|
| 1 | History verdict noise | #75, #83 | Same analyzer, same run-speed calculation |
| 2 | Evidence survives a re-run | #80, #81, #82, #84 | One debugging session, one verb, one shared way of addressing a run |
| 3 | Internal-flow span mixing | #87 | Correctness bug; should land before plan 4 |
| 4 | Internal-flow payload size | #89, then #86 | Same payload; the issues state the order themselves |
| 5 | Compress `TestRunReport.json` | #85 | Needs a maintainer decision and a schema change |
| 6 | PR delta selection | #77, #78, and what is left of #76 | Have to be designed together — see the gap below |
| 7 | PR comment template | #72 | Self-contained; can ship first |
| — | Standalone | #90 | Code coverage panel |
| — | Parked | #79 | Its author argues against building it yet |

## Plan 1 — History verdict noise (#75, #83)

**Planned in full:** `HISTORY_VERDICT_NOISE_PLAN.md` (2026-09-18). It refines the slicing below into
five slices and corrects two of the issues' proposed fixes.

#83 says it depends on the same run-speed notion as #75 §3 and "may well be one change".

**Order matters.** Fix the partial-run baseline (#75 §3) first. Otherwise #83's
`run degraded: 5.6× p95` label is built on a number #75 shows to be wrong.

#75 is really three work items and should be three PRs:

1. **The templater (§1)** — GUIDs with `_` separators, short hex digests, binary segments; optionally
   the consumer hook (`HistoryShapeTemplates`), claims-aware templating and the "only ids differ" verdict.
2. **Merge before drop at ingest (§2)** — in `IngestPipeline`, not the analyzer. It changes report
   content as well as history, so it is the slice most worth reviewing on its own.
3. **Partial runs in the analyzer (§3, §4, §5)** — exclude them from the duration baseline and from
   the alternating memory; drop the fold scenario from `absent`. #83 is added last, on top of this.

The issue claims 1a, 1b, 2a, 3a and 4a remove about 95% of the noise on its suite.

## Plan 2 — Evidence survives a re-run (#80, #81, #82, #84)

Siblings that cite each other, all from one session: a failure, an instinctive re-run, and the
evidence gone.

- **#81, #82, #84 are small and touch only `kronikol query history`:**
  - #81 — query the ledger without a report (`--sid`, `--run`), and exit 2 on a usage error (it exits 0 today).
  - #82 — stop truncating the error message in the single-scenario view, or say where the rest is.
  - #84 — when a filter matches nothing but the window holds qualifying scenarios, say so and give
    the `next:` command; name the `--min-runs` bar when that is what excluded a scenario.
  - These three can ship as a patch.
- **#80 is the larger change,** on the report-writing side: keep the last N runs
  (`Reports/runs/<runId>/…`), or at least never overwrite a failing report silently. It belongs in
  this plan because its `--run <id>` has to be the same address as #81's `--run`.

**Conflict with plan 1.** Both plans edit the history text renderer (#75 §3c and §4b, #83, #82, #84).
Run them one after the other, not in parallel.

## Plan 3 — Internal-flow span mixing (#87)

Popups pool the spans of many concurrent requests (one shows 547 spans and 60 root requests), which
manufactured an N+1 finding that did not exist. Likely cause per the issue: grouping by time window
instead of by trace/activity id.

Same code area as plan 4 but a different problem — do not fold it in. **Do it first:** #86's dedup
figures were measured on reports that contain 63 multi-request segments, and correct grouping
changes what a segment holds. #86 itself asks for its numbers to be confirmed before committing.

## Plan 4 — Internal-flow payload size (#89, then #86)

- **#89** — gzip + base64 `__iflowSegments` as one blob, decompressed lazily on the first popup.
  Takes the ClickHouse-lane report from 31.8 MB to about 13.1 MB. Cheap, no change to keying or
  rendering. Land it early regardless of plan 3.
- **#86** — store each distinct flow once and reference it by key. Adds only 10–25% on file size
  after #89; its real case is heap and parse time, and the growth curve (size scales with roughly
  the square of spans per request). Re-measure after plan 3 before committing.

Do not apply #89's one-blob reasoning to `puml-data`: that block must stay compressed per diagram
(#88), because something reads it by key.

## Plan 5 — Compress `TestRunReport.json` (#85)

Same theme as plan 4 (gzip + base64) but a different file and a different reader (`ReportIndex`
seeks by byte offset). Separate plan because it is blocked on a decision:

- **Option 1** — compress payload fields in place (`{"$z": …}`); the file stays valid JSON. 82.7 MB → 13.9 MB.
- **Option 2** — also gzip the whole file. → 6.1 MB, but `jq`, editors and schema validators cannot read it as shipped.

Either way: schema change (`$defs/compressed`, a root `payloadEncoding`), moves with `formatVersion`,
ships behind an option (changing the default is MAJOR, reserved for v4).

Independent of the decision: README line 177 says the file is "10 MB" on a real suite; the measured
one is 82.7 MB, and the token figure after it inherits the error. That can be fixed now.

## Plan 6 — PR delta selection (#77, #78, remainder of #76)

**#76 is mostly invalid,** by its author's own correction: `sourceFile` and `sourceLine` are already
on the scenario and populated (379 of 379). What remains:

1. `kronikol query scenarios --json` does not project them.
2. Step-level locations are still empty on a Gherkin suite (0 of 2,297 top-level steps).

Retitle it to the CLI projection or close it, as the author suggests.

**#77** — record the runner's test-case identity per scenario (`TestCaseId`, `TestCaseIdKind`), and a
`kronikol filter` verb that emits the right runner grammar. Touches every adapter package.

**#78** — `kronikol changed`: which scenarios a diff added, edited or removed, read from git refs,
comparing each scenario's text at the merge base with its text now. Comes with an eleven-check
conformance suite and a 30-PR replay.

### The gap neither issue states

`kronikol changed` works from git refs with no build and no report. `kronikol filter --from
TestRunReport.json` needs a report that already holds each scenario's `TestCaseId`. **A newly added
scenario has no earlier report** — and in #78's own example, 21 of the 22 changed scenarios were new.

So "composes straight into a runnable filter" fails for the main use case unless the key joining the
two is designed up front. One possibility: filter by source location or a runner trait rather than a
recorded id. This is the reason the two must be one plan.

## Plan 7 — PR comment template (#72)

A composite action under `templates/github-actions/` that keeps one PR comment with one line per
report artifact. Only loosely tied to #78 (one extra line in the comment), so it ships independently
and can go first.

## Standalone — #90 code coverage panel

Run-level coverage from any cobertura file, derived to a run-length-encoded form (16 KB for full
per-line detail). Connected to two other issues, neither strongly enough to merge:

- **#79** — per-scenario coverage is the footprint #79 wants; #90 explains why coverlet cannot provide it.
- **#89** — reuses the convention of decompressing a block only when first opened.

Its real design constraint: coverage does not exist at `[AfterTestRun]` time, so it needs an MSBuild
target or a CLI step that merges into an already-written report. Must render "not collected" rather
than 0%, and must respect `HistoryPartialRun`.

## Parked — #79 impact analysis beyond the diff

A discussion issue. Its author measured a static step-definition matcher, recommends *not* shipping
it, and recommends shipping #78 first. The observed-impact version it sketches is a later, separate
piece of work. Minimum to carry into plan 6: the delta report's wording should distinguish "no
scenario text changed" from "nothing in this PR affects the suite".

The only comment on it (from `kevin-lozada-santos`) is a promotion for an external tool and adds
nothing to the issue; it could be hidden as spam.

## Suggested order

1. #72 (plan 7) and the #81/#82/#84 patch from plan 2 — small, independent.
2. #89 from plan 4 — cheap, large win.
3. Plan 1, then the rest of plan 2 (they share the history renderer).
4. Plan 3, then re-measure and decide #86.
5. Plan 6, once the join key is designed.
6. Plan 5, once option 1 or 2 is chosen.
7. #90.
