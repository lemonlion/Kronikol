#!/usr/bin/env bash
# After the cut 10 chain: the repeats that need cut 10, then cut 11's baseline and its delta. Every step stops by itself under 3 GB.
cd "$(dirname "$0")"
PY=/c/Python314/python.exe
MUT="$(cd .. && pwd)"
PATCH="$MUT/../rr/prototype/cut11/r1-r2-r3.patch"

echo "=== repeats on cut 10"
$PY mutrun.py rerun nm31b rev5/rev5-bddfy 3
$PY mutrun.py rerun nm32 rev4/rev4-b 3

echo "=== to cut 11"
git -C "$MUT/wt" checkout -- . && git -C "$MUT/wt" apply "$PATCH" || { echo "CUT11_APPLY_FAILED"; exit 1; }
$PY switch_check.py || { echo "CUT11_SNAPSHOT_DIFFERS"; exit 1; }

export MUT_CUT=11
$PY mutrun.py control-pack
$PY mutrun.py control-fast 1 3 main rev5 rev6 review/rev3-plain main/mut-extra
$PY mutrun.py clean-control

echo "=== p"
$PY mutrun.py run p01 p02 p03 p04 p05 p06 p07 p08 p09 p10 p11
echo "RUN12_EXIT $?"
