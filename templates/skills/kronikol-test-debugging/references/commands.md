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

Ordinals are deterministic for a given file (features by display name, scenarios in file order).
Across runs, use `stableId` and `b:` hashes — both survive a re-run.

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
- Errors are unchanged: plain text on stderr, exit 2 for usage and 1 for a read failure. Nothing is
  written to stdout on a non-zero exit, in either format.

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

Prints `nothing failed` on a green run rather than an empty response.

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

`--in` picks the targets, as a comma list: `bodies`, `uris`, `steps` and `assertions` are the default
set; `headers` and `notes` are opt-in, and notes are searched last because they are the expensive target.
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

**Run diff** (two files) reports what broke, was fixed, got slower, disappeared — matched on `stableId`,
so one row of a scenario outline is distinguished from another. `--body s3/i47` resolves the address in
the *old* report, matches the scenario into the new run by `stableId` (ordinals shift between runs), and
diffs that one call's bodies across the two files.

It also reports **Tracking** losses: services that captured fewer calls than in the older run, and any
that fell to zero. Nothing fails when a client stops being tracked — the tests still pass and the
diagrams are just thinner — so this is the only place that regression shows up.

**`--baseline`** names only the current report and resolves the other side itself:
`<reports>/baseline/TestRunReport.json` beside it, else `$KRONIKOL_BASELINE` (a report, or a directory
holding one), else exit 2 naming both. The argument order inverts on purpose — `diff old new` names the
old report first, `diff <report> --baseline` names the current one — but the output is oriented the same
way either way: `-` is the older run, `+` the newer, `BROKE` means it passed then and fails now.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | answered |
| 1 | the report could not be read, or is not valid JSON |
| 2 | bad usage — unknown command, malformed address, out-of-range ordinal, ambiguous directory |

The message says what the valid range or spelling is; it is worth reading rather than guessing again.
