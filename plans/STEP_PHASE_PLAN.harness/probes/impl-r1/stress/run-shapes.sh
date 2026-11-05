#!/usr/bin/env bash
# Builds the stress probe once per version, then runs each shape in a process of its own, as a crash takes the whole process.
# Usage: run-shapes.sh <version> <weaver version> "<N list>" "<shapes>"
V="$1"; W="$2"; NS="$3"; SHAPES="$4"
OUT=/c/Code/k224-review1/out; T="$OUT/stress-times-$V.txt"
cd /c/Code/k224-review1/probes/stress || exit 1
rm -rf bin obj
dotnet build -p:KronikolVersion="$V" -p:WeaverVersion="$W" --nologo -v q -m:2 2>&1 | grep -E ' error |Elapsed' | cut -c1-200
python -I ../assets.py obj/project.assets.json
for N in $NS; do
  for S in $SHAPES; do
    before=$(wc -l < "$T" 2>/dev/null || echo 0)
    log="$OUT/stress-$V-$S-$N.log"
    start=$(date +%s)
    PROBE_N=$N PROBE_TIME="$(cygpath -w "$T")" timeout 280 dotnet test --no-build -p:KronikolVersion="$V" -p:WeaverVersion="$W" --nologo -v q --filter "DisplayName~$S" > "$log" 2>&1
    code=$?
    end=$(date +%s)
    crash=$(grep -c -E 'Catastrophic|crashed|Stack overflow' "$log")
    so=$(grep -m1 -oE 'exit code -?[0-9]+' "$log")
    echo "$V N=$N $S: exit=$code crash-lines=$crash $so wall=$((end-start))s $(tail -n +$((before+1)) "$T" 2>/dev/null | tr '\n' ' ')"
  done
done
rm -rf bin obj
