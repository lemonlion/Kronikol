#!/usr/bin/env bash
# The plan's runs of the shape probe, on the same shape list:
#   baseline: this worktree's src as main has it (the patch taken out), the weaver the issue reports on
#   prototype: plans/STEP_METHOD_SHAPES_PLAN.prototype.patch applied
# Each in Debug and Release, then in Release as /debug+ compiles it, which xUnit v2 turns on. The prototype again with the JIT at
# full optimisation, and on the .NET 11 runtime if DOTNET11_ROOT names a folder holding one.
# Each run of both configurations takes about twelve minutes, four child processes at a time. Run from a worktree where the patch is applied.
# SNAPSHOT=<dir> keeps each build's binaries there as linux-baseline-bin and linux-proto-bin, for linux-matrix.sh.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/../.." && pwd)"
patch="$root/plans/STEP_METHOD_SHAPES_PLAN.prototype.patch"
results="$here/results"
probe="$(cygpath -u "$TEMP" 2>/dev/null || echo "${TMPDIR:-/tmp}")/kronikol-226-probe/bin/Release/net10.0/probe"
mkdir -p "$results"

build() {
    dotnet build "$here/probe/probe.csproj" -c Release -nologo -v:q 2>&1 | grep -E " error |Build succeeded" | sort -u | cut -c1-200
}

git -c core.autocrlf=false -c core.eol=lf -C "$root" apply -R "$patch"
if [ -n "$(git -C "$root" status --short -- src)" ]; then
    echo "src is not as main has it" >&2
    exit 1
fi
build
snapshot() {
    if [ -n "${SNAPSHOT:-}" ]; then
        rm -rf "${SNAPSHOT:?}/$1"
        cp -r "$(dirname "$probe")" "${SNAPSHOT:?}/$1"
    fi
}
snapshot linux-baseline-bin
"$probe" all baseline-9d994a4b "$results" > /dev/null
PROBE_CONFIGS=ReleasePlus "$probe" all baseline-9d994a4b-debug-plus "$results" > /dev/null

git -c core.autocrlf=false -c core.eol=lf -C "$root" apply "$patch"
build
snapshot linux-proto-bin
STRICT_SENTINEL=1 "$probe" all prototype "$results" > /dev/null

# The same run with the JIT at full optimisation from the first call, which could refuse what tier 0 lets through.
STRICT_SENTINEL=1 DOTNET_TieredCompilation=0 "$probe" all prototype-full-opt "$results" > /dev/null
STRICT_SENTINEL=1 PROBE_CONFIGS=ReleasePlus "$probe" all prototype-debug-plus "$results" > /dev/null
if [ -n "${DOTNET11_ROOT:-}" ]; then
    STRICT_SENTINEL=1 DOTNET_ROOT="$DOTNET11_ROOT" DOTNET_ROLL_FORWARD=Major "$probe" all prototype-net11 "$results" > /dev/null
fi

sed -n '1,/^not ok/p' "$results/summary-baseline-9d994a4b.txt"
sed -n '1,/^not ok/p' "$results/summary-prototype.txt"
sed -n '1,/^not ok/p' "$results/summary-prototype-full-opt.txt"
