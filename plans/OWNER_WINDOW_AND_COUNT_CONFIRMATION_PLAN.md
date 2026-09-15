# The owner window, count changes confirmed, and the store guarded

**Status:** written 2026-09-15 after 3.19.0's consumer acceptance; green-lit the same day ("OK, go ahead with all
that"); shipped as **3.20.0**. §6 is the execution log.

## 1. The measured need

After 3.19.0 the user asked two questions: whether any tracking had been lost along the way for specific
circumstances, and whether consumers would get noise, "the primary reason why the history feature would fail
(false alerts etc)". Both were answered by measurement (`scratchpad/ledger_noise.py`, a replay of the analyzer's
behaviour rules over BreakfastProvider's `kronikol-history` ledger: 233 runs under the current templating rule,
18 lanes, the consumer's bar of `--min-runs 3`):

- **Nothing lost on the consumer.** Between 3.17.0 and 3.19.0 exactly one scenario's call set moved, the outbox
  retry exhaustion scenario, from 4 calls to 7, the processor's replaces back where the seed was. One structural
  gap remained: work in an identity-less flow that is neither a message, nor a scope, nor an operation on a
  document a scenario owns. The outbox processor's dispatch call between the claim of a row and its status update
  is the case: it names no document, so 3.19.0 could not place it. BreakfastProvider's dispatcher is not a tracked
  client, so nothing was captured there before or after; a consumer whose dispatcher is tracked loses that call.
- **One defect.** With `RequestResponseLogger.CaptureBackground` on, an identity-less write reached
  `AutoCorrelateIfWrite` in the Cosmos and Mongo trackers and registered the unknown identity as the document's
  owner; `DocumentOwnership.Resolve` then answered the next identity-less operation on the document with that
  "owner" and the provenance of one. Off by default (the resolver returns null when background is not captured).
- **Where the noise was.** Over the whole ledger the set-of-calls verdicts were 365 stayed (361 of them Kronikol's
  own upgrade transitions), 25 one-offs and 63 moved on (54 on the two back-to-back upgrade nights);
  `alternating` absorbed 90 returns and `unstable-shape` 83. The count verdicts were 13: 2 stayed, 10 reverted the
  next run, 1 with no next run. A count that moves once reverts more often than it holds. In the steady state
  after 3.19.0 (three dispatched runs), the docker and in-memory lanes read `behaviour-change: 0` on all 36
  lane-runs; over all 54 lane-runs there were 2 count alerts, both on external-SUT lanes (a reporting scenario
  polling once more after 10 to 12 constant runs), and 4 `slower`, which already needs two consecutive runs.

Three things follow, all Kronikol-side and automatic: confirm a count change on the second run before it is a
verdict; close the dispatch gap by keeping the flow on the scenario whose document it just wrote; and guard the
store against the unknown identity.

## 2. Count changes confirmed on the second run

The 3.15.0 rule made the same set of calls made a different number of times `behaviour-changed` on the run the
count moved, once it had been constant over `HistoryMinRuns` prior runs. From 3.20.0 the new count must be
**held**: `HistoryCountRuns` (default 2) consecutive runs must show it, the run that changed it and the next.

- The run that shows the new count reads it out in the evidence and raises nothing: `calls 5 in gh:41:1 to 6 now;
  a count verdict needs the new count held for 2 runs`.
- The next run that still holds it is the verdict, and names both: `the same calls made a different number of
  times: calls 5 to 6 in gh:42:1 and now, constant over the 12 runs before`. With `HistoryCountRuns = 3` the
  evidence lists the held runs: `to 6 in gh:42:1, gh:43:1 and now`.
- A count that reverts the next run is never a verdict, and the return says what it is instead of calling the
  count unstable: `calls 6 in gh:42:1 to 5 now; back to the count held over the 12 runs before it`.
- A count that changed together with the set was reported then, as a different set of calls, and is not
  reported again when it holds: the stretch's set must be the current set at the transition.
- Whatever the setting, the count must have been constant over `HistoryMinRuns` runs before the change; the
  advisory texts for a short or a varying history are unchanged.
- `HistoryCountRuns = 1` is the 3.15.0 rule, verbatim.

`HistoryAnalysisOptions.CountRuns`, `kronikol history gate --count-runs N`, `kronikol query history --count-runs N`.
A gate that fails on `behaviour-change` fails one run later for a real N+1; the user accepted the latency.

Replayed over the ledger at the consumer's bar: 13 count verdicts become 2, and both of those reverted on the
run after (a count held for two runs and then back), so the ledger holds no real N+1 at all and the old rule's
"2 stayed" were the first two runs of two-run blips.

## 3. The owner window

A document a scenario wrote is the scenario's (3.19.0). From 3.20.0, **a detached flow that wrote a scenario's
document is doing that scenario's work** until its next operation on a document that is not the scenario's.

### 3.1 The rule

- A **write** (Cosmos: create, upsert, replace, patch, delete; Mongo: insert, update, delete, findAndModify,
  bulk write) on a document with an attributed owner, made by a flow that resolved no scenario of its own, opens
  the flow's owner window for that owner, after the write succeeded. The write itself is the owner's per call, as
  in 3.19.0 (`DocumentOwner`).
- While the window is open, a call in the flow that resolves nothing else (a header, a `Begin` scope, a message
  identity all win) resolves to the owner with the provenance `DocumentFlow`: the dispatch between the claim and
  the status update, a publish, a call through any tracked client.
- Any operation on the **owner's** document, a read included, **confirms** what the flow did since the window
  opened; the window stays open.
- A **query** (Cosmos query or listing; Mongo find, aggregate, count, distinct, getMore, mapReduce naming no
  `_id`) names no document and **ends** the window: the poll before the next claim.
- An operation on **another scenario's** document ends the window; a write on it opens a new one for that owner.
- An operation on a document **nobody owns**, or on one **not yet known** (a create names its document in the body;
  a multi-document write names none), leaves the window as it is. It is not the owner's either.
- A **read never opens** a window: a poller reading a scenario's document every few hundred milliseconds must not
  make everything between its reads the scenario's.
- A call attributed by the window after its owner ended **expires** like an inherited one (`Expired`, `expiredFrom`).

### 3.2 Held until confirmed, so an interleaving never misattributes

What the window answers for is not recorded when it is captured: `RequestResponseLogger.Log` holds a
`DocumentFlow` entry on the flow. The next operation on the owner's document releases it into the store, in its
original place and with its original timestamp; a foreign operation or a query drops it (kept as background under
`(no scenario)` when `CaptureBackground` is on); at report time whatever is still held is settled as nobody's.

A call is therefore attributed only when the same document closes it out with nothing else between. Two workers
that share one detached flow and interleave (`W(x) W(y) D D W(x) W(y)`) drop both dispatches, which is what 3.19.0
did with them; two that do not interleave attribute both correctly; a sequential loop over a batch of rows
(`W(x) D W(x) W(y) D W(y)`) attributes every dispatch. There is no timeline in which one scenario's call lands in
another scenario.

### 3.3 Why a holder, and why detached flows only

The tracker that learns a document's owner runs inside the database SDK's async call. An `AsyncLocal` value set
there is restored when the call returns to the processor, so the window cannot be a value in a slot. It is a field
on an object the slot refers to: `TestIdentityScope.Detach` now stores a `DetachedFlow` holder in its `AsyncLocal`,
the SDK's callees inherit the reference, and what they set on it the caller sees. Every hosted service already
runs in such a flow (`DetachHostedServicesFromTestIdentity`, called by `AddTestTrackingContextPropagation`), so the
window is automatic for them. Outside a detached flow nothing holds a window, and ownership stays per call as in
3.19.0.

### 3.4 Mongo: a claim by filter

`findAndModify({status: Pending} -> {status: Processing})` is the idiomatic Mongo claim: it names no document until
the server answers with the one it took. From 3.20.0 the subscriber defers such a command's request half (when the
flow had no scenario of its own) until the reply, attributes both halves by the document the reply names, keeps the
request's original timestamp and phase, and opens the window. A claim that took nothing is nobody's, as before.

## 4. The store guarded

`AutoCorrelateIfWrite` registers a writer only when the identity is attributed (`TestIdentity.IsAttributed`), in
both trackers; `DocumentOwnership` ignores an owner whose id is `TestIdentityScope.UnknownTestId`, so a store a
consumer filled by hand cannot produce one either.

## 5. Outputs and acceptance

- **Report output:** `attributionSource` gains `DocumentFlow`; the JSON schema's enum lists it (generated from the
  enum), its description names `DocumentOwner` and `DocumentFlow`, the XSD types the source as a string and is
  unchanged. Kronikol4J's divergence ledger records it.
- **Public surface (MINOR):** `AttributionSource.DocumentFlow`; `DocumentOperationKind`;
  `DocumentOwnership.ForOperation` and `AfterWrite` (`Resolve` unchanged); `TestIdentityScope.OwnerWindow`;
  `ReportConfigurationOptions.HistoryCountRuns`; `HistoryAnalysisOptions.CountRuns`; `--count-runs` on the gate
  and on `query history`. `AttributeByDocumentOwner` (both trackers) switches the window off with the rest.
- **Unit acceptance:** the analyzer tests pin the count rule at 1, 2 and 3 runs, the revert, and the
  set-plus-count transition; `DocumentOwnershipTests` pins the state machine (open, confirm, query, foreign
  document, unowned document, read, scope, background capture, the unknown owner, settle, outside a detached flow,
  work started inside one); the Cosmos and Mongo tracker tests pin the same sequences through the real trackers,
  the claim by filter, and the guard; the resolver, scope and expiry tests pin the provenance.
- **Consumer acceptance:** BreakfastProvider on 3.20.0, two dispatched runs: `behaviour-change: 0` on every
  docker and in-memory lane after the pin run, and no call set moved against 3.19.0 (the consumer's dispatcher is
  not tracked, so the window changes nothing there; the count rule changes only when a count alert would be raised).

## 6. Execution log

- 2026-09-15: written and executed as 3.20.0, after 3.19.0's four-run acceptance and the noise audit.
