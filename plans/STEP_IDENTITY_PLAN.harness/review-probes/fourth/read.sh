#!/usr/bin/env bash
# usage: read.sh <reports-dir> <out-file> [count]. Reads a run through its own query.cs, never the report file.
set -u
R="$1"; OUT="$2"; N="${3:-40}"
Q() { dotnet run --file "$R/query.cs" -- "$@" 2>&1; }
n=$(Q summary "$R" | grep -o -E '^[0-9]+ scenarios?' | head -1 | cut -d' ' -f1)
n="${n:-$N}"
{
  echo "### summary"; Q summary "$R"
  for i in $(seq 0 $((n - 1))); do
    echo; echo "### s$i steps"; Q steps "$R" "s$i"
    echo; echo "### s$i calls: step, method, path"; Q interactions "$R" "s$i" --group-by step,method,path
  done
} > "$OUT"
wc -l "$OUT"
