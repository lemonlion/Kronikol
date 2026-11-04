#!/usr/bin/env bash
# The issue's own setting: a Reqnroll suite whose step definitions call [Step] methods, through dotnet test and the real adapter, read
# back from the report with the query.cs each run writes beside it. Run twice, since on the published packages the @byref scenario takes
# the test host and every result with it.
#   run-reqnroll.sh <label> <Kronikol.StepTracking version> [local feed]
set -u

label="$1"
version="$2"
feed="${3:-}"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
results="$here/../results"
work="$(cygpath -u "$TEMP" 2>/dev/null || echo "${TMPDIR:-/tmp}")/kronikol-226-consumer/reqnroll/$label"
out="$results/reqnroll-$label.txt"
rm -rf "${work:?}"
mkdir -p "$results" "$work/Features"
cp "$here/Spike.Shapes.csproj" "$here/reqnroll.json" "$here/Steps.cs" "$work/"
cp "$here/Features/Shapes.feature" "$work/Features/"
cp "$here/../consumer/Directory.Build.props" "$here/../consumer/Directory.Build.targets" "$work/"
sed -e 's/\r$//' "$here/../consumer/nuget.config" > "$work/nuget.config"
if [ -n "$feed" ]; then
    sed -i "s#<clear />#<clear />\n    <add key=\"local\" value=\"$(cygpath -m "$feed" 2>/dev/null || echo "$feed")\" />#" "$work/nuget.config"
fi
scrub() { python "$here/../consumer/scrub.py" | sed -E 's#[A-Za-z]:[\\/][^ ]*kronikol-226-consumer[\\/]reqnroll[\\/][^\\/ ]*[\\/]##g' | cut -c1-430; }
reports="$work/bin/Debug/net10.0/Reports"

{
    echo "$label: Reqnroll + Kronikol.ReqNRoll.xUnit2 5.28.2, Kronikol.StepTracking $version"
    (cd "$work" && dotnet build -nologo -v:m -tl:off -p:StepTrackingVersion="$version" -p:AssertionTrackingVersion="${ASSERTION_TRACKING_VERSION:-5.28.2}" 2>&1 | grep -E " error |warning KRONIKOL|Build succeeded" | scrub | sort -u)

    echo
    echo "=== dotnet test, the @byref scenario alone"
    (cd "$work" && timeout 280 dotnet test --no-build -nologo --filter "Category=byref" 2>&1 \
        | grep -E "Catastrophic|FATAL|Fatal error|No test is available|Passed!|Failed!|^(Passed|Failed) " | scrub | head -8)
    echo "exit ${PIPESTATUS[0]}"

    echo
    echo "=== dotnet test, the rest"
    rm -rf "${reports:?}"
    (cd "$work" && timeout 280 dotnet test --no-build -nologo --filter "Category!=byref" 2>&1 \
        | grep -E "Catastrophic|FATAL|No test is available|Passed!|Failed!|^ +Failed " | scrub | head -14)

    echo
    echo "=== the report's scenarios"
    query() { dotnet run --file "$reports/query.cs" -- "$@" 2>&1 | scrub; }
    query scenarios "$reports" | grep -E "^ *s[0-9]+|^ *[a-z0-9]+ +(passed|failed)|passed|failed" | head -14
    for address in s0 s1 s2 s3 s4 s5 s6; do
        echo
        echo "--- steps $address"
        query steps "$reports" "$address" | grep -vE "^(next|more|see|  kronikol|.*query\.cs)" | head -24
    done
    echo
    echo "=== Failures.md, without the file's own header"
    sed -n '/^## /,$p' "$reports/Failures.md" | grep -vE '^$' | scrub | head -70
} > "$out" 2>&1
rm -rf "${work:?}/bin" "${work:?}/obj"
tail -3 "$out" | cut -c1-200
