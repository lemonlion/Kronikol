# Kronikol.js — Node / TypeScript Port Plan

**Project name:** **Kronikol.js** — the TypeScript-native reimplementation of Kronikol for the Node.js ecosystem, with *full feature parity* as the end state, delivered core-first. Like the Java port (Kronikol4J), the core is engineered from day one as a foundation extensions plug into, so the parity build-out is incremental breadth rather than re-architecture.

**Guiding principle:** Idiomatic TypeScript throughout. We treat the C# code as an executable *specification of behavior*, not a blueprint to translate line-by-line. Where C# and Node diverge (async context, interception, the test-run lifecycle, cross-process aggregation), we build the idiomatic Node mechanism that produces the same observable result.

**Sibling effort:** A Java port (**Kronikol4J**, the `java/` subtree of this repo — `C:\Code\Kronikol4J` until the migration lands) is already substantially built out (core, diagram, report, runtime, junit5, http, jdbc, proxy, servlet, cli, redis, mongodb, messaging, testng, assertj, opentelemetry, spring modules exist with tests). Its plan lives at `docs/JAVA_PORT_PLAN.md`. **Use it as a proven decomposition and as the consolidated .NET source map (its Appendix A).** This document does not repeat that source map in full — it references it and focuses on the Node-specific design.

### Locked decisions
| Decision | Choice |
|---|---|
| **Name / scope** | **Kronikol.js** (prose/display name), npm scope **`@kronikol/*`** (`@kronikol/core`, `@kronikol/express`, …). **No separate `KronikolJS` repo** — superseded, see the row below. |
| **Language** | **TypeScript**, `strict` mode. Source under `js/packages/*/src`. |
| **Repository placement** | The **`js/` subtree of the Kronikol polyglot monorepo**, alongside `dotnet/` and `java/` — see `MONOREPO_MIGRATION_PLAN.md`. *This supersedes the original decision to build in a separate `KronikolJS` repo at `C:\Code\KronikolJS`.* Consequences: goldens come from the shared top-level **`parity/`** rather than a private per-language harness; CI is **`js-ci.yml`**, path-filtered on `js/**` + `parity/**`; release tags are **`js-v{version}`** (the repo's `v*` namespace is split per stack). |
| **Workspace + build** | Inside `js/`: **pnpm workspaces** + **Turborepo** (task orchestration/caching) + **changesets** (versioning + changelog). **tsup** (esbuild) for builds. The pnpm workspace root is `js/`, *not* the repository root — the repo holds three independent build systems side by side. |
| **Module format** | **Dual ESM + CJS** via `package.json` `exports` conditions + `.d.ts`. Express/Fastify apps span both worlds. |
| **Node baseline** | **Node 20 LTS floor** (stable `AsyncLocalStorage`, `node:test`, `require(ESM)`, global `fetch`). Test on 20 / 22 / current. |
| **Test frameworks (first-class)** | **Vitest, Jest, node:test, Mocha, Cucumber-js, Playwright** — adapters + parity tests. |
| **Web frameworks (first-class)** | **Express** (middleware) and **Fastify** (plugin/hooks) for server-side Layer-1 identity. |
| **Diagram rendering** | **Browser-only.** The server-rendered, IKVM, and the existing `NodeJsPlantUmlRenderer` (server-side SVG) paths are **not ported** — all rendering is client-side. |
| **Parity scope** | **Functional parity** — track everything the .NET version tracks, organized into idiomatic npm packages (package count differs from .NET). |
| **C# sync** | **Periodic parity-diff** — re-capture golden fixtures periodically and diff to catch drift. |

---

## 0. How to use this document — status & resumption

*(Self-sufficient: any fresh session can resume from this file + `JAVA_PORT_PLAN.md` Appendix A alone.)*

**What Kronikol is.** A .NET testing/documentation framework that automatically captures real dependency interactions during tests (HTTP, SQL/NoSQL, cache, messaging, cloud SDKs) and generates **interactive HTML reports with PlantUML diagrams** (sequence, component, activity, flame) — deterministic diagrams from actual execution, not AI. Source at `c:\Code\Kronikol` (**v3.0.43** at time of writing; multi-targets net8.0/9.0/10.0). Wiki at `../Kronikol.wiki` (89 pages). Demo project: "BreakfastProvider" (under `examples/Example.Api`).

**What this document is.** The authoritative plan for **Kronikol.js**, a from-scratch *idiomatic TypeScript* reimplementation targeting **full functional parity**, delivered **core-first**. This file is the source of truth for *intent and design*; the .NET code is the *behavioral specification* (executable spec, not a line-by-line porting blueprint). The **Java port plan's Appendix A is the shared .NET source map** — do not re-explore the .NET tree; start there.

**Status (at time of writing).** **Design phase complete; no Kronikol.js code written yet** (no repo, nothing built). The locked decisions (table above) and the section-by-section design are settled — including **twenty-two completed deep dives whose conclusions are already written into the body. Treat these as decided; do not re-open them:**
| Deep dive | Conclusion (see section for full reasoning) |
|---|---|
| **§3.2** Async context | `AsyncLocalStorage` is native; validated against pg/ioredis/kafkajs; the one rule is **bind-at-call-site** (never re-resolve at completion). |
| **§3.10** Dual-package singletons | The **#1 silent risk**. All core ambient state in a `globalThis` major-versioned symbol registry (OTel's pattern); post-build dual-load test + attw/publint gates. |
| **§3.11** HTTP-client interception | **Two disjoint transport hooks** (undici `Dispatcher` interceptor for `fetch` + `node:http` monkeypatch); body-tee is the real work; each hook also propagates identity headers. |
| **§3.12** Database tracking | Driver-level public-API wrap is universal and **covers every real-driver ORM**; **Prisma `$extends`** is the sole exception; OTel = shallow long-tail. |
| **§4.5** `RenderParameterizedGroup` pivot | A ~1,000-line subsystem, ≈80% mechanical: **two rule engines** (column R0/R1/R2 + cell-value); drop record-`ToString()` flatten (no Node analog); **engine in Phase 3, per-adapter capture in Phase 4/5**. |
| **§4.6** Specifications report (2nd output) | **Two HTML reports, one shared generator** (`includeTestRunData` flag) → specs HTML ~free; living-documentation with **blank-on-failure**; **3 simpler behavior-only data serializers** (text-only steps, no version/execution data). |
| **§3.13** Security & redaction | .NET redaction is **presentational** (render-time) → secrets leak into JSON + the worker fragment; nothing redacted by default. Port adds **capture-time redaction at Seam A** + secure preset; parity-vs-security mode split. |
| **§3.14** Step capture & phase | Agnostic `StepCollector` engine (Map+stack+ALS) ports verbatim → core; capture **tiered like §3.9** (explicit `step()` / native Cucumber-Gherkin + Playwright `test.step` / optional build-transform replacing IL-weaving); **phase from adapter setup-hooks→Setup for non-BDD runners**. |
| **§3.15** Participant naming | Per-adapter `serviceName`/`callerName` (the diagram boxes); **simpler in Node** — drop the Refit/`IHttpClientFactory` client-name matching (no analog); HTTP via `resolveServiceName`/host-map cascade, DB/cache/messaging via the resource; unmapped-target diagnostics registry. |
| **§3.16** Internal-flow / OTel activity+flame | **One subsystem** (capture+render). Capture = OTel `SpanProcessor` (cleaner than .NET, no AppInsights conflict; **opt-in — needs the SUT on OTel**). Render = pure-PlantUML activity + client-side flame (dodges §6.4 floats). Correlation **free via OTel's ALS context**. Phase 5, in `@kronikol/opentelemetry`. |
| **§3.17** CI integration | **The most trivially portable subsystem** — env-var + file-append + stdout logging-commands (language-agnostic platform contracts), copies near-verbatim. 2 platforms (GitHub Actions, Azure DevOps). Place in the §5 finalize/merge (once-per-run); stub metadata in golden mode; summary **links to report** (browser-only). |
| **§3.18** Diagnostics report | Standalone tracking-health dashboard — **pure aggregation + HTML** over already-captured data (logs, span sources, **component registry's "never-invoked"** warning, unmatched targets, assertion diag). Lower parity bar. Node value-add: **surfaces `__diagnostics()`** + a **dual-package-split detector** (§3.10) making the #1 silent failure visible. |
| **§3.19** Component diagrams & analytics | **Bigger than the §6.5 footnote** — a C4 diagram + a **mini-APM analytics engine** (latency percentiles/outliers/CV, payload/concurrency, fan-in/out, **cycle detection**, longest-chain, call-ordering, error-correlation, **diff mode**). Pure computation → ports mechanically, but **leans hard on the §6.1 deterministic clock** (all stats are timing-derived). Enabled by default; Phase 2 (compute+C4) → Phase 3 (HTML analytics). |
| **§3.20** Tabular attributes | Batch N test cases into one execution (pay lifecycle once) + input/output-table rendering. **Rendering ports mechanically** (data model in §6.7); the **C#-attribute DSL needs a Node redesign** → a `tabular()` builder + typed iterable inputs/outputs + verify. Value-prop carries to Node. |
| **§3.21** Event tracking / MessageTracker | `MessageTracker` = the **generic non-HTTP primitive** (events/messages) behind all messaging extensions — clean Seam-A `log` wrapper → core. The **`Event` meta-type** gives fire-and-forget diagram styling (blue notes, no method/status; pure §6.5 rendering). EDA testing reuses §3.2/§7 header propagation. |
| **§3.22** Minor feature gaps (consolidated) | Diagram customisation (themes/CSS/favicon/logo/palette), Setup/Action visual separation, focus fields + participant emphasis, tags/labels display+filter, content formatting (GraphQL), AI-prompt + `npm create @kronikol` scaffolder. All mechanical config/rendering — no architectural risk. |
| **§3.23** Multi-host / dual-host architectures | A usage pattern (SUT spans API + worker/function hosts). **In-process is *free* in Node** — the §3.10 process-global registry shares the sink/correlation/ALS, so no DI-container bridging (unlike .NET); cross-process reuses §5 fragments + §7 headers. From the wiki audit. |
| **§3.24** Kronikol4J reconciliation | The Java sibling **already built + proved the §6 backbone** (`dotnet-capture` tool, feature-keyed golden corpus, byte-parity tests, offline Playwright) → risk #3 *demonstrated*. #1 lesson: **audit every input-conditional branch** (whole features hid behind un-triggered inputs). Gaps found: **Failure Clusters**, filter/toggle boxes, `BackgroundStepsDetector`. Gotcha: anchor-id `-N` dedup. Divergence: immutable-rebuild models. |
| **§5 (5.3–5.9)** Run-lifecycle / aggregation | **Two axes / three finalize modes**; **fragment-per-file** (not per-worker); per-file sink-clear closes a cross-file-bleed bug; 6-row adapter SPI. |
| **§6.6** Golden-file harness | .NET capture tool w/ pre-encode tap → **one symmetric canonicalizer** → two-direction parity-diff; verify encoder via native `plantuml-render.js` decode. |
| **§6.7** Data-export serializers | **Three formats, three null policies** (JSON writes nulls/camelCase; YAML+XML omit/PascalCase; note-JSON strips) + two casings; exact `MapLogJson`/`MapStepJson`/`Full`, `ScenarioStableId` (SHA256/16-hex), `ExecutionStatus` names, `KronikolVersion` determinism hazard, lossy hand-built `SanitiseForYml`. **Empirically (a/b): JSON indent=2, durations=raw number (matches JS), but data-export JSON uses the *default* encoder (`<>&'+`/non-ASCII → UPPER `\uXXXX`) → needs a *custom escaper*, not native `JSON.stringify`.** Ship model+serializers+parity-tests together, iterate to byte-parity. |
| **§7** Express/Fastify server identity | Exact `test-tracking-*` header contract; **topology-4 rule** (scope from headers only when present, never shadow ambient — resolves §5.3); multi-hop relay is **free in Node** (ALS); **Express `run` vs Fastify `enterWith`+`fastify-plugin`** is the one porting risk. |

**Deliberately NOT yet deep-dived** (lower-risk; fine to leave until their phase): Phase-5 adapter specifics (messaging/cloud/NoSQL — thin packages against a frozen seam) and the assertion **Tier-2** build transform (§3.9 — demand-driven). *(All other subsystems have now been deep-dived; see the ledger above.)*

**A few version-specific facts could not be web-verified this session** (web tools were erroring) — they're flagged inline and consolidated in **Appendix C**; verify them first if you have web access. None change the architecture.

**The three Node-specific reframings that set the risk profile (read these first):**
1. **Async context is essentially free.** Node's `AsyncLocalStorage` (`node:async_hooks`, stable since Node 16) is a near-1:1 equivalent of .NET `AsyncLocal<T>`: it **auto-flows** across `await`, `.then`, `setTimeout`/`setImmediate`, and `process.nextTick`, and **auto-unwinds** when the `als.run(store, fn)` callback returns. This eliminates Java's single biggest risk (§3.2 of the Java plan) *and* its #1 must-do hazard (Java's mandatory `finally`-clearing of `ThreadLocal`). See §3.2 below.
2. **The frontend ports for free, literally.** The embedded `advanced-search.js` / `plantuml-render.js` are JavaScript — they run **natively** in Node and in the browser with no engine shim. The existing **search-engine test suite (~143 cases)** runs directly (Java needed GraalJS). Playwright E2E (~516 methods) is already JS-native and ports to `@playwright/test` cleanly.
3. **Cross-process aggregation is still required** — Node test runners fork worker processes (Jest workers, Vitest `forks` pool, `node:test` per-file subprocesses, Playwright workers), so the **mergeable-fragment model must be ported and auto-wired by default**, exactly as in Java (§5). This is a port of proven .NET/Java logic, not new design.

**How to resume — immediate next actions.** Begin **Phase 0** (§9):
0. **Orient (≈15 min):** read the deep-dive ledger above (those areas are settled), then — if web access is available — knock out **Appendix C**'s verification list, and confirm whether **Kronikol4J already completed the §6.2 .NET prep** (reuse if so).
1. **Shared .NET-side prep (§6.2)** — determinism seam · asset externalization (§4.2) · parity-hardening (§6.5). **Shared with Kronikol4J** — if already done for the Java port, Node consumes it directly; otherwise do it once, both ports benefit.
2. **pnpm/Turborepo/changesets monorepo skeleton** + tsup dual-build convention + CI + npm publish scaffold (§8, §11).
3. **Two blocker spikes** (down from Java's three — async context is no longer a blocker): the **run-lifecycle / worker-aggregation spike** (§5) and the **determinism + golden-file harness** (§6).

**Reading order.** §1 (three seams) → §2 (parity reframing) → §3 (hard decisions, incl. the deep dives §3.2 and §3.10–3.24) → §5/§6 (lifecycle + parity hazards + harness) → §9 (phasing). Appendix A = Node-specific source-map deltas (the full map is the Java plan's Appendix A). Appendix B = open questions / deferred decisions. **Appendix C = facts to verify at implementation** (do these first if you have web access).

---

## 1. The architecture spine: three seams

The entire port pivots on getting three boundaries right. Everything else hangs off these. (Identical conceptual spine to .NET/Java — only the realizations change.)

### Seam A — Ingestion (the critical one)
Every extension, regardless of what it tracks, ultimately calls **one** function with **one** data model:

```ts
RequestResponseLogger.log(entry: RequestResponseLog): void
```

`RequestResponseLog` is the lingua franca — ~20 fields describing one half of an interaction (request *or* response; a pair shares `traceId` + `requestResponseId`). **This is the single most important interface to get right.** It must be stable from Phase 1, because every future extension depends on it. .NET ref: `src/Kronikol/Tracking/RequestResponseLog.cs`, `RequestResponseLogger.cs`.

### Seam B — Context & lifecycle (cross-cutting, load-bearing)
Two halves:

**(i) Ambient context resolution.** Extensions resolve test identity from ambient context via a 4-layer fallback (`TestInfoResolver`):
1. HTTP/message **headers** (cross-process: server-side code reading an incoming request — §7)
2. A user-supplied **delegate** (test-framework context, e.g. the current Vitest/Jest test)
3. **Async-scoped value** (`AsyncLocalStorage` — flows to child async work automatically)
4. A **static global fallback** (serial-only; not parallel-safe)

Plus an ambient **test phase** (Setup vs Action) and a **data-keyed correlation store** (`TestCorrelationStore`) for *parallel-safe* attribution of background work without relying on ambient flow. **In Node, Layer 3 is native and automatic** (§3.2) — a large simplification over Java.

**(ii) Test-run lifecycle.** Scenario start/end *and* whole-run completion. Node test runners fork worker processes, so this needs per-realm fragment emission + a merge step — its own architectural concern (the granularity is per-*file*, see §5.4).

### Seam C — Output (the portable value, browser-rendered)
```
RequestResponseLog[]  →  PlantUML DSL text  →  (deflate + custom base64)  →  HTML report  →  browser renders
```
The PlantUML-generation half is ~800 lines of **pure string building** with no .NET dependencies — it ports almost mechanically and is verifiable with golden-file tests. The HTML-assembly half looks large (~10,800 lines) but ~64% is static JS/CSS that copies verbatim, leaving a genuine port surface of ~3,800 lines (§4). Rendering is **always client-side**. The interactive frontend (JS/CSS) is **JavaScript that runs natively** — it ports for free (§4). **Two reports come out of one generator** — `TestRunReport` (execution view) and `Specifications` (living-documentation view), flag-differentiated (§4.6) — each with HTML + JSON/YAML/XML data (§6.7).

---

## 2. What "full parity" means in Node (reframing the packages)

The .NET solution has 110 projects; parity is **functional**, not project-for-project. Node reshapes the count:

**Node needs FEWER packages in some areas:**
- **Rendering packages disappear** — `Kronikol.PlantUml.Ikvm`, server-render, and the `NodeJsPlantUmlRenderer` path are not ported (browser-only).

**Node needs DIFFERENT packages in others:**
- **Test frameworks:** xUnit/NUnit/MSTest/TUnit/ReqNRoll/LightBDD/BDDfy → **Vitest, Jest, node:test, Mocha, Cucumber-js, Playwright**. Different adapters, same lifecycle seam.
- **Web frameworks:** ASP.NET integration → **Express** and **Fastify** packages (server-side Layer-1 identity, §7).
- **HTTP clients:** one `DelegatingHandler` → **two disjoint transport hooks** (an undici `Dispatcher` interceptor for `fetch`/undici + a `node:http`/`https` monkeypatch for axios/got/node-fetch/AWS/Azure) — neither alone covers the ecosystem because `fetch` bypasses `node:http`. Deep dive §3.11.
- **Databases — the one genuinely harder area than Java.** No JDBC-equivalent universal abstraction → SQL tracking is **per-driver** (`pg`, `mysql2`, `better-sqlite3`/`node:sqlite`, `mssql`, `oracledb`). But the deep dive (§3.12) shows driver-level wrapping **transparently covers every query-builder/ORM that uses a real driver** (Knex/TypeORM/Sequelize/Kysely/Drizzle/MikroORM) — so it's **one driver layer + a single Prisma `$extends` exception** (Prisma's Rust engine bypasses the JS driver), with OTel as a shallow long-tail catch-all.
- **Cloud/messaging SDKs** map to their JS equivalents (`@aws-sdk/*` v3, `@azure/*`, `@google-cloud/*`, `kafkajs`, `ioredis`, `mongodb`, etc.).

So "full parity" = **track every dependency the .NET version tracks, and produce equivalent interactive reports**, organized into idiomatic npm packages — not a literal 110-project mirror.

---

## 3. The hard Node-specific design decisions

### 3.1 Discriminated unions (`OneOf<HttpMethod, string>`) — *native, trivial*
C# uses `OneOf<HttpMethod, string>` for `Method` and `OneOf<HttpStatusCode, string>` for `StatusCode`.
- **Decision:** TypeScript **union types**, optionally tagged. `type Method = HttpMethod | string` (with `HttpMethod` a string-literal union or `enum`), consumed by narrowing. No third-party `OneOf`. Cleaner than both C# and Java.

### 3.2 Async context propagation — *the risk that evaporates*
This was Java's nominal biggest risk; in Node it is largely a **non-problem**.

**Why.** `AsyncLocalStorage` (`node:async_hooks`) is the idiomatic, built-in equivalent of `AsyncLocal<T>`:
- A value set via `als.run(store, fn)` is visible to **all async continuations** spawned inside `fn` — across `await`, promise chains, `setTimeout`/`setImmediate`, `process.nextTick`, and event-emitter callbacks registered within the scope. This is exactly .NET's "flows across `await`" behavior.
- The scope **auto-unwinds** when `run()`'s callback settles — like a C# `using` scope. **No manual clearing is needed** (the opposite of Java's `ThreadLocal`, whose missing auto-unwind was its #1 hazard). *Caveat:* `als.enterWith()` does **not** auto-unwind — **prefer `run()`**; reserve `enterWith()` for the few sites (middleware that can't wrap the whole chain) where it's unavoidable, and pair it with explicit teardown.

**The 4-layer cascade, Node realization:**

| Concurrency scenario | .NET mechanism | Node mechanism | Verdict |
|---|---|---|---|
| Sync / same-test async chain | Layer 2 delegate + Layer 3 `AsyncLocal` | Layer 2 delegate + `AsyncLocalStorage.run` | ✅ native, auto-flow + auto-unwind |
| HTTP request in in-process test host (supertest / `fastify.inject` / app under test) | Layer 1 headers + server middleware sets scope | Express middleware / Fastify `onRequest` hook wraps handler in `als.run` (§7) | ✅ ports cleanly |
| Message-driven (Kafka/SQS/…) consumer | producer stamps headers; consumer `SetFromMessage` | consumer wrapper reads headers, `als.run` for handler duration | ✅ parallel-safe (header-borne) |
| Background pool / shared infra, **incl. parallel tests** | `TestCorrelationStore` (data-keyed) + decorators | `Map` + TTL + `als.run` decorator wrappers | ✅ ports verbatim; not ALS-dependent |
| Periodic background work, no data key | global fallback (serial-only) | static fallback (serial-only) | ✅ same caveat |
| In-process async hand-off, not data-keyed | `AsyncLocal` auto-flow | **`AsyncLocalStorage` auto-flow** | ✅ **the Java "genuine gap" does not exist in Node** |

**The genuine Node caveats (small and well-understood):**
- **`AsyncLocalStorage` does not cross `worker_threads` / `child_process` boundaries.** That's by design and is precisely what Layer 1 (headers) and the fragment/merge model (§5) handle — identity crosses processes via headers or data-keyed correlation, never via ambient flow.
- **Pooled/long-lived resources carry no store in their *internal* callbacks** (a pool's sockets are created at startup). This does **not** break the awaiting caller (the `await` continuation keeps the store) — it only bites if the tracker re-resolves identity in a completion callback instead of binding at the call site. See the validation deep dive below for the rule; data-keyed `TestCorrelationStore` covers the genuinely scope-less cases (consumers, pub/sub).
- **`enterWith` leakage** (above) — prefer `run`.

**`TestCorrelationStore` still ports verbatim** (a `Map<workItemKey, identity>` + TTL + `wrap()` decorators) — it remains the parallel-safe path for background work that can't carry headers or async flow. .NET ref: `TestCorrelationStore.cs`, `CorrelatedProcessingScope.cs`, `ProcessingCorrelation.cs`, `CorrelationKeys.cs`.

**Validation against real target libraries (deep dive — does "near-free" actually hold?).** The claim is only worth banking if ALS survives the libraries Kronikol must track — connection pools, Redis clients, Kafka consumers — which create long-lived async resources *outside* any test scope. Walking the real cases yields **one load-bearing rule** and confirms the rest.

- **The pooled-resource subtlety.** A `pg.Pool` / ioredis client / mysql2 pool is created once at startup (no store), so its sockets — the async resources whose context is captured at *creation* — carry **no test identity**. A query's response arrives on that socket's `data` event, in the **startup** context. So if the tracker tried to read identity in a low-level completion callback it would get **nothing — or, under parallel tests sharing one pool, the wrong test.** *But ALS still flows correctly to the awaiting caller:* `const r = await pool.query(...)` resumes in the test's scope because the `await` continuation captured the store. The pool internals never need to carry it.
- **The load-bearing rule — bind at the call site, never re-resolve at completion.** The tracker reads ALS identity **once, synchronously, when the tracked method is invoked** (inside the test scope), tags the in-flight operation with it, and carries it explicitly to the completion path — it must **not** call `als.getStore()` again when the response arrives. This makes pool internals irrelevant to attribution and is **parallel-safe by construction** (each in-flight op owns its identity; a shared pooled socket's context is never consulted). Same "capture, don't rely on ambient flow at completion" doctrine as .NET/Java — but Node makes the *capture* trivial because ALS is already correct at the call site.

**Per-library verdict** (command path = ✅ via call-site bind; consumer/background path = headers/correlation **by design**, not a regression):
| Library / pattern | Mechanism | Verdict |
|---|---|---|
| pg / mysql2 / mssql / oracledb `query()` (pool or client, promise) | call-site ALS read + tag in-flight | ✅ |
| better-sqlite3 (synchronous) | runs in-scope synchronously | ✅ |
| ioredis / node-redis commands, pipelines, cluster | call-site bind | ✅ |
| ioredis **pub/sub** `on('message')` | long-lived conn, no store → headers / `TestCorrelationStore` | ✅ by design (not ALS) |
| kafkajs **producer** `send()` | call-site bind + **stamp identity headers** | ✅ |
| kafkajs **consumer** `eachMessage` | long-lived poll loop, stale/no store → `als.run(identityFromHeaders)` at handler top, else correlation key | ✅ by design (not ALS) |
| mongodb commands | call-site bind | ✅ |
| mongodb **change streams** / tailable cursors | long-lived → correlation | ✅ by design |
| AWS/Azure/GCP `client.send(cmd)` | call-site bind | ✅ |
| SQS/PubSub long-pollers | consumer pattern → headers/correlation | ✅ by design |

**Honest residual caveats (all low-risk):** userland promise libs (bluebird/when) historically broke async_hooks propagation — native promises (what the targets use today) are fine; `emitter.emit` from I/O context runs listeners in the *emitter's* context (irrelevant under call-site binding, a trap only if someone attributes inside an event handler); modern ALS (Node 16+) dropped the old `async_hooks` perf overhead on the common path; worker/child boundaries never carry the store (by design → §5 mode 3).

**Validation in the corpus/spike (add to §5.9):** a **pg pool with two concurrent ALS scopes**, asserting each query logs the correct identity (proves call-site bind under pooling + parallelism), plus a deliberate **"wrong way"** case (re-resolve in the completion callback) showing it mis-attributes under concurrency — pinning *why* the rule exists.

**Net (refined).** "Near-free" **holds for the request/response command path** — the bulk of tracking — **provided the tracker binds at the call site**. It was never claimed for consumers/background, which the design already routes through headers + `TestCorrelationStore` (table rows 3–4). The deep dive's concrete addition: the **bind-at-call-site invariant** and the **pooled-socket completion-context trap** — a real, silent parallel-safety bug if violated — now **explicit rather than implicit**. Re-rating unchanged: native, no propagation toolkit, no `-javaagent` analog, no mandatory clearing; **one rule to follow**, wired at the four scope-establishing points (test adapter, web middleware, message consumer, processing decorator) plus every dependency call site.

### 3.3 Immutable-core-plus-mutable-enrichment — *native, trivial*
C# uses an immutable `record` *with settable properties* (`Phase`, `Timestamp`, `SetupVariant`, `PlantUml` override, etc. mutated after construction).
- **Decision:** A plain TS `class` (or object) with `readonly` core fields set in the constructor/builder and a small set of **mutable enrichment fields**, mirroring C# semantics exactly. Provide a fluent builder or a factory + typed partial. The enrichment-after-construction pattern is used pervasively by extensions and the report pipeline — don't force full immutability. **Validated alternative (§3.24):** Kronikol4J went **fully-immutable `record` + rebuild-on-enrichment** (e.g. it rebuilds affected `Scenario`s rather than mutating in place) and it works cleanly — so if mutable-enrichment gets awkward in TS, immutable-rebuild is a proven fallback.

### 3.4 Interception mechanisms (per extension family)
| .NET mechanism | Idiomatic Node equivalent |
|---|---|
| `DelegatingHandler` (HttpClient) | **Two disjoint transport hooks — deep dive §3.11:** an **undici `Dispatcher` interceptor** (covers global `fetch`/undici) **+** an idempotent **`node:http`/`https` monkeypatch** (covers axios/got/node-fetch/superagent/AWS-SDK-v3/Azure). Library-level `axios.interceptors`/`got` hooks are opt-in alternatives, not the default (double-count risk) |
| `DbCommandInterceptor` (EF Core) | **Per-driver public-API wrap — deep dive §3.12:** wrap the query choke point of `pg`/`mysql2`/`better-sqlite3`/`node:sqlite`/`mssql`/`oracledb`. This **transparently covers Knex/TypeORM/Sequelize/Kysely/Drizzle/MikroORM** (they use the real driver). **Prisma is the sole exception** (Rust engine bypasses the JS driver → `$extends`). OTel DB spans = shallow long-tail catch-all. *(No JDBC-style single hook — the one area Node is harder than Java.)* |
| `DispatchProxy<T>` | **Native `Proxy`** — better ergonomics than `DispatchProxy` or `java.lang.reflect.Proxy`; wraps both objects and class instances |
| Mono.Cecil IL weaving (assertions) | Tiered: hook assertion libs at runtime (zero-build baseline) → **build-time transform** (Vite/SWC/Babel plugin, power-assert-style) for full-fidelity capture (§3.9) |
| MSBuild assertion rewriter | Bundler/transform plugin (Vite, Rollup, esbuild, SWC, Babel, ts-patch) — Tier 2 only |
| gRPC interceptors | `@grpc/grpc-js` client/server `Interceptor` (near 1:1) |
| `IHttpContextAccessor` (server identity) | Express middleware / Fastify `onRequest` hook (§7) |

### 3.5 Diagram rendering — browser-only
Per the locked decision, **all rendering is client-side**. The server-rendered, IKVM/local, and the existing **`NodeJsPlantUmlRenderer`** (`src/Kronikol/PlantUml/NodeJsPlantUmlRenderer.cs` — downloads viz/plantuml JS and renders SVG server-side) paths are **not ported**.
- The report embeds each diagram's PlantUML source **compressed (Deflate + custom base64)** into a JS data map; `plantuml-render.js` decodes and renders it in the browser (PlantUML-WASM + Viz.js).
- **Consequence:** Kronikol.js has **no server-side PlantUML/Graphviz dependency** — a real simplification. `DiagramAsCode.imgSrc` is effectively unused; only the compressed `codeBehind` matters.
- **CI-summary caveat:** the .NET CI summary embedded *server-rendered* PNGs. Browser-only means the CI summary **links to the HTML report artifact** instead (optionally embedding raw/encoded PlantUML). A deliberate behavior change in the CI workstream (Phase 5+).

### 3.6 Serialization & output-format stack
C# uses `System.Text.Json` plus YAML/XML emitters; `TrackingSafeSerializer` for proxy-arg capture; `TryFormatAsJson` for note pretty-printing.
- **Decision:** **Native `JSON`** for the JSON outputs; **hand-built YAML and XML emitters** for the data-export `.yaml`/`.xml` (mirroring .NET's `StringBuilder`/`XElement`). **Do NOT use the `yaml` npm package for the data export** — it produces *standard* YAML escaping, but .NET uses a **lossy custom `SanitiseForYml`** (replaces specials with safe text), so byte-parity requires reproducing that exact routine, not a real YAML serializer (§6.7). `JSON.stringify` preserves insertion order (matches "do not sort") and its 2-space indent matches .NET — but **its escaping matches only the note-body path** (UnsafeRelaxed); the **data-export JSON needs a custom escaper** reproducing `System.Text.Json`'s default encoder (escapes `<>&'+`/non-ASCII → uppercase `\uXXXX`, §6.7). Provide a `SafeSerializer` (null-tolerant, cycle-safe via a seen-set, size-bounded) for proxy-argument capture. **Note the three different null policies + two casings across the serializers — see §6.7** (and §6.4 for the separate note-body escaping modes).

### 3.7 Configuration / options model
`ReportConfigurationOptions` (~235 lines) and per-extension options are a large surface with many defaults.
- **Decision:** Plain **typed options objects** (`interface` + `defu`-style deep-merge with defaults) for every options type — idiomatic in Node, no builder ceremony required. Each package exports `defaultXOptions` and a `resolveXOptions(partial)` merger. Express/Fastify packages additionally accept options from the app's config object.

### 3.8 Distributed tracing in the core model
The data model carries `ActivitySpanId`/`ActivityTraceId` from `System.Diagnostics.Activity`.
- **Decision:** Map to **OpenTelemetry JS** (`@opentelemetry/api`) span/trace IDs, read from `trace.getActiveSpan()?.spanContext()` when the OTel API is present; otherwise leave undefined. Keep the fields as plain strings in `@kronikol/core` (no hard OTel dependency in core); the bridge lives in the Phase-5 `@kronikol/opentelemetry` package, which also hosts the **internal-flow / activity+flame** capture+render subsystem (§3.16). Note: OTel's context propagation and `AsyncLocalStorageContextManager` are themselves ALS-based, so they compose cleanly with Seam B (this is what makes §3.16's span→test correlation free).

### 3.9 Assertion tracking — tiered (runtime hooks + optional build-time fidelity)
Largest C#→JS gap, same shape as Java's analysis.

**What .NET captures.** Per assertion: **expression text** (`x.Should().Be(1)` → "should be 1"), **pass/fail**, **failure message**, **caller file+line**, optionally **captured variable names + runtime values**; rendered as a green/red `hnote` attached to the active step/phase. Mechanisms: Mono.Cecil IL weaving + a Roslyn rewriter + a runtime `Track.That(...)` wrapper.

**The crux:** like Java (no `CallerArgumentExpression`), JS has no *free* runtime access to the source expression text. But Node has two things that help: **`Error().stack` + source maps** give file/line/function cheaply, and **build-time AST transforms are a mature pattern in JS** (power-assert/`@power-assert`, `unexpected`, `expect`'s own internals) — they already extract expression text *and* sub-expression runtime values.

**Decision — three tiers, each shippable independently behind Seam A (all feed `RequestResponseLogger`):**
- **Tier 0 — manual wrapper (always available; lands with Phase 4):** `track("description", () => expect(x).toBe(1))` (+ `track.softly`). Explicit description, captures outcome + message + call-site (via `Error().stack`) + ambient step/phase. The direct `Track.That` analog.
- **Tier 1 — runtime library hooks (zero-build; early Phase 5):** a `@kronikol/assert` package that wraps/monkeypatches the common matchers — **`node:assert`**, **`chai`**, **`expect` (Jest/Vitest)** — so every assertion's pass/fail + message + call-site is captured **automatically**. Cleaner than weaving for the outcome/message/call-site subset.
- **Tier 2 — full-fidelity capture (build-time; later, only if demanded):** a **Vite/SWC/Babel/esbuild transform plugin** (the power-assert pattern) that rewrites assertion call-sites to inject the source expression text + in-scope variable values — matching .NET's *automatic* readable expression text. Same effort class as .NET's two mechanisms, but **deferred, isolated, optional**.

**Net.** Outcome + message + call-site is free and clean and early (Tier 0/1). *Free* expression text + auto variable values is the residual hard part (Tier 2), but it's a well-trodden JS pattern, not a blocker. **Interlocks with §3.14:** tracked assertions attach as **sub-steps** of the active step (`StepCollector.addAssertionSubStep`), and the **Tier-2 build transform is shared with §3.14's step auto-capture** (one Babel/SWC/ts plugin replaces both .NET IL-weavers). .NET refs: `Track.cs`, `AssertionWeaver.cs`, `AssertionWrappingRewriter.cs`.

### 3.10 Dual-package singletons & shared ambient state — *the #1 dual-format footgun*
The locked decision to ship **dual ESM+CJS** (table in the header) collides head-on with Kronikol's architecture, which is **a shared global sink** (Seam A). This needs deliberate design, not a footnote — it is the single most likely way to ship a build that passes every test and silently produces wrong reports.

**The mechanic (the "dual-package hazard").** With a dual `exports` map (`import` → `./dist/index.mjs`, `require` → `./dist/index.cjs`), Node loads the package **twice** whenever a consumer's dependency graph reaches it through *both* an `import` and a `require` — directly or transitively. The two copies are **separate module instances with separate module-level state.** Because `@kronikol/core` is a transitive dependency of *every* other `@kronikol/*` package, a single stray `require('@kronikol/core')` anywhere in the graph (a legacy CJS Express app, a CJS HTTP-client wrapper, an older test adapter) splits the core's state from the rest.

**Why it is acute for Kronikol — and silent.** The whole value proposition is "every tracked interaction lands in *one* collection, read once at end-of-run." If the HTTP tracker (loaded ESM) appends to the **ESM copy's** log array but the test adapter's end-of-run hook (loaded CJS) reads the **CJS copy's** array, the report is **partial or empty — yet every test passes green.** No exception, no warning. This is the worst class of bug: invisible data loss in the product's core output.

**The full inventory of at-risk state** (all live in `@kronikol/core`, all must be process-singletons):
| State | Split-failure symptom |
|---|---|
| `RequestResponseLogger` queue | report missing interactions (silent data loss) |
| **The `AsyncLocalStorage` instance** | middleware `.run()` on one copy, logger `.getStore()` on the other → **context resolves `undefined` → wrong/empty identity attribution** (a *different*, equally silent failure) |
| `TestCorrelationStore` (Map+TTL) | parallel-safe background correlation silently breaks |
| `TrackingComponentRegistry` | registered components invisible to the reader copy |
| `TestPhaseContext` | Setup/Action phase mis-detected |
| injectable `IdGenerator` / `Clock` | deterministic mode set on one copy, ignored by the other → golden tests flake (§6.1) |
| global `ReportConfigurationOptions` | config set in test setup ignored by the generator |

The ALS-instance split is arguably nastier than the logger split: it fails by *mis-attributing* rather than *losing*, so the report looks plausible but wrong.

**Decision — a `globalThis` symbol registry, version-keyed by major.** Store every singleton as **plain data on `globalThis`**, keyed by a **global registry symbol** that encodes the package + its major version. Both the ESM and CJS copies resolve the *same* underlying object. This is exactly how **`@opentelemetry/api`** survives multiple installed copies (`Symbol.for('opentelemetry.js.api.1')`) — the canonical precedent for "an API that must be a singleton regardless of how many copies are loaded."

```ts
// @kronikol/core/internal/global-state.ts
import { AsyncLocalStorage } from 'node:async_hooks';

// Symbol.for() uses the PROCESS-GLOBAL symbol registry — equal across ESM & CJS copies.
// A plain Symbol('…') would NOT be equal and would defeat the whole mechanism.
// The "@v1" suffix is the MAJOR version: within a major, the shape is semver-stable;
// a different major gets its own slot (and we warn — see below).
const KEY = Symbol.for('@kronikol/core@v1/state');

interface KronikolState {
  logs: RequestResponseLog[];
  correlation: Map<string, TestIdentity>;
  als: AsyncLocalStorage<TestIdentity>;
  registry: Map<string, TrackingComponent>;
  config: ResolvedReportOptions;
  idGenerator: IdGenerator;
  clock: Clock;
  copies: number; // diagnostic: how many module copies initialized this realm
}

export function state(): KronikolState {
  const g = globalThis as Record<symbol, unknown>;
  let s = g[KEY] as KronikolState | undefined;
  if (!s) {
    s = { logs: [], correlation: new Map(), als: new AsyncLocalStorage(),
          registry: new Map(), config: defaultReportOptions(),
          idGenerator: defaultIdGenerator, clock: systemClock, copies: 0 };
    g[KEY] = s;
  }
  s.copies++;            // each copy that loads bumps this — surfaced by __diagnostics()
  return s;
}
```

**The rules that make this correct (each is a real footgun if violated):**
1. **`Symbol.for`, never `Symbol(...)`** — only the global registry is shared across the ESM/CJS realms.
2. **`globalThis` holds plain *data* (arrays, Maps, the ALS instance, config), never class *instances* with methods.** Each copy has its own copy of the *code* (classes/functions) that operates on the shared data — that's fine and unavoidable. So `RequestResponseLogger.log()` is a thin free function over `state().logs`; the data is shared, the code is duplicated.
3. **Always read through the accessor at call time** (`state().logs`), never cache the array/Map reference in a copy-local variable — a cached reference can survive a `clear()` and diverge. `clear()` truncates in place (`state().logs.length = 0`) rather than reassigning, so any incidental references stay valid.
4. **Never use `instanceof` across package boundaries.** A `RequestResponseLog` built by the ESM copy is **not** `instanceof` the CJS copy's class (§3.3 already pushes plain objects + structural typing — reinforce: `log()` accepts structurally-typed input; data-model identity is by shape/tag, never by `instanceof` or a local `Symbol`).
5. **Version the key by major.** Within a major, semver guarantees a compatible `KronikolState` shape, so two minor/patch copies safely share one slot. A *different major* present in the graph gets its own slot (genuinely two sinks) — a user misconfiguration we **warn loudly** about at init (mirroring OTel's incompatible-version warning), rather than corrupt data silently.

**Per-realm semantics — and why they're exactly right (links §5).** `globalThis` is per-**realm**: a fresh one per OS process *and* per `worker_thread`. So:
- *Within one worker:* the registry unifies the ESM+CJS copies → **one sink per worker.** ✓
- *Across workers/threads:* separate `globalThis` → separate sinks → **one complete fragment per worker.** This is precisely §5's model, achieved for free — ALS doesn't cross those boundaries anyway (§3.2), and cross-worker aggregation goes through fragment files, not shared memory.
- **Consequence for Vitest's *default* `threads` pool:** each test file runs in a `worker_thread` with its own `globalThis` → its own sink. So the `threads` pool needs the **same per-file fragment emission** as the `forks` pool (the merge just collects more fragments). This must be explicit in the run-lifecycle spike (§5.4), not assumed.

**Verification — the hazard is silent, so test it deliberately (post-build, not unit):**
- A `js/internal/dual-package` integration test that, **against the built artifacts** (the real `exports` map — the hazard cannot reproduce from `src`), loads `@kronikol/core` via **both** `import` and `require` in one process, logs through one handle, reads through the other, and asserts they observe the same `logs`/`als` state. This is the canonical regression guard.
- A `__diagnostics()` export returning `state().copies` (and which majors are registered) — turns "my report is empty" support tickets into a one-line check, and is **surfaced as the dual-package-split detector in the §3.18 Diagnostics report**.
- **CI gates on `exports`-map correctness:** run **`@arethetypeswrong/cli` (attw)** + **`publint`** on every package — a malformed/мis-ordered `exports` map (wrong condition order, missing `types`) is itself a cause of a tool picking CJS for one import and ESM for another *within a single build*, manufacturing the hazard. Condition order is fixed: `types` → `import` → `require` → `default`, plus `main`/`module` for legacy bundlers.

**Related dual-format rules (fold in):**
- **Inline embedded assets as strings at build time** (tsup `loader: { '.js':'text', '.css':'text' }`) rather than reading them via `fs`/path at runtime — `import.meta.url` (ESM) vs `__dirname` (CJS) resolve differently and would be a second dual hazard for the §4.2 externalized assets. Inlining sidesteps it entirely.
- **No top-level `await` in `@kronikol/core`** — it would break `require(ESM)` interop and the synchronous logging API.

**Escape hatch (deferred, not now).** Node 20.19+/22.12+ support `require(ESM)` for synchronous graphs, which is steadily removing the *reason* to ship CJS at all. When the supported floor makes `require(ESM)` universal, **`@kronikol/core` can go ESM-only and the hazard disappears by construction.** Until then we ship dual and rely on the registry. Tracked in Appendix B; the registry is designed so dropping CJS later is a no-op for consumers.

**Net.** A known, precedented pattern (OTel's exact approach) neutralizes the footgun, *and* its per-realm semantics give one sink per worker — exactly the isolation §5's per-file fragment model builds on. The residual work is discipline (the five rules), one post-build regression test, and two CI linters (attw + publint). Promoted from a Phase-4 afterthought to a **Phase-0/Phase-1 core-design invariant** (§9).

### 3.11 HTTP-client interception (deep dive) — two transport hooks, not one seam
.NET's single `DelegatingHandler` covers every `HttpClient` call. Node has **no universal seam**, and the modern default actively defeats the obvious one: **global `fetch` (Node 18+) is implemented by `undici` on raw sockets and does *not* go through `node:http`** — so patching `http.request` (the classic nock/OTel approach) **silently misses all `fetch` traffic**. `@kronikol/http` is the *first* ingestion adapter (Phase 4 critical path), so this has to be right from the start.

**The landscape — two disjoint transport stacks.**
| Client | Underlying transport | `node:http` patch | undici interceptor |
|---|---|---|---|
| global `fetch`, `undici.request/fetch`, `graphql-request`, newer `gaxios` | **undici** | ❌ | ✅ |
| `axios` (default Node adapter), `got`, `node-fetch` (npm), `superagent`, `needle` | **node:http** | ✅ | ❌ |
| `@aws-sdk/*` v3 (default `NodeHttpHandler`) | node:http | ✅ | ❌ |
| `@aws-sdk/*` v3 (fetch handler) / `@azure/*` (`core-rest-pipeline` default) | undici / node:https | ✅/❌ varies | ✅/❌ varies |

**Finding: the minimal *complete* cover is exactly TWO transport-level hooks** — and a single request traverses *one* stack or the other, **never both**, so the two hooks are **disjoint by construction and cannot double-count each other**:
1. **undici / `fetch`:** a composable **`Dispatcher` interceptor**, `compose()`d onto the global dispatcher (`setGlobalDispatcher`) while **preserving any existing one** (MSW, proxy agents — never *replace*). Its handler captures request + response incl. **bodies** (buffering bounded chunks while passing them through) and stamps identity headers. (undici also publishes `undici:request:*` on `node:diagnostics_channel` for lifecycle/timing — good for metadata, but the interceptor is what makes body-capture-with-pass-through clean.)
2. **node:http / node:https:** an **idempotent monkeypatch** of `request`/`get` on both modules — wraps outgoing body writes and **tees the response `IncomingMessage`** (capture chunks, re-emit so the app still reads them), stamps identity headers. (Node core also publishes `http.client.*` diagnostics channels for lifecycle.) **The "already-patched" flag lives in the §3.10 `globalThis` registry** so exactly one patch is installed across the ESM+CJS copies — a direct payoff of that design (double-patching would double-count *within* the http stack).

**Transport-level by default; library hooks opt-in.** Capturing at the transport layer catches the whole ecosystem **once** and avoids the double-count that stacking a library interceptor (`axios.interceptors`, `got` hooks) *on top of* the transport hook would cause. Offer library-level adapters only as an **opt-in alternative** (mutually exclusive with transport mode) for users who want *logical*-request semantics (baseURL, pre-retry) instead of physical wire traffic.

**Semantic difference to document.** Transport-level sees **physical** requests — a got/axios retry surfaces as multiple interactions. That's arguably *more* faithful to Kronikol's "real dependency interactions" promise than .NET's handler-level (pre-retry) view, but it differs — call it out in the wiki.

**Every hook is dual-purpose: capture + propagate.** Each interception point both **captures** (Seam A → `RequestResponseLogger.logPair`, naming the target participant via the `resolveServiceName`/host-map cascade of §3.15) and **propagates** — read ambient identity from `AsyncLocalStorage` (§3.2) and stamp `test-tracking-*` headers onto the outgoing request. This is the analog of .NET's `TestTrackingMessageHandler` and the **client half of §7's client→server identity flow** (and it's what feeds §5's mode-3 out-of-process E2E attribution).

**The genuinely hard part is body capture, not lifecycle.** Headers/status/timing are easy (diagnostics channels). Capturing bodies **without consuming them** requires **teeing** the response stream (undici interceptor `onData`; wrap the http `IncomingMessage`) and buffering the request body — all **bounded by `maxContentLength`** (Seam A). Streaming/SSE/`Upgrade` responses never "complete" → cap capture and mark as streaming.

**Coexistence with mocking.** Compose with MSW/nock/proxy dispatchers and chain http patches; never replace a global dispatcher. In integration tests that mock, the observed response *is* the mock — Kronikol records **what the app actually saw**; document this (it's usually the desired behavior for integration-level diagrams).

**Determinism.** Stamp request/response timestamps from the injected `Clock` (§6.1), not `Date.now()`, so golden fixtures (§6) stay stable.

**Known gaps (note, defer).** `node:http2` clients (gRPC has its own interceptors, §3.4); raw WebSocket payloads. Out of scope for the first adapter; revisit in Phase 5.

**Version-sensitive items — now CONFIRMED empirically (Node v25 + undici 8.5.0, App. C):** the `undici:request:*` / `undici:client:sendHeaders` / `undici:body:*` and `http.client.request.*` / `http.client.response.finish` channels are all subscribable, and `Agent.compose` + built-in interceptors exist. The two-hook design is validated, not assumed.

**.NET / Java refs:** `src/Kronikol/Tracking/TestTrackingMessageHandler.cs` (outgoing stamping) + the HttpClient DelegatingHandler (capture); Kronikol4J `kronikol4j-http` (`HttpExchangeRecorder.java`, `HttpTrackingOptions.java`).

**Net.** One .NET seam → **two disjoint Node transport hooks** (undici `Dispatcher` interceptor + idempotent `node:http`/`https` monkeypatch) is the minimal complete cover; they can't double-count each other (one request, one stack); the §3.10 registry guarantees a single install; **body-capture-with-pass-through is the real work** (teeing, bounded); and each hook doubles as the client-side identity propagator (§7, §5 mode 3). Bounded and well-understood — but materially more than the one-line §3.4 row implied.

### 3.12 Database tracking (deep dive) — driver-level is universal; Prisma is the sole bypass
**Risk #4: the one area genuinely harder than Java.** Java/.NET have a universal seam — **JDBC** (`DataSource`/`Statement`) / **ADO.NET** (`DbCommand`) + EF's `DbCommandInterceptor` — so two hooks cover the relational world. **Node has no common driver interface:** `pg`, `mysql2`, `better-sqlite3`, `mssql`, `oracledb`, `node:sqlite` each have their own API, pool objects, and result shapes → tracking is **per-driver**. The deep dive finds the surface is far smaller than "per-driver × per-ORM" first suggests.

**Interception layer — pick the public driver API (not wire, not ORM).**
- ❌ **Wire-protocol sniffing** (hook `net.Socket`, parse the PG/MySQL protocol — the APM/eBPF approach): TLS-opaque, one parser per DB protocol, params hard to reconstruct, zero semantic context. Rejected.
- ✅ **Driver public-API wrap** (`Client.query` / `connection.execute` / …): deep capture (SQL + params + timing + rowcount + bounded rows), works through TLS, idiomatic, **bind-at-call-site** identity (§3.2). The primary hook.

**The decisive finding — driver-level transparently covers (almost) every ORM.** Query-builders/ORMs that call a *real* JS driver are captured for free by the driver wrap — the SQL flows through `pg`/`mysql2` no matter how it was built:
| ORM / builder | Talks to DB via | Driver wrap captures it? |
|---|---|---|
| Knex, TypeORM, Sequelize, Kysely, Drizzle, MikroORM | the real JS driver | ✅ (no dedicated adapter needed) |
| **Prisma** (default **Rust query engine**) | **its own engine — bypasses the JS driver** | ❌ **needs `@kronikol/prisma`** |
| Prisma + opt-in `driverAdapters` (`@prisma/adapter-pg`) | JS driver | ✅ but still prefer `$extends` |

So "support all the ORMs" is **not** N adapters — it's **one driver layer + one Prisma exception.** Prisma's default engine is a separate binary that opens its *own* connection, so wrapping `pg` sees nothing; hook **`prisma.$extends({ query: { $allOperations(...) } })`** (modern, supported) — execute via the provided `query(args)`, capture model/operation/args/timing; `$on('query')` (`log:['query']`) is a secondary raw-SQL/timing source.

**Double-count rules (two distinct traps):**
1. **Pool → connection re-entrancy.** `pool.query()` internally checks out a connection and calls `connection.query()`; wrapping *both* levels logs one query twice. Fix: wrap the **single choke point** per driver (e.g. pg's `Client.prototype.query`, through which both `pool.query` and direct `client.query` flow) and/or a **re-entrancy guard** that suppresses nested capture where the API allows calls at multiple levels.
2. **Driver + ORM stacking.** Driver-level is the **single source of truth**; ORM-level hooks (TypeORM `Logger`, Sequelize hooks, Knex `query` events, Drizzle `logger`) are **opt-in enrichment that *replaces* (not stacks on) driver capture** for that connection — the same mutual-exclusivity doctrine as §3.11's transport-vs-library. Default: ORM hooks off, driver-level on.

**Each per-driver adapter must handle** (the real, bounded work): promise **and** callback APIs; pooled vs direct (trap #1); parameterized/prepared statements (capture SQL + separate params); **cursors/streams** (`pg-query-stream` — capture at start, complete at stream end, bounded); transactions (`BEGIN`/`COMMIT`/`ROLLBACK` as ops); **connection-string + bind-param redaction at capture** (Seam A — DB params/connection strings are the most security-sensitive surface; route through the §3.13 capture-time redactor); operation **classification** (SELECT/INSERT/…/DDL/tx — port Kronikol4J's `SqlOperationClassifier`); and the "already-wrapped" flag in the **§3.10 `globalThis` registry** (one patch across ESM+CJS). `postgres` (porsager) and `node:sqlite` are separate small adapters from `pg`/`better-sqlite3`.

**OTel as the breadth multiplier (ties §3.8).** `@opentelemetry/instrumentation-{pg,mysql2,mongodb,redis,ioredis,cassandra,…}` already monkeypatch each driver and emit `db.statement`/`db.system` spans. The `@kronikol/opentelemetry` bridge can turn DB spans into **shallow** `RequestResponseLog` entries (SQL + timing + status; **no rows, params often disabled for security**) — wide coverage, low fidelity, **opt-in**. So: **native adapters = deep capture for priority drivers; OTel = shallow catch-all for the long tail** (couchbase, cassandra, …) with zero per-driver code.

**Already covered elsewhere — don't rebuild.** Several "databases" are HTTP under the hood — **Elasticsearch, CouchDB, Atlas Data API, Cosmos-over-REST** — so the §3.11 HTTP transport hooks already capture them; add only client-event enrichment if wanted.

**.NET refs:** `Kronikol.Extensions.EfCore.Relational` (DbCommandInterceptor) + per-driver `Npgsql`/`SqlClient`/`MySqlConnector`/`Sqlite`/`Oracle`/`Dapper`. **Java ref:** `kronikol4j-jdbc` (`SqlTracking.java`, `SqlOperationClassifier.java`) — but Java had *one* JDBC hook; Node's per-driver reality is exactly why this is risk #4.

**Net.** Confirmed harder than Java, but **de-risked to a bounded breadth grind, not an architectural unknown.** The surprise upside: **driver-level public-API wrapping is the universal primary hook and transparently covers Knex/TypeORM/Sequelize/Kysely/Drizzle/MikroORM** — "all the ORMs" collapses to **one driver layer + the single Prisma `$extends` exception**, with **OTel** as a shallow long-tail catch-all and **HTTP-based DBs already handled by §3.11**. Real work = ~5 SQL driver adapters (pg first, Phase 4), each small, with concrete sharp edges (pool re-entrancy, promise/callback/cursor, redaction, driver-vs-ORM exclusivity).

### 3.13 Security & redaction parity (deep dive) — the .NET model is *presentational*; true redaction must move to capture
Reading the actual rules (`RequestResponseLogger.cs`, `ReportConfigurationOptions.cs`, `PlantUmlCreator.cs`) shows the .NET security model is **thinner and far more presentational than "redaction" implies** — a finding that directly contradicts the optimistic one-liner this used to be (in §15).

**What .NET actually does (the complete inventory):**
| Mechanism | Where applied | Default | Covers which outputs? |
|---|---|---|---|
| **`MaxContentLength`** truncation (`RequestResponseLogger.Log`) | **at capture** (Seam A) | `null` (no limit) | **all** — propagates everywhere incl. the worker fragment ✅ |
| **`ExcludedHeaders`** (`ReportConfigurationOptions`) | **diagram-render time** (`PlantUmlCreator.cs:597`) | `[]` (empty) | **PlantUML note ONLY** — header stays in the data model |
| `DefaultExcludedHeaders` = `Cache-Control`,`Pragma` | render, *only when the list is `null`* | n/a (config passes `[]`, so unused) | note only |
| `excludeAllHeaders` flag | render | — | note only |
| **`RequestResponseMidProcessor` / `PostProcessor`** (`Func<string,string>`) | render/format time | none (user-supplied) | wherever the processor runs — **not capture** |
| Built-in secret / token / password / connection-string redaction | — | **none exists** | — |

**The #1 finding — exclusion ≠ redaction; secrets leak into the data outputs.** Header exclusion and the content processors run **at diagram-render time, not at capture**, so the raw `RequestResponseLog` keeps full headers + content. An "excluded" `Authorization` header **still appears in the JSON/YAML/XML report and — critically — in the `GenerateMergeableData` fragment** each worker writes to disk (§5). And there is **no built-in redaction** of auth headers, bearer tokens, passwords, API keys, or SQL connection strings — so with default config (`ExcludedHeaders` empty) **secrets render in the diagram too**. The .NET model is "the user opts into cosmetic, diagram-only hiding"; it does **not** keep secrets out of artifacts. *(So the §15 one-liner "redacted secrets never appear in any emitted artifact" was aspirational, not a description of .NET behavior — corrected below.)*

**The design fork for the Node port (parity vs. security):**
- **Replicate the presentational layer for diagram golden-parity** — `excludedHeaders` + content processors applied at render so the **PlantUML/HTML matches .NET exactly** (§6.6).
- **Add capture-time redaction at Seam A** — the *only* point that covers **every** output (PlantUML, HTML, JSON, YAML, XML) **and the worker fragment** (§5) **and** cross-worker IPC. Implement it in `RequestResponseLogger.log()`, right where `MaxContentLength` already truncates:
  ```ts
  redactAtCapture?: {
    headers?: string[];                  // removed/masked before the log enters the sink
    content?: (body: string) => string;  // deterministic body redactor (mask, never hash)
  }
  ```
  Running at capture means a redacted secret is gone from the data model and therefore from **all** artifacts — the real requirement.
- **Two modes, explicitly documented:** *parity mode* (presentational only — matches .NET; secret persists in data; used for golden PlantUML/HTML) and *redact-at-capture mode* (secret removed everywhere; the JSON golden then **intentionally differs** from .NET — assert *security* here, *parity* there).

**Hardening the port should add (net-new — .NET has none):**
- An **opt-in "secure preset"** that redacts common sensitive headers (`Authorization`, `Cookie`, `Set-Cookie`, `Proxy-Authorization`, `X-Api-Key`, …) + token/connection-string patterns **at capture** — **off by default** (empty default preserves golden parity; document loudly that the secure default is opt-in).
- **DB adapters (§3.12) redact connection-string passwords and (optionally) bind-parameters at capture** — the most sensitive surface and the one the SQL adapters touch first; route through the same capture-time redactor.

**Determinism (feed §6).** Redactors must be **pure and deterministic** — mask to a fixed token (`***`), or use a **deterministic content-keyed hash** (never a random/salted one), so golden fixtures (in redact mode) stay stable.

**Redaction modes to support (from the wiki).** Beyond full masking, port the .NET recipes: **truncation** (`maxContentLength`, already core), **header exclusion** (above), and **hash-based token shortening** — replace a long token with a short *deterministic* hash (`Bearer eyJ… → Bearer #a3f29c01`) so **token *reuse* is visible across diagram arrows without exposing the value** (same token → same short hash). Node: a fast content hash (a non-crypto 64-bit hash → 8 hex, or `sha256`[..8]); deterministic ⇒ golden-stable.

**Centralization is the architectural call.** Put the redactor in **`@kronikol/core` (Seam A)** so every adapter inherits it and every output + the fragment is covered — *not* per-adapter, *not* at render. This single change turns the §15 promise from aspiration into a **testable guarantee**.

**.NET refs:** `RequestResponseLogger.cs:22–28` (capture truncation), `ReportConfigurationOptions.cs:44–45` (`ExcludedHeaders`) + `:17–21` (processors), `PlantUmlCreator.cs:28,61,597` (render-time exclusion).

**Net.** The deep dive overturned the one-liner: .NET's "redaction" is **presentational and opt-in**, leaving secrets in the JSON/mergeable outputs and nothing redacted by default. The Node port keeps the presentational layer for parity **and** adds **capture-time redaction at Seam A** (the only complete-coverage point), an **opt-in secure preset**, and **DB connection-string/param redaction** — with a documented parity-vs-security mode split. Security tests assert "no secret in any artifact" **in redact-at-capture mode** (the honest, achievable claim).

### 3.14 Step capture & phase detection (deep dive) — agnostic engine + tiered capture (§3.9's sibling)
BDD-style steps (Given/When/Then, sub-steps, tabular params) and the Setup/Action phase feed the report (`ScenarioStep`, §6.7) and verbosity filtering. Reading `StepCollector.cs` + the per-framework decorators shows a clean split: a **framework-agnostic engine that ports verbatim** + a **per-framework capture layer that is tiered exactly like assertion tracking (§3.9)**.

**The engine (`StepCollector`) ports cleanly to `@kronikol/core`.** It's a `ConcurrentDictionary<testId, state>` (data-keyed, parallel-safe — the §3.2 correlation pattern) where each test has a **stack** of in-progress steps + a list of completed ones:
- `startStep(keyword, text, params)` resolves `testId` from ambient context (ALS, §3.2), pushes a step; **a step started while another is active becomes its sub-step** (nesting via the stack).
- `completeStep(passed, error)` pops, times it, attaches to parent's `subSteps` or the top-level list. `bypassStep(reason)` → `Bypassed`.
- `addAssertionSubStep(testId, expr, passed)` — **tracked assertions (§3.9) become sub-steps of the active step** (the two systems interlock).
- `addAttachment`, `getSteps(testId) → ScenarioStep[]`, `clearSteps`. All a `Map` + array-stack + tree-walk → ports verbatim.
- **Keyword sequencing:** consecutive same-category keywords collapse to **"And"** (Given, Given→"And", Given→"And"); `ButWhen`→"But"; sub-steps keep their original keyword. A real display rule to replicate.

**Phase detection has two sources — and the non-BDD one was unstated.**
1. **BDD keyword → phase** (in `startStep`, when `whenTriggersAction`): `Given`/`But` → **Setup**; `When`/`Then`/`ButWhen` → **Action**. Sets `TestPhaseContext.current` (ALS-backed, §1 Seam B); trackers read it via `PhaseConfiguration.shouldTrack(setup, action)` for verbosity filtering.
2. **Non-BDD adapter lifecycle → phase** (the primary path for Vitest/Jest/node:test/Mocha, which have no Given/When/Then): the **adapter maps the runner's setup hooks to Setup and the test body to Action** — wrap `beforeAll`/`beforeEach`/fixtures in `TestPhaseContext=Setup`, the test fn in `Action`. This gives correct phase **without steps**, and replaces .NET's brittle "left the Given section" HTTP-handler heuristic (`InjectImplicitActionStartIfNeeded`). Cleaner in Node; state it explicitly.

**Capture is tiered per-framework — and .NET's IL-weaving maps to a build-transform (the §3.9 sibling), NOT a core requirement:**
| Source | .NET mechanism | Node mechanism | Tier |
|---|---|---|---|
| **Explicit step API** | `Track`/manual | **`step(keyword, text, async fn)`** — awaits fn, calls `completeStep` on resolve/reject (native to JS; cleaner than .NET's `CompleteStepAsync` Task-wrapping) | **0 — always available** |
| **BDD framework steps** | ReqNRoll/BDDfy/LightBDD decorators | **Cucumber-js / Playwright: consume Cucumber Messages** — `--format message` / playwright-bdd's `cucumberReporter('message')` carry the whole Gherkin model (feature description, rule, background, keyword, `PickleStep.type`, data table, doc string, outline example values, status, exception, attachments, retries). **The same converter as the .NET importer** (`Kronikol.Ingestion.Cucumber`), sharing its golden test fixtures; plus **Playwright** native `test.step()` → hook where no messages file exists | **1 — free where the runner has steps** |
| **Auto-capture from `[Given]`-style markers** | **`Kronikol.StepTracking` MSBuild IL-weaver** | **build-time transform** (Babel/SWC/ts plugin) — the *same* tooling as the §3.9 Tier-2 assertion transform | **2 — deferred/optional** |

**Cucumber Messages is the BDD seam, not a per-runner adapter (parity note, K-C1).** The .NET side ships `Kronikol.Ingestion.Cucumber` — `CucumberMessagesReader` (envelope NDJSON → typed envelopes; unknown envelope types and malformed lines counted, never fatal) + `CucumberFeatureSynthesizer` (envelopes → `Feature[]`, plus `start`/`step`/`end` markers so the Gherkin steps travel the ordinary ingest path) + `CucumberFeatureMerger` ("messages win for structure", the reporter still supplies assertions/UI/attachments/identity), reached by `kronikol ingest --cucumber-messages <file>` / `IngestRequest.CucumberMessagesFiles`. The Node port should implement **the same converter against the protocol**, not one adapter per runner — and reuse the checked-in golden fixture (`tests/Kronikol.Tests/TestData/Cucumber/playwright-bdd-9.2-messages.ndjson`, protocol 32.2.0) as its own test input, so the two ports provably agree. `@kronikol/playwright` additionally emits `attachment` events and the `kronikol-test-id` attachment that joins a scenario to its captured traffic. The version-coupled `// bdd-data-start` block playwright-bdd writes into its generated specs (`pwTestLine`, `pickleLine`, `tags`, `steps[]` with `pwStepLine`/`gherkinStepLine`/`keywordType`/`textWithKeyword`/`isBg`/`stepMatchArguments`) is the *fallback* source when no messages file was produced — guard it by shape and degrade to plain behaviour.

So most non-BDD Node tests simply have **no steps** (steps are optional; scenarios still capture interactions + the lifecycle-derived phase). Cucumber-js and Playwright get steps natively; the explicit `step()` API covers anyone else; IL-weaving's auto-capture becomes an optional build transform shared with §3.9.

**Gherkin Background extraction (§3.24).** A deterministic string algorithm (`BackgroundStepsDetector`) extracts a step prefix shared across scenarios in a `Rule` group (≥2 step-bearing members) into each scenario's `backgroundSteps`, trimming it from `steps` — with guards (open-with-`And`/`When` skips; zero-length prefix skips; remaining-step-reopening-with-`Given`/`When` skips). Ports as pure logic; covered by a `report-background` golden.

**Interlocks (don't re-port in isolation):** assertions→sub-steps (§3.9); `testId` resolution via ALS (§3.2); **tabular/complex-param capture** reuses §4.5's `ParameterParser` (incl. the C#-record-`ToString()` `[TypeName]` truncation that **has no Node analog** — same caveat as §4.5); step **timing from the deterministic `Clock`** (§6.1), not `performance.now()`, for golden stability.

**.NET refs:** `StepCollector.cs` (engine + keyword sequencing + phase), `TestPhaseContext.cs`/`PhaseConfiguration.cs`/`TestPhase.cs`, `StepTrackingOptions.cs`; weaver `Kronikol.StepTracking/StepWeaver.cs`; BDD decorators `Kronikol.BDDfy.xUnit3/`, `Kronikol.LightBDD.Core/StepTrackingStepDecorator.cs`, `Kronikol.ReqNRoll.Core/`.

**Net.** The `StepCollector` **engine + phase model are framework-agnostic core and port verbatim** (Map + stack + ALS phase). The capture is **tiered like §3.9**: explicit `step()` (Tier-0, free), native Cucumber-Gherkin / Playwright `test.step` (Tier-1, free), optional build-transform replacing IL-weaving (Tier-2, deferred). The genuinely new clarity: **phase comes from the adapter lifecycle (setup-hooks→Setup) for non-BDD runners**, not from steps. Engine + phase land in **Phase 1** (core); the `step()` API + Cucumber/Playwright capture land **per-adapter in Phase 4/5**; Tier-2 is demand-driven. Bounded; interlocks with §3.9/§3.2/§4.5/§6.1.

### 3.15 Participant (service) naming (deep dive) — per-adapter, and *simpler* in Node
Every tracked interaction carries a **`callerName`** (originating service — the left diagram participant) and a **`serviceName`** (target — the right participant); these *are* the sequence-diagram boxes. Reading `TestTrackingMessageHandlerOptions` + `ResolveServiceName` (`:63–105`) shows the .NET HTTP cascade and where Node simplifies.

**.NET HTTP cascade (`ResolveServiceName(port)`):** (1) `FixedNameForReceivingService` → (2) `ClientNamesToServiceNames` exact (the `IHttpClientFactory` client name) → (3) the same with **Refit suffix/`Contains` matching** (strip assembly qualification, `EndsWith` w/ boundary, `Contains` if assembly-qualified) → (4) `PortsToServiceNames` (port→name) → (5) fallback `localhost:{port}`. `callerName` is config (default `"Caller"`).

**Node simplifications + changes:**
- **Drop steps 2–3 (client-name / Refit matching) — no Node analog.** Node has no `IHttpClientFactory` named clients or Refit typed-client interfaces, so the most intricate part of the cascade (the suffix/`Contains`/assembly-qualification matching) **doesn't port** — a real simplification.
- **Port-map → host/port-map.** .NET keys by *port* (ASP.NET TestServer binds ports); Node URLs carry full hosts → map by **`host`/`host:port`** (`hostsToServiceNames`), and **fall back to the URL host** (more useful than `localhost:{port}`).
- **Add a `resolveServiceName(url, req) => string` custom resolver** as the idiomatic override — subsumes the maps and lets users name by host/path/header. Default cascade: `fixedName` → host/port map → host.
- **`callerName`** = the system-under-test (e.g. `"BreakfastProvider"`); default `"Caller"`.

**Naming is per-adapter (the broader point).** `serviceName` is set by *each* tracker, not just HTTP: **SQL (§3.12)** names the participant from the connection (DB system/name, e.g. `"PostgreSQL"`); **Redis/Mongo/Kafka** from the resource (host/topic); HTTP via the URL resolver above. Each adapter owns a `serviceName` default + config knob; the `serviceName`/`callerName` fields on `RequestResponseLog` (Seam A) are the stable contract.

**Diagnostics — the unmapped-target registry.** .NET's `UnmatchedClientNameRegistry` records names that matched no mapping, surfaced in the diagnostic report. Node analog: a registry of **targets that fell back to a raw host** → "these N targets weren't named; add them to your map" — pairs with the §3.10 `__diagnostics()` and the broader Diagnostics feature (Appendix B).

**Config that ports with Node-appropriate defaults:** `excludedHosts` (skip infra hosts — drop .NET's `"override.com"` default, use a Node-relevant set or empty); `trackDuringSetup`/`trackDuringAction` (phase-based tracking filter, §3.14); `headersToForward`.

**.NET refs:** `TestTrackingMessageHandlerOptions.cs`, `TestTrackingMessageHandler.ResolveServiceName` (`:63–105`), `UnmatchedClientNameRegistry.cs`.

**Net.** Participant naming **simplifies in Node** (drop the Refit/`IHttpClientFactory` client-name matching) and **generalizes** to a per-adapter `serviceName` — HTTP via a `resolveServiceName`/host-map cascade, DB/cache/messaging via the resource — with `callerName` for the SUT and an **unmapped-target diagnostics registry**. Small; HTTP-adapter + config scoped; no architectural risk.

### 3.16 Internal-flow / OTel → activity + flame diagrams (deep dive) — capture + render, two halves of one feature
*(This single subsystem **is** both "internal-flow" *and* "OTel→flame/activity" — capture via spans, render as activity/flame — so they're addressed together.)* It visualizes a test's **internal execution** (the spans inside the SUT *between* HTTP boundaries) as a PlantUML **activity diagram** + a **flame chart**, shown in a popup/inline on a step. Phase 5, gated on `@kronikol/opentelemetry` (§3.8).

**Pipeline (`InternalFlow*`):** capture (`InternalFlowActivityListener` → `InternalFlowSpanStore`) → **segment** (`InternalFlowSegmentBuilder` groups spans **between two consecutive HTTP boundaries**, keyed by TestId + RequestResponseId → `InternalFlowSegment`) → **render** (`InternalFlowRenderer` builds a span **tree** → PlantUML activity diagram with swimlanes + a flame chart, **batched ≤3×≤100 spans** with "Part N of M") → **embed** (`InternalFlowHtmlGenerator`, popup/inline, click/hover).

**Capture: `.NET ActivityListener` → OpenTelemetry-JS `SpanProcessor`.** The Node analog is an **in-memory `SpanProcessor`** (`onEnd(span)` → span store) registered on the OTel tracer provider, in `@kronikol/opentelemetry`. Two Node-specific notes:
- **Cleaner than .NET in one way:** the .NET listener must *exclude* `System.Net.Http` to avoid breaking Application Insights' `DiagnosticListener` path — **Node has no such conflict**; the `SpanProcessor` purely observes.
- **Higher adoption bar:** .NET's `ActivitySource` is in the BCL, so ASP.NET/EF Core emit spans **for free**; **Node has no built-in tracing**, so this feature **requires the SUT to have OpenTelemetry instrumentation installed** (`@opentelemetry/sdk-node` + auto-instrumentations). It's **opt-in and does nothing without OTel** — document loudly (the `InternalFlowNoDataBehavior`/`HasDataBehavior` options already model the no-data case).

**Span→test correlation composes with §3.2 for free.** OTel-JS's own context manager **is `AsyncLocalStorage`-based**, so a span created during a test already shares the Kronikol identity scope — read the testId from ALS at `onEnd` (or stamp it as a span attribute). The segment grouping (spans within a test's HTTP-boundary window) is then **pure logic**.

**Render = pure PlantUML + a client-side flame chart — both browser-only (§3.5). (Confirmed from source.)** The activity diagram is **pure string building** (`@startuml` activity + swimlanes from the span tree) — ports mechanically, golden-verified with the §6.5 discipline, compressed + embedded like sequence diagrams. The **flame chart is `flame-chart-render-script.js`** — a native-JS Bucket-A asset (ports for free) that renders bars in the browser from **compact server-emitted data** (`[…, left%, width%, …, durMs]`). So the server emits the **timing-derived percentages** (§6.1-dependent, but pure division → bit-reproducible, no trig) which *are* golden-verified, and the **client renders the pixels** (dodges any rendering-float concern). **Determinism caveat:** `InternalFlowRenderer` sorts spans by start-time with an **unstable `List.Sort`** (the *only* unstable sort in the .NET source) — Node's stable `Array.sort` **plus a tiebreaker** (span id) makes ties deterministic. Batching ports as logic.

**Determinism (§6.1):** span timings (wall-clock) + trace/span IDs are non-deterministic → drive them from the seeded `IdGenerator`/`Clock` + the §6.6 canonicalizer for goldens, exactly as for the sequence path.

**.NET refs:** `InternalFlow/InternalFlowActivityListener.cs` (capture), `InternalFlowSpanStore.cs`, `InternalFlowSegmentBuilder.cs` + `InternalFlowSegment.cs` (model), `InternalFlowRenderer.cs` (activity/flame/tree), `InternalFlowHtmlGenerator.cs` (embed); `InternalFlow*` options in `ReportConfigurationOptions`.

**Net.** "Internal-flow" and "OTel→flame/activity" are **one subsystem** (capture + render), now both scoped. Capture = an OTel `SpanProcessor` (cleaner than .NET — no AppInsights conflict — but **opt-in, gated on the SUT having OTel**, a higher bar than .NET's built-in `ActivitySource`); render = **pure PlantUML activity** (mechanical) + a **client-side flame chart** (free, dodges the §6.4 floats); correlation is **free via OTel's ALS context** (§3.2). Phase 5, in `@kronikol/opentelemetry` (§3.8); fits browser-only rendering; golden-verified like the sequence path.

### 3.17 CI integration (deep dive) — the most trivially portable subsystem
CI-environment detection, metadata capture, the markdown summary, and artifact publishing. Reading `CiEnvironment.cs`/`CiMetadata.cs`/`CiSummaryWriter.cs`/`CiArtifactPublisher.cs` shows this is **pure env-var + file-append + stdout-logging-command code with zero .NET-specific logic** — it ports to Node essentially **verbatim**, because the env-var names and CI logging conventions are language-agnostic *platform* contracts.

**The whole surface (2 platforms):**
- **Detect** (`CiEnvironmentDetector`): `GITHUB_ACTIONS` → GitHub Actions; `TF_BUILD` → Azure DevOps; else None.
- **Metadata** (`CiMetadata{Provider,BuildNumber,Branch,CommitSha,PipelineUrl,Repository,RunId}`): from platform env vars (`GITHUB_RUN_ID`/`GITHUB_SHA`/`GITHUB_REF_NAME`/… or `BUILD_BUILDID`/`BUILD_SOURCEVERSION`/…) + a computed pipeline URL. Embedded in the TestRunReport HTML.
- **Summary** (`CiSummaryWriter`): GitHub → append markdown to `GITHUB_STEP_SUMMARY`; Azure → temp file + `##vso[task.uploadsummary]`.
- **Artifacts** (`CiArtifactPublisher`): GitHub → write `reports-path`/`reports-retention-days` to `GITHUB_OUTPUT` (a workflow step uploads); Azure → `##vso[artifact.upload …]`.

**Node port:** read `process.env`, `fs.appendFileSync` to the `GITHUB_OUTPUT`/`GITHUB_STEP_SUMMARY` files, `console.log` the `##vso[…]` commands. **Same env-var names, same conventions** → near-verbatim. Mirror the .NET **injectable seam** (`getEnvVar`/`appendFile`/`writeLine`) for tests + determinism.

**Placement — the one real design point (ties §5).** These are **once-per-run** actions, so they belong in the **finalize/merge step**, not per-worker: the §5 merge owner (`globalTeardown`/reporter in mode 2/3, the self-finalize in mode 1, or `@kronikol/cli`) detects metadata, writes the summary, and publishes artifacts **after the final report exists**. §5.8 already reserves "CI summary/artifact publishing" as the once-per-run merge action — this is where it lands.

**Browser-only summary (§3.5).** .NET's summary inlined *server-rendered* diagram PNGs; browser-only has no server render → the Node summary **links to the HTML report artifact** + pass/fail counts + CI metadata (the deliberate behavior change from §3.5).

**Determinism (§6.1).** CI metadata (commit/branch/run-id) is embedded in the report HTML, so **golden mode must stub it** (`CiMetadata = null`/injected), or goldens differ per CI run. Fold into the determinism seam.

**Breadth opportunity (beyond parity).** .NET supports only GitHub Actions + Azure DevOps; Node teams also use **GitLab CI, CircleCI, Jenkins, Buildkite** (each with its own `CI_*` env vars + summary/artifact mechanisms). Parity = the two; the others are easy opt-in additions later.

**.NET refs:** `CiEnvironment.cs`, `CiMetadata.cs`, `CiSummaryWriter.cs`, `CiSummaryGenerator.cs`, `CiArtifactPublisher.cs`.

**Net.** The **simplest subsystem in the whole port** — env-var + file-append + logging-command ops that copy near-verbatim (language-agnostic platform contracts). The only decisions: **place it in the §5 finalize/merge step** (once-per-run), **stub CI metadata in golden mode** (§6.1), and **link-to-report** in the summary (browser-only, §3.5). Phase 5; optional GitLab/CircleCI/Jenkins breadth.

### 3.18 The Diagnostics report (deep dive) — the aggregation surface for every diagnostic hook
A **standalone, self-contained `DiagnosticReport.html`** (its own inline CSS — *not* the main report's assets) that answers "**why is my report empty / wrong?**" by aggregating tracking-health signals. Reading `DiagnosticReportGenerator.cs` shows it's **pure aggregation over already-captured data + HTML string-building** (no new capture) — it ports mechanically, and it's where the diagnostic hooks the *other* deep dives created get surfaced.

**What it reports (all read from existing structures):** a config dump; log summary (entries per service / per test); **⚠ unknown entries** (`testId == "unknown"` — unresolved identity, §3.2); **⚠ unpaired requests** (request with no response); **⚠ orphaned test IDs** / **scenarios with no logs** (empty-diagram cases); **activity-span sources** (tracked / well-known / not — §3.16); the **`TrackingComponentRegistry`** table (each tracker's instances, invocation count, active %, with a **⚠ "never invoked"** warning); **⚠ unmatched targets** (§3.15); and **assertion value-resolution** fallbacks (§3.9 `Track.DiagnosticLog`).

**It's the aggregation surface for hooks the other deep dives already added** — unresolved-identity (§3.2), unmapped targets (§3.15), span sources (§3.16), assertion diagnostics (§3.9), and the component registry (§1 Seam B / Phase 1). The load-bearing input is **`TrackingComponentRegistry`**: each Node adapter (HTTP, SQL, Redis, …) registers a `TrackingComponent` and increments `invocationCount` per tracked call; the **"registered but never invoked"** warning is *the* "why is my report empty" answer (wrong client, untracked pool, hooks not installed).

**Two net-new *Node* diagnostics to add (higher value than the .NET text they replace):**
- **The §3.10 dual-package check — the single most valuable Node-native diagnostic.** Surface `state().copies` from the `globalThis` registry: if `> 1`, **"⚠ `@kronikol/core` loaded N times (dual-package split) — interactions may be split across copies; check your `exports`/install."** This turns §3.10's *silent* failure into a visible one-line diagnosis — exactly the bug class this report exists for.
- **Node-specific "common causes."** Replace .NET's EF-Core/Duende guidance with Node failure modes: *the undici/`http` interceptor wasn't installed before the app made requests* (§3.11); *the `pg` Pool was created before tracking was set up*; *dual-package split* (above); *the SUT isn't on OpenTelemetry* (no spans, §3.16).

**Lower parity bar.** A **debug aid, not a parity-critical output** → structural tests suffice (no byte-golden HTML). Uses the `WebUtility.HtmlEncode` shim (§4.4); timestamps from the deterministic `Clock` (§6.1).

**.NET refs:** `DiagnosticReportGenerator.cs`, `ReportDiagnostics.cs`, `SqlDiagnosticTracker.cs`; inputs `TrackingComponentRegistry`, `UnmatchedClientNameRegistry` (§3.15), `InternalFlowSpanStore`/`ActivitySourceDiscovery` (§3.16), `Track.DiagnosticLog` (§3.9).

**Net.** A standalone tracking-health dashboard — **pure aggregation + HTML** over data the rest of the system already captures, so it ports mechanically with a lower parity bar. Its real Node value-add: it's the **surfacing point for `__diagnostics()` (§3.10)** and gains a **dual-package-split detector** that makes the #1 silent Node failure visible. Phase 5; consumes the registry + stores that exist by Phase 4.

### 3.19 Component diagrams & dependency analytics (deep dive) — a substantial subsystem the plan under-scoped
The plan reduced this to a §6.5 ordering footnote; reading `ComponentDiagram/*` shows it's a **C4-style architecture diagram + a full dependency-analytics engine derived from real test traffic** — **enabled by default** (`GenerateComponentDiagram=true`), so not optional. The largest feature the audit found under-scoped.

**What it computes (all pure math/graph over the captured `RequestResponseLog[]`):**
- **Per-edge (`RelationshipStats`, Caller→Service):** call/test counts; latency **mean/median/P95/P99/min/max**; **error rate**; **status-code** + **HTTP-method** distributions; **per-endpoint breakdown**; **payload sizes** (req/resp mean + P95 bytes); **concurrency** (concurrent-call detection + %); **coefficient of variation** (latency variance); **outlier detection** (count, threshold, top-N by deviations-from-mean); **latency-contribution %**; low-coverage flag.
- **Graph-level (`DependencyGraphMetrics`):** per-service **fan-in/fan-out** (+ inbound/outbound lists); **circular-dependency detection** (cycle-finding); **longest dependency chain**.
- **Cross-edge:** **call-ordering patterns** (how often A precedes B); **error correlation** (how often two edges fail together).
- **Rendering:** the **C4 PlantUML** diagram (participants classified service/broker/DB), **hotspot colouring** (`ArrowColorMode` — colour edges by latency/error), a **performance summary table** + **latency-distribution bar chart** in HTML, **interactive focus mode**, and a **diff mode** (`ComponentDiagramDiffer` — compare two runs, highlight added/removed/changed edges).

**Portability — mechanical, but determinism-bound:**
- ✅ **All the analytics port mechanically** — percentiles, CV, outliers, fan-in/out, **cycle detection**, longest-chain, call-ordering, error-correlation, concurrency are plain math + graph algorithms over the logs. No .NET dependency. Lands in `@kronikol/diagram` (a `component/` area). Golden-verifiable.
- ⚠️ **It leans *hard* on determinism (§6.1) — the single biggest consideration.** *Every* statistic is **timing-derived** (latency percentiles, outliers, CV, concurrency, ordering), so wall-clock durations make all of it non-deterministic. The **reproducible monotonic `Clock` (§6.1) is mandatory** for golden-stable component analytics — without it, none of these stats are golden-testable. This is the feature that most exercises the §6.1 seam.
- ✅ **Float math is bit-reproducible (unlike §6.4's trig).** Means/percentiles/`sqrt`-for-CV are IEEE-754-deterministic (`sqrt` *is* correctly-rounded, unlike `sin`/`cos`), so cross-runtime parity holds **given deterministic durations**; round *displayed* values to fixed precision; keep **bar/chart geometry client-side** (dodges §6.4 entirely).
- ✅ **C4 PlantUML rendering = pure string building** (§6.5 discipline: the `HashSet`→ordered-set ordering hazard already noted, invariant number formatting, ordinal sorts). The HTML analytics tables are Bucket B (templating); the charts + focus + diff interactivity are Bucket A client-side JS (port for free).

**Phasing:** the C4 diagram + analytics computation lands in **Phase 2** (pure, golden-verified, depends on the §6.1 deterministic clock); the HTML analytics tables/charts + focus/diff interactivity in **Phase 3**. Config (`ComponentDiagramOptions`: participant filter, dependency colours, custom labels, arrow-colour mode, title/theme) ports as options.

**.NET refs:** `ComponentDiagram/` — `ComponentDiagramGenerator.cs` (C4 PlantUML), `RelationshipStats.cs` + `DependencyGraphMetrics.cs` (analytics models), `ComponentRelationship.cs`, `ComponentDiagramReportGenerator.cs` (HTML tables/charts), `ComponentDiagramDiffer.cs` (diff mode), `ComponentDiagramOptions.cs`/`ArrowColorMode.cs` (config), `ComponentFlowSegmentBuilder.cs`.

**Net.** Far larger than the §6.5 footnote implied — a **C4 diagram + a mini-APM analytics engine** (percentiles, outliers, CV, payload/concurrency stats, fan-in/out, **cycle detection**, longest-chain, call-ordering, error-correlation, **diff mode**), all **pure computation that ports mechanically** but **leans hard on the §6.1 deterministic clock** for golden-stable timing stats. No architectural risk; bounded breadth; Phase 2 (compute + C4) → Phase 3 (HTML analytics + interactivity). Enabled by default.

### 3.20 Tabular attributes (deep dive) — rendering ports; the authoring DSL needs a Node redesign
A feature (`Kronikol.TabularAttributes`) that **batches many test cases into one test execution** — paying the per-test lifecycle (app boot, DI scope, setup/teardown) *once* while still exercising every case through the full stack — and renders each row's **expected-vs-actual in a table** with per-row diagram delimiters. Two halves: rendering (ports mechanically) + the authoring API (needs a Node redesign).

**The value-prop carries to Node.** Node component tests (`supertest`/`inject` against a real app) pay the same per-test overhead (`beforeEach`, app setup); batching N validation cases into one test is just as valuable. Not a .NET quirk — worth porting.

**Rendering ports mechanically (data model already in §6.7).** The report renders **input/output tables** with **expected-vs-actual cells + verification-status colouring** (pass/fail/not-applicable), a **tree view** for nested objects, **linked output**, and **per-row step delimiters** (`ShowStepDelimiters` hnotes, §3.14) separating each case's interactions in the sequence diagram. This is `ParameterValueRenderer` (tabular/tree/inline) + the step renderer — part of §4/§4.5 HTML assembly; the data model (`StepParameter` → columns/rows/cells/status/`isLinkedOutput`) is already captured by §6.7's `MapStepParameterJson`. Golden-verified HTML.

**The authoring API needs a Node redesign (the genuine net-new work).** .NET uses a **C#-attribute DSL** — `[HeadIn]`/`[HeadOut]`/`[Inputs]`/`[Outputs]` + `TabularInputs<T>`/`TabularOutputs<T>` parameters + **auto-verify** (assert actual == expected). TS has **no attribute analog**, so the Node shape is a **builder / data API**: e.g. `tabular().headIn('Email').headOut('Status','Error').row(input, expected)…` returning typed **iterable inputs + an outputs recorder** (`outputs.record(actual)`) with a `.verify()` (or auto-verify at test end). This DSL redesign is the real decision; the rest is data + rendering.

**Interlocks:** §6.7 (the `StepParameter` tabular/tree/inline data model), §3.14 (per-row step delimiters; tabular params attach to steps), §4.5 (tabular params render within the parameterized-group/step rendering), §3.9 (verification status reuses the assertion-outcome model).

**.NET refs:** `Kronikol.TabularAttributes` (the attribute DSL), `Tracking/Tabular/ITabularParameterData.cs`, `ParameterValueRenderer` (tabular/tree/inline rendering + status), `StepParameter`/`TabularParameterValue` (§6.7).

**Net.** Two halves: the **rendering ports mechanically** (input/output tables + verification-status cells + tree view + per-row delimiters — golden-verified, data model already in §6.7) and the **authoring DSL needs a Node-idiomatic redesign** (C# attributes → a `tabular()` builder + typed iterable inputs/outputs + verify). The batch-cases-pay-lifecycle-once value-prop carries to Node. Rendering in Phase 3; authoring API in Phase 4/5. Bounded; no architectural risk.

### 3.21 Event tracking & MessageTracker (deep dive) — the non-HTTP primitive + Event styling
The plan's corpus hinted at "Event meta-type → dual logs" but didn't scope the **non-HTTP tracking primitive** or the **Event diagram styling**. Reading `MessageTracker.cs` + `Event-Annotations.md` fills both in.

**`MessageTracker` = the generic non-HTTP interaction tracker.** Logs events/messages/commands (Kafka, SNS/SQS, RabbitMQ, EventGrid, webhooks, in-process commands) to `RequestResponseLogger` (Seam A) so they appear in sequence diagrams **alongside HTTP**. Its API has distinct verbs (port them): **`trackSendEvent`** / **`trackConsumeEvent`** (fire-and-forget, → `Event` styling) and **`trackSendMessage`** (request/response-style), each taking protocol/destination/uri/payload. It implements `ITrackingComponent` (registers with the registry → "never-invoked" diagnostics §3.18), honours phase filtering (`trackDuringSetup/Action`, §3.14) + **verbosity** levels, and supports **dual-layer correlation** (`UseHttpContextCorrelation` — read identity from the request when an event is published mid-request). **It's the base primitive behind every messaging extension** (Kafka/ServiceBus/SNS/SQS/EventBridge/PubSub/RabbitMQ, Phase 5). Ports cleanly to `@kronikol/core` (or a messaging base) as a typed `log`/`logPair` wrapper + registration + phase/verbosity filtering.

**The `Event` meta-type → distinct diagram styling (the rendering gap).** `RequestResponseMetaType` is `{Default, Event}`. Event-annotated interactions render as **fire-and-forget** notes — light-blue background (`#cfecf7`), 11px, rounded corners, **no HTTP method/status/headers** — visually distinct from request/response HTTP. Pure PlantUML note styling (§6.5) → ports mechanically. (Guard as .NET does: **don't route HTTP through `MessageTracker`** — HTTP must use the §3.11 tracker, or you get misleading event-style arrows.)

**Event-driven-architecture testing (the end-to-end pattern).** When the SUT is triggered *entirely* by consuming messages (no direct HTTP from the test), identity propagates via **message headers** (`kronikol-test-name`/`-id`, §7) — the producer stamps, the consumer reads and `als.run`s the handler (§3.2 message-driven row; the §5 mode-3-adjacent consumer pattern). Mechanically covered by §3.2 + §7; this feature names the user-facing pattern.

**Cross-cutting: verbosity.** `MessageTrackerVerbosity` (+ setup/action overrides) controls how much of an interaction is rendered — a tracking-detail config shared with the HTTP tracker (`TrackDuring*`). Port as a per-tracker verbosity option.

**.NET refs:** `MessageTracker.cs`, `MessageTrackerOptions.cs`, `RequestResponseMetaType` (`RequestResponseLog.cs:79`), Event note styling in `PlantUmlCreator.cs`; messaging extensions wrap it (Phase 5).

**Net.** `MessageTracker` is the **generic non-HTTP tracking primitive** (events/messages/commands) behind all messaging extensions — a clean Seam-A `log` wrapper + `ITrackingComponent` registration that ports to core. The **`Event` meta-type's fire-and-forget styling** (blue notes, no method/status) is pure PlantUML rendering (§6.5). Event-driven-architecture testing reuses §3.2/§7 header propagation. Primitive + styling in Phase 4; messaging extensions in Phase 5. Bounded; no architectural risk.

### 3.22 Minor feature gaps (consolidated) — config + rendering, all mechanical
The audit surfaced several smaller features that port as config/rendering with no architectural risk; scoping them together for completeness (each lands in Phase 3–4):
- **Diagram customisation.** Themes (`PlantUmlTheme`), the `DependencyPalette` colours, arrow-colour mode (§3.19), per-report **custom CSS / favicon / logo** (`CustomCss`/`CustomFaviconBase64`/`CustomLogoHtml`), setup-highlight colour. Port as options + Bucket-A/B assets. Browser-only drops `InlineSvgRendering` and PlantUML-server config (§3.5).
- **Setup/Action visual separation.** `SeparateSetup` (render setup/teardown steps in their own section) + `HighlightSetup`/`SetupHighlightColor` (shade the setup partition). PlantUML + HTML rendering driven by the phase (§3.14); pure string building.
- **Focus fields & participant emphasis.** `FocusFields` (highlight specific JSON fields in notes) + `FocusEmphasis`/`FocusDeEmphasis` (bold the focused participant, grey the rest). PlantUML note/participant styling (§6.5); already in the corpus ("focus-field highlighting").
- **Tags & attributes + filter UI.** Scenario **Labels/Categories** (in the §6.7 model) rendered as filterable **badges**; the search engine (`advanced-search.js`, §4.3) already filters by them. The concrete UI pieces Kronikol4J had to port (§3.24): the **`category-filters` box** (All + per-category toggles + Uncategorized), the **Assertions/Steps/Databases toolbar toggles** (driven by diagram markers `<<assertionNote>>`/`<<stepDelimiter>>`/`database "`), and the **dependency-filters box** — mechanical, but each needs a corpus fixture (§6.3).
- **Content formatting.** Content-type-aware body formatting incl. **GraphQL** (`GraphQlBodyFormatter` — note the §6.4 *default-encoder* escaping path) + the user `mid`/`post` content processors (§3.13). JSON pretty-printing is §6.4.
- **AI-integration prompt & project templates (adoption).** The "set up Kronikol via this AI prompt" doc → a wiki/README prompt; the `dotnet new` **project templates** → a Node **`npm create @kronikol`** scaffolder (or example repos). Docs/onboarding, not runtime.

**Net.** All mechanical — options, PlantUML/HTML rendering (§6.5 discipline), Bucket-A/B assets, and two adoption artifacts (AI prompt, `create-kronikol`). No architectural risk; fold into Phase 3 (rendering/customisation) and Phase 4 (focus/tags wiring), with the scaffolder/prompt as Phase-5 adoption polish.

### 3.23 Multi-host / dual-host test architectures (deep dive) — *free* in-process in Node (from the wiki)
A real pattern (`Multi-Host-Test-Architectures`): the SUT spans **two+ hosts** — e.g. an **API host** + a **worker/function host** (Service Bus / change-feed / queue consumer) — sharing databases, messaging, and caches. The test boots both and must track interactions across them.

**.NET's complexity is DI-container-scoped.** Each host is a separate DI container, so .NET must **bridge the `MessageTracker` across containers** and **share messaging instances** (with an `AddSingleton`-ordering gotcha — register shared instances *after* the framework's). This plumbing exists *only* because each host has its own DI graph and its own tracker instance.

**Node makes the in-process case *free*.** Because the §3.10 `globalThis` state registry is **process-global**, two in-process hosts (e.g. an Express app + an in-process BullMQ/Kafka worker in the same Node process) **automatically share the one sink + `TestCorrelationStore` + ALS** — there is **no DI-container bridging** to do (the .NET cross-container problem doesn't exist). A genuine win: the §3.10 design that fixes the dual-package hazard *also* makes in-process multi-host work for free.

**Cross-process multi-host = the existing §5 + §7 machinery.** When the worker is a *separate process* (API process + worker process), identity crosses via **headers** (HTTP §7) / **message headers** (§3.2 message-driven), and each process emits its **own fragment** (§5 mode 2/3) that the merge combines. No new mechanism.

**Net.** A **usage pattern, not a new subsystem**: **in-process is free in Node** (the §3.10 process-global registry shares the sink/correlation/ALS — no DI bridging, unlike .NET), and **cross-process reuses §5 fragments + §7 header propagation**. Document the pattern; no net-new engineering. *(This completes the feature-coverage audit against the wiki — see also the two enrichments folded into §3.13 (hash-based token shortening) and §3.21 (MessageTracker verbs).)*

### 3.24 Reconciliation against Kronikol4J (the working sibling port) — confirmations, gaps, gotchas
The Java port (`java/`) is **far more built-out than referenced** (28 modules, 63 test files, through Phase-5 breadth) and — critically — **it already built and *proved* the verification backbone this plan designs.** Reading its code + CHANGELOG validates the plan against a working implementation of the identical spec and surfaces real gotchas.

**Confirmation — the §6 backbone is *demonstrated*, not just designed.** Kronikol4J has:
- **The §6.6 capture tool, built:** `parity/dotnet-capture/` is a .NET console app (`kronikol-capture.exe`, references `Kronikol.dll` + `Mvc.Testing`) that runs the real .NET Kronikol to emit goldens — exactly the `KronikolParityCapture` the plan specified.
- **A feature-keyed golden corpus, committed:** `…/test/resources/parity/report-{background,blankname,dupnames,filterstoggles,failureclusters,combinedtable,complexparams,customassets,customstylesheet,attachments,cimetadata,blankonfail}.html` + `report-data.{json,xml}` + `ci-summary-*.md` + `diagnostic-report.html` + `iflow-{activity,calltree,flame,segmentdata}` + `html-escape-samples.txt`. This *is* the §6.3 corpus, realized.
- **Byte-for-byte parity tests** (`GoldenHtmlParityTest`, `ReportDataParityTest`, `HtmlEscaperParityTest`, `PlantUmlParityTest`) **and Playwright rendering the .NET-parity HTML in a real *offline* browser** — proving browser-only rendering (§3.5) end-to-end. **Verdict:** the plan's whole verification approach (capture → golden → byte-parity → offline Playwright) is **proven to work in a sibling port** — risk #3 drops from "designed" to "demonstrated."

**The #1 transferable lesson — whole features hide behind un-exercised input branches.** Kronikol4J ran repeated **"report re-sweep" audits** of `ReportGenerator`'s input-conditional branches, and *each sweep found whole features unported* — "invisible because no golden supplied their triggering input." So the corpus discipline must be **branch-coverage, not a feature checklist:** for every `if (options.X)` / `if (scenario.Y != null)` in `ReportGenerator`, ensure a corpus fixture triggers it. Budget explicit re-sweep passes. Elevated into §6.3.

**Feature gaps Kronikol4J found (add to the plan):**
- **Failure Clusters section** — failed scenarios sharing a *normalised first-line error* are grouped (clusters ≥2) into a collapsible `<details class="failure-clusters">`, rendered before the timeline (`ReportGenerator` 816–840; reuses `FailureClusterer`). **Net-new** — wasn't in the feature audit.
- **Category-filters box** (`<div class="category-filters">` — All + per-category toggles + Uncategorized), the **Assertions/Steps/Databases toolbar toggles** (driven by diagram markers `<<assertionNote>>`/`<<stepDelimiter>>`/`database "`), and the **dependency-filters box** — concretise §3.22's "tags/labels display+filter."
- **`BackgroundStepsDetector`** (Gherkin Background extraction) — a deterministic string algorithm: within a `Rule` group (≥2 step-bearing members) detect a shared step prefix → extract into each scenario's `backgroundSteps`, trim from `steps`; guards (open-with-`And`/`When` skips, zero-length prefix skips, remaining-step-reopening-with-`Given`/`When` skips). Refine §3.14.

**Gotchas (pre-empt them):**
- **Anchor-id dedup** — .NET pre-computes a unique anchor id per scenario, suffixing **duplicate display names with `-N`** (first keeps base, next `-2`; `ReportGenerator` 798–814), computed **once, keyed by testId**, threaded into scenario sections + parameterized rows + failure-cluster links. Slugging inline per-site (the obvious port) **collides** when two scenarios share a display name. (The parameterized-group anchor stays a raw group-name slug — *not* deduped, matching .NET 1601.) → §4 Bucket-C anchor-ID gen.
- **`HtmlEscaper` took iteration** — the `WebUtility.HtmlEncode` shim needed a parity *fix* in Kronikol4J, confirming §4.4's warning that it's a real hazard, not a one-liner.

**A validated divergence (informs §3.3).** Kronikol4J chose **fully-immutable `record` models + rebuild-on-enrichment** (e.g. `BackgroundStepsDetector` rebuilds affected `Scenario`s rather than mutating in place) — *not* the plan's "readonly core + mutable enrichment." Both work; Java's experience shows immutable-rebuild is clean and viable. For TS, note it as the proven alternative if mutable-enrichment gets awkward.

**Scoping boundary confirmed (§3.7).** Kronikol4J explicitly draws an **"options-orchestration / runtime-config" boundary**: the *render path* is faithful for any given title/component-diagram, but the *options→value resolution* (`GetTestRunReportTitle` default, `ShouldEmbedComponentDiagram`) is a separate runtime-config layer left at the seam — confirming §3.7's split.

**Net.** The working sibling **validates the plan's hardest bet (the golden-parity backbone) as demonstrably buildable**, surfaces **three concrete feature gaps** (Failure Clusters; the filter/toggle boxes; `BackgroundStepsDetector`) and **two gotchas** (anchor-id dedup; the escaper needing iteration), and shows **immutable-rebuild** as a validated model alternative. The #1 transferable lesson — **audit every input-conditional branch against the corpus** — is now in §6.3.

---

## 4. HTML-assembly port (deep dive) — mostly copy-paste, and the frontend is *native JS*

Phase 3 looked like the biggest *volume* risk (`ReportGenerator.cs` ~4,980 lines + `DiagramContextMenu.cs` ~4,064 + `Stylesheets.cs` ~1,736 ≈ **~10,800 lines**). A full read (in the Java plan) shows **~64% is static JS/CSS that copies byte-for-byte**, shrinking the genuine C#→TS surface to **~3,800 lines** — and Node has an extra advantage: **the static JS is native JavaScript that runs unchanged.**

### 4.1 The four buckets (measured)
| Bucket | ~Lines | % | What | Node note |
|---|---|---|---|---|
| **A — Static JS/CSS (FREE, copy verbatim)** | ~6,950 | ~64% | `DiagramContextMenu.cs` ~99.9% static JS/CSS; `ReportGenerator` inline JS funcs; `Stylesheets.cs` pure CSS; the two embedded `.js` files | **Runs natively in Node + browser — no engine, no transpile** |
| **B — Interpolated HTML templating (MECHANICAL)** | ~1,150 | ~11% | the `<body>` builder (~151 `Append($"…")`) + render helpers + `<head>` template | Template literals; golden-HTML diff |
| **C — Genuine logic (CAREFUL)** | ~1,950 | ~18% | grouping/sorting/**failure-clustering** (the Failure Clusters section, §3.24); the parameterized-group **pivot** (`RenderParameterizedGroup` — **deep dive §4.5**, a ~1,000-line subsystem); pie-chart geometry (trig-float hazard §6.4); **anchor-ID gen** (dedup duplicate display-names with `-N`, computed once keyed by testId — §3.24 gotcha; the param-group anchor stays a raw group-name slug); JSON/XML/YAML serializers; HTML-escaping | port carefully |
| **D — Orchestration / IO** | ~700 | ~7% | `CreateStandardReportsWithDiagrams`, file writing, attachment copy, resource loading | `node:fs` |

### 4.2 The enabling move: externalize Bucket A in .NET first (shared prep)
Bucket A currently lives *inside* `.cs` files as raw-string literals. **Refactor it into `.js`/`.css` resource files in the .NET codebase first** (behavior-preserving, gated by the existing AngleSharp + Playwright tests). Effect: `DiagramContextMenu.cs` 4,064 → ~150 lines; `ReportGenerator`'s JS region ~1,178 → ~30; `Stylesheets.cs` 1,736 → ~1. **Both runtimes then load the *same* asset files** → asset parity guaranteed by construction. Node bundles them via tsup (`loader: { '.css': 'text', '.js': 'text' }`) or `fs.readFileSync` at runtime. **This is shared with Kronikol4J — do it once.**

The genuine C#→TS port surface drops from ~10,800 to **~3,800 lines (B+C+D)**, of which only ~1,150 are mechanical templating.

### 4.3 Test assets that come with it (Node wins twice)
- **`advanced-search.js`** is the only non-trivial parseable logic in Bucket A, and it already has a **search-engine test suite (~143 cases)** pinning `tokenise`/`parse`/`evaluate`/`match` (`tests/Kronikol.Tests.SearchEngine/`, a Jint harness in .NET). **In Node these cases run against the *identical* file with no JS engine** — instant parity coverage, no GraalJS.
- **Playwright E2E (~516 methods, `tests/Kronikol.Tests.EndToEnd/`)** is already Playwright — port the .NET bindings to **`@playwright/test`** nearly verbatim. **The CLAUDE.md Playwright rules carry over directly** (no `force:true`, no network mocking, `pollingInterval`, SVG `dispatchEvent` workarounds, strict-mode `.first()`/`.nth()`, the `fillSearchBar` keyup helper).
- **Caveat:** there are **no golden/snapshot HTML fixtures today** — current C# tests assert structurally via AngleSharp. The golden-HTML harness (§6) is genuinely new Phase-0 infrastructure.

### 4.4 The one real cross-runtime hazard here
HTML escaping is **`System.Net.WebUtility.HtmlEncode`** at **63 call-sites** — it encodes `& < > "` *and all chars >127 to numeric entities*. No Node library matches this exactly. **Build a small escaping shim reproducing WebUtility's exact output** (the Java port already wrote `HtmlEscaper.java` — mirror it), unit-tested against .NET's output, or every golden-HTML diff fails. (Tracked with the §6.4 fidelity hazards.)

### 4.5 The `RenderParameterizedGroup` pivot (deep dive) — a two-level rule cascade, ~80% mechanical + ~20% per-adapter capture
The Java plan flagged this as "the single hotspot" (~560 lines, `ReportGenerator.cs:2545–3104`). Reading the actual code (plus its grouping brain `ParameterGrouper.cs` ~238 lines and the value-rendering helpers) shows it's **bigger than 560 lines** — the parameterized *subsystem* is ~1,000+ across files — but **less scary than "560 lines of branching logic"**: most is mechanical rendering, and the genuine port risk is two rule engines plus a per-adapter data-capture seam.

**What it does.** Data-driven tests (xUnit `[InlineData]`, NUnit `[TestCase]`, ReqNRoll Scenario Outlines) that differ only in input values are **collapsed into one collapsible group**: a **parameter table** (rows = parameter sets, columns = parameter names) with status/duration per row + **one shared diagram** when all rows produce identical diagrams (`diagramComparer` compares each row's `CodeBehind`, `ReportGenerator.cs:1929–1940`), plus per-row detail panels (steps, failure diff) toggled by row selection (`selectRow`; row 0 active). The payoff: a 50-case `[Theory]` renders as one diagram + a 50-row table, not 50 diagrams.

**Two rule engines — the real complexity, not the HTML:**
1. **Column rule** (`ParameterGrouper.DetermineParamsAndRule`) — which columns the table shows:
   - **R0 Fallback** — a single "Test Case" column (custom `ExampleDisplayName`, or > `maxColumns` params, or unparseable names).
   - **R1 ScalarColumns** — one column per scalar param (the common case).
   - **R2 FlattenedObject** — a *single complex-object* param whose scalar properties become columns, via **.NET reflection** (`ParameterValueRenderer.TryGetFlattenableProperties`) **or** **record-`ToString()` parsing** (`ParameterParser.TryParseRecordToString`, the C# `"TypeName { Prop = Val }"` format).
2. **Cell-value rule** (`ParameterValueRenderer`, called per cell) — scalar plain text, **expandable details for complex objects** (`RenderExpandable`), or **parsed-string** rendering (`TryRenderFromParsedString`).
   Plus: grouping by **`OutlineId`** (framework outline, e.g. ReqNRoll → Cucumber-js Scenario Outline) vs **display-name prefix** (`ParameterParser.ExtractBaseName`), and a **flat view** (original Gherkin Example columns, `ExampleFlatValues`) toggled against the grouped view.

**Portability — what ports clean, what diverges:**
- ✅ **Rendering + both rule engines port mechanically** (StringBuilder → template literals; plain control flow). Golden-HTML diff (§6.6) verifies it; the existing **`ParameterGrouperTests.cs`** ports directly as the column-rule unit suite.
- ✅ **R2 object-flattening: .NET reflection → `Object.entries`** — *easier* in JS, when the adapter captured the raw arg object (`ExampleRawValues`).
- ❌ **Record-`ToString()` parsing (`TryParseRecordToString`) has NO Node analog** — `"TypeName { Prop = Val }"` is a C# record feature. **Drop it; replace with JS object/JSON handling** (Node more often *has* the live object, so the string-parse fallback is rarely needed). A documented semantic divergence.
- ⚠️ **Display-name parsing (`ParameterParser.Parse`/`ExtractBaseName`) is framework-specific** — .NET parses xUnit/NUnit/ReqNRoll name formats; Node must re-tune for **Vitest/Jest `test.each`** templates (`%s`/`%i`/`$named`) and **Cucumber Examples**. Per-adapter parser rules.

**The load-bearing finding — the pivot's inputs come from each test adapter, not the report.** Grouping + rendering is *centralized*, but it consumes `ExampleValues` / `ExampleRawValues` / `ExampleFlatValues` / `OutlineId` / `ExampleDisplayName` on each `Scenario` — and **those are populated by the test-framework adapter's parameterization hook** (which in .NET lived inside each test adapter). So the work **splits across phases**: the **engine** (rule cascade + tables + golden tests fed by hand-authored `Scenario` inputs) lands in **Phase 3**; the **per-adapter capture** (Vitest/Jest `test.each` args → `ExampleValues`/`ExampleRawValues`; Cucumber Examples → `OutlineId`/`ExampleFlatValues`) lands **with each adapter** in Phase 4/5. ≈80% mechanical-centralized, ≈20% distributed-capture — **a phasing refinement the plan didn't have** (it implied the whole pivot was Phase 3).

**Determinism notes (feed §6):** param-name ordering uses `Distinct()` (first-seen order — JS `Set` matches); search text uses `ToLowerInvariant()` (§6.4 invariant casing); `ParameterGrouper` **deep-clones scenarios before R2 mutates `ExampleValues`** (`ParameterGrouper.cs:27–32`) — port the clone-before-mutate discipline (matters less under §5's per-worker model, but keep it; ties to §3.3 mutable-enrichment).

**.NET refs:** `ReportGenerator.cs:1926–2002` (grouping dispatch) + `2545–3104` (`RenderParameterizedGroup`); `ParameterGrouper.cs`, `ParameterParser.cs`, `ParameterValueRenderer.cs`, `ScenarioTitleResolver.cs`, `ErrorDiffParser.cs`; tests `ParameterGrouperTests.cs`.

**Net.** Re-rated from "560-line black-box hotspot" to **"~1,000-line subsystem, ≈80% mechanical."** Real risks: a **two-level rule cascade** (column rule R0/R1/R2 + cell-value rule) to port faithfully (golden-HTML + the existing `ParameterGrouperTests` contain it); **one feature to drop** (record-`ToString()` parsing, no Node analog); and **framework-specific capture** that **splits the work into a Phase-3 engine + per-adapter Phase-4/5 capture**. Bounded; no architectural surprise.

### 4.6 The Specifications report (deep dive) — the *second* output, but one shared generator
Kronikol emits **two** HTML reports, not one — `TestRunReport.html` *and* `Specifications.html` (plus a separate specs data file) — a scope item the plan previously omitted. The good news from reading `ReportGenerator.cs:37–176, 256, 4258–4400`: it's far smaller than "a whole second report," because **both reports come from the *same* `GenerateHtmlReport`**, differentiated by one flag.

**Two reports, one generator (the key finding).** `CreateStandardReportsWithDiagrams` calls `GenerateHtmlReport(...)` twice:
- **TestRunReport** — `includeTestRunData: true`, default stylesheet, CI metadata + component diagram embedded.
- **Specifications** — `includeTestRunData: false`, the **violet-theme stylesheet** (`HtmlSpecificationsCustomStyleSheet`), `generateBlankOnFailedTests: true`, **no** CI metadata / component diagram, `SpecificationsShowStepNumbers`.

So `includeTestRunData` is the master toggle: it branches the generator to **hide execution data** (durations, results, http-interaction detail, CI/component views) and render the scenarios as **clean living documentation**. **Porting §4's HTML assembly yields *both* reports** — the specs report's only net-new HTML work is the `includeTestRunData=false` branch + the violet stylesheet (a Bucket-A asset that copies verbatim, §4.2).

**Specifications = living documentation, with blank-on-failure.** The concept: when *all* tests pass, the scenarios **are** the verified service spec, shown without pass/fail/duration noise. So both the specs HTML **and** the specs data are written **empty** if *any* scenario failed (`generateBlankOnFailedTests`, checked in `GenerateHtmlReport:285` *and* `GenerateSpecificationsData:4260`) — a spec is only valid when green. This is a core Kronikol value-prop and a distinct behavior the port must replicate exactly (the TestRunReport always renders; the Specifications report self-blanks on any failure).

**Separate, *simpler* specs data serializers (extend §6.7).** `GenerateSpecificationsData` → `GenerateSpecifications{Yaml,Json,Xml}` are **distinct** from the TestRunReport serializers and describe **behavior, not execution**:
- Schema: `Title` + `Features > Feature{Name, Endpoint?, Description?, Labels?} > Scenarios{Name, IsHappyPath, Labels?, Categories?, Steps}`, scenarios ordered `IsHappyPath desc, then Name`.
- **No** `KronikolVersion` / `StartTime` / `EndTime` / `Result` / `DurationSeconds` / `errors` / `StableId` / `httpInteractions` / `diagrams` — none of the execution data.
- **Steps are text-only strings** (`MapSpecStepJson` returns `"{keyword} {text}"`), so `steps` is a `string[]`, **not** an object array — different from `MapStepJson` (§6.7).
- Same conventions as §6.7 otherwise: JSON camelCase + **default-encoder escaping** (§6.7 a-3) + 2-space indent; YAML/XML PascalCase + omit-null + lossy `SanitiseForYml`.
- **More deterministic** than the TestRunReport data — *no version/timestamp* → version-free goldens (simpler to pin).

**Config (all default on):** `GenerateSpecificationsReport`, `GenerateSpecificationsData`, `SpecificationsDataFormat`, `SpecificationsTitle` (`"Service Specifications"`), `HtmlSpecificationsFileName`/`YamlSpecificationsFileName` (`"Specifications"`), the violet stylesheet, `SpecificationsShowStepNumbers`.

**Net.** A real omission, but a **modest** addition: the HTML is **~free** (shared `GenerateHtmlReport` + the `includeTestRunData=false` branch + the violet asset); net-new work = **three simple specs serializers** (behavior-only, text-only steps — much simpler than §6.7's), the **blank-on-failure** semantic (both HTML and data), and the docs-style HTML branch. The corpus (§6.3) must now produce **both** reports and include a **failing scenario** to exercise blank-on-failure. Lands in Phase 3 alongside the TestRunReport.

---

## 5. Test-run lifecycle & worker aggregation (deep dive)

Node test runners **fork worker processes by default** (Jest workers; Vitest `pool: 'forks'`; `node:test` runs each file in a subprocess; Playwright workers; Cucumber-js parallel workers). So — exactly like Java's forked JVMs — this needs fragment emission per realm + a merge step. The good news: this **maps onto a mechanism Kronikol already has.** The deep dive below works out the *granularity* (per-file, §5.4), the *topologies* (§5.3), and the *per-runner binding* (§5.7); §5.1–5.2 first establish the baseline.

### 5.1 What .NET does (the model to replicate)
Each test process is **self-contained**: a static `RequestResponseLogger` queue accumulates everything, and one **end-of-run hook** calls `ReportGenerator.CreateStandardReportsWithDiagrams(...)` to load all logs, generate diagrams, and write a complete report. Critically, **.NET already solves multi-process**: `GenerateMergeableData=true` emits an enriched JSON fragment per process, and a `kronikol merge` CLI (`MergeableReportMerger`) combines fragments — features grouped by DisplayName, scenarios unioned by Id, component relationships re-aggregated, earliest-start/latest-end reconciled. **This is exactly the shape the Node worker problem needs.** Kronikol4J already ported this (`MergeableReportMerger.java`, `MergeableReportRenderer.java`, `kronikol4j-cli`) — **port the same semantics to TS.**

### 5.2 Why Node diverges (the real differences)
- A single logical test always runs entirely within one worker → per-worker fragments are complete for their tests; **no need to merge raw logs, only finished report fragments.**
- **Workers fork by default and often invisibly** (Jest `maxWorkers`, Vitest pool, `node:test` `--test-concurrency`). So fragment-emission + merge must be **automatic and on by default**, not a manual CLI step.
- **Worker→main signalling differs per runner.** Most runners give a **main-process once-per-run hook** (`globalSetup`/`globalTeardown`, reporter `onFinished`/`onRunComplete`) that runs *after* all workers — the natural place to merge. Workers write fragment files to a shared run dir; the main hook merges them.

### 5.3 Two axes, not "six runners" — the real shape (deep dive)
The "six runners" framing overstates the variety. What actually varies is **two orthogonal axes**, and getting them straight collapses the problem:

- **Axis 1 — where does the sink live?**
  - **(A) In the test realm** — unit/integration tests that exercise the code/app *in-process* (Vitest/Jest/node:test/Mocha unit tests; `supertest(app)` without a listening socket; `fastify.inject()`). The §3.10 `globalThis` sink **is** the data.
  - **(B) In a separate app process** — true E2E where the runner drives a browser or a real HTTP socket against a **separately-launched server** (Playwright; `supertest`/`fetch` against a listening server; testcontainers). The interactions are tracked **inside the server**, not the runner. Identity arrives via test-tracking **headers** (§7); the **server is the sink**.
- **Axis 2 — how many realms accumulate, and how is completion signalled?** Single realm (serial / `--runInBand` / IDE single file) vs many realms (worker pools). The merge engine is identical; only the *completion hook* differs per runner.

This yields **three finalize modes** — the plan previously had only the first two:
1. **Self-finalize (single realm, Axis-1-A).** No run dir present → the in-realm listener writes the final report directly from its sink ("merge of one"). The zero-config IDE/standalone path.
2. **Runner-orchestrated merge (many realms, Axis-1-A).** `globalSetup` creates `KRONIKOL_RUN_DIR`; each realm flushes a fragment; the main-process completion hook merges. The default for any parallel in-process run.
3. **Out-of-process server sink (Axis-1-B, the E2E topology — newly surfaced).** The app server is the sink; the runner injects identity headers per request (§7) and triggers a **server-side flush** at run end (shutdown signal or a control endpoint); fragments come from the *server* process(es), merged by the runner's global teardown or `@kronikol/cli`. Genuinely different plumbing — and it's the topology Playwright (the headline E2E runner) actually needs, so it can't be an afterthought.

### 5.4 The decisive simplification: fragment-per-FILE, not per-worker
Node gives **no clean "this pooled worker is shutting down" hook** — workers are *reused* across files (jest-worker, Vitest's pool), and the runners expose a per-**file** teardown, not a per-worker one. Rather than fight for a per-worker signal, **emit one fragment per test FILE**, flushed-and-cleared at the file's teardown:
- **Robust:** atomic temp-file + `fs.rename` per file; a crash loses *one file's* data, not a whole worker's.
- **No missing-hook problem:** every runner has a reliable per-file boundary (`afterAll` / environment `teardown()` / file-root `after()` / subprocess `exit`).
- **Clean attribution + closes the leak (§5.5):** clearing the sink at each file boundary prevents cross-file bleed in reused workers.
- **Merge-neutral:** the merger's operations (dedup features by DisplayName, union scenarios by Id, sum/union component relationships, earliest-start/latest-end) are associative and commutative — N *file*-fragments merge to the identical result as N *worker*-fragments, just with more inputs. Merge is linear; thousands of fragments are fine.

Finer-grained than .NET/Java (one fragment per *process*) and strictly safer. **This is a design decision the original §5 didn't make.**

### 5.5 The test-isolation leak (globalThis sink vs per-file module reset) — a silent correctness bug
Vitest (`isolate: true`, the default) and Jest reset the **module registry** between files, but the **worker is reused and its `globalThis` is not reset** — and the §3.10 sink lives on `globalThis`. So without intervention, **file B's fragment contains file A's interactions** (and a naive per-worker flush would double-count). Silent: green tests, wrong report — the §5 twin of the §3.10 hazard.
**Resolution (ties to §5.4):** the adapter **clears the sink at the start of each file** and flushes the fragment at its end; `clear()` truncates in place (§3.10 rule 3). Pool-specific:
- **`forks` / `threads`:** worker reused → globalThis persists → **clearing is mandatory.**
- **`vmThreads` / `vmForks`:** each file runs in a fresh `node:vm` context → fresh `globalThis` per file (no leak) **but** VM contexts carry their own `instanceof`/cross-realm caveats (a cousin of §3.10) → treat as experimental; support `forks`/`threads` first.

The spike must include a **two-files-one-worker** case asserting **zero cross-file bleed**.

### 5.6 Concurrent tests within a file — why ALS attribution is load-bearing
`test.concurrent` (Vitest/Jest) interleaves several tests on **one realm's sink**. Per-file fragments handle the *file* boundary, but each individual *log entry* must still attribute to the right *test*. That attribution rides entirely on `AsyncLocalStorage` (§3.2): the adapter wraps each test body in `als.run(identity, body)`, so `RequestResponseLogger.log()` reads the ambient testId **at call time**, regardless of interleaving. **Seam B's per-test scope is what makes concurrent-in-file correct** — the sink can hold interleaved entries because each carries its own identity. This ties §5 directly to §3.2 and is why the ALS-singleton (§3.10) and per-test scoping are non-negotiable. The spike must include a concurrent-in-file case.

### 5.7 The adapter SPI + per-runner hook matrix
Define a small SPI in `@kronikol/runtime`; each adapter binds it to its runner's native hooks:
- `onRunStart()` *(main realm)* — ensure `KRONIKOL_RUN_DIR`, install the determinism seam.
- `onFileStart()` — **clear the sink** (§5.5).
- `onScenarioStart(testId, phase)` — open an `als.run` identity + phase scope (§3.2).
- `onScenarioEnd(result)` — capture outcome → logs; close the scope.
- `onFileComplete()` — atomically **flush this file's fragment**.
- `onRunComplete()` *(main realm)* — **merge** fragments → report, or self-finalize (mode 1).

| Runner | Worker model | Per-test scope hook | Per-file flush hook | Run-completion / merge | Sink |
|---|---|---|---|---|---|
| **Vitest** | `forks` / `threads` / vm\* pools; `isolate` per file | reporter `onTestStart`/`onTestFinished` or `beforeEach`/`afterEach` | `afterAll` (file) + clear | custom **reporter `onFinished`** (main) + `globalSetup` teardown | test realm |
| **Jest** | child-process workers (jest-worker), reused | custom `testEnvironment.handleTestEvent` / `setupFilesAfterEach` | custom **`testEnvironment.teardown()`** (per file) | custom **reporter `onRunComplete`** or `globalTeardown` | test realm |
| **node:test** | subprocess **per file** (CLI) | `beforeEach`/`afterEach` + test context | file-root `after()` / `process.on('exit')` | global-setup teardown **or wrapper bin**; reporter in parent | test realm |
| **Mocha** | serial (1 proc) default; `--parallel` workers | root-hook `beforeEach`/`afterEach` (`mochaHooks`) | serial: n/a; parallel: per-worker `afterAll`/exit | reporter `end` (serial) / wrapper (parallel) | test realm |
| **Cucumber-js** | serial default; `--parallel` child workers | `Before`/`After` (+ `BeforeStep`/`AfterStep` → phase) | `AfterAll` (per worker) | custom **formatter** / wrapper | test realm |
| **Playwright** | **process** workers; drives browser/app | per-test **fixture injects identity headers** | — (no sink in worker) | `globalTeardown` triggers **server flush** + merges server fragments | **app-server process** |

`KRONIKOL_RUN_DIR` is inherited by both `child_process` *and* `worker_threads`, so run-dir discovery works across forks **and** threads pools — and (mode 3) must also be passed into the launched app server's env. **Version-sensitive items to confirm at implementation:** Vitest's default-pool version (`threads`→`forks` changed across a major), node:test's global-setup flag/version, Jest's experimental `workerThreads` mode.

### 5.8 Robustness & cross-links
- **No write contention:** each realm writes a uniquely-named fragment (pid / worker id / file hash) — concurrent writers never touch the same file.
- **Deterministic merge (links §6):** order features/scenarios deterministically (DisplayName, then scenario Id) so the report is identical regardless of completion order — required for golden parity.
- **Hard-kill resilience:** a SIGKILLed run (no teardown) still leaves whole file-fragments on disk → `@kronikol/cli merge <run-dir>` recovers a partial report.
- **Fragments are an on-disk secret-leak surface (§3.13):** each fragment is the enriched JSON written to a shared run dir (and uploaded as a CI artifact). Capture-time redaction (Seam A) is what keeps secrets out of it — render-time exclusion would not.
- **Mode detection:** presence of `KRONIKOL_RUN_DIR` selects mode 2/3 vs mode 1 (self-finalize).
- **Memory:** per-file generation bounds peak memory to one file's logs.
- **Monorepo / multi-project runs:** each project's run is its own report by default, with an optional cross-project aggregate (the CLI/merge).
- **Two merge entry points, one engine:** the automatic adapter merge (default) and the manual **`@kronikol/cli merge`** (cross-machine/CI) share the ported merger — the direct port of the "Merging Parallel Reports" feature (`kronikol merge <inputs…> -o <html> -t <title>`): inputs are files/dirs (recursive `*.json`)/globs; **only the enriched JSON is mergeable**; output is combined **HTML**. Merge semantics to preserve exactly: scenarios deduped within same-named features; component relationships **sum** call/test counts and **union** method sets; internal-flow data unioned; earliest-start/latest-end; CI metadata from the first runner that captured it.

### 5.9 What the Phase-0 spike must prove (acceptance criteria)
1. Vitest **`forks` AND `threads`** + Jest workers: each file emits a complete fragment (atomic), merged **deterministically** (order-independent) into one report.
2. **No cross-file bleed** (two files, one reused worker) — proves the §5.5 per-file clear.
3. **Concurrent-in-file** attribution (`test.concurrent`) — proves ALS per-test scope (§5.6 / §3.2). Include a **pg pool with two concurrent ALS scopes** (each query logs the correct identity → proves **bind-at-call-site** under pooling) and a deliberate **"wrong way"** re-resolve-at-completion case showing it mis-attributes (pins why the rule exists, §3.2).
4. **Self-finalize** (single file, no run dir) writes a report with **zero config** (mode 1).
5. **Killed worker** degrades gracefully (its fragment absent, not corrupt; others still merge).
6. **Out-of-process E2E** (Playwright or `supertest` against a *listening* server): identity propagates via the `test-tracking-*` headers (§7), the **server** flushes a fragment, the runner merges it — proves **mode 3**.

**Net.** "Port the merger" is the easy half and is already proven (.NET + Java). The deep dive's real findings: **(i)** it's **two axes / three finalize modes**, not six runners — and the **out-of-process E2E mode (3) was missing** from the original plan; **(ii) fragment-per-file** (not per-worker) sidesteps Node's absent per-worker hook, bounds crash loss, and — with a mandatory per-file sink clear — **closes a silent cross-file-bleed bug** that is the §3.10 hazard's twin; **(iii) concurrent-in-file correctness rides on `AsyncLocalStorage`** (Seam B), binding §5 to §3.2; **(iv)** a six-row **adapter SPI** maps one model onto every runner. Owned by `@kronikol/runtime` + each adapter + `@kronikol/cli`. Re-rated: bounded, but with **two genuinely new design outputs** (mode 3 + per-file fragments) the original §5 lacked. **The remaining Phase-0 blocker spike** (§9), now with concrete acceptance criteria (§5.9).

---

## 6. Determinism, the parity corpus & cross-runtime fidelity (Phase 0 backbone)

The golden-file strategy is the verification backbone. (Same structure as Java §6; Node-specific deltas noted.)

### 6.1 Determinism
The data model uses `Guid.NewGuid()` for `traceId`/`requestResponseId` and wall-clock timestamps — non-deterministic. Two mechanisms:
1. **Deterministic mode** — inject a seeded `IdGenerator` + a **reproducible *monotonic* `Clock`** (a seam the core needs for testability anyway). Node: a module-level injectable `idGenerator` / `clock` (avoid hard-coded `crypto.randomUUID()` / `Date.now()`). **Subtlety the deep dive surfaced:** a *frozen* instant yields zero durations and no tiebreak ordering — so the deterministic clock must emit a **reproducible monotonic sequence** (e.g. +1 tick per read). That single mechanism gives both **stable non-zero durations** *and* the **stable per-diagram ordering key** that PlantUmlCreator's `.AsOrdered()`/sorts need (§6.6), so interactions render in a fixed order even when the corpus runs in parallel. **The heaviest consumer is §3.19's component analytics** — *every* statistic (latency percentiles, outliers, CV, concurrency, call-ordering) is timing-derived, so without the monotonic clock none of it is golden-testable.
2. **Normalization pass** — a single **canonicalizer** (§6.6) that strips/rewrites volatile fields (residual IDs, timestamps, absolute → relative POSIX paths, BOM, `\r\n`, PlantUML `autonumber` drift) before diffing.

### 6.2 The shared .NET-side prep workstream
Three upstreamable, behavior-preserving changes to **Kronikol (.NET)**, gated by its existing test suite, **shared with Kronikol4J** (do once):
1. **Determinism seam** — `IdGenerator`/`Clock`.
2. **Asset externalization (§4.2)** — static JS/CSS into resource files.
3. **Parity-hardening (§6.5)** — newline/culture/ordinal/ordering fixes + client-side splitting.

### 6.3 The parity corpus
Anchor on **porting the .NET demo (BreakfastProvider / `examples/Example.Api`)** to Node, then extend to a feature-coverage checklist so every diagram code path has a fixture: HTTP pairs/status/headers; SQL; an event/fire-and-forget interaction (`Event` meta-type → blue-note styling, §3.21); a proxy call; Setup vs Action phases + phase-variant verbosity; parameterized scenarios + **tabular-attribute input/output tables** (§3.20); pass **and fail** outcomes + tracked assertions (Tier 0/1); focus-field highlighting; header exclusion; redaction/truncation; large-diagram splitting; **component diagram + dependency analytics** (§3.19 — latency stats/outliers/CV, fan-in/out, cycle detection, error-correlation; needs deterministic durations + a multi-edge/cyclic fixture; plus a two-run **diff-mode** fixture); (later, with OTel) activity + flame; multi-host / background-thread correlation. Each item yields golden PlantUML + HTML + JSON/YAML/XML (the data-export schema of §6.7 — `stableId`, `KronikolVersion` pinned, the three null policies, `httpInteractions`/`steps`). **Corpus-hardening (§6.7 a-3):** include fields containing `+`, `<`, `>`, `&`, `'`, and **non-ASCII** (e.g. `"café"`, `"a+b<c>"`) so the goldens actually exercise the data-export JSON escaper divergence — a plain-ASCII corpus would silently pass while the escaper is wrong. **Two reports (§4.6):** every corpus item yields *both* `TestRunReport.*` and `Specifications.*`, and the corpus must include a **failing scenario** so the goldens pin the **blank-on-failure** behavior (specs blank, test-run renders). **Branch-coverage discipline (§3.24 — the #1 Kronikol4J lesson):** the corpus must be **driven by `ReportGenerator`'s input-conditional branches, not a feature list** — Kronikol4J's repeated re-sweep audits found *whole features unported because no golden triggered their input*. Add fixtures for the ones it caught: **Failure Clusters** (≥2 failures sharing a normalised first-line error), the **category-filters / Assertions-Steps-Databases toggle / dependency-filters boxes**, **duplicate scenario display-names** (anchor-id `-N` dedup), **Gherkin Background** extraction, and **empty/blank feature & scenario names**. Kronikol4J's committed `…/parity/report-*.html` corpus is the proven starting set.

### 6.4 Cross-runtime fidelity hazards (where byte-equality is *not* free)
- **Deflate is not byte-stable.** `PlantUmlTextEncoder` deflates then custom-base64-encodes. .NET `DeflateStream` and Node `zlib.deflateRawSync` emit **different compressed bytes for identical input**. **Assert parity on the DECODED PlantUML text, never the encoded string**; test the encoder by **round-trip** (encode→decode→equals). The subtlety where .NET made *split decisions* on encoded length is removed by client-side splitting (§6.5). **Confirmed from source (`PlantUmlTextEncoder.cs`):** raw `DeflateStream`/`CompressionLevel.Optimal` UTF-8 → PlantUML's **standard custom-base64 alphabet `0–9 A–Z a–z - _`** (3-byte→4-char packing, *no* padding). Node = `zlib.deflateRawSync` + that exact alphabet → decodable by the same `plantuml-render.js` (and plantuml.com).
- **JSON content formatting — *note bodies only* (the data-export serializers are different; see §6.7).** For the request/response bodies pretty-printed into PlantUML notes (`TryFormatAsJson`), Node `JSON.stringify` is a *closer* default match to .NET than Jackson: **preserves insertion order**, easy **2-space indent**, does **not** escape `<>&+`/non-ASCII (matches `UnsafeRelaxedJsonEscaping`). Add: (a) **null object-property stripping** (keep null array elements) for this note path; (b) the **second escaping mode** — GraphQL metadata uses .NET's *default* encoder which *does* escape `<>&+`/non-ASCII → a custom escaper for that path. Force ISO/invariant formatting for dates/numbers (avoid `toLocaleString`). **⚠ Do NOT apply this null-stripping to the data-export JSON** — that serializer (`TestRunReport.json`/mergeable) **writes** nulls and uses camelCase; YAML/XML **omit** nulls and use PascalCase. All three are catalogued in **§6.7**.
- **HTML escaping (§4.4).** A `WebUtility.HtmlEncode`-parity shim (numeric entities for >127), unit-tested against .NET's exact output.
- **Newlines.** Emit `\n` only; never use `os.EOL`. Final `replace(/\r\n/g, '\n')` before encode/compare.
- **Casing.** Avoid `toLocaleUpperCase`/`toLocaleLowerCase` (the Turkish-i trap). Plain `toUpperCase`/`toLowerCase` are Unicode but locale-independent — match .NET's invariant casing after the §6.5 .NET fix.
- **Transcendental floats are NOT bit-reproducible across runtimes (newly surfaced).** IEEE-754 `+−×÷` are deterministic per-operation, but **`Math.sin`/`cos`/`atan2`/… are not specified to be bit-identical** — .NET `Math.Sin` and V8's can differ in the last ULP. The report's **pie-chart slice geometry** (§4 Bucket C) computes SVG path coordinates via trig **server-side**, so identical inputs could yield coordinates differing in the least-significant digits → golden mismatch. **Fix (upstreamable to .NET, matched in Node): round emitted geometry to a fixed precision** (2–3 dp) before serializing — absorbs the ULP gap. Pure arithmetic (percentages, counts) is safe; only trig-derived output needs rounding. *(A clean alternative — compute slice geometry client-side in the browser — is viable since rendering is browser-only, but the coordinates are currently server-emitted; rounding is the lower-touch fix.)*
- **Sort stability.** V8's `Array.prototype.sort` is stable, and .NET **LINQ `OrderBy` is stable** — so stable sorts match **iff the comparator is identical (ordinal, §6.5)**. But .NET `Array.Sort`/`List.Sort` are **not** stable: audit the .NET paths to confirm `OrderBy` (not `Array.Sort`) is used wherever output order matters, and pin an ordinal comparator on both sides.

### 6.5 PlantUML generation parity (deep dive)
A full read of `PlantUmlCreator.cs` (~844 lines) surfaced **13 places identical input could yield different PlantUML text** (catalogued in the Java plan §6.5). The pure string-building ports almost mechanically; these divergences are silent unless handled.

**The #1 hazard — and its free fix.** Diagram **splitting depended on Deflate-encoded length** (`PlantUmlCreator.cs:260` → `EncodedDiagramExceedsMaxLength`, 786–801). Because raw DEFLATE isn't byte-identical across runtimes, the split point could differ → different decoded structure. **Resolution: adopt client-side splitting** (`clientSideSplitting=true` sets the limit to `int.MaxValue`, `PlantUmlCreator.cs:132`; the browser splits at render). This aligns with **browser-only**: the server emits **one un-split, fully deterministic diagram per test**; the JS splits for display. Hazard #1 eliminated by design.

**Remaining hazards** (fix at source in .NET, then match in Node): `Environment.NewLine` → emit `\n`; status-code `Titleize()` CurrentCulture → invariant; JSON escaping per path (above); **component-diagram participant order from `HashSet` iteration** (`ComponentDiagramGenerator.cs:123–129`) → sort / insertion-ordered set (the component diagram is a full analytics subsystem — §3.19); `:F0`/`:F2` number formatting → invariant; header `OrderBy` → **ordinal** sort; `Camelize`/`Pascalize` culture casing → invariant + Unicode-property parity check; exact `Trim`/`TrimEnd` semantics. The LOW items (alias regex `[^a-zA-Z0-9_]`, the static palette, `EscapeForPlantUmlNote`, `.AsOrdered()`) port cleanly. **These are the same source-side fixes batched into the §6.2 shared prep.**

Without 6.2–6.5 the golden-file backbone is **unbuildable as originally specified.**

### 6.6 The harness mechanism (deep dive) — capture, canonicalize, compare
§6.1–6.5 catalogue the *hazards*; this works out the *machine* that produces and checks the goldens. It's the Phase-0 backbone, and a few load-bearing mechanics were unspecified.

**(a) Fixture capture — a .NET-side tool.** A `KronikolParityCapture` console/test project runs each corpus scenario (§6.3) through the **real** report pipeline in **deterministic mode**, pinned to **one TFM** (e.g. net10) with **`InvariantGlobalization`**, and writes artifacts to `fixtures/<scenario>/…`. Two granularities:
- **Unit goldens** — the **decoded** PlantUML *as emitted by `PlantUmlCreator` before Deflate* (per scenario). This needs a **capture seam** in .NET that taps the pre-encode string (a debug hook), since the report only embeds the *compressed* form — don't reconstruct by decoding the embed for the unit golden; tap the source.
- **Report goldens** — the full **HTML / JSON / YAML / XML** for a whole corpus run, plus the **decoded PlantUML extracted from the HTML's embedded data map** (to verify the encode-embed-decode round-trip end to end).

**(b) The canonicalizer — ONE implementation, applied symmetrically (the crux).** Rather than two normalizers that can drift, write **a single TS canonicalizer** and run it **both** as a *post-capture step* over the committed goldens **and** at *assert time* over fresh Node output — so the committed fixtures are already canonical (human-reviewable PR diffs) and the comparison is canonical-vs-canonical. Its spec: `\r\n`→`\n`, strip BOM, absolute→relative POSIX paths (the big Windows-capture-vs-Linux-CI delta, and the §3.9 assertion caller `file:line`), residual ID/timestamp scrub (belt-and-suspenders behind deterministic mode), trailing-whitespace + final-newline policy. **One normalizer, one spec, used three times** (canonicalize goldens at capture, canonicalize Node output at assert, and as the contract document).

**(c) Deflate parity — verified, not byte-pinned, using the *real* decoder natively.** Assert on **decoded** PlantUML (§6.4). Node makes this cleaner than Java: the shared **`plantuml-render.js` decoder runs natively in Node** (§4.3), so the harness can:
- **round-trip:** TS `encode(p)` → `plantuml-render.js` `decode` → `=== p`;
- **cross-decode:** `decode(.NET-encoded string from the golden HTML)` → `=== p` — proving the **same browser decoder** handles both runtimes' output (the thing that actually matters, since one shared `plantuml-render.js` must decode whatever the generator emits).
No byte-comparison of compressed output, ever (zlib level/strategy differences are irrelevant to decodability).

**(d) HTML comparison — two tiers.** (i) **Normalized exact-text diff** (after the canonicalizer) as the primary golden gate — catches whitespace/attribute drift; viable because a faithful body-builder port emits the same `Append` order. (ii) **Structural/DOM** assertions (parse5) for behavior that *shouldn't* depend on formatting (the analog of .NET's AngleSharp tests). If attribute-order ever diverges, a DOM-normalize pass in the canonicalizer is the fallback — but aim for exact-text.

**(e) The parity-diff runs in two directions (CI).** Goldens live in the repo's shared top-level **`parity/`** (fixtures + the canonicalizer + the comparison tests), the same fixtures `java/` consumes — one corpus, three consumers:
- **Node regression:** Node output vs committed goldens — every PR. Fails on a Node port regression.
- **.NET drift:** re-run capture + canonicalize, diff vs committed goldens — periodic (the locked parity-diff). A diff means *.NET changed*; review and re-bless. **In the monorepo this stops being periodic and becomes per-PR:** `parity/dotnet-capture` references `../../dotnet/src/Kronikol/Kronikol.csproj` directly, so the shared `parity.yml` job re-captures from live .NET on any change to `dotnet/**`, `js/**` or `parity/**` and fails the PR that introduced the drift. No cross-repo path, no published fixture artifact, no manual re-bless cycle — this is the specific problem the polyglot layout exists to solve.

**Net.** The harness is buildable and is the Phase-0 gate. The deep dive's concrete additions beyond the hazard catalogue: a **.NET capture tool with a pre-encode tap**; **one symmetric canonicalizer** (not two); the **monotonic-Clock-as-ordering-key** refinement (§6.1); **reusing the real `plantuml-render.js` decoder natively** for encode parity; a **two-tier HTML** gate; a **two-direction** parity-diff; and a **new hazard** the plan was missing — **trig floats aren't bit-reproducible** (§6.4), fixed by rounding pie-chart geometry.

### 6.7 Data-export serializers (deep dive) — three formats, three *different* null policies, byte-parity
The report emits three machine-readable data files (`TestRunReport.{json,yaml,xml}`) plus the enriched **mergeable JSON** (§5). The Java port discovered mid-build that these are far more divergent than "serialize the model" — **three serializers with three different null policies, two casing conventions, and several format-exact rules**. (This also **corrects §6.4**, which conflated the *note-body* JSON with the *data-export* JSON.) `@kronikol/report` must hit **byte-parity** on all three; this is a large, easy-to-underestimate surface.

**Three null policies — the central trap:**
| Serializer | Casing | Nulls | Mechanism |
|---|---|---|---|
| **Note-body JSON** (PlantUML bodies, §6.4) | n/a (raw bodies) | **stripped** (`TryFormatAsJson`) | content formatter |
| **Data-export JSON** (`TestRunReport.json` + mergeable) | **camelCase** | **written** (`"errorMessage": null`) | `System.Text.Json`, `WriteIndented`, **no `DefaultIgnoreCondition`** |
| **YAML / XML** (`TestRunReport.{yaml,xml}`) | **PascalCase** | **omitted** (field absent) | hand-built `StringBuilder` / `XElement`, conditional `if (x is not null)` |

The same `errorMessage: null` is **absent** from YAML, **present as `null`** in JSON, and **stripped** from a note body. One wrong policy → that format's golden never matches.

**Exact JSON schema (camelCase, nulls written, 2-space indent, features ordered by `name`):** `kronikolVersion`, `startTime`/`endTime` (`yyyy-MM-ddTHH:mm:ssZ`, second precision), `features[]`. **Feature:** `name`, `endpoint`, `description`, `labels` (`[]`), `scenarios[]`. **Scenario:** `id`, `stableId`, `name`, `result` (enum name), `durationSeconds` (double, `0.0` default), `isHappyPath`, `errorMessage`, `errorStackTrace`, `labels`, `categories`, `rule`, `outlineId`, `exampleValues`, `exampleDisplayName`, `attachments` (`[{name,relativePath}]`), `backgroundSteps`, `steps`, then conditionally `diagrams` + `httpInteractions`.
- **`MapLogJson` (httpInteractions):** `type`, **`method` (UPPER-CASE)**, `uri`, `serviceName`, `callerName`, `content`, `headers` (`[{key,value}]`), `statusCode`, `traceId`, `requestResponseId`, **`timestamp` (`yyyy-MM-ddTHH:mm:ss.fffZ`, ms precision — note the *different* format from start/end)**.
- **`MapStepJson` (the *report* step shape):** `keyword`, `text`, `status`, `durationSeconds` (nullable double), `subSteps` (recursive), `attachments`. **`MapStepJsonFull`** (the *mergeable* superset) adds `bypassReason`, `docString`, `docStringMediaType`, `comments`, `parameters`, `textSegments`. The report uses `MapStepJson`; the merge format uses `Full` — keep both, the merge reader depends on the superset.

**`ScenarioStableId` — replicate the algorithm exactly.** `SHA256(utf8("{feature}::{scenario}"))` — or `"{feature}::{outlineId}::{scenario}"` when an outline id exists — → hex → **first 16 chars, lowercase**. Node: `createHash('sha256').update(input,'utf8').digest('hex').slice(0,16)`. Deterministic (good — anchors/identity are stable cross-run *and* cross-runtime), but the **`::` separators, SHA256, 16-char truncation, and lowercase hex must match exactly** or every `stableId` diverges.

**`ExecutionStatus` — exact string names.** `Passed`, `Failed`, `Skipped`, `Bypassed`, `SkippedAfterFailure` (.NET `ExecutionResult`). Serialized as the **name** (`"result":"Passed"`), so the TS enum's string values must match verbatim.

**`KronikolVersion` — embedded in *every* format → a determinism hazard.** From `AssemblyInformationalVersion` (.NET) / `package.json` (Node). Appears in the HTML `<meta generator>` + a hidden row and as `kronikolVersion`/`KronikolVersion` in JSON/YAML/XML. **It changes per release, so it must be injectable and pinned to a fixed value in deterministic/golden mode** (the Java port hardcodes it). Add it to the §6.1 determinism seam.

**`SanitiseForYml` — port exactly; it's lossy, not standard YAML escaping.** It **replaces** specials with safe text, chained: `[`→`<`, `]`→`>`, `": "`→`" = "`, `#`→`(hash)`, `&`→`(and)`, `*`→`(star)`, `{`→`(`, `}`→`)`, `!`→`(bang)`, `%`→`(pct)`, `@`→`(at)`, `` ` ``→`'`, `|`→`(pipe)`. **Node gotcha:** C# `.Replace` replaces *all* occurrences; JS `String.replace(string,…)` replaces only the *first* — use `replaceAll`/`/g`. Applied to string **values** only (never enum/bool/number fields). YAML also uses 2-space indent, `F3` durations, lowercase bools, and `|` block scalars for embedded diagrams.

**Format-exact number/date rules (each its own parity hazard):** two timestamp precisions (`…ssZ` vs `…ss.fffZ`); `durationSeconds` is a **raw number** in JSON but **`F3`** (`toFixed(3)`) in YAML/XML; bools are `true/false` (JSON) vs lowercased strings (YAML/XML). All dates from the **deterministic `Clock`** (§6.1).

**Empirically resolved (the *a/b* deep dive — verified against a committed `tests/.../Reports/TestRunReport.json` + the XML/Full-step source, not docs):**
- **(a-1) JSON indent = 2 spaces** → `JSON.stringify(x, null, 2)` matches the width. ✓
- **(a-2) `durationSeconds` is a raw number and matches JS** — the file shows `"durationSeconds": 0` (not `0.0`); .NET `System.Text.Json` and JS both use shortest-round-trip and drop the `.0`, so a plain JS number serializes identically for these values. **Do not `toFixed` it in JSON** (only YAML/XML use `F3`).
- **(a-3) ⚠ THE BIG ONE — the data-export JSON uses `System.Text.Json`'s *default* `JavaScriptEncoder`, not `UnsafeRelaxedJsonEscaping`.** The file shows the `+` in the version escaped as **`+`** (uppercase hex). So the data-export escapes `< > & ' +` and **all non-ASCII** to **UPPER-CASE `\uXXXX`** — which **JS `JSON.stringify` does NOT replicate** (it leaves `+`/non-ASCII literal and uses lowercase `\u` for the few it does escape). **The data-export JSON therefore needs a custom string escaper** (post-process values or a custom stringifier) reproducing `JavaScriptEncoder.Default`, **not** native `JSON.stringify` alone. *(This is the same encoder the note-path GraphQL metadata uses, §6.4 — one shared escaper, two uses. It is the **opposite** of the note-body path, which uses UnsafeRelaxed = no escaping. So the three JSON escaping behaviors are: note bodies = none; GraphQL-meta + **data-export** = Default/aggressive/uppercase.)*
- **(b-1) XML — PascalCase elements, omit-null, `F3` durations, lowercase bools** (confirmed from `:4018–4048`): `TestRunReport > {KronikolVersion, StartTime, EndTime, Features > Feature > {Name, Endpoint?, Description?, Labels?>Label, Scenarios > Scenario > {Id, StableId, Name, Result, DurationSeconds(F3), IsHappyPath(lower), ErrorMessage?, …, Steps?>MapStepXml, Attachments?>Attachment>{Name,RelativePath}}}}`. Nulls omitted via `x != null ? XElement : null`. **Serialization confirmed:** both data reports return `doc.ToString()` → **no XML declaration, 2-space indent** (the `*.schema.xml` *does* prepend a declaration, but `TestRunReport.xml`/`Specifications.xml` do not). Node: emit indented XML (2-space) with no `<?xml?>` prolog.
- **(b-2) `MapStepJsonFull` sub-schemas (mergeable only — the "Tabular Attributes" data):** `parameters[]` = `MapStepParameterJson` → `{name, kind, inlineValue?, tabularValue?{columns[{name,isKey}], rows[{type, values[{value,expectation,status}]}], isLinkedOutput}, treeValue?{root}}` with `treeValue.root` = `MapTreeNodeJson` → `{path, node, value, expectation, status, children?}` (recursive); `textSegments[]` = `MapTextSegmentJson` → `{text, parameterName, parameter?{value,expectation,status}, tableReference, tableReferenceFormattedValue}`. The basic report (`MapStepJson`) **omits** all of this — only the mergeable `Full` path needs it.

**Delivery strategy (mirror the Java port's pragmatic call).** Ship the **core model (`Feature`/`Scenario`/`ScenarioStep`/`FileAttachment`/`ScenarioStableId`/`ExecutionStatus`) + all three serializers + the parity tests as one substantial release**, then **iterate against the golden fixtures to byte-parity** — *don't* release the model alone. The serializers are too coupled (shared schema; the `Full` superset) to land piecemeal, and byte-parity is only provable end-to-end.

**.NET refs:** `ReportGenerator.cs` — `BuildFeaturesJsonModel :3739–3796`, `MapLogJson :3918`, `MapStepJson`/`Full :3933–3962`, XML `:4016`, YAML `GenerateTestRunReportYaml :4101` + `AppendTestRunYamlStep/Log`; `YamlExtensions.SanitiseForYml`; `ScenarioStableId.cs`; `ExecutionResult.cs`; `KronikolVersion` (`ReportGenerator.cs:19`).

**Net.** A genuinely extensive surface the plan under-specified. New, evidence-based findings: **three serializers × three null policies × two casings**; the **exact `MapLogJson`/`MapStepJson` schemas** (method-uppercase, two timestamp formats, `durationSeconds` double-vs-F3); **`ScenarioStableId`** (SHA256/16-hex — exact); **`ExecutionStatus`** names; **`KronikolVersion`** as an embedded determinism hazard; the **lossy `SanitiseForYml`** (+ the `.Replace`-all vs `.replace`-first gotcha); and **deliver-together-iterate-to-byte-parity**. Bounded but large — one model+serializers+parity-tests release in Phase 2/3.

---

## 7. Web frameworks (Express + Fastify) — server-side identity (deep dive)

Node's server world, and the home of Layer-1 (header) identity resolution. The .NET version is ASP.NET-integrated (`IHttpContextAccessor` + `TestTrackingContextMiddleware`); Node users expect Express/Fastify equivalents. Reading the .NET middleware + handler (`TestTrackingContextMiddleware.cs`, `TestTrackingMessageHandler.cs`, `HttpHeaders.cs`) settles two things the plan left open: the **exact wire headers** and the **topology-4 rule** (in-process `inject` vs real socket, raised in §5.3).

**The exact headers (port verbatim — they're the cross-service *and* potential cross-runtime contract):**
| Header | Meaning |
|---|---|
| `test-tracking-current-test-name` | test display name |
| `test-tracking-current-test-id` | test id (identity key) |
| `test-tracking-caller-name` | originating service (→ component-diagram edges) |
| `test-tracking-trace-id` | shared trace id across the hop |
| `test-tracking-ignore` | mark this request's logs as ignore-flagged (not dropped) |
| `kronikol-test-name` / `kronikol-test-id` | the messaging-system equivalents (Kafka/SQS/…) |

A Node test hitting a .NET service (or vice-versa) interoperates **only** if these names match exactly — so they're constants in `@kronikol/core`, not per-framework strings.

**The topology-4 rule (confirmed from `TestTrackingContextMiddleware.cs:27–39`).** The middleware establishes a scope from headers **only when both name+id are present; otherwise it passes through, leaving any ambient scope intact.** That one rule resolves the §5.3 ambiguity:
- **Real socket** (`supertest` against a *listening* server, a real server, Playwright): the request crosses a socket → a fresh async context → the test's own ALS does **not** reach the handler. Identity arrives via **headers** (the §3.11 client hook stamps them; the middleware reads them → `als.run`). ✓
- **Synthetic in-process** (`fastify.inject()`, calling `app.handle` directly): no socket, so the §3.11 hook never fires and no headers are stamped → identity flows via the **ambient ALS** (the handler runs inside the test's `als.run` scope). The middleware sees no headers and **must not overwrite** that ambient scope. ✓
- **Load-bearing correctness rule:** *never establish an "unknown" scope when headers are absent* — passing through is what lets `inject`-based tests keep their identity. (This is just Layer-1 vs Layer-3 of the §3.2 cascade, realized at the middleware.)

**Multi-hop propagation composes for free in Node (a clean win over .NET).** When service A calls service B, identity must stay the same test. .NET does this with explicit `IHttpContextAccessor` plumbing in the handler (read incoming headers, forward them). In Node it's **automatic**: the middleware put identity in ALS (from incoming headers), and the §3.11 client hook reads identity from ALS at the call site (§3.2 bind-at-call-site) → re-stamps it on the outgoing request. So `test → A → B → …` keeps one `test-id` + `trace-id` with **no relay code** — the §7 (header→ALS) and §3.11 (ALS→header) halves chain transitively.

**The real Node-specific sharp edge — Express `run` vs Fastify `enterWith`:**
- **`@kronikol/express`** — `app.use((req,res,next) => headers ? als.run(identity, next) : next())`, registered **before routes**. `next()` is a callback, so `als.run` wraps the entire downstream chain cleanly and auto-unwinds. (Honor `test-tracking-ignore`.)
- **`@kronikol/fastify`** — Fastify's `onRequest` hook gives **no "wrap the rest of the request in a callback" seam** (Fastify owns continuation), so `als.run` doesn't fit. Use **`als.enterWith(identity)` in `onRequest`** — sets the store for the request's (bounded) async context; the no-auto-unwind caveat (§3.2) is acceptable because each request is its own context, but it's the one place we knowingly use `enterWith` over `run`. **And register with `fastify-plugin`** (skip-encapsulation) so the hook applies app-wide rather than to one encapsulated subtree. This Express/Fastify asymmetry is the genuine porting risk, not the header reading.

**Ties to the rest:** this is the **server half of the §3.11 client→server loop** and the **attributor for §5 mode-3** (out-of-process E2E: the server is the sink, the middleware attributes each request to a test). `ExcludedHosts`/infra-call suppression and `HeadersToForward` (forward selected app headers downstream) port from the .NET handler as config. In-process test integration (`supertest`, `fastify.inject()`, direct invocation) is the analog of `Mvc.Testing`.

**Sequencing:** minimal `@kronikol/express` + `@kronikol/fastify` land **alongside Phase 4's HTTP tracker** so client→server propagation works end-to-end; the full Fastify plugin + supertest/inject helpers round out in Phase 5.

**Net.** The header reading is trivial; the deep dive's real outputs are: the **exact header contract** (cross-service/cross-runtime), the **topology-4 rule** ("scope from headers only when present; never shadow the ambient scope" — resolving §5.3), the finding that **multi-hop relay is free in Node** (ALS both halves) where .NET needs `IHttpContextAccessor` plumbing, and the **Express-`run` vs Fastify-`enterWith`+`fastify-plugin`** asymmetry as the one genuine porting risk.

---

## 8. Package structure (engineered for parity extensibility)

pnpm workspace under `packages/`. npm scope `@kronikol/*`. TypeScript strict; tsup dual ESM+CJS.

```
js/                            # the js/ subtree of the Kronikol monorepo
├── pnpm-workspace.yaml · turbo.json · tsconfig.base.json · .changeset/
├── packages/
│   ├── core/                  @kronikol/core — THE SEAM. Zero runtime deps.
│   │   ├── tracking/          RequestResponseLog (+builder), RequestResponseLogger (+capture-time redactor §3.13), StepCollector + step() (§3.14)
│   │   ├── context/           AsyncLocalStorage-backed TrackingContext, TestIdentityScope (4-layer),
│   │   │                      TestPhaseContext, TestInfoResolver, TestCorrelationStore, CorrelationKeys, ProcessingCorrelation
│   │   ├── registry/          TrackingComponentRegistry, TrackingComponent (invocation tracking → §3.18 diagnostics), globalThis state registry (§3.10)
│   │   ├── support/           IdGenerator, Clock (determinism), SafeSerializer
│   │   └── constants/         DependencyCategories, header names, defaults
│   ├── diagram/               @kronikol/diagram — PlantUmlCreator, encoder (zlib), DependencyPalette, ComponentDiagram (PURE, zero deps)
│   ├── report/                @kronikol/report — Feature/Scenario/Step models + ScenarioStableId/ExecutionStatus, ReportGenerator (TestRunReport + Specifications §4.6), data-export serializers (§6.7), Diagnostics report (§3.18), HTML assembly, embedded JS/CSS, merge engine (yaml dep)
│   ├── runtime/               @kronikol/runtime — run dir, per-file fragment writer, merger, mode detection (§5), CI integration (§3.17)
│   ├── http/                  @kronikol/http — fetch/undici + axios + got + node:http client tracking
│   ├── express/               @kronikol/express — Layer-1 middleware (§7)
│   ├── fastify/               @kronikol/fastify — Layer-1 plugin (§7)
│   ├── sql/                   @kronikol/sql — per-driver wrappers (pg, mysql2, sqlite, mssql, oracledb) + ORM hooks
│   ├── proxy/                 @kronikol/proxy — generic Proxy tracker
│   ├── assert/                @kronikol/assert — Tier 0/1 assertion tracking (node:assert/chai/expect)
│   ├── vitest/ · jest/ · node-test/ · mocha/ · cucumber/ · playwright/   test-framework adapters (+ run-completion)
│   ├── opentelemetry/         @kronikol/opentelemetry — span/trace bridge + flame/activity (§3.8)
│   ├── redis/ · mongodb/ · kafka/ · ... messaging/cloud adapters (Phase 5+)
│   └── cli/                   @kronikol/cli — `merge` command (§5.5), bin entry
└── internal/dual-package/     Node-internal dual ESM+CJS regression suite (§3.10) — not the cross-runtime corpus
                               (cross-runtime goldens live in the repo-level ../parity/, shared with java/)
```

`@kronikol/core` and `@kronikol/diagram` must have **no dependency** on any tracked technology, OTel, or web framework — that purity is what makes them a stable foundation. Every adapter declares the tracked library (`pg`, `ioredis`, `kafkajs`, `express`, …) as a **`peerDependency` (+ `peerDependenciesMeta.optional`)**, never a hard dep — Node teams reject heavy/version-opinionated trees.

---

## 9. Phased delivery

Each phase follows the repo's TDD rule (red → green → refactor). Phases 0–4 deliver a working, end-to-end, *narrow* product. Phase 5+ is parallelizable breadth.

### Phase 0 — Foundations, parity harness & blockers
- **Build skeleton:** pnpm workspace rooted at `js/`, Turborepo pipeline, tsup dual-build convention, `tsconfig.base`, Node-20 baseline, **`js-ci.yml`** (path-filtered on `js/**` + `parity/**`), changesets + npm publish scaffold, versioning synchronized across the `@kronikol/*` packages.
- **Shared .NET-side prep (§6.2)** *(blocker)* — determinism seam, asset externalization, parity-hardening + client-side splitting. **Reuse from `java/` if already done.** In the monorepo this lands as an ordinary commit touching `dotnet/` and `parity/` in the same PR as its Node consumer — no cross-repo coordination, and `parity.yml` proves both stacks still agree.
- **TS determinism seam (§6.1):** injectable `IdGenerator`/`Clock` + normalization pass.
- **Parity corpus (§6.3):** port BreakfastProvider + the feature-coverage checklist.
- **Golden-file harness (§6.6):** a `.NET KronikolParityCapture` tool (deterministic mode, pinned TFM + InvariantGlobalization, **pre-encode PlantUML tap**) emits fixtures → the **single TS canonicalizer** (applied to goldens at capture *and* Node output at assert) → comparison against the repo-shared `parity/fixtures/`. Encoder verified by **round-trip + cross-decode using the real `plantuml-render.js` decoder run natively** (not byte-pinned). Wire the **two-direction** parity-diff — both directions per-PR via `parity.yml`, since the capture tool references `dotnet/` in-tree (§6.6e).
- **JSON note formatter (§6.4):** reproduce .NET's per-path behavior (key order, null-stripping, 2-space, both escaping modes).
- **Run-lifecycle / worker-aggregation spike (§5)** *(blocker — the one real remaining unknown):* satisfy the **six acceptance criteria in §5.9** — Vitest `forks` **and** `threads` + Jest workers each emit a complete fragment (atomic), merged deterministically (order-independent); **no cross-file bleed** in a reused worker (§5.5); **concurrent-in-file** attribution via ALS (§5.6); zero-config **self-finalize** (mode 1); **killed-worker** graceful degradation; and the **out-of-process E2E** topology (mode 3) where the app server is the sink and identity flows via headers. (The deep dive established the design — **fragment-per-file**, three finalize modes; the spike is now validation, not discovery.)
- *(No standalone async-context blocker spike — `AsyncLocalStorage` is native, §3.2. The one validation that matters — **bind-at-call-site** under pooled/parallel DB access — is folded into the §5.9 criteria, not a separate spike.)*
- **Dual-package singleton registry (§3.10)** *(core-design invariant, not a Phase-4 afterthought):* stand up the `globalThis` symbol-registry for all `@kronikol/core` ambient state, plus the **post-build dual-load regression test** (`import` + `require` the built artifact in one process, assert a shared sink) and the **attw + publint** CI gates on every package's `exports` map. Designing this now is cheap; retrofitting it after extensions exist is not.
- Lock cross-cutting primitives: union types (§3.1), `AsyncLocalStorage` context facade (§3.2), record+builder (§3.3), JSON/yaml stack (§3.6), the `globalThis` state registry (§3.10).

### Phase 1 — Core ingestion seam (`@kronikol/core`)
Freeze the contract every extension depends on.
- `RequestResponseLog` (+ builder); `RequestResponseType`, `RequestResponseMetaType`, `PhaseVariant`, `TestPhase`.
- `RequestResponseLogger` (async-safe sink; `maxContentLength` truncation **+ the capture-time redactor (§3.13)** — headers/content masked *before* entering the sink so no secret reaches any output or the fragment; `getAllLogs()`/`clear()`) — **a thin free-function API over the `globalThis` state registry (§3.10)**, read once at each file boundary by `@kronikol/runtime` to emit this file's fragment (§5.4). `clear()` truncates in place. **Every core singleton in this phase (logger queue, the single `AsyncLocalStorage` instance, `TestCorrelationStore`, registry, phase context, `IdGenerator`/`Clock`, config) is resolved through that registry** — this is the invariant that keeps dual ESM+CJS from silently splitting state.
- `TestIdentityScope` (4-layer, ALS-backed), `TestPhaseContext`, `TestInfoResolver`, `PhaseConfiguration`.
- **`StepCollector` + `step()` API (§3.14)** — the framework-agnostic step engine (`Map<testId,state>` + per-test stack, sub-step nesting, keyword→"And" sequencing, keyword→phase) and the always-available `step(keyword, text, async fn)` wrapper. Timing via the deterministic `Clock` (§6.1); assertions attach as sub-steps (§3.9).
- **Context + correlation (§3.2):** the `TrackingContext` facade (`als.run`-based, auto-unwind), `TestCorrelationStore` (Map + TTL, data-keyed), `CorrelationKeys`, `ProcessingCorrelation.wrap(...)`.
- `TrackingComponentRegistry` + `TrackingComponent`; `DependencyCategories`; HTTP/message header constants; `IdGenerator`/`Clock`/`SafeSerializer`.
- Full TDD coverage. **Output: a stable seam, even with no extensions yet.**

### Phase 2 — Core output pipeline (`@kronikol/diagram`)
Pure, high-value, verified against Phase-0 golden files.
- Port `PlantUmlCreator` (pure string building): **sequence** + **component** now; activity + flame land with the OTel bridge (Phase 5). Assert normalized parity vs C# golden PlantUML.
- Apply §6.5 discipline: client-side splitting (one un-split diagram/test); `\n` only; invariant casing/number formatting; ordinal header sort; deterministic component ordering.
- Port `PlantUmlTextEncoder` (`zlib.deflateRawSync` + custom base64). **Verify by round-trip + decoded parity, not encoded bytes (§6.4).**
- Per-path JSON note formatting (§6.4); `DependencyPalette` (static map); **`ComponentDiagramGenerator` + the dependency-analytics engine (§3.19)** — C4 PlantUML + per-edge stats (percentiles/outliers/CV/payload/concurrency), graph metrics (fan-in/out, cycle detection, longest-chain), call-ordering, error-correlation. **All timing-derived → depends on the §6.1 deterministic clock for golden-stable stats.**
- Models: `Feature`, `Scenario`, `ScenarioStep`, `FileAttachment`, `PlantUmlForTest`, `DiagramAsCode`, **`ScenarioStableId`** (SHA256/16-hex, exact — §6.7) and **`ExecutionStatus`** (`Passed`/`Failed`/`Skipped`/`Bypassed`/`SkippedAfterFailure` — exact names). **No rendering abstraction — browser-only.**

### Phase 3 — HTML report + frontend reuse (`@kronikol/report`)
Staged per §4 (genuine surface ~3,800 lines):
- **Consume the externalized Bucket-A assets** (§4.2) — bundle the *same* `.js`/`.css` files; wire the compressed-diagram data map for `plantuml-render.js`. Asset parity by construction.
- **Escaping shim (§4.4/§6.4):** reproduce `WebUtility.HtmlEncode` exactly; unit-test first.
- **Port Bucket B** (`<head>` + `<body>` builder + render helpers) via template literals + a small HTML-builder; verify by **golden-HTML diff**.
- **Port Bucket C** carefully — the **`RenderParameterizedGroup` pivot *engine*** (the two-level rule cascade + tables, **§4.5**), verified by golden-HTML + the ported `ParameterGrouperTests`, fed by **hand-authored `Scenario` inputs** (the per-adapter `ExampleValues`/`OutlineId` *capture* lands later with each Phase-4/5 adapter); drop the .NET record-`ToString()` flatten (no Node analog).
- **The data-export serializers (§6.7)** — native JSON (camelCase, **nulls written**, **custom default-encoder escaper** §6.7 a-3) + **hand-built** YAML/XML (PascalCase, **nulls omitted**, lossy `SanitiseForYml`), with `ScenarioStableId`/`ExecutionStatus`/pinned `KronikolVersion` and the exact `MapLogJson`/`MapStepJson` schemas. **Ship model + all three serializers + parity tests as one release and iterate to byte-parity** — do not land piecemeal.
- **Both reports + the Specifications serializers (§4.6)** — the same `GenerateHtmlReport` with `includeTestRunData=false` (docs-style branch + violet stylesheet) for `Specifications.html`; the **3 simpler specs data serializers** (behavior-only, text-only steps, version-free); and the **blank-on-failure** semantic (specs HTML *and* data write empty if any scenario failed).
- **Component-diagram HTML analytics (§3.19)** — the performance summary table + latency-distribution bar chart (Bucket B templating) and the focus/diff interactivity (Bucket A client-side JS, keep chart geometry client-side to dodge §6.4 floats).
- **Run the search-engine suite (~143 cases) against the identical `advanced-search.js`** — native, no engine.
- **Stand up Playwright E2E (`@playwright/test`) against Node-generated HTML** — proves UI + browser-rendering parity end-to-end; CLAUDE.md Playwright rules carry over.

### Phase 4 — First real adapters + run lifecycle (prove the seam in three shapes)
- `@kronikol/runtime` (the adapter SPI + fragment writer + merge engine + mode detection) + `@kronikol/vitest` (first adapter): Vitest lifecycle (per-test `als` scope, identity, phase detection) **and** the **per-file** fragment emit + sink-clear (§5.4–5.5) + reporter-orchestrated merge; covers modes 1 (self-finalize) and 2 (runner-orchestrated), validated under both `forks` and `threads` pools (§5.3–5.9). Also the **first parameter-capture** site: map `test.each` args → `ExampleValues`/`ExampleRawValues` to feed the §4.5 pivot engine. And the **first phase source (§3.14):** wrap `beforeAll`/`beforeEach` in `TestPhaseContext=Setup`, the test body in `Action` (non-BDD phase without steps).
- `@kronikol/http` (+ minimal `@kronikol/express` **and** `@kronikol/fastify`): HTTP tracker via the **two disjoint transport hooks (§3.11)** — undici `Dispatcher` interceptor (`fetch`) + idempotent `node:http`/`https` monkeypatch (axios/got/…), with bounded body-tee capture → `logPair` path **and** client→server identity-header propagation from ALS. The web middleware implements the **§7 topology-4 rule** (scope from `test-tracking-*` headers only when present, never shadow the ambient scope; Express `als.run(next)` vs Fastify `enterWith`+`fastify-plugin`) — proving the full client→server loop + multi-hop relay. Validate coverage across `fetch`, axios, and got in the corpus.
- `@kronikol/sql` (start with `pg`): relational tracker → direct `log(...)` path; **bind identity at the call site, tag the in-flight query, never re-resolve at completion** (§3.2) — the rule that keeps attribution correct under connection pooling + parallel tests.
- `@kronikol/proxy`: generic `Proxy` tracker → the `DispatchProxy` pattern.
- **Assertion Tier 0:** the `track(description, () => …)` manual wrapper (§3.9).

**End of Phase 4 = a genuinely usable Kronikol.js:** write a Vitest test, hit an HTTP API and a Postgres database, get the same interactive diagrammed HTML report the .NET version produces — including across worker processes.

### Phase 5+ — Breadth to full parity (parallelizable)
Each is a thin package against the now-stable seam:
- **More test adapters:** Jest, node:test, Mocha, Cucumber-js (BDD: Given→Setup / When·Then→Action; **Examples → `OutlineId`/`ExampleFlatValues`** for the §4.5 pivot; **`BeforeStep`/`AfterStep` → `StepCollector` for native Gherkin steps**, §3.14), Playwright (the first **mode-3 / out-of-process** adapter — server-as-sink + header-borne identity, §5.3/§7; **native `test.step()` → `StepCollector`**). Each adapter captures its parameterization (§4.5), feeds steps/phase (§3.14), and re-tunes the display-name parser.
- **Web:** full Fastify plugin; supertest/inject helpers; framework-specific niceties.
- **More SQL drivers:** mysql2, sqlite (better-sqlite3 + node:sqlite), mssql, oracledb, `postgres` (porsager) — each the same choke-point wrap as pg (§3.12). **`@kronikol/prisma`** via `$extends` (the one ORM that bypasses the driver). Optional ORM-enrichment adapters (TypeORM/Sequelize/Knex/Drizzle) as *replace-not-stack* opt-ins. **OTel DB-span bridge** as the shallow long-tail catch-all.
- **Messaging:** kafkajs, RabbitMQ (amqplib), cloud pub/sub (SNS/SQS, Service Bus/Event Hubs, GCP Pub/Sub).
- **Caching:** Redis via `ioredis`/`redis`.
- **Cloud SDKs:** `@aws-sdk/*` v3, `@azure/*`, `@google-cloud/*` (BigQuery, Bigtable, Spanner, Storage).
- **NoSQL:** MongoDB, Cassandra, Elasticsearch.
- **Architectural:** gRPC interceptors; `@kronikol/opentelemetry` bridge (§3.8) → the **internal-flow / activity + flame** subsystem (§3.16) — an OTel `SpanProcessor` capture + pure-PlantUML activity + client-side flame chart; opt-in (needs the SUT on OTel).
- **CI integration (§3.17):** GitHub Actions + Azure DevOps env-var detection, metadata, artifact publishing, and CI-summary markdown that **links to** the HTML report (no inline server-rendered images, §3.5) — wired into the §5 finalize/merge (once-per-run); optional GitLab/CircleCI/Jenkins breadth.
- **Diagnostics report (§3.18):** the standalone `DiagnosticReport.html` tracking-health dashboard — config dump, per-service/test counts, unresolved-identity + unpaired + orphaned warnings, the **`TrackingComponentRegistry` "never-invoked"** check, unmatched targets, and the **Node-native dual-package-split detector** (§3.10). Structural tests (lower parity bar).
- **Assertion tracking:** Tier 1 `@kronikol/assert` (runtime hooks — early); Tier 2 build-time transform plugin (later, only if demanded).

---

## 10. Testing & parity strategy
- **TDD** per CLAUDE.md: red → green → refactor on every unit.
- **Determinism first (§6):** seeded IDs + a reproducible monotonic clock (the ordering key, §6.1) + the symmetric canonicalizer make snapshots stable.
- **Golden-file / snapshot parity** (the backbone, §6.6): assert **decoded** PlantUML, report HTML, and JSON/YAML/XML match C#-captured fixtures after the **single symmetric canonicalizer**; HTML gated **two-tier** (normalized exact-text + structural/parse5); encoder verified by round-trip + cross-decode via the native `plantuml-render.js`, **not** byte-pinned; parity-diff runs **two directions** (Node-regression + .NET-drift).
- **Reusable test assets (§4.3):** the **search-engine suite (~143 cases)** runs against the identical `advanced-search.js` natively; the C# AngleSharp structural assertions port as structural checks. No golden HTML exists today — the harness is new Phase-0 infra.
- **Playwright E2E reuse** (`@playwright/test`) for the interactive UI + browser rendering, against Node-generated HTML.
- **Worker-aggregation tests:** run the suite under Vitest `pool:'forks'` **and `pool:'threads'`** and Jest multi-worker to prove cross-process *and* cross-thread merge — each realm has its own `globalThis` sink (§3.10), so both pools must emit one fragment per file (§5.4).
- **Dual-package singleton regression (§3.10):** a post-build `js/internal/dual-package` test that loads the **built** `@kronikol/core` via both `import` and `require` in one process and asserts a single shared sink/ALS. Plus **attw + publint** as CI gates on every package's `exports` map. The hazard is silent, so this guard is non-negotiable.
- **Cross-runtime parity CI job:** periodically regenerate fixtures from C# and diff (locked sync strategy).
- **Runner test:** unit-test under each adapter (Vitest/Jest/node:test/Mocha/Cucumber/Playwright) — the lifecycle seam is the riskiest cross-runner surface.

---

## 11. Publishing & distribution
NuGet auto-packaging → **npm** (simpler than Maven Central):
- **Coordinates:** scope `@kronikol/*`, public access (`npm publish --access public`).
- **Per package:** dual ESM+CJS build (tsup), `.d.ts`, correct `exports`/`main`/`module`/`types`, `files` whitelist, `sideEffects:false` where safe, `peerDependencies` for tracked libs, `repository`/`license`/`homepage` metadata, LICENSE + README + icon.
- **Versioning:** **changesets** — synchronized version across all `@kronikol/*` packages, generated changelog, `changeset version` + `changeset publish` in CI on tag. Note the monorepo scopes CLAUDE.md's "all packages same version" rule to *within a language stack*: `js/` versions independently of `dotnet/` (3.x) and `java/` (0.1.x), and only the `@kronikol/*` set moves in lockstep. **Provenance** (`npm publish --provenance`) via GitHub Actions OIDC.
- License + branding continuity: carry the existing LICENSE and icon.

---

## 12. Documentation & release automation
CLAUDE.md mandates wiki + changelog upkeep and synchronized versioning.

### 12.1 The Kronikol.js wiki (parity with Kronikol.wiki)
The existing `Kronikol.wiki` is mature (89 pages, ~25,600 lines). GitHub allows **one wiki per repository**, so with `js/` inside the monorepo there is no `KronikolJS.wiki` to ship: Kronikol.js documentation goes into **`../Kronikol.wiki` under a `Kronikol.js` section**, page names prefixed to avoid collisions with the .NET and Java sets. Same information architecture, Node content:
- **Home / Demo**, **Getting Started** (Quick Start: Vitest + pnpm, plus a Jest variant; AI-integration prompt; Framework Integration matrix).
- **Framework Integration guides** — Vitest, Jest, node:test, Mocha, Cucumber-js, Playwright (replacing the xUnit/NUnit/MSTest/TUnit/BDDfy/LightBDD/ReqNRoll set).
- **Extension guides** — one page per package (HTTP/fetch/axios, Express, Fastify, SQL/pg/mysql2/sqlite, Redis, Kafka, RabbitMQ, MongoDB, AWS/Azure/GCP SDKs, gRPC, OpenTelemetry, proxy…), same template each.
- **Configuration**, **Features** (Generated Reports, Component Diagrams, **Browser Rendering** — now the only path, Internal Flow, Step/Event Tracking, Tabular Attributes, Search Syntax, Large-Diagram Handling, CI Summary/Artifacts, **Merging Parallel Reports** — the worker-aggregation + `@kronikol/cli merge` guide, §5.5).
- **Node-specific pages (new):** *Worker-Process Log Aggregation* (§5), *Test-Identity Propagation & AsyncLocalStorage* (§3.2), *Determinism & the Golden-File Workflow* (§6), *Migrating from Kronikol (.NET) concepts*.
- **Reference** — API reference per package, Example Project walkthrough.
- **Assets:** regenerate screenshots/GIFs from Node-generated reports (UI is identical). Reuse the original's page templates + `[[WikiLink]]` density; only code samples change (TS + pnpm).

### 12.2 Docs-as-you-go (wired into phasing)
- **Phase 0–1:** the Node-specific concept pages (ALS context, worker aggregation, determinism).
- **Phase 2–3:** Browser Rendering, Generated Reports, Component Diagrams, Diagram Customisation.
- **Phase 4:** Quick Start (Vitest), first framework + extension guides, Home/Demo.
- **Phase 5+:** one wiki page lands **with** each new adapter package (definition of done includes its guide).

### 12.3 Other docs & release automation
- **`CLAUDE.md`** capturing Node conventions (TDD, Playwright rules carried over, determinism/golden-file workflow, the worker-aggregation gotcha, wiki-per-package rule).
- **README + Changelog** maintained per release (changesets-generated).
- **Release automation:** changesets single-source-of-truth version applied to all packages; a release workflow triggered on **`js-v{version}`** tags (the repo's tag namespace is split per stack — `dotnet-v*`, `java-v*`, `js-v*`) that versions, updates changelog, publishes to npm with provenance, and reminds to update the wiki.

---

## 13. Rough sizing (relative, not a commitment)
| Phase | Relative size | Notes |
|---|---|---|
| 0 — Foundations & blockers | M | **One** spike (worker aggregation) + harness + publishing — smaller than Java's (no async-context spike) |
| 1 — Core ingestion seam | S–M | Small surface; ALS makes context nearly free; must be exactly right |
| 2 — Diagram pipeline | M | Mostly mechanical port, golden-verified |
| 3 — HTML report + frontend | M | genuine surface ~3,800 lines; ~64% copies verbatim; search suite + Playwright reuse natively |
| 4 — First adapters + lifecycle | M | Proves all three ingestion patterns + worker aggregation + client→server propagation |
| 5+ — Breadth to parity | XL (parallelizable) | Many thin packages; scales with concurrency. Strong multi-agent-orchestration candidate once the seam is frozen |

Phases 0–4 are the critical path to a usable product. **Overall this port is *lower-risk* than Kronikol4J** — the async-context risk evaporates, the frontend + search suite run natively, and unions/mutability are native — at the cost of one harder area (per-driver DB tracking, §3.12).

---

## 14. Risks & decisions

**Top risks (re-rated for Node):**
1. **Worker-process aggregation (§5)** — **the #1 build risk** (was #2 in Java). The merge engine is a proven port (.NET *and* Java). The deep dive (§5.3–5.9) reframed the hard half: it's **two axes / three finalize modes**, not six runners; **fragment-per-file** (not per-worker) sidesteps Node's absent per-worker hook *and* closes a silent **cross-file-bleed** bug (§5.5, the §3.10 twin); concurrent-in-file correctness **rides on `AsyncLocalStorage`** (§5.6); and the **out-of-process E2E mode (3)** — the app server as sink with header-borne identity — was missing and is now designed in. A six-row adapter SPI binds it to every runner. Bounded; the Phase-0 spike validates against §5.9's criteria.
2. **Dual-package state splitting (§3.10)** — **the #1 *silent* risk**, unique to the locked dual ESM+CJS decision. Kronikol *is* a shared global sink, so a split core copy yields empty/partial reports (or mis-attributed identity via a split `AsyncLocalStorage`) with **all tests green**. Neutralized by the `globalThis` major-versioned symbol registry (OTel's exact pattern), a post-build dual-load regression test, and attw+publint CI gates. A Phase-0/1 core-design invariant, not a late fix. *Escape hatch:* ESM-only once `require(ESM)` is universal.
3. **Golden-file determinism (§6)** — hard Phase-0 gate, now **demonstrated buildable by Kronikol4J** (§3.24): the Java sibling shipped the `dotnet-capture` tool, a committed feature-keyed golden corpus, byte-for-byte parity tests, and offline-Playwright rendering — the exact backbone this plan designs. The deep dive (§6.6) made the harness concrete: a .NET capture tool with a pre-encode tap, **one symmetric canonicalizer** (not two), a **monotonic Clock that doubles as the ordering key** (§6.1), native `plantuml-render.js` decode for encode parity, and a two-direction parity-diff. **The residual risk is corpus *branch-coverage*** (§3.24's #1 lesson — whole features hide behind un-triggered inputs), not the harness itself. It also surfaced a hazard the plan was missing — **transcendental floats (`sin`/`cos`) aren't bit-reproducible across .NET and V8** (§6.4), fixed by rounding pie-chart geometry. Bounded, but genuinely new infrastructure (no golden HTML exists in .NET today).
4. **Per-driver DB tracking (§3.12)** — **the one area harder than Java** (no JDBC universal hook), now de-risked by the deep dive to a **bounded breadth grind**: driver-level public-API wrapping is the universal primary hook and **transparently covers Knex/TypeORM/Sequelize/Kysely/Drizzle/MikroORM**, so it's **one driver layer + the single Prisma `$extends` exception** + OTel as a shallow catch-all (HTTP-based DBs already handled by §3.11). Real work = ~5 driver adapters (pg first), each small; sharp edges = pool→connection re-entrancy double-count, promise/callback/cursor permutations, param redaction, driver-vs-ORM exclusivity.
5. **HTML-assembly volume (§4)** — **low**: ~64% static JS/CSS copies verbatim and *runs natively*; externalize-first (§4.2) shrinks the surface to ~3,800 lines. The biggest logic piece — the `RenderParameterizedGroup` pivot — is now deep-dived (§4.5): a ~1,000-line subsystem, ≈80% mechanical, with its engine in Phase 3 and per-adapter capture split into Phase 4/5. Residual: the serializers and the `WebUtility.HtmlEncode` shim (§4.4).
6. **Cross-runtime output formatting (§6.4–6.7)** — 13 PlantUML divergence points + the structural split-on-Deflate hazard (removed by client-side splitting) + the **data-export serializers (§6.7)**, which the deep dive showed are larger than assumed: **three formats with three different null policies + two casings**, exact schemas (`MapLogJson` method-uppercase, two timestamp formats), `ScenarioStableId` (SHA256/16-hex), `KronikolVersion` as an embedded determinism hazard, and a **hand-built lossy `SanitiseForYml`** (the `yaml` npm package can't reproduce it). Bounded but extensive — proven only by iterating model+serializers+parity-tests to byte-parity.
7. **Two/three-codebase drift** — periodic parity-diff (locked); the .NET prep is shared with Kronikol4J.
8. **Assertion tracking (§3.9)** — baseline free via runtime hooks (Tiers 0/1); *automatic* expression text needs a Tier-2 build transform (the power-assert pattern) — optional, isolated.
9. **Async context (§3.2)** — **re-rated from Java's top risk to near-zero**, and the deep dive confirmed it: `AsyncLocalStorage` is native, auto-flowing, auto-unwinding. The one real rule it surfaced is **bind-at-call-site** (read identity when the dependency method is invoked, tag the in-flight op, never re-resolve in a completion callback) — violating it silently mis-attributes under connection pooling + parallel tests. Consumers/pub-sub/change-streams are headers+correlation *by design*, not a regression. Residual: don't cross worker/child boundaries with ALS, prefer `run` over `enterWith`.
10. **Secret leakage / redaction (§3.13)** — the deep dive found .NET's redaction is **presentational only** (render-time), so secrets reach the JSON/YAML/XML **and the on-disk worker fragment**, with **nothing redacted by default**. Mitigation is a clear design (capture-time redactor at Seam A + opt-in secure preset + DB connection-string/param redaction), but it must be **built into core from Phase 1** — retrofitting redaction after adapters exist re-introduces the leak. Security-sensitive; the achievable guarantee ("no secret in any artifact") holds only in redact-at-capture mode.

**Locked decisions:** Kronikol.js / `@kronikol/*` / **`js/` subtree of the Kronikol monorepo** / pnpm+Turborepo+changesets / dual ESM+CJS / Node 20 floor / Vitest+Jest+node:test+Mocha+Cucumber+Playwright / Express+Fastify / **browser-only rendering** / functional parity / periodic parity-diff.

---

## 15. Engineering standards & compatibility
- **Dependency philosophy (adoption-critical).** `@kronikol/core` and `@kronikol/diagram` have **zero runtime deps**; `@kronikol/report` owns `yaml`. Every adapter declares the tracked library as a **`peerDependency` (optional)**, never a hard dep — no forced versions, no transitive bloat.
- **Node & module compatibility.** Build/test on Node **20 / 22 / current**. Ship **dual ESM+CJS** with a correctly-ordered `exports` map (`types` → `import` → `require` → `default`, plus `main`/`module`). **The dual-package hazard is a first-class design constraint (§3.10), not a caveat:** all `@kronikol/core` ambient state lives in a `globalThis` major-versioned symbol registry so ESM and CJS copies share one sink/ALS; **never** rely on `instanceof` across package boundaries (use structural typing); inline embedded assets as build-time strings (no runtime `__dirname`/`import.meta.url` divergence); no top-level `await` in core. CI gates every package with **`@arethetypeswrong/cli` + `publint`**, and a post-build dual-load test guards the registry.
- **Same-process parallelism is first-class.** Vitest/Jest can run tests concurrently on the same worker; `AsyncLocalStorage` per-test scoping (§3.2) keeps identity correct under in-process concurrency — its own test suite + wiki page.
- **TypeScript types are the public contract.** Ship `.d.ts` for every package; treat type-level breaking changes as semver-major.
- **Versioning relationship to .NET.** Independent SemVer, starting pre-1.0, reaching 1.0 when core + first adapters (Phases 0–4) are stable; does **not** lockstep .NET 3.x — sharing a repository does not mean sharing a version, and the split tag namespace (`dotnet-v*` / `java-v*` / `js-v*`) is what keeps the three release trains from firing each other's publish jobs. The wiki documents the feature-parity mapping per release. (Same stance as Kronikol4J.)
- **CI (GitHub Actions).** `js-ci.yml`, path-filtered on `js/**` + `parity/**`: matrix across Node versions; Playwright E2E against Node-generated HTML; npm publish with provenance on `js-v*`; wiki link-check. The cross-runtime parity-diff (§6, §10) is **not** a Node-local job — it lives in the repo-shared `parity.yml`, which runs both toolchains on one runner and is unfiltered so it gates every stack. Minimal in Phase 0, grown per phase.
- **Security / redaction parity (deep dive §3.13).** The deep dive corrected this: .NET's exclusion/processors are **presentational** (diagram-render time), so excluded headers still leak into JSON/YAML/XML **and the worker fragment**, and nothing is redacted by default. The Node port **replicates the presentational layer for diagram parity** *and* adds **capture-time redaction at Seam A** (the only complete-coverage point) + an opt-in secure preset + DB connection-string/param redaction. Security tests assert "no secret in any artifact (incl. the fragment)" **in redact-at-capture mode** — the achievable claim; parity mode matches .NET (secret persists in data, asserted separately).

---

## Appendix A — Node-specific source-map deltas

The **full .NET source map is the Java plan's Appendix A** (`docs/JAVA_PORT_PLAN.md`) — use it verbatim; line numbers are at v3.0.40–43 and may drift. Node-specific notes on top of it:
- **Browser-only excludes:** `src/Kronikol/PlantUml/NodeJsPlantUmlRenderer.cs` (server-side SVG via bundled Node JS — **not ported**), `Kronikol.PlantUml.Ikvm`, the IKVM/server-render paths, `PlantUmlRendering.cs`/`DefaultDiagramsFetcher.cs` rendering strategy.
- **Frontend runs natively (no engine):** `src/Kronikol/Reports/advanced-search.js` (286) + `src/Kronikol/PlantUml/plantuml-render.js` (363) — embed and run as-is; the **search-engine suite** (`tests/Kronikol.Tests.SearchEngine/`, ~143 cases) runs directly.
- **The mergeable model to port:** `src/Kronikol/Reports/Merge/MergeableReportMerger.cs` + `MergeableReportReader.cs` + `MergeableReportRenderer` — and Kronikol4J's already-ported `MergeableReportMerger.java` / `MergeableReportRenderer.java` / `kronikol4j-cli` as a second reference implementation.
- **Kronikol4J as a second reference:** `java/` — its `kronikol4j-core` (context/correlation already realized), `kronikol4j-diagram` (`PlantUmlTextEncoder.java`, `NoteFormatter.java`, `Json.java`), `kronikol4j-report` (`HtmlEscaper.java` — mirror for the WebUtility shim), `kronikol4j-runtime`, and the adapter packages show the decomposition already working in a second language.

## Appendix B — Open questions & deferred decisions
- **DB tracking strategy** — **decided (§3.12):** driver-level public-API wrap as the universal primary (covers all real-driver ORMs), Prisma `$extends` as the sole exception, ORM hooks as replace-not-stack opt-ins, OTel DB spans as the shallow long-tail catch-all. Open sub-question: how deep to capture result rows by default (rowcount-only vs bounded sample) given size/security trade-offs — settle with the corpus (§6.3).
- **node:test aggregation hook** — the cleanest once-per-run signal (custom reporter vs a wrapper bin) — confirm in the Phase-0 spike.
- **Dual-package singleton** — **decided (§3.10):** `globalThis` major-versioned symbol registry for all core ambient state, post-build dual-load regression test, attw+publint gates. Open sub-question: when to flip `@kronikol/core` to **ESM-only** once `require(ESM)` is universal on the supported Node floor (removes the hazard by construction) — revisit when the floor reaches Node 22+.
- **Tier-2 assertion transform** — whether to build the power-assert-style build plugin at all is demand-driven; Tiers 0/1 may suffice indefinitely.
- **Bundler coverage for Tier 2** — which transforms to support (Vite, SWC, Babel, esbuild, ts-patch) — Phase 5.
- **Cucumber phase mapping** — exact Given→Setup / When·Then→Action wiring (Phase 5).
- **Monorepo aggregate report** — per-project report is default; whether to offer a cross-project aggregate by default or opt-in.
- ~~**`.NET prep` upstreaming** — shared with Kronikol4J; whether the §6.2 changes merge into main Kronikol or a parity branch.~~ **Resolved by the monorepo:** `dotnet/`, `java/`, `js/` and `parity/` are one repo, so the §6.2 prep is a normal commit in the same PR as its consumer and the parity-diff is per-PR rather than periodic. No branch strategy needed.

---

## Appendix C — Facts to verify at implementation (could not be web-verified during planning)
These are **factual confirmations, not design decisions** — the web tools error in this environment, so a handful of version-specific facts are asserted from knowledge and flagged inline. **Verify these first if you have web access**; none change the architecture, they only tune specifics. (The design is robust to the likely answers.)

**Solving the web-tools gap (the workaround).** The `WebFetch`/`WebSearch` tools are **config-broken in this environment** — their internal helper model rejects "thinking" (`adaptive thinking is not supported` / `thinking may not be enabled when tool_choice forces tool use`), so retrying them always fails. But **`node` (v25.9.0), `npm`, `gh`, and `curl` all work and the network is open** — so the fix is to **route around the broken tools**: run the actual Node runtime (the *best* source for runtime facts), `npm view` for versions, and `curl`/`gh` for docs/source. (For a fresh session: if the web tools error, use Bash + node/npm/curl/gh instead.)

**Research-pass status.** Using that workaround, items **#2, 4, 5, 6, 7, 9, 10 are now empirically CONFIRMED** on Node v25 (table above — notably the exact undici/`http.client.*` diagnostics-channel names that §3.11's design rests on). A source-reading pass **resolved the .NET-source items** #13/#16/#17 plus two body details (the DEFLATE alphabet `0–9A–Za–z-_` §6.4; the client-side flame chart §3.16). The four remaining (⏳ #1, 3, 8, 15) are **version-confirmed** (vitest 4 / jest 30 / prisma 7 / fastify 5) with only a default-value/wording check pending.

**Verified empirically on Node v25.9.0 + latest packages** (via `node`/`npm`/`curl`/`gh` — the `WebFetch`/`WebSearch` tools are config-broken in this env, so route around them; see the §"Solving the web-tools gap" note below):
| # | Fact | Result |
|---|---|---|
| 2 | **node:test** global-setup hook | ✅ **`--test-global-setup=...` flag exists** (also `--test-isolation`); subprocess-per-file behaviour stands | §5.7 |
| 4 | undici **diagnostics-channel names** | ✅ **all subscribable:** `undici:request:create/headers/trailers/error`, `undici:client:sendHeaders`, `undici:body:sent/received` (undici 8.5.0) | §3.11 |
| 5 | core **`http.client.*`** channels | ✅ **all subscribable:** `http.client.request.created/start/error`, `http.client.response.finish` | §3.11 |
| 6 | undici **`compose()` / interceptors** | ✅ `Agent.compose` is a function; built-in interceptors `redirect,responseError,retry,dump,dns,cache,decompress,deduplicate` (undici 8) | §3.11 |
| 7 | **`require(ESM)`** | ✅ **works *unflagged* on Node 25** (floor: flagged in 20.19, unflagged 22.12+/23+) — ESM-only escape hatch viable | §3.10, §15 |
| 9 | **AsyncLocalStorage** | ✅ present (stable since Node 16) | §3.2 |
| 10 | **`node:sqlite`** | ✅ **loads** (built-in, on by default, still flagged *experimental* via `--no-experimental-sqlite`) | §3.12 |
| 1 | Vitest **default `pool`** | ⏳ vitest **4.1.9** installed; default is `forks` (changed from `threads` in v2) — *confirm exact v4 default value at impl (docs scrape was thin)* | §5.3, §5.7 |
| 3 | Jest **`workerThreads`** | ⏳ jest **30.4.2**; `workerThreads` is an opt-in config flag — confirm current status | §5.7 |
| 8 | Prisma **`driverAdapters`** GA | ⏳ `@prisma/client` **7.8.0** (driver adapters GA since Prisma 6) — confirm `$extends` surface | §3.12 |
| 15 | Fastify `enterWith`/`fastify-plugin` + `supertest` socket | ⏳ fastify **5.8.5** / fastify-plugin **6.0.0** / supertest **7.2.2** installed; `supertest` opens a real ephemeral socket (→ headers path) — confirm at impl | §7 |

**Confirm by reading `dotnet/src/Kronikol` (.NET source) / `java/`:**
| # | Fact to confirm | Where it matters |
|---|---|---|
| 11 | Whether the **§6.2 .NET prep** (determinism seam, asset externalization, parity-hardening) is **already done in Kronikol4J's branch/upstream** — if so, reuse rather than redo | §0, §6.2 |
| 12 | The **pre-encode PlantUML tap** point in `PlantUmlCreator.cs` (extract decoded text before Deflate) | §6.6 |
| 13 | **RESOLVED from source:** stable LINQ **`OrderBy`** everywhere **except one** unstable `List.Sort` (`InternalFlowRenderer` spans by `StartTimeUtc`) → Node stable `Array.sort` **+ a span-id tiebreaker** (§3.16). No other unstable sorts exist | §6.4, §6.5, §3.16 |
| 14 | Per-adapter parameter-capture formats (Vitest/Jest `test.each` templates → `ExampleValues`; Cucumber Examples → `OutlineId`/`ExampleFlatValues`) + the display-name parser rules `ParameterParser` must re-tune — *(pivot engine itself is deep-dived, §4.5; this is the residual per-adapter capture)* | §4.5, Phase 4/5 |
| 16 | **RESOLVED empirically (§6.7 a/b):** indent = **2 spaces**; `durationSeconds` raw number **matches JS**; data-export JSON uses the **default `JavaScriptEncoder`** (escapes `<>&'+`/non-ASCII → UPPER `\uXXXX`) — needs a custom escaper. *Residual:* confirm the encoder's **exact escape allow-list** (replicate `JavaScriptEncoder.Default`) | §6.7 |
| 17 | **RESOLVED from source (§6.7 b):** XML = PascalCase / omit-null / `F3` / lowercase bools; `MapStepJsonFull` sub-schemas captured; **serialization = `doc.ToString()` → no XML declaration, 2-space indent** (data reports; the schema file prepends a declaration). Nothing residual | §6.7 |

---

## Appendix — Proxy-tap / ingestion / Playwright contracts (added with .NET 3.0.44)

These land in .NET first and define the wire contracts the Node packages must match (full detail in the wiki: *Integration ProxyTap Extension*, *Ingesting External Captures*, *Integration Playwright*, *Capture-Time Redaction*; Java mirror in `JAVA_PORT_PLAN.md` Appendix C):

- **`@kronikol/proxy-tap`** — the out-of-process tee (topology B: fixture stamps identity headers → they ride the real request → the tap on each hop is the sink). Port `ProxyTapOptions` as-is (listen port/host, forward base URL, caller/service names, dependency category, body cap, header policy `All|AllExceptSecrets|Whitelist|None`, secret denylist + drop/replace, re-inject the four `test-tracking-*` headers, identity from `traceparent`, fallback name/id headers, custom resolver, capture-unattributed + fixed fallback id, sink, phase, timeouts, spans, synthesize traceparent). Node has no universal seam problem here — it is a plain `http` server + `undici` client. Must forward bytes untouched and record after responding.
- **`@kronikol/tcp-tap`** — the database counterpart of the proxy tap (added with .NET 3.0.45; full detail in the wiki: *Integration TcpTap Extension*). A byte-for-byte TCP tee with protocol decoders, so a service's Redis or MongoDB hop renders like the in-process extensions with nothing changed inside the service. Port `TcpTapOptions` as-is (listen host/port — `0` = ephemeral, exposed as `boundPort` —, forward host/port, caller/service names, dependency category, `verbosity` `summarised|detailed|raw`, `bodyCapBytes` 65536, `captureReplies`, sink, phase, `fallbackTestName`/`fallbackTestId`, `identityResolver` returning `{name, id} | null`, `emitActivities` on a `Kronikol.TcpTap` source, `keyRedaction`/`valueRedaction`, `channelCapacity` 1024, `maxBufferedBytes` 8 MiB, `readBufferBytes` 32 KiB, `acceptBacklog`, connect/drain timeouts, `decoderFactory`), plus `RedisTapOptions` (`excludedCommands`, `excludedKeyPrefixes`, `defaultDatabase`, `capturePubSub`) and `MongoTapOptions` (`excludedCommands`, `trackGetMore`, `logFilterText`, `logResponseContent`, `maxResponseDocuments`, `documentRedaction`); DI-equivalent factories `addRedisTapTestTracking` / `addMongoTapTestTracking` / `addTcpTapTestTracking`. Node is a natural fit — `net.createServer` + `net.connect` + two `pipe`s. **The contract is the invariants, not the code:** forward bytes downstream *before* queueing the copy, bounded drop-on-full queue with per-direction counters, propagate half-close, decode on a separate task, never let a decoder error stop forwarding, hard-exclude the auth commands whatever the option says, and emit the same NDJSON as .NET — same `method` labels (`Get (Hit)`, `Find ← Trial`, `Insert (×3) → Trial`, `Aggregate ($match, $group) ← x`), same URIs (`redis://db{n}/{key}` with multi-key commands comma-joined, `mongodb:///{db}/{coll}` from the command's `$db`), same reply notes (`n=…, nModified=…` and up to `maxResponseDocuments` pretty-printed `cursor.firstBatch` documents), same status mapping (Redis `OK` / 500 + error text; Mongo 200 / 500 + `errmsg`), and the real command/reply timestamps on the two records. RESP2 **and** RESP3 (both self-describing, one parser); MongoDB OP_MSG with kind-0 and kind-1 sections, OP_QUERY/OP_REPLY passed through and recorded never, OP_COMPRESSED passed through undecoded and reported once.
- **NDJSON + `kronikol ingest`** — `@kronikol/core` gets `InteractionRecord` (the `httpInteraction` shape + `testId`/`testName` + optional extras), `NdjsonInteractionWriter` (an `IRequestResponseSink`), `NdjsonInteractionReader`, `TestRunRecord`, `FeatureSynthesizer`, `IngestPipeline`; `@kronikol/cli ingest <inputs…> [--tests f] [-o dir] [--render …] [-t title] [--collapse|--no-collapse] [--collapse-threshold n] [--max-arrows n] [--no-component-diagram] [--no-redact] [--redact-header h] [--feature name] [--allow-empty]`, exit codes 0/1/2. This is **the** cross-runtime glue: a Node capturer can feed the .NET report and vice-versa.
- **`@kronikol/playwright`** — exactly the `TestTrackingIdentity` contract: `create(testName, testId?)` with `testId` defaulting to the 32-hex W3C trace id; `toHeaders()` = `test-tracking-current-test-name`, `-current-test-id`, `-caller-name` (`Browser`), `-trace-id` (UUID form) + sampled `traceparent`; `context.setExtraHTTPHeaders(...)` merge semantics; a `step()` helper and a per-run tests NDJSON writer (start/step/end) so `ingest --tests` can attribute outcome/duration/steps. The sink is downstream (server or tap), never the fixture.
- **`@kronikol/otlp-tap`** (added with .NET 3.0.45) — the span-side receiver-tee (topology C: the OTLP traces the services already export are teed and mapped, attributed by W3C trace id rather than by header). Port `OtlpTapOptions` as-is (listen port/host incl. `+`/`any`, `ForwardBaseUri`, `ExpectedHeaders` shared secret → `401`, `TracesPath`, `MaxRequestBytes` → `413`, `QueueCapacity` drop-on-full, sink, phase, `ServiceNameMap`, `CaptureKinds` = `db|http|messaging|rpc`, `IncludeServerSpans`, `AttributeByTraceId`, `KnownTestIds`, `FallbackTestId`/`FallbackTestName`, `ContentCapBytes`, `DefaultCallerName`, `Log`, `Name`) and the same counters. Node is a plain `http` server plus `undici`; decode both OTLP encodings (`protobufjs` or a hand-rolled reader — the .NET package deliberately takes no protobuf dependency) and answer an **empty** `ExportTraceServiceResponse` (zero bytes for protobuf, `{}` for JSON). `SpanToInteractionMapper` must produce byte-identical labels/URIs/categories to the .NET one — deprecated *and* stable semconv, the MongoDB directional arrows, `redis://db{n}/{key}`, `500` + status message on an ERROR span — because the merge below matches on them. Ack/forward first, map on a bounded queue.
- **Wire/span merge (`ingest --merge-duplicates`)** (added with .NET 3.0.45) — `InteractionMerger` in `@kronikol/core`: same caller/service/method-verb/last-URI-segment, intervals overlapping ≥ threshold (default 0.8) of the shorter, greedy best-overlap one-to-one matching; span identity wins, wire content/status/label wins, `x-kronikol-captured-by: wire + span` pseudo-header on the merged request. New optional `capturedBy: "wire" | "span"` field on `InteractionRecord`, with the same inference fallback.
- **Core parity items:** capture-time redaction hook (`Redaction` on the logger; secure preset opt-in), `ReportsFolderPath` honoured, resettable diagram cache, `CollapseConsecutiveIdenticalCalls`/`CollapseThreshold`/`MaxArrowsPerDiagram` (+ exact PlantUML `loop`/delay-line text), `ShowNoInteractionsMarker` (+ exact HTML), `DependencyCategories.AI`, `httpInteractions` always in the data file.

### Ingest parity, .NET 3.0.45

Four additions to the contracts above. All are wire-level, so a Node capturer and the .NET reader must agree
byte-for-byte (full detail: wiki *Ingesting External Captures*):

- **`attachment` event** in the tests NDJSON — `{event, testId, name, path, mediaType?, step?, timestamp}`.
  `path` is an absolute path, a path relative to `--attachments-base`, **or a `http`/`https` URL** (rendered as
  a plain link, never copied). `mediaType` decides inline rendering (`image/*` inline with a lightbox, anything
  else a link) and wins over the extension sniff; `step` is the 0-based index of the top-level step, absent →
  scenario-level, an unresolvable index falls back to the scenario. `FileAttachment` gains an optional third
  positional `mediaType`, which also lands in `TestRunReport.json`/`.xml`/`.yml`, the JSON Schema and the XSD.
  `@kronikol/playwright`'s reporter emits one record per `result.attachments` entry (skipping
  `kronikol-test-id`), which is how screenshots, `trace.zip`, `video.webm` and report links reach the report.
- **Widened `start`/`step`/`end`** — `start`: `featureDescription`, `description`, `rule`, `tags[]`,
  `outlineId`, `exampleValues{}`; `step`: `background`, `keywordType`
  (`Context|Action|Outcome|Conjunction|Unknown`), `docString`, `docStringMediaType`, `table` (`string[][]`,
  first row = header), `stackTrace`, `bypassReason`; `end`: `stackTrace`. New model field `Scenario.Description`.
  Tag conventions are the ReqNRoll ones (`@category:x`, `@endpoint:x`, `@happy-path`; a tag on **every**
  scenario of a feature is also the feature's label). An explicit `background: true` suppresses the heuristic
  background detector, which also stays out of the way when the steps carry no keywords.
- **Capitalisation rule** — the first *letter* of a step or assertion label with **no keyword** is
  upper-cased, after skipping whitespace and the marker glyphs `✓ ✗ ⚠ • -`; a label starting with a quote or
  bracket (`" ' ( [ {` and the typographic ones) is left verbatim; keyword steps are never touched;
  culture-invariant, Unicode-aware, idempotent. Default **on** (`CapitaliseStepText`, `ingest --no-capitalise`),
  applied once at model-build time so HTML/JSON/XML/YAML agree, and the same helper capitalises the diagram's
  step bars and ✓/✗ notes. Port it as one function — the report and the diagram must not diverge.
- **Window attribution** — `ingest --attribute-by-window [fallbackId]`: a record with no `testId` (or the
  capturer's fallback marker) goes to the test whose `[start, end]` window contains its timestamp; overlapping
  windows → the **latest-started** one; a tie → the first window in the file; no window → left alone for
  `--fold-unknown`; a **response follows its request** by `requestResponseId` rather than its own timestamp.
  Alongside it: `--phase-from-steps` (Given/Context → `Setup`, When/Then → `Action`, And/But inherit),
  `--strict` (a malformed line throws instead of being skipped and counted), and structured diagnostics —
  `IngestResult.Diagnostics` as `{kind, message, scenarioId?}`.

### `@kronikol/playwright` reporter — the user in the diagram (added with .NET 3.0.44, P7)

The reporter is the first concrete piece of `@kronikol/playwright`; the reference implementation is
`tests/kronikol-reporter.ts` (+ label helpers in `tests/kronikol.ts`) in the sidekick-intelligence-e2e repo.
Contract (all records are the ingest NDJSON shapes above):

- `onTestEnd(test, result)`: find the scenario id in the `kronikol-test-id` attachment (the fixture attaches
  `identity.testId`); walk `result.steps` recursively, skipping `hook`/`fixture` subtrees.
- `pw:api` steps whose title starts with an action verb (`Navigate to`, `Click`, `Double click`, `Right click`,
  `Fill`, `Type`, `Press`, `Check`, `Uncheck`, `Select option`, `Hover`, `Tap`, `Focus`, `Drag`, `Upload files`,
  `Reload`, `Go back`, `Go forward`, `Scroll into view`, `Set checked`) → `{"kind":"ui","type":"Request",
  "method":<label>,"uri":<current page url>,"serviceName":"web","callerName":"User","callerDependencyCategory":"User",
  "content":<raw title>,"timestamp":<step.startTime>,"durationMs":<next action start − this start, or test end>,
  "testId":…,"requestResponseId":<uuid>}` appended to `ui.ndjson`.
- `test.step` → tests NDJSON `{"event":"step","text","keyword?","level":<test.step depth>,"timestamp","durationMs",
  "status":"passed|failed","error?"}`; `Given|When|Then|And|But ` prefix becomes `keyword`.
- `expect` → `{"event":"assertion","text","status":"passed|failed","error?","timestamp"}`.
- Labels: `trimmed` (default) — `Navigate to "url"` → `Open /path`; `Click getByRole('button', { name: 'X' })` →
  `Click "X"`; `Fill "v" getByLabel('L')` → `Fill "L" with "v"`; `Press "Enter"`; `Select option locator('#s')` →
  `Select option in "#s"`; `Expect "toHaveText" getByRole('heading')` → `"heading" to have text` — or `raw`.
- Config knob (suite-level): `uiActions: { enabled, labelStyle: "trimmed"|"raw", assertions, stepDelimiters }`.
