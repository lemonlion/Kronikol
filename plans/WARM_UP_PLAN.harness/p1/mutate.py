"""4.9.1's mutations: each fix undone on its own, and the facts that must turn red for it.

    python plans/WARM_UP_PLAN.harness/p1/mutate.py      (from the repository root)

Applies one mutation to the source, runs the named facts, records whether any failed, and puts the source back.
Prints one line per mutation; exits 1 when a mutation survives.
"""
import os
import subprocess
import sys

ROOT = os.getcwd()
WARM = 'src/Kronikol/Reports/WarmUpCalls.cs'
GEN = 'src/Kronikol/Reports/ReportGenerator.cs'
DIGEST = 'src/Kronikol/Reports/FailuresDigestGenerator.cs'

MUTATIONS = [
    ('M1 the tooltip rounds a time under a second', GEN,
     '$"{Math.Floor(ms):0} ms"', '$"{ms:0} ms"',
     'FullyQualifiedName~WarmUpHtmlTests.The_tooltip_reads_each_time_as_the_badge_writes_it'),
    ('M2 the shape upper-cases its method', WARM,
     '$"{ReportGenerator.MethodText(request.Method)} {target}"', '$"{MethodOf(request).ToUpperInvariant()} {target}"',
     'FullyQualifiedName~WarmUpCallsTests.A_shape_names_its_method_as_the_data_file_writes_it|FullyQualifiedName~WarmUpCorpusTests'),
    ('M3 calls are grouped by the written method', WARM,
     'calls.GroupBy(c => (c.Service, c.Method, c.Target))', 'calls.GroupBy(c => (c.Service, c.Shape, c.Target))',
     'FullyQualifiedName~WarmUpCallsTests.Methods_that_differ_only_in_case_are_one_shape_named_by_its_first_call'),
    ('M4 Failures.md upper-cases the method', DIGEST,
     'var method = ReportGenerator.MethodText(log.Method);', 'var method = log.Method.Value?.ToString()?.ToUpperInvariant();',
     'FullyQualifiedName~DigestCallScopeTests.A_call_is_named_by_its_method_as_the_data_file_writes_it'),
    ('M5 the verb check is case-sensitive', DIGEST,
     'new(StringComparer.OrdinalIgnoreCase)\n        { "GET"', 'new(StringComparer.Ordinal)\n        { "GET"',
     'FullyQualifiedName~DigestCallScopeTests.An_uncategorised_verb_written_in_lower_case_is_still_a_verb'),
]

survived = 0
for name, path, old, new, facts in MUTATIONS:
    source = open(path, encoding='utf-8', newline='').read()
    if source.count(old) != 1:
        print(f'{name}: the text to mutate was found {source.count(old)} times')
        survived += 1
        continue
    try:
        open(path, 'w', encoding='utf-8', newline='').write(source.replace(old, new))
        run = subprocess.run(['dotnet', 'test', 'tests/Kronikol.Tests/Kronikol.Tests.csproj', '-f', 'net10.0', '--filter', facts],
                             capture_output=True, text=True, encoding='utf-8', errors='replace')
        failed = [line.strip() for line in run.stdout.splitlines() if line.strip().startswith('Failed Kronikol')]
        built = 'error CS' not in run.stdout
        caught = built and run.returncode != 0 and failed
        survived += not caught
        print(f'{name}: {"caught" if caught else "SURVIVED" if built else "did not build"}'
              + ''.join(f'\n    {line.split(" [")[0]}' for line in failed))
    finally:
        open(path, 'w', encoding='utf-8', newline='').write(source)
sys.exit(1 if survived else 0)
