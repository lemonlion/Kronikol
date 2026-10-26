# SQL response capture plan (#193, #197)

**Status:** written 2026-10-26 at 5.10.1 (`c37042e2`) by kronikol-d0, at the owner's ask to analyse #193 critically
and plan a fix for the underlying problem. A draft, **not green-lit**: nothing is implemented, and the questions in
section 9 are open. The prototype in the harness exists only to measure the design.

**Issues:** [#193](https://github.com/lemonlion/Kronikol/issues/193), "EF Core: `SqlTrackingInterceptor` hands
Npgsql's SaveChanges a `TrackingDbDataReader`, so every insert, update and delete that returns a row fails", and
[#197](https://github.com/lemonlion/Kronikol/issues/197), "`TrackingDbDataReader` leaves `GetFieldValue<T>` and its kin
to `DbDataReader`'s defaults", filed the same day from the same integration. They have one cause, so this plan fixes
both.

**Harness:** `plans/SQL_RESPONSE_CAPTURE_PLAN.harness/`, described in its `README.md`. `repro/` is a console that runs
every case twice, without and with the interceptor, against PostgreSQL 17 (Npgsql EF Core 10.0.1, the issue's
version), SQL Server 2022 LocalDB (EF Core 10.0.x with NetTopologySuite and HierarchyId) and SQLite, and runs Dapper
through Kronikol's ADO.NET wrappers. `results/` holds every run. `ilscan/` is a Mono.Cecil scan of 13 EF Core provider
packages for anything that needs the provider's own reader, with a database-free live test of SQL Server's spatial
mapping. `prototype.patch` is the design of section 4 as measured, with four differences:

- It leaves **every** SaveChanges reader alone, with no allow-list. SQL Server's and SQLite's SaveChanges notes under
  the allow-list are therefore the 5.10.1 ones, measured on 5.10.1.
- It writes the reason for an uncaptured query into the note, where the design puts it in a diagnostic.
- Its address helper misses three rules (4.1 f).
- It separates several result sets with one line break.

Windows 11, .NET SDK 10.0.300.

Line numbers are to `c37042e2` (5.10.1 plus one test commit). None of the files cited changed since `560ebb77`, so the
issue's own line numbers are the same. Wiki lines are on `origin/master` `8490f43`. Upstream lines are at the tags
named.

---

## 1. Summary

The issue is right about what fails and where. It understates how much fails, though. Its first suggestion fixes the
one crash it names and takes data away from providers that work today. Its second suggestion, capturing rows without
replacing the reader, has no public EF Core API behind it.

The underlying problem is that Kronikol records what a command returned by handing the application **a different
object** in place of the provider's `DbDataReader`, and it runs its own capture, formatting and address code inside
the application's calls with nothing to stop an exception. That is safe only if the substitute cannot be told apart
from the provider's reader. It can be told apart, both by its type and by how it behaves. Every failure measured comes
from one of these causes:

| What Kronikol does | Measured or found | Effect |
|---|---|---|
| Replaces the reader that EF Core's update pipeline reads (`SqlTrackingInterceptor.cs` L228-238, L270-280) | Npgsql's `Consume` casts it to `NpgsqlDataReader` as its first statement (efcore.pg v10.0.1 L76), in every release since 1.0.0 | **Every** Npgsql SaveChanges fails: insert, update, delete, batch (F1) |
| Replaces the reader that EF Core's compiled shaper reads | The shaper casts the reader whenever a type mapping reads through a provider method: SQL Server spatial (measured), Oracle `DateTimeOffset` and VECTOR, MySQL spatial (IL) | Queries fail on those columns. Suggestion 1 does not reach them (F2) |
| Forwards only `DbDataReader`'s abstract members (`TrackingDbDataReader.cs` L142-178) | `GetFieldValue<T>` falls back to `(T)GetValue()` | `DateTimeOffset`, `DateOnly`, `TimeOnly` and `List<int>` queries fail on PostgreSQL, SQL Server and SQLite. This is #197 (F3) |
| Reads every column of a row before the application does (L63-84) | Dapper's `Query` and `QueryAsync` open readers with `SequentialAccess` | Dapper fails through every ADO.NET wrapper. `GetValue` can also throw where the application would never call it (F4) |
| Serializes the provider's own objects into the note while the reader closes (`SqlRowFormatter.cs` L22-32, L78) | A geography value threw `SqlNullValueException` from `DisposeAsync` | The application's call fails, and the exception **hides** the application's own one (F5) |
| Builds the arrow's address with `new Uri($"sql://{DataSource}/…")` (L313-330, `SqlDiagnosticTracker.cs` L368-387) | `(localdb)\MSSQLLocalDB` and every named instance, `tcp:` and `np:` data sources, Npgsql host lists | **Every** command fails on those connections, through EF Core and through the SqlClient wrapper (F6, not in either issue) |
| Logs at the first `Read()` that finds no row (L37-43) | A second result set is never logged | Rows are lost, or merged without a boundary (F7) |
| Tests the interceptor with stub readers and SQLite (`SqlTrackingInterceptorResponseDetailTests.cs` L31) | All 566 existing SQL facts pass on the prototype unchanged | None of F1 to F7 was ever exercised (F8) |

Measured on 5.10.1 with the interceptor added (`results/`):

| Provider | Cases | Fail with the interceptor | Fail without it |
|---|---|---|---|
| PostgreSQL 17, Npgsql EF Core 10.0.1 | 15 | 9: every SaveChanges, `DateTimeOffset`, `List<int>`, the whole entity | 0 |
| SQLite, EF Core 10 | 13 | 4: `DateTimeOffset`, `DateOnly`, `TimeOnly`, the whole entity | 0 |
| SQL Server LocalDB, EF Core 10 | 15 | 15, all `UriFormatException` (F6) | 0 |
| The same, with the address bug bypassed | 17 | 4: `DateOnly`, `TimeOnly`, the whole entity, geography | 0 |
| Dapper through `TrackingNpgsqlConnection` / `TrackingSqlConnection` on LocalDB | 2 | 2 (`SequentialAccess`, the address) | n/a |

**R1, one patch release**, makes the substitution safe and rare, makes the substitute a faithful `DbDataReader`, and
stops Kronikol throwing into the application:

- **SaveChanges:** the reader is replaced only for providers whose update pipeline is known to read through
  `DbDataReader` alone (SQL Server, SQLite, and the providers the IL scan cleared). Npgsql, and any provider not on the
  list, keeps its own reader. Its response is logged when EF Core disposes it, as `N rows affected`.
- **Queries:** the reader is replaced unless the context's model reads some column through a provider reader method.
  This is decided once per model, from EF Core's own `GetDataReaderMethod()`. Such a context's queries keep the
  provider's reader, their response says `N rows`, and the run's diagnostics say which property needs it.
- **The decorator:** every virtual member forwards to the provider's reader. A sequential reader is never read ahead
  of the application. Every result set is kept.
- **No throwing:** every hook and capture path catches, and records a `CaptureDegraded` diagnostic, as the MongoDB
  subscriber has done since 4.19.6. A cell becomes JSON-safe text before it is serialized. Addresses come from one
  helper that cannot throw.

On the prototype every one of the 49 cases passes. On 5.10.1, 30 of the 45 cases measured there fail, and 4 of 17
still fail on SQL Server with the address bug bypassed. PostgreSQL SaveChanges logs
`1 rows affected` and `3 rows affected`. The query notes that work today are byte-identical. Dapper's rows are
captured through both wrappers (`results/prototype-all.txt`). The 566 existing facts in the EF Core, Npgsql,
SqlClient, Sqlite, Dapper, MySqlConnector, Oracle and core SQL suites all pass on it unchanged.

## 2. What the issues say, checked

| Claim | Verdict | Evidence |
|---|---|---|
| With `LogResponseContent` at its default, `ReaderExecuted(Async)` replace the provider's reader (L228-281, `WrapReaderIfTracked` L430-456) | **True** | L230-235, L272-277. Response capture, and with it the replacement, shipped in 2.37.0 (2026-05-17, `CHANGELOG.md` L13520) |
| Npgsql's provider reads its SaveChanges results as its own reader type | **True.** `var npgsqlReader = (NpgsqlDataReader)reader.DbDataReader;` is the first statement of `Consume` ([efcore.pg v10.0.1 L74-76](https://github.com/npgsql/efcore.pg/blob/v10.0.1/src/EFCore.PG/Update/Internal/NpgsqlModificationCommandBatch.cs#L74-L76)). It needs `Statements[i].Rows` for each statement's row count (L168). It is the same in 8.0.11, 9.0.4, 10.0.1, 10.0.3, 11.0.0-rc.2 and `main`. A cast of the reader has been there since the repository's first commit (2016), and reader interception arrived in EF Core 3.0 | IL scan, `ilscan/summary-1-reader-dependencies.txt` |
| "every insert, update and delete **that returns a row** fails" | **Understated.** The cast comes before the `try` (L86) and before any look at the result set mappings (L96), so an UPDATE or DELETE that returns nothing fails the same way. A single-command SaveChanges goes through it too, since the factory always builds this batch | Measured: "update, nothing returned" and "delete" fail (`results/5.10.1-pg-sqlite-dapper.txt`) |
| 405 of 459 scenarios failed, every one that seeds or saves through EF Core | **Consistent** with the above. The issue's explanation (a RETURNING row read through the reader) is one case of a cast that runs for every batch | as above |
| `LogResponseContent = false` avoids it, at the cost of every query's rows | **True.** The provider's reader goes back to EF Core (L236-237). At Detailed, non-queries still show `N rows affected` (L123-125), so the option's comment "response arrows are empty" (`SqlTrackingInterceptorOptions.cs` L34) is not quite right | code |
| Suggestion 1: leave the reader of a `CommandSource.SaveChanges` command alone and log it as `LogCommandExecuted` does | **Needed for Npgsql, not enough, and a loss elsewhere.** It fixes F1 for every provider. It does nothing for F2 to F7: #197's conversions alone failed 126 of the issue's 459 scenarios after the workaround. Applied to every provider, it takes SQL Server's and SQLite's SaveChanges notes from the generated values they show today (`[{"Id":3}]`, and `[{"Id":3,"_Position":0},…]` for a batch) to nothing at all on SQL Server, because EF Core runs SQL Server batches with `SET NOCOUNT ON` and `RecordsAffected` stays -1 (measured), and to `1 rows affected` on SQLite. `LogCommandExecuted` itself writes a response with no content | `results/5.10.1-mssql-localdb-uri-bypassed.txt`; prototype run |
| Suggestion 2: capture the rows without replacing the reader | **No public API does this.** `DataReaderClosing` sees the reader after EF Core has consumed it, with `ReadCount` but no values. `IMaterializationInterceptor` sees entity instances only, not projections or raw rows ([dotnet/efcore#33157](https://github.com/dotnet/efcore/issues/33157), [#33421](https://github.com/dotnet/efcore/issues/33421), both open). The one route is a provider-facing one, a `RelationalDataReader` subclass behind a replaced `IRelationalCommandBuilderFactory` (4.3, option C) | upstream notes Q3 |
| (#197) EF Core reads typed values through `GetFieldValue<T>`, whose base only casts what `GetValue` returns | **True.** [`DbDataReader.cs` v10.0.0 L245](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Data.Common/src/System/Data/Common/DbDataReader.cs): `public virtual T GetFieldValue<T>(int ordinal) => (T)GetValue(ordinal);` | measured on three providers (F3) |
| (#197) The same gap covers `IsDBNullAsync`, `GetStream`, `GetTextReader`, `GetSchemaTable(Async)`, `GetColumnSchema(Async)`, `GetProviderSpecificValue(s)`, `GetProviderSpecificFieldType`, `VisibleFieldCount` | **True**, and `GetEnumerator` too: it enumerates the provider's reader directly (L178), so rows read through it are never captured. `GetSchemaTable` and the `GetColumnSchema` fallback throw `NotSupportedException` on the decorator | runtime source |

## 3. Findings

### 3.1 F1: Npgsql's update pipeline needs its own reader

EF Core runs every SaveChanges batch through `ExecuteReader` with `CommandSource.SaveChanges`, and no SaveChanges path
uses `ExecuteNonQuery` ([`ReaderModificationCommandBatch.cs` v10.0.0 L336-393](https://github.com/dotnet/efcore/blob/v10.0.0/src/EFCore.Relational/Update/ReaderModificationCommandBatch.cs#L336-L393)).
The interceptor's reader goes into the `RelationalDataReader` that `Consume` receives. Npgsql casts it (section 2). The
maintainers treat this as by design. [efcore.pg#1113](https://github.com/npgsql/efcore.pg/issues/1113), open since
2019, is titled "Can't intercept via wrapping reader because NpgsqlModificationCommandBatch depends casts to
NpgsqlDataReader, and it is sealed". In it, roji wrote "For now, I'd advise doing other ways for manipulating the data
returned from the reader". Removing the cast waits on EF Core's batching API
([dotnet/efcore#18990](https://github.com/dotnet/efcore/issues/18990), Backlog).

Measured: with the interceptor, all six SaveChanges cases on PostgreSQL fail with
`DbUpdateException` / `InvalidCastException: Unable to cast object of type 'Kronikol.Sql.TrackingDbDataReader' to type
'Npgsql.NpgsqlDataReader'`. The IL scan found no other reader dependency in any Npgsql EF Core assembly, the
NetTopologySuite and NodaTime plugins included.

### 3.2 F2: EF Core's shaper casts the reader for some type mappings

`RelationalTypeMapping.GetDataReaderMethod()` may name a method of a `DbDataReader` subclass, and the shaper then
emits `Convert(dbDataReader, method.DeclaringType)`
([`ShaperProcessingExpressionVisitor.cs` v10.0.0 L2921-2935](https://github.com/dotnet/efcore/blob/v10.0.0/src/EFCore.Relational/Query/RelationalShapedQueryCompilingExpressionVisitor.ShaperProcessingExpressionVisitor.cs#L2921-L2935)).
EF Core documents this as allowed. [dotnet/efcore#33910](https://github.com/dotnet/efcore/issues/33910) hit it with a
wrapped reader and SQL Server spatial types and was closed as not planned. roji: "you can't just return an arbitrary
DbDataReader - EF expects it to be a SqlDataReader". SaveChanges reads generated values through the same methods
(`GetReaderFieldValue`).

| Provider (version scanned) | Mapping | Reader method | Basis |
|---|---|---|---|
| Microsoft.EntityFrameworkCore.SqlServer.NetTopologySuite 8.0.33, 9.0.22, 10.0.14 | geometry, geography | `SqlDataReader.GetSqlBytes` | IL, source, live test, **measured** |
| Oracle.EntityFrameworkCore 10.23.26400 | **any `DateTimeOffset`** (`TIMESTAMP WITH TIME ZONE`) | `OracleDataReader.GetDateTimeOffset` | IL |
| Oracle.EntityFrameworkCore 10.23.26400 | VECTOR | `GetInt16Array`, `GetFloatArray`, `GetDoubleArray`, `GetByteArray`, `GetOracleVector` | IL |
| Pomelo.EntityFrameworkCore.MySql.NetTopologySuite 9.0.0 | spatial | `MySqlDataReader.GetMySqlGeometry` | IL, source |
| MySql.EntityFrameworkCore 10.0.12 | `MySqlGeometry` | `MySqlDataReader.GetMySqlGeometry` | IL |

HierarchyId on SQL Server reads through `GetFieldValue<SqlHierarchyId>` and is safe (measured). Npgsql, SQLite,
Firebird and EF Core itself name no provider reader method. A NULL spatial value works, because the shaper checks
`IsDBNull` first. Every provider reader is sealed or has only internal constructors, so a subclass cannot stand in.

Measured on SQL Server: the geography query fails with `InvalidCastException: … 'Kronikol.Sql.TrackingDbDataReader'
to type 'Microsoft.Data.SqlClient.SqlDataReader'` in the compiled shaper, **but only with `ResponseDetail =
RowCountOnly`**. At the default detail, F5 replaces that exception with another one.

### 3.3 F3: the decorator is not the reader the provider wrote (#197)

`TrackingDbDataReader` overrides the abstract members and nothing else (L142-178). Each provider's `GetValue` returns
the column's default type, and its `GetFieldValue<T>` converts. EF Core reads every type outside its twelve basic ones
through `GetFieldValue<T>`, so through the decorator those reads become `(T)GetValue()`:

| Provider | `GetValue` returns | EF Core asks for | Measured |
|---|---|---|---|
| Npgsql 10 | `DateTime` (timestamptz) | `DateTimeOffset` | fails |
| Npgsql 10 | `int[]` | `List<int>` | fails |
| Npgsql 10 | `DateOnly`, `TimeOnly` (new in 10.0; `DateTime` and `TimeSpan` in 8 and 9) | the same | works on 10 only |
| SqlClient | `DateTime` (date), `TimeSpan` (time) | `DateOnly`, `TimeOnly` (`GetFieldValue` support since SqlClient 5.1) | fails |
| Microsoft.Data.Sqlite | `string` (TEXT) | `DateTimeOffset`, `DateOnly`, `TimeOnly` (and `TimeSpan`, `uint`, `ulong`, `ushort`, `sbyte` by source) | fails |

Npgsql's `jsonb` read as `JsonDocument` or `JsonElement` fails the same way, by source. `GetSchemaTable()` throws
`NotSupportedException`, and so does `DbDataReaderExtensions.GetColumnSchema()`, because the decorator does not
implement `IDbColumnSchemaGenerator` while `CanGetColumnSchema()` still answers true. `DataTable.Load` uses the
schema table. `GetEnumerator` (L178) hands out the provider's enumerator, so rows read through it are never captured.

### 3.4 F4: the capture reads ahead of the application

At every `Read()` the decorator calls `IsDBNull(i)` and `GetValue(i)` on every column before returning the row
(L63-84). That has two consequences.

**`SequentialAccess`.** In that mode a column can be read once and in order. SqlClient (`CheckDataIsReady`) and
Npgsql (`SeekToColumn`) both throw "Invalid attempt to read from column ordinal '0'. With
CommandBehavior.SequentialAccess, you may only read from column ordinal …". Dapper 2.1.89 opens every query reader
that way, sync and async, with no switch to turn it off
([`SqlMapper.Async.cs` L434](https://github.com/DapperLib/Dapper/blob/2.1.89/Dapper/SqlMapper.Async.cs#L422-L434),
[`SqlMapper.cs` L1219](https://github.com/DapperLib/Dapper/blob/2.1.89/Dapper/SqlMapper.cs#L1204-L1219)). Every
ADO.NET wrapper passes the caller's behaviour straight through and wraps the result (`TrackingNpgsqlCommand.cs`
L72-92, and the same shape at `TrackingSqliteCommand.cs`, `TrackingMySqlCommand.cs`, `TrackingSqlCommand.cs`,
`TrackingOracleCommand.cs` L219, `TrackingSpannerCommand.cs` L240, `TrackingClickHouseCommand.cs` L235, Dapper's
`TrackingDbCommand.cs` L363). Measured: `QueryAsync` through `TrackingNpgsqlConnection` fails with that message. SQLite
ignores the flag, which is why the Dapper suite never saw this. EF Core never passes a behaviour
(`RelationalCommand.cs` v10.0.0 L557, L573, L689, L707).

**A read the application would not make.** SqlClient's `GetValue` on a geography, geometry or hierarchyid column loads
Microsoft.SqlServer.Types and throws when it is absent
([`SqlDataReader.cs` v7.0.0 L2887-2898](https://github.com/dotnet/SqlClient/blob/v7.0.0/src/Microsoft.Data.SqlClient/src/Microsoft/Data/SqlClient/SqlDataReader.cs#L2846-L2905)).
EFCoreSecondLevelCacheInterceptor met exactly that
([#176](https://github.com/VahidN/EFCoreSecondLevelCacheInterceptor/issues/176)). An exception from the capture
escapes from the application's own `Read()`.

### 3.5 F5: writing the note can throw, and hides the application's exception

`FormatCellValue` keeps the provider's object as it is when its text is short (`SqlRowFormatter.cs` L22-32), and
`Format` hands the rows to `JsonSerializer.Serialize` (L78). System.Text.Json then reflects over the object's public
properties. That happens in `LogIfNotDone`, which runs from `Close`, `CloseAsync`, `Dispose` and `DisposeAsync`
(L111-138). Measured on SQL Server: a geography column's value threw `SqlNullValueException` from a property getter,
out of `RelationalDataReader.DisposeAsync`. Because disposal runs in a `finally`, that exception **replaced** the
`InvalidCastException` of F2, which is why the first run reported "Data is Null" for a query whose real failure was a
cast (`results/5.10.1-geography-close-stack.txt`).

No interceptor hook catches anything. The MongoDB subscriber has caught in every handler since 4.19.6 and records a
`CaptureDegraded` diagnostic through `ProcessDiagnostics.RecordOnce` (`MongoDbTrackingSubscriber.cs` L85-102,
L370-384). The SQL trackers never adopted that.

### 3.6 F6: the arrow's address throws for common data sources (not in either issue)

`BuildUri` writes `sql://{DataSource}/{Database}/{Table}` and calls `new Uri` (L313-330). The ADO.NET trackers do the
same in `SqlDiagnosticTracker.BuildUri` (L368-387). It runs in `LogCommandExecuting` (L68) before the command is sent,
in `LogCommandExecuted`, and again in `LogCommandFailed` (L186). `results/uri-probe.txt`:

| Data source | 5.10.1 |
|---|---|
| `(localdb)\MSSQLLocalDB`, `.\SQLEXPRESS`, `localhost\SQLEXPRESS`, `myhost\inst,1433` | throws `UriFormatException` |
| `tcp:myserver,1433`, `np:\\.\pipe\sql\query` | throws |
| `host1:5432,host2:5432` (an Npgsql host list) | throws |
| `tcp://localhost:55432` (what `NpgsqlConnection.DataSource` reports) | parses with host `tcp` and path `//localhost:55432/k193/Orders`: every Npgsql arrow through the interceptor is drawn against a host called `tcp` |
| Summarised, database `My Db` (it is the host there) | throws |
| `myserver.database.windows.net,1433`, `spanner-emulator:9010`, `[::1]:5432` | parses |

Measured on LocalDB: all 15 EF Core cases fail, and so does Dapper through `TrackingSqlConnection`. When the command
then fails, `LogCommandFailed` throws again, so EF Core surfaces the `UriFormatException` and not the provider's
error. The Npgsql wrapper avoids the `tcp://` form with its own `FormattedDataSource` (`TrackingNpgsqlConnection.cs`
L58-66); the interceptor has no such step.

### 3.7 F7: result sets after the first

The decorator logs when the first `Read()` finds no row (L39-43), then sets `_logged`. It takes the column names at
that moment, from whichever result set is current (L101-107). Measured:

- Raw ADO.NET through `TrackingNpgsqlConnection`, `SELECT 1 AS a UNION ALL SELECT 2; SELECT 'x' AS b`, read to the
  end. The application read `1, 2, x`. The note says `[{"a":1},{"a":2}]`, and the second set is gone.
- SQL Server SaveChanges into two tables, where EF Core reads one row per set and never drains a set. The note says
  `[{"Id":3},{"Id":2}]`: both sets in one list, with no boundary.

By the same code, a stored procedure with several SELECTs read to the end loses every set after the first. So does
Dapper's `QueryMultiple` on a provider that ignores `SequentialAccess`; elsewhere F4 fails it first.

### 3.8 F8: nothing tests a provider that minds

`Kronikol.Tests.EfCore.Relational` references only the InMemory and SQLite providers (csproj L13-16). The response
tests call `interceptor.ReaderExecuted(command, null!, new RowStubDataReader())` directly (L31), with a stub whose
`GetValue` returns exactly what is read. The one end-to-end fact runs SQLite for the failure path only. Test entities
have `int` and `string` columns. The ADO.NET wrapper suites run on SQLite or stubs, and SQLite ignores
`SequentialAccess`. On the prototype, all 566 existing facts in those suites pass unchanged, so none of them noticed
F1 to F7, and none pins what R1 changes.

The wiki says the extension "works with **any** EF Core relational provider" and lists PostgreSQL, Oracle and Spanner
(`Integration-EF-Core-Relational-Extension.md` L9-18).

### 3.9 Found on the way, not in this plan

- **Spanner SaveChanges raises no `DbCommandInterceptor` event at all.** The batch builds and runs its own commands
  (Google.Cloud.EntityFrameworkCore.Spanner 4.x, `SpannerModificationCommandBatch.cs` L128-140). This is #200, which has its
  own fix. R1 neither helps nor harms it.
- Oracle's async `ExecuteReader` reflects `m_isFromEFCore` onto the reader after the interceptor has run, so on a
  substituted reader the flag is silently not set. By the IL, ODP.NET 23.x never reads it, so nothing visible changes.
  R1 keeps the provider's reader in more cases. It does not otherwise touch this.
- Handing a Kronikol-wrapped connection to EF Core cannot work: Npgsql EF Core casts the connection
  (`NpgsqlRelationalConnection.get_DbConnection`), and Oracle casts the command. The wiki should say to use the
  interceptor with EF Core and the wrappers without it (section 7).

## 4. Design

### 4.1 The rule

**Kronikol never changes what the application's code receives, and never throws into it.** Concretely:

**(a) Replace the provider's reader only where everything that reads it uses `DbDataReader`'s own contract.**

- **SaveChanges** (`eventData.CommandSource == CommandSource.SaveChanges`): replace only when the context's provider
  (`DbContext.Database.ProviderName`) is on an allow-list of update pipelines the IL scan cleared. Those are
  `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.EntityFrameworkCore.Sqlite`,
  `Pomelo.EntityFrameworkCore.MySql`, `MySql.EntityFrameworkCore`, `FirebirdSql.EntityFrameworkCore.Firebird` and
  `Oracle.EntityFrameworkCore` (Q2 asks about the last four). For any other provider, Npgsql included, the provider's
  reader goes back to EF Core. The response is logged at `DataReaderDisposing`, after the reader is closed, from its
  `RecordsAffected`: `N rows affected` in the wording `NonQueryExecuted` already uses, or no content when the provider
  reports -1. It is read at Disposing, not Closing, because SqlClient documents that `RecordsAffected` "is not set until
  all rows are read and you close the SqlDataReader". (On SQL Server itself it stays -1 at both, because of
  `SET NOCOUNT ON`, measured. SQL Server stays on the allow-list.)
- **Every other command:** replace unless the context's model reads some value through a provider reader method. The
  test is EF Core's own: a type mapping's `GetDataReaderMethod().DeclaringType` that `DbDataReader` is not assignable
  to. It covers every property of every entity and complex type, plus the type mapping source's mappings for
  `DateTimeOffset`, `DateOnly`, `TimeOnly` and `TimeSpan`, the types raw SQL projects without an entity. The verdict
  is computed once per `IModel` and cached in a `ConditionalWeakTable`. The same verdict also stops SaveChanges
  substitution on allow-listed providers, since generated values are read through the same methods. When the verdict
  is "unsafe", the provider's reader goes back to EF Core. The response is logged at `DataReaderClosing` as `N rows`,
  where N is `ReadCount` less the final read that found no row. That read is detected by asking the still-open reader
  whether it stands on a row. Measured exact: 3 reads and not on a row is 2 rows, and EF Core's `First` makes 2 reads
  for 1 row. One `CaptureDegraded` diagnostic per context type names the property and the method, for example
  "rows of queries on `ShopContext` are not captured: `Place.Location` is read through `SqlDataReader.GetSqlBytes`,
  which needs the provider's own reader".
- A direct call with `eventData == null`, as the interceptor's own tests make, keeps today's behaviour.

**(b) The substitute is a faithful decorator.** Every virtual member of `DbDataReader` forwards to the provider's
reader: `GetFieldValue<T>`, `GetFieldValueAsync<T>`, `IsDBNullAsync`, `GetStream`, `GetTextReader`,
`GetSchemaTable(Async)`, `GetColumnSchemaAsync`, `GetProviderSpecificValue(s)`, `GetProviderSpecificFieldType`,
`VisibleFieldCount`, `GetData`. It also implements `IDbColumnSchemaGenerator` by forwarding (Q4), and `GetEnumerator`
enumerates the decorator itself, so those rows are captured too.

**(c) The capture never reads ahead of a sequential reader.** Under `CommandBehavior.SequentialAccess` it records each
value as the application reads it, through whichever getter the application used. Under any other behaviour it reads
the row first, as today, so today's notes stay byte-identical, but each cell is read separately. A cell that cannot
be read shows `(not readable as a value: <exception type>)` and the application's row is untouched. The ADO.NET
wrappers pass their `behavior` to the decorator through a new internal constructor. EF Core always uses the default
behaviour.

**(d) Every result set is kept.** The note is written once, at `Close`, `Dispose` or their async forms. One result set
gives exactly today's note. Several give one note per set (Q3). Each set's column names are read when its first row
is read.

**(e) Nothing escapes.**

- Every interceptor hook body catches. So do the decorator's capture, formatting and `onComplete` paths.
- A failure records one `CaptureDegraded` diagnostic per tracker through `ProcessDiagnostics.RecordOnce`, as the
  MongoDB subscriber does.
- `FormatCellValue` keeps a value as it is only when System.Text.Json writes it natively: primitives, `string`,
  `decimal`, `Guid`, the date and time types, enums, `JsonElement` and `JsonDocument`, and arrays or lists of those.
  Any other value becomes its `ToString()`, itself guarded, so writing a note cannot reflect over a provider type.

**(f) One address helper for every SQL tracker.** `SqlEndpoint` (internal, in `Kronikol/Sql`) turns any data source
into a host, and the address is built with `Uri.TryCreate` with a fallback that cannot throw. The interceptor's
`BuildUri` and `SqlDiagnosticTracker.BuildUri` both use it. The rule and its expected outputs:

| Data source | Host drawn | Rule |
|---|---|---|
| `localhost,1433`, `127.0.0.1,1433` | `localhost:1433`, `127.0.0.1:1433` | SQL Server's comma port becomes a colon, as today |
| `(localdb)\MSSQLLocalDB` | `localdb-mssqllocaldb` | an instance name joins its server with a dash |
| `.\SQLEXPRESS`, `localhost\SQLEXPRESS`, `myhost\inst,1433` | `localhost-sqlexpress`, `myhost-inst:1433` | `.` and `(local)` are `localhost` |
| `tcp:myserver,1433`, `np:\\.\pipe\sql\query` | `myserver:1433`, `localhost` | the protocol prefix goes. A pipe is local |
| `tcp://localhost:55432` (Npgsql) | `localhost:55432` | the scheme goes |
| `host1:5432,host2:5432` | `host1:5432` | the first host of a list |
| `/var/run/postgresql`, `C:\data\app.db` | `localhost` | a socket or file path has no host. Today the second draws as an empty host with the path in front of the database |
| `[::1]:5432` | `[::1]:5432` | a bracketed IPv6 address is kept, as today |
| database `My Db` at Summarised | `my-db` | the same sanitising, for the one verbosity that puts the database in the host |

The prototype's helper got three of these wrong: `[::1]:5432` (it drew `1-5432`), the path rows and the pipe
(`results/prototype-endpoint-table.txt`). Fact 6.2.6 pins every row.

### 4.2 What changes in the report

| Case | 5.10.1 | After R1 |
|---|---|---|
| Npgsql SaveChanges | the application's call fails | `1 rows affected`, `3 rows affected` for a batch (measured) |
| Npgsql query, any type | rows, or a failure (F3) | rows, the same note where 5.10.1 worked |
| Any arrow through the interceptor on Npgsql | address `sql://tcp//localhost:55432/k193/Orders` (host `tcp`) | `sql://localhost:55432/k193/Orders`. **A visible change for every Npgsql user of the interceptor** |
| SQL Server or SQLite SaveChanges | `[{"Id":3}]` | unchanged (allow-list), except that a batch across tables gets one note per result set (F7) |
| SQL Server LocalDB or a named instance | the application's call fails | drawn against `localdb-mssqllocaldb` |
| A query on a context with a provider reader method (SQL Server spatial, Oracle `DateTimeOffset`) | rows when no such column was read, a failure when one was | `N rows` for every query on that context, plus one diagnostic. **A loss for the queries that worked** (Q1 and Q8) |
| A command with several result sets | the first set, or all rows merged | one note per set (Q3) |
| A cell holding a provider object (geography, a range, a network address) | its properties as JSON, or a failure | its text |
| Dapper through any ADO.NET wrapper | a failure where the provider enforces sequential access | the rows Dapper read |

None of this touches report rendering, so Kronikol4J's byte parity is unaffected and its ledger needs no entry.

### 4.3 Options considered

| Option | Fixes | Against |
|---|---|---|
| A. The issue's suggestion 1: never replace a SaveChanges reader | F1 | Leaves F2 to F7. Takes SQL Server's and SQLite's SaveChanges notes from generated values to nothing (SQL Server) or a count |
| B. Never replace any reader: counts everywhere | F1 to F5 | Throws away every query's rows for every provider, the feature 2.37.0 shipped |
| **C. Capture inside a `RelationalDataReader` subclass** (roji's suggestion in [efcore.pg#1922](https://github.com/npgsql/efcore.pg/issues/1922#issuecomment-3190770868)): replace `IRelationalCommandBuilderFactory` so that `RelationalCommand.CreateRelationalDataReader()` returns a reader whose `Read()` copies the row from the provider's reader | F1, F2 and F3 without any substitution, rows for Npgsql SaveChanges and for spatial contexts too | Provider-facing API that has to be wired through the options builder (`ReplaceService`), so `.AddInterceptors(...)` users get nothing until they change their setup, which is new public surface. Oracle registers its own factory, so it would need a decorator or an exclusion. Instances are reused across executions. Values come from `GetValue`. **The right second step if rows matter there (Q8), not a first fix** |
| D. Deny-list Npgsql for SaveChanges, replace everywhere else | F1 | A provider not on the list with the same dependency (a fork, or one not yet written) fails the same way. Unsafe by default |
| **E. Allow-list SaveChanges, model verdict for the rest, faithful decorator, no throwing** | F1 to F7 | The coarse model verdict (4.2). An allow-list to keep current. **Recommended** |
| F. Catch the cast | none | It happens inside the provider |
| G. Subclass the provider's reader | none | Every provider reader is sealed or has only internal constructors |
| H. Capture through `IMaterializationInterceptor` | entities only | No projections, no raw SQL, no SaveChanges values, no command id to pair with |
| I. Per-query verdict from the result's columns (`GetDataTypeName`, `GetFieldType`) against the type mapping source | finer than E | A provider's `GetDataTypeName` need not match its mappings' store type names, and a column the database reports as a timestamp is safe or not depending on the CLR type EF Core maps it to. A wrong "safe" is a crash |

### 4.4 What does not change

- **No new option and no new public member** (but see Q4). `TrackingDbDataReader` keeps its public constructor. The
  behaviour-aware one is internal: every SQL extension already sees Kronikol's internals.
- `LogResponseContent = false` behaves exactly as today.
- A single-result-set note from a reader that worked on 5.10.1 is byte-identical. The prototype's notes for the
  working PostgreSQL and SQLite queries match 5.10.1's.
- The request half of every arrow is unchanged, apart from the address of F6.

## 5. Releases

**R1, one patch release.** Every item is a fix: nothing new for a consumer to call, and the behaviour changes in 4.2
are called out in the changelog per `CLAUDE.md`. The parts share two classes (`TrackingDbDataReader`,
`SqlTrackingInterceptor`), and each part alone still leaves the application failing. Without (e), for instance, (a)
and (b) still crash on F6. So they ship together. The number is claimed at push. kronikol-30 has 5.11.0 in flight, so
R1 is probably 5.11.1.

**R2 (only if Q8 says yes), a minor:** option C as an opt-in (for example through `WithSqlTestTracking`), to bring rows
back for Npgsql SaveChanges and for contexts with provider reader methods.

## 6. Tests (TDD: each written red against 5.10.1, then made green)

### 6.1 Fixtures

- `PostgresServerFixture` and `SqlServerServerFixture` in `Kronikol.Tests.EfCore.Relational`, built like
  `MongoServerFixture`. Each one uses the server its environment variable names if set, otherwise starts one through
  Testcontainers (`Testcontainers.PostgreSql`, `Testcontainers.MsSql`). Without either, the facts that need it skip
  with a reason. CI's "Extension Tests" lane, which already runs this project, is on `ubuntu-24.04` with Docker, so
  they run there. On Windows the SQL Server variable can name LocalDB, which also exercises F6.
- SQLite runs in process everywhere. Most facts need no container: fake readers do the work. These include a strict
  sequential reader, a reader whose `GetValue` and `GetFieldValue<T>` disagree as Npgsql's do, a reader with a column
  whose `GetValue` throws, and one with two result sets.

### 6.2 Facts

1. **Npgsql SaveChanges** (container): insert with a generated key, update with nothing returned, delete, three inserts
   in one batch, sync and async. Each succeeds with the interceptor, and its response says `N rows affected`. Red on
   5.10.1 with the issue's exception.
2. **Npgsql queries** (container): `DateTimeOffset`, `List<int>`, `jsonb` as `JsonDocument`, the whole entity. The
   application gets its values, and the note has the rows.
3. **SQLite queries** (no container): `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan`. Red today, in the suite's
   own provider.
4. **SQL Server** (container or LocalDB): `DateOnly`, `TimeOnly`, a geography query (non-NULL), and a query of an `int`
   column on the same context. All succeed. The spatial context's notes say `N rows`. One diagnostic names
   `Place.Location` and `SqlDataReader.GetSqlBytes`.
5. **SQL Server SaveChanges** note byte-identical to 5.10.1 (`[{"Id":…}]`, the batch's `_Position` rows): the
   allow-list pin.
6. **Addresses** (unit, both `BuildUri`s): every row of the table in 4.1 (f) gives its host, and none throws. A
   `DbConnection` whose `DataSource` getter throws still gets an arrow.
7. **No hook throws** (unit): with a formatter or logger made to throw, every `DbCommandInterceptor` override returns
   EF Core's result, and a `CaptureDegraded` diagnostic is recorded once.
8. **Forwarding** (unit, `TrackingDbDataReaderTests`): every virtual member listed in 4.1 (b) reaches the inner reader.
   `GetFieldValue<DateTimeOffset>` returns the inner reader's conversion. `GetColumnSchema()` and `GetSchemaTable()`
   return the inner reader's. Enumerating the decorator captures the rows.
9. **Sequential access** (unit, plus Dapper through `TrackingDbCommand` over the strict fake, plus Npgsql in the
   container): no value is read twice or out of order, and the note holds the values Dapper read.
10. **A cell that cannot be read**: `Read()` succeeds, and the cell shows the placeholder.
11. **A cell whose `ToString` or property getter throws**: the note is written, and `Dispose` does not throw.
12. **Result sets**: two sets read to the end are both in the note. One set gives a note byte-identical to 5.10.1's.
    SQL Server SaveChanges into two tables keeps its sets apart.
13. **Provider not on the allow-list** (SQLite under a test seam that reports another provider name): its SaveChanges
    reader is not replaced.
14. **Model verdict** (unit, no server): a SQL Server model with a NetTopologySuite property is unsafe. One with `int`,
    `string`, `DateTimeOffset` and `DateOnly` is safe. A SQLite model is safe. An Npgsql model with `List<int>` and
    `jsonb` is safe. The verdict is computed once per model.
15. **Untouched reader counts**: `ToList` over 2 rows, `First`, `Single`, and an enumeration abandoned after one row
    give 2, 1, 1 and 1.
16. The existing `ReaderExecuted(command, null!, …)` facts keep passing: a direct call is unchanged.

### 6.3 Mutations

Each of these must turn a fact red:

| Mutation | Fact that turns red |
|---|---|
| Drop the SaveChanges allow-list check | 1 |
| Drop the model verdict | 4 |
| Drop the `GetFieldValue<T>` forwarding | 2, 3, 8 |
| Capture eagerly under `SequentialAccess` | 9 |
| Remove one hook's catch | 7 |
| Format provider objects as before | 11 |
| Log at the first empty `Read()` again | 12 |
| Revert `SqlEndpoint`'s IPv6, path or pipe rules | 6 |

Reading `RecordsAffected` at Closing instead of Disposing has no fact that turns red. Npgsql's count is already final
at Closing, and SQL Server reports -1 at both. The choice rests on SqlClient's documented behaviour.

### 6.4 Checks

- The harness repro, re-run on R1, gives 49 of 49 with notes matching section 4.2.
- The full suite passes.
- `git diff` of the golden report pins is empty: no rendering change.

## 7. Documentation and ledger

- **Wiki, `Integration-EF-Core-Relational-Extension.md`:**
  - In the provider table (L9-18), say what is captured per provider.
  - A new "What a response shows" section, covering:
    - SaveChanges on Npgsql shows rows affected.
    - A context whose model reads a column through a provider's own reader shows row counts, and the run's
      diagnostics name the column.
    - Hand EF Core the interceptor, never a wrapped connection.
  - Correct the `LogResponseContent` row (L212).
- **Wiki SQL extension pages** (Npgsql, SqlClient, Sqlite, MySqlConnector, Oracle, Dapper):
  - Dapper's rows are captured, with sequential access respected.
  - Several result sets are each shown.
  - The addresses drawn for named instances and LocalDB.
- **XML docs:** the `LogResponseContent` comment (`SqlTrackingInterceptorOptions.cs` L32-35) and
  `TrackingDbDataReader`'s summary.
- **`CHANGELOG.md`, the patch's entry:** every row of 4.2 that changes, under "Changed behaviour", with #193, #197
  and F6.
- **Kronikol4J:** no rendering change, so no ledger entry.

## 8. Coordination

- **Files R1 touches:**
  - `src/Kronikol/Sql/TrackingDbDataReader.cs`, `SqlRowFormatter.cs`, `SqlDiagnosticTracker.cs`, and a new
    `SqlEndpoint.cs`.
  - `src/Kronikol.Extensions.EfCore.Relational/SqlTrackingInterceptor.cs` and a new `ProviderReaderMethods.cs`.
  - The `WrapReader` of each ADO.NET wrapper command.
  - The EF Core test project's csproj and fixtures, and the SQL wrapper test projects.
- No other worktree's branch touches these paths (checked against `feat/github-search`, `v5`, `v5-tags` and the open
  plan branches on 2026-10-26).
- kronikol-30 owns 5.11.0. The R1 number is claimed by message at push.
- #200 (Spanner) and #202 (documentation samples) touch neighbouring wiki pages. Wiki edits go in at release time, as a
  replacement script.

## 9. Questions for the owner

1. **The rule.** Allow-list SaveChanges and a model verdict for the rest (recommended, E), or the issue's suggestion 1
   alone (A: smaller, but it loses SQL Server's and SQLite's SaveChanges values and leaves F2 to F7)?
2. **The allow-list's last four providers.** Pomelo, MySql.EntityFrameworkCore, Firebird and Oracle are cleared by the
   IL scan, and 5.10.1 substitutes for them today, but no live test runs them. Keep them on the list (recommended: no
   change for their users) or leave them off until a test runs each?
3. **Several result sets.** Each set's note on its own line, in order (recommended, as prototyped: one set's note
   stays exactly as today), or one JSON array of arrays?
4. **`IDbColumnSchemaGenerator` on the public `TrackingDbDataReader`.** Implemented explicitly, it adds no member a
   consumer can call by name, and it makes `GetColumnSchema()` work as `CanGetColumnSchema()` already claims. A patch
   (recommended), or new surface and so a minor?
5. **Addresses.** Accept the table in 4.1 (f), including Npgsql's change from host `tcp` to `localhost:55432` and
   `localdb-mssqllocaldb` for LocalDB?
6. **Issues.** File F6 (the address) and F4 (sequential access, Dapper) as issues of their own for the record, or
   carry them in #193's and #197's release notes?
7. **CI.** Add Testcontainers PostgreSQL and SQL Server to the EF Core test project, which runs in the Extension Tests
   lane? The SQL Server image is about 1.5 GB. The alternative is SQL Server on Windows LocalDB only, outside CI.
8. **Rows where R1 gives counts.** Option C as an opt-in minor later (R2), and, separately, whether to tell
   efcore.pg#1113 about this use case. That would be public, under the owner's name.
9. **#197.** Close it with R1 (recommended), since its fix is 4.1 (b)?

## 10. Log

- 2026-10-26, kronikol-d0:
  - Read #193, #197, #200 and #202.
  - Reproduced on 5.10.1 against PostgreSQL 17 (podman), SQL Server 2022 LocalDB and SQLite. A SQL Server container
    in podman never answered TDS pre-login and was dropped, so LocalDB was used with the address bug bypassed by a
    temporary edit, reverted after each run (`repro/with-uri-bypass.sh`).
  - Two background research passes: the provider IL scan (`ilscan/`), and an upstream source and issue survey, whose
    conclusions are cited inline.
  - The first SQL Server geography run reported `SqlNullValueException`. Re-run with `RowCountOnly`, it showed the
    cast, and that is how F5's masking was found.
  - `RecordsAffected` read at `DataReaderClosing` was -1 on SQL Server, and still -1 at Disposing, because of SQL
    Server's `SET NOCOUNT ON` batches. That is why the allow-list keeps substitution for SQL Server rather than
    trading its rows for a count.
  - Prototyped the design (`prototype.patch`, which applies to `c37042e2`): 49 of 49 cases, and the 566 existing facts
    of the eight SQL suites pass.
  - The endpoint table caught three misses in the prototype's own helper (IPv6, paths, pipes), now in 4.1 (f) and
    fact 6.
