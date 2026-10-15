"""Summarises .trx files: each test's outcome and, for a failure, its message's first line and the test file's line.

usage: python trx_summary.py <title> <trx>... > summary.txt
"""
import re
import sys
import xml.etree.ElementTree as ET

NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
title, paths = sys.argv[1], sys.argv[2:]
rows = []
for path in paths:
    root = ET.parse(path).getroot()
    for result in root.iterfind('.//t:UnitTestResult', NS):
        name = result.get('testName').split('.')[-1]
        outcome = result.get('outcome')
        message = result.findtext('t:Output/t:ErrorInfo/t:Message', default='', namespaces=NS).strip()
        stack = result.findtext('t:Output/t:ErrorInfo/t:StackTrace', default='', namespaces=NS)
        line = ''
        for frame in stack.splitlines():
            m = re.search(r'\\(\w+Tests)\.cs:line (\d+)', frame)
            if m:
                line = f'{m.group(1)}.cs:{m.group(2)}'
                break
        first = ' '.join(message.splitlines()[:1])[:160]
        rows.append((outcome, name, line, first))

print(title)
print(f'{sum(1 for r in rows if r[0] == "Failed")} failed, {sum(1 for r in rows if r[0] == "Passed")} passed, {len(rows)} in all')
print()
for outcome, name, line, first in sorted(rows, key=lambda r: (r[0] != 'Failed', r[1])):
    print(f'{"RED  " if outcome == "Failed" else "green"} {name}')
    if outcome == 'Failed':
        print(f'      at {line}: {first}')
