#!/usr/bin/env bash
# Publishes the 5.0.0 wiki series: a fresh clone of the live wiki, the series applied with `git am -3`, each commit's
# subject reworded as the live wiki writes a release ("5.0.0: ...") and its author set to the live wiki's, the link
# check, then (only with "push" as the first argument) the push to master. usage: wiki-publish.sh [push]
set -euo pipefail
# usage as kept in the harness: REPO=<Kronikol checkout> W=<an empty folder for the clone> wiki-publish.sh [push]
SERIES=$REPO/plans/OPTION_C_PLAN.harness/wiki/series
LINKCHECK=$REPO/plans/OPTION_C_PLAN.harness/wiki/linkcheck.py
WHO=aryehcitron@gmail.com

rm -rf "$W"
git -c core.autocrlf=false clone -q -c core.autocrlf=false https://github.com/lemonlion/Kronikol.wiki.git "$W"
cd "$W"
BASE=$(git rev-parse HEAD)
echo "live head $BASE"
git -c user.name="$WHO" -c user.email="$WHO" am -q -3 "$SERIES"/*.patch
echo "tree $(git rev-parse HEAD^{tree})"

export FILTER_BRANCH_SQUELCH_WARNING=1
git -c user.name="$WHO" -c user.email="$WHO" filter-branch -f \
  --env-filter "export GIT_AUTHOR_NAME='$WHO' GIT_AUTHOR_EMAIL='$WHO' GIT_COMMITTER_NAME='$WHO' GIT_COMMITTER_EMAIL='$WHO'" \
  --msg-filter "sed -e 's/^5\.0\.0 draft, LOCAL ONLY (do not push before 5\.0\.0 ships): /5.0.0: /' \
                    -e 's/^5\.0\.0 draft, LOCAL ONLY: /5.0.0: /' \
                    -e 's/^TAGS_PLAN wiki patch (kronikol-89, .*/5.0.0: labels and categories are one list of tags (TAGS_PLAN T4): Tags and Attributes, Migrating to v5: Tags (new), Marking New and Changed Tests, Search Syntax, Generated Reports, the integration pages and the rest of the 29 pages the tags reach/' \
                    -e '/^Here only to test my series on top/d'" \
  "$BASE"..HEAD > /dev/null
echo "tree after reword $(git rev-parse HEAD^{tree})"
git log "$BASE"..HEAD --format='%an | %s' | cut -c1-150
if git log "$BASE"..HEAD --format=%B | grep -qi "local only\|draft\|here only to test"; then echo "REWORD LEFT SOMETHING"; exit 1; fi
# The release's own edit on top: the pages' package references name the release (they named 4.14.11, and Assertion
# Tracking 4.*), since a reader who copies one would otherwise install a 4.x package.
python -I - <<'PY'
import glob, re
n = 0
for p in glob.glob('*.md'):
    t = open(p, 'rb').read().decode('utf-8')
    t2, k = re.subn(r'(Include="Kronikol[^"]*" Version=")(?:4\.14\.11|4\.\*)(")',
                    lambda m: m.group(1) + ('5.*' if m.group(0).endswith('4.*"') else '5.0.0') + m.group(2), t)
    if k:
        open(p, 'wb').write(t2.encode('utf-8'))
        n += k
assert n == 14, n
print('pins', n)
PY
git add -A
git -c user.name="$WHO" -c user.email="$WHO" commit -q -m "5.0.0: the pages' package references name 5.0.0, where twelve named 4.14.11, and Assertion Tracking's 5.*, where it named 4.*, so a reader who copies one installs the release"
git log -1 --format='%an | %s' | cut -c1-150
if grep -rhn 'Include="Kronikol[^"]*" Version="4' *.md; then echo "A 4.x PIN IS LEFT"; exit 1; fi
python -I "$LINKCHECK" *.md | tail -1
if [ "${1:-}" = "push" ]; then git push origin HEAD:master; fi
