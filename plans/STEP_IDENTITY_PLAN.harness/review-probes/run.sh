#!/usr/bin/env bash
# Runs one of the review's probes from a copy outside the repo. usage: run.sh <harness dir> <scratch dir> <xu|rq|lb> <published|proto> [prototype worktree]
set -u
export KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off
H="$1"; S="$2"; P="$3"; L="$4"; W="${5:-}"
C="$S/rv-$P-$L"
mkdir -p "$C"
(cd "$H/review-probes/$P" && tar --exclude=bin --exclude=obj --exclude=.kronikol -cf - .) | (cd "$C" && tar xf -)
PROPS="-p:GeneratePackageOnBuild=false"
[ -n "$W" ] && PROPS="$PROPS -p:KronikolProto=$W"
(cd "$C" && dotnet build -v q --nologo $PROPS 2>&1 | grep -E " error |Error\(s\)" | cut -c1-160 | sort -u; dotnet test --no-build --nologo -v q 2>&1 | grep -E "^\s*(Passed|Failed)!" | cut -c1-100)
bash "$H/read-lane.sh" "$C/bin/Debug/net10.0/Reports" "$H/results/review-$P-$L.txt" > /dev/null
awk -f "$H/results/compact.awk" "$H/results/review-$P-$L.txt" > "$H/results/review-$P-$L.compact.txt"
