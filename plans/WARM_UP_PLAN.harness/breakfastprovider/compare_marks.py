"""S7 (plans/WARM_UP_PLAN.md 6.6): a release's own marks against warmup.py's, call for call.

    python compare_marks.py <root>

For every TestRunReport.json under root, the marks the report carries (`warmUp` on request records, `warmUpSeconds`
on scenarios) are compared with what `warmup.py marks` finds in the same file: the same calls, each with the same
kind, shape, baseline and first call, and each scenario's warm-up within 1 ms. Prints one line per lane and every
difference; exits 1 when any lane differs. On a report written before the marks every mark the script finds is a
difference, which is the control: a comparison that passes there cannot catch anything.
"""
import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))
import warmup  # noqa: E402


def report_marks(path):
    with open(path, encoding='utf-8-sig') as f:
        report = json.load(f)
    calls, scenarios = {}, {}
    for feature in report.get('features') or []:
        for scenario in feature.get('scenarios') or []:
            if scenario.get('warmUpSeconds'):
                scenarios[scenario['id']] = scenario['warmUpSeconds'] * 1000.0
            for record in scenario.get('httpInteractions') or []:
                mark = record.get('warmUp')
                if mark is not None:
                    calls[record['requestResponseId']] = mark
    return calls, scenarios


def main():
    root = sys.argv[1]
    differs = False
    for lane, loader in warmup.lanes_under(root):
        # Only a full report carries the release's own marks; a reduced corpus file has none to compare.
        if loader.__defaults__ is None or not str(loader.__defaults__[0]).endswith('TestRunReport.json'):
            continue
        path = loader.__defaults__[0]
        scenarios, calls = warmup.load(loader)
        expected = {c['rid']: c for c in warmup.detect(calls, 'P')}
        expected_ms = {sc['id']: warmup.warm_up_ms(sc) for sc in scenarios if warmup.warm_up_ms(sc) > 0}
        actual, actual_ms = report_marks(path)

        problems = []
        for rid in sorted(set(expected) | set(actual)):
            if rid not in actual:
                c = expected[rid]
                problems.append(f'  missing  {rid}  {c["warm"][0]} {c["shape"][1]} {c["shape"][2]}  {c["dur"]:.0f} ms')
                continue
            if rid not in expected:
                problems.append(f'  extra    {rid}  {actual[rid]}')
                continue
            c, m = expected[rid], actual[rid]
            want = {'kind': c['warm'][0], 'shape': c['name'], 'baselineCalls': c['warm'][2],
                    'first': c['warm'][3] if len(c['warm']) > 3 else None}
            got = {'kind': m.get('kind'), 'shape': m.get('shape'), 'baselineCalls': m.get('baselineCalls'), 'first': m.get('first')}
            if want != got or abs(c['warm'][1] - m.get('baselineMs', -1)) > 0.001:
                problems.append(f'  differs  {rid}  script {want} {c["warm"][1]:.4f} ms, report {got} {m.get("baselineMs")} ms')
        for sid in sorted(set(expected_ms) | set(actual_ms)):
            e, a = expected_ms.get(sid), actual_ms.get(sid)
            if e is None or a is None or abs(e - a) > 1.0:
                problems.append(f'  scenario {sid}  script {e} ms, report {a} ms')

        print(f'{lane}: script {len(expected)} marks in {len(expected_ms)} scenarios, report {len(actual)} in {len(actual_ms)}: '
              + ('equal' if not problems else f'{len(problems)} differences'))
        for p in problems:
            print(p)
        differs |= bool(problems)
    sys.exit(1 if differs else 0)


if __name__ == '__main__':
    main()
