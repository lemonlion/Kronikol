#!/usr/bin/env bash
# The probes of #223's second review, run on a source copy of this plan's prototype: did the faults it measured in a flow with a close by identity reach §6.2?
# Usage: run_sibling_review.sh <probes dir holding xu, rq, lb> <work dir> <prototype> <Kronikol.Tool.dll> <out dir> <label>
set -u
export KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off
PROBES="$1"; WORK="$2"; PROTO="$3"; TOOL="$4"; OUT="$5"; LABEL="$6"
Q() { dotnet "$TOOL" query "$@" 2>&1; }
for p in xu rq lb; do
  d="$WORK/rv-$p-$LABEL"
  rm -rf "$d"; mkdir -p "$d"
  (cd "$PROBES/$p" && tar --exclude=bin --exclude=obj --exclude=.kronikol -cf - .) | (cd "$d" && tar xf -)
  f="$OUT/sibling-review-$p-$LABEL.txt"
  (
    cd "$d" || exit 1
    dotnet build -v q --nologo -m:2 -nodeReuse:false -p:GeneratePackageOnBuild=false "-p:KronikolProto=$PROTO" 2>&1 | grep -E " error |Error\(s\)" | cut -c1-160 | sort -u
    dotnet test --no-build --nologo -v q 2>&1 | grep -E "^\s*(Passed|Failed)!" | cut -c1-100
  ) > "$f"
  reports="$d/bin/Debug/net10.0/Reports"
  [ -d "$reports" ] || { echo "no reports" >> "$f"; continue; }
  n=$(Q summary "$reports" | grep -o -E '^[0-9]+ scenarios' | head -1 | cut -d' ' -f1)
  {
    Q summary "$reports" | sed -n '/Diagnostics/,/next:/p'
    i=0
    while [ "$i" -lt "${n:-0}" ]; do
      echo; echo "### s$i steps"; Q steps "$reports" "s$i"
      echo; echo "### s$i calls: step, method, path"; Q interactions "$reports" "s$i" --group-by step,method,path
      i=$((i+1))
    done
  } >> "$f"
done
echo FINISHED
