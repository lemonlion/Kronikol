---
name: kronikol-test-debugging
description: >
  Debug a test run from a Kronikol report (TestRunReport.json) — why a test failed, what a service actually
  returned, where a wrong value came from, what changed between runs, what was slow. Use whenever a
  Kronikol report exists (.logs/kronikol/, Reports/, TestResults/) and the question is about test
  behaviour. Never read TestRunReport.json directly: reports reach 10 MB / 2.7M tokens and a single
  embedded diagram can be 166k tokens.
---

# Debugging a Kronikol test run

## The rule

**Never `Read`, `cat`, `Grep` or otherwise open `TestRunReport.json`, `TestRunReport.html`, or a diagram.**

Not "prefer not to" — it does not work. A measured report was **10.7 MB ≈ 2.7 million tokens**, with one
embedded PlantUML diagram of **663 KB ≈ 166,000 tokens**: a single diagram larger than most context
windows. Opening the file is not a slow way to answer the question, it is a way to end the session with
the question unanswered.

Use `kronikol query` instead. Every command prints an answer plus the addresses that fetch the next thing.

```
dotnet tool install -g Kronikol.Tool      # once, if `kronikol` is not on PATH
kronikol query summary .logs/kronikol/TestRunReport.json
```

`<report>` can be the directory holding the report; the tool finds it, and says so if there are several.
If the tool is genuinely unavailable, use `scripts/query.py` in this skill — it degrades to a smaller set
of commands, not to reading the file.

## What is cheap and what is not

A report is four layers, and only one of them is big:

| Layer | Share of the file | So |
|---|---|---|
| Narrative — features, scenarios, steps, assertions | **0.4%** | pull the whole tree without hesitating |
| Topology — who called whom, in what order, with what status | ~1% | `flow` renders it in 1–2 KB |
| Artifacts — attachments, diagnostics | ~0.2% | free |
| **Payloads — bodies and headers** | **~90%** | only ever fetch one you have named |

An agent that knows `steps` is a rounding error asks for the whole tree at once. One that does not pages
through it and learns less for more tokens.

The payloads are not the enemy — a body is usually *the* answer, because that is where a wrong number
actually comes from. The discipline is to reach one deliberately, by address, rather than sweeping all of
them into context on the way past.

## The ladder

```
summary  →  failures | scenarios  →  steps s3  →  services s3  →  interactions s3  →  values --path '$.x'  →  http s3/i47 --keys  →  http s3/i47 --path '$.x'
```

**Stop at the first rung that answers the question.** Most questions end at rung three. `failures` alone
usually answers "why did these tests fail". **Aggregate before you fetch**: when the question is about a
field across many calls ("what did `$.status` ever hold?"), `values --path` answers it in one command
without printing a single payload — reach `http` only when one specific body matters.

## Recipes

| The user says | Do this |
|---|---|
| "why did these tests fail?" | `failures` — usually sufficient on its own |
| "the number on screen is wrong" | `grep "<value>" --values` → `http <addr> --path $...` → `compare s<failing> s<passing>` |
| "did it even call X?" | `services` — absence is the answer; no payload needed |
| "what did X return?" | `interactions s3 --service X` → `http s3/iN --keys` → `--path` |
| "what values did X ever return?" | `values --path '$.field' --service X` — distinct values, counted, with addresses |
| "which example row broke?" | `steps s3` (its parameters) + `annotations s3` |
| "what broke since yesterday?" | `diff old.json new.json` — matched on `stableId` |
| "why is this slow?" | `summary` → `services --sort duration` → `flow s3` |
| "is this flaky?" | `diff` across runs; `stableId` survives re-runs, ordinals do not |
| "the report shows X but I can't find it" | `note s3/d0` — see **Notes are a rendering** below |
| "show me the flow" | `flow s3` — never the diagram |
| "what happened in step 2?" | `interactions s3 --step 2`, or `flow s3 --step 2` |

## Budget discipline

- Read the `… 24 of 127 · next: --offset 24` footer. It is always there; if you did not see one, you saw
  everything.
- **Filter harder before paging.** `--service`, `--status 5xx`, `--step`, `--grep` and `--group` all beat
  `--offset`.
- `--count` for yes/no questions. One token instead of a listing.
- `--group` folds runs of identical calls into one row. A hundred and twenty calls to one cache key are
  one fact.
- Above ~10 KB, `--out FILE` and then `Grep` the file. `wrote 64 KB → ./body.json` costs six tokens;
  printing it costs sixteen thousand.
- `--max-bytes 0` removes the budget. Rarely the right move; never the first one.

## Traps

- **Payloads come from `httpInteractions`, not from diagram notes** — but a note is a *rendering* of a
  payload, not a copy of it. Focus fields, phase variants, GraphQL query-only mode and user-supplied
  formatting processors all change what a note shows, and a processor can *add* information that exists
  nowhere else. So if the user quotes something you cannot find, `note s3/d0` is where it is.
- **`b:` addresses are content hashes.** The same hash means byte-identical: read it once, and know that
  every other address carrying that hash holds exactly the same bytes.
- **Ordinals are per-file.** `s3/i47` means nothing in another run. Across runs use `stableId` (printed by
  `steps`) and `b:` hashes.
- **A body ending `…truncated (N chars total)` was capped at capture time.** The rest was never recorded —
  it is not somewhere else in the file. `query` says so when it prints one.
- **Attachments are pointers.** `failures` and `steps` print absolute paths for screenshots; `Read` them
  individually. They are never inlined.
- **`traceId` is not the W3C trace id.** `traceId` is Kronikol's own identifier for the request/response
  pair; `activityTraceId` (printed by `http`) is the W3C one that matches your OTel traces and app logs.
- **Aggregate counts are per occurrence, not per distinct body.** `values` counts a body every time it
  arrived, because the question is "what did the system see" — the `(N distinct bodies)` in its header is
  where the dedup shows.
- **If a command prints `! report predates step attribution`,** that file was written by an older
  Kronikol: assertion messages, source locations and `stepPath` are absent from it. The answers are still
  correct, just thinner. Re-running the suite on a current Kronikol fills them in.

## Answering

Cite addresses — `s3/i47`, `b:4bdea521`, `OverviewTests.cs:142` — so the user can verify any claim with
one command. When you say a service was never called, say which command showed it. When you quote a
value, say which path in which body it came from.

Full flag reference: `references/commands.md`.
