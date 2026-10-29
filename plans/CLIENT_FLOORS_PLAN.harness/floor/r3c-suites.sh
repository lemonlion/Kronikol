#!/bin/sh
# R3c's suite at the release commit, on its floor (-p:FloorRun=true: the jar compiled by 8.9.0 and run on it) and on the
# newest (the jar the package builds, compiled by its floor, run on the newest 8.x, as a consumer on a newer IKVM runs
# it), each from an empty bin/ and obj/, the package's as well as the suite's (F39: the package's build used to copy
# its IKVM image into the suite's output). One line per run in <out>, with every advisory NuGet audit reported; logs in
# <logs>. An IKVM release is about 14.6 GB in the NuGet packages folder and 3.85 GB more in its HTTP cache, so the
# floor runs first, and each run's HTTP cache copy goes after the run (<http cache>: NuGet's v3-cache folder for
# nuget.org); free space is logged after each.
#   sh r3c-suites.sh <worktree> <out> <logs> <http cache>
W=$1; OUT=$2; LOGS=$3; V3=$4; mkdir -p "$LOGS"
cd "$W" || exit 1
T=tests/Kronikol.Tests.PlantUml.Ikvm; S=src/Kronikol.PlantUml.Ikvm
run() {
  name=$1; release=$2; shift 2
  rm -rf "$T/bin" "$T/obj" "$S/bin" "$S/obj"
  log="$LOGS/$name.log"
  timeout 3600 dotnet test "$T" "$@" -nologo -nodeReuse:false -p:NuGetAuditMode=all -- RunConfiguration.TreatNoTestsAsError=true > "$log" 2>&1
  code=$?
  summary=$(grep -aE "(Passed|Failed)!" "$log" | sed 's/^ *//; s/, Duration: [^-]*-/ -/' | tr -s ' ' | tr '\n' ' ')
  audit=$(grep -aoE "warning NU190[1-4]: Package '[^']+' [0-9.]+ has a known [a-z]+" "$log" | sort -u | sed "s/warning NU190[1-4]: Package //" | tr '\n' ';')
  resolved=$(grep -aoE '"IKVM/[0-9.]+' "$T/obj/project.assets.json" | sort -u | tr -d '"' | tr '\n' ' ')
  find "$V3" -maxdepth 1 -type f -name "nupkg_ikvm*.$release.dat" -delete
  echo "$name | exit $code | ${summary:-no summary }| audit: ${audit:-none} | resolved: $resolved" >> "$OUT"
  echo "# free after $name: $(df -B1M /c | tail -1 | awk '{print $4}') MB" >> "$OUT"
}
run "PlantUml.Ikvm floor" 8.9.0 -p:FloorRun=true
run "PlantUml.Ikvm" 8.16.3
echo "ALLDONE" >> "$OUT"
