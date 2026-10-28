"""Sorts the labels a prototype changes, to find any it makes worse: one that read as a sentence on 5.12.0 ("should" or
TUnit's verb in it) and is code as written on the prototype, and one that grew by much.

Usage: python changes.py <labels-5.12.0.txt> <labels-prototype.txt> [<examples to print>]
"""
import re
import sys


def labels(path):
    out, lines = {}, open(path, encoding='utf-8').read().split('\n')
    for i, line in enumerate(lines):
        if line.startswith('    => ') and i > 0:
            out[lines[i - 1]] = line[7:]
    return out


before, after = labels(sys.argv[1]), labels(sys.argv[2])
show = int(sys.argv[3]) if len(sys.argv) > 3 else 8
changed = [(s, before[s], after[s]) for s in before if s in after and before[s] != after[s]]
sentence = re.compile(r'\b(should|is|has|contains|throws|does)\b', re.I)
lost = [c for c in changed if sentence.search(c[1]) and not sentence.search(c[2])]
grew = [c for c in changed if len(c[2]) > len(c[1]) + 40]
print(f'{len(changed)} changed; {len(lost)} lost their sentence; {len(grew)} grew by more than 40 characters')
for title, rows in (('lost their sentence', lost), ('grew', grew)):
    for s, b, a in rows[:show]:
        print(f'  [{title}] {s[:140]}\n      5.12.0:    {b[:140]}\n      prototype: {a[:140]}')
