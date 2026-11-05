#!/usr/bin/env bash
# Reads one repro run's report through its own query engine (never the report file itself).
# usage: read-report.sh <reports-dir> <out-file>
set -u
R="$1"; OUT="$2"
Q() { dotnet run --file "$R/query.cs" -- "$@" 2>&1; }
{
  echo "### summary"; Q summary "$R"
  for i in $(seq 0 27); do
    echo; echo "### s$i steps"; Q steps "$R" "s$i"
    echo; echo "### s$i calls: step, method, path"; Q interactions "$R" "s$i" --group-by step,method,path
  done
} > "$OUT"
wc -l "$OUT"
