#!/usr/bin/env bash
# Each shape alone, as one report-stopping shape takes the whole run's report with it. The reports folder is fresh per shape.
# usage: run-shapes.sh <kind pub|proto> <label>
set -u
export KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off
# Copy this folder outside the repo first: it builds and writes runs/ beside itself.
HERE="$(cd "$(dirname "$0")" && pwd)"
KIND="$1"; LABEL="$2"
C="$HERE/runs/shapes-$LABEL"
mkdir -p "$C/Features"
(cd "$HERE/rq6-src" && tar --exclude=bin --exclude=obj --exclude='*.feature.cs' --exclude=Features -cf - .) | (cd "$C" && tar xf -)
cp "$HERE"/shapes/*.feature "$C/Features/"
PROPS=""
[ "$KIND" = proto ] && PROPS="-p:KronikolProto=${PROTO_ROOT:-C:/Code/Kronikol-223p} -p:GeneratePackageOnBuild=false"
OUT="$HERE/shapes-$LABEL.txt"
: > "$OUT"
(cd "$C" && dotnet build --nologo -v q $PROPS 2>&1 | grep -aE " error |Error\(s\)" | cut -c1-200 | sort -u | head -5) | tee -a "$OUT"
for f in "$HERE"/shapes/*.feature; do
  name=$(basename "$f" .feature)
  key="${name%%-*}"
  R="$C/bin/Debug/net10.0/R-$key-$RANDOM"
  {
    echo "=================== $name ($KIND)"
    (cd "$C" && R6_DIR="$(basename "$R")" timeout 200 dotnet test --no-build --nologo -v q --filter "FullyQualifiedName~Features.$key" 2>&1 | grep -aE "Failed!|Passed!|KeyNotFound|No test" | cut -c1-170 | sort -u | head -5)
    echo "files: $(ls "$R" 2>/dev/null | grep -aE '^(TestRunReport\.(krkl|html)|Failures\.md|Run\.json)$' | tr '\n' ' ')"
    if [ -f "$R/TestRunReport.krkl" ]; then
      (cd "$R" && dotnet run --file query.cs -- summary . 2>&1 | grep -aE "^! |Diagnostics|×" | cut -c1-200 | sort -u | head -6)
      for s in s0 s1 s2; do
        (cd "$R" && dotnet run --file query.cs -- steps . $s 2>&1 | grep -av "^stableId\|^at Fea\|^open:\|^$\|calls · interactions\|^! " | cut -c1-120)
      done
    fi
  } >> "$OUT" 2>&1
done
wc -l "$OUT"
