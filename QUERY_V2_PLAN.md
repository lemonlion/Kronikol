# `kronikol query` v2 — payload-side aggregation, comparison and search

**Status:** in progress. Milestone 1 (path engine, `--path` upgrade, exact pairing, one error
classifier, shared flags) shipped in 3.0.51; Milestone 2 (`values` + `--stats`) in 3.0.52;
Milestone 3 (`--where`, run-scoped `interactions`) in 3.0.53; Milestone 4 (body `diff`,
cross-run `--body`, `compare` pointer) in 3.0.54; Milestone 5 (`--group-by`) in 3.0.55;
Milestone 6 (`grep --number` + `--tolerance`) in 3.0.56. Milestones 7–8 not yet.

**Builds on:** `REPORT_QUERY_PLAN.md`, implemented in full in 3.0.47. That work made the report *navigable*
under a byte budget. This work makes the payloads *queryable*: today `--path` in
`src/Kronikol.Tool/Query/PayloadReader.cs` resolves exactly one value from exactly one body — no wildcards,
no aggregation, no comparison — while the answer to a debugging question is usually spread across many
bodies, or is the *difference* between two of them.

**The eight items**, in the order a debugging session needs them (implementation order is different — see
milestones):

| # | Item | Shape |
|---|---|---|
| 1 | Structural body diff | `diff` learns body addresses, in one report and across two |
| 2 | Cross-body projection + aggregation | new `values` command — SELECT/GROUP BY/COUNT over a JSON path |
| 3 | Array length as a first-class answer | `--path '$.items.length()'` |
| 4 | Body-content predicates | `--where "$.success = false"` on `interactions` and `values` |
| 5 | Generic grouping | `interactions --group-by service,status` |
| 6 | Numeric-aware grep | `grep 4173 --number` — matches `4,173.00` and `4173` alike |
| 7 | Trace following | new `trace` command over the W3C ids exported in 3.0.47 |
| 8 | Full-JSONPath escape hatch | new `select` command, go/no-go gated |

**What does not change.** No new fields in `TestRunReport.json` — every item reads data the file already
carries (trace ids and `stepPath` landed in 3.0.47). So: no generator work, no Kronikol4J parity work, no
report-format version bump. All work is inside `src/Kronikol.Tool` plus docs and tests.

---

# Part 1 — Invariants to preserve

These are the contract the existing tests (`tests/Kronikol.Tests/Tool/QueryCommandTests.cs`) enforce, and
every new command inherits them:

- **Budget** — all output flows through `QueryWriter` (`Query/QueryWriter.cs`); truncation announces
  itself and names the re-run. Paged listings go through `QueryWriter.Page`.
- **Addresses, not content** — listings hand back `s3/i47` / `b:hash` addresses that are valid input to
  the next command. New commands must print addresses beside every fact.
- **Elide, never omit** — where a payload would go, a pointer goes: size, `b:` address, the flags that
  fetch it.
- **No payload printed unless named** — aggregates and diffs may *read* bodies freely; they print values
  one-lined (`QueryWriter.OneLine`, ≤60 chars), never whole payloads.
- **Shared flags mean the same thing everywhere** — new flags go in `Query/QueryOptions.cs` once;
  filter flags that change what a paged listing means go into `RerunPrefix()`.
- **Provenance header** — commands that need enrichment-era data (trace ids) degrade with a `!` line on
  old reports, exactly as `WriteProvenance` does today.
- **Exit codes** — 0 answered, 1 unreadable file, 2 bad usage with a message that teaches the grammar.
- **TDD** — every behavior lands red-green-refactor in `QueryCommandTests.cs` (per CLAUDE.md). These are
  CLI-level tests: run the command, assert on the text.

---

# Part 2 — Shared infrastructure (Milestone 1)

Everything in items 2, 3 and 4 rides on one engine, so it comes first.

## 2.1 `Query/PathEngine.cs` — extract and extend the path resolver

Move `ParsePath`, `Path` and the `Keys` walker out of `PayloadReader` into a new `PathEngine` — and fold
in `PathsContaining` (`QueryCommand.Search.cs:110`), grep's private copy of the same recursive walk, so
the tool has one walker, one path-rendering format, and one place `[*]` semantics live. Then extend:

```
SelectAll(JsonElement root, string path) : IEnumerable<(string ConcretePath, JsonElement Value)>
```

Grammar (superset of today's `$.a.b[2].c`):

| Segment | Meaning |
|---|---|
| `.name` | object property (today) |
| `[2]` | array index (today) |
| `[*]` | **new** — every element; fans the traversal out |
| `['a.b']` | **new** — bracket-quoted property, for keys containing dots |
| `.length()` | **new** — terminal only: array → element count, object → property count, string → char count; any other kind is a miss with a message saying what the kind was |

- `SelectAll` yields concrete paths (`$.items[2].price`), so every emitted row is itself a valid `--path`.
- **Near-miss help**: when a property segment misses, collect the keys actually present at that level and
  suggest the closest (case-insensitive, then Levenshtein ≤2): `$.data.custmers is not in this body —
  nearest: $.data.customers`. This replaces today's bare `is not in this body` and is worth more to an
  agent than any other error-message work in this plan.
- `PayloadReader.Path` becomes a thin wrapper (single result or null) so `http`/`body` call sites don't
  churn.

## 2.2 `--path` behavior upgrade (item 3 lands here)

In `EmitPayload` (`QueryCommand.Payloads.cs`):

- A wildcard path prints one row per match, paged: `$.items[2].price = 4173`.
- `--path '$.items.length()'` prints `9`.
- A path resolving to an array/object bigger than the budget prints a *description* instead of refusing:
  `$.items — array, 9 elements, 4.1 KB · --path '$.items[0]' for one · --lines to window it`.

## 2.3 Run-wide iteration and a per-command body cache

- `AllInteractions(ReportIndex)` helper yielding `(scenario, request, response)` triples using the
  exact pairing of §2.4 — `values`, `--where`, `--group-by`, `trace` and numeric `grep` all need it.
- `BodyCache` — `Dictionary<string hash, JsonDocument?>`, filled lazily via `PayloadReader.Read`,
  disposed at command end. Grep already established the rule *evaluate once per distinct content*; the
  cache makes that automatic for every new command. (Measured corpus: 562 body-carrying interactions, 90
  unique bodies, 0.53 MB deduplicated — parsing all of them once is trivially cheap.)

## 2.4 Exact response pairing — a latent bug fixed on the way (per CLAUDE.md)

`FindResponse` (`QueryCommand.Narrative.cs:249`) pairs a request with its response by scanning up to
four entries ahead for the next response *from the same service*. But the report already carries the
exact key: `RequestResponseId` is the pairing identity throughout the main codebase —
`SequenceCollapser.cs:175` matches response to request on it, `ReportDiagnostics.cs:30` warns about
"unpaired request(s) (no matching response with same RequestResponseId)", and the JSON writer itself
groups on it (`ReportGenerator.cs:3122`). It is written to the JSON (`requestResponseId` — present even
in the pre-enrichment 3.0.44 fixture), and `ReportScanner` currently **drops it**. Under interleaved
parallel calls to the same service, the heuristic can attach the wrong status, duration and response
body. Verified blast radius today (`FindResponse` call sites): the `interactions` listing's status /
duration / response-body columns, its `--status` filter, and `flow` — `services` is unaffected, it reads
response entries directly. And it would poison `values`/`--where`/`--group-by`, which lean on pairing
everywhere.

Fix in M1, red-first:

- `ReportScanner` keeps `requestResponseId` (and the pair `traceId`) on `InteractionEntry`.
- `FindResponse` matches on `requestResponseId` when both sides carry one; the proximity heuristic
  remains only as the fallback where the id is absent or empty — genuinely unpaired entries exist
  (`InteractionMerger.cs:97` shows markers and user actions with empty ids, and `ReportDiagnostics`
  already treats unpaired requests as a warnable, real condition).
- Red test: two interleaved requests to the same service with different statuses; assert each request's
  row shows its own status, not its neighbour's.

## 2.5 One error classifier — a divergence fixed on the way

Two classifiers exist and disagree: `IsError` (`QueryCommand.Overview.cs:226`) treats `Created`,
`Accepted` and `NoContent` as success; `LooksLikeError` (`QueryCommand.Narrative.cs:263`) treats any
non-`OK*` text status as an error — so `flow --errors-only` flags a `Created` response that `services`
counts as fine. Consolidate to one shared helper with `IsError`'s semantics (it is the deliberate one —
its comment explains the non-HTTP statuses), used by `flow`, `services` and the new `--group-by` error
column. Red test pinning `Created`/`Accepted`/`NoContent`/`ERROR` classification across both commands.

## 2.6 Request/response targeting convention

Several commands need to say *which* body of a call they mean. One convention everywhere:

- **Default: the response body** — that is where debugging answers live.
- `--request` switches to request bodies; `--both` covers both and tags each row `req`/`resp`.
- In a `--where` expression only, a `req:` path prefix (`--where "req:$.qty > 0"`) targets the request,
  so one command can mix directions.
- **Unpaired calls**: the footnote rule is pairing-based, not event-based. A call with no paired
  response — a fire-and-forget event, or a genuinely unpaired request (`ReportDiagnostics` already warns
  about those) — is excluded from response-targeted evaluation and counted in the footnote
  (`3 calls had no response to evaluate`); under `--request` its own body participates like any other.
  An event *can* carry a tracked response (`MessageTracker.TrackMessageResponse` logs a `Response` half
  with the same `requestResponseId`), and when it does it participates normally. Never silently
  dropped — an event carrying the wrong payload is a real bug class.

(`--in` was considered and rejected: `grep` already uses `--in` for search targets and one flag with two
meanings is the kind of thing an agent gets wrong.)

## 2.7 New `QueryOptions` flags

| Flag | Type | Used by |
|---|---|---|
| `--where EXPR` | repeatable `List<string>` | `interactions`, `values`, `select` |
| `--group-by DIMS` | string | `interactions` |
| `--stats` | bool | `values` |
| `--request`, `--both` | bool | `values`, `interactions --where`, `select` |
| `--number` | bool | `grep` |
| `--tolerance N\|N%` | string | `grep --number` |
| `--body ADDR` | string | cross-run `diff` |

All of the filter-shaped ones (`--where`, `--group-by`, `--request`, `--both`, `--number`,
`--tolerance`) join `RerunPrefix()` so paging re-runs mean the same thing. `PrintUsage` and the help text
are updated in the same commit as each flag.

---

# Part 3 — The commands

## 3.1 `values` — projection with aggregation (item 2, Milestone 2)

The SQL analog: `SELECT value, COUNT(*) … GROUP BY value` where the column is a JSON path evaluated
across every matched body.

```
kronikol query values <report> --path '$.status' [s3] [--service X] [--status 5xx] [--method M]
                      [--step 2] [--grep URI] [--where EXPR] [--request|--both] [--stats]
```

(`--where` in this syntax is the end state: `values` ships in M2 without it and gains it when M3 lands —
§3.2 scopes `--where` to `interactions` + `values` together.)

Default output — distinct values, occurrence-counted, one example address each:

```
$.status across 44 response bodies (7 distinct bodies)
  "APPROVED"   ×41   e.g. s3/i12
  "DECLINED"   ×2    s3/i40, s7/i2
  (absent)     ×1    s3/i50
12 calls carried no body
```

`--stats` for numeric paths:

```
$.total across 44 response bodies
  count 43 · absent 1 · non-numeric 0 · distinct 7
  min 12.5 (s3/i4) · median 380 · max 4173 (s3/i47) · sum 18240.5 · mean 424.2
```

Semantics, precisely:

- **Scope**: the whole run when no address is given, one scenario with `s3`. All the `interactions`
  filters apply, plus `--where`.
- **Counting is per occurrence**, not per distinct body — the question is "what did the system see", and
  it saw the same body every time it arrived. Implementation: iterate the matched interactions and look
  each one's body hash up in the `BodyCache`, so a body is *parsed and evaluated* once per distinct
  content but *counted* once per interaction that carried it. (Not via `BodyEntry.Occurrences` — those
  are address strings and would need re-parsing back into interactions.)
- **Wildcards fan out**: `--path '$.items[*].price'` counts every element of every body.
- **Absent is a value**: a body the path misses is counted and shown as `(absent)` — silence would hide
  exactly the bug ("one response was missing the field") this exists to find.
- **Bodiless calls are footnoted**, not silently excluded.
- Sort: count descending, then value. Rows paged; `--count` prints the total number of matched values.
- `min`/`max` in `--stats` carry the address of the extreme — the outlier is usually the next thing to
  fetch.

## 3.2 `--where` — the WHERE clause (item 4, Milestone 3)

```
kronikol query interactions <report> s3 --where "$.success = false"
kronikol query interactions <report> --where "$.items[*].price < 0" --where "req:$.currency = GBP"
```

Grammar: `[req:]PATH OP LITERAL`, ops `= != < > <= >= ~ !~ exists !exists` (`exists` takes no literal).
Literals: `null` / `true` / `false`, numbers, quoted or bare strings. Both sides numeric → numeric
comparison; otherwise ordinal-ignore-case string comparison; `~` is substring on the value's raw text.

- **Wildcard paths use any-semantics**: `$.items[*].price < 0` passes when *any* element satisfies. (The
  agent asking "which calls returned a negative price" wants any; all-semantics has no natural question
  behind it here.)
- Repeated `--where` is AND. OR is deliberately absent — run the command twice.
- Default target is the response body; `req:` prefix per-expression; `--request`/`--both` shift the
  default.
- An interaction whose targeted body is missing, or is not JSON, fails the predicate (it cannot satisfy
  it); the footer reports how many were excluded that way.
- Malformed expression → exit 2 with the grammar in one line.
- Applies to `interactions` and `values`. (`flow --where` is noted as a possible follow-up, not in scope.)

**`interactions` also becomes run-scopable in this milestone**: today `TryScenario`
(`QueryCommand.Narrative.cs:266`) demands an address; `interactions <report> --where …` across the whole
run is half the value of the feature. Without an address, rows print full `s3/i47` addresses (they
already do) and the existing 120-row page cap keeps the output honest.

## 3.3 Structural body diff (item 1, Milestone 4)

The most common debugging move — "this call succeeded in the passing scenario, what was different in
mine?" — currently requires printing two bodies whole. The diff prints only differing paths.

```
kronikol query diff <report> s3/i47 s7/i47       # two interactions' bodies, one report
kronikol query diff <report> b:4bdea521 b:9f31c02a
kronikol query diff <old.json> <new.json>        # run diff — existing behavior, unchanged
kronikol query diff <old.json> <new.json> --body s3/i47   # same call across two runs
```

Dispatch rule in `Diff` (`QueryCommand.Search.cs`): if the first positional parses as an `Address` or a
`b:` hash, it is a body diff inside the single report; otherwise it is the existing two-file run diff,
which now also honors `--body`. Two **scenario** addresses (`diff <report> s3 s7`) parse as addresses
but name no bodies — exit 2 pointing at `compare`, because an agent will type exactly this and the
error must teach the right verb, not just refuse.

- An interaction address means **that interaction's own body** (`--request`/response distinction does not
  arise: request and response are separate interactions with separate addresses, and `interactions`
  prints both `b:` hashes on every row).
- **Cross-run `--body s3/i47`**: `s3` is resolved in the *old* report, then matched into the new one **by
  `stableId`**, not by ordinal — ordinals shift between runs, stableId is the cross-run key the run diff
  already uses. The interaction ordinal is then applied within the matched scenario; if it is out of
  range the error says how many interactions the new scenario has.

Output:

```
- s3/i47  b:4bdea521  2.1 KB
+ s7/i47  b:9f31c02a  2.2 KB

$.customer.region: "EU" → null
$.items: 9 → 10 elements
$.items[4].price: 12.50 → 1250
$.items[9]: (absent) → {sku, price, qty}
$.total: 4173 → 3902

5 paths differ
```

Algorithm:

- Hashes equal → `byte-identical` and stop (the index already knows without reading anything).
- Both JSON → recursive walk of both `JsonDocument`s in document order:
  - changed scalar → `path: a → b` (both one-lined at 60);
  - present/absent → `(absent)` on the empty side; an added/removed *subtree* is one row with a shape
    summary (`{sku, price, qty}` / `[3 elements]`), never a dump;
  - type change → `path: number 3 → string "3"`;
  - arrays index-aligned: a length change is emitted first as its own row, then per-index diffs.
- **Array-shift noise guard**: index alignment makes one inserted element diff every subsequent index.
  When more than 60% of a compared array's rows differ but the two element multisets are mostly shared,
  collapse to one row: `$.items: elements shifted/reordered — 9 vs 10, 8 identical`. Proper LCS alignment
  is explicitly deferred; this guard keeps v1 from being misleading, which is the actual risk.
- Either side non-JSON → line-level fallback over the pretty-printed texts: first 20 differing lines as
  `line 12:  - … / + …`.
- Rows are paged; `--count` prints the number of differing paths.

**`compare` gains one line** (cheap, high value): after the existing `bodies: 9 vs 9, 4 byte-identical`,
find the first *paired* call (same position in the request sequence) whose body hashes differ and print
`first differing body: diff s3/i12 s7/i12`. The footer's claim — "the first differing call is usually the
answer" — becomes an address instead of advice.

## 3.4 `--group-by` on `interactions` (item 5, Milestone 5)

```
kronikol query interactions <report> [s3] --group-by service,status [--sort errors] [--where …]
```

Dimensions (comma list, any order): `service`, `method`, `status`, `path` (URI path, query stripped),
`step`, `phase`, `category`, `kind` (metaType), `capturedBy`. Unknown dimension → exit 2 listing these.
`step` buckets on the bare `stepPath` string, which collides across scenarios — at run scope the header
says so (`step "2" spans scenarios — scope with s3 for one scenario's steps`) rather than pretending the
buckets are comparable.

```
service      status   calls  errors  median    max     bodies
payments     200         38       0   12 ms    80 ms        4
payments     500          2       2  230 ms   410 ms        1
catalogue    200        120       0    3 ms    19 ms        2
```

- `status` and duration come from the exactly-paired response (§2.4); duration coalesces
  `request.DurationMs ?? response.DurationMs` the way the `interactions` listing already does. `errors`
  uses the consolidated classifier (§2.5) so text statuses from non-HTTP taps (`ERROR`, `Created`)
  classify the same here as everywhere else. `bodies` = distinct response hashes in the bucket (a bucket
  with 120 calls and 1 body is one fact — the same signal `--group` exploits).
- Index-only (no payload seeks) unless combined with `--where`.
- Sort: calls descending by default; `--sort errors|duration` reuses the existing flag.
- `--count` = bucket count. Composes with every existing filter. This subsumes nothing: `services` stays
  as the curated view and the only answerer of negative questions; `--group-by` is the general form.
- Distinct from the existing `--group` (which folds *adjacent identical* calls in sequence order);
  passing both → exit 2 saying they don't compose.

## 3.5 Numeric-aware grep (item 6, Milestone 6)

The number the user quotes is the *formatted* one; the payload holds the raw one. `grep "4,173.00"`
missing `4173` is a real failure of the tool's flagship use case.

```
kronikol query grep <report> 4173 --number [--tolerance 0.5] [--tolerance 1%]
```

- Needle: strip `,`, `_`, spaces and leading currency symbols (`$ € £`), then must parse as a number —
  otherwise exit 2 ("--number needs a numeric needle; drop the flag for text search").
- **Separator ambiguity** (`4.173,00` is European for `4173.00`; naive comma-stripping reads it as
  `4.17300`): normalize each token under *both* interpretations — comma-as-thousands and
  comma-as-decimal — and match if either equals the needle. Two parses per token is cheap; a false
  negative on the flagship use case is not.
- JSON bodies: walk values — `Number` kinds compared numerically; `String` kinds scanned for embedded
  numeric tokens (regex `[-+]?\d[\d,._]*(\.\d+)?`, normalized the same way). Since a numeric match is
  always a *value* match, `--number` on JSON bodies always emits paths (i.e. `--values` behavior is
  implied): `s3/i47  body  $.data.total = 4173`. When the raw text differed from the needle, say so:
  `$.display = "4,173.00" (≈ 4173)`.
- Non-JSON targets (uris, headers, steps, assertions, notes): token-scan the text the same way.
- `--tolerance` absolute (`0.5`) or relative (`1%`); default is exact with a 1e-9 relative epsilon so
  `4173.0` matches `4173`.
- Everything else about `grep` — targets, dedup per distinct body, address output, paging — unchanged.

## 3.6 `trace` (item 7, Milestone 7)

3.0.47 put `activityTraceId`/`activitySpanId` (the W3C ids) on every interaction; nothing consumes them
yet.

```
kronikol query trace <report> 4bf92f3577b34da6a3ce929d0e0e4736
kronikol query trace <report> 4bf92f35          # unambiguous prefix, ≥8 hex chars
kronikol query trace <report> s3/i47            # that interaction's trace
```

```
trace 4bf92f35… — 7 calls across 2 scenarios
  +0 ms    s3/i12  api        POST /orders            202    span 00f067aa
  +12 ms   s3/i14  payments   POST /charge            200    span a1b2c3d4
  +80 ms   s7/i2   payments   POST /charge            500    span e5f60718
! spans 2 scenarios (s3, s7) — shared state or fixture leakage
```

- Rows chronological by timestamp, offsets from the first; each row is scenario-qualified address,
  service, summary, status, short span id. `InteractionEntry.Timestamp` is an ISO-8601 string
  (`2026-01-01T10:00:01.000Z`) — parse per row, and when any row's timestamp is absent or unparseable
  fall back to file order for the whole trace with a `!` line saying so, rather than silently mixing two
  orderings.
- The cross-scenario warning is the command's second job: a trace id that leaks across scenarios is the
  classic flaky-test smell and nothing else in the tool can see it.
- Footer states the known limitation honestly: parent span ids are not captured, so this is the
  chronology of the trace, not its tree.
- Address form with no `ActivityTraceId` on that interaction, or an unenriched report → the provenance
  message (re-run on a current Kronikol).
- Ambiguous prefix → exit 2 listing the candidate ids; unknown id → exit 2 saying how many distinct trace
  ids the report holds.

## 3.7 `select` — full-JSONPath escape hatch (item 8, Milestone 8, go/no-go)

```
kronikol query select <report> '$..items[?(@.price < 0)]' [s3] [--service X] [--request|--both]
```

Applies an RFC 9535 JSONPath — descent (`..`), filters (`?()`), functions — across the matched bodies
(distinct-body evaluation, occurrence-counted, exactly like `values`), printing
`address  concrete-path = value` rows under the budget.

- **Why JSONPath and not jq**: declarative (no process to sandbox, no dependency on a jq binary), a
  syntax LLMs produce reliably, and its results are still *paths + values* — which keeps the tool's
  contract that every output row is addressable input to the next command. jq's power (reshaping,
  arithmetic pipelines) is exactly the part whose failure modes are opaque in one-shot use.
- **Library**: `JsonPath.Net` (json-everything, MIT — confirm the license as the first task of the
  milestone; trimming/AOT is a non-issue, `Kronikol.Tool.csproj` is a plain `net10.0` `PackAsTool`
  with no trimming). The hand-rolled
  `PathEngine` stays as-is for `--path`/`values`/`--where` — its near-miss error messages are the point;
  `select` alone routes through the library.
- **Go/no-go**: decided after Milestones 2–5 have been used in anger. If `values` + `--where` +
  `--group-by` answer the real traffic — which is the bet this plan makes — `select` is dropped and this
  section becomes the record of why. The trigger to build it: bespoke verbs sprouting flags to
  approximate descent or filters.

## 3.8 Deliberately out of scope — decisions, not omissions

| Excluded | Why, and the escape hatch |
|---|---|
| Projecting/diffing **headers** (`values` over a header, `diff --headers`) | Bodies are where the answers live; headers already have `grep --in headers` and `http --headers`. Revisit if a real session is seen wanting "what auth header did each call send" — the `values` machinery would extend naturally |
| OR / grouping / functions in `--where` | Two commands, or `select` (M8) |
| LCS array alignment in `diff` | The §3.3 collapse guard prevents the misleading case; full alignment waits for evidence |
| `flow --where` | Noted follow-up; `interactions --where` covers the question with addresses |
| XML / YAML / binary body querying | v1's position holds (`REPORT_QUERY_PLAN.md`: "JSON is what agents get"); non-JSON bodies are counted and footnoted everywhere, never silently skipped |
| Percentiles beyond the median in `--stats` / `--group-by` | count/distinct/min/median/max/sum/mean answers the debugging question; p95 is a monitoring question |

---

# Part 4 — Milestones, each shippable

Order is by dependency, then by value. Item 3 (the path engine) → item 2 (`values`) → item 4 (`--where`)
is the spine; body diff (M4) touches none of it and could land in parallel.

| M | Delivers | Item | Depends on |
|---|---|---|---|
| 1 | `PathEngine` (`[*]`, `['k.k']`, `.length()`, near-miss), `--path` upgrade, `AllInteractions`, `BodyCache`, new flags parsed; **exact response pairing (§2.4)** and **one error classifier (§2.5)** — both are bug fixes and land red-first | 3 | — |
| 2 | `values` (+ `--stats`, `--request`/`--both`; `--where` joins it in M3) | 2 | M1 |
| 3 | `--where` on `interactions` + `values`; run-scoped `interactions` | 4 | M1 |
| 4 | body `diff` (single-report, cross-run `--body`, stableId matching); `compare` pointer line | 1 | — |
| 5 | `--group-by` | 5 | M3 (run scope) |
| 6 | `grep --number` (+ `--tolerance`) | 6 | M1 (walker reuse) |
| 7 | `trace` | 7 | M1 (`AllInteractions`) |
| 8 | `select` — **go/no-go first** | 8 | M1–M5 experience |

Per milestone, in order: red tests → implementation → `PrintUsage` + `RerunPrefix` → docs (§6) →
full suite → version bump, changelog, tag, push (per CLAUDE.md, every package to the same number).

---

# Part 5 — TDD detail

## 5.1 Fixture work (first red commit of M1)

`QueryCommandTests.BuildLogs()` needs richer bodies; extend the existing builders rather than adding a
parallel fixture:

- A response body with a numeric array and a formatted-number string:
  `{"items":[{"sku":"a","price":12.5},…],"total":4173,"display":"4,173.00","region":null}` — feeds
  wildcard paths, `length()`, `--stats`, `--where`, numeric grep.
- A field whose value varies across calls (`"status":"APPROVED"` ×N, `"DECLINED"` ×1) and one body where
  the field is absent — feeds `values` counting and `(absent)`.
- Two scenarios with near-identical paired bodies differing in 2–3 paths (one scalar change, one array
  length change, one absent key) — feeds `diff` and the `compare` pointer.
- A non-JSON body pair — feeds the line-diff fallback.
- Trace ids: **resolved** — `ActivityTraceId`/`ActivitySpanId` are plain settable properties on
  `RequestResponseLog` (`src/Kronikol/Tracking/RequestResponseLog.cs:52-53`), no ambient `Activity`
  needed. Set them directly, with one trace id shared across two scenarios — feeds `trace` and the
  leakage warning.
- Two interleaved request/response pairs to the *same service* with different statuses (distinct
  `RequestResponseId`s) — the red test for exact pairing (§2.4), and a regression guard for everything
  built on `FindResponse`.
- A `Created` (or `NoContent`) text status on one interaction — pins the consolidated error
  classifier (§2.5) across `services`, `flow --errors-only` and `--group-by`.
- Two events (`MetaType: Event`): one fire-and-forget (no response half) and one with a tracked response
  via the same `RequestResponseId` — feeds the §2.6 unpaired-call footnote and its paired-event
  counterpart.
- A second report file where a scenario has the same `stableId` but a different ordinal — feeds cross-run
  `diff --body`.

## 5.2 Test list (names in the file's existing style)

**M1** — `Path_wildcard_lists_every_match_with_its_concrete_path` ·
`Path_length_function_counts_an_array` · `Path_length_on_a_scalar_says_what_kind_it_was` ·
`Path_miss_suggests_the_nearest_key` · `Path_bracket_quoted_key_containing_a_dot` ·
`Big_path_result_describes_itself_instead_of_printing` ·
`Interleaved_calls_to_one_service_pair_by_requestResponseId` ·
`Pairing_falls_back_to_proximity_when_the_id_is_absent` ·
`Created_and_NoContent_are_not_errors_anywhere` · `Text_ERROR_status_is_an_error_everywhere`

**M2** — `Values_groups_distinct_values_with_counts_and_an_example_address` ·
`Values_counts_occurrences_not_distinct_bodies` · `Values_reports_absent_as_a_value` ·
`Values_stats_summarises_a_numeric_path_with_extreme_addresses` ·
`Values_wildcard_counts_every_element` · `Values_footnotes_bodiless_calls` ·
`Values_request_flag_targets_request_bodies` · `Values_scoped_to_one_scenario` ·
`Values_without_a_path_exits_2_with_usage` · `Values_footnotes_unpaired_calls_under_response_targeting` ·
`Values_evaluates_a_paired_event_response_normally` · `Values_both_tags_each_row_with_direction`

**M3** — `Where_filters_on_a_response_value` · `Where_comparison_is_numeric_not_lexical` ·
`Where_wildcard_passes_when_any_element_satisfies` · `Where_req_prefix_targets_the_request` ·
`Wheres_compose_as_and` · `Where_reports_how_many_calls_had_no_evaluable_body` ·
`Where_bad_grammar_exits_2_with_the_grammar` · `Interactions_without_an_address_cover_the_run` ·
`Where_survives_paging_in_the_rerun_footer`

**M4** — `Diff_bodies_prints_only_differing_paths` · `Diff_identical_bodies_answers_from_the_index` ·
`Diff_array_length_change_is_one_row_then_the_tail` · `Diff_shifted_array_collapses_to_a_summary` ·
`Diff_added_subtree_is_a_shape_not_a_dump` · `Diff_non_json_falls_back_to_lines` ·
`Diff_across_runs_matches_the_scenario_by_stableId` · `Diff_still_diffs_two_runs` ·
`Diff_of_two_scenario_addresses_points_at_compare` ·
`Compare_points_at_the_first_differing_body`

**M5** — `GroupBy_counts_errors_and_distinct_bodies_per_bucket` · `GroupBy_composes_with_where` ·
`GroupBy_unknown_dimension_lists_the_valid_ones` · `GroupBy_and_group_refuse_to_compose` ·
`GroupBy_at_run_scope`

**M6** — `NumberGrep_matches_across_formatting` · `NumberGrep_emits_the_json_path_of_each_hit` ·
`NumberGrep_shows_the_raw_text_when_it_differed` · `NumberGrep_tolerance_absolute_and_percent` ·
`NumberGrep_matches_a_european_decimal_comma` · `NumberGrep_rejects_a_non_numeric_needle`

**M7** — `Trace_lists_the_chain_chronologically_with_offsets` · `Trace_flags_cross_scenario_spans` ·
`Trace_by_interaction_address` · `Trace_prefix_must_be_unambiguous` ·
`Trace_on_an_unenriched_report_says_why` · `Trace_footer_admits_no_parent_links`

**M8** (if go) — `Select_runs_a_descent_filter_across_bodies` · `Select_rows_are_addressable` ·
`Select_bad_jsonpath_exits_2_with_the_library_message`

Plus, per the file's tradition, budget tests: `Values_stays_small_on_a_wide_run`,
`Diff_of_two_large_bodies_stays_small`.

---

# Part 6 — Documentation and release (per CLAUDE.md, every milestone)

## 6.1 Skill template (`templates/skills/kronikol-test-debugging/`)

- `references/commands.md` — the full flag reference; every new command and flag lands here in the
  milestone that ships it, in the existing table style. Every example that takes a JSONPath or a
  `--where` expression is shown **single-quoted** (`--path '$.items[*].price'`) — single quotes are
  literal in both bash and PowerShell, while `[*]`, `?(` and `>` are shell-active unquoted, and a
  quoting failure here surfaces as a baffling "path not in this body" rather than a shell error.
- `SKILL.md` — three edits: **the ladder** gains an aggregation rung (`values` / `--group-by` sit
  between `interactions` and `http` — aggregate before you fetch); **the recipes table** gains rows —
  "these two runs/scenarios differ" → `diff s3/i47 s7/i47`; "what values did X ever return" → `values
  --path`; "the number on screen is wrong" recipe upgrades to `grep <n> --number`; "is this one request
  or a chain?" → `trace s3/i47`; **Traps** gains "aggregate counts are per occurrence, not per distinct
  body".
- `scripts/query.py` (the no-dotnet fallback) — **stays a smaller set**; do not port the new verbs.
  Its help text gains one line naming the commands that exist only in the real tool, so an agent using
  the fallback knows what it is missing rather than concluding the feature doesn't exist.

## 6.2 Wiki (`../Kronikol.wiki`) — every page, audited 2026-08-25

Nine pages mention `kronikol query` today (`git grep -l "kronikol query"`); the table covers those nine
plus the pages the new verbs make relevant for the first time (`Event-Annotations`, the two OTel
integration pages, `Large-Response-and-Diagram-Handling`). Per page, what changes and in which
milestone:

| Page | Change | When |
|---|---|---|
| `Querying-Reports.md` | **The main page — updated every milestone.** New sections for `values`, `trace`, `select`; the `diff` section gains body-diff addressing; the shared-flags table gains `--where`, `--group-by`, `--stats`, `--request`/`--both`, `--number`, `--tolerance`; a short "Aggregate before you fetch" subsection after *The idea*; the `#using-it-from-an-ai-agent` anchor and section are preserved (other pages deep-link it) | every M |
| `AI-Integration-Prompt.md` | The embedded prompt text names capabilities; add one clause covering aggregation (`values`), body diff and `trace` so an agent that only ever sees the prompt knows they exist | M2, M4, M7 |
| `Diagnostics-and-Debugging.md` | The routing table's `kronikol query` row broadens ("…what a service returned **across every call**, what changed between two runs") | M2 |
| `Generated-Reports.md` | The pointer note stays; the key-fields section gains "consumed by `kronikol query trace`" against `activityTraceId`/`activitySpanId` | M7 |
| `Home.md` | The one-line description of Querying-Reports mentions aggregation and diffing | M2 |
| `Merging-Parallel-Reports.md` | Extend the existing "query reads both" paragraph: `values`/`--group-by` across a *merged* file aggregate over every runner's scenarios — the cross-runner comparison this page's readers actually want | M5 |
| `Assertion-Tracking.md` | Existing examples stand; add `values --path` as the follow-up when an assertion failure is about an aggregate | M2 |
| `Tabular-Attributes.md` | Existing `annotations` example stands; add `compare`/`diff` for "which example row differs and how" | M4 |
| `Phase-Aware-Tracking.md` | Gains `interactions --group-by phase` as the run-level view of setup-vs-action traffic | M5 |
| `Event-Annotations.md` | Gains `interactions --group-by kind` (metaType) beside the existing enrichment note | M5 |
| `Integration-OpenTelemetry-Extension.md`, `Integration-Otlp-Extension.md` | New cross-link: the W3C ids these extensions produce are followable with `kronikol query trace` | M7 |
| `Large-Response-and-Diagram-Handling.md` | One line: capture-time truncation markers surface in `values`/`diff` output the same way `body` surfaces them | M4 |
| `_Sidebar.md` | No change — no new pages; everything lands in `Querying-Reports` | — |

**Final docs sweep** (after the last milestone): grep the wiki and the skill for every command name and
flag in `PrintUsage`; run each documented example against a generated sample report; fix drift. This is
its own checklist item because per-milestone edits reliably miss a stale example on a page nobody
re-opened.

## 6.3 Repo docs and release

- `README.md` — one line per new command in the query section.
- `CHANGELOG.md` — per milestone.
- Version: patch bump across **all** packages to the same number, tag `v{version}`, push commit + tag.
- Kronikol4J: explicitly none — the tool is .NET-only and the report format is untouched. Note it in the
  changelog entry so the parity audit doesn't re-ask.
- Housekeeping with M1: flip the stale status line in `REPORT_QUERY_PLAN.md` ("Nothing here is
  implemented yet") to "implemented in full in 3.0.47", and mark this file's own status as each
  milestone lands — a plan that misstates what exists sends the next agent down the wrong path.

---

# Part 7 — Risks and open questions

| Risk | Position |
|---|---|
| Array diff noise from index alignment | The ≥60%-differing collapse guard in §3.3; LCS deferred until a real report shows the guard firing wrongly |
| `--where` grammar creep (OR, grouping, functions) | Refuse; two commands or `select` (M8) is the answer |
| Parsing every distinct body in `values`/`--where` on a huge report | Bounded by distinct-body dedup (90 unique of 562 in the measured corpus) and the `BodyCache`; if a pathological report appears, evaluate streaming per body before adding any cap — a silent cap is the one forbidden move |
| No real large report in the repo to validate perf against (`tools/render-bench/real/` holds only `.puml` files) | Synthesize one the way `BigDiagramReport()` already does — a generated report with several hundred interactions and a few hundred distinct bodies — and assert `values`/`grep --number` complete inside the existing test timeout |
| `JsonPath.Net` license (M8 only; trimming ruled out — plain `net10.0` tool, §3.7) | First task of the milestone, before any code |
| `values` on non-JSON bodies | Counted and footnoted as `non-JSON`, never silently skipped |
| Ordinal instability across runs | Already solved where it matters: cross-run operations key on `stableId` (§3.3) and `b:` hashes; single-run ordinals stay the cheap currency |

Resolved during planning (deep-dived 2026-08-25, no longer open):

- Trace ids **are** settable in fixtures — plain properties on `RequestResponseLog`
  (`RequestResponseLog.cs:52-53`), no ambient `Activity` required.
- Request/response pairing has an exact key the scanner currently discards — promoted from risk to the
  §2.4 bug fix. Verified: `RequestResponseId` is the matching key across `SequenceCollapser`,
  `ReportDiagnostics` and the JSON writer itself; the mispairing blast radius is `interactions` (columns
  and `--status` filter) and `flow`, not `services`, which reads response entries directly.
- Events are **not** always fire-and-forget — `MessageTracker.TrackMessageResponse` logs a `Response`
  half with the same `RequestResponseId`, so §2.6's footnote rule is pairing-based, not event-based.
- `flow --errors-only` and `services` classify text statuses differently — promoted to the §2.5 fix.
- `services --sort` accepts exactly `duration|bytes|errors` (`QueryCommand.Overview.cs:159-163`) —
  §3.4's reuse of the flag is accurate.
- Timestamps are ISO-8601 strings on every interaction — `trace` ordering is implementable as specified,
  with the file-order fallback in §3.6.
