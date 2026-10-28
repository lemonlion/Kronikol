"""Counts Shouldly's Should.Throw statements, and those whose chain goes on past the call (.Message.ShouldBe(…)),
which ParseMethodAndArgs read as Throw with the last call's arguments (F10).

Usage: python shouldly_chains.py <statements.txt> [...]
"""
import re
import sys
from collections import Counter


def call_end(s, i):
    """The index after the call whose name starts at i: its name, its type arguments and its bracketed arguments."""
    i += re.match(r'\w+', s[i:]).end()
    if i < len(s) and s[i] == '<':
        depth = 0
        while i < len(s):
            depth += s[i] == '<'
            depth -= s[i] == '>'
            i += 1
            if depth == 0:
                break
    if i < len(s) and s[i] == '(':
        depth, quote = 0, None
        while i < len(s):
            c = s[i]
            if quote:
                if c == '\\':
                    i += 2
                    continue
                if c == quote:
                    quote = None
            elif c in '"\'':
                quote = c
            elif c in '([{':
                depth += 1
            elif c in ')]}':
                depth -= 1
                if depth == 0:
                    return i + 1
            i += 1
    return i


for path in sys.argv[1:]:
    statements = [l for l in open(path, encoding='utf-8').read().split('\n') if l and not l.startswith('#')]
    throws, chained, links = 0, 0, Counter()
    for line in statements:
        body = line
        for head in ('await ',):
            if body.startswith(head):
                body = body[len(head):]
        if body.startswith('var ') and ' = ' in body:
            body = body.split(' = ', 1)[1]
        if body.startswith('await '):
            body = body[len('await '):]
        if not body.startswith('Should.Throw'):
            continue
        throws += 1
        # A statement over several lines holds its breaks as the two characters \n (extract_multiline.py).
        rest = body[call_end(body, len('Should.')):].replace('\\n', ' ').lstrip()
        if rest.startswith('.'):
            chained += 1
            name = re.match(r'\.(\w+)', rest)
            links[name.group(1) if name else '?'] += 1
    print(f'{path.replace(chr(92), "/").split("/")[-1]}: Should.Throw statements {throws}, '
          f'chained past the call {chained}: {dict(links.most_common(6))}')
