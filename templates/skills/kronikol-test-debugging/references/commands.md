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
| body | `b:4bdea521` | first 8 hex of SHA-1 of the content — stable across runs and scenarios |
| diagram | `s3/d0` | |
| note within a diagram | `s3/d0/n12` | |

Ordinals are deterministic for a given file (features by display name, scenarios in file order).
Across runs, use `stableId` and `b:` hashes — both survive a re-run.

## Shared flags

| Flag | Effect |
|---|---|
| `--max-bytes N` | output budget, default `6000`; `0` removes it |
| `--offset N` | resume a truncated listing at row N |
| `--limit N` | cap rows |
| `--count` | print how many matched, and nothing else |
| `--out FILE` | write the payload to a file; prints one line |

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
given an address.

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

## Payloads

Nothing here prints a payload that was not named.

### `interactions <report> s3 [flags]`
One row per request: address, service, method and path, status, duration, and body pointers
(`b:hash` + size) for the request and the response.

| Flag | Effect |
|---|---|
| `--service X` | substring match |
| `--status 5xx` | a class, or an exact status |
| `--method GET` | |
| `--step 2` | only calls inside that step |
| `--grep T` | substring match on the URI |
| `--group` | fold runs of identical calls into one row with `×N` |

### `http <report> s3/i47 [flags]`
The interaction: direction, participants, method, URI, status, duration, owning step, W3C trace and span
ids, phase, dependency category, capture path.

With no payload flag it *describes* the body — size, `b:` address, how many other places it occurs — and
lists the cheap ways to look at it.

| Flag | Effect |
|---|---|
| `--headers` | the header block |
| `--keys` | the body's shape: one line per path, with type and a sample |
| `--path $.a.b[2].c` | one value |
| `--lines 20-60` | a window of the pretty-printed body |
| `--body` | all of it, subject to the budget |
| `--out FILE` | write it out and print one line |

### `body <report> b:4bdea521 [same payload flags]`
The same views, addressed by content instead of by location, plus every address the body occurs at. Two
identical bodies are one entry: reading it once is reading all of them.

### `note <report> s3/d0 [/n12] [--out FILE]`
`s3/d0` lists a diagram's notes with sizes; `s3/d0/n12` prints one.

A note is what the **HTML report showed**, which is a rendering of the captured content, not a copy of
it — focus fields, phase variants, GraphQL query-only mode and user formatting processors all change it,
and a processor can add information found nowhere else. This is where to look when the user quotes
something the payloads do not contain.

### `diagram <report> s3/d0 --out FILE`
The raw PlantUML. **Refuses to print to stdout** — a real one is 663 KB — and points at `flow` instead.

## Search and comparison

### `grep <report> "4173" [--in ...] [--values] [--count]`
Returns **addresses**, not content.

`--in` defaults to `bodies,uris,steps,assertions`; add `headers` and `notes`. Notes are searched last
because they are the expensive target.

`--values` names the JSON path a match came from: `s3/i47  $.data.customers[2].total = 4173`. This is the
command for "the number on screen is wrong" — it finds where the number entered the system.

Bodies are searched once per distinct content, not once per occurrence.

### `compare <report> s3 s7`
Two scenarios side by side: example values, the first differing steps, the first differing calls, and how
many bodies are byte-identical. A passing neighbour is the best available oracle for a failing scenario.

### `diff <old.json> <new.json>`
Two runs matched on `stableId`: what broke, what was fixed, what got materially slower, what disappeared.
Because example values are part of `stableId`, one row of a scenario outline is distinguished from another.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | answered |
| 1 | the report could not be read, or is not valid JSON |
| 2 | bad usage — unknown command, malformed address, out-of-range ordinal, ambiguous directory |

The message says what the valid range or spelling is; it is worth reading rather than guessing again.
