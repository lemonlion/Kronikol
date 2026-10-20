# Mutation check of the binder's handling of the anchors 5.0.0's engine draws (plans/WASM_ENGINE_PLAN.md): each
# mutation removes one behaviour, IflowLinkKeyboardTests runs against it, and the render script is put back from its
# copy. A mutation the tests do not fail is reported as SURVIVED. usage: python -I mutate-anchors.py <repo root> <log>
import os, shutil, subprocess, sys

root, log = sys.argv[1], sys.argv[2]
script = root + '/src/Kronikol/Reports/plantuml-browser-render-script.js'
backup = script + '.mutation-backup'
shutil.copyfile(script, backup)

MUTATIONS = [
    ('the title is kept', "                a.removeAttribute('title');\n", ''),
    ('a plain xlink:title is kept', "                a.removeAttribute('xlink:title');\n", ''),
    ('a namespaced xlink:title is kept', "                a.removeAttributeNS('http://www.w3.org/1999/xlink', 'title');\n", ''),
    ('the anchors are not handed to the binder',
     "bindLink(anchorTexts[segId], segId, anchorEls[segId]); });", "bindLink(anchorTexts[segId], segId); });"),
    ('the first anchor is not named',
     "                    anchors[0].setAttribute('aria-label', groupEls.map(function(el) { return el.textContent; }).join(' '));\n", ''),
    ('the later anchors keep their stops', "                        a.setAttribute('tabindex', '-1');\n", ''),
    ('the later anchors are read out', "                        a.setAttribute('aria-hidden', 'true');\n", ''),
    ('every anchor loses its stop', "                    anchors.slice(1).forEach(function(a) {\n",
     "                    anchors.slice(0).forEach(function(a) {\n"),
    ('focus does not show the link', "                        anchors[0].addEventListener('focus', showLink);\n", ''),
    ('leaving does not put the link at rest', "                        anchors[0].addEventListener('blur', atRest);\n", ''),
    # Expected to survive: binding twice adds the same listeners and the same name again, which no reader can see.
    ('the anchors are bound each time', "                if (anchors && anchors.length > 0 && !anchors[0]._iflowKeys) {\n",
     "                if (anchors && anchors.length > 0) {\n"),
]


def run(project, filt):
    p = subprocess.run(['dotnet', 'test', project, '-nodeReuse:false', '-p:UseSharedCompilation=false', '--filter', filt],
                       cwd=root, capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=3600)
    summary = [l.strip() for l in p.stdout.splitlines() if 'Passed!' in l or 'Failed!' in l or 'error CS' in l]
    return p.returncode, summary


results = []
try:
    for name, old, new in MUTATIONS:
        text = open(backup, 'rb').read().decode('utf-8')
        assert text.count(old) == 1, name
        open(script, 'wb').write(text.replace(old, new).encode('utf-8'))
        e2e = run('tests/Kronikol.Tests.EndToEnd/Kronikol.Tests.EndToEnd.csproj', 'FullyQualifiedName~IflowLinkKeyboardTests')
        results.append('%s: %s | %s' % ('KILLED' if e2e[0] != 0 else 'SURVIVED', name, e2e[1]))
        with open(log, 'a', encoding='utf-8') as f:
            f.write(results[-1] + '\n')
finally:
    shutil.copyfile(backup, script)
    os.remove(backup)
print('\n'.join(results))
