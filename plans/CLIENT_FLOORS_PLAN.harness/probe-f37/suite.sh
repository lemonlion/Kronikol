#!/bin/bash
# F37: the Playwright suite on Linux, as CI's floor run takes it, in the .NET 10 SDK container (run from the repo root
# by the session's script): the newest build installs Chromium's system packages (install-deps), as CI's step does; then
# for each release given, Kronikol.Playwright pinned there, the suite built from an empty bin/ and obj/ with
# -p:FloorRun=true, the release's own Chromium installed, and the suite run.
#   bash suite.sh <release>...
set -u
P=tests/Kronikol.Tests.Playwright
S=src/Kronikol.Playwright/Kronikol.Playwright.csproj
dotnet build $P -c Release > /tmp/build-newest.log 2>&1 || { echo "newest | BUILD FAILED"; tail -5 /tmp/build-newest.log; exit 1; }
pwsh $P/bin/Release/net10.0/playwright.ps1 install-deps chromium > /tmp/deps.log 2>&1
echo "newest | install-deps exit $?"
for v in "$@"; do
  sed -i "s#Include=\"Microsoft.Playwright\" Version=\"[0-9.]*\"#Include=\"Microsoft.Playwright\" Version=\"$v\"#" $S
  rm -rf $P/bin $P/obj src/Kronikol.Playwright/bin src/Kronikol.Playwright/obj
  if ! dotnet build $P -c Release -p:FloorRun=true > /tmp/build-$v.log 2>&1; then
    echo "$v | BUILD FAILED | $(grep -m1 ' error ' /tmp/build-$v.log)"
    continue
  fi
  pwsh $P/bin/Release/net10.0/playwright.ps1 install chromium > /tmp/install-$v.log 2>&1
  echo "$v | install exit $? | $(grep -ao '"Microsoft.Playwright/[0-9.]*' $P/obj/project.assets.json | sort -u | tr -d '"')"
  dotnet test $P -c Release --no-build -p:FloorRun=true -- RunConfiguration.TreatNoTestsAsError=true > /tmp/test-$v.log 2>&1
  echo "$v | test exit $? | $(grep -E 'Passed!|Failed!' /tmp/test-$v.log | head -1)"
  grep -E "Win32Exception|\[FAIL\]" /tmp/test-$v.log | head -2 | cut -c1-240
done
