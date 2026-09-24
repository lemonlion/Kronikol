# Byte diff of two folders of generated pages, for the S3 claim ("a report without a custom sheet keeps
# its bytes"). Usage: python bytediff.py <before-dir> <after-dir>
# Ignores the run's clock (start/end rows) and normalises one checkout artifact: a git worktree on a
# core.autocrlf=true machine checks the .js resources out with CRLF, and plantuml-worker-host.js is
# embedded raw, so its JSON-escaped line breaks read \r\n in one build and \n in the other.
import difflib, os, sys

before, after = sys.argv[1], sys.argv[2]
for name in sorted(f for f in os.listdir(after) if f.endswith('.html')):
    a = open(os.path.join(before, name), encoding='utf-8').read().replace('\\r\\n', '\\n')
    b = open(os.path.join(after, name), encoding='utf-8').read().replace('\\r\\n', '\\n')
    al, bl = a.splitlines(True), b.splitlines(True)
    d = [l for l in difflib.unified_diff(al, bl, n=0)
         if l[:1] in '+-' and not l.startswith(('+++', '---'))
         and 'Start Date:' not in l and 'Start Time:' not in l and 'End Time:' not in l]
    moved = sorted(l for l in al if 'Start ' not in l and 'End Time' not in l) == sorted(l for l in bl if 'Start ' not in l and 'End Time' not in l)
    print(f'{name}: {len(a.encode())} -> {len(b.encode())} bytes, {len(d)} changed lines outside the clock'
          + (', every line kept (a pure reorder)' if moved and d else ''))
    for l in d[:4]:
        print('    ' + repr(l[:110]))
