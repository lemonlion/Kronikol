# Span attribution plan: #87, `V4_PLAN.md` R3

**Written:** 2026-09-29, at 3.35.0, as `V4_PLAN.md` §3.3 requires before #87 is fixed. **Status:** green-lit with
`V4_PLAN.md` (the owner asked for that plan in full on 2026-09-29; Q5 taken: both causes are fixed on in-repo
evidence, without waiting for the reporter's report). **Executed as 3.35.1 the same day** (§6). Roadmap row 1c.4. Evidence labels: **RUN**, **READ**, **INFERRED**.

## 1. The defect

A call's popup takes the spans of its trace that start in its window, the request less 50 ms to its response (READ,
`InternalFlowSegmentBuilder.cs:79-92`). Two paths let another request's spans in (`V4_PLAN.md` §3.3):

- **(a) No trace id.** A call whose log records no `ActivityTraceId` takes every span of its test in the window, and
  a test none of whose calls records one takes every span of the run (`FilterSpansByTestTraceIds` returns all spans,
  READ `:173-186`). Every concurrent test's spans land in its popups. The whole-test flow takes the whole run (`:128`).
- **(b) A shared trace.** Calls made under one ambient `Activity` all record its trace id and span id
  (`TestTrackingMessageHandler.cs:156-160`). When tests share it, only the window separates their spans.

A call with no ambient `Activity` gets a trace minted for it (`:161-169`), so its spans are its own; that path is right
today and must stay so.

## 2. The rule

A span belongs to a call when the trace, the structure or the time says so, and **a span that more than one test's
calls would take goes to none of them**: the popup says how many spans it left out and why, instead of showing
another request's work as this one's. In order, per call:

1. **Candidates.** The spans of the call's trace, narrowed by step 2 where it applies. With no trace id: what its
   test's traced calls may take by steps 1 and 2, when the test has any; otherwise the spans whose trace no call of
   the run claims, never every span of the run. (Written first as "the spans of its test's traces, as today". No
   database tracker records a trace id, only the HTTP handler, gRPC, the message tracker, the taps and OTLP do, so a
   test's database calls would have taken the whole shared trace in their time and contested every span step 2 gives
   to another test: §6.)
2. **Structure, where the trace is shared by calls of more than one test:** the call's `ActivitySpanId` and its
   descendants (`ParentSpanId`, within the trace). A test's own ambient span under a shared parent separates its
   calls from the others'; one ambient span shared by every test does not, and step 4 handles it. A trace used by
   one test keeps today's selection, so no popup of an unaffected report changes.
3. **Time.** Of those, the spans that start in the call's window (unchanged).
4. **Ambiguity.** A span that calls of two or more tests select is removed from all of them, and each segment counts
   what it lost. Calls of one test may share spans (an outer call and the calls nested in it), so this is between
   tests only.

The whole-test flow of a test takes all spans of each trace only it claims (as today), and, of a shared trace or for
a test that claims none, only what its calls kept after step 4.

**What the reader sees.** A popup that lost spans says so in its content ("N spans in this call's time also belong to
a request of another test, and the trace does not say which: left out") and its title counts what it shows. A call
that lost every span still gets a popup saying why, whatever `InternalFlowNoDataBehavior` is, since hiding the link
would read as "nothing happened". The count lives on `InternalFlowSegment` as an internal property, so the release
adds no public API (a patch).

## 3. What stays

The handler never creates or changes `Activity.Current` (pinned, `TestTrackingMessageHandlerTests.cs:1765`, `:1778`).
The 50 ms tolerance, the window's end (the response, else the next record, else 5 s), marker and user-action
exclusion, and span order. One builder fact pinned the old fallback and turns round on purpose:
`BuildWholeTestSegments_multiple_tests_get_separate_segments` gave two tests whose calls record no trace every span of
the run each, and now gives each test a trace and its own spans. (This paragraph first named four others,
`BuildSegments_null_trace_ids_returns_all_spans`, `BuildSegments_groups_by_test_id` and the two whose spans carry no
trace: each holds one test, whose calls contest nothing, and they pass unchanged.)

## 4. Proof

1. #87's own test, once per cause, at the builder: two tests' calls to one endpoint overlapping in time, each with a
   known and different number of query spans; each popup holds only its own, or, where nothing tells them apart,
   holds none and says how many it left out. Red on 3.35.0 for (a) and for (b).
2. The same with a real in-process server and concurrent calls through `TestTrackingMessageHandler`: minted traces
   (no ambient `Activity`) keep every popup exact; one ambient `Activity` shared by both tests leaves out the
   overlapping spans instead of pooling them.
3. A test's own ambient span under a shared trace separates its calls by structure (no spans lost).
4. Nested calls of one test keep sharing spans; a serial suite's popups do not change (the existing builder facts,
   untouched but for the four above).
5. The whole-test flow of a test with no trace ids no longer holds the run's spans.
6. The popup's note and title, and the message for a call that lost everything under `HideLink`.

## 5. Not in this plan

- #86 (R5) and the segments no arrow links (R6). R4 re-measures #86 on reports written after this release.
- Reading span links, baggage or `tracestate`; a trace id for trackers that record none (their records are the
  trackers' business, and the rule already stops them pooling).

## 6. Execution log

- **3.35.1 (2026-09-29).** §2's rule, with step 1 changed as it notes: a call with no trace id takes what its test's traced calls may take. §4's proofs: items 1, 3, 4 and 5 are builder facts (`SpanAttributionTests`); item 2 runs two tests' calls at once through the real `TestTrackingMessageHandler` into a stand-in service that continues the caller's trace as ASP.NET Core does (`SpanAttributionThroughTheHandlerTests`): minted traces keep both popups exact, and one ambient `Activity` around both leaves out what both could own; item 6 is a builder fact per flame-chart layout and two Playwright facts on a report the whole pipeline wrote (`SpansLeftOutPopupTests`). Run against 3.35.0's builder, the 12 cases that state a change fail and the 4 that state what stays pass. Thirteen guards broken one at a time were each caught (`V4_PLAN.harness/mutate_r3.py`). On BreakfastProvider's xUnit lane with a local build, 203 of 203 passing: 6 popups leave 12 spans out and 3 calls keep a popup that only says why; 1,294 popups show spans against 1,302 on 3.35.0, and 200 scenarios have a whole-test flow against 204 (two runs, so the counts also carry the runs' own differences). Building the segments costs what it did: 10 to 41 ms against 31 to 45 ms on 3.35.0, on a synthetic run of that size (1,600 calls, 9,400 spans).
- **Found on the way.** (1) No database tracker records a trace id (only the HTTP handler, gRPC, the message tracker, the taps and OTLP do), so "the spans of its test's traces" gave a test's database calls the whole trace an ambient `Activity` shares, and they contested every span the tree gives to the other test: a fact with each test's own ambient span and a database call each was red on the first cut, and step 1 now reads the tree too. (2) The note was dropped wherever the flame chart is on, which is the default: each of its layouts rebuilt the popup's content from the diagram alone; the builder fact had passed only because it called the generator with the flame chart off. (3) The popup script has set a segment's `message` as text since internal flow shipped, so `ShowMessage`'s diagnostic, which is markup, showed its tags, and so would this note; it renders the message as markup now, with the configured source names encoded. (4) `InternalFlowSegmentMapReportTests` failed about one run in three, on 3.35.0 too: `InternalFlowSpanStoreTests` cleared the process-wide span store from a collection that runs in parallel with it (0 failures in 6 runs without that fact, 3 in 7 with it). The clear runs after every parallel collection now, and `ProcessGlobalStoreTests` scans for any other.
