#!/bin/sh
# Writes the whole service.json in the revised 4.3 shape (revision-card-revised.cs) for run 40491266394's 18 lanes (68a8760,
# Kronikol 5.12.1), the Example.Api Reqnroll report and the Jest ingest stand-in, and prints the probe's lines.
# Usage: sh revision-sizes-revised.sh <audit/B dir> <scratchpad dir> <probe dir> <out dir> <harness inputs dir> <jest report or ->
B=$1; S=$2; P=$3; O=$4; H=$5; J=$6
mkdir -p "$O"; cd "$P" || exit 1
run() { echo "== $1"; dotnet run --no-build --file revision-card-revised.cs -- "$2" "$3" "$4" "$5" "$O/$1" 2>&1; }
for f in "$B/decoded/40491266394"/*.json; do
  l=$(basename "$f" .json); d="$B/downloads/40491266394/$l-report"
  run "C-$l" "$f" "$B/out/parties/C-$l.json" "$d/Run.json" "$d/TestRunReport.krkl"
done
run "example-ReqNRoll.xUnit3" "$S/decoded/example-reqnroll.json" "$B/out/parties/example-ReqNRoll.xUnit3.json" "$H/examples-5.8.0/ReqNRoll.xUnit3/Run.json" "$H/examples-5.8.0/ReqNRoll.xUnit3/TestRunReport.krkl"
run "jest-ingest" "$S/decoded/jest-ingest.json" "$B/out/parties/jest-ingest.json" "-" "$J"
