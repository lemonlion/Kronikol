#!/usr/bin/env bash
# Kept in the harness as plans/CLIENT_FLOORS_PLAN.harness/probe-r3a/f36/over-the-newest.sh (F36); run from the
# session scratchpad as f36-check.sh.
# F36, red then green, in C:/Code/Kronikol-r3a2 (the adapters' worktree, the same MSTest and Playwright suites as the
# release's): each suite's normal run, then its floor run built over that output, as CI's floor loop did, then the floor
# run from an empty bin/. Playwright's browsers go in a folder of this session's own.
set -u
cd /c/Code/Kronikol-r3a2 || exit 1
SP=/c/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/63719c49-3b93-4f9a-8c3a-1980cb488a9f/scratchpad
OUT=$SP/f36-check.txt; LOGS=$SP/f36-check; B=$SP/pw-f36
mkdir -p "$LOGS"; : > "$OUT"
sum() { grep -aE "Passed!|Failed!|No test is available" "$1" | head -2 | tr '\n' ' ' | cut -c1-200; }

M=tests/Kronikol.Tests.MSTest
dotnet test $M -c Release -nologo -- RunConfiguration.TreatNoTestsAsError=true > $LOGS/mstest.log 2>&1
echo "MSTest newest: exit $? $(sum $LOGS/mstest.log)" >> "$OUT"
dotnet test $M -c Release -nologo -p:FloorRun=true -- RunConfiguration.TreatNoTestsAsError=true > $LOGS/mstest-floor-over.log 2>&1
echo "MSTest floor over the newest's output: exit $? $(sum $LOGS/mstest-floor-over.log)" >> "$OUT"
rm -rf $M/bin
dotnet test $M -c Release -nologo -p:FloorRun=true -- RunConfiguration.TreatNoTestsAsError=true > $LOGS/mstest-floor-empty.log 2>&1
echo "MSTest floor from an empty bin/: exit $? $(sum $LOGS/mstest-floor-empty.log)" >> "$OUT"

P=tests/Kronikol.Tests.Playwright; PS=$P/bin/Release/net10.0/playwright.ps1
dotnet build $P -c Release -nologo > $LOGS/pw-build.log 2>&1
echo "Playwright newest build: exit $?, playwright.ps1 $( [ -f $PS ] && echo present || echo MISSING)" >> "$OUT"
dotnet build $P -c Release -nologo -p:FloorRun=true > $LOGS/pw-floor-over-build.log 2>&1
echo "Playwright floor build over the newest's output: exit $?, playwright.ps1 $( [ -f $PS ] && echo present || echo MISSING)" >> "$OUT"
rm -rf $P/bin
dotnet build $P -c Release -nologo -p:FloorRun=true > $LOGS/pw-floor-empty-build.log 2>&1
echo "Playwright floor build from an empty bin/: exit $?, playwright.ps1 $( [ -f $PS ] && echo present || echo MISSING)" >> "$OUT"
PLAYWRIGHT_BROWSERS_PATH=$B pwsh -NoProfile -File $PS install chromium > $LOGS/pw-install.log 2>&1
echo "  install exit $?" >> "$OUT"
PLAYWRIGHT_BROWSERS_PATH=$B dotnet test $P -c Release -nologo --no-build -p:FloorRun=true -- RunConfiguration.TreatNoTestsAsError=true > $LOGS/pw-floor-test.log 2>&1
echo "  floor tests: exit $? $(sum $LOGS/pw-floor-test.log)" >> "$OUT"
grep -ao '"Microsoft.Playwright/[0-9.]*' $P/obj/project.assets.json | sort -u | tr '\n' ' ' >> "$OUT"; echo >> "$OUT"
rm -rf "$B"
echo DONE >> "$OUT"
