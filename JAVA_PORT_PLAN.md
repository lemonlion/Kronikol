# Kronikol4J — Java Port Plan

**Project name:** **Kronikol4J** — the Java-native reimplementation of Kronikol, with *full feature parity* as the end state, delivered core-first. The core is engineered from day one as a foundation extensions plug into, so the parity build-out is incremental breadth rather than re-architecture.

**Guiding principle:** Idiomatic Java throughout. We treat the C# code as an executable *specification of behavior*, not a blueprint to translate line-by-line. Where C# and Java diverge (proxies, interceptors, bytecode weaving, async context, test-run lifecycle), we build the idiomatic Java mechanism that produces the same observable result.

### Locked decisions
| Decision | Choice |
|---|---|
| **Name** | **Kronikol4J**. Maven group `io.kronikol`, artifacts `kronikol4j-*`, root package `io.kronikol`. |
| **Java baseline** | **Java 17 floor**, core must not *require* newer. Ship an **optional Java-21 context-propagation module** (`ScopedValue` + structured concurrency) for teams on 21. |
| **Build tool** | **Gradle** multi-module with a `build-logic` convention plugin (shared version/deps/publishing). |
| **Parity scope** | **Functional parity** — track everything the .NET version tracks, organized into idiomatic Java modules (module count differs from .NET; see §2). |
| **C# sync** | **Periodic parity-diff** — .NET keeps evolving; re-capture golden fixtures periodically and diff to catch drift. |
| **Diagram rendering** | **Browser-only.** The server-rendered and IKVM/local rendering paths are **not ported** — all diagram rendering happens client-side in the browser. This *removes* any Java PlantUML runtime dependency (see §3.5). |

---

## 0. How to use this document — status & resumption

*(This section exists so the plan is self-sufficient: anyone — or any fresh session — can resume from this file alone.)*

**What Kronikol is.** A .NET testing/documentation framework that automatically captures real dependency interactions during tests (HTTP, SQL/NoSQL, cache, messaging, cloud SDKs) and generates **interactive HTML reports with PlantUML diagrams** (sequence, component, activity, flame) — deterministic diagrams from actual execution, not AI. Source at `c:\Code\Kronikol` (v3.0.40 at time of writing; multi-targets net8.0/9.0/10.0). Wiki at `../Kronikol.wiki` (89 pages). Demo project: "BreakfastProvider".

**What this document is.** The authoritative plan for **Kronikol4J**, a from-scratch *idiomatic Java* reimplementation targeting **full functional parity**, delivered **core-first**. This file is the source of truth for *intent and design*; the .NET code is the *behavioral specification* (treat it as executable spec, not a line-by-line porting blueprint). Maven group `io.kronikol`, artifacts `kronikol4j-*`, root package `io.kronikol`.

**Status (at time of writing).**
- Design phase **complete**; **no Kronikol4J code written yet** (no Java repo created, nothing built).
- The four foundational decisions are **locked** (table above): Java 17 floor + optional 21 module · Gradle · functional parity · periodic parity-diff · browser-only rendering.
- **Five areas have been deep-dived against the real .NET source** (each carries file:line evidence and is the load-bearing analysis): **§3.2** async context propagation · **§4** HTML-assembly port · **§5** forked-JVM aggregation · **§6.5** PlantUML generation parity · plus the ingestion/output **seam mapping** in §1/§3. Every deep dive concluded the risk is *bounded* — the hard mechanism either already exists in Kronikol (`TestCorrelationStore`, `MergeableReportMerger`), is static text that copies verbatim (§4), or is removed by the browser-only decision (§6.5).
- Verified external facts: AssertJ hooks (§3.9), Micrometer/TTL/ScopedValue (§3.2 — ScopedValue final in JDK 25, Structured Concurrency still preview), JUnit `LauncherSessionListener` semantics (§5.4).

**How to resume — immediate next actions.** Begin **Phase 0** (§9):
1. **.NET-side prep workstream (§6.2)** on the `c:\Code\Kronikol` repo — three upstreamable, test-gated changes (determinism seam · asset externalization §4.2 · parity-hardening §6.5). Prerequisite to golden-file capture.
2. **Gradle multi-module skeleton** + `build-logic` convention plugin (§8, §11) — Java 17, Maven Central publishing scaffold.
3. **Three blocker spikes** with the acceptance criteria written into each Phase-0 bullet: async-context (§3.2), run-lifecycle/forked-JVM (§5), determinism + golden-file harness (§6).
Then Phases 1→4 build the usable narrow product (core seam → diagram pipeline → HTML report → first adapters); Phase 5+ is parallelizable breadth toward full parity.

**Reading order.** §1 (three seams) → §2 (parity reframing) → §3 (hard decisions + deep dives) → §5/§6 (lifecycle + parity hazards) → §9 (phasing). **Appendix A** = the .NET source map (so the deep-dive evidence is preserved and re-exploration is unnecessary). **Appendix B** = open questions / deferred decisions.

---

## 1. The architecture spine: three seams

The entire port pivots on getting three boundaries right. Everything else hangs off these.

### Seam A — Ingestion (the critical one)
Every extension, regardless of what it tracks (HTTP, SQL, Redis, Kafka, cloud SDKs), ultimately calls **one** method with **one** data model:

```
RequestResponseLogger.log(RequestResponseLog entry)
```

`RequestResponseLog` is the lingua franca — ~20 fields describing one half of an interaction (request *or* response; a pair shares `traceId` + `requestResponseId`). If this model and this sink are right, any extension can be written against them without touching the core.

**This is the single most important interface to get right.** It must be stable from Phase 1, because every future extension depends on it.

### Seam B — Context & lifecycle (cross-cutting, invisible but load-bearing)
Two halves:

**(i) Ambient context resolution.** Extensions don't pass test identity around explicitly — they resolve it from ambient context via a 4-layer fallback:
1. HTTP headers (cross-process: server-side code reading an incoming request — see §7 for where this lives in Java)
2. A user-supplied delegate (test-framework context, e.g. JUnit's current test)
3. Async-scoped value (flows to child tasks)
4. A static global fallback (pre-existing pool threads, message consumers)

Plus an ambient **test phase** (Setup vs Action) used for verbosity/filtering decisions, and a **data-keyed correlation store** (`TestCorrelationStore`) that provides *parallel-safe* attribution of background work without relying on ambient flow. The deep dive in §3.2 shows this seam's C#→Java gap is narrower than it first appears — the load-bearing parts port cleanly.

**(ii) Test-run lifecycle.** Scenario start/end *and* whole-run-completion. The .NET version assumes a shared in-process collection that's read at the end; the JVM breaks that assumption (forked test JVMs, ServiceLoader-driven run completion). This is its own architectural concern — see §5.

### Seam C — Output (the portable value, browser-rendered)
```
RequestResponseLog[]  →  PlantUML DSL text  →  (compressed + embedded)  →  HTML report  →  browser renders
```
The PlantUML-generation half is ~800 lines of **pure string building** with no .NET dependencies — it ports almost mechanically and is verifiable with golden-file tests. The HTML-assembly half looks large (~10,800 lines) but ~64% is static JS/CSS that copies verbatim, leaving a genuine port surface of ~3,800 lines (§4). Rendering is **always client-side** (§3.5). The interactive frontend (JS/CSS) is **language-agnostic embedded text that ports for free** (§4).

---

## 2. What "full parity" means in Java (reframing the modules)

The .NET solution has 110 projects, but parity is **functional**, not module-for-module. The Java ecosystem reshapes the count in both directions:

**Java needs FEWER modules in some areas:**
- .NET has 5+ per-driver SQL projects (SqlClient, Npgsql, MySqlConnector, Sqlite, Oracle). Java's **JDBC** is a universal abstraction — *one* JDBC-level tracker (wrapping `DataSource`/`Connection`/`Statement`) covers every relational DB. Hibernate's `StatementInspector` is a second, optional hook.
- **Rendering modules disappear.** The .NET `Kronikol.PlantUml.Ikvm`, server-render, and Node.js-render paths are not ported at all (browser-only).

**Java needs DIFFERENT modules in others:**
- Test frameworks: xUnit/NUnit/MSTest/TUnit/ReqNRoll/LightBDD/BDDfy → **JUnit 5, TestNG, Spock, Cucumber**. Different adapters, same lifecycle seam.
- A **Spring Boot** ecosystem layer that has no direct .NET analog beyond the ASP.NET integration (§7).
- Cloud/messaging SDKs map to their Java equivalents (AWS SDK v2, Azure SDK for Java, Google Cloud Java, Kafka native client, Lettuce/Jedis, etc.).

So "full parity" = **track every dependency class the .NET version tracks, and produce equivalent interactive reports**, organized into idiomatic Java modules — not a literal 110-project mirror.

---

## 3. The hard Java-specific design decisions

Points where direct translation is impossible and the design must be deliberate.

### 3.1 Discriminated unions (`OneOf<HttpMethod, String>`)
C# uses `OneOf<HttpMethod, string>` for `Method` and `OneOf<HttpStatusCode, string>` for `StatusCode`. Java has no built-in union.
- **Decision:** Sealed interfaces with record implementations (Java 17+), consumed via pattern-matching `switch`:
  ```java
  sealed interface Method permits HttpMethod, CustomMethod {}
  record CustomMethod(String value) implements Method {}
  enum HttpMethod implements Method { GET, POST, ... }
  ```
  Clean, exhaustive, idiomatic. Avoids a third-party `OneOf` clone.

### 3.2 Async context propagation (deep dive)
This was the nominal "biggest risk." A close read of how Kronikol *actually* correlates work **narrows it substantially**: the load-bearing mechanisms for the hard cases are already language-agnostic and do **not** depend on `AsyncLocal` auto-flow. The genuine gap is one specific scenario, and it has a known, layered solution.

**The starting fear.** C# `AsyncLocal<T>` flows across `await` automatically — a value set before an await is visible in continuations, even on pool threads. Java has no automatic equivalent: `ThreadLocal` is lost across executor/`CompletableFuture`/reactive boundaries, and `InheritableThreadLocal` copies only at thread *creation* (so it both *misses* reused pool threads and *leaks* stale values into them).

**What Kronikol actually relies on (the reframe).** Identity is resolved by a 4-layer cascade (`TestInfoResolver`), and — crucially — the *parallel-safe* path for background work is a **data-keyed correlation store**, not ambient flow:
- **`TestCorrelationStore`** — a `ConcurrentDictionary<workItemKey, identity>` with a TTL. On every tracked write, extensions record `key → test identity` (e.g. `cosmos:{svc}:{docId}`, `kafka:{svc}:{msgKey}`). Background processors look the key back up and establish a scope per work-item via `CorrelatedProcessingScope`/`ProcessingCorrelation.Wrap(...)`. **This is what makes parallel tests + shared background infrastructure correct — and it's just a concurrent map + wrapper functions, which ports to Java verbatim.**
- The **global fallback (Layer 4)** is a single static value, explicitly documented as **serial-only / not parallel-safe** — Kronikol steers users to the correlation store instead. Same caveat ports unchanged.

So `AsyncLocal` is *not* the mechanism behind the hardest case. It carries identity for exactly two things: (a) the test thread and its directly-awaited continuations, and (b) flow into `Task.Run`-style work spawned *inside* an HTTP request whose scope the server middleware already established.

**Scenario-by-scenario: what carries identity, and Java portability.**

| Concurrency scenario | .NET mechanism | Java mechanism | Verdict |
|---|---|---|---|
| Sync, same test thread | Layer 2 delegate (reads framework's current test) | Extension-set `ThreadLocal` | ✅ ports cleanly |
| HTTP request in test host (`WebApplicationFactory`/`@SpringBootTest`) | Layer 1 headers + server middleware sets scope | Servlet `Filter`/`HandlerInterceptor` sets+clears `ThreadLocal` per request (§7) | ✅ ports cleanly (request thread) |
| Message-driven (Kafka/ServiceBus/…) | Producer stamps message headers; consumer `SetFromMessage` | Consumer interceptor reads headers, sets+clears `ThreadLocal` for handler duration | ✅ ports cleanly; parallel-safe (identity is header-borne) |
| Background pool / Change-Feed / Hangfire, **incl. parallel tests** | `TestCorrelationStore` (data-keyed) + processing decorators | `ConcurrentHashMap` + decorator wrappers | ✅ **ports verbatim; not AsyncLocal-dependent** |
| Periodic background work, no data key | Global fallback (Layer 4), serial-only | Static fallback, serial-only | ✅ ports verbatim (same caveat) |
| **In-process async/thread-pool hand-off within one flow, not data-keyed** | `AsyncLocal` auto-flow | **No free equivalent → propagation toolkit (below)** | ⚠️ the one genuine gap |

**The one genuine gap — and the layered toolkit that closes it.** Only the last row needs something Java doesn't give for free. Behind the `TrackingContext` facade we offer, in increasing power/cost:
1. **`ThreadLocal` default (Java 17).** Covers sync, the test thread, and the filter/consumer request threads — the large majority of real tests.
2. **Explicit capture/restore helpers.** `ContextSnapshot.capture()` at submit, restore-and-**clear** on the worker; wrap `Runnable`/`Callable`/`Executor`. This is the long-established SLF4J-MDC pattern.
3. **Micrometer `context-propagation` integration.** Register a `ThreadLocalAccessor` for Kronikol's context; then **Reactor (3.5+) automatic context propagation** and wrapped executors carry it across reactive/async boundaries. The idiomatic Spring/reactive answer. ([docs](https://docs.micrometer.io/context-propagation/reference/purpose.html))
4. **Optional `kronikol4j-context-agent` (TransmittableThreadLocal).** A `-javaagent` that transparently instruments standard JDK executors so context transmits at *task-submission* time with **zero user code** — the closest thing to AsyncLocal's "it just flows." Opt-in because it's an agent. ([TTL](https://github.com/alibaba/transmittable-thread-local))
5. **Optional Java-21+ module (`ScopedValue`).** `ScopedValue` is **final in JDK 25** (JEP 506) — immutable, leak-free, Loom-friendly. But it only auto-inherits into subtasks **forked inside a `StructuredTaskScope`** (Structured Concurrency is **still preview through JDK 26**); it does **not** auto-flow across arbitrary executors. So treat it as a hygiene/virtual-thread upgrade behind the same facade, **not** a universal async fix.

**Correctness difference that must not be missed — explicit clearing.** .NET's `AsyncLocal`/`using` scope auto-unwinds; Java `ThreadLocal` does **not**. Every scope-establishing point (servlet filter, message consumer, processing decorator) and the test adapter's `afterEach` **must** clear context in a `finally`, or identity leaks to the next test/work-item on a reused thread (the `InheritableThreadLocal` "stale data" trap). `TrackingContext.begin(...)` returns an `AutoCloseable` for try-with-resources; clearing is mandatory, not optional.

**Parallel-safety doctrine (ported from Kronikol).** For parallel-safe background attribution, use **data-keyed correlation** (`TestCorrelationStore` + decorators); treat the global fallback as serial-only; never rely on ambient flow into arbitrary pools under parallel tests. This is Kronikol's own guidance — and it happens to be the cleanly-portable path.

**Net.** The correlation store, headers, delegate, and global fallback — i.e. everything load-bearing — port to Java with `ThreadLocal` + `ConcurrentHashMap` + wrappers. The residual `AsyncLocal`-only scenario is narrow and covered by an opt-in toolkit (Micrometer / TTL-agent / ScopedValue). **Risk re-rated from "could derail" to "bounded and well-understood."** The Phase-0 spike's job is now specific (see §14).

### 3.3 Immutable-core-plus-mutable-enrichment records
C# uses an immutable `record` *with settable properties* (`Phase`, `Timestamp`, `SetupVariant`, `PlantUml` override, etc. are mutated after construction). Java `record`s are fully immutable.
- **Decision:** A plain class `RequestResponseLog` with `final` core fields (constructor/builder) and a small set of mutable enrichment fields, mirroring C# semantics exactly. Provide a fluent **builder**. The enrichment-after-construction pattern is used pervasively by extensions and the report pipeline, so don't force full immutability.

### 3.4 Interception mechanisms (per extension family)
| .NET mechanism | Idiomatic Java equivalent |
|---|---|
| `DelegatingHandler` (HttpClient) | `okhttp3.Interceptor`, `java.net.http.HttpClient` wrapper, Spring `ClientHttpRequestInterceptor` |
| `DbCommandInterceptor` (EF Core) | JDBC `DataSource`/`Connection` proxy; Hibernate `StatementInspector` |
| `DispatchProxy<T>` | `java.lang.reflect.Proxy` (interfaces) / ByteBuddy (classes) |
| Mono.Cecil IL weaving (assertions) | Tiered: AssertJ native hooks (zero-weave baseline) → ByteBuddy/ASM agent only for full-fidelity capture (§3.9) |
| MSBuild tasks (assertion rewriter) | Gradle/Maven plugin + javac AST pass (Tier 2 only — full-fidelity expression/variable capture, §3.9) |
| gRPC interceptors | gRPC `ClientInterceptor`/`ServerInterceptor` (nearly 1:1) |
| `IHttpContextAccessor` (server-side identity) | Servlet `Filter` / Spring `HandlerInterceptor` (§7) |

### 3.5 Diagram rendering — browser-only
Per the locked decision, **all rendering is client-side**. The server-rendered, IKVM/local, and Node.js paths are **not ported**.
- The report embeds each diagram's PlantUML source **compressed (Deflate + custom base64)** into a JS data map; `plantuml-render.js` decodes and renders it in the browser (PlantUML-WASM + Viz.js).
- **Consequence:** Kronikol4J has **no Java-side PlantUML/Graphviz dependency at all** — a real simplification over .NET's IKVM bridge. `DiagramAsCode.imgSrc` is effectively unused for the browser path; only the compressed `codeBehind` matters.
- **CI-summary caveat:** the .NET CI-summary markdown embedded *server-rendered* PNGs. With browser-only rendering, the CI summary cannot inline rendered images. Replacement: the CI summary **links to the HTML report artifact** (and optionally embeds the raw/encoded PlantUML for an external viewer). Noted as a deliberate behavior change in the CI workstream (§9, Phase 5+).

### 3.6 Serialization & output-format stack
C# uses `System.Text.Json` plus YAML/XML emitters; `TrackingSafeSerializer` for proxy-arg capture; `TryFormatAsJson` for note pretty-printing.
- **Decision:** **Jackson** as the single stack — `jackson-databind` (JSON) + `jackson-dataformat-yaml` + `jackson-dataformat-xml` cover all three report formats and pretty-printing from one dependency. Provide a `SafeSerializer` wrapper mirroring `TrackingSafeSerializer` (null-tolerant, cycle-safe, size-bounded) for proxy-argument capture.

### 3.7 Configuration / options model
`ReportConfigurationOptions` (~15 KB) and per-extension options records are a large surface with many defaults.
- **Decision:** Idiomatic Java **builders** for every options type (immutable result + fluent builder + sensible defaults). For Spring users, expose the same options via `@ConfigurationProperties` so they bind from `application.yml` (§7). One options shape, two construction front-doors.

### 3.8 Distributed tracing in the core model
The data model carries `ActivitySpanId`/`ActivityTraceId`, sourced from .NET `System.Diagnostics.Activity`.
- **Decision:** Map to **OpenTelemetry** (`io.opentelemetry`) span/trace IDs, read from the current `Span.current().getSpanContext()` when an OTel API is on the classpath; otherwise leave null. Keep the fields as plain strings in `kronikol-core` (no hard OTel dependency in core); the actual OTel bridge lives in the Phase-5 `kronikol4j-opentelemetry` module. This keeps the core dependency-free while preserving the field contract.

### 3.9 Assertion tracking — tiered (AssertJ-native baseline + optional compile-time fidelity)
This was analyzed in depth because it's the component with the largest C#→Java gap. Conclusion: **a useful subset becomes free and idiomatic in Java; full fidelity still needs compile-time tooling — same category of effort as .NET, just deferred and optional.**

**What .NET captures today.** Two mechanisms — `Kronikol.AssertionTracking` (Mono.Cecil IL weaving of FluentAssertions/AwesomeAssertions/TUnit `.Should()`) and `Kronikol.AssertionRewriter` (Roslyn compile-time source rewrite) — plus a runtime `Track.That(...)` wrapper. Per assertion they record: **expression text** (e.g. `x.Should().Be(1)` → "should be 1"), **pass/fail**, **failure message**, **caller file+line**, and optionally **captured variable names + runtime values**; the result is rendered as a green/red `hnote` and attached to the active step/phase.

**The crux: Java has no `CallerArgumentExpression`.** That C# compiler feature is what gives Kronikol *free* readable assertion source-text (it underpins both `Track.That` and the rewriter). Java has no runtime equivalent — so the cheap auto-capture of expression text is simply unavailable without compile-time work.

**What AssertJ *does* give for free (verified).** Real global runtime hooks, zero bytecode manipulation:
- `Configuration.setDescriptionConsumer(Consumer<Description>)` — fires for **all successful assertion descriptions and the first failed one** (standard assertions) / **all failed ones** (soft assertions). A genuine pass-*and*-fail global sink.
- `AfterAssertionErrorCollected` / `AssertionErrorCollector` SPI — full per-assertion failure capture for **soft assertions**.
- Failure message from the `AssertionError`; **call-site** recoverable by walking the stack trace at hook time.
- Caveat: the consumed `Description` is only meaningful when the user sets one via `.as("…")` — there is no automatic source text.

**Decision — a three-tier design, each shippable independently behind Seam A (all just feed `RequestResponseLogger`):**

- **Tier 0 — manual wrapper (always available; lands with Phase 4):** `Track.that("description", () -> assertThat(x).isEqualTo(1))` (+ `Track.thatSoftly`). Explicit description (the price of no `CallerArgumentExpression`), captures outcome + message + call-site + ambient step/phase. The direct `Track.That` analog.
- **Tier 1 — idiomatic AssertJ integration (zero-weave; early Phase 5):** a `kronikol4j-assertj` module that registers the global `setDescriptionConsumer` + a Kronikol `SoftAssertions`/`AssertionErrorCollector`, so every *described* assertion and every soft-assertion failure is captured **automatically, no per-call wrapping**. This is the "clean hook" — genuinely free, and for the outcome/description/call-site subset arguably nicer than .NET's weaver.
- **Tier 2 — full-fidelity capture (compile-time; later Phase 5+, only if demanded):** to match .NET's *automatic* readable expression text and *auto* variable capture (no `.as()`, no lambda), use compile-time tooling — the same effort class as .NET's two mechanisms:
  - a **Gradle/Maven plugin + javac/annotation-processing AST pass** (parallels `AssertionRewriter`) to recover the source expression and in-scope variable names, injected as the description / a `Track` call; and/or
  - a **ByteBuddy/ASM Java agent** (parallels the IL weaver) reading `LineNumberTable` + the `.java` source for expression text and `LocalVariableTable` (requires `-g`) for variable values.

**Net (correcting the earlier "hardest piece disappears" framing):** AssertJ's native hooks make the **outcome + description + call-site** layer free and clean and shippable early (Tier 0/1). The residual hard part — *free* expression text and *auto* variable values — does **not** come for free in Java and is the same compile-time effort as .NET, but it is now **deferred, isolated, and optional** (Tier 2) rather than a core blocker.

---

## 4. HTML-assembly port (deep dive) — the volume risk is mostly copy-paste

Phase 3 looked like the biggest *volume* risk (`ReportGenerator.cs` ~4,980 lines + `DiagramContextMenu.cs` ~4,064 + `Stylesheets.cs` ~1,736 ≈ **~10,800 lines** of HTML-rendering surface). A full read shows **~64% of it is static JS/CSS that copies byte-for-byte**, and a one-time refactor shrinks the genuine C#→Java surface to **~3,800 lines**.

### 4.1 The four buckets (measured, not estimated)
| Bucket | ~Lines | % | What |
|---|---|---|---|
| **A — Static JS/CSS literals (FREE, copy verbatim)** | ~6,950 | ~64% | `DiagramContextMenu.cs` is **~99.9% static** (11 methods returning raw-string JS/CSS; only ~9 interpolated lines); `ReportGenerator` inline JS funcs (lines 288–1466, ~22 pure-static `"""` functions); `Stylesheets.cs` (1,736 lines pure CSS); the two embedded `.js` files. |
| **B — Interpolated HTML templating (MECHANICAL)** | ~1,150 | ~11% | the `<body>` builder (~151 `body.Append($"…")` calls) + render-helper markup + the `<head>` template. Port + golden-HTML diff. |
| **C — Genuine logic (CAREFUL)** | ~1,950 | ~18% | grouping/sorting/failure-clustering; the parameterized-group **pivot** (`RenderParameterizedGroup`, lines 2545–3104, ~560 lines — the single hotspot); pie-chart geometry; anchor-ID gen; the JSON/XML/YAML data serializers; HTML-escaping. |
| **D — Orchestration / IO** | ~700 | ~7% | `CreateStandardReportsWithDiagrams`, `WriteFile`, attachment copy, resource loading, schema gen. |

### 4.2 The enabling move: externalize Bucket A in .NET first (upstreamable)
Bucket A currently lives *inside* `.cs` files as raw-string literals. Today only `advanced-search.js` / `plantuml-render.js` are real embedded-resource files (loaded via `GetManifestResourceStream`, injected verbatim — proving the pattern). **First refactor the rest of Bucket A into `.js`/`.css` resource files in the .NET codebase** — a behavior-preserving change, gated by the existing AngleSharp + Playwright tests. Effect:
- `DiagramContextMenu.cs` collapses **4,064 → ~150** lines (a loader/dispatcher).
- `ReportGenerator`'s JS region collapses **~1,178 → ~30** lines.
- `Stylesheets.cs` collapses **1,736 → ~1** line.
- **Both runtimes then read the *same* resource files** → asset parity is guaranteed by construction, not by diffing. Java just `getResourceAsStream`s them.

The genuine C#→Java port surface drops from ~10,800 to **~3,800 lines (B+C+D)** — of which only ~1,150 are mechanical templating. This is a .NET-side prep task analogous to the determinism seam (§6.2); do it in Phase 0 so Phases 2–3 consume the externalized assets.

### 4.3 Test assets that come with it
- **`advanced-search.js`** has the only non-trivial parseable logic in Bucket A, and it already has a **Jint harness (~143 tests)** pinning `tokenise`/`parse`/`evaluate`/`match`. **Reuse these cases directly** against a Java JS engine (GraalJS) running the *identical* file — instant parity coverage for the search engine.
- **Playwright E2E (~516 methods)** reuses almost verbatim against Java-generated HTML (CLAUDE.md Playwright rules carry over).
- **Caveat:** there are **no golden/snapshot HTML fixtures today** — current C# tests assert structurally via AngleSharp. So the golden-HTML harness (§6) is genuinely new infrastructure built in Phase 0, not something we inherit.

### 4.4 The one real cross-runtime hazard here
HTML escaping is **`System.Net.WebUtility.HtmlEncode`** at **63 call-sites** — it encodes `& < > "` *and all chars >127 to numeric entities*. Java's `commons-text escapeHtml4` / Spring `HtmlUtils` differ (named vs numeric entities, quote handling). **Build a small escaping shim that reproduces WebUtility's exact output**, or every golden-HTML diff fails. (Tracked alongside the other fidelity hazards in §6.4.)

---

## 5. Test-run lifecycle & forked-JVM aggregation (deep dive)

This looked like a scary JVM-specific problem requiring a bespoke cross-process log merge. The deep dive shows it largely **maps onto a mechanism Kronikol already has** — which de-risks it from "invent a merge" to "port a proven merge and auto-wire it."

### 5.1 What .NET actually does (the model to replicate)
Each .NET test process is **fully self-contained**: a static `RequestResponseLogger` queue accumulates everything, and one **end-of-run hook** (xUnit collection-fixture `Dispose`, MSTest `[AssemblyCleanup]`, NUnit `[OneTimeTearDown]`, TUnit `[After(Assembly)]`) calls `ReportGenerator.CreateStandardReportsWithDiagrams(...)` to load all logs at once, generate diagrams, and write a complete report. CI summary + artifact publishing happen in that same hook.

Critically, **.NET already solves multi-process** for parallel CI runners: `GenerateMergeableData=true` emits an enriched JSON fragment per process (full features/scenarios, compressed diagrams, component relationships, internal-flow segments, CI metadata), and a `kronikol merge ./artifacts` CLI (`MergeableReportMerger`) combines fragments — features grouped by DisplayName, scenarios unioned by Id, component relationships re-aggregated, earliest-start/latest-end reconciled. **This is exactly the shape the Java fork problem needs.**

### 5.2 Why the JVM still diverges (the one real difference)
A single logical test *always* runs entirely within one JVM, so per-process fragments are always complete for their tests — there is **no need to merge raw logs**, only finished report fragments (the cross-cutting aggregate views). The genuine divergence is **defaults**: in .NET multi-process is opt-in (you deliberately set up parallel CI jobs), whereas Gradle (`maxParallelForks`, `forkEvery`) and Maven Surefire (`forkCount`, `reuseForks`) **fork by default, often invisibly**. So Java must make fragment-emission + merge **automatic and on by default**, not a manual CLI step.

### 5.3 The design — port the mergeable model, auto-wire it
- **Every JVM emits a complete report fragment** (the `GenerateMergeableData` JSON, ported) to a shared **run directory**, written atomically (temp file + rename) at its end-of-run hook. No raw-log NDJSON spill; no append-corruption risk; a crashed fork leaves either a whole fragment or none.
- **A merge step combines fragments** into the final HTML/JSON/YAML/XML using the **ported `MergeableReportMerger`** semantics. Because a test never spans forks, the only true cross-fragment work is re-aggregating the component diagram, CI-summary counts, and run start/end — exactly what the .NET merger already does.
- **Two finalize modes, disambiguated by a plugin-injected property** (`kronikol.run.dir`):
  - *Build-orchestrated (default under Gradle/Maven):* the build plugin sets the run-dir property on forked JVMs and owns the merge in a post-test task (Gradle `test.finalizedBy(...)` / `afterSuite` in the daemon after all workers finish; Maven mojo after `test`). The single reliable cross-fork completion signal, since the build tool owns fork orchestration.
  - *Standalone (IDE / plain `java`, no plugin → property absent):* the per-JVM listener does a "merge of one" and writes the final report directly — so an IDE run still produces a report with zero setup.

### 5.4 Completion triggers per framework
- **JUnit Platform (JUnit 5, Cucumber, etc.):** a **`LauncherSessionListener`** registered via `ServiceLoader` (`META-INF/services/...LauncherSessionListener`) — explicitly *"called before the first and after the last test in a launcher session,"* i.e. once-per-JVM, the correct granularity for fragment emission. (Prefer it over `TestExecutionListener.testPlanExecutionFinished`, which can fire multiple times per JVM.)
- **TestNG:** `IExecutionListener.onExecutionFinish` (once-per-JVM).
- **Build layer:** `kronikol4j-gradle-plugin` / Maven plugin owns the cross-fork merge + CI summary/artifact publishing (the once-per-*run* actions).

### 5.5 Robustness & cross-links
- **No write contention:** each fork writes a uniquely-named fragment (JVM/fork id) — concurrent forks never touch the same file (drops .NET's fallback-rename hack).
- **Deterministic merge (links §6):** the merger orders features/scenarios deterministically (DisplayName, then scenario Id) so the final report is identical regardless of fork completion order — required for golden-file parity.
- **Memory:** per-fork generation bounds peak memory to one fork's logs — actually *better* than .NET's single-process-holds-everything.
- **Multi-module Gradle:** each module's `test` task is its own run → its own report by default (matches .NET per-assembly), with an **optional root aggregate** task (the CLI/plugin merge across modules).
- **Two merge entry points, one engine:** the automatic build-plugin merge (intra-build, default) and a manual **`kronikol4j` CLI `merge`** (cross-machine/cross-CI-job) share the ported `MergeableReportMerger`. The CLI is the direct port of the **"Merging Parallel Reports"** feature (the `kronikol merge <inputs…> -o <html> -t <title>` command + `MergeableReportRenderer.MergeFilesToHtml` API): inputs are files/dirs (recursive `*.json`)/globs; **only the enriched JSON is mergeable** (XML/YAML lack the precomputed payload), output is always combined **HTML**. Primary user-facing scenario: a sharded CI matrix where each shard sets `GenerateMergeableData=true`, uploads its `TestRunReport.json`, and a final job runs `kronikol4j merge ./artifacts`. Distribution: a runnable CLI (the `Kronikol.Tool` analog) plus a Gradle/Maven goal. Merge semantics to preserve exactly: scenarios deduped within same-named features; component relationships **sum** call/test counts and **union** method sets; internal-flow/whole-test-flow data unioned; earliest-start/latest-end; CI metadata taken from the first runner that captured it.

**Net.** The hard part (a correct cross-process merge) is a **port of existing, proven .NET logic**, not new design. The only net-new Java work is per-JVM fragment emission via a `LauncherSessionListener` and **auto-wiring the merge by default** because the JVM forks by default. Risk re-rated from "architectural unknown" to "bounded port + build-plugin glue." Owned by `kronikol4j-runtime` (run dir, fragment writer, merge engine, mode detection) + `kronikol4j-gradle-plugin` + `kronikol4j-cli`.

---

## 6. Determinism, the parity corpus & cross-runtime fidelity (Phase 0 backbone)

The golden-file strategy is the verification backbone. Four things must hold for it to work — and two of them expose real cross-runtime hazards that a naive "assert equal" would miss. All are hard Phase-0 gates.

### 6.1 Determinism
The data model uses `Guid.NewGuid()` for `traceId`/`requestResponseId` and wall-clock `DateTimeOffset` timestamps — non-deterministic, so naive snapshots won't match run-to-run or cross-runtime. Two mechanisms:
1. **Deterministic mode** — inject a seeded `IdGenerator` + fixed `Clock` (a seam the core needs for testability anyway).
2. **Normalization pass** — canonicalize/strip volatile fields (IDs, timestamps, absolute paths, PlantUML `autonumber` drift) before diffing, so even non-seeded outputs compare structurally.

### 6.2 The .NET-side prep workstream (real tasks, not just the Java side)
Several upstreamable, behavior-preserving changes to **Kronikol (.NET)** make both the golden capture and the Java match dramatically cleaner. Budget these as a Phase-0 workstream **on the .NET repo**:
1. **Determinism seam** — add the `IdGenerator`/`Clock` seam so the corpus captures deterministically and the periodic parity-diff stays reproducible (without it, snapshots never match).
2. **Asset externalization (§4.2)** — move the static JS/CSS into resource files; shrinks the Java port surface ~64% *and* guarantees frontend-asset parity by sharing the exact files.
3. **Parity-hardening (§6.5)** — normalize newlines to `\n`, pin `InvariantCulture` for casing/number formatting, ordinal sorts, deterministic component-diagram ordering, and adopt client-side diagram splitting. Each removes a cross-runtime divergence *at the source*.
All three are gated by the existing .NET test suite (unit + AngleSharp + Playwright), so "behavior-preserving" is verifiable.

### 6.3 The parity corpus (define it explicitly)
Golden files are only as good as the scenarios they exercise. Anchor the corpus on **porting the .NET demo project (BreakfastProvider)** to Java, then extend to a **feature-coverage checklist** so every diagram code path has a fixture:
- HTTP request/response pairs; status codes; headers.
- SQL (relational); an event / fire-and-forget interaction (`Event` meta-type → dual logs); a proxy call.
- Setup vs Action phases; phase-variant verbosity overrides.
- Parameterized scenarios (input/output tables); tabular attributes.
- Pass **and fail** outcomes; tracked assertions (Tier 0/1).
- Focus-field highlighting; header exclusion; content redaction/truncation.
- Large-diagram **splitting** (exceed max size/height → multiple diagrams).
- Component diagram; (later, with OTel) activity + flame diagrams.
- Multi-host / background-thread correlation.

Each corpus item yields golden PlantUML + HTML + JSON/YAML/XML.

### 6.4 Cross-runtime fidelity hazards (where byte-equality is *not* free)
Two outputs that look "deterministic" still differ across runtimes unless handled deliberately:
- **Deflate encoding is not byte-stable.** `PlantUmlTextEncoder` deflates, then custom-base64-encodes. .NET `DeflateStream` and Java `Deflater` can emit **different compressed bytes for identical input** (different match-finding), so the *encoded* string will likely differ even when the PlantUML is identical. **Assert parity on the DECODED PlantUML text, never the encoded string**; test the encoder in isolation by **round-trip** (encode→decode→equals). **Subtlety (see §6.5):** .NET currently makes *diagram-split decisions* using the encoded length — so non-deterministic Deflate could change *where* diagrams split, diverging even the decoded text. Browser-only client-side splitting removes that dependency.
- **JSON content formatting differs by serializer.** `System.Text.Json` and Jackson differ in default escaping (`+`, `<`, `>`, non-ASCII), key spacing, and ordering. Note bodies embed pretty-printed JSON, so these leak into PlantUML/HTML and break golden parity. **Decision: reproduce .NET's exact per-path behavior** in Java (not "a sensible canonical default"): **preserve input/document key order (do *not* sort)**, **strip null object-properties** (keep null array elements), **2-space indent**, `\n` newlines — and replicate **both** escaping modes the .NET code actually uses: request/response bodies via `UnsafeRelaxedJsonEscaping` (does *not* escape `<>&+`/non-ASCII), but GraphQL metadata via the default encoder (*does* escape them). Likewise force invariant/ISO formatting for culture-sensitive numbers/dates.
- **HTML escaping differs by library (§4.4).** The .NET report escapes via `System.Net.WebUtility.HtmlEncode` (63 call-sites) — `& < > "` plus *all* chars >127 → numeric entities. Java's `commons-text`/Spring encoders differ. **Decision: a WebUtility-parity escaping shim**, unit-tested against .NET's exact output, used everywhere the report escapes — or every golden-HTML diff fails.

Without 6.2–6.5 the golden-file backbone is not merely harder — it is **unbuildable as originally specified**.

### 6.5 PlantUML generation parity (deep dive)
A full read of `PlantUmlCreator.cs` (~844 lines) + the encoder, formatters, palette, and `ComponentDiagramGenerator` surfaced **13 places identical input could yield different PlantUML text**. The pure string-building ports almost mechanically, but these divergences are silent unless deliberately handled. The good news: the single *structural* hazard is removed by a decision we've already made.

**The #1 hazard — and its free fix.** Diagram **splitting depends on the Deflate-encoded length** (`PlantUmlCreator.cs:260` → `EncodedDiagramExceedsMaxLength`, 786–801, which calls `PlantUmlTextEncoder.Encode(...).Length`). Because raw DEFLATE isn't byte-identical across runtimes, the two could split at a different trace → different decoded structure. **Resolution: adopt client-side splitting** — `clientSideSplitting=true` sets the limit to `int.MaxValue` and bypasses server-side splitting (`PlantUmlCreator.cs:132`), and the browser already splits for display. This aligns exactly with our **browser-only** decision: the server emits **one un-split diagram per test** (fully deterministic, no Deflate in any decision); the JS splits at render time. Hazard #1 eliminated by design. *(Fallback if any server split ever remains: gate on a calibrated **plain-text** length, identical on both sides — never on encoded length.)*

**The remaining hazards (catalogue).** Most are best fixed *at the source* in .NET (upstreamable, and they make .NET's own output more deterministic), then matched in Java:

| Sev | Hazard | Evidence | Fix (both runtimes) |
|---|---|---|---|
| HIGH | `Environment.NewLine` (`\r\n` vs `\n`) pervasive; also interacts with #1 (changes length/Deflate) | `PlantUmlCreator.cs:292,596,601,664`; `JsonFocusFormatter.cs` | Emit only `\n`; final `ReplaceLineEndings("\n")` before encode/compare. .NET output is *already* internally newline-inconsistent — fix at source. |
| HIGH | Status-code `Titleize()` uses **CurrentCulture** (Turkish-i etc.) | `PlantUmlCreator.cs:316`; `StringCasing.cs:37` | Invariant/`Locale.ROOT` casing; pin `InvariantCulture` in .NET. |
| HIGH | JSON escaping differs by path (UnsafeRelaxed vs default) | `PlantUmlCreator.cs:604` vs `GraphQlBodyFormatter.cs` | Reproduce both modes (see §6.4). |
| HIGH | **Component-diagram** participant order from `HashSet` iteration | `ComponentDiagramGenerator.cs:123–129` | Sort / `LinkedHashSet` before emit (sequence diagrams already use ordered lists — safe). |
| MED | `:F0`/`:F2` number formatting uses CurrentCulture | `ComponentDiagramGenerator.cs:178`; `InternalFlowRenderer.cs` | `Locale.ROOT` / `InvariantCulture`. |
| MED | Header sort `OrderBy(key)` is culture-sensitive | `PlantUmlCreator.cs:598` | **Ordinal** sort on both sides. |
| MED | `Camelize`/`Pascalize` culture casing + `\p{Lu/Ll/Lo}` regex (feeds alias sanitize) | `StringCasing.cs:16,47` | Reimplement with `Locale.ROOT`; verify Unicode-property parity. |
| MED | Exact `.Trim()/.TrimEnd(NewLine chars)` semantics | `PlantUmlCreator.cs:601`; `JsonFocusFormatter.cs` | Replicate each; `TrimEnd(NewLine.ToCharArray())` strips `\r` *and* `\n`. |
| LOW | Alias regex `[^a-zA-Z0-9_]` | `PlantUmlCreator.cs:529` | ASCII-only — identical, no `\w` Unicode issue. ✅ |
| LOW | Color/shape palette | `DependencyPalette.cs` | Static `FrozenDictionary`/switch, **no hashing** — ports cleanly. ✅ |
| LOW | `EscapeForPlantUmlNote` | `PlantUmlCreator.cs:271` | `replace("\\","\\\\")` — identical. ✅ |
| LOW | `.AsParallel().AsOrdered()`; no Guid/random/timestamp in text path | `PlantUmlCreator.cs:66` | `.AsOrdered()` keeps output deterministic — process tests in any order, key by TestId. ✅ |

**Batch the source-side fixes.** The HIGH/MED items above are **upstreamable .NET parity-hardening fixes** (normalize newlines, pin invariant culture, ordinal sorts, deterministic component-diagram ordering). They join the determinism seam (§6.2) and asset-externalization (§4.2) as a **single Phase-0 ".NET prep" workstream** — each removes a divergence *at the source*, making both the golden capture and the Java match cleaner.

---

## 7. Spring ecosystem (the biggest adoption surface)

Java's dominant server-side world, and the home of server-side identity resolution. The .NET version is deeply ASP.NET-integrated (`IHttpContextAccessor`, `Microsoft.AspNetCore.Mvc.Testing`); Java users will expect equivalents.
- **`kronikol4j-spring-boot-starter`** — auto-configuration that wires trackers (HTTP via `RestTemplate`/`WebClient` interceptors, JDBC via `DataSource` post-processor, etc.) and binds options through `@ConfigurationProperties` (§3.7). Zero-ceremony onboarding.
- **`kronikol4j-servlet`** — a servlet `Filter` / Spring `HandlerInterceptor` that implements **Layer 1** of context resolution: read the Kronikol test-identity headers off the incoming request and push them into `TrackingContext` for the duration of request handling. This is *where the HTTP-header layer actually lives* server-side — §3.2 names the layer; this module realizes it.
- **`MockMvc` / `TestRestTemplate` / `@SpringBootTest`** integration — the analog of `Mvc.Testing`, so in-process Spring Boot tests are tracked end-to-end.
- Sequencing: foundational enough that a minimal `kronikol4j-servlet` should land alongside Phase 4's HTTP tracker (so client→server identity propagation works), with the full starter in early Phase 5.

---

## 8. Module structure (engineered for parity extensibility)

Gradle multi-module. Names use the `kronikol4j-` prefix; root package `io.kronikol`.

```
kronikol4j/
├── build-logic/                       # convention plugin: Java version, deps, publishing, signing, versioning
├── kronikol4j-core/                   # THE SEAM. No third-party tracking deps.
│   ├── tracking/                      # RequestResponseLog, RequestResponseLogger, builder
│   ├── context/                       # TrackingContext, TestIdentityScope, TestPhaseContext, TestInfoResolver, TestCorrelationStore, CorrelationKeys, ProcessingCorrelation (§3.2)
│   ├── registry/                      # TrackingComponentRegistry, TrackingComponent
│   ├── support/                       # IdGenerator, Clock seam (determinism), SafeSerializer
│   └── constants/                     # DependencyCategories, header names, defaults
├── kronikol4j-context-java21/         # optional ScopedValue context — final in JDK 25 (§3.2)
├── kronikol4j-context-agent/          # optional -javaagent: transparent TTL propagation across JDK executors (§3.2)
├── kronikol4j-diagram/                # PlantUmlCreator, encoder, DependencyPalette, ComponentDiagram (PURE)
├── kronikol4j-report/                 # Feature/Scenario/Step models, ReportGenerator, HTML assembly, embedded JS/CSS
├── kronikol4j-runtime/                # run dir, per-JVM report-fragment writer, MergeableReportMerger, mode detection (§5)
├── kronikol4j-junit5/                 # first test-framework adapter (+ ServiceLoader completion listener)
├── kronikol4j-http/                   # first ingestion adapter (HTTP client-side)
├── kronikol4j-servlet/                # server-side Layer-1 header resolution (§7)
├── kronikol4j-jdbc/                   # first DB adapter (covers all relational DBs)
├── kronikol4j-proxy/                  # generic interface/class proxy tracker
├── kronikol4j-assertj/                # Tier-1 assertion tracking via AssertJ native hooks (§3.9)
├── kronikol4j-spring-boot-starter/    # Spring auto-config + @ConfigurationProperties (§7)
├── kronikol4j-gradle-plugin/          # default fork-aware merge + CI summary/artifacts; sets run-dir on forks (§5.3)
├── kronikol4j-cli/                    # `merge` command — cross-machine/CI-job aggregation (kronikol-merge port, §5.5)
└── parity-harness/                    # golden-file fixtures + cross-runtime parity tests (test-only)
```

`kronikol4j-core` and `kronikol4j-diagram` must have **no dependency** on any tracked technology, OTel, or web framework — that purity is what makes them a stable foundation.

---

## 9. Phased delivery

Each phase follows the repo's TDD rule (red → green → refactor). Phases 0–4 deliver a working, end-to-end, *narrow* product. Phase 5+ is parallelizable breadth toward parity.

### Phase 0 — Foundations, parity harness & blockers
*Make the whole effort verifiable, and resolve the three things that would otherwise derail it.*
- Build skeleton: Gradle multi-module, `build-logic` convention plugin, Java 17 baseline, CI, Maven Central publishing scaffold (§11), synchronized versioning.
- **.NET-side prep workstream (§6.2)** *(blocker)* — the three upstreamable, test-gated changes to the .NET repo: (1) `IdGenerator`/`Clock` determinism seam (§6.1); (2) externalize static JS/CSS into resource files (§4.2); (3) parity-hardening — newline/culture/ordinal/ordering fixes + adopt client-side splitting (§6.5).
- **Java determinism seam (§6.1):** mirror `IdGenerator`/`Clock` + the normalization pass on the Kronikol4J side.
- **Parity corpus (§6.3):** port the BreakfastProvider demo + build the feature-coverage checklist that every fixture must exercise.
- **Golden-file harness:** run the *instrumented* C# in deterministic mode over the corpus; capture canonical **decoded** PlantUML, HTML, JSON/YAML/XML as fixtures (encoder verified by round-trip, not byte-pinned — §6.4).
- **JSON note formatter (§6.4):** reproduce .NET's exact per-path behavior (input key order, null-stripping, 2-space indent, the two escaping modes) — the other half of golden parity.
- **Async-context spike (§3.2):** prove, on a corpus with **parallel tests + background work**, that (a) the `ThreadLocal` default + mandatory `finally`-clearing gives correct sync/request-thread attribution with no leakage across reused threads; (b) the data-keyed `TestCorrelationStore` + decorators give parallel-safe background attribution; and (c) at least one propagation path (Micrometer executor-wrapping **or** the TTL `-javaagent`) carries identity across an in-process async hand-off. *(blocker — now scoped, not open-ended)*
- **Run-lifecycle spike (§5):** prove, under Gradle `maxParallelForks > 1`, that each fork emits a complete report fragment (atomic write) via a `LauncherSessionListener`, the plugin merges them deterministically into one report (ordering independent of fork completion), a standalone IDE run self-finalizes (no plugin), and a killed fork degrades gracefully (its fragment is absent, not corrupt). *(blocker — scoped)*
- Lock cross-cutting primitives: unions (§3.1), context facade (§3.2), record+builder (§3.3), Jackson stack (§3.6).

### Phase 1 — Core ingestion seam (`kronikol4j-core`)
*Freeze the contract every extension depends on.*
- `RequestResponseLog` + builder; `RequestResponseType`, `RequestResponseMetaType`, `PhaseVariant`, `TestPhase`.
- `RequestResponseLogger` (static, thread-safe sink; `MaxContentLength` truncation; `getAllLogs()`/`clear()`) — read once at end-of-run by `kronikol4j-runtime` to emit this JVM's report fragment (§5.3).
- `TestIdentityScope` (4-layer), `TestPhaseContext`, `TestInfoResolver`, `PhaseConfiguration`.
- **Context propagation & correlation (§3.2):** the `TrackingContext` facade (ThreadLocal default; `AutoCloseable` scopes with mandatory clearing), `TestCorrelationStore` (concurrent, TTL, data-keyed), `CorrelationKeys`, and the `ProcessingCorrelation.wrap(...)` decorator helpers. These are the parallel-safe primitives extensions build on.
- `TrackingComponentRegistry` + `TrackingComponent`.
- `DependencyCategories`; HTTP/message header constants; `IdGenerator`/`Clock`/`SafeSerializer` support.
- Full TDD coverage. **Output: a stable seam, even with no extensions yet.**

### Phase 2 — Core output pipeline (`kronikol4j-diagram`)
*Pure, high-value, verified against Phase 0 golden files.*
- Port `PlantUmlCreator` (pure string building). Diagram-type taxonomy: **sequence** (now), **component** (now, via `ComponentDiagramGenerator`); **activity** and **flame** are part of the taxonomy but the flame/internal-flow path depends on OTel spans → lands with the OTel bridge in Phase 5. Assert normalized parity against C# golden PlantUML.
- **Apply the §6.5 parity discipline:** adopt **client-side splitting** (one un-split diagram per test → no Deflate in any decision); emit `\n` only; invariant-culture casing/number formatting; ordinal header sort; deterministic component-diagram ordering. These match the .NET prep-workstream fixes (§6.2).
- Port `PlantUmlTextEncoder` (Deflate + custom base64). **Verify by round-trip + decoded-PlantUML parity, not encoded-byte parity (§6.4)** — Deflate output is not byte-stable across runtimes and need not be.
- Per-path JSON note formatting (§6.4 — input key order, null-stripping, two escaping modes); `DependencyPalette` (static map, no hashing); `ComponentDiagramGenerator`.
- Intermediate models: `Feature`, `Scenario`, `ScenarioStep`, `PlantUmlForTest`, `DiagramAsCode`.
- **No rendering strategy abstraction** — browser-only; the module just emits compressed PlantUML for client-side rendering.

### Phase 3 — HTML report + frontend reuse (`kronikol4j-report`)
Staged per the §4 deep dive (genuine surface ~3,800 lines, not ~10,800):
- **Consume the externalized Bucket-A assets** (done as .NET-side prep in Phase 0, §4.2) — embed the *same* `.js`/`.css` resource files via `getResourceAsStream`; wire the compressed-diagram data map for `plantuml-render.js`. (Asset parity by construction.)
- **Escaping shim (§4.4/§6.4):** reproduce `WebUtility.HtmlEncode` exactly; unit-test against its output before any templating.
- **Port Bucket B** (the `<head>` template + `<body>` builder + render helpers) via Java text blocks + a small HTML-builder helper structured to match C# output; verify by **golden-HTML diff** (§6).
- **Port Bucket C** carefully — especially `RenderParameterizedGroup`'s pivot (the hotspot) — and the JSON/XML/YAML serializers via Jackson (JSON/XML) + the hand-built YAML, using the canonical formatter (§6.4).
- **Reuse the Jint search-engine cases (§4.3)** against GraalJS running the identical `advanced-search.js`.
- **Stand up Playwright E2E against Java-generated HTML — proves UI + browser-rendering parity end-to-end.**

### Phase 4 — First real adapters + run lifecycle (prove the seam in three shapes)
Validate all three ingestion patterns and the completion trigger against the frozen core:
- `kronikol4j-runtime` + `kronikol4j-junit5` — JUnit 5 `Extension` (scenario lifecycle, identity, phase detection) **and** the per-JVM `LauncherSessionListener` that emits a report fragment; standalone self-finalize vs plugin-orchestrated merge via run-dir mode detection (§5.3–5.4).
- `kronikol4j-http` (+ minimal `kronikol4j-servlet`) — HTTP tracker → `LogPair` auto-resolve path **and** client→server header propagation (§7).
- `kronikol4j-jdbc` — relational DB tracker → direct `log(...)` path; covers *all* SQL DBs at once.
- `kronikol4j-proxy` — generic proxy tracker → the `DispatchProxy` pattern.
- **Assertion Tier 0:** the `Track.that(description, () -> …)` manual wrapper (§3.9) — small, feeds the logger directly, gives assertion notes from day one.

**End of Phase 4 = a genuinely usable Kronikol4J:** write a JUnit test, hit an HTTP API and a database, get the same interactive diagrammed HTML report the .NET version produces — including across forked JVMs.

### Phase 5+ — Breadth to full parity (parallelizable)
Each is a thin module against the now-stable seam:
- **Spring:** full `kronikol4j-spring-boot-starter` + `@ConfigurationProperties` + MockMvc/TestRestTemplate integration (§7).
- **More test adapters:** TestNG, Cucumber (BDD), Spock, JUnit 4.
- **Messaging:** Kafka (native client), RabbitMQ/JMS, cloud pub/sub (SNS/SQS, Service Bus/Event Hubs, GCP Pub/Sub).
- **Caching:** Redis via Lettuce/Jedis.
- **Cloud SDKs:** AWS SDK v2, Azure SDK for Java, Google Cloud Java (BigQuery, Bigtable, Spanner, Storage).
- **NoSQL:** MongoDB (Java driver), Cassandra.
- **Architectural:** gRPC interceptors; `kronikol4j-opentelemetry` bridge (§3.8); in-process mediator equivalent (Spring events / Axon).
- **CI integration (§3.5 caveat):** `kronikol4j-gradle-plugin`/Maven-plugin CI-environment detection, artifact publishing, and CI-summary markdown that **links to the HTML report** (no inline server-rendered images).
- **Assertion tracking (§3.9):** Tier 1 `kronikol4j-assertj` (zero-weave global hooks — early); Tier 2 compile-time AST/agent for full-fidelity expression+variable capture (later, only if demanded).

---

## 10. Testing & parity strategy

- **TDD** per CLAUDE.md: red → green → refactor on every unit.
- **Determinism first (§6):** seeded IDs + fixed clock + normalization make snapshots stable; this underpins everything below.
- **Golden-file / snapshot parity** (the backbone): the Java port asserts its **decoded** PlantUML, report HTML, and JSON/YAML/XML match fixtures captured from the C# original (after normalization, §6.1/§6.4); the encoder is verified by round-trip, **not** byte-pinned (§6.4).
- **Reusable test assets (§4.3):** the existing **Jint search-engine suite (~143 cases)** runs against GraalJS executing the identical `advanced-search.js`; the C# AngleSharp structural assertions port as structural checks. Note: **no golden/snapshot HTML exists today** — the golden harness is new Phase-0 infrastructure.
- **Playwright E2E reuse** for the interactive UI + browser rendering, run against Java-generated HTML.
- **Fork-aggregation tests:** explicitly run the suite under Gradle multi-fork to prove cross-process log merge (§5).
- **Cross-runtime parity CI job:** periodically regenerate fixtures from C# and diff, catching drift (locked sync strategy).

---

## 11. Publishing & distribution

NuGet auto-packaging → **Maven Central**, which is more involved:
- **Coordinates:** group `io.kronikol`, artifacts `kronikol4j-*`, root package `io.kronikol`.
- **Requirements:** GPG-signed artifacts, `javadoc` + `sources` jars per module, complete POM metadata (name/description/url/licenses/developers/scm), Sonatype/Central Portal account + namespace verification.
- **Decision:** Centralize all of this in the `build-logic` convention plugin so every module is publishable identically — set up the publishing pipeline in **Phase 0** (even publishing a `kronikol4j-core` snapshot early), so distribution isn't a late surprise.
- License + branding continuity: carry the existing LICENSE and icon into artifact metadata.

---

## 12. Documentation & release automation

CLAUDE.md mandates wiki + changelog upkeep and synchronized versioning; Kronikol4J needs its own.

### 12.1 The Kronikol4J wiki (full parity with Kronikol.wiki)
The existing `Kronikol.wiki` is a mature body of work: **89 pages, ~25,600 lines, ~150–170k words, ~300 code samples, 16 images**, organized hub-and-spoke via `_Sidebar.md`. Kronikol4J ships an **equivalent wiki**, same information architecture, Java content.
- **Location:** `../Kronikol4J.wiki` (sibling repo, mirroring the `../Kronikol.wiki` convention).
- **Information architecture (mirror the original):**
  - **Home / Demo** — landing page + link to a live Java demo report (a Java "BreakfastProvider" equivalent).
  - **Getting Started** — Quick Start (JUnit 5 + Gradle, and a Maven variant), Project setup, AI-integration prompt, Framework Integration overview matrix.
  - **Common Tasks** — How-To index, FAQ, Filtering & Redacting Diagram Content.
  - **Framework Integration guides** — JUnit 5, TestNG, Spock, Cucumber (replacing the xUnit/NUnit/MSTest/TUnit/BDDfy/LightBDD/ReqNRoll set).
  - **Extension guides** — one page per integration (JDBC, Hibernate, HTTP/OkHttp/WebClient, Spring Boot starter, Redis, Kafka, RabbitMQ/JMS, MongoDB, AWS/Azure/GCP SDKs, gRPC, OpenTelemetry, proxy…). One guide per module, same template each (How It Works → Install → Verbosity → Operations → Setup → Patterns → Troubleshooting).
  - **Configuration** — Tracking Dependencies, HTTP Tracking Setup, Custom Dependencies, Report Configuration, Diagram Customisation, Content Formatting, Phase-Aware Tracking, Excluded Headers.
  - **Features** — Generated Reports, Component Diagrams, **Browser Rendering** (now the only rendering path — the server/IKVM/SVG pages are dropped/merged), Internal Flow Tracking, Step/Event Tracking, Tabular Attributes, Search Syntax, Large-Diagram Handling, CI Summary/Artifacts, **Merging Parallel Reports** (the `GenerateMergeableData` + `kronikol4j merge` guide, §5.5 — including the sharded-CI-matrix example and, new for Java, the automatic forked-JVM aggregation note, §5.2–5.3), Diagnostics & Debugging.
  - **Java-specific pages (new, no .NET analog):** *Forked-JVM Log Aggregation* (§5), *Test-Identity Propagation & Async Context* (§3.2), *Determinism & the Golden-File Workflow* (§6), *Migrating from Kronikol (.NET) concepts*.
  - **Reference** — API Reference (per-module class/method tables), Example Project walkthrough.
- **Assets:** regenerate the screenshot/GIF set from Java-generated reports (the UI is identical, so visuals carry over once Phase 3 produces real HTML).
- **Style/templates:** reuse the original's page templates and cross-linking density (`[[WikiLink]]`, sidebar + in-page anchors); only code samples change (Java + Gradle/Maven).

### 12.2 Docs-as-you-go (wired into the phasing)
The wiki is **not a final-phase afterthought** — each phase produces its pages so docs never lag the code:
- **Phase 0–1:** the Java-specific concept pages (async context, fork aggregation, determinism) — these are most valuable while the design is fresh.
- **Phase 2–3:** Browser Rendering, Generated Reports, Component Diagrams, Diagram Customisation.
- **Phase 4:** Quick Start (JUnit 5), the first framework + extension guides, Home/Demo.
- **Phase 5+:** one wiki page lands **with** each new extension/adapter module (definition of done includes its guide), plus FAQ/How-To/API-Reference growth.

### 12.3 Other docs & release automation
- **Java `CLAUDE.md`** capturing Java conventions (TDD, Playwright rules, the determinism/golden-file workflow, the fork-aggregation gotcha, the wiki-per-module rule).
- **README + Changelog** maintained per release.
- **Release automation in `build-logic`:** single source-of-truth version applied to all modules; a release task that bumps version, updates changelog, tags `v{version}`, publishes to Maven Central, and reminds to update the wiki — mirroring the .NET release rule, centralized rather than per-project.

---

## 13. Rough sizing (relative, not a commitment)

| Phase | Relative size | Notes |
|---|---|---|
| 0 — Foundations & blockers | M–L | Three spikes + harness + publishing; high-leverage, de-risks everything |
| 1 — Core ingestion seam | M | Small surface, must be exactly right |
| 2 — Diagram pipeline | M | Mostly mechanical port, golden-verified |
| 3 — HTML report + frontend | M–L | genuine surface ~3,800 lines after §4.2 externalize-first (not ~10,800); ~64% copies verbatim; golden-HTML + Jint reuse |
| 4 — First adapters + lifecycle | M–L | Proves all three patterns + fork aggregation + Spring header propagation |
| 5+ — Breadth to parity | XL (parallelizable) | Many thin modules; scales with how many run concurrently |

Phases 0–4 are the critical path to a usable product; Phase 5+ is the long, parallelizable tail. A strong candidate for multi-agent orchestration once the seam is frozen.

---

## 14. Risks & decisions

**Top risks**
1. **Async context propagation (§3.2)** — **re-rated down** after deep dive. The load-bearing parallel-safe mechanism (`TestCorrelationStore`) is data-keyed, not AsyncLocal-dependent, and ports verbatim; headers/delegate/global-fallback port cleanly too. Residual gap = in-process async hand-off only, covered by an opt-in toolkit (Micrometer / TTL-agent / ScopedValue). The real must-do is **mandatory `finally`-clearing** to avoid `ThreadLocal` leakage. Bounded; Phase-0 spike validates it.
2. **Forked-JVM aggregation (§5)** — **re-rated down** after deep dive. Maps onto Kronikol's existing mergeable-data + `MergeableReportMerger` model (a port, not new design); a test never spans forks, so only finished fragments merge, not raw logs. Net-new work = per-JVM fragment emission (`LauncherSessionListener`) + auto-wiring the merge by default (the JVM forks by default). Bounded.
3. **Golden-file determinism (§6)** — without seeded IDs/clock + normalization, the verification backbone fails; hard Phase-0 gate.
4. **HTML-assembly volume (§4)** — **re-rated down** after deep dive. ~64% is static JS/CSS that copies verbatim; externalizing it in .NET first (§4.2) shrinks the genuine port surface from ~10,800 to ~3,800 lines (only ~1,150 mechanical). Residual care: the `RenderParameterizedGroup` pivot, the data serializers, and the `WebUtility.HtmlEncode` parity shim (§4.4). Golden-HTML diffing + the reusable Jint suite contain it.
5. **Two-codebase drift** — periodic parity-diff (locked).
6. **Assertion tracking (§3.9)** — baseline de-risked: AssertJ's native hooks give outcome/description/call-site with zero weaving (Tiers 0/1). Residual: *automatic* expression-text + variable capture has no free Java equivalent (no `CallerArgumentExpression`) and needs Tier-2 compile-time tooling — but it's now optional and isolated, not a core blocker.
7. **Cross-runtime output formatting (§6.4–6.5)** — the deep dive found 13 PlantUML-text divergence points (newlines, culture-sensitive casing/numbers, two JSON escaping modes, `HashSet` ordering) plus a structural one: split decisions depended on non-deterministic Deflate length. **The structural hazard is removed by adopting client-side splitting** (aligns with browser-only); the rest are fixed at the source via the .NET parity-hardening prep (§6.2) and matched in Java. Bounded; not a blocker.
8. **javax→jakarta split (§15)** — the Spring/servlet ecosystem fork; addressed by targeting `jakarta`/Spring Boot 3 primary with a documented compatibility stance.

**Locked decisions:** Kronikol4J / `io.kronikol` / `kronikol4j-*` · Java 17 floor + optional 21 module · Gradle · functional parity · periodic parity-diff · **browser-only rendering**.

---

## 15. Engineering standards & compatibility

- **Dependency philosophy (adoption-critical).** `kronikol4j-core` and `kronikol4j-diagram` have **zero required runtime dependencies** beyond the JDK; `kronikol4j-report` owns Jackson. Every extension declares the tracked technology (Kafka, Redis, a JDBC driver, an AWS SDK, Spring…) as **`compileOnly`/`provided`/optional** scope, so Kronikol4J never forces a version onto the user's app or drags transitive bloat. Hard rule — Java teams reject libraries with heavy or version-opinionated dependency trees.
- **JDK & library compatibility matrix.** Build/test on JDK 17, 21, and current. Test each adapter against the library versions in real use — and explicitly across the **`javax` → `jakarta` namespace split**: the servlet filter and Spring integrations (§7) target **`jakarta`/Spring Boot 3** as primary, with a documented stance (and, if demanded, a separate `javax`/Spring Boot 2 artifact). This split is the single biggest Java-ecosystem compatibility trap.
- **Same-JVM parallel execution is first-class.** Beyond forked JVMs (§5), JUnit 5 / TestNG parallel execution interleaves tests on threads within one JVM. The `ThreadLocal`-based context (§3.2) plus per-test scoping must keep identity correct under parallelism — the analog of the .NET "parallel-safe background correlation" feature; gets its own test suite and wiki page.
- **JPMS.** Ship stable `Automatic-Module-Name` manifest entries on every module from day one (cheap; prevents downstream module-path pain). Full `module-info.java` is optional and can come later.
- **Versioning relationship to .NET.** Kronikol4J uses **independent SemVer**, starting pre-1.0, reaching 1.0 when core + first adapters (Phases 0–4) are stable; it does **not** lockstep the .NET 3.x line. The wiki documents the feature-parity mapping per release.
- **Project CI pipeline (GitHub Actions).** Matrix build/test across JDKs; Playwright E2E against Java-generated HTML; the cross-runtime parity-diff job (§6, §10); Maven Central publish on tag; wiki link-check. Set up minimally in Phase 0, grown per phase.
- **Security / redaction parity.** Header-exclusion, token-redaction, and truncation rules are security-sensitive; they must port exactly, with tests asserting redacted secrets never appear in any emitted artifact (PlantUML, HTML, JSON/YAML/XML).

---

## Appendix A — Key .NET source map (so the deep-dive evidence is preserved)

Paths are under `c:\Code\Kronikol`. This is the consolidated result of the deep dives — use it to locate the behavioral spec without re-exploring. Line numbers are as observed at v3.0.40 and may drift; treat them as starting points.

**Ingestion seam (§1 Seam A, Phase 1)**
- `src/Kronikol/Tracking/RequestResponseLogger.cs` — static sink; `ConcurrentQueue` (line 13); `Log(...)`, `LogPair(...)`, `RequestAndResponseLogs`, `Clear`, `MaxContentLength`.
- `src/Kronikol/Tracking/RequestResponseLog.cs` — the data model record (fields ~10–50; mutable enrichment props after).
- `src/Kronikol/Constants/DependencyCategories.cs` — open string constants. `src/Kronikol/Constants/HttpHeaders.cs` — `test-tracking-*` HTTP headers + `kronikol-test-*` message headers.
- `src/Kronikol/Tracking/TrackingComponentRegistry.cs` + `ITrackingComponent`. `src/Kronikol/Tracking/MessageTrackerOptions.cs` — the common options shape.
- `src/Kronikol/Tracking/TrackingProxy.cs` — `DispatchProxy<T>` pattern (the proxy-tracker template).

**Context & correlation (§1 Seam B, §3.2)**
- `src/Kronikol/Tracking/TestIdentityScope.cs` — `AsyncLocal` (48), `GlobalFallback` single static (72–75, *not* parallel-safe), `Begin` (104–109), `SetFromMessage` (119–122).
- `src/Kronikol/Tracking/TestInfoResolver.cs` — the 4-layer cascade (15–127): HTTP header → delegate → AsyncLocal → global fallback.
- `src/Kronikol/Tracking/TestPhaseContext.cs` (AsyncLocal phase), `PhaseConfiguration.cs` (ShouldTrack / effective verbosity).
- **Parallel-safe correlation (the key §3.2 finding):** `src/Kronikol/Tracking/TestCorrelationStore.cs` (`ConcurrentDictionary` + TTL), `CorrelatedProcessingScope.cs`, `ProcessingCorrelation.cs`, `CorrelationKeys.cs`; `src/Kronikol.Extensions.CosmosDB/ChangeFeedCorrelation.cs` (a decorator example).
- HTTP propagation: `src/Kronikol/Tracking/TestTrackingMessageHandler.cs` (outgoing header stamping), `TestTrackingContextMiddleware.cs` (incoming → `Begin`). Message: Kafka `TrackingKafkaProducer/Consumer.cs`.

**Output pipeline (§1 Seam C, §4, §6.5, Phases 2–3)**
- `src/Kronikol/PlantUml/PlantUmlCreator.cs` (~844) — `CreatePlantUml` (105–266); **split decision (260) → `EncodedDiagramExceedsMaxLength` (786–801, uses encoded length; `clientSideSplitting` bypass at 132)**; `FormatNoteContent` (551–602, header `OrderBy` 598); `SanitizeAlias` regex (529–535, ASCII); `EscapeForPlantUmlNote` (271); status `Titleize` (316); `.AsParallel().AsOrdered()` (66).
- `src/Kronikol/PlantUml/PlantUmlTextEncoder.cs` — raw DEFLATE + custom base64 (alphabet at `EncodeByte` ~51–67). Used as a generation input *only* at the split decision.
- `src/Kronikol/PlantUml/DependencyPalette.cs` — static `FrozenDictionary`/switch, no hashing.
- `src/Kronikol/Reports/ReportGenerator.cs` (~4,977) — entry `CreateStandardReportsWithDiagrams` (37–245); `GenerateHtmlReport` (256–2322); head template (`$$"""`, 1468); inline JS funcs (288–1466, ~22 static); `RenderParameterizedGroup` (2545–3104, the logic hotspot); data serializers (JSON 3737, XML 4016, YAML 4101); `WriteFile` (4518–4535).
- `src/Kronikol/Reports/DiagramContextMenu.cs` (~4,064) — **~99.9% static JS/CSS** (11 raw-string methods); `src/Kronikol/Reports/Stylesheets.cs` (~1,736 static CSS); embedded `src/Kronikol/Reports/advanced-search.js` (286) + `src/Kronikol/PlantUml/plantuml-render.js` (363).
- `src/Kronikol/Reports/ComponentDiagramGenerator.cs` — **`HashSet` iteration order hazard (123–129)**; method sort (175,189).
- Formatting helpers w/ parity hazards: `StringCasing.cs` (Titleize/Camelize culture, 16/37/47), `GraphQlBodyFormatter.cs` (default JSON escaping), `JsonFocusFormatter.cs` (NewLine), `TryFormatAsJson` (`UnsafeRelaxedJsonEscaping`, ReportGenerator/PlantUml path ~604).
- Escaping: `System.Net.WebUtility.HtmlEncode` (63 call-sites in ReportGenerator.cs).
- `src/Kronikol/ReportConfigurationOptions.cs` (~235) — the big options object (incl. `GenerateMergeableData`).

**Merge / lifecycle (§5)**
- `src/Kronikol/Reports/Merge/MergeableReportMerger.cs` + `MergeableReportRenderer.MergeFilesToHtml` — the "Merging Parallel Reports" engine.
- CI: `src/Kronikol/Reports/CiMetadata.cs` (env detection), `CiArtifactPublisher.cs`, `CiSummaryWriter`.
- Report triggers (per framework): `templates/kronikol-xunit3/Infrastructure/TestRun.cs` (collection-fixture Dispose); MSTest `[AssemblyCleanup]`; NUnit `[OneTimeTearDown]`; TUnit `[After(Assembly)]`; xUnit2 `ReportingTestFramework.cs`.

**Assertions (§3.9)**
- `src/Kronikol/Tracking/Track.cs` — `Track.That` (50–96), `LogAssertion` (417–455, the `hnote` rendering). `src/Kronikol.AssertionTracking/AssertionWeaver.cs` (Mono.Cecil IL weaver). `src/Kronikol.AssertionRewriter/AssertionWrappingRewriter.cs` (Roslyn rewrite). `ClosureValueResolver.cs`, `AssertionExpressionFormatter.cs`. `StepCollector.cs:182` (assertion→sub-step).

**Models:** `src/Kronikol/Reports/Feature.cs`, `Scenario.cs`, `ScenarioStep.cs`; `PhaseVariant`.

**Reusable test assets**
- `tests/Kronikol.Tests.SearchEngine/` — **Jint harness, ~143 cases** pinning `advanced-search.js` (reuse via GraalJS).
- `tests/Kronikol.Tests/Reports/` — ~40 files, AngleSharp structural assertions (no golden/snapshot HTML exists).
- `tests/Kronikol.Tests.EndToEnd/` — Playwright (~516 methods).
- `DiagramContextMenuTests.cs` — 103 text-contains tests.

---

## Appendix B — Open questions & deferred decisions

Not blockers; revisit as the relevant phase approaches.
- **CLI distribution form** — the `kronikol4j merge` tool as a runnable fat-jar, `jbang` script, and/or a Gradle/Maven goal? (Decide in Phase 5 CI work.)
- **Spring Boot 2 / `javax` artifact** — ship a parallel `javax` artifact, or `jakarta`-only? Default: jakarta-only until demand appears (§15).
- **Full JPMS `module-info.java`** — `Automatic-Module-Name` from day one; full module descriptors deferred (§15).
- **Mediator equivalent** — the MediatR analog: Spring `ApplicationEvent`s vs Axon vs a thin own-interface proxy (§3.4/Phase 5).
- **JS engine for Jint-test reuse** — GraalJS is the presumptive choice (Nashorn removed in JDK 15+); confirm in Phase 3.
- **Assertion Tier 2** — whether to build the compile-time AST/ByteBuddy full-fidelity capture at all is demand-driven (§3.9); Tiers 0/1 may suffice indefinitely.
- **Cucumber phase mapping** — exact Given→Setup / When·Then→Action wiring for the BDD adapter (Phase 5).
- **Multi-module aggregate report** — per-module report is the default (§5.5); whether to offer a root cross-module aggregate by default or opt-in.
- **Plain-text split threshold** — only relevant if any server-side splitting is ever reintroduced; client-side splitting (§6.5) makes it moot for now.
- **`.NET prep` upstreaming** — whether the three §6.2 changes are merged into the main Kronikol repo or kept on a parity branch (affects the periodic parity-diff workflow).

---

## Appendix C — Proxy-tap / ingestion / Playwright contracts (added with .NET 3.0.44)

The .NET codebase now ships three generic assets the Java port must mirror byte-for-byte on the wire (see the wiki pages *Integration ProxyTap Extension*, *Ingesting External Captures*, *Integration Playwright*, *Capture-Time Redaction*):

1. **Proxy-tap topology (`kronikol4j-proxytap`, new module).** A transparent HTTP tee (`ProxyTap` + `ProxyTapOptions`) for backends that cannot be instrumented. Identity resolution order: `test-tracking-current-test-name`/`-current-test-id` → configured fallback headers → W3C `traceparent` trace id. Re-inject the four `TestTrackingHttpHeaders` (`test-tracking-current-test-name`, `-current-test-id`, `-caller-name`, `-trace-id`) on the forwarded request when missing; honour `test-tracking-ignore`; forward bytes untouched (hop-by-hop headers stripped, `Host` rewritten); emit server/client spans on an `ActivitySource`-equivalent (`Kronikol.ProxyTap`) and re-parent the forwarded `traceparent`; decode gzip/deflate/br for capture only; cap bodies (`…truncated (N chars total)` marker); **redact the secret denylist at capture** (`authorization`, `proxy-authorization`, `cookie`, `set-cookie`, `x-api-key`, `x-auth-token`, `api-key`, `x-amz-security-token`, `x-goog-api-key`); unattributed requests forwarded but not captured unless opted in (`CaptureUnattributedRequests` + optional fixed `FallbackTestId`). Options shape = the `ProxyTapOptions` record in `src/Kronikol.Extensions.ProxyTap/ProxyTapOptions.cs`. Java servlet/Jetty/undertow-based tee is acceptable; semantics must match.
2. **NDJSON capture format + `ingest` (`kronikol4j-cli ingest`, `InteractionRecord` in core).** One JSON object per request *or* response, camelCase, matching `TestRunReport.json`'s `httpInteraction` shape plus `testId`, `testName`, `dependencyCategory`, `callerDependencyCategory`, `phase`, `metaType`, `activityTraceId`, `activitySpanId`, `trackingIgnore` (nulls omitted, unknown properties ignored). Companion tests NDJSON: `event` (start|step|end), `testId`, `testName`, `feature`, `timestamp`, `status`, `durationMs`, `error`, `text`, `keyword`. Pipeline semantics: read → clear store → replay in timestamp order → normalise test names from the tests file → synthesise features (feature per `feature`, scenario per `testId`; tests seen only in interactions default to Passed) → generate. Status vocabulary mapping in `FeatureSynthesizer.MapStatus`. Non-UUID ids hash (MD5 → UUID) so pairs still match; 32-hex W3C trace ids parse as UUIDs directly.
3. **Playwright / browser identity (`kronikol4j-playwright`).** `TestTrackingIdentity` (testName, testId, callerName = `Browser`, traceId UUID whose 32-hex form is the W3C trace id; `testId` defaults to that 32-hex); `toHeaders()` = the four tracking headers + a sampled `traceparent`; header values ISO-8859-1-safe, ≤ 512 chars; `newTrackedContext(identity)` / `useTestTracking(context, identity)`; `beginScope()` bridges to the in-process identity scope.
4. **Capture-time redaction hook** on the logger (`RequestResponseLogger.Redaction` ↔ `CaptureRedaction`: header denylist, content regex patterns, custom hook, applied before enqueue, before truncation).
5. **Core additions to mirror:** `DependencyCategories.AI` (+ `DependencyType.AI`, colour `#16A085`, sequence shape `control`, component shape `hexagon`); `ReportsFolderPath` honoured (absolute or relative to base dir); resettable diagram cache (`DefaultDiagramsFetcher.Reset()`); `CollapseConsecutiveIdenticalCalls` / `CollapseThreshold` / `MaxArrowsPerDiagram` (PlantUML `loop ×N · min–max ms` fragment, `...+N more calls omitted (MaxArrowsPerDiagram)...` delay line, key = caller+service+method+pathAndQuery+GraphQL op+status+metaType); `ShowNoInteractionsMarker` (`<div class="no-interactions" data-no-interactions="true">No interactions captured for this scenario.</div>`); `httpInteractions` always emitted in the data file regardless of internal-flow tracking.

### User actions / steps / assertions from external captures (added with .NET 3.0.44, P7)

Ingest parity: `InteractionRecord.kind = "ui"` (one actor→service arrow, `durationMs` interval for nesting) and
tests-NDJSON `step` (with `level`/`status`/`durationMs`/`error`) and `assertion` events map onto the same step
delimiter / assertion note PlantUML the in-process step & assertion tracking emit. Java has no Playwright
reporter API equivalent in scope; the ingest side is what needs porting (`IngestPipeline` marker handling,
`FeatureSynthesizer.BuildStepTree`, `DependencyCategories.User`).
