#!/bin/sh
# R2a's suites at the release commit: each R2a package's suite on the newest client (its direct reference) and on the
# floor the package asks for (-p:FloorRun=true), and the six Testcontainers suites on Testcontainers 4.16.0 (F7).
# One line per run in <out>, with every advisory NuGet audit reported; logs in <logs>.
#   sh r2a-suites.sh <worktree> <out> <logs>
# The container suites need Docker, or podman's Docker API (DOCKER_HOST=tcp://127.0.0.1:2375,
# TESTCONTAINERS_RYUK_DISABLED=true). Kafka's broker facts take a broker from KRONIKOL_TEST_KAFKA when it is set: on a
# podman machine whose log driver returns no logs, Testcontainers' Kafka start never completes, so start one by hand.
W=$1; OUT=$2; LOGS=$3; mkdir -p "$LOGS"
cd "$W" || exit 1
run() {
  name=$1; shift
  log="$LOGS/$name.log"
  timeout 1200 dotnet test "$@" -nologo -p:NuGetAuditMode=all > "$log" 2>&1
  code=$?
  summary=$(grep -aE "(Passed|Failed)!" "$log" | sed 's/^ *//; s/, Duration: [^-]*-/ -/' | tr -s ' ' | tr '\n' ' ')
  audit=$(grep -aoE "warning NU190[1-4]: Package '[^']+' [0-9.]+ has a known [a-z]+" "$log" | sort -u | sed "s/warning NU190[1-4]: Package //" | tr '\n' ';')
  echo "$name | exit $code | ${summary:-no summary }| audit: ${audit:-none}" >> "$OUT"
}
for suite in Grpc CloudStorage Bigtable PubSub Spanner Kafka.BuildInterception Kafka; do
  run "$suite" "tests/Kronikol.Tests.$suite"
  run "$suite floor" "tests/Kronikol.Tests.$suite" -p:FloorRun=true
done
for suite in ClickHouse EfCore.Relational MongoDB MongoDB.V2 TcpTap; do
  run "$suite" "tests/Kronikol.Tests.$suite"
done
