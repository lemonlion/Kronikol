# KRONIKOL4J_PORTABILITY_PLAN.md — how much of the Java port has to stay Java

**Date:** 2026-09-13 · **.NET repo:** 3.3.0 · **Kronikol4J:** 0.1.25-SNAPSHOT
· **Status: investigation complete, nothing implemented, NOT green-lit.**

> **Partly superseded 2026-09-14 by `PLATFORM_FOUNDATIONS_PLAN.md`** (the definitive four-platform plan).
> Its §5 says what survives from here: the §1–§4 measurements, E1/E2 (now F6, run against the
> post-F5 ingest pipeline rather than `Kronikol.Reports` as it stands), and §8.1's syscall floor.
> "Run E1 before 1.0" becomes "run F1–F6 before 1.0".


**Third of four related plans.** `QUERY_FALLBACK_PLAN.md` covers the .NET fallback;
`QUERY_PORTABILITY_PLAN.md` covers shipping `kronikol query` to Java and Node. This one asks the
larger question behind both: **how much of Kronikol4J must be reimplemented and then maintained in
lockstep forever — and can a shared wasm module take any of that away?** Kronikol4J is pre-1.0, so
this is a question about what it ships as, not a retrospective on something already out.

**The answer, in three numbers.** Kronikol4J is **32,100 lines** of main Java, and it splits three
ways rather than two:

| | Lines | Share | Can it be avoided? |
|---|---|---|---|
| **Rendering** — `-report`, `-diagram` | 14,305 | 45% | **Yes** — a shared wasm module retires it (this plan) |
| **Capture tail** — 30 modules beyond core/HTTP/JDBC | 12,324 | 38% | **Partly** — one OTel bridge can cover it at reduced fidelity (no bodies) |
| **Capture core** — `-core`, `-http`, `-jdbc` | 5,471 | **17%** | **No.** This is the irreducible part |

**Only 17% is genuinely irreducible.** An earlier draft of this header said the whole 55% capture
half "is irreducibly Java", which overstated it by a factor of three. What is true is narrower:
**capture with bodies** must be Java, because bodies are ~90% of a report and OpenTelemetry does not
carry them (`NEXT_LANGUAGE_PLAN.md` §1). The 38% tail was a choice to match .NET module-for-module,
and it has a cheaper substitute.

**So the two worries split in opposite directions.** Reimplementation — getting the port
feature-complete — is dominated by the capture side, and wasm does not touch it (though §1.1 and the
table above say that side is smaller than it was made to look). Ongoing maintenance — staying
byte-identical — lands almost entirely in the rendering side, and wasm targets it precisely (§2
measures this rather than asserting it).

**The prize is larger than the Java port.** §4.1 measures the host side: one `.wasm` plus a small
shim per language runs on Node (`node:wasi`, no flag needed on v25.9.0) and Python (`wasmtime-py`,
verified working) as readily as on the JVM. So option B is not a Java tactic — it is the difference
between *one artifact per release* and *one full 14,305-line rendering port per language, forever*.

**Kronikol4J is 0.1.25-SNAPSHOT — pre-1.0 and experimental.** That is load-bearing throughout and was
under-weighted in the first draft of §6. It means there is no compatibility obligation, no users to
break, and no maturity mismatch in an experimental library depending on an experimental workload. It
also means this is the cheapest moment the decision will ever be: adopting the shared renderer now
*deletes* 14,305 lines, where adopting it after 1.0 *replaces a shipped renderer*.

**Recommendation: run E1 before 1.0 and let its result choose the 1.0 architecture (§6, §7).** Not a
side experiment beside a release — a scoping question for the release itself. If E1 works, most of
the ledger burden in §2 never starts; if it fails, the cost was days.

---

## 1. The split, measured

`find <module>/src/main -name '*.java' | xargs cat | wc -l`, all 35 published modules:

| Group | Modules | Lines | Share |
|---|---|---|---|
| **Rendering** | `kronikol4j-report` (11,553), `kronikol4j-diagram` (2,752) | **14,305** | **45%** |
| Capture / integration / glue | the other 33 | 17,795 | 55% |
| | | **32,100** | |

The top of the capture half, for scale: `kronikol4j-core` 3,600, `-azure` 1,413, `-aws` 1,369,
`-mongodb` 1,172, `-gcp` 1,067, `-jdbc` 1,013, `-http` 858, `-messaging` 851.

**Nothing in that 55% can be *shared* by any mechanism in this document.** Instrumenting a JDBC
driver or a Spring filter chain means being inside the JVM, holding JVM objects. A wasm module cannot
do it, a native binary cannot do it, and IKVM cannot do it in the useful direction. It is also — per
the 2026-06-27 audit — the half that is still substantially incomplete.

**But "cannot be shared" is not "cannot be reduced", and an earlier draft of this paragraph conflated
them.** §1.1 and §1.2 separate the two.

### 1.1 "Permanent floor" is right in direction, overstated in size

**Amended 2026-09-13 — see `NEXT_LANGUAGE_PLAN.md` §1.4 and §2.** The paragraph above is correct that
the capture half cannot be *shared*: no mechanism in this document reaches inside a JVM. It is wrong
to imply the *volume* is fixed at 17,795 lines.

Kronikol4J reproduced .NET's maximal surface — 33 capture modules against .NET's 41 — but the 80/20
is much smaller:

```
kronikol4j-core   3,600
kronikol4j-http     858
kronikol4j-jdbc   1,013
                 ------
                  5,471   ≈ 31% of the capture half
```

Everything beyond core, HTTP and SQL was a choice to match .NET module-for-module, not a floor. A
port scoped to those three plus one test-framework adapter is a third of the size.

**And because Kronikol4J is pre-1.0 with the capture half still incomplete, this is a live decision
for *this* port, not advice for a hypothetical later one.** The 17,795 lines are written, but the
remaining work is not, and the audit says the capture side is where it sits. Scoping 1.0 to core +
HTTP + JDBC as the *supported* surface — marking the other 30 modules experimental rather than
racing to finish them — is available now and gets cheaper the earlier it is taken.

**Note for anyone reading §5's "one full port per language, forever":** the per-language rendering
cost (14,305) is real and is what option B retires. The per-language *capture* cost is nearer 5,500
than 17,795, so B's advantage over A is in the rendering column, which is where this plan already
put it.

### 1.2 The two mechanisms that reduce the capture half

Neither *shares* code. Both cut how much has to be written.

**(a) Scope — the 80/20.** §1.1: core + HTTP + JDBC is 17% of the port. Everything beyond it was a
choice to mirror .NET's 41 modules, not a requirement.

**(b) OpenTelemetry, for the tail only.** One OTLP→NDJSON bridge in place of 30 bespoke adapters.
`NEXT_LANGUAGE_PLAN.md` §1.3 sets the terms, and they are strict:

- OTel auto-instrumentation covers *"HTTP servers and clients, most popular database drivers, and
  common messaging libraries"* — the shape of the tail almost exactly.
- **It cannot carry request or response bodies.** The semantic conventions capture *sizes*; the spec
  issues have been open for years and the reason is architectural (the client span closes before the
  body is readable). Bodies are ~90% of a report.
- So the tail arrives **degraded — topology without payloads.** The report already has vocabulary for
  degraded records, so this fails legibly rather than silently.

**That is a real reduction with a real price, and the price is the right way round:** full fidelity
where it matters (HTTP and SQL, hand-written, with bodies), degraded coverage where it does not
(Bigtable, Cassandra, EventHubs, Spanner — present in the diagram, thin in the notes).

**What it does not do is let OTel replace the capture core.** That was proposed in conversation on
2026-09-13 and withdrawn the same day on the body evidence. Anyone revisiting it should read
`NEXT_LANGUAGE_PLAN.md` §1.1 first.

---

## 2. Where maintenance has actually landed, from the ledger

`Kronikol4J/README.md` carries 13 `.NET x.y.z` divergence entries. Classified by reading each one in
full rather than by keyword:

**Created re-port work (7):**

| Entry | What | Half |
|---|---|---|
| 3.0.45 (L42) | render assets — "porting the render side means copying the two scripts + the worker host" | render |
| 3.0.83 (L82) | component diagram emitter **and** SQL classifier — "both of which change generated bytes" | render + capture |
| 3.0.84 (L96) | note payload formatter — "changes generated bytes for every non-JSON request body" | render |
| 3.0.85 (L108) | diagram output on several width axes — "a port matching diagram output must carry all of it" | render |
| 3.0.86 (L142) | note line-break markers — "six sites" | render |
| 3.1.0 (L176) | `TestRunReport.schema.json`, pinned byte-for-byte by `ReportDataSchema.java` | schema |
| 3.1.0 (L242) | `ciMetadata.runAttempt` — "a report-output divergence" | report data |

**No obligation (6):** OTLP span attribute (L235, no OTLP module here), the agent skill (L251),
`query --json` (L259), two optional outputs off by default (L265), `Failures.md` ordering (L282,
.NET-only output), the 3.2.0 failures-digest rewrite (L290).

**The finding: 5 of the 7 work-creating entries are rendering detail** — note formatting, diagram
widths, line-break markers, render assets. The remaining two are the report *data contract*, which
sits on the far side of an ingest-shaped boundary anyway: if a shared module writes the report, the
schema is satisfied by construction and `runAttempt` becomes a field passed through rather than a
key to re-implement and re-pin.

So the honest version of the prize is: **a shared renderer would have eliminated five of these
outright and reduced the other two to plumbing.** Against six entries that cost nothing either way.

---

## 3. Why the boundary is unusually clean

Two facts make this more tractable than it looks:

**The on-ramp already exists and is documented as such.** `kronikol ingest`'s own summary:
*"replay NDJSON interaction captures (and an optional tests file) into a full Kronikol report. The
cross-language on-ramp — any capturer that writes `InteractionRecord` lines can produce
`TestRunReport.html`."* The .NET side was already built to accept captures from another language.
Kronikol4J already pins the contract (`ReportDataSchema.java`).

**The rendering contract is text in, text out.** `kronikol4j-diagram` has **no PlantUML
dependency** — it emits PlantUML *source*, and diagrams are rendered client-side in the browser from
the engine the HTML carries. So a shared module would take captured JSON and emit HTML and `.puml`
text. No image encoding, no font resolution, no native calls, no filesystem beyond read-in/write-out.
That is the best-behaved workload wasm has, and it is why this is worth measuring at all.

**`parity-harness/dotnet-capture` already crosses the boundary.** It is a .NET project
(`kronikol-capture`) whose job is producing reference captures for the Java side to match. It is the
natural home for §7's experiment — the plumbing is built.

---

## 4. What `wasi-experimental` is, precisely

> **CORRECTED BY MEASUREMENT 2026-09-14 — see `PLATFORM_FOUNDATIONS_PLAN.md` §11.** This section, §4.2 and
> §4.3 assumed .NET emits a **Preview 1** module. It does not: .NET 10.0.300's workload hard-codes
> `wasm32-wasip2` and ships a prebuilt Mono runtime as a **Preview 2 component** (header `0d 00 01 00`).
> There is no P1 output to "adapt up" from. Consequences: `node:wasi` and `wasmtime-py` (A12/A13) cannot load
> it; **Chicory is P1-only and cannot host it (A7 is FALSE)**; jco's preview2 shim boots Mono but fails in
> assembly loading; **wasmtime 48 runs it** up to a real blocker — Mono/WASI has no
> `System.Security.Cryptography`, hit at `ScenarioStableId.cs:51` and `InteractionRecord.cs` (two call sites; a
> managed-hash fallback is the fix). §4.4's JVM-portability argument for wasm over a native binary is
> weakened (Java needs the JNI `wasmtime-java`, component support unverified) but not reversed.


A .NET SDK workload that compiles a .NET app to a **WASI Preview 1** module: wasm that runs outside a
browser, with a POSIX-shaped interface for args, stdout and preopened directories. Your IL ships
alongside a Mono runtime compiled to wasm. Any WASI host runs it — wasmtime, `node:wasi`, or
**Chicory**, a pure-Java wasm runtime that adds no native dependency to a JVM library.

**Measured status signal:** SDK 10.0.300 lists `wasi-experimental`, `wasi-experimental-net8` **and**
`wasi-experimental-net9`. It has ridden through three .NET releases keeping the word in its name.
That is the single most important fact about it for a library you are about to ship.

### 4.1 The host side is in better shape than the producer side — measured

The producer (`wasi-experimental`) is the experimental half. The **consumer** half is not, and this
was measured rather than assumed:

| Host | Mechanism | Result |
|---|---|---|
| **Node** | `node:wasi`, built in | **v25.9.0 imports it with no flag.** Constructs fine; emits only an `ExperimentalWarning`. |
| **Python** | `wasmtime-py` (Bytecode Alliance) | **pip-installs on 3.14/Windows.** `WasiConfig` + `preopen_dir` + `inherit_stdout` + `Linker.define_wasi()` all construct. |
| **Java** | Chicory (pure Java) or wasmtime-java | **not verified** — A7 |

Node needing no flag was the surprise; P1 support there is mature enough not to be gated any more.

**This widens the plan's scope beyond the Java port.** One `.wasm` plus a small host shim per
language reaches Node, Python and Java — and Go, Rust and C# hosts exist too (wazero, wasmtime,
wasmer). That is leverage a per-platform native binary cannot match: a RID matrix reaches five
platforms chosen in advance, a wasm module reaches every host anyone ever writes. §5's option B
should be read as "a shared renderer for every non-.NET language", not "a Java tactic".

### 4.2 Preview 1 is the right tier, and should be chosen deliberately

An earlier draft of this reasoning warned that P1 is a frozen spec and that targeting it means
forgoing sockets, threads and the Preview 2 / Component Model tooling. **That warning was aimed at
the wrong workload and is withdrawn.** This renderer reads a JSON and writes HTML and `.puml` text.
It needs no sockets, no threads and no clocks. P1's capability ceiling does not bind it anywhere.

Two things are true instead:

- **A P1→P2 migration would be cheap and invisible to consumers.** The engine code is ABI-insensitive
  — `File.ReadAllText`, `File.WriteAllText`, stdout — so moving would be a build-target change on the
  .NET side, not a rewrite. A Java developer calling `report.generate(...)` never learns which
  preview produced the bytes. The cost that *is* real lands on the per-language host shims, which are
  small, and which this project controls.
- **You would be gated by the slowest host, and the slowest hosts are P1-only today.** Measured on
  the wasmtime-py installed for §4.1: `component APIs: NONE`, `WASI APIs: ['WasiConfig']`. Wasmtime
  the engine supports components; the Python binding exposes none of them. So Python is P1-only
  regardless of what .NET offers.

**That argues *for* P1, not against it.** When a design depends on three independently-maintained
host implementations, the frozen, universally-implemented ABI is the one that keeps them from
drifting apart. Hosts will optimise P1 for as long as everything targets it, which is the substance
of the "improves over time without my help" thesis — and that thesis holds.

The only residual is a forced migration if hosts eventually drop P1. That is years out, would be well
telegraphed, and costs a handful of shim rewrites. It is not a reason to avoid the approach, and it
should not be recorded as one.

### 4.3 Mixing P1 and P2 hosts — the fork is a packaging step

The worry this answers: *with three or more hosts, will one laggard hold the rest back?*

**No, because the Component Model wraps core modules rather than replacing them.** A P2 component
*contains* a core module plus an interface layer, so one P1 build serves both tiers. Verified against
jco 1.33.0, installed and inspected:

```
jco new [options] <core-module>
  create a WebAssembly component adapted from a component core Wasm
  --adapt <[NAME=]adapter...>  component adapters to apply
  --wasi-command               build with the WASI Command adapter
```

So the pipeline forks after the .NET build, not inside it:

```
dotnet publish  →  renderer.wasm                    (P1 core module)
                       ├─ ship as-is                → Chicory, node:wasi, wasmtime-py
                       └─ jco new --wasi-command    → renderer.component.wasm
                                                      → jco transpile → JS, or wasmtime components
```

One compile, two packagings, same bytes underneath. Note the direction: **P1→P2 adaptation is the
supported path and P2→P1 is not**, so P1 is the correct base — which is also all the .NET workload
offers, so nothing is being settled for.

Three reasons the outlier risk is structurally weak, in increasing order of importance:

1. **The fork is post-build.** Not a second target, not a source `#if`, not a second codebase.
2. **Every shim is owned here anyway.** Each host needs its own small glue layer regardless; whether
   one loads a core module and another loads a component is a detail inside code this project already
   maintains. No shared abstraction has to move in lockstep.
3. **P2 support is additive, not a migration.** Nobody has to leave a tier for someone else to
   advance. That is the actual answer to the question: you add a lane, you never move everyone.

Costs of dual-shipping, honestly: two artifacts to build, version and publish; the conformance corpus
runs twice (same corpus, two runners); the adapter becomes a build-chain dependency. Mechanical and
bounded, not architectural.

**Ship P1 only at first** — not because of the outlier worry, which this defuses, but because it is
the smaller surface and the P2 lane can be added later with nothing to undo.

**Unverified:** jco advertises the adapter and the binary was inspected directly, but it has not been
run against a .NET-produced module. That the adapter works on Mono-wasm output is an assumption until
E1 produces one.

### 4.4 Why wasm, not the native binary, for the Java lane

`QUERY_PORTABILITY_PLAN.md` §3.4 proposed per-platform NativeAOT binaries behind a Maven classifier.
For a *tool* that is right. For a *library* it is a portability regression: a JVM library is expected
to run wherever the JVM runs — s390x, AIX, Alpine/musl, ARM32 — and a five-RID matrix covers none of
those tails. A user on an unlisted platform gets a hard failure from a test-reporting library, which
is the worst possible place to meet one.

Chicory-hosted wasm is pure Java and runs everywhere the JVM does. **For Kronikol4J the ranking
inverts: wasm over native binary.** `QUERY_PORTABILITY_PLAN.md` §3.4 now carries that amendment,
narrowed there to the distinction that matters: the portability argument kills a native binary inside
`kronikol4j-core`, but `kronikol4j-cli` is a tool someone runs deliberately, so the binary stands for
the CLI.

For Node the binary remains defensible — npm has no write-once-run-anywhere promise to break, and the
esbuild pattern is well trodden. But §4.1 weakens the case: if one module already serves Java and
Python, adding Node to it costs a shim, while the binary costs a fifth of a release-time RID matrix
forever. That trade should be re-argued once E1 (§7) says whether the module exists at all.

---

## 5. The three options

| | What | Buys | Costs |
|---|---|---|---|
| **A. Status quo** | Keep the Java renderer; keep the ledger | No new risk; full JVM portability; no new dependency | 14,305 lines maintained in lockstep; ~5 re-ports per the history in §2 |
| **B. Shared wasm renderer** | `kronikol4j-report`/`-diagram` delegate to a `.wasm` built from .NET, hosted by Chicory — **and the same module serves Node, Python and any future language** (§4.1) | Retires 45% of the port and most of the parity ledger; **one artifact per release instead of one port per language** | Experimental workload in a shipped artifact; multi-MB; unmeasured speed; threading question (§8) |
| **C. Native binary** | Maven classifier per platform | Same retirement as B, better speed | **Breaks the JVM portability promise** (§4.4). Rejected for the library. |

C is rejected. A versus B is a real decision, and §7 is how to make it with evidence.

**B's value grew after §4.1 was measured.** Written as a Java tactic, it traded an experimental
dependency for 45% of one port. Read as "the shared renderer for every non-.NET language", it is the
difference between *one artifact per release* and *one full port per language, forever* — and the
second Kronikol port would pay the 14,305-line rendering cost again from zero. If a Node or Python
Kronikol is ever plausible, that changes A-versus-B materially, and it should be decided with that
on the table rather than after someone starts the second port.

---

## 6. Recommendation

> **REVISED 2026-09-13.** The first version of this section read *"Ship on A. Run the experiment for
> B beside it, not before it,"* and justified it with *"B puts an experimental Microsoft workload
> inside the artifact a user's build depends on."* **That justification does not survive the fact
> that Kronikol4J is 0.1.25-SNAPSHOT — pre-1.0 and experimental.** The objection was a maturity
> *mismatch* between a stable library and an experimental dependency, and there is no mismatch. The
> superseded reasoning is kept below the line for the record.

**Run E1 before 1.0, and let its result choose the 1.0 architecture.**

Three things change once the port is understood as pre-release rather than shipped:

1. **No compatibility obligation, no users to break.** An experimental library may depend on an
   experimental workload; the risk tiers match. Post-1.0 they would not.
2. **This is the cheapest moment it will ever be.** Adopting B before 1.0 means *deleting* 14,305
   rendering lines. Adopting it after means *replacing a shipped renderer* — a migration, a
   behaviour change for users, and a ledger entry of its own.
3. **The remaining work is capture, and §1.1 says that work is smaller than it looks.** The port's
   incomplete half is the capture side; scoping 1.0 to core + HTTP + JDBC (≈5,500 lines, `NEXT_LANGUAGE_PLAN.md`
   §2) and marking the rest experimental is a live 1.0 decision, not advice for some future language.

So the sequencing inverts: **E1 is not a side experiment, it is a 1.0 scoping question.** If it
works, B is a serious candidate for the release architecture and most of §2's ledger burden never
starts. If it fails, ship A having lost days rather than a release.

**What still argues against B, and it is not nothing.** E1 is unverified (A9). Chicory's speed is
unmeasured (A6, E2). And if .NET's WASI workload is later withdrawn, a deleted Java renderer has to
be rewritten. **Mitigation: do not delete the Java renderer when B lands — keep it until B has
shipped and baked through at least one .NET release cycle.** That costs a period of double
maintenance, and it is the right price for a reversible decision.

---

<details>
<summary>Superseded reasoning (pre-2026-09-13), kept for the record</summary>

The ledger is why. Seven work-creating entries across the whole recorded history is a real cost but a
*bounded* one, paid per .NET release rather than continuously — and the port's release is not gated
on solving it. Against that, B puts an experimental Microsoft workload inside the artifact a user's
build depends on, and if it is withdrawn or stalls you are re-porting 14,305 lines under time
pressure instead of at leisure.

But do not let the question go unanswered, because §2 says the prize is most of the maintenance
burden you are worried about, and §3 says the boundary is the cleanest kind wasm handles.

</details>

---

## 7. The experiment — two measurements that decide the 1.0 architecture

Both run in `parity-harness`, which already has the .NET capture side (§3). **Run them before 1.0**:
§6 explains why this is a scoping question for the release rather than a side project beside it.

**E1. Does it render correctly at all?** Publish `Kronikol.Reports` to a WASI module that reads a
captured NDJSON and writes HTML + `.puml`. Run the existing parity corpus through it and diff against
the .NET goldens. Byte-identical or it is dead — and this is also where §8's threading question gets
answered rather than argued.

**E2. How slow is it under Chicory?** The same corpus, timed: Java-native renderer versus
Chicory-hosted wasm, interpreted and in Chicory's AOT mode. A report generation that takes seconds is
fine; one that takes minutes is not.

**E1 first, and stop if it fails.** E2 only matters if E1 works.

E1's artifact is host-agnostic, so once it exists the Node and Python lanes cost a shim each to
smoke-test — the hosts are already verified (§4.1, A12/A13). Worth doing in the same sitting: it is
what turns "a Java tactic" into the §5 claim about every future language, and it is nearly free once
the module is in hand.

If both land: B becomes a decision with evidence, scheduled at leisure. If WASI is still experimental
a year from now: two days spent, working port kept, nothing lost.

---

## 8. What would block B

- **Experimental across three releases** (§4). The headline risk, and not a technical one. Note the
  asymmetry §4.1 measured: it is the *producer* side that is experimental. The hosts are in better
  shape — Node needs no flag, Python's binding works. So the risk is concentrated in one place,
  Microsoft's, rather than spread across the whole chain.
- **Not** the frozen P1 ABI. §4.2 — that concern was raised in an earlier draft and withdrawn after
  measurement; P1 is the right tier for this workload and for a design depending on three independent
  hosts. Recorded here so it is not re-raised as a blocker.
- **Threading.** [ReportGenerator.cs](src/Kronikol/Reports/ReportGenerator.cs) runs its outputs
  through `Parallel.Invoke` specifically so one failing output cannot lose the rest — the comment on
  `RunOutputs` explains that a partial report beats none. WASI Preview 1 has no threads. TPL degrades
  to sequential so it will *run*, but the isolation semantics that block exists to protect must be
  re-verified under the degraded path, not assumed. E1 is where that happens.
- **Artifact size.** Mono runtime plus the assembly, in every Maven artifact. Unmeasured.
- **Chicory throughput.** Unmeasured, and decisive. E2.
- **Two-sided release coupling.** A shared module means Kronikol4J's output changes when the .NET
  renderer changes, whether or not Kronikol4J cut a release. That is the *point* — but it needs a
  version scheme that says which .NET renderer is inside a given Java artifact, which the ledger has
  no way to express today.

### 8.1 The one design constraint that must be held

**Keep the shared module's syscall surface at WASI Preview 1's floor: read a file, write files,
stdout. Nothing else.**

This is the rule that keeps §4.3 true. The lowest-common-denominator risk across N hosts is not the
ABI — that forks cheaply at packaging time. It is **capability**. The moment the renderer needs a
socket, a thread or a real clock, P1 hosts cannot follow, and the fork stops being a packaging step
and becomes two behaviours, two test matrices, and a feature that exists on some platforms and not
others.

Today this is free: §3 establishes the contract is text in, text out. It stays free only if it is
defended. **The risk is a function of the syscall surface, not of the host count** — hold the surface
and the number of supported languages can grow without limit.

Practical form: E1's corpus run should assert the module imports nothing outside that floor, so a
future change that reaches for a socket fails a test rather than silently stranding a host.

---

## 9. Assumption ledger

| # | Claim | Status |
|---|---|---|
| A1 | 32,100 lines; render half is 14,305 / 45% | **measured**, all 35 modules |
| A2 | 7 of 13 ledger entries created work; 5 of the 7 are rendering | **measured** — each entry read in full, not keyword-matched |
| A3 | `kronikol ingest` is the documented cross-language on-ramp | **measured** — its own summary in `IngestCommand.cs` |
| A4 | `kronikol4j-diagram` has no PlantUML dependency; emits source text | **measured** — no PlantUML in either module's `build.gradle.kts` |
| A5 | `wasi-experimental` present and still named experimental on SDK 10 | **measured** (`dotnet workload search`) |
| A6 | `ReportGenerator` uses `Parallel.Invoke` for output isolation | **measured** |
| A7 | Chicory is pure Java, adds no native dependency, has an AOT mode | **assumed from general knowledge — not verified against the project.** Load-bearing for §4.4. Verify before E2. |
| A12 | Node runs WASI P1 modules with no flag | **measured** — `node:wasi` on v25.9.0 imports and constructs; `ExperimentalWarning` only |
| A13 | Python runs WASI P1 modules | **measured** — `wasmtime-py` pip-installs on 3.14/Windows; `WasiConfig` + `preopen_dir` + `inherit_stdout` + `Linker.define_wasi()` all construct |
| A14 | wasmtime-py exposes no Component Model APIs | **measured** — `component APIs: NONE`, `WASI APIs: ['WasiConfig']`. Wasmtime the engine has components; the Python binding does not surface them. |
| A15 | A P1→P2 migration would be a build-target change, not a rewrite | **reasoned** from the engine's API surface (file read, file write, stdout — no sockets, threads or clocks), not executed. Low stakes: §4.2 concludes P1 is the right tier regardless. |
| A16 | Chicory supports the Component Model | **unknown.** Only matters if P2 is ever wanted for the Java lane; P1 is the recommendation. |
| A8 | WASI Preview 1 has no threads and Mono-on-WASI is single-threaded | **assumed.** E1 settles it in practice. |
| A9 | A WASI build of `Kronikol.Reports` is even buildable | **NOT VERIFIED.** No WASI publish was attempted. E1 is exactly this question and the plan dies there if the answer is no. |
| A10 | Artifact size 8–20 MB | **guessed.** Unmeasured. |
| A11 | The capture half cannot be *shared* by any mechanism | **reasoned, not measured** — but it follows from what instrumentation is. **Amended:** still holds for sharing; does **not** hold as a claim about volume. §1.1 scopes a useful capture half at ~31% of what Kronikol4J built, and `NEXT_LANGUAGE_PLAN.md` §1 rules out the one mechanism that looked like it might shrink it further (OTel cannot carry bodies, and bodies are ~90% of a report). |

**A9 is the gate. A7 is the one I would check first**, because §4.4's whole argument for wasm over a
native binary rests on it.

---

## 10. What this plan does not do

- **It does not propose sharing the capture half.** No mechanism here reaches inside a JVM. But
  "cannot be shared" is not "cannot be reduced": §1.2's two mechanisms — scope, and OTel for the tail
  — take the part that must be hand-written in Java from 55% to **17%**.
- **It does not gate the Java release on any of this.** Recommendation is ship on A.
- **It does not propose IKVM or TeaVM.** `QUERY_PORTABILITY_PLAN.md` §5 costs and rejects the
  Java-canonical inversion; nothing here changes that.
- **It does not touch `Failures.md`/`Failures.jsonl`.** Not generated by the port, and the ledger
  already records that as deliberate.
- **It does not propose a native binary for the Java lane.** §4.4 — rejected on JVM portability.
