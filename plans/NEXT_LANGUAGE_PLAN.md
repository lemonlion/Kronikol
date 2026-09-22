# NEXT_LANGUAGE_PLAN.md — which language next, and what a port should actually contain

**Date:** 2026-09-13 · **.NET repo:** 3.3.0 · **Kronikol4J:** 0.1.25-SNAPSHOT
· **Status: investigation complete, nothing implemented, NOT green-lit.**

> **Partly superseded 2026-09-14 by `PLATFORM_FOUNDATIONS_PLAN.md`** (the definitive four-platform plan).
> Its §5 keeps §1 (OTel cannot carry bodies), §2.1 (the cost model) and §3 (the ranking); §2's
> "scope to core+HTTP+SQL" is now that plan's §1 left column — a definition, not a recommendation.

> **Amended 2026-09-22 by `MOBILE_PLAN.md`:** §3's table gains three mobile rows (Android, Swift/iOS,
> Dart/Flutter) and a paragraph, and §5 gains B19 to B21. Nothing else changed.


**Fourth of four plans from the 2026-09-13 investigation.** `QUERY_FALLBACK_PLAN.md` (the .NET
fallback), `QUERY_PORTABILITY_PLAN.md` (shipping `kronikol query` beyond .NET),
`KRONIKOL4J_PORTABILITY_PLAN.md` (what a shared wasm renderer would retire), and this one — **which
language is worth porting to next, and how big a port actually has to be.** It revises §1 of the
third; see §1.4.

**It also overlaps three pre-existing plans it was written without reading** — `JAVA_PORT_PLAN.md`,
`NODE_PORT_PLAN.md` and `MONOREPO_MIGRATION_PLAN.md`. **§0 is the reconciliation and should be read
before anything else here.**

---

## 0. Read this first: what here is new, and what was already decided

**This plan was written without reading `JAVA_PORT_PLAN.md` (673 lines) or `NODE_PORT_PLAN.md`
(1,250 lines), and it partly duplicates them.** Corrected 2026-09-13. The honest accounting:

**Already decided, and better than here — do not treat §1 as a discovery.**
`NODE_PORT_PLAN.md` §3.12 reached the same capture conclusion independently and in more detail:

> *"native adapters = deep capture for priority drivers; OTel = shallow catch-all for the long tail
> (couchbase, cassandra, …) with zero per-driver code"* — with the fidelity cost named exactly
> (*"no rows, params often disabled for security — wide coverage, low fidelity, opt-in"*).

It also states the harder truth this plan only reaches by inference: *"The genuinely hard part is
body capture, not lifecycle"* (§3.11), and specifies the mechanism — an undici `Dispatcher`
interceptor plus a `node:http` monkeypatch, teeing streams to capture bodies without consuming them.
**§1 below is a rediscovery, not a finding.** Its value is as a correction to the 2026-09-13
conversation that proposed OTel-as-capture, and to `KRONIKOL4J_PORTABILITY_PLAN.md`'s header, which
had the same error. Cite §3.12 of the Node plan as the authority, not this.

**Genuinely new here:**

- **§3, the audience ranking.** No other plan compares language ecosystems or asks which is worth
  doing next. The C/C++ and Rust exclusions (§3.2) and the capture-feasibility axis are new.
- **The three-way split in `KRONIKOL4J_PORTABILITY_PLAN.md`'s header** — 17% irreducible / 38%
  fidelity choice / 45% shareable — which this plan's §2 numbers produced.

**Where this plan disagrees with a locked decision, and the disagreement is narrower than §2 makes
it sound.** Both port plans lock *"full feature parity as the end state, delivered core-first."*
Core-first delivery already avoids the failure mode §2 warns about — nobody is proposing to build 33
modules before shipping. The remaining disagreement is only whether **full parity should stay the
end-state commitment**, or whether core + HTTP + SQL should be the *supported* surface with the tail
explicitly OTel-shallow and marked as such. That is a real question for Kronikol4J 1.0 (§4), but it
is a question about a promise, not about sequencing.

**Also unread when this was written:** `MONOREPO_MIGRATION_PLAN.md` — the polyglot `dotnet/`,
`java/`, `js/` layout with a shared top-level `parity/` for goldens. §4's sequencing should be
reconciled against it.

---

**Two answers up front.**

1. **Do not build capture on OpenTelemetry.** OTel deliberately does not carry request and response
   bodies, and bodies are ~90% of a Kronikol report and the whole reason to open one. An OTel-based
   capturer produces a topology diagram with the product removed (§1). **`NODE_PORT_PLAN.md` §3.12
   already says this** — see §0.
2. **Python is the best target *after Node***, which already has a design-complete plan. Not because
   Python is the biggest ecosystem (JS/TS is) but because pytest's near-monopoly means one
   test-framework adapter where Node needs six and .NET needed fourteen (§3, §4).

**And a third, which matters more than either.** A port does not need 33 capture modules. **HTTP and
SQL are the 80/20**; everything else can degrade to OTel's no-body form. That takes a port's capture
half from ~18,000 lines to ~6,000 (§2).

---

## 1. The capture-architecture question, settled

### 1.1 OpenTelemetry cannot carry the payloads

*(Prior art: `NODE_PORT_PLAN.md` §3.11–§3.12 states this and specifies the mechanism. §0.)*

OTel's HTTP semantic conventions capture request and response **sizes**, not bodies. The reason is
architectural rather than an oversight: *"the HTTP client span is usually finished by the time the
body can be read from a stream after a response object is returned."* Issues
[#857](https://github.com/open-telemetry/semantic-conventions/issues/857) and
[#1219](https://github.com/open-telemetry/semantic-conventions/issues/1219) have been open on this
for years without resolution.

Set that against what Kronikol is. From the skill's own layer table:

| Layer | Share of the report file |
|---|---|
| Narrative | 0.4% |
| Topology | ~1% |
| Artifacts | ~0.2% |
| **Payloads — bodies and headers** | **~90%** |

and, in the skill's own words, *"a body is usually **the** answer, because that is where a wrong
number actually comes from."* `InteractionRecord.Content` is the field that carries it, documented as
*"Request or response body (already decoded/capped by the capturer)"* — **the capturer's job, by
design.**

**So an OTel-sourced capturer would deliver the topology and lose the product.** A Kronikol with no
bodies is a diagram of who called whom, which a tracing backend already provides.

The same applies to SQL: OTel's `db.statement` is typically the parameterized form, which is exactly
the degraded case the report's own `! some SQL was captured with placeholders and no values` banner
exists to warn about. Building on it would ship the caveat as the default.

### 1.2 The .NET design already resolved this correctly

`Kronikol.Extensions.OpenTelemetry` feeds `InternalFlowSpanStore` — *"used to generate internal flow
diagrams in the HTML report popups."* That is OTel used for what OTel is good at: internal span
topology, where no bodies exist to lose. Payload capture is done by bespoke adapters.

**That split is the architecture, not a gap in it.** Any new-language port should reproduce the split
rather than collapse it.

### 1.3 What OTel is still worth, precisely

Not as a replacement — as tail coverage:

- **The long tail of libraries** nobody will write an adapter for, in degraded no-body form. The
  report already has vocabulary for degraded records, so this fails legibly rather than silently.
- **Correlation.** Trace ids across hops; `activityTraceId` already exists in the model.
- **Internal flow**, as §1.2 — the feature it already powers on .NET.

One partial mitigation, recorded honestly: *some* OTel instrumentations expose opt-in body capture
through instrumentation-specific options. It is non-standard and varies by library and language, so
it cannot be a foundation, but it may cheaply extend the tail in specific cases. Worth checking per
language, never worth assuming.

### 1.4 Revision to `KRONIKOL4J_PORTABILITY_PLAN.md` §1

That plan says the capture half is 55% of the port and "irreducible". **Right in direction,
overstated in size.** It is irreducible as *shared code* — §1.1 does not change that, and neither
wasm nor a native binary can instrument a JVM. But it is not irreducible in *volume*: §2 scopes a
useful port at roughly a third of it. A11 in that plan is amended accordingly.

---

## 2. What a new-language port should actually contain

The .NET implementation is the maximal version: **41 `Kronikol.Extensions.*` capture modules and 14
test-framework adapters.** Kronikol4J reproduced 33 capture modules at 17,795 lines. Neither is the
target for a third language.

The 80/20, from Kronikol4J's own module sizes:

```
kronikol4j-core   3,600     the model, the writer, the run lifecycle
kronikol4j-http     858     HTTP client + server capture
kronikol4j-jdbc   1,013     SQL capture
                 ------
                  5,471     ≈ 31% of that port's capture half
```

**A first-cut port in a new language is: core + HTTP + SQL, with bodies, plus one test-framework
adapter, plus OTel for the tail.** Call it 5–6k lines rather than 18k. Everything else — Azure, AWS,
GCP, Mongo, Redis, Elasticsearch, Bigtable, Cassandra, ClickHouse, Spanner, EventHubs — is added on
demand, by evidence of demand, or never.

The contract is small enough to make this realistic: `InteractionRecord` has **five required
fields** — `type`, `uri`, `serviceName`, `callerName`, `testId` — with `content`, `headers`,
`statusCode`, `traceId` and `requestResponseId` carrying the rest. Unknown properties are ignored, so
a capturer can add diagnostics without a schema change.

And the rendering half need not be written at all if `KRONIKOL4J_PORTABILITY_PLAN.md` option B lands:
NDJSON to `kronikol ingest`, or to the shared wasm module.

### 2.1 What four platforms actually costs

**The arithmetic this section invites is "17% × 3 new platforms, plus some shared glue". That is the
right shape and the wrong magnitude.** Three corrections, in descending order of how much they
matter.

**(a) 17% is a fraction of the wrong denominator.** It is a share of Kronikol4J's 32,100 lines — a
total inflated by 30 capture modules a scoped port would not build and a renderer it would share. The
scoped port's own total is what matters:

```
core       3,600    model, tracking log, correlation, SQL classifier, naming, serialization
http         858
jdbc       1,013
adapter     ~100    per test framework (junit5 is 103; testng 83, cucumber 111, spock 94)
          ------
          ~5,600
```

**Carry ~5,600 forward, never "17%".** The percentage flatters it by measuring against something
nobody would rebuild. And none of `core` shrinks when the renderer is shared — its contents are
`TrackingSafeSerializer`, `UnifiedSqlClassifier`, `RequestResponseLog`, `ProcessingCorrelation`,
`ServiceNameResolver` — all capture-side, all still needed.

**(b) Lines do not transfer between languages, and Node is the expensive one.**
`NODE_PORT_PLAN.md` measured this and says so directly:

- Databases are *"the one genuinely harder area than Java"* — no JDBC equivalent, so per-driver wraps
  across `pg`, `mysql2`, `better-sqlite3`, `node:sqlite`, `mssql`, `oracledb`, **plus Prisma as a
  standing exception** (its Rust engine bypasses the JS driver).
- HTTP has *"no universal seam"* — two disjoint transport hooks (an undici `Dispatcher` interceptor
  and a `node:http` monkeypatch) where .NET has one `DelegatingHandler`.
- And it locks **six** test-framework adapters to Python's one.

Python should land near Java: monkeypatching is trivial and DB-API 2.0 is a genuine common seam, the
way JDBC is. **So Python ≈ 5,600; Node materially above it.**

**(c) The hard parts have low line counts.** `NODE_PORT_PLAN.md` ran **22 deep dives before writing a
line** — async context propagation, the identity seam, dual ESM/CJS singletons (its own *"#1 silent
risk"*), teeing bodies without consuming the stream. Those are a few hundred lines each and most of
the actual work. A line count systematically understates exactly the parts that are hard, so treat
5,600 as a floor on effort, not an estimate of it.

#### The model worth believing

| | Cost |
|---|---|
| **Per new platform** (scoped, renderer shared) | ~6–9k lines-equivalent — Python at the low end, Node above it — plus host shim, packaging, CI, release, and the design work that never appears in a count |
| **Shared, once** | wasm renderer productionised · conformance corpus · monorepo migration · four-way CI. **New infrastructure that does not exist yet, and is gated on E1** |
| **If E1 fails** | add ~14,305 lines-equivalent back **per platform** |

#### The line this arithmetic leaves out

> **A shared renderer takes *rendering* maintenance from N to 1. It does nothing for *capture*
> maintenance, which stays at N.**

Four capture implementations tracking undici releases, driver majors and framework upgrades — on
other people's schedules, not yours. That is the standing cost of four platforms, it does not appear
in any build estimate, and it is not reduced by anything in these four plans. The tail going to OTel
(§1.3) helps, because one bridge ages better than 30 adapters; the core does not.

**What makes four platforms viable is not the 17%.** It is that the renderer collapses to one
implementation and the tail degrades to OTel instead of being hand-written thirty times per language.
Both of those are still unproven — E1 for the first, B7 in §5 for the second.

---

## 3. The audience ranking

The table ranks **candidates for the next port**, so the two ecosystems already served are listed
first and greyed out rather than silently omitted — the question "why isn't Java here" should be
answered by the table, not by its absence.

| | Ecosystem size | Test-framework adapters | Capture feasibility | Verdict |
|---|---|---|---|---|
| *.NET* | *Mid-large; C# won TIOBE 2025* | *14, all built* | *41 modules, built* | *the original — not a candidate* |
| *Java/JVM* | *Top tier* | *JUnit5, TestNG, Cucumber, Spock — built* | *33 modules, capture half incomplete* | ***in flight, pre-1.0** — Kronikol4J 0.1.25-SNAPSHOT. Covers Kotlin, Scala and Groovy on the same adapters. §2 applies to it directly.* |
| **Python** | Biggest mover, SO 2025 (+7pp) | **1** — pytest near-monopoly | monkeypatching trivial; OTel mature for the tail | **Best return** |
| **JS/TS** | **Largest** — 66%, #1 since 2011 | 3+ — Playwright (~45% E2E), Vitest, Jest, Cucumber.js | module interception; good | Biggest pool, worse ratio |
| **Go** | Mid, high growth | **1** — stdlib `testing` | **the blocker** — see below | Best fit, worst mechanics |
| Ruby | Small | RSpec + Cucumber | monkeypatching trivial | Strong BDD culture, small pool |
| PHP | Mid | PHPUnit | OTel zero-code exists | Weak test-reporting culture |
| **Android (Kotlin, Java)** | Top tier: the larger mobile platform | JUnit 4 under `AndroidJUnitRunner` for instrumented tests, JUnit 5 for JVM unit tests, Kotest | OkHttp interceptor exists in Kronikol4J; no JDBC, so Room's query callback is the SQL seam; ART lacks `java.net.http` and runs no agents; the port has no NDJSON writer | **The Java port's problem, after F1 and F9.** `MOBILE_PLAN.md` M3 |
| **Swift / iOS** | Top tier: the other mobile platform | XCTest (observation and activities), Swift Testing (traits) | `URLProtocol` on `URLSession` captures bodies; `sqlite3_trace_v2` and GRDB for SQL; Core Data and SwiftData have no statement seam; `@TaskLocal` for identity | **A fifth platform, after the shared renderer.** `MOBILE_PLAN.md` M4 |
| **Dart / Flutter** | Mid, growing | `flutter_test`, `integration_test` | `HttpOverrides` and `dio` interceptors; its networking does not ride the native stacks, so M3 and M4 do not cover it | **Not planned.** Served at the edge by `MOBILE_PLAN.md` M1 only |
| *C/C++* | *Top tier by size* | *GoogleTest, loosely* | *no runtime instrumentation; no OTel zero-code* | *not a candidate — §3.2* |
| *Rust* | *Small but growing fast* | *stdlib `#[test]`* | *no runtime instrumentation; no OTel zero-code* | *not a candidate — §3.2* |

**Java is the answer to its own question: it is the port already in flight.** One adapter set there
also reaches Kotlin, Scala and Groovy, which is part of why it was the right first port and why no
separate JVM-language entry is needed.

**But "in flight" is doing work in that sentence.** Kronikol4J is 0.1.25-SNAPSHOT with the capture
half still incomplete, so **§2's scoping argument applies to Java first and hardest** — it is not
advice reserved for a third language. The immediate question is not "which language next" but
"what is in Kronikol4J 1.0": finish 33 capture modules, or declare core + HTTP + JDBC the supported
surface and mark the rest experimental. §4 sequences accordingly.

**Python.** One adapter reaches essentially the whole ecosystem — against fourteen on .NET (xUnit2,
xUnit3, NUnit4, MSTest, TUnit, ReqNRoll ×4, LightBDD ×4, BDDfy). Monkeypatching makes body capture
straightforward, which §1 says is the thing that matters. Backend/API test culture (FastAPI, Django)
matches what the report shows. Lowest cost per unit of audience by a clear margin.

**JS/TS.** The largest pool, but fragmented test frameworks mean three or more adapters, and the
testing centre of gravity is browser E2E — where a *service-interaction* sequence diagram is a weaker
differentiator than it is for backend component tests. Second, not first.

**Go.** Conceptually the best fit in the table: microservices, integration tests against real
dependencies, and a single stdlib test framework. But *"Go's direct compilation to machine code makes
traditional auto-instrumentation approaches difficult… it is impossible to add additional code at
runtime to instrument Go applications."* Capture means explicit wrapping (`http.RoundTripper`, a
`database/sql` driver wrapper) — which is idiomatic in Go but puts the burden on the user's code, a
different product. The eBPF route needs Linux and elevated privileges, awkward inside a test process,
and reportedly shows *"generic DB operations rather than detailed statements"* — §1's no-body failure
in its sharpest form. **Defer until the instrumentation story changes.**

**Mobile (added 2026-09-22).** The three rows above were missing, so the table could not answer why
iOS and Android were absent. The answer is in `MOBILE_PLAN.md`: every mobile platform is served first
*at the edge*, with no code in the app, by the taps, a HAR importer and converters for the files the
mobile runners already write (its M1, before the launch); Android in-app capture is then the Java
port's work, because its adapters are JVM-level and its OkHttp interceptor already exists (M3); iOS is
a new Swift capturer and the first port outside the four platforms the foundations plan names (M4);
Flutter is a Dart capturer nobody has asked for. React Native needs no row: its networking rides
OkHttp and `NSURLSession`, so M3 and M4 cover it. The ranking's rule holds for all three: no port
before the shared renderer, so that none writes a rendering half.

### 3.2 Why C/C++ and Rust are not candidates despite the size

C and C++ are top-tier by any popularity measure, so their absence needs a reason rather than an
oversight. Three, compounding:

1. **No runtime instrumentation, and worse than Go's version of that problem.** Capture would mean
   link-time interposition or making the user wrap their own calls. Go at least has one HTTP client
   and one `database/sql`; C++ has libcurl, cpp-httplib, Boost.Beast, Poco and more, with no common
   seam to hook.
2. **No OTel zero-code path.** OTel lists zero-code instrumentation for *"Go, .NET, PHP, Python,
   Java and JavaScript"* — C++ and Rust are not on it. So §1.3's tail-coverage fallback is
   unavailable too: you would write every adapter by hand, in the language where that is hardest.
3. **The test culture does not match the product.** Kronikol reports component tests of a service
   against real downstream dependencies. C/C++ testing is overwhelmingly unit-level; the systems that
   do integration-test against HTTP and SQL in bulk are mostly in the managed languages above.

Rust fails on (1) and (2) identically — no runtime instrumentation, no OTel zero-code — and its
test culture is likewise unit-heavy. Its HTTP and DB landscape is more consolidated than C++'s
(`reqwest`, `sqlx`), so it is the better of the two if this is ever revisited, but the ecosystem is
small and the mechanics are still against it.

**The pattern across the whole table: capture feasibility tracks whether the runtime lets you
intervene without the user changing their code.** That is the axis that separates Python and JS/TS
from Go, C++ and Rust — and §1 is why it cannot be worked around with OTel.

### 3.3 Competition

Allure is the incumbent in every one of these ecosystems (400K+ weekly npm downloads for its
Playwright package alone). It reports *test outcomes*. It does not capture service interactions or
render them as sequence diagrams. **That gap is the pitch, it is equally open everywhere, and it does
not move the ranking** — it only means the pitch must lead with the differentiator rather than with
"another test reporter".

---

## 4. Recommendation

> **Scoped by §0.** "Which language next" was already answered in practice: `NODE_PORT_PLAN.md` is a
> 1,250-line design with twenty-two completed deep dives and locked decisions, in design-complete
> state with no code written. **This section does not propose displacing it.** Node has a plan;
> Python does not. What follows is a recommendation about what comes *after* Node, and about what
> must be settled before either.

**After Node, Python — scoped to §2: core + HTTP + SQL + pytest, bodies captured properly, OTel for
the tail.** §3 is the case: pytest's near-monopoly means one adapter where Node needs six (Vitest,
Jest, node:test, Mocha, Cucumber-js, Playwright — its own locked list), and Python's capture story is
the easier of the two (monkeypatching, versus Node's *"no universal seam"* for HTTP and a per-driver
SQL layer with a Prisma exception).

**That ordering is worth stating explicitly because it is not obvious**: Node is the larger ecosystem
and the further-advanced plan, but Python is the cheaper port. Both are true; the plan that exists
wins on sequencing, not on ratio.

**And neither is the next task.** Kronikol4J is pre-1.0 with its capture half incomplete, so two
decisions sit ahead of both:

1. **`KRONIKOL4J_PORTABILITY_PLAN.md` E1 — does the shared wasm renderer work?** If it does, neither
   Kronikol4J nor a Python port writes a rendering half, and 45% of a port's cost stops existing.
   Pre-1.0 is the cheapest moment to find out, because adopting it now *deletes* Java rendering code
   rather than *replacing a shipped renderer*.
2. **What is in Kronikol4J 1.0?** §2 says core + HTTP + JDBC is the 80/20. Deciding that before
   racing to finish 30 more capture modules is worth more than any new language, because whatever
   scope Java sets becomes the template every later port copies — including the mistake, if it is
   one.

**Starting any new port before either answer is how the 14,305 rendering lines and a 33-module
capture surface both get paid for twice** — and with `NODE_PORT_PLAN.md` design-complete but
unwritten, that risk is live now rather than hypothetical. If E1 succeeds, Kronikol.js should never
write `@kronikol/report` or `@kronikol/diagram` at all.

Python is the right language after Node; neither is the right next task.

---

## 5. Assumption ledger

| # | Claim | Status |
|---|---|---|
| B1 | OTel semantic conventions capture body *sizes*, not bodies, for architectural reasons | **sourced** — semconv issues #857, #1219, both open |
| B2 | Payloads are ~90% of a report; a body is usually the answer | **measured** — the skill's own layer table, written from real reports |
| B3 | `InteractionRecord.Content` is the body and is the capturer's responsibility | **measured** — the field's own doc comment |
| B4 | `Kronikol.Extensions.OpenTelemetry` feeds internal-flow diagrams, not interaction capture | **measured** — its doc comment |
| B5 | .NET ships 41 capture extensions and 14 test-framework adapters | **measured** — directory count |
| B6 | core+http+jdbc ≈ 31% of Kronikol4J's capture half | **measured** — module line counts |
| B7 | A 5–6k-line first-cut port is sufficient to be useful | **reasoned, not validated.** The 80/20 claim rests on HTTP and SQL dominating real usage; no telemetry backs it. **Falsifier: ask three existing users which extensions they actually enable.** |
| B8 | pytest is a near-monopoly in Python testing | **sourced, not measured** — "pytest leads the Python category"; no share figure found |
| B9 | JS/TS test frameworks are fragmented enough to need 3+ adapters | **sourced** — Playwright ~45% E2E, Vitest for unit, Jest and Cucumber.js beside them |
| B10 | Go cannot be instrumented at runtime; eBPF loses statement detail | **sourced** — OTel Go docs and Dash0's eBPF guide |
| B11 | Allure does not capture service interactions or render them as diagrams | **reasoned from its documented feature set**, not verified by using it |
| B12 | OTel zero-code covers Go, .NET, PHP, Python, Java, JavaScript — and **not** C++ or Rust | **sourced** — OTel's own zero-code language list |
| B13 | Java/JVM is not a candidate because Kronikol4J already serves it, Kotlin/Scala/Groovy included | **measured** — the port exists; its adapters are JVM-level, not Java-language-level |
| B14 | C/C++ and Rust test cultures are unit-heavy rather than integration-heavy | **asserted from general knowledge, not sourced.** Weakest row here. It is the third of three reasons in §3.2, so the conclusion survives without it — but do not cite it alone. |
| B15 | A scoped port is ~5,600 lines; `core` does not shrink when the renderer is shared | **measured** — module and per-file line counts; `core`'s contents inspected (serializer, SQL classifier, tracking log, correlation, naming — all capture-side) |
| B16 | Node's scoped port is materially larger than Java's or Python's | **sourced from `NODE_PORT_PLAN.md`** (no JDBC equivalent, no universal HTTP seam, Prisma exception, six adapters). The *direction* is well evidenced; **the magnitude is not estimated anywhere** |
| B17 | §2.1's "6–9k lines-equivalent per platform" | **an estimate, not a measurement.** Built from B15 plus B16's direction. Python is the defensible end; the Node figure is a guess with a reason behind it. **Do not plan capacity from this number** — plan from it that Node > Python, and re-estimate when Kronikol.js has real code. |
| B18 | Capture maintenance stays at N platforms while rendering goes to 1 | **reasoned, and the reasoning is short:** capture tracks third-party library releases, which no shared artifact can absorb. High confidence, no measurement possible until more than one port is live. |
| B19 | Android's HTTP seam is OkHttp, which Kronikol4J's interceptor already covers, and its SQL seam is Room's query callback, not JDBC | **measured** for the interceptor (the class exists); **assumed** for Room's callback (`MOBILE_PLAN.md` A11) |
| B20 | iOS bodies are capturable through `URLProtocol` on `URLSession`; Core Data and SwiftData expose no statement seam | **asserted from general knowledge**, not checked against current Apple documentation |
| B21 | React Native's networking rides OkHttp and `NSURLSession`, so the native capturers cover it; Flutter's does not | **assumed** (`MOBILE_PLAN.md` A12) |

**B7 is the one to attack**, because the entire scoping argument rests on it and it is the only
headline claim here with no evidence behind it.

---

## 6. What this plan does not do

- **It does not propose building capture on OTel.** §1 — that was proposed in conversation and is
  withdrawn on the evidence.
- **It does not propose a Go port.** §3 — deferred on instrumentation, not on audience.
- **It does not propose starting any port before E1.** §4.
- **It does not revisit the rendering half.** `KRONIKOL4J_PORTABILITY_PLAN.md` owns that.
- **It does not propose reaching .NET's 41-module capture surface in any other language.** §2 — that
  is the maximal version, not the target.

**Sources:** [OTel semconv #857](https://github.com/open-telemetry/semantic-conventions/issues/857) ·
[#1219](https://github.com/open-telemetry/semantic-conventions/issues/1219) ·
[OTel Language APIs & SDKs](https://opentelemetry.io/docs/languages/) ·
[OTel Go Auto SDK](https://opentelemetry.io/docs/zero-code/go/autosdk/) ·
[Dash0 — OTel Go eBPF](https://www.dash0.com/guides/opentelemetry-go-ebpf-instrumentation) ·
[Stack Overflow 2025 Technology](https://survey.stackoverflow.co/2025/technology) ·
[QASkills — Best Test Automation Frameworks 2026](https://qaskills.sh/blog/best-test-automation-frameworks-2026) ·
[Allure Report](https://allurereport.org/)
