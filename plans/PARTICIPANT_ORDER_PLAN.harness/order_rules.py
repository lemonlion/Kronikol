"""Participant order rules for #228, measured on real diagram sources.

Reads every *.puml under the directories given (each written by `kronikol query diagram <report> --out DIR`),
parses the participant declarations and the arrows, and for each diagram compares the declared order with the
orders the candidate rules would give:

  R0  today's rule, rebuilt from the arrows: the pure caller first, then each party at its first arrow
      (caller before service). Checked against the declared order to prove the parser reads what the code wrote.
  RA  callers before callees: a party that sends a request to another is drawn left of it wherever the request
      graph allows (Kahn's algorithm, ties and cycles broken by first appearance). No configuration.
  RB  the action phase first: the pure caller, then each party at its first arrow in a When or Then step, then the
      rest as today. The phase is read off the BDD step bars, so a diagram with none keeps today's order.
  RC  RB's least move: only the party the pure caller first calls in a When or Then step moves, to right after it.
  RU  the named party (--under-test NAME, an alias) moved right after the pure caller; the rest as today.

Prints one summary line per corpus and, with --list, every diagram whose order a rule changes.
Run with `python -I order_rules.py [--under-test alias] [--list] DIR...`.
"""
import argparse
import collections
import os
import re
import sys

DECL = re.compile(r'^(actor|participant|database|queue|collections|entity|control|boundary) "(?:[^"]*)" as (\w+)')
STORE_SHAPES = {'database', 'queue', 'collections'}
# A request is `a -[#c]> b`, `a -> b`; a response is dashed: `b -[#c]-> a`, `b --> a`.
ARROW = re.compile(r'^(\w+) (-(?:\[[^\]]*\])?(-?)>{1,2}) (\w+)\s*:?')


# A BDD step bar: `hnote across <<stepDelimiter>> #black:<color:white>Given the …`. Its keyword stands in for the call's
# phase, which the source does not carry: Given is setup, When and Then are action, And and But continue the last.
STEP = re.compile(r'<<stepDelimiter>>.*?:(?:<color:\w+>)?\s*(\w+)', re.IGNORECASE)
PHASE_OF = {'given': 'setup', 'when': 'action', 'then': 'action'}


def parse(path, shapes=None):
    """Declared aliases in order, and arrows as (source, target, request|response, phase); shapes collects each alias's."""
    declared, arrows = [], []
    phase = 'unknown'
    with open(path, encoding='utf-8', errors='replace') as f:
        in_note = False
        for raw in f:
            line = raw.rstrip('\n')
            if in_note:
                if line.strip() == 'end note':
                    in_note = False
                continue
            # A note's header with a ':' carries its text on the line (`hnote across <<stepDelimiter>> #black:Given …`);
            # without one the text runs to `end note`.
            if re.match(r'^\s*(note|hnote|rnote)\b', line):
                step = STEP.search(line)
                if step:
                    phase = PHASE_OF.get(step.group(1).lower(), phase)
                in_note = ':' not in line
                continue
            m = DECL.match(line)
            if m:
                declared.append(m.group(2))
                if shapes is not None:
                    shapes[m.group(2)] = m.group(1)
                continue
            m = ARROW.match(line)
            if m:
                src, dashed, dst = m.group(1), m.group(3) == '-', m.group(4)
                arrows.append((src, dst, 'response' if dashed else 'request', phase))
    return declared, arrows


def first_appearance(arrows):
    order = []
    for src, dst, *_ in arrows:
        for p in (src, dst):
            if p not in order:
                order.append(p)
    return order


def rule_r0(arrows):
    """Today's CreateEntitiesPlantUml: the first caller that is never a service, then first appearances."""
    services = {d for s, d, k, *_ in arrows if k == 'request'} | {s for s, d, k, *_ in arrows if k == 'response'}
    callers = [s for s, d, k, *_ in arrows if k == 'request'] + [d for s, d, k, *_ in arrows if k == 'response']
    pure = next((c for c in callers if c not in services), None)
    order = [pure] if pure else []
    for p in first_appearance(arrows):
        if p not in order:
            order.append(p)
    return order


def rule_ra(arrows):
    """Callers before callees over the request graph; ties and cycles broken by first appearance."""
    base = rule_r0(arrows)
    rank = {p: i for i, p in enumerate(base)}
    edges = collections.defaultdict(set)
    indeg = {p: 0 for p in base}
    for s, d, k, *_ in arrows:
        if k != 'request' or s == d or d in edges[s]:
            continue
        edges[s].add(d)
        indeg[d] += 1
    order, left = [], set(base)
    while left:
        ready = [p for p in left if indeg[p] == 0]
        pick = min(ready or left, key=lambda p: rank[p])
        order.append(pick)
        left.discard(pick)
        for d in edges[pick]:
            if d in left:
                indeg[d] -= 1
    return order


def rule_rb(arrows):
    """The pure caller, then each party at its first ACTION-phase arrow, then the rest as today."""
    base = rule_r0(arrows)
    action = [a for a in arrows if a[3] == 'action']
    if not action:
        return base
    order = base[:1]
    for p in first_appearance(action) + base:
        if p not in order:
            order.append(p)
    return order


def rule_rc(arrows):
    """Only the party the pure caller first sends a request to in the action phase moves, to right after the caller."""
    base = rule_r0(arrows)
    if not base:
        return base
    first = next((d for s, d, k, ph in arrows if k == 'request' and ph == 'action' and s == base[0] and d != s), None)
    return rule_ru(arrows, first) if first else base


def rule_ru(arrows, under_test):
    base = rule_r0(arrows)
    if under_test not in base or len(base) < 2:
        return base
    rest = [p for p in base if p != under_test]
    return rest[:1] + [under_test] + rest[1:] if base[0] != under_test else base


def leftward_requests(order, arrows):
    pos = {p: i for i, p in enumerate(order)}
    return sum(1 for s, d, k, *_ in arrows if k == 'request' and s != d and pos.get(d, 0) < pos.get(s, 0))


def inferred_subject(all_arrows, shapes):
    """PartyResolver.InferSubjects over a whole run, read off the sources: the tests are callers nobody calls, the
    subject their callee drawn as no store, one that makes calls of its own first, then the most called."""
    requests = [(s, d) for s, d, k, *_ in all_arrows if k == 'request' and s != d]
    called = {d for _, d in requests}
    tests = {s for s, _ in requests if s not in called}
    callers = {s for s, _ in requests}
    made = collections.Counter(d for s, d in requests if s in tests and shapes.get(d) not in STORE_SHAPES)
    ranked = sorted(made, key=lambda n: (n in callers, made[n]), reverse=True)
    return ranked[0] if ranked else None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--under-test')
    ap.add_argument('--list', action='store_true')
    ap.add_argument('dirs', nargs='+')
    a = ap.parse_args()
    for d in a.dirs:
        files = sorted(os.path.join(r, f) for r, _, fs in os.walk(d) for f in fs if f.endswith('.puml'))
        n = parser_ok = with_arrows = 0
        changed = collections.Counter()
        left = collections.Counter()
        diagrams_left = collections.Counter()
        under_pos = collections.Counter()
        shapes, run_arrows = {}, []
        for path in files:
            run_arrows += parse(path, shapes)[1]
        inferred = inferred_subject(run_arrows, shapes)
        actor_subject = 0
        for path in files:
            declared, arrows = parse(path)
            if inferred:
                with open(path, encoding='utf-8', errors='replace') as f:
                    actor_subject += any(l.startswith('actor ') and l.split(' as ')[-1].split()[0] == inferred for l in f)
            n += 1
            if not arrows:
                continue
            with_arrows += 1
            r0 = rule_r0(arrows)
            if [p for p in declared if p in r0] == r0:
                parser_ok += 1
            elif a.list:
                print(f'  PARSER-MISMATCH {path}: declared={declared} r0={r0}')
            orders = {'R0': r0, 'RA': rule_ra(arrows), 'RB': rule_rb(arrows), 'RC': rule_rc(arrows)}
            if a.under_test:
                orders['RU'] = rule_ru(arrows, a.under_test)
                if a.under_test in r0:
                    under_pos[r0.index(a.under_test)] += 1
            for name, order in orders.items():
                lw = leftward_requests(order, arrows)
                left[name] += lw
                diagrams_left[name] += lw > 0
                if order != r0:
                    changed[name] += 1
                    if a.list:
                        print(f'  {name} {os.path.basename(path)}: {r0} -> {order} (leftward {leftward_requests(r0, arrows)} -> {lw})')
        print(f'{os.path.basename(os.path.normpath(d))}: {n} diagrams, {with_arrows} with arrows, parser agrees with the '
              f'declared order on {parser_ok}; leftward requests R0={left["R0"]} in {diagrams_left["R0"]} diagrams, '
              f'RA={left["RA"]} in {diagrams_left["RA"]} (order changed in {changed["RA"]}), '
              f'RB={left["RB"]} in {diagrams_left["RB"]} (changed in {changed["RB"]}), '
              f'RC={left["RC"]} in {diagrams_left["RC"]} (changed in {changed["RC"]})'
              + f'; inferred subject {inferred}, drawn as the actor in {actor_subject}'
              + (f', RU={left["RU"]} in {diagrams_left["RU"]} (changed in {changed["RU"]}); '
                 f'{a.under_test} at position {dict(sorted(under_pos.items()))}' if a.under_test else ''))


if __name__ == '__main__':
    sys.exit(main())
