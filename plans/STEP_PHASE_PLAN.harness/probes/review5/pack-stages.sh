#!/usr/bin/env bash
S=/c/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/31780689-26c4-4084-b1b5-c4efb9a6fd42/scratchpad
WT="$S/review5/wt"; P="$S/review5/probes"
for pair in r1:5.28.3-rev5r1 r1-r2:5.28.3-rev5r2; do
  p=${pair%%:*}; v=${pair##*:}
  git -C "$WT" checkout -q -- . 
  git -C "$WT" apply --whitespace=nowarn "$S/rr/prototype/cut9/$p.patch" || echo APPLY_FAILED $p
  bash "$P/pack.sh" "$WT" "$v"
done
git -C "$WT" checkout -q -- .
git -C "$WT" apply --whitespace=nowarn "$S/rr/prototype/cut9/r1-r2-r3.patch" || echo APPLY_FAILED r3
echo STAGES_DONE
