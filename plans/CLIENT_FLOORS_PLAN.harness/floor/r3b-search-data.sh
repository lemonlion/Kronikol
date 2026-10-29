#!/bin/sh
# R3b's floor searches for the SQL and search clients (plans/CLIENT_FLOORS_PLAN.md 5.2): Npgsql, Microsoft.Data.SqlClient,
# MySqlConnector and Elastic.Clients.Elasticsearch, each binary-searched over its major with floor_probe2.py, one after
# another (the searches share the worktree's builds). No suite needs a server: each runs on fakes of its own. The
# MySqlConnector floor cannot go below 2.6.1 whatever the search finds (F10's advisory), and SqlClient's audit column
# says which Pkcs each release brings.
#   sh r3b-search-data.sh <worktree> <logs>
W=$1; LOGS=$2; H=$W/plans/CLIENT_FLOORS_PLAN.harness/floor
search() {
  name=$1; project=$2; package=$3; shift 3
  python -I "$H/floor_probe2.py" --worktree "$W" --project "src/$project/$project.csproj" --package "$package" \
    "$@" --out "$H/r3b-$name.txt" --logs "$LOGS/$name" --search
}
search npgsql Kronikol.Extensions.Npgsql Npgsql --tests tests/Kronikol.Tests.Npgsql
search sqlclient Kronikol.Extensions.SqlClient Microsoft.Data.SqlClient --tests tests/Kronikol.Tests.SqlClient
search mysqlconnector Kronikol.Extensions.MySqlConnector MySqlConnector --tests tests/Kronikol.Tests.MySqlConnector
search elasticsearch Kronikol.Extensions.Elasticsearch Elastic.Clients.Elasticsearch --tests tests/Kronikol.Tests.Elasticsearch
echo ALLDONE > "$H/r3b-search-data.done"
