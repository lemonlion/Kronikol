#!/bin/bash
# Runs each probe (src/) on the published Kronikol packages ("before", version $1, default 4.9.1) and on this checkout's
# adapters ("after"), one at a time, and keeps what each wrote in before/ and after/. The probes are copied outside the
# repository first, so its Directory.Build.props does not apply to them. TUnit runs through `dotnet run` (Microsoft.Testing.Platform).
set -u
H="$(cd "$(dirname "$0")" && pwd)"
R="$(cd "$H/../../.." && pwd)"
VERSION="${1:-4.9.1}"
P="$(mktemp -d)"
cp -r "$H/src/." "$P/"
mkdir -p "$H/before" "$H/after"
cd "$P"
for mode in before after; do
  for d in xunit2 nunit mstest tunit xunit3; do
    rm -rf "$d/bin" "$d/obj"
    props="-p:KronikolVersion=$VERSION"
    [ "$mode" = after ] && props="$props -p:KronikolSource=$R"
    if [ "$d" = tunit ]; then (cd "$d" && dotnet run $props > "run-$mode.log" 2>&1); else (cd "$d" && dotnet test $props > "run-$mode.log" 2>&1); fi
    echo "$mode $d exit $?"
    cp "$d"/bin/Debug/net10.0/probe*.txt "$H/$mode/$d.txt" 2>/dev/null || echo "  no output for $mode $d"
  done
done
rm -rf "$P"
