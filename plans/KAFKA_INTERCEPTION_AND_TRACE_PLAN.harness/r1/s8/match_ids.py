"""S8: does each Kafka produce entry carry the ids of a KafkaPublish span the app started?

    python match_ids.py <interactions.json> <report> <probe.txt or -> <tool.exe>

Reads the entries' addresses from `kronikol query interactions --json`, asks `kronikol query http` for each (the
"trace X span Y" line), and compares with the probe's "<trace> <span>" lines. Prints counts and the first misses.
"""
import json
import re
import subprocess
import sys

listing, report, probe, tool = sys.argv[1:5]
items = json.load(open(listing, encoding='utf-8'))['items']
spans = set()
if probe != '-':
    spans = {tuple(line.split()) for line in open(probe, encoding='utf-8') if line.strip()}

matched, other, none, misses = 0, 0, 0, []
for item in items:
    out = subprocess.run([tool, 'query', 'http', report, item['address'], '--max-bytes', '2000'],
                         capture_output=True, text=True, encoding='utf-8').stdout
    found = re.search(r'^trace ([0-9a-f]{32}) span ([0-9a-f]{16})', out, re.M)
    if not found:
        none += 1
        misses.append((item['address'], 'no trace line'))
    elif found.groups() in spans:
        matched += 1
    else:
        other += 1
        misses.append((item['address'], ' '.join(found.groups())))
print(f'entries {len(items)}: a KafkaPublish span {matched}, another span {other}, no ids {none}; probe spans {len(spans)}')
for address, why in misses[:5]:
    print('  ', address, why)
