I built this PR at its head (`c80afb507e`) and ran the reads from #482 with the .NET client (`Google.Cloud.BigQuery.V2` 3.11.0 and 3.12.0). Nested and repeated TIMESTAMPs now come back in microseconds through `jobs.query` and `tabledata.list`, but `getQueryResults` still returns them as text, so a query run as a job still fails to parse. That is the path every .NET client before 3.12.0 takes for a query, and the one 3.12.0 takes when a query needs a job:

```
POST /projects/probe/jobs   {"configuration":{"query":{"query":"SELECT rec, arr FROM `probe.ds.t`","useLegacySql":false}}, ...}
GET  /projects/probe/queries/{jobId}?formatOptions.useInt64Timestamp=true&timeoutMs=60000

"rows":[{"f":[{"v":{"f":[{"v":"2023-10-27 00:00:01.500000+00"},{"v":"x"}]}},
              {"v":[{"v":"2023-10-27 00:00:00+00"},{"v":"2023-10-27 00:00:02+00"}]}]}]
```

The same query through `jobs.query` on this build returns `1698364801500000`, `1698364800000000` and `1698364802000000`.

Also, the head does not build as it stands: grpc v1.81.1 with golang.org/x/net v0.54.0 fails with `undefined: http2.TrailerPrefix`. It built after `go get google.golang.org/grpc@latest`.
