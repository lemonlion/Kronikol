"""The #170 audit's record in the plan (status, sections 3.11, 4.5, 7, the log and section 12) and its PLANS_STATUS row.
Run from the repository root with the release's version: python .../audit/plan_edits.py 4.19.4"""
import pathlib
import sys

version = sys.argv[1]
p = pathlib.Path('plans/PROXYTAP_TRACE_FROM_IDENTITY_PLAN.md')
s = p.read_bytes().decode('utf-8')


def replace_once(old, new):
    global s
    assert s.count(old) == 1, (old[:80], s.count(old))
    s = s.replace(old, new)


replace_once('R0 as 4.18.3 and R1 as 4.19.0; #170\nclosed.**',
             f'R0 as 4.18.3 and R1 as 4.19.0; #170\nclosed. Audited the same day at the owner\'s request; its fixes '
             f'shipped as {version} (section 12).**')
replace_once('Two calls from one party never nest, so a mobile app\'s\n  parallel calls are still told apart.',
             'Two calls from one party never nest, so a mobile app\'s\n  parallel calls are still told apart. '
             'The audit found that the rule alone puts the queries a service makes for two such calls inside the call '
             'opened last, and put the span tree first (section 12, A1).')
replace_once('they are shown in the other call\'s\n  popup.',
             'they are shown in the other call\'s\n  popup, when its time holds them (section 12 records the case where it does not).')
replace_once('and which of a test\'s\ncalls ran inside which, by `CallNesting`\'s rule over the test\'s records in time order (section 3.11).',
             'and which of a test\'s\ncalls ran inside which, over the test\'s records in time order: by the span tree first and '
             'by `CallNesting`\'s rule where the tree says nothing (section 3.11; the tree came first in the audit, section 12).')
replace_once('No row is taken: the owner green-lit the plan directly on 2026-10-13, as #133\'s plan was.',
             'No row was taken when the owner green-lit the plan directly on 2026-10-13, as #133\'s plan was. The audit '
             '(section 12, A8) gave it row 1.28, done, a register row and an Appendix C row, as the other green-lit issue '
             'plans have, and put Q7 among v5\'s candidate default changes in 12.1, as the owner took it.')

audit = f'''- **2026-10-13, audited (owner's request: check the plan was completed in full and properly, with no problems in what
  was built, and fix what is not).** Section 12. Nine findings, A1 to A9, each measured before its fix; the fixes
  shipped as {version}.

## 12. The audit (2026-10-13)

The owner asked, after both releases were out, for a check that the plan was completed in full and properly, with no
problems in what was built, and for fixes where there were. Every item of sections 4 to 6 was ticked against the
diff, the harness and the published records. Each finding below was measured before it was fixed (harness `audit/`),
each fix has a fact that fails on 4.19.0 (`audit/red-4.19.0.txt`), and its mutations are caught
(`audit/mutations.txt`: 11 of 13, two equivalent with the reason written there).

| # | Finding | How it was found | Fix in {version} |
|---|---|---|---|
| A1 | F4's nesting put the queries a service made for two of an app's calls at once inside whichever call opened last, so that call's popup showed the other call's query too. This is #170's own shape (a mobile app's parallel calls through a tap, the service's database spans recorded by the OTLP tap as calls of the test), and section 5.3's acceptance names it | Running section 5.3's acceptance for the first time (`audit/acceptance.cs`): on the published 4.19.0, three runs of three (`audit/acceptance-4.19.0.txt`) | The span tree first: an open call whose span id is above a call's span ran it; `CallNesting`'s rule only where the tree says nothing. Kronikol4J the same |
| A2 | F4's cost: for every span of the trace and every call recording a span id above it, a fresh walk of the calls' nesting, before the time filter. One test of 300 calls under one ambient span, each with a downstream call: 22.3 s (4.18.2: 56 ms); 1,000 calls ran past 20 minutes | Reading the builder, then `audit/bench-f4.cs` (`audit/bench.txt`) | Each call's own span ids worked out once, from one walk up each call's outer calls, and checked against the spans in its time only: 71 ms for the 300 calls. Kronikol4J, which checked only spans in a call's time and never took long, takes the same form |
| A3 | The tap's upstream `HttpClient` ran the process's HttpClient instrumentation. OpenTelemetry's instrumentation puts a span of its own in place of the forwarded `traceparent`: with nothing listening to the tap, a new trace, so neither the caller's trace nor the test's reached the service; beside Kronikol's internal-flow listener, `-00` again (F3 undone) and a parent no store holds | Reading `StartAsync`, then `audit/probe-http-instrumentation.cs` against the published 4.19.0 (`audit/probe-http-instrumentation-4.19.0.txt`) | `ActivityHeadersPropagator = null` on the tap's handler; the tap's own client span is the forward's span |
| A4 | A tap started under an activity forwarded that activity's trace and baggage on every call that brought no trace (R1 had cleared it for the test's trace only) | The same probe | `StartAsync` starts the accept loop with no current activity and gives its caller's back |
| A5 | With the option on, a resolver returning a null id made the tap answer 502 | Reading `IsTraceId` | A null id counts as not applied |
| A6 | The `OptionNotApplied` diagnostic and its log line left out "not all zero" | Reading | Added |
| A7 | Section 5.3's acceptance had never been run: the harness held no record of it | The harness | Run, on this tree and on 4.19.0 (A1); it passes on {version} |
| A8 | Q7, taken by the owner, was never written into the roadmap, and the plan had no roadmap row | `ROADMAP.md` held no "#170" | Row 1.28, Appendix B and C rows, Q7 in 12.1 |
| A9 | Two nesting rules F4 relies on had no fact: nesting two deep, and two calls recording one span | Mutations that survived the first run | `A_call_keeps_the_spans_of_the_calls_nested_two_deep_inside_it`, `A_call_does_not_run_inside_another_that_records_the_same_span` |

Checked and sound: every fact named in sections 5.1 and 5.2 exists, the record handed in twice among them; the
registration fact fails on any option it cannot fill; no producer writes an empty pair id or Kronikol trace id, so
the builder's copy of `CallNesting` cannot differ from it there (ingest hashes or mints them); the wiki's sentences
for R0 and R1 match the code, and no recipe for a device on the network was published.

Left as it is: a span the tree gives to another call of the test, which started after that call answered (work it
left running), shows in neither popup, only in the whole-test flow; before 4.18.3 it showed in whichever call's time
held it, as that call's work. Section 3.11's claim that the spans F4 removes "are shown in the other call's popup"
holds only when that call's time holds them.

'''
replace_once('## Appendix A. Edit sites', audit + '## Appendix A. Edit sites')
p.write_bytes(s.encode('utf-8'))

q = pathlib.Path('plans/PLANS_STATUS.md')
t = q.read_bytes().decode('utf-8')
old = 'R0 as 4.18.3, R1 as 4.19.0; #170 closed**'
assert t.count(old) == 1, t.count(old)
t = t.replace(old, f'R0 as 4.18.3, R1 as 4.19.0; #170 closed; audited 2026-10-13, fixes as {version} (section 12)**')
q.write_bytes(t.encode('utf-8'))
print('plan edited')
