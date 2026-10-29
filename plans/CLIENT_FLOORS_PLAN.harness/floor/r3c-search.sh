#!/bin/sh
# R3c's floor search (plans/CLIENT_FLOORS_PLAN.md 5.2): Kronikol.PlantUml.Ikvm's five IKVM references, one release line,
# binary-searched over the major with paired_floor.py, each probe pinning all five to the release and building the
# PlantUML jar with that release's compiler; the suite runs on the build.
# Each release's packages take about 8 GB (a JDK and a JRE image for every runtime), and the first run's three filled
# the disk: so the search restores into a NuGet cache of its own (<cache>), each probe's IKVM release is removed from
# it after the probe, and the search resumes from 8.8.0, the release after the first run's 8.7.6, which failed.
#   sh r3c-search.sh <worktree> <logs> <cache>
W=$1; LOGS=$2; CACHE=$3; H=$W/plans/CLIENT_FLOORS_PLAN.harness/floor
IKVM=IKVM,IKVM.Image,IKVM.Image.JDK,IKVM.Image.JRE,IKVM.MSBuild
mkdir -p "$CACHE"
echo "# the first run: 8.16.3 passed and 8.7.6 failed, then the disk filled during 8.12.0's build; this run resumes from 8.8.0 with a NuGet cache of its own" >> "$H/r3c-ikvm.txt"
python -I "$H/paired_floor.py" --worktree "$W" --project src/Kronikol.PlantUml.Ikvm/Kronikol.PlantUml.Ikvm.csproj \
  --tests tests/Kronikol.Tests.PlantUml.Ikvm --pin "$IKVM" --series "$IKVM" --property IkvmFloor --major 8 \
  --filter "FullyQualifiedName!~PackageTests" --env "NUGET_PACKAGES=$CACHE" --from 8.8.0 \
  --after 'cd "${NUGET_PACKAGES:?}" && for p in ikvm*; do [ -d "${p:?}/{version}" ] && rm -rf "${p:?}/{version}"; done; true' \
  --out "$H/r3c-ikvm.txt" --logs "$LOGS/ikvm"
echo ALLDONE > "$H/r3c-search.done"
