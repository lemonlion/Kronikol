"""plans/STEP_PHASE_PLAN.md 6, on the code as it lands: each mutant in mut_port must fail a fact.

Usage: mut_facts_port.py <worktree> <out file> [ids...]. For each mutant: apply its edits, build the test projects, run the facts around phases and the boundary, put the files back.
A mutant whose text is not there is listed as absent. A file STOP beside the out file ends the run between mutants.
"""
import os
import re
import subprocess
import sys
import time

wt, out = sys.argv[1], sys.argv[2]
only = set(sys.argv[3:])
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mut_port  # noqa: E402
mutants = dict(mut_port.MUTANTS)

CORE = '|'.join('FullyQualifiedName~' + name for name in (
    'StepPhase', 'StepBoundary', 'PhaseConfigurationTests', 'TestPhaseContextTests', 'BDDfy', 'ReqNRoll', 'TestTrackingMessageHandlerTests',
    'StepCollectorTests', 'FeatureSynthesizer', 'BackgroundAttribution', 'DiagnosticReportGenerator'))
PROJECTS = [
    ('core', 'tests/Kronikol.Tests', CORE),
    ('weave', 'tests/Kronikol.Tests.StepTracking', 'FullyQualifiedName~StepPhase'),
    ('lightbdd', 'tests/Kronikol.Tests.LightBDD.xUnit2', 'FullyQualifiedName~StepPhase'),
    ('nodecorator', 'tests/Kronikol.Tests.LightBDD.xUnit3', 'FullyQualifiedName~StepPhase'),
]


def run(args, timeout=1200):
    return subprocess.run(args, cwd=wt, capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=timeout)


def facts():
    """Builds and runs each project in turn, stopping at the first that fails: (project, failing names), or a build error."""
    for name, project, flt in PROJECTS:
        b = run(['dotnet', 'build', project, '-v', 'q', '-nologo', '--no-restore'])
        if b.returncode != 0:
            errors = sorted(set(re.findall(r'error [A-Z0-9]+: [^\[\r\n]*', b.stdout)))
            return name, ['BUILD: ' + e for e in errors[:3]]
        t = run(['dotnet', 'test', project, '--no-build', '-nologo', '--filter', flt])
        failed = re.findall(r'^\s+Failed (\S+)', t.stdout, re.M)
        if failed:
            return name, failed
        if t.returncode != 0:
            return name, ['EXIT ' + str(t.returncode) + ' ' + t.stdout[-300:]]
    return None, []


def apply(edits):
    originals = {}
    for edit in edits:
        path, old, new = edit[0], edit[1], edit[2]
        full = os.path.join(wt, path)
        text = open(full, encoding='utf-8', newline='').read()
        originals.setdefault(full, text)
        if old not in text:
            return originals, False
        text = text.replace(old, new, 1)
        open(full, 'w', encoding='utf-8', newline='').write(text)
    return originals, True


def restore(originals):
    for full, text in originals.items():
        open(full, 'w', encoding='utf-8', newline='').write(text)


def log(line):
    with open(out, 'a', encoding='utf-8') as f:
        f.write(line + '\n')


ids = [k for k in mutants if not only or k in only]
log(f'# {time.strftime("%H:%M")} {len(ids)} mutants')
project, failed = facts()
log(f'baseline\t{"RED " + project + " " + ", ".join(failed[:4]) if failed else "green"}')
if failed:
    raise SystemExit('the facts fail before any mutant')
for mutant_id in ids:
    if os.path.exists(os.path.join(os.path.dirname(out), 'STOP')):
        log('# stopped')
        break
    mutant = mutants[mutant_id]
    originals, applied = apply(mutant['edits'])
    try:
        if not applied:
            log(f'{mutant_id}\tABSENT\t{mutant["desc"][:90]}')
            continue
        started = time.time()
        project, failed = facts()
        verdict = 'KILLED' if failed else 'SURVIVED'
        shown = ', '.join(f.split('.')[-1][:70] for f in failed[:4])
        log(f'{mutant_id}\t{verdict}\t{project or ""}\t{len(failed)}\t{int(time.time() - started)}s\t{mutant["desc"][:90]}\t{shown}')
    finally:
        restore(originals)
log(f'# {time.strftime("%H:%M")} done')
