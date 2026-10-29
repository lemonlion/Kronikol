#!/usr/bin/env bash
# Kept in the harness as plans/CLIENT_FLOORS_PLAN.harness/probe-r3a/f36/playwright-obj.sh (F36); run from the
# session scratchpad as pwfloor-probe.sh.
# Why the Playwright floor's output lacks Microsoft.Playwright.dll and playwright.ps1 in C:/Code/Kronikol-r3a: after
# each step, whether the two are there. No browser install, no test that drives a browser (the test step's own build).
set -u
cd /c/Code/Kronikol-r3a || exit 1
SP=/c/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/63719c49-3b93-4f9a-8c3a-1980cb488a9f/scratchpad
OUT=$SP/pwfloor-probe.txt; L=$SP/pwfloor-logs; mkdir -p "$L"; : > "$OUT"
P=tests/Kronikol.Tests.Playwright; O=$P/bin/Release/net10.0
has() { echo "$1: dll $( [ -f $O/Microsoft.Playwright.dll ] && echo yes || echo NO ), ps1 $( [ -f $O/playwright.ps1 ] && echo yes || echo NO )" >> "$OUT"; }
rm -rf $P/bin
dotnet build $P -c Release -p:FloorRun=true > $L/1-build.log 2>&1; has "1 floor build from empty bin (obj kept), exit $?"
dotnet build $P -c Release -p:FloorRun=true > $L/2-build.log 2>&1; has "2 floor build again, exit $?"
rm -rf $P/bin $P/obj
dotnet build $P -c Release -p:FloorRun=true > $L/3-build.log 2>&1; has "3 floor build from empty bin and obj, exit $?"
dotnet build $P -c Release > $L/4-build.log 2>&1; has "4 newest build over it, exit $?"
rm -rf $P/bin
dotnet build $P -c Release -p:FloorRun=true > $L/5-build.log 2>&1; has "5 floor build from empty bin after a newest build, exit $?"
grep -ao '"Microsoft.Playwright/[0-9.]*' $P/obj/project.assets.json | sort -u >> "$OUT"
echo DONE >> "$OUT"
