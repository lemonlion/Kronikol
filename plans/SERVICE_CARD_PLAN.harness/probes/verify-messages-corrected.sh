#!/bin/sh
# Writes the revised card with --corrected-broker-rule (plan 4.2 "Messages" as the verification pass of 2026-10-28
# corrected it) for the inputs revision-sizes-revised.sh read: run 40491266394's 18 lanes (68a8760, Kronikol 5.12.1),
# the Example.Api Reqnroll report and the Jest ingest stand-in. Prints the probe's lines (results/verify-sizes.txt; the
# table is revision-sizes_table.py over them, results/verify-sizes-table.txt). Then one control run without the flag,
# which must write the revision's card byte for byte; verify-messages_compare.py then compares every card with the
# revision's (results/verify-messages.txt).
# The Jest report is passed under the name the revision gave its copy, jest-ingest-TestRunReport.krkl, since the card's
# run.report.file is the name.
# Usage: sh verify-messages-corrected.sh <audit/B dir> <scratchpad dir> <probe dir> <out dir> <harness inputs dir> <jest report or -> <revision cards dir>
B=$1; S=$2; P=$3; O=$4; H=$5; J=$6; R=$7
mkdir -p "$O/corrected" "$O/control"; cd "$P" || exit 1
run() { echo "== $1"; dotnet run --no-build --file revision-card-revised.cs -- "$2" "$3" "$4" "$5" "$O/corrected/$1" --corrected-broker-rule 2>&1; }
for f in "$B/decoded/40491266394"/*.json; do
  l=$(basename "$f" .json); d="$B/downloads/40491266394/$l-report"
  run "C-$l" "$f" "$B/out/parties/C-$l.json" "$d/Run.json" "$d/TestRunReport.krkl"
done
run "example-ReqNRoll.xUnit3" "$S/decoded/example-reqnroll.json" "$B/out/parties/example-ReqNRoll.xUnit3.json" "$H/examples-5.8.0/ReqNRoll.xUnit3/Run.json" "$H/examples-5.8.0/ReqNRoll.xUnit3/TestRunReport.krkl"
run "jest-ingest" "$S/decoded/jest-ingest.json" "$B/out/parties/jest-ingest.json" "-" "$J"
echo "== control: C-xunit-in-memory without the flag"
d="$B/downloads/40491266394/xunit-in-memory-report"
dotnet run --no-build --file revision-card-revised.cs -- "$B/decoded/40491266394/xunit-in-memory.json" "$B/out/parties/C-xunit-in-memory.json" "$d/Run.json" "$d/TestRunReport.krkl" "$O/control/C-xunit-in-memory" 2>&1
if cmp -s "$O/control/C-xunit-in-memory.service.json" "$R/C-xunit-in-memory.service.json"; then
  echo "control card byte-identical to the revision's"
else
  echo "control card DIFFERS from the revision's"
fi
