# What answers a tracked call: follow the call past a forwarder, and let a handler say (#219)

**Written:** 2026-10-29, kronikol-07, at `753f17f9` (5.27.2), and moved onto `54e8ee82` (5.27.5). Checked again on
2026-11-07 and moved onto `896ddad3` (5.31.1): its claims hold there, its probes answer as before on the published
5.31.1, and its handler citations are three lines higher, as 5.30.2 shortened the handler above the walk (section 11)
· **Status:** green-lit by the owner on 2026-11-07 ("can you implement the plan in full"), each question
taking its recommended answer: R1 released as 5.31.2, R2 next. · **Issue:** #219, open, filed by the owner on 2026-10-28 on 5.17.0. ·
**Harness:** [`WHAT_ANSWERS_PLAN.harness/`](WHAT_ANSWERS_PLAN.harness/README.md), every probe re-runnable.

Code citations are to `896ddad3` (5.31.1), checked there; wiki citations are to the wiki's `origin/master` at
`f73bdd6`; Kronikol4J citations are to its `main` at `07bdba4`.

---

## 0. Summary

**The cause.** `TestTrackingMessageHandler` answers two questions about every call from one walk of its chain,
`EndOfChain` (`src/Kronikol/Tracking/TestTrackingMessageHandler.cs:406-412`): does the transport write the call's
`traceparent` (since 4.14.3), and did a test double answer the call (since 5.16.0). The walk follows
`DelegatingHandler.InnerHandler` and stops at the first handler that is not a `DelegatingHandler`, which it reads as
where the call ends. A handler that hands the call on another way, through an `HttpMessageInvoker` or an `HttpClient`
it holds, is read as the end. Both answers then go wrong:

- **The mark.** Any end the handler does not know as a transport is named as the double that answered
  (`:421-426`). This breaks the field's own contract: a capturer writes `testDouble` only when it saw a double answer.
  The handler never sees an answer, only a type it does not know.
- **The trace context.** An end that is no socket transport makes the handler write the `traceparent` itself, naming
  the caller's span (`:175-184`). The transport then leaves it alone, so the next host's spans hang under the caller's
  request instead of under the call. The handler's own comment says this breaks Application Insights' dependency
  correlation. The call's response then names the caller's span too, so every call a host makes under one request
  opens that request's whole internal flow. 4.26.5 fixed that fault for a direct transport.

The issue reports the first. The second is unreported and comes from the same walk. A third gap sits beside them: the
mark has one input, the chain, so the test or library that knows what answers (a stub on a socket, an app hosted in
process as a stub) cannot say.

**Measured on 5.27.2, and again on 5.31.1** (P1, P3):

- Every call through either of the issue's forwarders is marked with the forwarder's name, wherever it goes. A real
  service on a socket and an app hosted on TestServer read as `double`, `observed`, and a stub behind `Onward` reads as
  `Onward`.
- Behind either forwarder, a call over a socket sends the caller's span while the transport has started its own.
- Each of three calls a host makes at once opens a popup of up to 6 spans holding all three calls' work, where 2
  are its own.

**The suggestion in the issue** (a `TestDouble` option: unset reads the chain, a name marks every call, empty marks
none) is the right fix for what no walk can see, and becomes R2. On its own it does not reach the cause:

- It is opt-in. Every suite with a forwarder has to notice and set it, and until then a real service reads as a
  double.
- Its empty value silences `Onward` over a stub, which R1 names correctly.
- It leaves the trace context and the spans as they are.

**The plan.**

- **R1, a patch: follow the call.** One walk per call, past delegating handlers and past a handler that holds another
  handler or an invoker, with every path through it followed. Both answers are read from it:
  - a mark only when every end answers the call itself;
  - the `traceparent` left to the transport only when every end is a socket transport.

  The transport's span is read from the request the transport sent. A prototype turns every red row of P1 and P3
  green, and the 297 existing facts nearest the change pass on it.
- **R2, a minor: say what answers.** `TestTrackingMessageHandlerOptions.TestDouble`, the issue's option.

Seven questions for the owner are in section 9.

---

## 1. The issue

Found on 2026-10-28 while moving a .NET monorepo's suites from 5.12.1 to 5.17.0 (Kronikol.xUnit2 5.17.0, net10.0,
Microsoft.Extensions.Http 10.0.14). The suites host ASP.NET Core services in process, and an integration library puts
the tracking handler in their `IHttpClientFactory` pipelines. It ends a tracker's chain in a plain `HttpMessageHandler`
in two places:

1. **`Onward`**: one tracker per receiving name, made on the first request once the name is known (#157, #207). It is
   handed the rest of the pipeline through this forwarder, because the pipeline disposes that handler itself:

   ```csharp
   sealed class Onward(HttpMessageHandler next) : HttpMessageHandler
   {
       private readonly HttpMessageInvoker _next = new(next, disposeHandler: false);

       protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
           _next.SendAsync(request, cancellationToken);
   }
   ```
2. **`SendingThrough`**: a tracked client over a client the test already holds (a `WebApplicationFactory` client, #208).
   Its chain ends in a handler that sends a copy of each request through that client.

Across 19 suites every call through either carried `"testDouble": "Onward"` or `"SendingThrough"`, and
`kronikol query parties` read every party they reached as `double`, `observed`. Those parties were all stubs, so the
kinds were right and the names wrong. But a client of the same kind that reached a real service would make it a double.
The issue also says no call to WireMock can be marked, since the mark is set inside the handler from the chain alone.
It suggests `string? TestDouble` on the handler's options (`null` reads the chain, a name marks every call, an empty
string marks none), "or a resolver given the request, if per-call matters".

The walk, the mark and the trace context have not changed since 5.16.0. `git log v5.16.0..896ddad3` on the handler and
its options gives 5.30.2, 5.31.0 and 5.31.1, which changed only how the handler reads the step type. So 5.17.0 behaves
as 5.27.2 and 5.31.1 do, where the probes ran.

Three open siblings explain why the library forwards at all:

- **#157**: the filter appends the handler innermost, and there is no per-request receiving name, hence one tracker per
  name, made late.
- **#207**: a stub that stands in for several services on one address.
- **#208**: an in-process host's clients, and no way to track an existing `HttpClient`.

None is a prerequisite here. Forwarders are an ordinary .NET shape, written by suites that never heard of those issues.

---

## 2. What happens, measured

**P1** (`p1-forwarders/results-5.27.2.txt`; `results-5.31.1.txt` gives every row the same).
Each shape is tracked by the released handler under a caller's span, as a
host serving a request makes its calls. The socket service is Kestrel on a free loopback port; the in-process one is
the same endpoint on TestServer; the factory client is what `WebApplicationFactory.CreateClient()` builds (its redirect
and cookie handlers over TestServer's handler).

| Shape | `testDouble` | The server's `traceparent` names | Transport's span | The response names |
|---|---|---|---|---|
| `SocketsHttpHandler` | none | the transport's span | started | the transport's span |
| `Onward` over sockets | `Onward` | **the caller's span** | started | **the caller's span** |
| `SendingThrough` a socket client | `SendingThrough` | **the caller's span** | started | **the caller's span** |
| a stub | `CannedMilk` | the caller's span | none | the caller's span |
| `Onward` over a stub | **`Onward`** | the caller's span | none | the caller's span |
| TestServer | none | the caller's span | none | the caller's span |
| `Onward` over TestServer | **`Onward`** | the caller's span | none | the caller's span |
| `SendingThrough` a factory client | **`SendingThrough`** | the caller's span | none | the caller's span |

Where no transport starts a span (a stub, TestServer), the caller's span is the right one: there is no other.

`query parties` on P1's report reads every forwarded service as `double`, `observed`. That covers the two on a socket
and the two on TestServer, and the stub behind `Onward`, a double under the forwarder's name. The run-end
`service.json` gives each of them `doubleCalls: 1`, and its hashed `behaviour.parties` carries each party's kind
(`src/Kronikol/Reports/ServiceCardWriter.cs:135-147`).

**P3** (`p3-own-spans/results-5.27.2.txt`, `results-5.31.1.txt`) runs the repository's own fact for 4.26.5,
`CallOwnSpanThroughTheHandlerTests.Calls_a_host_makes_at_once_each_open_the_spans_of_their_own_call`, on the released
handler with a forwarder between it and the socket transport. The host makes three calls at once under its request
span:

| Between the handler and the socket | Each call's popup |
|---|---|
| nothing | 2 spans: its transport's and its service's work |
| `Onward` | 6 spans: all three transports' and all three services' work |
| `SendingThrough` | 4 to 6 spans: all three services' work, and two or three transports' |

The counts behind a forwarder vary from run to run, on 5.27.2 and 5.31.1 alike, as other calls' transport spans fall in
or out of a popup (five more runs on each, in the harness README; a call behind `Onward` once opened 5). In every run
each forwarded popup held both other calls' service work, and each direct call its own 2 spans.

---

## 3. The root cause

### 3.1 One walk, two questions

| Release | What the walk decides | Where |
|---|---|---|
| 4.14.3 (`f9c67188`, 2026-10-08, `GRPC_IDENTITY_PROPAGATION_PLAN.md` R3) | Whether the transport writes the `traceparent` itself: `SocketsHttpHandler`, and `HttpClientHandler` on it, run the framework's DiagnosticsHandler; TestServer's in-memory handler does not | `TransportPropagatesTraceContext`, `:403` |
| 4.26.5 (`b5806ae9`, 2026-10-17) | Nothing new, but relies on the first: the response names the span the transport put in that `traceparent`, so each call a host makes keeps a span of its own | `TransportSpan`, `:358-370`; `InternalFlowSegmentBuilder.cs:224-230`, `OwnSpans` `:706-734` |
| 5.16.0 (`15a95d4c`, 2026-10-28, #190 R3) | Whether a double answered: the end's type name when it is no transport the handler knows | `EndOfChain` `:406-412`, `AnsweringTestDouble` `:421-426` |

### 3.2 The walk reads the chain's shape, not where the call goes

A `DelegatingHandler` is one way to hand a call on, and the only one the walk knows. A handler can also hold another
handler and send through an `HttpMessageInvoker` over it (the issue's `Onward`), or hold an `HttpClient` and send
through that (`SendingThrough`). The walk stops at either and reads it as the call's end. Both answers then go wrong
for the one reason:

- **The mark.** "Not a transport I know" becomes "a double answered", and the forwarder is named.
- **The trace context.** "Not a socket transport" makes the handler write the `traceparent` with the caller's span
  (`:175-184`). The transport's DiagnosticsHandler leaves a `traceparent` it finds: in P1 it started its span and the
  server still got the caller's. `TransportSpan` then sees a header that was there before the send (`:288`, `:366`),
  so the response names the caller's span too, which is what P3 counts.

### 3.3 The rule claims what it did not see

R2 of #190 gave the field its contract: "A capturer writes it only when it saw a double answer. An unmarked call is
unknown, never real" (`PARTY_IDENTITY_PLAN.md:727`). The data file's documentation says the same to every reader
(`Generated-Reports.md:427`), and that plan's table of who knows what gives the per-call fact to "the capturer, when it
saw the double answer" (`:378`).

The handler never sees an answer. R3 read "a type I do not know" as "a double answered", which errs in the costly
direction:

- A missing mark leaves a party `unknown`, and the `parties` verb asks for it to be declared.
- A wrong mark makes a party `double`, `observed`, and nothing flags it. In P1 six of the nine parties read so, and
  four of them are a server on a socket or an app on TestServer, which R3's own rule reads as no double.

The gap was known and recorded as by design: "A transport of your own at the end of the chain reads as a double"
(`Tracking-Dependencies.md:110`, the 5.16.0 entry of `CHANGELOG.md`, and the `PARTY_IDENTITY_PLAN` row R3 left in
`ROADMAP.md`'s Appendix C). A forwarder is a wider case than a home-made transport, and nobody had looked at it.

### 3.4 Nobody who knows can say

The mark has one input, the chain (`:259`). The test or the library that wired the chain knows what a walk can never
see: a stub on a socket (WireMock, a container), an app hosted on TestServer as a stub, or a forwarder that finds its
way onward only when the call comes.

The party declarations of 5.14.0 cover a party whose name is known before the run. They cannot reach these cases:

- The issue's library names WireMock's parties from client names at run time (#207: "Orders API (WireMock)").
- A declaration cannot correct a mark: a party declared `real` whose calls are marked keeps the marks and gives
  `PartyDeclarationConflict` (`PARTY_IDENTITY_PLAN.md:755`).

### 3.5 Why the suggested option does not reach the cause

The option supplies the missing input (3.4), and R2 is it. As the fix for 3.2 and 3.3 it falls short:

- **It is opt-in, suite by suite.** A suite learns it has a forwarder only by noticing that a plausible parties table
  is wrong. Every other forwarder in every other suite stays misread.
- **Its empty value is a blunt answer to a misreading.** On `Onward` over a stub it removes a mark that should be there
  (the stub's), where following the call names it.
- **It does not touch the trace context or the spans** (P1's two forwarded socket rows, and P3).

---

## 4. What the fix must do

1. Read where a call goes by following it through every way .NET hands a call on in a chain that the handler can see
   without running it: a delegating handler's inner handler, a handler another holds, an invoker's handler.
2. Mark a call only when every end the walk reaches answers the call itself. An end that is a transport, or that the
   walk cannot see past, means the call may reach the service: no mark, so `unknown`.
3. Leave the `traceparent` to the transport only when every end is a socket transport, and otherwise write it as today.
4. Name, as the call's own span, the span the transport started even when a forwarder sent a copy.
5. Change nothing for a chain that holds no forwarder: the same marks, the same headers, the same cost.
6. Let the code that wires a handler say what answers its calls, for what no walk can see.

---

## 5. The design

### 5.1 R1: follow the call

A new internal `HandlerChain` (`src/Kronikol/Tracking/HandlerChain.cs`) resolves, once per call, where a call handed to
the handler's `InnerHandler` goes. It replaces `EndOfChain`, `TransportPropagatesTraceContext`, `AnsweringTestDouble`
and the transports list, which moves into it. The prototype is `prototype/r1-walk.patch`.

**The fast path.** Walk delegating handlers as today. If the end is a socket transport, another transport, or nothing,
the answer is today's, with nothing allocated (P2: 23 ns against today's 4 to 8 ns).

**Otherwise, follow.** From the start, depth first:

- A delegating handler hands the call to its inner handler.
- An end is classified:
  - **socket transport**: `SocketsHttpHandler`, `HttpClientHandler` and any handler built on it;
  - **other transport**: TestServer's handler, `WinHttpHandler`, and the browser, Apple and Android handlers, named
    rather than referenced as today;
  - **a delegating handler with no inner handler**: read as any other handler (Q4), so looked into, and the answerer
    when it holds no way on;
  - **anything else**, which is looked into.
- A handler that is looked into hands the call on to every handler it holds. "Holds" means an instance field, of its
  type or of a base type below `HttpMessageHandler`, declared as an `HttpMessageHandler` or an `HttpMessageInvoker`
  (`HttpClient` is one), and set. An invoker's handler is read through
  `[UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_handler")]`. No public member exposes it, and the field has had
  this name from .NET Core 2.1 to `main` (A1).
  - Each handler held is a path, so a router that holds two handlers has two ends.
  - A handler it holds that the walk passed already (itself, or one before it) is no way onward, since a call handed
    back to it would never end.
  - A handler that holds no way onward answers the call itself (a stub). It is an end.
- **Limits.** The field list is cached per type. An invoker whose handler cannot be read (a runtime that renamed the
  field) is an end of unknown kind. A walk past 64 handlers stops there with an end of unknown kind.

The walk reads fields only. It runs no getter and sends nothing.

### 5.2 Two answers from one walk

| | Every end answers itself | Any end a transport, or unknown | Every end a socket transport |
|---|---|---|---|
| `testDouble` | the first end's type name | none | none |
| Who writes the `traceparent` | the handler, as today for a stub | the handler | **the transport** |

"First" is the walk's order: the inner handler before the fields, the fields as declared, a type's own fields before
its base's. A router of two stubs is marked with the first stub's name (P2). Any of its names says a double answered,
and a router of stubs is rare enough not to need its own format.

The walk runs once per call, at the top of `SendAsync` after the excluded-host check, and both decisions read it.

### 5.3 The transport's span from the request the transport sent

`TransportSpan` (`:364-370`) reads the `traceparent` the transport added from the request the handler was given. A
forwarder that sends a copy hands the transport another request, so the header lands on the copy: in P2, the copy
carries the transport's span and the original does not.

The response's log now reads it from `response.RequestMessage ?? request`. `SocketsHttpHandler` names the request it
sent there (P2, A2). The trace id is still checked against the call's, so a response that names some other request
gives no span. The send-that-threw path has no response and keeps reading the request.

### 5.4 Shape by shape (P2, `p2-follow/results.txt`)

| Shape | Mark: today | Mark: R1 | `traceparent`: today | `traceparent`: R1 |
|---|---|---|---|---|
| `SocketsHttpHandler`, an `HttpClientHandler` subclass | none | none | transport | transport |
| `Onward` over sockets; over the factory pipeline | `Onward` | **none** | handler | **transport** |
| `SendingThrough` a socket client; a factory-made client | `SendingThrough` | **none** | handler | **transport** |
| a stub; a stub with a responder delegate | its name | its name | handler | handler |
| `Onward` over a stub | `Onward` | **`CannedMilk`** | handler | handler |
| TestServer | none | none | handler | handler |
| `Onward` over TestServer; `SendingThrough` a WAF-style client | the forwarder | **none** | handler | handler |
| Moq's `Mock<HttpMessageHandler>` | `HttpMessageHandlerProxy` | the same | handler | handler |
| MockHttp, with and without a fallback client | `MockHttpMessageHandler` | the same | handler | handler |
| JustEat's interceptor (no inner handler) | none | **`InterceptingHttpMessageHandler`** (Q4) | handler | handler |
| a router of two stubs | `Router` | **`CannedMilk`** | handler | handler |
| a router of a stub and sockets | `Router` | **none** | handler | handler |
| a stub holding a transport it never uses | `Hoarder` | **none** | handler | **transport** |
| a handler that holds itself | `Looping` | the same | handler | handler |
| a lambda forwarder; one through `IHttpClientFactory` | its name | the same (5.5) | handler | handler |

On P1's and P3's shapes the prototype gives (`prototype/results-prototype.txt`):

- no mark on any forwarded transport call, and `CannedMilk` on `Onward` over a stub;
- the server's `traceparent` and the response both name the transport's span on both forwarded socket rows;
- the four forwarded parties read `unknown`, the stub behind `Onward` a `double`;
- each of the host's three calls opens its own 2 spans.

### 5.5 What R1 leaves as it is

- **A handler in the middle of the chain that answers and passes the rest on** (JustEat's interceptor in a pipeline):
  the end is still a transport. This is unchanged and documented.
- **A stub on a socket** (WireMock, a container): a transport reaches it like any service. R2 covers it.
- **A forwarder that finds its way onward only when the call comes**: one that asks an `IHttpClientFactory` or an
  `IServiceProvider`, uses a static, or sends through a delegate. It still reads as the double that answered (P2's last
  row). Following delegates was not taken (5.8 d). R2's empty value covers it, and the wiki names it.
- **A handler that keeps its ways onward in an array or a collection** (a router keyed by host): only fields declared
  as a handler or an invoker are followed, so it reads as the double that answered, as today (5.8 k).
- **A stub that holds a transport it never uses** reads as handing calls to it: no mark, and the `traceparent` is left
  to a transport that never runs. The stub ignores the header, so the only loss is the mark, in the safe direction.
- **MockHttp with a fallback client** stays marked: the fallback client is held by an object of MockHttp's own (its
  fallback request), not by a field of the handler, so the walk does not reach it. That is today's reading.
- **TestServer reads as the service answering in process**, though an app hosted there can be a stub (the issue's
  second case). Its calls are unmarked, so `unknown`. R2 lets the library say so.

### 5.6 R2: say what answers

```csharp
/// <summary>
/// What answers this handler's calls in their service's place, written on each call as <c>testDouble</c> (#219).
/// <c>null</c>, the default: the handler reads it from its chain, a handler at the end of it that answers the call
/// itself. A name: every call the handler tracks is marked as that double's answer, whatever its chain ends in, for a
/// double the chain cannot show, such as <c>"WireMock"</c> on a socket. Empty or white space: no call is marked, for a
/// chain whose end the handler reads as answering when it hands the call on.
/// </summary>
public string? TestDouble { get; set; }
```

- **Semantics.** `null` uses R1's walk. Blank marks nothing, as a blank mark already marks nothing in a capture
  (`src/Kronikol/Ingestion/InteractionRecord.cs:359`). Any other value is written as given, on both halves and on a
  send that threw, where the walk's mark goes today (`:283`, `:323`, `:352`).
- **It says nothing about the trace context**, which stays R1's.
- **It reaches every handler made from the options.** That covers `new TestTrackingMessageHandler(options)`,
  `TrackDependenciesForDiagrams` (every client of the host, so coarse by design; a value per client or per request
  is Q3), `CreateTestTrackingClient` and the legacy factory.
  - The options are records, so the issue's `options with { FixedNameForReceivingService = receiver }` carries it.
  - On the test client, whose calls reach the service under test, the service stays `underTest`: the observed subject
    outranks the marks (`src/Kronikol/Parties/PartyResolver.cs:85-88`), and the verb counts the marked calls.
- **A party every call to which a declared mark covers reads `double`, `observed`**, as any marked party does (Q5).

### 5.7 What does not change

- **The `testDouble` member**, its four forms, the resolver, the `parties` verb, the card's shape and the OTLP
  attribute. Only which calls carry the mark changes.
- **The bytes of every report from a suite with no forwarder.** No example and no fixture has one (READ: `examples/`
  holds no `HttpMessageHandler` subclass and no invoker; the tests' stubs hold no handler), so no golden pin moves.
  Kronikol builds trackers in three places, none ending in a forwarder:
  - `src/Kronikol/TestTrackingHttpClientFactory.cs:15`;
  - `src/Kronikol/Tracking/TrackingHttpMessageHandlerBuilderFilter.cs:23`;
  - `src/Kronikol/WebApplicationFactoryExtensions.cs:32-33`.
- **History's call lines.** `InteractionShape` reads no `testDouble`, so no fingerprint moves and its rule stays
  (`tests/Kronikol.Tests/History/HistoryGuard.cs`).
- **The other trackers.** Only this handler walks a chain or decides a `traceparent` from one (READ: grep of `src/` for
  `is SocketsHttpHandler`, `is HttpClientHandler` and `traceparent`).
- **Kronikol4J.** The port writes no `testDouble`, and its OkHttp interceptor writes a `traceparent` by its
  `injectTraceparent` option, not by its chain (`kronikol4j-http/.../KronikolOkHttpInterceptor.java:89`). Each release
  gets a "Not mirrored" line in `docs/REMAINING_PARITY.md`, as 5.16.0 did (`:2526`).

### 5.8 Designs not taken

a. **The issue's option alone.** See 3.5.

b. **Watching the transport**: listening to `System.Net.Http`'s activities, and calling a call carried when the
   transport starts its span inside the send. Two reasons rule it out:
   - TestServer and the platform transports start no span, so an in-memory transport would read as a stub.
   - The trace context has to be decided before the send, and the span starts inside it.

   The plan first gave two more, and #138's plan (`INTERNAL_FLOW_SOURCES_PLAN.md`, a draft on 2026-11-07) measured both
   false. On .NET 8 to 11 DiagnosticsHandler writes the events Application Insights reads whether `System.Net.Http` has
   a listener or not. A listener that samples that source only under a parent starts no span outside a trace.

c. **Reading `response.RequestMessage` as evidence of a transport.** Any handler can set it, a stub included, so it says
   nothing about who answered. R1 reads it for one thing only: which request carried the transport's header.

d. **Following delegates**, so that a lambda forwarder is followed. A stub's responder often closes over the test class,
   and the test class holds the test's own client of the service. The stub would then read as a forwarder to the
   service under test and lose its mark. That trade costs R3's main catch for a rarer shape.

e. **A public marker for forwarders** (an interface, or a base class Kronikol ships). Integrators would have to change
   their code, and only those who know of it would. The walk covers both of the issue's shapes as written.

f. **A tracker that does not own its inner handler**, so the library can chain it straight over the pipeline's
   handler. This removes the first forwarder for this one library. #157's per-request receiving name removes the reason
   to make one tracker per name at all, and belongs to #157.

g. **Fixing it in #157, #207 and #208.** A forwarder is a shape any suite can write, and the walk must read it right
   whatever those issues become.

h. **Declaring the parties in place of R2.** This needs names before the run and cannot undo a mark (3.4).

i. **Spotting WireMock by port, host or headers.** "A port proves nothing" (`PARTY_IDENTITY_PLAN.md:798`), and a stub's
   answer carries nothing that says so.

j. **Caching the walk per handler.** A chain of delegating handlers cannot change after its first send, but a
   forwarder's fields can. A walk through one costs a few hundred nanoseconds (P2), so it runs per call.

k. **Following arrays and collections of handlers.** An array could be read without running code, but a collection is
   read only by running its code, and the walk runs none (5.1). Neither of the issue's shapes needs it, and R2's empty
   value covers a router that reaches a transport. It can follow when a suite shows one.

---

## 6. Releases

Neither release names its number here. Other sessions shipped 5.27.3, 5.27.4 and 5.27.5 while this plan was written,
so each release takes the next free patch or minor when it is pushed, claimed first by a message to the other sessions.

### 6.1 R1, a patch: follow the call

**Why a patch.** It fixes shipped behaviour and adds nothing to call. The behaviour change goes in the changelog, as
the repository's rule asks.

**Files:**

- `src/Kronikol/Tracking/HandlerChain.cs` (new, internal);
- `src/Kronikol/Tracking/TestTrackingMessageHandler.cs`: the two answers from one walk, and `TransportSpan` reading
  `response.RequestMessage`. The private doc comments of the removed methods go with them.

**Facts, each red on today's code before its own code is written** (P1 and P3 show the red behaviour on 5.27.2
and 5.31.1).
Classes that read the request log join `[Collection("DiagramsFetcher")]`, as `HandlerTestDoubleTests` does. Servers
bind free loopback ports only.

`HandlerTestDoubleTests`:

1. `A_call_a_forwarder_hands_to_a_server_on_this_machine_is_not_marked`, a theory over `Onward` and `SendingThrough`.
   Red: marked with the forwarder's name.
2. `A_call_a_forwarder_hands_to_TestServer_is_not_marked`, over `Onward` on `CreateHandler()` and `SendingThrough` a
   factory-style client. Red.
3. `A_call_a_forwarder_hands_to_a_stub_is_marked_with_the_stubs_type`, on both halves. Red: `Onward`.
4. `A_forwarder_over_an_IHttpClientFactory_pipeline_is_followed_through_its_delegating_handlers`, the issue's first case
   built as its library builds it: `AddHttpClient` and a delegating handler whose tracker gets `Onward(InnerHandler)`.
   Red.
5. `A_call_with_two_ways_on_is_marked_only_when_both_answer_and_then_by_the_first`: a router of two stubs gives the
   first's name; a stub and sockets give none. Red: `Router`.
6. `A_handler_that_holds_only_itself_answers_the_call`. Green today; it guards the passed-handler rule.
7. `A_stub_whose_answer_is_a_delegate_is_still_marked`. Green today; it guards 5.8 d.
8. `A_stub_that_holds_a_transport_is_read_as_handing_calls_to_it`. Red: `Hoarder`. It pins 5.5's rule.
9. `A_chain_longer_than_the_walk_follows_marks_nothing`: 65 forwarders over a stub. Red.
10. `A_service_reached_only_through_a_forwarder_to_a_real_server_is_not_a_double`: the resolver over the logs reads it
    as no double (with that one call in the run, its inference reads it as the subject). Red: `double`.
19. `A_delegating_handler_with_no_inner_handler_answers_the_call` (Q4). Red: no mark.

`HandlerChainTests` (new):

11. `The_handler_an_invoker_sends_through_is_read_where_the_walk_reads_it`, for an `HttpMessageInvoker` and an
    `HttpClient`. Green. It fails on the first runtime that renames `_handler` (A1).
12. `An_invoker_whose_handler_cannot_be_read_is_an_end_of_unknown_kind`, through an internal overload of
    `HandlerChain.Of` that takes the reader: no mark, and the handler writes the `traceparent`.

`TestTrackingMessageHandlerTests`:

13. `Traceparent_is_left_to_the_framework_when_a_forwarder_hands_the_call_to_SocketsHttpHandler`, over `Onward` and
    over `SendingThrough` an `HttpClient` on `SocketsHttpHandler`. Red: the caller's span is written.
14. `Traceparent_carries_the_current_span_when_a_forwarder_hands_the_call_to_TestServer`. Green; a guard.
15. `Traceparent_carries_the_current_span_when_one_way_on_is_in_memory`: a router of sockets and TestServer. Green; it
    guards "every end" against "any end".

`CallOwnSpanThroughTheHandlerTests`:

16. `A_call_a_forwarder_hands_to_a_socket_names_the_span_its_transport_started`, over `Onward` and `SendingThrough`.
    Red: the response names the host's span.
17. `Calls_a_host_makes_at_once_through_a_forwarder_each_open_the_spans_of_their_own_call`, P3's shape. Red: each
    holds the other calls' service work too. It names the spans a popup holds, as the red counts vary (2).

**Playwright** (CLAUDE.md: what a reader sees). `ArrowLinkOpensPopupTests`:

18. `Each_call_a_host_makes_at_once_through_a_forwarder_opens_its_own_flow`, the 4.26.5 fact
    `Each_call_a_host_makes_at_once_opens_its_own_flow_and_not_the_scenarios` (`:134-170`) on a report the whole
    pipeline wrote from real calls.
    - The calls go through the real handler and a forwarder to a socket server, as `CallOwnSpanThroughTheHandlerTests`
      makes them, from a new `ReportTestHelper` method.
    - Each arrow is clicked by a dispatched event and its popup is read: its own service only, and its transport's span.
      No count is pinned: a call that opens a connection also has the transport's `HTTP wait_for_connection` span,
      which Kronikol's listener hears from `Experimental.System.Net.Http.Connections`.
    - Red on 5.27.2 and 5.31.1: each popup holds all three services' work.
    - It keeps the suite's rules: `PollingInterval = 200`, `.First` on shared selectors, no forced click, no network
      mock.

**Mutations**, each caught by a fact above:

- the walk stops at the first non-delegating handler (today's);
- an invoker is not looked through;
- only the first handler held is followed;
- "any end answers" in place of "every end";
- "any end a socket" in place of "every end";
- `response.RequestMessage` not read;
- the passed-handler check dropped;
- the 64 cap dropped;
- the fast path answering "socket" for any end;
- an unreadable invoker read as a stub;
- a delegating handler with no inner handler read as an end of unknown kind (Q4).

Run the full core and E2E suites, as each release does.

**Docs** (wiki at `f73bdd6`):

- `Tracking-Dependencies.md:98-111`. The handler follows a call past a handler that hands it on: one that holds another
  handler, an `HttpMessageInvoker` or an `HttpClient`. Say what it cannot follow (a factory, a service provider, a
  static, a delegate, a collection), what reads as what, and that before R1 every call through such a handler was
  marked with its name.
- `Internal-Flow-Tracking.md:15`. A call handed to a socket through a forwarder gets the transport's span too.
- `Service-Card.md:160-163`. Add R1 to the upgrades that move the hash once: a party reached through a forwarder
  changes kind.
- `CHANGELOG.md`, with a behaviour change paragraph. Calls through a forwarder to a transport lose their mark, and one to
  a stub takes the stub's name. Parties read `double` through them may read `unknown`. `doubleCalls` and the card's
  `behaviourHash` move once. A forwarded socket call made under a span sends the transport's span, as a direct one
  always did.
- Kronikol4J `docs/REMAINING_PARITY.md`, Not mirrored (5.7).
- `ROADMAP.md`, Appendix C's `PARTY_IDENTITY_PLAN` row: forwarders are followed; a home-made transport is left for R2.

### 6.2 R2, a minor: say what answers

**Why a minor.** A new public option, even though its default keeps R1's behaviour (CLAUDE.md).

**Files:**

- `src/Kronikol/Tracking/TestTrackingMessageHandlerOptions.cs`: the option and its doc comment, which ships in the
  package;
- `src/Kronikol/Tracking/TestTrackingMessageHandler.cs`: the declared mark beats the walk's.

**Facts, red first.** The first ones do not compile, then fail on behaviour once the member exists unread.

1. `A_declared_double_marks_every_call_whatever_the_chain_ends_in`, over a socket server and over `Onward` on one, on
   both halves.
2. `A_declared_double_marks_a_send_that_threw`.
3. `A_declared_double_wins_over_a_stub_at_the_end_of_the_chain`.
4. `An_empty_or_blank_declaration_marks_nothing_even_over_a_stub`, a theory over `""` and `"  "`.
5. `No_declaration_reads_the_mark_from_the_chain`. A guard: R1's facts cover the walk.
6. `A_service_only_a_declared_double_answered_is_a_double_observed`, through the resolver.
7. `TrackDependenciesForDiagrams_hands_the_declaration_to_every_client_it_tracks`, through `AddHttpClient`.
8. `A_declaration_survives_with_on_the_options`, as the issue's library copies them, on a framework adapter's options
   record.

**Mutations:**

- the option ignored;
- empty read as a name;
- white space read as a name;
- request half only;
- the walk's mark beating a declared one;
- the thrown send unmarked.

**Docs:**

- `HTTP-Tracking-Setup.md:40-54`: a `TestDouble` row.
- `Tracking-Dependencies.md:98-111`: the option, with the WireMock case and the empty case. The sentence "A transport of
  your own at the end of the chain reads as a double" gains "unless the handler says otherwise".
- `Generated-Reports.md:427` and `Querying-Reports.md:567-570`: the handler's option as a source of the mark.
- `AI-Integration-Prompt.md`: one clause, for a handler whose calls reach a stub on a socket (#190 Q14 made the prompt
  ask integrators to declare doubles).
- `CHANGELOG.md`; Kronikol4J, Not mirrored; `ROADMAP.md`, the Appendix C row's last part closed.
- #219 closed with a comment (Q7).

---

## 7. What the issue's library does after each release

- **After R1: nothing.**
  - `Onward` and `SendingThrough` stay as written, and their calls lose the forwarders' names.
  - A tracker whose pipeline ends in a stub is marked with the stub's name.
  - Calls to WireMock and to a stub app hosted on TestServer read `unknown`.
- **After R2: one line where it knows.** The trackers it makes for its stub addresses (#207's list) take
  `options with { FixedNameForReceivingService = receiver, TestDouble = "WireMock" }`. A tracked client over a stub
  host's own client takes the stub's name.
- **Later, if #157 lands** (a per-request receiving name): the library can drop `Onward` and its one tracker per name.

---

## 8. Related issues and plans

- **#157, #207, #208**, all open: why the library forwards (1). Each stands on its own; this plan changes none of their
  ground. #208's "track an existing `HttpClient`" helper, if built, would rest on R1's invoker reading to decide its
  `traceparent`.
- **`PARTY_IDENTITY_PLAN.md`** (#190): §5.10 (the field's contract), §5.11 (R3, the mark this plan corrects), its R3
  log (`:1296-1304`) and the row it left in `ROADMAP.md`'s Appendix C.
- **`GRPC_IDENTITY_PROPAGATION_PLAN.md` R3** (4.14.3): the `traceparent` decision.
- **4.26.5** (`b5806ae9`): each call's own span, which P3 shows a forwarder undoes.

---

## 9. Questions for the owner

The owner green-lit the plan on 2026-11-07 ("can you implement the plan in full"), so each question below takes its
recommended answer.

1. **Two releases or one?** R1 as a patch, then R2 as a minor, or both in one minor. **Recommended: two.** R1 fixes
   every suite with a forwarder without a change to its code, and by the repository's rule is a patch.

2. **The option's shape.**
   - a. `string? TestDouble`: `null` reads the chain, a name marks every call, blank marks none. This is the issue's
     shape.
   - b. Two members: a name, and a `bool` that turns the walk off.
   - c. A resolver, `Func<HttpRequestMessage, string?>`, alone.

   **Recommended: a.** One member, named as the field it writes, and blank already means "no mark" in a capture.

3. **A finer value as well?** Either a resolver per request (`Func<HttpRequestMessage, string?>`, the issue's
   alternative), or a map per client name beside `ClientNamesToServiceNames` for `TrackDependenciesForDiagrams`, whose
   one options object makes every client's handler. **Recommended: not now.** The issue's library makes one tracker per
   receiving name, so a per-handler value covers it. A finer value belongs beside #157's per-request receiving name, if
   that is built; meanwhile a host whose clients reach different doubles can declare those parties by name.

4. **Does a delegating handler with no inner handler answer the call?** It is the end of its chain and can only answer
   itself: its base `SendAsync` throws. Examples are JustEat's interceptor from `CreateHttpMessageHandler()` used on
   its own (P2: today's walk ends in nothing there and marks nothing) and a stub written as a `DelegatingHandler`.
   Mark it with its type's name in R1? **Recommended: yes**, as the same reading of where a call ends. It adds marks
   such suites never had (their parties become `double`, `observed`), which is why it is asked rather than done.

5. **A declared mark's `kindSource`.** A party whose calls carry R2's mark reads `double`, `observed`, as any marked
   party does. **Recommended: keep it so.** Telling a declared mark from a seen one would put a new member on every
   call in the capture format, and the parties file remains the place to declare a party's kind.

6. **Placement.** R1 is a defect in what shipped (`ROADMAP.md`'s rule 1). **Recommended: R1 now, R2 straight after
   it.** No roadmap row; this plan's `PLANS_STATUS.md` row is its record, as for other single-issue plans. Each release
   updates the Appendix C row.

7. **Closing #219.** **Recommended: after R2**, with a comment naming both releases and what the library can drop. If
   R2 is declined, close after R1.

---

## 10. Assumption ledger

| # | Assumption | Basis | If wrong |
|---|---|---|---|
| A1 | `HttpMessageInvoker` keeps its handler in a field named `_handler` | SOURCE: dotnet/runtime `v8.0.0`, `v9.0.0`, `v10.0.0` and `main` (`HttpMessageInvoker.cs:14`, read again 2026-11-07), corefx `release/2.1` (`:16`); RUN: P2 reads it on 10.0.11 | The accessor throws `MissingFieldException`, caught: the invoker is an end of unknown kind, so no mark and the handler writes the `traceparent`, today's reading of a forwarder. Fact 11 fails on the first CI run of that runtime |
| A2 | A socket transport names the request it sent in `response.RequestMessage` | RUN: P2 (the copy, carrying the transport's span) | A copy forwarder's response names the caller's span, as today; nothing else changes |
| A3 | DiagnosticsHandler leaves a `traceparent` the request carries | RUN: P1 (the transport started a span; the server got the caller's) | The trace context defect would not exist, and 5.2's `traceparent` column would be moot |
| A4 | A handler that holds a handler or an invoker hands calls to it | DESIGN, a heuristic; P2 shows where it is wrong (a stub holding a transport it never uses) | That stub loses its mark, the safe direction. A forwarder holding its way onward in any other form keeps today's reading |
| A5 | The walk costs too little to matter | RUN: P2, 23 ns on a chain that ends in a transport and 320 to 615 ns through a forwarder or to a stub, timed while other sessions built | It could be cached per handler for delegating chains, which cannot change after the first send |
| A6 | The issue's shapes are as P1 builds them | ISSUE: `Onward` verbatim; `SendingThrough` from "sends a copy of each request through that client" | A copy without the headers changes nothing: the transport writes its `traceparent` on the copy either way |
| A7 | No Kronikol-made chain, example or fixture ends in a forwarder | READ (5.7), at `54e8ee82` and again at `896ddad3` | A golden pin moves in R1's full run, and is then explained there |
| A8 | Nothing else reads the walk | READ: `EndOfChain` and its two callers are private | none |

---

## 11. Log

- 2026-10-29, kronikol-07, at the owner's request for "a detailed, well researched plan to solve the root cause of issue
  219 (which may be different from the proposed solution)", without interfering with other sessions:
  - Worked in a worktree of its own (`C:/Code/Kronikol-219`, branch `plan/219-answering-double`, from `753f17f9`). It
    fetched Kronikol4J and BreakfastProvider into private refs only, and read the wiki at `origin/master` without
    pulling it.
  - Checked that no session had #219: `ListAgents`, and a grep of every worktree's `plans/`.
  - Read #219 and its siblings #157, #207 and #208; the handler and its history (4.14.3, 4.26.5, 5.16.0); every reader
    of the mark (the resolver, the call graph, the `parties` verb, the card, the OTLP mapper); the #190 plan's sections
    and log; and the wiki's pages on the mark, the options, internal flow and the card.
  - P1 reproduced the issue on 5.27.2 and found the trace context and span defect beside it. P3 measured that defect
    with the repository's own 4.26.5 fact. P2 tried the walk over 22 shapes, among them three mock libraries from
    nuget.org.
  - The prototype (`prototype/`) turned every red row of P1 and P3 green. The 297 existing facts nearest the change
    passed on it. It was then taken out of `src/`, the worktree's build output deleted, and the patch kept.
  - BreakfastProvider's `main` (`640c190`, on 5.14.0) holds no forwarder (READ): R1 leaves its marks alone when it
    moves past 5.16.0.
  - Other sessions released 5.27.3, 5.27.4 and 5.27.5 meanwhile (to `54e8ee82`), and the plan's branch moved onto
    each. They changed no file cited here but `CHANGELOG.md`, now cited by entry rather than by line, and rows of
    `ROADMAP.md` and `PLANS_STATUS.md` that the plan does not cite. Its releases name no number until they are pushed
    (section 6).
- 2026-11-07, kronikol-07, at the owner's request to make sure the plan is "fully complete, accurate", with any deep
  dive that might change it:
  - Moved the branch onto `896ddad3` (5.31.1), 41 commits on. Of the code the plan cites only the handler changed:
    5.30.2, 5.31.0 and 5.31.1 changed how it reads the step type, and 5.30.2's three fewer fields put every handler
    citation three lines higher. `OwnSpans` ends at `:734`, where the plan said `:731`. On the wiki, now `f73bdd6`,
    only `Service-Card.md`'s list item grew, to `:160-163`; on Kronikol4J, now `07bdba4`, both citations hold.
  - Ran P1 and P3 on the published 5.31.1. P1 answers every row as on 5.27.2. P3's counts vary from run to run on both
    releases (section 2), which the one run of 2026-10-29 did not show, so fact 17 asserts services, not a count.
  - Read again: `_handler` on dotnet/runtime `main` (A1); the core package targets net10.0, where `UnsafeAccessor`
    exists; `r1-walk.patch` applies to `896ddad3`; every reader of the mark, among them the overview's and the call
    graph's counts, and none of history's call lines (5.7); no example, fixture or Kronikol-made chain with a
    forwarder (A7); no code that copies the options member by member, so R2's option reaches every record copy.
    #219, #157, #207 and #208 are open, with no comment since.
  - Added 5.5's collection limit, 5.8 k and 5.7's history line.
- 2026-11-07, kronikol-07, at the owner's word "can you implement the plan in full", each question of section 9 taking
  its recommended answer, in the same worktree, with kronikol-13 (#213) and kronikol-70 (#138) told of every heavy run:
  - R1 released as 5.31.2. Each fact was red on 5.31.1 first, but the two guards (6 and 7) and facts 11, 12, 14 and
    15, which pass on it as the plan says. `HandlerChain` is the prototype's, with Q4 (a delegating handler with no inner
    handler is read as any other handler, so it answers the call unless it holds a way on) and a cap on its fast path,
    so a loop of delegating handlers cannot hang it.
  - Three facts were corrected against the code. Fact 10 asserts no double: with that one call in the run, the
    resolver's inference reads the service as the subject, not `unknown`. Fact 17 names the spans a popup holds, not a
    count. Fact 18 pins no count either: a call that opens a connection also has the transport's
    `HTTP wait_for_connection` span, which Kronikol's listener hears from `Experimental.System.Net.Http.Connections`.
  - Eleven mutations, the plan's ten and Q4's, are each caught (`WHAT_ANSWERS_PLAN.harness/r1/mutations.txt`). The core
    suite and the end-to-end suite passed on the release.
  - 5.8 b now rests on its two reasons that stand: #138's plan measured the other two false (kronikol-70).
