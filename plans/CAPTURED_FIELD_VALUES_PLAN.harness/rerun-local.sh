#!/usr/bin/env bash
# T1 to T7 (repro/XunitV2 and repro/XunitV3) with a locally packed Kronikol.AssertionTracking (the release's weaver)
# over the published Kronikol core of the same day: builds each project in Debug and Release, runs run.sh over every
# class, and reads the outcomes. Usage: ./rerun-local.sh <feed folder> <weaver version> <published core version>
set -u
FEED=$1; W=$2; V=$3
H="$(cd "$(dirname "$0")" && pwd)"
cd "$H"
CONFIG=$(mktemp -d)/nuget.config
cat > "$CONFIG" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$(cygpath -w "$FEED")" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF
CLASSES="PrimaryConstructorParameterTests IssueFirstShapeTests SyncMethodControlTests OrdinaryFieldControlTests OneParameterTests SixParametersTests ParametersAndValueFieldsTests"
for p in XunitV2 XunitV3; do
  lbl=$(echo $p | tr 'A-Z' 'a-z' | sed 's/xunitv/xunit/')
  rm -rf repro/$p/bin repro/$p/obj
  (cd repro/$p && dotnet restore --configfile "$CONFIG" -p:KronikolVersion=$V -p:AssertionTrackingVersion=$W) > results/restore-$lbl-$W.txt 2>&1
  echo "restore $p exit $?"
  for cfg in Debug Release; do
    (cd repro/$p && dotnet build --no-restore -c $cfg -v:n -p:KronikolVersion=$V -p:AssertionTrackingVersion=$W) > results/build-$lbl-$W-$cfg.txt 2>&1
    echo "build $p $cfg exit $? ($(grep -c 'Instrumented' results/build-$lbl-$W-$cfg.txt) Instrumented lines)"
    ./run.sh $lbl-$W repro/$p $cfg $CLASSES > /dev/null 2>&1
    echo "ran $p $cfg"
  done
  rm -rf repro/$p/bin repro/$p/obj
done
python -I outcomes.py results/xunit2-$W-*.txt results/xunit3-$W-*.txt > results/outcomes-$W.txt 2>&1
echo DONE
