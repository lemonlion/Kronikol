# Draws over-long statements on an engine through Kronikol's Node renderer and says which drew a diagram, which drew
# the engine's error picture, and which drew nothing. usage: python -I limits.py <plantuml-render.js> <engine dir>
import subprocess, sys

render, engine = sys.argv[1], sys.argv[2]


def draw(body):
    src = '@startuml\n' + body + '\n@enduml\n'
    p = subprocess.run(['node', render, engine + '/viz-global.js', engine + '/plantuml.js'], input=src.encode('utf-8'),
                       capture_output=True, timeout=300)
    out = p.stdout.decode('utf-8', 'replace')
    err = p.stderr.decode('utf-8', 'replace')
    if '<svg' not in out:
        return 'NO SVG (' + (err.strip().splitlines() or [''])[-1][:100] + ')'
    if 'Maximum call stack' in out or 'too much recursion' in out:
        return 'STACK PICTURE'
    if 'Syntax Error' in out:
        return 'SYNTAX ERROR'
    return 'drawn %d bytes' % len(out)


cases = []
for n in (2000, 2100, 8000, 16000, 32000, 64000):
    cases.append(('loop label %d' % n, 'a -> b: x\nloop ' + 'l' * n + '\na -> b: y\nend'))
for n in (6000, 20000, 60000):
    cases.append(('coloured bar %d' % n, 'a -> b: x\nhnote across <<stepDelimiter>> #black:<color:white>' + 's' * n))
for n in (2000, 2001, 4000):
    cases.append(('message %d' % n, 'a -> b: ' + 'm' * n))
for n in (500, 2000, 8000, 32000):
    cases.append(('participant name %d' % n, 'participant "' + 'p' * n + '" as P\nP -> P: x'))
for name, body in cases:
    print(name, '->', draw(body), flush=True)
