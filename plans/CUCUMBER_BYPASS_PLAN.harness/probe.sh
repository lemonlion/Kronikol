#!/usr/bin/env bash
# Runs the issue's three ingests, three variants and the two real producers' messages through one
# Kronikol.Tool build, and prints only what #105 is about (extract.py), plus `query summary`'s count line.
#   bash probe.sh <path to Kronikol.Tool.dll> [scratch dir for the reports; default: a new temp dir]
# The reports are written outside this folder: never open them, extract.py reads the fields it needs.
set -u
TOOL="$1"
OUT="${2:-$(mktemp -d)}"
HERE="$(cd "$(dirname "$0")" && pwd)"
mkdir -p "$OUT"
cd "$HERE/issue" && python "$HERE/variants.py"
run() {
  local name="$1"; shift
  rm -rf "$OUT/$name"
  (cd "$HERE" && dotnet "$TOOL" ingest issue/empty.ndjson "$@" -o "$OUT/$name" > "$OUT/$name.log" 2>&1)
  echo "== $name: ingest $*"
  python "$HERE/extract.py" "$OUT/$name"
  # The feature's count line (a diagnostics line may stand above the header, so it is found by its words).
  echo "   query summary: $(dotnet "$TOOL" query summary "$OUT/$name" 2>&1 | grep -m1 -E ' [0-9]+ passed')"
}
run A --cucumber-messages issue/messages.ndjson
run B --tests issue/tests.jsonl
run C --tests issue/tests.jsonl --cucumber-messages issue/messages.ndjson
run D-failed-then-skipped --cucumber-messages issue/messages-ffs.ndjson
run E-last-step-skipped --cucumber-messages issue/messages-last.ndjson
run F-bypass-attachment --cucumber-messages issue/messages-attach.ndjson
run G-playwright-bdd-9.2.0 --cucumber-messages playwright-bdd/messages.ndjson
run H-cucumber-js-12.9.0 --cucumber-messages cucumber-js/messages.ndjson
