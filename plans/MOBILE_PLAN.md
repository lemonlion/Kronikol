# MOBILE_PLAN.md — Kronikol for mobile developers: the device at the edge, then inside the app

**Date:** 2026-09-22 · **.NET repo:** 3.27.0 tagged, 3.27.1 in the working tree while this was written · **Kronikol4J:** 0.1.25-SNAPSHOT
· **Status: design record, nothing implemented, NOT green-lit.** Answers the owner's question of
2026-09-22: "Kronikol should be a tool useful for all mobile devs, whether they're writing in iOS,
Android, Xamarin etc. Where would that fit into the roadmap, and what would it involve?"

**The decision it needs is D20 in `ROADMAP.md` §3.** Its milestones are placed there as stage 12b
(M1), 14.1 (M2) and after 14.7 (M3, M4). `NEXT_LANGUAGE_PLAN.md` §3 gains three rows from it.

**How far each fact was checked** follows the roadmap's marks: RUN (a command was executed today),
READ (the source, plan or wiki page was read today), PLAN (another plan says so, not re-checked),
ASSUMED (general knowledge of a third-party tool, not verified against its documentation today; every
ASSUMED row that a milestone depends on is a check in §5 before that milestone starts).

---

## 0. The answer in one paragraph

Mobile is in no planning document today, and the gap is smaller than it looks. Kronikol already
solved the "external client" shape once, for browser-driven suites: the test mints an identity, the
client stamps it on every request, a tap at the network edge or the instrumented backend attributes
each call, and `kronikol ingest` folds the tests file and the captures into one report. A phone is
that browser with worse plumbing. So the work is three layers of unequal cost: **M1, the device at
the edge**, which serves every mobile platform at once with no Kronikol code inside the app and is
mostly a recipe, one importer and three converters; **M2, in-app capture for .NET MAUI**, which is
blocked on a packaging split the foundations plan wants anyway; and **M3 and M4, native in-app
capture for Android and iOS**, which are platform ports in the foundations plan's §9 template and
belong after the shared renderer. The recommendation is M1 before the launch and the rest after it.

---

## 1. Where mobile stands today (checked 2026-09-22)

| Fact | Basis |
|---|---|
| `ROADMAP.md` has no stage, decision or mention of mobile. `NEXT_LANGUAGE_PLAN.md` §3 ranks Python, JS/TS, Go, Ruby and PHP and explains why C, C++ and Rust are out; it has no Swift, Android or Dart row. `PLATFORM_FOUNDATIONS_PLAN.md` names four platforms: .NET, Java, Node, Python | READ |
| The word "mobile" in this repository means the report's phone-width layout: `MobileResponsiveTests` and the CHANGELOG entries about viewports from 375 px. That covers reading a report on a phone, not building an app | RUN (`grep -ri mobile`) |
| Xamarin support ended on 2024-05-01 and .NET MAUI is its successor. Xamarin.Forms apps consume netstandard libraries | ASSUMED |
| Every Kronikol package targets `net8.0;net9.0;net10.0` and nothing ships netstandard | READ (`Directory.Build.props:6`) |
| The core package depends on `Microsoft.AspNetCore.Mvc.Testing` on every target, and that package's manifest declares a framework reference to `Microsoft.AspNetCore.App` | READ (`Kronikol.csproj:44-46`; the 9.0.15 nuspec in the NuGet cache) |
| Whether a `net9.0-ios` or `net9.0-android` project can reference the core package is **not measured**: this machine has only the `wasm-tools` workloads | RUN (`dotnet workload list`) |
| Kronikol4J has `KronikolOkHttpInterceptor`, the Java analogue of `TestTrackingMessageHandler`, and test-framework adapters for JUnit 5, TestNG, Spock and Cucumber. It has no JUnit 4 adapter, no NDJSON writer in any Java source, compiles with `--release 17`, uses `java.net.http` only in `TrackingHttpClient` and `HttpExchangeRecorder`, and its core imports `java.nio.file` and `javax.net.ssl` | RUN (`grep`), READ (`build-logic`) |
| `Kronikol.Playwright` mints a `TestTrackingIdentity`: the four `test-tracking-*` headers (test name, test id, caller name, trace id) plus a W3C `traceparent` whose trace id equals the test id by default. `BeginScope()` opens the matching in-process `TestIdentityScope` | READ (`TestTrackingIdentity.cs`) |
| `Kronikol.Extensions.ProxyTap` is an HTTP tee on `HttpListener`: listens on `localhost` by default, forwards to one `ForwardBaseUri`, reads identity from the headers, then `TestIdHeaderFallbacks`, then the inbound `traceparent`; redacts secrets at capture; can lend a database tap an identity through `InFlightIdentityRegistry`; writes to any `IRequestResponseSink` | READ (`ProxyTap.cs`, `ProxyTapOptions.cs`) |
| `Kronikol.Extensions.Otlp` has `OtlpTap`, an OTLP/HTTP receiver on plain sockets (protobuf and JSON, gzip) that binds any interface, authenticates with `ExpectedHeaders`, attributes by trace id and answers a non-test trace with `FallbackTestId`. Traces only, HTTP/1.1 only, no payloads | READ (`OtlpTap.cs`, `OtlpTapOptions.cs`, wiki `Integration-Otlp-Extension`) |
| `SpanToInteractionMapper` reads no `kronikol.*` attribute, so bodies exported by `OtlpExportSink` do not come back through the tap. `RequestResponseLogger` exposes no file sink and F1's `CaptureMode` is not built. **A backend running as its own process cannot hand its inside view to a test host today** | RUN (`grep 'kronikol\.'`), READ (foundations F1) |
| `kronikol ingest` has `--tests`, `--cucumber-messages`, `--attribute-by-window [id]`, `--run-window` with `--run-start` and `--run-end`, `--fold-unknown`, `--merge-duplicates`, `--phase-from-steps`, `--redact-header`, `--diagnostic` and `--attachments-base` | READ (`IngestCommand.cs` usage) |
| Window attribution: the window that started latest wins, or `ExclusiveOnly` leaves a record inside two windows unattributed; a response follows its request; a test killed before its `end` is bounded by the last timestamp seen for it | READ (`IngestAttribution.cs`, wiki) |
| A `kind: "ui"` interaction draws one arrow from a `User` actor with the calls it caused nested under it; `InteractionRecord.UserAction(...)` builds it. The reference reporter is about 150 lines of TypeScript in an external repository | READ (wiki `Ingesting-External-Captures`, `Integration-Playwright`) |
| CTRF is written by every run and read back into history (`history import --from-ctrf`). There is no CTRF-to-report importer. The foundations plan lists CTRF and HAR importers as takes (L4, L5) with nothing built, and rejects JUnit XML because "no per-test start time in most dialects" | READ (foundations §12.1, §12.4; `CtrfCommand.cs`) |
| `templates/` holds 24 project templates, the skills and the agents. No mobile sample exists anywhere in the three repositories | RUN (`ls templates`) |

---

## 2. What a mobile test needs, and what already exists

### 2.1 Two halves

A report needs the **scenario** (which test ran, its steps, its verdict) and the **interactions**
(what the app did). The contract for both already exists and is language-neutral: the tests NDJSON
with `start`, `step`, `assertion`, `attachment` and `end` records, and the interactions NDJSON in the
`httpInteraction` shape with `testId`. Any runner on any platform can write both; `kronikol ingest`
renders them. Nothing in this plan changes that contract (rule 6 of the roadmap: the contract is one
of the three coming freezes, and this plan adds readers to it, not fields).

### 2.2 The browser precedent, hop by hop

| Hop | Browser suite today | Mobile equivalent |
|---|---|---|
| Identity is minted | `TestTrackingIdentity.Create` in the test, or `FromCurrentScope` inside a Kronikol adapter | The same, in the driver process: an Appium test in .NET or Java, or a converter run after Maestro, XCTest or Espresso |
| The client carries it | Playwright `ExtraHTTPHeaders` on the context | The app stamps the headers itself from a launch argument (§3.1, M1.3), or nothing carries it and the time window applies |
| The edge records it | `ProxyTap` in front of the backend, or the backend's `TestTrackingContextMiddleware` | The same tap; or a HAR from Charles, Proxyman or mitmproxy; or `OtlpTap` for an app that already exports spans |
| The user's action is drawn | The Playwright reporter writes `kind: "ui"` records from the step tree | Maestro's per-command output, XCTest activities, Kronikol step tracking in an Appium test |
| The report is built | `kronikol ingest --tests ...` | The same command |

### 2.3 What the device adds: plumbing, not architecture

- **Reachability.** The iOS simulator shares the host's network stack, so `localhost` works. The
  Android emulator reaches the host's loopback through its `10.0.2.2` alias, so a `localhost`-bound tap
  works there too. A **physical device** needs a bind on a LAN interface: `OtlpTap` does that on plain
  sockets with `ListenHost = "+"`; `ProxyTap` is `HttpListener`, and on Windows a non-loopback prefix
  needs a URL ACL or elevation, while on macOS, which is the iOS build host anyway, it needs neither.
  Basis: READ for the taps, ASSUMED for the emulator and simulator networking.
- **TLS.** Both taps listen on `http://`. A debug build has to allow cleartext to the tap (Android's
  network security configuration, an ATS exception on iOS) or trust a certificate a future `kronikol
  tap` (F10) could present. This is the friction every Charles user already accepts. ASSUMED.
- **Identity.** A device cannot inherit `AsyncLocal`. Three carriers, in order of exactness:
  1. **A launch-argument shim.** The driver passes the test id to the app at launch and a few debug-only
     lines in the app stamp the four headers and a `traceparent` on every request. Exact, per platform,
     no package needed (M1.3).
  2. **Trace id as test id**, for an app that already exports OpenTelemetry spans. The SDK mints the
     trace ids, so the test cannot make them equal the test id from outside; the tap would need to read
     a span attribute or a baggage entry instead (M1.4, optional).
  3. **The time window**, exact with one device per run and honestly ambiguous on a parallel device
     farm, where `ExclusiveOnly` leaves the contested records unattributed rather than guessing.
- **Payloads.** OTel spans carry no bodies (`NEXT_LANGUAGE_PLAN` §1.1, PLAN). An app that only exports
  spans gives arrows. The proxy and the HAR give bodies. Both at once is the documented pairing, folded
  by `--merge-duplicates`.
- **The backend's inside view.** If the backend runs inside the test process (a `WebApplicationFactory`
  host, the usual .NET component-test shape), its SQL and messaging appear under the app's calls with no
  further work. If it is deployed as its own process, its inside view waits for 14.1 (§1, the grep).
  Until then the recipe's server-side view is the tap in front of the backend.

### 2.4 Where mobile developers already look

Charles Proxy, Proxyman, mitmproxy and Flipper's network plugin are how app traffic is inspected
today, by hand, one session at a time, and all of them export HAR (ASSUMED). A HAR importer therefore
meets mobile developers in the tool they already use, and turns "I looked at the traffic" into "every
UI test's traffic is in the report, attributed to the test, with the backend's calls under it".

---

## 3. The three layers

### 3.1 M1: the device at the edge (before the launch)

**Serves** iOS, Android, MAUI, Flutter and React Native alike, because nothing runs inside the app.
**Bump:** minor (new `ingest` flags and a new public reader). **Track:** D (`Ingestion/`), plus one
verb in the tool and the wiki.

| # | Item | What it is | Basis and open checks |
|---|---|---|---|
| M1.1 | **HAR importer**, `kronikol ingest --har <file>` (repeatable) and `IngestRequest.HarFiles` | `Ingestion/Har/HarReader.cs` turns each HAR 1.2 entry into a request record and a response record sharing one `requestResponseId`: `startedDateTime` is the request timestamp, `time` the `durationMs`, `request.method` and `url` the verb and URI, `postData.text` and `response.content.text` the bodies (base64 decoded when `encoding` says so), headers copied and redacted by the existing `--redact` pass. **Attribution reads the request headers exactly as `ProxyTap` does**: `test-tracking-current-test-id`, then the `traceparent` trace id, so a shim-stamped app gets exact attribution from a HAR with no tap at all; otherwise `testId` is left empty for `--attribute-by-window`. `capturedBy` is `wire`. `callerName` defaults to `App` and `serviceName` to the host, with `--har-service <host>=<name>` to rename, mirroring `OtlpTapOptions.ServiceNameMap` | Foundations L4 lists this as a take (READ). HAR field names are ASSUMED from the 1.2 specification; check against one export from each of Charles, Proxyman and mitmproxy before the reader is pinned. Playwright's `recordHar` is a free test fixture (ASSUMED that every binding writes one) |
| M1.2 | **Tests-record converters** for the three driver outputs that are not already Kronikol test processes | (a) **JUnit XML**, `--junit <file>`: Maestro (`--format junit`), the Android Gradle plugin's connected-test XML and Firebase Test Lab all write it. Most dialects carry a suite `timestamp` and per-case `time` but no per-case start, so windows are **reconstructed sequentially** from the suite start plus cumulative durations, and the report says so with a `CaptureDegraded`-class diagnostic. A single device runs its tests one after another, which is why the reconstruction is honest here and was not for a parallel server suite. (b) **xcresult**: `xcrun xcresulttool get test-results tests --format json` on the Mac, converted to tests records on any machine. XCTest activities (`XCTContext.runActivity`) carry start and finish times and become `step` records, which is the one mobile source of timestamped steps. (c) **Appium from .NET or Java**: nothing to build. The driver process is a Kronikol test process, so the adapters supply scenarios and `Kronikol.StepTracking` supplies steps | L5 rejected JUnit XML for the missing start time (READ). **Gate G1 before building (a):** measure the three dialects; if one carries per-case timestamps, read them instead of reconstructing. **Gate G2 before (b):** confirm the per-test and per-activity timing fields of the current `xcresulttool` JSON. Both ASSUMED today |
| M1.3 | **The identity shim recipe** (documentation, no package) | How each driver hands the id to the app, and the lines that stamp the headers: XCUITest `launchEnvironment`; AndroidJUnitRunner `InstrumentationRegistry.getArguments()`; Appium `processArguments` on iOS and `optionalIntentArguments` on Android; Maestro `launchApp` arguments; Detox `launchApp({ launchArgs })`; Flutter `--dart-define`. Stamping: `HttpClient.DefaultRequestHeaders` or a `DelegatingHandler` in MAUI; an OkHttp interceptor on Android, which can be Kronikol4J's own with a static resolver; `URLSessionConfiguration.httpAdditionalHeaders` on iOS; a `dio` or `HttpClient` interceptor in Flutter. The header names are the existing four plus `traceparent`; no new names | Every driver flag here is ASSUMED. **Check each against the tool's current documentation while writing the page, and record the version checked.** Whether Kronikol4J's `TestInfoResolver` can be given a static identity is a READ of one class |
| M1.4 | **Tap adjustments** (optional, small) | `OtlpTapOptions.TestIdAttribute` (a span attribute such as `test.id`) and a baggage entry, read before the trace id, so an OTel-instrumented app that received the id at launch attributes exactly. This is the foundations' L2 "the receiving side reads either", applied to the span tap | READ (L2). A patch if it lands alone, a minor if it lands with M1.1 |
| M1.5 | **Wiki page and sample** | `Mobile-End-to-End.md`: the four topologies (tap, HAR, OTLP, in-process backend), the shim per platform, emulator against physical device, TLS, single device against device farm, and one worked run with screenshots. The sample is Q1 in §5 | |
| M1.6 | **A search page at 13.3** | "What network calls did my app make during this UI test", ending in the sample's real report | Rule 9: it is written now and published at the launch |

**Acceptance for M1.** One real run, on the owner's machine, of the sample against the BreakfastProvider
API: a UI flow on the Android emulator through a `ProxyTap`, and the same flow through a HAR exported
from a desktop proxy. The report shows the user's actions as `User` arrows where the driver gave
timestamps, the app's calls with bodies under them, the API's own SQL and messaging under those, each
in the right scenario; `Failures.md` names the failing flow's calls; `kronikol query` answers against it.
In CI the importers are held by fixture files from that run (a HAR, a Maestro JUnit XML, an xcresult
JSON), never by an emulator (Q2).

### 3.2 M2: in-app capture for .NET MAUI (with 14.1)

**Serves** .NET mobile developers with the in-process view: `HttpClient`, SQLite and EF Core seen from
inside the app, the identity carried by `AsyncLocal` exactly as on the server. **Bump:** minor.

| # | Item | Notes |
|---|---|---|
| M2.1 | **A capture-only core package.** Everything a capturer needs (`TestIdentityScope`, `RequestResponseLogger`, the sinks, `NdjsonInteractionWriter`, `TestTrackingMessageHandler`, the Sqlite and EF Core extensions' dependencies) with no ASP.NET Core in its graph; the `Kronikol` package keeps its public surface and depends on it | The foundations plan's capturer/renderer split and its L9 in-process path are the same cut. **The first step is the measurement §1 lacks:** a `net9.0-android` class library referencing `Kronikol`, on a machine with the workload, to see what fails and how. Do it before designing the split |
| M2.2 | **A device test-runner adapter.** Scenario boundaries on a device come from whichever runner is maintained for MAUI today | ASSUMED that a maintained xUnit device runner exists; check which, and whether `Microsoft.Testing.Platform` runs on device, before choosing |
| M2.3 | **Records off the device.** `tests.ndjson` and `interactions.ndjson` written to the app's data directory and pulled (`adb pull`, `simctl get_app_container`), or posted to a small receiver on the host. F10's `kronikol tap` is the natural home for that receiver | Per-process NDJSON files are the foundations' §4.2 rule (READ). The receiver is INFERRED |
| M2.4 | **Trimming and AOT.** iOS builds are fully AOT and trimmed. `QUERY_PORTABILITY_PLAN` measured 17 AOT warning sites in the tool, all `JsonSerializer` without source generation; the capture core needs the same `JsonSerializerContext` treatment, which 14.3 does for the tool | PLAN |
| M2.5 | **sqlite-net-pcl.** MAUI apps more often use it than `Microsoft.Data.Sqlite`; a seam on its statement tracing is a new extension | ASSUMED that it exposes a statement trace hook; check |

**Xamarin.Forms is not served by M2.** Retargeting the capture core to netstandard for a platform whose
support ended in 2024 is rejected; a Xamarin app is served by M1 only, and the wiki page says so.

### 3.3 M3 and M4: native in-app capture (after 14.7)

Each is a platform port in the foundations plan's §9 template and is **not designed here**; this section
records what is already known so the template starts filled in.

**M3, Android through Kronikol4J** (Kotlin and Java on the same adapters, as the JVM port already
covers them):

- Item 1, ambient context: identity from `InstrumentationRegistry.getArguments()` into the port's
  resolver; a `ThreadLocal` with coroutine propagation, as the port does on the JVM.
- Item 3, HTTP: `KronikolOkHttpInterceptor` exists (READ). Ktor's client is a plugin away.
- Item 4, SQL: Android has no JDBC, so `kronikol4j-jdbc` does not apply. Room's query callback
  (`RoomDatabase.Builder.setQueryCallback`) reports the SQL and its bind arguments (ASSUMED; check),
  which is a better seam than a driver wrapper.
- Item 6, test framework: a JUnit 4 `RunListener` or `TestRule` for `AndroidJUnitRunner`, about 100
  lines, which the port does not have.
- Runtime: never load the `java.net.http` classes on ART (§1); the two agents do not apply; `--release
  17` is fine on current AGP with desugaring (ASSUMED).
- Item 2, the writers: the port has no NDJSON writer (RUN). F1 and F9 give it one, which is why M3
  follows them.

**M4, iOS through a new Swift package:**

- Item 1: a `@TaskLocal` identity with a thread-based fallback for callbacks.
- Item 3: a `URLProtocol` registered on `URLSessionConfiguration.protocolClasses` captures bodies for
  `URLSession`; Alamofire's `EventMonitor` for its users. Because React Native's iOS networking rides
  `NSURLSession`, and its Android networking rides OkHttp, M3 and M4 cover React Native apps too.
- Item 4: `sqlite3_trace_v2` on a raw SQLite connection, GRDB's trace API; Core Data and SwiftData
  expose no statement seam short of their debug logging, so they are a named gap.
- Item 6: `XCTestObservation` (`testCaseWillStart`, `testCaseDidFinish`) plus `XCTContext.runActivity`
  for steps; Swift Testing through a trait, which is the newer of the two and to be checked.
- Records leave the simulator's container or a device through a host receiver, as M2.3.

**Flutter** is a fifth thing: its Dart networking does not ride the native stacks, so it needs a Dart
package (`HttpOverrides`, `dio` interceptors) and a `flutter_test` adapter. It gets a row in the
ranking and nothing else here.

---

## 4. Where it sits in the roadmap, and why

| Milestone | Stage | Rule | Why there |
|---|---|---|---|
| M1 | **12b**, a new stage between 12 and 13, numbered so no existing citation moves | 9, then 8 | Rule 9 puts anything the bar does not need after the launch, and D18 recommends a .NET launch. A MAUI developer is a .NET developer arriving through the front door, and section 0's own worry is the visitor who finds their case missing and never returns. M1 makes "mobile end-to-end" true on every platform for the cost of a page, an importer, three converters and a sample. Rule 8: it is small, and its parts are measured or measurable in days. It depends on nothing and can run beside every track (§5 of the roadmap: track D plus one tool verb) |
| M2 | **14.1** | 6, 8 | The capture-only split is the foundations' own first foundation seen from the other side. Doing it twice would be the mistake rule 6 exists to prevent |
| M3, M4 | **after 14.7**, as §9 template instances | 6 | Neither port should write a rendering half, which is the shared renderer's whole point (`NEXT_LANGUAGE_PLAN` §4, PLAN). Android first: the OkHttp interceptor exists and the JVM port covers Kotlin |
| Flutter | `NEXT_LANGUAGE_PLAN` §3, a row | 8 | No plan asks for it yet |

**What this does to D18.** Nothing. The launch stays a .NET launch; M1 is not a second language, it is
a recipe over shipped parts plus readers for files other tools already write.

---

## 5. Decisions needed before green-lighting

| # | Decision | Recommendation |
|---|---|---|
| **D20** (roadmap §3) | Is mobile in the bar, and at which layer? | **M1 in, M2 to M4 out.** M1 is the only layer a first look can test, and the only one that costs days |
| Q1 | The sample. (a) A MAUI app in the BreakfastProvider repository, driven by Appium from an xUnit project; (b) a Kotlin app driven by Maestro; (c) no app, fixtures only | **(a).** All .NET, so Kronikol's own adapters supply scenarios and steps and no converter is on the critical path; Windows builds the Android head; the iOS walkthrough is one manual run on a Mac. Maestro is the second driver over the same app, which exercises M1.2(a) |
| Q2 | CI. An emulator lane, or fixtures only | **Fixtures in this repository.** The live run is manual and recorded in the wiki. An emulator lane in BreakfastProvider's CI only after it has proved stable there (the Android emulator runner needs KVM, which public GitHub runners have; ASSUMED) |
| Q3 | Reverse L5's JUnit XML rejection for the sequential dialects | **Yes, behind a diagnostic** that names the reconstruction. Gate G1 first |
| Q4 | HAR naming defaults: `App` as caller, the host as service | Take them; `--har-service` renames |
| Q5 | The app's stamped headers | **The existing four plus `traceparent`.** No new header names; every reader already exists |
| Q6 | `OtlpTapOptions.TestIdAttribute` and baggage (M1.4) | Yes, with M1.1; it is L2's receiving side and about 30 lines |
| Q7 | Where M1's importer code lives if F10's `kronikol tap` later becomes the shared artifact | In `Ingestion/`, where Cucumber's reader lives; readers are renderer-side under the foundations' architecture and move with it |

Gates that are checks rather than decisions: **G1** the three JUnit dialects' timing fields; **G2**
the `xcresulttool` JSON's timing fields; **G3** the MAUI framework-reference measurement (M2.1);
**G4** every driver flag in M1.3 against current documentation.

---

## 6. Assumption ledger

| # | Claim | Status |
|---|---|---|
| A1 | The identity, tap, window-attribution and `kind: "ui"` mechanisms exist as described in §1 | **read** today, file by file |
| A2 | A backend deployed as its own process cannot hand its inside view to the test host today | **measured** (RUN): the span mapper reads no `kronikol.*` attribute; no file sink on the logger; F1 not built |
| A3 | The core package cannot be referenced from a MAUI target because of the ASP.NET Core framework reference | **read from the manifest, not measured.** G3 measures it. If it turns out to work, M2.1 shrinks to trimming and the runner |
| A4 | The iOS simulator shares the host's loopback; the Android emulator reaches it through `10.0.2.2` | **assumed** from general knowledge; the sample run verifies both |
| A5 | Charles, Proxyman and mitmproxy export HAR 1.2 with `startedDateTime`, `time`, bodies and headers per entry | **assumed**; one export from each is a fixture before M1.1 is pinned |
| A6 | Maestro, the Android Gradle plugin and Firebase Test Lab write JUnit XML; most dialects carry a suite timestamp and per-case durations only | **assumed**; G1 |
| A7 | `xcresulttool` exports per-test results and per-activity start and finish times as JSON | **assumed**; G2 |
| A8 | Every driver in M1.3 can pass a launch argument or environment value to the app | **assumed**; G4 |
| A9 | A single device runs its tests sequentially, so reconstructed windows do not overlap | **reasoned**: one app instance per device, one UI. A device farm breaks it, which is why `ExclusiveOnly` is the farm setting |
| A10 | OTel Swift and OTel Android export OTLP over HTTP | **assumed**; the tap is HTTP/1.1 only, so a gRPC-only exporter needs a collector in between |
| A11 | Room's query callback reports SQL and bind arguments; sqlite-net-pcl exposes a statement trace | **assumed**; checks before M2.5 and M3 |
| A12 | React Native's networking rides OkHttp and `NSURLSession` | **assumed**; decides whether M3 and M4 cover it for free |
| A13 | M1 costs days to two weeks; M2 weeks; M3 and M4 months each | **estimates.** M1's parts are the only ones with comparable shipped work to measure against (the Cucumber reader, the Playwright reporter). Plan capacity from the order, not the numbers |
| A14 | A MAUI developer arrives through the .NET front door and counts against the launch bar | **reasoned**, and it is the whole case for 12b; if the owner rejects it, M1 moves to stage 14 unchanged |

**A3 and A6 are the ones to attack first**: A3 sizes M2, and A6 decides whether M1.2(a) reconstructs
windows or reads them.

---

## 7. What this plan does not do

- It designs no platform port. M3 and M4 are §9 template instances and get their own plans when they
  are next.
- It touches no renderer, no diagram, no report output. A report built from mobile captures is byte
  for byte the report the same records would give from anywhere else.
- It adds no field to the capture contract (rule 6). Readers only.
- It does not integrate device farms (BrowserStack, Sauce Labs, Firebase Test Lab) beyond the JUnit
  XML they already produce.
- It does not build a CONNECT-style proxy; a tap that fronts arbitrary hosts is F10's `kronikol tap`.
- It does not serve Xamarin.Forms inside the app (§3.2).
- It proposes no outreach to mobile communities. Rule 9: that is stage 13, at the bar.

---

## 8. Documentation this plan owes when it ships

- Wiki: a new `Mobile-End-to-End.md` (M1.5), linked from `Home.md`, `_Sidebar.md` and
  `How-To-Guides.md`; `Ingesting-External-Captures.md` gains `--har`, `--junit` and the xcresult
  converter; `Integration-ProxyTap-Extension.md` "Listening interface" gains the device paragraph;
  `Integration-Otlp-Extension.md` gains the mobile SDK paragraph and `TestIdAttribute`;
  `Integration-Playwright.md` "Other languages" points at the mobile page as the same contract.
- README: one line in the integration list, no more (rule 9 governs the rest).
- `CHANGELOG.md`: a minor, stating which part moved and why.
- The tool's flag registries: check whether the flag drift guard covers `ingest` flags before adding
  `--har` and `--junit`, and update both `commands.md` copies if it does.
- `PLANS_STATUS.md`: this plan's row, and the `ROADMAP.md` rows named in §4.
- `NEXT_LANGUAGE_PLAN.md` §3: the three rows (done with this plan).
