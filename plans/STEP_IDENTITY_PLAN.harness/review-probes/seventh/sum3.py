import subprocess,sys,re
R7=sys.argv[1]; F=sys.argv[2]
for name,pre in (('rv7-bd','bd'),('rv7-xu','rv7-xu'),('rv7-lb','rv7-lb')):
    for b in ('on','off'):
        a=f"{R7}/{pre}-2720-{b}.txt"; p=f"{R7}/{pre}-pub-{b}.txt"; f=f"{F}/{name}-{b}.txt"
        out=subprocess.run([sys.executable,f"{R7}/cmp3.py",'-a',f,a,p],capture_output=True,text=True,encoding='utf-8',errors='replace').stdout
        blocks=re.split(r'(?m)^--- ',out)[1:]
        n=len(blocks); same=0; d=[]
        for bl in blocks:
            t=bl.split('\n')[0][:70]
            g={m[0]:m[1].strip() for m in re.findall(r'(?m)^\s+(f9|2720|pub)\s+(.*)$',bl)}
            if g.get('f9')==g.get('2720'): same+=1
            else: d.append((t,g))
        print(f"{name} bars {b}: {n} scenarios, {same} read as 2720, {len(d)} differ")
        for t,g in d[:12]:
            print('  ',t); print('     final',g.get('f9','')[:90]); print('     2720 ',g.get('2720','')[:90]); print('     pub  ',g.get('pub','')[:90])
