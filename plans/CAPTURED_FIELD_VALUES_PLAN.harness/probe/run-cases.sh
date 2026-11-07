#!/usr/bin/env bash
# Runs every case of Probe213Tests.Case in a FRESH PROCESS OF ITS OWN, through the test project's own xUnit v3
# executable (the same assembly and facts `dotnet test` drives). One case per process gives full isolation: a case that
# kills its host (an AccessViolationException in CastHelpers.StelemRef cannot be caught) takes nothing else with it, no
# resume logic is needed, and the runtime's own "Fatal error." stack lands in that case's log. (Under `dotnet test` the
# VSTest bridge reports only "Test process crashed with exit code -1073741819", about 61 s after the crash.)
#
# The case list comes from the probe itself (KRONIKOL_213_LIST), so the shapes' entries live in one place.
# Round 1 runs every case. Rounds 2 and 3 run again the cases of the shapes whose load the static dive shows is wrong,
# because a mis-read is nondeterministic (the issue's suite aborted in 2 of 3 runs).
#
# Each line of OUT:  R<round> <shape>|<build>|<entry> :: <outcome>
#   outcome = the fact's RESULT payload (unwoven / woven / note), or
#             HOST-CRASH <exception> at <top frame> (<process exit>), or HOST-HANG (killed after the timeout), or
#             FACT-FAILED rc=<n> (a managed exception escaped the fact: not a crash)
#
# Re-runnable against a changed weaver: PROBE213_LABEL=<name> writes case-<name>.txt and case-logs-<name>/ beside the
# 5.27.2 results instead of over them (the default label, 5.27.2, keeps case-5.27.2.txt and case-logs/). The repository is
# the one this script sits in, so a copy in another worktree measures that worktree. PROBE213_ROUNDS=1 for one round.
set -u
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cd "$REPO"
LABEL="${PROBE213_LABEL:-5.27.2}"
BIN=tests/Kronikol.Tests.AssertionTracking/bin/Debug/net10.0
EXE="$(pwd)/$BIN/Kronikol.Tests.AssertionTracking.exe"
RES="$(pwd)/plans/CAPTURED_FIELD_VALUES_PLAN.harness/results"
OUT="${PROBE213_OUT:-$RES/case-$LABEL.txt}"
if [ "$LABEL" = "5.27.2" ]; then LOGS="$RES/case-logs"; else LOGS="$RES/case-logs-$LABEL"; fi
ROUNDS="${PROBE213_ROUNDS:-3}"
METHOD="Kronikol.Tests.AssertionTracking.Probe213Tests.Case"
REPEAT_SHAPES=" P1 P2 P6 P10 P13 P14 P15 P16 P19 P22 P22b P23 P12_1p P12_2p P12_4p P12_6p P12_1p_fields P12_2p_fields P12_4p_fields P12_6p_fields "

mkdir -p "$LOGS"
rm -f "$LOGS"/*.log "$LOGS"/*.out
: > "$OUT"
[ -x "$EXE" ] || { echo "no test executable at $EXE: build the test project first" >&2; exit 1; }

# Ask the probe for its cases.
LIST="$LOGS/cases.list"
( cd "$BIN" && KRONIKOL_213_LIST="$LIST" "$EXE" -method "$METHOD" > "$LOGS/list.log" 2>&1 )
[ -s "$LIST" ] || { echo "the probe listed no cases (see $LOGS/list.log)" >&2; exit 1; }
tr -d '\r' < "$LIST" > "$LIST.unix" && mv "$LIST.unix" "$LIST"
echo "# $(wc -l < "$LIST") cases; rounds=$ROUNDS; label $LABEL; repo $REPO; one case per process via the test executable" >> "$OUT"

for round in $(seq 1 "$ROUNDS"); do
  while IFS='|' read -r shape build entry; do
    [ -z "$shape" ] && continue
    if [ -n "${PROBE213_ONLY_SHAPES:-}" ] && [[ " $PROBE213_ONLY_SHAPES " != *" $shape "* ]]; then continue; fi
    if [ "$round" -gt 1 ] && [[ "$REPEAT_SHAPES" != *" $shape "* ]]; then continue; fi
    tag="${shape}.${build}.${entry}.r${round}"
    caseout="$LOGS/$tag.out"
    log="$LOGS/$tag.log"
    start=$(date +%s)
    ( cd "$BIN" && KRONIKOL_213_CASE="$shape" KRONIKOL_213_CASE_OUT="$caseout" KRONIKOL_213_CASE_ONLY="$build|$entry" \
        /usr/bin/timeout -s KILL 240 "$EXE" -method "$METHOD" > "$log" 2>&1 )
    rc=$?
    secs=$(( $(date +%s) - start ))
    result=$(grep -m1 -E "^RESULT " "$caseout" 2>/dev/null | sed -E 's/^RESULT [^ ]+ :: //' | tr -d '\r')
    if [ -n "$result" ] && grep -qE "^DONE " "$caseout" 2>/dev/null; then
      outcome="$result"
      [ "$rc" -ne 0 ] && outcome="$outcome (process rc=$rc)"
    elif [ "$rc" -eq 124 ] || [ "$rc" -eq 137 ]; then
      outcome="HOST-HANG killed after ${secs}s (rc=$rc)"
    elif grep -qE "^Fatal error\.|AccessViolationException" "$log"; then
      exc=$(grep -m1 -oE "System\.[A-Za-z.]+Exception" "$log")
      frame=$(grep -m1 -E "^ +at " "$log" | sed -E 's/^ +at //' | tr -d '\r')
      where=$(grep -E "^ +at " "$log" | grep -m1 -vE "CastHelpers|System\.Runtime|System\.Threading" | sed -E 's/^ +at //' | tr -d '\r')
      outcome="HOST-CRASH ${exc:-?} at ${frame:-?} in ${where:-?} (rc=$rc, ${secs}s)"
    else
      outcome="FACT-FAILED rc=$rc after ${secs}s (see case-logs/$tag.log)"
    fi
    echo "R${round} ${shape}|${build}|${entry} :: ${outcome}" >> "$OUT"
  done < "$LIST"
done
echo "ORCHESTRATION COMPLETE" >> "$OUT"
