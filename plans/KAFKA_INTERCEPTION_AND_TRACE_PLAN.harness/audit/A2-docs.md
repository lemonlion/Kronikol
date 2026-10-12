# A2: docs, wiki, Kronikol4J, roadmap and CI audit of KAFKA_INTERCEPTION_AND_TRACE_PLAN.md

Audited 2026-10-12, read-only. Code: worktree `C:/Code/Kronikol-audit135` at `d59843fa` (4.14.6). Wiki:
`origin/master` `8a57576` (4.14.6), exported with `git archive`; the plan's research base was `db4e473` (4.9.0).
Kronikol4J: `origin/main` `7e9c7d5`. BreakfastProvider: `origin/main` `0ee43e8` (Kronikol 4.9.1). Issues #135, #160,
#167, #142, #97 and #140 read with `gh issue view`. Labels: **VERIFIED** (read at those commits), **INFERRED**
(reasoned, not run).

Wiki pages the plan cites that changed between `db4e473` and `8a57576`: `Integration-Kafka-Extension.md` (+1 line at
212), `Querying-Reports.md` (+5 at 529), `Integration-Grpc-Extension.md` (+16 net), `API-Reference.md` (other sections
only), `Generated-Reports.md` (after line 452 only). Unchanged: Integration-OpenTelemetry-Extension, Internal-Flow-Tracking,
Exporting-to-OpenTelemetry, Event-Driven-Architecture-Testing, Parallel-Safe-Background-Correlation.

## 1. Wiki citations in the plan, at the wiki's head

| Plan cites | Status at `8a57576` | Label |
|---|---|---|
| F3 `Integration-Kafka-Extension.md:451` ("any call to `.Build()`") | Moved: **`:452`** | VERIFIED |
| F3 `:504-515` (Disable/Reset block) | Moved: **`:505-516`** (`:512` "removes all Harmony patches", `:516` "Always call `Reset()`") | VERIFIED |
| F3 `:527` (pairs independent) | Moved: **`:528`** | VERIFIED |
| F11 `:41-47` (Raw `Commit`) | Same lines, but the range is short: every row without a topic gets the trailing space (`KafkaOperationClassifier.cs:13`, and every `new KafkaOperationInfo(op)` call: `TrackingKafkaConsumer.cs:148,185`, `TrackingKafkaProducer.cs:141,191`), so Unsubscribe, Commit, Flush and all five transaction rows: **`:41-48`** | VERIFIED |
| F15 `:179` (manual DI example passes `new KafkaTrackingOptions()`) | Unchanged, still wrong | VERIFIED |
| F15 `API-Reference.md:245-258` | Same lines. Now **seven** options missing from `:250`, not six: `HttpContextAccessor` (4.11.0, #136) was never added to API-Reference. `:253` also omits `AddKafkaConsumerFactoryTestTracking`/`AddKafkaProducerFactoryTestTracking` and still says both registrations "auto-resolve `IHttpContextAccessor` from DI" (since 4.11.0 the option's accessor wins) | VERIFIED |
| F16 `Querying-Reports.md:837-857` | Moved: **`:842-862`** (claim at `:844`) | VERIFIED |
| F16 `Integration-OpenTelemetry-Extension.md:7-8` | Unchanged (the sentence ends on `:9`) | VERIFIED |
| C6 `Querying-Reports.md:612-648` (`values`) | Moved: **`:617-648`** (`--request` row `:636`) | VERIFIED |
| §6.4 `Integration-Kafka-Extension.md:447-529` | Moved: **`:448-530`** (Option G heading `:448`) | VERIFIED |
| §6.4 `:246-254` (Consumer Content) | Moved: **`:247-255`** | VERIFIED |
| §6.4 `Generated-Reports.md:295` | Unchanged. Note it makes no "every interaction" claim; nothing on it is false | VERIFIED |
| §6.4 `Integration-Grpc-Extension.md:441-452` (pattern to copy) | Moved: **`:457-468`**, and rewritten by 4.12.1: flags `01` when recorded, a caller's `traceparent` kept, the caller's `Metadata` no longer mutated (a reused one used to gain one `traceparent` per call) | VERIFIED |
| §6.4 `Internal-Flow-Tracking.md:15`, `:115-131` | Unchanged; the source list runs to **`:132`** (`Grpc.Net.Client`) | VERIFIED |
| §6.4 `Exporting-to-OpenTelemetry.md:64-67` | Unchanged | VERIFIED |
| §6.4 `Event-Driven-Architecture-Testing.md:98-108`, `Parallel-Safe-Background-Correlation.md:168` | Unchanged (`AddKafkaTracking` at EDAT `:101`, PSBC `:168`). But **#142 item 3 lists `AddKafkaTracking` only at EDAT L101**, not PSBC:168 (it lists PSBC only for `AddCosmosTracking`). "#142 item 3 covers these" is half wrong: this plan must own PSBC:168 or tell #142's executor | VERIFIED |
| §6.4 note "local wiki checkout is behind origin" | Still true and worse: `e164dbc` (4.5.0), **26** commits behind. Other wiki worktrees exist (`Kronikol.wiki-163` on `wiki-163`, `-134`, `-audit133`); #163's edits are on Assertion-Tracking, no overlap | VERIFIED |

## 2. Wiki and repo docs R0/R1 make false or should update, missing from §6.4

| Page:line (wiki head unless noted) | What changes | Rel. | Label |
|---|---|---|---|
| `Background-Thread-Correlation.md:57-62` | "Message identities end with the message ... The Kafka, Service Bus and Event Hubs wrappers call it before they establish the next message's identity". F10's fix clears at every `Consume`, whatever the poll returns; today's text describes the defect's behaviour | R0 | VERIFIED text; INFERRED edit |
| `Integration-Kafka-Extension.md:195` | `CurrentStepTypeFetcher` "Optional — returns the current BDD step type". The source says "Has no effect ... removal is an open question for v5" since 4.12.1 (`KafkaTrackingOptions.cs:20-25`, `V5_PLAN.md:240-248`); 4.12.1's wiki commit fixed only the gRPC page. Same table R1 adds rows to | R0 | VERIFIED |
| `Integration-Kafka-Extension.md:557`, `:593-602` | "injects two headers into every outgoing message" and the Header Format table. R1's `PropagateTraceContext` (default on) adds `traceparent`; F9 makes it one pair, replaced | R1 (and R0 for F9) | VERIFIED text |
| `Integration-Kafka-Extension.md:57` | `Produce` row "(with delivery handler)". After F7 a plain `Produce` with delivery reports off is logged at the call, without partition or offset | R0 | INFERRED |
| `Integration-Kafka-Extension.md:446` | Choosing table: Option G "Zero-change tracking". R0's remedy needs `<TieredCompilationQuickJit>false</TieredCompilationQuickJit>` in the test project | R0 | INFERRED |
| `Integration-Kafka-Extension.md:3` | The "shared library or abstraction layer?" callout is #160's case; R1 (`DependentProducerBuilder`) changes the answer, and #160's limit (one options set per pair, process-wide) belongs here | R1 | INFERRED |
| `Integration-Kafka-Extension.md:27-29` | Verbosity table "Message content: Key + Value"; R1's `Json` envelope | R1 | INFERRED |
| (no page) | `KafkaTracker.LogProduce`/`LogConsume`/`LogSubscribe`/... (`KafkaTracker.cs:31-91`, public) are documented nowhere; `API-Reference.md:249` says only "Central logging helper". The reporter's path, Q8's overloads and R2's `BeginConsume` need a home | R1 | VERIFIED gap |
| `Internal-Flow-Tracking.md:31`, `:336` | Listener "auto-started by the `TestTrackingMessageHandler` constructor". F14's fix makes the Kafka trackers start it too (gRPC already does) | R1 | VERIFIED text |
| `Generated-Reports.md:295`, `Ingesting-External-Captures.md:34` ("the span the call was made under"), `Internal-Flow-Tracking.md:17` ("a call takes its own span and its descendants") | R1 records on a consume the **producer's** span from `traceparent` (T28), not a span of the consume. The field's meaning for a consume needs a sentence | R1 | INFERRED |
| `Querying-Reports.md:857-858` (and skill `SKILL.md:116`) | "a trace id that leaks across scenarios is the classic flaky-test smell". R1's fallback for a consume without `traceparent` is `Activity.Current`, which in a long-lived consumer loop can be one activity for every scenario, so `trace` would flag Kronikol's own fallback as leakage | R1 | INFERRED |
| `Generated-Reports.md:298`, `Exporting-to-OpenTelemetry.md:32` | Both say the bang status is "the exception's type" (`!HttpRequestException`), as `FailedSend.Status` writes (`src/Kronikol/Tracking/FailedSend.cs:16-20`). R0's `!Local_MsgTimedOut` is an error code. Follow `FailedSend` for a thrown `ProduceAsync`, or document the Kafka form | R0 | VERIFIED convention |
| `Generated-Reports.md:299` (`durationMs`) | Says nothing of "two records stamped with one instant measured nothing" (`ReportGenerator.cs:4645`; the JSON schema text does say it). R0 puts Kafka's one-instant entries under that rule | R0 | VERIFIED gap |
| `Filtering-and-Redacting-Diagram-Content.md:20` | With `MaxContentLength` set, an envelope cut at the limit stops parsing (no YAML view, no `values`). One line in R1's "Message content as JSON" | R1 | INFERRED |
| `README.md:279` | Build Interception row names only `ConsumerBuilder`/`ProducerBuilder.Build()`; Appendix A edits only the csproj description | R1 | VERIFIED |
| `src/Kronikol/Reports/agent-instructions.md:107` (written beside every report as CLAUDE.md/AGENTS.md) | `values . --path '$.status'` reads response bodies; a Kafka payload is on the request half, so an agent gets "carried no body". Q11 says "document `--request`" but §6.4 lists only the wiki | R1 | VERIFIED |
| `.claude/skills/kronikol-test-debugging/SKILL.md:108`, `references/commands.md:190-210`, and the identical `templates/skills/...` copies | Same recipe gap. `SkillDriftTests` pins only verbs and flags, so it would not catch it | R1 | VERIFIED |
| Optional: `Multi-Host-Test-Architectures.md:12-14`, `Event-Driven-Architecture-Testing.md:14-37` | Trace across Kafka between hosts (R1); #160's host limit | R1 | INFERRED |

Not affected: `_Sidebar.md:66` (no new page), `FAQ.md:9`, `Home.md`, `nuget-readme.md` (no Kafka text), no `docs/` folder at the repo root.
`Phase-Aware-Tracking.md:10`, `:217-223` are false for Kafka content today (F13), and R0 makes them true with no edit.
The R0 edit "name the trackers that record W3C ids" should also cover deferred logging (`PendingLogEntry.ActivityTraceId`)
and `TrackingTraceContext`. The writers at d59843fa are the HTTP handler, `MessageTracker`, gRPC, OTLP `MappedSpan`,
ProxyTap, TcpTap and `PendingRequestResponseLogs` (VERIFIED by grep).

## 3. Kronikol4J (§6.5)

| Plan claims | Finding | Label |
|---|---|---|
| "No Java test pins this output" | **Wrong for F11.** `KafkaOperationClassifier.java:28` builds the Raw label as `displayName() + " " + nullToEmpty(topic)`, and `KafkaOperationClassifierTest.java:24-26` pins `"Flush "` with the comment "trailing space after the operation), matching .NET". R0's trim makes the port diverge, so it is "worth porting", not "nothing to mirror" | VERIFIED |
| `GoldenHtmlParityTest.java:63`, `ReportDataParityTest.java:36` pin 3.0.43 / 3.0.47 | True | VERIFIED |
| R0 "nothing to mirror" | Too broad. **F5/F6**: the port logs `Sent` before it calls `send` (`TrackingKafkaProducer.java:33-51`, `MessageTracker.java:134-155`), so a failed send is drawn as `Sent`: the same gap, worth porting. **F9**: `KafkaTestHeaders.stamp` already removes before adding (`:22-28`), pinned by `TrackingKafkaProducerTest.java:43-46`, so it converges. **F8**: port messaging entries carry no timestamp (`baseBuilder` sets none; `RequestResponseLogger.log` stamps nothing). **F10**: does not arise (the scope covers only the log call, `TrackingKafkaConsumer.java:86-95`). **F4**: does not arise, confirmed. Harmony boundary `REMAINING_PARITY.md:1769-1790` confirmed (it still calls the .NET mechanism a postfix) | VERIFIED |
| "records no ids on any messaging entry" | True: `baseBuilder` sets no ids, and `OtelBridge.stamp` has no caller in main code | VERIFIED |
| "already logs the value alone as JSON" | **Misleading.** `TrackingSafeSerializer.writeValue` writes a `CharSequence` as a quoted JSON string and a `byte[]` as an array of numbers. A JSON `String` payload (the `<string,string>` case ask 3 is about) is no more queryable in the port than in .NET today; only POJOs, records and maps come out as objects | VERIFIED |
| "Kafka — DECISION NEEDED" `:46-61` "stays" | Lines confirmed. Its recommended direction is to change Java to .NET's `Key: <k>, Value: <v>`. **#167** proposes the reverse for .NET (the value alone as the body), so if #167 is taken the note's direction flips and it cannot simply "stay" | VERIFIED lines; INFERRED effect |
| "one ledger line per release" | The ledger now ends at `:2428` (4.14.6), and `## Explicitly OUT OF SCOPE` follows at `:2432`, so a new line goes before that, not at the end of the file. There is no ledger line for 4.10.0/4.11.0 (#136, including its Kafka producer fix). R0's own ledger line is in no slice (S7 is R1's) | VERIFIED |

## 4. Roadmap and coordination

- **1.21 and D37 are still free** at d59843fa: row 1.20 is `:252` and 1.22 is `:253`; D36 is `:180` and D38 is `:181`. "D38 sits directly after D36" is still true. Several committed plans record 1.21/D37 as #135's (`SHOULDLY_ASSERTIONS_PLAN.md:732`, `XUNIT2_FRAMEWORK_COMPOSITION_PLAN.md:430,487`, `GRPC_IDENTITY_PROPAGATION_PLAN.md:766`, `MONGODB_ACCESSOR_OPTION_PLAN.md:334`). VERIFIED.
- **Placed since, and missing from §7:** **1.25** (#162, `LONG_COMPONENT_EDGE_PLAN`, done 4.14.5/4.14.6, no decision number, `:256`). **1.26** (#163, `ASSERTION_VALUE_SUBSTITUTION_PLAN`, green-lit and executing 2026-10-12) is committed locally in `C:/Code/Kronikol-163` (`046024b8`) and not yet on origin. There is no D39. #163 will take the next patch and minor numbers. VERIFIED.
- **Coverage check** (`ROADMAP.md:640`): #135, #160, #167, #142 and #140 are not listed. Add `#135 → 1.21, D37`, and #160/#167 if folded in. VERIFIED.
- **PLANS_STATUS.md**: the plan's row exists only in `C:/Code/Kronikol-kafka135` (at 4.11.0, uncommitted). At d59843fa it has to be re-added on top, above `LONG_COMPONENT_EDGE_PLAN.md`. VERIFIED.
- **§8 is stale:** #134 is shipped (4.12.1, 4.13.0, 4.13.2, 4.14.3), not "planned by other sessions"; #142 has no plan, and only item 6 is done. VERIFIED.
- **#167** (bug, 2026-10-11, same reporter) proposes recording the value as the body (indented, YAML view, `$.Status` reachable), the key and message headers as the entry's headers, and `LogProduce`/`LogConsume` overloads that take key and headers. That conflicts with §4.4 (an opt-in `{"key","value"}` envelope, default `Text`), Q8 (overloads taking only `Headers`), Q9 (no headers) and Q11. As planned, #167 is not fixed by default. VERIFIED (issue text).
- **#160** (2026-10-08, same reporter, at `709b3afc`, unmentioned although the plan's last log entry is the same day): `DependentProducerBuilder` interception "alone wouldn't cover this". It asks for public `KafkaTracker.TrackProduce`/`TrackConsume` helpers and per-host interception options. This overlaps §4.1 ("still `KafkaTrackingInterceptor`'s per-pair dictionary"), Q7 and Q8. Without reconciling the two, the package ends up with two or three overlapping hand-written-log APIs. VERIFIED.
- **Background identity on Kafka headers:** `GRPC_IDENTITY_PROPAGATION_PLAN.md:778-780` and `ROADMAP.md:953` (Appendix C, Q3 open) record that the messaging producers stamp an identity that names no scenario. `TrackingKafkaProducer.InjectTestIdentityHeaders` still has no `IsAttributed` check, which gRPC (`:568`), Cosmos and MongoDB have. R0's F9 rewrites this exact method, and the plan does not mention it. VERIFIED.
- **4.12.1 gRPC lesson:** do not mutate the caller's carrier. The plan injects `traceparent` into the caller's `Message.Headers` and never touches one that is present, so a reused `Message` (F9's case) keeps test A's trace on test B's produce. INFERRED.
- **#97 / D21** (`ROADMAP.md:165`, open; the recommendation is fixed floors): A1 and Q3 rely on the `Confluent.Kafka 2.*` float. That float is in the package csproj (`src/Kronikol.Extensions.Kafka/...csproj:10`), not "the test project" as the plan says. A2 relies on `Lib.Harmony 2.*`. Taking D21 removes that newest-version signal unless a newest lane is kept. VERIFIED / INFERRED.
- **#140:** if R0 gives a successful delivery a status, it must be one in `NonErrorStatuses` (`InteractionStatus.cs:84-88`), or every produce counts as an error. INFERRED.
- **V5_PLAN.md:240-248** removes `KafkaTrackingOptions.CurrentStepTypeFetcher` in v5. No conflict. If Q5 is answered "off now, flip in v5", V5_PLAN needs an item for it. VERIFIED / INFERRED.
- **Hand-off file:** no session has claimed #160, #167 or Kafka work. kronikol-cc: "#135 plan DONE and waiting on the owner". kronikol-94 is auditing #134, whose Appendix C Q3 touches Kafka's header injection. VERIFIED.

## 5. CI and test infrastructure

- (a) Remainder lane: matrix entry `ci.yml:79-80`, step **`:287-302`** (the plan cites `:277-290`, now stale), SKIP list `:294` with no Kafka entry. A new `tests/Kronikol.Tests.Kafka.Broker` with a csproj runs there on ubuntu-latest, sequentially with the others. Docker is proven by ClickHouse's log (`CLICKHOUSE_DRIVER_CLIENT_PLAN.md:400-402`, nine real-server facts, none skipped) and by MongoDB's host lane in the same lane. **Precedent puts real-server facts in the existing extension test project** (`ClickHouseServerFixture.cs:12-58`, `MongoServerFixture.cs:12-58` added by #136 in 4.10.0, both pinning `Image`); the plan should pin an image too (Kronikol4J uses `confluentinc/cp-kafka:7.6.1` KRaft). VERIFIED.
- (b) `Kronikol.sln` lists both Kafka test projects (`:136`, `:164`). `release.slnf` holds 61 src projects and no tests. No test guards sln contents (tests use `Kronikol.sln` only as a root marker). `release.yml:37` runs only `tests/Kronikol.Tests`. VERIFIED.
- (c) Testcontainers is pinned at 4.8.1 in five references (ClickHouse, MongoDB, MongoDB.V2, TcpTap ×2). There is no `Directory.Packages.props`. `Testcontainers.Kafka` 4.8.1 exists on nuget.org (latest 4.16.0). VERIFIED.
- (d) Both Kafka test projects target net10.0 only (`Directory.Build.props:5`); the packages target `net8.0;net9.0;net10.0` (`:6`). The reporter runs net8.0 and F2 depends on the runtime. Precedent for multi-targeting: `Kronikol.Tests.AssertionTracking.csproj:5`, and CI installs 8, 9 and 10. Q1's props has precedent in `Kronikol.AssertionTracking.csproj:29-30` (build/ packed as buildTransitive). VERIFIED.
- (e) `RequestResponseLogger.IsolateForTests()` (internal, `:97`, since 4.12.2) is used only in `Kronikol.Tests`. The `Kronikol.csproj` IVT list (`:13-26`) has no `Kronikol.Tests.Kafka*`, and `Kronikol.Tests` does not reference the Kafka extension. So T15, T31 and T35, which assert on report output, cannot isolate from other classes' logs. Appendix A adds IVT only for the src assemblies. The E2E project references Kronikol, Otlp and Grpc but not Kafka (T36, T42). New classes must join the existing collections (`TestCorrelationStore`, `TrackingComponentRegistry`; guard `ProcessWideTrackingStateTests`). VERIFIED.
- (f) No test compares the wiki or API-Reference to the public API, or checks that options are documented. There is only `SkillDriftTests` (CLI verbs and flags) and the manual `tools/wiki-links`. F15-class drift will recur. VERIFIED.

## 6. Versions and semver

- R0 patch, R1 minor, R2 minor are consistent with CLAUDE.md. `DiagnosticKind.CaptureDegraded` already exists (`DiagnosticEntry.cs:46`), so R0 adds no public member. R1's "why" should also name the new public enum `KafkaContentFormat` and Q8's overloads. VERIFIED.
- Rule 7 is `ROADMAP.md:89-93`. Q5 is already answered by precedent: 4.13.0 shipped `GrpcTrackingOptions.PropagateTestIdentity` (default true) as a minor, its changelog saying "on by default, as 2.34.0's messaging propagation was, and no existing option's default changes". Cite it. VERIFIED.
- #167 is labelled bug. Recording the value as the body by default would be a report-output change, which by CLAUDE.md is not on its own a major. The plan needs a position: an opt-in minor (as planned), or a patch with the behaviour change called out. INFERRED.
- The red-first baselines say 4.9.0 (T-tables, §6.6 `red-4.9.0-*`). The previous release is now 4.14.6 or later (and #163's releases may come first). §11's "every claim holds" was checked at 4.11.0, 16 versions ago. BreakfastProvider is on 4.9.1, and S8's "4.9.0 control" should be the release before R1. VERIFIED.

## 7. Moved code citations (outside this remit, for the record)

`ci.yml:277-290`→`:287-302`; `Kronikol.csproj:13-23`→`:13-26`; `InternalFlowActivityListener.cs:64`→`:66`;
`GrpcTrackingInterceptor.cs:331`→`:525`, `:335-353`→`:525-536` (ids) and `:590-640` (traceparent);
`TestTrackingMessageHandler.cs:156-169`→`:157-182`; `RequestResponseLogger.cs:53-54`→`:59-60`;
`KafkaTrackingOptions.cs:67`→`:72`. Unchanged since 4.11.0: the Kafka sources other than `KafkaTrackingOptions.cs`,
`InternalFlowSegmentBuilder`, `QueryCommand.Trace.cs`, `PathEngine.cs` and `ClickHouseServerFixture.cs` (VERIFIED, `git diff --stat 709b3afc d59843fa`).
