"""Writes the issue's messages with one step result changed, for the probes the issue's tests name."""
import json
lines = [json.loads(l) for l in open("messages.ndjson", encoding='utf-8') if l.strip()]
def write(name, results, attach_on=None):
    out = []
    for env in lines:
        fin = env.get('testStepFinished')
        if fin:
            sid = fin['testStepId'].rsplit('-', 1)[-1]
            if fin['testStepId'].endswith(tuple(f'step-{i}' for i in range(3))):
                i = int(sid)
                status, msg = results[i]
                r = {'duration': fin['testStepResult']['duration'], 'status': status}
                if msg: r['message'] = msg
                fin['testStepResult'] = r
        out.append(json.dumps(env, separators=(',', ':')))
        st = env.get('testStepStarted')
        if attach_on is not None and st and st['testStepId'].endswith(f'step-{attach_on}'):
            out.append(json.dumps({'attachment': {'testCaseStartedId': st['testCaseStartedId'], 'testStepId': st['testStepId'],
                'mediaType': 'text/plain', 'fileName': 'kronikol-bypass', 'body': 'Bypassed on dev: mock Gemini is not in the deployed path',
                'contentEncoding': 'IDENTITY', 'timestamp': st['timestamp']}}, separators=(',', ':')))
    open(name, 'w', encoding='utf-8', newline='\n').write('\n'.join(out) + '\n')
R = 'Bypassed on dev: mock Gemini is not in the deployed path'
write('messages-ffs.ndjson', [('FAILED', 'Error: expected 3 to be 4'), ('SKIPPED', None), ('SKIPPED', None)])
write('messages-last.ndjson', [('PASSED', None), ('PASSED', None), ('SKIPPED', R)])
write('messages-attach.ndjson', [('PASSED', None), ('PASSED', None), ('PASSED', None)], attach_on=1)
