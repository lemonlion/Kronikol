# A compressed TestRunReport.html: the page as zstd frames inside a small loader (29 MB to 7 MB)

**Date:** 2026-10-23 · **Repo version:** 5.5.2 (`origin/main` at `2933b7a0`; line numbers are for that commit, and
those the first draft cited are unchanged from `91d1afd7`, 5.5.1) · **Status: draft, not green-lit.** Audited the same
day (section 8).

This plan was written at the owner's asks of 2026-10-23:
- "Can we investigate options for getting the 29mb html report such as the one in
  "C:\Users\cex\Downloads\TestRunReportWithSlowToggle.zip" down to about 7mb, like we have for the equivalent .krkl
  file (which is 6.7mb). Look into using ztsd (I know it's natively only supported in firefox, but we could have a
  fallback to a non-native engine, and always have a check for a native version keeps it forward compatible). I don't
  mind a loading screen of up to 2 seconds (although the shorter the better)."
- On the level, after the first measurements: "Sounds like we should go with -9 for the moment".
- Then: "Write up the plan using the recommended design, including a follow up step to investigate the "26-38 s to fire
  load" thing".
- And: "another LLM said something a while back about one of the javascript ztsd decoders having some 32mb limit? I'm
  not sure if that's applicable with anything you planned, but make sure it's not something that we missed"
  (section 3.6).

**Decided by the owner on 2026-10-23:**
1. zstd at level 9 for now. Level 19 took 20 s on this report's diagram sources alone (section 3.2).
2. The design is the one recommended that day, called C2 below: two zstd frames in the page, the page first and the
   diagram sources second, with the diagram sources decoded in a Web Worker after the page is on screen.
3. A follow-up step investigates why WebKit fires `load` 26 to 38 s after the page is up (step F1, section 6).
4. No option: every `TestRunReport.html` is written compressed, and it is not a major. The owner: "I don't think this
   CompressHtmlReport should be an option at all. There is no reason to have it off, and I don't consider it a major
   change as it doesn't change any functionality from the user's point of view." (Section 4.7.)

Everything else here is a recommendation, with the questions in section 7.

Evidence labels:
- **RUN:** measured on 2026-10-23 on Windows 11 over the 5.4.4 report the owner shared (28,954,978 bytes), with Python
  3.14.4 (libzstd 1.5.7), Node 25.9.0, Playwright 1.59.0's own Chromium 147, Firefox 148 and WebKit 26.4 builds, and
  .NET 10 with ZstdSharp 0.8.8. Scripts and output are in [`HTML_COMPRESSION_PLAN.harness/`](HTML_COMPRESSION_PLAN.harness/README.md).
- **READ:** in the source at `2933b7a0`, `file:line`.
- **INFERRED:** reasoned, not measured.
- **WEB:** a published source, linked, read on 2026-10-23.

## 0. Summary

Today's page is 29 MB because every embedded item is gzipped on its own and then written as base64. That includes the
1,553 diagram sources, 102.8 MB of PlantUML that per-item gzip takes to 10.0 MB and base64 to 14.4 MB. No item can
reuse text from another. In one zstd stream the same sources take 3.48 MB, and the whole page's content 5.14 MB, at
level 9 (section 3.2). Wrapping today's page as it is gains far less (12.2 MB of zstd, 16.3 MB in a page) because its
items are already compressed (section 3.1).

The design (section 4) writes the report page with every item raw, compresses it as zstd frames (the page, then the
diagram sources) at level 9 with an 8 MB window, and puts them in a small page as base64, beside a loader and a
fallback zstd decoder in JavaScript or Wasm (Q14). The loader tries the browser's own `DecompressionStream('zstd')`
first and falls back to the vendored decoder, rebuilds the report with `document.open`, `write` and `close`, and
decodes the diagram sources in a Web Worker. For this report the file is **6,977,369 bytes** with the diagram sources
in one frame, as first measured, and **7,154,045** with them in the 16 MB pieces the audit chose (RUN).

| Measured (RUN, three runs each; run 1 is a new browser) | Today's page | C2 |
|---|---|---|
| File | 28,954,978 bytes | 6,977,369 bytes |
| Loading screen painted | none (the page renders as it parses) | 32 to 137 ms |
| Report painted, Chromium 147 | 816 to 818 ms (2,586 ms on a cold first run) | 685 to 709 ms (688 cold) |
| Report painted, Firefox 148 | 977 to 1,195 ms | 1,344 to 1,426 ms (fzstd); 1,335 to 1,372 ms (native, pref on) |
| Report painted, WebKit 26.4 (Windows) | 2,483 to 2,857 ms | 1,573 to 1,592 ms |
| Chromium JS heap + `ArrayBuffer` storage | 42 + 8 MB | 34 + 112 MB |

So the report is on screen inside the 2 s the owner allowed in every engine, sooner than today in Chromium and WebKit
and about 0.3 s later in Firefox.

The cost is memory, measured for the whole browser in step S0, which the audit ran (section 3.12). Today's page already
takes 1.25 to 1.6 GB in Chromium and 1.7 to 2.0 GB in Firefox. With the diagram sources in one frame, Firefox holds 290
to 375 MB more once diagrams are drawn and a deep search has run, because the page and the search worker each hold all
102.8 MB decoded; with pieces of 16 MB of source, decoded when first needed and at most two kept on each side, about
100 MB more, and the cost no longer grows with the report. In Chromium every layout is within about 160 MB of today's
page. So the diagram sources go in 16 MB pieces (Q2), 2.5% more file than one frame.

No browser decodes zstd natively by default today (section 3.5): the Compression Streams standard has no zstd, Firefox
138 and later have it behind a preference that is off, Chromium closed the request, and Safari has nothing. The native
check stays (it costs one constructor call, and with Firefox's preference on it decoded these frames), but every reader
runs the fallback today.

The fallback decoder is an open choice (Q14). fzstd (8.4 KB) decoded every frame here correctly. Its README's limit of
2^25-byte back-references is out of reach of 8 MB-window frames and did not reproduce even with a forced 40 MiB
back-reference (section 3.6); its open report of wrong output from its streaming decoder did not reproduce either. But
it does not check the frame checksum, so a wrong decode would go unseen. libzstd's own decoder built to Wasm (about
75 KB) gave the same bytes, can check the checksum, and decoded the page frame in 12 to 15 ms against 50 to 62 in
Chromium and 16 to 19 against 35 to 57 in WebKit, though in 69 to 72 against 51 to 67 in Firefox. The plan recommends
it, with fzstd kept for a page where Wasm is refused.

Steps (section 6): **S0** (memory, and the layout) is done. **F2** fixes deep search in WebKit, which fails there today
for a reason of its own (section 3.14): a patch that can ship first. **R1** (a minor) writes both full report pages,
`TestRunReport.html` and `Specifications.html`, compressed from a run, `kronikol merge` and `kronikol ingest`, with no
option (the owner's decision 4), and moves the tests and tools that read the page onto a decoding helper. **F1**
investigates WebKit's late `load` (a patch if the cause is the page's).

The audit (section 8) found that the first draft missed `Specifications.html`, the gzip fragments that merge takes from
data files already written, a C# step that decodes the page's blobs to build the search index, an export that would
write a broken file, the WebKit defect, the readers of the file's text (a weekly workflow, Kronikol4J's parity harness,
an example project, repo tools, wiki pages) and the test hooks that `document.open` erases, and that it counted 251
test files reading the page where there are 131. Each is now in the design.

Corrected on the way: the page carries up to five copies of the gzip helper (`report-decompress-helper.js`), by
rendering mode. That is deliberate (`DiagramContextMenu.cs:106-116`): each public script accessor prepends it so that a
page composed without the main report still works. It is not a defect, and inside a zstd stream the copies cost
nothing.

## 1. How far each claim was checked

| Claim | Evidence |
|---|---|
| What fills the page, by element, attribute and content class | RUN (`anatomy.py`, `json_blocks.py`, `split.py`), sizes only, never content |
| Codec sizes and times | RUN: libzstd 1.5.7 through Python, gzip 1.14, brotli 1.2.0, xz 5.8.2 (`cbench.py`, `level9.py`) |
| The writer's cost in .NET | RUN: ZstdSharp 0.8.8 (`zsharp.cs`), the release the product's source copy is |
| Load times, memory and decoder paths in three engines | RUN (`bench.js`), Playwright's bundled builds, headless, three runs each |
| Deep links, Back, a diagram drawn, page errors | RUN (`functional.js`), Chromium and Firefox; Back again in all three with the audit's prototype (`search-check.js`) |
| Deep search over text found only in diagram sources or flame spans | RUN (`make_c2s.py`, `search-check.js`): the same scenarios as today's page in Chromium and Firefox, with one frame and with pieces; in WebKit it fails in today's page too (section 3.14) |
| Whole-browser memory, one frame against pieces (step S0) | RUN (`mem.js`), Chromium and Firefox, while another session's test suites loaded the machine |
| What outlives `document.open` | RUN (`docopen-check.js`), all three engines |
| Native zstd and brotli support per browser | WEB (section 3.5); Firefox's native zstd also RUN with the preference on |
| fzstd's 2^25 back-reference limit, and its open issues | WEB and READ (its README, its code, its tracker); RUN: neither the limit (`fzcheck.js`, `fzstream.js`) nor issue #19 (`fz19.js`) reproduced |
| libzstd's decoder in Wasm as the fallback | RUN in Node (`dec-compare.js`) and in the three engines (`dec-browser.js`) |
| Who writes and reads each embedded item, and who reads the page's file | READ (Appendix A, section 3.13): three searches of the repo, the wiki and Kronikol4J, each site then read at its line |
| How the test suites use the page | READ (section 4.7): counted by pattern, the borderline hits read |
| Other reports (other suites, other sizes) | Not measured: one report, written by 5.4.4 |

## 2. What fills the page today

### 2.1 The parts (RUN)

| Content | Items | As stored in the page | gzip inside | Raw |
|---|---|---|---|---|
| Diagram sources (`#puml-data`) | 1,553 | 14,413,414 | 10,004,703 | 102,832,638 |
| Markup, scripts, CSS and plain attributes | | 12,284,541 | | 12,284,541 |
| Deep-search index (`#kron-search-index`, binary) | 1 | 1,035,322 | 776,489 | 1,346,203 |
| Internal-flow segments (`#iflow-segments`) | 1 | 482,868 | 335,921 | 4,146,822 |
| Flame charts (`data-flame-z`) | 771 | 448,500 | 335,591 | 963,831 |
| Held sub-steps (`#step-data`) | 359 | 272,583 | 188,390 | 3,097,565 |
| **Page** | | **28,954,978** | | **136 MB written back raw** |

Of the markup, 709,300 bytes are 15 inline scripts and 153,208 the stylesheet; the largest attributes are
`data-search` (2,328,656 bytes over 512 elements), `class` (1,877,270), `data-row-search` (1,247,916) and `title`
(1,141,694). `System.Text.Json` writes `+` as `\u002B`, so the base64 in JSON blocks is larger than plain base64.
The page holds no PlantUML engine: it comes from jsDelivr at run time (`TrackingDefaults.cs:34`).

### 2.2 How each item is written and read (READ)

Every item goes through `InternalFlowHtmlGenerator.CompressToBase64` (`InternalFlowHtmlGenerator.cs:573`), gzip then
base64, except the search index, which has its own (`SearchIndexBuilder.cs:248`). The page decodes them with one helper,
`decompressGzipBase64` (`report-decompress-helper.js:5-11`: `atob`, then `DecompressionStream('gzip')`), except the
search worker, which has its own `gunzipToText` and `gunzipToBytes` (`report-search-index.js:301-314`). Appendix A
lists every site. The page is assembled as one string in `GenerateHtmlReportCore` (`ReportGenerator.cs:1396`) and
written once (`ReportGenerator.cs:2720`), which is the one place a compressed page hooks in.

A run writes two such pages by default: `TestRunReport.html` (`ReportGenerator.cs:537`) and `Specifications.html`
(`:532`, behind `GenerateSpecificationsReport`, `true` by default, `ReportConfigurationOptions.cs:240`). Both are full
reports, with every item.

## 3. Findings

### 3.1 Wrapping today's page is not enough (RUN)

The page as it is, in one stream: zstd -9 12,257,677 bytes, zstd -19 12,157,731, brotli -q 11 12,024,206, gzip -9
12,772,165. Its items are already compressed, so a second pass over them gains nothing. In base64 inside a page
(prototype A) that is 16,347,583 bytes.

### 3.2 Raw content in one stream (RUN)

With every item written back raw, the classes at level 9 with a 2^23 window: markup 561,100 bytes, diagram sources
3,483,511, search index 719,857, internal-flow segments 279,762, flame charts 108,000, sub-steps 29,300. Together
5,181,530; in one frame 5,142,334. The diagrams gain the most: 10.0 MB of per-item gzip becomes 3.48 MB, because the
1,553 sources repeat each other.

gzip cannot do this even on raw content (11,404,891 bytes for the raw page): its 32 KB window cannot see from one
diagram to the next. Level 19 would take the classes to 4,316,965 bytes but took 20.5 s on the diagram sources alone,
against 0.33 s at level 9 (owner's decision 1). Brotli -q 11 on the raw page gave 4,058,511 bytes in 75 s; xz -9e
3,939,956 in 43 s.

### 3.3 The window: 2^23 (RUN, WEB)

RFC 9659 says that for zstd over HTTP, "decoders MUST support a Window_Size of up to and including 8 MB" and
"encoders MUST NOT generate frames requiring a Window_Size larger than 8 MB"
([RFC 9659](https://www.rfc-editor.org/rfc/rfc9659.html), September 2024). A native `DecompressionStream('zstd')` is
likely to keep the same cap (INFERRED), so the frames use a window of 2^23 (8 MB). A 2^27 window with long-distance
matching saved 2.4% (5,059,626 against 5,181,530 bytes) and is not worth a frame a native decoder may refuse. The
same cap also keeps the frames inside fzstd's limit (section 3.6).

### 3.4 How to store the diagram sources (RUN)

| Layout | Diagram bytes | Note |
|---|---|---|
| One frame | 3,483,511 | To read any diagram, the page decodes all 102.8 MB |
| Frames of 16 MB of source | 3,622,002 (+4%) | 7 frames; a diagram needs its own frame only |
| Frames of 8 MB | 3,781,262 (+9%) | 12 frames |
| Frames of 4 MB | 4,044,278 (+16%) | 24 frames |
| Frames of 1 MB | 4,751,961 (+36%) | 77 frames |
| One frame per diagram with a trained 110 KB dictionary | 6,316,355 (+81%) | Random access per diagram, at too high a cost |

A frame can only reuse text from inside itself, which is the cost of cutting. The page frame does not need cutting:
it is decoded once, whole, to build the page.

### 3.5 Native decoders (WEB, RUN)

- The [Compression Streams standard](https://compression.spec.whatwg.org) (Living Standard, updated 19 October 2026)
  defines `CompressionFormat` as `"brotli"`, `"deflate"`, `"deflate-raw"` and `"gzip"`. There is no zstd.
- `DecompressionStream('zstd')`: no support in Chrome, Edge or Safari; Firefox 138 and later have it, disabled by
  default ([caniuse](https://caniuse.com/mdn-api_decompressionstream_decompressionstream_zstd)), behind
  `dom.compression_streams.zstd.enabled`, which is `false`
  ([StaticPrefList.yaml](https://searchfox.org/firefox-main/source/modules/libpref/init/StaticPrefList.yaml)).
  Chromium's request ([issue 348499728](https://issues.chromium.org/issues/348499728)) was closed for want of a
  specification (as search results quote it; the issue page needs a sign-in).
- RUN: Playwright's Firefox 148 with the preference on decoded these frames natively. Native was about three times
  faster than fzstd on the 29 MB of prototype A (72 to 78 ms against 223 to 247 ms) and no faster on the 136 MB of
  prototype B (278 to 284 ms against 290 to 363 ms).
- `DecompressionStream('brotli')` is in the standard and ships in Firefox 147 and later
  ([bug 1921583](https://bugzilla.mozilla.org/show_bug.cgi?id=1921583)) and Safari 18.4 and later, iOS included, but
  not in Chrome or Edge ([caniuse](https://caniuse.com/mdn-api_decompressionstream_decompressionstream_brotli)).
  Section 7, Q5.

### 3.6 Fallback decoders, and fzstd's 32 MB limit (RUN, WEB, READ)

| Decoder | Size the page carries | Notes |
|---|---|---|
| fzstd 0.1.1, pure JavaScript, MIT | 8,394 bytes | Decoded 5.25 MB to 136 MB in 267 to 272 ms in Chromium; the 23.4 MB page frame in 67 to 76 ms |
| zstddec 0.3.4, Wasm in a module | 75,676 bytes | Needs the decoded size up front (its README); not timed |
| brotli-dec-wasm 2.3.2 | 208,439 bytes of Wasm | Carries brotli's dictionary; not timed |

**The 32 MB limit is fzstd's.** Its README: "unlike the reference implementation, `fzstd` only supports a maximum
backreference distance of 2^25 bytes. If you need to decompress files with an "ultra" compression level (20 or
greater) AND if your files can be above 32MB decompressed, `fzstd` *might* fail to decompress properly"
([fzstd](https://github.com/101arrowz/fzstd)).

- **It does not reach these frames.** A zstd back-reference cannot reach further back than the frame's window, and
  these frames use 2^23 (section 3.3), so no back-reference exceeds 8 MB, a quarter of the limit. fzstd decoded the
  102.8 MB diagram frame and the 136 MB frame of prototype B in all three engines (RUN).
- **It did not reproduce at all in 0.1.1** (RUN, `fzcheck.js` and `fzstream.js`, `results/fzstd-limit.txt`). Input:
  40 MiB of random bytes, then a copy of its first 4 MiB, so that the tail can only be a match 40 MiB back. Written
  with a 2^27 window and long-distance matching, and at level 22, both frames used that match (each about 4 MB
  smaller than the input). fzstd decoded both byte for byte, through its one-shot `decompress` and through its
  streaming `Decompress` fed in 64 KiB pieces. So did the diagram sources written the same two ways.
- **The plan keeps the 2^23 window regardless.** RFC 9659's cap needs it for a native decoder; the README's warning
  is the author's own and may cover cases not tried here; and a larger window saves only 2.4% (section 3.3). R1 pins
  the window with a test (section 5, test 3).

Two more properties of fzstd, both handled: its own check rejects only windows over 2,145,386,496 bytes (READ, its
`err(1)`), and it does not verify the frame checksum (READ: no XXH64 in its code). The loader therefore decodes text
with `TextDecoder('utf-8', { fatal: true })` and compares each frame's decoded length with the length the page
records; the native path verifies the checksum itself.

**fzstd's open issues (WEB, read 2026-10-23):** [#19](https://github.com/101arrowz/fzstd/issues/19), open since
December 2025, reports wrong output, with no exception, from the streaming `Decompress` past about 128 KiB of
repetitive text; another commenter saw out-of-bounds writes, and the maintainer suspects the distance bits and has not
fixed it. [#26](https://github.com/101arrowz/fzstd/issues/26) reports wrong output on big-endian hosts. RUN
(`fz19.js`, `results/decoders-2.txt`): #19's recipe, written by libzstd 1.5.7 at level 3 and at level 9 with a 2^23
window, from 120 KB to 12 MB, decoded byte for byte through `decompress` and through `Decompress` fed whole and in
pieces of 64 KiB, 4 KiB, 1,000 bytes and 1 byte. Not reproduced, but a decoder that can be wrong without saying so,
and that does not check the checksum, is a risk for every reader.

**libzstd's own decoder in Wasm (RUN, `dec-compare.js`):** zstddec 0.3.4 (MIT and BSD-3-Clause, 75,676 bytes as
shipped, the Wasm inline) decoded this report's frames to the same bytes as fzstd. Times on the main thread, the two
interleaved, four runs after a warm-up (`dec-browser.js`), in ms:

| Engine | Page frame (23.4 MB), fzstd | Page frame, zstddec | Diagram sources (102.8 MB), fzstd | Diagram sources, zstddec |
|---|---|---|---|---|
| Chromium 147 | 50 to 62 | 12 to 15 | 191 to 213 | 46 to 48 |
| Firefox 148 | 51 to 67 | 69 to 72 | 197 to 214 | 224 to 235 |
| WebKit 26.4 | 35 to 57 | 16 to 19 | 134 to 145 | 52 to 62 |
| Node 25.9 (under load) | 45 to 54 | 12 | 164 to 173 | 45 to 46 |

Its own
`decode` ignores libzstd's return value, so a frame with a bad checksum came back as other bytes without an error; a
vendored copy with a few lines of glue of Kronikol's own would check `ZSTD_isError` and so verify the checksum.
Section 7, Q14.

### 3.7 Putting the bytes in the page (WEB, INFERRED)

- **base64:** +33%, ASCII only, safe however the page is served. Chosen.
- **A `windows-1252` page carrying near-raw bytes** (+1.6%, with NUL, CR and `<` escaped): rejected. An HTTP
  `Content-Type` charset takes precedence over the page's `<meta charset>`, so a host that serves the report as UTF-8
  (GitHub Pages does) would corrupt the payload.
- **A UTF-8-safe denser encoding** (in the style of base-122, about +14%): about 6.0 MB for this report, at the cost
  of more decoder code. Section 7, Q4.

### 3.8 Rebuilding the page (RUN)

`document.open()`, `document.write(page)`, `document.close()` replace the document in the same window. Checked in
Chromium and Firefox against today's page (`results/functional.txt`): the address and its `#` fragment are kept, the
rebuilt document fires `DOMContentLoaded` and `load` (all three engines), Back after an in-page link stays in the same
document with no second decode, a diagram is drawn through the page's own pipeline with the engine from jsDelivr,
and no page errors occur. The report's scripts start on `DOMContentLoaded` (15 listeners, none on `load`, READ from
the page). A deep link to an element 60% down the page did not scroll into view in either page; whether the harness
picked an element the page scrolls to was not checked, and R1's deep-link fact compares with the plain page anyway.

What outlives the rebuild (RUN, `docopen-check.js`, `results/docopen.txt`, all three engines): window properties and
`var` names do; so does a top-level `let`, `const` or `class` in the loader, and a report script that declares the same
name then fails whole ("Identifier 'krnTop' has already been declared"); listeners on the window or the document added
before `document.open()` are erased, and listeners added after it serve the rebuilt page. `Uint8Array.fromBase64`
exists in all three builds, and `DecompressionStream('zstd')` throws in all three.

### 3.9 Load times and memory (RUN)

C2 against today's page is the table in section 0 (`results/bench-c2.tsv`). The other prototypes, Chromium 147, three
runs each (`results/bench-variants.tsv`; C2's row from `results/bench-c2.tsv`). Here "painted" is the first paint
after the rebuilt page's `load`, the one measure both files share:

| Prototype | File | Painted after `load` | JS heap + `ArrayBuffer` |
|---|---|---|---|
| A: today's page in one frame | 16,347,583 | 1,056 to 1,076 ms | 37 + 25 MB |
| B: raw items in one frame | 7,012,279 | 1,967 to 2,089 ms | 207 + 17 MB |
| C: two frames, diagrams decoded on the main thread | 6,976,609 | 1,377 to 1,650 ms | 24 + 115 MB |
| C2: two frames, diagrams decoded in a worker | 6,977,369 | 697 to 722 ms | 34 + 112 MB |

B holds the whole decoded page as strings; C's diagram decode on the main thread held up the first paint, which C2's
worker removes. In WebKit every page, today's included, fires `load` 26 to 38 s after it is up (step F1).

### 3.10 The writer's cost (RUN)

ZstdSharp 0.8.8 at level 9 with a 2^23 window writes the same bytes as libzstd: 12.3 MB of markup in 100 to 160 ms,
the 102.8 MB of diagram sources in 348 to 368 ms, the whole 136 MB raw page in 471 to 490 ms. The compressed page also
skips today's 1,553 separate gzip calls, which were not timed.

### 3.11 What the prototypes did not cover

- **Export** (`report-export-function.js`), the merge path (`kronikol merge`), the component diagram report, a
  corrupt payload, a page with JavaScript off, the dark theme on the loading screen, and printing.
- **Deep search** was not covered by the first prototypes: their search worker called a function on `window`, which a
  worker does not have. The audit's prototype carries section 4.4's design (section 3.12).
- **WebKit's memory**, and every engine's times for the audit's prototypes, which ran under another session's load.
- **Other reports:** one report, written by 5.4.4. A report with few diagrams gains less; one with more gains more.

### 3.12 Deep search and memory, with the diagram sources in pieces (RUN)

`make_c2s.py` builds C2 with section 4.4's search path and the diagram sources in one frame, in 7 pieces of up to 16 MB
of source, or in 14 of up to 8 MB (6,978,953, 7,154,045 and 7,449,278 bytes). With pieces the page decodes a piece in a
worker the first time a diagram in it is asked for, and keeps at most two decoded; the search worker does the same on
its side.

Deep search (`search-check.js`, `results/search-check.txt`): three words found in exactly one diagram source and
nowhere in the page's text, from diagrams at 10%, 50% and 90% of the list, and two found only in flame spans. In
Chromium and Firefox every layout found the same scenario for each word as today's page did, with no page errors.

Memory (`mem.js`, `results/memory.txt`): every process of the browser, private bytes, less the same browser on
`about:blank`, in MB, after the report is up, after the diagrams of six scenarios are drawn, and after a deep search
that verifies every scenario:

| Page | Chromium | Firefox |
|---|---|---|
| Today's page | 1,257, 1,253, 1,595 | 1,979, 1,687, 1,752 |
| C2, one diagram frame | 1,332, 1,398, 1,750 | 1,983, 1,979, 2,126 |
| C2, 16 MB pieces | 1,246, 1,324, 1,729 | 1,927, 1,798, 1,864 |
| C2, 8 MB pieces | 1,242, 1,316, 1,597 | not run |

The page itself dominates. In Firefox one frame holds 290 to 375 MB more than today once diagrams are drawn and a
search has run, because the page and the search worker each hold the whole 102.8 MB decoded; 16 MB pieces hold about
100 MB more. In Chromium the layouts are within about 160 MB of today's page and of each other. With one frame the cost
also grows with the report, where pieces hold at most two on each side. Q2 recommends 16 MB pieces.

### 3.13 What else reads the page (READ)

From three searches of the repo, the wiki and Kronikol4J, each site then read at its line:
- **Its file's text:** the weekly doorstep workflow fetches the live demo's report and greps it for
  `data-stable-id="..."` (`.github/workflows/doorstep.yml:55-56`); Kronikol4J's parity harness reads .NET's pages as byte-parity fixtures (its `parity-harness/dotnet-capture/Program.cs`, for one `:286`); the example
  project's `ReportParser` reads the page with AngleSharp, `#puml-data` included
  (`examples/Example.Api/tests/Example.Api.Tests.Integration/Helpers/ReportParser.cs:27-269`, used by five test
  classes); and three repo tools (`tools/search-bench/real-calibrate.js:14-19`, `tools/render-bench/bench-report.js:17-23`
  and `:72-83`, which also waits for `load`, and `tools/history-bench/jsd/export.js:3-32`).
- **The C# side:** `AddWholeTestFlowSearchPieces` (`ReportGenerator.cs:2763-2784`) decodes the activity diagrams'
  `data-plantuml-z`, their `#puml-data` entries and `data-flame-z` from the emitted fragments to build the search
  index.
- **Merge's stored fragments:** a mergeable data file carries each test's whole-test flow as HTML fragments with inline
  gzip and base64 `data-plantuml-z` and `data-flame-z` (`ReportGenerator.cs:5474-5488`, written at `:5609-5615`), which
  `kronikol merge` puts in the page as they are (`:3365-3368`). Every data file written so far holds gzip there.
- **A second full page:** `Specifications.html` (`ReportGenerator.cs:532`), on by default.
- **The export** (`report-export-function.js:114-136`) copies the head, which holds `#iflow-segments`, the visible
  features and every body script, pruning only `#puml-data` and `#step-data`.
- **Docs** that tell people to read the file's text or describe its blobs: the wiki's `Generated-Reports.md` (its
  "Diagram Storage Format" recipe at `:1365`, a `Get-Content` and a pattern over `data-plantuml-z`; its advice on
  searching the HTML at `:1514`), `Migrating-to-v5.md:94`, `Step-Tracking.md:268` and `Report-Configuration.md:173` (set
  `RenderCollapsedSubStepsOnExpand = false` to find a sub-step's markup in the page's HTML, which a compressed page no
  longer has as text), `Capture-Time-Redaction.md` (the HTML reports among the files that hold captured data),
  `PlantUML-Browser-Rendering.md:44`, `Internal-Flow-Tracking.md:197` and `API-Reference.md:40`.
- **Nothing** changes the page's string between building and writing it; no product code opens it in a browser,
  prints it or takes a screenshot; `kronikol decode` refuses an `.html` today (`src/Kronikol.Tool/DecodeCommand.cs:81-85`).

### 3.14 Deep search fails in WebKit today (RUN)

In Playwright's WebKit 26.4 (the Windows build) the deep-search index never loads, in today's page as in the
prototypes: inside a worker made from a `blob:` URL every read of a `Blob` fails with "NotReadableError: The I/O read
operation failed." (`Blob.stream()`, `Blob.arrayBuffer()`, and `Response` over either), and the search worker gunzips
its index through `new Blob([...]).stream()` (`report-search-index.js:307-314`). A `ReadableStream` built from the
bytes (`start(c) { c.enqueue(bytes); c.close(); }`) decodes there (`wk-search-probe.js`,
`results/webkit-worker-blob.txt`). Whether Safari on macOS does the same is not known. Step F2 fixes it, and the loader
and its workers never read a `Blob` (section 4.2).

## 4. The design (C2)

### 4.1 The file

```html
<!doctype html>
<html lang="en"><head>
<meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="generator" content="Kronikol 5.x.y">
<meta name="kronikol-page" content="frames 1">
<title>{the report's own title}</title>
<script>{the report's theme boot, as today}</script>
<style>{the loading screen: light and dark tokens, following the theme boot's choice}</style>
</head><body>
<div id="kron-loading" role="status" aria-live="polite">Opening the report...
  <p>{shown after a few seconds by CSS alone: what to do if this stays}</p></div>
<noscript>This report is compressed and needs JavaScript to open. {kronikol decode}</noscript>
<script id="kron-frame-page" type="application/octet-stream" data-codec="zstd" data-length="{decoded bytes}">{base64}</script>
<script id="kron-frame-diagrams-1" type="application/octet-stream" data-codec="zstd" data-length="{decoded bytes}">{base64}</script>
<script id="kron-frame-diagrams-2" ...>{one element per piece of about 16 MB of diagram source}</script>
<script id="kron-zstd">{the fallback decoder, vendored (Q14)}</script>
<script>{the loader}</script>
</body></html>
```

- Each zstd frame: level 9, window log 23 set explicitly (never left to the level's default), the content size in the
  frame header, the checksum on (section 4.6).
- The diagram sources go in pieces of about 16 MB, cut at diagram boundaries (Q2, from S0); a source larger than a
  piece gets a piece of its own. The size is the writer's alone, so changing it later touches nothing else.
- `data-codec` is `zstd` for what Kronikol writes. The loader also takes `gzip`, which every engine decodes natively:
  the export writes it (section 4.5), and Kronikol4J could write it with `java.util.zip` alone (section 4.9).
- `kronikol-page` marks the file for tools and tests (section 4.6 and Q9).
- The title and the theme boot make the tab read right and the loading screen match the reader's theme from the
  first paint. The loading text's second line appears after a few seconds through a CSS animation delay, so a viewer
  that runs no script still says what to do (with inline styles blocked too, it shows at once), and a fast open never
  shows it.
- A page the writer leaves empty stays empty: three tests pin an empty page as empty (`ReportToggleTests.cs:221`,
  `IngestCommandTests.cs:887`, `:927`), and the writer writes one (`ReportGenerator.cs:1451`).

### 4.2 The loader

1. **One function expression, nothing at the top level.** The window and its global scope outlive the rebuild, so a
   top-level `let`, `const` or `class` in the loader would stop any report script that declares the same name (section
   3.8). What the page needs from the loader is a property of `window`: `window.__kronikolFrames` (the index, the
   pieces' bytes, the decoded-piece cache) and `window.__kronikolLoader` (its own source and the decoder's, for the
   export).
2. Read each frame's bytes: `Uint8Array.fromBase64` where the browser has it (all three engines here, RUN), else `atob`
   and a loop.
3. Paint first: `requestAnimationFrame`, then `setTimeout(0)`, before any decoding.
4. Decode the page frame: `new DecompressionStream('zstd')` inside a `try`, else the vendored decoder (Q14); a `gzip`
   frame through `DecompressionStream('gzip')`. Streams are built from the bytes (`new ReadableStream`), never by
   reading a `Blob`, which fails inside WebKit's workers (section 3.14). Decode the text with
   `TextDecoder('utf-8', { fatal: true })` and check its length against `data-length`.
5. `document.open()`; then, so that `document.open` cannot erase it, the readiness listener: on the rebuilt page's
   `DOMContentLoaded` it sets `window.__kronikolReady` and dispatches `kronikol:ready` on the document; then
   `document.write(page)` and `document.close()`. A page frame over 16 MB is decoded and written in slices through a
   streaming `TextDecoder`, so no single string nears V8's limit of about 2^29 characters (INFERRED; this report's page
   frame is 23 MB).
6. A diagram piece is decoded the first time a diagram in it is asked for, in a Web Worker made from the decoder's text
   and a small handler (native first there too), the bytes transferred in and out; at most two decoded pieces are kept.
   If a worker cannot be made (a Content Security Policy that refuses `blob:` workers), on the main thread.
7. On any failure, replace the loading text with what failed and how to get a plain copy (`kronikol decode`, Q9).

### 4.3 Inside the page: one accessor for every item

`report-decompress-helper.js` learns the compressed page. With `window.__kronikolFrames` set, a value that is a diagram
token resolves from its piece (`TextDecoder` over the diagram's slice, the piece decoded on first use); any other value
is already raw text and resolves as it is. Without it, today's gzip and base64 path, which plain pages keep using (the
component diagram page, pages written before R1, Kronikol4J's). Every copy on a page is the same file, so they all
behave the same.

- `#puml-data` maps each id to a token instead of a gzip string: a string starting with U+0001 and the diagram's
  number, as the prototypes did. A string keeps `getPumlZ`'s `||` fallback (`plantuml-browser-render-script.js:709-715`)
  working, where a number 0 would not.
- The diagram index, `[piece, start, end]` per number, is a JSON block in the page frame.
- `#step-data`, `#iflow-segments`' `z`, `data-flame-z`, and `data-plantuml-z` on the merge path are written raw (JSON-
  or attribute-escaped), so the accessor returns them as they are. The writer makes that hold for merge's stored
  fragments too, which are gzip in every data file written so far (section 4.6).
- The search index stays gzip and base64 in R1 (Q6): its reader is the worker's own `gunzipToBytes`, and inside the
  page frame its 1,035,322 bytes as stored compress to 777,168, where the raw binary index would take 719,857 (RUN,
  `search_index.py`).

### 4.4 The search worker

The page posts `#puml-data`'s text to the search worker, and the worker decodes sources itself to verify a match
(`report-search-index.js:335-342`, the post at `:571-576`). In a compressed page:
- its `gunzipToText` passes raw text through (flame charts and `data-plantuml-z`), told the page's mode in the message
  that starts it;
- for diagram tokens, the page posts the pieces' compressed bytes (3.6 MB here, a copy), the index and the decoder's
  source in that message (the worker is built from a roster of function sources, `:502-519`, so the decoder's text
  goes ahead of them), and the worker decodes a piece the first time a verify needs it, keeping at most two;
- its streams are built from bytes, never from a `Blob` (section 3.14).

RUN: this design, in `make_c2s.py`, finds the same scenarios as today's page in Chromium and Firefox, with one frame and
with pieces (section 3.12). With pieces the search worker holds at most 32 MB of decoded source. One decoded copy
shared with the page through a `MessageChannel` would save that, at the cost of more plumbing, and is not needed.

### 4.5 Export

`export_html` (`report-export-function.js:114-136`) writes the head (with `#iflow-segments` and every script), the
visible features (`export_undrawn`, `:90-113`) and the body's scripts, pruning `#puml-data` and `#step-data` to the
entries it keeps (`export_data_scripts`, `:47-77`). From a compressed page those blocks hold tokens and raw text, and
the exported file would have no frames for them. So from a compressed page the export writes a compressed page itself:
- the markup it builds today, tokens kept, gzipped by `CompressionStream('gzip')` (native in every engine) into a
  `gzip` page frame;
- the diagram pieces its entries name, copied as they are, still compressed, with the index;
- the loader and the decoder, from `window.__kronikolLoader`.

No item is re-encoded, and the file stays small. From a plain page the export is today's (Q7).

### 4.6 The writer

- **Where:** `GenerateHtmlReportCore` (`ReportGenerator.cs:1396`) keeps building the page. Each item site
  (Appendix A.1) writes its text raw through one function, which also collects the diagram sources for the pieces. At
  `ReportGenerator.cs:2720`, `WriteFile` (`:6918-6957`, whose fallback name at `:6937-6941` takes the same bytes)
  receives the loader page in place of the report page.
- **Both full pages:** `TestRunReport.html` (`:537`) and `Specifications.html` (`:532`, on by default) go through
  `GenerateHtmlReportCore`, from a run, from `kronikol ingest` (`src/Kronikol/Ingestion/IngestPipeline.cs:694`) and from
  `kronikol merge` (`src/Kronikol/Reports/Merge/MergeableReportRenderer.cs:93`), so all of them are compressed (Q8).
- **Merge's stored fragments:** a mergeable data file keeps each test's whole-test flow as HTML with gzip and base64
  `data-plantuml-z` and `data-flame-z` (`ReportGenerator.cs:5474-5488`, `:5609-5615`), and every data file written so
  far has them, so the data file stays as it is. When the page is compressed, the writer decodes those attributes to raw
  text as it takes the fragments in (`ResolveWholeTestFlowContent`, `:3365-3368`), with the same pattern
  `AddWholeTestFlowSearchPieces` uses (`:2763-2784`), so that a compressed page holds no gzip but the search index.
- **The search index:** `AddWholeTestFlowSearchPieces` (called at `:2651`, `:3996`, `:4008`, `:4030`, `:4042`)
  reads the raw text instead of decoding, so the index is built from the same text as today (section 5, test 7e).
- **The frames:** the vendored ZstdSharp 0.8.8 has what they need (`SetParameter` at `:48` and `SetPledgedSrcSize` at
  `:228` of `src/Kronikol/Compression/Zstd/CompressionStream.cs`). Each piece is written through one `CompressionStream`
  with its length pledged, so its header carries the content size and the writer never holds the sources as one 103 MB
  buffer. The `.krkl` writer sets only the checksum (`src/Kronikol/Compression/ReportFileWriter.cs:30`); these frames set
  the window log too.
- **Repeatable output:** `allWtfIdentical` (`ReportGenerator.cs:3914-3921`) compares whole-test HTML with its blobs in
  it, and four test classes compare two pages' text (section 4.7). zstd at a fixed level and window gives the same bytes
  for the same input, as gzip does today.
- **Other pages** (the component diagram report, the Labs and diagnostic pages) carry one small diagram or none, and
  stay as they are (Q8).
- **Tests and tools that read the HTML:** a test helper decodes a compressed page (frames, then the page with tokens
  resolved), and `kronikol decode` does the same for people (Q9); section 4.7 has what moves onto them.

### 4.7 No option (the owner's decision 4)

Every run, `merge` and `ingest` writes the compressed pages; there is no setting to turn it off and no public type
changes. What follows from that:

- **The version:** not a major (the owner's ruling). R1 is a minor because `kronikol decode` learns the compressed
  page (Q9), which is new for a consumer to call; the page change on its own would be performance work, a patch.
- **Tests that read the generated HTML:** 131 test files read the generated page from disk (120 in `Kronikol.Tests`,
  10 in the E2E project, 1 in `Kronikol.Tests.ProxyTap`; the first draft's 251 did not reproduce), 17 name the item
  blocks, and there is no shared reader: 68 files have a private generate-and-read method. They move onto one helper
  that returns the decoded page, modelled on `ReportFile` (`tests/Kronikol.Tests/ReportFile.cs:14`, `:27`, `:36`), which
  already decodes the `.krkl` for 45 readers, so that they read what ships (Q13). Whole-page comparisons
  (`ToggleDefaultsBaselineTests.cs:272-282`, `ExamplesBlockRenderingTests.cs:162`, `WarmUpHtmlTests.cs:156`,
  `SearchIndexReportTests.cs:267-271`) hold on repeatable output; `CultureInvariantPipelineTests.cs:45-66` strips markup
  by a pattern (`:109-112`) and needs the decoded page. No test runs the page in a JavaScript engine outside a browser.
- **Browser tests:** the loader page's own `load` fires before the report is rebuilt, and the suites open reports with
  `GotoAsync`'s default wait for `load` (759 calls in 178 files, none with another `WaitUntil`), through many
  per-class helpers (32 classes have their own `OpenAsync`). A locator action waits for its element and so for the
  rebuild; the 24 `EvaluateAsync` calls made straight after opening a generated report would run against the loader
  page, and so would anything straight after a `ReloadAsync` (20 calls) or a `WaitForURLAsync` to a report. Init
  scripts that hook `DOMContentLoaded` to wrap the report's functions (`LongPayloadScrollTests.cs:1085`, `:1223`;
  `BrowserRenderWorkerTests.cs:39`) would see only the loader page, since `document.open` erases their listeners and
  init scripts do not run again. So the suites' opening helpers wait for `window.__kronikolReady`, and those hooks
  listen for `kronikol:ready` (section 4.2). CI runs Chromium only (`.github/workflows/ci.yml:266`, `:272`); the
  Firefox and WebKit cases run where those engines are installed.
- **Kronikol4J** keeps writing the plain page until it is ported: a divergence ledger line, as `CLAUDE.md` asks for a
  change to report output (Q11). Its parity harness reads .NET's pages as fixtures (section 3.13), so it decodes them
  first, and parity is of the decoded page: two compressors never give the same bytes.
- **Readers of the file's text** (section 3.13): the doorstep workflow decodes the demo's page before its grep; the
  example project's `ReportParser` reads the decoded page; the repo tools learn it; the docs there change with R1.
- **What a reader can notice:** a text search or view-source of the file finds base64 (the changelog and the wiki point
  to `kronikol decode`); a tool that waits for `load` and then reads the page (a headless print or screenshot, a
  script) sees the loading screen, since the loader page's `load` comes first; and a viewer that runs no script shows
  the loading text where today it shows the page's markup. Jenkins' default policy for the files it publishes
  (`sandbox allow-same-origin; default-src 'none'; img-src 'self'; style-src 'self';`,
  [Jenkins](https://www.jenkins.io/doc/book/security/configuring-content-security-policy/)) blocks scripts and inline
  styles, so there today's page is unstyled markup without diagrams or controls (Q10).

### 4.8 The fallback decoder, vendored

Whichever Q14 picks goes in as an embedded resource, pinned by version and hash, with a `THIRD-PARTY-NOTICES.md` entry
beside ZstdSharp's, and an update rule like ZstdSharp's (`tools/vendor-zstd`, `.github/workflows/vendor-zstd.yml`):
from tagged releases, read before taking.
- fzstd 0.1.1: `umd/index.js`, 8,394 bytes, MIT, Arjun Barrett.
- libzstd's decoder in Wasm: the `.wasm` of a pinned zstddec release (MIT and BSD-3-Clause), or one built from the zstd
  sources at a tagged release, with a few lines of glue of Kronikol's own that check `ZSTD_isError`, so that a checksum
  that does not match is an error; fzstd stays beside it for a page where Wasm is refused.

### 4.9 Kronikol4J

Kronikol4J keeps writing the plain page, which the accessor's gzip path keeps working. Its divergence ledger
(`docs/REMAINING_PARITY.md`) records that the compressed page is not ported (Q11), and its parity harness, which reads .NET's pages, decodes them before comparing. When it is ported, `gzip` frames need nothing outside the JDK
(about 15 MB for this report in place of 29 MB, INFERRED from the 11.4 MB that gzip makes of the raw page, section
3.2); zstd frames would need a Java encoder.

### 4.10 Not in this plan

The `.krkl` (unchanged); the engine; the plain page's own per-item gzip (stays as it is); a sidecar file next to the
page (the report stays one file); levels above 9 (Q3).

## 5. Tests, red first

Writer (core suite):
1. Every writer path (a run, `kronikol merge`, `kronikol ingest`) writes both full pages as loader pages, never plain;
   a page the writer leaves empty stays empty.
2. The file is the loader page of section 4.1: the report's title, the loading screen and its later hint, the marker,
   one frame element per frame with its codec and decoded length, the decoder, the loader, and no report markup outside
   the frames.
3. Each frame's header: for content over 8 MB the window is exactly 2^23 (level 9's own default is 2^22, so a writer
   that forgot to set it fails; one that set 2^27 fails too), and for smaller content the frame is single-segment,
   its window the content size; the content size present; the checksum flag set. This is the guard for RFC 9659's
   8 MB cap and for fzstd's documented 2^25 back-reference limit.
4. Round trip: decoding the page frame and resolving its tokens gives every item the plain page holds, equal after
   decoding: `#puml-data` through the index, `#step-data`, `#iflow-segments`, `data-flame-z`, `data-plantuml-z` on the
   merge path, the component diagram; the search index unchanged.
5. The vendored decoder, run in Node, decodes every frame of a fixture whose content is over 32 MB byte for byte through
   the call the loader makes; with the Wasm decoder, a frame whose checksum is wrong is refused. There is no failing
   control for fzstd's 2^25 limit, since it did not reproduce (section 3.6); test 3 is the guard.
6. The diagram index: every token resolves to its own source, the first and the last included, and across a piece
   boundary; a source larger than a piece has a piece of its own.
7. A merged page (`kronikol merge`) and an ingested one round-trip the same way.
   - 7a. `kronikol decode TestRunReport.html` writes a plain page whose items equal the compressed page's after
     decoding, and which opens and works in a browser (test 13's checks).
   - 7b. The test helper of section 4.7 returns, for a fixture, the same decoded page that `kronikol decode` writes.
   - 7c. A merge of a data file written by today's release (gzip fragments) gives a compressed page that holds no gzip
     but the search index, and whose items equal the plain merged page's.
   - 7d. `Specifications.html` is compressed and round-trips.
   - 7e. The search index of a run with compressed pages equals today's, byte for byte.
   - 7f. Two runs over the same input write the same bytes (the whole-page comparisons and `allWtfIdentical` rely on it).

Page (Playwright, the bundled Chromium; Firefox and WebKit where the suite runs them):
8. The loading screen paints, then the report replaces it, with no page errors.
9. A deep link to a scenario lands where it lands in the plain page.
10. Back and Forward after in-page navigation stay in the document, with no second decode.
11. A diagram, a flame chart, an internal-flow popup, held sub-steps on expand and the component diagram all work,
    with diagrams from the first and the last piece.
12. Deep search finds text that is only in a diagram source (from pieces far apart), only in a flame span, and only in
    internal-flow text: the same scenarios as the plain page.
13. Export from a compressed page writes a compressed page (a `gzip` page frame and the pieces it needs) that opens and
    works: diagrams drawn, sub-steps open, deep search.
14. The dark theme gives a dark loading screen, and the theme switch works after the rebuild.
15. A corrupted frame shows the failure in the loading screen, never a blank page; so does a frame whose checksum is
    wrong, with the Wasm decoder.
16. With workers refused, the diagrams still draw (main-thread decode).
17. Firefox with `dom.compression_streams.zstd.enabled`: the native path decodes (if the suite runs Firefox; else a
    unit test of the loader's branch).
18. The suites' opening helpers wait for the rebuilt report: with the loader slowed down, an `EvaluateAsync` straight
    after the helper still sees the report, never the loader page; an init script that listens for `kronikol:ready`
    sees the report's functions.
19. A report script that declares, at its top level, every name the loader uses still runs after the rebuild (the
    loader declares none).
20. With JavaScript off (a context with `JavaScriptEnabled = false`), the notice and the loading text's hint show.

Each new fact fails on the unchanged code first, and each change is shown to matter by mutation, as the repo's plans
do.

## 6. Steps and releases

| Step | What | Version |
|---|---|---|
| **S0** | Done in the audit (section 3.12): whole-browser memory in Chromium and Firefox for today's page, one diagram frame, and 16 MB and 8 MB pieces, and deep search with pieces. Answer: 16 MB pieces (Q2). Left for R1's own measurements: WebKit's memory, and the time to a diagram in a piece | none |
| **F2** | Deep search in WebKit (section 3.14 and below) | a patch, before R1 or with it |
| **R1** | Both full pages compressed from a run, `merge` and `ingest`, with no option (section 4); merge's stored fragments decoded; the export writing a compressed page; `kronikol decode` for the page; the decoder of Q14, timed in the three engines first; the tests and tools that read the HTML moved onto the decoding helper, and the browser suites' helpers and init scripts onto the readiness event; section 5's tests; the docs and the records below | a minor (section 4.7) |
| **F1** | WebKit's late `load` (below) | none, or a patch if the cause is the page's |
| later | Each its own go: level 19 (Q3), a denser encoding (Q4), brotli (Q5) | |

F1 and F2 need nothing from R1 and can run in either order, before it or alongside. Version numbers are claimed at
push, as usual.

### F1: why WebKit fires `load` 26 to 38 s late

**Seen (RUN):** in Playwright's WebKit 26.4 (the Windows build), today's page fires `DOMContentLoaded` at 727 to 875
ms and is painted at 2.5 to 2.9 s, but fires `load` at 26.5 to 38.0 s, in all six runs. The compressed prototypes
do the same (25.6 to 36.0 s), so compression neither causes nor cures it. Chromium and Firefox fire `load` 0.2 to
0.3 s after `DOMContentLoaded`.

**Why it matters:** the report starts on `DOMContentLoaded`, so it works. But a tab that counts as loading for half a
minute shows a progress indicator that long, and anything that waits for `load` waits too: Playwright's `goto` by
default, printing tools, people's own scripts. If real Safari behaves the same, every Safari reader sees it.

**The first lead (READ):** when the page draws on its main thread, `useMainThreadEngine`
(`plantuml-browser-render-script.js:439`) calls `loadEngineScripts()` (`:463`, defined at `:486`), which adds
`<script src>` elements for the engine from jsDelivr through `addScript` (`:465-475`). A script element added before
`load` holds `load` until it loads or fails. That route is taken, for one, when a worker cannot be made (`:248`).
About 30 s is a typical network time-out.

**Method:**
1. Read the render script's own telemetry (`engine`, `engineProblem`) in WebKit to see which route it took.
2. Record `performance.getEntriesByType('resource')` and Playwright's request, requestfinished and requestfailed
   events, with times, from navigation to `load`.
3. Run with the browser context offline (`setOffline`, which cuts the network and mocks nothing) and see whether
   `load` then comes at once.
4. Cut a copy of the page down (scripts and elements out, by halves) until `load` comes on time.
5. Try a small report, to see whether size matters.
6. Try real Safari on macOS (a GitHub macOS runner) and WebKit on Linux (the podman runner), to learn whether readers
   see it or only the Windows build does.

**Outcome:** if the cause is the page's, a patch with a WebKit fact that `load` comes within a few seconds of
`DOMContentLoaded` (which needs the E2E suite to run WebKit, a question for then). If it is the Windows build alone,
a line in this plan's log, and stop.

### F2: deep search in WebKit

**Seen (RUN, section 3.14):** in Playwright's WebKit 26.4 (Windows), deep search never becomes ready, in today's page as
in the prototypes, because every `Blob` read inside the search worker fails.

**Method:** a WebKit fact (the suite's `InEngineAsync` runs WebKit where it is installed) that a word found only in a
diagram source is found, red on today's code; then `gunzipToText` and `gunzipToBytes`
(`report-search-index.js:307-314`) build their streams from the bytes; every other place a worker reads a `Blob` gets
the same look; then the same check on Safari on macOS (a GitHub macOS runner) and on WebKit on Linux (the podman
runner), to learn whether readers see it or only the Windows build does.

**Outcome:** a patch, and a line in this plan's log either way.

### Records (R1)

- `CHANGELOG.md`: the compressed pages, the minor bump and why (section 4.7), `kronikol decode` for anyone who searched
  the HTML file, and what a reader can notice (section 4.7).
- The wiki: `Generated-Reports.md` (the page, the loading screen, `kronikol decode`; its "Diagram Storage Format"
  recipe and its advice on searching the HTML, section 3.13), `Querying-Reports.md` (`kronikol decode`),
  `PlantUML-Browser-Rendering.md`, `Internal-Flow-Tracking.md`, `API-Reference.md`, `Migrating-to-v5.md`,
  `Step-Tracking.md` and `Report-Configuration.md` (`RenderCollapsedSubStepsOnExpand = false` no longer puts sub-step
  markup in the file's text), and `Capture-Time-Redaction.md` (a text search of the HTML file finds nothing in a
  compressed page). The other pages that name `CompressTestRunReport` (`CI-Artifact-Upload.md`,
  `Ingesting-External-Captures.md`) to be read for the same.
- `README.md` and the skill's `references/commands.md` (both copies) for `kronikol decode`.
- `.github/workflows/doorstep.yml`, the example's `ReportParser.cs` and the repo tools of section 3.13.
- `THIRD-PARTY-NOTICES.md`: the decoder (Q14).
- Kronikol4J's ledger line and its harness; this plan's log and `PLANS_STATUS.md` row.

The rules that say never to open `TestRunReport.html` (`CLAUDE.md`, `templates/agents/CLAUDE.md`, the skill) stay as
they are: a compressed page is no more readable than a plain one.

## 7. Questions, with recommendations

| # | Question | Recommendation |
|---|---|---|
| Q1 | An option to turn the compressed page off? | **Answered by the owner on 2026-10-23: no option, and not a major** (decision 4, section 4.7) |
| Q2 | One diagram frame or pieces? | **16 MB pieces**, from S0 (section 3.12): 7,154,045 bytes against 6,978,953 here, about 180 to 260 MB less held in Firefox once diagrams are drawn and a search has run, and bounded however large the report. 8 MB pieces make 7,449,278 bytes, 4% more again, and were measured in Chromium only, where they held less than 16 MB pieces only after a search |
| Q3 | A higher level later? Level 19 gives 4.32 MB of content in 20 s (libzstd) | Stay at 9 (the owner's decision 1). If ever, keep the window at 2^23: the ultra levels' larger windows exceed RFC 9659's cap, and fzstd's README warns about them (though 0.1.1 decoded them here) |
| Q4 | A denser UTF-8-safe encoding than base64 (~6.0 MB here)? | Later, and only if size still matters after R1 |
| Q5 | Brotli instead, for its native decoding in Firefox 147+ and Safari 18.4+? | Stay with zstd: the codec of the `.krkl`, its encoder already in the product, a far smaller fallback than brotli's 208 KB, and Chrome (most readers) has neither natively. Brotli at its faster qualities was not measured |
| Q6 | The search index raw in a frame of its own (719,857 bytes) instead of gzip and base64 inside the page frame (777,168)? | Not in R1: 57 KB is not worth a binary frame and a new reader |
| Q7 | What does the export write from a compressed page? | A compressed page: a `gzip` page frame from `CompressionStream` and the diagram pieces it needs, copied as they are (section 4.5). A plain page would mean re-gzipping every kept item, and an export of everything would be the 29 MB page again |
| Q8 | Which pages are compressed? | Both full pages that `GenerateHtmlReportCore` writes (`TestRunReport.html`, `Specifications.html`), from a run, `merge` and `ingest`; the component diagram, Labs and diagnostic pages carry one small diagram or none, and stay as they are |
| Q9 | `kronikol decode` writes a working plain copy of a compressed page? | Yes, in R1: an `.html` given to it and recognised by the `kronikol-page` marker is written back as a plain page, to standard output or `-o`, as the JSON is today (it refuses an `.html` now, `DecodeCommand.cs:81-85`). With every page compressed it is the way to a searchable file, and the tests' helper uses the same decoding |
| Q10 | A reader with JavaScript off, or a viewer that blocks scripts (Jenkins by default), sees only the loading text where today's page shows its markup? | Accept, with the loading text's hint saying what to do (open the file in a browser, or `kronikol decode`): the report is a script application already (diagrams, search, tabs), and under Jenkins' default policy today's page is unstyled markup without them |
| Q11 | Kronikol4J? | A ledger line in R1, and its parity harness decodes .NET's pages first; port when the owner wants Kronikol4J to match, with `gzip` frames (`java.util.zip`) or zstd (a Java encoder) |
| Q12 | A `ROADMAP.md` placement? | The owner's call; not placed |
| Q13 | Tests that read the HTML: through the decoding helper, or an internal switch that writes them the plain page? | The helper: tests then read what ships. A switch would keep the 131 files as they are but test a page no reader gets |
| Q14 | The fallback decoder: fzstd (8.4 KB, pure JavaScript, no checksum check, an open report of wrong output that did not reproduce here) or libzstd's decoder in Wasm (about 75 KB as base64, the reference implementation, its checksum checked through its return value)? | libzstd in Wasm, with fzstd kept for a page where Wasm is refused (a policy without `wasm-unsafe-eval`): about 1% of this report's file buys the reference decoder, and the page frame decodes about 40 ms sooner in Chromium and 20 to 40 ms sooner in WebKit, though about 15 ms later in Firefox (section 3.6) |
| Q15 | F2 first, as its own patch? | Yes: today's page has the defect, and the fix is small |

## 8. Log

- 2026-10-23 (kronikol-08): investigation at the owner's ask. Anatomy and codec measurements over the 5.4.4 report;
  native support read from the standard, caniuse, Firefox's preferences and Chromium's tracker; prototypes A, B, C
  and C2 built and timed in three engines; functional checks in two. The owner chose level 9. Plan written at the
  owner's ask, with step F1 for WebKit's late `load`. Corrected on the way: the five copies of the gzip helper are
  deliberate (`DiagramContextMenu.cs:106-116`), not a defect as first reported. Checked at the owner's prompt: the
  "32 MB limit" is fzstd's documented 2^25 back-reference distance, out of reach of 2^23-window frames; it did not
  reproduce in fzstd 0.1.1 with a forced 40 MiB back-reference, one-shot or streaming (section 3.6). A first draft's
  test 5 had a "failing control" for it, dropped as untrue.
- 2026-10-23: the owner ruled out the option ("I don't think this CompressHtmlReport should be an option at all ...")
  and ruled it not a major (decision 4). Sections 0, 4.6, 4.7, 5, 6 and 7 rewritten for it: R1 is a minor through
  `kronikol decode`, the tests that read the HTML move onto a decoding helper (Q13), and the browser suites wait for a
  readiness flag because the loader page's own `load` comes before the report.
- 2026-10-23 (kronikol-08): audit at the owner's ask ("make sure the plan is complete, accurate and make sure you do
  any deep dives that might effect the plan"), after the plan was committed (`b3898519`). Three searches of the repo,
  the wiki and Kronikol4J for every writer and reader of the page, its items and its file, each site then read at its
  line; a prototype with section 4.4's search path and the diagram sources in pieces (`make_c2s.py`), checked for deep
  search in three engines (`search-check.js`) and measured for whole-browser memory in two (`mem.js`), which is step
  S0, done: 16 MB pieces; what outlives `document.open` (`docopen-check.js`); fzstd's tracker read and its issue #19
  recreated (`fz19.js`, not reproduced); libzstd's decoder in Wasm against fzstd (`dec-compare.js`). Found and taken
  into the design: `Specifications.html`; merge's gzip fragments; `AddWholeTestFlowSearchPieces`; the export; the
  readers of the file's text; the test hooks that `document.open` erases; the loader's global names; the empty page;
  and deep search broken in WebKit today (F2, new). Corrected: 251 test files reading the page (131 do); Appendix A's
  lines for two unused public methods, and its ranges for the search and export scripts; "five copies" of the helper,
  which is the most, by rendering mode. Every reference re-read at `2933b7a0` (5.5.2).

## Appendix A. Edit sites at `2933b7a0`

### A.1 Writers (C#)

| Site | Item |
|---|---|
| `src/Kronikol/Reports/ReportGenerator.cs:532`, `:537` | `Specifications.html` and `TestRunReport.html`, both through `GenerateHtmlReportCore` (`:1396`) |
| `src/Kronikol/Reports/ReportGenerator.cs:2289` | the component diagram's source into `#puml-data` |
| `src/Kronikol/Reports/ReportGenerator.cs:2606`, `:2615` | scenario diagrams and their SVG sources into `#puml-data` (`GenerateHtmlReportCore`) |
| `src/Kronikol/Reports/ReportGenerator.cs:4073`, `:4080` | the same in `RenderDiagramsForScenario` (`:4057`) |
| `src/Kronikol/Reports/ReportGenerator.cs:2697-2720` | the page's tail: `#puml-data`, `#step-data`, the search index, `WriteFile` |
| `src/Kronikol/Reports/ReportGenerator.cs:2751` | the search index (`SearchIndexBuilder.CompressToBase64`, `SearchIndexBuilder.cs:248`) |
| `src/Kronikol/Reports/ReportGenerator.cs:2763-2784` | `AddWholeTestFlowSearchPieces`, which decodes the fragments' blobs for the search index (called at `:2651`, `:3996`, `:4008`, `:4030`, `:4042`) |
| `src/Kronikol/Reports/ReportGenerator.cs:452-465`; `src/Kronikol/Reports/Merge/MergeableReportRenderer.cs:181-191`; `src/Kronikol/InternalFlow/InternalFlowHtmlGenerator.cs:24-36` | `#iflow-segments`: `BuildFlowScript`, merge's, and the public `GenerateSegmentDataScript`; the element sits in the head (`ReportGenerator.cs:1751`) |
| `src/Kronikol/InternalFlow/InternalFlowHtmlGenerator.cs:70` | `#iflow-segments`' `z` (`WrapSegmentData`) |
| `src/Kronikol/Reports/StepDeferral.cs:34` | held sub-steps into `#step-data` (its caller writes `data-steps`, `ReportGenerator.cs:4476`) |
| `src/Kronikol/InternalFlow/InternalFlowHtmlGenerator.cs:436` | `data-flame-z` in `GetWholeTestFlowContent` (its markup at `:437`) |
| `src/Kronikol/InternalFlow/InternalFlowHtmlGenerator.cs:522`, `:530` | the whole-test activity diagram's batches |
| `src/Kronikol/InternalFlow/InternalFlowHtmlGenerator.cs:395`, `:401`, `:476`, `:494` | `data-plantuml-z` and `data-flame-z` in two public methods nothing in `src` calls: `GenerateRelationshipPopupContent` (`:540-550`) and `GenerateWholeTestFlowHtml` (`:447-508`) |
| `src/Kronikol/InternalFlow/InternalFlowHtmlGenerator.cs:573`, `:584` | `CompressToBase64`, `DecompressFromBase64` |
| `src/Kronikol/Reports/ReportGenerator.cs:5474-5488`, `:5609-5615`, `:3365-3368` | merge's stored whole-test fragments with their gzip blobs: built, written to the data file, and taken into the merged page |
| `src/Kronikol/Reports/ReportGenerator.cs:3914-3921` | `allWtfIdentical`, which compares whole-test HTML with its blobs in it |
| `src/Kronikol/Reports/ReportGenerator.cs:6918-6957` | `WriteFile`, with its fallback name at `:6937-6941`; an empty page at `:1451` |
| `src/Kronikol/Reports/ReportGenerator.cs:1486`, `:1647`, `:1651`, `:1655`, `:1656`, `:1718`; `src/Kronikol/Reports/DiagramContextMenu.cs:69`, `:97`, `:106-116`, `:118`, `:128` | where the helper's copies come from: up to five on a page, by rendering mode |
| `src/Kronikol/Compression/ReportFileWriter.cs:30`; `src/Kronikol/Compression/Zstd/CompressionStream.cs:48`, `:228` | the `.krkl` writer's zstd setup, and the vendored ZstdSharp calls the frames need |
| `src/Kronikol/ComponentDiagram/ComponentDiagramReportGenerator.cs:59`, `:199-200`, `:215-216` | the component diagram page: its own `File.WriteAllText`, one inline `data-plantuml-z`, the render and menu scripts (unchanged in R1) |

### A.2 Readers (page scripts, `src/Kronikol/Reports/`)

| Site | Reads |
|---|---|
| `report-decompress-helper.js:5-12` | the helper itself |
| `plantuml-browser-render-script.js:666`, `:709-716`, `:1686-1694`, `:1736` | `#puml-data` (the `_pumlData` cache) and `data-plantuml-z` for drawing |
| `context-menu-script.js:72-74` | a diagram's source for the menu's copy actions |
| `context-menu-script.js:261-279` | `htmlToCanvas`, which draws a flame chart's markup, `data-flame-z` included, into a picture (no change needed) |
| `internal-flow-popup-script.js:11-14`, `:37`, `:193-195` | `#iflow-segments` and popup diagrams |
| `flame-chart-render-script.js:110`, `:150-155` | `data-flame-z` |
| `report-sub-steps-on-expand.js:11`, `:27` | `#step-data` |
| `report-components-script.js:30-33`, `:212-214` | `#component-diagram-model` (plain JSON) and the component diagram's source |
| `report-search-index.js:301-314`, `:324-346`, `:381-392`, `:424`, `:465-499`, `:502-519`, `:538-577` | the search worker's decoders, its corpus, its start, the index element, what the page collects and posts (`:571-576`), and the worker's roster of function sources |
| `report-export-function.js:39-136` | `export_data_scripts`, `export_undrawn`, `export_html` |
| `collapsible-notes-script.js:3985-3986` | assumes `data-plantuml` is set once a source has been decoded |

### A.3 Readers of the page's file

| Site | Reads |
|---|---|
| `.github/workflows/doorstep.yml:55-56` | the live demo's report, grepped for `data-stable-id` |
| `examples/Example.Api/tests/Example.Api.Tests.Integration/Helpers/ReportParser.cs:27-269` | the page through AngleSharp, `#puml-data` at `:269` |
| `tools/search-bench/real-calibrate.js:14-19`; `tools/render-bench/bench-report.js:17-23`, `:72-83`; `tools/history-bench/jsd/export.js:3-32` | repo tools |
| Kronikol4J's `parity-harness/dotnet-capture/Program.cs` (for one `:284`, `:286`) | .NET's pages as parity fixtures |
| `src/Kronikol.Tool/DecodeCommand.cs:81-85`; `src/Kronikol/Query/QueryCommand.cs:442-479`; `src/Kronikol/Compression/ReportFileFormat.cs:92-153` | `kronikol decode`, which refuses an `.html` today, and how it recognises a file |
