#!/bin/sh
# R3a's Playwright search (plans/CLIENT_FLOORS_PLAN.md 5.2): Microsoft.Playwright under Kronikol.Playwright, whose suite
# launches the bundled Chromium of the release built (never an installed Chrome or Edge), so each probe first installs
# that release's Chromium into a folder of its own (PLAYWRIGHT_BROWSERS_PATH, not the shared ms-playwright cache),
# emptied before each install and removed at the end.
#   sh r3a-playwright.sh <worktree> <logs> <browsers folder>
W=$1; LOGS=$2; B=$3; F=$W/plans/CLIENT_FLOORS_PLAN.harness/floor
python -I "$F/floor_probe2.py" --worktree "$W" --project src/Kronikol.Playwright/Kronikol.Playwright.csproj \
  --package Microsoft.Playwright --tests tests/Kronikol.Tests.Playwright --env "PLAYWRIGHT_BROWSERS_PATH=$B" \
  --setup "rm -rf '$B' && pwsh -NoProfile -File {test}/bin/Debug/net10.0/playwright.ps1 install chromium" \
  --out "$F/r3a-playwright.txt" --logs "$LOGS/Playwright" --search
rm -rf "$B"
echo ALLDONE > "$F/r3a-playwright.done"
