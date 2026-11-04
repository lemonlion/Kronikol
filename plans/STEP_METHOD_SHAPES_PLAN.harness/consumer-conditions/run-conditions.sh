#!/usr/bin/env bash
# An assertion's result used in a condition, with no step in the project: the assertion weaver's own (the plan's F29). A line ending WRONG took the other way.
#   run-conditions.sh <label> <Kronikol.AssertionTracking version> [local feed]
set -u

label="$1"
version="$2"
feed="${3:-}"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
results="$here/../results"
work="$(cygpath -u "$TEMP" 2>/dev/null || echo "${TMPDIR:-/tmp}")/kronikol-226-consumer/conditions/$label"
out="$results/conditions-$label.txt"
mkdir -p "$results"
: > "$out"
{
    for config in Debug Release; do
        rm -rf "${work:?}"
        mkdir -p "$work"
        cp "$here/Program.cs" "$here/conditions.csproj" "$work/"
        sed -e 's/\r$//' "$here/../consumer/nuget.config" > "$work/nuget.config"
        if [ -n "$feed" ]; then
            sed -i "s#<clear />#<clear />\n    <add key=\"local\" value=\"$(cygpath -m "$feed" 2>/dev/null || echo "$feed")\" />#" "$work/nuget.config"
        fi
        echo "=================== $label, Kronikol.AssertionTracking $version, $config"
        (cd "$work" && dotnet build conditions.csproj -c "$config" -nologo -v:q -tl:off -p:AssertionTrackingVersion="$version" 2>&1 | grep -E " error |warning KRONIKOL" | sed -E 's/^.*warning (KRONIKOL[0-9]+): /warning \1: /' | cut -c1-90 | sort -u)
        (cd "$work" && dotnet "bin/$config/net10.0/conditions.dll"; echo "exit $?")
    done
} | tee "$out"
rm -rf "${work:?}"
