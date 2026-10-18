#!/bin/sh
# Round 17 regression: every check from earlier rounds against option-c-r9.local.html; one log per check.
cd "$(dirname "$0")/.."
P=option-c-r9.local.html
O=reg17
node diagram-check.js $P > $O/diagram.txt 2>&1
node note-hover-check.js $P > $O/note-hover.txt 2>&1
ASSERTIONS=1 node svg-contrast.js $P $O/svg-contrast.json > $O/svg-contrast.txt 2>&1
node func.js $P > $O/func.txt 2>&1
node q-test.js $P > $O/q-test.txt 2>&1
node toggles-test.js $P $O/toggles > $O/toggles.txt 2>&1
node r5-check.js $P $O/r5 > $O/r5.txt 2>&1
node r6-check.js $P chromium > $O/r6c.txt 2>&1
node r6-check.js $P firefox > $O/r6f.txt 2>&1
node tabs-check.js $P $O/tabs > $O/tabs.txt 2>&1
node nav-check.js $P $O/nav-c chrome > $O/nav-c.txt 2>&1
node nav-check.js $P $O/nav-f firefox > $O/nav-f.txt 2>&1
node obscured-check.js $P chrome > $O/obscured-c.txt 2>&1
node obscured-check.js $P firefox > $O/obscured-f.txt 2>&1
echo ALLDONE > $O/done.txt
