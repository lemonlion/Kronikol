# OPTION_C_PLAN.md's header: 5.0.0 is four pieces, TAGS_PLAN.md the fourth (TAGS_PLAN.md section 7 asks for it).
# usage: python header-four-pieces.py <root>
import sys

p = sys.argv[1].rstrip('/\\') + '/plans/OPTION_C_PLAN.md'
s = open(p, encoding='utf-8', newline='').read()
old = '''5.0.0 is therefore three pieces of work on one branch: this plan, the dependency filter's OR default (`9452ddb5`,
already on `v5`, under `[Unreleased]` in the changelog), and the report size reduction (`REPORT_FORMAT_PLAN.md`,
written in its own worktree, `plan/report-format`). The version moves to 5.0.0 when the branch is released, not
before; until then the changelog collects its entries under `[Unreleased]`.
'''
new = '''5.0.0 is therefore four pieces of work on one branch: this plan, the dependency filter's OR default (`9452ddb5`,
already on `v5`, under `[Unreleased]` in the changelog), the report size reduction (`REPORT_FORMAT_PLAN.md`, written
in its own worktree, `plan/report-format`), and labels and categories as one list of tags (`TAGS_PLAN.md`, put in
5.0.0 by the owner on 2026-10-19 and built on the branch `v5-tags`, which `v5` fast-forwards to). The version moves
to 5.0.0 when the branch is released, not before; until then the changelog collects its entries under `[Unreleased]`.
'''
assert '\r' not in s, 'CR line endings here'
assert s.count(old) == 1, ('the header is not the one this edit was written for', s.count(old))
assert all(len(line) <= 120 for line in new.splitlines()), 'a line is wider than 120'
open(p, 'w', encoding='utf-8', newline='').write(s.replace(old, new))
print('ok: header')
