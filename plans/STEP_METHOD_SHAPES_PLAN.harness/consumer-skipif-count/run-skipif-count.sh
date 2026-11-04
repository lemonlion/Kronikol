#!/usr/bin/env bash
# What the build says of refused steps whose SkipIf goes unread, when the first of them has none (the plan's Q1).
#   run-skipif-count.sh <label> <Kronikol.StepTracking version> [local feed]
set -u

label="$1"
version="$2"
feed="${3:-}"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
results="$here/../results"
work="$(cygpath -u "$TEMP" 2>/dev/null || echo "${TMPDIR:-/tmp}")/kronikol-226-consumer/skipif-count/$label"
out="$results/skipif-count-$label.txt"
mkdir -p "$results"
{
    rm -rf "${work:?}"
    mkdir -p "$work"
    cp "$here/Program.cs" "$here/skipif.csproj" "$work/"
    sed -e 's/\r$//' "$here/../consumer/nuget.config" > "$work/nuget.config"
    if [ -n "$feed" ]; then
        sed -i "s#<clear />#<clear />\n    <add key=\"local\" value=\"$(cygpath -m "$feed" 2>/dev/null || echo "$feed")\" />#" "$work/nuget.config"
    fi
    echo "=================== $label, Kronikol.StepTracking $version"
    (cd "$work" && dotnet build skipif.csproj -c Debug -nologo -v:q -tl:off -p:StepTrackingVersion="$version" 2>&1 | grep -E " error |warning KRONIKOL" | sed -E 's/^.*warning (KRONIKOL[0-9]+): /warning \1: /; s/ \[[A-Za-z]:.*$//' | cut -c1-420 | sort -u)
    (cd "$work" && dotnet "bin/Debug/net10.0/skipif.dll"; echo "exit $?")
} | tee "$out"
rm -rf "${work:?}"
