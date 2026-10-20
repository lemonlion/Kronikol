# Which form 5.0.0's engine fails in when it runs out of stack: text, or its error picture naming a line.
# usage: python -I overflow-probe2.py <render.js> <engine dir> <outdir>
import re, subprocess, sys

render, engine, outdir = sys.argv[1], sys.argv[2], sys.argv[3]


def draw(name, src):
    p = subprocess.run(['node', render, engine + '/viz-global.js', engine + '/plantuml.js'], input=src.encode('utf-8'),
                       capture_output=True, timeout=600)
    out = p.stdout.decode('utf-8', 'replace')
    err = p.stderr.decode('utf-8', 'replace')
    open(outdir + '/' + re.sub(r'\W+', '-', name) + '.out', 'w', encoding='utf-8').write(out + '\n---stderr---\n' + err)
    stack = 'Maximum call stack' in out or 'too much recursion' in out or 'Maximum call stack' in err
    if '<svg' in out:
        texts = [re.sub(r'<[^>]*>', '', t) for t in re.findall(r'<text\b[^>]*>[^<]*</text>', out)]
        first = texts[0] if texts else ''
        last = texts[-1] if texts else ''
        return ('PICTURE' if stack else 'svg') + ' first=%r last=%r lines=%r' % (first[:40], last[-90:], [t for t in texts if 'line' in t][:2])
    return ('TEXT ' if stack else 'NO SVG ') + (err.strip().splitlines() or [''])[-1][:120]


comp = '@startuml\nleft to right direction\nrectangle "Caller" as caller\ndatabase "Warehouse" as warehouse\ncaller --> warehouse : "%s"\n@enduml\n'
cases = [
    ('component nested bold 5000', comp % ('<b>' * 5000 + 'x' + '</b>' * 5000)),
    ('json nested 5000', '@startjson\n' + '[' * 5000 + ']' * 5000 + '\n@endjson\n'),
    ('nested preprocessor if 5000', '@startuml\n' + '!if 1\n' * 5000 + 'a -> b: x\n' + '!endif\n' * 5000 + '@enduml\n'),
    ('sequence nested groups 3000', '@startuml\n' + 'group g\n' * 3000 + 'a -> b: x\n' + 'end\n' * 3000 + '@enduml\n'),
    ('participant nested bold 5000', '@startuml\nparticipant "' + '<b>' * 5000 + 'x' + '</b>' * 5000 + '" as P\nP -> P: x\n@enduml\n'),
]
for name, src in cases:
    print(name, '->', draw(name, src), flush=True)
