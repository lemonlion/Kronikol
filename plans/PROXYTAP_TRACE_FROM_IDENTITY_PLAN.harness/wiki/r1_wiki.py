"""R1 (4.19.0) wiki edits for #170. Run from a checkout of the wiki at origin/master: python r1_wiki.py <wiki dir>.

Each edit replaces one anchor that must occur exactly once.
"""
import pathlib
import sys

wiki = pathlib.Path(sys.argv[1])
PAGE = 'Integration-ProxyTap-Extension.md'


def sub(old, new):
    p = wiki / PAGE
    s = p.read_text(encoding='utf-8')
    assert s.count(old) == 1, (old[:80], s.count(old))
    p.write_text(s.replace(old, new), encoding='utf-8', newline='\n')


sub("| `IdentityResolver` | `null` | Custom `(headers, traceparent) → (name, id)?` override. |\n",
    "| `IdentityResolver` | `null` | Custom `(headers, traceparent) → (name, id)?` override. |\n"
    "| `TraceIdFromIdentity` | `false` | From 4.19.0. Forward a request that brings no `traceparent` under a trace whose "
    "id is the test id, when the tap resolves the identity and the id is a W3C trace id as written; see "
    "[A caller that cannot send a trace](#a-caller-that-cannot-send-a-trace). |\n")
sub("Spans on `ProxyTap.ActivitySource`; mint a `traceparent` when the caller sent none. |",
    "Spans on `ProxyTap.ActivitySource`; mint a `traceparent` when the caller sent none (its trace is random, or the "
    "test id with `TraceIdFromIdentity`). |")
sub("## In-flight identity: lending a database tap an identity\n",
    """## A caller that cannot send a trace

A browser test mints the trace its requests carry with the test id as the trace id, so every hop behind the tap, and
every capturer that attributes by trace id (another tap's `IdentityFromTraceparent`, the OTLP tap's
`AttributeByTraceId`), files the work under the test. A caller that cannot send a `traceparent` or the
`test-tracking-*` headers, such as a mobile app, has no trace to carry. The host knows whose call it is, so it names
the test through `IdentityResolver`, and from 4.19.0 `TraceIdFromIdentity` makes the tap forward the call under the
test's trace:

```csharp
(string Name, string Id)? runningTest = null; // your suite sets it as each test starts, with Id = Guid.NewGuid().ToString("N")

await using var tap = new ProxyTap(new ProxyTapOptions
{
    ListenPort = 5100,
    ForwardBaseUri = new Uri("http://localhost:5000"),
    CallerName = "App",
    ServiceName = "Api",
    IdentityResolver = (_, _) => runningTest,
    TraceIdFromIdentity = true,
});
```

- The test id must be a W3C trace id as written: 32 lower-case hex digits, not all zero, such as
  `Guid.NewGuid().ToString("N")`. The readers behind the tap turn a trace id back into a test id by its text, so an id
  written any other way (a hyphenated GUID, upper case, xUnit v3's or NUnit's ids) keeps a random trace. The tap counts
  those requests in `Diagnostics()` as `OptionNotApplied` and logs the first.
- A request that brings its own `traceparent` keeps it. An unattributed request (`CaptureUnattributedRequests`) and an
  ignored one (`test-tracking-ignore`) keep a random trace.
- With a listener on `Kronikol.ProxyTap`, each call's server span is a root of the test's trace. Without one, the
  forwarded `traceparent` names the test's trace and a parent span of its own, as a browser test's does. With
  `SynthesizeTraceparent` off nothing is sent, and the tap's records still carry the test's trace.
- The trace goes out sampled, so a parent-based sampler records it whatever its ratio, as it records a browser test's.
- Every call of the test then shares one trace. A call's internal-flow popup still shows only its own spans when the
  calls overlap in time (from 4.18.3).

## In-flight identity: lending a database tap an identity
""")
print('r1 wiki edits applied')
