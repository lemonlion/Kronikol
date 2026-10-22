"""Writes results/<lane>/ids.tsv from results/<lane>/query-scenarios-all.json (kronikol query scenarios --json
--max-bytes 0): feature, example values as sorted JSON, scenario name with a line feed written as the two characters
backslash and n, stable id. Run from this folder: python -I tsv.py before-5.4.0 cucumber-lane-5.4.0"""
import json
import sys

for lane in sys.argv[1:]:
    with open(f'results/{lane}/query-scenarios-all.json', encoding='utf-8') as f:
        d = json.load(f)
    rows = sorted((i['feature'], json.dumps(i.get('exampleValues'), ensure_ascii=False, sort_keys=True),
                   i['scenario'].replace('\n', '\\n'), i['stableId']) for i in d['items'])
    with open(f'results/{lane}/ids.tsv', 'w', encoding='utf-8', newline='\n') as f:
        f.writelines('\t'.join(r) + '\n' for r in rows)
    print(lane, len(rows), 'rows')
