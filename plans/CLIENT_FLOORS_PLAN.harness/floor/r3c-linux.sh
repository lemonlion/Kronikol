#!/bin/sh
# R3c's suite on Linux, as the plan's traps ask of a package that carries platform parts (F37): IKVM brings a Java
# runtime image for each platform. Runs in mcr.microsoft.com/dotnet/sdk:10.0 with the release tree at /repo, on the
# floor (-p:FloorRun=true: 8.9.0) and on the newest 8.x (the package built on 8.9.0), each from an empty bin/ and obj/
# of the package and the suite, with Graphviz installed as CI's IKVM job installs it. One line per run in <out>, logs
# in <logs>.
#   sh r3c-linux.sh <out> <logs>
OUT=$1; LOGS=$2; mkdir -p "$LOGS"
cd /repo || exit 1
{ apt-get update -qq && apt-get install -y -qq graphviz; } > "$LOGS/apt.log" 2>&1 || { echo "graphviz install failed" >> "$OUT"; exit 1; }
T=tests/Kronikol.Tests.PlantUml.Ikvm; S=src/Kronikol.PlantUml.Ikvm
run() {
  name=$1; shift
  rm -rf "$T/bin" "$T/obj" "$S/bin" "$S/obj"
  timeout 3600 dotnet test "$T" "$@" -nologo -p:NuGetAuditMode=all -- RunConfiguration.TreatNoTestsAsError=true > "$LOGS/$name.log" 2>&1
  code=$?
  summary=$(grep -aE "(Passed|Failed)!" "$LOGS/$name.log" | sed 's/^ *//; s/, Duration: [^-]*-/ -/' | tr -s ' ' | tr '\n' ' ')
  resolved=$(grep -aoE '"IKVM/[0-9.]+' "$T/obj/project.assets.json" | sort -u | tr -d '"' | tr '\n' ' ')
  echo "$name | exit $code | ${summary:-no summary }| resolved: $resolved | $(uname -sm), $(dot -V 2>&1)" >> "$OUT"
}
run "PlantUml.Ikvm floor, Linux" -p:FloorRun=true
run "PlantUml.Ikvm, Linux"
echo ALLDONE >> "$OUT"
