# V4 plan: TestRunReport.json size, two default flips, the monospace control removed

**Written:** 2026-09-29, at 3.34.1 (`a8abc614`), first as `JSON_SIZE_PLAN.md` and made the v4 plan the same day.
**Status: green-lit 2026-09-29** (the owner: "implement V4_PLAN.md in full"); executing from R1, each release
recorded in §7. **Decided by the owner on 2026-09-29:** the scope in §0 (`ROADMAP.md` D30), compression in place
(D7), the JSON keeps its indentation (Q1; dropping it is a maybe for v5), R6 drops what no arrow links (Q4),
the internal-flow track proceeds on in-repo evidence without waiting for the reporter (Q5), and 4.0.0 is
published once its gate holds. Evidence labels as elsewhere: **RUN** (measured here), **READ** (in the source, `file:line`), **INFERRED**.

**About the name.** Until 2026-09-29 `V4_PLAN.md` was the plan for Mermaid CI summaries and removing Server and
Local rendering. That plan is now [`V5_PLAN.md`](V5_PLAN.md). A plan written before that date that names
`V4_PLAN.md`, or puts Mermaid CI summaries, the Server and Local removal, toolbar Option C, the `--kron-*` tokens or
the internal-flow options no report reads "in v4" or "at 4.0.0", means v5.

## 0. Scope

4.0.0 is four breaking changes and a wiki page:

1. #85's payload compression, in place, on by default.
2. Headers hidden in notes by default.
3. Notes in YAML by default.
4. The opt-in monospace note control removed.
5. A "Migrating to v4" wiki page.

The rest of this plan ships before it, in 3.x: the relaxed encoder, the #85 option, the internal-flow size fixes
(#87, #86 and the segments no arrow links) and one preparation minor. Everything else the old v4 held is v5
(`V5_PLAN.md`, `ROADMAP.md` stages 11 and 12): Mermaid CI summaries; the Server, Local, IKVM and C4 removal; the
five internal-flow options no report reads; toolbar Option C with `--kron-*` as the theming contract; the
toggle-default unification; and, as maybes, numeric HTTP status in the data file, the dead `.examples-*` CSS
rules and dropping the JSON's indentation.

Why this is a major: #85 reaches a default report only when its default flips, and flipping a default is a major
(`ROADMAP.md` rule 7). The owner made the size of `TestRunReport.json` the temporary top priority, so the major
that flips it comes first.

## 1. What the file is made of

Two BreakfastProvider reports, measured with scripts that print sizes only (**RUN**,
[`V4_PLAN.harness/`](V4_PLAN.harness/README.md)):

| | xUnit lane, 3.32.4 | Docker lane, 3.27.1 |
|---|---:|---:|
| Scenarios | 203 | 202 |
| File as written | 6,579,291 | 18,972,302 |
| `diagrams` strings (PlantUML source) | 1,379,374 (21.0%) | 5,801,245 (30.6%) |
| `content` strings (bodies) | 779,301 (11.8%) | 5,334,111 (28.1%) |
| `\uXXXX` escapes in the file | 154,392, of which `\u0022` 141,470 | 888,319, of which `\u0022` 869,298 |
| Line ends | CRLF (87,993) | LF |
| The same data, indent 2, quotes as `\"`, LF | 5,862,812 (89.1%) | 15,401,997 (81.2%) |
| Same, no indentation | 4,488,524 (68.2%) | 14,189,497 (74.8%) |
| #85 option 1 on the indent-2 form | 4,252,419 (64.6%) | 5,915,462 (31.2%) |
| #85 option 1, no indentation | 2,860,529 (43.5%) | 4,683,838 (24.7%) |

Four findings follow.

- **F1. A fifth of the Docker report is escaped quotes.** The writer serializes with the default encoder and
  `WriteIndented = true` (READ, `ReportGenerator.cs:4467`, `:4497`), and the default encoder writes every `"`
  inside a string as `\u0022` (six bytes where JSON needs two), and `<`, `>`, `'`, `&`, `+` as six-byte
  escapes. JSON bodies and PlantUML are full of quotes. The relaxed encoder, which `PlantUmlCreator.cs:1538` and
  `TrackingDbDataReader.cs:158` already use, writes the same JSON value in fewer bytes: 7.0% to 18.8% on seven
  BreakfastProvider lanes, most of them 8% to 10%, the Docker lane 18.8% (**RUN**, `encoder_saving.py`, which
  counts every escape the relaxed encoder would not write). The row "indent 2, quotes as `\"`" is a second model of
  it, which also drops CRLF, and it matches the written file to within 77 bytes once the escapes and CRLF are
  added back. **RUN** on .NET 10: the relaxed encoder writes `\"` and leaves `<`, `&`, `'`, `+` and `✓` as they
  are. No roadmap item or issue had this.
- **F2. Indentation is a fifth of what is left** (indent 2 against none: 1,374,288 bytes on the xUnit report,
  8% to 30% after R1 across the seven lanes), but a file without line breaks is one line, and `grep` on it prints
  the whole file: the failure mode the agent-facing docs exist to prevent. **Decided (Q1): kept in v4; a maybe for
  v5.**
- **F3. #85 is the large lever, and it reaches a default report only at 4.0.0.** With R1 under it, it takes the
  Docker report to 31% of today's size and the xUnit one to 65% (indent kept): 38% and 73% of R1's.
- **F4. #86, #87 and the segments no arrow links shrink the JSON only when `GenerateMergeableData` is on.** A
  default report carries no internal-flow segments; the mergeable file adds `internalFlowSegments` (READ,
  `ReportGenerator.cs:488`, `:4711`). #86 and #87 were measured on the HTML's copy of the map. No mergeable report
  with segments was on this machine to measure.

## 2. The releases

| # | Release | Bump | Shrinks | Blocked on |
|---|---|---|---|---|
| R0 | The measuring harness, `V4_PLAN.harness/` | none | nothing | done with this plan |
| R1 | `TestRunReport.json` written with the relaxed encoder (F1) | patch | every report, by default: 7.0% to 18.8% | nothing |
| R2 | #85: payload fields as `$z`, in place, behind an option (off) | minor | reports that opt in: to 38% (Docker) and 73% (xUnit) of R1's size | §3.2's design points |
| R3 | #87: a popup holds only its own request's spans | patch | mergeable reports' segments (and the HTML) | a plan for it; both causes fixed (§3.3, Q5) |
| R4 | Re-measure #86 on reports written after R3 | none | nothing | a synthetic suite that runs requests concurrently (§3.3, Q5) |
| R5 | #86: each distinct flow stored once, if R4 supports it | patch | mergeable reports | R4's numbers |
| R6 | Segments no drawn arrow links (the blob plan's leftover) | patch | mergeable reports with collapse, the cap, `Skip` variants or override blocks | nothing (Q4: drop) |
| R7 | Preparation: `--headers shown\|hidden` on `kronikol ingest`, and `[Obsolete]` on the monospace members | minor | nothing | nothing |
| R8 | **4.0.0:** #85 on, headers hidden, YAML notes, the monospace control removed, "Migrating to v4" | **major** | every report: R2's figures | R1, R2 and R7 released, and R2 run on BreakfastProvider |

The order:

- **R1 first:** the only change that shrinks every report without a major, and small. It must precede R2: R2
  proves its option-off output byte-identical, and that proof should be taken against R1's bytes rather than be
  broken by R1 a release later. And with the default encoder, base64's `+` is written `\u002B`, which inflates
  every `$z` string by about 7.8% (READ, `InternalFlowHtmlGenerator.cs:65-66` notes the same); after R1 it is not
  escaped.
- **R2 next,** with R3 to R6 beside it in a separate worktree once R1 is on `main`. The two tracks share little code
  (R2 is the JSON writer, `Query/`, `Merge/` and the skill scripts; R3 to R6 are `InternalFlow/`). Both touch
  `ReportGenerator.cs`: merge, do not overlap edits.
- **R3 before R4 and R5,** by rule 4: pooled spans make N concurrent requests' segments carry the same pooled flow,
  which is exactly the duplication #86 counts, so #86 measured before R3 measures #87.
- **R6 whenever there is a gap:** with Q4's recommended "drop" it depends on nothing.
- **R7 any time before R8.**
- **R8 waits for R1, R2 and R7, not for R3 to R6.** Those are patches with no default to flip; whatever of them is
  not out by 4.0.0 ships in 4.x. (Q5 took the reporter's reports off R4's path, so all of them can precede it.)

## 3. Blockers and decisions

### 3.1 R1

None. Points to settle in the release itself: every writer of the file moves together, the standard one
(`ReportGenerator.cs:4465-4498`), the mergeable one (`:4649-4730`) and merge's `MergeableReportRenderer.Serialize`
(`:138-166`), with ingest following the standard one; the relaxed encoder is unsafe only for text embedded in
HTML, and no reader found in §3.2 embeds this file's text in a page (the segment element has its own encoder,
`InternalFlowHtmlGenerator.cs:57-72`); the XML and YAML writers are separate (READ, `ReportGenerator.cs:4910`,
`:5173`) and out of scope; it is a patch by the repo's rule (performance, the same JSON value), and the changelog
says the bytes change; `b:` addresses do not move, because the index hashes the decoded string (READ,
`ReportScanner.cs:798-803`); every reader is a standard JSON parser. No golden pin of the file exists, so R1 adds
one (it becomes R2's option-off baseline). Kronikol4J writes the 3.0.4x shape of this file with its own writer,
so R1 is a ledger entry, not a port (READ, `Kronikol4J/docs/REMAINING_PARITY.md:1877`).

### 3.2 R2 (#85)

- **D7, taken 2026-09-29: in place.** The file stays JSON.
- **The index cannot seek past a bare `{"$z"}`.** #85 says `ReportIndex` needs no change; it does. The scanner
  records a hash and a length for every body at scan time and indexes string tokens only (READ,
  `ReportScanner.cs:644-686`, `:749-750`), so a wrapper holding only `$z` either makes every verb inflate every
  body (`summary`, `ctrf` and `history gate` included) or loses the body. The wrapper has to carry the decoded
  hash and length itself, under `$`-prefixed keys (an older scanner reads a plain key such as `type` or `name`
  inside an interaction as the interaction's own field, READ `:555-570`).
- **Version gating.** `ReportGate` accepts `formatVersion` equal to 1 only (READ, `ReportGate.cs:42`), and merge
  likewise (`MergeableReportReader.cs:62`); the constant is shared with XML and YAML (`ReportGenerator.cs:36`).
  A compressed file must declare a new version, so that an older tool refuses it rather than answering wrong:
  without the bump an older `query` reports "body: none" and a false "not in bodies", and an older `merge`
  crashes on `GetString()` (READ, `MergeableReportReader.cs:89`). Readers accept both values; an uncompressed file
  keeps writing 1.
- **`query.py` in the field.** Both copies load the file with `json.load` and have no version gate (READ,
  `templates/skills/.../query.py:36-52`), so a copy already in a consumer's repository crashes on a compressed
  report. The new copy can inflate with the standard library; old copies cannot be reached. Under R2 only those
  who opt in meet it; at 4.0.0 everyone does (§3.4).
- **Smaller points for the plan of R2:** gzip's header carries an OS byte, so tests compare decoded text; a size
  threshold (only large bodies compressed) means the schema can allow the wrapper but not require it; `kronikol
  merge` and `kronikol ingest` have no way to choose the encoding (READ, `MergeableReportRenderer.cs:138-166`,
  `IngestCommand.cs:273-291`); `internalFlowSegments` is left alone in R2 (R3 and R5 reshape it, and merge would
  have to inflate it before `WrapSegmentData`).
- **There is no plan file for #85.** R2 starts by writing one (its design is in the issue body and above).

### 3.3 R3 to R6

- **How a segment picks its spans today** (READ, `InternalFlowSegmentBuilder.cs:26-92`, `:173-186`): per call,
  the spans of the call's W3C trace that start between the request (less 50 ms) and its own response. Nothing
  reads span parentage, although one path records an exact join key: with no ambient `Activity`, the handler
  injects a `traceparent` with a fresh span id, stores that id as the log's `activitySpanId`, and the server span's
  parent is that id (READ, `TestTrackingMessageHandler.cs:161-169`).
- **#87 has two possible causes, and they need different fixes.** (a) A call that records no trace id takes every
  span of the run in its window, from every concurrent test (READ, `InternalFlowSegmentBuilder.cs:81`,
  `:180-181`). (b) Calls or tests sharing one trace id through an ambient `Activity` are separated by the window
  alone (READ, `TestTrackingMessageHandler.cs:156-160`). Minted per-call traces cannot produce #87's 547 spans.
  **Blocker: which one produced the reporter's popup.** `kronikol query trace` on that report flags a trace shared
  by several scenarios (READ, `QueryCommand.Trace.cs:97-104`) and settles it without opening the file. Without that
  report, R3 reproduces and fixes both.
- **Nothing in the repo exercises it.** No test or example makes concurrent calls. The xUnit example lanes run
  serially; the TUnit lanes run in parallel but nothing asserts on their segments. The one concurrency fact
  separates two different traces (READ, `InternalFlowSegmentBuilderTests.cs:795`). R3 writes the reproduction
  first: #87's own suggestion, two scenarios calling one endpoint at once with a known, different number of
  queries each, once per cause.
- **#87 has no plan, so R3 starts by writing one.** It decides: selection by the call's own span subtree where the
  join key exists; an anchor for calls with no trace id other than "every span of the run"; the whole-test flow
  (`BuildWholeTestSegments`, `:128-148`), which has no window at all; keeping the rule that the handler never
  creates or changes `Activity.Current` (pinned, `TestTrackingMessageHandlerTests.cs:1765`, `:1778`). Four builder
  facts turn red on purpose (`InternalFlowSegmentBuilderTests.cs:423`, `:442`, `:675`, `:828`).
- **R3 decides what R5 would remove.** Today's byte-identical copies come from several calls on one trace
  selecting the same spans in overlapping windows (READ, `InternalFlowSegmentBuilder.cs:79-92`). Select by subtree
  and most of them go, which is why R4 re-measures before R5 is sized.
- **R4 needs reports from a suite where the problem shows.** The mergeable JSON's map and the HTML element are one
  map rendered twice (READ, `ReportGenerator.cs:329`; equality pinned by
  `InternalFlowSegmentMapReportTests.cs:127`), so R4 can measure the decoded HTML element and needs no mergeable
  report. But #86's and #87's numbers come from the reporter's ClickHouse and BigQuery lanes at 3.20.0, and no
  suite on this machine runs requests concurrently. **Blocker: fresh reports from the reporter's suite, written
  after R3.** Without them, R5 is sized on a synthetic parallel suite, which rule 4 treats as weaker evidence.
- **R5's constraints:** flow keys must be content hashes, because merge keeps the first value per key across
  shards (READ, `MergeableReportMerger.cs:376-388`); shards written before R5 must still render
  (`internal-flow-popup-script.js:95-104`); Kronikol4J mirrors the popup script, so a ledger entry.
- **R6 is one filter, and it covers more than the blob plan's leftover names.** The segment builder never sees
  what the diagram drew (READ, `ReportGenerator.cs:307-312`, `:329`). Keeping only the keys the shown diagrams link
  (`InternalFlowHtmlGenerator.LinkedIds`), after the diagrams exist and before both `BuildSegmentData` calls
  (`ReportGenerator.cs:409-421`, `:4596-4608`), covers collapse and the cap, and also calls a `Skip` phase variant
  hides, calls inside an override block and tests the page does not show. Its gain on BreakfastProvider is small:
  3 unlinked segments of 1,296 on its xUnit lane at 3.31.4 (READ, release commit `81f77b58`). "Give the run's arrow
  the whole run's spans" instead needs the collapser to export run membership, which `SequenceCollapser.cs:24`
  discards, and it needs R3's selection first.

### 3.4 R7 and 4.0.0

- **#85 on by default reaches everyone.** Every report 4.0.0 writes is compressed. `query.py` copies already in
  consumers' repositories crash on it, so the migration page tells them to refresh those copies (`kronikol
  init-agents`, or the template's copy). A `kronikol` older than R2 refuses the file by its `formatVersion`, and
  the page names the version to upgrade to. `kronikol merge` takes 3.x and 4.0.0 shards together (R2 reads both
  forms). `kronikol ingest` writes the compressed form by default. The history action's `VERSION` installs a tool
  from R2 on by then, since it moves with every release. Kronikol4J writes its own older shape and is not
  affected.
- **Headers hidden.** The option already exists: `ReportToggleDefaults.HeadersShown` (READ,
  `ReportToggleDefaults.cs:24`), whose baseline is `true` (`ReportToggleDefaultsResolver.cs:16`), and the toolbar
  buttons and the script's start state already follow it (`ReportGenerator.cs:983`, `:1723`;
  `DiagramContextMenu.cs:134`). What the old plan's phase 4a asked for is therefore down to three things: flip the
  baseline; add `--headers` to ingest (R7; ingest has `--note-format` and no `--headers`, READ
  `IngestCommand.cs:122`); and prove the initial hidden render end to end. Tests that assume headers shown pin
  `TestRunReportToggleDefaults.HeadersShown = true` in their fixtures, so toolbar Option B's first step
  (`ROADMAP.md` 6.2) still migrates the `[data-shown]` tests only once, as the roadmap wanted.
- **YAML notes.** Flip `ReportConfigurationOptions.NotePayloadFormat` (READ, `ReportConfigurationOptions.cs:482`),
  the resolver's baseline (`ReportToggleDefaultsResolver.cs:20`) and the mirrored parameter defaults
  (`ReportGenerator.cs:1052`, `:1147`, and the others the old phase 4b listed, in `V5_PLAN.md`'s history, checked
  again). The script's `|| 'json'` fallbacks stay: "unset means JSON" carries notes that cannot be YAML. The
  parameterless `GetCollapsibleNotesScript()` overload follows the new default or goes (the old plan's question 5).
- **The monospace control.** What goes: `ReportConfigurationOptions.ShowNoteFontControls` (READ, `:511`),
  `ReportToggleDefaults.NoteFont` (`ReportToggleDefaults.cs:51`), the `NoteFontFamily` enum (`NoteAppearance.cs:17`),
  the resolver's two fields (`ReportToggleDefaultsResolver.cs:21`, `:30`), the script's `kronNoteMono` class and
  controls (`collapsible-notes-script.js:1640`, `:2207`), the two script tokens (`DiagramContextMenu.cs:131-132`)
  and the toolbar markup (`ReportGenerator.cs:1338-1347`). The note width control stays; it shares
  `NoteAppearance.cs`. R7 marks the public members `[Obsolete]` first: the roadmap's rule is no major that removes
  API without a deprecation release behind it (`ROADMAP.md` 11.1).
- **Report output.** R1, R2, the three flips and the monospace removal each change report bytes: a Kronikol4J
  divergence-ledger entry each (`Kronikol4J/docs/REMAINING_PARITY.md:1877`).

### 3.5 Questions for the owner

- **Q4.** R6: drop every segment no shown diagram links (recommended: smaller, one filter, independent), or give
  a collapsed run's arrow the whole run's spans (more information; after R3, and its title must say "×N" or it
  reads like #87). **Taken 2026-09-29: drop.**
- **Q5.** Whether the reporter of #85 to #87 can run `kronikol query trace` on the #87 report, and rerun that
  suite after R3 (§3.3). Both are the internal-flow track's only outside dependencies. **Taken 2026-09-29: do not
  wait.** R3 reproduces and fixes both causes in the repo; R4 measures a synthetic concurrent suite and R5 is sized
  on it, and the record says that is the weaker evidence rule 4 names.

Decided on 2026-09-29: Q1, the indentation stays (a maybe for v5); Q2, D7 in place; Q3, whether #85's default
flips before v4, is answered by making that flip v4; Q4 and Q5 as above.

## 4. How each release is proved

Every release follows the repo's TDD rule: a fact red on the release before it, for its own reason, before the
change.

- **R0:** done. Every later release takes its before and after numbers from the harness, run on the same
  BreakfastProvider lane.
- **R1:** a byte pin of a rich generated report (the corpus of `TestRunReportSchemaContractTests`, with
  `kronikolVersion` normalised); a fact that a body holding quotes, `<`, `+` and non-ASCII round-trips through
  `kronikol query body` with the same `b:` address as on 3.34.1; the size measured on BreakfastProvider.
- **R2:** option-off output byte-identical to R1's pin; every verb that reads payload text (`http --body`,
  `body`, `diff`, `values`, `interactions --where`, `grep` in all modes, `note`, `diagram`, the SQL hint in
  `failures`) answers the same on a compressed and an uncompressed copy of one run; an older-version refusal; merge
  of a compressed shard; the schema validator on both forms; `query.py` agreement (`FallbackScriptTests`).
- **R3:** the test #87 suggests, once per cause: two scenarios hitting one endpoint concurrently with a known,
  different number of queries each; each popup holds only its own.
- **R4 and R5:** #86's own script on the decoded map, before and after.
- **R6:** a scenario with each option on: no segment without an arrow (or the arrow's segment holds the run).
- **R7:** `IngestCommandTests` parse `--headers` (and `--note-format`, if it still has no parsing fact); the
  monospace members still work and carry the attribute.
- **R8:** each flipped default pinned, the old default tests turned round; an end-to-end fact for the first render
  with headers hidden and one with YAML notes; a report written with no options is compressed, and every
  payload-reading verb answers as on its uncompressed copy; a 3.x shard merged with a 4.0.0 shard; no monospace
  control on a generated page.

## 5. The "Migrating to v4" page

- **Compression:** what changed and the option that turns it off (named in R2); refresh `query.py`; the lowest
  `kronikol` that reads the files.
- **Headers:** `TestRunReportToggleDefaults.HeadersShown = true` restores them (`SpecificationsToggleDefaults` too
  where it is set on its own); `kronikol ingest --headers shown`.
- **YAML notes:** `NotePayloadFormat = NotePayloadFormat.Json`; `kronikol ingest --note-format json`.
- **Monospace control:** the removed members, with no replacement.

With it at 4.0.0, as for any release: every package at 4.0.0, a changelog with a breaking-changes section, the
template pins and the history action's `VERSION`, the wiki pages for the options that changed, and the Kronikol4J
ledger entries.

## 6. Not in this plan

- **v5** (`V5_PLAN.md`, `ROADMAP.md` stages 11 and 12): Mermaid CI summaries; the Server, Local, IKVM and C4
  removal; the five internal-flow options no report reads; toolbar Option C and `--kron-*`; the toggle-default
  unification; the maybes (numeric HTTP status, the `.examples-*` CSS, the JSON's indentation).
- Whole-file gzip (D7's other option) and a cached index, both argued in #85.
- Dropping `diagrams` from the JSON (the bodies are stored twice, once in `content` and once inside the diagram
  notes); `kronikol query diagram` and `note` read them. Recorded, not proposed.
- The XML and YAML data formats, and Kronikol4J's writer.
- One wiki sentence still says v4 where it now means v5 (`Internal-Flow-Tracking.md:198`, on the internal-flow
  options no report reads): changed with the next wiki push.

## 7. Execution log

Each release adds its entry here when it is tagged: what shipped, the numbers measured with the harness, and what
the release found that the plan did not say.

- **2026-09-29, the plan committed** (no bump) with the owner's answers to Q4 and Q5 and the green light.
- **R1, 3.34.2 (2026-09-29): the relaxed encoder.** Both writers of the file, `GenerateTestRunReportJson` and `GenerateMergeableReportJson` (which `kronikol merge` goes through too), share `TestRunReportJsonOptions`. Measured on BreakfastProvider's xUnit lane with a local build of the release: 6,461,200 bytes on 3.34.0 and 5,855,252 on R1, 9.4% smaller, 203 of 203 passing on both; `encoder_saving.py` predicted 601,532 bytes from the control's escapes (134,734 of them quotes) and the measured saving is 605,948. The byte pin (`tests/Kronikol.Tests/TestData/Reports/TestRunReport.pin.json`) is R2's option-off baseline. **Found on the way:** `kronikol query` printed a body, what `--out` writes and a `--path` object through the default encoder (`Zo\u00EB` for `Zoë`), so a search of its output for captured text missed; fixed in the same patch. The `--json` envelopes, `describe`, `Failures.jsonl`, the CTRF file and `Specifications.json` still use the default encoder, which is valid JSON every parser reads the same: left alone, no defect. `QueryStreamingTests`' allocation guard read 30% of its fixture against a bound of a third before R1 and 35% after, the file having shrunk around an unchanged 1.9 MB scan: its bodies are larger now. Published: CI 36618040868, Release 36619477881 (GitHub release 19:39 UTC), wiki `d85d2bb` (Generated Reports' escaping note, Internal-Flow-Tracking's v4 now v5), Kronikol4J ledger `35984c5`.
- **R2, 3.35.0 (2026-09-29): #85 behind `CompressTestRunReportPayloads`.** Designed in `PAYLOAD_COMPRESSION_PLAN.md` (§3.2's points taken as written: the wrapper carries `$h` and `$n`, a compressed file says `formatVersion` 2, merge keeps the shards' form, ingest has `--payloads`). The threshold is 512 characters plus "only if smaller", read off two lanes (`threshold.py`). BreakfastProvider's xUnit lane with the option on: 4,344,734 bytes, 74% of R1's run and 67% of 3.34.0's, which is §1's model (73%); this is R8's "R2 run on BreakfastProvider". Option off, the file is R1's pin byte for byte. Published: CI 36622777380, Release 36624540157, wiki `6f584a1`, Kronikol4J ledger `a255ee3`; #85 closed.
- **R3, 3.35.1 (2026-09-29): #87.** Designed in `SPAN_ATTRIBUTION_PLAN.md`: a span is a call's by its trace, then the span tree where a trace is shared by tests, then its time; a span calls of two tests would both take goes to neither, and the popup says how many it left out. Both of §3.3's causes are reproduced in the repo (Q5), at the builder and through the real handler. On BreakfastProvider's xUnit lane with a local build, 203 of 203 passing: 6 popups leave 12 spans out and 3 calls keep a popup that only says why; 1,294 popups show spans against 1,302 on 3.35.0, and 200 scenarios have a whole-test flow against 204 (two runs, so the counts also carry the runs' own differences). Building the segments costs what it did: 10 to 41 ms against 31 to 45 ms on 3.35.0, on a synthetic run of that size (1,600 calls, 9,400 spans). Found on the way: database trackers record no trace id (the rule's step 1 reads the tree for them), the note was lost under the default flame chart, `ShowMessage` popups have always shown their markup as text, and a span-store clear in a parallel collection failed a report fact about one run in three.
