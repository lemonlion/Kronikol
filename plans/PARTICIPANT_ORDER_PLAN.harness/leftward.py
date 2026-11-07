"""Every request that points left under today's order, counted by (from, to) over all diagrams under the directories given.

Run with `python -I leftward.py DIR...` beside order_rules.py.
"""
import collections
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from order_rules import parse  # noqa: E402

pairs = collections.Counter()
for d in sys.argv[1:]:
    for root, _, names in os.walk(d):
        for name in names:
            if not name.endswith('.puml'):
                continue
            declared, arrows = parse(os.path.join(root, name))
            pos = {p: i for i, p in enumerate(declared)}
            for s, t, kind, _ in arrows:
                if kind == 'request' and s != t and pos.get(t, 0) < pos.get(s, 0):
                    pairs[(s, t)] += 1
for (s, t), n in pairs.most_common():
    print(f'{n:6}  {s} -> {t}')
