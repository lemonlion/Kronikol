#!/bin/sh
# R2c's runs that need containers (plans/CLIENT_FLOORS_PLAN.md 5.2), one after another, as they share the worktree's
# builds: the EF Core Relational and TcpTap floor searches, whose suites start their servers through Testcontainers,
# through the session's own podman API on 2376 (podman/podman-api-2376.sh, which logs to files, as Testcontainers
# waits on a log line), then the fourth test for Redis and Oracle against servers started here as root in the VM.
#   sh r2c-containers.sh <worktree> <logs> [searches|probes, by default both]
W=$1; LOGS=$2; H=$W/plans/CLIENT_FLOORS_PLAN.harness
WHAT=${3:-both}
TC="--env DOCKER_HOST=tcp://127.0.0.1:2376 --env TESTCONTAINERS_RYUK_DISABLED=true"
if [ "$WHAT" != probes ]; then
python -I "$H/floor/paired_floor.py" --worktree "$W" \
  --project src/Kronikol.Extensions.EfCore.Relational/Kronikol.Extensions.EfCore.Relational.csproj \
  --tests tests/Kronikol.Tests.EfCore.Relational --pin Microsoft.EntityFrameworkCore.Relational \
  --series Microsoft.EntityFrameworkCore.Relational,Microsoft.EntityFrameworkCore.InMemory,Microsoft.EntityFrameworkCore.Sqlite,Microsoft.EntityFrameworkCore.SqlServer,Microsoft.EntityFrameworkCore.SqlServer.NetTopologySuite \
  --property EfCoreFloor --major 10 --out "$H/floor/r2c-efcore.txt" --logs "$LOGS/efcore" $TC
python -I "$H/floor/paired_floor.py" --worktree "$W" \
  --project src/Kronikol.Extensions.TcpTap/Kronikol.Extensions.TcpTap.csproj \
  --tests tests/Kronikol.Tests.TcpTap --pin MongoDB.Bson --series MongoDB.Bson,MongoDB.Driver \
  --property MongoBsonFloor --major 2 --out "$H/floor/r2c-tcptap.txt" --logs "$LOGS/tcptap" $TC
fi
[ "$WHAT" = searches ] && exit 0
# Git Bash must not turn the VM's paths into Windows ones, but python must get Windows paths.
vm() { MSYS_NO_PATHCONV=1 wsl -d podman-machine-default -u root -- podman "$@"; }
vm rm -f r2c-redis >/dev/null 2>&1
vm run -d --name r2c-redis --log-driver k8s-file -p 26379:6379 docker.io/library/redis:7-alpine >/dev/null
sleep 5
for run in floor newest; do
  flag=""; [ $run = newest ] && flag="-p:Newest=true"
  (cd "$H/probe-r2c/redis" && timeout 900 dotnet run $flag 2>&1 | grep -v "^\s*$") > "$H/results/r2c-redis-$run.txt"
done
vm rm -f r2c-redis >/dev/null 2>&1
vm rm -f r2c-oracle >/dev/null 2>&1
vm run -d --name r2c-oracle --log-driver k8s-file -p 21521:1521 -e ORACLE_PASSWORD=R2c_Probe_Pw1 docker.io/gvenzl/oracle-free:23-slim >/dev/null
for i in $(seq 1 90); do vm logs r2c-oracle 2>&1 | grep -q "DATABASE IS READY TO USE" && break; sleep 10; done
for run in floor newest; do
  flag=""; [ $run = newest ] && flag="-p:Newest=true"
  (cd "$H/probe-r2c/oracle" && timeout 900 dotnet run $flag 2>&1 | grep -v "^\s*$") > "$H/results/r2c-oracle-$run.txt"
done
vm rm -f r2c-oracle >/dev/null 2>&1
echo ALLDONE > "$H/floor/r2c-containers.done"
