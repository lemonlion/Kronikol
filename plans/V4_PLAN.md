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
- **R3, 3.35.1 (2026-09-29): #87.** Designed in `SPAN_ATTRIBUTION_PLAN.md`: a span is a call's by its trace, then the span tree where a trace is shared by tests, then its time; a span calls of two tests would both take goes to neither, and the popup says how many it left out. Both of §3.3's causes are reproduced in the repo (Q5), at the builder and through the real handler. On BreakfastProvider's xUnit lane with a local build, 203 of 203 passing: 6 popups leave 12 spans out and 3 calls keep a popup that only says why; 1,294 popups show spans against 1,302 on 3.35.0, and 200 scenarios have a whole-test flow against 204 (two runs, so the counts also carry the runs' own differences). Building the segments costs what it did: 10 to 41 ms against 31 to 45 ms on 3.35.0, on a synthetic run of that size (1,600 calls, 9,400 spans). Found on the way: database trackers record no trace id (the rule's step 1 reads the tree for them), the note was lost under the default flame chart, `ShowMessage` popups have always shown their markup as text, and a span-store clear in a parallel collection failed a report fact about one run in three. Published: CI 36631356709, Release 36632641499, wiki `ee36ec4`, Kronikol4J ledger `d44b793`. #87 is left for the owner to close: Q5 chose not to write to its reporter.
- **R4, no release (2026-09-29): #86 re-measured after R3.** On BreakfastProvider's TUnit lane, which runs its 203 scenarios in parallel, and its xUnit lane, which runs them a class at a time, each on local builds of 3.35.0 and 3.35.1, with #86's own method (`V4_PLAN.harness/dedup.py`: GUIDs normalised, each distinct `content` and `flameData` counted once). A real parallel suite in place of the synthetic one the roadmap row named: stronger than that, and still not the reporter's, so the weaker evidence rule 4 names. After R3, 58% of the parallel lane's `content` bytes and 42% of its `flameData` bytes are copies (2.3 copies of each distinct flow, up to 20 of one), and 54% and 36% on the serial lane (up to 34); on 3.35.0 they were 55% and 42%, 50% and 36%. R3 moved them little because BreakfastProvider's HTTP calls each get a trace of their own, so pooling was rare there; the copies are nested calls of one test showing the same spans, which R3 keeps by design. Stored once, the map decodes to 49% of its size on the parallel lane and 53% on the serial one (811,559 bytes against 1,659,007; 1,285,919 against 2,418,029), while its gzip, the page's segment element, only falls to 78% and 79% (70,464 against 90,384; 120,328 against 152,528), about 1% of either page. That is #86's own case (memory and parse time after the first popup, and growth with nesting), not file size, and it holds: R5 goes ahead. The parallel lane leaves 498 drawn call arrows unlinked on 3.35.0 and 499 on 3.35.1: those calls captured no spans, which is not R3's doing.
- **R5, 3.35.2 (2026-09-29): #86.** Each flow that more than one segment shows is stored once: the first segment in the map that shows it keeps it, and each later one keeps its title and names that segment under `sameAs`; the popup follows the name. The activity diagram element in a popup is named for its flow (the SHA-256 of its PlantUML source) rather than its call, which was the one difference between two copies. **Not §3.3's design.** Content-hash keys, the first cut, stored each flow under a new `flow-` key at the end of the map: the decoded map fell by a third but the page's gzip grew (152,528 to 162,732 bytes on the xUnit lane), because a flow no longer sat beside the flows like it that gzip had compressed it against. Kept where it is first shown, the flow needs no new key at all, and a segment key is a call's id, which no other shard holds, so a merge keeps every copy a segment names without the constraint the content-hash keys were for. On BreakfastProvider's lanes, local builds: the decoded map 2,418,029 to 1,583,452 bytes on the xUnit lane and 1,659,007 to 1,039,491 on the TUnit lane, the page's gzip 152,528 to 145,616 and 90,384 to 84,848, 203 of 203 passing on both, the popups the same. Proofs: builder facts for the layout and the element id, merge facts (two stored shards, and one written before beside one written after), a pipeline fact on the page and the data file, a Playwright fact that each arrow of a stored flow opens it; seven guards broken one at a time were each caught (`V4_PLAN.harness/mutate_r5.py`, one through the Playwright fact). Found on the way: in the full Playwright run that fact found five segments holding "SELECT orders" where it expected one, because a page's map carries the segments of every test the process logged, another fixture's among them; it reads its own calls' keys now. R6 leaves a map only the segments its page links. Left for the owner: a flow's activity diagram is often the same for segments whose flame charts differ (their timings do), and storing the diagram apart from the flame data would take about another 9% off the segment element's gzip (`dedup.py`'s layout; written here first as the page's gzip, which it is not: see the 4.0.1 follow-up); that goes past #86, so it is not in this plan.
- **R6, 3.35.3 (2026-09-30): the segments no drawn arrow links are dropped (Q4).** One filter, `InternalFlowHtmlGenerator.LinkedSegments`, before both maps are built: the page keeps the segments the diagrams it embeds link (the component diagram among them), and the data file those its scenarios' diagrams link, so a merge renders what the run's page did. The has/hidden list does not change: it was already computed over the links. On BreakfastProvider's lanes, run on local builds, 3 of about 1,300 segments and 3 of about 820 were unlinked on 3.35.2 (4,101 and 3,735 decoded bytes), and none are on 3.35.3; in the repo's own report fixture, whose process had logged other tests, 19 of the page's 21 segments were. Two pipeline facts, one with a test the run does not name and one with an arrow cap of 1, were red on 3.35.2; four guards broken one at a time were each caught (`V4_PLAN.harness/mutate_r6.py`). The component diagram's links name relationship segments, which this map never held.
- **R7, 3.36.0 (2026-09-30): `kronikol ingest --headers shown|hidden`, and `[Obsolete]` on the monospace note control.** The flag sets `TestRunReportToggleDefaults.HeadersShown`; without it ingest keeps the library's default, which R8 moves to hidden. `ReportConfigurationOptions.ShowNoteFontControls`, `ReportToggleDefaults.NoteFont`, `ResolvedToggleDefaults.NoteFont`, `ResolvedToggleDefaults.ShowNoteFontControls` and the `NoteFontFamily` enum carry one message (removed in 4.0.0, the note width control stays) as a warning: they work as before, and the library's own reads of them sit inside a pragma. Proofs: an ingest fact on the flag's validation and on the page it writes (the script's start flag and every headers button), red with the flag parsed but not applied; a theory over the five members for the attribute and its message, red on 3.35.3; the members' existing facts, unit and Playwright, pass unchanged. `--note-format` already had its parsing fact. Report output changes only when the flag is given, and then to what the option always wrote, so there is no Kronikol4J entry. Found on the way: the changelog, this log and the roadmap dated R4 and 3.35.2 the day after they happened (2026-09-29); put right here.
- **R8, 4.0.0 (2026-09-30): #85 on by default, headers hidden, YAML notes, the monospace control removed.** `CompressTestRunReportPayloads` defaults to `true`, `NotePayloadFormat` and the built-in start states to YAML and headers hidden, and `kronikol ingest` follows the library unless a flag says otherwise; the five members R7 deprecated are gone with the glyph, the dropdowns, the two script tokens and `kronNoteMono`. On BreakfastProvider's xUnit lane, run on local builds with nothing configured, TestRunReport.json came to 4,345,421 bytes with 325 payloads compressed, where 3.34.2 wrote 5,855,252 and 3.34.0 6,461,200; the suite built and passed 203 of 203 with no change to its code, and the query.cs its run wrote read the file. Proofs: each flipped default pinned by a unit fact, and the facts that pinned the 3.x defaults turned round; a Playwright fact for the first render with nothing configured (headers hidden, notes as YAML) and one for a configuration that brings the 3.x start back; R2's verb theory now runs over a file written with the default option, and its merge fact merges a default 4.0.0 shard with a 3.x one; no monospace control on a generated page, and the members gone by reflection. Six mutations, each putting one 3.x default back, were each caught (`V4_PLAN.harness/mutate_r8.py`, one through Playwright). Facts written for the 3.x start pin it in their fixtures (`ReportTestHelper.ClassicStart`, 25 fixtures and three direct calls), as §3.4 asked; facts that read a diagram out of `TestRunReport.json` read it through a helper that inflates a compressed payload; facts named for the start state itself were turned round. Checked on the way: the YAML view of a note quoting a diagram puts `@startuml` and `@enduml` at the start of note lines, and every part still draws (a Playwright fact now holds it); a report that opens with such notes offers full and current source in the diagram menu, as a 3.x report did once a reader switched a note. Published on the Release run's second attempt (36652729813): the first failed on `ExportCommandTests`, where `HttpListener` on Linux binds its port again when it is disposed after `Stop()`, a race with a parallel test that 4.0.0 did not cause; fixed on main with a guard (`HttpListenerLifetimeTests`, 2a9ef7ae, no bump). 62 of 62 packages on NuGet; wiki "Migrating to v4"; Kronikol4J ledger entries.
- **Follow-up, 4.0.1 (2026-09-30): each distinct diagram and flame chart stored once, in a table.** R5's left-over, which the owner asked for on 2026-09-30 once it was measured over the published site rather than one page: on BreakfastProvider's 18 published 4.0.0 pages `dedup.py`'s layout came to 129,172 bytes of their segment elements and 8% of the maps a browser decodes, 0.25% of the pages, under 0.8% of their gzip and 1.0% to 1.7% of an in-memory lane's (R5's 9% is of the segment element). **Not the first cut.** Storing each diagram once where it is first shown, a later segment naming it under `contentAs` and keeping its flame chart, took the decoded map down 14% and 20% on the xUnit and TUnit lanes but the element's gzip only 2.3% and 2.1%: gzip had already been compressing the repeats. What `dedup.py` measured is the separation: diagrams beside diagrams and flame charts beside flame charts. Moving each flame chart to a key of its own made the gzip grow 11% and 12% (a call id per key), and `dedup.py`'s tables under fixed keys would collide in a merge, so the table ends the map under the first segment's key with a `~` before it, which no other shard holds: the holder of a flow keeps its title and names the table, its diagram's place and its flame chart's place, and `sameAs` stays for a whole flow repeated. Measured on the same two pages, that layout took the element 8.7% and 8.0%. On BreakfastProvider's xUnit and TUnit lanes, on local builds with 203 of 203 passing on both, the segment element came to 133,128 and 77,240 bytes against 144,884 and 83,856 for 4.0.0's layout of the same runs (8.1% and 7.9% less), and the map a browser decodes to 1,416,035 and 862,459 bytes against 1,577,081 and 1,035,395 (10.2% and 16.7% less); every segment resolved. Proofs: builder facts for the layout, the key order, the table and the round trip (every segment's title, diagram, flame chart and message come back through the names and places, a flow whose holder shares a diagram among them, and with the flame chart off), a merge fact with two shards' tables, a Playwright fact that each arrow opens the diagram with its own flame chart, one through a call whose flow's holder shares the diagram; eight guards broken one at a time were each caught (`V4_PLAN.harness/mutate_401.py`, three through the Playwright fact), and `V4_PLAN.harness/diagram_once.py` resolves every segment of a written page and measures it against 4.0.0's layout of the same run. Found on the way: a local `dotnet pack` of `release.slnf` with no build before it packed a Kronikol.dll left from an earlier build (the package was written before its net10.0 build finished), so the first local measurement ran 4.0.0's code; the published 4.0.0 carries the release commit's build, and a local pack now follows a build. **Published 2026-09-30:** the Release run (36702018936) passed on its first attempt and nuget.org lists all 62 packages; the wiki's Internal Flow Tracking page (`e400ad1`) and the Kronikol4J ledger (`1227a4d`) followed. BreakfastProvider moved to 4.0.1 in `fed4b46` (CI: Main 36703994950, 58 jobs, the 18 lanes' history gates passed). On the published xUnit, TUnit and Docker xUnit pages every segment resolves, and the segment element is 9.8%, 8.4% and 8.0% smaller than 4.0.0's layout of the same run (`diagram_once.py`); the external-SUT lanes hold no internal flow, their service running outside the tests' process. The six in-memory lanes' pages came to 0.4% to 0.7% smaller than on 4.0.0's run but BDDfy's, 0.3% larger, since the runs differ.
- **Measured after 4.0.0 reached the published site (2026-09-30): #85's default makes a report of small payloads larger once it is zipped.** BreakfastProvider moved to 4.0.0 in `9278137` (CI: Main 36685661344, 58 jobs, the 18 lanes' history gates passed with nothing flagged). Its 18 `TestRunReport.json` files came to 78,409,994 bytes against 157,444,729 on 3.34.0, half, but GitHub Pages serves them gzipped, and there they came to 15,614,536 against 16,853,002, 7% less, because 12 of the 18 grew: the six in-memory lanes' files by 20% to 38% and the six external-SUT lanes' by 41% to 70%, while the six Docker lanes', whose payloads are large, fell by up to 34% (ReqNRoll's by 0.2%). The runs' report artifacts, zips of the page and the file, grew 1% to 9% on the in-memory lanes and 10% to 12% on the external-SUT lanes, and fell 9% to 12% on five Docker lanes (ReqNRoll's grew 9%, a lane whose page varies from run to run); all 76 of the run's artifacts came to 0.6% less. A wrapper's base64 is gzip already, which a zip's deflate cannot shrink, and a wrapped payload no longer sits beside the payloads like it that deflate had compressed it against. R2's `threshold.py` chose 512 characters by the bytes the file saves, which is #85's own measure (its motivation is a directory of unzipped runs); nothing measured the zip. `V4_PLAN.harness/zip_cost.py` models the threshold from a written report by inflating its wrappers; its model of 512 comes within 2.1% of each file as written and within 1.3% of its deflate. On three xUnit lanes, against the same report with no payload wrapped:

  | Lane | Threshold | File | Deflate |
  |---|---|---:|---:|
  | In memory (local 4.0.1 build) | 512 (4.0.x) | -22% | +44% |
  | | 8,192 | -17% | +9% |
  | | 32,768 | -9% | 0% |
  | External SUT (published by 4.0.0) | 512 (4.0.x) | -26% | +78% |
  | | 8,192 | -18% | +9% |
  | | 32,768 | -13% | 0% |
  | Docker (published by 4.0.0) | 512 (4.0.x) | -62% | -24% |
  | | 8,192 | -61% | -33% |
  | | 32,768 | -59% | -35% |

  Wrapping only the diagrams is no answer (+38% and +79% on the two small lanes, +9% on Docker's): the diagrams are what compress best against each other. **Open for the owner:** raise `ReportPayloads.Threshold`, which is internal, so a patch whose report output changes (a Kronikol4J ledger entry): 8,192 gives up 1 to 8 points of the file's saving and takes most of the zip's growth back, and on a report of large payloads it keeps nearly all of the file's saving (61% against 62%) while its zip falls further (33% against 24%). Or leave it, since #85 asked for the file. Nothing is changed; Appendix C of the roadmap carries it. **Taken the same day as 4.0.2**, below.
- **Follow-up, 4.0.2 (2026-09-30): payloads compressed from 8,192 characters.** The owner took the recommendation above. `ReportPayloads.Threshold` goes from 512 to 8,192, and nothing else changes: the rule's second half (a payload stays text when its wrapper would not be smaller), the wrapper, `formatVersion` 2 and every reader. On BreakfastProvider's in-memory xUnit lane, on a local build with 203 of 203 passing and nothing configured, `TestRunReport.json` came to 4,603,856 bytes with 47 payloads compressed, 20% less than the same run with none compressed, and its deflate at level 6, what a zip spends, to 546,519 bytes, 8.5% more than with none; the lane's 4.0.1 run had come to 20% less and 44.6% more (4,278,126 bytes with 319 payloads compressed, deflate 653,779), and the run's own `query.cs` read the new file. The readers take a wrapper at any length, so the files 4.0.0 and 4.0.1 wrote read as they did; a fact now proves it on a file wrapped from 512 by hand (`CompressedReportQueryTests`), with the SQL hint of `failures` among its verbs, since that hint reads a statement of up to 8,192 characters and is the one reader with a length bound (two mutations, a copy that wraps nothing and a hint that skips short statements, were each caught). Proofs: the boundary fact moved to 8,191 and 8,192, red on 4.0.1; the facts that wrote payloads between 512 and 8,191 characters to see them compressed size them from the threshold now, and failed until they did. **Published 2026-09-30:** the Release run (36722490491) passed on its first attempt and nuget.org lists all 62 packages; the wiki (`26dd79a`) and the Kronikol4J ledger (`95c58f0`) followed. BreakfastProvider moved to 4.0.2 in `28dcc72` (CI: Main 36725314574, 58 jobs, the 18 lanes' history gates passed), its in-memory xUnit lane having passed 203 of 203 locally on the published packages. As GitHub Pages serves them, its 18 `TestRunReport.json` files came to 12,493,151 bytes against 14,852,605 on 4.0.1 and 16,853,002 on 3.34.0, smaller than on 4.0.1 on 17 lanes, by 11% to 41% (Docker ReqNRoll's run came out a fifth larger, and that lane varies from run to run); decoded they came to 82,033,346 bytes against 76,769,768 on 4.0.1 and 157,444,729 on 3.34.0. Against 3.34.0 each lane's file is 16% to 68% smaller decoded and between 43% smaller and 7% larger as served. The run's report artifacts came to 3% to 10% less than 4.0.1's on the same 17 lanes, and all 76 of its artifacts to 1.9% less than 4.0.1's and 6.3% less than 3.34.0's.
