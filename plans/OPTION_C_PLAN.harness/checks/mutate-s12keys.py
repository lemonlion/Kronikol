# S12 keyboard links' mutations: each undoes one part of the change, rebuilds, runs IflowLinkKeyboardTests, expects it
# to FAIL, and puts the file back. usage: python mutate-s12keys.py [part of a test name]   |   --check [root]
import os, subprocess, sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..')).replace(os.sep, '/') + '/'
R = 'src/Kronikol/Reports/plantuml-browser-render-script.js'
E2E = 'tests/Kronikol.Tests.EndToEnd/Kronikol.Tests.EndToEnd.csproj'
T = 'Kronikol.Tests.EndToEnd.IflowLinkKeyboardTests'
MUTATIONS = [
    ("no Tab stop", R, "                    first.setAttribute('tabindex', '0');\n", "", E2E, T),
    ("no link role", R, "                    first.setAttribute('role', 'link');\n", "", E2E, T),
    ("named from its first text only", R,
     "first.setAttribute('aria-label', groupEls.map(function(el) { return el.textContent; }).join(' '));",
     "first.setAttribute('aria-label', first.textContent);", E2E, T),
    ("Enter opens nothing", R, "if (ev.key !== 'Enter' && ev.key !== ' ') return;", "if (ev.key !== ' ') return;", E2E, T),
    ("Space opens nothing", R, "if (ev.key !== 'Enter' && ev.key !== ' ') return;", "if (ev.key !== 'Enter') return;", E2E, T),
    ("a Java-engine link gets a second stop", R, "if (!anchored && !first._iflowKeys) {", "if (!first._iflowKeys) {", E2E, T),
    ("focus does not show a hover-only link", R,
     "                        first.addEventListener('focus', function() {\n",
     "                        first.addEventListener('focus-never', function() {\n", E2E, T),
    ("leaving does not put it at rest", R, "                        first.addEventListener('blur', atRest);\n", "", E2E, T),
    ("the stop on the link's last text", R, "var first = groupEls[0], anchor", "var first = groupEls[groupEls.length - 1], anchor", E2E, T),
]

if len(sys.argv) > 1 and sys.argv[1] == '--check':
    base = (sys.argv[2].rstrip('/') + '/') if len(sys.argv) > 2 else ROOT
    bad = 0
    for name, path, old, new, project, test in MUTATIONS:
        n = open(base + path, encoding='utf-8', newline='').read().count(old)
        if n != 1:
            bad += 1
            print(f'NOT ONCE ({n}): {name}')
    print(f'{len(MUTATIONS) - bad} of {len(MUTATIONS)} anchors found once')
    sys.exit(1 if bad else 0)

ONLY = sys.argv[1] if len(sys.argv) > 1 else ''
MUTATIONS = [m for m in MUTATIONS if ONLY in m[5]]
killed = 0
for name, path, old, new, project, test in MUTATIONS:
    s = open(ROOT + path, encoding='utf-8', newline='').read()
    assert s.count(old) == 1, (name, s.count(old))
    open(ROOT + path, 'w', encoding='utf-8', newline='').write(s.replace(old, new))
    try:
        r = subprocess.run(['dotnet', 'test', ROOT + project, '--filter', 'FullyQualifiedName~' + test,
                            '-nodeReuse:false', '-p:UseSharedCompilation=false'],
                           capture_output=True, text=True, creationflags=getattr(subprocess, 'BELOW_NORMAL_PRIORITY_CLASS', 0))
        failed = r.returncode != 0 and 'Failed!' in r.stdout
        built = 'error CS' not in r.stdout and 'Build FAILED' not in r.stdout
        killed += failed and built
        print(('KILLED  ' if failed and built else 'SURVIVED') + ' ' + name + ('' if built else ' (build failed)'), flush=True)
    finally:
        open(ROOT + path, 'w', encoding='utf-8', newline='').write(s)
print(f'{killed} of {len(MUTATIONS)} killed')
sys.exit(0 if killed == len(MUTATIONS) else 1)
