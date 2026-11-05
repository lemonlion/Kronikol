#!/usr/bin/env bash
# What coverlet makes of a woven assembly, as a project that collects coverage sees it: the same steps on the published weaver and
# on the prototype, line by line, in each configuration, as a project collecting coverage in Release is as common as one in Debug.
# Coverlet instruments the woven IL and reads the woven symbols. Writes results/coverage.txt.
#   WORK=<a folder of your own, holding feed/ with the prototype's package> run-coverage.sh
set -u

W="${WORK:?set WORK to a folder of your own, holding feed/ with the package of the prototype}"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
out="$here/../results/coverage.txt"
scratch="$W/coverage"
mkdir -p "$scratch"
: > "$out"

for configuration in Debug Release; do
echo "################### $configuration" >> "$out"
for pair in "published 5.28.2" "prototype 5.28.99-proto226"; do
    set -- $pair
    label=$1; version=$2
    p="$scratch/$label"
    rm -rf "${p:?}"; mkdir -p "$p"
    cp "$here/xunit-probe.csproj" "$here/Directory.Build.props" "$here/Directory.Build.targets" "$p/"
    sed -e 's/\r$//' "$here/nuget.config" | sed "s#<clear />#<clear />\n    <add key=\"local\" value=\"$(cygpath -m "$W/feed")\" />#" > "$p/nuget.config"
    sed -i 's#<PackageReference Include="Microsoft.NET.Test.Sdk"#<PackageReference Include="coverlet.collector" Version="10.0.0" PrivateAssets="all" />\n    <PackageReference Include="Microsoft.NET.Test.Sdk"#' "$p/xunit-probe.csproj"
    cat > "$p/Facts.cs" <<'EOF'
using Kronikol.Tracking;
using Xunit;

[assembly: TrackSteps]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

public class Order : Kronikol.xUnit3.DiagrammedComponentTest
{
    [Fact] public void A_void_step() => new Driver().AVoid(true);
    [Fact] public async Task A_task_step() => await new Driver().ATask(true);
    [Fact] public async Task A_plain_task_step() => await new Driver().APlainTask(false);
    [Fact] public async Task A_task_int_step() => Assert.Equal(2, await new Driver().ATaskInt(true));
    [Fact] public void A_bypassed_void_step() => new Driver().ABypassedVoid();
    [Fact] public async Task A_bypassed_task_step() => await new Driver().ABypassedTask();
    [Fact] public void A_throwing_step() => Assert.Throws<InvalidOperationException>(() => new Driver().AThrow());
}

public class Driver
{
    public bool Off => true;

    [Step]
    public void AVoid(bool b)
    {
        if (b)
        {
            Console.Write("");
        }
        else
        {
            Console.Write(" ");
        }
    }

    [Step]
    public async Task ATask(bool b)
    {
        await Task.Yield();
        if (b)
            Console.Write("");
    }

    [Step]
    public Task APlainTask(bool b)
    {
        if (b)
            return Task.Delay(1);
        return Task.CompletedTask;
    }

    [Step]
    public async Task<int> ATaskInt(bool b)
    {
        await Task.Yield();
        return b ? 2 : 3;
    }

    [Step(SkipIf = nameof(Off))]
    public void ABypassedVoid()
    {
        Console.Write("");
    }

    [Step(SkipIf = nameof(Off))]
    public async Task ABypassedTask()
    {
        await Task.Yield();
    }

    [Step]
    public void AThrow()
    {
        throw new InvalidOperationException("x");
    }
}
EOF
    echo "=================== $label" >> "$out"
    (cd "$p" && dotnet test xunit-probe.csproj -c "$configuration" -nologo -tl:off -p:StepTrackingVersion="$version" --collect:"XPlat Code Coverage" -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.IncludeTestAssembly=true 2>&1 \
        | grep -E "Passed!|Failed!|error |warning KRONIKOL" | sed -E 's/, Duration: [^-]*-/ -/' | cut -c1-160 | head -6) >> "$out"
    find "$p" -name coverage.cobertura.xml | head -1 | xargs -I{} cp {} "$scratch/$label.xml"
done
python - "$(cygpath -m "$scratch")" >> "$out" <<'PY'
import sys
import xml.etree.ElementTree as E

work = sys.argv[1]


def read(name):
    root = E.parse(f"{work}/{name}.xml").getroot()
    methods = {}
    for c in root.iter('class'):
        for m in c.iter('method'):
            lines = [(l.get('number'), l.get('hits'), l.get('branch'), l.get('condition-coverage')) for l in m.iter('line')]
            methods[c.get('name') + '.' + m.get('name')] = lines
    return root, methods


a, am = read('published')
b, bm = read('prototype')
for label, root in (('published', a), ('prototype', b)):
    print(f"{label}: lines {root.get('lines-covered')} of {root.get('lines-valid')}, branches {root.get('branches-covered')} of {root.get('branches-valid')}")
differ = [k for k in sorted(set(am) | set(bm)) if am.get(k) != bm.get(k)]
print(f"methods {len(am)} and {len(bm)}; methods whose lines, hits or branches differ: {len(differ)}")
turned = [(k, x[0]) for k in differ for x, y in zip(am.get(k, []), bm.get(k, [])) if (x[1] == '0') != (y[1] == '0')]
print(f"lines that change between covered and not: {len(turned)}")
for k in differ:
    print('DIFF', k, am.get(k), bm.get(k))
PY
done
rm -rf "${scratch:?}"
tail -3 "$out"
