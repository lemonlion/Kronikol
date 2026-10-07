"""R1's wiki edits (plans/WARM_UP_PLAN.md 6.4): the marks in the data files, the CLI's lines, a page on why a first
scenario is slow, and what merge, ingest and BDDfy do with them.

    python r1_wiki.py <wiki checkout> <version>

Each edit anchors on text the page holds and fails if it is missing or ambiguous, so a page that moved is found
rather than edited in the wrong place. Run once: a second run fails on the first edit, whose text is now there.
"""
import pathlib
import sys

WIKI = pathlib.Path(sys.argv[1])
V = sys.argv[2]


def edit(page, anchor, new, after=True):
    path = WIKI / page
    text = path.read_text(encoding='utf-8')
    assert text.count(anchor) == 1, (page, anchor[:60], text.count(anchor))
    assert new.strip() not in text, (page, 'already applied', new[:60])
    text = text.replace(anchor, anchor + new if after else new + anchor)
    path.write_bytes(text.encode('utf-8'))
    print('edited', page)


def replace(page, old, new):
    path = WIKI / page
    text = path.read_text(encoding='utf-8')
    assert text.count(old) == 1, (page, old[:60], text.count(old))
    path.write_bytes(text.replace(old, new).encode('utf-8'))
    print('edited', page)


PAGE = 'Why-Is-My-First-Scenario-Slow'

# Generated-Reports: the scenario's field and the call's.
edit('Generated-Reports.md', '\n| `stepPath` |', f"""
| `warmUp` | On the request record of a call the run's first-call warm-up slowed down ({V}): see [Why is my first scenario slow?]({PAGE}). For each kind of call a test makes (its service, its method and its path with ids folded, or a statement's templated head), the run's first call is marked when it took at least 10 times and 50 ms more than the median of the later calls of its kind, and so is a call that started while it ran and waited for it. `kind` is `first` or `waited`, `shape` the kind of call (`POST /orders/{{id}}`), `baselineMs` and `baselineCalls` the median of the later calls and how many it is over, and `first`, on a call that waited, the `requestResponseId` of the call it waited for. Absent on every other record. |""",
     after=False)
edit('Generated-Reports.md', '\n| `sourceFile`, `sourceLine` (scenario and feature) |', f"""
| `warmUpSeconds` | How much of the scenario's `durationSeconds` was the run's first-call warm-up ({V}): the time its marked calls (`warmUp`, below) cover, each instant counted once and never more than `durationSeconds`, so `durationSeconds - warmUpSeconds` is the scenario's own time. Written only on a scenario that carries such a call; `TestRunReport.xml` and `.yml` write it as `WarmUpSeconds`, and the XSD takes it as optional. |""",
     after=False)

# Querying-Reports: the JSON members, summary, scenarios, flow, interactions and the run diff.
replace('Querying-Reports.md', '`failed` (up to ten), `failedTotal`, `slowest`, `diagnostics` |',
        f'`failed` (up to ten), `failedTotal`, `slowest` (from {V} an item carries `warmUpSeconds` when it has one, and the list is ordered by `durationSeconds - warmUpSeconds`), `warmUp` ({V}, when the run marked any: `seconds`, `scenarios`, `calls`, and `largest` with its `address`, `warmUpSeconds` and `durationSeconds`), `diagnostics` |')
replace('Querying-Reports.md', 'status, durationMs, stepPath, `request`/`response` body hash and length | — |',
        f'status, durationMs, stepPath, `request`/`response` body hash and length, and `warmUp` on a marked call ({V}) | — |')
edit('Querying-Reports.md', '(4.6.2+; before, every scenario that did not fail was counted as passed).\n', f"""
The Slowest list ranks each scenario by its own time. When the run marked a first-call warm-up ({V}, see
[Why is my first scenario slow?]({PAGE})) its heading says the warm-up is left out, and a line under it says
where the warm-up went. On BreakfastProvider's xUnit lane of 2026-10-05:

```
Slowest, first-call warm-up left out:
  s100  1.23s  Valid order should be created and an event published
  s109  1.13s  Outbox message should transition to failed after exhausting retries
  s101  0.94s  Creating an order should produce an audit log entry and events
First-call warm-up: 2.65s in 7 scenarios; the most in s49, 1.19s of its 1.27s (flow s49)
```

By wall time the list opened with s49 at 1.27 s, three first calls and almost nothing else. A run with no mark
prints `Slowest:` and ranks by wall time, as before.
""")
replace('Querying-Reports.md', '| `--slower-than 5` | seconds |',
        f'| `--slower-than 5` | seconds of the scenario\'s own time: from {V} a marked first-call warm-up is left out |')
edit('Querying-Reports.md', '\nA call is indented two spaces under the call it ran inside:', f"""
A call the run's first-call warm-up slowed down ({V}) gains one field after its duration, before its body pointer,
so neither the start of the line nor its `inside s3/i4` ending moves:
`661 ms  first POST /orders of the run: later calls 7.6 ms median (39)`, or for a call that waited for it,
`406 ms  waited for s88/i1, the run's first POST /graphql: later calls 43 ms median (5)`.
""", after=False)
edit('Querying-Reports.md', 'request and the response. Without an address it covers the whole run — rows print full `s3/i47`\naddresses either way.\n', f"""
A call the run's first-call warm-up slowed down reads ` warm-up` after its duration ({V}), and its `--json` item
carries `warmUp`.
""")
edit('Querying-Reports.md', '`--count` prints how many rows it holds (4.6.1; it printed the whole diff).', f""" From {V}, when
both runs carry warm-up marks, Slower compares each scenario's own time, so a scenario that went first in one run
and not in the other is not reported slower for it.""")

# The page itself, and the FAQ's pointer to it.
(WIKI / f'{PAGE}.md').write_bytes(f"""# Why is my first scenario slow?

Often because it went first. The first request down each path pays the app's one-time warm-up: the JIT, a
GraphQL schema build, a first database connection. On BreakfastProvider's CI run of 2026-10-05 the run's first
`POST /graphql` took 55 to 115 times what later ones did, and whichever scenario made it carried the cost.

## What Kronikol marks ({V})

For each kind of call a test makes (its service, its method and its path with ids, numbers and timestamps folded;
a statement's templated head for a database), the run's first call is a **first-call warm-up** when it took at
least 10 times and 50 ms more than the median of the calls of its kind that started after it ended. A call that
started while it ran and waited for it is marked too. A first call that failed with a 5xx is compared only with
later calls that failed so. The app's own calls, events and the background are not judged, and a kind of call made
once is not judged either.

- The data files carry `warmUp` on each marked call and `warmUpSeconds` on its scenario
  ([Generated Reports](Generated-Reports#interaction-fields)); `durationSeconds` is unchanged.
- `kronikol query summary` ranks Slowest by each scenario's own time and says where the warm-up went; `flow` and
  `interactions` mark the calls; `scenarios --slower-than` and the run diff's Slower read the time left
  ([Querying Reports](Querying-Reports)).
- `HistoryShapeTemplates` ([Report Configuration](Report-Configuration)) fold your own ids in the kinds of call
  too: without them an id the built-in templates do not know splits a kind, which only marks less.

On BreakfastProvider's CI run the rule marked 10 calls in 7 of 212 scenarios (xUnit) and 18 in 17 of 214
(ReqNRoll).

## What it cannot see

- **A path called once.** With nothing to compare it with, a single call's warm-up cannot be told from its work.
- **A first call whose later calls all overlapped it.** No call ran warm after it.
- **An event-driven wait.** A scenario that polls for an event waits on the app's consumer, whose first message
  pays the warm-up; the test's own calls are not slow.
- **A second host.** A test that builds its own host pays part of the warm-up again. Only the run's first call of
  each kind is judged, and nothing records which host served a call.
- **Parallel lanes.** Only a call that overlaps the first is judged a waiter, so a later call that is still slow is
  not marked.
- **Several processes in one ingest** ([Ingesting External Captures](Ingesting-External-Captures)): each kind's
  first call across all the files is judged, and another process's own first call is not.

## How to avoid it

Measured on BreakfastProvider's xUnit lane: as it is, the run's first `POST /graphql` took 440 and 737 ms against
later medians of 7.0 and 5.4 ms. With HotChocolate's `InitializeOnStartup()` it took 108 ms, and with
`InitializeOnStartup(warmup: ...)` running a real query 25 ms, and that call was not marked.

- Send one request down each expensive path before the first scenario, from an assembly or collection fixture,
  through a client Kronikol does not track (or a tracked one outside any test, whose calls land in no scenario).
- For HotChocolate, `InitializeOnStartup(warmup: (executor, ct) => ...)` with a real query: the schema alone leaves
  a first execution of about 100 ms. It runs as a hosted background service, so a suite whose first scenario is a
  GraphQL one can still wait on it.
- The trade: a warm-up hides the first-request cost production users pay after a deploy. If that matters, test it
  on purpose, in a scenario of its own, and let the mark show it.
""".encode('utf-8'))
print('wrote', PAGE)
edit('FAQ.md', '\n---\n\n## See Also', f"""

**Why is my first scenario slow?**

Often because it went first and paid the app's one-time warm-up (the JIT, a schema build, a first connection). From
{V} Kronikol marks those calls, leaves them out of `kronikol query summary`'s Slowest and says where the time went:
[Why is my first scenario slow?]({PAGE}).
""", after=False)
edit('_Sidebar.md', '* [[FAQ]]\n', f'  * [[Why is my first scenario slow?|{PAGE}]]\n')

# History's cold start is not the app's warm-up.
replace('Cross-Run-History.md', 'pull request\'s first push is a cold start, when the question it',
        'pull request\'s first push is a cold start (here a stream with too short a ledger; the app\'s first-request cost is what the report calls [first-call warm-up](' + PAGE + ')), when the question it')

# Merge, ingest and BDDfy.
edit('Merging-Parallel-Reports.md', 'call follows its own shard\'s scenario.\n', f"""
From {V} each shard's first-call warm-up marks (`warmUp`, `warmUpSeconds`) are carried into the merged file and
never recomputed: every shard is a process that paid its own warm-up, and judging one shard's first call against
another's later ones would mark what neither process saw. Shards written before {V} carry no marks, and the merged
file has none for them.
""")
edit('Ingesting-External-Captures.md', '\n## Design notes', f"""
### First-call warm-up ({V})

An ingested run is marked by the same rule as a .NET run ([Why is my first scenario slow?]({PAGE})): its data file
carries `warmUp` and `warmUpSeconds`, and the CLI reads them. Several capture or messages files make one run, and no
record says which process served a call, so the rule judges each kind of call's first across all the files: a second
worker's own first call goes unmarked. That only marks less; it never marks a call that was not slowed.
""", after=False)
edit('Integration-BDDfy-xUnit3.md', 'Your HTTP call steps can be async.\n', """
Until [#122](https://github.com/lemonlion/Kronikol/issues/122) is fixed, BDDfy times an async step until its first
`await`, so a scenario's recorded duration can be shorter than its own calls. Its first-call warm-up
(`warmUpSeconds`, see [Why is my first scenario slow?](""" + PAGE + """)) is capped at that duration, so the time
left is never negative, but it can read as zero.
""")
