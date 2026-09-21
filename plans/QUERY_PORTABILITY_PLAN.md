# QUERY_PORTABILITY_PLAN.md — one query engine, three languages

**Date:** 2026-09-13 · **.NET repo:** 3.3.0 · **Kronikol4J:** 0.1.25-SNAPSHOT
· **Status: investigation complete, nothing implemented, NOT green-lit.**

> **Partly superseded 2026-09-14 by `PLATFORM_FOUNDATIONS_PLAN.md`** (the definitive four-platform plan).
> Its §5 keeps the AOT-readiness measurement, NativeAOT as the fallback for F6, and the query
> conformance corpus (M1); the per-platform binary as the *primary* route is superseded by F7 (query
> as a second entry point on the shared renderer artifact).


**One of four related plans, all independent of each other.** `QUERY_FALLBACK_PLAN.md` answers
"what does a .NET user run when `kronikol` is not installed"; this one answers "what do Java and Node
users run at all"; `KRONIKOL4J_PORTABILITY_PLAN.md` asks the larger question behind both — how much
of the Java port must be reimplemented and then maintained forever, and whether a shared wasm module
can take any of it away; `NEXT_LANGUAGE_PLAN.md` asks which language is worth porting to next and how
big a port has to be. This plan shares a conclusion with the first in §3.5, and §3.4 carries an
amendment from the third.

**The question.** When `kronikol query` is ported to Java and Node, is there a wrapper that avoids
reimplementing it three times — and does TeaVM help?

**The answer.** TeaVM does not help, because it runs the wrong way (§1). The wrapper that does work
is a process boundary: compile the existing .NET engine to a **NativeAOT binary per platform** and
ship it inside the npm and Maven artifacts, the way esbuild and protoc already do. One
implementation, no interop layer, no wasm runtime, no second dialect. The AOT readiness of the
engine is **measured, not assumed** (§2.1): the analyzers pass with 17 warning sites from a single
mechanical cause.

**What this replaces.** Kronikol4J's divergence ledger currently records the query tool as
deliberately unported — "`kronikol4j-cli` ships `Main.java` and `MergeCommand.java` only … Recorded
so the absence is a decision on the ledger rather than a gap someone rediscovers"
(`Kronikol4J/README.md:261-263`). This plan is the proposal to change that decision.

---

## 1. TeaVM: the direction problem

TeaVM consumes **JVM bytecode** and emits JS/WASM. It cannot consume .NET IL. Against a
.NET-canonical engine it does nothing whatsoever.

It helps in exactly one scenario: **Java becomes the canonical implementation.** And that inversion
is not hypothetical in this codebase — both legs already exist:

| Leg | Where | Status |
|---|---|---|
| Java → .NET | `src/Kronikol.PlantUml.Ikvm/` — IKVM 8.15.0, `IkvmReference` on `plantuml-mit-1.2024.6.jar` | in production |
| Java → JS | the browser render workers consume TeaVM-built PlantUML JS | in production (as a **consumed artifact** — this repo does not itself run TeaVM) |

So "write the query engine in Java, IKVM it into .NET, TeaVM it into Node" is a pattern with
precedent here. §5 costs it properly and rejects it, but it is recorded rather than dismissed,
because the precedent makes it a reasonable thing to propose.

---

## 2. Measured evidence

### 2.1 The engine is NativeAOT-ready, to one mechanical fix

```
dotnet build src/Kronikol.Tool -c Release \
  -p:EnableAotAnalyzer=true -p:EnableTrimAnalyzer=true -p:EnableSingleFileAnalyzer=true
```

**Result: `Build succeeded`.** 17 distinct warning sites. **Two warning codes only — IL2026 and
IL3050 — every one of them `JsonSerializer.Serialize`/`Deserialize` without source generation**, in
`Query/PathEngine.cs`, `Query/PayloadReader.cs` and `Query/QueryWriter.cs`.

What is *absent* is the finding. No IL2070/IL2075/IL2090 — **no reflection anywhere in 6,568 lines**.
Corroborated directly: zero `Activator.`, zero `GetType()`, zero `GetProperties()`, and exactly one
`Regex` in the whole engine ([QueryCommand.NumberGrep.cs:17](src/Kronikol.Tool/QueryCommand.NumberGrep.cs#L17)),
a numeric-token matcher that isn't even flagged.

A 6,500-line engine whose entire AOT exposure is one serializer call pattern is an unusually clean
candidate. The fix is a `JsonSerializerContext` source-gen context plus 17 call-site swaps.

### 2.2 The Java lane already has a zero-install launcher

`Kronikol4J/jbang-catalog.json` publishes the alias `kronikol4j` →
`io.github.lemonlion:kronikol4j-cli:0.1.25-SNAPSHOT`, main `io.kronikol.cli.Main`. jbang resolves
from Maven and runs with nothing installed but a JDK.

That is the exact analogue of `dotnet run query.cs` from the sibling plan, **already in place**. It
means the Java lane does not need a new distribution story invented for it — only a new subcommand
behind the alias it already has.

### 2.3 Kronikol4J has a conformance harness already

`Kronikol4J/parity-harness/` is a module. The byte-parity methodology that made report *output*
match is the same methodology §6 proposes for query *output*, so §6 is an extension of existing
practice, not a new discipline.

### 2.4 WASI is in the SDK, and still says experimental

`dotnet workload search` on SDK 10.0.300 lists `wasi-experimental`, `wasi-experimental-net8`,
`wasi-experimental-net9`. The name is the status. §4.

---

## 3. The recommendation: one binary, thin wrappers

### 3.1 Make the engine AOT-clean

A `[JsonSerializable]` context over the `--json` envelope types and the payload records, and the 17
sites in §2.1 switch to the `JsonTypeInfo` overloads. Nothing else changes. This is worth doing on
its own merits — it is a prerequisite for every option in this document including the rejected ones,
and it makes the existing tool trim-clean.

**Do this first and independently.** It is the only step that is certainly not wasted.

### 3.2 Publish `kronikol-query` as a NativeAOT binary per platform

Five RIDs: `win-x64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`. Built in CI on a matrix,
attached to the GitHub release, and republished into the two package ecosystems below.

The binary is the *query* surface only, not the whole tool — `ingest`, `export`, `merge`, `ctrf` and
`init-agents` stay .NET-only, because nothing outside .NET wants them.

### 3.3 Node lane — the esbuild pattern

A wrapper package `kronikol-query` with per-platform `optionalDependencies`
(`kronikol-query-linux-x64`, …), each carrying one binary, and a few lines of JS that resolve the
right one and `spawn` it. npm installs exactly one platform package. This is how esbuild, swc and
`@napi-rs/*` ship; it is boring and well understood, which is the point.

### 3.4 Java lane — Maven classifiers behind the existing jbang alias

> **AMENDED 2026-09-13, and the amendment is narrower than it first looks.** See
> `KRONIKOL4J_PORTABILITY_PLAN.md` §4.1. A **library** that a user's build depends on must run
> wherever the JVM runs — s390x, AIX, Alpine/musl, ARM32 — and a five-RID matrix covers none of those
> tails, so a native binary inside `kronikol4j-core` would be a portability regression. **That
> argument does not apply to `kronikol4j-cli`**, which is a tool someone runs deliberately on a
> machine they are sitting at, exactly like `kronikol` itself. The design below therefore stands for
> the CLI, with one added obligation: **fail with a message that names the platform and points at
> `kronikol query`**, rather than with a missing-file stack trace. If the query surface is ever wanted
> *inside* the library rather than beside it, re-read §4 of this plan and §5 of that one first.

`kronikol4j-cli` gains a `QueryCommand.java` beside its `MergeCommand.java`. It resolves the binary
for the running platform from a Maven artifact with an OS/arch classifier, extracts it to a cached
temp location, and `ProcessBuilder`s it — the protoc-jar pattern.

The payoff is §2.2: `jbang kronikol4j@lemonlion query summary .` then works with no install, which
is the same shape of promise the .NET side makes with `dotnet run query.cs`.

### 3.5 .NET lane — unchanged

`kronikol query` stays as it is, and `QUERY_FALLBACK_PLAN.md`'s emitted `query.cs` stays as it is.
**The binary is not emitted beside the report** — 10–15 MB per reports directory is not a trade
worth making when the .NET machine already has the cache.

The two plans close different holes and the combination closes both: `query.cs` for .NET users with
a warm cache, a shipped binary for Java and Node users with no .NET at all. As a side effect the
binary also answers the one caveat `QUERY_FALLBACK_PLAN.md` could not — a NativeAOT executable has
no SDK floor.

---

## 4. WASI, parked with reasons

One `.wasm`, consumed by Node via `node:wasi` and by Java via Chicory (pure Java, so Kronikol4J
gains no native dependency) or GraalWasm. No RID matrix, one artifact, genuinely attractive.

Parked because:

- The workload is still `wasi-experimental` (§2.4). Shipping a Maven artifact on it is a bet on
  someone else's timeline.
- A mono-wasm module carries a runtime; the artifact is multi-MB before the engine is counted.
- Chicory interprets. Over a 10 MB report that is the wrong end of a large constant factor — see
  A6, this is the number this plan is least sure of and it is the one that decides the option.
- File access goes through preopens, so the "point it at a directory" ergonomics need design work
  that the process boundary gets free.

**Revisit when WASI leaves experimental**, or if the RID matrix in §3.2 turns out to be the
expensive part. The §3.1 AOT work is not wasted either way.

---

## 5. The Java-canonical inversion, costed and rejected

Write the engine once in Java; IKVM it into .NET; TeaVM it into Node. Both legs have precedent here
(§1), and it is the only option under which the answer to "does TeaVM help" is yes.

Rejected on three counts:

1. **It discards 6,568 lines of working, tested .NET code** and moves the maintenance centre of
   gravity onto the port that is substantially less complete than the original.
2. **IKVM in the hot path.** `IKVM.Image.JDK` + `IKVM.Image.JRE` is a heavy dependency to put inside
   a tool whose entire pitch is answering under a byte budget, and whose current `Kronikol.Tool.dll`
   is 398 KB total.
3. **You would be writing to the intersection of two restricted Java dialects** — IKVM 8.15 is Java
   8 level, TeaVM enforces its own subset. Acceptable for a vendored jar nobody edits. A tax on
   every future change to code under active development.

Recorded rather than omitted so that the precedent in §1 does not make this look unconsidered.

---

## 6. What must agree, whichever route wins

Most of the 6,568 lines is presentation — budget trimming, footers, banners, table layout. The part
that has to **agree across languages** is much smaller:

- the addressing scheme (`s3`, `s3/i47`, `b:` content hashes, `stableId`)
- the `--path` JSONPath subset (`$.a.b`, `[*]`, `['a.b']`, `.length()`)
- body hashing, so a `b:` hash means byte-identical in every implementation
- the report index and the ordering scenarios are numbered in

So: a **golden conformance corpus** — `(report, argv) → expected stdout`, as data files in a
directory, runnable by any language. It is what makes a second implementation safe if one ever
happens, it is the acceptance test for the binary route (the Java and Node wrappers must produce
byte-identical output to `kronikol query`), and it extends `parity-harness` (§2.3) rather than
inventing a new practice.

**Build this in M1, before any distribution work**, because it is the thing that tells you whether
any of the rest worked.

---

## 7. Milestones

| M | Work | Where |
|---|---|---|
| **M0** | `JsonSerializerContext` + 17 call sites; turn the three analyzers on permanently in `Kronikol.Tool.csproj` so this cannot regress. **Independently valuable; do regardless of the rest.** | .NET |
| **M1** | Golden conformance corpus (§6) + a runner. Pin it against today's `kronikol query` output. | .NET |
| **M2** | Prove `PublishAot` actually produces a working binary on all five RIDs, and measure size and cold start. **A2 is unverified — this milestone is where the plan can die.** | CI |
| **M3** | Node wrapper package + per-platform packages; conformance corpus green through the wrapper. | Node |
| **M4** | `QueryCommand.java` in `kronikol4j-cli`, Maven classifier artifacts, jbang alias verified; conformance corpus green through the wrapper. | Kronikol4J |
| **M5** | Update the Kronikol4J divergence ledger — the `README.md:261-263` entry saying the query tool was never ported becomes the entry saying how it is now reached. Wiki and changelogs on both sides. | both |

M0 and M1 are worth doing even if M2 fails, because they are the prerequisites for §4 and §6 too.

---

## 8. Assumption ledger

| # | Claim | Status |
|---|---|---|
| A1 | The engine has no reflection; AOT exposure is 17 `JsonSerializer` sites under 2 warning codes | **measured** (§2.1) |
| A2 | `PublishAot` produces a working `kronikol-query` binary | **NOT VERIFIED.** No MSVC linker was found on this machine, so no AOT publish was attempted. The analyzers passing is a strong signal, not a proof. **M2 exists to settle this and it is the plan's main risk.** |
| A3 | Binary size ~10–15 MB per RID | **estimated** from typical NativeAOT console apps. Unmeasured. |
| A4 | Cold start of a NativeAOT binary beats `dotnet run query.cs` | **assumed.** Plausible (no JIT, no restore) but unmeasured. |
| A5 | `wasi-experimental` is still experimental on SDK 10 | **measured** — it is the workload's published name (§2.4) |
| A6 | Chicory is too slow for a 10 MB report | **assumed from general knowledge of interpreted wasm, not measured here.** This single number decides §4. If §3.2's RID matrix proves painful, measure this before re-deciding. |
| A7 | IKVM 8.15 is Java 8 level and TeaVM enforces a subset | **assumed** from the versions in use; not re-verified for this plan. Only load-bearing for §5, which is rejected anyway. |
| A8 | Kronikol4J ships a jbang alias and no query command | **measured** (`jbang-catalog.json`, `README.md:261-263`) |
| A9 | `parity-harness` can host the conformance corpus | **assumed** — it exists as a module; its shape was not inspected |

**A2 and A6 are the two that matter.** A2 gates the recommendation; A6 gates the alternative.

---

## 9. Decisions needed before M2

1. **Does the query binary carry `--json` only, or the full text surface?** Text is what an agent
   should read (the skill is emphatic about this), so: full text surface. Stated here so it is a
   decision rather than a default.
2. **Five RIDs or three?** `osx-x64` is shrinking; `linux-arm64` is growing. Dropping `osx-x64`
   halves the macOS matrix at the cost of Intel Macs.
3. **Version coupling.** The binary is built from .NET Kronikol; Kronikol4J versions independently
   (0.1.x vs 3.x). The Maven artifact needs a version scheme that says *which .NET engine* is inside
   it, or the divergence ledger gets a new kind of entry it cannot express.
4. **Who owns the corpus?** It has to live somewhere both repos can consume. A third repo, a
   submodule, or generated into both — this is the one piece of shared infrastructure the plan
   creates.

---

## 10. What this plan does not do

- **No reimplementation in Java or Node.** That is the whole point; §6's corpus exists so that if
  anyone ever does, it is safe.
- **No TeaVM.** §1 — wrong direction unless §5 is accepted, and §5 is rejected.
- **No WASI today.** §4, parked with a named trigger to revisit.
- **No emitting the binary beside reports.** §3.5.
- **No porting `ingest`/`export`/`merge`/`ctrf`/`init-agents`.** Query is the surface with demand
  outside .NET; the rest is not.
