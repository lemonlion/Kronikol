"""Apply or revert a named source mutation in this worktree, for the plan's red proofs.

    python mutate.py apply <name> [<name> ...]
    python mutate.py revert            # restores every file a mutation touched, from git

Each mutation is an exact-string replacement that must match once. Nothing here is shipped.
"""
import io, subprocess, sys, os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..'))

MUTATIONS = {
    # F1: each anchored assertion must fail when the piece it reads is missing.
    'f1-diag-summary': ('src/Kronikol/Reports/ReportGenerator.cs',
        'var summary = $"Report diagnostics ({diagnostics.Count}',
        'var summary = $"Diagnostics ({diagnostics.Count}'),
    'f1-diag-kind': ('src/Kronikol/Reports/ReportGenerator.cs',
        'var kindClass = $"report-diagnostic-kind report-diagnostic-kind-{',
        'var kindClass = $"report-diagnostic-kind report-diagnostic-kindx-{'),
    'f1-diag-none': ('src/Kronikol/Reports/ReportGenerator.cs',
        '        if (diagnostics.Count == 0)\n            return string.Empty;\n\n        var byKind',
        '        if (diagnostics.Count >= 0)\n            return string.Empty;\n\n        var byKind'),
    'f1-sparkline': ('src/Kronikol/History/HistoryHtml.cs',
        'return $" <span class=\\"history-sparkline\\" role',
        'return $" <span class=\\"history-sparklinx\\" role'),
    'f1-clusters': ('src/Kronikol/Reports/ReportGenerator.cs',
        'body.Append($"<details class=\\"failure-clusters\\"{',
        'body.Append($"<details class=\\"failure-clusterz\\"{'),
}

def apply(name):
    path, a, b = MUTATIONS[name]
    p = os.path.join(ROOT, path)
    raw = io.open(p, 'rb').read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    s = raw.decode('utf-8-sig')
    n = s.count(a)
    if n != 1:
        sys.exit(f'{name}: expected one match in {path}, found {n}')
    s = s.replace(a, b)
    io.open(p, 'w', encoding='utf-8-sig' if bom else 'utf-8', newline='').write(s)
    print(f'applied {name} to {path}')

def revert():
    files = sorted({m[0] for m in MUTATIONS.values()})
    subprocess.check_call(['git', '-C', ROOT, 'checkout', '--', *files])
    print('reverted', ', '.join(files))

if __name__ == '__main__':
    if sys.argv[1] == 'apply':
        for n in sys.argv[2:]:
            apply(n)
    elif sys.argv[1] == 'revert':
        revert()
