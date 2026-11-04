#!/usr/bin/env bash
# Which hook methods Reqnroll runs, by visibility, own and inherited. Usage: run.sh <scratch dir>
here=$(cd "$(dirname "$0")" && pwd); q=$1/hookprobe; rm -rf "$q"; mkdir -p "$q"; cp -r "$here"/. "$q"/; cd "$q" || exit 1
for v in 3.3.4 3.0.1; do rm -rf bin obj; dotnet test -c Release -p:ReqnrollVersion=$v 2>&1 | tail -1 | cut -c1-90; echo "== Reqnroll $v"; cat bin/Release/net10.0/hooks.txt; done
