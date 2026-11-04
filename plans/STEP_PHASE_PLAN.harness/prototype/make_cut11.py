"""Cut 11: the sixth review's fixes on cut 10, by stage.
Run on a tree with a stage's cut 10 patch applied: python -I make_cut11.py <tree> <r1|r2|r3> <path of review6/apply_fix.py>. Each stage is cumulative."""
import subprocess
import sys

tree, stage, fix = sys.argv[1], sys.argv[2], sys.argv[3]
r2 = stage in ('r2', 'r3')

SC = 'src/Kronikol/Tracking/StepCollector.cs'
TH = 'src/Kronikol/Tracking/TestTrackingMessageHandler.cs'


def edit(path, *pairs):
    full = f'{tree}/{path}'
    with open(full, encoding='utf-8', newline='') as f:
        text = f.read()
    for old, new in pairs:
        if old not in text:
            raise SystemExit(f'not found in {path}: {old[:90]}')
        text = text.replace(old, new, 1)
    with open(full, 'w', encoding='utf-8', newline='') as f:
        f.write(text)


# F-T7, F-T3, F-T9 at every stage; F-T1, F-T2 from R2 on.
subprocess.run([sys.executable, '-I', fix, tree, 'r1' if stage == 'r1' else 'r2'], check=True)

# Dead since T9: nothing asks for a test's own chain any more.
edit(SC,
     ('    private static FlowStep? ChainOf(string testId)\n    {\n        var node = CurrentFlowStep.Value;\n        while (node is not null && node.TestId != testId)\n            node = node.Parent;\n        return node;\n    }\n\n', ''))

if r2:
    # A shared handler now asks its fetcher for each test, so whatever the fetcher throws must not fail a call that passes today, and a second
    # client's first call past the boundary still reads the phase the fetcher named for it.
    edit(TH,
         ('        catch (InvalidOperationException)\n        {\n            return;\n        }\n',
          '        catch (Exception)\n        {\n            return;\n        }\n'),
         ('        // A fetcher can throw for a call outside a scenario, as LightBDD\'s does, and a call under a TestIdentityScope is one. A call\n',
          '        // A fetcher can throw for a call outside a scenario, as LightBDD\'s does, or on a test it does not know, as a shared handler now asks for each. A call\n'))
    edit(SC,
         ('            state.ClientsPastBoundary.Add(client);\n            if (state.ActionStartDrawn)\n                return;\n',
          '            state.ClientsPastBoundary.Add(client);\n            if (state.ActionStartDrawn)\n            {\n                TestPhaseContext.Current = TestPhase.Action;\n                return;\n            }\n'))
print('cut 11 applied,', stage)
