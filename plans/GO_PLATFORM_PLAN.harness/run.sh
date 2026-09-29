#!/usr/bin/env bash
# Regenerates every results-*.txt in this directory. See README.md for what each probe answers.
#
# Needs: Go (any 1.24+; the script fetches go1.27.1 through GOTOOLCHAIN), network to the Go module
# proxy, python3. Optional: KRONIKOL_TOOL=<path to Kronikol.Tool.dll built from this repository> and
# DOTNET=<dotnet executable> for H11's render; DOTNET_WASM=<a .NET 10 wasi-wasm dotnet.wasm> for H10b;
# PGURL=<a throwaway PostgreSQL database accepting sslmode=disable and require> for H15, which creates
# and drops a table named h15_orders there.
set -u
here="$(cd "$(dirname "$0")" && pwd)"
cd "$here"
OLD="${OLD:-go1.24.7}"
NEW="${NEW:-go1.27.1}"
DOTNET="${DOTNET:-dotnet}"
scratch="$(mktemp -d)"

anon() { sed -E -e "s#$scratch#<tmp>#g" -e 's#/tmp/tmp\.[A-Za-z0-9]+#<tmp>#g' -e 's#/tmp/[A-Za-z0-9_]+[0-9]{6,}/[0-9]+#<tmp-artifacts>#g' \
  -e "s#$(go env GOMODCACHE)#<modcache>#g" -e 's#/usr/local/go[0-9.]*/#<goroot>/#g' -e "s#$here#<harness>#g"; }

{
  for tc in "$OLD" "$NEW"; do
    for p in h1_defaulttransport h2_registerprotocol h3_bodies h5_labels h6_sql; do
      echo "=== $p ($tc)"
      GOTOOLCHAIN=$tc go run ./$p 2>&1
    done
  done
  echo "=== the label layout each toolchain's runtime/pprof uses (H5 reads none of it)"
  for tc in "$OLD" "$NEW"; do
    root="$(GOTOOLCHAIN=$tc go env GOROOT)"
    echo "-- $tc"; grep -n 'type LabelSet struct' -A2 "$root/src/runtime/pprof/label.go"; grep -n 'type labelMap struct' -A2 "$root/src/runtime/pprof/label.go"
    grep -n 'hall of shame' -A4 "$root/src/runtime/proflabel.go" | tail -5
  done
} 2>&1 | anon > results-h1-h6-seams.txt

{ for tc in "$OLD" "$NEW"; do GOTOOLCHAIN=$tc bash ./h7.sh; done; } 2>&1 | anon > results-h7-gotest.txt

{
  echo "=== H8 $OLD, GOEXPERIMENT=synctest"
  GOTOOLCHAIN=$OLD GOEXPERIMENT=synctest go test -v -count=1 ./h8_synctest 2>&1 | grep -E 'bubble|inside|rebased|took|ok|FAIL'
  echo "=== H8 $NEW"
  GOTOOLCHAIN=$NEW go test -v -count=1 ./h8_synctest 2>&1 | grep -E 'bubble|inside|rebased|took|ok|FAIL'
} 2>&1 | anon > results-h8-synctest.txt

{
  OLD=$OLD NEW=$NEW bash ./h9_apidiff.sh
  echo "=== H9b $NEW: T.Attr, T.ArtifactDir under -artifacts, httptest.NewTestServer"
  out="$(mktemp -d)"
  GOTOOLCHAIN=$NEW go test -json -count=1 -artifacts -outputdir "$out" ./h9_go127 | grep -E '"(attr|artifacts)"'
  (cd "$out" && find . -type f)
  GOTOOLCHAIN=$NEW go test -v -count=1 ./h9_go127 2>&1 | grep -E 'URL =|Get|ArtifactDir ='
} 2>&1 | anon > results-h9-go127.txt

{
  echo "=== H10 wazero: a WASI Preview 1 guest, then the component preamble"
  (cd h10_wasmhost && GOTOOLCHAIN=$NEW GOOS=wasip1 GOARCH=wasm go build -o "$scratch/guest.wasm" ./guest && GOTOOLCHAIN=$NEW go run . "$scratch/guest.wasm")
  W="$(GOTOOLCHAIN=$NEW go env GOMODCACHE)/github.com/tetratelabs/wazero@v1.12.0"
  echo "=== wazero v1.12.0 RATIONALE.md on wasip2"; sed -n '436,440p' "$W/RATIONALE.md"
  WT="$(GOTOOLCHAIN=$NEW go env GOMODCACHE)/github.com/bytecodealliance/wasmtime-go/v49@v49.0.0"
  if [ -d "$WT" ]; then
    echo "=== wasmtime-go v49.0.0: size in the module cache, and its component linker"
    du -sh "$WT" "$WT"/build/* | sed "s#$WT#<wasmtime-go>#"
    grep -n 'func (l \*ComponentLinker)\|TODO: WASIp2' "$WT/component_linker_feat_component_model.go" | sed "s#$WT#<wasmtime-go>#"
  fi
  if [ -n "${DOTNET_WASM:-}" ]; then
    echo "=== H10b the .NET 10 wasi-wasm output under both Go hosts"
    (cd h10b_wasmtimego && GOTOOLCHAIN=$NEW go build -o "$scratch/h10b" . && "$scratch/h10b" "$DOTNET_WASM")
  else
    echo "=== H10b skipped: set DOTNET_WASM"
  fi
} 2>&1 | anon > results-h10-wasmhost.txt

{
  (cd h11_prototype && GOTOOLCHAIN=$NEW go build -o "$scratch/kronikol-go" ./cmd/kronikol-go)
  for mode in off on; do
    run="$scratch/run-labels-$mode"
    echo "=== H11 the prototype end to end, goroutine labels $mode"
    (cd h11_prototype && KRONIKOL_HISTORY=off KRONIKOL_GOROUTINE_LABELS=$([ $mode = on ] && echo 1 || echo 0) KRONIKOL_RUN_DIR="$run" \
      KRONIKOL_TOOL="${KRONIKOL_TOOL:-}" PATH="$(dirname "$DOTNET"):$PATH" "$scratch/kronikol-go" test -count=1 ./orders)
    python3 - "$run" <<'EOF'
import json,sys,glob,collections
run=sys.argv[1]
tests={}
for l in open(run+"/tests.ndjson"):
    r=json.loads(l)
    if r.get("event")=="start": tests[r["testId"]]=r["testName"]
c=collections.Counter(); wrong=[]
for f in glob.glob(run+"/interactions/*.ndjson"):
    for l in open(f):
        r=json.loads(l)
        if r["type"]!="Request": continue
        c[r.get("attributionSource")]+=1
        u=r["uri"]+" "+r.get("content","")
        for sku in ("ALICE-1","BOB-22"):
            if sku in u and sku not in tests.get(r["testId"],""):
                wrong.append((r["method"],r["uri"].split("/")[-1][:20],r.get("attributionSource")))
print("attribution sources of request records:",dict(c))
callers=collections.Counter()
for f in glob.glob(run+"/interactions/*.ndjson"):
    for l in open(f):
        r=json.loads(l)
        if r["type"]=="Request" and ("audit" in r["uri"] or "notify" in r["uri"]):
            callers[(r["callerName"],r.get("attributionSource"))]+=1
print("context-less calls by (caller, source):",dict(callers))
print("parallel customers' calls not stamped with their own scenario:",wrong or "none")
EOF
    if [ -n "${KRONIKOL_TOOL:-}" ]; then
      q() { "$DOTNET" "$KRONIKOL_TOOL" query "$@" "$run/Reports" 2>&1; }
      echo "--- kronikol query scenarios";  "$DOTNET" "$KRONIKOL_TOOL" query scenarios "$run/Reports" 2>&1
      echo "--- kronikol query grep sku=ALICE-1 --in uris"; "$DOTNET" "$KRONIKOL_TOOL" query grep "$run/Reports" "sku=ALICE-1" --in uris 2>&1
      echo "--- kronikol query failures"; "$DOTNET" "$KRONIKOL_TOOL" query failures "$run/Reports" 2>&1
      echo "--- kronikol query services"; "$DOTNET" "$KRONIKOL_TOOL" query services "$run/Reports" 2>&1
    fi
  done
  if [ -n "${KRONIKOL_TOOL:-}" ]; then
    echo "=== H11 ingest's --tests file and a second tests fragment among its inputs (plan K21)"
    both="$scratch/both-streams"; mkdir -p "$both/captures"
    cp "$scratch"/run-labels-on/interactions/*.ndjson "$both/captures/"
    cp "$scratch"/run-labels-on/tests/*.ndjson "$both/captures/tests-fragment.ndjson"
    cp "$scratch/run-labels-on/tests.ndjson" "$both/captures/tests.ndjson"
    (cd "$both" && KRONIKOL_HISTORY=off "$DOTNET" "$KRONIKOL_TOOL" ingest ./captures --tests ./captures/tests.ndjson -o ./Reports 2>&1 \
      | grep -E 'Ingesting|captures/|Tests file|malformed' | head -6)
  fi
  echo "=== H11 the test cache: a run directory read inside the test is part of go test's cache key"
  for d in a a b; do
    echo "--- KRONIKOL_RUN_DIR=cache-$d"
    (cd h11_prototype && KRONIKOL_RUN_DIR="$scratch/cache-$d" KRONIKOL_TOOL= "$scratch/kronikol-go" test -run 'TestPlaceOrder$' ./orders 2>&1 | grep -E 'WARNING|scenarios in')
  done
  echo "=== H11 prototype size (lines; non-blank, non-comment in brackets)"
  for f in h11_prototype/kgo/*.go h11_prototype/cmd/kronikol-go/main.go; do
    echo "$f $(wc -l < "$f") [$(awk '!/^[[:space:]]*(\/\/|$)/' "$f" | wc -l)]"
  done
} 2>&1 | anon > results-h11-prototype.txt

bash ./h13_seams.sh 2>&1 | anon > results-h13-seams.txt
bash ./h14_instrumentation.sh 2>&1 | anon > results-h14-instrumentation.txt

{
  if [ -n "${PGURL:-}" ]; then
    (cd h15_pgx && GOTOOLCHAIN=$NEW go build -o "$scratch/h15" . && "$scratch/h15")
  else
    echo "=== H15 skipped: set PGURL to a throwaway PostgreSQL database"
  fi
  NEW=$NEW bash ./h15_pgxfloor.sh
} 2>&1 | anon > results-h15-pgx.txt
echo "done: $(ls results-*.txt | tr '\n' ' ')"
