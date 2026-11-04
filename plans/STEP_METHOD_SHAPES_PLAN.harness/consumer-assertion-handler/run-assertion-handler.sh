#!/usr/bin/env bash
# An assertion that is the first statement of a catch or a finally, with no step in the project: the assertion weaver's own (the plan's F23).
#   run-assertion-handler.sh <label> <Kronikol.AssertionTracking version> [local feed]
set -u

label="$1"
version="$2"
feed="${3:-}"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
results="$here/../results"
work="$(cygpath -u "$TEMP" 2>/dev/null || echo "${TMPDIR:-/tmp}")/kronikol-226-consumer/assertion-handler/$label"
out="$results/assertion-handler-$label.txt"
mkdir -p "$results"
: > "$out"
{
    for config in Debug Release; do
        rm -rf "${work:?}"
        mkdir -p "$work"
        cp "$here/Program.cs" "$here/handler.csproj" "$work/"
        sed -e 's/\r$//' "$here/../consumer/nuget.config" > "$work/nuget.config"
        if [ -n "$feed" ]; then
            sed -i "s#<clear />#<clear />\n    <add key=\"local\" value=\"$(cygpath -m "$feed" 2>/dev/null || echo "$feed")\" />#" "$work/nuget.config"
        fi
        echo "=================== $label, Kronikol.AssertionTracking $version, $config"
        (cd "$work" && dotnet build handler.csproj -c "$config" -nologo -v:q -tl:off -p:AssertionTrackingVersion="$version" 2>&1 | grep -E " error |warning KRONIKOL" | cut -c1-200 | sort -u | python "$here/../consumer/scrub.py")
        (cd "$work" && dotnet "bin/$config/net10.0/handler.dll"; echo "exit $?")
    done
} | tee "$out"
rm -rf "${work:?}"
