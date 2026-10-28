#!/bin/sh
# The revised card (revision-card-revised.cs) on audit B's ten same-commit lane pairs: eight CI lanes of runs 40290999167 (A) and
# 40473765203 (B), both cf47045 on Kronikol 5.4.1, and two lanes run twice locally (L1, L2: 68a8760, Kronikol 5.12.1);
# then runs B and 40491266394 (C: 68a8760, 5.12.1) on all 18 lanes. Prints, per pair, whether behaviour is byte-identical,
# and without its scenario index (revision-pairs_revised.py). Usage: sh revision-pairs-revised.sh <audit/B dir> <scratchpad dir> <probe dir> <out dir> <script dir>
B=$1; S=$2; P=$3; O=$4; X=$5
mkdir -p "$O"; cd "$P" || exit 1
input() { case "$1" in A-nunit-in-memory|A-tunit-in-memory|A-lightbdd-in-memory|A-bddfy-in-memory) echo "$S/decoded/${1#A-}.json";;
  A-*) echo "$B/decoded/40290999167/${1#A-}.json";; B-*) echo "$B/decoded/40473765203/${1#B-}.json";; C-*) echo "$B/decoded/40491266394/${1#C-}.json";;
  L1-*) echo "$B/decoded/local/${1#L1-}-run1.json";; L2-*) echo "$B/decoded/local/${1#L2-}-run2.json";; esac; }
card() { [ -s "$O/$1.service.json" ] || dotnet run --no-build --file revision-card-revised.cs -- "$(input $1)" "$B/out/parties/$1.json" - - "$O/$1" > "$O/$1.txt" 2>&1; }
for t in A:B:xunit-in-memory A:B:reqnroll-in-memory A:B:reqnroll-in-docker A:B:xunit-external-sut A:B:nunit-in-memory A:B:tunit-in-memory A:B:lightbdd-in-memory A:B:bddfy-in-memory L1:L2:xunit-in-memory L1:L2:reqnroll-in-memory; do
  x=${t%%:*}; rest=${t#*:}; y=${rest%%:*}; l=${rest#*:}
  card "$x-$l"; card "$y-$l"
  python -I -X utf8 "$X/revision-pairs_revised.py" "$O/$x-$l.service.json" "$O/$y-$l.service.json" "same-commit $x-vs-$y $l"
done
for f in "$B/decoded/40491266394"/*.json; do
  l=$(basename "$f" .json); card "B-$l"; card "C-$l"
  python -I -X utf8 "$X/revision-pairs_revised.py" "$O/B-$l.service.json" "$O/C-$l.service.json" "upgrade B-vs-C $l"
done
# One commit on two platforms: run C (Linux CI) against the two local runs (Windows), xunit-in-memory.
card "C-xunit-in-memory"; card "L1-xunit-in-memory"; card "L2-xunit-in-memory"
for r in L1 L2; do
  python -I -X utf8 "$X/revision-pairs_revised.py" "$O/C-xunit-in-memory.service.json" "$O/$r-xunit-in-memory.service.json" "platform C-vs-$r xunit-in-memory"
done
