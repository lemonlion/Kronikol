# Checks that every mutation's pattern occurs exactly once in the worktree's scripts, without building anything.
import importlib.util, sys
spec = importlib.util.spec_from_file_location('m', sys.argv[1])
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)
bad = 0
for name, (edits, facts) in m.MUTATIONS.items():
    for path, old, new in edits:
        n = open(path, 'rb').read().count(old.encode())
        if n != 1:
            bad += 1
            print('STALE', name, path.split('/')[-1], n, repr(old[:70]))
print('checked', len(m.MUTATIONS), 'mutations,', bad, 'stale')
