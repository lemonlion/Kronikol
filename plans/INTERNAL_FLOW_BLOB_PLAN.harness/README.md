# Harness for `INTERNAL_FLOW_BLOB_PLAN.md`

Measured against HEAD `2843018a` (3.27.2) on 2026-09-22, on the reports already on disk. Nothing here
changes the product; the scripts read a `TestRunReport.html` and print numbers. Everything runs from
this folder.

```bash
# 1. Size and composition of the segment block, per report (python 3, stdlib only). Also runs on a
#    report written AFTER the plan (it decodes the iflow-segments element instead).
PYTHONUTF8=1 python measure.py <TestRunReport.html> [more...]
# 2. Parse and decode cost in Node (V8): the object literal today, the decode path proposed
node node-timing.mjs <TestRunReport.html>
# 3. Load cost in Chromium: the report as it is against a copy carrying the blob; heap; decode cost
node load-timing.js <TestRunReport.html> [runs=7]
# 4. One browser fact for Q7: a PlantUML source round-trips through a data-plantuml attribute
node attr-newlines.js
# 5. Do the plan's cited lines still hold? The working tree, or any revision through git show
PYTHONUTF8=1 python check-anchors.py [origin/main]
# 6. Every variant (R1, S1, Q7 and their mixes): file, gzip (what a GitHub Pages visitor downloads),
#    and whether it binds the same arrows. On a report written after R2, the element's list is checked
PYTHONUTF8=1 python variants.py <TestRunReport.html> [more...] [--write DIR]
# 7. S1's two script changes patched into a copy of a real report
PYTHONUTF8=1 python prototype-s1.py <TestRunReport.html> <out.html> [--q7] [--fix] [--attach-first]
# 8. In Chromium: real diagrams drawn, their bound links counted, one clicked each, the popup timed
node popup-smoke.js <TestRunReport.html | https URL> [diagrams=3]
# 9. Load timing of pages already built, side by side
node load-pages.js [runs=7] <page.html> [more...]
```

Sections A to D are the measurements of 2026-09-22; sections E to I are the third pass, 2026-09-25,
against 3.29.6 (`ad289f55`) and the consumer's published reports.

The plan cites lines at 3.27.2 (`2843018a`). Script 5 re-checks all 38 and prints the new line
for each one that moved. On 2026-09-25: 27 of 38 held at 3.29.3 (`ReportGenerator.cs` had shifted by
12 to 39 lines, `ReportDiagnostics.cs` by 8); 19 of 38 held at 3.29.6 (`ad289f55`), where the render
script had also shifted by 21 lines above `bindIflowLinks`, whose body was unchanged (its membership
lines at 1062 and 1106). P3's 3.30.0 edits that function again, so run it before executing.

Scripts 3 and 4 use the E2E project's own Playwright driver
(`tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package`), so the E2E project must have
been built once and its browsers installed. Node 25 was used.

The largest report is the xUnit lane's kept run
`BreakfastProvider.Tests.Component.xUnit/bin/Debug/net10.0/Reports/runs/local_20260922T080715Z_14e947b5/`
(8,151,281 bytes, 3.27.0). The lane's current `TestRunReport.html` is a later filtered run of 514 KB,
so measure the kept run, not the top-level file.

## A. Size and composition (`measure.py`)

python zlib level 6 throughout; .NET `GZipStream` at `Optimal` came out 3.7% larger on the one block
measured both ways (517,825 against 499,585 bytes of gzip). "Raw sources" is the §9 Q7 variant: the
`data-plantuml-z` islands inside each segment's content replaced by the PlantUML they hold, in a
`data-plantuml` attribute, before the outer gzip.

| Report | Generated | File | Block | Of file | Segments | Distinct activity diagrams | Blob (base64) | Blob, raw sources |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| BreakfastProvider xUnit, kept run 08:07 | 2026-09-22, 3.27.0 | 8,151,281 | 5,699,628 | 69.9% | 2,897 | 656 | 666,116 (.NET 690,436) | 268,748 |
| BreakfastProvider xUnit, kept run | 2026-09-19 | 6,492,959 | 3,970,682 | 61.2% | 2,295 | 492 | 480,672 | 189,168 |
| BreakfastProvider BDDfy | 2026-09-05 | 3,815,269 | 1,242,129 | 32.6% | 804 | 466 | 255,300 | 86,052 |
| BreakfastProvider NUnit | 2026-09-05 | 3,289,565 | 1,247,071 | 37.9% | 807 | 370 | 221,072 | 76,076 |
| BreakfastProvider LightBDD | 2026-09-05 | 4,042,187 | 1,214,336 | 30.0% | 789 | 603 | 277,940 | 90,724 |
| BreakfastProvider ReqNRoll | 2026-09-05 | 3,898,460 | 1,167,623 | 30.0% | 765 | 544 | 261,376 | 84,948 |
| BreakfastProvider TUnit | 2026-09-05 | 2,501,389 | 613,418 | 24.5% | 383 | 212 | 122,644 | 40,296 |
| Example.Api CiPreview.Mixed | on disk | 517,170 | 9,061 | 1.8% | 7 | 4 | 1,944 | 1,096 |
| Example.Api ReqNRoll.xUnit3 | on disk | 704,314 | 125,134 | 17.8% | 88 | 20 | 11,852 | 6,628 |

What the block is made of, every report alike: segment `content` HTML 48% to 59% of the block,
`flameData` (raw JSON) 10% to 30%, and the `data-plantuml-z` islands inside `content` 18% to 22%.
Every `content` string is distinct (it embeds the segment's id and its flame data); the islands
repeat (2,897 islands, 656 distinct on the largest report). The islands are gzip already, so the
outer gzip cannot shrink them, and base64 has inflated them by a third: that is the whole of the
raw-sources gain (41.5% of the baseline on the largest report, 33.6% to 35.6% on the 800-segment
lanes, 56% to 58% on the tiny Example.Api ones where fixed overheads dominate).

On the largest report, the map split by whether any diagram links the key:

| | Keys | Raw | Blob (base64) | Blob, raw sources |
|---|---:|---:|---:|---:|
| Whole map | 2,897 | 4,876,979 | 647,776 | 265,288 |
| Keys some diagram links | 1,296 | 2,113,829 | 492,612 | 153,772 |
| Keys no diagram links | 1,601 | 2,763,151 | 299,964 | 161,980 |

(python re-serialisation, hence 647,776 rather than 666,116 for the whole map.)

Links against keys, per report (`[[#iflow-…` ids over every `puml-data` source):

| Report | Distinct links | Links with no map entry | Map keys | Keys no diagram links |
|---|---:|---:|---:|---:|
| xUnit 2026-09-22 | 1,315 | 19 | 2,897 | 1,601 |
| xUnit 2026-09-19 | 974 | 18 | 2,295 | 1,339 |
| BDDfy 2026-09-05 | 1,347 | 546 | 804 | 3 |
| NUnit 2026-09-05 | 1,339 | 535 | 807 | 3 |
| LightBDD 2026-09-05 | 1,323 | 537 | 789 | 3 |
| ReqNRoll 2026-09-05 | 1,345 | 583 | 765 | 3 |
| TUnit 2026-09-05 | 1,564 | 1,184 | 383 | 3 |
| Example.Api CiPreview.Mixed | 7 | 0 | 7 | 0 |
| Example.Api ReqNRoll.xUnit3 | 23 | 0 | 88 | 65 |

No report holds a single `loop ×N` label (`CollapseConsecutiveIdenticalCalls` defaults to off), so
the collapse explanation for the unlinked keys is out. On the 2026-09-22 run the 1,601 unlinked keys
are request ids that appear in **no** scenario's `httpInteractions` in `TestRunReport.json`, and the
file's `background` bucket reports 0 calls; the run has 203 scenarios with 203 distinct ids, no
attempt beyond the first, every scenario's own ids all linked and all in the map. Their activity
diagrams carry the same labels as the linked segments' (367 labels shared, 2 only in the unlinked
set), so they are scenario-shaped calls under test ids no scenario owns. `ReportDiagnostics` has a
warning for exactly that class ("N orphaned test ID(s) in logs do not match any feature scenario"),
but the section was not emitted in this run. The in-repo `Example.Api.Tests.Component.ReqNRoll.xUnit3`
lane shows the same class (65 of 88 keys) and is the one to debug.

## B. Node timing (`node-timing.mjs`), the 5.7 MB block, median of five

| Step | ms |
|---|---:|
| Today: V8 parse+eval of the object literal, compilation cache defeated (unique source per run) | 37.1 to 37.8 across three runs |
| The same, same source every time (the cache returns the previous parse; the trap) | 5.3 |
| `JSON.parse` of the same text | 11.6 |
| `atob` + byte loop (the shared helper) on 666 KB of base64 | 1.0 |
| `Uint8Array.fromBase64`, where present | 0.1 |
| `zlib.gunzipSync` | 9.8 |
| Proposed: `DecompressionStream` + `Response.text()` + `JSON.parse` | 27.3, 29.9 and 84.4 across three runs |

Node's web-streams implementation is noisy (27 to 84 ms for the same bytes); the Chromium numbers
below are the ones that count.

## C. Chromium (`load-timing.js`), median of 7 loads each, every network request aborted

`today.html` is the report as it is (8,151,281 bytes); `after.html` has the block replaced by the
element (3,117,227 bytes; the blob at Node zlib level 6).

| Metric (ms from navigation start) | Today | After |
|---|---:|---:|
| `responseEnd` | 37 | 11 |
| `domInteractive` = `domContentLoadedEventStart` | 139 | 82 |
| `loadEventStart` | 500 | 447 |
| JS heap after load (MB) | 17 | 6 |

Decode on the `after` page, five times in one page, the popup script's path: first call
decompress 36.6 ms + `JSON.parse` 10.0 ms; then 13 to 18 ms + 8 to 13 ms. Each decoded copy held
adds about 11 MB of heap (26, 40, 51, 62 MB, then collected to 30), which is the heap the object
literal costs today at load: memory-neutral once a popup has opened, 11 MB lighter before.

## D. The attribute round-trip (`attr-newlines.js`)

A PlantUML source holding `"`, `&`, `<`, a tab and newlines, escaped and set through `innerHTML` in
a `data-plantuml` attribute, is returned by `getAttribute` byte-identical, both with literal
newlines and with `&#10;`: `{"literalNewlines":true,"entityNewlines":true}` (Chromium).

## E. The consumer's 18 published reports, on the wire (`variants.py`)

BreakfastProvider's CI publishes one report per lane on GitHub Pages
(`https://lemonlion.github.io/BreakfastProvider/reports/<lane>/TestRunReport.html`, with `docker/` and
`docker-sut/` prefixes for the other two modes). All 18 were downloaded on 2026-09-25, generated by
3.29.0 in the nightly run of that morning (`36112260332`). Pages answers `Accept-Encoding: br, gzip`
with gzip (`Content-Encoding: gzip`), and answers `br` alone with the file uncompressed. Python's
level 6 of the xUnit file is 1,278,471 bytes against 1,300,236 served. The serialiser in `variants.py`
reproduces all 18 blocks byte for byte, and every variant binds the same arrows on all 18.

| Report | File today | Download today | R1 file / download | S1 file / download | R1+S1+Q7 file / download | Segments | Unlinked |
|---|---:|---:|---:|---:|---:|---:|---:|
| xUnit | 7,999,910 | 1,278,471 | 61.5% / 90.2% | 39.2% / 99.6% | 32.8% / 69.7% | 2,867 | 1,572 |
| ReqNRoll | 9,448,479 | 1,373,598 | 56.8% / 88.0% | 40.1% / 99.5% | 34.3% / 70.1% | 3,407 | 2,163 |
| LightBDD | 9,305,027 | 1,350,965 | 59.3% / 88.4% | 41.6% / 99.6% | 35.6% / 69.2% | 3,260 | 1,970 |
| BDDfy | 9,192,063 | 1,357,643 | 57.4% / 87.7% | 39.7% / 99.6% | 33.5% / 68.5% | 3,432 | 2,145 |
| TUnit | 6,119,167 | 970,229 | 63.1% / 91.7% | 43.3% / 100.4% | 37.9% / 75.3% | 1,851 | 1,035 |
| NUnit | 7,976,840 | 1,219,138 | 60.6% / 89.7% | 38.2% / 99.6% | 31.8% / 68.5% | 2,891 | 1,595 |
| docker xUnit | 8,418,024 | 2,118,292 | 65.7% / 92.8% | 51.6% / 100.0% | 45.3% / 81.4% | 2,629 | 1,572 |
| docker ReqNRoll | 8,639,591 | 1,936,871 | 65.4% / 92.1% | 53.6% / 100.0% | 47.8% / 80.8% | 2,871 | 1,850 |
| docker LightBDD | 9,510,558 | 2,219,804 | 65.5% / 92.3% | 54.0% / 100.0% | 48.3% / 82.0% | 2,969 | 1,923 |
| docker BDDfy | 10,122,143 | 2,259,309 | 60.0% / 91.0% | 49.0% / 99.9% | 43.1% / 80.3% | 3,362 | 2,299 |
| docker TUnit | 5,304,833 | 1,070,786 | 64.9% / 90.4% | 52.9% / 101.0% | 46.7% / 78.2% | 1,559 | 968 |
| docker NUnit | 7,178,992 | 1,330,332 | 61.4% / 88.9% | 44.9% / 100.0% | 37.6% / 70.9% | 2,595 | 1,534 |
| external SUT, six lanes | 1,776,654 to 2,476,763 | 418,420 to 473,500 | 92.9% to 97.9% / 95.7% to 99.0% | 93.5% to 98.1% / 99.9% to 100.1% | 92.2% to 97.9% / 95.7% to 99.0% | 22 to 60 | all but TUnit's 10 |

Totals: files 111.5 MB today, 73.0 MB (R1), 57.2 MB (S1), 54.8 MB (R1+S1), 52.6 MB (S1+Q7), 51.1 MB
(R1+S1+Q7). Downloads 21.13 MB, 19.32, 21.11, 19.37, 17.65 and 16.57 MB. The raw output of the run is in
`variants-published-2026-09-25.txt`.

The CI artifacts are zipped: `xunit-in-memory-report` is 3,208,261 bytes, for a directory holding
`TestRunReport.html` and `Specifications.html` (7.9 MB each) and a 6.1 MB `TestRunReport.json`. The
Pages artifact is 58.7 MB. `Specifications.html` is published beside each lane's report and carries the
same map.

## F. What the unlinked segments are (a log dump, 2026-09-25)

No script: a scratch copy of the report hook wrote every `RequestResponseLog` to a JSON-lines file just
before the report was generated. The fields written were test id and name, method, URI, caller,
service, type, `RequestResponseId`, `TrackingIgnore`, `IsDiagramMarker`, `IsUserAction`, attribution,
time, trace id and marker kind. The segment keys of the report were then matched to those records.

- **This repo's `Example.Api.Tests.Component.ReqNRoll.xUnit3`**, in a worktree at `ad289f55` (the edit
  went into `Hooks/TestSetupHooks.cs` there, never here): 8 scenarios, 126 records, 88 segments, 23
  linked. **65 of the 65 unlinked are marker records** (`http://override.com/`, no caller or service),
  under the scenarios' own test ids.
- **BreakfastProvider's xUnit lane in memory**, in a scratch clone with its pins at 3.29.6 (the edit
  went into `Infrastructure/GlobalTestSetup.cs` there): 203 scenarios, 4,320 records, 2,903
  segments, 1,605 unlinked. **1,594 are assertion-note markers and 8 are step markers.** The other 3
  are real calls under three test ids that are no scenario's: `TestIdentityScope.Begin("RecipeCostTest",
  context.RequestId)` and two like it in the consumer's shared steps (attribution `Scope`). 1,682
  request-type marker records exist, 1,602 of them with a segment. None carries a trace id, so each
  takes the test's spans between itself (less 50 ms) and the next record. No linked segment is a
  marker.
- **When:** `TrackingDiagramOverride.cs` at `v3.15.0` sets no `Timestamp`; `d9eff2d4` (3.15.1)
  stamps one on every log without one (`RequestResponseLogger.cs`, "Only the HTTP handler and LogPair
  stamp the time"). `InternalFlowSegmentBuilder` filters on `Timestamp.HasValue` and on the type,
  never on `IsDiagramMarker` (checked at 3.29.6).

## G. S1 prototyped on the consumer's report (`prototype-s1.py`, `popup-smoke.js`)

The consumer's 3.29.6 xUnit report (section F's run, 7,994,701 bytes) was patched four ways, and each
copy was driven in headless Chromium with network allowed (the engine loads from the CDN). The popup
and its diagram are timed with a MutationObserver from the dispatched click.

| Page | Diagrams checked | Bound link texts per diagram | Popups drawn | Decodes | Console errors |
|---|---:|---|---:|---:|---:|
| today | 8 | 44, 41, 3, 86, 86, 86, 6, 86 | 8 | (none) | 0 |
| S1 + R1 + Q7, today's popup order | 8 | the same | **5** | 1 | 0 |
| S1 + R1 + Q7, popup attached first | 8 | the same | 8 | 1 | 0 |
| S1 + R1, popup attached first | 8 | the same | 8 | 1 | 0 |
| S1 alone, popup attached first | 3 | 44, 41, 3 | 3 | 1 | 0 |
| R1 alone (today's form) | 3 | 44, 41, 3 | 3 | (none) | 0 |

The three popups lost with today's order were the three render-cache hits (`plantuml.cacheStats().hits`
rose on each). Timings: today, the popup appears 1 to 3 ms after the click, and the first one's
diagram is drawn at 15 to 27 ms. With the blob, the first popup appears after the decode, at 37 ms
(S1 alone), 28 ms (S1 + R1) and 25 ms (S1 + R1 + Q7), with its diagram drawn at 61, 46 and 43 ms.
Every later popup appears in 1 to 4 ms. Seven of the first diagrams linking a segment sit in
sections the page does not show (`offsetParent` null), and the smoke script skips them.

The published 3.29.0 xUnit report over https, today's form: 2 diagrams (44 and 41 bound texts), each
popup drawn in about 205 ms by the old polling measure, no console error.

## H. Load timing on the consumer's report (`load-pages.js`), median of 7, network aborted

| Metric (ms) | today | R1 alone | S1 alone | S1 + R1 | S1 + R1 + Q7 |
|---|---:|---:|---:|---:|---:|
| `responseEnd` | 34 | 22 | 9 | 9 | 9 |
| `domContentLoaded` | 128 | 94 | 74 | 72 | 71 |
| `loadEventStart` | 429 | 400 | 381 | 381 | 379 |
| JS heap after load (MB) | 17 | 11 | 6 | 7 | 7 |

Two batches: today, R1, S1+R1 and S1+R1+Q7 in one; today (129), S1 alone and R1 (93) again in the
other.

## I. The consumer on 3.29.6, a dry run (2026-09-25)

A clone of BreakfastProvider at `0d29537` (its `main`, on 3.29.0). The 27 package pins in 10 files and
the three `Kronikol.Tool --version` sites were moved to 3.29.6. `dotnet restore BreakfastProvider.sln`
restored all 16 projects with no `NU1605` and no `NU1102`; the `NU1902`/`NU1903` advisories are the
consumer's own, as before. `dotnet test ./tests/BreakfastProvider.Tests.Component.xUnit/...csproj
-p:WarningLevel=0` passed 203 of 203 in 3 min 25 s (4 min 5 s with the build). `Failures.md` reads
`# No failures`, and `TestRunReport.html` is 7,994,701 bytes. Nothing was pushed, and the clone was
deleted afterwards (the report went on to sections G and H). The worktree of section F was removed as
well.
