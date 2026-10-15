#!/usr/bin/env bash
# One measurement row: a clean build and test run of the project, the weave's lines, the notes, the PDB's documents.
# Usage: run_row.sh <row> <project-dir> <Debug|Release> [extra dotnet test args...]
set -u
export MSYS_NO_PATHCONV=1 MSYS2_ARG_CONV_EXCL='*' MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 PYTHONUTF8=1
R=<S>
ROW=$1; P=$2; CFG=$3; shift 3
L=$R/logs
cd "$P" || exit 1
rm -rf bin obj
S=$L/$ROW-summary.txt
{
  echo "row $ROW"
  echo "dir $P"
  echo "git: $(git rev-parse --show-toplevel 2>&1 | head -1)"
  echo "command: dotnet test -c $CFG $* -v:n"
} > "$S"
START=$(date +%s)
dotnet test -c "$CFG" "$@" -v:n > "$L/$ROW-test.log" 2>&1
EXIT=$?
echo "dotnet test exit $EXIT ($(( $(date +%s) - START ))s)" >> "$S"
{
  echo "--- weave, warnings, errors, PathMap (deduplicated)"
  grep -E "Kronikol\.AssertionTracking|AssertionTracking:|KRONIKOL|warning |error |PathMap=|_DumpPathMap|_SnapshotPreWeave|unwoven|Passed!|Failed!|Total tests" "$L/$ROW-test.log" \
    | sed -E 's/^ +//; s/ \[[^]]*\.csproj(::TargetFramework=[^]]*)?\]$//' | sort -u
} >> "$S"
REP=$(find "$P/bin" -name TestRunReport.json 2>/dev/null | head -1)
if [ -n "$REP" ]; then
  RD=$(dirname "$REP")
  echo "reports $RD" >> "$S"
  "$R/tool/kronikol" query assertions "$RD" > "$L/$ROW-assertions.txt" 2>&1
  echo "query exit $?" >> "$S"
  python "$R/summarize.py" "$L/$ROW-assertions.txt" >> "$S"
  # Every woven assertion is a diagram note; `assertions` lists only the ones inside a step (s0), not s1/s2.
  {
    echo "### notes with a pass mark"; "$R/tool/kronikol" query grep "$RD" "✓" --in notes --max-bytes 0
    echo "### notes with a fail mark"; "$R/tool/kronikol" query grep "$RD" "✗" --in notes --max-bytes 0
    echo "### notes reading 'Assertion at line'"; "$R/tool/kronikol" query grep "$RD" "Assertion at line" --in notes --max-bytes 0
  } > "$L/$ROW-notes.txt" 2>&1
  echo "diagram notes (✓ hits / ✗ hits / 'Assertion at line' hits): $(grep -E '^[0-9]+ hits|is not in notes' "$L/$ROW-notes.txt" | sed -E 's/ hits//; s/.*is not in notes/0/' | paste -sd/ -)" >> "$S"
  awk '/^### notes with a pass mark/{f=1;next} /^###/{f=0} f && /^s[0-9]+\//' "$L/$ROW-notes.txt" | sed 's/^/  /' >> "$S"
else
  echo "reports: none found" >> "$S"
fi
TFM=net8.0
OBJ=$P/obj/$CFG/$TFM
{
  for f in "$OBJ/preweave/IssueStack.dll" "$OBJ/preweave/IssueStack.pdb" "$OBJ/IssueStack.dll" "$OBJ/IssueStack.pdb"; do
    [ -f "$f" ] && echo "$(sha256sum "$f" | cut -c1-16)  $(stat -c %s "$f") bytes  ${f#$P/}"
  done
} >> "$S"
DUMP=()
for f in "$OBJ/preweave/IssueStack.dll" "$OBJ/preweave/IssueStack.pdb" "$OBJ/IssueStack.dll" "$OBJ/IssueStack.pdb"; do
  [ -f "$f" ] && DUMP+=("$f")
done
dotnet run --file "$R/dumpdocs/dumpdocs.cs" -- "${DUMP[@]}" > "$L/$ROW-pdb.txt" 2>&1
echo "pdb dump exit $? -> logs/$ROW-pdb.txt" >> "$S"
cat "$S"
