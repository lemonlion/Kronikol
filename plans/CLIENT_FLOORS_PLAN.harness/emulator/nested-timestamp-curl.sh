#!/bin/bash
# A client-free repro of the nested TIMESTAMP gap: one row with a top-level TIMESTAMP, one in a RECORD and two in a
# REPEATED field, read back through jobs.query and tabledata.list with formatOptions.useInt64Timestamp=true.
# bash nested-timestamp-curl.sh <base, e.g. http://127.0.0.1:19050> <dataset>
B=${1:-http://127.0.0.1:19050}; D=${2:-ds_curl}; P=probe
j() { curl -s -X "$1" "$B$2" -H 'Content-Type: application/json' ${3:+-d "$3"}; echo; }
j POST "/projects/$P/datasets" "{\"datasetReference\":{\"projectId\":\"$P\",\"datasetId\":\"$D\"}}" > /dev/null
j POST "/projects/$P/datasets/$D/tables" "{\"tableReference\":{\"projectId\":\"$P\",\"datasetId\":\"$D\",\"tableId\":\"t\"},\"schema\":{\"fields\":[{\"name\":\"ts\",\"type\":\"TIMESTAMP\"},{\"name\":\"rec\",\"type\":\"RECORD\",\"fields\":[{\"name\":\"inner_ts\",\"type\":\"TIMESTAMP\"}]},{\"name\":\"arr\",\"type\":\"TIMESTAMP\",\"mode\":\"REPEATED\"}]}}" > /dev/null
j POST "/projects/$P/datasets/$D/tables/t/insertAll" '{"rows":[{"json":{"ts":"2023-10-27T00:00:00Z","rec":{"inner_ts":"2023-10-27T00:00:01.5Z"},"arr":["2023-10-27T00:00:00Z","2023-10-27T00:00:02Z"]}}]}' > /dev/null
echo "## jobs.query, formatOptions.useInt64Timestamp=true in the body"
j POST "/projects/$P/queries" "{\"query\":\"SELECT ts, rec, arr FROM \`$P.$D.t\`\",\"useLegacySql\":false,\"formatOptions\":{\"useInt64Timestamp\":true}}" | grep -o '"rows":.*\]' | head -c 400; echo
echo "## tabledata.list, formatOptions.useInt64Timestamp=true in the URL"
j GET "/projects/$P/datasets/$D/tables/t/data?formatOptions.useInt64Timestamp=true" | grep -o '"rows":.*\]' | head -c 400; echo
