# Teoz Renderer Performance Plan

## Purpose and deliverable

PlantUML 1.2026.7 removed the legacy Puma sequence-diagram engine and made Teoz the only engine. Teoz is
slower. This plan's goal is to get Teoz to parity with (or better than) Puma in the browser JS build.

We do not control the plantuml codebase. The deliverable of every workstream is therefore a **package for the
plantuml maintainer** (arnaudroques), posted to
[plantuml/plantuml#2834](https://github.com/plantuml/plantuml/issues/2834), containing for each finding:

1. a clear explanation of the bottleneck (what, where, why it is slow, profiler evidence with caller chains);
2. a recreatable patch or proof of concept against a named upstream commit (unified diff, applies with
   `git apply`, in a fenced ```diff block inside a `<details>` section);
3. proof the patch does not change behaviour: SVG output SHA-256-identical across the whole test corpus
   (or an explicit note where output legitimately changes);
4. detailed before/after benchmarks (warm ms per corpus diagram, both engines, same machine, same session)
   so the win is quantified and reproducible.

The bar: the maintainer should be able to implement each change from our post alone, without re-deriving
anything. That standard is what worked twice already in #2834 (both earlier report packages were acted on
within a day).

## POSTED 2026-08-29 (gate lifted by user)

The user explicitly instructed posting without waiting for the maintainer's reply. Verified first:
master still at `4ce99a4` (our measurement base) and no new comments on #2834, so all numbers and
diffs were current. Posted:

- Consolidated reply (5 patches + benchmarks + Kronikol context):
  <https://github.com/plantuml/plantuml/issues/2834#issuecomment-5463156700>
  (exact body: `tools/render-bench/reply2-final.md`)
- TeaVM issue: <https://github.com/konsoletyper/teavm/issues/1247>
  (exact body: `scratchpad/teavm-issue-final.md`, mirrored from `teavm-issue-draft.md`)
- Gist updated with gen-shapes.js, the 6 shape fixtures and small-sample.puml.

NOW AWAITING replies on both threads. The section below is retained for history; its retest
checklist still applies to any FUTURE post in #2834 (re-verify against his then-HEAD first).

## Posting protocol (superseded for this round, kept for future posts)

**Nothing further is posted to #2834 until the maintainer replies to our most recent comment**
(<https://github.com/plantuml/plantuml/issues/2834#issuecomment-5450539259>, posted 2026-08-28, containing
the retest tables, the profiling diagnosis, and the LiveBoxes patch).

When he replies:

1. Note which commit/version he is now on (he may have merged the LiveBoxes patch, changed teoz, or cut
   1.2026.8).
2. Re-run the full retest matrix (Section "Retest checklist" below) against that version.
3. Rebase every pending patch onto it; re-verify SVG-SHA identity and re-measure every number we intend to
   quote. Stale numbers or patches that no longer apply must not be posted.
4. Reply with one consolidated package: everything he needs to bring Teoz in line with Puma
   performance-wise, aligned to his current version.

Work (profiling, patch development, corpus building) proceeds meanwhile; only the posting waits.

Post style (user requirement, non-negotiable): plain engineering prose, **no em-dashes**, no emoji, no
hype/LLM-style writing. Tables and fenced code blocks are fine. Large source attachments go in a gist
(comments cannot take zip attachments via CLI): existing gist with the corpus sources is
<https://gist.github.com/lemonlion/8a19fa9c6e7e53c139090b4bbb813a76> (update or extend it when the corpus
grows). GitHub posting is via `gh` (authenticated as `lemonlion`, the same account that filed the issue).

## Background and timeline (everything a cold start needs)

- Kronikol renders PlantUML sequence diagrams in the browser via a TeaVM-compiled engine. Kronikol has
  ALWAYS used Teoz: every diagram prefix contains `!pragma teoz true`
  (`src/Kronikol/PlantUml/PlantUmlCreator.cs`, `CreatePlantUmlPrefix`, ~line 538). Any Puma-vs-Teoz
  comparison on 1.2026.6 is controlled by that pragma; from 1.2026.7 Teoz is the only engine
  (commit `7d1d71800d` "migrate default sequence diagram engine from Puma to Teoz" removed the escape hatch).
- Kronikol currently pins fork tag `lemonlion/plantuml-js-plantuml_limit_size_98304@v1.2026.6-patched`
  (= npm `@plantuml/core` 1.2026.6 with the hardcoded `4096.0` SVG size limit raised to `98304.0`, 2 patch
  points + 1 message string). Upstream master has a `maxSvgSize` render option (from issue #2832) which
  retires the fork; decision on record: upgrade Kronikol when npm 1.2026.8 ships, not before
  (1.2026.7 is perf-neutral for Teoz users and lacks `maxSvgSize`).
- Issue #2834 timeline:
  - 2026-08-27: we filed it (npm 1.2026.7 renders 3-5x slower than 1.2026.6; bisected; included a
    `net.sourceforge.plantuml.real` stack-capture patch).
  - Maintainer merged fix `6535ebfac` (removed the real-package stack captures; added a cache for
    text-DESCENT measurements in the JS StringBounder) and asked us to retest on master with real diagrams.
  - 2026-08-28: we posted the retest (master `81eb091` roughly halves 1.2026.7; gap vs 1.2026.6 default
    down to 1.6-1.9x) plus a profiling diagnosis and a verified LiveBoxes patch. **Awaiting reply.**
- Full history, numbers, and gotchas are also in auto-memory `plantuml-js-fork-perf.md`; this file is
  self-sufficient without it.

## Repository artifacts (all under `tools/render-bench/` unless noted)

| file | what it is |
|---|---|
| `bench-real.js` | renders arbitrary `.puml` files in headless Chromium, per-rep ms + `measureText` count; env: `PRAGMA=1` injects `!pragma teoz true`, `MAXSVG=<n>` passes `{maxSvgSize:n}` render option (master+ only), `REPS=<n>` (default 2; rep 0 = cold), `SVGHASH=1` prints first 8 bytes of SHA-256 of `svg.outerHTML` (the output-identity proof) |
| `profile-real.js` | same rendering, wrapped in a CDP CPU profile (100 us sampling); prints self-time ranking; `FOCUS=<functionName>` prints top caller chains for that function; needs an UNOBFUSCATED build for readable names |
| `bench-browser.js` | the original synthetic-only harness used for the tables in the issue itself (`node bench-browser.js new <engine> [sizes]`); keep using it when numbers must be like-for-like with the issue's original tables |
| `gen.js` | synthetic sequence-diagram generator (N arrows, 4 participants, periodic notes/audit calls) |
| `real/gen-50.puml`, `real/gen-200.puml`, `real/gen-500.puml` | generated synthetic sources (no pragma) |
| `real/puml-0.puml`, `real/puml-1.puml`, `real/puml-19.puml` | report-shaped fixtures matching Kronikol production output (autonumber, JSON/SQL note payloads); synthetic content, shareable |
| `livebox-patch.diff` | the posted LiveBoxes patch, against master `81eb091` (see Findings) |
| `reply-draft.md` | exact text of the posted 2026-08-28 comment |
| `kronikol-bench-diagrams.zip` | the corpus as shared in the gist |
| `core-1.2026.6-patched.js` / `-unpatched.js` | npm 1.2026.6 engine (patched = 98304 limit) |
| `core-1.2026.7-patched.js` / `-unpatched.js` | npm 1.2026.7 engine |
| `core-master-fix.js` | stock master `81eb091` (obfuscated) |
| `core-master-livebox2.js` | master + LiveBoxes patch (obfuscated) = the benchmark build |
| `core-master-livebox2-noobf.js` | same, unobfuscated = the profiling build |
| `core-master-livebox2-noobf-enumcache.js` | above + JS-level `getEnumConstants` memo (the TeaVM PoC) |
| `core-1.2026.6-noobf.js` | unobfuscated 1.2026.6 for Puma profiling |
| `results/` | JSON results from the pre-worker-era experiments (historical) |

Harness prerequisite: the Playwright driver bundled with the .NET E2E tests must exist at
`tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package` (build that project once:
`dotnet build tests/Kronikol.Tests.EndToEnd`).

## Building engines from plantuml source (recipe)

```
git clone --depth 1 [--branch <tag>] https://github.com/plantuml/plantuml.git
cd plantuml
./gradlew.bat :plantuml-mit:npmPackage -Pci --no-daemon
# output: plantuml-mit/build/npm-plantuml/plantuml.js  (~2 min on this machine, JDK 25 works)
```

- Obfuscation toggle: `plantuml-mit/build.gradle.kts`, `obfuscated.set(true|false)` (~line 39). Build
  obfuscated for benchmark numbers (matches shipping), unobfuscated for profiling.
- The `-Pci` flag is required (the plantuml-mit subproject only activates with it).
- Fetching an arbitrary commit into a shallow clone works: `git fetch --depth 1 origin <sha> && git checkout -qf <sha>`.
- The TeaVM getEnumConstants JS memo PoC is made by post-processing an unobfuscated build: find the single
  occurrence of `jl_Class_getEnumConstants = $this => {`, wrap the arrow body so the result is cached on the
  class object (`if ($this.$kronEnumCache) return $this.$kronEnumCache; return $this.$kronEnumCache = (...)`).
  Brace-match the body programmatically; do not regex the whole function.

## Measurement methodology (follow exactly, or numbers are not comparable)

- Warm number = later reps in the same page (rep 0 is cold and includes first-render JIT; report it
  separately). Use `REPS=3`, quote the median of reps 1-2.
- Absolute ms drift day-to-day and under load. **Never mix numbers from different sessions in one table**:
  re-measure baseline and candidate back-to-back, same machine, same session.
- `profile-real.js` ms are inflated by sampling overhead; use its output only for the distribution
  (percentages, ranking, caller chains). Headline ms always come from `bench-real.js`/`bench-browser.js`.
- Output identity: `SVGHASH=1` on the full corpus for baseline and candidate; every hash must match. This is
  the proof quoted in posts ("SVG output byte-identical, SHA-256 over the rendered svg element, all N corpus
  diagrams").
- Engines that predate `maxSvgSize` need the patched (`98304.0`) builds so renders complete end to end;
  master takes `MAXSVG=98304`. Keep the limit consistent across compared builds.
- Do not confuse the two baselines: "Puma" = 1.2026.6 WITHOUT pragma; "Teoz 1.2026.6" = WITH `PRAGMA=1`.
  Comparisons posted to the issue historically used the no-pragma default; Kronikol-relevant comparisons use
  pragma everywhere. Label tables accordingly and never mix.

## Current state (measured 2026-08-28, warm ms, this machine)

| build | gen-200 | gen-500 | puml-0 | puml-1 | puml-19 |
|---|---|---|---|---|---|
| Puma (1.2026.6, no pragma) | 597 | 1154 | 190 | 97 | 199 |
| Teoz 1.2026.6 (pragma) | 1828 | 4993 | 443 | 298 | 644 |
| npm 1.2026.7 | 1612 | 4649 | 526* | 313* | 643* |
| master 81eb091 stock | 896 | 2996 | ~250 | ~170 | ~332 |
| master + LiveBoxes patch | ~750 | 2002 | ~236 | 158 | ~313 |
| + getEnumConstants memo (unobf pair: 1895 -> 1587) | — | ~1600 eq. | — | — | ~260 eq. |

*no-pragma runs; pragma variants within noise of these. "eq." = ratio from the unobfuscated A/B applied to
the obfuscated number; re-measure properly when a real TeaVM-side build exists.

Gap to close after the two known fixes: roughly 1.35-1.4x on gen-500, ~1.3x on report fixtures.

## Execution log 2026-08-29 (IMPORTANT: read before resuming)

- Maintainer has NOT replied in #2834, but he ACTED on our post: master (now versioned 1.2026.8beta1)
  gained `d26b07c` (our LiveBoxes patch, merged near-verbatim, credited to lemonlion), `c93fc79`
  (SkinParam.getValue key cache) and `4ce99a4` (ValueImpl result caching), the last two crediting #2834.
  New working baseline = his HEAD `4ce99a4`. The posting gate still holds (no reply yet).
- W0 DONE: `gen-shapes.js` generated `real/shape-*.puml` (groups, parallel, wide, activation, self,
  createdestroy) + `real/many-small/small-NN.puml` x30; `profile-real.js` gained inclusive-time ranking
  (`TOPTOTAL` env); `alloc-real.js` added (CDP HeapProfiler, FOCUS chains). 3-engine matrix over the
  corpus in `results/matrix-2026-08-29.txt` (CAUTION: absolute ms inflated, a gradle build overlapped
  the first two legs; per-shape ratios still valid). Verdict: HEAD is at Puma parity on ALL new shapes;
  the remaining ~1.35-1.5x gap is confined to message+note-heavy diagrams (gen-*, puml-* = the
  Kronikol shape).
- Stock-HEAD profile (`results/profile-head-stock.txt`, gen-500+puml-19): getEnumConstants 10.0% self
  (EnumSet-hash cluster ~16%), formatting cluster ~6% (String.format 9.8% INCLUSIVE), getThickness 2.2%,
  allocation ~8%. Key inclusive finds: `CommunicationTile.getComponent` 35%, `Rose.createComponentArrow`
  21.8% (component rebuilt from ~6 phases per tile).
- Patches developed as local commits in the scratchpad clone (rebased tips; regenerate diffs with
  `git diff 4ce99a4..<sha>`): P1 `6d53384` LiveBoxes isNextEventADestroy/getActivateColor O(1) via the
  merged eventInfo map (they were O(P*E^2) via getStairs); P2 `395dd4c` CommunicationTile memoizes its
  ArrowComponent per orientation (isReverse reads live solver values, hence the 2-slot cache);
  P3 `8217391` XColor.toSvg hex table + SvgGraphicsTeaVM/DriverPathTeaVM `%.2f` exact fast path
  (validated vs String.format on 40M values, 0 mismatches, `scratchpad/FormatCheck.java`);
  P4 `81d801c` SkinParam.getThickness cache (same pattern as his getValue cache); P5 `ca41e8e`
  NoteTile/CommunicationTileSelf/CommunicationExoTile component memo (their getComponent ignores the
  stringBounder argument entirely).
- Engines: `core-head4ce99a4.js` (stock obf), `core-head4ce99a4-noobf.js`, `core-head-p1/p12/p123.js`
  (+p1234/p12345/p12345-noobf when builds finish). SHA gate over 15 diagrams (12 corpus + 3 many-small):
  stock hashes in `results/hashes-stock.txt`; p1, p12, p123 all IDENTICAL. p1234/p12345 pending.
- TeaVM issue drafted at `tools/render-bench/teavm-issue-draft.md` (verified identical code on TeaVM
  master; no existing issue). NOT posted; needs user approval.
- RESULTS (all done 2026-08-29): ALL FIVE stages SVG-SHA-IDENTICAL over the 15-diagram gate. Clean
  8-engine matrix in `results/matrix-clean-2026-08-29.txt`: on the gap shapes the full stack BEATS
  Puma (gen-500 warm: Puma 4636 / stock HEAD 5671 / p12345 2372 = 0.51x Puma; gen-200 0.26x;
  puml-19 0.55x; puml-0/1 ~0.8x); shape fixtures at parity within noise (sub-400ms cells noisy,
  re-measure with REPS=5 for quoted tables). Per-stage on gen-500: P2 is the big one (-2.1s), then
  P3 -0.5s, P5 -0.4s, P4 -0.2s, P1 neutral on this corpus (keep: complexity fix, zero risk).
  Many-small (30x10 arrows): totals 1460 -> 647 ms warm, median 49 -> 19 ms (the Kronikol
  interactive path). Post-patch profile `results/profile-head-p12345.txt`: FLAT, nothing >5.4%;
  getEnumConstants 10.0% -> 2.2% (P2/P5 killed signature churn at the source, so NO plantuml-side
  EnumSet patch needed; TeaVM issue still worth filing for the remainder). Alloc profile
  (alloc-real.js): diffuse string/object churn, no actionable single site; W3 closed as no-action.
  Remaining clusters: regex/parse ~7%, allocation ~8% incl GC, DoubleAnalyzer residue ~3% (from
  some non-format() double stringification, minor). DIMINISHING RETURNS REACHED; goal (<= Puma on
  all shapes) achieved on this corpus.
- Deliverables staged: per-patch diffs in `tools/render-bench/patches/` (p1..p5 + all-p1-p5, vs
  named base 4ce99a4; regenerate against his HEAD at retest); consolidated reply skeleton
  `tools/render-bench/reply2-draft.md` (has post-time checklist); TeaVM issue draft
  `tools/render-bench/teavm-issue-draft.md` (awaiting user approval to post).
- W5.3 done: `OptimizationLevel.AGGRESSIVE` does NOT build on this codebase, TeaVM 0.14.1 codegen
  crashes with `AssertionError: Variable used before definition` in generateJavaScript. BALANCED
  stays. (Second potential TeaVM report, low priority.)
- High-rep shape re-run (`results/shapes-highrep-2026-08-29.txt`): the tier-3 "behind Puma" cells
  were noise. Method: REPS=12, engines interleaved PUMA/STOCK/PATCHED then PATCHED/STOCK/PUMA to
  cancel drift, warm reps pooled (n=22/cell), median + IQR. Result vs Puma: self 0.60x, groups
  0.70x, activation 0.77x, parallel 0.79x, wide 0.96x (tied), createdestroy 1.05x (tied, IQRs
  overlap). Also patched-vs-stock wins hidden before: activation 606->335, self 183->106.
  FINAL CLAIM for the post: patched beats Puma on 10/12 shapes (0.23-0.84x), ties on 2, behind on
  none. USE THIS interleaved-block + pooled-median methodology for every table quoted at retest.

## Findings so far (status ledger)

| finding | status | artifact |
|---|---|---|
| `real` package stack captures (RealMax per-iteration Throwable guard, creationPoint in 3 classes) | FIXED upstream (`6535ebfac`) | described in issue OP |
| Uncached text-descent measurements in JS StringBounder | FIXED upstream (`6535ebfac`) | maintainer's own find |
| `LiveBoxes.getLevelAtInternal` quadratic (prefix rescan per call + message lookahead walking all following messages); fixed with prefix-level cache (IdentityHashMap; Event classes define no equals, verified) + next-LifeEvent skip chain preserving exact visit order | POSTED 2026-08-28, awaiting reply | `livebox-patch.diff` vs `81eb091`; gen-500 3.0 s -> 2.0 s; SHA-identical x6 |
| TeaVM `TClass.getEnumConstants()` rebuilds the array every call; `TGenericEnumSet.iterator().next()` calls it PER ELEMENT; hot via `StyleSignatureBasic`/`StyleKey` `EnumSet<SName>` hashing (10.5% self) | POSTED as analysis + PoC number (~15% more, SHA-identical); proper fix belongs in TeaVM | memo recipe above; TeaVM sources confirm no caching (checked teavm-classlib 0.14.1 sources jar, `TClass.java`, `TGenericEnumSet.java`) |
| Style value re-parsing (`ValueImpl`, `SkinParam.getValue`) | FIXED upstream (`c93fc79`, `4ce99a4`, crediting #2834) | maintainer's own follow-up to our diagnosis |
| `SkinParam.getThickness` still uncached (2.2% self) | PATCH READY (P4 `81d801c`), SHA-identical x15 | `patches/p4-getthickness-cache.diff` |
| `CommunicationTile.getComponent` rebuilds ArrowComponent from ~6 phases per tile (`createComponentArrow` 21.8% inclusive) | PATCH READY (P2 `395dd4c`), biggest single win (-2.1s on gen-500), SHA-identical x15 | `patches/p2-communicationtile-memo.diff` |
| Same rebuild pattern in NoteTile/CommunicationTileSelf/CommunicationExoTile (their getComponent ignores stringBounder) | PATCH READY (P5 `ca41e8e`), drives note-heavy + many-small wins, SHA-identical x15 | `patches/p5-sibling-tile-memo.diff` |
| SVG number formatting churn (String.format %.2f per coordinate, hex colors per attribute) | PATCH READY (P3 `8217391`); fast path validated vs String.format on 40M values 0 mismatches; SHA-identical x15 | `patches/p3-svg-formatting.diff`, `scratchpad/FormatCheck.java` |
| Remaining O(E)-per-call scans in `LiveBoxes` (`isNextEventADestroy`, `getActivateColor` via `getStairs` = O(P*E^2)) | PATCH READY (P1 `6d53384`), perf-neutral on corpus but closes complexity class, SHA-identical x15 | `patches/p1-liveboxes-o1-lookup.diff` |
| Allocation churn | CLOSED no-action: alloc profile shows diffuse string/object churn only | alloc-real.js run 2026-08-29 |
| TeaVM getEnumConstants | mostly mooted plantuml-side by P2/P5 (10.0% -> 2.2%); TeaVM issue drafted for the remainder | `teavm-issue-draft.md` |

## Workstream 0: corpus and instrumentation (do first; gates everything)

The corpus is message-heavy and shallow; Teoz costs shift with diagram shape. Parity must be proven wider,
and the maintainer package must show shape coverage.

1. Add generators (extend `gen.js` or add siblings) + fixture files for: nested groups (alt/opt/loop 5+
   deep), parallel (`&`) messages, wide (20+ participants), activation-heavy (activate/deactivate per
   message), self-messages, create/destroy participants, and a many-small set (30 diagrams x 10 arrows;
   Kronikol reports are dominated by per-diagram fixed cost).
2. Extend `profile-real.js`: total-time (inclusive) aggregation next to self-time; optional speedscope JSON
   export.
3. Allocation profiling variant: CDP `HeapProfiler.startSampling` around renders, rank allocation sites.
4. Real-solver convergence probe: local build with an iteration counter in `RealLine`/`PositiveForce`
   (diagnostic only, never posted as a patch) to know solver behaviour per shape.
5. Produce the per-shape Puma-vs-Teoz ratio table on the patched build. This decides which workstreams
   matter and becomes the coverage table in the eventual post.

## Workstream 1: style system caching (~10% target, plantuml-side)

1. `FOCUS=nsps_SkinParam_getValue` and `FOCUS=nsps_ValueImpl_asInt` caller-chain profiles to find the
   per-element call sites.
2. Candidates in order of invasiveness: per-instance memo of parsed numerics in `ValueImpl` (immutable);
   per-key result cache in `SkinParam` (per-diagram lifetime, safe); intern `StyleSignatureBasic` or hash a
   precomputed SName bitmask to kill EnumSet hash churn (note: `StyleKey.hashCode` and
   `StyleSignatureBasic.hashCode` already memoize per instance; the churn is NEW instances, so interning is
   the lever there).
3. Each candidate: build, SHA-verify corpus, bench, keep if >= 2%. Package survivors as separate small diffs.

## Workstream 2: SVG number formatting (~6% target)

1. FOCUS-profile `jt_DecimalFormatSymbols_clone` and `otcit_DoubleAnalyzer_analyze` to find call sites
   (expected: `SvgGraphicsTeaVM.format` / coordinate stringification via `String.format`).
2. Candidates: a static reusable formatter or hand-rolled fixed-precision double-to-string in the TeaVM SVG
   backend (coordinates need "up to N decimals, trailing zeros trimmed" only); if the churn is inside
   TeaVM's `TDecimalFormat`, it becomes a second TeaVM report instead.
3. Formatting is output-visible: the fix must reproduce the exact same strings; SHA identity is the gate.

## Workstream 3: allocation churn (measure before acting)

Run the W0 allocation profiler; expected suspects from code reading: `EnumSet.clone` in `StyleKey.add*`,
`RealDelta` chains, per-tile list growth, `Map<Double, Double>` boxing in `LiveBoxes.delays`/`eventsStep`.
Only act where reuse is semantically safe; SHA-verify each.

## Workstream 4: finish the O(E) family in LiveBoxes

`isNextEventADestroy` and `getActivateColor` still scan from the list head per call; `getStairs` iterates
all events per participant. The `eventIndex` map from the posted patch makes them O(1)-lookup + short local
walk. Do this if and only if W0's activation-heavy/many-small profiles show them; fold into the same patch
family as the posted LiveBoxes change.

## Workstream 5: TeaVM upstream

1. File the `getEnumConstants` issue at github.com/konsoletyper/teavm (TeaVM version 0.14.1): no caching in
   `TClass.getEnumConstants()`, called per element by `TGenericEnumSet`'s iterator; quote the 10.5% self
   time and the ~15% render win from the memo PoC; suggest caching the shared array and cloning only in the
   public API, like the JDK. (Check konsoletyper/teavm for an existing issue first.)
2. Same treatment for `TDecimalFormat` if W2 lands there.
3. Cheap one-off: build master with `OptimizationLevel.AGGRESSIVE` (same file as the obfuscation flag) and
   measure size/load/render vs BALANCED.
4. The JS memo doubles as a fork-patch stopgap for Kronikol if TeaVM's release lags plantuml's.

## Retest checklist (run when the maintainer replies, before composing the post)

1. `git fetch` upstream master; note his current HEAD sha and whether `livebox-patch.diff` still applies
   (`git apply --check`); rebase if not.
2. Rebuild: stock obfuscated, stock unobfuscated, patched obfuscated, patched unobfuscated.
3. Re-run the full corpus bench matrix (Puma 1.2026.6 reference, Teoz 1.2026.6 pragma reference, his HEAD
   stock, his HEAD + our pending patches), warm, same session; regenerate the ratio table.
4. Re-verify SHA identity for every patch on his HEAD.
5. Re-run the post-patch profile; re-rank remaining clusters (his changes may have shifted them).
6. Compose ONE consolidated reply: per finding = explanation + evidence + diff + identity proof + before/after
   table; close with the overall Teoz-vs-Puma ratio table across shapes and what remains. Update the gist if
   the corpus changed. Style rules from "Posting protocol".

## Kronikol-side notes (not part of the upstream package)

- Upgrade to npm 1.2026.8 when it ships: drop the fork, consume `@plantuml/core` directly, pass
  `maxSvgSize: 98304` through worker host / main-thread fallback / Node renderer; re-measure
  `PlantUmlStatementLimits`; re-pin golden fixtures (Teoz Real-Y migration changed layout vs 1.2026.6);
  playbook and gotchas in auto-memory `browser-render-workers.md`.
- After upgrade, add a perf budget assertion to `BrowserRenderWorkerTests`/`LargeReportFixture` so engine
  regressions fail CI here.
- Keep per-engine-version result snapshots in `tools/render-bench/results/`.

## Sequencing and effort

| step | depends on | est. effort |
|---|---|---|
| W0 corpus + instrumentation | — | 0.5-1 day |
| W1 style caching experiments | W0 | 1 day |
| W2 number formatting | W0 | 0.5-1 day |
| W5.1 TeaVM issue filing | — | 1-2 h (not gated by the #2834 reply; different repo) |
| W3 allocation profiling | W0 | 0.5 day, act only on findings |
| W4 O(E) family | W0 evidence | 2-4 h |
| Retest + consolidated post | maintainer reply | 0.5 day |

Roughly 3-4 working days of work to a parity-or-explained-remainder package. The honest gate is W0: if
activation-heavy or group-heavy shapes show different bottlenecks, re-rank from those profiles; "parity on
Kronikol-shaped diagrams" is the explicit fallback target if some Teoz cost proves to be essential solver
work rather than waste.

## Risks

- Some Teoz cost is essence, not waste (the Real constraint solver does more than Puma's fixed layout);
  universal parity is not guaranteed. The per-shape table makes the fallback target explicit.
- Teoz is under active development; keep patches small, single-purpose, and rebase-friendly, and always
  re-verify against the maintainer's current HEAD before posting (see Retest checklist).
- TeaVM fixes ride a different release train; the JS memo stopgap keeps Kronikol unblocked.
- Machine-variance: all quoted numbers must come from same-session A/B runs (see Methodology).
