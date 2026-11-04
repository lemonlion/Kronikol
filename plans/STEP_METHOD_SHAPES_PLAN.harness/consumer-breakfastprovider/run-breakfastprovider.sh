#!/usr/bin/env bash
# BreakfastProvider's three lanes with woven steps and assertions, on the packages it pins and then with the prototype's two weavers
# over the core it pins, since a release moves the weavers together. A clone of its own: nothing here reaches its repository. Its 15 step methods are shapes that work today, and
# every one of them gets new IL, so each lane must pass as before and read the same. Prints the numbers results/breakfastprovider.txt holds.
#   WORK=<a folder of your own, holding feed/ with the packages of the prototype> BP_CLONE=<a clone of BreakfastProvider, on a short path> run-breakfastprovider.sh <the version it pins>
set -u

W="${WORK:?set WORK to a folder of your own, holding feed/ with the packages of the prototype}"
B="${BP_CLONE:?set BP_CLONE to a clone of BreakfastProvider, on a short path}"
pinned="$1"
prototype=5.28.99-proto226
cd "$B" || exit 1

lane() {
    local name=$1 project=$2 label=$3 log="$W/bp-$1-$3.log" marker="$W/bp-started"
    touch "$marker"
    export KRONIKOL_SUITE="$name-in-memory"
    find . -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} + 2>/dev/null
    dotnet test "tests/$project/$project.csproj" -p:WarningLevel=0 > "$log" 2>&1
    echo "$name $label: exit $?, $(grep -E "succeeded:|failed:|total:" "$log" | tr -s ' ' | sort -u | tr '\n' ' '), KRONIKOL warnings: $(grep -c "warning KRONIKOL" "$log")"
    local reports
    reports=$(find . -type f -name "Failures.md" -newer "$marker" | head -1)
    rm -rf "$W/bp-reports-$name-$label"; mkdir -p "$W/bp-reports-$name-$label"
    if [ -z "$reports" ]; then
        echo "no report found"
        return
    fi
    cp -r "$(dirname "$reports")/." "$W/bp-reports-$name-$label/"
    (
        cd "$W/bp-reports-$name-$label" || exit 1
        id=$(dotnet run --file query.cs -- scenarios . --feature "Audit Logs Retrieval Tests" 2>&1 | grep -oE "\bs[0-9]+\b" | head -1)
        dotnet run --file query.cs -- steps . "$id" 2>&1 | sed -E 's/[0-9]+(\.[0-9]+)? ?(ms|s)\b//g; s/ +$//; /^s[0-9]/d; /^open:/d; /^stableId/d' > "$W/bp-steps-$name-$label.txt"
    )
}

for weaver in published prototype; do
    if [ "$weaver" = prototype ]; then
        for package in Kronikol.StepTracking Kronikol.AssertionTracking; do
            sed -i "s#Include=\"$package\" Version=\"$pinned\"#Include=\"$package\" Version=\"$prototype\"#" $(grep -rl "Include=\"$package\"" --include=*.csproj .)
        done
        cat > nuget.config <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear /><add key="local" value="$(cygpath -m "$W/feed")" /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources></configuration>
EOF
    fi
    lane xunit BreakfastProvider.Tests.Component.xUnit "$weaver"
    lane nunit BreakfastProvider.Tests.Component.NUnit "$weaver"
    lane tunit BreakfastProvider.Tests.Component.TUnit "$weaver"
done

for name in xunit nunit tunit; do
    echo "== $name, the scenario with the woven steps, published against prototype, timings taken out:"
    diff "$W/bp-steps-$name-published.txt" "$W/bp-steps-$name-prototype.txt" && echo "no line differs"
done
