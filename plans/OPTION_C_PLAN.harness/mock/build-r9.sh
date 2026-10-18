#!/bin/sh
# Round 9: option-c-r9 = round 8 + the rows under the search field folded until More filters opens them, and a
# tooltip on every control.
# c-dom.js = c-dom.r5.js + c-round6.js + c-diagram-look.js + c-round8.js + c-round9.js + c-round9-tips.js.
set -e
cd "$(dirname "$0")"
cat c-dom.r5.js c-round6.js c-diagram-look.js c-round8.js c-round9.js c-round9-tips.js c-round13.js c-round16.js c-round17.js > c-dom.js
python -I compose.py | tail -1
ENGINE=../engines/packed-wasm-0.16.0.js ENGINE_LABEL="PlantUML master (1.2026.9beta4) compiled to WebAssembly (Wasm GC) with TeaVM 0.16.0" node build.js bp2-TestRunReport.html option-c-r9.html | grep -v "^engine" | tail -7
