# Client floors plan (#205, #97)

**Status:** written 2026-10-27 at 5.12.0 (`1ee6373a`) by kronikol-2d, at the owner's ask to analyse #205 critically and
plan a fix for the underlying problem; audited the same day at 5.14.0 (`8d7d1e1d`), at the owner's ask to make it
complete and accurate (section 11 lists what the audit found and changed). **Green-lit 2026-10-28** by the owner ("Can
you implement the plan in full"), every question in section 10 at its recommendation; executing. **R1 released as
5.22.1, R2a as 5.27.1, R2b as 5.27.3, R2c as 5.27.5, F26, found by R2c, as 5.27.7, F30, found by R3a, as 5.27.8,
R3a as 5.27.10, and F37, found by 5.27.10's first CI run, as 5.27.11** (section 11).

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
| R3b, the SDK clients | S3, DynamoDB, SNS, SQS and EventBridge (`AWSSDK.*` 4.0.x), Npgsql (10.0.2), SqlClient (`Microsoft.Data.SqlClient` 7.0.1), Elasticsearch (`Elastic.Clients.Elasticsearch` 9.4.0), MassTransit (9.1.0), MediatR (`MediatR` 14.1.0), MySqlConnector (2.5.0, 2.6.1 from R2a: F10), ClickHouse.Client (7.14.0), ClickHouse.Driver (1.4.0), ClickHouse.Octonica (`Octonica.ClickHouseClient` 3.1.10) |
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

*As built in R2a:* the ledger landed with R2a, as section 6 has it, and its facts are six, in `FloorLedgerTests`: rules
1 and 2 above, the counts of rule 3, and three that keep the list true to the csprojs: every entry names a reference
its project still has, a project references a package once and the ledger holds it once, and each entry says what its
version rests on. Red against an empty ledger, green on the full one. `PackedNuspec.DisagreementsWithLedger` packs a
project and holds every dependency group of its nuspec to the ledger, so each package's fact is one line. R2a's floors
(RUN, `floor/r2a-*.txt`, and the fourth test in `probe-r2a/`):

| Package | Floor | Why there |
|---|---|---|
| Kafka, `Confluent.Kafka` | 2.2.0 | The IConsumer of 2.1.x declares `PositionTopicPartitionOffset`, added in 2.1.0 and dropped in 2.2.0, which `TrackingKafkaConsumer` does not implement; both suites pass on 2.2.0, as on 2.16.0 |
| Grpc, `Grpc.Net.Client` | 2.45.0 | The oldest the suite passes on, against the server of the same release; older releases build the extension, but the suite's streaming facts pass a `CancellationToken` to `WriteAsync`, an overload new in 2.45.0 |
| Grpc, `Google.Protobuf` | 3.19.4 | What Grpc.AspNetCore 2.45.0, the suite's server on the floor run, asks for |
| CloudStorage, `Google.Cloud.Storage.V1` | 4.0.0 | The first of the major |
| Bigtable, `Google.Cloud.Bigtable.V2` | 3.0.0 | The first of the major; the extension calls none of the client's types |
| PubSub, `Google.Cloud.PubSub.V1` | 3.35.0 | The suite passes on 3.4.0, where `DisposeAsync` came, but `SubscriberClient` gained `StopAsync(ShutdownOptions, CancellationToken)` in 3.35.0 with a base that throws, and the wrapper must override it (F8) |
| Spanner, `Google.Cloud.Spanner.Data` and `.V1` | 5.0.0 | The first of the major |
| Spanner, `Grpc.Core.Api` | 2.66.0 | The least Spanner.Data 5.0.0 needs; a lower pin is a downgrade NuGet refuses (NU1605) |

None of their graphs carries a known advisory. The gRPC test project's server moves with the switch: `Grpc.AspNetCore`
`2.*` on the normal run, and `$(GrpcServerFloor)`, 2.45.0, under `-p:FloorRun=true`. After R2a the ledger holds 16
still floating, 33 unmeasured and 12 measured.

*As built in R2b:* the five Azure packages ask for measured floors (RUN, `floor/r2b-*.txt`, and the fourth test in
`probe-r2b/`):

| Package | Floor | Why there |
|---|---|---|
| BlobStorage, `Azure.Storage.Blobs` | 12.13.0 | The first release past GHSA-64x4-9hc6-r2h6 (moderate, a padding oracle in client-side encryption); the suite passes from 12.0.0 |
| StorageQueues, `Azure.Storage.Queues` | 12.11.0 | The first release past the same advisory; the suite passes from 12.0.0 |
| EventHubs, `Azure.Messaging.EventHubs` | 5.6.0 | Each tracking client builds the client it derives from with the identifier of the client it wraps, which 5.6.0 added (F13); before that fix the suite passed from 5.5.0, where `EventData.MessageId` came |
| CosmosDB, `Microsoft.Azure.Cosmos` | 3.46.0 | The first release whose graph carries no known advisory: earlier ones ask for Newtonsoft.Json 10.0.2, under GHSA-5crp-9r3c-p9vr (high); the suite passes from 3.9.0, where `CosmosClientOptions.HttpClientFactory` came |
| ServiceBus, `Azure.Messaging.ServiceBus` | 7.11.0 | `TrackingServiceBusReceiver` overrides the `DeadLetterMessageAsync` with properties and a reason that 7.11.0 added (F12); the suite passes from 7.10.0 without it |
| ServiceBus, `Microsoft.Extensions.DependencyInjection.Abstractions` | 10.0.7 | The least the core's graph allows: `Microsoft.AspNetCore.Mvc.Testing` 10.0.7 asks for it, and a lower pin is a downgrade NuGet refuses (NU1605); R3a measures that pin |

The last took three searches. While Service Bus floated, its newest, 7.21.0, brought an Azure.Core that asks for
10.0.10; with Service Bus on its floor, the test project's own 10.0.0 conflicted with the core's 10.0.7 (NU1605); the
test project now takes 10.0.7 under `-p:FloorRun=true` and `10.*` otherwise (`floor/r2b-servicebus-di.txt`).

The fourth test drove each real client through its extension against the emulators
(`podman/start-r2b-emulators.sh`, `results/r2b-*.txt`): Azurite for Blob Storage and Storage Queues, and the Cosmos DB
vnext-preview emulator, each on the floor and on the newest; the Service Bus and Event Hubs emulators on the newest and
on the oldest release that reads `UseDevelopmentEmulator`, 7.17.3 and 5.11.0, as their floors cannot reach an
emulator. It found that the Service Bus and Event Hubs wrappers left members to their base, which, built through the
constructor for mocking, runs against an empty client (F12, F13), and a sweep of every wrapper found F11.
`WrapperCoverage` (`tests/Kronikol.Tests/Packaging/`) lists the public virtual members a wrapper leaves to its SDK base,
and a guard fact per wrapper holds it to the members known to come back to an override, on whichever release the run
resolves, so a release that adds a virtual turns the newest run red. After R2b the ledger holds 10 still floating, 33
unmeasured and 18 measured.

*As built in R2c:* the last ten floating references ask for measured floors (RUN, `floor/r2c-*.txt`, and the fourth
test in `probe-r2c/`):

| Package | Floor | Why there |
|---|---|---|
| Sqlite, `Microsoft.Data.Sqlite` | 10.0.11 | The first release past GHSA-2m69-gcr7-jv3q (high), against the SQLite build earlier releases bring; the suite passes from 10.0.0 |
| Redis, `StackExchange.Redis` | 2.6.80 | The first release past GHSA-rxg9-xrhp-64gj (critical), against the System.Drawing.Common 5.0.0 earlier releases bring; the suite passes from 2.2.50 |
| Oracle, `Oracle.ManagedDataAccess.Core` | 23.4.0 | The first of the major, where the suite passes |
| Oracle, `System.Security.Cryptography.Pkcs` | 8.0.2 | The first release past GHSA-846f-f764-76mx and GHSA-2fmv-rv2c-59x7, under which every 23.x client's own Pkcs falls (F21) |
| OpenTelemetry, `OpenTelemetry` and `OpenTelemetry.Exporter.InMemory` | 1.15.3 | The first release past GHSA-g94r-2vxg-569j (moderate), against the `OpenTelemetry.Api` earlier releases bring; the suite passes from 1.4.0 |
| MediatR and DispatchProxy, `Microsoft.Extensions.DependencyInjection.Abstractions` | 10.0.7 | The least the core's graph allows, as for Service Bus |
| Kafka.BuildInterception, `Lib.Harmony` | 2.4.2 | The first release that can patch on .NET 10 (F22) |
| EfCore.Relational, `Microsoft.EntityFrameworkCore.Relational` | 10.0.0 | The first of the major, where the suite passes with the providers of the same release; its own graph carries no known advisory, and the three the search's builds report are the test providers' (F28) |
| TcpTap, `MongoDB.Bson` | 2.28.0 | The oldest release the suite can run on: its test project also takes the MongoDB.V2 extension, whose driver floor is 2.28.0, so every older release fails restore (NU1605); its own graph is MongoDB.Bson alone |

The OpenTelemetry, EF Core and TcpTap suites need partners of the floor's release, so each test project takes them at
the floor under `-p:FloorRun=true`: the OpenTelemetry hosting extensions (`$(OpenTelemetryFloor)`), the EF Core
providers (`$(EfCoreFloor)`, with Npgsql's at 10.0.0) and the MongoDB driver (`$(MongoBsonFloor)`), and
`floor/paired_floor.py` searches with them. The fourth test drove the real Redis and Oracle clients through their
extensions against servers in podman, on the floor and on the newest: every call passed on both, with the same record
on both, which for Oracle names no host and no database (F26). After R2c the ledger holds none still floating, 33
unmeasured and 29 measured.

*As built in R3a:* the core package and the test framework adapters ask for measured floors (RUN, `floor/r3a-*.txt`,
and `probe-r3a/`), and the three dependency injection floors R2b and R2c left at the core's 10.0.7 move with the core:

| Package | Floor | Why there |
|---|---|---|
| Kronikol, `Microsoft.AspNetCore.Mvc.Testing` | 10.0.0 | The first of the major, where the core suite passes |
| xUnit2, `xunit.extensibility.execution` | 2.4.0 | The oldest the xUnit v2 suite passes on, each probe with xunit of the same release: 2.3.1 and older have no `TestMethodDisplayOptions`, which the adapter reads |
| xUnit3, `xunit.v3.extensibility.core` | 3.0.0 | The first of the major, each probe with xunit.v3 of the same release |
| NUnit4, `NUnit` | 4.6.0 | The pin: on 4.5.x only `UncapturedTestsTests` fails, the guard on the result tree of `NUnit.Framework.Internal` that the adapter reads, and 4.3.0 and older have no `TestContext.TestAdapter.MethodInfo` |
| MSTest, `MSTest.TestFramework` | 4.0.0 | The first of the major, each probe with MSTest of the same release |
| TUnit, `TUnit.Core` | 1.40.0 | The oldest release whose build of the adapter runs on the newest (F35): TUnit's source generator writes the adapter's hook registrations at its build, and built on 1.39.0 or older they name a field later releases no longer have; each release the second search probed passes on itself, after F30 (`floor/r3a-tunit.txt`), and TUnit's own engine runs a `[HeadIn]` test through the adapter built on 1.40.0 (`probe-r3a/tunit-engine-floor.txt`) |
| ReqNRoll.Core, `Reqnroll` | 3.0.1 | 3.0.0 has no `IFeatureLevelCucumberMessages`, which the adapter reads |
| LightBDD.Core, `LightBDD.Framework` | 3.10.0 | The oldest the LightBDD xUnit v2 example passes on: 3.9.0 and older have no `IScenario.Descriptor`; the xUnit v3 and TUnit runners begin later, at 3.12.0 and 3.12.1 |
| LightBDD.TUnit, `LightBDD.TUnit` | 3.12.1 | The pin: 3.12.0 and older fail two of the suite's facts, as 3.12.0's hook registrations, built on TUnit 1.0.0, name a field later TUnit.Core releases no longer have (`MissingFieldException`); 3.12.1 asks for TUnit 1.53.0 |
| LightBDD.xUnit2, `LightBDD.XUnit2` | 3.10.0 | As LightBDD.Framework, each probe with it of the same release |
| LightBDD.xUnit2, `xunit` | 2.4.2 | The least LightBDD.XUnit2 3.10.0 asks for, where the example passes |
| LightBDD.xUnit3, `LightBDD.XUnit3` | 3.12.0 | Its first release |
| BDDfy.xUnit3, `TestStack.BDDfy` | 8.0.1.3 | The pin, the oldest 8.x NuGet lists; after F32 the suite passes on the newest too, 8.0.11 and the unlisted 8.0.12 a floating `8.*` takes |
| Playwright, `Microsoft.Playwright` | 1.12.1 (1.12.0 in 5.27.10, F37) | The oldest whose driver starts on Linux: 1.12.0, the first stable release, which the search measured on Windows alone, starts its driver there through a script with CRLF line ends. The suite passes on 1.12.1 on Linux and on Windows, each run driving the Chromium of its own release |
| ServiceBus, MediatR and DispatchProxy, `Microsoft.Extensions.DependencyInjection.Abstractions` | 10.0.0 | The first of the major, each probe with the container (and for MediatR the logging) of the same release, now that the core asks for less; MediatR 14.1.0 asks for 10.0.0 |

None of their graphs carries a known advisory. An adapter's suite takes its framework at the newest on the normal run
and at the floor under `-p:FloorRun=true`, through a property a search can set: xUnit v3 (`$(XunitV3Floor)`), xUnit v2
(`$(XunitFloor)`), MSTest (`$(MSTestFloor)`, with the test SDK MSTest brings) and LightBDD's xUnit v3 runner
(`$(LightBddFloor)`); the DI suites take the container at `$(DiFloor)`. The NUnit suite keeps NUnit pinned at its
floor, the release its guard holds the result tree to. The TUnit, LightBDD TUnit and Playwright suites, and the core
suite for Mvc.Testing, Reqnroll and BDDfy, reference the newest under `'$(FloorRun)' != 'true'`, as R2's do. The
LightBDD xUnit v2 adapter had no suite of its own, only the example, which no floor run reaches:
`tests/Kronikol.Tests.LightBDD.xUnit2` holds its package fact, F33's facts and the configuration facts the other
runners' suites have, and runs on 3.10.0 and 2.4.2 under the switch. CI's floor step installs the Chromium of the
Playwright floor before the Playwright suite runs on it, as each release drives its own (`FloorRunTests`).

The searches met three limits of the suites' own code, each fixed in the test, not the floor: `TagMappingTests`
compared tags with collection expressions, which C# 14's first-class spans make ambiguous on xunit.assert 2.6.1 to
2.9.0 (CS0121), so they compare arrays; `HeadInAttributeTests` built a `ParameterMetadata` without `ReflectionInfo`,
required before TUnit.Core 1.19.0 and settable on every 1.x, so it sets it; and the NUnit guard keeps its floor where
the guard reads. The harness gained F29's and F31's fixes, a check that every series package resolves on the release
probed ("NOT ON x"), `--to` (a search from below a release that cannot build), `--setup` (Playwright's browsers, per
probe), `--no-floor-run` (the package built on the release probed, the suites on their newest, F35) and `--filter` for the paired and family searches (each package fact fails every probe of another release, as
F23 found). After R3a the ledger holds none still floating, 19 unmeasured and 43 measured.

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
| The floor ledger's facts, six as built (section 5.2) | no packable project floats, every consumer-facing pin is in the ledger at its version, the still-floating and unmeasured marks only go, and the ledger names only references that exist, each once, each with its basis | an empty ledger; the counts are 25 and 33 after R1, 16 and 33 after R2a, 10 and 33 after R2b, 0 and 33 after R2c, 0 and 19 after R3a |
| Each R2 and R3 package's floor run and nuspec check | as R1 | each batch's parent commit |
| `TrackingSubscriberClientTests` (R2a) | stopping the tracking subscriber stops the client it wraps, by each overload | R2a's parent on 3.39.0: two of the three throw `NotImplementedException` (F8) |
| `A_statement_is_named_for_what_it_does_not_for_the_call_that_carries_it` and the server observer's twin (R2a) | an INSERT, UPDATE or DELETE through `ExecuteSql` is drawn as what it does | R2a's parent: each drawn as `SELECT FROM <table>` (F9) |
| The wrapper guards, one per tracking client that derives from an SDK client (R2b) | the client leaves to its SDK base only the members known to come back to an override, on whichever release the run resolves, and names each in full when one is not | each of five mutations, an override removed (`floor/wrapper-mutations.txt`); on the newest run, a release that adds a virtual |
| `TrackingClientOverloadTests` (R2b) | each Pub/Sub publish overload sends one tracked message through the client it wraps, and a subscriber started with a `SubscriptionHandler` handles on the tracked path | R2b's parent: the handler start throws (F11) |
| `TrackingServiceBusWrapperCoverageTests` (R2b) | processors, rule managers, disposal, a batch send and both dead-letters with properties reach the client the wrapper holds, and the sends and dead-letters are drawn | R2b's parent: `NullReferenceException`, or undrawn (F12) |
| `TrackingEventHubsWrapperCoverageTests` (R2b) | held as the SDK type, the clients have the names and identifier of the clients they wrap and are closed with them; each of the five reads goes through the same overload of the client it wraps and is drawn with its response when it ends, is left early or fails, with its count at `Raw` | R2b's parent: 23 of its facts and theory rows (F13) |
| `ReplaceWithTrackedTests` (R2c) | replacing a service replaces each of its unkeyed registrations, leaves its keyed ones, and the overload with options draws each call under the service it names | R2c's parent: the keyed registration removed (F19) |
| `BDDfyHumaniserTests` (R3a) | a scenario or story title made by the humaniser a test sets is BDDfy's own, and the default's is not while another is set, on 8.0.1.3, 8.0.11 and 8.0.12 | R3a's parent on 8.0.12, its three facts in one run: the adapter reads the copy of the humaniser set first (F32) |
| `TestCountTests` (R3a), in the LightBDD xUnit v2 and v3 suites | a scenario method is a test for each row of its test data, whichever attribute gives the rows, one with none and one whose rows cannot be read | R3a's parent: on xUnit v2 a `MemberData` or `ClassData` method counted one over its rows; on v3 `ClassData` counted one over and `MemberData` one (F33) |
| `Each_floor_run_starts_from_an_empty_output` (R3a), for both of CI's loops | each floor run empties the project's `bin/` and `obj/` before it builds or tests | R3a's parent's `ci.yml` (F36) |
| `The_floor_run_installs_the_Chromium_its_Playwright_floor_drives_before_it_tests` (R3a) | CI's floor run builds the Playwright suite on its floor and installs that build's Chromium before it tests | R3a's parent's `ci.yml` |

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
| F7 | Six test projects took Testcontainers 4.8.1 (ClickHouse, EfCore.Relational, Kafka, MongoDB, MongoDB.V2 and TcpTap), whose SSH.NET 2024.2.0 is under GHSA-q939-rpr3-3284 and GHSA-mggc-4xg6-vcxf (both high, patched in 2026.0.0); NuGet audit reported it on every Kafka probe (RUN). Test projects only, nothing shipped. | Kronikol | All six move to 4.16.0, which brings SSH.NET 2026.0.0, in R2a, building each container from its image (`new KafkaBuilder(Image)` for `new KafkaBuilder().WithImage(Image)`), as 4.16.0 marks the parameterless builders obsolete |
| F8 | `TrackingSubscriberClient` left `SubscriberClient.StopAsync(ShutdownOptions, CancellationToken)`, new in Pub/Sub 3.35.0, to its base, which throws `NotImplementedException`, and the base `StopAsync(TimeSpan)` goes through it: on 3.35.0 and later both stops threw through the wrapper (RUN, `probe-r2a/pubsub` on 3.39.0 against the emulator). The suite's stubs never stopped a subscriber. | Kronikol | Fixed in R2a: the wrapper overrides it, which needs 3.35.0 to compile, so that is the floor; `TrackingSubscriberClientTests` |
| F9 | The interceptor that `SpannerConnectionStringBuilder.WithTestTracking` installs, and the server observer, named an `ExecuteSql` statement for the call that carried it, so an INSERT, UPDATE or DELETE was drawn as `SELECT FROM <table>`; `TrackingSpannerCommand` named it right (RUN, `probe-r2a/spanner` against the emulator, on 5.0.0 and 5.12.0 alike). | Kronikol | Fixed in R2a: both name a statement the SQL classifier knows for what it does; two theories |
| F10 | `Kronikol.Extensions.MySqlConnector` pins MySqlConnector 2.5.0, one of R3b's pins, and GHSA-473q-m89c-ghf8 (moderate, published 2026-10-28) covers every release before 2.6.1: in zero-configuration TLS mode the client sends the account password before it has verified the server's certificate (RUN: NU1902 in R2a's Release build of `release.slnf`). | Kronikol | Moved in R2a to 2.6.1, the first release past it, as A3 has it; the ledger keeps it unmeasured, at its new version, for R3b |
| F11 | `TrackingSubscriberClient` left `SubscriberClient.StartAsync(SubscriptionHandler)` to its base, whose body does not come back to the delegate overload the wrapper overrides: it throws, so a subscriber started with a `SubscriptionHandler` threw through the wrapper (READ, `probe-r2b/wrappers.cs`, then RUN in a fact). The eight `PublishAsync` overloads it leaves come back to the one it overrides. | Kronikol | Fixed in R2b: the override hands the handler's method to the tracked overload; `TrackingClientOverloadTests`, with a guard fact for each client |
| F12 | The Service Bus wrappers left to their base `CreateProcessor(String)`, `CreateProcessor(String, String)`, `CreateRuleManager`, the `DeadLetterMessageAsync` with properties and a reason (new in 7.11.0) and the sender's and receiver's `DisposeAsync`: against the emulator the processors, the rule manager and that dead-letter threw `NullReferenceException` through a wrapper held as the SDK type, and a batch send and a dead-letter with properties went through undrawn (RUN, `results/r2b-servicebus-newest-before-fix.txt`). | Kronikol | Fixed in R2b, each reaching the client the wrapper holds, the batch send and both dead-letters drawn; the override sets the floor at 7.11.0. Left: `GetMessageSessionsAsync`, new in 7.21.0, which a wrapper built against 7.11.0 cannot override; through the wrapper it throws, and the wiki says to call it on `Inner`. A batch's messages carry no test identity, as the caller built the batch |
| F13 | The Event Hubs wrappers, held as the SDK type, threw `NullReferenceException` for their names, had no `Identifier` and said they were open after disposal, as those members are not virtual and read the empty base; the consumer left `ReadEventsAsync(Boolean, ReadEventOptions, CancellationToken)` to its base, which threw; a read its caller left early, or that failed, drew a request with no response; and a read of one partition never showed at `Raw` how many events it read (RUN, `results/r2b-eventhubs-newest-before-fix.txt`: 4 requests, 2 responses). | Kronikol | Fixed in R2b: each wrapper builds its base from the names and identifier of the client it wraps, with a credential that fails at once if asked for a token, and closes it with that client; the SDK asks the credential before it opens a connection, so a call that reached the base would never touch the network (`results/r2b-eventhubs-base.txt`). The five reads go through one tracked loop. `Identifier` needs 5.6.0, the floor |
| F14 | That base is no fix for Service Bus: its client resolves the host before it asks for a token ("No such host is known" in 201 ms with retries off; with them on, it waits out the caller's token), so a call that reached the base would go to the network (RUN). | Kronikol | Tried in R2b and reverted: the Service Bus client keeps the constructor for mocking, and `GetMessageSessionsAsync` stays F12's recorded gap |
| F15 | Storage Queues against Azurite name every call `Other /`, on 12.11.0 and on 12.30.1 alike (RUN, `results/r2b-queues-{floor,newest}.txt`): #125's F10. | Kronikol, #125 | Left to `EMULATOR_URL_SHAPES_PLAN.md` R2, green-lit 2026-10-29 |
| F16 | Cosmos DB 3.46.0's query fails against the vnext-preview emulator ("Unknown JsonNodeType: Unknown"), with Kronikol and without it (RUN, `results/r2b-cosmos-floor-plain.txt`); 3.63.3's passes, and every other call works through the extension on both. | upstream, the client or the emulator | Wiki: a query against vnext-preview needs a newer client than the floor |
| F17 | xUnit cuts a string over 50 characters short in a collection failure, so a guard that listed `ReadEventsAsync(Boolean, ReadEventOptions, CancellationToken)` did not name it (RUN, `floor/wrapper_mutations.py`). | Kronikol | Fixed in R2b: the guards fail through `WrapperCoverage.AssertLeavesOnly`, which names each member in full; five mutations, each red on its guard with the member named (`floor/wrapper-mutations.txt`) |
| F18 | `FloorLedger.cs` names `FloorLedgerTests` in a `cref` that resolves only in `Kronikol.Tests`, so from 5.27.1 each test project that links the file warned CS1574 (RUN). | Kronikol | Fixed in R2b |
| F19 | `ReplaceWithTracked` removed every registration of the service type, keyed ones included, so replacing `IFoo` with a tracking proxy also dropped `AddKeyedSingleton<IFoo>(...)` (RUN, `floor/r2c-di.txt`: the new fact red on 10.0.7 and on 10.0.14). | Kronikol | Fixed in R2c: only the unkeyed registrations are replaced; three facts in `ReplaceWithTrackedTests` |
| F20 | The OpenTelemetry and DispatchProxy facts lived in the core suite, so a floor run of either package would have rerun 11,500 facts on the floor's dependencies (READ). | Kronikol | R2c moved them to `Kronikol.Tests.OpenTelemetry` and `Kronikol.Tests.DispatchProxy` |
| F21 | Every `Oracle.ManagedDataAccess.Core` 23.x, the newest included, asks for a `System.Security.Cryptography.Pkcs` under GHSA-846f-f764-76mx (high) and GHSA-2fmv-rv2c-59x7 (moderate), both published 2026-10-13, so no floor of the client is past them (RUN, `floor/r2c-oracle.txt`). | Kronikol, upstream | R2c: the package asks for Pkcs 8.0.2, the first past both, which lifts the client's; a ledger entry of its own |
| F22 | `Lib.Harmony` before 2.4.2 cannot patch on .NET 10: it throws `PlatformNotSupportedException` ("CoreCLR version 10.0.x is not supported"), and 48 of the build interception suite's 75 facts fail on 2.4.1 (RUN, `floor/r2c-harmony.txt`). | upstream | The floor is 2.4.2 |
| F23 | A package fact present while a floor search runs fails every probe that pins an older release, so the OpenTelemetry search stopped at "the newest release fails" (RUN). | the harness | The fact is written after the search (`apply_floor.py`) |
| F24 | A package with an unmeasured R3 pin cannot hold its nuspec to an empty list of disagreements: MediatR's fact reads its one line, MediatR 14.1.0 unmeasured (RUN). | the harness | The fact expects that line until R3b measures the pin |
| F25 | The TcpTap test project's `NoWarn` hides NU1902 to NU1904 from its own restore, so its runs cannot report an advisory in the Bson it resolves; the ones its logs show, SharpCompress 0.30.1 and Snappier 1.0.0, come from the MongoDB.V2 extension it references, through MongoDB.Driver (READ, RUN). | the harness | The Bson graph was read on its own, a restore of the one reference: MongoDB.Bson alone, no advisory |
| F26 | Every Oracle arrow names `oracle://localhost/unknown/<table>`: `SqlEndpoint.Host` reads an EZConnect data source (`127.0.0.1:21521/FREEPDB1`) as no host, and ODP.NET's `Database` is empty, so the database is `unknown`, on the floor and the newest alike (RUN, `results/r2c-oracle-{floor,newest}.txt`). The database is in history's call line, so its fix moves history's rule. | Kronikol | Fixed in 5.27.7, its own patch: `SqlEndpoint.Address` reads the host, port and service of an EZConnect string or a connect descriptor for the shared SQL tracker, Dapper and the EF Core interceptor, and history's rule moved to 10, each moved call keeping the address rules 6 to 9 recorded (`RequestResponseLog.RecordedBefore10`) |
| F27 | The TcpTap test project pinned `StackExchange.Redis` 2.6.48 and references the Redis extension, so the Redis floor, 2.6.80, made its restore a downgrade NuGet refuses (NU1605): the TcpTap search's first probe failed to build (RUN, `floor/r2c-tcptap.txt`). | Kronikol | Fixed in R2c: the pin moves to 2.6.80; no other test project pins a floored client below its floor |
| F28 | The audit column of `floor/otel_floor.py` and `floor/paired_floor.py` read only a warning that names its project in a closing bracket, and a restore warning names it first, so the EF Core search reported no advisory where each build had three, all from the test providers: `System.Security.Cryptography.Pkcs` 9.0.4 through the SQL Server provider and `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 through the Sqlite provider (RUN). The floors do not rest on that column: each graph was read on its own (`floor/r2c-advisory.sh`, and a restore of the one reference). | the harness | Fixed in R2c: the three search scripts, `floor_probe2.py` among them, read either form |
| F29 | VSTest exits 0 when it finds no test, and `floor/paired_floor.py` and `floor/floor_probe2.py` read the exit code alone, so the MSTest search's first rerun counted three probes that ran nothing as passes and reported 4.0.0 (RUN, `floor/r3a-mstest.txt`). | the harness | Fixed in R3a: each probe's test run passes `RunConfiguration.TreatNoTestsAsError=true`, and a run with no summary fails |
| F30 | `Kronikol.TUnit`'s `HeadInAttribute` implemented TUnit's `IDataSourceAttribute` itself, and TUnit.Core 1.53.0 added `DeferEnumeration` to the interface, so on 1.53.0 and every later release a test with `[HeadIn]` failed before it ran: TUnit's generated test source threw `TypeLoadException` (RUN, `f30/results/`). R3a's TUnit search found it, its first probe, the newest release (1.76.45), failing to build the adapter. Nothing had run the adapter on a later TUnit.Core, as its test project took TUnit.Core only through it, and the wiki's TUnit page installs 1.53.0. | Kronikol | Fixed in 5.27.8, its own patch, ahead of R3a, whose TUnit search needs it: the attribute derives from TUnit's `AsyncUntypedDataSourceGeneratorAttribute`, which implements what TUnit adds; the TUnit suite runs on the newest 1.x and, with `-p:FloorRun=true`, on 1.43.11, and `TUnitInterfaceTests` holds that no adapter type implements a TUnit interface its base class does not |
| F35 | TUnit's source generator writes `Kronikol.TUnit`'s hook registrations into the adapter at its build, bound to that TUnit.Core's internals, so the adapter built on one release loads only on releases that keep them: built on 1.39.0 or older it fails on later releases at load ("Field not found: 'TUnit.Core.Sources.AfterTestHooks'", `MissingFieldException`), and every test with it fails. R3a's second TUnit search built and ran each probe on one release, as every floor search had, so it reported 1.0.0; the full suite's normal runs, which run each suite on the newest against the package built on its floor, found it in the TUnit and LightBDD TUnit suites (RUN, `floor/r3a-tunit3.txt`). No other adapter carries code a framework generates into it. | the harness | Fixed in R3a: `floor_probe2.py --no-floor-run` builds the package on the release probed and runs the suites on their own newest references; the TUnit floor is 1.40.0, the oldest that passes so, and the TUnit suite passes on it as well, with `-p:FloorRun=true` |
| F36 | CI's floor runs built each project over its normal run's output, so a file a package copies only when newer kept the newest's: the MSTest floor run found no test, on MSTest 4.5.2's test adapter, which refuses the 4.0.0 framework (F31's cause), and the Playwright floor's build, 1.12.0's over 1.64.1's, left no `playwright.ps1` to install its browser with, and so did one into an empty `bin/` on the newest's `obj/` (RUN, `probe-r3a/f36/playwright-obj.txt`: a floor build from an empty `bin/` and `obj/` has it, one from an empty `bin/` after a newest build has not) (RUN, the full suite as CI runs it, and `probe-r3a/f36/over-the-newest.txt`: red over the newest's output, green from an empty one). Neither project had a floor run before R3a. | CI | Fixed in R3a: each floor run, in both of CI's loops, starts from an empty `bin/` and `obj/`, and `FloorRunTests` holds both loops to it |
| F37 | `Microsoft.Playwright` 1.12.0 ships the script that starts its driver on Linux and macOS, `.playwright/node/linux/playwright.sh`, with CRLF line ends, so Linux looks for an interpreter named `/bin/sh` and a carriage return and reports "No such file or directory": every test that starts Playwright fails there. R3a's Playwright search ran on Windows alone, where the driver starts through `playwright.cmd`, so it measured 1.12.0, and 5.27.10's first CI run failed its Linux floor run of the Playwright suite (RUN, CI run 40910684927, the Playwright Adapter Tests job; `probe-f37/results.txt`: of the 77 stable 1.x releases only 1.12.0's driver fails to start on Linux; `probe-f37/suite.txt`: the suite on Linux, red on 1.12.0, its browser facts green on 1.12.1). 1.12.1's scripts end their lines in LF; the targets and the code that starts the driver are the same. | the harness | Fixed in 5.27.11: the floor is 1.12.1, and the suite passes on it on Linux and on Windows |
| F31 | Each probe built its test project in place, and `MSTest.TestAdapter` reaches the output as content copied only when newer, so a probe after a newer one ran that one's adapter, which refuses a framework of another release ("'MSTest.TestAdapter' and 'MSTest.TestFramework' must have the same version") and so found no test: MSTest 4.1.0 and older looked unsupported (RUN, VSTest diagnostics; 84 of 84 on 4.1.0 from an empty output). Library assemblies copy on any change, so no client library's search was touched. | the harness | Fixed in R3a: every probe builds from an empty `bin/` and `obj/`; the MSTest, xUnit v3 and xUnit v2 searches ran again so |
| F32 | The BDDfy adapter decides whether a title is BDDfy's own by asking `Configurator.Scanners.Humanize`, the instance field 8.0.1.3 titles with; 8.0.11 titles with `Configurator.Humanizer` and keeps `Scanners.Humanize` only as an obsolete static copy of the humaniser configured when the field is first read, for the rest of the run. The adapter does not build on 8.0.11 or 8.0.12 (CS0176, RUN, `floor/r3a-bddfy.txt`), but built on 8.0.1.3, as published, it runs on them, as the runtime reads the static field through the instance reference, and reads that copy (RUN, `probe-r3a/bddfy-humanize/`). So once a test sets or changes its humaniser after the adapter's first title, a title BDDfy makes with it is taken for one the test wrote: it is left as written, where BDDfy's own titles pass through the title word list (`TitleWords`), and the report marks it written (RUN, `BDDfyHumaniserTests` on the parent and 8.0.12: green each alone, and in one run the default's fact fails after a fact that set a humaniser). Nothing ran the adapter on a later BDDfy, as the core suite took BDDfy only through it. | Kronikol | Fixed in R3a: the adapter reads `Configurator.Humanizer` where the release has it and `Scanners.Humanize` where it does not, each time, as a test may set its own; the core suite runs on the newest 8.x and on 8.0.1.3 |
| F33 | Kronikol holds a LightBDD run's scenarios to the count of tests the adapter expects (`ReportConfigurationOptions.ExpectedTestCount`) and writes no Specifications report, data or Markdown when the run falls short, as a partial run would. The xUnit v2 and v3 adapters counted a scenario method as one test plus the rows of its `MemberData` and `ClassData`, so on xUnit v2 a method with either counted one over its rows and a whole run fell short: no Specifications report, for any suite with such a method (READ, `ReportGenerator`; RUN, `TestCountTests`). On v3 `ClassData` counted one over too, and `MemberData` one, as the count asked for its rows with xUnit v2's signature, and v3's attribute names no rows until it is given the test class, which xUnit gives it before it reads them. The TUnit runner's count reads `[Arguments]` alone, so a method with another data source counts one, under its rows: the report is written, and a partial run that leaves out fewer tests than the shortfall can overwrite it. | Kronikol | Fixed in R3a for xUnit v2 and v3: a method is a test for each row its data attributes give, read through xUnit's own `DataAttribute` and `IDataAttribute`, and one with none; the TUnit count is left, as TUnit's data sources are read by its engine, and TUnit's API moves fast (F30) |
| F34 | The example API and its fake cow service took `Swashbuckle.AspNetCore` 10.1.5, which brings `Microsoft.OpenApi` 2.4.1, under GHSA-v5pm-xwqc-g5wc (high, circular schema references may stop OpenAPI parsing): every LightBDD xUnit v2 probe's audit column named it (RUN, `floor/r3a-lightbdd-xunit2*.txt`). | the examples | Fixed in R3a: both take 10.3.1, which brings the patched 2.7.5 |

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
- **2026-10-29, R2a released as 5.27.1**, rebased onto 5.27.0 (`fd9c1c53`). The nine references of section 5.2's R2a row
  are measured floors (the table in section 5.2's note), the ledger landed with its six facts, and its still-floating
  count fell from 25 to 16. Found on the way and fixed in the release, each red first: F8, the Pub/Sub subscriber's
  stop, which moved the Pub/Sub floor from the 3.4.0 the suite passes on to 3.35.0; and F9, the Spanner interceptor's
  and server observer's DML labels. F10, which the Release build reported, moved MySqlConnector's pin past an advisory
  published the day before. F7 moved six test projects to Testcontainers 4.16.0. The fourth test ran for the three unit
  suites against emulators (`probe-r2a/`), each on its floor and on the newest client, with the same calls and the same
  record on both once F8 and F9 were fixed. This machine's podman logs to journald, which returns no logs, so a
  Testcontainers start that waits on a log line never completes through its own API: Kafka's broker facts ran first
  against a broker started by hand (`KRONIKOL_TEST_KAFKA`), then, with TcpTap's MongoDB facts, through a podman API of
  the session's own whose containers log to files (`floor/r2a-suites.txt`).

  Run: the floor searches (`floor/r2a-*.txt`); the suite matrix before the rebase, on 5.22.1 with R2a's changes, each
  R2a suite on the newest and on the floor and the six Testcontainers suites (`floor/r2a-suites.sh`,
  `floor/r2a-suites.txt`); the fourth test (`probe-r2a/`, `results/r2a-*-{floor,newest}.txt`); the ledger's facts
  against seven mutations, each red on the fact that guards it (`floor/ledger_mutations.py`,
  `floor/ledger-mutations.txt`); then, on 5.27.0 with R2a, the core suite (11,543 passed, 2 skipped), the MySqlConnector
  suite on 2.6.1 (22 of 22), `DiagnosticPageAccessorColumnTests` (2 of 2), which take the gRPC extension, and the
  Release build of `release.slnf`, which reported F10 and, after it, only the older advisories 5.22.1 listed.
- **2026-10-29, R2b released as 5.27.3**, rebased onto 5.27.2 (`753f17f9`). The six references of section 5.2's R2b row
  are measured floors (the table in section 5.2's note), and the still-floating count fell from 16 to 10. Found on the
  way and fixed in the release, each red first: F11, the Pub/Sub subscriber started with a handler; F12, the members
  the Service Bus wrappers left to their base; F13, the Event Hubs wrappers' names, identifier, closing and reads, which
  moved the Event Hubs floor from the 5.5.0 the suite passes on to 5.6.0; F17, the guards' failure message; and F18.
  F14 was tried and reverted, F15 is #125's and F16 the client's own.

  Run: the floor searches (`floor/r2b-search.sh`, `floor/r2b-*.txt`); the fourth test (`probe-r2b/`, `results/r2b-*.txt`)
  and the Event Hubs base probe (`probe-r2b/eh-base/`, `results/r2b-eventhubs-base.txt`); the wrapper guards against
  five mutations, each red on its guard with the member named (`floor/wrapper_mutations.py`,
  `floor/wrapper-mutations.txt`); then, on 5.27.2 with R2b, each R2b suite and Pub/Sub's on the newest and on the
  floor (`floor/r2b-suites.sh`, `floor/r2b-suites.txt`: BlobStorage 75, StorageQueues 87, EventHubs 122, CosmosDB
  118, ServiceBus 194 and PubSub 103, each on both, with no advisory), the core suite (11,543 passed, 2 skipped) and
  the Release build of `release.slnf`, whose only advisories are the older ones R1 listed: MongoDB.Driver 2.x's
  Snappier and SharpCompress, and System.Security.Cryptography.Pkcs through the Oracle and SqlClient clients, which
  R2c and R3b take.

- **2026-10-29, R2c released as 5.27.5**, rebased onto 5.27.4 (`41d0d38f`). The ten references of section 5.2's R2c row
  are measured floors, with the Pkcs the Oracle package now asks for (the table in section 5.2's note), and the
  still-floating count fell from 10 to none. Found on the way and fixed in the release, each red first: F19, the keyed
  registrations `ReplaceWithTracked` removed, and F27, the TcpTap test project's Redis pin; and in the harness, F28. F21
  lifted the Oracle client's Pkcs past two advisories, F22 set the Harmony floor, and F20 gave the OpenTelemetry and
  DispatchProxy packages test projects of their own. F26, the Oracle tracker's address, is left to a patch of its own,
  as its fix moves history's rule, which #125's R1 moves first.

  Run: the floor searches (`floor/r2c-search.sh`, `floor/r2c-advisory.sh`, `floor/r2c-di.sh`, `floor/otel_floor.py`,
  `floor/paired_floor.py`, `floor/r2c-*.txt`); the fourth test for Redis and Oracle against servers in podman
  (`floor/r2c-containers.sh`, `probe-r2c/`, `results/r2c-*.txt`); then each R2c suite on the newest and on the floor
  (`floor/r2c-suites.sh`, `floor/r2c-suites.txt`: Sqlite 30, Redis 164, Oracle 25, OpenTelemetry 4, MediatR 6,
  DispatchProxy 6, Kafka.BuildInterception 76, EfCore.Relational 245 and TcpTap 510 with 1 skipped, each on both, the
  advisories each reports all the test providers' and the MongoDB driver's), the core suite (11,538 passed, 2 skipped)
  and the Release build of `release.slnf`, whose only advisories are MongoDB.Driver 2.x's Snappier and SharpCompress and
  the Pkcs 9.0.13 that SqlClient brings, R3b's; the Oracle client's are gone.

- **2026-10-29, F26 released as 5.27.7**, on 5.27.6 (`7b7a238d`). An Oracle call is drawn against the server and the
  service its data source names, through the Oracle tracker, Dapper and EF Core; `SqlEndpoint.Address` reads EZConnect
  strings and connect descriptors, and a `host/name` data source as a server and a service only where the connection
  names no database. The address is in history's call line, so the rule moved from 9 (#125's R1, 5.27.6) to 10, with the
  address rules 6 to 9 recorded kept on each moved call; the rule was claimed at push with kronikol-5d, whose #125 R2
  moves it next.

  Run: the address theories and facts in `SqlEndpointTests`, the three trackers' suites and
  `SqlOracleAddressBridgeTests`, each red on 5.27.6; three mutations of the bridge (the trackers' mark, rule 9's path,
  the stored `path9`), each red on the facts that guard it; then, on 5.27.6 with F26, the core suite (11,569 passed, 2
  skipped), the Oracle, Dapper, EF Core Relational (251, its servers in podman), SqlClient, Npgsql, MySqlConnector,
  Spanner and Sqlite suites, and the Release build of `release.slnf`, whose only advisories are the older ones 5.27.5
  listed.

- **2026-10-29, F30 released as 5.27.8**, on 5.27.7 (`7d06fd92`). R3a's TUnit search found that the newest
  TUnit.Core, 1.76.45, does not build the TUnit adapter: 1.53.0 added `DeferEnumeration` to `IDataSourceAttribute`,
  which `HeadInAttribute` implemented itself. A consumer's build does not check a referenced type's interfaces, so on
  1.53.0 and later a test with `[HeadIn]` built and then failed: the published 5.27.7 on 1.76.45 throws
  `TypeLoadException` from TUnit's generated test source, and passes on 1.43.11 (RUN, `f30/results/`). The attribute
  now derives from TUnit's `AsyncUntypedDataSourceGeneratorAttribute`, which 1.43.11 already has: it hands
  `GetDataRowsAsync` to the protected `GenerateDataSourcesAsync` the attribute overrides, and from 1.53.0 implements
  `DeferEnumeration` itself.

  Run: the TUnit suite on 1.76.45, red in the five `HeadInAttribute` facts and the four methods that carry the
  attribute (xUnit's discovery reads it), then green, 49 of 49, and on 1.43.11 with `-p:FloorRun=true`, 49 of 49;
  `TUnitInterfaceTests` red on the old attribute; TUnit's own engine on a `[HeadIn]` test, through the fixed adapter on
  1.43.11 and 1.76.45 and through the published 5.27.7 on 1.76.45 (`f30/`); then, on 5.27.7 with F30, the
  LightBDD.TUnit suite (25), the three TUnit examples as CI runs them (12, 3 and 2), the core suite (11,569 passed, 2
  skipped) and the Release build of `release.slnf`, whose only advisories are the older ones 5.27.5 listed.

- **2026-10-29, R3a released as 5.27.10**, on 5.27.9 and the plan commit after it (`d62d7c20`). The core package and
  the test framework adapters ask for measured floors (the table in section 5.2's note), the three DI floors move to
  10.0.0 with the core, and the unmeasured count fell from 33 to 19. Found on the way and fixed in the release, each red
  first: F32, the BDDfy adapter's humaniser; F33, the LightBDD xUnit runners' test counts, with a suite of its own for
  the xUnit v2 runner; F34, the examples' Swashbuckle; and F36, CI's floor runs, which now start from an empty output.
  Fixed in the harness: F29, F31 and F35, which moved the TUnit floor to 1.40.0. CI's Playwright floor run installs
  its floor's Chromium.

  Run: the floor searches (`floor/r3a-*.sh`, `floor/r3a-*.txt`) and TUnit's own engine on the TUnit floor
  (`probe-r3a/`); then the whole suite as CI runs it, before F35 and F36 were found (62 projects on the newest, 21,141
  passed, 63 skipped; 29 floor runs, 16,175 passed, 12 skipped), every failure of which was F35's or F36's; after their
  fixes, the suites they touch on the newest and on their floors, each floor run from an empty `bin/` and `obj/` (TUnit
  50, LightBDD TUnit 26, MSTest 85, Playwright 10, on 1.12.0 with its own Chromium), the three TUnit examples as CI runs
  them (12, 2 and 3), the Integration suite (259), and TUnit's own engine on a `[HeadIn]` test through the adapter built
  on 1.40.0, on 1.40.0, 1.53.0 and 1.76.45, and through the one built on 1.0.0, which fails to load on 1.76.45; the
  facts made red first, `BDDfyHumaniserTests`, the `TestCountTests` of both LightBDD xUnit suites and both new
  `FloorRunTests` facts; the LightBDD xUnit examples, which write their Specifications reports, the v3 one with a
  `[MemberData]` scenario; the core suite on the newest and on its floor (11,578 passed, 2 skipped, each); the
  Playwright E2E suite (2,088 passed, 28 skipped; the one fact that failed, a toggle's timing against its budget under
  the load of the whole run, passes alone with its class, 13 of 13); and the Release build of `release.slnf`, whose only
  advisories are the older ones 5.27.5 lists.

- **2026-10-29, F37 released as 5.27.11**, on 5.27.10 (`2efa6535`). `Kronikol.Playwright` asks for Microsoft.Playwright
  1.12.1, the oldest release whose driver starts on Linux; on 1.12.0, which R3a measured on Windows alone, every test
  that starts Playwright fails there. Found by 5.27.10's first CI run, in its Linux floor run of the Playwright suite.

  Run: the driver of each stable 1.x on Linux, binary-searched in the .NET 10 SDK container (`probe-f37/search.py`,
  `probe-f37/results.txt`); the Playwright suite on Linux in the same container on 1.12.0 and 1.12.1
  (`probe-f37/suite.sh`, `probe-f37/suite.txt`); then the Playwright suite on Linux in the .NET 10 SDK container, as
  CI's floor run takes it: red on 1.12.0, whose two facts that start a browser cannot start its driver, as in 5.27.10's
  CI, and its browser facts green on 1.12.1; the driver of each stable 1.x on Linux, binary-searched, of which only
  1.12.0's fails to start; on Windows the Playwright suite on the newest and, from an empty `bin/` and `obj/`, on 1.12.1
  (10 of 10 each), its package fact red first on the moved ledger; the core suite's packaging, template and workflow
  facts (291); 5.27.10's CI, whose one failed job was this floor run; and the Release build of `release.slnf`, whose
  only advisories are the older ones 5.27.5 lists.

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
