#!/bin/sh
# R3c's IKVM search a second time (plans/CLIENT_FLOORS_PLAN.md F39). IKVM compiles Kronikol.PlantUml.Ikvm's PlantUML
# assembly at the package's build, so the package holds code its own IKVM release wrote, and a consumer on a newer
# release runs that code there. The first search built and ran each probe on one release, as every floor search had,
# and found 8.9.0 (floor/r3c-ikvm.txt); the suite's normal run, the package built on 8.9.0 and the suite on 8.16.3, then
# failed 46 of its 83 facts (floor/r3c-suites.txt). Here each probe builds the package on the release probed and runs
# the suite on its own newest 8.x (--no-floor-run), from 8.9.1 on, as the first search ran each release from 8.9.0 on
# itself. The releases restore into the shared NuGet cache (<packages>), where 8.16.3, which every probe runs on, and
# 8.15.0, main's pin, stay; each other release probed is removed from it after its probe, and every release probed from
# NuGet's HTTP cache, which keeps a copy whatever NUGET_PACKAGES says.
#   sh r3c-ikvm2.sh <worktree> <logs> <packages>
W=$1; LOGS=$2; PACKAGES=$3; H=$W/plans/CLIENT_FLOORS_PLAN.harness/floor
IKVM=IKVM,IKVM.Image,IKVM.Image.JDK,IKVM.Image.JRE,IKVM.MSBuild
python -I "$H/paired_floor.py" --worktree "$W" --project src/Kronikol.PlantUml.Ikvm/Kronikol.PlantUml.Ikvm.csproj \
  --tests tests/Kronikol.Tests.PlantUml.Ikvm --pin "$IKVM" --series "$IKVM" --property IkvmFloor --major 8 \
  --filter "FullyQualifiedName!~PackageTests" --no-floor-run --from 8.9.1 --env "NUGET_PACKAGES=$PACKAGES" \
  --after 'find "${LOCALAPPDATA:?}/NuGet/v3-cache" -maxdepth 2 -type f -name "nupkg_ikvm*.{version}.dat" -delete; case {version} in 8.15.0|8.16.3) exit 0;; esac; cd "${NUGET_PACKAGES:?}" && for p in ikvm*; do [ -d "${p:?}/{version}" ] && rm -rf "${p:?}/{version}"; done; true' \
  --out "$H/r3c-ikvm2.txt" --logs "$LOGS/ikvm2"
echo ALLDONE > "$H/r3c-ikvm2.done"
