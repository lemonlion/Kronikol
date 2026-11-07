# FAILED_CALLS_PLAN harness

What [`FAILED_CALLS_PLAN.md`](../FAILED_CALLS_PLAN.md) measured, and how to measure it again. Written 2026-11-07 at 5.31.1
(`896ddad3`).

| Path | What it is |
|---|---|
| `probes/failed-calls.cs` | A .NET 10 file-based app. It drives each shipped tracker over an inner client that throws, on the published 5.31.1 packages, and prints for each case the records written (type, label, kind, status, note, `Error`), what the caller saw (the exception's type, whether it is the object the inner threw, whether its stack still names the inner frame), the history fingerprint line and the diagram's arrows. It started as `CALLS_AND_EVENTS_PLAN.harness/probes/failed-sends.cs`, repinned. |
| `results/failed-calls-5.31.1.txt` | Its output, the source of the plan's RUN rows. |
| `probes/more-failed-calls.cs` | The cases the first probe left to reading: `DeferredLogFlushHandler` over a send that throws, then one that does not, under another test (F28); EF Core with the token cancelled once the request is logged (F8); a Sqlite reader whose second row fails (F10); a `TrackingProxy` stream that throws after one item (F24). On the published 5.31.1 packages, with `Microsoft.EntityFrameworkCore.Sqlite` 10.0.11. |
| `results/more-failed-calls-5.31.1.txt` | Its output. |
| `probes/probe-on-build.py` | Makes a copy of a probe that runs on a worktree's build: `#:project` references in place of the 5.31.1 packages, and each fingerprint line printed under rules 11 and 12 (the rule-numbered reading is internal, so the copy reaches it by reflection). |
| `probes/compare-lines.py` | Compares each case's 5.31.1 line with the line a build makes under rule 11, and prints what rule 12 reads. |
| `results/failed-calls-232.txt`, `results/more-failed-calls-232.txt` | The probes' output on 5.34.3's build, before its tag; the first lines name the build (5.32.0's base before the rebase onto 5.34.0), as project references are no packages. |
| `results/across-the-fix-232.txt` | The comparison: 45 cases, none whose rule-11 line moved. |
| `notes/test-fixtures.md` | Where each tracker's tests already build it over a fake inner, and which tests already drive a failing call: the starting point for each contract fact (§6.2). |

## Running it

From this directory, with the .NET 10 SDK and network access to nuget.org (the first run restores the packages):

```bash
dotnet run --file probes/failed-calls.cs > results/failed-calls-5.31.1.txt 2>&1
```

It opens no port. Its only connection is to `127.0.0.1:1`, which refuses, for the Elasticsearch case.

## The run of 2026-11-07

- Exit 0, 525 lines. Every case compiled and ran, and none was skipped. The file's first lines print what NuGet resolved, read
  from the app's `deps.json`: all 20 Kronikol packages at 5.31.1 (`db26e5cb`), `AWSSDK.Core` 4.0.3.3.
- `Grpc.Core.Api` resolved to 2.71.0, not the gRPC package's floor of 2.66.0. `Google.Cloud.PubSub.V1` 3.35.0 needs
  `Google.Api.Gax.Grpc` 4.13.1, which requires 2.71.0 or later, so a lower pin would be a downgrade error.
- `Azure.Storage.Queues` is at its `src` floor, 12.11.0, where `failed-sends.cs` pinned 12.28.1. The Storage Queues case reads as
  it did in `failed-sends-5.28.2.txt`.
- Elastic 9.0.0's response base type is `Elastic.Transport.Products.Elasticsearch.ElasticsearchResponse`.
- Cases beyond the plan's first list: BigQuery under phase verbosities (both records carry variants, where Cosmos DB's answer
  has none), Kafka's consumer under `TraceMessageProcessing`, a `Task<T>` method that throws before it has a task,
  `ReplaceWithTracked` async, and a gRPC continuation that throws as the call starts.

## The second probe's run, 2026-11-07

`dotnet run --file probes/more-failed-calls.cs > results/more-failed-calls-5.31.1.txt 2>&1`, exit 0, every case as the plan reads
the source:

- F28: test A's send threw with one call queued, and test A got no record. Test B's send then wrote test A's call and its own,
  four records, all under test B.
- F8: EF Core called `CommandCanceledAsync`, not `CommandFailedAsync`, and its caller caught `OperationCanceledException`. Only the
  request was written.
- F10: the caller caught the second row's `SqliteException` (`integer overflow`), and the answer was `OK` with the first row.
- F24: the stream was answered `OK` as it was returned, its note the iterator's type name, and nothing recorded its throw.

The output's first lines are NuGet's `NU1903` warnings for `Microsoft.DotNet.ILCompiler` 10.0.8, which the SDK pulls in for any
file-based app. They are not the probe's own packages.

## Reading it against a build

The probe pins `#:package` versions. To run it against a worktree's build, pack the changed packages into a local feed and
change the pins. That is how #220's `failed-sends.cs` ran against its prototype (`CALLS_AND_EVENTS_PLAN.harness/probes/on-prototype.sh`).

## Across the fix, 2026-11-07 (5.34.3, #232's R1 and R2)

```bash
python probes/probe-on-build.py probes/failed-calls.cs <scratch>/failed-calls.cs <worktree>
dotnet run --file <scratch>/failed-calls.cs > results/failed-calls-232.txt 2>&1
python probes/compare-lines.py results/failed-calls-5.31.1.txt results/failed-calls-232.txt > results/across-the-fix-232.txt
```

Exit 0. Every one of the 45 cases reads under rule 11 exactly as the published 5.31.1 recorded it, so a suite that upgrades
reads no changed call where #232 answers one. Rule 12 reads the answers the plan's §5.3 names: `!` and the type for every call
that had no answer, for the brokers' failures and for the made-up `500`s; the calls whose status did not move read the same
under both rules. The build warnings, which name local paths, are left out of the saved output.
