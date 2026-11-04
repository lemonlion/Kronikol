#!/usr/bin/env bash
# steps.sh <tool> <reports-dir> [grep-pattern] : `query steps` for each scenario whose line matches
tool=$1; out=$2; pat=${3:-.}
"$tool" query scenarios "$out" --limit 500 --max-bytes 2000000 2>&1 | grep -E '^s[0-9]+' | grep -E "$pat" | cut -d' ' -f1 | while read -r address; do
  echo "--- $address"
  "$tool" query steps "$out" "$address" --max-bytes 2000000 2>&1 | grep -v -E "^(stableId|open:|\s*$)" | cut -c1-150
done
