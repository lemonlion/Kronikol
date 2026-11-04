"""Writes the project files of the review's probes, which the first version of this harness left out.

Usage: gen-projects.py <probes dir> <copy>...

Each copy is the name of a source copy of the repo two levels above a project, with its weaver built in Debug. For each
one this writes <probes dir>/<suite>-<copy>/<suite>-<copy>.csproj, and it copies each suite's sources to <probes dir>/<suite>-src.
"""
import pathlib
import shutil
import sys

here = pathlib.Path(__file__).parent

HEAD = r"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
{props}
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <LangVersion>preview</LangVersion>
    <IsPackable>false</IsPackable>
    <TrackStepsEnabled>true</TrackStepsEnabled>
    <KRoot>$(MSBuildProjectDirectory)\..\..\{copy}</KRoot>
  </PropertyGroup>
  <UsingTask TaskName="Kronikol.StepTracking.WeaveStepsTask" AssemblyFile="$(KRoot)\src\Kronikol.StepTracking\bin\Debug\netstandard2.0\Kronikol.StepTracking.dll" />
  <ItemGroup>
{items}
  </ItemGroup>
  <Import Project="$(KRoot)\src\Kronikol.StepTracking\build\Kronikol.StepTracking.targets" />
</Project>
"""

EXE = "    <OutputType>Exe</OutputType>\n"
NO_DEFAULT = "    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>"
TEST = "    <IsTestProject>true</IsTestProject>\n"

SUITES = {
    "plain": (
        EXE + '    <AssemblyName>Kronikol.Tests</AssemblyName>\n    <RootNamespace>Probe</RootNamespace>\n' + NO_DEFAULT,
        r"""    <Compile Include="..\plain-src\*.cs" />
    <ProjectReference Include="$(KRoot)\src\Kronikol\Kronikol.csproj" />""",
    ),
    "bddfy": (
        EXE + "    <RootNamespace>ProbeBddfy</RootNamespace>\n" + TEST + NO_DEFAULT,
        r"""    <Compile Include="..\bddfy-src\*.cs" />
    <ProjectReference Include="$(KRoot)\src\Kronikol.BDDfy.xUnit3\Kronikol.BDDfy.xUnit3.csproj" />
    <PackageReference Include="TestStack.BDDfy" Version="8.0.1.3" />
    <PackageReference Include="xunit.v3" Version="3.2.2" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.3.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />""",
    ),
    "rq": (
        # Reqnroll writes the assembly's name into a class name, so it takes no hyphen.
        "    <AssemblyName>ProbeRq</AssemblyName>\n    <RootNamespace>Probe.Rq</RootNamespace>\n" + TEST.rstrip("\n"),
        r"""    <Compile Include="..\rq-src\*.cs" />
    <ReqnrollFeatureFiles Include="..\rq-src\Features\*.feature" />
    <None Include="reqnroll.json" CopyToOutputDirectory="Always" />
    <ProjectReference Include="$(KRoot)\src\Kronikol.ReqNRoll.xUnit2\Kronikol.ReqNRoll.xUnit2.csproj" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.3.0" />
    <PackageReference Include="Reqnroll" Version="3.3.4" />
    <PackageReference Include="Reqnroll.xUnit" Version="3.3.4" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />""",
    ),
    "lb": (
        EXE + TEST + NO_DEFAULT,
        r"""    <Compile Include="..\lb-src\*.cs" />
    <ProjectReference Include="$(KRoot)\src\Kronikol.LightBDD.xUnit3\Kronikol.LightBDD.xUnit3.csproj" />
    <PackageReference Include="LightBDD.XUnit3" Version="3.12.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.3.0" />
    <PackageReference Include="xunit.v3" Version="3.2.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />""",
    ),
}

REQNROLL_JSON = """{
  "$schema": "https://schemas.reqnroll.net/reqnroll-config-latest.json",
  "bindingAssemblies": [ { "assembly": "Kronikol.ReqNRoll.xUnit2" }, { "assembly": "Kronikol.ReqNRoll.Core" } ]
}
"""

probes = pathlib.Path(sys.argv[1])
probes.mkdir(parents=True, exist_ok=True)
(probes / "Directory.Build.props").write_text("<Project></Project>\n")

for suite, (props, items) in SUITES.items():
    source = probes / f"{suite}-src"
    if not source.exists():
        shutil.copytree(here / f"{suite}-src", source)
    for copy in sys.argv[2:]:
        project = probes / f"{suite}-{copy}"
        project.mkdir(exist_ok=True)
        (project / f"{suite}-{copy}.csproj").write_text(HEAD.format(props=props, copy=copy, items=items))
        if suite == "rq":
            (project / "reqnroll.json").write_text(REQNROLL_JSON)
print("projects written")
