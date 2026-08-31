# SEARCH_INDEX_PLAN — Full-text "search everything" index

Status: **EXECUTED IN FULL — 3.0.70, 2026-08-31** (green-lit and implemented same day, §15 order
followed; recorded in PLANS_STATUS.md). Measurements M1-M3 executed and recorded in
`tools/search-bench/README.md` (Q-A locked: B=65536). Notable execution deltas from the plan as
written: the honest client cold worst case is ~2-3.5s on a 100MB-class monster (the §4.4 0.5-0.8s
estimate omitted client-side normalization; the §7 batched progressive reveal covers it, real
Chrome fixture measurement 70ms/multi-MB); hex/GUID-heavy selective queries degenerate to
verify-bound (every doc holds every hex trigram — M1); §5.1's generation ceiling needed three
optimization rounds (bitmap trigram collection, hand-rolled normalizer passes, parallel
serialization) landing at ~5-10% on a 147MB corpus, within run noise; rule 3 gained the
line-leading `#`/`=` creole escapes the probe fixture had missed; and Phase 2 (§10) remains
unbuilt as planned.

**Post-release audit (3.0.71, 2026-08-31):** an adversarial full-plan audit found and fixed five
defects in the execution — rule 5b missed `note<<eventNote>>`/`hnote across` openers (recall hole
for event/assertion note bodies); C# `char.IsWhiteSpace` vs JS `/\s/` diverged on U+0085/U+FEFF in
5b (cross-language deep false negative); the deep-authoritative final could hide a raw shallow
match for positive queries (now removal happens only under `!!` — the only removal §4.4 ever
promised); a corrupt index blob wedged the chip (worker init had no rejection handler); and the
KSI1 sparse row encoding was untested everywhere (list rows appear only at ≥17 docs; a 20-doc
vector now pins both encodings). §2.5's "row highlighting reuses the data-row-search mechanism
during verify" is unreachable by construction (any needle in `data-row-search` shallow-matches the
group, since group `data-search` aggregates all row text) — per-row pinpointing of payload-only
matches is hit-location UX, i.e. Phase 2. Coverage gaps from §9.2(e)/(f) and §9.3(b)/(c)/(l)/(n)
closed; the wrap-crossing E2E needle now provably crosses the cut.

This plan is self-contained: all file/line references verified against the working tree at v3.0.69
(supporting measurement tooling lives in `tools/search-bench/`).

---

## 1. Problem and current state

The report search box does not search everything. Users searching for a string they can see in a
note payload, a diagram message, or SQL text get no results. This was a deliberate limit to avoid
bloating already-large reports (100MB-class HTML at ~1,400 tests with deep nesting) and to keep
per-keystroke search fast.

### 1.1 What IS searched today

Search runs client-side over a pre-built `data-search` attribute on each scenario `<details>`:

- Non-parameterized scenarios: built at [ReportGenerator.cs:1172-1184](src/Kronikol/Reports/ReportGenerator.cs#L1172-L1184) —
  feature display name + description, scenario display name, rule, feature labels, categories,
  scenario labels, error message (failed only), all step + sub-step text (`CollectStepText`
  recurses `SubSteps`, [:4268-4276](src/Kronikol/Reports/ReportGenerator.cs#L4268-L4276)), and
  *extracted* diagram terms.
- Parameterized groups: group-level `data-search` at [:1789-1811](src/Kronikol/Reports/ReportGenerator.cs#L1789-L1811)
  (aggregates all member scenarios), per-row `data-row-search` at [:1898-1914](src/Kronikol/Reports/ReportGenerator.cs#L1898-L1914)
  and [:1993-2009](src/Kronikol/Reports/ReportGenerator.cs#L1993-L2009).
- "Extracted diagram terms" = **only** participant names and `METHOD: url` targets
  (`ExtractDiagramSearchTerms`, [:4247-4266](src/Kronikol/Reports/ReportGenerator.cs#L4247-L4266)).

The search itself (`report-search-function.js`) is a 300ms-debounced linear `String.includes()`
over every scenario's `searchText`, with an advanced-syntax path (`&&`/`||`/`!!`, quoted phrases,
`@tag`, `$status` — `advanced-search.js`).

### 1.2 What is NOT searched today (the gap this plan closes)

- Full diagram source (`diagram.CodeBehind`): message/arrow text, **note payloads (request/response
  bodies)**, SQL statements, assertion note content.
- Flame-chart / whole-test-flow text: span names, ActivitySource names, boundary marker URLs
  (gzip'd JSON in `data-flame-z` attributes).
- **Parameterized example values** — `ExampleFlatValues`/`ExampleValues` are rendered as `<td>`s
  ([:1922-1926](src/Kronikol/Reports/ReportGenerator.cs#L1922-L1926)) but appear in neither
  `data-search` nor `data-row-search`; they are searchable only when echoed in the display name.
  This is a coverage bug this plan also fixes.

### 1.3 Facts that shape the design (verified)

1. **The missing text is (almost all) already in the report, compressed.** In BrowserJs and
   InlineSvg rendering modes, every diagram's full source is gzip+base64 in the
   `<script id="puml-data" type="application/json">` blob
   ([ReportGenerator.cs:1441-1446](src/Kronikol/Reports/ReportGenerator.cs#L1441-L1446)); in plain-img
   mode it is uncompressed in a `.raw-plantuml <pre>` ([:1375-1385](src/Kronikol/Reports/ReportGenerator.cs#L1375-L1385)).
   So full search does not require duplicating megabytes of text — only an index over it.
2. **Report generation happens after the test run**, as parallel output actions
   (`RunOutputs`, [:245-291](src/Kronikol/Reports/ReportGenerator.cs#L245-L291)). Index build cost
   is report-generation cost, not test-run cost, and diagram rendering dominates that phase.
3. **Reports open via `file://`** (E2E tests pin this). `fetch()` of sibling files is blocked there,
   but Blob-URL Web Workers are proven from `file://` in this codebase
   (`BrowserRenderWorkerTests.Diagram_renders_in_a_worker_from_a_file_url`,
   [BrowserRenderWorkerTests.cs:274-291](tests/Kronikol.Tests.EndToEnd/BrowserRenderWorkerTests.cs#L274-L291)).
4. **Compression machinery exists on both sides**: `InternalFlowHtmlGenerator.CompressToBase64`
   ([InternalFlowHtmlGenerator.cs:325-334](src/Kronikol/InternalFlow/InternalFlowHtmlGenerator.cs#L325-L334);
   `GZipStream`, `CompressionLevel.Optimal`, UTF-8, standard base64) and browser-side
   `DecompressionStream('gzip')` readers.
5. **Merged reports fully regenerate HTML** (`MergeableReportRenderer.Render` calls
   `GenerateHtmlReport`), so a generation-time index is rebuilt automatically on merge (§5.4 caveat
   for flame data).
6. Scale target: **up to 2,000 scenarios, ~100 nested sub-step strings per scenario, heavy
   payloads, ~100MB HTML.**

---

## 2. Decisions (settled)

1. **Prebuilt index at report-generation time.** Not a client-side on-demand build, not a JS search
   library (library index formats would need byte-identical reimplementation in C# and in
   Kronikol4J; the report JS is deliberately dependency-free).
2. **On by default (opt-out).** New option `FullSearchIndex` (bool, default `true`) on
   `ReportConfigurationOptions` ([src/Kronikol/ReportConfigurationOptions.cs](src/Kronikol/ReportConfigurationOptions.cs),
   property-initializer style with XML-doc default statement, like `LazyLoadDiagramImages` :61-62).
3. **Embedded only — no sidecar file.** A lazy `<script id="kron-search-index" type="application/json">`
   gzip+base64 blob, emitted next to `puml-data`. Single-file portability holds unconditionally; no
   CI artifact-allowlist changes; no missing-file degradation path.
4. **Hashed-trigram → scenario-bitset index with an exact verify pass.** The index is an
   accelerator, never the truth: candidates are confirmed with the same `includes()` semantics the
   search has today, so hash collisions and arbitrary-unicode payloads are harmless (false
   positives only) and the advanced syntax composes unchanged.
5. **Scenario granularity** (≤ ~2,000 docs), not row granularity (rows could be 10× and would
   balloon bitsets). Row highlighting inside matched groups reuses the `data-row-search` mechanism
   during verify.
6. **Docs referenced by anchor id via a doc table** in the blob, not DOM ordinal.
7. **Activation is auto-escalate** (shallow instantly, deep merges in) with the explicit UX in §7;
   chip wording decided (§12 Q-C).
8. **Hit-location highlighting is a separate optional Phase 2** (§10), not part of the core
   deliverable.

---

## 3. Corpus: exactly what gets indexed per scenario

Per scenario doc, the corpus is the concatenation of:

1. The existing `data-search` content (pre-HTML-encode string — identical to what
   `getAttribute('data-search')` returns client-side), **extended** with parameterized example
   values (§1.2 gap; also add them to `data-row-search` so row highlighting matches).
2. Every diagram's full `CodeBehind` for that scenario.
3. Flame text for that scenario: ActivitySource names (`s[]`), span names (`f[i][1]`), boundary
   marker labels (`m[i][1]`) from the flame JSON
   ([InternalFlowRenderer.cs:183-232](src/Kronikol/InternalFlow/InternalFlowRenderer.cs#L183-L232)).

Each piece passes through the **shared normalization** (§4.1) before trigram extraction.

**The corpus/verify invariant (correctness-critical):** the client verify pass must reassemble
byte-identical normalized text from the DOM (§6.3). Anything indexed but not reassemblable client
side is a bug that silently *hides* results (candidate found by index, killed by verify).
Consequently:

- Verify reads diagram source from **`#puml-data` only** (or the `.raw-plantuml <pre>`
  `textContent` in img mode). Never from `data-plantuml` attributes: `collapsible-notes-script.js`
  rewrites those **on first render** (default note-format conversion, assertion/step/database
  stripping, truncation — `_preProcessSource`,
  [collapsible-notes-script.js:1852-1900](src/Kronikol/Reports/collapsible-notes-script.js#L1852-L1900))
  and on every note toggle ([:1405](src/Kronikol/Reports/collapsible-notes-script.js#L1405)).
  `#puml-data` is written once ([ReportGenerator.cs:1443](src/Kronikol/Reports/ReportGenerator.cs#L1443))
  and never mutated.
- Report-level data that is not per-scenario (component diagram, per-boundary
  `window.__iflowSegments` popups) is **excluded** from the scenario index.

### 3.1 What can never be found (capture-time truncation — stated plainly)

Text cut **before** it reaches the report cannot be indexed or verified. The complete audited list:

| Cap | Default | Effect |
|---|---|---|
| `RequestResponseLogger.MaxContentLength` ([RequestResponseLogger.cs:20,40-41](src/Kronikol/Tracking/RequestResponseLogger.cs#L20)) | **off** (`null`) | HTTP bodies cut at N chars |
| Redis tap bulk-string cap ([RedisTapOptions.cs:60](src/Kronikol.Extensions.TcpTap/RedisTapOptions.cs#L60)) | off | |
| SQL `MaxResponseRows` = 10, `MaxValueDisplayLength` = 500 ([SqlTrackingOptionsBase.cs:49-59](src/Kronikol/Sql/SqlTrackingOptionsBase.cs#L49-L59)) | **ON** | SQL result rows/cells beyond caps are unsearchable |
| Assertion/closure value caps (`Track.MaxValueLength` = 100, `ClosureValueResolver` = 50) | ON | |

Deep search searches everything **the report contains**. The SQL defaults stay as they are —
decided (§12 Q-D): keep the caps, document them in the search wiki page. Arrow labels truncated at
2,000 chars are fine — the full URL is re-added into the note
([PlantUmlCreator.cs:328-332](src/Kronikol/PlantUml/PlantUmlCreator.cs#L328-L332)). The "40 lines"
note truncation is client-display-only; full text stays in the page.

---

## 4. Index design

### 4.1 Shared normalization (`normalizeForSearch`) — one function, two implementations, pinned vectors

`CodeBehind` is not the wire text. `PlantUmlCreator` re-indents JSON and **strips `null`
properties** ([PlantUmlCreator.cs:1087-1112](src/Kronikol/PlantUml/PlantUmlCreator.cs#L1087-L1112)),
inserts Creole escapes (`~` before doubled `/ * _ - " [` and `<`-tag lookalikes,
[:418-479](src/Kronikol/PlantUml/PlantUmlCreator.cs#L418-L479)), chunks note values to 80-char
lines (`MaxNoteChunkChars`), and **inserts real newlines into any whitespace-free run > 120 chars**
(`WrapUnbreakableRuns`, [:1008-1085](src/Kronikol/PlantUml/PlantUmlCreator.cs#L1008-L1085)) — so a
minified payload, base64 blob, or long URL is physically line-broken in the source and a naive
substring search misses across the break. Users search for what they *see rendered*, so both the
index and verify normalize identically:

1. **Canonicalize line endings** `\r\n` → `\n` first. Measured fact (2026-08-31,
   `tools/search-bench/real-calibrate.js` over real E2E-generated reports): `CodeBehind` contains
   CRLF — `PlantUmlCreator` uses `Environment.NewLine`. Canonicalizing also makes the index immune
   to the .NET/Java newline difference (Kronikol4J parity bonus).
2. **ASCII-only case fold** (`A-Z` → `a-z`), applied identically to corpus, DOM text, and the
   query. NOT `ToLowerInvariant`/`toLowerCase`: measured divergence — .NET `ToLowerInvariant`
   leaves `İ` (U+0130) unchanged while JS `toLowerCase` maps it to `i` + U+0307 (two code units,
   different trigrams) — an index built in C# and verified in JS would silently disagree on any
   such character. Pin with test vectors including `İ`, `ß`, and combining marks. (Non-ASCII
   case-insensitivity is thereby unsupported in deep search; today's shallow search has the same
   divergence, unnoticed.)
3. Strip Creole escape `~` characters (client inverse already exists —
   [collapsible-notes-script.js:860](src/Kronikol/Reports/collapsible-notes-script.js#L860)).
4. Strip PlantUML inline markup tags: `<color:...>`, `<font...>`, `</font>`, `<i>`, `</i>`, `<b>`,
   `</b>` (verified against real output: `<color:gray>[traceparent=...]` header lines,
   `<font color="lightgray">&` dividers, `<i>[binary content]</i>`).
5. **Rejoin forced breaks**, two sub-rules (both discovered/validated against real formatter
   output — `tools/search-bench/formatter-probe` + `validate-normalize.js`, 10/10 assertions):
   - **5a — arrow labels:** long message labels wrap with a *literal* `\n` escape sequence plus
     indentation spaces inside the line (e.g. `...ppp\n        ppp...`) — strip `\\n[ \t]*`
     everywhere. (Found only by probing: a third break mechanism beyond chunking/wrapping.)
   - **5b — note bodies** (between `note ...` and `end note` — chunking and `WrapUnbreakableRuns`
     only apply to note content): delete a `\n` whose next line starts with a non-whitespace
     character. Re-indented JSON lines are indented, so structural boundaries survive; 80-char
     value chunking and 120-char run wrapping (both continue flush-left) are rejoined. Scoping to
     note bodies avoids gluing directive lines onto payload text. Benign side effect: adjacent
     flush-left header lines join into one run (false boundary trigrams only — verify absorbs).
6. Collapse remaining whitespace runs to a single space.

Implemented once in C# (generation) and once in JS (verify), pinned to each other by **shared test
vectors** (§9.1) — the reference implementation lives in `tools/search-bench/validate-normalize.js`
and its stress fixture (`formatter-probe/Program.cs`) doubles as the vector source: run-wrapped
200-char token reconstructed contiguously (the wrap split the planted needle mid-token), chunked
300-char header reconstructed, `**important**`/`//slanted//`/`__deep__` unescaped, wrapped URL in
both arrow label and JSON value reconstructed, `İstanbul` untouched by the ASCII fold, ordinary
sentences unharmed, `end note` not glued into payloads, and the null-stripped property confirmed
unfindable (the documented formatter limitation, not a normalization bug). Residual known miss: a
phrase spanning a legitimate indented line boundary — document, don't chase.

### 4.2 Structure and serialization

- **Buckets:** every 3-char sliding window (UTF-16 code units, step 1) of the normalized corpus is
  hashed — **FNV-1a 32-bit: `h = 0x811C9DC5`, then per code unit `h = (h XOR unit) * 0x01000193`
  (mod 2³²), bucket = `h & (B-1)`** (reference implementation:
  `tools/search-bench/synthetic-bench.js`). Working default `B = 65536`; final value from §11-M1.
  Hashing (not literal trigrams) makes the table size **absolutely bounded** regardless of unicode
  content or corpus volume.
- **Rows:** per bucket, scenario doc-ids in whichever encoding is smaller:
  bitset (`ceil(D/8)` bytes — 250B at 2,000 docs) or delta-varint id list. JSON-syntax trigrams go
  dense; the GUID/hex payload tail goes sparse.
- **Doc table:** scenario anchor ids; position = doc-id; **order = the `allScenarios` enumeration
  (`features.SelectMany(f => f.Scenarios)`), the same order the anchor-id map is built in**
  ([ReportGenerator.cs:959-977](src/Kronikol/Reports/ReportGenerator.cs#L959-L977)).
- **Binary layout (v1):** `magic "KSI1" | u8 version | u32 B | u32 docCount | doc table
  (length-prefixed UTF-8) | rows (varint length + u8 encoding + payload; length 0 = empty bucket)`
  → gzip (`CompressToBase64` conventions: Optimal, UTF-8-irrelevant since binary → use raw bytes +
  `Convert.ToBase64String`) → `<script id="kron-search-index" type="application/json">"<base64>"</script>`.
- Emitted for both HTML reports (Specifications + TestRunReport — both come from
  `GenerateHtmlReport`), only when the report has scenarios and `FullSearchIndex` is true.

### 4.3 Size budget (design must stay inside; verify in §11)

| Scale | Raw ceiling (all-dense) | Expected (adaptive + gzip) | Embedded (×1.33 base64) |
|---|---|---|---|
| Typical report | ~1-2 MB | tens–hundreds of KB | < 0.5 MB |
| 1,400 scenarios / 100 MB | 65,536 × 175 B ≈ 11.5 MB | ~2-4 MB | ~3-5 MB |
| 2,000 scenarios / heavy payloads | 65,536 × 250 B ≈ 16.4 MB | ~2-6 MB | ~3-8 MB |

Rationale: ~30-50KB of JSON-ish text touches ~3-8k buckets (JSON keys/syntax are
trigram-repetitive) → matrix ~5-10% dense. Dials: `B` (32k halves the table; more false-positive
candidates for verify to absorb) and the dense/sparse threshold.

**Measured (synthetic first pass, 2026-08-31 — `tools/search-bench/`, caveats in its README):**
the estimates above are conservative. Worst tier (2,000 × 50KB ≈ 98MB text) produced a **1.06MB
base64 blob**; medium (1,400 × 40KB) 0.90MB; median (300 × 3KB) 0.25MB. Synthetic corpora are more
trigram-repetitive than real ones, so treat the §11 real-corpus rerun as the confirmation, but the
budget has an order of magnitude of headroom.

### 4.4 Query semantics

- `@tag` and `$status` terms never touch the text index (metadata, already cheap).
- Text/phrase term with ≥3 chars: AND-intersect the rows for all its trigram windows → that term's
  candidate set. Terms <3 chars contribute no constraint (candidate set = all docs) — and a query
  consisting only of such terms stays entirely on the shallow path.
- Legacy multi-token queries (implicit AND): candidates = intersection across terms.
- Advanced expressions: evaluate the boolean AST over candidate-set membership as an
  **over-approximation** — positive terms use their candidate sets; any term under negation (`!!`)
  is treated as "possibly true", so its scenarios are never pruned. Sound by construction; the
  verify pass computes the real answer. Verified feasible: the AST has exactly 7 node types
  (`text`, `phrase`, `tag`, `status`, `and`, `or`, `not` —
  [advanced-search.js:149-195](src/Kronikol/Reports/advanced-search.js#L149-L195)), so the
  evaluator is a small recursive walk.
- **Verify**: for each candidate, assemble the normalized corpus (§6.3) and run the *existing*
  matcher (`includes()` / `advancedSearchMatch`) against it. Feed survivors into the existing
  `sr` flag → `applyVisibility(c)` → `update_url_hash()` pipeline (§6.2).
- Note deep search can **remove** results for negated queries, not just add: today `!!foo`
  evaluates against partial text; with full text, a scenario may newly match `foo` and be excluded.
  Deep is authoritative. §7 covers the UX.

**Known pressure point (measured, revised down):** a low-selectivity query (trigrams in nearly
every scenario, e.g. `id`) degenerates to decompress-and-verify over everything. Synthetic bench
(worst tier, 98MB corpus): **324ms cold, ~1ms warm** in Node. Per-blob `DecompressionStream`
overhead is now measured (Node's web-streams implementation over real diagram blobs):
**~0.16ms/blob**, i.e. ~320ms extra across 2,000 blobs — worst-case cold verify lands around
**0.5-0.8s in-browser**, comfortably inside the §7 progress affordance (confirm on real Chrome:
§11-M2). All other measured query classes are effectively instant (selective ~0ms, moderate ≤26ms
cold).

**What the index is actually for (measured insight):** linear `includes()` over the full 98MB
corpus *already resident in RAM* takes only ~30ms — V8 substring scan is ~3GB/s. The index's value
is therefore not scan speed but being the **decompression/memory gatekeeper**: selective queries
(the common kind) decompress 1-200 scenarios' text instead of all 2,000, and resident memory stays
LRU-bounded instead of ~100MB pinned. A no-index "decompress everything once, then linear-scan"
design was considered and rejected on exactly those two grounds (permanent ~100MB tab memory;
multi-second first use on monster reports for *every* query class, not just broad ones).

---

## 5. Generation-side implementation (.NET)

1. **Where:** inside `GenerateHtmlReport`, in the same loop that already builds `searchParts` (both
   the scenario path ~:1172 and `RenderParameterizedGroup` ~:1789 — the diagram `CodeBehind` is in
   hand at the `CompressToBase64` call sites :1358/:1367/:2329/:2336; flame JSON is in hand in
   `InternalFlowHtmlGenerator`). Accumulate normalized-trigram bucket sets per scenario; serialize
   at the end next to the `puml-data` emission (:1441). Single pass over ~100MB ≈ low single-digit
   seconds worst case; ~zero wall-clock in `RunOutputs` parallelism.
2. **Corpus additions** (§3): example values into corpus + `data-row-search`; flame text via the
   flame JSON before compression.
3. **Option:** `FullSearchIndex` bool default `true` on `ReportConfigurationOptions`, XML-doc
   stating the default, wiki table + sample block updated
   ([Kronikol.wiki/Report-Configuration.md](../Kronikol.wiki/Report-Configuration.md)).
4. **Merge path:** `MergeableReportRenderer` passes `wholeTestSegments: null` — flame arrives
   only as precomputed HTML strings containing `data-flame-z` attributes. Decided (§12 Q-E):
   extract + gunzip those attributes from the precomputed HTML when building the corpus, so
   "search everything" holds for merged reports too.
5. The head template has an unused `enrichSearchDataScript` slot
   ([ReportGenerator.cs:602](src/Kronikol/Reports/ReportGenerator.cs#L602), interpolated at :649)
   usable for wiring/config constants.

### 5.1 Latency requirement (hard): no significant addition to report-generation wall-clock

Report generation is what stands between "tests finished" and "run complete", so the index must not
lengthen it noticeably. Requirement: **no measurable increase in `RunOutputs` wall-clock on typical
reports; hard ceiling low single-digit % on 100MB-class reports** — measured and recorded in §11-M3
before shipping default-on. How:

1. **Build the heavy part once, share it between both HTML reports.** Specifications and
   TestRunReport render the same features/diagrams; trigram extraction (the expensive half) is
   keyed by scenario id and identical for both. Compute per-scenario bucket sets in one shared
   `Lazy<Task<...>>`; each report then only assembles its own doc table + serializes + gzips
   (cheap, a few MB). Verified safe: anchor ids are computed deterministically from display names
   + duplicate counters over `allScenarios` before rendering
   ([ReportGenerator.cs:961-977](src/Kronikol/Reports/ReportGenerator.cs#L961-L977)) and both
   reports receive the same `features` array, so doc tables agree. One interplay:
   `generateBlankOnFailedTests` writes the Specifications report as an **empty file** when any
   test failed ([:441-442](src/Kronikol/Reports/ReportGenerator.cs#L441-L442)) — the `Lazy` must
   be triggered on first *use*, so a blanked report never pays for (or forces) the build.
2. **Overlap, don't append.** Kick the shared build off as a `Task` at the start of report
   generation (features + diagram `CodeBehind` are all in hand before the body loop); await it only
   at the emission point next to `puml-data`. Diagram embedding/HTML string building dominates that
   window, so the await should normally be a no-op.
3. **Parallelize across scenarios** (`Parallel.ForEach`, per-scenario bucket sets merged at the
   end) — trigram hashing is embarrassingly parallel.
4. **Memoize per distinct text.** The same body appears dozens of times in a real report (the CLI's
   `BodyCache` dedupes on exactly this observation — see §5.2); cache trigram sets per distinct
   `CodeBehind`/payload string so repeated payloads hash once.
5. **Hash over spans, not concatenated strings** — no materializing a giant per-scenario corpus
   string; feed the normalizer/hasher piecewise (house style: the 3.0.69 walker allocation diet).
6. gzip at `CompressionLevel.Optimal` first; drop to `Fastest` only if §11-M3 shows the
   compression step material (a few MB of binary should be ~100-300ms).

Pin the generation cost with a deterministic observable (e.g. distinct-text hash count), not
wall-clock, in unit tests; wall-clock lives in the §11-M3 bench record only.

### 5.2 Relationship to the CLI query engine (`kronikol query`)

The CLI already has full-text search — over the **data file**, not the HTML:
`kronikol query grep` ([QueryCommand.Search.cs](src/Kronikol.Tool/QueryCommand.Search.cs)) does
case-insensitive substring over steps, assertions, URIs, headers, and payload bodies, reading raw
captured content via an offset-based lazy index (`ReportIndex` holds narrative/topology in memory
and seeks payloads on demand — [ReportIndex.cs:7-15](src/Kronikol.Tool/Query/ReportIndex.cs#L7-L15)),
deduping identical bodies so each distinct body is searched once. `grep --number` adds
numeric-aware matching ([QueryCommand.NumberGrep.cs](src/Kronikol.Tool/QueryCommand.NumberGrep.cs)).

Implications, both directions:

- **The two search surfaces see different text.** CLI grep matches the *wire* payload (nulls
  intact, original formatting); HTML deep search matches the *rendered* text (`CodeBehind` after
  PlantUML formatting: nulls stripped, JSON re-indented, §4.1 normalization). The same needle can
  hit in one and miss in the other — notably `"foo": null` is CLI-findable but invisible to the
  report. Document this in the wiki (Search-Syntax.md ↔ Querying-Reports.md cross-links): deep
  search = "find it in the report you're looking at"; `query grep` = "wire-exact search, plus
  headers-as-data and numeric matching".
- **Borrow its proven patterns, not its code**: distinct-body dedup (→ §5.1 item 4) and
  deterministic-observable perf pinning (`PayloadOpens` — → §5.1, §9.2) both originate there.
- **No index sharing in either direction.** The CLI's offset index answers "where in the data
  file"; the HTML index answers "which scenario in this page". The CLI is already fast on its own
  artifact and reads the data file, not the HTML; the HTML index has nothing to offer it (and vice
  versa).
- **Non-goals inherited knowingly**: numeric-aware matching (`grep --number` semantics) and
  address-precise results ("which body, which JSON path") stay CLI-only; the report's deep search
  stops at scenario visibility (+ Phase 2 hit-location, §10).

## 6. Client-side implementation (JS)

### 6.1 New script + shared decompressor

- New file `src/Kronikol/Reports/report-search-index.js` — auto-embedded by the csproj wildcard
  `<EmbeddedResource Include="Reports\report-*.js" />`
  ([Kronikol.csproj:33](src/Kronikol/Kronikol.csproj#L33)) and always included (all `report-*.js`
  are unconditional — [ReportGenerator.cs:444-549](src/Kronikol/Reports/ReportGenerator.cs#L444-L549)).
- **Structure constraint for testability:** all pure logic (normalizeForSearch, trigram windowing,
  FNV-1a, varint/bitset decode, intersection, candidate evaluation) as plain functions with **no
  DOM/worker/`DecompressionStream` references**, so the Jint harness (§9.1) can execute them
  directly. Worker plumbing and DOM assembly live in separate functions covered by Playwright only.
- `decompressGzipBase64` currently ships **only** in BrowserJs mode
  ([ReportGenerator.cs:587](src/Kronikol/Reports/ReportGenerator.cs#L587)); flame has a private
  duplicate. Extract **one shared always-included decompressor** (e.g.
  `report-decompress-helper.js`) consumed by search, and by the two broken callers in §8. Deep
  search must not depend on the BrowserJs-only global.
- **Worker source**: the worker is a Blob built from the pure-function section of
  `report-search-index.js` (the same functions Jint tests) plus a small message-loop wrapper —
  e.g. serialize the functions into the Blob string, or keep them in one `const SOURCE = ...`
  region. No token-substitution machinery needed (unlike `plantuml-worker-host.js`, which is
  JSON-escaped via `DiagramContextMenu`); pick whichever keeps a single copy of the logic.

### 6.2 Integration points (all verified)

- Reuse the **`sr` hide-flag** on `fc().items` — composes with all six filter dimensions for free
  via the OR at [report-scenario-feature-map-helper.js:25](src/Kronikol/Reports/report-scenario-feature-map-helper.js#L25).
  `fc()` is built once and never invalidated; write results onto the item objects, not DOM attributes.
- `clear_all_filters()` ([report-export-function.js:1-38](src/Kronikol/Reports/report-export-function.js#L1-L38))
  must reset any new deep-search state (it resets `sr` at :6).
- Worker: Blob-URL pattern from [plantuml-browser-render-script.js:223-226](src/Kronikol/Reports/plantuml-browser-render-script.js#L223-L226)
  (strictly less capability needed: no fetch, no OffscreenCanvas). Query messages carry a
  generation counter; stale replies dropped (typing cancels in-flight deep queries).
- Decompressed-verify-text cache: copy the byte-bounded LRU at
  [plantuml-browser-render-script.js:73-95](src/Kronikol/Reports/plantuml-browser-render-script.js#L73-L95).
  Worker-resident memory only; **no IndexedDB/localStorage** — decided (§12 Q-B): the report keeps
  zero browser-storage use (localStorage persistence was deliberately removed; dead stubs at
  `report-persistent-filter-function.js`).
- URL hash: deep needs no new key initially (deep applies whenever the index exists and the query
  qualifies); if a key is added later, note `parse_url_hash` drops any value containing `=`
  ([report-url-hash-function.js:53-57](src/Kronikol/Reports/report-url-hash-function.js#L53-L57)).
  Hash restore runs the shallow search synchronously (:60) — deep must re-run when the index is
  ready.
- Telemetry for E2E: `window.__kronikolSearch = { indexState, docs, buckets, candidates, verified, ms }`
  mirroring `window.__kronikolRender`.

### 6.3 Verify-corpus assembly per rendering mode

Given a scenario `<details>` (all are in `fc().items`):

- Always: `getAttribute('data-search')`.
- BrowserJs / InlineSvg: descendant `.plantuml-browser[id], .plantuml-inline-svg[id]` → source from
  the lazily-parsed `#puml-data` map (own accessor mirroring `_getPumlZ`,
  [plantuml-browser-render-script.js:429-436](src/Kronikol/Reports/plantuml-browser-render-script.js#L429-L436)),
  gunzipped. Never `data-plantuml` (§3 invariant).
- Plain img: `.raw-plantuml pre` → `textContent` (no `puml-data` exists in this mode).
- Flame: `.iflow-flame[data-flame-z]` → gunzip → JSON → `s[]`/`f[i][1]`/`m[i][1]`; plus
  uncompressed `[data-flame]` variants.
- Parameterized groups: descendant queries find all per-row diagram wrappers — correct for
  scenario-granularity docs.
- All pieces through JS `normalizeForSearch` before matching.

## 7. Activation UX (auto-escalate, spec)

States of a status chip inside `div.filter-search`
([ReportGenerator.cs:820](src/Kronikol/Reports/ReportGenerator.cs#L820); already
`display:flex; position:relative` — [stylesheets.css:168-174](src/Kronikol/Reports/stylesheets.css#L168-L174)):

1. **Hidden** — no query, index absent (`FullSearchIndex=false` / old report), or query has no
   deep-eligible term. Behaviour is byte-for-byte today's.
2. **Working** — deep-eligible query typed: shallow results apply instantly exactly as today; chip
   shows "searching everything…" using the report's pending pattern (0.2s-delayed spinner,
   refcounted — [collapsible-notes-script.js:2159-2188](src/Kronikol/Reports/collapsible-notes-script.js#L2159-L2188),
   [collapsible-notes-styles.css:40-70](src/Kronikol/Reports/collapsible-notes-styles.css#L40-L70)).
   First use also covers index load/decompress in the worker.
3. **Done** — deep-only matches become visible (scenarios sit in fixed document order, so nothing
   reorders — more rows simply unhide) and the chip reads **"+N more found in payloads &
   diagrams"** ("no additional matches" when N=0). For negated queries where deep *removes*
   results: "results refined (+N/−M)". Chip resets on next input.
4. Single-match auto-expand (existing behaviour) is not retracted when deep adds results.
5. There is no result-count UI today anywhere (only `#failure-counter`); this chip is the first —
   keep it small, `.search-help-toggle` is the visual model.
6. **Expected timings (from the §11 first-pass measurements):** index load on first deep query is
   ~10-50ms (8ms decode + worker spawn) — under the 0.2s spinner delay, so users normally never
   see a loading state at all. Typical deep queries verify in ≤30ms — deep results land visually
   simultaneously with shallow ones. The chip's "working" state is genuinely visible only for the
   cold broad-term query on a monster report (~0.5-0.8s, §4.4); for that case verify runs in batches and
   reveals matches progressively (scenarios unhide in document order as batches complete), so the
   page is responsive and honest during the only slow path that exists.

## 8. Pre-existing bugs surfaced by this investigation (fix first, own commits, TDD)

1. **InlineSvg + context menu → `ReferenceError`.** `context-menu-script.js:51` calls
   `decompressGzipBase64`, but the script is emitted for `isInlineSvg` too
   ([ReportGenerator.cs:590](src/Kronikol/Reports/ReportGenerator.cs#L590)) while the definition is
   BrowserJs-gated (:587).
2. **Internal-flow popup in non-BrowserJs reports → same class.**
   `internal-flow-popup-script.js:78` (gated on `internalFlowTracking`, :594) calls the same
   missing global. Both fixed by the shared decompressor of §6.1.
3. **Unencoded `CodeBehind` in img mode.** Raw source is interpolated into `<pre>` without
   `WebUtility.HtmlEncode` ([:1382](src/Kronikol/Reports/ReportGenerator.cs#L1382),
   [:2349](src/Kronikol/Reports/ReportGenerator.cs#L2349)) — a payload containing `<` yields
   malformed markup (and breaks `textContent` round-trip, which §6.3 depends on).

## 9. Tests (TDD, red-green-refactor per CLAUDE.md)

### 9.1 JS logic — Jint harness (NOT the C# port)

- `tests/Kronikol.Tests.SearchEngine/` executes the *real shipped* `advanced-search.js` via Jint
  (`JintTestBase` loads the embedded resource — [JintTestBase.cs:24-34](tests/Kronikol.Tests.SearchEngine/JintTestBase.cs#L24-L34)).
  Add a sibling base loading `report-search-index.js`; test normalizeForSearch, trigram windows,
  FNV-1a, decoders, intersection, over-approximating AST evaluation.
- **Do NOT extend `tests/Kronikol.Tests/Reports/SearchFunction.cs`** — it is a hand-maintained C#
  *port* of the search JS kept in sync only by convention; a second hash implementation there would
  green-light without touching the shipped code.
- **Cross-language pinning:** one shared set of test vectors (input string → normalized string →
  trigram hashes → bucket ids; corpus → serialized index bytes) asserted by both the C# unit tests
  and the Jint tests. This doubles as the Kronikol4J parity spec.

### 9.2 Generator — .NET unit tests

Mirror `SearchDataAttributeTests` (generate report → regex/extract → assert): blob present by
default and absent with `FullSearchIndex=false`; decode blob in C# and assert a note-payload-only
string, a message-text-only string, an SQL-only string, an example-value-only string each map to
the right doc; byte-stable serialization for a fixed corpus; doc table matches anchor ids;
merge-path report contains a rebuilt index. Perf properties via deterministic observables (e.g.
candidate counts), **never wall-clock** — house rule.

### 9.3 Playwright E2E

New class in `[Collection(PlaywrightCollections.Search)]`, following `AdvancedSearchTests` idioms
(`SearchAndWaitForCount` = `FillSearchBar` + `WaitForFunctionAsync` with `PollingInterval = 200`;
unique report filename per fact; generate in-test, navigate `file://`):

- **Fixture rule**: tests covering chunked/wrapped/escaped content must generate diagrams through
  the capture pipeline (`RequestResponseLog` entries → the diagrams fetcher, as
  `tools/search-bench/formatter-probe/Program.cs` demonstrates) — `LargeReportFixture`-style
  hand-written PlantUML bypasses `PlantUmlCreator` entirely and those transforms never fire (§11
  third pass).
- Red-first coverage: payload-only / message-only / SQL-only / example-value-only strings found;
  each rendering mode (BrowserJs, InlineSvg, plain img); parameterized row highlight on
  payload-only match; advanced expression with `!!` where deep removes a shallow result; chip
  states incl. N=0; `clear_all_filters` resets deep state; `#q=` hash restore re-runs deep;
  `FullSearchIndex=false` report byte-behaves like today; JSON→YAML note toggle does **not** change
  deep results (pins the `puml-data`-not-`data-plantuml` invariant); telemetry object populated.
- CI: the class lands in "E2E (Remainder)" automatically; to place it in "E2E (Search & Filters)"
  it must be added to **both** that job's `filter:` and the Remainder `filter-exclude:` union
  ([ci.yml:84-87](/.github/workflows/ci.yml#L84-L87), [:119](/.github/workflows/ci.yml#L119) —
  keeping them in sync is a stated invariant there).

### 9.4 Perf guard

`LargeReportFixture` ([tests/Kronikol.Tests.EndToEnd/LargeReportFixture.cs](tests/Kronikol.Tests.EndToEnd/LargeReportFixture.cs))
is the ready-made heavy corpus. Add `DeepQueryMs` (and index decode ms) to the `Metrics` record and
a `× m.Stretch` budget clause in the ternary at
[BrowserRenderWorkerTests.cs:226-233](tests/Kronikol.Tests.EndToEnd/BrowserRenderWorkerTests.cs#L226-L233)
(ContentionScale is `internal` to that assembly — the budget lives there). Also confirm `ReadyMs`
(4500ms budget) absorbs parsing the extra multi-MB script.

### 9.5 Collateral-damage audit

The blob is base64 inside the document: audit existing `Assert.DoesNotContain`/regex assertions
that scan whole report HTML for accidental collisions (the `kron-search-index` wrapper text is the
realistic collision surface).

## 10. Phase 2 (optional, deferred): hit-location UX

Auto-open the matching diagram / flash the matching note when a deep-only match is opened. The
verify pass already knows which source (payload vs message vs flame vs step) matched, so this is
purely additive UI over the same index — explicitly **not** part of the core deliverable; build
later or never.

## 11. Pre-implementation measurements (before locking §4.2 constants)

Ad-hoc bench under `tools/search-bench/` with README + protocol (convention: `tools/query-bench/`).

**First pass DONE (2026-08-31, synthetic — `tools/search-bench/synthetic-bench.js`, results in its
README):** blob sizes 0.25/0.90/1.06MB b64 across tiers; single-threaded JS build 33/802/730ms
(the C# side, parallel + memoized per §5.1, will be well under); decode 2/5/8ms; queries per §4.4.

**Second pass DONE (2026-08-31, real corpora — `tools/search-bench/real-calibrate.js` over 668
real CodeBehind docs, 8MB, from E2E-generated reports):**

- Bucket density on real docs: ~300 buckets/KB for small docs, saturating hard for big ones
  (311KB doc → only 1,862 buckets; union across all 668 docs just 7,107/65,536) — real diagram
  text is even more trigram-repetitive than the synthetic corpus, so the §4.3 ~1MB worst-tier blob
  projection strengthens.
- `DecompressionStream` per-blob overhead: ~0.16ms → §4.4's revised 0.5-0.8s worst-case cold.
- Real `<color:gray>` header lines and CRLF confirmed (→ §4.1 rules 1 and 4).
- **Fixture gap found:** no on-disk corpus contains >80-char values, so chunking/wrapping/creole
  escaping never fires in available data (LargeRenderBench longest line: 37 chars).

**Third pass DONE (2026-08-31 — `tools/search-bench/formatter-probe` drives the real
`PlantUmlCreator.GetPlantUmlImageTagsPerTestId` with stress content; `validate-normalize.js`
asserts the §4.1 rules against its output, 10/10):** rejoin rules validated on genuine
chunked/wrapped/escaped output, and the probe *discovered* rule 5a (arrow-label literal `\n`
escape) that the plan would otherwise have missed. `LargeReportFixture` note: it hand-writes
PlantUML and **bypasses the formatter entirely** — the red-first E2E tests for
chunked/wrapped/escaped content must generate diagrams through the capture pipeline
(`RequestResponseLog` → fetcher), not via hand-written PlantUML, or the transforms never fire.

Remaining (labels are referenced from other sections):

- **M1** — Blob size for `B ∈ {32768, 65536}` + candidate precision over a *payload-heavy*
  100MB-class corpus (formatter-driven, per the note above). Confirms §4.3 and locks Q-A.
- **M2** — **Real Chrome** worst-case decompress-all verify (confirm the 0.5-0.8s estimate; batch
  candidates per decompression call if per-blob overhead surprises).
- **M3** — .NET-side index build time within `RunOutputs` (pin the §5.1 wall-clock requirement).

Note: `tools/search-bench/formatter-probe` is a standalone csproj deliberately **not** in
`Kronikol.sln` — CI test discovery scans `tests/*/` only, so it stays inert; do not add it to the
solution.

## 12. Open questions

- **Q-A — Bucket count `B` and dense/sparse threshold:** measurement-gated, locked by §11-M1
  during implementation (not a user decision). Synthetic first pass: size difference between 32k
  and 64k buckets was <10% — default 65536 (fewer collisions) unless M1 says otherwise.
- **Q-B — Browser storage (IndexedDB caching): DECIDED — no** (user, 2026-08-31). Rationale kept
  for the record: recommended no on four grounds. (1) The benefit mostly evaporated when the index became prebuilt: there is no
  expensive client-side build to persist — first use is a sub-second gunzip+parse of a few-MB blob.
  The only costly derived state is decompressed verify text, and persisting *that* means writing up
  to ~100MB of payload text into the browser profile. (2) Which is the second problem: payloads can
  contain sensitive data (auth headers, PII); an IndexedDB copy outlives the report file itself — a
  data-retention surface the self-contained single file deliberately doesn't have. (3) IndexedDB
  under `file://` is inconsistent across browsers/private-mode/corporate policies, so the cache
  must silently no-op anyway. (4) The report once had localStorage persistence and it was
  deliberately removed (dead stubs remain, [ReportGenerator.cs:539](src/Kronikol/Reports/ReportGenerator.cs#L539)).
  Revisit only if §11-M2 measures first-deep-query latency on monster reports as genuinely
  annoying (multi-second), and then as an explicit opt-in.
- **Q-C — Chip wording: DECIDED** (user, 2026-08-31) — "+N more found in payloads & diagrams" as
  specced in §7 ("results refined (+N/−M)" for negated queries).
- **Q-D — SQL capture caps: DECIDED** (user, 2026-08-31) — keep the defaults
  (`MaxResponseRows` 10 / `MaxValueDisplayLength` 500). Document in the search wiki page: SQL
  results are captured to 10 rows / 500 chars per value by default, raise the options for more
  searchable text; capped-away text is unrecoverable by any search (report or CLI).
- **Q-E — Merged-report flame text: DECIDED** (user, 2026-08-31) — extract `data-flame-z` from the
  precomputed HTML and index it on the merge path (§5.4), so "search everything" holds for merged
  reports too.

## 13. Kronikol4J parity

In parity scope (core report generation; the port is browser-render-only, so only BrowserJs paths
matter there). The hand-rolled format + §9.1 shared vectors are the porting spec (respect the
port's gzip/CRLF/encoding parity conventions). Until ported, the Java side compares against
`FullSearchIndex=false` output. The CHANGELOG entry must carry the conventional explicit
"Kronikol4J:" impact line.

## 14. Rollout

Same-version bump across all packages (`Directory.Build.props`), template pins keep lagging one
release per convention. CHANGELOG (Keep-a-Changelog house style: bold claim, mechanism, numbers,
pinning tests) must state: reports grow by index size; `FullSearchIndex=false` produces
**byte-identical output to the previous version**; Kronikol4J impact. Wiki:
`Report-Configuration.md` (option), `Search-Syntax.md` (deep behaviour, what's searchable, §3.1
limits incl. the Q-D SQL-caps note, `kronikol query grep` cross-reference per §5.2),
`Large-Response-and-Diagram-Handling.md` cross-link. Tag `v{version}`, push, mark executed in
PLANS_STATUS.md.

## 15. Execution order (when green-lit)

1. **§8 bug fixes** — three pre-existing bugs, each its own TDD commit (red test → fix → green);
   the shared decompressor (§6.1) lands here since two of the fixes need it.
2. **§11 measurements M1-M3** — lock `B` and the dense/sparse threshold (Q-A), confirm the browser
   worst-case, pin the generation wall-clock. Record results in `tools/search-bench/README.md`.
3. **Generation side (§5)** — normalization (C#, against the §9.1 shared vectors first), trigram
   accumulation, serialization, option, emission; unit tests per §9.2 throughout.
4. **Client side (§6) + UX (§7)** — pure functions first (Jint-tested, §9.1), then worker/DOM
   assembly and the chip; E2E per §9.3.
5. **Perf guard (§9.4) + collateral audit (§9.5).**
6. **Docs + release (§13, §14)** — wiki, CHANGELOG (with the Kronikol4J line and the
   `FullSearchIndex=false` byte-identical statement), version bump, tag, push; mark executed in
   PLANS_STATUS.md.

Phase 2 (§10) is explicitly out of scope for this execution.
