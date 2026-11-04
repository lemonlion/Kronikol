#!/usr/bin/env bash
# Usage: run-drawn.sh <results label> <Kronikol version>
# Runs the suites whose drawn diagrams and service cards the report quotes, and reads those from each suite's own report before the next build replaces it:
# results/<label>/partitions.txt (where the Setup partition opens and closes, against the calls drawn), results/<label>/server-card.txt (each call's effective phase on the service card),
# results/<label>/server-query.txt (the issue's table: kronikol query interactions --group-by step,phase,method,path).
set -u
LABEL="$1"
VERSION="$2"
HERE="$(cd "$(dirname "$0")" && pwd)"
OUT="$HERE/../results/$LABEL"
mkdir -p "$OUT"
PARTITIONS="$OUT/partitions.txt"
: > "$PARTITIONS"

reports() { echo "$HERE/$1/bin/Debug/net10.0/Reports"; }

bash "$HERE/run-suite.sh" nunit4 "$LABEL" "$VERSION"
{
    echo "#### nunit4"
    bash "$HERE/show-partition.sh" "$(reports nunit4)" "R01 handler" "R02 shared" "R06 handler" "R07 shared" "L01 set up async then a when" "L11 tear down async given then calls" "L11 tear down async given and when" "T01 a when" "T06 a given"
} >> "$PARTITIONS" 2>&1

bash "$HERE/run-suite.sh" tunit "$LABEL" "$VERSION"
{
    echo "#### tunit"
    bash "$HERE/show-partition.sh" "$(reports tunit)" "R01 handler" "L01 before test async then a when"
} >> "$PARTITIONS" 2>&1

bash "$HERE/run-suite.sh" bddfy-xunit3 "$LABEL" "$VERSION"
{
    echo "#### bddfy-xunit3"
    bash "$HERE/show-partition.sh" "$(reports bddfy-xunit3)" "B90" "B91"
} >> "$PARTITIONS" 2>&1

bash "$HERE/run-suite.sh" reqnroll-xunit2 "$LABEL" "$VERSION"
{
    echo "#### reqnroll-xunit2"
    bash "$HERE/show-partition.sh" "$(reports reqnroll-xunit2)" "N01 " "N02 " "N20 " "N30 " "Q01 " "Q02 "
} >> "$PARTITIONS" 2>&1

bash "$HERE/run-suite.sh" reqnroll-server "$LABEL" "$VERSION"
{
    echo "#### reqnroll-server"
    bash "$HERE/show-partition.sh" "$(reports reqnroll-server)" "S01 " "S02 " "S11 " "S21 " "S31 "
} >> "$PARTITIONS" 2>&1
python -I "$HERE/card.py" "$(reports reqnroll-server)" > "$OUT/server-card.txt" 2>&1
(
    cd "$(reports reqnroll-server)" || exit 0
    for fragment in "S01 " "S02 " "S11 " "S21 "; do
        line="$(dotnet run --file query.cs -- scenarios . --grep "$fragment" --max-bytes 800 2>/dev/null | grep -E '^s[0-9]+ ' | head -1)"
        id="${line%% *}"
        echo "== $line"
        [ -n "$id" ] && dotnet run --file query.cs -- interactions . "$id" --group-by step,phase,method,path --max-bytes 0 2>&1 | cut -c1-150
    done
) > "$OUT/server-query.txt" 2>&1
echo "done drawn $LABEL"
