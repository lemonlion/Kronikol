#!/usr/bin/env bash
# The .NET 11 SDK on a net11.0 test project, as it comes and with runtime async on, in each configuration: each fact's result,
# what the compiler wrote, and which phase a step leaves its caller in. The matrix's Z70-Z83 read the step trees.
#   DOTNET11=<dotnet.exe of an 11.0 SDK> run-net11.sh <label> <Kronikol.StepTracking version> [local feed]
set -u

label="$1"
version="$2"
feed="${3:-}"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
results="$here/../results"
work="$(cygpath -u "$TEMP" 2>/dev/null || echo "${TMPDIR:-/tmp}")/kronikol-224-consumer/net11/$label"
out="$results/net11-$label.txt"
mkdir -p "$results" "$work"
: > "$out"
{
    echo "$label, SDK $(cd "$work" && "$DOTNET11" --version | cut -d' ' -f1), Kronikol.StepTracking $version"
    for feature in default runtime-async; do
        for config in Debug Release; do
            rm -rf "${work:?}"
            mkdir -p "$work"
            cp "$here/Facts.cs" "$here/net11.csproj" "$here/Directory.Build.props" "$here/Directory.Build.targets" "$work/"
            sed -e 's/\r$//' "$here/../consumer/nuget.config" > "$work/nuget.config"
            if [ -n "$feed" ]; then
                sed -i "s#<clear />#<clear />\n    <add key=\"local\" value=\"$(cygpath -m "$feed" 2>/dev/null || echo "$feed")\" />#" "$work/nuget.config"
            fi
            extra=""
            [ "$feature" = runtime-async ] && extra="-p:Features=runtime-async=on"
            echo "=================== $label, $feature, $config"
            (cd "$work" && "$DOTNET11" test net11.csproj -c "$config" -nologo -tl:off -p:StepTrackingVersion="$version" -p:KronikolVersion="${KRONIKOL_VERSION:-5.28.2}" $extra 2>&1 \
                | grep -E "^ +Failed |error |warning KRONIKOL|Passed!|Failed!|Catastrophic|crashed" | cut -c1-200 | sed -E 's/\[[0-9]+ ms\]//; s/, Duration: [^-]*-/ -/' | sort -u | python "$here/../consumer/scrub.py")
            sort "$work/bin/$config/net11.0/said.txt" 2>/dev/null
        done
    done
} | tee "$out"
rm -rf "${work:?}"
