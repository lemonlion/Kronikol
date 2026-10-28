"""Counts FluentAssertions statements whose assertion part goes on past its first call (Which, WhoseValue, Subject,
WithMessage, Where, a second .Should()), which ParseMethodAndArgs reads as the first call's method with the LAST
top-level call's arguments. .And. chains are counted apart: Format cuts them by design (the first assertion is read).

Usage: python fa_chains.py <statements.txt> <labels.txt> [<examples to print>]
"""
import re
import sys
from collections import Counter


def mask(s):
    """True for each character inside a string or char literal (regular, verbatim and interpolated alike)."""
    out, i, quote = [False] * len(s), 0, None
    while i < len(s):
        c = s[i]
        if quote:
            out[i] = True
            if c == '\\' and quote in '"\'':
                if i + 1 < len(s):
                    out[i + 1] = True
                i += 2
                continue
            if c == quote:
                quote = None
        elif c in '"\'':
            quote = c
            out[i] = True
        i += 1
    return out


def top_level(s, token):
    m, depth = mask(s), 0
    for i, c in enumerate(s):
        if m[i]:
            continue
        if c in '([{':
            depth += 1
        elif c in ')]}':
            depth -= 1
        elif c == '.' and depth == 0 and s.startswith(token, i):
            return i
    return -1


def links_after_first_call(part):
    """The top-level '.Name' links that follow the first call's closing bracket."""
    m, depth, closed, links = mask(part), 0, False, []
    for i, c in enumerate(part):
        if m[i]:
            continue
        if c in '([{':
            depth += 1
        elif c in ')]}':
            depth -= 1
            if depth == 0:
                closed = True
        elif c == '.' and depth == 0 and closed:
            name = re.match(r'\.(\w+)', part[i:])
            links.append(name.group(1) if name else '?')
    return links


labels = {}
lines = open(sys.argv[2], encoding='utf-8').read().split('\n')
for i, line in enumerate(lines):
    if line.startswith('    => ') and i > 0:
        labels[lines[i - 1]] = line[7:]

chains, ands, total = [], 0, 0
for statement in open(sys.argv[1], encoding='utf-8').read().split('\n'):
    if not statement or statement.startswith('#'):
        continue
    k = top_level(statement, '.Should().')
    if k < 0:
        continue
    total += 1
    links = links_after_first_call(statement[k + len('.Should().'):])
    if not links:
        continue
    if links[0] == 'And':
        ands += 1
        continue
    chains.append((links, statement, labels.get(statement, '?')))

print(f'FluentAssertions statements: {total}; .And. chains: {ands}; other chains past the first call: {len(chains)}')
print('first link after the call:', dict(Counter(c[0][0] for c in chains)))
for links, statement, label in chains[: int(sys.argv[3]) if len(sys.argv) > 3 else 12]:
    print(f'  {statement}\n    => {label}')
