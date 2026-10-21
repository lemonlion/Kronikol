#!/usr/bin/env bash
# P4: what `kronikol merge` keeps of a scenario that is not a test (plan F7). Runs p4-merge.cs (an ingest of P1's
# browser run with a fold, GenerateMergeableData on, its own ledger written), then merges that shard with the published
# Kronikol.Tool 5.2.3 into a second ledger, and prints both ledgers' run lines and the merged report's summary.
#   p4-merge.sh <folder holding p4-merge.cs> <P1's output folder> <empty folder>
set -euo pipefail
probes="$1"; p1="$2"; out="$3"
mkdir -p "$out"
(cd "$probes" && dotnet run --file p4-merge.cs -- "$p1" "$out")
shard="$out/shard"
data="$shard/TestRunReport.krkl"
# merge reads a ledger and never writes one (MergeCommand.cs:194-197), so it is given a copy of the ingest's.
cp "$out/ingest-history.jsonl" "$out/merge-history.jsonl"
echo "merging $(basename "$data")"
(cd "$out" && dotnet tool execute Kronikol.Tool@5.2.3 merge "$data" -o "$out/merged/TestRunReport.html" --history "$out/merge-history.jsonl" 2>&1 | tail -2)
cmp -s "$out/ingest-history.jsonl" "$out/merge-history.jsonl" && echo "the merge left the ledger unchanged"
for f in shard merged; do
  echo "$f: the fold scenario's history row:"
  grep -o -E 'Traffic outside any test.{0,300}' "$out/$f/TestRunReport.labs.html" | grep -o -E 'aria-label="[^"]*"' | head -1
done
