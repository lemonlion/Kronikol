#!/usr/bin/env bash
# The gate phase (plans/HISTORY_ACTION_PLAN.md section 4.6). After the tests: each report (one per line, a file or a
# directory) gated with kronikol history gate against the ledger the read phase named in KRONIKOL_HISTORY, its
# reading written to the job summary under the report's path. Every report is gated before the phase fails: an exit
# 1 from any fails it, and any other exit is an error naming the report, since an unreadable report is never a pass.
#
# Without a ledger there is nothing to read the run against, so the phase fails only when the tests did (F11). On a
# pull request the tool reads against the branch the pull request targets, as the run itself did.
#
# Its limit is the library's (F10): a re-run of a failed job reads its own first attempt, which record already
# folded, so a failure that persists reads as already failing, and passes.
set -uo pipefail
. "$(dirname "$0")/lib.sh"
. "$(dirname "$0")/tool.sh"

ledger=${KRONIKOL_HISTORY:-}
if [ -z "$ledger" ] || [ "$ledger" = off ] || [ ! -f "$(kh_posix "$ledger")" ]; then
  kh_output result no-ledger
  if [ "${KRONIKOL_HISTORY_TEST_OUTCOME:-}" = failure ]; then
    kh_error "the tests failed, and there is no ledger yet to tell a new failure from an old one"
    exit 1
  fi
  kh_notice "there is no ledger yet to read this run against, so there is nothing to gate; the first record starts one"
  exit 0
fi

if ! kh_tool_prepare; then
  kh_output result failed
  exit 1
fi

gate() {
  set -- history gate "$1" --history "$ledger" --fail-on "${KRONIKOL_HISTORY_FAIL_ON:-new-failures}"
  if [ -n "${KRONIKOL_HISTORY_MIN_RUNS:-}" ]; then set -- "$@" --min-runs "$KRONIKOL_HISTORY_MIN_RUNS"; fi
  if [ -n "${KRONIKOL_HISTORY_MAX_NEW_FAILURES:-}" ]; then set -- "$@" --max-new-failures "$KRONIKOL_HISTORY_MAX_NEW_FAILURES"; fi
  if [ -n "${KRONIKOL_HISTORY_MIN_PASS_RATE:-}" ]; then set -- "$@" --min-pass-rate "$KRONIKOL_HISTORY_MIN_PASS_RATE"; fi
  kh_tool "$@"
}

result=passed
gated=0
out="$KH_TEMP/kronikol-history-gate.out"
reports=$(printf '%s\n' "${KRONIKOL_HISTORY_REPORTS:?KRONIKOL_HISTORY_REPORTS names the reports to gate}" | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//' | grep -v '^$' || true)
while IFS= read -r report; do
  [ -n "$report" ] || continue
  gated=$((gated + 1))
  status=0
  gate "$report" > "$out" 2>&1 || status=$?
  kh_verbatim "$out"
  {
    printf '### Kronikol history gate: `%s`\n\n````text\n' "$report"
    cat "$out"
    printf '````\n\n'
  } | kh_summary
  case $status in
    0) ;;
    1)
      result=failed
      kh_error "the history gate tripped on $report: $(grep -E '^(new-failures|flaky|duration-regression|behaviour-change): [1-9]' "$out" | tr '\n' ' ')"
      ;;
    *)
      result=failed
      kh_error "could not gate $report (kronikol history gate exit $status: $(kh_message "$out")); an unreadable report is not a pass"
      ;;
  esac
done <<EOF
$reports
EOF

if [ "$gated" -eq 0 ]; then
  kh_error "reports names no report to gate"
  result=failed
fi
kh_output result "$result"
[ "$result" = passed ]
