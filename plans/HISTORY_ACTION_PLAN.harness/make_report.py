#!/usr/bin/env python3
"""Writes the TestRunReport.json a run's fragment stands beside, so `kronikol history gate` can read it.

Usage: make_report.py <History.run.json>

The report goes into the fragment's own directory. Its scenarios are the fragment's roster, in roster
order, each with the result the fragment's run line gives it (P passed, F failed, anything else skipped),
and its ciMetadata carries the run number and attempt of the fragment's id (gh:<run>:<attempt>). It is the
shape HistoryGateTests writes by hand: only what the gate reads.
"""
import json, os, sys

fragment_path = sys.argv[1]
fragment = json.load(open(fragment_path, encoding='utf-8'))
roster, run = fragment['roster'], fragment['run']
_, run_number, attempt = run['id'].split(':')
names = {'P': 'Passed', 'F': 'Failed'}

features = {}
for i, sid in enumerate(roster['ids']):
    letter = run['results'][i] if i < len(run['results']) else '-'
    result = names.get(letter, 'Skipped')
    feature = roster['features'][i] or 'Feature'
    features.setdefault(feature, []).append({
        'id': f't{i}', 'stableId': sid, 'name': roster['names'][i], 'result': result,
        'durationSeconds': 0.01, 'errorMessage': 'boom' if result == 'Failed' else None,
        'labels': [], 'categories': [], 'steps': [], 'httpInteractions': []})

report = {
    'kronikolVersion': '3.31.9', 'formatVersion': 1, 'suite': roster['suite'],
    'startTime': run['at'], 'endTime': run['at'],
    'ciMetadata': {'provider': 'GitHubActions', 'buildNumber': run_number, 'branch': run.get('branch') or 'main',
                   'commitSha': 'abc1234', 'pipelineUrl': None, 'repository': 'o/r',
                   'runId': run_number, 'runAttempt': attempt},
    'features': [{'name': name, 'labels': [], 'scenarios': scenarios} for name, scenarios in features.items()],
}
out = os.path.join(os.path.dirname(fragment_path), 'TestRunReport.json')
with open(out, 'w', encoding='utf-8', newline='\n') as f:
    json.dump(report, f)
print(f'{out}: {sum(len(s) for s in features.values())} scenarios, run {run["id"]}')
