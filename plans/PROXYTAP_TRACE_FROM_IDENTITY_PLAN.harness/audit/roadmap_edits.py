"""The #170 audit's roadmap rows (plan section 12, A8). Run from the repository root with the release's version:
python plans/PROXYTAP_TRACE_FROM_IDENTITY_PLAN.harness/audit/roadmap_edits.py 4.19.4"""
import pathlib
import sys

version = sys.argv[1]
p = pathlib.Path('plans/ROADMAP.md')
s = p.read_bytes().decode('utf-8')
assert 'PROXYTAP_TRACE_FROM_IDENTITY' not in s, 'already applied'


def after_line_starting(prefix, new_line):
    global s
    lines = s.split('\n')
    hits = [i for i, line in enumerate(lines) if line.startswith(prefix)]
    assert len(hits) == 1, (prefix, len(hits))
    lines.insert(hits[0] + 1, new_line)
    s = '\n'.join(lines)


def replace_once(old, new):
    global s
    assert s.count(old) == 1, (old, s.count(old))
    s = s.replace(old, new)


after_line_starting('| 1.27 | ', (
    f'| 1.28 | **Done: 4.18.3 (R0) and 4.19.0 (R1), 2026-10-13; #170 closed; audited the same day, fixes as {version}.** '
    '**#170** A proxy tap in front of a caller that cannot send a trace (a mobile app) forwarded every call under a new '
    'random trace, so the services, database calls and taps behind it landed in "Traffic outside any test" '
    '(`PROXYTAP_TRACE_FROM_IDENTITY_PLAN.md`). R0 (a patch) fixed what the option needed first: a tap whose spans are not '
    "recorded forwarded calls unsampled, a listener that dropped the tap's server span replaced the caller's trace, the "
    "list registration dropped `InFlightRegistry`, and one test's calls in one trace showed each other's internal flow. "
    'R1 (a minor) adds `ProxyTapOptions.TraceIdFromIdentity`. The audit found the plan\'s acceptance never run and, once '
    "run, an app's two calls at once showing each other's queries (the span tree now decides which call a call ran "
    "inside), the internal-flow rule taking cubic time, the tap's forward running the process's HttpClient "
    'instrumentation, and a tap started under an activity forwarding it on every call | #170. RUN: the plan\'s harness '
    '(published 4.18.1) and its `audit/` (published 4.19.0) | Green-lit in full by the owner on 2026-10-13, every '
    'question in §9 as recommended, and released as each slice passed, ahead of #172 and #171 |'))

replace_once('#160 → 1.21 (its helpers; per-host options open) · #167 → 1.21',
             '#160 → 1.21 (its helpers; per-host options open) · #167 → 1.21 · #170 → 1.28')
replace_once('`ASSERTION_VALUE_SUBSTITUTION` → 1.27 ·',
             '`ASSERTION_VALUE_SUBSTITUTION` → 1.27 · `PROXYTAP_TRACE_FROM_IDENTITY` → 1.28, Appendix C ·')

after_line_starting('| `ASSERTION_VALUE_SUBSTITUTION_PLAN` (2026-10-11) |', (
    '| `PROXYTAP_TRACE_FROM_IDENTITY_PLAN` (2026-10-13) | DONE; green-lit in full 2026-10-13 by the owner, every §9 '
    f'question as recommended; audited 2026-10-13 | 4.18.3 (R0), 4.19.0 (R1), {version} (audit) | 1.28 | #170: the '
    "forwarded sampled flag, the caller's trace kept when a listener drops the server span, `InFlightRegistry` copied by "
    "the list registration, one test's calls in one trace told apart by the span tree (R0, patch); "
    "`ProxyTapOptions.TraceIdFromIdentity` (R1, minor); the audit's fixes (its section 12) |"))

after_line_starting('| `MONGODB_ACCESSOR_OPTION_PLAN` | ', (
    '| `PROXYTAP_TRACE_FROM_IDENTITY_PLAN` | Q2(b): hyphenated GUID test ids (ReqNRoll, xUnit 2) forwarded as their `N` '
    'form, with every reader that files records under tests normalising GUID spellings. Q6: ids that are not hex (xUnit '
    "v3, NUnit), and an ingest join by the tap record's `activityTraceId`. Q7, the option on by default, is a v5 "
    "candidate (12.1). Section 8: a test's own calls through `TestTrackingMessageHandler` with no current activity mint a "
    'random trace per request and send it unsampled; `TestTrackingIdentity.Create(name, id)` and `FromCurrentScope()` '
    'mint a trace that is not the test id. From the audit (section 12): a span the tree gives to another call of the '
    'test, started after that call answered, shows in neither popup, only in the whole-test flow | Left to the owner; '
    'each serves hosts the shipped option does not |'))

replace_once("dropping the JSON's indentation (`V4_PLAN.md` Q1).",
             "dropping the JSON's indentation (`V4_PLAN.md` Q1), and `ProxyTapOptions.TraceIdFromIdentity` on by default "
             "(`PROXYTAP_TRACE_FROM_IDENTITY_PLAN.md` Q7, taken 2026-10-13).")

p.write_bytes(s.encode('utf-8'))
print('roadmap edited')
