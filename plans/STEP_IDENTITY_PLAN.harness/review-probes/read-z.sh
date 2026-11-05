#!/usr/bin/env bash
# usage: read.sh <reports-dir> <out-file> : every scenario's steps and calls, the failures digest, through query.cs
R="$1"; OUT="$2"
Q() { dotnet run --file "$R/query.cs" -- "$@" 2>&1; }
n=$(Q summary "$R" | grep -o -E '^[0-9]+ scenarios' | head -1 | cut -d' ' -f1)
{
  Q summary "$R" | sed -n '1,12p'
  for i in $(seq 0 $((n - 1))); do
    echo; echo "### s$i steps"; Q steps "$R" "s$i" | grep -v -E "^stableId|^open:|^at "
    echo "--- s$i calls"; Q interactions "$R" "s$i" --group-by step,method,path | grep -v "buckets"
  done
  echo; echo "### failures"; Q failures "$R" | grep -v -E "^\s+(rerun|open|thrown)"
} > "$OUT"
wc -l "$OUT"
