# After 5.0.0 is on NuGet and everything that waited for it is out: the plans and the harness notes say where each part
# was published. usage: python -I published-500.py <repo root> <release run> <wiki base> <wiki head> <kronikol4j commit>
#   <breakfastprovider commit> <breakfastprovider run>
import sys

root, run, wiki_base, wiki_head, k4j, bp, bp_run = sys.argv[1:8]


def edit(rel, pairs):
    p = root + '/' + rel
    t = open(p, 'rb').read().decode('utf-8')
    assert '\r' not in t, rel
    for old, new in pairs:
        assert t.count(old) == 1, (rel, t.count(old), old[:100])
        t = t.replace(old, new)
    open(p, 'wb').write(t.encode('utf-8'))


def append(rel, line):
    p = root + '/' + rel
    t = open(p, 'rb').read().decode('utf-8')
    assert '\r' not in t, rel
    open(p, 'wb').write((t.rstrip('\n') + '\n' + line + '\n').encode('utf-8'))


append('plans/WASM_ENGINE_PLAN.md',
       '- 2026-10-20, published: `v5.0.0` tagged at `f2f71790`, on NuGet from the Release workflow\'s run ' + run + ';\n'
       '  the wiki\'s engine pages with the 5.0.0 series (wiki `' + wiki_head + '`), the Kronikol4J ledger entry (`' + k4j + '`),\n'
       '  and BreakfastProvider on 5.0.0 (`' + bp + '`, its CI run ' + bp_run + ').')
append('plans/OPTION_C_PLAN.md',
       '- 2026-10-20, published with 5.0.0: the wiki series as the live wiki\'s `' + wiki_base + '..' + wiki_head + '`, each\n'
       '  subject reworded "5.0.0: ..." and one commit on top moving the pages\' package references to 5.0.0 (they named\n'
       '  4.14.11, and Assertion Tracking 4.*); the Kronikol4J ledger entries as `' + k4j + '`, kronikol-39\'s in the form it\n'
       '  sent that day, with its code names in backticks.')
edit('plans/OPTION_C_PLAN.harness/wiki/NOTES.md', [
    ("""2026-10-20). Nothing in it is pushed: the wiki describes 5.0.0 only once 5.0.0 ships, and pushing is the owner's call.""",
     """2026-10-20). Published on 2026-10-20, once 5.0.0 was on NuGet, as the live wiki's `""" + wiki_base + """..""" + wiki_head + """`: each
subject reworded "5.0.0: ...", the author set to the live wiki's, and one commit on top moving the pages' package
references from 4.14.11 and 4.* to 5.0.0 and 5.* (`plans/WASM_ENGINE_PLAN.harness/wiki/wiki-publish.sh`)."""),
])
edit('plans/OPTION_C_PLAN.harness/kronikol4j-ledger.md', [
    ("""# Kronikol4J ledger entry for Option C
""",
     """# Kronikol4J ledger entry for Option C

Published on 2026-10-20 with 5.0.0, as Kronikol4J's `""" + k4j + """` (the three entries below, in this order, after the 4.32.1
entry). The references held at the port's `1a62e28`: no file they cite changed after `92d6b28`.
"""),
])
print('ok')
