# CLIENT_FLOORS_PLAN harness

Everything `plans/CLIENT_FLOORS_PLAN.md` measured, as run on 2026-10-27 (Windows 11, .NET SDK 10.0.300, Kronikol
5.12.0 `1ee6373a`), and in the audit the same day at 5.14.0 (`8d7d1e1d`), which added Q12 and Q13, the PR #492 runs,
`prototype/`, `restore/cpm-float/` and `nuget/fixed-pins.*`. The scripts were run from the session scratchpad, so their
absolute paths name it; change `S=`, `W=` and `H=` before running them again. No `bin/` or `obj/` is kept.

## probe/: what the client sends, what the emulator answers

- `Program.cs` builds a `BigQueryClient` on an emulator's root (`BaseUri`, `ApiKey = "any"`) whose HTTP client factory
  puts a recording handler next to the network, inside Kronikol's tracking handler when the mode is `kronikol`. It
  creates a table with a top-level TIMESTAMP, one in a RECORD and two in a REPEATED field, inserts a row, then runs
  scenarios Q1 to Q11 (the issue's call, no options, a literal, a parameter, nested fields, `ListRows`, DML, three
  options that keep 3.12.0 on the job path, the job path by hand) and, since the audit, Q12 and Q13 (the issue's call
  with an INT64 and a STRING parameter in the `WHERE`), and prints every request, the rows of every answer and what
  reading the TIMESTAMP did. In `kronikol` mode it ends with what Kronikol recorded.
- `client-3.0.0/`, `client-3.11.0/`, `client-3.12.0/`: the same program on each client, with the published
  Kronikol.Extensions.BigQuery 5.12.0 (NU1605 let through, so the extension packed against 3.12.0 runs on an older
  client). `client-3.0.0` defines `NO_INT64_OPTION`: `GetQueryResultsOptions.UseInt64Timestamp` arrived in 3.6.0.
- `consumer-floor/`: the program on client 3.12.0 with the extension taken as a project reference from the #205
  worktree while its csproj was pinned to 3.0.0, which is a consumer of the proposed R1.
- `matrix.py` turns `results/emu*-plain.txt` into `results/matrix.md`.

Run: `cd probe/client-3.12.0 && dotnet run -- http://127.0.0.1:19050/ plain tag`.

## results/

`emu<tag>-client<version>-<mode>.txt` for emulators 0.4 (which is v0.4.4), 0.6.0, 0.6.1, 0.6.6 and 0.8.1, clients
3.0.0 (0.8.1 only), 3.11.0 and 3.12.0, modes `plain` and `kronikol` (0.8.1 only); `matrix.md`; and
`emu0.8.1-nested-timestamp-curl.txt`. On 0.8.1, Q6 (`ListRows`) read the first table named `t` of any dataset, the
3.11.0 run's (the emulator's `tabledata.list` defect, plan section 8); it has the same columns, so Q6's TIMESTAMP results
stand.

From the audit: `emu0.4-client*-plain-params.txt` and `emu0.8.1-client*-plain-params.txt`, the probe with Q12 and Q13
on clients 3.11.0 and 3.12.0; and `emupr492-client*-plain.txt`, the probe against a build of goccy/bigquery-emulator
PR #492 (`emulator/pr492-build.sh`: its head does not build as it stands, so the script moves grpc to its newest
release).

## emulator/

- `emu-start.sh tag:port ...` starts `ghcr.io/goccy/bigquery-emulator:<tag>` containers in the podman WSL VM:
  `MSYS_NO_PATHCONV=1 wsl -d podman-machine-default -u root -- sh /mnt/c/.../emu-start.sh 0.8.1:19050 0.4:19051`.
  The Windows build of 0.8.1 does not start ("failed to create analyzer: ... wasm trap: invalid memory address or nil
  pointer dereference").
- `nested-timestamp-curl.sh <base> <dataset>`: the client-free repro of the nested TIMESTAMP gap.

## floor/: the oldest client the extension can ask for

- `floor_probe.sh <version> ...` pins Google.Cloud.BigQuery.V2 to each version in the worktree's extension csproj,
  builds `tests/Kronikol.Tests.BigQuery` (which takes the client from the extension), records what NuGet resolved and
  any NU1901-NU1904 warning, runs the suite and puts the csproj back. `floor.txt`: 3.0.0 to 3.12.0, 95 of 95 each, no
  audit warning.
- `vulnerable.txt`: `dotnet list package --vulnerable --include-transitive` for the probe on clients 3.0.0 and 3.12.0
  with the published extension, both clean.
- `core_at_floor.sh` pins 3.0.0 the way `floor_probe.sh` does, builds `tests/Kronikol.Tests` and runs the facts that
  touch the extension. `core.txt`: 9 of 9, plus an unrelated AngleSharp advisory.

## restore/

`cpm/` (central package management with transitive pinning) and `direct/` (a plain project) pin 3.11.0 beside the
published 5.12.0 extension. `results.txt`: NU1109 and NU1605, both errors. `cpm-float/` holds a float in
`Directory.Packages.props`: its `results.txt` is NU1011, and a clean restore with
`-p:CentralPackageFloatingVersionsEnabled=true`.

## R1, as released

- `r1-acceptance.sh` is section 5.1's "Proving R1": it packs Kronikol and the extension at `5.22.1-r1floor`, a version
  never published, into a feed of its own, restores `restore/cpm-r1/` and `restore/direct-r1/` (client 3.11.0 beside
  the extension, #205's two failing shapes) and runs `probe/consumer-packed/client-{3.11.0,3.12.0}/` through the packed
  extension against a fresh emulator 0.8.1 for each client. Its packages restore into a folder of their own, so the
  unpublished version never reaches the machine's NuGet cache. `results/r1-acceptance.txt` and
  `results/emu0.8.1-r1-packed-*.txt` are its output.
- `floor/floor-r1.txt`: the suite at the release commit on 3.12.0 (the direct reference) and on 3.0.0
  (`-p:FloorRun=true`), with what NuGet resolved.

## floor/floor_probe2.py: the probe for R2 and R3

`floor_probe.sh` generalised to take the package id, the packable project that references it and the test projects
that run it: for each version it pins the package in the project's csproj, builds each test project with
`-p:FloorRun=true` (the test project's own newest reference drops out) and `-p:NuGetAuditMode=all` (an advisory anywhere
in the graph is reported), records what NuGet resolved and every NU1901 to NU1904, runs the suite and puts the csproj
back. `--search` binary-searches the stable releases of the reference's major for the oldest that passes, after
checking the newest; `--also Id=version` moves another reference of the project with it, for a family that must move
together.

## R2a, as released

- `floor/r2a-<package>.txt`: each binary search, one line per probed release with the suite's result, every advisory
  NuGet audit reported with the project whose graph held it, and what NuGet resolved; the closing line names the oldest
  release that passed, and a `#` note gives any reason the floor sits above it. Bigtable, Cloud Storage, Pub/Sub and
  Kafka came from `floor_probe2.py --search`; Kafka's two suites ran on each release, without Docker, so its three
  broker facts were skipped there and ran in `floor/r2a-suites.txt`.
- `floor/spanner_floor.py`: Spanner.Data and Spanner.V1 move together, and Grpc.Core.Api goes to the least each release
  needs, which it reads from the NU1605 that a pin of 2.23.0 draws.
- `floor/grpc_floor.py`: the suite's server, Grpc.AspNetCore, brings a Grpc.Net.Client and a Google.Protobuf of its own
  release, so each Grpc.Net.Client release runs against the server of the same release (`-p:GrpcServerFloor=X` under
  `-p:FloorRun=true`) with Google.Protobuf at what that server asks for.
- `probe-r2a/{storage,pubsub,spanner}/`: the fourth test for the three unit suites. Each runs the real client through the
  extension against an emulator (fake-gcs-server, the gcloud Pub/Sub emulator, cloud-spanner-emulator), on the floor
  the extension asks for, or on the newest of the major with `-p:Newest=true`, and prints each call's outcome and what
  Kronikol recorded; `results/r2a-<package>-{floor,newest}.txt` are their output, each on a freshly started emulator.
  The Pub/Sub probe found the subscriber's stop failing on 3.39.0, and the Spanner probe found every DML statement
  through the interceptor drawn as a SELECT (both fixed in R2a; plan section 11). Bigtable needs no probe: the
  extension calls none of the client's types. Kafka's broker facts and gRPC's in-process server are their suites' own
  fourth test.
- `floor/r2a-suites.sh`: each R2a suite on the newest client and on its floor, and the six Testcontainers suites, one
  line each in `floor/r2a-suites.txt` with every advisory NuGet audit reported. On this machine podman logs to journald,
  which returns no logs, so a Testcontainers start that waits on a log line (Kafka's, and MongoDB's with a user) never
  completes through the VM's own API: the file records Kafka's broker facts against a broker started by hand
  (`KRONIKOL_TEST_KAFKA`), then TcpTap and Kafka through a podman API started with `CONTAINERS_CONF_OVERRIDE` naming
  `log_driver = "k8s-file"` (and the VM's `volume_path`, which the override otherwise loses) on another port. The
  scripts are in `podman/`: `podman-api-2376.sh` and `stop-api-2376.sh`, the override, and `start-kafka.sh`, the broker.
- `floor/ledger_mutations.py`: the ledger's facts against seven changes they exist to catch, each applied, run and put
  back; `floor/ledger-mutations.txt` names the facts each one turned red.

## R2b, as released

- `floor/r2b-search.sh`: the six binary searches, one after another, with `floor_probe2.py --search`;
  `floor/r2b-<package>.txt` are their output, as R2a's. `floor/r2b-eventhubs.txt` closes with the suite on 5.6.0 and on
  5.12.2 after the wrapper fix moved the Event Hubs floor; `floor/r2b-servicebus-di.txt` and the copies beside it record
  the three searches the Dependency Injection floor took (plan section 5.2).
- `floor/first_with_member.py`: the first release of a major whose XML documentation names a member, by binary search
  over the nupkgs read in memory. It dated `Identifier` (Event Hubs 5.6.0), `UseDevelopmentEmulator` (Event Hubs 5.11.0,
  Service Bus 7.17.3) and the members the wrappers had to override.
- `probe-r2b/wrappers.cs`: a file-based app that lists, for each Kronikol class deriving from an SDK client, the base's
  virtual members it does not override, read from the assemblies without loading them. It found F11 and the members
  of F12 and F13; the guard facts that `tests/Kronikol.Tests/Packaging/WrapperCoverage.cs` serves now do the same on
  every run, so the app is the record of the sweep, not a step to repeat.
- `probe-r2b/{blobs,queues,cosmos,servicebus,eventhubs}/`: the fourth test. Each runs the real client through the
  extension against an emulator from `podman/start-r2b-emulators.sh` (Azurite, the Cosmos DB vnext-preview emulator
  over HTTP, the Service Bus emulator with its SQL Server, the Event Hubs emulator), on the floor the extension asks
  for, on the newest with `-p:Newest=true`, or, for Service Bus and Event Hubs, whose floors predate
  `UseDevelopmentEmulator`, on a given release with `-p:ClientVersion=<release>`. The Service Bus and Event Hubs probes
  hold the wrappers as the SDK types, as dependency injection hands them out, and call the members the wrappers left to
  their base. `results/r2b-<package>-<run>.txt` are their output; `results/r2b-servicebus-newest-before-fix.txt` and
  `results/r2b-eventhubs-newest-before-fix.txt` are the runs that found F12 and F13, and `results/r2b-cosmos-floor-plain.txt`
  is the Cosmos floor with `--plain`, no tracking, which shows F16 is the client's own.
- `probe-r2b/eh-base/`: what an Event Hubs client answers when built through the constructor for mocking, and when built
  from a namespace, a hub and a credential that throws if asked for a token: the names, the identifier, closing, and
  how soon a stray call fails (`-p:EventHubsVersion=<release>`; `results/r2b-eventhubs-base.txt` on 5.12.2, 5.6.0 and
  5.5.0). It is the evidence for F13's fix; F14, the same base for Service Bus, failed on the network instead and was
  reverted.
- `floor/wrapper_mutations.py`: five mutations, each an override removed from a wrapper, each run against the guard
  fact that holds that wrapper and put back; `floor/wrapper-mutations.txt` shows each guard red with the member named,
  after F17.

## R2c, as released

- `floor/r2c-search.sh`: the Sqlite, Oracle and Redis searches with `floor_probe2.py --search`, and
  `floor/r2c-advisory.sh`, which builds a package at each release of a range and lists what NuGet audit reports, to find
  the first release past an advisory; `floor/r2c-<package>.txt` close with the floor each took, past the advisory or
  at the suite's oldest pass. `floor/r2c-harmony.txt` is the Harmony search, which found F22.
- `floor/r2c-di.sh`: the Dependency Injection floor of MediatR and DispatchProxy, with the run that found F19, red, and
  the same run after the fix (`floor/r2c-di.txt`).
- `floor/otel_floor.py` and `floor/paired_floor.py`: a search whose suite needs partners of the same release. Each
  release of the series is pinned in the package and passed to the test project as a property (`-p:OpenTelemetryFloor`,
  `-p:EfCoreFloor`, `-p:MongoBsonFloor`), under which the test project takes its partners at that release;
  `paired_floor.py` is `otel_floor.py` made general (`--pin`, `--series`, `--property`, `--env`). Both read NuGet
  audit's warnings in either form since F28. `floor/r2c-opentelemetry.txt`, `floor/r2c-efcore.txt` and
  `floor/r2c-tcptap.txt` are their output; the first two lines of the last are the run that found F27.
- `floor/r2c-containers.sh`: the runs that need podman, one after another: the EF Core and TcpTap searches through the
  session's podman API on 2376, then the fourth test for Redis and Oracle against servers it starts as root in the VM
  (`searches`, `probes`, or both).
- `probe-r2c/redis/` and `probe-r2c/oracle/`: the fourth test, the real client through the extension against Redis 7 and
  Oracle Database Free 23, on the floor the extension asks for or on the newest with `-p:Newest=true`.
  `results/r2c-<package>-<run>.txt` are their output: every call passed on both, and the Oracle record, the same on
  both, is F26.
- `floor/r2c-suites.sh`: each R2c suite on the newest and on the floor, one line a run with what NuGet audit reported
  (`floor/r2c-suites.txt`); the EF Core and TcpTap suites run through it with `DOCKER_HOST` set to the podman API.

## f30/: `[HeadIn]` on a later TUnit.Core, found by R3a

Run from the session scratchpad, so `engine/f30run.csproj` names the adapter by its absolute path: the F30 worktree's, or the one `-p:Adapter=` names, as R3a's run on its TUnit floor did (F35).

- `consumer/`: a program that makes a `HeadInAttribute` from the published Kronikol.TUnit 5.27.7 on the TUnit.Core
  given (`-p:TUnitCore=1.53.0`); `results/consumer.txt` has it on 1.43.11, 1.53.0 and 1.76.45, the last two the
  `TypeLoadException` of F30.
- `members/`: the members of `IDataSourceAttribute` and of TUnit's two data source generator base classes on the
  TUnit.Core given (`results/members.txt`, the same three releases): 1.53.0 adds `DeferEnumeration` to the interface,
  and the base class implements it. `results/first-with-member.txt` is `floor/first_with_member.py` finding 1.53.0.
- `engine/`: a TUnit project with one `[HeadIn]` test, run by TUnit's own engine on the TUnit release given
  (`-p:TUnitVersion=`), through the adapter of the F30 worktree, or the published one with `-p:Published=5.27.7`
  (`results/engine.txt`).

## R3a, as released

Each search ran in a worktree of its own, one after another, as they share its builds; `floor/r3a-<package>.txt` close
with the floor each took, and the lines above a `# the run below` note are the earlier run it replaced, with why.

- `floor/r3a-core.sh`: the core's `Microsoft.AspNetCore.Mvc.Testing`, the whole core suite at each release
  (`floor/r3a-core.txt`), without `FloorLedgerTests`, which holds the csproj to the ledger and so fails every other
  release. `floor/r3a-di.sh` then searched the DI abstractions of ServiceBus, MediatR and DispatchProxy again, with the
  core at 10.0.0 (`floor/r3a-di-<package>.txt`).
- `floor/r3a-adapters.sh` to `floor/r3a-adapters4.sh`: the xUnit v3, NUnit, MSTest, xUnit v2 and TUnit adapters, each
  test project taking its framework at the release probed (`-p:XunitV3Floor` and the like). The later scripts are the
  reruns, each header saying what the run before found: F29 and F31 in the harness, the C# 14 ambiguity in
  `TagMappingTests`, and the TUnit adapter not building on the newest TUnit.Core, which was F30. `floor/r3a-tunit2.sh`
  is the TUnit search on 5.27.8's adapter (`floor/r3a-tunit.txt`), each probe built and run on one release, which
  reported 1.0.0; `floor/r3a-tunit3.sh` is the search again with the adapter built on each release and the TUnit and
  LightBDD TUnit suites on their own newest references (`--no-floor-run`), which found F35 and the floor, 1.40.0
  (`floor/r3a-tunit3.txt`). `probe-r3a/tunit-engine-floor.txt` is TUnit's own engine on a `[HeadIn]` test through the
  adapter built on the floor, on the floor and on later releases.
- `floor/r3a-bdd.sh`: the LightBDD families with `floor/family_floor.py`, which moves a series in every packable project
  that references it (LightBDD.Framework in the core of the three runners, each runner's package in its adapter), on
  each runner's suite and on the LightBDD xUnit v2 example; xunit under the LightBDD xUnit v2 adapter; and BDDfy and
  Reqnroll through the core suite's facts for each (`floor/r3a-lightbdd-*.txt`, `floor/r3a-bddfy.txt`,
  `floor/r3a-reqnroll.txt`). `floor/r3a-bddfy2.sh` is the BDDfy search over the listed releases, and
  `floor/r3a-bddfy-f32.sh` the same after F32, with the core suite's run on the unlisted 8.0.12 its floating `8.*`
  takes. `probe-r3a/bddfy-humanize/` is what the adapter built on 8.0.1.3 reads on each release, with a humaniser set
  before its first read or after it (`results.txt`, F32): on 8.0.11 and 8.0.12 a copy of the one set at the first read.
  `probe-r3a/f36/` is F36: `over-the-newest.sh` runs the MSTest and Playwright floors over the normal run's output and
  from an empty `bin/` (in the adapters' worktree, whose `obj/` came from the floor's own restore), and
  `playwright-obj.sh` the Playwright floor's build after each of five starts, of which only an empty `bin/` on the
  newest's `obj/` leaves no `playwright.ps1`.
- `probe-f37/` is F37: `search.py` binary-searches the stable 1.x releases of Microsoft.Playwright for the oldest whose
  driver starts on Linux, building `probe/` for each in the .NET 10 SDK container of the podman VM (`results.txt`), and
  `suite.sh` runs the Playwright suite there as CI's floor run takes it, on the releases given (`suite.txt`: 1.12.0 and
  1.12.1; on 1.12.1 the package fact fails, as the probe moves the pin and not the ledger).
- `floor/r3a-playwright.sh`: Microsoft.Playwright, each probe installing the Chromium of the release it built into a
  browsers folder of its own (`--setup`), never the shared cache (`floor/r3a-playwright.txt`).
- `floor/floor_probe2.py`, `floor/paired_floor.py` and `floor/family_floor.py` gained, in R3a: a test run that finds no
  test fails (F29); each probe builds from an empty `bin/` and `obj/` (F31); a probe fails when a series package
  resolves to another release than the one probed ("NOT ON x"); `--filter`, to leave out the package facts, which
  hold the nuspec to the ledger and so fail every probe of another release (F23); and in `floor_probe2.py`, `--to`, to
  search from below a release that cannot build, `--setup`, a command each probe runs after its build, through
  `sh`, as Python's bare `bash` on Windows is WSL's, and `--no-floor-run`, which builds the test projects without the
  switch, so each runs on its own newest reference against the package built on the release probed (F35).

## prototype/: R1, applied and reverted

`r1.patch` is R1's steps 1 to 4 (the pin, the test project's direct reference under `$(FloorRun)`, and
`BigQueryPackageTests`), applied to the worktree at `1ee6373a` and reverted after the runs in `runs.txt`: the package
test red on today's tree, then the suite on 3.12.0 and on 3.0.0 with `-p:FloorRun=true`.

## nuget/

- `floor-history.py` reads the client floor out of every published Kronikol.Extensions.BigQuery nuspec
  (`bq-ext-versions.txt`, 292 versions). `floor-history.txt`: 3.11.0 from 3.0.0, 3.12.0 from 3.0.28.
- `floors-5120.py` reads, for every floating reference in a packable project (`floating-refs.txt`), the floor 5.12.0
  asks for, when that version was published, and the client's newest. `floors-5120.txt`.
- `fixed-pins.py` does the same for every fixed reference in a packable project, and reads whether the published
  5.14.0 nuspec hands it to consumers (the command is in its header). `fixed-pins.txt`: 35 of 39 do.

## drafts/

Text for the owner to decide on: the wiki section, the reply on #205, two upstream reports for the emulator, and the
R1 changelog entry.

## R3b, as released

- `floor/r3b-search-aws.sh`, `floor/r3b-search-data.sh` and `floor/r3b-search-bus.sh`: the fourteen searches, in
  three worktrees at once, one per script (`floor/r3b-<name>.txt`). The bus script starts a ClickHouse 25.8 server
  in the podman VM for the ClickHouse.Driver search (`floor/r3b-clickhouse-server.txt`), and the ClickHouse suite
  took its clients under the switch for the searches (F38).
- `floor/r3b_aws_core.py`: each AWS client's first release whose `AWSSDK.Core` dependency is past GHSA-9cvc-h2w8-phrp,
  read from NuGet's registration metadata (`floor/r3b-aws-core.txt`), which sets the five AWS floors.
- `floor/r3b-suites.sh`: each R3b suite on the newest and on its floor, the floor run from an empty `bin/` and
  `obj/` (`floor/r3b-suites.txt`), the ClickHouse one against a server in the podman VM.

## R3c, as released

- `floor/r3c-search.sh`: the five IKVM references, one release line, searched with `paired_floor.py`
  (`floor/r3c-ikvm.txt`). An IKVM release takes about 18 GB here, so the search restores into a NuGet cache of its own,
  `--after` removes each probe's release from it, and `--from` resumed it past the first run's 8.7.6, after that run's
  three releases filled the disk. NuGet's HTTP cache keeps each release too, whatever `NUGET_PACKAGES` says; the session
  removed each finished probe's from it as the search ran.
- `floor/r3c-ikvm2.sh`: the search again, each probe building the package on the release and running the suite on the
  newest 8.x (`paired_floor.py --no-floor-run`, `floor/r3c-ikvm2.txt`), from 8.9.1 on, into the shared NuGet cache, each
  release but 8.15.0 and 8.16.3 removed after its probe. It passed from 8.15.0 on, but what it measured was F39, the
  package's IKVM image in the suite's output, not the package.
- `floor/r3c-suites.sh`: the suite on its floor and on the newest, each from an empty bin/ and obj/ of the package and
  the suite, each run's HTTP cache copy removed after it (`floor/r3c-suites.txt`: before the fixes, after F39's first
  fix, and after the fix as released).
- `floor/r3c-linux.sh`: the suite on Linux both ways, in the dotnet SDK 10 container with Graphviz, as CI's IKVM job
  runs it (`floor/r3c-linux.txt`). The tree and the NuGet packages folder were the Windows side's, mounted, as the
  podman VM's disk does not shrink after an IKVM release lands in it.
- `probe-r3c/`: `mixing.txt`, which release's files the suite's output held (F39); `bind/`, the file-based program that
  found it, the package built on 8.9.0 and on 8.15.0 and run on 8.16.3; `package_sizes.py` and `package-sizes.txt`,
  every published package's size (F40); `consumer/`, a project that takes the package from a feed, run on 5.27.12 from
  nuget.org and on the package packed from the release commit, alone and with IKVM raised to 8.16.3 (`run-*.txt`). The
  consumer ran from a folder outside the repository, the packed packages in `feed/` beside it, so the repository's build
  props did not apply.
- `paired_floor.py` gained `--after`, `--from` and `--no-floor-run` (the last as `floor_probe2.py`'s, F35), and empties
  the package's own bin/ and obj/ before each probe as well as the suite's.
