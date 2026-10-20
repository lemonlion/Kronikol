# Looks for sources that still run 5.0.0's engine out of stack in node. usage: python -I overflow-probe.py <render.js> <engine dir>
import subprocess, sys

render, engine = sys.argv[1], sys.argv[2]


def draw(src):
    p = subprocess.run(['node', render, engine + '/viz-global.js', engine + '/plantuml.js'], input=src.encode('utf-8'),
                       capture_output=True, timeout=600)
    out = p.stdout.decode('utf-8', 'replace')
    err = p.stderr.decode('utf-8', 'replace')
    if 'Maximum call stack' in out or 'too much recursion' in out:
        return ('STACK PICTURE' if '<svg' in out else 'STACK TEXT') + ' ' + str(len(out))
    if '<svg' not in out:
        return 'NO SVG (' + (err.strip().splitlines() or [''])[-1][:140] + ')'
    if 'Syntax Error' in out:
        return 'SYNTAX ERROR'
    return 'drawn %d bytes' % len(out)


def seq(body):
    return '@startuml\n' + body + '\n@enduml\n'


cases = []
for n in (500, 2000):
    cases.append(('nested groups %d' % n, seq('group g\n' * n + 'a -> b: x\n' + 'end\n' * n)))
for n in (300, 1500):
    cases.append(('nested if %d' % n, '@startuml\nstart\n' + 'if (c) then (y)\n' * n + ':x;\n' + 'endif\n' * n + 'stop\n@enduml\n'))
for n in (20000, 200000):
    cases.append(('function recursion %d' % n, seq('!function $f($n)\n!if $n > 0\n!return $f($n - 1)\n!endif\n!return 0\n!endfunction\na -> b: $f(%d)' % n)))
for n in (5000, 50000):
    cases.append(('nested bold %d' % n, seq('a -> b: ' + '<b>' * n + 'x' + '</b>' * n)))
for name, src in cases:
    print(name, '->', draw(src), flush=True)
