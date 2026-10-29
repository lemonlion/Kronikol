#!/bin/sh
# R3b's suites at the release commit: each package R3b touched, on the newest client (its direct reference) and on the
# floor the package asks for (-p:FloorRun=true), each floor run from an empty bin/ and obj/, as CI's floor loop runs
# (F36). One line per run in <out>, with every advisory NuGet audit reported; logs in <logs>. Only the ClickHouse suite
# needs a server, for its ClickHouse.Driver facts: one runs in the podman VM for both of its runs, started here as root
# under a name of this run's own, on a port of its own, and removed at the end.
#   sh r3b-suites.sh <worktree> <out> <logs>
W=$1; OUT=$2; LOGS=$3; mkdir -p "$LOGS"
cd "$W" || exit 1
run() {
  name=$1; shift
  log="$LOGS/$name.log"
  timeout 1800 dotnet test "$@" -nologo -nodeReuse:false -p:NuGetAuditMode=all -- RunConfiguration.TreatNoTestsAsError=true > "$log" 2>&1
  code=$?
  summary=$(grep -aE "(Passed|Failed)!" "$log" | sed 's/^ *//; s/, Duration: [^-]*-/ -/' | tr -s ' ' | tr '\n' ' ')
  audit=$(grep -aoE "warning NU190[1-4]: Package '[^']+' [0-9.]+ has a known [a-z]+" "$log" | sort -u | sed "s/warning NU190[1-4]: Package //" | tr '\n' ';')
  echo "$name | exit $code | ${summary:-no summary }| audit: ${audit:-none}" >> "$OUT"
}
both() {
  suite=$1
  run "$suite" "tests/Kronikol.Tests.$suite"
  rm -rf "tests/Kronikol.Tests.$suite/bin" "tests/Kronikol.Tests.$suite/obj"
  run "$suite floor" "tests/Kronikol.Tests.$suite" -p:FloorRun=true
}
for suite in S3 DynamoDB SNS SQS EventBridge Npgsql SqlClient MySqlConnector Elasticsearch MassTransit MediatR; do
  both "$suite"
done
# Git Bash must not turn the VM's paths into Windows ones.
vm() { MSYS_NO_PATHCONV=1 wsl -d podman-machine-default -u root -- podman "$@"; }
vm rm -f r3b-clickhouse >/dev/null 2>&1
vm run -d --name r3b-clickhouse --log-driver k8s-file -p 28123:8123 -e CLICKHOUSE_USER=clickhouse \
  -e CLICKHOUSE_PASSWORD=clickhouse -e CLICKHOUSE_DEFAULT_ACCESS_MANAGEMENT=1 \
  docker.io/clickhouse/clickhouse-server:25.8-alpine >/dev/null
for i in $(seq 1 60); do curl -fs http://127.0.0.1:28123/ping 2>/dev/null | grep -q Ok && break; sleep 5; done
export KRONIKOL_TEST_CLICKHOUSE="Host=127.0.0.1;Port=28123;Username=clickhouse;Password=clickhouse"
both ClickHouse
vm rm -f r3b-clickhouse >/dev/null 2>&1
echo "ALLDONE" >> "$OUT"
