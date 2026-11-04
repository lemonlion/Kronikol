"""Stamps lightbdd-template/Scenarios.cs into one suite per LightBDD framework. Run from any folder: python -I stamp_lightbdd.py <probes folder>."""
import pathlib
import sys

P = pathlib.Path(sys.argv[1])
source = (P / 'lightbdd-template' / 'Scenarios.cs').read_text(encoding='utf-8')

COMMON_SCOPE_BODY = '''    protected override void OnConfigure(LightBddConfiguration configuration)
    {
        configuration.StepTypeConfiguration().UpdatePredefinedStepTypes("given", "when", "then", "and", "but", "setup", "arrange");
        configuration.CreateStandardReportsWithDiagrams(new ReportConfigurationOptions { SpecificationsTitle = "Rev5 probe", SeparateSetup = true });
    }

    protected override void OnTearDown()
    {
        Skeleton.Write("NAME");
    }
'''

SUITES = {
    'rev5-lightbdd-x2': dict(
        name='rev5-lightbdd-x2',
        usings='using Kronikol.LightBDD.xUnit2;\nusing LightBDD.XUnit2;',
        assembly='[assembly: Rev5.LightBdd.ProbeScope]\n[assembly: ClassCollectionBehavior(AllowTestParallelization = false)]\n[assembly: TrackSteps]',
        scope='internal class ProbeScopeAttribute : LightBddScopeAttribute\n{\n' + COMMON_SCOPE_BODY + '}\n\npublic partial class Scenarios : FeatureFixture { }',
        csproj='''<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Kronikol.LightBDD.xUnit2" Version="$(KronikolVersion)" />
    <PackageReference Include="Kronikol.StepTracking" Version="$(KronikolVersion)" PrivateAssets="all" />
    <PackageReference Include="LightBDD.XUnit2" Version="3.12.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.3.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <Compile Include="..\\shared\\*.cs" />
  </ItemGroup>
</Project>
'''),
    'rev5-lightbdd-x3': dict(
        name='rev5-lightbdd-x3',
        usings='using Kronikol.LightBDD.xUnit3;\nusing LightBDD.XUnit3;\nusing Xunit.v3;',
        assembly='[assembly: TestPipelineStartup(typeof(Rev5.LightBdd.ProbeScope))]\n[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]\n[assembly: TrackSteps]',
        scope='public class ProbeScope : LightBddScope\n{\n' + COMMON_SCOPE_BODY + '}\n\npublic partial class Scenarios : FeatureFixture { }',
        csproj='''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Kronikol.LightBDD.xUnit3" Version="$(KronikolVersion)" />
    <PackageReference Include="Kronikol.StepTracking" Version="$(KronikolVersion)" PrivateAssets="all" />
    <PackageReference Include="LightBDD.XUnit3" Version="3.12.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.3.0" />
    <PackageReference Include="xunit.v3" Version="3.2.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <Compile Include="..\\shared\\*.cs" />
  </ItemGroup>
</Project>
'''),
    'rev5-lightbdd-tunit': dict(
        name='rev5-lightbdd-tunit',
        usings='using Kronikol.LightBDD.TUnit;\nusing LightBDD.TUnit;',
        assembly='[assembly: Rev5.LightBdd.ProbeScope]\n[assembly: TrackSteps]',
        scope='public class ProbeScopeAttribute : LightBddScopeAttribute\n{\n' + COMMON_SCOPE_BODY + '}\n\n[NotInParallel]\npublic partial class Scenarios : FeatureFixture { }',
        csproj='''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Kronikol.LightBDD.TUnit" Version="$(KronikolVersion)" />
    <PackageReference Include="Kronikol.StepTracking" Version="$(KronikolVersion)" PrivateAssets="all" />
    <PackageReference Include="LightBDD.TUnit" Version="3.12.1" />
  </ItemGroup>
  <ItemGroup>
    <Compile Include="..\\shared\\*.cs" />
  </ItemGroup>
</Project>
'''),
}

LEGACY = dict(SUITES['rev5-lightbdd-x2'])
LEGACY['name'] = 'rev5-lightbdd-legacy'
LEGACY['scope'] = LEGACY['scope'].replace('configuration.CreateStandardReportsWithDiagrams(', '''#pragma warning disable CS0618
        configuration.ReportWritersConfiguration().CreateStandardReportsWithDiagrams(''').replace('SeparateSetup = true });', '''SeparateSetup = true });
#pragma warning restore CS0618''')
SUITES['rev5-lightbdd-legacy'] = LEGACY

for folder, s in SUITES.items():
    d = P / folder
    d.mkdir(exist_ok=True)
    text = source.replace('//USINGS', s['usings']).replace('//ASSEMBLY', s['assembly']).replace('//SCOPE', s['scope'].replace('NAME', s['name']))
    (d / 'Scenarios.cs').write_text(text, encoding='utf-8', newline='\n')
    (d / f'Probe.{folder}.csproj').write_text(s['csproj'], encoding='utf-8', newline='\n')
    print('stamped', folder)
