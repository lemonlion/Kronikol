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
