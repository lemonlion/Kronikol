"""R1's rule as §5.1 states it, measured on real diagram sources, beside the narrower RU of order_rules.py.

  R1    the pure caller (when the test has one), then the parties resolved Test, then those resolved UnderTest, then the
        rest, each group in today's order (a stable partition of today's order).
  R1-P  as R1, but the parties resolved Test other than the pure caller stay where they are today: only the parties
        under test move.

The roles are given as aliases: `--test caller --under-test breakfastProvider` (each may repeat). Prints, per
directory, how many diagrams each rule reorders, and with --list each reordered diagram's order before and after.
Run with `python -I r1_rule.py --test ALIAS --under-test ALIAS [--list] DIR...` beside order_rules.py.
"""
import argparse
import collections
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from order_rules import parse, rule_r0  # noqa: E402


def pure_caller(arrows):
    services = {d for s, d, k, *_ in arrows if k == 'request'} | {s for s, d, k, *_ in arrows if k == 'response'}
    callers = [s for s, d, k, *_ in arrows if k == 'request'] + [d for s, d, k, *_ in arrows if k == 'response']
    return next((c for c in callers if c not in services), None)


def partition(today, groups):
    """A stable partition of today's order: each party in the first group that holds it, the rest last."""
    def rank(p):
        return next((i for i, g in enumerate(groups) if p in g), len(groups))
    return sorted(today, key=lambda p: (rank(p), today.index(p)))


def rule_r1(arrows, tests, under_test, move_other_tests=True):
    today = rule_r0(arrows)
    pure = pure_caller(arrows)
    head = {pure} if pure else set()
    return partition(today, [head, tests - head if move_other_tests else set(), under_test - head])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--test', action='append', default=[])
    ap.add_argument('--under-test', action='append', default=[])
    ap.add_argument('--list', action='store_true')
    ap.add_argument('dirs', nargs='+')
    a = ap.parse_args()
    tests, under_test = set(a.test), set(a.under_test)
    for d in a.dirs:
        files = sorted(os.path.join(r, f) for r, _, fs in os.walk(d) for f in fs if f.endswith('.puml'))
        counts = collections.Counter()
        for path in files:
            _, arrows = parse(path)
            if not arrows:
                continue
            counts['with arrows'] += 1
            today = rule_r0(arrows)
            pure = pure_caller(arrows)
            counts['no pure caller'] += pure is None
            counts['holds a party under test'] += bool(under_test & set(today))
            for name, order in (('R1', rule_r1(arrows, tests, under_test)),
                                ('R1-P', rule_r1(arrows, tests, under_test, move_other_tests=False))):
                if order != today:
                    counts[name + ' reorders'] += 1
                    if a.list:
                        print(f'  {name} {os.path.relpath(path, d)}: pure={pure} today={today[:6]} -> {order[:6]}')
        print(f'{d}: {len(files)} diagrams; ' + ', '.join(f'{k} {v}' for k, v in counts.items()))


if __name__ == '__main__':
    main()
