"""What "warmup.py must follow" costs under Q5 (b): the spec, its stored marks, and the spec with MessageTable's publish rows ported.

usage: PYTHONIOENCODING=utf-8 python probes/warmup-spec-cost.py <worktree> [<folder for the synthetic lane>]

Reads plans/WARM_UP_PLAN.harness/warmup.py and its corpus from the worktree, and edits neither: the port is applied in this process,
over the 'test' field load() set. No consumer data is printed, only counts and generic labels.
"""
import glob
import gzip
import importlib.util
import json
import os
import re
import sys
from collections import Counter

tree = sys.argv[1]
harness = os.path.join(tree, 'plans', 'WARM_UP_PLAN.harness')
stored_folder = os.path.join(tree, 'tests', 'Kronikol.Tests', 'TestData', 'WarmUp')

spec = importlib.util.spec_from_file_location('warmup', os.path.join(harness, 'warmup.py'))
warmup = importlib.util.module_from_spec(spec)
spec.loader.exec_module(warmup)

# PORT BEGIN
# The publish rows of src/Kronikol/CallGraph/MessageTable.cs, and the two labels of 'neither' that extend one of them. Nothing
# holds this to the table: its broker tests hold the table to each classifier, and no test runs this script.
PUBLISHES = {
    'kafka': ['Produce', 'ProduceAsync'],
    'eventhubs': ['Send', 'SendBatch'],
    'servicebus': ['Send', 'SendBatch', 'Schedule'],
    'pubsub': ['Publish', 'PublishBatch'],
    'sqs': ['SendMessage', 'SendMessageBatch'],
    'sns': ['Publish', 'PublishBatch'],
    'storagequeue': ['SendMessage', 'Send'],
    'masstransit': ['Send', 'Publish', '→'],
    'eventbridge': ['PutEvents', 'PutPartnerEvents'],
}
FAULTS = {'masstransit': ['Send Fault', 'Publish Fault']}


def leads(label, prefix):
    return label.startswith(prefix) and (len(label) == len(prefix) or label[len(prefix)] in ' ([→←\t')


def is_publish(uri, label):
    colon = (uri or '').find('://')
    scheme = uri[:colon].lower() if colon > 0 else None
    text = (label or '').strip()
    longest_publish = max((len(p) for p in PUBLISHES.get(scheme, []) if leads(text, p)), default=-1)
    longest_fault = max((len(p) for p in FAULTS.get(scheme, []) if leads(text, p)), default=-1)
    return longest_publish > longest_fault
# PORT END


def port_lines():
    text = open(__file__, encoding='utf-8').read()
    body = text[text.index('# PORT BEGIN'):text.index('# PORT END')].splitlines()[1:]
    return len([line for line in body if line.strip()])


def ported(scenarios, calls):
    for sc in scenarios:
        services = {c['service'] for c in sc['calls']}
        for c in sc['calls']:
            c['test'] = c['caller'] not in services and (c['meta'] != 'Event' or is_publish(c['uri'], c['method']))
    return scenarios, calls


def marks(calls):
    return {c['rid']: (c['warm'][0], c['name'], round(c['warm'][1], 6), c['warm'][2]) for c in warmup.detect(calls, 'P')}


print('== 1. The stored marks against the script as it stands')
want = json.load(open(os.path.join(stored_folder, 'expected-marks.json'), encoding='utf-8'))
for lane, loader in warmup.lanes_under(stored_folder):
    scenarios, calls = warmup.load(loader)
    marked = warmup.detect(calls, 'P')
    got = {
        'calls': {c['rid']: {'kind': c['warm'][0], 'shape': c['name'], 'baselineMs': c['warm'][1], 'baselineCalls': c['warm'][2],
                             'first': c['warm'][3] if len(c['warm']) > 3 else None} for c in marked},
        'scenarios': {sc['id']: warmup.warm_up_ms(sc) for sc in scenarios if warmup.warm_up_ms(sc) > 0},
    }
    same = got == want.get(lane)
    print(f'{lane}: {len(got["calls"])} marks, equal to expected-marks.json: {same}')
print()

print('== 2. Every lane of the corpus: test-made events, and what the ported rows change')
print(f'{"lane":<58}{"calls":>7}{"events":>8}{"test-made":>11}{"publishes":>11}{"marks":>7}{"ported":>8}{"differ":>8}')
roots = [os.path.join(harness, 'corpus'), stored_folder]
totals = Counter()
seen_labels = Counter()
for root in roots:
    for lane, loader in warmup.lanes_under(root):
        scenarios, calls = warmup.load(loader)
        before = marks(calls)
        events = [c for c in calls if c['meta'] == 'Event']
        test_made = []
        for sc in scenarios:
            services = {c['service'] for c in sc['calls']}
            test_made += [c for c in sc['calls'] if c['meta'] == 'Event' and c['caller'] not in services]
        publishes = [c for c in test_made if is_publish(c['uri'], c['method'])]
        for c in test_made:
            scheme = (c['uri'] or '').split('://')[0] if '://' in (c['uri'] or '') else '(none)'
            seen_labels[(scheme, (c['method'] or '').split(' ')[0])] += 1

        scenarios2, calls2 = warmup.load(loader)
        ported(scenarios2, calls2)
        for c in calls2:
            c.pop('warm', None)
        after = marks(calls2)
        differ = len(set(before.items()) ^ set(after.items()))
        name = os.path.basename(root) + '/' + lane
        print(f'{name[:57]:<58}{len(calls):>7}{len(events):>8}{len(test_made):>11}{len(publishes):>11}{len(before):>7}{len(after):>8}{differ:>8}')
        totals.update(calls=len(calls), events=len(events), test_made=len(test_made), publishes=len(publishes), marks=len(before), after=len(after), differ=differ, lanes=1)
print(f'{"all " + str(totals["lanes"]) + " lanes":<58}{totals["calls"]:>7}{totals["events"]:>8}{totals["test_made"]:>11}{totals["publishes"]:>11}{totals["marks"]:>7}{totals["after"]:>8}{totals["differ"]:>8}')
print()
print('Test-made events by scheme and leading word of the label, over every lane:')
for (scheme, word), n in sorted(seen_labels.items()):
    print(f'  {scheme}:// {word}: {n}')
print()

print('== 3. A lane holding a publish the test makes, as a report under R2 writes one')
lane = {'kronikolVersion': 'synthetic', 'suite': 'publish', 'scenarios': []}
ids = {}
for index, (broker, uri, label) in enumerate([
        ('sqs', 'sqs:///orders', 'SendMessage'),
        ('kafka', 'kafka:///orders', 'Produce → orders'),
        ('servicebus', 'servicebus://orders/', 'Send → orders')]):
    calls = []
    for n, (start_ms, duration) in enumerate([(0, 600.0), (700, 5.0), (800, 5.0)]):
        rid = f'00000000-0000-0000-0000-00000000{index}{n}00'
        calls.append({'rid': rid, 'caller': 'Test', 'service': broker, 'method': label, 'uri': uri, 'meta': 'Event', 'category': 'MessageQueue',
                      'start': f'2026-10-05T09:48:13.{start_ms:03d}Z' if start_ms < 1000 else None, 'durationMs': duration, 'status': 200})
    lane['scenarios'].append({'feature': 'Publishes', 'name': broker, 'id': f'scenario-{index}', 'result': 'Passed', 'durationSeconds': 1.0,
                              'endedAt': '2026-10-05T09:48:14.000Z', 'calls': calls})

out_folder = sys.argv[2] if len(sys.argv) > 2 else None
synthetic = (lambda: json.loads(json.dumps(lane)))
scenarios, calls = warmup.load(synthetic)
plain = marks(calls)
scenarios2, calls2 = ported(*warmup.load(synthetic))
with_port = marks(calls2)
print(f'the script as it stands marks {len(plain)} of its 3 slow first publishes; with the rows ported, {len(with_port)}')
if out_folder:
    os.makedirs(out_folder, exist_ok=True)
    with gzip.open(os.path.join(out_folder, 'publish.calls.json.gz'), 'wt', encoding='utf-8') as fh:
        json.dump(lane, fh)
    for name, found, cs, ss in [('unported', plain, calls, scenarios), ('ported', with_port, calls2, scenarios2)]:
        doc = {'publish': {
            'calls': {c['rid']: {'kind': c['warm'][0], 'shape': c['name'], 'baselineMs': c['warm'][1], 'baselineCalls': c['warm'][2],
                                 'first': c['warm'][3] if len(c['warm']) > 3 else None} for c in cs if 'warm' in c},
            'scenarios': {s['id']: warmup.warm_up_ms(s) for s in ss if warmup.warm_up_ms(s) > 0}}}
        json.dump(doc, open(os.path.join(out_folder, f'publish.expected-{name}.json'), 'w', encoding='utf-8'), indent=1, sort_keys=True)
print()

print('== 4. The size of the port')
print(f'{port_lines()} lines of Python (the block between PORT BEGIN and PORT END in this file), plus the one condition at warmup.py:147')
print(f'{sum(len(v) for v in PUBLISHES.values())} publish labels over {len(PUBLISHES)} schemes, and {sum(len(v) for v in FAULTS.values())} fault labels')
