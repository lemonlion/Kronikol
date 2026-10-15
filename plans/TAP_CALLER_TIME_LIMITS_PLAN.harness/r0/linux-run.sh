#!/bin/sh
# Runs the tap test projects of a tree on Linux (managed HttpListener). $1 = the tgz under /mnt/c, $2 = a label, $3.. = projects.
set -u
TGZ="$1"; LABEL="$2"; shift 2
TREE=/root/k173/$LABEL
mkdir -p "$TREE" /root/nuget
rm -rf "$TREE/src" "$TREE/tests"
tar -xzmf "$TGZ" -C "$TREE"
for PROJECT in "$@"; do
  NAME=$(basename "$PROJECT")
  LOG="$TREE/$NAME.log"
  podman run --rm --name "k173-$LABEL-$NAME" -v "$TREE":/repo -v /root/nuget:/root/.nuget/packages -w /repo \
    mcr.microsoft.com/dotnet/sdk:10.0 sh -c "dotnet test $PROJECT --blame-hang-timeout 10m 2>&1" > "$LOG" 2>&1
  echo "== $NAME exit $?"
  grep -E "Passed!|Failed!|^\s+Failed |error" "$LOG" | head -40
done
