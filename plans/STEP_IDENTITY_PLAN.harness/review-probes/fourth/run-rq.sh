#!/usr/bin/env bash
# usage: run-rq.sh <src folder> <label> pub|proto [count]. Copies the probe, builds, tests, writes a read of the run to <label>.txt beside the script.
set -u
export KRONIKOL_HISTORY=off KRONIKOL_KEEP_RUNS=off
# Copy this folder outside the repo first: it builds and writes runs/ beside itself.
HERE="$(cd "$(dirname "$0")" && pwd)"
SRC="$1"; LABEL="$2"; KIND="$3"; N="${4:-40}"
C="$HERE/runs/$LABEL"
mkdir -p "$C"
(cd "$SRC" && tar --exclude=bin --exclude=obj --exclude='*.feature.cs' -cf - .) | (cd "$C" && tar xf -)
PROPS=""
[ "$KIND" = proto ] && PROPS="-p:KronikolProto=C:/Code/Kronikol-223p -p:GeneratePackageOnBuild=false"
cd "$C"
dotnet build --nologo -v q $PROPS 2>&1 | grep -aE " error |Error\(s\)|Warn.*\(s\)" | cut -c1-200 | sort -u | head -8
timeout 280 dotnet test --no-build --nologo -v q 2>&1 | grep -aE "Failed!|Passed!|^\s*Failed " | cut -c1-130 | head -40
R="$C/bin/Debug/net10.0/Reports"
ls "$R" 2>/dev/null | head -30
bash "$HERE/read.sh" "$R" "$HERE/$LABEL.txt" "$N"
