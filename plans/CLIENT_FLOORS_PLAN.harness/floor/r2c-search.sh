#!/bin/sh
# R2c's floor searches (plans/CLIENT_FLOORS_PLAN.md 5.2) for the unit suites whose test projects do not reference the
# package themselves, each binary-searched over its major with floor_probe2.py, one after another.
#   sh r2c-search.sh <worktree> <logs>
W=$1; LOGS=$2; H=$W/plans/CLIENT_FLOORS_PLAN.harness/floor
search() {
  name=$1; project=$2; package=$3; shift 3
  python -I "$H/floor_probe2.py" --worktree "$W" --project "src/$project/$project.csproj" --package "$package" \
    "$@" --out "$H/r2c-$name.txt" --logs "$LOGS/$name" --search
}
search sqlite Kronikol.Extensions.Sqlite Microsoft.Data.Sqlite --tests tests/Kronikol.Tests.Sqlite
search oracle Kronikol.Extensions.Oracle Oracle.ManagedDataAccess.Core --tests tests/Kronikol.Tests.Oracle
search redis Kronikol.Extensions.Redis StackExchange.Redis --tests tests/Kronikol.Tests.Redis
echo ALLDONE > "$H/r2c-search.done"
