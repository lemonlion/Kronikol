#!/usr/bin/env bash
# The VALUES pass: every case once, in a fresh process of its own (as run-cases.sh), with KRONIKOL_213_VALUES=1, which
# makes the Case fact redirect the woven Track.*WithValues calls to Probe213Recorder. The recorder keeps the values
# array the weave built and forwards to Track, so the value-loads and the note are unchanged; each value is then named by
# what it IS among the objects the run created (test instance, each Harness/C by tag, the entry's own task), or as
# UNCREATED, with its runtime type read last (the TYPES line), once everything else about the case is on disk.
#
# Each case writes, after its outcome line:
#   VALUES  [expression] name = <identity>, ...   (one bracket per recorded call)
#   TYPES   the same, with the type and text of any UNCREATED value
# A case that crashes has no values: the host dies in CastHelpers.StelemRef while the array is being built.
#
# Re-runnable against a changed weaver: PROBE213_LABEL=<name> writes values-<name>.txt and values-logs-<name>/ beside the
# 5.27.2 results (the default label keeps values-5.27.2.txt and values-logs/). The repository is the one this script
# sits in. PROBE213_ONLY_SHAPES="P1 P6" limits it.
set -u
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cd "$REPO"
LABEL="${PROBE213_LABEL:-5.27.2}"
# PROBE213_BIN: the folder holding the test executable (default: the project's own bin). A second build elsewhere lets
# this pass run while run-cases.sh is using the project's bin.
BIN="${PROBE213_BIN:-$(pwd)/tests/Kronikol.Tests.AssertionTracking/bin/Debug/net10.0}"
EXE="$BIN/Kronikol.Tests.AssertionTracking.exe"
RES="$(pwd)/plans/CAPTURED_FIELD_VALUES_PLAN.harness/results"
OUT="${PROBE213_OUT:-$RES/values-$LABEL.txt}"
if [ "$LABEL" = "5.27.2" ]; then LOGS="$RES/values-logs"; else LOGS="$RES/values-logs-$LABEL"; fi
METHOD="Kronikol.Tests.AssertionTracking.Probe213Tests.Case"

mkdir -p "$LOGS"
rm -f "$LOGS"/*.log "$LOGS"/*.out "$LOGS"/cases.list
: > "$OUT"
[ -x "$EXE" ] || { echo "no test executable at $EXE: build the test project first" >&2; exit 1; }

LIST="$LOGS/cases.list"
( cd "$BIN" && KRONIKOL_213_LIST="$LIST" "$EXE" -method "$METHOD" > "$LOGS/list.log" 2>&1 )
[ -s "$LIST" ] || { echo "the probe listed no cases (see $LOGS/list.log)" >&2; exit 1; }
tr -d '\r' < "$LIST" > "$LIST.unix" && mv "$LIST.unix" "$LIST"
echo "# VALUES pass: $(wc -l < "$LIST") cases, one per process, recorder on; label $LABEL; repo $REPO" >> "$OUT"

while IFS='|' read -r shape build entry; do
  [ -z "$shape" ] && continue
  if [ -n "${PROBE213_ONLY_SHAPES:-}" ] && [[ " $PROBE213_ONLY_SHAPES " != *" $shape "* ]]; then continue; fi
  tag="${shape}.${build}.${entry}"
  caseout="$LOGS/$tag.out"
  log="$LOGS/$tag.log"
  ( cd "$BIN" && KRONIKOL_213_VALUES=1 KRONIKOL_213_CASE="$shape" KRONIKOL_213_CASE_OUT="$caseout" \
      KRONIKOL_213_CASE_ONLY="$build|$entry" /usr/bin/timeout -s KILL 240 "$EXE" -method "$METHOD" > "$log" 2>&1 )
  rc=$?
  result=$(grep -m1 -E "^RESULT " "$caseout" 2>/dev/null | sed -E 's/^RESULT [^ ]+ :: //' | tr -d '\r')
  values=$(grep -m1 -E "^VALUES " "$caseout" 2>/dev/null | sed -E 's/^VALUES [^ ]+ :: //' | tr -d '\r')
  types=$(grep -m1 -E "^TYPES " "$caseout" 2>/dev/null | sed -E 's/^TYPES [^ ]+ :: //' | tr -d '\r')
  if [ -n "$result" ]; then
    echo "${shape}|${build}|${entry} :: ${result}" >> "$OUT"
    echo "    VALUES ${values:-(none written)}" >> "$OUT"
    if [ -n "$types" ]; then
      echo "    TYPES  ${types}" >> "$OUT"
    elif [ -n "$values" ]; then
      echo "    TYPES  (the process ended while reading an UNCREATED value's type, rc=$rc)" >> "$OUT"
    fi
  elif grep -qE "^Fatal error\.|AccessViolationException" "$log"; then
    exc=$(grep -m1 -oE "System\.[A-Za-z.]+Exception" "$log")
    where=$(grep -E "^ +at " "$log" | grep -m1 -vE "CastHelpers|System\.Runtime|System\.Threading" | sed -E 's/^ +at //' | tr -d '\r')
    echo "${shape}|${build}|${entry} :: HOST-CRASH ${exc:-?} in ${where:-?} (rc=$rc): no values reached Track" >> "$OUT"
  elif [ "$rc" -eq 124 ] || [ "$rc" -eq 137 ]; then
    echo "${shape}|${build}|${entry} :: HOST-HANG (killed, rc=$rc)" >> "$OUT"
  else
    echo "${shape}|${build}|${entry} :: FACT-FAILED rc=$rc (see values-logs/$tag.log)" >> "$OUT"
  fi
done < "$LIST"
echo "VALUES PASS COMPLETE" >> "$OUT"
