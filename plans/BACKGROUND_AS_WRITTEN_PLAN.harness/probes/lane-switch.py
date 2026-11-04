"""Moves a copy of the shape suite to another Reqnroll adapter or generator setting, from inside it: lane-switch.py <xunit3|tunit|norowtests>."""
import json, sys
lane = sys.argv[1]
csproj = open("Shapes.Suite.csproj", encoding="utf-8").read()
config = json.load(open("reqnroll.json", encoding="utf-8"))
def drop_item(text, start):
    i = text.index(start)
    j = text.index("</PackageReference>", i) + len("</PackageReference>") if "/>" not in text[i:text.index("\n", i)] else text.index("\n", i)
    return text[:i] + text[j:]
if lane == "xunit3":
    csproj = csproj.replace('Include="Kronikol.ReqNRoll.xUnit2"', 'Include="Kronikol.ReqNRoll.xUnit3"')
    csproj = csproj.replace('<PackageReference Include="Reqnroll.xUnit" Version="$(ReqnrollVersion)" />', '<PackageReference Include="Reqnroll.xunit.v3" Version="$(ReqnrollVersion)" />')
    csproj = csproj.replace('<PackageReference Include="xunit.assert" Version="2.9.3" />', '<PackageReference Include="xunit.v3" Version="3.2.2" />')
    csproj = csproj.replace("<IsTestProject>true</IsTestProject>", "<IsTestProject>true</IsTestProject>\n    <OutputType>Exe</OutputType>")
    config["bindingAssemblies"] = [{"assembly": "Kronikol.ReqNRoll.xUnit3"}]
elif lane == "tunit":
    csproj = csproj.replace('Include="Kronikol.ReqNRoll.xUnit2"', 'Include="Kronikol.ReqNRoll.TUnit"')
    csproj = csproj.replace('<PackageReference Include="Reqnroll.xUnit" Version="$(ReqnrollVersion)" />', '<PackageReference Include="Reqnroll.TUnit" Version="$(ReqnrollVersion)" />\n    <PackageReference Include="TUnit" Version="1.53.0" />')
    csproj = drop_item(csproj, '<PackageReference Include="Microsoft.NET.Test.Sdk"')
    csproj = drop_item(csproj, '<PackageReference Include="xunit.assert"')
    csproj = drop_item(csproj, '<PackageReference Include="xunit.runner.visualstudio"')
    csproj = csproj.replace("<IsTestProject>true</IsTestProject>", "<IsTestProject>true</IsTestProject>\n    <OutputType>Exe</OutputType>")
    config["bindingAssemblies"] = [{"assembly": "Kronikol.ReqNRoll.TUnit"}]
elif lane == "norowtests":
    config["generator"] = {"allowRowTests": False}
open("Shapes.Suite.csproj", "w", encoding="utf-8").write(csproj)
json.dump(config, open("reqnroll.json", "w", encoding="utf-8"), indent=2)
