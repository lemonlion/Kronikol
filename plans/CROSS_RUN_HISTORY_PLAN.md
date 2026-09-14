# CROSS_RUN_HISTORY_PLAN — what this test did last time, and the fifty times before that

Status: **EXECUTED (2026-09-14).** 3.9.0 shipped M0, M1, M2, M3 and M6 — the ledger, the verdicts,
the digest/CTRF/pointer surfaces, `kronikol query history` and the `kronikol history` maintenance
verbs; 3.10.0 shipped M4 and M7 — the gate, quarantine, rename aliases, the doctor and imports from
CTRF and Allure; 3.11.0 shipped M5 and M8 — history in the HTML report (rendered on the generation
side, one element per sparkline, rather than the payload-script design below; the verdict filter is the
search box's `$` sigil, the toolbar control waits for the toolbar redesign), `merge --history`, the
dogfood on the orphan branch `kronikol-history`, the wiki and the Kronikol4J divergence entry. Three decisions below moved under
implementation (the roster key includes the suite; flaky needs two failing episodes, not two flips; a
first failure with nothing earlier to compare against is `unknown`, not `broke`) and the code and the
changelog record why. Before that: investigation complete (2026-09-12), nothing implemented. Revised across
many passes the same day, each replacing guesses with measurements — sizing and fingerprinting against the real
BreakfastProvider corpus (§2), three live consecutive runs of a real suite (§2.5), an analyzer
benchmark (§2.6), a concurrent-append harness on Windows plus Linux overlayfs and ext4 (§6.5), and
direct reads of the CI mechanics, git layouts, the CTRF schema and the report's own tokeniser and
export.

**Fifteen of this plan's own decisions have been reversed by checking them** — the base64 templating
rule, the single repo-wide ledger, the lock-free append, the `is:` search syntax, the "lines read"
observable, the absolute flip-count threshold, the direction CTRF's `insights` flow, the claim that
`$verdict` is additive, the claim that setting CTRF's `flaky` improves a flakiness rate, the claim
that a verdict-only query scans the whole corpus, the assumption that pruning to a window *saves*
repository space (it costs 55% more, §5.10), the claim that `merge=union` makes a committed ledger
conflict-free (it does not apply to server-side merges at all), the assumption that a *committed*
ledger means one committed to the **main line** (§6.3d2), the citation of Allure as precedent for
crash-safe appending (its implementation rewrites the whole file), and the headline claim that **the
status half is commodity** (it is not: flakiness is commodity only for suites that *retry*, §0.2). Each would have
shipped a defect and five of them silent ones. **Two were added to this plan on the same day they
were reversed**, from a careful reading of the very code that then contradicted them. **They are all the same mistake** — an
existence check allowed to stand in for a behaviour check — which is why §17.0 now names the class and
§17.1 is split by *how far each check went* rather than by confidence. That is the argument for
keeping the assumption ledger permanently rather than treating verification as a phase.

**The most consequential finding is not a measurement but a read: §0.1 establishes that most of the
status half of this plan already ships free** in `ctrf-io/github-test-reporter`. §0.2 takes that
seriously and offers three options with a recommendation; it is the first section to read if time is
short. §16 has sixteen open questions; five change the artifact.

Written against 3.0.86 + the 3.1.0 working tree. **Assumes `LLM_FRIENDLY_PLAN.md` is complete when
this starts** (stated by the user, 2026-09-12) — so run identity in the JSON, `merge` writing JSON,
`--json` envelopes, `#sid-` deep links, the MCP server, the CTRF writer, the packaged skill and its
drift tests all *exist*. §1.5 lists what that gives for free and what it obliges; several obligations
are mechanical and will break the build if skipped.

Origin: the "Where Kronikol Goes Next" direction note (artifact `807ca04e`), move 5, *Cross-run
history — the minimal version*. This plan deliberately **rejects the word minimal** and keeps the
constraint it was attached to. §0 argues why those are two different things; §0.1 is the evidence.

Interacts with `TOOLBAR_REDESIGN_PLAN.md` (§8.1 adds a filter to a toolbar that plan is rewriting)
and `V4_PLAN.md` (§15.4 — nothing here waits for v4, nothing here is breaking).

---

## 0. The premise: "no hosted dashboard" is not a scope limit

The direction note said two things in one breath:

> Do it as a trend file in the repo, or by reading an artifact folder. **Do not build a hosted
> dashboard** — that trades away "self-contained HTML, no infrastructure" for a fight with funded
> SaaS.

The second sentence is right and survives this plan unchanged. The first sentence — and the word
*minimal* in the heading — does not follow from it. "No server" is a constraint on **where state
lives**. It is not a constraint on **how much the feature computes from that state**.

So the useful question is not "how little can we do" but: **what does the funded competition actually
need a database for?** Exactly three things:

1. **Unbounded retention and cross-repo aggregation.** A file bounded to fit in a git repo cannot
   hold two years of forty repositories, and should not try.
2. **Identity, RBAC and workflow.** Assignment, comments, mute-with-an-approver, audit. These need
   accounts, which need a server.
3. **Push and real-time.** Live dashboards, Slack on first failure, "notify me when this unmutes".

Everything else is a **pure function of the last N runs**, and N runs of verdicts is small — §2.2
measures it at **228 KB for fifty runs of a two-hundred-scenario suite, 36 KB gzipped**. Per-test
status history, flakiness scoring, new-versus-persistent classification, first-seen/last-seen,
duration baselines and regression detection, quarantine lists, merge gates, cross-branch comparison,
failure-cluster ageing — arithmetic over a file that fits in a git commit.

**Therefore the complete feature is: everything except those three, and say so out loud.** §14 is the
anti-scope, written as a contract rather than an apology.

One thing the constraint buys that a hosted dashboard actively loses: if the ledger is a file in the
repo, a PR branch's checkout **already contains main's history**. "Is this failure new, or has main
been red since Tuesday?" is answerable inside a pull-request build with zero infrastructure, zero
credentials and zero network calls. Allure TestOps answers that with a database and an API token.
Kronikol can answer it with `git checkout`.

## 0.1 Prior art and competitive position — checked, not assumed

Three claims that were general knowledge in the first draft were checked on 2026-09-12. All three
changed something.

**Allure has already made the file-format mistake once, and corrected it.** Allure 2 stored history
as a **directory of JSON documents** (`allure-results/history`) copied between runs, retaining **20
reports**. Allure 3 replaced it with **a single JSONL file** (`historyPath: "./history.jsonl"`): each
generation reads prior lines and *appends one*. A vendor with a paid server product moved, in a major
version, from document-rewriting to append-only. §3.1 follows it. Also: Allure's **`historyId` is the
fully-qualified name plus non-excluded parameters** — functionally identical to `stableId`, which
validates the key and confirms the rename wound is industry-wide; and their **Retries tab is "same
historyId within one launch"**, the same model as §1.2's `attempts`.

**Azure DevOps already gives .NET teams free flaky-test management.** It reruns failed tests inside
one pipeline execution, tags anything that passes on rerun, propagates the tag across the branch, and
can **suppress flaky failures so they do not fail the build** — the gate, free, in the box. What it
does not do: detection is **rerun-based only** and **tightly coupled to the VSTest task**; it is
**ADO Services only** (not Server, not GitHub); test-summary integration covers only the VSTest and
Publish Test Results tasks; custom detection means REST calls; and **switching detection mode erases
all stored flakiness history**.

**The no-server flaky niche on GitHub is not empty either** — this corrects the direction note's "one
real capability gap" framing, which this plan's first two drafts repeated. Open-source Actions
already do history-based flakiness without a database:
[`WithSecureOpenSource/flaky-tests-detection`](https://github.com/WithSecureOpenSource/flaky-tests-detection)
processes historical junit/xunit results and ranks tests by how often they change state — the same
flip-rate idea; [`Staffbase/github-action-find-flaky-tests`](https://github.com/Staffbase/github-action-find-flaky-tests)
derives flakiness from a branch's runs; and several tools read GitHub Actions run history through the
API with no rerun and no config.

**And the strongest finding of the whole investigation: for a .NET team on GitHub, most of this plan
already exists, free, today.** Read from `ctrf-io`'s own repositories and action manifest:
- **`ctrf-io/github-test-reporter`** takes `upload-artifact: true` + `fetch-previous-results: true` +
  a `GITHUB_TOKEN` — **precisely the artifact-relay ingress this plan designs in §6.3b, already
  shipping** — and produces `flaky-rate-report`, `fail-rate-report`, `previous-results-report`,
  `insights-report`, `slowest-report`, `summary-delta-report` and `tests-changed-report`, rendered
  into PR comments and job summaries. Its README claims *"failures, flaky tests, and duration trends
  **across hundreds of runs**"*. It also has `exit-on-fail` and `annotate`.
- **`ctrf-io/dotnet-ctrf-json-reporter`** already emits CTRF from **MSTest, NUnit and xUnit**.
- **`ctrf-io/junit-to-ctrf`** converts the lingua franca in, and Slack / Teams / Jira / Mattermost
  reporters consume it out.

So a .NET team can have cross-run flakiness, fail-rate and duration trends with PR comments **in
about ten lines of YAML, with no server and no Kronikol**. That is not a reason to abandon this work,
but it is a reason to stop describing the status half as a capability gap. §0.2 takes it seriously.

**What is still genuinely unoccupied** — and what the plan must now be judged on:
- **The interaction layer** (§4) — behaviour drift on a *passing* test. Nothing in the field has it,
  because nothing else captures the calls. CTRF's schema cannot even represent it.
- **A history-aware gate with quarantine** (§8.5–§8.6). `exit-on-fail` is binary: any red fails. "Fail
  on *new* failures while tolerating known-flaky ones" is the policy teams actually want, and nothing
  in the CTRF chain expresses it.
- **The repo-committed ledger** (§6.3d/§6.3d2) — history in the *repository*, so a PR build answers
  "is main red too?" with no API, no token beyond the job's own, and no artifact retention window.
  Artifact-relay tooling cannot. *(Precision the later sections force: with §6.3d the file is in the
  checkout and costs nothing to read; with the recommended §6.3d2 it is one `git fetch` away. Both
  keep the property that matters — the answer comes from the repository rather than a provider API —
  but "no fetch" is true only of (d).)*
- **It lives in the artifact people already open**, works on any CI or none, and needs no reruns.

Two design rules follow, and both are load-bearing. **Do not make rerun-based detection the primary
mechanism** — that is ADO's commoditised half. And **the competition consumes JUnit XML**, which is
why they work anywhere; §6.3 and §16 Q13 ask whether Kronikol's ledger should ingest it too.

---

## 0.2 Which half is actually commodity — narrowed by execution

§0.1's CTRF finding forces a question this plan should not dodge, because answering it wrong wastes
the most expensive thing available — attention. The first draft of this section said **"the status
half is commodity."** That was too strong, and running the free chain's own arithmetic says exactly
how much too strong.

> **The free chain cannot see flakiness that is not expressed as retries.** `enrichReportWithInsights`
> from `ctrf-io/github-test-reporter` was executed directly (Node 25 type-stripping, six synthetic
> CTRF runs, `tools/history-bench/ctrfrun/`) over a test that **genuinely flips** — passing four
> runs, failing two, never retried — which is what **Kronikol itself produces**, and that is a fact
> about this repo rather than a claim about anyone's test framework: `Scenario.Attempt` is populated
> by exactly one ingestion lane (`CucumberFeatureSynthesizer`; the merge reader and query scanner
> only pass it through), and its own XML doc says so — *"null where the runner reports nothing about
> attempts, which is every lane but Cucumber Messages today."*
>
> | Producer | `flakyRate` | `failRate` | `insights.extra.totalResultsFlaky` |
> |---|---|---|---|
> | Plain CTRF emitter | **0** | 0.3333 | 0 |
> | Kronikol also sets `flaky: true` | **0** | 0.3333 | **6** |
> | A suite that retries (`retries: 2`, always passes) | **0.6667** | 0 | 5 |
>
> The last row is the point: a test that **never fails** scores 0.67 flaky because it was retried,
> and a test that **fails a third of the time** scores 0. `flakyRate` is not a flakiness measure, it
> is a retry-volume measure. **For every suite that does not retry — which is the default for the
> frameworks Kronikol targets — the free chain's flakiness feature returns zero for every test,
> permanently.**

So the accurate statement is narrower and more useful than "the status half is commodity":

- **Genuinely commodity:** fail rate, pass rate, duration trend and P95, run-over-run deltas, and the
  PR comment that renders them. These are real, free, and Kronikol should not rebuild them.
- **Commodity only for retrying suites:** flakiness. Kronikol's flip-rate measure (§7.2) is not a
  better version of the free one — it measures a **different thing**, and it is the only one of the
  two that works when nothing retries. **Kronikol has no retry count to emit on any native lane**,
  so for every suite it captures outside Cucumber Messages, the free chain's `flakyRate` is
  structurally zero no matter what the underlying runner supports.
- **Unoccupied either way:** the interaction layer (§4), the history-aware gate with quarantine, and
  the repo-committed ledger that answers "is main red too?" from the checkout.

**The interop consequence is concrete and was also executed** — including rendering the templates.
A producer-set `test.flaky` survives into the enriched report; is counted into
`insights.extra.totalResultsFlaky`, which **is emitted and therefore templatable**; moves `flakyRate`
by exactly zero; and **fills the PR comment's Flaky Tests table — which nothing else fills**. That
last point is the surprise: rendering `flaky-table.hbs` shows a test with `retries: 2` and no `flaky`
flag produces *"No flaky tests in this run ✨"*, because `anyFlakyTests` is `some(t => t.flaky)` and
never consults `isTestFlaky`. The table is **producer-only**; the rate is **retry-only**; they share
nothing.

So the recommendation is **set `flaky`, turn `flaky-report` on (it defaults to `false`), and ship a
Handlebars template that reads `totalResultsFlaky`** rather than pointing users at the built-in
flaky-rate table, which will report 0.00% beside a populated list of flaky tests and look broken
(§8.6, §15.1).

**A. Build all of it** (what §7–§8 describe). Justified only if the in-report experience, the
repo-committed ledger, the history-aware gate and quarantine are worth their cost *on their own*,
knowing that flip-rate, fail-rate and duration trends are already free elsewhere.

**B. Emit CTRF; build only the behaviour axis.** Kronikol writes CTRF (already planned as LLM M3.2);
`github-test-reporter` computes and renders the status trends; Kronikol's ledger stores **only what
CTRF cannot represent** — interaction shapes — and the report surfaces behaviour drift alone. Every
unique claim in §0.1 survives, at perhaps a third of the work. What is lost: the gate, quarantine,
the committed ledger, and history for anyone not on GitHub Actions.

**C. Build the ledger, but reorder around what is scarce.** *Recommended.* The ledger is cheap —
§2.2 measured 228 KB for fifty runs — and it is the same file whether it carries one field or six, so
the marginal cost of the status columns is close to zero once the format exists. What changes is
**sequence and framing**: behaviour history stops being M6-at-the-end and moves up behind the ledger
itself, because it is the only part nobody else is giving away; the status surfaces are described
honestly as *parity with a free baseline, in a better place*, not as the value proposition; and
`export --ctrf` (§8.6) ships early so a team that prefers the free chain can have both.

**The milestone order this implies:** M1 (ledger) → **M6 (behaviour) promoted to M3** → verdicts →
digest/CLI → gate + quarantine → HTML. The gate and quarantine keep their place as the second thing
nobody else offers, and the HTML work — the most expensive slice — sits behind two milestones that
have already justified themselves. §10 keeps the original numbering for traceability and notes the
recommended execution order.

**This also closes two open investigations.** The "GitHub Action synergy" (previously §17.3 item 3)
is answered: **the Action exists and belongs to someone else.** Kronikol should interoperate with it
via CTRF, not build a rival. And whether CTRF's ecosystem consumes `insights` (item 2) is answered —
in the opposite direction to the first draft's assumption, and only fully once the code was run
rather than read (§8.6, §17.0).

**One line of framing follows from all of it, and it is not marketing.** Against the free chain,
Kronikol's honest claim is *not* "better flakiness detection". It is **"flakiness detection at all,
for suites that do not retry"** — plus behaviour drift on a passing test, which nothing else has.

---

## 1. What already exists (verified 2026-09-12 — do not rebuild)

### 1.1 The cross-run key
- **`stableId`** — `ScenarioStableId.Compute` (`src/Kronikol/Reports/ScenarioStableId.cs:18-34`):
  first 16 hex of SHA-256 over `feature::[outlineId::]scenario[::k=v|…]`. Already in the JSON
  (`ReportGenerator.cs:3712`), XML (`:4040`), YAML (`:4158`), schema (`:5039`),
  `Failures.md`/`.jsonl` (`FailuresDigestGenerator.cs:135`, `:456`) and the console pointer
  (`RunSummaryConsoleWriter.cs:101`). Equivalent to Allure's `historyId`.
- **It is not unique within a run** — a `[Theory]` with repeated data, the same example row in two
  `Examples:` blocks, a retried scenario, an ingested repeat. `query diff` groups by id and matches
  in file order (`QueryCommand.Search.cs:203-213`, `:272-280`); history inherits that exactly.
- **It is not unique across test projects either** — measured, §2.3. This is new information and it
  changes the format.
- **It is not rename-proof.** §7.4, M7.

### 1.2 The one-run pieces a history layer composes
- **`kronikol query diff`** (`QueryCommand.Search.cs:235-310`) — the pairwise ancestor; already
  computes `BROKE`/`fixed`/`new`/`Gone`/`slower` (1.5× threshold, `:298-299`).
- **`FailureClusterer`** (`Reports/FailureClusterer.cs`, `NormalizeKey` `:32-39`) — the natural
  **error identity across runs**. **`ErrorDiffParser`** — expected/actual, public.
- **`CiMetadataDetector.Detect()`** (`Reports/CiMetadata.cs`), called at `ReportGenerator.cs:229`.
  **`GITHUB_RUN_ATTEMPT` is not captured today** and must be (§3.3).
- **`RunOutputs`** — the isolated writer list, `ReportGenerator.cs:265-336`, `Parallel.Invoke` with
  per-output try/catch (`:337`, `DiagnosticKind.OutputFailure`). **Every new file goes here.**
- **`RunSummaryConsoleWriter`** (pointer, `::notice`, `BuildCiSummarySection`) and
  **`FailuresDigestGenerator`** (`Failures.md`/`.jsonl`, clustered, `MaxDetailedFailures = 25`).
- **`MergeableReportMerger`** — dedupes by runtime `Scenario.Id` not `stableId` (`:41-72`, `:52`/`:60`);
  **`MergeableReportRenderer.Render`** calls `ReportGenerator.GenerateHtmlReport`, so merged HTML
  inherits whatever the generator renders (§8.7).
- **Within-run retries exist on one path**: Cucumber ingest keeps the last attempt and leaves a
  `retry N` label (`CucumberFeatureSynthesizer.cs:88-90`, `:472-475`).
- **`DiagnosticKind.ResultDefaulted`** (`Reports/DiagnosticEntry.cs:62`, raised at
  `IngestPipeline.cs:375`) — `ResultWhenUnknown` defaults to `Passed` (`:63`). §5.11.
- **`ComponentFlowSegmentBuilder.NormalizePathGuids`** (`:712-713`) — GUID-only, whole-segment.
  **Measured at 77.9% where the plan needs 96.6%** (§2.4). It is the starting point, not the answer.

### 1.3 Delivery channels and their reliability
Measured 2026-09-12, in `RunSummaryConsoleWriter`'s doc comment: under `dotnet test` **every
VSTest-hosted runner swallows library console output at every verbosity**, and TUnit's runner
suppresses it too; only a directly executed MTP host shows it. The durable channels are **files in
the reports directory** and **the CI job summary**.

### 1.4 Where the code goes
`Kronikol.Tool.csproj` has `<ProjectReference Include="..\Kronikol\Kronikol.csproj" />`, so the tool
already sees the library. The ledger reader, writer, analyzer and fingerprint therefore live in
**`Kronikol` (the library)** — the library writes fragments and needs verdicts for the HTML and the
digest; the tool needs the same types for `record`/`import`/`gate`. One implementation, no
duplication, and Kronikol4J inherits a single porting target. New `DiagnosticKind` members
(`HistoryUnavailable`, `HistoryPartialRun`, `HistoryLedgerDamaged`) are **appended, never
renumbered** — the enum's own doc comment says so.

### 1.5 What the completed LLM plan hands this one — and what it obliges
**Free:** `ciMetadata` in the standard JSON (M2.3) · `merge` writes JSON (M2.4a) · `#sid-` deep links
(M2.1) · the `--json` envelope (M2.9), so history verbs get it on day one · `Failures.md`/`.jsonl`
and `agent-instructions.md` · the MCP server (M3.1) · the CTRF writer (M3.2).

**Already landed in the working tree, checked 2026-09-12** (so these are facts now, not assumptions):
**M2.3** — `MapCiMetadataJson` + `MapEnvironmentJson` in the standard JSON, and the matching
`CiProvider`/`CiBranch`/`CiCommitSha`/`CiRunId`/`EnvironmentOs`/`EnvironmentRuntime` on `ReportIndex`,
which is exactly what `history import` (§6.3e) reconstructs run entries from — that path no longer
needs the mergeable file. `ReportIndex.OnCi` even distinguishes *"ran off CI"* from *"written by an
older Kronikol"*, a distinction history needs when deciding whether a run belongs to a branch stream.
**M2.6** — `Feature.SourceFile` and `Scenario.SourceFile`/`SourceLine` (project-relative, forward
slashes, null on lanes that cannot supply one). Two consequences worth taking: the ledger's roster can
carry the source path, which gives §7.4's rename detection **a third matching signal** alongside name
distance and shape; and `query history` can point at the file rather than only naming the scenario.
Both are cheap once the fields exist, and neither was available when this plan's §7.4 was written.

> **Status re-checked 2026-09-12 afternoon, after the LLM plan moved underneath this one.** Its own
> §11 now reads: **done** M2.1, M2.2, M2.3, **M2.4 (`merge` JSON *and* `--baseline`)**, M2.5, M2.6,
> M2.7; **in flight this afternoon** M2.8 (skill distribution, `kronikol init-agents`,
> `SkillDriftTests`); **not started** M2.9 `--json`, M2.10 OTLP, M3.1 MCP, M3.2 CTRF, M3.3
> `Specifications.md`. So the "Free" list above is still a *premise* — this plan assumes the LLM plan
> finishes — but three items in it have now landed as fact, and **three obligations appeared that
> were not there when §1.5 was written**:
>
> - **`Commands.cs` is new: one table is now the single source of what `kronikol` can do**, replacing
>   three hand-kept lists in `Program.cs`, and `CommandTableTests` sees it. Five entries today
>   (`merge`, `ingest`, `query`, `export`, `init-agents`). A new top-level command is now **one edit
>   plus a test that already exists** — cheaper than when this plan was written, and a touchpoint M1
>   must name.
> - **`kronikol init-agents` writes a managed `<!-- kronikol:begin -->` block into a consumer's
>   `CLAUDE.md` and `AGENTS.md`.** Teaching an agent about history is therefore a **code change**, not
>   a documentation edit. And the templates csproj now states the rule: `templates/skills/` and
>   `templates/agents/CLAUDE.md` are **canonical**, with the repo's own `.claude/skills/` copy,
>   `init-agents` and all twelve templates installing those same bytes, `SkillDriftTests` pinning
>   every copy. One edit propagates to 36 places — and hand-editing any copy fails the suite.
> - **`KRONIKOL_BASELINE` is now the house idiom** for "an environment variable names a location the
>   tool would otherwise find by convention", alongside a `baseline/` folder beside the report
>   (`BaselineDiffTests`). §6.2's `KRONIKOL_HISTORY` should be read as following that precedent, not
>   inventing one — same shape, same ordering, same "name both in the error when neither is found".

**Obliged — mechanical, and skipping these breaks the build or the contract:**
- **The skill has three hand-maintained assets** (`templates/skills/kronikol-test-debugging/SKILL.md`,
  `references/commands.md`, `scripts/query.py`) and LLM M2.8 adds `SkillDriftTests`, asserting every
  verb in `PrintUsage` appears in `SKILL.md` and `commands.md` and vice versa. **Adding a 19th verb
  without updating all three fails the suite.** A red test, not a documentation nicety.
- **`query diff --baseline` becomes the degenerate case**, not a casualty: `diff` stays the pairwise
  tool, history answers the N-run question, and the wiki must say which to reach for. **It has now
  landed** (M2.4), so this is a fact to integrate rather than a future to anticipate: it resolves a
  baseline explicitly, then by a `baseline/` folder beside the report, then `KRONIKOL_BASELINE`, and
  errors naming both when neither exists. Two consequences. §6.2 inherits that ordering rather than
  inventing a parallel one. And there is an integration worth deciding at M2 rather than discovering
  later: **once a ledger exists, `--baseline` could resolve "the last green run" from it** instead of
  needing a folder on disk. That is the natural join between the two features and this plan had not
  considered it; it is not M1 scope, but it belongs in §16 as an open question rather than a surprise.
- **CTRF's test object carries `flaky`**, which M3.2 cannot fill. History fills it (§8.6).
- **The MCP tools map 1:1 onto verbs** — a new verb wants a tool plus M3.1's parity fact.
- **`agent-instructions.md`** teaches the ladder and gains a rung.

---

## 2. Measured, before designing (the M0 pass, run 2026-09-12)

Everything below was measured against **the real BreakfastProvider corpus** — six component test
projects (BDDfy, LightBDD, NUnit, ReqNRoll, TUnit, xUnit) exercising one API, 5–7 MB of
`TestRunReport.json` each. Scripts in the session scratchpad; **M0 re-runs them as committed
harnesses** (§10). Four results changed the design; one killed a rule the previous draft proposed.

### 2.1 Shape of a real suite
**203 scenarios · 2,693 interactions · mean 13.3 calls per scenario · max 76 · 153 distinct URIs.**
Every scenario had at least one interaction. This is the per-run row size the format has to carry.

### 2.2 Ledger size — measured, not estimated
Built from the real roster (203 ids, names and features) with realistic run lines:

| Window | durations + shapes every run | …only last 10 runs | neither |
|---|---|---|---|
| 20 | 107 KB (gz 19) | 84 KB (gz 13) | 60 KB (gz 7) |
| 30 | 148 KB (gz 25) | 100 KB (gz 14) | 76 KB (gz 8) |
| **50** | **228 KB (gz 36)** | 133 KB (gz 15) | 109 KB (gz 8) |

The previous draft estimated "~210 KB, ~30 KB gz" for a *341*-scenario suite at window 50; the real
figure for a *203*-scenario suite is 228 KB, so the estimate was optimistic by roughly 1.8×. It does
not change the answer: **window 50 with full durations and shapes is affordable** — a 228 KB file in
a repo is unremarkable, and 36 KB gzipped is a fine HTML payload. The recent-10 policy saves 42% and
is worth having as an option, not as a default.

**Six suites in one repo at window 50, recent-10 policy: 796 KB** (full: ~1.4 MB). Which leads
directly to:

### 2.3 The design flaw: `stableId` collides across test projects — **fixed at source while this plan was being written**

> **Outcome first, because it changes what this plan builds.** This measurement was acted on in
> production rather than worked around here: `ScenarioStableId.Compute` now takes **`suite` as its
> first argument**, `RunSuite` resolves it, and `ReportConfigurationOptions.SuiteName` overrides it
> (§3.2). The implementation's own doc comment cites the numbers below, and adds one this plan did not
> have: **0 of 1,043 ids collide *within* any single report.** So the defect was always a
> *cross-report* one — a `merge` across suites, an ingest folding two runners, or this ledger — and
> never a live defect in an ordinary run. Two consequences: §3.2 stops specifying suite resolution and
> consumes `RunSuite` instead, and the ledger's suite scoping is re-justified on roster semantics
> rather than on collisions.

Across the six projects: **905 distinct stableIds, of which 145 (16.0%) appear in more than one
project.** Where two projects run the same Gherkin against the same API they produce identical
`feature::scenario` pairs and therefore identical ids. **Which projects, precisely:** the 145 are the
scenarios common to **NUnit, TUnit and xUnit**; BDDfy, LightBDD and ReqNRoll word their Gherkin
differently enough to share no id with anyone (§2.10). The collision is real and the count is right —
it is just not a six-way one.

Under the previous draft's single repo-root ledger, those six runs would interleave **as if they were
retries of one test**: flip rates computed across frameworks, durations mixed across runners, and
`absent` firing on 100% of every other project's scenarios on every run. The feature would produce
confident nonsense, in the demo repo, on day one.

**Fix (§3.2): every run line carries a `suite`, and all analysis is scoped by it.** Rosters are
per-suite. `absent` means absent *within its own suite*. Default suite identity is the test assembly
name; `SuiteName` overrides (§3.2 — now `RunSuite`, implemented in production).

### 2.4 The fingerprint: a measured templating ladder
Using the 145 cross-project scenarios as a proxy for "the same logical test, captured twice",
agreement of the interaction fingerprint under successive policies:

| Policy | Identical shape | Distinct templated URIs (of 435 raw) |
|---|---|---|
| No templating | 66.9% | 383 |
| **GUID-only, whole segment (today's `NormalizePathGuids`)** | **77.9%** | 205 |
| Whole-segment classes (guid, hex32, numeric, ULID, ISO) | 89.7% | 141 |
| **Intra-segment substitution, ordered** | **93.8%** | **106** |
| **Intra-segment, order-insensitive (sorted multiset)** | **96.6%** | — |
| Call count only (floor) | 97.2% | — |

Three conclusions, each now a design decision:

1. **Whole-segment classification is not enough.** The jump from 89.7% to 93.8% is entirely
   `Recipe-6f7d1a996eed46389c30b791b6a62bfe` — a volatile id glued to a literal prefix *inside* one
   segment. The templater substitutes **within** segments, keeping the literal part:
   ```
   GUID  →  {id}        \b[0-9a-fA-F]{32}\b        →  {id}
   \b[0-9a-fA-F]{16,}\b →  {id}   ULID(26)         →  {id}
   ISO-8601 timestamp   →  {ts}   bare digit run   →  {n}   (not adjacent to letters)
   query string: keys kept, sorted; values dropped
   ```
2. **The base64 rule proposed in the previous draft is actively harmful — delete it.** It classified
   10.2% of all path segments as volatile, and inspection shows *every one is a real literal*:
   `breakfast-equipment-alerts`, `BatchCompletionRecords`, `breakfast_batch_completions`,
   `PancakeBatchCompletedEvent`, `DailySpecialOrderedEvent`. These are event, queue, topic and table
   names — **precisely the identity that must stay in the fingerprint.** Templating them would make a
   scenario that switched from publishing `PancakeBatchCompletedEvent` to `DailySpecialOrderedEvent`
   report *no behaviour change*: a silent false negative in the one feature whose whole purpose is
   catching silent changes. Any "long opaque token" heuristic is rejected on this evidence.
3. **Order-insensitivity is worth 2.8 points and the residue is not noise.** The nine remaining
   divergences are `GET /health` fanning out to Supplier / Goat / Cow / Kitchen **in different
   orders** (unordered parallel calls) plus four genuine call-count differences between harnesses. So
   there are **two fingerprints**: `shapeSet` (sorted multiset — the primary signal) and
   `shapeOrdered` (secondary). `behaviour-changed` fires on `shapeSet`; an ordered-only difference is
   a weaker *reordered* signal, off by default (§7.1).

**This is a cross-time measurement, not merely a cross-framework one — checked afterwards.** The six
reports carry `startTime`s of 07:36:04, 07:36:55, 07:37:31, 07:41:04, 07:46:16 and 07:54:42 on
2026-09-05: **six independent executions spread over nineteen minutes.** And of the 262 GUID/hex-32
ids appearing in their URIs, **257 occur in exactly one run** — freshly minted every execution — while
only 5 are fixed test data (`11111111-0000-…`). So 98% of the identifiers the fingerprint had to
absorb were genuinely volatile across runs. The ladder is the churn measurement.

**What is still not controlled is the framework, not the time.** The residual 3.4% is *different
harnesses*, which genuinely make different numbers of calls (§2.4 conclusion 3 identified exactly
that). The gate this left open — *is the residual harness difference or drift?* — is closed in §2.10,
and closed from data already on disk rather than by a re-run.

> **One correction to this section's own framing.** "145 cross-framework scenarios" reads as six
> frameworks agreeing. It is not: **only NUnit, TUnit and xUnit share any `stableId` at all**, and
> the 145 are the scenarios common to those three. BDDfy, LightBDD and ReqNRoll overlap with nobody —
> their Gherkin differs enough to mint different ids. The ladder is a **three-way** measurement over
> 145 scenarios, which is still a real one, but the plan should not imply six independent harnesses
> agreed when three did.

### 2.5 Capture is deterministic across consecutive runs — three real runs
`Example.Api.Tests.Component.xUnit3` executed **three times in succession, unchanged** (11:47:53,
11:48:24, 11:48:29), 5 scenarios and 22 interactions each:

| | ordered | order-insensitive |
|---|---|---|
| **raw, no templating** | 5/5 (100%) | 5/5 (100%) |
| **intra-segment templated** | 5/5 (100%) | 5/5 (100%) |

Byte-for-byte identical call sequences, and **nothing differed even untemplated**. What this proves:
when a suite mints no volatile identifiers, Kronikol's capture is perfectly repeatable — no ordering
jitter, no timing artefacts, no spurious drift from the capture layer itself. That is the half of the
churn question that has nothing to do with templating, and it is now closed.

What it does **not** prove: this suite never exercises the templater (raw was already 100%), so it
says nothing about volatile-id absorption. §2.4 is the evidence for that half. The two measurements
are complementary and neither substitutes for the other.

### 2.6 Analyzer cost, and root discovery — both measured
A C# harness generating and analysing roster-interned ledgers, warm third pass, `Release`:

| Suite | Ledger | Read | Parse | Analyse | **Total** |
|---|---|---|---|---|---|
| 203 scenarios × 50 runs | 0.14 MB | 0.3 ms | 3.9 ms | 0.0 ms | **4.2 ms** |
| 1,000 × 50 | 0.67 MB | 0.9 ms | 18.4 ms | 0.1 ms | **19.4 ms** |
| 5,000 × 50 | 3.34 MB | 4.2 ms | 60.3 ms | 0.8 ms | **65.3 ms** |
| 5,000 × **250 lines**, window 50 | 15.66 MB | 21.5 ms | 74.9 ms | 0.8 ms | **97.2 ms** |

Four things follow:
- **The cost is negligible** — 65 ms on the largest realistic suite, against a test run measured in
  seconds or minutes. §7.7 gets a real budget rather than a hopeful one.
- **Parsing dominates** (60 of 65 ms); the verdict arithmetic is 0.8 ms. If it ever needs optimising
  the lever is not parsing `durations` unless a duration verdict was asked for — not the analysis.
- **The previous draft's observable was subtly wrong.** It said "lines **read** is bounded by the
  window". The last row disproves it: an unpruned 250-line file is scanned end to end (read 4.2 →
  21.5 ms) even though only the last 50 lines are parsed. **Parsing** is window-bounded; scanning is
  file-bounded. So the pinned observable is **`LinesParsed`**, and `history prune` is what keeps the
  scan cheap — a real reason for it, not tidiness.
- **Root discovery is verified for both awkward layouts.** A worktree's `.git` is a file
  (`gitdir: …/worktrees/<name>`, §5.2) and **a submodule's `.git` is also a file**
  (`gitdir: ../.git/modules/<name>`) — both created and inspected locally. A walk accepting
  file-or-directory handles both. Consequence worth documenting: a test project inside a submodule
  resolves to the **submodule's** root and gets its own ledger — correct (a submodule is its own
  repository) and consistent with §3.2's suite scoping, but surprising if undocumented.

### 2.7 What remains unmeasured — and what §2.8–§2.10 closed
**Nothing blocking.** The same-framework repeat on a volatile-id suite is now *confirmatory rather
than load-bearing* (§2.10 closed its question from existing data). (The template sweep
that used to sit here is §2.8, and is done.) Attempting the first on BreakfastProvider found the practical obstacle worth recording: its component tests run
with `EnableDockerInSetupAndTearDown: false` and expect **eight docker-compose stacks** (database,
storage, fakes, messaging, eventhub, prometheus, grafana, jaeger) to be up already — so M0.2 is a
scripted environment bring-up, not a `dotnet test` invocation, and should be budgeted as such.

### 2.8 The template sweep: twelve layouts, one answer, and it is not the one M0 expected
**Done, and it collapses.** All twelve `kronikol-*` templates were read: **not one sets a folder
path.** *(Re-checked after M2.8 modified `Kronikol.Templates.csproj` and the skill assets the same
afternoon — the finding survives: the templates now ship the agent skill and a `CLAUDE.md`/`AGENTS.md`
block by `PackagePath` from one canonical copy, but still configure no report location.)* Every template's setup file (`TestRun.cs`, `BDDfyTestSetup.cs`, `ConfiguredLightBddScope.cs`
or `TestSetupHooks.cs` — four shapes across the twelve) constructs `ReportConfigurationOptions` with
only `SpecificationsTitle` and `SeparateSetup`, so all twelve inherit the `BaseDirectory`-relative
default and land in `bin/Debug/net10.0/Reports/`. **Twelve templates are one layout**, and §6.2's
walk behaves identically for all of them. M0 item 3 shrinks from a sweep to a sentence.

The risk is somewhere else, and the sweep is what exposed it. **No template ships a `.gitignore`, a
`.sln`, or anything else implying a repository** — so the natural first run,
`dotnet new kronikol-xunit3` in a fresh folder, finds **no `.git` to walk to**, falls through to
§6.2's step 4, and produces *no history at all* behind a single diagnostic. That is the first
experience of a person evaluating the feature, and it is silent.

Two changes follow, both cheap:

- **Root discovery accepts an existing `.kronikol/` directory as a root marker, not only `.git`.**
  `kronikol history init` then works in any folder, and the answer to "why is there no history?" is
  a command rather than "put your project in a git repository". The walk order stays `.git` first,
  so a `.kronikol/` inside a repo cannot shadow the repo root.
- **The `HistoryUnavailable` diagnostic must name the fix**, not the condition: *"no repository root
  above `<dir>` — run `git init`, or `kronikol history init`, or set `HistoryFilePath`."* §5.14's
  rule that a silent absence is worse than a loud one applies most sharply here.

### 2.9 Identity volatility, and the escape hatch Kronikol does not have
A `stableId` is only stable if everything feeding it is. `ScenarioStableId.Compute` hashes
`feature::[outlineId::]name::k=v|…` over **every** example value, ordinal-sorted by key. A scenario
parameterised by a timestamp, a GUID or a random seed therefore mints a **fresh id every run** — it
reads as `new` forever, never accrues history, and grows the roster without bound. Silent, and
exactly the failure a history feature must not have.

**Measured first, before designing anything for it.** `tools/history-bench/volatile.py` scans real
reports for example values that look run-varying (GUID, hex-32, ISO timestamp, epoch-ms, long digit
runs):

> **1,195 scenarios across the six BreakfastProvider suites; 325 parameterised (27.2%); zero
> run-varying example values.**

So this is a real hazard with **no instances in a large real corpus** — which is the useful shape of
answer, because it says *do not spend M1 on it* while still naming what would go wrong.

**Prior art says the hazard is real enough to have been solved twice.** Allure's `historyId` was
read at source (`allure-js-commons/src/sdk/reporter/utils.ts`, `getTestResultHistoryId`) rather than
from documentation, and the comparison corrects this plan's earlier summary:

| | Allure | Kronikol |
|---|---|---|
| Base | `testCaseId ?? md5(fullName)` | `sha256(feature::[outline::]name)` |
| Parameters | `md5` of `name:value` pairs, **sorted**, joined `,` | `k=v` pairs, **sorted ordinal**, joined `\|` |
| Composition | `"<base>:<paramsHash>"` | one hash over the joined string |
| **Exclude a parameter from identity** | **yes — `p.excluded` is filtered out** | **no** |
| **Override the id outright** | **yes — a supplied `historyId` short-circuits everything** | **no** |

**Executed, not just read** (`tools/history-bench/allure/`, the real `getTestResultHistoryId`):

| Input | Resulting `historyId` |
|---|---|
| parameters `[b=2, a=1]` vs `[a=1, b=2]` | **identical** — order does not matter, both sort |
| `seed=91823` **`excluded: true`** | `…:9ef12b2e` |
| same, `excluded` removed | `…:56e1bdf8` — the parameter now counts |
| **`seed=55555`, still `excluded: true`** | **`…:9ef12b2e`, unchanged** — a volatile parameter is fully neutralised |
| `historyId: "pinned-across-renames"` | returned **verbatim**, short-circuiting everything |
| test renamed, no override | base hash changes `bf3a1025…` → `77a34811…` |

The last row is the nuance reading had missed: **Allure is rename-fragile too**. Its rename survival
is not automatic — it comes from a user explicitly setting `historyId`. So the free-chain criticism
in §8.6 applies to Allure as well, and Kronikol's alias-based approach (§7.4) is a *third* design,
not a copy of Allure's. The first three rows are the same idea in different notation — both sort their parameters, so this
plan's summary of *"`historyId` ≡ `stableId` in construction"* was right as far as it went. **It did
not go far enough:** the last two rows are a gap it never mentioned, and they are the two mechanisms
Allure has precisely because volatile parameters happen in the field. A claim can be accurate and
still be the wrong size — which is the §17.0 failure mode wearing a different coat, and the reason
that row moves from DOC to READ rather than staying a citation to prose.

**What this plan does about it — detection, not a new identity.** Changing `stableId` is out: it is
already the report's anchor (`#sid-` links), the merge key and the query key, so an option that
altered it would break links in previously generated reports for a hazard with zero measured
instances. Instead:

- **`history doctor` reports volatility directly** — any id present in exactly one run, across a
  window where the suite was otherwise stable, is listed as *"mints a new identity every run; will
  never accrue history"*. That is a fact the ledger already contains; it costs a query, not a format
  change.
- **The same signal caps the roster.** §3.4's `history prune` drops ids seen once and not for N runs,
  so a volatile scenario cannot grow the file unboundedly even if nobody reads the diagnostic.
- **An escape hatch is deferred to M7 and named in §14 as deliberately absent for now**, with
  Allure's two designs recorded as the prior art to copy if a user ever reports it. The cheaper of
  the two is the exclusion flag; the override needs somewhere for a user to put it, which Kronikol
  does not have today.

### 2.10 The residual is harness difference, not drift — closed without a re-run
§2.4 left one gate: the 3.4% of scenarios whose fingerprints disagree might be *drift* (the same
harness producing different shapes over time, which would make the whole feature unreliable) or
*harness difference* (different harnesses genuinely doing different things, which is irrelevant to a
single-suite ledger). The discriminator does not need new runs: **drift is unstructured and would
scatter disagreement randomly; harness difference is structured and disagrees on the same scenarios
every time, for identifiable reasons.** `tools/history-bench/pairwise.py` computes it.

| `shapeSet` (primary) | NUnit | TUnit | xUnit |
|---|---|---|---|
| **NUnit** | — | 96.6% | 97.9% |
| **TUnit** | 96.6% | — | **98.6%** |
| **xUnit** | 97.9% | 98.6% | — |

Across all three pairs, exactly **five distinct scenarios** disagree, and every one has an identified
cause:

- **Four differ in raw call count.** `52183df5…` makes **6** calls under NUnit and **2** under
  TUnit/xUnit; `b7a8bdb7…` is the mirror image, **2** under NUnit and **6** under the others;
  `a10fedfc…` is 18/20/18. A harness that makes three times as many HTTP calls is not drifting, it is
  a different test.
- **The fifth is fixture data.** `a2ff9c48…` has an identical 8-entry shape in all three except one
  segment: `/ingredient-usage/ingredient/**Sugar**-{id}` under NUnit against `…/**Flour**-{id}` under
  TUnit and xUnit. The templater did its job — it normalised the id and kept the ingredient, which is
  exactly the behaviour §2.4 conclusion 2 insisted on. The harnesses simply seed different data.
- **Zero are unexplained.**

**So the residual is entirely harness difference, and M0.2 stops being a blocking gate.** The
structure supports it independently: TUnit and xUnit — the two most similar harnesses — agree best
(98.6%), and NUnit is the outlier on precisely the scenarios where it makes a different number of
calls. `shapeOrdered` is consistently 2–3 points worse than `shapeSet` across every pair, which is
the same 2.8-point gap §2.4 measured and confirms `shapeSet` as the primary signal.

*Honest limit:* this proves every **observed** disagreement has a non-drift cause over a nineteen-
minute window. It does not prove drift is zero over weeks. A same-framework repeat remains worth
doing as cheap confirmation — it is simply no longer the thing M6 waits on.

> **An implementation detail this surfaced, from looking at real interaction rows.** Each HTTP call
> appears as **two** entries, request and response, and the request half carries an **empty** status
> (`Caller|Breakfast Provider|POST|/ingredient-usage|` then the same line with `Created`). A
> fingerprint that hashes raw interaction rows therefore doubles its length and leaves half its
> status fields blank. §4's reduction must **pair the halves** — one fingerprint entry per logical
> call, carrying the response status — using `RequestResponseId`, which `InteractionEntry` already
> exposes as the exact pairing key.

---

## 3. The artifact

### 3.1 Format: append-only JSONL, roster-interned
The first draft specified a rewritten JSON document. **The prior-art pass changed it**: Allure moved
document → JSONL in a major version, and the reasons generalise.

| Property | Rewritten document | Append-only JSONL |
|---|---|---|
| Git diff of one run | whole file churns | **one added line** — *two* once the window is full and each run also drops the oldest |
| Two branches both append | whole-document conflict | **no conflict** under `merge=union` wherever there is a checkout; a two-line conflict server-side or without it (§5.10) |
| Concurrent writers | read-modify-write, lock-critical | **append under a short lock** |
| Killed mid-write (CI timeout, OOM) | **whole ledger corrupt, permanently** | last line dropped, rest intact |

The last row decides it: a test run is exactly the kind of process CI kills, and a history file a
single `SIGKILL` can destroy will be destroyed.

> **But do not cite Allure as precedent for that row — checked at source, and Allure does not
> actually have the property.** This plan justified JSONL partly by "Allure moved document → JSONL in
> a major version, and the reasons generalise". The format claim is true; the *implementation* is
> not an append. `AllureLocalHistory.appendHistory` (`allure3/packages/core/src/history.ts`) opens
> the file `r+`, creates a write stream at **`start: 0`**, copies the surviving tail over the top of
> the file from byte zero, writes the new entry, and `truncate`s. A sliding window implemented by
> shifting bytes down — **every run rewrites the whole file**, so a `SIGKILL` mid-write corrupts it
> exactly the way a rewritten document would.
>
> So the honest version: **JSONL-as-a-format is precedented; JSONL-written-by-`O_APPEND` is
> Kronikol's, and it is strictly better than the precedent.** §6.5's measured lock plus a real append
> gives the crash-safety row that Allure's own implementation does not. That is a point in the
> design's favour, but it has to be argued from §6.5's measurements rather than borrowed.
>
> Two further details from the same file, both useful:
> - **`limit` is `undefined` — unlimited — by default**, matching `github-action-benchmark`'s
>   `max-items-in-chart`. Two independent projects default to keeping everything, which is the third
>   independent vote for §5.10's finding that bounding the window is a read-side concern, not a
>   storage one.
> - Allure hit the concurrency problem too and solved it by **detection, not exclusion**:
>   `#throwUnexpectedReadError` compares `mtime` and raises *"The history file was modified outside
>   Allure"*. §6.5's `FileShare.None` + jittered retry is the stronger answer, and this is evidence
>   the hazard is real rather than theoretical.

JSONL's cost is repetition — a 16-hex id on every line. **Roster interning removes it without giving
up append-only:** a `roster` line declares the ordered scenario list and its hash; `run` lines
reference the hash and give results positionally. §2.2's sizes are measured with interning in place.

> **The roster key must be a content hash, and that is now a correctness requirement rather than a
> preference.** Under the `merge=union` driver §5.10 specifies, git resolves two branches' appends by
> keeping **both** sets of lines. Demonstrated: two branches that each added a roster under a
> *sequential* key (`r2`) merged cleanly into a file containing **two different rosters both named
> `r2`**, with run lines on either side of the merge pointing at whichever one a reader happens to
> resolve first — silent, and undetectable from the file. A content-derived key removes the failure
> by construction: the same scenario set yields the same key (so union **de-duplicates** the line —
> also demonstrated), and a different set yields a different key. **`hash` is `sha256(ids joined)`
> truncated, never a counter**, and `history verify` fails on a duplicate key with differing
> content.

```jsonc
{"t":"header","historyFormatVersion":1,"generator":"3.1.0","window":50}
{"t":"roster","hash":"r1","suite":"BreakfastProvider.Tests.Component.BDDfy",
 "ids":["a1b2…"],"names":["Pay with an expired card"],"features":["Checkout"],"slots":[0]}
{"t":"run","id":"gh:18273645:1","suite":"BreakfastProvider.Tests.Component.BDDfy","partial":false,
 "at":"2026-09-12T10:04:11Z","branch":"main","commit":"abc1234","provider":"GitHubActions",
 "url":"https://github.com/…/runs/18273645","shards":8,"roster":"r1",
 "results":"PPFPS?P…","attempts":"1111121…","durations":[1203,1190],
 "shapeSet":["a3f9"],"shapeOrdered":["7c10"],"errors":[null,"e1"],
 "errorText":{"e1":"Expected status 200 but got 500"}}
```

- **`results` alphabet:** `P` passed · `F` failed · `S` skipped · `B` bypassed · `A`
  skipped-after-failure · `?` **defaulted or unknown** (§5.11) · `.` **not present in this run**.
  Five `ExecutionResult` values exist; the first draft encoded two, and §7.2 depends on telling `S`
  from `.` from `?` because none of the three is a flip.
- **`suite`** (§2.3) and **`partial`** (§5.12) are not optional extras; without them the analysis is
  wrong rather than incomplete.
- **`errorText`** is per-line and local, so a dropped line loses nothing else.
- **Ordering is append position** (§5.13), never `at`.
- **Deterministic serialisation** — ordinal key order, invariant culture, LF, no BOM — so two
  platforms writing the same run produce the same bytes. (`.gitattributes` already has
  `* text=auto` and `*.json text`, which covers normalisation but not conflict behaviour, §5.10.)

The column-major projection the first draft described is not gone; it is a **render-time transform**
for the HTML payload (§8.1) and the `--json` envelope. One on-disk format, one in-memory model, one
compact projection.

### 3.2 Suite identity — **this is no longer this plan's to define**
**Overtaken by production on 2026-09-12/13, and the plan would otherwise have shipped a duplicate
option.** While this plan was being written, §2.3's finding was implemented *at source*: a new
`RunSuite` resolves the suite, `ReportConfigurationOptions.SuiteName` overrides it, and
`ScenarioStableId.Compute` now takes `suite` as its **first argument** — its doc comment cites this
plan's own 145-of-905 measurement as the reason.

So the ledger **consumes** `RunSuite.Resolve(options)` and this plan **must not introduce
`HistorySuiteName`**: two options naming the same concept, one of which silently re-keys the other's
ids, is exactly the kind of surface a plan should not add. Every `HistorySuiteName` in earlier drafts
is now `SuiteName`.

`RunSuite`'s actual algorithm, which the ledger inherits rather than re-specifies: `SuiteName` when
set, else the single `*.deps.json` in `AppContext.BaseDirectory` (the test project's build output),
else `Assembly.GetEntryAssembly()`, else **null** — with `testhost`, `testhost.x86`, `dotnet`,
`vstest.console`, `ReSharperTestRunner` and `Kronikol.Tool` rejected by name because they are the same
string for every project.

> **⚠️ And this creates a hazard that is precisely §2.9's, now caused by Kronikol itself.** A null
> suite reproduces the pre-suite id *byte for byte* — deliberately, so a caller whose suite cannot be
> resolved keeps the ids it has always had. The consequence for **history**, which nothing outside
> this plan has reason to consider: **if the probe resolves a name one run and null the next, every
> scenario's id changes at once and the entire history detaches silently.** The probe is not
> hypothetically fragile — it returns null whenever the output directory holds more than one
> `*.deps.json`, which a published or self-contained output does. Three consequences, all cheap:
> - **`Cross-Run-History.md` recommends setting `SuiteName` explicitly** for any repo using history.
>   It is one line of config and it removes the failure mode entirely.
> - **`history doctor` reports a whole-suite id turnover** — every id in a run absent from the
>   previous run, with a fresh set replacing them — as *"the suite name changed or stopped
>   resolving"*, which is the one diagnosis a user will never reach on their own.
> - **A run whose suite is null is recorded as such**, so the ledger can tell "no suite" from a suite
>   literally named nothing, and the doctor can say which runs were affected.

**What the ledger still owes, unchanged:** rosters, verdicts, trends and `absent` are **all scoped by
the suite**, one ledger file holds many suites, and nothing is ever compared across them.

**But the reason has moved, and saying so matters.** §2.3 justified scoping by collision — 16.0% of
ids shared across projects. With `stableId` now suite-qualified at source that collision is *mostly
gone*, so scoping is justified from here on by **roster and `absent` semantics**: two suites have
different scenario sets, and a scenario missing from suite B's run is not `absent` from suite A's
history. The collision argument survives only as the **residual** case — a run whose suite does not
resolve falls back to the pre-suite id and can still collide with another such run. That residual is
also exactly why the ledger records suite per run rather than trusting the id to carry it.

### 3.3 Run identity
1. `<provider>:<runId>:<attempt>` when CI metadata has a run id — GitHub `GITHUB_RUN_ID` +
   `GITHUB_RUN_ATTEMPT`, ADO `BUILD_BUILDID`. **This is what makes eight shards one run.**
2. `local:<yyyyMMddTHHmmssZ>:<short hash of machine+project>` otherwise.

> **⏳ TIME-SENSITIVE, and cheap only until 3.1.0 ships.** Checked in the working tree on
> 2026-09-12: LLM **M2.3 has landed** — `MapCiMetadataJson` now writes `ciMetadata` (before
> `features`, deliberately) plus `environment` into the standard JSON, and `ReportIndex` reads
> `CiProvider` / `CiBranch` / `CiCommitSha` / `CiRunId` / `CiRepository` / `CiPipelineUrl` /
> `EnvironmentOs` / `EnvironmentRuntime`. **But it emits exactly seven CI fields and `runAttempt` is
> not among them**, and 3.1.0 is **unreleased** (no `v3.1.0` tag; `Directory.Build.props` still
> says 3.0.86). So the batch §9.4 relies on is still open.
>
> **The edit, audited rather than estimated** — the earlier "one record field and one line" was an
> understatement, and a checklist is more useful than a slogan. Eight edits across five files:
>
> | # | File | Change |
> |---|---|---|
> | 1 | `CiMetadata.cs:13` | `string? RunAttempt = null` on the record — **optional, so every existing positional construction still compiles** (8 of them across src and tests) |
> | 2 | `CiMetadata.cs` `DetectGitHub` | `RunAttempt: getEnvVar("GITHUB_RUN_ATTEMPT")` |
> | 3 | `CiMetadata.cs` `DetectAzureDevOps` | ADO's equivalent — **variable name not verified here**; GitHub-only is acceptable for v1 (§0.1: ADO's own flaky handling is rerun-based anyway), and shipping `null` is honest where shipping a guess is not |
> | 4 | `ReportGenerator.cs:3520` | `MapCiMetadataJson` — the eighth field |
> | 5 | `ReportGenerator.cs:4213` | the XML `<RunId>` sibling |
> | 6 | `ReportGenerator.cs:4248` | the YAML line |
> | 7 | `ReportGenerator.cs:5299` | the XSD element declaration |
> | 8 | `MergeableReportReader.cs:410` + `Query/ReportIndex.cs:33` + `ReportScanner.cs:401` | read it back on the merge and query paths |
>
> **The cost is not the eight edits — it is that doing them later needs a compatibility story and a
> second Kronikol4J divergence entry, and doing them now needs neither**, because the format is
> unreleased and the schema is already being changed. `TestRunReportSchemaContractTests` enforces
> that every emitted key is declared, so the schema edit cannot be forgotten — the test fails
> instead. Being an *optional* positional parameter, it is a **minor**-bump addition under the house
> rule, and 3.1.0 is already minor, so it rides free.
>
> **This is an ask on in-flight work, not new scope; it wants raising before 3.1.0 is tagged** — and
> it is the user's call, not something to action unilaterally on an unreleased branch.

`GITHUB_RUN_ATTEMPT` is a one-field addition to `DetectGitHub` (§9.4). **Verified against GitHub's
variables reference (2026-09-12):** `GITHUB_RUN_ATTEMPT` exists — *"A unique number for each attempt of a particular
workflow run… begins at 1"* — and, decisively, `GITHUB_RUN_ID` *"does not change if you re-run"*. So
run id alone collapses a re-run onto its original, silently discarding exactly the evidence a
flakiness feature exists to collect. The attempt is not a nicety; without it, re-running a failed job
until it passes **erases the flake from history**.

### 3.4 Reading, pruning, compaction
Read = stream lines, keep the last `window` run lines *per suite* and the rosters they reference.

> **The reader needs a retry budget too — measured, and the previous draft did not mention readers at
> all.** §6.5 settled writer-versus-writer. The realistic second case is a **reader during a write**,
> because §2.3's whole premise is several test projects sharing one ledger and `dotnet test` runs
> projects in parallel: project A appends its run while project B reads the ledger to embed history in
> its report. `tools/history-bench/readtest` runs exactly that — one writer appending 150 realistic
> ~4 KB lines, one reader looping:
>
> | Writer | Reader | Reads | **Denied** | Torn tail |
> |---|---|---|---|---|
> | `FileShare.None` | `None` | 53,963 | **5,015 (9.3%)** | 0 |
> | `FileShare.None` | `Read` | 47,551 | **4,742** | 0 |
> | `FileShare.None` | `ReadWrite` | 131,117 | **85,199** | 0 |
> | `FileShare.ReadWrite` | `ReadWrite` | 1,850 | 0 | 0 |
>
> Two results. **A reader is locked out roughly a tenth of the time** under contention — frequent
> enough to be a user-visible intermittent failure, and the one combination with zero denials is
> precisely the unlocked mode §6.5 proved loses a third of all lines. So: **the reader opens
> permissively (`FileShare.ReadWrite`) and retries on `IOException` with the same jittered budget as
> the writer**, and on exhausting it **degrades to "no history" plus a `HistoryUnavailable`
> diagnostic — never a failed test run and never a failed report** (§6.2 step 4's rule, applied to
> reads). The reader always caught up eventually: every combination observed the newest run.
>
> And **zero torn tails in ~380,000 reads**, because an append of one ~4 KB line plus
> `Flush(true)` is not observed half-written. That makes §3.4's "skip the unparseable line" a genuine
> belt-and-braces rather than the primary defence — worth knowing, because a design that *relied* on
> tail-skipping would be relying on something that has never fired. It stays, since a `SIGKILL`
> mid-write is still the case it exists for.
`history prune` rewrites without leading lines; `history compact` folds stale rosters and drops
`errorText` outside the window. An unparseable line is skipped with a counted warning
(`HistoryLedgerDamaged`), never fatal.

### 3.5 Reserved at v1, and what happens when v2 arrives
`attempts`, `shapeSet`, `shapeOrdered`, `slots`, `shards`, `partial`, `suite` — all written from v1
with a defined unknown encoding, so the milestones that surface them need no version bump.

**The version story itself needs stating, because the obvious precedent is half wrong.**
`mergeableFormatVersion` is refused by printing an error and **exiting 1**
(`QueryCommand.cs:71-75`). That is right for a command a user explicitly invoked and cannot be
served. It is **exactly wrong for the capture path**, where §6.2 step 4's rule is absolute: a test run
must never fail because of history. So the two paths diverge deliberately:

| Situation | Behaviour |
|---|---|
| `kronikol query history` meets an unknown `historyFormatVersion` | error + **exit 1**, "upgrade Kronikol.Tool" — the `mergeableFormatVersion` precedent, verbatim |
| A **test run** meets an unknown version while reading | no history in the report, one `HistoryUnavailable` diagnostic, **run unaffected** |
| A **test run** meets an unknown version while **writing** | **do not append.** Keep the fragment (§3.6), diagnose, exit 0 |

That third row is the one worth being explicit about. A newer Kronikol in one project and an older one
in another — a monorepo with a pinned dependency, or a half-finished upgrade — is ordinary, and an
older writer appending a v1 line into a v2 ledger produces a file **neither version reads correctly**
and which no diagnostic would catch later. Refusing to write costs one run of history; appending
corrupts the file permanently. The fragment survives either way, so a later run with the right version
folds it.

**Forward compatibility is a read rule, not a migration:** a vN reader understands every version ≤ N.
Migration is **explicit only** — `history compact` rewrites to the current version and says so.
Nothing migrates silently during a test run, because a silent rewrite would destroy exactly the
properties §3.1 and §5.10 are built on: the append-only shape git deltas cheaply, and the
`merge=union` behaviour that depends on lines being added rather than moved.

### 3.6 The one-run fragment
Each run also writes `History.run.json` into the reports directory — one run line's worth, no window,
no prior data. Race-free (one process, its own directory), automatically part of the CI artifact, the
unit `history record` folds, and the shard unit.

---

## 4. The thing nobody else can build: behaviour history, not status history

§0.1 establishes that flip-rate flakiness is table stakes. **This is the part that is not.**

Every competitor's history is a column of pass/fail, because that is all they capture. Kronikol
captures **what the test did** — every HTTP call, SQL statement, message and gRPC invocation, in
order, with status codes. So its history can answer a question no funded SaaS and no JUnit-XML Action
can:

> *"This scenario has passed twenty runs in a row. At run seventeen it started making a second call
> to `POST /inventory/reserve`, and nobody noticed."*

Green test, changed behaviour: a retry loop that starts firing, an N+1 that appears when an
`.Include()` is dropped, a cache that stops being hit, a dependency wired in by a constructor change.
In a mature suite this is the most expensive regression class there is, and it is invisible to
pass/fail.

The mechanism is §2.4's two fingerprints, stored per scenario per run. From them:
- `shapeSet` differs, status unchanged → **behaviour changed**;
- call count differs → the cheap, explainable half, reported in words (`7 calls → 8`);
- `shapeOrdered` differs while `shapeSet` matches → **reordered** (off by default — §2.4 measured
  this as usually concurrency, not change);
- a `caller→service` pair appears that was in no prior run → **new dependency**, the component
  diagram's version of the same idea.

**One construction detail, from reading real interaction rows (§2.10):** each call appears as
**two** entries, request and response, and the request half carries an empty status. The reduction
pairs them via `RequestResponseId` — one fingerprint entry per logical call, carrying the response
status — or the fingerprint doubles in length with half its status fields blank.

Hazards, both now evidence-based rather than anticipated:
- **Templating quality decides everything** — §2.4 measured the ladder, supplied the regexes, and
  deleted the rule that would have caused false negatives.
- **Some scenarios are legitimately nondeterministic.** A `shapeSet` that changes in more than half
  the window self-suppresses as `unstable-shape`.

---

## 5. Constraints the design must survive

**5.1 The reports directory is inside `bin/` and is gitignored.** `ResolveReportsDirectory`
(`ReportGenerator.cs:66-72`) resolves against `AppDomain.CurrentDomain.BaseDirectory`, so the default
is `bin/Debug/net10.0/Reports`, covered by `.gitignore:29` (`[Bb]in/`) — confirmed with
`git check-ignore`. **The ledger cannot live there**: a `dotnet clean`, a fresh clone or a CI checkout
wipes it and it can never be committed. Any "write history next to the report" design ships a feature
that silently does nothing in CI, the only place it matters. This dictates §6.

**5.2 There is no repo-root discovery in the codebase.** One `AppContext.BaseDirectory` use
(`Kronikol.xUnit2/ReportingTestFramework.cs:197`). Walking up for `.git` is new code.
**Verified locally (2026-09-12)** by creating a worktree: the main checkout's `.git` is a
**directory**, the worktree's `.git` is a **file** whose content is `gitdir: <main>/.git/worktrees/<name>`.
So the walk must accept **either**, and a walk testing `Directory.Exists` alone finds nothing in a
worktree and silently disables history there. Consequence worth documenting rather than discovering:
a worktree's root is the *worktree* directory, so it gets its own `.kronikol/` — correct for the
committed ledger (the checkout carries its own copy) and empty for the cache path. Submodules and
"no repo at all" each get a test.

**5.3 A run is not a file.** A sharded build is one run written by eight processes; a developer
running twice is two runs with no CI metadata. §3.3.

**5.4 Shards must fold as one run.** `history record` groups fragments by run id (§6.3b), proved in §12.

**5.5 `stableId` collides within a run.** The ledger stores a list per id, positionally matched, as
`query diff` does. One slot per id silently drops `[Theory]` rows.

**5.6 `stableId` collided across projects** — 16.0%, measured (§2.3), and **fixed at source**: ids are
now suite-qualified (§3.2). What remains for the ledger is the residual (a run whose suite does not
resolve keeps the old id) and the new hazard that **a change in suite resolution re-keys everything at
once** — which `history doctor` must name, because nobody will diagnose it unaided.

**5.7 `stableId` does not survive a rename.** §7.4, M7.

**5.8 A test that passes on retry is flaky now.** Only Cucumber models attempts; the field is
recorded for every path from v1, and stays a *secondary* signal (§0.1).

**5.9 Branches pollute each other.** Every run carries its branch; every read filters. §7.5.

**5.10 A committed ledger is a file two pull requests will both touch.** The previous draft listed
"a `.gitattributes` entry" as a mitigation without saying which one or what it buys. Measured, in a
throwaway repo, four ways:

| Scenario | Result |
|---|---|
| Two branches each append one run, **no** `.gitattributes` | `CONFLICT (content)` — but exactly the trivial two-line conflict §3.1 predicted, both runs visible between the markers |
| Same, with `merge=union` | **auto-merged, both runs kept, in order** |
| Union, and both sides appended the **identical** line | **de-duplicated to one line** — git treats it as the same change |
| Union, under `core.autocrlf=true`, with `text eol=lf` | LF preserved in **both** working tree and object store — §3.1's byte determinism survives Windows |
| Union, `git rebase` rather than `git merge` | clean, order preserved |

### What a committed ledger actually costs a repository — measured

The acceptance half of that question is answered in §6.3d2 by revealed preference; this is the
**cost** half, and it is a number. `tools/history-bench/gitgrowth.py` commits
one run per build against a 203-scenario ledger and measures `.git` before and after `gc`:

| Design | Working file | `.git` loose | `.git` **packed** | Per build |
|---|---|---|---|---|
| JSONL, window 50 — **what this plan ships** | 180 KB | 11.9 MB | **0.69 MB** | 1.8 KB |
| JSONL, unbounded | 1.4 MB | 45.2 MB | **0.45 MB** | 1.1 KB |
| Rewritten JSON document, window 50 | 365 KB | 15.2 MB | 0.89 MB | 2.3 KB |
| Rewritten JSON document, unbounded | 2.8 MB | 59.1 MB | 0.49 MB | 1.2 KB |

*(400 builds; the same run at 250 builds gives the same per-build figures, so it is linear.)*

Three things fall out, two of which contradict what this plan assumed:

1. **The absolute cost is small enough to stop worrying about.** 1.8 KB of packed history per build.
   Ten builds a day for a year is **~6.5 MB** — less than one screenshot-heavy commit. The objection
   to a committed ledger is about *taste and review noise*, not about repository size, and §15.1
   should say so with this number rather than reassure.
2. **Pruning makes the repository bigger, not smaller.** Window 50 costs **0.69 MB** where unbounded
   costs **0.45 MB** — 55% more, consistently at both scales. Dropping the oldest line each commit
   shifts the whole file and breaks git's delta chains, while a pure append deltas almost perfectly.
   **`HistoryWindow` is a read-cost and working-tree control, not a storage one** (§2.6 already
   showed scanning is file-bounded), and for the committed ingress specifically a *larger* window is
   cheaper in the repository. Q2's answer of 50 stands on read cost and reviewability; it must not be
   defended on repo size, which it makes worse.
3. **§3.1's storage case for JSONL is weaker than that table implies** — 21% at window 50 and 8%
   unbounded. JSONL is still right, but for the two rows that *were* measured: surviving a `SIGKILL`,
   and merging under `union`. The honest framing is "same storage, incomparably better failure and
   merge behaviour", not "smaller".

One operational note: **loose objects run 17–100× the packed size** (45 MB against 0.45 MB) until a
`gc`. Each ledger commit creates exactly **three** loose objects — commit, tree, blob — measured
(60 objects over 20 commits), so the count grows predictably. It does not affect CI, which clones
fresh; the exposure is a developer who pulls a year of ledger commits and never gcs, and `git gc`
reclaims it instantly. Git's own `gc.auto` should repack long before it matters, though that
threshold is taken from git's documentation rather than measured here. Worth one line in the FAQ,
not a design change.

So the `.gitattributes` entry is not a mitigation, it is the fix, and it is exactly one line:

```gitattributes
KronikolHistory.jsonl merge=union text eol=lf
```

`merge=union` is a **built-in** git driver, so it needs no `merge.*.driver` config and works on a
fresh clone with nothing installed. Three obligations follow, none optional:

- **The roster key must be content-derived** — union's line-keeping semantics corrupt a counter-keyed
  roster silently (§3.1).
- **Union can reorder relative to wall-clock**, since it keeps ours-then-theirs. §5.13 already says
  ordering is append position and never `at`; under union, append position is *merge* position. Any
  analysis that assumes monotonic `at` across the file is wrong — the tests must include a
  union-merged ledger as a fixture, not just a linearly-appended one.
- **`.gitattributes` must ship in the recipe**, because a repo that commits the ledger without it
  gets a conflict on every concurrent PR and will conclude the feature is unusable. `history init`
  writes the line (and says so) rather than leaving it to documentation.

> **⚠️ The union driver does not apply to server-side merges, and finding out how it resolves
> attributes is what showed it.** The previous paragraph listed GitHub's merge button as an open
> question. It is now characterised, by running `git merge-tree --write-tree` — the no-worktree
> plumbing a server-side merge uses:
>
> | Context | `.gitattributes` present as | Result |
> |---|---|---|
> | Normal clone, `merge-tree` | checked out **and** committed | **clean union merge** |
> | Same clone, file **deleted from the worktree** (still committed) | committed only | **conflict** |
> | **Bare** repository | committed only | **conflict** |
> | Bare repository, `-c attr.tree=HEAD` | committed only | **clean union merge** |
>
> **The attribute is read from the working tree, not from the commit.** Deleting the checked-out
> file while it remains committed is enough to lose the behaviour. A bare repository has no working
> tree, so the committed `.gitattributes` is invisible to the merge unless `attr.tree` is set
> explicitly (git ≥ 2.40), and that is **off by default**.
>
> So the honest position, which is narrower than the one this plan held for two drafts:
>
> - **Union works everywhere there is a checkout** — `git merge`, `git rebase`, `git pull`, and any
>   CI job that clones and commits back. That is the common path, and it is genuinely solved. A fresh
>   clone checks the file out, so no setup step is needed.
> - **Assume it does NOT work for GitHub's merge button**, or its squash and rebase-merge variants,
>   until someone proves otherwise: they run server-side against bare repositories, and nothing
>   suggests `attr.tree` is set. §15.1 must **not** promise conflict-free merges to a repo that
>   merges through the web UI.
> - The fallback there is the two-line conflict — survivable and visible, not silent (§3.1).
>
> M0.6's experiment is unchanged but now has a precise hypothesis rather than an open question:
> two concurrent PRs, merged through the web UI, are **expected to conflict**. If they do not,
> GitHub sets `attr.tree` and the recipe gets better; if they do, the wiki says so plainly.
>
> **How much this actually costs, stated so it is not read as worse than it is:** the documented
> recipe already has **only the default branch write the ledger** (§6.3b). Under that rule a pull
> request never touches the file, so no PR merge — server-side or otherwise — can conflict on it.
> Union therefore protects the pattern the docs *don't* recommend (every branch writing) on the path
> where it *does* work (local merges), and the recommended pattern needs no protection at all. The
> correction matters because the wiki would otherwise have promised something untrue to the team that
> ignores the recommendation, which is exactly the team that would hit it.

**5.11 A defaulted result is not a pass.** `ResultWhenUnknown` defaults to `Passed`
(`IngestPipeline.cs:63`), so a crashed worker's scenarios render green. Writing those into the ledger
**permanently poisons the trend**. The rule is absolute: **record `?`, never `P`**, keyed off the
existing `ResultDefaulted` diagnostic, and exclude `?` from flip-rate denominators.

**5.12 A filtered run is not a shrunken suite.** `dotnet test --filter`, an IDE running one class, or
a crash halfway all produce a strict subset of the roster. Without a signal, every other scenario
reports `absent` and the trend fills with holes. So: a run whose present set is a strict subset of
the prior roster is marked `partial` — explicitly via `--partial` / `HistoryPartialRun`, and
otherwise by a **heuristic** (more than `HistoryPartialThreshold`, proposed 10%, of the prior roster
missing) which records `HistoryPartialRun` and suppresses `absent` for that run. Shards are exempt:
they fold before analysis (§5.4).

**5.13 Clock skew.** Runs are ordered by **append position**, never by `at`.

**5.14 Cold start.** With two runs everything is "new" and a single flip is 100%. §7.6.

**5.15 Size, and the fact that it is reviewed.** Measured in §2.2. Small, deterministically ordered,
append-mostly.

---

## 6. Where the ledger lives, and the ways it gets there

**6.1 Principle.** One format, five doors — one local, three CI, one backfill — and no preference
forced on the user, because every team's CI is different. Kronikol never
opens a socket on any of them (§6.4).

**6.2 Resolution order** (new: `HistoryFilePath`):
1. `ReportConfigurationOptions.HistoryFilePath` — absolute, or relative to `BaseDirectory` like
   `ReportsFolderPath`. **Named for the house convention, checked against the options record:** it
   uses `ReportsFolderPath`, `HtmlTestRunReportFileName`, `YamlSpecificationsFileName` — `*FolderPath`
   for directories, `*FileName` for names. A bare `HistoryPath` (the previous draft's name) matches
   neither, and this is a full path to a file. Booleans follow the existing `Generate*` / `Write*`
   split too: `GenerateHistoryFragment` and `WriteHistoryLedger`, not `UpdateHistory`.
2. `KRONIKOL_HISTORY` — the CI form that needs no code change. **Following the `KRONIKOL_BASELINE`
   precedent that landed in M2.4** (`BaselineDiffTests`), not inventing an idiom: same `KRONIKOL_*`
   shape, same "explicit, then convention, then environment" ordering, and the same rule that when
   nothing resolves the error **names every place it looked**.
3. Repo root by walking up from `BaseDirectory` for a `.git` entry (file **or** directory) →
   `<repo>/.kronikol/history.jsonl`; **failing that, an existing `.kronikol/` directory**, which is
   what makes the feature reachable from a template instantiated outside a repository (§2.8). `.git`
   is checked first at every level, so a nested `.kronikol/` can never shadow the real root.
4. Nothing found → **no ledger, no error**, one `HistoryUnavailable` diagnostic. A test run must never
   fail because history could not be written. **The diagnostic names the fix, not the condition** —
   §2.8 measured that the most likely way to reach step 4 is a brand-new template project in a
   folder that is not a repository, which is precisely a first-impression moment.

The asymmetry with `ReportsFolderPath` is deliberate: reports belong to a build and live in `bin/`;
history belongs to the repository and must outlive `bin/`.

**6.3 The paths.** (The previous draft listed three and wrongly dismissed a fourth — §6.4.)

**(a) Local — automatic.** Write the fragment, then append **under an exclusive `FileShare.None`
lock with jittered retry** — measured as both necessary and sufficient on Windows and Linux (§6.5);
without it a third of run lines vanish silently. If the lock is never won the fragment stays, a
diagnostic is recorded, and the next run folds it. Self-healing, and never silent.

**(b) CI, artifact relay — the recommended default.** Each run uploads its **~5 KB
`History.run.json` as its own artifact** with its own retention; the next run downloads the last N
and folds. Every mechanic here is verified (2026-09-12):
- `actions/upload-artifact` `retention-days`: *"Minimum 1 day. Maximum 90 days unless changed from
  the repository settings page."* So the fragment can persist **90 days** — against Kronikol's own
  `CiArtifactRetentionDays = 1` default (`ReportConfigurationOptions.cs`), which is right for
  megabyte reports and fatal for history. **The fragment must be a separate artifact with its own
  retention**; sharing the reports artifact means yesterday's history is already gone.
- `actions/download-artifact` takes `run-id`, `repository` and `github-token`, documented as
  *"required when downloading artifacts from a different repository **or from a different workflow
  run**"*. Cross-run download is a first-party supported input, not a workaround.
- The token is `${{ github.token }}` — provisioned per run, no user-managed secret — with a
  `permissions: actions: read` block. Note it is **not** an ambient env var; it is wired into the
  step.
- Which run to fetch: `gh run list --branch main --status success --limit 1 --json databaseId`
  (`gh` is preinstalled on GitHub-hosted runners).

**(c) CI, cache.** `actions/cache` keyed on branch with restore-keys falling back to `main`.
Verified rules, and one of them is disqualifying for long windows:
- A run **can restore caches from its own branch and the default branch**, and a pull-request run
  can also restore from its **base** branch. So a PR reading main's history works. ✅
- A cache created by a PR run is scoped to the **merge ref** and *"can only be restored by re-runs of
  the pull request"* — so PR writes cannot pollute `main`. ✅
- Fork PRs are **read-only** against base-branch caches — which is the behaviour history wants
  anyway. ✅
- **GitHub removes cache entries not accessed in over 7 days**, and evicts oldest-access-first
  against a 10 GB per-repository limit. ❌ **A quiet fortnight silently empties the window**, and the
  window this plan is named after is a fortnight. Cache is the convenient option, not the durable one,
  and the wiki must say so.

**(d) Commit-back on the default branch only.** Survives forever, gives §0's cross-branch answer free,
costs one bot commit per main build, and avoids §5.10's conflicts by having only one writer. **(d2)
below avoids them more thoroughly and should be read before choosing this one.**

**(d2) Commit-back to an orphan *data branch* — added after looking at what teams already do, and it
is better than (d) on every axis except discoverability.** §17.2 listed *"teams will accept a
machine-written file in their repo"* as unevidenced. It is now evidenced, and the evidence came with
a design attached.

> **Revealed preference, counted.** Workflow files on GitHub referencing actions whose whole purpose
> is letting CI write into the repository: `peter-evans/create-pull-request` **40,640**,
> `stefanzweifel/git-auto-commit-action` **37,504**, `EndBug/add-and-commit` **14,848**. And the
> direct analogue — `benchmark-action/github-action-benchmark`, which keeps **append-only
> machine-written performance history** committed by CI and alerts on regressions — appears in
> **2,700**. (Code-search totals are approximate and cover indexed public repos only, so these are
> lower bounds.) The objection is not that teams refuse machine-written commits. Tens of thousands
> accept them.
>
> **But note where the history one puts them.** `github-action-benchmark`'s default is
> `gh-pages-branch: gh-pages` with `benchmark-data-dir-path: dev/bench` — **a separate branch, never
> the main line** — plus `gh-repository` for a different repo entirely, and
> `external-data-json-path` for "store the file anywhere yourself". It also defaults
> `max-items-in-chart` to *no limit*, which independently corroborates §5.10's finding that not
> pruning is fine, and ships `fail-on-alert` + `alert-threshold` + PR comment + job summary — the
> same four surfaces as §8. An unrelated project converged on nearly this whole design, and the one
> place it differs is the one this plan had not considered.

**The recipe, verified end to end rather than sketched:**

| Step | Command | Verified |
|---|---|---|
| Create | `git checkout --orphan kronikol-history` (shares **no** history with `main`) | `main` contains only its own files |
| Clone | — | a fresh clone checks out **only** `main`; the data branch is not even a local branch |
| Read without checkout | `git fetch origin kronikol-history:refs/remotes/origin/kronikol-history` then `git cat-file -p origin/kronikol-history:KronikolHistory.jsonl` | ✅ ledger read, worktree untouched |
| Write from CI | `git worktree add ../hist kronikol-history`, append, commit, push | ✅ main worktree stays on `main`, contents unchanged |
| Two jobs racing | union merge inside the data worktree | ✅ **clean** — it is a real checkout, so §5.10's attribute applies |

What it buys, against (d):

- **Never appears in a pull request diff.** The review-noise objection — the actual objection, now
  that size is measured at 1.8 KB a build — disappears entirely rather than being mitigated.
- **Never conflicts with a feature branch**, because feature branches do not contain the file. §5.10
  stops mattering, *including* the server-side-merge hole, since no PR merge ever touches it.
- **Still answers §0's cross-branch question from a checkout** — one fetch, no API, no token beyond
  the one the job already has, no infrastructure.
- **Kronikol writes nothing special for it.** The plumbing is `git worktree`/`commit`/`push` in a
  workflow, so §14's "Kronikol opens no socket" is untouched.

What it costs: the ledger is **not visible in the working tree**, so "why is there no history in my
repo?" becomes a real support question — answered by `kronikol history show` performing the fetch
itself, and by the page saying plainly which branch it lives on.

**(e) Backfill — `kronikol history import <dir>…`.** Builds or extends a ledger from prior
`TestRunReport.json` files; run entries reconstructed from `ciMetadata` + `startTime`.
> **One discontinuity to handle, created by §2.3's fix.** Reports written **before** suite-qualified
> ids carry unsuffixed `stableId`s; reports written after carry `suite::…` ones for any run whose
> suite resolves. Importing the two into one ledger therefore produces **two disjoint id sets for the
> same scenarios**, which reads as "the whole suite was replaced" exactly once, at the version
> boundary. `import` must detect it — the boundary is visible, since the older reports predate the
> field — and either **re-key the old ids forward** using the suite it is importing into, or refuse
> and say why. Silently interleaving them is the one unacceptable option. This is the same turnover
> §3.2's `history doctor` check looks for, arriving by a different route. The direction
note's "reading an artifact folder", kept as a first-class door. **Also worth importing: Allure**
(`--from-allure`, name-matched, best-effort) and **JUnit XML** (§16 Q13), which is what every
competing tool consumes and what would let a team build history from runs predating Kronikol.

**6.4 "Reading CI history needs a token, so it is out of scope" — that was wrong, and here is the
correction.** The previous draft listed provider-API ingress under the anti-scope. Checking it
(§6.3b) shows the objection collapses: the token is automatic, cross-run download is a documented
first-party input, and `gh` ships on the runner. **The real distinction is not "API or no API" but
*who makes the call*.**
- **Out of scope, permanently:** Kronikol shipping an HTTP client, token handling, or
  provider-specific API code. That is what would make it one-provider, credential-bearing software,
  and it is what §14 forbids.
- **In scope, and now the recommended CI default:** the platform's own tooling fetches prior
  fragments into a directory, and `kronikol history record <dir>` reads them **offline**. Kronikol
  never opens a socket; the workflow does, using machinery it already has.

That framing generalises: Azure DevOps' `DownloadPipelineArtifact` with `runVersion: latestFromBranch`
is the same recipe, and a team on neither platform still has (d) and (e). The product stays offline;
the recipe does the fetching.

**6.5 The append must hold an exclusive lock — measured on both platforms, 2026-09-12.**

A previous draft guessed that .NET's `FileShare` might not enforce cross-process exclusion on Unix
and designed around it. **That guess was wrong, and the measurement found a worse problem than the
one it was guarding against.** 8 and 32 processes appending concurrently, run natively on Windows
(NTFS) and in a Linux container (Ubuntu 24.04, .NET 10.0.11) on both overlayfs and an ext4 volume:

| Platform / fs | Mode | Lines expected | Lines present | Torn |
|---|---|---|---|---|
| Windows NTFS | plain append, `FileShare.ReadWrite` | 320 | **235** | 0 |
| Linux overlayfs | plain append | 320 | 107–185 | 0 |
| Linux ext4 | plain append | 320 | **107** | **1 (mid-line splice)** |
| **Windows NTFS** | **`FileShare.None` + retry** | 320 | **320** | **0** |
| **Linux overlayfs** | **`FileShare.None` + retry** | 320 | **320** | **0** |
| **Linux ext4** | **`FileShare.None` + retry** | 320 | **320** | **0** |

And the realistic shape — 32 test projects each appending **one** run line, which is §5.9's scenario
exactly: **21 of 32 lines survive without the lock (34% lost); 32 of 32 with it, in 135 ms.**

Three conclusions, all now evidence:

1. **`FileShare.None` works for cross-process exclusion on Linux as well as Windows** — verified on
   overlayfs and ext4, at 4.3 KB and 128 KB lines. The lock is not best-effort decoration; it is the
   mechanism, and this plan was wrong to doubt it.
2. **The failure mode without it is silent loss, not corruption.** A naive append *believes it
   succeeded* while a third to two-thirds of lines vanish (both platforms seek to a stale end offset
   and overwrite each other). Corruption would at least be detectable; this is not. **Idempotence
   cannot fix it** — the previous draft's `(suite, runId)` dedupe protects against a *doubled* write
   and does nothing about a *lost* one, so it stays for the retry-after-ambiguity case and is no
   longer the primary defence.
3. **Lock starvation is detectable, and that is what makes the design safe.** Under deliberate stress
   (16 × 200 = 3,200 contended appends) three writers exhausted a 200-attempt budget — and *said so*.
   So the rule is: **retry generously with jitter; on give-up, keep the fragment, record a
   `HistoryUnavailable` diagnostic, and let the next run fold it.** Nothing is lost and nothing is
   silent. A run must never fail because the ledger was busy.

M0.5 keeps this as a regression harness on both OSes rather than a one-off, since the retry budget is
the only tunable and a future runtime change could move it.

**6.6 Order of operations.** Read ledger → compute verdicts → `RunOutputs` (HTML, digest, fragment) →
append. Read early because the HTML and digest need verdicts; write late so it records what reached
disk. An append that throws is caught like any other output failure.

---

## 7. What gets computed: one vocabulary, every surface

`HistoryAnalyzer` is a pure function — `(ledger, currentRun, options) → HistoryVerdicts` — with no IO,
shared by the HTML, digest, CLI, gate, CI summary, CTRF and MCP. **One computation, eight surfaces.**

### 7.1 Per-scenario verdict
| Verdict | Meaning | Reported with it |
|---|---|---|
| `new` | no prior record in the window *for this suite* | — |
| `broke` | failed now, passed in its previous run | previous run's commit/date |
| `failing` | failed now and in ≥2 prior consecutive runs | `since` run, count, days |
| `always-failing` | failed every run of the window, never green | run count |
| `fixed` | passed now, failed previously | how long it had been failing |
| `flaky` | ≥2 status flips in the window, or passed-on-retry | flips, fail rate, last failure |
| `stable` | passed throughout | run count |
| `slower` | above window p95 by ≥`HistorySlowerBy` for ≥2 consecutive runs | old p50 → new |
| `behaviour-changed` | `shapeSet` differs, status unchanged | call-count delta |
| `reordered` | `shapeOrdered` differs, `shapeSet` matches — off by default (§2.4) | — |
| `unstable-shape` | `shapeSet` changes in >50% of the window | suppressed from drift |
| `quarantined` | listed in `.kronikol/quarantine.json` | reason, who, when |
| `absent` | in prior runs of this suite, not in this one, run not `partial` | last seen run |
| `unknown` | this run's result was defaulted (§5.11) | the `ResultDefaulted` text |

`new` and `broke` are the two the direction note asked for; the other twelve are the difference
between a feature and a demo.

### 7.2 Two flakiness numbers, both explained in words
- **fail rate** = failures ÷ runs *with a real verdict*. A permanently broken test scores 1.0.
- **flip rate** = status transitions ÷ (those runs − 1). A permanently broken test scores 0.0.

**`flaky` keys on the flip *rate*, never on an absolute flip count — the §2.6 harness demonstrated
why.** A draft rule of "≥2 flips in the window" sounds conservative and is not: over a 50-run window a
test failing independently 4% of the time has an expected 2·p·(1−p)·(n−1) ≈ **3.8 transitions**, so it
clears a threshold of 2 almost always. In the synthetic run **4,265 of 5,000 scenarios were labelled
flaky** — a flakiness report that flags 85% of the suite is one nobody opens twice. An absolute count
does not scale with window length; a rate does. So `flaky` requires **flip rate ≥ `HistoryFlakyRate`**
(proposed 0.1, about one flip per ten runs) **and** at least 2 flips, the second only to stop a 3-run
window calling a single flip 50% flakiness.

> **Prior art, read at source rather than assumed — and it is a third design, not either of the two
> this section considered.** Allure 2's rule (`HistoryPlugin.java:120`) is neither an absolute count
> over the window nor a rate. It is a **bounded lookback**: take the previous **5** statuses, and call
> the test flaky when the current run failed *and* a `PASSED` appears before the last `FAILED`. In
> other words *"it passed recently, and it is failing now"*.
>
> That has two properties worth stealing and one worth refusing:
> - ✅ **It cannot be inflated by window length**, because the lookback is fixed at 5 regardless of how
>   much history exists — the same immunity a rate gives, by a different route.
> - ✅ **It is recency-weighted.** A test that flapped twenty runs ago and has been green since is not
>   flaky today; flip rate over a 50-run window still scores it. That is a genuine weakness of the
>   rate, and the answer is not to abandon the rate but to **report the window position** — "last
>   flipped 18 runs ago" — which §7.2's evidence rule already requires.
> - ❌ **It only fires when the current run failed.** A test that fails intermittently but happens to
>   pass today reports *not flaky*, which is precisely the test a developer most needs warned about
>   before they trust a green build. Kronikol's flip rate is status-independent and keeps that.
>
> So `HistoryFlakyRate` stays a rate (Q12b), and gains a companion the surfaces must print: **runs
> since the last flip**. Allure's rule is the cheap approximation of exactly that.

Flip rate is also the right primitive because a reliably red test is a bug, not a flake. **`S`, `B`, `A`, `?`
and `.` are not statuses for this purpose** — excluded from numerator and denominator, so skipping a
test for a week neither creates nor hides flakiness. Neither number is printed alone: every surface
says *"failed 4 of the last 10, last failed 2 runs ago"*. A score without its evidence is not
actionable and will not be trusted.

### 7.3 Run-level verdict
Counts and pass rate over the window; new / fixed / still-failing lists; suite duration trend; §4's
new-dependency signal; and the count of first-seen ids, which is how a rename storm announces itself.

### 7.4 Rename survival
When an id is `absent` and another is `new` **in the same run, suite and feature**, propose an alias
if the display names are within a normalised edit-distance threshold, **or** the `shapeSet`
fingerprints match, **or** `Scenario.SourceFile`/`SourceLine` agree (available since LLM M2.6 landed —
§1.5; a rename in place keeps its file and usually its line). Never applied silently: printed, then `kronikol history rename <old> <new>` (or
`--accept-renames`) writes `.kronikol/aliases.json`; the analyzer follows aliases transitively.
Shape-matching is the part no competitor can do and is what turns a heuristic into a usually-right
one — §2.4 measured `shapeSet` agreement at 96.6% for the same logical test, which is precisely the
signal a rename detector needs.

### 7.5 Branch scoping
Reads filter by branch, defaulting to the current run's. `--branch` overrides; `--compare-branch main`
answers "is it failing on main too?" from the same file. A run with no branch sits in a `local` stream.

### 7.6 Cold start, and honesty about it
`HistoryMinRuns` (proposed 5) gates every verdict needing a window — `flaky`, `slower`,
`unstable-shape`, and the gate's thresholds. Below it the surfaces say *"3 runs recorded — flakiness
needs 5"* rather than silently reporting nothing, because a silent feature reads as a broken one.
`new`, `broke`, `fixed` and `behaviour-changed` work from run 2 and are not gated.

### 7.7 Cost — budgeted from measurement (§2.6)
The analyzer runs on every test run, so it gets a budget and a **deterministic observable** in the
house style (`QUERY_PERF_PLAN`'s `PayloadOpens` is the precedent):
`HistoryStats { LinesScanned, LinesParsed, ScenariosAnalysed, Elapsed }`.

The pinned invariant is **`LinesParsed ≤ window`** — not lines *scanned*, which §2.6 showed grows with
file size and is `history prune`'s job to contain. Budget: **under 150 ms for 5,000 scenarios over a
50-run window** (measured 65 ms, and 97 ms on a deliberately unpruned file), asserted on a generated
fixture, so a regression to "parse the whole ledger" fails a test rather than slowly ruining
everyone's test runs.

---

## 8. Surfaces

**8.1 The HTML report (M5).** Embedded column-major projection, gzip+base64 in one
`<script type="application/json">` like `#puml-data`, decoded through the existing
`window.decompressGzipBase64` (`report-decompress-helper.js` — always emitted, returns a Promise, so
no new dependency and no new browser requirement).

> **Export interaction — checked by reading, then confirmed by running the export function over a
> synthetic report in jsdom (`tools/history-bench/jsd/`). Seven assertions, all as designed, and one
> constraint that reading had left implicit:**
>
> | Assertion | Result |
> |---|---|
> | `#history-data` as a **direct child of `<body>`** is copied, payload intact | ✅ |
> | **`#history-data` nested inside a `<div>` is NOT copied** | ⚠️ `:scope > script` means direct child or nothing |
> | `#puml-data` pruned to the visible ids | ✅ |
> | Hidden features excluded | ✅ |
> | The aggregate History **section** does not survive | ✅ (intended) |
> | `<head>` carried over whole | ✅ |
>
> Two requirements follow, both cheap and both easy to violate later:
> **(1) the payload script is emitted as a direct child of `<body>`**, beside `#puml-data`
> (`ReportGenerator.cs:1711` appends it after `body`, which is exactly the right place) — wrapping it
> in a container for tidiness would silently break export, so a test asserts the placement, not just
> the presence. **(2) the history render script must tolerate a missing History section**, because
> the export produces precisely that state: head scripts *are* copied and *will* run, against a
> document that has the per-scenario elements but no aggregate section. A null-dereference there
> would take the sparklines down with it.
>
> **Export interaction — gentler than the previous draft assumed.**
> `report-export-function.js:65-66` copies **every direct-child `<script>` of `<body>`** verbatim and
> special-cases only `#puml-data`, which it prunes to the ids the filtered export actually contains.
> So a `#history-data` script placed at body level **rides along automatically**; there is no payload
> list to add to, and at 36 KB gzipped (§2.2) it does not need pruning either. What *will not* survive
> the export is the **History section**, because the export copies the head plus visible
> `details.feature` elements only — the same reason the toolbar, timeline and component diagram are
> already absent, documented as deliberate. That is the right outcome and should be stated in
> `Generated-Reports.md` beside the existing list, not discovered by a user: **per-scenario sparklines
> export, the aggregate History section does not.** Per-scenario **sparkline** beside the
duration badge (`ReportGenerator.cs:1419`, `:1471`): ten segments, latest rightmost, `title` carrying
dates and commits, plus a verdict pill for anything not `stable`.

> **The sparkline must be one DOM node, not an inline SVG — this repo has already paid for getting
> main-thread cost wrong.** A ten-rect inline SVG is ~12 nodes; at 203 scenarios that is ~2,400 extra
> elements, against the existing Scenario Timeline's 3 nodes per scenario. Three shipped budgets sit
> directly in that path: `WorstTaskMs` **500 × contention-stretch**, `ToggleWorstTaskMs` **800 ×**
> (`BrowserRenderWorkerTests.cs:243,246`), and `FilterPerformanceTests` asserting a dependency filter
> over **200 scenarios completes in under 1,000 ms** (`:191`) — and filters walk scenario elements, so
> nodes-per-scenario loads that assertion directly. The contention-scaling machinery exists at all
> because this guard was deflaked twice.
> **Design that avoids it entirely:** render the strip as a single element whose
> `background: linear-gradient(90deg, …)` carries one hard-stop pair per run, built from the
> `results` string. Full per-run colour, a `title` for the tooltip, **one node and zero children** —
> cheaper than the timeline row already shipping. Varying-height bars (a duration sparkline) cannot be
> done this way, so they stay in the **History section's** handful of aggregate charts, where an
> inline SVG costs nothing because there are three of them rather than two hundred. A **History section** by the
timeline (`:1270`): pass-rate and duration trends and the three lists — New failures / Failing since… /
Newly fixed — linking by `#sid-`. A **toolbar filter** over the verdicts, **designed inside
`TOOLBAR_REDESIGN_PLAN`'s vocabulary, not beside it**.
**Specifications.html gets none of it** — a specification has no run history.

**Search DSL — the previous draft's `is:new` / `age:>7d` syntax is wrong. Corrected after reading
the tokeniser, then re-checked by *executing* it, which moved two of the three conclusions.**
`advancedSearchTokenise` is **sigil-based**: `@` opens a tag, `$` opens a status, anything else is a
bare word running to whitespace. Run against the shipped `advanced-search.js` under node:

| Input | `isAdvancedSearch` | Tokens | Verdict |
|---|---|---|---|
| `is:new` | **false** | `[text "is:new"]` | never reaches the advanced path at all |
| `age:>7d` | **false** | `[text "age:>7d"]` | same; `>` is not a delimiter |
| `is:new && @slow` | true | `[text "is:new"], and, [tag "slow"]` | advanced path, text term matches nothing |
| `$flaky` | true | `[status "flaky"]` | tokenises and parses cleanly |
| `$zzz` | true | `[status "zzz"]` | evaluates **false**, does not throw |

Two corrections to what reading alone had produced:

1. **`is:new` fails *further upstream* than "matches nothing".** `isAdvancedSearch` returns **false**
   for it, so the query is routed to the **legacy** free-text path — the report cannot even know an
   advanced query was attempted, which rules out the obvious mitigation of an "unknown operator"
   hint at the advanced-parse stage. It is only detectable by pattern-matching the raw input.
2. **`$verdict` is additive at the tokeniser and parser, and *not* additive at the evaluator.**
   `advancedSearchEvaluate(ast, searchText, tags, status)` resolves a status as
   `status.toLowerCase() === ast.value`, and `status` is **one string carrying the execution
   result**. A flaky scenario's status is `Passed`. So a naive `$flaky` is false for precisely the
   scenarios it should match, and — worse — folding verdicts into the `status` string would make
   `$failed` and `$flaky` **mutually exclusive**, when a scenario is routinely both.

So the verdict set must arrive as its own argument, and the ripple is larger than one signature.
Every touchpoint, enumerated so M5 is not estimated from the two obvious ones:

| File | What changes |
|---|---|
| `advanced-search.js:251,280` | new parameter on `advancedSearchEvaluate` **and** `advancedSearchMatch`, new `verdict` token type |
| `report-search-function.js:62` | shallow-path call site |
| `report-search-index.js:239` | deep-path call site (`kronDeepMatchesItem`, itself 5-arg) |
| `report-search-index.js:466` | **the posted worker item shape** — verdicts ride here, beside `status` |
| `report-search-index.js:344` | worker-side call, unpacking the new item field |
| `report-search-index.js:484` | the `fns` roster — **any new helper must be added or the worker dies at runtime** ([[note-copy-fidelity]]'s trap: a missing roster entry timed out all 18 `DeepSearchTests`) |
| `JintTestBase.cs:75,89` | `JsEngine.Invoke` passes a fixed argument list for both functions |

- **Age is a control, not a token.** Duration is already filtered by a control
  (`report-duration-filter-function.js`) with **no** DSL representation — verified: `advanced-search.js`
  contains no duration handling at all. An `age:` token would invent a second idiom for a job the
  report already has one idiom for.
- **The collision rule** stays as drafted: the evaluator checks the closed verdict set **before**
  falling through to execution status, and an `ExecutionResult` name always wins, so nothing existing
  can be shadowed.

> **A verdict-only query is cheap — and the paragraph that used to sit here said the opposite.**
> Reading `kronCandidateDocsForQuery` showed `default: // tag, status, not — never prune`, and this
> plan concluded that `$flaky` would mark every scenario a candidate and trigger a full-corpus deep
> scan. **Executing the shipped functions says otherwise**, because the pruner is not the gate:

| Query | `kronIsDeepEligible` | Candidates of 24 |
|---|---|---|
| `$flaky` | **false** | 24 |
| `@slow` | **false** | 24 |
| `$flaky && @slow` | **false** | 24 |
| `checkout` | true | 0 |
| `checkout && $flaky` | true | 0 |
| **`$flaky \|\| checkout`** | true | **24** |
| `ab` | false | 24 |

> `kronIsDeepEligible` requires **at least one text or phrase term of ≥3 normalized code units**
> before a query enters the deep path at all. `$flaky` has none, so it never reaches the pruner —
> it stays on the shallow path over the already-materialised per-item `searchText`/`tags`/`status`,
> which is the cheap one. **Verdicts therefore add no pruning obligation, and M5 does not need
> pruner work.** The error was reading the first half of one function and concluding about the whole
> path; the gate was fifteen lines further down.
>
> The one real cost is narrower and **pre-existing**: a *disjunction* that mixes a non-pruning term
> with a text term (`$flaky || checkout`) is deep-eligible and ORs the text bitset with `ones()`, so
> it scans everything. That is already true of `@slow || checkout` today — verdicts join an existing
> shape rather than creating one — so it belongs in §14, not in M5's budget.

**8.2 `Failures.md` / `Failures.jsonl` (M3 — the cheapest real win).** One line at the top of each
failure: `**New** — first failure; passed in the previous 12 runs.` / `**Failing since 8 Sep** — 7
consecutive runs, first bad commit abc1234.` / `**Flaky** — failed 4 of the last 10 runs; last passed
2 runs ago.` And **the digest leads with new failures** — twenty red tests of which one is new is the
commonest CI morning, and today the one that matters is buried in report order.

**8.3 The command surface — and a split this plan had left implicit.** Until now it wrote both
`kronikol query history` (a read) and `kronikol history record` (a write) without ever saying they
were different things. `Commands.cs` (§1.5) makes the choice concrete and cheap, so state it:

| | Where | Why |
|---|---|---|
| **`kronikol query history [address]`** | a verb in the `query` family | it is a *read* under a byte budget that returns addresses, which is what every `query` verb is; it inherits `--json` (M2.9) and the MCP mapping for free |
| **`kronikol history record\|import\|prune\|compact\|init\|quarantine\|rename\|doctor`** | a **new top-level entry in `Commands.Table`** | these *mutate* the ledger or the repository. Putting a write behind `query` would make `query` unsafe, and `query`'s whole contract is that it only reads |

One table entry, one `PrintUsage`, and `CommandTableTests` covers it. **Both halves still owe the
three skill assets** — `SkillDriftTests` asserts every verb in `PrintUsage` appears in `SKILL.md` and
`commands.md` and back, and those are canonical in `templates/`, never edited per-copy (§1.5).

**8.3a `kronikol query history` (M3).** The read verb: bare (run trend + three lists) · `history s3` /
`history <stableId>` (timeline: run, date, commit, result, duration, shape marker, error cluster) ·
`--flaky|--new|--failing|--regressed|--changed` · `--branch` / `--compare-branch` · `--suite`. Plus a
history column in `query failures`, and `--json` from day one. **Obligation (§1.5): the three skill
assets update in the same commit or `SkillDriftTests` fails.**

**8.4 Pointer, `::notice`, CI summary (M3).** `3 failed — 1 new, 2 failing since Tuesday` in the
pointer and notice; a **Trend** section in `CiSummary.md` (the channel that always survives). Content
rules unchanged: counts, ids and names, never captured text (§9.1).

**8.5 The gate (M4) — what makes it a product rather than a display.**
`kronikol history gate <dir>` → 0 clean, 1 tripped, 2 usage, with
`--fail-on new-failures,flaky,duration-regression,behaviour-change` and `--max-new-failures`,
`--min-pass-rate`, `--flaky-threshold`, `--slower-by`. **Block the merge on new failures while
tolerating known-flaky ones.** Today the only available policy is "any red fails the build", which is
why teams with flaky suites go permanently red and stop looking. ADO sells exactly this (§0.1) — to
ADO Services users with VSTest. Everyone else has nothing.

**8.6 Quarantine (M4), CTRF and MCP (M6).** `.kronikol/quarantine.json` — id, reason, added-by,
added-on, optional expiry; `history quarantine <id> --reason … [--until …]` and `--release`. The gate
ignores quarantined scenarios, the report badges them, and an **expired** quarantine is reported as a
finding, because that is how every quarantine list in the industry dies. **MCP**: a `history` tool,
covered by M3.1's parity fact.

**CTRF is a much bigger opportunity than the previous draft's one-line "fill `flaky`".** Read from
the authoritative schema (`ctrf-io/ctrf`, `schema/ctrf.schema.json`, 2026-09-12), the test object
already defines everything this plan computes:

| CTRF field | Type | Filled from |
|---|---|---|
| `flaky` | boolean | §7.1 `flaky` |
| `retries` | integer | §5.8 `attempts` |
| `retryAttempts[]` | `{attempt, attemptId}` | §5.8 |
| `summary.flaky` | integer | §7.3 run-level count |
| **`insights`** | object — *"Derived metrics for this test across runs"* | **do not populate — see below** |
| `insights.passRate` / `failRate` / `flakyRate` | `metricDelta` `{current, baseline, change}` | — |
| `insights.averageTestDuration` / `p95TestDuration` | `metricDelta` | — |
| `insights.executedInRuns` | integer | — |

…where `metricDelta` is `{current, baseline, change}`. **An independent standard has converged on
precisely this vocabulary** — pass/fail/flaky rate, duration percentiles, runs-executed, each against
a baseline — which is the strongest available evidence that §7's model is the right shape rather than
an idiosyncratic one. It also means Kronikol's history has a **standard wire format** the moment M6
lands: `kronikol export --ctrf` emits a file other tools already read, without inventing anything.

**Who writes `insights` — checked, because an earlier draft of this plan had it backwards.**
`github-test-reporter` **computes and writes** insights itself: `run-insights.ts:389` assigns
`currentReport.insights = {…}` derived from the `previousReports` it fetched from artifacts (`:364`
notes it sets `current` first and calculates `baseline`/`change` afterwards). It is a *producer* of
insights, not a consumer of them. So **Kronikol must not populate `insights`** — anything it wrote
would be recomputed and overwritten. Emit plain per-run CTRF and let the Action do the trending.

**`flaky`, by contrast, IS honoured from the producer — but only halfway, and the half it reaches is
not the one that matters most. Three layers of source were read before this paragraph was trusted,
because the first two each looked conclusive and each was misleading.**

| Layer | What it says | Reached by a producer-set `flaky`? |
|---|---|---|
| `isTestFlaky(test)` | `test.flaky \|\| (test.retries > 0 && status === "passed")` — producer value is the **first** clause | yes |
| `flaky-table.hbs` — the PR comment's **Flaky Tests** list | `{{#if (anyFlakyTests ctrf.tests)}}` → helper is `tests.some(t => t.flaky)`, rows are `{{#if flaky}}` | **yes — the row appears**, with a blank `Retries` column |
| `flaky-rate-table.hbs` — **Overall Flakiness** and the per-test rate | `insights.flakyRate` ← `calculateFlakyRateFromMetrics` = `totalAttemptsFlaky / (totalResults + totalAttemptsFlaky)`, and `totalAttemptsFlaky += test.retries \|\| 0` | **no — contributes exactly 0.0000** |

**The middle row was then rendered rather than reasoned about, and it came back stronger than the
claim.** `flaky-table.hbs` compiled with the reporter's own `anyFlakyTests` and `getCtrfEmoji`
helpers (`tools/history-bench/hbs/`):

| Producer | Rendered output |
|---|---|
| Kronikol — `flaky: true`, no retries | `\| **Flaky Tests 🍂** \| **Retries** \|` … `\| Pay with an expired card \|  \|` |
| **A retried test — `retries: 2`, no `flaky` flag** | **`No flaky tests in this run ✨`** |
| Neither | `No flaky tests in this run ✨` |

**The free chain's own retry-based detection does not populate its own Flaky Tests table.**
`anyFlakyTests` is `tests.some(t => t.flaky)` — it never calls `isTestFlaky`. So the two flaky
surfaces are driven by **completely disjoint** mechanisms: the *table* is producer-only, the *rate* is
retry-only. Kronikol is therefore not improving that table, it is **the only thing that can fill it**
for a suite whose CTRF emitter does not set `flaky`. One cosmetic consequence for the docs: the
`Retries` column renders blank, because Kronikol has no retries to put there.

> Both surfaces are **opt-in**: `action.yml` declares `flaky-report` and `flaky-rate-report` with
> `default: false`. §15.1's recipe must set `flaky-report: true` explicitly, or a user following it
> sets `flaky` faithfully and sees nothing at all.

The decisive detail is a **dead counter**. `isTestFlaky` gates *two* accumulators (`run-insights.ts:192-195`):
`totalResultsFlaky += 1` and `totalAttemptsFlaky += test.retries || 0`. The first is the one a
producer-set `flaky` increments — and `totalResultsFlaky` is **never read by any rate calculation**;
all three `flakyRate` sites (`:269`, `:454`, `:501`) divide `totalAttemptsFlaky`. With no retries to
count, Kronikol contributes nothing to it. Worse, `sortTestsByFlakyRate` filters to
`insights.flakyRate.current > 0`, so a Kronikol-flagged scenario is **excluded from the flaky-rate
table while present in the flaky table** — the two halves of the same PR comment visibly disagree.

**All of that was then executed rather than left at a reading** — `enrichReportWithInsights` run
directly over synthetic CTRF runs (`tools/history-bench/ctrfrun/`). Four results, one of which
reverses the "dead counter" conclusion in Kronikol's favour:

| Probe | Result |
|---|---|
| Kronikol style — `flaky: true`, no retries, 5 runs | `flakyRate` **0** at run *and* test level |
| Retry style — `retries: 2`, always passes | `flakyRate` **0.6667** |
| Producer-set `test.insights.flakyRate = 0.42` | **overwritten** — comes back 0 |
| Producer-set `test.extra` | **survives intact** into the enriched report |
| `insights.extra.totalResultsFlaky` | **5 — emitted, and therefore templatable** |

So `totalResultsFlaky` is dead only for the *shipped* rate template; it **is** present on the output
object under `insights.extra`, exactly where `flaky-rate-table.hbs` already reads
`insights.extra.totalAttemptsFlaky` from. The corrected interop position:

- **Do set `flaky`.** It puts genuinely flaky scenarios into the PR comment's flaky list, which is
  the surface a reviewer reads, and it costs one boolean.
- **Do ship a Handlebars template** (`kronikol-flaky.hbs`, wired through the Action's `template-path`
  input) that renders `insights.extra.totalResultsFlaky` and `appearsInRuns`. Without it a user sees
  a populated Flaky Tests list beside an **Overall Flakiness of 0.00%** and reasonably concludes
  something is broken. With it, the free chain renders Kronikol's flip-rate flakiness correctly.
- **Never emit a synthetic `retries` count** to move the built-in rate. Kronikol does not retry; it
  observes flips across runs, and faking retries would put a fabricated number in someone's PR.
- **Do not populate `insights`** — confirmed by execution, not just by reading `:389`.
- **`totalResultsFlaky` having no rate reader is still an upstream gap** worth one evidence-first
  issue at M6: a producer honouring CTRF's own `flaky` field cannot move any rate the tool renders.

**Cross-report identity is `test.name` — a display string.** `aggregateTestMetricsAcrossReports`
keys its map on `const testName = test.name`, nothing else. Two consequences, one defensive and one
strategic. Defensive: **if Kronikol emits CTRF, outline rows must carry disambiguated names**, or
rows sharing a display name silently merge into one history bucket — Kronikol's own `stableId`
includes example values for exactly this reason (§1.1), and CTRF has no field to carry it into.
Strategic: the free chain's history is **rename-fragile by construction** — and executing it shows
the failure is worse than "history restarts". Five runs of history in which a test fails twice, then
one more run:

| Current run | `failRate` | Runs seen |
|---|---|---|
| same name | **0.4** | 5 |
| test renamed | **1** | 1 |
| `use-suite-name` toggled on | **1** | 1 |

A rename does not produce a *missing* number, it produces a **confidently wrong** one: a test that
fails 40% of the time is reported as failing 100% of the time, with nothing to indicate that four
runs of evidence were discarded. And the third row means a user can cause it **by flipping a
workflow input**, without touching a test. That is not a gap Kronikol has to argue for; it is §0.2's
unoccupied ground, and `stableId` (§1.1) is precisely the thing that prevents it.

**And `test.name` is not even stable within the Action.** `prefixTestNames` rewrites every name to
`"<suite> - <name>"` whenever the `use-suite-name` input is on (falling back to `filePath`). Since
the aggregation keys on the rewritten name, **toggling that input silently resets the entire
suite's history** — every test becomes a new identity, every baseline reads as cold start, and
nothing reports that it happened. Worth knowing before recommending the free chain to anyone, and
worth one sentence in `Cross-Run-History.md` rather than a support conversation later.

Two things checked because they *could* have quietly excluded Kronikol, and do not:

- `validateReportForInsights` requires only `results.tests` to be an array — no hidden field gate.
- **Producer-set fields survive to the template context.** The read path
  (`read-reports.ts:34,72`) runs `JSON.parse` then `normalizeLegacyReport`, which is
  `createReportNormalizer([preV1Plugin])` — a plugin chain that **applies nothing** when
  `specVersion >= 1.0.0` and ends in `result as unknown as CTRFReport`, a pure cast. No zod, no
  allow-list, no field stripping anywhere in the pipeline. So a Kronikol drift payload on
  `test.extra` reaches Handlebars intact, which together with the `template-path` and
  `custom-report` inputs settles the *mechanism* half of §17.3's last open question: a shipped
  template already dereferences `insights.extra.totalAttemptsFlaky`, so `extra`-shaped paths
  demonstrably template. What is left is only whether a drift line **reads** well in a PR comment —
  a taste question needing one real run, not more source.
  > **Obligation that falls out of it: Kronikol's CTRF must declare `specVersion` ≥ `1.0.0`.**
  > Below that, `preV1Plugin` matches and rewrites `test.suite` from string to string array — and
  > `suite` is what `prefixTestNames` builds the identity key from. An omitted `specVersion` is
  > treated as pre-v1 (`isPreV1(undefined)`), so *not* emitting the field opts into the rewrite.

One structural caveat regardless: CTRF's insights are per test with a single baseline — strictly less
expressive than §3.1's ledger (no per-run series, no shapes, no `failing-since`). CTRF is an
**export**, never the storage format.

**8.7 The merged report.** `MergeableReportRenderer.Render` calls `GenerateHtmlReport`, so merged HTML
inherits history rendering free *if* something hands it verdicts — but `merge` is a separate process
with no ledger. **`merge` accepts an optional `--history <path>` and renders the section when given
one; it never folds** (folding stays `history record`). Without the flag the merged report is exactly
what it is today.

---

## 9. Constraints inherited from the house rules

**9.1 Security.** The ledger lives in the **repository**, the widest-read surface Kronikol touches —
wider than CI logs. It carries ids, names, results, durations, counts, commit SHAs, branch names and
**error cluster keys**. Cluster keys are the judgement call: a normalised first assertion line can in
principle contain a captured value. Mitigations, both required: length-capped, and `HistoryErrorKeys`
can store only a hash. Aggravating factor for the committed path: **git history is permanent**, so a
key committed today survives turning the option off tomorrow (§16 Q5). Never, under any setting:
URIs, bodies, headers, SQL, stack traces. Note that `shapeSet` stores **hashes of templated URIs**,
not URIs — deliberately, and it is why §2.4's templater must never be asked to preserve a raw value.

**9.2 A run with no history is byte-identical to today.** Pre/post diff on a deterministic fixture,
per slice. When a ledger *is* present the HTML changes — deliberate, gated behind data existing, so
no existing user's first upgrade changes anything.

**9.3 Byte-identity pins that move.** Sparkline and History section change every `TestRunReport.html`
**that has history**. Per the 3.0.82 lesson, anchor assertions on emitted attributes
(`data-history-verdict`), never bare substrings, and fix the silently-weakened siblings.

**9.4 Schema surface cost.** Any new field in `TestRunReport.json` lands in eight places. This plan
puts **nothing** there except `runAttempt`, riding in LLM M2.3's batch. The ledger is a separate file
with its own version — the whole reason it is separate — and gets its own drift walker mirroring
3.1.0's `Every_key_the_json_writer_emits_is_declared_in_the_schema`.

**9.5 Nothing here is breaking.** Every option is new and defaults to preserving today's behaviour →
**MINOR**. §15.4.

---

## 10. Milestones — strict TDD, each its own release

**M0 — finish the measurements** *(no shipped code)*. §2 answered every question it opened; **no M0
item is a blocking gate any more.** What remains is turning one-off harnesses into committed
regression guards, plus one dry run:
1. **Re-run §2.2 and §2.4 as committed scripts** so the numbers are reproducible and regressions
   visible. The corpus is BreakfastProvider, which is gitignored — the harness generates or points,
   never vendors (the `tools/query-bench` precedent).
2. ~~The remaining churn gate.~~ **Closed by §2.10, from data already on disk.** Every one of the
   five residual disagreements has an identified non-drift cause — four are raw call-count
   differences between harnesses, one is fixture data. A same-framework repeat on a volatile-id suite
   stays worth doing as **confirmation over a longer span than nineteen minutes**, budgeted as an
   environment bring-up rather than a `dotnet test` (§2.7), but M6 no longer waits on it.
3. ~~Repo-root discovery across the twelve templates.~~ **Done — §2.8.** All twelve are one layout
   (none sets a folder path), so what remains is not a sweep but two fixes it exposed: `.kronikol/`
   as a fallback root marker, and a `HistoryUnavailable` diagnostic that names the fix.
4. ~~Analyzer cost~~ — **measured** (§2.6): 65 ms at 5,000 × 50. M0 keeps the harness and pins
   `LinesParsed ≤ window` plus the 150 ms budget.
5. **Concurrent append on Windows and Linux** — **already measured** (§6.5); M0 turns the one-off
   harness into a committed regression guard on both OSes, and calibrates the retry budget and jitter
   from it. The correctness rule (lock, then keep the fragment and diagnose on give-up) is settled.
6. **One end-to-end artifact-relay dry run** on a scratch repo: upload a fragment with
   `retention-days`, download it from the *next* run with `run-id` + `${{ github.token }}` and a
   `permissions: actions: read` block, fold it. Every input is verified (§6.3b); the assembled
   workflow is not, and a recipe in the wiki that nobody has executed is a defect.

**M1 — the ledger.** Format, streaming reader **with its own retry budget** (§3.4), roster interning,
**suite scoping**, fragment, run identity (+ `GITHUB_RUN_ATTEMPT`), append + lock + retry, corruption
tolerance, version refusal that never appends (§3.5), `partial` detection, resolution order,
`HistoryFilePath`/`KRONIKOL_HISTORY`, **`RunSuite.Resolve` consumed rather than re-implemented
(§3.2)**, and the new **top-level `history` entry in `Commands.Table`**
(§8.3) carrying `record|import|compact|prune`.
Red: `Fold_of_eight_shard_fragments_produces_one_run` ·
`Two_projects_sharing_a_stableId_do_not_share_history` ·
`A_run_whose_suite_does_not_resolve_is_recorded_as_having_none` ·
`Whole_suite_id_turnover_is_reported_as_a_suite_name_change_not_a_rewritten_suite` · `Ledger_keeps_every_scenario_sharing_a_stableId` ·
`Unknown_historyFormatVersion_is_refused` · `Truncated_last_line_is_skipped_not_fatal` ·
`Append_under_contention_loses_no_fragment` · `Runs_are_ordered_by_append_position_not_timestamp` ·
`Defaulted_results_are_recorded_as_unknown` · `Filtered_run_is_marked_partial_and_suppresses_absent` ·
`No_repo_and_no_option_writes_nothing_and_records_a_diagnostic` ·
`Two_platforms_serialise_the_same_run_to_the_same_bytes`.

**M2 — the verdicts.** `HistoryAnalyzer`, the §7 vocabulary, aliases, branch and suite scoping, both
flakiness numbers, `HistoryMinRuns`, `HistoryStats`.
Red: one per verdict row, plus `Permanently_red_test_is_failing_not_flaky` ·
`Skipped_and_unknown_runs_are_not_flips` ·
`Verdicts_needing_a_window_are_silent_and_say_so_below_min_runs` ·
`Duration_regression_needs_two_consecutive_runs` · `Lines_read_is_bounded_by_the_window`.

**M3 — the agent surfaces.** Digest verdict lines + new-first ordering, pointer, `::notice`, CI
summary Trend, `query history` + the `failures` column, **the three skill assets**, the
`agent-instructions.md` rung.
Red: `Digest_leads_with_new_failures` · `Pointer_says_new_and_since` ·
`Digest_without_a_ledger_is_byte_identical_to_today` · `History_verb_addresses_one_scenario` ·
`SkillDriftTests` stays green (it will not, until the three assets are updated).

**M4 — gate and quarantine.** Exit codes, thresholds, quarantine file, expiry findings.
Red: `Gate_passes_when_only_quarantined_tests_fail` · `Expired_quarantine_is_reported` ·
`Gate_exit_code_1_only_on_the_selected_conditions` · `Gate_is_advisory_below_min_runs`.

**M5 — the HTML report.** Projection payload (+ export list), sparklines, History section, toolbar
filter, search DSL, `merge --history`. Playwright per `CLAUDE.md`: `PollingInterval = 200` on every
`WaitForFunctionAsync`, `.First`/`.Nth` on multi-match selectors, `FillSearchBar()`, no
`Force = true`, no network mocking; the sparkline is inline SVG, so interactions use JS
`dispatchEvent`.

**M6 — behaviour history + interop.** The §2.4 templater and both fingerprints, drift verdicts,
`unstable-shape` suppression, `reordered` behind an option, new-dependency signal,
`query history --changed`; CTRF `flaky`; the MCP history tool.
Red: `Identical_rerun_produces_identical_shapes` ·
`Volatile_id_inside_a_literal_segment_is_templated` (the `Recipe-<hex32>` case) ·
`Event_and_queue_names_are_never_templated` (the deleted base64 rule, pinned so it cannot return) ·
`Query_string_values_do_not_change_the_shape` ·
`Reordered_parallel_calls_do_not_report_a_behaviour_change` ·
`Shape_change_on_a_passing_test_is_reported` ·
`Scenario_whose_shape_always_changes_is_suppressed` ·
`Ctrf_flaky_is_true_only_for_history_flaky_tests` · MCP tool-↔-verb parity.

**M7 — rename survival and maintenance.** Aliases, detection, `history rename`, `history doctor`
(orphans, roster churn, size, expired quarantines, corrupt lines, suite sprawl), `--from-allure`.
Red: `Renamed_scenario_with_the_same_shape_is_proposed_not_applied` ·
`Alias_chain_resolves_transitively` · `Doctor_reports_orphans_and_expired_quarantines`.

**M8 — documentation, dogfooding, Kronikol4J ledger, release.** §11, §15.

Suggested shipping: **M1+M2+M3 as one release** (the CLI and agent half works end to end), then M4,
M5, M6, M7. Two finished beat six open.

---

## 11. Dogfooding — this repo is the first user, and the demo is the second

1. **This repo's own CI** (`.github/workflows/ci.yml`) runs a large suite with known flaky areas —
   the perf guard has been deflaked twice and `ContentionScale` exists because of it. A committed
   ledger would have made both deflake sessions evidence-driven rather than inferential. First real
   user, and the honest test of the commit-back recipe — **and it should dogfood §6.3d2's orphan data
   branch, not `main`**, because that is what the plan now recommends and an untested recommendation
   is the thing §11 exists to prevent.
2. **`examples/Example.Api/tests/…CiPreview.{Mixed,AllFailing}`** — already the skill's worked
   examples; a seeded multi-run history makes the history verbs demonstrable in the docs.
3. **The BreakfastProvider demo report.** A live report showing *"flaky — failed 4 of the last 10"* on
   a real scenario advertises the product better than any prose about sequence diagrams, and it is
   the linked artifact the direction note wants people landing in. **Note §2.3: that repo has six
   component test projects, so it is also the sharpest test of suite scoping.**

---

## 12. Verification protocol (run before each release, recorded here)

1. Full suite green including the E2E remainder job.
2. **The sharded-run proof.** Real suite in 8 shards, download all 8 artifact folders,
   `kronikol history record ./artifacts` → exactly **one** new run with `shards: 8`.
3. **The multi-project proof.** Run two BreakfastProvider component projects into one ledger → two
   suites, no shared history, no `absent` (§2.3). Use **NUnit and xUnit**, since §2.10 shows those are
   two of the three suites that actually share `stableId`s — picking a non-colliding pair would pass
   the test without exercising it.
3b. **The data-branch proof (§6.3d2).** On a scratch repo: orphan branch created, `main`'s worktree
   unchanged, ledger read via `fetch` + `cat-file` without checkout, a run appended from a
   `git worktree` without leaving `main`, and two racing appends union-merged. §11.1 dogfoods this
   recipe, so it is verified before it is recommended.
4. **The fifty-run proof.** Fifty synthetic runs with seeded flakiness, a rename at 30, a behaviour
   change at 40, a skipped fortnight, one defaulted-result run and one filtered run; assert size and
   that all six are classified correctly.
5. **The no-history proof.** Delete `.kronikol/`, regenerate, diff every output → byte-identical.
6. **The clean-worktree proof.** `git clean -xfd`, build, run — history still accrues (§5.1). Run it
   literally; the whole design turns on it.
7. **The kill proof.** `SIGKILL` mid-append, run again → ledger readable, at most the last line lost.
8. **The merge proof.** Two branches each append one run, merge → **no conflict**, both runs kept, on
   a checkout whose `.gitattributes` carries `merge=union text eol=lf`; then the same merge with the
   attribute removed → the two-line conflict a human resolves in seconds. Both directions, because
   the recipe's value is the first and its safety net is the second. Add the roster case: two
   branches introducing *different* rosters merge to a file `history verify` accepts, and a
   counter-keyed roster is rejected (§3.1).
9. Ledger committed into a scratch repo, three runs, `git diff` read by hand: is it reviewable?
10. Real-data pass: M5's report against BreakfastProvider output, not a synthetic fixture.

### 12.1 Log

- **3.9.0 (2026-09-14).** `dotnet test tests/Kronikol.Tests` green (4,973). Proofs covered in-process by
  the unit tests rather than run literally: 2 (`Fold_of_eight_shard_fragments_produces_one_run`,
  `Record_folds_the_shards_of_one_run_into_one_line`), 3 (`Two_projects_sharing_a_stableId_do_not_share_history`),
  4 piecewise (one analyzer test per classification; `Ledger_for_5000_scenarios_over_50_runs_stays_under_the_budget`
  for the size), 7 as a torn tail (`Truncated_last_line_is_skipped_not_fatal`,
  `An_append_after_a_torn_tail_starts_on_a_fresh_line`), 8 as a simulated union output
  (`Ledger_written_by_a_union_merge_is_read_in_append_order_not_timestamp_order`,
  `Two_rosters_sharing_a_key_with_different_contents_is_a_verify_failure`); 5 only as "history off"
  (`Nothing_about_history_reaches_the_outputs_when_it_is_off`) - the no-ledger byte-identity pin of the
  HTML is M5's. Not run: 1 (E2E remainder), 3b, 6, 9, 10 - all of them the dogfood in M8.
- **3.10.0 (2026-09-14).** `dotnet test tests/Kronikol.Tests` green (4,992: +19 in `HistoryGateTests`
  and `HistoryMaintenanceTests`). Library untouched, so the 3.9.0 coverage stands. The gate's advisory
  rule, the quarantine trip, the rename suggestion and the CTRF/Allure imports are pinned by tests;
  none of the numbered proofs moved.
- **3.11.0 (2026-09-14).** `dotnet test tests/Kronikol.Tests` green (4,999: +6 in `HistoryHtmlTests`,
  `HistoryOutputsTests`, `MergeCommandTests`), `Kronikol.Tests.SearchEngine` green (210: +8 verdict
  tests), Playwright: the four history classes green (10: sparkline, section, `$flaky` filter, export)
  plus the search, deep-search, export, grouping, stable-id, hash and toggle-default classes (110).
  Proof 5 now holds for the HTML: `ToggleDefaultsBaselineTests` pins the no-ledger report byte for byte
  and stayed green; `Without_history_nothing_about_history_reaches_the_html` pins the markup. The M5
  design moved: rendered on the generation side, one node per sparkline, no payload script, so the
  export needs no special case (the export proof is `HistoryExportTests`). Proofs 3b, 6, 9: the dogfood
  workflow on the orphan branch `kronikol-history` is the executed recipe (§11.1). **Verified on the
  push of this release:** CI Summary Preview run 34854297755 created the branch (commit `1572965e`,
  README + `.gitattributes` + `history.jsonl`) and recorded one line per suite — four rosters, four
  runs under `gh:34854297755:1`, nine lines in all — in 43 seconds, and `history show` wrote what the
  branch holds into the job summary. Proof 1 (the E2E remainder) and 10 run on CI.
- **3.12.0 (2026-09-14).** `dotnet test tests/Kronikol.Tests` green (5,001: +2 in `HistoryOutputsTests`).
  Closes the gap the dogfood surfaced: a pull request's runs form their own stream, so a run read as a
  cold start when the question was what changed against the branch it targets. `HistoryBranch` reads
  the run against another stream (`GITHUB_BASE_REF` on GitHub) and `HistoryCompareBranch` adds a second
  reading beside the run's own on the digest, the pointer and the report's History section; the run's
  own line still records under its own branch (`A_run_reads_against_the_stream_it_is_told_to`,
  `A_compare_branch_is_read_out_beside_the_run_s_own_stream`). No-ledger output untouched.
- **3.13.0 (2026-09-14).** `dotnet test tests/Kronikol.Tests` green (+3: `CiMetadataDetectorTests`,
  `HistoryOutputsTests`, `HistoryGateTests`). The 3.12.0 opt-in became the default the same day, on the
  user's call that a feature hours old is the moment to move a default: a pull request build reads
  against the branch it targets (`GITHUB_BASE_REF`, `SYSTEM_PULLREQUEST_TARGETBRANCH`) unless
  `HistoryBranch` says otherwise, and the gate reads the same way. `gate --min-runs N` closes the
  other gap the BreakfastProvider dogfood surfaced: the report took `HistoryMinRuns` and the gate could
  not be told it, so the two disagreed below five runs. `query history` and `merge --history` take the
  same default. The 3.12.0 push's CI was red on its own two new tests: they seeded `main` and the CI
  process *is* on main, so the run under test sat on the stream it was reading across to; the tests
  seed `trunk` now, and the suite's own-stream tests pin `HistoryBranch = ""` so a pull request build
  of this repository reads them as a push does.
- **Proof 10 on a consumer (2026-09-14).** BreakfastProvider (lemonlion/BreakfastProvider, 3.0.83 →
  3.13.0) runs the recipe on all 18 CI lanes: one suite per lane (`KRONIKOL_SUITE`), the ledger fetched
  from an orphan `kronikol-history` branch before the tests, `history gate --min-runs 3` and the CTRF
  reporter into the job summary, a `history` job folding the `*-report` artifacts, a nightly schedule.
  Run 34881911876 created the branch (16 suites: the three TUnit lanes, which run through a separate
  reusable workflow, recorded as one suite until that workflow got the same steps); run 34886898742
  read it (18 suites, 55 lines) and published reports with sparklines and verdicts against the earlier
  run. Two things learned: `gh run cancel` leaves an `if: always()` history job recording whatever lanes
  finished, and a first run on an empty stream reads as `stable`, not `new`, by the analyzer's rule.

---

## 13. Tests

**Unit** — `HistoryLedgerTests` (round-trip, rosters, suites, pruning, unknown version, truncated
line, duplicate ids, byte-determinism) · `HistoryFoldTests` (shards, contention, self-healing
fragments, run identity, append ordering) · `HistoryAnalyzerTests` (one per verdict, both flakiness
numbers, skip/unknown exclusion, partial runs, min-runs gating, aliases, branch and suite scoping) ·
`HistoryPathResolutionTests` (four steps, worktree, submodule, none) · `InteractionShapeTests`
(table-driven templater including the two pinned regressions in M6, churn gate) · `HistoryGateTests` ·
`HistoryRenameTests` · `HistoryDoctorTests` · `FailuresDigestHistoryTests` · `RunSummaryHistoryTests` ·
`QueryHistoryCommandTests` · `CtrfFlakyTests` · budget pins
(`Ledger_for_5000_scenarios_over_50_runs_stays_under_the_budget`,
`Lines_read_is_bounded_by_the_window`).

**E2E (Playwright)** — `HistorySparklineTests`, `HistorySectionTests`, `HistoryFilterTests`,
`HistoryExportTests`, `HistoryAbsentTests`, under the `CLAUDE.md` E2E rules.

**Drift guards** — `SkillDriftTests` (existing, fails without §8.3's asset updates); the ledger's own
schema walker; MCP tool-↔-verb parity.

**Cross-checks the house has learned to add** — assertions anchored on emitted attributes rather than
bare substrings (3.0.82); the embedded payload asserted to *render*, not merely be present (3.1.0);
a fixture exercising the paths the corpus does not.

**Fixtures that exist because §17.0's error class bit here** — each one pins a behaviour that reading
the code had got wrong:

- **A union-merged ledger, not just a linearly-appended one.** `merge=union` keeps ours-then-theirs,
  so a merged file has non-monotonic `at` and interleaved rosters. Every analyzer test that reads a
  ledger gets a merged variant; `HistoryLedgerTests` gains
  `Ledger_written_by_a_union_merge_is_read_in_append_order_not_timestamp_order` and
  `Two_rosters_sharing_a_key_with_different_contents_is_a_verify_failure`.
- **`$verdict` against a scenario whose execution status is `Passed`.** The evaluator bug this plan
  nearly shipped is invisible to any test whose flaky fixture also failed — so
  `AdvancedSearchVerdictTests` asserts `$flaky` matches a **passing** scenario, and that `$failed &&
  $flaky` matches one scenario rather than none.
- **A verdict-only query asserted to stay off the deep path.** `Verdict_only_query_is_not_deep
  _eligible` — pinning the measured behaviour, so that a later change to `kronIsDeepEligible` that
  pulled `$flaky` onto the deep path would fail rather than quietly becoming a full-corpus scan.
- **A reader during a write.** `Reader_retries_while_the_writer_holds_the_lock` and
  `Reader_that_exhausts_its_budget_degrades_to_no_history_without_failing_the_run` — §3.4 measured a
  ~9% denial rate, so the retry path is on the common path, not the exotic one.
- **A version the writer does not understand.** `Unknown_historyFormatVersion_is_not_appended_to`
  — the corruption case §3.5 names, which no later diagnostic could detect.
- **`CtrfFlakyTests` asserts the *list*, never the rate.** The upstream flaky rate is retry-derived
  and Kronikol emits no retries; a test asserting a rate change would pin a fiction (§8.6).

---

## 14. What this deliberately does not build (the contract, not the apology)

- **No server, database, hosted dashboard or account. Kronikol itself opens no socket** — no HTTP
  client, no token handling, no provider-specific API code in the library or the tool. That is the
  line, and it is narrower than the previous draft's, which wrongly banned *using* CI-fetched history
  at all: the platform's own tooling may fetch prior fragments into a directory and Kronikol reads
  them offline (§6.4). Who makes the call is the distinction; that there is a call is not.
- **No unbounded retention.** The window is finite; a team wanting two years wants a data warehouse
  and should export (CTRF/OTLP).
- **No retrofit of the deep-search pruner.** Measured (§8.1): a verdict-only query is not
  deep-eligible, so verdicts cost nothing and need nothing. What does scan everything is a
  **disjunction mixing a non-pruning term with a text term** — `$flaky || checkout`, and equally
  `@slow || checkout` today, because `orInto` unions the text bitset with `ones()`. Fixing that means
  giving `tag`/`status`/`verdict` real bitsets, which is a search-plan change with its own risk of
  altering results. **Noted here so it is a decision and not an oversight.**
- **No cross-repository aggregation.** One ledger, one repository.
- **No assignment, comments, ownership or approval workflow.** Tags already cover "whose area".
- **No notifications or real-time push.** The job summary and the gate's exit code are the
  notification; the CI system delivers it.
- **No rerun orchestration.** Kronikol does not re-execute tests to detect flakiness — ADO's
  mechanism, and the commoditised half. It *records* attempts when the framework reports them.
- **Nothing in `Specifications.html`.** **No history in the OTLP export** — a span describes one run.
- **No new capture.** History is computed entirely from what is already recorded.

---

## 15. Documentation, port, release

The first draft of this section was **incomplete** — six pages from memory, missing the two that are
mandatory. The list below is verified against `../Kronikol.wiki`.

**15.1 Wiki.** **New page `Cross-Run-History.md`** — the ledger, all **five** ingress paths with
executed (not merely written) CI recipes for artifact relay, cache, commit-back and **the orphan data
branch (§6.3d2, the recommended durable option)**, each carrying its verified limit — the cache's
**7-day idle deletion**, the artifact's **90-day maximum**, the fact that Kronikol's own
`CiArtifactRetentionDays = 1` makes a shared reports artifact useless for history, and for the data
branch the one real cost: **the ledger is not in the working tree**, so the page must say which branch
holds it and how to read it — the verdict vocabulary, the gate, quarantine, renames, §0.1's honest comparison with ADO and
the JUnit-XML Actions, and §14's anti-scope stated plainly.

> Four things on that page are **documentation obligations created by measurement**, not nice-to-haves:
> the exact `.gitattributes` line, what it buys, and **the case it does not cover** — merges
> performed server-side, where the attribute is not read at all (§5.10), so the page must not promise
> conflict-free merges to a repo that merges through the web UI; the CTRF interop being a **flaky list entry and not a flaky rate**, so
> nobody reads the PR comment's two tables as contradicting each other (§8.6); and the warning that
> turning `use-suite-name` on or off in `github-test-reporter` **silently resets that tool's own
> history**, because it rewrites the names the tool keys on. The third is not Kronikol's bug, and it
> is exactly the kind of thing a page comparing the two should say. And fourth: **`HistoryWindow` does
> not save repository space** — §5.10 measured pruning as 55% *more* packed storage than keeping
> everything — so the page must justify the window on read cost and reviewability and must not repeat
> the intuition that a smaller file is a smaller repo.

*Mandatory edits (both missing from the first draft):* **`Report-Configuration.md`** — the options
reference (`## ReportConfigurationOptions`, `### Data Format Options` `:75`). Every new option gets a
row: `HistoryFilePath`, `HistoryWindow`, `HistoryMinRuns`, `HistoryDurations`,
`HistoryShapes`, `HistoryErrorKeys`, `HistoryFlakyRate`, `HistorySlowerBy`, `HistoryPartialThreshold`,
`GenerateHistoryFragment`, `WriteHistoryLedger`, `EmbedHistoryInReport`. **An option not on this page does not exist.**
**`Search-Syntax.md`** — the history verdicts join the **`$status`** section (`:103`) alongside `@tag`
(`:79`), *not* as a new `prefix:` idiom (§8.1); the operator table (`:41`) and examples table (`:135`)
both gain rows, and the collision rule (an `ExecutionResult` name always wins) is stated where someone
wondering why `$failed` still means failed will look. Age is documented with the **duration control**,
not here.

*Also:* `Generated-Reports.md` — the **Output Files Summary table** (`:5-13`) gains
`History.run.json`, and its opening sentence about `bin\Debug\net10.0` is exactly where §5.1's "the
ledger is elsewhere, and here is why" belongs; the Gold Standard recipe (`:614-651`) retires onto real
commands. `Querying-Reports.md` — the `history` verb and when to reach for it rather than `diff`.
`Merging-Parallel-Reports.md` — Limitations (`:131+`) and `merge --history`.
`CI-Summary-Integration.md` (Trend), `CI-Artifact-Upload.md` (**`History.run.json` needs its own
artifact with its own `retention-days`** — riding along in the reports artifact inherits the 1-day
default and the history is gone by the next run), `Ingesting-External-Captures.md` (§5.11), `Capture-Time-Redaction.md` (§9.1 — the
page enumerates derived files and is wrong the moment the ledger exists), `Project-Templates.md` (the
`.kronikol/` convention), `FAQ.md` ("does Kronikol detect flaky tests?" — the honest answer includes
§0.1), `Diagnostics-and-Debugging.md` (`history doctor`, the three new diagnostic kinds), `Home.md`,
`_Sidebar.md`.

**15.2 Outside the wiki.** `README.md` and `nuget-readme.md` (one line each — §0's cross-branch claim
is a real differentiator); the **three skill assets** (a red test, §1.5); `agent-instructions.md`;
`CHANGELOG.md`; `PLANS_STATUS.md`; `.gitattributes` (§5.10); and XML doc comments on every new public
type and option, which is how `Report-Configuration.md` stays honest.

**15.3 Kronikol4J.** The HTML changes when history is present → a ledger entry in
`Kronikol4J/docs/REMAINING_PARITY.md`. Capture/tooling-side work, exactly the half the port has not
done, so **record, do not port** — and note that the format is deliberately language-neutral JSONL so
a future Java implementation reads the same file.

**15.4 Versioning.** **MINOR — `3.1.0`.** New options, types, verbs and outputs; nothing removed or
renamed, no default changed so existing code behaves differently. Per `CLAUDE.md` the highest-ranking
change decides the bump and "anything new" is minor. First minor under the rule adopted in `f951f75`;
the changelog must say which part moved and why. Later milestones are minor too; only M0's
measurements and doc fixes are free.

---

## 16. Open questions (recommendations attached; Q1–Q5 change the artifact)

**Q1. Which ingress is the documented default?** **Recommendation changed twice.** Previously
"committed, cache equal billing". Then, after §6.3's verification: **artifact relay (6.3b) is the CI
default**. Then again, after §6.3d2: **the durable option is the orphan data branch (6.3d2), not
commit-back on `main` (6.3d)** — same durability and same cross-branch answer, minus the PR diff and
minus every conflict question in §5.10. So: relay is the default, **(d2)** is the durable
recommendation, (d) is for teams who want the file visible in their working tree and accept the
review noise, and
**cache (6.3c) is demoted to third with an explicit warning**. The reason is measured, not
aesthetic: caches are deleted after **7 days without access**, so the cache cannot hold the fortnight
window the feature is named after, while an artifact holds **90 days**. Relay also needs no repo
write, so it sidesteps §5.10 entirely — and §5.10 got **worse** after this was written, since
`merge=union` turns out not to apply to server-side merges at all, which strengthens Q1's answer
rather than changing it. The one thing only (d) buys is a PR checkout that already
contains `main`'s history with no fetch at all — which is §0's headline, so (d) stays prominent
rather than becoming a footnote.

**Q2. Window — 20 or 50?** **Answered by §2.2: 50 — but for the right reason, which is not the one
the first answer gave.** Measured at 228 KB (36 KB gz) for a 203-scenario suite with full durations
and shapes; Allure's default is 20, and 50 is about a fortnight of a busy repo, which matches the
framing the feature is named after. **What changed:** §5.10 measured that pruning to a window makes
the *repository* 55% larger, not smaller, because dropping the oldest line each commit breaks git's
delta chains. So 50 is justified by **read cost, working-file size and reviewability** — never by
repo size, which it worsens. Two consequences: the docs must not sell the window as saving space, and
a team using the committed ingress may reasonably set `HistoryWindow` *higher* than 50, which is the
opposite of the advice the first draft would have given. Revisit only if a user reports a multi-suite
repo where §2.2's 796 KB six-suite figure hurts.

**Q3. Automatic local folding, explicit in CI?** *Yes, and document the asymmetry in one sentence.*
A CI job that silently mutates a checked-out file surprises people; a developer who must run a
command will not have history.

**Q4. Fingerprint in the first shipped slice or M6?** *Fields in the format from v1 (M1),
computation and surfacing in M6.* §2.4 has de-risked the templater enough to commit to the design; the
real churn gate has now **passed** — §2.10 closed it from existing data, so this is a scheduling
choice rather than a risk-management one.

**Q5. Error cluster keys in a committed ledger — on by default?** *On, capped at 200 characters,
`HistoryErrorKeys = false` documented beside `Capture-Time-Redaction`.* Without them "failing for the
same reason as last week" is unanswerable. §9.1's git permanence is a real aggravator and a
security-conscious repo may prefer hash-only, which still supports "same reason".

**Q6. `--json` before the shared envelope?** **Closed, with one precision added 2026-09-12 afternoon:**
LLM M2.9 is **planned, not built** — it is on that plan's "left" list alongside MCP and CTRF. This
plan's premise is that the LLM plan completes first (§1.5), so history verbs get `--json` on day one;
what must not happen is history *implementing its own* envelope because M2.9 slipped. If the order
ever inverts, history waits rather than forking the idiom.

**Q15. Should `query diff --baseline` resolve its baseline from the ledger?** *Open, and new — raised
by M2.4 landing while this plan was being written (§1.5).* `--baseline` today wants a report on disk:
a `baseline/` folder, or `KRONIKOL_BASELINE`. Once a ledger exists, "the last green run on this
branch" is a question history can answer without anything on disk, which is strictly more useful than
a folder someone has to populate. **Recommendation: not M1, and not silent.** `diff` keeps its
current resolution order; a later milestone adds an explicit `--baseline last-green` (or similar) that
reads the ledger, so the two features join on purpose rather than one quietly changing the other's
behaviour. The wiki must still say which tool answers which question.

**Q7. Does the toolbar filter block on `TOOLBAR_REDESIGN_PLAN`?** *The control yes, the data no.*
Ship M5's sparkline, section and DSL tokens; add the filter control inside the redesign's component.

**Q8. Quarantine in the repo or the ledger?** *Separate file.* Hand-edited and reviewed versus
machine-written and noisy.

**Q9. Does `merge` fold history?** *No — `merge` renders (optionally `--history`), `history record`
folds.*

**Q10. Consume Azure DevOps' own flaky tag?** *No for v1.* REST calls and an ADO-only code path,
against a signal Kronikol computes better without reruns. Document how they coexist.

**Q11. Allure import?** *Yes, M7.* Cheap, name-matched, a genuine outreach hook — but a migration
feature is worth nothing until the thing being migrated to exists.

**Q12. `HistoryMinRuns` default — 5?** *5, and never silent about it (§7.6).*

**Q13. Import JUnit XML, or import CTRF?** **Answered by §0.1: import CTRF, and point at
`junit-to-ctrf` for the rest.** The previous draft proposed a JUnit XML importer because "every
competing tool consumes JUnit". They do — but `ctrf-io/junit-to-ctrf` already converts it, and CTRF
is the richer target (it carries `flaky`, `retries`, `retryAttempts` and `insights`, none of which
JUnit has). One importer reaches the whole ecosystem instead of the poorest format in it. Imported
runs stay second-class and the docs must say so: they carry status only, so every §4 verdict is
absent for them. M7.

**Q12b. `HistoryFlakyRate` default — 0.1?** *Recommendation: 0.1, and surface the evidence beside it
always — now with a third data point.* Allure 2 uses a **bounded 5-run lookback** rather than a count
or a rate (§7.2), which is immune to window length by a different route and is recency-weighted in a
way a 50-run rate is not. The answer is not to copy it — it misses a test that is flaky but green
today — but it does mean `runs since last flip` is **mandatory beside the rate**, not optional. §7.2's arithmetic shows an absolute flip count is unusable at a 50-run window; a rate of 0.1
means "flipped at least once every ten runs", which reads as flaky to a person. Calibrate against a
real ledger once §11.1's dogfood has one, and expect to move it.

**Q14. One ledger file with many suites, or one file per suite?** *One file, suite-scoped lines
(§3.2).* One path to resolve, one thing to commit, one thing to cache. Revisit if §2.2's multi-suite
size becomes a complaint; `history compact` can split before the format has to.

---

## 17. Assumption ledger

Every load-bearing claim in this plan, with how it is known. A plan that cannot say which of its
statements are measured is a plan that will be wrong somewhere and not know where.

### 17.0 The error class this plan keeps making, and the rule that catches it

Fifteen of this plan's own conclusions have now been reversed by checking them. They are **not
fifteen different mistakes — they are one mistake, fifteen times**, and naming it is worth more than any
individual correction:

> **A claim about *existence or shape* was verified, and allowed to transfer to a claim about
> *behaviour* that was not.**

| Verified | Then claimed, unverified | Actually |
|---|---|---|
| CTRF's schema declares `insights` | the ecosystem *reads* a producer's `insights` | it **writes** them; a producer's are overwritten |
| `isTestFlaky` honours `test.flaky` | so setting `flaky` improves the flaky **rate** | the counter it feeds has **no readers**; the rate is retry-only |
| segments *look* opaque | they *are* opaque | every one was a real event/queue/table name |
| `stableId` is documented stable | it is unique | 16.0% collide across projects |
| the window bounds the parse | it bounds the read | scanning is unbounded |
| `is:new` is plausible syntax | the tokeniser rejects it | it never reaches the advanced path at all |
| `$` is sigil-based and open | verdicts are "purely additive" | additive at the parser, **broken** at the evaluator |
| `FileShare` exists | the risk is double-writes | the risk is **lost** writes, 34–67% |
| "≥2 flips" sounds like flakiness | it scales with window length | it flagged 4,265 of 5,000 |
| the pruner returns `ones()` for `$flaky` | a verdict-only query scans the whole corpus | it never reaches the deep path — `kronIsDeepEligible` gates it |
| the free chain computes `flakyRate` | its flakiness detection is commodity | `flakyRate` measures **retry volume**; it is 0 forever for a non-retrying suite |
| a bounded window keeps the ledger small | pruning keeps the *repository* small too | it makes it **55% bigger** — dropping the oldest line breaks git's delta chains |
| `merge=union` resolved two appends cleanly | the committed ledger never conflicts | attributes come from the **worktree**; a bare (server-side) merge ignores them entirely |
| teams might not accept a machine-written file | so the committed ingress is risky and was demoted | tens of thousands accept it — **on a side branch**, which removes the objection instead of weighing it |
| Allure 3's history file is `.jsonl` and its config key is `historyPath` | Allure appends, so it is precedent for crash-safety | `appendHistory` writes from **byte 0** and truncates — it rewrites the whole file every run |

This is why re-reading the plan never caught them. Each pass asked *"is this claim supported?"*, found
a real citation beside it, and moved on. **The citation was always real.** The defect lives in the
gap between the citation and the sentence built on it, and that gap is invisible to re-reading,
because a satisfied citation looks identical whether or not it supports the thing next to it.

**The rule, which is mechanical and therefore usable:** for any claim that decides what gets built,
do not ask *how do I know this is true* — there is always an answer. Ask **what would I have seen if
it were false, and did I look there?** Two corollaries, both earned the hard way in this plan:

1. **A schema can never settle a direction-of-data-flow question.** "System X consumes Y" requires
   the code that reads Y. The `insights` error survived four passes because the falsifier lived in a
   *different repository* from the citation, so the claim felt verified.
2. **Verifying a predicate is not verifying its effect.** `isTestFlaky` really does honour
   `test.flaky` — and the value dies two frames later in a counter nothing reads. Follow the value
   to a rendered number or a written byte, or the check is not finished.

A related correction, not a reversal but the same shape: §2.4 described its templater ladder as
"145 cross-framework scenarios" over six frameworks. Only **three** of the six share any `stableId`
at all. The measurement is real and the number is right; the *scope* implied by the sentence was
not — a claim can be accurate and still be the wrong size.

A third corollary, earned last and the least comfortable: **reading *part* of a call path is reading
none of it.** The verdict-pruning claim came from `kronCandidateDocsForQuery`, whose body really does
say `never prune` — the gate that makes it moot was fifteen lines further down in a different
function. A citation that is accurate about the lines it quotes can still be wrong about the path
those lines sit on.

And the base rate, which is the part worth acting on: **of the behaviour claims in this plan that
were reasoned about but never executed, every single one that has since been executed came back
wrong — including two written on the same day, by someone who had just finished writing this
section.** Careful reasoning has so far been worth nothing as evidence about behaviour, and knowing
about the trap has not been worth much either; only running the code has. Hence the split below — the two columns are not degrees of confidence, they are *different kinds of statement*
with different failure modes, and only one kind has ever been wrong here.

### 17.1a Existence and shape — cheap to check, never yet wrong
| Claim | How verified |
|---|---|
| Reports live in `bin/` and are gitignored — the ledger cannot live there (§5.1) | `ReportGenerator.cs:66-72` read; `git check-ignore` run |
| CTRF has `flaky`, `retries`, `retryAttempts`, `summary.flaky` and an `insights` object of `metricDelta`s (§8.6) | `ctrf-io/ctrf` `schema/ctrf.schema.json` read directly |
| A worktree's `.git` is a **file**, not a directory (§5.2) | Worktree created locally and inspected |
| A submodule's `.git` is a file, like a worktree's (§2.6) | Submodule created and inspected locally |
| Artifact retention max 90 days, min 1 (§6.3b) | `actions/upload-artifact` `action.yml` |
| Kronikol's own artifact retention defaults to **1 day** (§6.3b) | `ReportConfigurationOptions.cs` |
| Cross-run artifact download is first-party (§6.3b) | `actions/download-artifact` `action.yml` inputs |
| `GITHUB_RUN_ATTEMPT` exists; `GITHUB_RUN_ID` is stable across re-runs (§3.3) | GitHub variables reference |
| Option naming is `*FolderPath` / `*FileName`, `Generate*` / `Write*` (§6.2) | `ReportConfigurationOptions` read |
| The tool references the library, so shared code goes in the library (§1.4) | `Kronikol.Tool.csproj` |
| M2.3 landed without `runAttempt`; 3.1.0 untagged (§3.3) | `MapCiMetadataJson` emits 7 fields; `git tag` + `Directory.Build.props` |
| A .NET CTRF emitter and a JUnit→CTRF converter already exist (§0.1) | `ctrf-io` repository listing |
| File-based flaky Actions already exist (§0.1) | Two named repositories |
| `window.decompressGzipBase64` is always present and returns a Promise (§8.1) | `report-decompress-helper.js` |
| Duration filtering has no DSL form, only a control (§8.1) | Zero duration references in `advanced-search.js` |
| All twelve `kronikol-*` templates set no folder path — four setup shapes, identical options (§2.8) | Every template setup file read |
| Verdict support touches 7 call sites incl. the worker item payload and the `fns` roster (§8.1) | Call-site sweep across `report-search-*.js` and `JintTestBase.cs` |
| Kronikol assigns `Scenario.Attempt` on exactly one lane, so it has no retry count to emit (§0.2) | Only `CucumberFeatureSynthesizer` assigns it; the doc comment says the same |
| `template-path` and `custom-report` are supported Action inputs (§8.6) | `github-test-reporter` `action.yml` |

### 17.1b Behaviour — the kind that has been wrong fifteen times
Each row says **how far the check went**: `RUN` = code executed and its output observed; `READ` =
source inspected and reasoned about; `DOC` = a vendor's documentation taken at its word. Every
reversal in §17.0 was a `READ` or `DOC` row promoted to fact. **A `READ` row is not evidence that the
value reaches anything** — that is the §17.0 rule, applied as bookkeeping.

| Claim | Depth | How verified |
|---|---|---|
| **`FileShare.None` excludes across processes on Windows AND Linux** (§6.5) | **RUN** | 8 and 32 concurrent processes, natively on NTFS and in a Linux container on overlayfs and ext4 |
| **A plain append silently loses 34–67% of lines on both platforms** (§6.5) | **RUN** | Same harness, `FileShare.ReadWrite`; one torn line also observed on ext4 |
| Lock starvation is detectable and reportable (§6.5) | **RUN** | 16×200 stress run; three writers exhausted the budget and said so |
| `stableId` collides across test projects — 16.0% (§2.3) | **RUN** | 905 ids over 6 real BreakfastProvider reports |
| Templater ladder 66.9→77.9→89.7→93.8→96.6% (§2.4) | **RUN** | 145 scenarios shared by **three** of the six suites (NUnit/TUnit/xUnit) — not a six-way measurement (§2.10) |
| The base64 rule is harmful (§2.4) | **RUN** | 10.2% of segments; every sample inspected, all literals |
| Ledger 228 KB / 36 KB gz at window 50 (§2.2) | **RUN** | Built from the real 203-id roster |
| Capture is bit-stable across consecutive runs of one suite (§2.5) | **RUN** | `Example.Api.Tests.Component.xUnit3` run three times; 5/5 identical, raw and templated |
| The six BreakfastProvider reports are six independent runs minutes apart (§2.4) | **RUN** | `startTime` + id-provenance analysis across the six files |
| Analyzer cost is 65 ms at 5,000 scenarios × 50 runs (§2.6) | **RUN** | C# harness, warm pass, Release build |
| Parsing is window-bounded, scanning is not (§2.6) | **RUN** | Same harness with 200 extra lines: 50 parsed, 252 scanned |
| An absolute flip threshold mislabels most of a suite on a long window (§7.2) | **RUN** | Harness: 4,265 of 5,000 flagged flaky at ≥2 flips |
| **Two appends conflict without `merge=union` and auto-merge with it; identical lines de-duplicate; LF survives `autocrlf=true`; rebase behaves as merge** (§5.10) | **RUN** | Five-scenario throwaway repo |
| **`merge=union` is read from the working tree, so a bare/server-side merge ignores it** — `attr.tree` is the off-by-default opt-in (§5.10) | **RUN** | `git merge-tree --write-tree` in four contexts: clone, clone with the file deleted, bare, bare with `attr.tree=HEAD` |
| **`stableId` is now suite-qualified in production; 0 of 1,043 ids collide within a single report** (§2.3, §3.2) | **READ (source)** | `ScenarioStableId.Compute(suite, …)`, `RunSuite`, `ReportConfigurationOptions.SuiteName` — landed 2026-09-12/13, citing this plan's measurement |
| **A reader is locked out ~9% of the time while the writer holds `FileShare.None`, and a ~4 KB append is never observed torn** (§3.4) | **RUN** | `tools/history-bench/readtest`: one writer, one looping reader, five lock combinations, ~380,000 reads |
| **An orphan data branch is invisible to `main`, readable without checkout, writable from CI without leaving `main`, and union-merges cleanly in a side worktree** (§6.3d2) | **RUN** | Orphan branch + clone + `fetch`/`cat-file` + `worktree add` + two racing branches merged |
| **Machine-written CI commit-back is accepted at scale; the direct analogue keeps history on a side branch and does not prune by default** (§6.3d2) | **COUNTED** | GitHub code-search totals over `.github/workflows` — approximate, indexed public repos only, so a **lower bound**; plus `github-action-benchmark`'s `action.yml` |
| **A counter-keyed roster corrupts silently under union merge** (§3.1) | **RUN** | Same repo: two differing rosters both named `r2` in one merged file |
| **A committed ledger costs 1.8 KB packed per build; pruning to a window costs 55% *more* than not pruning** (§5.10) | **RUN** | 400 commits per design, `git gc --aggressive`, four designs; linear against a 250-build run |
| **`flaky-table.hbs` renders Kronikol's flaky row — and renders nothing for a retried test** (§8.6) | **RUN** | Template compiled with the reporter's own `anyFlakyTests`/`getCtrfEmoji`; `retries: 2` produced "No flaky tests in this run" |
| **A rename or a `use-suite-name` toggle reports `failRate` 1 for a test that fails 40% of the time** (§8.6) | **RUN** | `enrichReportWithInsights` over five runs of history plus one renamed run |
| **No run-varying example value exists in 1,195 real scenarios** — identity volatility is a real hazard with zero measured instances (§2.9) | **RUN** | `volatile.py` over six suites, five run-varying patterns |
| **`is:new` never reaches the advanced path** — `isAdvancedSearch` returns false, so it routes to legacy free text (§8.1) | **RUN** | `advanced-search.js` loaded in node; token dumps for five inputs |
| **`$verdict` parses cleanly and an unknown `$` value returns false rather than throwing** (§8.1) | **RUN** | Same harness |
| **`$verdict` is NOT additive at the evaluator** — `status` is one string, so `$flaky` is false for exactly the flaky scenarios (§8.1) | **RUN** | Same harness: `advancedSearchMatch('$flaky', …, 'Passed')` → `false` |
| **A verdict-only query never reaches the deep path** — so it is cheap, not expensive (§8.1) | **RUN** | Shipped pruner + `kronIsDeepEligible` executed over a 24-doc index. *Reverses a claim added to this plan the same day from a READ of half the function.* |
| `github-test-reporter` **writes** `insights`, overwriting a producer's (§8.6) | **RUN** | `enrichReportWithInsights` executed: a producer-set `flakyRate` of 0.42 came back 0 |
| A producer-set `test.flaky` moves `flakyRate` by **zero**, but *is* emitted at `insights.extra.totalResultsFlaky` (§8.6) | **RUN** | Six synthetic runs: flipping test scores `flakyRate` 0 / `failRate` 0.3333; a retried test that never fails scores 0.6667 |
| Cross-report identity is `test.name`, a display string (§8.6) | **RUN** | Renaming the test between runs collapsed its history to one run in `enrichReportWithInsights` |
| **`prefixTestNames` rewrites `test.name`, so toggling `use-suite-name` resets all history** (§8.6) | **RUN** | The real `prefixTestNames` executed: `"Pay with an expired card"` → `"Checkout - Pay with an expired card"`, and `failRate` 0.4 over 5 runs became **1 over 1 run** |
| **Producer-set fields survive to the Handlebars context** (§8.6) | **RUN** (confirmed; citation below was corrected first) | `read-reports.ts:34,72` → `normalizeLegacyReport` = plugin chain that no-ops at `specVersion >= 1.0.0` and ends in a cast; no zod/allow-list anywhere. *First citation for this row was the wrong function — caught by applying §17.0's rule to a row written minutes earlier.* |
| **The free chain's flakiness is structurally retry-only — zero for every test in a non-retrying suite** (§0.2) | **RUN** | Same harness; narrowed §0.2 from "the status half is commodity" |
| `github-test-reporter` does artifact-relay cross-run flakiness, fail-rate, duration trends, PR comments (§0.1) | **RUN** | Its insights engine executed over synthetic runs and two of its templates rendered |
| The report export copies **every** body-level `<script>`, pruning only `#puml-data` — and a *nested* script is dropped (§8.1) | **RUN** | `export_html()` executed in jsdom over a synthetic report; seven assertions |
| The console channel is unreliable under VSTest (§1.3) | **RUN** (prior session) | Recorded in `RunSummaryConsoleWriter` |
| Caches: branch + default + base readable; PR caches merge-ref-scoped; fork PRs read-only; **deleted after 7 idle days**; 10 GB cap (§6.3c) | **DOC, and unpromotable** | GitHub dependency-caching reference. The falsifier for the 7-day rule is *waiting a week on real infrastructure*; there is no local proxy. It is load-bearing for Q1's demotion of the cache ingress, so it is flagged rather than quietly trusted |
| Allure 3's history is **append-only as a format but a full-file rewrite as an implementation**; Allure 2 keeps **20**; Allure 3 defaults to **unlimited** (§0.1, §3.1) | **READ (source)** | `allure3/packages/core/src/history.ts` — `appendHistory` writes from `start: 0` and truncates; `allure2` `HistoryPlugin.java:179` is a literal `.limit(20)` |
| **Allure 2's flakiness rule is a bounded 5-run lookback, not a rate** (§7.2) | **READ (source)** | `HistoryPlugin.java:120` — `.limit(5)` over previous statuses, flaky when the current run failed and a PASSED precedes the last FAILED |
| `historyId` and `stableId` agree on base+sorted-parameters; **Allure alone can exclude a parameter or override the id**; and Allure is **also rename-fragile** without that override (§2.9) | **RUN** | The real `getTestResultHistoryId` executed over eight inputs, incl. changing an `excluded` parameter's value and confirming the id does not move |
| Azure DevOps flaky management is rerun-based, VSTest-coupled, Services-only (§0.1) | **DOC, and unpromotable** | Microsoft Learn. Needs an ADO organisation to falsify. It only positions the plan against prior art — nothing is built on it |

**Two `DOC` rows remain, and both are now labelled unpromotable with the reason.** Neither has a
local falsifier: one needs a week of real GitHub cache eviction, the other an Azure DevOps
organisation. The ADO row decides nothing. The cache row **does** feed Q1's demotion of the cache
ingress, so if that ever becomes contentious the answer is to measure it on real infrastructure, not
to re-read the documentation. Everything else in this table has been executed.

### 17.2 Not verified — and what this plan does about each
| Assumption | Status | How the plan avoids depending on it |
|---|---|---|
| ~~A shape fingerprint is stable across *time*~~ | **Verified** — §2.4 is six runs minutes apart with 98% freshly-minted ids, §2.5 is three consecutive runs at 100%, and §2.10 shows **all five** residual disagreements have an identified non-drift cause (four are call-count differences, one is fixture data) | M0.2 becomes confirmatory, not a gate. The honest limit is that this covers a nineteen-minute window, not weeks |
| The assembled artifact-relay workflow works | **Inputs verified, whole not** | M0.6 executes it end to end before the wiki documents it |
| ~~CTRF ecosystem Actions render `insights` into PR comments~~ | **Verified — and the flow is the reverse of the question** | `flaky-rate-table.hbs` dereferences `report.insights.flakyRate` and `insights.extra.*`, and `enrichReportWithInsights` *writes* them (§8.6). Kronikol must not populate `insights`; the producer field that matters is `flaky` |
| ~~Teams will accept a machine-written file in their repo~~ | **Both halves now evidenced** — cost is 1.8 KB packed per build (§5.10); acceptance is revealed preference at scale: 40,640 / 37,504 / 14,848 workflows using commit-back actions, and **2,700 using the direct analogue** (`github-action-benchmark`, append-only machine-written history committed by CI) | The evidence carried a condition — the analogue stores history on a **side branch**, not the main line — which is why §6.3 gained ingress **(d2)**. §11.1 still dogfoods it here first |
| `HistoryMinRuns = 5`, `HistorySlowerBy`, `HistoryPartialThreshold = 10%` | **Chosen, not derived** | Declared as provisional in §16; each is an option, and the surfaces state the threshold rather than hiding it |

### 17.3 Open investigations
1. **M0 items 1, 5 and 6** (§10) — committing the harnesses as regression guards, and the one
   artifact-relay dry run. Items 2, 3 and 4 are **closed** (§2.10, §2.8, §2.6). **Do not ship M1 with
   guessed defaults**, but no measurement now blocks starting.
2. **~~Does the CTRF ecosystem consume `insights`?~~ CLOSED — and it took three passes, each of
   which looked conclusive.** (§8.6, and it is §17.0's worked example.)
   - *Pass 1, from the schema:* "CTRF defines `insights`, so the ecosystem consumes a producer's."
     **Wrong** — a schema cannot settle direction of flow.
   - *Pass 2, from `run-insights.ts`:* "the Action **writes** insights, so don't populate them; but
     `isTestFlaky` honours a producer's `flaky`, so setting it upgrades flakiness detection."
     **Half wrong** — the predicate really does honour it; the value then dies in
     `totalResultsFlaky`, a counter no rate calculation reads.
   - *Pass 3, following the value to a rendered number:* setting `flaky` **lists** the test in the
     PR comment's Flaky Tests table and moves the flaky **rate** by exactly zero, so the comment's
     two tables disagree about the same test. That is the answer, and it is still worth doing —
     for the list, not the rate.

   Each pass ended with a real citation. Only the third followed the value all the way to something
   a person sees, which is what §17.0 now requires of any claim that decides what gets built.
3. **~~The GitHub Action synergy is asserted, not designed.~~ CLOSED — the Action exists and is
   someone else's** (§0.1). Kronikol interoperates through CTRF rather than building a rival. What
   remains open was **does a Kronikol behaviour-drift line render usefully inside a
   `github-test-reporter` PR comment**, and the *mechanism* half is now closed (§8.6): `template-path`
   and `custom-report` are supported inputs, `normalizeTestSuite` spreads `{...test}` so a producer's
   `extra` survives, and a shipped template already dereferences an `extra`-shaped path. The
   remaining half is **legibility, not capability**, and one real PR comment answers it. Budget it
   in M0.6 beside the workflow rehearsal, since both want the same throwaway repo.
4. **~~ReportPortal, Trunk and BuildPulse were not examined directly.~~ CLOSED — checked, and none
   has a no-server mode**, so §0's three-things list stands:
   - **ReportPortal** — its own `docker-compose.yml` stands up PostgreSQL, RabbitMQ, OpenSearch, a
     gateway and six services (`service-api`, `-authorization`, `-auto-analyzer`, `-index`, `-jobs`,
     `-ui`). Self-hosted, but emphatically a server stack.
   - **Trunk** — `analytics-uploader`'s `action.yaml` marks the organisation **slug** `required: true`
     alongside an API token. SaaS upload by construction.
   - **BuildPulse** — a Go uploader whose own description is "Connect your CI to BuildPulse". SaaS.

   One genuinely useful detail came out of it: ReportPortal's **`service-auto-analyzer`** is a
   dedicated ML service for clustering failures by cause. That is prior art for §16's Q5 (error
   cluster keys) and it is worth being explicit that Kronikol's version is deliberately the crude
   one — a capped, redactable string key, no model, no service — because the whole point is that it
   costs nothing to run. Q5's recommendation does not change; its framing gets one honest sentence.

---

## 18. Where this sits in the order

The direction note ranks this fifth, on the assumption that **adoption is the goal**. That ranking is
sound under that assumption: at 31 visitors a fortnight, a capability nobody sees compounds nothing.

Its closing aside is the relevant one: *if the goal is internal use or a portfolio piece, the order
inverts and cross-run history goes first, because it is the thing you would use daily.* Two
observations, without deciding it:

- 3.0.83–3.0.86 were **all user-reported bugs from one production report** — this suite is in daily
  earnest use, which is exactly the population that gets value from "this has been flaky for three
  weeks" on day one.
- The gate (§8.5) is the only item here that changes a team's behaviour rather than their
  information, and is worth the same whether or not anyone new arrives.

So this plan is written to be executed whenever it is green-lit, not to argue for its own priority.
With `LLM_FRIENDLY_PLAN.md` complete it has **no blocking dependency left** — M1+M2+M3 is the slice
that pays for itself immediately and leaves the report work for later.

### 18.1 What green-lighting this actually commits you to

A 2,000-line plan should say what it costs before asking for a decision, and until now this one did
not. **These are estimates — judgement calibrated against comparable shipped work in this repo, not
measurements** (§17.0's rule: a guess gets labelled as one). What *is* firm is the shape: every
milestone is independently shippable, and there is a real stop point after each.

| | Milestone | Relative size | Ships something usable on its own? |
|---|---|---|---|
| **M0** | Commit the harnesses, one relay dry run | **small** | no — but nothing blocks on it any more (§2.7) |
| **M1** | The ledger | **large** — the format, reader, writer, locking, suite scoping, fragments, four verbs | no: it records but says nothing |
| **M2** | The verdicts | medium | no on its own; **M1+M2 is the first coherent unit** |
| **M3** | Agent surfaces (digest, pointer, `::notice`, CLI) | small–medium | **yes — and this is the first release worth having** |
| **M4** | Gate and quarantine | medium | **yes**, and the only milestone that changes behaviour rather than information |
| **M5** | The HTML report | **largest** — payload, sparklines, section, toolbar, export, E2E | yes, and the most visible |
| **M6** | Behaviour history + CTRF interop | medium–large | **yes — and it is the only part nobody else gives away** (§0.2) |
| **M7** | Renames, `history doctor`, importers | medium | yes, maintenance-facing |
| **M8** | Docs, dogfooding, Kronikol4J, release | medium | required before any of it is real |

**Three honest stop points**, so this is not all-or-nothing:

1. **After M3** you have cross-run history that a human and an agent can both read, with no report
   work at all. This is the smallest thing worth releasing.
2. **After M4** you have the gate — the item §18 argues is worth the same whether or not adoption
   follows.
3. **After M6** you have the differentiated feature. §0.2 recommends reaching it *before* M5, because
   the HTML is the most expensive slice and the least unique.

**The cheapest possible start is smaller than M1.** `RunAttempt` (§3.3) is eight edits on in-flight
work and must happen before 3.1.0 is tagged or it gets more expensive; it is useful even if this plan
is never built, because it makes the standard JSON honest about re-runs. That decision is due now.
Everything else can wait for a green light.
