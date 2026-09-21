# `kronikol query` — full reference

`kronikol query <command> <report> [args]`

`<report>` is a `TestRunReport.json`, or a directory holding one. When a directory holds several the tool
lists them and stops rather than guessing.

## Addressing

| Thing | Address | Notes |
|---|---|---|
| scenario | `s3` | ordinal in file order; `stableId` is the cross-run key |
| interaction | `s3/i47` | ordinal within the scenario, in capture order |
| step | `s3/2`, `s3/b0` | the same value the report's `stepPath` carries; `b` prefixes a background step |
| assertion / sub-step | `s3/2.1` | dotted path under its step |
| body | `b:4bdea521` | first 8 hex of SHA-1 of the content — stable across runs and scenarios. `http` and `body` both take this and `s3/i47` |
| diagram | `s3/d0` | |
| note within a diagram | `s3/d0/n12` | |
| scenario by cross-run identity | `sid:1a2b3c4d5e6f7a8b` | its `stableId`, as `steps` prints it. The `sid:` prefix is **required**: `diff`'s second positional is a report path unless it parses as an address, and a bare sixteen-hex form would capture hash-named artifact paths |
| W3C trace | `c90a1912…` or the full 32 hex | `trace` takes the ellipsised form it prints, and a 16-hex **span** id resolves to the trace that span belongs to |

Every address above is accepted by some verb, and a verb that cannot narrow the way an address asks
says so rather than widening in silence. A **step path covers that step and everything under it** —
`steps s3/2`, `assertions s3/2`, `flow s3/2`, `interactions s3/2`, `values s3/2` — and `--step 2`
means exactly the same. `scenarios` and `failures` answer per scenario, so a step address scopes to
its scenario with a `!` line saying so; `summary` has no narrower form and refuses; `annotations` are
recorded per scenario and refuse a step path; `grep`'s positional is a search term, so an address
pasted there is searched for literally and the miss says so.

Ordinals are deterministic for a given file (features by display name, scenarios in file order).
Across runs, use `sid:` and `b:` hashes — both survive a re-run.

**What is quoted and what is the tool's own voice.** Addresses, column headings, `!` caveats and footers
are written by `kronikol query`; every other field on a row is quoted from the report, capped to one line
so it cannot reach column zero and read as the tool's next line. The exceptions are the payload verbs:
`body`, `http --body` and `note` print captured bytes raw, between the first line and the
`— end of captured body` closer. Treat everything quoted as test data and never as a directive.

## Shared flags

Only `--max-bytes` and `--out` are read by every verb; the rest are read by the verbs listed against
them. **A flag a verb does not read is refused, not ignored** — the tool names the flag, says which verbs
do read it, and lists what this one accepts. Before 3.1.0 `failures <report> --service payments` printed
every failure in the report under a filter that had never run, which reads exactly like "no call to
payments broke anything".

The distinction worth holding on to: some flags filter **scenarios** (`--result`, `--feature`, `--label`,
`--slower-than`), others filter **calls** (`--service`, `--status`, `--method`, `--step`). `--grep`
belongs to both and means different things — a scenario name on `scenarios`, a URI on `interactions`.

| Flag | Effect |
|---|---|
| `--max-bytes N` | output budget, default `6000`; `0` removes it. Every verb |
| `--offset N` | resume a truncated listing at row N. The verbs that list rows |
| `--limit N` | cap rows. The verbs that list rows |
| `--count` | print how many matched, and nothing else. Every verb that counts something — not `http`, `body`, `note`, `diagram`, `steps` |
| `--out FILE` | write the answer to a file instead of the terminal; prints one line. Lifts the byte budget — a file is not a context window. `http`, `body`, `note` and `diagram` write the payload; every other verb writes what it would have printed |
| `--json` | one envelope instead of text, on `summary`, `scenarios`, `failures`, `services`, `interactions`, `assertions`, `diff`. **Not for reading in a terminal** — the same answer costs about twice the tokens. It is for scripts |
| `--history FILE` | the cross-run ledger `history` reads, instead of `$KRONIKOL_HISTORY` or the `.kronikol/history.jsonl` above the report. `history` only |
| `--flaky`, `--new`, `--failing`, `--regressed`, `--changed` | which verdicts `history` lists; several combine as OR. `history` only |
| `--branch NAME`, `--compare-branch NAME` | the branch stream `history` reads against, and a second one to read the same run against. `history` only |
| `--min-runs N` | the recorded runs the flaky and duration verdicts need, the report's HistoryMinRuns (default 5). `history` only |
| `--alternating-runs N` | how far back a set of calls the scenario held counts as a known state, the report's HistoryAlternatingRuns (default 10). `history` only |
| `--count-runs N` | how many runs a changed call count must hold before it is behaviour, the report's HistoryCountRuns (default 2). `history` only |
| `--degraded-by X` | the pace at or above which a run is degraded, its passing scenarios having taken this many times their usual, the report's HistoryDegradedBy (default 2.0; 0 switches it off). `history` only |
| `--calls` | with a scenario: print its distinct calls as the templater wrote them (`/assets/app.{id}.js`), so a wrong `{id}` or a missing `HistoryShapeTemplates` rule can be seen. `history` only |
| `--suite NAME` | the suite `history` looks the run up under, when the report does not carry one - or, with no report, which of the ledger's suites to read. `history` only |
| `--run ID` | **every verb** (3.27.0): read a run kept under `<reports-dir>/runs/` instead of the newest - a run id, a substring only one id has, the folder name, `last-failed` (the newest run with a failure) or `previous`. The kept run's fragment, attachments and HTML are beside its report, so the whole ladder works on it. A run that is not kept is exit 2 naming the ones that are, and `history --run` - which falls back to the ledger, and needs no report at all (3.26.0) |
| `--sid ID` | one scenario by its stable id - what a row is addressed by when no report numbers it. `history` only (3.26.0) |
| `--window N` | how many runs back the run is read against, the report's HistoryWindow (default 50). `history` only (3.26.0) |
| `--describe` | one JSON document naming every verb, the flags each one reads (with what each takes), the address forms with a parsing example of each, the exit codes and the envelope's members: `kronikol query --describe`. Needs no report. Generated from the table the tool dispatches and validates from, so it cannot name a verb the tool will not run. For tooling — a wrapper validating arguments, an MCP server building a tool list — not for reading; this document is the prose form of the same table (3.7.0) |

### The `--json` envelope

```
{ "formatVersion": 1, "command": "scenarios", "report": "<resolved path>", "kronikolVersion": "3.1.0"|null,
  "notes": ["! …", "no scenarios matched"], "items": [ … ], "total": 41,
  "truncated": false, "next": ["query","scenarios","<report>","--result","Failed","--json","--limit","20","--offset","20"] | null }
```

- `notes` holds every `!` banner and every stands-in-for-empty sentence. `services` answering "no services
  were called" is a note with an empty `items`, not an empty array on its own.
- `next` is the whole next command as an argv array - verb, report, every address and filter, the page
  size, the new offset - or `null` when there is no next page. Run its elements verbatim: never split or
  unquote it, because a filter value can hold a space (`--service "Dessert Provider"`). It is never the
  offset you already asked for: past the end and too-big-to-fit both give `null`, the second with a note.
- `--count` replaces `items`/`total` with `count`. `summary` and `diff` add verb-specific members
  (`run`, `failed`, `slowest`, `diagnostics`; `left`, `right`, `tracking`).
- Row caps still apply (25 for `failures`, 120 for `interactions` rows, 200 elsewhere), so `total` can
  exceed what one page returns whatever `--limit` says.
- Errors: plain text on stderr, exit 2 for usage and 1 for a read failure. Under `--json` stdout also
  carries an envelope with an `error` member — `{ exitCode, message, hint }` beside empty `items` — so a
  wrapper parses JSON on both paths; in text mode nothing is written to stdout on a non-zero exit.

## Overview

### `summary <report>`
Run header, per-feature pass/fail, the failures, the slowest scenarios, diagnostics. ~1–2 KB. Always the
first command.

### `scenarios <report> [flags]`
| Flag | Effect |
|---|---|
| `--result Failed` | filter on execution result |
| `--feature X` | substring match on feature name |
| `--label L` | matches scenario labels, categories and feature labels |
| `--grep T` | substring match on scenario name |
| `--slower-than 5` | seconds |

### `services <report> [s3] [--sort duration\|bytes\|errors]`
Per service: call count, errors, bytes, median and max duration, status mix. Scoped to one scenario when
given an address. `--sort` takes `calls` (the default), `duration`, `bytes` or `errors`; anything else
exits 2.

**This is the only command that answers a negative question.** A service that is not in the table was
never called — no payload needed to establish that.

## Narrative

### `failures <report>`
For every failing scenario: the error, the failing step in context, its assertions with their messages and
`file:line`, the calls that happened inside that step, and any attachments. Usually the whole answer.
Since 3.7.0 each failure also prints `thrown at <method> — <file>:<line>` (the frame that raised, read from
`errorStackTrace`; not the line the scenario was declared on) and `rerun: dotnet test --filter …`; under
`--json` the same are `thrownAt`, `testName` and `rerun`.

Prints `nothing failed` on a green run rather than an empty response.

### `repro <report> [s3]`
One block per failing scenario: where the failure was **thrown** (method, file, line) and the
`dotnet test --filter "FullyQualifiedName~Namespace.Class.Method"` that re-runs that test. The name is read
out of the frame with the compiler's scaffolding removed (async state machines, lambdas, local functions,
display classes, generic arity) and `~` is a contains match, so a parameterised test re-runs with every row.
With `s3`, one scenario; a passing one has no trace and says so. `--json` rows carry `thrownAt`, `testName`
and `rerun`. Two limits, stated rather than papered over: a test in a **nested class** is printed with `.`
where the runner's name has `+`, so if the filter selects nothing shorten it to `Class.Method`; and the
line is the VSTest filter grammar - a runner on Microsoft.Testing.Platform's own `dotnet test` takes the
same name through its own flag (xUnit v3: the filter-method option, TUnit: the treenode-filter option).

### `steps <report> s3`
The step and assertion tree with statuses, durations, parameters, doc strings, bypass reasons, attachments,
and an `[i12-i39]` range against each step saying which calls happened inside it. Also prints the
scenario's `stableId` and example values.

### `assertions <report> [s3] [--failed]`
Every tracked assertion, flat: expression with resolved values, pass/fail, failure message, source
location. Omit the address for the whole run.

Assertions reach the data file only when `IncludeTrackedAssertionsInStepList` is on. When it is off they
exist only in the diagram — `note` finds them there.

### `flow <report> s3 [--step 2] [--service X] [--errors-only]`
The scenario as an interleaved sequence: step bars, annotations, and one line per call with its status,
duration and body pointer. **This replaces reading the diagram** — 1–2 KB against 663 KB.

### `annotations <report> s3`
The example-row markers (`Row 3`) and any fragment the test author injected with
`DefaultTrackingDiagramOverride.InsertPlantUml`, each with the interaction index it sat before. Step and
assertion markers are excluded — those are already in `steps`.

## Aggregation

### `values <report> [s3] --path '$.status' [flags]`

`SELECT value, COUNT(*) … GROUP BY value` where the column is a JSON path evaluated across every matched
body. Aggregate **before** you fetch: one `values` answers "what did this field ever hold" without
printing a single payload.

```
$.status across 44 response bodies (7 distinct bodies)
  "APPROVED"   ×41   e.g. s3/i12
  "DECLINED"   ×2    s3/i40, s7/i2
  (absent)     ×1    s3/i50
12 calls carried no body
```

| Flag | Effect |
|---|---|
| `--path '$.x'` | required; the full path grammar applies (`[*]` fans out over every element) |
| `--service X` / `--status 5xx` / `--method M` / `--step 2` / `--grep URI` | the same filters `interactions` takes |
| `--where '$.status = APPROVED'` | body-content predicate, same grammar as `interactions --where` |
| `--stats` | numeric summary: count/absent/non-numeric/distinct, min/median/max/sum/mean — min and max carry the address of the extreme |
| `--request` / `--both` | target request bodies, or both (rows tagged `req`/`resp`); default is the response body |

- Scope is the whole run, or one scenario with `s3`.
- **Counting is per occurrence, not per distinct body** — the same body arriving 41 times counts 41
  times (each distinct body is still parsed only once).
- A body the path misses is counted as `(absent)` — silence would hide exactly the bug this finds.
- Bodiless calls, unpaired calls (no response to evaluate) and non-JSON bodies are footnoted, never
  silently dropped.

## Payloads

Nothing here prints a payload that was not named.

### `interactions <report> [s3] [flags]`
One row per request: address, service, method and path, status, duration, and body pointers
(`b:hash` + size) for the request and the response. Without an address it covers the whole run — rows
print full `s3/i47` addresses either way.

| Flag | Effect |
|---|---|
| `--service X` | substring match |
| `--status 5xx` | a class, or an exact status |
| `--method GET` | |
| `--step 2` | only calls inside that step |
| `--grep T` | substring match on the URI |
| `--group` | fold runs of identical calls into one row with `×N` |
| `--where '$.success = false'` | body-content predicate — see below |

Statuses, durations and response body pointers come from exact request/response pairing
(`requestResponseId`), so interleaved parallel calls to one service each show their own status.

#### `--where` — the WHERE clause

```
kronikol query interactions <report> s3 --where '$.success = false'
kronikol query interactions <report> --where '$.items[*].price < 0' --where 'req:$.currency = GBP'
```

Grammar: `[req:]PATH OP LITERAL` · ops `= != < > <= >= ~ !~ exists !exists` (`exists` takes no
literal) · literals: `null`/`true`/`false`, numbers, quoted or bare strings. Both sides numeric →
numeric comparison; otherwise case-insensitive string comparison; `~` is substring.

- Wildcards use **any**-semantics: `$.items[*].price < 0` passes when any element satisfies.
- Repeated `--where` is AND. OR is deliberately absent — run the command twice.
- Default target is the **response** body; a `req:` prefix targets the request per-expression;
  `--request` shifts the default.
- A call whose targeted body is missing or not JSON fails the predicate; the footer reports how many
  were excluded that way.
- Works on `interactions` and `values`. Single-quote the expression — `>`, `?` and `[*]` are shell-active.

#### `--group-by` — generic bucketing

```
kronikol query interactions <report> [s3] --group-by service,status [--sort errors] [--where …]
```

```
service      status               calls  errors   median      max  bodies
payments     OK                      38       0    12 ms    80 ms       4
payments     InternalServerError      2       2   230 ms   410 ms       1
```

Dimensions (comma list, any order): `service`, `method`, `status`, `path` (URI path, query stripped),
`step`, `phase`, `category`, `kind` (metaType), `capturedBy`. `bodies` is the number of distinct
response bodies in the bucket — a bucket with 120 calls and 1 body is one fact. Index-only unless
combined with `--where`; composes with every filter. Default sort is calls descending;
`--sort errors|duration` (a bucket has no single byte total, so `--sort bytes` — valid on `services` —
exits 2 here rather than quietly ordering by calls). The `next:` line carries `--sort` forward: an offset
counted against one ordering is meaningless against another. **`--sort` needs `--group-by`** — the
ungrouped listing is capture order and cannot be sorted, so it exits 2 rather than accepting a flag it
would discard. Distinct from `--group` (which folds *adjacent identical* calls in sequence
order) — the two don't compose. At run scope, `step` buckets collide across scenarios and the header
says so.

### `http <report> s3/i47 | b:4bdea521 [flags]`
The interaction: direction, participants, method, URI, status, duration, owning step, the address of the
call's other half (`response s3/i50` on a request, `answers s3/i47` on a response), W3C trace and span
ids, phase, dependency category, capture path.

Given a `b:` address instead, it names every call carrying that payload and describes the first.

With no payload flag it *describes* the body — size, `b:` address, how many other places it occurs — and
lists the cheap ways to look at it.

| Flag | Effect |
|---|---|
| `--headers` | the header block |
| `--keys` | the body's shape: one line per path, with type and a sample |
| `--path '$.a.b[2].c'` | one value — see the path grammar below |
| `--lines 20-60` | a window of the pretty-printed body |
| `--body` | all of it, subject to the budget |
| `--out FILE` | write it out and print one line |

#### Path grammar

| Segment | Meaning |
|---|---|
| `.name` | object property |
| `[2]` | array index |
| `[*]` | every element — prints one row per match, each with its concrete path (`$.items[2].price = 4173`), paged |
| `['a.b']` | bracket-quoted property, for keys containing dots |
| `.length()` | terminal only: array → element count, object → property count, string → char count |

**Always single-quote a path** (`--path '$.items[*].price'`): `[*]` is shell-active in bash and
PowerShell, and an unquoted one surfaces as a baffling miss rather than a shell error.

A miss suggests the nearest key that does exist (`$.data.custmers is not in this body — nearest:
$.data.customers`). A result too big for the budget *describes itself* — kind, element count, size, and
the flags that window it — instead of refusing.

### `body <report> b:4bdea521 | s3/i47 [same payload flags]`
The same views, addressed by content **or** by location, plus every address the body occurs at. Two
identical bodies are one entry: reading it once is reading all of them.

Both address kinds work here and in `http`, because both are addresses a listing hands you and neither
tells you which verb it belongs to. `body s3/i47` is the payload that call carried; `http b:4bdea521`
names the calls that carry that payload and describes the first.

A listing folds a request and the response that answered it into one row, under the **request's**
address — so a response body's own address (`s3/i50`) appears in no listing. Both ends now say so: the
occurrence list marks it (`s3/i50  (the response to s3/i47)`) and `http s3/i47` prints a `response`
line naming it. That is the address that fetches the response body.

### `note <report> s3/d0 [/n12] [--out FILE]`
`s3/d0` lists a diagram's notes with sizes; `s3/d0/n12` prints one.

A note is what the **HTML report showed**, which is a rendering of the captured content, not a copy of
it — focus fields, phase variants, GraphQL query-only mode and user formatting processors all change it,
and a processor can add information found nowhere else. This is where to look when the user quotes
something the payloads do not contain.

### `diagram <report> s3/d0 --out FILE`
The raw PlantUML. **Refuses to print to stdout** — a real one is 663 KB — and points at `flow` instead.

## Search and comparison

### `grep <report> "4173" [--in ...] [--values] [--number [--tolerance 0.5|1%]] [--count]`
Returns **addresses**, not content.

`--number` matches *numerically*, across formatting: the needle `4,173.00` finds the raw `4173`, and a
body's `"4.173,00"` (European decimal comma) finds the needle `4173` — every token is read under both
separator conventions. Currency symbols and `_` separators are stripped. On JSON bodies a numeric match
is always a value match, so `--number` always emits paths (`s3/i47  body  $.data.total = 4173`), and
when the matched text differed from the needle it says so (`$.display = "4,173.00" (≈ 4173)`).
`--tolerance 0.5` (absolute) or `--tolerance 1%` (relative) widens the match; the default is exact with
a 1e-9 relative epsilon so `4173.0` matches `4173`. A non-numeric needle with `--number` exits 2 —
drop the flag for text search.

`--in` picks the targets, as a comma list: `bodies`, `uris`, `steps`, `assertions`, `names` and `errors`
are the default set; `headers` and `notes` are opt-in, and notes are searched last because they are the
expensive target. `names` covers the feature and scenario titles; `errors` covers a scenario's
`errorMessage` and `errorStackTrace`, and a stack hit prints the frame the needle is in rather than the
whole trace. Both are in the default set because they are the answer to the question the verb is usually
asked, and because both are already in the index and open no payload.
An unknown target exits 2 rather than searching nothing — a silently dropped `--in bodys` would have read
exactly like proof the value is absent — and so does an empty one (`--in ""`, `--in ,`), which is what you
get from joining a list that came back empty. Every paging footer carries `--in` and `--values` forward, so
the command in a `next:` line continues the search you asked for rather than the default one.

`--values` names the JSON path a match came from: `s3/i47  $.data.customers[2].total = 4173`. This is the
command for "the number on screen is wrong" — it finds where the number entered the system.

Bodies are searched once per distinct content, not once per occurrence.

### `trace <report> <id | prefix | s3/i47>`

Follows a W3C trace id across the whole run — the ids `http` prints (`activityTraceId`), matched to
your OTel traces and app logs. Takes the full id, an unambiguous prefix of at least 8 hex chars, or an
interaction address (that call's trace).

```
trace 4bf92f35… — 7 calls across 2 scenarios
  +0 ms     s3/i12   api        POST /orders            202      span 00f067aa
  +12 ms    s3/i14   payments   POST /charge            200      span a1b2c3d4
  +80 ms    s7/i2    payments   POST /charge            500      span e5f60718
! spans 2 scenarios (s3, s7) — shared state or fixture leakage
```

Rows are chronological with offsets from the first (file order, flagged, when a timestamp is missing).
**The cross-scenario warning is the command's second job**: a trace id that leaks across scenarios is
the classic flaky-test smell, and nothing else in the tool can see it. Parent span ids are not captured,
so this is the chronology of the trace, not its tree. An ambiguous prefix exits 2 listing the
candidates; an unenriched report or an untraced call is told to re-run on a current Kronikol.

### `compare <report> s3 s7`
Two scenarios side by side: example values, the first differing steps, the first differing calls, and how
many bodies are byte-identical — plus the address of the first differing body, ready to paste into
`diff` (`first differing body: diff s3/i12 s7/i12`). A passing neighbour is the best available oracle
for a failing scenario.

### `diff` — bodies and runs

```
kronikol query diff <report> s3/i47 s7/i47       # two interactions' bodies, one report
kronikol query diff <report> b:4bdea521 b:9f31c02a
kronikol query diff <old.json> <new.json>        # two runs, matched on stableId
kronikol query diff <old.json> <new.json> --body s3/i47   # the same call across two runs
kronikol query diff <report> --baseline          # against last-green, resolved for you
```

**Body diff** prints only the paths that differ — never a payload:

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

- Identical hashes answer `byte-identical` from the index without reading anything.
- An added/removed subtree is one row with a shape summary (`{sku, price, qty}`, `[3 elements]`), never a dump.
- An array where one insert shifted everything collapses to one honest row
  (`$.items: elements shifted/reordered — 9 vs 10, 8 identical`) instead of a page of misleading per-index rows.
- Non-JSON bodies fall back to a line diff (`line 12:  - … / + …`).
- Two scenario addresses are refused with a pointer at `compare`.

**Run diff** (two files) reports what broke, was fixed, is new, got slower, disappeared — matched on
`stableId`, so one row of a scenario outline is distinguished from another. `--body s3/i47` resolves the
address in the *old* report, matches the scenario into the new run by `stableId` (ordinals shift between
runs), and diffs that one call's bodies across the two files. A pair that cannot be matched — only one
side has `stableId`s, or none agree while the scenario names do — is refused with exit 2 naming the side
or the two suites, never "matched by position" into a page of new-and-gone.

It also reports **Tracking** losses, over the scenarios both runs hold: services that captured fewer
calls than in the older run, any that fell to zero, a service one scenario stopped seeing entirely while
the total held, and — as a total loss — a new run that captured nothing at all. Calls in scenarios only
one run has are stated (`not compared: …`), never counted into the comparison, so an added or removed
test neither hides a loss nor invents one. Nothing fails when a client stops being tracked — the tests
still pass and the diagrams are just thinner — so this is the only place that regression shows up. The
one absence that is not a loss is a side that cannot carry traffic at all (a mergeable file written
before 3.1.0); that side is noted and the section skipped.

**`--baseline`** names only the current report and resolves the other side itself:
`<reports>/baseline/TestRunReport.json` beside it, else `$KRONIKOL_BASELINE` (a report, or a directory
holding one), else exit 2 naming both. The argument order inverts on purpose — `diff old new` names the
old report first, `diff <report> --baseline` names the current one — but the output is oriented the same
way either way: `-` is the older run, `+` the newer, `BROKE` means it passed then and fails now.

### `history [<report>] [s3 | sid:<id>] [--run ID] [--sid ID] [--window N] [--flaky|--new|--failing|--regressed|--changed] [--branch NAME] [--compare-branch NAME] [--min-runs N] [--alternating-runs N] [--count-runs N] [--degraded-by X] [--calls] [--suite NAME] [--history FILE]`

```
kronikol query history --run last-failed         # no report: the failing run a re-run overwrote, from the ledger
kronikol query history --run T101611Z --sid 3fa9c0d1e2b47a65   # one scenario of one run, by a unique part of its id
kronikol query history <report>                  # every scenario with a verdict, regressions first
kronikol query history <report> --flaky          # only the ones the ledger calls flaky
kronikol query history <report> s3               # one scenario: its verdicts, the numbers, its last runs
kronikol query history <report> --branch main --compare-branch feature/x
```

What the last runs say about this one, read from the cross-run ledger — the append-only
`.kronikol/history.jsonl` a run appends to (or, on CI, the `History.run.json` fragments that
`kronikol history record` folds into it). The first line is the run's summary — `1 broke, 2 flaky
(against 12 earlier runs on main)` — then one entry per scenario with a verdict:

```
s3  Checkout › Pay with an expired card
  broke              PPPPPF     passed in gh:18273645:1 (abc1234), failing now
s7  Refunds › Refund twice
  flaky              PFPPFPPPFP failed 3 of the last 10 runs with a verdict, 6 flips, last failed 1 run ago
```

The vocabulary, and what each word is measured from:

| Verdict | Meaning |
|---|---|
| `broke` | failing now, passing in the previous run that had a verdict — a regression |
| `failing` | failing now and in the previous run; the evidence says since which run |
| `always-failing` | failing in every run of the window; it has never been seen passing |
| `fixed` | passing now, failing in the previous run |
| `flaky` | it has failed, recovered and failed again, at or above the flip rate — or it passed on a retry. **Flip rate, not fail rate**: five failures in a row is one break and one fix; five alternating is flakiness |
| `new` | not in any earlier run of this stream |
| `slower` | above the p95 of its durations in earlier full runs by the factor, in this run and the previous one. Never read in a degraded run, or against one |
| `behaviour-changed` | the same status with a different set of calls than the previous run; the evidence gives the call counts |
| `reordered` | the same calls in a different order — only reported when the run asked for it |
| `alternating` | back on a set of calls it held within the last ten passing runs, with a different set in between: a scenario with two states (a warm cache and a cold one), not a change. The evidence names the other state |
| `unstable-shape` | the calls change most runs, so behaviour verdicts are suppressed for it |
| `quarantined` | on `.kronikol/quarantine.json`; additive, with the reason |
| `unknown` | no verdict — a defaulted result, or a failure with nothing earlier to compare against |

The series (`PPPPPF`) is the last results oldest first, this run last: `P` passed, `F` failed, `S`
skipped, `?` no verdict, `.` not in that run, `N` not a test (the scenario an ingest folds unattributed
traffic into: it reads no verdict and is never new or absent). Verdicts are computed **within a branch stream** (the
run's own branch; runs off CI form the `local` stream), so a feature branch's failure is not main's
history — `--branch` reads against another stream and `--compare-branch` adds a second reading of the
same run. A run that lacks more than a tenth of the previous run's scenarios (a filtered run, a
crashed half) is **partial**: nothing is reported absent from it, and its durations and calls are not
what a full run is compared against (a filtered run is alone on the machine, or pays the cold start with
fewer scenarios to spread it over). Its passes and failures are read like any other run's, and its rows
are marked `(partial)`. Below the minimum number of runs the status verdicts still apply, and the header says how
many runs are recorded and how many flakiness needs. On a pull request build, `history` without `--branch` reads against the branch the pull request targets, as the run itself did (3.13.0).

`s3` answers for one scenario in full: every verdict, the flip and fail rates, since when it has been
failing, its duration against the p95, its calls against the previous run, its quarantine entry, and
its last runs one per line with commit and duration. `--json` carries the same rows plus a `history`
member with the run-level summary, the counts per verdict, the absent scenarios and any new
`caller>service` dependency pairs.

**The report is not the run you want (3.26.0).** A re-run overwrites the report; the ledger still has the
run it replaced. `history` is the one verb that answers with **no report**: the ledger is found the usual
way (`--history`, `$KRONIKOL_HISTORY`, the `.kronikol/history.jsonl` above the working directory), the
run read is the newest of the suite or the one `--run` names, and it is read against the runs recorded
**before** it, behaviour verdicts included. Rows are addressed `sid:<id>` (a report's `s3` is an ordinal
nobody else has) and `--sid <id>` asks for one; the header says `ledger only — no report read; steps,
calls and payloads need one`. `--run` takes a whole id, any part of one that only one id has,
`last-failed` or `previous`; a name that fits no run or several is exit 2 **listing the last ten runs
with their pass and fail counts**, so the refusal is the index. Several suites in the ledger and no
`--suite` is exit 2 naming them. With a report **and** `--run`, the report names the ledger and the
suite and the flag names the run; a run other than the report's own is read from the ledger under a
`! <report> describes <run> — reading <other run> from the ledger instead` banner. A `next:` pointer
always carries the run an alias resolved to, never the alias.

**An empty filter says what it is empty of (3.26.0).** `--failing` on a green re-run used to print `no
scenario with a verdict (failing)`, which reads as "nothing failed". It now reads `no scenario is
failing in this run`, and then points at what the window holds: `1 scenario here failed earlier in the
window: s8 (2 runs ago, local:…T101611Z) · next: history s8` (failures up to five runs back are named,
at most five, older ones are counted with the address of the newest). `failing` is a verdict - failing
now **and** before - so a run in which something **broke** is told `no scenario has been failing since
an earlier run (what fails in this one broke in it)` with the scenarios named. `--flaky` names the
scenarios that have flipped without being flaky and gives the analyzer's own reason: `s8 — 2 flips, one
failing episode; flaky needs two` (one break and one fix is two flips and ONE episode; `--min-runs` is
named only when the bar is the sole obstacle). A **partial** run says so on its own line - `this run is
partial: 34 scenarios, the last full run had 396 — a filter answers for these 34 only` - and adds
`also: 3 scenarios outside this partial run were failing when they last ran (most recently in <run>) ·
next: history --run <run>`. The scenario view gains `failing episodes N` beside the flips. `--json`:
`history.source` (`report` or `ledger`), `history.previousFullCount`, `history.nearMisses` (`kind`
`failed-earlier` / `failed-now` / `flips-not-flaky`, `reason`, `runId`, `runsAgo`),
`history.failingOutside`, and `failingEpisodes` on each row; `report` is `null` with no report.

**Nothing in the `s3` view is cut (3.25.3).** A failed run's stored error is printed whole on its own
line under the row, the evidence is whole, and every new and gone call is listed (`new:` / `gone:`, one
per line) where the evidence names three and says `and N more`. No free-text field is complete only
in `--json`. The one limit left is the ledger's own: it keeps the **first line** of a message, up to
199 characters, and text that ends in its `…` is followed by `… first line only — the whole message is
in that run's Failures.md`. The run view is a list and does cut its evidence column, keeping both ends
around ` … ` (the end of an error is where "but found Y" is); when it has cut anything the footer says
`… marks cut text — history s3 prints it whole`, naming the first row it cut.

**When `behaviour-changed` looks like noise.** The fingerprint is made from templated call lines, and a
variable part the templater does not know (an application's own cache-key format) changes it on every
run. The evidence says so when it can: `…; the calls differ only in what looks like an id (sess_ab1… →
sess_zz9…): a HistoryShapeTemplates rule would make them compare equal`. It is a hint and the verdict
stands, because `/v2/` becoming `/v3/` looks the same. `history <report> s3 --calls` prints what the
templater made of the scenario's calls; the fix is a rule in the suite's
`ReportConfigurationOptions.HistoryShapeTemplates`, which costs one quiet run.

**A degraded run** is one whose passing scenarios took `--degraded-by` (2.0) times their usual, the usual
being a scenario's median passing duration over the other full runs. A failure inside one is weak
evidence against the test, so it is labelled, never discounted:

```
runs seen: 8 · verdicts: 9 · failed 1 (1 in a degraded run) · flips 2 · flip rate 0.25 · fail rate 0.11 · last failed 2 run(s) ago
  F  gh:7:1  2026-09-01T07:00:00Z  c000007  82215 ms (6.4× usual)  [run degraded: passing scenarios took 2.3× their usual]  The service bigquery has thrown…
```

`[run degraded: …]` is a fact about the run, and the only note that says anything about the machine.
`(6.4× usual)` is a fact about the reading and **not a cause**: a failing test is usually slow because it
failed (a polling assertion ran to its timeout). It prints on any row at or over the factor and at least
100 ms over its usual. The run view says `degraded: passing scenarios took 7.4× their usual, so no
scenario is read slower in this run` once, under the ledger line; `--json` carries `history.pace`,
`history.degraded`, `failuresInDegradedRuns`, and per run `timesUsual`, `overUsual`, `runDegraded`,
`partial`. The duration line reads `p95 of earlier full runs, at this run's speed`: the bar is scaled to
the run being read, so it can stand above every raw reading under it.

The verdicts are the library's own — the same analysis the run performed when it wrote `Failures.md`
(which carries a `History:` line per failure and works through regressions first) and
`ctrf-report.json` (`extra.kronikolHistory`, and `flaky` set from the ledger) — read again here
against the ledger as it stands now. `failures` prints the same `history:` line under each failure
whenever a ledger resolves on its own, and says nothing about history when none does.

**The CI gate is not a query verb.** `kronikol history gate <report>` reads the same ledger and trips on
what it says is *new* - a failure carrying the broke, new or unknown verdict that is neither flaky nor
quarantined - and reads the rest out without tripping. Its fail-on list (new-failures, flaky,
duration-regression, behaviour-change) widens it; below the minimum number of recorded runs the flaky,
duration and behaviour categories are advisory, and its min-runs flag gives it the bar the suite reports
with. On a pull request build it reads against the branch the pull request targets, as the run did.
Exit 0 clean, 1 tripped, 2 usage. When a build is red
and the gate said `gate: passed`, the failure is one the ledger already knew about, and `history
<report>` shows which verdict it carries. `kronikol history quarantine` parks one with a reason on
file; `kronikol history doctor` explains a ledger that behaves oddly, and `kronikol history doctor
<reports-dir>` (3.27.0) says which runs are kept under `runs/`, which is the newest failing one, and what
an interrupted rotation left - whether or not history is on.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | answered |
| 1 | the report could not be read, is not valid JSON, or was replaced by a finishing run while it was being read (`… changed while it was being read; run the command again`, 3.25.3) |
| 2 | bad usage — unknown command, malformed address, out-of-range ordinal, ambiguous directory |

The message says what the valid range or spelling is; it is worth reading rather than guessing again.
