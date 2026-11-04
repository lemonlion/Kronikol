#!/usr/bin/env bash
# What classifying costs a build: a project with thousands of [Step] methods, whose return and parameter types make the new weaver
# resolve types the old one never looked at, some of them in a project it references. Prints what the task says it took.
#   run-weave-time.sh <label> <Kronikol.StepTracking version> [local feed] [methods, default 3000]
set -u

label="$1"
version="$2"
feed="${3:-}"
methods="${4:-3000}"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
results="$here/../results"
work="$(cygpath -u "$TEMP" 2>/dev/null || echo "${TMPDIR:-/tmp}")/kronikol-226-consumer/weave-time/$label"
rm -rf "${work:?}"
mkdir -p "$results" "$work/Dep" "$work/App"

echo '<Project />' > "$work/Directory.Build.props"
echo '<Project />' > "$work/Directory.Build.targets"
sed -e 's/\r$//' "$here/../consumer/nuget.config" > "$work/nuget.config"
if [ -n "$feed" ]; then
    sed -i "s#<clear />#<clear />\n    <add key=\"local\" value=\"$(cygpath -m "$feed" 2>/dev/null || echo "$feed")\" />#" "$work/nuget.config"
fi

cat > "$work/Dep/Dep.csproj" <<'XML'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <UseArtifactsOutput>false</UseArtifactsOutput>
  </PropertyGroup>
</Project>
XML
echo 'public class Customer { public int V; } public struct Money { public decimal A; } public class Base { public bool Off => true; }' > "$work/Dep/Types.cs"

cat > "$work/App/App.csproj" <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <UseArtifactsOutput>false</UseArtifactsOutput>
    <NoWarn>\$(NoWarn);CS1998</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../Dep/Dep.csproj" />
    <PackageReference Include="Kronikol" Version="5.28.2" />
    <PackageReference Include="Kronikol.StepTracking" Version="$version" PrivateAssets="all" />
  </ItemGroup>
</Project>
XML

python - "$work/App/Steps.cs" "$methods" <<'PY'
import sys
path, count = sys.argv[1], int(sys.argv[2])
shapes = [
    "public void M{0}(int a, string b) {{ }}",
    "public async Task M{0}(Customer c) {{ await Task.Yield(); }}",
    "public async Task<int> M{0}(Money m) {{ await Task.Yield(); return 1; }}",
    "public int M{0}(ref int a) => a;",
    "public Customer M{0}(Customer c) => c;",
    "public Money M{0}() => default;",
    "public System.Collections.Generic.List<string> M{0}(System.Span<int> s) => null;",
    "public async ValueTask<Customer> M{0}() {{ await Task.Yield(); return null; }}",
]
with open(path, "w") as f:
    f.write("using System.Threading.Tasks;\nusing Kronikol.Tracking;\n[assembly: TrackSteps]\npublic class Steps : Base\n{\n")
    for i in range(count):
        f.write("    [Step] " + shapes[i % len(shapes)].format(i) + "\n")
    f.write("}\n")
PY

{
    echo "$label: Kronikol.StepTracking $version, $methods step methods"
    for i in 1 2 3; do
        rm -rf "$work/App/bin" "$work/App/obj"
        (cd "$work/App" && dotnet build App.csproj -c Debug -nologo -v:n -tl:off -m:1 2>&1 | grep -E "StepTracking: Instrumented| error " | sed -E 's/^ +//' | cut -c1-120)
    done
} | tee "$results/weave-time-$label.txt"
rm -rf "${work:?}"
