#!/usr/bin/env bash
# Runs the five features one at a time, as one of them may stop the report and take the others with it.
# usage: run-every.sh <harness dir> <scratch dir> <label> [prototype worktree]
set -u
H="$1"; S="$2"; LABEL="$3"; W="${4:-}"
COPY="$S/repro-every-$LABEL"
OUT="$H/results/every-$LABEL.txt"
mkdir -p "$COPY"
(cd "$H/repro-every" && tar --exclude=bin --exclude=obj --exclude=.kronikol -cf - .) | (cd "$COPY" && tar xf -)
PROPS="-p:GeneratePackageOnBuild=false"
[ -n "$W" ] && PROPS="$PROPS -p:KronikolProto=$W"
(cd "$COPY" && dotnet build -v q --nologo $PROPS 2>&1 | grep -E " error |Error\(s\)" | cut -c1-200 | sort -u)
R="$COPY/bin/Debug/net10.0/Reports"
: > "$OUT"
for category in every some whenfirst whenfirstgiven everyfile; do
  [ -d "$R" ] && mv "$R" "$R-before-$category"
  {
    echo "=== $LABEL, feature tagged $category"
    (cd "$COPY" && dotnet test --no-build --nologo -v q --filter "Category=$category" 2>&1) | grep -a -E "^\s*(Passed|Failed)!|Assembly Cleanup Failure|KeyNotFound" | cut -c1-130 | sort -u
    if [ -f "$R/query.cs" ] && ls "$R"/TestRunReport.* >/dev/null 2>&1; then
      for i in 0 1; do
        dotnet run --file "$R/query.cs" -- steps "$R" "s$i" 2>&1 | grep -v -E "^stableId|^at Features|^open:|^$|^next:|^[0-9]+ calls" | cut -c1-90
        dotnet run --file "$R/query.cs" -- interactions "$R" "s$i" --group-by step,path 2>&1 | grep -E "^(b?[0-9]+|-) " | awk '{print "      call> " $1 " " $2}'
      done
    else
      echo "NO REPORT was written"
    fi
    echo
  } >> "$OUT"
done
cat "$OUT"
