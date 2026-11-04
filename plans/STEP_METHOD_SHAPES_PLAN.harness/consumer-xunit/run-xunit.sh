#!/usr/bin/env bash
# dotnet test on three facts, the second calling [Step] void M(in int x), on the step weaver version given.
#   run-xunit.sh <label> <Kronikol.StepTracking version> [local feed]
set -u

label="$1"
version="$2"
feed="${3:-}"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
results="$here/../results"
work="$(cygpath -u "$TEMP" 2>/dev/null || echo "${TMPDIR:-/tmp}")/kronikol-226-consumer/xunit/$label"
rm -rf "${work:?}"
mkdir -p "$work" "$results"
cp "$here/Facts.cs" "$here/xunit-probe.csproj" "$here/Directory.Build.props" "$here/Directory.Build.targets" "$work/"
sed -e 's/\r$//' "$here/nuget.config" > "$work/nuget.config"
if [ -n "$feed" ]; then
    sed -i "s#<clear />#<clear />\n    <add key=\"local\" value=\"$(cygpath -m "$feed" 2>/dev/null || echo "$feed")\" />#" "$work/nuget.config"
fi
{
    echo "$label: dotnet test on three facts, the second calling [Step] void M(in int x)"
    (cd "$work" && dotnet test xunit-probe.csproj -nologo -tl:off -p:StepTrackingVersion="$version" 2>&1; echo "exit $?") \
        | grep -vE "^\s+at |^\s*$|Determining projects|Restored " | python "$here/../consumer/scrub.py" | cut -c1-240 | head -30
} | tee "$results/xunit-$label.txt"
rm -rf "${work:?}/bin" "${work:?}/obj"
