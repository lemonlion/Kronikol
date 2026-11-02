import json,sys
M=sys.argv[1]
i=int(sys.argv[2])
open(M+'/in-tests.ndjson','w').write('\n'.join(json.dumps(x) for x in [
 {"event":"start","testId":"t1","testName":"Row","feature":"F","timestamp":"2026-11-01T10:00:0%d+00:00"%i,"outlineId":"Row","exampleValues":{"a":None}},
 {"event":"end","testId":"t1","status":"passed","durationMs":5,"timestamp":"2026-11-01T10:00:0%d.5+00:00"%i},
 {"event":"start","testId":"t2","testName":"Row","feature":"F","timestamp":"2026-11-01T10:00:0%d+00:00"%i,"outlineId":"Row","exampleValues":{"a":"x"}},
 {"event":"end","testId":"t2","status":"passed","durationMs":5,"timestamp":"2026-11-01T10:00:0%d.5+00:00"%i},
])+'\n')
open(M+'/in-calls.ndjson','w').write(json.dumps({"testId":"t1","testName":"Row","type":"Request","method":"GET","uri":"http://h/a","serviceName":"Svc","callerName":"Caller","traceId":"6f2c5b30-0000-4000-8000-000000000001","requestResponseId":"6f2c5b30-0000-4000-8000-000000000002","timestamp":"2026-11-01T10:00:0%d.1+00:00"%i})+'\n')
