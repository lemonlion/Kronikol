#!/usr/bin/env bash
# Wall-clock time of the CLI as a person runs it (process start, JIT, decode, scan, answer), three runs each.
# usage: bash cli-timing.sh <report file> ...   Writes nothing beside the reports; decode output goes to ./tmp.
set -u
here="$(cd "$(dirname "$0")" && pwd)"
tool="C:/Code/Kronikol-192/src/Kronikol.Tool/bin/Release/net10.0/Kronikol.Tool.dll"
mkdir -p "$here/tmp"
ms() { echo $(( ($(date +%s%N) - $1) / 1000000 )); }
for report in "$@"; do
  q=""
  for i in 1 2 3; do
    t=$(date +%s%N)
    answer=$(dotnet "$tool" query interactions "$report" --group-by service,method,path --count 2>&1 | tr '\n' ' ')
    q="$q $(ms "$t")"
  done
  d=""
  for i in 1 2 3; do
    t=$(date +%s%N)
    dotnet "$tool" decode "$report" -o "$here/tmp/decoded.json" > /dev/null 2>&1
    d="$d $(ms "$t")"
  done
  rm -f "$here/tmp/decoded.json"
  echo "$report | query ms:$q | decode -o ms:$d | answer: $answer"
done
