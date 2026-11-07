#!/usr/bin/env bash
# T1 to T7 (repro/XunitV2 and repro/XunitV3) on the published packages of one version: builds each project in Debug and
# Release, runs run.sh over every class, dumps the woven IL and reads its values arrays. Usage: ./rerun-version.sh <version>
set -u
V=$1
H="$(cd "$(dirname "$0")" && pwd)"
cd "$H"
CLASSES="PrimaryConstructorParameterTests IssueFirstShapeTests SyncMethodControlTests OrdinaryFieldControlTests OneParameterTests SixParametersTests ParametersAndValueFieldsTests"
for p in XunitV2 XunitV3; do
  lbl=$(echo $p | tr 'A-Z' 'a-z' | sed 's/xunitv/xunit/')
  rm -rf repro/$p/bin repro/$p/obj
  for cfg in Debug Release; do
    (cd repro/$p && dotnet build -c $cfg -v:n -p:KronikolVersion=$V) > results/build-$lbl-$V-$cfg.txt 2>&1
    echo "build $p $cfg exit $? ($(grep -c 'Instrumented' results/build-$lbl-$V-$cfg.txt) Instrumented lines)"
    dll=$(ls repro/$p/bin/$cfg/net10.0/*Repro.dll | head -1)
    ilspycmd --disable-updatecheck -il "$dll" > il/$lbl-$V-$cfg.il 2>/dev/null
    ./run.sh $lbl-$V repro/$p $cfg $CLASSES > /dev/null 2>&1
    echo "ran $p $cfg"
  done
done
python -I outcomes.py results/xunit2-$V-*.txt results/xunit3-$V-*.txt > results/outcomes-$V.txt 2>&1
for f in il/xunit2-$V-*.il il/xunit3-$V-*.il; do echo "=== $f"; python -I values_il.py "$f"; done > results/il-values-arrays-$V.txt 2>&1
echo DONE
