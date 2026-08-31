# search-bench

Measurement harness for SEARCH_INDEX_PLAN (full-text "search everything" report index).

## Protocol

- `node synthetic-bench.js` — synthetic first pass (2026-08-31, Node v25.9.0): builds the
  hashed-trigram → scenario-bitset index over three deterministic synthetic corpora
  (median 300×3KB, medium 1400×40KB, worst 2000×50KB ≈ 98MB text), serializes with adaptive
  bitset/varint rows for B ∈ {32768, 65536}, then measures decode + candidate intersection +
  decompress-and-verify for selective / moderate / worst-selectivity queries, plus the
  linear-`includes()` baseline.
- Node's zlib approximates browser `DecompressionStream` throughput but NOT its per-call
  overhead; the browser decompress-all path must be measured for real (§11 of the plan).
- Synthetic corpora are more trigram-repetitive than real reports (bounded key/word vocabulary,
  hex GUIDs). Re-run over a real 100MB-class corpus (LargeReportFixture output) before locking
  constants.

## Results 2026-08-31 (synthetic, Node v25.9.0, dev box)

| Tier | Corpus | Build (1 thread JS) | Blob (b64, B=64k) | Decode | Selective query | Moderate | Worst cold ("id") | Worst warm |
|---|---|---|---|---|---|---|---|---|
| Median 300×3KB | 0.9 MB | 33 ms | 0.25 MB | 2 ms | ~0 ms | 1 ms | 4 ms | ~0 ms |
| Medium 1400×40KB | 55 MB | 802 ms | 0.90 MB | 5 ms | ~0 ms | 16 ms | 162 ms | ~0 ms |
| Worst 2000×50KB | 98 MB | 730 ms | 1.06 MB | 8 ms | ~0 ms | 26 ms | 324 ms | 1 ms |

Baseline: linear `includes()` over the full 98MB corpus resident in RAM = 30 ms — V8 substring
scan is ~3GB/s. The index's real job is therefore NOT scan speed; it is being the
decompression/memory gatekeeper: selective queries decompress 1 scenario instead of 2000, and
resident memory stays LRU-bounded instead of ~100MB.

B=32768 vs 65536 changed blob size by <10% on synthetic data; default 65536 (fewer collisions)
pending real-corpus rerun.

## Real-corpus calibration 2026-08-31 (`real-calibrate.js`)

`node real-calibrate.js [reportsDir]` — extracts real CodeBehind from generated reports' puml-data
blobs. Run over `tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/PlaywrightOutput` (668 docs, 8MB):

- Bucket density ~300/KB small docs, saturating (311KB doc → 1,862 buckets; union 7,107/65,536).
  Real text is MORE trigram-repetitive than the synthetic corpus → blob projection strengthens.
- DecompressionStream vs zlib over 2,000 real blobs: 358ms vs 41ms → **~0.16ms/blob overhead**;
  browser worst-case decompress-all ≈ 0.5-0.8s.
- CRLF present in real CodeBehind (`Environment.NewLine`); `<color:gray>` header lines confirmed.
- Gap: no on-disk corpus has >80-char values — chunking/wrapping/creole never fires (longest line
  37 chars). LargeReportFixture hand-writes PlantUML, bypassing the formatter entirely.

## Formatter validation 2026-08-31 (`formatter-probe/` + `validate-normalize.js`)

`dotnet run --project formatter-probe` drives the REAL `PlantUmlCreator` public API with stress
content (240-char unbroken token, minified JSON, creole markers, 300-char header, null property,
İ) and writes genuine CodeBehind to `formatter-output/`. `node validate-normalize.js` applies the
§4.1 normalization (reference implementation) and asserts 10 properties — all pass:

- 120-char run wrapping, 80-char gray-header chunking, and the **arrow-label literal `\n` escape**
  (rule 5a — DISCOVERED by this probe, a third break mechanism) all rejoin to contiguous text.
- Creole `~` escapes strip back to `**`/`//`/`__`; ordinary text and note structure unharmed.
- `"nullField": null` is stripped by the formatter before the report — the documented
  cannot-find limitation, confirmed.

`validate-normalize.js` doubles as the cross-language test-vector source for the C#/JS/Java
implementations. The normalization reference implementation lives in `normalize.js` (shared by
validate-normalize.js and m1-bench.js).

## M1 2026-08-31 (`formatter-probe --corpus 2000` + `m1-bench.js`) — Q-A LOCKED

Corpus: 2000 scenarios of real `PlantUmlCreator` output (18-28 req/resp pairs each, GUID/token/
URL-heavy JSON, per-doc planted needle) = **147.1 MB raw CodeBehind, 122.8 MB normalized** —
beyond the 100MB-class target.

| B | raw | gz | b64 blob | rows dense/sparse/empty | decode |
|---|---|---|---|---|---|
| 32768 | 1.77MB | 0.71MB | **0.94MB** | 6503/1153/25112 | 8ms |
| 65536 | 1.88MB | 0.76MB | **1.02MB** | 6760/1268/57508 | 6ms |

- **Q-A locked: B = 65536**, adaptive dense/sparse by byte-count comparison as implemented.
  Size delta between 32k/64k is 8% on a 147MB corpus; both land at ~1MB b64 — an order of
  magnitude inside the §4.3 budget.
- **Precision finding:** hex/numeric-dominated selective queries (a GUID, a trace id) degenerate
  to candidates≈all — every doc contains every hex trigram, so the discriminating characters
  carry no rare trigram. Intersection stays ~0.1ms; the cost lands on verify
  (decompress-all ≈ 260-490ms in Node zlib over 147MB). English/phrase needles with any rare
  trigram stay selective. The index remains the memory gatekeeper for those; broad/hex queries
  are bounded by the §4.4 verify path, not helped by it.
- **Worst-case revision (feeds M2):** client verify must NORMALIZE decompressed text before
  `includes()` — cost §4.4 ignored. After making the reference normalizer linear (the original
  5b rejoin was accidentally O(n²); now array-join, 10/10 vectors still pass), normalize+load =
  3.1s/147MB single-threaded JS (~1.4ms/doc). Realistic cold decompress-all in-browser is
  therefore **~2-3s on a 123MB-normalized monster**, not 0.5-0.8s — normalized text is
  LRU-cached and the §7 batched progressive reveal is the UX answer; M2 (real Chrome, in the
  E2E perf guard) pins the actual number. Single-pass char-loop normalization is the known
  optimization lever if M2 demands it.

## M2 2026-08-31 (real Chrome — E2E perf guard) — DONE

The large-report render bench (`BrowserRenderWorkerTests.MeasureLargeReport`) now runs a COLD
worst-case broad deep query ("order", hits every JSON body) after the render/toggle phases:
index load + worker spawn + item-metadata collection + decompress-all + normalize + verify,
first use. Measured on the LargeRenderBench fixture (6 diagrams × 40 steps, multi-MB corpus):
**DeepQueryMs = 70ms cold** (recorded per run in `render-bench-results.txt`), budgeted at
2000ms × ContentionScale in the guard. Per-item cost ≈ 1.1ms/blob end-to-end, extrapolating to
**~2-3.5s cold on a 2000-doc monster** — consistent with the M1 revision; the worker keeps the
page responsive, batches reveal progressively, and the normalized-text LRU makes it one-time.

## M3 2026-08-31 (`formatter-probe --m3`) — DONE (§5.1 wall-clock requirement)

`dotnet run --project formatter-probe -c Release -- --m3` times `GenerateHtmlReport` over the
147MB --corpus output with the index off/on, interleaved, plus an isolated phase breakdown.
After three optimization rounds (bitmap trigram collection instead of HashSet inserts — the
dominant cost at ~1 set-insert per corpus char; hand-rolled multi-pass normalizer replacing the
regex passes, equivalence pinned by `SearchNormalizerEquivalenceTests` + shared vectors; flat
counted arrays + parallel chunked row emission in the serializer; `Distinct()` moved onto the
prewarm task):

- Single monster report in isolation: **+~0.15-0.35s on a ~3.1s baseline (~5-10%, within
  run-to-run noise)**; emitted file 59.59 → 60.67 MB (the 1.07MB blob).
- Isolated phases: normalize+hash 259ms (parallel, fully overlapped with body building in real
  runs), serialize 102ms, gzip+b64 Optimal 56ms.
- In a real run both HTML reports share the build cache (`DistinctTextCount` observable pinned
  in `SearchIndexReportTests`), so the second report re-pays only assembly+serialize.
- Typical (few-MB) reports: unmeasurable.

Remaining known lever if ever needed: single-scan fused normalization (rejected for now — the
passes interact; see the SearchNormalizer comment).

## Post-release audit (3.0.71, 2026-08-31)

An adversarial audit of the 3.0.70 execution changed the reference normalization and vectors:

- **Rule 5b opener widened**: `/^[hr]?note(?:<<[^>]*>>)? (left|right|over|across)\b/` with a
  no-`:` guard — the formatter also emits `note<<eventNote>> right` (event captures) and
  `hnote across <<assertionNote>>` (assertion notes), whose chunked/wrapped bodies were never
  rejoined; single-line forms (step delimiters `#black:<color:white>…`, row markers `: Row N`)
  carry a colon and must not enter note mode.
- **Whitespace is JS `/\s/` exactly** on all three sides — .NET `char.IsWhiteSpace` disagrees on
  U+0085 (NEL, .NET-only) and U+FEFF (JS-only), which made the C# index and the browser verify
  corpus diverge on note-body continuation lines starting with those characters.
- **`serializationSparse` vector added** (20 docs, 61 bitset + 78 list rows): the varint-list row
  encoding is only chosen at ≥17 docs and no prior fixture reached it.

Regenerate with `node gen-vectors.js` after any normalize.js change, then port to
`SearchNormalizer.cs` + `report-search-index.js` (the vector tests on both sides enforce this).
