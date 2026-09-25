# Internal-flow segments as one blob (#89)

**Date:** 2026-09-22 (deep dives added the same day; third pass 2026-09-25 against 3.29.6, `ad289f55`,
and the consumer's published reports) · **Repo version:** 3.27.2 (`2843018a`), where the cited line
numbers hold; the harness's `check-anchors.py` re-derives them at any revision ·
**Status: NOT green-lit, nothing implemented.** This is P5 of [`STAGE_1_PLAN.md`](STAGE_1_PLAN.md),
roadmap item 1.9. §10 is the assumption ledger: what was RUN, what was only READ, and what is taken
from the issue. The scripts behind every number are in
[`INTERNAL_FLOW_BLOB_PLAN.harness/`](INTERNAL_FLOW_BLOB_PLAN.harness/README.md), with their output.

Covers GitHub issue **#89**: `window.__iflowSegments` is the one large report block shipped
uncompressed, 62.7% of the reporter's 31.8 MB ClickHouse-lane report. The issue's design is adopted
whole: one gzip-plus-base64 blob, decompressed on the first popup, never per segment. This plan adds
what the issue does not cover and the stage plan asks for: the merge path (§4), the filtered export
(§5), the `puml-data` trap in writing (§3.6), a Kronikol4J ledger entry (§8.4), the consumer's
upgrade and release (§8.6), and what the measurements turned up on the way (§6):

- **More than half of every map is segments no arrow can open, and the cause is a regression** (F8).
  Since 3.15.1 every assertion note and step marker gets a segment of its own: 1,602 of 2,903 on the
  consumer's xUnit run. The fix is one condition in the segment builder and needs no design, so it is
  recommended as a patch of its own, first (R1, §9 Q8). Filed as #100, roadmap item 1.12.
- **The blob alone does not shrink what a visitor to a published report downloads** (§1.5, F13).
  GitHub Pages serves gzip, and a gzip blob in base64 compresses back to about what the raw map
  already compressed to. S1 improves the file on disk, the parse and the heap; the download shrinks
  through R1 and Q7.
- **The map's own activity-diagram islands are gzip inside gzip** and cost a further 2.4x (§3.7, Q7).
  Carrying them raw needs the popup in the page before it renders, or a popup whose diagram is
  already cached draws nothing (F12, measured).
- **Under Server and Local rendering the default `HideLink` is not what the wiki says it is** (F4, S3).

**Why it can land now.** Neither release changes how segments are keyed or how a popup renders, so
#87 (span mixing, stage 5.1) and #86 (dedup, stage 5.3) cannot invalidate it, and the roadmap wants it
before both. It needs no decision. It is a bug fix and performance work, so **two patches**; each
changes report output, so one ledger entry each.

---

## 0. Summary

The segment map is emitted as a classic `<script>` in the **head** of both HTML reports, at
[InternalFlowHtmlGenerator.cs:41](../src/Kronikol/InternalFlow/InternalFlowHtmlGenerator.cs#L41), and
read by two scripts: the popup script takes the whole object at load
([internal-flow-popup-script.js:3](../src/Kronikol/Reports/internal-flow-popup-script.js#L3)), and the
render script consults it, synchronously, every time it binds the arrows of a freshly drawn diagram,
at two lines, one per binding path
([plantuml-browser-render-script.js:1041](../src/Kronikol/Reports/plantuml-browser-render-script.js#L1041)
and [:1085](../src/Kronikol/Reports/plantuml-browser-render-script.js#L1085)). That second reader is
the one design constraint the issue does not mention: lazy decompression must not make arrow binding
asynchronous, or every diagram's links change timing. §3.3 solves it with a membership list the
generator keeps small.

Measured on the consumer's xUnit lane run on 3.29.6 (§1.5, §8.6; 203 scenarios), in Chromium for the
times. Each column is the report rebuilt by the harness, and the S1 columns carry a prototype of S1's
two script changes (harness G):

| | Today | R1 alone (the marker fix) | S1 alone | R1 + S1 | R1 + S1 + Q7 |
|---|---:|---:|---:|---:|---:|
| `TestRunReport.html` | 7,994,701 | 4,767,943 (59.6%) | 2,956,027 (37.0%) | 2,798,451 (35.0%) | 2,451,275 (30.7%) |
| What a GitHub Pages visitor downloads (gzip) | 1,244,075 | 1,126,546 (90.6%) | 1,239,174 (99.6%) | 1,128,499 (90.7%) | 866,062 (69.6%) |
| The segment block | 5,707,861 | 2,481,103 | 667,685 | 510,045 | 162,869 |
| The `<head>`, which the export copies verbatim | 6,196,382 | 2,969,624 | 1,157,708 | 1,000,132 | 652,956 |
| `domContentLoaded`, ms from navigation start | 128 | 94 | 74 | 72 | 71 |
| JS heap after load | 17 MB | 11 MB | 6 MB | 7 MB | 7 MB |
| Click to the first popup's diagram drawn (it pays the one decode) | 15 to 27 ms | 14 ms | 61 ms | 46 ms | 43 ms |

Every later popup appears 1 to 4 ms after its click, in every column. The download row is python's
gzip level 6 of each file, within 2% of what GitHub Pages serves (§1.5). Across the consumer's 18
published reports, the files come to 111.5 MB today, 73.0 MB with R1, and 51.1 MB with R1, S1 and
Q7. What a visitor downloads comes to 21.1 MB today, 19.3 MB with R1, still 21.1 MB with S1 alone,
and 16.6 MB with all three. On the issue's ClickHouse lane, S1 is 31.8 MB to about 13.1 MB by its
own numbers, and lower again with R1 and Q7.

Two releases, both patches, each ending with the consumer upgraded, released and checked (§8.6):

| Release | Slice | What | Bump |
|---|---|---|---|
| **R1** | **S1c** | The marker fix (#100, roadmap 1.12): `InternalFlowSegmentBuilder` gives no segment to a diagram marker or a user action (F8), red unit test and a report-level invariant first | patch |
| **R2** | **S0** | Red-first tests: the end-to-end test that clicks a real arrow (none exists today, F6), the round-trip pin, and the size measurement recorded before the change (done, §1.1, §1.5) | none |
| | **S1** | The blob: emit `<script id="iflow-segments" type="application/json">`, decode on first popup, the popup attached before its diagram renders (F12), the membership list (`has` or `hidden`, whichever is shorter) for synchronous binding, the merge renderer through the same emitter, the legacy-object shim. **S1b**, own commit, if Q7 is taken: the activity-diagram sources inside the map carried raw, since the outer gzip now does their compressing | patch |
| | **S2** | Filtered export: measured, carried whole, one end-to-end test that opens a popup from an export | same release |
| | **S3** | `HideLink` honoured on server-rendered SVG: an `<a>` link with no segment gets no href and opens nothing (F4; under `BrowserJs` the arrow is already hidden). Two lines on top of S1, but its own commit and its own decision (§9 Q2) | same release, if taken |
| | **S4** | Changelog, wiki (including the wrong browser floor, F11), the Kronikol4J ledger entry, version bump on every package | with the release |
| both | **S5** | BreakfastProvider upgraded to the release, released, and checked on its CI and its published site (§8.6) | none in Kronikol |

If Q8 is declined, S1c is the first commit of R2 and S5 runs once.

Not taken (§9): per-segment compression, a prune of the export, `SmallestSize`, an opt-out option,
`Uint8Array.fromBase64`, a `hidden`-only or keys-only list, a filter on the test id, and anything that
touches `puml-data`.

---

## 1. How far each claim was checked

- **RUN**: executed here, output read. **COMPUTED**: arithmetic on RUN numbers. **READ**: the code was
  read and the claim follows. **ISSUE**: taken from #89, #86 or #88, not re-measured here.

| Claim | Level |
|---|---|
| The block is 62.7% of a 31.8 MB report and gzips to 6.1% of itself | **ISSUE** (#89, v3.20.0 reports the reporter holds; not on this machine) |
| On the largest local report the block is 69.9% of 8.15 MB and gzips to 12.1% with .NET's `GZipStream` at `Optimal` | **RUN**, §1.1 |
| Nine local reports: 1.8% to 69.9% of the file, 9.5% to 26.6% of themselves after compression | **RUN**, §1.1 (python zlib level 6; 3.7% smaller than .NET `Optimal` on the one block measured both ways) |
| The activity-diagram islands inside the map are 18% to 22% of its bytes and do not compress again; carried raw inside the blob, the blob is 41.5% of itself on the largest report and about 35% on the 800-segment lanes | **RUN**, §1.1 and §3.7 |
| A PlantUML source set through `innerHTML` in a `data-plantuml` attribute comes back byte-identical, with literal newlines or `&#10;` | **RUN**, Chromium (harness D) |
| V8 parses the 5.7 MB literal in 37 ms; in Chromium the page reaches `domContentLoaded` 57 ms sooner and holds 11 MB less heap without it; the first decode is 47 ms | **RUN**, §1.2. A first attempt read 6 ms for the parse, which was V8's compilation cache returning the previous iteration's result; the number above defeats it |
| The block sits in the `<head>`, before the popup script, and the export copies the head verbatim | **RUN** (byte offsets, §1.3) and **READ** ([ReportGenerator.cs:1232](../src/Kronikol/Reports/ReportGenerator.cs#L1232), [report-export-function.js:83](../src/Kronikol/Reports/report-export-function.js#L83)). Two earlier probes said "not in head": both matched the tag text inside a JavaScript string literal in the head's own scripts |
| 19 of 1,315 distinct arrow links in the newest report's diagrams have no entry in the map; on the five lanes generated on 2026-09-05 it is 535 to 1,184 of 1,323 to 1,564 (40% to 76%) | **RUN**, §1.4 |
| Those links still open a popup, saying there is no data | **READ**, and only where the SVG holds an `<a>`: [internal-flow-popup-script.js:125](../src/Kronikol/Reports/internal-flow-popup-script.js#L125) binds clicks on `<a>` elements only. The P3 session measured on the pinned engine (`DIAGRAM_COLOURS_PLAN.harness/`) that the browser build paints an iflow link as `<text fill="#0000FF">` with **no `<a>`**, so under `BrowserJs` the render script's blue-text path ([plantuml-browser-render-script.js:1058](../src/Kronikol/Reports/plantuml-browser-render-script.js#L1058)) blacks every link out and re-binds only ids in the map: hidden, as the wiki says. The defect is confined to Server and Local rendering with inline SVG. Not yet seen in a browser: S3's first test settles it |
| No end-to-end test clicks an arrow link inside a rendered sequence diagram | **RUN** (grep over `tests/Kronikol.Tests.EndToEnd/`; the popup tests press buttons that call `_iflowShowPopup`) |
| A merge does not re-key segments | **RUN**: `MergeableReportMerger.Id` renames scenario ids only ([MergeableReportMerger.cs:289](../src/Kronikol/Reports/Merge/MergeableReportMerger.cs#L289)); segment keys are `iflow-<RequestResponseId>` |
| Nothing but the two scripts reads the block: not the tool, not the search index, not ingest | **RUN** (grep): the index extracts whole-test-flow text from `data-plantuml-z` and `puml-data`, never from the segment map; `Kronikol.Tool` reads `TestRunReport.json` |
| 1,601 of the newest report's 2,897 map entries are request ids that appear in no scenario of `TestRunReport.json` and not in its background bucket, which reports 0 calls; no report holds a `loop ×N` label | **RUN**, §1.4. The cause was found in the third pass: marker records (F8) |
| 1,602 of the 1,605 segments no diagram links on the consumer's 3.29.6 xUnit run are the run's own marker records (1,594 assertion notes, 8 step markers); in this repo's ReqNRoll example, 65 of 65 | **RUN**: every log of both runs written to a file just before the report was generated (harness F) |
| Marker records have carried a time since 3.15.1, and the segment builder filters on the time alone | **READ** in git: `TrackingDiagramOverride.cs` at `v3.15.0` sets no `Timestamp`; `d9eff2d4` (3.15.1) stamps every log that has none; at 3.29.6 the builder has no marker check, while 21 other places in `src/Kronikol` test `IsDiagramMarker` |
| GitHub Pages serves the consumer's reports gzip-encoded, never brotli, and python zlib level 6 of a file is within 2% of what it serves | **RUN**, `curl` with `Accept-Encoding: br, gzip` and with `br` alone, 2026-09-25 (§1.5) |
| S1 alone leaves the download at 99.5% to 101.0% of today's on the twelve in-memory and docker reports; R1 alone takes it to 87.7% to 92.8%, and R1 with S1 and Q7 to 68.5% to 82.0% | **RUN**, `variants.py` over the 18 published reports (§1.5) |
| Every variant binds exactly the arrows today's report binds | **RUN** from the data on all 18 published reports (`variants.py`), and in Chromium on the consumer's 3.29.6 report: up to 8 diagrams each, the same count of bound link text in today's page and in every prototype of S1 (harness G) |
| With Q7's raw sources and today's popup order, a popup whose activity diagram is already in the render cache draws nothing | **RUN**, Chromium: 3 of 8 popups on the prototype, all three cache hits; 8 of 8 once the popup is attached before it renders (F12) |
| The consumer restores and passes on 3.29.6 | **RUN**: a clone of BreakfastProvider `0d29537` with its 30 pins at 3.29.6, `dotnet restore` clean, the xUnit lane in memory 203 of 203 (§8.6) |
| The component diagram's `[[#iflow-rel-…]]` links and the relationship popup are produced by no report the product writes | **RUN** (grep): every `GeneratePlantUml` call in `src/` omits `stats`, and `BuildRelationshipSegments` / `GenerateRelationshipPopupContent` have no caller outside tests |
| `DecompressionStream`: Chrome 80, Firefox 113, Safari 16.4 | **RUN**: `mdn/browser-compat-data` `api/DecompressionStream.json`, fetched 2026-09-22 (Edge, Android and iOS mirror) |
| The wiki's `SeparateFragments` remedy, `InternalFlowDisplay.Inline` and `InternalFlowTrigger.Hover` are never read by any code | **RUN** (grep over `src/`): declared on `ReportConfigurationOptions`, no other reference |

### 1.1 Size

`GZipStream` at each level on the 5,699,628-byte block, .NET 10.0.12, this machine:

| Level | gzip | base64 | Of the raw block | Time |
|---|---:|---:|---:|---:|
| `Optimal` (the report's convention) | 517,825 | 690,436 | 12.1% | 24 ms |
| `SmallestSize` | 506,485 | 675,316 | 11.8% | 29 ms |
| `Fastest` | 715,831 | 954,444 | 16.7% | 4 ms |

Generation cost: 24 ms here, so about 150 ms on the issue's 35 MB lane (COMPUTED, linear), and
twice that when the Specifications report starts on a different internal-flow tab, because
[ReportGenerator.cs:308](../src/Kronikol/Reports/ReportGenerator.cs#L308) builds the script once per tab.

Every internal-flow report on disk (`measure.py`; python zlib level 6, base64, 3.7% under .NET
`Optimal`; "raw sources" is Q7, §3.7). The 2026-09-05 lanes were written by an earlier 3.x, the xUnit
runs by 3.27.0:

| Report | Generated | File | Block | Of file | Segments | Blob | Blob, raw sources | File after (S1 / with Q7) |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| BreakfastProvider xUnit, kept run 08:07 | 2026-09-22 | 8,151,281 | 5,699,628 | 69.9% | 2,897 | 666,116 (.NET 690,436) | 268,748 | ~3.14 / ~2.73 MB |
| BreakfastProvider xUnit, kept run | 2026-09-19 | 6,492,959 | 3,970,682 | 61.2% | 2,295 | 480,672 | 189,168 | ~3.00 / ~2.71 MB |
| BreakfastProvider BDDfy | 2026-09-05 | 3,815,269 | 1,242,129 | 32.6% | 804 | 255,300 | 86,052 | ~2.83 / ~2.66 MB |
| BreakfastProvider NUnit | 2026-09-05 | 3,289,565 | 1,247,071 | 37.9% | 807 | 221,072 | 76,076 | ~2.26 / ~2.12 MB |
| BreakfastProvider LightBDD | 2026-09-05 | 4,042,187 | 1,214,336 | 30.0% | 789 | 277,940 | 90,724 | ~3.11 / ~2.92 MB |
| BreakfastProvider ReqNRoll | 2026-09-05 | 3,898,460 | 1,167,623 | 30.0% | 765 | 261,376 | 84,948 | ~2.99 / ~2.82 MB |
| BreakfastProvider TUnit | 2026-09-05 | 2,501,389 | 613,418 | 24.5% | 383 | 122,644 | 40,296 | ~2.01 / ~1.93 MB |
| Example.Api CiPreview.Mixed | on disk | 517,170 | 9,061 | 1.8% | 7 | 1,944 | 1,096 | ~0.51 MB either way |
| Example.Api ReqNRoll.xUnit3 | on disk | 704,314 | 125,134 | 17.8% | 88 | 11,852 | 6,628 | ~0.59 MB either way |

What the block is made of, every report alike: segment `content` HTML 48% to 59%, `flameData` (raw
JSON, already compressible) 10% to 30%, the `data-plantuml-z` islands inside `content` 18% to 22%.
Every `content` string is distinct, because it embeds the segment's id and its flame data; the
islands repeat: 2,897 of them draw 656 distinct activity diagrams on the largest report.

The two xUnit runs differ by 1.7 MB of file for 600 more segments: the same scenarios, more spans
per request. That is #86's growth curve, and compression flattens it (both land near 3 MB).

### 1.2 Time

Chromium (the E2E project's Playwright driver, `load-timing.js`), the 8.15 MB report against a copy
carrying the blob, every network request aborted so the file alone is measured, median of 7 loads:

| Metric (ms from navigation start) | Today | After |
|---|---:|---:|
| `responseEnd` | 37 | 11 |
| `domInteractive` = `domContentLoadedEventStart` | 139 | 82 |
| `loadEventStart` | 500 | 447 |
| JS heap after load | 17 MB | 6 MB |

The popup script's decode path on that page: the first call 36.6 ms of `DecompressionStream` and
10.0 ms of `JSON.parse`, later calls 13 to 18 plus 8 to 13 ms. Each decoded copy held adds about
11 MB of heap, which is what the literal costs at load today: memory-neutral once a popup has opened,
11 MB lighter before. The popup itself then renders an activity diagram in the browser, which takes
longer than the decode by an order of magnitude, so the 47 ms is not visible in the interaction.

Node 25.9 (`node-timing.mjs`), the same block, median of five: V8 parse and evaluation of the
literal 37 ms with the compilation cache defeated (5 ms with it, the trap); `JSON.parse` of the same
text 11.6 ms; `atob` and the helper's byte loop on 666 KB of base64 1.0 ms; `Uint8Array.fromBase64`
0.1 ms; `zlib.gunzipSync` 9.8 ms; the full `DecompressionStream` path 27, 30 and 84 ms across three
runs, Node's web-streams being noisy. The Chromium numbers above are the ones that count.

The issue measured the 20 MB payload at 109 ms today against 55 to 113 ms proposed. All three agree
on the shape: the decode costs about what the parse costs, and moves from every load to the first
popup.

### 1.3 Placement

Byte offsets in the xUnit report: the segment script starts at 467,921; the popup script that reads
it at 6,174,333; the real `</head>` at 6,180,278; `puml-data` is in the body. The head is 6.18 MB of
an 8.15 MB file, and 5.7 MB of that is this block. `export_html` copies `head.innerHTML` unchanged,
so today's export carries the whole raw map (§5). The same map is in `Specifications.html` when that
report is written (it is blank by rule on a run with a failure, as this one was).

### 1.4 Links against keys

Over the 409 diagram sources in `puml-data` (1,284,123 characters, decoded in 5 ms, scanned for
`[[#iflow-…` in 1.0 ms): 1,315 distinct link ids, 1,296 in the map, **19 not in the map**. The map
holds 2,897 keys, so **1,601 keys are linked from no diagram**. Every key has the form
`iflow-<guid>`, no `-res` suffix. Per report (harness A):

| Report | Distinct links | Links with no map entry | Map keys | Keys no diagram links |
|---|---:|---:|---:|---:|
| xUnit 2026-09-22 | 1,315 | 19 | 2,897 | 1,601 |
| xUnit 2026-09-19 | 974 | 18 | 2,295 | 1,339 |
| BDDfy, NUnit, LightBDD, ReqNRoll (2026-09-05) | 1,323 to 1,347 | 535 to 583 | 765 to 807 | 3 |
| TUnit 2026-09-05 | 1,564 | 1,184 | 383 | 3 |
| Example.Api CiPreview.Mixed | 7 | 0 | 7 | 0 |
| Example.Api ReqNRoll.xUnit3 | 23 | 0 | 88 | 65 |

The 1,601 on the newest run are request ids that appear in **no** scenario's `httpInteractions` in
`TestRunReport.json`, whose `background` bucket reports 0 calls; the run has 203 scenarios with 203
distinct ids, no second attempt, every scenario's own ids all linked and all in the map. Their
activity diagrams carry the same labels as the linked segments' (367 labels shared, 2 only in the
unlinked set). No report holds a `loop ×N` label (`CollapseConsecutiveIdenticalCalls` defaults to
off), so the collapse explanation is out. They looked like scenario calls under test ids no scenario
owns. The third pass found what they are: segments built for the run's own marker records, whose
windows hold the spans of the calls around them (§6 F8).

### 1.5 On the wire, and on the consumer's published site

BreakfastProvider publishes 18 reports on GitHub Pages, one per lane
(`lemonlion.github.io/BreakfastProvider/reports/…`), generated by 3.29.0 in the nightly CI run of
2026-09-25. Pages serves them gzip-encoded: asked for `br, gzip` it answers gzip, and asked for `br`
alone it sends the file uncompressed. So what a visitor downloads is the file's gzip, and python's
level 6 came within 2% of what Pages served (1,278,471 against 1,300,236 bytes for the xUnit report).
`variants.py` rebuilds each variant from the published file. It serialises the map exactly as
`System.Text.Json` does (checked byte for byte against all 18 blocks), and it checks that each variant
binds exactly the arrows today's report binds. All variants do, on all 18 reports.

| Reports | Today: files / downloaded | R1 alone | S1 alone | R1 + S1 | R1 + S1 + Q7 |
|---|---:|---:|---:|---:|---:|
| In memory (6) | 50.0 / 7.55 MB | 29.8 / 6.73 | 20.2 / 7.53 | 19.1 / 6.75 | 17.1 / 5.29 |
| In docker (6) | 49.2 / 10.94 MB | 31.3 / 10.00 | 25.1 / 10.94 | 23.9 / 10.04 | 22.1 / 8.70 |
| External SUT (6) | 12.3 / 2.64 MB | 11.9 / 2.58 | 11.9 / 2.64 | 11.9 / 2.58 | 11.9 / 2.58 |
| All 18 | 111.5 / 21.13 MB | 73.0 / 19.32 | 57.2 / 21.11 | 54.8 / 19.37 | 51.1 / 16.57 |

Per report, on the twelve in-memory and docker lanes:

| Variant | File | Download |
|---|---:|---:|
| R1 alone | 56.8% to 65.7% | 87.7% to 92.8% |
| S1 alone | 38.2% to 54.0% | 99.5% to 101.0% |
| R1 + S1 + Q7 | 31.8% to 48.3% | 68.5% to 82.0% |

On these twelve lanes the map is 53% to 70% of the file, and 55% to 68% of its segments are linked
from no diagram. The two TUnit lanes download slightly more under S1, because 499 and 531 dataless
links make their `hidden` lists long. The "R1" variant drops every unlinked segment, which is R1
plus the three consumer calls per lane that F8 describes.

Why S1 alone does not move the download: the map is text that gzip takes to about 12% of itself. S1
compresses it the same way and then base64-encodes it, and the outer gzip takes that back to within a
few percent of the same bytes. S1 moves the file itself, wherever it is not compressed on the way: a
report opened from disk, an unzipped CI artifact, an export saved and sent on. It also saves the
parse (about 55 ms of `domContentLoaded`) and 11 MB of heap. What moves the download is carrying
less. R1 drops the segments no arrow opens, and Q7 drops the gzip islands that no second compression
can shrink. The consumer's CI artifacts are zipped (`upload-artifact`, deflate): the
`xunit-in-memory-report` artifact is 3.2 MB for a directory holding two 8 MB HTML files, so an
artifact behaves like the download column, not the file column.

On the external-SUT lanes the SUT runs in its own process, so its spans are not captured. On five of
those lanes no arrow has a segment (356 to 372 dataless links per report), and on TUnit's 10 of 372
do. Each map's 22 to 60 segments are all unlinked, apart from those 10. After R1, five of the six maps
are empty and the report carries no element at all (§3.1). That the unlinked segments there are
markers, as on the in-memory lane, is inferred, not run.

`Specifications.html` carries the same map and is published beside each lane's report when the run
passes (1.29 MB downloaded for the xUnit lane), so the site holds the map twice for each passing lane.

---

## 2. Where the payload lives today

| | Where | What it does |
|---|---|---|
| Emit, live path | [ReportGenerator.cs:308](../src/Kronikol/Reports/ReportGenerator.cs#L308) `BuildFlowScript` | `GetInternalFlowConfigScript` + `GenerateSegmentDataScript`, built once, or twice when the Specifications report starts on a different tab; handed to both `GenerateHtmlReport` calls as a string. The diagrams are in scope (`diagrams`, a `DiagramAsCode[]` whose `CodeBehind` is the PlantUML), and so is the component diagram's source when it is embedded ([:390](../src/Kronikol/Reports/ReportGenerator.cs#L390), [:419](../src/Kronikol/Reports/ReportGenerator.cs#L419)) |
| Emit, merge path | [MergeableReportRenderer.cs:38](../src/Kronikol/Reports/Merge/MergeableReportRenderer.cs#L38) | Boxes the JSON file's `JsonElement` values and calls `WrapSegmentData`; `report.Diagrams` (`DiagramAsCode[]`) and the component diagram source ([:46](../src/Kronikol/Reports/Merge/MergeableReportRenderer.cs#L46)) are in scope |
| The wrapper | [InternalFlowHtmlGenerator.cs:38](../src/Kronikol/InternalFlow/InternalFlowHtmlGenerator.cs#L38) | `<script>window.__iflowSegments = {json};</script>`, `System.Text.Json` with the HTML-safe default encoder |
| The map | `BuildSegmentData`, same file | `key → { title, content, flameData? }` or `{ message }`; segments with no spans are dropped under `HideLink` ([:113](../src/Kronikol/InternalFlow/InternalFlowHtmlGenerator.cs#L113)). `content` is the activity diagram as a `plantuml-browser` div whose source is gzip+base64 in `data-plantuml-z` ([:163](../src/Kronikol/InternalFlow/InternalFlowHtmlGenerator.cs#L163)), or the `CallTree` HTML; `flameData` is raw JSON |
| The map's input | [ReportGenerator.cs:298](../src/Kronikol/Reports/ReportGenerator.cs#L298), [InternalFlowSegmentBuilder.cs:25](../src/Kronikol/InternalFlow/InternalFlowSegmentBuilder.cs#L25) | Every tracked log in the process (`runLogs` minus `TrackingIgnore`), grouped by `TestId`, one segment per request record with a time, marker records included (F8); the data file lists a scenario's logs by `TestId == scenario id` ([:4053](../src/Kronikol/Reports/ReportGenerator.cs#L4053)) and its background bucket counts the unknown identity only ([BackgroundAttribution.cs:124](../src/Kronikol/Reports/BackgroundAttribution.cs#L124)) |
| Placement | [ReportGenerator.cs:1232](../src/Kronikol/Reports/ReportGenerator.cs#L1232) | In the head, after the flame-chart script, before the popup script. The popup script is emitted only with tracking on ([:1169](../src/Kronikol/Reports/ReportGenerator.cs#L1169)); the render script whenever `BrowserJs` is |
| Reader 1 | [internal-flow-popup-script.js:3](../src/Kronikol/Reports/internal-flow-popup-script.js#L3) | `var iflowData = window.__iflowSegments || {}` at load; `showPopup(id)` reads `iflowData[id]` synchronously ([:21](../src/Kronikol/Reports/internal-flow-popup-script.js#L21)); the diagram inside is rendered from `data-plantuml` when present, else from `data-plantuml-z` through `decompressGzipBase64` ([:72](../src/Kronikol/Reports/internal-flow-popup-script.js#L72)); a document-level capture-phase click handler opens a popup for any `<a>` whose href starts with `#iflow-` ([:125](../src/Kronikol/Reports/internal-flow-popup-script.js#L125)) |
| Reader 2 | [plantuml-browser-render-script.js:1031](../src/Kronikol/Reports/plantuml-browser-render-script.js#L1031) `bindIflowLinks` | Reads `window.__iflowSegments` at call time ([:1033](../src/Kronikol/Reports/plantuml-browser-render-script.js#L1033)). The `<a>` path skips a link with no entry ([:1041](../src/Kronikol/Reports/plantuml-browser-render-script.js#L1041)); the blue-text path blacks out every blue text node and re-binds a group only when `extractIflowMap(source)` names an id with an entry ([:1085](../src/Kronikol/Reports/plantuml-browser-render-script.js#L1085)). Called from six sites in `collapsible-notes-script.js` after fragment re-renders |
| JSON data file | [ReportGenerator.cs:4479](../src/Kronikol/Reports/ReportGenerator.cs#L4479) | `internalFlowSegments`, the plain map, only with `GenerateMergeableData`; read back by `MergeableReportReader` as `JsonElement` per key; documented at [MergeableReport.cs:42](../src/Kronikol/Reports/Merge/MergeableReport.cs#L42) as consumed "as `window.__iflowSegments`" |
| Export | [report-export-function.js:83](../src/Kronikol/Reports/report-export-function.js#L83) | Copies `head.innerHTML`, the visible features, and the body's data scripts with `puml-data` pruned by key |
| The decompressor | [report-decompress-helper.js](../src/Kronikol/Reports/report-decompress-helper.js) | `decompressGzipBase64`: `atob`, byte loop, `DecompressionStream('gzip')`, `Response.text()`. Included unconditionally in the head and prepended to every public script accessor |
| Test emitters | [TestPageGenerator.cs:63](../tests/Kronikol.Tests.EndToEnd/TestPageGenerator.cs#L63) and [:238](../tests/Kronikol.Tests.EndToEnd/TestPageGenerator.cs#L238) | The popup page uses the real emitter; the component-flow page writes `window.__iflowSegments = {…}` by hand, for a relationship popup no report produces (F10) |
| Kronikol4J | `InternalFlowHtmlGenerator.java:36`, `renderActivityDiagramInline` at `:92`, `DotNetHtmlReportRenderer.appendHead` | The same string, `CompactJson`; the popup script is a byte-identical copy of .NET's; `InternalFlowHtmlGeneratorTest:40` byte-compares the script against a .NET golden captured in `CallTree` style precisely because that style has no gzip island, and `:53` pins `data-plantuml-z="` on the inline div |

Nothing else reads it. The search index takes its whole-test-flow text from `data-plantuml-z`
and `puml-data` ([ReportGenerator.cs:2147](../src/Kronikol/Reports/ReportGenerator.cs#L2147)), never from
the popup map; the tool reads the JSON file; ingest renders through the core writer with no spans.

---

## 3. The design

### 3.1 The emitted form

```html
<script id="iflow-segments" type="application/json">{"hidden":["iflow-…"],"z":"H4sIAAAA…"}</script>
```

or, when that is the shorter list, `{"has":["iflow-…", …],"z":"…"}`.

- `z`: the map serialised exactly as today (`JsonSerializer.Serialize(data, WriteIndented = false)`,
  the HTML-safe default encoder, so `<` is `<` and the decoded text is byte-for-byte today's
  literal), then `GZipStream` at `Optimal`, then base64. The same `CompressToBase64` every other
  island in the report uses, so the Java port's `GzipBase64` matches by construction and the one
  convention stays one.
- `has` or `hidden`: the membership list (§3.3). `hidden` is every link id in the report's diagrams
  that has no map entry; `has` is every map key some diagram links. The generator emits whichever is
  shorter. On the measured reports that is 19 ids on the newest run and 380 to 583 on the
  2026-09-05 lanes: 0.9 KB to 27 KB at 46 bytes an id, against blobs of 123 KB and up.
- `type="application/json"`: the browser neither parses nor executes it. This is the idiom
  `kron-search-index` already uses (`JSON.parse(script.textContent)` in
  [report-search-index.js:523](../src/Kronikol/Reports/report-search-index.js#L523)).
- It stays in the head, where it is now. Moving it to the body would change nothing the export does
  with it (§5) and would change the template for no gain.
- **No segments, no element.** Today a report with tracking on and no spans emits
  `window.__iflowSegments = {};`. After this, nothing; the scripts treat an absent element as an
  empty map, in which no id has a segment. `ComponentDiagramReportTests:333` moves its
  `DoesNotContain` to the new id.

`GenerateSegmentDataScript` and `WrapSegmentData` keep their public signatures and their names; only
what they return changes. The membership list needs the diagram sources, which the public wrapper
does not have, so it is computed by an **internal** overload called from the two emit sites. The
public wrapper emits `has` holding every key: exact, and today's behaviour for a hand-built page, so
nothing a caller of the public API sees changes but the bytes. No new public member, so no minor
bump. The XML doc comments on both public members and on `MergeableReport.InternalFlowSegments` are
rewritten in the same commit (the 3.22.1 lesson: stale public doc comments ship).

### 3.2 The decode convention

This is the convention 8.2 (#90, coverage) is to reuse, so it is stated once:

> A large payload the page needs only on demand is one `<script id="…" type="application/json">`
> holding a JSON object whose `z` field is the gzip-plus-base64 of the payload and whose other
> fields are whatever the page must know **before** decoding. A single function returns a memoised
> promise of the decoded value: the element is read and decompressed on the first call, through
> `decompressGzipBase64`, never at load, never per item. Absence of the element is the empty value.
> Failure to decompress is reported in the place the value would have been shown, naming the cause.

In the popup script:

```js
var _index = null;                          // the element's fields other than the map, read once
function readIndex() {
    if (_index) return _index;
    var el = document.getElementById('iflow-segments');
    var payload = el ? JSON.parse(el.textContent) : {};   // one object holding one long string: under a millisecond
    return (_index = { has: payload.has ? new Set(payload.has) : null,
                       hidden: new Set(payload.hidden || []), z: payload.z || null });
}
var _segments = null;                       // Promise<map>, created on first use
function loadSegments() {
    if (_segments) return _segments;
    var legacy = window.__iflowSegments;   // a hand-built page or an external composition
    if (legacy && typeof legacy === 'object') return (_segments = Promise.resolve(legacy));
    var z = readIndex().z;
    if (!z) return (_segments = Promise.resolve({}));
    return (_segments = decompressGzipBase64(z).then(JSON.parse));
}
function hasSegment(id) {                   // synchronous, exact for every id a diagram can present
    var legacy = window.__iflowSegments;
    if (legacy && typeof legacy === 'object') return id in legacy;
    if (!document.getElementById('iflow-segments')) return false;
    var ix = readIndex();
    return ix.has ? ix.has.has(id) : !ix.hidden.has(id);
}
```

Exposed for tests and for the render script: `window._iflowLoadSegments` (the promise) and
`window._iflowHasSegment(id)` (§3.3). `window._iflowShowPopup` keeps its name.

### 3.3 Membership without decoding

`bindIflowLinks` runs synchronously inside the render pipeline and asks, per link, "is there a
segment for this id?", on both of its paths: the `<a>` path for server-rendered SVG, and the blue-text
path that is the live one under `BrowserJs` (the P3 session measured that the browser build emits no
`<a>`), which blacks out every blue text node and re-binds only the ids in the map, in the same tick
as the render. Making that asynchronous would delay the hover-only styling and the click
binding of every first diagram by the decode time, and would put a promise into six call sites in
`collapsible-notes-script.js`.

The exact answer needs only the ids a diagram can present, and the generator has both sides of it: it
scans every PlantUML source the page embeds for `[[#iflow-…` ids (the sequence diagrams, plus the
component diagram when it is embedded, whose `[[#iflow-rel-…]]` links exist in the code even though
no report produces them today, F10), and it holds the map's keys. `hidden` is the links with no key;
`has` is the keys with a link. Each is exact; the generator emits the shorter. The first version of
this plan proposed `hidden` alone, "small by nature, under 1 KB": true on the newest run (19 ids)
and false on the 2026-09-05 lanes, where 40% to 76% of the arrows have no segment and `hidden` alone
would be 25 to 54 KB, more than the TUnit lane's whole blob. The shorter of the two is bounded by
half the links. On the seven BreakfastProvider reports of §1.1 it is 19 to 583 ids, and on the 18
the consumer published on 2026-09-25 it is 0 to 531. One regex pass over
text the generator already holds in memory: 1.0 ms over 1.28 MB of diagram text here, so tens of
milliseconds on the issue's 35 MB lane. The same function serves both emit sites, because both have
the diagrams (`diagrams` on the live path, `report.Diagrams` on the merge path), and a merge does
not re-key segments (§1). The alternative, collecting the dropped keys inside `BuildSegmentData`, is
free on the live path but does not exist on the merge path; two mechanisms for one list is how they
drift.

`_iflowHasSegment(id)`, in §3.2: with a legacy object, `id in object`; with no element, `false` (the
empty map); with `has`, membership; otherwise `!hidden.has(id)`. Every id a diagram can present is
in the map or in the list, by construction, so the answer is exact and synchronous, and
`bindIflowLinks` keeps its shape: its two membership lines (`:1041` and `:1085`) become
`if (!window._iflowHasSegment || !window._iflowHasSegment(segId)) return;`, guarded because the
popup script that defines it is emitted only when tracking is on, while the render script is emitted
whenever `BrowserJs` is; with tracking off the diagrams carry no links, so the guard is never the
difference.

### 3.4 The popup

`showPopup(id)` builds the overlay, the close button and the header, and attaches them to the page,
**before** decoding, so the click is answered at once and a test can wait for `.iflow-popup` as it
does now. Then `loadSegments().then(fill)`. A `Loading…` line stands in the diagram's place until the
promise resolves. On the first popup that is 25 to 37 ms on the consumer's report (harness G) and
about 100 ms on the issue's lane; after that there is no wait. A
missing id shows the existing `iflow-no-data` message. A rejected promise (no `DecompressionStream`,
a corrupt blob) shows, in the same box, `Internal flow data could not be decompressed: <reason>`,
and the console gets the error. Today there is no failure mode at all, so this is new text, and the
end-to-end suite pins it by handing the page a blob that is not gzip.

Toggle wiring, flame-chart rendering and the PlantUML render of the activity diagram move inside
`fill`, unchanged. With Q7 the activity diagram's source is already in `data-plantuml`, the branch the
popup script takes first ([:72](../src/Kronikol/Reports/internal-flow-popup-script.js#L72)), so the
per-popup island decode disappears too; the DOM after the render is what it is today, because the
decoded source is written back into `data-plantuml` today anyway.

**The popup is in the page before its diagram renders: a requirement, measured (F12).** Today's
`showPopup` renders the activity diagram first and attaches the overlay last. That works only because
the island's decode puts a task between the two. The render shim resolves its target element when it
is called (`shimRender` in the render script), and on a render-cache hit it writes the SVG straight
away, into an element that is not yet in the page. With the islands decoded that never happens. With
Q7's raw sources it happens on every popup whose activity diagram has been drawn before. On the
prototype (harness G), 3 of 8 popups drew nothing, and all three were cache hits; with the popup
attached first, 8 of 8 drew, the cache hits in 2 to 3 ms. S1 therefore ships the order above, and
S1b's end-to-end test opens two popups that share an activity diagram.

### 3.5 What this asks of browsers

The popup gains a hard dependency on `DecompressionStream`: Chrome 80, Firefox 113, Safari 16.4,
per `mdn/browser-compat-data` (RUN). `BrowserJs` diagrams, the deep-search index and the context
menu already require it, so for the default configuration nothing changes. The one configuration
that worked without it, `CallTree` popups under Server or Local rendering, now needs it too.
Recorded in the changelog and the wiki; the degradation is the message in §3.4, not a blank box.
The wiki's browser-floor sentence is wrong today (F11) and S4 corrects it.

### 3.6 The `puml-data` trap, in writing

The two blocks look alike and the reasoning does not transfer. `puml-data` holds every diagram
gzipped **separately**, and that is load-bearing three times over: `getPumlZ(el)` decompresses one
diagram when it scrolls into view, so most diagrams of a large report are never decoded at all;
`export_data_scripts` prunes the block by key, so a filtered export carries only the diagrams it
shows; and `DecompressionStream` has no preset dictionary, so there is no way to share a dictionary
across diagrams while keeping keyed access. Re-streaming it as one blob would save 919 KB on the
issue's BigQuery lane (#88) and cost all three properties, decoding 35 MB on the first diagram.

The segment map has no keyed reader: nothing asks for one segment without the map, and pruning it
by key is what §5 declines. So the trade runs the other way here, and **only here**. Compressing the
12,021 segments individually would also lose the shared dictionary that gives the 6.1%: the issue
measured that dedup alone (#86) reaches 13.0% where compression alone reaches 6.1%, because
duplicate segments sit next to each other inside gzip's 32 KB window.

### 3.7 What the blob holds: the islands inside it (Q7)

Each segment's `content` carries its activity diagram as a `plantuml-browser` div with the PlantUML
gzipped and base64'd in `data-plantuml-z` ([InternalFlowHtmlGenerator.cs:163](../src/Kronikol/InternalFlow/InternalFlowHtmlGenerator.cs#L163)).
That was the right shape for a map that sat raw in the page and decoded one island per popup. Inside
one gzip stream it is the wrong one: gzip cannot shrink gzip, base64 has inflated it by a third, and
the islands are 18% to 22% of the map's bytes on every report on disk. Measured (harness A): with the
islands replaced by the PlantUML they hold, in a `data-plantuml` attribute, the blob is 268,748
bytes instead of 666,116 on the largest report (41.5%), 86,052 instead of 255,300 on the BDDfy lane
(33.7%), 40,296 instead of 122,644 on TUnit (32.9%). The file: ~2.73 MB instead of ~3.14 MB; the
head the export copies: ~760 KB instead of ~1.17 MB.

What it takes: one branch in `RenderActivityDiagramHtml` for content destined for the map, emitting
`data-plantuml="<HTML-attribute-escaped source>"` (`&`, `"` and `<` escaped; newlines may stay
literal, RUN in Chromium, harness D) instead of `data-plantuml-z`. Both readers already take
`data-plantuml` first: the popup script at [:72](../src/Kronikol/Reports/internal-flow-popup-script.js#L72),
the render script's `enqueueElement` at [:1120](../src/Kronikol/Reports/plantuml-browser-render-script.js#L1120),
and the popup skips a decode it does today. The first version of this plan concluded "no script
changes". That holds only with §3.4's order, which S1 brings: under today's order a popup whose
activity diagram is already in the render cache draws nothing (F12, measured). So S1b must not ship
ahead of S1's popup, or without the end-to-end fact that pins the order. The whole-test-flow content
(`RenderWholeTestActivityDiagramHtml`) and the dead relationship popup keep `data-plantuml-z`: they
sit raw in the body, where per-diagram lazy decoding and the search index's
[`data-plantuml-z` scan](../src/Kronikol/Reports/ReportGenerator.cs#L2152) depend on it. `CallTree`
content has no island and is untouched. In the mergeable JSON the map's `content` strings change
shape, and a merge of shards from either side of the release renders both, since the popup accepts
both attributes. Kronikol4J mirrors the branch in `renderActivityDiagramInline` (`:92`) with an
attribute escaper (its `CiSummaryGenerator.escapeHtml` is private today), and its
`InternalFlowHtmlGeneratorTest:53` pin moves from `data-plantuml-z="` to `data-plantuml="`.

Why it is a question and not the design: #89 asks for the blob, and this is a second change to the
map's bytes with its own Java parity cost. It is recommended (Q7) as S1b, its own commit in the same
release, because the islands were compressed for exactly the per-popup decode S1 makes redundant,
and shipping S1 without it leaves 59% of the blob as dead weight that a later release would have to
change the map again to recover.

---

## 4. The merge path

The mergeable JSON keeps carrying `internalFlowSegments` as the plain map. It is read into
`JsonElement`s, merged by key (`MergeMap`), renamed only where a scenario id is renamed (which a
segment key never is), and serialised back verbatim. None of that reads HTML, so nothing "reads the
block back": the merge renderer builds the HTML from the map, through the same internal overload as
the live path, with `report.Diagrams` supplying the link ids for the membership list. Compressing
the map inside the JSON file is #85's job (a schema change, `$defs/compressed`, `payloadEncoding`,
behind an option) and is not touched here.

What changes in [MergeableReportRenderer.cs:31](../src/Kronikol/Reports/Merge/MergeableReportRenderer.cs#L31):
the call becomes the internal overload with `report.Diagrams.Select(d => d.CodeBehind)`
(`DiagramAsCode` is `(TestRuntimeId, ImgSrc, CodeBehind)`,
[DefaultDiagramsFetcher.cs:446](../src/Kronikol/DefaultDiagramsFetcher.cs#L446)) plus the component
diagram source the renderer builds at `:46`. What is tested: a shard JSON holding a segment whose id
a diagram links, merged with a second shard, rendered, opened in a browser; the popup shows the
segment's content. Today's `MergeableReportTests:360` asserts the substring `__iflowSegments`, which
the new popup script still contains (the legacy check in §3.2), so that assertion would keep passing
while proving nothing. It is replaced by an assertion on the emitted element,
`<script id="iflow-segments"`, the 3.0.82 rule.

---

## 5. Filtered export

`export_html` copies the head verbatim. Today that means every export carries the whole raw map,
5.7 MB of a 6.2 MB head on the measured report, whatever filter was applied. After S1 it carries the
blob and the list, 690 KB; with Q7, about 280 KB. With R1 first, the consumer's 3.29.6 report carries
510 KB (163 KB with Q7; §0). R1 alone already halves what every export carries, 5.7 MB to 2.5 MB,
before the blob exists. Options:

| | Bytes in the export | Cost |
|---|---:|---|
| Carry whole (recommended) | 690 KB here (280 KB with Q7), ~1.2 MB on the issue's lane | none; the head is copied as it is |
| Prune by key, like `puml-data` | the segments the export's diagrams link, probably a few KB | decode the whole map, drop keys, `JSON.stringify`, `CompressionStream`, base64, in the browser, on every export; a second implementation of the map's semantics; the membership list must be pruned to match |

Carry whole. The export was 5.7 MB heavier than it needed to be yesterday and nobody noticed; a
blob in a file that also carries the render engine loader and every visible diagram's source is not
the export's problem. If a filtered export is ever pruned it should prune the head's other
payloads too, and that is a plan of its own. S2 is the measurement above, recorded in the changelog,
and one end-to-end test: export a filtered report, open the export in a fresh page, click an arrow,
see the popup. That test does not exist today for the raw map either;
`ExportFilteredHtmlRenderingTests` already captures the download and opens it, so it is the home.

---

## 6. Findings the issue does not state

| # | Finding | Where it goes |
|---|---|---|
| F1 | The block is in the head, so today's export already carries the whole map. Carrying the blob whole is a 30x improvement on what ships, not a compromise | §5 |
| F2 | The render script needs synchronous membership, which the issue's "decompress on first popup" would break. A list the generator keeps to the shorter of two exact sets solves it at 1 to 27 KB | §3.3 |
| F3 | Compression alone removes most of #86's redundancy from the file: 2,897 segments draw 656 distinct activity diagrams (4.4x), every `content` string is nonetheless distinct because it embeds the segment's id and flame data, and the block still lands at 12.1% | §3.6; stage 5.2 re-measures #86 after this |
| F4 | **Under Server and Local rendering with inline SVG, the default `HideLink` leaves the arrow a link.** The generator emits `[[#iflow-<id>]]` for every tracked request before the segments exist, and `BuildSegmentData` drops the empty ones. Under `BrowserJs` that is enough: the engine emits no `<a>` (measured by the P3 session), so the render script's blue-text path blacks every link out and re-binds only ids in the map, and the arrow is plain text. A Java-rendered SVG carries `<a href="#iflow-…">`; `bindIflowLinks` skips an `<a>` with no entry, which only withholds the hover class, and the popup script's document-level click handler then opens a popup for it, saying "No internal flow data available for this segment." The wiki says the arrow "is not clickable". The weight: 19 such links on the newest report, but 535 to 1,184 of 1,323 to 1,564 (40% to 76%) on the 2026-09-05 lanes, so under those two modes most arrows of those reports would open an empty popup. The two modes are the ones v4 deletes (12.1). READ, to be confirmed by S3's first test | S3 |
| F5 | The popup has no failure mode: a segment that cannot be shown is a blank box. After S1 it has one and it says why | §3.4 |
| F6 | No end-to-end test clicks a real arrow. Every popup test presses a button that calls `_iflowShowPopup`. The path S1 changes most, link click to fallback handler to decode to popup, is untested on `main` | S0 |
| F7 | Three documented options are inert. `InternalFlowContentStrategy.SeparateFragments` (the wiki's remedy for exactly this file-size problem), `InternalFlowDisplay.Inline` and `InternalFlowTrigger.Hover` are declared on `ReportConfigurationOptions` and read by nothing. The wiki documents all three as working | §9 Q3: the wiki is corrected now (a falsehood is in the bar); the options' fate is a v4 question (D12), since removing them is a major |
| F8 | **More than half of every map is segments built for marker records, since 3.15.1.** On the consumer's 3.29.6 xUnit run, 1,602 of the 1,605 segments no diagram links belong to the run's own marker records: 1,594 assertion notes and 8 step markers. In this repo's ReqNRoll example it is 65 of 65 (RUN, harness F: every log dumped just before the report was generated). A marker is logged as a request record with no response and no trace id. `BuildSegments` makes a segment of every request record that has a time ([InternalFlowSegmentBuilder.cs:25](../src/Kronikol/InternalFlow/InternalFlowSegmentBuilder.cs#L25)), so it takes the test's spans that start between the marker (less 50 ms) and the next log. No diagram links a marker, so each such segment is unreachable, and it repeats its neighbours' spans. That is why these looked like scenario calls under test ids no scenario owns. Until 3.15.1 markers carried no time, and the builder's time filter dropped them; `d9eff2d4` (3.15.1, "captures say when") stamps a time on every log that has none. Hence 3 unlinked segments on the 2026-09-05 lanes (3.0.83), 1,339 on 2026-09-19 (3.20.0), and 968 to 2,299 on every in-memory and docker lane the consumer published on 2026-09-25. The fix is the check 21 other places in `src/Kronikol` already make: skip `IsDiagramMarker`, and `IsUserAction` too, whose arrow carries no link either (READ, `PlantUmlCreator`). The 3 unlinked segments left per lane are real calls, attributed by the consumer's own step code with `TestIdentityScope.Begin("RecipeCostTest", context.RequestId)` and two like it. That puts a request id where the scenario's id belongs, so those calls are missing from their scenarios' diagrams. That is the consumer's to change (§8.6); `ShowReportDiagnosticsSection` reports them as 3 orphaned test ids. The first version of this plan's loop-collapse explanation, and the second's "a scope question", were both wrong | S1c, released first as R1 (§9 Q8); #100, roadmap 1.12 |
| F9 | `VisualDistinction` on `InternalFlowNoDataBehavior` behaves as `ShowMessage`: `BuildSegmentData` distinguishes only `HideLink` | Not this plan; noted for the roadmap's §6 |
| F10 | The relationship popup is unreachable from any report the product writes. `[[#iflow-rel-…]]` links are emitted only when `GeneratePlantUml` is given `stats` ([ComponentDiagramGenerator.cs:234](../src/Kronikol/ComponentDiagram/ComponentDiagramGenerator.cs#L234)), and every call in `src/` omits it; `BuildRelationshipSegments` and `GenerateRelationshipPopupContent` have no caller outside tests; `ComponentDiagramReportTests:333` asserts the component report carries no map. The end-to-end `ComponentFlowPopupTests` exercises a hand-built page ([TestPageGenerator.cs:238](../tests/Kronikol.Tests.EndToEnd/TestPageGenerator.cs#L238)) that no generator produces. It stays as the legacy-object fixture here; its fate is D12's | §7, D12 |
| F11 | The wiki's browser floor is wrong for the decompressor: `PlantUML-Browser-Rendering.md:66` says "Blob workers + OffscreenCanvas 2D + DecompressionStream: Chrome/Edge 69+, Firefox 105+, Safari 16.4+"; `DecompressionStream` is Chrome 80 and Firefox 113 (RUN, BCD) | S4 |
| F12 | **Q7, as first written, would have lost popups.** §3.7 said the readers need no change. But the popup script renders before it attaches the popup, and today only the island's async decode hides that. With raw sources, a popup whose diagram is a render-cache hit is written into an element not yet in the page: 3 of 8 popups on the prototype drew nothing, and all 8 drew with the popup attached first (RUN, harness G) | §3.4's order, S1; S1b's end-to-end fact |
| F13 | **S1 alone does not change what a visitor to a published report downloads**: 99.5% to 101.0% of today's gzip (§1.5). The summary's file sizes are the file as stored and opened. The download moves with R1 (87.7% to 92.8%) and Q7 (68.5% to 82.0% with R1). GitHub Pages serves gzip only, and CI artifacts are zipped, so both behave like the download | §0, §8.2: each changelog entry states the file and the download |

---

## 7. Tests, red first

**S0, before any production change.**

1. `ArrowLinkOpensPopupTests` (end-to-end, new): a real report from `ReportTestHelper` with a
   sequence diagram whose request arrow carries `[[#iflow-<id>]]` and a segment map holding that id,
   rendered under `BrowserJs`. Wait for the SVG, dispatch a click on the arrow's text (SVG rule: JS
   `dispatchEvent`, never `force`; the browser build has no `<a>`), expect `.iflow-popup` and the
   segment's title. Green today; it is the guard that S1 does not break the product's main
   interaction.
2. `InternalFlowHtmlGeneratorTests`: `WrapSegmentData` round-trips. Red: the test decodes `z` from
   the emitted element and asserts it equals `JsonSerializer.Serialize(map)`; today there is no `z`.
3. The size record: `measure.py` over the nine reports of §1.1, numbers into this plan's §1.1 "before"
   column (done) and the changelog; `variants.py` over the consumer's 18 published reports (§1.5,
   done), re-taken on the release before R1 and on R1's published site before R2.

**S1.**

4. Unit: the element has the id and the type; the list is `hidden` when the links without a key are
   fewer than the keys with a link and `has` otherwise, each exactly the set §3.3 defines, computed
   over every source handed in (sequence diagrams and the component diagram); no element when the
   map is empty; the two emit sites produce the same element for the same inputs; the public wrapper
   emits `has` holding every key.
5. `ComponentDiagramReportTests:333` and `MergeableReportTests:360` re-anchored on
   `<script id="iflow-segments"` (§4).
6. End-to-end, `IflowPopupTests`: every existing fact passes unchanged against the real emitter
   (the test page already uses `GenerateSegmentDataScript`); plus: the popup opens before the decode
   completes (the overlay is visible while `_iflowLoadSegments` is pending, checked by stalling the
   promise); the second popup does not decode again (count calls to `decompressGzipBase64`); a page
   whose blob is not gzip shows the §3.4 message; a page with no element behaves as empty
   (`_iflowHasSegment` false for every id); a page with a legacy `window.__iflowSegments` object (the
   component-flow page, kept as the one legacy fixture, F10) still works.
7. End-to-end, binding: on the report of test 1, an arrow whose id has no segment gets no
   `iflow-link-hover` class and stays black, and an arrow whose id has one is bound, synchronously
   after render (no wait on the promise), on a page emitted with `hidden` and on one emitted with
   `has`. This pins §3.3's "two lines change".
8. Merge: the §4 test.
9. `ToggleDefaultsBaselineTests` stays green as is: it compares three generations of one process, and
   gzip is deterministic for identical input in one process.

**S1b, if Q7 is taken.** Unit: the map's activity div carries `data-plantuml` holding the source with
`&`, `"`, `<`, a tab and newlines, HTML-decoded back to the input; the whole-test-flow content still
carries `data-plantuml-z` (the existing `InternalFlowHtmlGeneratorTests:187` pin, unchanged). End to
end: the popup of test 6 renders its diagram from `data-plantuml` with no call to
`decompressGzipBase64` for the island. Also end to end, red against S1b on today's popup order
(F12): two segments whose activity diagrams are the same source, their popups opened one after the
other; the second is a render-cache hit, and both draw.

**S1c, the marker fix (R1).** Unit, red first, in `InternalFlowSegmentBuilderTests`: two request
records with spans and, between them, an assertion-note marker and a step marker, written as
`TrackingDiagramOverride` writes them (request records with a time and no trace id), plus a user
action. The map holds the two requests' segments and nothing else; today it holds five. At report
level, also red first: a report generated with internal-flow tracking on from a scenario with a step
bar and an assertion note, in which every key of the segment map is a `[[#iflow-…` id in some diagram
source. The existing segment-builder and report tests stay green, and any that counted a marker's
segment is a finding to read before it is changed. On the consumer: `variants.py` on the release's
report finds no unlinked segment beyond the consumer's own three (§8.6). In the Example.Api ReqNRoll
lane, the 65 unlinked segments go to 0.

**S2.** The §5 export test, in `ExportFilteredHtmlRenderingTests`.

**S3.** A Playwright test on a page holding a server-style SVG, an `<a href="#iflow-…">` around the
arrow text (the shape a Java-rendered inline SVG has; the browser build emits no `<a>`, so the report
of test 1 cannot show this), with a second arrow whose segment has no spans: under `HideLink` the
link has no href, `cursor` is not `pointer`, and a dispatched click opens nothing. Red first; if it is
green on `main`, F4 is wrong and S3 is dropped.

**Kronikol4J** is not changed by this plan; §8.4 says what its tests will have to do.

Every end-to-end `WaitForFunctionAsync` uses `PollingInterval = 200`; every locator that can match
per-diagram elements uses `.First` or `.Nth`.

---

## 8. Slices, releases, records

### 8.1 Bump

Two patch releases, each the next number after whatever `main` is at when it starts, agreed with any
session releasing in parallel (P3's second release, 3.30.0, was being built in the worktree
`C:/Code/Kronikol-p3` on 2026-09-25). **R1** is a bug fix: nothing new to call, no option, no public
member. **R2** is performance work and a bug fix, with no new option and no new public member either.
Each changes report output, so each gets a ledger entry (§8.4) and no more. The CLAUDE.md judgement
call says a report-output change is not on its own a major, and the bump follows the nature of the
change. If Q8 is declined, it is one patch.

**File overlap with P3.** `DIAGRAM_COLOURS_PLAN.md` S2 rewrites the colour literals inside the same
`bindIflowLinks` (lines 1062 to 1106 of the render script) that S1 here changes two membership lines
in. Rule 5: one after the other, and whichever ships second rebases. The stage plan's "their files are
disjoint" does not hold for this one function. P3's working copy on 2026-09-25 (uncommitted, read
with `git diff` in its worktree) moved the blue-text path's check: `if (!segId || !iflowData[segId])
return;` becomes `if (!segId) return;`, and a few lines later `if (!iflowData[segId]) { atRest();
return; }`, which puts an unbound link back in its surrounding ink. After P3, S1 changes that
condition and the `<a>` path's `if (!iflowData[segId]) return;` (untouched by P3), and nothing else.
R1 touches no script, so it does not overlap P3 at all.

### 8.2 Changelog drafts

R1, the marker fix (numbers from §0, to be re-taken on the release's own report):

> ## [3.x.y] - 2026-09-xx
>
> **Patch - a bug fix: marker records no longer get an internal-flow segment.** The patch part moved
> because nothing is new for a consumer to call.
>
> ### Fixed
> - **Assertion notes and step markers no longer carry an internal-flow segment of their own.** Since
>   3.15.1 every captured record carries a time, marker records included. The segment builder then
>   made a segment of each marker from the spans around it, and no arrow could open any of them. On
>   an 8 MB BreakfastProvider report, 1,602 of 2,903 segments were markers. That report is now 40%
>   smaller (7,994,701 to 4,767,943 bytes), `domContentLoaded` goes from 128 to 94 ms in Chromium,
>   the JS heap after load from 17 to 11 MB, and a visitor to a report published on GitHub Pages
>   downloads 9% less. The mergeable JSON's `internalFlowSegments` loses them too. No arrow opens a
>   different popup than before. A UI action's record, whose arrow carries no link either, gets no
>   segment.

R2, the blob:

> ## [3.x.z] - 2026-09-xx
>
> **Patch - performance: the internal-flow segment map ships as one gzip blob (#89).** The patch part
> moved because nothing is new for a consumer to call: the two public emitters keep their signatures
> and the report's scripts decode what they emit.
>
> ### Changed
> - **The internal-flow segment map is compressed** (#89). It was the one large block a report
>   shipped raw, a `<script>` object literal that V8 had to parse before the body: 62.7% of the
>   issue's 31.8 MB report, and 52% of a 4.8 MB BreakfastProvider report even after the previous
>   release's fix. It is now `<script id="iflow-segments" type="application/json">`, holding the same
>   map gzipped and base64'd. It is decoded once, on the first popup, through the decompressor every
>   report already carries, and it comes with the short list of arrow ids the render script needs to
>   bind arrows without decoding. Measured on that report: 4,767,943 bytes to 2,798,451 [2,451,275
>   with Q7]; `domContentLoaded` 94 to 72 ms in Chromium; the head the filtered export copies from
>   2.97 MB to 1.00 MB [0.65 MB]; the first popup pays about 30 ms once, later ones nothing. A report
>   served gzip-encoded, as GitHub Pages serves one, downloads about what it did [23% less with Q7]:
>   the map was already compressed on the way, and this release makes the file itself small. [Q7: The
>   activity diagrams inside the map are carried as plain PlantUML now that the blob does the
>   compressing; they were gzip inside gzip.] `puml-data` is deliberately not treated the same way: it
>   is read by key, per diagram, and pruned by key on export (#88). The popup now needs
>   `DecompressionStream` (Chrome 80, Firefox 113, Safari 16.4), which `BrowserJs` rendering and deep
>   search already required. A page that sets `window.__iflowSegments` itself still works.
> - **The popup opens at once and says why when it cannot show a segment**, instead of a blank box.
>
> ### Fixed
> - **`HideLink` hides the link under Server and Local rendering too** (if S3 ships). In a
>   server-rendered inline SVG, an arrow whose segment captured no spans kept its href and opened a
>   popup saying there was no data; under `BrowserJs` it was already plain text. …

If Q8 is declined, the two entries are one, with R1's paragraph under Fixed.

### 8.3 Wiki

- `Internal-Flow-Tracking.md` line 21 (the popup "looks up the segment data from
  `window.__iflowSegments`"), line 194 (embedded as a `<script>` containing `window.__iflowSegments`;
  "can increase file size significantly" becomes the measured sentence), the diagram at line 296, and
  the `HideLink` row at line 175 (true after S3; reworded if S3 is not taken). F7: the Content
  Strategy section (lines 163, 196 to 202) documents `SeparateFragments` as working; it does nothing,
  and the section says so until D12 decides the option. `Report-Configuration.md` lines 152 to 153
  repeat the two rows.
- `API-Reference.md` line 39: `GenerateSegmentDataScript` produces the `iflow-segments` element; line
  49 describes `InternalFlowContentStrategy` as controlling storage (F7).
- `PlantUML-Browser-Rendering.md` line 66: the floor sentence corrected to Chrome/Edge 80, Firefox
  113, Safari 16.4 for `DecompressionStream` (F11), with the popup added to what needs it.

### 8.4 Kronikol4J ledger entries (`../Kronikol4J/docs/REMAINING_PARITY.md`, the divergence ledger at its line 1877)

R1's entry:

> - **Marker records get no internal-flow segment (.NET 3.x.y, 2026-09-xx).** .NET's
>   `InternalFlowSegmentBuilder.BuildSegments` now skips `IsDiagramMarker` and `IsUserAction` records,
>   so the segment map loses the segments no diagram could link. `InternalFlowSegmentBuilder.java`
>   builds them the same way it did: at `:93` it skips only records that are not requests. That
>   matters once the port's marker records carry a time, and whether they do today was not checked.
>   Port the check with the capture side's timestamps.

R2's entry:

> - **Internal-flow segments as one gzip blob (.NET 3.x.z, 2026-09-xx).** .NET's
>   `WrapSegmentData` now emits `<script id="iflow-segments" type="application/json">{"hidden":[…],"z":"…"}</script>`
>   (or `"has":[…]`, whichever list is shorter) in the head where
>   `<script>window.__iflowSegments = {…};</script>` was: `z` is the unchanged `System.Text.Json`
>   map, gzipped at `Optimal` and base64'd (the `puml-data` convention, so
>   `GzipBase64.encode(CompactJson.write(data))` reproduces it); `hidden` is every `[[#iflow-…` id in
>   the report's diagram sources (sequence diagrams and the embedded component diagram) that has no
>   map entry, `has` every map key some diagram links, in first-seen order; the public wrapper with
>   no diagrams emits `has` holding every key. Both HTML reports carry it; no element at all when the
>   map is empty. [Q7: the map's activity-diagram divs carry `data-plantuml="<attribute-escaped
>   PlantUML>"` instead of `data-plantuml-z`; `renderActivityDiagramInline` mirrors it, and the
>   `InternalFlowHtmlGeneratorTest` assertion on `data-plantuml-z="` moves with it. The whole-test-flow
>   divs keep `data-plantuml-z`.] The popup script and the render script changed and must be copied
>   verbatim as usual (`internal-flow-popup-script.js` is byte-identical to .NET's today).
>   `InternalFlowHtmlGeneratorTest` byte-compares the script against a golden and must become a
>   decoded compare, and `GoldenHtmlParityTest.mask` gains a third island beside `puml-data` and
>   `data-flame-z`. The mergeable JSON's `internalFlowSegments` is unchanged [in shape; with Q7 its
>   `content` strings carry the raw attribute]. Until ported, an unported Java report keeps working
>   with the old popup script it carries; a Java report with the NEW scripts and the OLD emitter also
>   works, through the scripts' legacy-object check.

### 8.5 Before declaring done

The 3.22.1 audit list: the public doc comments on `GenerateSegmentDataScript`, `WrapSegmentData` and
`MergeableReport.InternalFlowSegments` rewritten; a paint-level end-to-end fact per decision (the
loading state, the failure message, the hover class timing on both list forms, the export, the
attribute round-trip if Q7, the render-cache hit if Q7); the wiki sentences above, including the
sibling pages; the numbers in §1.1 and §1.5 re-taken with `measure.py` and `variants.py` after the
change (both read the new element) and written into the changelog; `PLANS_STATUS.md` row and
`STAGE_1_PLAN.md` P5 row updated; the full suite, then the bump on every package, tag, push; the CI
run of the release commit read (CI, Release, CodeQL); then §8.6. A release of this plan is done when
the consumer is running it, not when NuGet has it.

### 8.6 The consumer: BreakfastProvider upgraded, released and checked (S5)

BreakfastProvider (`c:/Code/BreakfastProvider`, `lemonlion/BreakfastProvider` on GitHub) is this
plan's measured consumer. Its xUnit lane is the report §0 measures, and its CI publishes all 18 lanes'
reports on GitHub Pages, the live demo the roadmap points the repository's homepage at (2.1, 13.1).
Each release of this plan counts as accepted only once the consumer runs on it and its published
reports behave, so this is done once for R1 and once for R2. The consumer has no tags or releases of
its own. Its release is a commit on `main`, whose `CI: Main` run tests all 18 lanes (six frameworks,
each in memory, in docker and against an external SUT), records the history ledger, publishes the
fakes and deploys the Pages site.

**Where it stands (2026-09-25, RUN).** It is on 3.29.0 (`0d29537`): 27 `Kronikol*` package pins in 10
files, plus `Kronikol.Tool --version` in `ci-main.yml`, `_tests.yml` and `_tests-tunit.yml`. The span
to R1 covers 3.29.1 to 3.29.6, P3's 3.30.0, and whatever else lands first. A dry run at 3.29.6, in a
scratch clone with all 30 pins moved:

- `dotnet restore` is clean. No extension's floating client moved past the consumer's pins this time,
  where 3.27.x had needed `Grpc.Net.Client` 2.84.0.
- The xUnit lane in memory passes 203 of 203, in 3 min 25 s.
- The report is 7,994,701 bytes, against 7,999,910 for the 3.29.0 one published that morning.
- `popup-smoke.js` finds the same bound-link counts as the published 3.29.0 report (44 and 41 on the
  first two diagrams), every popup drawn, and no console error.

Nothing in the span so far needs a consumer change.

**The steps, for each release.**

1. **Baseline, before the bump.** Run `popup-smoke.js` and `variants.py` over the published xUnit,
   ReqNRoll and LightBDD in-memory reports (the three shapes: xUnit, Gherkin and LightBDD's own) and
   keep the outputs in the plan's log. The published site is the baseline, so nothing is run locally
   for it.
2. **Wait for the packages.** Bump only after every package of the release is listed in NuGet's
   registration index (`registration5-gz-semver2/<id>/index.json`, not the flat container), and
   restore with `--no-cache`. The 3.20.0 and 3.27.1 bumps each lost one lane to `NU1102` when pushed
   minutes after the publish.
3. **Bump all 30 sites in one commit**, changing only the version strings. A `sed -i` on this machine
   rewrote the files' line endings; that is harmless to what git stores, but it is noise in the
   editor. `dotnet restore BreakfastProvider.sln --no-cache` must come back clean. An `NU1605` means
   an extension floated its client past one of the consumer's pins (#97, D21), and that pin moves in
   the same commit, as `Grpc.Net.Client` did for 3.27.x.
4. **Local run, before pushing.** Run the xUnit lane in memory: `dotnet test
   ./tests/BreakfastProvider.Tests.Component.xUnit/BreakfastProvider.Tests.Component.xUnit.csproj
   -p:WarningLevel=0`, about 4 minutes, and expect 203 of 203. Then, on its report:
   - `measure.py`, which reads either form.
   - `variants.py`, to count the unlinked segments: after R1 only the consumer's own three.
   - `popup-smoke.js`: the same bound counts per diagram as the baseline, every popup's diagram drawn,
     one decode, no console error.
   - One export by hand (§5): filter, export, open the export, click an arrow.

   `git restore docs/` before committing, because a local run rewrites `docs/Specifications.yml`.
5. **Commit and push** to `main` in the consumer's form (`Kronikol X.Y.Z: <what changed for this
   consumer>`), with no attribution lines.
6. **CI: Main on the push.** Every one of the 18 lanes must be green; a lane red on `NU1102` is the
   registration lag, so re-run it. Each component lane's history gate must read `nothing changed`,
   because neither release changes a capture; a `behaviour-changed` is a finding to explain before the
   release is called accepted. The `history` job records 18 suites, and `publish-fakes` and
   `deploy-pages` are green.
7. **The published site.** For every lane, compare the file and the downloaded size (`curl
   --compressed`) with §1.5's today column. Run `popup-smoke.js` on the six in-memory lanes' URLs and
   one docker lane's, over https. For R1, expect the files at 57% to 66% of today and the download at
   88% to 93% on the in-memory and docker lanes. For R2, expect the §1.5 column its questions picked.
8. **The next run.** The nightly schedule (03:00 UTC) or a dispatched run must read `nothing changed`
   again on every lane. This is the two-run rule the history plans used.
9. **Record** the run ids, the sizes and the smoke outputs in this plan's log, and update the
   consumer's memory note.

**What stops the release:**

- a lane that fails for a reason the release caused;
- a popup that does not draw;
- a bound-link count that differs from the baseline on the same diagram;
- a console error the baseline did not have;
- a history verdict the release does not explain.

Each is fixed in Kronikol, in a further patch, before the plan is marked executed. The consumer is
never changed to work around a Kronikol defect.

**Consumer-side, and the owner's call, not this plan's.** Three step classes in
`tests/BreakfastProvider.Tests.Component.Shared/Common/` open `TestIdentityScope.Begin("<name>Test",
context.RequestId)`: `CustomerFeedback/PostCustomerFeedbackSteps.cs:29`,
`RecipeCosts/PostRecipeCostSteps.cs:30` and `ServiceTimes/PublishOrderServedEventSteps.cs:33`. The id
is the request's, not the scenario's. So the call each scope covers, to the Kitchen Service or the
Supplier Service, is missing from its scenario's diagram, and after R1 those calls are the map's only
unlinked segments. Passing the scenario's identity would put them back in their diagrams. The report
diagnostics section counts them as 3 orphaned test ids.

---

## 9. Not taken, and open questions

**Not taken.**

- Per-segment compression: loses the shared dictionary (§3.6).
- Pruning the export by key: §5.
- `SmallestSize`: 2.2% smaller for 5 ms more, and a second gzip convention beside the one every
  other island and the Java port share.
- An opt-out option: a new option is a minor, and the old form has no reader but the report's own
  scripts.
- `Uint8Array.fromBase64` in the shared helper: about 1 ms saved per decode of this blob; it touches
  a helper four scripts and the port copy. Left for a moment when the helper changes for another
  reason.
- Moving the block to the body: no effect on the export, a template change for nothing.
- A `hidden`-only list (the first version of this plan): 25 to 54 KB on the 2026-09-05 lanes, more
  than one of their blobs. A full key list: 130 KB here, ~540 KB on the issue's lane. The shorter of
  two exact sets is four lines of script (§3.3).
- Compressing the membership list: the answer must be synchronous, so it stays plain.
- A filter on the test id (the second version of this plan's S1c): no segment for a record whose test
  id no scenario has. With the cause found (F8), all it would still drop is three real calls per
  consumer lane. Their attribution is the consumer's to fix, and the diagnostics section reports the
  class, so a filter would only hide it.
- Anything in `puml-data` (#88 closed as a finding).

**Open questions.** Each has a recommendation; none blocks S0.

| | Question | Recommendation |
|---|---|---|
| Q1 | Carry the blob whole in the export, or prune | Whole (§5) |
| Q2 | Ship S3 (`HideLink` honoured on server-rendered SVG) in the same release | Yes if S3's red test confirms F4, as its own commit: two lines on S1's membership list, for the two rendering modes v4 deletes. Declining is reasonable for the same reason; then the wiki row says `BrowserJs` |
| Q3 | F7: three inert options documented as working | Correct the wiki now; put the options on the v4 list (D12) as removals, since removing is a major and implementing `SeparateFragments` is a feature this plan makes unnecessary |
| Q4 | Emit nothing when the map is empty, or an empty blob | Nothing; the scripts read absence as empty, and an ingest or merge report without spans carries no dead element |
| Q5 | `Optimal` or `SmallestSize` | `Optimal`: one convention |
| Q6 | Name and shape: `iflow-segments` with `z` and `has` or `hidden` | As written; `kron-search-index` set the element idiom and 8.2 will follow the object shape |
| Q7 | Carry the activity-diagram sources raw inside the blob (§3.7) | Yes, as S1b, its own commit in the same release. It is a further 2.4x on the blob (666 to 269 KB here), and with R1 it is the only part of the plan that takes a real amount off what a published report's visitor downloads (§1.5: 23% of it on the consumer's xUnit report). It costs one Java method and one Java assertion, and it depends on §3.4's order, which S1 brings (F12). The measured attribute round-trip, a unit pin and the render-cache end-to-end fact cover it. Declining leaves 59% of the blob as gzip inside gzip and means a second change to the map's bytes later |
| Q8 | F8: ship the marker fix (S1c) first, as its own patch (R1) | Yes. Its cause is found and it is a regression, and it needs no design and changes no script. On its own it takes 40% off the consumer's report, 34 ms off `domContentLoaded` (128 to 94) and 9% off what a visitor downloads. It also halves the map before S1 decides how to carry it, and it does not overlap P3. Declined, it becomes S1c, the first commit of R2 |

---

## 10. Assumption ledger

| Statement | Level | Note |
|---|---|---|
| The issue's 6.1% and 31.8 to 13.1 MB | ISSUE | Not on this machine. This machine's largest report lands at 12.1%; the ratio depends on duplication, which that lane has more of (11.9x against 4.4x of distinct activity diagrams here) |
| .NET `Optimal` output and timing | RUN | PowerShell 7 on .NET 10.0.12, the real block |
| Nine size rows, the composition, the raw-source variant | RUN, python zlib level 6 | 3.7% under .NET `Optimal` on the block measured both ways; the "after" file sizes are COMPUTED |
| Chromium load, heap and decode numbers | RUN, headless Chromium via the E2E driver | The blob at Node zlib level 6; `--enable-precise-memory-info`; network aborted; medians of 7 |
| V8 parse 37 ms; Node decode 27 to 84 ms | RUN, Node 25.9 | Node's `DecompressionStream` is noisy; Chromium's first decode read 47 ms |
| The block is in the head | RUN and READ | See §1 for the two false probes |
| 19 orphan links here, 535 to 1,184 on the older lanes; 1,601 unlinked keys absent from the data file | RUN | Per-report counts in §1.4; the unlinked keys are marker records (F8, found in the third pass) |
| Orphan links open a no-data popup | READ, Server/Local inline SVG only | Under `BrowserJs` the P3 session's engine probe (no `<a>`) plus the render script's blue-text path say hidden; S3's first test is the RUN for the `<a>` path |
| Binding must stay synchronous | READ | The six `_iflowBindLinks` call sites; the hover-only styling is applied in the same tick as the render today |
| The membership list is exact | READ | Every id a diagram can present is in the scanned sources; fragment re-renders slice the same sources; a merge does not re-key |
| A merge does not re-key segments | RUN | `Id()` reads the scenario rename table |
| Nothing else reads the block | RUN | grep over `src/`, `tests/`, `tools/`, the tool, the wiki |
| Relationship popups are produced by no report | RUN | grep: `stats` never passed; the builders have test callers only |
| The readers take `data-plantuml` before `data-plantuml-z` | READ | Popup script `:72`, render script `:1120` |
| An attribute round-trips a PlantUML source | RUN | Chromium, harness D; other engines not run |
| Kronikol4J's popup script is byte-identical | RUN | `diff` after CRLF normalisation |
| `DecompressionStream` availability | RUN | `mdn/browser-compat-data`, fetched 2026-09-22 |
| The unlinked segments are marker records | RUN on two runs | The consumer's xUnit lane at 3.29.6 (1,602 of 1,605) and this repo's ReqNRoll example (65 of 65). On the other 16 published lanes it is inferred from the same unlinked share |
| The regression dates from 3.15.1 | READ | git at `v3.15.0` and `d9eff2d4`; the unlinked counts of 2026-09-05 (3) and 2026-09-19 (1,339) agree |
| What a visitor downloads | RUN | `curl` against GitHub Pages; python level 6 within 2% of the served bytes; variants rebuilt by `variants.py`, whose serialiser matches the .NET block byte for byte on all 18 |
| The prototype of S1 behaves as §3.2 to §3.4 say | RUN, Chromium, one report | Patched copies of the consumer's 3.29.6 report (`prototype-s1.py`): 8 diagrams, same bound counts, one decode, no console error. The prototype waits for the decode before the popup appears, where §3.4 shows it first |
| Popups are lost with Q7 and today's order | RUN | 3 of 8 on the prototype, all render-cache hits; 8 of 8 attached first |
| The consumer upgrades cleanly | RUN at 3.29.6 only | Restore and the xUnit lane in memory; the other 17 lanes, CI and the gate are §8.6's, per release |
| Kronikol4J's markers carry no time | NOT CHECKED | The Java builder has the same missing check (READ); whether it bites depends on the port's capture side |

---

## Appendix A. Re-taking the numbers

Everything is in [`INTERNAL_FLOW_BLOB_PLAN.harness/`](INTERNAL_FLOW_BLOB_PLAN.harness/README.md), with
the commands and the output of 2026-09-22 and 2026-09-25:

- `measure.py`: size, composition, the raw-source variant, links against keys, loop labels; reads a
  report from before this plan (`window.__iflowSegments`) or after it (the `iflow-segments` element),
  so it is the script that re-takes §1.1 for the changelog.
- `node-timing.mjs`: the V8 parse with the compilation cache defeated, and the decode path, in Node.
- `load-timing.js`: the Chromium load, heap and decode numbers, through the E2E project's driver.
- `attr-newlines.js`: the attribute round-trip behind Q7.
- `check-anchors.py`: whether the plan's 38 cited lines still hold at a revision, and where each moved
  line went.

Added in the third pass (2026-09-25), with their output in the README's sections E to I:

- `variants.py`: every variant of §1.5 (R1, S1, S1 with R1, Q7, Q7 with R1) rebuilt from a report
  written before R2, with its size, its gzip and whether it binds the same arrows. On a report written
  after R2, it checks the element's list instead. It re-takes §0 and §1.5 for each changelog.
- `prototype-s1.py`: S1's two script changes patched into a copy of a real report (`--q7`, `--fix`,
  `--attach-first`).
- `popup-smoke.js`: in Chromium, over a file or a URL, it counts the bound links of real diagrams,
  clicks one per diagram, and times the popup and its diagram. The consumer check of §8.6, before and
  after each release.
- `load-pages.js`: `load-timing.js` for pages already built, any number side by side.
- The marker diagnosis (F8) used a throwaway edit, not a script: a few lines in a scratch copy of the
  report hook that write every `RequestResponseLog` to a JSON-lines file just before the report is
  generated (README section F).

The .NET numbers of §1.1 come from `System.IO.Compression.GZipStream` in PowerShell 7 over the same
bytes at each `CompressionLevel`. If a permanent guard is wanted, it is the round-trip pin of §7, not
a size assertion.
