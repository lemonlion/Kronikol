#!/bin/sh
# Bundled full Chromium 147 in new headless mode: is Chrome 154's lower edge the V8 version or the headless shell?
cd "$(dirname "$0")"
node measure.js stack '{"label":"F-stack","browser":"chromium-new"}'
node measure.js stack '{"label":"F-stack","browser":"chromium-new","jsflags":"--no-opt --no-maglev"}'
node measure.js bisect '{"label":"H-chromium-new-w4-jit","browser":"chromium-new","page":"w4","mode":"cold","shape":"real","lo":1500,"hi":1979,"reps":3}'
node measure.js bisect '{"label":"H-chromium-new-w4-off","browser":"chromium-new","page":"w4","mode":"cold","jsflags":"--no-opt --no-maglev","shape":"real","lo":300,"hi":1979,"reps":2}'
echo "chain3 finished"
