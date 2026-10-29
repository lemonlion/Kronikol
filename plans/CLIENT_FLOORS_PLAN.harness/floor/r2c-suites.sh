#!/bin/sh
# R2c's suites at the release commit: each package R2c touched, on the newest client (its direct reference) and on the
# floor the package asks for (-p:FloorRun=true). One line per run in <out>, with every advisory NuGet audit reported;
# logs in <logs>. The default suites need no Docker; EfCore.Relational's and TcpTap's start their servers through
# Testcontainers, so they run with the session's podman API (podman/podman-api-2376.sh): DOCKER_HOST=tcp://127.0.0.1:2376
# TESTCONTAINERS_RYUK_DISABLED=true sh r2c-suites.sh <worktree> <out> <logs> "EfCore.Relational TcpTap".
#   sh r2c-suites.sh <worktree> <out> <logs> ["<suite> ...", by default every R2c suite]
W=$1; OUT=$2; LOGS=$3; mkdir -p "$LOGS"
cd "$W" || exit 1
run() {
  name=$1; shift
  log="$LOGS/$name.log"
  timeout 1200 dotnet test "$@" -nologo -nodeReuse:false -p:NuGetAuditMode=all > "$log" 2>&1
  code=$?
  summary=$(grep -aE "(Passed|Failed)!" "$log" | sed 's/^ *//; s/, Duration: [^-]*-/ -/' | tr -s ' ' | tr '\n' ' ')
  audit=$(grep -aoE "warning NU190[1-4]: Package '[^']+' [0-9.]+ has a known [a-z]+" "$log" | sort -u | sed "s/warning NU190[1-4]: Package //" | tr '\n' ';')
  echo "$name | exit $code | ${summary:-no summary }| audit: ${audit:-none}" >> "$OUT"
}
for suite in ${4:-Sqlite Redis Oracle OpenTelemetry MediatR DispatchProxy Kafka.BuildInterception}; do
  run "$suite" "tests/Kronikol.Tests.$suite"
  run "$suite floor" "tests/Kronikol.Tests.$suite" -p:FloorRun=true
done
echo "ALLDONE" >> "$OUT"
