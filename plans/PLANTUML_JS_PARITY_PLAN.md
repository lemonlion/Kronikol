# PLANTUML_JS_PARITY_PLAN — turning "The Road to Server Parity" into upstream PRs

Date: 2026-09-10. Source: the engineering report at
https://claude.ai/code/artifact/6a67776f-9d37-4d8d-861c-40dec27f4636 (Parts one to nine,
version 1788886459-ddff) plus its session memories (`plantuml-js-full-parity-dive`,
`plantuml-js-size-levers`, `plantuml-js-bundle-granularity`, `plantuml-dom-native-output`,
`plantuml-js-ascii-probe`, `plantuml-js-vs-java-gap`).

**Status: DRAFT, not green-lit. Nothing here is executed.** Every seam, line number and
upstream fact below was re-verified on 2026-09-10 against plantuml/plantuml master `c9d6209`
(1.2026.9beta2; includes the 1.2026.8 release of 2026-09-05, Arnaud's `b0a8655` adding Salt to
the TeaVM build on 2026-09-07, and the merged stdlib loader PR #2873) by a recon-plus-adversarial-
verify pass. Where the report and master disagree, master wins and the correction is noted.

> **This file is a fragment (marked 2026-09-22, `ROADMAP.md` 0.3).** It stops at §3.6. §4 (the
> issues), §5 and §6 (the PR waves), §7 (the prose drafts) and appendix B, which §0 and §1
> describe and which the text below cites, were never committed: `f3f7318d` holds the only
> version of this file, 233 lines, and no other source has them, the engineering report in
> artifact `6a67776f` included (read in full on 2026-09-21: no issue list, no waves, no drafts).
> "Recover" is not an option; writing them again is decision D14 in `ROADMAP.md`, and the plan
> cannot be green-lit before that.

Companion documents: `SMETANA_PERF_PLAN.md` (the campaign that made Smetana viable in the
browser) and `PERF_CI_PLAN.md` (perf-bench methodology), both done and deleted on 2026-09-22
(`git show 158d62e5:plans/<name>`), `TEOZ_PERF_PLAN.md`, `THEME_PLAN.md`. Prose rules for everything posted upstream are in section 3.5 and were
derived from the memory `no-llm-tells-in-public-text` and the accepted bodies of #2861, #2867
and #2873.

---

## 0. What this plan delivers

A ladder of upstream pull requests on `plantuml/plantuml`, each sized for one review sitting,
each tied to an issue (existing where one exists, otherwise opened first), each carrying
test coverage that fails before and passes after, and each with before/after or measurement
images in its body. Together they take the TeaVM-compiled JavaScript build from "22 diagram
families, SVG only, a hand-maintained parser twin that drifts" to the state the report
measured as reachable: every text-rendered family, ten string output formats with
byte-parity to the jar, a tiered engine with a lazy full tier, a smaller default download,
CSS-styleable DOM-native output aligned with the maintainer's own SVGNEWDATA work, and a
short list of layout features that close the remaining gap with Mermaid.

The plan has three layers:

1. **Issues** (section 4): the conversation with the maintainer. Each opens symptom-first,
   names the mechanism, proposes the PR shape, and lists alternatives honestly.
2. **PRs** (sections 5 and 6): the work, in waves ordered so that trust-builders and shared
   tooling land first and every later PR is small because the tooling already exists.
3. **Prose drafts** (section 7): the bodies, written to the house style, ready to be checked
   against the evidence step and posted.

## 1. Where this plan starts: the Smetana-parity assumption

This plan begins **after** the separate "Smetana to Graphviz parity" workstream is complete.
That is a precondition, not something this plan delivers. Concretely it assumes:

- Smetana layout output matches Graphviz `dot` output within an agreed tolerance on the
  reference corpus, so the #1703 class of "Smetana differences to GraphViz" is closed.
- `skinparam linetype` and the `splines` graph attribute are honored under Smetana, or an
  unsupported mode degrades with a diagnostic rather than silently.
- The browser build's recommended default is Smetana-only: `viz-global.js` is optional (the
  #2861 fallback already makes it optional in code; the parity work makes it optional in
  fidelity).

What the assumption changes in this plan, item by item, is recorded in each cluster's
"assumption effects" (section 6). The two biggest effects: the report's "layout engine"
diff class (about 24 of 148 sampled outputs) stops being an untestable category, so Graphviz
families can carry goldens on both the JVM (`nonreg/BasicTest.java:44` already forces
Smetana) and in the browser; and every browser check in this plan runs on a viz-less page.

### 1.1 Verification checklist to run on day one (before any PR is cut)

The recon found that on master `c9d6209` none of the Smetana linetype support exists yet:
`sdot/CucaDiagramFileMakerSmetana.java:607` sets only `rankdir`, the port's `edgeType()`
parser at `gen/lib/common/utils__c.java:997-1040` is a fully UNSUPPORTED stub, the
`ET_PLINE` flat-edge branch at `dotsplines__c.java:1108-1112` and the `ET_CURVED` "goto
finish" at `:456` throw, and `gen/lib` has no `ortho` package at all. So "honored under
Smetana" cannot be true for `ortho` without a port of `lib/ortho`. Before cutting PR-H1 or
any browser check that renders class-family diagrams, confirm which of these the parity
workstream actually landed:

- [ ] `splines` set on the graph next to `rankdir` (mirror of `svek/DotStringFactory.java:157-165`)
- [ ] `edgeType()` ported or bypassed (`utils__c.java:997`)
- [ ] `ET_PLINE` flat edges (`dotsplines__c.java:1108`) and `ET_CURVED` (`:446-456`) reachable without throwing
- [ ] `ortho` diagnosed, not silently ignored (no `gen/lib/ortho` exists)
- [ ] the vega corpus (`src/test/resources/vega`, run under `FORCE_SMETANA`) re-pinned after the parity work's last commit, so this plan's SHA baselines (PR-J2) are taken once

Anything still open on that list becomes PR-H2 (section 6, cluster H) and is scheduled
before the items that depend on it.

## 2. What the report settled and this plan does not reopen

Measured dead ends, kept here so no PR or issue re-proposes them and so issue text can
pre-empt the obvious suggestions. Numbers are the report's, taken on the MIT engine built
from the same tree (baseline 3,947,542 raw / 1,072,448 gzip -9 / 843,805 brotli -11).

| Lever | Result | Why it is closed |
|---|---|---|
| `OptimizationLevel.AGGRESSIVE` | 10,510,833 raw / 2,118,663 gzip, 2.6x larger | Only difference from BALANCED is multi-use inlining (call-site duplication); reachability DCE is level-independent. Also crashes TeaVM 0.14.1 (teavm#1248, root-caused, fix offered) |
| wasm-gc backend | 4,944,316 raw / 1,732,674 gzip: +25% raw, +62% gzip, +44% brotli | Built for real from the same tree; typed bytecode is bigger than minified JS text and compresses worse. Blocked anyway under GitHub's `script-src 'self'` CSP (no `wasm-unsafe-eval`) |
| CLDR locale trim | 0 bytes | `CLDRReader` already defaults to `en_EN`; the residual likelySubtags map ignores the property by design |
| Timezone severing (`%date` to `Intl`) | −41,188 raw but only −7,571 gzip | gzip had already eaten the repetitive TZ tables; not worth breaking `%date` and timing dates |
| Smetana severing | −40,138 raw / −7,992 gzip | gen.lib was already reachable through svek before #2852; severing kills the WASM-free story for 8 KB |
| `maxTopLevelNames` 80,000 | +177 gzip | 80,000 is already the default |
| Assertion removal via SPI plugin | ±150 bytes | reachable assert weight is nil (the SPI plugin mechanism itself is proven and reused in cluster F) |
| Brotli dictionary strings (#2805) | already absent | banked silently between 1.2026.6 and head |
| Code splitting inside TeaVM | impossible at 0.14.1 | one entry point = one self-contained bundle; the maintainer's own 2022 `js-code-splitting` branch is the only prior art (see appendix B) |
| Splitting the Java project into modules | no effect | DCE is method-granular; only reachability from the entry point sets bundle size |

Still-standing levers, all carried by PRs in this plan: terser second pass (−61,161 gzip,
SVG byte-identical), optional `plantuml-src` processing instruction (−21,744 gzip), UTC-only
timezone metadata (−22,924 gzip, costs named-timezone `%date`), and the tier ladder
(sequence-floor 517,215 / github-tier 911,442 / stock 1,072,448 / full 1,251,731 gzip).

Report corrections found by the recon (already applied below): `LabelPosition.INSIDE_BAR`
does not exist and gantt's LEGACY position already draws inside the bar when the label fits
(the Mermaid gap is centering); `SEQUENCE_MESSAGE_SPAN` is a live "temporary pragma", not a
reserved slot; packetdiag needs 13 registration points, not 8; pie does not need a new
diagram family because the chart family (`SeriesType` BAR/LINE/AREA/SCATTER) is its home
and absorbs quadrant (#2115) too; the fork registers 21 distinct factories today (Salt
landed 2026-09-07), and `checkEndingBackslash` is hard-coded off at two sites in
`PSystemBuilder2`, not one.

## 3. Working rules for every PR

### 3.1 Size and shape

- One subsystem per PR, reviewable in one sitting: XS/S PRs are under ~60 changed lines,
  M under ~250, L is reserved for genuinely new families or packers and is always preceded
  by an issue that agreed the design.
- **Byte-identical default path.** Any PR that touches a rendering or resolution path pins
  SVG SHA-256 identity on the corpus for every source it does not intend to change, and says
  so in the body with the corpus and the engine sha256. Feature PRs are off by default.
- Trust-builders first: each cluster opens with its smallest obviously-correct PR.
- No stacking unless unavoidable; when a PR must stack, the body says so and the base PR
  is named. Rebase to a single standalone commit as soon as the base merges (the #2861
  pattern: a stacked PR that goes `mergeable_state = dirty` after the base squashes stops
  running CI, which looks like "CI never triggered").
- Cut every PR from a fresh master (never from the `ascii-probe` branch wholesale: it
  predates Salt and 23 further commits and conflicts in `PSystemBuilder2`). The prototype
  diff (`ascii-probe` minus `stdlib-loader`, 657 lines, 12 hunks) is a reference, and the
  `plantuml-mit/build.gradle.kts` hunk in it (AGGRESSIVE comment + `Locale.available`) is a
  measurement leftover that must not be carried into any PR.

### 3.2 Test coverage rules

- **JVM (JUnit 5, `src/test/java`, runs under plain `gradle test` in CI's `test_linux`)**
  for everything the JVM can reach: parser/builder parity, golden output (the vega
  convention: `VegaTest` compares `SvgCleaner.normalise` output, so say "normalised-XML
  identical" rather than "byte-identical" for vega goldens; goldens are written when absent
  or with `VEGA_FORCE_WRITE=true`), string-level assertions for dot attributes (CI has no
  Graphviz installed).
- **Browser (`tools/browser-test/check-<name>.js`, Playwright 1.62.1 with `--only-shell`,
  a README section per check, one step in `.github/workflows/browser-test.yml`, which is the
  only PR-blocking job that builds the TeaVM engine)** for everything behind
  `TeaVM.isTeaVM()`: a JUnit test cannot reach those branches because the marker is a
  compile-time constant that reads false on the JVM. Every check states its red/green
  counts ("7 of 14 fail on master, all pass here"). Checks classify on painted `<text>`,
  never on "an SVG came back": the "Diagram not supported by this release" card is itself
  a valid SVG (`PSystemUnsupported.java:62`), and PlantUML emits one `<text>` per word.
- **Both, sharing one golden,** for the text output formats: the JUnit test pins the jar's
  `utxt`/deterministic-SVG output to a checked-in file and the browser check byte-diffs the
  engine's output against the same file (PR-J3).
- **Java 8 Ant job**: the sjpp `JAVA8` strip compiles the whole `src/main/java` tree,
  including the `teavm` package. Explanatory comments go above a `::revert` marker, never
  inside (the per-line comment toggle turned a comment into bare prose in #2851's first
  push). Verify locally: `java -cp sjpp.jar sjpp.App <src> <dst> JAVA8` then `javac`.
- **DCE reachability** is proven only by `gradlew :plantuml-mit:npmPackage -Pci` succeeding;
  a green `gradle test` says nothing about the engine. TeaVM classlib gaps surface as
  `generateJavaScript` compile errors or, worse, as runtime static-initializer failures
  (the `\p{N}` case), which only the family matrix (PR-J2) detects at runtime.

### 3.3 Verification gate (every PR, before every push)

1. `gradlew test -Pci` green; restore `src/test/resources/vega/*` files the run mutated
   before committing (`git add -A` sweeps them in).
2. sjpp `JAVA8` strip compiles locally for every touched file with markers.
3. `gradlew :plantuml-mit:npmPackage -Pci` (JDK 25 works, 54 s to 2 min); record the
   artifact's sha256 and raw/gzip/brotli sizes in the PR body. `generateJavaScript` alone
   writes `build/generated/teavm/js/plantuml-mit.js` without refreshing
   `npm-plantuml/plantuml.js`, and `gradlew … | tail` masks exit codes: a failed build leaves
   a stale engine that measures like the old one. Hash before measuring.
4. All `tools/browser-test` checks green against that artifact (six today, plus this plan's).
5. SVG SHA-256 identity on the default path across the corpus (7 families today; all
   registered families once PR-J2 lands) against the base engine.
6. perf-bench dispatched on the fork branch with `compare=git:<base sha>`: every row within
   band (tolerance 0.10) and `= ref` on every SHA column for byte-identical PRs.
7. CRLF preserved (repo Java files are CRLF; `sed` rewrites endings, `git checkout` the file
   if a script touched it).
8. Commit message plain; no attribution lines.

### 3.4 Evidence rules

- Every PR body carries at least one image with descriptive alt text; feature PRs carry
  before/after renders (Playwright, `deviceScaleFactor: 2`), size PRs carry a raw/gzip/brotli
  bar chart, perf-sensitive PRs carry the ratio-to-reference chart with the parity line
  (the `pr2859-ratio-by-commit.png` shape). Charts follow the dataviz house palette.
- Images live on the fork's orphan branch `pr-assets` (lemonlion/plantuml, HEAD `da232c7`
  holds 13 PNGs from the merged Smetana PRs) and are referenced by SHA-pinned
  `raw.githubusercontent.com/lemonlion/plantuml/<sha>/<file>.png` URLs, never by branch
  name, so a force-push can never change what a merged PR shows.
- The evidence generators from earlier sessions (`evidence.html` + `shoot.js`,
  `gen-shots.js`, `shots-2861.js`) are gone with their scratchpads. Re-create one generator
  under `C:\Code\Kronikol\tools\render-bench\evidence\` (versioned, next to `bench-real.js`
  which already does local-http + ESM import + Playwright render + SVG SHA) and keep it.
- A Smetana render or profile must show `gen.lib` frames and no wasm frames; the first
  #2858 "after" profile was a viz render because the branch predated #2852.
- Numbers in prose come with their method (reps, engine, corpus) and are re-measured on
  the exact engine sha256 the PR builds; the report's numbers are the expectation, not
  the claim.

### 3.5 House style for issues and PR bodies

The full rulebook is reproduced in section 7.0. The short version: no em dashes (and no en
dashes as sentence punctuation), no emoji, no marketing adjectives, no "delve/leverage/
unlock/streamline", no bold-led bullet scaffolding, no rhetorical questions or exclamation
marks, first sentence states the symptom or the user-visible effect (never restates the
title), numbers carry their method, "I" is fine, "we" is not, thank the reviewer once when
there is something specific to thank them for. Issues use the repo templates verbatim
(`bug_report.md`, `feature_request.md`) with an optional **Context** paragraph first when
the issue belongs to a thread; PRs have no template upstream, short bodies for small PRs
and Problem / Change / Tests / Evidence / Performance headings for large ones (#2873).
"Closes #N" only when the PR fully resolves the issue; "Part of #N" or "Tracked in #N"
when several PRs share an issue, so one merge does not auto-close it.

### 3.6 Upstream mechanics

- Fork `lemonlion/plantuml` (remote `fork` in `C:\code\plantuml`; the local clone is shallow
  and its `master` is 23 commits behind: fetch first). Cut PRs from a fresh blobless clone
  or a detached worktree at `origin/master`; never `git mv` into a directory that already
  has untracked `node_modules` (it nests).
- lemonlion holds TRIAGE on plantuml/plantuml since 2026-09-06: labels, close/reopen, assign,
  request reviewers. Not push, not merge, not CI rerun. A transient CI failure is cleared by
  refreshing the commit timestamp and force-pushing with lease (the #2860 precedent).
- `gh api pulls/N --jq .mergeable_state` before concluding "CI never triggered"; require a
  minimum check count before declaring CI settled (triage completes in seconds).
- The maintainer merges silently and quickly (every engine PR from the Smetana workstream
  merged within days with no reply); watch commits, not threads. Nudge once at two weeks of
  silence on a PR; never on an issue.
- Release cadence: 1.2026.8 shipped 2026-09-05, master is 1.2026.9beta2. perf-bench's
  reference pin is `npm:1.2026.8`. PRs in this plan target 1.2026.9 and later.
