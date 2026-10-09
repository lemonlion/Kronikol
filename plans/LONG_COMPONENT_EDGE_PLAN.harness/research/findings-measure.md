# Agent "Measure edge thresholds in browsers" result (2026-10-09). Evidence in scratchpad/measure/
Setup: Kronikol 4.14.4 from nuget.org (git diff v4.13.2 v4.14.4: no change to render script, worker host, component emitter);
engine @plantuml/core@1.2026.8 jsDelivr, SRI passed. Edge line swapped inside page's data-plantuml-z; cold = fresh browser per case;
issue's own CDP method (one Chrome, fresh tab per page) gave identical edges. Wrapped lines byte-identical to emitter. 10-char resolution,
>=3 repeats JIT on, >=2 opt-off, plus +-10 cases. DEFAULT run writes `caller -[#E74C3C]-> warehouse` (coloured), not `-->`; issue's lengths fit coloured form.
Browsers: Chrome stable 154.0.8037.99; Playwright 1.59.1 Chromium 147.0.7727.15 (headless shell; full Chromium new headless same); Firefox 148.0.2; WebKit 26.4.

| last draws / first fails (LINE length, coloured form) | Chrome 154 | Chromium 147 |
| workers JIT on cold | 1,770/1,780 (3/3) | 1,930/1,940 (3/3) |
| workers JIT on warm (50 shorter renders first) | <=1,550 always drew; 1 of 5 failed at 1,580, 1,610, 1,630; 4/5 at 1,650; 5/5 from 1,700 | 1,640/1,650 (8/8) |
| workers --no-opt --no-maglev | 570/580 | 570/580 |
| main thread JIT on | all to 2,000 | same |
| main thread opt-off | 1,100/1,110 | same |
- Firefox 148: every length to 2,000 draws, workers + main, JIT on and with blinterp/baseline/Ion/native regexp off.
- WebKit 26.4 (Windows): every length draws, also JSC_useJIT=false; NO OffscreenCanvas on this build -> 4-worker page falls back to main thread; worker path unmeasurable. (ENGINE_PIN §10.1 "WebKit workers" row probably main thread too.)
- Shapes: unbroken / wrapped (real emitter) / all-x: same edge in every Chromium config. Plain `-->` fails 10 chars earlier by line = same LABEL length (Chrome 154: 1,770/1,780 JIT on?, 560/570 opt-off).
- C4 Rel(...) not measured: only Server/Local emit it, drawn by Java engine.
- Real emitter N -> edge line chars (display lines): 10->393 (4), 15->555 (6), 20->717 (8), 30->1,040 (11), 40->1,363 (14), 44->1,493 (16), 48->1,622 (17), 56->1,881 (20), 66->1,979 (21).
  Workers JIT on: Chrome draws <=48, fails 56/66; Chromium <=56, fails 66. Workers opt-off: <=15 draws, from 20 fails. Main: all JIT on; opt-off <=30 draws, from 40 fails.
- Issue claims: worker JIT 1,600/1,610 DIFFERENT (cold 1,770/1,780; CDP method 1,600 and 1,610 drew 3/3; issue's value sits in the WARM flaky band);
  opt-off 540/550 DIFFERENT (570/580; 550 drew 2/2 via CDP); opt-off wrapped 500/600 CONFIRMED (exact 570/580); main JIT to cap CONFIRMED (1,984, 2,000 drew 3/3);
  main opt-off 1,040/1,050 DIFFERENT (1,100/1,110); main opt-off wrapped 1,000/1,600 CONFIRMED; line length not breaks CONFIRMED (content irrelevant too);
  160 short lines (832) draw CONFIRMED JIT on + main opt-off, but FAIL (2/2) in workers opt-off; table N=44 draws CONFIRMED; N=48 errors DIFFERENT (draws 3/3 both);
  N=56 CONFIRMED Chrome 154, DIFFERENT Chromium 147; N=66 CONFIRMED; main draws all CONFIRMED; lengths/display lines match exactly; every failure same picture CONFIRMED (357 failures, RangeError picture at line 48 = edge line).
- Stack depth (trivial recursion frames, main vs worker): Chrome 154 12,447 vs 6,349 opt-off; 20,710 vs 10,583 JIT tier-up; ratio 1.96. Chromium 147 12,458 vs 6,352 (1.96).
  Firefox 148 39,457 vs 21,609 (1.83); JITs off both 49,997 (frame-count cap). WebKit 26.4 70,978 vs 70,978 (1.00). Component edge main/worker opt-off ratio ~1.92.
- Surprises: WARM workers fail earlier than cold (Chrome + Chromium); Chrome 154 warm edge flaky -> cap against warm, not cold. Chrome 154 cold 160 chars below Chromium 147 (browser/V8 version, not headless). Opt-off edges identical across versions.
  Gap to issue is probably PLATFORM: issue's check.js builds 'file://' + path -> POSIX path (Linux/macOS?) - unverified.
Files: probe/probe.cs, measure.js, run-matrix.js, cdp-check.js, run-cdp.sh, run-chain2.sh, run-chain3.sh, summarize.js; pages/w4|w0/n<N>/ (edge.txt); gen/; logs/*.jsonl, logs/run-*.txt

## Derived (mine)
- Coloured prefix `caller -[#E74C3C]-> warehouse : "` = 33 chars + closing quote -> opt-off worker label edge here ~536 draws / ~546 fails; issue's platform ~506/516.
- Node 400 KB opt-off label edge 426-427 (root-cause) => Chrome worker opt-off ~1.25x node-400.
- POSSIBLE LATENT BUG: block opener (cap 1471) and coloured bar (cap 1400) fail at 658/692 in node-400 opt-off (lazy cycle) => predicted ~800-860 in the opt-off worker, far below their caps. ENGINE_PIN §10.1 row says "--jitless loop labels, coloured bars ... every length to 1,975" - may have meant a request inside a loop. MUST MEASURE in worker opt-off (follow-up sent to measure agent).

## FOLLOW-UP (worker, strict verdict, 642 cases, fresh browser per case, 4 workers) - last draws / first fails
| item (cap) | opt-off (Chromium 147 = Chrome 154) | JIT on cold Chromium 147 | JIT on cold Chrome 154 |
| loop opener (1,471) | 830/840 | 1,000/1,010 | 1,010/1,020, flaky to 1,050 (5 of 16 failed) |
| partition opener (1,471) | 830/840 | 1,000/1,010 | 1,000/1,010 (5 of 6 failed); 1,030 drew 2 of 3 |
| coloured step bar as emitted (1,400) | 870/880 | 1,030/1,040 | 1,030/1,040 |
| styled step bar (step with spaces; 16,000) | all to 13,600 | same | same |
| sequence entity name (line) | 270/275 (543/553) | 535/540 (1,063/1,073) | 545/550 (1,083/1,093) |
| sequence database name | 270/275 (545/555) | 515/540, 520-535 flaky (3 of 16) | 545/550 (5 of 6 failed) |
| component database name (line) | 330/335 (663/673) | 720/725 (1,428/1,439) | same |
| component rectangle <<system>> name (line) | 305/310 (676/685) | 680/685 (1,434/1,444) | same |
| stats edge as emitted (link text 350, line 491) | draws 2/2 | draws 3/3 | draws 3/3 |
| stats edge link-text length (line) | 430/440 (571/581) | all to 1,500 | same |
| activity action span name (line) | 810/820 (827/837) | 980/990 (999/1,009) | same |
- Openers, declarations, actions fail as "Syntax Error?" naming the declaration line; coloured bar fails as plain RangeError TEXT (no picture); stats edge as RangeError picture.
- Reachability: Kronikol's openers short (loop ×6 · 37 ms, partition #F6F6F6 Setup); long ones only via spliced PlantUML (guard caps 1,471). Coloured bar only for a step with no break (one-token JSON array); at 1,400 it is the emitter's own statement.
- 09-26 legacy verdicts NOT sound: probe ~lines 52-54 `if (el.querySelector('svg')) done('drawn')` accepts any SVG incl. error pictures. Loop labels 1,000-2,000 with --jitless/opt-off are all "Syntax Error?" pictures. Coloured bars drew only because filler breaks every ~110 chars; without breaks fails from 880 (850/860 without <<stepDelimiter>>). Messages really draw to 2,000. JIT-on 09-25 warm run right (warm loop labels draw to 2,000).
- Files: follow/gen/gen.cs, follow.js, run-follow*.js, summarize-follow.js, grid.js, src/, logs/K-* L-* M-*.
