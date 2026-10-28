#!/usr/bin/env bash
# The three Shouldly corpora of the plan's §2.4 (AutoMapper, MediatR, Marten), fetched at the commits measured, outside
# the repository, and labelled on 5.12.0 (formatter/published.cs) and, given a worktree holding prototype.patch and a
# copy of this harness at the same path, on the prototype (its formatter/probe.cs). Their source is read as text and
# never built. None of it is kept in the repository: AutoMapper and MediatR are under the Lucky Penny Software licence,
# so only the counts are (shouldly-counts.txt).
# Usage: shouldly_corpora.sh <work-dir> [<prototype-worktree>]
set -u
WORK=${1:?work dir}
PROTO=${2:-}
HERE=$(cd "$(dirname "$0")" && pwd)
mkdir -p "$WORK"
while read -r name url commit; do
  src="$WORK/$name/src"
  if [ ! -d "$src/.git" ]; then
    mkdir -p "$src" && git -C "$src" init -q && git -C "$src" remote add origin "$url"
    git -C "$src" fetch -q --depth 1 origin "$commit" && git -C "$src" checkout -q FETCH_HEAD
  fi
  python -I -X utf8 "$HERE/extract.py" "$src" > "$WORK/$name.txt"
  python -I -X utf8 "$HERE/extract_multiline.py" "$src" > "$WORK/$name-multiline.txt"
  for k in "" -multiline; do
    (cd "$HERE/../formatter" && dotnet run --file published.cs -- "$WORK/$name$k.txt" 2>&1 | grep -v 'warning CS' | tr -d '\r' > "$WORK/$name$k-5.12.0.txt")
    if [ -n "$PROTO" ]; then
      (cd "$PROTO/plans/ASSERTION_LABELS_PLAN.harness/formatter" && dotnet run --file probe.cs -- "$WORK/$name$k.txt" 2>&1 | grep -v 'warning CS' | tr -d '\r' > "$WORK/$name$k-prototype.txt")
    fi
  done
  echo "== $name $commit"
  echo "one-line $(grep -vc '^#' "$WORK/$name.txt"), multi-line $(grep -vc '^#' "$WORK/$name-multiline.txt")"
  for v in 5.12.0 ${PROTO:+prototype}; do
    echo "-- $v"
    python -I -X utf8 "$HERE/classify.py" "$WORK/$name-$v.txt" "$src" | tr -d '\r'
    echo "bracket_space: $(python -I -X utf8 "$HERE/bracket_space.py" "$WORK/$name-$v.txt" | tail -1 | tr -d '\r')"
    echo "bracket_space multi-line: $(python -I -X utf8 "$HERE/bracket_space.py" "$WORK/$name-multiline-$v.txt" | tail -1 | tr -d '\r')"
  done
  if [ -n "$PROTO" ]; then
    echo "labels that change: $(diff "$WORK/$name-5.12.0.txt" "$WORK/$name-prototype.txt" | grep -c '^>'), multi-line $(diff "$WORK/$name-multiline-5.12.0.txt" "$WORK/$name-multiline-prototype.txt" | grep -c '^>')"
  fi
done <<'EOF'
AutoMapper https://github.com/AutoMapper/AutoMapper.git fc1488510affb79d1035c052e485f1c6b1d7f2c6
MediatR https://github.com/jbogard/MediatR.git 0e8861720748e871fb10466d004e1cb61881b4d4
marten https://github.com/JasperFx/marten.git baae1f119ee779dd54fe10069ba3f7113ed142d3
EOF
