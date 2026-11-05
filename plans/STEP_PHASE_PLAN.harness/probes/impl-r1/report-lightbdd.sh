#!/usr/bin/env bash
# Writes a real report from the LightBDD probe on a version, keeps its folder under out/, and asks it the questions through query.cs.
# Usage: report-lightbdd.sh <version> <weaver version> <label>
V="$1"; W="$2"; L="$3"
cd /c/Code/k224-review1 || exit 1
KEEP=1 bash probes/run.sh lightbdd "$V" "$W" "$L"
R=/c/Code/k224-review1/out/$L/reports-lightbdd
rm -rf "$R"; mkdir -p "$R"
src=$(find probes/lightbdd/bin -name query.cs 2>/dev/null | head -1)
if [ -z "$src" ]; then echo "no query.cs under bin"; else cp -r "$(dirname "$src")/." "$R/"; fi
rm -rf probes/lightbdd/bin probes/lightbdd/obj
Q=/c/Code/k224-review1/out/$L/query-lightbdd; rm -rf "$Q"; mkdir -p "$Q"
q() { name="$1"; shift; timeout 170 dotnet run --file "$R/query.cs" -- "$@" > "$Q/$name.txt" 2>&1; }
q summary summary "$R"
q scenarios scenarios "$R" --result Failed
q failures failures "$R"
for sid in $(grep -oE '\bs[0-9]+\b' "$Q/scenarios.txt" | sort -u); do
  q "steps-$sid" steps "$R" "$sid"
  q "attachments-$sid" attachments "$R" "$sid"
done
echo "$L report questions written"
