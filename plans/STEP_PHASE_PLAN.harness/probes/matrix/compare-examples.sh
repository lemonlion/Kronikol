#!/usr/bin/env bash
# Usage: compare-examples.sh <before label> <after label>
# For each of the repo's component suites run by run-examples.sh: which phases moved, which diagrams' PlantUML changed
# (run ids, GUIDs and durations are masked, as they differ every run), and whose fingerprints moved.
set -u
HERE="$(cd "$(dirname "$0")" && pwd)"
BEFORE="$HERE/../results/examples/$1"
AFTER="$HERE/../results/examples/$2"
mask() { sed -E 's/-[0-9a-f]{16}-0[01]/-{span}-00/g; s/Date=[^]]+/Date={date}/g; s/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/{guid}/g; s/[0-9a-f]{32}/{hex}/g; s/[0-9]+(\.[0-9]+)? ?ms/{ms}/g; s/[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9:.]+Z?/{time}/g' "$1" | tr -d '\r'; }
for suite in ReqNRoll.xUnit2 ReqNRoll.xUnit3 LightBDD.xUnit2 LightBDD.xUnit3 BDDfy.xUnit3 xUnit3 NUnit4; do
    echo "== $suite"
    # The timings columns of the phase table differ every run: compare phase, caller, service and calls.
    diff <(awk 'NR>1 && NF>=4 {print $1, $2, $3, $4}' "$BEFORE/$suite/phases.txt" | sed -E 's/ [0-9]+$//' | sort) \
         <(awk 'NR>1 && NF>=4 {print $1, $2, $3, $4}' "$AFTER/$suite/phases.txt" | sed -E 's/ [0-9]+$//' | sort) > /dev/null \
        && echo "phase buckets: same" || { echo "phase buckets:"; sed 's/^/  before: /' "$BEFORE/$suite/phases.txt" | cut -c1-70; sed 's/^/  after:  /' "$AFTER/$suite/phases.txt" | cut -c1-70; }
    same=0; changed=0
    for f in "$BEFORE/$suite/diagrams/"*.puml; do
        name="$(basename "$f")"
        if diff <(mask "$f") <(mask "$AFTER/$suite/diagrams/$name") > /dev/null 2>&1; then same=$((same+1)); else changed=$((changed+1)); echo "  diagram changed: $name"; diff <(mask "$f") <(mask "$AFTER/$suite/diagrams/$name") | grep '^[<>]' | cut -c1-110 | head -8; fi
    done
    echo "diagrams: $same same, $changed changed"
    /c/Python314/python.exe -I "$HERE/compare_fingerprints.py" "$BEFORE/$suite/History.run.json" "$AFTER/$suite/History.run.json" | tail -1
done
