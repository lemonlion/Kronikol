# Harness for `LONG_COMPONENT_EDGE_PLAN.md`

Measured on 2026-10-09 at Kronikol 4.14.4 (`main` at `9f2cd394`) on Windows 11, with the pinned engine
`@plantuml/core@1.2026.8`. The scripts ran from a session scratchpad: their paths (the scratchpad, the engine cache under
`%LOCALAPPDATA%\Kronikol\plantuml-js\1.2026.8\`, a Playwright install) have to be pointed at your own before a re-run.
Large outputs were left out: the upstream clones the engine work read, the generated pages, and all but the first 40
and last 80 lines of each captured stack.

## `engine/`: the cause (plan §2.3, §2.4)

| Path | What it is |
|---|---|
| `harness.js` | Loads an engine build into node the way Kronikol's node renderer does and renders one source to SVG |
| `gen.js` | Writes the statement shapes: the component edge in every form, links, participants, block openers, coloured bars, activity actions, titles |
| `probe.js`, `bisect.js`, `run-matrix.sh` | Bisect the longest statement that draws for one shape, one engine and one `--stack-size`, in a fresh process per probe |
| `stack.js` | Captures the RangeError's own stack (`Error.stackTraceLimit = Infinity`, a hook in a copy of the engine) |
| `instr.js` | Counts the regex engine's nesting per match (3 x length + 5 for the component label) |
| `patches.js` | The two TeaVM prototypes (an iterative greedy quantifier over a character class, an iterative lazy one over the dot) and the PlantUML `Labels` guard, applied in memory to the unobfuscated build |
| `corpus-hash.js`, `svgdump.js`, `corpus*.json` | The byte-identity check of the prototypes over the render corpus |
| `chromium-check.js`, `chromium-*.txt` | The same shape in Chromium 147's main thread, to tie node's figures to the browser's |
| `results-*.jsonl` | Bisection results: `npm-s<KB>-{noopt,jit}` per stack size on the npm build, `oct-*` on the unobfuscated master build, `fu-*` the follow-up on the other statement kinds |
| `instr-oct-*.txt` | Nesting counts per shape |
| `stacks/` | Captured overflows. `npm-*` are the npm build (obfuscated names: the cycle is `Epg → BKE.oR → DJJ → Z2.oR → EXv → AGU.oR`); `oct-*` are an unobfuscated build of upstream master, whose last lines show the entry point (`CommandLinkElement_executeArg → Labels__init_0 → GroupQuantifierSet_matches`) |
| `jvm/` | `RegexDepth*.java` runs the same patterns on OpenJDK 25 with `-Xss256k` (no overflow at 1,000,000 characters; the control overflows at 500), and `jar-xss256k.txt` the 1.2026.8beta1 jar drawing 20,000-character edges at that stack |

## `measure/`: the browsers (plan §1, §2.2, §2.4)

Chrome stable 154.0.8037.99, Playwright 1.59.1's Chromium 147.0.7727.15, Firefox 148.0.2 and WebKit 26.4, run one at a
time. The generated pages and most generated sources are left out; `follow/gen/gen.cs` writes them again.

| Path | What it is |
|---|---|
| `probe/probe.cs` | The issue's probe on 4.14.4 from nuget.org: writes `ComponentDiagram.html` for N statements |
| `measure.js`, `run-matrix.js`, `summarize.js`, `wait-for.js`, `dump-svg.js` | Swap the edge line inside a page's `data-plantuml-z`, render it in a fresh browser per case (cold), after 50 shorter renders (warm), or with `--js-flags=--no-opt --no-maglev`, and classify the result |
| `cdp-check.js`, `run-cdp.sh` | The issue's own method (one Chrome, a fresh tab per page over CDP), which gave the same edges |
| `run-chain*.sh` | The order the runs were made in |
| `logs/` | One JSONL per configuration: `A-*` the edge per browser and mode, `B-*` the shapes, `C-*` the real emitter at N = 10 to 66, `D*-*warm*` the warm workers; `run-*.txt` the console of each run |
| `follow/gen/gen.cs` | A file-based app that writes each statement kind through Kronikol 4.14.4's own emitters (internals by reflection): openers, step bars, participants in both diagrams, the stats edge, activity actions |
| `follow/follow.js`, `run-follow*.js`, `grid.js`, `summarize-follow.js` | §2.4's runs: a fresh browser per case, the strict verdict (RangeError picture or text, `Syntax Error?`, `An error has occurred`, a first drawn line starting `PlantUML `, a sequence participant drawn once) |
| `follow/src/` | The representative sources: the step bars (one token, rich, short), `loop`, `partition`, the stats edge |
| `follow/logs/` | `K-*` the edges per statement kind, `L-*` 3.30.4's legacy cases re-run with the strict verdict, `M-*` the coloured bar with and without the filler's line breaks |

## `r1r3/`: the execution of R1 and R3 (plan §11, 2026-10-10)

Measured on 2026-10-10 at 4.14.4's emitters with the tools in `tools/render-bench/` (their README has the commands); the
table is `tools/render-bench/results/statement-limits-worker-2026-10-10.txt`.

| Path | What it is |
|---|---|
| `run-win-1.sh`, `run-win-2.sh` | The Windows runs: Chromium 147 and Chrome 154 cold, warm and with the optimizing compilers off, the main thread, Firefox 148 and WebKit 26.4, edges and kinds |
| `linux-run.sh`, `linux-pull.sh` | The Linux runs: Playwright's `v1.59.1-noble` image under podman in WSL, Chromium 147 with the JIT on and off, and how the logs were copied out |
| `logs/` | One log per platform, browser, mode and shape set (`*-edges.txt` the component edge in each form, `*-kinds.txt` every other kind), each case with its verdict |
| `red/` | The new facts copied onto 4.14.4 with the three new constants stubbed: `core-on-4.14.4.txt` (22 fail) and `e2e-on-4.14.4.txt` (12 fail), each on its own assertion |
| `mutate_r1r3.py`, `mutations.jsonl` | Each mutation puts one behaviour back as it was, in a snapshot worktree, and runs the facts that should catch it; the results, one line each |

## `research/`: the inventories the plan was written from

| File | What it is |
|---|---|
| `findings-rootcause.md` | The engine work: the cycle, the TeaVM and PlantUML source lines, the per-shape table, the JVM runs, the prototypes, and the follow-up on the other statement kinds |
| `findings-measure.md` | The browser matrix: Chrome 154, Chromium 147, Firefox 148, WebKit 26.4, per configuration, with a verdict on each of the issue's claims |
| `findings-emitters.md` | The product code: every caller of the component emitter and the edge form a default run writes, which adapters' method names grow, other statements with no measured cap, and how the page and node treat an error picture |
| `findings-tests-port-wiki.md` | The tests, the probes and how to re-run them, Kronikol4J's generator and ledger, the wiki sentences that change, the changelog, the roadmap rows, and the house structure of a plan |
