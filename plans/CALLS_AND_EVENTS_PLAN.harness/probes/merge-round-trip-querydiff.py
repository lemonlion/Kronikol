# `kronikol query interactions --group-by` on a real report before and after `kronikol merge`, on both tools, over every
# dimension history reads. Prints counts, and the classes of what moved, with no consumer label unless it is a verb in
# another case (which the plan already names). args: <published tool dll> <prototype tool dll> <work dir> <report file>...
import json, os, shutil, subprocess, sys

published, prototype, work, *reports = sys.argv[1:]
DIMS = ['service,kind,method,status', 'caller,service,method,status', 'service,method,route,status', 'service,method,path,status', 'category,kind,method', 'service,capturedBy,phase']


def query(tool, report, dims):
    out = subprocess.run(['dotnet', tool, 'query', 'interactions', report, '--group-by', dims, '--json', '--max-bytes', '0'],
                         stdout=subprocess.PIPE, stderr=subprocess.PIPE, encoding='utf-8', errors='replace')
    envelope = json.loads(out.stdout)
    rows = {}
    for item in envelope['items']:
        key = tuple(sorted(item['key'].items()))
        rows[key] = (item['calls'], item['errors'], item.get('bodies'), item.get('bytes'))
    return rows, envelope.get('truncated')


def classify(before, after):
    only_before = {k: v for k, v in before.items() if k not in after}
    only_after = {k: v for k, v in after.items() if k not in before}
    moved = [k for k in before if k in after and before[k] != after[k]]
    casing = 0
    others = []
    for key, value in only_before.items():
        fold = tuple((a, b.upper() if a == 'method' and isinstance(b, str) else b) for a, b in key)
        pair = [k for k in only_after if tuple((a, b.upper() if a == 'method' and isinstance(b, str) else b) for a, b in k) == fold]
        if pair:
            casing += 1
        else:
            others.append(key)
    return dict(rows_before=len(before), rows_after=len(after), only_before=len(only_before), only_after=len(only_after), numbers_moved=len(moved), method_casing_pairs=casing, unexplained=len(others)), moved, others


for report in reports:
    name = os.path.basename(os.path.dirname(os.path.abspath(report)))
    print(f'\n=== {name}')
    for label, tool in (('5.27.13', published), ('prototype', prototype)):
        out_dir = os.path.join(work, 'querydiff', name, label)
        shutil.rmtree(out_dir, ignore_errors=True)
        os.makedirs(out_dir)
        merged = os.path.join(out_dir, 'merged.html')
        r = subprocess.run(['dotnet', tool, 'merge', report, '-o', merged], stdout=subprocess.PIPE, stderr=subprocess.STDOUT, encoding='utf-8', errors='replace')
        print(f'-- {label}: merge exit {r.returncode}')
        data = [f for f in os.listdir(out_dir) if f in ('merged.json', 'merged.krkl')][0]
        data = os.path.join(out_dir, data)
        for dims in DIMS:
            before, t1 = query(tool, report, dims)
            after, t2 = query(tool, data, dims)
            summary, moved, others = classify(before, after)
            print(f'   {dims:34} {summary}' + (' TRUNCATED' if t1 or t2 else ''))
            for key in moved[:5]:
                print('      moved:', dict(key), before[key], '->', after[key])
            for key in others[:5]:
                print('      only before:', [a for a, _ in key])
