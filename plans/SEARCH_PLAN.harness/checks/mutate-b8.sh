#!/usr/bin/env bash
# B8 and B17's mutation checks (SEARCH_PLAN 6.4, checks/mutations-b8.md): each breaks one shipped script, runs the test
# classes that should fail, one class per run, and puts the file back byte for byte before the next.
#   bash plans/SEARCH_PLAN.harness/checks/mutate-b8.sh            every mutation
#   bash plans/SEARCH_PLAN.harness/checks/mutate-b8.sh R1 C1      the ones named
# Playwright runs one class at a time on its bundled Chromium. Logs go to $TMPDIR/kronikol-mutate-b8.
set -u
ROOT=$(git -C "$(dirname "$0")" rev-parse --show-toplevel)
OUT=${TMPDIR:-/tmp}/kronikol-mutate-b8
mkdir -p "$OUT"

J=Kronikol.Tests.SearchEngine:ShippedSearchFunctionTests
E=Kronikol.Tests.EndToEnd:SearchFunctionPageTests
P=Kronikol.Tests.EndToEnd:FilterPerformanceTests

# <id> <file under src/Kronikol/Reports> <replacements expected> <old> <new> <project:class>...
mutate() {
  local id=$1 file=$2 count=$3 old=$4 new=$5; shift 5
  local src="$ROOT/src/Kronikol/Reports/$file" backup="$OUT/$id.orig"
  cp "$src" "$backup"
  if ! OLD="$old" NEW="$new" COUNT="$count" perl -0pi -e 'my $c = s/\Q$ENV{OLD}\E/$ENV{NEW}/g; die "expected $ENV{COUNT} replacements, made $c\n" unless $c == $ENV{COUNT};' "$src"; then
    cp "$backup" "$src"; echo "$id: not applied"; return 1
  fi
  echo "== $id: $file"
  for target in "$@"; do
    local project=${target%%:*} class=${target#*:}
    (cd "$ROOT" && dotnet test "tests/$project" --filter "FullyQualifiedName~$class" -nologo 2>&1) > "$OUT/$id.$class.log"
    echo "-- $class"
    grep -E "^(Passed|Failed)!" "$OUT/$id.$class.log" || echo "(no summary: the build failed?)"
    grep -E "^\s+Failed [A-Za-z]" "$OUT/$id.$class.log" | sed -E 's/^\s+Failed /   FAILED /; s/ \[[^]]*\]$//' || true
  done
  cp "$backup" "$src"
  cmp -s "$backup" "$src" && echo "-- put back byte for byte"
}

WANTED="$*"
want() { [ -z "$WANTED" ] || [[ " $WANTED " == *" $1 "* ]]; }

want W1 && mutate W1 report-search-function.js 2 '/"(.*?)"/g' '/\u0001(.*?)\u0001/g' $J $E
want M1 && mutate M1 report-search-function.js 1 'if (!text.includes(searchTokens[j])) {' 'if (j === 0 && !text.includes(searchTokens[j])) {' $J $E
want M2 && mutate M2 report-search-function.js 1 'input = input.toLowerCase().trim();' 'input = input.trim();' $J
want M3 && mutate M3 report-search-function.js 1 'let text = c.items[i].searchText;' "let text = c.items[i].el.getAttribute('id');" $J $E
want H1 && mutate H1 report-scenario-feature-map-helper.js 1 "d.el.style.display = hidden ? 'none' : '';" "d.el.classList.toggle('search-hidden', hidden);" $E $P $J
want R1 && mutate R1 report-search-function.js 1 "[el.closest('details.feature'), el.closest('details.rule'), el].forEach(" "[el.closest('details.feature'), el.closest('details.rule')].forEach(" $E
want R2 && mutate R2 report-search-function.js 1 "['details.example-diagrams', 'details.scenario-steps', 'details.scenario-background']" "['details.example-diagrams']" $E
want R3 && mutate R3 report-search-function.js 1 "['details.example-diagrams', 'details.scenario-steps', 'details.scenario-background']" "['details.example-diagrams', 'details.scenario-steps', 'details.scenario-background', 'details.example']" $E
want R4 && mutate R4 report-search-function.js 2 'if (matchCount === 1 && singleMatch) {' 'if (matchCount >= 1 && singleMatch) {' $E
want C1 && mutate C1 report-search-function.js 2 'c.items[i].sr = false;' "/* the search's flags kept */" $E
want F1 && mutate F1 report-scenario-feature-map-helper.js 1 "rules[i].style.display = hasVisibleInRule ? '' : 'none';" "rules[i].style.display = '';" $E
want F2 && mutate F2 report-scenario-feature-map-helper.js 1 "f.style.display = hasVisible ? '' : 'none';" "f.style.display = '';" $E
want P1 && mutate P1 report-search-function.js 1 'c.items[i].sr = !(textMatch && tagMatch);' 'c.items[i].sr = !(textMatch && tagMatch); c.items[i].tag = false;' $E
want D1 && mutate D1 report-search-index.js 1 'window._kronDeepQuery = function(input) {' 'window._kronDeepQuery = function(input) { return;' $E
want T1 && mutate T1 report-dependency-filter-function.js 1 "c.items[i].dep = !chip_match(c.items[i].deps, sets, _depMode === 'AND');" "c.items[i].dep = false;" $P
git -C "$ROOT" diff --quiet -- src || echo "src is NOT as it was"
