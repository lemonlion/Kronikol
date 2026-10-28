<!-- Two drafts for goccy/bigquery-emulator, for the owner to decide on (Q5): 1 is a comment for #482, with a
     pointer on PR #492, and 2 a new issue. Plain text, no em-dashes. Neither names Kronikol or the suite the issue
     came from. -->

# 1. Comment on #482: the same with the .NET client, and what PR #492 still misses

The same on v0.8.1 (a531d3de), image `ghcr.io/goccy/bigquery-emulator:0.8.1`, with the .NET client, and for a
REPEATED TIMESTAMP as well as one in a RECORD. With `formatOptions.useInt64Timestamp` set, a top-level TIMESTAMP column
comes back as microseconds, but a nested or repeated one comes back in the backend's text form, and a client that asked
for microseconds fails to parse it. The .NET client (`Google.Cloud.BigQuery.V2` 3.6.0 and later asks for microseconds by default)
throws `FormatException: The input string '2023-10-27 00:00:01.500000+00' was not in a correct format.` for
`jobs.query`, `getQueryResults` and `tabledata.list` alike.

Repro with curl (the full script, which also creates the dataset, is attached as `nested-timestamp-curl.sh`):

```
POST /projects/probe/datasets/ds/tables
  schema: ts TIMESTAMP, rec RECORD<inner_ts TIMESTAMP>, arr TIMESTAMP REPEATED
POST /projects/probe/datasets/ds/tables/t/insertAll
  {"json":{"ts":"2023-10-27T00:00:00Z","rec":{"inner_ts":"2023-10-27T00:00:01.5Z"},
           "arr":["2023-10-27T00:00:00Z","2023-10-27T00:00:02Z"]}}
POST /projects/probe/queries
  {"query":"SELECT ts, rec, arr FROM `probe.ds.t`","useLegacySql":false,"formatOptions":{"useInt64Timestamp":true}}
```

Response rows:

```
[{"f":[{"v":"1698364800000000"},
       {"v":{"f":[{"v":"2023-10-27 00:00:01.500000+00"}]}},
       {"v":[{"v":"2023-10-27 00:00:00+00"},{"v":"2023-10-27 00:00:02+00"}]}]}]
```

Expected: `1698364801500000` for `rec.inner_ts`, and `1698364800000000` and `1698364802000000` in `arr`. Google's
.NET client reads nested and repeated TIMESTAMPs with the same int64 converter as top-level ones
(`BigQueryRow.ConvertRecord` and `ConvertArray`).

Cause: `internal/types/types.go` `Format` (L224-243) converts a cell only when its own top-level field is a TIMESTAMP
and its value is a string. A RECORD's value is an object and a REPEATED field's an array, so both pass through
unchanged. #218 was the same symptom for the old backend.

I built PR #492 at its head (`c80afb507e`) and ran the same reads. `jobs.query` and `tabledata.list` now return
microseconds for nested and repeated fields, but `getQueryResults` still returns the text form, so a query run as a job
(`jobs.insert`, then `getQueryResults`, which the .NET client before 3.12.0 always does) still throws. The head also
does not build as it stands: grpc v1.81.1 with golang.org/x/net v0.54.0 fails with `undefined: http2.TrailerPrefix`. It
built after `go get google.golang.org/grpc@latest`.

# 2. tabledata.list returns another dataset's table when two datasets have a table of the same name

**Version:** v0.8.1 (a531d3de).

With tables `ds1.t` (columns id, ts, rec, arr; two rows) and `ds2.t` (columns ts, rec, arr; one row) in one project,
`GET /projects/probe/datasets/ds2/tables/t/data` returns `ds1.t`'s two rows with four columns. `tables.get` on
`ds2/tables/t` returns `ds2.t`'s own schema and one row, and `jobs.query` on `` `probe.ds2.t` `` returns its one row,
so only `tabledata.list` resolves the table without its dataset.

It may share a cause with #470. PR #492, which closes #470, still shows it: built at `c80afb507e`, `tabledata.list` on
the second of two same-named tables returned the first one's rows.
