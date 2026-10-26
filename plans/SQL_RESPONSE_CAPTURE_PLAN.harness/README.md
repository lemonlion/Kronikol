# Harness for SQL_RESPONSE_CAPTURE_PLAN.md (#193, #197)

Everything here was run on Windows 11 with the .NET 10.0.300 SDK, on 2026-10-26, against `c37042e2` (5.10.1).

## repro/

A console that runs each case twice, without and with `SqlTrackingInterceptor`, and prints a table. It references the
repository's own projects (`../../../src/...`), so it measures whatever the working tree holds: apply
`../prototype.patch` to measure the prototype, and revert it afterwards.

```
dotnet run -c Release --project repro -- pg mssql sqlite dapper   # no argument runs pg, mssql, sqlite and dapper
dotnet run -c Release --project repro -- ado                      # two result sets through TrackingNpgsqlConnection
dotnet run -c Release --project repro -- uri                      # which data sources make 5.10.1's address throw
```

- `pg`: PostgreSQL 17 on `localhost:55432` (user `postgres`, password `k193pass`). `pg-up.sh` starts it in podman:
  `MSYS_NO_PATHCONV=1 wsl -d podman-machine-default -u root -- sh /mnt/c/<path>/repro/pg-up.sh`, then
  `podman rm -f k193-pg` when done.
- `mssql`: SQL Server LocalDB, `(localdb)\MSSQLLocalDB`. On 5.10.1 every case fails with `UriFormatException` (F6).
  `with-uri-bypass.sh mssql` patches that one line for the run and reverts it after, which is how
  `results/5.10.1-mssql-localdb-uri-bypassed.txt` was taken. A SQL Server container in podman never answered TDS
  pre-login on this machine, so it was not used.
- `sqlite`: a file next to the binary.
- `dapper`: Dapper `Query` and `QueryAsync` through `TrackingSqlConnection` (LocalDB) and `TrackingNpgsqlConnection`.
- `K193_STACK=1` prints each failure's stack, `K193_CASE=<text>` runs only the cases whose name contains it, and
  `K193_DETAIL=RowCountOnly` sets the interceptor's `ResponseDetail` (that is how F5's masking was seen).
- Do not leave `.cs` copies inside `repro/`: the SDK compiles every `*.cs` under the project, a failed build is easy
  to miss with `--no-build`, and the old binary then runs.

## results/

| File | What |
|---|---|
| `5.10.1-first-run-with-localdb.txt` | the first full run: PostgreSQL, LocalDB (all F6), SQLite, Dapper |
| `5.10.1-pg-sqlite-dapper.txt` | the same after the update case was changed to read only the id, so it measures SaveChanges alone |
| `5.10.1-mssql-localdb-uri-bypassed.txt` | SQL Server with F6 bypassed: DateOnly, TimeOnly, geography (and the SaveChanges notes `[{"Id":3}]`, batch `_Position` rows) |
| `5.10.1-geography-close-stack.txt` | F5: `SqlNullValueException` from `SqlRowFormatter.Format` inside `RelationalDataReader.DisposeAsync` |
| `5.10.1-notes-pg-sqlite.txt` | 5.10.1's notes for the queries that work, to compare with the prototype's (byte-identical) |
| `uri-probe.txt` | what each data source and database name does to 5.10.1's address |
| `prototype-all.txt` | the prototype: 49 of 49 cases, its notes, and the ADO.NET two-result-set run |
| `prototype-endpoint-table.txt` | the prototype's address helper over the same data sources (three rows wrong, see plan 4.1 f) |

## ilscan/

A Mono.Cecil scan of the EF Core provider packages for everything that depends on a provider reader's concrete type:
`castclass`/`isinst` to a `DbDataReader` subclass, `ldtoken` of one (the `GetDataReaderMethod()` pattern), and calls to
members declared on one. `IlScan.Program.cs` and `IlScan.csproj` are the scanner. Restore the provider packages into
`pkgs/<name>/` (one restore-only project per provider version), then:

```
dotnet run --project ilscan/IlScan.csproj -- pkgs --out out
```

`summary-1-reader-dependencies.txt` is the first section of its report. The full report (2 MB) and the hit list are
not kept, since the scan writes them again. `livetest-ms/` checks SQL Server's spatial and HierarchyId mappings with
no database: the connection's open is suppressed and rows come from a fake reader behind a forwarding decorator. A
non-NULL geography throws the cast, a NULL one and a hierarchyid do not. Versions scanned: Npgsql EF Core 8.0.11,
9.0.4, 10.0.1, 10.0.3 (with NetTopologySuite and NodaTime); EF Core SqlServer, Sqlite and Relational 8.0.33, 9.0.22,
10.0.14 (with NetTopologySuite and HierarchyId); Pomelo 9.0.0; MySql.EntityFrameworkCore 10.0.12; Oracle 10.23.26400;
Firebird 13.0.0; Spanner 4.0.0 and 4.1.1.

## prototype.patch

The design of plan section 4, applied to `c37042e2` (`git apply --check` passes there), with the four differences the
plan's header lists. All 566 existing facts of the eight SQL test projects pass on it. It is a measurement, not the
implementation: R1 is to be written test-first from plan section 6.
