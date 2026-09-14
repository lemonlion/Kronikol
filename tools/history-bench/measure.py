import json, re, sys, os, hashlib, glob
from collections import Counter

GUID = re.compile(r'^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')
NUM  = re.compile(r'^\d+$')
HEX  = re.compile(r'^[0-9a-fA-F]{16,}$')
ULID = re.compile(r'^[0-9A-HJKMNP-TV-Z]{26}$')
ISO  = re.compile(r'^\d{4}-\d{2}-\d{2}([T_].*)?$')
B64  = re.compile(r'^[A-Za-z0-9_\-]{22,}={0,2}$')

def segclass(s):
    if GUID.match(s): return 'guid'
    if NUM.match(s):  return 'numeric'
    if ULID.match(s): return 'ulid'
    if ISO.match(s):  return 'iso'
    if HEX.match(s):  return 'longhex'
    if B64.match(s):  return 'base64ish'
    return 'literal'

def split_uri(u):
    # strip scheme://host, keep path + query
    m = re.match(r'^[a-zA-Z][a-zA-Z0-9+.\-]*://[^/]+(/.*)?$', u)
    tail = (m.group(1) or '/') if m else u
    path, _, query = tail.partition('?')
    return [s for s in path.split('/') if s], query

def template(u, policy):
    segs, query = split_uri(u)
    out = []
    for s in segs:
        c = segclass(s)
        if policy == 'none':
            out.append(s)
        elif policy == 'guid':
            out.append('{guid}' if c == 'guid' else s)
        else:  # proposed
            out.append(s if c == 'literal' else '{' + c + '}')
    p = '/' + '/'.join(out)
    if query:
        if policy == 'proposed':
            keys = sorted(k.split('=')[0] for k in query.split('&') if k)
            p += '?' + '&'.join(keys)
        else:
            p += '?' + query
    return p

def scenarios(doc):
    for f in doc.get('features', []):
        for s in f.get('scenarios', []):
            yield f.get('name'), s

def shape(s, policy):
    parts = []
    for i in s.get('httpInteractions', []) or []:
        u = i.get('uri') or ''
        parts.append('%s|%s|%s|%s|%s' % (
            i.get('callerName') or '', i.get('serviceName') or '',
            i.get('method') or '', template(u, policy), i.get('statusCode') or ''))
    return hashlib.sha256('\n'.join(parts).encode()).hexdigest()[:8], len(parts)

def analyse(path):
    doc = json.load(open(path, encoding='utf-8-sig'))
    scs = list(scenarios(doc))
    segcount = Counter(); uris = set(); volatile_uris = {p: set() for p in ('none','guid','proposed')}
    inter = 0
    for _, s in scs:
        for i in s.get('httpInteractions', []) or []:
            u = i.get('uri') or ''
            if not u: continue
            inter += 1
            uris.add(u)
            segs, _q = split_uri(u)
            for seg in segs: segcount[segclass(seg)] += 1
    return doc, scs, segcount, uris, inter

def fmt(n): return '{:,}'.format(n)

for path in sys.argv[1:]:
    doc, scs, segcount, uris, inter = analyse(path)
    name = os.path.basename(os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(path)))))
    total_seg = sum(segcount.values())
    print('=== %s  (%s, %.1f MB)' % (name, os.path.basename(path), os.path.getsize(path)/1e6))
    print('  scenarios %s | interactions %s | distinct URIs %s' % (fmt(len(scs)), fmt(inter), fmt(len(uris))))
    print('  path segments %s: %s' % (fmt(total_seg), ', '.join('%s=%s(%.1f%%)' % (k,fmt(v),100*v/total_seg) for k,v in segcount.most_common())))
    # distinct templated URI counts per policy
    for pol in ('none','guid','proposed'):
        print('  distinct templated URIs [%s]: %s' % (pol, fmt(len({template(u,pol) for u in uris}))))
    # per-scenario shape stability under a hypothetical value change: compare policies
    sh_none  = {}; sh_guid = {}; sh_prop = {}
    for fname, s in scs:
        key = s.get('stableId') or s.get('id')
        sh_none[key] = shape(s,'none'); sh_guid[key] = shape(s,'guid'); sh_prop[key] = shape(s,'proposed')
    withcalls = sum(1 for k,v in sh_prop.items() if v[1] > 0)
    print('  scenarios with >=1 interaction: %s ; mean calls %.1f ; max %s'
          % (fmt(withcalls), (sum(v[1] for v in sh_prop.values())/max(withcalls,1)), max(v[1] for v in sh_prop.values())))
    print()
