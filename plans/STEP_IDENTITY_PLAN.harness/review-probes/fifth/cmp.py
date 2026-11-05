import re,sys
def split(path):
    cases={}
    cur=None
    for line in open(path,encoding='utf-8',errors='replace'):
        m=re.match(r'=+ (\S+) \((pub|this)\)',line)
        if m:
            cur=m.group(1); cases[cur]=[]
            continue
        if cur: cases[cur].append(line.rstrip('\n'))
    return cases
a=split(sys.argv[1]); b=split(sys.argv[2])
for k in a:
    if a[k]==b.get(k):
        print('SAME',k)
    else:
        print('DIFF',k)
        import difflib
        for l in difflib.unified_diff(a[k],b[k],lineterm='',n=0,fromfile='pub',tofile='this'):
            if l.startswith('@@'): continue
            print('   ',l[:170])
