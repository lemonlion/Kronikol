"""The warm-up audit patch's wiki edits (plans/WARM_UP_PLAN.md, audit of 2026-10-12), as exact-string replacements.

Run from a wiki worktree of its own at release time: python audit113-wiki.py <wiki-dir> <version>. Each old string must
occur exactly once; the wiki checkout writes CRLF, so line endings are matched as the file has them.
"""
import sys

wiki, version = sys.argv[1], sys.argv[2]

EDITS = {
    'Why-Is-My-First-Scenario-Slow.md': [
        ("""- `kronikol query summary` ranks Slowest by each scenario's own time and says where the warm-up went; `flow` and
  `interactions` mark the calls; `scenarios --slower-than` and the run diff's Slower read the time left
  ([Querying Reports](Querying-Reports)).""",
         """- `kronikol query summary` ranks Slowest by each scenario's own time and says where the warm-up went; `flow` and
  `interactions` mark the calls; `scenarios --slower-than` and the run diff's Slower read the time left
  ([Querying Reports](Querying-Reports)).
- The HTML report (4.9.0) keeps a marked scenario's wall time as its duration badge and adds the warm-up as muted text,
  `2.0s · 1.5s warm-up`, with the calls named when you hover over it; the Scenario Timeline shades the warm-up's share of
  the scenario's bar, and the P50 to P99 filter ranks each scenario by its own time
  ([Generated Reports](Generated-Reports#report-features))."""),
    ],
    'Querying-Reports.md': [
        ("""| `--slower-than 5` | seconds of the scenario's own time: from 4.8.0 a marked first-call warm-up is left out |""",
         f"""| `--slower-than 5` | seconds of the scenario's own time: from 4.8.0 a marked first-call warm-up is left out. From {version} a note counts the scenarios that took as long only with their warm-up, a marked scenario's row ends `  0.6s warm-up` (its time is wall time), and its `--json` item carries `warmUpSeconds` |"""),
        ("""A call the run's first-call warm-up slowed down reads ` warm-up` after its duration (4.8.0), and its `--json` item
carries `warmUp`.""",
         f"""A call the run's first-call warm-up slowed down reads ` warm-up` after its duration (4.8.0), and its `--json` item
carries `warmUp`. On a call that waited, its `first` is the address of the call it waited for, as `flow` prints it (from
{version}; before, the call's `requestResponseId`, which no verb takes)."""),
        ("""both runs carry warm-up marks, Slower compares each scenario's own time, so a scenario that went first in one run
and not in the other is not reported slower for it.""",
         f"""both runs carry warm-up marks, Slower compares each scenario's own time, so a scenario that went first in one run
and not in the other is not reported slower for it. From {version} a run written by 4.8.0 or later that paid no warm-up
counts as paying none, where it was read by wall time beside a marked run; a run written before 4.8.0 is still read by
wall time on both sides."""),
    ],
    'Integration-DispatchProxy-Extension.md': [
        ("""- **`Task<TResult>` methods** — Awaited; result serialised as response content
- **`Task` (void) methods** — Awaited; no response content
- **Exceptions** — Logged with `500 InternalServerError` status; original exception re-thrown (unwrapped from `TargetInvocationException`)""",
         f"""- **`Task<TResult>` and `ValueTask<TResult>` methods** — Awaited; result serialised as response content
- **`Task` and `ValueTask` (void) methods** — Awaited; no response content
- **Exceptions** — Logged with `500 InternalServerError` status; original exception re-thrown (unwrapped from `TargetInvocationException`)

A method that returns a `ValueTask` is awaited from {version}. Before, it was recorded when it returned, so a call that
failed later read as a success, and a `ValueTask<TResult>` was written by reading its result, which held the caller's
thread until the call ended."""),
        ("""- Task unwrapping (completed tasks → their result; pending → `<pending Task>`)""",
         f"""- Task unwrapping (completed tasks → their result; pending → `<pending Task>`)
- A `ValueTask` written as `<ValueTask>` and never read (from {version}), since reading one can block or consume it"""),
    ],
}

for page, edits in EDITS.items():
    path = f'{wiki}/{page}'
    raw = open(path, encoding='utf-8', newline='').read()
    crlf = '\r\n' in raw
    text = raw.replace('\r\n', '\n')
    for old, new in edits:
        if text.count(old) != 1:
            sys.exit(f'{page}: expected one match, found {text.count(old)}: {old[:70]!r}')
        text = text.replace(old, new)
    open(path, 'w', encoding='utf-8', newline='').write(text.replace('\n', '\r\n') if crlf else text)
    print(f'{page}: {len(edits)} edit(s)')
