#!/usr/bin/env bash
# A step that hands back an assertion's task, in a library that names no type of the core: the assertion runs, and on main it leaves no note and no word (the plan's F25).
#   run-returned-assertion.sh <label> <weaver version> [local feed]
set -u

label="$1"
version="$2"
feed="${3:-}"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
results="$here/../results"
work="$(cygpath -u "$TEMP" 2>/dev/null || echo "${TMPDIR:-/tmp}")/kronikol-226-consumer/returned-assertion/$label"
out="$results/returned-assertion-$label.txt"
mkdir -p "$results"
: > "$out"
{
    for config in Debug Release; do
        rm -rf "${work:?}"
        mkdir -p "$work"
        cp -r "$here/Program.cs" "$here/returned.csproj" "$here/steps" "$work/"
        sed -e 's/\r$//' "$here/../consumer/nuget.config" > "$work/nuget.config"
        if [ -n "$feed" ]; then
            sed -i "s#<clear />#<clear />\n    <add key=\"local\" value=\"$(cygpath -m "$feed" 2>/dev/null || echo "$feed")\" />#" "$work/nuget.config"
        fi
        echo "=================== $label, both weavers $version, $config"
        (cd "$work" && dotnet build returned.csproj -c "$config" -nologo -v:q -tl:off -p:WeaverVersion="$version" 2>&1 | grep -E " error |warning KRONIKOL" | cut -c1-200 | sort -u | python "$here/../consumer/scrub.py")
        (cd "$work" && dotnet "bin/$config/net10.0/returned.dll"; echo "exit $?")
    done
} | tee "$out"
rm -rf "${work:?}"
