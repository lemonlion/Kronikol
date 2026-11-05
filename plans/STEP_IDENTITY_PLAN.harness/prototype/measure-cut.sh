#!/usr/bin/env bash
# Runs the probes against one cut of the prototype, each from a copy outside the repo, and reads them through their own query engine.
# usage: measure-cut.sh <label> <prototype worktree> <scratch dir>. Results: results/cut-<label>-<probe>.txt
set -u
export KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off
LABEL="$1"; W="$2"; S="$3"
HERE="$(cd "$(dirname "$0")/.." && pwd)"
PROPS="-p:KronikolProto=$W -p:GeneratePackageOnBuild=false"
R_="$HERE/results"

one() {  # <probe folder> <short name> <reader> [extra count]
  local copy="$S/cut-$LABEL-$2"
  mkdir -p "$copy"
  (cd "$HERE/$1" && tar --exclude=bin --exclude=obj --exclude=.kronikol -cf - .) | (cd "$copy" && tar xf -)
  echo "=== $2"
  (cd "$copy" && dotnet build -v q --nologo $PROPS 2>&1 | grep -E " error |Error\(s\)" | cut -c1-170 | sort -u)
  (cd "$copy" && dotnet test --no-build --nologo -v q 2>&1 | grep -E "^\s*(Passed|Failed)!" | cut -c1-110)
  local reports="$copy/bin/Debug/net10.0/Reports"
  if false; then
    bash "$HERE/review-probes/read-z.sh" "$reports" "$R_/cut-$LABEL-$2.txt" > /dev/null
  elif [ "$3" = read-more ]; then
    bash "$HERE/read-more.sh" "$reports" "$R_/cut-$LABEL-$2.txt" "$4" > /dev/null
  elif [ "$3" = read-report ]; then
    bash "$HERE/read-report.sh" "$reports" "$R_/cut-$LABEL-$2.txt" > /dev/null
  else
    bash "$HERE/read-lane.sh" "$reports" "$R_/cut-$LABEL-$2.txt" > /dev/null
  fi
}

one repro triggers read-report
one repro-bddfy bddfy read-lane
one repro-bddfy-retry bddfy-retry read-lane
one repro-lightbdd lightbdd read-lane
one repro-more more read-more 16
one review-probes/rq rq read-lane
one review-probes/lb lb read-lane
one review-probes/xu xu read-lane
echo "=== done"; df -h /c | tail -1
