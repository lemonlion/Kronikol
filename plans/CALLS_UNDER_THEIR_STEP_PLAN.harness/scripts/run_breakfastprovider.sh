#!/bin/bash
# The differential on BreakfastProvider's in-memory lanes: published against the two prototypes built as one.
# Usage: run_breakfastprovider.sh <prototype worktree, with apply_probe_one.py applied> <work dir> <results dir> <bp-bump.py> [lanes]
# Works in a scratch clone, never in C:/Code/BreakfastProvider. Reports are read only through the query engine.
set -u
PROTO=$1; WORK=$2; RESULTS=$3; BUMP=$4; LANES=${5:-"ReqNRoll LightBDD BDDfy NUnit"}
VER=${VER:-5.28.3-proto225a}
PUB=5.28.2
FEED=$WORK/feed
CLONE=$WORK/bp
mkdir -p "$WORK" "$FEED" "$RESULTS"

ids="Kronikol Kronikol.StepTracking Kronikol.AssertionTracking Kronikol.ReqNRoll.Core Kronikol.ReqNRoll.xUnit3 Kronikol.LightBDD.Core Kronikol.LightBDD.xUnit3 Kronikol.BDDfy.xUnit3 Kronikol.xUnit3 Kronikol.NUnit4 Kronikol.TUnit"

pack() {
  echo "== pack $VER $(date +%H:%M:%S)"
  cd "$PROTO" || exit 1
  for id in $ids; do
    dotnet build "src/$id/$id.csproj" -c Release -p:Version=$VER -p:WarningLevel=0 -v q -nologo 2>&1 | grep -E " error |rror\(s\)" | sort -u | head -3
    dotnet pack "src/$id/$id.csproj" -c Release --no-build -p:Version=$VER -o "$FEED" -v q -nologo 2>&1 | grep -E " error " | head -3
  done
  ls "$FEED" | wc -l
}

clone() {
  [ -d "$CLONE/.git" ] || git clone --quiet "$(git -C /c/Code/BreakfastProvider remote get-url origin)" "$CLONE"
  cd "$CLONE" || exit 1
  git fetch --quiet origin
  git checkout --quiet --force origin/main
  git clean -fdxq -e bin -e obj
  git log --oneline -1 | cut -c1-90
  local old; old=$(git grep -h -o 'Include="Kronikol.AssertionTracking" Version="[^"]*"' -- '*.csproj' | head -1 | sed 's/.*Version="//; s/"//')
  echo "pins at $old"
  python "$BUMP" "$CLONE" "$old" $PUB | tail -1
  cat > "$CLONE/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="k225" value="$(cygpath -w "$FEED")" />
  </packageSources>
</configuration>
EOF
}

run_lane() { # lane label
  local lane=$1 label=$2 proj="tests/BreakfastProvider.Tests.Component.$1"
  local probe=""; [ "$label" = proto ] && probe="$(cygpath -w "$RESULTS")/bp-$lane.tsv"
  [ -n "$probe" ] && rm -f "$RESULTS/bp-$lane.tsv"
  echo "== $label $lane $(date +%H:%M:%S)"
  cd "$CLONE" || exit 1
  find "$proj" -type d -name Reports -prune -exec rm -rf {} + 2>/dev/null
  KRONIKOL_PROBE_ATTRIBUTION="$probe" KRONIKOL_SUITE=$(echo "$lane" | tr A-Z a-z)-in-memory \
    timeout 1500 dotnet test "$proj/BreakfastProvider.Tests.Component.$lane.csproj" -p:WarningLevel=0 2>&1 | grep -E "^(Passed!|Failed!)|Test Run|Total:|  Failed: | error |Tests failed|succeeded:|failed:" | tail -6 | cut -c1-200
  # A lane's first run has failed every scenario in 0 ms straight after its build, and passed in full on the next run with nothing changed.
  if [ "$(find "$proj" -type f -name 'Failures.md' -path '*Reports*' | wc -l)" = 0 ]; then
    echo "no report: running the lane once more"
    KRONIKOL_PROBE_ATTRIBUTION="$probe" KRONIKOL_SUITE=$(echo "$lane" | tr A-Z a-z)-in-memory       timeout 1500 dotnet test "$proj/BreakfastProvider.Tests.Component.$lane.csproj" --no-build -p:WarningLevel=0 2>&1 | grep -E "^(Passed!|Failed!)|Test Run|Total:|  Failed: | error |Tests failed|succeeded:|failed:" | tail -4 | cut -c1-200
  fi
  local rep; rep=$(find "$proj" -type f -name 'Failures.md' -path '*Reports*' | head -1)
  mkdir -p "$WORK/out/$label"
  rm -rf "$WORK/out/$label/$lane"
  [ -n "$rep" ] && cp -r "$(dirname "$rep")" "$WORK/out/$label/$lane" && echo "reports: $(dirname "$rep")"
}

# Only what was packed moves to the prototype's version. The extension packages stay on the published one, and take the prototype's core by NuGet's lowest-applicable rule.
bump_packed() {
  cd "$CLONE" || exit 1
  git ls-files '*.csproj' | while read -r f; do
    for id in $ids; do
      sed -i -E 's/(Include="'"$id"'" Version=")[^"]*"/\1'"$1"'"/' "$f"
    done
  done
}

pack
clone
if [ "${SKIP_CONTROL:-}" != 1 ]; then
  for lane in $LANES; do run_lane "$lane" published; done
fi
bump_packed $VER
for lane in $LANES; do run_lane "$lane" proto; done
echo "== done $(date +%H:%M:%S)"
