#!/usr/bin/env bash
# Runs a Kronikol tool's readers on base/ and new/ (make-report.py) and says whether each output differs in anything
# but the report's path and size. usage: run.sh <kronikol executable> <out dir from make-report.py> <tag>
set -u
tool=$1; dir=$2; tag=$3
for f in base new; do
  d="$dir/$f"
  for verb in "query summary" "query services --json" "query interactions s1 --json" "query flow s1" "query grep Cow"; do
    set -- $verb
    name=$(echo "$verb" | tr ' ' '_')
    if [ "$1" = query ]; then "$tool" query "$2" "$d" "${@:3}" --max-bytes 100000 > "$dir/$tag-$f-$name.txt" 2>&1; else :; fi
    echo "exit $?" >> "$dir/$tag-$f-$name.txt"
  done
  "$tool" ctrf "$d" -o "$dir/$tag-$f-ctrf.json" > "$dir/$tag-$f-ctrf.txt" 2>&1; echo "exit $?" >> "$dir/$tag-$f-ctrf.txt"
done
for out in "$dir/$tag"-base-*; do
  other=${out/-base-/-new-}
  if cmp -s <(sed -e 's#/base#/X#g; s#\base#\X#g; s/[0-9.]* KB  Kronikol/SIZE  Kronikol/' "$out") \
            <(sed -e 's#/new#/X#g; s#\new#\X#g; s/[0-9.]* KB  Kronikol/SIZE  Kronikol/' "$other"); then
    echo "$tag $(basename "$out" | sed "s/$tag-base-//"): same but for path and size ($(tail -1 "$other"))"
  else
    echo "$tag $(basename "$out" | sed "s/$tag-base-//"): DIFFERS"
  fi
done
