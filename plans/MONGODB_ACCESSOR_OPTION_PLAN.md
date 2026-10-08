# MongoDB accessor option plan: #136

**Written:** 2026-10-07, at 4.9.0 (`417c8e58`), the day #136 was filed. **Status: green-lit 2026-10-08, executing**
(the owner: "implement the plan in full", and "also fix the problems you found"; roadmap row 1.22). R1, a minor, is
4.10.0 (§6). The problems found (§3) come with it: R2, a minor, the plan's Q1, is 4.11.0. It gives the property to the
five other options types without it, and makes Kafka's producer stamp the scenario its produce is recorded under. **Both
were published on 2026-10-08**, with the wiki (`289a25a`); #136 is closed. The log (§11) records what was run.

Evidence levels: **RUN** measured here, with the harness; **READ** read in the source at `417c8e58` (`file:line`);
**DOC** the wiki at `db4e473`; **ISSUE** the issue's own report; **INFERRED** reasoned, not measured.

Harness: `MONGODB_ACCESSOR_OPTION_PLAN.harness/`. `start-mongo.sh` starts MongoDB 7.0 in the podman WSL distro.
`probe/` is a net8.0 console that starts in-process TestServer hosts on the published packages, with MongoDB.Driver
2.30.0, or 3.12.0 with `-p:Driver=3`. `results/` holds its output on 4.9.0 for both drivers.

#136 asks for a way to hand `MongoClientSettings.WithTestTracking` an `IHttpContextAccessor`, as the HTTP, gRPC,
BigQuery and Cloud Storage options allow. The claim holds, and the probe shows what it costs. Take a host that wires
MongoDB tracking the way the wiki shows for `WebApplicationFactory` (Option C). Its commands reach the right scenario
only when it also registers the propagation middleware and its fetcher answers "unknown" outside a test. Otherwise
every command is lost, or lands under a test id no scenario has. With an accessor, 8 of 8 land in the right scenario
in every configuration. The fix follows the pattern the other options types already use.

The plan also corrects three things in the wiki:

- Since v2.26.3 it has said that every options class has the property, and six do not.
- Its MongoDB page never names the driver 2.x package (#142 item 6).
- The setup it calls the simplest (Option D) records nothing.

## 0. Summary

| | |
|---|---|
| What ships | `MongoDbTrackingOptions.HttpContextAccessor`. The subscriber falls back to it, and `AddMongoDbTestTracking` prefers it to the container's. Both packages get it (`Kronikol.Extensions.MongoDB` and `.V2`), since they compile the same source |
| What changes for existing code | Nothing. The property is new and null by default |
| What it does not do | Give the property to the five other options types that lack it (Q1), make `AddMongoDbTestTracking` wire a client (Q2), or read the request without an accessor (Q3) |
| Bump | R1 is a **minor** (new public property): 4.10.0 if nothing lands before it |
| Wiki | Integration-MongoDB-Extension (install, Options C and D, the options table), plus HTTP-Tracking-Setup and Diagnostics-and-Debugging (the "all options classes" claims and the Kafka sample) |
| Open | Q1 to Q5 (§9) |

## 1. How far each claim was checked

| # | The issue says | Level | Verdict |
|---|---|---|---|
| C1 | `WithTestTracking(settings, options)` creates `new MongoDbTrackingSubscriber(options)` with no accessor | READ `MongoClientSettingsExtensions.cs:17` | True |
| C2 | `MongoDbTrackingOptions` has no `HttpContextAccessor`, unlike the HTTP, gRPC, BigQuery and Cloud Storage options | READ `MongoDbTrackingOptions.cs:7-87`, `TestTrackingMessageHandlerOptions.cs:62`, `GrpcTrackingOptions.cs:28`, `BigQueryTrackingMessageHandlerOptions.cs:39`, `CloudStorageTrackingMessageHandlerOptions.cs:29` | True. These declare it: 19 extension options classes, the SQL options base class (`SqlTrackingOptionsBase.cs:21`) and the core HTTP handler's options. Six in-process options types do not (F3) |
| C3 | The subscriber's constructor takes one | READ `MongoDbTrackingSubscriber.cs:23` | True. It stores only the parameter (`:26`) |
| C4 | A host gets request-header attribution for its commands only by building the subscriber itself and chaining `ClusterConfigurator` by hand | RUN, rows A and W (§2.2) | True |
| C5 | The MongoDB page doesn't mention `Kronikol.Extensions.MongoDB.V2` | DOC `Integration-MongoDB-Extension.md:21-25` | True. No wiki page, `README.md` or `nuget-readme.md` names it, though `release.slnf:31` packs it. #142 item 6 says the same |
| C6 | Line references are to `a074cf14` (4.6.0) | READ: `git log a074cf14..417c8e58` over both MongoDB projects and `TestInfoResolver.cs` is empty | 4.9.0 behaves as 4.6.0 did |

## 2. Where it stands today

### 2.1 The code (READ)

- **How a command gets its scenario.** The subscriber resolves each command's scenario with
  `TestInfoResolver.ResolveWithSource(_httpContextAccessor, _options.CurrentTestInfoFetcher)`
  (`MongoDbTrackingSubscriber.cs:89`). The resolver (`TestInfoResolver.cs:86-119`) asks, in order:
  1. the request headers, through the accessor;
  2. in a detached flow, the scope or the owner window;
  3. the fetcher, unless it answers the unknown id;
  4. `TestIdentityScope.Current`;
  5. `TestIdentityScope.GlobalFallback`.

  If none answers, the command is not recorded.
- **Three ways in, and only one passes an accessor to a subscriber a client uses:**
  - `WithTestTracking(settings, options)` passes none (`MongoClientSettingsExtensions.cs:17`).
  - `new MongoDbTrackingSubscriber(options, accessor)` with `Subscribe(builder)` (`:23`, `:39-44`) is the one that does.
  - `AddMongoDbTestTracking(configure)` registers a singleton subscriber with the container's accessor
    (`MongoDbServiceCollectionExtensions.cs:18`), and no client subscribes it.
- **The V2 package compiles the same ten files** (`Kronikol.Extensions.MongoDB.V2.csproj:22-31`). Its test project
  links the five V3 test files (`tests/Kronikol.Tests.MongoDB.V2/Kronikol.Tests.MongoDB.V2.csproj:21-25`).
- **The house pattern for an accessor.**
  - The tracker takes `_httpContextAccessor = httpContextAccessor ?? options.HttpContextAccessor;`
    (`BigQueryTrackingMessageHandler.cs:19`, `GrpcTrackingInterceptor.cs:27`; 29 sites in `src/`).
  - The DI registration takes `options.HttpContextAccessor ??= sp.GetService<IHttpContextAccessor>();`
    (`GrpcServiceCollectionExtensions.cs:54`, `RedisServiceCollectionExtensions.cs:31`; 22 sites in 15 files).
  - A settings extension hands the options' accessor on (`CosmosClientOptionsExtensions.cs:26-29`).

  So an accessor passed to the constructor wins, then the options' one, then the container's.
  `tests/Kronikol.Tests/Tracking/HttpContextAccessorOptionsTests.cs` pins that order for the HTTP handler, the Atlas
  Data API and BigQuery, with three facts each.

### 2.2 What a host records (RUN)

**How the probe works.** `probe/Program.cs` starts one TestServer host per configuration. It uses a `WebHostBuilder`,
the legacy builder #136's hosts use, registers `AddHttpContextAccessor()` (as `TrackDependenciesForDiagrams` does)
and talks to a real MongoDB 7.0. Four scenarios send the two headers `TestTrackingMessageHandler` sends: A, then B,
then C and D at once. Each request inserts into and finds in a collection named for its scenario, so each row covers
8 commands.

**Three fetchers.**
- None.
- One that answers the unknown id outside a test, which is what the resolver expects.
- One that answers a new id on each call outside a test, which is what #133 reports `Kronikol.xUnit2`'s
  `CurrentTestInfo.Fetcher` does.

**Versions.** Published 4.9.0, on MongoDB.Driver 2.30.0 (the issue's) and on 3.12.0. The rows are identical on both
drivers (`results/probe-4.9.0.txt` and `results/probe-4.9.0-driver3.txt`).

| Wiring | Propagation | No fetcher | Fetcher: unknown | Fetcher: new id (#133) |
|---|---|---|---|---|
| A `WithTestTracking` (wiki Options A and C) | no | lost 8 | lost 8 | wrong 8 |
| | yes | right 8 (`Scope`) | right 8 (`Scope`) | wrong 8 |
| D `AddMongoDbTestTracking` (wiki Option D) | no, yes | lost 8 | lost 8 | lost 8 |
| W subscriber with the container's accessor (#136's workaround) | no, yes | right 8 (`RequestHeader`) | right 8 (`RequestHeader`) | right 8 (`RequestHeader`) |
| X subscriber with `new HttpContextAccessor()`, in a host that registers none | no | lost 8 | lost 8 | wrong 8 |
| | yes | right 8 (`Scope`) | right 8 (`Scope`) | wrong 8 |

**Reading the table.**
- **Row A.** Without an accessor, a host's commands depend on two things #136's suite did not have: the propagation
  middleware (`AddTestTrackingContextPropagation()`), and a fetcher that answers "unknown" inside a host.
- **"wrong" cells.** The call is recorded under a test id that no scenario has (INFERRED: the report groups calls
  by test id).
- **Row X.** An accessor sees a request only in a host whose container holds one. In that host the subscriber still
  reports `HasHttpContextAccessor` as true.

## 3. Findings the issue does not state

| # | Finding | Level | Where it is handled |
|---|---|---|---|
| F1 | The wiki's Option C, written for `WebApplicationFactory`, is #136's case. It sets `CurrentTestInfo.Fetcher` and no accessor, so its host's commands go as in row A | RUN, DOC `Integration-MongoDB-Extension.md:139-157` | R1 wiki |
| F2 | Option D, "the simplest approach", records nothing. It registers a subscriber that no client subscribes | RUN row D, DOC `:162-173`, READ `MongoDbServiceCollectionExtensions.cs:18` | R1 wiki; the code is Q2 |
| F3 | Since v2.26.3 the wiki has said that every options class has `HttpContextAccessor`. Six in-process ones do not: MongoDB's, Kafka's, Bigtable's, Spanner's, MediatR's and EF Core's `SqlTrackingInterceptorOptions` | DOC `HTTP-Tracking-Setup.md:204,256,281,287`, `Diagnostics-and-Debugging.md:405`; READ | R1 wiki; Q1 |
| F4 | The wiki's two accessor samples set `KafkaTrackingOptions.HttpContextAccessor`, which does not exist, so neither compiles | DOC `HTTP-Tracking-Setup.md:228-245`, `Diagnostics-and-Debugging.md:410-419`; READ `KafkaTrackingOptions.cs:9-56` | R1 wiki (the text depends on Q1) |
| F5 | EF Core's `WithSqlTestTracking(builder, options)` has #136's exact shape: it passes no accessor and has no way to take one | READ `DbContextOptionsBuilderExtensions.cs:13-17` | Q1 |
| F6 | `WithTestTracking_PreservesExistingClusterConfigurator` asserts only `NotSame` and never runs the configurator, so it still passes with the earlier configurator's call removed | READ `MongoClientSettingsExtensionsTests.cs:31-42` | R1, T6 |
| F7 | Two test classes clear the process-wide `TrackingComponentRegistry` in their constructors and `Dispose`, and run in parallel: `MongoDbServiceCollectionExtensionsTests` (no collection) and `MongoDbTrackingSubscriberTests` (collection `TestCorrelationStore`). `Constructor_AutoRegistersWithTrackingComponentRegistry` (`:424`) reads the registry between a registration and its assertion | READ; INFERRED (never seen failing) | R1, §5 |
| F8 | No MongoDB fact covers an accessor: not the one `AddMongoDbTestTracking` passes, nor any other | READ (a grep of both MongoDB test folders for `HttpContext` and `Accessor` finds nothing) | R1, T1 to T5 |
| F9 | `HasHttpContextAccessor` is true for an accessor that can never see a request (row X), so the diagnostic report says "configured" | RUN row X, READ `DiagnosticReportGenerator.cs:207` | #134 and #137 (kronikol-94, §8) |

## 4. The design

### 4.1 `MongoDbTrackingOptions.HttpContextAccessor`

```csharp
/// <summary>
/// Resolves the scenario from the test-tracking request headers when a command runs inside a host's request
/// pipeline (a host <c>WebApplicationFactory</c> starts, or any other host in the test process). Pass the host's own
/// (<c>sp.GetService&lt;IHttpContextAccessor&gt;()</c>): ASP.NET Core fills an accessor only in a host that registers
/// one (<c>AddHttpContextAccessor</c>, which <c>TrackDependenciesForDiagrams</c> calls).
/// <see cref="MongoDbServiceCollectionExtensions.AddMongoDbTestTracking"/> uses the container's when this is null, and
/// an accessor passed to the <see cref="MongoDbTrackingSubscriber"/> constructor takes precedence.
/// </summary>
public Microsoft.AspNetCore.Http.IHttpContextAccessor? HttpContextAccessor { get; set; }
```

It goes after `CurrentStepTypeFetcher`, beside the fetchers, which is where the other options types place it. The
sentence about registering an accessor is what row X measured. Write it only after T7 holds it, or drop it.

### 4.2 The subscriber

`_httpContextAccessor = httpContextAccessor ?? options.HttpContextAccessor;` (`MongoDbTrackingSubscriber.cs:26`).
Nothing else changes: `HasHttpContextAccessor` and the resolver call both read the field.

### 4.3 `WithTestTracking`

The signature is unchanged. It already builds the subscriber from the options, so 4.2 carries the accessor through.
Its doc gains a `<param>` for each parameter, and one sentence: in a host, set `options.HttpContextAccessor`. There is
no overload (Q4).

### 4.4 `AddMongoDbTestTracking`

`new MongoDbTrackingSubscriber(options, options.HttpContextAccessor ?? sp.GetService<IHttpContextAccessor>())`.
The options' accessor comes first, which is the order every other registration uses (§2.1), and nothing is written
back to the options. Today the container's accessor is the only one, so existing callers see no change.

### 4.5 Compatibility

- **Source and binary.** A property is added to a record, and no member changes or moves.
  `MongoDbTrackingOptions` is a record, so the new member also joins its equality, its `with` copies and its
  `ToString` (A3).
- **Behaviour.** The property is null by default. The constructor's fallback then reads null, and
  `AddMongoDbTestTracking` resolves as it did.
- **Report output.** Unchanged for any configuration that sets nothing. Kronikol4J gets no ledger line.

## 5. Tests, red first

Every MongoDB fact goes in a file the V2 test project links, so it runs on driver 2.x as well as 3.x. A new file needs
its own `<Compile Include="..." Link="..." />` line in `tests/Kronikol.Tests.MongoDB.V2/Kronikol.Tests.MongoDB.V2.csproj`.
For the accessor, use a private test double like `HttpContextAccessorOptionsTests`'s `TestHttpContextAccessor`, which
holds a `DefaultHttpContext` carrying the two headers. For a command, use the existing
`MakeStartedEvent(...)` helper.

| # | Fact | File | On 4.9.0 |
|---|---|---|---|
| T1 | `Subscriber_reads_HttpContextAccessor_from_options_when_not_passed_directly`: a command fired with the options' accessor lands under the header's test with `RequestHeader`, though the fetcher answers another test | `MongoDbTrackingSubscriberTests.cs` | Does not compile (no property). Behaviour proved by M1 |
| T2 | `Subscriber_explicit_accessor_takes_precedence_over_options`: two accessors carrying two header pairs, and the constructor's wins | same | As T1, and M2 |
| T3 | `Subscriber_has_no_accessor_when_neither_options_nor_parameter` | same | Passes: a guard |
| T4 | `WithTestTracking_passes_the_options_accessor_to_its_subscriber`: find the subscriber `WithTestTracking` registered (by a unique `ServiceName`); `HasHttpContextAccessor` is true, and a command fired on it lands under the header's test | `MongoClientSettingsExtensionsTests.cs` | Does not compile. M1 |
| T5 | `AddMongoDbTestTracking_prefers_the_options_accessor_to_the_containers`, and `AddMongoDbTestTracking_uses_the_containers_accessor_when_the_options_carry_none` | `MongoDbServiceCollectionExtensionsTests.cs` | The first does not compile (M4). The second passes and pins today's behaviour (F8, M5) |
| T6 | `WithTestTracking_PreservesExistingClusterConfigurator`, strengthened: run the configurator on a `ClusterBuilder`, and assert that the earlier configurator ran and the call completed | `MongoClientSettingsExtensionsTests.cs` | Passes, as a strengthened vacuous fact does (F6). It turns red only under M3; say so in the log, do not call it red-first |
| T7 | `A_hosts_commands_land_in_the_scenario_its_request_names`: a TestServer host, a real MongoDB, and `WithTestTracking` with the options' accessor. Scenarios A to D run as in the probe, with no propagation and a fetcher that answers a new id (row A's worst cell). Expect 8 of 8 right, with `RequestHeader` | new `MongoDbHostAttributionTests.cs`, linked into V2 | Does not compile. With the accessor line removed it reads wrong 8, which is the behaviour red |

- **T7's fixture.** `MongoServerFixture` reads `KRONIKOL_TEST_MONGO` when it is set (on this machine, the harness's
  podman container). Otherwise it starts `Testcontainers.MongoDb` 4.8.1, the version `Kronikol.Tests.TcpTap` already
  uses. It skips only on `DockerUnavailableException`, as `ClickHouseServerFixture` does.
- **Where T7 runs in CI.** Both MongoDB test projects run in ci.yml's "Build & Test (auto-discovered remainder)" job
  (`:277-291`) on ubuntu, which has Docker. Read that job's log for T7 before calling R1 done (Q5).
- **The registry race.** T4 and T5 read the process-wide registry. Move `MongoClientSettingsExtensionsTests` and
  `MongoDbServiceCollectionExtensionsTests` into `[Collection("TestCorrelationStore")]` with the classes that clear it,
  which also fixes F7.

**Mutations.** Each must turn the facts named red:

| # | Mutation | Red |
|---|---|---|
| M1 | Drop `?? options.HttpContextAccessor` in the subscriber | T1, T4, T7 |
| M2 | Swap to `options.HttpContextAccessor ?? httpContextAccessor` | T2 |
| M3 | Drop `existingConfigurator?.Invoke(builder)` in `WithTestTracking` | T6 |
| M4 | `AddMongoDbTestTracking` passes `sp.GetService<IHttpContextAccessor>() ?? options.HttpContextAccessor` | T5, first fact |
| M5 | `AddMongoDbTestTracking` passes `options.HttpContextAccessor` alone | T5, second fact |
| M6 | Drop `subscriber.Subscribe(builder)` in `WithTestTracking` | T7 |

**Acceptance.** Run the probe with `-p:AccessorOption=true -p:KronikolVersion=<local>` against a local feed of R1's
packages, on both drivers. Row N must read right 8 with `RequestHeader` in all six cells, and rows A, D, W and X must
read as they do in `results/`.

## 6. Slices, releases and records

### 6.1 Slices

| Slice | What | Bump |
|---|---|---|
| S1 | T1 to T7 and F7's collection change, red where §5 says | none |
| S2 | §4.1 to §4.4 | minor |
| S3 | M1 to M6, then the probe acceptance on both drivers | none |
| S4 | The wiki edits (§6.4), kept as a script in the harness and applied after publication | none |

### 6.2 Release

| Release | Contents | Bump | Why this part moved |
|---|---|---|---|
| R1 | S1 to S4 | minor: 4.10.0 if nothing lands before it | `MongoDbTrackingOptions.HttpContextAccessor` is new public surface. CLAUDE.md makes a new option a minor even when its default preserves today's behaviour |

### 6.3 Changelog draft

```markdown
## [4.10.0] - YYYY-MM-DD

**Minor - MongoDB tracking takes an IHttpContextAccessor through its options (#136).**
`plans/MONGODB_ACCESSOR_OPTION_PLAN.md` R1. `MongoDbTrackingOptions.HttpContextAccessor` is new public surface, so the
minor part moves (4.9.0 to 4.10.0). Code that does not set it behaves as before.

### Added
- `MongoDbTrackingOptions.HttpContextAccessor`, in `Kronikol.Extensions.MongoDB` and `Kronikol.Extensions.MongoDB.V2`.
  `MongoClientSettings.WithTestTracking` hands it to the subscriber, so a host's commands land in the scenario its
  request headers name, as the HTTP, gRPC, BigQuery and Cloud Storage options already allowed. Measured on 4.9.0 in a
  TestServer host with MongoDB.Driver 2.30.0 and 3.12.0: through `WithTestTracking`, a host's commands were lost unless
  the host registered `AddTestTrackingContextPropagation()`, and went to a test id no scenario has when the fetcher
  answered one outside a test. With the accessor, every one of them lands in its scenario. An accessor passed to the
  `MongoDbTrackingSubscriber` constructor still wins, and `AddMongoDbTestTracking` uses the options' accessor before
  the container's.

### Documentation
- The MongoDB page names `Kronikol.Extensions.MongoDB.V2` for MongoDB.Driver 2.x. Its WebApplicationFactory setup passes
  the host's accessor, and it says that `AddMongoDbTestTracking` registers a subscriber that a client must subscribe
  (on its own it recorded nothing). HTTP-Tracking-Setup and Diagnostics-and-Debugging no longer say that every options
  class has `HttpContextAccessor`, and their Kafka sample compiles.
```

### 6.4 Wiki

| Where | Change |
|---|---|
| Integration-MongoDB-Extension, Install (`:21-25`) | Two lines: `Kronikol.Extensions.MongoDB` for MongoDB.Driver 3.x, and `Kronikol.Extensions.MongoDB.V2` for 2.x, with the same namespace and API (#142 item 6) |
| Option C (`:139-157`) | Set `HttpContextAccessor = sp.GetService<IHttpContextAccessor>()`, and say why: a host's commands run in its request pipeline, where TestServer does not carry the test's context, so the fetcher cannot answer (F1) |
| Option D (`:162-173`) | Say that it registers a subscriber for a client to subscribe, show the `ClusterConfigurator` line that does it, and point a host at Option C (F2). Drop "the simplest approach" |
| Configuration Reference (`:181-201`) | Add a `HttpContextAccessor` row, marked 4.10.0+ |
| Dual-Resolution note (`:203`) | Name the options property and the order of precedence |
| HTTP-Tracking-Setup `:204`, `:256`, `:281`, `:287`; Diagnostics-and-Debugging `:405` | Replace "all options classes" with the list of options types that lack it: five after R1, none if Q1 ships. Take MongoDB out of "already auto-resolved": it was resolved only through `AddMongoDbTestTracking`, which records nothing on its own |
| HTTP-Tracking-Setup `:228-245`; Diagnostics-and-Debugging `:410-419` | The Kafka sample passes the accessor through `new KafkaTracker(options, accessor)`, the constructor that exists. It stays as written if Q1 gives Kafka the property |

Each line number is checked against the wiki at the time of the edit. #142's item 6 is this table's Install row, so a
plan for #142 drops it. Its other items edit other pages (Tracking-Dependencies, Event-Driven-Architecture-Testing,
Parallel-Safe-Background-Correlation, Integration-xUnit2 and Multi-Host-Test-Architectures), none of the lines above.

### 6.5 Kronikol4J

No ledger line, since the report output does not change.

### 6.6 Before declaring done

- Build `release.slnf` in Release, for every target.
- Run the core suite and both MongoDB test projects. Find T7 in the remainder job's log
  (`gh run view <run> --log | grep MongoDbHostAttributionTests`), on both projects.
- Grep `src/` doc comments and the wiki for claims about which options take an accessor ("all options classes",
  "auto-resolved") that the release makes false.
- Close #136 once 4.10.0 is published (`gh issue close`), and comment on #142 that item 6 is done.
- Apply the usual release steps: the templates' pin, the history action's `VERSION`, and the tag.

## 7. Where it sits in the roadmap

Not placed. The proposed placement is stage 1 by rule 1, as 1.16 was: a shipped integration loses or misattributes a
host's commands when it is wired the way its own wiki page shows. Take the row number with "taking 1.N now" when the
plan is green-lit, because several sessions are writing plans for the same batch of issues (#132 to #145, all filed
2026-10-07). No decision number is needed, since R1 follows an existing pattern.

## 8. Found on the way, not in this plan

| What | Level | Where it goes |
|---|---|---|
| #133: a fetcher that answers a new id outside a test outranks the scope the propagation middleware sets (rows A and X with propagation: wrong 8) | RUN | #133. kronikol-1c's xUnit2 plan (#132) names it |
| The diagnostic report's accessor column reads `instances[0]` and says "configured" for an accessor that sees no request (F9). `ITrackingComponent.HasHttpContextAccessor`'s doc promises null from a `bool` | READ, RUN | #134 and #137: kronikol-94 owns `ITrackingComponent.cs` and the column (agreed by message, 2026-10-07) |
| Multi-Host-Test-Architectures' "HttpContextAccessor Wiring Order" covers Cosmos only | DOC `:151-179` | #142 |
| `MessageTrackerOptions` is the one core options type with a `CurrentTestInfoFetcher` and no `HttpContextAccessor`: `MessageTracker` takes an accessor by constructor only. R2's guard covers extension options types, since the wiki's claim is about those (found 2026-10-08) | READ `MessageTrackerOptions.cs`, `MessageTracker.cs:37-80` | kronikol-94, whose #134 edits `MessageTracker.cs`, keeps it out for now |
| `HttpContextAccessorOptionsTests.TestTrackingMessageHandler_explicit_accessor_takes_precedence_over_options` asserts only that the handler is not null (found 2026-10-08; R2 strengthens the fallback fact beside it) | READ | kronikol-94: its #134 R3 changes how the handler reads the accessor |
| `CurrentStepTypeFetcher` is declared by the options of 24 extensions and read by none (EF Core's `WithTestInfoFrom` copies it from the HTTP options, and nothing reads the copy): only the core HTTP handler and `MessageTracker` read theirs. The phase an extension acts on is `TestPhaseContext`'s, which the framework adapters set (found 2026-10-08, writing R1's doc comments) | READ: a grep of each extension's sources for the member | R1 documents MongoDB's as unread. Removing the member from the others is a breaking change, so it waits for the owner (v5) |
| Eight `MongoDbTrackingOptions` members have no doc comment: `ServiceName`, `Verbosity`, both fetchers, `SetupVerbosity`, `ActionVerbosity`, `TrackDuringSetup` and `TrackDuringAction` | READ | Whenever they are next touched; not R1 |

## 9. Questions, with recommendations

| # | Question | Recommendation |
|---|---|---|
| Q1 | **Taken 2026-10-08 as R2 (4.11.0).** Should the five other options types without the property get it (Kafka's, Bigtable's, Spanner's, MediatR's and EF Core's `SqlTrackingInterceptorOptions`)? That would make the wiki's "all options classes" (since v2.26.3) and its Kafka sample true. It would come with a guard fact: every in-process options type with a `CurrentTestInfoFetcher` has an `HttpContextAccessor` | Yes, as a separate minor after R1. EF Core's `WithSqlTestTracking` has #136's shape (F5); the other four take an accessor some other way today. Not in R1, since #136 names MongoDB. #135's plan (kronikol-cc) adds two other members to `KafkaTrackingOptions.cs`, so whichever ships second rebases onto the other |
| Q2 | Should `AddMongoDbTestTracking` record something on its own? The choices: (a) docs only; (b) a `WithTestTracking(this MongoClientSettings, MongoDbTrackingSubscriber)` overload, so a host writes `settings.WithTestTracking(sp.GetRequiredService<MongoDbTrackingSubscriber>())`; (c) have it rewrite `IMongoClient` registrations | (a) in R1, since the page is wrong. (b) only if asked. (c) no: a client's settings freeze when the client is built, so it would have to rebuild the user's client |
| Q3 | Should every tracker without an accessor read the request through a static `HttpContextAccessor` in the resolver? | No. It would change the resolution order for every tracker whose user set no accessor, and row X shows it sees nothing in a host that registers no accessor anyway |
| Q4 | Should there also be an overload `WithTestTracking(settings, options, IHttpContextAccessor)`, the issue's second suggestion? | No. The options property is how every other options type takes the accessor, and one way is enough |
| Q5 | Should R1 add the host lane (T7, a MongoDB container in CI), or rely on the unit facts and the probe? | Add it. It is the one fact that runs the driver's events inside a host, which is where #136 happens, and TcpTap's tests already start a MongoDB container in CI |

## 10. Assumption ledger

| # | Assumption | Level | What would change |
|---|---|---|---|
| A1 | The driver raises `CommandStartedEvent` on the request's async flow, so the accessor sees the request | RUN for insert and find, on 2.30.0 and 3.12.0 against server 7.0. INFERRED for other commands (bulk writes, transactions, cursors) | A command raised off the flow would resolve as it does today. T7 could add a bulk write |
| A2 | #136's hosts register an `IHttpContextAccessor` | ISSUE: its workaround worked. RUN row X: it must | The doc comment says so (§4.1) |
| A3 | Nothing compares `MongoDbTrackingOptions` records or reads their `ToString` | INFERRED | A new member changes both |
| A4 | A call under a test id that no scenario has shows in no scenario | INFERRED | The "wrong" cells would read differently in a report |
| A5 | TestServer does not carry the test's async context into a request (`TestServer.PreserveExecutionContext` is false by default), so a fetcher reading the test framework's context cannot answer inside a host | DOC (the ASP.NET Core API), ISSUE (#142 item 4). The probe does not measure it: its fetchers stand in for the two answers such a fetcher gives | The Option C wiki text (§6.4) would need another reason |

## 11. Log

- 2026-10-07: written at 4.9.0 by kronikol-f1. Claims checked (§1), and the probe run on both drivers (§2.2).
  Coordinated by message: kronikol-94 (#134) owns `ITrackingComponent` and the diagnostic column; kronikol-1c (#132)
  holds the xUnit2 files. kronikol-cc (#135, Kafka) took roadmap row 1.21 and D37. It leaves this plan the two Kafka
  wiki samples and the "all options classes" claims (§6.4), and keeps to Integration-Kafka-Extension,
  Querying-Reports:839, Integration-OpenTelemetry-Extension:7 and API-Reference's Kafka rows.
- 2026-10-08: green-lit by the owner ("Can you implement the plan in full, in a separate worktree so you don't
  interfere with the other sessions. Also fix the problems you found"), so Q1 to Q5 are taken as recommended: R2 is Q1,
  Q2 is (a), Q3 and Q4 are no, and Q5 is yes. Executed by kronikol-f1 in worktree `Kronikol-mongo136-impl` (branch
  `mongo-136` off `e59305da`, 4.9.1). Roadmap row 1.22 taken by message (no decision number); kronikol-50 took 1.23
  (#141), kronikol-1c 1.24 and D38 (#132), and kronikol-28 releases #133 as a patch.
- R1, red first (harness `r1/`): T1 to T7 did not compile on 4.9.1. With the property added and nothing reading it,
  T1, T4, T5's first fact and T7 failed for their own reasons (`red-property-only-v3-detail.txt`): T7 read "right 0,
  wrong 8, lost 0; TestContext 8", row A's worst cell. T2, T3, T5's second fact and the strengthened T6 passed, as §5
  says they would. Green: 177 facts in each MongoDB test project.
- R1 mutations (`r1/mutate.py`, `r1/mutations.txt`): M1 to M6 were each caught by exactly the facts §5 names, and the
  restored tree passed 177 of 177.
- R1 acceptance: the probe on local 4.10.0 packages (`-p:AccessorOption=true`) reads right 8 with `RequestHeader:8` in
  all six cells of row N, on MongoDB.Driver 2.30.0 and 3.12.0, and rows A, D, W and X are identical to 4.9.0's
  (`results/probe-r1-local.txt`, `results/probe-r1-local-driver3.txt`).
- While writing R1's doc comments: `MongoDbTrackingOptions.CurrentStepTypeFetcher` is never read, nor is any extension
  options type's (§8). R1's doc says so for MongoDB. kronikol-94 took the other 22 for its #134 R0 (doc comments) and
  V5_PLAN open question 9.
- R1 published 2026-10-08 as 4.10.0 (`47a0203b`, tag `v4.10.0`): Release 37750369225 pushed all 62 packages, and CI
  37750367339 passed, the host lane among it in both MongoDB projects
  (`Passed ... MongoDbHostAttributionTests.A_hosts_commands_land_in_the_scenario_its_request_names`, 518 ms and 387 ms,
  against a Testcontainers MongoDB on ubuntu). Verified before the push: both MongoDB projects 177, TcpTap 265 with 4
  skipped, the core suite 6,903 with 2 skipped, and `release.slnf` built and packed in Release (62 packages).
- R2 (Q1), red first (harness `r2/`): the facts did not compile on 4.10.0. The source guard failed there, naming the
  five types (`red-guard-4.10.0.txt`). With the properties added and nothing reading them, every fallback, options-first
  and entry-point fact failed for its own reason, as did the two Kafka producer facts
  (`red-property-only-<project>.txt`). The precedence, no-accessor and container facts passed: they hold behaviour that
  was already right. Kafka's producer stamped a message from the fetcher alone, so a produce recorded under a request's
  scenario sent a message carrying another one: fixed in R2, raised by kronikol-cc (#135), which rebases onto it.
- R2 mutations (`r2/mutate.py`, `r2/mutations.txt`): 26, one per changed line (each Kafka registration separately), each
  caught by exactly the facts it names and no other. The restored tree passed in all five projects.
- The wiki's Option D recipe (subscribe `AddMongoDbTestTracking`'s subscriber in the client's registration) is held by
  the host lane, now a theory over Options C and D as the page writes them.
- R2 published 2026-10-08 as 4.11.0 (`709b3afc`, tag `v4.11.0`): Release 37754946619 pushed all 62 packages, and CI
  37754943415 and CodeQL 37754943411 passed (R1's CodeQL, 37750367337, passed too). nuget.org listed all 62 ids at 4.10.0 by 09:22 UTC: its pages answered 404 for about 25 minutes
  after the 08:42 push while nuget.org validated them. 4.11.0's Release finished at 09:22 and nuget.org listed all 62
  ids at 09:34.
- The probe on the published 4.10.0 packages, restored from nuget.org (`--no-http-cache`, since the local cache still
  held the old version list): row N reads right 8 with `RequestHeader:8` in all six cells on MongoDB.Driver 2.30.0 and
  3.12.0, and rows A, D, W and X are identical to 4.9.0's (`results/probe-4.10.0.txt`, `results/probe-4.10.0-driver3.txt`).
- The wiki edits (`wiki/wiki_136.py ... 4.10.0 4.11.0`) went in as `289a25a`: eight pages, 48 lines added and 19
  removed. `tools/wiki-links` finds no dead or reversed link on the result. #136 is closed with a comment naming both
  releases, and #142 has a comment saying item 6 is done.
- Left to peers, as agreed by message: F9 and the diagnostic accessor column (kronikol-94, #134/#137), #133
  (kronikol-28), the stale resolver order in HTTP-Tracking-Setup's "How It Works Internally" (kronikol-94, whose #134
  changes the resolver), and Multi-Host-Test-Architectures' Cosmos-only wiring section (#142).
