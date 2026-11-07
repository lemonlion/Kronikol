#!/usr/bin/env bash
# Runs one built test project three times whole, then three times per test class alone, and writes what each run
# printed, its exit code and the assertion notes its report holds (read with the report's own query.cs; the
# report files themselves are never opened).
#
#   [RUN_ALL=0] ./run.sh <label> <project dir, relative to this folder> <Debug|Release> [class ...]
#
# Writes results/<label>-<config>-<all|class>.txt, one file per set of three runs. Build the project first, at the
# same configuration: every run here is `dotnet test --no-build`.
set -u
H="$(cd "$(dirname "$0")" && pwd)"
label=$1; proj="$H/$2"; cfg=$3; shift 3
R="$proj/bin/$cfg/net10.0/Reports"

# The history ledger would otherwise go to the nearest .kronikol or .git above the output: the repository's root.
export KRONIKOL_HISTORY=off

query() { (cd "$R" && dotnet run --file "$R/query.cs" -- "$@" 2>&1); }

notes() {
  local f=$1
  if [ ! -f "$R/Run.json" ]; then echo "report: none written" >> "$f"; return; fi
  echo "report: written" >> "$f"
  echo "--- scenarios" >> "$f"
  query scenarios "$R" >> "$f"
  local s
  for s in $(query scenarios "$R" | grep -o -E '^s[0-9]+'); do
    echo "--- $s" >> "$f"
    query steps "$R" "$s" | head -1 >> "$f"
    local listing; listing=$(query note "$R" "$s/d0")
    echo "$listing" >> "$f"
    local n
    for n in $(echo "$listing" | grep -o -E '^n[0-9]+'); do
      echo "--- $s/d0/$n in full" >> "$f"
      query note "$R" "$s/d0/$n" | grep -v '^this is the rendered note' >> "$f"
    done
  done
}

run_set() {
  local name=$1 filter=$2 f="$H/results/$label-$cfg-$1.txt" i
  : > "$f"
  for i in 1 2 3; do
    rm -rf "$R"
    {
      echo "=================== run $i of 3: $label $cfg $name ($(date -u +%Y-%m-%dT%H:%M:%SZ))"
      echo "dotnet test -c $cfg --no-build --logger console;verbosity=normal${filter:+ --filter $filter}"
    } >> "$f"
    if [ -n "$filter" ]; then
      (cd "$proj" && timeout 300 dotnet test -c "$cfg" --no-build --logger "console;verbosity=normal" --filter "$filter") >> "$f" 2>&1
    else
      (cd "$proj" && timeout 300 dotnet test -c "$cfg" --no-build --logger "console;verbosity=normal") >> "$f" 2>&1
    fi
    echo "exit code: $?" >> "$f"
    notes "$f"
  done
  echo "$f"
}

# RUN_ALL=0 leaves out the three whole-assembly runs (the value probes run only the classes that can reach the probe).
[ "${RUN_ALL:-1}" = 1 ] && run_set all ""
for c in "$@"; do
  run_set "$c" "FullyQualifiedName~WeaveRepro.$c."
done
