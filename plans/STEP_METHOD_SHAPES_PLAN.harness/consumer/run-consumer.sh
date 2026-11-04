#!/usr/bin/env bash
# Builds the consumer as a user's project is built, from packages, under each debug symbol setting, and runs it.
#   [ASSERTION_TRACKING_VERSION=<version in the feed>] run-consumer.sh <label> <Kronikol.StepTracking version> [extra local feed]
# 5.28.2 reads the package as nuget.org serves it. A prototype run packs this worktree's weavers under a version of their own
# into a local feed, and keeps the core at 5.28.2: a new weaver over an older core is the skew the package allows, since
# a weaver's package declares no dependency on the core.
set -u

label="$1"
version="$2"
feed="${3:-}"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
results="$here/../results"
work="$(cygpath -u "$TEMP" 2>/dev/null || echo "${TMPDIR:-/tmp}")/kronikol-226-consumer/$label"
out="$results/consumer-$label.txt"
weavers=(-p:StepTrackingVersion="$version" -p:AssertionTrackingVersion="${ASSERTION_TRACKING_VERSION:-5.28.2}")
mkdir -p "$results" "$work"
: > "$out"

for debug in portable embedded none; do
    for config in Debug Release; do
        project="$work/$debug-$config"
        rm -rf "${project:?}"
        mkdir -p "$project"
        cp "$here/Program.cs" "$here/consumer.csproj" "$here/Directory.Build.props" "$here/Directory.Build.targets" "$project/"
        sed -e 's/\r$//' "$here/nuget.config" > "$project/nuget.config"
        if [ -n "$feed" ]; then
            sed -i "s#<clear />#<clear />\n    <add key=\"local\" value=\"$(cygpath -m "$feed" 2>/dev/null || echo "$feed")\" />#" "$project/nuget.config"
        fi
        {
            echo "=================== $label, DebugType=$debug, $config"
            (cd "$project" && dotnet build consumer.csproj -c "$config" -nologo -v:m -tl:off -p:DebugType="$debug" "${weavers[@]}" 2>&1 \
                | grep -E " error |warning KRONIKOL|Build succeeded|Warn|Error\(s\)" | python "$here/scrub.py" | cut -c1-300 | sort -u)
            ls "$project/bin/$config/net10.0/" | grep -E "^consumer\.(pdb)$" | sed 's/^/file: /'
            "$project/bin/$config/net10.0/consumer" 2>&1 | python "$here/scrub.py" | cut -c1-240
            echo "exit ${PIPESTATUS[0]}"
            "$project/bin/$config/net10.0/consumer" ref 2>&1 | grep -vE "^ +at " | python "$here/scrub.py" | cut -c1-200 | head -14
            echo "exit of the ref run: ${PIPESTATUS[0]}"
        } >> "$out"
        rm -rf "${project:?}/bin" "${project:?}/obj"
    done
done
# What a build itself says, in one more copy of the project, built afresh each time: the order of the two weavers and the list
# at normal verbosity; the warning under the terminal logger, whose colours are taken out to read it; and NoWarn.
project="$work/build-output"
rm -rf "${project:?}"
mkdir -p "$project"
cp "$here/Program.cs" "$here/consumer.csproj" "$here/Directory.Build.props" "$here/Directory.Build.targets" "$project/"
sed -e 's/\r$//' "$here/nuget.config" > "$project/nuget.config"
if [ -n "$feed" ]; then
    sed -i "s#<clear />#<clear />\n    <add key=\"local\" value=\"$(cygpath -m "$feed" 2>/dev/null || echo "$feed")\" />#" "$project/nuget.config"
fi
{
    echo "=================== $label, normal verbosity: the weavers in the order they ran, and what they listed"
    (cd "$project" && dotnet build consumer.csproj -c Debug -nologo -v:n -tl:off "${weavers[@]}" 2>&1 \
        | grep -E "^ +_Weave[A-Za-z]+:|step method\(s\)|, line [0-9]+, in " | python "$here/scrub.py" | cut -c1-260)
    rm -rf "${project:?}/bin" "${project:?}/obj"
    echo "=================== $label, terminal logger, -v:m -tl:on"
    (cd "$project" && dotnet build consumer.csproj -c Debug -nologo -v:m -tl:on "${weavers[@]}" 2>&1 \
        | python -c "import re,sys; [print(l.rstrip()) for l in re.sub(r'\x1b\[[0-9;?]*[A-Za-z]|\x1b\][^\x1b\a]*(\x1b\\\\|\a)', '', sys.stdin.read()).splitlines() if 'KRONIKOL00' in l]" \
        | python "$here/scrub.py" | cut -c1-260)
    rm -rf "${project:?}/bin" "${project:?}/obj"
    echo "=================== $label, NoWarn: lines naming KRONIKOL004 at -v:m -tl:off"
    (cd "$project" && dotnet build consumer.csproj -c Debug -nologo -v:m -tl:off "${weavers[@]}" "-p:NoWarn=KRONIKOL004;CS1591" 2>&1 \
        | grep -c "KRONIKOL004")
    rm -rf "${project:?}/bin" "${project:?}/obj"
    echo "=================== $label, PdbFile: symbols written somewhere other than beside the assembly"
    mkdir -p "$project/symbols"
    (cd "$project" && dotnet build consumer.csproj -c Debug -nologo -v:m -tl:off "${weavers[@]}" -p:PdbFile=symbols/consumer.pdb 2>&1 \
        | grep -E " error |warning KRONIKOL|Build succeeded" | python "$here/scrub.py" | cut -c1-240 | sort -u)
    "$project/bin/Debug/net10.0/consumer" 2>&1 | python "$here/scrub.py" | grep -A3 -E "^(void|a value step that throws|value \+ assertion)" | sed -E 's/ [0-9]+ ms,/,/' | cut -c1-160
} >> "$out"
rm -rf "${project:?}"

cat "$out" | grep -cE "^=+ " | sed 's/^/runs: /'
