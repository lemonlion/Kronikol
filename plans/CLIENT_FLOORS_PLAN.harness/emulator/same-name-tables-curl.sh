#!/bin/bash
# A client-free repro of F3: two datasets each with a table named t, of different schemas; tabledata.list on the second
# returns the first one's rows. tables.get and jobs.query read the right table.
# bash same-name-tables-curl.sh <base, e.g. http://127.0.0.1:19052> (a fresh emulator)
B=${1:-http://127.0.0.1:19052}; P=probe
j() { curl -s -X "$1" "$B$2" -H 'Content-Type: application/json' ${3:+-d "$3"}; echo; }
for d in ds1 ds2; do
  j POST "/projects/$P/datasets" "{\"datasetReference\":{\"projectId\":\"$P\",\"datasetId\":\"$d\"}}" > /dev/null
done
j POST "/projects/$P/datasets/ds1/tables" "{\"tableReference\":{\"projectId\":\"$P\",\"datasetId\":\"ds1\",\"tableId\":\"t\"},\"schema\":{\"fields\":[{\"name\":\"id\",\"type\":\"INT64\"},{\"name\":\"name\",\"type\":\"STRING\"}]}}" > /dev/null
j POST "/projects/$P/datasets/ds2/tables" "{\"tableReference\":{\"projectId\":\"$P\",\"datasetId\":\"ds2\",\"tableId\":\"t\"},\"schema\":{\"fields\":[{\"name\":\"label\",\"type\":\"STRING\"}]}}" > /dev/null
j POST "/projects/$P/datasets/ds1/tables/t/insertAll" '{"rows":[{"json":{"id":1,"name":"one"}},{"json":{"id":2,"name":"two"}}]}' > /dev/null
j POST "/projects/$P/datasets/ds2/tables/t/insertAll" '{"rows":[{"json":{"label":"only row of ds2.t"}}]}' > /dev/null
echo "## tables.get ds2.t (schema)"
j GET "/projects/$P/datasets/ds2/tables/t" | grep -o '"schema":{"fields":\[[^]]*\]'
echo "## jobs.query SELECT * FROM probe.ds2.t"
j POST "/projects/$P/queries" "{\"query\":\"SELECT * FROM \`$P.ds2.t\`\",\"useLegacySql\":false}" | grep -o '"rows":\[.*\]' | head -c 300; echo
echo "## tabledata.list ds2.t"
j GET "/projects/$P/datasets/ds2/tables/t/data" | grep -o '"rows":\[.*\]' | head -c 300; echo
echo "## tabledata.list ds1.t"
j GET "/projects/$P/datasets/ds1/tables/t/data" | grep -o '"rows":\[.*\]' | head -c 300; echo
