# SMETANA_PARITY_PLAN: Smetana to Graphviz parity for everything PlantUML asks of dot (linetype ortho first)

Date: 2026-09-14 (audit run 2026-09-10 against plantuml master `dea3374`; re-checked
against master `fb5bda4` on 2026-09-14: 323 files moved upstream since, but the only
changes under `sdot/` are style-API renames and `gen/`, `smetana/`, `h/` are untouched,
so every line number below still holds).
Status: **plan only, NOT green-lit. Nothing has been posted, pushed or committed
anywhere.** The user asked for the plan; the issue and PR drafts are appendices to be
posted only after review.

Goal set by the user: one issue in plantuml/plantuml plus one or more PRs that bring
Smetana (the Java port of Graphviz that ships inside PlantUML) to full parity with
Graphviz *for the features PlantUML actually uses*; `skinparam linetype` (ortho and
polyline) is the mandatory minimum; investigate everything else Smetana ignores; issue
and PR prose informal, positive, respectful, with images (before/after, performance
graphs) and no tells of machine authorship (no em dashes, no buzzwords, no emoji
scatter); code with real test coverage.

**Headline result of the audit.** Smetana is not "missing ortho"; it is missing the
whole `splines` family plus a dozen smaller things, and the reasons split cleanly into
two piles:

1. **Maker-side omissions** (an afternoon each). `CucaDiagramFileMakerSmetana` sets
   only `margin`, `rankdir`, `shape=box`, node sizes, `arrowtail/arrowhead=none`,
   `minlen` and label dimensions. It never sends `nodesep`, `ranksep`, `searchsize`,
   `constraint=false`, `together` clusters, `sametail`, `shape=ellipse`, or the
   `splines` value, although the port already parses most of them. The "denser
   layout" of #1703 is exactly this: Smetana runs on Graphviz defaults of 18px/36px
   node/rank separation while the dot path floors them at 35px/60px.
2. **Port gaps** (real translation work). `lib/ortho` was never translated (the
   C-to-Java translator ran without `#define ORTHO`), the `ET_PLINE` branches of
   `dotsplines.c` are half done, `edgeType()` (the parser for the `splines`
   attribute) is a whole stub that would throw the moment anyone sets the attribute,
   `constraint=false` would also throw (two stubbed lines in `dot_init_edge`),
   `dot_sameports` is a stub, `collapse_rankset` is a stub, the xlabel placement
   branches are stubbed, and HTML labels (which the dot path uses for ports and
   label shields) have no parser at all.

Plus a third, PlantUML-drawing-side pile in `SmetanaEdge` versus `SvekEdge`:
middle decorations, label direction arrows, qualified associations, multi-colour
arrows, ArrowHeadColor, package self-links, label collision avoidance, SVG metadata.

Everything above is verified twice: by reading the code at file:line on both sides
(section 3 cites them) and by rendering a 52-probe corpus through both engines on the
built jar (section 2.2), with 2x2 montages already generated for 59 probes.

---

## 0. Read this first: the decisions

| # | Decision | Why (short) |
|---|---|---|
| D1 | **Nine PRs, one umbrella issue.** Small maker-side PRs first (A spacing, B constructs, C drawing), then the routing chain (E polyline, F1 ortho port unwired, F2 ortho wired, G xlabel and label alignment), then D groupInheritance and H ports/shields. | Each PR is independently mergeable and reviewable; the first three fix #1703 and build trust before the 5k-line port lands. Reference-render churn (Vega) is isolated in PR A. |
| D2 | **Ortho = hand port of Graphviz 2.38 `lib/ortho` in the existing `gen/` idiom, with the post-2.38 fixes folded in** (strategy A in section 6.6). Not a custom router, not a polyline post-process, not a port of Graphviz main. | Only A reproduces dot's routes (maze + Dijkstra + channel tracks). Main's C depends on `util/list.h`, `gv_alloc`, `OPTIONAL()` and error returns that do not exist in the port; the 2.38 structure keeps `@Original` keys and file layout consistent with the other 587 ported functions. |
| D3 | **Ortho lands with `label=` first (2.38 semantics via `setEdgeLabelPos`), then PR G switches Smetana ortho to `xlabel` + `forcelabels` + the `alignEdgesAtLabelNodes` post-pass** the dot path uses. | xlabel placement is stubbed in the port today; un-stubbing it is ~60 lines but a separate concern. Landing ortho without it keeps F2 reviewable; G then closes the label-placement gap. |
| D4 | **Align Smetana's nodesep/ranksep to the dot path's computed values** (floors 35/60px, activity 20/40, per-edge label widening, skinparam override). Accept that ~80 Vega reference SVGs change in that one PR. | That is what parity means, and it is the fix for the density half of #1703. The churn is mechanical (VEGA_FORCE_WRITE) and isolated. |
| D5 | **Ports, member ports and label shields via post-layout endpoint relocation in the maker, not by porting htmltable.c.** | The HTML label parser is thousands of lines with a lexer; the maker already knows every port rectangle. Relocation matches what users see (edge leaves the member row / the port side). Documented as an approximation in the PR. |
| D6 | **No exception objects anywhere on the ortho routing path.** All `assert`, `longjmp`, `goto orthofinish` become status codes; on failure fall back to the spline router for that graph and log once. | SMETANA_PERF_PLAN measured 76.6% of browser Smetana time in TeaVM exception construction; a router that throws per edge would undo that work. Graphviz main made the same change (MR !1917). |
| D7 | **Reference for tests = geometry invariants plus a small hand-checked corpus, never byte hashes against a `dot` binary.** Adopt Graphviz main semantics (eqEndSeg fix, `round()` in htrack, double edgeLen, insertion-ordered rawgraph) and reproduce glibc `drand48` so Linux `dot` is the tie-break reference. | Ortho tie-breaks depend on `drand48` permutation order, sort stability and int/double drift; Windows and Linux `dot` already disagree with each other. |
| D8 | **Evidence images: 2x2 montages (Smetana on / dot on / Smetana off / dot off) regenerated at 2x scale from the probe corpus, hosted on the existing `pr-assets` orphan branch of lemonlion/plantuml; perf graphs rendered from JSON via the Playwright screenshot recipe.** | Same pipeline as the merged perf PRs (#2858-#2861); raw.githubusercontent URLs survive PR edits. |
| D9 | **Prose house style** (section 9.4): plain engineer's write-up, first person, specific numbers, no em dashes, no bold-everywhere headings, no emoji, no "comprehensive/robust/seamless/leverage", no closing summaries. | User requirement; also matches how the maintainer writes. |

Open questions that need the user (section 12): Q1 whether to fold PR B and C
together, Q2 whether the ortho port goes in as two PRs (unwired then wired) or one,
Q3 whether to touch edge emission order (a layout-changing "parity" that may read as
regression), Q4 whether the extra Smetana cluster margin (16 vs dot's 8) is parity work
or a deliberate choice to keep, Q5 perf-bench corpus rows (touching the corpus resets
band history per its README).

---

## 1. Goal and constraints

### 1.1 User requirements (verbatim intent)
- Issue in plantuml/plantuml with one or more attached PRs.
- Smetana at full parity with Graphviz "in the context of PlantUML functionality":
  everything the dot path (`svek`) can express that Smetana cannot.
- Minimum: `skinparam linetype` fully supported (ortho is the known gap).
- Investigate anything else Smetana does not support.
- Issue and PR prose: informal, positive, respectful; no telltale signs of LLM
  authorship, explicitly no em dashes; images (before/after, performance graphs).
- Code with relevant test coverage.

### 1.2 Standing constraints (from earlier sessions and memory)
- Never post, comment, label or push anything upstream while planning. All drafts
  stay in this file until the user says go.
- `C:/Code/plantuml-aggr` is a read-only checkout (audit base `dea3374`);
  `C:/Code/plantuml` is the working clone (remotes: `origin` = plantuml/plantuml,
  `fork` = lemonlion/plantuml; `pr-assets` orphan branch exists on the fork at
  `da232c7`).
- lemonlion has triage on plantuml/plantuml (labels, close, assign) but no push,
  merge or CI re-run; CI reruns still need the empty-commit trick.
- No attribution lines in commits or PR bodies.
- The port must stay TeaVM-clean and compile on the Java 8 Ant job (`ant -noinput`
  on JDK 8 compiles the whole tree; Gradle compiles at `--release 11`). New code in
  `gen/` therefore uses the same Java subset the existing `gen/` code uses: no `var`,
  no streams, no `List.of`, no `String.repeat`, no lambdas in hot paths, no
  reflection, no threads, no `String.format`, no stdout.
- Every ported function carries `@Original(version="2.38.0", path, name, key)` and
  unported lines stay as `UNSUPPORTED("hash")`, because the plantuml/smetana `.ctoj`
  override scheme keys on them.

### 1.3 What "parity" means here (scope boundary)
Parity = for every DOT attribute or construct the dot path emits, and for every
drawing step `SvekEdge`/`SvekNode` perform, Smetana either does the same thing or
the difference is documented as deliberate. It does **not** mean byte-identical SVG
against a `dot` binary (D7), and it does not mean porting Graphviz features PlantUML
never emits (`concentrate`, `newrank`, `ordering`, `compound/lhead/ltail`,
`splines=line/curved`, `mclimit`, `nslimit`, compass corner ports: all confirmed
never emitted by svek, section 3.7).

---

## 2. Method (how every claim below was established)

### 2.1 Code audit, both sides
- Dot path read in full: `svek/DotStringFactory.java` (attribute emission 112-205,
  `solve()` 451+, `alignEdgesAtLabelNodes` 464-626), `svek/SvekEdge.java`
  (`appendLine` 391-480, `drawU` 840-1020, `drawRainbow` 1113-1141, `manageCollision`
  1205), `svek/SvekNode.java` (shapes 332-343, HTML port tables 138-269),
  `svek/Cluster.java` and `svek/ClusterDotString.java` (together 528-556, rank
  source/sink 135-183), `dot/DotData.java` (`removeIrrelevantSametail` 122),
  `svek/GraphvizImageBuilder.java` (sameClassWidth 372-397).
- Smetana maker read in full: `sdot/CucaDiagramFileMakerSmetana.java` (925 lines),
  `sdot/SmetanaEdge.java` (473 lines), `GroupMakerStateSmetana`,
  `CucaDiagramSimplifierStateSmetana`.
- Port: awk map of all 587 `@Original` functions under `src/main/java/gen` classified
  by `UNSUPPORTED` density (3665 `UNSUPPORTED` lines total, 33 whole-function stubs),
  then every layout-relevant function read: `dotsplines__c`, `utils__c`, `routespl__c`,
  `splines__c`, `postproc__c`, `xlabels__c`, `rank__c`, `sameport__c`, `dotinit__c`,
  `class1__c`, `shapes__c`, `htmltable__c`, `input__c`, `ns__c`, `mincross__c`,
  `smetana/core/{Globals,Macro,CArray,CArrayOfStar,CFunction,JUtils,jmp_buf,ZType,FieldOffset}`.
- Three failure idioms in the port, only one of which throws: (1)
  `UNSUPPORTED("hash"); // C line` throws `UnsupportedOperationException`
  (`Macro.java:85`); (2) empty `{ }` branches where the C body was commented out
  silently skip (edge xlabel, `dir`, labeljust, node margin/regular, rankdir BT/RL,
  xladjust sliding, beginpath TOP side); (3) whole-function stubs `f(Object... arg_)`.
  Any parity PR must grep for (2) in the functions it touches; they never surface as
  exceptions.

### 2.2 Empirical probes (built jar `plantuml-1.2026.8beta1.jar` = master, Graphviz 16.1.0 portable dot)
- 52 probe rows, 59 rendered pairs once the nodesep and ranksep ladders are counted separately (`X.puml` with the directive, `X-off.puml` without; otherwise
  identical) across class, component, deployment, usecase, state, object, legacy
  activity; rendered with `-Playout=smetana` and with `-graphvizdot <dot.exe>`
  (without the flag PlantUML silently falls back to Smetana, which made an early run
  look like everything was honoured).
- Geometry-normalised comparison: strip `<!-- -->` comments, `<?plantuml-src?>`
  and `data-source-line`, then hash; a second order-insensitive variant (sorted
  multiset of coordinate attributes) because `together{}` and `[[url]]` only change
  element order. Byte hashes and counts of `C` path commands are NOT diagnostic (the
  SVG writer emits cubics for straight segments too).
- Numeric fidelity script (`analyse.py`) for ellipse clipping and density.
- 2x2 montages `montage/X.png` = [Smetana on | dot on ; Smetana off | dot off] for
  59 probes (`Montage.java`), all viewed.
- Browser: the same corpus through `core-1.2026.8beta1-0e4f452.js` in headless
  Chromium, with and without `viz-global.js` (Viz.js 3.24.0 / Graphviz 14.1.1),
  with and without `!pragma layout smetana`.
- Only one probe produced stderr (`sameClassWidth` prints NOT YET IMPLEMENTED); no
  probe crashed.

### 2.3 History
GitHub issues, forum threads, plantuml.com pages and the Graphviz `lib/ortho` history
(512 commits since 2.38, read against the 2.38 and main sources in the scratchpad).
Section 10 and Appendix A cite them.
Threads the drafts cite, with what they establish: forum 15893 (2022-05-04, maintainer
answer "orthogonal line has not been implemented in smetana"); forum 1608 (origin of
`linetype ortho/polyline` in 2016 and the 2021-07-31 beta that switched ortho to
`xlabel` + `forcelabels`); forum 15405 (mirecg: ELK does not respect positional hints);
#1703 (density and reversed ordering, the only open umbrella issue); #939 (shared
target styles on ortho) and #345 (connector routing with ortho + groupInheritance 2);
#1058 (labels dropped on `{rank=sink}` port edges, graphviz#2346); #1443 and #1441
(nested ports, The-Lum's port issue list with #1068, #1236, #1602, #1766); #2471
(per-link linetype); #2866 (browser Smetana perf: exception construction was most of
the render time); #2389, #2395, #2665 (dot-path ortho label work); #1346 (maintainer
invites collaboration on the port and asks for tests). ELK is not part of the TeaVM
build (the browser build compiles no ELK classes).

### 2.4 Where the material is (scratchpad `C:/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/fe0caa2b-f032-4ecf-a1fb-096bdb608693/scratchpad/`)
- `wf1/attr-audit/` (49 findings + `probe/out/*-sm|-dot.{svg,png}` for the drawing-side probes),
  `wf1/port-audit/` (31 findings, `funcmap.tsv`), `wf1/jvm-probe/` (`probes.md`,
  `puml/`, `out/`, `montage/`, `analyse.py`, `Montage.java`, `compare.sh`,
  `compare2.sh`), `wf1/browser-probe/` (`browser-probe.js`, renders),
  `wf1/history/notes.md`, `wf1/ortho/design.md` (49 KB port feasibility study,
  condensed in section 6), `probe/` (first corpus + `compare.sh` normaliser),
  `gv238/` and `gvmaster/` (Graphviz sources), `gv16/` (portable dot).
- Parsed audit output: `../tasks/wf1b-result.json`, `wf1b-majors.md`,
  `wf1b-verification.json`, `wf1-findings.md`.

## 3. Gap inventory (verified by code on both sides and by probe)

Paths are relative to `src/main/java/` in plantuml master. "Probe" names refer to
`wf1/jvm-probe/puml/<name>.puml` and `montage/<name>.png`, or to
`wf1/attr-audit/probe/out/<name>-{sm,dot}.png` for the drawing-side ones. Severity:
blocker = the headline feature; major = a documented option or common syntax is
ignored or wrong; minor = visible but rare; cosmetic = measurable, rarely noticed.

### 3.1 Routing (`skinparam linetype`)

| Key | User syntax | Dot path | Smetana today | Evidence | Sev | PR |
|---|---|---|---|---|---|---|
| linetype-ortho | `skinparam linetype ortho` (class, object, component, deployment, usecase, state, legacy activity) | `splines=ortho; forcelabels=true` (`DotStringFactory:157-164`), edge labels as `xlabel=<TABLE>` (`SvekEdge:434`), post-layout `alignEdgesAtLabelNodes` (`DotStringFactory:451, 464-626`), `edge_corner_radius` rounding (`SvekEdge:911`) | Maker never reads `getDotSplines()` (root graph gets only `margin` 587 and `rankdir` 607). Port: `_dot_splines` has no `ET_ORTHO` branch (`dotsplines__c:428-698`, only ET_NONE/ET_CURVED/ET_LINE at 444-457, 563), no `gen/lib/ortho` at all, `setEdgeLabelPos` absent, `resetRW` stub (385-387), `arrow_clip` ortho guard throws unconditionally (`splines__c:147-150`) | 21 ortho probes all IGNORED (identical geometry on/off): class-ortho, component-ortho, deployment-ortho, usecase-ortho, state-ortho, object-ortho, lr-class/component/usecase/state-ortho, ortho-edgelabels, ortho-quantifiers, ortho-lr-quantifiers, ortho-selfloop, ortho-packagelinks, ortho-noteonlink, ortho-nestedpackages, ortho-entrypoints, activity-legacy-ortho, stereo-shapes, composition | blocker | F1, F2, G |
| linetype-polyline | `skinparam linetype polyline` | `splines=polyline` (`DotStringFactory:158`) | Maker never sets it. Port: `routepolylines` is ported (`routespl__c:505`) and used by `make_flat_edge`, `make_flat_labeled_edge` and the last segment of `make_regular_edge` (1885), but `makeSimpleFlat`'s ET_PLINE branch (1108-1118), the multi-rank `smode` loop (1834-1839, entered when `straight_len(hn) >= 3`, or 5 with edge labels) and `make_flat_bottom_edges` (1445) are UNSUPPORTED; `polylineMidpoint` stub (`splines__c:1491`) | class/deployment/state/lr-class-polyline IGNORED; component/usecase/object-polyline identical on dot too (already straight) | major | E |
| splines-attr-parse | any `splines=` value | n/a (dot parses) | `setEdgeType` (`utils__c:1099`) calls `edgeType(s, dflt)` for any non-null `splines`; `edgeType` is a whole stub (`utils__c:1089`, 74 UNSUPPORTED lines), so setting the attribute throws before layout. `dotinit__c:330` defaults to ET_SPLINE, which is why nothing throws today. `ET_*` constants exist (`Macro:1357-1361`) | latent (no probe can reach it) | blocker gate | E |
| linetype line/curved | not exposed (`DotSplines` enum = POLYLINE, ORTHO, SPLINES) | never emitted | `makeStraightEdge`, `makeLineEdge` stubs | n/a | none | non-goal |

### 3.2 Graph and edge knobs

| Key | User syntax | Dot path | Smetana today | Evidence | Sev | PR |
|---|---|---|---|---|---|---|
| nodesep | `skinparam nodesep N`, and the implicit spacing of every diagram | `nodesep = max(getHorizontalDzeta, getMinNodeSep)` where dzeta = max over length-1 edges of label + decoration widths / 10 (`SvekEdge:1159`), floor 35px (activity 20) (`DotStringFactory:115-120, 248-254`), skinparam override, emitted in inches (139-140) | Never set. Port parses it (`input__c:273`, `DEFAULT_NODESEP = 0.25in = 18px`, `Macro:1553`) | nodesep-50/120/200: dot canvas 251/359/511px, Smetana 188px in all three; base density ratio F1 | major | A |
| ranksep | `skinparam ranksep N`, vertical spacing everywhere | `getVerticalDzeta` (label heights) floored at 60px (activity 40) (`DotStringFactory:124-129, 238-246`), override, inches (137-138) | Never set. Port parses it (`input__c:278`, default 0.5in = 36px) | ranksep-30/100/200: dot 341/551/851px tall, Smetana 369 always | major | A |
| searchsize | none (implicit) | `searchsize=500` always (`DotStringFactory:150`) | Not set; `ns__c:978` reads it, default `SEARCHSIZE = 30` | rank assignment on larger graphs may differ; plausible cause of the ordering half of #1703, unverified | major | A |
| remincross | none | `remincross=true` (148) | port treats absent as true (`mincross__c:193`) | none | none | no change |
| constraint=false | `-[norank]->`; links between two entry/exit points of one container | `,constraint=false` (`SvekEdge:475-476`; `Link:443`; `WithLinkType:157`) | Maker never sets it (createEdge 861-899). Port: `nonconstraint_edge` ported (`class1__c:88`, used at 165) but the `dot_init_edge` branch body is two UNSUPPORTED lines (`dotinit__c:202-204`, `xpenalty=0; weight=0`), so setting it would throw | norank probe IGNORED (dot puts C under B, A->C curves around) | major | B |
| together | `together { class A  class D }` | `subgraph cluster<id>t<n> { ... }` (`Cluster:528-556`) | No reference to `Together` in `sdot/`; leaves exported flat (424) | together probe: coordinates identical on/off (only element order changes); dot puts A and D side by side | major | B |
| sametail (groupInheritance) | `skinparam groupInheritance N` | `DotData.removeIrrelevantSametail` (122) sets `sametail=<uid>` per extends-like link above the threshold and installs a `Neighborhood` so the parent is wrapped in `EntityImageProtected` (20px, `GeneralImageBuilder:112`); `SvekEdge:478` emits it | Maker never calls it (`DotData` not constructed, `layoutAndGetTextBlock` 530). Port: `sameport__c.dot_sameports` body UNSUPPORTED (80-108), throws if either attribute exists | groupinh probe: three separate triangles vs one merged | major | D |
| rank=same | none | dead: `SkinParam.useRankSame()` returns false (`SkinParam:1175`) so `appendRankSame`/`rankSame` never emit | `collapse_rankset` stub, irrelevant | n/a | none | no change |
| rank=min/max + point nodes, rank=source/sink chains | legacy activity swimlanes with `skinparam swimlane`; cluster entry/exit and port ranks | `manageMinMaxCluster` (`DotStringFactory:203`), `ClusterDotString:135-183, 254` (`{rank=source; ...}` + `weight=999` chains, `arrowhead=none` chain ending in `->empty`) | Nothing in the maker; port `collapse_rankset` stub (`rank__c:238`), `minmax_edges_w_` throws when a set exists (516), `point` shape unsupported (`shapes__c:170`, `Globals:279`) | ports-state and ortho-entrypoints render correctly on both because `Cluster.manageEntryExitPoint` (410) snaps them at draw time; the port geometry problem is 3.4 | minor (niche syntax) | H (ordering emulation); full rank sets are section 13 |
| weight, minlen, hidden, dir hints, minClassWidth, rankdir LR | `-[hidden]->`, `-left->`, `-->>`, length | identical emission | honoured | hidden, dir-left/right/up, minclasswidth, longedge probes honoured | none | no change |

### 3.3 Node shapes

| Key | User syntax | Dot path | Smetana today | Evidence | Sev | PR |
|---|---|---|---|---|---|---|
| node-shape-ellipse | usecases, requirements, Chen attributes | `shape=ellipse` for `ShapeType.OVAL` (`SvekNode:343`; `EntityImageUseCase:222`) | `shape=box` for everything (maker 469); `Globals.Shapes` = box, ellipse, record (279-281); `poly_init` handles the ellipse case without hitting UNSUPPORTED (`shapes__c:225`) | oval-clip: Smetana tips stop on the ellipse bounding box (r2 1.62-1.65), dot on the ellipse (r2 1.03); usecase-ortho side arrivals | major | B |
| node-shape-circle | `[*]` start/end, lollipop interfaces, association points | `shape=circle` (`SvekNode:341`) | box | small radius so barely visible | minor | B (ellipse with square dims) |
| node-shape-diamond/octagon/hexagon/rounded | `<<choice>>`, Chen relationships, legacy synchro bars, rounded states | `shape=diamond/octagon/hexagon`, `rect,style=rounded` (`SvekNode:339`) | box; `poly_init` 244-263 UNSUPPORTED (sides/skew/distortion), no descriptors in `Globals` | diamond probe near parity for vertical arrivals | minor | section 13 (optional) |

### 3.4 Ports and label shields (HTML labels on the dot path)

| Key | User syntax | Dot path | Smetana today | Evidence | Sev | PR |
|---|---|---|---|---|---|---|
| ports-html-table | component `portin`/`portout`/`port`; state entry/exit points linked from outside | `shape=plaintext, label=<TABLE><TD PORT="P" ...>` (`SvekNode:138, 180-220`), edges addressed `uid:P` (`EntityPort:54`, `Link:227`) | `agnode(uid)` + `shape=box` (461, 469); no `headport`/`tailport` attribute in the port at all (`input__c:374`), no DOT-string parser so `a:P` is never split, `make_html_label` and `html_port` stubs (`htmltable__c:92, 67`), `poly_path` html branch UNSUPPORTED (`shapes__c:785`) | ports-component: p1 and p2 both on the BOTTOM edge of Service, Client->p1 passes through the box (F6) | major | H |
| ports-member | `A::field --> B`, object and json/map key links | `RECTANGLE_HTML_FOR_PORTS` (`EntityImageClass:255`), one `<TR PORT=...>` per member (`SvekNode:269`), `uid:pXXX` (`EntityPort:50`) | ignored: edge leaves the class centre | memberport-sm.png vs memberport-dot.png | major | H |
| label-shield | cardinalities `"1" --> "*"`, roles, qualifiers | `entity.ensureMargins(Margins.uniform(16))` when a quantifier/role/Kal exists and the Graphviz version uses shields (`SvekEdge:232`, `GraphvizVersionFinder:49`), HTML TABLE with `PORT="h"` (`SvekNode:148, 233-267`), uid `:h` (`Bibliotekon:193`) | nothing reserves label room; node width/height = image dimension (470) | quantifier probe: "0..1 very long cardinality" and "one" overprint the arrows | major | H |
| cluster-protection-wrappers | links to/from a package as a whole | `za<uid>` point node inside `a/i/p0/p1` protection subgraphs (`ClusterDotString:98-155`), `simulateCompound(lhead, ltail)` (`SvekEdge:671`) | `z<uid>` 0.1x0.1 box core node (438-450) + `simulateCompound` and cluster magnetic border (`SmetanaEdge:142-165`) | clusterlinks/F7: Smetana arrow lands on the package tab corner, dot on the border with a smooth approach | minor | H stretch |

### 3.5 Drawing side (`SmetanaEdge` vs `SvekEdge`, all PlantUML Java, no port work)

| Key | User syntax | Dot path | Smetana today | Evidence | Sev | PR |
|---|---|---|---|---|---|---|
| arrowheadcolor | `skinparam ArrowHeadColor`, `<style> arrow { HeadColor }` | `drawRainbow` paints extremities with `headColor` incl. the transparent branch (`SvekEdge:1113-1139`) | `arrowHeadColor` computed (172-184) and never read; extremities painted with the line colour (207-217) | arrowhead-sm.svg polygon fill #00F vs dot #F00 | minor | C |
| middle-decor | `-(0)-`, `-0)-`, `-(0-`, Chen subset/superset | drawn at `dotPath.getMiddle()` (`SvekEdge:982-989`), label shield 7px (353) | none (drawU 106+) | middle: 3 ellipses vs 6 | major | C |
| magic-arrows | `A --> B : label >`, `: < label`, per-line guide arrows | `addMagicArrow`/`addSeveralMagicArrows` (`SvekEdge:283, 297, 304`), `getArrowDirection` at draw time | maker has the calls commented out (746, 765); `SmetanaEdge.getArrowDirection*` throw "refactor in progress" (454-470) | magic: 2 polygons vs 4 | major | C |
| qualified-association | `Bank [accountNo] --> Account` | `Kal` objects (`SvekEdge:242`), `computeKal` (1069), overlap resolution (`SvekResult:104`, `SvekNode.fixOverlap`), drawn at 1015-1019 | no Kal in `sdot/`; the qualifier text disappears | kal: "accountNo" absent | major | C (+ shield in H) |
| supplementary-colors | `-[#red;#blue]->` | one offset copy per extra colour (`SvekEdge:1141`) | single path (217) | rainbow2: 1 path vs 2 | minor | C |
| group-self-link | `P --> P : text` on a package | loop translated to the right border (`SvekEdge:863-869`, `Link:341`) | loop drawn on the core node inside the package | pkgself | major | C |
| link-constraint | `constraint on links : {xor}` | 10x10 label spot (`SvekEdge:430`), drawn at 994-1013 | nothing | n/a (not probed) | minor | C |
| label-collision | cardinalities/roles in dense diagrams | `manageCollision(allNodes)` after layout (`DotStringFactory:454`, `SvekEdge:1205`) | labels drawn at the port's textlabel position (358) | F4, paralleledges "fourfive" | minor | C |
| svg-edge-metadata | tooling that reads `<!--link ...-->`, `data-source-line` | `commentForSvg`, `UGroup(location)`, `setCommentAndCodeLine` (`SvekEdge:844, 944`) | plain `UGroup()` (111) | any Smetana SVG | minor | C |
| sameclasswidth | `skinparam SameClassWidth true` | `getMaxWidth` over class-like leafs (`GraphvizImageBuilder:372-397`) | prints "NOT YET IMPLEMENTED" (910-911) | sameclasswidth probe + stderr | major | B |
| cardinality-style, url-on-link | style `arrow.cardinality`; `[[url]]` on links | applied / label clickable | plain arrow style; URL closed before labels | cosmetic | cosmetic | C |

### 3.6 Confirmed non-gaps (no work, but worth stating in the issue)
`rank=same` (dead on both paths), `remincross`, `forcelabels` (parsed), `hidden`
links (laid out, not drawn: maker 262/346), `minlen`, direction hints, `minClassWidth`,
`[[url]]` geometry, entry/exit points and pins (correct on both by PlantUML-side
snapping), crow's feet, lollipops, empty packages, legacy partitions, degenerate
single-entity diagrams, opale notes, head/tail label sizing.

### 3.7 Never emitted by PlantUML (out of scope by construction)
`concentrate`, `newrank`, `ordering`, `compound`/`lhead`/`ltail` (svek keeps them
commented out and simulates cluster clipping itself), `splines=line|curved`,
`mclimit`, `nslimit`, compass corner ports `ne/nw/se/sw`, `labelangle`/`labeldistance`,
`rankdir=BT|RL`, the experimental `!pragma kermor`, `DotMode` retry modes (browser
viz path only).

### 3.8 Base-render fidelity measurements (from `analyse.py`, the `-off` renders)
- F1 density: median canvas area ratio Smetana/dot = 0.79 over 59 base renders (min
  0.71, max 1.05). class-ortho-off rank gaps 56/56/37px (Smetana) vs 78/78/61px
  (dot); node gap 18.7 vs 34.7px. Fully explained by 3.2 nodesep/ranksep.
- F2 ordering: identical rank ordering in every probe; #1703's ordering complaint
  needs a bigger diagram (searchsize is the candidate; PR A will re-render the
  reporter's Spock diagram before and after).
- F3 oval endpoints: bounding box vs ellipse (3.3).
- F4 labels on or hard against the curve; parallel labels touching (3.5 collision).
- F5 self loops: Smetana stacks several self loops of one node on the same small
  loop (labels overprint); dot nests them. Engine-side (`selfRight` in
  `splines__c:404`), not yet owned; F2's corpus (ortho-selfloop) will re-measure
  and, if still wrong under splines too, it becomes a follow-up.
- F6 ports on the wrong side (3.4).
- F7 cluster-target arrival on the tab corner (3.4 wrappers).
- F8 actor label beside the arrow start instead of between figure and arrow (minor,
  later).

## 4. Decisions in detail (alternatives considered)

### D1 PR split and order
Order: **A** spacing (nodesep, ranksep, searchsize) -> **B** constructs
(constraint=false + its two port lines, together, ellipse/circle shapes,
sameClassWidth) -> **C** drawing parity in `SmetanaEdge` -> **E** polyline (brings
the `edgeType()` port that unblocks the `splines` attribute) -> **F1** `lib/ortho`
port, unit-tested, not wired -> **F2** wire `ET_ORTHO` + maker + corpus + browser
check -> **G** xlabel completion + label-node alignment for Smetana ortho -> **D**
groupInheritance (`sameport.c` port + maker) -> **H** ports, member ports, label
shields by endpoint relocation.

Why this order and not "ortho first": A, B, C are each under 200 lines, fix the only
open umbrella issue (#1703) and several closed-as-unsupported forum answers, and get
the reviewer used to the Vega re-baseline mechanics before the 5k-line port. E is the
smallest routing change and proves the `splines` gate end to end. Two PRs for ortho
(Q2) so F1 can be reviewed as pure translation with oracles and zero behaviour
change, and F2 is a 200-line diff plus fixtures. D and H are the largest "other"
items and neither blocks linetype.

Alternatives rejected: one giant PR (unreviewable; the maintainer merged the four
perf PRs quickly precisely because each was one idea); ortho first (no trust built,
the Vega churn from A would then land on top of ortho fixtures and hide regressions).

### D2 Ortho strategy (from `wf1/ortho/design.md` section 6)
- **A. Port 2.38 `lib/ortho`, then fold in the later fixes. Chosen.** Highest
  fidelity (same maze, Dijkstra, channel/track assignment as dot); self-contained C
  (no cgraph dependency beyond `agfstnode/agfstout/ND_coord/ED_spl`); excellent
  oracles (partition tiles the free space exactly, shortest path vs brute force,
  axis-aligned output that must avoid node boxes); Graphviz's own regression graphs
  apply. ~4,800 Java lines in the port idiom, ~3,200 without scaffolding.
- B. Port Graphviz main. Same algorithm, cleaner C, but built on `util/list.h`,
  `gv_alloc/gv_calloc`, `OPTIONAL()`, `bitarray_t`, `dfp_cmp`, `EDGETYPE_*`,
  `agwarningf` that the port lacks; every other ported file is 2.38-shaped. Use main
  as the review reference and the source of fixes only.
- C. Hand-written channel router on dot's rank structure. ~700 lines but visibly
  different from dot on every non-trivial graph (dot's ortho ignores ranks and routes
  Manhattan shortest paths side-to-side through free space with congestion weights);
  would route through nodes on long edges unless a node-avoidance maze is added, at
  which point it is A again. Every user comparing engines would file a diff.
- D. Route polylines then snap to axis-aligned staircases. Dog-legs at every rank
  boundary, no spacing between parallel edges, inherits the unfinished polyline
  port. Not recommended.

### D3 `label=` first, `xlabel` second
2.38's ortho never routes around labels: `orthoEdges` warns "Orthogonal edges do
not currently handle edge labels. Try using xlabels." and forces `doLbls = 0`
(`ortho.c:1300-1303`); main deleted the dead parameter in 2026-04. What 2.38 does
is `setEdgeLabelPos` (`dotsplines.c:218-240`): copy the coordinates dot reserved for
label virtual nodes into `ED_label(e)->pos` before routing. That gives every labelled
ortho edge a position the existing `SmetanaEdge` reads back
(`BoxInfo.fromTextlabel(data.label)`, 350-354). Consequence: labels sit where the
rank structure put them and a route may pass under one, exactly like
`dot -Gsplines=ortho` with `label=`. PlantUML's dot path avoided that in 2021 by
switching ortho to `xlabel` + `forcelabels` (forum 1608), then added
`alignEdgesAtLabelNodes` (PR #2395) and edge corner rounding (PR #2389). PR G brings
Smetana to that point: un-stub the `ED_xlabel` branches (`utils__c:792` empty block,
`utils__c:647`, `postproc__c:570-577, 580-590, 185`, ~60 lines incl.
`polylineMidpoint`), have the maker emit `xlabel` + `forcelabels` under ORTHO like
`SvekEdge:434-436`, and port `alignEdgesAtLabelNodes` onto `SmetanaEdge` (needs a
`replaceDotPath`, ~40 lines).

### D4 Spacing alignment and the Vega churn
Smetana runs on 18px/36px; the dot path never goes below 35px/60px (activity 20/40)
and widens per edge label. Aligning is the parity fix and the #1703 fix. About 80 of
the 301 Vega diagrams go through the maker (`TitledDiagram.FORCE_SMETANA = true`
makes every reference render use Smetana); all of their reference SVGs change in
PR A and nowhere else. The PR shows the density ratio before/after (target ~1.0
against dot on the 59-probe corpus) and the reporter's #1703 diagram. Smetana's
extra cluster margin (root and cluster `margin=16`, 20 for shaped symbols, vs
Graphviz's 8) is deliberate maker code and is left alone (Q4).

### D5 Ports without htmltable
Option A (port `htmltable.c` + `htmllex` + `htmlparse`): thousands of lines and an
HTML lexer for four PORT cells. Option B (chosen): the maker already computes every
port rectangle (`Ports`/`EntityImagePort`, `WithPorts.getPorts`) and every shield
margin; after `gvLayoutJobs` move the `SmetanaEdge` start/end to the port rectangle
(`DotPath.moveStartPoint/moveEndPoint` already exist) and, for shields, size the node
with `ensureMargins(16)` and clip the edge to the inner rectangle. For entry/exit and
port ranks, emulate `rank=source/sink` by exporting port nodes first/last so mincross
keeps them at the cluster top/bottom (interim), with the true `collapse_rankset`
port listed in section 13. A future true-port implementation (`ED_tail_port`
plumbing via `poly_port`/`compassPort`, ~200 lines) is documented as the upgrade
path in the PR.

### D6 No exceptions on the routing path
`ortho.c` has one `longjmp` (`seg_cmp`, 752) caught at 1400 and an `assert(0)` in
`decide_point` (868); `trapezoid.c` has table-overflow asserts; `maze.c` has
`chkSgraph`. All become status codes (`seg_cmp` returns -2, `decide_point`,
`assignTracks`, `orthoEdges` return int, `_dot_splines` propagates) as Graphviz main
did, with `chkSgraph` kept as a test assertion and a production return code. On
failure `_dot_splines` re-runs with `GD_flags` switched to ET_SPLINE (what users get
today) and logs once. `PQcheck` (an O(n) loop on every heap op that C optimises
away) is debug-only. `traverse_polygon` recursion (O(20n) frames, ~24k for 300
nodes; JVM and TeaVM stacks fail around 10^4) is ported with an explicit stack.

### D7 Reference and test philosophy
`partition()` seeds `srand48(173)` and shuffles segments with `drand48()`; Windows
`dot` maps that to `rand()`, glibc mergesort is stable and MSVC quicksort is not,
2.38 truncates `edgeLen` and `htrack` to int while main keeps the double and rounds
(verified at `gvmaster/ortho.c:1060-1067`). So two `dot` binaries already disagree on
tie-breaks. Tests therefore assert invariants: every segment axis-aligned, endpoints
on the node border, no segment crosses a node box, expected segment count for
hand-checked cases, bounding box within tolerance, plus Graphviz's own regression
inputs (`tests/14.dot`, `1408.dot`, `1658.dot`, `1990.dot`, `2784.dot`, all under
1.5 KB) translated to `.puml` as crash-free tests. The port implements glibc
`srand48/drand48` (48-bit LCG, ~12 lines; `java.util.Random` is not a drop-in) so the
Linux `dot` on the PlantUML server is the reproducible reference where ties exist.

### D8 Evidence
- Before/after: 2x2 montage per feature (Smetana on | dot on ; Smetana off | dot
  off) regenerated with `Montage.java` from the probe corpus at 2x scale, plus a
  "before PR / after PR / dot" triple for the PR bodies (same script, different jars).
- Perf: JVM `Bench.java` (warm medians, 30 reps, ladder 10/24/48 classes, spline vs
  ortho vs polyline on Smetana, and Smetana ortho vs the external `dot` ortho as the
  user experiences it); browser `bench-ladder.js` ratio-to-reference per rep
  alternation (pragma smetana ortho vs pragma smetana spline vs viz ortho). Charts:
  JSON -> `evidence.html` -> Playwright screenshot at deviceScaleFactor 2 (the recipe
  from the perf PRs).
- Hosting: `pr-assets` orphan branch on lemonlion/plantuml under
  `smetana-parity/<pr>/<name>.png`, referenced by raw.githubusercontent URL. Images
  are generated by Appendix C before posting; the drafts carry the final file names.

### D9 Prose
See 9.4. The appendices are written in that style already; a final pass (grep for
em dashes, curly quotes, "delve", "leverage", "robust", "seamless", "comprehensive",
"crucial", emoji, "In summary", triple parallel phrasing) runs before posting.

## 5. Deliverables

### 5.1 The umbrella issue
One issue (draft in Appendix A) using the feature_request template headings with a
short context preface, listing every gap in 3.1-3.5 with its evidence image, stating
the non-gaps (3.6) so nobody re-audits them, and carrying a checklist of the nine
PRs. Each PR body says "Tracked in #<issue>" (never "Fixes", since the issue outlives
any one PR). Labels to apply with triage rights after posting: `smetana`,
`enhancement`. People to mention in the first comment, not the body: The-Lum
(triages every smetana issue), Vampire (#1703 reporter, whose diagram PR A
re-renders).

### 5.2 PR A: spacing and rank-search parity (maker only)
- Branch `smetana-spacing`. Files: `sdot/CucaDiagramFileMakerSmetana.java`, `svek/DotStringFactory.java`
  (both call the new helper), a new `svek/DotSeparations.java` (+ Vega references).
- Change (`getTextBlockInternal`, next to `margin` at 587): compute nodesep and
  ranksep exactly as `DotStringFactory:115-131` (extract the dzeta/floor/override
  logic into a small static helper both makers call, e.g. `DotSeparations.of(links,
  skinParam, diagramType)` in `svek/`, so the two paths cannot drift) and
  `agsafeset(g, "nodesep"/"ranksep", inches)`; `agsafeset(g, "searchsize", "500")`.
  The maker already has the label dimensions it needs (`getLabel`/`getQuantifier`
  via `createHackInitDimensionFromLabel`).
- Tests: (1) new JUnit `SmetanaSeparationTest` rendering a 4-class diagram with
  `skinparam nodesep 120` / `ranksep 150` and asserting measured node gaps from the
  SVG (parse `<rect>`/`<g>` positions) within 2px of the requested values, and that
  the defaults produce gaps >= 35/60; (2) the helper unit test proving both makers
  produce the same nodesep/ranksep for the same links; (3) Vega re-baseline
  (`VEGA_FORCE_WRITE=true`) with the diff reviewed diagram by diagram: only
  translations, no topology changes.
- Evidence: `nodesep-120` and `ranksep-150` montages before/after; density ratio
  chart over the 59-probe corpus (0.79 -> ~1.0); #1703's Spock diagram rendered
  before/after/dot.
- Perf: none expected (attributes only); one JVM ladder run to show no change.
- Size: ~40 lines + ~80 reference SVGs. Risk: users who tuned diagrams for the old
  density see wider output; that is the documented behaviour of the other engine.

### 5.3 PR B: layout constructs the dot path already sends
- Branch `smetana-constructs`. Files: maker; `gen/lib/dotgen/dotinit__c.java`
  (2 lines); `smetana/core/Globals.java` (optional circle descriptor).
- Changes:
  - `createEdge` (861): `if (!link.isConstraint() || link.hasTwoEntryPointsSameContainer()) agsafeset(e, "constraint", "false")`; port the two stubbed lines in `dot_init_edge` (`dotinit__c:203-204`: `ED_xpenalty(e, 0); ED_weight(e, 0);`) and, for symmetry, the `group` branch (196-198). Without the port lines the attribute throws.
  - `exportGroup`/`exportEntities`: group leaves by `Entity.getTogether()` (walk parents like `Cluster.addTogetherWithParents`) into an unlabelled `agsubg(parent, "cluster<id>t<n>")` exactly as `Cluster.printTogether` (528).
  - `exportEntity` (469): `shape=ellipse` for `ShapeType.OVAL` and `CIRCLE` (square dims make a circle); keep `box` for the rest. Verify `Drawing.getCorner` still uses the bounding box.
  - `printEntityInternal` (910): replace the NOT YET IMPLEMENTED print with the `getMaxWidth`/`setParamSameClassWidth` logic from `GraphvizImageBuilder:372-397`.
- Tests: JUnit per construct using the probe diagrams: norank (C's y below B's, A->C endpoint order), together (A and D share a rank: same y within 1px), ellipse (arrow tip distance to the ellipse <= 1px using the `analyse.py` formula ported to Java), sameClassWidth (all class rects equal width, no stderr); Vega fixtures for each under a new `src/test/resources/vega/nonreg/group<issue>/` directory (naming follows the existing `group2712` convention).
- Evidence: norank, together, oval-clip, sameclasswidth montages before/after.
- Size: ~120 lines. Risk: `together` relies on the port's cluster containment (`position__c.contain_subclust`) keeping members adjacent; the probe verifies it.

### 5.4 PR C: drawing parity in `SmetanaEdge`
- Branch `smetana-edge-drawing`. Files: `sdot/SmetanaEdge.java`, maker (label
  construction), possibly a shared helper extracted from `SvekEdge` where the code is
  copied verbatim (arrow direction, rainbow extremities, middle decor), to stop the
  two classes drifting further.
- Changes (each cites the `SvekEdge` lines to mirror): ArrowHeadColor (1113-1139),
  middle decorations (982-989, + 2x7px label shield in `createEdge`), magic arrows
  (implement `getArrowDirection`/`InRadian` from 177-217 over `getDotPathInternal`;
  build the label inside `drawContent` with the edge as `GuideLine`; restore the
  `addMagicArrow` calls at maker 746/765; size the label with the arrow width),
  qualified associations (`Kal` from 242, `computeKal` 1069, draw 1015-1019, overlap
  via `SvekNode.fixOverlap`), supplementary colours (1141), package self-link shift
  (863-869), link constraints (430, 994-1013), label collision pass (`manageCollision`
  1205 over `bibliotekon.allNodes()` before drawing), SVG metadata (844, 944 with a
  shared id set), cardinality style and clickable label under URL.
- Tests: one JUnit per item over the `wf1/attr-audit/probe/puml` diagrams asserting
  on the SVG (polygon fill colour for ArrowHeadColor, ellipse count for middle
  decors, polygon count for magic arrows, presence of the qualifier text, path count
  for rainbow, self-loop bbox outside the package rect, `<!--link` comment present);
  Vega fixtures for each.
- Evidence: arrowhead, middle, magic, kal, rainbow2, pkgself before/after (one composite); dot as
  the reference column.
- Size: ~250 lines. Risk: the label-as-TextBlock change touches every labelled
  Smetana edge; Vega catches regressions.

### 5.5 PR E: `skinparam linetype polyline`
- Branch `smetana-polyline`. Files: `gen/lib/common/utils__c.java` (`edgeType`),
  `gen/lib/dotgen/dotsplines__c.java` (three ET_PLINE sites),
  `gen/lib/common/splines__c.java` (`polylineMidpoint`), maker (`splines=`).
- Changes: port `edgeType` from 2.38 `utils.c` (~45 C lines: switch on the first
  character, warn and default on unknown); translate `dotsplines__c:1834-1839`
  (mirror the ported final-segment code at 1885), the 10-line ET_PLINE branch of
  `makeSimpleFlat` (1109-1118, `pointfof` + `CArray` `get__/___`), `polylineMidpoint`
  (2.38 `splines.c:1278-1318`, ~35 lines), and `make_flat_bottom_edges` (1445, ~60
  lines; only reachable with bottom-side ports, so it may stay a stub with a
  comment if H does not need it). Maker: `agsafeset(g, "splines", "polyline")` when
  `skinParam.getDotSplines() == POLYLINE` (the same switch later emits `ortho`).
- Tests: JUnit `SmetanaPolylineTest`: for each probe with polyline, parse every
  path's cubic segments and assert control points coincide with their endpoints
  (straight segments), and that the render differs from the spline render where dot
  differs (class, deployment, state, lr-class) and is identical where dot is identical
  (component, usecase, object); a 5-rank straight chain to hit the `smode` loop; a
  flat adjacent pair to hit `makeSimpleFlat`; Vega fixtures.
- Evidence: class-polyline, state-polyline, lr-class-polyline montages
  before/after/dot.
- Perf: JVM ladder spline vs polyline (expected: polyline slightly faster).
- Size: ~200 lines. Risk: `routepolylines` already ported; the new branches are
  index arithmetic, unit tests per branch.

### 5.6 PR F1: `lib/ortho` port, unit-tested, not yet wired
- Branch `smetana-ortho-port`. New files under `src/main/java/gen/lib/ortho/`:
  `trapezoid__c`, `partition__c`, `rawgraph__c`, `sgraph__c`, `fPQ__c`, `maze__c`,
  `ortho__c`; new `h/ST_*` structs (section 6.2); `smetana/core` additions
  (`ZType` cases, `FieldOffset.p`, `dtmatch`, `drand48`, four `Dtdisc` fields on
  `Globals`). No call from `_dot_splines` yet, so zero behaviour change and the Vega
  suite is untouched.
- Tests (section 6.7 oracles): trapezoidation of one and two boxes in a frame
  against a hand-computed decomposition; `partition()` of k random non-overlapping
  boxes tiles `BB \ boxes` exactly (area sum, pairwise non-overlap, no rectangle
  intersects a box); `top_sort` on small DAGs; `shortPath` vs brute-force Dijkstra on
  a hand-built grid; maze invariants (every search node has two cells, ordinary cells have at most
  four sides while node cells carry a growable side list, `markSmall` on a thin node); `convertSPtoRoute` from a hand-built `N_DAD`
  chain; the `eqEndSeg/overlapSeg/ellSeg` truth table from main (with #2047 fixed);
  `assignTracks` on two crossing routes; growth paths of the on-demand tables; the
  glibc `drand48` sequence against known values.
- Evidence: none needed beyond the test report; the PR body explains the
  translation conventions and the folded-in fixes (section 6.5).
- Size: ~4,800 lines + ~900 test lines. Commits ordered bottom-up (structs,
  trapezoid, partition, rawgraph, sgraph+fPQ, maze, ortho) so each compiles and is
  tested on its own.

### 5.7 PR F2: wire `skinparam linetype ortho` on Smetana
- Branch `smetana-ortho`. Files: `dotsplines__c` (ET_ORTHO branch, `resetRW`,
  `setEdgeLabelPos`), `splines__c` (`arrow_clip` guard), `h/ST_Agnodeinfo_t` (`alg`
  widened to `__ptr__`) + `Macro.ND_alg` overloads, maker (`splines=ortho`), `Globals`
  (`Concentrate` guard).
- Changes: after the ET_CURVED block in `_dot_splines` (444-457): `resetRW(g); if
  (GD_has_labels(g) & EDGE_LABEL) setEdgeLabelPos(g); if (orthoEdges(g) != 0) ->
  fall back to ET_SPLINE once; goto finish`; the finish block frees `P.boxes` only
  when `et != ET_CURVED && et != ET_ORTHO` (the port's version would NPE otherwise);
  `arrow_clip` guard becomes `if (eflag[0] != 0 || sflag[0] != 0)
  UNSUPPORTED("arrowOrthoClip")` (never fires: the maker sets both arrows to none);
  maker emits `splines=ortho` under ORTHO. `SmetanaEdge` needs no reader change
  (`attachOrthoEdges` emits one bezier per edge with degenerate `(A,A,B,B)` cubics;
  `DotPath` tangents already fall back to the chord). `DotPath.muteToRoundOrthogonalPaths`
  (edgeCornerRadius, PR #2389) applies unchanged.
- Tests: JUnit `SmetanaOrthoTest` over the 21 ortho probes: every path segment
  axis-aligned (|dx| < 0.5 or |dy| < 0.5 per cubic, control points degenerate),
  endpoints within 1px of a node border, no segment interior inside any node rect,
  segment count for three hand-checked diagrams, bbox within 15% of dot's for the
  corpus (the dot comparison only when a `dot` binary is found, skipped on CI); the four
  chkSgraph regression graphs as `.puml` render without falling back (assert the log line is absent) and the GV#2784 graph takes the failure path cleanly (fallback, one log line, no exception); fallback path test (a synthetic failure
  returns a spline render, not an exception); Vega fixtures for every ortho probe
  (`output: svg`); browser check (section 7.3).
- Evidence: class-ortho, state-ortho, lr-class-ortho, ortho-packagelinks,
  ortho-selfloop, usecase-ortho, deployment-ortho montages before/after/dot; perf
  ladder JVM (spline vs ortho on Smetana; Smetana ortho vs external dot ortho
  end-to-end) and browser (ratio to the stock engine); TeaVM bundle size delta
  (expected +5-6 KB raw, +1-2 KB gz on 3.95 MB / 1.08 MB).
- Size: ~200 lines + fixtures. Risk list in section 6.8.

### 5.8 PR G: xlabel placement and label-node alignment for Smetana ortho
- Branch `smetana-ortho-labels`. Files: `utils__c` (792 empty block, 647),
  `postproc__c` (185, 570-577, 580-590), `xlabels__c` (audit the 92 UNSUPPORTED
  lines; port the non-diagnostic ones incl. `xladjust`'s sliding loops ~30 lines and
  `getintrsxi`'s quadrant switch ~20 lines), maker (`xlabel` + `forcelabels` under
  ORTHO like `SvekEdge:434-436`), `SmetanaEdge` (`replaceDotPath`) + a shared
  `alignEdgesAtLabelNodes` extracted from `DotStringFactory:464-626` so both engines
  use one implementation.
- Tests: ortho-edgelabels / ortho-quantifiers / state-ortho: label rect does not
  intersect any node rect or any other label rect (that is stricter than dot,
  where #2326/#1996 still overlap; the assertion is on the PlantUML-side pass, so it
  should hold), label within 20px of its edge; Vega fixtures; the JUnit from
  `SvekEdgeOrthoLabelTest` style (PR #2665) reused for Smetana.
- Evidence: ortho-edgelabels, ortho-lr-quantifiers, state-ortho before (F2) / after
  (G) / dot.
- Size: ~250 lines.

### 5.9 PR D: `skinparam groupInheritance` (sametail)
- Branch `smetana-sametail`. Files: `gen/lib/dotgen/sameport__c.java` (port
  `dot_sameports`, `sameedge`, `sameport`, `free_list`, ~150 C lines, no cdt
  dependency; needs a `same_t`/`elist` struct in `h/`), `dot/DotData.java` (extract
  `removeIrrelevantSametail` into a static helper over links + skinParam + leafs),
  maker (call it before `printEntities`; `agsafeset(e, "sametail", uid)`;
  `Neighborhood` -> `EntityImageProtected` wrapping as `GeneralImageBuilder:112`).
- Tests: groupinh probe: exactly one hollow triangle at Base (count polygons with
  the extends fill), three edges sharing a tail point within 1px; threshold test
  (`groupInheritance 4` with three subclasses: no merge); Vega fixtures.
- Evidence: groupinh before/after/dot.
- Size: ~250 lines.

### 5.10 PR H: ports, member ports and label shields
- Branch `smetana-ports`. Files: maker, `SmetanaEdge`, `sdot/` new helper for port
  geometry.
- Changes: after `gvLayoutJobs`, for each link whose `getEntityPort1/2` carries a
  port id (`EntityPort.create`) or whose position `usePortP()`: move the
  `SmetanaEdge` start/end to the port rectangle from `((WithPorts) image).getPorts(stringBounder)`
  translated by the node corner, choosing the side the dot path would (`html_path`
  semantics: top for inputs, bottom for outputs, or the nearest side for member
  rows); export port nodes first/last inside their cluster so mincross keeps them
  at the top/bottom (interim for `rank=source/sink`); shields: when a link has a
  quantifier/role/Kal call `entity.ensureMargins(Margins.uniform(16))` before
  `exportEntity` so the node size includes the shield, then place the image inside
  the shield and clip the edge to the inner rectangle; combine with C's collision
  pass.
- Tests: ports-component: p1 on the top border, p2 on the bottom, Client->p1 does
  not intersect the Service rect; memberport: edge start within the gamma row's
  rect; quantifier: cardinality text rect does not intersect the arrow path; Vega
  fixtures; #1443 / #1441 style nested-ports diagrams as crash-free tests.
- Evidence: ports-component, memberport, quantifier, ports-state before/after/dot.
- Size: ~250 lines. Risk: approximation (the port does not know the port during
  layout, so routes may need a corner near the node); stated in the PR with the
  upgrade path (true `ED_tail_port` plumbing via `poly_port`, ~200 lines, plus the
  compass/beginpath TOP-side blocks listed in the port audit).

### 5.11 Effort summary

| PR | Lines (code / tests) | Focused days | Depends on |
|---|---|---|---|
| A spacing | 40 / 60 + Vega | 0.5 | none |
| B constructs | 120 / 120 | 1 | A (for stable fixtures) |
| C drawing | 250 / 200 | 2 | B |
| E polyline | 200 / 150 | 1.5 | none (parallel to B/C) |
| F1 ortho port | 4,800 / 900 | 6-8 | none |
| F2 ortho wired | 200 / 300 + fixtures | 2-3 | E (edgeType), F1 |
| G ortho labels | 250 / 150 | 1.5-2 | F2 |
| D groupInheritance | 250 / 100 | 1.5 | B |
| H ports and shields | 250 / 150 | 2-3 | C |

Total about 18-22 focused days; the ortho pair is 8-11 of them.

## 6. The ortho port (condensed from `wf1/ortho/design.md`, which has the line-by-line inventory)

### 6.1 Findings that shaped the plan
1. The `#ifdef ORTHO` block was never translated: Graphviz defines `ORTHO` in
   `config.h` (2.38 `configure.ac:2988-3000`, default on); the translator ran
   without it. Hence no `ET_ORTHO` branch, no `setEdgeLabelPos`, `resetRW` stub.
2. 2.38 already disables label-aware ortho routing (`doLbls` forced to 0 after the
   "Try using xlabels" warning; `mkMaze`'s `if (doLbls) {}` is empty). There is no
   "keep 2.38 label routing vs follow main" choice; only `setEdgeLabelPos` (22 lines)
   matters (D3).
3. `arrowOrthoClip` is not needed: the maker sets both arrows to `none`, so
   `sflag == eflag == 0`; only the stubbed guard in `arrow_clip` (`splines__c:147-150`,
   which throws for every ortho edge regardless) must become real code.
4. No reader change: `attachOrthoEdges` emits one bezier per edge of degenerate
   `(A,A,B,B)` cubics (`ortho.c:1177-1198`); `SmetanaEdge.getDotPathInternal` reads
   any bezier list and `DotPath` tangents fall back to the chord when a control point
   coincides with its endpoint.
5. Byte parity with a `dot` binary is not well defined even between two `dot`
   builds (D7).

### 6.2 Inventory and Java line estimates (2.38 sources; debug/PostScript code never ported)

| C file (lines) | Port to | Java lines | Notes |
|---|---|---|---|
| `trapezoid.c` (1082) | `trapezoid__c` | ~1,050 | Seidel trapezoidation; `add_segment` alone is 577 lines of index juggling; tables become growable (fixes #56/#1880); main's `-1` error return in `add_segment` (#2784). `locate_endpoint` recursion is O(log n), fine. |
| `partition.c` (769) | `partition__c` | ~800 | `traverse_polygon` (296 lines, recursion O(#trapezoids) = O(20n)) ported with an explicit stack that preserves pre-order (push the four children in reverse, test `visited[]` at pop); file statics become a small context object; `generateRandomOrdering` via glibc `drand48`; main returns NULL when a trapezoidation is empty. |
| `sgraph.c` (273) + `fPQ.c` (162) | `sgraph__c`, `fPQ__c` | ~330 | flat arrays; Dijkstra with the negative "seen" encoding; `PQ` becomes an explicit `ST_pq_t` threaded like main (no `Globals` statics); `PQcheck` debug-only. |
| `maze.c` (520) | `maze__c` | ~480 | two `Dtoset` dictionaries keyed on `pointf`; **the #1408 crash is here**: 2.38 shares one `sides` array sized `g->nnodes` across gcells and overflows it; port main's per-gcell growable list. `chkSgraph` = return code in production, assertion in tests. |
| `ortho.c` (1563) | `ortho__c` | ~1,250 | channels as a two-level `Dtoset` with the "containment == equal" comparator; `eqEndSeg` typo fixed (#2047); `seg_cmp` returns -2 instead of `longjmp`; `decide_point` returns a status; `htrack` rounds (main); `edgeLen` double; `attachOrthoEdges` skips routes with no segments (main 3c0ef274). |
| `rawgraph.c` (165) | `rawgraph__c` | ~120 | Java collections: per-vertex insertion-ordered `int[]` adjacency (main semantics), `top_sort` recursive DFS (depth <= segments per channel). `intset.c` dropped. |
| `pointset.c` | not ported | 0 | only under `concentrate`, never set by PlantUML; `if (zz.Concentrate) UNSUPPORTED(...)` at the top of `orthoEdges` keeps the translation honest. |
| `h/ST_*` (about twenty classes), `ZType` cases (7), `FieldOffset.p`, `Globals` discs, `drand48`, `dtmatch` | | ~650 | section 6.3 |
| integration (`dotsplines__c` ET_ORTHO branch, `resetRW`, `setEdgeLabelPos`, `polylineMidpoint`, `arrow_clip` guard, `ND_alg` widening, maker) | | ~150 | PR F2 |
| **Total** | | **~4,800** (~3,200 as plain Java; the `ENTERING/LEAVING/@Original` scaffolding is the repo convention) | tests ~1,200 |

### 6.3 Struct mapping (port conventions observed in `h/` and `smetana/core`)
Every struct is `final public class ST_x extends UnsupportedStarStruct` with public
fields; nested by-value structs are `final ST_pointf p = new ST_pointf()` copied with
`___()`; arrays of struct are `CArray<ST_x>` (needs a `ZType` case) with `plus_/get__`;
arrays of pointers are `CArrayOfStar<ST_x>`; unions flatten into prefixed fields;
`Dtlink_t link` members become `final ST_dtlink_s link = new ST_dtlink_s(this)` plus
`getTheField(FieldOffset)` and `castTo` boilerplate (pattern: `h/ST_Agsym_s.java:66-74`);
comparators are `CFunction` objects; `int*` out-params are `int[]{0}`.

| C | Java | Notes |
|---|---|---|
| `paird`, `pair`, `enum bend` | `ST_paird`, `ST_pair`, int constants | `pair` returned via out-param as in main |
| `segment`, `route` | `ST_segment`, `ST_route` | `CArray` (pointer arithmetic on `rte.segs + j`), `track_no` plain int |
| `channel`, `chanItem` | `ST_channel`, `ST_chanItem` | keyed structs; `chanItem` key is a bare double: keep `v` on the object, comparator reads `.v`, lookups use a scratch object on `Globals` (existing pattern `agsubrepScratch`) |
| `cell`, `maze` | `ST_cell`, `ST_maze` | `sides` = fixed 4 for ordinary cells, growable list for node cells (6.2 maze) |
| `snode`, `sedge`, `sgraph` | `ST_snode`, `ST_sedge`, `ST_sgraph` | per-node `int[]` adjacency instead of one shared sliced array |
| `snodeitem` | `ST_snodeitem` | key `pointf`, two comparators (x-then-y, y-then-x) with `exeCmpInt` fast paths |
| `segment_t`, `trap_t`, `qnode_t` | `ST_segment_t`, `ST_trap_t`, `ST_qnode_t` | 1-based like C; whole-struct copies `tr[tl] = tr[tu]` via `___()`; growable |
| `monchain_t`, `vertexchain_t` | same names | `int[4]` fields |
| `vertex`, `rawgraph` | `ST_vertex`, `ST_rawgraph` | Java adjacency (6.2) |
| `epair_t` | `ST_epair_t` | `CArrayOfStar` + `JUtils.qsort` (stable bubble sort, fine for hundreds of edges) with `Double.compare` |
| `PQ` statics | `ST_pq_t` | threaded explicitly |
| `jmp_buf jbuf` | none | status codes (D6) |

`ND_alg` widening: `h/ST_Agnodeinfo_t.java:61` declares `ST_Agedge_s alg` (dot stores
the flat-edge-label edge there); ortho stores `cell*`. Change the field to `__ptr__`,
keep the typed getter for dotgen callers (cast), add `ND_alg(ST_Agnode_s, ST_cell)`.
Label vnodes and real nodes never share a node. `ND_xsize/ND_ysize` are not port
macros: use `ND_lw + ND_rw` / `ND_ht`.

### 6.4 Missing support code
- cdt: `dtmatch(d, key)` = `d.searchf.exeSearch(zz, d, key, DT_MATCH)` (3 lines next
  to `dtsearch`, the macro is already quoted in `Macro.java:1440`); `dtlink` = the
  `right` field; `dtwalk`/`dtsize` are not used by lib/ortho at all. Four
  `ST_dtdisc_s` fields on `Globals` initialised like `Hdisc` (475-483). Implement
  `exeCmpInt` for the four comparators (hot path of `findSVert`, 4x per cell, and
  `chanSearch`).
- Allocation: `N_NEW` -> `CArray.ALLOC__` (zeroed), `ALLOC` growth ->
  `CArrayOfStar.REALLOC`, shrinking `realloc` -> keep array and `n`, `free` -> nothing,
  `memset` -> fresh arrays.
- `drand48`: glibc exactly (`X = (0x5DEECE66D * X + 0xB) mod 2^48`, `srand48(seed)`
  -> `X = (seed << 16) | 0x330E`, `drand48()` -> `X / 2^48`), ~12 lines of `long`
  arithmetic; main's 0-based `generateRandomOrdering`.
- `setjmp/longjmp`: the port cannot longjmp (`JUtils.setjmp` returns 0; every
  `longjmp` site is `UNSUPPORTED`); adopt main's error returns (D6). Failure =
  re-run `_dot_splines` as ET_SPLINE once.
- `assert`: return codes or vanish with on-demand allocation; `chkSgraph` test-only.

### 6.5 Post-2.38 Graphviz fixes to fold in (read from `gvmaster/` and CHANGELOG)

| Fix | Decision |
|---|---|
| `chkSgraph` assertion failures / segfault GV#14, #1408, #1658, #1990 (4.0.0, MR !2672): epsilon compares in `traverse_polygon` and `hcmpid/vcmpid`, and the per-gcell sides list (13.0.0, MR !4219) | fold in (dominant 2.38 crash class; free in Java) |
| `eqEndSeg` typo `S2l2=!T2` GV#2047 (2.48.0) | fold in |
| trapezoid/qnode on-demand allocation GV#56/#1880 (7.0.4, MR !2973) | fold in |
| negative `trnum` guard (5c995804) | fold in (Java would throw on the index) |
| error returns replacing `setjmp/longjmp` GV#1801 (MR !1917) | fold in (D6) |
| invalid trapezoid failure propagation GV#2784 (16.1.0, MR !5001) | fold in (the whole error spine) |
| `attachOrthoEdges` skips routes with no segments (3c0ef274); `decide_point` explicit init; `edgeLen` overflow (221038c7); `edgecmp` without signed subtraction | fold in (cheap) |
| removal of the dead `doLbls` path (2026-04) | follow main; keep `setEdgeLabelPos` |
| `htrack` `round()` (main `ortho.c:1066`), double `edgeLen`, insertion-ordered rawgraph, double `updateWt` | follow main (each a one-line switch; 2.38 behaviour can be pinned by a flag if a test ever needs it) |
| GV#2082 `inside_polygon` polarity (2.47.3), GV#2361/#2183 concentrate, GV#144 `arrowOrthoClip` dir=both, `radius` rounded corners (14.1.0; PlantUML rounds on its side), twopi/circo/fdp/neato items, thread-safety globals (#2558; the port threads `Globals zz` anyway) | not applicable |

### 6.6 Strategy (see D2)
"2.38 structure, main semantics": port the 2.38 files function by function so the
`@Original` keys and layout match `gen/`, but take the error-return spine, on-demand
allocation and per-gcell sides from main, adopt main's four behavioural drifts, and
reproduce glibc `drand48`. Never port the debug/PostScript code, `pointset`,
`intset`, `PQcheck`, `PQprint`, `odb` flag parsing.

### 6.7 Bottom-up port order (each step compiles and has its oracle)
1. `h/` structs + `ZType` + `drand48` + `dtmatch`. Test: the `drand48` sequence and
   one dictionary of each keyed struct in isolation (the `FieldOffset` sign trick is
   easy to get subtly wrong).
2. `trapezoid__c`. Test: one and two boxes inside a frame against a hand-computed
   decomposition; the growth path.
3. `partition__c`. Test: k random non-overlapping boxes -> rectangles tile
   `BB \ boxes` exactly (area sum, pairwise non-overlap, no rectangle intersects a
   box). Strongest oracle in the port, engine-independent.
4. `rawgraph__c`. Test: `top_sort` on small DAGs.
5. `sgraph__c` + `fPQ__c`. Test: `shortPath` on a hand-built grid vs brute-force
   Dijkstra.
6. `maze__c`. Test: every search node has two cells, ordinary cells have at most four sides (node cells carry a growable side list),
   `markSmall` on a thin node.
7. `ortho__c` minus `orthoEdges`. Test: `convertSPtoRoute` from a hand-built `N_DAD`
   chain; the `eqEndSeg/overlapSeg/ellSeg` truth table (from main, #2047 fixed);
   `assignTracks` on two crossing routes.
8. `orthoEdges` + `attachOrthoEdges`; ET_ORTHO branch; `setEdgeLabelPos`;
   `polylineMidpoint`; `arrow_clip` guard; maker (PR F2).
9. End to end: Vega corpus + `SmetanaOrthoTest` + browser check (PR F2).

### 6.8 Risks specific to ortho
1. 2.38 crash classes: all four folded in (6.5); the four regression inputs are tiny
   and become `.puml` tests; fallback tied to a log line so silent degradation is
   visible.
2. Recursion depth: `traverse_polygon` iterative (6.2). `locate_endpoint` and
   `DFS_visit` are safe.
3. Performance: `partition` is two trapezoidations plus `hd*vd` rectangle
   intersections (O(n^2) candidates); `orthoEdges` runs one Dijkstra per edge over
   ~4 cells search nodes / ~6 cells edges. For 200 nodes / 300 edges: a few 10^7 to
   10^8 heap operations, sub-second on the JVM, seconds in TeaVM if the heap or
   dictionaries allocate. Mitigations: no `PQcheck`, `exeCmpInt` fast paths, scratch
   key objects reused, no exceptions on the routing path (D6), and `dot` itself is
   slow with `splines=ortho` on large graphs, so parity includes parity of cost; the
   existing PlantUML size guard applies.
4. Fidelity ties (D7): geometry-tolerant tests, small hand-verified corpus.
5. Labels: `label=` semantics until PR G; document that a route may pass under a
   label exactly as Graphviz 2.38 does.
6. Clusters: the maze contains only real nodes (`mkMaze` iterates `agfstnode`), so
   routes ignore cluster boundaries, same as dot; `SmetanaEdge.simulateCompound`
   works on degenerate cubics.
7. Port-idiom friction: `ND_alg` widening touches a shared struct; four new keyed
   structs need the `castTo`/`getTheField` boilerplate; seven new `ZType` cases.
8. Kronikol4J: no impact (it pins PlantUML output, not the engine) unless Kronikol
   enables `linetype ortho` in Smetana-rendered reports, at which point it inherits
   the new geometry for free.

---

## 7. Test and evidence infrastructure

### 7.1 Vega reference renders (`src/test/java/test/vega/VegaTest.java`)
- `TitledDiagram.FORCE_SMETANA = true` in `@BeforeAll`: every one of the 301 `.puml`
  under `src/test/resources/vega/` renders through Smetana, and the ~80 Graphviz-family
  ones are the regression net for every PR here.
- New fixtures go under `src/test/resources/vega/nonreg/group<issue-number>/` (the
  existing convention: `group2712`, `group2882`, ...), one `.puml` per probe with the
  YAML header `--- output: svg ---` and the `.svg` reference generated with
  `VEGA_FORCE_WRITE=true ./gradlew test --tests test.vega.VegaTest`.
- PR A's re-baseline is reviewed as a diff of the 80 SVGs: expected changes are
  translations of `<rect>`/`<path>` coordinates and viewBox growth only; any
  topology change (different rank order, crossing count) is a bug in the PR.
- Every later PR adds fixtures and must not touch existing references (except
  where the PR intends to: e.g. C changes the arrowhead colour SVG in fixtures that
  use ArrowHeadColor).

### 7.2 Geometry assertions (JUnit, `src/test/java/test/sdot/`)
A small test utility `SvgGeometry` (test scope only): parse the emitted SVG into
node rectangles (`<rect>`/`<ellipse>` with their `id`/comment), edge paths (cubic
segments from `d=`), text boxes; helpers `isAxisAligned(seg)`, `isDegenerateCubic`,
`distanceToBorder(point, rect|ellipse)`, `intersects(seg, rect)`, `gapBetween(rectA,
rectB)`. Tests per PR use it (5.2-5.10). Byte hashes and `C` counts are never
asserted (2.2). Where a test compares to the dot path, it renders the same diagram
with the dot maker only if `GraphvizUtils` finds a `dot` on the machine and skips
otherwise (CI has no Graphviz: the Ant job installs only `ant`; the matrix job runs
`gradle test` on a bare image). So CI asserts invariants; the dot comparison is a
local, opt-in check documented in the test.

### 7.3 Browser check (`tools/browser-test/check-linetype.js`)
Follows the refactored checks (#2891): `createCheckReporter`, `parseTargetArg`,
`createMountedServer/startServer`, `openReadyPage`, `renderOn` with
`includeHash`, `includeShapeCounts`, `includeWasmCount` from `tools/lib/`. Contract:
on the page without `viz-global.js`, a class diagram with `!pragma layout smetana`
plus `skinparam linetype ortho` renders with zero WebAssembly, its normalised hash
differs from the same diagram without the skinparam, and every path segment is
axis-aligned (evaluated in the page from the SVG DOM); `linetype polyline` differs
from spline and has only straight segments; `nodesep 120` widens the canvas. The
control page with `viz-global.js` pins that the default viz path is unchanged.
Added to `.github/workflows/browser-test.yml` next to `check-smetana.js` and to
`tools/browser-test/README.md`.

### 7.4 Performance evidence
- JVM: `Bench.java` (scratchpad; same generator as the browser bench) renders a
  10/24/48-class ladder 30 warm reps each, medians, for Smetana spline / polyline /
  ortho and, as the user-experienced comparison, PlantUML with external `dot` ortho
  (process spawn included, which is what a user waits for). Reported as a table
  and a bar chart.
- Browser: `bench-ladder.js` per-rep alternation, ratio to the stock engine (spline,
  same diagram): pragma smetana ortho, pragma smetana spline, viz ortho. The bar to
  beat is "ortho on Smetana costs no more than ortho on viz relative to spline".
- TeaVM bundle size: raw and gzip delta of `plantuml.js` before/after F1+F2.
- Upstream `tools/perf-bench` rows: the corpus is sequence diagrams and its README
  says changing the corpus resets band history, so adding class-ortho rows is Q5,
  not a default.

### 7.5 Evidence images
`Montage.java` (scratchpad) builds the 2x2 grids; for PR bodies a three-panel
variant (before / after / dot) is generated from two jars (`master` and the PR
branch) plus `-graphvizdot`. PNG at 2x (`-Dplantuml.dpi` or the SVG->PNG route via
Playwright screenshot of the SVG for crisp text). Charts: `evidence.html` +
`shoot.js` (Playwright, deviceScaleFactor 2). Appendix C has the commands.

### 7.6 Regression corpus from Graphviz
`tests/14.dot`, `1408.dot`, `1658.dot`, `1990.dot`, `2784.dot` (60-711 bytes each,
GitLab main). Re-expressed as `.puml` class diagrams with the same node/edge
structure (the graph shape is what triggers the bugs, not the DOT text), so nothing
under Graphviz's EPL is copied into the repo. The four chkSgraph graphs must render under `linetype
ortho` without the fallback log line; the 2784 graph must take the failure path cleanly (spline fallback, one log line, no exception).

## 8. Verification protocol (per PR, before it is opened)

1. `./gradlew test` green locally (VegaTest + the PR's JUnit), with the Vega diff
   reviewed file by file for PR A and spot-checked for the others.
2. Java 8 build: `ant -noinput` with a JDK 8 (`JAVA_HOME` pointing at Temurin 8), the
   same command the CI job runs; catches any Java 9+ API in `gen/`.
3. TeaVM build (`gradle` task that produces `plantuml-mit/build/npm-plantuml`, as
   `browser-test.yml` does) and the browser checks: `check-smetana.js`,
   `check-viz-fallback.js`, `check-viz-missing.js`, `check-linetype.js` (from F2 on).
4. Perf ladder (7.4) for E, F2, G; a no-change run for A.
5. Montages regenerated for every probe the PR claims to change; the images named
   in the PR body exist on `pr-assets` before the PR is posted.
6. Prose lint on the PR body (9.4).
7. After opening: CI matrix (Ubuntu/Windows/macOS x Java versions), the Ant job, the
   browser-test workflow; if CI needs a rerun, an empty commit (no rerun rights).
8. Cross-link: "Tracked in #<issue>" in the body; tick the checklist in the issue
   once merged (triage rights allow editing the issue).

---

## 9. Upstream mechanics

### 9.1 Branching
One branch per PR in `C:/Code/plantuml`, created from `origin/master` at the time of
opening, pushed to `fork` (lemonlion/plantuml). Rebase, never merge, when master
moves. Series order is D1; a later PR that depends on an earlier one is opened only
after the earlier one merges (the maintainer merged the perf series within days,
so waiting costs little and keeps each diff clean).

### 9.2 Images
`pr-assets` orphan branch on the fork (exists, head `da232c7`): `git worktree add
../plantuml-pr-assets pr-assets`, put files under `smetana-parity/<pr-key>/`, commit,
push, then reference `https://raw.githubusercontent.com/lemonlion/plantuml/pr-assets/smetana-parity/<pr-key>/<name>.png`.
Alt text on every image. Keep the 2x2 grids under ~1200px wide so GitHub does not
downscale them into mush.

### 9.3 sjpp and build markers
`gen/`, `smetana/`, `h/` carry no sjpp markers today and the new code needs none if
it stays in the Java 8 subset (1.2). If any JVM-only diagnostic is added (it should
not be), wrap it in `// ::comment when __TEAVM__` blocks with the comment above the
marker, as the perf PRs did. `javacRelease` stays 11 for Gradle; the Ant job is the
Java 8 gate.

### 9.4 Prose house style (issue, PR bodies, commit messages, code comments that will be read in review)
- First person, informal, specific. Say what was measured and how. Numbers in
  sentences are fine when they are the point ("0.79 of Graphviz's canvas area").
- No em dashes, no en dashes as punctuation, no curly quotes, no emoji, no bold
  lead-ins on bullets, no headings inside a PR body beyond the template's, no
  "In summary", no "comprehensive/robust/seamless/leverage/delve/crucial/ensure",
  no lists of exactly three adjectives, no closing offer.
- The maintainer prefixes his own commits with gitmoji; contributor PR titles in the
  merged perf series were plain sentences. Keep titles plain: "Smetana: send nodesep,
  ranksep and searchsize like the Graphviz path does".
- Respectful about the port: it is a huge piece of work and most of the gaps are
  one-line omissions; say so.
- Lint before posting: `grep -nP '[\x{2014}\x{2013}\x{201C}\x{201D}\x{2018}\x{2019}]|\b(delve|leverage|robust|seamless|comprehensive|crucial|ensure)\b|[\x{1F300}-\x{1FAFF}]' draft.md`
  must print nothing.

### 9.5 CI facts that matter
- `ci.yml`: Ant/Java 8 job (`ant -noinput`, then a version check expecting exit
  code 16 and an ASCII sample render), a Gradle matrix job (`gradle test`), an
  artifacts job (Java 21). No Graphviz on any runner; the Vega results are uploaded
  as an artifact (`vega-results`) which is handy for reviewing PR A's churn.
- `browser-test.yml` builds the TeaVM engine and runs the `tools/browser-test`
  checks; `perf-bench.yml` is dispatch-only.
- No rerun rights: the empty-commit trick (`git commit --allow-empty -m "ci"` then
  push) is the way to retrigger a flaky run.

### 9.6 People
- arnaudroques: author, merges everything, owns Smetana (invited collaboration on
  the port and asked for tests in discussion #1346).
- The-Lum: triages every smetana issue, keeps the port/nesting issue lists
  (#1443, #1441, #1068, #1236, #1602, #1766); will cross-reference the issue within
  a day.
- Vampire (#1703), davmf (ortho PRs #2389/#2395), hundredGrand (open #2665),
  dnguyenv (#2645/#2654), akaGelo (#2796): mentioned only where their work is
  reused or their issue is answered.

---

## 10. Documentation that becomes wrong or incomplete

plantuml.com is maintained by the maintainer outside the code repo; each PR body
lists the sentences it outdates so he can update them, and the issue collects them:
- `plantuml.com/layout-engines`: "Smetana ... Tends to make slightly straighter
  arrows" (spacing changes after A; ortho after F2); "ELK ... Supports only
  orthogonal layout" stays but is no longer the only orthogonal option without dot;
  the Layout section lists `linetype ortho/polyline`, `nodesep`, `ranksep`,
  `together`, `norank`, `hidden` as generic options with no engine caveat, which
  becomes true.
- `plantuml.com/smetana02`: has no limitation list any more; after the series a
  short "what still differs" paragraph (label placement under ortho until G, ports
  approximated, shapes beyond ellipse) would help.
- `plantuml.com/class-diagram` and `deployment-diagram`: "With Smetana ... The main
  rule is the opposite: Simple element first, then nested element" (ordering) is
  not touched by this plan (Q3) and stays; the deployment appendix uses `skinparam
  nodesep 5/10`, which Smetana starts honouring after A.
- `plantuml.com/graphviz-dot` and `faq`: version lists unaffected.
- In repo: `tools/browser-test/README.md` (new check), the plantuml-mit browser
  README if it lists Smetana limitations, `src/test/resources/vega/vega-summary.md`
  regenerates on its own.
- Kronikol: the wiki page PlantUML-Browser-Rendering mentions the Smetana-only
  extension path; once the engine pin moves past these PRs it can say that
  `linetype ortho` works in reports.

---

## 11. Risks and mitigations (series level)

| Risk | Mitigation |
|---|---|
| The maintainer prefers one big PR or a different order | The issue proposes the order and asks; PRs are independent enough to be reordered. |
| PR A's 80-file reference churn hides a real regression | Review the diff as translations only; the `SvgGeometry` invariant tests run on the same fixtures; the #1703 diagram is the human check. |
| The ortho port is slow in TeaVM | D6 (no exceptions), fast comparators, no `PQcheck`, measured on the ladder before F2 is opened; if a 48-class ortho render is more than 2x the viz ortho ratio, F2 waits for a fix. |
| Bundle size growth objection | Expected +5-6 KB raw / +1-2 KB gz on 3.95 MB / 1.08 MB; measured and stated in F2. |
| Ortho routes look different from the user's Windows `dot` | Documented tie-break story (D7) in F2 with the montage against Linux-reference behaviour; geometry tests do not pin ties. |
| `label=` under ortho produces overlaps that users report before G lands | F2's body says so and links G; G is opened the day F2 merges. |
| Ports approximation (H) draws criticism | Stated as an approximation with the upgrade path; the probe corpus shows it beats today's "through the box" routing. |
| Graphviz licensing of test inputs | Structure re-expressed as `.puml`, no `.dot` text copied. |
| Maintainer's `.ctoj` override scheme | New functions carry `@Original` keys computed the same way (path + name + hash of the C definition) so plantuml/smetana tooling keeps working; F1's body explains it. |
| Session/tooling limits during execution | Each PR is small enough to finish in one sitting except F1, which is split into seven compilable commits. |

---

## 12. Open questions for the user

- Q1: fold B (constructs) and C (drawing) into one "Smetana catches up with svek"
  PR? Separate is recommended (different reviewers' mental models: DOT attributes vs
  Java drawing code).
- Q2: ortho as two PRs (F1 unwired + F2) or one? Two recommended (F1 reviewable as
  pure translation with oracles; F2 a 200-line diff plus fixtures). If the
  maintainer objects to dead code landing first, squash to one.
- Q3: edge emission order (3.5 `edge-emission-order`, section 2 of the attr audit):
  mirroring `Cluster.printCluster1`/`Bibliotekon.lines0` ordering would move
  siblings left/right on many diagrams. It is "parity" but reads as regression to
  anyone whose Smetana layout they liked. Recommended: leave out; mention in the
  issue as a known difference with the #1703 ordering complaint attached.
- Q4: Smetana's cluster `margin=16` (20 for shaped symbols) vs Graphviz's 8: keep
  (recommended, deliberate maker code) or align?
- Q5: add class-ortho rows to `tools/perf-bench` corpus? Touching the corpus resets
  band history per its README; recommended: no, keep perf evidence in the PR bodies.
- Q6: shapes beyond ellipse (diamond/octagon/hexagon, section 3.3): include in B as
  a stretch (~40 lines of descriptors + `poly_init` regular/peripheries blocks) or
  leave for later? Recommended: later, low visibility.
- Q7: legacy activity swimlanes (`rank=min/max` + point shape): the probe could not
  even produce a valid diagram with the legacy syntax; recommended non-goal unless
  a reporter shows a working case.

---

## 13. Non-goals (stated in the issue so they read as restraint)
- Byte-identical output against any `dot` binary (D7).
- Porting `htmltable.c` (D5), `concentrate`, `newrank`, `ordering`,
  `compound/lhead/ltail`, `splines=line|curved`, compass corner ports,
  `labelangle/labeldistance`, `rankdir=BT|RL` (never emitted, 3.7).
- Changing Smetana's deliberate cluster margins (Q4) or edge emission order (Q3).
- Legacy activity swimlane rank sets and the `point` shape (Q7); shapes beyond
  ellipse/circle (Q6).
- Per-link linetype (#2471): PlantUML-side per-edge routing, whichever engine.
- ELK: orthogonal only, not part of the browser build, ignores positional hints (forum 15405); a different engine with its own differences, not a parity target.
- Kronikol changes (only the wiki note in section 10 once the pin moves).

---

## 14. Execution order and what "done" looks like

1. User review of this plan; answer Q1-Q7.
2. Generate evidence for the issue (Appendix C: montages for the 3.1-3.5 keys,
   density chart, #1703 render), push to `pr-assets`, post the issue (Appendix A),
   apply labels.
3. PR A (same day as the issue: it is the smallest and it answers #1703).
4. PR B, PR C, PR E in that order, each after the previous merges (or in parallel
   branches if the maintainer is slow, rebased as they land).
5. PR F1 (seven commits), then F2 the day F1 merges, then G.
6. PR D, PR H.
7. Tick the issue checklist as each merges; when all nine are in, post the final
   comparison montage set and the remaining known differences (section 13) and
   leave the issue to the maintainer to close.
8. Update the Kronikol wiki note; save memory notes (upstream status, the
   `SvgGeometry` helper, the fallback log line, the `drand48` reference choice).

Done = every 3.1-3.5 key either matches the dot path on its probe (geometry
invariant test + montage) or is documented in 13; `skinparam linetype ortho` and
`polyline` work under `!pragma layout smetana` on the JVM and in the browser build
with zero WebAssembly; the Vega suite, the Ant job and the browser checks are green.

---

## Appendix A: issue draft

Image URLs are placeholders until Appendix C has run; `<issue>` in the PR drafts
becomes the real number. Every thread number cited below is listed with its source
in section 2.3.

Title: `Smetana: bring the layout engine level with Graphviz for what PlantUML actually uses (starting with linetype ortho)`

Labels after posting: `smetana`, `enhancement`.

---

I have been running Smetana a lot lately (it is what the browser build uses when there is no Graphviz WASM on the page, and what servers without Graphviz get) and kept a list of the places where the same diagram comes out different from the Graphviz path. Most of the list is small stuff; ortho is not. This is the list with evidence, and a set of PRs that work through it. I have triage here, so I can keep the checklist current as things land.

The port itself is a huge piece of work, and most of what follows is a one-line omission on the PlantUML side rather than anything wrong in the engine.

**Is your feature request related to a problem? Please describe.**

`skinparam linetype ortho` does nothing under Smetana. That has been the forum answer since 2022 (15893: "orthogonal line has not been implemented in smetana") and it is still true on master: the Smetana maker never sends `splines` to the engine, and the engine has no orthogonal router (Graphviz's `lib/ortho` was never translated and `_dot_splines` has no `ET_ORTHO` branch). `linetype polyline` is ignored too: the maker never sends it and three branches of the polyline code are still stubs. And the `splines` attribute cannot even be parsed by the port today (`edgeType()` is a stub), which is the first thing to fix for both.

![class diagram with linetype ortho: Smetana ignores it, Graphviz routes right angles](https://raw.githubusercontent.com/lemonlion/plantuml/pr-assets/smetana-parity/issue/class-ortho.png)

To see what else differs I rendered 59 small diagrams through both engines on the same jar, one directive per diagram with an identical control, and diffed the geometry (comments and source-line attributes stripped first). Besides linetype, Smetana currently ignores:

- `skinparam nodesep` and `skinparam ranksep`, and the `searchsize` the Graphviz path sets on every render. The Graphviz path never goes below 35px between nodes and 60px between ranks (20/40 for activity diagrams) and widens both for edge labels; Smetana runs on Graphviz's raw defaults of 18px/36px. That is the "denser layout" of #1703: on this corpus Smetana's canvas area is 0.79 of Graphviz's at the median. (searchsize is 500 on the Graphviz path and 30 in the port; a candidate for the ordering half of #1703, not verified.)
- `-[norank]->`. The Graphviz path sends `constraint=false`; the Smetana maker does not, and the two lines of the port that act on it are stubs, so it would throw if it did.
- `together { }`. No cluster is created, and the members drift apart.
- `skinparam groupInheritance`. `sametail` is never set and `dot_sameports` is a stub.
- `skinparam sameClassWidth`. Prints "NOT YET IMPLEMENTED" on stderr.
- usecase ovals. Arrows stop at the bounding box because every node goes out as `shape=box`, although the port already has `ellipse`.
- component `portin`/`portout`, `A::member --> B`, and entry or exit points linked from outside. Both ports land on the bottom edge and the incoming link crosses the component; member links leave the class centre; entry and exit points get snapped into place at draw time, which hides a route that still aims at the box. The Graphviz path expresses all of these with HTML label ports, which the port cannot parse.
- cardinality and role labels get no shield, and overprint the arrows on dense diagrams.
- on the drawing side (`SmetanaEdge` versus `SvekEdge`): middle decorations like `-(0)-`, label direction arrows (`: label >`), qualified associations (`A [key] --> B`), multi-colour arrows (`-[#red;#blue]->`), `ArrowHeadColor`, a self-link on a package (drawn inside it), `constraint on links : {xor}` spots, the `arrow.cardinality` style, labels that stay clickable under `[[url]]`, the post-layout label collision pass, and the SVG comment and `data-source-line` metadata on edges.

![six of the smaller gaps side by side: nodesep, norank, together, groupInheritance, usecase ovals, component ports](https://raw.githubusercontent.com/lemonlion/plantuml/pr-assets/smetana-parity/issue/small-gaps.png)

A few things I expected to be gaps are not: `-[hidden]->`, direction hints, `minClassWidth`, crow's feet, lollipops and `[[url]]` all match, and entry and exit points look right for the reason above. `{rank=same}` is dead code on the Graphviz path as well (`useRankSame()` always returns false), so there is nothing to do there.

**Describe the solution you'd like**

Work through the list in small PRs, each reviewable on its own, each with tests and (where there is anything to look at) before/after images, all tracked here:

- [ ] Spacing: send nodesep, ranksep and searchsize from the Smetana maker the way `DotStringFactory` does, through one shared helper. This changes about 80 Vega reference renders, spacing only.
- [ ] Constructs: `constraint=false` (plus the port lines), `together` clusters, ellipse and circle shapes, sameClassWidth.
- [ ] Drawing: the `SmetanaEdge` items above.
- [ ] linetype polyline: port `edgeType()` and translate the two `ET_PLINE` branches of `dotsplines` PlantUML can reach (the third, `make_flat_bottom_edges`, needs bottom-side ports the maker never sets).
- [ ] linetype ortho, part 1: port `lib/ortho` (trapezoid, partition, sgraph, fPQ, maze, ortho, rawgraph) from 2.38 in the existing `gen/` idiom, with the Graphviz fixes since 2.38 folded in (the chkSgraph crashes, the trapezoid table overflow, the eqEndSeg typo, error returns instead of longjmp, the 16.1.0 invalid-trapezoid failure path). Unit tests with geometric oracles, no behaviour change yet.
- [ ] linetype ortho, part 2: the `ET_ORTHO` branch, `setEdgeLabelPos`, the maker switch, a Vega corpus and a browser check.
- [ ] ortho labels: `xlabel` plus `forcelabels` under ortho like the Graphviz path, and the label-node alignment pass shared between the two engines.
- [ ] groupInheritance: port `sameport.c` and run the sametail logic in the maker.
- [ ] ports and shields: move edge endpoints to the port rectangles after layout and reserve shield room, without porting the HTML label parser.

Two rules I want to hold across all of them: no exception objects on the routing path (in the TeaVM build exception construction was most of Smetana's render time, see #2866), and tests that assert geometry invariants rather than byte-compare against a dot binary (dot's own ortho tie-breaks already differ between Linux and Windows: `drand48` versus `rand()`, and a stable versus an unstable sort).

**Describe alternatives you've considered**

- ELK. Orthogonal only, not part of the browser build, and it does not honour positional hints (forum 15405).
- A hand-written orthogonal router on top of dot's ranks. About 700 lines against 3,200 of real port code, but it would not match Graphviz on any non-trivial diagram: dot's ortho ignores ranks and routes Manhattan shortest paths through the free space between nodes with congestion weights, and every "Smetana looks different from dot" report would be right. Snapping polylines into staircases has the same problem plus dog-legs at every rank boundary and nothing keeping parallel edges apart.
- Porting Graphviz main instead of 2.38. Same algorithm, but its C leans on `util/list.h`, `gv_alloc`, `OPTIONAL()` and friends that the port does not have, and every other translated file is 2.38-shaped. Main is the review reference and the source of the fixes.

**Additional context**

How the numbers were made: master jar, Graphviz 16.1.0, each probe rendered with `-Playout=smetana` and with `-graphvizdot` (without the flag PlantUML quietly falls back to Smetana, which had an early run of mine showing everything honoured), SVG comments and source-line attributes stripped before hashing. The montages are Smetana on / Graphviz on / Smetana off / Graphviz off. Each PR adds its probes as Vega fixtures, and all of this ends up reproducible in the repo.

Rendering cost today, for scale (warm medians on the JVM, the same 24-class diagram): Smetana <n> ms, PlantUML calling dot <n> ms. The ortho PRs will carry the same table with ortho added on both sides, and the browser ratio.

Related: #1703 (density and ordering), forum 15893, #939 and #345 (groupInheritance with ortho), #1058 (labels on port edges), #1443 and #1441 (nested ports), #2471 (per-link linetype, which needs PlantUML-side per-edge routing whichever engine does the layout).

---

First comment after posting (not in the body): "@The-Lum this overlaps with your port and nesting lists (#1443, #1441, #1068, #1236); if any of yours belongs in this list, say so and I will add it. @Vampire the spacing PR re-renders your Spock diagram, image in the PR."

---

## Appendix B: PR body drafts

Every body ends with `Tracked in #<issue>.` Image paths are under
`https://raw.githubusercontent.com/lemonlion/plantuml/pr-assets/smetana-parity/<pr-key>/`.
Before posting any of them, fill the placeholders listed at the end of this
appendix and make sure every image named exists on `pr-assets`.

### B.1 PR A: `Smetana: send nodesep, ranksep and searchsize like the Graphviz path does`

The Smetana maker sets `margin` and `rankdir` on the root graph and nothing else, so the port runs on Graphviz's built-in 0.25in/0.5in separation. `DotStringFactory` never goes below 35px/60px (20/40 for activity), widens both for edge labels, and lets `skinparam nodesep`/`ranksep` override. This PR moves that computation into one helper used by both makers and sends the result to the port, plus `searchsize=500`, which the Graphviz path sends on every render.

With that, `skinparam nodesep`/`ranksep` work under Smetana, and the default node and rank spacing match the Graphviz path (cluster margins stay as they are; that is deliberate maker code). It is the "denser layout" half of #1703; here is the reporter's diagram before, after, and through dot:

![Spock docs diagram: Smetana before, Smetana after, Graphviz](https://raw.githubusercontent.com/lemonlion/plantuml/pr-assets/smetana-parity/a/issue-1703.png)

![nodesep 120 and ranksep 150: before, after, Graphviz](https://raw.githubusercontent.com/lemonlion/plantuml/pr-assets/smetana-parity/a/nodesep-ranksep.png)

Over the 59-diagram probe corpus the median canvas-area ratio Smetana/Graphviz goes from 0.79 to <n>.

About 80 of the Vega fixtures go through Smetana (VegaTest forces it) and all of their reference renders change, spacing only. I went through the diff; every change is a translation of existing shapes or a larger viewBox, no rank order or crossing changes. The re-baseline is its own commit so the code change can be reviewed apart from it.

Tests: `SmetanaSeparationTest` renders a 4-class diagram with and without `skinparam nodesep 120` / `ranksep 150` and measures the gaps out of the SVG (within 2px of the request; defaults at least 35/60), and a unit test on the helper pins that both makers compute the same values for the same links. Nothing in the perf ladder moved (attributes only).

How I checked: `./gradlew test`, `ant -noinput` on JDK 8, the browser-test checks on the TeaVM build, and the montage script over the probe corpus with `-Playout=smetana` and `-graphvizdot`.

Docs this touches: plantuml.com/layout-engines says Smetana "tends to make slightly straighter arrows" (the spacing part of that changes), and the deployment-diagram appendix examples with `skinparam nodesep 5` / `10` start working under Smetana.

Tracked in #<issue>.

### B.2 PR B: `Smetana: honour norank, together, oval shapes and sameClassWidth`

Four things the Graphviz path sends and Smetana did not, all maker-side except a few lines in `dot_init_edge`:

- `-[norank]->` now sets `constraint=false` on the edge. The port's `nonconstraint_edge` was already translated, but the lines in `dot_init_edge` that act on it (`xpenalty=0`, `weight=0`) were still `UNSUPPORTED`, so the attribute would have thrown; they are real code now, and the `group` branch next to them for the same reason.
- `together { }` members go into an unlabelled `cluster<id>t<n>` subgraph exactly as `Cluster.printTogether` does, and the port lays them out as one small cluster and keeps them side by side (the together probe confirms it).
- usecases, requirements and Chen attributes go out as `shape=ellipse` (circles as an ellipse with square dimensions), and arrows now clip on the oval rather than its bounding box. The port already has the ellipse shape (`Globals.Shapes`); the maker sent `box` for everything.
- `skinparam sameClassWidth` does what the Graphviz path does rather than printing "NOT YET IMPLEMENTED".

![norank, together, oval clipping, sameClassWidth: before, after, Graphviz](https://raw.githubusercontent.com/lemonlion/plantuml/pr-assets/smetana-parity/b/constructs.png)

Tests: one JUnit per item over the probe diagrams (C sits below B with norank; A and D share a rank under together; arrow tips within 1px of the ellipse; equal class widths and no stderr), plus Vega fixtures under `nonreg/group<issue>`.

How I checked: as in the spacing PR.

Docs this touches: the Layout section of plantuml.com/layout-engines lists `together` and `norank` without saying they were Graphviz-only; now they are not.

Tracked in #<issue>.

### B.3 PR C: `Smetana: draw what SvekEdge draws`

`SmetanaEdge` is a cut-down copy of `SvekEdge` and a good number of drawing steps never made it across. This PR brings them over, and where the code was a verbatim copy (arrow direction, rainbow extremities, middle decorations) it now lives in one helper both classes call:

- `skinparam ArrowHeadColor` (the value was computed and then never used)
- middle decorations `-(0)-`, `-0)-`, `-(0-` and the Chen subset/superset ones
- label direction arrows (`: label >`), which also means `getArrowDirection` no longer throws "refactor in progress"
- qualified associations `A [key] --> B` (the qualifier text never got drawn)
- multi-colour arrows `-[#red;#blue]->`
- a self-link on a package, which was drawn on the package's hidden core node inside it instead of on the border
- `constraint on links : {xor}`
- the `arrow.cardinality` style, and labels that stay clickable under `[[url]]` (the URL group was closed before the labels were drawn)
- the post-layout collision pass that pushes cardinality labels off neighbouring nodes
- the `<!--link ...-->` comment and `data-source-line` on edges, which tooling reads

![arrowhead colour, middle decorations, magic arrows, qualifiers, rainbow arrows, package self-link: before, after, Graphviz](https://raw.githubusercontent.com/lemonlion/plantuml/pr-assets/smetana-parity/c/drawing.png)

Tests: a JUnit per item asserting on the SVG (fill colour of the arrowhead, ellipse and polygon counts, the qualifier text is present, two paths for two colours, the loop's box lies outside the package, the link comment exists), plus Vega fixtures.

How I checked: as in the spacing PR.

Tracked in #<issue>.

### B.4 PR E: `Smetana: skinparam linetype polyline`

Two halves. In the port: `edgeType()`, the parser for the `splines` attribute, was a whole stub, so any `splines` value threw before layout; it is translated from 2.38 `utils.c` now. Two of the three `ET_PLINE` sites in `dotsplines` that were still `UNSUPPORTED` (the multi-rank straight-run loop in `make_regular_edge` and the ten-line branch of `makeSimpleFlat`) are translated, as is `polylineMidpoint` in `splines.c`; `routepolylines` itself had been ported already and was in use for flat edges and the last segment of regular ones. The third site, `make_flat_bottom_edges`, stays a stub with a comment: it is only reachable with bottom-side ports, which the maker never sets. In the maker: `splines=polyline` goes out when `skinparam linetype polyline` is set, through the same switch the ortho PR extends.

![class, state and left-to-right class diagrams with linetype polyline: before, after, Graphviz](https://raw.githubusercontent.com/lemonlion/plantuml/pr-assets/smetana-parity/e/polyline.png)

Where dot's polyline output is identical to its spline output (the component, usecase and object probes, whose edges are already straight), Smetana's is too; the tests pin both directions.

Tests: `SmetanaPolylineTest` parses every path and checks the cubic control points coincide with their endpoints (straight segments), that the render differs from the spline render where Graphviz's does and is identical where Graphviz's is, a five-rank chain to exercise the straight-run loop, an adjacent flat pair for `makeSimpleFlat`, and Vega fixtures.

Perf: the JVM ladder (10/24/48 classes) is <n>/<n>/<n> ms with polyline against <n>/<n>/<n> with splines.

How I checked: `./gradlew test`, `ant -noinput` on JDK 8, the browser-test checks on the TeaVM build, and the perf ladder.

Docs this touches: the `linetype polyline` line on plantuml.com/layout-engines no longer needs an unstated Graphviz-only caveat.

Tracked in #<issue>.

### B.5 PR F1: `Smetana: port Graphviz lib/ortho (not wired yet)`

This is the orthogonal router from Graphviz 2.38 (`lib/ortho`: trapezoid, partition, rawgraph, sgraph, fPQ, maze, ortho) translated into `gen/lib/ortho` in the same idiom as the rest of the port: `Globals zz` threaded through, `@Original` on every function, `CArray`/`CArrayOfStar` for the C arrays, `CFunction` comparators, `h/ST_*` classes for the twenty-odd new C structs, and `UNSUPPORTED` markers only where I deliberately did not translate (the debug and PostScript dumping code, `pointset`, `intset`, `PQcheck`). The `@Original` keys are computed the same way as the existing ones (path, name and a hash of the C definition), so the `.ctoj` override scheme in plantuml/smetana still lines up.

Nothing calls it yet. `_dot_splines` is untouched, so no render changes and the Vega suite is identical; the next PR wires `ET_ORTHO` and the `skinparam`. Splitting it this way keeps this diff reviewable as a translation with its own tests.

What is different from 2.38 on purpose, all taken from Graphviz main and its changelog:

- the `chkSgraph` crash class (GV#14 #1408 #1658 #1990): epsilon compares in `traverse_polygon` and the `hcmpid`/`vcmpid` comparators, and a per-gcell side list in `mkMazeGraph` (2.38 shares one array sized by node count and overflows it)
- trapezoid and query-node tables grow on demand (GV#56 #1880)
- the `eqEndSeg` typo `S2l2=!T2` (GV#2047)
- status codes instead of `setjmp/longjmp` and `assert` (GV#1801), including the 16.1.0 invalid-trapezoid failure path (GV#2784); there is no exception object anywhere in the router, which matters for the browser build
- `traverse_polygon` runs on an explicit stack (its recursion depth is about twenty frames per node)
- `htrack` rounds, `edgeLen` stays a double, channel adjacency is insertion-ordered, as in main
- glibc's `drand48` is reproduced bit for bit (the segment shuffle in `partition` depends on it; `java.util.Random` is not the same generator), so the Linux dot is the reference where equal-cost routes tie
- and the small ones: the negative `trnum` guard, `attachOrthoEdges` skipping routes with no segments, `decide_point` initialised explicitly, the `edgeLen` overflow fix, `edgecmp` without signed subtraction, double `updateWt`, and the dead `doLbls` label path dropped as main did

Tests (`src/test/java/test/sdot/ortho/`): the trapezoidation of one and two boxes against a hand-computed decomposition; `partition` of random non-overlapping boxes must tile the free space exactly (area sum, no overlaps, no rectangle overlaps a box); `top_sort` on small DAGs; `shortPath` against a brute-force Dijkstra on a grid; maze invariants (every search node has two cells, ordinary cells have at most four sides, `markSmall` flags a node thinner than a cell); `convertSPtoRoute` from a hand-built parent chain; the segment comparison truth table; `assignTracks` on crossing routes; the `drand48` sequence; the on-demand growth paths.

Size: about 4,800 lines including the scaffolding the convention asks for, in seven commits ordered bottom-up so each one compiles and carries its tests.

How I checked: `./gradlew test` (the new tests plus an unchanged Vega suite), `ant -noinput` on JDK 8, and the TeaVM build compiles with the new package.

Tracked in #<issue>.

### B.6 PR F2: `Smetana: skinparam linetype ortho`

With `lib/ortho` in the tree this wires it up: the `ET_ORTHO` branch in `_dot_splines` (with `resetRW`, which was a stub, and `setEdgeLabelPos`, which was missing), the `arrow_clip` guard that used to throw for every ortho edge, `ND_alg` widened so a node can carry its maze cell, and the maker sending `splines=ortho` when `skinparam linetype ortho` is set. `SmetanaEdge` needed no change: the router emits one bezier per edge made of degenerate cubics and the existing path code handles those.

![class, state, usecase and deployment diagrams under linetype ortho: before, after, Graphviz](https://raw.githubusercontent.com/lemonlion/plantuml/pr-assets/smetana-parity/f2/ortho.png)

![left-to-right class diagram, package links and self loops under ortho: before, after, Graphviz](https://raw.githubusercontent.com/lemonlion/plantuml/pr-assets/smetana-parity/f2/ortho-2.png)

One thing this does not finish is labels. It lands with 2.38's `label=` behaviour: the label sits where the rank structure reserved room for it and a route may pass under it, the same as `dot -Gsplines=ortho` with plain labels. The Graphviz path switched ortho to `xlabel` in 2021 (forum 1608) and later added the label-node alignment pass (#2395); the next item on the checklist in #<issue> does the same for Smetana.

If the router fails on a graph (the cases Graphviz itself gives up on) the layout falls back to the spline router for that render and logs one line. The four `chkSgraph` crash graphs from Graphviz's regression suite render without falling back, and the GV#2784 graph takes the failure path cleanly instead of throwing.

Tests: `SmetanaOrthoTest` over the 21 ortho diagrams of the probe corpus checks every path segment is axis-aligned, endpoints sit on a node border, no segment passes through a node, the segment count on three hand-checked cases, and, when a `dot` is on the machine, the bounding box within 15% of Graphviz's (skipped on CI, which has no Graphviz); the fallback path; Vega fixtures for all 21; and `tools/browser-test/check-linetype.js`, which renders under `!pragma layout smetana` in headless Chromium with zero WebAssembly and checks the same axis-aligned property from the SVG DOM. Wired into `browser-test.yml`.

Perf, warm medians on a 10/24/48-class ladder: Smetana spline <n>/<n>/<n> ms, Smetana ortho <n>/<n>/<n> ms, PlantUML calling dot with ortho <n>/<n>/<n> ms. In the browser, ortho on Smetana relative to spline is <n>x, ortho on viz relative to spline is <n>x. TeaVM bundle: +<n> KB raw, +<n> KB gzip.

![render time ladder: Smetana spline, Smetana ortho, dot ortho](https://raw.githubusercontent.com/lemonlion/plantuml/pr-assets/smetana-parity/f2/perf-ladder.png)

How I checked: `./gradlew test`, `ant -noinput` on JDK 8, the TeaVM build plus all browser-test checks, the perf ladder on both runtimes, and the montage script over the ortho probes.

Docs this outdates: on plantuml.com/layout-engines, "tends to make slightly straighter arrows" is no longer true with `linetype ortho`, and any hint that ELK is the only way to get orthogonal edges without Graphviz can go.

Tracked in #<issue>.

### B.7 PR G: `Smetana: xlabel placement and label alignment for ortho edges`

The Graphviz path renders ortho edge labels as `xlabel` with `forcelabels=true` (since a 2021 beta, forum 1608) and then straightens edges through their label nodes (`alignEdgesAtLabelNodes`, #2395). Smetana could not do either: the `ED_xlabel` branches in `common_init_edge`, `addXLabels` and `map_edge` were empty blocks or `UNSUPPORTED`, and the alignment pass lived in `DotStringFactory`. This PR translates the xlabel branches (and the sliding loops in `xladjust` and the quadrant switch in `getintrsxi` that were silently skipped), has the maker send `xlabel` and `forcelabels` under ortho like `SvekEdge` does, and moves `alignEdgesAtLabelNodes` into a helper both engines call.

![ortho edge labels and cardinalities: after the previous PR, after this one, Graphviz](https://raw.githubusercontent.com/lemonlion/plantuml/pr-assets/smetana-parity/g/ortho-labels.png)

Tests: labels do not intersect nodes or each other on the label-heavy ortho diagrams and stay within 20px of their edge; a Smetana twin of `SvekEdgeOrthoLabelTest` (#2665); Vega fixtures updated for the ortho corpus.

How I checked: as in the ortho PR, plus the perf ladder (xlabel placement is an extra pass).

Tracked in #<issue>.

### B.8 PR D: `Smetana: skinparam groupInheritance`

`groupInheritance` works by setting `sametail` on the extends-like edges above the threshold and wrapping the parent so the merged arrowheads have room. Smetana never called that logic (it lives in `DotData`, which the Smetana maker does not build) and the port's `dot_sameports` was a stub that would have thrown if the attribute ever appeared. This PR ports `sameport.c` (no cdt in it, about 150 lines of C), extracts the sametail selection from `DotData` into a helper both makers use, and has the Smetana maker run it, send `sametail`, and wrap the parent in the same 20px protected image the Graphviz path uses.

![three subclasses with groupInheritance 2: before, after, Graphviz](https://raw.githubusercontent.com/lemonlion/plantuml/pr-assets/smetana-parity/d/groupinh.png)

Tests: one merged triangle and three edges sharing a tail point on the probe; no merge below the threshold; Vega fixtures.

How I checked: as in the spacing PR.

Docs this touches: `groupInheritance` on plantuml.com/class-diagram works under Smetana now.

Tracked in #<issue>.

### B.9 PR H: `Smetana: component ports, member links and label shields`

The Graphviz path expresses ports (`portin`/`portout`, `A::member --> B`, and entry or exit points linked from outside, whose draw-time snapping hides a route that still aims at the box) and label shields as HTML labels with `PORT` cells, which the port cannot parse and I did not want to port the HTML label engine for. The maker knows every port rectangle already, so after layout this PR moves each edge endpoint onto its port (or member row) on the side the Graphviz path would pick, exports port nodes first and last inside their cluster so they stay at the top and bottom, and reserves the 16px shield around nodes with cardinalities or roles the same way `SvekEdge` does, then clips the edge to the inner rectangle.

![component ports, member links and cardinality shields: before, after, Graphviz](https://raw.githubusercontent.com/lemonlion/plantuml/pr-assets/smetana-parity/h/ports.png)

This is an approximation: the router does not know about the port during layout, so a route may need a short corner near the node. On the probe corpus it beats today's output, where both ports sit on the bottom edge and the incoming link runs through the component. The real fix is `tailport`/`headport` in the port through `poly_port`, about 200 lines plus the compass and beginpath blocks that are still stubs; I have not done that here.

Tests: p1 on the top border and p2 on the bottom with no link crossing the component; member links start inside their row; cardinality text does not intersect its arrow; nested-port diagrams in the shape of #1443 and #1441 render without errors; Vega fixtures.

How I checked: as in the spacing PR.

Tracked in #<issue>.

### B.10 Before-posting checklist (placeholders and images)

- `<issue>`: the Appendix B preamble; the nine `Tracked in #<issue>.` lines; PR B's `nonreg/group<issue>`; PR F2's "the next item on the checklist in #<issue>". Also `<n>` in the issue's rendering-cost sentence. (`cluster<id>t<n>` in PR B is the real DOT subgraph naming from `Cluster:528`; leave it.)
- `<n>` values: issue (two JVM medians); PR A (the after-ratio); PR E (six ladder medians); PR F2 (nine ladder medians, two browser ratios, raw and gzip bundle deltas). Section 11's gate applies to the F2 numbers: if 48-class ortho is more than 2x the viz ortho ratio, F2 waits.
- Images on `pr-assets/smetana-parity/`: `issue/class-ortho.png`, `issue/small-gaps.png`, `a/issue-1703.png`, `a/nodesep-ranksep.png`, `b/constructs.png`, `c/drawing.png`, `e/polyline.png`, `f2/ortho.png`, `f2/ortho-2.png`, `f2/perf-ladder.png`, `g/ortho-labels.png`, `d/groupinh.png`, `h/ports.png` (Appendix C step 3b composes the multi-probe ones).
- Names that must match the code as merged: the separation helper and its test (PR A), `SmetanaSeparationTest`, `SmetanaPolylineTest`, `SmetanaOrthoTest`, `check-linetype.js`, the `test/sdot/ortho` package.
- Run the 9.4 lint on each body.

---

## Appendix C: evidence generation recipe

All paths relative to the scratchpad unless stated. `J_MASTER` = a jar built from
`origin/master`, `J_PR` = the PR branch jar, `DOT` = `gv16/Graphviz-16.1.0-win64/bin/dot.exe`
(Windows path via `cygpath -w`). Without `-graphvizdot` PlantUML falls back to
Smetana silently, so every dot render passes it explicitly.

```
# 1. Probe corpus (already generated; regenerate after any .puml edit)
bash wf1/jvm-probe/gen.sh            # writes puml/X.puml and puml/X-off.puml
bash wf1/jvm-probe/render.sh $J_PR    # out/smetana/*.svg|png, out/dot/*.svg|png, out/err/*
bash wf1/jvm-probe/compare2.sh        # order-insensitive geometry fingerprint table

# 2. 2x2 montages (Smetana on | dot on ; Smetana off | dot off) at 2x
java wf1/jvm-probe/Montage.java out montage 2

# 3. Three-panel before/after/dot per probe for a PR body
for p in class-ortho state-ortho lr-class-ortho ortho-packagelinks ortho-selfloop usecase-ortho deployment-ortho; do
  java -Djava.awt.headless=true -jar $J_MASTER -Playout=smetana -tpng -pipe < puml/$p.puml > tri/$p-before.png
  java -Djava.awt.headless=true -jar $J_PR     -Playout=smetana -tpng -pipe < puml/$p.puml > tri/$p-after.png
  java -Djava.awt.headless=true -jar $J_PR -graphvizdot "$DOT" -tpng -pipe < puml/$p.puml > tri/$p-dot.png
done
java wf1/jvm-probe/Montage.java --triple tri montage-tri 2   # (add a --triple mode: three columns, one row per probe)

# 3b. Composites named in the PR bodies (one PNG, one probe per row, three columns), ~1200px wide
#     (add a --compose mode to Montage.java: takes the output name and the probe list)
java wf1/jvm-probe/Montage.java --compose f2/ortho.png     class-ortho state-ortho usecase-ortho deployment-ortho
java wf1/jvm-probe/Montage.java --compose f2/ortho-2.png   lr-class-ortho ortho-packagelinks ortho-selfloop
java wf1/jvm-probe/Montage.java --compose a/nodesep-ranksep.png nodesep-120 ranksep-100
java wf1/jvm-probe/Montage.java --compose b/constructs.png norank together oval-clip sameclasswidth
java wf1/jvm-probe/Montage.java --compose c/drawing.png    arrowhead middle magic kal rainbow2 pkgself   # attr-audit probes
java wf1/jvm-probe/Montage.java --compose e/polyline.png   class-polyline state-polyline lr-class-polyline
java wf1/jvm-probe/Montage.java --compose g/ortho-labels.png ortho-edgelabels ortho-lr-quantifiers state-ortho
java wf1/jvm-probe/Montage.java --compose d/groupinh.png   groupinh
java wf1/jvm-probe/Montage.java --compose h/ports.png      ports-component memberport quantifier ports-state
java wf1/jvm-probe/Montage.java --compose issue/small-gaps.png --grid 3x2 nodesep-120 norank together groupinh oval-clip ports-component   # 2x2 montages tiled
cp montage/class-ortho.png issue/class-ortho.png
# a/issue-1703.png: render the #1703 reporter's Spock diagram (issue attachment) the same three ways

# 4. JVM perf ladder (Bench.java: 10/24/48 classes, 30 warm reps, medians)
java Bench.java --jar $J_PR --ladder 10,24,48 --modes smetana-spline,smetana-polyline,smetana-ortho --reps 30 > perf/jvm-pr.json
java Bench.java --jar $J_PR --dot "$DOT" --ladder 10,24,48 --modes dot-ortho --reps 30 > perf/jvm-dot.json

# 5. Browser ratio ladder (bench-ladder.js in Kronikol tools/render-bench, per-rep alternation)
node C:/Code/Kronikol/tools/render-bench/bench-ladder.js --engine build/npm-plantuml/plantuml.js --reference core-1.2026.8beta1-0e4f452.js --modes "pragma+ortho,pragma+spline,viz+ortho" --reps 32 > perf/browser.json

# 6. Charts: JSON -> evidence.html -> PNG (Playwright, deviceScaleFactor 2)
node shoot.js perf/evidence.html f2/perf-ladder.png

# 7. Density ratio (PR A): analyse.py over out/smetana and out/dot for before and after
python wf1/jvm-probe/analyse.py --density out-before out-dot > density-before.txt
python wf1/jvm-probe/analyse.py --density out-after  out-dot > density-after.txt

# 8. Publish
cd /c/Code/plantuml && git worktree add ../plantuml-pr-assets pr-assets
mkdir -p ../plantuml-pr-assets/smetana-parity && cp -r issue a b c e f2 g d h ../plantuml-pr-assets/smetana-parity/
cd ../plantuml-pr-assets && git add -A && git commit -m "smetana parity evidence" && git push fork pr-assets
```

Prose lint before posting any body:
`grep -nP '[\x{2014}\x{2013}\x{201C}\x{201D}\x{2018}\x{2019}]|\b(delve|leverage|robust|seamless|comprehensive|crucial|ensure)\b|[\x{1F300}-\x{1FAFF}]' body.md`
(run under `LC_ALL=C.UTF-8`; grep refuses `-P` with Unicode classes in a C locale).

---

## Appendix D: source material index

- Audit findings (49 attribute-level, 31 port-level, with file:line evidence on both
  sides): `tasks/wf1b-result.json` (`result.audits.attrAudit.findings`,
  `result.audits.portAudit.findings`), readable dump `tasks/wf1b-majors.md`.
- Probe corpus and classification: `scratchpad/wf1/jvm-probe/probes.md` (52-row
  table + fidelity notes F1-F8), `puml/`, `out/`, `montage/` (59 PNGs), `analyse.py`,
  `Montage.java`, `compare.sh`, `compare2.sh`, `render.sh`, `gen.sh`.
- Drawing-side probes: `scratchpad/wf1/attr-audit/probe/{puml,out}` (arrowhead,
  assocnote, diamond, folder, kal, legacyact, legacypart, legacyswim, magic,
  memberport, middle, pkgself, quantifier, rainbow, rainbow2, sameclasswidth,
  selfloop) and `NOTES.md`.
- Browser probe: `scratchpad/wf1/browser-probe/` (`browser-probe.js`, 16 variants,
  `out/`, `out-cold-*`).
- Ortho feasibility study (line-by-line inventory of the seven C files, struct
  mapping, support code, fixes, strategies, effort, risks):
  `scratchpad/wf1/ortho/design.md`.
- History (issues, forum, docs, Graphviz fixes, people): `scratchpad/wf1/history/notes.md`
  and `result.audits.history` in `wf1b-result.json`.
- Graphviz sources: `scratchpad/gv238/` (2.38.0 `lib/ortho`, `lib/common`,
  `lib/dotgen`), `scratchpad/gvmaster/` (main `ortho.c`, `maze.c`, `sgraph.c`,
  `fPQ.c`, `partition.c`, `rawgraph.c`, `dotsplines.c`, `CHANGELOG.md`,
  `test_regression.py`, `h/`).
- First probe corpus and the geometry normaliser: `scratchpad/probe/` (`gen-probes.sh`,
  `compare.sh`, `out/`, `png/`).
- Portable dot: `scratchpad/gv16/Graphviz-16.1.0-win64/bin/dot.exe`.
- Jar under test: `C:/Code/plantuml/build/libs/plantuml-1.2026.8beta1.jar`
  (master `dea3374` at build time); browser engine
  `C:/Code/Kronikol/tools/render-bench/core-1.2026.8beta1-0e4f452.js`;
  `viz-global.js` = Viz.js 3.24.0 / Graphviz 14.1.1.
- Related Kronikol plans: `plans/SMETANA_PERF_PLAN.md` (the exception-cost story
  behind D6, the bench-ladder methodology), `plans/PERF_CI_PLAN.md` (upstream CI
  house style, perf-bench), memory notes `plantuml-smetana-browser`,
  `plantuml-upstream-collab`, `no-llm-tells-in-public-text`.

**Durable copy (2026-09-14):** the session scratchpad lives under `%TEMP%` and may not
survive, so the reusable kit was copied to `C:/Code/plantuml-plans/smetana-parity/`
(`jvm-probe/` corpus + montages + scripts, `attr-audit-probe/`, `browser-probe/`,
`ortho/design.md`, `history/notes.md`, `audit-json/`, `first-probe/`; 341 files, 3.5 MB).
Not copied (re-download if needed): the Graphviz 2.38 and main sources (`gv238/`,
`gvmaster/`) and the portable dot 16.1.0 (`gv16/`).
