#!/usr/bin/env bash
# usage: read.sh <reports-dir> <out-file> : every scenario's steps and calls through the tool, never the report file itself
R="$1"; OUT="$2"
K=/c/Code/Kronikol-223r/src/Kronikol.Tool/bin/Debug/net10.0/Kronikol.Tool.exe
Q() { "$K" query "$@" 2>&1; }
n=$(Q summary "$R" | grep -a -o -E '^[0-9]+ scenarios' | head -1 | cut -d' ' -f1)
{
  Q summary "$R" | sed -n '1,14p'
  echo "=== Files: $(ls "$R" | tr '\n' ' ')"
  for i in $(seq 0 $((${n:-0} - 1))); do
    echo; echo "### s$i steps"; Q steps "$R" "s$i" | grep -a -v -E "^stableId|^open:|^at "
    echo "--- s$i calls"; Q interactions "$R" "s$i" --group-by step,method,path | grep -a -v "buckets"
  done
} > "$OUT"
wc -l "$OUT" | cut -d' ' -f1
