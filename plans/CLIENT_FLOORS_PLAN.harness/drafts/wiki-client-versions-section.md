<!-- Draft for Integration-BigQuery-Extension.md (wiki origin/master ae3393f), to go after "## Install". R1 applies it
     with the floor the owner picks (Q1); the CI sentence depends on Q3. The operation tables further down the page
     belong to #125's plan (EMULATOR_URL_SHAPES_PLAN.md) and are not touched here. -->

## Client versions and emulators

The package asks for `Google.Cloud.BigQuery.V2` 3.0.0 or later, so a suite can pin whichever 3.x its application
ships with. The package's own suite runs on the newest 3.x, and in CI on 3.0.0 as well.

### `ExecuteQuery` and the BigQuery emulator

From 3.12.0 the client runs `ExecuteQuery` and `ExecuteQueryAsync` as one `jobs.query` call
(`POST /projects/{project}/queries`) whenever the query's options allow it, and asks for TIMESTAMP values in
microseconds in the request body. Earlier clients create a job (`POST /projects/{project}/jobs`) and then ask for its
results with that option in the URL. A query keeps the job path on 3.12.0 when it sets a job id or prefix, a priority,
a destination table or another job-only setting.

[goccy/bigquery-emulator](https://github.com/goccy/bigquery-emulator) reads the option from the request body from
0.6.1 (April 2024) on. An older emulator answers in seconds, and reading the column throws
`FormatException: The input string '1698364800.0' was not in a correct format.` Testcontainers' `BigQueryBuilder()`
without an image starts `ghcr.io/goccy/bigquery-emulator:0.4`, which is 0.4.4, in every release of the module (the
constructor has been marked obsolete since 4.10.0), so name a newer image:

```csharp
var emulator = new BigQueryBuilder("ghcr.io/goccy/bigquery-emulator:0.8.1").Build();
```

Three gaps remain in 0.8.1 (checked 2026-10-27 with clients 3.0.0, 3.11.0 and 3.12.0):

- A TIMESTAMP inside a RECORD, or in a REPEATED field, comes back as text (`2023-10-27 00:00:01.500000+00`) while the
  client asked for microseconds. Reading it throws `FormatException` with any client from 3.6.0, the first to ask for
  microseconds by default, through `ExecuteQuery` and `ListRows` alike
  ([goccy/bigquery-emulator#482](https://github.com/goccy/bigquery-emulator/issues/482); the open fix,
  [#492](https://github.com/goccy/bigquery-emulator/pull/492), does not yet cover a query run as a job).
- A query parameter's declared type is not used
  ([goccy/bigquery-emulator#511](https://github.com/goccy/bigquery-emulator/issues/511)), so `SELECT @p` with a
  TIMESTAMP parameter returns a STRING column.
- `ListRows` on a table returns the rows of the first table of that name in any dataset.

The tracking handler records each reply as the emulator sent it and changes nothing the client receives.
