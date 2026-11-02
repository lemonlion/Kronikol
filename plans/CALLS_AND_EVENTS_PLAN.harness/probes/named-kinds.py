"""Every broker test fact that names or asserts a kind, so a name that says Event over an assertion of Default shows.

usage, from a checkout holding the patches: python <harness>/probes/named-kinds.py
"""
import glob
import os
import re

roots = ['SQS', 'SNS', 'EventBridge', 'StorageQueues', 'PubSub', 'EventHubs', 'Kafka', 'MassTransit', 'ServiceBus']
pat = re.compile(r'public\s+(?:async\s+)?(?:Task|void)\s+(\w+)\s*\(')
for r in roots:
    for f in sorted(glob.glob(f'tests/Kronikol.Tests.{r}/**/*.cs', recursive=True)):
        n = f.replace(os.sep, '/')
        if '/obj/' in n or '/bin/' in n:
            continue
        s = open(f, encoding='utf-8').read()
        ms = list(pat.finditer(s))
        for i, m in enumerate(ms):
            body = s[m.end(): ms[i + 1].start() if i + 1 < len(ms) else len(s)]
            name = m.group(1)
            ev = 'RequestResponseMetaType.Event' in body
            de = 'RequestResponseMetaType.Default' in body
            if ev or de or re.search(r'(?i)metatype|_event|default_kind', name):
                print(os.path.relpath(f, 'tests').replace(os.sep, '/'), name, 'Event' if ev else '', 'Default' if de else '')
