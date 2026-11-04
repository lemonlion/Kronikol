#!/usr/bin/env bash
# Assertions beside a throw: a switch or a ternary with a throwing arm, a throw part-way, a throw in a default label. The note on the path a run takes,
# with each weaver, so that refusing a statement that ends by throwing (the plan's F26) is seen to take nothing away.
#   run-throw-arms.sh <label> <weaver version> [local feed]
set -u

label="$1"
version="$2"
feed="${3:-}"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
results="$here/../results"
work="$(cygpath -u "$TEMP" 2>/dev/null || echo "${TMPDIR:-/tmp}")/kronikol-226-consumer/throw-arms/$label"
out="$results/throw-arms-$label.txt"
mkdir -p "$results"
: > "$out"
{
    for config in Debug Release; do
        rm -rf "${work:?}"
        mkdir -p "$work"
        cp "$here/Program.cs" "$here/thr.csproj" "$work/"
        sed -e 's/\r$//' "$here/../consumer/nuget.config" > "$work/nuget.config"
        if [ -n "$feed" ]; then
            sed -i "s#<clear />#<clear />\n    <add key=\"local\" value=\"$(cygpath -m "$feed" 2>/dev/null || echo "$feed")\" />#" "$work/nuget.config"
        fi
        echo "=================== $label, both weavers $version, $config"
        (cd "$work" && dotnet build thr.csproj -c "$config" -nologo -v:q -tl:off -p:WeaverVersion="$version" 2>&1 | grep -E " error |warning KRONIKOL" | sed -E 's/.*(warning KRONIKOL[0-9]+: [^:]*: [0-9]+ [a-z ]+\([a-z]+\)).*/\1/' | cut -c1-200 | sort -u)
        (cd "$work" && dotnet "bin/$config/net10.0/thr.dll"; echo "exit $?")
    done
} | tee "$out"
rm -rf "${work:?}"
