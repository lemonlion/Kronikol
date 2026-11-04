#!/usr/bin/env bash
# Usage: run-readers.sh <version>. Runs rev5-readers, keeps its Reports, then runs read-only query verbs over them.
HERE="$(cd "$(dirname "$0")" && pwd)"
V="$1"
OUT="$HERE/../out/$V"; mkdir -p "$OUT"
cd "$HERE/rev5-readers" && rm -rf bin obj
PHASE_PROBE_OUT="$OUT" dotnet test -p:KronikolVersion="$V" --nologo -v q 2>&1 | grep -E "^(Passed|Failed)!| error " | cut -c1-160 | head -5
R="$OUT/reports-rev5-readers"; rm -rf "$R"; mkdir -p "$R"
cp -r bin/Debug/net10.0/Reports/. "$R/" 2>/dev/null
cd "$HERE/rev5-readers" && rm -rf bin obj
Q="$OUT/query-rev5-readers"; rm -rf "$Q"; mkdir -p "$Q"
ls "$R" | head -30 > "$Q/files.txt"
i=0
for v in "summary" "scenarios" "services" "parties" "sizes" "failures" "repro" "assertions" "interactions --group-by phase" "interactions --group-by step,phase,method,path" "interactions --phase setup --group" "interactions --phase action --group" "steps s0" "steps s2" "flow s0" "flow s2" "diagram s0" "diagram s2" "diagram s3" "diagram s4"; do
  i=$((i+1))
  name="$(printf '%02d' $i)-$(echo "$v" | tr ' /,-' '____')"
  (cd "$Q" && timeout 180 dotnet run --file "$R/query.cs" -- ${v%% *} "$R" $( [ "$v" != "${v#* }" ] && echo "${v#* }" ) --max-bytes 0 > "$Q/$name.txt" 2>&1)
done
echo READERS_DONE "$V"
