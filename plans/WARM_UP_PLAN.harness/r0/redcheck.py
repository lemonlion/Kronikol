"""Per new fact: its outcome on v4.6.0 and the first line of each distinct failure message.

  python redcheck.py <trx> <worktree> <base-sha> [<test-project-dir> ...]

A fact is "new" when the branch adds its method (an added line declaring a public void/Task method under [Fact] or
[Theory]) in a test file. Theory rows are collapsed per method; distinct first lines of their messages are listed.
"""
import collections
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

trx, worktree, base = sys.argv[1], sys.argv[2], sys.argv[3]
dirs = sys.argv[4:] or ['tests']

diff = subprocess.run(['git', '-C', worktree, 'diff', '-U0', base, 'HEAD', '--', *dirs],
                      capture_output=True, text=True, encoding='utf-8').stdout
new = set()
for line in diff.splitlines():
    m = re.match(r'^\+\s*public (?:async )?(?:void|Task|ValueTask)\s+(\w+)\s*\(', line)
    if m:
        new.add(m.group(1))

ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
root = ET.parse(trx).getroot()
outcomes = collections.defaultdict(lambda: collections.Counter())
messages = collections.defaultdict(collections.OrderedDict)
for r in root.iterfind('.//t:UnitTestResult', ns):
    name = r.get('testName')
    method = re.sub(r'\(.*$', '', name).split('.')[-1]
    if method not in new:
        continue
    key = re.sub(r'\(.*$', '', name)
    outcomes[key][r.get('outcome')] += 1
    msg = r.find('.//t:Message', ns)
    if msg is not None and msg.text:
        first = msg.text.strip().splitlines()[0][:300]
        messages[key][first] = messages[key].get(first, 0) + 1

seen = {re.sub(r'\(.*$', '', k).split('.')[-1] for k in outcomes}
for key in sorted(outcomes):
    print(f"{key}  {dict(outcomes[key])}")
    for first, n in list(messages[key].items())[:4]:
        print(f"    x{n}: {first}")
missing = sorted(new - seen)
if missing:
    print("\nnew methods not in this trx:", ', '.join(missing))
