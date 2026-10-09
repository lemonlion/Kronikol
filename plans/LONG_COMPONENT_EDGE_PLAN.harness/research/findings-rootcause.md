# Agent "Root-cause engine recursion" result (2026-10-09). Evidence in scratchpad/rootcause/

## Cause: TeaVM's java.util.regex port recurses once per character for a bracket class under + or *, driven by PlantUML's Labels class
- Proved by capturing the RangeError's own stack (Error.stackTraceLimit = Infinity, hook in an engine copy) and by counting regex nesting per match. node 25.9; Chromium 147 spot check.
- Cycle, unobfuscated upstream master build: jur_GroupQuantifierSet_matches -> jur_CompositeRangeSet_matches -> jur_SupplRangeSet_matches -> (repeat), each via $rt_wrapFunction3. npm 1.2026.8 obfuscated: Epg -> BKE.oR -> DJJ -> Z2.oR -> EXv -> AGU.oR (bodies compared).
- 6 JS frames = 3 Java frames per label character; nesting exactly 3 x input + 5 (1,136 / 2,336 / 4,736 for 377 / 777 / 1,577 chars).
- Stack cost ~920 bytes/char optimizers off (linear 200-1,600 KB, ~17 KB base); ~280 bytes/char JIT on.
- TeaVM (0.14.1; byte-identical in 0.16.0): TGroupQuantifierSet.java:58 -> TCompositeRangeSet.java:135 -> TSupplRangeSet.java:131. TPattern.processQuantifier (TPattern.java:653,667) gives any non-TLeafSet term a recursive TGroupQuantifierSet. Every bracket class becomes such a term: TCharClass ctor calls setNegative (TAbstractCharClass.java:154) marking every class supplementary-capable; hasLowHighSurrogates() (:88-91) true for negated classes AND positive ones (a `-1 < 2048` bug) -> processRangeSet (TPattern.java:1203-1210) builds TCompositeRangeSet.
- PlantUML: Labels.java:62 -> init :80-81 (BOTH_LABELS `^[%g]([^%g]+)[%g]...`); quoted label also :87-88; unquoted label and C4 (Rel macro emits label unquoted) :94-95; via Matcher2.java:74. Callers CommandLinkElement.java:309 (description/component), CommandLinkClass.java:343 (class). UBrex, preprocessor, creole NOT involved; link command's own regex stays at 105 levels at any length.

## Why a sequence message is fine
- CommandArrow captures with `(.*)` (CommandArrow.java:132) -> TDotQuantifierSet (a loop); message goes to Display (:348), no Labels pass; 800-char message peaks at 80 levels.
- A `[[...]]` link recurses in CommandCreoleUrl / UrlBuilder (`[^\[\]]*`, 3 levels/char): SAME TeaVM defect = what MaxLinkedLabelChars works around.

## Upstream / JVM
- PlantUML master 57a3d2848 (Oct 6) same Labels patterns, TeaVM 0.16.0; an Oct 1 master build fails at the same place/length (451 vs 450).
- TeaVM master commit 846242b3d (Oct 4, unreleased) fixes only the positive-class bug; negated classes + processQuantifier unchanged (read, not built).
- No existing report in either tracker.
- JVM (OpenJDK 25, -Xss256k): the three patterns handle 1,000,000-char labels without overflow; control `(?:a|[^g])+` overflows at 500 (test detects recursion). PlantUML 1.2026.8beta1 jar renders 20,000-char component edges and class links at that stack. => TeaVM-build artifact only (like the block-opener and coloured-bar limits per PSL doc; INFERRED they may be the same defect, untested).

## Longest statement that draws, node --stack-size=400, npm 1.2026.8, fresh process per probe
| shape | opt off | JIT on |
| (a) caller --> warehouse : "..." | 450 | 1,431 |
| (b) unquoted | 452 | 1,432 |
| (c) -[#E74C3C]-> | 460 | 1,440 |
| (d) wrapped/one line | 452/450 | 1,431/1,431 |
| real ClickHouse / only \n / only commas | 450/451/450 | 1,430/1,433/1,432 |
| quote chars inside label | >=4,000 | >=4,000 |
| (e) sequence message | 2,000 (PlantUML syntax limit) | 2,000 |
| sequence [[#link ...]] | 433 | 795 |
| (f) class link | 438 | 806 |
| (g) C4 Rel (stdlib local) | 450 | 1,285 |
| (h) 200-char aliases | 837 | 1,779 |
- Opt off: every Labels shape fails at the same LABEL length, 426-427 chars. Plain edge linear: 228 / 450 / 898 / 1,786 at 200 / 400 / 800 / 1,600 KB.
- Commas, \n, wrapping: no difference. Only quote chars (", “, ”, U+E121) shorten the run (run = chars between quotes).
- Aliases matched by a different regex at 3 levels/char; depths do NOT add -> LABEL length is the exact proxy; a whole-statement cap is conservative.
- JIT on: edge moves with warm-up (class link 806 vs component 1,431) -> cap must come from the opt-off figure.
- Chromium 147 main thread same flags: 440 draws, 450 fails, same cycle.

## Candidate upstream fixes (both prototyped in memory on the unobfuscated build)
- TeaVM: in processQuantifier give TSupplRangeSet/TCompositeRangeSet an iterative greedy quantifier (like OpenJDK CharPropertyGreedy: walk forward over code points, then try the rest from the longest match down); reluctant/possessive need the same; optionally flag only negated classes as supplementary. 30-diagram corpus byte-identical (loop ran 207,314 times, no fallbacks); edges drew at 30,000+; linked label then stops only at the 2,000 message limit (fixes links too).
- PlantUML: Labels.init runs each pattern only when quotes sit where anchors need them, or parse with indexOf/UBrex (one-or-more is a loop). Ten Labels cases byte-identical; edges 30,000+. Does NOT fix [[...]] links.

## Not established: numbers inside a Chromium worker (main thread only); TeaVM master built/run. Opt-off edges vary ~ +-2 chars.
Scripts: harness.js, gen.js, probe.js, bisect.js, stack.js, instr.js, patches.js, corpus-hash.js, svgdump.js, chromium-check.js, jvm/RegexDepth.java; stacks oct-plain-1600.stack.txt, npm-plain-1600.stack.txt; instr-oct-*.txt; results-*.jsonl; corpus-*.json, svg-*.json; chromium-*.txt; jvm/*.txt; upstream-plantuml/, upstream-teavm/ partial clones.

## FOLLOW-UP (other JS-only limits): same TeaVM defect, a SECOND cycle; JVM fine at 100,000 chars -Xss256k for all
- Cycle 2 (lazy over dot): TReluctantGroupQuantifierSet.matches (TReluctantGroupQuantifierSet.java:55-57) <-> TDotSet.matches (TDotSet.java:72), 4 JS / 2 Java frames per char; TeaVM builds it for every `*?`/`+?` over the dot (TPattern.java:673-676); greedy `.*` gets an iterative class. npm names ESH/E2u.
- Block opener: CommandGrouping COMMENT `(.*?)` (CommandGrouping.java:72, master :78) via SingleLineCommand2.isValid -> Matcher2.find inside getCandidate; RangeError SWALLOWED there -> picture says "Syntax Error?".
- Coloured bar: CommandCreoleColorChange.matchingSize (:77-80), pattern `^(\<color[\s:]+(#[0-9a-fA-F]{1,6}|#?\w+)[%s]*\>(.*?)\</color\>)` (:58), reached from creole parser while Teoz builds the note tile; without </color> the lazy group walks the rest of the line (adding the closing tag would not help). Bar without the tag never runs it (~16,000).
- Longest that draws (unobfuscated master; first = node default stack JIT on, second = 400 KB opt-off):
  | | no fix | Labels fix | TeaVM greedy fix | greedy + lazy fix |
  | loop | 1,968/658 | 1,970/658 | 1,968/655 | 16,366/16,366 (then "Diagram too large", 98,308 px) |
  | coloured bar | 2,007/692 | 2,007/689 | 2,003/691 | >=30,000/>=30,000 |
  | sequence [[#iflow...]] | 2,000*/448 | 2,000*/447 | 2,000*/2,000* | 2,000*/2,000* |
  | component edge | 1,993/451 | >=30,000 | >=30,000 | >=30,000 |
  (* = PlantUML's own 2,000 message limit, a Syntax Error.) Lazy fix = second prototype, iterative `.*?` over dot. Both TeaVM fixes, and separately the Labels fix: 35 renders byte-identical.
- Other statements (npm 1.2026.8, 400 KB, opt-off), longest that draws / where / cycle:
  | sequence `participant "<name>" as x` | 435 (a `database` in a sequence diagram 432) | CommandParticipantA FULL `[%g]([^%g]+)[%g]` (:58) | greedy, 3 frames/char |
  | component `database "<name>" as x` | 648 | CommandCreateElementFull `[%g].+?[%g]` (:128, :130) | lazy |
  | Kronikol's wrapped `rectangle "**...**\n<size:10>[Software System]</size>" ... <<system>>` | 647 | same | lazy |
  | same rectangle, one unbroken name | 330 | CreoleStripeSimpleParser `^(\*+)([^*]+(?:[^*]|\*\*[^*]+\*\*)*)$` (:69, :120) | ~5 frames/char (bold display line starts with * so creole list pattern runs; Kronikol's 80-char name wrap avoids it) |
  | activity `:<text> (12ms);` | 653 | CommandActivity3 `(.*?)` (:70) | lazy |
  | title | >=4,000 | none | |
- With both TeaVM fixes every row draws >= 4,000 except unbroken bold name (324): its `(?:...|...)*` alternation still recurses (OpenJDK avoids, TeaVM not). Every regex named is unchanged on upstream master.
- Files: results-fu-*.jsonl, stack-oct-{loop,colourbar}-800.txt, stacks-other.txt, instr-oct-{loop,colourbar,other}.txt, corpus2-*.json, jvm/regexdepth2-*.txt, patches.js, run-matrix.sh
