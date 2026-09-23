# History dashboard store plan — full-fidelity trend history in a database

**Date:** 2026-09-19 · **Repo version:** 3.22.1 (`82abeb7f`) · **Status: investigation complete,
nothing implemented, NOT green-lit.** §10 is the assumption ledger: what is RUN, what is only READ,
what is arithmetic, and what is neither. Every storage figure below is reproduced by
`HISTORY_DASHBOARD_STORE_PLAN.harness/measure.py` against real reports and a real CI ledger; nothing
here is an estimate unless it says so.

Answers the owner's question (2026-09-19): *a database-backed dashboard that saves as much as
possible and can view trends and any point in time — how feasible is that, especially after doing
everything in `OPEN_ISSUES_TRIAGE_2026-09-18.md`, how cost-effective is it on storage, and can it be
fundamentally better than the competition?*

**The answer in one line.** Feasible. Storage is off by two orders of magnitude from being the
constraint — **4.6 GB a year, full fidelity, for a repository running 90 runs a day** (this line read
3.2 GB until 2026-09-21: the first draft's lossy 95,598 figure, which F3 corrected everywhere but
here) — and the costs
that actually bite are analyzer compute, ingest bandwidth and custody of captured payloads. The
triage work is on the critical path rather than competing with it.

**Where it sits against the plans it touches.**

- **`DASHBOARD_PLAN.md` (2026-09-15) — complement, not contradiction.** That plan builds a static page
  over the history ledgers and its §6 declines four things by name: no database, and *"no retention
  beyond the window — a team wanting two years wants a warehouse; CTRF and OTLP export exist for
  that."* This plan is that warehouse. Its §3 names retention beyond the window as one of the places
  the static dashboard *should not try to win*; this is the plan for winning it separately. Every
  decision already taken there (Observable Plot on vendored D3, dogfood on both Pages sites, verdicts
  computed on the .NET side and never in JavaScript) is adopted here unchanged. **Nothing in that
  plan is superseded.**
- **`CROSS_RUN_HISTORY_PLAN.md` §14 — extended along its own pointer, not broken.** §14 rules out
  "no server, database, hosted dashboard or account", and rules out unbounded retention with the
  words *"a team wanting two years wants a data warehouse and should export (CTRF/OTLP)"*. This plan
  takes that pointer and makes the export good enough to be the warehouse. The socket rule survives
  intact, because §14 states the distinction itself: *"the platform's own tooling may fetch prior
  fragments into a directory and Kronikol reads them offline. Who makes the call is the distinction;
  that there is a call is not."* Kronikol writes files; the workflow moves them. §8.5 proposes the
  amendment sentence.
- **`HISTORY_ANALYZER_COST_PLAN.md` (#91) — hard prerequisite**, not a nice-to-have. §7.1.
- **`HISTORY_VERDICT_NOISE_PLAN.md` (#75, #83) — hard prerequisite.** §7.2.
- **`PLATFORM_FOUNDATIONS_PLAN.md` — supplies the storage unit already.** Its F2 contract (two NDJSON
  streams plus an options file) is the schema this plan stores. §3.1.
- **`MCP_PLAN.md` §10 — the credential-sink argument decides the hosting question here too**, and
  decides it the same way: bring-your-own-bucket, §5.3.

---

## 0. Summary

Ten findings, seven of which change the design rather than confirming it, **numbered in the order
they were found, not in order of importance**. F3 and F7 were both found on 2026-09-20, after the
first draft, and each corrects an overclaim in it; **F9 is a pattern that had been recorded three
times in three places without being added up**.

| # | Finding | Where |
|---|---|---|
| **F1** | **Storage is not the constraint and no scale makes it one.** Full fidelity, every run, kept forever, costs **138,341 bytes per run** on a real 203-scenario suite, or **681 bytes per scenario-execution** — 4.6 GB a year at the consumer's measured 90.6 runs/day, and **$0 on Cloudflare R2, whose free tier is larger than the entire workload** (§2.4). The cost question is answered and should stop driving the design | §2 |
| **F2** | **Dedup does not work; the ids defeat it.** All 2,692 interactions in a run are byte-distinct — 0% exact-dedup saving. Eight simulated re-runs in which *nothing behavioural changed* still cost **154 KB each** under long-range zstd, 68% of a standalone run | §2.2 |
| **F3** | **Correlation ids are 43% of the compressed payload, but only Kronikol's own join keys may be renumbered.** The four id columns are 64,465 of 148,646 columnar bytes. Renumbering the two that are internal join keys is **lossless** (verified: 0 of 1,346 `requestResponseId`s appear in any content, header or URI) and takes a run from 181,953 to **138,341**. Renumbering ids *inside payloads* is **lossy**, and once the mapping needed to restore them is counted its real saving is 6.7%, not the 47% this plan first claimed | §3.2 |
| **F4** | **A third of the report is derived and should not be stored at all.** `diagrams` is 33.3% of the compact JSON and is regenerated from the interactions by a pipeline that already ships — `kronikol ingest` replays NDJSON into a full report today | §3.1 |
| **F5** | **The shape-rule versioning gap is real, and closes by storing less.** The analyzer refuses to compare fingerprints made by different `InteractionShape.Version` rules; a years-deep store spans rule changes, so today's ledger would be chopped into incomparable eras. Fingerprints must become *derived*, recomputed on a rule bump. The inputs cost **22,069 bytes per run** — and the ledger today spends **71% of a run line** on the fingerprints a store would not keep | §4 |
| **F6** | **Columnar buys nothing on size and everything on retention.** Columnar and row-wise compress the same (148,646 against 148,870, a wash — zstd already finds it). The reason to go columnar is that bodies can expire on a timer while the cheap identifying fields stay for years | §3.3 |
| **F7** | **Replay ships; the projection does not, and the one writer loses data.** `kronikol ingest` genuinely rebuilds a report from records, but nothing turns a finished run back into records: `InteractionRecord.FromLog` drops `RequestResponseLog.PlantUml` (the record has no such field), drops `DurationMs`, and never emits step or assertion markers. Two of the three are live defects today, filed as #93 and #94. **This plan's first draft claimed the round trip from the existence of `FromLog`/`ToLog` and was wrong** | §3.1 |
| **F8** | **Two layers, two stores.** Payloads are never queried analytically: they are fetched whole, by run id, and they are 84% of the bytes. That is a blob workload. The stable layer is read as "this column across thousands of runs", which is a columnar workload. Putting payloads in a database is what makes this look like it needs a warehouse budget | §3.4 |
| **F9** | **The likely buyer is the least served, and it took three separate findings to notice.** Kronikol is a .NET product with a .NET-only differentiator, so the probable customer runs Azure DevOps, on Azure, with Entra ID — the profile with no Tier 0 recipe, no onboarding artifact, no possible Tier 1, and no store path. **But Azure is also the easiest provider for browser auth and ships the §6.2 proxy for free**, which makes it the *shortest* path to a complete stack rather than a grudging port | §6.3 |
| **F10** | **There is no atomicity story, and the ledger had one.** Eighteen lanes finish at once; the ledger handled that with `merge=union` plus a fetch-and-rebase retry. Object storage has no merge, and §3.5's mandatory compactor rewrites files while writers append and readers scan. **This is the one design gap on the list rather than an omission**, and retrofitting it onto a format already holding years of data is close to a rewrite | §3.4 |

**Read §13 beside this table.** A critical review on 2026-09-21 left F1 standing and challenges
several of the rest: M1's premise that a verdict never changes once written is false (R1), F3's
renumbering should be dropped rather than tuned (R3), F6's retention argument does not hold for
immutable objects (R5), F7 undercounts what `FromLog` loses (R4), F10 over-scopes the contention (R7),
and the slices build the one product nobody has asked for first (R16). No decision below was changed
by it; §11 questions 14 to 20 put the choices to the owner.

**The order of work was decided on 2026-09-21 (§8.0):** years of verdict-level trends first, any past
run's evidence second, and the facts layer, which is what the six slices below build, third and only
when somebody asks for it.

Six slices. None is breaking; none needs v4; every one is a MINOR bump except M0's two writer
defects, which are PATCH (§8.6).

| Slice | What | Depends on |
|---|---|---|
| **M0** | The storage unit. **Build the projection** (it does not exist), close three writer gaps, then prove the round trip on the facts | — |
| **M1** | Verdicts computed once at ingest and stored as rows | #91 S1, verdict-noise plan |
| **M2** | The store: columnar, bring-your-own-bucket, per-column retention, ids renumbered on write | M0 |
| **M3** | Fingerprints derived, not stored; the backfill a rule bump triggers | M2 |
| **M4** | The dashboard reads the store — `DASHBOARD_PLAN.md`'s page, pointed at a warehouse | M1, M2 |
| **M5** | Migration from an existing ledger, and what it cannot carry (§4.3) | M2 |

---

## 1. How far each claim was checked

- **RUN** — executed here, output read.
- **COMPUTED** — arithmetic over RUN numbers.
- **READ** — the code or the issue was read and the claim follows; nothing was executed.
- **REFERENCE** — a vendor list price, from knowledge and **not checked against the vendor today**.

| Claim | Level |
|---|---|
| A real 203-scenario report is 7,646,936 B raw / 5,063,819 B compact | **RUN** |
| `httpInteractions` 52.1%, `diagrams` 33.3%, `steps` 11.5% of the compact bytes | **RUN** |
| gzip 604,152; zstd-19 225,196; minus derived diagrams 181,953; + join keys renumbered **138,341** | **RUN** |
| Entropy floor 75,825 B | **COMPUTED** from the RUN count of distinct ids and timestamps |
| A no-change re-run still costs ~154 KB under long-range zstd | **RUN** — eight re-runs, ids refreshed and clocks shifted, §2.2 |
| All 2,692 interactions in one run are byte-distinct | **RUN** |
| Correlation ids are 43% of columnar bytes; 64,465 → 6,238 renumbered | **RUN** |
| Columnar and row-wise compress within 0.2% of each other | **RUN** |
| The re-templatable core is 22,069 B/run | **RUN**, and **READ** for *which* fields it must hold — `InteractionShape.Target` and `.Calls` (`InteractionShape.cs:152-200`) read exactly caller, service, method, URI, dependency category, status, the pairing id, and the first line of content for statement-shaped dependencies only |
| The analyzer refuses to compare across rule versions | **READ** — `HistoryAnalyzer.cs:349-354`, the evidence string "fingerprinted by an earlier rule" |
| Fingerprints are 71% of a run line | **RUN** — `callSets` + `shapeSet` + `shapeOrdered` = 8,893 of 12,514 B on the consumer's last line |
| The consumer runs 90.6 runs/day across 18 suites; 412 runs in 4.5 days; 81,292 scenario-runs; the whole ledger is 195,737 B zstd, 475 B/run | **RUN** — BreakfastProvider's `kronikol-history` branch, fetched 2026-09-19 |
| `kronikol ingest` already replays NDJSON captures into a full report | **READ** — `IngestCommand.cs:7-12` |
| `InteractionRecord.FromLog` drops `PlantUml`, `DurationMs` and every marker field, and the record has no `plantUml` property | **READ**, 2026-09-20 — the 29 `JsonPropertyName` members of `InteractionRecord.cs`, and the assignment list of `FromLog` at `:164`. **This corrects a claim in this plan's first draft**, which inferred a lossless round trip from the existence of the two methods without reading either |
| A measured duration is believed over an inferred one, and is the only source for a single-record call | **READ** — `ReportGenerator.cs:4046-4062`, its own comment |
| `TrackingDiagramOverride.InsertPlantUml` is public and defaults to `DiagramMarkerKind.Custom` | **READ** — `TrackingDiagramOverride.cs:70,113`; `StepCollector.cs:100`, `Track.cs:456` and `TabularInputs.cs:84` pass specific kinds |
| The tests stream carries feature, rule, tags, outline, examples, attachments, steps and errors; `stableId` and `isHappyPath` are derived | **READ** — `TestRunRecord.cs` (32 members), `ScenarioStableId.Compute`, `FeatureSynthesizer.cs:174` |
| A heavy suite's reports are 82,685,589 and 64,473,328 B at 263 scenarios; `diagrams` 35,307,037 raw, 4,351,222 gzipped | **READ** — issue #85's measurements, not re-measured here |
| `__iflowSegments` is 62.7% of the ClickHouse-lane report and 91% redundant | **READ** — issues #89 and #86 |
| A heavy suite costs ~1.4 MB/run full fidelity | **ESTIMATE**, §2.3 — the weakest number in this plan and labelled so |
| Renumbering ids inside payloads is lossy, and costs back almost everything it saves | **RUN**, 2026-09-20 — 181,953 lossless, 95,645 renumbered, 169,840 once the restoring mapping is counted |
| Renumbering `requestResponseId` and `traceId` is lossless | **RUN** — 0 of 1,346 `requestResponseId`s occur anywhere in `content`, `headers` or `uri`, and `ToLog` hashes any string to a stable Guid, so sequential integers preserve every pairing |
| 681 bytes per scenario-execution; 32.6M scenario-executions and 433M captured calls over five years | **COMPUTED** from the RUN figures and the ledger's own run rate |
| R2 $0.015/GB-month, egress free, 10 GB free tier; B2 $0.00695, egress free to 3x stored; S3 $0.023, 100 GB/month egress free then $0.09; Azure Hot $0.018, egress $0.087 | **REFERENCE, checked 2026-09-20** against vendor pricing pages (R2 and B2 read directly; S3 and Azure via 2026 pricing guides, because the vendors' own tables did not render) |
| Azure Blob has no native S3 API, so one S3 code path does not cover it | **REFERENCE**, not verified against Azure's documentation here. §11 raises it as an open question rather than stating it as fact |
| Competitors store outcomes, not behaviour | **READ** — `DASHBOARD_PLAN.md` §3 and its §2 competitor survey, 2026-09-14; not re-surveyed |

### 1.1 The harness

`HISTORY_DASHBOARD_STORE_PLAN.harness/measure.py`, Python 3.14 (it needs `compression.zstd`, new in
3.14). It prints numbers only and never report content — the `CLAUDE.md` rule about not opening a
report applies to the agent, and the script is written so following it costs nothing.

```
py -3.14 plans/HISTORY_DASHBOARD_STORE_PLAN.harness/measure.py C:\Code\BreakfastProvider\tests history.jsonl
```

The ledger argument is optional:

```
git -C C:\Code\BreakfastProvider fetch origin kronikol-history
git -C C:\Code\BreakfastProvider show FETCH_HEAD:history.jsonl > history.jsonl
```

Reports measured: BreakfastProvider's six component lanes, 4.6–7.6 MB, the largest being ReqNRoll at
203 scenarios and 2,692 HTTP interactions. Ledger measured: 447 lines, 412 runs, 18 suites,
2026-09-14 to 2026-09-19. Compression is zstd level 19 with long-distance matching and a 2 GB window
throughout, so cross-run redundancy is actually found rather than missed by a small window — that
choice is what makes F2 a finding instead of an artefact.

**Read the `at` stamps, not the file's age.** The ledger grew from 1.42 MB to 4.73 MB between
`DASHBOARD_PLAN.md`'s measurement (2026-09-14) and this one. Figures quoted from that plan are its
own and are not restated here as if fresh.

---

## 2. What a run costs to keep

### 2.1 One run, every storage model

203 scenarios, 2,692 HTTP interactions, 5.06 MB of compact JSON:

| Storage model | bytes/run | lossless? | note |
|---|---|---|---|
| raw pretty JSON | 7,646,936 | yes | 34% of it is indentation |
| compact JSON | 5,063,819 | yes | |
| gzip | 604,152 | yes | what a CI artifact costs today |
| zstd-19 | 225,196 | yes | 2.3x better than gzip for one flag |
| + diagrams dropped (derived) | 181,953 | yes | regenerated on replay, §3.1 |
| **+ join keys renumbered** | **138,341** | **yes** | **the engineered figure**, §3.2 |
| + *every* id renumbered | 95,645 | **no** | destroys the payloads |
| …that, plus the mapping to undo it | 169,840 | yes | worse than 138,341 |
| irreducible entropy (volatile layer) | 75,825 | — | random ids; nothing gets below this |
| ledger run line (verdict layer only) | ~475 | — | 412 real runs → 195,737 B total |

**Corrected 2026-09-20, and the correction is the interesting part.** This plan's first draft gave
95,598 as the engineered figure. That measured renumbering *every* id in the report, which is a lossy
transform: rewriting `{"orderId":"7f3a9c..."}` to `{"orderId":"k42"}` stores a redacted run, not the
run. Restoring it needs a mapping of the original ids, and that mapping costs back nearly everything
saved, 181,953 to 169,840: a **6.7% saving, not the 47% claimed**. What is legitimately free is
renumbering Kronikol's own join keys (§3.2). **The honest figure is 138,341, 45% above the first
draft's**, and the conclusion survives it comfortably.

Per scenario-execution that is **681 bytes**, carrying every call the scenario made and its payloads.
The random ids do not compress away and never will: cost is strictly linear in runs, and the volatile
layer's entropy floor (75,825) is 55% of a run. What controls the long-run bill is not compression
but retention (§3.3).

### 2.2 The re-run is the cost, and dedup does not touch it

The instinct is that a hundred runs of an unchanged suite should cost barely more than one. Measured,
they do not. Every one of the 2,692 interactions in a single run is byte-distinct — exact dedup saves
**0%** — because each carries its own ids and clock. Simulating eight re-runs in which nothing
behavioural changed, refreshing the ids and shifting the clocks the way a real re-run does:

| runs | cumulative | incremental | per run |
|---|---|---|---|
| 1 | 225,196 | 225,196 | 225,196 |
| 2 | 385,118 | 159,922 | 192,559 |
| 4 | 693,498 | 154,974 | 173,374 |
| 8 | 1,308,994 | 154,024 | 163,624 |

**A re-run that changed nothing still costs 154 KB**, 68% of a standalone run, and the incremental
cost flattens rather than falling. Cross-lane confirms it from the other direction: storing a second
lane's report given the first costs 82–85% of storing it alone.

This is the finding that kills the obvious architectures. Content-addressed dedup, delta chains
against the previous run, and "store the diff" all assume a redundancy that the ids destroy. What
works instead is to stop storing the ids as ids (§3.2) and to separate what is stable from what is
not (§3.3).

### 2.3 The heavy suite — the weakest number here

Issue #85 measured a different consumer's reports at **82,685,589** and 64,473,328 bytes for 263
scenarios, 16x this one per scenario, because that suite's driver traces a layer deeper and its
diagrams are 35.3 MB of raw PlantUML. Removing the derived diagrams leaves ~47 MB of source facts;
applying this plan's measured compact→engineered ratio of 35x gives **~1.4 MB per run**.

That is an **ESTIMATE**, composed from one plan's ratio and another issue's byte counts, and it is
the only figure in this plan not produced by the harness. It should be re-measured against a real
report from that suite before anyone sizes anything on it. It does not change the conclusion — 1.4 MB
a run at 90 runs a day is 46 GB a year, about a dollar a month — but it is the number most likely to
be wrong, and if it is wrong it is probably low.

### 2.4 What that costs

At the consumer's measured rate, 90.6 lane-runs and **17,885 scenario-executions a day** across 18
lanes, a median 203 scenarios per run:

| Layer kept | GB/year | GB at 5 years |
|---|---|---|
| verdict/trend layer only | 0.016 | 0.08 |
| stable layer only (kept forever) | 0.73 | 3.5 |
| **full fidelity, engineered** | **4.6** | **22.2** |
| **tiered: stable forever + payloads 90 days** | — | **4.5, and it stops growing** |
| gzipped reports as they come off CI | 20.0 | 100 |
| heavy suite (§2.3 ESTIMATE) | ~46 | ~230 |

Five years of full fidelity is **32.6 million scenario-executions and 433 million captured calls**.

**Vendor prices checked 2026-09-20.** At the tiered 4.5 GB, about 8,000 writes and 300,000 reads a
month, and a dashboard pulling on the order of 120 GB/month:

| | storage | egress | total/month |
|---|---|---|---|
| **Cloudflare R2** ($0.015/GB, egress free, 10 GB free tier) | **$0** | **$0** | **$0** |
| Backblaze B2 ($0.00695/GB, egress free to 3x stored) | $0.03 | $1.07 | ~$1.10 |
| AWS S3 Standard ($0.023/GB, 100 GB/month egress free, then $0.09) | $0.10 | $1.80 | ~$2.06 |
| Azure Blob Hot ($0.018/GB, egress $0.087 after the first 100 GB/month, which is free) | $0.08 | $1.74 | ~$1.82 |

**Azure row corrected 2026-09-21.** It read "~$10 / ~$10": the 100 GB free allowance had been applied
to S3 and not to Azure, which has the same one (§13 R17). The 120 GB/month under all four rows has no
derivation and is about a hundred times a realistic figure (same finding).

Two things follow, and the second matters more. **Storage is at most 5% of the bill**, so the per-GB
rate is very nearly irrelevant and this plan should stop quoting it as though it were the number.
And **the entire workload fits inside R2's free tier**: 4.5 GB against 10 GB, with egress free by
policy rather than by allowance. Untiered at five years it is $0.18/month; at 25x the suite size,
$1.73.

#### The axes price does not cover (checked 2026-09-20)

Price was the first axis looked at and it turned out to be the least decisive. Four others were
checked afterwards, and two of them change a decision.

**Lifecycle expiry exists on all four, so §3.3's tiering is implementable anywhere.** Two wrinkles
worth knowing before writing the recipe: R2 caps a bucket at **1,000 lifecycle rules** and removes
objects within roughly 24 hours of expiry rather than instantly; B2's model is **version-oriented**
(`daysFromUploadingToHiding` plus `daysFromHidingToDeleting`) rather than age-oriented, so its recipe
differs from S3's. Neither is a constraint on two or three global rules; the R2 cap would only bite a
rule-per-repository design.

**Do not tier the payload layer to Infrequent Access or Cool.** This is the obvious optimisation
("payloads are rarely read"), and the arithmetic kills it. Payload objects are ~116 KB and
Standard-IA bills a **128 KB minimum per object**:

| | |
|---|---|
| payload objects in steady state | 8,154 (90.6/day × 90 days) |
| actual | 0.948 GB |
| billed at the 128 KB minimum | 1.069 GB, a **12.7% inflation** |
| S3 Standard | $0.0218/month |
| S3 Standard-IA | $0.0134/month, **plus $0.01/GB retrieval on every drill-down** |
| **saving** | **$0.008/month** |

Under a penny, for three new failure modes. **Keep every layer in Standard or Hot.**

**The trap, and the reason the row above is a rule rather than a note: a minimum storage duration
versus a *configurable* retention window.** §11 question 5 makes the payload window configurable.
Standard-IA imposes a **30-day minimum** and Azure Cold a **90-day minimum** — the latter exactly the
default payload lifetime. A customer who shortens retention to 30 or 60 days on Azure Cold pays for
90 regardless. A fixed 90-day policy would be safe; a configurable one is not. **If a tier with a
minimum duration is ever adopted, the retention option must refuse to be set below it.**

**Two smaller findings.** CORS with `range` among the allowed headers is configurable on B2, R2 and
S3, so the browser read path is supported at the provider level — §11's spike still has to prove it
end to end. And **data residency is free under bring-your-own-bucket**: the customer picks the region,
so residency and GDPR questions answer themselves rather than becoming a vendor interrogation. That
is an unnamed advantage in §6 and a real one for exactly the enterprise buyers §6.2 is about.

**Deliberately not chased:** durability (all four claim roughly eleven nines), maximum object size,
read-after-write consistency, and request rate limits. None plausibly differentiates at 90 writes a
day and a few gigabytes.

**The conclusion to carry forward: stop optimising for storage.** The engineering in §3 is worth
doing because it makes retention policies simple and queries cheap, not because the bytes cost
anything.

---

## 3. The levers, and where each layer lives

### 3.1 Store facts, not reports — the replay path ships, the projection does not

`diagrams` is 33.3% of the compact report and is **derived**: PlantUML emitted from the interaction
log, including the user-injected PlantUML that rides on `RequestResponseLog.PlantUml`. Storing it is
paying twice for the same information.

**The replay direction ships and works.** `kronikol ingest` "replay[s] NDJSON interaction captures
(and an optional tests file)" into `TestRunReport.html` (`IngestCommand.cs:7-12`). The record pair is
already a flat table: `InteractionRecord` carries 29 nullable fields, `TestRunRecord` 32, and between
them they hold feature, description, rule, tags, outline id, examples block and values, attachments
(`path`, `mediaType`, `step`), per-step keyword, level, background, doc string and table, error and
stack trace. The scenario fields the streams do not carry are **derived and already recomputed on the
ingest path**: `stableId` is `ScenarioStableId.Compute(suite, feature, scenario, outlineId,
exampleValues)`, `isHappyPath` is `HappyPathDetection.AnyHappyPathTag(tags)`
(`FeatureSynthesizer.cs:174`).

**The projection direction does not exist, and the one writer that could serve it loses data.**
Verified 2026-09-20, after this plan's first draft asserted the round trip from the existence of
`FromLog`/`ToLog`; the assertion was wrong and is corrected here rather than quietly edited away.
`NdjsonInteractionWriter.Log` (`:42`) is the only path from a `RequestResponseLog` to a record, it is
referenced in the repository from a single OTLP doc comment, and `InteractionRecord.FromLog` (`:164`)
drops three things:

| Dropped by `FromLog` | Consequence |
|---|---|
| `RequestResponseLog.PlantUml` | `InteractionRecord` has **no `plantUml` property at all** (29 fields, none of them it). `Step` and `Assertion` markers survive anyway, because `ToLogs` re-derives their PlantUML from structured fields (`StepDelimiterPlantUml`, `AssertionNotePlantUml`). `DiagramMarkerKind.Custom`, which the public `TrackingDiagramOverride.InsertPlantUml` defaults to (`:70`, `:113`), cannot be re-derived and is lost |
| `DurationMs` | A capturer's own measurement degrades to the request/response timestamp delta, and is lost outright for a call sent as a single record — which the NDJSON contract permits, and which `ReportGenerator.cs:4052-4054` names as the only source for such a call |
| `Text`, `Keyword`, `Table`, `DocString`, `Passed`, `Message` | Never populated from a log, so the existing writer cannot emit step or assertion markers at all. These fields exist for external capturers writing records directly, which is what the writer was built for |

The first two are **live defects for anyone writing records through the OTLP tap today**, independent
of this plan, and are filed as **#93** (the missing `plantUml` field) and **#94** (`DurationMs`)
rather than folded into it. The third is a missing half of a
writer built for an inbound contract, and is this plan's work.

`PLATFORM_FOUNDATIONS_PLAN.md` arrived at the same unit from the opposite direction: its F2 contract
is "two NDJSON streams and an options file", the boundary every platform's capturer writes and the
shared renderer reads. **That contract is this plan's schema.** Two plans reaching the same storage
unit from unrelated requirements is the strongest evidence in this document that it is the right one.

So: store `interactions.ndjson`, `tests.ndjson` and `kronikol.render.json`. "Any point in time" is
`kronikol ingest` over the rows for one run. Reports become a projection, not an asset — once the
projection exists, which is M0 and is more work than this plan first implied.

**The open question this creates** is which renderer a time-travelled report should use — the version
that ran, or today's. The report already carries `kronikolVersion`, so both are answerable; §11
asks which is the default.

### 3.2 Renumber the correlation ids

Laid out columnar, the compressed cost of a run divides like this:

| column | zstd bytes | share |
|---|---|---|
| `content` | 42,888 | 29% |
| `headers` | 27,077 | 18% |
| `requestResponseId` | 26,879 | 18% |
| `traceId` | 21,222 | 14% |
| `activityTraceId` | 8,691 | 6% |
| `activitySpanId` | 7,673 | 5% |
| `uri` | 3,004 | 2% |
| everything else (14 columns) | ~11,000 | 7% |

**The four correlation-id columns are 64,465 bytes, 43% of the run, and they are pure entropy.**
Renumbering them to per-run sequential integers costs 6,238. But **whether that is free depends
entirely on whose id it is**, and the first draft of this plan got that wrong.

**The rule: an id may be renumbered only if its literal value is meaningless outside the run.**

| Id | Renumberable? | Why |
|---|---|---|
| `requestResponseId` | **yes** | Kronikol's own pairing key. Verified: **0 of 1,346 appear anywhere in `content`, `headers` or `uri`**, and `ToLog` hashes any string to a stable Guid, so integers preserve every pairing |
| `traceId` | **yes** | Kronikol's own grouping key, same reasoning |
| `activityTraceId`, `activitySpanId` | **only if nobody joins them externally** | W3C ids. If a customer correlates them with their own tracing backend, renumbering severs that link. §11 asks; the conservative default is to keep them verbatim, which forgoes 16,364 bytes |
| **ids inside `content`, `headers`, `uri`** | **NO** | **These are the evidence.** An order id in a response body is what the reader came to see. Rewriting it stores a redacted run |

Renumbering the two safe columns takes a run from 181,953 to **138,341**, a genuine 24% saving for
one pass at write time.

**Why the first draft's 40% was wrong.** It renumbered every id everywhere, reaching 95,645, and
counted that as the engineered figure. To reproduce a payload you need the originals back, and the
mapping is 5,333 random ids: 74,195 bytes even stored as raw bytes rather than hex. 95,645 + 74,195 =
169,840, against 181,953 for doing nothing. **A 6.7% saving, not 47%**, and lossy in between. The
lesson generalises: a transform that saves space by discarding identity is not a compression result
unless the discarded part is counted.

One constraint survives from the first draft: **the mapping is per run and must never be reused
across runs.** Reusing it would manufacture cross-run byte identity that does not exist, and corrupt
exactly the comparisons the history feature makes.

### 3.3 Two layers that age differently

Columnar and row-wise compress the same — 148,646 against 148,870, a difference of 0.2%. zstd with a
long window already finds the structure that a column layout makes explicit, so **there is no size
argument for columnar at all.**

The argument is retention. Split a run into two layers:

- **The stable layer** — URI, method, service, caller, status, dependency category, the statement
  head, the scenario roster, results and durations. About **22 KB a run** (§4), it is what every
  trend, verdict and fingerprint is computed from, and it is what a rule change needs back.
- **The volatile layer** — bodies and headers, ~70 KB of the 148 KB, the part a reader wants when
  debugging one run and almost never wants at two years.

Keep the stable layer for the life of the repository and expire the volatile layer on a timer, and
**five years costs 4.5 GB in steady state instead of 22.2 GB, and stops growing**. That policy is
only expressible if the storage format lets a field be dropped without rewriting the run, which is
what columnar buys and why the recommendation survives a 0% size advantage.

This is also the real answer to "how does storing random GUIDs not become enormous". It is not that
they compress: they do not, and cost is strictly linear in runs. It is that **the layer holding them
is the layer that expires.**

### 3.4 Where each layer lives

The whole stack, across this plan and `DASHBOARD_PLAN.md`, so the pieces can be seen at once. Nothing
in it is green-lit.

```
CAPTURE   .NET adapters  (Java capture incomplete - see the constraint in §6)
          └─ InteractionRecord + TestRunRecord NDJSON + kronikol.render.json

INGEST    kronikol ingest / a CI step
          ├─ HistoryAnalyzer computes verdicts once, stored as rows   (M1, needs #91 S1)
          ├─ join keys renumbered                                     (§3.2, the two safe ones)
          └─ compaction: per-run -> daily -> monthly                  (§3.5, mandatory)

STORE     the customer's own bucket, S3 API (R2 / B2 / GCS / S3; Azure is a gap, §11)
          ├─ verdicts       ~79 MB / 5yr    Parquet
          ├─ stable facts   ~3.7 GB / 5yr   Parquet, partitioned + compacted
          └─ payloads       ~0.9 GB @ 90d   blob objects keyed by run id

QUERY     DuckDB (CLI / CI)  ·  DuckDB-WASM (browser)  ·  kronikol query

PAGE      one index.html from `kronikol dashboard build`   (DASHBOARD_PLAN §4.3)
          ├─ Observable Plot 0.6.17 on vendored D3 7.9.0, pinned, inlined, never a CDN
          ├─ SVG marks with aria-label - what the E2E tests locate
          ├─ hash CSP computed at build; one inline <script>, one inline <style>
          ├─ no verdict computed in JavaScript, ever
          └─ ledger text via textContent (it is test data)

SERVE     static: GitHub Pages, or the customer's bucket behind their own auth  (§6.1)

WRITES    prefilled pull request now  ·  federated bucket write once the store lands  (§6.1)
```

**Deliberately absent:** no server, no framework, no CDN, no web font, no external stylesheet, no
database engine to operate. Plot and D3 are embedded resources of the tool.

The two data layers have opposite access patterns, so they want opposite stores. This is F8, and it is
what keeps the bill small.

| Layer | Bytes/run | How it is read | Store |
|---|---|---|---|
| verdicts | 475 | scan every run; 80 MB at five years | one Parquet file, or today's JSONL unchanged |
| stable facts | 22 KB | one column across thousands of runs; full-column backfill (§4) | **columnar on object storage** |
| payloads | 116 KB | fetched whole, by run id, rarely | **blob storage**, one compressed object per run |

**Payloads are never queried analytically.** They are read when a human opens one run. That is a blob
access pattern, and it is 84% of the bytes. Putting them in a database is the decision that would
make this look like it needs a warehouse budget.

**Recommended for the columnar half: Parquet on object storage, queried with DuckDB.** Over
ClickHouse, for now, on three grounds: it needs no server, which is what keeps §5.3's
bring-your-own-bucket answer intact; DuckDB-WASM can query Parquet over HTTP range requests, which
would let `DASHBOARD_PLAN.md`'s deliberately backend-free page run real queries over years of
history; and at 4.5 GB steady state nothing is strained. ClickHouse stays the upgrade path for a
5,000-scenario suite wanting sub-second org-wide queries, and the schema carries over unchanged.

**Rejected, with reasons, because they will be proposed:**

- **Document database.** Worst of the three. Whole documents read to get two columns; per-column
  expiry becomes a rewrite of every document; per-GB cost 5-10x object storage; and a
  5,000-scenario run's interactions exceed the document limits (Mongo 16 MB, Cosmos 2 MB) so it
  shards anyway, losing the one advantage.
- **Relational SQL.** Fine at today's scale and gives per-column retention through `DROP COLUMN`,
  but storage is ~$0.115/GB-month against $0.015, and both dominant workloads (column scans for
  trends, full scans for backfill) are analytics on an OLTP engine. At the 5,000-scenario regime it
  is the wrong tool, and migrating later is worse than starting columnar.
- **Blob storage alone.** Right for payloads, useless for trends: every query becomes a full
  download.

**Unverified and load-bearing:** `DASHBOARD_PLAN.md` §2 measured that `raw.githubusercontent.com`
sends `Access-Control-Allow-Origin: *`, but **did not measure whether it honours HTTP range
requests**, which DuckDB-WASM needs in order not to download whole files. If it does not, the
static-page query story needs a different host (R2 and S3 both range fine). A one-hour spike, and it
belongs before this approach is green-lit.

#### Concurrency: the part the ledger solved and this had not

**Added 2026-09-20, after the owner asked what the plan was missing. It is F10, and it is the one
item that is a design gap rather than a gap in the writing.**

The ledger's concurrency story was deliberate and dogfooded: an orphan branch with `merge=union` in
`.gitattributes`, a fetch-and-rebase retry around the push, and eighteen lanes exercising it on every
CI run. **Object storage has no merge**, and nothing in §3.4 as first written replaces it.

Two races, both live:

- **Two lanes writing the same logical partition.** Last-writer-wins silently loses a run.
- **Compaction against live traffic.** §3.5 makes compaction mandatory; a compactor rewriting a
  month's file while a writer appends, or while a reader is part-way through a scan, can produce a
  torn or vanished read. The reader here is a dashboard, so the failure is visible and confusing
  rather than loud.

**The standard answer is a table format with atomic commits** — Iceberg or Delta Lake — or, far
simpler and probably right at this scale, **a manifest with an atomic pointer swap**: writers only
ever add immutable files, a small manifest lists the current set, and publishing a new version is one
conditional write of the manifest. Readers resolve the manifest first and then read a fixed file set,
so compaction never disturbs them. Object stores support the conditional write this needs
(S3 `If-None-Match` / `If-Match`, and R2 and Azure have equivalents), **though that is REFERENCE and
not verified here**.

Deciding this is **M2's first task, ahead of the layout**, because a format already holding years of
data cannot have atomicity retrofitted cheaply. §11 asks the owner to confirm the manifest approach
over a full table format.

#### The fork this creates, which nothing has resolved

`DASHBOARD_PLAN.md` §4.3 is explicit that **the data is inlined**: "One `index.html` … The data is
inlined, gzip + base64, decoded with the report's `decompressGzipBase64`." That works because
`history.view.json` is small — 116 KB gzipped on BreakfastProvider, about 420 KB at 1,000 runs.

§3.5 puts the hot path at a **79 MB verdict layer** over five years. **That cannot be inlined**, and
querying Parquet with DuckDB-WASM is a different page architecture, not the same page pointed
elsewhere. M4's original wording ("the page, pointed at a store instead of a ledger view") glossed
over this and has been corrected.

A second constraint sharpens it: **the E2E project opens pages from `file://`**, where Chromium blocks
`fetch()` — which is part of why inlining was chosen in the first place. Any fetching design needs a
real local HTTP server for its tests.

| | Route | Consequence |
|---|---|---|
| a | Inline everything | Breaks past a few thousand runs |
| b | Full DuckDB-WASM query page | Abandons inlining, needs range requests, changes the E2E story |
| **c** | **Inline a build-time summary; query the store for drill-down and deep history** | **Recommended** |

**(c) is close to free**, because the inlined view is *already* a derived summary: keep it bounded by
construction (a window, pre-aggregated at build) and let the page reach into the store only for "show
me 2024" or "show me this run's payloads". The page stays instant, still works offline, keeps today's
E2E approach for the default path, and **only the drill-down depends on range requests** — which is
the part §11's spike already covers. §11 records the decision as open.

---

### 3.5 Performance: the layout decides it, not the medium

**Unmeasured. This section is mechanism and arithmetic; no query has been timed.** §11's spike is
where it becomes evidence, and §10.3 records it as this plan's largest remaining unverified claim.
The owner asked how anything built on blob storage can be performant, and the short answer is that
blob storage is not what makes it slow. Layout is.

Two access patterns, and only one is a question:

- **A payload fetch** is one GET for one object, on the order of 30-80 ms to first byte.
  Indistinguishable from loading any web asset.
- **A trend query** is the question, and the answer is that the data volume is trivial while the cost
  is almost entirely round trips.

| Layer | Total at 5 years | What one trend query reads |
|---|---|---|
| verdicts | 79 MB | kilobytes: one scenario is ~9,190 rows |
| stable facts | 3.7 GB | a few MB, after column projection and row-group pruning |
| payloads | 0.9 GB | never scanned; fetched by key |

Parquet's footer carries per-row-group statistics, so a reader skips the groups that cannot match and
range-requests only the surviving column chunks: 1-5% of a file for a typical query. **The number
that decides latency is therefore how many files must be opened**, which is a layout decision and not
a property of the storage medium:

| Layout | Files a full-history query opens | Order of magnitude |
|---|---|---|
| one file per run | 165,421 | minutes. Pathological |
| monthly, compacted | ~60 | ~400 ms |
| one verdict file plus a partitioned stable layer | 1-10 | ~150-250 ms |

**Compaction is mandatory, not an optimisation.** Ingest writes one object per run, rolls up daily,
and compacts to monthly. **The small-file problem is the one way this design fails on performance,
and it fails hard** — the naive layout is three orders of magnitude off. M2 carries it as acceptance.

**The lever that makes most of this moot.** The dashboard should not query the raw layers for its
common case. M1 computes verdicts at ingest and stores them as rows, and `DASHBOARD_PLAN.md` already
works this way, rendering a view the analyzer derived rather than deriving one in the page. **The hot
path is a 79 MB precomputed layer, not 22 GB of facts.** At that size the partitions can be fetched
whole and cached, so the common case does not depend on range requests working at all — a useful
property to keep while §11's spike is outstanding.

**What is being traded.** Blob-based: on the order of 150-400 ms a query, $0 a month. A Postgres or
ClickHouse server: single-digit ms, $20-50 a month plus something to operate, and
bring-your-own-bucket gone. For a dashboard opened a few times a day by a handful of people the first
is clearly right. The second stays documented as the upgrade path for anyone wanting sub-50 ms ad-hoc
analytics across an organisation.

---

## 4. Fingerprints become derived, not stored

### 4.1 The gap

Kronikol decides whether a scenario's behaviour changed by reducing each call to a shape:
`GET /orders/7f3a9c…` becomes `GET /orders/{id}`. Ids, timestamps and numbers are replaced so that
two runs doing the same thing hash the same. Those rules live in `InteractionShape` and **have already
changed three times** — `Version` is 3, and the XML doc comment records why each moved. (**2026-09-21:
make that four.** 3.22.2 (`4afe38a4`) shipped `Version = 4`, #75's id and `{bin}` rules, the day after
this was written: four rule sets inside the feature's first month. That strengthens this section, and
it has shifted the `InteractionShape.cs` line numbers this plan cites, so read them as of `82abeb7f`.)

Fingerprints made under different rules are not comparable, and the analyzer knows it. It filters
prior points to the current rule and, where the previous point used another, emits *"the calls in
`<run>` were fingerprinted by an earlier rule; behaviour is compared from the next run"*
(`HistoryAnalyzer.cs:349-354`).

For a ledger that is the right behaviour and a shrug: one run's comparison is lost. **For a store
promising trends over years it is a wall.** Every past rule change would split the history into eras
that cannot be compared across, and at the observed rate — three versions in the feature's first year
— five years of data would carry several such walls. "Has this scenario behaved differently since
2026?" would have no answer.

And it cannot be repaired after the fact, because **the ledger stores the already-templated text**.
The `shapes` line holds `GET /orders/{id}`; the original id is gone. Templating is one-way, so a v4
fingerprint cannot be derived from a v3 one.

### 4.2 The fix: keep the inputs, derive the fingerprint

A store, unlike a ledger, can keep what the templater consumed. Then a fingerprint stops being a
stored fact and becomes a derived column — recomputed for the whole history whenever the rules change,
as a background backfill. A rule bump becomes a job instead of a wall.

Reading `InteractionShape.Target` and `.Calls` (`InteractionShape.cs:152-200`), the inputs are exactly:
caller, service, method, URI, dependency category, the paired response's status, a pairing id, and —
for statement-shaped dependencies only — the first line of content, truncated to `HeadRaw` (2000).
Nothing else. Measured:

| column | zstd bytes/run |
|---|---|
| `pairId` (renumbered) | 2,069 |
| `stmtHead` (620 of 2,692 interactions) | 14,172 |
| `uri` | 3,004 |
| `method` | 913 |
| `serviceName` | 723 |
| `statusCode` | 521 |
| `callerName` | 450 |
| `dependencyCategory` | 417 |
| **re-templatable core** | **22,069** |

**22 KB a run** — 23% of the engineered full-fidelity run, 728 MB for a year at the consumer's rate.
Re-templating it is a linear regex pass over ~460 KB of raw text per run; a year of history is ~15 GB
of text, minutes of CPU, for an event that has happened three times in the feature's life.

### 4.3 It does not reach backwards, and that is permanent

**Added 2026-09-20.** §4.2 says a rule change becomes a backfill. That is true only of runs captured
**after** the store exists, and the reason is the same one that makes §4.1 a problem in the first
place: **the ledger keeps already-templated text.** A migration from an existing `kronikol-history`
branch can carry results, durations, verdicts and history, but not the re-templatable core and not
the payloads, because neither was ever written.

So **history migrated from a ledger is permanently stuck at the rule version that made it**, and can
never be re-fingerprinted. Anyone reading §4.2 would assume otherwise, which is why it is stated here
rather than discovered later. The practical consequences:

- The store's re-fingerprintable history begins on the day it is switched on. Migrated runs are
  readable and comparable among themselves, never across a future rule change.
- That is an argument for switching the store on **early** rather than after a long ledger has
  accumulated, and for saying so in the migration documentation.
- The migration itself is a slice nobody has written: every existing user has a ledger, and §8 has no
  path from one into the store. §8.6.

### 4.4 It is cheaper than what the ledger does today

On the consumer's most recent run line, `callSets` + `shapeSet` + `shapeOrdered` are **8,893 of
12,514 bytes — 71% of the line.** A store that derives fingerprints keeps none of that. So the
version-proof design is also the smaller one, which is the rare case where the robust choice needs no
justification on cost.

The `shapes` interning introduced in 3.17.0 stays useful for the ledger and is untouched by this.

---

## 5. What actually costs money

### 5.1 Analyzer compute — the real constraint

`HistoryAnalyzer.Analyse` is quadratic in scenario count and costs ~2 s at 5,000 scenarios × 50 runs,
6.9 s through the tool (#91, measured in `HISTORY_ANALYZER_COST_PLAN.md`). That is survivable at the
end of a CI run and fatal for a dashboard, where a page load must not re-derive years of verdicts.

Two changes, in order:

1. **#91's S1** makes the analysis linear — 2,060 → ~310 ms, and 6.9 → 1.0 s through the tool.
2. **Verdicts move to ingest.** A verdict for a run depends only on the runs before it, so it never
   changes once written. Compute it when the run arrives and store it as a row; the dashboard reads
   answers instead of deriving them.

Point 2 has one real consequence: the analyzer then runs in two places — the CLI and the ingest path
— and they must agree. That is a conformance-test obligation, and `PLATFORM_FOUNDATIONS_PLAN.md`
already establishes the pattern for one.

### 5.2 Ingest bandwidth

A heavy suite ships 82.7 MB per run from CI. At 90 runs a day that is 7.4 GB a day moving, which no
object store charges for on ingress but every CI job pays for in wall-clock. Issue #85 takes the same
file to 13.9 MB (payload fields compressed in place) or 6.1 MB (whole-file gzip); #89 takes the
ClickHouse-lane HTML from 31.8 MB to ~13.1 MB. **Doing #85 and #89 first turns the ingest problem into
a non-problem**, which is the strongest practical reason to run the triage before this plan rather
than alongside it.

### 5.3 Custody — the cost that is not measured in bytes

Captured request and response bodies are the product's whole advantage and its whole liability.
Holding them on behalf of other organisations makes the holder a data processor, with everything that
implies, and `MCP_PLAN.md` §10 already resolved the analogous question against hosting on the
credential-sink argument.

**The design that avoids it: the store lives in the customer's own bucket.** Kronikol ships the
schema, the writer, the query engine and the page; the data never leaves the customer's account.
Kronikol itself still opens no socket — the workflow moves the files, exactly the distinction
`CROSS_RUN_HISTORY_PLAN.md` §14 draws. This is not only the cheap answer and the safe answer, it is
also a competitive one (§6).

### 5.4 Secrets, and why deletion is the backstop rather than the control

Bounded retention on the volatile layer (§3.3) means exposure is limited by default rather than by
someone remembering to clean up, and per-customer deletion is a supported operation. Two caveats that
survive it:

- **A secret in a URL outlives body deletion.** A token in a query string lands in `uri`, which the
  stable layer keeps for years precisely so fingerprints stay rebuildable. Templating replaces ids and
  numbers, not `?token=…`. Capture-time redaction — `PLATFORM_FOUNDATIONS_PLAN.md` §4.4's two
  redactions — remains the control; deletion is the backstop.
- **Deleting from compressed columnar files is a rewrite**, not a `DELETE`. Fine when designed in,
  awkward when discovered later. It belongs in M2's acceptance, not in a later slice.
- **Subject-level erasure is a different and harder ask than per-repository deletion**, and this plan
  had only covered the latter. "Remove everything containing this person's data" means searching
  captured request and response bodies, which is what the payload layer is. Bounded retention limits
  the window but does not answer the request inside it. **An enterprise buyer under GDPR will ask**,
  and the honest answers are the payload layer's expiry, capture-time redaction, and, if it must be
  exact, a rewrite of the affected partitions. Recorded here rather than left to be discovered in a
  procurement questionnaire.

---

## 6. Competitive position

`DASHBOARD_PLAN.md` §3 surveyed the field on 2026-09-14 and is not re-surveyed here. Its finding:
every product has per-test pass/fail over time, flakiness, duration and quarantine; Codecov Test
Analytics is JUnit XML with 60-day retention, Allure keeps roughly 20 reports, Datadog and Trunk are
outcome-level. **None of them store what a test did**, because none of them capture it.

That plan's honest positioning was *interaction-level test history with no infrastructure*, losing
deliberately on retention, alerting and private hosting. This plan takes back the retention half and
adds a second axis the field cannot follow:

- **Behaviour over years, not outcomes over 60 days.** *This scenario still passes, but it makes five
  calls where it made three, and the change lands in this commit.* The verdict vocabulary already
  exists and is already noise-tuned against 412 real runs — `alternating` absorbed 90 returns,
  count confirmation took 13 verdicts to 2.
- **Point-in-time reconstruction.** The full report for any past run, re-rendered from stored facts
  (§3.1). Competitors keep a summary row; this keeps the evidence.
- **The data never leaves the customer's account.** Every hosted competitor requires shipping them
  your request and response bodies. Bring-your-own-bucket is a feature for exactly the buyers who
  care most, and §5.3 shows it is also the cheap and safe choice — the rare case where the three
  align.

**The constraint on all of it, which this plan had not recorded until 2026-09-20: the capture is
.NET-only.** Everything above rests on Kronikol capturing what a test did, and only the .NET
implementation does that fully. Kronikol4J's divergence ledger (updated 2026-09-18) states it
plainly: *"The report/diagram **output rendering** is byte-for-byte complete. The **capture
(instrumentation) breadth** and **configuration-options surface** … are the remaining work toward
every-feature parity."* `PLATFORM_FOUNDATIONS_PLAN.md`, the four-platform plan, is not green-lit.

So the claim available today is **best-in-class for .NET teams who adopt Kronikol's capture**, which
is a segment and a defensible one, not the field. A team arriving with existing JUnit XML gets
`history import --from-ctrf`: pass, fail, duration, flakiness, the commoditised half, with no drift
and no calls. **The differentiator carries an adoption toll**, and §6.2 uses that fact to sequence
what comes next.

### 6.1 The three exclusions are narrower than they read

`DASHBOARD_PLAN.md` §3 gives up on identity and workflow, alerting, and anything needing an account.
The owner asked (2026-09-20) whether a follow-up could close them. **It can, and two of the three are
closer than either plan implies** — because all three descend from one constraint, and §14 of
`CROSS_RUN_HISTORY_PLAN.md` states the escape in the same breath as the rule: *"Who makes the call is
the distinction; that there is a call is not."*

Each capability has a blocked form and a permitted one:

| Gap | Blocked | What a follow-up can ship |
|---|---|---|
| **Alerting** | Kronikol posts to Slack | Kronikol emits the event; the workflow delivers it |
| **Workflow** | An in-page mute button mutating server state | A prefilled pull request against the committed quarantine file |
| **Accounts** | Kronikol hosts identity | No accounts at all: inherit the customer's |

**Alerting is the easiest and is half-built.** §14 already says "the job summary and the gate's exit
code are the notification; the CI system delivers it", and `history gate` ships. What is missing is
per-verdict granularity ("tell me when *this scenario's* behaviour changes"), not delivery. The
pattern is already in the backlog: **#72** (triage plan 7) is a composite action under
`templates/github-actions/` that maintains a PR comment. Emit structured events, ship an action that
posts them, and Kronikol still opens no socket. **This is the first of the three exclusions to close** — a drift
verdict nobody sees is not worth the storage it sits in — though the onboarding action below should
precede even it.

**Workflow closes by leaning on git, and is arguably better for it.** Quarantine and aliases are
already committed files, so "approval to mute" is a pull request editing one: approval, audit (git
history), identity (commit author) and discussion (PR review) from infrastructure the team already
runs. `DASHBOARD_PLAN.md` §6 anticipates this — the page "may link to a prefilled issue or pull
request URL, which is a link and not state". Ownership is already covered: §14 notes "tags already
cover whose area". What is lost is in-page mutation — a click yields a prefilled PR, not an instant
change. That is real friction, and some teams will dislike it, but mute-requires-review is a
defensible position rather than an apology.

#### The mute friction, and three ways to remove it

"Mute" here is quarantine: marking a flaky or known-broken scenario so it stops failing the gate.
Competitors do it with a button that writes a record on their server. Kronikol's equivalent state is
already a committed file, `.kronikol/quarantine.json`, whose entries are
`HistoryQuarantineEntry(StableId, Reason, AddedBy, AddedOn, Until)` — **note that it already carries
who and when**, so the audit fields exist whichever route writes them.

The friction is: click, new tab, pull request, review, merge, next CI run picks it up, against a
competitor's click and done. The owner asked (2026-09-20) whether there is an alternative. There are
three, and the best falls out of §3.4 at no extra cost.

**A. Write the mute to the store rather than to git.** Once §3.4 exists, quarantine need not be a
committed file. It can live in the customer's bucket beside everything else, written by the page with
the same federated credentials §6.2 describes for reading: the browser exchanges an IdP token for
short-lived credentials minted by the customer's **own** cloud, and `AddedBy` comes from that
identity. That is **click, muted**, with no server and no Kronikol-held credential, and the
constraint survives because the customer's cloud mints the access. What is lost is review and the git
audit trail; both can be kept by reconciling — the page writes immediately, a CI step folds
bucket-side mutes back into the committed file. **This is the real fix, and it is nearly free because
the mechanism is already being built for reads.**

**B. The user's own token, held only in the user's browser.** A PKCE or device-flow OAuth against the
forge, with the token in the page's session and nowhere else; the page commits as that user, with
that user's permissions. Kronikol holds nothing. This is the reasoning §6.2 already accepts for OIDC
federation — a credential nobody but the user holds is not a sink — but it requires revisiting
`DASHBOARD_PLAN.md` §6's flat *"the page holds no token"*, which is stated absolutely rather than
reasoned. **Worth doing deliberately or not at all: this is precisely the kind of rule that erodes by
drift**, which is §6.2's gravity risk arriving through a side door.

**C. Make the pull-request path cheap.** Prefill title, body, reason and the exact diff so it is
click, review, merge with no typing; for a user with write access the forge's direct-edit URL skips
the pull request entirely. Free, ships today, revisits no rule.

**Recommended: C now, A when the store lands, B only if someone asks.**

**The question worth putting above all three: should muting be instant at all?** Quarantine
suppresses a signal, and quietly burying flaky tests is the standard failure mode of every dashboard
that has the button. That `Until` is already in the model — a quarantine that lapses on its own —
suggests it was designed as a deliberate, time-boxed act rather than a reflex. The friction may be
partly a feature, so this belongs as a **policy choice**, instant for teams that want it and
review-gated for teams that do not, rather than an assumption that faster is better.

**Accounts close by never having any, and this plan already changed that answer.**
`DASHBOARD_PLAN.md`'s account problem was specific: on free and Team plans a private repository's
Pages site is public. That is a GitHub Pages constraint, not a design one. Under §3.4 the data lives
in the customer's bucket; serve the page from there and **their** access control applies (S3 or R2
behind their SSO, or an internal host). Nothing is built, nothing is in the way, and §5.3's custody
argument is untouched because no credential is ever held.

**What genuinely stays weaker, and it is not capability:**

- **Onboarding friction.** A hosted competitor is "sign up, point at your repo". This is not, and it
  is adoption rather than capability. **Quantified below**, because this bullet asserted a cost for
  weeks without ever counting the steps.
- **Alert latency** is CI cadence, not push. For test results CI *is* the event source, so this
  mostly does not matter.
- **Cross-organisation aggregation** still needs someone to run something.

**Unvalidated, and worth saying here rather than burying in §10.3:** nothing measured shows these
three gaps are what loses a team's choice. One dogfood consumer is the whole evidence base. A second
consumer who is not the author would say more than any of this reasoning.

#### Onboarding, counted: three tiers, not one

The bullet above called onboarding the real strategic cost and never counted the steps. The owner
asked (2026-09-20) what installing this actually takes. Counted, **it is three tiers with very
different costs, and this plan had been conflating them.**

**Tier 0, the history ledger. Shipped today.**

1. Kronikol capture already in the test project (the real prerequisite, and §6's .NET-only constraint)
2. `dotnet tool install -g Kronikol.Tool`
3. **Copy about 45 lines of YAML** from the wiki's `Cross-Run-History`, section "The recommended
   shape": read the ledger before the tests, upload a fragment artifact, and one job with
   `contents: write` that folds and pushes to an orphan branch

That buys verdicts in the report, `kronikol query history` and the gate, with no cloud account, no
credential and no bucket.

**Tier 1, the static dashboard.** `DASHBOARD_PLAN.md`, not built. Two more steps:
`kronikol dashboard build` in a workflow, and Pages enabled. Still no bucket, no cloud account and no
credential beyond `GITHUB_TOKEN`. **Most teams should stop here** — **but only on GitHub or GitLab**;
the sub-section below shows Tier 1 is the rung that does not port.

**Tier 2, the warehouse.** This plan, not built. Create the bucket; configure CORS with `range`
allowed; set the lifecycle rules; create scoped credentials and add them to CI secrets; add the
projection-and-upload step; add the scheduled compaction workflow (§3.5); and, if the repository is
private, put an authenticating proxy in front and configure IdP trust (§6.2).

**So the install is progressive, which is a better story than the bullet told: nobody onboards into
Tier 2, they grow into it** when they want years of history and payload drill-down.

**The problem is Tier 0, the tier everyone meets first.** Forty-five lines of bash that creates an
orphan branch, juggles git worktrees and runs a fetch-and-rebase retry loop is a poor first
impression for the tier carrying the differentiator. And **`templates/github-actions/` does not
exist** — the repository ships 24 `dotnet new` templates and no CI actions at all.

**The fix is already in the backlog.** #72 (triage plan 7) proposes exactly this shape: a composite
action under `templates/github-actions/`. A `kronikol-history` action reduces Tier 0's third step to:

```yaml
- uses: lemonlion/kronikol/actions/history@v3
  with:
    suite: ${{ matrix.lane }}
```

**Recommended ahead of the alerting follow-up.** It is smaller, it helps every tier including the one
already shipped, it is independent of everything else in this plan, and #72 already sits first in the
triage's own suggested order. Against a competitor's "install the app", three lines is an argument
that can be had; forty-five lines of worktree bash is not.

#### Forge and CI portability

**Owner's requirement, 2026-09-20: GitHub and Azure DevOps are both first-class; other forges and CI
systems are supported where practical.** This plan and `DASHBOARD_PLAN.md` had assumed GitHub
throughout without ever stating it. The gap is recorded here; §11 asks how far "the others" reaches.

**What is already neutral.** Capture, report, verdicts, gate and query are CI-agnostic. Run identity
has a documented escape — the wiki's `Cross-Run-History` says *"on a provider Kronikol does not
detect, set `HistoryRunId` from the pipeline's own variables, `gitlab:$CI_PIPELINE_ID:1`"* — and the
ledger's location is `KRONIKOL_HISTORY` pointing at a file *"fetched from wherever it is kept"*.
**Azure DevOps is already detected**: `TF_BUILD`, the `BUILD_*` and `SYSTEM_*` metadata,
`ado:<build id>:1` run ids, and `SYSTEM_PULLREQUEST_TARGETBRANCH` behind the 3.13.0 pull-request
default. The wiki names it 37 times against GitHub Actions' 40.

**What is not.** `CiEnvironment` detects exactly two providers; every other CI returns
`CiEnvironment.None`, so branch, commit, run id and run URL must be supplied by hand.

| | Tier 0 recipe | Tier 1 serving | Tier 1 data fetch | Tier 2 |
|---|---|---|---|---|
| **GitHub** | documented | Pages | anonymous raw URLs | works |
| **Azure DevOps** | **missing** | **no Pages** | **no anonymous raw** | works |
| GitLab | missing | Pages | raw URLs | works |
| Jenkins, Bitbucket, CircleCI, TeamCity | missing, and undetected | n/a | n/a | works |

**The tier ladder is GitHub-shaped, and Tier 1 is the rung that does not port.** Azure DevOps has no
Pages equivalent and no anonymous raw file access, which is precisely what `DASHBOARD_PLAN.md`'s
delivery model rests on. **So for an Azure DevOps customer the ladder is Tier 0 then Tier 2**, the
page served from Blob static hosting or Azure Static Web Apps and its data read from the store,
skipping Tier 1 altogether.

**The inversion worth carrying: §3.4's bucket-served page is forge-neutral, so Tier 2 is MORE
portable than Tier 1.** Nobody should build Tier 1 believing it is the easy on-ramp for everyone.

**What "Azure DevOps first-class" concretely requires**, none of which is in any plan today:

1. **An Azure Pipelines recipe for Tier 0** — the `checkout` / `PublishPipelineArtifact` /
   `DownloadPipelineArtifact` equivalent of the orphan-branch fold, including the gotcha that the
   default build service identity usually lacks Contribute permission to push a branch.
2. **A pipeline template as the analogue of #72's composite action.** A composite action does not
   exist outside GitHub Actions; the ADO equivalent is a YAML template the consumer references, so
   §6.1's onboarding fix needs a sibling rather than a port.
3. **A non-Pages serving story**, most likely Azure Blob static website hosting or Azure Static Web
   Apps.
4. **Azure Blob support in the store** (§11 question 2) — and recall §6.2: Blob is the *easiest*
   provider for browser auth, so the gap and the advantage sit on different axes.

**Cheapest of "the others": GitLab.** It has Pages and anonymous raw URLs, so Tier 1 ports unchanged,
and detection is a small well-defined addition (`CI_PIPELINE_ID`, `CI_COMMIT_REF_NAME`,
`CI_COMMIT_SHA`, `CI_PROJECT_URL`, `CI_MERGE_REQUEST_TARGET_BRANCH_NAME`). Jenkins, Bitbucket,
CircleCI and TeamCity need only detection and documentation, because they reach the dashboard through
Tier 2 regardless.

**Verification level.** The Kronikol-side claims are **RUN**: which providers `CiEnvironment` detects
and what the wiki documents, read from source and wiki on 2026-09-20. The Azure DevOps and GitLab
*platform* claims — no Pages, no anonymous raw, the build-identity permission, template-not-action —
are **REFERENCE**, reasoned from working knowledge and **not checked against either vendor's
documentation in this pass**. They must be before any of the four items above is planned.

### 6.2 Hosted identity: possible, and deliberately not next

§6.1 closes the three exclusions without anyone logging in. The owner asked (2026-09-20) whether a
later plan could go further — real accounts, single sign-on, the identity surface an enterprise buyer
expects. **It could, and the argument usually cited against it does not reach.**

**What the blocking argument actually says.** `MCP_PLAN.md` §3.2: *"A hosted Kronikol MCP service would
be, by construction, a credential sink. §2.8: the capture path writes headers verbatim and redacts
nothing unless the consumer opts in.* ***Accepting uploads of other people's reports*** *makes the
project a data processor with a breach surface, a retention policy and a DPA conversation."* Its
second point — *"auth is not a weekend, and cannot be skipped for this data"* — is explicitly
conditional on the first (*"**Given (1)**"*).

That is an argument about **holding other people's data**, not about authenticating anyone. Under
§3.4 the payloads never reach Kronikol, so (1) never happens. The argument is not a prohibition to be
overturned; it simply does not apply.

**The pattern that keeps it inapplicable: OIDC federation, where the customer's own cloud mints the
credentials.** The browser takes an ID token from the customer's IdP and exchanges it through their
own STS, Entra or GCP for short-lived bucket credentials, then reads directly. A Kronikol service
would hold a user record and some configuration, and never a key that can read a payload.

> **The invariant, which belongs at the top of any such plan rather than in its conclusion: never
> hold a credential that can read customer data.** Identity, configuration and preferences are fine.
> A bucket key, a long-lived token, or anything that could fetch a payload is not. Break it and
> `MCP_PLAN.md` §3.2 reactivates in full — reached sideways rather than decided.

#### How it actually works, given there is no server

Worth spelling out, because "authenticate against the store" hides a correction: **there is no
database and no connection.** DuckDB is an in-process engine compiled to WASM inside the page, and
the store is Parquet files and blobs. The question is not how a client authenticates to a server, it
is **how an HTTP GET of an object is authorised**. Object stores have no OIDC provider of their own;
their IAM trusts the customer's.

**The simple architecture, and almost certainly the right one: put the page and the data behind the
same authenticating proxy.**

```
1. Browser hits the dashboard's URL
2. The PROXY, not the page, sees no session cookie
3. Proxy redirects to the customer's IdP; the user logs in; the proxy sets a cookie
4. The page loads
5. DuckDB-WASM fetches Parquet and blobs as ordinary same-origin requests, cookie attached
```

**No token handling in JavaScript at all.** Cloudflare Access does this in front of R2, CloudFront
with OAC in front of S3, Front Door or App Service Auth in front of Azure Blob. Note where the
redirect happens: at the edge, before the page loads, not after some connection fails.

**The federated architecture** is needed only when the page is served from one origin (GitHub Pages,
say) and must read a private bucket on another: a PKCE authorization-code flow to the customer's IdP
for an ID token, then an exchange of that ID token for **temporary cloud credentials**, then object
reads signed with them and refreshed about hourly. That exchange is uneven across providers:

| Provider | Exchanging an ID token for object access | |
|---|---|---|
| **Azure Blob, GCS** | Accept an OAuth bearer token **directly** in the `Authorization` header: no exchange, no request signing | Easiest |
| **AWS S3** | `AssumeRoleWithWebIdentity` at STS returns temporary credentials; every request is then SigV4-signed | Well-trodden, more parts |
| **Cloudflare R2** | **No OIDC federation.** Its temporary credentials are "short-lived, scoped credentials derived from an API token" (Cloudflare's docs, checked 2026-09-20), so something must hold the parent token | Dead end in-page |

**Two inversions this produces:**

- **R2, recommended in §2.4 on cost, is the worst fit for the federated route** — its parent API token
  is precisely the credential the invariant above forbids anyone from holding. R2 stays the right
  choice on cost, but **behind Cloudflare Access, never federated**.
- **Azure Blob, named in §11 as the S3-API integration gap, is the *easiest* provider for browser
  auth**, because it takes a bearer token natively. The gap and the advantage sit on different axes.

**The onboarding cost of the federated route is real:** the customer registers an OIDC provider in
their cloud IAM, creates a role scoped to the bucket, configures CORS including range headers, and
registers the page's redirect URI with their IdP. That is §6.1's residual friction arriving again,
and it is a reason to prefer the proxy architecture wherever it is available.

**Verified 2026-09-20:** R2's temporary-credential position, from Cloudflare's own documentation.
**Not verified:** browser-callable `AssumeRoleWithWebIdentity`, and Azure/GCS bearer-token acceptance
from a browser origin. Both are believed, neither checked — §10.3.

**The cost is not the engineering.** SAML and OIDC both, because enterprises have both; SCIM for
provisioning and deprovisioning; per-IdP quirks across Okta, Entra, Google and Ping; an RBAC model;
audit logging; then on-call, security questionnaires and probably SOC 2. `MCP_PLAN.md`'s second point
holds even with no data in play. **This is a decision to become a different kind of company, not a
feature.**

**Sequencing is the actual recommendation. SSO is a deal-closer, not a deal-opener.** Enterprises ask
for it once they have decided they want the product; nobody evaluates a test dashboard by starting
with the IdP integration. It sits behind the three things that decide whether there is a product at
all: **a second consumer who is not the author**, **a second language with real capture** (the
.NET-only constraint above), and **alerting** (§6.1). Building the most expensive and least reversible
piece first, for buyers not yet shown to exist, is the error this section exists to prevent.

**The risk to carry: gravity.** Once accounts exist, hosting the data gets easier to justify with
every feature — cross-organisation rollups, saved views and faster queries all want data server-side.
§5.3's "the data never leaves your account" erodes one reasonable-sounding increment at a time, and
the invariant above is the check on each of them.

### 6.3 The Microsoft-stack customer: the likely buyer, the least served

**This is not a fourth observation. It is the same one, recorded three times in three places and
never added up.** §11 question 2 noted that Azure Blob has no S3 API. §6.2 noted that Blob is
nevertheless the easiest provider for browser auth. §6.1's portability sub-section listed four things
Azure DevOps parity needs. Assembled, they say something none of them says alone.

**Who the buyer is.** Kronikol is a .NET product and its differentiator is .NET-only (§6). A .NET shop
disproportionately runs **Azure DevOps, on Azure, with Entra ID**. That is the most likely customer
for this plan, and the profile the plan is worst at.

| | On the Microsoft stack |
|---|---|
| Capture and report | **best supported anywhere** — it is a .NET product |
| CI detection | works: `TF_BUILD`, `BUILD_*`, `SYSTEM_*`, `ado:<build id>:1` |
| Verdicts, gate, query | work |
| **Tier 0 recipe** | **missing** — GitHub Actions only |
| **Onboarding artifact** | **missing** — #72's composite action has no ADO form |
| **Tier 1 dashboard** | **impossible as designed** — no Pages, no anonymous raw |
| **Store** | **missing** — Azure Blob has no S3 API |
| Browser auth | **easiest of any provider** — Blob takes an Entra bearer token natively (§6.2) |
| The §6.2 auth proxy | ~~free — App Service Auth and Static Web Apps ship it~~ **Does not exist without compute** (corrected 2026-09-21, §13 R19): Static Web Apps guards only what it serves and holds 2 GB at most, Blob static websites are anonymous-only, Easy Auth guards only its own app. The Azure route is the bearer token in the row above |
| Data residency | free; the customer picks the region |

**The reframe, and why this is a finding rather than a complaint.** The last three rows say the
Microsoft stack is not a second-class port to be added grudgingly. **It is the shortest path to a
complete, differentiated offering**, because on Azure one customer gets capture (the best-supported
platform), store (Blob), auth (Entra, with a proxy that ships with the hosting) and serving (Static
Web Apps) inside a cloud they already have and already pay for. **The identity story §6.2 treats as a
distant follow-up is nearly free there.**

What is missing is plumbing, and it is well defined. This table **supersedes** §6.1's four items and
§11 question 2, which were the same work counted twice:

| # | Work | Supersedes |
|---|---|---|
| 1 | Azure Pipelines recipe for Tier 0, including the build-identity Contribute gotcha | §6.1 item 1 |
| 2 | An ADO pipeline template as the sibling of #72's composite action | §6.1 item 2 |
| 3 | Azure Blob in the store, as its own code path rather than the S3 one | §6.1 item 4, §11 Q2 |
| 4 | Serving from Blob static website or Static Web Apps, with App Service Auth as §6.2's proxy | §6.1 item 3 |

**Sequencing.** None of this blocks the store; all of it blocks an Azure DevOps customer adopting any
of it. Items 1 and 2 are small and help the tier that already ships. **Items 3 and 4 belong inside M2
rather than after it**, because retrofitting a second storage code path once the Parquet layout and
retention rules are settled is more work than carrying it from the start.

**It also reorders §6.2.** That section puts hosted identity behind "a second language with real
capture". If the ideal customer is .NET-on-Azure, **Azure plumbing outranks a second language**: it
serves the buyer who already exists rather than one who might. §6.2's sequencing should be read with
that correction.

**Unverified, and this is the first task.** Every Azure *platform* claim here is REFERENCE (§6.1):
reasoned from working knowledge, not read from Microsoft's documentation. Four of the rows above are
load-bearing. **Half a day confirming them comes before any of the four items is planned**, because
this section would rearrange a roadmap on the strength of them.

---

## 7. What the triage work gives this

Against `OPEN_ISSUES_TRIAGE_2026-09-18.md`. **Nothing in the triage conflicts with this plan**, and
most of it is load-bearing.

| Triage item | What this plan gets | Strength |
|---|---|---|
| #91 / `HISTORY_ANALYZER_COST_PLAN.md` | A linear analyzer | **prerequisite** (§5.1) |
| Plan 1 — #75, #83 | Verdicts worth trending; the issue claims ~95% noise removal | **prerequisite** (§7.2) |
| #81 | `query history` without a report, via `--sid` / `--run` — the seam a store plugs into | strong |
| #80 | `Reports/runs/<runId>/…`; its `--run <id>` is this plan's primary key | strong |
| #86 | Content-addressed storage keyed by flow, 91% redundancy measured | strong |
| #85, #89 | Ingest per run from 82.7 MB to 6.1 | strong (§5.2) |
| #77, #78 | The scenario↔source join key behind "which commit changed this" | moderate |
| #90 | A run-level metric worth trending | moderate |

### 7.1 Why #91 is a prerequisite and not an optimisation

Ingest-time verdicts (§5.1) do not remove the analyzer's cost, they relocate it. Quadratic behaviour
at 5,000 scenarios is as unacceptable in an ingest worker as in a CLI. S1 is what makes the relocation
worth doing.

### 7.2 Why the noise plan is a prerequisite and not a polish item

A trend dashboard built on today's verdict noise would be **worse than no dashboard**: it would show
a wall of behaviour-changed pills that readers learn to ignore, which is how a signal dies
permanently. The `history-noise-audit` replay found 77% of count verdicts were one-offs before 3.20.0
confirmed them; the remaining sources are what #75 and #83 address. Ship the noise fixes, then build
something that displays verdicts prominently — not the other way round.

### 7.3 One ordering correction

The triage's suggested order puts #72 and the #81/#82/#84 patch first and plan 1 third. For this
plan's purposes #91 should go **first** — its own plan argues the same, on the independent ground
that two other plans rewrite the loop it fixes.

---

## 8. Slices

### 8.0 The order of work, decided 2026-09-21

**Owner's decision on §11 question 19 ("go ahead", 2026-09-21): three products, in this order.** It
decides the *order*; it green-lights nothing, and this plan and `DASHBOARD_PLAN.md` both stay NOT
green-lit for implementation. §13 R16 is the argument. M0 to M5 below are unchanged as text and are now
the slices of the **third** product.

| | Product | What it needs | What it does not need |
|---|---|---|---|
| **P1** | **Years of trends, at verdict level** | `DASHBOARD_PLAN.md`'s page and view, with its §6 "no retention beyond the window" lifted: the view written as **monthly files on the existing history branch**, where `merge=union` already works; the page inlines the recent window and fetches older months whole (no range requests, no preflight, §13 R18). On Azure DevOps, which has no raw file access, the workflow copies the same files beside the page | a bucket, a credential, the projection, Parquet, a compactor, a manifest |
| **P2** | **Any past run's evidence** | The report CI already emits, uploaded to the team's own bucket **under a key that carries the branch class and the run id** (lifecycle rules select by key prefix and keys are immutable, §13 R14), and its address on the run line so the page and `query history` can link it. 981,768 B gzipped for the HTML on the measured lane: about 8 GB steady at 90 days, 32 GB per year kept. Gzip at rest, which every browser reads (§13 R20) | the projection, Parquet, a compactor, a manifest, DuckDB |
| **P3** | **The facts layer**: call-level analytics, and re-fingerprinting that outlives the stored reports | M0 to M3 and M5 below, with §13's corrections applied first: verdicts as a stamped cache (R1), ids stored verbatim (R3), immutable per-run bundles as the record and Parquet as a rebuildable index (R6), a single manifest writer (R7), and a page that fetches shaped files instead of querying (R9) | to start before somebody asks for it |

**What this changes.**

- **M0 stops being the gate for time travel.** P2 keeps the artifact itself, so "any point in time" no
  longer waits on a lossless projection. The projection stays worth building early for its own reasons
  (#93, #94, the further losses in §13 R4, and `PLATFORM_FOUNDATIONS_PLAN.md` F2), and it becomes the
  gate for P3 only.
- **F5's fix does not need P3 to start.** Re-fingerprinting reads the stable facts, and a stored
  `TestRunReport.json` holds them: `measure.py` derives the re-templatable core from exactly that file.
  P2 therefore covers a rule bump for as long as the reports are kept. P3's forever-layer is what
  covers it after they expire, which makes payload expiry a decision about custody and not about cost
  (§11 question 20, still open).
- **P1 and P2 each ship something a reader can see**, are small, and put §6.1's own test to the store:
  one dogfood consumer is the whole evidence base, so build the cheap thing and see who asks for more.
- **Prerequisites are unchanged and one is done.** #91's S1 and the noise plan (executed 2026-09-21 as
  3.22.2 to 3.25.0) still come before anything that displays verdicts. **#95, the as-of defect §13 R2
  found, shipped as 3.25.1**, and P2 depends on it: a linked past run is read again later, which is
  exactly the read that was wrong.

**Still open and now first in line:** where P1's slice lives. It is an amendment to
`DASHBOARD_PLAN.md` (its §4 view, its §6 retention line, its M-slices) rather than a slice of this
plan, and that plan's owner questions (§9 there) have not all been answered. Recommendation: write P1
as a short addendum to `DASHBOARD_PLAN.md` and leave this plan holding P2 and P3.

### The third product's slices (M0 to M5)

None is breaking; none needs v4; each is a minor bump unless noted.

### M0 — the storage unit: records in, report out

**Build the projection, close the three gaps, then prove it.** §3.1 found that the outbound half
does not exist: replay works, but nothing today turns a finished run back into records without losing
the user's own PlantUML, the measured durations and every step marker.

Three pieces, in order:

1. **Close the two writer defects, #93 and #94**: a `plantUml` field on
   `InteractionRecord` that `FromLog` populates and `ToLog` restores, and `DurationMs` carried
   through. Both are small and both are worth doing whether or not this plan proceeds. *(Done:
   #94 in 3.27.4; #93 in 3.29.0 as a `kind: "marker"` record per override half with `markerKind`,
   `plantUml` and `markerEnd`, not a `plantUml` field on the existing kinds, which would have left
   `Step`, `Assertion` and `Phase` markers as the junk request line every marker became;
   `INGEST_FEED_PLAN.md` F2 and §4.2. Measured on the LightBDD xUnit3 example: 6 of 6 diagrams
   byte-identical after projection and ingest.)*
2. **Teach the writer the markers.** `FromLog` must emit `Text`, `Keyword`, `Table`, `DocString`,
   `Passed` and `Message` for a log carrying a `Step` or `Assertion` `DiagramMarkerKind`, so a step
   bar survives the trip as structure rather than as pre-rendered PlantUML.
3. **The projection itself**: a report, or better the live logs at `[AfterTestRun]`, written out as
   `interactions.ndjson` + `tests.ndjson` + `kronikol.render.json`.

**Done when** a real 203-scenario run projects to records, replays through `kronikol ingest`, and the
result matches the directly-generated report **on the facts**: every scenario, step, call, payload,
status, duration, error and attachment identical, with the diff explained field by field wherever the
bytes differ. **Byte-identity is explicitly not the bar** (§8.4).

**This is the gate for the whole plan.** If a fact cannot survive the trip, §2's figures describe a
store that cannot answer "any point in time", and the plan should stop here rather than shrink
quietly.

### M1 — verdicts at ingest

`HistoryAnalyzer` runs when a run arrives; verdicts are stored as rows. Needs #91's S1 and the noise
plan. **Done when** the stored verdict for every run in the consumer's 412-run ledger is identical to
what the CLI analyzer produces for the same run, byte for byte, and a conformance test holds the two
paths together.

### M2 — the store

Two stores, per §3.4: Parquet on object storage for the stable layer and the verdicts, blob objects
keyed by run id for the payloads, both in the customer's own account. Per-column retention; join keys
renumbered on write (§3.2, **the two safe ones only**); deletion designed in (§5.4).

**Compaction is part of this slice, not a later one** (§3.5): per-run objects rolled up daily and
compacted monthly, because the one-file-per-run layout is three orders of magnitude too slow and
retrofitting compaction means rewriting everything already stored.

**Done when** the consumer's full history is stored and queried; a payload-expiry policy runs and the
stable layer survives it; a per-repository delete completes and is verified; **a full-history trend
query is timed against the compacted layout and meets whatever bar §11's spike sets**; and the
range-request question has an answer.

The S3 API is the portability surface, not a vendor: R2, B2, GCS and S3 all speak it. **Azure Blob
does not**, which §11 raises, and which matters more here than it would for most products.

### M3 — derived fingerprints and the backfill

Fingerprints computed from the stable layer rather than stored; a rule bump triggers a backfill.
**Done when** the consumer's history is re-fingerprinted from v3 to a synthetic v4 and every verdict
that should be unchanged is unchanged — the control being a deliberate rule change whose effect is
known in advance.

### M4 — the dashboard reads the store

`DASHBOARD_PLAN.md`'s page, reading this store. **Its page-level decisions all hold** — Observable
Plot on vendored D3, no verdict computed in JavaScript, ledger text rendered as `textContent`, hash
CSP, no CDN.

**What does not simply carry over is how the data reaches the page.** That plan inlines it, which
works at 116-420 KB and not at 79 MB, so M4 cannot start until the fork in §3.4 is decided
(recommendation: inline a bounded build-time summary, query the store for drill-down). **Done when**
the page renders the consumer's full history, the default view needs no range requests, and the
drill-down path has an E2E test running against a real local HTTP server rather than `file://`.

### 8.4 Two promises, and only one of them is offered

Worth separating, because they are easy to conflate and only the first is on offer:

- **Reproduce the evidence** — every call, payload, step, result and error of any past run, rendered
  into a report. This is the plan's promise and what M0 gates.
- **Reproduce the artifact byte for byte** — the same HTML and JSON bytes that run emitted. **Not
  offered.** Diagrams are re-rendered, so output tracks the PlantUML engine version, and the report
  template moves every release. Pinning either would freeze the renderer at the version that ran.

The second is also probably not wanted: a two-year-old run re-rendered by today's renderer gains
every report feature added since. §11 asks whether that is the default anyone would choose.

### 8.5 One sentence for `CROSS_RUN_HISTORY_PLAN.md` §14

Applied when this plan is green-lit and not before, amending the second bullet:

> **No unbounded retention *in the ledger*.** The window is finite; a team wanting two years wants a
> data warehouse and should export. A store that Kronikol writes as files, that lives in the team's
> own account, and that the team's own tooling moves and serves (`HISTORY_DASHBOARD_STORE_PLAN.md`) is
> that warehouse and not a hosted dashboard in the sense of the bullet above: Kronikol still opens no
> socket, holds no token and needs no account.

### 8.6 Operations, versioning and docs: what this plan had not covered

**Added 2026-09-20, when the owner asked what was missing.** Twelve probes found twelve absences.
F10 (§3.4), the migration consequence (§4.3) and subject-level erasure (§5.4) are recorded in their
own homes. The rest are collected here, because each is real work and none had a slice.

**M5 — migration from an existing ledger.** Every current user has a `kronikol-history` branch and
§8 had no path from one into the store. It must carry results, durations, verdicts and history, and
it must say plainly what it cannot carry (§4.3: the re-templatable core and the payloads were never
written, so migrated history is permanently stuck at its rule version). **Done when** the consumer's
412-run ledger is in the store, every verdict matches what the ledger's own analyzer produces, and
the documentation states the rule-version limit rather than leaving it to be discovered.

**The store is the system of record, which is a step up in criticality nobody named.** A ledger on a
git branch is replicated to every clone and to the forge; a bucket is one copy, and the runs behind it
no longer exist. Losing it loses the history irrecoverably. The plan needs a stated position on object
versioning, cross-region replication, or an explicit "this is the customer's backup responsibility,
here is what to turn on". **Unstated is the wrong answer whichever way it goes.**

**The store needs its own format version.** `HistoryFormat.Version` and `InteractionShape.Version`
both exist because this was learned once already. New columns are certain (#90's coverage, new capture
fields), and there is no policy for reading old files under a new schema. Parquet tolerates added
columns; that is not the same as having decided what happens.

**Observability of the pipeline.** The ledger was *visible*: a branch anyone could open. A bucket is
opaque, and ingest failing silently for a week is a plausible failure with no detector. Related and
equally unstated: **what the gate does when the store is unreachable.** Failing a build on a storage
outage would be unacceptable; the answer is presumably to degrade to advisory and say so, but
presumably is not a decision.

**Multi-platform writes.** §6 records that *capture* is .NET-only. It says nothing about the store
*writer*. If `PLATFORM_FOUNDATIONS_PLAN.md` lands, do Java and Node capture paths write to the store
directly, or does ingest stay .NET tooling they shell out to? That is a boundary question its F2
contract should answer, and neither plan does.

**Versioning, per `CLAUDE.md`.** This plan carries no semver statement, which the house style
requires and `HISTORY_ANALYZER_COST_PLAN.md` demonstrates with a per-slice Bump column. The reading:
every slice here is **MINOR** — new verbs, new options, new public surface, nothing removed and no
default changed. **M0's two writer fixes (#93, #94) are PATCH**, being defects in shipped behaviour.
*(Roadmap D4 ruled otherwise on #93: it adds public members to `InteractionRecord` and a new `kind`
value to a documented contract, so it shipped as the minor 3.29.0; #94 shipped as the patch 3.27.4.
`INGEST_FEED_PLAN.md` §7.)* Nothing here is MAJOR, and nothing may become MAJOR without asking first.

**Documentation, per `CLAUDE.md`.** The wiki is the primary target and this plan names no page. At
minimum: a new store page, edits to `Cross-Run-History` where the ledger stops being the only home
for history, and the migration limits from §4.3. The README's history section and the agent skill's
`commands.md` copies move with any new verb, and `SkillDriftTests` holds those two copies identical.

**Testing, per `CLAUDE.md`'s TDD rule — and this is the largest of the omissions here.** The plan has
acceptance criteria per slice and no statement of how any of it is tested. A store means a fake or a
container: Azurite for Blob, MinIO or LocalStack for S3-compatible, or a filesystem-backed
implementation behind the same interface. **The choice matters because it decides whether store tests
can run in the normal unit suite or need containers**, and `ci-flake-classes` records what container
tests already cost this repository's port pool. It should be decided in M0, with the rest of the
storage design, not when the first test is written.

---

## 9. Risks

- **M0 fails and the round trip is lossy.** The whole plan rests on runs being replayable. **Three
  losses are already known** (§3.1) and M0 closes them; the risk is a fourth found later. Mitigated by
  making M0 the gate rather than a milestone, and by testing the trip on facts rather than bytes.
- **M0 is larger than first scoped.** The first draft budgeted "prove the round trip"; it is now
  "build the projection, fix two defects, teach the writer markers, then prove it". Still small next
  to M2, but no longer free.
- **Query latency is entirely unmeasured.** §3.5 is mechanism and arithmetic. If a real dashboard
  query against a real layout costs seconds rather than the predicted 150-400 ms, the answer is a
  server and the bring-your-own-bucket position goes with it. §11's spike exists to find this out
  cheaply and early. **This is the plan's largest unverified claim**, and it is the same error class
  as the two already corrected: reporting an adjacent measurement as the claim.
- **Compaction is skipped or deferred.** The one-file-per-run layout is three orders of magnitude too
  slow, and retrofitting compaction rewrites everything stored before the decision. M2 carries it as
  acceptance for that reason.
- **The §2.3 heavy-suite estimate is low.** It is the one unmeasured number. Even 5x wrong it is
  single-digit dollars a month, so it threatens sizing and not feasibility.
- **Two analyzers drift.** M1 creates a second caller; conformance tests are the answer and the
  obligation is permanent.
- **Renumbering breaks an external trace join.** §3.2; the escape is to exempt `activityTraceId` at a
  cost of 8,691 bytes a run.
- **Per-column retention is decided too late.** Choosing a format that cannot drop a field cheaply
  forces a rewrite of everything stored before the decision. This is why M2 names retention in its
  acceptance rather than deferring it.
- **The dashboard ships before the noise is fixed** and teaches its readers to ignore it. §7.2.
- **Concurrency is designed late or not at all (F10).** The ledger's `merge=union` and rebase retry
  have no equivalent here, and §3.5's compactor runs against live writers and readers. Retrofitting
  atomicity onto a format already holding years of data is close to a rewrite, which is why §3.4
  makes it **M2's first task, ahead of the layout**.
- **The bucket is lost and the history is gone.** The store is the system of record and the runs
  behind it no longer exist (§8.6). A git branch was implicitly replicated; a bucket is not.
- **Migration is assumed to be complete and is not.** §4.3: ledger-sourced history can never be
  re-fingerprinted. If that is not documented, the first rule-version bump after a migration will
  look like a bug.
- **Scope gravity.** A store invites accounts, alerting and hosting — the three things §5.3 and
  `DASHBOARD_PLAN.md` §3 both decline. The bring-your-own-bucket constraint is what holds the line.

---

## 10. Assumption ledger

### 10.1 Verified by running

Everything in §1's RUN rows: report sizes and composition, every compression model in §2.1
**including the lossless/lossy separation added 2026-09-20**, the eight-run re-run simulation, the
columnar breakdown and the id renumbering, the check that no `requestResponseId` appears in any
payload, the re-templatable core, the cross-lane deltas, and every ledger figure. Reproduced by the
harness in one pass, §1.1.

### 10.2 Verified by reading

- `InteractionShape.Target` / `.Calls` read exactly the eight fields of §4.2 — the basis of the whole
  versioning fix.
- `HistoryAnalyzer.cs:349-354` filters prior points by rule and emits the "earlier rule" evidence.
- `IngestCommand` replays NDJSON into a report. **`InteractionRecord` does NOT round-trip
  `RequestResponseLog`**: this bullet still claimed it did until 2026-09-21, a day after F7 retracted
  the claim everywhere else. §13 R4 lists what `FromLog` drops, which is more than F7's three.
- `CROSS_RUN_HISTORY_PLAN.md` §14, `DASHBOARD_PLAN.md` §3 and §6, `MCP_PLAN.md` §10,
  `PLATFORM_FOUNDATIONS_PLAN.md` F2 and §4.4 — quoted, not paraphrased, where they constrain this plan.
- Issues #85, #86, #89, #90 — their own measurements, not re-measured.

### 10.3 Not verified, and what this plan does about each

| Not verified | What is done about it |
|---|---|
| The heavy suite's real full-fidelity cost | Labelled ESTIMATE in §2.3 and in §1; re-measure before sizing |
| That a run projects to records without loss | **Partly answered 2026-09-20, and negatively**: three losses found and named in §3.1. M0 closes them and remains the gate. Byte-identity is no longer claimed at all (§8.4) |
| Whether anything beyond those three is lost | Not known. M0's acceptance is a field-by-field diff precisely so a fourth loss surfaces there rather than in production |
| Whether `DiagramMarkerKind.Custom` PlantUML can be re-derived at all | It cannot, by construction: it is opaque text a consumer supplied. The record must carry it verbatim, which is what #93 asks for. `Row` is in the same position; `Phase` may be recoverable from the `phase` field the surrounding records carry, and #93 records that as unchecked |
| Cloud list prices | **Checked 2026-09-20** against vendor pages and 2026 pricing guides, §2.4. Still REFERENCE, because prices move and two of the four came from guides rather than the vendor's own table |
| Whether `raw.githubusercontent.com` serves HTTP range requests | **Not measured.** DuckDB-WASM needs it for the backend-free page. §11 question 1 |
| **Any query latency at all** | **Nothing has been timed.** §3.5's 150-400 ms, its file-count table and its 1-5% read fraction are all mechanism and arithmetic. §11 question 1 is now a four-part spike covering this, and §9 carries it as the largest risk |
| What compaction costs at ingest | **Not measured**, and it has to fit inside a CI job's window. §11 question 1 |
| Whether DuckDB-WASM prunes as well as Parquet's statistics allow | **Not measured**; real readers are often less selective than the format permits |
| Whether `api.github.com` permits browser-origin authenticated calls under CORS | **Not verified.** §6.1's option B depends on it. A small spike, needed only if B is ever planned |
| How well a forge's prefill handles a multi-line JSON edit rather than a new file | **Not verified.** §6.1's option C depends on it, and C is the recommended near-term route |
| That `AssumeRoleWithWebIdentity` is callable from a browser origin | **Not verified**, believed. §6.2's federated route on AWS depends on it |
| That Azure Blob and GCS accept an OAuth bearer token from a browser origin | **Azure verified 2026-09-21** against Microsoft's REST and JS SDK documentation (§13 R19: Entra token, `x-ms-version`, Storage Blob Data Reader, and a CORS rule allowing `authorization`, `x-ms-version` and `range`). GCS still not verified |
| That R2 has no OIDC federation | **Verified 2026-09-20** against Cloudflare's own documentation: temporary credentials are derived from an API token, so something must hold the parent |
| Lifecycle expiry, storage-class minimums, CORS range headers | **Checked 2026-09-20** (§2.4): R2 and B2 lifecycle docs, S3 Standard-IA minimums, Azure tier minimums from a 2026 pricing guide rather than Azure's own table |
| That object stores support the conditional write a manifest needs | **Checked 2026-09-21, and one does not** (§13 R7): S3, R2, Azure and MinIO do; GCS does under its own header, `x-goog-if-generation-match`; **Backblaze B2 does not at all** (501). A design that needs the conditional write excludes B2; one with a single manifest writer does not |
| Whether Parquet schema evolution covers what this needs | **Not checked.** §8.6: added columns are tolerated, which is not the same as a decided policy |
| How the store is tested | **Not decided.** §8.6: Azurite, MinIO, LocalStack or a filesystem fake, and the choice decides whether store tests need containers |
| Durability, max object size, consistency, request rate limits | **Not checked, deliberately.** None plausibly differentiates at this scale, and §2.4 says so rather than leaving the omission silent |
| Whether Azure Blob needs its own code path | **Yes, as far as can be read** (2026-09-21): no S3 API appears anywhere in Microsoft's documentation and a Microsoft Q&A answer says it is unsupported, which is a secondary source |
| Whether all 18 lanes cost what the one measured lane costs | **No, and the spread is known to be large**: issue #86 measured two lanes running the same 263 scenarios at 12.6 MB and 31.8 MB, 2.5x apart. §2.4's annual figures apply one lane's cost to all 18 and are optimistic by an unmeasured factor |
| The competitor survey | Cited to `DASHBOARD_PLAN.md` §3 with its date; not restated as fresh |
| Whether anyone joins `activityTraceId` to an external backend | §11 asks; the exempt-and-pay escape costs 8,691 B/run |
| Whether a real re-run of the *same* lane behaves like the §2.2 simulation | The simulation refreshes ids and shifts clocks, which is what a re-run does, but it was not compared against two real consecutive runs of one lane. §11 |
| Query latency at five years | M2's acceptance, not a benchmark |

### 10.4 Open investigations

- Two consecutive real runs of one lane, stored and measured, against §2.2's simulation. The cheapest
  way to get them is two artifacts from the consumer's CI rather than a local re-run.
- Whether `stmtHead` at 14,172 B is the right cut. It is 64% of the re-templatable core and exists
  only for statement-shaped dependencies; `HeadRaw` is 2000 and `HeadLength` 120, so most of what is
  stored is discarded by the templater. Storing the already-cut 120 would be far smaller but would
  re-create exactly the one-way problem §4 exists to solve. Worth measuring, not worth guessing.

---

## 11. Open questions for the owner

1. ~~**Format: Parquet on object storage, or ClickHouse?**~~ **Answered 2026-09-20 (§3.4): two
   stores, blob for payloads and Parquet-on-object-storage for the stable layer, with ClickHouse as
   the documented upgrade path.** What remains open is a spike, and it is now wider than the
   original question, because §3.5 is arithmetic rather than measurement. **Half a day against a
   generated Parquet set at five-year scale, answering four things:**
   1. **What does a real dashboard query cost** against the compacted layout, cold and warm, from a
      browser with CORS in the path? §3.5 predicts 150-400 ms and has timed nothing.
   2. **Does the chosen host honour HTTP range requests?** DuckDB-WASM needs them;
      `DASHBOARD_PLAN.md` measured `raw.githubusercontent.com`'s CORS header but not this.
   3. **Does DuckDB-WASM's actual read pattern match the theory?** Real readers are often less
      selective than statistics-based pruning suggests.
   4. **What does compaction cost at ingest**, and does it fit in the window a CI job has?

   **This is the one remaining thing that could send the design back to a server**, so it runs before
   green-light rather than during M2.
2. ~~**Does Kronikol need an Azure Blob path?**~~ **Folded into §6.3 on 2026-09-20**, which assembles
   it with the two other places the same pattern had been recorded. The question is no longer whether
   Azure Blob needs a path but **whether §6.3's reframe is accepted**: that the Microsoft stack is the
   shortest route to a complete offering rather than a port, that its items 3 and 4 belong inside M2,
   and that Azure plumbing outranks a second language in §6.2's ordering. **The half-day of vendor
   verification §6.3 names as its first task was done on 2026-09-21 (§13 R19)**: no native S3 API,
   the bearer-token route works from a browser, and the "free proxy" row was wrong.
3. **Time travel: re-render with the version that ran, or today's?** The report carries
   `kronikolVersion`, so both are possible. Today's renderer means old runs gain new report features;
   the original means fidelity to what was seen.
4. **Does anything join `activityTraceId` or `activitySpanId` to an external tracing backend?** If
   not, renumber those two as well and take a further 16,364 bytes a run. If yes, they stay verbatim.
   `requestResponseId` and `traceId` are settled: verified renumberable (§3.2).
5. **Default retention for the payload layer.** 90 days is the assumption behind §3.3's and §2.4's
   arithmetic; it is an assumption, not a recommendation. It is also the single biggest lever on the
   five-year figure: 22.2 GB untiered against 4.5 GB tiered.
6. **Does this supersede M3 of `DASHBOARD_PLAN.md`** (per-scenario dependencies, which needs a ledger
   v2)? A store makes ledger v2 unnecessary for that panel. If so, that plan's M3 should be marked
   superseded rather than left to be built twice.
7. **Ordering of the two §6.1 follow-ups.** The recommendation is **the composite action first**
   (#72's shape, `templates/github-actions/kronikol-history`), because it cuts Tier 0's install from
   about 45 lines of worktree bash to three, helps the tier that is already shipped, and is
   independent of everything else here. **Alerting second**, because a dashboard whose verdicts
   nobody is told about is the failure mode §7.2 warns of in another form. The question for the owner
   is whether either should be scheduled alongside this plan rather than after it.
8. **Hosted identity (§6.2): record it as a future option, or rule it out?** The recommendation is
   to record it with its invariant and its sequencing attached, and **not to plan it until a real
   buyer asks**. The question for the owner is whether that is the standing answer, and whether the
   invariant ("never hold a credential that can read customer data") should be promoted into
   `CROSS_RUN_HISTORY_PLAN.md` §14 alongside the socket rule, where it would bind every future plan
   rather than only this one.
9. **Inline or query: how does the data reach the page (§3.4)?** `DASHBOARD_PLAN.md` §4.3 inlines it;
   §3.5's 79 MB verdict layer cannot be inlined. **Recommendation: (c), a bounded build-time summary
   inlined, with the store queried for drill-down and deep history** — it keeps the page instant and
   offline-capable, keeps the `file://` E2E path for the default view, and confines the
   range-request dependency to drill-down. This is tied to question 1's spike and should be decided
   with it. **M4 cannot start until it is.**
10. **How far does "and ideally the others" reach (§6.1)?** GitHub and Azure DevOps are decided as
    first-class (owner, 2026-09-20). GitLab is the cheapest addition and the only other forge where
    Tier 1 ports unchanged. Jenkins, Bitbucket, CircleCI and TeamCity need detection plus docs only,
    because they reach the dashboard through Tier 2 regardless. The question is whether GitLab is in
    scope now or later, and whether the rest are supported or merely not obstructed.
11. **Atomicity: a manifest with an atomic pointer swap, or a full table format (§3.4)?**
    Recommendation is the manifest — writers only add immutable files, one conditional write
    publishes a new version, readers resolve it first — because Iceberg or Delta is a large
    dependency for a store this size. **This is M2's first task and it cannot be deferred**, since a
    format already holding years of data cannot have atomicity added cheaply.
12. **Backup: Kronikol's problem or the customer's (§8.6)?** Bring-your-own-bucket argues the
    customer's, but the store is the system of record and losing it is irrecoverable, so silence is
    the wrong answer either way.
13. **Is bring-your-own-bucket the final answer on hosting**, or is it the answer until someone asks
   for a hosted version? §5.3 treats it as final, consistent with `MCP_PLAN.md` §10.

**Added 2026-09-21 by the critical review (§13). Each carries the review's recommendation; none is
decided.**

14. **Is a stored verdict a fact or a cache (R1)?** §5.1 treats it as a fact. The code says it is a
    function of eleven options, the baseline stream, two editable companion files and append order,
    under rules that carry no version. **Recommendation: a cache**, stamped with a rules version (to
    be invented), the options, the baseline and the as-of position, and recomputed by M3's backfill.
    The alternative is a trend chart with a step at every analyzer release.
15. **Drop the id renumbering (R3)?** **Recommendation: yes.** Kronikol's own OTLP export publishes
    both "safe" keys, sequential integers collide across runs after `ToGuid`, and the saving is two
    cents a month. 181,953 becomes the engineered figure.
16. **What is the system of record (R6)?** **Recommendation: immutable per-run bundles** (`facts/`
    kept, `payloads/` expiring), with every Parquet file a rebuildable index over them. It turns a
    compactor bug, a schema change and most of F10 from data loss into a rebuild.
17. **Who talks to the bucket (R8)?** The workflow, in per-provider shell that Kronikol cannot test,
    or an opt-in `Kronikol.Extensions.Store.*` package on the precedent of `OtlpExporter`. This
    decides where the commit protocol lives and whether store tests need containers at all, so it
    comes before the manifest.
18. **Does the page query, or fetch files the compactor shaped for it (R9)?** **Recommendation:
    fetch.** DuckDB is 110 MB in the tool and a SQL engine in a page whose rules were written to keep
    one out; as the customer's own tool against an open format it is a feature.
19. ~~**Which product first (R16)?**~~ **Answered 2026-09-21: the owner said go ahead with the
    recommendation.** Years of verdict-level trends on the existing branch (P1), then durable reports
    by run id (P2), then the facts layer only if someone asks for call-level analytics (P3). §8.0
    records it and what it changes. What it opens: where P1's slice lives (an addendum to
    `DASHBOARD_PLAN.md` is recommended), and P2's key layout, which has to carry the branch class
    from the first write.
20. **What survives day 91, and why does anything expire (R12, R13, and question 5)?** It cannot be
    cost: keeping everything for ever is cents. If the reason is custody, then the statement head,
    the attachments and the URIs in the keep-for-ever layer need the same treatment as the bodies,
    and the owner's "any point in time" is knowingly traded for it. If there is no custody reason,
    the default should be to keep everything.

---

## 12. Execution log

Nothing implemented. Written 2026-09-19 against 3.22.1 (`82abeb7f`); the repository's working tree was
not modified beyond this file, its harness and the `PLANS_STATUS.md` row.

**2026-09-20, verification round.** The owner asked whether the approach really stores everything and
reproduces any point in time. Checking rather than answering found F7: the replay half ships, the
projection half does not exist, and `InteractionRecord.FromLog` loses three things. §0 (F7 and the M0
row), §1 (five ledger rows), §3.1, §8's M0, the new §8.4, §9 and §10.3 were corrected. Two live
defects were filed against `lemonlion/Kronikol` as #93 (no `plantUml` field on `InteractionRecord`;
`Custom`, `Row` and `Phase` markers lost) and #94 (`DurationMs` dropped by `FromLog`).

**2026-09-20, second verification round.** The owner asked how storing per-run random GUIDs does not
become enormous. It does not compress, and checking why found the **second overclaim: the 95,598
engineered figure measured a lossy transform** (§2.1, F3). Corrected to **138,341**, with the rule for
which ids may be renumbered and the measurement that settles it. The same round added §3.4 (two
layers, two stores, with document and relational databases rejected in writing), replaced §2.4's
REFERENCE prices with vendor figures checked that day, and resolved §11's first question while
opening two more: the range-request spike and the Azure Blob path. **The conclusion did not move**:
4.6 GB a year, $0 on R2's free tier, and the number that matters is 681 bytes per scenario-execution.

**2026-09-20, third round.** The owner asked how anything on blob storage can be performant. Added
§3.5, which answers it structurally — the volumes are trivial, the cost is round trips, and the file
count is a layout decision, so **compaction is mandatory and the naive one-file-per-run layout is
three orders of magnitude too slow**. M2 now carries compaction as acceptance. §11's first question
widened from one range-request check to a **four-part half-day spike including actual query timing**,
because §3.5 is arithmetic and nothing has been timed; §9 and §10.3 record that as the plan's largest
unverified claim and name it as the same error class as the two already corrected.

**2026-09-20, fourth round.** The owner asked whether a follow-up could close the three exclusions
`DASHBOARD_PLAN.md` §3 gives up on. Added §6.1: all three descend from the one socket/credential
constraint, and §14 supplies the escape, so each has a permitted form — alerting by emitting events
the workflow delivers (half-built already, the #72 pattern), workflow by prefilled pull requests
against the committed quarantine file, accounts by inheriting the customer's rather than building
any. **Onboarding friction is named as the residual**, because it is the one thing no follow-up here
closes. §11 gains a question on scheduling the alerting work. A later pass added §6.1's
sub-section on the mute friction, after the owner asked whether the pull-request route had an
alternative: three (write to the store, a user-held browser token, or simply a cheap prefilled pull
request), recommended C now / A with the store / B only on request, with the prior question of
whether muting should be instant at all raised above them. Two small unverified items went to §10.3.

**2026-09-20, fifth round.** The owner asked whether a later plan could add accounts and single
sign-on. Added §6.2: the credential-sink argument in `MCP_PLAN.md` §3.2 is about **accepting other
people's data**, and its auth argument is explicitly conditional on that, so under §3.4 it does not
reach identity at all. OIDC federation lets the customer's own cloud mint the bucket credentials, and
the invariant that keeps it honest — **never hold a credential that can read customer data** — is
written as the first line of any such plan rather than its conclusion. Recommendation is to record it
and not plan it until a buyer asks, because SSO is a deal-closer and sits behind a second consumer, a
second language and alerting. **The same round found that §6 had never recorded the .NET-only capture
constraint**, which the sequencing argument depends on; it is now stated with the Kronikol4J ledger's
own words. **No figure in §2 changed** — the storage
arithmetic never depended on the projection existing, only on the facts being capturable, and they
are.


**2026-09-20, sixth round.** The owner asked what the dashboard's technology architecture actually is.
Assembling it surfaced a fork nothing had resolved: `DASHBOARD_PLAN.md` §4.3 **inlines** the data,
which works at 116-420 KB and not at §3.5's 79 MB verdict layer, and M4's wording ("the page, pointed
at a store instead of a ledger view") glossed over it. §3.4 gains the end-to-end stack diagram and the
fork with three routes; M4 is corrected and now blocked on the decision; §11 question 9 records it with
(c) recommended. The `file://` E2E constraint is named as part of why inlining was chosen, so it is
not re-discovered later.

**2026-09-20, seventh round.** The owner asked how authentication works when there is no server.
Answering it required correcting the framing — **there is no database and no connection**, only
authorised object GETs — and produced §6.2's sub-section: the **proxy** architecture (page and data
behind one authenticating edge, no token in JavaScript, and almost certainly the right default) set
against the **federated** one (PKCE to the customer's IdP, ID token exchanged for temporary cloud
credentials). The per-provider table produced two inversions worth carrying: **R2, recommended on
cost, has no OIDC federation at all** (verified) and works only behind Cloudflare Access, while
**Azure Blob, named as the S3-API integration gap, is the easiest provider for browser auth**. §11
question 2 and §10.3 updated.

**2026-09-20, eighth round.** The owner asked what other axes §2.4's provider comparison was missing,
after the R2 auth finding showed cost had been the only one examined. Four were checked and two
change a decision: **lifecycle expiry exists on all four** (R2 caps at 1,000 rules and is eventual
within ~24h; B2's model is version-oriented), and **the payload layer must not be tiered to
Infrequent Access or Cool** — at ~116 KB objects the 128 KB minimum billable size inflates the bill
12.7% to save **$0.008 a month**. The sharper form is a **trap**: Standard-IA's 30-day and Azure
Cold's 90-day minimum durations collide with §11 question 5's *configurable* retention window, so a
customer shortening retention would pay the minimum anyway. Also recorded: CORS `range` headers are
configurable on all three S3-compatible providers, and **data residency is free under
bring-your-own-bucket**, an advantage §6 had not named. Durability, object size, consistency and rate
limits were deliberately not chased, and §10.3 says so rather than leaving it silent.

**2026-09-20, ninth round.** The owner asked what installing the dashboard actually takes. Counting it
showed §6.1's onboarding bullet had asserted a cost without ever enumerating it, and that **the install
is three tiers, not one**: Tier 0 (shipped) is a tool install plus ~45 lines of copied YAML; Tier 1
adds two steps and no cloud account; Tier 2 is where the bucket, credentials, compaction and proxy
land. That makes the story **progressive** — nobody onboards into the warehouse, they grow into it —
but it also locates the real problem in **Tier 0**, the tier everyone meets first, where 45 lines of
orphan-branch and worktree bash is the first impression and `templates/github-actions/` does not
exist. The fix is #72's composite-action shape, and it is now **recommended ahead of the alerting
follow-up**; §11 question 7 was rewritten to order the two.

**2026-09-20, tenth round.** The owner observed that the plans assume every customer is on GitHub, and
**stated the requirement: GitHub and Azure DevOps both first-class, others where practical.** An audit
found the library in better shape than the plans — Azure DevOps is already detected, run identity has
a documented escape for undetected providers, and the ledger's location is a plain file path — but
`CiEnvironment` covers exactly two providers, the only documented recipe is GitHub Actions, and
`DASHBOARD_PLAN.md` is GitHub throughout. **The finding that matters: Tier 1 is the rung that does not
port**, because Azure DevOps has neither Pages nor anonymous raw file access, so an ADO customer goes
Tier 0 then Tier 2 — which makes **§3.4's bucket-served page forge-neutral and Tier 2 more portable
than Tier 1**, the reverse of what the ladder implies. Four concrete requirements for ADO parity are
listed, all absent from every plan, and §11 question 10 asks how far "the others" reaches. The same
pass restored the fifth-round entry above to chronological order; it had been anchored on a sentence a
later edit extended.

**2026-09-20, eleventh round.** The owner asked for the Microsoft-stack pattern to be sorted out
rather than noted again. It had been recorded three times in three places — §11 question 2 (Blob has
no S3 API), §6.2 (Blob is the easiest provider for browser auth), §6.1 (four things ADO parity needs)
— and never added up. **§6.3 assembles them as F9**, with the reframe that matters: the last three
rows of its table (browser auth easiest, the §6.2 proxy free, residency free) mean the Microsoft stack
is **the shortest path to a complete offering, not a grudging port**. Its four-item work table
supersedes §6.1's list and §11 question 2, which were the same work counted twice; items 3 and 4 move
**inside M2** rather than after it, and **Azure plumbing is recorded as outranking a second language**
in §6.2's ordering, because it serves a buyer who exists rather than one who might. The first task is
half a day of vendor verification, since every Azure platform claim is REFERENCE. The same pass
renumbered §0's findings table, which successive insertions had left reading F1-F5, F7, F9, F8, F6.

**2026-09-20, twelfth round.** The plan was committed (`f01f8f19`), and the owner asked what it had
missed. Twelve probes found twelve absences, and the important one is a **design gap rather than a
gap in the writing**: **F10, there is no atomicity story**, where the ledger had a deliberate one
(`merge=union` plus a fetch-and-rebase retry, exercised by eighteen lanes every run). Object storage
has no merge and §3.5's compactor runs against live writers and readers. §3.4 now proposes a manifest
with an atomic pointer swap over a full table format, and makes it **M2's first task, ahead of the
layout**, because a format already holding years of data cannot have atomicity retrofitted cheaply.

Two more went to their own homes. **§4.3: the backfill does not reach backwards** — a ledger keeps
already-templated text, so history migrated from one is **permanently stuck at its rule version**,
which anyone reading §4.2 would have assumed otherwise. **§5.4: subject-level erasure** is a different
and harder ask than the per-repository deletion the plan had covered.

The remainder became **§8.6**, including the three the house style requires and this plan lacked:
a semver statement (every slice MINOR, the two writer defects PATCH), the wiki pages that move, and
**a testing strategy — the largest omission, because whether store tests need containers is decided
by the fake and that decision belongs in M0**. Also there: **M5, migration**, which no slice covered
though every existing user has a ledger; the store as **system of record** with no backup position;
a **format version** for the store; **pipeline observability** and what the gate does when the store
is unreachable; and whether non-.NET platforms write to the store directly.

The pattern worth recording: after eleven rounds this plan was heavily verified on **storage** and
thin on **operations**. Every axis the owner chose found something; every axis chosen here was about
bytes.

**2026-09-21, thirteenth round.** The owner asked for a critical assessment: anything missed,
anything to improve, any decision that is wrong. **§13 records twenty findings, R1 to R20**, checked
by reading the code and the harness, by two measurements, and by two verification agents. The ones
that change the design: **verdicts are not write-once** (R1: eleven options, the baseline stream, two
editable files and append order decide them, and the rules carry no version, so §5.1's premise is
false and M1 needs F5's treatment); **the analyzer has no "as of"** (R2: a shipped defect, a re-read
counts later runs as prior, and it is the very operation M1, M5 and time travel perform); **the id
renumbering should go** (R3: `OtlpSpanMapper` publishes both "safe" keys, the harness checked one,
and sequential integers collide across runs after `ToGuid`, all for two cents a month);
**`FromLog` loses at least four more things** than F7 found (R4: `Error`, the attribution fields,
`FocusFields` / `NoteOnRight`, the phase variants); **columnar does not buy per-column retention**
on immutable objects, the blob split does (R5); and **the compactor rewrites the only copy** (R6),
answered by making immutable per-run bundles the record and Parquet a rebuildable index. The ones
about what is absent: nobody renders the point-in-time report (R10), attachments are uncounted files
(R11), the re-templatable core is a bet on future rules and 64% of it is payload kept for ever (R12),
the layers are not defined (R13), there is no branch dimension (R14), and **Parquet was never
measured** — every figure is long-window zstd-19 over JSON, and the "79 MB verdict layer" is the
ledger (R15). **R16 is the one about order**: three products are being built as one, and the two the
owner asked for (years of verdict-level trends; any past run's evidence) need neither the projection
nor a columnar store, while the slices build the third product's machinery first. Closed on the way:
the range-request question, measured (R18: 206 and a correct `Content-Range`, preflight refused with
403, and never load-bearing for a store that lives in a bucket); the 120 GB/month egress, which has
no derivation and drives §2.4's table (R17); **the vendor check §6.3 named as its first task, done**
(Azure's first 100 GB of egress is free, so §2.4's Azure row is $1.74 and not ~$10; Azure has **no**
zero-compute auth proxy, so §6.3's "free" row was wrong and the Azure route is a bearer token in
JavaScript, R19; Backblaze B2 has **no conditional write**, so a manifest swap excludes it, R7;
DuckDB-WASM cannot send an `Authorization` header, reads whole files by default and fetches its
Parquet reader from a third origin, R9; and no browser decompresses zstd natively, R20); and three
stale facts corrected in place (the 3.2 GB
headline, §10.2's round-trip bullet, §4.1's rule count, which 3.22.2 made four while this round was
being written). §11 gains questions 14 to 20. **No decision was changed**, and no source file was
touched: another session was releasing from the same working tree throughout (3.22.2 in
`src/Kronikol/History`, then uncommitted work in `src/Kronikol/Ingestion`), so R2's defect is recorded
for filing rather than fixed here.

The pattern this time: **the plan concluded that storage should stop driving the design, and four of
its decisions were still driven by storage.** And it made fingerprints derived because rules change
while making verdicts permanent, although the verdict rules have changed more often.

**2026-09-21, fourteenth round.** The owner answered both things §13 put to them: file and fix R2, and
settle §11 question 19 as recommended. **R2 is #95 and shipped as 3.25.1** (PATCH: no public member
moves). The fix went further than the three lines the finding named, because the analyzer's cut alone
leaves the worse case standing: a report older than the reader's window is not in memory at all, so
the *reader* had to learn which run and which stream a read is for. It now indexes every run line by
id and stream, reads a stream back from before the run's own line, and parses a line the window let
go only when asked, from the ledger read a second time, so nothing is held in memory that was not
before. That also fixed the stream-crowding defect R2 recorded "beside it". Measured on the consumer's
448-run ledger: 265 runs and 2,413 scenario verdicts read differently today than when they ran, and
none after. **Question 19 is answered and §8.0 records the order**: P1 years of verdict-level trends
on the branch, P2 durable reports by run id, P3 the facts layer, with M0 to M5 now P3's slices and M0
no longer the gate for time travel. The noise plan was executed in full the same morning (3.22.2 to
3.25.0), which closes one of this plan's two prerequisites and, through 3.23.0, 3.24.0 and 3.25.0,
added three more data points to R1 (recorded there), including the one that matters to M3: part of
the fingerprint rule is now the consumer's own, so a backfill needs their rules and not only their
hash.


---

## 13. Critical review (2026-09-21)

**The owner asked for a critical assessment: what is missing, what could be better, which decisions
are wrong.** Checked rather than argued, which is what §12's rounds taught: direct reads of the code
and the harness, two measurements, and two verification agents (one running probes against the built
`Kronikol.dll`, one reading vendor documentation). Levels as in §1. **Nothing here changes a decision
on its own authority**: §11 gains the questions (14 to 20), and three factual slips found on the way
are corrected in place with dated notes (the 3.2 GB headline, §10.2's round-trip bullet, §4.1's rule
count).

**Two patterns account for most of it.**

1. **§2.4 concludes that storage should stop driving the design, and storage still drives four
   decisions**: renumbering ids (§3.2), payload expiry argued from bytes (§3.3), columnar chosen "for
   retention" (F6), and the projection treated as the gate because reports are a third derived (F4).
   At $0 to $0.30 a month none of them earns the risk it carries.
2. **Fingerprints are made derived because rules change (F5), and verdicts are made permanent in the
   same breath (§5.1), although the verdict rules have changed more often than the fingerprint rules.**

| # | Finding | Level |
|---|---|---|
| **R1** | Verdicts are not write-once; §5.1's premise is false and M1 is built on it | READ + RUN |
| **R2** | The analyzer has no "as of": a shipped defect, and this plan's core operation | READ + RUN |
| **R3** | Id renumbering (§3.2) should be dropped: OTLP export publishes both "safe" keys, the harness checked one of them, and sequential integers collide across runs after replay | READ |
| **R4** | `FromLog` drops at least four more things than F7 found | READ |
| **R5** | Columnar does not buy per-column retention on object storage; the file split does | reasoning + vendor docs |
| **R6** | The compactor rewrites the only copy; make immutable per-run bundles the record and Parquet an index | design |
| **R7** | F10 over-scopes the contention: the shipped recipe folds in ONE job | READ |
| **R8** | The socket rule moves the commit protocol into untested shell, and the rule already has a shipped exception | READ |
| **R9** | DuckDB is named in three places and costed in none; in the page it cannot send a bearer token, reads whole files by default and needs a third origin | RUN |
| **R10** | Nobody renders the point-in-time report | READ |
| **R11** | Attachments are files; no figure, layer or rule covers them | READ |
| **R12** | The re-templatable core is a bet on future rules, and 64% of it is payload kept for ever | READ |
| **R13** | The layers are not defined consistently, so what survives day 91 is unknown | READ |
| **R14** | The store has no branch dimension, and lifecycle rules are prefix-based | READ + vendor docs |
| **R15** | Every byte figure is long-window zstd-19 over JSON text; Parquet was never measured, and the "79 MB verdict layer" is the ledger | READ |
| **R16** | Three products are being built as one, in the order that shows nothing until the end | strategy |
| **R17** | §2.4's 120 GB/month egress is underived and drives the provider table; the Azure row is wrong on its own terms ($1.74, not ~$10) | COMPUTED + vendor page |
| **R18** | The range-request question is answered, and was never load-bearing for this plan | RUN |
| **R19** | §6.3's "free auth proxy" on Azure does not exist: nothing fronts Blob without compute | vendor docs |
| **R20** | No browser decompresses zstd natively, and every figure in §2 is zstd-19 | RUN + vendor data |

### 13.1 Decisions that do not survive checking

**R1. Verdicts are not write-once.** §5.1: *"A verdict for a run depends only on the runs before it,
so it never changes once written."* Read and probed, a verdict is a function of: the prior runs **in
append order**; eleven options (`Window` 50, `MinRuns` 5, `FlakyRate` 0.1, `SlowerBy` 1.5,
`SlowerMinMs` 100, `AlternatingRuns` 10, `CountRuns` 2, `PartialThreshold` 0.10, `ReportReordered`,
`Branch`, `CompareBranch`; `HistoryVerdicts.cs:101-131`); the baseline stream, which for a pull request
comes from `GITHUB_BASE_REF` / `SYSTEM_PULLREQUEST_TARGETBRANCH` **at analysis time**
(`CiMetadata.cs:80-92`); and `quarantine.json` and `aliases.json`, both applied at read, so a
rename alias added later changes an old run's `new` verdict. Probes: `CountRuns` 2 reads `stable`
where 1 reads `behaviour-changed`; one run reads `always-failing` against its own stream and `broke`
against main; the same prior runs appended pass-then-fail read `failing`, fail-then-pass `broke`.

**There is no version for the verdict rules anywhere.** `InteractionShape.Version` versions the
templater and `HistoryFormat.Version` the file. No verdict is stored today; every surface recomputes.
The rules have moved in at least four releases, by the changelog's own words: 3.15.0 "a design
change to a verdict rule", 3.16.0 "one changed verdict rule", 3.18.0 added `alternating`, and 3.20.0
made a count change a verdict "on the second run that holds it, not the first". And `gate`,
`query history` (a fixed window of 50, `QueryOptions.cs:115`, and no slower or flaky flags) and
`merge --history` (every default) already disagree with each other about the knobs.

**Consequence.** A verdict stored at ingest and never recomputed turns every analyzer release into a
step in the trend chart. 3.20.0's count confirmation took 13 verdicts to 2 (§6); a years-deep chart
would draw that as an 85% improvement on the day of the upgrade. **The fix is F5's, applied to
verdicts: a verdict is a cache, not a fact** — stamped with a rules version (to be invented), the
options, the baseline stream and the as-of position, and recomputable by the backfill M3 already
builds. M1's acceptance ("byte for byte identical to what the CLI analyzer produces") has to say which
CLI surface, since three disagree; and M1 has nowhere to put its rows until M2 exists, which §0's
dependency table does not show.

**The same day supplied three more data points.** 3.23.0 ("a partial run no longer sets the bar a full
run is read against") and 3.24.0 ("no slower verdict is read in one or against one") each changed a
verdict rule, and 3.24.0 added a twelfth option, `DegradedBy`. That is six releases that moved the
verdict rules inside the feature's first month. And **3.25.0 put part of the fingerprint rule in the
consumer's hands**: `HistoryShapeTemplates`, whose hash rides on the run line as `shapeRules`, so
fingerprints are comparable only when the pair (`InteractionShape.Version`, rules hash) matches. For
this plan that widens F5 and M3: a backfill is triggered by the consumer editing a rule as well as by
a Kronikol release, and it needs the consumer's rules as an input, which live in their test
configuration and not in the store. The store has to record the rules themselves, not only their
hash, or a backfill cannot reproduce a fingerprint it made last month.

**R2. The analyzer has no "as of".** `HistoryAnalyzer.AnalyseStream` (`:72-78`) takes every run of the
stream whose id differs from the current run's and keeps the last `Window`. It never cuts the ledger
at the current run's position. At run time that is harmless, because a run is analysed before its line
is appended. On any later re-read it is wrong. Probe: run 3 read `stable`; after runs 4 and 5 were
appended the same report read `fixed — failed the previous 2 runs`, and those two runs are later ones.
It affects `query history`, `history gate` and `merge --history` over any report that is not the
newest, the only test of the path pins the case where the current run is the last line
(`HistoryAnalyzerTests.cs:759-771`), and **it is exactly the operation M1's and M5's acceptance
criteria and a time-travelled report perform.** Beside it: the ledger reader's window is per *suite*
across all streams while the analysis is per stream (`HistoryLedger.cs:231-239`), so pull-request runs
can crowd a quiet `main` out of its own window (probe: four PR runs after six main runs left main one
usable run, under `MinRuns`). **Not fixed in this round**: another session was releasing from the same
working tree throughout (3.22.2 in `src/Kronikol/History`, then uncommitted work in
`src/Kronikol/Ingestion`). **Filed as #95 and fixed the same day as 3.25.1**, once that session was idle:
the reader indexes every run line by id and stream, `HistoryLedger.PriorRuns` reads a stream back from
before the run's own line, and a line the window let go is parsed on demand from the ledger read
again. **Measured on BreakfastProvider's CI ledger before the fix: read again today, 265 of its 448
runs had at least one verdict that differed from what the run read when it ran, 2,413 scenario
verdicts of 88,396. After it: none.** Replayed with each run cut at its own line, the two builds are
byte-identical, so nothing a run read when it ran moved. `tools/history-replay`'s header had said all
along that the analyzer "does not cut there itself": the workaround was written and the defect was
never filed.

**R3. Drop the id renumbering rather than tune it.** Three independent problems.

- **Kronikol's own OTLP export publishes both "safe" keys.** §3.2's rule is "renumber only if the
  literal value is meaningless outside the run". `OtlpSpanMapper` exports the span id as the first 16
  hex of `RequestResponseId` whenever no `ActivitySpanId` was captured, and under
  `TraceIdStrategy.PerPair` exports `TraceId` as the trace id (`:280-295`). Those literals live on in
  the customer's tracing backend: the concern §3.2 raises for the W3C ids and misses for its own.
- **The harness checked one of the two.** `s3b_lossless` counts `requestResponseId` occurrences in
  payloads and never looks at `traceId`, which §1 nevertheless lists as RUN.
- **Sequential integers are the cross-run reuse §3.2's last paragraph forbids.** Every run's first
  pair becomes `"0"`, and `InteractionRecord.ToGuid` is `MD5(text)` (`:451-457`), so after replay pair
  17 of every run ever stored carries the *same* `RequestResponseId`. That is manufactured cross-run
  identity, handed to `merge`, `diff` and any re-export. Salting with the run id at replay would fix
  it and is nowhere designed.

The saving is 43,612 bytes a run: 1.4 GB a year, **two cents a month**. Store the ids verbatim.
181,953 becomes the engineered figure (6.0 GB a year), and every conclusion in §2.4 survives it.

**R5. Columnar does not buy per-column retention; the file split does.** §3.3: *"That policy is only
expressible if the storage format lets a field be dropped without rewriting the run, which is what
columnar buys."* A Parquet file is immutable and so is an object: dropping a column **is** rewriting
the file. What delivers "bodies expire, facts stay" is §3.4's decision to put them in different
objects with a lifecycle rule on one prefix. After F8, F6's rationale is vestigial, and M2's
"per-column retention" and §9's "per-column retention is decided too late" describe a problem the blob
split already solved. The honest case for Parquet is projection and row-group pruning for call-level
analytics, which is R16's product (c), the one with no demonstrated demand.

**R6. The compactor rewrites the only copy.** §8.6 names the bucket as a single copy. It does not
name the likelier loss, a compactor bug, and §9 has no such risk. The design also splits one run
across two stores that must agree (a payload blob without its rows, or rows without their blob, is a
two-store atomicity problem the manifest does not obviously cover). **Proposed: the immutable per-run
bundle is the record, and everything columnar is a rebuildable index over it.** Per run, objects
under a unique key, never rewritten: `facts/` (everything except `content` and `headers`) and
`payloads/` (those two; the only prefix with a lifecycle rule). Point-in-time is two GETs and no join;
the compactor can be wrong and rerun; a schema change is a rebuild rather than a migration, so §8.6's
format-version question mostly dissolves; and F10 shrinks to R7. Cost: the 22 KB core exists twice,
3.7 GB at five years, about five cents a month. "The log is the truth, tables are views" is the rule
the ledger already follows.

**R7. F10 over-scopes the contention.** The wiki's recommended shape is *"One job after every shard,
with `contents: write`: fold and push."* Eighteen lanes do not write concurrently: each uploads a
fragment artifact and **one job folds**. The rebase retry exists for concurrent *workflow runs*. The
same fan-in carries over: one ingest job per workflow run, which a CI `concurrency:` group serialises.
With R6's unique immutable keys, writers never touch shared state, and the manifest has exactly one
writer, the scheduled compactor. The conditional write becomes insurance rather than the protocol
(and it has to be, because **Backblaze B2 has no conditional write at all**: its Put Object documents no conditional header and `If-None-Match: *` returns 501, while GCS spells its own as `x-goog-if-generation-match`, so "one S3 code path" does not cover the manifest swap even among the S3-compatible four. S3 since 2024-08-20 and 2024-11-25, R2 since 2022, Azure and MinIO all have it; vendor docs read 2026-09-21). What F10 omits and any version of this needs: **idempotent ingest** (a retried
job re-uploading the same run; `If-None-Match: *` on the run key) and an **orphan sweep** for objects
uploaded before a failed publish.

**R8. The socket rule moves the hardest logic into the least tested place.** "Kronikol writes files;
the workflow moves them" puts the commit protocol (conditional PUT, retry, orphan sweep, compaction
upload) into per-provider shell, per CI system, in each consumer's repository: §6.1's "45 lines of
worktree bash" again, for four clouds and two forges. §14 says "no HTTP client … in the library or
the tool", and that is true of both. But `Kronikol.Extensions.Otlp/OtlpExporter.cs` is, in its own
words, "a standalone `HttpClient` POSTing to a URL": an opt-in extension package talking to an
endpoint the customer configures with credentials the customer supplies. A `Kronikol.Extensions.Store.*`
family on the ClickHouse-pairing pattern (adapter interface in core, one package per provider) would
keep the protocol in tested .NET and leave the core and the tool socket-free. **This is the real
first question of M2, ahead of the manifest**, and §8.6's testing question is downstream of it: under
"the workflow moves them" Kronikol's code only ever touches a directory and needs no Azurite or MinIO
at all, while the shell recipes need exactly that.

**R9. DuckDB is named in three places and costed in none.** Measured on the NuGet flat container,
2026-09-21: `DuckDB.NET.Bindings.Full` 1.5.5 is **109,800,495 bytes**, against `Kronikol.Tool` 3.22.1
at 7,295,050 and `Parquet.Net` 6.1.0 at 634,432. Embedding DuckDB would grow the tool fifteenfold for
a 4.5 GB store; the managed library reads and writes Parquet, with projection and statistics, for
nothing. **In the browser it is worse, measured 2026-09-21 against `@duckdb/duckdb-wasm` 1.33.1-dev57.0 in Chrome 153:** `duckdb-eh.wasm` is 35,913,747 bytes, 6,163,011 brotli (jsDelivr serves 7,124,338), plus a 773 KB worker; **Parquet is not in the core**, so a further 522 KB is fetched at run time from `extensions.duckdb.org`, a third origin, and blocking it crashes the engine; it needs a Web Worker (its range reads are synchronous XHR), `'wasm-unsafe-eval'`, `worker-src blob:` and a `connect-src` naming that third origin; **its default configuration GETs the whole file with no `Range`** (ranges need `forceFullHTTPReads: false` and `allowFullHTTPReads: false`, after which a `count(*)` over a 515 MB file cost 12 KB); it opens with a `HEAD` carrying `Range: bytes=0-` and demands a 206, which Azure Blob answers with 200; and **it cannot send an `Authorization` header at all** (duckdb-wasm #1967, open; only S3 SigV4 settings exist). Set that last one beside R19: on Azure, where nothing can front Blob without compute, a bearer token is the *only* route to the data, and DuckDB-WASM cannot carry one. In the page it collides with the `DASHBOARD_PLAN.md` rules this plan says it
adopts unchanged (one `index.html`, hash CSP, libraries inlined, nothing fetched but data). That plan
weighed 161 KB of Plot and D3 with care; this one adds a SQL engine in a line. **Proposed: the page
never queries.** The compactor is a build step, so let it emit query-shaped files (the monthly view,
and one small file per scenario: 9,190 rows over five years is a few hundred KB) that the page fetches
whole. That keeps "no verdict computed in JavaScript" honest (SQL aggregation in the page is the same
erosion under another name), works with any auth scheme because the page sets its own headers, needs
no range requests, and keeps today's E2E approach. DuckDB stays what it should be: a tool a *customer*
may point at an open format in their own bucket, which is a feature to advertise and not a dependency
to ship.

### 13.2 What is missing

**R4. `FromLog` drops more than three things.** `RequestResponseLog.cs:10-116` against the 29 members
of `InteractionRecord` and `FromLog` (`:164-193`). Beyond F7's `PlantUml`, `DurationMs` and the marker
fields, the record has **no member at all** for: **`Error`** (3.18.0's failed-send message chain,
which is evidence, and what a reader opening a failed run came for); **`AttributionSource`** and
**`ExpiredFromTestId`** (3.17.0 to 3.20.0 provenance: how the call got its scenario, background bucket
included); **`FocusFields`** and **`NoteOnRight`** (author-supplied rendering intent, not derivable);
and **`SetupVariant`** / **`ActionVariant`** (phase verbosity computed by the capturing extension,
not re-derivable at replay without it). §9 says "the risk is a fourth found later"; it took one read
of two files. #93 and #94 undercount. M0 should start from a mechanical member-by-member diff of the
two types, **held by a test**, so a new `RequestResponseLog` member cannot ship without a decision
about the record. *(Done: `INGEST_FEED_PLAN.md` F1 ran the diff mechanically and found 16 of 38
members lost, this list plus `CollapsedCount`/`CollapsedSummary` (render-time, excluded by design)
and the three flags `IsOverrideStart`/`IsOverrideEnd`/`IsActionStart`, lost with every marker until
3.29.0; `RequestResponseLogRoundTripTests` holds the classification since 3.27.4, with the seven
members of this paragraph pinned as known gaps for roadmap 14.1.)*

**R10. Nobody renders the point-in-time report.** §3.1: *"'Any point in time' is `kronikol ingest`
over the rows for one run."* `kronikol ingest` is a .NET tool; §3.4's page is static and there is no
server. The plan never says who runs it, where, or what the reader clicks. Cheapest first: keep the
rendered report for the payload window as a third per-run object and link it (zero compute, and it is
what `DASHBOARD_PLAN.md` §6 already calls "the hand-off to the report artifact", made durable); a CLI
verb the reader runs against a downloaded bundle; a dispatched CI job. §11 Q3 asks *which renderer*;
the prior question is *which process*.

**R11. Attachments are files.** `TestRunRecord.cs:22-24,144-146`: an attachment is "a screenshot, a
trace archive", carried as a path and copied into the report directory. M0's acceptance says "every …
attachment identical"; every byte in §2 comes from `TestRunReport.json`, which holds the reference
and not the file. A Playwright suite (`Kronikol.Playwright` ships) with a screenshot per step or a
trace archive per failure is megabytes per scenario. **This is the one realistic way F1's "no scale
makes storage the constraint" is false**, and the layout, the lifecycle rules and §5.4 never mention
it. Screenshots of a UI are also the likeliest place for personal data in the whole store.

**R12. The re-templatable core is a bet, and most of it is payload.** Three things, read from
`InteractionShape.cs` as of 3.22.2. (a) §4.2's "exactly" is incomplete even for today's rules:
`Calls` also reads `Type`, `TrackingIgnore` and marker-ness to decide what is a call, needs a scenario
key to group by, and needs record order for `shapeOrdered`. None is in the measured 22,069; all are
cheap. (b) The core exists for **future** rules and holds only what **today's** read. A v5 that tells
GraphQL operations apart (every call is `POST /graphql`; the operation is in the body), or gRPC, SOAP
or message types by a header, cannot be backfilled past the payload window. The list is cheap to
widen now and impossible to widen later. (c) **`stmtHead` is payload content in the keep-for-ever
layer**: up to 2,000 characters of every SQL, Cosmos or Mongo statement, literals included, 64% of
the core. §5.4 flags a token in a URL and misses `WHERE email = 'a@b.c'`, which outlives the payload
expiry §5.4 offers as the exposure bound. (b) wants more kept and (c) wants less. That is the real
trade, and it deserves a question rather than a default.

**R13. What survives day 91 is unknown.** §3.3 defines the stable layer as URI, method, service,
caller, status, category, statement head, *"the scenario roster, results and durations"*, "about
22 KB". But 22,069 is `s7_retemplatable`'s eight columns and nothing else: no steps (11.5% of the
compact report), no tests stream, no timestamps, no per-call durations, no errors. "Payloads 116 KB"
is the remainder by subtraction, so it silently includes step text, stack traces and both W3C id
columns. Whether a two-year-old run still shows its steps, its error and how long each call took is a
product decision the byte tables currently make by accident. Per-call latency over years, the obvious
trend after pass and fail, needs a duration column the stable layer does not have.

**R14. No branch dimension.** The word does not occur in §3. Runs come from `main` and from pull
requests, the analyzer is per stream, and retention by branch class (main for years, a PR branch for
weeks) is a larger and more natural lever than retention by layer. Lifecycle rules select objects by key prefix: S3 also by tag and size, R2 by prefix only (1,000 rules), B2 by `fileNamePrefix` only (100 rules), and B2 rejects `x-amz-tagging`, so **the key prefix is the only portable lever** (vendor docs, read 2026-09-21). So the class has to
be in the object key from the first write, and keys are immutable: a day-one layout decision of the
same kind as F10.

**R15. The recommended format was never measured.** `measure.py:29-34, 222-244`: every figure is
zstd-19 with long-distance matching and a 2 GB window over JSON text. "Columnar" in F6 is
newline-joined JSON strings per key, one stream each. It is not Parquet, which compresses per column
chunk per page, typically at level 3 with no long window, plus a footer of several KB per file that
matters at 22 KB a run before compaction. **The "79 MB verdict layer" is the ledger** (`s9_ledger`):
raw points, including the fingerprints M3 removes, compressed as one 4.7 MB stream, at 2.3 bytes per
scenario-execution because `results` is one character per scenario. M1's rows (one per
scenario-execution with evidence text, 32.6 million over five years) are a different object and
nobody has sized them. None of this threatens F1. It does mean §3.4's per-layer sizes and §3.5's
"79 MB, fetch it whole" are estimates carrying RUN labels. F2 has the same shape: `s5_reruns`
regenerates *every* id and draws every duration from a uniform distribution, so "the ids defeat
dedup" is partly the simulation's premise. §10.4 already lists the real check; F2 should read
SIMULATED until it is run. (Smaller, same file: `STATEMENT_HINTS` is a substring list that includes
`storage` and `queue`, which `DependencyCategories.StatementShaped` does not, and omits Redis,
DynamoDB, Elasticsearch, Oracle, Spanner, Bigtable and AtlasDataApi, which it does. 14,172 is
approximate.)

### 13.3 The order of work

**R16. Three products are being built as one.** The owner's ask has two halves and the plan adds a
third.

- **(a) Trends over years, at verdict level.** `DASHBOARD_PLAN.md`'s window exists because the view is
  *inlined* (its §7: 1.2 MB gzipped at window 200), not because history is big: 337 B gzipped a run
  is about 11 MB a year. Monthly view files on the existing history branch, where `merge=union`
  already works, with the page inlining the recent window and fetching older months whole, is years
  of trends with **no bucket, no credential and no new atomicity problem**. R18 measured the host.
- **(b) Point-in-time evidence.** A dumb blob store holding what CI already emits, keyed by run id and
  linked from the run line, which already carries `url`. Measured 2026-09-21 on the same lane: the
  HTML report is 3,898,460 B, **981,768 gzipped**; with §2.1's 604,152 for the JSON that is 1.59 MB a
  run, 13 GB steady at 90 days or 52 GB per year kept: about $0.80 a month on R2 for each year of
  history, and nothing at all inside a 90-day window on its free tier if only the HTML is kept. **No
  projection, no Parquet, no compactor, no manifest.** Re-fingerprinting (F5) can read those reports
  too, for as long as they are kept: `measure.py` derives the core from exactly that file.
- **(c) Call-level analytics and a compact facts layer.** The only part that needs M0's projection as
  a gate, a columnar index and compaction, and the only part with no demonstrated demand.

The slices build (c)'s machinery first (M0 to M3) and show a reader something at M4. Inverted, each
step ships something visible, the first two are small, and §6.1's own warning ("one dogfood consumer
is the whole evidence base") is applied to the store and not only to SSO. M0's projection stays worth
doing early for its own reasons (#93, #94, R4, and `PLATFORM_FOUNDATIONS_PLAN.md` F2); it stops being
the gate for time travel.

**And the document is four documents.** A storage investigation (§2 to §4), a positioning memo (§6),
an auth architecture (§6.2) and a forge-portability and onboarding audit (§6.1, §6.3). By the plan's
own argument the highest-value near-term work (#72's action, the Azure Pipelines recipe, #93/#94,
#91) sits *outside* its slices. Splitting it lets each be green-lit on its own.

### 13.4 Facts corrected or closed

**R17. The 120 GB/month.** It appears once in §2.4 with no arithmetic, and is the whole 4.5 GB store
read 27 times a month, against §3.5's "a trend query reads kilobytes" and "opened a few times a day by
a handful of people". A 1.5 MB page loaded 20 times a day plus 50 payload drill-downs is about 1 GB a
month. "Storage is at most 5% of the bill, the rest is egress" is an artefact of the 120.
**And the Azure row is wrong on its own terms.** The S3 row subtracts a 100 GB free allowance and the
Azure row does not, but Azure's bandwidth page reads "First 100 GB/Month: Free" and then $0.087 for
the next 10 TB, "to all customers in all Azure regions" (read 2026-09-21). 120 GB is 20 × $0.087 =
**$1.74, not ~$10**; §2.4's row is corrected in place. Recomputed at a few GB a month, all four
providers come in under about $0.25 and price stops discriminating between them at all. That matters,
because the table made Azure look five to ten times the others, and §6.3 says Azure is where the buyer
is.

**R18. Range requests, measured.** curl, 2026-09-21, against
`raw.githubusercontent.com/lemonlion/BreakfastProvider/kronikol-history/history.jsonl`: `HEAD` is 200
with `Accept-Ranges: bytes` and a `Content-Length`; `Range: bytes=0-99` is **206** with
`Content-Range: bytes 0-99/4927833` and `Access-Control-Allow-Origin: *`; with `Accept-Encoding: gzip`
the range indexes the *gzipped* representation (`/701228`), harmless in a browser because the Fetch
standard appends `Accept-Encoding: identity` to a range request; **the CORS preflight is refused,
403**, with no `Access-Control-Allow-Headers`. So ranges work only because a single byte range of the
form `bytes=N-` or `bytes=N-M` is CORS-safelisted and sends no preflight (the Fetch standard and MDN,
read 2026-09-21; per-browser behaviour not checked). A **suffix range** (`bytes=-N`, the natural way
to read a Parquet footer) is not safelisted and would be refused, as would any request carrying
another header. **But it was never
load-bearing here**: this plan's store is the customer's bucket, where range and CORS are
configuration, and 4.6 GB a year with expiring objects cannot live on a git branch anyway. The host
matters only to R16 (a), which fetches whole files. §3.4's "unverified and load-bearing" paragraph
and §11 Q1.2 can close.

**R19. Azure's "free auth proxy" does not exist.** §6.3's table says *"The §6.2 auth proxy: free — App
Service Auth and Static Web Apps ship it"*, and its reframe leans on that row. Microsoft's own
documentation, read 2026-09-21: **Static Web Apps** holds 250 MB per environment on Free and 500 MB
(2 GB in total) on Standard, and its "routing rules can only secure HTTP requests to routes that are
served from Static Web Apps", so it cannot guard a separate storage account without an `/api`
backend, which is compute with a 45-second request limit; **Blob static-website hosting** answers
"Do static websites support Microsoft Entra ID? No", and does not support storage CORS either;
**App Service Authentication** is middleware "on the same virtual machine as your application", so it
guards blobs only if an app proxies every read. **There is no zero-compute Azure equivalent of
Cloudflare Access in front of R2.** What does hold, and was verified in the same pass: a browser can
read a private blob directly with an Entra token from MSAL.js (resource `https://storage.azure.com/`,
`x-ms-version` 2017-11-09 or later, the Storage Blob Data Reader role, and a CORS rule allowing
`authorization`, `x-ms-version` and `range`, which forces a preflight), and Azure Blob has no native
S3 API. So the Azure route is §6.2's *federated* architecture, not its proxy: the page on Static Web
Apps, the data read straight from Blob with a token in JavaScript. **§6.3's conclusion survives**
(still the shortest path, still the easiest browser auth); **"zero tokens in JavaScript" does not**,
and with R9 it rules DuckDB-WASM out of the Azure page. §6.3's table is corrected in place.

**R20. No browser decompresses zstd, and every figure in §2 is zstd-19.** `DecompressionStream` takes
gzip and deflate; Chrome 153 and Edge 153 throw a `TypeError` for `"zstd"` (measured), Firefox has it
behind a preference, Safari not at all, and it is not in the WHATWG specification. `Content-Encoding:
zstd` is a separate thing (Chrome 123, Firefox 126, Safari 26.3) and depends on the bucket serving
object metadata as a header. So a page that opens a payload blob needs either `fzstd` (MIT, 8,394
bytes minified, about 3.8 KB gzipped) inlined beside Plot, or gzip at rest, which the report's own
`decompressGzipBase64` already reads: §2.1's 604,152 against 225,196, 2.7 times the bytes and still
cents. Either is fine. Neither is in the plan, whose §3.4 stack goes from zstd objects to a page
with nothing in between.

**Small ones.** §8.6 lists MinIO among the candidate test fakes; `minio/minio` was archived in April
2026 (last push 2026-04-24), which belongs in that decision. §2.4 gives the stable layer as 3.5 GB at five years and §3.4 as 3.7 (22,069 × 90.6 ×
365 × 5 = 3.65); the tiered total is 4.7, not 4.5, by the same sum. `PLANS_STATUS.md` said "six open
questions"; there were thirteen. §11 Q2 kept three sentences from before it was folded into §6.3.
