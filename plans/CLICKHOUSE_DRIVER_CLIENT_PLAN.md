# ClickHouse.Driver client plan: #126

**Written:** 2026-10-01, at 4.4.0 (`a56c38e9`), the day #126 was filed. **Status: green-lit 2026-10-01**
(`ROADMAP.md` D32, row 1.16): the owner asked for it to be implemented in full, which takes §9's recommendations for Q1
to Q9, so Q4, Q8 and Q9 stay out of it. Being executed, R1 first (§11). Evidence labels: **RUN** (measured here, against
ClickHouse 25.8.33.6), **READ** (in the source, `file:line`; Kronikol at `a56c38e9`, ClickHouse.Driver at its `1.5.0` tag,
`95e38785`, the commit the issue links), **INFERRED** (reasoned from facts, stated by none), **DOC** (the driver's own
documentation), **ISSUE** (taken from #126, not re-measured). The probe behind every RUN line is in
[`CLICKHOUSE_DRIVER_CLIENT_PLAN.harness/`](CLICKHOUSE_DRIVER_CLIENT_PLAN.harness/README.md), with its output.

#126 asks for calls made through ClickHouse.Driver's `IClickHouseClient` to be recorded the way calls through a tracked
connection are, with `InsertBinaryAsync` drawn as one `INSERT INTO <table>` call showing its rows. The ask is right and
mostly buildable. This plan checks each claim (§1), lists what the issue does not say (§3), and designs the tracked client
around four limits it does not mention:

- **The driver's own recommended DI shape is out of reach.** Its examples register the concrete, sealed `ClickHouseClient`,
  which no decorator can stand in for (F1).
- **A data source's connections cannot come back tracked.** The second half of the ask cannot be built (F2).
- **The client reports no row counts for writes.** `ExecuteNonQueryAsync` returns 0 for an `INSERT` or a `DELETE`, and the
  client exposes no statistics, so recording that number would print a false "0 rows affected" (F3).
- **The rows have to be read after the call.** The driver keeps references to the caller's rows and reads them at send
  time, so a record taken as the rows go by would show values that were never written (F4).

It also found a defect in what ships today. A statement the server rejects, sent through the tracked ADO.NET connection,
records a request and no response, so the failure never appears in the report (F10, RUN). That is a patch of its own,
ahead of the minor.

## 0. Summary

| | |
|---|---|
| What ships | **R1, a patch:** a failed statement through `TrackingClickHouseCommand` is drawn with its error (F10). **R2, a minor:** `TrackingClickHouseClient`, a `WithClickHouseDriverTestTracking(IClickHouseClient)` overload, a data-source decorator, and `AddClickHouseDriverTestTracking` decorating `IClickHouseClient` and `IClickHouseDataSource` registrations |
| What it records | Every SQL call through the client's interface. `InsertBinaryAsync` (both overloads) is one call with its statement, its rows formatted the way a tracked reader formats rows, and the count it returns |
| What it cannot record | Code that holds the concrete `ClickHouseClient` or `ClickHouseDataSource`, connections handed out by a data source or client, `ClickHouseBulkCopy`, the experimental TCP client, the rows a client `ExecuteReaderAsync` returns, and a row count for the client's `ExecuteNonQueryAsync`. The wiki says so, each with its way round where there is one (§4.6) |
| What it needs first | A real-server test lane (S1): the facts that matter (single pass, references, batches, failures) cannot be faked faithfully, and today nothing in the repo executes a ClickHouse call (F11) |
| Bumps | R1 patch, R2 minor (§6.2). No new option in either |
| Open | Q1 to Q9 (§9). The one that shapes the work is Q1: decorator now, the HTTP handler only if someone needs the concrete shapes |

## 1. How far each claim was checked

| # | The issue says | Level | Verdict |
|---|---|---|---|
| C1 | Kronikol records ClickHouse.Driver traffic through ADO.NET only | READ, RUN | **True.** `src/` takes a `DbConnection` or a `ClickHouseConnection` and nothing else. The probe's issue sequence recorded `CREATE TABLE orders` and `SELECT FROM orders` and nothing between them |
| C2 | `WithClickHouseDriverTestTracking` wraps a `ClickHouseConnection`; `AddClickHouseTestTracking` decorates registered `DbConnection`s | READ | **True.** `ClickHouseDriverTrackingExtensions.cs:18-25`, `ClickHouseServiceCollectionExtensions.cs:22-37` (`DecorateAll<DbConnection>`, matched by the type name `ClickHouseConnection`) |
| C3 | The client a data source's `GetClient()` returns sends its calls straight to the server | READ | **True.** `ClickHouseDataSource.GetClient()` returns its own `ClickHouseClient` (`ADO/ClickHouseDataSource.cs:153`), whose methods send through its `HttpClient`. A connection's commands go through the same client object, via the internal `PostSqlQueryAsync` (`ADO/ClickHouseCommand.cs:233`), so a decorator on the interface never sees a command and nothing is recorded twice |
| C4 | `InsertBinaryAsync` is the client's bulk insert | READ, DOC | **True, and understated.** `ClickHouseBulkCopy` is `[Obsolete]` and points at it (`Copy/ClickHouseBulkCopy.cs:20`; `docs/http.mdx:24`), so it is the driver's only supported bulk path |
| C5 | The repro: two rows inserted, one deleted, `count()` is 1, two calls recorded | RUN | **Reproduced verbatim** on 4.4.0 with ClickHouse.Driver 1.5.0 against 25.8.33.6 (`probe-results.txt`, lines 1 to 4) |
| C6 | The driver's README lists `ClickHouseClient` beside ADO.NET as the HTTP client's main API | DOC | **True** (`README.md:44-61`). The driver's docs go further: `ClickHouseClient` is "recommended", ADO.NET "required for ORM integration" (`docs/http.mdx:22-24`) |
| C7 | Ask, first form: `WithClickHouseDriverTestTracking` could also wrap an `IClickHouseClient` | READ | **Buildable.** `IClickHouseClient` is a public interface with no default members (`IClickHouseClient.cs:17-233`). See F5 for what a hand-written implementation risks |
| C8 | Ask, second form: an `IClickHouseDataSource` whose `GetClient()` **and connections** come back tracked | READ | **Half buildable.** `GetClient()` can return a tracked client. The connections cannot: `IClickHouseDataSource` hands out `IClickHouseConnection`, whose only member returns the concrete `ClickHouseCommand`, bound to a concrete `ClickHouseConnection` (F2) |
| C9 | `InsertBinaryAsync` as one call named `INSERT INTO <table>`, with the count it returns and its columns and rows as a readable body | READ, RUN | **Buildable, with two corrections.** On the wire it is a schema probe plus one POST per batch (RUN: three requests for two client calls under the HTTP handler). One call is the right record. The count it returns is the client's own sum of batch sizes (`ClickHouseClient.cs:537`, `:861-862`), rows sent rather than the server's `written_rows`. And the rows must be formatted after the call, not as they are read (F4) |
| C10 | Implied: "as calls through a tracked connection are recorded" | READ, RUN | **Not fully.** A tracked connection shows a row count for writes and the rows a reader returns. Through the client, the first is unavailable (F3) and so is the second for `ExecuteReaderAsync` (F5b) |

## 2. Where it stands today

The pairing package `Kronikol.Extensions.ClickHouse.Driver` (since 3.0.75) adds two things to the driver-agnostic
`Kronikol.Extensions.ClickHouse`. The first is an adapter that reads `ClickHouseCommand.QueryStats` for true `INSERT` counts
(`ClickHouseDriverAdapter.cs:17-25`). The second is two typed helpers. Everything rests on `TrackingClickHouseConnection`, a
`DbConnection` decorator. The package references ClickHouse.Driver `1.4.0` as a minimum
(`Kronikol.Extensions.ClickHouse.Driver.csproj:13`). `IClickHouseClient` has existed since the driver's 1.0.0 and is
identical in 1.4.0 and 1.5.0 (READ, `git log` of `IClickHouseClient.cs`), so no dependency floor moves.

What a consumer's code gets, by what it holds:

| The code holds | Today | After R2 |
|---|---|---|
| A `DbConnection` (wrapped, or registered and decorated) | tracked | tracked, unchanged |
| An `IClickHouseClient` (wrapped, or registered as the interface) | **not tracked** | tracked |
| An `IClickHouseDataSource` from DI, then `GetClient()` | **not tracked** | tracked, through the data-source decorator (§4.5) |
| An `IClickHouseDataSource`, then `CreateConnection()` or `OpenConnection()` | not tracked | not tracked (F2) |
| The concrete `ClickHouseClient`, as the driver's examples register it | not tracked | **not tracked** (F1); Q1 |
| The concrete `ClickHouseDataSource` | not tracked | not tracked (F1) |
| `ClickHouseBulkCopy` (obsolete) | not tracked; the wiki says so (`Integration-ClickHouse-Extension.md:87`) | not tracked (F7) |
| The TCP client (`ClickHouse.Driver.Tcp`, experimental) | not tracked | not tracked (F8) |

Kronikol already uses three patterns for clients that are not connections (READ). Redis uses a `DispatchProxy`, where
members it does not know pass through (`RedisTrackingDatabase.cs:9`, `:33-34`). Kafka hand-writes the third-party
interface (`TrackingKafkaProducer.cs:11`), with no test that every member is covered. Cosmos, BigQuery and the AWS packages
put a `DelegatingHandler` into the SDK's `HttpClient` (`CosmosClientOptionsExtensions.cs:24`). Each is a candidate here (§4.7, Q1, Q2).

## 3. Findings the issue does not state

| # | Finding | Level | Where |
|---|---|---|---|
| F1 | **The driver's recommended DI shape cannot be decorated.** `ClickHouseClient` and `ClickHouseDataSource` are `sealed` (`ClickHouseClient.cs:51`, `ADO/ClickHouseDataSource.cs:11`). The driver's DI helper registers neither `IClickHouseClient` nor `ClickHouseClient` (`DependencyInjection/ClickHouseServiceCollectionExtensions.cs:205-212`), and its own example registers the concrete type as "the recommended approach for new code" (`examples/Http/Core/Core_003_DependencyInjection.cs:69-71`). An interface decorator reaches only code that holds the interface. Code that holds the concrete type has to change before it can be tracked, or needs the HTTP handler (Q1) | READ, DOC | §4.6 |
| F2 | **A data source's or client's connections cannot be handed back tracked.** `IClickHouseConnection.CreateCommand` returns the concrete `ClickHouseCommand` (`IClickHouseConnection.cs:9-15`). Its constructor needs a concrete `ClickHouseConnection` (`ADO/ClickHouseCommand.cs:37`), its `DbConnection` setter casts to one (`:113`), and `ClickHouseConnection.CreateCommand(string)` is `new`, not virtual. `IClickHouseClient.CreateConnection()` returns the concrete `ClickHouseConnection`. A connection obtained either way stays untracked unless the caller wraps it as a `DbConnection`, as today | READ | §4.5 |
| F3 | **The client gives no row count for a write.** Its `ExecuteNonQueryAsync` reads a number from the response body (`ClickHouseClient.cs:258`), which is empty for writes. It returned 0 for a one-row `DELETE` and for a two-row `INSERT ... VALUES`. The true count is in `X-ClickHouse-Summary`, which the client parses into an internal `QueryResult` (`QueryResult.cs:9`) and exposes only on `ClickHouseCommand.QueryStats`. The driver's Activity gets the statistics only after it has stopped (`ActivitySourceHelper.cs:92` before `:64-75`), so a listener cannot read them either. Logging the return value would print "0 rows affected" for every write | RUN, READ | §4.2 |
| F4 | **The driver keeps references to the caller's rows and reads them at send time.** Batching stores each `object[]` reference in a pooled array (`Utility/EnumerableExtensions.cs:33`) and serializes later (`Copy/BatchSerializer.cs:51-56`). The source is enumerated once, lazily, only after the schema probe. An iterator that reuses one `object[]` for three rows stored ids `12,12,12`. A tracker that copied values as each row went by would record `10,11,12`, rows that were never written. It must hold references and format them after the call, and never enumerate the rows itself | RUN, READ | §4.3 |
| F5 | **A hand-written `IClickHouseClient` breaks when the driver adds a member.** Members were added without default implementations in 1.2.0 (#273, `InsertBinaryAsync<T>`, `RegisterBinaryInsertType<T>`) and 1.3.0 (#318, `RegisterPocoType<T>`, `QueryAsync<T>`). A class compiled against 1.4.0 throws `TypeLoadException` on a later driver that adds one. Kronikol's test project floats `ClickHouse.Driver` `1.*` (restores 1.5.0), so CI meets a new member on its first run after the driver ships one, but consumers who upgrade first meet it too | READ | §4.7, Q2 |
| F5b | **The rows of a client `ExecuteReaderAsync` cannot be captured.** It returns the concrete `ClickHouseDataReader`, whose only constructor is private (`ADO/Readers/ClickHouseDataReader.cs:54`), so no tracking reader can be returned in its place. `QueryAsync<T>` (an `IAsyncEnumerable<T>`) and `ExecuteScalarAsync` can be captured | READ | §4.2 |
| F6 | **A workaround works today, and shows why it is not the answer.** Kronikol's `TestTrackingMessageHandler` inside the data source's `HttpClient` recorded three calls for one insert and one delete. One was the driver's schema probe (``SELECT `id`,`item` FROM orders WHERE 1=0``), and the insert's body read `[binary content]`. The participant was `localhost:8123/`, with no dependency category, so it was drawn as an HTTP API. A ClickHouse-aware handler could fix the labels, drop the probe and read `X-ClickHouse-Summary` for every count, and it reaches every API and every concrete type. The insert's rows would stay unreadable: the body is zstd-compressed RowBinary | RUN, READ | Q1 |
| F7 | **`ClickHouseBulkCopy` is obsolete and stays untracked.** It calls the client's internal `InsertBinaryAsync` overload (`Copy/ClickHouseBulkCopy.cs:137-147`, `ClickHouseClient.cs:822`), which no interface decorator sees, and it takes the concrete connection. Only the HTTP handler would reach it | READ | §4.6 |
| F8 | **The ClickHouse.Driver package has carried a second, native-TCP client since 1.5.0.** It is packed into the same package, marked `[Experimental("CHTCP0001")]`, and has its own interfaces (`IClickHouseTcpClient`, `IClickHouseTcpOperations`) separate from `IClickHouseClient` (`docs/tcp.mdx:76`). It is not tracked either | READ, DOC | §8 |
| F9 | **A connection string that names no database is drawn as database `unknown`.** The URI read `clickhouse://localhost/unknown/orders`, where ClickHouse used `default` (`SqlDiagnosticTracker.cs:348` substitutes `unknown` for every SQL tracker). The client also takes a per-call database, `QueryOptions.Database`, which the tracked client must read before `Settings.Database` | RUN, READ | §4.2, Q8 |
| F10 | **A statement the server rejects leaves a request with no response, through the shipped ADO.NET wrapper.** `SELECT * FROM no_such_table_126` threw `ClickHouseServerException`, and the tracker recorded 1 request and 0 responses. `TrackingClickHouseCommand` has no `catch` around the inner call (`TrackingClickHouseCommand.cs:72-113`). `DoLogResponse` takes an `Exception` (`TrackingClickHouseConnection.cs:106-110`), but no caller passes one. ROADMAP 1.15's list of lone requests names only ten SDK HTTP handlers (`INGEST_FIDELITY_PLAN.md:561`) | RUN, READ | R1 |
| F11 | **Nothing in the repo executes a ClickHouse call.** `Kronikol.Tests.ClickHouse` is fakes only. The real driver types are built and never opened (`ClickHouseClientIntegrationTests.cs:272-273`), and `QueryStats` is built by hand (`:249`). F3 and F4 are driver behaviour that a fake would have to imitate. A fake that got them wrong would pass | READ | S1 |
| F12 | **Disposing a tracked client disposes the data source's.** `GetClient()` returns the data source's own client. A decorator that forwards `Dispose`, as `TrackingClickHouseConnection` does, would close it for every later caller if a test wrote `using var client = ds.GetClient().WithClickHouseDriverTestTracking()`. That is the same outcome as disposing the client without Kronikol, but the wiki's example must not invite it | INFERRED | §4.1 |

## 4. The design

### 4.1 The surface (R2)

All of it lives in `Kronikol.Extensions.ClickHouse.Driver`, which already references the driver. No option is added.

| Member | What it does |
|---|---|
| `public sealed class TrackingClickHouseClient : IClickHouseClient, ITrackingComponent` | Records the calls in §4.2 and forwards everything to the inner client. Thread-safe like the client it wraps (the driver designs `ClickHouseClient` as a singleton): no per-call state on the instance, invocation count by `Interlocked` |
| `WithClickHouseDriverTestTracking(this IClickHouseClient client, ClickHouseTrackingOptions? options = null)` | An overload of the existing method, as the issue suggests. It returns `TrackingClickHouseClient`, as the connection overload returns `TrackingClickHouseConnection`. Wrapping an already tracked client returns it unchanged |
| `public sealed class TrackingClickHouseDataSource : IClickHouseDataSource` | `GetClient()` returns one tracked client per data source, created on first use. `ConnectionString` and the three connection members forward unchanged (F2) |
| `AddClickHouseDriverTestTracking` | Also decorates every `IClickHouseClient` and every `IClickHouseDataSource` registration, keyed ones included, sharing the options and the `IHttpContextAccessor` lookup the `DbConnection` decoration uses. Behaviour that changes without a code change; see Q6 |

The tracker is a new internal `SqlDiagnosticTracker` subclass in the `.Driver` assembly. `LogRequest` and `LogResponse` are
`protected internal` on a public abstract class (`SqlDiagnosticTracker.cs:14`, `:169`, `:230`, `:292`), so a subclass there
can call them. The existing `SqlDiagnosticTrackerForClickHouseWrapping` is internal to the core package, and widening it is
not needed.

`Dispose` forwards, as the connection wrapper's does. The wiki's example wraps a client the test owns, or one taken from a
data source without disposing it (F12).

### 4.2 What each member records

The data source in the URI is `Settings.Host`, which matches what a tracked connection draws. The database is
`options?.Database ?? Settings.Database` (F9). Labels, notes and URIs come from the shared tracker, so verbosity, phases,
`ExcludedOperations`, `LogSqlText` and `LogParameters` behave as on a connection.

| Member | Recorded as | Request note | Response |
|---|---|---|---|
| `ExecuteNonQueryAsync` | the classified SQL (e.g. `DELETE FROM orders` at Detailed) | the SQL; parameters at Raw with `LogParameters`, as on a connection | status only, **no count** (F3, Q5) |
| `ExecuteScalarAsync` | the classified SQL | the SQL | the value, by the connection's scalar rule (`TrackingClickHouseCommand.cs:178-185`) |
| `ExecuteReaderAsync` | the classified SQL | the SQL | status, logged when the reader is returned; no rows (F5b) |
| `QueryAsync<T>` | the classified SQL, logged when enumeration starts (nothing is sent before) | the SQL | the rows read, as JSON of `T`, capped like a reader's; logged when enumeration ends or is disposed. Never enumerated, nothing recorded |
| `ExecuteRawResultAsync` | the classified SQL | the SQL | status; the body belongs to the caller and is not read |
| `InsertBinaryAsync(table, columns, rows)` | `INSERT INTO {table}` | `INSERT INTO {table} ({columns}) FORMAT {Format}`, then the rows (§4.3) | `{n} rows affected`, n the count returned |
| `InsertBinaryAsync<T>(table, rows)` | `INSERT INTO {table}` | the statement, then the rows as JSON of `T` | `{n} rows affected` |
| `InsertRawStreamAsync` | `INSERT INTO {table}` | `INSERT INTO {table} ({columns}) FORMAT {format}`; the stream is never read (the driver owns and disposes it) | `written_rows` from the returned response's `X-ClickHouse-Summary` when present, else status only |
| `PostStreamAsync` (both) | the classified SQL, or `SQL` when the statement travels inside the stream | the SQL, or nothing | as `InsertRawStreamAsync` |
| `PingAsync` | not recorded, as a connection's `Open` is not | | |
| `Settings`, the four `Register*` members, `CreateConnection`, `Dispose` | not recorded; forwarded. `CreateConnection` returns the inner client's untracked connection (F2) | | |

Every recorded member that throws logs its response with the exception (status `Error`, the message as content) and rethrows
the same exception object. That is the path F10 shows the connection wrapper lacks.

### 4.3 The rows of an insert

- The tracked client passes the driver a wrapper around `rows` that yields each row unchanged. The wrapper keeps the
  references of the first `MaxResponseRows` rows and counts the rest. It never enumerates on its own, so a lazy or single-use
  source is pulled once, by the driver, exactly as without Kronikol (F4).
- The rows are formatted when the inner call returns or throws, from the references. Within a batch this reproduces what the
  driver serialized, including the reused-array case (RUN, `12,12,12`). A caller that reuses one array across batches gets
  corrupted rows from the driver, and the record shows the final values for all of them. Neither is what the caller meant,
  and the wiki says the driver does not support it (A5).
- Formatting uses the reader's existing rules, extracted from `TrackingDbDataReader` (its formatter is private instance code
  today, `TrackingDbDataReader.cs:111-159`) into an internal static helper that both use:
  - At FullRows the rows are a JSON array of objects keyed by column, with `... (N more rows not shown)` after the cap.
  - At RowCountAndColumns the content reads `2 rows [id, item]`.
  - `byte[]` values are shown as `[bytes: N]`.
  - A value longer than `MaxValueDisplayLength` is cut to that length, with its full length after it.
  - The renderer already lays out that shape as a JSON document plus a footer (`PlantUmlCreator.cs:1568`). With no
    `columns` given, the rows are arrays.
- `ResponseDetail`, `MaxResponseRows` and `MaxValueDisplayLength` govern the inserted rows as they govern returned ones (Q3).
  At Summarised the request has no note at all, by the shared rule (`SqlDiagnosticTracker.cs:367-376`).
- `columns` is passed through untouched. The tracker reads the names by enumerating it once more, which the driver itself
  already does several times (`Utility/SchemaResolver.cs:89-91`, `:109`, `:170-173`).
- The call's time runs from before the schema probe to after the last batch. Batches are not separate calls, whatever
  `BatchSize` and `MaxDegreeOfParallelism` say (defaults 100,000 and 1, `InsertOptions.cs:17`, `:22`).

### 4.4 Failures

- **The tracked client (R2):** §4.2's last paragraph.
- **The tracked connection (R1):** every `Execute*` override in `TrackingClickHouseCommand` gets the same `try`, log with the
  exception, `throw;`.
- **The other SQL wrappers:** Npgsql, MySqlConnector, Oracle, SqlClient, Sqlite and Spanner are, by the 3.0.74 record,
  copy-paste twins of the ClickHouse wrapper. R1 reads each one. Any that share the gap gets the same fix and its own fact in
  the same patch (Q7).

### 4.5 DI and the data source

- The driver's `AddClickHouseDataSource` registers `IClickHouseDataSource` (and `DbConnection`, already decorated today), so
  decorating `IClickHouseDataSource` is what makes the issue's own shape automatic: service code that receives the data
  source and calls `GetClient()`.
- The data-source decorator cannot track the connections it hands out (F2). Code that wants tracked connections takes
  `DbConnection`, which `AddClickHouseDriverTestTracking` already decorates, or wraps one by hand as today.
- `DbDataSource` is also registered by the driver and is an abstract class Kronikol could subclass to hand back tracked
  `DbConnection`s. Nobody has asked for that, so it is a question (Q4), not a slice.

### 4.6 What stays untracked, and what the wiki says about each

| Shape | What the wiki tells the reader |
|---|---|
| Concrete `ClickHouseClient` or `ClickHouseDataSource` in service code (F1) | Depend on `IClickHouseClient` or `IClickHouseDataSource` instead, which the driver supports, and the decoration is automatic. Or, in the test host, replace the registration with `new ClickHouseClient(settings).WithClickHouseDriverTestTracking()` registered as `IClickHouseClient`, which works only if the service takes the interface. If neither is possible, Q1's handler is the only route |
| Connections from `IClickHouseDataSource` or `IClickHouseClient.CreateConnection()` (F2) | Wrap them with the existing `WithClickHouseDriverTestTracking(ClickHouseConnection)` and use the result as a `DbConnection` |
| `ClickHouseBulkCopy` (F7) | Obsolete in the driver; use `InsertBinaryAsync`, which is tracked |
| A client `ExecuteReaderAsync`'s rows (F5b) | Use `QueryAsync<T>` or the ADO.NET path to see rows; the call and its status are recorded |
| Counts of a client `ExecuteNonQueryAsync` (F3) | Not available from the client; the ADO.NET path shows them |
| The TCP client (F8) | Not tracked; experimental in the driver |

### 4.7 When the driver adds an interface member

Recommended (Q2): a hand-written decorator, plus three things.

- A fact that lists every member of `IClickHouseClient` by reflection and names how each is handled (recorded or forwarded).
  A member the list does not name is red. The test project floats `1.*`, so a driver release that adds a member turns CI
  red before most consumers upgrade, and the fix is a patch.
- The supported range is written on the wiki page: 1.4.0 to the newest tested.
- The changelog of each such patch names the driver version it follows.

The alternative is a `DispatchProxy`, as Redis has. A new member would pass through untracked rather than fail to load, at
the cost of reflection dispatch and harder async plumbing (`Task<T>`, `IAsyncEnumerable<T>`). It is weighed in Q2.

## 5. Tests, red first

Every fact is red first. The real-server facts need Docker and skip without it, with the reason, as
`ContainerEndToEndTests.cs:53` does. Docker is a condition a machine without it cannot meet, so the skip falls under the
owner's rule, and CI runs them (A2).

| # | Fact | File | Red on | Slice |
|---|---|---|---|---|
| T1 | A statement the server rejects, through a tracked connection, records its request and an `Error` response carrying the server's message; the same exception reaches the caller | `Kronikol.Tests.ClickHouse/TrackingClickHouseCommandTests.cs` (fake throwing command) and the real-server class | 1 request, 0 responses (RUN) | S0 |
| T2 | The issue's sequence records four calls: `CREATE TABLE orders`, `INSERT INTO orders` with both rows and `2 rows affected`, `DELETE FROM orders` with no count, `SELECT FROM orders` | `Kronikol.Tests.ClickHouse/ClickHouseDriverClientRealServerTests.cs` (new) | two calls (RUN) | S2 |
| T3 | Rows from an iterator that reuses one `object[]` are recorded as the driver wrote them (`12,12,12` in one batch), and the iterator is pulled exactly once | same | no call; with values copied as rows go by, `10,11,12` | S2 |
| T4 | Twelve rows with `MaxResponseRows` 10 show ten and `... (2 more rows not shown)`; a 600-character value is cut at 500 with its length; a `byte[]` reads `[bytes: N]`; RowCountAndColumns reads `12 rows [id, item]` | `Kronikol.Tests.ClickHouse/TrackingClickHouseClientTests.cs` (new, fake inner client) | no call | S2 |
| T5 | `BatchSize` 1 and `MaxDegreeOfParallelism` 4 over ten rows is one call with `10 rows affected` | real-server class | no call | S2 |
| T6 | The client's `ExecuteNonQueryAsync` never shows `0 rows affected` for a write | both classes | the driver's 0 logged (mutation) | S2 |
| T7 | `ExecuteScalarAsync` shows its value; `QueryAsync<T>` shows the rows read when enumerated and records nothing when never enumerated; `ExecuteReaderAsync` records the call with no rows | both classes | no call | S2 |
| T8 | A client call the server rejects records an `Error` response and rethrows the same exception object | both classes | no call | S2 |
| T9 | Summarised draws `INSERT` with no request note; `ExcludedOperations` containing Insert records nothing; `TrackDuringSetup = false` records nothing during setup | `TrackingClickHouseClientTests.cs` | no call | S2 |
| T10 | `QueryOptions.Database` set on one call puts that database in its URI | `TrackingClickHouseClientTests.cs` | no call | S2 |
| T11 | `InsertRawStreamAsync` leaves the caller's stream unread by the tracker and shows `written_rows` from the response header | real-server class | no call | S2 |
| T12 | `AddClickHouseDriverTestTracking` decorates an `IClickHouseClient` and an `IClickHouseDataSource` registration (keyed too); `GetClient()` returns the same tracked client twice; a concrete `ClickHouseClient` registration is left alone | `ClickHouseClientIntegrationTests.cs` | undecorated | S3 |
| T13 | Every `IClickHouseClient` member is named in the handled list (§4.7) | `TrackingClickHouseClientTests.cs` | the list without one member (mutation) | S2 |
| T14 | The generated report draws the insert's note with its rows as JSON and the footer, using the emitted attributes, not a bare substring of the page | `Kronikol.Tests.ClickHouse` or the report fact class the SQL notes already use | no call | S2 |

Mutations to run once each, every one caught by the fact named:

| Mutation | Caught by |
|---|---|
| Copy row values as rows go by | T3 |
| Enumerate the rows in the tracker | T3's pull count |
| Drop the `catch` | T1, T8 |
| Log the driver's count for `ExecuteNonQueryAsync` | T6 |
| Ignore `QueryOptions.Database` | T10 |
| Record at the first `QueryAsync<T>` call instead of at enumeration | T7 |
| Remove a member from the handled list | T13 |

## 6. Slices, releases and records

### 6.1 Slices

| Slice | What | Bump |
|---|---|---|
| **S0** | F10: the `catch` in `TrackingClickHouseCommand`, the read of the other SQL wrappers, T1 (and a fact per twin that shares the gap) | patch |
| **S1** | The real-server lane: Testcontainers ClickHouse 25.8 in `Kronikol.Tests.ClickHouse`, its Docker skip, and a first fact that the probe's issue sequence runs there (T2 starts here, red) | none alone |
| **S2** | `TrackingClickHouseClient`, the overload, the row wrapper and the extracted formatter; T2 to T11, T13, T14 | minor |
| **S3** | `TrackingClickHouseDataSource` and the DI decoration; T12 | minor (with S2) |
| **S4** | Docs: the wiki page (§6.4), the doc comments that name only ClickHouse.Client and Octonica (`ClickHouseTrackingOptions.cs:8`, `:21`; `DependencyCategories.cs:39`; the core csproj's `Description`), the `.Driver` csproj's `Description`, the changelog | with R2 |

### 6.2 Releases

| Release | Contents | Bump | Why this part moved |
|---|---|---|---|
| **R1** | S0 | patch | A bug fix: nothing new to call. The changelog calls out the visible change: a failed statement now has a response arrow, status `Error` |
| **R2** | S1 to S4 | minor | New public types and an overload. `CLAUDE.md`: anything new for a consumer to call is a minor, and the highest-ranking change decides |

R1 can ship alone and first (rule 1). R2 does not wait for anything but the answers to Q1 to Q6.

### 6.3 Changelog drafts

> **Patch - a ClickHouse statement the server rejects is drawn with its error.** A failed `ExecuteNonQuery`,
> `ExecuteScalar` or `ExecuteReader` through `TrackingClickHouseConnection` recorded its request and no response, so the
> report showed a call that never answered. It now records an `Error` response with the server's message and rethrows the
> same exception. The patch part moved because nothing is new for a consumer to call.

> **Minor - calls through ClickHouse.Driver's `IClickHouseClient` are recorded (#126).** `InsertBinaryAsync` (both
> overloads) is one call, `INSERT INTO <table>`, with its rows formatted as a tracked reader formats rows and the count it
> returns. The SQL calls (`ExecuteNonQueryAsync`, `ExecuteScalarAsync`, `ExecuteReaderAsync`, `QueryAsync<T>`,
> `ExecuteRawResultAsync`) and the raw-stream inserts are recorded as a tracked connection records them. Two exceptions: the
> client reports no row count for writes, and the rows of an `ExecuteReaderAsync` cannot be captured.
> `AddClickHouseDriverTestTracking` now also decorates `IClickHouseClient` and `IClickHouseDataSource` registrations, so an
> app that already uses it gains arrows for the client calls it was missing. The minor part moved because
> `TrackingClickHouseClient`, `TrackingClickHouseDataSource` and a `WithClickHouseDriverTestTracking(IClickHouseClient)`
> overload are new public surface.

### 6.4 Wiki

| Where | Change | Release |
|---|---|---|
| `Integration-ClickHouse-Extension.md` line 3 | ClickHouse.Driver has two APIs, and both are tracked | R2 |
| same, a new section "ClickHouseClient (ClickHouse.Driver)" | wrapping, DI, the per-member table of §4.2, the rows rule, the supported driver range | R2 |
| same, a "What is not tracked" section | the table of §4.6, replacing line 87's single sentence | R2 |
| same, line 123 | says the Raw label is the full SQL text; the code draws the keyword (`TrackingClickHouseCommandTests.cs:308-315`). Corrected (§8) | R2 |
| same, "Response Payload Capture" | failed statements show their error | R1 |

### 6.5 Kronikol4J

- **R1** changes report output for a failed ClickHouse statement. That needs a ledger entry. Check whether
  Kronikol4J's JDBC `TrackingDataSource`, which its `ClickHouseTracking.wrap` uses, records a failed statement's response.
  If it does not, the entry says so.
- **R2** adds a .NET-only capture surface. Kronikol4J's ClickHouse module is JDBC only, so the Java client-v2 API is
  untracked there. That needs a ledger line, with no output change for existing inputs.

### 6.6 Before declaring done

- The real-server facts ran in CI, not skipped: read the job's log for them. Each mutation in §5 was caught. Every doc
  comment that names the ClickHouse drivers was updated.
- The issue's probe (harness) records four calls on the release's packages, re-run against a fresh server.
- **The consumer.** #126's reporter runs the release, since BreakfastProvider uses only ADO.NET (READ, on a local checkout last
  updated 2026-09-23: no client calls). The release is done when the reporter's suite draws the seeded rows.

## 7. Where it sits in the roadmap

Row **1.16** in stage 1, marked **minor** for R2, placed by **rule 1**. A shipped integration silently omits the calls of
the API its driver recommends, so a report says a test's setup wrote nothing when it wrote its rows. R1 is a live defect in
shipped code and goes first. **D32** is the green light and the answers to Q1 to Q6.

## 8. Found on the way, not in this plan

| What | Level | Where it goes |
|---|---|---|
| F9: a connection with no database is drawn as `unknown`, where ClickHouse uses `default` | RUN | Q8 |
| The wiki's Raw-label claim (line 123) | READ | S4, as a doc fix |
| The driver's `AddClickHouseDataSource` registers `DbConnection` and `IClickHouseConnection` with the data source's lifetime (singleton by default) while `ClickHouseConnection` is transient (`DependencyInjection/ClickHouseServiceCollectionExtensions.cs:211-212`). An app resolving `DbConnection` would share one connection. A driver defect, if real, and Kronikol decorates whatever it is given | READ, not run | An upstream report, the owner's call |
| The TCP client (F8) | READ | A separate issue once the driver drops `[Experimental]` (Q9) |
| #125 (its sibling) has no place in `ROADMAP.md` §7 either | READ | Placed when its plan is written |

## 9. Questions, with recommendations

| # | Question | Recommendation |
|---|---|---|
| Q1 | Mechanism: the interface decorator, a ClickHouse-aware HTTP handler, or both | **The decorator, now.** It is what the issue asks for, and the only way to readable rows. The handler (F6) reaches every API and every concrete type and gets true counts from `X-ClickHouse-Summary`, but its insert bodies stay binary. It would also record every ADO.NET command a second time unless the connection wrapper stood down. Build it only if a consumer's code holds the concrete types and cannot change |
| Q2 | Hand-written decorator or `DispatchProxy` (F5) | **Hand-written**, with T13 and the floating test reference. It is compile-checked and readable, and a new driver member is caught in CI, then shipped as a patch. A `DispatchProxy` never fails to load, but a new member would be silently untracked, which is the very failure #126 reports |
| Q3 | Inserted rows governed by the existing `ResponseDetail`, `MaxResponseRows` and `MaxValueDisplayLength`, or new request options | **The existing ones.** One rule for rows, whichever way they travel, and no new surface. The names say "response", so the doc comments and the wiki say they cover inserted rows too |
| Q4 | A `DbDataSource` decorator that hands back tracked `DbConnection`s | **Not in this plan.** Nobody has asked, and `DbConnection` decoration already covers code that takes connections from DI |
| Q5 | The client's `ExecuteNonQueryAsync`: no count, or the driver's number | **No count.** The driver's number is 0 for every write (RUN); a status with no count is true |
| Q6 | `AddClickHouseDriverTestTracking` decorating client and data-source registrations by default: existing apps gain arrows with no code change | **By default, called out in the changelog; a minor, not a major.** The method's name promises ClickHouse.Driver tracking. The missing arrows are the defect #126 reports, not a contract. The repo's rule for a fix that changes what is drawn is to call it out, not to inflate the bump. The alternative is a new opt-in method, which would leave every existing user with the gap |
| Q7 | R1's scope: ClickHouse only, or every SQL wrapper that shares F10 | **Every wrapper that shares it, in the same patch.** They are copy-paste twins (the 3.0.74 record). `CLAUDE.md` asks for every bug found to be fixed, and each gets its own fact |
| Q8 | F9: draw `default` for a ClickHouse connection with no database | **A separate small patch if wanted.** It changes the URI text in existing reports. It is not part of #126 |
| Q9 | The TCP client | **Not now.** It is experimental in the driver. Open an issue when it is not |

## 10. Assumption ledger

| # | Assumption | Level | What would change |
|---|---|---|---|
| A1 | The reporter's service reaches the client through an interface (the issue: "the `IClickHouseClient` a data source's `GetClient()` returns") | ISSUE | If it holds the concrete `ClickHouseDataSource`, R2 does not reach it without a code change, and Q1's handler moves up |
| A2 | The CI job that runs `Kronikol.Tests.ClickHouse` (the auto-discovered remainder) has Docker | INFERRED (GitHub's Ubuntu runners ship it; TcpTap's container facts use it) | If not, the lane needs a job of its own |
| A3 | `UnifiedSqlClassifier` labels `INSERT INTO orders (id, item) FORMAT RowBinary` as `INSERT INTO orders` | INFERRED | T2 proves it; if not, the tracked client passes the classifier `INSERT INTO {table}` alone |
| A4 | Later driver minors keep adding interface members | READ (two of five did) | Only how often T13 goes red |
| A5 | Formatting after the call equals what was written | RUN for one batch | Across batches, a reused array is corrupted by the driver itself; the record shows the final values (§4.3) |

## 11. Log

- **2026-10-01.** Written at 4.4.0 (`a56c38e9`). The probe (harness) ran against ClickHouse 25.8.33.6 in podman with
  `Kronikol.Extensions.ClickHouse.Driver` 4.4.0 and ClickHouse.Driver 1.5.0. Results:
  - The issue's sequence reproduced, two calls of four.
  - The client returned 0 for a `DELETE` and for a two-row `INSERT`.
  - A reused row array was stored as `12,12,12`.
  - The HTTP-handler workaround recorded three calls, one of them the schema probe, with the insert as `[binary content]`.
  - A rejected statement through the tracked connection recorded one request and no response.

  The driver was read at its `1.5.0` tag, the commit the issue links, and compared with `1.4.0`.
