# BACKGROUND_ATTRIBUTION_PLAN.md — dependency calls made outside any scenario's own flow

**Status:** written 2026-09-14, **NOT green-lit**, nothing implemented. The cross-run history dogfood on
BreakfastProvider surfaced it (`CROSS_RUN_HISTORY_PLAN.md` §12.1, 3.14.0 and 3.15.0 entries).

## 1. The problem, with the evidence

A dependency call the SUT makes is attributed to the scenario that is running when it happens. That is
right when the call is part of the scenario's own request flow, and it is right for a message consumer
that handles *this* scenario's message before the scenario ends. It is wrong whenever background work
runs while another scenario is current: a consumer draining messages produced by earlier scenarios, a
hosted service, a retry loop finishing late.

Measured on BreakfastProvider run 34896168088, lane `xunit-in-docker`, scenario *Toppings should include
raspberries when feature flag is enabled*: one test-side request (`GET /toppings`) and **fourteen Cosmos
DB calls on the orders container** (4 Create, 4 Query, 6 Replace), each carrying its own trace id, none
of which is the trace of any scenario's own request in the run. The next run held eight of them and no
Replace. Across the docker and external-SUT lanes 1 to 20 scenarios a run carried such calls; the
in-memory lanes carried none, because the fakes deliver synchronously inside the producing scenario.

Consequences today: those scenarios' sequence and component diagrams show work they did not cause; the
history fingerprint sees a different set of calls run to run and reads it as `behaviour-changed` (3.15.0
made the count wobble harmless; a call class appearing and disappearing still trips); `unstable-shape`
mutes only scenarios that flip in most runs.

## 2. What attribution has to work with

- The test-side request carries the scenario's correlation (`ComponentTestRequestId` /
  `CorrelationId` headers in BreakfastProvider; `RequestResponseLog.TestId` in Kronikol) and, in-process,
  the scenario's `AsyncLocal` context flows into the server pipeline.
- A message produced inside that flow can carry the same correlation in its headers; a consumer that
  reads it starts new work whose `Activity` may be parented to the producer's (W3C `traceparent` in
  headers) or may not (the fourteen calls above had fresh trace ids).
- A hosted service's work has no scenario at all.

## 3. Options

| | Option | Effect | Cost |
|---|---|---|---|
| A | **Attribute by correlation, not by currency.** A dependency call is the scenario's only when its ambient context (AsyncLocal test id, or a correlation carried by the message being handled) names that scenario; a call with no scenario context goes to a `background` bucket, listed in the report but in no scenario. | Diagrams and fingerprints show only what the scenario caused; consumer work that carries the correlation still lands in the right scenario, even after it ended (recorded until the run ends). | The adapters that set the ambient scenario must stop falling back to "the current test"; the messaging extensions (Kafka, PubSub, EventHub) must propagate the correlation into the consumer's context; a report section for the bucket. Diagrams change for every consumer with async processing. |
| B | **Keep attribution, mark provenance.** Record on each interaction whether its context was the scenario's own flow or the ambient fallback; fingerprints exclude fallback calls; diagrams keep them, styled as background. | History becomes clean without changing diagrams. | The fallback still mis-attributes in diagrams; two truths in one report. |
| C | **Time-fence.** Attribute only calls that begin between the scenario's first and last own request. | Cheap. | Consumer work for this scenario usually lands after the last request; it would be lost. |

**Recommendation:** A, staged: (1) the provenance mark of B first (one field on the log, no behaviour
change, lets the history fingerprint exclude fallback calls immediately); (2) correlation propagation
through the messaging extensions; (3) the fallback removed and the `background` bucket added, behind an
option for one release.

## 4. Tests that would decide it

- A scenario whose message is consumed after it ended still owns the consumer's calls (correlation flows).
- Two scenarios in parallel; a consumer handling A's message while B is current: the calls are A's.
- A hosted service's call with no correlation lands in `background`, in no scenario, and does not enter
  any fingerprint.
- BreakfastProvider docker lanes: two consecutive runs with no code change read `nothing changed` on
  every lane (the acceptance test for the whole plan).

## 5. Open questions

1. Which of BreakfastProvider's consumers propagate the correlation today, and which start fresh traces?
2. Should `background` calls still count towards the component diagram's dependency edges? (Probably yes: the SUT does talk to that store.)
3. Kronikol4J parity: the Java port's attribution is manual; the provenance field is additive.
