"""Writes a multi-line corpus (extract_multiline.py) as the weave reads each statement: its lines trimmed and joined by
one space (AssertionWeaver.CutColumns), so the probes see the text a woven assertion passes to Track.

Usage: python join_lines.py <multiline.txt> > joined.txt
"""
import re
import sys

for line in open(sys.argv[1], encoding='utf-8'):
    line = line.rstrip('\n')
    if line.startswith('#'):
        print(line)
    else:
        print(re.sub(r'\s*\\n\s*', ' ', line))
