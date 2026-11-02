#!/bin/bash
# history-routes.cs with its shards written by R1's own library, not the published one, recorded and merged by R1's tool. `MethodText` is
# in the library, so a verb built by hand only keeps its casing where the writer is R1's.
# args: <empty scratch folder> <a worktree with prototype/r1-first-cut.patch applied, built>
set -u
P=$(cd "$(dirname "$0")" && pwd)
root=$1
W=$2
TOOL=$W/src/Kronikol.Tool/bin/Debug/net10.0/Kronikol.Tool.dll
rm -rf "$root"; mkdir -p "$root"
sed -e "s|#:package Kronikol@5.27.13|#:project $W/src/Kronikol/Kronikol.csproj|" \
    -e "s|#:package Kronikol.Extensions.Redis@5.27.13|#:project $W/src/Kronikol.Extensions.Redis/Kronikol.Extensions.Redis.csproj|" \
    -e "s|#:package Kronikol.Extensions.Grpc@5.27.13|#:project $W/src/Kronikol.Extensions.Grpc/Kronikol.Extensions.Grpc.csproj|" \
    -e "s|#:package Kronikol.Extensions.StorageQueues@5.27.13|#:project $W/src/Kronikol.Extensions.StorageQueues/Kronikol.Extensions.StorageQueues.csproj|" \
    "$P/history-routes.cs" > "$root/history-routes.cs"
ledger=$root/history.jsonl
dotnet "$TOOL" history init --history "$ledger" >/dev/null 2>&1
for i in 1 2 3 4 5 6 7; do
  cp "$ledger" "$root/read$i.jsonl"
  KRONIKOL_HISTORY="$root/read$i.jsonl" dotnet run --file "$root/history-routes.cs" -- "$root/run$i" >/dev/null 2>&1
  [ "$i" -lt 7 ] && dotnet "$TOOL" history record "$root/run$i" --history "$ledger" >/dev/null 2>&1
done
echo "== written, recorded and merged by R1: run 7's own view, then its merge"
dotnet "$TOOL" history gate "$root/run7" --history "$ledger" 2>&1 | grep -E '^history:' | cut -c1-120
mkdir -p "$root/m"; dotnet "$TOOL" merge "$root/run7" -o "$root/m/x.html" --history "$ledger" 2>&1 | grep -E 'history:' | cut -c1-120
python -I "$P/history-routes-verdicts.py" "$root/m/x.labs.html" "$P/history-routes-names.txt"
