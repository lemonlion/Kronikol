#!/usr/bin/env bash
# H14. Has Go's "instrumentation story" changed since NEXT_LANGUAGE_PLAN deferred Go (2026-09-13)?
# What the module proxy says about the zero-code routes and the libraries a capturer would stand beside,
# and what OpenTelemetry's compile-time instrumenter ships. Reads only; builds and runs nothing.
set -u
export GOTOOLCHAIN="${GOTOOLCHAIN:-go1.27.1}" GOFLAGS=-mod=mod
latest() { curl -s -m 20 "https://proxy.golang.org/$1/@latest" | python3 -c 'import json,sys; d=json.load(sys.stdin); print(d["Version"], d["Time"][:10])' 2>/dev/null || echo "not found"; }
echo "=== zero-code routes (module proxy, @latest)"
for m in github.com/open-telemetry/opentelemetry-go-compile-instrumentation 'github.com/!data!dog/orchestrion' \
         github.com/alibaba/loongsuite-go-agent go.opentelemetry.io/auto; do echo "  $m  $(latest "$m")"; done
echo "=== Go WebAssembly runtimes (module proxy, @latest)"
for m in github.com/tetratelabs/wazero github.com/bytecodealliance/wasmtime-go/v49 github.com/wasmerio/wasmer-go; do echo "  $m  $(latest "$m")"; done
echo "=== prior art a Go capturer stands beside"
for m in go.opentelemetry.io/contrib/instrumentation/net/http/otelhttp 'github.com/!x!s!a!m/otelsql' github.com/ngrok/sqlmw \
         github.com/cloudwego/localsession; do echo "  $m  $(latest "$m")"; done
d="$(go mod download -json github.com/open-telemetry/opentelemetry-go-compile-instrumentation@v1.1.0 2>/dev/null | python3 -c 'import json,sys; print(json.load(sys.stdin)["Dir"])')"
echo "=== opentelemetry-go-compile-instrumentation v1.1.0: README overview"
sed -n '/## Overview/,/## Quick Start/p' "$d/README.md" | grep -v '^\s*$' | head -12
echo "=== its rule kinds (tool/internal/rule) and the -toolexec entry point"
grep -hn '^type Inst.*Rule struct' "$d"/tool/internal/rule/*.go | sed 's/^[0-9]*://'
ls "$d/tool/cmd/otelc" | grep -i toolexec
echo "=== its instrumentation manifest: $(python3 -c "import json; print(len(json.load(open('$d/tool/data/instrumentation-manifest.json'))))" ) entries; targets:"
python3 -c "import json; print('  '+', '.join(sorted({e['target'] for e in json.load(open('$d/tool/data/instrumentation-manifest.json'))})))"
echo "=== what the module proxy already serves for this repository (its root v* tags carry no go.mod)"
curl -s -m 30 https://proxy.golang.org/github.com/lemonlion/kronikol/@v/list | sort -V | tail -3 | sed 's/^/  /'
echo "  github.com/lemonlion/kronikol  @latest $(latest github.com/lemonlion/kronikol)"
echo "  github.com/lemonlion/Kronikol  @latest $(latest 'github.com/lemonlion/!kronikol')"
echo "=== Go's release policy (go.dev/doc/devel/release)"
curl -s -m 30 https://go.dev/doc/devel/release | grep -i -o 'Each major Go release is supported[^.]*\.' | head -1 | sed 's/^/  /'
echo "=== a precedent: Apache Arrow's Go module in its polyglot monorepo, then in its own repository"
for m in github.com/apache/arrow/go/v17 github.com/apache/arrow/go/v18 github.com/apache/arrow-go/v18; do
  echo "  $m  $(curl -s -m 20 https://proxy.golang.org/$m/@latest | python3 -c 'import json,sys; d=json.load(sys.stdin); print(d["Version"], d["Time"][:10], d.get("Origin",{}).get("Ref","(no tag)"))' 2>/dev/null || echo 'not found')"
done
