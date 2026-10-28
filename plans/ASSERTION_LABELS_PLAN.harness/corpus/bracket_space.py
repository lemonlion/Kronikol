"""Counts a probe output's labels that hold a space after an opening bracket, a space before a closing one or a line
break, outside literals and outside the "[ x => ... ]" wrapper the formatter writes around a lambda on purpose.

Usage: python bracket_space.py <probe-output> [--list]
"""
import re
import sys

labels = [l[len('    => '):] for l in open(sys.argv[1], encoding='utf-8').read().splitlines() if l.startswith('    => ')]
hits = []
for label in labels:
    bare = re.sub(r'@?\$?"(?:[^"\\]|\\.)*"', '""', label)
    bare = re.sub(r"'(?:[^'\\]|\\.)*'", "''", bare)
    bare = re.sub(r' \[ (.*=>.*) \]$', r' <\1>', bare)
    if re.search(r'[(\[] |\s[)\]]', bare) or '\\n' in bare:
        hits.append(label)
print(f'{len(hits)} of {len(labels)} labels hold a space inside a bracket or a line break')
if '--list' in sys.argv:
    for h in hits:
        print('    ' + h)
