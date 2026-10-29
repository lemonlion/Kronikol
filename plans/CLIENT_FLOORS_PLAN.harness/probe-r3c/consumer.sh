#!/bin/sh
# F40 (plans/CLIENT_FLOORS_PLAN.md): a project that takes Kronikol.PlantUml.Ikvm from a feed, as a consumer does. Packs
# Kronikol and Kronikol.PlantUml.Ikvm, built (Debug) in <worktree>, as <version> into <dir>/feed, copies consumer/ to
# <dir>, outside the repository so the repository's build props do not apply, and runs it three ways: the package alone,
# IKVM alone raised to <newest> (it brings IKVM.Image and IKVM.MSBuild with it), and all five raised. Each from an empty
# bin/ and obj/; what it drew, and the IKVM releases it resolved, in consumer/run-<version>-<how>.txt.
#   sh consumer.sh <worktree> <dir> <version> <newest>
W=$1; D=$2; V=$3; N=$4; H=$(cd "$(dirname "$0")" && pwd)
mkdir -p "$D/feed"
for p in Kronikol Kronikol.PlantUml.Ikvm; do
  dotnet pack "$W/src/$p/$p.csproj" --no-build --nologo -c Debug -p:TargetFrameworks=net10.0 -p:Version="$V" -o "$D/feed" > "$D/pack-$p.log" 2>&1 || { echo "pack $p failed"; exit 1; }
done
cp "$H/consumer/consumer.csproj" "$H/consumer/Program.cs" "$H/consumer/nuget.config" "$D/"
cd "$D" || exit 1
run() {
  how=$1; shift
  rm -rf bin obj
  out="$H/consumer/run-$V-$how.txt"
  timeout 1500 dotnet build -nologo -nodeReuse:false -p:KronikolVersion="$V" "$@" > "build-$how.log" 2>&1
  echo "build exit $?" > "$out"
  timeout 600 dotnet run --no-build -p:KronikolVersion="$V" "$@" >> "$out" 2>&1
  echo "exit $?" >> "$out"
  echo "resolved: $(grep -oE '"IKVM[A-Za-z.]*/[0-9.]+"' obj/project.assets.json | sort -u | tr -d '"' | tr '\n' ' ')" >> "$out"
}
run alone
run ikvm-raised -p:IkvmVersion="$N"
run all-raised -p:IkvmVersion="$N" -p:AllIkvm=true
