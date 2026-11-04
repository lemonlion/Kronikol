import sys
p = sys.argv[1]
t = open(p, encoding='utf-8').read()
old = """for folder, s in SUITES.items():
"""
new = """LEGACY = dict(SUITES['rev5-lightbdd-x2'])
LEGACY['name'] = 'rev5-lightbdd-legacy'
LEGACY['scope'] = LEGACY['scope'].replace('configuration.CreateStandardReportsWithDiagrams(', '''#pragma warning disable CS0618
        configuration.ReportWritersConfiguration().CreateStandardReportsWithDiagrams(''').replace('SeparateSetup = true });', '''SeparateSetup = true });
#pragma warning restore CS0618''')
SUITES['rev5-lightbdd-legacy'] = LEGACY

for folder, s in SUITES.items():
"""
assert old in t
open(p, 'w', encoding='utf-8', newline='\n').write(t.replace(old, new))
print('ok')
