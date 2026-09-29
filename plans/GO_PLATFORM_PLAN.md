# GO_PLATFORM_PLAN.md — Kronikol for Go: a fifth platform, after Java, Node and Python

**Date:** 2026-09-28 · **.NET repo:** 3.31.10 (`e7e8504`) · **Kronikol4J:** 0.1.25-SNAPSHOT · **Go:** 1.27.1 current, 1.24.7 on the machine
· **Status: design record, investigation run, nothing implemented. Green-lit 2026-09-28: D29 and Q1 to Q8 taken as recommended (§7, §11).** Answers the owner's
question of 2026-09-28: "Can you come up with a plan to have Kronikol also support Go after it supports
python, js and java". **Amended 2026-09-29** at the owner's request: pgx's own API gets its rows through
a protocol tap joined to its tracer (§3.4, K27 to K29, H15; §11).

**The decision was D29 in `ROADMAP.md` §3, taken 2026-09-28 as recommended** (drafted as D28 on this
plan's branch; it lands as D29 because D28 went to the query fallback, stage 1b). The platform is stage
14.16, after 14.9's Python. One part of it is not after anything: §4 is seven questions for the capture
contract, and rule 6 says the five format changes among them cost nothing before 14.5 freezes the format
and a format version after. With D29 taken they are `PLATFORM_FOUNDATIONS_PLAN.md` §4.8, decided in or
out at 14.5 (M0).

**What it is.** The Go instance of `PLATFORM_FOUNDATIONS_PLAN.md` §9's template, the eleven items every
platform plan fills in, in that order (§3). That plan names four platforms and says it "does not
promise a fourth platform"; this is a fifth, and it assumes everything F0 to F10 build exists when it
starts. It is also the first §9 instance written: the Java, Node and Python platform plans do not exist
yet, so the template is applied here before it has been proven, and this plan should be re-read against
the first of them.
`NEXT_LANGUAGE_PLAN.md` §3 deferred Go "until the instrumentation story changes"; §1 is what changed and
what did not, and that plan now carries a banner pointing here.

**How far each fact was checked** follows the roadmap's marks: RUN (a command was executed today), READ
(the source, plan or document was read today), PLAN (another plan says so, not re-checked), INFERRED
(reasoned from two facts, stated by neither), ASSUMED (general knowledge, not verified today; every
ASSUMED row a milestone depends on is a check before that milestone starts). Every RUN has its probe in
[`GO_PLATFORM_PLAN.harness/`](GO_PLATFORM_PLAN.harness/), numbered H1 to H15 (H4 and H12 folded into
H11) and run on Go 1.24.7 and 1.27.1 (H15 on 1.27.1, against PostgreSQL 16.13); its README maps each probe to the facts it supports, and `run.sh` regenerates every result.
Every fact about a rendered report came from `kronikol query`; no report file was opened.

---

## 0. The answer in one paragraph

Go needs no foundation the other platforms do not already need, and no rendering code: a Go capturer is
the §9 template filled in, and the harness built a throwaway one (about 1,100 lines, standard library
only) whose output today's `kronikol ingest` rendered into a full report, failure digest included. What
makes Go different is not whether it can be instrumented, which the deferral doubted, but four things no
other platform has. **Identity rides `context.Context`**, so a call that drops the context needs a
fallback, and the one that works is a goroutine-label pointer the runtime copies into every goroutine it
starts, read through a symbol Go promises to keep. **`go test` is its own run model**: one process per
package, a cache that replays a passing package without running it, failure text the test cannot read,
and a timeout that ends the binary before cleanup; so Go needs a run wrapper, `kronikol-go test`, that no
other platform needs. **Neither maintained Go WebAssembly runtime can host the shared renderer's
component in-process**, so the host shim execs a binary. And **Go's newest test idioms**, the in-memory `httptest` network and
`testing/synctest`'s fake clock, change what a capture record can say about where and when. The
recommendation: answer §4's contract questions now, before 14.5; build the platform after Python, core
first, in the milestones of §5; give it its own launch at its own bar (D18).

**Three things up front.**

1. **Re-open Go** (D29, taken 2026-09-28). The deferral was right that nothing can be injected into a running Go binary
   and wrong about what follows: the wrap sites of a Go component test are in the test's own wiring,
   and the two cases where they are not have answers (§1).
2. **The contract asks are the only urgent part** (§4): an ordering key that is not the clock, SQL
   parameters as data rather than a text convention, the attribution source on the wire, the raw test
   name with its framework, and the tests stream as a directory. None needs Go code.
3. **Exec, never embed, the renderer** (§3.7). Neither maintained Go WebAssembly runtime can run .NET
   10's WASI Preview 2 component; the shim runs the NativeAOT binary, the wasmtime CLI or, until the shared
   renderer exists, `kronikol ingest`.

---

## 1. What changed since Go was deferred, and what did not

`NEXT_LANGUAGE_PLAN.md` §3 (2026-09-13) ranked Go "best fit, worst mechanics" and deferred it. Its
premises, one by one, against the harness:

| The deferral said | What the harness found | Verdict |
|---|---|---|
| "it is impossible to add additional code at runtime to instrument Go applications" | True of injecting code into a running binary. But three runtime-level hooks need no injection. `http.DefaultTransport` is a package variable (K2). `Transport.RegisterProtocol` hooks the shared transport in place and keeps its type, so even clients built at package initialisation are covered (K3). The runtime copies a goroutine's pprof label pointer into every goroutine it starts, and Go promises to keep the symbol that reads it (K5). At compile time, zero-code instrumentation is now stable: OpenTelemetry's `otelc` is v1.1.0, rewriting the build through `-toolexec` with rules for `net/http`, `database/sql`, `runtime` and 20 other targets (K23) | Right about run time; overtaken at compile time |
| "Capture means explicit wrapping … which is idiomatic in Go but puts the burden on the user's code, a different product" | Half right. A Go component test constructs the service it tests (constructor injection is the idiom), so the wrap sites are in test code: the prototype's whole cost to its user was `TestMain`, one `Start(t)` per test, and four wraps where the test already built the service (H11, `orders_test.go`). The burden reaches production code in two cases: a service that opens its own `*sql.DB` under a driver name it does not read from configuration, since a name cannot be registered twice (K6), and a service that drops the context (K19) | A different product only where the service hides its wiring. The tap (F10) covers the first case, the label fallback most of the second |
| The eBPF route needs privileges and "shows generic DB operations rather than detailed statements" | Unchanged. OpenTelemetry's eBPF agent is still v0.24.0 (K23) | Stands. Not pursued |
| One adapter: "stdlib `testing`" | One and a half. `testing` covers testify's suites, which run on `*testing.T`; godog and Ginkgo are separate runners with their own hooks (K22). And `testing` cannot hand a test its own failure text or a timed-out test's verdict (K9): part of the adapter has to live outside the process | One adapter plus a run wrapper; godog and Ginkgo by demand |

**What the ranking did not know, in Go's favour.** The tail is cheap. Every library the harness read at
its current version exposes a context-carrying hook or an injectable client: pgx's `QueryTracer`,
go-redis's `Hook`, the MongoDB driver's `CommandMonitor` (with command and reply bodies), gRPC's
interceptors, franz-go's hooks, kafka-go's `RoundTripper`, the AWS SDK's `HTTPClient` (K22). `go test
-json` gives every test's start, verdict and output with no adapter at all (K9), and since Go 1.25
`T.Attr` puts the scenario's id into that stream (K13).

**And against it.** Neither maintained Go runtime can run the shared renderer in-process (K16). The test cache silently
drops a package's captures (K8). The in-memory test network and the fake clock move the ground under a
capture record (K11, K15).

"Best fit, worst mechanics" becomes **best fit, different mechanics.**

---

## 2. Where Go stands today (checked 2026-09-28; K27 to K29 on 2026-09-29)

| # | Fact | Basis |
|---|---|---|
| K1 | Go 1.27.1 is the current release; the machine has 1.24.7. "Each major Go release is supported until there are two newer major releases", so 1.26 and 1.27 are supported today. Go 1.27 still has one WebAssembly system target, `wasip1/wasm` | RUN (go.dev's release JSON, `go tool dist list`), READ (go.dev/doc/devel/release) |
| K2 | Swapping `http.DefaultTransport` for a wrapper captures `http.Get`, `http.DefaultClient` and every client whose `Transport` is nil, and misses clients built at package initialisation, clients with their own transport and `httptest`'s client. Afterwards `http.DefaultTransport.(*http.Transport).Clone()` panics (`interface conversion: http.RoundTripper is *main.recorder, not *http.Transport`), and the comma-ok form silently takes its fallback | RUN (H1, both toolchains) |
| K3 | `RegisterProtocol("http", rt)` on the default transport keeps it a `*http.Transport`, captures the same clients as K2 plus the init-time one (it holds the same pointer), and not a `Clone()` taken afterwards. `RegisterProtocol("https", …)` panics with `protocol https already registered`: HTTP/2 owns that scheme once the transport is set up | RUN (H2) |
| K4 | A RoundTripper can capture both bodies without changing what the caller sees, including a 307 replayed through `GetBody`. Reading the response before returning (what the .NET handler does, awaiting `ReadContentAsStringAsync`) delayed an SSE stream's first event by the full 1.5 s until its second; teeing returned at once, but left the record of a body nobody reads pending until something flushes it. A 1 MiB body reached the caller whole with the record capped at 64 KiB | RUN (H3), READ (`TestTrackingMessageHandler.cs`) |
| K5 | pprof labels set on a goroutine reach its children and grandchildren, not a goroutine started earlier. Read through `runtime/pprof.runtime_getProfLabel`, which `runtime/proflabel.go` keeps linkable on both toolchains ("Do not remove or change the type signature. See go.dev/issue/67401"), and keyed by the pointer, they need no knowledge of the label layout, which differs between 1.24.7 (`LabelSet{list []label}`) and 1.27.1 (`labelMap{label.Set}`). A server started while test T1's label was on the goroutine served test T2's request under T1 until the handler re-labelled from the headers; a user's own `pprof.Do` replaces the pointer | RUN (H5, both toolchains), READ (both runtimes' sources) |
| K6 | One wrapper around `driver.Connector` saw statement text, argument values, the caller's context and the rows, through `ExecContext`, `QueryContext`, `QueryRowContext`, a prepared statement (with each execution's own context) and a transaction, on a driver implementing only the mandatory interfaces (by answering `driver.ErrSkip`) and on one implementing all of them. `db.Exec` with no context carried no identity. A second `sql.Register` of a name panics. A rows wrapper that forwards only `driver.Rows` hid two optional interfaces with no error: `DatabaseTypeName` went from `INT8` to `""`, `NextResultSet()` from true to false | RUN (H6) |
| K7 | `go test ./...` builds one binary per package and runs them as separate processes at the same time | RUN (H7 S1, S7) |
| K8 | A passing package is cached: the next run replays its recorded output, per-test `run` and `pass` events and the old process id included, without running it. An environment variable a test reads is part of the cache key; one read in `TestMain` before `m.Run()` is not. `-count=1` disables the cache for every package | RUN (H7 S2 to S4, H11) |
| K9 | A panicking test runs its `t.Cleanup`, then ends the binary: the package's later tests have no events at all. A test past `-timeout` ends the binary without `t.Cleanup` and without a per-test verdict, only the package's `fail`. A package that does not compile reports `build-output` and `build-fail` events and a `fail` with `FailedBuild`. `testing.T` has no method that returns what the test logged | RUN (H7 S1, S5, S6), READ (the method set, 1.27.1) |
| K10 | `t.Run` names are mangled: `valid order` becomes `valid_order`, a repeated `dup` becomes `dup#01`, and `a/b` stays `a/b`, which reads as nesting. `t.Context()` is cancelled before `t.Cleanup` runs, and values derived from it are still readable there | RUN (H7 S1) |
| K11 | Inside a `testing/synctest` bubble `time.Now()` starts at 2000-01-01T00:00:00Z and a 5 s sleep costs no real time; a stamp rebased onto the wall clock lands 5 s after records written just after the bubble | RUN (H8: 1.24.7 with `GOEXPERIMENT=synctest`, and 1.27.1) |
| K12 | From 1.24.7 to 1.27.1: `testing` gained `T.Attr` and `T.Output` (1.25) and `T.ArtifactDir` with `go test -artifacts` (1.26); `testing/synctest` became a standard package with `Test` and `Wait` (1.25) and `Sleep` (1.27); `net/http` gained a connection-level `ClientConn` (`Transport.NewClientConn`, 1.26) and `CrossOriginProtection`; `httptest` gained `NewTestServer` (1.27); `database/sql/driver` gained `RowsColumnScanner` (1.27), which `database/sql` then calls instead of `Next`. `runtime/pprof` and `context` did not change | RUN (H9, each API checked against the 1.25.0, 1.26.0 and 1.27.0 toolchains), READ (`go doc`) |
| K13 | `T.Attr("kronikol.testId", id)` (Go 1.25) appears in `go test -json` as `{"Action":"attr","Test":…,"Key":…,"Value":…}` | RUN (H9b) |
| K14 | Under `-artifacts -outputdir D`, `T.ArtifactDir()` (Go 1.26) is `D/_artifacts/<package>/<test>/<n>/`, and `-json` reports it as an `artifacts` event with its `Path`; without `-artifacts` it is a temporary directory removed after the test | RUN (H9b), READ (`go doc`) |
| K15 | `httptest.NewTestServer` (Go 1.27) serves on an in-memory network: its URL is `http://example.com`, only `srv.Client()` reaches it, and `http.Get(srv.URL)` through the default transport fetched the real example.com. Go's documentation says "Most tests should use the in-memory network", and that it suits `testing/synctest` | RUN (H9b), READ (`go doc net/http/httptest.Server`) |
| K16 | Neither maintained Go host runs .NET 10's WASI output in-process, and the third Go binding, wasmer-go, has not released since v1.0.4 (2021-08-12). wazero v1.12.0 (pure Go) runs a 2,593 KB WASI Preview 1 guest doing the renderer's file I/O (compile about 1 s, run 8 ms) and refuses both the component preamble and the real `dotnet.wasm` (12,465,086 bytes) with `invalid version header`; its rationale defers wasip2 "when the design settles". wasmtime-go v49.0.0 (cgo; 280 MB in the module cache, a 40 to 69 MB static library per platform) compiles the component and cannot instantiate it: `component imports instance wasi:cli/environment@0.2.0, but a matching implementation was not found in the linker`, and its component linker has no WASI definer (`TODO: WASIp2 / wasi:http integration`) | RUN (H10, H10b), READ (H14 for the versions) |
| K17 | A Go capturer of the template's items 1 to 4 and 6, standard library only, is about 900 lines, and the run wrapper about 190 | RUN (H11) |
| K18 | The prototype's NDJSON from a component test (an orders API, a payments dependency, a fake PostgreSQL; six scenarios with table-driven and parallel subtests, one failure) rendered through `kronikol ingest` built at `e7e8504`: 68 records in 6 scenarios, and `Failures.md` carrying the Go failure text, which only go test's stream had | RUN (H11) |
| K19 | 21 of the 31 requests carried their scenario in the context. The other 10 dropped it (an `http.Get`, a goroutine started from `context.Background()`). Without the label fallback, over four runs 6 to 8 went to the only running scenario and 2 to 4, from two parallel tests, to nobody; in all three rendered runs ingest's time window then filed ALICE-1's calls under BOB-22; and all 10 named `Caller` as their caller, not the `Orders API` that made them. With it, all 10 went to their own scenario under the right caller, and nothing reached the window | RUN (H11) |
| K20 | The report said "some SQL was captured with placeholders and no values … Set both LogParameters = true and Verbosity = Raw", though the values were captured: the renderer recognises them only by the literal `\n-- Parameters: ` block .NET's trackers append (`ParameterCaptureHint.cs`, `SqlDiagnosticTracker.cs:373`). It titled the Go package `H11/orders`, capitalising an import path | RUN (H11), READ |
| K21 | `kronikol ingest` takes one `--tests` file (`IngestCommand.cs:57-60`) and leaves that file out of its interaction inputs; any other tests fragment among the inputs is read as interactions (17 malformed lines for one fragment) | RUN, READ |
| K22 | Seams at the proxy's latest versions: pgx v5.11.0 `ConnConfig.Tracer` (`QueryTracer`: SQL, arguments, context; no rows); go-redis v9.22.0 `Hook`; mongo-driver v2.9.1 `event.CommandMonitor` (command and reply as `bson.Raw`); grpc v1.84.0 unary and stream interceptors, client and server; sarama's `ProducerInterceptor.OnSend` (no context); franz-go v1.22.1 hooks; kafka-go v0.4.51 `RoundTripper`; aws-sdk-go-v2 v1.47.1 `aws.HTTPClient`; godog v0.16.0 `ScenarioContext` (`Before`, `After`, `BeforeStep`, `AfterStep`) and `TestSuite`, with formatters `cucumber` (JSON), `events`, `junit`, `pretty` and `progress`, none of them Cucumber Messages; ginkgo v2.33.0 `ReportAfterEach`; testify v1.12.1 `assert.TestingT` (`Errorf` only) and `suite.Suite` on `*testing.T`; the OTel SDK v1.46.0 `SpanExporter`; GORM's Postgres driver v1.6.3 `Config.Conn`; testcontainers-go v0.44.0 | READ (H13, `go doc`) |
| K23 | Zero-code instrumentation for Go: OpenTelemetry's `opentelemetry-go-compile-instrumentation` v1.1.0 (2026-08-24, "Status: Stable"), a `-toolexec` rewriter with nine rule kinds and 28 manifest entries over 23 targets, among them `net/http`, `database/sql`, `runtime`, gRPC, go-redis, kafka-go and both MongoDB drivers. Datadog's Orchestrion v1.13.1 and Alibaba's loongsuite-go-agent v1.15.0 do the same. The eBPF agent is v0.24.0 | READ (H14) |
| K24 | The module proxy already serves this repository: `github.com/lemonlion/kronikol` has versions up to `v3.31.10+incompatible`, because the release tags carry no `go.mod`, under both spellings of the name | RUN (H14) |
| K25 | Apache Arrow's Go module lived in its polyglot monorepo's `go/` directory with `go/vN.x.y` tags and a major version tracking Arrow's; the last tag there is `go/v17.0.0` (2024-07-11), and it now ships from its own repository as `github.com/apache/arrow-go/v18` (v18.8.0, 2026-09-04) | READ (H14, the module proxy) |
| K26 | On .NET the identity headers are `test-tracking-current-test-name`, `-current-test-id`, `-caller-name` and `-trace-id`; resolution has four levels and a detached flow (`TestInfoResolver`), and a call nothing names is not recorded unless background capture is on; `AttributionSource` has nine values; a test id is 32 hex digits, the W3C trace id of its `traceparent` (`TestTrackingIdentity`); SQL keeps 10 rows by default (`MaxResponseRows`) | READ |
| K27 | pgx v5.11.0 against PostgreSQL 16.13: its tracers (`QueryTracer`, `BatchTracer`, `CopyFromTracer`, `PrepareTracer`, `ConnectTracer`, and from 5.6.0 the pool's `AcquireTracer` and `ReleaseTracer`) hand a capturer the SQL, the arguments as the caller passed them (a `NamedArgs` map and `@sku`, where the prepared statement reads `$1`), the caller's context, and the command tag (`SELECT 2`) or the error with its SQLSTATE. They carry no result rows (a `RETURNING` id included), no COPY data (the table and column names only), nothing for a batched statement that fails (its error reaches `TraceBatchEnd` alone: `Exec()` returns before tracing at `batch.go:309-313`, where `Query()` traces the same error at `batch.go:365`), and nothing below pgx (`PgConn().Exec`, `Ping`). `ConnConfig.Tracer` is one slot; `multitracer` (5.7.0) combines several. pgx v4 (last release v4.18.3, 2024-03-09) has no tracer, only `ConnConfig.Logger` | RUN (H15), READ (pgx v5.11.0 and v4.18.3 source and changelog) |
| K28 | A tap on the connection recovers all of it. Wrapping the `net.Conn` that `pgconn.Config.AfterNetConnect` (5.8.0, 2025-12-26) hands over after TLS, decoding the protocol with pgx's own `pgproto3` and `pgtype`, and joined to the tracer by connection (a `pgx.Conn` is not safe for concurrent use, so a connection has one caller at a time), the probe recovered, with TLS off and on: every result's columns and rows (int4, int8, text, numeric, jsonb and NULL; binary and text formats; statements served from pgx's statement cache, whose columns cross the wire only when first described), the `RETURNING` row, each batched statement's result and the failing one's error, the COPY rows, and a `PgConn().Exec` with its rows, attributed through the pool's acquire tracer, which receives the caller's context. `BuildFrontend` (pgx 5.0.0 on) did the same under TLS; it misses only what pgconn writes to the connection directly, the client half of `QueryExecModeExec` batches (`pgconn.go:2107`). A `DialFunc` wrapper sees ciphertext under TLS. The decoder is about 300 lines. A module requiring pgx v5.8.0, imported from a service's tests only, moved the service's production binary from v5.5.0 to v5.8.0; one requiring v5.6.0 set `AfterNetConnect` by reflection on v5.11.0 and fell back to `BuildFrontend` on v5.6.0 | RUN (H15, H15b), READ (pgx v5.11.0 and v5.0.0 source) |
| K29 | Importers on pkg.go.dev (public packages, internal ones left out, counted across every version ever published): lib/pq 54,638; GORM's Postgres driver, which runs on pgx's `database/sql` adapter, 16,201; pgx v5's pool `pgxpool` 8,512 and its adapter `stdlib` 3,233; v4's 3,402 and 2,130; go-pg 1,056. The `database/sql` routes outnumber pgx's own pool about 6 to 1; among pgx v5's importers the pool leads the adapter 2.6 to 1. lib/pq is maintained again (v1.12.3, 2026-04-03). go-pg speaks the protocol itself, is in maintenance mode by its README, and has its own `QueryHook` (the context, the query, its parameters, and a `Result` with row counts and the model scanned into) | READ (pkg.go.dev, the module proxy, both READMEs and changelogs, `go doc`) |

---

## 3. The platform, by the template

`PLATFORM_FOUNDATIONS_PLAN.md` §9's eleven items, in its order. Where Go has a choice the others do not,
the item says which way and why. What a user writes, as the prototype had it (K18), in the names this
plan proposes:

```go
func TestMain(m *testing.M) {
	kronikol.InstrumentDefaultTransport() // optional: the service's context-less http.Get and friends
	os.Exit(kronikol.Main(m))
}

func TestPlaceOrder(t *testing.T) {
	ctx := kronikol.Start(t) // t.Context(), carrying the scenario
	// pgConnector: the PostgreSQL driver's driver.Connector
	db := sql.OpenDB(kronikol.Connector(pgConnector, kronikol.Service("orders-db"), kronikol.Category("PostgreSQL")))
	payments := &http.Client{Transport: kronikol.Transport(nil, kronikol.Service("Payments"))}
	api := httptest.NewTestServer(t, kronikol.Handler(orders.New(db, payments), kronikol.Service("Orders API")))
	client := kronikol.Client(api.Client(), kronikol.Service("Orders API"))

	kronikol.Step(ctx, "When", "the order is placed", func(ctx context.Context) {
		req, _ := http.NewRequestWithContext(ctx, "POST", "http://orders/orders", body)
		resp, err := client.Do(req)
		// ...
	})
}
```

and runs `kronikol-go test ./...` where they ran `go test ./...`.

### 3.1 Ambient context (item 1)

**The carrier is `context.Context`.** `kronikol.Start(t)` mints the scenario (32 hex digits, which are
also the W3C trace id, as `TestTrackingIdentity` does; K26) and returns `t.Context()` carrying it.
Everything the test does with that context, and everything a wrapped handler serves for its requests,
is the scenario's. The identity is read once, when a call starts (`NODE_PORT_PLAN.md` §3.2's
bind-at-call-site rule).

**The cascade** is .NET's (K26) in Go's form, with one level .NET does not need:

| Level | Go form | `attributionSource` |
|---|---|---|
| 1. The request's headers | `kronikol.Handler` reads the four headers into the request's context, only when name and id are both present (`NODE_PORT_PLAN.md` §7's topology-4 rule) | `RequestHeader` |
| 2. The test's context | the context `Start(t)` returned | `TestContext` |
| 3. An explicit scope | `kronikol.WithIdentity(ctx, …)`: a consumer that read a message's `kronikol-test-*` headers, a worker handed a job | `Scope` |
| 4. Inheritance | **new**: the goroutine's pprof label pointer, set by `Start` and by every `Handler` request and registered to the identity (K5) | `GoroutineLabel` |
| 5. The only scenario open | the Go form of .NET's global fallback, which .NET sets explicitly: here it is set automatically while exactly one scenario is open in the process, and never while two are | `GlobalFallback` |
| none | recorded with no `testId`, left for the window rule at ingest, which the report marks as inferred | `None` |

A scenario that has ended answers as `Expired`, and `kronikol.Detach(ctx)` marks a flow that belongs to
no scenario (a background service the test started), both as on .NET. Level 4 comes before level 5
because a leftover goroutine of an ended scenario would otherwise land on whichever scenario is open
next; with its label it resolves to its own scenario, as `Expired`.

**Level 4 is what made the prototype's parallel tests correct (K19)**, and it has costs, so:

- **On by default** (Q2, taken), **off two ways**: `KRONIKOL_GOROUTINE_LABELS=0` at run time, and a build tag
  (`kronikol_nolinkname`) that compiles the linkname out for a future Go whose linker refuses it.
- **A self-test at the first `Start`**: set a label, start a goroutine, read it back. If it fails, the
  level turns itself off and the run says so in a diagnostic. A runtime change becomes a degraded
  report, never a wrong one.
- **Re-label on every inbound request**, or clear when the request carries no identity: otherwise a
  server started inside one test serves the next test's requests under the first (K5).
- **Never decode the label set.** The registry is keyed by pointer (K5); the layout has changed once.
- **A CI lane on Go's development branch**, weekly, so a runtime change is seen before a release ships.
- The sturdier successor is compile-time: `otelc` instruments `runtime` (K23), which suggests
  goroutine context propagated at build time; INFERRED from the target's name, and part of M7's spike.

**Egress**: the four headers, a `traceparent` whose trace id is the scenario id, and a `baggage` entry
holding the id (foundations L2), stamped by every wrapped client and producer.

### 3.2 The two writers (item 2)

- **One file per process per stream**, append-only, one `write` per line: a panic loses nothing already
  written; a timeout loses only in-flight records, and the wrapper writes the verdict (K9).
- **The run directory is `KRONIKOL_RUN_DIR`**, the Node plan's name (`NODE_PORT_PLAN.md` §5), fresh for
  every run, and read inside `Start(t)` as well as at start-up. That puts it in go test's cache key, so
  a new run directory re-runs exactly the packages that use Kronikol and no others; read only in
  `TestMain`, it does not (K8, measured both ways).
- **Two subdirectories until F2 settles the directory rule**: `interactions/` and `tests/`. The wrapper
  concatenates `tests/*.ndjson` into the single file today's ingest takes (K21); §4 asks for the
  directory form (C5).
- **A per-process `seq`** beside every timestamp: the order a clock cannot give under `synctest` (K11;
  §4 C1).
- **F2's header record** when it exists, with `"platform":"go"`, the producer's version and
  `runtime.Version()`.

### 3.3 HTTP adapter (item 3)

- **Client, the explicit path**: `kronikol.Transport(rt, opts…)`, and `kronikol.Client(c, opts…)`,
  which wraps `c.Transport`. For `httptest.NewTestServer` the only way in is `srv.Client()`'s transport
  (K15), so the recipe wraps that client and never builds its own; the same recipe serves the loopback
  `httptest.NewServer`, which is all Go 1.26 has.
- **Client, the global switch**: `kronikol.InstrumentDefaultTransport()` registers the `http` scheme on
  the default transport (K3). It keeps the type, so no `.(*http.Transport)` assertion anywhere breaks
  (K2), and it covers package-level clients built before `TestMain`. It does not cover `https`, a
  `Clone()` of the default transport or a `ClientConn` (K12); those are the explicit path's. A request a
  wrapped client already recorded carries a context marker and is not recorded twice. **Swapping
  `http.DefaultTransport` is rejected** (K2).
- **Server**: `kronikol.Handler(h, opts…)` sets the identity (§3.1) and marks the context with the
  service it serves, so its outbound calls name it as their caller, through the label too (K19).
  Optional server-side capture, for handler tests that call `ServeHTTP` directly and so have no client
  to capture on; off when a wrapped client already recorded the exchange.
- **Bodies, by the rule K4 measured**: read before returning when the length is known and within the
  cap; teed otherwise, so a stream's timing is untouched; a teed record still open when its scenario
  ends is written with what was read and a marker. Never mutate the caller's request: capture on a
  clone, and keep `GetBody`.
- **Service names from the wrap site**, never from the URL: every in-memory server is `example.com`
  (K15). The URL still crosses as a raw fact (F3).
- **Security redaction at capture** (foundations §4.4), with the `x-kronikol-redacted` mark; the
  default list is .NET's credential headers.

### 3.4 SQL adapter (item 4)

- **`database/sql` is Go's JDBC.** `kronikol.Connector(c, opts…)` for `sql.OpenDB`, and
  `kronikol.Register("postgres")` for a service that takes its driver name from configuration: it
  registers a wrapped `kronikol-postgres` beside the original, which cannot be taken over, since a name
  cannot be registered twice (K6).
- **Optional-interface fidelity is the silent risk**, Go's counterpart of `NODE_PORT_PLAN.md` §3.10's
  dual-package singletons: a wrapper that forwards less than the driver implements changes the driver's
  behaviour with no error (K6). Forward every optional interface the wrapped value implements, with a
  wrapper type generated per combination, and prove each with a test that compares the wrapped and the
  unwrapped driver. Go 1.27's `RowsColumnScanner` (K12) moves rows from `Next` to `ScanColumn`, so row
  capture reads the destination after the driver has written it.
- **Rows**: the first ten, with column names, as .NET keeps (K26). **Parameters** as data (§4 C2); until
  then in the `\n-- Parameters: ` block the renderer recognises (K20).
- **pgx's own API is the second route**, in a module of its own (`go/pgx`): code on `pgxpool.Pool` or
  `pgx.Conn` never reaches `database/sql`, and among pgx v5's importers it is the more common way in
  (K29). Its tracers alone carry the statement, the arguments, the context and a command tag; not the
  rows, the COPY data, a batched statement that fails, or anything below pgx (K27). So the module is
  two halves joined by connection. **The tracer** gives the identity (its callbacks run on the caller's
  goroutine, so §3.1's cascade applies unchanged), the statement as written and the arguments. **A
  protocol tap** gives what the server returned: it wraps the connection `AfterNetConnect` hands over
  after TLS, decodes it with pgx's own `pgproto3` and `pgtype`, and keeps rows by the connector's rule.
  With both, a record carries what the connector's does, and COPY rows, every batched statement and the
  calls below pgx besides (K28).
- **Wiring is one call where the pool is built**: `kronikolpgx.Instrument(cfg, opts…)` on a
  `pgxpool.Config` or a `pgx.ConnConfig` sets the tap and the tracer, and the tracer passes every
  callback on to one the service had already set: what pgx's `multitracer` does from 5.7.0 (K27), done
  in the module so that its floor stays at 5.6.0.
- **The module requires pgx 5.6.0, not 5.8.0**, because its requirement becomes the production
  build's (K28): it compiles against 5.6.0 (the pool's acquire tracer), sets `AfterNetConnect` by
  reflection where the service's pgx has it, and falls back to `BuildFrontend`, whose one gap is
  `QueryExecModeExec` batches.
- **The tap only observes.** It never blocks, alters or fails a read or a write; a tap that loses its
  place in the protocol stops decoding that connection and says so in a diagnostic, and the record
  keeps the tracer's half. A degraded record, never a wrong one, as §3.1's self-test has it.
- **Not F10.** Foundations F10's tap decodes the same protocol out of process, with the window's
  inferred attribution; this one runs per connection in the process, with the tracer's identity. They
  are separate decoders: F10's is .NET.
- **Out of scope**: pgx v4, which has no tracer and has not released since 2024 (K27); go-pg, in
  maintenance mode, by demand through its own `QueryHook` (K29).
- **The ORMs need nothing further**: GORM takes a wrapped `*sql.DB` through `Config.Conn` (K22); sqlx,
  ent and sqlc sit on `database/sql` or pgx (ASSUMED for those three; a conformance case each).
- **A fixed driver name inside the service** is the case wrapping cannot reach (K6): the recipe names
  the one-line change (the driver name read from configuration, or a connector passed in), and F10's
  Postgres and MySQL wire decoders serve a service that will not change.

What a SQL record carries, by route:

| Route | Who takes it | What the record carries | Basis |
|---|---|---|---|
| `database/sql` through `Connector` or `Register` | every `database/sql` driver: lib/pq, pgx's `stdlib` (GORM's way in), MySQL's | statement, arguments, context, the first ten rows, the error | K6, on fake drivers; M3's fixtures make it live |
| pgx's own API, tracer and tap | `pgxpool`, `pgx.Conn` | the same, and COPY rows, every batched statement, calls below pgx | K27, K28 |
| pgx's own API, tracer only (the tap off, or lost its place) | the same | statement as written, arguments, context, command tag, error | K27 |
| go-pg | go-pg's users | nothing unless built by demand | K29 |
| A driver name fixed inside the service | a service that will not change | F10's tap: rows, attribution inferred | K6, foundations L1 |

### 3.5 OTel bridge and the tail (item 5)

- **The bridge is an OTLP file**: a `sdktrace.SpanExporter` (K22) writing OTLP-JSON lines to the span
  stream F5 adds, and the shared `SpanToInteractionMapper` does the rest (foundations §11, Decision 5).
  About 150 lines (an estimate), in a module of its own because the OTel SDK is a dependency. For a
  service already instrumented with OTel, this is the internal-flow view and the no-body tail at once.
- **Go's tail is deep, not shallow** (K22): its hooks carry bodies. gRPC's interceptors see the request
  and reply messages; the MongoDB monitor sees command and reply; go-redis's hook sees the command and
  its result; franz-go's hooks see records with their context; kafka-go's `RoundTripper` wraps the
  protocol; the AWS SDK is HTTP through `aws.HTTPClient`, so the HTTP adapter and a service classifier
  cover it. sarama's interceptor has no context, so identity crosses in the message headers.
- **gRPC joins HTTP and SQL in the first cut** (Q4, taken; A6's falsifier is asked before M5 builds
  it). Go is gRPC's home ground, and a Go service's
  dependencies are as often gRPC as HTTP. This departs from `NEXT_LANGUAGE_PLAN.md`'s 80/20 on a claim
  with no evidence (A6), and its falsifier is that plan's B7, asked of Go users.

### 3.6 Test-framework adapters (item 6)

**The `testing` adapter is split, because `go test` makes it so (K7 to K9).** F9's SPI names six
operations; in Go the end of a test splits in two, the scenario's own end in the process and the
verdict's text in the wrapper:

| Operation | Where it happens in Go | Why |
|---|---|---|
| Run start | the wrapper: a fresh `KRONIKOL_RUN_DIR`, the options file | many processes and no hook they share (K7) |
| Test start and end | in the process: `Start(t)` and its `t.Cleanup` | the identity is minted where the test runs |
| The verdict's text; timeouts, panics, build failures | the wrapper, from `go test -json`, joined on package and test name, or on the `kronikol.testId` attribute from Go 1.25 (K13) | `testing.T` returns nothing it logged, and a timeout skips `t.Cleanup` (K9) |
| Step start and end | in the process: `kronikol.Step(ctx, keyword, text, fn)` | |
| Assertion | in the process: `kronikol.T(t)` implements testify's `TestingT` and records each failure's text as an assertion record, its expected and actual parsed renderer-side (C6; K22). Passes stay invisible, since testify returns a bool | |
| Attachment | in the process, into `T.ArtifactDir()` under `-artifacts` (K14) | the files go where go test already keeps a test's artifacts |
| Run end, render | the wrapper, after the last package | only the wrapper knows when every process has ended (K7) |

**The wrapper, `kronikol-go test <go test args>`** (named by Q3, taken), is the one thing Go needs that no other platform
does. It runs `go test -json` with a fresh run directory; prints go test's own lines; names any package
that was cached (K8) or did not build (K9), since either means scenarios missing from the report; joins
go test's verdicts and failure text onto the scenarios; writes the `end` a timed-out or killed test
never wrote (`timedOut`, `interrupted`); writes the options file; and invokes the host shim. Without it,
`go test` still captures and `kronikol ingest` still renders; the report then lacks failure text and the
verdicts of tests that never reached `t.Cleanup`.

**Table-driven subtests are Go's scenario outlines**: the parent test is the outline, each case an
example row (the prototype wrote them so, K18). `t.Run` has already mangled the case's name (K10);
`kronikol.Run(t, name, fn)` keeps it, and otherwise the renderer undoes the mangling by a rule keyed to
the framework (§4 C4).

**Then, by demand**: **godog** (BDD, Kronikol's own heritage) through its `ScenarioContext` hooks, since
none of its formatters writes the Cucumber Messages ingest already reads (K22); **Ginkgo** through
`ReportAfterEach`, whose parallel mode is one process per worker, the same fragment rule.

**The failure digest needs Go's dialects** (§4 C6): the conventional `got X, want Y` and testify's
`expected:` and `actual:` block, parsed renderer-side into `Failures.md`'s expected and actual. Today
they arrive verbatim (K18).

### 3.7 The host shim (item 7)

No maintained Go runtime hosts the component in-process (K16), so the shim lives in the wrapper, never
in the capture library, and it **execs**. In order of preference:

1. **The NativeAOT `kronikol-render` binary**: F6's fallback and `QUERY_PORTABILITY_PLAN.md`'s five
   RIDs. The wrapper downloads the build matching its own version from the GitHub release on first use,
   checks it against a SHA-256 table compiled into the wrapper, and caches it under
   `os.UserCacheDir()`; `KRONIKOL_RENDER` names a local copy for a machine with no network.
2. **The `wasmtime` CLI running the component**, if F6 ships wasm only: the invocation foundations §11
   found working.
3. **`kronikol ingest` from the .NET tool**, which works today (K18): the dogfood path until F6.
4. **wazero in-process, only if** the renderer is ever built as a WASI Preview 1 core module, which .NET
   10's toolchain does not emit (foundations §11). Then it would be the best Go host there is: pure Go,
   no cgo, a 2.6 MB guest compiled in about a second (K16). An option, not a plan.

**wasmtime-go is rejected**: cgo in every user's build, 280 MB in the module cache, and no WASI Preview
2 in its Go API as of v49 (K16). A dependency's size and whether it needs cgo are the first things a Go
user checks.

Size: foundations P12's ~50 lines for the invocation, and about 150 more for download, verification and
cache (INFERRED from the esbuild and protoc-jar patterns `QUERY_PORTABILITY_PLAN.md` cites).

### 3.8 Options (item 8)

- **Render options**: a Go struct generated from `render-options.schema.json` (foundations L7), with
  pointer fields and `omitempty` so that only non-defaults are written. The wrapper writes
  `kronikol.render.json` once per run, from its flags and an optional `kronikol.json` beside `go.mod`
  (Q7, taken). Never per process: twenty packages must not write twenty options files.
- **Capture options** at the wrap site as functional options (`Service`, `Caller`, `Category`, the
  redaction list, the body cap), and run-wide switches as environment variables the wrapper passes down.

### 3.9 Conformance (item 9)

- `parity/capture/`'s live-service fixtures through the Go capturer, against the same Testcontainers
  services, started with testcontainers-go (K22).
- `parity/corpus/` through the Go capturer and the shared renderer, byte-identical to .NET's.
- **Go's own cases, as permanent tests**: a cached package; a timeout; a panic; a build failure; a
  `synctest` bubble; an in-memory server; K19's parallel case (two scenarios, a context-less call each,
  each call in its own scenario); optional-interface parity per driver (K6).
- `go vet`, `staticcheck`, `govulncheck` and `-race` on every change; the two supported Go releases on
  Linux, macOS and Windows; Go's development branch weekly (§3.1).

### 3.10 Packaging and release (item 10)

- **Where**: a `go/` subtree, as F0 added `py/`. Not the repository root: the proxy already serves the
  root as `v3.31.10+incompatible` (K24), so a root module would start life colliding with a history the
  proxy never forgets.
- **Module path** (Q1, taken): `github.com/lemonlion/kronikol/go`, lower case and spelled one way
  forever, since both spellings resolve and one build must never see two (K24). A vanity path was the
  alternative, worth it only for a domain kept anyway. The root package is `kronikol` (no package can be called `go`). Integrations with
  third-party dependencies are modules of their own (`go/pgx`, `go/grpc`, `go/otel`, `go/godog`,
  `go/ginkgo`), so a user's `go.sum` carries only what they use; the core imports the standard library
  only, which K17 shows is possible. The wrapper is `go/cmd/kronikol-go`, installed with `go install` or
  declared with a `tool` directive in `go.mod`.
- **Tags**: `go/v0.1.0`, and `go/pgx/v0.1.0` for each nested module. The Go toolchain finds a
  subdirectory module's versions only under tags prefixed with its directory (Arrow's `go/v17.0.0`,
  K25), so **foundations F9's `<lang>-v*` shape cannot hold for Go**: a `go-v0.1.0` tag is invisible to
  `go get`. This is the one place this plan overrides the foundations (Q8, taken), and banners there
  and in `MONOREPO_MIGRATION_PLAN.md` §5a say so.
- **Versions**: independent, from v0.1.0 (`MONOREPO_MIGRATION_PLAN.md` §5b: one version per language
  stack). **Never the .NET number**: v2 and later need `/vN` in the import path, so a lockstep 3.x would
  put `/v3` in every import and change it at each .NET major. Arrow's Go module tracked its monorepo's
  majors and now ships from its own repository (K25).
- **Releasing is pushing the tag**: nothing is uploaded. The proxy and the checksum database keep every
  version they have fetched, so a wrong release cannot be withdrawn, only retracted in a later
  `go.mod`. The release job warms the proxy with `go list -m` and checks pkg.go.dev.
- **Minimum Go** (Q6, taken): the oldest supported release at each release (1.26 today, K1). At that floor `T.Attr`
  and `T.ArtifactDir` are always there, and `NewTestServer` and `RowsColumnScanner` only from 1.27
  (K12); newer APIs sit behind build tags, as the prototype does for `T.Attr` (H11).
- **CI**: `go-ci.yml` on `go/**` and `parity/**`, membership in `parity.yml`, and `go-release.yml` on
  `go/v*` tags.

### 3.11 What is deliberately not built (item 11)

- **eBPF** (K23): no bodies, elevated privileges, statements reduced to operations.
- **Compile-time zero-code capture, yet.** It is the one route to no wiring at all and it is stable at
  v1.1.0 (K23), but it is a spike (M7), not a milestone, until it is known whether a third party can
  ship its own rules, whether it builds test binaries, and what each Go release costs it.
- **Patching machine code at run time** (monkey-patching libraries): unsafe, per-architecture, and
  defeated by inlining (ASSUMED).
- **`https` through the global switch, and `http.ClientConn` round trips** (K3, K12): the explicit
  path's.
- **A Go renderer, or any rendering code** (foundations §1).
- **The .NET tail wholesale.** Modules by demand, gRPC first (§3.5).
- **Mobile Go** (`gomobile`): nobody has asked.

---

## 4. What Go asks of the contract, before 14.5 freezes it

Roadmap rule 6: a format change costs nothing before the freeze and a format version after. These are
this plan's only time-sensitive part, and none needs Go code: each is a question for F2, F3 or F5,
answered in or out with a reason.

| # | Ask | Why Go needs it, measured | Where |
|---|---|---|---|
| C1 | **An ordering key that is not the clock**: a per-producer-process `seq` on every record of both streams, and the renderer ordering one process's records by it | Inside a `synctest` bubble every stamp is 2000-01-01 plus fake time (K11), and Go's own documentation steers tests there (K15). No rebasing orders them against records outside the bubble | F2 |
| C2 | **SQL parameters as data** (`parameters: [{name, value}]`), not the `\n-- Parameters: ` text block | The renderer knows captured values only by that literal; anything else gets a false warning with .NET-only advice (K20). Every non-.NET capturer would otherwise have to reproduce a .NET string | F2, F3 |
| C3 | **`attributionSource` on the wire**, with its values open to a platform (Go adds `GoroutineLabel`), and window attribution marked as inferred in the report | 14.1 already pins it as a member the writer loses. The parallel case shows why a reader must be able to tell: the window rule misfiled a scenario's calls (K19) | F1 and F5 (14.1), F2 |
| C4 | **The raw test name and the framework** (`framework: "go-testing"`) as facts, with casing and de-mangling as renderer rules keyed by framework | Ingest capitalised an import path (K20); `t.Run` names arrive mangled (K10) | F3's title rules |
| C5 | **The tests stream as a directory of fragments**, foundations §4.2's rule applied to `--tests`, or a record kind that lets one directory hold both streams | Go writes one fragment per package process by construction; today there is one `--tests` file, and a second fragment among the inputs becomes 17 malformed interactions (K21) | F2, §4.2 |
| C6 | *(a renderer rule, no format change)* **Go's failure dialects** into `Failures.md`'s expected and actual | Shown verbatim today (K18) | F3, L6 |
| C7 | *(a renderer rule)* **Diagnostics worded by producer**, from foundations G11's `producer` and `platform` | K20's hint names .NET settings to a Go user | F2's header record |

C1 to C5 are every other platform's problem too, which is why they belong in the foundations rather than
here: Python's fake-clock libraries meet C1 (ASSUMED), every non-.NET SQL capturer meets C2, and Node's
per-file fragments meet C5 (PLAN, `NODE_PORT_PLAN.md` §5.4).

---

## 5. Milestones

Each is a slice with the test that says it is done. M0 is now; the rest start after 14.9's Python.

| # | What | Done when |
|---|---|---|
| **M0** | §4's asks into F2, F3 and F5: a banner on the foundations plan (done with this plan) and, with D29 taken, its §4.8 (done 2026-09-28); then a decision on each at 14.5 | Each of C1 to C7 is in or out of `captureFormatVersion 1`, with its reason |
| **M1** | The skeleton, from F9's generator (`kronikol new-platform go`): the `go/` tree, the core module, the writers, levels 1, 2, 3 and 5 of §3.1, `Start`, `Step`, `Main`, CI | The conformance tests run, red, in `go-ci.yml` on the first commit |
| **M2** | HTTP (§3.3) | `parity/capture/`'s HTTP fixtures green; the in-memory server, streaming and redaction cases green |
| **M3** | SQL (§3.4): the connector with generated forwarding, rows and parameters; the pgx module, its tracer and its protocol tap (K28) | The Postgres and MySQL fixtures green; an interface-parity test per supported driver green; through pgx's own API, the Postgres fixtures' rows, a failing batched statement and a COPY green with TLS off and on, on pgx 5.6.0 (`BuildFrontend`) and the newest pgx (`AfterNetConnect`); a tap fed a protocol it cannot follow keeps the tracer's record and says so |
| **M4** | The wrapper and the shim (§3.6 to §3.8) | `parity/corpus/` renders byte-identical through the Go path; the cached, timeout, panic and build-failure cases are permanent tests |
| **M5** | Level 4 of §3.1; the OTel exporter module; the gRPC module | K19's parallel case green with every call in its own scenario; the gRPC fixtures green |
| **M6** | testify assertions, attachments through `ArtifactDir`, godog; then Go's launch at its own bar (D18) | A Go demo beside BreakfastProvider's, rendered by the shared renderer and walked as 13.0 walks the .NET one |
| **M7** | Spikes, not features: compile-time capture through `otelc` or Orchestrion rules (§3.11); the tap recipe for black-box Go services with testcontainers-go | A written verdict for each, with its harness |

**Size.** The prototype is a floor, not an estimate: about 1,100 lines for most of M1 to M4 with none of
the fidelity work, no options channel and no tail (K17). Kronikol4J's core, HTTP and JDBC modules are
5,471 lines (PLAN, `NEXT_LANGUAGE_PLAN.md` §2). Go's consolidated seams (one HTTP interface, one SQL
interface) put it with Python at the low end of that plan's 6 to 9k range; the wrapper and the generated
SQL forwarding are the parts no other platform has. The pgx module's tap adds a protocol decoder, about
300 lines in H15 with no row cap, no check that it still follows the protocol and six types (K28).
Plan capacity from the order, not the numbers (A14).

---

## 6. Where it sits in the roadmap, and why

| Milestone | Stage | Rule | Why there |
|---|---|---|---|
| M0 | **Before 14.5**, beside 14.11 | 6 | The only format change here, and free only before the freeze. 14.11 is placed the same way for the same reason |
| M1 to M6 | **14.16**, after 14.9's Python | 9, 6, 8 | Rule 9: nothing the launch bar needs. Rule 6: no port before the shared renderer exists (foundations §6, `NEXT_LANGUAGE_PLAN.md` §4), or Go writes a rendering half. Rule 8: the template is proven by the platforms before it, and Go's cost is better known after three. The owner's question put Go after Python, JS and Java; the rules agree |
| M7 | §6, "Deliberately not scheduled" | 8 | A spike with a trigger: M6 shipped, or a user asks for zero wiring |

**What this does to the order.** Nothing before 14.5; one row beside it (M0) and one after 14.9. D18
stands: Go gets its own launch at its own bar. **Go comes before iOS** (`MOBILE_PLAN.md` M4): Q5, taken
2026-09-28, for the reason the recommendation gave: iOS is already served at the edge by 12b, and a Go
service's component test is the product's own shape. Go is the fifth platform and in-app iOS the sixth;
`MOBILE_PLAN.md`, `NEXT_LANGUAGE_PLAN.md` and the roadmap's 12b say so.

---

## 7. Decisions, all taken 2026-09-28 as recommended

The owner took every recommendation below on 2026-09-28 ("Can we go with the recommended decisions");
§11 records what that set moving.

| # | Decision | Recommendation, taken |
|---|---|---|
| **D29** (roadmap §3) | Re-open Go: M0 now, the platform at 14.16 | **Yes.** M0 costs a banner and seven answers, and only costs that little before 14.5. **Taken 2026-09-28** |
| Q1 | The module path | `github.com/lemonlion/kronikol/go`, lower case; a vanity path only if a domain is being kept anyway |
| Q2 | The goroutine-label level (§3.1) | **On by default**, with the environment switch, the build tag and the self-test. K19 is the case for it |
| Q3 | The wrapper's name | **`kronikol-go`**, not `kronikol`: the .NET tool already answers to `kronikol` on the same `PATH`. Its `query` verb execs the shared artifact's (foundations F7), so a Go user needs one binary |
| Q4 | gRPC in the first cut | **Yes** (§3.5), with A6's falsifier asked first |
| Q5 | Go or iOS first, after the shared renderer | **Go** (§6) |
| Q6 | The minimum Go version | **The two supported releases** (K1) |
| Q7 | Where run-level options live | Flags on `kronikol-go`, and an optional `kronikol.json` beside `go.mod` |
| Q8 | The tag scheme's override of foundations F9 (§3.10) | **Accept**: the Go toolchain decides it |

---

## 8. Assumption ledger

| # | Claim | Status |
|---|---|---|
| A1 | Go component tests usually construct the service, so the wrap sites are in test code | **reasoned** from Go's constructor-injection idiom and the shape H11 imitates; **not measured** on real suites. **Falsifier: read five open-source Go services' component tests and count the wrap sites that are not in test code.** §1's verdict and the cost model rest on it |
| A2 | Context-less calls are a minority in a well-written Go service | **assumed**, nothing measured. K19's 10 of 31 come from a probe built to have them |
| A3 | `runtime_getProfLabel` stays linkable | **read**: the runtime's own promise on 1.24.7 and 1.27.1 (go.dev/issue/67401). A promise, not an API: hence the build tag and the development-branch lane |
| A4 | The runtime keeps copying the label pointer into new goroutines | **measured** on two releases (K5); nothing documents it. The self-test turns a change into a diagnostic |
| A5 | The Go shim can exec a NativeAOT renderer | **assumed**: `QUERY_PORTABILITY_PLAN.md` never attempted the AOT publish (its A2); F6 decides |
| A6 | gRPC is a first-cut dependency for Go services | **assumed**, no evidence. Falsifier: `NEXT_LANGUAGE_PLAN.md`'s B7 question, asked of Go users |
| A7 | godog is the Go BDD framework worth an adapter, and Ginkgo the second runner | **assumed**; their usage was not measured |
| A8 | sqlx, ent and sqlc need nothing beyond the two SQL seams | **assumed**; a conformance case each (§3.4) |
| A9 | `otelc` can carry third-party rules and builds test binaries | **not verified**: K23 read its rule kinds and manifest only. M7 |
| A10 | `otelc`'s `runtime` target propagates context into new goroutines | **inferred** from the target's name. M7 |
| A11 | The five format asks serve the other platforms too | **reasoned** (§4's last paragraph); Python's fake clocks ASSUMED |
| A12 | K19's counts generalise | **No.** One synthetic suite, timing-dependent. What generalises is the mechanism: a time window cannot tell two overlapping scenarios' context-less calls apart |
| A13 | Later Go releases keep what K12 lists | **assumed**; H9's API diff is re-run at each Go release |
| A14 | §5's size | **a floor and an analogy**, not an estimate. Plan capacity from the order |
| A15 | Arrow left its monorepo because of lockstep majors | **inferred** from its tags (K25); its own stated reasons were not read. §3.10's advice stands on Go's `/vN` rule alone |
| A16 | The pgx tap's decoder follows every protocol path and type a suite exercises | **measured** for six types, pgx's default cached statements and its simple protocol, a batch and a COPY, on one server, 16.13 (K28); **not** for pgx's other query modes, arrays, enums, composite and custom types, COPY TO, pgconn driven directly beyond one `Exec`, protocol 3.2 (PostgreSQL 18), CockroachDB, or its cost at run time. Falsifier: M3's fixtures in every query mode on every PostgreSQL release pgx supports (14 and later) |
| A17 | pgx's own API is a large minority of Go services on PostgreSQL, larger among new ones | **inferred** from K29, whose counts span every version ever published and leave out internal packages, where services keep their code; not measured on services. It sizes M3's pgx half, not whether it is built |

**A1 and A2 are the ones to attack first**: together they decide whether Go's different mechanics are a
small tax or a different product.

---

## 9. What this plan does not do

- It builds nothing in `src/` and changes no format: §4 asks, F2 decides.
- It starts no Go work before 14.9, except M0.
- It puts no rendering code in Go and embeds no WebAssembly runtime in the Go library.
- It revises the foundations only in §3.10's tag rule and §4's asks, both by banner there, and the
  monorepo plan's tag scheme by a note in its §5a.
- It does not promise the .NET tail in Go.
- Its prototype (`GO_PLATFORM_PLAN.harness/h11_prototype`) is evidence, not a starting point: the
  harness README lists what it lacks.

## 10. Documentation it owes when it ships

- The wiki: a Go section in the single wiki (foundations L8): install, the wrap sites, the wrapper, the
  test cache, the in-memory server, the attribution levels. Feature pages name options by their schema
  names.
- The README: one line at Go's own launch (D18, rule 9), none before.
- `go/CHANGELOG.md` for the Go line, apart from the .NET changelog (`MONOREPO_MIGRATION_PLAN.md` §3).
- `PLANS_STATUS.md` and `ROADMAP.md` rows, and banners on `NEXT_LANGUAGE_PLAN.md`,
  `PLATFORM_FOUNDATIONS_PLAN.md`, `KRONIKOL4J_PORTABILITY_PLAN.md` and `MONOREPO_MIGRATION_PLAN.md`
  (all done with this plan).

## 11. Decision log

**2026-09-28.** The owner took D29 and Q1 to Q8 as recommended. What that moved, the same day (the
decision was drafted as D28 and renumbered D29 when the plan landed on `main`, where D28 had gone to the
query fallback):

- **M0 began.** §4's seven asks are now `PLATFORM_FOUNDATIONS_PLAN.md` §4.8, beside the fleet plan's
  §4.7, and roadmap 14.5 decides each in or out of `captureFormatVersion 1`. The foundations plan is
  still not green-lit: D29 commits to asking before the freeze, not to the answers.
- **The order.** Go is the fifth platform, stage 14.16, and in-app iOS (`MOBILE_PLAN.md` M4) the sixth,
  after it (Q5). `MOBILE_PLAN.md`, `NEXT_LANGUAGE_PLAN.md` and the roadmap's 12b carry the change;
  Android (M3) is the Java port's work and does not move.
- **§3's open choices are settled**: the module path `github.com/lemonlion/kronikol/go` (Q1); the
  goroutine-label level on by default, with its switch, build tag and self-test (Q2); the wrapper named
  `kronikol-go` (Q3); gRPC in the first cut, once A6's falsifier has been asked (Q4); the two supported
  Go releases as the floor (Q6); flags and an optional `kronikol.json` for run-level options (Q7);
  `go/v*` tags (Q8).
- **What does not move.** M1 to M6 still wait for 14.9 and the foundations they build on (F6's shared
  renderer, F9's generator); nothing is built in Go before then. The two checks the ledger ranks first,
  A1 (where a Go component test's wrap sites are) and A2 (how often a call drops its context), can run
  at any time; their answers size M1 to M5, not whether Go happens.

**2026-09-29.** The owner asked what pgx is, how much it is used and what the plan misses there, then
asked for the answer in the plan. H15 ran pgx against a real PostgreSQL (K27, K28) and pkg.go.dev's
counts were read (K29). What moved:

- **§3.4's pgx route**: from the tracer alone, with no rows, to the tracer joined to a protocol tap on
  the connection, which recovered the rows, the COPY data, the failing batched statement and the calls
  below pgx that the tracer misses. pgx stops being the SQL exception; go-pg is the gap left, by
  demand. §3.4 now tabulates what each route's record carries.
- **The module's pgx floor**: 5.6.0, with `AfterNetConnect` found by reflection, because a
  requirement of 5.8.0 would upgrade its users' production pgx (K28).
- **M3** grows by the decoder and its fixtures, and §5's size says so; A16 and A17 are new.
- **Found on the way**: pgx's batch tracer never reports a batched statement that fails when its
  results are read through `Exec()`, which is how `Close()` reads them (K27). No upstream report was
  filed; with the tap, Kronikol's record does not depend on it.
- **What does not move**: the decisions (§7), the order (§6) and M0's asks. C2, parameters as data,
  applies to the pgx module as to the connector.
