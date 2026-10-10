"""Mutations for plans/LONG_COMPONENT_EDGE_PLAN.md R2: each puts one behaviour back the way it was, or breaks it, and the
facts named must turn red. Run in a snapshot worktree of the release's working tree (never the tree itself):

    git worktree add --detach <dir> $(git stash create)
    python mutate_r2.py <dir> [M1 M2 ...]

Each mutation edits one file in place, runs its filter with dotnet test, and restores the file from memory.
"""
import os, re, subprocess, sys, json

ROOT = sys.argv[1]
PICK = set(sys.argv[2:])
CORE = 'FullyQualifiedName~NodeJsPlantUmlRendererTests|FullyQualifiedName~OnDemandRenderingReportTests'
E2E = ('FullyQualifiedName~A_diagram_the_engine_runs_out_of_stack_on_is_reported_as_a_failure'
       '|FullyQualifiedName~A_stack_failure_the_engine_writes_as_text_is_reported_as_a_failure'
       '|FullyQualifiedName~The_stack_detector_tells_the_engines_stack_failure_from_a_drawn_diagram'
       '|FullyQualifiedName~A_popup_diagram_the_engine_runs_out_of_stack_on_is_described_when_it_is_drawn')
SCRIPT = 'src/Kronikol/Reports/plantuml-browser-render-script.js'
POPUP = 'src/Kronikol/Reports/internal-flow-popup-script.js'
NODE = 'src/Kronikol/PlantUml/NodeJsPlantUmlRenderer.cs'

MUTATIONS = [
    ('M1', 'the worker caches a stack failure', SCRIPT,
     'if (isStackOverflow(m.svg)) telemetry.errors++;\n                else cachePut(job.key, m.svg);',
     'if (isStackOverflow(m.svg)) telemetry.errors++;\n                cachePut(job.key, m.svg);', [E2E]),
    ('M2', 'the worker counts no error for a stack failure', SCRIPT,
     'if (isStackOverflow(m.svg)) telemetry.errors++;\n                else cachePut',
     'if (isStackOverflow(m.svg)) {}\n                else cachePut', [E2E]),
    ('M3', 'the main thread counts no error for a stack failure', SCRIPT,
     '                if (isStackOverflow(el.innerHTML)) telemetry.errors++;\n', '', [E2E]),
    ('M4', 'the page\'s detector reads the whole picture, not its last line', SCRIPT,
     'return _stackOverflowRx.test(texts[texts.length - 1]);', 'return _stackOverflowRx.test(result);', [E2E]),
    ('M5', 'the page\'s detector without the error picture\'s first line', SCRIPT,
     "if (!texts || texts[0].replace(/<[^>]*>/g, '').indexOf('PlantUML ') !== 0) return false;",
     'if (!texts) return false;', [E2E]),
    ('M6', 'no description of a stack failure', SCRIPT,
     "if (window._isStackOverflow && window._isStackOverflow(el.innerHTML) && !el.querySelector('[data-engine-failure]')) {",
     "if (false) {", [E2E]),
    ('M7', 'the description below the engine\'s picture', SCRIPT,
     'el.insertBefore(stack, el.firstChild);', 'el.appendChild(stack);', [E2E]),
    ('M8', 'the description without the statement past its cap', SCRIPT,
     "var past = findOverLongStatement(stackSource);", "var past = null;", [E2E]),
    ('M9', 'the popup reads no stack failure', POPUP,
     'if (window._isStackOverflow && window._isStackOverflow(el.innerHTML)) {', 'if (false) {', [E2E]),
    ('M10', 'the batch returns the stack picture as a drawing', NODE,
     'results[index] = IsStackOverflow(answer)', 'results[index] = false', [CORE]),
    ('M11', 'a single render returns the stack picture as a drawing', NODE,
     '        if (IsStackOverflow(svg))\n            throw new InvalidOperationException(StackOverflowError(plantUml, svg));\n', '', [CORE]),
    ('M12', 'the error without the line the engine named', NODE,
     'var named = ErrorLineRegex().Match(result);', 'var named = System.Text.RegularExpressions.Match.Empty;', [CORE]),
    ('M13', 'the node detector reads the whole picture, not its last line', NODE,
     'return StackOverflowRegex().IsMatch(texts[^1].Value);', 'return StackOverflowRegex().IsMatch(result);', [CORE]),
    ('M14', 'the node detector without the error picture\'s first line', NODE,
     'if (texts.Count == 0 || !TagRegex().Replace(texts[0].Value, "").StartsWith("PlantUML ", StringComparison.Ordinal))',
     'if (texts.Count == 0)', [CORE]),
]

def run(filter_, project):
    cmd = ['dotnet', 'test', project, '--filter', filter_] + (['-f', 'net10.0'] if 'Kronikol.Tests.csproj' in project else [])
    p = subprocess.run(cmd, cwd=ROOT, capture_output=True, text=True, encoding='utf-8', errors='replace')
    out = p.stdout + p.stderr
    failed = sorted(set(re.findall(r'^\s+Failed (\S+?)(?:\(.*?\))? \[', out, re.M)))
    summary = re.findall(r'(Passed!|Failed!)\s+- Failed:\s+(\d+), Passed:\s+(\d+)', out)
    if 'error CS' in out or not summary:
        return {'error': (re.findall(r'.*error CS.*', out) or [out[-600:]])[:3]}
    return {'summary': summary[-1], 'failed': [f.split('.')[-1] for f in failed]}

results = []
for key, what, rel, old, new, filters in MUTATIONS:
    if PICK and key not in PICK:
        continue
    path = os.path.join(ROOT, rel)
    original = open(path, 'rb').read()
    text = original.decode('utf-8')
    if text.count(old) != 1:
        results.append({'mutation': key, 'what': what, 'error': f'the site is in the file {text.count(old)} times'})
        print(json.dumps(results[-1]), flush=True)
        continue
    open(path, 'wb').write(text.replace(old, new).encode('utf-8'))
    try:
        row = {'mutation': key, 'what': what}
        for f in filters:
            project = 'tests/Kronikol.Tests.EndToEnd' if f == E2E else 'tests/Kronikol.Tests/Kronikol.Tests.csproj'
            row['e2e' if f == E2E else 'core'] = run(f, project)
        row['killed'] = any(r.get('failed') for k, r in row.items() if k in ('core', 'e2e') and isinstance(r, dict))
        results.append(row)
        print(json.dumps(row), flush=True)
    finally:
        open(path, 'wb').write(original)

print('killed {} of {}'.format(sum(1 for r in results if r.get('killed')), len(results)))
