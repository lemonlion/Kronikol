# GO_PLATFORM_PLAN harness

The probes behind every RUN mark in [`../GO_PLATFORM_PLAN.md`](../GO_PLATFORM_PLAN.md), with the output
they printed on 2026-09-28. Go 1.24.7 (the machine's toolchain) and Go 1.27.1 (the current release,
fetched through `GOTOOLCHAIN`), on linux/amd64. Everything but H11's render and H10b is the Go
standard library plus two modules fetched from the public proxy (wazero, wasmtime-go). H11 renders
with `Kronikol.Tool` built from this repository at `e7e8504` (3.31.10) on .NET SDK 10.0.401; H10b uses
the `dotnet.wasm` a .NET 10.0.401 `wasi-experimental` publish writes.

`./run.sh` regenerates every `results-*.txt`. Paths in the results are replaced by `<tmp>`,
`<modcache>`, `<goroot>` and `<harness>`. Timings and the counts in H11's labels-off run move from run
to run (they depend on how the two parallel tests interleave); what the plan cites from them held on
every run.

## The probes

The numbers run H1 to H14 with two gaps: H4 (the identity carried through a served request) and H12
(a capture core built on the standard library alone) were folded into the prototype, H11, which
answers both.

| Probe | What it answers | Plan | Output |
|---|---|---|---|
| `h1_defaulttransport/` | Which clients a swapped `http.DefaultTransport` captures, which it misses (clients built at package init, own transports, httptest's), and what it breaks (`.(*http.Transport)` panics) | K2, §3.3 | `results-h1-h6-seams.txt` |
| `h2_registerprotocol/` | `Transport.RegisterProtocol("http", …)` as a hook that keeps the type: covers init-time clients, not clones; `https` is already HTTP/2's | K3, §3.3 | same |
| `h3_bodies/` | Request and response bodies from a RoundTripper without changing what the caller sees; eager read against tee on an SSE stream, a 1 MiB body, a 307 replay and a body nobody reads | K4, §3.3 | same |
| `h5_labels/` | pprof goroutine labels as an identity child goroutines inherit, read through the one runtime symbol Go keeps linkable for it and keyed by pointer, so no label layout is decoded; the stale label a server started inside another test carries | K5, §3.1 | same (with the two toolchains' label layouts) |
| `h6_sql/` | One `driver.Connector` wrapper on a minimal and a full fake driver: statement text, argument values, the caller's context and rows, on every path; `sql.Register` twice panics; a naive rows wrapper silently hides two optional interfaces | K6, §3.4 | same |
| `h7.sh`, `h7_gotest/`, `h7_summarize.py` | `go test`'s process model: one binary per package, run at once; the `-json` events; `t.Run` name mangling; `t.Context()` against `t.Cleanup`; a panic (Cleanup runs, later tests vanish); a timeout (no Cleanup, no per-test verdict); a build failure; the cache and what re-runs a package | K7 to K10, §3.6 | `results-h7-gotest.txt` |
| `h8_synctest/` | A capture timestamp inside a `testing/synctest` bubble is the fake clock (2000-01-01), and a sleep costs no real time | K11, §4 | `results-h8-synctest.txt` |
| `h9_apidiff.sh`, `h9_go127/` | What `testing`, `net/http`, `httptest`, `database/sql`, `runtime/pprof`, `context` and `log/slog` gained from 1.24.7 to 1.27.1, and which release (1.25, 1.26, 1.27) first had each API the plan uses; `testing.T`'s whole method set; `T.Attr` and `T.ArtifactDir` in the `-json` stream; `httptest.NewTestServer`'s in-memory network (and where a request from any other client goes) | K9, K12 to K15 | `results-h9-go127.txt` |
| `h10_wasmhost/` | wazero (pure Go) hosting a WASI Preview 1 guest that does the renderer's file I/O, and refusing the component preamble; wazero's own stated position on wasip2; wasmtime-go's size and its component linker | K16, §3.7 | `results-h10-wasmhost.txt` |
| `h10b_wasmtimego/` | The real .NET 10 `wasi-wasm` output under both Go hosts: wazero refuses it, wasmtime-go compiles it and cannot instantiate it (no WASI Preview 2 in its Go API) | K16, §3.7 | same |
| `h11_prototype/` | A throwaway capturer (`kgo/`, about 900 lines, standard library only) and run wrapper (`cmd/kronikol-go/`, about 190) around an orders service with a payments dependency and a fake PostgreSQL; the NDJSON rendered by today's `kronikol ingest` and read back with `kronikol query`; attribution with and without the goroutine-label fallback; the test cache | K17 to K21, §3 | `results-h11-prototype.txt` |
| `h13_seams.sh` | The hooks pgx, go-redis, the MongoDB driver, gRPC, sarama, franz-go, kafka-go, the AWS SDK, godog, Ginkgo, testify, the OTel SDK and GORM's Postgres driver expose, read with `go doc` at the proxy's latest version | K22, §3.4, §3.5 | `results-h13-seams.txt` |
| `h14_instrumentation.sh` | Whether Go's zero-code story changed after `NEXT_LANGUAGE_PLAN` deferred Go: the versions of OpenTelemetry's compile-time instrumenter, Orchestrion, loongsuite and the eBPF agent, and what the compile-time instrumenter ships (its rule kinds, its `-toolexec` entry, its 28 targets) | K23 to K25, §1, §3.10, §3.11 | `results-h14-instrumentation.txt` |

## What the prototype is not

`h11_prototype/kgo` exists to prove the shape end to end, not to be started from. It has no options
channel, redacts two header names, forwards no optional `database/sql` interface (H6 is why that is a
defect), classifies SQL by its first keyword because pre-F3 ingest will not, and writes its own guess at
two contract fields the contract does not have yet (`attributionSource`, `seq`). It appends SQL
arguments as `-- $1 = …` rather than the `\n-- Parameters: ` block .NET's trackers write, which is the
only form `ParameterCaptureHint` recognises: that is how the false "placeholders and no values" hint in
`results-h11-prototype.txt` arises (plan K20), and it is left so the result reproduces. Its
goroutine-label level is opt-in (`KRONIKOL_GOROUTINE_LABELS=1`), which is what let H11 measure the
cascade with and without it; the plan's decided design (Q2, taken 2026-09-28) turns it on by default,
behind a self-test. Its size is a floor for the plan's cost model, never an estimate.

## Re-running

```bash
# Go only (everything but the render and H10b):
./run.sh
# With the render and the real component (paths are examples):
dotnet build ../../src/Kronikol.Tool/Kronikol.Tool.csproj -c Release -f net10.0 -o /tmp/kt
DOTNET=$(command -v dotnet) KRONIKOL_TOOL=/tmp/kt/Kronikol.Tool.dll \
DOTNET_WASM=<a wasi-wasm publish>/dotnet.wasm ./run.sh
```

H11's `kronikol-go` passes `--attribute-by-window --run-window` to ingest, sets `KRONIKOL_HISTORY=off`
through the environment the script gives it, and never opens a report file: every fact about the
reports comes from `kronikol query`.
