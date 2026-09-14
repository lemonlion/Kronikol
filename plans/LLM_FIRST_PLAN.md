# LLM_FIRST_PLAN — the agent lane, audited against itself

Status: **investigation complete (2026-09-12), NOT green-lit — but §5.4's three no-green-light-needed items and §5.2 row 3 (`runAttempt`) are now done (§16).** Everything else is unimplemented. Written against
the uncommitted 3.1.0 working tree while M2.8 was still being typed into it, so §1.2 records what
moved underneath the audit and §14.3 records what that invalidated.

> **Staleness note, 2026-09-12 16:46 — the target moved during the audit, exactly as §1.2 warns.**
> **M2.9 (`--json`) landed while this plan was being written.** `QueryOptions.Parse:158` accepts
> `--json`; `QueryWriter` emits `{formatVersion, command, report, kronikolVersion, notes, <verb data>,
> count|items+total, truncated, next}`; `QueryJsonTests.cs` (695 lines) and `QueryStreamingTests.cs`
> were last touched at 16:39 and 16:44. Every sentence below that says "M2.9 is not started" is wrong;
> §4.9 I1 and §7.1 are corrected in place. **Both survived the change** — see I1. Nothing else in the
> plan was written against `--json`, so nothing else is affected. This is the first entry the loop's
> re-sync step exists to produce.

Successor to `LLM_FRIENDLY_PLAN.md`, which is green-lit and ~90% implemented (M0, M1, M2.1–M2.7 done;
M2.8 and M2.9 landed or landing during this audit; M2.10, M3.1–M3.3 not started). **This plan does not replace it** — that plan
finishes on its own terms. This one covers what auditing the shipped half found: the parts it missed,
the bugs in what it built, the parts it left out deliberately and wrongly, the design flaws worth
reversing while the surface is days old, the extensions beyond its M3, and the *LLM-first* axis it
never had — how an agent discovers Kronikol at all, and why it would choose Kronikol over the free
alternative.

**Breaking changes are in scope and the window is now.** The user's position, 2026-09-12: these
features are days old and it is better to fix them before adoption than to carry them. §5 ranks every
breaking proposal by how much more expensive it becomes after 3.1.0 is tagged. **Seven of them cost
one edit today and a migration later.**

---

## 0. What this pass found, in one sentence

**The discovery loop reaches the agent; what it hands the agent is not reliably true.**

Eighteen read-only lanes produced 144 findings; 108 were put through an adversarial verifier that was
told to break them. **107 of the 108 reached RUN depth — code executed, output observed.** And
**47 of the 108 did not survive as written**: 6 were reversed outright, 41 were narrowed or
re-attributed. That ratio is the most important number in this document and §14.0 is built on it.

Of the 61 that survived unchanged and the 41 that survived smaller, the ones that decide what gets
built cluster into six statements. Each is measured, and each is stated at the scope that survived
verification rather than the scope the surveyor first claimed.

### 0.1 The six that matter

**1. `Failures.md` — M1's flagship — works through one failure in fifteen and says in print that the
other fourteen "are the same failure and need the same fix".** `Kronikol.xUnit3` splices xUnit v3's
`FailureCause` enum name in as line 1 of every `ErrorMessage`
(`TestContextEnumerableExtensions.cs:43`), and both cluster keys in the product take the first line
only. Measured by controlled A/B through the real generator: the same fifteen messages with that
line stripped produce 5 clusters and 9 worked examples (4,152 → 7,278 bytes). The fixture holds **ten
distinct messages and nine distinct normalised keys**, not fifteen — the honest sentence is *nine root
causes read as one, and fourteen failures suppressed where six should have been.* Three corrections
the verification caught, and one the error-class critic caught afterwards. **The *prefix* is one
adapter of eight; the *first-line key* is all eight** — C22 measured unprefixed xUnit v2 messages still
collapsing five distinct failures into one cluster keyed `Assert.Equal() Failure: Values differ`, so
"one adapter" is the scope of the prefix and not of the defect, and stating it the other way will be
refuted by the first reader who tries xUnit v2. It is **per `FailureCause`**, so an exception-caused
run collapses under `Exception` just
as totally; and **`Failures.md` does not use `FailureClusterer`** — it carries a private duplicate,
`FailuresDigestGenerator.ClusterKey:286`, under a comment asserting the two match. Patching
`FailureClusterer` alone leaves the digest byte-identical. The same key also renders
`Failure Clusters (1 root cause)` in the shipped HTML.

**2. The one thing nothing else has — the captured calls — is structurally absent from the file the
agent is told to read first.** `FailuresDigestGenerator.BuildCalls` emits a call only when an
interaction is attributed to a step whose `Status == Failed`. The correct predicate, established by
running four shapes, is **"the failing step itself carries an attributed interaction"** — not "the
adapter has steps", which is what the survey said and what a fix built on it would miss. Three
populations get `calls: []`: step-less scenarios (the default non-BDD .NET shape — **667 of 1,282 real
scenarios across the two corpora, 52%**); scenarios whose failure is in a hook, fixture or teardown so
no step is marked failed; and **every `kronikol ingest` run**, because `InteractionRecord.OverrideLog`
never sets `MarkerKind`, so every ingested marker is `Custom`, every `stepPath` is null, and the step
bars leak into `annotations` as raw PlantUML. The Calls section renders in **zero of the five shipped
report fixtures**. `Reports/CLAUDE.md` promises "the calls made inside that step" in the same
directory.

**3. In the default configuration, on GitHub Actions, under `dotnet test`, Kronikol delivers no
run-end signal to the CI log and uploads no artifact.** `WriteCiSummary` defaults false, so the
"Debug this run" section — the one channel M1.0 measured as surviving — is never produced.
`PublishCiArtifacts` also defaults false, so the files are written under `bin/Debug/net10.0/Reports`
on the runner and discarded with the job. The pointer and the `::notice` both ride `Console.WriteLine`
— there are **zero `Console.Error` uses in the entire product outside the Tool** — and M1.0's own
table measured stdout as swallowed by every VSTest runner at `normal`. §3.1's belt-and-braces is one
strap, and it is the strap already measured to break. `kronikol merge`, the report a matrix CI job
actually publishes, emits **six of the outputs not at all**: `Failures.md`, `Failures.jsonl`,
`CLAUDE.md`, `AGENTS.md`, `TestRunReport.schema.json` and the CI summary.

**4. Report generation overwrites a user's `CLAUDE.md` and `AGENTS.md` wholesale, by default, with no
check and no warning — including Kronikol's own `init-agents` output.** Reproduced: `kronikol
init-agents .` merges correctly inside `<!-- kronikol:begin -->` markers; a later `kronikol ingest -o .`
in the same directory destroys the user's rule *and the markers*; re-running `init-agents` then grafts
its block onto the generated body, leaving a repo-root `CLAUDE.md` that permanently opens with *"You
are in a Kronikol reports directory. This file is generated; do not edit it."* Two policies, and they
corrupt each other. The guard that was supposed to keep run data out of that file
(`It_is_static_text_with_no_room_for_run_data`) asserts *parameter types* over `GetMethods()` — it
stays green for an added `Build(string, string scenarioName)` overload, and for a non-string parameter
entirely if the overload is `internal`, because default binding flags never enumerate it.

**5. `TestRunReport.json` does not validate against the `TestRunReport.schema.json` the same build
ships beside it.** The schema declares draft 2020-12 and expresses optionality with OpenAPI's
`nullable` keyword at 48 nodes; 2020-12 has no such keyword, so a conformant validator ignores it and
rejects every emitted null. **All 16 emitted pairs on disk fail, zero clean** (11–917 errors),
including three written by the current build minutes before the check. Two independent conformant
validators agree; ajv passes only because it carries `nullable` as a deliberate OpenAPI extension. It
predates this work — the 2026-09-04 fixture has 36 `nullable` nodes — so **M2.5 widened the defect
from 36 to 48 rather than introducing it**, which is worse: the slice whose stated purpose was "the
schema as the contract" made the contract falser.

**6. The run says false things about outcomes, in four places, with exit 0.** `query services` reports
**313 errors on an all-green production run** because `IsError` treats any non-numeric status outside
`{OK*, Created, Accepted, NoContent}` as a failure, and `Ack` and `Responded` are Kronikol's *own*
default success labels (208 of the 313, 66.5%). `query assertions --failed` answers "no assertions
failed" on a 15-failure run and blames an option that is already on by default. `query failures`
hard-codes 25 in its pager footer, so an agent following `next:` to completion on a 150-failure report
**sees 142 of 150** — and the last page's terminal footer, plus the shipped `CLAUDE.md` rule *"if you
did not see a `next:` footer, you saw everything"*, asserts completeness while two are missing. And
**any valid JSON is accepted as a report**: the schema file sitting next to the real one answers
`nothing failed / 0 scenarios, all passed`, exit 0, empty stderr.

### 0.2 What is good, and must not be rebuilt

Verification confirmed these at RUN depth; they are not in scope for change.

- **M2.1's deep links work, including the case the plan doubted.** In jsdom over the real generated
  HTML: `#sid-` resolves on load, survives a filter click, survives *Clear All*, and resolves on
  `hashchange`. The **exported** HTML works too — `export_html()` produced 706,507 bytes carrying all
  23 `data-stable-id` attributes and the init script, and re-loading it at `#sid-…` opened and scrolled
  to the target. HTML and JSON stableId sets are exactly equal on both fixtures checked.
- **The nested-`CLAUDE.md` mechanism fires — for a path spelled the way the session's cwd is spelled.**
  It was filed as refuted, the refutation was itself refuted (§14.0 row 5), and then the error-class
  critic narrowed it again: a 2×2 over four real Reports directories found the injection present for
  `c:/Code/…` (2/2) and **absent** for `C:/Code/…` (2/2), path case the only variable. **Kronikol's own
  pointer prints an uppercase drive letter**, so an agent following the product's own output defeats
  the channel M1.3 is built on, silently and in both directions. The mechanism is real; the bet is not
  yet sound. Fix is one line — print the reports directory relative to the cwd. See §14.3 row 8.
- **Flag parity between the CLI, `SKILL.md` and `references/commands.md` is exact** — 33 flags, three
  ways, `comm` empty in both directions. M2.8's drift guards work for what they cover.
- **The crash-recovery path works under a hard kill.** A process terminated with `TerminateProcess`
  mid-write left 12,151,381 bytes / 1,471 lines, **all 1,471 parsing as valid JSON with no torn final
  line**, and `kronikol ingest` rebuilt the full output set from it.
- **`CLAUDE.md` and `AGENTS.md` are byte-identical** in every generated directory (4,891 bytes each).
- **`ResultWhenUnknown` detection (M1.4) works end to end** on the ingest lane: the banner prints,
  lands in `diagnostics[]`, and heads `Failures.md` above `# No failures`.
- **Kronikol4J reports are already queryable.** §6 gate 5 was reported unrunnable and is not:
  15 Kronikol4J-produced `TestRunReport.json` files exist under `build/`, and `kronikol query summary`
  over one passes the gate's own criterion today. The survey missed them because its census honoured
  `.gitignore` over a tree whose entire output lives in gitignored directories.

---

## 1. What this plan is written against

### 1.1 The frame, unchanged
Agents read `TestRunReport.json` through `kronikol query`, and `Failures.md` directly. The HTML is
~114K tokens and is for people. `LLM_FRIENDLY_PLAN` §1 is the inventory of what exists and is not
repeated here.

### 1.2 The tree moved under the audit, and it cost real findings
Every lane ran read-only against a working tree another session was editing. Three consequences,
recorded because they are the loop's standing hazard (§13) and not a one-off:

- **`Kronikol.Tool.dll` was rebuilt at 14:37 → 15:02 → 15:04 → 15:10 → 15:34 during the run.** The
  15:04 Debug build routed `query` into `MergeCommand` and rejected every query flag. Two lanes drafted
  findings from it and discarded them; a third kept one that was really the M2.8 author's own
  deliberate mutation probe, in flight. `bin/Release/net10.0` at 15:00 is an *incoherent* build — it
  has M2.1's `#sid-` links and rejects `--max-bytes`.
- **Two findings were stale on arrival.** `CommandTableTests` was rewritten at 15:26:41, ~20 minutes
  after the survey caught it, using the surveyor's own proposed fix. Filing either as open work would
  have sent someone to fix something already fixed.
- **The reference fixtures are not uniformly fresh.** `CiPreview.AllFailing` and `AllPassing` are from
  **2026-09-04** and contain none of M1 — no `Failures.md`, no `CLAUDE.md`, a 16 KB schema against the
  fresh 27 KB. §6 gate 4 names `AllFailing` and **cannot be evaluated against what is on disk.**

**Rule adopted from this, and binding on the loop: every RUN row records the mtime or commit of the
binary and the fixture it ran against.** A RUN result without provenance is a READ result about a
build that may no longer exist.

### 1.3 The corpus is the first thing to fix
There is **no fixture anywhere in the repo with a failing scenario that has steps**, so the digest's
populated-`calls` path is unverified by observation in the repository's own test data. There is **no
mergeable `TestRunReport.json` anywhere on disk**, so every merge finding was established against
synthetic shards built from real reports. `BreakfastProvider`'s six suites contain **zero failed
scenarios**. Until the corpus covers a failing-with-steps run, a real sharded run and a re-generated
`AllFailing`, three whole classes of assertion cannot be made from generated output. **This is M0.0.**

---

## 2. Measured, before designing

### 2.1 The first sixty seconds, finally measured (§6 gate 2, never run until now)

Fixture: `CiPreview.Mixed` (2026-09-12 14:23, verified fresh). Task: *"the tests are failing"* → reach
the root cause of each of the 15 failures. Ground truth, read out of band: **15 failures, ~9 distinct
causes.** Tokens are bytes ÷ 4 (÷ 3 for JSON) — stated as an approximation, not measured with a
tokenizer.

| Path | commands / reads | bytes | ~tokens | causes reached |
|---|---|---|---|---|
| (a) `Failures.md` only | 2 | 13,406 | 3,351 | **9/9 — but only by disobeying the file.** Obeying it: **1/9** after 1 read / 4,180 B |
| (b) console pointer + `query` ladder | 3 (1 wasted) | 6,826 | 1,706 | 9/9, plus the failing HTTP call on screen |
| (c) generated `CLAUDE.md` as the only instruction | 3 | 13,474 | 3,368 | 9/9 |

Three things fall out, and two of them contradict the predecessor plan's design.

- **Rung zero is neither the cheapest nor the most complete path.** The query ladder beats
  `Failures.md` on *both* size (6,826 B vs 13,406 B) and completeness. The plan's ordering — digest
  first, tool second — is backwards on this corpus, and it is backwards *because* of §0.1's defects,
  not because the digest is a bad idea.
- **Obeying `Failures.md` costs you eight of nine causes.** The file's own suppression sentence is what
  stops the reader.
- A separate measurement puts the compression at roughly **3,200:1** against the HTML. That number is
  the single most rankable fact this project has, and nothing publishes it (§8).

### 2.2 The clustering collapse, by A/B
Two ingests differing **only** by a leading `Assertion\n`: A → one cluster of 5, one worked example,
four rows whose Error cell is the bare word `Assertion`. B → no Clusters section, five worked examples
with their real messages. A three-cause variant (3 `Assertion` + 2 `Exception`, all messages distinct)
produced exactly two clusters. The prefix is the sole cause, and the collapse is per-cause, not
per-run.

### 2.3 The calls table, by four shapes — and a census that reopens it
(A) Failed scenario, no steps, real traffic → `calls: []`. (B) Failed top-level step **with** markers →
`calls: [{address: "s0/i0", summary: "POST /cake", status: "OK", durationMs: 11}]`. (B′) Failed
top-level step, no markers in the log stream → `[]`. (C) Steps all Passed, scenario Failed
(fixture/teardown) → `[]`. (D′) Only Failed step is a **sub**-step → `[]`, because the digest addresses
sub-steps as `1.0` while `OrderedStepPaths` only ever attributes `1`.

**Case B is `RUN(proxy — reflection harness, hand-built inputs)`, and the first draft's conclusion from
it — "the mechanism is alive" — is withdrawn.** The bytes came from a harness that loaded
`Kronikol.dll` and called `FailuresDigestGenerator.Generate` with constructed objects. That proves the
generator emits calls *given the shape*; it does not prove any shipped lane produces the shape. A census
over **all 37 `TestRunReport.json` in Kronikol, BreakfastProvider and Kronikol4J** found: 14 reports
carry attributed `stepPath`s (up to 320) and have **zero** failures; the only two reports with failures
have **zero** steps; **scenarios with a failed step carrying an attributed interaction: 0 of 1,284**;
and all five `Failures.jsonl` on disk carry `calls: []` on every row. So **A2 may be an attribution
defect on the failing path rather than only a predicate defect**, and a predicate-only fix would ship a
feature that still emits nothing. M0's failing-with-steps fixture is the precondition for deciding
which, and A2's red test must assert on emitted bytes from a run, never from a harness. Until then
**Q2's fallback is the only thing known to work**, and *the flagship claim of M1 — that the captured
calls are what nothing else has — has never been observed in a generated artifact.*

### 2.4 Schema conformance, by two validators
python `jsonschema` 4.26.0 and `@hyperjump/json-schema` both reject; ajv 8 accepts. On the Mixed
fixture: **917 errors, all of keyword `type`, all with instance exactly `null`, all on properties
carrying `nullable: true`** — nothing else hides behind the count. 43 of the 48 nullable nodes are a
bare `type` and are a mechanical fix; **five are not** (two carry `additionalProperties`, one `items`,
one `format`, and `$defs/step/properties/status` carries an `enum`, where `type: ["string","null"]`
alone still rejects `null` against the enum).

### 2.5 Discovery, measured — and three of four first answers were wrong
- The Claude Code **community plugin marketplace holds 2,282 plugins, zero Kronikol, and no .NET
  test-report reader.** The census is exact. But the field is **not** hosted SaaS with remote MCP —
  1 of 22 sampled entries declares an `mcpServers` block; 15+ are local OSS skill plugins. And
  `category`/`tags` are **not** `plugin.json` fields: category is assigned by Anthropic's review
  pipeline, so a proposal to set them in the manifest is not implementable.
- The **official MCP registry's `search` is substring-over-name only** — verified against its own
  OpenAPI spec and by execution. `search=test-report` returning zero says nothing about the shelf,
  which is **occupied**: `playwright-report-mcp` ("surface structured results for AI agents doing test
  failure analysis" — near-verbatim the M3.1 pitch), `verdict-mcp`, `buildpulse-mcp`, `cypress-cloud`,
  `allure-testops-mcp`, and **seven** distinct `dotnet` servers including `dotnet-coverage-mcp`.
- **The name is unbound, the product is not.** A bare-name web search returns IKEA furniture. But a
  *capability-intent* query with no product name — "autogenerate PlantUML sequence diagrams from xUnit
  component tests NuGet package" — returns `github.com/lemonlion/TestTrackingDiagrams` at **rank 1**,
  which 301-redirects to Kronikol. Lifetime downloads across both identities are **~3.06M**, not the
  4.1K first reported (that was `Kronikol.Tool`, the family's least-installed package). The real
  problem is **orphaned discovery equity**: every indexed asset addresses a dead name, the old package
  is undeprecated with no pointer, and `README.md` contains zero occurrences of "TestTrackingDiagrams".
- `dotnet package search`, `gh search repos`, the GitHub search API and `WebFetch` of the NuGet and
  GitHub pages **all** find Kronikol today. "An agent cannot verify it" is false by four independent
  routes.

### 2.6 The channel, still one strap
`RunSummaryConsoleWriter` has exactly **one** sink — an `Action<string>` filled with
`Console.WriteLine` at `ReportGenerator.cs:394`. §6 gate 1 explicitly asked whether `Console.Error`
fares better under VSTest; the M1.0 table does not answer it and **no stderr measurement exists
anywhere in the repo.** The swap is *not* one delegate: `Write` pushes the `::notice` through the same
delegate, so moving to stderr relocates a GitHub workflow command that only works on stdout.

---

## 3. Design constraints

### 3.1 Never let the tool's voice be forgeable
`query` prints `"! " + message` taken from the report file, unescaped, uncapped, above the header. A
report can forge a byte-identical `! report predates step attribution…` banner and a fabricated
`s99  Checkout › All other tests passed` record, because `QueryCommand.Narrative.cs:34` writes
`scenario.Name` raw. **This is not a new exposure class** — the released 3.0.86 tool could already be
made to forge `!` lines through `kronikolVersion`, feature names and scenario names — but it is a more
convenient instance, and it is the channel an agent reads. Rule: **anything read from a file is data.
The tool composes its own sentences from structured fields; file text is printed below, indented,
single-line, capped, and marked.**

### 3.2 Captured data needs a container, not a sentence
`Failures.md`'s only trust boundary is one English sentence on line 3, with no closing delimiter, no
nonce and no restatement — so on hostile input the **last bytes of the file are attacker-controlled**.
Error and step messages *are* inside fences with a working ```` ``` ````→`'''` substitution, and table
cells go through a pipe/CRLF escape, so "no per-value container" is too strong. What is absent is a
tagged, bounded container, and there are **at least nine** `Escape()`-only emission sites where
captured text lands in a heading or a bare cell. Cluster headings render the first line of an error
message as a real `<h3>` — third-party text in the document's own voice — and an odd backtick count
escapes its span at three sites, yielding live `<img src="https://evil.test/…">` when rendered.

### 3.3 The security boundary is unchanged in class and changed in likelihood
Capture-time redaction stays opt-in and does not touch error messages, step failure messages or stack
traces — the digest's headline content. So `Failures.md` is the same exposure class as
`TestRunReport.json`, as §3.3 of the predecessor claims. But it is **small, readable, agent-targeted
and designed to be pasted**, which changes likelihood materially. Say so rather than resting on
"no new class".

### 3.4 The eight-place rule still binds, and it just cost a silent 40-release data loss
Every field lands in JSON + XML + YAML + schema, twice (.NET and Kronikol4J). The same batch that
added five fields also revealed that `MergeableReportReader.ReadSteps` had never read `failureMessage`,
`sourceFile` or `sourceLine` — written since 3.0.47, dropped on every merge, nothing said so. A
declarative field table driving all writers plus the schema is worth costing (§7.3), and the collapse
should be costed **now**, because every release makes the bill larger.

### 3.5 A `RUN` result is scoped to the environment it ran in
Earned twice in this pass. A finding was RUN-verified in **jsdom** and reversed in a real browser —
nwsapi's selector engine is stricter than both Blink and Gecko. And three probes of the nested-
`CLAUDE.md` mechanism each broke on a different confound and were read as three confirmations of one
cause. **New depth value: `RUN(proxy)`** — executed, but in a substitute for the environment the claim
is about (jsdom for a browser, `ingest` for a framework adapter, a synthetic shard for a real one).
`RUN(proxy)` is not `RUN`, and the ledger says which.

---

## 4. The findings

All 108 verified rows live in §14.1b with their depth and their falsifier. This section groups the
ones that decide what gets built. **Severity and scope are as they stand after verification**, not as
first filed.

### 4.1 The digest lies (A)
| | What | Breaking |
|---|---|---|
| A1 | Cluster key is the first line; xUnit v3 prepends `FailureCause`; the key is **duplicated** in `FailuresDigestGenerator.ClusterKey`, `FailureClusterer.NormalizeKey` and a third first-line site at `:386`, plus the HTML panel. Fix all four or fix nothing. | yes |
| A2 | `calls: []` whenever the failing step carries no attributed interaction — 52% of real scenarios, plus every ingest run, plus every hook/fixture failure | yes |
| A3 | `Escape()` does three replacements; nine emission sites put captured text in headings and bare cells; an odd backtick count breaks out of every inline span | yes |
| A4 | `Truncate` cuts UTF-16 code units, so a non-BMP character at a boundary throws `EncoderFallbackException`; under 8192 units the file is **zero bytes**, at or above it **no file is written and the previous run's survives, stale, beside a stale `.jsonl`** | no |
| A5 | `Failures.jsonl` has **no cap on the message fields**, and `cluster` repeats the first line of `errorMessage` verbatim, so the line is written twice: a 2,000,047-char message yields **4,000,639 bytes**. *(Restated per §14.3 item 15 — the earlier "no per-field cap" was pre-narrowing wording; call summaries **are** capped at 120 at build time, C43.)* | yes |
| A6 | An all-skipped run writes `# No failures — All 12 scenarios passed`; `query failures` and `query summary` print the same falsehood from their own arithmetic | no |
| A7 | `Failures.jsonl` is **0 bytes on a green run** (pinned by a test) and its `formatVersion` has **no reader anywhere** — §3.4's "query refuses unknown versions loudly" is unimplemented | yes |
| A8 | The digest bakes `#sid-` links unconditionally; the CLI gates on the HTML existing. A failed HTML write under the default config produces a dangling link in a persisted file | yes |
| A9 | `errorStackTrace` carries the failing `file:line` and FQN for **26 of 26** failures and the agent lane prints none of it — while the HTML, `CiSummary.md` and the XML export all do, and the emitted `CLAUDE.md` forbids opening the HTML | no |

### 4.2 The loop does not reach CI (B)
| | What | Breaking |
|---|---|---|
| B1 | `WriteCiSummary` **and** `PublishCiArtifacts` both default false; pointer and `::notice` both on stdout, measured swallowed | yes |
| B2 | `kronikol merge` emits none of: `Failures.md`, `Failures.jsonl`, `CLAUDE.md`, `AGENTS.md`, `TestRunReport.schema.json`, `CiSummary.md`, the pointer, the artifact publish | yes |
| B3 | `Console.Error` never measured; the writer has one sink; the `::notice` shares it | no |
| B4 | `Reports/` lives under gitignored `bin/`, so a gitignore-aware content search cannot see it. **The narrowing that dropped this to medium has itself been reversed.** It claimed the root `CLAUDE.md` block is "tracked, greppable and auto-loaded"; *tracked* is **false** (`git show HEAD:CLAUDE.md \| grep -c kronikol:begin` → 0 — it is an uncommitted working-tree edit, so a fresh clone and CI have no pointer at all), while *auto-loaded* is now **measured true** (C86): a session started after the edit carries the block in full, from the **untracked** working-tree file. **Severity splits rather than restores** — the root-`CLAUDE.md` channel *works on a dev box for the next session* and is *absent in CI and in every fresh clone*, so the `.ignore` recipe is load-bearing exactly where CI is. Three measured corollaries the plan must carry: the load is a **start-of-session snapshot**, so `kronikol init-agents` writes a file the session that ran it will **never** see and must say so (C88); the loader **strips the `kronikol:begin`/`kronikol:end` markers**, so an agent cannot tell Kronikol's block from the repo's own prose (C87); and **`AGENTS.md` is not loaded by this host at all** (C89) | no |
| B5 | `ingest` drops the pointer's first line — the file list *and* the data-file size, the write-time size signal — and emits no `::notice` even on GitHub Actions | no |

### 4.3 The pointer is unsafe (C)
| | What | Breaking |
|---|---|---|
| C1 | A newline in a feature or scenario name emits a raw stdout line at column 0. Verified against `actions/runner`'s own `ActionCommand.TryParseV2`: it trims **whitespace only**, so an injected line *is* consumed as a workflow command. The most damaging reachable payload is `::stop-commands::<token>`, which suppresses all workflow-command processing for the rest of the job. **C1 and B1 contradict each other and neither named its channel** (§14.3 row 10): a line that never reaches the job log never reaches the command parser. C1 was reproduced on `kronikol ingest` (its own process) and on a directly executed test host — exactly the two rows the M1.0 table marks *yes*. **Both rows now carry a channel column**: library pointer under VSTest → swallowed, so C1 is unreachable there; library pointer under MTP or a direct host → reaches the log; `kronikol ingest`/`merge`/`query` as a CI step → reaches the log, and this is where C1 is real. State the payload severity against the channels that survive | yes |
| C2 | Five printed-command sites interpolate the reports directory bare. A path with a space produces a command that either exits 2 **or — reproduced — exits 0 and answers about a different report**: a paste that should have reported 6 failures printed `nothing failed / 11 scenarios, all passed` | yes |
| C3 | `Summarise` checks only that a file exists, so after an output failure the pointer names, sizes and routes the agent to the **previous** run's files. Two modes; in the `IOException` mode `WriteFile` silently writes `<name>2.<ext>` and returns normally, so nothing anywhere marks the run | no |
| C4 | The Tool sets `Console.OutputEncoding` nowhere, so on Windows every byte is the host code page even when redirected. `→` → `0x1A` (valid UTF-8, silently wrong), `·` → `0xFA` (invalid). **This corrupts data, not chrome**: `query failures` mangles xunit's `↓`/`↑` string-diff position markers | yes |

### 4.4 The instruction files clobber (D)
| | What | Breaking |
|---|---|---|
| D1 | Report generation overwrites `CLAUDE.md`/`AGENTS.md` unconditionally, destroying `init-agents`' own marker block; re-running `init-agents` then grafts onto the generated body | yes |
| D2 | The static-template guard asserts parameter **types** over `GetMethods()`; green for an added string overload and for any non-public overload at all | no |
| D3 | The emitted `Reports/CLAUDE.md` is outside every M2.8 drift guard — 6 of 18 verbs asserted, zero flags | no |

### 4.5 The data contract (E)
| | What | Breaking |
|---|---|---|
| E1 | Schema uses OpenAPI `nullable` in a 2020-12 document; 16 of 16 pairs fail; five of the 48 nodes need more than the mechanical fix | yes |
| E2 | Four version signals and **none versions the primary file**. `Failures.jsonl`'s is inert; `mergeableFormatVersion`'s single reader discards the value with `out _`; `query` hard-errors on an unknown one while `merge` **accepts it and re-stamps the output as 1**, laundering it past the gate | yes |
| E3 | `statusCode` is an enum **name** when .NET names the code and a **number** when it does not, so `--status 5xx` finds a 599 and misses a 500; `IsError` is a four-name allow-list that counts Kronikol's own `Ack`/`Responded`/`Sent` as failures — **313 errors on an all-green production run, 208 of them phantom** | yes |
| E4 | Interaction `timestamp` formats with a literal `Z` and no UTC conversion, so the sender's offset is preserved and mislabelled. Scope narrowed: only the NDJSON ingest lane and a library caller can carry a non-zero offset | yes |
| E5 | `merge` rewrites `environment` to the **merging machine**, and **fabricates** one for shards that carried none. Narrowed: nothing reads `environment` today — `ReportIndex.EnvironmentOs/Runtime` are write-only — so it is an artifact defect, not an output defect, yet | no |
| E6 | XML and YAML emit **no `diagnostics` at all**; a YAML run ships the camelCase JSON schema beside PascalCase data, and the pair fails on all three required root keys | no |
| E7 | `stableId` carries no suite and collides — 145 of 905 cross-suite. Narrowed and important: **zero within-report duplicates across 1,043 corpus scenarios**, so the trigger is a combined report (`merge` across suites, ingest folding two runners) or a repeated `Examples`/`[Theory]` row, not an ordinary run. Reproduced end to end through `ingest`: two reports with identical results print one spurious BROKE and one spurious fixed | yes |
| E8 | Any valid JSON is accepted as a report and answered `0 scenarios, all passed`, exit 0 | yes |

### 4.6 `merge` is the weakest link (F)
| | What | Breaking |
|---|---|---|
| F1 | The self-ingest guard runs **after** the HTML is written, so a refused merge leaves a rewritten HTML beside a stale JSON and exits 0. Narrowed: the rewritten HTML is byte-identical to a correct merge **except** the component diagram's call/test label, which doubles | yes |
| F2 | A scenario id in two inputs is deduped while its interactions **concatenate** and its `stepPath`s do not: 2N interactions under N paths, doubled component counts, and **first-file-wins by path order** — so a passing shard sorting before a failing one merges as Passed | yes |
| F3 | `MergeableReportReader` defaults a missing or unparseable `result` to **Passed** with no `ResultDefaulted` diagnostic. Narrowed hard: a *truncated* shard fails the merge with exit 1, and Kronikol's own writer can never produce this input — the reachable triggers are third-party files, post-hoc mutation, and **version skew** | yes |
| F4 | The Tracking section aggregates whole-run per-service totals across **unmatched** scenario sets, so one added test hides a total capture loss and one removed test invents one. The wiki's own Gold Standard guarantee — "reject a service falling to zero" — never fires | no |
| F5 | A diff where only **one** side has stableIds prints "matched by position" and then reports every scenario as both new and gone, exit 0. Narrowed: Kronikol4J *does* emit `stableId`, so the trigger is pre-3.0.47 .NET artifacts and hand-edited files | no |

### 4.7 The query surface does not round-trip (G)
| | What |
|---|---|
| G1 | The `sN/<stepPath>` address the tool **prints** is accepted-and-ignored by five verbs, degrades `compare` to a whole-scenario comparison, and is rejected by six others. `TryScenario` never inspects `Kind`, so `steps s0/99` is byte-identical to `steps s0` |
| G2 | `query failures` hand-rolls its pager off a literal 25: a footer-guided walk of a 150-failure report printed **142 of 150**, with 8 addresses appearing zero times |
| G3 | The `next:` footer breaks on multi-word values (`--service Reporting Database (SQL Server)` → `Not an address: Database`), drops the **positional scenario scope** (24 rows → 1,346), and never terminates |
| G4 | `grep` cannot see `errorMessage`, `errorStackTrace` **or the scenario name**, and reports the miss as a confident negative with exit 0 and a remediation footer that repeats the caller's own targets |
| G5 | `--out` is a silent no-op on **all 14** listing verbs, and on the payload verbs in their bodiless/index forms — while the emitted `CLAUDE.md`, `SKILL.md` and `query --help` all advertise it |
| G6 | `--out` into a missing directory throws an unhandled exception with a stack trace and a CLR crash code outside the 0–255 range, on all four payload verbs and `export` |
| G7 | `! report predates step attribution` prints above a report written minutes ago, on any current report with no steps and no tracked calls — and asserts "source locations are absent" on reports that carry them. Two of 15 real fixtures banner |
| G8 | `query assertions --failed` says "no assertions failed" on a 15-failure run and blames an already-on option; the trigger is per-failure, not per-project |
| G9 | `diff`'s second operand rejects a directory with a raw .NET permission error while the emitted `CLAUDE.md` says every command takes a directory; `diff --baseline` is unaffected |
| G10 | `diff` prints **no** provenance banner, so a run whose verdicts were defaulted to Passed reports as `Fixed (3)` |

### 4.8 LLM-first: discovery and preference (H)
| | What |
|---|---|
| H1 | **Orphaned discovery equity.** The capability query ranks `TestTrackingDiagrams` #1; the name "Kronikol" is unbound and post-dates most training cutoffs; the old package is undeprecated with no pointer; `README.md` never names the old identity. This is the cheapest discovery fix in the plan and it is a redirect, not a campaign |
| H2 | **The community plugin marketplace is an open lane** — 2,282 plugins, no .NET test-report reader, and the skill is publishable as-is with no MCP server. Discoverability must come from name and description, **not** from `category`/`tags`, which are not manifest fields |
| H3 | **The MCP registry's search is substring-over-name**, so name and description must literally contain `dotnet`, `test`, `report`, `xunit`, `failures`. The shelf is occupied and will not stay open |
| H4 | `Kronikol.Tool` carries no agent/AI/LLM/CLI tag and ranks 23rd of 36 for `test report`; a same-category competitor carries the Verified badge a free reserved-prefix application would grant. **Narrowed:** five projects already override `PackageTags`, so the scoping pattern exists |
| H5 | **`dnx Kronikol.Tool` runs the published tool with no install on .NET 10** and no agent-facing asset mentions it — 13 install-line sites. Narrowed on four measured counts: it needs a live feed on **every** invocation with no cache fallback, it swallows `--help`/`--version`, it serves the last *published* version (3.0.86, which lacks `diff --baseline`), and it costs 3–5× per command |
| H6 | **The differentiator is the interaction capture, and it is invisible at rung zero** (A2). Results are commodity; calls are not. Nothing else in the field can represent them — CTRF's schema cannot |
| H7 | **CTRF as a pointer carrier, not a second opinion on pass/fail.** RUN-verified: a producer-set `extra` survives `github-test-reporter` v1.0.29's read → enrich → group → render path **unmodified**, and `suite-folded`/`file-report` add siblings rather than replacing it. **But the rendered bytes needed a template the project does not ship** — the run used `INPUT_CUSTOM-REPORT=true` with a hand-written `kronikol.hbs`, and the same verifier's census found `extra` in 11 stock templates with **every occurrence reporter-written**. So the carrier works and **nothing a consumer already has renders it**: the deliverable is a template plus consumer wiring plus docs, not a free channel. That changes M8's cost line and weakens the sequencing argument against MCP. Also corrected: `Microsoft.Testing.Extensions.CtrfReport` is **alpha-only**, so "the platform already writes CTRF" is premature |

### 4.9 Extensions worth building (I)
| | What |
|---|---|
| I1 | **`--json` shipped mid-audit, and the half that mattered is still open.** What shipped is good and supersedes most of §7.1: `formatVersion` (the house idiom, better than the `schemaVersion` this plan proposed), `command`, `notes`, and a deliberate `count` **xor** `items`+`total` so a script cannot iterate an empty array beside a number. **Two rules from §7.1 did not ship, both RUN-verified against the 16:46 build.** (a) **The error path emits no envelope**: `query summary ./nope.json --json` prints bare prose and exits 2, so a wrapper gets valid JSON on success and unparseable text on failure — the single thing that makes a machine channel usable. (b) **`truncated` is a bare bool**, so a consumer learns that rows were dropped and never how many, which is G2's silent-skip made machine-readable. And E8 is **strengthened**, not fixed: the schema file beside the report now yields a *well-formed envelope* claiming `scenarios: 0`, `kronikolVersion: null`, exit 0 — a confident wrong answer a wrapper will believe where a human reading text might have paused |
| I2 | **A capability document.** The verb set is duplicated by hand in **four** places plus a byte-identical dogfood copy, and `SkillDriftTests` recovers it by regexing prose — a regex that has already silently dropped a verb. `kronikol query --describe` off one table removes the whole 3.0.82 substring class from the drift guard |
| I3 | **`query why <address>`.** Narrowed usefully: the command *count* claim was refuted — one command (205 B) answers the body-value class today. The real defect is an **addressing inconsistency**: `interactions`/`flow` collapse a request/response pair onto the request's ordinal while `values` prints the response's, so `http s5/i8 --body` prints the request and never hints the response exists |
| I4 | **`query repro`.** Narrowed: *not* impossible today — the FQN is in `errorStackTrace` in the exact `Namespace.Class.Method` shape `--filter` wants, extractable for **26 of 26** failing scenarios. First increment is a tool-side stack-frame parser that retro-fits every report ever written; a `scenario.testId` field is justified only for passing/skipped scenarios and cross-run uses |
| I5 | **An HTML run-facts island.** An agent holding only the HTML from a CI artifact reads 147 KB before the first scenario |
| I6 | **MCP judgment survives 2026** — a local stdio wrapper still adds little where a shell exists — but the reason has changed: the registry lane is open *now* and the SaaS vendors are moving into the adjacent channel |

### 4.10 What nobody looked at (the completeness critic's sweep)
It enumerated every surface in the working tree's own diff — 60 modified and 22 new product and test
files — and diffed that against all 144 findings. Twelve surfaces had no finding at all. What it found
when it went and looked:

| | What |
|---|---|
| **GAP-2** | **`query.py` — the fallback shipped in the skill, in `.claude/skills/` and in all twelve templates — crashes with an unhandled `UnicodeEncodeError` on Windows on `summary` and `failures`, the two rung-zero verbs.** An earlier finding about it was REFUTED for the wrong reason: nobody had run it. This is the fallback an agent without the .NET tool is told to use |
| **GAP-3** | **§3.1's forgery class already reaches the shipped machine channel**: report-supplied text lands verbatim in `--json`'s `notes[]`, the tool's own `!` marker prefixed and embedded newlines intact. The text-path rule in §3.1 must be written against the envelope too |
| **GAP-4** | **Every CI job in every workflow is `ubuntu-latest` — no Windows or macOS job anywhere — while all 107 of the audit's RUN results are Windows.** So C4/C39's whole encoding class may not exist on the platform that actually runs CI, and gate 6 must settle it |
| **GAP-5** | **M2.6 is half-shipped and the plan has no row about whether it works**: ReqNRoll populates `sourceFile`/`sourceLine` on 8 of 8; seven other .NET adapters emit null on 69 of 69; `scenario.attempt` is null on **89 of 89** scenarios across all 16 reports on disk |
| **GAP-6** | **There is no `failureCause` field anywhere in the schema or the data**, so A1's proposal to stop prepending it *deletes the run's only record of the failure category* with no replacement proposed. A1 must add the field in the same slice |
| **GAP-8** | **The template pack path — the only channel that installs the skill into a consumer project — has zero findings**, and its pack-time `Content Include` on the untracked `templates/agents/` fails **silently** where C10's `EmbeddedResource` fails loudly. The new CI guard for it has never executed |
| **GAP-9** | `Specifications.html`/`.yml` are **0 bytes on exactly the runs that have failures** (0-byte on both failing fixtures, non-zero on all 12 without), and the unstarted M3.3 is built on the same generator |
| **GAP-10** | **36 of 144 findings never reached the verifier and at least nine never reached the plan in any form** — not narrowed, not rejected, absent. §14.2 now carries them |
| **GAP-11** | `Kronikol.MSTest` and `Kronikol.Playwright` have **no example project and no generated report anywhere**, and 8 of 13 adapter fixtures predate M1. §1.3's corpus item named none of this |
| **GAP-12** | **The repo already contains the Playwright test §14.3 proposed to write** (`tests/Kronikol.Tests.EndToEnd/StableIdDeepLinkTests.cs`), for exactly the deep-link cases the audit measured in jsdom. Run it rather than writing four Chromium cases |

And one thing that was silent **because it is sound**: the `--json` pager. Walked 15/15 with set
equality, `total` computed from what was rendered — so **G2's silent skip is a text-path defect only**,
and the fix is to route the text pager through the same code the envelope already uses.

---

## 5. Milestones — ordered by when a change stops being cheap

Sizes are **estimates**, labelled as such, calibrated against measured source and test volume in this
repo (`FailuresDigestGenerator.cs` 505 lines / 20 tests; `RunSummaryConsoleWriter.cs` 222 lines /
9 tests; `src/Kronikol.Tool/Query*` 6,690 lines / 144 + 28 tests; `ReportGenerator.cs` 5,343 lines).
Named by what a consumer gets. **Every slice is one release** — version bump in all packages,
changelog, wiki page, ledger line, tag — per `CLAUDE.md`. There is no trailing docs milestone.

### 5.0 Two clocks, and the re-pin inventory

The predecessor table ordered by module. The right order comes from two different clocks, and mixing
them is what produced the old M0/M3 split.

- **Clock A — adoption.** A shape an outsider parses (a JSON key, a key's type, an id's value, an
  envelope's `next`) gets more expensive the moment anyone reads it. It runs out **at the 3.1.0 tag**.
- **Clock B — parity.** A change to generated bytes costs one Kronikol4J re-pin cycle, and the cycle
  costs the same whether it carries one change or twenty. It never runs out; it only rewards batching.

**Measured re-pin inventory** (`ls` over `kronikol4j-report/src/test/resources/parity`, 2026-09-12):
59 pinned files — **45 HTML**, `report-data.json` / `.xml` / `.yaml`, `testrunreport-schema.json` /
`.xsd`, three `ci-summary-*.md`, five flow/escape text pins. **Zero of the 45 HTML goldens carry
`data-stable-id` today**, and `report-data.json` has no `ciMetadata`, no `environment`, no `attempt`
and `"statusCode": "OK"` — so 3.1.0 already owes that whole cycle.

On the .NET side there is **no stored byte golden for the report at all**:
`ToggleDefaultsBaselineTests` compares three freshly generated documents to each other (`Assert.Equal(specA, specB)`),
so it is invariant to content. What "re-pinning" costs in .NET is assertion churn —
`RunIdentityDataTests`' exact `CiKeys` array, `QueryJsonTests` (28), `FailuresDigestGeneratorTests`
(20), `QueryCommandTests` (144), `RunEndPointerTests` (9), `SkillDriftTests` (10).

**Budget exactly two Kronikol4J parity cycles for this whole plan** — one after M1, one after M4.
Any ordering that creates a third is wrong, and the old M0/M3 split created one.

### 5.1 The table

| | Milestone | Size (est.) | Ships alone? | Breaking | Forces a re-pin of |
|---|---|---|---|---|---|
| **M0** | **The release compiles from a clean checkout, and the tool stops calling successes errors** — ~~stage `templates/agents/`~~ **DONE 18:1x by `e91cffb` (C98)**, which also tracked `.claude/skills/`, so the CS1566-on-fresh-checkout item is closed and M0 shrinks to the predicate work; template pins 3.0.85 → 3.0.86; rewrite the K4J 3.1.0 ledger paragraph (§9); `IsError` learns `Ack`/`Responded`/`Sent` | **trivial** — one `git add`, an 8-line predicate, three call sites | it is the condition of any release, not a release | no | nothing |
| **M1** | **The shapes that can never change again** — the whole of clock A in one release, **before the tag**: `--json`'s `next` → argv + an envelope on non-zero exits + `truncated` carrying a count; `ciMetadata.runAttempt`; `formatVersion` as the first key + the reader that gates on it; `statusCode` → integer with `statusText` beside it (and the tool reading **both** forms); suite-scoped `stableId`; the `Failures.jsonl` header line. Carries only the fixtures its own red tests need | **large** | **yes, and it must** | **yes — all of it, by design** | K4J `report-data.{json,xml,yaml}` + `testrunreport-schema.{json,xsd}` + **all 45 HTML** (suite scoping moves every `data-stable-id`, which 3.1.0 is introducing anyway — one cycle, not two); `RunIdentityDataTests` key array; `QueryJsonTests` (28); the schema contract walker; every `CiPreview` fixture |
| **M2** | **`Failures.md` tells the truth** — A1 at all four key sites, A2 + a `callsScope` discriminator, A3 fencing, A4 surrogate-safe truncation + the two-action `RunOutputs` split, A5 caps, A6 all-skipped, A8 link gating, A9's digest half (`file:line` + FQN), Q1's suppression cap | **medium–large** | **yes — and for this repo's own daily use it is the first release that pays** | output shape only (no consumer contract survives M1) | `FailuresDigestGeneratorTests` (20), `FailureClustererTests`, the HTML cluster panel; the port must adopt the key or the ledger records a divergence — its own `report-failureclusters.html` probably does **not** move (its fixture messages carry no `FailureCause` prefix; unverified) |
| **M3** | **You find out from the log that the run failed, on every runner** — B1 **reframed** (see 5.5), B3 + the `::notice` split, B5, C1–C4, D1–D3, B4's `.ignore` | **medium** | yes | no, once reframed | `RunEndPointerTests` (9); K4J `ci-summary-*.md` ×3 **if** the summary body changes — batch that with M4 |
| **M4** | **The schema tells the truth about the file beside it** — E1 (43 mechanical + 5 special), YAML ships the YAML schema, diagnostics in XML/YAML, `additionalProperties: false`, E8, E2's merge re-stamp, E4 UTC, E5 environment | **medium** | yes | no — a fix plus additions | K4J `testrunreport-schema.{json,xsd}`, and the three data goldens if E4/E5 move bytes. **This is parity cycle two; pull M3's ci-summary change into it** |
| **M5** | **Every address the tool prints, the tool accepts** — G1, G4, G7, G8, G9, G10, stableId as an address, §3.1's data-vs-voice rule. **G2, G5 and G6 are verify-don't-rebuild**: on the 16:46 build `--out` writes and prints `wrote 3.1 KB <path>`, and the pager computes `total` | **medium** | yes | no | `QueryCommandTests` (144, partial), `SKILL.md`, `references/commands.md`, the emitted `Reports/CLAUDE.md` |
| **M6** | **A merged report is a real run** — F1–F5 + B2, via the `FinishRun` extraction and its measured obstacle (process-global ambient state in the post-output tail) | **large** | yes | output shape | merge tests; no port goldens measured |
| **M7** | **One question, one command** — I2 `--describe` off the dispatch table (with `PrintUsage` generated from it and `SkillDriftTests` comparing against the table, not a regex), I3's request/response addressing fix, I4's stack-frame FQN → `repro`, A9's query half | **medium** | yes | no | `SkillDriftTests`, `commands.md` |
| **M8** | **An agent that never heard of Kronikol finds it** — H1 redirect equity, H2 marketplace plugin + `claude plugin validate --strict` in CI, H3 registry-shaped name and description, H4 `PackageTags` + the reserved prefix, H5 the `dnx` line | **small** | yes | no | nothing — the only milestone whose value is outside the repository |

The old **M8 (docs, port, gates, release) is deleted as a milestone.** `CLAUDE.md` already requires the
changelog, the wiki and the tag at the end of every session of work; parking them behind eight
milestones is how §7's four wiki pages came to be outstanding in the first place. §8's list is
distributed across M0–M8 and each slice closes with its own page.

### 5.2 The breaking-change window, ranked — six, not seven

Ranked by **how much more expensive each gets after the 3.1.0 tag**, which is not severity and is not
build order. Two rows from the previous list are evicted and one new row is first.

1. **`--json`'s `next`, its error path, and its `truncated` count.** RUN, 16:46 build:
   `query interactions <report> --service "Dessert Provider" --limit 3 --json` returns
   `"next": "--service Dessert Provider --json --offset 3"`, and following it the way the shipped test
   follows it (`QueryJsonTests.cs:165` does `.Split(' ')`) prints `Not an address: Provider` with no
   envelope at all. The machine channel's resume pointer is broken for every multi-word filter value,
   and `formatVersion: 1` is being published around it **today**. Today: one method plus 28 tests in
   one file. After the tag: `formatVersion: 2`, a dual renderer, and every wrapper. **Deadline: hours,
   not days.**
2. **The `suite`-scoped `stableId`.** It keys deep links, `diff`, baselines and the coming history
   ledger. After the tag it invalidates every published `#sid-` URL and every stored baseline, and it
   costs a **second** 45-golden port cycle; before the tag it rides the `data-stable-id` cycle 3.1.0
   already owes. Free now, twice-paid later.
3. **`ciMetadata.runAttempt`.** Uniquely **unrecoverable**: a re-run's attempt number cannot be
   reconstructed after the fact, so every report 3.1.0 ever writes is permanently silent about flakes.
   Ranked third and not first only because nothing reads it yet — this is insurance, not use.
   Measured absent on the fresh fixture (`ciMetadata` has exactly 7 keys). Name it `runAttempt`, never
   `attempt`: `scenario.attempt` already exists and means something else.
4. **`formatVersion` as the first key of `TestRunReport.json`.** Confirmed absent from the fresh
   fixture's root keys. Add it before v1 files exist without it, or every reader ours and the port's
   carries a "version 1 means absent" branch forever.
5. **`statusCode` → integer, `statusText` beside it.** The port's golden still reads
   `"statusCode": "OK"`. Rides M1's one data re-pin; after the tag it is a second re-pin plus every
   consumer's parser. **Note the hidden cost the old table missed: the tool must read both forms from
   the day it ships, so this is library *and* Tool work, not a writer change.**
6. **The `Failures.jsonl` header line.** Real, and last: zero external consumers today, so the window
   is about when someone starts rather than about the tag.

**Evicted from the list.** *Schema `nullable` → `type: [X, "null"]`* is not window-bound — the schema is
wrong now and fixing it costs exactly the same next year; it belongs in M4 with the rest of the schema
truth, and it rides whichever data re-pin is going anyway. *Splitting the digest into two `RunOutputs`
actions* is an internal refactor with no consumer-visible shape; it belongs in M2 on its merits.

**Also not on this list, because it cannot ship in 3.x at all: see 5.5.**

### 5.3 Four honest stop points

1. **After M0** the release stops being able to break CI on a clean checkout, and `query services`
   stops inventing 208 errors. Two edits. This is not a stop point anyone plans for; it is the floor.
2. **After M1** the contract is one a third party could build on and the window has closed correctly.
   Say plainly what this buys: **nothing today.** It is insurance with a deadline — the most urgent
   slice in the plan and the least valuable one on the day it ships.
3. **After M2 + M3 the work pays for itself.** This is the real stop point. The digest stops printing
   that fourteen unrelated failures "need the same fix", and the run says where to look in a channel
   that survives. The justification is this repo's own history: 3.0.83–3.0.86 were all user-reported
   bugs found in one production report, so the population that benefits on day one is the author.
4. **After M5** an outsider's agent never meets an address the tool printed and refuses. That is the
   first point at which recommending Kronikol to someone else is defensible — and it is the
   precondition for M8, not the reverse.

### 5.4 The cheapest possible start — and why it is not `RunAttempt`

The previous answer was *`RunAttempt` plus regenerating `CiPreview.AllFailing`*. Both fail their own
test of "useful even if nothing else is built".

- **Regenerating `AllFailing` produces nothing durable.** `git check-ignore -v` puts that whole path
  under `.gitignore:29 [Bb]in/`. It is a local precondition for writing tests, not a deliverable, and
  nothing outside this plan's own gates consumes it. Worse, §6's proposed guard — "every `CiPreview`
  fixture is newer than `FailuresDigestGenerator.cs`" — asserts an mtime relation over build output
  that does not exist on a fresh clone. **Commit the fixtures instead**: `Kronikol.Tests.csproj:48`
  already copies `TestData\**\*`, and the only thing in it today is four Cucumber files.
- **`RunAttempt` is window-bound but not useful-now.** Nothing reads it; its sibling `environment` is
  measured write-only. Keep it in M1 for the deadline, not at the front for the value.

**The actual cheapest start, in order, none of it needing this plan green-lit** — all three executed
2026-09-12, see §16; items 2 and 3 were both wider than stated here:

1. **`git add templates/agents/`** — measured `?? templates/agents/` against a tracked, *modified*
   `Kronikol.Tool.csproj` whose line 35 embeds `..\..\templates\agents\CLAUDE.md` by literal path. A
   release commit that stages the csproj without the directory is CS1566: CI fails before any test
   runs. One minute, and it is the only item here that can break the build of everyone else's work.
2. **`IsError`** — eight lines, three call sites, no re-pin, no schema. On the plan's own reference
   fixture the 16:46 build prints `Event broker  8 calls  8 errors  …  Respondedx8`: every one of
   those "errors" is Kronikol's own default success label. Independent of the `statusCode` type change,
   because non-HTTP taps keep a string status either way.
3. **The envelope's `next`** — see 5.2 row 1. Not cheap in value terms; cheap in edits, and the only
   item whose deadline is measured in hours.

`Failures.md`'s cluster key is the fourth candidate and deserves naming: one method
(`FailuresDigestGenerator.ClusterKey:286`, still first-line on the 12:59 source), no schema, no port
data change, and it turns one worked example out of fifteen into nine. If M2 slips, ship that alone.

### 5.5 Rejected

Carried, all four still stand after re-checking, one strengthened:

- **Renaming `Failures.jsonl` to `.ndjson`.** `IngestCommand.cs:15` is
  `InteractionPatterns = ["*.ndjson", "*.jsonl"]`, so the rename moves the file between two swept
  patterns. Exclude our own output **names**, mirroring `MergeCommand`'s guard.
- **ASCII-ising the pointer's separators.** Still dead, and now reproduced twice in this pass: the same
  encoder carries report *data*, so ASCII chrome leaves xunit's `↓`/`↑` markers corrupted.
- **`category` / `tags` in `plugin.json`.** Not manifest fields.
- **`CosmosDB` in `ParameterCaptureHint`.** Measured harmful.

New, and each one saves real money:

- **The declarative field table (§7.3).** Reject as a build item. The bug it is sold on — the
  40-release loss of `failureMessage` / `sourceFile` / `sourceLine` — was in a **reader**
  (`MergeableReportReader.ReadSteps`), and a table driving the writers and the schema would not have
  caught it. Replace it with one **round-trip** contract test: write a report with every field
  populated, read it back through every reader, assert equality. Roughly 2% of the cost, and it covers
  the measured failure; the eight-place bill is one batch per release and is already paid.
- **Flipping `WriteCiSummary` or `PublishCiArtifacts` to true.** The repo's own rule:
  *"MAJOR — … changing a default so that existing code behaves differently without being touched.
  Reserved for the planned v4; never bump it without asking first."* B1 as written **cannot ship in
  3.x.** Reframe it the way M1 of the predecessor plan already did successfully: a **new** option,
  default on — `WriteCiDebugSection`, writing the short debug block **to stdout, and additionally to
  `$GITHUB_STEP_SUMMARY`**, when CI is detected and something failed — which is a minor bump by the same
  rule and has three precedents
  in this very tree (`WriteRunSummaryToConsole`, `GenerateFailuresDigest`, `WriteAgentInstructions`,
  all added default-true in the uncommitted diff). Leave both existing defaults alone and make the
  **absence** loud, per Q8. **The "and additionally" is load-bearing and is measured (C93): the step
  summary is not the job log.** Content written only to `$GITHUB_STEP_SUMMARY` is absent from
  `gh run view --log`, which is the command an agent reaches for — so a summary-only debug block is
  invisible to the consumer this plan is written for. The shape that reaches both is `tee` (C94):
  emit to stdout, append to the summary file.
- **`query why` as a verb.** The finding was narrowed to an addressing inconsistency — one 205-byte
  command already answers the body-value class. Fix the request/response ordinal collapse in M7 and do
  not add a verb whose reason for existing has been refuted.
- **I5, the HTML run-facts island, as a standalone slice.** Any HTML change costs a 45-file port cycle.
  Its population is "an agent holding the HTML and nothing else", which M3's artifact work addresses
  directly. If it ever ships it must ride an HTML batch, never its own release.
- **The fixture-freshness mtime assertion.** See 5.4: it asserts a relation over gitignored build
  output. It is the audit's own error class — a predicate that is green for reasons unrelated to the
  thing it names.
- **The MCP server, within this plan's horizon.** I6's judgement survived verification. `--describe`
  (M7) is the artefact an MCP server would be generated from, and M8's marketplace entry reaches the
  same agents for a fraction of the maintenance. Revisit only with measured demand, and never couple it
  to M8 — H2 measured that lane as open **now**.

*This section is the sequencing critic's, adopted wholesale over the author's first draft. It replaced a
module-ordered table with an adoption/parity-clock ordering, evicted two rows from the breaking window
and added a new first, measured the Kronikol4J re-pin inventory at 59 files, and established that the
old M0 "cheapest start" failed its own test. §5.5 gained four new rejections, one of which
(flipping a default cannot ship in 3.x) is a rule the author missed.*

## 6. Test matrix

| Slice | Red first | Guards that must exist afterwards |
|---|---|---|
| M0 | `RunIdentityDataTests` 8th key; a compile-guard that every `EmbeddedResource` literal path is tracked | fixture-freshness assertion: every `CiPreview` fixture is newer than `FailuresDigestGenerator.cs` |
| M1 | `Cluster_key_ignores_a_bare_failure_cause_prefix` (built from the **xUnit v3 shape**, `"Assertion\r\n" + message`, not from unique first lines); `Mixed_fixture_produces_more_than_one_cluster`; `Digest_lists_the_real_error_for_every_unworked_failure`; `Digest_lists_calls_for_a_failing_scenario_with_no_steps`; `Digest_lists_calls_when_the_failing_step_made_none`; a **hostile-value fixture** (backticks, pipes, fences, leading `#`, image syntax, CRLF) asserted through a real CommonMark render; `Truncate_never_splits_a_surrogate_pair`; budget facts at 20 / 200 / 2000 failures for **both** files | re-point the existing `Distinct(n)` helper at the real message shape, so the suite stops certifying a case the product never produces |
| M2 | `Pointer_collapses_CR_LF_in_every_run_derived_string`; `Pointer_quotes_a_path_containing_a_space`; `Pointer_names_only_outputs_that_reached_disk`; `Tool_stdout_round_trips_through_strict_UTF8` (must run the process with stdout redirected — an in-process string assertion cannot see this) | the stderr column added to the M1.0 channel table, pass or fail |
| M3 | `A_generated_report_validates_against_its_own_schema` with a conformant 2020-12 validator, zero errors; `Yaml_report_validates_against_the_schema_generated_for_Yaml`; `Query_refuses_an_unknown_formatVersion`; `Merge_refuses_an_unknown_mergeableFormatVersion`; `Ack_and_Responded_are_not_errors`; `Timestamp_with_an_offset_serialises_as_UTC` in all three writers | `additionalProperties: false` everywhere, so the key-walker becomes redundant rather than load-bearing |
| M4 | A **round-trip property test**: take every address the tool prints on a fixture and assert each is accepted by the verb it names; `Paging_failures_to_completion_shows_every_failure` (set equality, not a count); `Grep_finds_a_scenario_error_message`; `Out_on_a_listing_verb_writes_the_file_and_prints_a_receipt`; `Dispatch_never_leaks_a_stack_trace` | strengthen `SkillDriftTests` from "the flag parses" to "the flag changes an observable" |
| M5 | `Merge_refuses_before_writing_the_html_when_the_output_is_an_input`; `Merging_the_same_shard_twice_does_not_double_its_interactions`; `Merging_preserves_the_runs_environment_not_the_mergers`; `Tracking_reports_a_loss_in_matched_scenarios_even_when_a_new_scenario_adds_the_same_calls`; all four M1 files exist beside a merged report | |
| M6 | `claude plugin validate --strict` in CI | a nightly scripted grep of the community marketplace JSON for the plugin name |
| M7 | Envelope facts per verb **and per exit code**; `--describe` ↔ dispatch-table parity | |

Playwright per `CLAUDE.md` for anything touching the report: `PollingInterval = 200`, `.First`/`.Nth`,
`FillSearchBar()`, no `Force = true`, no network mocking. **And per §3.5: a browser assertion must run
in a browser** — jsdom reversed a finding in this very pass.

---

## 7. Specifications for the three things that need one

### 7.1 The `--json` envelope — **superseded in part; this is now a delta, not a design**
M2.9 shipped during the audit (staleness note, top of file). What follows is kept only where the
shipped envelope differs, because the rest is built and is better named than what this plan proposed.
**Adopt the shipped `formatVersion` / `command` / `notes` / `count`-xor-`items` shape.** The three
deltas below are the ones that decide whether a wrapper can rely on it, and the first is not optional:

1. **An envelope on every exit code.** Today a non-zero exit prints prose and no JSON (RUN-verified).
   Populate `error: {code, message, hint}` and keep `items` empty rather than leaving stdout unparseable.
2. **`truncated` as an object**, `{limitBytes, droppedItems}`, not a bool — otherwise G2's silent skip
   is merely re-encoded.
3. **`next` as an argument vector**, not a string, so G3's quoting break cannot be expressed.

The original design, for the record and for the parts M7 still has to specify for verbs that do not yet
emit one:

```
{ "schemaVersion": 1,
  "tool":    { "name": "kronikol", "version": "3.1.0" },
  "verb":    "failures",
  "report":  { "path": "…", "kronikolVersion": "…", "formatVersion": 1,
               "startTime": "…", "endTime": "…", "mergeable": false, "enriched": true,
               "ciMetadata": {…}, "environment": {…} },
  "notices": [ { "code": "resultDefaulted", "message": "…" } ],
  "items":   [ … typed per verb … ],
  "page":    { "offset": 0, "returned": 15, "total": 15 },
  "truncated": { "limitBytes": 6000, "droppedItems": 9 } | null,
  "next":    { "argv": ["query","failures","<report>","--offset","15"] } | null,
  "error":   null }
```

Rules that are the whole point, each answering a measured defect: `notices` carries what the `!`
banners carry today, **structured**, so a forged banner cannot impersonate the tool (§3.1); `error` is
populated on non-zero exits instead of stdout being empty; `next.argv` is an **argument vector**, not a
string, so quoting cannot break it (G3); `page.total` makes G2's silent skip impossible to express;
`--count` becomes `page.total` rather than a bare integer with banners above it; and it covers all 18
verbs, because the payload verbs' `--out` does **not** cover the case where a wrapper wants the body
*content*.

### 7.2 `kronikol query --describe`
JSON on stdout in the §7.1 envelope. `toolVersion`, `envelopeSchemaVersion`, the address grammar with
examples, the exit codes, and per verb `{name, summary, positional[], flags[{name, arg, repeatable}],
itemFields[], pagesWithOffset}`. **Generated from the same table the CLI dispatches on**, with
`PrintUsage` generated from it too — the way M2.8 made `Program.cs` one edit. `SkillDriftTests` then
compares `commands.md` against the table instead of against a regex over prose, and an MCP server
generates its tool list from it. `kronikol --version` is one line off the existing attribute and
should ride along.

### 7.3 ~~The declarative field table~~ — **rejected, and replaced by a round-trip test**
The first draft proposed one table driving the JSON/XML/YAML writers, the schema, the XSD and the
contract tests, sold on the 40-release loss of `failureMessage`/`sourceFile`/`sourceLine`. **The
sequencing critic killed it, and the argument is exact: that bug was in a *reader*
(`MergeableReportReader.ReadSteps`), and a table driving the writers and the schema would not have
caught it.** The eight-place bill is one batch per release and is already paid.

**What to build instead, at roughly 2% of the cost and covering the measured failure:** one round-trip
contract test — write a report with every field populated, read it back through **every** reader
(standard, mergeable, the query scanner, the port's), assert equality. That is the test whose absence
cost 40 releases, and it is a single fixture plus a walker.

---

## 8. Documentation

`LLM_FRIENDLY_PLAN` §7 is **not** yet due — its own Remaining block schedules it after M3.3 — so the
four untouched pages it names are outstanding, not missed. This plan inherits them and adds:

- **The three default-on options are the only three of 86 public options absent from all 98 wiki
  pages.** `Generated-Reports.md`'s "Always Generated?" column names the governing option for every
  other conditional output; it breaks its own pattern at exactly the cell a reader would check — and
  the working-tree edit put a **factual error** there: `Failures.md`/`.jsonl` are listed as written
  "When the run had failures", while a green run writes them, which is the property that gives the
  file meaning. The same wrong condition appears again in `CI-Artifact-Upload.md:74`.
- A **docs guard**: a test or CI grep asserting every public option on `ReportConfigurationOptions`
  appears in `Report-Configuration.md`. This is the second consecutive plan to ship an undocumented
  option.
- The **AI & Agents sidebar grouping**, with a real `### Installing the skill` anchor — the one line
  added so far points at the heading directly above it.
- `API-Reference.md` does not know `RunEnvironment` exists.
- **Publish the 3,200:1 compression measurement** (§2.1). It is the only fact here a search engine will
  rank, and it is the honest core of H6.

---

## 9. Kronikol4J and the release

**The port's 3.1.0 ledger paragraph is false and must be rewritten before the tag.** It says *"The
report JSON's bytes are unchanged; only the schema file differs."* Verified against the current build,
what actually moves:

- Five new fields in **all three** writers (`ciMetadata` + `environment` between `endTime` and
  `features`; `feature.sourceFile`; `scenario.sourceFile`/`sourceLine`/`attempt`), so
  `report-data.json`, `.xml` **and** `.yaml` goldens move — the port's goldens go
  `KronikolVersion/StartTime/EndTime` straight to `Features`.
- **Both** schema goldens, not one: `.xsd` 9,471 bytes vs the golden's 8,063 (new `CiMetadataType`,
  `RunEnvironmentType`, `Attempt`), and `.schema.json` 27,427 vs 14,989. The change is **purely
  additive — 133 added keys, zero removals** — which is the one piece of good news and should be stated
  so the port can adopt it without a compatibility story.
- **42 of the port's 45 pinned HTML goldens**, because `data-stable-id` is new on every scenario
  `<details>` and every summary row. The ledger frames the only client-side change as script-only.
- The mergeable format gained `Interactions`, `StepPaths`, `Annotations` and `Diagnostics`; the port's
  `ReportFragment` has none of the four.

Release mechanics: `Directory.Build.props` is still 3.0.86 (correct, per the predecessor plan);
template pins still read **3.0.85** and should track 3.0.86; `README.md` never names the four files
3.1.0 adds. And the compile-level hazard: `Kronikol.Tool.csproj` has a new **tracked** line
`<EmbeddedResource Include="..\..\templates\agents\CLAUDE.md" …/>` while `templates/agents/` is
**untracked** — a literal-path `EmbeddedResource` pointing at a missing file is CS1566, a hard compile
error, so a release commit that stages modified files without that directory breaks CI before any test
runs.

---

## 10. Verification protocol

Gate 1 (channel table) is done and gains a **stderr column** (§2.6). Gate 2 is **run and recorded**
(§2.1) and is re-run per slice. The rest:

3. **Fresh-agent dry run.** `dotnet new kronikol-xunit3`, one failing assertion and one failing SQL
   call, a new session with only "the tests are failing". Pass = both causes reached **without opening
   `TestRunReport.json` or the HTML**. Repeat once with global tool installs denied, to settle H5.
   **This cannot be run by a subagent of the session that wrote the plan** — a contaminated agent
   already knows the answer.
4. **Budget**, on the regenerated `AllFailing`, for `Failures.md` **and** `Failures.jsonl`.
5. **Cross-runtime**: `kronikol query summary` over a Kronikol4J-produced report. **Already passes**
   (§0.2); keep it as a regression gate, and note the port's golden is a 3.0.47 capture with no
   `ciMetadata`, so `ReportIndex.OnCi` classifies every Java report as "written by an older Kronikol" —
   a distinction `CROSS_RUN_HISTORY_PLAN` §1.5 banks on.
6. **New: one real GitHub Actions run.** Everything in §4.2 and C1 is about a CI log, and no lane could
   reach one. Until this runs, B1–B3 and C1 are `RUN` about the mechanism and `READ` about the outcome.
7. **New: a schema-conformance gate**, in CI, not just in a test.

---

## 11. Non-goals

Carried from `LLM_FRIENDLY_PLAN` §10 except where measurement moved them:

- **Making the HTML agent-navigable** — still a non-goal as a whole, but I5's run-facts island is a
  cheap middle and is in M7.
- **Per-failure files at failure time** — **stands, and the finding that challenged it was narrowed
  into a different feature.** The crash-durable NDJSON sink already exists and works under a hard kill
  (§0.2); what is absent is a `LiveCaptureFile` option turning it on for in-process instrumentation.
  That is live capture, not per-failure files, and it belongs in M7 as its own item.
- **Injecting digests into framework failure messages** — reopened as a *question*, not a reversal.
  M1.0 removed the premise ("the pointer gets there without framework surgery"), and one appended line
  on the first failure's message is the only channel guaranteed to appear in every runner's default
  output. Scope it to one line, paths and counts only, opt-in — or reject it explicitly in §12.
- Flipping `LogParameters` or `Redaction` defaults; capturing application logs; a persisted query index;
  `llms.txt` (still no consumer; the ruling is right).

---

## 12. Open questions (recommendation first)

**Q1. Does the digest keep clustering at all?** *Recommend yes, with the suppression capped rather than
the key merely improved.* A clusterer is a heuristic and will be wrong again; the damage comes from
suppression plus the sentence "need the same fix". Never let one cluster absorb more than a fraction of
the run, and never let a suppressed failure lose its own error text.

**Q2. What does `calls` fall back to?** *Recommend the scenario's own last N interactions, errors first,
under a heading that says what it is, plus a `callsScope: "failingStep" | "scenario" | "none"`
discriminator in the jsonl.* A consumer must be able to tell "we did not look there" from "there was
nothing there". Cheap now — two consumers, both ours; expensive once MCP and CTRF read it as truth.

**Q3. `statusCode` integer now, or at v4?** *Recommend now.* It rides the schema-golden re-pin the
release already owes, and `IsError`'s four-name allow-list is wrong on Kronikol's own defaults today.
Keep greppability by having `Failures.md` and `query` **render** `500 InternalServerError` while the
file stores `500` — the display property belongs in the display layer.

**Q4. Does the pointer move to stderr?** *Open, and measure before deciding* (§2.6). If stderr survives
VSTest, the pointer is a diagnostic and belongs there on principle. Either way the `::notice` stays on
stdout and stays best-effort.

**Q5. Is `WriteCiSummary` the right gate for "Debug this run"?** *Recommend no — split them, **and do
not send the debug section to the step summary alone**.* Writing a short debug section when CI is
detected and something failed is a different cost/benefit from generating the full 48 KB
diagram-bearing `CiSummary.md`; merging the two behind one flag is what produced §0.1's third defect.
**Corrected by measurement (C93):** this question, and Q8, both routed the run-end signal to
`$GITHUB_STEP_SUMMARY`, and that channel **does not reach `gh run view --log`** — verified on two real
public runs, one of them written by an action in code rather than echoed by a shell, so no
command-echo confound. The destination is **stdout**, with the summary as the human-facing addition.

**Q6. Do the generated instruction files get the marker treatment, or stop being written where a file
already exists?** *Recommend markers, with one implementation shared with `init-agents`.* Two writers
with two policies is what corrupts. Also consider refusing outright when the reports directory contains
a `.git` or a `.csproj` — that is a configuration mistake worth naming.

**Q7. Does `suite` scoping land in M3 or wait?** *Recommend M3.* `CROSS_RUN_HISTORY_PLAN` inherits the
key; fixing it after that ledger exists is a schema migration. But note the measured scope: **zero
within-report collisions in 1,043 scenarios**, so this is a correctness fix for combined reports, not a
live bug in an ordinary run — do not oversell it.

**Q8. Does `PublishCiArtifacts` flip to on?** *Open, and it is a product stance.* On means every
consumer's CI starts uploading artifacts they did not ask for; off means §0.1's third defect stands in
the default configuration. A middle option: leave it off and make the **absence** loud — the CI summary
section says "no artifact was uploaded; set `PublishCiArtifacts`".

**Q9. Does `merge` get the full run treatment, or an explicit "merge is a renderer" contract?**
*Recommend the full treatment*, but note the measured obstacle: the post-output tail of
`CreateStandardReportsWithDiagramsCore` reads process-global ambient state (`RequestResponseLogger`
statics, `CurrentReportsDirectory`, the diagrams fetcher) that the merge path does not have, so "extract
`FinishRun` and call it from both" is **not** a clean extraction and must be costed as a refactor.

**Q10. Does the plugin-marketplace submission happen before or after M7's MCP server?** *Recommend
before, and independently.* H2 measured that the field is mostly local OSS skills with no backend; the
skill ships today. Do not couple a cheap, available channel to the most expensive milestone.

---

## 13. Standing hazards for anyone working this plan

Recorded because every one of them produced a wrong finding in this pass.

1. **Check the binary's mtime before and after a batch.** The tree rebuilds under you (§1.2).
2. **Check the fixture's mtime and its field set before concluding a feature is broken.** Two of three
   `CiPreview` fixtures predate M1 entirely.
3. **jsdom is not a browser.** nwsapi is stricter than Blink and Gecko and reversed a finding.
4. **A gitignore-aware search cannot see a build tree.** It produced a false "this cannot be run".
5. **One search engine is not the web.** It produced a false "this cannot be found".
6. **A census over one file is not a claim about all files.** It produced a false "always zero".
7. **A blind search API is not an empty shelf.** It produced a backwards market conclusion.
8. **Path case matters to the nested-`CLAUDE.md` traversal** — it walks directories under the session
   cwd, case-sensitively, and this session's cwd has a lowercase drive letter. Three probes broke on
   three different confounds and were read as three confirmations.
9. **An append-only log shared between runs cannot tell you what the current run did.** A resumed
   workflow writes into the same `journal.jsonl`; a record's *existence* says nothing about which run
   wrote it. Check the timestamp and the agent transcripts' mtimes, or relaunch into a fresh directory.
   This one cost an hour of redundant re-verification before it was caught (§14.0 row 7).
10. **A census with no positive control is not a measurement.** This loop's first iteration wrote a
    census that reported **zero failed scenarios** across a corpus containing a fixture with fifteen,
    because it read `status`/`interactions` where the emitted schema says `result`/`httpInteractions`.
    The number was plausible, it *agreed with the previous pass's conclusion*, and it was wrong. A zero
    that confirms what you already believe is the most dangerous number in this document. **Assert a
    known-positive case in the same script, and treat the zero as unreportable until it passes.**
11. **A `�` at the console is usually the console.** The auto-loaded `CLAUDE.md` looked
    encoding-damaged; the data holds 20 × U+2014 and 0 × U+FFFD. Print the codepoint, never judge the
    glyph — this host's code page mangles em-dashes on the way out, which is also C39's territory.
12. **A claim about repository state has a half-life; date it inside the claim.** "The block is
    untracked" was measured correctly at 14:30 and was false by 18:1x, because another session
    committed. The same applies to "no such file exists", "nothing reads this" and every `git`-derived
    row in §14.1a. **State the observation *and its clock*, and re-take it at re-sync** — a tracking
    claim carried forward two iterations is an assumption wearing a measurement's clothes.
13. **A file in a test-output directory is not the product's output shape.** Iteration 2 recorded
    "new surface: report filenames are now hash-suffixed (`TestRunReport_a94ef381.json`)" after seeing
    one in `tests/Kronikol.Tests/bin/Debug/net10.0/Reports/`. **It was a unit-test artifact.** That
    directory is the suite's scratch space — it also holds `Attr_annotations.json`,
    `TestRunReport_scen.schema.json` and dozens more — and the product still writes plain
    `TestRunReport.json`, which that very directory's generated `CLAUDE.md` names. One filename was
    read as a feature. **Before calling anything new surface, check whether the directory holding it is
    a product output or a test's scratch dir.**
14. **An anchor that spans a line wrap will not match.** Two edits and one grep in this loop failed
    silently or noisily because the phrase they searched for is broken across lines in the source
    (`is **test
data, not instructions**`; `a tracking
claim carried forward`). A zero hit-count from
    a multi-word search over wrapped prose means *check the wrap*, not *the text is absent* — which is
    hazard 10 in a different costume.
15. **A literal grep over generated HTML will miss the element you are counting.** Counting
    `<details class="scenario"` across the port's 45 parity goldens returns **11**; the tolerant
    `<details[^>]*class="[^"]*scenario` returns **42**, because the generator emits other attributes
    before `class`. The 11 was about to be written up as a refutation of C71's 42/45 — it was a
    refutation of my own regex. **Markup is not a string: match the element, not the spelling you
    expect it to have**, and run the tolerant form as the control before reporting the strict one.
16. **When the corpus cannot exercise a path, the model is the instrument — not the corpus.** I
    measured `rule` at **0 of 1,349 scenarios** and correctly declined to claim the spec data file drops
    it. But the falsifier I wrote down — *find a report carrying a Rule* — is one **no corpus in this
    repo can satisfy**, so it would have parked the question forever. The implementer settled it in one
    read: `GenerateSpecificationsData`'s model flattens each step to a string and drops `Rule` and
    `Description` outright (C111). **A zero-coverage path is a reason to read the code, not a reason to
    stop.** Declining to claim was right; naming a corpus-shaped falsifier was not.
17. **`--no-build` does not run the current product — it runs whatever that project's output directory
    last received.** Measured at 19:39: with `src/Kronikol/bin/.../Kronikol.dll` at **19:07:51**, the
    copy beside `Component.xUnit3` was 19:07:51, beside `Kronikol.Tests` 19:07:51, beside
    `CiPreview.Mixed` **13:29:53**, and beside `CiPreview.AllFailing` **10:18:27**. A `--no-build` run
    of the last two measures a build from hours earlier while looking exactly like a current run.
    **This is why C85's rule says the DLL *beside the fixture*, and iteration 6 still stamped a row
    with the canonical build's time instead** (C103, corrected). Stamp the adjacent DLL, and if it is
    stale either build that one project or say which vintage the row measures.

---

## 14. Assumption ledger

Every load-bearing claim in this plan, with how it is known. A plan that cannot say which of its
statements are measured is a plan that will be wrong somewhere and not know where.

This ledger is also **the work-list**. Rows carry stable ids (`C1`…) so an iterating session can
reference them across passes without ambiguity, a **Decides** column so prioritisation is mechanical —
a row that decides nothing may stay at `READ` forever, and should say so rather than consume an
iteration — and an explicit **falsifier**, because §14.0's rule is worth nothing if it lives only in
prose.

### 14.0 The error class, and this plan's own base rate

The predecessor named it:

> **A claim about *existence or shape* was verified, and allowed to transfer to a claim about
> *behaviour* that was not.**

The mechanical rule: for any claim that decides what gets built, do not ask *how do I know this is
true* — there is always an answer. Ask **what would I have seen if it were false, and did I look
there?**

**This pass ran that rule as a process rather than as advice, and here is what it cost.** 144 findings
were produced by eighteen lanes; 108 were handed to an adversarial verifier told to break them, with
instructions to state the falsifier first and go where the claim would break rather than re-read the
citation. Result:

| | count | share |
|---|---|---|
| CONFIRMED as written | 61 | 56.5% |
| **NARROWED** — true in a smaller or different scope | **41** | **38.0%** |
| **REFUTED** — false as stated | **6** | **5.6%** |
| Reached `RUN` depth | 107 | 99.1% |

**Forty-seven of 108 findings did not survive verification as written.** The verifiers were not
smarter than the surveyors; they were pointed at the falsifier instead of at the evidence. That is the
entire difference, and it is the argument for keeping this ledger permanently rather than treating
verification as a phase.

The six reversals, and what each teaches:

| Verified | Then claimed, unverified | Actually |
|---|---|---|
| Claude's WebSearch returns zero Kronikol URLs | Kronikol has no reachable web presence and no agent can verify it | DuckDuckGo returns the repo at **rank 1**; `gh search`, `dotnet package search`, the GitHub API and `WebFetch` all find it |
| `query.py`'s error test parses a numeric status | the branch is structurally dead, so it reports 0 errors on every report | numeric statuses **do** occur (ingest, and undefined `HttpStatusCode` values); it **under**counts, 5 where the tool says 8 |
| The MCP registry's `search=test-report` returns zero | the category is empty, which is a discovery argument | `search` is **substring-over-name**; the shelf is occupied by `playwright-report-mcp`, `verdict-mcp`, `cypress-cloud`, seven `dotnet` servers |
| A backslash in the fragment throws a selector `SyntaxError` in jsdom | it aborts the whole `DOMContentLoaded` handler in the report | neither Blink nor Gecko rejects it — CSS Syntax closes the block at EOF. **The real trigger is one line away**: `decodeURIComponent` raises `URIError` on `#q=100%` |
| Three probes of the nested-`CLAUDE.md` load all failed | the mechanism M1.3 is built on does not fire | it **does** fire, on the real gitignored file, main loop and subagent alike. Three probes, three different confounds — cwd case, out-of-tree path, reading the memory file itself |
| `.claude/skills/` is untracked | `SkillDriftTests.The_repo_dogfoods_the_skill_it_ships` therefore reddens CI | **no such test exists.** The real staging hazard is a tracked `EmbeddedResource` pointing at an untracked directory — a **compile** error, not a test failure |
| The workflow journal holds `started` records labelled `Critique` | the resumed run had therefore launched the three critics | **it had not.** Those records belonged to the *previous* run, whose critics died on a session limit. The resume was re-running the entire 108-agent Verify phase live; the critics had not started and would not for another hour |

Rows 1, 3 and 6 are the pure form of the class, committed by the *audit* rather than caught by it. Row
4 gives §3.5's `RUN(proxy)`. Row 5 gives §13's corollary: **three failed probes with three different
causes are not three confirmations.**

**Row 7 was committed by this plan's own author, roughly forty minutes after writing this section, and
was caught by the user rather than by the process.** A shared append-only journal makes a prior run's
records indistinguishable from the current run's at a glance; the falsifier — *are these records
timestamped after the resume, and do the agent transcripts have fresh mtimes?* — takes one command and
was not run, because the records existed and said the right word. It is the same shape as row 1 (one
index's silence read as the web's) and row 6 (a directory's tracking state read as a test's verdict).
The lesson is not "be careful": it is that **the falsifier has to be executed even when the evidence is
sitting in front of you agreeing**, which is the only claim §14.0 has ever made. Re-verification after
relaunch was done into a **fresh** transcript directory, where a `started` record cannot be ambiguous.

**Three corollaries carried forward from the predecessor, all re-earned here:**
1. **A schema can never settle a direction-of-data-flow question.** Re-earned by H7: the CTRF `extra`
   claim was only settled by *executing* `github-test-reporter`'s render path.
2. **Verifying a predicate is not verifying its effect.** Re-earned by A7: `Failures.jsonl`'s
   `formatVersion` is written, asserted by a test and documented — and read by nothing.
3. **Reading *part* of a call path is reading none of it.** Re-earned by G3: a finding cited
   `RerunPrefix()` as dropping five flags, having stopped reading six lines short of the additions that
   carry them.

**A fourth, new here, and it is the one this pass earned the hard way:**
4. **A `RUN` is scoped to the environment it ran in, and a stale binary is not the product.** Two
   reversals and three discarded findings came from jsdom-instead-of-browser and from an in-flight
   build. `RUN` without provenance is a claim about something that may no longer exist (§1.2, §3.5).

**And then a third pass broke three more, after the plan was written.** The error-class critic, pointed
at this document rather than at the codebase, reversed three of its own author's conclusions:

| Verified | Then claimed, unverified | Actually |
|---|---|---|
| The root `CLAUDE.md` carries a `kronikol:begin` block | it is "tracked, greppable and auto-loaded", so B4 drops to medium | **not tracked** — `git show HEAD:` finds nothing; it is an uncommitted edit, absent from CI and every fresh clone. "Auto-loaded" was never executed, and two available observations contradict it |
| The nested-`CLAUDE.md` injection fired on the real gitignored file | the mechanism M1.3 rests on "fires" | it fires **only when the path's drive-letter case matches the session cwd** (2/2 vs 0/2) — and Kronikol's own pointer prints the other case |
| A harness produced a populated `calls` array | "the mechanism is alive", so A2 is a predicate defect | the harness was **reflection over hand-built objects**. Census of 37 real reports: scenarios with a failed step carrying an attributed interaction, **0 of 1,284**. A predicate-only fix may ship a feature that still emits nothing |

**And a fourth pass reversed one of the critic's own reversals.** The loop's first iteration executed
the experiment §14.3 item 7 named, and the result goes the other way:

| Verified | Then claimed, unverified | Actually |
|---|---|---|
| A session attached 4.5 min *before* the edit carried pre-block bytes, and the block is untracked | therefore "auto-loaded" is unsupported, and B4 keeps its original severity on **both** channels | **it is auto-loaded, measured** (C86). A session started 2 h 51 min *after* the edit carries the block **in full** in its `attachment/instructions` record. The unified diff of the loaded copy against the on-disk file is **exactly the two marker lines and nothing else** (C87). *Tracked* stays false and still matters for CI and fresh clones; *auto-loaded* is now `RUN`. The two observations that "contradicted" it were both **pre-edit sessions** — which is snapshot semantics (C88), not a failure to load |

**And a fifth, four hours later, on the same row.** B4's tracking predicate flipped **twice in one
day** as the tree moved underneath it:

| Verified | Then claimed, unverified | Actually |
|---|---|---|
| At 14:30 the `kronikol:begin` block was an uncommitted working-tree edit | therefore CI and every fresh clone have **no pointer at all**, and the `.ignore` recipe is load-bearing | true when measured and **false by 18:1x** — commit `e91cffb` tracked it (C98), along with `templates/agents/` and `.claude/skills/`, which also closes C10's fresh-checkout **compile** hazard. Combined with C86, B4's root-`CLAUDE.md` channel is now **alive on both axes**: tracked *and* auto-loaded |

**The lesson is not that the first measurement was wrong — it was right when taken.** It is that a
claim about *repository state* on a live tree has a half-life, and this ledger had no way to say so.
Rows C98 and C81 both need a timestamp in the claim itself, not only in the provenance stamp. **Hazard
12.**

**A sixth reversal, and this one was caught by the implementer, not by this ledger.** The sibling
plan's blast-radius audit (`scratchpad/audit/M3.json`) recorded the MCP SDK as **unrestorable** — 711
packages in the local cache, no match, no fallback folder — and §5.5's M3.1 reasoning sat beside it:

| Verified | Then claimed, unverified | Actually |
|---|---|---|
| A restore of the MCP SDK failed, and 711 cached packages contained no match | therefore building M3.1 is **infeasible here**, which reads as a reason not to build it | **`ModelContextProtocol` 2.2.0 restores in under two seconds.** The failure was an **offline artifact** of the session that measured it, not a property of the package. Feasibility was never the blocker and must not be recorded as one. M3.1's three real reasons stand on their own — a local stdio wrapper adds little to an agent that already has a shell (this plan's §5.5), `Kronikol.Tool` has zero `PackageReference`s today, and M2.9's `--json` envelope already returns what such a wrapper would |

**It is the same shape as §14.0 row 1** — one environment's failure read as the world's — and it is the
third time in this document that an *absence* measured in a constrained environment was promoted to an
impossibility. That pattern now has three instances (WebSearch, the MCP registry search, this), which
makes it the most repeated single error in the whole audit.

**The base rate, maintained.** Of behaviour claims in this plan reasoned about but never executed, the
running score is **0 correct out of 5** — §14.0 row 7, the three above, and now the error-class
critic's own inference that two pre-block observations "contradict" auto-loading. They do not; they are
exactly what a start-of-session snapshot predicts, and the claim was reasoned rather than executed by
the very section that exists to stop that. Each was a claim that
*looked* verified because a real citation sat beside it. The verified ledger itself executed 107 of 108.
The two numbers exist so they can be watched: **the moment the second starts accumulating unexecuted
rows, the plan is drifting back into the failure mode, and the first says what that costs.** The
predecessor's score after a far longer run was also zero.
**The fifth reversal above is deliberately *not* counted in that five.** It was not a reasoning
error — the measurement was correct when taken and the tree moved. Counting it would hide the real
lesson, which is hazard 12, not carelessness. Reversals and errors are different tallies: **seven
reversals, six errors** — the seventh reversal and sixth error both being the MCP "unrestorable"
finding, which *was* a genuine unexecuted transfer from *one failed restore* to *infeasible*, and which
this ledger did not catch: the implementer did.

**The pattern across all ten reversals is now specific enough to act on: every one came from a check
that succeeded.** Nobody was ever stopped by an absent citation. What failed was the step *after* the
citation — the transfer from "this exists / this fired / this returned bytes" to "therefore the
consumer sees it". Which is why §14.3 exists as a standing list rather than a phase.

### 14.1a Existence and shape — cheap to check, never yet wrong
| id | Claim | How verified |
|---|---|---|
| C1 | `Directory.Build.props` is 3.0.86; latest tag is v3.0.86; no v3.1* tag | read + `git tag` |
| C2 | ~~`CiMetadata` is a 7-field positional record with no `RunAttempt`; zero `GITHUB_RUN_ATTEMPT` hits in `src/`~~ — **TRUE WHEN TAKEN, FALSE BY 19:4x** | read + grep; **re-taken iteration 7**: `CiMetadata.cs:14` now declares `string? RunAttempt = null` as an 8th field, `:54` reads `getEnvVar("GITHUB_RUN_ATTEMPT")`, and `:75` documents Azure DevOps having no equivalent. Hazard 12 |
| C3 | `FailuresDigestGenerator` carries its own `ClusterKey:286` plus a third first-line site at `:386`; `FailureClusterer` has one consumer, `ReportGenerator.cs:1228` | read |
| C4 | `Kronikol.xUnit3/TestContextEnumerableExtensions.cs:43` prepends `FailureCause`; the other eight adapters pass the framework message through | read, all nine |
| C5 | The schema declares draft 2020-12 and uses `nullable` at 48 nodes, 0 uses of `type: [X,"null"]` | read |
| C6 | Zero `Console.Error` uses in the product outside the Tool; the pointer has one sink at `ReportGenerator.cs:394` | grep |
| C7 | `WriteCiSummary` and `PublishCiArtifacts` have no initialiser and default false | read |
| C8 | Three of 86 public options appear in none of the 98 wiki pages, and they are exactly the three M1 added | grep, both directions |
| C9 | The verb set is duplicated by hand in four places plus a byte-identical dogfood copy | read |
| C10 | ~~`Kronikol.Tool.csproj` has a tracked `EmbeddedResource` literal path into untracked `templates/agents/`~~ — **true at 14:30, CLOSED by 18:1x** | `git status` + read; re-taken iteration 2 (C98): `git ls-files templates/agents/` → 1 file, committed in `e91cffb`. The fresh-checkout **compile** error is gone |
| C11 | The community marketplace holds 2,282 entries, zero Kronikol; `category`/`tags` are not `plugin.json` fields | fetched catalog + two manifests |
| C12 | The MCP registry's OpenAPI spec defines `search` as substring-over-name | fetched spec |
| C13 | Five projects already override `PackageTags`; three deviations are visible on the published feed | read + feed |
| C14 | Kronikol4J emits `stableId`, ported from the same SHA-256 scheme | read `ReportDataSerializer.java:88` |
| C15 | 15 Kronikol4J-produced `TestRunReport.json` exist under gitignored `build/` | filesystem census, ignore-blind |

### 14.1b Behaviour — the kind that has been wrong 47 times out of 108

`RUN` = code executed and its output observed. `RUN(proxy)` = executed in a substitute for the real
environment (§3.5). `READ` = source inspected and reasoned about — **not evidence that a value reaches
anything.** `DOC` = a vendor's documentation. `COUNTED` = a census, approximate, a lower bound.
`UNVERIFIED` = asserted.

**Decides** names what in this plan dies if the row is false. A row whose Decides cell is `—` may stay
below `RUN` indefinitely.

| id | Claim | Depth | Decides | Falsifier, and whether it was checked |
|---|---|---|---|---|
| C20 | The xUnit v3 `FailureCause` prefix is the **sole** cause of the single-cluster collapse; stripping it yields 5 clusters / 9 worked examples from the same 15 messages | **RUN** | M1/A1 | Stripping line 1 would leave the collapse. **A/B run through the real generator; it did not.** |
| C21 | The fixture holds **10 distinct messages / 9 distinct normalised keys**, not 15 distinct causes | **RUN** | A1's red test asserts 4 clusters + 10 entries, not 15 | Counted every message. A test written to "15 separate entries" would assert the wrong number. |
| C22 | Removing the prefix does **not** fix the key: unprefixed xunit messages still collapse 5 distinct failures into one cluster keyed `Assert.Equal() Failure: Values differ` | **RUN** | that A1 must fix the key **and** the adapter, not either alone | If false, an adapter-only fix would suffice. **Ran it; it does not.** |
| C23 | The calls predicate is "the failing step itself carries an attributed interaction", not "the adapter has steps" | **RUN** | Q2's fallback trigger | A fallback on `failing.Count == 0` would fix it. **Ran case D: a scenario with a failing step still yields `[]`.** |
| C24 | 667 of 1,282 real scenarios (52%) are step-less | **RUN** | A2's severity | Counted across both corpora. |
| C25 | Every `kronikol ingest` run yields `stepPath: null` because `OverrideLog` never sets `MarkerKind`; step bars leak into `annotations` as raw PlantUML | **RUN** | A2 is two bugs, not one | If the markers were classified, `stepPath` would be set. **Emitted `"kind":"Custom"` observed directly** — positive proof, not inference from silence. |
| C26 | All 16 emitted report/schema pairs fail a conformant 2020-12 validator; 917 of 917 errors are `type`-on-`null` | **RUN** | E1, and that E1's fix is complete | Any non-`null` error would mean something else hides behind the count. **None.** |
| C27 | Five of the 48 `nullable` nodes need more than `type: [X,"null"]` — notably `step.status`, where the `enum` still rejects `null` | **RUN** | E1's cost | The mechanical fix would suffice. **Applied it to `status`; still rejects.** |
| C28 | `IsError` counts `Ack` and `Responded` as failures: **313 errors on an all-green production run, 208 phantom**; `flow --errors-only` on a `[Passed]` scenario shows 3 calls, all successes | **RUN** | E3, and Q3 | A numeric-only classifier would not fire. **Ran all four call sites.** |
| C29 | Kronikol's own in-product classifier (`ComponentFlowSegmentBuilder`) disagrees with the query tool about the same bytes, and is right | **RUN** | that E3 is a CLI fix, not a capture fix | Read + ran; the HTML and ComponentDiagram exclude them correctly. |
| C30 | `metaType` is already captured, parsed **and rendered** (`--group-by kind`), so E3's fix needs no format or capture work | **RUN** | E3's cost, and that it is **not** breaking to the data file | If absent, E3 would be an eight-place change. **`--group-by kind` prints `Event 258 208`.** |
| C31 | A footer-guided walk of a 150-failure report prints **142 of 150**; 8 addresses appear zero times across six pages | **RUN** | G2 | Set equality over the union of pages. **8 missing.** |
| C32 | The `next:` footer drops the **positional scenario scope**: `interactions s10 --limit 3` → `--offset 3` → 1,346 rows where 24 were scoped | **RUN** | G3's real mechanism | The surveyor's stated mechanism (dropped filter flags) is **already fixed** in the tree; this is a different cause. |
| C33 | `grep` cannot see `errorMessage`, `errorStackTrace` or the **scenario name**, and answers a confident negative with exit 0 | **RUN** | G4 | `--in errors` exists. **It is rejected, exit 2.** |
| C34 | `--out` is a silent no-op on all 14 listing verbs, and on `http`/`note` in their bodiless and index forms | **RUN** | G5's scope | The surveyor ran 3 verbs; the verifier ran all 14 plus the payload edge forms. |
| C35 | Any valid JSON — including the schema file beside the report — answers `0 scenarios, all passed`, exit 0, empty stderr | **RUN** | E8 | A shape check exists. **None: only parse failure and `mergeableFormatVersion` gate.** |
| C36 | `query diff` prints **no** provenance banner, so a defaulted-to-Passed run reports `Fixed (3)` | **RUN** | G10 | Reproduced through an **organic** `kronikol ingest` crash-worker capture, not a hand-edited file. |
| C37 | A newline in a scenario name reaches stdout at column 0, and `actions/runner` trims **whitespace only**, so it parses as a workflow command; `::stop-commands` is reachable | **RUN** + **READ (runner source)** | C1 | The runner might match `::` anywhere, making the newline irrelevant. **Read `TryParseV2`: it does not — the newline is load-bearing, which makes CR/LF collapsing a sufficient fix.** |
| C38 | An unquoted reports path with a space produces a paste that **exits 0 and answers about a different report** | **RUN** | C2's severity | Only exit 2 was expected. **Reproduced the false-green: 6 failures reported as "all passed".** |
| C39 | The Tool writes host-code-page bytes even when redirected; `query failures` mangles xunit's `↓`/`↑` diff markers into `0x19`/`0x18` | **RUN** | C4, and that ASCII-ising chrome is **not** the fix | If only decoration were affected, ASCII separators would suffice. **Report data is affected.** |
| C40 | Report generation destroys `init-agents`' merged block **and its markers**, and a subsequent `init-agents` grafts onto the generated body | **RUN** | D1, Q6 | Reproduced the full sequence in a scratch directory. |
| C41 | `It_is_static_text_with_no_room_for_run_data` stays green for an added string overload **and** for a non-public overload with a non-string parameter | **RUN** | D2's rewrite must use explicit `BindingFlags` | Executed the exact predicate against four shapes; only a **public** non-string parameter reddens it. |
| C42 | `Truncate` splitting a surrogate throws `EncoderFallbackException`; under 8,192 UTF-16 units the file is **zero bytes**, at or above it **no file is written and the previous run's survives** | **RUN** | A4's fix must cover both regimes | The "zero-byte" claim is the whole story. **It is the small-digest case only; 25 ordinary failures measure 19,616 chars.** |
| C43 | A 2,000,047-char message yields a **4,000,639-byte** one-line `Failures.jsonl`, because `cluster` repeats the whole first line | **RUN** | A5's fix must dedupe `cluster`, not just cap fields | Call summaries were claimed uncapped. **They are capped at 120 at build time.** |
| C44 | `merge` dedupes a repeated scenario while **concatenating** its interactions and taking `StepPaths` first-wins; verdict is first-by-**path-order**, so a passing shard sorting first hides a failure | **RUN(proxy — synthetic shards)** | F2 | Only the JSON was measured first. **Checked the HTML: byte-identical except the component diagram's doubled label.** |
| C45 | The self-ingest guard runs after the HTML write; the refused case exits **0** with a rewritten HTML beside a stale JSON | **RUN(proxy)** | F1 | The rewritten HTML was claimed double-counted. **It is not — one label differs in a 472 KB file.** |
| C46 | `merge` accepts an unknown `mergeableFormatVersion` and **re-stamps the output as 1**, laundering it past the gate `query` applies | **RUN(proxy)** | E2, F3 | If `merge` gated like `query`, skew would be caught. **A shard declaring version 99 merged clean.** |
| C47 | `merge` writes the **merging machine's** `environment` and fabricates one for shards that carried none | **RUN(proxy)** | E5 | Two shards with sentinel OS strings; neither sentinel survives. |
| C48 | Nothing reads `environment` today: `ReportIndex.EnvironmentOs/Runtime` have no reader in `src/` or `tests/` | **RUN** + grep | E5's severity (artifact defect, not output defect) | A consumer would show the wrong value. **Grep found none; the merged HTML contains no environment string.** |
| C49 | `stableId` collides 145/905 cross-suite and **0 within-report across 1,043 scenarios**; the diff mis-pairing reproduces end to end through `ingest` with no hand-edited JSON | **RUN** | E7, Q7, and its honest scope | If within-report duplicates were common, every diff would be unreliable. **They are not.** |
| C50 | `Failures.jsonl` is **0 bytes on a green run** (pinned by a test) and its `formatVersion` has no reader: deleting the file or corrupting it changes no `query` output | **RUN** | A7 | The same mutation on `mergeableFormatVersion` **did** produce the loud refusal — so the mirror is provably half-built. |
| C51 | `Failures.md`'s cluster heading renders a captured first line as a real `<h3>`; an odd backtick count escapes its span at three sites, yielding live `<img>`/`<a>` | **RUN** | A3, §3.2 | Rendered the hostile file through **two independent CommonMark engines**. |
| C52 | The guard `Everything_quoted_is_marked_as_captured_data` passes unchanged against the hostile file | **RUN** | that A3 needs new tests, not a stricter existing one | Both asserted substrings are present in the poisoned output. |
| C53 | `#sid-` resolves on load, after a filter click, after *Clear All* and on `hashchange`, **and in the exported HTML** | **RUN(proxy — jsdom)** | §0.2; nothing in this plan | A real browser could differ — and did for C57. **Worth one browser confirmation; nothing rests on it.** |
| C54 | A `#sid-` miss is **totally silent** — identical DOM, identical open-`<details>` count, `scrollY` 0, zero console or exception events | **RUN (real Chromium over CDP)** | M4's "say so" behaviour | Any console output would falsify. **Zero `Runtime.consoleAPICalled`.** |
| C55 | `hashchange` reveals the anchor and ignores the filter half, so one URL means two reports; and the report's **own** chain icon fires it with a filter-free fragment | **RUN (real Chromium)** | M4 | Claimed to need a paste. **The report's own `a.scenario-link` triggers it.** |
| C56 | With filters hiding the target, `scrollIntoView` on a `display:none` node is a **no-op** — the common case is a normal-looking report missing exactly the linked scenario | **RUN (real Chromium)** | M4's wording | "Blank report" was claimed. **That is the nothing-matches special case only.** |
| C57 | A backslash in the fragment does **not** throw in Blink or Gecko; the real unguarded throw is `decodeURIComponent` on `#q=100%` | **RUN (real Chromium + Firefox)** | §3.5, §14.0 row 4 | Brute-forced 0x20–0x7E in three positions through the real URL parser into both selector sites: **zero throws.** |
| C58 | The nested-`CLAUDE.md` load **fires**, on the real gitignored `Reports/CLAUDE.md`, for main loop and subagent | **RUN** | M1.3's whole bet | Absence of the `<system-reminder>` injection. **Present. Three earlier negatives each had a different confound.** |
| C59 | A hard-killed process leaves 1,471/1,471 valid JSON lines and `ingest` rebuilds the full output set | **RUN** | §0.2, and the live-capture item in §11 | A torn final line. **None, at 8 KB payloads.** |
| C60 | A producer-set `extra` survives `github-test-reporter` v1.0.29 to rendered bytes in `GITHUB_STEP_SUMMARY`, including a `#sid-` link | **RUN** | H7, M7's CTRF scope | The reporter might strip unknown keys. **Ran its real `dist/index.js`; it does not, and `suite-folded`/`file-report` add siblings rather than replacing.** |
| C61 | `Microsoft.Testing.Extensions.CtrfReport` has **no stable release** — five `1.0.0-alpha.*`, latest 2026-09-02 | **RUN (feed query)** | H7's wiki advice | "The platform already writes CTRF" would make Kronikol's writer redundant. **It is alpha-only; the advice would point users at a prerelease.** |
| C62 | The MCP registry's shelf is occupied: `playwright-report-mcp`, `verdict-mcp`, `cypress-cloud`, `allure-testops-mcp`, **seven** `dotnet` servers | **RUN (registry API, paged)** | H3, I6 | The surveyor's zero-result search. **Reproduced, and the endpoint proven substring-only.** |
| C63 | DuckDuckGo returns the repo at rank 1 for the bare name; `gh search repos`, `dotnet package search`, the GitHub API and `WebFetch` all resolve it | **RUN** | H1's framing (equity, not absence) | One engine's zero was taken for the web's. **Four independent routes succeed.** |
| C64 | A name-free capability query returns `TestTrackingDiagrams` at **rank 1**, 301-redirecting to Kronikol; lifetime downloads ~3.06M across both identities | **RUN** | H1 | "4.1K downloads" was `Kronikol.Tool` alone — off by 20× for the core package. |
| C65 | `dnx Kronikol.Tool` works, needs a live feed on **every** invocation with no cache fallback, swallows `--help`/`--version`, and serves 3.0.86 | **RUN** | H5's honest wording | It would fall back to the global cache. **It does not, even pinned.** |
| C66 | `--describe` does not exist; the verb set is recovered by a regex over prose that has already silently dropped `interactions` | **RUN** | I2 | Re-ran the regex against real help output. |
| C67 | The `!` banners land on stdout **above `--count`'s bare integer**, and every non-zero exit leaves stdout empty with prose on stderr | **RUN, 5/5 paths** | I1, §7.1 | `--count` was only run on a clean fixture. **A ResultDefaulted report breaks `int.Parse(stdout)` today.** |
| C68 | The FQN is extractable from `errorStackTrace` for **26 of 26** failing scenarios in the exact `--filter` shape, and for **0 of 5** passing ones | **RUN** | I4's sequencing | "Impossible today" would make a field mandatory first. **A tool-side parser retro-fits every report ever written.** |
| C69 | Two of 15 real fixtures print the false "predates step attribution" banner, one written by the build under test 16 minutes earlier; it also asserts "source locations are absent" on reports carrying them | **RUN** | G7 | `kronikolVersion` might gate it. **It is scanned and never consulted.** |
| C70 | ~~`GITHUB_RUN_ATTEMPT=2` reaches **zero** emitted bytes~~ — **FIXED IN THE TREE, iteration 7** | **RUN** (original), superseded | ~~M0, §5.1 rank 1~~ — **this item is DONE** | A GitHub-provider block might carry it. **Seven keys, both runs.** **Re-taken 19:4x: `runAttempt` now appears in the emitted `TestRunReport.schema.json` (2 occurrences), so it reaches emitted bytes.** §5.1's rank-1 breaking-window item and `CROSS_RUN_HISTORY_PLAN`'s time-sensitive dependency ("add `RunAttempt` before 3.1.0 is tagged") are both **satisfied** |
| C71 | 3.1.0 moves five data/schema goldens **and 42 of the port's 45 HTML goldens**; the schema change is purely additive, 133 added keys, 0 removals | **RUN (diffed against the port's goldens)** | §9 | The ledger says bytes are unchanged. **They are not, in five files plus the HTML corpus.** |
| C72 | `kronikol query summary` over a real Kronikol4J report passes gate 5 today, on both Debug and Release binaries | **RUN** | §10 gate 5 | "No such file exists." **15 do, under gitignored `build/`.** |
| C73 | The three-path first-60-seconds numbers in §2.1 | **RUN** | §2.1, Q1 ordering | Token counts are bytes÷4, **stated not measured** — the ratios are the load-bearing part. |
| C74 | Under `dotnet test` at default and `normal` verbosity the pointer is swallowed by every VSTest runner | **RUN — re-executed iteration 2 with an A/B control** (`Kronikol.dll` 18:18:25); see C95. The *quantifier* "every VSTest runner" remains the prior session's — one runner, one adapter was re-run | B1, B3, Q4 | Reproduced then; **not re-run in this pass.** |
| C75 | On a default project on GitHub Actions no artifact is uploaded and no CI-log signal appears | **READ** | B1, Q8 | **Not checked on a real runner.** Both options' defaults are read; the *outcome* on a live runner is inferred. **This is §10 gate 6 and it is the highest-value unpromoted row in the ledger.**  **UNPROMOTABLE by this loop** — §14.3 item 1: needs a live GitHub Actions run, i.e. a push. Falsifier named there. |
| C76 | `Console.Error` fares better than stdout under VSTest | **UNVERIFIED** | Q4, B3 | Never measured anywhere. §10 gate 1's stderr column.  **UNMEASURABLE AS SHIPPED** — C97 measured stderr at 0 bytes in both arms; needs a source change before the question can be asked. |
| C77 | Non-Claude agents (Codex, Cursor, Amp) load a nested `AGENTS.md` from a gitignored directory | **UNVERIFIED** | M1.3's reach beyond Claude Code | No such agent was available. The plan cites no source for it either.  **UNPROMOTABLE — instrument absent**: §14.3 item 5, no `codex`/`cursor`/`amp`/`aider` on PATH. |
| C78 | A ripgrep-based agent other than Claude Code honours a `.ignore` file | **DOC** | B4's fix for non-Claude hosts | ripgrep's documented behaviour, proven for Claude Code's Grep and for `rg` 14.1.1 directly; **not executed for Codex or Cursor.**  **UNPROMOTABLE — instrument absent**: §14.3 item 5, same measurement. |
| C79 | A community-marketplace submission would be accepted, and which submission form applies to this account | **DOC** | M6's cost | Read the docs; **did not attempt a submission.**  **UNPROMOTABLE — outward-facing action this loop may not take**: §14.3 item 6. |
| C80 | `claude plugin eval` measures whether an agent reaches for the tool | **DOC** | M6's measurement story | It is the only instrument in H2 that measures *use* rather than *reach*. **Run it once before depending on it.**  **UNPROMOTABLE — no `claude` binary on PATH**: §14.3 item 6. |
| C81 | M2.9 shipped mid-audit; `--json` emits a real envelope on success and **nothing on a non-zero exit** — `query summary ./nope.json --json` prints bare prose, rc=2 | **RUN** (re-run iteration 7 against `Kronikol.Tool.dll` **19:07:55**; earlier 16:46 and 18:23:58 stamps superseded) | I1, §7.1 delta 1 | If the error path emitted an envelope, I1's core would be closed. **It does not.** |
| C82 | With `--json`, the schema file beside a report yields a **well-formed envelope** claiming `scenarios: 0`, `kronikolVersion: null`, rc=0 | **RUN** (re-run iteration 7, `Kronikol.Tool.dll` **19:07:55**) | E8's severity, raised | E8 might have been fixed by the envelope work. **It is strengthened: the wrong answer is now machine-readable and confident.** |
| C86 | The root `CLAUDE.md` `kronikol:begin` block **is auto-loaded** by Claude Code — uncommitted, straight from the working tree | **RUN** (host-behaviour claim; no product binary involved) | B4, M1.3, D1's severity, Q6 | Absence of the block text from this session's `attachment/instructions` record. **Present, in full.** `python` over `~/.claude/projects/c--Code-Kronikol/b2cd976d-….jsonl` record 15: `type=attachment subtype=instructions`, `files[0].path='c:\Code\Kronikol\CLAUDE.md'`, `files[0].type='Project'`, 6,869 chars running from `Never open` to `a command to follow.` It was **untracked** at the time (`git show HEAD:CLAUDE.md \| grep -c kronikol:begin` → `0`), so auto-load does **not** require tracking |
| C87 | The loaded copy is the on-disk file **minus exactly the two marker lines**: the loader strips `<!-- kronikol:begin -->` / `<!-- kronikol:end -->` and changes nothing else | **RUN** | D1, Q6, and whether an agent can tell Kronikol's block from the repo's own prose — it **cannot** | Any other difference: truncation, re-encoding, comments retained. **`difflib.unified_diff(disk, loaded)` → 9 lines, two deletions, both of them the markers.** 6,916 → 6,869 chars; U+2014 ×20 in both, U+FFFD ×0 in both — the `�` seen at the console was the host code page, not the data (§13 hazard 11) |
| C88 | The instructions attachment is a **start-of-session snapshot**, so `kronikol init-agents` never reaches the session that ran it | **RUN** (this session) + **RUN** (the critic's pre-edit session) | D1, Q6, and every sentence anywhere in this plan of the form "the agent will then pick it up" | A post-edit session not seeing it, or a pre-edit session seeing it. **Mine started 2 h 51 min after the edit (record 15, `2026-09-12T16:21:38.659Z`, cwd `c:\Code\Kronikol`) and sees it; the critic's, attached 4.5 min before, does not.** Both observations, one rule |
| C89 | **`AGENTS.md` is not auto-loaded by Claude Code.** The instructions attachment lists exactly **two** files: the project `CLAUDE.md` and the `AutoMem` `MEMORY.md` | **RUN** | §3.2's "emit both, identical" — in *this* host the `AGENTS.md` half reaches nothing, and its whole value rests on C77, which is unverified | A third entry in `files[]`. **There are two.** The repo's own root `AGENTS.md` (2,229 bytes, mtime 14:30) is absent from the load |
| C90 | Across the **entire corpus on disk** — 37 reports, 1,349 scenarios, 4,944 step objects — `step.status` is `Passed` **4,944 of 4,944**, **zero** steps carry a `failureMessage`, and scenarios with a failed step number **0** | **RUN** (fixtures as dated below; positive control passes) | A2, M0.0, and the **shape** of the defect, which this changes | That the zero is a harness artifact. **Positive control: the same script reports CiPreview.Mixed = 15 `result:"Failed"` / 5 `"Passed"`, which is correct.** An earlier version read `status`/`interactions` where the emitted schema says `result`/`httpInteractions` and returned a false `0 failed scenarios` over a corpus containing 26 — caught **only** by the control (§13 hazard 10) |
| C91 | The nested-`CLAUDE.md` injection is **drive-letter-case-conditional**, reproduced on today's tree | **RUN** (`Kronikol.dll` 18:01:45; fixtures 17:04/17:05/16:50) | M1.3's whole bet, and the shape of the one-line fix | The 2×2 coming out mixed. **It did not:** four *different* reports directories, each read once, `C:/…Component.xUnit3` → no injection, `c:/…LightBDD.xUnit3` → injection, `C:/…ReqNRoll.xUnit3` → no injection, `c:/…Kronikol.Tests` → injection. **What it cannot settle:** "matches the cwd" and "is lowercase" predict this identically, because this session's cwd is lowercase. See §14.3 item 8 — unpromotable in-session |
| C92 | With **stock inputs only**, a producer-set `extra` in a CTRF report is rendered by **24 of 24** stock report flags at **zero** occurrences — but "rendered by nothing" is **false**: stock `ai-report` / `ai-summary-report` render producer-set `results.extra.ai` and `results.extra.aiSummary` verbatim, offline and with no API key | **RUN** (`github-test-reporter` @ `7974087`, `package.json` 1.0.29, Node v25.9.0, no token, no network) | H7, M7's CTRF scope, and §8's wiki advice | That the zeros were a broken harness. **Positive control: a hand-written `kronikol.hbs` under `custom-report` rendered 9 of 9 sentinels including the `#sid-` link.** The census corrects to **13** templates touching `extra`, 11 reporter-written and **2 producer-reachable** — the two AI ones, which the earlier census missed |
| C93 | **`$GITHUB_STEP_SUMMARY` is not the job log.** Content written to the step summary is **absent** from `gh run view --log` — the command an agent reaches for | **RUN**, two independent runs | C84, §12 Q5 and Q8, and B1's proposed run-end channel | That the summary text appears in the log. **It does not.** grafana/grafana run 34702879360: the workflow echoes two lines into `$GITHUB_STEP_SUMMARY`; the 726,412-byte log holds only the **unexpanded** `${STORYBOOK_URL}` script echo, and the **composed** line as it exists in the summary appears **zero** times. ctrf-io run 34678802541, where the action writes the summary in code and the YAML never names the variable: all **10** fixture test names appear **0** times in the 527,382-byte log. Source-read confirms `core.summary.write()` appends to the file and emits nothing to stdout |
| C94 | **`tee -a "$GITHUB_STEP_SUMMARY"` *does* put the text in the log** — via stdout, not via the summary | **RUN (source + workflow read)** | that C93 is not falsified by the first counter-example someone finds | A `tee` sighting read as "the summary reaches the log". grafana's `pr-bundle-size.yml` uses exactly this shape. **It is stdout doing the work**, and it is also the thing that makes a run-end pointer reachable at all: write it to **stdout**, and *additionally* to the summary |
| C95 | **The run-end pointer is emitted and then swallowed by VSTest** — not disabled, not missing. Same assembly, same binary, one A/B | **RUN** (`Kronikol.dll` 18:18:25; report regenerated 18:19) | §14.3 item 2, and B1, B3, Q4, §0.1 #3 — all of which rested on a **prior session's** table nobody had re-run | That the option was simply off, which would make the absence meaningless. **It is not.** `dotnet test … --no-build` → 376 B at default, 3,016 B at `-v normal`, 3,035 B at `--logger console;verbosity=detailed`; the pointer appears in **none**. The **same** `Example.Api.Tests.Component.xUnit3.exe` run **directly** → 4,236 B containing `Kronikol: reports written to …  (TestRunReport.html · TestRunReport.json 38 KB · Failures.md)`. Control: that report's `CLAUDE.md`/`AGENTS.md`/`ComponentDiagram.html` were all restamped 18:19, so generation ran in both arms |
| C96 | **Kronikol's own pointer prints an UPPERCASE drive letter** — `C:\Code\Kronikol\…\Reports` | **RUN** (the C95 direct-run output, verbatim) | §14.3 item 8's product half, previously parked BLOCKED(tree) | A lowercase or a relative path. **Neither.** This is the defect closing on itself: C91 measured that `C:/…` does **not** fire the nested-`CLAUDE.md` injection from a lowercase cwd, and here is Kronikol directing the agent to exactly that spelling. **An agent that follows the product's own printed path defeats the mechanism M1.3 is built on** |
| C97 | **Nothing reaches stderr in either arm** — 0 bytes under `dotnet test -v normal`, 0 bytes on the direct run | **RUN** | C76, §14.4 item 1 | That stderr carried the pointer, or anything at all. **It carries nothing**, consistent with C6 (no `Console.Error` in the product outside the Tool). So **C76 is not "unmeasured", it is unmeasurable as shipped** — promote it only behind a source change |
| C98 | **The tracking-state rows flipped at ~18:1x**: commit `e91cffb` (LLM_FRIENDLY_PLAN M0–M2.9) made the root `CLAUDE.md` `kronikol:begin` block **tracked**, and committed `templates/agents/` (1 file) and `.claude/skills/` (3 files) | **RUN** | B4, C10, and the CS1566-on-fresh-checkout hazard | `git show HEAD:CLAUDE.md \| grep -c kronikol:begin` → **`1`**, where iteration 1 measured **`0`** four hours earlier. `git ls-files templates/agents/` → 1; `.claude/skills/` → 3. **C10's compile hazard is closed** — the tracked `EmbeddedResource` no longer points at an untracked directory |
| C99 | The repo's **root `AGENTS.md` is neither tracked nor auto-loaded** | **RUN** | §3.2's "emit both, identical" — in this host that file is dead on both axes | Either predicate holding. **`git ls-files --error-unmatch AGENTS.md` → `did not match any file(s) known to git`**, and it is absent from the instructions attachment (C89) |
| C100 | **M2.10: `kronikol export --tests <file>` puts a `kronikol.test.result` attribute on every span of that test, and without it no span claims a verdict** | **RUN(proxy — the product's own tests, but decoded from emitted bytes)** (`Kronikol.dll` 18:18:25; 18/18 passed) | M7/§8 — this is the first Kronikol output that asserts a *verdict* into someone else's telemetry | That the attribute is only asserted as a predicate. **It is not:** both tests round-trip through `OtlpTraceReader.ReadJson(File.ReadAllBytes(outFile))` — the emitted document is decoded back and the attribute read off the span, which is effect-evidence, not predicate-evidence. The A/B is built in: `Tests_ndjson_gives_every_span_of_a_test_its_verdict` asserts `"Failed"`, `Without_tests_ndjson_no_span_claims_a_verdict` asserts **null**, and a `[Theory]` maps four runner status words through `FeatureSynthesizer.MapStatus`. `dotnet test tests/Kronikol.Tests --no-build --filter "FullyQualifiedName~ExportCommandTests"` → `Passed! - Failed: 0, Passed: 18`. **Honest scope:** the inputs are test-authored, not a capture from a real instrumented run, so this is `RUN(proxy)` on inputs and `RUN` on the emitted bytes |
| C101 | ~~Report filenames are now hash-suffixed (`TestRunReport_a94ef381.json`)~~ — **REFUTED, and it was my own claim** | **RUN** | nothing — but it is the cleanest example of §14.0's error class committed inside the loop that exists to catch it | That the file was a product output. **It is a unit-test artifact.** `tests/Kronikol.Tests/bin/Debug/net10.0/Reports/` is the suite's scratch directory (`Attr_annotations.json`, `TestRunReport_scen.schema.json`, dozens more); the product writes plain `TestRunReport.json`, which that directory's own generated `CLAUDE.md` names. See §13 hazard 13 |
| C102 | The port's parity corpus is **45** HTML goldens, **42** carrying a `<details … class=*scenario*` and **0** carrying `data-stable-id`; the current build emits the attribute | **RUN** (`Kronikol.dll` 18:18:25; port goldens dated June, pre-M2.1) | C71, §9's release note, §14.3 item 16 | **Two controls, and the first one caught me.** A literal `grep '<details class="scenario"'` returns **11 of 45** and would have refuted C71's 42/45; the generated HTML puts other attributes before `class`, so the attribute-order-tolerant regex `<details[^>]*class="[^"]*scenario` returns **42** — C71 is right and my grep was the broken instrument (§13 hazard 15). Second control, on identity: the .NET unit-test outputs are **not** these fixtures — `<title>Test</title>` vs `<title>Kronikol Run</title>`, 460 KB vs 276 KB — so no matched-pair diff is available and none was fabricated |
| C103 | **The framework failure message is a live channel under VSTest** | **RUN** (`dotnet test … --no-build`, rc=1, 10,056 B) — **provenance CORRECTED iteration 7: the `Kronikol.dll` beside `CiPreview.AllFailing` is 10:18:27, not the 18:18:25 originally stamped** (§13 hazard 17) | §14.4 item 3, B3, M3's channel choice — this is the only channel measured to reach a VSTest log | That failure output is swallowed like everything else. **It is not.** `Example.Api.Tests.CiPreview.AllFailing` under `dotnet test --no-build` prints full `Error Message:` bodies *and* stack traces carrying `…\AllFailingTests.cs:line 137` — 33 matches. **The internal control is WEAKENED by the provenance correction:** `grep -c "reports written"` → `0` in that same output, but against a **10:18 Kronikol** whose pointer behaviour is not the shipped one, so that zero cannot carry the swallowing claim. **It does not need to — C95 carries it**, on a correctly-stamped 19:07:51 build with a real A/B. The surviving claim here is the **positive** half, which is a property of the runner and not of Kronikol's version: xUnit's `Error Message:` blocks and stack traces with `file:line` reach `dotnet test` stdout, 33 matches. **Scope: xUnit3 + VSTest only.** And it proves the channel is *readable*, not that Kronikol can *write* to it — the adapter's `FailureCause` prefix (C4) is applied to the captured `ErrorMessage`, which is report-side; no `FailureCause` string appears in this runner output |
| C104 | **The specification documents are deliberately blank when the run has failures** — `generateBlankOnFailedTests: true`, passed to both the HTML spec report and `GenerateSpecificationsData` | **RUN + READ (the named flag)** | **M3.3.** A `Specifications.md` must inherit this or it diverges from its own HTML and YAML siblings *and* from a pinned port golden | That it was a bug. **It is not.** `Specifications.yml` is **0 bytes** on `CiPreview.AllFailing` and `CiPreview.Mixed` while those same reports carry **4 features / 11 scenarios** and 20 scenarios respectively; `CiPreview.AllPassing` is 1,228 B and populated. The control rules out "no data". `ReportGenerator.cs:274,284` names the behaviour `generateBlankOnFailedTests: true`, and it cross-confirms C102: `report-blankonfail.html` is one of the **two** parity goldens with no `<details>` tag at all |
| C105 | **`Specifications.yml` drops scenario-outline examples**: a report carrying two named examples blocks yields a populated spec file naming **neither**, with no `Examples:` key | **RUN** (`Kronikol.dll` 18:18:25) | **M3.3** — a `Specifications.md` off the same feature tree inherits the omission, and example names are exactly what distinguishes parameterized scenarios | That the path was simply unexercised — the failure mode this repo keeps hitting. **Controlled against it:** same run, same directory, `Specifications.yml` non-empty at 3,826 B, and the JSON demonstrably carries `examplesBlockName` values `a classic bake` and `speciality flours`; both are absent from the yml, as is any `Examples:` key. **By contrast `rule` (0 of 1,349 scenarios) and `categories` (0) are genuinely unexercised corpus-wide, so nothing may be claimed about how the spec handles them** — that is the recurring class, not a finding |
| C106 | **There IS a free rendered CTRF channel, and it is exactly one field: `results.environment.buildUrl`** renders as a real clickable markdown link under stock `github-report` and `previous-results-report`, `#sid-` fragment intact, with no `custom-report`/`template-path` | **RUN** (`github-test-reporter` @ `7974087`/1.0.29, offline, 31 run dirs, 65-sentinel fixture) | **M3.2's cost, and therefore whether it gets built at all** | That no standard field renders. **Refuted:** verbatim from `out-fields/github-report/step-summary.md` — `[#9471](https://example.invalid/r.html#sid-ZZFIELD-buildUrl-9471)`. Gate is effectively unconditional: dropping `buildName` and `buildNumber` each still rendered the link, so a producer need only set `buildUrl`. Positive control passed (11 sentinels, 1,591 B). The all-at-once run's sentinel set is **exactly** the union of the single-flag runs — nothing renders only in combination |
| C107 | **But it is one run-level link, not a per-scenario one.** No standard CTRF field renders a clickable per-test URL | **RUN** | M7/M3.2's scope, and §0.2's `#sid-` story | That some per-test field carries a link. **None does.** `test.message` renders unlinked inside a raw-HTML `<td>`; `test.trace` only inside `<pre><code>`; `test-list-report`'s `escapeMarkdown` actively **corrupts** a URL (`https://example\.invalid/r\.html\#sid\-…`). `annotate` carries the URL byte-intact but annotations are plain text. **Bound on the field census, and why it does not weaken the claim:** the canonical CTRF schema was **unobtainable offline** — no `node_modules/ctrf` anywhere on the filesystem, and `gtr/package-lock.json:3907-3910` pins `ctrf@0.2.1` whose tarball is absent from the npm `_cacache` store — so the list of producer-fillable fields comes from the action's own TypeScript, templates and fixture, not from the schema. That means **"renders nowhere" does not prove "not in the schema"** for `rawStatus`, `screenshot`, `tags`, `browser`, `device`, `parameters`, `steps`, `stdout`, `stderr`, `threadId` and `attachments`. **It leaves C107 intact regardless**, because rendering requires the action to reference the field, and a field absent from the action's source cannot be rendered by it. **So a per-scenario deep link still costs the Handlebars template** — C92's conclusion survives for deep links and is refuted only for the single run-level link |
| C108 | **`buildUrl` is not free of meaning**: it conventionally denotes the CI run URL, and the action **back-fills it from the GitHub context** when a producer leaves it empty | **READ (`gtr/src/ctrf/enrichers.ts:23-25`)** | whether M3.2 should actually take the C106 channel | That writing it is additive. **It is an override** — Kronikol would replace the link to the CI run with a link to its own report, and `previous-results-report` labels the cell `[#<buildNumber>]`, which reads as a build number, not a report. A real trade, not a free win |
| C109 | **M3.2 shipped (`CtrfReportGenerator.cs` + `CtrfCommand.cs`, `GenerateCtrfReport`, default off), and it puts every Kronikol-specific value in `extra`** — per-test `kronikolAddress`/`stableId`/`categories`, run-level `runId`/`runAttempt` — while `environment.buildUrl` carries `metadata.PipelineUrl`. **No report URL is emitted anywhere** | **RUN (read of the shipped generator)** (`Kronikol.dll` 19:07:51; file uncommitted at time of reading) | whether the C92/C106/C107 measurements actually reach the consumer the option names | That a rendered field carries the address. **It does not:** `grep -n "reportUrl\|\.html\|sid-"` over the generator returns **nothing**, and the only standard fields set are `buildName/buildNumber/buildUrl/repositoryName/commit/branchName`. **Consequence, measured not asserted (C92, C107): `extra` renders at zero across all 24 stock report flags, so the `sN` address the option's own doc says "leads back into `kronikol query`" reaches machine consumers reading the file and reaches no rendered consumer at all.** The option's doc names both audiences — "annotation actions, PR comment bots, flaky-test dashboards" — and only the last of those reads the file. **Not a bug**: `extra` is the schema's correct escape hatch, `buildUrl` is correctly left to the CI pipeline URL per C108, and Kronikol cannot know its own published URL. It is a **scope statement §8's wiki page must make** |
| C110 | **M3.3 shipped (`GenerateSpecificationsMarkdown`, default off), and it deliberately inverts C104**: the Markdown spec is **NOT** blanked on a failed run, where `Specifications.html` and the data file are | **RUN (read of the shipped option doc)** (`Kronikol.dll` 19:07:51) | that C104 is a *per-output* policy, not a global one — anything reasoning about "the spec is blank on failure" must now say which spec | That the new output inherited `generateBlankOnFailedTests`. **It does not, and the reason is stated in the source:** "a reader who reaches for it mid-failure needs the narrative most", plus "the same suite run red and run green produces the same bytes, which is what makes it safe to commit to a docs site" |
| C111 | **The Markdown spec carries `Rule` and `Description`, which the data trio drops — and the corpus could never have shown this** | **RUN (the implementer's model read) + my own corpus census** | C105's scope, and §13's recurring class | I measured `rule` at **0 of 1,349 scenarios** and correctly refused to claim the data file drops it — but I also named the wrong falsifier ("find a report carrying a Rule"), which **no corpus here can satisfy**. The implementer settled it by reading the model instead: `GenerateSpecificationsData`'s model flattens each step to a string and drops both `Rule` and `Description`. **Lesson, now §13 hazard 16: when the corpus cannot exercise a path, the model is the instrument and the corpus is not.** My C105 (examples blocks dropped) stands — it *was* corpus-exercisable and was controlled |
| C112 | **A1 reproduces verbatim on the current binary: `Failures.md` puts all 15 distinct failures in ONE cluster keyed `Assertion`, works through one, and prints "the rest are the same failure and need the same fix"** | **RUN** (`Kronikol.dll` **beside the fixture** 19:40:40, built from the 19:07:51 source; `Failures.md` 19:40, 4,180 B) | A1, §0.1 #1, M2 — the flagship finding, re-verified after M3 | That M3's digest work fixed it. **It did not:** `ClusterKey` (`FailuresDigestGenerator.cs:292`) is still `FirstLine(errorMessage)` whitespace-normalised, and commit `338231b` touched the **comparer**, not the key. Measured output: `### Assertion — 15 scenarios`, `### 1. …` (one worked example), `## 14 further failures`. The cluster label is **`Assertion`** — the xUnit v3 `FailureCause` enum name promoted to line 1 — so every assertion failure in the run collapses regardless of cause, and `kronikol query failures` over the same report lists all 15 **separately**, with at least four visibly distinct causes |
| C113 | **New surface, commit `338231b`: a comparer defect "that made `Failures.md` point at the wrong scenario" was found and fixed by the implementer — this ledger never held it** | **READ (commit message + the file it touched)** | A9/§4.1's accuracy claims, and the ledger's own completeness | That the audit had covered the digest's addressing. **It had not.** Eighteen survey lanes and 108 verifications produced no row about the digest pointing at the wrong scenario; it was found by someone writing the code. **A standing limit on this document worth stating: an audit over a corpus finds what the corpus exercises, and the implementer finds what the code does.** No experiment is owed here — the fix is in — but the gap is the point |

### 14.2 Not verified — and what this plan does about each
| Assumption | Status | How the plan avoids depending on it |
|---|---|---|
| The digest's populated-`calls` path works on a real failing-with-steps run | **UNPROMOTABLE BY THIS LOOP — needs a source change, re-checked 19:39.** Still no such fixture. The A1/A2 work added failing steps to `FailuresDigestGeneratorTests:151,396`, but those are hand-built `Feature[]`/`Scenario` objects passed straight to the generator — C83's exact objection, so they raise no depth. **Falsifier: one test project that fails *inside a step* carrying an attributed interaction, then assert `step.status == "Failed"` and a non-empty `calls` array in the emitted `Failures.jsonl`.** | M0.0 creates one before M1's red tests are written. Until then A2's fix is specified against four synthetic shapes (§2.3), not against production output |
| A real sharded run round-trips through `merge` | **UNPROMOTABLE BY THIS LOOP — needs a source change, re-checked 19:39.** `grep -rl mergeableFormatVersion --include=*.json examples tests` → **0**, still. `MergeCommandTests` does enable `GenerateMergeableData`, but calls `ReportGenerator.GenerateMergeableReportJson` directly on constructed data, so the shards stay synthetic and C44–C47 keep their `RUN(proxy)`. **Falsifier: `GenerateMergeableData = true` in one example project, `dotnet test --no-build`, then `kronikol merge`.** | Every F-row is `RUN(proxy)` over synthetic shards built from real reports. M0.0 produces a real one; M5 does not ship until a real shard has been diffed against the synthetic shape |
| The CI-log outcome on a live GitHub runner | **UNPROMOTABLE HERE — people-work (C75, §14.3 item 1).** Needs a live Actions run, i.e. a push. Its C84 half *was* settled without one (C93/C94). | §10 gate 6. B1–B3 and C1 are stated as mechanism claims, which they are, and the outcome claim is flagged rather than assumed |
| ~~The nested-`CLAUDE.md` mechanism~~ | **Verified — it fires, conditionally** (C58, C91) | M1.3's bet stands only for lowercase-drive-letter paths, reproduced 2/2 vs 2/2 on today's tree. The *cause* (matches-cwd vs always-lowercase) is **unpromotable in-session** — §14.3 item 8 |
| ~~The root `CLAUDE.md` block is auto-loaded~~ | **Verified — it is** (C86–C88), and it is a start-of-session snapshot | B4's severity splits (dev box yes, CI no); every "the agent will pick it up after `init-agents`" sentence is **false for the running session** and the plan now says so |
| `AGENTS.md` reaches any host | **Refuted for Claude Code** (C89, C99 — untracked *and* unloaded); **UNPROMOTABLE elsewhere — instrument absent** (C77): no `codex`/`cursor`/`amp`/`aider` on PATH. **If C77 is false, `AGENTS.md` is dead surface everywhere and nobody has checked.** | §3.2 keeps emitting both, but the plan no longer claims the `AGENTS.md` half does anything here. Its entire value rests on C77, which no session available to this loop can settle |
| A producer-set CTRF `extra` is a free rendered channel | **Refuted** (C92) | M7's CTRF item costs a Handlebars template **plus** consumer wiring. The honest free channel is the **machine** one: `write-ctrf-to-file`/`upload-artifact` preserve `extra` faithfully |
| The run-end signal can ride `$GITHUB_STEP_SUMMARY` | **Refuted** (C93) | It is not the job log. Q5, Q8 and B1 now name **stdout**, with the summary as an addition (C94) |
| ~~Kronikol is unreachable by an agent~~ | **Refuted** (C63, C64) | H1 becomes a redirect problem, not a visibility problem, and M6 gets cheap and measurable |
| Token counts throughout §2.1 | **DECIDES NOTHING — parked deliberately at bytes÷4.** Per the loop's own rule, a row that decides nothing may stay below `RUN` forever and should say so rather than consume an iteration. | Ratios are load-bearing, absolutes are not; a tokenizer run would sharpen §2.1 and change no decision |
| `HistoryMinRuns`-style thresholds (cluster caps, fallback N, budget ceilings) | **DECIDES NOTHING — chosen by design, not pending measurement.** Each is an option and each is stated in the output, so a derived value would replace a visible default with another visible default. | Each is stated in the output rather than hidden, and each is an option |

### 14.3 Rows that must not be promoted without an experiment

The error-class critic's standing list, rebuilt 2026-09-12 after re-running the load-bearing rows.
Each row: the claim id, **the step in it that no one executed**, and the experiment that would settle
it. A row stays here until its experiment is run — not until someone re-reads the evidence.

**Settled this pass, and removed from the list:** C48 (nothing reads `environment`) — re-run at RUN
depth: `EnvironmentOs`/`EnvironmentRuntime` have no reader in `src/` or `tests/`, and the OS string
`Microsoft Windows 10.0.26200` appears **zero times** in `TestRunReport.html`, `CiSummary.md`,
`Failures.md`, `Reports/CLAUDE.md` and `kronikol query summary` output on the Mixed fixture. E5 is an
artifact defect, as stated. Also re-confirmed live: C24 (669/1,284 = 52.1% step-less, my own census over
37 reports), the flag-parity row in §0.2 (re-run at 16:50 against the 16:33 M2.8 edits — `comm` still
empty both directions, dogfood copies still byte-identical), G3 (both footer defects reproduce verbatim
on the 16:5x binary: `next: --service Dessert Provider --offset 3` pastes back as `Not an address:
Provider`, and `interactions s0 --limit 2` emits `next: --offset 2`, which returns 69 rows where 6 were
scoped), E3 (`services` prints `Event broker 8 calls 8 errors Respondedx8` today), and the four commands
`Failures.md` prints (all four run, exit 0).

1. **C75 — "no CI-log signal in the default configuration."** Two option defaults are `READ`; the
   *outcome on a runner* is not. **Experiment: one real GitHub Actions run** (§10 gate 6). Still the
   highest-value unpromoted row in the ledger. Fold in C84 below — the same run answers both.
   **PARTLY SETTLED, and the rest is UNPROMOTABLE here — iteration 4.** C84's half was settled without
   a runner (item 11, C93/C94): the step summary is **not** the job log, measured on two existing
   public runs. What remains is C75 proper — *what a default project's CI log and artifacts look like
   on a real runner* — and it needs a live GitHub Actions run this loop cannot launch (launching one
   means pushing, which is a write outside `LLM_FIRST_PLAN.md` and an outward-facing action).
   **Unpromotable by this loop; still the highest-value unpromoted row.** It is §10 gate 6 and it is
   people-work. What rests on it: B1's and Q8's outcome claims, which the plan already flags as
   mechanism-claims rather than outcome-claims.

2. **C74 — the M1.0 channel table is a prior session's measurement, re-used, not re-run.**
   **SETTLED for the stdout column — loop iteration 2 (C95, C97), with the A/B control the original
   table never had.** Re-run on `Kronikol.dll` 18:18:25 via `--no-build`, so the other session's tree
   was never touched. The pointer is **absent** from `dotnet test` at default (376 B), `-v normal`
   (3,016 B) and `--logger console;verbosity=detailed` (3,035 B), and **present** in the *same*
   assembly executed **directly** (4,236 B). The report was regenerated in both arms, so the option is
   on and the line is emitted: **VSTest swallows it.** B1, B3, Q4 and §0.1 #3 now rest on an executed
   measurement rather than a re-used table.
   **The stderr column is closed differently from how it was posed:** 0 bytes on stderr in *both* arms
   (C97). There is nothing to survive, so **C76 is unmeasurable as shipped** and stays unpromotable
   until someone writes to `Console.Error` on purpose. §14.4 item 1 should say that rather than
   "unmeasured, cheap".
   **Still not re-run:** the other seven adapters and the MTP/TUnit runner. The "every VSTest runner"
   quantifier is still the prior session's.

3. **C44–C47 — every `merge` row is `RUN(proxy)` over synthetic shards.** **Experiment: one real
   sharded run** (M0.0). Shape evidence is not behaviour evidence.
   **Re-measured 18:28, loop iteration 3 — still zero, and the blocker is not what this list says.**
   `grep -rl "mergeableFormatVersion" --include=*.json examples tests` → **0 files**. M2.4 shipped
   `merge` writing JSON and `diff --baseline`, and M0–M2 are now **all** complete, yet **no mergeable
   report has ever been written to this disk** — `GenerateMergeableData` is off by default and no
   project in the tree turns it on. **So this row is not BLOCKED(tree), and `--no-build` does not reach
   it**: it needs a project configured to emit mergeable data, which is a source change outside
   `LLM_FIRST_PLAN.md`. **Unpromotable by this loop.** Falsifier: set
   `ReportConfigurationOptions.GenerateMergeableData = true` in one example project, `dotnet test
   --no-build`, then `kronikol merge`. F1–F3 and E2 all still rest on synthetic shards and the plan
   must keep saying so.

4. ~~**C53 — the deep-link suite is jsdom.**~~ **CLOSED as decides-nothing — loop iteration 3.** The
   row's own Decides cell reads "§0.2; nothing in this plan", and three iterations have confirmed it:
   no milestone, recommendation or open-question answer rests on it, because M4's behaviour claims are
   carried by C54–C57, which are **already real Chromium**. Per the loop's own rule — *a row that
   decides nothing may stay below `RUN` forever, and should say so rather than consume an iteration* —
   this row is now **permanently parked at `RUN(proxy — jsdom)`**. The falsifier is unchanged and cheap
   if anyone ever wants it (re-run the four positive cases in Chromium); nothing should wait for it.

5. **C77, C78 — the whole non-Claude half of M1.3's reach.** **Experiment: one Codex or Cursor session**
   reading a generated `AGENTS.md` from a gitignored directory.
   **UNPROMOTABLE — measured absence of the instrument, iteration 1.** `command -v` for `claude`,
   `codex`, `cursor`, `amp` and `aider`: **none is on PATH on this machine.** So this is not "not yet
   run", it is unreachable from any session here. What rests on it: the whole non-Claude half of
   M1.3's reach, plus C89's consolation that emitting `AGENTS.md` is worth anything at all — in this
   host it is loaded by nothing (C99). **If C77 is false, `AGENTS.md` is dead surface everywhere**, and
   nobody has checked.

6. **C79, C80 — M6's cost and its measurement.** **Experiment: attempt the submission; run
   `claude plugin eval` once.** Note the verifier could not even execute `claude plugin validate`
   (no `claude` binary on PATH), so §6's M6 gate is currently specified against an unavailable tool.
   **UNPROMOTABLE — confirmed independently, iteration 1.** `command -v claude` → not on PATH here
   either, so §6's M6 gate is still specified against a tool no session in this environment can run,
   and the marketplace submission (C79) is an outward-facing action this loop may not take. Both stay
   people-work. Note the asymmetry worth keeping: C80 is the only instrument in H2 that measures
   *use* rather than *reach*, so M6's success criterion is currently unmeasurable by design.

7. **NEW — C81 (B4's narrowing): "the root `CLAUDE.md` block is tracked, greppable and auto-loaded."**
   Two of the three predicates are unsupported. *Tracked* is **false**: `git show HEAD:CLAUDE.md |
   grep -c kronikol:begin` → `0`; the block is an uncommitted working-tree edit (` M CLAUDE.md`, mtime
   14:30:57), so a fresh clone and CI have no such pointer. *Auto-loaded* was **never executed** — it is
   an existence check on a file plus an assumption about the host — and the two observations available
   both contradict it: the audit session's own project-instructions attachment
   (`~/.claude/projects/c--Code-Kronikol/f29483dc-…jsonl`, record 16, timestamped
   `2026-09-12T13:35:27.927Z` = 14:35 local, **4.5 minutes after the block was written**) carries the
   pre-block bytes, and so does this critic subagent's system prompt.
   **SETTLED — loop iteration 1, 2026-09-12 (C86, C87, C88).** The experiment was run without needing
   the commit: this session started 2 h 51 min after the block was written, and its own
   `attachment/instructions` record carries the block **in full**, from an **untracked** working-tree
   file. *Auto-loaded* is `RUN` and the critic's inference is reversed (§14.0, fourth table). *Tracked*
   stays **false** and still costs CI and every fresh clone their pointer. The snapshot question is
   answered too, and the answer is the bad one: **the attachment is a start-of-session snapshot, so
   `kronikol init-agents` cannot reach the session that ran it** — the pre-edit session the critic cited
   is not a counter-example, it is the rule. Two consequences the plan must carry: the loader **strips
   the `kronikol:begin`/`kronikol:end` markers**, so an agent reading its own instructions cannot tell
   Kronikol's block from the repo's own prose (C87); and the same attachment proves **`AGENTS.md` is
   never loaded by this host at all** (C89). B4's severity is now split rather than restored: the
   root-`CLAUDE.md` channel **works, for the next session, on a dev box**, and is **absent in CI**.

8. **NEW — C82 (C58's real scope): the nested-`CLAUDE.md` load is conditional on path spelling.**
   §0.2 promotes it to "the mechanism fires"; §13 hazard 8 records path case as a *probe* confound and
   the plan never carries it into the product conclusion. Measured here, 2×2, one session, four
   different reports directories each holding a 4,891-byte `CLAUDE.md`: Read with `C:/Code/Kronikol/…`
   → **no injection** (2/2); Read with `c:/Code/Kronikol/…` → **injection** (2/2). This session's cwd is
   `c:\Code\Kronikol`. Kronikol resolves relative paths and `AppDomain.BaseDirectory` to an **uppercase**
   drive letter (`kronikol ingest inter.ndjson` echoed `C:\Users\…` from a lowercase cwd; the audit's own
   library-pointer run printed `Kronikol: reports written to C:\…\Reports`), so an agent that follows
   Kronikol's own printed absolute path defeats the mechanism M1.3 is built on.
   **RE-CONFIRMED, loop iteration 1 (C91).** Independently reproduced on today's tree — `Kronikol.dll`
   18:01:45, fixtures 17:04/17:05 and 16:50 — as a fresh 2×2 over **four different** reports
   directories in one session, each read exactly once so no caching can explain it:
   `C:/…Component.xUnit3/…/Failures.md` → **no injection**; `c:/…Component.LightBDD.xUnit3/…` →
   **injection**; `C:/…Component.ReqNRoll.xUnit3/…` → **no injection**; `c:/…Kronikol.Tests/…` →
   **injection**. 2/2 and 2/2, matching the critic's numbers on a different corpus.
   **What this still cannot settle, and no session started in this cwd ever can:** "fires when the case
   *matches the cwd*" and "fires when the path is *lowercase*" predict the identical result here,
   because this session's cwd is itself lowercase (`c:\Code\Kronikol`, transcript record 15).
   Discriminating them needs a session **started** with an uppercase cwd, which a session cannot do to
   itself — **unpromotable in-session; falsifier: open one Claude Code session in `C:\Code\Kronikol`
   and repeat the 2×2.** The distinction is not academic: under "matches the cwd" the fix is to echo the
   path in the cwd's case, under "lowercase" it is to lowercase the drive letter unconditionally.
   **The product half is now SETTLED — loop iteration 2 (C96), and it confirms the bad case.** The
   pointer prints `Kronikol: reports written to C:\Code\Kronikol\…\Reports` — **uppercase `C:`**,
   captured verbatim from the direct-run arm of C95. So the two halves meet: the product tells the
   agent to go to a spelling that C91 measured **does not** fire the injection. **The fix is one line
   and it is now specified**, whichever way the cause falls out — print the reports directory
   **relative to the cwd**, which is correct under both the "matches the cwd" and the "lowercase"
   hypothesis and is shorter output besides.

9. **NEW — C83 (§2.3 case B, "the mechanism is alive").** The populated-`calls` output was produced by a
   **reflection harness** that loaded `Kronikol.dll` and called `FailuresDigestGenerator.Generate` with
   hand-built objects. That proves the generator emits calls given the shape; it does not prove any
   shipped lane produces the shape. My census over **all 37 `TestRunReport.json` in Kronikol,
   BreakfastProvider and Kronikol4J**: 14 reports carry attributed `stepPath`s (up to 320 distinct) and
   have **zero** failures; the 2 reports with failures (CiPreview.Mixed 15, AllFailing 11) have **zero**
   steps; scenarios with a failed step *and* an attributed interaction: **0 of 1,284**. All five
   `Failures.jsonl` on disk: `calls: []` on every row. **Experiment: M0.0's failing-with-steps fixture,
   then assert a non-empty `calls` array in the emitted `Failures.jsonl` — from a run, not from a
   harness.** Until that passes, A2 may be an attribution defect and not only a predicate defect, and
   Q2's fallback is the only thing known to work.
   **SHARPENED, loop iteration 1 (C90) — and the defect is one layer deeper than either of us thought.**
   Re-run over the live corpus (37 reports, now 1,349 scenarios) with a positive control: not merely
   *zero* failed-step-with-call scenarios, but **zero failed steps at all**. `step.status` is `Passed`
   **4,944 times out of 4,944**, and **no step anywhere carries a `failureMessage`**. The two reports
   that do hold failures — CiPreview.Mixed (15) and AllFailing (11) — carry **zero steps** while
   carrying 20 and 11 interactions; the 615 scenarios that *do* carry steps are all green.
   So the missing link is not attribution and not the predicate: **nothing on disk has ever recorded a
   step as failed.** Two hypotheses remain, and the corpus cannot separate them — *no adapter ever sets
   `step.status` to `Failed`*, versus *no failing-with-steps run has ever been captured*. M0.0's fixture
   settles both at once, and its red test must assert **`step.status == "Failed"` first**; asserting a
   non-empty `calls` array on its own would pass through a fixture that never failed a step and tell
   nobody anything. **BLOCKED(tree)** — the command is `dotnet test` over a failing-with-steps project.
   **RECLASSIFIED — loop iteration 4. This was never BLOCKED(tree).** The tree went quiet at 18:38 and
   the row did not become reachable, because what it needs is a **failing-with-steps fixture that does
   not exist** — a new test project, i.e. a source change outside `LLM_FIRST_PLAN.md`. No amount of
   `--no-build` or waiting produces it. **Unpromotable by this loop**, same category as item 3.
   Falsifier, one project: a test that fails *inside a step* with an attributed interaction, then assert
   `step.status == "Failed"` **and** a non-empty `calls` array in the emitted `Failures.jsonl`.

10. **C37 / §4.3 C1 — the injected workflow command is `RUN` on a channel §0.1 #3 says is dead.**
    The runner's parser was read (correct); the *reachability* was demonstrated on `kronikol ingest`
    (its own process) and on a **directly executed test host**, i.e. exactly the two rows the M1.0 table
    marks "yes". Under `dotnet test` + VSTest at default or `-v normal` — the configuration §0.1 #3
    calls the default — the same table says the pointer never reaches the job log, so nothing reaches
    the command parser either. B1 and C1 cannot both be true of one channel and neither row names its
    configuration.
    **RESOLVED — loop iteration 3, from C95 rather than from gate 6.** The contradiction dissolves once
    the channel is measured instead of assumed. C95 showed that under `dotnet test` + VSTest **nothing
    Kronikol writes reaches stdout at all** — not the pointer, therefore not a `::` command either — and
    that the *same* assembly run **directly** puts it there. So **C1's severity is scoped to the
    channels where output survives**: a directly executed test host (xUnit v3's self-hosting exe, MTP),
    and `kronikol ingest` / `kronikol query` in their own processes. Under the configuration §0.1 #3
    calls the default, C1 is **unreachable** — which is not reassurance, because it is unreachable for
    exactly the reason B1 is a defect. **State it as one sentence: the injection rides whichever channel
    carries the pointer, so fixing B1 without fixing C1 ships the hole.** The gate-6 run is no longer
    needed to settle this, only to confirm it on a runner.

11. **NEW — C84: `$GITHUB_STEP_SUMMARY` is not the job log.** The verifier's correction to B1's proposal
    ("writing the notice to the step summary does **not** put anything in the log; `gh run view --log`,
    the command an agent reaches for, does not show it") never reached the plan, while §12 Q5 and Q8
    both route the run-end signal there. **SETTLED — loop iteration 1 (C93, C94), and without needing gate 6.**
    The falsifier was executed against two *existing* public runs rather than a new one: a workflow that
    echoes literal text into the summary (grafana/grafana 34702879360) and one where the action writes
    it in code with the variable never named in the YAML (ctrf-io 34678802541). In both, the summary
    content is **absent** from `gh run view --log`. The confound that would have produced a false
    negative-of-the-negative is resolved explicitly: the literal *does* appear in grafana's log inside
    the `##[group]Run …` header, because the runner echoes the **command**; the proof that this is
    incidental is that the **composed** line — `${STORYBOOK_URL}` expanded — appears nowhere in 726 KB.
    **So §12 Q5 and Q8 are both wrong to route the run-end signal to the step summary alone**, and B1's
    proposal must name **stdout** as the channel an agent can read, with the summary as a human-facing
    extra. C94 is the constructive half: `tee -a "$GITHUB_STEP_SUMMARY"` reaches both at once.
    **Not settled, and don't repeat it:** the *rendered* summary was never retrieved — check-run
    `output.summary` is empty for all 17 jobs, the run page loads it client-side, and seven
    `summary_partial`-shaped endpoints 404. This row rests on the negative side of the falsifier plus a
    source read of where the bytes go, which is enough for the claim as stated but would not settle a
    claim *about the rendered summary's contents*. Secondary, same strength: **artifact contents are not
    in the log either** — 8 of 8 test names from a downloaded artifact appear 0 times in its run's log.

12. **NEW — C85: fixture vintage cannot be read off `kronikolVersion`.** Every working-tree build stamps
    `3.0.86+34388cb…`, so the version string separates nothing. Two Component fixtures dated **today**
    (11:26) carry a 23 KB schema, no `Failures.md`, no `CLAUDE.md` and **zero** `data-stable-id` — none
    of M1 or M2.1 — while the 15:59/16:00 ones carry all of it. §1.2's rule ("record the mtime or commit
    of the binary and the fixture") is insufficient as written: the commit is constant across the whole
    audit window. **Rule: record the mtime of the `Kronikol.dll` sitting beside the fixture, and assert
    it in M0's freshness test** (§6's M0 row already wants this — point it at the DLL, not the report).
    **CLOSED as a rule, and audited — loop iteration 4.** Four iterations ran under it and it works:
    `Kronikol.dll` moved 16:57 → 18:01 → 18:18 and `Kronikol.Tool.dll` 18:01 → 18:04 → 18:23, and the
    drop it forced (C81/C82, re-run against 18:23:58) was real. **But the audit it invites is the
    finding:** of the 82 rows predating this loop, **exactly one** (C81) ever carried a stamp, so "a row
    whose binary has moved drops back to UNVERIFIED" could never fire for the other 81. The rule is
    sound and was simply never retro-applied. Every row added from iteration 1 onward carries one.

13. **C60 / H7 — "reaches rendered bytes" needed a template the project does not ship.** The RUN that
    produced the pointer in `GITHUB_STEP_SUMMARY` ran with `INPUT_CUSTOM-REPORT=true` and a hand-written
    `kronikol.hbs`. **SETTLED — loop iteration 1 (C92), and the answer is the expensive one, with one
    narrow escape hatch that is worse than it looks.** The sweep was run: 24 stock report flags
    individually, then all 19 at once, plus `annotate`, over a CTRF fixture carrying the sentinel in
    `results.extra`, `summary.extra`, `environment.extra` and all three tests' `extra` — **0
    occurrences in the rendered `GITHUB_STEP_SUMMARY` in every one**, and 0 in the emitted annotations,
    which carry name + message + `file:line` only. The positive control rendered **9 of 9** through a
    hand-written template, so the zeros are real. **Therefore M7's CTRF item is not a free channel**: it
    costs shipping a Handlebars template *and* getting every consumer to add `custom-report: true` plus
    a `template-path:` to their workflow — two-sided adoption, not "emit CTRF and it appears".
    **The narrowing:** stock `ai-report` / `ai-summary-report` *do* render producer-set
    `results.extra.ai` and `results.extra.aiSummary` verbatim, offline, with no API key — there is no
    writer for `extra.ai` anywhere in the action's `src/`, so it is a producer slot by design. That is a
    real zero-template channel and this plan should **not** take it: it renders under an "AI Analysis"
    heading, still requires the consumer to set `ai-report: true`, and is a semantic abuse of a reserved
    key that the action's own `src/integrations/ai.ts:233` will overwrite the moment a consumer supplies
    a key. **What *is* free and real: `write-ctrf-to-file` / `upload-artifact` preserve `extra`
    faithfully** — a machine channel for downstream tooling, which is a smaller but honest claim, and
    §8's wiki page should say exactly that rather than implying a rendered one.
    **NARROWED AGAIN — iteration 5 (C106–C108), and this is the version M3.2 should be built against.**
    The follow-up question nobody had asked was whether a *standard* CTRF field renders. **One does:**
    `results.environment.buildUrl` comes out of stock `github-report` and `previous-results-report` as a
    **real clickable markdown link with the `#sid-` fragment intact**, no template and no consumer
    wiring — and the gate is effectively unconditional, since dropping `buildName` and `buildNumber`
    each still rendered it. So "rendered by nothing" is wrong twice over (the AI fields, and this).
    **But the shape of the win is narrow and the plan must not overstate it:** it is **one run-level
    link**, not a per-scenario one. No standard field renders a clickable per-test URL — `message` is
    unlinked inside a raw `<td>`, `trace` only inside `<pre><code>`, and `test-list-report`'s
    `escapeMarkdown` actively corrupts a URL into `https://example\.invalid/r\.html\#sid\-…`.
    **So C92's "you must ship a template" conclusion stands for `#sid-` deep links and falls only for
    the single report-level link.**
    And the free link is not free of meaning (C108): `buildUrl` conventionally denotes the CI run URL
    and the action back-fills it from the GitHub context, so writing it **replaces** the run link rather
    than adding one, and `previous-results-report` renders it as `[#<buildNumber>]`, which reads as a
    build. **M3.2's decision is therefore a trade, not a freebie, and should be taken deliberately.**
    **Honest limit on all three rows:** this measures *the markdown bytes the action writes*, offline.
    Whether GitHub's own step-summary sanitiser preserves the fragment, autolinks a bare URL inside a
    raw-HTML `<td>`, or permits `<a href>` there is **unmeasured** — no network, no GitHub. Anything
    built on the raw-HTML routes needs that check first.

14. **C20/C22 — "one adapter of eight" is the prefix's scope, not the defect's.** C22 (RUN, in this same
    ledger) shows *unprefixed* xunit messages still collapsing five distinct failures into one cluster
    keyed `Assert.Equal() Failure: Values differ` — which is the xUnit **v2** shape, no `FailureCause`
    involved. §0.1 #1 reads as a scope correction about the defect and will be refuted by the first
    reader who tries xUnit v2. **Experiment: one failing run per adapter (xUnit v2, NUnit, MSTest,
    TUnit, LightBDD, ReqNRoll) with two distinct failures of the same assert type; count clusters.**
    Restate §0.1 as: the *prefix* is one adapter of eight; the *first-line key* is all eight.
    **Restatement DONE (§0.1 already carries it). Experiment UNPROMOTABLE by this loop — iteration 4.**
    Same category as items 3 and 9, and for the reason C90 measured: **every example project in the
    tree passes**, and the only failing fixtures (CiPreview.Mixed, AllFailing) are xUnit3-generated and
    step-less. Counting clusters per adapter needs a deliberately-failing project per adapter, none of
    which exists. Falsifier unchanged; the cost is six small test projects, which is M0.0's work.

15. ~~**C43 / A5 — "no per-field cap" is the pre-narrowing wording.**~~ **DONE — loop iteration 3.**
    A5's row in §4.1 now reads "no cap on the **message fields**, and `cluster` repeats the first line
    of `errorMessage` verbatim", with the C43 narrowing (call summaries **are** capped at 120 at build
    time) stated inline. No experiment was needed and none was run.

16. **C71 / §9 — the count survives, the reason does not.** Re-run: `data-stable-id` lands on
    `<details class="scenario">` and nowhere else (20 attributes, 20 scenarios, **0** of 24 `<tr>` rows
    on the Mixed fixture), so "and every summary row" is false; 42 of the port's 45 goldens do contain
    `<details class="scenario"` so 42/45 is right. **Experiment before promoting to a release note:
    regenerate one port golden and diff it**, rather than inferring the byte move from the attribute's
    presence.
    **DISPOSED — loop iteration 4 (C102). The 42/45 is confirmed by census; the diff is unpromotable,
    and my first attempt at it refuted itself.** Census over the port's parity directory: **45** HTML
    goldens, **42** carry a `<details … class=*scenario*`, **0** carry `data-stable-id`, while the
    current build emits it (4 occurrences in one fresh output). The figure holds and the goldens are
    all pre-M2.1.
    **The diff cannot be run here**, for a reason worth recording rather than retrying: there is **no
    parity-fixture regeneration path in either repo** (`grep -rln parity` over the .NET repo's
    `*.csproj`/`*.ps1`/`*.sh` → nothing; no script in the port), and **the .NET unit-test outputs are
    not the parity fixtures** — the identity control failed outright, `<title>Test</title>` against
    `<title>Kronikol Run</title>`, on a 460 KB file versus a 276 KB one. Diffing that pair would have
    measured two different reports and reported it as version drift.
    The residual unverified step is narrow and §9 should say so: *42 goldens carry the element, the
    element now carries a new attribute, therefore those 42 files' bytes move.* Sound — but still an
    inference, and it is the one the release note rests on.

17. **M0's `CommandTableTests` line is already done.** The first-line anchor (`Assert.Contains("kronikol
    " + name, first)`) is in `tests/Kronikol.Tests/Tool/CommandTableTests.cs:62-74`, mtime 15:26, with a
    doc comment naming the mutation. §1.2 records this and M0 still lists it as work — the exact hazard
    §1.2 warns about. The surviving residue is the **duplicate-delegate fact** over `Commands.Table`
    (`Delegate.Method` distinctness), which is what the M0 row should say.
    **CONFIRMED and DISPOSED — iteration 4.** Re-read at 18:4x: `CommandTableTests.cs` carries the
    first-line anchor (`Assert.Contains("kronikol " + name, first)`) inside
    `Every_command_answers_its_own_help_with_its_own_usage`, with the doc comment naming the mutation —
    so that half is done and M0 must not re-list it. And `grep -n "Delegate\|Method)"` over the same
    file returns **nothing**: **no test asserts `Delegate.Method` distinctness over `Commands.Table`**,
    so the residue is real and is the only thing M0's row should carry. M0's row was separately
    corrected this iteration for `templates/agents/`, which `e91cffb` closed (C98).

**Left for a person by M8 (2026-09-14) — outward-facing, not for a session:** (a) submit
`.claude-plugin/marketplace.json` to the community marketplace (C79); (b) run
`claude plugin validate --strict` and `claude plugin eval` once against the manifest (C80) —
`PluginManifestTests` is the offline stand-in until then; (c) deprecate the `TestTrackingDiagrams.*`
packages on nuget.org naming `Kronikol.*` as the alternate (H1); (d) apply for the `Kronikol.` reserved
prefix (H4); (e) publish the MCP registry entry only if `MCP_PLAN.md` is green-lit (H3).

*This list is the error-class critic's, adopted wholesale. It removed one row it settled (C48), added
eleven, and reversed three of §0.2's and §4's own conclusions — EC1, EC2 and EC3 below.*

### 14.4 Open investigations
1. **Does `Console.Error` survive VSTest?** (C76) **Not "unmeasured, cheap" — unmeasurable as
   shipped, and that is now executed** (C97): stderr is **0 bytes** under `dotnet test -v normal` and
   0 bytes on a direct run, because the product writes nothing there (C6). The question cannot be asked
   until something is written to `Console.Error` on purpose, so this is an experiment that **requires a
   source change first**, not a measurement someone forgot to take. Note the measured trap that still
   stands: the swap is not one delegate — the `::notice` rides the same sink and only works on stdout.
2. **Is the `.ignore` recipe complete?** The nine-line recipe was found incomplete in three measured
   ways, each a location the default `ReportsFolderPath` can resolve to. Settle the full set before
   `init-agents` writes it.
3. **Does injecting one line into the first failure's framework message survive the runners?** §11
   reopened this as a question because M1.0 removed its premise. It is the only channel guaranteed to
   appear in every runner's default output. **Measure before deciding**, and the measurement is the
   same harness as C74/C76.
   **HALF-SETTLED — loop iteration 4 (C103), and it is the good half.** The channel is **live**:
   `Example.Api.Tests.CiPreview.AllFailing` under `dotnet test --no-build` puts full `Error Message:`
   bodies and stack traces with `file:line` on stdout, while the **same run's** Kronikol pointer is
   absent (`grep -c "reports written"` → 0). So the premise holds — *this is the one channel measured
   to reach a VSTest log*, which is what makes it worth designing against.
   **The unsettled half is whether Kronikol can write into it.** The adapter's `FailureCause` prefix
   (C4) lands on the captured `ErrorMessage`, which is report-side; **no `FailureCause` string appears
   in the runner's output**, so the existing injection does *not* demonstrate runner-side reach.
   Falsifier for the rest: prepend a sentinel to the message the *framework* raises (not the one
   Kronikol captures) and grep a `dotnet test` log for it. That is a source change — unpromotable by
   this loop, and it is now the cheapest unbuilt thing in M3.
4. **What does the declarative field table actually cost?** (§7.3) The eight-place bill is measured for
   3.1.0 (C71). The saving is not. Cost it in M3 against one real field addition before committing.

---

## 15. Where this sits

`LLM_FRIENDLY_PLAN` finishes first — M2.8 and M2.9 landed during this audit, and M3.1/M3.2 are
obligations `CROSS_RUN_HISTORY_PLAN` §1.5 depends on. This plan's **M0 is not this plan's to defer**:
staging `templates/agents/` is a one-minute fix for a **CS1566 on a fresh checkout** that would break
everyone else's build, and `IsError` is eight lines that stop the tool inventing 208 errors on a green
run. Both belong in 3.1.0 whatever happens to the rest of this document.

**M1 is deadline-bound and the deadline is the tag.** Its whole content is shapes an outsider parses,
and §5.2 row 1 is measured in hours: `--json` is publishing `formatVersion: 1` **today** around a
`next` pointer that is broken for every multi-word filter value.

Everything else can wait for a green light, with one exception worth stating plainly: **M2 fixes things
the shipped release will otherwise say to every consumer who upgrades.** A digest that suppresses
fourteen failures in fifteen and asserts they need the same fix is worse than no digest, because the
reader acts on it.

---

## 16. Implementation log

### 2026-09-12 — §5.4's "cheapest start", all three items, executed against a green tree

Not a milestone: the three things §5.4 says are worth doing whether or not this plan is ever green-lit.
All three were **re-verified before being acted on** rather than taken from the audit — the plan's own
base rate (47 of 108 findings not surviving as written, §14.0) makes that the only honest way to read
it. All three survived, and two were **wider than the audit stated**.

1. **`git add templates/agents/`.** Confirmed exactly as described: the directory was untracked while a
   tracked, modified `Kronikol.Tool.csproj:35` embeds `..\..\templates\agents\CLAUDE.md` by literal
   path. A release commit staging the csproj without it is CS1566 on a clean checkout — CI fails before
   a test runs. Staged.

2. **`IsError`.** Confirmed, and the audit named three of six. `MessageTracker`'s own defaults are
   `Sent`, `Ack` and `Responded`; a cache lookup is `Hit` or `Miss`; `SpannerResponseFormatter` returns
   `Committed`. Every one was an error, because the allowlist held only the HTTP non-200 successes.
   **A cache miss is an outcome, not a failure**, and that one is the reason to state the rule as a
   named vocabulary rather than a chain of `Equals` — the next extension that invents a label will
   otherwise be an error too. `Nack` and `Fault` stay errors. Two facts, over a fixture of Kronikol's
   own non-HTTP labels; the fix is a `HashSet` behind the same single classifier, so `services`,
   `flow --errors-only` and `--group-by` cannot disagree.

3. **The envelope's `next`** — §5.2 row 1, and the one whose deadline was hours. Confirmed, and it was
   worse than the quoting break: an argv rebuilt from the flags alone **also dropped every positional
   address** (`interactions s0 --json` resumed across the whole report — same envelope shape, silently
   wider answer) **and the page size** (page two came back at the default width, so the pointer did not
   resume the paging it was following). `next` is now the whole command as an argument vector. The text
   footer is unchanged in content and quotes what it must; both render the same token list, which is
   what stops them drifting. `formatVersion` stays `1` — no released build ever emitted the string form,
   which is precisely the window §5.0 calls clock A, closed with hours to spare rather than a
   `formatVersion: 2` and a dual renderer.

**A test caught a real slip while being written.** The address fact failed on first run because the
envelope was built from `options.Positional.Skip(1)` — `Skip(1)` copied from the report-is-first-
positional shape, when `QueryOptions.Parse` already routes the report to `File` and leaves `Positional`
holding only the addresses. The fact that exists to stop the pointer dropping its scope caught the fix
for that bug dropping its scope. The other three new facts were mutation-proved deliberately
(flattening the vector on spaces; disabling the quoting), each failing only its own test.

One extra defect fell out on the way: `--slower-than` was interpolated into the pointer in the current
culture, so a comma-decimal locale produced `--slower-than 5,5`, which the parser rejects.

**§5.2 row 3, `ciMetadata.runAttempt`, done the same day once the user took the call.** The eight-edit
checklist was accurate to the line — all four emitters (`MapCiMetadataJson`, the XML sibling, the YAML
line, the XSD element) and all three readers were exactly where §5.2 said, and `TestRunReportSchemaContractTests`
did enforce the JSON Schema edit. Two things the checklist did not carry: the query scanner's `KnownNames`
interning table needs the key too (a perf convention, not correctness), and **nothing read `CiRunId` either**
— it had no test anywhere and no verb printed it, so adding `runAttempt` beside it would have shipped a
second write-only field, which is the criticism this plan levels at `environment`. `kronikol query summary`
now prints `attempt 2` on the `run:` line past the first attempt and carries `runId`/`runAttempt` in
`run.ci` under `--json`. Azure DevOps ships null, per checklist row 3: it exposes no equivalent variable
and a guess would be worse than an absence.

**Verification:** 4228 unit (up 6) / 0 failed. E2E had been confirmed 758/0/28 immediately before this
work, on a solo `--no-build` run — that run predates these three items and is owed a repeat.


### 2026-09-13 — M2, `Failures.md` tells the truth — shipped as 3.2.0

Every row M2 names, plus Q1 and Q2, plus two defects that only existed in output. TDD throughout; the
mechanisms that could be got wrong quietly are mutation-proved, and the row is named in each commit.

**A1 — one cluster key, all four sites.** Three were already folded into `FailureText.FirstLine` by the
earlier commits in this release. The fourth was a second `FirstLine` inside `FailuresDigestGenerator`
itself — not a cluster key, it summarises a statement, but it trimmed where the shared one collapses, so
two functions of the same name in one file disagreed about what a line is. Folded away.

**A3 — fencing.** Two halves. Inline spans: the delimiter is now sized past the longest backtick run in
the value, with padding when the value starts or ends with a backtick or a space — but *not* when it is
nothing but spaces, where a reader would not strip the padding back and a whitespace assertion would gain
two characters it never had. Block fences: the error block used to rewrite ``` in the payload to ''',
which keeps the file well-formed by falsifying the evidence; it widens the fence instead. **The first
test written for this was wrong** and is worth recording: it counted backticks per line and required an
even count, but `` a ` b `` holds five and is perfectly well-formed. The test now implements CommonMark's
rule — the closing run must be the same length as the opening one and must land at the end of the cell —
which is the difference between checking the property and checking a proxy for it. Mutation-proved both
ways.

**A4 — surrogate-safe truncation**, in `FailureText.Truncate` with its eight call sites, and the same
defect in `QueryWriter.OneLine` where the ending is worse (a terminal and the `--json` envelope mangle
silently rather than throwing). Plus the **two-action `RunOutputs` split**: the digest was one entry in
the isolated output list, so the rule that an output which throws costs only itself stopped at the pair.
The existing test pinned the old behaviour in as many words; it now pins the opposite.

**A5 — caps.** One 4,000-character cap across every free-text jsonl field and a `truncated` flag. The
`cluster` duplication stays — both halves of one digest must group failures the same way — it just costs
a bounded amount now.

**A6 — all-skipped wording**, at both sites, landed in the earlier commits of this release.

**A8 — link gating.** Gated on the option, not on `File.Exists`: the HTML is written by a sibling action
in the same parallel list, so checking the disk would answer whatever the scheduler happened to have done.
The residual — a report whose write threw — is named in the code rather than papered over.

**A9's digest half.** See the two output-only defects below; the shipped rule is not the one this plan
proposed.

**A2 + Q2 — `callsScope`.** The nested-path mismatch is real and was the whole of the mechanism: a call
attributed to step `1` is now taken as the answer for a failure in `1.0`, matched segment by segment so
that `1` is not an ancestor of `10`. The fallback is the scenario's own calls, failures first then most
recent, under a heading that says what it is. `InteractionStatus.IsError` moved out of the tool so the
digest asks the same question `query services` does — its own docstring calls it "the single classifier",
and a second copy would have made that false.

**Q1 — suppression capped.** Adopted as recommended and then some: a cluster is sampled (one example plus
one per ten members, to five) rather than reduced to one, and *both* previously unbounded lists are capped
with an announced remainder. Measured: 1,200 failures now produce under 80,000 characters of markdown.

**Two defects that only existed in the output.** Both found by regenerating `CiPreview.Mixed` and reading
`Failures.md`, and both invisible to every unit test, because the fixtures were built from what the code
expects rather than from what a run produces:

1. **`Thrown at` named the assertion library.** xUnit v3 ships source-linked PDBs, so a real trace carries
   three `Xunit.Assert.Equal` frames with a file and a line before the test appears. "The first frame with
   source information" — the obvious rule, and the one written — pointed all nine worked examples at
   `StringAsserts.cs`, which is the exact outcome the code comment claimed to be avoiding. The declaring
   file the producer already reported now wins, with a framework-namespace skip as the fallback.
2. **The Call column printed a message body.** `IsStatementLike` was "anything that is not an HTTP verb",
   and a `MessageQueue` send carries the method `SEND (EVENT PROTOCOL)` — neither empty nor a verb. The
   dependency category decides now, as a positive list so that a category added later loses detail rather
   than leaking a body.

**The plan did not predict either of them**, and neither is in §14's ledger. They belong to the class
§17.0 already names: a check that stops at the code cannot see what the code produces. The M3–M8 rows
should be read with that in mind — each one's falsifier should end at generated output, not at a green
unit test.

**Verification:** 4,498 unit / 0 failed (up from 4,458 at the start of M2). `CiPreview.Mixed` regenerated
three times and read each time; the numbers above are from it. Mutation-proved: `Code` reduced to a naive
backtick wrap reddens five fencing cases; the `'''` substitution restored reddens the block case;
ancestor matching reduced to equality reddens the composite-step case; the fallback reduced to a naive
last-N reddens the errors-first case; forcing the tool's console to CP437 reddens the stdout-encoding case
(and the same probe, run against the pre-fix build, is what proved the fix was not decorative — it wrote
`0xFA` where the fixed build writes `C2 B7`).

**Not in M2 and deliberately left:** A7 (the jsonl `formatVersion` still has no reader — M3's acceptance
row owns `Query_refuses_an_unknown_formatVersion`) and A9's query half (M7 — `ReportScanner` does not read
`errorStackTrace`). The digest/`query failures --json` divergence is now **written down in
`FailureRecord`'s own docstring** rather than left for a consumer to find by diffing two files that claim
to describe the same failure: seven fields and three caps differ.

### 2026-09-13 — M3, you find out from the log that the run failed — shipped as 3.3.0

Every row, and **two of them came back different from how they were written**.

**B3 is the reversal.** Q4 said: "if stderr survives VSTest, the pointer is a diagnostic and belongs
there on principle." Measured rather than assumed — a probe line written to stderr from the run-end hook,
then `dotnet test`: under **NUnit 4 the stderr line survives while the stdout pointer is swallowed**;
under **xUnit 2 both are swallowed**. Both are VSTest. So stderr is better on one runner and no better on
another, and the premise is half-true, which is not enough to move a channel on. **The pointer does not
move.** The honest conclusion is that the console cannot be made reliable by choosing a different stream,
which is why the fix that works is B1's two channels and why the files beside the report remain the one
that always works. The numbers are now in `WriteCiDebugSection`'s own documentation so the next person
does not re-derive them. The structural half of B3 — a separate sink for the `::notice` — shipped anyway,
because the constraint is real whatever the default is: the annotation is a workflow command GitHub parses
from stdout and nowhere else, so one shared sink meant a future decision about the pointer would silently
delete the annotation.

**B1** shipped as §5.5 requires: a NEW option, `WriteCiDebugSection`, default on, rather than flipping
`WriteCiSummary` or `PublishCiArtifacts`, either of which is a MAJOR bump under the repository's own rule.
Writes to stdout AND the job summary, per C93/C94.

**B4** shipped and is measured end to end with real ripgrep, on a scratch repository, before and after:
without `.ignore`, a marker inside `bin/Debug/net10.0/Reports/Failures.md` is not found; with the block
`init-agents` actually writes, it is. The non-obvious part is ordering — an ignore rule cannot re-include a
file whose parent directory is still excluded, so `!bin/**/Reports/**` alone does nothing.

**B5, C1, C3, D1, D2, D3** shipped as written. **C2 and C4 were already closed by M2's pointer work** and
were re-verified rather than re-done.

**A bug the plan had filed for M6 came due early, and the way it surfaced is the point.** C3 made an
unwritable output report itself instead of silently salvaging to `<name>2.ext`. Within one test run that
turned a green suite intermittently red — two different tests across runs, both in `Reports.Merge`, on
`Combined.html`. The cause was not the tests: `MergeableReportRenderer.Render` reduced the caller's path
to its file name, wrote under `<BaseDirectory>/Reports`, and copied the result to the destination. The
copy meant the requested file did appear, so nothing ever looked wrong, while every merge also left the
report in the tool's own install directory and two merges to different destinations raced over one
intermediate name. Fixed here rather than deferred, because the alternative was a red suite or restoring
the silence that hid it. **This is the clearest evidence so far for §17.0's class:** a defect that no
amount of reading the merge code had surfaced, found by making a different subsystem honest.

**A discrepancy in the plan itself.** The acceptance table (§ at the "Slice / Red first / Guards" heading)
is **offset by one** from the milestone table: its `M2` row holds the console and pointer tests
(`Pointer_collapses_CR_LF_in_every_run_derived_string`, `Pointer_quotes_a_path_containing_a_space`,
`Pointer_names_only_outputs_that_reached_disk`, `Tool_stdout_round_trips_through_strict_UTF8`), which are
M3's content, and its `M3` row holds the schema tests, which are M4's. All four of the first set were
landed during M2 and are green. Read the acceptance table one row up from the milestone it names.

**Verification:** 4,529 unit / 0 failed, run twice (from 4,498 at the end of M2); 49 IKVM / 0 failed;
solution builds clean. The `~Reports.Merge` filter, which reproduced the flake 3 times out of 3, is green
3 times out of 3. Mutation-proved: disabling the CI debug block reddens its stdout+summary fact; an
internal `Build(string, Feature[])` reddens the rewritten injection guard; renaming one verb in
`agent-instructions.md` reddens the new emitted-instructions drift guard.

---

## 17. Verification log

*(One dated entry per verification pass. Each records: the rows attempted, the exact commands, the
verdicts, what changed in the plan body as a result, and — most importantly — **what was tried and did
not settle a row**, so the next pass does not repeat it. Harness paths go here too.)*

### 2026-09-12 — initial pass
- **Harness:** 18 read-only survey lanes + 108 adversarial verifications + 3 critics, run as a
  workflow. Per-agent transcripts under
  `…/subagents/workflows/wf_60266443-e01/`; consolidated digest at
  `…/scratchpad/digest.json`; per-lane extracts at `…/scratchpad/verified_{A,B,C}.md`.
- **Attempted:** all of §14.1b. **Promoted to `RUN`: 107 of 108.**
- **Reversed:** six (§14.0). **Narrowed:** 41.
- **Did not settle, and why:** C75 (no live CI runner reachable); C76 (never measured, needs a build);
  C77/C78 (no non-Claude agent available); C79/C80 (no submission attempted); C44–C47 (no real
  mergeable shard exists on disk); the digest's populated-`calls` path (no failing-with-steps fixture
  exists). Every one of these is in §14.3 with its experiment.
- **Blocked by the constraint, not by difficulty:** the tree was live with M2.8 in flight, so no lane
  could build, run `dotnet test`, run Playwright, or generate a report with modified options. That
  single constraint is what holds C74, C76 and the whole of §14.3 item 3 below `RUN`.
- **Process defect, recorded because it is §14.0 row 7.** The three critics died on a session limit and
  the run was resumed. The resume did **not** replay from cache — it re-ran the whole 108-agent Verify
  phase live, and the critics were still an hour away. This was misread as "the critics are running",
  from `started` records in a journal shared with the previous run. Caught by the user, not by the
  process. The redundant re-run was stopped and the critics relaunched standalone against the saved
  digest, into a fresh transcript directory. **Cost: roughly one hour and a full duplicate verification
  pass.** Standing rule added as §13 item 9.
- **Collateral from the workflow, audited afterwards.** One stray 128-byte file (`mtp437.txt`, a
  redirected shell error) was written into the repo root at 15:14 by a survey agent, in breach of the
  read-only constraint; removed. Nothing else: the repo's own `CLAUDE.md` (94 lines, one
  `kronikol:begin` block) and `AGENTS.md` are intact with mtimes predating the run — which matters,
  because D1 is a bug that destroys exactly those two files and several agents were exercising it.
  No other untracked file, and no tracked file, was touched by any agent.
- **The target moved mid-audit and part of the plan went stale within the hour.** M2.9 landed; see the
  staleness note at the top, I1, §7.1 and rows C81–C82. Two claims were corrected **by measurement
  against the new build**, not by re-reading: one survived and one got worse.

### 2026-09-12 (later) — the critic pass
- **Harness:** three critics over the 108 verified rows plus the written plan, run standalone after the
  first attempt died on a session limit. Transcripts under `…/subagents/workflows/wf_23bfc297-5a3/`;
  extracts at `…/scratchpad/critic_{completeness,sequencing,error-class}.md`. 41 findings, 628K tokens,
  22 minutes.
- **Adopted wholesale:** §5 (the sequencing critic's, replacing the author's module-ordered table) and
  §14.3 (the error-class critic's).
- **Reversed three of the plan's own conclusions** — the root-`CLAUDE.md` narrowing, "the nested load
  fires", and "the calls mechanism is alive". All three are in §14.0's third table. Base rate moved to
  0 of 4.
- **Rule the author missed, caught by the sequencing critic:** flipping `WriteCiSummary` or
  `PublishCiArtifacts` to true **cannot ship in 3.x** — the repo's own versioning rule reserves a
  changed default for v4. B1 is reframed as a **new** option, default on, which is a minor bump and has
  three precedents in this very diff.
- **Did not settle, and why:** every row in §14.3. Three need a live GitHub Actions run, two need a
  non-Claude agent, two need a real sharded run or a failing-with-steps fixture, and one needs the
  `claude` binary, which the critic could not find on PATH — so §6's M8 gate is currently specified
  against an unavailable tool.

### 2026-09-12 (later still) — collateral sweep, and four subjects that moved after their RUNs
- **Collateral: clean.** A background sweep of every repo file modified during the workflow window
  (`find . -mmin -100`, bin/obj excluded) returned 31 paths, all of them either the other session's
  in-flight M2.8/M2.9 implementation or this plan. The audit's one breach of its own read-only
  constraint, a 128-byte `mtp437.txt` in the repo root, was removed when it was found. Nothing else
  from the 108 verifications reached the tree.
- **But the sweep is also a staleness report, and this is the first real test of §18's premise.** Four
  subjects were edited by the other session *after* the RUNs that verified claims about them:

  | Subject, and its mtime | Rows that rest on it |
  |---|---|
  | `src/Kronikol/Reports/agent-instructions.md` 16:52 | the generated reports-directory pointer — M1.3's channel, so B4 / C81 / C82 |
  | `.claude/skills/…/SKILL.md` + `references/commands.md` 16:33–34, `templates/` copies 16:33 | the skill rows, including `query.py` crashing on Windows |
  | `Query/QueryWriter.cs` 16:29, `Query/ReportScanner.cs` 16:46, `QueryCommand.*.cs` 16:08–16:38 | the `--json` envelope and the `next` footer — G3, G10, C81–C82 |
  | `tests/…/QueryJsonTests.cs` 17:28, `QueryCommandTests.cs` 17:16 | still moving 40 minutes after the audit's last RUN |

- **What this does and does not license.** An mtime says the subject moved; it says nothing about
  *what* changed or whether the finding is now wrong — concluding "the file changed, therefore the row
  is refuted" is §14.0's error class wearing the opposite coat. These rows are **UNVERIFIED at the next
  re-sync, not refuted**, and they are where iteration 1 should start: each one is cheap to re-run and
  each decides something. Recorded here so the loop does not have to rediscover it.
- **And it settles the question §18 used to get wrong.** The tree moved under the audit *and kept
  moving after it*; a version of this plan that waited for quiet would still be waiting, with its
  highest-`Decides` rows ageing the whole time.

### 2026-09-12 (loop iteration 1) — the host-channel iteration

**Re-sync.** `git status --short` → **196 entries, dirty** (another session is mid-M2.10/M3), so every
row needing `dotnet build`/`dotnet test` stays **BLOCKED(tree)** and was not retried. `git log
--oneline -5` unchanged, HEAD `34388cb`. **The binaries moved twice during the iteration**:
`src/Kronikol/bin/Debug/net10.0/Kronikol.dll` 16:57:38 at re-sync, **18:01:45** by mid-iteration;
`Kronikol.Tool.dll` **18:01:49**. The ledger's only explicitly stamped row (C81, "binary 16:46") is
therefore stale by the §14.0 rule. **Recorded as a finding about the ledger rather than silently
fixed: only one of 82 pre-existing rows carries a provenance stamp at all**, so the rule introduced in
the last pass has never been retro-applied and "drops back to UNVERIFIED at the next re-sync" cannot
actually fire for 81 of them. Either stamp them or stop claiming the mechanism works.

**New surface that landed mid-iteration**, needing its own claims next time: three Component fixtures
regenerated 17:04–17:05 (xUnit3, LightBDD.xUnit3, ReqNRoll.xUnit3), and — observed live at ~18:05 — a
generated `Reports/CLAUDE.md` whose report filename appeared **hash-suffixed**
(`TestRunReport_a94ef381.json` / `.html`, interpolated into the prose *and* into the `#sid-` row of the
address table). **~~New surface~~ — REFUTED in iteration 3 (C101): that was a unit-test artifact, not a
product change.** The directory is `tests/Kronikol.Tests/…/Reports/`, the suite's own scratch space;
the product still writes plain `TestRunReport.json`. Left in place rather than deleted, because it is
the error class committed *inside* the loop built to catch it — see §13 hazard 13.

**Rows attempted, and the commands.**

| Row | Verdict | Instrument |
|---|---|---|
| §14.3 item 7 → **C86, C87, C88** | **SETTLED — reverses the critic** | `python` over this session's own transcript `~/.claude/projects/c--Code-Kronikol/b2cd976d-….jsonl`, record 15 |
| §14.3 item 8 → **C91** | **RE-CONFIRMED; cause unpromotable in-session** | four `Read` calls, alternating drive-letter case, four distinct reports directories |
| §14.3 item 9 → **C90** | **SHARPENED — the defect is a layer deeper** | `scratchpad/census_calls2.py`, 37 reports / 1,349 scenarios / 4,944 steps, with a positive control |
| §14.3 item 11 → **C93, C94** | **SETTLED without gate 6** | subagent: `gh run view --log` over two existing public runs |
| §14.3 item 13 → **C92** | **SETTLED, and NARROWED** | subagent: `github-test-reporter` @ `7974087` (1.0.29), 24 stock flags + positive control |
| **C89** (new, unlooked-for) | **RUN** | the same transcript record — `files[]` has exactly two entries |

**What changed in the body.** §14.0 gained a **fourth reversal table** (the critic's own EC1 reversed:
*auto-loaded* is true, measured) and the base rate moved to **0 correct of 5**. B4's row in §4.2 no
longer says "original severity restored" — the severity **splits**: the channel works on a dev box for
the *next* session and is absent in CI. §5.5's B1 reframe and §12 Q5 now name **stdout** as the
run-end channel with the step summary as an addition, because C93 measured that the summary is not the
log. §14.2 gained five rows and lost none. §13 gained hazards **10** (a census with no positive
control is not a measurement) and **11** (a `U+FFFD` at the console is usually the console).

**The near-miss, recorded because it is hazard 10 and it nearly shipped.** The first census reported
**zero failed scenarios** across a corpus that contains a fixture with fifteen — it read
`status`/`interactions` where the emitted schema says `result`/`httpInteractions`. The number was
plausible **and it agreed with the previous pass's published conclusion**, which is precisely why it
was nearly reported. It was caught only because the script was made to print CiPreview.Mixed's count
first. The corrected census then produced a *stronger* result than the one it nearly faked.

**What did NOT settle a row, so the next iteration does not repeat it.**
- **No agent CLI but this one exists on PATH** — `claude`, `codex`, `cursor`, `amp`, `aider` all absent
  (`command -v` each). So **C77, C78, C79, C80 and §14.3 items 5 and 6 are unreachable from any session
  on this machine**, not merely unrun. `gh`, `node`, `npm`, `rg`, `dotnet`, `python` are present.
- **The rendered step summary cannot be retrieved.** Check-run `output.summary` is empty for all 17
  jobs of the probe run; the run page loads it client-side; seven `summary_partial`-shaped endpoints
  404. C93 rests on the negative side of the falsifier plus a source read — enough for the claim as
  stated, not enough for any claim *about the rendered summary's contents*.
- **`github-test-reporter` is not on npm** (E404); `github-actions-ctrf@0.0.58` is a different, older
  package. There is no `v1.0.29` tag — the floating `v1` carries it. Clone and use the checked-in
  `dist/index.js`. It needs `GITHUB_OUTPUT`/`GITHUB_ENV`/`GITHUB_PATH` to pre-exist or it hard-fails
  *after* writing the summary; a zero from that state is a harness artifact.
- **The path-case *cause* cannot be discriminated from here.** "Matches the cwd" and "is lowercase"
  predict the same 2×2 in a session whose cwd is already lowercase, and a session cannot restart itself
  into the other case.
- Two incidental defects in `github-test-reporter`, noted for whoever writes M7 and **not** acted on:
  `pull-request-report` on a `push` event zeroes the **entire** step summary (6,845 bytes → 0), and
  `src/integrations/ai.ts:233` does `report.results.extra = report.extra || {}`, destroying producer
  `results.extra` whenever the AI path runs.

**Harness and scratch paths** (all under
`…2cd976d-ef14-4c70-8140-e75274c986ca\scratchpad\`): `census_calls.py` (the broken first version,
kept deliberately as the hazard-10 exhibit), `census_calls2.py` (the corrected one),
`c86-ctrf-extra\` (clone, fixtures, `run.sh`/`run-ai.sh`/`sweep.sh`, 32 output dirs),
`joblog.txt`, `grafana_joblog.txt`, `artifact_dl\`, and the probe HTML/JSON captures.
**`git status --short` re-run at end of iteration: 198 entries, up from 196 at re-sync.** The delta is
**not** mine and the check is what caught it: the two additions are the other session's, and every
untracked entry is a plan file, a `src/`/`tests/` source file or a `tools/render-bench/` artifact that
predates this iteration. Nothing this iteration created appears in the repo — all harness and output
paths are under the session scratchpad. *(Noted because the first draft of this very line said
"196, identical", which was written from the re-sync reading rather than from the re-run. On a live
tree the end-of-iteration check is the measurement; copying the opening number forward is hazard 1
wearing a different hat.)*

### 2026-09-12 (loop iteration 2) — the build window opened, and it was taken

**Re-sync, and the target moved hard.** `git status --short` → **104 entries, down from 198**, and
`git log` shows a **new commit**: `e91cffb` *"Make Kronikol a tool an agent can debug with … 
(LLM_FRIENDLY_PLAN M0–M2.9)"*. The other session committed the entire uncommitted diff this ledger had
been reasoning about. `Kronikol.dll` **18:18:25**, `Kronikol.Tool.dll` 18:04:09. Only **two** tracked
files remained modified: `tests/Kronikol.Tests.Otlp/OtlpSpanMapperTests.cs` (their M2.10, in flight)
and `.claude/scheduled_tasks.lock` — **which is mine**, written by this loop's own `ScheduleWakeup`.
Recorded rather than glossed: the constraint says to confirm `git status` lists nothing I created, and
that lock file is the one thing that does. It is harness state, not content, and I did not author it
by hand — but it is not the other session's either.

**The window, and how it was taken without interference.** With one source file in flight, a
solution-wide build was still off the table, so every run used **`--no-build`** against the 18:18:25
binaries. Nothing was compiled, nothing was written to the repo beyond the gitignored
`bin/Debug/net10.0/Reports/` of one example project, which the other session regenerates constantly
anyway. **This is the pattern for future iterations: `--no-build` converts "BLOCKED(tree)" into
"reachable" for every row that needs a *run* rather than a *compile*.** Four of the five rows parked
BLOCKED(tree) at iteration 1 were compile-free and should never have been parked as a group.

**Rows attempted, and the commands.**

| Row | Verdict | Instrument |
|---|---|---|
| §14.3 item 2 → **C95, C97** | **SETTLED** for stdout; C76 reclassified **unmeasurable as shipped** | `dotnet test … --no-build` at three verbosities, **A/B against the same assembly run directly** |
| §14.3 item 8 product half → **C96** | **SETTLED**, and it confirms the bad case | the direct-run stdout, verbatim |
| B4 / C10 / C81 tracking → **C98, C99** | **REVERSED AGAIN** — now tracked | `git show HEAD:CLAUDE.md \| grep -c kronikol:begin` → `1`; `git ls-files templates/agents/ .claude/skills/` |
| §14.3 item 14 restatement | **already done** in §0.1 — no action needed | read |

**The A/B that made item 2 worth doing.** The prior table said "swallowed"; nobody had shown the line
was *emitted*. Absence under `dotnet test` is equally consistent with the option being off, and that
confound would have survived any number of re-runs of the swallowed arm alone. Running the **same**
`Example.Api.Tests.Component.xUnit3.exe` directly produced the pointer at 4,236 bytes; `dotnet test`
produced 376 / 3,016 / 3,035 bytes without it; the report was restamped 18:19 in both arms. **That is
the difference between "we did not see it" and "it is emitted and swallowed"**, and only the second
supports B1.

**The finding that closes a loop inside the plan.** The pointer prints
`C:\Code\Kronikol\…\Reports` — **uppercase**. C91 measured that an uppercase path does **not** fire
the nested-`CLAUDE.md` injection from a lowercase cwd. So **the product's own output directs the agent
to the one spelling that defeats the mechanism M1.3 is built on.** Both halves are now measured, four
hours apart, by different instruments. The fix is specified and is one line — **print the reports
directory relative to the cwd**, which is correct under either hypothesis for the cause and is shorter
output besides.

**What changed in the body.** §14.0 gained a **fifth reversal** (B4's tracking predicate, flipped twice
in one day) and, with it, hazard **12** — a claim about repository state has a half-life and must carry
its clock. The tallies are now explicitly separated: **six reversals, five errors**, because the B4
flip was a correct measurement overtaken by events, not a reasoning failure, and counting it would hide
the actual lesson. C10 is struck through and marked closed. §14.4 item 1 no longer calls C76
"unmeasured, cheap" — it is unmeasurable until someone writes to `Console.Error` on purpose.

**What did NOT settle a row.**
- **The "every VSTest runner" quantifier is still the prior session's.** One runner, one adapter
  (xUnit3) was re-run. NUnit, MSTest, MTP/TUnit, LightBDD, ReqNRoll and BDDfy were not.
- **The path-case *cause* is still undiscriminable** and no session started in this cwd can settle it
  (§14.3 item 8). C96 makes it matter less: the relative-path fix is correct either way.
- **C76 needs a source change**, which this loop may not make.
- **§14.3 item 9 remains genuinely blocked** — it needs a failing-with-steps *fixture*, i.e. a new test
  project, which is a write outside `LLM_FIRST_PLAN.md`. `--no-build` does not help; this one is
  people-work or a scope change.

**Scratch paths** (all under `…2cd976d-…\scratchpad\`): `c74_default.txt`, `c74_normal.out`,
`c74_normal.err`, `c74_detailed.out`, `c74_direct.out`, `c74_direct.err`, plus iteration 1's
`census_calls*.py`, `c86-ctrf-extra\`, `joblog.txt`, `grafana_joblog.txt`, `artifact_dl\`.

### 2026-09-12 (loop iteration 3) — closing rows rather than opening them

**Re-sync.** `git status --short` → **112 entries, 10 of them tracked-and-modified**; the other session
is finishing M2.10 (`OtlpExportOptions/Sink/SpanMapper`, `ExportCommand`, two test files) plus
`CHANGELOG.md`. HEAD still `e91cffb`. `Kronikol.dll` **18:18:25 — unchanged since iteration 2**, so
C95/C96/C97 keep their stamps and do not drop back. `Kronikol.Tool.dll` moved **18:04:09 → 18:23:58**,
which staleness-drops the two rows stamped against a Tool binary, and both were re-run.

**LLM_FRIENDLY_PLAN's target moved again:** its Remaining block now reads **"All of M0, M1 and M2"** —
M2.10 is done. Only M3.1 (MCP), M3.2 (CTRF) and M3.3 (`Specifications.md`) are left, and the block now
carries M3.1's own judgement, reinforced by this plan's §5.5, that a local stdio MCP wrapper adds
little to an agent that already has a shell.

**Rows attempted.**

| Row | Verdict | Instrument |
|---|---|---|
| **C81** | re-confirmed on the new binary | `query summary ./nope.json --json` → rc=2, **stdout empty**, stderr bare prose. Still no envelope on the error path |
| **C82** | re-confirmed, still the worse outcome | the schema file + `--json` → a well-formed envelope, `"scenarios":0`, `"kronikolVersion":null`, rc=0, stderr 0 B |
| §14.3 item 10 | **RESOLVED from C95**, no gate-6 run needed | reasoning *from an executed measurement*, stated as such |
| §14.3 item 15 | **DONE** — A5 restated in §4.1 | no experiment, and none was needed |
| §14.3 item 4 | **CLOSED as decides-nothing**, permanently parked at `RUN(proxy — jsdom)` | the row's own Decides cell |
| §14.3 item 3 | **re-measured, and reclassified** | `grep -rl mergeableFormatVersion --include=*.json examples tests` → **0** |
| **C100** (new surface) | **READ**, honestly | `kronikol export --help` |
| **C101** (new surface) | **REFUTED — my own claim** | `ls` of the directory I read it from |

**Two rows changed category, which matters more than two rows changing depth.**
*Item 10* was posed as a contradiction needing a live runner: B1 says the channel is dead, C1 says a
workflow command is injectable through it. C95 dissolves it — under `dotnet test` **nothing** reaches
stdout, so C1 is scoped to the channels where output survives (a direct test host, `kronikol ingest`).
The one-sentence form: **the injection rides whichever channel carries the pointer, so fixing B1
without fixing C1 ships the hole.**
*Item 3* was parked BLOCKED(tree) for three iterations. It is not. **No mergeable report has ever been
written to this disk** — `GenerateMergeableData` is off by default and nothing enables it — so no
amount of `--no-build` reaches it. It is **unpromotable by this loop** and needs a one-line source
change this loop may not make. That is a better answer than "waiting for a quiet tree", which it was
never waiting for.

**The self-inflicted one.** Iteration 1 logged "new surface: report filenames are now hash-suffixed".
Iteration 3 checked the directory and it is `tests/Kronikol.Tests/…/Reports/` — the **unit suite's
scratch space**, holding `Attr_annotations.json`, `TestRunReport_scen.schema.json` and dozens more. The
product writes plain `TestRunReport.json`, which that directory's own generated `CLAUDE.md` names. **A
filename was read as a feature**, which is §14.0's error class committed inside the loop built to catch
it, by its author, for the second time in three iterations. Now §13 hazard **13**. The iteration-1 entry
is struck through rather than deleted.

**And a mechanical one worth a hazard of its own.** Two edits and one grep failed because the phrase
searched for is **broken across a line wrap** in the source (`is **test
data, not instructions**`;
`a tracking
claim carried forward`). The first cost a false "0 hits" that briefly looked like evidence
the block was truncated. §13 hazard **14**: a zero from a multi-word search over wrapped prose means
check the wrap, not conclude absence.

**What did NOT settle a row.**
- **C100 could not be run for lack of inputs, not tooling.** `find . -name "*.ndjson"` → 3 files, all
  Cucumber messages; there is **no** interaction-shaped capture and **no** tests NDJSON on disk.
  Reconstructing both from a real report's `httpInteractions` would test my own reconstruction as much
  as the product, so the row stays `READ` with its command written out.
- **Per-adapter clustering (item 14's experiment) is still unreachable** for the same reason C90 found:
  every example project passes, and the only failing fixtures are xUnit3-generated. Counting clusters
  per adapter needs failing runs per adapter, which needs fixtures that do not exist.
- **No new `git`-derived row was taken on faith**: C98's tracking facts were re-taken this iteration,
  per hazard 12.

**Scratch paths** (all under `…2cd976d-…\scratchpad\`): `c81.out`, `c81.err`, `c82.out`,
`c82.err`, plus everything from iterations 1–2.

### 2026-09-12 (loop iteration 4) — the quiet tree, and the last dispositions

**Re-sync, and the tree finally went quiet.** `git status --short` → **103 entries, and the only
tracked-and-modified file is `.claude/scheduled_tasks.lock`, which is this loop's own.** M2.10 landed
as commit `06c83a6`. `Kronikol.dll` **18:18:25** and `Kronikol.Tool.dll` **18:23:58**, both unchanged
since iteration 3, so no row dropped back.

**One thing to know about this document's own provenance:** commit `06c83a6` swept
**`LLM_FIRST_PLAN.md` (+288 lines)** and `LLM_FRIENDLY_PLAN.md` into the other session's M2.10 commit.
Iterations 1–3 of this loop are therefore committed under a message about OTLP export. The content is
intact and `git status` shows the working copy clean against HEAD; recorded only so nobody later reads
the commit graph and concludes the ledger work happened during M2.10.

**What the quiet tree was actually worth — and it was not what the prompt predicted.** The loop's
standing instruction treats a quiet tree as the scarce resource that unblocks §14.3 items 2, 3, 4 and
9. Measured: **item 2 was already settled by `--no-build` on a dirty tree (iteration 2); items 3, 4 and
9 did not become reachable when the tree went quiet, because none of them was ever waiting on it.**
Item 3 needs `GenerateMergeableData = true` somewhere, item 9 needs a failing-with-steps project, item
14 needs six of them — all **source changes**, which no amount of quiet provides. Item 4 was closed as
deciding nothing. The only row the quiet tree genuinely unlocked was **item 16**, and it turned out to
be unrunnable for an unrelated reason. **The "wait for a quiet tree" heuristic was wrong for four of
the five rows it was written for.**

**Rows attempted.**

| Row | Verdict |
|---|---|
| §14.3 item 16 → **C102** | **DISPOSED**: 42/45 confirmed by census; the diff is unrunnable, and my first attempt refuted itself |
| §14.3 item 9 | **RECLASSIFIED** from BLOCKED(tree) to unpromotable-by-this-loop |
| §14.3 item 12 | **CLOSED as a rule, and audited** — 1 of 82 pre-loop rows ever carried a stamp |
| §14.3 item 14 | restatement already done; experiment **unpromotable** (no failing project per adapter exists) |
| §14.3 items 1, 5, 6, 17 | **explicit unpromotable / disposed markings written into the items** |
| §14.1b C74–C80, C100 | **disposition markers written into the cells**, so the ledger reads its own status |

**The near-miss, and it is the third of this loop.** Counting the port's parity goldens with a literal
`grep '<details class="scenario"'` returned **11 of 45** — a clean refutation of C71's 42/45, ready to
write up. The generator emits other attributes before `class`; the tolerant
`<details[^>]*class="[^"]*scenario` returns **42**. **C71 was right and my regex was the finding.** Now
§13 hazard **15**: markup is not a string, and the tolerant form is the control you run *before*
reporting the strict one. A second control on the same row failed usefully: the .NET unit-test outputs
are **not** the port's parity fixtures — `<title>Test</title>` against `<title>Kronikol Run</title>`,
460 KB against 276 KB — so the matched-pair diff item 16 asks for does not exist to be run, and diffing
the pair anyway would have reported two different reports as version drift.

**What did NOT settle a row.**
- **There is no parity-fixture regeneration path in either repo.** `grep -rln parity` over the .NET
  repo's `*.csproj`/`*.ps1`/`*.sh` → nothing; the port has no script either. Don't go looking again.
- **Three §14.3 items are source-change-blocked, not tree-blocked** (3, 9, 14). Each now names the
  one-line change that clears it. This is the category the earlier iterations kept mislabelling.
- **Four items are people-work and always were** (1, 5, 6, and the cause-half of 8): a live Actions
  run, a non-Claude agent session, a marketplace submission plus `claude plugin eval`, and one session
  started with an uppercase cwd. None becomes reachable by waiting.

**One row settled after the dispositions, because it became reachable the moment the tree went quiet
— §14.4 item 3, and it is the best news in four iterations (C103).** The question was whether a line
injected into the first failure's framework message survives the runners. Half of it is now measured,
with the cleanest control this loop has produced: in **one** `dotnet test --no-build` run of
`CiPreview.AllFailing`, full `Error Message:` bodies and stack traces with `file:line` reach stdout —
33 of them — while `grep -c "reports written"` over that same output returns **0**. One process, one
stdout, one channel alive and one dead. **This is the only channel measured to reach a VSTest log**,
which makes it the right thing for B3/M3 to design against rather than the step summary (C93) or
library stdout (C95). The unsettled half is whether Kronikol can *write* to it: the adapter's
`FailureCause` prefix lands on the **captured** `ErrorMessage`, and no `FailureCause` string appears in
the runner's output, so the existing injection proves nothing about runner-side reach. That falsifier
needs a source change and is now the cheapest unbuilt thing in M3.

**Scratch paths**: `allfailing.out` added this iteration; iterations 1–3 artefacts remain under
`…2cd976d-…\scratchpad\`.

### 2026-09-12 (loop iteration 5) — restarted, because stopping at the §14 boundary was the wrong boundary

**Why there is an iteration 5.** Iteration 4 stopped on the literal STOP condition: every §14 row RUN,
unpromotable or disposed. The user pointed out that **M3 work is only beginning**, and they are right —
step 1 of this loop's own instructions says new surface gets new numbered claims *so the target moves*,
and §18 exists because "a single pass is correct about a tree that no longer exists by the time it is
read". **The completeness pass was complete only for the surface that existed at 18:45.** Stopping as
M3 starts is precisely the failure mode the loop was built to avoid: settled before M3 these are
design, settled after they are rework.

**Re-sync.** 104 entries, and the only tracked-and-modified files are this plan and this loop's own
lock. HEAD still `06c83a6`; binaries unchanged at 18:18:25 / 18:23:58, so nothing dropped back.
**M3 has landed no surface yet** — no MCP project, no CTRF writer, no `Specifications.md` generator —
so this iteration pre-positions the decisions M3 needs rather than auditing what it built.

**Rows moved.**

| Row | Verdict |
|---|---|
| **C100** | **READ → RUN(proxy)** — and better than expected |
| **C104** (new) | the spec documents are **deliberately** blank on failure; not a bug |
| **C105** (new) | `Specifications.yml` **drops scenario-outline examples**; controlled against the unexercised-path confound |
| M3.2's decisive question | **in flight** — a subagent is measuring whether any *standard* CTRF field renders in a stock summary |

**C100 promoted, and the reason is the ledger's own corollary.** M2.10's two tests do not check a
predicate — they round-trip: `OtlpTraceReader.ReadJson(File.ReadAllBytes(outFile))` decodes the emitted
OTLP document and reads the attribute off the span, with the A/B built in (`"Failed"` when `--tests` is
given, **null** when it is not) and a `[Theory]` mapping four runner status words through the same
`FeatureSynthesizer.MapStatus` the ingest uses. `dotnet test … --filter
"FullyQualifiedName~ExportCommandTests" --no-build` → **18 passed**. Honest scope: inputs are
test-authored, so `RUN(proxy)` on the inputs and `RUN` on the emitted bytes. What stopped it being a
full RUN is recorded so nobody retries it blindly — the capture NDJSON needs a **test-id join key that
the report's `httpInteraction` shape does not carry**, so reconstructing inputs from a real report
would have measured my reconstruction.

**The two M3.3 findings, and one of them is a near-miss avoided.** `Specifications.yml` is **0 bytes**
on both failing fixtures while those reports carry 4 features / 11 scenarios — which looks exactly like
a defect. It is not: `ReportGenerator.cs:274,284` passes `generateBlankOnFailedTests: true` to both the
HTML and the data writer. **Deliberate, named, and pinned** — and it cross-confirms C102, because
`report-blankonfail.html` is one of the two parity goldens the census found with no `<details>` tag at
all. Filing that as a bug would have been the error class again; checking the source took one grep.
The real finding is C105: the spec YAML **drops examples blocks**. Controlled properly — same run, same
directory, yml populated at 3,826 B, JSON carrying `a classic bake` and `speciality flours`, **neither
in the yml, and no `Examples:` key**. And the control that matters most: **`rule` (0 of 1,349) and
`categories` (0) are unexercised corpus-wide**, so no claim may be made about them — that is this
repo's recurring class (a path gated on input no corpus exercises), not a finding.

**What did NOT settle a row.**
- **M3.1 needs no new measurement.** The plan's §5.5 and C62 already answer it: a local stdio MCP
  wrapper adds little to an agent that already has a shell, and the registry shelf is occupied.
  `LLM_FRIENDLY_PLAN` now carries the same judgement. Re-probing it would consume an iteration to
  confirm a decision already made in both documents.
- **The `examplesBlockName` omission is not yet traced to a cause** — it is measured at the output, not
  in `GenerateSpecificationsYaml`. Whether the writer never reads the field or reads and discards it is
  unmeasured, and it changes the size of M3.3's fix.

**The M3.2 question came back, and it changes the milestone (C106–C108).** C92 had established that a
producer-set `extra` renders at **zero** across 24 stock flags. The question nobody had asked was
whether a *standard* field renders — and one does: **`results.environment.buildUrl` comes out of stock
`github-report` and `previous-results-report` as a real clickable markdown link with the `#sid-`
fragment intact**, with no template and no consumer wiring, and the gate is effectively unconditional
(dropping `buildName` and `buildNumber` each still rendered it). Verbatim:
`[#9471](https://example.invalid/r.html#sid-ZZFIELD-buildUrl-9471)`.
**Three qualifications keep it honest, and each is measured.** It is **one run-level link, not a
per-scenario one** — no standard field renders a clickable per-test URL, `message` is unlinked inside a
raw `<td>`, `trace` only inside `<pre><code>`, and `test-list-report`'s `escapeMarkdown` corrupts URLs
outright (C107). `buildUrl` **conventionally means the CI run URL and is back-filled by the action**, so
writing it is an override, not an addition (C108). And all of it measures **the bytes the action
writes**, offline — whether GitHub's own sanitiser preserves the fragment or permits `<a href>` inside
that `<td>` is unmeasured, with no network available.
**Net effect on the plan: C92's "you must ship a Handlebars template" survives for `#sid-` deep links
and falls for the single report-level link.** M3.2 is now a deliberate trade rather than an open
question, which is exactly what settling it *before* the slice was supposed to buy.
Two incidental defects in `github-test-reporter`, recorded and not acted on: `formatTestPath` is
registered twice and the second registration (`if (!suite \|\| typeof suite !== "string") return name`)
always discards the canonical array-form `suite`, which is why `suite` renders in no stock report; and
`pull-request-report` on a push event zeroes the entire summary.

**Scratch paths**: `c100_tests.out` added; the CTRF agent worked under
`…\scratchpad\c86-ctrf-extra\` and will name its own files.

### 2026-09-12 (loop iteration 6) — M3 landed, and eleven rows went stale in nine minutes

**Re-sync, and the target moved further in one gap than in the previous four iterations combined.**
`Kronikol.dll` **19:07:51** and `Kronikol.Tool.dll` **19:07:55**, both newer than every stamp this loop
holds (18:18:25 / 18:23:58) — so **C90, C91, C95, C96, C97, C100, C102, C103, C104, C105 and C81/C82
all drop to UNVERIFIED** by the §14.0 rule. The tree is dirty again with 18 tracked files in flight.
LLM_FRIENDLY_PLAN's Remaining block now reads: **M3.2 and M3.3 shipped, §6's verification protocol has
been run, and only M3.1 is left — deliberately not built.**

**Re-run first, since the binary moved.** C96 re-executed against the 19:07 build (exe restamped
19:21): the pointer still prints `Kronikol: reports written to C:\Code\Kronikol\…\Reports` —
**uppercase, unfixed**. `grep -c "reports written to C:"` → 1, `"…to c:"` → 0. The defect C91+C96
describe survives M3.

**New surface, audited.**

- **C109 — M3.2 shipped and it puts everything Kronikol-specific in `extra`.** Per-test
  `kronikolAddress`, `stableId`, `categories`; run-level `runId`, `runAttempt`; `environment.buildUrl`
  carries `metadata.PipelineUrl`. **`grep -n "reportUrl\|\.html\|sid-"` over the generator returns
  nothing** — no report URL is emitted at all. Against C92 and C107 that means the `sN` address the
  option's own doc says "leads back into `kronikol query`" reaches **machine** consumers reading the
  file and **no rendered consumer whatsoever**, while the doc names rendering consumers first
  ("annotation actions, PR comment bots"). **This is not a bug** — `extra` is the schema's correct
  escape hatch, leaving `buildUrl` to the pipeline URL is exactly what C108 recommends, and Kronikol
  cannot know its own published URL. It is a **scope statement §8's wiki page has to make**, and it is
  the one place where this loop's CTRF measurements have a live consequence.
- **C110 — M3.3 shipped and deliberately inverts C104.** `GenerateSpecificationsMarkdown` is **not**
  blanked on a failed run, where the HTML and data spec are, with the reason given in the source: "a
  reader who reaches for it mid-failure needs the narrative most", and "the same suite run red and run
  green produces the same bytes, which is what makes it safe to commit to a docs site". So
  blank-on-fail is a **per-output policy**, not a global one.
- **C111 — and the implementer settled what I had parked.** The Markdown spec carries `Rule` and
  `Description`, which the data trio drops, because that model flattens every step to a string. **I had
  measured `rule` at 0 of 1,349 scenarios and correctly refused to claim it — then named a falsifier no
  corpus here can satisfy.** The right instrument was the model, not the corpus. §13 hazard **16**.

**A sixth reversal, caught by the implementer and not by this ledger.** The sibling plan's audit
recorded the MCP SDK as unrestorable (711 cached packages, no match). **`ModelContextProtocol` 2.2.0
restores in under two seconds** — the failure was an offline artifact of the session that measured it.
Feasibility was never M3.1's blocker. **This is the third time in this document that an absence
measured in a constrained environment was promoted to an impossibility** (WebSearch's zero results, the
MCP registry's substring search, and now this), which makes it the single most repeated error in the
whole audit. Tallies move to **seven reversals, six errors**.

**Process note: the loop's timer has not fired since iteration 4 — and my first diagnosis of why was
itself the error class.** The 19:07 and 19:16 wakeups did not wake the session; iteration 5 ran because
a subagent notification arrived, iteration 6 because the user asked. **I wrote here that the probable
cause was this loop's own `ScheduleWakeup(stop: true)` at ~18:50**, reasoning from its "any dynamic loop
in this session is ended" message. Then I ran `CronList`, which I had not done: the job **is queued and
pending** (`9c3c31a1`), so it was never killed. The actual constraint is in the scheduler's own
documentation — **jobs fire only while the REPL is idle, not mid-query** — and this session has been
inside a long turn or awaiting the user at every fire moment. **A plausible cause was asserted with an
unrun command sitting one call away**, which is §14.0's rule violated by the document's own author, in
the log entry of the iteration that recorded two other instances of it. Recorded in full rather than
quietly corrected, because the correction is the finding. Consequence for the ledger: freshness is only
as good as the wake mechanism, and across those two gaps the binary moved twice while nothing re-ran.

**Scratch paths**: `c96_rerun.out` added.

### 2026-09-12 (loop iteration 7) — the provenance rule catches its own author, and A1 re-measured

**Why this ran immediately rather than on the 13-minute cron.** The user pointed out that M3 is
complete bar M3.1, so there is no new surface arriving to poll for, and that §14.2 still carries
unverified rows. Both correct: the cron was sized for catching new surface and had become pure latency
on work that was already actionable. Cancelled (`8cde2fcb`), and the iteration run on the spot.

**Re-sync.** Tree **quiet** — 2 tracked files, both this loop's. New commit **`338231b`**: *"CTRF output
and a prose Specifications.md (M3.2 and M3.3), plus the comparer that made `Failures.md` point at the
wrong scenario."* So M3.2/M3.3 are committed **and carry a defect this ledger never held** (C113).

**The finding of this iteration is a correction to my own method, and it invalidates one stamp.**
`--no-build` does **not** run the current product. It runs whatever each project's output directory last
received. Measured at 19:39 with `src/Kronikol/bin/…/Kronikol.dll` at **19:07:51**:

| fixture | its own `Kronikol.dll` |
|---|---|
| `Component.xUnit3` | 19:07:51 |
| `Kronikol.Tests` | 19:07:51 |
| `CiPreview.Mixed` | **13:29:53** |
| `CiPreview.AllFailing` | **10:18:27** |

So **C95, C96 and C100 are correctly stamped** — those projects were current. **C103 was not**: it ran
against a 10:18 Kronikol and was stamped 18:18:25. Corrected in its cell, and its *internal control*
("the pointer is absent from the same output") is **withdrawn**, because a 10:18 build's pointer
behaviour is not the shipped one. The claim survives intact because C95 carries the swallowing half on
a correctly-stamped build, and C103's positive half — xUnit's `Error Message:` blocks reach `dotnet
test` stdout — is a property of the runner, not of Kronikol's version. **This is C85's rule working
exactly as written, against the author who kept writing it down and then stamped the canonical build's
time anyway.** §13 hazard **17**.

**A1 re-measured properly, and it survives M3 untouched (C112).** With the tree quiet, the stale
project was rebuilt (`dotnet build … CiPreview.Mixed`, DLL beside the fixture now **19:40:40**) and the
report regenerated. `Failures.md` (19:40, 4,180 B):

```
### 2026-09-13 (M4) — the schema tells the truth about the file beside it, released as 3.4.0

Six rows: E1, E2, E4, E8 (three commits, already logged), then E6, E5 and the
`additionalProperties: false` guard. Four of the plan's own statements about them were wrong, and the
writing of the acceptance tests is what found it each time.

**E6's headline was not the one the row records.** The row says XML and YAML emit no `diagnostics`, and
that a YAML run ships the camelCase JSON Schema beside PascalCase data. Both hold. But writing
`Yaml_report_validates_against_the_schema_generated_for_Yaml` produced a **parse error**, not a
validation result: `SanitiseForYml` substituted characters rather than escaping them (`[` to `<`, `: `
to ` = `, `{` to `(`, so a captured body `{"item":"Widget"}` was written `("item":"Widget")`) and did
nothing about newlines, so a multi-line assertion message put its second line at column 0 and ended the
block mapping. **Every failing run produces one**, so `TestRunReport.yml` did not parse for exactly the
runs anyone would open it for. The row's "fails on three required root keys" is also stale twice over:
it is four since 78b8311, and once the file parsed the true count was **one error covering four keys** —
because a schema with no `additionalProperties` permits every key it has not heard of, so "none of this
matches" presents as "four keys missing".

**Two same-class defects the row did not name.** `annotations` is dropped by the identical three lines
as `diagnostics`, and an annotation carries text recorded nowhere else in the file. And
`TestRunReportFullStepDetail` is inert for XML and YAML — neither writer was ever handed the flag. The
first is fixed; the second is a format-parity gap of real size (parameter, tree and text-segment shapes
plus XSD types) and is **recorded as its own item rather than bundled**, with the option's own
documentation now stating the limit.

**E5 was narrowed in the wrong direction.** The row confines itself to `merge` on the grounds that
nothing reads `environment` yet. Ingest is the worse lane *and the truth was already in the file*: the
Cucumber Messages `meta` envelope carries `os` and `runtime`, this repository's fixture reports
**node.js 25.9.0 on win32**, and Kronikol's model read the protocol version and the implementation and
stopped — so an ingested report claimed the .NET version of the tool doing the reading, two fields away
from something it was already parsing.

**The `additionalProperties` row needed its slogan corrected.** "everywhere, so the key-walker becomes
redundant" cannot hold: `internalFlowSegments` is re-serialised verbatim through a merge from shard
files another version may have written, so pinning its values would make `kronikol merge` emit a file
that fails its own schema with no code change on either side. The honest wording, and what shipped, is
**`additionalProperties: false` on every node with a fixed key set, with two maps deliberately left
open**. 22 of 22 fixed-key nodes are closed; `exampleValues` and `wholeTestFlow` constrain their values
but not their keys; `internalFlowSegments` is open by design and has a test that says so.

**The proof is on real data, not a fixture.** The CI-preview measuring project was regenerated (15
deliberate failures) and its 234,627-byte `TestRunReport.json` validated against its own 42,130-byte
schema by a conformant 2020-12 validator: **0 errors**. Before E1 the same pairing produced 5,464.

Suite 4,529 → **4,639**, all green; solution builds clean. Released as **3.4.0** — minor, because
`RunEnvironment.Unrecorded`, `MergeableReport.Environment`, `CucumberMeta.Os`/`.Runtime` and an
`environment` parameter on two public methods are new surface. No default moved: a null environment
still means this machine.

**Plan-table correction.** §14's acceptance table and the milestone table are offset by one milestone,
recorded during M3 and confirmed again here — the row labelled M3 in the acceptance table holds M4's
schema tests. Read the milestone table as authoritative.

## Clusters
Failures sharing an error message. Each is worked through once below; the rest are the same failure and need the same fix.
### Assertion — 15 scenarios
## Failures
### 1. Cake Error Diff Feature › Cake batch id should be a specific value
## 14 further failures
```

**One cluster, fifteen distinct failures, one worked example.** The label is `Assertion` — the xUnit v3
`FailureCause` enum name promoted to line 1 — and `ClusterKey` at `FailuresDigestGenerator.cs:292` is
still `FirstLine(errorMessage)`. Commit `338231b` touched the **comparer**, not the key. The same report
through `kronikol query failures` lists all 15 **separately** with at least four visibly different
causes (`Strings differ`, `Values differ`, `to contain "Sugar"`, `StatusCode … but found OK`). §0.1 #1
is confirmed on the current binary, with correct provenance, after M3.

**Re-stamped:** C81 and C82 re-run against `Kronikol.Tool.dll` **19:07:55** — rc=2 with empty stdout and
bare prose on stderr, and a well-formed envelope claiming `scenarios: 0`, `kronikolVersion: null`, rc=0.
Both unchanged.

**§14.2's two data rows were re-examined for reachability and did NOT move.** The A1/A2 work added
failing steps to `FailuresDigestGeneratorTests` (lines 151, 396) and `MergeCommandTests` does enable
`GenerateMergeableData` — but both build `Feature[]`/`Scenario` objects in memory and call the
generator directly. That is precisely C83's objection: it proves the generator handles the shape, not
that any shipped lane produces it. `grep -rl mergeableFormatVersion --include=*.json examples tests` →
still **0**. Both rows stay unverified-for-a-real-run, for the reason already recorded.

**What did NOT settle a row.** `kronikol query failures` does **not** cluster — clustering is a
`Failures.md` concern only. Measuring A1 through the query verb is a dead end; regenerate the report.

**Scratch paths**: `a1_live.out`, `mixed_run.out`, `mixed_run2.out`, `mixed_build.out`, `c81b.out`,
`c81b.err`, `c82b.out`, `c96_rerun.out`.

---

### 2026-09-13 (M5) — every address the tool prints, the tool accepts, released as 3.5.0 and 3.5.1

Rows G1, G2, G4, G5, G6, G7, G8, G9, G10 and §3.1.

**The plan was wrong about three rows, and the verifier lane caught it before anything was built.** G2,
G5 and G6 were marked "verify-don't-rebuild" on the evidence that `--out` writes and the pager computes
a total. Both true; neither closed the row. `--out` was a silent no-op on the fourteen listing verbs;
the pager's footer-guided walk skipped rows because a `--limit` above the ceiling was clamped in
silence while `next:` advanced by the limit asked for; and `--out` into a missing directory was an
unhandled exception on all four payload verbs. A row that says *verify* is still a row to measure.

**Four defects the rows did not name.** (1) The provenance banner corrupted `--count`: it was written
before the verb ran, and `--count` is documented as one token, so every caller parsing stdout got two.
Under `--count` the notes go to stderr. (2) Twelve of the thirteen `DiagnosticKind`s never reached the
banner — only `ResultDefaulted` did. The rule that replaced the list: a kind banners when it means the
report holds less than the run produced, so a count below it is a lower bound and an absence is not a
negative; the two capitalisation kinds are excluded on purpose. (3) Six writes of a caller-supplied
path, two guarded, both too narrowly — `--out ""` threw `ArgumentException`, in no catch clause. One
write path now (`QueryWriter.TryWriteFile`). 3.5.1 followed because that guard *caught* the failure
rather than checking for it, and Windows and POSIX disagree about a whitespace-only path — CI found it
on the 3.5.0 tag, so **no 3.5.0 package was published**, exactly as with 3.4.0. (4) `CiSummary.md` was
a live markdown breakout on captured text — three fixed-width fences and one un-HTML-escaped
`errorMessage`, in a file rendered as HTML in the GitHub job summary.

**Two address-grammar decisions, both load-bearing.** A step path covers that step and everything under
it, everywhere, `--step` included — the addresses `failures` prints are frequently the parent of the
assertion that failed. `sid:` is mandatory: `diff` decides whether its second positional is an address
or a report *file* by asking the grammar, so a bare sixteen-hex form would capture hash-named artifact
paths.

**Three of the suite's own drift guards were green while drifting**, each keyed on the syntax of what it
guarded: the banner scraper keyed on `Note($"! ` and missed a banner written as a `switch` arm; it
enumerated `src/Kronikol.Tool/*.cs` non-recursively and never visited `Query/`; and nothing compared the
two skill trees, so the shipped `query.py` lacked a line the repo's copy grew in 3.1.0. All three key on
the property now, and `The_two_copies_of_the_skill_are_the_same_files` pins the trees file for file.
`scripts/query.py` is a fourth implementation of the address grammar and had reimplemented the
step-path defect on its own.

Suite 4,643 → **4,709**. Minor: `sid:<stableId>` and two `grep` targets are new surface. Behaviour
changes in the changelog: `grep` searches names and errors by default; `--step N` covers N's sub-steps;
`summary <address>`, `annotations <s>/<step>` and `--body <not-an-address>` exit 2 where they exited 0.

### 2026-09-14 (M6) — a merged report is a real run, released as 3.6.0

Rows F1–F5 and B2, over eight commits (`f95ae4b5` … `9e064aec`). **The three worst defects were in
none of the rows, and the rows turned out to be their consequences.**

**Measured on the way in, before any row was touched.** (1) `merge -o <a-shard>.json` overwrote the
shard with half a megabyte of HTML and then printed a message saying the shard had been protected: the
guard compared only the derived `.json` name, ran after the render, and `--no-json` skipped it. F1 as
written ("runs after the HTML is written") was the mild half. (2) `merge` crashed outright — exit 127,
an unhandled `ArgumentException`, from whichever of the artifacts it was, unnamed — on any shard that
captured a database call, because a tracker's `method` is a label and the reader fed it to
`HttpMethod.Parse`. Every real sharded suite that tracks a database was un-mergeable. (3) Two shards
that ran different tests merged into **one** scenario: dedup keyed on the runtime id, and NUnit's is a
per-process counter (`"id": "0-1002"` in this repository's own report). A failing shard merged green.
**The survey's proposed fix — a global seen-set on the runtime id — would have deleted distinct
scenarios run-wide; the verifier lane refuted it before it was written.** F2's "interactions
concatenate" was one consequence of (3); F1's "one label doubles" was the last piece of it.

**What the dedup key had to be.** The scenario's content — what `ScenarioStableId` hashes — plus the
shard's suite, the attempt, the result, the duration and the error message. A copy of a shard is
byte-identical; two runs of one scenario differ, almost always in duration alone. So a shard given
twice (an artifact downloaded into two folders, yesterday's merged file left in the directory, the
merge's own output fed back in) is counted once, shard and all — which is what makes the relationship
sum exact — while an overlapping partition or a failed shard re-run beside its first attempt keeps both
runs, because first-wins merges a failing run green; a retry keeps both attempts. Each case is a
diagnostic. Colliding runtime ids of different scenarios are renumbered `id#n`; `stableId` is untouched.

**B2 was built as a new writer, on the measured obstacle the plan recorded.** `MergedRunOutputs.Write`
lives in the Kronikol assembly (it needs `ScopeReportsDirectory`, which is internal) and takes
everything as a parameter, including the environment, so the GitHub Actions branches are tested without
GitHub Actions. `FinishRun` was not extracted: the run's tail reads process-ambient state a merged
report lacks, and the memoised diagram cache would have regenerated a merged report's diagrams from an
empty log. One prerequisite the plan named held — `FailuresDigestGenerator.Generate` needed a carried
`stepPaths`, or every merged digest entry read `callsScope: "scenario"` — and one it did not:
`kronikol query <dir>` finds only `TestRunReport.json`, so a merge named after `-o` needs the pointer
to hand over the *file* (`RunSummary.QueryTarget`). The merged file validates against the schema
written beside it, which settles that the schema covers the mergeable superset.

**F4 and F5, as the verifier restated them.** Tracking aggregates over matched pairs and states what the
unmatched scenarios carried; a run that captured nothing is the total loss it is, and the only non-loss
absence is a mergeable file written before 3.1.0, told apart by version rather than by count. The
verifier was right that identical scenario *sets* are not sufficient — five calls moving between two
matched scenarios left the totals flat — so a service one scenario stopped seeing while the total held
is reported per scenario with "still N elsewhere", and the reader tells a broken correlation from a
refactor. F5's trigger was wider than the row said: Kronikol4J emits `stableId` without a suite, so a
.NET 3.1.0+ run beside a Kronikol4J run of the same tests shares no id and every name. Both shapes exit
2 before a line is printed, naming the side or the two suites; no positional fallback was built. `new`
rows rendered under "Broken" in the text and have their own section.

**Three of §6's acceptance names exist under other names.**
`Merge_refuses_before_writing_the_html_when_the_output_is_an_input` is `MergeRefusesBeforeItWritesTests`
(five facts); `Merging_the_same_shard_twice_does_not_double_its_interactions` exists as written;
`Merging_preserves_the_runs_environment_not_the_mergers` shipped in M4 as E5;
`Tracking_reports_a_loss_in_matched_scenarios_even_when_a_new_scenario_adds_the_same_calls` is
`RunDiffTests.An_added_scenario_does_not_hide_a_tracking_loss`; "all four M1 files exist beside a
merged report" is `MergeWritesTheRunOutputsTests`. Two existing tests pinned defects and were inverted
(`SourceLocationTests`' merge fixture reused one scenario in both shards, which is the duplicate case;
`BaselineDiffTests` pinned F4's skip), and a flaky test that asserted on the process-global request log
another class clears was fixed along the way.

Suite 4,709 → **4,778**. Minor: `MergedRunOutputs`, two `merge` flags, `RunSummary.QueryTarget`, the
digest's `stepPaths` and the diff's `New` section are new surface. Behaviour changes are called out in
the changelog. §14.2's "a real sharded run round-trips through `merge`" stays unpromotable by this
session for the reason recorded there: every F-row is still `RUN(proxy)` over synthetic shards.

---

### 2026-09-14 (M7) — one question, one command, released as 3.7.0

Rows I2, I3, I4 and the query half of A9, over two commits (`aa1323e0`, `4e61ef9c`).

**I2 — `--describe` off one table.** `VerbTable` (`src/Kronikol.Tool/Query/VerbTable.cs`) is the single
source now: `RunCore` dispatches from it, `QueryOptions` validates per-verb flag legality against it,
`PrintUsage` renders from it, and `--describe` serialises it — each verb with its forms, flags (name,
argument, repeatable, summary), `json` and `pagesWithOffset`; the seven address forms with an example
each; the three exit codes; the envelope's members; `toolVersion` off the informational version
attribute, which is also `kronikol --version`, one line, as §7.2 asked. The four hand-kept verb lists are
gone from the code; the two in `commands.md`/`SKILL.md` remain prose and are held to the table by
`SkillDriftTests`, whose `UsageVerbs()` asserts the help regex equals `VerbTable.Names` instead of a
count. `DescribeTests` holds the document to the tool in both directions: every listed verb dispatches
and every dispatched verb is listed; every flag the parser knows is described and none is invented;
every address example parses to the kind it claims and every `AddressKind` has one; the help lists every
verb once. **One deliberate deviation from §7.2's sketch: no `itemFields[]`.** `interactions` has three
row shapes and `diff` two, so a per-verb field list would be a sixth hand-kept copy of the thing this row
exists to remove; the checkable statement of a row's shape is its projector, and the wiki's
"Verb-specific members" table is the human copy. Found on the way: an unknown verb with no report after
it was answered as a *missing report*; it is refused by name first now.

**I4 — `repro`, built as the tool-side parser C68 argued for.** `FailureText.TestFilter` reads the FQN
out of the frame `ThrownAt` already picks, stripping `(args)`, `` `1 ``, `[T]`,
`<>c`/`<>c__DisplayClassN_M`, `.MoveNext`, and — iterated innermost-first — `<Method>b__0`,
`<Method>d__3`, `<Method>g__Local|0_0`, so an async lambda's `<<Method>b__0>d` unwraps too.
`RerunCommand` emits `dotnet test --filter "FullyQualifiedName~Ns.Class.Method"`; `~` because NUnit's
FQN carries a parameterised test's arguments. The same reader feeds `repro`, `failures` (text
`thrown at`/`rerun:`, JSON `thrownAt`/`testName`/`rerun`), `Failures.md` (`Re-run:` after `Thrown at`)
and `Failures.jsonl`, which closes the `thrownAt` half of the digest/query divergence `FailureRecord`'s
docstring recorded in M2. Two limits are stated in every doc rather than papered over: a nested class
prints `.` where the runner's own name has `+` (the frame cannot say which segments were classes), and
the line is the VSTest grammar — a runner on Microsoft.Testing.Platform's own `dotnet test` takes the
same name through its own flag. Not added to `scripts/query.py`, which is already a fourth
address-grammar implementation; it stays the digest-only fallback.

**I3 — closed by resolution, not by changing addresses.** The inconsistency was real (`values` prints the
response ordinal, listings fold the pair onto the request's), but `http` has named the counterpart of
either half since M5 (`response s1/i1` / `answers s1/i0`). `ReproTests` holds the round trip: `http` on
each half names the other; the address `values` prints, handed back to `http --path` with the same path,
yields the value; the `b:` hash a listing row carries resolves to the response. Changing what listings
print would have moved every documented example and reached no body that was unreachable.

**A9's query half.** `ReportScanner` indexes `errorStackTrace` since M5's G4, and `failures` now prints
from it, so the agent lane carries the frame the HTML, `CiSummary.md` and the XML export always did.

**One guard learned one exemption, by name.** `Every_flag_the_reference_documents_is_known_to_the_parser`
read `--filter`, `--filter-method` and `--treenode-filter` out of the new prose as tool flags. A quoted
`dotnet test …` line is another program's command line, so that one program's line is stripped before
flags are extracted; a bare `--flag` anywhere else in a code context is still held to the parser, and
the two runner options are named in prose without their dashes.

Suite 4,790 → **4,815**. Minor: `repro`, `--describe`, `--version`, `FailureText.TestFilter` /
`RerunCommand` and the three record members are new surface. Pins → 3.6.0, which published.

---

### 2026-09-14 (M8) — an agent that never heard of Kronikol finds it, released as 3.8.0

Rows H1–H5, in-repo only, one commit (`5337c0e1`). Nothing was published outward; the outward steps are
listed in §14.3 as people-work.

**H2/H3 — a plugin manifest, held by a test instead of a binary.** `.claude-plugin/plugin.json` names
the `kronikol` plugin and points `skills` at `./templates/skills`, the canonical copy (`.claude/skills/`
is the dogfood mirror and stays pinned to it); `.claude-plugin/marketplace.json` lists it from this
repository (`source: "./"`), so `/plugin marketplace add lemonlion/Kronikol` then
`/plugin install kronikol@kronikol` installs the skill without `kronikol init-agents`. C11 held:
`category`/`tags` are marketplace-entry fields, not manifest fields, so discoverability is the
description and `keywords`, and both literally carry `dotnet`, `test`, `report`, `failures`, `xunit`,
`nunit`. `claude plugin validate --strict` has no runner here (C80's instrument is still absent), so
`PluginManifestTests` is the offline stand-in: documented fields only (the misspelling class `--strict`
exists for), kebab-case names, `./` component paths that stay inside the plugin and hold a `SKILL.md`
whose frontmatter name is the folder, the version equal to `Directory.Build.props` so the release helper
moves it or the build fails, and the same terms on `Kronikol.Tool`'s description. The MCP registry entry
itself (H3's shelf) stays out: `MCP_PLAN.md` is not green-lit, and the terms are applied where an entry
would be generated from.

**H4 — tags.** `Kronikol.Tool` overrides `PackageTags` the way four sibling projects already do
(`$(PackageTags);dotnet-tool;cli;test-report;test-results;failures;debugging;ai;agent;llm;claude-code;ctrf;merge;ingest;opentelemetry`)
and its description opens with the search sentence. The reserved-prefix application is people-work.

**H1 — the old name, named.** `README.md`, `nuget-readme.md`, the wiki `Home` and the `Kronikol`
package description say "formerly TestTrackingDiagrams". Found on the way: the core package's NuGet
`<Title>` still read `Test Tracking Diagrams`, four months after the rename — which is part of why the
capability query ranked the old identity first. The title is `Kronikol`; the old name lives in the
description, where it keeps ranking without mislabelling the package. Deprecating the
`TestTrackingDiagrams.*` packages on NuGet is people-work.

**H5 — `dnx`, with its costs.** The no-install route is on every agent-facing install site (both skill
copies, the emitted `Reports/CLAUDE.md`, the `templates/agents` block and its installed copies in this
repo's `CLAUDE.md`/`AGENTS.md`, the wiki) beside `dotnet tool install`, which stays primary, with C65's
four measured counts stated: feed on every call and no offline fallback, 3–5× per command, last
*published* version only, and it swallows the tool's `--help`/`--version`. The run-end pointer and
`CiSummary.md` were left alone: one line each, already naming the installed command.

**A release defect found by the 3.7.0 tag, fixed under it.** `ReproTests` asserted a Windows path's
file name through `Path.GetFileName`, which on the Linux release runner does not split `\`; the Release
job failed on that one test and published nothing. The same trap was in `FailureText.ThrownAt`'s
declaring-file match — a report written on Windows and read on a Linux runner compared whole paths
where it meant file names and fell through to the namespace skip — fixed with a separator-agnostic
`FileNameOf` and a guard that passes on Windows either way and fails on Linux without the fix. The tag
was moved to the fixed commit; the version did not change because nothing a consumer calls did.

Suite 4,815 → **4,823**. Minor: a plugin manifest is a new integration; the tool's tags and the old-name
pointers are words. Pins → 3.7.0.

**The plan is complete.** Eight milestones, one release each, 3.1.0 → 3.8.0. What no session could do is
in §14.3 as people-work: a real GitHub Actions run of a default project (C75), a non-Claude agent
session (C77/C78), the marketplace submission (C79), `claude plugin eval` (C80), the old packages'
deprecation and the reserved-prefix application (H1/H4).

---

## 18. How to continue this plan

§14 is the work-list and §17 is its journal. The intended mechanic is an iterating session that drives
every §14.1b row to `RUN` or to an explicit unpromotable, then stops.

**Start it while the tree is live — chasing the moving target is the whole reason it is a loop and not
one more audit.** Step 1 exists precisely because M2.10 and M3 are being written into this same working
tree: a claim whose subject moved is a claim to re-run, not a reason to postpone. Waiting also inverts
the plan's purpose. The rows with the highest `Decides` values — the three clustering key sites, the 313
phantom errors in `services`, `query.py` on Windows — decide work that is being written *now*. Settled
before M3, they are design; settled after it, they are rework, which is the outcome this plan exists to
avoid.

The precedent is this pass itself: **all 107 of its RUNs were taken against a live tree whose binary
rebuilt five times underneath them** (14:37 → 16:46), and M2.9 landed mid-audit and was folded in as
C81/C82 rather than waiting for. A moving tree is the demonstrated operating condition, not the
exception.

Two things genuinely have to wait, and they are **rows, not the loop**:

- **The experiments needing `dotnet build` / `dotnet test`** — §14.3 items 2, 3, 4 and 9 (the channel
  table re-run, a real sharded run, Chromium deep-links, a real populated-`calls` path). Park those
  rows and say so in their cells; everything else is reachable against the pre-built
  `Kronikol.Tool.dll` and the 14 fixtures, which is how the 107 were done.
- **The ones no session can reach at all** — §14.3 items 1, 5 and 6: a live GitHub Actions run, a
  non-Claude agent session, a marketplace submission. Those never become reachable by waiting, so
  waiting buys nothing for them either.

The live tree's real hazard is not staleness but *silent* staleness: one intermediate build during this
pass routed `query` into `MergeCommand` and produced a finding that was wrong rather than merely old.
The answer is provenance (§3.5, §13 hazard 1) — stamp every `RUN` with the mtime of the `Kronikol.dll`
beside the fixture, so a result taken against a half-built tree is *detectable* and re-run on the next
iteration. That machinery only earns its keep on a moving tree; gating the loop on a still one would
make it dead weight.

Run in a fresh session, at `high` effort, with ultracode off — the prompt's own fan-out clause is the
opt-in, and most iterations are "run three commands, update four rows", which a workflow per tick only
makes slower.

**Check `/loop` is offered before pasting 2,500 words into it.** It is a Claude Code built-in, not a
file: there is no `loop` skill under `.claude/skills/`, `.claude/commands/` or `~/.claude/` on this
machine, and searching for one returns nothing — which is the *expected* result for a built-in, not
evidence against it. The support is visible instead in the `ScheduleWakeup` tool, which exists to serve
"/loop dynamic mode — the user invoked /loop without an interval". That is shape evidence, so settle it
the cheap way: type `/` in the fresh session and look for `loop` in the list. If it is there, the
command below runs in **dynamic mode** (no interval given), which self-paces between iterations rather
than firing on a clock — the right mode here, because iteration cost varies from three commands to a
fanned-out experiment. If it is *not* offered, the body still works pasted as a plain instruction, but
nothing re-invokes it: you get one pass, not a loop, and the re-sync in step 1 never happens — which is
the entire point of running it this way.

The command:

```
/loop Work on C:\Code\Kronikol\LLM_FIRST_PLAN.md until every load-bearing claim in its §14 assumption ledger is at Depth RUN, or is explicitly marked unpromotable with the reason and the experiment that would settle it.

Each iteration:

1. RE-SYNC FIRST. Read §14 (the ledger), §17 (the verification log) and §13 (nine standing hazards, every one of which produced a wrong finding in the pass that wrote this plan) of LLM_FIRST_PLAN.md, then LLM_FRIENDLY_PLAN.md §11 and its "Remaining" block, then run `git status --short` and `git log --oneline -5`. Whatever LLM_FRIENDLY_PLAN still lists as remaining is being implemented in this same working tree - take that list from that file, never from this prompt, which goes stale - so the target moves. Then stat the Kronikol.dll the fixtures are exercised against and compare its mtime against the provenance stamps in §17: every row stamped against an older binary drops back to UNVERIFIED and is re-run, and so does any claim whose subject changed. Note in §17 whether `git status` came back quiet, because that decides step 2. New surface that landed since the last iteration gets its own new numbered claims. Record the re-sync in §17 even when nothing moved.

2. PICK WORK. From §14.1b and §14.2, take the 3-6 highest-stakes rows that are not yet RUN, ranked by the "Decides" column - does this claim decide what gets built? A row that decides nothing may stay at READ forever; write that in its cell rather than spending an iteration on it. TAKE THE BUILD WINDOW WHEN IT OPENS: if this iteration's `git status` came back quiet, spend it on the rows parked BLOCKED(tree) before anything else - §14.3 items 2, 3, 4 and 9 need `dotnet build` or `dotnet test` and are unreachable on a dirty tree, so a quiet tree is the scarce resource, not the iteration. If the tree is dirty, park those rows BLOCKED(tree) naming the exact command they are waiting for, and move on: do not retry them every iteration, and do not let them hold the STOP condition. Where an iteration has several independent experiments, fan them out with subagents or a workflow.

3. APPLY THE §14.0 RULE BEFORE TOUCHING ANYTHING. For each row, write down what you would have seen if the claim were false, and where that would be visible. Then go there. Do not re-read the citation already in the row - it is already satisfied, and that is exactly why it proves nothing. Execute the falsifier: run the tool, run the report JS in node, generate a real report, parse the real output, drive a real agent path.

4. PROMOTION RULE, ABSOLUTE. A row may read RUN only if its evidence cell contains the literal command and the real output you saw. No command, no RUN - READ stays READ. A RUN in a substitute environment (jsdom for a browser, a reflection harness for a run, a synthetic shard for a real one, one search engine for the web) is RUN(proxy) and must say which. STAMP PROVENANCE ON EVERY RUN that touches the product binary: record the mtime of the Kronikol.dll beside the fixture you ran against, because the tree is live and rebuilds underneath you, and kronikolVersion separates nothing - every working-tree build stamps the same string. A row whose binary has moved since its stamp drops back to UNVERIFIED at the next re-sync; that is step 1 doing its job, not wasted work. If the experiment is impossible here (needs live CI, a fresh agent session, a week of elapsed time, an org you do not have), mark the row unpromotable with its blocker, name the falsifier, and state whether anything is built on it. An honestly unpromotable row is a finished row.

5. PROPAGATE. A REFUTED or NARROWED claim does not just change its own row: fix every sentence, milestone, recommendation and open-question answer in the plan body that rested on it, and add a line to the §14.0 reversal table giving what was verified, what was then claimed unverified, and what is actually true. A reversal that only edits the ledger is the error class repeating itself. Update the base-rate line in §14.0 with the running count.

6. LOG. Append one dated entry to §17: rows attempted, the exact commands, the verdicts, what changed in the body, and what you tried that did NOT settle a row - so the next iteration does not repeat it.

CONSTRAINTS. Write only LLM_FIRST_PLAN.md. The working tree is live with another session's in-flight work: leave every other tracked file alone, and put harnesses and scratch output in the session scratchpad, naming their paths in §17. Every command that GENERATES something - `kronikol ingest`, a generated report, a harness, a tool that defaults its output beside its input - gets an explicit output path inside the scratchpad. The pass that wrote this plan let a subagent drop a stray file into the repo root despite the same instruction, so end every iteration by re-running `git status --short` and confirming it lists nothing you created. Do not build or run the suite while `git status` shows someone else's edits in flight. Never invent a number, a command output or a citation - an honest UNVERIFIED beats a dressed-up READ, and the ledger is worthless the moment it cannot be trusted about which of its own statements are measured.

STOP when every §14 row is RUN, explicitly unpromotable, or parked BLOCKED(tree) with its command named; §14.2 has no unaddressed assumption; and a completeness pass finds no unexamined surface. Then post a final summary - rows moved, claims reversed, rows permanently unpromotable and why, and the BLOCKED(tree) list with the one command that would clear it - and stop the loop. Also stop if two consecutive iterations promote nothing and reverse nothing, and say what is blocking.
```

**Why a loop rather than one more audit.** A single pass is correct about a tree that no longer exists
by the time it is read — this one went stale inside an hour when M2.9 landed. The loop's unit of work is
one claim promoted or reversed, and its step 1 exists because the target moves. What it cannot do is
settle the six rows in §14.3 that need a live runner, a non-Claude agent or a submission; those are
people-work, and the loop's job is to say so rather than to guess.
