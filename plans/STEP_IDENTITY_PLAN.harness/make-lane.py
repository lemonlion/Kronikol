#!/usr/bin/env python
# Makes a copy of repro/ for another Reqnroll lane, outside the repo.
# usage: make-lane.py <lane: xunit3|tunit|floor|xunit2> <build: pub|proto> <target-dir> <kronikol-version> [<prototype-worktree>]
import pathlib, shutil, sys

lane, build, target, version = sys.argv[1:5]
proto = sys.argv[5] if len(sys.argv) > 5 else ""
here = pathlib.Path(__file__).parent
target = pathlib.Path(target)
shutil.copytree(here / "repro", target, ignore=shutil.ignore_patterns("bin", "obj", "*.feature.cs", "HookSteps.Repro.csproj"))

adapter = {"xunit3": "Kronikol.ReqNRoll.xUnit3", "tunit": "Kronikol.ReqNRoll.TUnit", "floor": "Kronikol.ReqNRoll.xUnit2", "xunit2": "Kronikol.ReqNRoll.xUnit2"}[lane]
reqnroll = "3.0.1" if lane == "floor" else "3.3.4"
reference = (f'<ProjectReference Include="{proto}/src/{adapter}/{adapter}.csproj" />' if build == "proto"
             else f'<PackageReference Include="{adapter}" Version="{version}" />')
runner = {
    "xunit3": """<PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.3.0" />
    <PackageReference Include="Reqnroll.xunit.v3" Version="3.3.4" />
    <PackageReference Include="xunit.v3" Version="3.2.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />""",
    "tunit": """<PackageReference Include="Reqnroll.TUnit" Version="3.3.4" />
    <PackageReference Include="TUnit" Version="1.53.0" />""",
}.get(lane, f"""<PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.3.0" />
    <PackageReference Include="Reqnroll.xUnit" Version="{reqnroll}" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />""")
exe = "<OutputType>Exe</OutputType>" if lane in ("xunit3", "tunit") else ""
(target / "HookSteps.Repro.csproj").write_text(f"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <GeneratePackageOnBuild>false</GeneratePackageOnBuild>
    {exe}
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Reqnroll" Version="{reqnroll}" />
    {runner}
    {reference}
    <PackageReference Include="Kronikol.StepTracking" Version="{version}" />
  </ItemGroup>
</Project>
""", encoding="utf-8")
(target / "reqnroll.json").write_text('{ "bindingAssemblies": [ { "assembly": "' + adapter + '" } ] }', encoding="utf-8")
print(target)
