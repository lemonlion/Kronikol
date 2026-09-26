# Engine pin plan (`ROADMAP.md` 1.8, `STAGE_1_PLAN.md` P4)

**Date:** 2026-09-22 · **Repo version:** 3.27.2 (`2843018a`) · **Status:** written, nothing
implemented, **NOT green-lit**. S0 needs no decision. S1 to S6 need D6; the recommendation below is the
roadmap's (move now, keep `viz-global.js`), with the measurements that back it. Two patch releases: S0
alone, then S1 to S6. §1 is what was RUN and READ on
2026-09-22, §9 is the assumption ledger, §10 is the execution log, empty until the work starts.

**Re-checked 2026-09-25 against 3.29.3 (`b74c8ddc`):** every source, test and render-bench file this plan
cites is unchanged since `2843018a` except `ReportGenerator.cs` (the fragment-height parameter moved
from line 940 to 964, updated in §1.9). In the wiki, the `Diagnostics-and-Debugging` telemetry check
moved from line 536 to 541 (updated in §1.6 and §3.6); the `Report-Configuration` rows 129 and 136 still
hold. Kronikol4J's README was rewritten (`92f2334`), and the `plantuml-browser-render-script.js`
divergence that S6 extends is still its first ledger entry. Nothing in the design moved.

**Third pass, 2026-09-25** (§1.14 to §1.16, the re-check in §1.5, the browser-support paragraph in §1.10,
Appendix A.9 to A.12):

- **The emitter check.** Kronikol's own emitter renders byte-identical on both builds, not only the bench
  corpus: 13 current-format sources with header blocks, step bars in both forms, assertion notes,
  internal-flow links, focus colours and a 41,573 px note (§1.16).
- **The code cache.** A release node checks no checksum on V8's code cache, and a damaged cache crashes
  node on every later render. So S3 gains a checksum and an atomic write in `plantuml-render.js` (§1.14).
- **Payload text reaches the engine's own script loader, which the integrity check does not cover.** The
  emitter leaves `<&name>` and `<:name:>` unescaped. One such payload stalls every later diagram of a
  NodeJs batch and holds a BrowserJs worker, on both builds (§1.15). The defect is older than the pin and
  belongs to P3. P3's own third pass found it the same day, along with the Node renderer's non-XML SVG,
  and plans the fixes as its first patch. This plan adds the npm build's `PLANTUML_STDLIB_LOADER` hook, measured
  as a one-line way to fail fast, and hands that on (§7).
- **Registry, CDN and browsers.**
  - Under npm's written policy the owner cannot unpublish 1.2026.8.
  - jsDelivr freezes a static version on both routes, which narrows the tag-move gap this plan described.
  - Every browser feature S2 uses predates the report's floor.
- **A limit that fails in the worker.** The request statement that carries the internal-flow link fails in
  the Chromium worker from about 1,100 characters, on both builds, where the cap is 2,000 (§1.11). It is
  older than the pin, and P3's session found it first. The owner made its fix the plan's first step, S0
  (§3.0).
- **S2 changes in four places.**
  - A `viz-global.js` mismatch drops Graphviz only, which is the shim's own rule for a viz failure.
  - The telemetry becomes two fields.
  - The classic-build branch goes with the classic tag.
  - The fidelity E2E's loader follows the new fallback.

1.2026.9 was still unpublished on 2026-09-25.

**Revised the same day, second pass** (§1.10 to §1.13 and Appendix A.6 to A.8): S2 now rests on the
browser's own integrity check (`fetch(url, { integrity })` and `<script integrity>`, enforced in
Chromium, Firefox and WebKit on every origin measured) instead of digest code in the shim; the fallback
path is verified too, so the residual the first draft accepted is gone; S3 gains the cross-process race
and a corrected claim about the V8 code cache (it checks the source length, not its bytes); the
statement-limit probes are already run on the npm build; the hash constants are verified against the
registry tarball, not only against jsDelivr.

Stage 1 item 1.8 in four parts, as the roadmap and the grouping state them: the pin at
[TrackingDefaults.cs:28](../src/Kronikol/Constants/TrackingDefaults.cs#L28) moves from the personal fork
tag to the published npm package on jsDelivr's immutable `/npm/` route; a known-hash check on the engine
fetch in the report page and in the Node renderer's cache, because today the report runs whatever the
CDN returns for the tag; the perf-budget assertion `TEOZ_PERF_PLAN.md` promised after the upgrade; and a
re-measurement of `BrowserFragmentMaxHeight`, recorded as a number only, because a default flip is a
major and waits for 12.1. The `maxSvgSize` pass-through at the three engine sites is unchanged and stays
pinned by its tests.

**The headline measurement (§1.3):** npm `@plantuml/core@1.2026.8` renders the whole render-bench corpus,
plus a class, a JSON and a Kronikol-shaped component diagram, **byte-identical** to the pinned fork build,
and 4% faster on the sequence corpus. The third pass added 13 sources written by Kronikol's own emitter,
also byte-identical (§1.16). So the roadmap's "golden re-pins and a ledger entry only if SVG bytes
differ from `0e4f452`" resolves to: no golden re-pin. One Kronikol4J ledger line is still owed, because the
render script's bytes change (the URL and the check), not because any diagram does.

---

## 0. Summary

| Slice | What | Bump | Tests it adds |
|---|---|---|---|
| S0 | A long request label keeps its diagram in the worker. The text inside the internal-flow link is capped by a new internal `MaxLinkedLabelChars`, set from a worker bisection with the usual margin (§1.11, §3.0) | patch, its own release, first | a unit test on the capped linked label; a worker E2E at 2,300 characters; the probe re-run |
| S1 | `PlantUmlJsCdnBase` = `https://cdn.jsdelivr.net/npm/@plantuml/core@1.2026.8`; doc comments, the step-bar test's included (§1.6); the four literal asserts; the two render-bench scripts stop pinning any tag; the statement-limit doc comment reworded on the numbers already measured (§1.11) | patch | a route-shape test on the constant; the four asserts moved |
| S2 | The report page fetches `plantuml.js` and `viz-global.js` with the Fetch API's `integrity` option and loads the fallback through `integrity` attributes on a classic and a module tag, so the browser verifies the bytes against constants baked into the script before a byte of either is evaluated and the shim carries no digest code; an engine mismatch is a refusal, not a fallback, and a `viz-global.js` mismatch drops Graphviz only; the fallback's `new Function` and the pre-3.0.76 classic-build branch go | patch (constants are `internal`) | script-content tests; wrong-hash E2Es for the engine on the worker path and on the main-thread path, and for viz; a telemetry E2E on the real CDN |
| S3 | The Node renderer verifies the same hashes on download and on every start, re-downloads once, then throws a message that names both hashes; writes are atomic and safe across processes; the code cache is deleted whenever the engine file is replaced, because V8 checks the source length, not its bytes (§1.12), and it carries its own checksum and is written atomically, because a damaged cache crashes node on every later render (§1.14) | patch | an `EngineCache` class tested with a temp directory and a fake downloader, no network; a node-level test that a damaged code cache is rejected and rebuilt; the stale code-cache comments corrected |
| S4 | Two contention-scaled budgets in the existing E2E guard: per-render worker time on the note-heavy fixture, and one arrow-heavy diagram; the fork-vs-npm ladder table filed under `tools/render-bench/results/` | none | the assertions, proven red by a 1 ms budget |
| S5 | `LargeReportFixture` takes a fragment height; the bench is run at 4,000 / 6,000 / 8,000 / 12,000 px; the table goes to §10 and to 12.1; no default moves | none | the fixture parameter only |
| S6 | Changelog, four wiki pages, the Kronikol4J ledger, `PLANS_STATUS`, `ROADMAP` 1.8 and D6, the version in three files, tag, push | | |

Order: S0 first, as its own patch, because it needs no decision; then S1, S3, S2, S4, S5, S6 (§4). Everything after S1 measures against the new engine, S3 is pure
unit work and goes before the E2E-bound S2, and S5 is a measurement with no product change.

---

## 1. What was checked (2026-09-22)

RUN means a command was executed and its output is in this file or in Appendix A. READ means a file was
read at HEAD.

### 1.1 The two builds

| | Fork tag `v1.2026.8beta1-0e4f452` (pinned) | npm `@plantuml/core@1.2026.8` |
|---|---|---|
| Source | plantuml/plantuml master `0e4f452e` (2026-09-04), built locally, pushed to `lemonlion/plantuml-js-plantuml_limit_size_98304` | release commit `149874a1` (2026-09-05, "version 1.2026.8"), published by the project; `time.modified` 2026-09-06T08:23:16Z |
| `plantuml.js` | 3,946,817 B, SHA-256 `eeee8a60…8064` | 3,947,570 B, SHA-256 `ade8f15e…4d52`, jsDelivr lists it as `rejxXtfyoyJYFDMtOsbQW8fr277IYwUSzHz2eLwMTVI=` |
| `viz-global.js` | 1,445,427 B, SHA-256 `ef2cd8a0…6cba` | 1,445,436 B, SHA-256 `fc6ca2de…dc5e`, listed as `/Gyi3oPdTj/Kln1SFBInd4kKXNGrEBEVofcW/ITm3F4=` |
| `themes.js` | 326,345 B | 326,396 B, listed as `1vUIcEsna2ZjHP/wUpHeMBOreAeXK3yyt3oU1L6rLC0=` |
| Also in the package | | `emoji.js` 1,876,355 B, `openiconic.js` 51,251 B, `main.js`, four demo pages, `GITHUB_INTEGRATION.md`; 19 files, 7,756,543 B unpacked, `dist.integrity` `sha512-md2wGuaI…` |

RUN: `npm view`, the jsDelivr package listing API, `curl -I` on every file on both routes, `sha256sum` on
the downloaded files (the downloaded npm bytes hash to exactly what the listing says, so the listing hash
is usable as the pinned value). The local copies `tools/render-bench/core-1.2026.8beta1-0e4f452.js` and
`core-npm-1.2026.8.js` hash the same as the CDN files.

### 1.2 What changed upstream between them

RUN: `gh api repos/plantuml/plantuml/compare/0e4f452e...149874a1`. Nine commits, 87 files, all on top
of the pinned commit (status `ahead`). Java sources touched:

| File | Change | Reachable from a Kronikol diagram? |
|---|---|---|
| `teavm/browser/TeaVmScriptLoader.java` +59 −11 | #2873, stdlib includes for every host (`PLANTUML_STDLIB_BASE`, `PLANTUML_STDLIB_LOADER`) | No. The JS engines never see an `!include`: [ComponentDiagramReportGenerator.cs:43](../src/Kronikol/ComponentDiagram/ComponentDiagramReportGenerator.cs#L43) renders the plain-shape form under BrowserJs and NodeJs because plantuml.js has no stdlib |
| `core/UmlSource.java` +1, `core/DiagramChromeFactory.java` 1/1, `security/SURL.java` +2, `security/authentication/SecurityCredentials.java` +1, `eggs/QuoteUtils.java` 1/1 | one-liners around the same feature and Javadoc | Not on the sequence, skin, style or SVG path |
| `gantt/ngm/*` (+4), `png/quant/Quantify555.java` 3/2 | gantt maths, PNG quantiser | Gantt is not emitted; the PNG path does not exist in the TeaVM build |

The other 78 files are CI workflows, the `perf-bench/` and `browser-test/` move under `tools/`
(#2868), `gradle.properties` (the version), Javadoc, and eight vega non-regression resources. Nothing in
the sequence engine, the skin parameters, the SVG writer or the TeaVM DOM bridge.

### 1.3 Output identity and speed (RUN)

`tools/render-bench/bench-ladder.js` in the E2E project's Chromium 147.0.7727.15, the two engines
interleaved per rep on one page each, cold render discarded, `maxSvgSize` 98304, the teoz pragma
injected (the corpus files do not carry it; every Kronikol diagram does). SVG SHA-256 compared per
file. Full output in Appendix A.

| Corpus | Files | e1/e0 geomean | SVG |
|---|---|---|---|
| Sequence (`gen-50/200/500`, `puml-0/1/19`, six `shape-*`), REPS 5 | 12 | **0.961** (range 0.84 to 1.01) | all `same` |
| `probe-class`, `probe-json`, a Kronikol-shaped component diagram (`tools/render-bench/probe-component.puml`: plain shapes, `left to right direction`, the generator's skinparams, five arrows), REPS 2 | 3 | 1.006 | all `same` |

So the npm build is a superset of the pinned build for everything Kronikol emits, at the same speed or
better. The 1.2026.9 line is a different matter: master `70cc513` trims trailing zeros in SVG numbers
(`0.50` to `0.5`, memory of the 2026-09-21 profile), which is the second re-pin D6 warns about and the
reason to take 1.2026.8 now rather than wait.

### 1.4 `viz-global.js`

The two files differ in 1,298,280 byte positions and 9 bytes of length, which looked like a real
difference. It is line endings: the npm file has CR LF at its nine line breaks, the fork file LF. After
stripping CR the files are identical. The nine breaks sit at offsets 3, 17, 46, 47, 110, 144, 177 and
180 (inside the Viz.js licence comment and between `*/` and `!function(A,I)`) and at the trailing newline,
so no break falls inside a string. Same program, same Graphviz WASM. D6's "keep or drop" therefore
costs nothing to answer "keep": the file ships in the same package at the same version, needs one more
hash constant, and dropping it would remove Graphviz-family layouts from BrowserJs, which is 6.1 and §5
territory, not stage 1.

### 1.5 The routes

RUN, `curl -I` on both routes: `Cache-Control: public, max-age=31536000, s-maxage=31536000, immutable`,
`X-JSD-Version-Type: version`, `Access-Control-Allow-Origin: *`, `Content-Type: application/javascript;
charset=utf-8`, `X-Content-Type-Options: nosniff`. The npm route answers 404 for a file the package does
not hold. The two routes differ in what stands behind the URL, not in how jsDelivr serves it:

- `/gh/<owner>/<repo>@<tag>/` resolves a git tag. The owner of the fork can delete and re-push the tag;
  jsDelivr's edge would keep the old bytes until its cache expired or was purged, and the origin would
  serve new ones. That is the "mostly theoretical tag-move gap" the parity report names. The re-check below
  narrows it further.
- `/npm/@plantuml/core@1.2026.8/` resolves a registry version. The npm unpublish policy (RUN, fetched):
  "Registry data is immutable, meaning once published, a package cannot change" and "Once
  `package@version` has been used, you can never use it again. You must publish a new version even if
  you unpublished the old one." Nobody, the project included, can put different bytes behind that URL.

What "leaving the fork" means: new reports stop pointing at it. Every report generated by 3.0.76 to
3.27.2 references the fork tag, Kronikol4J's report script pins the older `v1.2026.3beta6-patched` tag
in two Java files (`DotNetHtmlReportRenderer.java:72`, `HtmlReportGenerator.java:36`, READ), and the
old `v1.2026.6-patched` tag serves Kronikol 3.0.50 to 3.0.75 reports. **The fork repository and its
tags stay published indefinitely.** The plan deletes nothing there.

**Re-checked 2026-09-25** (READ and RUN; the sources are quoted in A.12):

- **Unpublishing.**
  - npm lets an owner unpublish a version older than 72 hours only when all three of these hold: no
    public package depends on it, it had fewer than 300 downloads in the last week, and it has a single
    maintainer.
  - `@plantuml/core` has 7 public dependents (deps.dev). Six declare `^1.2026.6` and one `^1.2026.8`,
    and all of them resolve to 1.2026.8. It had 4,166 downloads in the week to 2026-09-21. So its owner
    cannot unpublish 1.2026.8 under the written policy.
  - npm support can still remove a version, and the policy does not rule that out.
  - A name@version can never be reused.
- **jsDelivr keeps what it has served.**
  - It stores a file permanently the first time it serves it, and keeps serving that copy "even if a npm
    package gets deleted".
  - It says the same of a deleted GitHub release or repository: "once we download your tagged files,
    there is no way for you to update them".
  - The fork tag is served with `X-JSD-Version-Type: version`, as the npm route is.
  - So both routes are frozen at the CDN once fetched. The tag-move gap above is open only for a file
    jsDelivr has never served.
  - What the npm route adds is an origin that cannot change either, and a registry-signed tarball that
    anyone can hash against the constants (§1.13).
- **jsDelivr is not unconditional.**
  - In July 2026 it withdrew eight malicious npm versions it had kept serving (jsdelivr/jsdelivr#18727;
    all return 404 now).
  - Its terms reserve the right to withdraw anything.
  - Availability is beyond what a hash check can give. §2's mirror option is the answer to that.
- **The bare package URL is not the file.** jsDelivr's default file for the bare package URL is a
  `plantuml.min.js` that jsDelivr generates, and it is not in the tarball. The constants pin explicit file
  paths only, and jsDelivr serves those byte-identical to the tarball (RUN, `cmp`).

What CORS gates on the ES-module build (RUN, §1.10): `import()` of a cross-origin module needs the
CORS header in Chromium and Firefox exactly as `fetch` does; only WebKit's `import()` loaded the engine
from a server that sent none. A classic `<script>` loads without it everywhere, but the engine is not
a classic script any more. So a host without CORS gives no engine on either path in the two engines
the E2E suite and most viewers use, and the wiki's sentence "the fetch fails and the page takes the
fallback below" describes the classic builds before 3.0.76. S6 rewrites it; nothing in S2 depends on it.

### 1.6 Where the pin is read (READ)

| Site | What it does with the URL | Change |
|---|---|---|
| [TrackingDefaults.cs:28](../src/Kronikol/Constants/TrackingDefaults.cs#L28) | the `public const` | new value, new doc comment (S1) |
| [NodeJsPlantUmlRenderer.cs:15-36](../src/Kronikol/PlantUml/NodeJsPlantUmlRenderer.cs#L15-L36) | `CdnBase`; `CdnVersionSegment()` takes the last path segment (`Split('/')[^1]`) and the text after its first `@`, with invalid file-name characters replaced, for the cache directory `%LOCALAPPDATA%/Kronikol/plantuml-js/<segment>/` | on the npm URL the last segment is `core@1.2026.8`, so the directory becomes `1.2026.8`, distinct from every fork directory; no change needed. `DownloadIfMissing` (line 257) trusts whatever `GetByteArrayAsync` returns and skips a file that exists, and `EnsureInitialized` locks within its own process only, so two processes sharing the directory race on `File.WriteAllBytes`: S3 |
| [DiagramContextMenu.cs:34,64](../src/Kronikol/Reports/DiagramContextMenu.cs#L64) | substitutes `__PLANTUML_CDN_BASE__` into the render script | gains the hash constants (S2) |
| [plantuml-browser-render-script.js:33-34](../src/Kronikol/Reports/plantuml-browser-render-script.js#L33), `acquireEngine` (line 207) | `fetch(...).text()` on both files, rewrites the ESM tail, inlines into one Blob with the worker host; `loadEngineScripts` (line 266) is the fallback: a classic tag for viz, a classic tag for the engine (an `export` in a classic script is a syntax error and the load event fires anyway, so that tag only warms the cache), then `import()` through `new Function('u', 'return import(u)')`, which a content security policy without `unsafe-eval` refuses, so a hosted report under such a policy loses the fallback with "Refused to evaluate a string as JavaScript" | S2, including the `new Function` and the `realLoad` branch for the pre-3.0.76 classic builds after it (lines 269 to 291) |
| [plantuml-worker-host.js:192-201](../src/Kronikol/Reports/plantuml-worker-host.js#L192) | `loadEngine(esm)`: takes the exports the page rewrote in; passes `{ maxSvgSize: 98304 }` | none; the check runs on the page, where the bytes are |
| [plantuml-render.js:243-272](../src/Kronikol/PlantUml/plantuml-render.js#L243) | `loadEngineWithCodeCache`: reads the cached file, rewrites the tail, `vm.Script` with `plantuml.js.v8cache`, and writes a new cache with `fs.writeFileSync` straight to its final name (line 267) | the engine handling stays; it reads what .NET verified. Its comments (lines 12 and 242) and the `LastCodeCacheStatus` doc say V8 rejects the cache for "a changed file", which holds only when the length changes (§1.12): S3 rewords them and deletes the cache whenever it replaces the engine. The cache itself reaches V8 unchecked, and a damaged one crashes node (§1.14): S3 item 6 |
| Tests naming the tag: [PlantUmlBrowserReportGeneratorTests.cs:41-42](../tests/Kronikol.Tests/PlantUmlBrowser/PlantUmlBrowserReportGeneratorTests.cs#L41), [DiagramContextMenuTests.cs:895-896](../tests/Kronikol.Tests/Reports/DiagramContextMenuTests.cs#L895) | literal URL asserts | S1 |
| [StepBarPlantUmlTests.cs:11](../tests/Kronikol.Tests/PlantUml/StepBarPlantUmlTests.cs#L11) | a doc comment: the step bar's two forms were "measured on the stock v1.2026.8beta1-0e4f452 build" (missed by the first two passes, found by the 2026-09-25 grep) | S1 wording: the npm build draws both forms identically (§1.16) |
| [BrowserRenderWorkerTests.cs:415-460](../tests/Kronikol.Tests.EndToEnd/BrowserRenderWorkerTests.cs#L415) | the fidelity E2E's own main-thread loader: a classic tag, then `new Function('u', 'return import(u)')`, commented as loading "exactly as the shim's own main-thread fallback does" (line 428) | S2: it follows the new fallback, or the comment stops being true |
| [NodeJsPlantUmlRendererTests.cs:438-447](../tests/Kronikol.Tests/PlantUml/NodeJsPlantUmlRendererTests.cs#L438), [DependencyCategoriesTests.cs:53](../tests/Kronikol.Tests/Constants/DependencyCategoriesTests.cs#L53), [BrowserRenderWorkerTests.cs:460](../tests/Kronikol.Tests.EndToEnd/BrowserRenderWorkerTests.cs#L460) | derive from the constant | keep passing |
| `tools/render-bench/bench-report.js:13`, `variants/worker.js:16` | hard-code `v1.2026.3beta6-patched`, two pins behind already: `bench-report.js` rewrites that URL to a local file and silently rewrites nothing on a 3.0.50+ report | S1: match any jsDelivr engine URL by pattern |
| `tools/render-bench/README.md:20`, [PlantUmlStatementLimits.cs:43-45](../src/Kronikol/PlantUml/PlantUmlStatementLimits.cs#L43) doc comment | name the fork build | S1 wording |
| Wiki: `PlantUML-Browser-Rendering.md` lines 45, 64, 68, 70, 289, 311; `Report-Configuration.md` 129, 136; `Large-Response-and-Diagram-Handling.md` 11; `Diagnostics-and-Debugging.md` 541 (536 when written) | "a fork repository holding tagged engine builds", the `file://` CORS paragraph, the fallback reasons and line 68's account of the fallback detecting the missing `plantumlLoad` and calling `import()`, the cache directory, the fragment-height numbers | S6 |

The `maxSvgSize` pass-through sits at the three ESM sites the roadmap counts (worker host line 200, the
page's `import()` fallback at line 279, `plantuml-render.js` line 320) and is pinned by
`DiagramContextMenuTests.Esm_render_call_sites_pass_the_max_svg_size_option`, the Node stub that echoes
`"maxSvgSize":98304` in `NodeJsPlantUmlRendererTests`, and the E2E render past the stock 8192 px
default. No change; they are the regression net for S1.

No consumer-facing override of the CDN base exists (`ReportConfigurationOptions` has none, READ), and no
report carries a CSP. A consumer who serves reports under their own CSP already allows
`cdn.jsdelivr.net`; the move changes the path, not the host.

### 1.7 Hashing in the browser (RUN)

A Playwright probe (`tools/render-bench/secure-context-probe.js`) opened a page three ways in the E2E project's
Chromium 147 and its Firefox build 1511, fetched the npm `plantuml.js` (3,947,570 B) and hashed it with
`crypto.subtle.digest` and with a plain-JavaScript SHA-256 (the 30-line function the first draft of S2
shipped as the fallback), on the page and in a Blob worker:

| Page origin | `isSecureContext` | `crypto.subtle` page / worker | subtle ms (Chromium / Firefox) | plain JS ms | digests |
|---|---|---|---|---|---|
| `file://` | true | object / object | 3 / 2 | 25 / 72 | equal, and equal to the jsDelivr listing |
| `http://127.0.0.1` | true | object / object | 3 / 2 | 27 / 72 | equal |
| `http://192.168.1.217` (LAN) | **false** | **undefined / undefined** | n/a | 25 / 64 | JS digest still equal |

So the native digest is free (3 ms for the engine, under 5 ms for both files) wherever reports are
usually opened, and absent exactly where a team serves reports over plain http from a non-loopback host.
The plain-JS fallback costs 25 ms in Chromium and about 70 ms in Firefox, once per page, against an
engine compile of 300 to 1,300 ms (§1.8).

**Superseded the same day by §1.10.** The browser hashes inside `fetch` itself, on every origin, secure
context or not, so the shim needs neither `crypto.subtle` nor a digest of its own. This table stays as
the record of what that code would have cost and why it was considered.

### 1.8 What the perf guard measures today (RUN, the results file)

`Large_report_renders_off_the_main_thread_within_budget` writes one JSON line per run to
`tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/PlaywrightOutput/render-bench-results.txt`. The 38
worker-mode rows since 3.0.76 (2026-09-04 to 2026-09-22, 6 diagrams, 70 renders, 10 fragments on the
largest), and the 11 rows of the 1.2026.6 engine just before it:

| Metric | min | p50 | p90 | max |
|---|---|---|---|---|
| `WorkerMs / Renders` (ms per fragment render, 4 workers in flight) | 170 | 291 | 506 | 606 |
| the same divided by the contention stretch | 81 | **146** | 185 | 235 |
| stretch (probe median over 20 ms, capped at 5) | 1.0 | 2.0 | 5.0 | 5.0 |
| `AllMs` / stretch | 1,999 | 3,875 | 4,687 | 5,675 |
| `ToggleMs` / stretch | 331 | 615 | 754 | 791 |
| engine fetch plus compile (`EngineReadyMs` minus `ReadyMs`) | 316 | 570 | 941 | 1,296 |
| 1.2026.6 engine, per-render / stretch (n = 11) | 96 | 153 | 184 | 213 |

Two facts shape S4. First, the existing budgets (`ReadyMs` 4,500, `WorstTaskMs` 500, `AllMs` 60,000,
`ToggleMs` 5,000, `ToggleWorstTaskMs` 800, `DeepQueryMs` 2,000, each times the stretch, three attempts)
are page-responsiveness checks; none is an engine-speed check, which is what the Teoz plan asked for.
Second, **the fixture cannot see a Teoz regression**: its diagrams are note-dominated (2 to 8 KB JSON
notes, 40 arrows), and the engine that was 4 to 8 times faster on arrow-heavy shapes moved this fixture's
per-render figure from 153 to 146 ms. Text measurement, not layout, is its cost. An engine-speed budget
needs an arrow-heavy diagram in the measurement.

### 1.9 The fragment height

`BrowserFragmentMaxHeight` is 12,000 px ([TrackingDefaults.cs:41](../src/Kronikol/Constants/TrackingDefaults.cs#L41)).
The render bench (2026-08-22, artifact "Kronikol Render Bench", READ) measured 4,000 px best on a real
20-diagram report with the 1.2026.6 engine: total 5.9 to 10.1 s and toggle 0.50 to 0.54 s against 6.0 to
9.0 s and 0.80 to 0.83 s at 12,000 px, at the cost of 120 fragments instead of 36. The bench itself said
"the cache, not the fragment size, is what makes toggles fast". That real report no longer exists on this
machine (RUN: `C:/Code/work/sidekick-intelligence-e2e/.logs/kronikol/TestRunReport.html` is gone), and the
E2E fixture has no fragment-height parameter (`LargeReportFixture.Generate` takes only
`browserRenderWorkers`; `ReportGenerator.GenerateHtmlReport` does take `browserFragmentMaxHeight`, line
964 at 3.29.3, 940 when first read). The wiki already tells consumers "4,000 to 6,000 renders about 20 % faster on note-heavy
reports at the cost of more fragment seams" in two places, on the old engine's numbers.

### 1.10 Subresource integrity in the browser (RUN)

`tools/render-bench/integrity-probe.js` (results in `results/integrity-probe-2026-09-22.txt`) opened a
page three ways (`file://`, `http://127.0.0.1`, `http://192.168.1.217`, the last not a secure context)
in Playwright's Chromium 147, Firefox 148 and WebKit 26.4 (installed for this: A3 had said "WebKit not
installed"), and asked the platform the questions S2 depends on, against the real CDN and against a
local server that counts requests and can withhold the CORS header:

| Question | Chromium | Firefox | WebKit |
|---|---|---|---|
| `fetch(engine, { integrity: wrong })` on each of the three origins | rejected, `TypeError: Failed to fetch`; console names the computed SHA-256 | rejected, `TypeError: NetworkError when attempting to fetch resource.`; console names the computed hash | rejected, `TypeError: Load failed`; console gives the byte counts |
| the same with the right hash, from cache, 3.9 MB | resolves, 24 to 26 ms | 16 to 21 ms | 26 to 28 ms |
| the same inside a Blob worker (right / wrong), on the LAN origin where `crypto.subtle` is absent | ok / rejected | ok / rejected | ok / rejected |
| a plain `fetch` after the rejected one | served from cache: 0 bytes transferred, one request at the origin | same | same |
| classic `<script integrity crossorigin>` for viz, wrong then right | not executed (`Viz` undefined), then executed | same | same |
| `<script type=module integrity crossorigin>` for the engine, then `import()` of the same URL, then a render | one request; `render` exported; SVG rendered | same | same |
| a wrong hash on that module tag, then `import()` | one request; the import rejects ("Failed to fetch dynamically imported module") | rejects ("error loading dynamically imported module") | rejects ("Cannot load script due to integrity mismatch") |
| `import()` and `fetch` from a server without the CORS header | both rejected | both rejected | `import()` **loads**, `fetch` rejected |
| classic `<script>` without CORS | loads | loads | loads |
| `import()` of a `blob:` URL holding the engine text, from `file://` too | works | works | works |

What this decides. The check belongs to the platform: one option on each `fetch`, two attributes on
each fallback tag, no hashing code in the shim, no dependence on `crypto.subtle`, no fallback digest,
and the same behaviour on a plain-http LAN page as on `file://`. The rejection is not distinguishable
from a network failure by its text, and a plain re-fetch (free, from cache) tells the two apart. The
module-tag-then-import construction makes the fallback path verified as well, which closes the residual
the first draft recorded under §8 Q3. The three origins cover how reports are opened; the E2E suite
runs Chromium only, so Firefox and WebKit are measured here and not guarded.

**Browser support (READ, MDN browser-compat-data 8.1.3, 2026-09-25; first version in Chrome, Firefox and
Safari):**

| Feature | Chrome | Firefox | Safari |
|---|---|---|---|
| `<script integrity>` | 45 | 43 | 11.1 |
| `Request.integrity` (BCD has no key for the init option itself) | 46 | 51 | 10.1 |
| module scripts | 61 | 60 | 10.1 |
| `import()` | 63 | 67 | 11.1 |
| the report's floor, `DecompressionStream` | 80 | 113 | 16.4 |
| an import map's `integrity` section | 127 | 138 | 18 |

Every feature S2 uses predates the floor, so §2's claim is now read rather than assumed. An import map's
`integrity` section would verify an `import()` directly. It arrived well after the floor, which is why S2
uses the module tag instead (§3.2).

### 1.11 The statement limits on the npm build (RUN)

`tools/render-bench/statement-limits-probe.js` runs the five Integration probes of
`NodeJsPlantUmlRendererTests` (the message limit on five prefixes at 2000 and 2001, the whitespace
rule, the block-opener pin and crash, the coloured-bar pin and crash, the three uncapped note forms)
through `plantuml-render.js` itself, one node process per probe as `RenderMany` does for the tests,
against the pinned build and the npm build side by side, and then bisects each edge
(`results/statement-limits-2026-09-22.txt`, 257 processes, 307 ms each):

| | pinned `0e4f452` | npm 1.2026.8 |
|---|---|---|
| the 18 pins | all pass | all pass |
| edge, `loop` / `alt` / `group` / `opt` label | 2011 / 2019 / 2041 / 2034 | 2022 / 2018 / 1980 / 2014 |
| edge, coloured note bar (whole statement) | 2008 | 2005 |
| ceiling, uncoloured bar / note body / one-line note (payload) | 16376 / 16370 / 16377 | 16376 / 16370 / 16377 |

So A2 holds (RUN, not READ): nothing the constants pin moved. Two things the doc comment on
`PlantUmlStatementLimits` says need rewording in S1. The block and bar edges are **V8 stack edges,
not parser limits**: past the edge a `loop` label comes back as a "Syntax Error" SVG and a bar as
`RangeError: Maximum call stack size exceeded`, and with `node --stack-size=4000` a `loop` label of
5,000 characters renders on both builds. They sit near 2,000 on node 25.9 with the default stack where
the comment quotes 3,660 to 5,641 and about 4,124 from 2026-09-04, so the number depends on the runtime,
and the constants (1471, 1400) are what they should be: under the lowest measurement rather than near
any one of them. The note ceilings match the comment's 1.2026.8beta1 figures to the character.

**In the worker (RUN 2026-09-25, after a report from P3's session, its F25 and R30).** The probes above ran
in node. BrowserJs, the default, renders in a Chromium worker, which has a smaller stack.
`tools/render-bench/statement-limits-worker-probe.js` took the same limits through the shipped render
script with one worker, the engine from each CDN route (`results/statement-limits-worker-2026-09-25.txt`).
The results are identical on both builds:
- **The request statement with its `[[#iflow-…]]` link** is drawn at 1,000 characters. From 1,100 up to
  the 2,000 cap, the engine writes "RangeError: Maximum call stack size exceeded" into the diagram's place,
  and the whole diagram is lost. 1,600 is drawn, so the edge is not monotonic, as in node.
- **The same statement without the link** is drawn at every length up to 2,000.
- **Block labels and coloured bars** carrying the same query text are drawn up to 2,000.

So A2 holds in the worker too: the pin moves nothing. But `MaxMessageStatementChars` (2,000) is wrong for
a statement that carries the internal-flow link, which is on by default. A request whose label passes
about 1,000 characters loses its diagram in a default BrowserJs report. The Node renderer draws every one
of these. The defect is older than the pin. On 2026-09-25 the owner made its fix the plan's first step, S0 (§3.0). Firefox's and WebKit's workers
were not measured, and the E2E suite runs Chromium only.

### 1.12 The V8 code cache accepts a same-length source (RUN)

`tools/render-bench/v8-code-cache-probe.js`, node 25.9: cached data produced from one script, handed
to `vm.Script` with a different source of the **same length**, is accepted (`cachedDataRejected` is
false) and the cached code runs (the probe returns the first script's value). A different length is
rejected. V8's sanity check on cached data compares the source length and the flags, not the bytes.
The renderer's comments say the cache is "thrown away and rebuilt when V8 rejects it (a node upgrade, a
changed file)", which is true only of a change that alters the length. Today's exposure is small
(the directory is versioned, and the fork and npm engines differ by 753 bytes), but S3 replaces engine
files on a hash mismatch, and a corrupt file of the right length is exactly the case the check exists
for: the `.v8cache` delete in S3 is load-bearing, and the comments are corrected.

### 1.13 The registry tarball, the package and the CI filter (RUN, READ)

`npm pack @plantuml/core@1.2026.8`: the tarball's `plantuml.js`, `viz-global.js` and `themes.js` hash to
exactly the three values in §1.1 and A.3, so the constants are verified against the registry itself,
not only against jsDelivr's listing. `dist.integrity` is `sha512-md2wGuaI…yw==`; the package is
`"type": "module"` with an `exports` map for the five engine files; it has one maintainer
(`arnaud.roques`); the versions published are `1.2026.5` to `1.2026.8`, and no `1.2026.9` existed on
2026-09-22. READ, `.github/workflows/ci.yml`: the unit-test job filters by test name only, never by
trait, and the hosted runners have `node`, so the Integration-trait probes of S1 and the real-CDN hash
test of S3 run on every CI run, not only locally.

### 1.14 A damaged code cache crashes node (RUN)

**V8 does not check the payload.** On node 25.9, `node --v8-options` lists `--verify-snapshot-checksum` as
off by default ("Enabled by default in debug builds"). So V8 checks a code cache's header (magic,
version, source length, flags, payload length) and never its payload.

**The probe.** `tools/render-bench/v8-code-cache-probe.js damage` ran the shipped `plantuml-render.js` on
the npm engine against damaged copies of its 3.29 MB cache. It ran two series, on two freshly built caches
(`results/v8-code-cache-2026-09-25.txt`, Appendix A.9):

| Damage | Outcome |
|---|---|
| one byte flipped at 10, 25, 50, 75 or 90% of the file | node died in 6 of the 10 flips, with a V8 fatal error (exit `0x80000003`) or an access violation (`0xC0000005`); twice this came after the status line had printed `hit`, so in the middle of the render. The other 4 went unnoticed on that diagram |
| 4 KB scrambled at 25, 50 or 75% | node died, 6 of 6 |
| 64 KB zeroed at 50% | node died with a stack overflow (`0xC00000FD`), 2 of 2 |
| the file truncated to half, or header byte 0, 4, 8, 12, 16 or 20 flipped | `rejected`, rebuilt, the same SVG |
| 64 KB of random bytes appended, or header byte 24 or 28 flipped | `hit`, the same SVG |

**Why a crash is worse than a wrong answer.**
- The crash comes before the script can rewrite the cache, since it rewrites only after a `miss` or a
  `rejected`. So every later render on that machine crashes the same way.
- In every crash measured, it came before any SVG was written. So `RenderMany` throws "batch render failed
  (exit code …)", and every diagram of every report becomes a placeholder until someone deletes the file
  by hand.

**The likely cause is disk or copy damage, not a race.** The concurrent-write race was tried: 40 rounds of
8 cold processes writing at once, and it never produced a torn cache, because each process writes its
3.29 MB in one call. The caches differ in length run to run (3,289,472 to 3,289,976 bytes), so the output
is not deterministic, and a torn cache could not be ruled out by its content either.

**The cost of a check.** Checking the payload costs 1.5 ms (SHA-256 of 3.29 MB in node), against the
compile the cache saves (the script's own header: about 160 ms down to about 1 ms). This is S3 item 6.

### 1.15 The engine's own script loader, and payload text that reaches it (READ, RUN)

**The loader.** Both builds carry one script loader (`EK_` in the minified output; READ of both files).
- It appends `<script src="<name>">` to `document.head` and waits for `onload` or `onerror`.
- The npm build, from upstream #2873, first asks `globalThis.PLANTUML_STDLIB_LOADER(name, ok, fail)` and
  takes that answer unless the hook returns `false`. It also prefixes `globalThis.PLANTUML_STDLIB_BASE`
  when that is a string.
- The fork build has neither global. Without them, the name is document-relative on both builds.
- It asks for four files: `themes.js` (`!theme`), `<lib>.min.js` (`!include <lib/…>`), `emoji.js`
  (`<:name:>`) and `openiconic.js` (`<&name>`).
- Kronikol sets neither global.

Three facts follow.

1. **The integrity check does not reach this loader, and today does not need to.**
   - On the main-thread path, the tag resolves against the report's own address, not the CDN. So it can
     only load a file served beside the report.
   - In the worker host and in the Node script, the mock `head` loads nothing (P3's F9 and F10).
2. **Captured payload text reaches it.** `EscapeCreoleMarkup` puts a `~` before `<` only when a `/`, a `#`
   or a letter follows ([PlantUmlCreator.cs:559](../src/Kronikol/PlantUml/PlantUmlCreator.cs#L559)). So a
   body holding `<&check>` or `<:smile:>` goes into the note as live syntax (RUN on the emitter's own
   output, §1.16). `tools/render-bench/payload-syntax-probe.js` and `worker-wedge-probe.js` measured both
   builds (`results/payload-syntax-2026-09-25.txt`, Appendix A.10):
   - **The Node script.** That diagram times out after 20 s. In `--batch`, **every later diagram of the
     batch times out too**, because the engine never finishes the stalled render.
     - The whole-report run of §1.16 lost 12 of 14 diagrams this way, 5 minutes per build.
     - Two doc comments do not hold for this case: `RenderMany`'s "never affects the others"
       ([NodeJsPlantUmlRenderer.cs:79](../src/Kronikol/PlantUml/NodeJsPlantUmlRenderer.cs#L79)) and
       `RenderNodeBatchIsolated`'s "the per-diagram isolation is kept"
       ([DefaultDiagramsFetcher.cs:368](../src/Kronikol/DefaultDiagramsFetcher.cs#L368)).
     - Each later diagram becomes a placeholder, at 20 s apiece.
   - **The shipped BrowserJs shim**, the default mode, with the engine from the CDN pin. The diagram never
     renders, and its worker takes no other job.
     - With one worker, nothing after it was drawn in the 100 s the probe waited.
     - With four workers, the icon diagram and the emoji diagram each held a worker, and the other seven
       rendered on the two that were left.
     - No failure block and no console line appear anywhere.
   - **`<$sprite>`** does not stall, but the reference is drawn as nothing. **The `~`-escaped forms**
     render as written.
   - **Arrow labels:** a URL in an arrow label reaches the engine percent-encoded (`%3C&x%3E`), so it is
     safe. A hand-written label holding `<&check>` stalls the same way.

   This is P3's hang (its F9 to F11), reached by ordinary captured data, not only by a theme. It is older
   than the pin: the fork build behaves the same in every case.
3. **The npm build has a one-line fail-fast.** A host can answer the hook at once:
   `globalThis.PLANTUML_STDLIB_LOADER = (name, ok, fail) => { fail('Kronikol does not load ' + name); }`.
   That ends the stall:
   - The icon and emoji diagrams fail in about 260 ms, with "java.lang.RuntimeException: Kronikol does not
     load openiconic.js".
   - A theme renders unthemed, with the engine's own warning.
   - The C4 include draws the engine's own picture.
   - A five-diagram batch isolates each failure, in 566 ms.

   The fork build has no hook, so on it the same line changes nothing. The fail-fast turns a stall into a
   placeholder. Only an escape makes such a payload render as the text it is.

**Both fixes are P3's.** Its own third pass, the same day, found the same path independently, and more
(`DIAGRAM_COLOURS_PLAN.md` F16 to F20):
- step, test and assertion text is not escaped either, so LightBDD table steps lose `<$name>` from their
  bars;
- the Node renderer's SVG is not XML: `serializeElement` escapes nothing, where the worker host's
  serializer does. A cross-check here agreed. On the emitter's `rest-0` source, the `&` of a query string
  makes the `data:` image fail to load, which is what a report shows with `InternalFlowTracking` off. On
  `formats-0`, eight text runs carry a raw `<`, and inline, which is the default, the XML body's tags
  become elements.

P3 plans the fail-fast in both hosts, the escapes and the serializer as its first patch (its S3a, S4 and
S5, and its Q9). On 2026-09-25 that patch was numbered 3.29.6, because 3.29.5 went to a restore fix. What this plan adds is fact 3, the npm build's hook, and the notes in §7. These were
sent to the P3 session on 2026-09-25.

### 1.16 Kronikol's own emitter on both builds (RUN)

**The old corpus.** The ladder's corpus (§1.3) predates the current emitter. `tools/render-bench/real/`
holds three diagrams from an older real report and nine synthetic shapes. None of the twelve carries a
header block, a step bar, an internal-flow link, `autonumber` or the teoz pragma (RUN, counted).

**The new corpus.** `tools/render-bench/emitter-corpus/` is a console app named `formatter-probe`, the name
`Kronikol.csproj` grants its internals to. It drove `PlantUmlCreator.GetPlantUmlImageTagsPerTestId`,
`StepBarPlantUml.Build`, and the assertion-note and override shapes to write 14 current-format sources.
Between them they cover:
- REST calls with header blocks
- SQL, Cosmos, Redis, Kafka and blob participants
- step bars in both forms, with a table and a doc string
- passing and failing assertion notes, and a tabular-row band
- Bold and Colored focus, and participant colours
- internal-flow links with setup separation and a collapsed run
- a user action and a failed send
- XML, GraphQL and form bodies
- a 400-line JSON note, and 200 calls

**The result.** `tools/render-bench/emitter-corpus-compare.js` rendered them through the shipped
`plantuml-render.js --batch` on each build (`results/emitter-corpus-2026-09-25.txt`, Appendix A.11).
- **13 of 13 are byte-identical**, and stable run to run.
- Two draw past the stock 8,192 px default, identically on both builds: the 400-line note at 41,573 px
  and the 200 calls at 27,742 px. So the `maxSvgSize` pass-through works on the npm build as it does on
  the fork.
- The fourteenth source is the creole-payload one, which stalls on both builds (§1.15).
- Speed is §1.3's question. These batches took 4.05 s and 3.98 s on a quiet machine, and 10.96 s and
  9.47 s beside another probe. They say only that neither build is slower.

---

## 2. Scope

In: the six slices of §0. Out, with where each goes:

- A consumer option to point reports at another engine host (an air-gapped mirror). New public surface,
  a minor, and it needs its own hash story. Roadmap §5 or a consumer's ask; not stage 1. The constants
  make any mirror that serves the same bytes safe to use, and a mirror is also the only answer to a
  version the CDN withdraws (§1.5).
- `themes.js`: not fetched today, `THEME_PLAN.md` fetches it. Its hash is recorded here (§1.1) so that
  plan can pin it on the same route without re-measuring.
- Flipping the fragment-height default: 12.1, by the `CLAUDE.md` major rule. S5 hands 12.1 the number.
- A browser without integrity on `fetch` or on module scripts. None of the three engines lacks it
  (§1.10). Every feature S2 uses predates the report's `DecompressionStream` floor by years (browser-compat
  data: Chrome 45 to 63 against 80, Firefox 43 to 67 against 113, Safari 10.1 to 11.1 against 16.4). So
  no code path is written for such a browser: a viewer that old fails on `DecompressionStream` first.
- The payload escape gap, the loader stall and the Node serializer (§1.15). These are defects older than
  the pin, present on both builds, and owned by P3, which plans them as its first patch: the fail-fast in both
  hosts, the escapes, and the serializer (its S3a, S4 and S5). §7 lists what this plan hands it.
- Managed hashing for the WASI build (`PLATFORM_FOUNDATIONS_PLAN` 14.2 counts five `SHA256`/`MD5`
  sites). S3 adds a sixth `SHA256.HashData` call, but the Node renderer spawns a process and is not on the
  WASI path, so nothing new is gated. Noted for the 14.2 count.

---

## 3. Design

### 3.0 S0: a long request label keeps its diagram in the worker

**The defect (§1.11).**
- With internal-flow tracking on, which is the default, the request label sits inside
  `[[#iflow-<id> <label>]]` ([PlantUmlCreator.cs:352-366](../src/Kronikol/PlantUml/PlantUmlCreator.cs#L352)).
  Its only cap is the 2,000-character statement budget.
- BrowserJs renders in a Chromium worker. There, the engine overflows its stack parsing a link that long,
  from about 1,100 characters.
- The diagram is lost: its place holds "RangeError: Maximum call stack size exceeded", telemetry counts no
  error, and the note's `[Full path]` block is never seen.
- Node draws the same diagram.
- Both engine builds behave the same. So S0 needs no decision, and ships before the pin.

**The change.**
- `PlantUmlStatementLimits` gains an internal `MaxLinkedLabelChars`: the longest text allowed inside an
  internal-flow link. The class is internal, so this adds no public surface.
- When the link wraps the label, the label budget becomes `Math.Min(labelBudget, MaxLinkedLabelChars)`.
  A statement without the link keeps 2,000.
- The cap counts the label as emitted, after any escaping. P3's 3.30.2 escapes the request label in the
  same method, and each `~` it adds counts toward the cap.
- The full path is not lost. It stays in the note (`AppendFullPathToNote`), as it already does for a label
  cut at 2,000.
- The value comes from S0's red run. `statement-limits-worker-probe.js` bisects the worker edge for at
  least four label shapes: a query string, a path of many short segments, one long token, and non-ASCII
  text.
- The constant sits at 75% or less of the lowest edge found. That is the margin `MaxBlockLabelChars` keeps
  under its node edges. On today's query-string measurement, it comes to about 750.

**Tests, red first.**
- Unit, `PlantUmlStatementLengthTests`: a request with a 2,300-character query, under internal-flow
  tracking, emits link text of at most `MaxLinkedLabelChars`, closes its `]]`, and keeps the full path in
  its note. Without tracking, the statement is still capped at 2,000.
- E2E (Playwright, in the worker, `PollingInterval = 200`):
  `Long_request_label_with_an_internal_flow_link_draws_in_the_worker`. A report built through
  `ReportTestHelper` holds that request, with BrowserJs and tracking on. The diagram's SVG is drawn, and
  its note shows the `[Full path]` block. Red today: the element holds the RangeError text.
- `NodeJsPlantUmlRendererTests` (Integration): the same source still renders in node.
- After the fix, the worker probe is re-run, and every linked row is drawn.

**Docs and release.**
- The `PlantUmlStatementLimits` doc comment records where each limit was measured: node, and the Chromium
  worker.
- The changelog says which diagrams no longer disappear, and that a linked request label is now cut
  shorter, with the full path kept in its note.
- The wiki page that documents label truncation, if there is one, is updated.
- Kronikol4J: check whether the port emits the link; it gets a ledger entry only if it does.

### 3.1 S1: the pin

`PlantUmlJsCdnBase = "https://cdn.jsdelivr.net/npm/@plantuml/core@1.2026.8"`. The doc comment says
what the constant is now: the published package at its release version, a stock ES-module build of
release commit `149874a1` (nine commits past the `0e4f452e` build it replaces, none of them reachable
from a Kronikol diagram, byte-identical output on the corpus, this plan §1.3), `maxSvgSize` passed at every
render site as before, the route immutable by the registry's rule, and the previous fork tags left
published for the reports that carry them. It keeps the two working notes that are still true (every
consumer rewrites the trailing `export`; a host without CORS sends the page to the main-thread fallback).

Tests, red first:

- `TrackingDefaults_PlantUmlJsCdnBase_is_the_npm_route_at_a_release_version` in
  `DependencyCategoriesTests` (beside the existing URL test): the constant matches
  `https://cdn.jsdelivr.net/npm/@plantuml/core@` followed by three dot-separated numbers and nothing
  else. It fails on the fork URL, and it fails on a `beta` or a `-<sha>` suffix, which is the point: a
  future pin cannot go back to a personal build without changing the test.
- The four literal asserts in the two test files move to the npm URL. The NodeJs cache test
  (`The_engine_cache_is_versioned_by_the_cdn_tag`) derives its expectation from the constant and keeps
  passing; add one line asserting the derived segment is `1.2026.8`, so the directory name is on record.
- `NodeJsPlantUmlRendererTests` (Integration trait, needs `node` and the network) download from the new
  route on their next run; nothing to write, but they are the first real render through the new bytes and
  are run in S1's green step.
- The statement-limit probes in the same file (the message-length exact pin at 2000, the crash probes
  at 6000 and 8000, the note ceilings) were already run against the npm build through
  `tools/render-bench/statement-limits-probe.js` (§1.11): all 18 pins pass, and every edge is within a
  few characters of the pinned build's. S1 runs the Integration tests themselves as its green step and
  rewords `PlantUmlStatementLimits.cs` lines 43 to 67: the npm build is the measured one, and the
  block-opener and coloured-bar edges are V8 stack edges that sit near 2,000 on node 25.9 with the
  default stack and near 3,660 to 5,641 on the 2026-09-04 measurement, so the constants (1471 and 1400)
  keep their margin under the lowest figure rather than under a number that moves with the runtime.

`tools/render-bench/bench-report.js` and `variants/worker.js` replace their literal tag with a pattern
that matches any `https://cdn.jsdelivr.net/…/plantuml.js` and `…/viz-global.js` in the report, so the
scripts follow the pin instead of going stale a third time; `README.md` line 20 names the npm route.
The doc comment of `StepBarPlantUmlTests` (line 11) says the two bar forms were "measured on the stock
v1.2026.8beta1-0e4f452 build". It gains "and on npm 1.2026.8, which draws both identically
(`plans/ENGINE_PIN_PLAN.md` §1.16)", so the claim stays true of the build that ships.

Kronikol4J is not touched (its report is a 3.0.43 port with its own pin), but the ledger line in S6
records that the fork tags it and older .NET reports depend on remain published.

### 3.2 S2: the known-hash check in the report page

**Constants.** `TrackingDefaults` gains `internal const string PlantUmlJsIntegrity =
"sha256-rejxXtfyoyJYFDMtOsbQW8fr277IYwUSzHz2eLwMTVI="` and `VizGlobalJsIntegrity =
"sha256-/Gyi3oPdTj/Kln1SFBInd4kKXNGrEBEVofcW/ITm3F4="`, beside the URL they belong to. The SRI form,
base64 of the SHA-256 of the raw file bytes, is what jsDelivr's listing prints, what the registry
tarball's files hash to (§1.13), what the Fetch API's `integrity` option and a `<script integrity>`
attribute take, and what `Convert.ToBase64String(SHA256.HashData(bytes))` produces, so one string
serves the page, the Node cache and a reviewer. Internal, not public: a consumer has nothing to call,
which keeps the release a patch (§8 Q2). `DiagramContextMenu.GetPlantUmlBrowserRenderScript`
substitutes them into the script the way it substitutes the CDN base (`__PLANTUML_ENGINE_INTEGRITY__`,
`__PLANTUML_VIZ_INTEGRITY__`).

**The browser does the hashing.** The first draft of this plan fetched the bytes, hashed them with
`crypto.subtle` and fell back to a SHA-256 written in the shim where `crypto.subtle` is absent (§1.7).
§1.10 measured that none of that is needed: `fetch(url, { integrity })` is enforced by Chromium,
Firefox and WebKit on `file://`, on loopback and on a plain-http LAN origin, on the page and inside a
Blob worker, and a module or classic `<script>` with `integrity` is enforced the same way. The shim
therefore ships no digest code at all; it hands the constant to the platform and reads the outcome.

**The worker path** (`acquireEngine`). `fetchText` passes `{ integrity: <constant> }` for each file.
The promise resolves only after the whole body has been received and checked, so `.text()` on it
cannot see unverified bytes, and everything after it (the ESM tail rewrite, the Blob, the workers) is
unchanged. A rejection is a `TypeError` whose text is the generic network one in all three engines
("Failed to fetch", "NetworkError when attempting to fetch resource.", "Load failed"), so the shim
classifies it with a second, plain `fetch(url)`: the browser serves it from the cache it already filled
(measured: zero bytes transferred, one request at the origin), and if it resolves the bytes exist and
the hash was wrong; if it rejects too, the network is the problem and the existing
`useMainThreadEngine('engine fetch failed: ...')` runs as today.

**A mismatch refuses the file that failed.** A file whose hash differs is never evaluated.

**`plantuml.js`: the engine is refused.**
- The shim calls `engineFailed` with this message: `engine integrity check failed: plantuml.js from <url>
  does not match sha256-<expected>; the browser console names the hash it computed`. (Chromium and
  Firefox print the computed digest, WebKit the byte counts.)
- Every queued and future render writes the existing failure block through `writeFailure` with that
  message, and `console.error` carries it.
- There is no fall back to the script-tag path, because that path would fetch the same bytes and fail
  the same check.
- The one legitimate cause of a mismatch on the immutable route is something between the viewer and
  jsDelivr rewriting the response: a proxy that injects, a captive portal that answers 200 with a login
  page, a corrupted cache entry. In every one of those cases the old behaviour was to evaluate the
  result, usually as a syntax error, followed by a fallback that failed the same way.

**`viz-global.js`: only Graphviz is refused.**
- The worker Blob is built without it, and `console.error` carries the same sentence for that file.
- Sequence diagrams render.
- A diagram that needs Graphviz (the component diagram page) fails in the engine, exactly as it does
  today when viz fails to evaluate in the worker.
- This is the shim's own rule, written beside the Blob it builds: "Graphviz is only used for
  non-sequence diagrams: a failure there must not take the sequence diagrams down with it" (line 221).
  The main-thread path already logs a viz load failure and carries on (`addScript(VIZ_URL).catch(…)`,
  line 267).
- A refused viz file costs far less than a refused report (Q7).

**Telemetry.** `telemetry.engineIntegrity` and `telemetry.vizIntegrity` are each `'verified' |
'mismatch' | null`. Each stays null until its fetch settles, and after a network failure.
`fallbackReason` does not change meaning.

**The fallback path** (`loadEngineScripts`), rewritten so it is verified too. The classic `viz-global.js`
tag gets `integrity` and `crossOrigin = 'anonymous'`. The engine is loaded by a `<script type="module">`
tag with the same two attributes, and then `import(ENGINE_URL)`: the module map keys on the URL, so
the import reuses the record the tag fetched and verified (measured: one request, `render` exported, a
diagram rendered, in all three engines), and a wrong hash on the tag leaves a failed entry that the
import rejects (measured: one request, rejected, in all three). The `import()` is written directly
instead of through `new Function('u', 'return import(u)')`: every browser that has
`DecompressionStream`, the report's hard floor, parses `import()`, and the `Function` construction is
refused by any content security policy without `unsafe-eval`, which is one of the hosted-report cases
the wiki names for this very path. A tag failure is classified the same way as a fetch rejection when
`fetch` exists (a plain fetch of the URL), and is a network failure otherwise.
- **The classic engine tag goes.** Today it only warms the cache.
- **So does the branch after it** (`realLoad`, lines 269 to 291), which drove a classic build through
  `window.plantumlLoad`. A module tag cannot run that protocol, and the pin has been an ES-module build
  since 3.0.76.
- **`window.plantumlLoad = noop` (line 379) stays.** Two unit tests hold it for callers outside the shim:
  `PlantUmlBrowser_report_defines_the_worker_shim_and_a_plantumlLoad_noop`, and `DiagramContextMenuTests`
  line 922. Its comment is reworded, since nothing compares against it any more.
- **A viz tag failure** is classified the same way and sets `vizIntegrity`. It is logged while the engine
  loads, as today.
- **An import map was considered.** An import map with an `integrity` section would verify the
  `import()` without a tag, but it arrived after the report's floor (§1.10), so the tag stays.

`fallbackReason` keeps its existing values
(`BrowserRenderWorkers = 0`, `Worker unavailable`, ...), which the two existing fallback E2Es assert.

**What remains unverified: nothing the shim loads.**
- The residual the first draft recorded (§8 Q3) was the engine `import()` without an integrity
  attribute. The module tag closes it.
- A browser that has `fetch` but ignores `integrity` would run unverified. None of the three engines does
  (§1.10).
- The engine's own loader (§1.15) is outside the check. On the main-thread path it can only load a file
  served beside the report, and in the worker it loads nothing.
- A later plan that points that loader at the CDN (the theme bundle, 6.1) must answer
  `PLANTUML_STDLIB_LOADER` with a fetch that carries the file's integrity constant. It must not set
  `PLANTUML_STDLIB_BASE`, which turns the name into a script tag with no `integrity`.

**Tests, red first.**

- `DiagramContextMenuTests`: the script contains both constants; both engine fetches pass `integrity`;
  the viz tag and the module tag carry `integrity` and `crossOrigin`; the script contains no
  `new Function` and no `realLoad`; the existing `PlantUml_engine_is_fetched_into_workers_not_loaded_by_script_tags` and
  `Esm_render_call_sites_pass_the_max_svg_size_option` keep their asserts.
- E2E `Engine_with_a_wrong_expected_hash_is_refused_and_says_so`: generate the fixture report, replace
  the engine constant in the written HTML with a wrong value (the E2E rule forbids route mocking, not
  editing the local file the test itself wrote), open it, and assert: no worker is created, every rendered
  diagram container holds the failure block naming the file and the expected hash,
  `__kronikolRender.engineIntegrity` is `'mismatch'`, `renders` is 0, and the page is still interactive
  (`plantuml-ready`). The render script sits in the written HTML as plain text (`{{plantUmlBrowserScript}}`,
  [ReportGenerator.cs:1254](../src/Kronikol/Reports/ReportGenerator.cs#L1254)), so the replacement is a
  string edit (checked 2026-09-25).
- E2E `Viz_with_a_wrong_expected_hash_drops_graphviz_only`: the same with the viz constant wronged. Worker
  mode, the fixture's sequence diagrams render, `vizIntegrity` is `'mismatch'` and `engineIntegrity`
  `'verified'`, and the page console carries the viz sentence.
- E2E `Engine_with_a_wrong_expected_hash_is_refused_on_the_main_thread_path`: the same with
  `BrowserRenderWorkers = 0` (the fixture already generates that report for the existing fallback
  test): the module tag fails, the import rejects, the same failure block, `mode` `'main-thread'`,
  `engineIntegrity` `'mismatch'`.
- E2E `Engine_integrity_is_verified_before_the_worker_starts`: the existing fixture, real CDN, asserts
  `engineIntegrity` and `vizIntegrity` are `'verified'`; this is the positive case every other worker test
  already exercises without saying so. No new timing budget: the check adds under 30 ms (§1.10) to a
  fetch plus compile of 316 to 1,296 ms (§1.8), which the existing contention-scaled `ReadyMs` and `AllMs`
  budgets already cover. `engineFetchMs`, which the shim already records (line 210), joins the `Metrics`
  record and the results-file line so a slower fetch shows in the record.
- The fidelity E2E's own main-thread loader (§1.6) moves to the module tag with `integrity` and a direct
  `import()`. It then still loads "exactly as the shim's own main-thread fallback does", and its
  comparison runs the verified path.
- The two literal URL asserts of S1 already prove the script carries the npm route.

### 3.3 S3: the known-hash check in the Node cache

Today `DownloadJsFiles` writes whatever came back and never looks at a file again. Refactor the
download and verification into an `internal sealed class EngineCache` with a constructor the static
renderer calls with its defaults and tests call with a temp directory, a `Func<string, byte[]>`
downloader and the expected hashes. Behaviour:

1. `EnsureFiles()` runs on every `EnsureInitialized`. For each of `viz-global.js` and `plantuml.js`: if
   the file exists and its SHA-256 matches, done. If it exists and does not match (a corrupt or partial
   write, a tampered file), delete it and its `plantuml.js.v8cache` sibling and fall through. If it is
   missing, download.
2. A download is written to a temporary name in the same directory and renamed into place only after its
   hash matched; `File.WriteAllBytes` on the final name is not atomic and a process killed mid-write left
   a truncated engine that every later run trusted.
3. A download whose hash does not match is retried once (the one transient cause worth a retry is a
   proxy answering something else the first time). A second mismatch throws
   `InvalidOperationException`: the URL, the expected hash, the actual hash, the byte count, and the two
   remedies (check for a proxy that rewrites JavaScript; delete `<cache dir>` to force a fresh download).
   That message reaches `kronikol ingest --render nodejs` and the report's placeholder path as any other
   engine-download failure does today.
4. **The code cache is deleted whenever the engine file is replaced**, and that is load-bearing, not
   belt and braces. §1.12 measured that V8's sanity check on cached data compares the source length
   and flags, not its bytes: a replaced engine of the same length is accepted against the stale cache
   and the stale code runs. The comments in `plantuml-render.js` (lines 12 and 242) and the
   `LastCodeCacheStatus` doc comment, which say a changed file is rejected, are reworded to say what is
   true (a changed length is).
5. **Two processes may share the directory** (parallel test assemblies on one machine, the tool and a
   test run, two developers' shells): `EnsureInitialized` locks within its process only, and today two
   downloaders write the same final name with `File.WriteAllBytes`. The temporary name is unique per
   process (`Path.GetRandomFileName()`), the rename is `File.Move(temp, final, overwrite: true)`, and
   an `IOException` from the move (the other process's node has the final file open, or the other
   process won the rename) is answered by hashing the final file: if it matches, the other process's
   copy is accepted and the temporary file deleted; if not, the exception propagates with the same
   message as a persistent mismatch.

6. **The code cache checks itself.**
   - **The write.** `plantuml-render.js` writes `plantuml.js.v8cache` as a 32-byte SHA-256 of the V8 data,
     followed by the data. It writes to a temporary name unique to the process (`<cache>.<pid>.tmp`),
     then renames it into place.
   - **The read.** It hands V8 the data only when the prefix matches. A mismatch is reported as
     `rejected` and rebuilt, like any other rejection.
   - **Why this is not optional (§1.14).** A release node verifies no checksum. A damaged payload crashes
     node before any output, and the crash never rewrites the file. So one bad sector costs every NodeJs
     diagram on that machine, until someone deletes the file by hand.
   - **Cost and upgrade.** The check costs 1.5 ms per process. A cache written by an earlier Kronikol
     version has no prefix, so it is rejected once; the 1.2026.8 directory is new in any case.
   - **The alternative not taken:** deleting the cache and retrying when node exits abnormally. It pays
     one crash per damaged cache, and a crash exit code is not a portable signal (`0x80000003` and
     `0xC0000005` on Windows, signals on Linux).

The Node script's engine handling is unchanged, because it reads what .NET verified. Its comments move
(item 4), and its code-cache handling gains item 6. `LastCodeCacheStatus` keeps its three values, and
`CodeCacheFileName` keeps its name.

**Tests, red first,** all in a new `EngineCacheTests` with a temp directory and no network:

- a fake downloader serving bytes whose hash the test computes: both files land, hashes verified,
  downloader called once per file.
- a pre-existing file with wrong bytes plus a `.v8cache` beside it: after `EnsureFiles` the file holds the
  good bytes and the `.v8cache` is gone.
- a downloader that answers wrong bytes then right bytes: one retry, success, two calls.
- a downloader that always answers wrong bytes: the exception, its message containing the URL, both
  hashes and the cache directory; no file left behind under the final name.
- an existing good file is not re-downloaded (zero downloader calls), so a warm machine pays one hash
  of both files per process: 4 ms warm and 19 ms on a process's first call (RUN, `SHA256.HashData` on
  .NET 10, 2026-09-25).
- two `EngineCache` instances on one directory, one whose downloader is slow: both finish, the final
  file matches, no temporary file remains, and the slow one's mismatching rename is accepted because the
  file in place already verifies.
- a source-content assert that `plantuml-render.js` no longer claims a changed file is rejected.
- `NodeJsPlantUmlRendererTests`, item 6. It needs `node`, no network, and the stub engine the ES-module
  tests already use (line 409).
  - A second run reports `hit`.
  - With one byte flipped in the middle of the cache file, the next run reports `rejected`, renders, and
    leaves a cache that the run after reports as `hit`.
  - No `.tmp` file remains.
  - Red today: the flipped cache is reported `hit`, or crashes node (§1.14).
- `NodeJsPlantUmlRendererTests` (Integration): the real CDN files hash to the constants. This is also the
  guard against a typo in the constants; together with the E2E suite fetching the real CDN it makes a
  wrong constant impossible to ship green.

### 3.4 S4: the perf budget

Two additions to `Large_report_renders_off_the_main_thread_within_budget`, in the same contention-scaled,
three-attempt structure, each with a "sustained, not contention" failure on the third breach:

| Budget | Metric | Measured baseline (§1.8, this machine) | Budget | Catches |
|---|---|---|---|---|
| Per-render, note-heavy | `WorkerMs / Renders` | 146 ms per render at stretch 1 (p50), 235 max | 600 ms times the stretch | a 3x regression of the text-measurement and note paths |
| Arrow-heavy single render | wall time from `window.plantuml.render` to the SVG's insertion for one 200-arrow, 4-participant diagram (the ladder's `gen-200` shape, generated by a new `LargeReportFixture.BuildArrowHeavyDiagram(200)`), rendered once warm in worker mode | 267 ms warm on the ladder in Chromium, to be re-measured through the worker in S4's first step | 1,500 ms times the stretch | a return to the 1.2026.6-teoz class (about 2,200 ms) or 1.2026.7 (about 7,000 ms), the two regressions that actually happened |

Both metrics join the `Metrics` record and the results file line. Each worker `done` message already
carries the render's own `ms` (`plantuml-worker-host.js` line 227), which the shim sums into `workerMs`;
the arrow-heavy figure is taken in the test's own `evaluate` as wall time from the `render` call to the
observer firing, so neither budget needs a product change. The first structural assert already
present (`Renders` above zero, worker mode) is what makes the ratio meaningful. Red proof, as 087be03 did
for the retry path: set a budget to 1 ms locally, watch all three attempts fail with the sustained
message, restore. The fork-versus-npm ladder table (Appendix A) is already filed as
`tools/render-bench/results/ladder-fork-vs-npm-2026-09-22.txt` (every rep in the `.json` beside it), the
per-engine-version snapshot the Teoz plan asks for; S4 only keeps it.

**D17 reads its number here:** the arrow-heavy baseline. A Teoz change that moves a 200-arrow render by
less than the run-to-run noise of that figure (about 10% between reps on a quiet machine) is craft.

### 3.5 S5: the fragment-height re-measure

`LargeReportFixture.Generate` gains `browserFragmentMaxHeight` (threaded to the parameter
`GenerateHtmlReport` already takes). A measurement test, `Trait("Category", "Bench")` and excluded from
the default run, renders the fixture at 4,000, 6,000, 8,000 and 12,000 px, three runs each, and records
`AllMs`, `ToggleMs`, `Fragments`, per-render ms, all divided by the stretch, at two fixture sizes: the
default (6 by 40) and 20 by 80, the closest stand-in for the gone 20-diagram report. The table goes into
§10 and into the roadmap's 12.1 row.

What the table is for, and what it is not: the number that decides a default at 12.1 is the toggle and
total-render time against the seam count, since every fragment repeats the participant boxes (about 60 px
and the autonumber continuity per seam) and every seam is one more place the parked page-shift-on-toggle
bug of 3.0.67 can show. No default moves in this release; the two wiki rows that quote "4,000 to 6,000
about 20 % faster" are rewritten with the measured figures on the current engine, so a consumer who sets
the option today sets it on a true number.

### 3.6 S6: docs, ledger, release

- `CHANGELOG.md`, patch, in the house form: the part that moved and why (nothing new for a consumer to
  call: a constant's value, two internal constants, an integrity check, a test budget), a paragraph per
  slice, the measured identity and the 4%, and the sentence that the fork tags stay published. It also
  names the behaviour a consumer can see:
  - a report whose engine fails its check says so instead of evaluating it;
  - a viz mismatch costs the Graphviz-laid diagrams only;
  - a damaged code cache is rebuilt instead of crashing node;
  - a mirror that rewrites the engine URL must serve the npm package's bytes.
- Wiki: `PlantUML-Browser-Rendering` (the "two JavaScript files" bullet names the npm package and the
  release commit; a new "Engine integrity" paragraph under How Rendering Runs: what is hashed, when, what
  a mismatch looks like, the `engineIntegrity` and `vizIntegrity` telemetry fields, a viz mismatch
  costing Graphviz only, the fallback verified through the tag attributes, the CORS sentence corrected
  for the ES-module build (§1.5); line 68's account of the fallback detecting the missing `plantumlLoad`,
  which S2 replaces; the NodeJs section's cache bullet gains the verification, the atomic write and the
  checksummed code cache; the fragment-height figures); `Report-Configuration`
  rows 129 and 136; `Large-Response-and-Diagram-Handling` row 11; `Diagnostics-and-Debugging` line 541 adds
  the integrity failure to the list of reasons and the two telemetry fields. Links in the `[[label|Target]]`
  form the 2026-09-22 sweep established.
- Kronikol4J `README.md`, the ledger: one paragraph extending the standing
  `plantuml-browser-render-script.js` divergence (the URL on the npm route, the integrity constants and
  check), stating that the port's `v1.2026.3beta6-patched` pin is unaffected and that tag stays
  published. Committed and pushed there separately.
- `PLANS_STATUS.md`: this plan's row; `TEOZ_PERF_PLAN.md` row (the pin move done, the budget added);
  `THEME_PLAN.md` row (the pin it waited on has moved); `STAGE_1_PLAN.md` row. `ROADMAP.md`: 1.8 done,
  D6 taken, the 12.1 row carries S5's number, D17 its baseline.
- Version: `Directory.Build.props`, `.claude-plugin/plugin.json`, `.claude-plugin/marketplace.json`
  (`PluginManifestTests` holds the last two equal to the first). Tag `v<version>`, push commit and tag,
  then check the CI run of the pushed SHA (the E2E shards fetch the engine from the new route for the
  first time on CI there).

---

## 4. Order of work and the red-green cycle

0. **S0**, its own patch.
   - Red: the unit test and the worker E2E fail on today's emitter.
   - Bisect the worker edge per label shape, and set the constant. The table goes into §10.
   - Green: the capped linked label.
   - Then the doc comment, the changelog, the version, the tag, the push, and the CI check of the pushed
     SHA.
   - It needs no decision, and can run before D6 is answered.
1. **S1.** Red: the route-shape test and the four moved asserts fail on the fork URL. Green: the
   constant. Then the Integration renders and the statement probes on the new bytes; the doc comments;
   the bench scripts. Commit.
2. **S3.**
   - Red: `EngineCacheTests`, six cases, against a class that does not exist; and the code-cache test
     against today's script, which reports `hit` on a flipped byte.
   - Green: `EngineCache`, with the static renderer delegating to it; and the checksummed, atomically
     written cache in `plantuml-render.js`.
   - Refactor: `DownloadJsFiles` and `DownloadIfMissing` go.
   - The Integration hash test last. Commit.
3. **S2.**
   - Red: the script-content tests, then the wrong-hash E2Es:
     - the engine on the worker path (the page today evaluates the engine and renders, so the test fails
       on "renders is 0");
     - the engine on the main-thread path;
     - viz (today there is no `vizIntegrity`).
   - Green: constants, substitution, the `integrity` option on both fetches, the classification fetch,
     the refusal for the engine and the Graphviz-only refusal for viz, the two telemetry fields, the two
     tag attributes, the direct `import()`, and the classic branch removed.
   - Then the positive E2E and the fidelity E2E's loader. Commit.
4. **S4.** The arrow-heavy diagram in the fixture and its measurement first (numbers into §10), then the
   two budgets, the 1 ms red proof, the results snapshot. Commit.
5. **S5.** The fixture parameter, the bench test, the table into §10 and the wiki rows. Commit.
6. **S6.** Docs, ledger, index rows, version, tag, push, CI check.

The full unit suite and the E2E `BrowserRenderWorkerTests`, `DiagramNote*`, `DatabaseToggleTests` and
`NoteAppearanceTests` collections run green before S6; the E2E suite is sharded on CI and never runs
whole locally, so the collections that read painted geometry are the ones to run by name.

**Start point.** Main at 3.29.4 and earlier no longer restores (NU1605: Azure.Messaging.ServiceBus 7.21.0
needs Microsoft.Extensions.DependencyInjection.Abstractions 10.0.10 against Kronikol.Extensions.ServiceBus's
10.0.9 pin). 3.29.5 (`bb262d37`) raised the pin, so the work starts from `bb262d37` or later. P3's first release then landed as 3.29.6 (`ad289f55`). It changed `plantuml-render.js`,
`plantuml-worker-host.js`, `PlantUmlCreator.cs` and `StepBarPlantUml.cs`. 3.30.0 and 3.30.1 followed the same day, and
3.30.1 touched `PlantUmlCreator.cs`, which S0 edits, and the render script's `splitWithChunkedNotes`. So
before S0, re-run the header's re-check against the current HEAD, re-read every line number this plan
cites, and re-run §1.16 on the new serializer.

**If P3 or P5 lands first.** Both edit the render script, and P3 also edits the two mock DOMs.
- What they touch: P3 the link-hover restore and the mock `head`; P5 `bindIflowLinks`.
- What this plan touches: S2 `fetchText`, `acquireEngine`, the fallback loader, `addScript` and the
  telemetry block; S3 the code-cache function of `plantuml-render.js`.
- So the overlap is textual at most. The header's re-check (`git diff --stat 2843018a HEAD -- <cited
  files>`) is re-run before S1.
- If P3's fail-fast is on the mock DOMs by then, §1.15's stall is already gone, and
  `payload-syntax-probe.js` will show it.
- P3's S5 changes the Node renderer's SVG bytes wherever text holds `&`, `<`, `>` or a quote. The change
  is the same on both builds, because the serializer is Kronikol's, not the engine's. Even so, §1.16's
  comparison is re-run on the new serializer before S1.

---

## 5. Acceptance

- (S0) A request whose label is 2,300 characters draws in the worker with internal-flow tracking on, and
  its note carries the full path.
- `TrackingDefaults.PlantUmlJsCdnBase` is the npm route at a release version, held by a test.
- A report opened from `file://`, from `http://localhost` and from a LAN address renders in worker mode
  with `engineIntegrity` and `vizIntegrity` `verified` (the probe measured the three origins in Chromium,
  Firefox and WebKit; the E2E suite opens files in Chromium).
- A report whose expected engine hash is wrong renders nothing, says why in every diagram's place and in
  the console, and stays interactive, on the worker path and on the main-thread path alike.
- A report whose `viz-global.js` fails its check still draws its sequence diagrams, and says in the
  console why Graphviz is missing.
- The Node renderer heals a corrupt cache, refuses a persistent mismatch with the documented message,
  and never leaves a partial engine file under the final name. A damaged code cache is rejected and
  rebuilt, and node never crashes on one.
- `bench-ladder.js` on the corpus with the pinned engine and the npm engine: every SVG `same`, geomean
  at or below 1.05 (the table in Appendix A is the reference).
- The E2E perf guard carries the two budgets and passes on three consecutive local runs and on the CI run
  of the pushed SHA.
- The wiki, the changelog and the Kronikol4J ledger say what shipped; no page still calls the engine
  source "a fork repository holding tagged engine builds".

---

## 6. Release

**Two patches.** S0 ships first, and alone. It fixes lost diagrams, needs no decision, and adds only an
internal constant, so it is a patch with its own changelog entry. That entry calls out that a linked
request label is now cut shorter, with the full path kept in its note. S1 to S6 then ship as one patch.

For S1 to S6: by the `CLAUDE.md` rule nothing here is new for a consumer to call: a public constant's
value changes, two internal constants appear, a check runs before something that already ran, a test
budget is added, a fixture gains a parameter. The report's script bytes change, which is a report-output
change and takes the ledger entry, not a bump. If the owner prefers the hash constants public (§8 Q2),
the release becomes a minor and the changelog says so.

A `public const` is inlined by the C# compiler into any consumer assembly that reads it. A consumer that
read `TrackingDefaults.PlantUmlJsCdnBase` into its own code keeps the fork URL until it recompiles.
Nothing in Kronikol's own packages reads it across an assembly boundary (READ: the three readers are in
`Kronikol`), and the fork stays published, so the worst case is a stale but working URL. Recorded in the
changelog.

**A rollback** is the URL and the two integrity constants. The fork build's constants are recorded in A.3
beside the npm ones, so reverting needs no re-measure. The code-cache format change (item 6) needs no
revert of its own, for two reasons:
- a rolled-back version reads its own engine directory;
- a prefixed file handed to a node without the check fails V8's header check and is rebuilt (A.9: a
  flipped first header byte is rejected).

---

## 7. What this plan hands to others

| To | What |
|---|---|
| `THEME_PLAN.md` | the upstream gate was lifted on 2026-09-22 and the pin it waited on has moved; the `themes.js` hash on the same route (§1.1), verified against the registry tarball (§1.13). Load `themes.js` through `PLANTUML_STDLIB_LOADER` with a fetch that carries that hash, never through `PLANTUML_STDLIB_BASE`, which becomes a script tag with no `integrity` (§1.15, §3.2) |
| `DIAGRAM_COLOURS_PLAN.md` (P3) | P3 owns the loader stall, the escapes and the Node serializer (its F16 to F20, and its S3a, S4 and S5 as its first patch). This plan hands it four things, sent to the P3 session on 2026-09-25. (1) Once this pin lands, the npm build's `PLANTUML_STDLIB_LOADER` answered at once is an engine-level fail-fast: one line per host, measured in the Node script (§1.15). P3's mock-DOM `_onAppend` works on both builds, so it stays right if P3 lands first. (2) The existing `Creole_markup_in_a_captured_body_reaches_the_svg_as_text` ([NodeJsPlantUmlRendererTests.cs:197](../tests/Kronikol.Tests/PlantUml/NodeJsPlantUmlRendererTests.cs#L197)) reads `<b>raw</b>` raw out of a `<text>` element (line 228). It pins the unescaped serialization, and turns red under P3's S5 unless its extraction decodes entities. (3) This plan's S3 edits `loadEngineWithCodeCache` in the same file, which is disjoint from P3's mock `head` and `serializeElement`. (4) The one-worker and four-worker stall numbers |
| The wiki, in S6 | the fallback paragraph's CORS sentence, true of the classic builds only (§1.5), and the "fork repository" wording |
| `ROADMAP.md` 12.1 | the fragment-height table (S5) and the seam count per setting |
| D17 | the arrow-heavy baseline (S4) |
| `PLATFORM_FOUNDATIONS_PLAN` 14.2 | one more managed-hash site, off the WASI path |
| Roadmap §5 / a consumer ask | the CDN-override option, with the hash constants as the thing it must also override |
| `TEOZ_PERF_PLAN.md` | the pin move and the budget it promised are done; W0.4 and the speedscope export stay optional |

---

## 8. Open questions

**D6 (the owner's).** Move the pin to npm 1.2026.8 now, keep `viz-global.js`. The measurements in
§1.3 and §1.4 remove the two risks the roadmap left open: output does not change, and the viz file is the
same program; §1.11 adds that the statement edges are the same on both builds, and §1.16 that Kronikol's
own emitter output is identical too. Waiting for 1.2026.9 buys a second re-pin (trailing-zero trimming)
and no date: it was not published on 2026-09-22 (§1.13), nor on 2026-09-25 (RUN, `npm view`). The line
is in use (7 public dependents, 4,166 downloads a week), and under npm's written policy its owner cannot
unpublish 1.2026.8 (§1.5).

| # | Question | Recommendation |
|---|---|---|
| Q1 | On a hash mismatch, refuse the engine or warn and run it? | Refuse. The check exists because the report ran whatever came back; a warning keeps that. The message names both hashes and the remedies |
| Q2 | Hash constants `internal` (patch) or public (minor)? | Internal. Nothing outside the assembly needs them until an override option exists, and that option is its own minor |
| Q3 | Closed by measurement (§1.10): a module tag with `integrity` followed by `import()` verifies the fallback's engine in all three engines, so no path is unverified | nothing to decide |
| Q4 | S5's second fixture size | 20 by 80 as the stand-in for the gone real report; if a real report of that size is at hand at execution time, run `bench-report.js worker` with `MAXH` on it too and record both |
| Q5 | Record the `themes.js` hash as a constant now, unused, or leave it to `THEME_PLAN`? | Leave the constant to the theme plan; the value is in §1.1 |
| Q6 | Should the refusal message compute the actual hash with `crypto.subtle` where it exists? | No. The console already prints it in Chromium and Firefox, and the point of the design is a shim with no digest code |
| Q7 | On a `viz-global.js` mismatch, refuse every diagram or drop Graphviz only? | Drop Graphviz only (§3.2). It is the shim's own rule for a viz failure (line 221). Sequence diagrams need nothing from the file, and refusing them would turn one bad Graphviz download into a blank report. Refusing everything is the stricter reading of "refuse on mismatch" and costs nothing to build, so it is the owner's call |
| Q9 | Closed on 2026-09-25: the owner made the fix the plan's first step, S0 (§3.0) | nothing to decide |
| Q8 | Closed on 2026-09-25: the payload escape gap, the loader stall and the Node serializer (§1.15) belong to P3, whose third pass plans them as its first patch (its S3a, S4, S5 and Q9) | nothing to decide here; §7 lists what this plan hands P3 |

---

## 9. Assumption ledger

| # | Statement | Basis | Effect if wrong |
|---|---|---|---|
| A1 | npm 1.2026.8 renders every Kronikol diagram identically to the pinned build | RUN on 12 sequence shapes, a class, a JSON and a component diagram, and on 13 sources from Kronikol's own emitter through the Node renderer (§1.16); READ of the 9-commit range | A diagram kind outside the corpus (timing, state, activity) is not emitted by Kronikol today. If one differs, it is a report-output change in the ledger and still a patch |
| A2 | The parse and stack-overflow limits are unchanged | RUN (§1.11): all 18 pins pass on the npm build and every edge is within a few characters of the pinned build's; in the Chromium worker the two builds give identical results (2026-09-25) | the constants keep their margin either way; S1 re-runs the Integration tests as its green step |
| A3 | The browser enforces `integrity` on `fetch` and on classic and module script tags, on every origin, secure context or not | RUN in Chromium 147, Firefox 148 and WebKit 26.4 on `file://`, loopback and a LAN http page (§1.10) | a browser that ignored the option would run unverified and report `verified`; none of the three does, and the feature predates every floor the wiki lists |
| A4 | jsDelivr serves the raw package file, uncompressed at the application layer, for `plantuml.js` | RUN: downloaded bytes hash to the listing's hash; `.min.js` is a different URL | A CDN that rewrote the file would fail the check on every viewer, which is the loud failure the check is for; a rollback is a one-line constant change |
| A5 | The E2E suite reaching jsDelivr on CI is unchanged in kind by the move | READ: it fetches the fork today | Same host, same headers; a first CI run on the new route is checked before the tag is called done |
| A6 | The fixture's note-heavy per-render time is dominated by text measurement | RUN: 153 to 146 ms across an engine that was 4 to 8 times faster on arrows | Hence the arrow-heavy second budget; if S4's worker measurement of `gen-200` lands far from 267 ms, the budget is set from the measured figure with the same 5x headroom, not from the ladder |
| A7 | The stretch cap of 5 bounds the budgets on a saturated CI runner | READ: `ContentionScale`, 38 rows with p90 at the cap | A wedged runner fails the third attempt with the sustained message, which is the existing contract |
| A8 | No consumer reads `PlantUmlJsCdnBase` across an assembly boundary | READ: three readers, all in `Kronikol` | A stale inlined URL still works (§6) |
| A9 | V8 accepts a code cache against a changed source of the same length | RUN on node 25.9 (§1.12) | the `.v8cache` delete in S3 covers it; a V8 that checked the bytes would make the delete redundant, not wrong |
| A10 | A module tag and a later `import()` of the same URL share one module record, and a failed tag poisons it | RUN in the three engines (§1.10) | a second request, or worse an unverified second evaluation; the main-thread wrong-hash E2E would catch it in Chromium |
| A11 | A response that failed the integrity check is still cached, so the classifying fetch is free | RUN (§1.10) | a second 3.9 MB download, on the failure path only |
| A12 | The owner cannot unpublish `@plantuml/core@1.2026.8`, and jsDelivr keeps serving what it has served | READ, 2026-09-25: npm's policy, deps.dev's 7 dependents, 4,166 downloads a week, jsDelivr's README (§1.5) | npm support or jsDelivr can still withdraw a version, and jsDelivr did in July 2026. Every report that references it would stop rendering, just as a deleted fork tag would stop them. The remedy is a release with a new constant, and the structural answer is §2's mirror option |
| A13 | A release node checks no checksum on a V8 code cache | RUN on node 25.9 (§1.14) | a node that did would reject what item 6 rejects: the checksum is then redundant, not wrong |
| A14 | The engine's own loader resolves against the document unless `PLANTUML_STDLIB_BASE` is set, and Kronikol sets no `PLANTUML_*` global | READ of both builds (§1.15; P3's R19 agrees) | a loader that resolved against the CDN would load unverified code on the main-thread path, and §3.2's "nothing the shim loads" would need the hook |

---

## 10. Execution log

Empty. Filled slice by slice when the work runs:
- S0's worker bisection, per label shape, and the cap it chose;
- the Integration-test run of the statement probes (S1; §1.11 already holds the harness numbers);
- the code-cache test's red run (S3);
- the viz E2E's red run (S2);
- the worker measurement of the arrow-heavy diagram (S4);
- the fragment-height table (S5);
- §1.16's comparison re-run on P3's serializer, if S5 of that plan has landed;
- any departure from §3.

---

## Appendix A: measured tables (2026-09-22)

**A.1 Ladder, sequence corpus** (`bench-ladder.js`, Chromium 147.0.7727.15, REPS 5 warm, cold discarded,
engines interleaved, `maxSvgSize` 98304, teoz pragma injected; e0 = `core-1.2026.8beta1-0e4f452.js`,
e1 = `core-npm-1.2026.8.js`)

| file | e0 ms | e1 ms | e1/e0 | e1 svg | measureText |
|---|---|---|---|---|---|
| gen-50 | 88 | 86 | 0.98 | same | 245/245 |
| gen-200 | 267 | 268 | 1.00 | same | 955/955 |
| gen-500 | 641 | 633 | 0.99 | same | 2375/2375 |
| puml-0 | 85 | 80 | 0.94 | same | 292/292 |
| puml-1 | 57 | 55 | 0.97 | same | 198/198 |
| puml-19 | 106 | 104 | 0.98 | same | 387/387 |
| shape-activation | 152 | 150 | 0.99 | same | 426/426 |
| shape-createdestroy | 102 | 103 | 1.01 | same | 242/242 |
| shape-groups | 73 | 72 | 0.98 | same | 258/258 |
| shape-parallel | 141 | 128 | 0.91 | same | 328/328 |
| shape-self | 146 | 139 | 0.95 | same | 204/204 |
| shape-wide | 135 | 114 | 0.84 | same | 236/236 |

e1 geomean ratio vs e0: **0.961**, all svg same: **true**.

**A.2 Ladder, other families** (REPS 2, no pragma)

| file | e0 ms | e1 ms | e1/e0 | e1 svg |
|---|---|---|---|---|
| probe-class | 20 | 21 | 1.02 | same |
| probe-json | 14 | 14 | 1.03 | same |
| component (Kronikol plain-shape form) | 36 | 35 | 0.97 | same |

geomean 1.006, all svg same.

**A.3 The npm package on jsDelivr** (`data.jsdelivr.com/v1/packages/npm/@plantuml/core@1.2026.8`)

| file | bytes | SHA-256 (base64, as listed) |
|---|---|---|
| plantuml.js | 3,947,570 | `rejxXtfyoyJYFDMtOsbQW8fr277IYwUSzHz2eLwMTVI=` |
| viz-global.js | 1,445,436 | `/Gyi3oPdTj/Kln1SFBInd4kKXNGrEBEVofcW/ITm3F4=` |
| themes.js | 326,396 | `1vUIcEsna2ZjHP/wUpHeMBOreAeXK3yyt3oU1L6rLC0=` |
| emoji.js | 1,876,355 | `47l6EDtIuwCm13zwZn86D9oKN5nPvF4iSU/N4CShjkk=` |
| openiconic.js | 51,251 | `s0ptfAMVrUwdeAvP6S57+omrvU+iCuQZ40CfHMT1bFI=` |

Headers on `…/npm/@plantuml/core@1.2026.8/plantuml.js`: `HTTP/1.1 200`, `Content-Length: 3947570`,
`Content-Type: application/javascript; charset=utf-8`, `Cache-Control: public, max-age=31536000,
s-maxage=31536000, immutable`, `Access-Control-Allow-Origin: *`, `X-JSD-Version: 1.2026.8`,
`X-JSD-Version-Type: version`, `X-Content-Type-Options: nosniff`. The fork route answers with the same
cache and version-type headers for `v1.2026.8beta1-0e4f452`.

RUN, `npm pack @plantuml/core@1.2026.8` (registry tarball `core-1.2026.8.tgz`, `dist.integrity`
`sha512-md2wGuaIJnAq1yKkedSpQE68cLsvZvctwzWHkDAnC3DTVLe/MSd/2vr4ETwr6jtc1av8cdxCohFuW4VGejMHyw==`):
the extracted `plantuml.js` (3,947,570 B), `viz-global.js` (1,445,436 B) and `themes.js` (326,396 B) hash
to the three values above. Maintainers: `arnaud.roques` only. Versions: `1.2026.5`, `1.2026.6`,
`1.2026.7`, `1.2026.8`; still no `1.2026.9` on 2026-09-25. License `MIT`. The registry signs the tarball
(`dist.signatures`); the package carries no provenance attestation.

**The fork build's constants, for a rollback** (RUN 2026-09-25, `openssl dgst -sha256 -binary | openssl
base64` on the jsDelivr files, equal to jsDelivr's listing of the tag):

| file | bytes | SRI value |
|---|---|---|
| `plantuml.js` | 3,946,817 | `sha256-7u6KYG1CVjFrgWuP6/sGcOsSGMh8zp5OO/0gzn+hgGQ=` |
| `viz-global.js` | 1,445,427 | `sha256-7yzYoItc+LZeNjQTEFK0GHD/MLtvsj4jqH/QnURmbLo=` |

The npm values were re-derived the same way, and in .NET as S3 will compute them
(`Convert.ToBase64String(SHA256.HashData(bytes))` gives `rejxXtfy…TVI=` for `plantuml.js`).

**A.4 The commit range** (`gh api repos/plantuml/plantuml/compare/0e4f452e...149874a1`): `cd67d0d7`
move perf-bench and browser-test under tools/ (#2868); `834f59f5` sonar action and `GPL_ONLY` (#2871);
`d6890f0d` perf-bench expected bands (#2862); `24b4fb18` Javadoc `@param` (#2850); `dea33744` Javadoc
warning; `d8579252` setup-node 6 to 7; `2db0db82` stdlib includes for every host (#2873); `1f356488`
mindmap and wbs non-regression tests; `149874a1` version 1.2026.8.

**A.5 The secure-context probe:** §1.7; the script is `tools/render-bench/secure-context-probe.js`
(run from that directory with the E2E project built, so its Playwright browsers exist); the probe's plain-JS digest of `abc` is `ba7816bf…`, and its digest of the
npm engine equals the listing's on every origin.

**A.6 The integrity probe:** §1.10; `tools/render-bench/integrity-probe.js`, results with the raw JSON
per browser context in `tools/render-bench/results/integrity-probe-2026-09-22.txt`. Playwright 1.59.1
builds: Chromium 147.0.7727.15, Firefox 148.0.2, WebKit 26.4 (installed with
`node <playwright package>/cli.js install webkit`).

**A.7 The statement limits:** §1.11; `tools/render-bench/statement-limits-probe.js`, results in
`tools/render-bench/results/statement-limits-2026-09-22.{txt,json}` (the `.txt` also records the failure
modes past the edges and the `--stack-size=4000` runs).

**A.8 The V8 code cache:** §1.12; `tools/render-bench/v8-code-cache-probe.js`, node 25.9.0: same length,
different source, `cachedDataRejected = false` and the cached code runs; different length, rejected.

**A.9 A damaged code cache** (2026-09-25): §1.14.
- Command: `node v8-code-cache-probe.js damage <npm dir>` and `race <npm dir> 20 8`.
- node 25.9.0, where `--verify-snapshot-checksum` defaults to `--no-verify-snapshot-checksum`.
- Two damage series on two freshly built caches (3,289,712 bytes each time), and two race series of 20
  rounds each.
- Every row is in `tools/render-bench/results/v8-code-cache-2026-09-25.txt`.
- Exit codes: `2147483651` is `0x80000003` (V8 fatal error), `3221225477` is `0xC0000005` (access
  violation), `3221225725` is `0xC00000FD` (stack overflow).
- Race: 0 of 40 rounds left a cache that crashed the next run. 20 of 20 ended in `hit` in each series.

**A.10 The engine's loader from payload text** (2026-09-25): §1.15.
`node payload-syntax-probe.js npm=<dir> fork=<dir>`, then again with `--loader-hook`
(`results/payload-syntax-2026-09-25.txt`). Node script, one process per case:

| case | fork build, shipped script | npm build, shipped script | npm build, hook failing at once |
|---|---|---|---|
| plain note | SVG, 494 ms | SVG, 291 ms | SVG, 361 ms |
| `<&check>` in a note | timeout, 25.1 s | timeout, 23.5 s | "does not load openiconic.js", 260 ms |
| `<:smile:>` in a note | timeout, 24.8 s | timeout, 23.4 s | "does not load emoji.js", 258 ms |
| `<$sprite>` in a note | SVG, the reference drawn as nothing | the same | the same |
| `~<&check>`, `~<:smile:>` | SVG, drawn as written | the same | the same |
| `<&check>` in an arrow label | timeout | timeout | fails, 242 ms |
| `!theme cerulean` | timeout | timeout | SVG, unthemed, the engine's warning |
| `!include <C4/C4_Context>` | timeout | timeout | SVG, 1,331 bytes |
| `--batch` of plain, icon, plain, emoji, plain | 1 SVG, 4 timeouts, 95.6 s | 1 SVG, 4 timeouts, 101.1 s | 3 SVGs, 2 failures, 566 ms |

The fork build with the hook behaves as the shipped script does on it, because it has no hook.

The shipped BrowserJs shim (`worker-wedge-probe.js`, fork build from the CDN pin, file://, Chromium 147,
nine diagrams, 100 s):

| workers | drawn | telemetry |
|---|---|---|
| 1 | the first diagram only; nothing after the icon diagram | `renders` 1, `errors` 0, `inFlight` 1 |
| 4 | 7 of 9; the icon and the emoji diagram each held a worker | `renders` 7, `errors` 0, `inFlight` 2 |

**A.11 Kronikol's own emitter on both builds** (2026-09-25): §1.16.
- Command: `node emitter-corpus-compare.js <corpus> fork=<dir> npm=<dir>`.
- The sources come from `emitter-corpus/` (`results/emitter-corpus-2026-09-25.txt`).
- The SVG SHA-256 is truncated to 12 hex characters, and the pair shown is equal on both builds.

| source | SVG | bytes | viewBox |
|---|---|---|---|
| arrows-200 | `f65fbc8dd3f6` | 706,148 | 0 0 534 27742 |
| data | `08a8c1138b94` | 41,959 | 0 0 1036 1249 |
| data-participant-colors | `886a8064d7cc` | 41,959 | 0 0 1036 1249 |
| flow (internal flow, setup, collapsed run) | `182d5c15e22f` | 18,277 | 0 0 949 725 |
| flow-plain | `c0c1df6d4837` | 28,454 | 0 0 758 1130 |
| focus-bold | `756948b1263d` | 19,336 | 0 0 538 709 |
| focus-colored | `ef5eb4778656` | 18,880 | 0 0 538 709 |
| formats (XML, GraphQL, form) | `e023828e133d` | 22,644 | 0 0 1558 924 |
| large (400-line note) | `5e7e809e20f9` | 1,594,087 | 0 0 1768 41573 |
| rest (header blocks) | `e0a31754fe33` | 72,448 | 0 0 1238 2363 |
| rest-noheaders | `206d4dd67916` | 60,652 | 0 0 908 1886 |
| rest-server-encoded | `e0a31754fe33` | 72,448 | 0 0 1238 2363 |
| steps (both bar forms, assertion notes, row band) | `c0d35337eda7` | 47,474 | 0 0 665 1477 |

**A.12 Sources for the 2026-09-25 re-check**, read that day:

- **npm.**
  - The unpublish policy (<https://docs.npmjs.com/policies/unpublish/>): "Regardless of how long ago a
    package was published, you can unpublish a package that meets all of the following conditions: no
    other packages in the npm Public Registry depend on it / it had less than 300 downloads over the last
    week / it has a single owner/maintainer", and "Once package@version has been used, you can never use
    it again."
  - deps.dev v3alpha: `dependentCount` 7. The seven are named by deps.dev and checked in each manifest.
  - `api.npmjs.org`: 4,166 downloads from 2026-09-15 to 2026-09-21.
- **jsDelivr's README** (<https://github.com/jsdelivr/jsdelivr>), three passages:
  - "When a file is first accessed, it gets permanently stored in a reliable file system. This means that
    even if a npm package gets deleted or an existing file gets removed by a developer, jsDelivr will
    continue to serve the stored copy forever"
  - "We use a permanent S3 storage to ensure all files remain available even if GitHub goes down or a
    repository or a release is deleted by its author."
  - "once we download your tagged files, there is no way for you to update them."
- **jsDelivr withdrawing versions.** jsdelivr/jsdelivr#18727 (2026-07-16) reported eight removed
  malicious versions still being served. It was closed the next day, and all eight answer 404. The terms,
  §10: "We may withdraw or restrict the availability of all or any part of jsDelivr CDN for business and
  operational reasons".
- **jsDelivr on SRI** (<https://www.jsdelivr.com/using-sri-with-dynamic-files>): "Only use SRI with full
  single-file links, and static versions." The package's default file on jsDelivr is `/plantuml.min.js`,
  which jsDelivr generates.
- **Browser compatibility**, MDN browser-compat-data 8.1.3. The keys read were
  `html.elements.script.integrity`, `api.Request.integrity`, `html.elements.script.type.module`,
  `javascript.operators.import`, `api.DecompressionStream` and
  `html.elements.script.type.importmap.integrity`.
