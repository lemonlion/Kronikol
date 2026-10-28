On v0.8.1 (`ghcr.io/goccy/bigquery-emulator:0.8.1`), when two datasets each hold a table named `t`, `tabledata.list` on the second one returns the first one's rows. `tables.get` and `jobs.query` read the right table, so it looks as if only `tabledata.list` resolves the table without its dataset.

Repro against a fresh emulator started with `--project=probe`:

```bash
B=http://127.0.0.1:9050; P=probe
j() { curl -s -X "$1" "$B$2" -H 'Content-Type: application/json' ${3:+-d "$3"}; echo; }
for d in ds1 ds2; do
  j POST "/projects/$P/datasets" "{\"datasetReference\":{\"projectId\":\"$P\",\"datasetId\":\"$d\"}}" > /dev/null
done
j POST "/projects/$P/datasets/ds1/tables" "{\"tableReference\":{\"projectId\":\"$P\",\"datasetId\":\"ds1\",\"tableId\":\"t\"},\"schema\":{\"fields\":[{\"name\":\"id\",\"type\":\"INT64\"},{\"name\":\"name\",\"type\":\"STRING\"}]}}" > /dev/null
j POST "/projects/$P/datasets/ds2/tables" "{\"tableReference\":{\"projectId\":\"$P\",\"datasetId\":\"ds2\",\"tableId\":\"t\"},\"schema\":{\"fields\":[{\"name\":\"label\",\"type\":\"STRING\"}]}}" > /dev/null
j POST "/projects/$P/datasets/ds1/tables/t/insertAll" '{"rows":[{"json":{"id":1,"name":"one"}},{"json":{"id":2,"name":"two"}}]}' > /dev/null
j POST "/projects/$P/datasets/ds2/tables/t/insertAll" '{"rows":[{"json":{"label":"only row of ds2.t"}}]}' > /dev/null
j GET "/projects/$P/datasets/ds2/tables/t"
j POST "/projects/$P/queries" "{\"query\":\"SELECT * FROM \`$P.ds2.t\`\",\"useLegacySql\":false}"
j GET "/projects/$P/datasets/ds2/tables/t/data"
```

What comes back for `ds2.t`:

```
tables.get       "schema":{"fields":[{"name":"label","type":"STRING"}]
jobs.query       "rows":[{"f":[{"v":"only row of ds2.t"}]}]
tabledata.list   "rows":[{"f":[{"v":"1"},{"v":"one"}]},{"f":[{"v":"2"},{"v":"two"}]}]
```

Expected from `tabledata.list`: the one row of `ds2.t`, as `jobs.query` returns it. `tabledata.list` on `ds1.t` returns the same two rows.

It may share a cause with #470. PR #492, which closes #470, still shows it: built at its head (`c80afb507e`), `tabledata.list` on the second of two same-named tables returned the first one's rows.
