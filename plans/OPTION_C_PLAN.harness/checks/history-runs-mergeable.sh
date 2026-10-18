#!/usr/bin/env bash
# Five runs of the BreakfastProvider ReqNRoll suite in the throwaway worktree, labelled as BreakfastProvider's
# real CI runs #318-#322, so the report's History tab has real verdicts: one flaky scenario (fails in #319
# only), one failing since #321, one new failure in #322. KRONIKOL_HISTORY names the ledger, since a CI run
# otherwise leaves the ledger to the workflow.
set -euo pipefail
WT=C:/Code/BreakfastProvider-cmock
T=$WT/tests/BreakfastProvider.Tests.Component.ReqNRoll
S=C:/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/4446b339-7f87-4b87-990f-495a136f5c68/scratchpad/cmock
STEPS=$T/StepDefinitions/Orders/OrderRetrievalSteps.cs
PANCAKES=$T/Features/Pancakes/PancakesCreation.feature
ORDERS=$T/Features/Orders/OrderValidation.feature
HIST=$WT/tests/BreakfastProvider.Tests.Component.Shared/KronikolHistory.cs

# v5 parity (OPTION_C_PLAN S0): the same five runs, with mergeable data so 'kronikol merge' can draw the last one again
grep -q GenerateMergeableData "$HIST" || sed -i 's/        options.HistoryMinRuns = 3;/        options.HistoryMinRuns = 3;\n        options.GenerateMergeableData = true;/' "$HIST"
grep -c GenerateMergeableData "$HIST"

# History and diagnostics sections drawn in the report itself (the opt-ins since 4.6.0)
grep -q ShowHistorySection "$HIST" || sed -i 's/        options.HistoryMinRuns = 3;/        options.HistoryMinRuns = 3;\n        options.ShowHistorySection = true;\n        options.ShowReportDiagnosticsSection = true;/' "$HIST"
grep -c "ShowHistorySection\|ShowReportDiagnosticsSection" "$HIST"

e1() { if [ "$1" = on ]; then sed -i 's/StatusCode.Should().Be(HttpStatusCode.NotFound);/StatusCode.Should().Be(HttpStatusCode.Gone);/' "$STEPS";
       else sed -i 's/StatusCode.Should().Be(HttpStatusCode.Gone);/StatusCode.Should().Be(HttpStatusCode.NotFound);/' "$STEPS"; fi; }
e2() { if [ "$1" = on ]; then sed -i "s/| 'Eggs' is required.                              |/| 'Eggs' must be provided.                         |/" "$PANCAKES";
       else sed -i "s/| 'Eggs' must be provided.                         |/| 'Eggs' is required.                              |/" "$PANCAKES"; fi; }
e3() { if [ "$1" = on ]; then sed -i 's/| Quantity must be greater than zero. | Bad Request     |/| Quantity must be positive.          | Bad Request     |/' "$ORDERS";
       else sed -i 's/| Quantity must be positive.          | Bad Request     |/| Quantity must be greater than zero. | Bad Request     |/' "$ORDERS"; fi; }

rm -f "$WT/.kronikol/history.jsonl"
run() {
  local num=$1 id=$2
  rm -rf "$T/bin/Debug/net10.0/Reports"
  echo "== run #$num (E1 $(grep -c 'HttpStatusCode.Gone' "$STEPS") E2 $(grep -c 'must be provided' "$PANCAKES") E3 $(grep -c 'must be positive' "$ORDERS"))"
  ( cd "$T" && GITHUB_ACTIONS=true GITHUB_SERVER_URL=https://github.com GITHUB_REPOSITORY=lemonlion/BreakfastProvider \
      GITHUB_RUN_ID=$id GITHUB_RUN_NUMBER=$num GITHUB_RUN_ATTEMPT=1 GITHUB_SHA=0ee43e8c0b5d3e85a4a099a20ee4ce37305b6b3f \
      GITHUB_REF_NAME=main GITHUB_WORKSPACE='C:\Code\BreakfastProvider-cmock' \
      GITHUB_STEP_SUMMARY='C:\Users\cex\AppData\Local\Temp\claude\c--Code-Kronikol\4446b339-7f87-4b87-990f-495a136f5c68\scratchpad\cmock\step-summary.md' \
      GITHUB_OUTPUT='C:\Users\cex\AppData\Local\Temp\claude\c--Code-Kronikol\4446b339-7f87-4b87-990f-495a136f5c68\scratchpad\cmock\gh-output.txt' \
      KRONIKOL_SUITE=reqnroll-in-memory KRONIKOL_HISTORY='C:\Code\BreakfastProvider-cmock\.kronikol\history.jsonl' \
      timeout 1200 dotnet test 2>&1 | grep -E "total:|failed:|succeeded:|error" | grep -v warning || true )
}
e1 off; e2 off; e3 off; run 318 38233302187
e3 on;                  run 319 38379331608
e3 off;                 run 320 38533139789
e1 on;                  run 321 38701369989
e2 on;                  run 322 38868550876
wc -l "$WT/.kronikol/history.jsonl"
R=$T/bin/Debug/net10.0/Reports
P=C:/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/4446b339-7f87-4b87-990f-495a136f5c68/scratchpad/v5parity
cp "$R/TestRunReport.html" "$P/bp3-TestRunReport.html"
cp "$R/TestRunReport.json" "$P/bp3-TestRunReport.json"
cp "$WT/.kronikol/history.jsonl" "$P/bp3-history.jsonl"
ls -la "$R" > "$P/bp3-reports-listing.txt"
head -c 900 "$R/Failures.md" | tail -c 400
echo DONE
