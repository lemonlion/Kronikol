# WHAT_ANSWERS_PLAN harness (#219)

The probes behind `plans/WHAT_ANSWERS_PLAN.md`, run on 2026-10-29 on Windows 11 with the .NET 10.0.300 SDK (runtime
10.0.11), against Kronikol 5.27.2 from nuget.org unless a file says otherwise. Each probe is a file-based app
(`dotnet run --file`), binds only free loopback ports (port 0), and writes its reports to a directory it is given, never
into the repository. `tidy.py` replaces that directory's path with `<scratch>` in a results file.

| Folder | What it shows | Results |
|---|---|---|
| `p1-forwarders/` | The issue's two forwarders (`Onward`, verbatim from #219, and `SendingThrough`, built from its description) and the same shapes over a stub and over TestServer, each tracked by the released handler: what it writes as `testDouble`, the `traceparent` the server got, whether the transport started a span, and the span the call's response names. `run.sh` then runs `query parties` on the report, with the `query.cs` the run wrote beside it. | `results-5.27.2.txt` |
| `p2-follow/` | The walk section 5.1 proposes, as a copy outside Kronikol, beside today's walk, over 22 shapes: the issue's, plain transports, stubs, Moq, RichardSzalay.MockHttp with and without a fallback client, JustEat's interceptor, routers, a handler that holds itself, a lambda forwarder and one through `IHttpClientFactory`. Then the forwarded socket calls sent with no `traceparent` written first, to see what the transport writes and which request the response names. Then what each walk costs per call. | `results.txt` |
| `p3-own-spans/` | `CallOwnSpanThroughTheHandlerTests.Calls_a_host_makes_at_once_each_open_the_spans_of_their_own_call` (4.26.5) on the released handler, with each forwarder between it and the socket transport: how many spans each call's internal-flow popup holds. | `results-5.27.2.txt` |
| `prototype/` | `r1-walk.patch`: a prototype of section 5.1 on `753f17f9` (`HandlerChain`, and the handler reading its two answers from it and the transport's span from `response.RequestMessage`). Research, not R1: it was written before any fact, which R1 may not do. `run.sh` runs P1 and P3 against a tree with the patch applied; `facts-prototype.txt` is the run of the 297 existing facts nearest the change on that tree. | `results-prototype.txt`, `facts-prototype.txt` |

To run them again:

```bash
p1-forwarders/run.sh <reports dir> > p1-forwarders/results-5.27.2.txt
(cd p2-follow && dotnet run --file follow.cs > results.txt)
(cd p3-own-spans && dotnet run --file own-spans.cs > results-5.27.2.txt)
# the prototype: in a worktree of 753f17f9 with prototype/r1-walk.patch applied
prototype/run.sh <that worktree> <scratch dir> > prototype/results-prototype.txt
python tidy.py <results file> <scratch dir>
```

The timings in `p2-follow/results.txt` were taken while other sessions ran builds on the same machine, so read them
for their order of magnitude only.
