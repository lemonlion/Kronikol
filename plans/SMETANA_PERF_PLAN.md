# SMETANA_PERF_PLAN — browser Smetana to within 50% of viz.js, then to parity everywhere

Date: 2026-09-01. Goal set by user: `!pragma layout smetana` rendering in the
TeaVM browser build no more than 1.5x the viz.js bridge. Baseline was 2.8x-7.7x.
**Campaign 1 result: 0.36x-1.20x. Campaign 2 (user then asked for wins on ALL
benchmarks): 0.37x-1.0x — Smetana now wins or ties every row; the worst row
(48-class) is a statistical dead heat, median ratio 0.996 over four 32-rep
runs (0.993/0.996/0.997/1.005). See "Campaign 2" below.** Three fixes, all output-identical
(SVG byte-identity pinned stock vs fixed on BOTH the default viz path and the
pragma path, 7 families).

## Method

- Unobfuscated TeaVM build (`obfuscated.set(false)` in plantuml-mit/build.gradle.kts)
  so V8 CPU-profile frames map to Java methods; CDP `Profiler` at 100us sampling,
  30 warm renders of a 24-class diagram; aggregation + caller attribution scripts
  in scratchpad `smetana-audit/` (profile.js, aggregate.js, attr.js, bucket.js,
  bench-ladder.js).
- JVM side: JFR (`jdk.ExecutionSample#period=1ms`, 1500 reps) for the algorithmic
  view; Bench.java harness (same diagram generator as the browser bench).
- Headline numbers from the OBFUSCATED (shipping) engine via bench-ladder.js:
  per-rep alternation (viz,smetana / smetana,viz by rep parity), warm medians,
  first 2 reps per mode discarded.
- Worktree: scratchpad e3c4f61b.../plantuml-smetana (branch teavm-smetana-layout
  = PR #2852 + these three uncommitted fixes).

## What the profile showed (24-class diagram, noobf engine)

Before: smetana 436.5ms vs viz 83.6ms. **76.6% of ALL smetana time was
`JavaError` + `$rt_fillNativeException`** — TeaVM constructs a JS `Error` (with
stack capture) for every Java exception object created. On the JVM the same
render is 22ms total, so the whole browser gap was substrate, not algorithm.

Exception constructions attributed to two sources:

1. **`gen.lib.cdt.dttree__c`** (74.6%): the C `goto` labels of Graphviz's core
   dictionary (`no_root`, `has_root`, `do_search`, `dt_delete`, `dt_insert`,
   `dt_next`) are translated as `RuntimeException` subclasses thrown per dict
   operation — and cdt backs every agnode/agedge/agsubg lookup.
2. **`SmetanaDebug.safeName` via `position__c.dumpAuxEdges`** (the rest): debug
   trace scaffolding ([DEBUG-2735]) whose `SMETANA_TRACE` lines are all
   `if (false)` (dead), but whose label building runs for real — and `safeName`
   throws+catches `UnsupportedOperationException` for EVERY virtual node
   (their `tag.id` is 0, so `Memory.fromIdentityHashCode` misses and throws).

After removing both, the top shared cost was regex: **26% of viz-mode time was
`Pattern` COMPILATION**, 88% of it from `BodierLikeClassOrObject.isMethod`,
which does `s.toString().replaceAll(UrlBuilder.getRegexp(), "")` — recompiling
the URL regex for every member line of every class.

## The three fixes (in the plantuml-smetana worktree, uncommitted)

1. **dttree stackless singletons** (`gen/lib/cdt/dttree__c.java`): the six goto
   classes are stateless and only ever matched by type, so each becomes a
   preallocated singleton constructed once with
   `super(null, null, false, false)`; `throw new X()` becomes `throw X.INSTANCE`.
2. **Trace scaffolding gated** (`smetana/core/debug/SmetanaDebug.java` +
   position__c, mincross__c, dotinit__c, ns__c): new compile-time constant
   `SmetanaDebug.TRACE_ON = false`; `dumpAuxEdges` / `dumpSkeletonOrder` /
   `dumpClusters` / `dumpNegativeSlackEdges` early-return on it, and the eager
   `"guard label=" + safeName(...)` / `dumpSkeletonOrder(zz, g, ...safeName...)`
   call sites are guarded so javac drops them. Flipping one constant restores
   the traces.
3. **URL pattern cached** (`net/sourceforge/plantuml/cucadiagram/BodierLikeClassOrObject.java`):
   `static final Pattern URL_PATTERN = Pattern.compile(UrlBuilder.getRegexp())`,
   `replaceAll` goes through the cached matcher (identical raw-regex semantics;
   the other getRegexp() consumers already precompile).

## Measured results (shipping obfuscated engine, warm medians)

| diagram        | viz stock | smetana stock | ratio | viz fixed | smetana fixed | ratio |
|----------------|-----------|---------------|-------|-----------|---------------|-------|
| class 6        | 27.0ms    | 94.8ms        | 3.50  | 24.1ms    | 20.8ms        | 0.86  |
| class 12       | 42.9ms    | 181.8ms       | 4.24  | 32.6ms    | 33.4ms        | 1.03  |
| class 24       | 67.6ms    | 396.1ms       | 5.86  | 52.1ms    | 56.2ms        | 1.08  |
| class 48       | 136.9ms   | 984.7ms       | 7.19  | 97.8ms    | 117.4ms       | 1.20  |
| component 8    | 14.7ms    | 113.0ms       | 7.69  | 26.3ms    | 20.6ms        | 0.79  |
| state composite| 22.7ms    | 90.8ms        | 4.00  | 31.4ms    | 11.3ms        | 0.36  |
| usecase 7      | 15.1ms    | 61.0ms        | 4.05  | 16.6ms    | 10.6ms        | 0.64  |

(The fixed engine's viz column also improves on class diagrams — fix 3 helps
every mode; the small viz-column wobbles on the tiny diagrams are run noise.)

- JVM dividend: 24-class render 18.5ms -> 10.3ms (1.8x); helps every JVM
  smetana user, and fix 3 helps ALL class diagrams even with native dot.
- Engine size: 3,952,404 -> 3,950,116 bytes (slightly smaller).
- Correctness: check-smetana.js 13/13 PASS on the fixed engine; SVG sha256
  byte-identity stock vs fixed on default AND pragma paths, 7 families;
  JVM `svg bytes=70777` unchanged at every step; full `gradlew test -Pci`
  BUILD SUCCESSFUL with all three fixes in.

## Remaining profile (fixed engine, 24-class)

regex 31% / parse+model 25% / smetana-layout 25% / sdot-bridge 9% (measured on
the fix-1+2 noobf build; fix 3 then removed most of the regex bucket). The
layout engines are no longer the story; parse+draw dominates both modes and is
SHARED, which is why the ratio sits near 1.0.

## Further optimization paths (ranked; none needed for the 1.5x target)

1. ~~dttree goto exceptions~~ DONE (fix 1).
2. ~~dumpAuxEdges/safeName scaffolding~~ DONE (fix 2).
3. ~~isMethod URL regex recompile~~ DONE (fix 3).
4. dttree restructured to return codes instead of throw/catch entirely:
   `$rt_throw` still ~2% post-fix. Small win, invasive, low priority.
5. `Class.getEnumConstants` (~4% both modes): TeaVM returns a fresh clone per
   call; PlantUML calls `values()` in hot parsing paths. Either cache locally
   at PlantUML's hottest call sites or upstream a TeaVM cache. (Related to the
   Kronikol "enumcache" experiment.)
6. Allocation pressure (`jl_Object` ~8%, GC ~1.5%): `CArray.ALLOC__` eagerly
   fills every slot via `ZType.create()` (C calloc emulation); could fill
   lazily in the accessors. `UnsupportedStarStruct`'s constructor bumps an
   `AtomicInteger` UID used only by commented-out debug code — removable.
7. Remaining regex churn (`RegexLeaf.getGroupCount` compiles a throwaway
   Pattern; Pattern2 lazy-compiles per leaf): shared-path item; would lower
   absolute times further for ALL diagrams in the browser.
8. `PathSystem`/BrowserLog console logging per stdlib lookup (~0.7%): quiet by
   default behind a verbosity flag.
9. Genuine algorithm time (`pathplan shortest connecttris`, mincross): normal
   Teoz-style profile-and-patch territory if ever needed; nothing pathological
   visible now.

## Packaging plan (NOT executed — awaiting user go)

The three fixes are independent of PR #2852 (pragma) and benefit the JVM build
too. Proposed: one upstream PR "Smetana performance: stop constructing
exceptions on hot paths" with fixes 1+2 (Smetana-specific), and fix 3 either in
the same PR or a tiny separate one (it is general PlantUML, not Smetana).
Evidence to include: the ladder table, the byte-identity method, and the
`TRACE_ON` escape hatch for the debug scaffolding. After merge+release, PR
#2852's story for GitHub #10111 becomes "WASM-free AND as fast as the WASM
bridge", and the Kronikol dividend (dropping viz-global.js for component-flow
popups) costs nothing in speed.

## sjpp reminder (from #2851)

Any comments added near `::revert when JAVA8` markers must go ABOVE the marker
(sjpp toggles comment state per line). None of the three fixes touch sjpp
blocks, but keep this in mind when upstreaming; verify with
`java -cp sjpp.jar sjpp.App <src> <dst> JAVA8` if in doubt.


## Campaign 2 (same day): win every benchmark row

After campaign 1 the losses were class 12/24/48 (1.03-1.20). Key insight:
shared-path optimizations cannot flip a ratio (both columns move together);
only smetana-only cost counts. Rounds B-E, all committed as 3c73d3c on
`smetana-perf` (on top of 7357843):

- **Round B**: dttree goto exceptions became flag + labeled block (`break
  hasNoRoot` IS a forward goto) — no exceptions at all in dictionary ops, the
  six classes deleted; `CFunction.exeCmpInt` (no varargs Object[], no boxed
  Integer per dict comparison, overridden by the 5 cgraph comparators) and
  `CFunction.exeSearch` (no boxed DT_* flag per dict op, overridden by
  dttree); UnsupportedStarStruct's per-allocation AtomicInteger UID dropped.
  A lazy CArray slot fill was tried and REVERTED: it moved cost into the
  hottest read path (get__ became the #4 JVM frame).
- **Round C** (allocation attribution found these): dotsplines edgecmp
  allocated 4 structs per sort comparison before its cheap early-outs (moved
  into the rare backward-edge branches) — the single biggest lever, class 48
  went 1.17 to 1.03; connecttris hoisted loop-invariant plus_ wrappers out of
  its 3x3 loop; Bezier evaluates on two flat double[] triangles; agsubrep
  uses a per-render scratch template on Globals.
- **Round D**: limitBoxes hoists its 4 fixed spline points out of loops doing
  ~44 accessor calls per iteration; _DTKEY zero-offset fast path (object
  itself is the key) in dttree and Macro.
- **Round E** (closed the last 2 percent): mincross in_cross/out_cross/rcross
  hoist pure get_ reads the JIT cannot common up; reorder walks rank arrays
  by INDEX instead of allocating a plus_ wrapper per pointer step. JVM
  48-class render dropped 21.0 to 18.4ms from this round alone.

Final ladder (obf engine, warm medians; class 24/48 from 32-rep focused runs):
class 6 0.83 | class 12 0.95 | class 24 0.94-0.97 | class 48 0.99-1.01 (median
0.996 over 4 runs) | component 0.61 | state composite 0.37 | usecase 0.57.

Verification at every round: 10-diagram JVM SVG sha256 (HashAll.java: class,
component, deployment, composite state, usecase, packaged component, link
note, LTR, 24-class, JSON) identical stock vs patched; final engine also
pinned via browser svgpar (default + pragma, 7 families) and check-smetana
13/13; full gradlew test -Pci green; engine 3,945,809 bytes (6.6 KB smaller
than stock).

Dead ends, recorded: TeaVM OptimizationLevel.AGGRESSIVE crashes compiling this
codebase ("AssertionError: Variable used before definition") — and beware:
`gradlew ... | tail` masks the exit code, and a failed generateJavaScript
leaves the previous npm-plantuml artifact in place, silently benchmarking the
old engine (caught by sha256 comparison). Lazy CArray fill: net loss.

What remains if anyone wants MORE: eager struct-field allocation
(ST_Agnodeinfo_t allocates coord + 9 ST_elist per node; lazy-fying means
touching direct field access across generated code), ns__c dfs walks
(dfs_range/dfs_enter_inedge/rerank, same CSE treatment as mincross), the
shared-path items from campaign 1 (getEnumConstants clone-per-call,
RegexLeaf.getGroupCount throwaway compiles, console logging) which lower
absolute times but not the ratio.


## Packaging executed (2026-09-02)

Three draft PRs, per the several-small-PRs precedent, each commit measured:

- **#2858** `smetana-goto-exceptions` (2 commits): dttree labeled blocks;
  TRACE_ON gating. Ratios (class 24 / class 48): 5.21/6.97 -> 1.22/1.31 ->
  1.06/1.11.
- **#2859** `smetana-hotpath-alloc` (5 commits): typed dictionary entry
  points -> 1.02/1.08; edgecmp scratch -> 1.02/1.06; pathplan and splines ->
  0.98/0.99; per-lookup constants -> 0.94/1.00; mincross -> 0.94/0.99. M7
  full ladder: every row 0.35-0.97.
- **#2860** `classdiagram-url-regex-cache` (1 commit): viz-path class 24
  69.3 -> 53.8ms, class 48 134.6 -> 97.1ms, back to back runs.

Method: branches rebuilt off master c7d6964 by re-running the patch scripts;
merged result verified tree-identical to the fully tested smetana-perf state.
Measurement branch perf-measure = teavm-smetana-layout + 8 cherry-picks;
engines M0-M8 built serially (engines/ in the audit scratchpad), 32-rep
focused bench per point, svgpar identity per point (all identical, both
paths). No file overlap with #2852; #2858/#2859 auto-merge in any order.
PR bodies: scratchpad pr-a/b/c-body.md, house style checked.
