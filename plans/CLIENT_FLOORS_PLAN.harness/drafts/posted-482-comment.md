A data point from the .NET client, for a REPEATED TIMESTAMP as well as one inside a RECORD.

On v0.8.1 (`ghcr.io/goccy/bigquery-emulator:0.8.1`), with `formatOptions.useInt64Timestamp` set, a top-level TIMESTAMP comes back in microseconds and a nested or repeated one in the backend's text form. `Google.Cloud.BigQuery.V2` asks for microseconds by default from 3.6.0, so it throws `FormatException: The input string '2023-10-27 00:00:01.500000+00' was not in a correct format.` on all three read paths: `jobs.query`, `getQueryResults` and `tabledata.list`.

<details><summary>curl repro (an emulator started with <code>--project=probe</code>)</summary>

```bash
B=http://127.0.0.1:9050; D=ds_curl; P=probe
j() { curl -s -X "$1" "$B$2" -H 'Content-Type: application/json' ${3:+-d "$3"}; echo; }
j POST "/projects/$P/datasets" "{\"datasetReference\":{\"projectId\":\"$P\",\"datasetId\":\"$D\"}}" > /dev/null
j POST "/projects/$P/datasets/$D/tables" "{\"tableReference\":{\"projectId\":\"$P\",\"datasetId\":\"$D\",\"tableId\":\"t\"},\"schema\":{\"fields\":[{\"name\":\"ts\",\"type\":\"TIMESTAMP\"},{\"name\":\"rec\",\"type\":\"RECORD\",\"fields\":[{\"name\":\"inner_ts\",\"type\":\"TIMESTAMP\"}]},{\"name\":\"arr\",\"type\":\"TIMESTAMP\",\"mode\":\"REPEATED\"}]}}" > /dev/null
j POST "/projects/$P/datasets/$D/tables/t/insertAll" '{"rows":[{"json":{"ts":"2023-10-27T00:00:00Z","rec":{"inner_ts":"2023-10-27T00:00:01.5Z"},"arr":["2023-10-27T00:00:00Z","2023-10-27T00:00:02Z"]}}]}' > /dev/null
j POST "/projects/$P/queries" "{\"query\":\"SELECT ts, rec, arr FROM \`$P.$D.t\`\",\"useLegacySql\":false,\"formatOptions\":{\"useInt64Timestamp\":true}}"
```

Rows from `jobs.query`:

```
[{"f":[{"v":"1698364800000000"},
       {"v":{"f":[{"v":"2023-10-27 00:00:01.500000+00"}]}},
       {"v":[{"v":"2023-10-27 00:00:00+00"},{"v":"2023-10-27 00:00:02+00"}]}]}]
```

Expected: `1698364801500000` for `rec.inner_ts`, and `1698364800000000` and `1698364802000000` in `arr`.

</details>

PR #492 fixes `jobs.query` and `tabledata.list` for these fields but not `getQueryResults`; I've left the details on the PR.
