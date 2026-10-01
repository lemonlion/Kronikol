# Harness for `CLICKHOUSE_DRIVER_CLIENT_PLAN.md` (#126)

Measured on 2026-10-01 at Kronikol 4.4.0 (`a56c38e9`). The probe uses the published packages
`Kronikol.Extensions.ClickHouse.Driver` 4.4.0 and `ClickHouse.Driver` 1.5.0, and runs against ClickHouse
25.8.33.6 (`clickhouse/clickhouse-server:25.8-alpine`). Its output is in `probe-results.txt`.

| File | What it is |
|---|---|
| `start-ch.sh` | Starts the server in podman on port 8123 and waits until `SELECT version()` answers. The 25.8 image needs a password, so the script sets `CLICKHOUSE_PASSWORD=probe126` |
| `probe/Probe.csproj`, `probe/Program.cs` | The probe: a console app with no test framework. `CurrentTestInfoFetcher` makes the tracker log, and the calls are read from `RequestResponseLogger.RequestAndResponseLogs` |
| `probe-results.txt` | The output of the run the plan cites. The query ids in the HTTP lines change on every run |

What the probe does, in order:

1. Runs the issue's sequence. A tracked connection runs `CREATE TABLE orders`. The data source's client
   inserts two rows with `InsertBinaryAsync` and deletes one with `ExecuteNonQueryAsync`. The tracked
   connection then counts what is left. Plan §1, claims C1 and C5.
2. Records what the client's `ExecuteNonQueryAsync` returns for a two-row `INSERT ... VALUES` (F3).
3. Inserts three rows through an iterator that reuses one `object[]`, then reads the ids back (F4).
4. Puts Kronikol's `TestTrackingMessageHandler` into the data source's `HttpClient`, the workaround that
   works today, and records what it captures (F6).
5. Sends a statement the server rejects through the tracked ADO.NET connection, and counts the requests
   and responses it records (F10).

To re-run it on this machine, start the server from Git Bash:

```sh
MSYS_NO_PATHCONV=1 wsl -d podman-machine-default -u root -- sh /mnt/c/Code/Kronikol/plans/CLICKHOUSE_DRIVER_CLIENT_PLAN.harness/start-ch.sh
```

Then run `dotnet run -c Release` in `probe/`. To remove the server afterwards, run
`wsl -d podman-machine-default -u root -- podman rm -f ch126`.

On another machine, any ClickHouse 25.8 server works. Point the connection string at the top of
`Program.cs` at it.
