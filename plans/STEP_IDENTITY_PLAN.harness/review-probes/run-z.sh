#!/usr/bin/env bash
# The third reviewer's probes, one build. usage: run-z.sh <harness dir> <scratch dir> <label> [prototype worktree]. Results: results/z-rq-<label>.txt, z-bd-<label>.txt
# BG10 and whatever else stops the report takes the whole run's report with it, so a run with no report says so.
set -u
export KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off
H="$1"; S="$2"; L="$3"; W="${4:-}"
PROPS="-p:GeneratePackageOnBuild=false -p:KronikolVersion=5.28.2"
[ -n "$W" ] && PROPS="$PROPS -p:KronikolProto=$W"
for p in z-rq z-bd; do
  C="$S/$p-$L"
  mkdir -p "$C"
  (cd "$H/review-probes/$p" && tar --exclude=bin --exclude=obj --exclude='*.feature.cs' -cf - .) | (cd "$C" && tar xf -)
  echo "=== $p $L"
  (cd "$C" && dotnet build -v q --nologo $PROPS 2>&1 | grep -E " error |Error\(s\)" | cut -c1-160 | sort -u)
  # BG10, a failure inside a shared step with a file on it, stops the report on every build (#222's F3), so it runs alone, after the rest.
  (cd "$C" && dotnet test --no-build --nologo -v q $PROPS --filter "FullyQualifiedName!~BG10" 2>&1 | grep -a -E "^\s*(Passed|Failed)!|Cleanup Failure" | cut -c1-130 | sort -u)
  R="$C/bin/Debug/net10.0/Reports"
  if [ -f "$R/query.cs" ] && ls "$R"/TestRunReport.* >/dev/null 2>&1; then
    bash "$H/review-probes/read-z.sh" "$R" "$H/results/$p-$L.txt" > /dev/null
  else
    echo "NO REPORT was written" | tee "$H/results/$p-$L.txt"
  fi
  if [ "$p" = z-rq ]; then
    echo "--- BG10 alone"
    (cd "$C" && dotnet test --no-build --nologo -v q $PROPS --filter "FullyQualifiedName~BG10" 2>&1 | grep -a -E "^\s*(Passed|Failed)!|Cleanup Failure" | cut -c1-120 | sort -u)
    if [ -f "$R/query.cs" ] && ls "$R"/TestRunReport.* >/dev/null 2>&1; then
      (cd "$R" && for s in s0 s1; do dotnet run --file query.cs -- steps . $s 2>&1 | grep -aE "^s[0-9]|^. +b?[0-9.]+ |attachment:" | cut -c1-100; done) > "$H/results/$p-$L-bg10.txt"
    fi
  fi
done
