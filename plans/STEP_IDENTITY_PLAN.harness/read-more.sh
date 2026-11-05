#!/usr/bin/env bash
# Reads one repro-more or repro-retry run through its own query engine (never the report file itself).
# usage: read-more.sh <reports-dir> <out-file> [scenario-count]
set -u
R="$1"; OUT="$2"; N="${3:-14}"
Q() { dotnet run --file "$R/query.cs" -- "$@" 2>&1; }
{
  echo "### summary"; Q summary "$R"
  for i in $(seq 0 $((N - 1))); do
    echo; echo "### s$i steps"; Q steps "$R" "s$i"
    echo; echo "### s$i calls: step, phase, method, path"; Q interactions "$R" "s$i" --group-by step,phase,method,path
  done
} > "$OUT"
wc -l "$OUT"
