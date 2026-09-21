# PlantUML Perf CI Plan

Plan for contributing a performance-tracking GitHub Actions workflow to plantuml/plantuml, so that
Teoz render performance cannot drift unnoticed in future versions. Written to be executable with no
session context. Companion to TEOZ_PERF_PLAN.md (which delivered the patches this workflow will
guard; see its Execution log for the harness, corpus and methodology this plan reuses).

## Goal and constraints (user requirements)

- A PR to plantuml/plantuml adding a **non-blocking** GitHub Actions workflow.
- Runnable by **manual dispatch against any commit or branch**.
- Writes a **GitHub step summary** with the results.
- Uploads a **detailed artifact with `retention-days: 1`** so it never accumulates storage.
- Two comparison modes (decided 2026-08-29 with the user):
  - **General default (used by automatic runs and bare manual dispatch): pinned npm reference.**
    The target build is benchmarked against a pinned published `@plantuml/core` version fetched
    from npm, rendered back-to-back in the same browser session. Fixed yardstick, catches slow
    cumulative drift, adds seconds not minutes.
  - **Manual dispatch option: two-build git-ref comparison.** A `compare` input accepting any git
    ref; that ref is built from source too and the two builds are compared head-to-head. This is
    the bisect/investigation mode ("which commit caused it", "my branch vs master").
- Why ratios, not absolute ms (argued and accepted): GitHub runners differ per run (host CPU SKU,
  co-tenants, fleet refreshes), which is a systematic per-run multiplier that within-run
  repetition cannot average away. Same-session ratio cancels it algebraically. Reps and pooled
  medians handle within-run jitter; the ratio handles between-run offset. Both are used.

## Open decisions (confirm with user before the PR goes out)

1. **Offer-first vs PR-first.** RESOLVED 2026-08-30 (user decision): PR opened directly as
   <https://github.com/plantuml/plantuml/pull/2840>, announced with a short comment on #2834
   (<https://github.com/plantuml/plantuml/issues/2834#issuecomment-5467859203>) that also lists
   the five patch PRs #2835-#2839. ALL rollout steps complete; now awaiting review on all six
   PRs plus the TeaVM issue. On review feedback: iterate on the fork branches (PRs update
   automatically on push).
2. **Automatic trigger.** RESOLVED 2026-08-30 (user decision): ship with `push` to `master`
   (docs-only pushes skipped via paths-ignore) plus `workflow_dispatch` (fork commit 81b320f).
   Rationale: dispatch-only drift detection never runs; removing a trigger is one obvious deleted
   block while adding one requires studying the yml; his own CI already runs on push to master.
   Say in the PR text that deleting the push block is the one-line opt-out.
3. **Directory name** for the harness in their repo. Placeholder: `perf-bench/`. Check their repo
   conventions when writing the PR and follow whatever fits (they have `test/`, `plantuml-mit/`
   etc.).

## Hard constraint discovered during planning: the npm pin must support `maxSvgSize`

Most of the message/note-heavy corpus renders taller than 4096 px, and engines without the
`maxSvgSize` render option (added on master after 1.2026.7, from issue #2832) TRUNCATE such output
("max 4096"), which is both wrong output and artificially fast, poisoning the ratio. Therefore:

- The npm reference pin MUST be **>= 1.2026.8** (first release expected to include `maxSvgSize`).
  As of 2026-08-29 npm latest is 1.2026.7, which is NOT usable as reference for the full corpus.
- If the PR needs to land before 1.2026.8 ships, either (a) default the reference to a git tag
  built from source (slower but correct), or (b) restrict the reference comparison to the corpus
  subset that stays under 4096 px (shape fixtures and small diagrams) and mark the tall rows
  "target-only ms" in the summary. Option (a) preferred; drop it the moment 1.2026.8 is on npm.
- The bench must pass `{maxSvgSize: 98304}` to every engine that accepts it and record in the
  artifact whether the option was honored (feature-detect: option accepted plus output not
  containing the truncation marker).

## House style of their CI (surveyed 2026-08-29, .github/workflows/)

Match these or the PR looks foreign:

- `actions/checkout@v7`, `actions/setup-java@v6` (temurin; gradle jobs use JDK 21),
  `gradle/actions/setup-gradle@v6.3.0` (sometimes sha-pinned), `actions/upload-artifact@v7`
  (NOT v4), `defaults: run: shell: bash`, `--no-daemon` on gradle, `paths-ignore` for
  `**.md`/`docs/**` on push/PR triggers.
- They already write to `$GITHUB_STEP_SUMMARY` (ci-other.yml), so the summary approach fits.
- NO existing workflow builds `:plantuml-mit:npmPackage` or uses Node/Playwright; this PR
  introduces both. Keep the Node footprint minimal: one `package.json` + `package-lock.json`
  inside `perf-bench/`, single dependency (`playwright`), exact-pinned.
- Add `permissions: contents: read` (their newer workflows are permission-conscious).
- Note for the PR text: `workflow_dispatch` upstream is limited to users with write access, so
  "benchmark any commit" does not mean strangers can burn their minutes.

## Deliverable 1: harness directory (`perf-bench/` in the plantuml repo)

Self-contained, no dependency on Kronikol. Adapted from `tools/render-bench/` here.

| file | content |
|---|---|
| `bench.js` | Node script. Serves engine dir(s) + fixtures over a local http server, launches headless Chromium via Playwright, renders every corpus diagram in interleaved blocks for N engines, records per-rep ms, `measureText` count, SVG byte size, SHA-256 of `svg.outerHTML`. Outputs `results.json` plus a markdown fragment for the step summary. Derived from Kronikol `tools/render-bench/bench-real.js` (rewrite cleanly: single file, argv engines list `name=path` pairs, `--reps`, `--blocks`, `--corpus` glob, `--out dir`). Interleaving: block 1 renders engines in order A,B; block 2 in order B,A; warm reps pooled per (engine, diagram); medians + IQR reported. Rep 0 of each (engine, diagram) discarded as cold. |
| `gen.js`, `gen-shapes.js` | Deterministic corpus generators (copied from Kronikol tools, paths adjusted). Regenerating fixtures must be byte-stable so the checked-in fixtures stay canonical. |
| `corpus/*.puml` | Checked-in fixtures: `gen-50`, `gen-200`, `gen-500`, the three `puml-*` report-shaped fixtures, the six `shape-*` fixtures, and `small/small-00..29` (30 x 10-arrow). Same set as the 2026-08-29 benchmarks in #2834, so CI numbers stay comparable with the issue's tables. |
| `expected-bands.json` | Per diagram: expected target/reference ratio and tolerance, e.g. `{ "gen-500": { "ratio": 1.0, "tol": 0.15 } }`. Summary marks rows outside band. Non-blocking always; bands are a reviewable file the maintainer bumps deliberately when a change legitimately moves them. Initial values: measured on the runner during rollout (step R2 below), not copied from local-machine numbers. |
| `reference.json` | The npm pin: `{ "npm": "@plantuml/core@1.2026.8" }` (version TBD per the constraint above). Single source of truth; the workflow reads it. |
| `README.md` | How to run locally (`node bench.js ...`), what the bands mean, how to bump the pin, methodology note (ratios, interleaving, why). |

Bench implementation notes carried over from the Kronikol harness (hard-won, do not rediscover):

- Serve engines over local http; load as ESM (`import {render}`); `render(lines, id, {maxSvgSize})`.
- Serve the WHOLE built `npm-plantuml` output directory (it contains `viz-global.js` etc.), not
  just `plantuml.js`. Include `<script src=".../viz-global.js">` before the module import.
- Completion detection: MutationObserver on the output div for `svg` or error text; 180 s timeout.
- Hash in-page via `crypto.subtle.digest('SHA-256', new TextEncoder().encode(svg.outerHTML))`.
- One Chromium page per engine-set run; all engines of a comparison share the session (that is the
  point); never compare numbers across sessions.
- Playwright on the runner: `npm ci` in `perf-bench/` + `npx playwright install --with-deps chromium`.
- **One page per engine, one browser instance per run.** Do NOT import two engine ESM modules into
  the same page (untested global/DOM interactions); ratio validity needs same host and interleaved
  timing, which pages within one browser launch provide. Blocks alternate between the pages.
- **Browser/Playwright pinning policy:** the Playwright version (and hence Chromium) is exact-pinned
  by the lockfile. Browser upgrades can shift different engine builds differently (historically the
  CPS-style and deep-stack TeaVM builds responded differently to V8 changes), so a Playwright bump
  is a deliberate commit and resets band history, like a corpus change.
- **Also track per run (summary rows + artifact):** engine file size in bytes and module
  import-to-ready ms for each engine. Size/load regressions matter to embedders (the 7.1 MB vs
  3.9 MB build change was a real event) and cost nothing to record. Optional artifact-only extra:
  JS heap used after each render via CDP (`Performance.getMetrics`), informational.

## Deliverable 2: workflow (`.github/workflows/perf-bench.yml`)

```yaml
name: perf-bench
on:
  workflow_dispatch:
    inputs:
      ref:      { description: "Commit/branch/tag to benchmark", default: "" }        # empty = the ref the workflow was dispatched on
      compare:  { description: "Comparison: empty = npm pin from perf-bench/reference.json, or npm:<version>, or any git ref (built from source)", default: "" }
      reps:     { description: "Reps per diagram per block", default: "6" }
      corpus:   { description: "Corpus glob filter", default: "*" }
  push:
    branches: [master]          # decision 2 above; drop or swap for schedule per maintainer preference
concurrency:
  group: perf-bench
  cancel-in-progress: false
```

Single job, `ubuntu-latest`, `timeout-minutes: 45`:

1. **Checkout target.** `actions/checkout` with `ref: ${{ inputs.ref || github.ref }}` into
   `target/`. (Note: on dispatch, the workflow *definition* comes from the branch selected in the
   UI; the `ref` input controls the benchmarked code. Document this in the README.)
2. **JDK + Gradle cache** (`actions/setup-java`, temurin, cache: gradle). Build target engine:
   `./gradlew :plantuml-mit:npmPackage -Pci --no-daemon` in `target/`. Engine dir:
   `target/plantuml-mit/build/npm-plantuml/`. (~2-5 min on runners; obfuscated default is fine,
   matches shipping.)
3. **Resolve comparison engine:**
   - `compare` empty -> read `perf-bench/reference.json` -> npm mode.
   - `compare` = `npm:<ver>` -> npm mode with that version: `npm pack @plantuml/core@<ver>` into
     `reference/`, untar; engine dir is the package root.
   - otherwise treat as git ref -> second `actions/checkout` with that ref into `reference-src/`,
     second gradle build (sequential; reuses the gradle cache, so cheaper than the first).
4. **Node + Playwright** (`actions/setup-node`, cache npm; `actions/cache` on
   `~/.cache/ms-playwright` keyed on playwright version).
5. **Run bench:** `node perf-bench/bench.js target=<dir> reference=<dir> --reps ... --out results/`.
   The harness copy used is ALWAYS the one from the workflow's checkout of the default/dispatched
   branch, NOT from the target ref, so old commits can be benchmarked with the current harness.
   (Practically: check out the workflow branch's `perf-bench/` separately, or use
   `actions/checkout` `path:` layout so the harness dir comes from the dispatching branch.)
6. **Step summary** (`>> $GITHUB_STEP_SUMMARY`): header naming target sha + comparison (npm version
   or ref sha); table per diagram: target median ms (IQR), reference median ms (IQR), ratio,
   band verdict (OK / OUTSIDE BAND +x%); SVG SHA-256 first 8 bytes for target and reference with a
   match/differ column (in npm mode "differ" is expected and labelled as such; in git-git mode a
   differ flags an output change, which is half the point of the bisect mode); footer with runner
   CPU model (`lscpu`), Node and Chromium versions, total wall time.
7. **Artifact** (`actions/upload-artifact@v7`, `retention-days: 1`): `results.json` (every rep, every
   engine, every diagram), `env.json`, copy of the summary markdown, full SVG hashes. Name:
   `perf-bench-<target-short-sha>`.

Non-blocking by construction: separate workflow, not a required check, nothing downstream depends
on it. No `continue-on-error` gymnastics needed.

Time budget sanity: build ~4 min + browsers ~1 min (cached: seconds) + bench (13 fixtures + 30
small, 2 engines, 6 reps x 2 blocks; renders are 0.02-3 s warm) ~8-12 min => ~15 min typical npm
mode, ~20 min git-git mode. Fine for push-to-master.

## Methodology rules encoded in the harness (same as TEOZ_PERF_PLAN.md, restated)

- Warm medians only (rep 0 per block discarded); IQR always shown next to medians.
- Engines interleaved across mirrored blocks within ONE browser session; never sequential sessions.
- Ratios are the headline; absolute ms informational (artifact + secondary columns).
- SHA-256 of `svg.outerHTML` recorded for every (engine, diagram); byte sizes too.
- The corpus is fixed and checked in; a corpus change invalidates band history, so corpus and
  `expected-bands.json` must change in the same commit.

## Rollout sequence

1. **R1 - build in our fork.** NOTE (verified 2026-08-29): `lemonlion/plantuml` does NOT exist yet;
   the #2834 work used a local clone and the JS-package fork is a different repo. First step:
   `gh repo fork plantuml/plantuml --clone=false`, enable Actions on the fork, then implement
   `perf-bench/` + workflow on a branch there. Ensure `git apply`-level independence from our five
   pending patches (harness must not assume they are merged).
2. **R2 - validate on Actions.** Run by dispatch on the fork repeatedly (5+ runs, different days):
   confirm ratio stability (expect ratio spread well under +-10% while absolute ms spread is much
   larger; that spread difference IS the validation of the design). Calibrate
   `expected-bands.json` tolerances from the observed ratio spread, not from local numbers.
   Test all three compare modes (default npm pin, `npm:<ver>`, git ref) and the corpus filter.
   Test dispatching against an OLD commit (pre-harness) to prove the harness-from-workflow-branch
   layout works.
3. **R3 - user review** of the PR text and the #2834 offer comment (house style: plain prose, no
   em-dashes, no LLM patterns; outward-facing, so user approves before anything is posted).
4. **R4 - offer/post per decision 1.** PR description: what it does, why ratios (2 sentences), cost
   per run, `retention-days: 1` (nothing accumulates; artifacts are free on public repos anyway),
   non-blocking, how to dispatch, how to bump pin/bands. Explicitly invite renaming/relocation.
5. **R5 - after merge:** dispatch once against master and against a known-older tag to seed the
   maintainer's intuition for the summary format. Then hands off; we keep using it from the outside
   (anyone can dispatch on their fork; only maintainers dispatch upstream).

## R1/R2 execution log (2026-08-29)

- Fork created: <https://github.com/lemonlion/plantuml>, branch `perf-bench` (based on upstream
  `4ce99a4`), commit `f65e2a7` adds `perf-bench/` + `.github/workflows/perf-bench.yml`. Default
  branch of the fork set to `perf-bench` so `workflow_dispatch` works (workflows must exist on the
  default branch to be dispatchable; harmless on the fork, irrelevant for the upstream PR).
- Harness implemented per plan: `bench.js` (multi-engine, one page per engine in one browser,
  mirrored blocks, pooled warm medians + IQR, small/ aggregated into `small-30` row, SHA-256 +
  truncation detection, engine size + import ms, bands verdicts, `BENCH_PW` env override for local
  runs against Kronikol's bundled Playwright), `generate.js` (byte-stable corpus: regenerated
  fixtures hash-match the Kronikol originals, verified via SVG SHA), `reference.json` (pin
  currently 1.2026.7 with truncation caveat documented; bump to 1.2026.8 when it ships),
  placeholder `expected-bands.json` (tol 0.3 pending runner calibration), README, package.json +
  lockfile (playwright 1.62.1 exact).
- Local validation: full corpus p12345-vs-stock run reproduced the known ratios (gen-* 0.43-0.52,
  puml-* 0.73-0.78, small-30 0.58, shapes at/below parity) with SHA `= ref` on every row.
- Cloud run 1 <https://github.com/lemonlion/plantuml/actions/runs/33265912688>: pipeline SUCCESS
  end to end (gradle, npm pack, playwright, summary, 1-day artifact). Two findings:
  (a) target SVG hashes on the AMD EPYC runner are IDENTICAL to the local i9 hashes, so output
  determinism holds across machines and the SHA column is trustworthy in CI; (b) npm 1.2026.7
  as reference REFUSES (throws "Diagram too large ... (max 4096)") every diagram over 4096 px,
  which is all 12 main fixtures; only small-30 produced a ratio (0.52, consistent with local).
  Sharper than the plan's truncation assumption: pre-maxSvgSize engines error out entirely.
- Fixes after run 1 (fork commit 2): `reference.json` now uses a `pin` field with `npm:<ver>` or
  `git:<ref>` formats; interim pin = `git:4ce99a4...` (fixed master sha, built from source via
  `git worktree` in the workflow) until 1.2026.8 is on npm; bench.js labels "Diagram too large"
  rows as `size-limited (no maxSvgSize)` instead of ERROR; README updated.
- Cloud run 2 <https://github.com/lemonlion/plantuml/actions/runs/33266341114>: git-pin reference
  worked (dual gradle build, full 13-row table, SHA `= ref` everywhere). CRITICAL FINDING: this
  was accidentally an A/A test (target branch = pin commit + harness only), so all ratios should
  be 1.00; measured 1.02-1.17, ALL above 1.00. Systematic bias: with engine-level mirrored blocks
  (T,R | R,T) the reference's legs are adjacent mid-session while the target's sit at the
  extremes, so convex session warm-up (JIT tiering beyond the discarded cold rep) penalizes the
  target. Fix (fork commit 3): alternate engines PER REP within each diagram (T,R / R,T by rep
  parity) so paired samples are temporally adjacent; linear and convex drift cancel. The same
  per-rep pairing should be back-ported to any future local interleaved-block measurements too.
- Cloud run 3 <https://github.com/lemonlion/plantuml/actions/runs/33266656061>: A/A with per-rep
  alternation = ratios 0.98-1.01 on all 13 rows (bias eliminated; was 1.02-1.17). Third distinct
  CPU model across the three runs (AMD EPYC 9V74, EPYC 7763, Intel Xeon 8370C), so host diversity
  is real and the ratio method is confirmed to cancel it. Noise floor from this run: about +-2pp.
- Runs 4-6 (33266899952, 33267180656, 33267379302): all success. CALIBRATION RESULT across 4 A/A
  runs on 4 distinct CPU models (Xeon 8370C, EPYC 9V74, Xeon 6973P-C, EPYC 7763): worst ratio
  deviation from 1.00 = 0.07 (gen-50, the fastest diagram), most rows within 0.05. Bands set to
  tol 0.10 everywhere (fork commit 68b4f63). R2 COMPLETE except the different-day re-check:
  dispatch one more A/A run on a later day before opening the PR (bands confirmed if still
  within 0.10). Branch state: 4 commits on `perf-bench`, PR-READY pending that re-check and the
  user's decision on offer-first vs PR-first (Open decisions, item 1).

## 2026-08-30 update

- The five patches were opened as individual PRs (user instruction, fulfilling the offer in
  comment-5463156700; verified first: no reply on #2834, master still `4ce99a4`): #2835 LiveBoxes
  O(1) lookup, #2836 CommunicationTile component cache, #2837 SVG formatting fast path, #2838
  getThickness cache, #2839 NoteTile/Self/Exo component cache. Branches on the fork:
  teoz-liveboxes-o1-lookup, teoz-communicationtile-component-cache, svg-formatting-fast-path,
  skinparam-getthickness-cache, teoz-tile-component-cache (each = 4ce99a4 + one cherry-pick).
- Different-day A/A recheck run 33302574445: SUCCESS, ratios 0.98-1.02 on all 13 rows, everything
  OK against the 0.10 bands. Noise floor stable across days (5 A/A runs total, 4+ CPU models).
  R2 is COMPLETE. The perf-bench branch is PR-READY; the ONLY remaining gate is the user's
  decision on Open decisions item 1 (offer in #2834 first vs open the PR directly) and item 2
  (automatic trigger choice; the branch currently ships dispatch-only, which is also a valid
  opening position that lets him add triggers himself).

## 2026-09-04 update: #2862 rebased over the merged tools/ move

- arnaudroques merged #2868 (tools/ move, cd67d0d7) before #2862, putting #2862 into CONFLICTING,
  and asked on the PR for the conflicts to be fixed (comment-5537769948). The-Lum had already had
  it flipped from draft to ready.
- Rebased `perf-bench-recenter-bands` onto master from a fresh blobless scratchpad clone (same
  procedure as the #2868 rebase; still no persistent local plantuml clone). The rebase linearised
  the branch to its two real commits; the bands-recenter commit applied clean (git followed the
  rename into `tools/perf-bench/expected-bands.json`), and the only conflict was
  `tools/browser-test/README.md` where master's new `tools/` paths overlapped the `--only-shell`
  install line — resolution is new paths + `--only-shell`. Verified the full diff vs master is
  exactly the 4 intended files (2 workflow lines + README line + recentered bands), then
  force-pushed (tip 87c4c2e22f).
- CI green post-push (browser-test `checks` job exercises the changed install line directly);
  PR now MERGEABLE / mergeStateStatus CLEAN, awaiting arnaud's merge.

## Risks and mitigations

- **Residual runner variance in ratios.** Mitigated by interleaving + pooled medians; R2 measures
  the actual ratio noise floor and bands are set above it. If a diagram proves too jumpy (small
  ones), widen its band or drop it from banding (keep it informational).
- **TeaVM build failures on old refs** in git-compare mode (e.g. pre-TeaVM-migration commits).
  Harness reports "comparison build failed" in the summary and still benchmarks the target alone.
- **npm pin lag** (the maxSvgSize constraint above). Gate: do not open the PR with an npm default
  that truncates the corpus; use a git-tag default until 1.2026.8 is on npm.
- **Maintainer declines CI changes.** Fallback: run the identical workflow in the
  `lemonlion/plantuml` fork on a cron against upstream master (`git fetch upstream`), which needs
  nobody's approval and still catches drift; offer upstream again later with accumulated results.
- **Harness rot vs upstream refactors** (npm package layout, render signature). The workflow runs
  the harness from the current branch (not the target ref) precisely so one fix commit repairs all
  future runs.

## Deliberately out of scope (state in the PR so it reads as restraint, not omission)

- **No results history.** Artifacts die in 1 day and summaries live only in run logs. A trend chart
  needs persistent storage (a data branch or gh-pages JSON the workflow appends to); that is a
  follow-up the maintainer can opt into later, not something to impose in the first PR. The
  band file is the stand-in: drift shows up as out-of-band rows, and the pin-bump commits document
  each accepted level over time.
- **No PR triggers, no gating.** Ever, unless he asks.

## Relationship to Kronikol (out of scope for the PR, tracked here)

Kronikol's own CI perf budget (planned at the npm 1.2026.8 upgrade, see TEOZ_PERF_PLAN.md
"Kronikol-side notes") stays separate: it pins npm releases and guards what Kronikol ships; the
upstream workflow guards plantuml's trunk. Do not merge the two concerns.
