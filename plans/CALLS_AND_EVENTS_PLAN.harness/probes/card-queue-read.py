# What a service.json says about each call and message. args: <service.json>...
import json
import sys


def walk(node, found):
    if isinstance(node, dict):
        if 'op' in node:
            found.append({k: node.get(k) for k in ('op', 'method', 'target', 'kind') if k in node})
        for key, value in node.items():
            if key == 'messages':
                found.append({'messages': value})
            else:
                walk(value, found)
    elif isinstance(node, list):
        for item in node:
            walk(item, found)


for path in sys.argv[1:]:
    data = json.load(open(path, encoding='utf8'))
    found = []
    walk(data, found)
    print('==', path.replace('\\', '/').split('/')[-2])
    for item in found:
        print(json.dumps(item, sort_keys=True)[:170])
    print()
