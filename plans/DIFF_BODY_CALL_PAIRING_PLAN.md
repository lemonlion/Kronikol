# `diff --body` call pairing plan: #115

**Written:** 2026-10-06, at 4.6.0 (`37e93813`); #115 was filed on 2026-09-30 against the 4.0.1 tool, linking
`8762dff2`, and the lines it links are unchanged at `37e93813`. **Status: executed 2026-10-07 as 4.6.1**
(`ROADMAP.md` D36): the owner asked for the plan in full, so every question in §9 was taken as recommended; Q1 (§9)
shaped the work. The execution, and F19 found on the way, are in §11. Evidence labels: **RUN** (measured here), **READ** (in the source, `file:line` at `37e93813`),
**INFERRED** (reasoned from facts, stated by none), **ISSUE** (taken from #115, not re-measured). The scripts behind
every RUN line, and their output, are in [`DIFF_BODY_CALL_PAIRING_PLAN.harness/`](DIFF_BODY_CALL_PAIRING_PLAN.harness/README.md).

#115 reports that `kronikol query diff old.json new.json --body s3/i47` finds the scenario in the new run by `stableId`
and then takes the call with the same ordinal, so when a service makes its calls concurrently the diff compares two
different calls, prints a confident answer about the wrong pair and exits 0. It asks for the call to be paired by
what it is: the same service, method and URI, the n-th of them for the n-th when several share them, and a refusal
with exit 2 when nothing matches.

The defect is real and reproduces on 4.6.0 against two runs of one commit, three minutes apart (RUN). The ask is
right, and buildable. Measured on those two runs, though, its literal form refuses seven calls for every wrong pair
it fixes, because a URI that carries a generated id never matches across runs. This plan:

- **checks each claim** (§1) and **measures** the defect and the fix on BreakfastProvider's xUnit lane, two pairs of
  runs, 2,614 addresses that carry a body (§2);
- **lists what the issue does not say** (§3): the literal key's refusals (F1), the host and port in a URI (F2), the half
  of the call an address names (F3), a wrong pair that hides a change rather than inventing one (F4), and thirteen
  sibling defects on the same code path, among them a `--count` the run diff never reads and a `diff old new s3/i47`
  that forgot its `--body` and gets the whole run diff at exit 0 (F5 to F17), plus one tool-wide class left to its own
  patch (F18);
- **designs the pairing** (§4): the exact key first, then the same key with the URI templated the way the history
  fingerprint templates it, then the refusal. On both pairs that finds the right call wherever 4.6.0 took a
  different one, leaves every call 4.6.0 pairs correctly where it is, and refuses only calls the new run did not
  make.

It ships as one patch, 4.6.1 (§6.2).

## 0. Summary

| | |
|---|---|
| What ships | **R1, a patch (4.6.1):** `diff <old> <new> --body ADDR` pairs the call by what it is (§4.1) and refuses with exit 2 when nothing pairs; the answer names both calls, in text and in `--json`. On the same code path: both body-diff forms print the provenance notes every other verb prints, the two sides are labelled apart under `--baseline`, the run diff's own refusals apply to `--body`, `compare`'s "first differing body" uses the same pairing, the run diff answers `--count`, `diff` refuses a flag or a positional its chosen form does not read, and `--body` takes a value only for `diff` (F5 to F17) |
| The rule | The scenario as today (by `stableId`). The call by its request: the same service, method and URI (path and query), the n-th of the old scenario's calls with that key for the n-th of the new's; when the new scenario has no such call, the same with the URI templated (ids, timestamps and numbers as placeholders); then the same half of the paired call |
| What it refuses | A call the new scenario does not make under the same key or the same shape, or makes fewer times than the old one did (2 of 1,518 addresses in the same-version pair: a fourth query the new run did not repeat) |
| What it measured | Same-version pair: 4.6.0 pairs a different call for 13 of 1,518 addresses (12 confidently); the literal key refuses 96, 94 of which 4.6.0 pairs correctly; the recommended rule finds all 13 and refuses 2 (§2.2) |
| Bumps | Patch. No option, type or member is added; the JSON envelope gains members that name the two calls the answer compared (§6.2) |
| Open | Decided 2026-10-07, all as recommended (D36). Q1, the templated fallback, decided that 94 of 1,518 addresses pair rather than refuse. The leftovers (Q2 to Q5, Q7, Q10) are in `ROADMAP.md` Appendix C |

## 1. How far each claim was checked

| # | The issue says | Level | Verdict |
|---|---|---|---|
| C1 | The diff finds the scenario by `stableId`, then takes the call with the same ordinal | READ | **True.** `QueryCommand.Diff.cs:98-100` matches the scenario; `:107` and `:113` look the call up by `Ordinal == address.Interaction` on each side |
| C2 | A service that makes its calls concurrently records them in a different order on each run | RUN | **True, and requests reorder as well as responses.** In two runs of one commit, 5 of 212 scenarios hold the same calls in another order. BreakfastProvider's health check asks three services at once; run a logged Goat, Kitchen, Supplier and run b Goat, Supplier, Kitchen (`repro-4.6.0.txt` §1) |
| C3 | The diff does not check; the only check is that the ordinal exists | READ, RUN | **True, and it does not check the half either.** Across 3.20.0 and 3.27.0, `--body s10/i29` paired a *response* to `POST /orders` with a *request* to CosmosDB, then refused because the request had no body, naming `s10/i29` without saying which file (`repro-4.6.0.txt` §2) |
| C4 | The repro: a 369.5 KB cache write against a 63 B write to another key, 102 paths, exit 0 | ISSUE | Not re-measured (the owner's suite). Consistent with RUN: every wrong pair measured here exits 0 |
| C5 | Both scenarios have 44 calls, so nothing hints that the positions moved | RUN | **True, and the header hides it too:** both sides are labelled with the same ordinal (`s58/i4`, `s58/i4`), since the new side's label reuses the old address (`Diff.cs:128`) |
| C6 | `Diff.cs:80` says ordinals shift between runs and handles that for scenarios only | READ | **True** |
| C7 | The run diff refuses pairs it cannot match rather than guessing (since 3.6.0) | READ | **True** (`RefuseUnmatchable`, `QueryCommand.Search.cs:425-459`), and the `--body` path never calls it (F8) |
| C8 | Concurrent calls are ordinary | RUN, ISSUE | **True.** A real fan-out in the owner's demo app (C2), and the owner's own suite (`Task.WhenAll` over its caches) |
| C9 | Ask: same service, method and URI; the n-th for the n-th; no match says so and exits 2 | RUN | **Buildable, with three corrections.** The literal key refuses 94 calls 4.6.0 gets right (F1); "URI" has to be the path and query, not the host and port (F2); the pairing has to hold the half, and rank a response by its request (F3) |

## 2. Where it stands today

### 2.1 The code

`CrossRunBodyDiff` (`QueryCommand.Diff.cs:80-131`) is reached from `Diff` (`QueryCommand.Search.cs:620-621`) after both
reports are scanned and oriented, whether the old side came from the command line, `--baseline` or `--baseline-run`.
It parses the address, finds the old scenario by ordinal, finds the new one by `stableId` (the n-th holder for the
n-th, falling back to the first, `Diff.cs:96-100`), then looks the call up by the same ordinal on both sides
(`:107`, `:113`) and hands both bodies to `EmitBodyDiff` (`:133-183`). It returns before anything the run diff does
for its two sides: `RefuseUnmatchable` (`Search.cs:632`), `WriteSideProvenance` (`:640-641`) and `DiffLabels`
(`:645`).

An interaction address `sN/iM` is the M-th entry of the scenario's `httpInteractions` array, a request or a response
(`ReportScanner.cs:400-402`); listings fold the pair into one row under the request's address
(`QueryCommand.Shared.cs:16-34`). The two halves of a call carry one `requestResponseId`; `FindRequest`
(`Shared.cs:40-59`) and `FindResponse` (`QueryCommand.Narrative.cs:597-619`) pair them by it, with a four-entry
proximity scan for entries that carry none. `InteractionEntry` (`ReportIndex.cs:210-273`) holds `Type`, `ServiceName`,
`Method`, `Uri`, `CallerName`, `StepPath`, `DependencyCategory`, `BodyHash` and `BodyLength`, which is everything a
pairing key can use without reading a payload.

Kronikol already has a cross-run identity for a call: the history fingerprint's `ShapeCall` (`History/InteractionShape.cs:16-20`),
the caller, service, method and templated path and query, plus the templated statement head for a statement-shaped
dependency (`:219-230`). Its doc comment states the premise of #115: "parallel steps reorder calls run to run without
anything having changed" (`:25-26`). `InteractionShape.Template` is public and is what #117 asks `--group-by` to reuse.

`compare` (`Search.cs:215-282`) pairs two scenarios' calls by position for its "first differing body" line
(`:261-278`), the same rule in a sibling verb (F11).

### 2.2 The measurements

Two pairs of reports of BreakfastProvider's xUnit lane (in memory, nothing configured): **a**, two runs of one commit
(`106702b`, Kronikol 4.0.2) three minutes apart on one machine, 212 of 212 passing each time; **b**, two retained
runs on 3.20.0 and 3.27.0, three days apart, across an upgrade that records more calls. `research/pairing.py` reads
both reports and, for every interaction in a matched old scenario that carries a body (every address `--body`
accepts), records what each rule pairs it with (RUN; output in `research/results-*.txt`).

| | a: one commit, two runs | b: 3.20.0 to 3.27.0 |
|---|---|---|
| Scenarios matched by `stableId` | 212 | 203 |
| ...holding the same calls in another order | 5 | 4 |
| Addresses that carry a body | 1,518 | 1,096 |
| **4.6.0 (same ordinal)**: the same call | 1,409 | 1,016 |
| ...the same call with a regenerated id in its URI | 94 | 39 |
| ...**a different call** | **13** (12 with a body, diffed as if they were one call) | **41** (12 with a body) |
| ...beyond the new scenario's last call | 2 | 0 |
| **Literal key** (half, service, method, full URI): paired | 1,422 | 1,057 |
| ...refused | **96** (94 that 4.6.0 pairs correctly) | **39** (all 39 pair correctly today) |
| **Recommended rule** (§4.1): paired on the exact key | 1,422 | 1,057 |
| ...paired on the templated key | 94 | 39 |
| ...refused | 2 | 0 |
| ...the right call where 4.6.0 took a different one | 13 of 13 | 41 of 41 |
| ...a different call where 4.6.0's is right | 0 | 0 |
| Addresses whose key another call in the scenario shares (decided by the n-th rule) | 252 (197 statement-shaped) | 87 (62 statement-shaped) |
| ...where one key covers different statements | 9 | 9 |
| ...paired with a call whose request differs while a byte-identical one was there | 0 | 0 |
| Addresses with no `stepPath` (the lane records no steps) | 1,492 | 1,081 |

The 94 are 19 endpoints, each called with an id the run generated, led by `PATCH /orders/{guid}/status` (54 of the
94), then `/customer-preferences/{guid}`, `/chef-notes/{guid}`, `/chef-notes/recipe/Recipe-{32 hex}`,
`/audit-logs?entityId={guid}` and `/audit-logs?entityType=NonExistent_{number}` among others; pair b's 39 are 18 of the
same endpoints. The 2 refusals in pair a are one call: the old run of `s109` queried `CosmosDB QUERY /orders` four
times and the new run three times; both passed.

The rule was also measured two other ways, which changed nothing on these reports: the URI as path and query instead
of the full URI (the hosts are fixed in memory), and ranking a response through its request instead of among the
responses (0 differences).

## 3. Findings the issue does not state

**F1. The literal key refuses as many calls as it fixes, or more (RUN).** In pair a it refuses 96 addresses to fix 13;
in pair b, 39 to fix 41. 94 of pair a's 96, and all 39 of pair b's, are calls 4.6.0 pairs correctly today: the same
endpoint, with an id the test generated for that run. Ids, hashes and dates in a path or a key are ordinary (the
owner's #117 shows a cache key carrying an id, a hash and two dates). The history fingerprint already says what such
a call is: its URI through `InteractionShape.Template` (GUIDs, hex ids, ULIDs, ISO timestamps and bare numbers as
`{id}`, `{ts}` and `{n}`, query values dropped). Falling back to that key when the exact one finds nothing pairs all
94 and all 39, each with the call 4.6.0 pairs. It does not pair the issue's own two calls: `/app:charts-agg-…` and
`/app:competitor-charts-agg-…` keep different shapes
(`/app:charts-agg-{n}-{id}-Daily-OneMonth-{ts}-{ts}` and `/app:competitor-charts-agg-{n}-…`; the harness's port of the
templater, which reproduces the .NET output quoted in #117). Q1.

**F2. "URI" has to be the path and query (INFERRED).** A dependency in a container is reached on a host port the
container runtime picks per run (Testcontainers maps a random port unless told otherwise), so a full URI with a
host and port would refuse every call to it on every run. The service name already says which dependency it is. The
issue's own listing shows the path (`Set /app:charts-agg-…`), `InteractionEntry.ShortUri` prints it
(`ReportIndex.cs:266-272`), and the fingerprint keys on `PathAndQuery` (`InteractionShape.cs:221`). Not measurable on
the pairs above, whose hosts are fixed.

**F3. An address names one half of a call (READ, RUN).** The key must keep the half, or a request is diffed against a
response (C3). And a response should be ranked by the request it answers, not among the responses: responses are
logged as they complete, so two calls that share a key can complete in either order, and ranking each half on its own
could pair `s3/i10` with one call and its response `s3/i11` with the other. Measured, ranking through the request
changed nothing here (§2.2); the rule takes it so that a request and its response always pair with one call.

**F4. A wrong pair hides changes as well as inventing them (RUN).** On pair a, `--body s58/i4` compared Goat's health
response with Kitchen's and printed `byte-identical`, exit 0 (`repro-4.6.0.txt` §1). Had Kitchen's response changed,
the diff would have said it had not. The issue's case is the loud direction; this one is silent.

**F5. Neither body-diff form prints the provenance notes (RUN).** `WriteProvenance` returns early for `diff`
(`QueryCommand.cs:358-359`) so the run diff can label its notes by side once both reports are resolved
(`Search.cs:640-641`), but both body forms return before that point: the one-report form at `Search.cs:587`, the
cross-run form at `:621`. On a report written before step attribution, `interactions` prints `! report predates step
attribution…` and both `diff s0/i0 s0/i1` and `diff <r> <r> --body s0/i0` print nothing (`repro-4.6.0.txt` §4).
The notes that matter most here are `CaptureDegraded` and `DroppedUnattributed`: calls the run lost, which is
exactly when a call has no partner.

**F6. Under `--baseline` both sides read the same (RUN).** The cross-run labels use the file name
(`Diff.cs:126-129`), and under `--baseline` and `--baseline-run` both files are normally `TestRunReport.json`:
`- TestRunReport.json s58/i4` above `+ TestRunReport.json s58/i4` (`repro-4.6.0.txt` §3). The run diff fixed this for
itself with `DiffLabels` (`Search.cs:528-539`).

**F7. "carries no body" does not say which file (RUN).** `Diff.cs:120-124` names the scenario address only, and
`s10/i29` exists in both reports (C3).

**F8. The run diff's refusals do not apply to `--body` (READ).** A pair of reports whose ids were computed under
different suites, or where one side has no `stableId`s, gets `No scenario in new.json with stableId sid:…` instead of
the run diff's reason and remedy (`Search.cs:425-459`).

**F9. The scenario fallback is silent (READ).** When the new run holds fewer scenarios with the `stableId` than the old
one (a retry that did not happen again, a repeated row that now runs once), `Diff.cs:100` takes the first one without
saying so; the run diff lists the extra old holder under `Gone`. The pairing is reasonable (the same test); the
silence is the defect.

**F10. `diff <old> <new> --count` ignores `--count` (RUN).** The verb declares the flag (`VerbTable.cs:250`), the body
forms read it (`Diff.cs:139-143`, `:165-169`), and the run-diff branch never does: it prints the whole run diff and
exits 0 (`repro-4.6.0.txt` §5, where the cross-run body form also answers `0` for section 1's wrong pair). `CountFlagTests` covers the one-report body form only (`CountFlagTests.cs:44`). The same class as the 3.30.3
`flow`/`trace`/`compare` finding. With F5's notes, `--count` must also send them to stderr, as `WriteProvenance` does.

**F11. `compare`'s "first differing body" pairs by position (READ).** `Search.cs:261-278` walks the two scenarios'
calls in step and suggests the first pair whose bodies differ. Under a fan-out it can suggest `diff sA/iN sB/iN` for
two different calls. Not seen on the measured suite (`compare s58 s59` suggests the right pair, because the first call
is stable there; RUN, `repro-4.6.0.txt` §6). Two scenarios of one run often differ in their data (an id each, another row of an outline), so
the exact key may pair few of their calls and the templated key carries the comparison (INFERRED; Q1).

**F12. The exit code table does not list unmatchable pairs (READ).** Since 3.6.0 the run diff exits 2 for a pair it
cannot match; `VerbTable.ExitCodes` (`:309-315`, printed by `--describe` and `--help`) and the wiki's table say 2 is
bad usage, an address or a flag, and nothing about a pair. This release adds a second such refusal.

F13 to F18 were found on a second pass, when the owner asked whether every defect was in the plan: a probe of every
way a flag or a positional can reach a branch of `diff` that does not read it (`repro-4.6.0.txt` §7 to §10).

**F13. `diff` ignores what the form it chose does not read (RUN, §7 and §10).** `Diff` picks its form from the first positional
(`Search.cs:586-587`) and reads nothing else the other forms take. Measured on 4.6.0, each at exit 0:

- `diff old.json new.json s58/i4`, the natural slip when someone wants #115's answer and forgets `--body`, prints the
  whole run diff and never mentions the call;
- a third report, `diff a.json b.json c.json`, and a third address, `diff r.json s0/i0 s0/i1 s0/i2`, are dropped;
- beside two addresses, `--body s0/i0` and `--baseline` are dropped (`--baseline-run` is refused there, `:578-582`);
- beside two reports, a bare `--body` is dropped;
- beside `--baseline`, a second report is dropped and the baseline is diffed instead.

The per-verb flag check (`QueryCommand.cs:307-333`) cannot see any of this: every one of those flags is legal for
`diff`, just not for the form it chose.

**F14. `--body`'s value form reaches `http`, which never reads it (RUN, §9).** `QueryOptions.Parse` does not know the
verb (`QueryCommand.cs:114`), so `--body` followed by an address always takes it as `diff`'s value
(`QueryOptions.cs:293-304`). `http r.json --body s0/i0`, flags first, swallows the address and answers "Which
interaction?" (exit 2); `http r.json s0/i0 --body s0/i1` prints the call without the body `--body` asked for (exit 0).

**F15. The parser offers a `--body` value nothing takes (RUN, §7).** Its refusal says "Give it s3/i47 (a call) or
b:4bdea521 (a body)" (`QueryOptions.cs:300`), and the only reader of the value refuses a `b:` address
(`Diff.cs:84-88`). A body hash names bytes, not a call, so it cannot be followed into another run;
`diff <report> b:x b:y` is the form that compares two bodies.

**F16. Two reports without `stableId`s refuse `--body` with an empty id (RUN, §7).** The run diff matches such a pair
by position and says so; `--body` refuses with `No scenario in noids.json with stableId sid: (Checkout).`, an
address with nothing after the prefix. Reports written before 3.0.47 (Q11).

**F17. The run diff ignores `--offset` and `--limit` (RUN, §8).** Both are declared for `diff` and read only by the body
forms. With and without `--limit 1` the text is byte-identical, and `--json` carries 25 items, `total` and `next`
null, either way. The text cuts its sections at 15 rows (`… 19 more`, `Search.cs:787-790`) with no way to the rest
except `--json`, which the line does not mention, and cuts `Gone` at 10 (`:727`) without a line at all, only the count
in its heading (Q9).

**F18. A verb that reads one positional drops the rest, tool-wide (RUN, §9).** `http r.json s0/i0 s0/i1` answers for
`s0/i0` alone at exit 0. This plan fixes it for `diff` (F13) and for `http`, where F14 would otherwise turn a dropped
`--body` value into a dropped positional; the other verbs are Q10.

**F19. As first measured, the shape tier could give one new call to two old ones (READ, found while building it).**
`pairing.py`'s `call_pair` with `shape` ranked a call among every call with its shape, including the calls the exact key
had already paired. Old `[GET /orders/A (seeded), GET /orders/B (created)]` against new `[GET /orders/C (created),
GET /orders/A (seeded)]`: `A` pairs with `A` on the exact key, and `B`, the 2nd of the two `/orders/{id}` calls, paired
with the new run's 2nd, which is `A` again, where the call the new run created, `C`, is its partner. The shape tier now
ranks only the calls the exact key leaves unpaired on each side, so the pairing is one to one. Re-measured on both pairs
of §2.2 it moves no address (the case needs a fixed id beside a generated one on one endpoint); a fact holds it (T30).

## 4. The design

### 4.1 The rule

1. **The scenario**, as today: the old scenario by its ordinal, the new by `stableId`, the n-th holder for the n-th. When
   the new run has fewer holders, the first, and a note says so (F9). F8's refusals come first.
2. **The old entry** by its ordinal, as today. A malformed or out-of-range address is refused as today.
3. **The call.** A request is its own call; a response is the call of the request it answers (`FindRequest`). An entry
   with no request to rank by (a response nothing pairs with) is ranked among the entries of its own half instead.
4. **The exact key**: the service (ordinal comparison), the method (ignoring case, as `--method` compares it) and the
   target, which is the URI's path and query (`Uri.PathAndQuery` for an absolute URI, the URI as written otherwise).
   `n` is the call's position among the old scenario's requests with that key, in file order; the partner is the n-th
   of the new scenario's requests with that key.
5. **The templated key** (Q1), only when step 4 pairs nothing: the target through `InteractionShape.Template` with no
   consumer rules (the report does not carry them; Q7), `n` and the partner counted the same way, over the calls step 4
   leaves unpaired on each side, so no call is the partner of two (F19).
6. **The half.** For a request address the partner request; for a response address the partner's response
   (`FindResponse`). A partner call with no response recorded is refused.
7. **Bodies.** A side without a body is refused as today, naming its file (F7).

Steps 4 and 5 are `research/pairing.py`'s `call_pair` with `key_path` and then `shape`, which produced the numbers in
§2.2. The harness is the executable spec: §6.1 S6 runs it against the built tool.

### 4.2 What the answer says

Text, when the pair is found (the second label is the partner's own address, so a moved call is visible):

```
- old.json s106/i35  b:1a2b3c4d  369.5 KB
+ new.json s109/i41  b:7c8d9e0f  369.1 KB
call: Redis Set /app:charts-agg-…  (request)

$.Items[3].Total: 412 → 415
…
```

The labels go through `DiffLabels` (F6). Notes, one line each, through `writer.Note` (stderr under `--count`, the
envelope's `notes` under `--json`):

- the templated tier: `! no call in s109 has that URI; matched on its shape /chef-notes/{id} (the URIs differ in what looks like an id)`
- a shared key: `! 3 calls in s106 share Redis Set /app:x (this is the 2nd); s109 has 3, matched in order`, worded like
  the run diff's `! N scenarios share a stableId (repeated rows or retries) — matched in order`
- the scenario fallback: `! s4 is the 2nd of 2 scenarios with sid:… in old.json; new.json has 1, compared with it`
- the provenance notes of each side, prefixed `old:` and `new:` as the run diff prefixes them (F5)

When nothing pairs, exit 2, stderr only (no answer on stdout, the `--json` error envelope as for every refusal):

```
No call in new.json s109 matches old.json s106/i35, Redis Set /app:charts-agg-…: s109 makes no Redis Set call to that URI or to one shaped like it.
s109's Redis Set calls: s109/i35 /app:competitor-charts-agg-…, s109/i37 /app:metrics-agg-… (2 of 6)
`interactions new.json s109 --service Redis` lists them; `http <report> <address> --body --out FILE` saves either body
```

and for a call the new run makes fewer times: `s109 makes 3 CosmosDB QUERY /orders calls; old.json s109/i14 is the 4th
of 4.`

`--json` on success gains three envelope members beside `items`, as the run diff carries `left` and `right`
(`QueryJsonTests.cs:445-447`): `left` and `right`, each `{ report, address, half, service, method, uri, bodyHash,
bodyLength }`, and `pairing`, `{ on: "uri" | "shape", position, old, new, shape }` (`shape` only on the templated
tier). The one-report body diff's JSON is unchanged: both of its addresses are the command's own.

### 4.3 Where it goes

| File | Change |
|---|---|
| `src/Kronikol/Query/CallPairing.cs` (new, internal) | The rule: `Pair(oldScenario, oldEntry, newScenario)` returns the partner or the reason there is none, the tier, the position, both counts and the candidates for the refusal |
| `src/Kronikol/Query/QueryCommand.Diff.cs` | `CrossRunBodyDiff` resolves through `CallPairing`, refuses per §4.2, writes notes, labels and `left`/`right`/`pairing`; `BodyDiff` writes the report's provenance notes; the type summary (`:6-11`) and `CrossRunBodyDiff`'s (`:80`) say how the call is matched |
| `src/Kronikol/Query/QueryCommand.Search.cs` | `Diff` hands `--body` the run diff's `RefuseUnmatchable` and `DiffLabels`; the run-diff branch answers `--count` with the number of `items` and routes its notes to stderr under it (Q6); each form refuses what it does not read (§4.4, F13, F17); two reports without ids match the scenario by position (F16, Q11); `Compare` takes its hint from `CallPairing` |
| `src/Kronikol/Query/QueryOptions.cs` | `Parse` takes the verb (the class is internal, so no public signature changes): `--body` takes a value for `diff` alone, and that value must be a call address, with a hint that offers only that (F14, F15) |
| `src/Kronikol/Query/QueryCommand.Payloads.cs` | `http` refuses a second positional (F18's one verb here) |
| `src/Kronikol/Query/QueryCommand.Narrative.cs`, `QueryCommand.Shared.cs` | `FindResponse` moves beside `FindRequest` as `internal` (it is `private` in another partial today) |
| `src/Kronikol/Query/QueryCommand.cs` | `Parse` is called with the verb (`:114`); `WriteProvenance`'s comment (`:355-357`) says which branches write their own header |
| `src/Kronikol/Query/VerbTable.cs` | The `--body` flag text (`:82`), the `diff` usage line (`:253`), and exit code 2's meaning (`:314`): "...or a pair of runs, or a call, that `diff` cannot match" (F12) |

`src/Kronikol/Query` compiles for `net10.0` only (`Kronikol.csproj`); nothing under `Reports/` may call it.

### 4.4 What each form of `diff` reads (F13 to F17)

`Diff` decides its form before it reads anything, as today, and then refuses with exit 2, before a line is printed,
anything that form does not read, naming it and the form that would:

| Form | Reads | Refused, with the way on |
|---|---|---|
| `<report> A B` (two bodies in one report) | `--count`, `--offset`, `--limit`, `--json` | `--baseline`, `--baseline-run` (refused today), `--body` in either form, a third positional |
| `<old> <new>` (the run diff) | `--count` (Q6), `--json` | `--offset` and `--limit` (Q9), a bare `--body` ("`--body` names the call: `--body s3/i47`"), a third positional; when that positional is a call address, the refusal names `diff <old> <new> --body <it>` |
| `<old> <new> --body ADDR`, and `--baseline` or `--baseline-run` with `--body` | `--count`, `--offset`, `--limit`, `--json` | a third positional |
| `<report> --baseline`, `<report> --baseline-run ID` | as the run diff | as the run diff; a positional beside `--baseline` gets the message `--baseline-run` gives today (`Search.cs:580`) |

The run diff's `… N more` lines say `--json` lists every row, and `Gone` gets one when it is cut.

`--body`'s value form exists for `diff` alone: `Parse` is told the verb, so for any other verb `--body` is the bare flag
and the next token stays a positional (`http r.json --body s0/i0` prints the body). The value must be a call address;
a `b:` value is refused with the reason (a hash names bytes, not a call) and the form that compares two bodies.

Two reports with no `stableId` on either side match the scenario by position under `--body`, as the run diff matches
them, with the run diff's note (`Search.cs:656`); the call is then paired by §4.1, which refuses rather than diffs when
the position landed on another test.

## 5. Tests, red first

Every fact is written first and run against `v4.6.0` in a worktree (§6.6) before the code that turns it green. The
fixtures are reports written through the real writer (`QueryCommandTests.Write`), with calls given real GUID
`requestResponseId`s, so the scanner pairs halves as it does on a run.

| # | Fact | File | Red on 4.6.0 because |
|---|---|---|---|
| T1 | Two runs whose scenario holds `Redis Set /a` and `Redis Set /b` in swapped order: `--body` on `/a`'s request diffs it with the new run's `/a`, and the `+` label carries the new ordinal | `Tool/DiffBodyPairingTests.cs` (new) | it diffs `/a` with `/b` |
| T2 | The same for a response address: the response of the paired call, not the response at the same ordinal | same | it diffs the other call's response |
| T3 | A request and its response pair with the two halves of one call (`requestResponseId` agrees) when the responses completed in the other order | same | it pairs by ordinal |
| T4 | Three calls share a key and sit at other positions: the 2nd pairs with the 2nd; the note names 3 and 3 | same | wrong call; no note |
| T5 | The new scenario makes no call with the key or the shape: exit 2, stdout empty, stderr names both files, the call and the new scenario's calls to the same service and method | same | exit 0 with a diff of whatever sits at the ordinal |
| T6 | The new scenario makes the call fewer times: exit 2 with both counts | same | it diffs the call at the ordinal |
| T7 | A URI whose GUID changed, in a scenario whose calls moved: paired on the shape, with the shape note (if Q1 is taken; if not, T7 asserts exit 2) | same | it diffs a different call; no note |
| T8 | The same call at a different host port: paired on the exact key, no shape note | same | it pairs by ordinal |
| T9 | `--json`: `left`, `right` and `pairing` name both calls; a refusal is the error envelope with exit 2 and the candidates in `hint` | `Tool/QueryJsonTests.cs` | members absent; exit 0 |
| T10 | `--count`: a paired call answers the number of differing paths; a refusal exits 2 with nothing on stdout; the provenance notes go to stderr | `Tool/CountFlagTests.cs` (the cross-run form joins the theory) | it counts the wrong pair |
| T11 | Both body-diff forms print each side's provenance notes (`! old: report predates…`) on a report written before step attribution | `Tool/QueryCommandTests.cs` | no notes |
| T12 | Under `--baseline` and under `--baseline-run`, the two labels differ by their directories | `Tool/BaselineDiffTests.cs` | identical labels |
| T13 | "carries no body" names the file it is about | `Tool/DiffBodyPairingTests.cs` | it names the address only |
| T14 | `--body` across two suites' ids, and across a report without `stableId`s, refuses with the run diff's reason | same | "No scenario … with stableId" |
| T15 | Two old holders of a `stableId` against one new: paired with it, with the scenario note | same | no note |
| T16 | `diff <old> <new> --count` prints one number, the count of `items`, and exits 0 | `Tool/CountFlagTests.cs` | the whole run diff |
| T17 | `compare` over two scenarios whose calls are reordered suggests a pair of the same call | `Tool/QueryCommandTests.cs` | it suggests two different calls |
| T18 | The existing `Diff_across_runs_matches_the_scenario_by_stableId` still passes, and its fixture's calls now also move (the shifted fixture gains a reordered call) | `Tool/QueryCommandTests.cs:1274-1280` | the reordered half is new |
| T19 | `SkillDriftTests.Every_banner_the_tool_can_print_is_explained_in_the_skill` and `The_two_copies_of_the_skill_are_the_same_files` pass with the new notes | existing | red as soon as a note is added and the skill is not |
| T20 | F4: a wrong pair whose bodies are byte-identical while the right pair's differ: the diff reports the difference | `Tool/DiffBodyPairingTests.cs` | `byte-identical` |
| T21 | F12: `--describe`'s exit code 2 names a pair `diff` cannot match | `Tool/DescribeTests.cs` | absent |
| T22 | F13, two bodies in one report: `--baseline`, `--body s0/i0`, a bare `--body` and a third address each exit 2 with nothing on stdout, naming what was refused | `Tool/DiffFormsTests.cs` (new) | exit 0, the flag or address dropped |
| T23 | F13, the run diff: `diff old new s3/i47` exits 2 and names `diff old new --body s3/i47`; a third report and a bare `--body` exit 2 | same | exit 0 with the whole run diff |
| T24 | F13, `--baseline`: a second report beside it exits 2 | same | exit 0, the baseline diffed |
| T25 | F14: `http r --body s0/i0` prints that call's body; `http r s0/i0 --body s0/i1` and `http r s0/i0 s0/i1` exit 2 | `Tool/QueryCommandTests.cs` | "Which interaction?"; exit 0 with no body; exit 0 for `s0/i0` alone |
| T26 | F15: `diff old new --body b:x` exits 2 with the reason, and a malformed `--body` value's hint offers `s3/i47` only | `Tool/DiffFormsTests.cs` | the hint offers `b:` |
| T27 | F16: two reports without `stableId`s: `--body s0/i0` pairs the scenario by position, with the run diff's note | same | exit 2, `sid: ` with no id |
| T28 | F17: `--offset` or `--limit` on the run diff exits 2; a section cut at 15 rows, and `Gone` cut at 10, says `--json` lists every row | same | exit 0, both dropped; no line for `Gone` |
| T29 | F3: two calls sharing a key, sequential in the old run and concurrent in the new: a request pairs with the request, never with a response (added in execution: without it, dropping the half from the key survived every fact) | `Tool/DiffBodyPairingTests.cs` | it diffs the request with the first call's response |
| T30 | F19: a seeded id beside a generated one on one endpoint: the generated call pairs with the new run's generated call, not with the seeded call another call already pairs with | same | it diffs the call at the ordinal, the seeded one |

Mutations (each must turn at least one fact red, run in a `git stash create` snapshot worktree, as the 4.6.0 harness
did): rank by ordinal again; drop the half from the key; rank a response among responses; skip the templated tier;
try the templated tier first; take the first candidate instead of the n-th; key on the full URI; omit the tie note;
omit the shape note; label the new side with the old ordinal; accept a third positional again; give `--body` its value
for every verb again; let the run diff read `--limit` without paging.

Every finding, where it is fixed, and the facts that hold it:

| Finding | Fixed in | Facts |
|---|---|---|
| #115: the call paired by its ordinal (C1, C3, C5) | §4.1 | T1, T4 to T6, T18; S6 on 2,614 real addresses |
| F1 the literal key refuses calls with a regenerated id | §4.1 step 5 (Q1) | T7 |
| F2 a host and port in the URI | §4.1 step 4 | T8 |
| F3 the half; a response ranked by its request | §4.1 steps 3 and 6 | T2, T3 |
| F4 a wrong pair hides a change | §4.1 (no fix of its own) | T20 |
| The answer does not name the calls it compared | §4.2 | T1 (label), T9 (JSON) |
| F5 no provenance notes on either body form | §4.2, §4.3 | T10, T11 |
| F6 one label for both sides under `--baseline` | §4.2 | T12 |
| F7 "carries no body" names no file | §4.1 step 7 | T13 |
| F8 the run diff's refusals skipped | §4.1 step 1 | T14 |
| F9 the scenario fallback is silent | §4.2 (Q8) | T15 |
| F10 the run diff ignores `--count` | §4.3 (Q6) | T16 |
| F11 `compare`'s hint pairs by position | §4.3 | T17 |
| F12 exit code 2 omits unmatchable pairs | §4.3 | T21 |
| F13 `diff` drops what its form does not read | §4.4 | T22 to T24 |
| F14 `--body`'s value reaches `http` | §4.4 | T25 |
| F15 the hint offers a `b:` value nothing takes | §4.4 | T26 |
| F16 two reports without ids refuse with an empty `sid:` | §4.4 (Q11) | T27 |
| F17 the run diff ignores `--offset` and `--limit` | §4.4 (Q9) | T28 |
| F18 extra positionals dropped | §4.4 for `diff` and `http` | T22, T23, T25; the other verbs are Q10, a later patch |
| F19 one new call the shape partner of two old ones | §4.1 step 5 | T30 |
| The new notes need the skill | S5 | T19 |

Left unfixed on purpose, each with its reason in §9: the statement head in the key (Q2, no mis-pair measured), a flag
to name the new side (Q3, a new flag), the direction `--baseline` resolves an address (Q4, documented behaviour),
`compare`'s `calls:` list (Q5, says nothing false), consumer templating rules (Q7, with #117), the other verbs'
extra positionals (Q10).

## 6. Slices, releases and records

### 6.1 Slices

| Slice | What | Facts |
|---|---|---|
| S1 | `CallPairing` and the cross-run rule, refusals and labels | T1 to T8, T13, T18, T20 |
| S2 | JSON members, `--count` on both diff forms, provenance on both body forms, the run diff's refusals on `--body`, the scenario note | T9 to T12, T14 to T16 |
| S3 | What each form of `diff` reads, `--body`'s parse, two reports without ids (§4.4) | T22 to T28 |
| S4 | `compare`'s hint through `CallPairing` | T17 |
| S5 | Docs: `VerbTable` (exit code 2 among them), XML doc comments, the skill (both copies, the new notes in SKILL.md's banner list), the wiki, the changelog | T19, T21 |
| S6 | Acceptance on real reports: the built tool's `--json` pairing (`right.address`) against `pairing.py`'s recommended rule for every address of both pairs (2,614); then the same script against 4.6.0 as the control, whose differences must be exactly the 13 and 41 wrong pairs and the 2 refusals | harness |
| S7 | Producers: the rule over two runs from each other writer of the records it reads, Kronikol4J (`parity-harness`), `kronikol ingest` and `kronikol merge`; anything other than "pairs or refuses with its reason" is a finding, recorded, not folded in | harness |

### 6.2 Release

**4.6.1, a patch.** Every change makes an existing answer correct or say what it is: no option, public type or member
is added. The refusal changes behaviour (a call that 4.6.0 diffed now exits 2), which `CLAUDE.md` keeps a patch and
asks the changelog to call out. The new JSON members name the two calls the answer already compared, as the run diff's
`left` and `right` do. If Q3 is taken, the flag makes the release 4.7.0.

`CLAUDE.md`'s release steps apply: every package to 4.6.1; the templates pin 4.6.0 (pins track the published release,
`PluginManifestTests`); `templates/github-actions/kronikol-history/VERSION` installs 4.6.1; `.claude-plugin/plugin.json`
and `marketplace.json` move with `Directory.Build.props`. `release.slnf` builds in Release for every target before the
tag. The issue is closed by `gh issue close 115` with the release named, after nuget.org lists 4.6.1; the commit
message says "#115" only where it means it.

### 6.3 Changelog draft

> ## [4.6.1] - 2026-10-DD
>
> A patch: `kronikol query diff` bug fixes, nothing new to call (the version's third part moved).
>
> - **`diff <old> <new> --body ADDR` pairs the call by what it is, not by its position** (#115). It took the call with
>   the same ordinal in the new run's scenario, so where a service makes its calls concurrently, and their order
>   changes from run to run, it compared two different calls: measured on two runs of one commit, 13 of 1,518
>   addresses, including a health check whose responses from two services were reported `byte-identical`. The call is
>   now the one with the same service, method and URI (path and query); the n-th of several for the n-th, said in a
>   note; when no call has that URI, the one whose URI differs only in what looks like an id, said in a note. A
>   response is paired through its request. **When no call pairs, the diff exits 2** and lists the calls the new
>   scenario makes to that service, where 4.6.0 diffed whatever sat at the ordinal. The `+` label carries the paired
>   call's own address, a `call:` line names it, and `--json` names both calls (`left`, `right`, `pairing`).
> - Both body-diff forms print the provenance notes every other verb prints; they printed none.
> - Under `--baseline` and `--baseline-run` the two sides of `--body` are labelled apart; both read `TestRunReport.json`.
> - `--body` refuses a pair of reports the run diff refuses (ids from different suites, one side without ids) with the
>   run diff's reason, and "carries no body" names the file it is about.
> - `diff <old> <new> --count` prints the number of changes the run diff lists; it printed the whole run diff.
> - `compare`'s "first differing body" pairs the two scenarios' calls the same way; it paired them by position.
> - **`diff` refuses, with exit 2, a flag or a positional the form it chose does not read**, where it dropped them at
>   exit 0: `diff old.json new.json s3/i47` printed the whole run diff and now names `--body s3/i47`; a third report or
>   address, `--baseline` or `--body` beside two addresses, a second report beside `--baseline`, a bare `--body` beside
>   two reports, and `--offset` or `--limit` on the run diff were all ignored. The run diff's cut sections, `Gone`
>   among them, say `--json` lists every row.
> - `--body` takes a value only for `diff`: `http report.json --body s3/i47` prints the body, where it answered "Which
>   interaction?", and `http` refuses a second address, which it ignored. A `b:` value for `diff --body` is refused
>   with the reason; the hint no longer offers it.
> - `--body` across two reports without `stableId`s pairs the scenario by position, as the run diff does, where it
>   refused with an empty `sid:`.
> - Exit code 2's description names a pair `diff` cannot match.

### 6.4 Wiki

`Querying-Reports.md`: the usage comment at `:839`; the `--body` paragraph at `:871-873` becomes a short subsection on
how the call is found (the key, the shape fallback, the tie and shape notes, the refusal and what to do next); `:920-921`
(`--baseline` composes with `--body`) gains that the address resolves in the baseline, as now; the exit code table at
`:1251` gains the unmatchable pair; the `--count` sentence for `diff`, wherever the page lists what `--count` counts;
the forms of `diff` and what each refuses (§4.4), beside the usage block at `:837-842`; `http`'s `--body` before or
after the address. Grep the wiki for `--body s` and `diff --body` for any page this list missed.

### 6.5 Kronikol4J and consumers

Nothing for Kronikol4J: it has no query tool (its README: "no `kronikol query`") and the report format does not
change, so its divergence ledger gets no line. Its reports are read by this tool, which S7 covers. Nothing changes for
BreakfastProvider; its own `query.cs` picks the engine up with its next Kronikol pin.

### 6.6 Before declaring done

The sweep in the plan execution checklist, and specifically: every T-fact red on `v4.6.0` in a worktree, each failure
read for its reason; the mutations of §5; S6 and its control; S7; the XML doc comments of every member touched read for
sentences about ordinal matching; the skill's banner list; `release.slnf` in Release; the CI run of the pushed commit
read job by job (`gh run list --commit <sha>`); this plan's §11 log and `PLANS_STATUS.md` row updated; `ROADMAP.md` row
1.17 struck with the release.

## 7. Where it sits in the roadmap

Stage 1, the patch train, as row **1.17**, by rule 1 (a live defect in shipped code goes ahead of the launch). It
depends on nothing. #117 (a `shape` dimension for `--group-by`) reuses the same templater and, if it ships first or
with Q7, would decide whether consumer rules reach the query engine; neither waits for the other.

## 8. Found on the way, not in this plan

- `compare`'s `calls:` list compares the two scenarios' calls position by position, so a reorder reads as a page of
  differences (`compare s58 s59`: three differing rows, two of them Goat and Kitchen trading places; `repro-4.6.0.txt`
  §6). It says nothing false; Q5.
- `--baseline` resolves the `--body` address in the baseline, as the wiki documents (`:920`), while every address a run
  hands out (`Failures.md`, `failures`, `interactions`) is the current run's. Q4.
- Consumer `HistoryShapeTemplates` live only in the run's options (#117 says the same); the templated tier uses the
  built-in rules alone. Q7.
- F18 beyond `diff` and `http`: every other verb that reads one positional drops the rest. Q10.

## 9. Questions, with recommendations

| # | Question | Recommendation |
|---|---|---|
| Q1 | The templated fallback (§4.1 step 5), or the literal key alone | **The fallback.** The literal key alone refuses 94 of 1,518 addresses that 4.6.0 pairs correctly, to fix 13 (§2.2); the fallback pairs all 94 with the call 4.6.0 pairs, refuses only what the new run did not do, and says when it was used. It keeps the issue's own two keys apart (F1). It is one function call on a rule Kronikol already ships and versions |
| Q2 | The templated statement head in the key for statement-shaped dependencies, as the history fingerprint has it | **Not now.** Nine addresses per pair share one key across different statements (ClickHouse, whose method is empty and URI `clickhouse:///`), and the n-th rule never paired one with another statement while its own was there (§2.2). Take it if a fan-out of queries shows the n-th rule wrong |
| Q3 | A way to name the new side when nothing pairs (`--body s3/i47 --to s9/i50`) | **Not now.** A new flag, so a minor. The refusal lists the candidates and the `http … --body --out` route to both bodies |
| Q4 | Under `--baseline` and `--baseline-run`, resolve the address in the current run instead of the baseline | **Keep the documented direction for this patch.** It is behaviour people may script against; after F6 the `-` label shows it is the baseline. Revisit with v5 |
| Q5 | `compare`'s `calls:` list: say "the same calls in another order" instead of listing a reorder as differences | **Not in this plan.** A display change; nothing it says is false |
| Q6 | What `diff <old> <new> --count` counts | **The rows `items` holds** (broke, fixed, new, slower, gone), the same number `--json` gives as the length of `items`; refusing the flag on the run form is the alternative |
| Q7 | Consumer `HistoryShapeTemplates` in the templated tier | **With #117.** The report would have to carry the rules; the built-in rules paired all 94 here |
| Q8 | The scenario fallback (F9): pair with the first holder and say so, or refuse as the run diff counts it `Gone` | **Pair and say so.** It is the same test, and refusing would remove an answer people get today |
| Q9 | The run diff's `--offset` and `--limit` (F17): refuse them, or page the run diff's rows | **Refuse them, and point the cut sections at `--json`.** The JSON is uncapped on purpose (`Search.cs:760-762`), and paging sections that render under five headings is a feature, not a fix |
| Q10 | Extra positionals on the other verbs (F18) | **A separate patch after this one,** built like the flag check: each verb's positionals in `VerbTable`, one refusal for all. It needs a census of what every verb reads first, which this plan did not take |
| Q11 | `--body` across two reports without `stableId`s (F16): by position, or refuse | **By position, with the run diff's note.** It is what the run diff does for the same pair, and the call pairing refuses if the position lands on another test |

## 10. Assumption ledger

| # | Assumption | Level | What would change |
|---|---|---|---|
| A1 | BreakfastProvider's xUnit lane is representative of how often URIs carry generated ids and how often calls reorder | INFERRED | On a suite with more fan-out the 13 grows; on one with fixed test data the 94 shrinks. Neither changes the rule, only Q1's weight |
| A2 | A container's host port changes per run on the suites this serves (F2) | INFERRED (Testcontainers' default) | If every suite fixed its ports, the full URI and the path would pair alike, and F2 would be moot, not wrong |
| A3 | `FindRequest` finds the request of every response a current writer emits | RUN for the .NET writer (all 4,598 entries of two of the four measured reports carry a `requestResponseId`) | A writer that drops the id falls back to the proximity scan and then to ranking the half (§4.1 step 3); S7 checks the writers |
| A4 | The owner's own case pairs under the rule | ISSUE (keys obfuscated) | If the owner's keys carry values that change between runs and no id-like token, the exact and the templated keys both miss and the diff refuses; the refusal lists the candidates (§4.2) |
| A5 | The JSON members are part of a patch | INFERRED from `CLAUDE.md`'s rules | If the owner counts envelope members as new surface, 4.7.0 |

## 11. Log

- 2026-10-06: written at 4.6.0 (`37e93813`). Reproduced on 4.6.0 against two same-commit runs and two runs across an
  upgrade (RUN, `repro-4.6.0.txt`); measured with `research/pairing.py` (`results-*.txt`). Not green-lit.
- 2026-10-06, later: the owner asked whether every defect was in the plan. Each finding was traced to its fix and its
  fact (F4 and F12 had no fact: T20, T21), and a probe of every way a flag or a positional reaches a branch of `diff`
  that does not read it found F13 to F18 (RUN, `repro-4.6.0.txt` §7 to §10). Added with §4.4, T22 to T28, slice S3,
  Q9 to Q11; the count of sibling defects in the summary, `PLANS_STATUS.md` and `ROADMAP.md` row 1.17 moved from eight
  to thirteen.
- 2026-10-07: the owner asked for the plan to be completed in full, in a worktree of its own (`fix/115-diff-body-pairing`
  off `37e93813`), so every question in §9 was taken as recommended: `ROADMAP.md` D36. Built as one patch, 4.6.1.
  - **Red first.** T1 to T28 were written first, through the CLI, and run against the tag in a worktree at `v4.6.0`:
    47 facts failed, each read for its reason, and 22 passed, all existing `CountFlagTests` facts and the new theory
    row for the cross-run form, which 4.6.0 already answers with one number (`r1/red-4.6.0.txt`). Some facts landed
    in other files than §5 names: T9, T10's pairing half, T11 and T17 in the new `DiffBodyPairingTests.cs`, T16 and T25
    in `DiffFormsTests.cs`, and T12 in `BaselineDiffTests.cs` and `RetainedRunsTests.cs` (for `--baseline-run`), so that
    `QueryCommandTests.cs` and `QueryJsonTests.cs`, which a parallel session edits, change only where T18 must.
  - **Built** as §4 says, in `CallPairing.cs` (new), with these choices the plan left open: the tie note is said when
    either side holds more than one call with the key (`! api POST /charge is made 3× in old s0 and 3× in new s0; this
    is the 2nd, matched in order`); a report pair without `stableId`s that has fewer scenarios at the old one's position
    is refused rather than compared with the first, which there is another test (Q8's fallback is for the same test run
    fewer times); the old side's missing body is refused before the pairing, the new side's after it, each naming its
    file; `pairing.old` and `pairing.new` in `--json` are the counts that shared the key; a URI is a path and query only
    when it has a scheme (on Unix `Uri.TryCreate` takes a bare `/orders?x=1` as a file URI and escapes its query).
  - **F19** (§3) was found while building the shape tier; `pairing.py` gained the same rule (`unpaired`, `exact=`) and a
    `--design FILE` output, and its re-run moves no address on either pair (`s6/pairing-rerun-*.txt`).
  - **Mutations** (`mutations/mutate.py`, `results.txt`): the 13 of §5 and 19 more, one for each refusal, note and label
    added, in a copy of the branch (`Kronikol-115-mut`): 32 of 32 killed. "Drop the half from the key" survived the facts
    as first written, which is T29.
  - **S6** (`s6/`): on all 2,614 addresses the built engine pairs as the rule does, 1,518 of 1,518 and 1,096 of 1,096,
    refusing the 2 the rule refuses with `s109 makes 3 CosmosDB Query /orders calls; … is the 4th of 4.`; the 4.6.0 control
    differs on exactly the 13 and 41 wrong pairs.
  - **S7** (`s7/`): two runs from `kronikol ingest`, `kronikol merge` and Kronikol4J's serializer pair as the rule pairs
    them (15, 81 and 15 addresses); nothing else was found.
- **2026-10-07, published.** 4.6.1 is `c9c41711`, tagged `v4.6.1`. Release run 37600554365 passed, with CI 37600552210
  (31 of 31 jobs), CodeQL 37600552119 and CI Summary Preview 37600552216 on that commit; nuget.org lists all 62 ids at
  4.6.1. The wiki has the edits (`cab9ae4`: Querying-Reports, and Tabular-Attributes, whose `compare` and `diff`
  examples had no report and took `s3` as one, found by §6.4's grep). #115 is closed with a comment. Before the tag, a
  rerun of the core suite after the version bump failed `NodeJsCodeCacheTests` once: it deletes the machine's one V8
  cache file, and another session's suite on the same machine rebuilt it; it passed run alone.
