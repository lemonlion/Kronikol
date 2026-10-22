#!/bin/bash
# Runs the #101 probe and keeps what it wrote in results/<label>/.
#   run.sh <work-dir> before [version]   the published Kronikol.ReqNRoll.xUnit2 (default 5.4.0) from nuget.org
#   run.sh <work-dir> after <checkout>    the adapter of a checkout (ProjectReference), e.g. a prototype
#   PROBE_LABEL=<name> before it names the results folder (default before-<version> or after)
# The probe is copied into <work-dir> first, outside the repository, so the repository's Directory.Build.props does
# not apply to it. Reports are never opened: names, ids and values are read back through the report's own query.cs.
set -u
H="$(cd "$(dirname "$0")" && pwd)"
W="$1"; MODE="$2"; ARG="${3:-}"
case "$MODE" in
  before) LABEL="before-${ARG:-5.4.0}"; PROPS="-p:KronikolVersion=${ARG:-5.4.0}" ;;
  after)  LABEL="after"; PROPS="-p:KronikolVersion=0.0.0 -p:KronikolSource=$ARG" ;;
  *) echo "usage: run.sh <work-dir> before [version] | after <checkout>"; exit 2 ;;
esac
LABEL="${PROBE_LABEL:-$LABEL}"   # another name for the answers, so a re-run keeps the recorded ones
D="$W/$LABEL"; OUT="$H/results/$LABEL"
rm -rf "$D" "$OUT"; mkdir -p "$D" "$OUT"
cp -r "$H/src/." "$D/"
cd "$D/xunit2" || exit 1
dotnet test $PROPS > "$OUT/test-log.txt" 2>&1; echo "dotnet test exit $?" | tee -a "$OUT/test-log.txt"
R="$D/xunit2/bin/Debug/net10.0/Reports"
cp "$R/Failures.md" "$OUT/" 2>/dev/null || echo "no Failures.md" | tee -a "$OUT/test-log.txt"
cp "$D/xunit2/bin/Debug/net10.0/cucumber-messages.ndjson" "$OUT/" 2>/dev/null || echo "no cucumber-messages.ndjson" | tee -a "$OUT/test-log.txt"
for q in "summary" "scenarios" "failures"; do
  f="$OUT/query-$q.txt"
  dotnet run --file "$R/query.cs" -- $q "$R" > "$f" 2>&1; echo "exit $?" >> "$f"
done
# every row, uncapped, for tsv.py
dotnet run --file "$R/query.cs" -- scenarios --json --max-bytes 0 "$R" > "$OUT/query-scenarios-all.json"
ls -la "$R" > "$OUT/reports-dir.txt"
echo "done: $OUT"
