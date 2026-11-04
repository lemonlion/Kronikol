"""Which tests vary by themselves over the baseline runs of a suite. Usage: racy_report.py <file name> <dir> [<dir> ...]"""
import os
import sys
import dumps
name, dirs = sys.argv[1], sys.argv[2:]
paths = [os.path.join(d, name) for d in dirs if os.path.exists(os.path.join(d, name))]
seen = dumps.variants(paths)
racing = {t: c for t, c in seen.items() if len(c) > 1}
print(f"{name}: {len(paths)} dumps, {len(seen)} tests, {len(racing)} vary")
for t, c in sorted(racing.items()):
    print(f"  {t[:100]}  ({'+'.join(str(v) for v in c.values())})")
