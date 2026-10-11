# Proxy tap trace context plan: a request the host's resolver attributes joins the test's trace

**Written:** 2026-10-11, at 4.14.6 (`d59843fa`). **Status: asked for by the owner on 2026-10-11 and built the same day as
R1, a minor, 4.15.0 if nothing lands before it, on branch `feature/resolver-trace-parent` for review. Not merged, tagged
or published.** A small change, so a short plan: the problem, the design and why, the facts and their proofs, and the
records a release needs.

Evidence levels: **RUN** measured here, with the harness; **READ** read in the source at `d59843fa` (`file:line`);
**DOC** the wiki at `8a57576`; **INFERRED** reasoned, not measured.

Harness: `PROXY_TAP_TRACE_CONTEXT_PLAN.harness/`. `red.sh` (with `failures.py`) runs the new facts on the previous
release with compile stubs, and `red-v4.14.6.txt` is its output. `mutate.py` runs the mutations of §4 in a scratch
worktree, and `mutations.jsonl` holds the results. `wiki/Integration-ProxyTap-Extension.patch` is the wiki edit, applied
after publication.

## 0. Summary

| | |
|---|---|
| What ships | `ProxyTapOptions.ResolvedIdentityTraceparent`: for a request `IdentityResolver` attributed that carries no `traceparent`, the host names the test's trace context, and the tap continues it as though the client had sent it. Two counters, `ProxyTap.IdentityResolverFailures` and `ProxyTap.ResolvedIdentityTraceparentFailures`, each with a `Diagnostics()` entry |
| Found and fixed (§2) | F1, a resolver that throws answered the request `502`; F2, `AddProxyTapTestTracking(IEnumerable<ProxyTapOptions>)` dropped `InFlightRegistry`; F3, the wiki's in-flight sample calls a method that does not exist |
| What changes for existing code | Nothing unless the option is set, except F1 (a throwing resolver now falls back to the built-in rules, and the request is forwarded) and F2 (a list-registered tap now publishes to its registry) |
| Bump | **Minor**: new public surface (an option, two counters). 4.14.6 to 4.15.0 |
| Wiki | Integration-ProxyTap-Extension: a section, three table rows, two steps of "How It Works", and F3 |
| Open | Q1 to Q3 (§6) |

## 1. Where it stands at 4.14.6

- **Attribution** (READ `ProxyTap.cs:322-353`): `IdentityResolver` first, then the `test-tracking-*` headers and their
  fallbacks, then the inbound `traceparent`'s trace id. `ReinjectCorrelation` stamps the four headers from whichever
  identity won (`:273`, `:355-365`), so a hop below that reads Kronikol's headers attributes correctly.
- **The trace** (READ `:207-254`): the server span is parented on the inbound `traceparent` or on nothing (`:212-214`),
  so for a request with none its trace id is random; the client span is its child and names the forwarded `traceparent`
  (`:225`, `:233`). With no listener, the tap forwards the inbound `traceparent` verbatim, or with
  `SynthesizeTraceparent` a random one (`:243-248`), or none.
- **So** a request the resolver attributes, from a client that cannot stamp headers (a native app on a device or an
  emulator: all its traffic belongs to whichever test holds the device), is forwarded in a trace no test owns. Services
  below the tap usually propagate only W3C `traceparent`, so every hop below lands there too, and a consumer that keys
  tests by trace id (test id equal to the W3C trace id, one root span per test) shows those hops as traffic outside any
  test. RUN: on 4.14.6 the new facts' forwarded `traceparent` names a random trace, not the one the host would name
  (`red-v4.14.6.txt`, `Without_a_listener_...` and `A_traceparent_the_tap_cannot_read_counts_as_none`).
- **Capture health** (READ `:85-102`): counters, `Diagnostics()` (one `CaptureDegraded` entry per non-zero problem
  counter) and the `Log` callback. ProxyTap has no `OnCaptureDegraded`; only `TcpTap` does (`TcpTapOptions.cs:143`).

## 2. Found on the way

| # | Finding | Level | Handled |
|---|---|---|---|
| F1 | A resolver that throws fails the request: `ResolveIdentity` calls it unguarded (`ProxyTap.cs:324-326`), so `GuardedHandleAsync` counts a forward failure and answers `502 Bad Gateway` (`:161-166`), and `Diagnostics()` blames the upstream. `TcpTap` catches its resolver and falls back (`TcpTap.cs:661-673`) | READ, RUN (`An_IdentityResolver_that_throws_...`: BadGateway on 4.14.6) | R1: caught, logged, counted in `IdentityResolverFailures`, reported, and the built-in rules attribute the request. A behaviour change, called out in the changelog |
| F2 | `AddProxyTapTestTracking(IEnumerable<ProxyTapOptions>)` copies each options object member by member and leaves out `InFlightRegistry` (`ProxyTapServiceCollectionExtensions.cs:42-75`), so a tap registered that way publishes nothing, from 3.0.45, when the registry arrived. No fact covered a tap publishing to its registry at all | READ, RUN (`A_tap_publishes_...(registeredFromAList: True)` red on 4.14.6) | R1: copied, a reflection guard that every option survives the copy (it also caught the new option before it was added there), and a fact that a tap publishes each request it forwards, direct and list-registered |
| F3 | The wiki's in-flight sample calls `services.AddProxyTap(...)`; the method is `AddProxyTapTestTracking` | DOC `Integration-ProxyTap-Extension.md:128` | The wiki patch |
| F4 | `TraceParent.TryParse` reads more than W3C allows: any version (`ff` too), extra fields, upper-case hex (lower-cased), an all-zero parent span id. With a listener, such an inbound `traceparent` with an all-zero parent id attributes the request to its trace id while the server span starts as a root in a random trace, since `ActivityContext.TryParse` refuses it | READ `ProxyTapOptions.cs:183-195`, `ProxyTap.cs:212`; INFERRED for the consequence | Not changed: tightening a public parser changes what it accepts. Q2. The new option validates the host's value strictly (§3.3) |
| F5 | Not ProxyTap: on macOS, 25 facts of `QueryScriptEndToEndTests` fail, on the 4.14.6 tag as here. `QueryScriptGenerator.OwnCopy` writes the run's `Kronikol.dll` relative to the reports directory as given (`/var/folders/...`), and `query.cs` resolves it against the directory `dotnet run --file` reports for it, the canonical `/private/var/folders/...`, one level deeper, so it climbs one level short and looks under `/private` (`/private/Users/...`; from a worktree under `/tmp`, `/private/private/tmp/...`). So `query.cs` misses the run's own engine wherever the reports directory's path goes through a symlink, as macOS's temp folders do. CI runs the core suite on Linux only | RUN: the class on `v4.14.6` and on this branch, 25 of 29 failing on each with the paths above; READ `QueryScriptGenerator.cs:216-226` and the `here` it is resolved against; INFERRED that the entry point path is the canonical one | Not here: unrelated to this plan. A patch of its own: take the relative path from the reports directory's real path, or keep the absolute path as a second candidate |

## 3. The design

### 3.1 The option

```csharp
public Func<(string Name, string Id), TraceParent?>? ResolvedIdentityTraceparent { get; set; }
```

It takes the identity the resolver returned, the same tuple `IdentityResolver` returns, and answers with the existing
public `TraceParent` record, the type the resolver already receives. A host typically keeps, per test, its trace id and
root span id: `test => TraceParent.TryParse($"00-{traceIdOf[test.Id]}-{rootSpanOf[test.Id]}-01")`.

Why this shape:

- **Additive and keyed on the attribution.** Existing resolvers compile and behave as before, and the trace is looked
  up for the identity the resolver chose, so it cannot disagree with it (re-resolving the device in a second callback
  could pick another test if the device changed hands in between).
- **Not a second resolver** returning identity and parent together: two overlapping resolvers would need a rule for
  which wins. **Not a new return type for `IdentityResolver`**: a breaking change. **Not `ActivityContext`**: natural
  for a host holding the test's `Activity`, but the tap's API speaks `TraceParent`, and such a host writes
  `TraceParent.TryParse(activity.Id)`. **Not a string**: the record already exists and says which part is which.

### 3.2 What the tap does with it

| The request | Listener on `Kronikol.ProxyTap` | No listener |
|---|---|---|
| Carries a `traceparent` `TryParse` accepts | As before: the server span is its child (callback never asked) | As before: forwarded verbatim |
| Resolver-attributed, no `traceparent` (or none `TryParse` accepts), callback answers a valid context | Server span is its child, client span the server's child, forwarded `traceparent` is the client span's, in that trace | Forwarded `traceparent` is the context itself, flags included, whatever `SynthesizeTraceparent` says; the entries carry its trace and span ids |
| Attributed by the headers or the `traceparent` | As before (callback never asked) | As before |
| Callback answers null, an invalid context, or throws | As before: a root span | As before: `SynthesizeTraceparent` or nothing |

`SynthesizeTraceparent` is for a request with no trace at all, and this one has one: the client would have sent it had
it known it. `Validate()` refuses the option without `IdentityResolver`, since it could never apply.

### 3.3 Validation and reporting

The host's value must be W3C trace context the tap can write: a trace id of 32 and a parent span id of 16 lowercase hex
digits, neither all zeros. The version is always `00`, because the tap formats the record itself. That is stricter than
`TraceParent.TryParse` (F4) on purpose: it is what downstream parsers, `ActivityContext` among them, accept. A value
that fails, or a callback that throws, never fails the request: it goes on as without the option, and the tap writes a
`Log` line naming the value, the reason and the test id, counts it in `ResolvedIdentityTraceparentFailures`, and
`Diagnostics()` reports a `CaptureDegraded` entry. That is the tap's established channel (§1); a null answer is not a
failure and is not counted.

### 3.4 Compatibility

Source and binary: a property and two read-only counters are added; `ResolveIdentity`, the only member whose signature
changes, is internal. Behaviour: unchanged unless the option is set, except F1 and F2. Report output: unchanged, so
Kronikol4J's ledger gets no line; Kronikol4J has no proxy tap (its `kronikol4j-proxy` ports `TrackingProxy<T>`).

## 4. Tests, red first, and mutations

`ResolvedIdentityTraceparentTests` runs in a collection of its own with parallelization off: its facts attach a
process-wide `ActivityListener` to the tap's source, and the no-listener facts assert that none is attached.
`ProxyTapRegistrationTests` holds F2's facts.

| Fact | On 4.14.6 with the stubs |
|---|---|
| `With_a_listener_a_request_the_resolver_attributed_nests_under_the_span_the_host_names`: asked with the resolver's identity; server span in the test's trace, parented on its root span; client the server's child; forwarded `traceparent` the client's | Red: the callback is never asked |
| `Without_a_listener_the_request_is_forwarded_with_the_traceparent_the_host_names` (flags 01 and 00, `SynthesizeTraceparent` on and off): forwarded exactly `00-{trace}-{root span}-{flags}`, entries carry both ids | Red, 4 of 4: a random trace, or no `traceparent` at all |
| `An_inbound_traceparent_wins_over_the_one_the_host_names` (with and without a listener) | Passes: a guard (M4) |
| `A_traceparent_the_tap_cannot_read_counts_as_none` | Red: a random trace |
| `An_identity_read_from_the_headers_never_asks_the_host` (with and without a listener) | Passes: a guard (M5) |
| `A_value_that_is_not_W3C_trace_context_...` (8 values: upper-case, short, null and all-zero trace ids; upper-case, long, non-hex and all-zero span ids): a fresh trace, still attributed, counted, one diagnostic, a log line naming the value and the reason | Red, 8 of 8: not counted |
| `With_a_listener_a_value_that_is_not_W3C_trace_context_...` | Red: not counted |
| `A_host_callback_that_throws_...` | Red: not counted |
| `A_null_answer_leaves_the_request_as_it_was_and_reports_nothing` (asked once, fresh trace, no diagnostic) | Red: never asked |
| `The_option_needs_an_IdentityResolver` | Red: no exception |
| `An_IdentityResolver_that_throws_no_longer_fails_the_request` (F1) | Red: `BadGateway` |
| `Registering_taps_from_a_list_keeps_every_option` (F2's guard, by reflection, failing on a property type it has no value for) | Red: drops `ResolvedIdentityTraceparent` and `InFlightRegistry` |
| `A_tap_publishes_each_request_it_forwards_to_its_in_flight_registry` (direct, and registered from a list) | Direct passes (new coverage); from a list red |

**Mutations** (`mutate.py`, each alone, the whole ProxyTap project run against it): M1 the host never asked; M2 the
server span ignores the named context; M3 the named context not forwarded without a listener; M4 the named context wins
over an inbound one; M5 a header identity asks too; M6 a `traceparent` the tap cannot read counts as one; M7 no
validation; M8 upper-case hex accepted; M9 and M10 all-zero ids accepted; M11 a null trace id throws; M12 to M14 a
rejection not counted, not in `Diagnostics()`, not logged; M15 a throwing callback not caught; M16 a null answer counted;
M17 `SynthesizeTraceparent` off withholds the named context; M18 the flags not kept; M19 the entries keep their own
trace; M20 the option accepted without a resolver; M21 to M24 F1 undone, not counted, not reported, not logged; M25 and
M26 the list registration drops the new option, or `InFlightRegistry`. The results are in §7.

## 5. Release and records

| Release | Contents | Bump | Why this part moved |
|---|---|---|---|
| R1 | §3, F1, F2, the facts of §4 | **minor**: 4.15.0 if nothing lands before it | An option and two counters are new public surface. CLAUDE.md makes a new option a minor even when its default preserves today's behaviour |

- **The release commit is this branch's commit** (the house's "Release X.Y.Z:" shape): the version in
  `Directory.Build.props` and both `.claude-plugin` manifests, the history action's `VERSION` (4.15.0), the twelve
  template pins (4.14.6, the newest version nuget.org lists), `CHANGELOG.md`, this plan, its `PLANS_STATUS.md` row and
  `ROADMAP.md` row 1.26. If a release lands on `main` first, rebase and move every one of them to the next minor.
- **To publish:** merge, then push the tag `v4.15.0` on the merge commit (`release.yml` builds `release.slnf`, runs the
  core suite, packs and pushes to nuget.org). Then apply `wiki/Integration-ProxyTap-Extension.patch` to the wiki (it
  names 4.15.0 in four places), and record the runs in §7.
- **Kronikol4J:** no ledger line (§3.4).

## 6. Questions, with recommendations

| # | Question | Recommendation |
|---|---|---|
| Q1 | Should the tap also continue a host-named context for an identity read from the headers when the request carries no `traceparent`? | No, not without a case. A client that stamps Kronikol's headers can stamp `traceparent` too (`TestTrackingIdentity.ToHeaders()` does), so the gap is the resolver's |
| Q2 | Should `TraceParent.TryParse` follow W3C strictly (F4)? | Yes, as its own patch: refuse version `ff`, refuse an all-zero parent span id, and keep reading upper case as it does now. It changes which inbound requests are attributed by trace id, so it is a changelog line of its own |
| Q3 | Should ProxyTap gain an `OnCaptureDegraded` like TcpTap's, so a host hears of a failure as it happens? | Only if a host asks. The counters, `Diagnostics()` and `Log` cover the report and the console; a callback is a second channel to keep in step |

## 7. Log

- 2026-10-11: written and built at 4.14.6 in worktree `kronikol-wt/resolver-trace-parent`, branch
  `feature/resolver-trace-parent` off `d59843fa`. Red first (`red-v4.14.6.txt`): with the three compile stubs, 21 of the
  26 new rows failed, each on its own assertion, and the 5 guard rows passed, as §4 says. Green: the ProxyTap project
  passes 57 of 57 (31 existing, 26 new rows).
- Mutations (`mutations.jsonl`): all 26 caught, each by the new facts alone (none of the 31 facts that predate this plan
  turned red under any of them), and the restored tree passed 57 of 57. M1 alone turned 17 rows red; M2, M6,
  M9, M11, M15, M16 and M20 to M25 one each.
- The twelve template pins move to 4.14.6, which nuget.org lists for each of the twelve packages they name (RUN, the
  flat-container index of each, 2026-10-11).
- Checks: the ProxyTap project passes 57 of 57 five runs in a row; `release.slnf` builds in Release with no error (no warning in ProxyTap, on any target) and packs 62 packages at 4.15.0; the core suite (`tests/Kronikol.Tests`, Release, macOS) passes 7,117 with 6 skipped, and its other 25 facts are F5's, which fail the same way on the 4.14.6 tag.
