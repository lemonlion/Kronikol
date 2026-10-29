#!/bin/sh
# R3b's floor searches for MassTransit, MediatR and the three ClickHouse clients (plans/CLIENT_FLOORS_PLAN.md 5.2), each
# binary-searched over its major with floor_probe2.py, one after another (the searches share the worktree's builds).
# MediatR's package fact is left out (it holds the nuspec to the ledger, so it fails every probe of another release,
# F23). The ClickHouse suite takes its three clients at the newest under '$(FloorRun)' != 'true', so each probe's
# -p:FloorRun=true leaves it the release probed; only the ClickHouse.Driver facts need a server, and for that search
# one runs in the podman VM, started here as root under a name of this run's own, on a port of its own.
#   sh r3b-search-bus.sh <worktree> <logs>
W=$1; LOGS=$2; H=$W/plans/CLIENT_FLOORS_PLAN.harness/floor
grep -q "Include=\"ClickHouse.Driver\" Version=\"1.\*\" Condition=\"'\$(FloorRun)' != 'true'\"" \
  "$W/tests/Kronikol.Tests.ClickHouse/Kronikol.Tests.ClickHouse.csproj" || { echo "the ClickHouse suite floats its clients"; exit 1; }
search() {
  name=$1; project=$2; package=$3; shift 3
  python -I "$H/floor_probe2.py" --worktree "$W" --project "src/$project/$project.csproj" --package "$package" \
    "$@" --out "$H/r3b-$name.txt" --logs "$LOGS/$name" --search
}
search masstransit Kronikol.Extensions.MassTransit MassTransit --tests tests/Kronikol.Tests.MassTransit
search mediatr Kronikol.Extensions.MediatR MediatR --tests tests/Kronikol.Tests.MediatR --filter "FullyQualifiedName!~PackageTests"
search clickhouse-client Kronikol.Extensions.ClickHouse.Client ClickHouse.Client --tests tests/Kronikol.Tests.ClickHouse
search clickhouse-octonica Kronikol.Extensions.ClickHouse.Octonica Octonica.ClickHouseClient --tests tests/Kronikol.Tests.ClickHouse

# Git Bash must not turn the VM's paths into Windows ones.
vm() { MSYS_NO_PATHCONV=1 wsl -d podman-machine-default -u root -- podman "$@"; }
vm rm -f r3b-clickhouse >/dev/null 2>&1
vm run -d --name r3b-clickhouse --log-driver k8s-file -p 28123:8123 -e CLICKHOUSE_USER=clickhouse \
  -e CLICKHOUSE_PASSWORD=clickhouse -e CLICKHOUSE_DEFAULT_ACCESS_MANAGEMENT=1 \
  docker.io/clickhouse/clickhouse-server:25.8-alpine >/dev/null
for i in $(seq 1 60); do curl -fs http://127.0.0.1:28123/ping 2>/dev/null | grep -q Ok && break; sleep 5; done
echo "# server: clickhouse-server:25.8-alpine, ping $(curl -fs http://127.0.0.1:28123/ping 2>/dev/null | tr -d '\n')" > "$H/r3b-clickhouse-server.txt"
search clickhouse-driver Kronikol.Extensions.ClickHouse.Driver ClickHouse.Driver --tests tests/Kronikol.Tests.ClickHouse \
  --env "KRONIKOL_TEST_CLICKHOUSE=Host=127.0.0.1;Port=28123;Username=clickhouse;Password=clickhouse"
vm rm -f r3b-clickhouse >/dev/null 2>&1
echo ALLDONE > "$H/r3b-search-bus.done"
