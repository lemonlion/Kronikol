#!/usr/bin/env python3
"""Prints the `run: |` body of one step of a workflow shown in a Markdown page, dedented, byte for byte.

Usage: extract_recipe.py <page.md> <step name> [<out.sh>]

The wiki's manual recipe (Cross-Run-History.md, "Without the action") is run by the scenarios from the page's own
text, so what the harness runs is what the page says: recipe_manual_read.sh and recipe_manual_record.sh are this
script's output, and `extract_recipe.py <wiki page> "<step>" | diff - recipe_manual_<x>.sh` says whether the page
and the harness still agree.
"""
import sys

page, step = sys.argv[1], sys.argv[2]
lines = open(page, encoding='utf-8').read().split('\n')

start = next((i for i, l in enumerate(lines) if l.strip() == f'- name: {step}'), None)
if start is None:
    sys.exit(f'no step named {step!r} in {page}')
step_indent = len(lines[start]) - len(lines[start].lstrip()) + 2
run = next((i for i in range(start + 1, len(lines))
            if lines[i].strip() == 'run: |' and len(lines[i]) - len(lines[i].lstrip()) == step_indent), None)
if run is None:
    sys.exit(f'step {step!r} has no "run: |" block')

body = []
for line in lines[run + 1:]:
    if line.strip() == '' or line.startswith('```'):
        break
    if len(line) - len(line.lstrip()) <= step_indent:
        break
    body.append(line)
indent = min(len(l) - len(l.lstrip()) for l in body)
text = '\n'.join(l[indent:] for l in body) + '\n'

if len(sys.argv) > 3:
    with open(sys.argv[3], 'w', encoding='utf-8', newline='\n') as f:
        f.write(text)
else:
    sys.stdout.buffer.write(text.encode('utf-8'))  # bytes: a Windows console would write each line end as CRLF
