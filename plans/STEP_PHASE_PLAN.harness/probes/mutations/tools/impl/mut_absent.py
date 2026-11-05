"""Which mutants' text is absent at the worktree's tip, with the first edit that does not match. Usage: mut_absent.py <worktree>."""
import os, sys
sys.path.insert(0, r'C:/Code/Kronikol-224/plans/STEP_PHASE_PLAN.harness/probes/mutations/tools')
os.environ.setdefault('MUT', 'x')
import mutations as m9, mutations10 as m10, mutations11 as m11
wt = sys.argv[1]
mutants = {}
for mod in (m9, m10, m11):
    mutants.update(mod.MUTANTS)
here, gone = [], []
for mid, mu in mutants.items():
    miss = None
    for e in mu['edits']:
        path, old = e[0], e[1]
        text = open(os.path.join(wt, path), encoding='utf-8', newline='').read()
        if old not in text:
            miss = (path, old)
            break
    if miss:
        gone.append((mid, mu['desc'][:60], miss[0].split('/')[-1], ' '.join(miss[1].split())[:110]))
    else:
        here.append(mid)
print('PRESENT', len(here), ' '.join(here))
for g in gone:
    print('\t'.join(g))
