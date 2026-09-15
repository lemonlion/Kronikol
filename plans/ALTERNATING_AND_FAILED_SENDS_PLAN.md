# Alternating verdicts, failed sends and the background heading

**Status:** written 2026-09-15 from the measured residue of `BACKGROUND_ATTRIBUTION_PLAN.md`; green-lit the same
day ("Ok, implement all of that"); shipped as **3.18.0**. §6 is the execution log.

## 1. What is left after 3.17.0

With hosted services detached and inherited identities expiring at scenario end, two consecutive runs of the
BreakfastProvider demo (34939097672 and 34939625141) read `nothing changed` on ten of the twelve docker and
in-memory lanes. The two residual `behaviour-changed` verdicts are not attribution, and the named evidence
says what they are:

- **ReqNRoll in docker**, the correlation-id scenario: `gone: Breakfast Provider>Google Cloud Pub/Sub Publish
  (Pub/Sub) /MenuAvailabilityChangedEvent` and the supplier availability call with it. Its own `GET /menu`
  builds the menu from the supplier and publishes the change when the five-minute menu cache is cold, and
  serves the cache otherwise. Two sets of calls, both the scenario's own; which one a run sees depends on
  ordering. The ledger shows the scenario flipping between one and three calls since the lane began.
- **ReqNRoll in memory**, the AsyncAPI scenario: `new: Caller>Breakfast Provider GET /asyncapi/v1.json -`. The
  first attempt throws `HttpRequestException: Error while copying content to a stream` out of the third-party
  `Bielu.AspNetCore.AsyncApi` (`Utf8BufferTextWriter.Flush` advances its pipe by an out-of-range count); the
  test's retry loop masks it, and the failed attempt stands in the report as a request without a response and
  without a reason. Finding the cause took a probe patch and a rerun.

Measured over the whole ledger, keyed by scenario id and with the analyzer's own rules applied (same
templating rule, both runs passing, the unstable-shape suppression):

| Runs | Verdicts flagged | Returns to a set the scenario had held |
|---|---|---|
| three steady-state runs before 3.17.0, twelve lanes | 51 | 29 |
| run 34939625141, after 3.17.0 | 2 | 1 (the correlation-id scenario, held in 4 of 7 prior runs) |

The AsyncAPI first-attempt failure has flipped as a return in six other lanes. A scenario with two states
therefore pays for both of them on every flip, for ever, under the reading rule as it stands.

## 2. The reading rule: `alternating`

A scenario whose current set of calls is one it held within the recent window, when a different set was held
in between, is **alternating**: a standing state, read out like `flaky`, never tripped as `behaviour-changed`.
The first sighting of each set is still `behaviour-changed`, because that is the moment the reader learns the
scenario has a second state; every later return is the same fact repeated.

Precisely, over the last `AlternatingRuns` (default 10) prior passing points fingerprinted by the current
templating rule: the current set appears earlier, and a set that differs from it appears after that earliest
occurrence. Both conditions matter. `A A A B B B` with the current run `B` is a change that stayed, not an
alternation; `A B A` is one; `A B A A A` still is, until `B` ages out of the window, after which the scenario
reads stable again. A set held only beyond the window is a change again, which is why the memory is short: a
regression back to how the scenario behaved months ago must read as a change, not as a known state.

Where it sits: evaluated before `unstable-shape`, which stays for the scenario whose set is new on most runs
(random data in a path, a fresh id each run) and cannot be alternating because nothing ever returns. It is not
a summary count (`nothing changed` stays true, as it does for `unstable-shape`), not a gate category, and it
takes precedence after `reordered` and before `unstable-shape`. The evidence names the state: `alternating
between 2 sets of calls over the last 7 runs: this set in 4 of them`, followed by `new:` and `gone:` against the
previous run when the set changed and both runs recorded their call lists.

Option: `ReportConfigurationOptions.HistoryAlternatingRuns` (10), `HistoryAnalysisOptions.AlternatingRuns`,
and `--alternating-runs` on `kronikol history gate` and `kronikol query history`, plumbed exactly like
`HistorySlowerMinMs`.

The consumer-side alternative, isolating shared state per scenario, is not available to the demo: its docker
lanes share one host across scenarios that run in parallel, so clearing the menu cache between scenarios would
race. The scenario's behaviour is genuine and correctly attributed; the reading rule is what was wrong.

## 3. Failed sends carry their exception

`TestTrackingMessageHandler.SendAsync` logs the request, awaits the inner handler and the body, and logs the
response. When either throws, nothing more is logged: the request stands alone, the template reads `-` for its
status, and no output says why. The same holds for `CosmosTrackingMessageHandler`.

Now a send that throws logs a Response record whose `StatusCode` is the string `!` followed by the exception's
type name (`!HttpRequestException`, `!TaskCanceledException` for a client timeout) and whose new `Error`
property carries the message chain (outer message, then each inner message). The exception is rethrown
untouched. Everything downstream follows from the existing status plumbing: `InteractionStatus.Split` gives
`(null, "!HttpRequestException")`, so the template reads `GET /asyncapi/v1.json !HttpRequestException`,
`IsError` is true, the diagram's return arrow carries the type, and the tool's listings print it. The outputs
gain `error` (JSON), `<Error>` (XML), `Error:` (YAML), with the schema and the XSD updated; `kronikol query
http` prints the error under the status; the failures digest shows the type in its status column. The other
trackers already record their failures (gRPC status, MassTransit faults, Mongo `CommandFailedEvent`, Event Hubs
exceptions); they are not changed here.

## 4. The background heading

The section's summary and note only describe calls that arrived after a scenario ended, but with
`RequestResponseLogger.CaptureBackground` on the same table also lists identity-less calls under `(no
scenario)`. The summary becomes `Background calls (N: M after a scenario ended, K with no scenario)` and the
note says both.

## 5. Acceptance

- The analyzer tests pin: a return within the window is `alternating` and not `behaviour-changed`; the state
  stands on the following runs; a first sighting is still `behaviour-changed`; a change that stays is neither;
  a set beyond the window is a change again; a two-state flipper is `alternating`, not `unstable-shape`; the
  name is `alternating`; the summary line does not count it.
- The handler tests pin: a throwing inner handler leaves a request and a response record, the response with
  `!InvalidOperationException` and the message; the report outputs carry `error`; the template token is
  `!Type`.
- BreakfastProvider on 3.19.0 (which includes this): the correlation-id scenario reads `alternating` and the
  ReqNRoll docker lane `nothing changed`; the AsyncAPI package bump removes the failed first attempt.

## 6. Execution log

- 2026-09-15: written after the 3.17.0 acceptance; executed the same day as 3.18.0:
  1. **`alternating`** in `HistoryAnalyzer` (before `unstable-shape`; failed runs excluded from the memory),
     `HistoryVerdictKind.Alternating`, name `alternating`, precedence after `reordered`,
     `HistoryAnalysisOptions.AlternatingRuns` / `ReportConfigurationOptions.HistoryAlternatingRuns` (10),
     `--alternating-runs` on `history gate` and `query history` (VerbTable, the skill reference in both
     copies), the pill's stylesheet class. Eight analyzer tests pin the rule.
  2. **Failed sends**: `RequestResponseLog.Error`, `FailedSend.Status`/`Describe`, the try/catch in
     `TestTrackingMessageHandler` and `CosmosTrackingMessageHandler`, `error` in JSON/XML/YAML/schema/XSD,
     the tool's `ReportIndex`/`ReportScanner` and `query http`. The failures digest shows the type in its
     status column; the message is in the report.
  3. **Heading**: `Background calls (N: M after a scenario ended, K with no scenario)`, the note says why a
     `(no scenario)` row is there.
