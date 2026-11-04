#!/usr/bin/env python
"""Stamps the Reqnroll suites from reqnroll-xunit2's Steps.cs and Phases.feature, with the nested shapes, on each test framework.
Usage: python -I templates/stamp_reqnroll.py (from probes/)."""
import pathlib, re, shutil

HERE = pathlib.Path(__file__).resolve().parent
PROBES = HERE.parent
SOURCE = PROBES / 'reqnroll-xunit2'
TEMPLATE = HERE / 'reqnroll'

XUNIT_RUNNER = '''    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
'''

SUITES = {
    'reqnroll-xunit2': dict(
        adapter='Kronikol.ReqNRoll.xUnit2', assembly='Xunit.CollectionBehavior(DisableTestParallelization = true)',
        retry='xRetry.Reqnroll', fail='throw new InvalidOperationException(message);', exe=False,
        packages=['<PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.3.0" />', '<PackageReference Include="Reqnroll.xUnit" Version="3.3.4" />',
                  '<PackageReference Include="Reqnroll.Tools.MsBuild.Generation" Version="3.3.4" />', '<PackageReference Include="xunit.assert" Version="2.9.3" />',
                  '<PackageReference Include="xRetry.Reqnroll" Version="1.1.0" />'], extra=XUNIT_RUNNER),
    'reqnroll-xunit3': dict(
        adapter='Kronikol.ReqNRoll.xUnit3', assembly='Xunit.CollectionBehavior(DisableTestParallelization = true)',
        retry=None, fail='throw new InvalidOperationException(message);', exe=True,
        packages=['<PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.3.0" />', '<PackageReference Include="Reqnroll.xunit.v3" Version="3.3.4" />',
                  '<PackageReference Include="Reqnroll.Tools.MsBuild.Generation" Version="3.3.4" />', '<PackageReference Include="xunit.v3" Version="3.2.2" />'], extra=XUNIT_RUNNER),
    # No adapter package for Reqnroll on NUnit ships: the hooks are Kronikol.ReqNRoll.Core's, which no test framework appears in.
    'reqnroll-nunit': dict(
        adapter='Kronikol.ReqNRoll.Core', assembly=None,
        retry='NUnitRetry.ReqnrollPlugin', fail='NUnit.Framework.Assert.Fail(message);', exe=False,
        packages=['<PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.3.0" />', '<PackageReference Include="Reqnroll.NUnit" Version="3.3.4" />',
                  '<PackageReference Include="Reqnroll.Tools.MsBuild.Generation" Version="3.3.4" />', '<PackageReference Include="NUnit" Version="4.6.0" />',
                  '<PackageReference Include="NUnit3TestAdapter" Version="6.2.0" />',
                  '<PackageReference Include="NUnitRetry.ReqnrollPlugin" Version="2.1.0" />'], extra=''),
    'reqnroll-tunit': dict(
        adapter='Kronikol.ReqNRoll.TUnit', assembly=None,
        retry=None, fail='throw new InvalidOperationException(message);', exe=True,
        packages=['<PackageReference Include="Reqnroll.TUnit" Version="3.3.4" />', '<PackageReference Include="TUnit" Version="1.53.0" />',
                  '<PackageReference Include="Reqnroll.Tools.MsBuild.Generation" Version="3.3.4" />'], extra=''),
}

# Taken once from the harness's copy, kept apart so re-stamping starts from what the harness has.
BASE_STEPS = TEMPLATE / 'Steps.base.cs'
BASE_FEATURE = TEMPLATE / 'Phases.base.feature'
if not BASE_STEPS.exists():
    shutil.copy(SOURCE / 'Steps.cs', BASE_STEPS)
    shutil.copy(SOURCE / 'Features' / 'Phases.feature', BASE_FEATURE)

steps = BASE_STEPS.read_text(encoding='utf-8').replace('\r\n', '\n')

for name, s in SUITES.items():
    folder = PROBES / name
    (folder / 'Features').mkdir(parents=True, exist_ok=True)
    text = steps.replace('[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]\n',
                         f"[assembly: {s['assembly']}]\n" if s['assembly'] else '')
    text = text.replace('Dump.Write("reqnroll-xunit2");', f'Dump.Write(Environment.GetEnvironmentVariable("PHASE_PROBE_NAME") ?? "{name}");')
    (folder / 'Steps.cs').write_text(text, encoding='utf-8', newline='\n')
    shutil.copy(BASE_FEATURE, folder / 'Features' / 'Phases.feature')
    shutil.copy(TEMPLATE / 'Nested.feature', folder / 'Features' / 'Nested.feature')
    shutil.copy(TEMPLATE / 'NestedSteps.cs', folder / 'NestedSteps.cs')
    if s['retry']:
        shutil.copy(TEMPLATE / 'Retried.feature', folder / 'Features' / 'Retried.feature')
        (folder / 'RetriedSteps.cs').write_text(
            'using System.Collections.Concurrent;\nusing Reqnroll;\n\nnamespace PhaseProbe.Reqnroll;\n\n[Binding]\npublic class RetriedSteps\n{\n'
            '    private static readonly ConcurrentDictionary<string, int> Attempts = new();\n\n'
            '    [Then("the first attempt of (.*) fails")]\n    public void TheFirstAttemptFails(string name)\n    {\n'
            '        if (Attempts.AddOrUpdate(name, 1, (_, n) => n + 1) == 1)\n        {\n'
            '            var message = "the first attempt fails";\n            ' + s['fail'] + '\n        }\n    }\n}\n',
            encoding='utf-8', newline='\n')
    (folder / 'reqnroll.json').write_text(
        '{\n  "$schema": "https://schemas.reqnroll.net/reqnroll-config-latest.json",\n  "bindingAssemblies": [ { "assembly": "%s" } ]\n}\n' % s['adapter'],
        encoding='utf-8', newline='\n')
    project = name.replace('reqnroll-', 'Probe.Reqnroll.') + '.csproj'
    for old in folder.glob('*.csproj'):
        old.unlink()
    package_lines = ''.join(f'    {p}\n' for p in s['packages'])
    (folder / project).write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
{"  <PropertyGroup>" + chr(10) + "    <OutputType>Exe</OutputType>" + chr(10) + "  </PropertyGroup>" + chr(10) if s['exe'] else ''}  <ItemGroup>
    <PackageReference Include="{s['adapter']}" Version="$(KronikolVersion)" />
    <PackageReference Include="Kronikol.StepTracking" Version="$(KronikolVersion)" PrivateAssets="all" />
{package_lines}{s['extra']}  </ItemGroup>
  <ItemGroup>
    <None Include="reqnroll.json" />
    <AdditionalFiles Include="reqnroll.json" />
    <Compile Include="..\\shared\\*.cs" />
  </ItemGroup>
</Project>
''', encoding='utf-8', newline='\n')
    print('stamped', name)
