#!/bin/bash
# S7 for the warm-up audit (plans/WARM_UP_PLAN.md 6.6): BreakfastProvider (scratch clone bp-s7, origin/main 0ee43e8) on the
# local 4.16.3 packages, xUnit twice and ReqNRoll three times, then the release's marks against warmup.py's and the
# pages against the data. Leaves the clone's pin move and nuget.config in place until restored by hand.
S=C:/Users/cex/AppData/Local/Temp/claude/c--Code-Kronikol/4da7d127-ac79-4498-84d8-6f4c5a25b755/scratchpad
H=C:/Code/Kronikol-rel113/plans/WARM_UP_PLAN.harness/breakfastprovider
OUT=$S/bp-4.16.3
rm -rf "$OUT"
rm -rf C:/Users/cex/.nuget/packages/kronikol*/4.16.3
echo "cached 4.16.3 folders left: $(ls -d C:/Users/cex/.nuget/packages/kronikol*/4.16.3 2>/dev/null | wc -l)"
cd "$S/bp-s7" || exit 1
[ -f nuget.config ] && { echo "nuget.config present, stopping"; exit 1; }
git checkout -q 0ee43e8 || exit 1
PYTHONUTF8=1 python "$H/bp_pin.py" "$S/bp-s7" 4.9.1 4.16.3 --feed "$S/feed-4.16.3"
BP="$S/bp-s7" bash "$H/runlanes.sh" "$OUT" xUnit:2 ReqNRoll:3
echo "restored from: $(python -c "import json; print(json.load(open('C:/Users/cex/.nuget/packages/kronikol/4.16.3/.nupkg.metadata'))['source'])")"
for f in "$OUT"/*.test.txt; do echo "$(basename "$f"): $(grep -E '^\s*(total|failed|succeeded|skipped):' "$f" | tr -s ' ' | tr '\n' ' ')"; done
PYTHONUTF8=1 python "$H/compare_marks.py" "$OUT"
for lane in "$OUT"/xUnit-* "$OUT"/ReqNRoll-*; do [ -d "$lane" ] && { echo "== html $(basename "$lane")"; PYTHONUTF8=1 python "$H/html_marks.py" "$lane"; }; done
