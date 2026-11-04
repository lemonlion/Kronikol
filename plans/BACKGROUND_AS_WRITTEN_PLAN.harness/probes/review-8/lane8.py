"""Moves a copy of the R8 suite to another Reqnroll adapter, from inside it: lane8.py <xunit3|tunit>."""
import json, sys, re
lane = sys.argv[1]
csproj = open("R8.Suite.csproj", encoding="utf-8").read()
config = json.load(open("reqnroll.json", encoding="utf-8"))
def drop(text, start):
    i = text.index(start)
    end_line = text.index("\n", i)
    j = text.index("</PackageReference>", i) + len("</PackageReference>") if "/>" not in text[i:end_line] else end_line
    return text[:i] + text[j:]
adapter = {"xunit3": "Kronikol.ReqNRoll.xUnit3", "tunit": "Kronikol.ReqNRoll.TUnit"}[lane]
csproj = csproj.replace('Include="Kronikol.ReqNRoll.xUnit2"', 'Include="%s"' % adapter)
if lane == "xunit3":
    csproj = csproj.replace('<PackageReference Include="Reqnroll.xUnit" Version="$(ReqnrollVersion)" />', '<PackageReference Include="Reqnroll.xunit.v3" Version="$(ReqnrollVersion)" />')
    csproj = csproj.replace('<PackageReference Include="xunit.assert" Version="2.9.3" />', '<PackageReference Include="xunit.v3" Version="3.2.2" />')
else:
    csproj = csproj.replace('<PackageReference Include="Reqnroll.xUnit" Version="$(ReqnrollVersion)" />', '<PackageReference Include="Reqnroll.TUnit" Version="$(ReqnrollVersion)" /><PackageReference Include="TUnit" Version="1.53.0" />')
    csproj = drop(csproj, '<PackageReference Include="Microsoft.NET.Test.Sdk"')
    csproj = drop(csproj, '<PackageReference Include="xunit.assert"')
    csproj = drop(csproj, '<PackageReference Include="xunit.runner.visualstudio"')
    info = open("AssemblyInfo.cs", encoding="utf-8").read()
    open("AssemblyInfo.cs", "w", encoding="utf-8").write(re.sub(r"\[assembly: Xunit\.[^\n]*\n", "", info))
csproj = csproj.replace("<IsTestProject>true</IsTestProject>", "<IsTestProject>true</IsTestProject>\n    <OutputType>Exe</OutputType>")
config["bindingAssemblies"] = [{"assembly": adapter}]
open("R8.Suite.csproj", "w", encoding="utf-8").write(csproj)
json.dump(config, open("reqnroll.json", "w", encoding="utf-8"), indent=2)
