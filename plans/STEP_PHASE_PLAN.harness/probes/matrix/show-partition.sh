#!/usr/bin/env bash
# Usage: show-partition.sh <reports dir> <scenario name fragment> [...]
# What the drawn diagram says: each scenario's PlantUML, cut to one line of where the Setup partition opens and closes and the request arrows between.
# Only those lines are read, never the file whole: S( opens the partition, )S closes it, each label a GET to the service under test.
set -u
REPORTS="$1"
shift
cd "$REPORTS" || exit 2
OUT="$(mktemp -d)"
for fragment in "$@"; do
    line="$(dotnet run --file query.cs -- scenarios . --grep "$fragment" --max-bytes 800 2>/dev/null | grep -E '^s[0-9]+ ' | head -1)"
    id="${line%% *}"
    echo "== $line"
    [ -n "$id" ] || continue
    dotnet run --file query.cs -- diagram . "$id" --out "$OUT" > /dev/null 2>&1
    for f in "$OUT/$id"-d*.puml; do
        [ -f "$f" ] || { echo "  (no diagram)"; continue; }
        tr -d '\r' < "$f" | awk '
            /^partition / { printf "S( "; next }
            /^end$/ { printf ")S "; next }
            /GET: \// { s=$0; sub(/.*GET: \//,"",s); sub(/\]\].*/,"",s); printf "%s ", s; next }
        ' ; echo
    done
done
rm -rf "$OUT"
