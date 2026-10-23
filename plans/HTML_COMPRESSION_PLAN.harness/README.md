# HTML_COMPRESSION_PLAN harness

The scripts behind [`HTML_COMPRESSION_PLAN.md`](../HTML_COMPRESSION_PLAN.md) sections 2 and 3. Each one prints sizes,
counts and timings only, never report content, so its output is safe to read in an agent session. Their output from
2026-10-23 is in [`results/`](results/).

The input was the report the owner shared on 2026-10-23, `TestRunReportWithSlowToggle.zip`, holding one
`TestRunReport.html` of 28,954,978 bytes written by Kronikol 5.4.4 (1,553 diagrams, 359 held sub-step groups, 771 flame
charts). It is not in the repository. Treat a report like it as untrusted input: extract it into a folder of its own,
keep the scripts elsewhere, run Python with `-I` and pass the file as an argument.

Run on Windows 11 on 2026-10-23 with:

- Python 3.14.4, whose `compression.zstd` wraps libzstd 1.5.7; gzip 1.14, brotli 1.2.0 and xz 5.8.2 on the path
- Node 25.9.0 with the packages in [`package.json`](package.json) (`npm install` here): `playwright-core` 1.59.0, the
  version the repository's .NET suites use, so it drives the browser builds already installed under
  `%LOCALAPPDATA%\ms-playwright`: Chromium 147.0.7727.15 (`chromium_headless_shell-1228`), Firefox 148.0.2
  (`firefox-1511`) and WebKit 26.4 (`webkit-2272`, the Windows build)
- .NET SDK 10.0.300 with ZstdSharp.Port 0.8.8 from NuGet, the release `src/Kronikol/Compression/Zstd` copies

The browser scripts launch Playwright's own builds headless, never the installed Chrome or Edge (see `CLAUDE.md`: a
fresh profile of either costs the Windows account a failed sign-in).

| File | What it prints |
|---|---|
| `anatomy.py` | The page's bytes by raw-text element (scripts by id and type, the stylesheet), then the markup outside them by tag and by attribute, base64-looking runs and `data:` URIs |
| `json_blocks.py` | The shape of each `type="application/json"` block: counts of objects, arrays and strings, how many strings look like base64, the most common keys |
| `split.py` | The page split into its content classes with each gzip+base64 layer removed: per class, the bytes as stored, the gzip inside and the raw content. Writes the classes to files, and `normalized.html`, the page with every blob written back raw |
| `cbench.py` | zstd at levels 3 to 22 (default window, and a 2^27 window with long-distance matching), gzip -9, brotli -q 11 -w 24 and xz -9e over the page and each class: size, write time, read time |
| `level9.py` | Level 9 only: windows 2^22 (the default), 2^23 and 2^27, everything in one frame, the diagram sources cut into independent frames of 1 to 16 MB, and one frame per diagram with a trained dictionary |
| `make_variants.py` | Builds the prototype pages A, B, C and C2 (its docstring says what each is), each with a timing probe |
| `bench.js` | Loads each page in a fresh browser context and prints the loader's marks, the rebuilt page's `DOMContentLoaded`, `load` and first paint after each, first contentful paint, and in Chromium the JS heap plus `ArrayBuffer` storage after a forced collection |
| `functional.js` | A deep link, Back after an in-page link, one diagram drawn through the page's own pipeline, and the search index's state, for each page |
| `zsharp.cs` | ZstdSharp's level 9 write time with a 2^23 window over the split's files |
| `search_index.py` | The search index inside a level 9 stream, as the page stores it and as raw binary |
| `fzstd_inputs.py` | Frames for the two scripts below: one input at window 2^23, at 2^27 with long-distance matching, and at level 22; the input is a given file, or a synthetic one whose tail is only a match 40 MiB back |
| `fzcheck.js`, `fzstream.js` | fzstd's one-shot `decompress` and its streaming `Decompress` over those frames, each compared with the input's SHA-256: the test of its documented 2^25 back-reference limit |
| `make_c2s.py` | The audit's prototypes (section 3.12): C2 with section 4.4's search path, the diagram sources in one frame, in pieces of 16 MB or of 8 MB, decoded on demand with at most two kept |
| `search-check.js` | Deep search in today's page against each prototype: words found only in one diagram source (from diagrams at 10%, 50% and 90%) or only in flame spans, the scenarios each finds, Back, page errors |
| `mem.js` | Step S0: private bytes of every process of the browser, less `about:blank`, after the report is up, after diagrams are drawn and after a deep search |
| `docopen-check.js`, `docopen-check2.js`, `docopen/` | What outlives `document.open`: global `let` and `var` names, window properties, listeners added before and after `open()` |
| `wk-search-probe.js`, `wk-search-probe2.js` | Why deep search fails in WebKit: each way of reading bytes inside a `blob:` worker |
| `fz19_inputs.py`, `fz19.js`, `fz19-nocopy.js` | fzstd's open issue #19 recreated: its recipe through every fzstd path and zstddec |
| `extract_frames.py`, `dec-compare.js` | A prototype's frames to files, then fzstd against zstddec (libzstd in Wasm) on them: output, time, a bad checksum |
| `dec-browser.js` | The same two decoders on the same frames inside each engine, on the main thread |
| `f2/` | Step F2: `probe.html`, a `blob:` worker that reads a `Blob` and gunzips a stream built from bytes; `run-playwright.js` opens it over `file://` and `http://` in Playwright's builds, `linux-webkit.sh` in the Linux ones on the podman runner, `run-safari.py` in Safari through safaridriver, and `probe-f2-safari.yml` is the workflow that ran the last two on a GitHub macOS runner |

```bash
py -I anatomy.py TestRunReport.html                      > results/anatomy.txt
py -I json_blocks.py TestRunReport.html                  > results/json-blocks.txt
py -I split.py TestRunReport.html out/split              > results/split.txt
py -I cbench.py TestRunReport.html out/split/markup.html out/split/steps.txt out/split/iflow.txt \
    out/split/flame.txt out/split/search.bin out/split/puml.txt out/split/normalized.html > results/cbench.txt
py -I level9.py out/split                                > results/level9.txt
py -I make_variants.py TestRunReport.html node_modules/fzstd/umd/index.js out/variants
ONLY_C2=1 py -I make_variants.py TestRunReport.html node_modules/fzstd/umd/index.js out/variants
node bench.js out/variants v0-probed,vC2-worker chromium,firefox,firefox+zstd,webkit 3 > results/bench-c2.tsv
node functional.js out/variants v0-probed,vC2-worker chromium,firefox  > results/functional.txt
dotnet run --file zsharp.cs -c Release -- out/split/markup.html out/split/puml.txt out/split/normalized.html
py -I search_index.py TestRunReport.html out/split/search.bin > results/search-index.txt
py -I fzstd_inputs.py out/fzlong                         # the forced 40 MiB match
py -I fzstd_inputs.py out/fzcheck out/split/puml.txt     # the diagram sources
node fzcheck.js out/fzlong; node fzstream.js out/fzlong; node fzcheck.js out/fzcheck; node fzstream.js out/fzcheck
# The audit (plan section 8), run while another session's test suites loaded the machine
py -I make_c2s.py TestRunReport.html node_modules/fzstd/umd/index.js out/c2s
node search-check.js out/variants/v0-probed.html,out/c2s/vC2s-one.html,out/c2s/vC2s-p16.html,out/c2s/vC2s-p8.html chromium
node search-check.js out/variants/v0-probed.html,out/c2s/vC2s-one.html,out/c2s/vC2s-p16.html firefox,webkit  # results/search-check.txt
node mem.js chromium out/variants/v0-probed.html,out/c2s/vC2s-one.html,out/c2s/vC2s-p16.html,out/c2s/vC2s-p8.html
node mem.js firefox out/variants/v0-probed.html,out/c2s/vC2s-one.html,out/c2s/vC2s-p16.html  # results/memory.txt
node docopen-check.js docopen/page.html; node docopen-check2.js docopen/page2.html          # results/docopen.txt
node wk-search-probe.js out/variants/v0-probed.html; node wk-search-probe2.js out/variants/v0-probed.html  # results/webkit-worker-blob.txt
py -I fz19_inputs.py out/fz19; node fz19.js out/fz19; node fz19-nocopy.js out/fz19         # results/decoders-2.txt
py -I extract_frames.py out/c2s/vC2s-one.html out/frames
node dec-compare.js out/frames/krn-z1.zst out/frames/krn-d0.zst                              # results/decoders-2.txt
node dec-browser.js out/frames                                                                # results/decoders-2.txt
# Step F2 (results/f2-blob-probe.txt)
node f2/run-playwright.js webkit,chromium
wsl -d podman-machine-default -u root -- sh f2/linux-webkit.sh /mnt/c/<path to f2>
# macOS: f2/probe-f2-safari.yml as .github/workflows/ on a temporary branch probe/f2-safari, with f2/ as probes/webkit-blob/
```

`firefox+zstd` in `bench.js` is Firefox with `dom.compression_streams.zstd.enabled` set, so its loader takes the native
path (`decoder` reads `native`).

Notes on the timing files:

- `bench-c2.tsv` is the current `bench.js`. `bench-variants.tsv` came from an earlier version of it and of the probe: it
  has no `shell`, `start` or `paintDcl` columns, and a run ended at the first paint after `load` or 20 s after the
  rebuild. Its C row is the prototype that decoded diagrams on the main thread.
- The columns `inflate`, `text` and `write` are durations; every other time is milliseconds from navigation start.
  `paintDcl` is two animation frames after the rebuilt page's `DOMContentLoaded`, the "report painted" figure of the
  plan; `fcp` is the browser's first contentful paint, which for a compressed page is its loading screen.
- `heapMB` is `used JS heap + ArrayBuffer backing stores`, Chromium only. It is not the whole tab.
- Run 1 of each browser and page is cold (a new browser); the plan quotes runs 2 and 3 and names run 1 apart.
- The machine was shared with other sessions; one held its suites for the timing runs and one only waited on CI.
