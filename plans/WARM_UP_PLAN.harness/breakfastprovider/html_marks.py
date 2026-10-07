"""S7 for R2 (plans/WARM_UP_PLAN.md 6.6): the page draws the marks its data file records, and no others.

    python html_marks.py <reports dir>

Reads TestRunReport.json and TestRunReport.html (and Specifications.html when there is one) without printing either:
every scenario the data file gives `warmUpSeconds` must carry `data-warmup-ms` with the same milliseconds and a muted
note in its badge on each page, a scenario without it must carry neither, and a page with a mark must carry the
warm-up rules once. A scenario drawn inside a parameterized group is checked through the group's sum. Exit 1 on any
difference.
"""
import html
import json
import os
import re
import sys

root = sys.argv[1]
with open(os.path.join(root, 'TestRunReport.json'), encoding='utf-8-sig') as f:
    report = json.load(f)
want = {}
for feature in report.get('features') or []:
    for scenario in feature.get('scenarios') or []:
        want[scenario['stableId']] = round((scenario.get('warmUpSeconds') or 0) * 1000)

problems = 0
for page in ('TestRunReport.html', 'Specifications.html'):
    path = os.path.join(root, page)
    if not os.path.exists(path):
        continue
    text = open(path, encoding='utf-8').read()
    drawn = {}
    for tag in re.finditer(r'<details class="scenario[^"]*"[^>]*>', text):
        attrs = dict(re.findall(r'([\w-]+)="([^"]*)"', tag.group(0)))
        if 'data-stable-id' in attrs:
            drawn[attrs['data-stable-id']] = int(attrs.get('data-warmup-ms', '0'))
    groups = [int(m.group(1)) for m in re.finditer(r'<details class="scenario scenario-parameterized[^"]*"[^>]*?data-warmup-ms="(\d+)"', text)]
    notes = len(re.findall(r'<span class="warm-up-note">', text))
    rules = text.count('.timeline-warm-up {')
    marked = {k: v for k, v in want.items() if v > 0}
    individually = {k: v for k, v in marked.items() if k in drawn}
    grouped = {k: v for k, v in marked.items() if k not in drawn}
    wrong = [(k, v, drawn[k]) for k, v in individually.items() if abs(drawn[k] - v) > 1]
    extra = [k for k, v in drawn.items() if v > 0 and want.get(k, 0) == 0]
    ok = not wrong and not extra and notes == len(individually) + len(groups) and rules == (1 if notes else 0) \
        and abs(sum(groups) - sum(grouped.values())) <= len(groups) + 1
    problems += not ok
    print(f'{page}: {len(marked)} marked in the data ({len(individually)} drawn alone, {len(grouped)} in {len(groups)} groups), '
          f'{notes} notes, warm-up rules {rules}x, wrong {len(wrong)}, unmarked drawn {len(extra)}: {"equal" if ok else "DIFFERS"}')
    for k, v, d in wrong[:10]:
        print(f'  {k}: data {v} ms, page {d} ms')
    for k in extra[:10]:
        print(f'  {k}: drawn with a warm-up the data does not record')
sys.exit(1 if problems else 0)
