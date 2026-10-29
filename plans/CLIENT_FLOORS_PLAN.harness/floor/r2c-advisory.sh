#!/bin/sh
# R2c's advisory floors (plans/CLIENT_FLOORS_PLAN.md 5.2): the releases between the oldest passing one, whose graph
# carries an advisory, and the newest checked clean one, each probed with floor_probe2.py, so the floor is the first
# release past every known advisory. Appends to the search's own file.
#   sh r2c-advisory.sh <worktree> <logs>
W=$1; LOGS=$2; H=$W/plans/CLIENT_FLOORS_PLAN.harness/floor
probe() {
  name=$1; project=$2; package=$3; tests=$4; shift 4
  echo "# advisory floor: each release from the oldest passing to the first without an advisory" >> "$H/r2c-$name.txt"
  python -I "$H/floor_probe2.py" --worktree "$W" --project "src/$project/$project.csproj" --package "$package" \
    --tests "$tests" --out "$H/r2c-$name.txt" --logs "$LOGS/$name-advisory" "$@"
}
probe sqlite Kronikol.Extensions.Sqlite Microsoft.Data.Sqlite tests/Kronikol.Tests.Sqlite 10.0.8 10.0.9 10.0.10 10.0.11 10.0.12 10.0.13
probe redis Kronikol.Extensions.Redis StackExchange.Redis tests/Kronikol.Tests.Redis 2.5.43 2.5.61 2.6.45 2.6.48 2.6.66 2.6.70 2.6.80 2.6.86 2.6.90
echo ALLDONE > "$H/r2c-advisory.done"
