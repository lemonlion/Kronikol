#!/bin/bash
# Runs the sweep's probes end to end and writes what each printed to ../results.
# args: <empty scratch folder> <published 5.27.13 kronikol> <prototype Kronikol.Tool.dll>
set -u
H=$(cd "$(dirname "$0")/.." && pwd)
P=$H/probes
R=$H/results
SW=$1
K=$2
PROTO=$3
export SW K
mkdir -p "$SW"

# Seven identical runs through the routes F2 did not list: each run's own view, then a merge on 5.27.13 and on the prototype.
{
  root=$SW/routes; rm -rf "$root"; mkdir -p "$root"; ledger=$root/history.jsonl
  "$K" history init --history "$ledger" >/dev/null 2>&1
  for i in 1 2 3 4 5 6 7; do
    cp "$ledger" "$root/read$i.jsonl"
    KRONIKOL_HISTORY="$root/read$i.jsonl" dotnet run --file "$P/history-routes.cs" -- "$root/run$i" >/dev/null 2>&1
    [ "$i" -lt 7 ] && "$K" history record "$root/run$i" --history "$ledger" >/dev/null 2>&1
  done
  echo "== run 7's own view, against the ledger (published 5.27.13)"
  "$K" history gate "$root/run7" --history "$ledger" 2>&1 | grep -E '^history:' | cut -c1-120
  echo
  echo "== run 7 through kronikol merge --history (published 5.27.13)"
  mkdir -p "$root/m"; "$K" merge "$root/run7" -o "$root/m/x.html" --history "$ledger" 2>&1 | grep -E 'history:' | cut -c1-120
  python -I "$P/history-routes-verdicts.py" "$root/m/x.labs.html" "$P/history-routes-names.txt"
  echo
  echo "== the same run and ledger through the prototype's merge"
  mkdir -p "$root/mp"; dotnet "$PROTO" merge "$root/run7" -o "$root/mp/x.html" --history "$ledger" 2>&1 | grep -E 'history:' | cut -c1-120
  python -I "$P/history-routes-verdicts.py" "$root/mp/x.labs.html" "$P/history-routes-names.txt"
} > "$R/history-routes.txt" 2>&1

# What a merge does to the service card, for a Storage Queues delete.
{
  root=$SW/card; rm -rf "$root"; mkdir -p "$root"
  dotnet run --file "$P/card-queue.cs" -- "$root/shard" >/dev/null 2>&1
  mkdir -p "$root/m" "$root/mp"
  "$K" merge "$root/shard" -o "$root/m/x.html" >/dev/null 2>&1
  dotnet "$PROTO" merge "$root/shard" -o "$root/mp/x.html" >/dev/null 2>&1
  echo "shard: the run; m: its 5.27.13 merge; mp: the prototype's merge"
  python -I "$P/card-queue-read.py" "$root/shard/service.json" "$root/m/service.json" "$root/mp/service.json"
} > "$R/card-queue.txt" 2>&1

# The NDJSON route of kronikol export, against the live route.
dotnet run --file "$P/otlp-verb.cs" > "$R/otlp-verb.txt" 2>&1

# The run id on CI, simulated by environment variables, not on real runners.
{
  mkdir -p "$SW/q2"
  for shape in "gh-broke gh fail pass" "ado-broke ado fail pass" "gitlab-broke gitlab fail pass" "local-broke local fail pass"; do
    echo "##### $shape"
    # shellcheck disable=SC2086
    bash "$P/ci-run-ids.sh" $shape 2>&1 | cut -c1-200
    echo
  done
  bash "$P/ci-run-ids-more.sh" 2>&1 | cut -c1-200
} > "$R/ci-run-ids.txt" 2>&1

# F1 on the first release that has merge --history, labels logged by hand.
{
  root=$SW/311; rm -rf "$root"; mkdir -p "$root"
  dotnet tool install Kronikol.Tool --version 3.11.0 --tool-path "$root/tool" >/dev/null 2>&1
  OLD=$root/tool/kronikol
  ledger=$root/history.jsonl
  "$OLD" history init --history "$ledger" >/dev/null 2>&1
  for i in 1 2 3 4 5 6 7; do
    dotnet run --file "$P/f1-at-3.11.0.cs" -- "$root/run$i" "$root/read$i.jsonl" "local-$i" >/dev/null 2>&1
    cp "$ledger" "$root/before$i.jsonl"
    [ "$i" -lt 7 ] && "$OLD" history record "$root/run$i" --history "$ledger" >/dev/null 2>&1
  done
  echo "== kronikol merge --history at 3.11.0, run 7 of 7 identical runs (a shard named as a file: a folder fails on History.run.json until 3.25.3)"
  shard=$(ls "$root/run7"/*.json | grep -vE 'History\.run|schema' | head -1)
  mkdir -p "$root/m"; "$OLD" merge "$shard" -o "$root/m/x.html" --history "$ledger" 2>&1 | grep -E 'history:|rror' | cut -c1-140
  echo
  echo "== kronikol merge --help at 3.10.0: lines naming --history"
  dotnet tool install Kronikol.Tool --version 3.10.0 --tool-path "$root/tool310" >/dev/null 2>&1
  "$root/tool310/kronikol" merge --help 2>&1 | grep -c -- '--history'
} > "$R/f1-at-3.11.0.txt" 2>&1
echo DONE
