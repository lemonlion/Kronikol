#!/usr/bin/env bash
# One probe against a prototype worktree, from a fresh copy outside the repo. usage: run-probe.sh <probe folder under the harness> <fresh scratch folder> <worktree> <result file> [read-lane|read-more N|read-z]
set -u
export KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off
HERE="$(cd "$(dirname "$0")/.." && pwd)"
P="$1"; C="$2"; W="$3"; OUT="$4"; READER="${5:-read-lane}"; COUNT="${6:-}"
mkdir -p "$C"
(cd "$HERE/$P" && tar --exclude=bin --exclude=obj --exclude='*.feature.cs' -cf - .) | (cd "$C" && tar xf -)
(cd "$C" && timeout 280 dotnet test --nologo -v q -p:KronikolProto="$W" -p:GeneratePackageOnBuild=false 2>&1 | grep -aE " error |Failed!|Passed!" | cut -c1-120 | sort -u | head -3)
R="$C/bin/Debug/net10.0/Reports"
case "$READER" in
  read-more) bash "$HERE/read-more.sh" "$R" "$OUT" "$COUNT" > /dev/null ;;
  read-z) bash "$HERE/review-probes/read-z.sh" "$R" "$OUT" > /dev/null ;;
  *) bash "$HERE/read-lane.sh" "$R" "$OUT" > /dev/null ;;
esac
