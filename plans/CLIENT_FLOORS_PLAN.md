# Client floors plan (#205, #97)

**Status:** written 2026-10-27 at 5.12.0 (`1ee6373a`) by kronikol-2d, at the owner's ask to analyse #205 critically
and plan a fix for the underlying problem; audited the same day at 5.14.0 (`8d7d1e1d`), at the owner's ask to make it
complete and accurate (section 11 lists what the audit found and changed). **Green-lit 2026-10-28** by the owner ("Can
you implement the plan in full"), every question in section 10 at its recommendation; executing. **R1 released as
5.22.1** (section 11).

**Issues:** [#205](https://github.com/lemonlion/Kronikol/issues/205), "BigQuery: the extension's floor,
Google.Cloud.BigQuery.V2 3.12, runs ExecuteQuery through jobs.query, whose timestamps the emulator answers in seconds,
so reading a TIMESTAMP column throws FormatException", and its general case
[#97](https://github.com/lemonlion/Kronikol/issues/97), "Extension packages float their client dependency (2.*):
every release raises consumers' floor". Siblings: [#125](https://github.com/lemonlion/Kronikol/issues/125) (emulator
URLs drawn as `Other`), planned by kronikol-5d in `EMULATOR_URL_SHAPES_PLAN.md`, roadmap 1.33;
[#158](https://github.com/lemonlion/Kronikol/issues/158) (rebuilding the app's client in DI), which this plan does not
need.

**Harness:** `plans/CLIENT_FLOORS_PLAN.harness/`, described in its `README.md`. `probe/` is a console that runs the same
TIMESTAMP reads (eleven, and two more with parameters since the audit) on Google.Cloud.BigQuery.V2 3.0.0, 3.11.0 and
3.12.0 against five releases of the BigQuery emulator and a build of its fix PR #492, with and without Kronikol's
tracking handler, and records every request and answer. `floor/` measures the oldest client the extension can ask
for, `restore/` reproduces the issue's restore failure and central package management's answer to a float, `nuget/`
reads what every published package asks for, floats and fixed pins alike, `prototype/` holds R1 as a patch with its
runs, and `drafts/` holds text for the owner to decide on. Windows 11, .NET SDK 10.0.300, emulators in podman
(`ghcr.io/goccy/bigquery-emulator`, the images the issue names).

Line numbers are to `1ee6373a`; every file they cite is unchanged at `8d7d1e1d` (5.14.0). Upstream lines are at the
tags or commits named: google-cloud-dotnet `5941905203` (the 3.11.0 release) and `7ffaea80ef` (3.12.0),
goccy/bigquery-emulator `v0.6.0`, `v0.6.1` and `v0.8.1` (`a531d3de`), and its PR #492 at `c80afb507e`.
Wiki lines are on `origin/master` `ae3393f`. **RUN** marks a fact measured in the harness, **READ** one read in source.

---

## 1. Summary

The issue is right about what changed in the client and about Kronikol's part in it. It is wrong about the emulator,
and so about the failure.

- **The FormatException needs an emulator older than 0.6.1, not 0.8.1.** Emulator 0.8.1 reads `formatOptions` from the
  body of a `jobs.query` call and answers the issue's call in microseconds; the read passes on 3.12.0 (RUN). Emulators
  0.4.4 and 0.6.0 read the option only from the URL and answer in seconds, which throws exactly the issue's
  `FormatException: The input string '1698364800.0'` (RUN). The emulator fixed this in 0.6.1, April 2024 (READ). The
  value itself is the old backend's spelling: 0.8.1 writes seconds as `1698364800.000000` (READ, RUN). The call with
  query parameters, as the issue's has, gives the same results (RUN). Testcontainers' `BigQueryBuilder()` with no image
  starts `ghcr.io/goccy/bigquery-emulator:0.4`, which is 0.4.4, in every release of the module (READ, RUN), and #125,
  from the same integration, calls the image the suite ran "the image Testcontainers' BigQuery module starts". So the
  suite most likely ran 0.4.4.
- **Kronikol moved the suite onto the new path, and that is Kronikol's defect.** The extension references
  `Google.Cloud.BigQuery.V2` `3.*`, which NuGet packs as the newest version of the day. Every release from 3.0.28
  (2026-06-06) to 5.14.0 asks for 3.12.0 (RUN, every published version read), and a 3.11.0 pin fails restore with NU1109 under
  central package management and NU1605 without it (RUN). Nothing in the extension needs 3.12.0: its suite passes on
  every 3.x from 3.0.0 to 3.12.0 (RUN, 13 of 13 versions, 95 of 95 facts each).
- **The underlying problem is general.** A test-time package decides which version of a production SDK the system
  under test runs, and it decides by release date. On 5.12.0, 26 floating references in 21 packages each ask for the
  newest release of their range on the day 5.12.0 was packed (RUN). Kronikol.Extensions.CosmosDB 5.12.0 asks for a
  Cosmos client published that same day. Floats are only the drifting half: 35 more dependencies reach consumers as
  fixed pins in 30 packages, set at the newest release of the day someone last moved them, and only MongoDB's two were
  measured (RUN). One is the core package's `Microsoft.AspNetCore.Mvc.Testing` 10.0.7, which every consumer takes; its
  net8.0 pin failed restore in #97's own data point.
- **The fix:** R1 pins BigQuery's floor at 3.0.0, which meets each test the MongoDB floors used (section 5.1), keeps the
  suite on the newest client through a direct test reference, runs it again at the floor in CI, and reads the floor
  back from the packed nuspec (prototyped, RUN). R2 does the same for the other 20 floating packages and R3 for the
  fixed pins, core first, behind a floor ledger that a fact checks against every packable project, so no float and no
  unrecorded pin can land. All are patches.
- **Declined:** the issue's second suggestion, an option that rewrites the emulator's timestamps in the tracking
  handler. Kronikol never changes what the application's code receives (`SQL_RESPONSE_CAPTURE_PLAN.md` L266), and the
  emulator case it would cover was fixed upstream two and a half years ago (section 5.3).
- **Left to #125:** the third suggestion. With the `/bigquery/v2/` prefix, `POST …/queries` already classifies as
  `Query`; the emulator's root lacks the prefix, which is #125's defect (section 5.4).
- **Found on the way:** three gaps in emulator 0.8.1, one of which still breaks the issue's suite on every client from
  3.6.0 (nested and repeated TIMESTAMPs; open upstream as #482, whose unmerged fix PR #492 covers two of the three
  read paths, RUN), and a test-only advisory (section 8).

---

## 2. The issue, claim by claim

| # | Claim | Verdict | Evidence |
|---|---|---|---|
| C1 | On 3.12.0, `BigQueryRow`'s indexer throws `FormatException: The input string '1698364800.0'` after `ExecuteQueryAsync(sql, parameters, new QueryOptions { UseQueryCache = false })` | **Holds on emulators up to 0.6.0 only** | RUN: Q1 throws that message on 0.4.4 and 0.6.0 with 3.12.0, and passes on 0.6.1, 0.6.6 and 0.8.1; Q12 and Q13, the call with parameters, the same on 0.4.4 and 0.8.1 (section 3.2) |
| C2 | 3.11.0 sends `jobs.insert` then `GET …/queries/{job}?formatOptions.useInt64Timestamp=true`; 3.12.0 sends `POST …/queries` with `formatOptions` in the body | **Holds** | RUN (section 3.1); READ: google-cloud-dotnet `4c3b692945` "Add stateless query fast path optimization to ExecuteQuery", in 3.12.0 |
| C3 | The emulator (0.8.1) ignores the body's option and answers `1698364800.0` | **Wrong for 0.8.1**; true for 0.6.0 and older | READ: v0.8.1 `server/handler.go` L1991-1995 reads `req.FormatOptions`; v0.6.0 L1679 reads only the URL. RUN: 0.8.1 answers `1698364800000000` (section 3.3) |
| C4 | The suite can't stay on 3.11.0: the extension asks for 3.12.0, and with central package management the pin fails restore (NU1109) | **Holds**, and without CPM it fails too (NU1605, an error by default) | RUN: `restore/results.txt`; nuget.org: every release from 3.0.28 asks for 3.12.0 (`nuget/floor-history.txt`) |
| C5 | So the floor is what moves a passing suite onto the broken path; #97 is the general case | **Holds** | Section 4 |
| C6 | The `jobs.query` call is drawn `Other`, the case of #125 | **Holds**, and with the emulator's root as `BaseUri` every other call is `Other` too, on all three clients | RUN: `results/emu0.8.1-client*-kronikol.txt`, 25 of 25 requests `Other` on 3.0.0 |
| C7 | The workaround rewrites each TIMESTAMP in a `jobs.query` answer from seconds to microseconds, into RECORD and REPEATED fields | **Not needed for top-level columns on 0.6.1 or later; not enough for nested ones on 0.8.1** | RUN: on 0.8.1 nested values come back as text (`2023-10-27 00:00:01.500000+00`), not seconds, with either client and either read path |
| S1 | Lower the floor to the oldest version the extension builds against (#97) | **Right**, and measured: 3.0.0 | Section 5.1 |
| S2 | Optionally, an option on `BigQueryTrackingMessageHandlerOptions` that does the conversion | **Declined** | Section 5.3 |
| S3 | Classify `POST …/queries` as a query (#125) | **Already true with the prefix**; the prefix is #125 | READ: `BigQueryOperationClassifier.cs` L13-16, L52 and L59-65 |

---

## 3. What was measured

### 3.1 What each client sends for `ExecuteQuery`

RUN, `results/emu0.8.1-client*-plain.txt`, the issue's call (Q1):

| Client | Requests |
|---|---|
| 3.0.0 | `POST /projects/p/jobs` (`jobs.insert`), then `GET /projects/p/queries/{job}?timeoutMs=60000`: no `formatOptions` at all. The client reads TIMESTAMPs as seconds; int64 timestamps arrived in 3.6.0 (2024-02-20). |
| 3.11.0 | `POST /projects/p/jobs`, then `GET /projects/p/queries/{job}?formatOptions.useInt64Timestamp=true&timeoutMs=60000` |
| 3.12.0 | `POST /projects/p/queries` (`jobs.query`) with `{"formatOptions":{"useInt64Timestamp":true},"query":…}` |

3.12.0 takes `jobs.query` whenever `StatelessQueryOptions.TryCreateStatelessOptions` accepts the options (READ,
`BigQueryClient.Queries.cs` L41-49 and L68-77, `StatelessQueryOptions.cs` L249-342 at `7ffaea80ef`). It keeps the job
path when the results options set `StartIndex` or `PageToken` (L255), or the query options set a job id or prefix, a
priority, a destination table, a write or create disposition, partitioning, clustering, script options, table
definitions or UDF resources (L278-293). The body's `formatOptions` defaults to int64 (L64, from `fb4b4c82b6` "Default
UseInt64Timestamp to true"). 3.11.0 already asked for int64 on `getQueryResults` and `tabledata.list` by default
(READ, `GetQueryResultsOptions.cs` L69 and `ListRowsOptions.cs` L53 at `5941905203`), and parses a TIMESTAMP at any
depth with `long.Parse` when it asked (READ, `BigQueryRow.cs` L85, L138 and L156).

### 3.2 What each emulator answers

RUN, `results/matrix.md`. Columns are emulator / client; "ok" means every TIMESTAMP read back as the right `DateTime`.

| Scenario | 0.4.4 / 3.11.0 | 0.4.4 / 3.12.0 | 0.6.0 / 3.11.0 | 0.6.0 / 3.12.0 | 0.6.1 / 3.11.0 | 0.6.1 / 3.12.0 | 0.6.6 / 3.11.0 | 0.6.6 / 3.12.0 | 0.8.1 / 3.0.0 | 0.8.1 / 3.11.0 | 0.8.1 / 3.12.0 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Q1 the issue's call (`UseQueryCache = false`) | ok | **`1698364800.0`** | ok | **`1698364800.0`** | ok | ok | ok | ok | ok | ok | ok |
| Q2 no options | ok | **`1698364800.0`** | ok | **`1698364800.0`** | ok | ok | ok | ok | ok | ok | ok |
| Q3 a TIMESTAMP literal | ok | **`1698364800.0`** | ok | **`1698364800.0`** | ok | ok | ok | ok | ok | ok | ok |
| Q4 a TIMESTAMP parameter | job failed | API 400 | job failed | API 400 | job failed | API 400 | job failed | API 400 | a STRING | a STRING | a STRING |
| Q5 RECORD and REPEATED timestamps | API 500 | `2023-10-27T00:00:01Z` | API 400 | `2023-10-27T00:00:01Z` | API 400 | API 500 | API 400 | API 500 | **text** | **text** | **text** |
| Q6 `ListRows` | `1698364800.0` | `1698364800.0` | `1698364800.0` | `1698364800.0` | API 500 | API 500 | API 500 | API 500 | top-level ok, nested **text** | same | same |
| Q7 DML insert, then select | ok | **`1698364801.0`** | ok | **`1698364801.0`** | ok | ok | ok | ok | ok | ok | ok |
| Q8 `Priority = Interactive` | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| Q9 `JobIdPrefix` | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| Q10 `UseInt64Timestamp = false` | ok | ok | ok | ok | ok | ok | ok | ok | n/a | ok | ok |
| Q11 the job path by hand | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok | ok |
| Q12 the issue's call with an INT64 parameter in the `WHERE` | ok | **`1698364800.0`** | not run | not run | not run | not run | not run | not run | not run | ok | ok |
| Q13 the same with a STRING parameter | ok | **`1698364800.0`** | not run | not run | not run | not run | not run | not run | not run | ok | ok |

A quoted value is the `FormatException`'s input string. "text" is `2023-10-27 00:00:01.500000+00`, the 0.8.1 backend's
form, which `long.Parse` rejects. Q4 to Q6 hit other emulator gaps (section 8), unrelated to the client change. Q12 and
Q13 were added in the audit, because the issue's call passes parameters and Q1 passes none
(`results/emu0.4-client*-plain-params.txt`, `results/emu0.8.1-client*-plain-params.txt`; the probe's other scenarios
ran again in those files with the same outcomes).

Three things follow:

1. **The issue's failure is the 3.12.0 column on 0.4.4 and 0.6.0 and nowhere else.** Q1, Q2, Q3 and Q7 throw the
   issue's message there, with the issue's value, and so do Q12 and Q13 on 0.4.4, the one of the two they ran on.
2. **The tracking handler plays no part.** Every outcome is the same with and without it, on 0.8.1 and all three
   clients (RUN, `results/emu0.8.1-client*-kronikol.txt` against `*-plain.txt`).
3. **Emulators up to 0.6.0 had already broken `ListRows` for every client from 3.6.0** (Q6, 3.11.0 columns). A suite on
   those images that read only through `ExecuteQuery` worked by the luck of the URL parameter.

### 3.3 Why 0.8.1 cannot give the issue's message

- Its `jobs.query` handler sets `useInt64Timestamp` from the body's `formatOptions` or the URL (READ, v0.8.1
  `server/handler.go` L1991-1995), and its `getQueryResults` handler from the URL (L1153). Both pass the flag to the
  same `internaltypes.Format` (READ, `internal/types/types.go` L224). So on 0.8.1 the two clients' paths give the same
  TIMESTAMP text for the same query, and no column can pass on 3.11.0 and fail on 3.12.0.
- `Format` writes a TIMESTAMP that was not asked for as int64 as `"%d.%06d"` (L265), so 0.8.1's seconds are
  `1698364800.000000`. `1698364800.0` is the old go-zetasqlite backend's epoch-float spelling, which the 0.7.0 backend
  switch replaced (READ, googlesqlite v0.3.1 `timestamp.go`, "the predecessor's '<unix-secs>[.frac]' epoch-float
  spelling").
- The emulator added the body read in 0.6.1 (READ, v0.6.0 L1666-1679 reads only the URL; v0.6.1 L1676-1680 reads both;
  upstream PR #276, commit `0d38e32dbf`, 2024-04-08, released 2024-04-09).

### 3.4 Which emulator the suite ran

Not knowable from here: the suite is the owner's employer's, and no copy or record of it is on this machine (searched
`C:/Code`, `Downloads`, `source`, `Documents`, `Desktop` and every session scratchpad for `bigquery-emulator`,
`useInt64Timestamp` and `1698364800`). Everything measured points to 0.6.0 or older:

- Testcontainers.BigQuery's `BigQueryBuilder.BigQueryImage` is `ghcr.io/goccy/bigquery-emulator:0.4`, marked
  `[Obsolete]` with the parameterless constructor that uses it (READ, testcontainers-dotnet `develop` at `5b9feb0bcc`,
  `src/Testcontainers.BigQuery/BigQueryBuilder.cs` L8 and L17-20). Every release of the module, 3.7.0 to 4.16.0
  (2026-10-09), has that default; the two `[Obsolete]` marks arrived in 4.10.0 (2026-01-01), so on a current release a
  suite that takes the default gets warning CS0618 and still runs (READ, the file at each release tag). That image
  reports `v0.4.4 (61e8d90)` (RUN).
- #125, filed by the owner from the same integration, says 0.8.1 is "the image Testcontainers' BigQuery module starts".
  That is how a suite that takes the module's default, rather than naming an image, would describe it, and the default
  is 0.4. kronikol-5d has taken the correction into `EMULATOR_URL_SHAPES_PLAN.md` as its claim C11.
- No later emulator explains the message either. The newest release is still 0.8.1 (2026-06-13), and the two commits on
  `main` after it, to 2026-06-14, change documentation, dataset resolution, CSV loads and the Storage API, not the body
  read or `Format` (READ, `ac6e3a8e68`, `cb8555bee7`).
- The issue's call passes parameters, and the probe's Q1 passed none. With an INT64 or a STRING parameter in the
  `WHERE`, 0.8.1 answers in microseconds on both clients, and 0.4.4 throws the issue's message on 3.12.0 only (RUN, Q12
  and Q13).

R1 does not depend on which image it was. The reply on #205 (Q6) and the wiki's advice do, and both are worded on what
was measured.

### 3.5 How the floor moved

RUN, `nuget/floor-history.txt`: Kronikol.Extensions.BigQuery 3.0.0 to 3.0.27 asked for Google.Cloud.BigQuery.V2 3.11.0,
and 3.0.28 (`v3.0.28`, 2026-06-06) to 5.14.0 ask for 3.12.0, published on nuget.org on 2026-05-30. The extension's
source did not change for it: the float resolved to the new release a week after Google shipped it. No client after
3.12.0 has been published as of 2026-10-27, and none of the 26 floating references packed a different floor in 5.12.1,
5.13.0 or 5.14.0, all three packed on the same day as 5.12.0 (RUN, nuget.org).

Restore, RUN (`restore/`): a project that pins 3.11.0 beside the 5.12.0 extension fails with
`NU1109: Detected package downgrade: Google.Cloud.BigQuery.V2 from 3.12.0 to centrally defined 3.11.0` under central
package management with transitive pinning, and with `NU1605` (warning as error) without it.

### 3.6 What a suite on an old emulator can do without Kronikol

On 0.4.4 with 3.12.0, three per-call settings keep the read working (RUN, Q8 to Q10): `Priority = Interactive`, which
is BigQuery's default anyway, a `JobIdPrefix`, or `GetQueryResultsOptions.UseInt64Timestamp = false`. Each is a change to
the application's own query code for the sake of a test emulator, so the plan does not recommend them; the wiki names
the cheaper fix, a current image (section 5.5).

---

## 4. The underlying problem

Kronikol's packages are test-time packages, but their dependencies are the system under test's dependencies. A suite
that hosts its services in-process (`WebApplicationFactory`, as #205's ten-service monorepo does) runs the services'
code against whatever client version the test project resolves. When a Kronikol package asks for a higher client than
the application ships with, one of two things happens:

1. restore fails (NU1109 or NU1605), as it did for #205's suite and for the six packages in #97's 2026-10-07 data
   point; or
2. the suite raises its pin and from then on tests its services against a client they do not ship with, as #205's suite
   did, which moved it onto a REST call its emulator mishandled.

A floating reference makes Kronikol's choice "the newest release on the day the package was packed". So every Kronikol
release can raise a production SDK version in its consumers' tests for no reason the release had. RUN,
`nuget/floors-5120.txt`: on 5.12.0, every one of the 26 floating references asks for the newest release of its range
that day. Nine of them ask for a release under two weeks old:

| Package | Client | 5.12.0 asks for | Published |
|---|---|---|---|
| Kronikol.Extensions.CosmosDB | Microsoft.Azure.Cosmos `3.*` | 3.63.3 | 2026-10-27, the day of 5.12.0 |
| Kronikol.Extensions.DispatchProxy, MediatR, ServiceBus | Microsoft.Extensions.DependencyInjection.Abstractions `10.*` | 10.0.14 | 2026-10-25 |
| Kronikol.Extensions.EfCore.Relational | Microsoft.EntityFrameworkCore.Relational `10.*` | 10.0.14 | 2026-10-25 |
| Kronikol.Extensions.Sqlite | Microsoft.Data.Sqlite `10.*` | 10.0.14 | 2026-10-25 |
| Kronikol.Extensions.OpenTelemetry | OpenTelemetry `1.*`, OpenTelemetry.Exporter.InMemory `1.*` | 1.20.1 | 2026-10-20 |
| Kronikol.Extensions.Oracle | Oracle.ManagedDataAccess.Core `23.*` | 23.26.400 | 2026-10-20 |

**A fixed pin is the same defect, frozen.** RUN, `nuget/fixed-pins.txt`: 39 `PackageReference`s in packable projects
have a fixed version, and 35 of them reach consumers as the nuspec floor, in 30 packages (the four in StepTracking and
AssertionTracking are bundled). Only MongoDB's two were measured. The rest were set at the newest release on the day
someone last moved them, and each still lifts a consumer on an older release of the same major, which restore reports
as NU1605 or NU1109, as it does for a float. #97's own data point has one: on net8.0 the core package asked for
`Microsoft.AspNetCore.Mvc.Testing` ≥ 8.0.26, a fixed pin (#97: "the net8 and net9 pins are fixed"), against the
solution's 8.0.14, and restore failed (#97, comment of 2026-10-07). The pins every consumer, or every consumer of a
test framework, meets:

| Package | Pin | Older releases of that major | Pin published |
|---|---|---|---|
| Kronikol (core) | Microsoft.AspNetCore.Mvc.Testing 10.0.7 | 7 (10.0.0 to 10.0.6) | 2026-04-21 |
| Kronikol.TUnit | TUnit.Core 1.43.11 | 172 | 2026-05-02 |
| Kronikol.NUnit4 | NUnit 4.6.0 | 12 | 2026-05-03 |
| Kronikol.MSTest | MSTest.TestFramework 4.2.2 | 5 | 2026-04-30 |
| Kronikol.xUnit3 | xunit.v3.extensibility.core 3.2.2 | 5 | 2026-01-14 |
| Kronikol.ReqNRoll.Core | Reqnroll 3.3.4 | 13 | 2026-03-23 |
| Kronikol.LightBDD.Core | LightBDD.Framework 3.12.0 | 20 | 2026-03-28 |
| Kronikol.Playwright | Microsoft.Playwright 1.59.0 | 70 | 2026-04-09 |

The SDK packages have the same shape: `AWSSDK.S3` 4.0.23 has 107 older 4.x releases, `Npgsql` 10.0.2 two, and so on
through the table in the file.

The float exists because it lets CI test the newest client for free (#97, "Why it is this way"). That benefit belongs to
the test projects, which are never packed, and MongoDB already moved it there in 4.20.2: the package asks for its floor,
the test project references the newest (`MONGODB_DETAILED_PAYLOADS_PLAN.md` Q18, `MongoDbPackageTests`).

So the fix is not a lower number for BigQuery alone. It is a rule: **every dependency a packable project hands its
consumers is a measured floor, and only test projects float.** A float breaks the rule anew on every release; a fixed
pin breaks it once, on the day it is set, and stays broken. BigQuery is the first package to follow it after MongoDB,
because #205 measured the harm.

---

## 5. The fix

### 5.1 R1: the BigQuery package asks for 3.0.0 (a patch)

**The floor.** MongoDB's floors were chosen by three tests (`MONGODB_DETAILED_PAYLOADS_PLAN.md` Q18): the oldest
version the suite passes on, raised past any version whose dependencies carry an advisory, raised again until a
consumer on a later version can compile against it. For BigQuery all three land on 3.0.0, the first 3.x (2022-06-08):

| Test | Result at 3.0.0 | Evidence |
|---|---|---|
| The suite passes | 95 of 95, and on each of the 13 releases from 3.0.0 to 3.12.0 | RUN, `floor/floor.txt` |
| No advisory in the resolved graph | none: restore's audit is silent at every version, and `dotnet list package --vulnerable --include-transitive` lists nothing at 3.0.0 or 3.12.0. The audit does run: the same restore flags AngleSharp in `Kronikol.Tests` (F6) | RUN, `floor/floor.txt`, `floor/vulnerable.txt`, `floor/core.txt`; the graph is GAX 4.0.0, Google.Apis 1.57.0, Newtonsoft.Json 13.0.3 (lifted by Kronikol itself) |
| A consumer on a later client compiles | yes: Google's assemblies are strong-named (token `185c282632e132a0`), so the compiler unifies 3.0.0.0 with 3.12.0.0; a project on 3.12.0 built against the extension compiled at 3.0.0 with no warning and gave the same eleven outcomes | RUN, `probe/consumer-floor/`, `results/emu0.8.1-consumer3.12.0-ext-floor3.0.0-kronikol.txt` |
| The real client runs through the handler | yes, 3.0.0 against emulator 0.8.1 | RUN, `results/emu0.8.1-client3.0.0-*.txt` |

The extension touches the client in two places only, `BigQueryClientBuilder.HttpClientFactory` and
`Google.Apis.Http.HttpClientFactory.CreateHandler` (READ, `BigQueryClientBuilderExtensions.cs` L14-20,
`TrackingBigQueryHttpClientFactory.cs` L19-23), and both are in 3.0.0. A 2.x floor is out of scope: every published
release of the package has asked for 3.11.0 or 3.12.0 (`nuget/floor-history.txt`), so none has served a 2.x consumer.

The alternatives, for Q1: 3.11.0 restores the pre-3.0.28 value and serves exactly #205's suite, nothing older; 3.6.0
is the first client to ask for int64 timestamps, a behavioural line that matters to emulators, not to the extension.
Neither is needed by anything the extension calls.

No upper bound, as MongoDB's pins have none: a consumer on a future 4.x would be outside anything tested either way,
and NuGet's convention is a floor.

**The changes**, in TDD order:

1. **Red.** `tests/Kronikol.Tests.BigQuery/BigQueryPackageTests.cs`, modelled on `MongoDbPackageTests` (L17-87): pack
   the extension with `--no-build`, read every dependency group's `Google.Cloud.BigQuery.V2` version from the nuspec,
   expect `3.0.0`. Today it reads `3.12.0` and fails.
2. **Green.** `src/Kronikol.Extensions.BigQuery/Kronikol.Extensions.BigQuery.csproj` L12:
   `Version="3.*"` becomes `Version="3.0.0"`, with a comment in the style of the MongoDB csproj (why this floor, what
   was measured, that `BigQueryPackageTests` reads it back).
3. **Keep the suite on the newest client.** `tests/Kronikol.Tests.BigQuery/Kronikol.Tests.BigQuery.csproj` gets
   `<PackageReference Include="Google.Cloud.BigQuery.V2" Version="3.*" Condition="'$(FloorRun)' != 'true'" />`.
   Without it the suite would run on 3.0.0 from then on, since NuGet picks the lowest version that satisfies the floor.
4. **Run it at the floor too (Q3).** With `-p:FloorRun=true` the direct reference drops out and the suite runs on
   whatever the extension asks for. The `test` job's auto-discovered matrix entry in `ci.yml` (L84-85, its loop at
   L316-330) gains one step, under the same `if: matrix.discover-remaining`, that runs every test project whose csproj
   mentions `$(FloorRun)`, found by `grep -l`, so R2's and R3's projects join without a CI edit:
   `dotnet test "$dir" --configuration Release -p:FloorRun=true -- RunConfiguration.TreatNoTestsAsError=true`.
   The suite takes about a second (RUN); the cost is one restore and build. The step goes after the loop, because the
   switch rewrites the test project's assets file. Projects with a matrix entry of their own (the loop's `SKIP` list)
   never reach that entry; an R2 or R3 project among them gets the same step in its own entry. *As built in R1:* the
   floor run went into both of the job's test steps, the loop over a matrix entry's `projects` as well as the
   auto-discovered loop, each after its own loop, so a project with an entry of its own joins without a CI edit too;
   `FloorRunTests` holds both, and that a project naming the switch drops a reference under it.
5. **`tests/Kronikol.Tests`** references the extension (`Kronikol.Tests.csproj` L36) for `HttpContextAccessorOptionsTests`
   and has no client reference of its own, so after step 2 it resolves 3.0.0. RUN, `floor/core.txt`: it builds there,
   and the facts that touch the extension (`HttpContextAccessorOptionsTests` and every test named for BigQuery, 9 in
   all) pass. Leave it so: it is a second, free floor run.
6. **Docs.** The wiki's BigQuery page gains "Client versions and emulators" after "Install" (draft in
   `drafts/wiki-client-versions-section.md`, section 5.5); the changelog entry follows 4.20.2's shape (draft in
   `drafts/changelog-r1.md`), with the behaviour change stated: a project that takes the client only through Kronikol now
   resolves 3.0.0.

The bump is a **patch**: a packaging fix with no new option or member, like 4.20.2.

**Proving R1.** `BigQueryPackageTests` red on the parent commit and green after. The floor run green. `floor/floor.txt`
regenerated with `floor_probe.sh` on the release commit for 3.0.0 and the newest 3.x. The consumer probe
(`probe/consumer-floor`) against the packed `.nupkg` instead of the project reference, on client 3.11.0 and 3.12.0,
which is #205's suite's case.

**Prototyped** in the audit and reverted (RUN, `prototype/r1.patch`, `prototype/runs.txt`): steps 1 to 3 as written,
and step 4's switch run locally (the CI step itself is not in the patch).
The package test fails on today's tree with `Expected: "3.0.0"`, `Actual: "3.12.0"` and passes with the pin; the suite
then passes 96 of 96 on 3.12.0 through the direct reference and 96 of 96 on 3.0.0 with `-p:FloorRun=true`, and neither
restore warns.

### 5.2 R2 and R3: every dependency a package hands its consumers is a measured floor (#97; patches)

**The guard first: a floor ledger.** `tests/Kronikol.Tests/Packaging/FloorLedger.cs`, a list in code with one entry per
dependency a packable `src/` project hands its consumers: the project, the package, the version, and either the
evidence (the plan or harness that measured it) or the batch that will. Three static facts read every packable csproj,
with no pack:

1. no consumer-facing `PackageReference` has a `Version` containing `*`, except entries marked still floating, each
   with its R2 batch;
2. every consumer-facing fixed `PackageReference` has an entry with the same version, so moving a pin means editing the
   ledger in the same change, in one place a reviewer reads;
3. the entries marked still floating, and those marked unmeasured with their R3 batch, are no more than two counts
   written beside the ledger (25 and 33 after R1), which each batch lowers and no change may raise; and a
   still-floating entry whose csproj no longer floats fails. So the marks can only go, and the ledger ends with
   evidence on every line.

"Consumer-facing" leaves out `PrivateAssets="all"` and what a package bundles, as StepTracking and AssertionTracking
bundle Mono.Cecil. Test projects may float; the guard does not read them. Written red against an empty ledger, the
facts fail on today's tree; after R1 the ledger holds 61 entries: 25 still floating, 33 unmeasured pins, and three
measured floors (BigQuery's and MongoDB's two).

**The method, per package**, as R1, for a float and a pin alike: `floor/floor_probe.sh` generalised to take the package id, the extension's csproj
and its test project; the four tests of section 5.1; the test project's direct newest reference under
`'$(FloorRun)' != 'true'`; the package's wiki page names its floor. The Kafka, EF Core Relational and TcpTap suites start
their own servers through Testcontainers, so their floor runs need Docker or podman; the other R2 suites are unit suites
with stubs, like BigQuery's, so their fourth test (the real client through the handler) needs a probe of its own where
the client can reach a local server or emulator.

**The batches**, each a patch. R2a takes the floating packages consumers have already hit (#97's 2026-10-07 data point
names Kafka, gRPC and Cloud Storage, besides BigQuery, MongoDB and the core package, whose pin R3a takes) and the Google
packages that share their GAX and gRPC dependencies. The R3 batches take the fixed pins, the ones every consumer meets
first:

| Batch | Packages and references |
|---|---|
| R2a, consumers' first | Kafka (`Confluent.Kafka` `2.*`), Grpc (`Grpc.Net.Client` `2.*`, `Google.Protobuf` `3.*`), CloudStorage (`Google.Cloud.Storage.V1` `4.*`), Bigtable (`Google.Cloud.Bigtable.V2` `3.*`), PubSub (`Google.Cloud.PubSub.V1` `3.*`), Spanner (`Google.Cloud.Spanner.Data` `5.*`, `Google.Cloud.Spanner.V1` `5.*`, `Grpc.Core.Api` `2.*`) |
| R2b, Azure | BlobStorage (`Azure.Storage.Blobs` `12.*`), StorageQueues (`Azure.Storage.Queues` `12.*`), ServiceBus (`Azure.Messaging.ServiceBus` `7.*`, `Microsoft.Extensions.DependencyInjection.Abstractions` `10.*`), EventHubs (`Azure.Messaging.EventHubs` `5.*`), CosmosDB (`Microsoft.Azure.Cosmos` `3.*`) |
| R2c, the rest | Kafka.BuildInterception (`Lib.Harmony` `2.*`), TcpTap (`MongoDB.Bson` `2.*`), Redis (`StackExchange.Redis` `2.*`), Oracle (`Oracle.ManagedDataAccess.Core` `23.*`), OpenTelemetry (`OpenTelemetry` `1.*`, `OpenTelemetry.Exporter.InMemory` `1.*`), DispatchProxy and MediatR (`Microsoft.Extensions.DependencyInjection.Abstractions` `10.*`), EfCore.Relational (`Microsoft.EntityFrameworkCore.Relational` `10.*`), Sqlite (`Microsoft.Data.Sqlite` `10.*`) |
| R3a, the core and the test frameworks | Kronikol (`Microsoft.AspNetCore.Mvc.Testing` 10.0.7), xUnit2 (`xunit.extensibility.execution` 2.9.3), xUnit3 (`xunit.v3.extensibility.core` 3.2.2), NUnit4 (`NUnit` 4.6.0), MSTest (`MSTest.TestFramework` 4.2.2), TUnit (`TUnit.Core` 1.43.11), ReqNRoll.Core (`Reqnroll` 3.3.4), LightBDD.Core, .TUnit, .xUnit2 and .xUnit3 (`LightBDD.*` 3.12.0 and 3.12.1, and `xunit` 2.9.3), BDDfy.xUnit3 (`TestStack.BDDfy` 8.0.1.3), Playwright (`Microsoft.Playwright` 1.59.0) |
| R3b, the SDK clients | S3, DynamoDB, SNS, SQS and EventBridge (`AWSSDK.*` 4.0.x), Npgsql (10.0.2), SqlClient (`Microsoft.Data.SqlClient` 7.0.1), Elasticsearch (`Elastic.Clients.Elasticsearch` 9.4.0), MassTransit (9.1.0), MediatR (`MediatR` 14.1.0), MySqlConnector (2.5.0), ClickHouse.Client (7.14.0), ClickHouse.Driver (1.4.0), ClickHouse.Octonica (`Octonica.ClickHouseClient` 3.1.10) |
| R3c | PlantUml.Ikvm (`IKVM`, `IKVM.Image`, `IKVM.Image.JDK`, `IKVM.Image.JRE` and `IKVM.MSBuild` 8.15.0), which consumers rarely reference themselves |

Known in advance, from `nuget/floors-5120.txt` and #97:

- The three `10.*` families follow the .NET 10 servicing band. Their floor is likely 10.0.0, which is what an app on any
  .NET 10 runtime has; measure it like the rest.
- `Lib.Harmony` patches IL at build time and `MongoDB.Bson` decodes MongoDB's wire format, the two places where #97
  notes "newest is what was tested". Their floors need the suites' full runs at each candidate, not a compile.
- Three floats sit behind a newer major the package does not cover: `Google.Cloud.Storage.V1` 5.0.0 (2026-09-21),
  `StackExchange.Redis` 3.4.20 (2026-10-23) and `MongoDB.Bson` 3.13.0. A consumer on those restores today, as there is no
  upper bound, and nothing tests it. That is a question of support, not of floors, and it stays out of R2 (Q2).
- `Microsoft.AspNetCore.Mvc.Testing` follows the same .NET 10 servicing band, so its floor is likely 10.0.0 too. It
  goes first in R3a because every consumer takes the core package.
- A test framework adapter calls into the framework's extensibility points, and TUnit.Core has had 172 releases in
  its 1.x line before the pin. An adapter's floor may stay close to its pin; the measurement decides, and a floor that
  stays where it is is a measured floor all the same.

R2 and R3 need their own measurements before each batch ships; this plan fixes the method and the guard, not the
numbers.

**Not chosen: central package management**, #97's third option. RUN, `restore/cpm-float/`: under
`ManagePackageVersionsCentrally`, restore rejects a central floating version (`NU1011: The following PackageVersion
items cannot specify a floating version`) unless a project sets `CentralPackageFloatingVersionsEnabled`, so a test
project could still float through that property and a `VersionOverride`. CPM can therefore express the rule, but it
cannot check it: it has no notion of a packable project or of a measured floor, so the ledger's facts would still be
needed. And it would move every version in the repo into `Directory.Packages.props`, a far larger change than the
ledger, which already makes the floors one list to review.

### 5.3 Declined: an option that rewrites timestamps in the handler

The issue suggests an option on `BigQueryTrackingMessageHandlerOptions` that converts the emulator's seconds to
microseconds. The plan does not propose it:

1. **It is the wrong layer.** The tracking handler observes; it records what the emulator sent. Rewriting the reply
   would make a tracked run behave differently from an untracked one, and both differently from production. 5.11.1 was
   released to remove exactly that from SQL capture: "Kronikol never changes what the application's code receives, and
   never throws into it" (`SQL_RESPONSE_CAPTURE_PLAN.md` L266).
2. **The case it covers is fixed upstream.** Only emulators older than 0.6.1 (April 2024) need it, and the fix there is
   a newer image tag, one line in the suite.
3. **It would not cover what 0.8.1 still gets wrong.** Nested and repeated TIMESTAMPs come back as text on 0.8.1 with
   either client; a seconds-to-microseconds rewrite does not parse that form. Covering it would turn the option into a
   general emulator shim inside a tracking package.
4. **It is new public surface** (a minor bump) for one emulator's old releases, and the report would then show either
   the emulator's reply or the rewritten one, each misleading someone.

The wiki section (5.5) says what to do instead, and the reply on #205 says why (Q6).

### 5.4 Left to #125

The classifier recognises `/bigquery/v2/projects/{p}/…` only (`BigQueryOperationClassifier.cs` L13-16), and an emulator's
root sends `/projects/{p}/…`, so every call is `Other` (RUN, all three clients) and `Summarised` drops them (handler
L45-47). With the prefix, `POST …/queries` is already `Query` (L52, L59-65), so the issue's third suggestion needs no rule
of its own. kronikol-5d's `EMULATOR_URL_SHAPES_PLAN.md` owns the classifier, the handler's `Other` handling and the
wiki page's operation tables; it cites this harness for the two clients' request shapes. One fact for it, sent on
2026-10-27: on 3.11.0 a query is two arrows (`jobs.insert`, which the classifier calls `Create`, then the query
results), and on 3.12.0 one (`Query`), so the diagram's shape changes with the client version.

The two plans touch no common source file. Both edit `CHANGELOG.md` and `Integration-BigQuery-Extension.md`, in
different sections, and either can ship first (Q7).

### 5.5 Documentation

`Integration-BigQuery-Extension.md` says nothing today about client versions or emulators (wiki L1-302). R1 adds
"Client versions and emulators" after "Install" (`drafts/wiki-client-versions-section.md`):

- the floor, and that a suite can pin any 3.x;
- that 3.12.0 runs `ExecuteQuery` as `jobs.query` when the options allow, and what keeps the job path;
- that the emulator reads the body's option from 0.6.1, that older images answer in seconds with the issue's
  exception, and that Testcontainers' parameterless builder starts 0.4.4, with the one-line fix;
- 0.8.1's three gaps (section 8);
- that the handler records replies as sent and changes nothing the client receives.

R2 and R3 add each package's floor to its own wiki page. `README.md` L275 and `nuget-readme.md` make no version claim and
need no change.

---

## 6. Releases

| Release | Content | Bump |
|---|---|---|
| R1 | Section 5.1, the AngleSharp fix (section 8) and the wiki section | patch |
| R2a, R2b, R2c | Section 5.2's floats, one batch each, the ledger with R2a | patch each |
| R3a, R3b, R3c | Section 5.2's fixed pins, one batch each | patch each |

Numbers are taken at push, as peers ship in between. R1 and #125's first release are independent; if both are green-lit
together they can share one patch release (Q7).

## 7. Tests

| Test | Proves | Red on |
|---|---|---|
| `BigQueryPackageTests.The_packed_extension_asks_for_its_measured_client_floor` | the nuspec asks for 3.0.0 | 5.12.0 (asks 3.12.0) |
| The BigQuery suite with `-p:FloorRun=true` in CI | the extension works on its floor, not only compiles | a floor raised by a call into a newer API |
| The BigQuery suite without it | it still runs on the newest 3.x | a missing direct reference (it would run on 3.0.0) |
| The floor ledger's three facts | no packable project floats, every consumer-facing pin is in the ledger at its version, and the still-floating and unmeasured marks only go | today's tree: no ledger; after R1, 25 still floating and 33 unmeasured |
| Each R2 and R3 package's floor run and nuspec check | as R1 | each batch's parent commit |

No Playwright fact: nothing in a report changes.

## 8. Found on the way

| Finding | Where | Action |
|---|---|---|
| F1 | Emulator 0.8.1 returns a TIMESTAMP in a RECORD or a REPEATED field as text when int64 was asked for, so every client from 3.6.0 throws on it, on every read path (RUN, Q5, Q6, `results/emu0.8.1-nested-timestamp-curl.txt`). `Format` converts only top-level string cells (READ, `types.go` L224-243). Already open upstream as #482 (2026-05-23, found with the Go client, RECORD only); #218, the same symptom on the old backend, was closed on 2026-05-18. Its fix, PR #492 (open since 2026-06-15, no review), was built from its head and run here: nested values read right through `jobs.query` and `tabledata.list`, but `getQueryResults`, the job path that 3.11.0 and 3.12.0's opt-outs take, still returns text (RUN, `results/emupr492-client*-plain.txt`). The head does not build as it stands (grpc v1.81.1 against x/net v0.54.0: `undefined: http2.TrailerPrefix`); it was built with grpc moved to its newest release. | upstream | Wiki; posted on 2026-10-28 as a comment on #482 and a note on PR #492 (Q5; `drafts/posted-482-comment.md`, `drafts/posted-492-comment.md`) |
| F2 | Emulator 0.8.1 ignores a query parameter's declared type, so a TIMESTAMP parameter comes back a STRING column (RUN, Q4). Already open upstream as #511, with a fix open as PR #516; PR #492 leaves it (RUN, Q4). | upstream | Wiki |
| F3 | Emulator 0.8.1's `tabledata.list` returns the first table of that name from any dataset: `ds_curl_081.t` (three columns, one row) read back as another dataset's `t` (four columns, two rows), while `tables.get` and `jobs.query` read the right table (RUN). No upstream issue found; the nearest is #470 (rows streamed into one of two same-named tables in different projects go missing), which PR #492 closes, and PR #492 still shows F3: the 3.12.0 run's `ListRows` read the 3.11.0 run's two rows (RUN). The probe's Q6 on 0.8.1 read the 3.11.0 run's table, which has the same columns, so its results stand. | upstream | Wiki; filed on 2026-10-28 as goccy/bigquery-emulator#532 (Q5; `drafts/posted-f3-issue.md`, repro `emulator/same-name-tables-curl.sh`, `results/emu0.8.1-same-name-tables-curl.txt`) |
| F4 | Emulators up to 0.6.0 broke `ListRows` for every client from 3.6.0 (RUN, Q6). | upstream, fixed in 0.6.1 | Wiki (covered by "use 0.6.1 or later") |
| F5 | The Windows build of emulator 0.8.1 does not start: "failed to create analyzer: … wasm trap: invalid memory address or nil pointer dereference" (RUN). | upstream | Noted in the harness only |
| F6 | `tests/Kronikol.Tests` takes AngleSharp 1.4.0 (`Kronikol.Tests.csproj` L14) and the example's integration tests 1.3.0 (`Example.Api.Tests.Integration.csproj` L13), both under GHSA-pgww-w46g-26qg (moderate, mXSS), patched in 1.5.0 (RUN, NU1902 on restore). Test projects only, nothing shipped. | Kronikol | Both move to 1.5.0 in R1 |

## 9. Risks and assumptions

| # | Assumption or risk | If wrong |
|---|---|---|
| A1 | The suite in #205 ran an emulator older than 0.6.1 (inferred, section 3.4: the module's default in every release is 0.4, #125 calls the suite's image the module's default, and no 0.8.1 build or call shape gives the message) | R1 is unaffected. The reply on #205 says "may be on 0.4.4" and gives the measurement, so it stays true either way. |
| A2 | Consumers reference the client themselves; one that takes it only through Kronikol resolves 3.0.0 after R1 | Stated in the changelog's behaviour change, as 4.20.2 did; such a project references the client to get a newer one |
| A3 | GAX 4.0.0 and Google.Apis 1.57.0 stay free of advisories | A new advisory shows as NU1902 or NU1903 in the floor run, and the floor moves past it deliberately |
| A4 | Future extension code may want an API newer than 3.0.0 | It fails to compile against the floor, which is the guard doing its job; raise the floor in the same change and say so |
| A5 | NuGet keeps reading a fixed version as `>=` | Has been NuGet's rule since its first release; the nuspec test would show any change |

## 10. Questions for the owner

Each has a recommendation; none is legwork.

1. **The BigQuery floor.** 3.0.0 (recommended: it meets all four tests and frees every 3.x consumer), 3.6.0, or 3.11.0.
2. **Scope.** R1 now, then R2's three float batches and R3's three pin batches behind the one ledger (recommended:
   a pin hurts a consumer exactly as a float does, and #97's own data point failed on the core package's pin); R1 and
   R2 only, leaving the fixed pins as they are; or R1 alone with #97 planned separately. Supporting a client's next
   major (Storage 5.x, Redis 3.x, Bson 3.x) stays out either way.
3. **A floor run in CI**, the `FloorRun` switch and one auto-discovering step (recommended), or rely on compiling at the
   floor and the nuspec test only, as MongoDB does today.
4. **Declining the timestamp-rewrite option** (recommended, section 5.3), or building it anyway as an opt-in.
5. **Upstream posts to goccy/bigquery-emulator**, under your name: a comment on #482 and PR #492 for F1, saying which
   read path the PR still misses and that its head needs a newer grpc to build, and a new issue for F3 (recommended:
   both, drafts ready); the comment alone; or neither.
6. **The reply on #205** (`drafts/issue-205-comment.md`): post it when R1 ships and close #205 then (recommended),
   keeping #97 open until R2's last batch; or post it now.
7. **Release order with #125**: either first, or both in one patch if both are green-lit together (recommended: one
   patch, since both are BigQuery fixes a consumer of the emulator needs at once). `EMULATOR_URL_SHAPES_PLAN.md`'s Q2
   points here, so one answer settles both plans.
8. **Roadmap.** Place #205 at 1.34, next to #125's 1.33 (recommended), or leave it out of `ROADMAP.md` as the SQL capture
   plan was.

## 11. Log

- **2026-10-27, written** at 5.12.0 (`1ee6373a`).
- **2026-10-27, audited** at 5.14.0 (`8d7d1e1d`), at the owner's ask. 5.12.1, 5.13.0 and 5.14.0 touch no file the plan
  cites, and no packed floor moved. Found and added:
  - the fixed pins (section 4), and with them R3 and a floor ledger in place of a guard on floats alone (section 5.2,
    Q2);
  - F1 is open upstream as #482, and its fix, PR #492, was built and run: it misses `getQueryResults`, and F3 survives
    it (section 8, Q5);
  - Testcontainers' default image in every release, and #125's wording, behind A1 (section 3.4);
  - Q12 and Q13, the issue's call with parameters (section 3.2);
  - R1 prototyped and reverted (section 5.1);
  - central package management, #97's third option, measured and not chosen (section 5.2).

  Checked and unchanged: no BigQuery client after 3.12.0 and no emulator release after 0.8.1; the wiki page is as it was
  at `ae3393f`; and Kronikol's `HttpContentReader` decodes the client's gzipped request bodies, so the notes show them
  as JSON.
- **2026-10-28, green-lit** by the owner ("Can you implement the plan in full"): every question in section 10 at its
  recommendation. Q1 3.0.0; Q2 R1, R2 and R3 behind the ledger; Q3 the floor run in CI; Q4 the option declined; Q5 both
  upstream posts; Q6 the reply on #205 when R1 ships, #97 open until the last batch; Q7 R1 alone, as #125's plan is not
  green-lit; Q8 row 1.34, beside #97's 1.11, with D21 taken as option 1.
- **2026-10-28, R1 released as 5.22.1**, rebased onto 5.22.0. Between the audit and R1, 5.16.1 and 5.17.0 changed
  `BigQueryTrackingMessageHandler.cs` (a phase's own verbosity, the operation key) and added three facts to its suite, and
  no release moved a package reference. As built, beyond section 5.1:
  - the floor run is in both of the `test` job's steps, not only the auto-discovered one (step 4's note), and
    `FloorRunTests` holds it: red on 5.22.0's `ci.yml`, and red when the BigQuery csproj names the switch without
    dropping a reference under it;
  - `PackedNuspec` (`tests/Kronikol.Tests/Packaging/`) is the pack-and-read helper both `BigQueryPackageTests` and
    `MongoDbPackageTests` link, so R2 and R3 add one fact per package rather than one copy of it.

  Run: `BigQueryPackageTests` red on 5.22.0 (`Expected: "3.0.0"`, `Actual: "3.12.0"`) and green after; the suite 99 of
  99 on 3.12.0 and 99 of 99 on 3.0.0 with `-p:FloorRun=true`, neither restore warning (`floor/floor-r1.txt`); the
  acceptance against the packed extension (`r1-acceptance.sh`, `results/r1-acceptance.txt`): the nuspec asks for
  3.0.0, `restore/cpm-r1` and `restore/direct-r1` restore 3.11.0 where 5.12.0 failed with NU1109 and NU1605, and the
  probe on clients 3.11.0 and 3.12.0 through the packed extension against emulator 0.8.1 gives every outcome the
  published extension gave. Then the core suite (11,348 passed, 2 skipped), which resolves 3.0.0 through its reference
  to the extension, both MongoDB suites' floor facts, the Release build of `release.slnf` (its 30 advisory warnings
  all older: MongoDB.Driver 2.28.0's Snappier and SharpCompress, and System.Security.Cryptography.Pkcs through the Oracle
  and SqlClient clients) and the Integration (NUnit4) lane, 30 of 30, which reads its reports with AngleSharp 1.5.0.
- **2026-10-28, Q5 posted:** the comment on goccy/bigquery-emulator#482 (the .NET client, REPEATED as well as RECORD), the
  note on PR #492 (`getQueryResults` still returns text; its head needs a newer grpc to build), and F3 filed as #532 with
  a client-free repro (`emulator/same-name-tables-curl.sh`), run again on a fresh 0.8.1.

---

## Appendix A. R1 edit sites (at `1ee6373a`)

| File | Line | Change |
|---|---|---|
| `src/Kronikol.Extensions.BigQuery/Kronikol.Extensions.BigQuery.csproj` | 12 | `3.*` to `3.0.0`, with the floor's comment |
| `tests/Kronikol.Tests.BigQuery/Kronikol.Tests.BigQuery.csproj` | 8-21 | direct `Google.Cloud.BigQuery.V2` `3.*` under `'$(FloorRun)' != 'true'` |
| `tests/Kronikol.Tests.BigQuery/BigQueryPackageTests.cs` | new | after `tests/Kronikol.Tests.MongoDB/MongoDbPackageTests.cs` L17-87 |
| `.github/workflows/ci.yml` | after 330 | the floor-run step for `discover-remaining` |
| `tests/Kronikol.Tests/Kronikol.Tests.csproj` | 14 | AngleSharp 1.5.0 |
| `examples/Example.Api/tests/Example.Api.Tests.Integration/Example.Api.Tests.Integration.csproj` | 13 | AngleSharp 1.5.0 |
| `CHANGELOG.md` | top | the R1 entry |
| wiki `Integration-BigQuery-Extension.md` | after 19 | "Client versions and emulators" |
| `plans/PLANS_STATUS.md` | this plan's row | the release |

## Appendix B. Upstream sources

| Source | Where |
|---|---|
| The 3.12.0 changes | google-cloud-dotnet `apis/Google.Cloud.BigQuery.V2/docs/history.md`; commits `28f65aec9a` (stateless query execution), `4c3b692945` (the fast path in `ExecuteQuery`), `ff9f80e81e` (stateless queries made internal), `fb4b4c82b6` (int64 by default), release `7ffaea80ef` |
| The emulator's body read | goccy/bigquery-emulator PR #276, commit `0d38e32dbf` (2024-04-08), first in v0.6.1 (2024-04-09) |
| The emulator's formatter | v0.8.1 `internal/types/types.go` L224-265; v0.6.6 `internal/types/types.go` L187-210 |
| The backend's timestamp forms | goccy/googlesqlite v0.3.1 `timestamp.go` `TimeFromTimestampValue` |
| Testcontainers' default image | testcontainers-dotnet `develop` `5b9feb0bcc`, `src/Testcontainers.BigQuery/BigQueryBuilder.cs` L8, L17-20; the same file at every release tag, 3.7.0 to 4.16.0, `[Obsolete]` from 4.10.0 |
| The nested TIMESTAMP report and its fix | goccy/bigquery-emulator #482 (open); PR #492 (open), head `c80afb507e` on lraigosov/bigquery-emulator `feat/maintenance-and-fixes` |
| Related upstream issues | goccy/bigquery-emulator #218 (closed 2026-05-18), #470 (open), #511 (open) and its PR #516 (open) |
| Central floating versions | NuGet's NU1011 and `CentralPackageFloatingVersionsEnabled` (RUN, `restore/cpm-float/`) |
