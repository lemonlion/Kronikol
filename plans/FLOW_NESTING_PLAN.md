# `flow` shows which call ran inside which

**Date:** 2026-09-26 · **Repo version:** 3.29.6 in the shared checkout (`6c689b5e`); origin/main is 3.30.1
(`512bc85a`) and has changed no tool source since, so every cited source line holds at both ·
**Status: green-lit 2026-09-26, nothing implemented.** The owner took D22 as recommended: S2 goes ahead,
with rule R4 and Q1's `no response`. Roadmap items **1.13** (S1, a patch) and **10.0** (S2, a minor). §9 is the assumption ledger. The scripts behind every number
are in [`FLOW_NESTING_PLAN.harness/`](FLOW_NESTING_PLAN.harness/README.md), with their output.

The owner asked on 2026-09-25 whether `kronikol query` should translate a scenario's diagram into Mermaid,
since every LLM reads Mermaid natively. Measured, the answer was no (§8.1). The one thing a Mermaid
rendering carried that `flow` does not was where each response falls, which says which call ran inside
which. This plan puts that into `flow` and nowhere else. What it adds to the idea:

- **The obvious rule is wrong, and the data says how** (F2 to F4). "Nest a call under whatever call is
  still waiting for its answer" turns a service's parallel fan-out into a staircase four levels deep and
  hangs the test's own call under an unrelated background query. Matching the call's caller to the open
  call's service fixes both, and one more clause, the open call's trace, puts message deliveries where they
  belong. Over 7,183 real requests the chosen rule (R4) nests 60% of them and never lets the indentation
  point at the wrong parent.
- **Three defects in the same function** (F6 to F8), fixed first as their own patch: a filtered `flow` or a
  step address prints step headers with nothing under them, an annotation after a scenario's last call is
  never printed, and a dictionary is built and never read.
- **It is tool-only.** No report byte changes: no golden re-pin, no Kronikol4J ledger entry.

**Why it can land any time.** It needs no other stage. It touches one function of `kronikol query`, one
`VerbTable` line and both skill copies, so the only constraint is roadmap rule 5: no other track A stage
editing those files at the same time (§7.6).

---

## 0. Summary

`flow` prints one line per request in capture order, under step headers. It does not say that the calls
below a line were made while that line's call was still waiting for its answer. The report already holds
what is needed: every request and every response, in capture order, paired by `requestResponseId`.

Today, BreakfastProvider's ReqNRoll lane, `flow s26` (excerpt):

```
── 0  Given a pancake batch has been created
  s26/i0    Caller → Breakfast Provider  GET /milk  OK  15 ms
  s26/i1    Breakfast Provider → Cow Service  GET /milk  OK  14 ms
  s26/i4    Caller → Breakfast Provider  GET /eggs  OK  1 ms
  s26/i6    Caller → Breakfast Provider  GET /flour  OK  1 ms
  s26/i8    Caller → Breakfast Provider  POST /pancakes  Created  1.1 s  b:2195078e 80 B
  s26/i9    Breakfast Provider → CosmosDB  CREATE /orders  Created
  s26/i11   Kafka Broker → Breakfast Provider  CONSUME (KAFKA) /breakfast_recipe_logs  Ack  0 ms  b:1cc793f0 189 B
  s26/i13   Breakfast Provider → Reporting Database (SQL Server)  INSERT /RecipeReports  OK
```

Proposed (the prototype, `flow_prototype.py`, on the same report):

```
── 0  Given a pancake batch has been created
  s26/i0    Caller → Breakfast Provider  GET /milk  OK  15 ms
    s26/i1    Breakfast Provider → Cow Service  GET /milk  OK  14 ms
  s26/i4    Caller → Breakfast Provider  GET /eggs  OK  1 ms
  s26/i6    Caller → Breakfast Provider  GET /flour  OK  1 ms
  s26/i8    Caller → Breakfast Provider  POST /pancakes  Created  1.1 s  b:2195078e 80 B
    s26/i9    Breakfast Provider → CosmosDB  CREATE /orders  Created
    s26/i11   Kafka Broker → Breakfast Provider  CONSUME (KAFKA) /breakfast_recipe_logs  Ack  0 ms  b:1cc793f0 189 B
    s26/i13   Breakfast Provider → Reporting Database (SQL Server)  INSERT /RecipeReports  OK
    …six more, all inside s26/i8
38 calls shown · http s26/iN --keys for a payload · indented calls ran inside the call above them
```

Today the Kafka, Pub/Sub and Event Hub lines read as "after the POST". The indented form says nine calls
ran inside the POST's 1.1 s, three of them message deliveries on its own trace.

| Measured (RUN, 2026-09-26) | |
|---|---|
| Corpus | 37 reports on this machine: 1,134 scenarios, 7,183 requests (§1) |
| Requests that ran inside another call, rule R4 | 4,333 (60.3%) |
| Deepest nesting | one level, in every report |
| Parents the plain rule R1 gets wrong | 74: 67 calls under a sibling from the same party, 4 under a request never answered, 3 under a background call. R4's clauses exclude all three shapes |
| Lines whose indentation points at a line that is not the parent | R4: 0. Caller-only R2: 961 |
| `inside` references an unfiltered view needs | 0 over 992 scenarios |
| Bytes | +3.9% over 992 scenarios (826,657 to 859,022); largest +18.6%, a 274-byte flow gaining 51 |

---

## 1. How far each claim was checked

Marks as in `ROADMAP.md`: **RUN** (a command was executed for this plan), **READ** (the source was read for
it), **INFERRED** (reasoned from two facts, stated by neither).

**The corpus** (RUN): every `TestRunReport.json` on this machine, `runs/` and `baseline/` copies excluded.
BreakfastProvider's five full lanes (BDDfy, LightBDD, NUnit, ReqNRoll, TUnit; 178 to 205 scenarios each)
and a one-scenario xUnit run, written by Kronikol 3.0.83 on 2026-09-05; the fifteen in-repo
`examples/Example.Api` reports, written by 3.0.77 to 3.28.0; `tests/Kronikol.Tests` (no calls); fifteen
Kronikol4J module reports (66 scenarios, no calls).

**The tool** (RUN): `Kronikol.Tool` 3.29.6 built from `6c689b5e` into a scratch directory. The prototype's
`--mode flat` is byte-identical to it, trailing whitespace aside, on ten scenarios of two lanes
(`results-fidelity.txt`), so the prototype's `--mode nested` is a faithful picture of the proposal.

---

## 2. What `flow` does today

### 2.1 The verb (READ)

[`Flow`](../src/Kronikol.Tool/QueryCommand.Narrative.cs#L467-L527) walks every record of the scenario,
requests and responses both, in the order of `httpInteractions`, which is capture order and the order
addresses number:

- Before record `i` it prints the annotations whose index is `i` (:492-493), for `i` below the record count
  only.
- When a record's `stepPath` differs from the last one it prints that step's header (:495-500). This runs
  for every record, before any filter is applied.
- It skips responses, then applies `--step`, `--service` and `--errors-only` (:502-513).
- It prints one line per surviving request: address padded to 9, caller → service, summary, status,
  duration, body pointer (:515-518). Status and duration come from the paired response, found by
  [`FindResponse`](../src/Kronikol.Tool/QueryCommand.Narrative.cs#L529-L551): the first response with the
  request's `requestResponseId`, else a proximity scan for entries without one. `FindResponse` is the one
  pairing routine of the tool, used at four call sites
  ([`QueryCommand.Shared.cs:31`](../src/Kronikol.Tool/QueryCommand.Shared.cs#L31), [`:68`](../src/Kronikol.Tool/QueryCommand.Shared.cs#L68),
  [`QueryCommand.Payloads.cs:170`](../src/Kronikol.Tool/QueryCommand.Payloads.cs#L170) and `Flow`).
- A `responses` dictionary is built at :478-481 and never read.

The description `--describe` prints is
[`VerbTable.cs:167-171`](../src/Kronikol.Tool/Query/VerbTable.cs#L167-L171): "One scenario's calls in
order, grouped under the step that made them - the diagram as text, in 1-2 KB."

The index already holds everything nesting needs, per interaction
([`ReportIndex.cs:210-252`](../src/Kronikol.Tool/Query/ReportIndex.cs#L210-L252)): `Ordinal`, `Type`,
`RequestResponseId`, `CallerName`, `ServiceName`, `TraceId`, `ActivityTraceId`, `StepPath`. **No scanner
change.**

### 2.2 What the records say (READ, RUN)

- **A response record repeats its request's caller and service.** It is `Caller → Breakfast Provider` for
  both halves (RUN, `dump_records.py` on s26), so "who made this call" is the request's `callerName` and "who is
  handling it" is its `serviceName`.
- **`traceId` is Kronikol's own propagated identity, not an ambient test id.** The HTTP handler reuses the
  incoming `test-tracking-trace-id` header or mints one
  ([`TestTrackingMessageHandler.cs:211-214`](../src/Kronikol/Tracking/TestTrackingMessageHandler.cs#L211-L214)),
  a consumed message carries its producer's
  ([`MessageTracker.cs:362`](../src/Kronikol/Tracking/MessageTracker.cs#L362)), and a SQL call gets a fresh
  one ([`SqlDiagnosticTracker.cs:64`](../src/Kronikol/Sql/SqlDiagnosticTracker.cs#L64)). In s26 the
  Kafka, Pub/Sub and Event Hub deliveries carry POST /pancakes' `traceId`; the CosmosDB and SQL calls
  carry fresh ones (RUN). An ingested record takes the span's trace, else the wire record's
  ([`InteractionMerger.cs:316`](../src/Kronikol/Ingestion/InteractionMerger.cs#L316)).
- **Two in five requests carry no timestamp** (F5).

### 2.3 The precedent

`kronikol ingest --call-tree` already orders records as a call tree
([`IngestPipeline.cs:839-846`](../src/Kronikol/Ingestion/IngestPipeline.cs#L839-L846)), and its parent
is "the latest-started earlier request whose interval contains this one and whose service is this unit's
caller" (:884). R4's first clause is that rule, read from capture order instead of timestamps. Its second
clause, the trace, has no counterpart there (Q4).

---

## 3. Findings

**F1. Nesting is the majority case.** Under R4, 4,333 of 7,183 requests (60.3%) ran inside another call,
every one of them one level deep (RUN, `results-rules.txt`). In BreakfastProvider's lanes the pattern is
the test calling the service, and the service calling its dependencies while the test waits.

**F2. The plain rule, "the innermost call still waiting for its answer" (R1), is wrong three ways** (RUN,
`results-census.txt`):

- **Parallel fan-out becomes a staircase.** 46 responses close a request that is not the innermost open
  one, because a party made several calls at once and they answered in another order. R1 puts each such
  call under its sibling: 67 calls, at depths 2 to 4. BDDfy s57, the health check, puts its four
  dependency checks at depths 1, 2, 3 and 4; BDDfy s103/i40, i42 and i44, CosmosDB writes, land under a
  Kitchen Service call the same service had open.
- **A request never answered holds everything after it.** Four requests have no response record (s165 in
  the BDDfy, NUnit and TUnit lanes: the AsyncApi endpoint, requested again), and under R1 the test's next
  call nests under them: 4 calls, one of them two deep.
- **A background call can capture the test's call.** In ReqNRoll s89, s99 and s174 a CosmosDB query
  recorded before the step, from outside the test, is still open when the test makes its call, and R1
  nests the test's call under it, across a step boundary.

**F3. Matching the caller alone (R2) misplaces message deliveries.** A delivery is recorded as
`Kafka Broker → Breakfast Provider`, so no open call has the broker as its service and R2 leaves it at the
top level, between the POST's other children. Indentation then points at the wrong parent 961 times: the
lines after a delivery read as the delivery's children (BDDfy s0/i11: parent i6, indentation says i9).

**F4. R4 (§4.1) nests 4,333 requests.** Its indentation never points at a line other than the parent,
no child sits in a later step than its parent, and by construction it gives none of F2's three wrong
parents. Adding `activityTraceId` to the trace clause (R3) gives the same counts, and would expose the rule
to an ambient per-test trace, so R4 reads `traceId` alone.

**F5. Timestamps cannot drive it.** 2,908 of 7,183 requests (40.5%) carry no timestamp (RUN): the
CosmosDB, SQL and Kafka produce records of the consumer's lanes (s26/i9, i13, i15). Capture order is the
one signal every record has.

**F6. Defect: a filtered `flow` and a step address print headers with nothing under them.** The header is
printed at :495, before the filters at :505-513 (READ). RUN on 3.29.6:

```
$ kronikol query flow <ReqNRoll> s26 --errors-only
s26  An order should progress through all status transitions to completion  [Passed]

── 0  Given a pancake batch has been created
── 1  And a breakfast order has been placed for the batch
── 2  When the order progresses through all statuses to completed
── 3  Then the completed order should be retrievable with all details
── 4  And an audit log entry should exist for the order
  (nothing matched the filters)
```

Five headers, which read as five steps that made no calls. They made 38. `flow s26/4` prints the headers
of steps 0 to 3 above step 4's two calls, although `AddressRoundTripTests` documents the step address as
scoping the answer.

**F7. Defect: an annotation after a scenario's last call is never printed.** The loop that prints
annotations runs for indices below the record count (:488-493), while an annotation recorded after the
last call is given index = count (the counter at
[`ReportGenerator.cs:4144-4175`](../src/Kronikol/Reports/ReportGenerator.cs#L4144-L4175) advances on
real calls only). RUN on `trailing-annotation-probe.json`: `annotations s0` lists "after the last call",
`flow s0` drops it. None of the 9 annotations in the corpus sits there, so no real report is known to have
lost one.

**F8. Dead code.** The `responses` dictionary (:478-481), keyed by ordinal, is never read. `FindResponse`
scans the scenario per request, but it is shared by four call sites and its cost was not measured, so it
stays.

**F9. Two different facts print the same blank.** A request never answered and an answered call with no
status both show nothing where the status goes: 4 of the first, 258 of the second in the corpus (RUN).
Q1.

**F10. Coverage gap.** Nothing tests `flow`'s pairing, header placement or line format beyond `Contains`
checks. Exact pairing and the proximity fallback are pinned through `interactions`
(`Interleaved_calls_to_one_service_pair_by_requestResponseId`,
`Pairing_falls_back_to_proximity_when_the_id_is_absent`), which reach the same `FindResponse`.

**F11. The README tells AI tools to read the raw PlantUML.** [`README.md:120-122`](../README.md#L120-L122),
"Feeding AI tools", calls it "a compact, structured representation" to "feed directly into AI coding
assistants". One diagram has measured 663 KB, the tool refuses to print one for that reason
([`QueryCommand.Payloads.cs:618-623`](../src/Kronikol.Tool/QueryCommand.Payloads.cs#L618-L623)), and the
skill says never to read it. Found in passing; docs only, no bump; it can go with S1 (§7.3).

**F12. Tool-only.** No report byte changes, so no golden re-pin and no Kronikol4J divergence entry.
Kronikol4J has no query tool, and it writes the fields R4 reads
([`ReportDataSerializer.java:293-309`](../../Kronikol4J/kronikol4j-report/src/main/java/io/kronikol/report/data/ReportDataSerializer.java)),
but none of its reports on this machine holds a call, so R4 has not run on a Java report (§7.5).

---

## 4. The design

### 4.1 The rule, R4

Walk the scenario's records in capture order. A request is **open** from its record until the response
record carrying its `requestResponseId`. A request with no `requestResponseId`, or whose response never
appears in the scenario, is never open: nobody knows when it ended, so it is never a parent.

The **parent** of a request X is the innermost open request P for which either

- **(a)** P's service is X's caller: the party handling P made X; or
- **(b)** X's caller is not P's caller, and X carries P's `traceId`: X arrived on P's trace while P waited.

If no open request qualifies, X is at the top level. Every clause answers a measured case:

| Clause | Without it | Measured |
|---|---|---|
| (a) caller = open call's service | a service's calls are not placed under the call it is handling | clause (a) alone (R2) nests 3,769 |
| (b) the trace, for another caller | deliveries sit at the top level and misplace their siblings | adds 564 (the census's "other caller, same trace"); without it R2's indentation misleads 961 times |
| (b) "X's caller is not P's caller" | a party's parallel calls on one trace become each other's children | the fan-out of F2, s57 |
| never answered, never open | one lost response captures the rest of the scenario | 4 requests, F2 |

### 4.2 What "ran inside" claims

Indentation says: **this call was made while the call above it was waiting for its answer, by the service
handling that call or on its trace.** It does not say the call above caused it. Work the handling service
does in the background during the call nests too, which is true in time (INFERRED: the s26 SQL insert
that follows a Kafka delivery's acknowledgement nests under POST /pancakes, which was still open). The
legend and the docs use these words, and never "caused by".

### 4.3 How it prints

- **Depth** is the number of the line's ancestors that are shown. Unfiltered, that is its depth in the tree;
  under a filter, an ancestor that is filtered out does not count.
- **Two spaces per level, before the address,** so the whole line moves, the way a call stack or `tree`
  prints and the way an LLM reads nesting without being told.
- **A line names its parent, `inside s26/i8`,** unless the parent is the nearest line above it, in the same
  step section, one level shallower. That one condition covers a parent filtered out, a parent in an
  earlier step, and two branches whose lines interleave in capture order. It names the immediate parent,
  an address `http` takes. Unfiltered, the corpus needs none (RUN); under `--service CosmosDB` every line
  gets one, which is how a filtered view says which call a downstream call belonged to.
- **The line is built from its non-empty fields.** Today an empty status or duration leaves trailing
  spaces (s26/i9 ends in two); the budget pays for them, and the suffix would sit after a gap.
- **The footer gains `indented calls ran inside the call above them`** when any line is indented.

### 4.4 Headers and annotations (S1)

A step header is printed only above a call that is shown. Annotations are printed where they fall among
the shown calls, in today's order (the annotations, then the header), so one whose call is filtered out
appears above the next call shown. Annotations after the last shown call, including one after the
scenario's last call, are printed at the end. A view that shows no call prints only
`(nothing matched the filters)`.

### 4.5 What does not change

The order of lines (capture order), every address, what each filter selects, `--count`, the byte budget,
the shape of `--describe` (only the description text), and the report. No new flag (§8.2) and no `--json`
for `flow`.

### 4.6 Where the code goes

- `src/Kronikol.Tool/Query/CallNesting.cs`, internal: `Parents(IReadOnlyList<InteractionEntry>)` returns
  each entry's parent ordinal or null, in one pass over an open list: O(n × open calls). Pairing by
  `RequestResponseId`, the first response per id, as `FindResponse` pairs; entries without an id are never
  parents. Tested alone (§6.4).
- `Flow` becomes two passes: which calls are shown, then printing with the headers, annotations, depth,
  references and legend above. `FindResponse` is untouched.
- The rule lives in one place so a later `http`, `compare` or Failures.md nesting (Q2, Q3) calls it rather
  than copying it.

---

## 5. Before and after (RUN)

All from `results-examples.txt`: today is the 3.29.6 tool, proposed is the prototype, BreakfastProvider's
2026-09-05 reports.

**Fan-out** (BDDfy s57; R1 would print these at depths 1, 2, 3, 4):

```
── 0  When the health check endpoint is called
  s57/i0    Caller → Breakfast Provider  GET /health  OK  1.96 s
    s57/i1    Breakfast Provider → Kitchen Service  GET /health  OK  139 ms
    s57/i2    Breakfast Provider → Supplier Service  GET /health  OK  135 ms
    s57/i3    Breakfast Provider → Goat Service  GET /health  OK  135 ms
    s57/i4    Breakfast Provider → Cow Service  GET /health  OK  135 ms
```

**A failure and its cause** (ReqNRoll s59 `--errors-only`):

```
── 1  When goat milk is requested
  s59/i0    Caller → Breakfast Provider  GET /goat-milk  BadGateway  48 ms
    s59/i1    Breakfast Provider → Goat Service  GET /goat-milk  ServiceUnavailable  22 ms
2 calls shown · http s59/iN --keys for a payload · indented calls ran inside the call above them
```

**A filtered view** (ReqNRoll s26 `--service CosmosDB`, first lines):

```
── 0  Given a pancake batch has been created
  s26/i9    Breakfast Provider → CosmosDB  CREATE /orders  Created  inside s26/i8
── 1  And a breakfast order has been placed for the batch
  s26/i29   Breakfast Provider → CosmosDB  CREATE /orders  OK  inside s26/i28
  s26/i33   Breakfast Provider → CosmosDB  CREATE /orders  Created  inside s26/i28
```

**S1 on its own** (ReqNRoll s26): `--errors-only` prints the title and `(nothing matched the filters)`,
with no header; `--step 4` prints step 4's header and its two calls, without steps 0 to 3. The probe
report prints `── after the last call` below its call.

**A background call stays out** (ReqNRoll s89): the CosmosDB query recorded before step 0 prints at the
top level with no header above it, as today, and the test's `DELETE /menu/cache` is not nested under it.

---

## 6. Tests, red first

### 6.1 Where

- `tests/Kronikol.Tests/Tool/CallNestingTests.cs`: the rule, on `InteractionEntry` lists built in memory.
- `tests/Kronikol.Tests/Tool/FlowTests.cs`: the verb, on reports written through
  `ReportGenerator.GenerateTestRunReportData` from `RequestResponseLog` arrays and run through
  `QueryCommand.Run`, the pattern of `QueryCommandTests.Write` and `RunFull`. Its own fixture, so the
  shared `QueryCommandTests` fixture keeps its counts.

### 6.2 Before S1: pin what exists (green today, F10)

1. `Flow_pairs_interleaved_calls_to_one_service_by_requestResponseId` (the t4 shape: reqA, reqB, respB,
   respA).
2. `Flow_pairs_by_proximity_when_the_id_is_absent`.
3. `Unfiltered_flow_prints_steps_annotations_and_calls_in_capture_order`: one scenario with two steps, a
   `Row` annotation and an interleaved pair, pinned whole, then held byte-identical by every later change
   except S2's indentation and trimmed line ends.

### 6.3 S1 (each red on the release before it)

4. `A_step_address_prints_only_that_steps_header`
5. `A_filter_that_matches_nothing_prints_no_step_header` (only the title and `(nothing matched the filters)`)
6. `A_step_whose_calls_are_all_filtered_out_prints_no_header`
7. `An_annotation_after_the_last_call_is_printed`
8. `An_annotation_whose_call_is_filtered_out_prints_above_the_next_shown_call`
9. `A_view_that_shows_no_call_prints_no_annotation`

### 6.4 S2, the rule (`CallNestingTests`)

10. `A_call_made_by_the_service_handling_an_open_call_is_inside_it`
11. `Calls_a_service_makes_at_once_are_siblings_whatever_order_they_answer_in` (R1 gives a staircase)
12. `A_message_delivered_on_an_open_calls_trace_is_inside_it`
13. `Two_calls_from_one_caller_are_never_parent_and_child`, with a shared `traceId`, so it proves the
    same-caller exclusion beats clause (b)
14. `A_request_never_answered_is_never_a_parent`
15. `A_request_without_a_requestResponseId_is_never_a_parent`
16. `A_call_from_another_caller_on_another_trace_stays_top_level` (the s89 shape)
17. `The_innermost_qualifying_call_is_the_parent` (test → api, api → auth, auth → api callback, api → db:
    db is inside the callback)
18. `Nesting_goes_as_deep_as_the_calls_do` (a chain of four: the corpus never goes past one level, so depth
    two and three are held here)
19. `A_response_closes_its_request_wherever_it_sits` (out-of-order closing)

### 6.5 S2, the verb (`FlowTests`)

20. `A_nested_call_is_indented_two_spaces_under_its_parent` (exact line prefix)
21. `The_legend_is_printed_only_when_a_line_is_indented`
22. `A_line_whose_parent_is_filtered_out_names_it`
23. `Every_inside_reference_is_an_address_http_takes` (round trip, exit 0)
24. `Interleaved_branches_name_their_parent` (api → svc1 and api → svc2 open, then svc1 → db: db names svc1)
25. `A_child_recorded_in_a_later_step_names_its_parent`
26. `No_flow_line_ends_in_whitespace`
27. `A_report_without_requestResponseIds_prints_flat` (written literally, like `UnenrichedReport`)
28. `Count_is_unchanged_by_nesting`

### 6.6 Existing facts that must stay green

`QueryCommandTests`: `Flow_replaces_reading_the_diagram`, `Created_and_NoContent_are_not_errors_anywhere`,
`Text_ERROR_status_is_an_error_everywhere`, `No_scenario_command_emits_a_payload`,
`Every_command_stays_under_the_budget`, and the flow rows of
`A_flag_a_verb_never_reads_is_refused_rather_than_ignored` and `The_flags_a_verb_does_read_are_still_accepted`.
`AddressRoundTripTests`: `A_step_address_scopes_the_calls_a_flow_shows`,
`The_step_flag_and_the_step_address_are_the_same_question`. `SkillDriftTests` (both skill copies
identical; verbs and flags only, no description text is pinned: READ).

### 6.7 Proving red

Copy the new facts into a worktree at the previous release's tag and run them there (the audit checklist's
rule): 4 to 9 must fail there, 10 to 28 must fail or not compile. A fact that passes there is vacuous and is
rewritten.

---

## 7. Slices, releases, records

### 7.1 S1, a patch (roadmap 1.13)

F6, F7, F8, and the characterization facts of §6.2 first. A bug fix that changes what a filtered `flow`
prints, so a patch with the behaviour change called out. Changelog draft:

> **Fixed.** `kronikol query flow` under a filter or a step address printed the header of every step that
> made a call, with nothing under it: `flow s3 --errors-only` on a passing scenario listed each step, which
> reads as steps that made no calls, and `flow s3/4` listed steps 0 to 3 above step 4. A step header or an
> annotation is now printed only above a call that is shown, so a filtered `flow` prints fewer lines.
> `flow` also printed no annotation recorded after a scenario's last call, which `annotations` listed; it
> is printed after the last call. Patch: bug fixes, nothing new to call.

### 7.2 S2, a minor (roadmap 10.0, D22 taken 2026-09-26)

§4.1 to §4.3, Q1's `no response`, `CallNesting`, the `VerbTable` description, and the docs of §7.3. New information in a verb's
output, so a minor by `CLAUDE.md`. If S1 and S2 ship in one release, that release is the minor. Changelog
draft:

> **Added.** `kronikol query flow` indents a call under the call it ran inside: made while that call was
> waiting for its answer, by the service handling it or on its trace. A line whose parent the indentation
> cannot show names it (`inside s3/i8`), so `flow s3 --service orders-db` says which request each query
> belonged to, and `--errors-only` shows a failing call with the failure inside it. The footer says what
> indentation means, and a request that never got an answer says `no response`. No flow line ends in
> spaces any more. Minor: new information in a verb's output.

### 7.3 Docs

| Where | What | Slice |
|---|---|---|
| [`VerbTable.cs:167-171`](../src/Kronikol.Tool/Query/VerbTable.cs#L167-L171) | "One scenario's calls in order, grouped under the step that made them, each indented under the call it ran inside - the diagram as text, in 1-2 KB." | S2 |
| `templates/skills/kronikol-test-debugging/references/commands.md:153-155` and the `.claude/skills` copy, identically | the rule in one sentence, the `inside` reference, the legend | S2 |
| the same two `SKILL.md` copies, :101 | "why is this slow?": the calls indented under the slow one are where its time went | S2 |
| wiki `Querying-Reports.md:475-478` | the rule, one example, the filtered-view reference; and under S1, that a filtered view prints only the steps it shows | S1, S2 |
| [`README.md:120-122`](../README.md#L120-L122) | point an AI debugging a run at `kronikol query`, keep the PlantUML for the architecture use below it (F11) | S1 |
| `CHANGELOG.md` | §7.1, §7.2 | each |

`src/Kronikol/Reports/agent-instructions.md` (:96, :100) stays: its rows remain true, and editing it
changes every report's generated `CLAUDE.md` and `AGENTS.md`.

### 7.4 Records

`PLANS_STATUS.md` row and `ROADMAP.md` rows 1.13, 10.0 and D22 (written with this plan). No golden, no
Kronikol4J ledger entry (F12). Both skill copies change together (`SkillDriftTests`).

### 7.5 Before declaring done

- **The real tool against the prototype.** Every scenario of the five BreakfastProvider lanes, unfiltered,
  with `--service CosmosDB` and with `--step 1`: the built `flow` must equal `flow_prototype.py --mode
  nested`. `--errors-only` is compared on s26 and s59 only, because the prototype's error test is an
  approximation of `InteractionStatus.IsError`. Then `flow_bytes.py`'s three numbers, re-taken from the
  real output.
- **Reports the corpus lacks.** Run `nesting_rules.py` on a Kronikol4J report that captured calls, and on
  one run of the consumer ingested with and without `--call-tree` (Q4). If R4 points at a wrong parent on
  either, stop and bring the case back.
- The `plan-execution-audit-checklist` sweep, the wiki link checker (`tools/wiki-links`), and the CI run of
  the pushed SHA.

### 7.6 Where it sits in the roadmap

- **S1 is 1.13, in stage 1,** by rule 1: a live defect in shipped code, small, no decision.
- **S2 is 10.0, first in stage 10.** It is agent-facing work, and the agent channel is a row of the bar.
  Before 10.1, because MCP wraps the verbs, and before 10.2, whose cold agent has to "name the interaction
  that broke": nesting is what says a 502 came from the 503 inside it. Before 14.3 by rule 6, since the
  query conformance corpus pins `flow`'s text.
- **Both are track A** (`Kronikol.Tool/`, `VerbTable`, both skill copies), so neither runs beside another
  track A stage (rule 5). The files are few, so S2 may be pulled forward to any point when no other track A
  stage is in flight (rule 8: small and measured).

---

## 8. Not taken, and open questions

### 8.1 Mermaid output, the question this came from

A prototype turned s26's call data into a Mermaid sequence diagram: 3,555 bytes against `flow`'s 3,817,
and valid (mermaid 11.17.2 through mermaid-cli rendered all 76 messages). But an LLM reads `flow`'s arrow
lines as readily as Mermaid's, so the format itself adds nothing; the one gain was where responses fall,
which this plan gives `flow`. A second format would also mean building the addresses, failure notes,
budget and truncation twice. Mermaid stays where a human renders it: V4's CI summaries (roadmap 11.1).

### 8.2 Designs not taken

- **R1, R2, R3**: F2, F3, F4.
- **Reordering lines into a tree** (children printed under their parent even when capture order
  interleaves them): it would break "in order", which `flow`'s description promises, to serve a case the
  corpus never shows; the `inside` reference covers it.
- **Timestamps**: F5.
- **A `--flat` flag**: nothing reads `flow`'s text back (it has no `--json`; `SkillDriftTests` pins verbs
  and flags only), so a flag would be public surface with no user.

### 8.3 Open questions

| # | Question | Recommendation |
|---|---|---|
| Q1 | Print `no response` in the status place for a request never answered, so it no longer looks like the 258 answered calls with no status (F9)? | **Taken 2026-09-26: yes, in S2.** R4 gives "never answered" a consequence (such a call holds no children), and the line should say why |
| Q2 | Should `http sN/iM` print the call it ran inside? | Later, its own minor, reusing `CallNesting` |
| Q3 | Nest the per-step call lists of `Failures.md` and of `compare`? | Measure after S2 ships. `Failures.md` is report output, so it would be a report change with its own record |
| Q4 | `--call-tree` ingest places a delivery after its parent's response (its parent rule has no trace clause), so `flow` would nest it in a report ingested without `--call-tree` and not with it (INFERRED from `OrderAsCallTree`'s comment, not RUN). Give `OrderAsCallTree` clause (b)? | Run §7.5's ingest check first; decide on what it shows |
| Q5 | UI user actions have no response record, so a click cannot be a parent and the calls it caused stay at the top level | Measure on the first UI report; none is on this machine |
| Q6 | The legend costs about 50 bytes per nested flow | Keep it. The tool explains its output in its footers, and "ran inside" is not "caused by" |

D22 in the roadmap asked for the green light, R4 and Q1 together; the owner took all three on 2026-09-26.

---

## 9. Assumption ledger

| Claim | Basis | Where |
|---|---|---|
| 4,333 of 7,183 requests nest under R4, all one level deep, and no indentation points at a line that is not the parent | RUN | `results-rules.txt` |
| R1: 46 out-of-order closes; 67 calls under a same-party sibling (depths 2 to 4), 4 under a request never answered, 3 under a background call | RUN | `results-census.txt` |
| R2: indentation points at a line that is not the parent 961 times | RUN | `results-rules.txt` |
| R3 and R4 give the same counts | RUN | `results-rules.txt` |
| 2,908 of 7,183 requests carry no timestamp | RUN | §1 corpus |
| +3.9% bytes over 992 scenarios, largest +18.6%, 0 `inside` references unfiltered | RUN, prototype | `results-bytes.txt` |
| The prototype's flat mode is today's `flow` | RUN, 10 scenarios, trailing whitespace ignored | `results-fidelity.txt` |
| F6 and F7 are live at 3.29.6 | RUN | `results-examples.txt` |
| `traceId` is propagated, not ambient | READ | §2.2 |
| `FindResponse` is shared by four call sites | READ | §2.1 |
| No test pins `flow`'s description text | READ | `DescribeTests`, `CommandTableTests`, `SkillDriftTests` |
| Kronikol4J writes the fields R4 reads | READ | F12 |
| R4 on a Java report, and on an ingested report | not run: no such report here | §7.5 |
| Background work of the handling service nests under the open call | INFERRED | §4.2 |
| `--call-tree` ingest shows deliveries flat | INFERRED | Q4 |
| The skill's Python fallback has no `flow` to mirror | READ (`query.py` implements summary, failures, steps, services, grep, http) | none |

## Appendix A. Re-taking the numbers

The commands are in [`FLOW_NESTING_PLAN.harness/README.md`](FLOW_NESTING_PLAN.harness/README.md). The
corpus is whatever reports the machine holds; the BreakfastProvider lanes named in §1 are the ones every
figure in this plan was taken on.
