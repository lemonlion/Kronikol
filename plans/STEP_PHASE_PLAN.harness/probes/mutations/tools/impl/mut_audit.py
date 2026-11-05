"""plans/STEP_PHASE_PLAN.md 9, the audit's paint facts: each mutant below must fail the fact it names.

Usage: mut_audit.py <worktree> <out file>. For each mutant: apply its edit, build the E2E project, run StepBoundaryPaintTests, put the
failing names in the out file, and restore the source. A mutant whose text is not there is listed as absent.
"""
import re
import subprocess
import sys

wt, out = sys.argv[1], sys.argv[2]
COLLECTOR = 'src/Kronikol/Tracking/StepCollector.cs'

MUTANTS = [
    # Q5: an explicit StartAction() inside the When logs no marker of its own, so the frame stays at the When.
    ('a1', 'A_StartAction_inside_the_When_moves_the_frame_past_what_came_before_it', COLLECTOR,
     '            var first = !state.ExplicitBoundaryDrawn && !superseded;\n',
     '            var first = false && !state.ExplicitBoundaryDrawn && !superseded;\n'),
    # Q9: a bypass takes the boundary back, so the next When draws it again and the Given before that When falls in the frame.
    ('a2', 'A_bypassed_When_closes_the_frame_and_a_Given_after_it_stays_on_the_page', COLLECTOR,
     '            bypassed.Bypassed = true;\n',
     '            bypassed.Bypassed = true;\n'
     '            if (bypassed.SaysPhase) { state.ActionStartDrawn = false; state.ActionStepSeen = false; state.Phase = TestPhase.Setup; }\n'),
    # Q13: a Given's end closes the frame, so the body's calls after it stay on the page.
    ('a3', 'A_When_in_tear_down_after_a_body_with_no_When_closes_the_frame_after_the_body', COLLECTOR,
     '        if (testId is null)\n            return;\n\n        if (!States.TryGetValue(testId, out var state))\n            return;\n\n        CollectedStep? completed;\n',
     '        if (testId is null)\n            return;\n\n        if (!States.TryGetValue(testId, out var state))\n            return;\n\n'
     '        if (state.Open.LastOrDefault() is { EffectiveKeyword: "Given" } && !state.ActionStartDrawn)\n'
     '        {\n            lock (state) { state.ActionStartDrawn = true; state.BoundaryLogged = true; }\n'
     '            DefaultTrackingDiagramOverride.DrawBoundary(testId, false);\n        }\n\n        CollectedStep? completed;\n'),
]


def run(args, timeout=1500):
    return subprocess.run(args, cwd=wt, capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=timeout)


lines = []
for mid, target, path, old, new in MUTANTS:
    p = f'{wt}/{path}'
    original = open(p, encoding='utf-8', newline='').read()
    if original.count(old) != 1:
        lines.append(f'{mid} ABSENT ({original.count(old)} matches)')
        continue
    try:
        open(p, 'w', encoding='utf-8', newline='').write(original.replace(old, new, 1))
        b = run(['dotnet', 'build', 'tests/Kronikol.Tests.EndToEnd', '-c', 'Release', '-v', 'q', '-nologo'])
        if b.returncode != 0:
            errors = sorted(set(re.findall(r'error [A-Z0-9]+: [^\[\r\n]*', b.stdout)))
            lines.append(f'{mid} BUILD {errors[:2]}')
            continue
        t = run(['dotnet', 'test', 'tests/Kronikol.Tests.EndToEnd', '-c', 'Release', '--no-build', '-nologo',
                 '--filter', 'FullyQualifiedName~StepBoundaryPaintTests'])
        failed = sorted(set(n.rsplit('.', 1)[-1] for n in re.findall(r'^\s+Failed (\S+)', t.stdout, re.M)))
        verdict = 'KILLED' if target in failed else 'SURVIVED'
        lines.append(f'{mid} {verdict} (target {target}); failing: {", ".join(failed) or "none"}')
    finally:
        open(p, 'w', encoding='utf-8', newline='').write(original)
    open(out, 'w', encoding='utf-8', newline='\n').write('\n'.join(lines) + '\n')

open(out, 'w', encoding='utf-8', newline='\n').write('\n'.join(lines) + '\n')
print('\n'.join(lines))
