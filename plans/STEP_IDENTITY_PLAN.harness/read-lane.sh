#!/usr/bin/env bash
# Reads every scenario of a run's report through its own query engine. usage: read-lane.sh <reports-dir> <out-file>
set -u
R="$1"; OUT="$2"
Q() { dotnet run --file "$R/query.cs" -- "$@" 2>&1; }
n=$(Q summary "$R" | grep -o -E '^[0-9]+ scenarios' | head -1 | cut -d' ' -f1)
{
  Q summary "$R" | sed -n '/Diagnostics/,/next:/p'
  for i in $(seq 0 $((n - 1))); do
    echo; echo "### s$i steps"; Q steps "$R" "s$i"
    echo; echo "### s$i calls: step, method, path"; Q interactions "$R" "s$i" --group-by step,method,path
  done
} > "$OUT"
wc -l "$OUT"
