import json, sys
d = json.load(open(sys.argv[1], encoding='utf-8'))
libs = sorted(k for k in d['libraries'] if k.lower().startswith('kronikol'))
print('assets:', ' '.join(libs))
